using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Events;
namespace Dfo.Server.Application.Events;

internal sealed class GmJoustDomainService(IGmAccessDomainService access,IJoustDomainService joust,
    IGmAuditDomainService audit,TimeProvider clock) : IGmJoustDomainService
{
    private async ValueTask<GmIdentity> Authorize(string secret,CancellationToken ct)
    {
        var actor=await access.IdentifyAsync(secret,ct) ?? throw new GmMailException(GmMailRefusal.Unauthorized,"会话已失效。");
        if(!actor.Permissions.HasFlag(GmPermission.All))throw new GmMailException(GmMailRefusal.Forbidden,"仅全权限管理员可管理活动。");
        return actor;
    }
    public async ValueTask<JoustSettings> SettingsAsync(string secret,CancellationToken ct)
    {await Authorize(secret,ct);return await joust.SettingsAsync(ct);}
    public async ValueTask<JoustSettings> SaveAsync(string secret,JoustSettingsChange change,CancellationToken ct)
    {
        var actor=await Authorize(secret,ct);var now=clock.GetUtcNow();
        var result=await joust.SaveSettingsAsync(change,actor.LoginName,now,ct);
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"),now,"ChangeJoustSchedule",actor.Id,actor.LoginName,
            "骑士马战赛程","Succeeded",$"版本 {result.Revision}"),ct);
        return result;
    }
}
public sealed record QueryGmJoustSettingsCommand(string Secret) : ICommand<JoustSettings>;
internal sealed class QueryGmJoustSettingsCommandHandler(IGmJoustDomainService joust) : ICommandHandler<QueryGmJoustSettingsCommand,JoustSettings>
{public ValueTask<JoustSettings> HandleAsync(CommandExecutionContext c,QueryGmJoustSettingsCommand q,CancellationToken ct)=>joust.SettingsAsync(q.Secret,ct);}
public sealed record ChangeGmJoustSettingsCommand(string Secret,JoustSettingsChange Change) : ICommand<JoustSettings>;
internal sealed class ChangeGmJoustSettingsCommandHandler(IGmJoustDomainService joust) : ICommandHandler<ChangeGmJoustSettingsCommand,JoustSettings>
{public ValueTask<JoustSettings> HandleAsync(CommandExecutionContext c,ChangeGmJoustSettingsCommand q,CancellationToken ct)=>joust.SaveAsync(q.Secret,q.Change,ct);}
public sealed record AdvanceJoustLifecycleCommand(DateTimeOffset Now) : ICommand<JoustLifecycleResult>;
internal sealed class AdvanceJoustLifecycleCommandHandler(IJoustDomainService joust) : ICommandHandler<AdvanceJoustLifecycleCommand,JoustLifecycleResult>
{public ValueTask<JoustLifecycleResult> HandleAsync(CommandExecutionContext c,AdvanceJoustLifecycleCommand q,CancellationToken ct)=>joust.AdvanceLifecycleAsync(q.Now,ct);}
