using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.World;

public sealed record DespairTowerFloor(int Floor, int DungeonId, int DisplayApcId, IReadOnlyList<DungeonRequiredItem> Tickets);
public sealed record DespairTowerCatalog(int MinimumLevel, int MaximumDailyClears, IReadOnlyList<DespairTowerFloor> Floors);
public interface IDespairTowerContentSource { DespairTowerCatalog Load(); }
public sealed record DespairTowerProgress(int HighestClearedFloor = 0);
public interface IDespairTowerDomainStore
{
    ValueTask<DespairTowerProgress> ReadAsync(CharacterId character, CancellationToken ct);
    ValueTask<bool> TryAdvanceAsync(CharacterId character, int expectedFloor, Guid runId, CancellationToken ct);
}
public sealed record DespairTowerEntryPlan(int Floor, DateOnly Period);
public sealed record DespairTowerReward(int ItemId, int Count, ushort Durability = 0);
public sealed record DespairTowerInventoryUpdate(BusinessSlot Slot, ItemSnapshot Item);
public sealed record DespairTowerSettlement(int Floor, uint ClearTimeMilliseconds, ImmutableArray<DespairTowerReward> Rewards,
    ImmutableArray<DespairTowerInventoryUpdate> InventoryUpdates);
public interface IDespairTowerRuleService
{
    bool IsTower(int dungeonId);
    DespairTowerCatalog Catalog { get; }
    DespairTowerFloor? NextFloor(DespairTowerProgress progress);
    IReadOnlyList<DespairTowerReward> Rewards(int floor, int characterLevel, int basisLevel, ulong seed);
}
public interface IDespairTowerDomainService
{
    bool IsTower(int dungeonId);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
    ValueTask<DespairTowerSettlement?> SettleAsync(AccountId account, CharacterId character, DungeonRunId runId, CancellationToken ct);
}
