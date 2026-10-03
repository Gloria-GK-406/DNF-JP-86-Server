using System.Collections.Immutable;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World.IllusionTower;

public enum TowerRoomPhase { Loading, Ready, Fighting, Cleared }
public enum TowerEndReason { Completed, Defeated, Abandoned, Disconnected }
public sealed record TowerActionContext(SelectedSessionIdentity Session, Guid RunId, long RoomVersion);
public sealed record TowerEntryMember(SelectedSessionIdentity Session, byte Slot);
public sealed record TowerMember(SelectedSessionIdentity Session, byte Slot, bool IsLeader, bool Dead);
public sealed record TowerActor(ushort Uid, TowerActorSeed Seed, bool Dead);
public sealed record TowerObject(ushort Uid, TowerObjectDeclaration Seed);
public sealed record TowerRoomSnapshot(int Level, int MapId, uint Seed, long Version,
    TowerRoomPhase Phase, ImmutableArray<TowerActor> Actors, ImmutableArray<TowerObject> Objects,
    ImmutableHashSet<CharacterId> LoadedMembers, DateTimeOffset? StartedAt = null, DateTimeOffset? ClearedAt = null);
public sealed record TowerRewardItem(int ItemId, int Count);
public sealed record TowerRewardCandidate(int ItemId, int DisplayValue, ushort Durability = 0)
{
    public bool Delivered { get; init; }
}
public sealed record TowerInventoryUpdate(BusinessSlot Slot, ItemSnapshot Item);
public sealed record TowerKillReward(SelectedSessionIdentity Session, byte Slot, ExperienceAward Award,
    QuestProgressSnapshot? QuestProgress)
{
    public ImmutableArray<TowerInventoryUpdate> InventoryUpdates { get; init; } = [];
}
public sealed record TowerMemberSettlementReceipt(CharacterId CharacterId, uint Experience, int Gold, ImmutableArray<TowerRewardItem> Items)
{
    public byte Slot { get; init; }
    public SelectedSessionIdentity? Session { get; init; }
    public ExperienceAward? Award { get; init; }
    public int GoldBalance { get; init; }
    public ImmutableArray<TowerRewardCandidate> Candidates { get; init; } = [];
    public ImmutableArray<TowerInventoryUpdate> InventoryUpdates { get; init; } = [];
    public QuestProgressSnapshot? QuestProgress { get; init; }
}
public sealed record TowerSettlementReceipt(ImmutableArray<TowerMemberSettlementReceipt> Members);
public sealed record TowerCompletion(TowerEndReason Reason, int CompletedLevels, int RewardLevel, bool Settled,
    DateTimeOffset CompletedAt, TowerSettlementReceipt? Receipt = null);
public sealed record IllusionTowerSnapshot(Guid RunId, int DungeonId, ImmutableArray<TowerMember> Members,
    TowerRoomSnapshot Room, ImmutableArray<int> CompletedLevels, TowerCompletion? Completion, DateTimeOffset StartedAt)
{
    public int TotalLevels { get; init; }
    public byte? ReturnOption { get; init; }
    public DateTimeOffset? ReturnDueAt { get; init; }
}
public sealed record TowerMemberDebit(SelectedSessionIdentity Session, IReadOnlyList<MaterialDebit> Debits);
public sealed record TowerEntryResult(string? Rejection, IllusionTowerSnapshot? Run,
    IReadOnlyList<TowerMemberDebit> Debits);
// Facts are provisional until the enclosing command transaction commits.
public sealed record TowerActionResult(bool Changed, IllusionTowerSnapshot? Run, TowerActor? KilledActor = null)
{
    public ImmutableArray<TowerKillReward> KillRewards { get; init; } = [];
    public bool SettlementCreated { get; init; }
}

public interface IIllusionTowerDomainService
{
    IllusionTowerSnapshot? Find(SelectedSessionIdentity session);
    bool IsInTower(CharacterId characterId);
    bool TryReserveOrdinaryEntry(IReadOnlyList<CharacterId> members);
    ValueTask<TowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members,
        SelectedSessionIdentity leader, uint seed, CancellationToken cancellationToken);
    ValueTask<TowerActionResult> LoadAsync(TowerActionContext context, CancellationToken cancellationToken);
    ValueTask<TowerActionResult> StartAsync(TowerActionContext context, CancellationToken cancellationToken);
    ValueTask<TowerActionResult> KillAsync(TowerActionContext context, ushort actorUid, CancellationToken cancellationToken);
    ValueTask<TowerActionResult> NextLevelAsync(TowerActionContext context, CancellationToken cancellationToken);
    ValueTask<TowerActionResult> DieAsync(TowerActionContext context, CancellationToken cancellationToken);
    ValueTask<TowerActionResult> LeaveAsync(TowerActionContext context, bool disconnected, CancellationToken cancellationToken);
    ValueTask<TowerActionResult> SynchronizeLeaderAsync(TowerActionContext context,
        SelectedSessionIdentity leader, CancellationToken cancellationToken);
    ValueTask<bool> CanContinueAsync(TowerActionContext context, CancellationToken cancellationToken);
    ValueTask<TowerActionResult> MarkSettledAsync(TowerActionContext context, TowerSettlementReceipt receipt, CancellationToken cancellationToken);
    ValueTask<TowerActionResult> SelectReturnAsync(TowerActionContext context, byte option, CancellationToken cancellationToken);
    IReadOnlyList<TowerActionContext> FindReturnsDue(DateTimeOffset now);
    ValueTask<bool> SupportsDungeonAsync(int dungeonId, CancellationToken cancellationToken);
}

public interface IIllusionTowerDepartureSink
{
    ValueTask DepartedAsync(TowerActionResult action, CancellationToken cancellationToken);
}
