using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.Events;
internal sealed class EventPlayerProjectionDomainService(IEventManagementDomainService events,IWeeklyAttendanceDomainService attendance,
    IJoustProjectionDomainService joust,TimeProvider clock):IEventPlayerProjectionDomainService
{
    public async ValueTask<EventPlayerSnapshot> ReadNowAsync(SelectedSessionIdentity session,CancellationToken ct)
    {
        var now=clock.GetUtcNow();
        return new(await events.GetEventInfoListAsync(session.AccountId,now,ct),await attendance.StateAsync(session.AccountId,now,ct),
            await joust.ReadAsync(session.CharacterId,now,ct));
    }
}
