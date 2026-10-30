using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Inventory;
using System.Net;
using System.Net.Http.Json;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.WeeklyAttendance;
using Dfo.Server.Testing;
using Dfo.Server.Infrastructure.Content;
using System.Buffers.Binary;
namespace Dfo.Server.Host.Tests;
public sealed class GmEventsHttpTests
{
    private const string Url="/api/gm/v1/events";
    private sealed class Rules : IWeeklyAttendanceDefinitionSource
    {
        internal bool Broken { get; set; }
        public ValueTask<WeeklyAttendanceDefinition> ReadAsync(CancellationToken ct) => Broken
            ? throw new InvalidDataException("source absent")
            : ValueTask.FromResult(new WeeklyAttendanceDefinition("fixture",4,44,12,
                Enumerable.Range(1,12).Select(i=>new AttendanceReward(i,3,i)).ToArray(),[new(4,3033,2),new(8,3034,1),new(11,3035,1)]));
    }
    private sealed class Dungeons : IDungeonDefinitionSource
    { public ValueTask<DungeonDefinition?> GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult<DungeonDefinition?>(id==1 ? new(1,"fixture",80,true,[],86) : null); }
    private static void ConfigureDungeons(IServiceCollection services)
    {
        // Retain the archive reader for startup's tower prewarm; only attendance uses dungeon 1.
        var archive = services.Single(s=>s.ServiceType==typeof(IDungeonDefinitionSource)).ImplementationFactory!;
        services.AddSingleton<IDungeonAdmissionDefinitionSource>(s=>(IDungeonAdmissionDefinitionSource)archive(s));
        services.AddSingleton<IDungeonDefinitionSource>(new Dungeons());
    }
    public sealed record Act(AccountId AccountId,bool Claim=false,Guid RunId=default,int Dungeon=1,int Level=86,bool Fail=false) : ICommand<bool>;
    public sealed class ActHandler(IWeeklyAttendanceDomainService attendance,TimeProvider clock) : ICommandHandler<Act,bool>
    {
        public async ValueTask<bool> HandleAsync(CommandExecutionContext c,Act q,CancellationToken ct)
        {
            var result=q.Claim ? (await attendance.ClaimAsync(q.AccountId,new CharacterId(q.AccountId.Value),clock.GetUtcNow(),ct)).Succeeded
                : await attendance.RecordClearAsync(q.AccountId,q.RunId,q.Dungeon,q.Level,clock.GetUtcNow(),ct);
            if(q.Fail) throw new InvalidOperationException("injected post-operation failure");
            return result;
        }
    }
    private static void Content(IServiceCollection services,Rules? rules=null)
    {
        services.AddFixtureCommand<ClearExperienceAlgorithmInput, ClearExperienceResult, ClearExperienceAlgorithmDriver>(FixtureUnitKind.Transactional);
        services.AddSingleton<IWeeklyAttendanceDefinitionSource>(rules ?? new Rules());ConfigureDungeons(services);
        services.AddFixtureCommand<Act,bool,ActHandler>(FixtureUnitKind.Transactional);
    }
    private static EventChange Configure(long rev,string action="configure")=>new(2208,rev,action,"签到","周签到","说明","开始","结束",DateTimeOffset.Parse("2026-01-01T00:00:00Z"),DateTimeOffset.Parse("2028-01-01T00:00:00Z"));
    private static async Task<ManagedEventEntry> Get(GmTestHost host)=>Assert.Single((await host.Client.GetFromJsonAsync<ManagedEventList>(Url))!.Entries,e=>e.EventId==2208);
    private static async Task<ManagedEventEntry> Change(GmTestHost host,string csrf,EventChange c)
    { using var response=await host.PostAsync(Url,csrf,c);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return Assert.Single((await response.Content.ReadFromJsonAsync<ManagedEventList>())!.Entries,e=>e.EventId==2208); }
    private static async Task<string> Enable(GmTestHost host)
    { var csrf=await GmCeraHttpTests.Login(host);await Change(host,csrf,Configure(0));await Change(host,csrf,new(2208,1,"enable"));return csrf; }
    private static async Task Clears(GmTestHost host,int count=4)
    { for(var i=0;i<count;i++) Assert.True(await DomainCommandFixture.Bus(host.Services).SendAsync(new Act(new(1),RunId:Guid.NewGuid()))); }
    [Fact]
    public async Task List_contains_only_common_fields_and_specific_settings_are_read_only_without_creating_state()
    {
        await using var host=await GmTestHost.StartAsync(configureServices:s=>Content(s));
        var token=await GmCeraHttpTests.Login(host);
        using var list=System.Text.Json.JsonDocument.Parse(await host.Client.GetStringAsync(Url));
        var entry=list.RootElement.GetProperty("entries")[0];
        foreach(var field in new[]{"sourcePath","requiredClears","totalWeeks","weeklyRewards","cumulativeRewards"})
            Assert.False(entry.TryGetProperty(field,out _));
        var settings=(await host.Client.GetFromJsonAsync<WeeklyAttendanceSettings>(Url+"/weekly-attendance/settings"))!;
        Assert.Equal(2208,settings.EventId);Assert.Equal(0,settings.Revision);Assert.Equal("initial",settings.SeasonId);
        Assert.True(settings.Available);Assert.Equal(4,settings.RequiredClears);Assert.Equal(12,settings.TotalWeeks);
        Assert.All(settings.WeeklyRewards,r=>Assert.Equal("测试消耗品",r.ItemName));
        Assert.Equal(new[]{"测试黑色材料","测试白色材料","测试红色材料"},settings.CumulativeRewards.Select(r=>r.ItemName));
        Assert.Equal(12,settings.WeeklyRewards.Length);Assert.Equal(new[]{4,8,11},settings.CumulativeRewards.Select(r=>r.AttendanceNo));
        Assert.Empty(await host.ReadAsync("SELECT event_id FROM event_configuration"));
        Assert.Empty(await host.ReadAsync("SELECT operation_id FROM gm_audit WHERE action='ChangeEvent'"));
        using var response=await host.PostAsync(Url+"/weekly-attendance/settings",token,new { requiredClears=99 });
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Empty(await host.ReadAsync("SELECT event_id FROM event_configuration"));
        await Change(host,token,Configure(0));
        var updated=(await host.Client.GetFromJsonAsync<WeeklyAttendanceSettings>(Url+"/weekly-attendance/settings"))!;
        Assert.Equal(1,updated.Revision);Assert.Equal("initial",updated.SeasonId);Assert.Equal(4,updated.RequiredClears);
    }
    private sealed class Names : IItemCatalogSource
    {
        internal bool Missing { get; set; }
        internal string Prefix { get; set; }="名称";
        internal int Reads { get; set; }
        public ValueTask<ItemCatalogEntry?> FindAsync(int id,CancellationToken ct)
        {
            Reads++;
            return ValueTask.FromResult<ItemCatalogEntry?>(Missing ? null : new(id,$"{Prefix}{id}","consumable",null,1,1));
        }
        public ValueTask<IReadOnlyList<ItemCatalogEntry>> SearchAsync(ItemCatalogFilter filter,CancellationToken ct)=>throw new NotSupportedException();
    }
    [Fact]
    public async Task Names_are_read_by_attendance_service_once_per_distinct_item_and_do_not_change_season_rules_or_claims()
    {
        var names=new Names();
        await using var host=await GmTestHost.StartAsync(configureServices:s=>{Content(s);s.AddSingleton<IItemCatalogSource>(names);});
        await GmPlayerDirectoryHttpTests.SeedAsync(host);await Enable(host);await Clears(host);
        var before=await host.ReadAsync("SELECT input FROM event_configuration");
        var attendance=host.Services.GetRequiredService<IWeeklyAttendanceDomainService>();
        var initial=await attendance.SettingsAsync(default);
        Assert.All(initial.WeeklyRewards,r=>Assert.Equal("名称3",r.ItemName));Assert.Equal(4,names.Reads);
        names.Prefix="更新名称";var updated=await attendance.SettingsAsync(default);
        Assert.Equal(initial.SeasonId,updated.SeasonId);Assert.Equal(initial.Revision,updated.Revision);
        Assert.True(updated.Available);Assert.All(updated.WeeklyRewards,r=>Assert.Equal("更新名称3",r.ItemName));
        names.Missing=true;var missing=await attendance.SettingsAsync(default);
        Assert.True(missing.Available);Assert.All(missing.WeeklyRewards,r=>Assert.Null(r.ItemName));
        Assert.All(missing.CumulativeRewards,r=>Assert.Null(r.ItemName));Assert.Equal(3,missing.WeeklyRewards[0].ItemId);
        Assert.Equal(before,await host.ReadAsync("SELECT input FROM event_configuration"));
        Assert.True(await DomainCommandFixture.Bus(host.Services).SendAsync(new Act(new(1),Claim:true)));
        Assert.Single(await TestUseCase.Mailbox(host.Services).InboxAsync(new(1)));
    }
    [Fact]
    public async Task Specific_settings_require_full_permission_and_missing_rules_do_not_fabricate_targets()
    {
        await using(var denied=await GmTestHost.StartAsync(permissions:"QueryPlayers",configureServices:s=>Content(s)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized,(await denied.Client.GetAsync(Url+"/weekly-attendance/settings")).StatusCode);
            await GmCeraHttpTests.Login(denied);
            Assert.Equal(HttpStatusCode.Forbidden,(await denied.Client.GetAsync(Url+"/weekly-attendance/settings")).StatusCode);
        }
        await using var host=await GmTestHost.StartAsync(configureServices:s=>Content(s,new Rules { Broken=true }));
        await GmCeraHttpTests.Login(host);
        var settings=(await host.Client.GetFromJsonAsync<WeeklyAttendanceSettings>(Url+"/weekly-attendance/settings"))!;
        Assert.False(settings.Available);Assert.NotNull(settings.UnavailableReason);
        Assert.Null(settings.RequiredClears);Assert.Null(settings.TotalWeeks);
        Assert.Empty(settings.WeeklyRewards);Assert.Empty(settings.CumulativeRewards);
    }
    [Fact]
    public async Task Registered_events_are_available_to_authorized_administrator()
    {
        await using var host = await GmTestHost.StartAsync();
        await GmCeraHttpTests.Login(host);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/gm/v1/events")).StatusCode);
    }
    [Fact]
    public async Task Unicode_calendar_text_is_saved_and_projected_without_GBK_restrictions()
    {
        await using var host=await GmTestHost.StartAsync(configureServices:s=>Content(s));
        var token=await GmCeraHttpTests.Login(host);
        var change=Configure(0) with { Title="签到😀",ShortName="周签到𠮷",Description="说明한글",StartNotice="开始é",EndNotice="结束🚀" };
        await Change(host,token,change);await Change(host,token,new(2208,1,"enable"));
        var entry=await Get(host);
        var info=Assert.Single(await host.Services.GetRequiredService<IEventManagementDomainService>().GetEventInfoListAsync(new(1),DateTimeOffset.UtcNow,default),e=>e.EventId==2208);
        Assert.Equal(change.Title,entry.Title);Assert.Equal(change.ShortName,entry.ShortName);Assert.Equal(change.Description,entry.Description);
        Assert.Equal(change.StartNotice,entry.StartNotice);Assert.Equal(change.EndNotice,entry.EndNotice);
        Assert.Equal(change.Title,info.Title);Assert.Equal(change.ShortName,info.ShortName);Assert.Equal(change.Description,info.Description);
        Assert.Equal(change.StartNotice,info.StartNotice);Assert.Equal(change.EndNotice,info.EndNotice);
        var rules=host.Services.GetRequiredService<IWeeklyAttendanceRuleService>();
        foreach(var malformed in new[]{ "\uD800", "\uDC00" })Assert.Throws<ArgumentException>(()=>rules.ValidateCalendarText(malformed));
    }
    [Fact]
    public async Task Permissions_csrf_strict_input_revision_and_audit_failure_protect_configuration()
    {
        await using(var denied=await GmTestHost.StartAsync(permissions:"QueryPlayers",configureServices:s=>Content(s)))
        { var csrf=await GmCeraHttpTests.Login(denied);Assert.Equal(HttpStatusCode.Forbidden,(await denied.Client.GetAsync(Url)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await denied.PostAsync(Url,csrf,Configure(0))).StatusCode); }
        var fault=new GmAuditFault();await using var host=await GmTestHost.StartAsync(fault:fault,configureServices:s=>Content(s));
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client.GetAsync(Url)).StatusCode);
        var token=await GmCeraHttpTests.Login(host);
        Assert.Equal(HttpStatusCode.Forbidden,(await host.PostAsync(Url,null,Configure(0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(Url,token,new { eventId=2208,action="enable" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(Url,token,new { eventId=2208,expectedRevision=0,action="enable",bad=true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(Url,token,Configure(0) with { EndsAt=null })).StatusCode);
        fault.Throw=true;Assert.Equal(HttpStatusCode.InternalServerError,(await host.PostAsync(Url,token,Configure(0))).StatusCode);
        Assert.Equal(0,(await Get(host)).Revision);fault.Throw=false;
        await Change(host,token,Configure(0));Assert.Equal(HttpStatusCode.Conflict,(await host.PostAsync(Url,token,new EventChange(2208,0,"enable"))).StatusCode);
        Assert.Single(await host.ReadAsync("SELECT operation_id FROM gm_audit WHERE action='ChangeEvent'"));
    }
    [Fact]
    public async Task Repeated_runs_concurrent_claims_and_restart_keep_exactly_one_reward_and_preserve_seasons()
    {
        var directory=GmTestHost.NewDirectory();string season;
        await using(var host=await GmTestHost.StartAsync(directory:directory,configureServices:s=>Content(s)))
        {
            await GmPlayerDirectoryHttpTests.SeedAsync(host);var csrf=await Enable(host);season=(await Get(host)).SeasonId;
            Assert.Same(host.Services.GetRequiredService<IWeeklyAttendanceDomainService>(),Assert.Single(host.Services.GetServices<IManagedEventDomainService>(),e=>e.EventId==2208));
            var events=host.Services.GetRequiredService<IEventManagementDomainService>();Assert.Single(await events.GetEventInfoListAsync(new(1),DateTimeOffset.UtcNow,default), e=>e.EventId==2208);
            var start=DateTimeOffset.Parse("2026-01-01T00:00:00Z");var end=DateTimeOffset.Parse("2028-01-01T00:00:00Z");
            Assert.DoesNotContain(await events.GetEventInfoListAsync(new(1),start.AddTicks(-1),default),e=>e.EventId==2208);Assert.Single(await events.GetEventInfoListAsync(new(1),start,default),e=>e.EventId==2208);
            Assert.DoesNotContain(await events.GetEventInfoListAsync(new(1),end,default),e=>e.EventId==2208);
            var bus=DomainCommandFixture.Bus(host.Services);var id=Guid.NewGuid();
            Assert.False(await bus.SendAsync(new Act(new(1),Claim:true)));
            Assert.False(await bus.SendAsync(new Act(new(1),RunId:Guid.NewGuid(),Dungeon:2)));
            Assert.False(await bus.SendAsync(new Act(new(1),RunId:Guid.NewGuid(),Level:60)));
            Assert.True(await bus.SendAsync(new Act(new(1),RunId:id)));Assert.False(await bus.SendAsync(new Act(new(1),RunId:id)));
            await Clears(host,3);
            var claims=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>bus.SendAsync(new Act(new(1),Claim:true)).AsTask()));
            Assert.Single(claims,b=>b);
            var mail=Assert.Single(await TestUseCase.Mailbox(host.Services).InboxAsync(new(1)));
            var reward=Assert.IsType<Dfo.Server.Data.Mail.MailAsset.Stackable>(Assert.Single(mail.Attachments).Asset);Assert.Equal(3,reward.ItemId);Assert.Equal(1,reward.Count);
            Assert.Equal(1,(await host.Services.GetRequiredService<IWeeklyAttendanceDomainService>().StateAsync(new(1),DateTimeOffset.UtcNow,default)).CurrentAttendanceNo);
            await Change(host,csrf,new(2208,2,"disable"));Assert.False(await bus.SendAsync(new Act(new(1),RunId:Guid.NewGuid())));
            Assert.DoesNotContain(await events.GetEventInfoListAsync(new(1),DateTimeOffset.UtcNow,default),e=>e.EventId==2208);await Change(host,csrf,new(2208,3,"enable"));
            Assert.False(await bus.SendAsync(new Act(new(1),Claim:true)));Assert.Equal(season,(await Get(host)).SeasonId);
            Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(Url,csrf,Configure(4) with { EndsAt=DateTimeOffset.Parse("2029-01-01T00:00:00Z") })).StatusCode);
        }
        await using var restart=await GmTestHost.StartAsync(directory:directory,configureServices:s=>Content(s));var token=await GmCeraHttpTests.Login(restart);
        var state=await restart.Services.GetRequiredService<IWeeklyAttendanceDomainService>().StateAsync(new(1),DateTimeOffset.UtcNow,default);Assert.Equal(1,state.AttendanceCount);Assert.False(state.CanClaim);
        var next=await Change(restart,token,Configure(4,"new-season"));Assert.NotEqual(season,next.SeasonId);Assert.False(next.Enabled);
        await Change(restart,token,new(2208,5,"enable"));state=await restart.Services.GetRequiredService<IWeeklyAttendanceDomainService>().StateAsync(new(1),DateTimeOffset.UtcNow,default);Assert.Equal(0,state.AttendanceCount);Assert.Equal(0,state.RecommendedClears);
        Assert.Single(await TestUseCase.Mailbox(restart.Services).InboxAsync(new(1)));
    }
    [Fact]
    public async Task Transaction_failure_rolls_back_runs_rewards_and_claims_and_retry_is_available()
    {
        await using var host=await GmTestHost.StartAsync(configureServices:s=>Content(s));await GmPlayerDirectoryHttpTests.SeedAsync(host);await Enable(host);
        var bus=DomainCommandFixture.Bus(host.Services);var run=Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>bus.SendAsync(new Act(new(1),RunId:run,Fail:true)).AsTask());
        Assert.True(await bus.SendAsync(new Act(new(1),RunId:run)));await Clears(host,3);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>bus.SendAsync(new Act(new(1),Claim:true,Fail:true)).AsTask());
        Assert.Empty(await TestUseCase.Mailbox(host.Services).InboxAsync(new(1)));
        var state=await host.Services.GetRequiredService<IWeeklyAttendanceDomainService>().StateAsync(new(1),DateTimeOffset.UtcNow,default);Assert.True(state.CanClaim);Assert.Equal(0,state.AttendanceCount);
        Assert.True(await bus.SendAsync(new Act(new(1),Claim:true)));Assert.Single(await TestUseCase.Mailbox(host.Services).InboxAsync(new(1)));
    }
    [Fact]
    public async Task Weekly_reset_cumulative_rewards_expiry_and_missing_content_follow_server_authority()
    {
        var clock=new GmTestClock();var rules=new Rules();await using var host=await GmTestHost.StartAsync(clock:clock,configureServices:s=>Content(s,rules));await GmPlayerDirectoryHttpTests.SeedAsync(host);var csrf=await Enable(host);
        var bus=DomainCommandFixture.Bus(host.Services);var attendance=host.Services.GetRequiredService<IWeeklyAttendanceDomainService>();
        var reset=host.Services.GetRequiredService<IWeeklyAttendanceRuleService>();
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T22:00:00Z"),reset.WeekStart(DateTimeOffset.Parse("2026-10-05T06:00:00+08:00")));
        Assert.Equal(DateTimeOffset.Parse("2026-09-27T22:00:00Z"),reset.WeekStart(DateTimeOffset.Parse("2026-10-05T05:59:59+08:00")));
        for(var week=1;week<=4;week++)
        { await Clears(host);Assert.True(await bus.SendAsync(new Act(new(1),Claim:true)));Assert.False(await bus.SendAsync(new Act(new(1),Claim:true)));if(week<4)clock.Advance(TimeSpan.FromDays(7)); }
        var mails=await TestUseCase.Mailbox(host.Services).InboxAsync(new(1));Assert.Equal(4,mails.Count);Assert.Equal(5,mails.Sum(m=>m.Attachments.Length));
        Assert.Contains(mails.SelectMany(m=>m.Attachments),a=>a.Asset is Dfo.Server.Data.Mail.MailAsset.Stackable { ItemId:3033,Count:2 });
        csrf=await GmCeraHttpTests.Login(host);
        rules.Broken=true;Assert.Equal("unavailable",(await Get(host)).Status);Assert.False((await attendance.StateAsync(new(1),clock.GetUtcNow(),default)).Active);
        await Change(host,csrf,new(2208,2,"disable"));Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(Url,csrf,new EventChange(2208,3,"enable"))).StatusCode);
        rules.Broken=false;await Change(host,csrf,new(2208,3,"enable"));
        clock.Advance(TimeSpan.FromDays(800));await GmCeraHttpTests.Login(host);Assert.Equal("expired",(await Get(host)).Status);Assert.False(await bus.SendAsync(new Act(new(1),Claim:true)));
    }
    [PvfFact]
    public async Task Actual_A21_archive_uses_client_script_reward_order_and_delivers_declared_first_reward()
    {
        var path=new PvfContentOptions().ResolveArchivePath()!;
        await using var host=await GmTestHost.StartAsync(realContent:true,configuration:new Dictionary<string,string?> { ["Pvf:ArchivePath"]=path },configureServices:s=>
        { ConfigureDungeons(s);s.AddFixtureCommand<Act,bool,ActHandler>(FixtureUnitKind.Transactional); });
        await GmPlayerDirectoryHttpTests.SeedAsync(host);await Enable(host);
        var entry=(await host.Client.GetFromJsonAsync<WeeklyAttendanceSettings>(Url+"/weekly-attendance/settings"))!;
        Assert.True(entry.Available,entry.UnavailableReason);Assert.Equal("event/chn_event/chn_totalattendance.evt",entry.SourcePath);Assert.Equal(4,entry.RequiredClears);
        Assert.Equal(new[]{490003187,490003188,490003189,490003196,490003190,490003191,490003192,490003197,490003193,490003194,490003195,490003198},entry.WeeklyRewards.Select(r=>r.ItemId));
        Assert.Equal(new[]{4,8,11},entry.CumulativeRewards.Select(r=>r.AttendanceNo));
        var names=host.Services.GetRequiredService<IItemCatalogSource>();
        foreach(var reward in entry.WeeklyRewards.Concat(entry.CumulativeRewards))
        {
            Assert.False(string.IsNullOrWhiteSpace(reward.ItemName));
            Assert.Equal((await names.FindAsync(reward.ItemId,default))!.Name,reward.ItemName);
        }
        await Clears(host);Assert.True(await DomainCommandFixture.Bus(host.Services).SendAsync(new Act(new(1),Claim:true)));
        var mail=Assert.Single(await TestUseCase.Mailbox(host.Services).InboxAsync(new(1)));
        Assert.Equal(490003187,Assert.IsType<Dfo.Server.Data.Mail.MailAsset.Stackable>(Assert.Single(mail.Attachments).Asset).ItemId);
    }
    /// <summary>
    /// What the host's real registration adapter registered, read where the outbox hands it to the sender (TASK-25);
    /// an explicit delivery counts with its direct messages only, as before.
    /// </summary>
    private sealed class CaptureSender
    {
        private RecordedOutbound outbound=null!;
        internal RecordedMessages<IServerNetworkMessage> Messages=>outbound.CallMessages;
        public void Use(IServiceCollection services)=>outbound=RecordedOutbound.AddTo(services);
    }
    /// <summary>
    /// The dispatch group loop can take a delivery before <c>DrainOnceAsync</c> reads it; that delivery is
    /// then consumed by a job already on the executor, so a marker job enqueued afterwards waits for it.
    /// </summary>
    private static async Task Settled(GmTestHost host)=>
        await host.Services.GetRequiredService<Dfo.Server.Abstractions.Execution.IGameplayQueue>().RunAsync(
            new Dfo.Server.Abstractions.Execution.GameplayWork(Dfo.Server.Abstractions.Execution.GameplayShardKey.World,
                Dfo.Server.Abstractions.Execution.GameplayCallChain.NewRoot(Dfo.Server.Abstractions.Execution.GameplayCallSource.Gm),
                "test:settled",_=>ValueTask.CompletedTask),default);
    private static byte[][] Frames(GmTestHost host,CaptureSender sender)=>sender.Messages.SelectMany(message=>
    {
        var registration=host.Services.GetServices<ServerMessageEncoderRegistration>().Single(r=>r.MessageType==message.GetType());
        return ((IServerMessageEncoder)host.Services.GetRequiredService(registration.EncoderType)).Encode(message);
    }).ToArray();
    [PvfFact]
    public async Task Durable_clear_command_decoder_dispatcher_claim_and_online_close_refresh_the_native_frames()
    {
        var sender=new CaptureSender();await using var host=await GmTestHost.StartAsync(configureServices:s=>{Content(s);sender.Use(s);});
        await GmPlayerDirectoryHttpTests.SeedAsync(host);var csrf=await Enable(host);
        await host.Services.GetRequiredService<Dfo.Server.Application.DomainEvents.DomainEventDispatcher>().DrainOnceAsync();await Settled(host);
        var user=await GmPlayerDirectoryHttpTests.ConnectAsync(host,1,21011);var session=GmPlayerDirectoryHttpTests.Publish(host,user,1,1,21011);
        var bus=DomainCommandFixture.Bus(host.Services);var run=Guid.NewGuid();
        var clear=new ClearExperienceAlgorithmInput(new(1),new(1),1,new(86,0,1,50,1,1),Session:session,RunId:run,SettlementLevel:86);
        Assert.True((await bus.SendAsync(clear)).AttendanceChanged);Assert.False((await bus.SendAsync(clear)).AttendanceChanged);await Clears(host,3);
        var decoderInfo=host.Services.GetServices<GameMessageDecoderRegistration>().Single(r=>r.Selector==1 && r.Type==0x0441);
        var request=((IGameMessageDecoder)host.Services.GetRequiredService(decoderInfo.DecoderType)).Decode(new(1,0x0441,0,1),Enumerable.Repeat((byte)0xFE,14).ToArray())!;
        sender.Messages.Clear();await host.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(user.Connection),request,default);
        await host.Services.GetRequiredService<Dfo.Server.Application.DomainEvents.DomainEventDispatcher>().DrainOnceAsync();await Settled(host);
        var frames=Frames(host,sender);var ack=Assert.Single(frames,f=>f[0]==1 && BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(1))==0x0441);
        var beforeIds=ReadEventIds(Assert.Single(frames,f=>BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(1))==0x006C)[15..]);
        Assert.Contains((ushort)2208,beforeIds);
        Assert.Equal(19,ack.Length);Assert.Equal(0u,BinaryPrimitives.ReadUInt32LittleEndian(ack.AsSpan(15)));Assert.Contains(frames,f=>BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(1))==0x042D && f.Length==465);
        Assert.Single(await TestUseCase.Mailbox(host.Services).InboxAsync(new(1)));
        await host.Services.GetRequiredService<Dfo.Server.Application.DomainEvents.DomainEventDispatcher>().DrainOnceAsync();await Settled(host);
        sender.Messages.Clear();await Change(host,csrf,new(2208,2,"disable"));
        await host.Services.GetRequiredService<Dfo.Server.Application.DomainEvents.DomainEventDispatcher>().DrainOnceAsync();await Settled(host);frames=Frames(host,sender);
        var calendar=Assert.Single(frames,f=>BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(1))==0x006C);Assert.Equal(beforeIds.Where(id=>id!=2208),ReadEventIds(calendar[15..]));
        Assert.Contains(frames,f=>BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(1))==0x033F && BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(23))==3900 && BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(19))==1);
        var state=Assert.Single(frames,f=>BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(1))==0x042D);Assert.All(state[15..],b=>Assert.Equal(0,b));
        GmPlayerDirectoryHttpTests.Cleanup(host,session);
    }

    private static ushort[] ReadEventIds(byte[] body)
    {
        using var buffer = new MemoryStream(body);
        using var reader = new BinaryReader(buffer);
        var ids = new ushort[reader.ReadUInt16()];
        void Text() { var length = checked((int)reader.ReadUInt32()); Assert.Equal(length, reader.ReadBytes(length).Length); }
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = reader.ReadUInt16(); Assert.Equal(0u, reader.ReadUInt32()); Text(); Text();
            if (reader.ReadByte() == 0) continue;
            reader.ReadBytes(2); Text(); Text(); Text(); reader.ReadBytes(8); Text(); Text(); reader.ReadByte();
        }
        Assert.Equal(0, reader.ReadByte()); Assert.Equal(buffer.Length, buffer.Position);
        return ids;
    }
}
