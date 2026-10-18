using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Data.Marriage;

public sealed record HousingStorageItem(int Slot, int ItemId, int Count);
public sealed record HousingPlacement(int Slot, int ItemId);
public sealed record HousingTree(Guid PlantingId, int Kind, DateTimeOffset PlantedAt, DateTimeOffset ExpiresAt,
    int Growth, int HarvestRound, long WaterPeriod, int Waters, long Revision);
public sealed record HousingReward(CharacterId Character, int ItemId, int Count);
/// <summary>Raw PVF modifiers for an effective-stat consumer; never persisted into base stats.</summary>
public sealed record HousingBenefits(int DecorationValue, byte RingLevel, IReadOnlyDictionary<string, int> Modifiers);
public sealed record HousingSnapshot(Guid RelationId, int Instance, IReadOnlyList<HousingStorageItem> Storage,
    IReadOnlyList<HousingPlacement> Placements, HousingTree? Tree);
public enum HousingAction { Storage, Move, Delete, Plant, Water, Harvest, Clear, UpgradeRing }
public sealed record HousingEndpoint(byte Kind, int Slot, int ItemId);
public sealed record HousingRequest(HousingAction Action, int Slot = 0, int ItemId = 0, int Count = 0,
    HousingEndpoint? Source = null, HousingEndpoint? Destination = null);
public sealed record HousingResult(int Status)
{
    public HousingSnapshot? Snapshot { get; init; }
    public IReadOnlyList<CharacterId> Audience { get; init; } = [];
    public IReadOnlyList<HousingReward> Rewards { get; init; } = [];
    public BusinessSlot? ConsumedSlot { get; init; }
    public ItemSnapshot? Remaining { get; init; }
    public byte RingLevel { get; init; }
}
