using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.Joust;
using Dfo.Server.Protocol.Game.Mailbox;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Networking.Runtime;
using System.Buffers.Binary;
using System.Text.Json;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Events;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Persistence;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Events;

public sealed class JoustIntegrationTests
{
    [Theory]
    [InlineData("not-started")]
    [InlineData("expired")]
    [InlineData("before-daily")]
    [InlineData("after-daily")]
    [InlineData("disabled")]
    public async Task Startup_outside_the_effective_activity_or_daily_window_does_not_create_a_round(string gate)
    {
        using var f=new Fixture();
        f.Now=DateTimeOffset.Parse(gate switch
        {
            "before-daily"=>"2026-10-06T09:59:59+08:00",
            "after-daily"=>"2026-10-07T00:00:00+08:00",
            _=>"2026-10-06T12:10:00+08:00",
        });
        await f.Enable(startsAt:f.Now.AddSeconds(gate=="not-started"?1:-1));
        if(gate=="expired")f.Now=f.Now.AddDays(3);
        if(gate=="disabled")await f.Run(async()=>
        {await f.Managed.ChangeAsync(new(2365,2,"disable"),"test",f.Now,default);return true;});
        using var worker=ActivatorUtilities.CreateInstance<JoustMaintenanceService>(f.Services);
        try
        {
            await worker.StartAsync(default);
            Assert.False((await f.Services.GetRequiredService<IJoustProjectionDomainService>().ReadAsync(new(1),f.Now,default)).Active);
            Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM event_joust_round"));
        }
        finally{await worker.StopAsync(default);}
    }

    [Fact]
    public async Task Startup_settlement_failure_prevents_readiness_and_successful_restarts_pay_only_once()
    {
        using var f=new Fixture();f.Now=DateTimeOffset.Parse("2026-10-06T12:10:00+08:00");
        await f.Enable(startsAt:f.Now.AddMinutes(-1));await f.AdvanceLifecycle();await f.Seed(121,490002916,10);
        var state=await f.GetState();Assert.True((await f.Bet("startup",state.Knights[0].Knight.Index,0,5)).Succeeded);
        f.Now=DateTimeOffset.Parse("2026-10-06T13:50:00+08:00");
        f.Sql("CREATE TRIGGER fail_startup_settlement BEFORE UPDATE ON event_joust_round WHEN NEW.settled=1 BEGIN SELECT RAISE(ABORT,'startup settlement failure'); END;");
        using(var failed=ActivatorUtilities.CreateInstance<JoustMaintenanceService>(f.Services))
            await Assert.ThrowsAsync<SqliteException>(()=>failed.StartAsync(default));
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
        Assert.Equal(0,f.Scalar("SELECT settled FROM event_joust_round"));
        f.Sql("DROP TRIGGER fail_startup_settlement;");
        using(var recovered=ActivatorUtilities.CreateInstance<JoustMaintenanceService>(f.Services))
        {
            try{await recovered.StartAsync(default);Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));}
            finally{await recovered.StopAsync(default);}
        }
        using var reopened=f.Reopen();
        using var restarted=ActivatorUtilities.CreateInstance<JoustMaintenanceService>(reopened.Services);
        try
        {
            await restarted.StartAsync(default);
            Assert.Equal(1,reopened.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
            Assert.Equal(1,reopened.Scalar("SELECT settled FROM event_joust_round"));
        }
        finally{await restarted.StopAsync(default);}
    }

    [Theory]
    [InlineData("12:10:00", JoustPhase.Betting, 0)]
    [InlineData("13:30:00", JoustPhase.Locked, 0)]
    [InlineData("13:40:00", JoustPhase.QuarterFinal, 4)]
    [InlineData("13:43:20", JoustPhase.SemiFinal, 6)]
    [InlineData("13:46:40", JoustPhase.Final, 7)]
    [InlineData("13:50:00", JoustPhase.Result, 7)]
    public async Task Startup_inside_an_activity_recovers_the_current_round_and_phase_before_start_returns(
        string time, JoustPhase phase, int revealed)
    {
        using var f=new Fixture();f.Now=DateTimeOffset.Parse($"2026-10-06T{time}+08:00");
        await f.Enable(startsAt:f.Now.AddMinutes(-1));
        using var worker=ActivatorUtilities.CreateInstance<JoustMaintenanceService>(f.Services);
        try
        {
            await worker.StartAsync(default);
            // Entry uses the read-only projection: it must already be ready without a query or timer tick.
            var state=await f.Services.GetRequiredService<IJoustProjectionDomainService>().ReadAsync(new(1),f.Now,default);
            Assert.True(state.Active);Assert.Equal(phase,state.Phase);Assert.Equal(revealed,state.Winners.Length);
            Assert.Equal(DateTimeOffset.Parse("2026-10-06T12:00:00+08:00"),state.StartsAt);
            Assert.Equal(DateTimeOffset.Parse("2026-10-06T14:00:00+08:00"),state.EndsAt);
            Assert.Equal(2,state.DailyRoundNo);Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM event_joust_round"));
            await worker.StopAsync(default);
            using var reopened=f.Reopen();
            using var restarted=ActivatorUtilities.CreateInstance<JoustMaintenanceService>(reopened.Services);
            try
            {
                await restarted.StartAsync(default);
                var restored=await reopened.Services.GetRequiredService<IJoustProjectionDomainService>().ReadAsync(new(1),reopened.Now,default);
                Assert.Equal(state.RoundId,restored.RoundId);Assert.Equal(state.Phase,restored.Phase);
                Assert.Equal(state.Knights.Select(k=>k.Knight.Index),restored.Knights.Select(k=>k.Knight.Index));
                Assert.Equal(state.Winners,restored.Winners);
                Assert.Equal(1,reopened.Scalar("SELECT COUNT(*) FROM event_joust_round"));
            }
            finally{await restarted.StopAsync(default);}
        }
        finally{await worker.StopAsync(default);}
    }

    [PvfFact]
    public async Task Real_content_both_coins_preferred_stack_idempotence_split_bets_and_restart_are_durable()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();
        var settings=await f.Joust.SettingsAsync(default);
        Assert.True(settings.Available,settings.UnavailableReason);Assert.Equal(17,settings.MinimumLevel);Assert.Equal(1000,settings.MaximumBetting);
        Assert.Equal(Enumerable.Range(0,12),settings.Knights.Select(k=>k.Index));
        Assert.All(settings.Knights,k=>{Assert.Equal(28,k.Win.Length);Assert.Equal(28,k.Loss.Length);});
        var definition=(await f.Services.GetRequiredService<IJoustRuleService>().DefinitionAsync(default)).Definition!;
        Assert.All(definition.Materials.Concat([definition.Reward,definition.BettingReward]),i=>
        {Assert.False(string.IsNullOrEmpty(i.ItemName));Assert.Equal(2016,i.ArchivedExpiresAt!.Value.Year);Assert.Equal(0,i.GrantTemplate!.ExpireTime);});
        Assert.Same(f.Joust,f.Services.GetServices<IManagedEventDomainService>().Single(e=>e.EventId==2365));
        Assert.Contains(await f.Services.GetRequiredService<IEventManagementDomainService>().GetEventInfoListAsync(new(1),f.Now,default),e=>e.EventId==2365);
        await f.Seed(121,490002916,20);await f.Seed(122,490002916,30);await f.Seed(123,490700609,80);
        var state=await f.GetState();var knight=state.Knights[0].Knight.Index;
        var bet=await f.Bet("one",knight,0,35);Assert.True(bet.Succeeded,bet.Rejection);Assert.Equal(35,bet.State.PersonalTotal);
        Assert.Equal(15,f.Count(490002916));Assert.Equal(80,f.Count(490700609));Assert.Equal(2,bet.Debits.Length);
        var repeated=await f.Bet("one",knight,0,35);Assert.True(repeated.Replayed);Assert.Equal(15,f.Count(490002916));
        Assert.Equal("RequestConflict",(await f.Bet("one",knight,0,36)).Rejection);
        Assert.Equal("InsufficientMaterial",(await f.Bet("two",knight,0,16)).Rejection);Assert.Equal(80,f.Count(490700609));
        Assert.True((await f.Bet("three",state.Knights[1].Knight.Index,2,10)).Succeeded);Assert.Equal(70,f.Count(490700609));
        using var reopened=f.Reopen();Assert.Equal(45,(await reopened.GetState()).PersonalTotal);
        Assert.True((await reopened.Bet("three",state.Knights[1].Knight.Index,2,10)).Replayed);Assert.Equal(70,f.Count(490700609));
        Assert.Equal(state.Knights.Select(k=>k.Knight.Index),(await reopened.GetState()).Knights.Select(k=>k.Knight.Index));
    }
    [PvfFact]
    public async Task Fault_after_inventory_debit_rolls_back_bet_and_balance_and_retry_is_available()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,20);var state=await f.GetState();
        f.Sql("CREATE TRIGGER fail_joust_bet BEFORE INSERT ON event_joust_bet BEGIN SELECT RAISE(ABORT,'injected bet failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(()=>f.Bet("retry",state.Knights[0].Knight.Index,0,5));
        Assert.Equal(20,f.Count(490002916));Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM event_joust_bet"));
        f.Sql("DROP TRIGGER fail_joust_bet;");Assert.True((await f.Bet("retry",state.Knights[0].Knight.Index,0,5)).Succeeded);
        Assert.Equal(15,f.Count(490002916));Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM event_joust_bet"));
    }
    [PvfFact]
    public async Task Level_limits_invalid_knight_phase_and_expired_instance_refuse_without_consumption()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,2000);var state=await f.GetState();var knight=state.Knights[0].Knight.Index;
        await f.Run(async()=>await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(1)).SetLevelAsync(16,default));
        Assert.Equal("LevelTooLow",(await f.Bet("level",knight,0,1)).Rejection);
        await f.Run(async()=>await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(1)).SetLevelAsync(17,default));
        Assert.Equal("InvalidRequest",(await f.Bet("knight",200,0,1)).Rejection);
        Assert.Equal("InvalidRequest",(await f.Bet("amount",knight,0,0)).Rejection);
        Assert.Equal("BetLimitExceeded",(await f.Bet("limit",knight,0,1001)).Rejection);
        Assert.Equal(2000,f.Count(490002916));
        Assert.True((await f.Bet("valid",knight,0,1000)).Succeeded);Assert.Equal("BetLimitExceeded",(await f.Bet("extra",knight,0,1)).Rejection);
        f.Now=f.Now.AddMinutes(90);Assert.Equal("NotBettingPhase",(await f.Bet("locked",knight,0,1)).Rejection);Assert.Equal(1000,f.Count(490002916));
        var rules=f.Services.GetRequiredService<IJoustRuleService>();var d=(await rules.DefinitionAsync(default)).Definition!;
        Assert.True(rules.MaterialAllowed(d,d.Materials[0].GrantTemplate! with{ExpireTime=(int)d.Materials[0].ArchivedExpiresAt!.Value.ToUnixTimeSeconds()},f.Now));
        Assert.False(rules.MaterialAllowed(d,d.Materials[0].GrantTemplate! with{ExpireTime=(int)f.Now.AddDays(-1).ToUnixTimeSeconds()},f.Now));
    }
    [PvfFact]
    public async Task Disabled_activity_still_settles_after_restart_once_and_real_reward_attachments_can_be_claimed()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,100);
        var state=await f.GetState();var round=await f.Services.GetRequiredService<IJoustPersistence>().RoundAsync(state.RoundId!,default);
        Assert.True((await f.Bet("win",round!.Winners[^1],0,30)).Succeeded);
        Assert.True((await f.Bet("lose",round.Knights.First(k=>k.Index!=round.Winners[^1]).Index,0,71)).Succeeded==false);
        Assert.True((await f.Bet("lose",round.Knights.First(k=>k.Index!=round.Winners[^1]).Index,0,70)).Succeeded);
        await f.Run(async()=>{await f.Managed.ChangeAsync(new(2365,2,"disable"),"test",f.Now,default);return true;});
        Assert.Equal("Closed",(await f.Bet("disabled",round.Winners[^1],0,1)).Rejection);
        f.Now=f.Now.AddMinutes(110);using var reopened=f.Reopen();await reopened.AdvanceLifecycle();
        var mail=Assert.Single(await reopened.Services.GetRequiredService<IMailboxDomainService>().InboxAsync(new(1)));
        Assert.Contains(mail.Attachments,a=>a.Asset is MailAsset.Stackable{ItemId:490002925,Count:100});
        Assert.Contains(mail.Attachments,a=>a.Asset is MailAsset.Stackable{ItemId:490002916,Count:99});
        await reopened.AdvanceLifecycle();Assert.Equal(1,reopened.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
        foreach(var attachment in mail.Attachments)await reopened.Services.GetRequiredService<IMailboxDomainService>().ClaimAsync(new(1),attachment.Id);
        Assert.Equal(100,reopened.Count(490002925));Assert.Equal(99,reopened.Count(490002916));
        Assert.All((await reopened.Services.GetRequiredService<IMailboxDomainService>().InboxAsync(new(1))).Single().Attachments,a=>Assert.True(a.Claimed));
        Assert.Equal(0,reopened.Scalar("SELECT COUNT(*) FROM event_joust_round WHERE settled=0"));
    }
    [PvfFact]
    public async Task Fault_in_settlement_rolls_back_all_mail_and_settled_marker_then_retry_recovers_once()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,10);var state=await f.GetState();
        await f.Bet("bet",state.Knights[0].Knight.Index,0,10);f.Now=f.Now.AddMinutes(110);
        f.Sql("CREATE TRIGGER fail_joust_settle BEFORE UPDATE OF settled ON event_joust_round WHEN NEW.settled=1 BEGIN SELECT RAISE(ABORT,'injected settlement failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(()=>f.AdvanceLifecycle());Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
        Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM event_joust_round WHERE settled=0"));
        f.Sql("DROP TRIGGER fail_joust_settle;");await f.AdvanceLifecycle();await f.AdvanceLifecycle();Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
    }
    [PvfFact]
    public async Task Settings_are_revisioned_restart_durable_and_existing_round_keeps_original_schedule_and_results()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();var original=await f.GetState();
        var changed=await f.Run(()=>f.Joust.SaveSettingsAsync(new(0,new("10:05",2,60,30,10,200)),"test",f.Now,default));
        Assert.Equal(1,changed.Revision);Assert.Equal(original.EndsAt,(await f.GetState()).EndsAt);
        await Assert.ThrowsAsync<Dfo.Server.Abstractions.Initialization.InitializationConflictException>(()=>f.Run(()=>f.Joust.SaveSettingsAsync(new(0,JoustSchedule.Default),"test",f.Now,default)));
        using var reopened=f.Reopen();Assert.Equal(changed.Schedule,(await reopened.Joust.SettingsAsync(default)).Schedule);
        Assert.Equal(original.Knights.Select(k=>k.Knight.Index),(await reopened.GetState()).Knights.Select(k=>k.Knight.Index));
        f.Now=original.EndsAt!.Value;reopened.Now=f.Now;await reopened.AdvanceLifecycle();var next=await reopened.GetState();Assert.Equal(f.Now.AddMinutes(-55),next.StartsAt);
    }
    [PvfFact]
    public async Task Prize_is_not_sent_during_final_animation_and_expired_activity_keeps_its_settlement_obligation()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,10);var state=await f.GetState();
        await f.Bet("one",state.Knights[0].Knight.Index,0,10);
        await f.Run(async()=>{await f.Managed.ChangeAsync(new(2365,2,"configure","马战","马战","","","",f.Now.AddDays(-1),f.Now.AddMinutes(1)),"test",f.Now,default);return true;});
        f.Now=f.Now.AddMinutes(100).AddSeconds(400);var final=await f.GetState();Assert.Equal(JoustPhase.Final,final.Phase);Assert.Equal(7,final.Winners.Length);
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));Assert.False(final.Active);
        f.Now=f.Now.AddSeconds(200);await f.AdvanceLifecycle();Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
        using var reopened=f.Reopen();await reopened.GetState();Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
    }
    [PvfFact]
    public async Task Pool_protocol_bound_does_not_debit_materials()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,10);var state=await f.GetState();var knight=state.Knights[0].Knight.Index;
        await f.Run(async()=>{await f.Services.GetRequiredService<IJoustPersistence>().InsertBetAsync(new("synthetic-full-pool",state.RoundId!,new(2),knight,490002916,int.MaxValue-1000),default);return true;});
        Assert.Equal("PoolLimitExceeded",(await f.Bet("too-big",knight,0,1)).Rejection);Assert.Equal(10,f.Count(490002916));
        Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM event_joust_bet"));
        Assert.False((await f.Bet("invalid-space",knight,0,-1)).Succeeded);Assert.Equal(10,f.Count(490002916));
    }
    [PvfFact]
    public async Task Illegal_material_in_selected_slot_refuses_instead_of_spending_another_coin()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,10);await f.Seed(122,490002925,1);
        var state=await f.GetState();Assert.Equal("InvalidMaterial",(await f.Bet("wrong-material",state.Knights[0].Knight.Index,1,1)).Rejection);
        Assert.Equal(10,f.Count(490002916));Assert.Equal(1,f.Count(490002925));Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM event_joust_bet"));
    }
    [Fact]
    public void Schedule_phase_boundaries_utc8_and_decimal_odds_are_explicit()
    {
        var rules=new JoustRuleService(null!);
        Assert.Throws<ArgumentException>(()=>rules.ValidateSchedule(new("23:00",2,60,30,10,200)));
        Assert.Throws<ArgumentException>(()=>rules.ValidateSchedule(new("10:00",1,60,60,10,200)));
        Assert.Throws<ArgumentException>(()=>rules.ValidateSchedule(new("1:00",1,60,30,10,200)));
        Assert.Throws<ArgumentException>(()=>rules.ValidateSchedule(new("10:00",7,120,90,10,400)));
        Assert.Equal(3.4m,rules.Odds(101,30));Assert.Equal(1.1m,rules.Odds(21,20));Assert.Equal(8m,rules.Odds(1,0));
        Assert.Null(rules.CurrentStart(JoustSchedule.Default,DateTimeOffset.Parse("2026-10-06T09:59:59+08:00")));
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T02:00:00Z"),rules.CurrentStart(JoustSchedule.Default,DateTimeOffset.Parse("2026-10-06T10:00:00+08:00")));
        var definition=new JoustDefinition("fixture",17,1000,[],new(1,"reward",null),new(2,"participation",null),Enumerable.Range(0,12).Select(i=>new JoustKnight(i,"knight",0,[],[])).ToArray());
        var start=DateTimeOffset.Parse("2026-10-06T02:00:00Z");var r=rules.CreateRound("season",start,new(0,JoustSchedule.Default),definition);
        Assert.Equal(7,r.Winners.Length);Assert.Equal(7,r.Knights.Count(k=>k.Index<8));Assert.Single(r.Knights,k=>k.Index>=8);
        Assert.Equal(JoustPhase.Betting,rules.Phase(r,start).Phase);Assert.Equal(JoustPhase.Locked,rules.Phase(r,start.AddMinutes(90)).Phase);
        Assert.Equal((JoustPhase.QuarterFinal,4),(rules.Phase(r,start.AddMinutes(100)).Phase,rules.Phase(r,start.AddMinutes(100)).Revealed));
        Assert.Equal(6,rules.Phase(r,start.AddMinutes(100).AddSeconds(200)).Revealed);Assert.Equal(7,rules.Phase(r,start.AddMinutes(100).AddSeconds(400)).Revealed);
        Assert.Equal(7,rules.Phase(r,start.AddMinutes(110)).Revealed);
    }

    [Fact]
    public async Task Settled_round_rejects_new_bet_after_clock_rewinds()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,20);var first=await f.GetState();
        await f.Bet("before",first.Knights[0].Knight.Index,0,5);f.Now=f.Now.AddMinutes(110);await f.AdvanceLifecycle();
        f.Now=f.Now.AddMinutes(-110);var result=await f.Bet("after",first.Knights[0].Knight.Index,0,5);Assert.False(result.Succeeded);Assert.Equal("RoundSettled",result.Rejection);Assert.Equal(15,f.Count(490002916));
    }
    [Fact]
    public async Task Native_dispatch_uses_physical_ledger_and_refreshes_joust_on_duplicate_without_second_debit()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,30);var state=await f.GetState();var counter=new GameInboundFrameCounter();
        JoustBetRequest Request(ushort seq,int amount=5)
        {var (ordinal,fresh)=counter.Observe(seq);var body=new byte[23];body[14]=(byte)state.Knights[0].Knight.Index;BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(15),121);BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(19),amount);
         return new((new GameMessageMetadata(1,0x04E7,0,seq){InboundOrdinal=ordinal,SequenceIsFresh=fresh}).WithRawBody(body),body[14],121,amount);}
        var dispatcher=f.Services.GetRequiredService<INetworkMessageDispatcher>();
        await dispatcher.DispatchAsync(new(f.Connection),Request(1),default);Assert.Equal(25,f.Count(490002916));Assert.Single(f.Sender.Messages.OfType<JoustResponse>());
        f.Sender.Messages.Clear();f.Sender.Batches.Clear();await dispatcher.DispatchAsync(new(f.Connection),Request(1),default);
        Assert.Equal(25,f.Count(490002916));Assert.Empty(f.Sender.Messages.OfType<JoustResponse>());Assert.Contains(f.Sender.Batches.SelectMany(b=>b.Frames),frame=>frame[0]==0 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))==0x04BA && BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(17))==5);
        await dispatcher.DispatchAsync(new(f.Connection),Request(2),default);Assert.Equal(20,f.Count(490002916));
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await dispatcher.DispatchAsync(new(f.Connection),Request(2,6),default));Assert.Equal(20,f.Count(490002916));
        f.Sender.FailBatch=true;await Assert.ThrowsAsync<IOException>(async()=>await dispatcher.DispatchAsync(new(f.Connection),Request(3),default));Assert.Equal(15,f.Count(490002916));
        f.Sender.FailBatch=false;await dispatcher.DispatchAsync(new(f.Connection),Request(3),default);Assert.Equal(15,f.Count(490002916));
        for(var seq=4;seq<=ushort.MaxValue;seq++)counter.Observe((ushort)seq);
        await dispatcher.DispatchAsync(new(f.Connection),Request(0,15),default);Assert.Equal(0,f.Count(490002916));
        f.Sender.Batches.Clear();await dispatcher.DispatchAsync(new(f.Connection),Request(0,15),default);Assert.Equal(0,f.Count(490002916));
        Assert.Contains(f.Sender.Batches.SelectMany(b=>b.Frames),frame=>frame[0]==0 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))==0x000E);
        f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session);
        await dispatcher.DispatchAsync(new(f.Connection),Request(1),default);Assert.Equal(0,f.Count(490002916));Assert.Equal(6u,f.Sender.Messages.OfType<JoustResponse>().Last().Result);
    }
    [Fact]
    public async Task Native_query_holds_selection_lease_until_exact_physical_batch_is_sent()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();var gate=f.Services.GetRequiredService<Dfo.Server.Application.OnlineUsers.SelectedSessionTransitionGate>();
        Task<IAsyncDisposable>? pending=null;var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Sender.BeforeSend=()=>
        {
            pending=Task.Run(async()=>{started.SetResult();return await gate.AcquireAsync(f.Session.AccountId,Dfo.Server.Application.Commands.UserAccessMode.Exclusive,default);});
            started.Task.GetAwaiter().GetResult();Assert.False(pending.IsCompleted);
        };
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),new JoustQueryRequest(new(1,0x04E6,0,1)),default);
        Assert.Empty(f.Sender.Messages.OfType<JoustResponse>());
        Assert.DoesNotContain(f.Sender.Batches.SelectMany(b=>b.Frames),frame=>frame[0]==0 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))==0x04B8);
        Assert.Same(f.Connection,Assert.Single(f.Sender.Batches).Connection);
        await using var replacementLease=await pending!;f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session);
    }
    [Fact]
    public async Task Native_bet_confirmed_rollback_can_retry_same_frame_without_partial_debit()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,10);var state=await f.GetState();var stream=new GameInboundFrameCounter();
        JoustBetRequest Request(){var(ordinal,fresh)=stream.Observe(1);var body=new byte[23];body[14]=(byte)state.Knights[0].Knight.Index;BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(15),121);BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(19),5);return new((new GameMessageMetadata(1,0x04E7,0,1){InboundOrdinal=ordinal,SequenceIsFresh=fresh}).WithRawBody(body),body[14],121,5);}
        f.Sql("CREATE TRIGGER fail_native_bet BEFORE INSERT ON event_joust_bet BEGIN SELECT RAISE(ABORT,'native injected'); END;");
        var dispatcher=f.Services.GetRequiredService<INetworkMessageDispatcher>();await dispatcher.DispatchAsync(new(f.Connection),Request(),default);Assert.Equal(10,f.Count(490002916));Assert.Equal(6u,f.Sender.Messages.OfType<JoustResponse>().Last().Result);
        f.Sql("DROP TRIGGER fail_native_bet;");await dispatcher.DispatchAsync(new(f.Connection),Request(),default);Assert.Equal(5,f.Count(490002916));Assert.Equal(0u,f.Sender.Messages.OfType<JoustResponse>().Last().Result);
    }
    [Theory]
    [InlineData(4, 0x04E6)] [InlineData(14, 0x04E6)]
    [InlineData(4, 0x04E8)] [InlineData(14, 0x04E8)]
    public async Task Queries_do_not_create_rounds_settle_or_send_mail(int hours, int opcode)
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,10);var state=await f.GetState();await f.Bet("late",state.Knights[0].Knight.Index,0,5);
        f.Sender.Messages.Clear(); f.Sender.Batches.Clear();
        f.Now=f.Now.AddHours(hours);
        for(var i=0;i<3;i++)await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),new JoustQueryRequest(new(1,(ushort)opcode,0,1)),default);
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
        Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM event_joust_round"));
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM event_joust_round WHERE settled=1"));
        Assert.Equal(5,f.Count(490002916));
        Assert.DoesNotContain(f.Sender.Messages,m=>m.GetType().Name=="MailboxAlarm");
    }
    [Fact]
    public async Task Queries_before_lifecycle_do_not_create_a_round()
    {
        using var f=new Fixture();await f.Enable();
        var dispatcher=f.Services.GetRequiredService<INetworkMessageDispatcher>();
        foreach(var opcode in new ushort[]{0x04E6,0x04E8})
            await dispatcher.DispatchAsync(new(f.Connection),new JoustQueryRequest(new(1,opcode,0,1)),default);
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM event_joust_round"));
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
    }
    [Fact]
    public async Task Repeated_queries_after_settlement_do_not_announce_existing_mail()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,10);
        await f.Bet("earned",(await f.GetState()).Knights[0].Knight.Index,0,5);
        f.Now=f.Now.AddMinutes(110);await f.AdvanceLifecycle();
        Assert.Single(f.Sender.Messages.OfType<MailboxAlarm>());
        f.Sender.Messages.Clear();
        var dispatcher=f.Services.GetRequiredService<INetworkMessageDispatcher>();
        for(var i=0;i<3;i++)foreach(var opcode in new ushort[]{0x04E6,0x04E8})
            await dispatcher.DispatchAsync(new(f.Connection),new JoustQueryRequest(new(1,opcode,0,1)),default);
        await f.AdvanceLifecycle();
        Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
        Assert.DoesNotContain(f.Sender.Messages,m=>m is MailboxAlarm);
        Assert.DoesNotContain(f.Sender.Messages,m=>m.GetType().Name is "JoustStateMessage" or "JoustResultsMessage");
    }
    [Fact]
    public async Task Refused_bet_does_not_settle_an_overdue_round()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,10);
        var knight=(await f.GetState()).Knights[0].Knight.Index;
        await f.Bet("valid",knight,0,5);f.Now=f.Now.AddMinutes(110);
        Assert.False((await f.Bet("late",knight,0,1)).Succeeded);
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM event_joust_round WHERE settled=1"));
        Assert.Equal(5,f.Count(490002916));
    }
    [Fact]
    public async Task Maintenance_projects_phase_changes_and_mail_service_notifies_actual_delivery()
    {
        using var f=new Fixture();await f.Enable();await f.AdvanceLifecycle();await f.Seed(121,490002916,10);var state=await f.GetState();await f.Bet("tick",state.Knights[0].Knight.Index,0,5);
        var tick=ActivatorUtilities.CreateInstance<Dfo.Server.Application.Events.JoustMaintenanceService>(f.Services);
        await tick.TickAsync(default);var count=f.Sender.Messages.Count;Assert.True(count>0);await tick.TickAsync(default);Assert.Equal(count,f.Sender.Messages.Count);
        f.Now=f.Now.AddMinutes(90);await tick.TickAsync(default);Assert.True(f.Sender.Messages.Count>count);count=f.Sender.Messages.Count;await tick.TickAsync(default);Assert.Equal(count,f.Sender.Messages.Count);
        f.Now=f.Now.AddMinutes(20);await tick.TickAsync(default);Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));Assert.Contains(f.Sender.Messages,m=>m.GetType().Name=="MailboxAlarm");
    }
    [Theory]
    [InlineData("10:10:00")]
    [InlineData("11:40:00")]
    [InlineData("11:46:40")]
    [InlineData("11:50:00")]
    public async Task Startup_recovers_each_phase_without_replaying_announcements(string time)
    {
        using var f=new Fixture();f.Now=DateTimeOffset.Parse($"2026-10-06T{time}+08:00");await f.Enable();
        using var worker=ActivatorUtilities.CreateInstance<JoustMaintenanceService>(f.Services);
        try
        {
            await worker.StartAsync(default);
            await worker.TickAsync(default);
            Assert.Empty(f.Events);
            Assert.DoesNotContain(f.Sender.Messages,m=>m.GetType().Name is "JoustStateMessage" or "JoustResultsMessage");
            Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM event_joust_round"));
        }
        finally{await worker.StopAsync(default);}
    }
    [Fact]
    public async Task Lifecycle_broadcasts_opening_and_completed_winner_once_without_final_preview()
    {
        using var f=new Fixture();await f.Enable();
        using var worker=ActivatorUtilities.CreateInstance<JoustMaintenanceService>(f.Services);
        await worker.TickAsync(default);await worker.TickAsync(default);
        Assert.Single(f.Events,e=>e.Kind==JoustLifecycleEventKind.RoundStarted);
        f.Now=f.Now.AddMinutes(100);await worker.TickAsync(default);
        f.Now=f.Now.AddSeconds(200);await worker.TickAsync(default);
        f.Sender.Messages.Clear();f.Now=f.Now.AddSeconds(200);await worker.TickAsync(default);
        Assert.DoesNotContain(f.Sender.Messages,m=>m.GetType().Name is "JoustStateMessage" or "JoustResultsMessage");
        f.Now=f.Now.AddSeconds(200);await worker.TickAsync(default);await worker.TickAsync(default);
        Assert.Single(f.Events,e=>e.Kind==JoustLifecycleEventKind.RoundEnded);
        Assert.Single(f.Sender.Messages,m=>m.GetType().Name=="JoustResultsMessage");
        var before=f.Sender.Messages.Count;
        for(var i=0;i<3;i++)await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),new JoustQueryRequest(new(1,0x04E6,0,1)),default);
        Assert.Single(f.Sender.Messages,m=>m.GetType().Name=="JoustResultsMessage");
        Assert.DoesNotContain(f.Sender.Messages.Skip(before),m=>m.GetType().Name is "JoustStateMessage" or "JoustResultsMessage");
    }
    [Fact]
    public async Task Cross_round_tick_publishes_the_committed_old_winner_before_the_new_opening()
    {
        using var f=new Fixture();await f.Enable();
        using var worker=ActivatorUtilities.CreateInstance<JoustMaintenanceService>(f.Services);
        await worker.TickAsync(default);var original=await f.GetState();
        f.Events.Clear();f.Sender.Messages.Clear();
        f.Now=f.Now.AddHours(2);await worker.TickAsync(default);await worker.TickAsync(default);
        Assert.Equal(new[]{JoustLifecycleEventKind.RoundEnded,JoustLifecycleEventKind.RoundStarted},f.Events.Select(e=>e.Kind));
        Assert.Equal(original.RoundId,f.Events[0].State.RoundId);
        Assert.Equal((ushort)1,f.Events[0].State.DailyRoundNo);
        Assert.Equal((ushort)2,f.Events[1].State.DailyRoundNo);
        Assert.Equal(JoustPhase.Result,f.Events[0].State.Phase);
        Assert.Equal(7,f.Events[0].State.Winners.Length);
        Assert.Single(f.Sender.Messages,m=>m.GetType().Name=="JoustResultsMessage");
    }
    private sealed class EventProbe(List<JoustLifecycleEvent> events):IDomainEventHandler<JoustLifecycleEvent>
    {
        public ValueTask HandleAsync(JoustLifecycleEvent value,CancellationToken ct){events.Add(value);return ValueTask.CompletedTask;}
    }
    [Fact]
    public async Task Failed_broadcast_is_not_replayed_by_the_next_tick()
    {
        var notifications=new FailingNotifications();using var f=new Fixture(notifications);await f.Enable();
        using var worker=ActivatorUtilities.CreateInstance<JoustMaintenanceService>(f.Services);
        await worker.TickAsync(default);await worker.TickAsync(default);
        Assert.Equal(1,notifications.Attempts);
        f.Now=f.Now.AddMinutes(110);await worker.TickAsync(default);await worker.TickAsync(default);
        Assert.Equal(2,notifications.Attempts);
        Assert.Single(f.Events,e=>e.Kind==JoustLifecycleEventKind.RoundEnded);
        Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM event_joust_round WHERE settled=1"));
    }
    private sealed class FailingNotifications:INotificationBus
    {
        public int Attempts{get;private set;}
        public ValueTask<NotificationDelivery> PublishAsync<T>(NotificationAudience audience,T notification,CancellationToken ct)
            where T:class,INotification<T>
        {Attempts++;throw new IOException("broadcast transport failed");}
    }
    [Fact]
    public async Task Reenable_or_clock_rewind_does_not_announce_the_same_round_as_new()
    {
        using var f=new Fixture();await f.Enable();
        using var worker=ActivatorUtilities.CreateInstance<JoustMaintenanceService>(f.Services);
        await worker.TickAsync(default);
        await f.Run(async()=>{await f.Managed.ChangeAsync(new(2365,2,"disable"),"test",f.Now,default);return true;});
        await worker.TickAsync(default);
        await f.Run(async()=>{await f.Managed.ChangeAsync(new(2365,3,"enable"),"test",f.Now,default);return true;});
        await worker.TickAsync(default);
        f.Now=f.Now.AddMinutes(90);await worker.TickAsync(default);
        f.Now=f.Now.AddMinutes(-90);await worker.TickAsync(default);
        Assert.Single(f.Events,e=>e.Kind==JoustLifecycleEventKind.RoundStarted);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now=DateTimeOffset.Parse("2026-10-06T10:00:00+08:00");public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory;private readonly bool owns;private readonly ServiceProvider provider;private readonly Clock clock;
        private string Path=>System.IO.Path.Combine(directory,"joust.db");
        public DateTimeOffset Now {get=>clock.Now;set=>clock.Now=value;}
        public IServiceProvider Services=>provider;public IJoustDomainService Joust=>provider.GetRequiredService<IJoustDomainService>();
        public IManagedEventDomainService Managed=>provider.GetServices<IManagedEventDomainService>().Single(e=>e.EventId==2365);
        public SelectedSessionIdentity Session {get;}
        public NetworkConnectionContext Connection {get;}
        public ShopSender Sender {get;}=new();
        public List<JoustLifecycleEvent> Events {get;}=[];
        public Fixture(INotificationBus? notifications=null):this(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"dfo-joust-"+Guid.NewGuid().ToString("N")),new Clock(),true,notifications)
        {Sql("INSERT INTO accounts(account_id,m_id) VALUES(1,'joust'); INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES(1,1,'joust',0,1,0); INSERT INTO character_stats(character_id,carried_gold) VALUES(1,0);");}
        private Fixture(string directory,Clock clock,bool owns,INotificationBus? notifications=null)
        {
            this.directory=directory;this.clock=clock;this.owns=owns;
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            { ["Database:Path"]=Path,["Pvf:ArchivePath"]=new PvfContentOptions().ResolveArchivePath() }).Build();
            var s=new ServiceCollection().AddLogging();new InfrastructureModule().ConfigureServices(s,config);new PersistenceModule().ConfigureServices(s,config);
            new ApplicationModule().ConfigureServices(s,config);new ProtocolModule().ConfigureServices(s,config);s.AddSingleton<INetworkMessageSender>(Sender);s.AddSingleton<TimeProvider>(clock);
            if(notifications is not null)s.AddSingleton(notifications);
            s.AddSingleton<IDomainEventHandler<JoustLifecycleEvent>>(new EventProbe(Events));provider=s.BuildServiceProvider();Sender.Services=provider;
            _=provider.GetRequiredService<SqliteDatabase>();
            var connection=new NetworkConnectionContext(new("joust-test"),NetworkEndpointKind.Game,10011);Connection=connection;
            var registry=provider.GetRequiredService<OnlineStateRegistry>();var online=registry.RegisterConnection(connection);registry.Authenticate(online.OnlineUserId,new(1));
            Session=SelectedSessionIdentity.Create(new(1),new(1),connection.ConnectionId,registry.GetRequiredUser(online.OnlineUserId).Generation,10011);
            registry.Publish(Session);provider.GetRequiredService<ISelectedSessionDomainService>().Select(Session);
            var containers=provider.GetRequiredService<ICharacterContainerDomainService>();
            containers.Register(new(1),new PersistentItemContainer(ContainerId.ForCharacter(new(1),InventoryContainerType.Main),new(InventoryContainerType.Main,0),
                provider.GetRequiredService<IItemDomainStore>(),provider.GetRequiredService<IDomainTransactionAccessor>(),provider.GetRequiredService<IItemPlacementRuleService>(),provider.GetRequiredService<IItemSlotRuleService>()));
        }
        public Fixture Reopen()=>new(directory,clock,false);
        public async Task<T> Run<T>(Func<ValueTask<T>> act)
        {await using var tx=provider.GetRequiredService<DomainTransactionScope>().Begin();var result=await act();await tx.CommitAsync(default);return result;}
        public async Task Enable(DateTimeOffset? startsAt=null)
        {
            await Run(async()=>await provider.GetRequiredService<ICharacterDomainService>().Get(new(1)).SetLevelAsync(17,default));
            await Run(async()=>{await Managed.ChangeAsync(new(2365,0,"configure","马战","马战","","","",startsAt ?? Now.AddDays(-1),Now.AddDays(3)),"test",Now,default);
                await Managed.ChangeAsync(new(2365,1,"enable"),"test",Now,default);return true;});
        }
        public async Task<JoustState> GetState()
        {await using var tx=provider.GetRequiredService<DomainTransactionScope>().Begin(readOnly:true);var state=await Joust.GetStateAsync(new(1),Now,default);await tx.CommitAsync(default);return state;}
        public Task<JoustLifecycleResult> AdvanceLifecycle()=>Run(()=>Joust.AdvanceLifecycleAsync(Now,default));
        public Task<JoustBetResult> Bet(string request,int knight,int slot,int amount)=>Run(()=>Joust.BetAsync(Session,request,knight,new(BusinessInventorySpace.Material,slot),amount,Now,default));
        public Task<bool> Seed(int slot,int item,int count)=>Run(async()=>
        {
            var definition=(await provider.GetRequiredService<IJoustRuleService>().DefinitionAsync(default)).Definition!;
            var template=definition.Materials.Append(definition.BettingReward).Single(m=>m.ItemId==item).GrantTemplate!;
            var store=provider.GetRequiredService<IItemDomainStore>().SessionIn(provider.GetRequiredService<IDomainTransactionAccessor>().Current!);
            var id=ContainerId.ForCharacter(new(1),InventoryContainerType.Main);
            await store.PutAsync(id,slot,await store.CreateAsync(template with{Count=count},default),default);return true;
        });
        public int Count(int item)=>Scalar($"SELECT COALESCE(SUM(s.count),0) FROM item_slots s JOIN items i ON i.item_uid=s.core_uid WHERE s.owner_scope='character' AND s.owner_id=1 AND substr(hex(i.item_core),3,8)='{Convert.ToHexString(BitConverter.GetBytes(item))}'");
        public int Scalar(string sql){using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path}.ToString());c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;return Convert.ToInt32(cmd.ExecuteScalar());}
        public void Sql(string sql){using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path}.ToString());c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.ExecuteNonQuery();}
        public void Dispose(){provider.Dispose();if(owns){SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}}
    }
}
