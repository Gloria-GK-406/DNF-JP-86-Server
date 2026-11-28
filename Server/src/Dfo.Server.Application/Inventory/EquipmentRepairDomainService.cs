using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentRepairDomainService(
    ISelectedSessionDomainService sessions,
    ICharacterDomainService characters, ICharacterItemUseDomainService itemUse, IBusinessInventorySlotDomainService slots,
    IEquipmentDomainService equipments,
    IEquipmentRepairRuleService rules, IPremiumServiceDomainService premiums,
    IPremiumServiceUsageDomainService usage, IMaterialConsumptionDomainService materials,
    IDungeonRunQueryDomainService runs, ConsumableCooldownDomainService cooldowns, TimeProvider clock) : IEquipmentRepairDomainService
{
    public async ValueTask<EquipmentRepairResult> RepairWithCouponAsync(SelectedSessionIdentity session,
        BusinessSlot targetSlot, BusinessSlot couponSlot, CancellationToken ct)
    {
        if (!sessions.IsInWorld(session)) return new(EquipmentRepairRejection.NoSession);
        if (targetSlot.Space is not (BusinessInventorySpace.CarriedEquipment or BusinessInventorySpace.WornEquipment))
            return new(EquipmentRepairRejection.InvalidRequest);
        var at = clock.GetUtcNow();
        var (coupon, rejection) = await ReadRepairMaterialAsync(session, couponSlot, at, paidAll: false, ct)
            .ConfigureAwait(false);
        if (coupon is null) return new(rejection);
        var instance = await equipments.Of(session.CharacterId).FindAtAsync(targetSlot, ct).ConfigureAwait(false);
        var target = instance is null ? null : await instance.ReadAsync(ct).ConfigureAwait(false);
        if (target is null || target.Snapshot.IsMirror || Unavailable(target.Snapshot, at))
            return new(EquipmentRepairRejection.ItemUnavailable);
        var maximum = await rules.AutoRepairDurabilityAsync(target.Snapshot, ct).ConfigureAwait(false);
        if (maximum is null or 0 || target.Snapshot.Durability >= maximum)
            return new(EquipmentRepairRejection.CannotRepair);
        var consumed = await materials.ConsumeAsync(session, [new(couponSlot, coupon.ItemId, 1)], ct).ConfigureAwait(false);
        if (!consumed.Success) return new(EquipmentRepairRejection.ItemUnavailable);
        await instance!.SetDurabilityAsync(maximum.Value, ct).ConfigureAwait(false);
        if (coupon.Rule.Cooldown is { } cooldown)
            await cooldowns.StartAsync(session, coupon.ItemId, cooldown, at, ct).ConfigureAwait(false);
        return new(EquipmentRepairRejection.None, new(session, targetSlot, await characters.Get(session.CharacterId).GoldAsync(ct).ConfigureAwait(false))
            { Coupon = new(couponSlot, consumed.Debits.Single().Remaining) });

    }

    public async ValueTask<EquipmentRepairResult> RepairAllWithPotionAsync(SelectedSessionIdentity session,
        BusinessSlot potionSlot, CancellationToken ct)
    {
        if (!sessions.IsInWorld(session)) return new(EquipmentRepairRejection.NoSession);
        var (potion, rejection) = await ReadRepairMaterialAsync(session, potionSlot, clock.GetUtcNow(), paidAll: true, ct)
            .ConfigureAwait(false);
        return potion is null ? new(rejection) : await RepairPaidAsync(session, null, potion, ct).ConfigureAwait(false);
    }

    private async ValueTask<(RepairMaterial? Material, EquipmentRepairRejection Rejection)> ReadRepairMaterialAsync(
        SelectedSessionIdentity session, BusinessSlot slot, DateTimeOffset at, bool paidAll, CancellationToken ct)
    {
        if (slot.Space is not (BusinessInventorySpace.QuickSlot or BusinessInventorySpace.Consumable))
            return (null, EquipmentRepairRejection.InvalidRequest);
        var bound = slots.Find(session.CharacterId, slot);
        var item = bound is null ? null : await bound.Container.GetAsync(slot.Index, ct).ConfigureAwait(false);
        if (item is not { IsPile: true, Count: > 0 } || Unavailable(item.Snapshot, at))
            return (null, EquipmentRepairRejection.ItemUnavailable);
        var rule = paidAll
            ? await rules.PreparePotionAsync(item.ItemId, ct).ConfigureAwait(false)
            : await rules.PrepareCouponAsync(item.ItemId, ct).ConfigureAwait(false);
        if (rule is null) return (null, EquipmentRepairRejection.CannotRepair);
        var run = runs.Find(session.CharacterId);
        if (run is not null && (!run.ReadyParticipants.Contains(session.CharacterId)
            || run.ModeState is DungeonRunModeState.LostLand { Exploration: { } exploration } && exploration.HasExpired(at)))
            return (null, EquipmentRepairRejection.CannotRepair);
        var candidate = await itemUse.FactsAsync(session.CharacterId, rule.Cooldown, at, ct).ConfigureAwait(false);
        return candidate is not null && rule.Allows(candidate, run?.Layout.DungeonId)
            ? (new(slot, item.ItemId, rule, at), EquipmentRepairRejection.None)
            : (null, EquipmentRepairRejection.CannotRepair);
    }

    private sealed record RepairMaterial(BusinessSlot Slot, int ItemId, IEquipmentRepairCouponRule Rule, DateTimeOffset At);

    private static bool Unavailable(ItemSnapshot item, DateTimeOffset at) => item.EquipmentLockId != 0
        || item.SortLockFlag != 0 || item.Lifetime.IsExpired(at);

    // A21 repair ACK excludes title, accessories, support and magic stone. These business
    // indices project to weapon 12 and armor 14..18; ordinary NPC repair remains unchanged.
    private static readonly IReadOnlyList<BusinessSlot> AutoRepairSlots =
    [
        .. Enumerable.Range(0, 6).Select(i => new BusinessSlot(BusinessInventorySpace.QuickSlot, i)),
        .. new[] { 0, 2, 3, 4, 5, 6 }.Select(i => new BusinessSlot(BusinessInventorySpace.WornEquipment, i)),
    ];

    public async ValueTask<EquipmentRepairResult> AutoRepairAsync(SelectedSessionIdentity session,
        CancellationToken cancellationToken)
    {
        if (!sessions.IsInWorld(session))
            return new(EquipmentRepairRejection.NoSession);
        var entitlement = premiums.For(session.AccountId);
        if (!await entitlement.HasBenefitAsync(PremiumBenefit.AutoRepair, cancellationToken).ConfigureAwait(false))
            return new(EquipmentRepairRejection.MissingEntitlement);

        var candidates = new List<(IEquipmentInstance Equipment, ushort Durability)>();
        var broken = false;
        foreach (var repairSlot in AutoRepairSlots)
        {
            var bound = slots.Find(session.CharacterId, repairSlot);
            if (bound is null) continue;
            var item = await bound.Container.GetAsync(bound.Slot.Index, cancellationToken).ConfigureAwait(false);
            if (item is null || (item.IsPile || !item.Type.HasFlag(ItemTraits.Equipment))) continue;
            var instance = await equipments.Of(session.CharacterId).FindAtAsync(repairSlot, cancellationToken).ConfigureAwait(false);
            if (instance is null) continue;
            var facts = await instance.ReadAsync(cancellationToken).ConfigureAwait(false);
            var durability = await rules.AutoRepairDurabilityAsync(facts.Snapshot, cancellationToken).ConfigureAwait(false);
            if (durability is null) return new(EquipmentRepairRejection.CannotRepair);
            broken |= durability > 0 && facts.Snapshot.Durability == 0;
            if (facts.Snapshot.Durability < durability)
                candidates.Add((instance, durability.Value));
        }
        // Do not trust the client's in-flight flag: stale/duplicate or forged requests must
        // not consume another use, nor turn automatic repair into discounted manual repair.
        if (!broken) return new(EquipmentRepairRejection.NoBrokenEquipment);
        if (!await usage.TryUseAutoRepairAsync(session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false))
            return new(EquipmentRepairRejection.DailyLimitReached);
        foreach (var (equipment, durability) in candidates)
            await equipment.SetDurabilityAsync(durability, cancellationToken).ConfigureAwait(false);
        var gold = await characters.Get(session.CharacterId).GoldAsync(cancellationToken).ConfigureAwait(false);
        return new(EquipmentRepairRejection.None, new(session, null, gold))
        {
            Entitlements = new AccountEntitlementEntryState(await entitlement.ReadAsync(cancellationToken).ConfigureAwait(false)),
            Usage = await usage.ReadAsync(session.CharacterId, cancellationToken).ConfigureAwait(false),
        };
    }

    private static readonly IReadOnlyList<BusinessSlot> RepairAllSlots =
    [
        .. Enumerable.Range(0, 6)
            .Select(index => new BusinessSlot(BusinessInventorySpace.QuickSlot, index)),
        .. Enumerable.Range(0, 13)
            .Select(index => new BusinessSlot(BusinessInventorySpace.WornEquipment, index)),
    ];

    public ValueTask<EquipmentRepairResult> RepairAsync(SelectedSessionIdentity session,
        BusinessSlot? slot, CancellationToken cancellationToken) => RepairPaidAsync(session, slot, null, cancellationToken);

    private async ValueTask<EquipmentRepairResult> RepairPaidAsync(SelectedSessionIdentity session,
        BusinessSlot? slot, RepairMaterial? potion, CancellationToken cancellationToken)
    {
        if (!sessions.IsInWorld(session))
            return new(EquipmentRepairRejection.NoSession);
        var rule = await rules.PrepareAsync(cancellationToken, roundPrice: potion is not null).ConfigureAwait(false);
        if (rule is null)
            return new(EquipmentRepairRejection.CannotRepair);

        var candidates = new List<(IEquipmentInstance Equipment, EquipmentRepairQuote Quote)>();
        if (slot is null)
        {
            foreach (var repairSlot in RepairAllSlots)
            {
                var bound = slots.Find(session.CharacterId, repairSlot);
                if (bound is null) continue;
                var item = await bound.Container.GetAsync(bound.Slot.Index, cancellationToken)
                    .ConfigureAwait(false);
                if (item is null || (item.IsPile || !item.Type.HasFlag(ItemTraits.Equipment))) continue;
                var candidate = await QuoteAsync(session.CharacterId, repairSlot, rule, cancellationToken)
                    .ConfigureAwait(false);
                // An unreadable occupied equipment slot must not turn "repair all" into a partial charge.
                if (candidate is null) return new(EquipmentRepairRejection.CannotRepair);
                if (potion is not null && candidate.Value.Quote.Write
                    && (item.Snapshot.IsMirror || Unavailable(item.Snapshot, potion.At)))
                    return new(EquipmentRepairRejection.CannotRepair);
                candidates.Add(candidate.Value);
            }
        }
        else
        {
            if (!IsRepairable(slot.Value.Space))
                return new(EquipmentRepairRejection.InvalidRequest);
            var bound = slots.Find(session.CharacterId, slot.Value);
            var item = bound is null
                ? null
                : await bound.Container.GetAsync(bound.Slot.Index, cancellationToken).ConfigureAwait(false);
            if (item is null) return new(EquipmentRepairRejection.ItemUnavailable);
            if ((item.IsPile || !item.Type.HasFlag(ItemTraits.Equipment)))
                return new(EquipmentRepairRejection.CannotRepair);
            var candidate = await QuoteAsync(session.CharacterId, slot.Value, rule, cancellationToken).ConfigureAwait(false);
            if (candidate is null) return new(EquipmentRepairRejection.CannotRepair);
            candidates.Add(candidate.Value);
        }

        if (potion is not null && !candidates.Any(x => x.Quote.Write))
            return new(EquipmentRepairRejection.NoBrokenEquipment);
        var total = candidates.Sum(x => (long)x.Quote.Cost);
        if (total > int.MaxValue)
            return new(EquipmentRepairRejection.InsufficientGold);
        var character = characters.Get(session.CharacterId);
        if (await character.IdentityAsync(cancellationToken).ConfigureAwait(false) is null)
            return new(EquipmentRepairRejection.NoSession);
        var gold = await character.TrySpendGoldAsync((int)total, cancellationToken).ConfigureAwait(false);
        if (gold is null)
            return new(EquipmentRepairRejection.InsufficientGold);
        EquipmentRepairCouponChange? materialChange = null;
        if (potion is not null)
        {
            var consumed = await materials.ConsumeAsync(session, [new(potion.Slot, potion.ItemId, 1)], cancellationToken)
                .ConfigureAwait(false);
            if (!consumed.Success)
                throw new InvalidOperationException("Repair potion changed during settlement.");
            materialChange = new(potion.Slot, consumed.Debits.Single().Remaining);
        }
        foreach (var (equipment, quote) in candidates)
            if (quote.Write)
                await equipment.SetDurabilityAsync(quote.Durability, cancellationToken).ConfigureAwait(false);
        if (potion?.Rule.Cooldown is { } cooldown)
            await cooldowns.StartAsync(session, potion.ItemId, cooldown, potion.At, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<EquipmentRepairBagSlot> carriedEquipment = [];
        if (potion is not null && slots.Find(session.CharacterId, new(BusinessInventorySpace.CarriedEquipment, 0)) is { } bag)
        {
            var carried = await bag.Container.GetAllAsync(cancellationToken).ConfigureAwait(false);
            carriedEquipment = carried.Where(item => item.Type.HasFlag(ItemTraits.Equipment))
                .Select(item => new EquipmentRepairBagSlot(new(BusinessInventorySpace.CarriedEquipment, item.SlotIndex), item.Snapshot))
                .ToArray();
        }
        return new(EquipmentRepairRejection.None, new(session, slot, gold.Value)
        {
            Coupon = materialChange,
            CarriedEquipment = carriedEquipment,
        });
    }

    private static bool IsRepairable(BusinessInventorySpace space) =>
        space.IsCarriedItemSpace()
        || space == BusinessInventorySpace.PersonalCargo
        || space is BusinessInventorySpace.WornAvatar
            or BusinessInventorySpace.WornEquipment
            or BusinessInventorySpace.WornCreature
            or BusinessInventorySpace.WornCreatureEquipment
            or BusinessInventorySpace.WornEquipmentExtension;

    private async ValueTask<(IEquipmentInstance Equipment, EquipmentRepairQuote Quote)?> QuoteAsync(
        Dfo.Server.Data.Characters.CharacterId owner, BusinessSlot slot, IEquipmentRepairRule rule,
        CancellationToken cancellationToken)
    {
        var equipment = await equipments.Of(owner).FindAtAsync(slot, cancellationToken).ConfigureAwait(false);
        if (equipment is null) return null;
        var facts = await equipment.ReadAsync(cancellationToken).ConfigureAwait(false);
        var quote = await rule.QuoteAsync(facts.Snapshot, cancellationToken).ConfigureAwait(false);
        return quote is null ? null : (equipment, quote);
    }
}
