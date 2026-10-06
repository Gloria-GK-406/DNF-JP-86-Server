using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Creatures;

/// <summary>A same-UID creature item change staged inside the caller's domain transaction.</summary>
public sealed record CreatureTransformationReceipt(
    CreatureState Before,
    CreatureState After,
    BusinessSlot Slot,
    ItemSnapshot Core)
{
    public int CreatureId { get; init; }
}
