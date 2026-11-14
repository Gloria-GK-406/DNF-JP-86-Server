using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class PackageOpeningRuleService(IPackageOpeningDefinitionSource content, IItemPlacementRuleService placements,
    IItemExpirationRuleService expirationRules, IServiceItemRewardRuleService services) : IPackageOpeningRuleService
{
    public async ValueTask<OpeningPackageDefinition> DefinitionAsync(int itemId, OpeningPackageKind kind, CancellationToken ct)
    {
        var definition = await content.GetAsync(itemId, ct).ConfigureAwait(false);
        if (definition is null || definition.Kind != kind
            && !(kind is OpeningPackageKind.Package or OpeningPackageKind.AvatarPackage && definition.Kind == OpeningPackageKind.Selection))
            throw new OpeningRejectedException(OpeningRejection.WrongItem, $"Item {itemId} does not belong to {kind}.");
        if (!definition.Diagnostics.IsEmpty)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, string.Join("; ", definition.Diagnostics));
        if (definition.PurchaseSelectionGroups > 0)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Package selection groups are chosen only at purchase.");
        return definition;
    }

    public ImmutableArray<OpeningReward> Roll(OpeningPackageDefinition definition, OpeningDraw draw, int seed)
    {
        var rewards = ImmutableArray.CreateBuilder<OpeningReward>();
        var choices = draw.Choices;
        if (definition.Kind == OpeningPackageKind.Selection)
        {
            // The window offers one category, keyed by the coordinates the request carries (client 0151C440 and
            // 70 _processBoosterSelect look the IDs up in that category alone, so other categories may repeat them).
            var category = definition.Categories.FirstOrDefault(c => c.Key == draw.Category)
                ?? throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "Undeclared selection category.");
            if (definition.SelectionCount > 0)
            {
                if (choices.Count != definition.SelectionCount || choices.Distinct().Count() != choices.Count)
                    throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "Selection count or uniqueness disagrees.");
                foreach (var id in choices)
                    rewards.Add(category.Rewards.FirstOrDefault(r => r.ItemId == id)
                        ?? throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "Undeclared selection."));
            }
            else
            {
                // Selection count zero grants the whole category; the window sends every row's ID in declaration order.
                if (!choices.Order().SequenceEqual(category.Rewards.Select(r => r.ItemId).Order()))
                    throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "Whole-category selection disagrees.");
                rewards.AddRange(category.Rewards);
            }
        }
        else
        {
            if (choices.Count != 0 || draw.Category != default)
                throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "Unexpected selection.");
            rewards.AddRange(definition.FixedRewards);
        }
        var random = new Random(seed);
        foreach (var pool in definition.Pools)
        {
            // A [charactor] pool draws only for its job; the request carries no job, so the opener's own is used.
            if (pool.Job is { } job && job != draw.Job) continue;
            if (pool.Draws is < 1 or > 1024) throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Invalid draw count.");
            var total = pool.Choices.Sum(row => (long)row.Weight);
            if (total <= 0) throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Empty pool.");
            for (var n = 0; n < pool.Draws; n++)
            {
                var ticket = random.NextInt64(total);
                foreach (var row in pool.Choices)
                {
                    ticket -= row.Weight;
                    if (ticket >= 0) continue;
                    rewards.Add(row.Reward);
                    break;
                }
            }
        }
        if (rewards.Count == 0)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "No reward is declared for this job.");
        for (var i = 0; i < rewards.Count; i++)
            if (rewards[i].Upgrade == OpeningReward.RandomUpgrade)
                rewards[i] = rewards[i] with { Upgrade = DrawUpgrade(rewards[i].UpgradeChances, random), UpgradeChances = [] };
        return rewards.ToImmutable();
    }

    // 70 _processItem -> ProcessSpecialItem: a [cera package] / [cera booster] reward is unpacked, not inserted.
    // Its [package data selection] groups have no chooser here and are left out, as 70 leaves them out.
    public async ValueTask<ImmutableArray<OpeningReward>> ExpandAsync(IReadOnlyList<OpeningReward> rewards, int job, int seed, CancellationToken ct)
    {
        var random = new Random(seed);
        var result = ImmutableArray.CreateBuilder<OpeningReward>();
        foreach (var reward in rewards) await ExpandOneAsync(reward, 0).ConfigureAwait(false);
        return result.ToImmutable();

        async ValueTask ExpandOneAsync(OpeningReward reward, int depth)
        {
            var definition = await content.GetAsync(reward.ItemId, ct).ConfigureAwait(false);
            if (definition is not { ExpandsWhenGranted: true })
            {
                if (result.Count >= 8192) throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Too many expanded rewards.");
                result.Add(reward);
                return;
            }
            if (depth >= 8 || !definition.Diagnostics.IsEmpty || reward.Count > 1024)
                throw new OpeningRejectedException(OpeningRejection.UnsupportedContent,
                    $"Unexpandable package reward {reward.ItemId}: {string.Join("; ", definition.Diagnostics)}");
            for (var copy = 0; copy < reward.Count; copy++)
            {
                ImmutableArray<OpeningReward> contents = definition.FixedRewards.IsEmpty && definition.Pools.IsEmpty ? []
                    : Roll(definition with { Kind = OpeningPackageKind.Package }, new([], default, job), random.Next());
                foreach (var part in contents)
                {
                    var days = reward.PeriodDays == 0 ? part.PeriodDays : part.PeriodDays == 0 ? reward.PeriodDays
                        : Math.Min(reward.PeriodDays, part.PeriodDays);
                    await ExpandOneAsync(part with { PeriodDays = days }, depth + 1).ConfigureAwait(false);
                }
            }
        }
    }

    private static byte DrawUpgrade(ImmutableArray<OpeningUpgradeChance> chances, Random random)
    {
        var total = chances.Sum(c => (long)c.Weight);
        if (total <= 0) throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Empty upgrade probability.");
        var ticket = random.NextInt64(total);
        foreach (var chance in chances)
        {
            ticket -= chance.Weight;
            if (ticket < 0) return chance.Level;
        }
        throw new InvalidOperationException("Upgrade draw did not land.");
    }

    public async ValueTask<ImmutableArray<OpeningPreparedGrant>> PrepareAsync(IReadOnlyList<OpeningReward> rewards, DateTimeOffset now, CancellationToken ct)
    {
        if (rewards.Count is 0 or > 8192) throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Invalid reward count.");
        var result = ImmutableArray.CreateBuilder<OpeningPreparedGrant>();
        foreach (var drawn in rewards)
        {
            var reward = drawn;
            var item = await content.GetRewardAsync(reward.ItemId, ct).ConfigureAwait(false);
            if (item is not { Supported: true } || reward.Count <= 0 || reward.PeriodDays < 0)
                throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, $"Unsupported reward {reward.ItemId}.");
            if (item.IsService && !await services.GrantsAsItemAsync(item.ItemId, ct).ConfigureAwait(false))
                throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, $"Service reward {reward.ItemId} has no item use.");
            if (AccountMaterialCatalog.ContainsItem(item.ItemId))
                item = item with { Placement = await placements.PlacementForAsync(item.ItemId, ct).ConfigureAwait(false)
                    ?? throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Shared reward has no placement.") };
            if (item.Placement.WorldClass == ItemWorldClass.Avatar
                && (item.Abilities.IsEmpty ? reward.Ability != 0 : !item.Abilities.Contains(reward.Ability))
                || item.Placement.WorldClass != ItemWorldClass.Avatar && reward.Ability != 0)
                throw new OpeningRejectedException(OpeningRejection.InvalidChoice, $"Undeclared ability for {reward.ItemId}.");
            // Reinforcement shares the equipment attribute byte's low five bits.
            // A selection block's equipment modifiers reach every row of its [equipment] category; the client and 70
            // apply them only to equipment, so a stackable filed there keeps none.
            if (item.Placement.WorldClass != ItemWorldClass.Equipment)
                reward = reward with { Upgrade = 0, UpgradeChances = [], Forging = 0, QualityTier = null };
            if (reward.Upgrade > 31 || reward.Forging > 8 || reward.QualityTier is > EquipmentQualityTiers.Highest)
                throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, $"Invalid equipment modifier for {reward.ItemId}.");
            if (item.Placement.WorldClass == ItemWorldClass.Creature && (item.CreatureStartLevel is null || reward.PeriodDays != 0))
                throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unsupported creature creation.");
            // An avatar row names one of the avatar's [avatar type select] rows and takes its period, 0 being
            // permanent; a row out of range keeps the avatar's own period (70 CEquipItem::getAvatarPeriod).
            var ownDays = item.Placement.WorldClass == ItemWorldClass.Avatar && reward.AvatarTypeRow is { } row
                && row < item.AvatarTypePeriods.Length ? item.AvatarTypePeriods[row] : item.DefaultPeriodDays;
            if (ownDays < 0) throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, $"Invalid avatar period for {reward.ItemId}.");
            var days = reward.PeriodDays == 0 ? ownDays : ownDays == 0
                ? reward.PeriodDays : Math.Min(reward.PeriodDays, ownDays);
            var expiration = days == 0 ? 0 : checked((int)now.AddDays(days).ToUnixTimeSeconds());
            if (item.AbsoluteExpiration > 0) expiration = expiration == 0 ? item.AbsoluteExpiration : Math.Min(expiration, item.AbsoluteExpiration);
            var declared = expirationRules.Prepare(new ItemSnapshot(), item.Placement.Lifetime, now).ExpireTime;
            if (declared > 0) expiration = expiration == 0 ? declared : Math.Min(expiration, declared);
            if (expiration != 0 && expiration <= now.ToUnixTimeSeconds())
                throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, $"Reward {reward.ItemId} has already expired.");
            result.Add(new(reward, item, expiration));
        }
        return result.ToImmutable();
    }

    public bool LuckBonus(int value) => value == 8;

    // 70 CUser::check_locked_multibox_error / check_multibox_error: before a magic box draws, the
    // equipment, consumable, material, expert material (ITEM_TYPE 10) and creature tabs each hold
    // N = 2 empty slots. 70 omits the avatar tab; it is included so that acceptance never depends
    // on what is drawn. 70 scales N for a lucky opening by a DataManager+0x877e byte whose value is
    // unknown; #325 keeps N = 2 for every opening and every batch, because the extra lucky copies
    // and anything else that does not fit are mailed, and a larger N only kept a full tab stuck at
    // luck 8, which advances only on a successful opening.
    // 70 Dispatcher_UseBoosterItem checks one empty creature slot; Dispatcher_CeraPackageOpen has
    // fixed contents and checks nothing. Neither can fail because of what was drawn.
    public ImmutableArray<OpeningRoomRequirement> RoomBeforeDrawing(OpeningPackageDefinition definition)
    {
        if (definition.Kind == OpeningPackageKind.MagicBox)
            return [.. MagicBoxRoomSpaces.Select(space => new OpeningRoomRequirement(space, MagicBoxRoomPerOpening))];
        var booster = !definition.Pools.IsEmpty || definition.Kind == OpeningPackageKind.Selection;
        return booster ? [new(BusinessInventorySpace.CreatureInventory, 1)] : [];
    }

    private const int MagicBoxRoomPerOpening = 2;
    private static readonly ImmutableArray<BusinessInventorySpace> MagicBoxRoomSpaces =
    [
        BusinessInventorySpace.CarriedEquipment, BusinessInventorySpace.Consumable, BusinessInventorySpace.Material,
        BusinessInventorySpace.ExpertMaterial, BusinessInventorySpace.CreatureInventory, BusinessInventorySpace.Avatar,
    ];
    public int AdvanceLuck(int value) => value is < 0 or > 8
        ? throw new OpeningRejectedException(OpeningRejection.StateChanged, "Invalid Seria luck.") : value == 8 ? 1 : value + 1;
}
