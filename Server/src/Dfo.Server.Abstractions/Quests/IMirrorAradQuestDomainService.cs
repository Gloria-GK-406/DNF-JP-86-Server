using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Quests;

public sealed record MirrorQuestState(CharacterId CharacterId, DungeonRunId RunId, int QuestId,
    DateTimeOffset AcceptedAt, uint TriggerValue, bool ReviveUsed = false,
    bool RewardClaimed = false, bool GivenUp = false)
{
    public IReadOnlyList<RoomActorId> ObservedDeaths { get; init; } = [];
    public IReadOnlyList<MazeCell> ClearedCells { get; init; } = [];
    public bool TargetMapCleared { get; init; }
}
public sealed record MirrorDailyQuota(int Entries = 0, int Clears = 0, bool DailyRewardClaimed = false);

/// <summary>Run-bound state, separate from ordinary active and permanently cleared quests.</summary>
public interface IMirrorAradQuestDomainStore
{
    ValueTask<MirrorQuestState?> ReadAsync(CharacterId character, DungeonRunId run, CancellationToken ct);
    ValueTask WriteAsync(AccountId account, MirrorQuestState state, CancellationToken ct);
    ValueTask<bool> DeleteAsync(CharacterId character, DungeonRunId run, CancellationToken ct);
    ValueTask<MirrorDailyQuota> ReadQuotaAsync(CharacterId character, DateOnly day, CancellationToken ct);
    ValueTask WriteQuotaAsync(AccountId account, CharacterId character, DateOnly day, MirrorDailyQuota quota, CancellationToken ct);
}

public enum MirrorQuestKind { Hunt, Clear, ClearMap, TimedClear, Locations }
public sealed record MirrorHuntTarget(int DungeonId, int Difficulty, int ActorSelector, int EnemyType, int Count, int Channel);
public sealed record MirrorQuestObjective(QuestDefinition Quest, MirrorQuestKind Kind,
    IReadOnlyList<MirrorHuntTarget> Hunt, int MinimumDifficulty = -1, int MapId = 0,
    int LocationCount = 0, int TimeLimitSeconds = 0, bool RequireNoRevive = false)
{
    public uint InitialTrigger => Kind == MirrorQuestKind.Hunt
        ? Hunt.Aggregate(0u, (v, x) => v | ((uint)x.Count << (x.Channel * 9)))
        : Kind == MirrorQuestKind.Locations ? (uint)LocationCount : 1;
}
public interface IMirrorAradQuestRuleService
{
    MirrorDungeonCatalog Catalog { get; }
    ValueTask<MirrorQuestObjective?> ResolveAsync(int dungeonId, int questId, CancellationToken ct);
    MirrorQuestState Apply(MirrorQuestObjective objective, MirrorQuestState state, DungeonRunState run, QuestProgressFact fact);
    MirrorQuestState ApplyMapClear(MirrorQuestObjective objective, MirrorQuestState state, DungeonMapCleared clear);
    bool CanFinish(MirrorQuestObjective objective, MirrorQuestState state, DungeonRunState run);
    ValueTask<ImmutableArray<DomainEffect>?> RewardEffectsAsync(byte level, MirrorDailyQuota quota, CancellationToken ct);
}
public sealed record MirrorQuestCompletionPreparation(bool Prepared, ushort QuestId, string? Objective,
    ExperienceAward? Award, ImmutableArray<DomainEffect> Effects, QuestCompletionRewardPlan? Reward,
    MirrorQuestState? Expected, MirrorQuestState? Completed, MirrorRewardRecorded? QuotaChange, bool AlreadyClaimed = false);

public interface IMirrorAradQuestDomainService
{
    ValueTask<QuestAcceptance> AcceptAsync(AccountId account, CharacterId character, ushort quest, CancellationToken ct);
    ValueTask<QuestAdvance> TriggerAsync(AccountId account, CharacterId character, ushort quest, CancellationToken ct);
    ValueTask<MirrorQuestCompletionPreparation> PrepareCompletionAsync(AccountId account, CharacterId character, ushort quest,
        ushort selection, ushort count, DateTimeOffset at, SelectedSessionIdentity? session, CancellationToken ct);
    ValueTask CommitCompletionAsync(AccountId account, CharacterId character, MirrorQuestCompletionPreparation preparation, CancellationToken ct);
    ValueTask<bool> GiveUpAsync(AccountId account, CharacterId character, ushort quest, CancellationToken ct);
    ValueTask<bool> ReportAsync(AccountId account, CharacterId character, IReadOnlyList<QuestProgressFact> facts, CancellationToken ct);
    ValueTask<QuestProgressSnapshot> SnapshotAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask<QuestProgressSnapshot> MergeAsync(CharacterId character, QuestProgressSnapshot ordinary, CancellationToken ct);
    ValueTask ClearRunAsync(CharacterId character, DungeonRunId run, CancellationToken ct);
}
public sealed record MirrorQuestChanged(CharacterId CharacterId) : IDomainEvent;
public sealed record MirrorQuestViewChanged(CharacterId CharacterId) : INotification<MirrorQuestViewChanged>;
