using System.Net;
using System.Net.Http.Json;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Events;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Host.Tests;

public sealed class GmJoustHttpTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow()=>now; }

    [Theory]
    [InlineData("12:10:00", JoustPhase.Betting)]
    [InlineData("13:40:00", JoustPhase.QuarterFinal)]
    public async Task Starting_host_mid_round_recovers_before_http_is_ready(string time,JoustPhase phase)
    {
        var directory=GmTestHost.NewDirectory();
        // CookieContainer uses the real wall clock, independently of the host's
        // fixed clock. A future day keeps simulated 09:00+8h cookies valid.
        var offset=TimeSpan.FromHours(8);
        var day=DateTimeOffset.UtcNow.ToOffset(offset).Date.AddDays(1);
        var now=new DateTimeOffset(day+TimeSpan.Parse(time),offset);
        await using(var initial=await GmTestHost.StartAsync(directory:directory,
            clock:new FixedClock(new DateTimeOffset(day.AddHours(9),offset)),configureServices:Configure))
        {
            var csrf=await GmCeraHttpTests.Login(initial);
            using var configured=await initial.PostAsync("/api/gm/v1/events",csrf,
                new EventChange(2365,0,"configure","马战","马战","","","",now.AddMinutes(-1),now.AddDays(1)));
            Assert.Equal(HttpStatusCode.OK,configured.StatusCode);
            using var enabled=await initial.PostAsync("/api/gm/v1/events",csrf,new EventChange(2365,1,"enable"));
            Assert.Equal(HttpStatusCode.OK,enabled.StatusCode);
            Assert.Empty(await initial.ReadAsync("SELECT round_id FROM event_joust_round"));
        }
        await using var restarted=await GmTestHost.StartAsync(directory:directory,clock:new FixedClock(now),configureServices:Configure);
        Assert.Equal(HttpStatusCode.OK,(await restarted.Client.GetAsync("/api/gm/v1/health")).StatusCode);
        var state=await restarted.Services.GetRequiredService<IJoustProjectionDomainService>().ReadAsync(new(0),now,default);
        Assert.True(state.Active);Assert.Equal(phase,state.Phase);
        Assert.Equal(new DateTimeOffset(day.AddHours(12),offset),state.StartsAt);
        Assert.Single(await restarted.ReadAsync("SELECT round_id FROM event_joust_round"));
    }

    private const string Url="/api/gm/v1/events/joust/settings";
    [Fact]
    public async Task Schedule_changes_are_rejected_instead_of_overriding_the_fixed_client_timeline()
    {
        await using var host=await GmTestHost.StartAsync(configureServices:Configure);
        var csrf=await GmCeraHttpTests.Login(host);
        using var response=await host.PostAsync(Url,csrf,new{expectedRevision=0,schedule=JoustSchedule.Default});
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.Empty(await host.ReadAsync("SELECT id FROM event_joust_schedule"));
    }
    private sealed class Source : IJoustDefinitionSource
    {
        public ValueTask<JoustDefinition> ReadAsync(CancellationToken ct)
        {
            JoustItem Item(int id)=>new(id,$"币{id}",DateTimeOffset.Parse("2016-11-17T00:00:00+08:00"))
                {GrantTemplate=new(){ItemId=id,IsPile=true,LegacyKind=3,Type=ItemTraits.Material}};
            return ValueTask.FromResult(new JoustDefinition("fixture",17,1000,[Item(490002916),Item(490700609)],Item(490002916),Item(490002925),
                Enumerable.Range(0,12).Select(i=>new JoustKnight(i,$"骑士{i}",0,[],[])).ToArray()));
        }
    }
    private static void Configure(IServiceCollection s)=>s.AddSingleton<IJoustDefinitionSource>(new Source());
    private sealed class RoundClock : TimeProvider
    {
        public DateTimeOffset Now=new(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date.AddDays(1).AddHours(11).AddMinutes(35),TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    private const string CurrentUrl="/api/gm/v1/events/joust/current-round";
    private static async Task<string> Enable(GmTestHost host,RoundClock clock)
    {
        var csrf=await GmCeraHttpTests.Login(host);
        using var configured=await host.PostAsync("/api/gm/v1/events",csrf,new EventChange(2365,0,"configure","马战","马战","","","",clock.Now.AddDays(-1),clock.Now.AddDays(1)));
        Assert.Equal(HttpStatusCode.OK,configured.StatusCode);
        using var enabled=await host.PostAsync("/api/gm/v1/events",csrf,new EventChange(2365,1,"enable"));Assert.Equal(HttpStatusCode.OK,enabled.StatusCode);
        await host.Services.GetRequiredService<ICommandBus>().SendAsync(new AdvanceJoustLifecycleCommand(clock.Now),default);
        return csrf;
    }
    [Fact]
    public async Task Current_round_reads_are_pure_and_champion_requires_csrf_identity_revision_entrant_and_locked_phase()
    {
        var clock=new RoundClock();await using var host=await GmTestHost.StartAsync(clock:clock,configureServices:Configure);
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client.GetAsync(CurrentUrl)).StatusCode);
        var csrf=await Enable(host,clock);var current=(await host.Client.GetFromJsonAsync<JoustCurrentRound>(CurrentUrl))!;
        Assert.True(current.CanSetChampion);Assert.Equal(8,current.Knights.Length);
        var before=await host.ReadAsync("SELECT input FROM event_joust_round");
        for(var i=0;i<3;i++)await host.Client.GetFromJsonAsync<JoustCurrentRound>(CurrentUrl);
        Assert.Equal(before,await host.ReadAsync("SELECT input FROM event_joust_round"));
        var change=new JoustChampionChange(current.RoundId!,0,current.Knights[0].Index);
        Assert.Equal(HttpStatusCode.Forbidden,(await host.PostAsync(CurrentUrl+"/champion",null,change)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(CurrentUrl+"/champion",csrf,new{roundId=current.RoundId,expectedRevision=0})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await host.PostAsync(CurrentUrl+"/champion",csrf,change with{RoundId="wrong"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(CurrentUrl+"/champion",csrf,change with{KnightIndex=99})).StatusCode);
        using var response=await host.PostAsync(CurrentUrl+"/champion",csrf,change);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var saved=(await response.Content.ReadFromJsonAsync<JoustCurrentRound>())!;Assert.Equal(1,saved.Revision);Assert.Equal(change.KnightIndex,saved.ForcedChampionIndex);
        Assert.Equal(HttpStatusCode.Conflict,(await host.PostAsync(CurrentUrl+"/champion",csrf,change)).StatusCode);
        Assert.Single(await host.ReadAsync("SELECT operation_id FROM gm_audit WHERE action='SetJoustChampion'"));
        Assert.Empty(await host.ReadAsync("SELECT 1 FROM mailbox_messages"));
        clock.Now=current.ChampionDeadline!.Value;
        Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(CurrentUrl+"/champion",csrf,change with{ExpectedRevision=1,KnightIndex=null})).StatusCode);
        Assert.False((await host.Client.GetFromJsonAsync<JoustCurrentRound>(CurrentUrl))!.CanSetChampion);
    }
    [Fact]
    public async Task Champion_permission_and_audit_failure_cannot_change_round_or_freeze_odds()
    {
        await using(var denied=await GmTestHost.StartAsync(permissions:"QueryPlayers",configureServices:Configure))
        {
            var token=await GmCeraHttpTests.Login(denied);Assert.Equal(HttpStatusCode.Forbidden,(await denied.Client.GetAsync(CurrentUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await denied.PostAsync(CurrentUrl+"/champion",token,new JoustChampionChange("round",0,0))).StatusCode);
        }
        var clock=new RoundClock();var fault=new GmAuditFault();await using var host=await GmTestHost.StartAsync(clock:clock,fault:fault,configureServices:Configure);
        var csrf=await Enable(host,clock);var current=(await host.Client.GetFromJsonAsync<JoustCurrentRound>(CurrentUrl))!;
        var before=await host.ReadAsync("SELECT input FROM event_joust_round");fault.Throw=true;
        Assert.Equal(HttpStatusCode.InternalServerError,(await host.PostAsync(CurrentUrl+"/champion",csrf,new JoustChampionChange(current.RoundId!,0,current.Knights[0].Index))).StatusCode);
        Assert.Equal(before,await host.ReadAsync("SELECT input FROM event_joust_round"));
        Assert.Empty(await host.ReadAsync("SELECT operation_id FROM gm_audit WHERE action='SetJoustChampion'"));
    }
    [Fact]
    public async Task Save_and_restart_keep_schedule_and_readonly_content_with_revision_conflicts()
    {
        var directory=GmTestHost.NewDirectory();var parameters=new JoustGameplayParameters(1.5,1,.75,1.4,20,.6,2000);
        await using(var host=await GmTestHost.StartAsync(directory:directory,configureServices:Configure))
        {
            Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client.GetAsync(Url)).StatusCode);
            var csrf=await GmCeraHttpTests.Login(host);
            var first=(await host.Client.GetFromJsonAsync<JoustSettings>(Url))!;Assert.True(first.Available);Assert.Equal(0,first.Revision);Assert.Equal(12,first.Knights.Length);
            Assert.Equal(HttpStatusCode.Forbidden,(await host.PostAsync(Url,null,new JoustSettingsChange(0,parameters))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(Url,csrf,new{expectedRevision=0,parameters,minimumLevel=1})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(Url,csrf,new{expectedRevision=0,parameters=new{
                strongMultiplier=1.5,normalMultiplier=1,weakMultiplier=.75,counterMultiplier=1.4,historyPrior=20,bettingScale=2000}})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(Url,csrf,new JoustSettingsChange(0,parameters with{WeakMultiplier=2}))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(Url,csrf,new JoustSettingsChange(0,parameters with{BettingScale=0}))).StatusCode);
            using var saved=await host.PostAsync(Url,csrf,new JoustSettingsChange(0,parameters));Assert.Equal(HttpStatusCode.OK,saved.StatusCode);
            var value=(await saved.Content.ReadFromJsonAsync<JoustSettings>())!;Assert.Equal(1,value.Revision);Assert.Equal(JoustSchedule.Default,value.Schedule);Assert.Equal(parameters,value.Parameters);
            Assert.Equal(HttpStatusCode.Conflict,(await host.PostAsync(Url,csrf,new JoustSettingsChange(0,parameters))).StatusCode);
            Assert.Single(await host.ReadAsync("SELECT operation_id FROM gm_audit WHERE action='ChangeJoustParameters'"));
        }
        await using var restart=await GmTestHost.StartAsync(directory:directory,configureServices:Configure);await GmCeraHttpTests.Login(restart);
        var after=(await restart.Client.GetFromJsonAsync<JoustSettings>(Url))!;Assert.Equal(1,after.Revision);Assert.Equal(JoustSchedule.Default,after.Schedule);Assert.Equal(parameters,after.Parameters);
    }
    [Fact]
    public async Task Permission_and_audit_failure_cannot_leave_saved_settings()
    {
        await using(var denied=await GmTestHost.StartAsync(permissions:"QueryPlayers",configureServices:Configure))
        {var csrf=await GmCeraHttpTests.Login(denied);Assert.Equal(HttpStatusCode.Forbidden,(await denied.Client.GetAsync(Url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await denied.PostAsync(Url,csrf,new JoustSettingsChange(0,new(1.15,1,.85,1.25,10,.4,1000)))).StatusCode);}
        var fault=new GmAuditFault();await using var host=await GmTestHost.StartAsync(fault:fault,configureServices:Configure);
        var token=await GmCeraHttpTests.Login(host);fault.Throw=true;Assert.Equal(HttpStatusCode.InternalServerError,(await host.PostAsync(Url,token,new JoustSettingsChange(0,new(1.15,1,.85,1.25,10,.4,1000)))).StatusCode);
        Assert.Empty(await host.ReadAsync("SELECT id FROM event_joust_schedule"));Assert.Empty(await host.ReadAsync("SELECT operation_id FROM gm_audit WHERE action='ChangeJoustParameters'"));
    }
}
