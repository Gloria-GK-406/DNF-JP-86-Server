using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public enum TournamentStatus { Fighting, Champion, Eliminated }
public sealed record TournamentQuotaSnapshot(int DungeonId, DateOnly Period, int Used, int Remaining, int Maximum);
public sealed record TournamentDungeonState(DungeonRunId RunId, CharacterId CharacterId, TournamentPlan Plan,
    int CompletedRounds = 0, TournamentStatus Status = TournamentStatus.Fighting)
{
    public int Revivals { get; init; }
    public DateTimeOffset? RewardSelectionEndsAt { get; init; }
    public IReadOnlyList<IReadOnlyList<TournamentRewardCard>> Rewards { get; init; } = [];
    public IReadOnlyList<byte?> Selections { get; init; } = [null, null];
    public bool IsFinished => Status != TournamentStatus.Fighting;
}
public sealed record TournamentActionResult(TournamentDungeonState State)
{
    public ExperienceAward? Experience { get; init; }
    public CharacterSkillProtocolMirrorState? SkillPoints { get; init; }
    public QuestProgressSnapshot? Quests { get; init; }
    public IReadOnlyList<QuestProgressChange> QuestChanges { get; init; } = [];
    public ItemDeliveryReceipt? Delivery { get; init; }
    public int? GoldBalance { get; init; }
    public Dfo.Server.Abstractions.Characters.CharacterFatigueBalance? Fatigue { get; init; }
}
public interface ITournamentDungeonDomainService
{
    ValueTask<IReadOnlyList<TournamentQuotaSnapshot>> ReadQuotaAsync(CharacterId characterId, CancellationToken ct);
    bool Supports(int dungeonId);
    TournamentDungeonState? Find(CharacterId characterId);
    IReadOnlyList<TournamentDungeonState> PendingRewards(DateTimeOffset now);
    bool BeginRewardSelection(SelectedSessionIdentity session, DungeonRunId runId);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
    bool CanKill(DungeonRunState run, ushort actor);
    ValueTask<TournamentActionResult?> AdvanceAsync(SelectedSessionIdentity session, DungeonRunId runId, ushort actor, CancellationToken ct);
    ValueTask<TournamentActionResult?> EliminateAsync(SelectedSessionIdentity session, DungeonRunId runId, CancellationToken ct);
    ValueTask<TournamentActionResult?> SelectRewardAsync(SelectedSessionIdentity session, DungeonRunId runId, byte row, byte index, CancellationToken ct);
    bool CanRevive(CharacterId characterId, DungeonRunId runId);
    bool StageRevival(CharacterId characterId, DungeonRunId runId);
}
