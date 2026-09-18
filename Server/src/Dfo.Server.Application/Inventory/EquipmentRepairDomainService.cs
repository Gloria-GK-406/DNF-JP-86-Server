using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentRepairDomainService(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ICharacterDomainService characters, ICharacterContainerDomainService containers,
    IEquipmentRepairDefinitionSource definitions) : IEquipmentRepairDomainService
{
    public async ValueTask<EquipmentRepairResult> RepairAsync(SelectedSessionIdentity session,
        InventoryContainerType container, int slotIndex, CancellationToken cancellationToken)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentRepairRejection.NoSession);
        if (container is not (InventoryContainerType.Main or InventoryContainerType.Equipment
            or InventoryContainerType.PersonalCargo) || slotIndex < -1 || slotIndex > short.MaxValue)
            return new(EquipmentRepairRejection.InvalidRequest);

        var rates = await definitions.RatesAsync(cancellationToken).ConfigureAwait(false);
        if (rates is null)
            return new(EquipmentRepairRejection.CannotRepair);

        var candidates = new List<(IItemContainer Bag, EquipmentInstance Equipment, int Cost)>();
        if (slotIndex == -1)
        {
            foreach (var (type, first, last) in new[]
                { (InventoryContainerType.Equipment, 11, 22), (InventoryContainerType.Main, 3, 8) })
            {
                var bag = containers.FindContainer(session.CharacterId, type);
                if (bag is null) continue;
                for (var slot = first; slot <= last; slot++)
                {
                    var item = await bag.ReadSlotAsync(slot, cancellationToken).ConfigureAwait(false);
                    if (item is null || item.WorldClass != ItemWorldClass.Equipment) continue;
                    var candidate = await QuoteAsync(bag, item, rates.Value, cancellationToken).ConfigureAwait(false);
                    if (candidate is not null) candidates.Add(candidate.Value);
                    // An unreadable occupied equipment slot must not turn "repair all" into a partial charge.
                    else return new(EquipmentRepairRejection.CannotRepair);
                }
            }
        }
        else
        {
            var bag = containers.FindContainer(session.CharacterId, container);
            var item = bag is null ? null : await bag.ReadSlotAsync(slotIndex, cancellationToken).ConfigureAwait(false);
            if (item is null) return new(EquipmentRepairRejection.ItemUnavailable);
            if (item.WorldClass != ItemWorldClass.Equipment)
                return new(EquipmentRepairRejection.CannotRepair);
            var candidate = await QuoteAsync(bag!, item, rates.Value, cancellationToken).ConfigureAwait(false);
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
        foreach (var (bag, equipment, _) in candidates)
            if (equipment.Core.Durability != equipment.OriginalDurability)
                await bag.PutAsync(equipment.SlotIndex, equipment.Core, cancellationToken)
                    .ConfigureAwait(false);
        return new(EquipmentRepairRejection.None, new(session, container, slotIndex, gold.Value));
    }

    private async ValueTask<(IItemContainer Bag, EquipmentInstance Equipment, int Cost)?> QuoteAsync(
        IItemContainer bag, ContainerSlotItem item, (float CostRate, IReadOnlyList<float> UpgradeRates) rates,
        CancellationToken cancellationToken)
    {
        var definition = await definitions.GetAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        if (definition is null)
            return null;
        var equipment = EquipmentInstance.From(item);
        var decision = equipment?.Repair(definition, rates);
        return decision is { } repaired
            ? (bag, equipment!, repaired.Cost)
            : null;
    }
}
