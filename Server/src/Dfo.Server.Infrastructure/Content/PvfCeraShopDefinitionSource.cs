using System.Collections.Immutable;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Content;
using Section = Dfo.Server.Infrastructure.Content.PvfNpcShopDefinitionSource.Section;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfCeraShopDefinitionSource(PvfArchiveProvider provider) : ICeraShopDefinitionSource
{
    private const string CatalogPath = "etc/cerashop.etc";
    private readonly Lock gate = new();
    private readonly Dictionary<int, CeraShopProductDefinition?> products = [];
    private readonly Dictionary<int, CeraShopItemDefinition?> items = [];
    private readonly Dictionary<int, (string Category, PvfToken[] Values, bool Malformed)> rows = [];
    private readonly HashSet<int> serviceItems = [], serviceProducts = [], restricted = [], onlyCera = [], onlyPoint = [];
    private readonly List<CeraShopDiagnostic> contentDiagnostics = [];
    private Dictionary<int, string>? equipment, stackables, creatures;
    private IReadOnlyList<Section>? avatarAbilityCases;
    private bool loaded;
    private Dictionary<int, (int Type, TimeSpan Duration)> premiumTerms = [];

    public ValueTask<CeraShopProductDefinition?> GetProductAsync(int productId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Load();
            if (!products.TryGetValue(productId, out var result))
                products[productId] = result = ReadProduct(productId);
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<CeraShopItemDefinition?> GetItemAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Load();
            return ValueTask.FromResult(Item(itemId));
        }
    }

    private CeraShopItemDefinition? Item(int id)
    {
        if (!items.TryGetValue(id, out var result)) items[id] = result = ReadItem(id);
        return result;
    }

    private void Load()
    {
        if (loaded) return;
        var archive = provider.Archive;
        foreach (var path in new[] { CatalogPath, "etc/premiumlist_new.etc", "etc/accountcargo.etc",
                     "equipment/equipment.lst", "stackable/stackable.lst", "creature/creature.lst" })
            if (archive.FindFile(path) < 0 || (path.EndsWith(".etc", StringComparison.Ordinal) && Sections(path).Count == 0))
                contentDiagnostics.Add(new("MissingContentFile", path, "Required catalog or service classification content is missing."));
        equipment = Paths(archive, "equipment/equipment.lst");
        stackables = Paths(archive, "stackable/stackable.lst");
        creatures = Paths(archive, "creature/creature.lst");
        foreach (var section in Sections(CatalogPath))
        {
            var stride = section.Name switch
            {
                "[avatar]" => 6,
                "[coin]" or "[item]" or "[material]" or "[recovery item]" or "[recoveryitem]"
                    or "[premium]" or "[creature]" or "[community item]"
                    or "[selectable character premium]" or "[charac premium package]" => 9,
                "[package]" or "[regular package]" or "[community package]" => 11,
                "[visual]" => 8,
                _ => 0,
            };
            if (stride > 0)
            {
                for (var i = 0; i + stride <= section.Values.Count; i += stride)
                {
                    var row = section.Values.Skip(i).Take(stride).ToArray();
                    if (row[0].Kind != PvfTokenKind.Int32) continue;
                    var malformed = section.Values.Count % stride != 0 || rows.ContainsKey(row[0].Value);
                    rows[row[0].Value] = (section.Name, row, malformed);
                    if (ServiceCategory(section.Name) && row[1].Kind == PvfTokenKind.Int32) serviceItems.Add(row[1].Value);
                }
            }
            if (section.Name == "[buy only cera]") AddIntegers(onlyCera, section);
            if (section.Name == "[buy only cera point]") AddIntegers(onlyPoint, section);
            if (section.Name == "[not stackable buy]") AddIntegers(restricted, section);
            if (section.Name == "[buy restrict item]") AddColumn(restricted, section, 4, 1);
            if (section.Name == "[common character premium detail]") AddColumn(serviceItems, section, 8, 2);
        }
        foreach (var section in Sections("etc/premiumlist_new.etc"))
            if (section.Name is "[target item]" or "[item]") AddIntegers(serviceItems, section);
        foreach (var section in Sections("etc/accountcargo.etc"))
            if (section.Name == "[upgrade info]") AddColumn(serviceProducts, section, 6, 5);
        premiumTerms = PvfPackageOpeningDefinitionSource.ParsePremiums(Sections("etc/premiumlist_new.etc"));
        loaded = true;
    }

    private CeraShopProductDefinition? ReadProduct(int id)
    {
        if (!rows.TryGetValue(id, out var row)) return null;
        var itemId = row.Values[1].Kind == PvfTokenKind.Int32 ? row.Values[1].Value : 0;
        var item = Item(itemId);
        var result = ParseProduct(row.Category, row.Values, item?.Avatar);
        var diagnostics = result.Diagnostics.ToBuilder();
        diagnostics.AddRange(contentDiagnostics);
        if (row.Malformed) diagnostics.Add(new("MalformedCatalog", CatalogPath, "Duplicate product or incomplete category row."));
        if (restricted.Contains(id)) diagnostics.Add(new("PurchaseRestriction", CatalogPath, "Unimplemented catalog purchase restriction."));
        if (item is null) diagnostics.Add(new("MissingItem", CatalogPath, $"Item {itemId} is not readable."));
        return result with
        {
            Premium = row.Category == "[premium]" && premiumTerms.TryGetValue(itemId, out var premium)
                && premium.Type is 22 or 27 or 84 or 97 ? new(premium.Type, premium.Duration) : null,
            IsService = result.IsService || serviceProducts.Contains(id) || item?.Kind == CeraShopItemKind.Service,
            BuyOnlyCera = onlyCera.Contains(itemId), BuyOnlyCeraPoint = onlyPoint.Contains(itemId),
            Diagnostics = diagnostics.ToImmutable(),
        };
    }

    // Kept typed at the boundary: a string offset must never become a numeric price/count.
    internal static CeraShopProductDefinition ParseProduct(string category, IReadOnlyList<PvfToken> values,
        CeraShopAvatarDeclaration? avatar = null)
    {
        var errors = ImmutableArray.CreateBuilder<CeraShopDiagnostic>();
        void Error(string code, string detail) => errors.Add(new(code, CatalogPath + category, detail));
        var package = category is "[package]" or "[regular package]" or "[community package]";
        var isAvatar = category == "[avatar]";
        var service = ServiceCategory(category);
        var expected = package ? 11 : isAvatar ? 6 : category == "[visual]" ? 8 : 9;
        var nameColumn = package ? 5 : isAvatar ? -1 : category == "[charac premium package]" ? 3 : 6;
        if (values.Count != expected || values.Where((t, i) => i != nameColumn).Any(t => t.Kind != PvfTokenKind.Int32)
            || (nameColumn >= 0 && values.Count > nameColumn && values[nameColumn].Kind != PvfTokenKind.QuotedString))
            Error("MalformedCatalog", "Unexpected row width or token kind.");
        int N(int index) => index < values.Count && values[index].Kind == PvfTokenKind.Int32 ? values[index].Value : 0;
        var count = package || isAvatar || service ? 1 : N(2);
        CeraShopPrice? price;
        if (isAvatar && N(5) < 0)
        {
            var tier = N(2) - 1;
            price = avatar is not null && tier >= 0 && tier < avatar.PriceOptions.Length ? avatar.PriceOptions[tier] : null;
            if (price is null) Error("UnknownAvatarPrice", "Negative catalog price requires a declared avatar tier.");
        }
        else price = new(service ? 0 : N(3), service || package ? 0 : N(4),
            category == "[charac premium package]" ? N(2) : package ? N(4) : N(5), 0,
            values.Where(t => t.Kind == PvfTokenKind.Int32).Select(t => t.Value).ToImmutableArray());
        if (N(0) <= 0 || N(1) <= 0 || count <= 0) Error("InvalidIdentityOrCount", "Product, item and delivery count must be positive.");
        if (price is not null && (price.Cera < 0 || price.Gold < 0 || price.OtherPayment != 0))
            Error("UnsupportedPayment", "Invalid price or nonzero unknown payment column.");
        // Avatar rows do not share the standard row's gold/payment columns. The current archive
        // uses a 0/1 catalog flag here; the selected equipment tier supplies the complete price.
        if (isAvatar && N(5) < 0 && (N(3) is not (0 or 1) || N(4) != 0))
            Error("UnsupportedAvatarCatalogFlags", "Unknown avatar catalog flags.");
        if (price is not null && price.Period < 0) Error("InvalidPeriod", "Duration cannot be negative.");
        if (isAvatar && price is not null && avatar is not null && price.RawFields.Length == 7
            && (price.RawFields[4] != 0 || price.RawFields[5] != 0 || price.RawFields[6] != 0))
            Error("UnsupportedAvatarTier", "Unimplemented tier flags or sockets.");
        if (package && (N(2) != 0 || N(3) != 0 || N(6) != 0 || N(7) != 0 || N(8) != -1 || N(9) != -1))
            Error("UnknownPackageConditions", "Nondefault package fields need interpretation.");
        if (!package && !isAvatar && !service && (N(7) != 0 || N(8) != 0))
            Error("UnknownCatalogConditions", "Nonzero catalog tail fields need interpretation.");
        if (category == "[selectable character premium]" &&
            (N(2) is < 0 or > 7 || N(3) is not (7 or 30) || N(4) != 0 || N(7) != 0 || N(8) != -1))
            Error("UnsupportedService", "Unknown service slot, duration or catalog flags.");
        return new(N(0), N(1), count, category, price, service, false, false, errors.ToImmutable(), CatalogPath + category,
            category == "[selectable character premium]" ? N(2) : null,
            category == "[selectable character premium]" ? N(3) : 0);
    }

    private CeraShopItemDefinition? ReadItem(int id)
    {
        var isEquipment = equipment!.TryGetValue(id, out var relative);
        if (!isEquipment && !stackables!.TryGetValue(id, out relative)) return null;
        var path = (isEquipment ? "equipment/" : "stackable/") + relative;
        var sections = Sections(path);
        if (sections.Count == 0) return null;
        var errors = ImmutableArray.CreateBuilder<CeraShopDiagnostic>();
        errors.AddRange(contentDiagnostics);
        void Error(string code, string detail) => errors.Add(new(code, path, detail));
        // Nested opening rewards do not describe purchase-time restrictions or creation facts.
        var top = new List<Section>();
        var depth = 0;
        foreach (var section in sections)
        {
            if (section.Name == "[booster info]") { depth++; continue; }
            if (section.Name == "[/booster info]") { if (--depth < 0) Error("MalformedItem", "Unbalanced booster section."); continue; }
            if (depth == 0) top.Add(section);
        }
        if (depth != 0) Error("MalformedItem", "Unclosed booster section.");
        errors.AddRange(ParseInstanceDeclarations(top, path));
        string? Text(string tag) => top.FirstOrDefault(s => s.Name == tag)?.Values.FirstOrDefault() is { IsString: true } t
            ? provider.Archive.ResolveString(t.Value).ToLowerInvariant() : null;
        int? Number(string tag)
        {
            var all = top.Where(s => s.Name == tag).ToArray();
            if (all.Length == 0) return null;
            if (all.Length != 1 || all[0].Values.Count != 1 || all[0].Values[0].Kind != PvfTokenKind.Int32)
            { Error("MalformedItem", tag); return null; }
            return all[0].Values[0].Value;
        }
        var type = Text(isEquipment ? "[equipment type]" : "[stackable type]");
        var kind = type switch
        {
            "[creature]" => CeraShopItemKind.Creature,
            "[artifact red]" or "[artifact blue]" or "[artifact green]" => CeraShopItemKind.CreatureEquipment,
            "[name tag]" => CeraShopItemKind.Service,
            "[cera package]" => CeraShopItemKind.Package,
            _ when type?.EndsWith(" avatar]", StringComparison.Ordinal) == true => CeraShopItemKind.Avatar,
            _ when isEquipment && type is not null => CeraShopItemKind.Equipment,
            _ when !isEquipment && type is not null => CeraShopItemKind.Stackable,
            _ => CeraShopItemKind.Unknown,
        };
        if (serviceItems.Contains(id) || ServicePath(path)) kind = CeraShopItemKind.Service;
        if (kind == CeraShopItemKind.Unknown) Error("UnknownItemType", "Missing item type declaration.");
        var minimum = Number("[restrict buy level cashshop]");
        if (minimum is < 0) Error("MalformedPurchaseLevel", "Negative purchase level.");
        foreach (var section in top)
            if ((section.Name.Contains("buy", StringComparison.Ordinal) || section.Name.Contains("purchase", StringComparison.Ordinal))
                && section.Name != "[restrict buy level cashshop]") Error("PurchaseRestriction", section.Name);
        var durability = Number("[durability]") ?? 0;
        if (durability is < 0 or > ushort.MaxValue) Error("MalformedItem", "Invalid durability.");
        var components = ImmutableArray.CreateBuilder<CeraShopComponent>();
        var selections = ImmutableArray.CreateBuilder<CeraShopSelectionGroup>();
        // These six shop wrappers deliver both guaranteed rewards at purchase time.
        // Other boosters remain inventory items; see protocol/patterns/data/probes/a21-hammer-bundles-20260909.md.
        if (id is >= 10007472 and <= 10007477)
        {
            if (type != "[booster]") Error("MalformedHammerBundle", "Expected a booster shop wrapper.");
            var parsed = ParseHammerBundle(sections, path);
            errors.AddRange(parsed.Diagnostics);
            components.AddRange(parsed.Components);
            kind = CeraShopItemKind.Package;
        }
        else if (kind == CeraShopItemKind.Package)
        {
            foreach (var section in top)
            {
                if (section.Name is "[package data]" or "[package data include usable period]" or "[package data selection]")
                {
                    var parsed = ParseComponents(section.Values, section.Name == "[package data include usable period]" ? 3 : 2, path);
                    errors.AddRange(parsed.Diagnostics);
                    if (section.Name == "[package data selection]") selections.Add(new(parsed.Components));
                    else components.AddRange(parsed.Components);
                }
                else if (section.Name.StartsWith("[package data", StringComparison.Ordinal)) Error("UnknownPackageDeclaration", section.Name);
            }
            if (components.Count == 0 && selections.Count == 0) Error("EmptyPackage", "No declared delivery components.");
        }
        CeraShopAvatarDeclaration? avatar = null;
        if (kind == CeraShopItemKind.Avatar)
        {
            var prices = ImmutableArray.CreateBuilder<CeraShopPrice>();
            var priceTokens = top.FirstOrDefault(s => s.Name == "[avatar type select]")?.Values ?? [];
            for (var i = 0; i < priceTokens.Count;)
            {
                if (i + 7 > priceTokens.Count || priceTokens.Skip(i).Take(7).Any(t => t.Kind != PvfTokenKind.Int32))
                { Error("MalformedAvatarPrice", "Incomplete seven-integer tier."); break; }
                var raw = priceTokens.Skip(i).Take(7).Select(t => t.Value).ToImmutableArray();
                prices.Add(new(raw[1], raw[2], raw[3], raw[0], raw));
                i += 7;
                if (raw[6] < 0 || raw[6] > priceTokens.Count - i || priceTokens.Skip(i).Take(raw[6]).Any(t => t.Kind != PvfTokenKind.QuotedString))
                { Error("MalformedAvatarPrice", "Invalid tier socket list."); break; }
                i += raw[6];
            }
            var abilities = ImmutableArray.CreateBuilder<int>();
            foreach (var ability in top.Where(s => s.Name == "[avatar select ability]"))
            {
                var parsed = ParseAvatarAbilities(ability.Values, provider.Archive.ResolveString, path);
                abilities.AddRange(parsed.Abilities);
                errors.AddRange(parsed.Diagnostics);
            }
            var caseIndex = Number("[ability case index]");
            if (caseIndex is not null)
            {
                avatarAbilityCases ??= Sections("skill/abilitydatas.dat");
                var parsed = ResolveAvatarAbilityCase(caseIndex.Value, avatarAbilityCases,
                    provider.Archive.ResolveString);
                abilities.AddRange(parsed.Abilities);
                errors.AddRange(parsed.Diagnostics);
            }
            avatar = new(type!, prices.ToImmutable(), abilities.Distinct().ToImmutableArray());
        }
        CeraShopCreatureDeclaration? creature = null;
        if (kind == CeraShopItemKind.Creature)
        {
            var species = Number("[creature species]");
            if (species is null || !creatures!.TryGetValue(species.Value, out var creaturePath)) Error("UnknownCreature", "Missing creature species mapping.");
            else
            {
                var start = Sections("creature/" + creaturePath).Where(s => s.Name == "[start level]").ToArray();
                if (start.Length != 1 || start[0].Values.Count != 1 || start[0].Values[0].Kind != PvfTokenKind.Int32
                    || start[0].Values[0].Value is < 1 or > byte.MaxValue) Error("UnknownCreature", "Invalid start level.");
                else creature = new(species.Value, start[0].Values[0].Value);
            }
        }
        return new(id, path, kind, (ushort)Math.Clamp(durability, 0, ushort.MaxValue), Text("[attach type]") == "[sealing]",
            minimum, components.ToImmutable(), selections.ToImmutable(), avatar, creature, errors.ToImmutable());
    }

    internal static (ImmutableArray<CeraShopComponent> Components, ImmutableArray<CeraShopDiagnostic> Diagnostics)
        ParseHammerBundle(IReadOnlyList<Section> sections, string source)
    {
        (ImmutableArray<CeraShopComponent>, ImmutableArray<CeraShopDiagnostic>) Invalid() =>
            ([], [new("MalformedHammerBundle", source, "Expected two equal guaranteed hammer/box rewards in one closed booster section.")]);
        var starts = sections.Select((section, index) => (section, index))
            .Where(x => x.section.Name == "[booster info]").ToArray();
        if (starts.Length != 1 || sections.Count(s => s.Name == "[/booster info]") != 1) return Invalid();
        var start = starts[0].index;
        string[] expected = ["[booster info]", "[etc]", "[/etc]", "[etc]", "[/etc]", "[/booster info]"];
        if (start + expected.Length > sections.Count) return Invalid();
        var rewards = new Dictionary<int, int>();
        for (var offset = 0; offset < expected.Length; offset++)
        {
            var section = sections[start + offset];
            if (section.Name != expected[offset]) return Invalid();
            if (offset is not (1 or 3))
            {
                if (section.Values.Count != 0) return Invalid();
                continue;
            }
            var values = section.Values;
            if (values.Count != 4 || values.Any(t => t.Kind != PvfTokenKind.Int32)
                || values[0].Value != 1 || values[1].Value is not (10007367 or 10007368)
                || values[2].Value != 1000 || values[3].Value <= 0
                || !rewards.TryAdd(values[1].Value, values[3].Value)) return Invalid();
        }
        if (rewards[10007367] != rewards[10007368]) return Invalid();
        return ([new(10007367, rewards[10007367], 0), new(10007368, rewards[10007368], 0)], []);
    }

    internal static (ImmutableArray<CeraShopComponent> Components, ImmutableArray<CeraShopDiagnostic> Diagnostics)
        ParseComponents(IReadOnlyList<PvfToken> values, int stride, string source)
    {
        var errors = ImmutableArray.CreateBuilder<CeraShopDiagnostic>();
        var result = ImmutableArray.CreateBuilder<CeraShopComponent>();
        if (values.Count == 0 || values.Count % stride != 0 || values.Any(t => t.Kind != PvfTokenKind.Int32))
            errors.Add(new("MalformedPackage", source, "Expected complete integer component tuples."));
        else for (var i = 0; i < values.Count; i += stride)
        {
            var period = stride == 3 ? values[i + 2].Value : 0;
            if (values[i].Value <= 0 || values[i + 1].Value <= 0 || period < 0)
                errors.Add(new("MalformedPackage", source, "Invalid component identity/count/period."));
            result.Add(new(values[i].Value, values[i + 1].Value, period));
        }
        return (result.ToImmutable(), errors.ToImmutable());
    }

    private IReadOnlyList<Section> Sections(string path) => PvfNpcShopDefinitionSource.Sections(provider.Archive, path);

    internal static (ImmutableArray<int> Abilities, ImmutableArray<CeraShopDiagnostic> Diagnostics)
        ResolveAvatarAbilityCase(int id, IReadOnlyList<Section> sections, Func<int, string> text)
    {
        const string source = "skill/abilitydatas.dat";
        var matches = sections.Select((section, index) => (section, index))
            .Where(x => x.section.Name == "[ability case]" && x.section.Values.Count > 0
                && x.section.Values[0].Kind == PvfTokenKind.Int32 && x.section.Values[0].Value == id).ToArray();
        if (id < 0 || matches.Length != 1 || matches[0].index + 1 >= sections.Count
            || sections[matches[0].index + 1].Name != "[/ability case]")
            return ([], [new("UnsupportedAvatarAbilities", source, $"Missing, duplicate or unclosed ability case {id}.")]);
        return ParseAvatarAbilities(matches[0].section.Values.Skip(1).ToArray(), text, source + $" case={id}");
    }

    internal static (ImmutableArray<int> Abilities, ImmutableArray<CeraShopDiagnostic> Diagnostics)
        ParseAvatarAbilities(IReadOnlyList<PvfToken> values, Func<int, string> text, string source)
    {
        var abilities = ImmutableArray.CreateBuilder<int>();
        var seen = new HashSet<int>();
        var offset = 0;
        while (offset < values.Count)
        {
            if (offset + 4 > values.Count || values[offset].Kind != PvfTokenKind.Int32
                || values[offset].Value is < 0 or > byte.MaxValue || !seen.Add(values[offset].Value)
                || values[offset + 1].Kind != PvfTokenKind.QuotedString
                || values[offset + 2].Kind != PvfTokenKind.QuotedString)
                break;
            var skill = text(values[offset + 1].Value) == "[SKILL_LEVEL]";
            // Stat: option, name, operator, amount. Skill: option, SKILL_LEVEL, job, skill id, levels.
            if (skill ? offset + 5 > values.Count || values[offset + 3].Kind != PvfTokenKind.Int32
                    || values[offset + 3].Value < 0 || values[offset + 4].Kind != PvfTokenKind.Int32
                    || values[offset + 4].Value <= 0
                : values[offset + 3].Kind is not (PvfTokenKind.Int32 or PvfTokenKind.Single))
                break;
            abilities.Add(values[offset].Value);
            offset += skill ? 5 : 4;
        }
        // Do not return a valid prefix if a later option cannot be understood.
        return offset == values.Count && abilities.Count > 0 ? (abilities.ToImmutable(), [])
            : ([], [new("UnsupportedAvatarAbilities", source, "Empty, malformed or duplicate avatar option.")]);
    }
    internal static ImmutableArray<CeraShopDiagnostic> ParseInstanceDeclarations(IEnumerable<Section> sections, string source) =>
        sections.Where(s => s.Name is "[expiration date]" or "[usable period]" or "[trade limit]")
            .Where(s => s.Values.Any(t => t.Kind != PvfTokenKind.Int32 || t.Value != 0))
            .Select(s => new CeraShopDiagnostic("UnsupportedInstanceDeclaration", source, s.Name))
            .ToImmutableArray();
    private static bool ServiceCategory(string category) => category is "[visual]" or "[selectable character premium]" or "[charac premium package]";
    private static Dictionary<int, string> Paths(PvfArchive archive, string path) => archive.FindFile(path) < 0 ? []
        : PvfListFile.Read(archive, path).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().RelativePath);
    private static void AddIntegers(HashSet<int> set, Section section)
    { foreach (var value in section.Values) if (value.Kind == PvfTokenKind.Int32) set.Add(value.Value); }
    private static void AddColumn(HashSet<int> set, Section section, int stride, int column)
    { for (var i = column; i < section.Values.Count; i += stride) if (section.Values[i].Kind == PvfTokenKind.Int32) set.Add(section.Values[i].Value); }
    private static bool ServicePath(string path)
    {
        path = path.ToLowerInvariant();
        return path is "stackable/cash/skilltree_plusitem.stk" or "stackable/cash/character_slotkit.stk"
            or "stackable/cash/chn_character_rename.stk"
            || path.StartsWith("stackable/cash/safe_upgradekit", StringComparison.Ordinal)
            || path.StartsWith("stackable/cash/chn_account_cargo/account_cargo", StringComparison.Ordinal)
            || path.StartsWith("stackable/cash/inven_upgradekit", StringComparison.Ordinal)
            || path.StartsWith("stackable/cash/chn_20140819_avatar_inven/", StringComparison.Ordinal);
    }
}
