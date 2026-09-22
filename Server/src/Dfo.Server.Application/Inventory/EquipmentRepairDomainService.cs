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
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ICharacterDomainService characters, IBusinessInventorySlotDomainService slots,
    IEquipmentRepairDefinitionSource definitions, IPremiumServiceDomainService premiums,
    IPremiumServiceUsageDomainService usage) : IEquipmentRepairDomainService
{
    // A21 repair ACK excludes title, accessories, support and magic stone. These business
    // indices project to weapon 12 and armor 14..18; ordinary NPC repair remains unchanged.
    private static readonly IReadOnlyList<BusinessSlot> AutoRepairSlots =
    [
        .. Enumerable.Range(0, 6).Select(i => new BusinessSlot(BusinessInventorySpace.QuickSlot, i)),
        .. new[] { 1, 3, 4, 5, 6, 7 }.Select(i => new BusinessSlot(BusinessInventorySpace.WornEquipment, i)),
    ];

    public async ValueTask<EquipmentRepairResult> AutoRepairAsync(SelectedSessionIdentity session,
        CancellationToken cancellationToken)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentRepairRejection.NoSession);
        var entitlement = premiums.For(session.AccountId);
        if (!await entitlement.HasBenefitAsync(PremiumBenefit.AutoRepair, cancellationToken).ConfigureAwait(false))
            return new(EquipmentRepairRejection.MissingEntitlement);

        var candidates = new List<(BoundInventorySlot Slot, InventoryItemCoreState Core)>();
        var broken = false;
        foreach (var repairSlot in AutoRepairSlots)
        {
            var bound = slots.Find(session.CharacterId, repairSlot);
            if (bound is null) continue;
            var item = await bound.Container.ReadSlotAsync(bound.StorageIndex, cancellationToken).ConfigureAwait(false);
            if (item is null || item.WorldClass != ItemWorldClass.Equipment) continue;
            var definition = await definitions.GetAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
            if (definition is null) return new(EquipmentRepairRejection.CannotRepair);
            if (definition.MaxDurability == 0) continue;
            if (item.Core.Durability > definition.MaxDurability) return new(EquipmentRepairRejection.CannotRepair);
            broken |= item.Core.Durability == 0;
            if (item.Core.Durability < definition.MaxDurability)
                candidates.Add((bound, item.Core with { Durability = definition.MaxDurability }));
        }
        // Do not trust the client's in-flight flag: stale/duplicate or forged requests must
        // not consume another use, nor turn automatic repair into discounted manual repair.
        if (!broken) return new(EquipmentRepairRejection.NoBrokenEquipment);
        if (!await usage.TryUseAutoRepairAsync(session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false))
            return new(EquipmentRepairRejection.DailyLimitReached);
        foreach (var (bound, core) in candidates)
            await bound.Container.PutAsync(bound.StorageIndex, core, cancellationToken).ConfigureAwait(false);
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
        .. Enumerable.Range(0, 12)
            .Select(index => new BusinessSlot(BusinessInventorySpace.WornEquipment, index)),
    ];

    public async ValueTask<EquipmentRepairResult> RepairAsync(SelectedSessionIdentity session,
        BusinessSlot? slot, CancellationToken cancellationToken)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentRepairRejection.NoSession);
        var rates = await definitions.RatesAsync(cancellationToken).ConfigureAwait(false);
        if (rates is null)
            return new(EquipmentRepairRejection.CannotRepair);

        var candidates = new List<(BoundInventorySlot Slot, EquipmentInstance Equipment, int Cost)>();
        if (slot is null)
        {
            foreach (var repairSlot in RepairAllSlots)
            {
                var bound = slots.Find(session.CharacterId, repairSlot);
                if (bound is null) continue;
                var item = await bound.Container.ReadSlotAsync(bound.StorageIndex, cancellationToken)
                    .ConfigureAwait(false);
                if (item is null || item.WorldClass != ItemWorldClass.Equipment) continue;
                var candidate = await QuoteAsync(bound, item, rates.Value, cancellationToken)
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
                : await bound.Container.ReadSlotAsync(bound.StorageIndex, cancellationToken).ConfigureAwait(false);
            if (item is null) return new(EquipmentRepairRejection.ItemUnavailable);
            if (item.WorldClass != ItemWorldClass.Equipment)
                return new(EquipmentRepairRejection.CannotRepair);
            var candidate = await QuoteAsync(bound!, item, rates.Value, cancellationToken).ConfigureAwait(false);
            if (candidate is null) return new(EquipmentRepairRejection.CannotRepair);
            candidates.Add(candidate.Value);
        }

        var total = candidates.Sum(x => (long)x.Cost);
        if (total > int.MaxValue)
            return new(EquipmentRepairRejection.InsufficientGold);
        var character = characters.Get(session.CharacterId);
        if (await character.IdentityAsync(cancellationToken).ConfigureAwait(false) is null)
            return new(EquipmentRepairRejection.NoSession);
        var gold = await character.TrySpendGoldAsync((int)total, cancellationToken).ConfigureAwait(false);
        if (gold is null)
            return new(EquipmentRepairRejection.InsufficientGold);
        foreach (var (bound, equipment, _) in candidates)
            if (equipment.Core.Durability != equipment.OriginalDurability)
                await bound.Container.PutAsync(bound.StorageIndex, equipment.Core, cancellationToken)
                    .ConfigureAwait(false);
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

    private async ValueTask<(BoundInventorySlot Slot, EquipmentInstance Equipment, int Cost)?> QuoteAsync(
        BoundInventorySlot slot, ContainerSlotItem item,
        (float CostRate, IReadOnlyList<float> UpgradeRates) rates,
        CancellationToken cancellationToken)
    {
        var definition = await definitions.GetAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        if (definition is null)
            return null;
        var equipment = EquipmentInstance.From(item);
        var decision = equipment?.Repair(definition, rates);
        return decision is { } repaired
            ? (slot, equipment!, repaired.Cost)
            : null;
    }
}
