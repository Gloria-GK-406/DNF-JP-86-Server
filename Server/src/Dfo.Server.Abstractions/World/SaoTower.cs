using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public sealed record SaoTowerFloor(int Floor, int DungeonId, int MinimumLevel,
    IReadOnlyList<DungeonRequiredItem> Tickets);
public sealed record SaoTowerCatalog(IReadOnlyList<SaoTowerFloor> Floors, int FirstClearRewardItemId);
public interface ISaoTowerContentSource { SaoTowerCatalog Load(); }
public interface ISaoTowerRuleService
{
    bool Supports(int dungeonId);
    SaoTowerCatalog Catalog { get; }
    SaoTowerFloor Floor(int floor);
}

/// <summary>One server-wide progression per activity season, never per character.</summary>
public sealed record SaoTowerProgress(string Season, int Round = 1, int NextFloor = 1, long Revision = 0);
public sealed record SaoTowerEntryPlan(SaoTowerProgress Progress);
public interface ISaoTowerDomainStore
{
    ValueTask<SaoTowerProgress> ReadAsync(string season, CancellationToken ct);
    ValueTask<bool> TryClearAsync(SaoTowerProgress expected, Guid runId, int maximumFloor, CancellationToken ct);
    ValueTask RecordRewardAsync(Guid runId, CharacterId character, int itemId, CancellationToken ct);
}

public sealed record SaoTowerRewardRefresh(CharacterId CharacterId, ImmutableArray<BusinessSlot> Slots);
public sealed record SaoTowerCleared(string Season, Guid RunId,
    ImmutableArray<SaoTowerRewardRefresh> Rewards) : IDomainEvent;

public interface ISaoTowerDomainService : IDungeonBossClearHook
{
    bool Supports(int dungeonId);
    ValueTask<SaoTowerProgress?> ReadStatusAsync(CancellationToken ct);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
}
