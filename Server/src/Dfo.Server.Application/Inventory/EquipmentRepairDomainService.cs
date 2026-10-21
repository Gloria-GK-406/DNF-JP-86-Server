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

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentRepairDomainService(
    ISelectedSessionDomainService sessions,
    ICharacterDomainService characters, IBusinessInventorySlotDomainService slots,
    IEquipmentDomainService equipments,
    IEquipmentRepairRuleService rules, IPremiumServiceDomainService premiums,
    IPremiumServiceUsageDomainService usage) : IEquipmentRepairDomainService
{
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

    public async ValueTask<EquipmentRepairResult> RepairAsync(SelectedSessionIdentity session,
        BusinessSlot? slot, CancellationToken cancellationToken)
    {
        if (!sessions.IsInWorld(session))
            return new(EquipmentRepairRejection.NoSession);
        var rule = await rules.PrepareAsync(cancellationToken).ConfigureAwait(false);
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
                if (candidate is not null) candidates.Add(candidate.Value);
                // An unreadable occupied equipment slot must not turn "repair all" into a partial charge.
                else return new(EquipmentRepairRejection.CannotRepair);
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

        var total = candidates.Sum(x => (long)x.Quote.Cost);
        if (total > int.MaxValue)
            return new(EquipmentRepairRejection.InsufficientGold);
        var character = characters.Get(session.CharacterId);
        if (await character.IdentityAsync(cancellationToken).ConfigureAwait(false) is null)
            return new(EquipmentRepairRejection.NoSession);
        var gold = await character.TrySpendGoldAsync((int)total, cancellationToken).ConfigureAwait(false);
        if (gold is null)
            return new(EquipmentRepairRejection.InsufficientGold);
        foreach (var (equipment, quote) in candidates)
            if (quote.Write)
                await equipment.SetDurabilityAsync(quote.Durability, cancellationToken).ConfigureAwait(false);
        return new(EquipmentRepairRejection.None, new(session, slot, gold.Value));
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
