using Dfo.Server.Data.Events;
using Dfo.Server.Abstractions.Notifications;
namespace Dfo.Server.Abstractions.Events;
public interface IGmEventsDomainService
{
    ValueTask<ManagedEventList> ListAsync(string secret,CancellationToken ct);
    ValueTask<WeeklyAttendanceSettings> WeeklyAttendanceSettingsAsync(string secret,CancellationToken ct);
    ValueTask<ManagedEventList> ChangeAsync(string secret,EventChange change,CancellationToken ct);
}
public sealed record EventsChanged : INotification<EventsChanged>
{
    public bool RefreshQuestOffers { get; init; }
}
