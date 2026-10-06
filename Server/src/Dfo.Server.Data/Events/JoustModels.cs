using System.Text.Json.Serialization;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Data.Events;

public sealed record JoustKnight(int Index, string Name, int AttackType, int[] Win, int[] Loss);
public sealed record JoustItem(int ItemId, string? ItemName, DateTimeOffset? ArchivedExpiresAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ItemSnapshot? GrantTemplate { get; init; }
}
public sealed record JoustDefinition(string SourcePath, int MinimumLevel, int MaximumBetting,
    JoustItem[] Materials, JoustItem Reward, JoustItem BettingReward, JoustKnight[] Knights);
public sealed record JoustSchedule(string DailyStartTime, int RoundsPerDay, int RoundIntervalMinutes,
    int BettingMinutes, int LockMinutes, int StageSeconds)
{
    public static JoustSchedule Default { get; } = new("10:00",7,120,90,10,200);
}
public sealed record JoustGameplayParameters([property:JsonRequired] double StrongMultiplier,
    [property:JsonRequired] double NormalMultiplier, [property:JsonRequired] double WeakMultiplier,
    [property:JsonRequired] double CounterMultiplier, [property:JsonRequired] int HistoryPrior,
    [property:JsonRequired] double ForecastWeight, [property:JsonRequired] long BettingScale);
public sealed record JoustScheduleConfiguration(long Revision, JoustSchedule Schedule, DateTimeOffset? UpdatedAt=null, string? UpdatedBy=null)
{ public JoustGameplayParameters? Parameters { get; init; } }
public sealed record JoustSettings(int EventId, long Revision, JoustSchedule Schedule, JoustGameplayParameters Parameters, string SourcePath, bool Available,
    string? UnavailableReason, int? MinimumLevel, int? MaximumBetting, JoustKnight[] Knights, JoustItem[] Currencies,
    JoustItem? Reward, JoustItem? BettingReward, DateTimeOffset? UpdatedAt, string? UpdatedBy);
public sealed record JoustSettingsChange([property:JsonRequired] long ExpectedRevision, [property:JsonRequired] JoustGameplayParameters Parameters);
public sealed record JoustChampionChange([property:JsonRequired] string RoundId, [property:JsonRequired] long ExpectedRevision,
    [property:JsonRequired] int? KnightIndex);
public sealed record JoustCurrentKnight(int Index, string Name, int AttackType, byte? Condition, double? CombatMultiplier,
    double? ChampionshipProbability, decimal Odds, long BettingAmount, JoustKnightStatistics Statistics);
public sealed record JoustCurrentRound(string? RoundId, long RoundNo, long Revision, bool Active, string Phase,
    DateTimeOffset ServerTime, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, DateTimeOffset? NextTransitionAt,
    DateTimeOffset? ChampionDeadline, bool CanSetChampion, int? ForcedChampionIndex, DateTimeOffset? UpdatedAt,
    string? UpdatedBy, JoustCurrentKnight[] Knights);
public enum JoustPhase { Inactive, Betting, Locked, QuarterFinal, SemiFinal, Final, Result }
public sealed record JoustRound(string RoundId, string SeasonId, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    JoustScheduleConfiguration Schedule, JoustDefinition Definition, JoustKnight[] Knights, int[] Winners, bool Settled)
{
    // Version zero identifies existing rounds whose original pool-based rewards must survive upgrades.
    public int AlgorithmVersion { get; init; }
    public int Seed { get; init; }
    public JoustKnightForecast[] Forecasts { get; init; } = [];
    public JoustKnightOdds[] LockedOdds { get; init; } = [];
    public long AdminRevision { get; init; }
    public int[] NaturalWinners { get; init; } = [];
    public int? ForcedChampionIndex { get; init; }
    public DateTimeOffset? ChampionUpdatedAt { get; init; }
    public string? ChampionUpdatedBy { get; init; }
}
public sealed record JoustKnightForecast(int KnightIndex, byte Condition, double HistoricalProbability,
    double ChampionshipProbability, double PricingProbability)
{ public double CombatMultiplier { get; init; } }
public sealed record JoustKnightOdds(int KnightIndex, decimal Odds);
public sealed record JoustBet(string RequestId, string RoundId, CharacterId Character, int KnightIndex, int MaterialId, int Amount)
{
    public BusinessSlot? PreferredSlot { get; init; }
}
public sealed record JoustKnightStatistics(int KnightIndex, long Wins, long Losses, long Championships)
{
    public decimal WinRate => Wins == 0 && Losses == 0 ? 0m : Wins * 100m / ((decimal)Wins + Losses);
}
public sealed record JoustKnightInfo(int Index, string Name, int AttackType, JoustKnightStatistics Statistics);
public sealed record JoustKnightState(JoustKnight Knight, long Pool, int PersonalBet, decimal Odds)
{
    public JoustKnightStatistics Statistics { get; init; } = new(Knight.Index, 0, 0, 0);
    public JoustKnightForecast? Forecast { get; init; }
}
public sealed record JoustState(bool Active, DateTimeOffset ServerTime, JoustPhase Phase, string? RoundId,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, DateTimeOffset? NextTransitionAt, JoustKnightState[] Knights,
    int[] Winners, int PersonalTotal, int MaximumBetting, int MinimumLevel)
{ public long RoundNo { get; init; } }
public sealed record JoustLifecycleResult(int SettledRounds)
{
    // Captured in the settlement transaction, including rounds no longer current at publication time.
    public JoustState[] CompletedRounds { get; init; } = [];
}
public sealed record JoustInventoryDebit(BusinessSlot Slot, InventoryItemCoreState? Remaining);
public sealed record JoustBetResult(bool Succeeded, string? Rejection, bool Replayed, JoustState State, JoustInventoryDebit[] Debits);
public sealed record JoustHistoryEntry(string RoundId, DateTimeOffset StartsAt, int WinnerIndex, decimal Odds)
{ public long RoundNo { get; init; } }
