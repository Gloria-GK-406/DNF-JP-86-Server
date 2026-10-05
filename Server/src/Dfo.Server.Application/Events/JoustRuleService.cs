using System.Globalization;
using System.Security.Cryptography;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Application.Events;

internal sealed class JoustRuleService(IJoustDefinitionSource source) : IJoustRuleService
{
    private JoustDefinition? definition;
    private readonly SemaphoreSlim loadGate=new(1);
    public async ValueTask<(JoustDefinition? Definition, string? Error)> DefinitionAsync(CancellationToken ct)
    {
        if(definition is {} loaded)return (loaded,null);
        await loadGate.WaitAsync(ct);
        try
        {
            if(definition is {} cached)return(cached,null);
            var d = await source.ReadAsync(ct);
            if (d.MinimumLevel is < 1 or > 86 || d.MaximumBetting is < 1 or > 1000
                || !d.Knights.Select(k => k.Index).SequenceEqual(Enumerable.Range(0, 12))
                || d.Materials.Length != 2 || d.Materials.Select(m => m.ItemId).Distinct().Count() != 2
                || d.Materials.Concat([d.Reward, d.BettingReward]).Any(i => i.GrantTemplate is not { IsPile:true, ExpireTime:0 }))
                throw new InvalidDataException("马战内容不完整或超出协议支持范围。");
            definition=d;return (d, null);
        }
        catch (Exception e) when (e is not OperationCanceledException) { return (null, e.Message); }
        finally{loadGate.Release();}
    }
    public void ValidateSchedule(JoustSchedule s)
    {
        if (s is null || !TimeOnly.TryParseExact(s.DailyStartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || s.RoundsPerDay is < 1 or > 24 || s.RoundIntervalMinutes is < 1 or > 1440
            || s.BettingMinutes is < 1 or > 1440 || s.LockMinutes is < 0 or > 1440 || s.StageSeconds is < 1 or > 86400
            || (long)s.BettingMinutes*60 + (long)s.LockMinutes*60 + (long)s.StageSeconds*3 >= s.RoundIntervalMinutes*60L
            || start.ToTimeSpan().TotalMinutes + (long)s.RoundsPerDay*s.RoundIntervalMinutes > 1440)
            throw new ArgumentException("赛程时间格式无效、跨午夜或阶段占满每期时长；必须保留正时长结果展示窗口。");
    }
    public DateTimeOffset? CurrentStart(JoustSchedule s, DateTimeOffset now)
    {
        ValidateSchedule(s);
        var day = now.ToOffset(TimeSpan.FromHours(8));
        var first = new DateTimeOffset(day.Date + TimeOnly.ParseExact(s.DailyStartTime, "HH:mm", CultureInfo.InvariantCulture).ToTimeSpan(), day.Offset);
        var index = (int)Math.Floor((day-first).TotalMinutes/s.RoundIntervalMinutes);
        return index >= 0 && index < s.RoundsPerDay ? first.AddMinutes((long)index*s.RoundIntervalMinutes).ToUniversalTime() : null;
    }
    public JoustRound CreateRound(string season, DateTimeOffset start, JoustScheduleConfiguration schedule, JoustDefinition d)
    {
        var knights = d.Knights.Take(8).OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).Take(7)
            .Append(d.Knights[RandomNumberGenerator.GetInt32(8,12)]).OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToArray();
        var winners = new List<int>(); var stage = knights.Select(k => k.Index).ToArray();
        while (stage.Length > 1)
        {
            stage = Enumerable.Range(0,stage.Length/2).Select(i => stage[i*2+RandomNumberGenerator.GetInt32(2)]).ToArray();
            winners.AddRange(stage);
        }
        return new($"{season}:{start.ToUnixTimeSeconds()}", season, start, start.AddMinutes(schedule.Schedule.RoundIntervalMinutes),
            schedule, d, knights, [..winners], false);
    }
    public (JoustPhase Phase, DateTimeOffset Next, int Revealed) Phase(JoustRound r, DateTimeOffset now)
    {
        var s = r.Schedule.Schedule; var bet = r.StartsAt.AddMinutes(s.BettingMinutes); var locked = bet.AddMinutes(s.LockMinutes);
        if (now < r.StartsAt) return (JoustPhase.Inactive, r.StartsAt, 0);
        if (now < bet) return (JoustPhase.Betting, bet, 0);
        if (now < locked) return (JoustPhase.Locked, locked, 0);
        if (now < locked.AddSeconds(s.StageSeconds)) return (JoustPhase.QuarterFinal, locked.AddSeconds(s.StageSeconds), 4);
        if (now < locked.AddSeconds(s.StageSeconds*2L)) return (JoustPhase.SemiFinal, locked.AddSeconds(s.StageSeconds*2L), 6);
        if (now < locked.AddSeconds(s.StageSeconds*3L)) return (JoustPhase.Final, locked.AddSeconds(s.StageSeconds*3L), 7);
        return (JoustPhase.Result, r.EndsAt, 7);
    }
    public decimal Odds(long total, long pool) => pool == 0 ? 8m : decimal.Round((decimal)total/pool,1,MidpointRounding.AwayFromZero);
    public bool MaterialAllowed(JoustDefinition d, ItemSnapshot item, DateTimeOffset now)
    {
        var declaration = d.Materials.SingleOrDefault(i => i.ItemId == item.ItemId);
        return declaration is not null && item.IsPile && item.EquipmentLockId == 0
            && (item.ExpireTime == 0 || item.ExpireTime > now.ToUnixTimeSeconds()
                || declaration.ArchivedExpiresAt?.ToUnixTimeSeconds() == item.ExpireTime);
    }
}
