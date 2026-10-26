using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Administration;
namespace Dfo.Server.Application.Events;
internal sealed class GmEventsDomainService(IGmAccessDomainService access,IEventManagementDomainService events,IWeeklyAttendanceDomainService attendance,
    IEventPersistence store,IGmAuditDomainService audit,TimeProvider clock,IEventService facts) : IGmEventsDomainService
{
    private async ValueTask<GmIdentity> Authorize(string secret,CancellationToken ct)
    {
        var actor=await access.IdentifyAsync(secret,ct) ?? throw new GmMailException(GmMailRefusal.Unauthorized,"会话已失效。");
        if (!actor.Permissions.HasFlag(GmPermission.All)) throw new GmMailException(GmMailRefusal.Forbidden,"仅全权限管理员可管理活动。");
        return actor;
    }
    public async ValueTask<ManagedEventList> ListAsync(string secret,CancellationToken ct)
    { await Authorize(secret,ct); return await events.ListAsync(clock.GetUtcNow(),ct); }
    public async ValueTask<WeeklyAttendanceSettings> WeeklyAttendanceSettingsAsync(string secret,CancellationToken ct)
    { await Authorize(secret,ct); return await attendance.SettingsAsync(ct); }
    public async ValueTask<ManagedEventList> ChangeAsync(string secret,EventChange change,CancellationToken ct)
    {
        await store.AcquireWriteAsync(ct);
        var actor=await Authorize(secret,ct); var now=clock.GetUtcNow();
        var result=await events.ChangeAsync(change,actor.LoginName,now,ct);
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"),now,"ChangeEvent",actor.Id,actor.LoginName,
            $"活动 {change.EventId}","Succeeded",$"{change.Action}；版本 {result.Entries.Single(e=>e.EventId==change.EventId).Revision}"),ct);
        await facts.EnqueueAsync(new ManagedEventConfigurationChanged(change.EventId,
            result.Entries.Single(e=>e.EventId==change.EventId).Revision),ct);
        return result;
    }
}
