using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public sealed record GuardianAltarPurchase(GuardianAltarShopKind Kind, int Id, int Tier);
/// <summary>Coalesces a wake of the guardian timer; provides no queue or gameplay state access.</summary>
public interface IGuardianAltarSchedulerWakeSignal { void Wake(); }
public sealed record GuardianAltarSlot(byte Index, GuardianAltarShopKind Kind, int Id);
public sealed record GuardianAltarProgress(
    long Revision, DateOnly TicketPeriod, int FreeTickets, int ExtraTickets,
    int GameStars, int PurchasedStars, int AvailableStars, int BestSurvival, int StarResetCount,
    ImmutableDictionary<int, int> Stages, ImmutableArray<GuardianAltarPurchase> Purchases,
    ImmutableArray<GuardianAltarSlot> Slots, ImmutableHashSet<int> ClaimedAchievements);

public interface IGuardianAltarProgressDomainStore
{
    ValueTask<GuardianAltarProgress?> ReadAsync(CharacterId character, CancellationToken ct);
    /// <summary>Optimistic revision check in the caller's shared SQLite transaction.</summary>
    ValueTask SaveAsync(CharacterId character, GuardianAltarProgress value, long? expectedRevision, CancellationToken ct);
    ValueTask<bool> TryRecordSettlementAsync(CharacterId character, Guid runId, int round, CancellationToken ct);
}

public sealed record GuardianAltarSpawn(int OffsetMilliseconds, int MonsterCode, byte Level, byte Rank,
    ushort X, ushort Y, ushort Z, bool Hostile = true);
public sealed record GuardianAltarAdmission(GuardianAltarStage Stage, GuardianAltarMap Map,
    GuardianAltarDifficulty Difficulty, int SurvivalRound)
{
    public ImmutableDictionary<int, GuardianAltarShopLevel> Towers { get; init; } = ImmutableDictionary<int, GuardianAltarShopLevel>.Empty;
}

public interface IGuardianAltarRuleService
{
    GuardianAltarCatalog Catalog { get; }
    int EventId { get; }
    Dfo.Server.Data.Events.EventConfiguration InitialActivity { get; }
    bool Supports(int dungeonId);
    byte MinimumLevel { get; }
    GuardianAltarProgress Initial(DateOnly period);
    GuardianAltarProgress RefreshTickets(GuardianAltarProgress progress, DateOnly period);
    GuardianAltarAdmission? Admission(GuardianAltarProgress progress, int dungeonId, GuardianAltarDifficulty difficulty, int survivalRound = 1);
    GuardianAltarProgress RecordClear(GuardianAltarProgress progress, GuardianAltarAdmission admission);
    GuardianAltarProgress? Buy(GuardianAltarProgress progress, GuardianAltarShopKind kind, int id, int expectedTier, int targetTier);
    GuardianAltarProgress? SetSlot(GuardianAltarProgress progress, byte slot, GuardianAltarShopKind kind, int id);
    GuardianAltarProgress? ExchangeSlots(GuardianAltarProgress progress, byte first, byte second);
    GuardianAltarProgress ResetStars(GuardianAltarProgress progress);
    int ResetGold(GuardianAltarProgress progress);
    GuardianAltarAchievement? ClaimableAchievement(GuardianAltarProgress progress, int achievementId);
    GuardianAltarShopLevel? SlotUnit(GuardianAltarProgress progress, byte slot);
    ImmutableArray<GuardianAltarReward> Rewards(GuardianAltarAdmission admission);
    ImmutableArray<GuardianAltarSpawn> PlanPhase(GuardianAltarAdmission admission, int phase, int alive);
    int MaximumEnergy(int level);
    int EnergyPerSecond(int level);
    int UpgradeEnergyCost(int level);
}

public enum GuardianAltarStatus : byte { Loading, Fighting, Paused, BetweenRounds, Won, Lost, Abandoned }
public sealed record GuardianAltarRunState(DungeonRunId RunId, SelectedSessionIdentity Session,
    GuardianAltarAdmission Admission, DateOnly TicketPeriod, GuardianAltarStatus Status,
    long RoomVersion, long Generation, DateTimeOffset At, DateTimeOffset? Deadline,
    int Phase, int PhaseElapsedMilliseconds, int Energy, int EnergyLevel, int NextActorSequence,
    ImmutableArray<GuardianAltarSpawn> PlannedSpawns, int NextSpawn,
    ImmutableDictionary<byte, DateTimeOffset> UnitCooldowns);
public sealed record GuardianAltarActionResult(GuardianAltarRunState State)
{
    public ImmutableArray<(ushort Sequence, GuardianAltarSpawn Spawn)> Spawns { get; init; } = [];
    public bool StageEnded { get; init; }
    public GuardianAltarProgress? Progress { get; init; }
    public ItemDeliveryReceipt? Delivery { get; init; }
    public bool RoundStarted { get; init; }
    public int StarDelta { get; init; }
    public bool NewSurvivalRecord { get; init; }
    public ImmutableArray<GuardianAltarPurchase> UnlockedPurchases { get; init; } = [];
}
public interface IGuardianAltarDomainService
{
    GuardianAltarCatalog Catalog { get; }
    bool Supports(int dungeonId);
    ValueTask<GuardianAltarProgress> ReadProgressAsync(CharacterId character, CancellationToken ct);
    ValueTask<GuardianAltarProgress?> OpenAsync(SelectedSessionIdentity session, CancellationToken ct);
    bool IsSelecting(SelectedSessionIdentity session);
    GuardianAltarRunState? Find(DungeonRunId run);
    IReadOnlyList<GuardianAltarRunState> Pending();
    void PruneStaleSessions();
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
    ValueTask<GuardianAltarActionResult?> ReadyAsync(SelectedSessionIdentity session, DungeonRunId run, CancellationToken ct);
    ValueTask<GuardianAltarActionResult?> AdvanceAsync(DungeonRunId run, long generation, DateTimeOffset now, CancellationToken ct);
    ValueTask<GuardianAltarActionResult?> SummonAsync(SelectedSessionIdentity session, byte slot, int resourceId, int expectedEnergyLevel, CancellationToken ct);
    ValueTask<GuardianAltarActionResult?> UpgradeEnergyAsync(SelectedSessionIdentity session, int expectedLevel, CancellationToken ct);
    ValueTask<GuardianAltarActionResult?> PauseAsync(SelectedSessionIdentity session, bool pause, CancellationToken ct);
    ValueTask<GuardianAltarActionResult?> AbandonAsync(SelectedSessionIdentity session, CancellationToken ct);
    ValueTask<bool> ReportDeathAsync(SelectedSessionIdentity session, DungeonRunId run, long roomVersion, ushort actor, CancellationToken ct);
    ValueTask<GuardianAltarProgress?> BuyAsync(SelectedSessionIdentity session, GuardianAltarShopKind kind, int id, int firstLevel, int lastLevel, CancellationToken ct);
    ValueTask<GuardianAltarProgress?> SetSlotAsync(SelectedSessionIdentity session, byte slot, GuardianAltarShopKind kind, int id, CancellationToken ct);
    ValueTask<GuardianAltarProgress?> ExchangeSlotsAsync(SelectedSessionIdentity session, byte first, byte second, int expectedFirstId, int expectedSecondId, CancellationToken ct);
    ValueTask<GuardianAltarProgress?> ResetStarsAsync(SelectedSessionIdentity session, CancellationToken ct);
    ValueTask<(GuardianAltarProgress Progress, ItemDeliveryReceipt Delivery)?> ClaimAchievementAsync(SelectedSessionIdentity session, int id, CancellationToken ct);
}
