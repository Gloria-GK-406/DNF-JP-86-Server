using System.Collections.Immutable;
using System.Collections.Frozen;
using System.Globalization;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfPackageOpeningDefinitionSource(PvfArchiveProvider provider,
    ICeraShopDefinitionSource items, IItemPlacementDefinitionSource placements) : IPackageOpeningDefinitionSource, ILotteryDefinitionCache
{
    private FrozenDictionary<int, OpeningPackageDefinition>? lotteryDefinitions;

    public OpeningPackageDefinition GetRequired(int itemId)
    {
        lock (gate)
        {
            if (lotteryDefinitions is null)
                throw new InvalidOperationException($"Lottery cache is not initialized (pot {itemId}).");
            return lotteryDefinitions.TryGetValue(itemId, out var definition) ? definition
                : throw new OpeningRejectedException(OpeningRejection.WrongItem, $"Pot {itemId} is not in the lottery cache.");
        }
    }

    public ValueTask<int> InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (lotteryDefinitions is not null) return ValueTask.FromResult(lotteryDefinitions.Count);
            paths ??= PvfListFile.Read(provider.Archive, "stackable/stackable.lst")
                .ToDictionary(row => row.Id, row => "stackable/" + row.RelativePath);
            var definitions = new Dictionary<int, OpeningPackageDefinition>();
            foreach (var (id, path) in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = provider.Archive.FindFile(path);
                if (index < 0) continue;
                var script = provider.Archive.OpenScript(index);
                var typeFollows = false;
                var isLottery = false;
                while (script.Read(out var token))
                {
                    if (typeFollows)
                    {
                        isLottery = token.IsString && string.Equals(provider.Archive.ResolveString(token.Value),
                            "[upgradable legacy]", StringComparison.OrdinalIgnoreCase);
                        break;
                    }
                    typeFollows = token.Kind == PvfTokenKind.Identifier
                        && provider.Archive.ResolveString(token.Value) == "[stackable type]";
                }
                if (!isLottery) continue;
                // Keep diagnostics: loading a definition does not authorize unsupported content.
                var definition = Parse(id, PvfNpcShopDefinitionSource.Sections(provider.Archive, path),
                    token => provider.Archive.ResolveString(token.Value));
                if (definition.Diagnostics.IsEmpty && definition.Pools.Length == 1 && definition.Pools[0].Draws == 1)
                {
                    long total = 0;
                    var ends = ImmutableArray.CreateBuilder<long>(definition.Pools[0].Choices.Length);
                    foreach (var choice in definition.Pools[0].Choices)
                        ends.Add(total = checked(total + choice.Weight));
                    definition = definition with { CumulativeWeights = ends.MoveToImmutable() };
                }
                definitions.Add(id, definition);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Publish only a complete snapshot; a failed/cancelled attempt is safe to retry.
            lotteryDefinitions = definitions.ToFrozenDictionary();
            foreach (var id in definitions.Keys) cache.Remove(id);
            return ValueTask.FromResult(lotteryDefinitions.Count);
        }
    }

    private readonly Lock gate = new();
    private readonly Dictionary<int, OpeningPackageDefinition?> cache = [];
    private Dictionary<int, string>? paths;
    private Dictionary<int, (int Type, TimeSpan Duration)>? premiums;

    public async ValueTask<OpeningPackageDefinition?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpeningPackageDefinition? result;
        lock (gate)
        {
            if (lotteryDefinitions is not null && lotteryDefinitions.TryGetValue(itemId, out result)) return result;
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
        if (itemId is 1 or 42) return new(1, new(0, int.MaxValue,
            new(BusinessInventorySpace.VirtualResource, 1, 1)), 0, false, [], null, true);
        lock (gate)
        {
            premiums ??= Premiums();
            if (premiums.TryGetValue(itemId, out var premium))
                return new(itemId, new(0, 1,
                    new(BusinessInventorySpace.VirtualResource, premium.Type, premium.Type)),
                    0, false, [], null,
                    premium.Type is > 0 and <= sbyte.MaxValue && premium.Duration > TimeSpan.Zero, PremiumType: premium.Type, PremiumDuration: premium.Duration);
        }
        var item = await items.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false);
        var placement = await placements.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null || placement is null) return null;
        placement = item.Kind switch
        {
            CeraShopItemKind.Avatar => placement with { ItemKind = 8, StackLimit = 1,
                Destination = new(BusinessInventorySpace.Avatar, 0, 209) },
            CeraShopItemKind.Creature => placement with { ItemKind = 5, StackLimit = 1,
                Destination = new(BusinessInventorySpace.CreatureInventory, 0, 139) },
            CeraShopItemKind.CreatureEquipment => placement with { ItemKind = 6, StackLimit = 1,
                Destination = new(BusinessInventorySpace.CreatureEquipment, 0, 48) },
            _ when placement.ItemKind == 7 => placement with {
                Destination = new(BusinessInventorySpace.CreatureConsumable, 0, 50) },
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

    private Dictionary<int, (int Type, TimeSpan Duration)> Premiums()
    {
        return ParsePremiums(PvfNpcShopDefinitionSource.Sections(provider.Archive, "etc/premiumlist_new.etc"));
    }

    internal static Dictionary<int, (int Type, TimeSpan Duration)> ParsePremiums(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections)
    {
        var result = new Dictionary<int, (int, TimeSpan)>();
        var type = 0;
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.Name == "[type]") type = Number(section);
            if (type <= 0 || section.Name != "[item]") continue;
            var item = Number(section);
            var term = 0; var minutes = false;
            for (var j = i + 1; j < sections.Count; j++)
            {
                var next = sections[j];
                if (next.Name is "[/item]" or "[item]" or "[type]") break;
                if (next.Name == "[term]") term = Number(next);
                if (next.Name == "[is term unit minute]") minutes = Number(next) == 1;
            }
            if (item > 0 && term > 0)
                result[item] = (type, minutes ? TimeSpan.FromMinutes(term) : TimeSpan.FromDays(term));
        }
        return result;

        static int Number(PvfNpcShopDefinitionSource.Section value) =>
            value.Values.Count == 1 && value.Values[0].Kind == PvfTokenKind.Int32 ? value.Values[0].Value : 0;
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
            "[upgradable legacy]" => OpeningPackageKind.Lottery,
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
        var goldCost = 0;
        var material = 0; var materialCount = 0; var sourceCount = 1; var selectionCount = 0;
        var random = false; var booster = false; var selection = false;
        foreach (var section in sections)
        {
            var name = section.Name.ToLowerInvariant();
            if (kind == OpeningPackageKind.Lottery && name == "[lottery use cost]")
            {
                var values = Numbers(section);
                if (values.Length != 1 || values[0] < 0) errors.Add("Invalid lottery cost");
                else goldCost = values[0];
                continue;
            }
            if (kind == OpeningPackageKind.Lottery && name == "[lottery use need item]")
            {
                errors.Add("Lottery material costs are not supported by this opening entry yet");
                continue;
            }
            if (kind == OpeningPackageKind.Lottery && name == "[int data]")
            {
                var values = Numbers(section);
                var entries = ImmutableArray.CreateBuilder<OpeningWeightedReward>();
                if (values.Length == 0 || values.Length % 3 != 0) errors.Add("Invalid lottery reward triples");
                else for (var i = 0; i < values.Length; i += 3)
                {
                    if (values[i] <= 0 || values[i + 1] < 0 || values[i + 2] <= 0) errors.Add("Invalid lottery reward");
                    entries.Add(new(new(values[i], values[i + 2]), values[i + 1]));
                }
                pools.Add(new(1, entries.ToImmutable()));
                continue;
            }
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
            material, materialCount, sourceCount, id == 2682272, errors.ToImmutable()) { GoldCost = goldCost };
    }
}
