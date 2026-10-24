using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Effects;

public sealed record EffectFootprintOwner(AccountId AccountId, CharacterId CharacterId, SelectedSessionIdentity? Session);

/// <summary>One invocation's explicit authority to report its domain footprint.</summary>
public interface IEffectFootprintRecorder
{
    void DemandEffects(EffectFootprintOwner owner);
    void RecordEffects(EffectFootprintOwner owner, IReadOnlyList<AppliedEffect> applied);
    void DemandActive(SelectedSessionIdentity owner);
    void RecordInventorySlots(SelectedSessionIdentity owner, IReadOnlyList<BusinessSlot> slots);
    void RecordRentalState(SelectedSessionIdentity owner);
}
