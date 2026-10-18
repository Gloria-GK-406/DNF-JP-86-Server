using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public enum AltarStatus { AwaitingReady, Fighting, ChoosingDifficulty, AwaitingRoom, Ranking, RewardDisplay, Awarded }
public sealed record AltarEntryPlan(AltarDungeonKind Kind, DateOnly Period);
public sealed record AltarSpawn(ushort Sequence, int MonsterCode, byte ActorType, byte Level, ushort Scale,
    ushort X, ushort Y, ushort Z, byte Variant = 19);
public sealed record AltarScheduledWave(int OffsetMilliseconds, IReadOnlyList<AltarSpawn> Actors)
{
    public ushort SpawnTail { get; init; }
}
public sealed record AltarParticipantEntry(DungeonRunParticipant Participant, int Level, int EntryCount, int AdmissionGold);
public sealed record AltarReward(int ItemId, int Count, ushort Durability = 0);
public sealed record AltarParticipantReward(DungeonRunParticipant Participant, uint Experience, int Gold, IReadOnlyList<AltarReward> Items)
{
    public IReadOnlyList<AltarReward> Cards { get; init; } = [];
}
public sealed record AltarDungeonState(DungeonRunId RunId, AltarDungeonDefinition Definition,
    IReadOnlyList<AltarParticipantEntry> Participants, DateTimeOffset StartedAt, long RoomVersion, int MapId)
{
    public AltarStatus Status { get; init; } = AltarStatus.AwaitingReady;
    public long Generation { get; init; }
    public int CompletedRounds { get; init; }
    public int LocalRound { get; init; }
    public byte Difficulty { get; init; } = 1;
    public int DifficultyPoints { get; init; }
    public int NextActorSequence { get; init; } = 1;
    public int NextWave { get; init; }
    public DateTimeOffset? RoundStartedAt { get; init; }
    public DateTimeOffset? NextDeadline { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public bool Failed { get; init; }
    public bool PreparationRequested { get; init; }
    public System.Collections.Immutable.ImmutableHashSet<CharacterId> SettledParticipants { get; init; } = [];
    public IReadOnlyList<AltarScheduledWave> Waves { get; init; } = [];
    public IReadOnlyList<AltarParticipantReward> Rewards { get; init; } = [];
    public IReadOnlyDictionary<CharacterId, int> Revivals { get; init; } = new Dictionary<CharacterId, int>();
}
public sealed record AltarDeadline(DungeonRunId RunId, long RoomVersion, long Generation, DateTimeOffset At);
public sealed record AltarQuotaSnapshot(int DungeonId, DateOnly Period, int Used, int Remaining, int Maximum);
public sealed record AltarParticipantAward(DungeonRunParticipant Participant, ExperienceAward? Experience,
    CharacterSkillProtocolMirrorState? SkillPoints, ItemDeliveryReceipt? Delivery, int GoldBalance);
public sealed record AltarActionResult(AltarDungeonState State)
{
    public IReadOnlyList<AltarSpawn> Spawns { get; init; } = [];
    public ushort SpawnTail { get; init; }
    public bool RoundStarted { get; init; }
    public bool DifficultyPrompt { get; init; }
    public bool DifficultyResolved { get; init; }
    public bool ShowRanking { get; init; }
    public bool ShowRewards { get; init; }
    public bool RoomCleared { get; init; }
    public IReadOnlyList<AltarParticipantAward> Awards { get; init; } = [];
}
public interface IAltarDungeonDomainService
{
    bool Supports(int dungeonId);
    AltarDungeonState? Find(DungeonRunId runId);
    IReadOnlyList<AltarDeadline> Pending();
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
    ValueTask<AltarActionResult?> ReadyAsync(SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, CancellationToken ct);
    ValueTask<AltarActionResult?> AdvanceAsync(DungeonRunId runId, long roomVersion, long generation, DateTimeOffset now, CancellationToken ct);
    ValueTask<AltarActionResult?> SelectDifficultyAsync(SelectedSessionIdentity session, DungeonRunId runId, long roomVersion,
        long generation, byte difficulty, CancellationToken ct);
    ValueTask<AltarActionResult?> FailAsync(DungeonRunId runId, CancellationToken ct);
    ValueTask<AltarActionResult?> SettleBeforeExitAsync(DungeonRunId runId, CancellationToken ct, CharacterId? participant = null);
    ValueTask<IReadOnlyList<AltarQuotaSnapshot>> ReadQuotaAsync(CharacterId characterId, CancellationToken ct);
    bool CanRevive(CharacterId characterId, DungeonRunId runId);
    bool StageRevival(CharacterId characterId, DungeonRunId runId);
}

public sealed class AltarDungeonPolicy
{
    public TimeSpan DifficultySelectionWindow { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan RankingWindow { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan RewardDisplayWindow { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan RoundInterval { get; init; } = TimeSpan.FromSeconds(3);
    // Empty means no configured daily opening window; historical schedules are not guessed.
    public IReadOnlyList<AltarOpeningWindow> OpeningWindows { get; init; } = [];
}
public sealed record AltarOpeningWindow(int DungeonId, TimeOnly Opens, TimeOnly Closes);
public interface IAltarDungeonRuleService
{
    ValueTask<AltarDungeonDefinition> GetDefinitionAsync(int dungeonId, CancellationToken ct);
    AltarDungeonPolicy Policy { get; }
    bool IsOpen(int dungeonId, DateTimeOffset now);
    int AdmissionGold(AltarDungeonDefinition definition, int level, int previousEntries);
    IReadOnlyList<AltarScheduledWave> PlanRound(AltarMapDefinition map, int localRound, byte difficulty,
        int completedRounds, byte level, int firstActorSequence);
    IReadOnlyList<AltarParticipantReward> Rewards(AltarDungeonState state, int seed);
}
public sealed record AltarChallengeCompleted(DungeonRunId RunId, int DungeonId, DungeonDifficulty Difficulty,
    DateTimeOffset At, System.Collections.Immutable.ImmutableArray<DungeonRunParticipant> Participants)
    : Dfo.Server.Abstractions.DomainEvents.IDomainEvent;
