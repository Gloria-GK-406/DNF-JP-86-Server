using Dfo.Pvf.Schema;
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
    private static readonly PvfFieldFamily PackageDataFields = new("[package data");

    private static class Fields
    {
        internal static readonly PvfField<int> LotteryUseCostField = PvfField.Int32("[lottery use cost]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> LotteryUseNeedItemField = PvfField.Int32List("[lottery use need item]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> IntDataField = PvfField.Int32List("[int data]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> RandomboxField = PvfField.Tokens("[randombox]", optional: true);
        internal static readonly PvfField<bool> CloseRandomboxField = PvfField.Marker("[/randombox]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> BoosterInfoField = PvfField.Tokens("[booster info]", optional: true);
        internal static readonly PvfField<bool> CloseBoosterInfoField = PvfField.Marker("[/booster info]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> BoosterSelectCategoryField = PvfField.Int32List("[booster select category]", optional: true);
        internal static readonly PvfField<bool> CloseBoosterSelectCategoryField = PvfField.Marker("[/booster select category]", optional: true);
        internal static readonly PvfField<int> BoosterSelectionNumField = PvfField.Int32("[booster selection num]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> NeedMaterialField = PvfField.Int32List("[need material]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> SealingRemovalItemField = PvfField.Int32List("[sealing removal item]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> AvatarField = PvfField.Tokens("[avatar]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> PackageDataIncludeUsablePeriodField = PvfField.Int32List("[package data include usable period]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> BroadcastField = PvfField.Tokens("[broadcast]", optional: true);
        internal static readonly PvfField<int> UsableCountLimitField = PvfField.Int32("[usable count limit]", optional: true);
        internal static readonly PvfField<int> UsableCountField = PvfField.Int32("[usable count]", optional: true);
        internal static readonly PvfField<int> UseCountLimitField = PvfField.Int32("[use count limit]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> PackageDataField = PvfField.Int32List("[package data]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> EquipmentField = PvfField.Tokens("[equipment]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> EtcField = PvfField.Tokens("[etc]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> CreatureField = PvfField.Tokens("[creature]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> SpecialAvatarField = PvfField.Tokens("[special avatar]", optional: true);

        internal static readonly PvfField<IReadOnlyList<PvfToken>> UsablePeriodField = PvfField.Tokens("[usable period]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> ExpirationDateField = PvfField.Tokens("[expiration date]", optional: true);
        internal static readonly PvfField<int> TypeField = PvfField.Int32("[type]", optional: true);
        internal static readonly PvfField<int> ItemField = PvfField.Int32("[item]", optional: true);
        internal static readonly PvfField<int> TermField = PvfField.Int32("[term]", optional: true);
        internal static readonly PvfField<int> IsTermUnitMinuteField = PvfField.Int32("[is term unit minute]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> StackableTypeField = PvfField.Tokens("[stackable type]", optional: true);
        internal static readonly PvfField<bool> CloseItemField = PvfField.Marker("[/item]", optional: true);
    }

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
            paths ??= PvfListFile.Paths(provider.Archive, "stackable/stackable.lst", prefix: "stackable/");
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
                        && provider.Archive.ResolveString(token.Value) == Fields.StackableTypeField.Name;
                }
                if (!isLottery) continue;
                // Keep diagnostics: loading a definition does not authorize unsupported content.
                var definition = Parse(id, (PvfDocument.TryOpen(provider.Archive, path, PvfPreamble.Ignore)?.Sections ?? []),
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
    private IReadOnlyDictionary<int, string>? paths;
    private Dictionary<int, (int Type, TimeSpan Duration)>? premiums;

    public async ValueTask<OpeningPackageDefinition?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpeningPackageDefinition? result;
        lock (gate)
        {
            if (lotteryDefinitions is not null && lotteryDefinitions.TryGetValue(itemId, out result)) return result;
            if (cache.TryGetValue(itemId, out result)) return result;
            paths ??= PvfListFile.Paths(provider.Archive, "stackable/stackable.lst", prefix: "stackable/");
            result = paths.TryGetValue(itemId, out var path) ? Parse(itemId,
                (PvfDocument.TryOpen(provider.Archive, path, PvfPreamble.Ignore)?.Sections ?? []),
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
        if (itemId == 0) return new(0, new(0, int.MaxValue,
            new(BusinessInventorySpace.VirtualResource, 0, 0)), 0, false, [], null, true);
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
        var sections = (PvfDocument.TryOpen(provider.Archive, item.Source, PvfPreamble.Ignore)?.Sections ?? []);
        var periods = sections.Where(s => s.Name == Fields.UsablePeriodField.Name).ToArray();
        var periodSection = PvfRepeatedFields.Effective(periods, Fields.UsablePeriodField.Name);
        var period = periods.Length == 0 ? 0 : periodSection is { Values.Count: 1 }
            && periodSection.Values[0].Kind == PvfTokenKind.Int32 ? periodSection.Values[0].Value : -1;
        var expirations = sections.Where(s => s.Name == Fields.ExpirationDateField.Name).ToArray();
        var expiration = 0;
        if (expirations.Length > 0)
        {
            var values = expirations[0].Values;
            if (expirations.Length != 1 || values.Count != 1) expiration = -1;
            else if (values[0].Kind == PvfTokenKind.Int32) expiration = values[0].Value == 0 ? 0 : -1;
            else if (values[0].IsString && DateTime.TryParseExact(provider.Archive.ResolveString(values[0].Value),
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                var seconds = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.FromHours(8)).ToUnixTimeSeconds();
                expiration = seconds is > 0 and <= int.MaxValue ? (int)seconds : -1;
            }
            else expiration = -1;
        }
        var supported = period >= 0 && expiration >= 0 && item.Kind is not CeraShopItemKind.Unknown
            && item.Diagnostics.All(d => d.Code is "PurchaseRestriction" or "MalformedPurchaseLevel"
                || d.Code == "UnsupportedInstanceDeclaration" && (d.Detail == Fields.UsablePeriodField.Name || d.Detail == Fields.ExpirationDateField.Name));
        return new(itemId, placement, item.Durability, item.Sealed,
            item.Avatar?.SelectableAbilities ?? [], item.Creature?.StartLevel, supported, period, expiration)
        {
            AvatarTypePeriods = item.Avatar?.PriceOptions.Select(row => row.Period).ToImmutableArray() ?? [],
            IsService = item.Kind == CeraShopItemKind.Service,
            ExpandsWhenGranted = await GetAsync(itemId, cancellationToken).ConfigureAwait(false) is { ExpandsWhenGranted: true },
        };
    }

    private Dictionary<int, (int Type, TimeSpan Duration)> Premiums()
    {
        return ParsePremiums((PvfDocument.TryOpen(provider.Archive, "etc/premiumlist_new.etc", PvfPreamble.Ignore)?.Sections ?? []));
    }

    internal static Dictionary<int, (int Type, TimeSpan Duration)> ParsePremiums(
        IReadOnlyList<PvfSection> sections)
    {
        var result = new Dictionary<int, (int, TimeSpan)>();
        foreach (var section in sections)
        {
            if (section.Name == Fields.TypeField.Name) Number(section, Fields.TypeField);
            else if (section.Name == Fields.ItemField.Name) Number(section, Fields.ItemField);
            else if (section.Name == Fields.TermField.Name) Number(section, Fields.TermField);
            else if (section.Name == Fields.IsTermUnitMinuteField.Name) Number(section, Fields.IsTermUnitMinuteField);
        }
        var type = 0;
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.Name == Fields.TypeField.Name) type = Number(section, Fields.TypeField);
            if (section.Name != Fields.ItemField.Name) continue;
            var item = Number(section, Fields.ItemField);
            var term = 0; var minutes = false; var hasTerm = false; var hasUnit = false;
            for (var j = i + 1; j < sections.Count; j++)
            {
                var next = sections[j];
                if (next.Name == Fields.CloseItemField.Name || next.Name == Fields.ItemField.Name || next.Name == Fields.TypeField.Name) break;
                if (next.Name == Fields.TermField.Name)
                {
                    if (hasTerm) throw new InvalidDataException("Premium item: duplicate [term].");
                    term = Number(next, Fields.TermField); hasTerm = true;
                }
                if (next.Name == Fields.IsTermUnitMinuteField.Name)
                {
                    if (hasUnit) throw new InvalidDataException("Premium item: duplicate [is term unit minute].");
                    minutes = Number(next, Fields.IsTermUnitMinuteField) == 1; hasUnit = true;
                }
            }
            if (type > 0 && item > 0 && term > 0)
                result[item] = (type, minutes ? TimeSpan.FromMinutes(term) : TimeSpan.FromDays(term));
        }
        return result;

        // Archive-independent parser entry retains the same complete scalar oracle as typed Int32.
        static int Number(PvfSection value, PvfField<int> field)
        {
            if (value.Values.Count != 1 || value.Values[0].Kind != PvfTokenKind.Int32)
                throw new InvalidDataException($"Premium item, field '{field.Name}': expected exactly one Int32.");
            return value.Values[0].Value;
        }
    }

    // [booster info] / [booster select category] category tags, numbered as the A21 client numbers them
    // (0294F060; Document/tasks/package-opening-328/README.md). Any other tag is the client's "other" category.
    private static readonly FrozenDictionary<string, int> BoosterCategories = new Dictionary<string, int>
    {
        ["[avatar]"] = 0, ["[special avatar]"] = 1, ["[cera]"] = 2, ["[creature]"] = 3, ["[equipment]"] = 4,
        ["[stackable]"] = 5, ["[etc]"] = 6, ["[special creature]"] = 7, ["[emblem]"] = 8, ["[broadcast]"] = 9, ["[charactor]"] = 10,
    }.ToFrozenDictionary();
    private const int AvatarCategory = 0, SpecialAvatarCategory = 1, EquipmentCategory = 4, BroadcastCategory = 9, JobCategory = 10;

    // [charactor] job names in the client's job numbering (table built by 02FE2590); [all] is its fallback entry.
    private static readonly FrozenDictionary<string, int> JobNames = new Dictionary<string, int>
    {
        ["[swordman]"] = 0, ["[fighter]"] = 1, ["[gunner]"] = 2, ["[mage]"] = 3, ["[priest]"] = 4, ["[at gunner]"] = 5,
        ["[thief]"] = 6, ["[at fighter]"] = 7, ["[at mage]"] = 8, ["[demonic swordman]"] = 9, ["[creator mage]"] = 10,
        ["[at swordman]"] = 11, ["[knight]"] = 12, ["[demonic lancer]"] = 13, ["[all]"] = 14,
    }.ToFrozenDictionary();
    private const int AllJobs = 14;
    private const int MaxForging = 8;

    // These three weapon avatar boxes promise in their description that the weapon cannot be traded. Their rows
    // carry no trade fact (3 0 is an avatar type row and an ability) and 70 adds no trade lock; #328 keeps the promise.
    private static readonly FrozenSet<int> UntradeableAvatarBoxes = FrozenSet.ToFrozenSet([10007316, 10007317, 10007318]);

    internal static OpeningPackageDefinition Parse(int id, IReadOnlyList<PvfSection> sections,
        Func<PvfToken, string> text)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        var fixedRewards = ImmutableArray.CreateBuilder<OpeningReward>();
        var pools = ImmutableArray.CreateBuilder<OpeningRewardPool>();
        var categories = new List<OpeningSelectionCategory>();
        var typeSection = PvfRepeatedFields.Effective(
            sections.Where(section => section.Name == Fields.StackableTypeField.Name).ToArray(), Fields.StackableTypeField.Name);
        if (typeSection is not null && (typeSection.Values.Count == 0 || !typeSection.Values[0].IsString ||
            typeSection.Values.Skip(1).Any(value => value.Kind != PvfTokenKind.Int32))) errors.Add("Invalid stackable type");
        var typeToken = typeSection?.Values.FirstOrDefault();
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
        int[] Numbers(PvfSection section)
        {
            if (section.Values.Any(t => t.Kind != PvfTokenKind.Int32)) errors.Add("Noninteger " + section.Name);
            return section.Values.Where(t => t.Kind == PvfTokenKind.Int32).Select(t => t.Value).ToArray();
        }
        // Avatar rows end with two extensions (70 df_game_r booster handler; A21 stores them as u16 and u8):
        // the avatar's own [avatar type select] row, whose period applies, and the avatar ability.
        OpeningReward AvatarRow(OpeningReward reward, int row, int ability)
        {
            // A negative ability (-1) declares none; the client keeps it in a byte and the window picks one.
            if (ability > byte.MaxValue) { errors.Add("Invalid avatar ability"); ability = 0; }
            if (ability < 0) ability = 0;
            return reward with
            {
                Ability = (byte)ability, AvatarTypeRow = row is >= 0 and <= ushort.MaxValue ? row : null,
                TradeRestriction = UntradeableAvatarBoxes.Contains(id) ? (byte)1 : (byte)0,
            };
        }
        var goldCost = 0;
        var costSeen = false;
        var materialSeen = false;
        var material = 0; var materialCount = 0; var sourceCount = 1; var selectionCount = 0;
        var random = false; var booster = false; var purchaseGroups = 0;
        string? boosterCategory = null;
        SelectionBlock? block = null;
        void BoosterSection(PvfSection section, string name)
        {
            if (name.StartsWith("[/", StringComparison.Ordinal))
            {
                if (boosterCategory is null || name != "[/" + boosterCategory[1..]) errors.Add("Mismatched booster category " + name);
                boosterCategory = null;
                return;
            }
            if (!BoosterCategories.TryGetValue(name, out var category)) { errors.Add("Unsupported reward pool " + name); return; }
            if (boosterCategory is not null) errors.Add("Unclosed booster category " + boosterCategory);
            boosterCategory = name;
            var values = section.Values;
            int? job = null;
            var start = 0;
            if (category == JobCategory)
            {
                if (values.Count == 0 || !values[0].IsString || !JobNames.TryGetValue(text(values[0]).ToLowerInvariant(), out var code))
                { errors.Add("Invalid job pool"); return; }
                job = code == AllJobs ? null : code;
                start = 1;
            }
            // The client's strict integer reader (028A9310) truncates a float toward zero: 252.5 is 252.
            if (values.Skip(start).Any(t => t.Kind is not (PvfTokenKind.Int32 or PvfTokenKind.Single))) { errors.Add("Noninteger " + name); return; }
            var ns = values.Skip(start).Select(t => t.Kind == PvfTokenKind.Single ? (int)MathF.Truncate(t.AsSingle()) : t.Value).ToArray();
            (int, int)? header = null;
            if (category == JobCategory)
            {
                if (ns.Length < 2) { errors.Add("Invalid job pool"); return; }
                header = (ns[0], ns[1]);
                ns = ns[2..];
            }
            var stride = category is AvatarCategory or SpecialAvatarCategory ? 5 : category == BroadcastCategory ? 4 : 3;
            // A pool may declare its draw count and no rows (`[etc] 1 [/etc]`, an empty job pool): the client keeps
            // the empty category (029A28AD..029A2A52) and it grants nothing.
            if (ns.Length == 1 && ns[0] > 0) return;
            if (ns.Length <= 1 || (ns.Length - 1) % stride != 0 || ns[0] <= 0)
            { errors.Add("Unsupported reward pool " + name); return; }
            var entries = ImmutableArray.CreateBuilder<OpeningWeightedReward>();
            for (var i = 1; i < ns.Length; i += stride)
            {
                if (ns[i] <= 0 || ns[i + 1] < 0 || ns[i + 2] <= 0) errors.Add("Invalid pool row");
                var reward = new OpeningReward(ns[i], ns[i + 2]);
                if (stride == 5) reward = AvatarRow(reward, ns[i + 3], ns[i + 4]);
                entries.Add(new(reward, ns[i + 1]));
            }
            if (entries.All(e => e.Weight == 0)) errors.Add("Empty weight pool");
            pools.Add(new(ns[0], entries.ToImmutable()) { Job = job, JobHeader = header });
        }
        void SelectionSection(SelectionBlock selection, PvfSection section, string name)
        {
            switch (name)
            {
                case "[booster equipment upgrade]":
                {
                    var ns = Numbers(section);
                    if (ns.Length != 1 || ns[0] is < 0 or > 31 && ns[0] != OpeningReward.RandomUpgrade) errors.Add("Invalid equipment upgrade");
                    else selection.Upgrade = (byte)ns[0];
                    return;
                }
                // Forging (锻造, the separate-upgrade table) and the quality tier 0..4; 10006048 describes its
                // +11 / separate 5 weapon as "强化等级为 +11 ... 锻造等级为 +5".
                case "[booster equipment separate]":
                {
                    var ns = Numbers(section);
                    if (ns.Length != 1 || ns[0] is < 0 or > MaxForging) errors.Add("Invalid " + name);
                    else selection.Forging = (byte)ns[0];
                    return;
                }
                case "[booster equipment grade]":
                {
                    var ns = Numbers(section);
                    if (ns.Length != 1 || ns[0] is < EquipmentQualityTiers.Lowest or > EquipmentQualityTiers.Highest) errors.Add("Invalid " + name);
                    else selection.QualityTier = (byte)ns[0];
                    return;
                }
                // Window marks only: preselected and recommended cells. The opener still sends every chosen ID.
                case "[default select]":
                case "[recommend]":
                {
                    var ns = Numbers(section);
                    if (ns.Length == 0 || ns[0] != ns.Length - 1) errors.Add("Invalid " + name);
                    return;
                }
                case "[random probability]":
                {
                    // The client refuses the whole script unless the block's upgrade is the random marker.
                    var ns = Numbers(section);
                    if (selection.Upgrade != OpeningReward.RandomUpgrade) errors.Add("Random probability without random upgrade");
                    if (ns.Length == 0 || ns.Length % 2 != 0) { errors.Add("Invalid random probability"); return; }
                    var chances = ImmutableArray.CreateBuilder<OpeningUpgradeChance>();
                    for (var i = 0; i < ns.Length; i += 2)
                    {
                        if (ns[i] is < 0 or > 31 || ns[i + 1] < 0) errors.Add("Invalid random probability");
                        else chances.Add(new((byte)ns[i], ns[i + 1]));
                    }
                    if (chances.Sum(c => (long)c.Weight) <= 0) errors.Add("Empty random probability");
                    selection.Chances = chances.ToImmutable();
                    return;
                }
                case "[force insert avatar option]":
                    errors.Add("Unsupported " + name);
                    return;
            }
            if (name.StartsWith("[/", StringComparison.Ordinal)) return;
            // The client reads any other tag as its "other" category and takes its integers as rows.
            if (!BoosterCategories.TryGetValue(name, out var category))
            {
                if (section.Values.Any(t => t.Kind == PvfTokenKind.Int32)) errors.Add("Unsupported selection declaration " + name);
                return;
            }
            // Rows end at the first non-integer and no closing tag is required, so a closer written without its
            // slash (`[equipment] id n [equipment]`) or an empty category declares no rows.
            if (section.Values.Count == 0) return;
            var stride = category is AvatarCategory or SpecialAvatarCategory ? 4 : category == BroadcastCategory ? 3 : 2;
            var rows = Numbers(section);
            if (rows.Length == 0 || rows.Length % stride != 0) { errors.Add("Invalid selection rows " + name); return; }
            for (var i = 0; i < rows.Length; i += stride)
            {
                var reward = new OpeningReward(rows[i], rows[i + 1]);
                // A selection row's ability is only the window's default: the opener picks the ability and the
                // request carries it, so only the requested one is checked against the avatar's declaration.
                if (category is AvatarCategory or SpecialAvatarCategory) reward = AvatarRow(reward, rows[i + 2], 0);
                // The block's equipment modifiers reach equipment rows declared after them (client 029A2FA0).
                if (category == EquipmentCategory)
                {
                    if (selection.Upgrade == OpeningReward.RandomUpgrade && selection.Chances.IsEmpty)
                        errors.Add("Random upgrade without probability");
                    reward = reward with
                    {
                        Upgrade = selection.Upgrade, UpgradeChances = selection.Chances,
                        Forging = selection.Forging, QualityTier = selection.QualityTier,
                    };
                }
                selection.Rewards.Add(reward);
            }
        }
        foreach (var section in sections)
        {
            var name = section.Name.ToLowerInvariant();
            if (kind == OpeningPackageKind.Lottery && name == Fields.LotteryUseCostField.Name)
            {
                if (costSeen) errors.Add("Duplicate lottery cost");
                costSeen = true;
                var values = Numbers(section);
                if (values.Length != 1 || values[0] < 0) errors.Add("Invalid lottery cost");
                else goldCost = values[0];
                continue;
            }
            if (kind == OpeningPackageKind.Lottery && name == Fields.LotteryUseNeedItemField.Name)
            {
                errors.Add("Lottery material costs are not supported by this opening entry yet");
                continue;
            }
            // Other pots use this field for shop recipes, already paid by BuyItem.
            if (kind == OpeningPackageKind.Lottery && id == 7615 && name == Fields.NeedMaterialField.Name)
            {
                if (materialSeen) errors.Add("Duplicate lottery material cost");
                materialSeen = true;
                var values = Numbers(section);
                if (values.Length != 2 || values[0] <= 0 || values[1] <= 0)
                    errors.Add("Invalid lottery material cost");
                else { material = values[0]; materialCount = values[1]; }
                continue;
            }
            if (kind == OpeningPackageKind.Lottery && name == Fields.IntDataField.Name)
            {
                var values = Numbers(section);
                var entries = ImmutableArray.CreateBuilder<OpeningWeightedReward>();
                if (values.Length == 0 || values.Length % 3 != 0) errors.Add("Invalid lottery reward triples");
                else for (var i = 0; i < values.Length; i += 3)
                {
                    if ((values[i] <= 0 && !(id == 7615 && values[i] == 0)) || values[i + 1] < 0 || values[i + 2] <= 0) errors.Add("Invalid lottery reward");
                    entries.Add(new(new(values[i], values[i + 2]), values[i + 1]));
                }
                pools.Add(new(1, entries.ToImmutable()));
                continue;
            }
            if (name == Fields.RandomboxField.Name) { random = true; continue; }
            if (name == Fields.CloseRandomboxField.Name) { random = false; continue; }
            if (name == Fields.BoosterInfoField.Name) { booster = true; continue; }
            if (name == Fields.CloseBoosterInfoField.Name)
            {
                if (boosterCategory is not null) errors.Add("Unclosed booster category " + boosterCategory);
                booster = false; boosterCategory = null;
                continue;
            }
            if (name == Fields.BoosterSelectCategoryField.Name)
            {
                if (block is not null) errors.Add("Unclosed selection category");
                var key = Numbers(section);
                if (key.Length != 2 || key.Any(value => value is < 0 or > byte.MaxValue)) { errors.Add("Invalid selection category"); key = [0, 0]; }
                block = new(new((byte)key[0], (byte)key[1]));
                continue;
            }
            if (name == Fields.CloseBoosterSelectCategoryField.Name)
            {
                // A stray closing tag outside a block is skipped, as the client skips unknown top-level tags.
                if (block is null) continue;
                // A later block at the same coordinates replaces the earlier one, an empty block included
                // (client 0296C3D0 map operator[] then 01520AC0 vector assignment).
                var index = categories.FindIndex(c => c.Key == block.Key);
                var category = new OpeningSelectionCategory(block.Key, block.Rewards.ToImmutable());
                if (index < 0) categories.Add(category); else categories[index] = category;
                block = null;
                continue;
            }
            if (block is not null) { SelectionSection(block, section, name); continue; }
            if (booster) { BoosterSection(section, name); continue; }
            if (name.StartsWith("[/", StringComparison.Ordinal)) continue;
            if ((name == Fields.UsableCountLimitField.Name || name == Fields.UsableCountField.Name || name == Fields.UseCountLimitField.Name)) errors.Add("Unimplemented use restriction " + name);
            if (name == Fields.BoosterSelectionNumField.Name)
            {
                var ns = Numbers(section);
                if (ns.Length != 1 || ns[0] < 0) errors.Add("Invalid selection count");
                else selectionCount = ns[0];
            }
            if (random && name == Fields.SealingRemovalItemField.Name)
            {
                var ns = Numbers(section);
                if (ns.Length < 3 || (ns.Length - 1) % 2 != 0 || ns[0] * 2 + 1 != ns.Length) { errors.Add("Invalid removal declaration"); continue; }
                for (var i = 1; i < ns.Length; i += 2)
                {
                    if (ns[i] <= 0 || ns[i + 1] < 0) errors.Add("Invalid removal count");
                    else if (ns[i] == id) sourceCount = ns[i + 1];
                    // Seria's declaration retains material 201 with count zero: no key is consumed.
                    else if (ns[i + 1] == 0) continue;
                    else if (material == 0) { material = ns[i]; materialCount = ns[i + 1]; }
                    else errors.Add("Multiple required materials");
                }
            }
            if (name == Fields.PackageDataField.Name || name == Fields.PackageDataIncludeUsablePeriodField.Name)
            {
                // The client clears the list on each of these two tags: the last declaration is the content.
                fixedRewards.Clear();
                var ns = Numbers(section);
                var stride = name == Fields.PackageDataIncludeUsablePeriodField.Name ? 3 : 2;
                if (ns.Length == 0 || ns.Length % stride != 0) errors.Add("Invalid fixed reward " + name);
                else for (var i = 0; i < ns.Length; i += stride)
                    fixedRewards.Add(new(ns[i], ns[i + 1], PeriodDays: stride == 3 ? ns[i + 2] : 0));
            }
            else if (random && name == Fields.IntDataField.Name)
            {
                var ns = Numbers(section);
                const int start = 3, stride = 4;
                if (ns.Length <= start || (ns.Length - start) % stride != 0) { errors.Add("Unsupported reward pool " + name); continue; }
                var entries = ImmutableArray.CreateBuilder<OpeningWeightedReward>();
                for (var i = start; i < ns.Length; i += stride)
                {
                    if (ns[i] <= 0 || ns[i + 1] < 0 || ns[i + 2] <= 0) errors.Add("Invalid pool row");
                    entries.Add(new(new(ns[i], ns[i + 2]), ns[i + 1]));
                }
                if (entries.All(e => e.Weight == 0)) errors.Add("Empty weight pool");
                pools.Add(new(1, entries.ToImmutable()));
            }
            else if (name == "[package data selection]") purchaseGroups++;
            else if (name.StartsWith(PackageDataFields.Prefix, StringComparison.Ordinal)) errors.Add("Unsupported " + name);
        }
        if (random || booster || block is not null) errors.Add("Unclosed reward section");
        if (kind == OpeningPackageKind.Selection && categories.Count == 0) errors.Add("No selection categories");
        if (kind != OpeningPackageKind.Selection && categories.Count > 0) errors.Add("Selection categories outside a selection package");
        // An empty category is legitimate (a job without that growth type); the window cannot confirm it.
        if (categories.Any(c => c.Rewards.Any(r => r.ItemId <= 0 || r.Count <= 0)))
            errors.Add("Invalid selection category rows");
        if (fixedRewards.Any(r => r.ItemId <= 0 || r.Count <= 0 || r.PeriodDays < 0)) errors.Add("Invalid fixed reward");
        if (fixedRewards.Count == 0 && pools.Count == 0 && categories.Count == 0 && purchaseGroups == 0) errors.Add("No rewards");
        return new(id, kind, fixedRewards.ToImmutable(), pools.ToImmutable(), selectionCount,
            material, materialCount, sourceCount, id == 2682272, errors.ToImmutable())
        {
            GoldCost = goldCost, Categories = [.. categories], PurchaseSelectionGroups = purchaseGroups,
            ExpandsWhenGranted = type is "[cera package]" or "[cera booster]",
        };
    }

    private sealed class SelectionBlock(OpeningCategory key)
    {
        public OpeningCategory Key { get; } = key;
        public byte Upgrade { get; set; }
        public ImmutableArray<OpeningUpgradeChance> Chances { get; set; } = [];
        public byte Forging { get; set; }
        public byte? QualityTier { get; set; }
        public ImmutableArray<OpeningReward>.Builder Rewards { get; } = ImmutableArray.CreateBuilder<OpeningReward>();
    }
}
