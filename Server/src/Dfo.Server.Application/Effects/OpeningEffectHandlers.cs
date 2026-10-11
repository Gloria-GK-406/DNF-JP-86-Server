using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Effects;

internal abstract class OpeningEffectHandler<T> : EffectHandler<T>, IInventoryEffectPreviewHandler where T : InventoryEffect
{
    protected static void Refuse(InventoryEffectRejection reason) => throw new InventoryEffectException(reason);
    public abstract ValueTask PreviewAsync(EffectExecutionContext context, T effect, IInventoryEffectPreviewState state, CancellationToken ct);
    ValueTask IInventoryEffectPreviewHandler.PreviewAsync(EffectExecutionContext context, DomainEffect effect,
        IInventoryEffectPreviewState state, CancellationToken ct) => PreviewAsync(context, (T)effect, state, ct);
}

internal sealed class GrantPreparedItemEffectHandler(IItemArrivalRuleService arrivals, IItemDeliveryDomainService delivery)
    : OpeningEffectHandler<GrantPreparedItemEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, GrantPreparedItemEffect effect, CancellationToken ct)
    {
        var g = effect.Grant;
        if (g.Reward.Count <= 0 || g.Item.ItemId <= 0 || g.Item.PremiumType != 0
            || g.Item.Placement.Destination.Space == BusinessInventorySpace.VirtualResource
            || g.Item.Placement.WorldClass == ItemWorldClass.Creature && g.Item.CreatureStartLevel is null)
            Refuse(InventoryEffectRejection.UnsupportedItem);
        return ValueTask.CompletedTask;
    }
    private static ItemSnapshot Baseline(OpeningPreparedGrant grant) =>
        new ItemSnapshot
        {
            ItemId = grant.Item.ItemId, LegacyKind = grant.Item.Placement.ItemKind, Type = grant.Item.Placement.Traits,
            IsPile = grant.Item.Placement.WorldClass == ItemWorldClass.Interchangeable, Count = 0,
            AvatarDetail = grant.Item.Placement.WorldClass == ItemWorldClass.Avatar
                ? new(grant.Expiration, 0, new byte[30], 0, 0, 0) : null,
            CreatureDetail = grant.Item.Placement.WorldClass == ItemWorldClass.Creature
                ? new(100, 0, 0, 0, 0, grant.Item.CreatureStartLevel!.Value, [], 0) : null,
            Attribute = grant.Reward.Ability, Durability = grant.Item.Durability,
            SealFlag = grant.Item.Placement.WorldClass == ItemWorldClass.Creature ? (byte)0 : grant.Item.Sealed ? (byte)1 : (byte)0,
            ExpireTime = grant.Item.Placement.WorldClass == ItemWorldClass.Avatar ? 0 : grant.Expiration,
        };
    private static BusinessSlot[] Candidates(ItemPlacement placement)
    {
        var result = Enumerable.Range(placement.Destination.FirstIndex, placement.Destination.LastIndex - placement.Destination.FirstIndex + 1)
            .Select(i => new BusinessSlot(placement.Destination.Space, i));
        if (placement.Destination.Space != BusinessInventorySpace.SharedMaterial && ItemSlotRuleService.AllowsQuickSlot(placement))
            result = result.Concat(Enumerable.Range(0, 6).Select(i => new BusinessSlot(BusinessInventorySpace.QuickSlot, i)));
        return result.ToArray();
    }
    private ItemArrival Arrival(OpeningPreparedGrant grant, BusinessSlot[] candidates,
        Dictionary<BusinessSlot, ItemSnapshot?> held, int remaining) =>
        arrivals.ForGrant(candidates.Where(s => held[s] is { IsPile: true } item && item.ItemId == grant.Item.ItemId
            && item.CanStackWith(Baseline(grant)))
            .Select(s => new ItemPile(s, held[s]!.Count)).ToArray(), remaining, grant.Item.Placement,
            candidates.Where(s => held[s] is not null).ToHashSet());
    private static (BusinessSlot Slot, int Added, ItemSnapshot Core) Next(
        ItemArrival arrival, OpeningPreparedGrant grant, Dictionary<BusinessSlot, ItemSnapshot?> held, bool applying = true) => arrival switch
    {
        ItemArrival.OntoExistingPile pile => (pile.Slot, pile.Added, held[pile.Slot]! with { Count = pile.Total }),
        ItemArrival.IntoEmptySlot empty => (empty.Slot, empty.Count, Baseline(grant) with
        { Count = grant.Item.Placement.WorldClass == ItemWorldClass.Interchangeable ? empty.Count : 1,
            InstanceValue = grant.Item.Placement.WorldClass == ItemWorldClass.Interchangeable ? 0 : applying ? Dfo.Server.Application.Inventory.EquipmentQualitySeeds.NewRandom() : 1 }),
        _ => throw new InventoryEffectException(InventoryEffectRejection.NoRoom),
    };
    public override async ValueTask PreviewAsync(EffectExecutionContext context, GrantPreparedItemEffect effect, IInventoryEffectPreviewState state, CancellationToken ct)
    {
        await ValidateAsync(context, effect, ct).ConfigureAwait(false);
        var placement = InventoryCapacityPlacement.Limit(effect.Grant.Item.Placement,
            await state.OpenSlotCountAsync(effect.Grant.Item.Placement.Destination.Space, ct).ConfigureAwait(false));
        if (placement is null)
        {
            if (effect.AllowMail) return;
            throw new InventoryEffectException(InventoryEffectRejection.NoRoom);
        }
        effect = effect with { Grant = effect.Grant with { Item = effect.Grant.Item with { Placement = placement } } };
        var candidates = Candidates(placement);
        var held = new Dictionary<BusinessSlot, ItemSnapshot?>();
        foreach (var s in candidates) held[s] = await state.GetAsync(s, ct).ConfigureAwait(false);
        var remaining = effect.Grant.Reward.Count;
        while (remaining > 0)
        {
            var arrival = Arrival(effect.Grant, candidates, held, remaining);
            if (arrival is ItemArrival.NoRoom && effect.AllowMail) return;
            var (slot, added, core) = Next(arrival, effect.Grant, held, applying: false);
            if (added <= 0 || added > remaining) throw new InvalidDataException("Nonprogressing prepared grant.");
            var item = core;
            held[slot] = item; state.SetSlot(slot, item); state.AddedRewardEntries(1); remaining -= added;
        }
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, GrantPreparedItemEffect effect, CancellationToken ct)
    {
        var grant = effect.Grant;
        ItemDeliveryReceipt receipt;
        try
        {
            receipt = await delivery.DeliverAsync(context.AccountId, context.CharacterId,
                [new(grant.Item.ItemId, grant.Reward.Count, Baseline(grant))], "礼包奖励", ct,
                effect.AllowMail ? ItemDeliveryMode.InventoryThenMail : ItemDeliveryMode.InventoryOnly).ConfigureAwait(false);
        }
        catch (ContainerAdditionException error) when (error.Reason == ContainerAdditionFailure.NoRoom)
        { throw new InventoryEffectException(InventoryEffectRejection.NoRoom); }
        var display = receipt.Inventory.Select(x => new OpeningRewardDisplay(x.Slot, grant.Reward.ItemId, x.Count,
            x.Snapshot.Durability, x.Snapshot.Attribute, grant.Expiration, x.Snapshot)).ToList();
        display.AddRange(receipt.MailedItems.Select(x => new OpeningRewardDisplay(null, x.ItemId, x.Count,
            x.Durability, x.Attribute, grant.Expiration, x)));
        return new PreparedItemGrantChange(display.ToImmutableArray(), receipt.Inventory.Select(x => x.Slot).Distinct().ToImmutableArray());
    }
}

internal sealed class GrantOpeningVirtualEffectHandler(ICharacterContainerDomainService containers, IItemGrantDomainService grants)
    : OpeningEffectHandler<GrantOpeningVirtualEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, GrantOpeningVirtualEffect effect, CancellationToken ct)
    {
        if (effect.Slot is < 0 or > 2 || effect.CoreItemId <= 0 || effect.Reward.Count <= 0) Refuse(InventoryEffectRejection.UnsupportedItem);
        return ValueTask.CompletedTask;
    }
    private static ItemSnapshot Add(GrantOpeningVirtualEffect effect, ItemSnapshot? item)
    {
        if (item is not null && (!item.IsPile || item.ItemId != effect.CoreItemId)) throw new InventoryEffectException(InventoryEffectRejection.UnsupportedItem);
        var total = (long)(item?.Count ?? 0) + effect.Reward.Count;
        if (total > int.MaxValue) throw new InventoryEffectException(InventoryEffectRejection.NoRoom);
        return item is null ? new ItemSnapshot { ItemId = effect.CoreItemId, LegacyKind = effect.ItemKind,
            Type = ItemTraitClassification.For(effect.ItemKind, effect.CoreItemId), IsPile = true, Count = (int)total } : item with { Count = (int)total };
    }
    public override async ValueTask PreviewAsync(EffectExecutionContext context, GrantOpeningVirtualEffect effect, IInventoryEffectPreviewState state, CancellationToken ct)
    {
        await ValidateAsync(context, effect, ct).ConfigureAwait(false);
        state.SetVirtualSlot(effect.Slot, Add(effect, await state.VirtualSlotAsync(effect.Slot, ct).ConfigureAwait(false)));
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, GrantOpeningVirtualEffect effect, CancellationToken ct)
    {
        var main = containers.FindContainer(context.CharacterId, InventoryContainerType.Main) ?? throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
        var item = await main.GetAsync(effect.Slot, ct).ConfigureAwait(false);
        var core = Add(effect, item?.Snapshot);
        await grants.ApplyPlannedAsync(main, effect.Slot, core, ct).ConfigureAwait(false);
        var slot = new BusinessSlot(BusinessInventorySpace.VirtualResource, effect.Slot);
        return new OpeningVirtualChange(slot, core, new(slot, effect.Reward.ItemId, effect.Reward.Count, 0, 0, 0));
    }
}

internal sealed class SetOpeningPremiumEffectHandler(IPremiumServiceDomainService premiums) : OpeningEffectHandler<SetOpeningPremiumEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, SetOpeningPremiumEffect effect, CancellationToken ct)
    {
        if (((int)effect.Type is < 1 or > 127) && !Enum.IsDefined(effect.Type) || effect.Type == PremiumServiceType.BlackDiamond || effect.ExpectedExpiry < 0
            || effect.NextExpiry <= effect.ExpectedExpiry || effect.NextExpiry > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            Refuse(InventoryEffectRejection.UnsupportedItem);
        return ValueTask.CompletedTask;
    }
    public override async ValueTask PreviewAsync(EffectExecutionContext context, SetOpeningPremiumEffect effect, IInventoryEffectPreviewState state, CancellationToken ct)
    {
        await ValidateAsync(context, effect, ct).ConfigureAwait(false);
        if (await state.PremiumExpiryAsync(effect.Type, ct).ConfigureAwait(false) != effect.ExpectedExpiry) Refuse(InventoryEffectRejection.InvalidTarget);
        state.SetPremiumExpiry(effect.Type, effect.NextExpiry);
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, SetOpeningPremiumEffect effect, CancellationToken ct)
    {
        await premiums.For(context.AccountId).SetExpiryAsync(effect.Type, effect.ExpectedExpiry, effect.NextExpiry, ct).ConfigureAwait(false);
        return new OpeningPremiumChange(new((int)effect.Type, effect.NextExpiry), new(null, effect.Reward.ItemId, effect.Reward.Count, 0, 0, 0));
    }
}

internal sealed class SetSeriaLuckEffectHandler(IAccountTable accounts) : OpeningEffectHandler<SetSeriaLuckEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, SetSeriaLuckEffect effect, CancellationToken ct)
    {
        if (effect.Expected is < 0 or > 8 || effect.Next is < 0 or > 8) Refuse(InventoryEffectRejection.InvalidTarget);
        return ValueTask.CompletedTask;
    }
    public override async ValueTask PreviewAsync(EffectExecutionContext context, SetSeriaLuckEffect effect, IInventoryEffectPreviewState state, CancellationToken ct)
    {
        await ValidateAsync(context, effect, ct).ConfigureAwait(false);
        if (await state.SeriaLuckAsync(ct).ConfigureAwait(false) != effect.Expected) Refuse(InventoryEffectRejection.InvalidTarget);
        state.SetSeriaLuck(effect.Next);
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, SetSeriaLuckEffect effect, CancellationToken ct)
    {
        if (await accounts.TrySetSeriaLuckAsync(context.AccountId, effect.Expected, effect.Next, ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Decided account luck changed during effect application.");
        return new SeriaLuckChange(effect.Expected, effect.Next);
    }
}

internal sealed class SetDoublePotUsageEffectHandler(IPremiumServiceUsageDomainService usage) : OpeningEffectHandler<SetDoublePotUsageEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, SetDoublePotUsageEffect effect, CancellationToken ct)
    {
        if (effect.Decision.At != context.At || effect.Decision.NextRound is < 0 or > 255 || effect.Decision.NextTotal is < 0 or > 255)
            Refuse(InventoryEffectRejection.InvalidTarget);
        return ValueTask.CompletedTask;
    }
    public override async ValueTask PreviewAsync(EffectExecutionContext context, SetDoublePotUsageEffect effect, IInventoryEffectPreviewState state, CancellationToken ct)
    {
        await ValidateAsync(context, effect, ct).ConfigureAwait(false);
        if (await state.DoublePotCountersAsync(ct).ConfigureAwait(false) != effect.Decision.Expected) Refuse(InventoryEffectRejection.InvalidTarget);
        state.SetDoublePotCounters(new(new(effect.Decision.NextRound, context.At), new(effect.Decision.NextTotal, context.At)));
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, SetDoublePotUsageEffect effect, CancellationToken ct)
    {
        await usage.ApplyDoublePotAsync(context.AccountId, context.CharacterId, effect.Decision, ct).ConfigureAwait(false);
        return new DoublePotUsageChange(effect.Decision);
    }
}
