using Dfo.Server.Abstractions.Effects;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World.BossTower;

public sealed record BossTowerGroundItem(ushort Uid, ushort ActorUid, RolledDrop Rolled, bool Claimed = false);
public sealed record BossTowerActionContext(SelectedSessionIdentity Session, Guid RunId, long RoomVersion);
public sealed record BossTowerSnapshot(Guid RunId, int DungeonId, ImmutableArray<TowerMember> Members,
    TowerRoomSnapshot Room, ImmutableArray<int> CompletedLevels, TowerCompletion? Completion, DateTimeOffset StartedAt)
{
    public int TotalLevels { get; init; } = 8;
    public int CurrentStage => Room.Level;
    public int LastClearedStage => CompletedLevels.IsEmpty ? 0 : CompletedLevels.Max();
    public ImmutableArray<int> NpcIds { get; init; } = [];
    public ImmutableArray<BossTowerGroundItem> GroundItems { get; init; } = [];
    public byte? PendingResultChoice { get; init; }
    public DateTimeOffset LoadDueAt { get; init; }
    public DateTimeOffset? ReturnDueAt { get; init; }
}
public sealed record BossTowerEntryResult(string? Rejection, BossTowerSnapshot? Run,
    IReadOnlyList<DungeonEntryInventoryChange> InventoryChanges)
{
    public byte? BlockedSlot { get; init; }
    public DungeonEntryFailure? Failure { get; init; }
}
public sealed record BossTowerActionResult(bool Changed, BossTowerSnapshot? Run, TowerActor? KilledActor = null)
{
    public ImmutableArray<TowerKillReward> KillRewards { get; init; } = [];
    public ImmutableArray<BossTowerGroundItem> Drops { get; init; } = [];
    public bool SettlementCreated { get; init; }
    public ImmutableArray<TowerInventoryUpdate> InventoryUpdates { get; init; } = [];
    public int? GoldBalance { get; init; }
    public Dfo.Server.Abstractions.Quests.QuestProgressSnapshot? QuestProgress { get; init; }
}
public sealed record BossTowerPickupResult(BossTowerActionResult Action, ushort GroundUid, bool NoRoom = false);
public interface IBossTowerDomainService
{
    bool Supports(int dungeonId);
    BossTowerSnapshot? Find(SelectedSessionIdentity session);
    bool IsInTower(CharacterId characterId);
    ValueTask<BossTowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members,
        SelectedSessionIdentity leader, uint seed, CancellationToken ct, byte requestMode = 0,
        byte difficulty = 0, byte practiceMode = 0);
    ValueTask<BossTowerActionResult> LoadAsync(BossTowerActionContext context, CancellationToken ct);
    ValueTask<BossTowerActionResult> StartAsync(BossTowerActionContext context, CancellationToken ct);
    ValueTask<BossTowerActionResult> KillAsync(BossTowerActionContext context, ushort uid, IEffectFootprintRecorder footprint, CancellationToken ct);
    ValueTask<BossTowerActionResult> NextLevelAsync(BossTowerActionContext context, byte targetX, byte targetY, CancellationToken ct);
    ValueTask<BossTowerPickupResult> PickupAsync(BossTowerActionContext context, ushort uid, CancellationToken ct);
    ValueTask<BossTowerActionResult> DieAsync(BossTowerActionContext context, IEffectFootprintRecorder footprint, CancellationToken ct);
    ValueTask<BossTowerActionResult> LeaveAsync(BossTowerActionContext context, bool disconnected, IEffectFootprintRecorder footprint, CancellationToken ct);
    ValueTask<BossTowerActionResult> SynchronizeLeaderAsync(BossTowerActionContext context, SelectedSessionIdentity leader, CancellationToken ct);
    ValueTask<bool> CanRetryAsync(BossTowerActionContext context, CancellationToken ct);
    ValueTask<BossTowerActionResult> SelectResultAsync(BossTowerActionContext context, byte option, CancellationToken ct);
    ValueTask<BossTowerActionResult> ReleaseForSelectionAsync(BossTowerActionContext context, int requestedDungeonId, CancellationToken ct);
    IReadOnlyList<BossTowerActionContext> FindReturnsDue(DateTimeOffset now);
    bool CanShop(SelectedSessionIdentity session, int npcId, int shopId);
}
public interface IBossTowerDepartureSink
{
    ValueTask DepartedAsync(BossTowerActionResult action, CancellationToken ct);
}
public interface IBossTowerRewardRuleService
{
    ValueTask<uint> KillExperienceAsync(int characterLevel, byte actorType, CancellationToken ct);
    ValueTask<uint> ClearExperienceAsync(int characterLevel, int stage, CancellationToken ct);
    ValueTask<ImmutableArray<TowerRewardCandidate>> CandidatesAsync(int characterLevel, int stage, ulong seed, CancellationToken ct);
}
public interface IBossTowerRewardDomainService
{
    ValueTask<ImmutableArray<TowerKillReward>> KillAsync(BossTowerSnapshot run, TowerActor actor, IEffectFootprintRecorder footprint, CancellationToken ct);
    ValueTask<TowerSettlementReceipt> SettleAsync(BossTowerSnapshot run, ulong seed, IEffectFootprintRecorder footprint, CancellationToken ct);
}
