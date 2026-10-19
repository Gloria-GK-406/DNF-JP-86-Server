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
                        && provider.Archive.ResolveString(token.Value) == Fields.StackableTypeField.Name;
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
        var sections = PvfNpcShopDefinitionSource.Sections(provider.Archive, item.Source);
        var periods = sections.Where(s => s.Name == Fields.UsablePeriodField.Name).ToArray();
        var period = periods.Length == 0 ? 0 : periods.Length == 1 && periods[0].Values.Count == 1
            && periods[0].Values[0].Kind == PvfTokenKind.Int32 ? periods[0].Values[0].Value : -1;
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
        var supported = period >= 0 && expiration >= 0 && item.Kind is not (CeraShopItemKind.Unknown or CeraShopItemKind.Service)
            && item.Diagnostics.All(d => d.Code is "PurchaseRestriction" or "MalformedPurchaseLevel"
                || d.Code == "UnsupportedInstanceDeclaration" && (d.Detail == Fields.UsablePeriodField.Name || d.Detail == Fields.ExpirationDateField.Name));
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
        static int Number(PvfNpcShopDefinitionSource.Section value, PvfField<int> field)
        {
            if (value.Values.Count != 1 || value.Values[0].Kind != PvfTokenKind.Int32)
                throw new InvalidDataException($"Premium item, field '{field.Name}': expected exactly one Int32.");
            return value.Values[0].Value;
        }
    }

    internal static OpeningPackageDefinition Parse(int id, IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections,
        Func<PvfToken, string> text)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        var fixedRewards = ImmutableArray.CreateBuilder<OpeningReward>();
        var pools = ImmutableArray.CreateBuilder<OpeningRewardPool>();
        var typeSections = sections.Where(section => section.Name == Fields.StackableTypeField.Name).ToArray();
        if (typeSections.Length > 1) errors.Add("Duplicate stackable type");
        if (typeSections.Length == 1 && (typeSections[0].Values.Count == 0 || !typeSections[0].Values[0].IsString ||
            typeSections[0].Values.Skip(1).Any(value => value.Kind != PvfTokenKind.Int32))) errors.Add("Invalid stackable type");
        var typeToken = typeSections.FirstOrDefault()?.Values.FirstOrDefault();
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
        var costSeen = false;
        var material = 0; var materialCount = 0; var sourceCount = 1; var selectionCount = 0;
        var random = false; var booster = false; var selection = false;
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
            if (name == Fields.CloseBoosterInfoField.Name) { booster = false; continue; }
            if (name == Fields.BoosterSelectCategoryField.Name)
            {
                selection = true;
                var category = Numbers(section);
                if (category.Length != 2 || category.Any(value => value < 0)) errors.Add("Invalid selection category");
                continue;
            }
            if (name == Fields.CloseBoosterSelectCategoryField.Name) { selection = false; continue; }
            if (name.StartsWith("[/", StringComparison.Ordinal)) continue;
            if ((name == Fields.UsableCountLimitField.Name || name == Fields.UsableCountField.Name || name == Fields.UseCountLimitField.Name)) errors.Add("Unimplemented use restriction " + name);
            if (name == Fields.BoosterSelectionNumField.Name)
            {
                var ns = Numbers(section);
                if (ns.Length != 1 || ns[0] < 0) errors.Add("Invalid selection count");
                else selectionCount = ns[0];
            }
            if (name == Fields.NeedMaterialField.Name || random && name == Fields.SealingRemovalItemField.Name)
            {
                var ns = Numbers(section);
                var start = name == Fields.SealingRemovalItemField.Name ? 1 : 0;
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
            if ((name == Fields.PackageDataField.Name || name == Fields.PackageDataIncludeUsablePeriodField.Name)
                || selection)
            {
                var ns = Numbers(section);
                if (selection && !(name == Fields.AvatarField.Name || name == Fields.EquipmentField.Name || name == Fields.EtcField.Name || name == Fields.CreatureField.Name))
                { errors.Add("Unsupported selection declaration " + name); continue; }
                var stride = selection && name == Fields.AvatarField.Name ? 4 : name == Fields.PackageDataIncludeUsablePeriodField.Name ? 3 : 2;
                if (ns.Length == 0 || ns.Length % stride != 0) errors.Add("Invalid fixed reward " + name);
                else for (var i = 0; i < ns.Length; i += stride)
                    // Avatar selection's two trailing fields are UI declaration fields in the reference reader.
                    // Creation uses the equipment's declared ability and the selected option supplied by the client.
                    fixedRewards.Add(new(ns[i], ns[i + 1], PeriodDays: stride == 3 ? ns[i + 2] : 0));
            }
            else if (random && name == Fields.IntDataField.Name || booster)
            {
                var ns = Numbers(section);
                var stride = random || name == Fields.BroadcastField.Name ? 4 : (name == Fields.AvatarField.Name || name == Fields.SpecialAvatarField.Name) ? 5 : 3;
                var start = random ? 3 : 1;
                if (ns.Length <= start || (ns.Length - start) % stride != 0 || !random && ns[0] <= 0)
                { errors.Add("Unsupported reward pool " + name); continue; }
                var entries = ImmutableArray.CreateBuilder<OpeningWeightedReward>();
                for (var i = start; i < ns.Length; i += stride)
                {
                    if (ns[i] <= 0 || ns[i + 1] < 0 || ns[i + 2] <= 0) errors.Add("Invalid pool row");
                    var reward = new OpeningReward(ns[i], ns[i + 2]);
                    // This archive's Dark Repulsor box explicitly promises an untradeable weapon.
                    // Support its complete observed profile, without assigning an unproven general
                    // meaning to extension 3. Other nondefault avatar profiles still fail closed.
                    var darkRepulsor = id == 10007316 && kind == OpeningPackageKind.Package && name == Fields.AvatarField.Name
                        && stride == 5 && ns.AsSpan().SequenceEqual(new int[] { 1, 101600023, 100000, 1, 3, 0 });
                    if (darkRepulsor) reward = reward with { TradeRestriction = 1 };
                    else if (stride == 5 && (id == 10007316 || ns[i + 3] != 0 || ns[i + 4] != 0)) errors.Add("Unsupported avatar pool extension");
                    entries.Add(new(reward, ns[i + 1]));
                }
                if (entries.All(e => e.Weight == 0)) errors.Add("Empty weight pool");
                pools.Add(new(random ? 1 : ns[0], entries.ToImmutable()));
            }
            else if (name.StartsWith(PackageDataFields.Prefix, StringComparison.Ordinal)) errors.Add("Unsupported " + name);
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
