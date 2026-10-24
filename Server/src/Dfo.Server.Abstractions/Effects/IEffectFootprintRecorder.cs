using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Effects;

/// <summary>One invocation's explicit authority to report its domain footprint.</summary>
public interface IEffectFootprintRecorder
{
    void DemandActive(SelectedSessionIdentity owner);
    void RecordInventorySlots(SelectedSessionIdentity owner, IReadOnlyList<BusinessSlot> slots);
    void RecordRentalState(SelectedSessionIdentity owner);
}
