using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Administration;
namespace Dfo.Server.Application.Events;
internal sealed class GmEventsDomainService(IGmAccessDomainService access,IEventManagementDomainService events,
    IEventPersistence store,IGmAuditDomainService audit,TimeProvider clock) : IGmEventsDomainService
{
    private async ValueTask<GmIdentity> Authorize(string secret,CancellationToken ct)
    {
        var actor=await access.IdentifyAsync(secret,ct) ?? throw new GmMailException(GmMailRefusal.Unauthorized,"会话已失效。");
        if (!actor.Permissions.HasFlag(GmPermission.All)) throw new GmMailException(GmMailRefusal.Forbidden,"仅全权限管理员可管理活动。");
        return actor;
    }
    public async ValueTask<ManagedEventList> ListAsync(string secret,CancellationToken ct)
    { await Authorize(secret,ct); return await events.ListAsync(clock.GetUtcNow(),ct); }
    public async ValueTask<ManagedEventList> ChangeAsync(string secret,EventChange change,CancellationToken ct)
    {
        await store.AcquireWriteAsync(ct);
        var actor=await Authorize(secret,ct); var now=clock.GetUtcNow();
        var result=await events.ChangeAsync(change,actor.LoginName,now,ct);
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"),now,"ChangeEvent",actor.Id,actor.LoginName,
            $"活动 {change.EventId}","Succeeded",$"{change.Action}；版本 {result.Entries.Single(e=>e.EventId==change.EventId).Revision}"),ct);
        return result;
    }
}
public sealed record QueryGmEventsCommand(string Secret) : ICommand<ManagedEventList>;
internal sealed class QueryGmEventsCommandHandler(IGmEventsDomainService events) : ICommandHandler<QueryGmEventsCommand,ManagedEventList>
{ public ValueTask<ManagedEventList> HandleAsync(CommandExecutionContext c,QueryGmEventsCommand q,CancellationToken ct)=>events.ListAsync(q.Secret,ct); }
public sealed record ChangeGmEventsCommand(string Secret,EventChange Change) : ICommand<ManagedEventList>;
internal sealed class ChangeGmEventsCommandHandler(IGmEventsDomainService events) : ICommandHandler<ChangeGmEventsCommand,ManagedEventList>
{ public ValueTask<ManagedEventList> HandleAsync(CommandExecutionContext c,ChangeGmEventsCommand q,CancellationToken ct)=>events.ChangeAsync(q.Secret,q.Change,ct); }
