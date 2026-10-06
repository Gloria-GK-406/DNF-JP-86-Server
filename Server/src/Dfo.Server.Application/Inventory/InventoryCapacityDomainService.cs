using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class InventoryCapacityDomainService(ICharacterContainerStateTable characterStates,
    IAccountCargoStateTable accountStates, IInventoryCapacityRuleService rules)
    : IInventoryCapacityDomainService
{
    public async ValueTask<int> OpenSlotCountAsync(ContainerId container, BusinessInventorySpace space,
        CancellationToken cancellationToken)
    {
        if (space == BusinessInventorySpace.SharedMaterial)
            return container.TryGetAccountOwner(out _, out var sharedType) && sharedType == InventoryContainerType.SharedMaterial
                ? rules.OpenSlotCount(space, 0) : throw new InvalidOperationException($"Container {container} is not an account's shared material container.");
        int parameter;
        if (container.TryGetAccountOwner(out var account, out var accountType)
            && accountType == InventoryContainerType.AccountCargo)
            parameter = (await accountStates.ReadByAccountAsync(account, cancellationToken)
                .ConfigureAwait(false)).SingleOrDefault()?.SelectionKey ?? 0;
        else if (container.TryGetCharacterOwner(out var character, out var type))
            parameter = (await characterStates.ReadByCharacterAsync(character, cancellationToken)
                .ConfigureAwait(false)).SingleOrDefault(row => row.ListType == (int)type)?.Value
                ?? (type == InventoryContainerType.PersonalCargo ? 8 : 0);
        else
            throw new InvalidOperationException($"Container {container} has no persistent capacity owner.");
        return rules.OpenSlotCount(space, parameter);
    }
}

internal sealed class InventoryCapacityRuleService : IInventoryCapacityRuleService
{
    // A21 avatar parameter is extra slots, not total capacity; see the NOTI-0x000D record.
    public int OpenSlotCount(BusinessInventorySpace space, int parameter) => space switch
    {
        BusinessInventorySpace.SharedMaterial => AccountMaterialCatalog.Definitions.Count,
        BusinessInventorySpace.Avatar => parameter is >= 0 and <= 105 && parameter % 7 == 0
            ? 105 + parameter : 0,
        BusinessInventorySpace.PersonalCargo => parameter == 0 ? 8
            : parameter is >= 8 and <= 200 && (parameter - 8) % 16 == 0 ? parameter : 0,
        BusinessInventorySpace.AccountCargo => parameter == 1 ? 1
            : parameter is >= 8 and <= 120 && parameter % 8 == 0 ? parameter : 0,
        BusinessInventorySpace.CarriedEquipment or BusinessInventorySpace.Consumable
            or BusinessInventorySpace.Material or BusinessInventorySpace.Quest
            or BusinessInventorySpace.ExpertMaterial => parameter is >= 0 and <= 24 && parameter % 8 == 0
                ? 32 + parameter : 0,
        BusinessInventorySpace.QuickSlot or BusinessInventorySpace.TowerQuickSlot => 6,
        BusinessInventorySpace.AvatarEmblem => 63,
        BusinessInventorySpace.CreatureInventory => 140,
        BusinessInventorySpace.CreatureEquipment => 49,
        BusinessInventorySpace.CreatureConsumable => 51,
        BusinessInventorySpace.WornAvatar => 12,
        BusinessInventorySpace.WornEquipment => 13,
        BusinessInventorySpace.WornEquipmentExtension or BusinessInventorySpace.WornCreatureEquipment => 3,
        BusinessInventorySpace.WornCreature => 1,
        BusinessInventorySpace.GuildMedal or BusinessInventorySpace.GuardianGem => 49,
        _ => 0,
    };
}

internal static class InventoryCapacityPlacement
{
    public static Dfo.Server.Abstractions.Content.ItemPlacement? Limit(
        Dfo.Server.Abstractions.Content.ItemPlacement placement, int openSlots) =>
        openSlots <= placement.Destination.FirstIndex ? null : placement with
        {
            Destination = new(placement.Destination.Space, placement.Destination.FirstIndex,
                Math.Min(placement.Destination.LastIndex, openSlots - 1)),
        };
}
