using Dfo.Server.Data.Events;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
namespace Dfo.Server.Abstractions.Events;

public interface IJoustDefinitionSource { ValueTask<JoustDefinition> ReadAsync(CancellationToken ct); }
public interface IJoustRuleService
{
    ValueTask<(JoustDefinition? Definition,string? Error)> DefinitionAsync(CancellationToken ct);
    void ValidateSchedule(JoustSchedule schedule);
    DateTimeOffset? CurrentStart(JoustSchedule schedule, DateTimeOffset now);
    JoustRound CreateRound(string season, DateTimeOffset start, JoustScheduleConfiguration schedule, JoustDefinition definition);
    (JoustPhase Phase, DateTimeOffset Next, int Revealed) Phase(JoustRound round, DateTimeOffset now);
    decimal Odds(long total, long pool);
    bool MaterialAllowed(JoustDefinition definition, ItemSnapshot item, DateTimeOffset now);
}
public interface IJoustDomainService
{
    ValueTask<JoustSettings> SettingsAsync(CancellationToken ct);
    ValueTask<JoustSettings> SaveSettingsAsync(JoustSettingsChange change,string actor,DateTimeOffset now,CancellationToken ct);
    ValueTask<JoustState> GetStateAsync(CharacterId character,DateTimeOffset now,CancellationToken ct);
    ValueTask<JoustBetResult> BetAsync(SelectedSessionIdentity session,string requestId,int knightIndex,BusinessSlot preferredSlot,int amount,DateTimeOffset now,CancellationToken ct);
    ValueTask<JoustHistoryEntry[]> GetHistoryAsync(CancellationToken ct);
    ValueTask<JoustLifecycleResult> AdvanceLifecycleAsync(DateTimeOffset now,CancellationToken ct);
    ValueTask<bool> NeedsAdvanceAsync(DateTimeOffset now,CancellationToken ct);
}
public interface IJoustPersistence
{
    ValueTask<JoustScheduleConfiguration?> ScheduleAsync(CancellationToken ct);
    ValueTask<bool> SaveScheduleAsync(long expectedRevision,JoustScheduleConfiguration schedule,CancellationToken ct);
    ValueTask<JoustRound?> RoundAsync(string roundId,CancellationToken ct);
    ValueTask<JoustRound?> CurrentRoundAsync(DateTimeOffset now,CancellationToken ct);
    ValueTask<bool> HasUnsettledAsync(CancellationToken ct);
    ValueTask SaveRoundAsync(JoustRound round,CancellationToken ct);
    ValueTask<JoustRound[]> UnsettledAsync(CancellationToken ct);
    ValueTask<JoustRound[]> HistoryAsync(CancellationToken ct);
    ValueTask<JoustBet[]> BetsAsync(string roundId,CancellationToken ct);
    ValueTask<JoustBet?> RequestAsync(CharacterId character,string requestId,CancellationToken ct);
    ValueTask InsertBetAsync(JoustBet bet,CancellationToken ct);
}
public interface IGmJoustDomainService
{
    ValueTask<JoustSettings> SettingsAsync(string secret,CancellationToken ct);
    ValueTask<JoustSettings> SaveAsync(string secret,JoustSettingsChange change,CancellationToken ct);
}

public interface IJoustProjectionDomainService
{ ValueTask<JoustState> ReadAsync(CharacterId character, DateTimeOffset now, CancellationToken ct); }
public enum JoustLifecycleEventKind { RoundStarted, PhaseChanged, RoundEnded }
public sealed record JoustLifecycleEvent(JoustState State, JoustLifecycleEventKind Kind) : IDomainEvent;
public sealed record JoustLifecycleNotification(JoustState State, JoustLifecycleEventKind Kind)
    : Dfo.Server.Abstractions.Notifications.INotification<JoustLifecycleNotification>;
public sealed record JoustDetailsChanged(DateTimeOffset Now)
    : Dfo.Server.Abstractions.Notifications.INotification<JoustDetailsChanged>;
