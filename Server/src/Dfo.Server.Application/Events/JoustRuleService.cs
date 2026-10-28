using System.Globalization;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Application.Events;

internal sealed class JoustRuleService(IJoustDefinitionSource source) : IJoustRuleService, Dfo.Server.Abstractions.Content.IContentWarmup
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
                || d.Materials.Concat([d.Reward, d.BettingReward]).Any(i => i.GrantTemplate is not { IsPile:true, Lifetime.IsPermanent:true }))
                throw new InvalidDataException("马战内容不完整或超出协议支持范围。");
            definition=d;return (d, null);
        }
        catch (Exception e) when (e is not OperationCanceledException) { return (null, e.Message); }
        finally{loadGate.Release();}
    }
    public string Name => "joust";

    /// <remarks>A content error is not cached here, so a request retries it exactly as it would have.</remarks>
    public async ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        var (loaded, error) = await DefinitionAsync(cancellationToken).ConfigureAwait(false);
        return loaded?.Knights.Length ?? throw new InvalidDataException(error);
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
    public JoustScheduleConfiguration NormalizeConfiguration(JoustScheduleConfiguration? configuration)
    {
        var value=configuration ?? new(0,JoustSchedule.Default);
        var parameters=value.Parameters ?? JoustContestRules.DefaultParameters;ValidateParameters(parameters);
        return value with{Schedule=JoustSchedule.Default,Parameters=parameters};
    }
    public void ValidateParameters(JoustGameplayParameters p)
    {
        if(p is null || new[]{p.StrongMultiplier,p.NormalMultiplier,p.WeakMultiplier,p.CounterMultiplier,p.ForecastWeight}.Any(v=>!double.IsFinite(v))
            || p.WeakMultiplier<.1 || p.StrongMultiplier>3 || p.WeakMultiplier>p.NormalMultiplier || p.NormalMultiplier>p.StrongMultiplier
            || p.CounterMultiplier is <1 or >3 || p.HistoryPrior is <1 or >1000 || p.ForecastWeight is <0 or >1
            || p.BettingScale is <1 or >1000000)
            throw new ArgumentException("状态倍率必须满足 0.1≤弱势≤常态≤强势≤3；克制倍率 1..3，历史先验 1..1000，预测权重 0..1，投注衰减基数 1..1000000。");
    }
    public JoustKnightForecast? KnightForecast(JoustRound round,int knightIndex)
    {
        var forecast=round.Forecasts.SingleOrDefault(f=>f.KnightIndex==knightIndex);
        return forecast is null?null:forecast with{CombatMultiplier=JoustContestRules.ConditionStrength(forecast.Condition,
            round.Schedule.Parameters ?? JoustContestRules.DefaultParameters)};
    }
    public JoustRound SetChampion(JoustRound round,int? knightIndex)
    {
        var natural=round.NaturalWinners.Length==0?round.Winners:round.NaturalWinners;
        _=ResultStatistics(round with{Winners=natural});
        if(knightIndex is {} chosen && !round.Knights.Any(k=>k.Index==chosen))throw new ArgumentException("指定冠军必须是本场参赛骑士。");
        var winners=natural.ToArray();var entrants=round.Knights.Select(k=>k.Index).ToArray();var offset=0;
        if(knightIndex is {} champion)
            while(entrants.Length>1)
            {
                var next=new int[entrants.Length/2];
                for(var i=0;i<next.Length;i++)
                    next[i]=entrants[i*2]==champion || entrants[i*2+1]==champion?champion:natural[offset+i];
                Array.Copy(next,0,winners,offset,next.Length);offset+=next.Length;entrants=next;
            }
        var result=round with{Winners=winners,NaturalWinners=natural.ToArray(),ForcedChampionIndex=knightIndex};
        _=ResultStatistics(result);return result;
    }
    public DateTimeOffset? CurrentStart(JoustSchedule s, DateTimeOffset now)
    {
        ValidateSchedule(s);
        var day = now.ToOffset(TimeSpan.FromHours(8));
        var first = new DateTimeOffset(day.Date + TimeOnly.ParseExact(s.DailyStartTime, "HH:mm", CultureInfo.InvariantCulture).ToTimeSpan(), day.Offset);
        var index = (int)Math.Floor((day-first).TotalMinutes/s.RoundIntervalMinutes);
        return index >= 0 && index < s.RoundsPerDay ? first.AddMinutes((long)index*s.RoundIntervalMinutes).ToUniversalTime() : null;
    }
    public JoustRound CreateRound(string season, DateTimeOffset start, JoustScheduleConfiguration schedule, JoustDefinition d,
        IReadOnlyList<JoustKnightStatistics>? statistics = null, int seed = 0)
    {
        var parameters=schedule.Parameters ?? JoustContestRules.DefaultParameters;ValidateParameters(parameters);
        schedule=schedule with{Parameters=parameters};
        var random = new Random(seed);
        var knights = d.Knights.Take(8).OrderBy(_ => random.Next()).Take(7)
            .Append(d.Knights[random.Next(8,12)]).OrderBy(_ => random.Next()).ToArray();
        var conditions = knights.Select(_ => (byte)random.Next(3)).ToArray();
        var forecasts = JoustContestRules.Forecast(knights, conditions, statistics ?? [],parameters);
        var conditionByKnight = forecasts.ToDictionary(f => f.KnightIndex, f => f.Condition);
        var entrants = knights.ToDictionary(k => k.Index);
        var winners = new List<int>(); var stage = knights.Select(k => k.Index).ToArray();
        while (stage.Length > 1)
        {
            stage = Enumerable.Range(0,stage.Length/2).Select(i =>
            {
                var a=stage[i*2];var b=stage[i*2+1];
                var probability=JoustContestRules.MatchWinProbability(entrants[a],conditionByKnight[a],entrants[b],conditionByKnight[b],parameters);
                return random.NextDouble()<probability?a:b;
            }).ToArray();
            winners.AddRange(stage);
        }
        return new(start.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), season, start, start.AddMinutes(schedule.Schedule.RoundIntervalMinutes),
            schedule, d, knights, [..winners], false){AlgorithmVersion=JoustContestRules.Version,Seed=seed,Forecasts=forecasts};
    }
    public JoustKnightStatistics[] ResultStatistics(JoustRound round)
    {
        var entrants=round.Knights.Select(k=>k.Index).ToArray();
        if(entrants.Length!=8 || entrants.Distinct().Count()!=8 || entrants.Any(i=>i is <0 or >11) || round.Winners.Length!=7)
            throw new InvalidDataException("马战统计需要八位唯一骑士和七场完整赛果。");
        var statistics=entrants.ToDictionary(i=>i,i=>new JoustKnightStatistics(i,0,0,0));
        var offset=0;
        while(entrants.Length>1)
        {
            var next=new int[entrants.Length/2];
            for(var i=0;i<next.Length;i++)
            {
                var left=entrants[i*2];var right=entrants[i*2+1];var winner=round.Winners[offset+i];
                if(winner!=left && winner!=right)throw new InvalidDataException("马战胜者不属于当前对阵。");
                var loser=winner==left?right:left;
                statistics[winner]=statistics[winner] with{Wins=statistics[winner].Wins+1};
                statistics[loser]=statistics[loser] with{Losses=statistics[loser].Losses+1};
                next[i]=winner;
            }
            offset+=next.Length;entrants=next;
        }
        statistics[entrants[0]]=statistics[entrants[0]] with{Championships=1};
        return statistics.Values.OrderBy(s=>s.KnightIndex).ToArray();
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
    public decimal Odds(JoustRound round, int knightIndex, long total, long pool)
    {
        if(round.LockedOdds.SingleOrDefault(o=>o.KnightIndex==knightIndex) is {} locked)return locked.Odds;
        if(round.AlgorithmVersion==0)
            return pool==0?8m:decimal.Round((decimal)total/pool,1,MidpointRounding.AwayFromZero);
        if(round.AlgorithmVersion!=JoustContestRules.Version || round.Forecasts.Length!=8)
            throw new InvalidDataException("马战概率快照缺失或版本无效。");
        var forecast=round.Forecasts.Single(f=>f.KnightIndex==knightIndex);
        return JoustContestRules.BettingOdds(forecast.PricingProbability,pool,round.Schedule.Parameters);
    }
    public JoustRound FreezeOdds(JoustRound round, IReadOnlyList<JoustBet> bets)
    {
        if(round.LockedOdds.Length!=0)
        {
            if(round.LockedOdds.Length!=8 || !round.LockedOdds.Select(o=>o.KnightIndex).Order().SequenceEqual(round.Knights.Select(k=>k.Index).Order()))
                throw new InvalidDataException("封盘赔率快照不完整。");
            return round;
        }
        var total=bets.Sum(b=>(long)b.Amount);
        return round with{LockedOdds=round.Knights.Select(k=>new JoustKnightOdds(k.Index,Odds(round,k.Index,total,
            bets.Where(b=>b.KnightIndex==k.Index).Sum(b=>(long)b.Amount)))).ToArray()};
    }
    public bool MaterialAllowed(JoustDefinition d, ItemSnapshot item, DateTimeOffset now)
    {
        var declaration = d.Materials.SingleOrDefault(i => i.ItemId == item.ItemId);
        return declaration is not null && item.IsPile && item.EquipmentLockId == 0
            && (!item.Lifetime.IsExpired(now) || declaration.ArchivedExpiresAt?.ToUnixTimeSeconds() == item.Lifetime.Deadline);
    }
}
