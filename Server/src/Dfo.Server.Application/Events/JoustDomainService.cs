using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.Events;

internal sealed class JoustDomainService(IEventPersistence events, IJoustPersistence store, IJoustRuleService rules,
    ICharacterDomainService characters, IBusinessInventorySlotDomainService slots,
    ICharacterBusinessInventoryDomainService inventory, IMaterialConsumptionDomainService materials,
    IItemLifecycleDomainService lifecycle, IMailboxDomainService mailbox, IDomainTransactionAccessor transactions)
    : IJoustDomainService, IJoustProjectionDomainService, IManagedEventDomainService
{
    public int EventId => 2365;
    private static EventConfiguration Initial => new(0,"initial",false,"骑士马战大竞猜","骑士马战","","","",null,null);
    private ValueTask<EventConfiguration?> Configuration(CancellationToken ct) => events.ConfigurationAsync(EventId,ct);
    private static bool Active(EventConfiguration c,DateTimeOffset now)=>c.Enabled && c.StartsAt<=now && c.EndsAt>now;
    private async ValueTask Write(CancellationToken ct)
    {
        if (transactions.Current is null || transactions.Current.IsReadOnly) throw new InvalidOperationException("马战写入和推进必须在命令事务内执行。");
        await events.AcquireWriteAsync(ct);
    }
    private async ValueTask<JoustScheduleConfiguration> Schedule(CancellationToken ct)=>await store.ScheduleAsync(ct) ?? new(0,JoustSchedule.Default);
    public async ValueTask<JoustKnightInfo?> GetKnightInfoAsync(int knightIndex,CancellationToken ct)
    {
        var (definition,_)=await rules.DefinitionAsync(ct);
        if(definition?.Knights.SingleOrDefault(k=>k.Index==knightIndex) is not {} knight)return null;
        var statistics=(await store.KnightStatisticsAsync(ct)).SingleOrDefault(s=>s.KnightIndex==knightIndex)
            ?? new(knightIndex,0,0,0);
        return new(knight.Index,knight.Name,knight.AttackType,statistics);
    }
    public async ValueTask<JoustSettings> SettingsAsync(CancellationToken ct)
    {
        var s=await Schedule(ct);var (d,error)=await rules.DefinitionAsync(ct);
        static JoustItem Display(JoustItem item)=>item with{GrantTemplate=null};
        return new(EventId,s.Revision,s.Schedule,d?.SourcePath ?? "event/chn_event/chn_joust.evt",d is not null,error,
            d?.MinimumLevel,d?.MaximumBetting,d?.Knights ?? [],d?.Materials.Select(Display).ToArray() ?? [],
            d is null?null:Display(d.Reward),d is null?null:Display(d.BettingReward),s.UpdatedAt,s.UpdatedBy);
    }
    public async ValueTask<JoustSettings> SaveSettingsAsync(JoustSettingsChange change,string actor,DateTimeOffset now,CancellationToken ct)
    {
        await Write(ct);rules.ValidateSchedule(change.Schedule);
        if (change.ExpectedRevision<0 || !await store.SaveScheduleAsync(change.ExpectedRevision,new(change.ExpectedRevision+1,change.Schedule,now,actor),ct))
            throw new InitializationConflictException();
        return await SettingsAsync(ct);
    }
    public async ValueTask<ManagedEventEntry> DescribeAsync(DateTimeOffset now,CancellationToken ct)
    {
        var c=await Configuration(ct) ?? Initial;var (d,error)=await rules.DefinitionAsync(ct);
        var status=d is null?"unavailable":!c.Enabled?"disabled":c.StartsAt>now?"scheduled":c.EndsAt<=now?"expired":"active";
        return new(EventId,"joust",c.Revision,c.SeasonId,c.Enabled,status,c.Title,c.ShortName,c.Description,c.StartNotice,c.EndNotice,
            c.StartsAt,c.EndsAt,d is not null,error,c.UpdatedAt,c.UpdatedBy);
    }
    public async ValueTask<EventInfo?> GetEventInfoAsync(AccountId account,DateTimeOffset now,CancellationToken ct)
    {
        var c=await Configuration(ct) ?? Initial;
        if(account.Value<=0 || !Active(c,now) || (await rules.DefinitionAsync(ct)).Definition is null)return null;
        return new(EventId,c.Title,c.ShortName,c.Description,c.StartNotice,c.EndNotice,c.StartsAt!.Value,c.EndsAt!.Value,"joust event/0/0");
    }
    public async ValueTask ChangeAsync(EventChange change,string actor,DateTimeOffset now,CancellationToken ct)
    {
        await Write(ct);var c=await Configuration(ct) ?? Initial;
        if(change.ExpectedRevision!=c.Revision)throw new InitializationConflictException();
        if(change.Action is "configure" or "new-season")
        {
            string Text(string? value,int min,int max)
            {
                if(value is null || value.Trim().Length<min || value.Trim().Length>max || value.Contains('\0'))throw new ArgumentException("活动展示字段缺失或长度无效。");
                try { _=new UTF8Encoding(false,true).GetByteCount(value); }catch(EncoderFallbackException e){throw new ArgumentException("活动文案包含无效字符。",e);}
                return value.Trim();
            }
            if(change.StartsAt is not {} start || change.EndsAt is not {} end || start>=end || start.ToUnixTimeSeconds()<0 || end.ToUnixTimeSeconds()>uint.MaxValue)
                throw new ArgumentException("活动有效期必须为完整且递增的时间范围。");
            c=c with{Title=Text(change.Title,1,80),ShortName=Text(change.ShortName,1,40),Description=Text(change.Description,0,1000),
                StartNotice=Text(change.StartNotice,0,200),EndNotice=Text(change.EndNotice,0,200),StartsAt=start,EndsAt=end};
            if(change.Action=="new-season")c=c with{SeasonId=Guid.NewGuid().ToString("N"),Enabled=false};
        }
        else if(change.Action is "enable" or "disable")
        {
            if(change.Title is not null || change.ShortName is not null || change.Description is not null || change.StartNotice is not null
                || change.EndNotice is not null || change.StartsAt is not null || change.EndsAt is not null)throw new ArgumentException("启停操作不能携带配置字段。");
            if(change.Action=="enable" && (c.StartsAt is null || c.EndsAt is null || (await rules.DefinitionAsync(ct)).Definition is null))
                throw new ArgumentException("请先配置活动有效期并确认 PVF 可用。");
            c=c with{Enabled=change.Action=="enable"};
        }
        else throw new ArgumentException("未知活动操作。");
        if(!await events.SaveAsync(EventId,change.ExpectedRevision,c with{Revision=c.Revision+1,UpdatedAt=now,UpdatedBy=actor},ct))throw new InitializationConflictException();
    }
    private async ValueTask<JoustRound?> EnsureCurrentRoundAsync(EventConfiguration c,DateTimeOffset now,CancellationToken ct)
    {
        var existing=await store.CurrentRoundAsync(now,ct);
        if(existing is not null)return existing;
        if(!Active(c,now))return null;
        var s=await Schedule(ct);var start=rules.CurrentStart(s.Schedule,now);
        // Enabling or restarting inside a round joins its scheduled timeline; do not reset its start.
        // Active(c, now) already bounds participation by the activity's effective period.
        if(start is null || start>=c.EndsAt)return null;
        var id=start.Value.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        if(await store.RoundAsync(id,ct) is {} saved)return saved;
        var (d,_)=await rules.DefinitionAsync(ct);if(d is null)return null;
        var round=rules.CreateRound(c.SeasonId,start.Value,s,d);await store.SaveRoundAsync(round,ct);return round;
    }
    public async ValueTask<JoustLifecycleResult> AdvanceLifecycleAsync(DateTimeOffset now,CancellationToken ct)
    {
        await Write(ct);var completed=new List<JoustState>();
        // A restart inside Result can create the current scheduled round. Include it in
        // this same settlement pass so the next tick cannot announce a recovered result.
        var c=await Configuration(ct) ?? Initial;await EnsureCurrentRoundAsync(c,now,ct);
        // Legacy completed rounds receive a persistent result index in chronological
        // order during startup recovery; queries never perform this backfill.
        foreach(var saved in await store.UnrecordedResultsAsync(ct))
        {
            _=rules.ResultStatistics(saved);
            await RecordResultAsync(saved,ct);
        }
        // Upgrade already completed rounds without replaying settlement, mail or notices.
        // This write belongs to lifecycle recovery, never to any information query.
        foreach(var saved in await store.UnrecordedStatisticsAsync(ct))
            await store.RecordStatisticsAsync(saved.RoundId,rules.ResultStatistics(saved),ct);
        foreach(var round in await store.UnsettledAsync(ct))
        {
            if(rules.Phase(round,now).Phase!=JoustPhase.Result)continue;
            var statistics=rules.ResultStatistics(round);
            var bets=await store.BetsAsync(round.RoundId,ct);var total=bets.Sum(b=>(long)b.Amount);var winner=round.Winners[^1];
            var odds=rules.Odds(total,bets.Where(b=>b.KnightIndex==winner).Sum(b=>(long)b.Amount));
            foreach(var group in bets.GroupBy(b=>b.Character))
            {
                var participation=checked(group.Sum(b=>b.Amount));
                var prize=checked((int)decimal.Floor(group.Where(b=>b.KnightIndex==winner).Sum(b=>(long)b.Amount)*odds));
                var builder=MailBuilder.FromSystem(group.Key).WithText("骑士马战大竞猜奖励",$"轮次 {round.StartsAt:yyyy-MM-dd HH:mm} 的参与和竞猜奖励。");
                var prepared=new List<PreparedMailAttachment>();
                async ValueTask Add(JoustItem item,int count)
                {
                    if(count<=0)return;
                    var asset=new MailAsset.Stackable(item.ItemId,count);builder.AddStackable(item.ItemId,count);
                    var template=item.GrantTemplate ?? throw new InvalidDataException("轮次奖励快照不完整。");
                    prepared.Add(new(asset,[await lifecycle.CreateAsync(template with{Count=count},ct)]));
                }
                await Add(round.Definition.BettingReward,participation);await Add(round.Definition.Reward,prize);
                await mailbox.SendPreparedAsync(builder.Build(),prepared,ct);
            }
            // This marker and every attachment join the same ambient SQLite transaction.
            await store.SaveRoundAsync(round with{Settled=true},ct);
            await RecordResultAsync(round,ct);
            await store.RecordStatisticsAsync(round.RoundId,statistics,ct);
            completed.Add(await Project(round,new(0),true,now,ct));
        }
        return new(completed.Count){CompletedRounds=[..completed]};
    }
    public async ValueTask<bool> NeedsAdvanceAsync(DateTimeOffset now,CancellationToken ct)=>
        Active(await Configuration(ct) ?? Initial,now) || await store.HasUnsettledAsync(ct)
            || await store.HasUnrecordedStatisticsAsync(ct) || await store.HasUnrecordedResultsAsync(ct);
    private async ValueTask<JoustState> Project(JoustRound? r,CharacterId character,bool active,DateTimeOffset now,CancellationToken ct)
    {
        if(r is null)return new(false,now,JoustPhase.Inactive,null,null,null,null,[],[],0,0,0);
        var bets=await store.BetsAsync(r.RoundId,ct);var total=bets.Sum(b=>(long)b.Amount);var phase=rules.Phase(r,now);
        var statistics=(await store.KnightStatisticsAsync(ct)).ToDictionary(s=>s.KnightIndex);
        var states=r.Knights.Select(k=>{var pool=bets.Where(b=>b.KnightIndex==k.Index).Sum(b=>(long)b.Amount);
            return new JoustKnightState(k,pool,bets.Where(b=>b.Character==character && b.KnightIndex==k.Index).Sum(b=>b.Amount),rules.Odds(total,pool))
                {Statistics=statistics.GetValueOrDefault(k.Index) ?? new(k.Index,0,0,0)};}).ToArray();
        return new(active,now,phase.Phase,r.RoundId,r.StartsAt,r.EndsAt,phase.Next,states,r.Winners.Take(phase.Revealed).ToArray(),
            bets.Where(b=>b.Character==character).Sum(b=>b.Amount),r.Definition.MaximumBetting,r.Definition.MinimumLevel)
            {RoundNo=(await store.ResultAsync(r.RoundId,ct))?.RoundNo ?? await store.NextResultIndexAsync(ct)};
    }
    public async ValueTask<JoustState> GetStateAsync(CharacterId character,DateTimeOffset now,CancellationToken ct)
    {
        var c=await Configuration(ct) ?? Initial;var r=await store.CurrentRoundAsync(now,ct);
        return await Project(r,character,Active(c,now) && r?.SeasonId==c.SeasonId,now,ct);
    }
    public ValueTask<JoustState> ReadAsync(CharacterId character,DateTimeOffset now,CancellationToken ct) => GetStateAsync(character,now,ct);
    public async ValueTask<JoustBetResult> BetAsync(SelectedSessionIdentity session,string requestId,int knightIndex,BusinessSlot preferredSlot,int amount,DateTimeOffset now,CancellationToken ct)
    {
        await Write(ct);
        if(string.IsNullOrWhiteSpace(requestId) || requestId.Length>160)return new(false,"InvalidRequest",false,await GetStateAsync(session.CharacterId,now,ct),[]);
        var prior=await store.RequestAsync(session.CharacterId,requestId,ct);
        if(prior is not null)
        {
            var old=await store.RoundAsync(prior.RoundId,ct);
            var c=await Configuration(ct) ?? Initial;
            if(prior.KnightIndex!=knightIndex || prior.Amount!=amount || prior.PreferredSlot!=preferredSlot)
                return new(false,"RequestConflict",false,await Project(old,session.CharacterId,false,now,ct),[]);
            return new(true,null,true,await Project(old,session.CharacterId,Active(c,now) && old?.SeasonId==c.SeasonId && old.EndsAt>now,now,ct),[]);
        }
        var state=await GetStateAsync(session.CharacterId,now,ct);
        JoustBetResult Refuse(string reason)=>new(false,reason,false,state,[]);
        if(!state.Active)return Refuse("Closed");if(state.Phase!=JoustPhase.Betting)return Refuse("NotBettingPhase");
        if(amount<=0 || !state.Knights.Any(k=>k.Knight.Index==knightIndex))return Refuse("InvalidRequest");
        var progress=await characters.Get(session.CharacterId).ProgressAsync(ct);
        if(progress is null || progress.Level<state.MinimumLevel)return Refuse("LevelTooLow");
        if((long)state.PersonalTotal+amount>state.MaximumBetting)return Refuse("BetLimitExceeded");
        // One-decimal odds can round a winning character's <=1000 stake up by at most 50 coins.
        if(state.Knights.Sum(k=>k.Pool)+amount>int.MaxValue-1000L)return Refuse("PoolLimitExceeded");
        var round=(await store.RoundAsync(state.RoundId!,ct))!;
        if(round.Settled)return Refuse("RoundSettled");
        var candidates=new List<(BusinessSlot Slot,ItemReference Item)>();
        if(!preferredSlot.Space.IsCarriedItemSpace() || preferredSlot.Index<0)return Refuse("InvalidLocation");
        var preferred=slots.Find(session.CharacterId,preferredSlot);
        if(preferred is not null && await preferred.Container.GetAsync(preferredSlot.Index,ct) is {} selected)
        {
            if(!rules.MaterialAllowed(round.Definition,selected.Snapshot,now))return Refuse("InvalidMaterial");
            candidates.Add((preferredSlot,selected));
        }
        var bag=inventory.GetStore(session.CharacterId,BusinessInventoryStoreKind.Item);
        foreach(var space in new[]{BusinessInventorySpace.Material,BusinessInventorySpace.Consumable,BusinessInventorySpace.QuickSlot})
            if(bag.FindContainer(space) is {} container)
                foreach(var item in await container.GetAllAsync(ct))
                    if(rules.MaterialAllowed(round.Definition,item.Snapshot,now) && !candidates.Any(p=>p.Slot==new BusinessSlot(space,item.SlotIndex)))
                        candidates.Add((new(space,item.SlotIndex),item));
        // The chosen currency is meaningful: split across its stacks, never spend the other coin.
        var currency=candidates.FirstOrDefault().Item?.ItemId;
        candidates=candidates.Where(c=>c.Item.ItemId==currency).ToList();
        if(candidates.Sum(p=>(long)p.Item.Count)<amount)return Refuse("InsufficientMaterial");
        var costs=new List<MaterialCost>();var remaining=amount;
        foreach(var candidate in candidates)
        {var take=Math.Min(candidate.Item.Count,remaining);if(take<=0)continue;costs.Add(new(candidate.Slot,candidate.Item.ItemId,take));remaining-=take;}
        var consumed=await materials.ConsumeAsync(session,costs,ct);
        if(!consumed.Success)return Refuse(consumed.Rejection!);
        await store.InsertBetAsync(new JoustBet(requestId,round.RoundId,session.CharacterId,knightIndex,costs[0].ItemId,amount){PreferredSlot=preferredSlot},ct);
        return new(true,null,false,(await Project(round,session.CharacterId,true,now,ct)),consumed.Debits.Select(d=>new JoustInventoryDebit(d.Cost.Slot,d.Remaining)).ToArray());
    }
    private async ValueTask RecordResultAsync(JoustRound round,CancellationToken ct)
    {
        var bets=await store.BetsAsync(round.RoundId,ct);var winner=round.Winners[^1];
        var odds=rules.Odds(bets.Sum(b=>(long)b.Amount),bets.Where(b=>b.KnightIndex==winner).Sum(b=>(long)b.Amount));
        await store.RecordResultAsync(new(round.RoundId,round.StartsAt,winner,odds),ct);
    }
    public ValueTask<JoustHistoryEntry[]> GetHistoryAsync(CancellationToken ct)=>store.HistoryAsync(ct);
}
