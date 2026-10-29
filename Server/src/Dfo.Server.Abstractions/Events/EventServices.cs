using Dfo.Server.Data.Events;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.Events;

public interface IManagedEventDomainService
{
    int EventId { get; }
    ValueTask<ManagedEventEntry> DescribeAsync(DateTimeOffset now, CancellationToken ct);
    /// <summary>Whether the activity is enabled and inside its period; the gameplay meaning of "open".</summary>
    ValueTask<bool> IsActiveAsync(DateTimeOffset now, CancellationToken ct);
    ValueTask<EventInfo[]> GetEventInfoEntriesAsync(AccountId account, DateTimeOffset now, CancellationToken ct);
    ValueTask ChangeAsync(EventChange change, string actor, DateTimeOffset now, CancellationToken ct);
}
public interface IEventManagementDomainService
{
    ValueTask<ManagedEventList> ListAsync(DateTimeOffset now, CancellationToken ct);
    /// <summary>Whether the registered activity is open now; an unregistered identity is never open.</summary>
    ValueTask<bool> IsOpenAsync(int eventId, DateTimeOffset now, CancellationToken ct);
    ValueTask<EventInfo[]> GetEventInfoListAsync(AccountId account, DateTimeOffset now, CancellationToken ct);
    ValueTask<ManagedEventList> ChangeAsync(EventChange change, string actor, DateTimeOffset now, CancellationToken ct);
}
public interface IWeeklyAttendanceDomainService
{
    ValueTask<WeeklyAttendanceSettings> SettingsAsync(CancellationToken ct);
    ValueTask<WeeklyAttendanceState> StateAsync(AccountId account, DateTimeOffset now, CancellationToken ct);
    ValueTask<bool> RecordClearAsync(AccountId account, Guid runId, int dungeonId, int level, DateTimeOffset now, CancellationToken ct);
    ValueTask<AttendanceClaimResult> ClaimAsync(AccountId account, CharacterId recipient, DateTimeOffset now, CancellationToken ct);
}
public interface IWeeklyAttendanceRuleService
{
    void ValidateCalendarText(string text);
    ValueTask<(WeeklyAttendanceDefinition? Definition, string? Error)> DefinitionAsync(CancellationToken ct);
    ValueTask<string?> ItemNameAsync(int itemId, CancellationToken ct);
    ValueTask<bool> RecommendedAsync(int dungeonId, int level, CancellationToken ct);
    DateTimeOffset WeekStart(DateTimeOffset now);
}
public interface IWeeklyAttendanceDefinitionSource
{
    ValueTask<WeeklyAttendanceDefinition> ReadAsync(CancellationToken ct);
}
public interface IEventPersistence
{
    ValueTask AcquireWriteAsync(CancellationToken ct);
    ValueTask<EventConfiguration?> ConfigurationAsync(int eventId, CancellationToken ct);
    ValueTask<bool> SaveAsync(int eventId, long expectedRevision, EventConfiguration configuration, CancellationToken ct);
    ValueTask<bool> HasProgressAsync(int eventId, string season, CancellationToken ct);
    ValueTask<AttendanceProgress> ProgressAsync(int eventId, string season, AccountId account, DateTimeOffset week, CancellationToken ct);
    ValueTask<bool> CountClearAsync(int eventId, string season, AccountId account, DateTimeOffset week, Guid run, CancellationToken ct);
    ValueTask<bool> MarkClaimedAsync(int eventId, string season, AccountId account, DateTimeOffset week, int required, int maximum, CancellationToken ct);
}

public interface IEventPlayerProjectionDomainService
{ ValueTask<EventPlayerSnapshot> ReadNowAsync(Dfo.Server.Data.World.SelectedSessionIdentity session,CancellationToken ct); }
