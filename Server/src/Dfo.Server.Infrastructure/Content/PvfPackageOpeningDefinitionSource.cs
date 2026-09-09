using System.Collections.Immutable;
using System.Globalization;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfPackageOpeningDefinitionSource(PvfArchiveProvider provider,
    ICeraShopDefinitionSource items, IItemPlacementDefinitionSource placements) : IPackageOpeningDefinitionSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, OpeningPackageDefinition?> cache = [];
    private Dictionary<int, string>? paths;
    private Dictionary<int, (int Type, int Days)>? premiums;

    public async ValueTask<OpeningPackageDefinition?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpeningPackageDefinition? result;
        lock (gate)
        {
            if (cache.TryGetValue(itemId, out result)) return result;
            paths ??= PvfListFile.Read(provider.Archive, "stackable/stackable.lst")
                .ToDictionary(row => row.Id, row => "stackable/" + row.RelativePath);
            result = paths.TryGetValue(itemId, out var path) ? Parse(itemId,
                PvfNpcShopDefinitionSource.Sections(provider.Archive, path),
                token => provider.Archive.ResolveString(token.Value)) : null;
        }
        // The same PVF stackable type and CMD can describe a pet capsule or an avatar set.
        // The declared contents, rather than that shared UI entry, determine the business family.
        if (result is { Kind: OpeningPackageKind.AvatarPackage } && !result.FixedRewards.IsEmpty)
        {
            var allKnown = true; var hasAvatar = false;
            foreach (var id in result.FixedRewards.Select(r => r.ItemId).Distinct())
            {
                var reward = await GetRewardAsync(id, cancellationToken).ConfigureAwait(false);
                allKnown &= reward is not null;
                hasAvatar |= reward?.Placement.WorldClass == ItemWorldClass.Avatar;
            }
            if (allKnown && !hasAvatar) result = result with { Kind = OpeningPackageKind.Package };
        }
        lock (gate) { cache.TryAdd(itemId, result); return cache[itemId]; }
    }

    public async ValueTask<OpeningRewardItemDefinition?> GetRewardAsync(int itemId, CancellationToken cancellationToken)
    {
        // The archive's 42 is a revive-coin grant, represented by virtual main slot 1.
        if (itemId is 1 or 42) return new(1, new(0, int.MaxValue, 1, 1), 0, false, [], null, true);
        lock (gate)
        {
            premiums ??= Premiums();
            if (premiums.TryGetValue(itemId, out var premium))
                return new(itemId, new(0, 1, -1, -1), 0, false, [], null,
                    premium.Type is > 0 and <= sbyte.MaxValue && premium.Days > 0, PremiumType: premium.Type, PremiumDays: premium.Days);
        }
        var item = await items.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false);
        var placement = await placements.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null || placement is null) return null;
        placement = item.Kind switch
        {
            CeraShopItemKind.Avatar => placement with { ItemKind = 8, StackLimit = 1, SlotStart = 0, SlotEnd = 209 },
            CeraShopItemKind.Creature => placement with { ItemKind = 5, StackLimit = 1, SlotStart = 0, SlotEnd = 139 },
            CeraShopItemKind.CreatureEquipment => placement with { ItemKind = 6, StackLimit = 1, SlotStart = 140, SlotEnd = 188 },
            _ when placement.ItemKind == 7 => placement with { SlotStart = 189, SlotEnd = 239 },
            _ => placement,
        };
        // Catalog purchase restrictions do not restrict rewards. Instance diagnostics still do.
        var sections = PvfNpcShopDefinitionSource.Sections(provider.Archive, item.Source);
        var periods = sections.Where(s => s.Name == "[usable period]").ToArray();
        var period = periods.Length == 0 ? 0 : periods.Length == 1 && periods[0].Values.Count == 1
            && periods[0].Values[0].Kind == PvfTokenKind.Int32 ? periods[0].Values[0].Value : -1;
        var expirations = sections.Where(s => s.Name == "[expiration date]").ToArray();
        var expiration = 0;
        if (expirations.Length > 0)
        {
            var values = expirations[0].Values;
            if (expirations.Length != 1 || values.Count != 1) expiration = -1;
            else if (values[0].Kind == PvfTokenKind.Int32) expiration = values[0].Value == 0 ? 0 : -1;
            else if (values[0].IsString && DateTime.TryParseExact(provider.Archive.ResolveString(values[0].Value),
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                var seconds = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date)).ToUnixTimeSeconds();
                expiration = seconds is > 0 and <= int.MaxValue ? (int)seconds : -1;
            }
            else expiration = -1;
        }
        var supported = period >= 0 && expiration >= 0 && item.Kind is not (CeraShopItemKind.Unknown or CeraShopItemKind.Service)
            && item.Diagnostics.All(d => d.Code is "PurchaseRestriction" or "MalformedPurchaseLevel"
                || d.Code == "UnsupportedInstanceDeclaration" && d.Detail is "[usable period]" or "[expiration date]");
        return new(itemId, placement, item.Durability, item.Sealed,
            item.Avatar?.SelectableAbilities ?? [], item.Creature?.StartLevel, supported, period, expiration);
    }

    private Dictionary<int, (int Type, int Days)> Premiums()
    {
        var result = new Dictionary<int, (int, int)>();
        var sections = PvfNpcShopDefinitionSource.Sections(provider.Archive, "etc/premiumlist_new.etc");
        var type = 0;
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.Name == "[type]")
                type = section.Values.Count == 1 && section.Values[0].Kind == PvfTokenKind.Int32 ? section.Values[0].Value : 0;
            if (type <= 0 || section.Name != "[item]" || section.Values.Count != 1
                || section.Values[0].Kind != PvfTokenKind.Int32 || i + 1 >= sections.Count) continue;
            var term = sections[i + 1];
            if (term.Name == "[term]" && term.Values.Count == 1 && term.Values[0].Kind == PvfTokenKind.Int32)
                result[section.Values[0].Value] = (type, term.Values[0].Value);
        }
        return result;
    }

    internal static OpeningPackageDefinition Parse(int id, IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections,
        Func<PvfToken, string> text)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        var fixedRewards = ImmutableArray.CreateBuilder<OpeningReward>();
        var pools = ImmutableArray.CreateBuilder<OpeningRewardPool>();
        var typeToken = sections.FirstOrDefault(s => s.Name == "[stackable type]")?.Values.FirstOrDefault();
        var type = typeToken is { IsString: true } token ? text(token).ToLowerInvariant() : "";
        var kind = type switch
        {
            "[random upgradable legacy]" => OpeningPackageKind.MagicBox,
            "[usable cera package]" => OpeningPackageKind.AvatarPackage,
            "[booster selection]" => OpeningPackageKind.Selection,
            "[cera package]" or "[booster]" or "[cera booster]" or "[booster random]" => OpeningPackageKind.Package,
            _ => OpeningPackageKind.Unsupported,
        };
        int[] Numbers(PvfNpcShopDefinitionSource.Section section)
        {
            if (section.Values.Any(t => t.Kind != PvfTokenKind.Int32)) errors.Add("Noninteger " + section.Name);
            return section.Values.Where(t => t.Kind == PvfTokenKind.Int32).Select(t => t.Value).ToArray();
        }
        var material = 0; var materialCount = 0; var sourceCount = 1; var selectionCount = 0;
        var random = false; var booster = false; var selection = false;
        foreach (var section in sections)
        {
            var name = section.Name.ToLowerInvariant();
            if (name == "[randombox]") { random = true; continue; }
            if (name == "[/randombox]") { random = false; continue; }
            if (name == "[booster info]") { booster = true; continue; }
            if (name == "[/booster info]") { booster = false; continue; }
            if (name == "[booster select category]")
            {
                selection = true;
                var category = Numbers(section);
                if (category.Length != 2 || category.Any(value => value < 0)) errors.Add("Invalid selection category");
                continue;
            }
            if (name == "[/booster select category]") { selection = false; continue; }
            if (name.StartsWith("[/", StringComparison.Ordinal)) continue;
            if (name is "[usable count limit]" or "[usable count]" or "[use count limit]") errors.Add("Unimplemented use restriction " + name);
            if (name == "[booster selection num]")
            {
                var ns = Numbers(section);
                if (ns.Length != 1 || ns[0] < 0) errors.Add("Invalid selection count");
                else selectionCount = ns[0];
            }
            if (name == "[need material]" || random && name == "[sealing removal item]")
            {
                var ns = Numbers(section);
                var start = name == "[sealing removal item]" ? 1 : 0;
                if (ns.Length < start + 2 || (ns.Length - start) % 2 != 0
                    || start == 1 && ns[0] * 2 + 1 != ns.Length) { errors.Add("Invalid removal declaration"); continue; }
                for (var i = start; i < ns.Length; i += 2)
                {
                    if (ns[i] <= 0 || ns[i + 1] < 0) errors.Add("Invalid removal count");
                    else if (ns[i] == id) sourceCount = ns[i + 1];
                    // Seria's declaration retains material 201 with count zero: no key is consumed.
                    else if (ns[i + 1] == 0) continue;
                    else if (material == 0) { material = ns[i]; materialCount = ns[i + 1]; }
                    else errors.Add("Multiple required materials");
                }
            }
            if (name is "[package data]" or "[package data include usable period]"
                || selection)
            {
                var ns = Numbers(section);
                if (selection && name is not ("[avatar]" or "[equipment]" or "[etc]" or "[creature]"))
                { errors.Add("Unsupported selection declaration " + name); continue; }
                var stride = selection && name == "[avatar]" ? 4 : name == "[package data include usable period]" ? 3 : 2;
                if (ns.Length == 0 || ns.Length % stride != 0) errors.Add("Invalid fixed reward " + name);
                else for (var i = 0; i < ns.Length; i += stride)
                    // Avatar selection's two trailing fields are UI declaration fields in the reference reader.
                    // Creation uses the equipment's declared ability and the selected option supplied by the client.
                    fixedRewards.Add(new(ns[i], ns[i + 1], PeriodDays: stride == 3 ? ns[i + 2] : 0));
            }
            else if (random && name == "[int data]" || booster)
            {
                var ns = Numbers(section);
                var stride = random || name == "[broadcast]" ? 4 : name is "[avatar]" or "[special avatar]" ? 5 : 3;
                var start = random ? 3 : 1;
                if (ns.Length <= start || (ns.Length - start) % stride != 0 || !random && ns[0] <= 0)
                { errors.Add("Unsupported reward pool " + name); continue; }
                var entries = ImmutableArray.CreateBuilder<OpeningWeightedReward>();
                for (var i = start; i < ns.Length; i += stride)
                {
                    if (ns[i] <= 0 || ns[i + 1] < 0 || ns[i + 2] <= 0) errors.Add("Invalid pool row");
                    // Avatar row extensions are not yet interpreted; never discard a nondefault one.
                    if (stride == 5 && (ns[i + 3] != 0 || ns[i + 4] != 0)) errors.Add("Unsupported avatar pool extension");
                    entries.Add(new(new(ns[i], ns[i + 2]), ns[i + 1]));
                }
                if (entries.All(e => e.Weight == 0)) errors.Add("Empty weight pool");
                pools.Add(new(random ? 1 : ns[0], entries.ToImmutable()));
            }
            else if (name.StartsWith("[package data", StringComparison.Ordinal)) errors.Add("Unsupported " + name);
        }
        if (random || booster || selection) errors.Add("Unclosed reward section");
        if (kind == OpeningPackageKind.Selection && selectionCount <= 0 && fixedRewards.Count > 1)
            errors.Add("Category-dependent zero-selection mode requires a separate interpretation");
        if (fixedRewards.GroupBy(r => r.ItemId).Any(g => g.Count() > 1) && kind == OpeningPackageKind.Selection)
            errors.Add("Duplicated selection identifier across categories");
        if (fixedRewards.Any(r => r.ItemId <= 0 || r.Count <= 0 || r.PeriodDays < 0)) errors.Add("Invalid fixed reward");
        if (fixedRewards.Count == 0 && pools.Count == 0) errors.Add("No rewards");
        return new(id, kind, fixedRewards.ToImmutable(), pools.ToImmutable(), selectionCount,
            material, materialCount, sourceCount, id == 2682272, errors.ToImmutable());
    }
}
