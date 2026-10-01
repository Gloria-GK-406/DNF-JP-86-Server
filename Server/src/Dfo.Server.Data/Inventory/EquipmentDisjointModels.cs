using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

/// <summary>The authority whose rules turn one piece of equipment into materials.</summary>
public enum EquipmentDisjointMethod
{
    SystemNpc = 0,
    ExpertMachine = 1,
}

/// <summary>
/// The variable part of a disjoint operation. System disjoint ignores the expert fields; a
/// player machine can later use them without changing inventory settlement or the wire command.
/// </summary>
public sealed record EquipmentDisjointSettings(
    EquipmentDisjointMethod Method,
    int ExpertLevel = 0,
    int MachineGrade = 0,
    int MachineState = 0)
{
    public static EquipmentDisjointSettings System { get; } = new(EquipmentDisjointMethod.SystemNpc);
}

public sealed record EquipmentDisjointReward(int ItemId, int Count);

/// <summary>
/// The equipment-instance facts a disjoint strategy may inspect. Keeping the complete core here
/// leaves reinforcement/amplification available to future expert-machine rules without giving a
/// rule the inventory slot or any ability to mutate it.
/// </summary>
public sealed record EquipmentDisjointCandidate(int ItemId, InventoryItemCoreState Core);

public enum EquipmentDisjointRejection
{
    None,
    NoSession,
    InvalidRequest,
    ItemUnavailable,
    CannotDisjoint,
    InventoryFull,
    UnsupportedMethod,
}

public sealed record EquipmentDisjointRuleResult(
    EquipmentDisjointRejection Rejection,
    IReadOnlyList<EquipmentDisjointReward>? Rewards = null);

public sealed record EquipmentDisjointMaterialReceipt(BusinessSlot Slot, int ItemId, int Count);

/// <summary>The committed final state of one slot changed by equipment disassembly.</summary>
/// <remarks>
/// A null core means that the slot is empty.  The disassembly acknowledgement reports the
/// operation result, but it does not carry every changed slot (notably the portable machine), so
/// the protocol projection also needs these authoritative post-commit states.
/// </remarks>
public sealed record EquipmentDisjointSlotChange(
    BusinessSlot Slot,
    InventoryItemCoreState? Core);

public sealed record EquipmentDisjointReceipt(
    SelectedSessionIdentity Session,
    BusinessSlot TargetSlot,
    IReadOnlyList<EquipmentDisjointMaterialReceipt> Materials,
    IReadOnlyList<AccountMaterialChange> AccountMaterials,
    IReadOnlyList<EquipmentDisjointSlotChange> InventoryChanges,
    DateTimeOffset PreparedAt,
    BusinessSlot? PortableToolSlot = null);

public sealed record EquipmentDisjointResult(
    EquipmentDisjointRejection Rejection,
    EquipmentDisjointReceipt? Receipt = null);
