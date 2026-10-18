using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Data.Marriage;

public enum MarriageCapsuleAction { Make, Give, Take }
public sealed record MarriageCapsuleState(int Energy = 0, byte Owned = 0, byte Pending = 0);
public sealed record MarriageCapsuleSnapshot(MarriageCapsuleState State, bool AwaitingPartner = false);
public sealed record MarriageCapsuleItem(BusinessSlot Slot, ItemSnapshot Snapshot);
public sealed record MarriageCapsuleResult(int Status)
{
    public MarriageCapsuleState State { get; init; } = new();
    public CharacterId? Partner { get; init; }
    public MarriageCapsuleSnapshot? PartnerState { get; init; }
    public IReadOnlyList<MarriageCapsuleItem> Items { get; init; } = [];
}
public sealed class MarriageCapsuleRejectedException(int status) : Exception("Capsule transaction refused.")
{ public int Status { get; } = status; }
