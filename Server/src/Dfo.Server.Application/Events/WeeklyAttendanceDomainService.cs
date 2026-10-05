using Dfo.Server.Abstractions.Events;
using System.Text.Json;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Initialization;
using Dfo.Server.Data.Mail;
namespace Dfo.Server.Application.Events;

internal sealed class WeeklyAttendanceDomainService(IEventPersistence store,IWeeklyAttendanceRuleService rules,
    IMailboxDomainService mailbox,ICharacterTable owners) : IManagedEventDomainService,IWeeklyAttendanceDomainService
{
    public int EventId => 2208;
    private static EventConfiguration Initial => new(0,"initial",false,"累计周签到","周签到","", "","",null,null);
    private async ValueTask<EventConfiguration> Configuration(CancellationToken ct) => await store.ConfigurationAsync(EventId,ct) ?? Initial;
    private static bool Active(EventConfiguration c,DateTimeOffset now) => c.Enabled && c.StartsAt <= now && c.EndsAt > now;
    private async ValueTask<(WeeklyAttendanceDefinition? Definition,string? Error)> Definition(EventConfiguration c,CancellationToken ct)
    {
        var result=await rules.DefinitionAsync(ct);
        if (result.Definition is { } d && c.RuleDefinition is { } saved && saved!=JsonSerializer.Serialize(d))
            return (null,"本期次的 PVF 规则已改变，请恢复原规则或显式创建新期次。");
        return result;
    }
    public async ValueTask<ManagedEventEntry> DescribeAsync(DateTimeOffset now,CancellationToken ct)
    {
        var c = await Configuration(ct); var (d,error) = await Definition(c,ct);
        var status = d is null ? "unavailable" : !c.Enabled ? "disabled" : c.StartsAt > now ? "scheduled" : c.EndsAt <= now ? "expired" : "active";
        return new(EventId,"weekly-attendance",c.Revision,c.SeasonId,c.Enabled,status,c.Title,c.ShortName,c.Description,c.StartNotice,c.EndNotice,
            c.StartsAt,c.EndsAt,d is not null,error,c.UpdatedAt,c.UpdatedBy);
    }
    public async ValueTask<WeeklyAttendanceSettings> SettingsAsync(CancellationToken ct)
    {
        var c = await Configuration(ct); var (d,error) = await Definition(c,ct);
        return new(EventId,c.Revision,c.SeasonId,d?.SourcePath ?? "event/chn_event/chn_totalattendance.evt",d is not null,error,
            d?.RequiredClears,d?.TotalWeeks,d?.WeeklyRewards ?? [],d?.CumulativeRewards ?? []);
    }
    public async ValueTask<EventInfo?> GetEventInfoAsync(AccountId account,DateTimeOffset now,CancellationToken ct)
    {
        var c = await Configuration(ct);
        if (account.Value <= 0 || !Active(c,now) || (await Definition(c,ct)).Definition is null) return null;
        return new(EventId,c.Title,c.ShortName,c.Description,c.StartNotice,c.EndNotice,c.StartsAt!.Value,c.EndsAt!.Value,"total attendance check event/0/0");
    }
    public async ValueTask ChangeAsync(EventChange change,string actor,DateTimeOffset now,CancellationToken ct)
    {
        await store.AcquireWriteAsync(ct);
        var c = await Configuration(ct);
        if (change.ExpectedRevision!=c.Revision) throw new InitializationConflictException();
        if (change.Action is "configure" or "new-season")
        {
            string Text(string? value,int min,int max)
            {
                if (value is null || value.Trim().Length < min || value.Trim().Length > max || value.Contains('\0')) throw new ArgumentException("活动展示字段缺失或长度无效。");
                var text=value.Trim();rules.ValidateCalendarText(text);return text;
            }
            if (change.StartsAt is not { } start || change.EndsAt is not { } end || start>=end
                || start.ToUnixTimeSeconds()<0 || end.ToUnixTimeSeconds()>uint.MaxValue) throw new ArgumentException("活动有效期必须为完整且递增的时间范围。");
            if (change.Action=="configure" && (c.StartsAt!=start || c.EndsAt!=end) && await store.HasProgressAsync(EventId,c.SeasonId,ct))
                throw new ArgumentException("已有玩家进度的期次不能改变时间范围，请显式创建新期次。");
            c = c with { Title=Text(change.Title,1,80),ShortName=Text(change.ShortName,1,40),Description=Text(change.Description,0,1000),
                StartNotice=Text(change.StartNotice,0,200),EndNotice=Text(change.EndNotice,0,200),StartsAt=start,EndsAt=end };
            if (change.Action=="new-season") c=c with { SeasonId=Guid.NewGuid().ToString("N"),Enabled=false,RuleDefinition=null };
        }
        else if (change.Action is "enable" or "disable")
        {
            if (change.Title is not null || change.ShortName is not null || change.Description is not null || change.StartNotice is not null
                || change.EndNotice is not null || change.StartsAt is not null || change.EndsAt is not null) throw new ArgumentException("启停操作不能携带配置字段。");
            if (change.Action=="enable")
            {
                var (d,error)=await Definition(c,ct);
                if (d is null) throw new ArgumentException($"活动规则不可用：{error}");
                if (c.StartsAt is null || c.EndsAt is null) throw new ArgumentException("请先配置活动有效期。");
                c=c with { RuleDefinition=JsonSerializer.Serialize(d) };
            }
            c=c with { Enabled=change.Action=="enable" };
        }
        else throw new ArgumentException("未知活动操作。");
        c=c with { Revision=c.Revision+1,UpdatedAt=now,UpdatedBy=actor };
        if (!await store.SaveAsync(EventId,change.ExpectedRevision,c,ct)) throw new InitializationConflictException();
    }
    public async ValueTask<WeeklyAttendanceState> StateAsync(AccountId account,DateTimeOffset now,CancellationToken ct)
    {
        var c=await Configuration(ct); var (d,_) = await Definition(c,ct);
        if (!Active(c,now) || d is null || account.Value<=0) return WeeklyAttendanceState.Empty;
        var p=await store.ProgressAsync(EventId,c.SeasonId,account,rules.WeekStart(now),ct);
        return new(true,p.Count,p.Clears,Math.Min(Math.Max(1,p.Count),d.TotalWeeks),!p.Claimed && p.Clears>=d.RequiredClears && p.Count<d.TotalWeeks,p.ClaimedAttendance);
    }
    public async ValueTask<bool> RecordClearAsync(AccountId account,Guid runId,int dungeonId,int level,DateTimeOffset now,CancellationToken ct)
    {
        await store.AcquireWriteAsync(ct);
        var c=await Configuration(ct);
        if (runId==Guid.Empty || account.Value<=0 || !Active(c,now) || (await Definition(c,ct)).Definition is null
            || !await rules.RecommendedAsync(dungeonId,level,ct)) return false;
        return await store.CountClearAsync(EventId,c.SeasonId,account,rules.WeekStart(now),runId,ct);
    }
    public async ValueTask<AttendanceClaimResult> ClaimAsync(AccountId account,CharacterId recipient,DateTimeOffset now,CancellationToken ct)
    {
        await store.AcquireWriteAsync(ct);
        if (await owners.FindOwnerAsync(recipient,ct)!=account) return new(false,WeeklyAttendanceState.Empty);
        var state=await StateAsync(account,now,ct);
        if (!state.CanClaim) return new(false,state);
        var c=await Configuration(ct); var d=(await Definition(c,ct)).Definition!;
        if (!await store.MarkClaimedAsync(EventId,c.SeasonId,account,rules.WeekStart(now),d.RequiredClears,d.TotalWeeks,ct))
            return new(false,await StateAsync(account,now,ct));
        var no=state.AttendanceCount+1;
        var rewards=d.WeeklyRewards.Where(r=>r.AttendanceNo==no).Concat(d.CumulativeRewards.Where(r=>r.AttendanceNo==no));
        var mail=MailBuilder.FromSystem(recipient).WithText(c.Title,$"累计第 {no} 次周签到奖励");
        foreach (var r in rewards) mail.AddStackable(r.ItemId,r.Count);
        await mailbox.SendAsync(mail.Build(),ct);
        return new(true,await StateAsync(account,now,ct));
    }
}
