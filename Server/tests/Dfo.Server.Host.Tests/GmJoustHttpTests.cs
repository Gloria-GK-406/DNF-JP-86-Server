using System.Net;
using System.Net.Http.Json;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
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
    [Fact]
    public async Task Save_and_restart_keep_schedule_and_readonly_content_with_revision_conflicts()
    {
        var directory=GmTestHost.NewDirectory();var schedule=new JoustSchedule("09:00",3,90,60,10,200);
        await using(var host=await GmTestHost.StartAsync(directory:directory,configureServices:Configure))
        {
            Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client.GetAsync(Url)).StatusCode);
            var csrf=await GmCeraHttpTests.Login(host);
            var first=(await host.Client.GetFromJsonAsync<JoustSettings>(Url))!;Assert.True(first.Available);Assert.Equal(0,first.Revision);Assert.Equal(12,first.Knights.Length);
            Assert.Equal(HttpStatusCode.Forbidden,(await host.PostAsync(Url,null,new JoustSettingsChange(0,schedule))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(Url,csrf,new{expectedRevision=0,schedule,minimumLevel=1})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(Url,csrf,new JoustSettingsChange(0,new("23:00",2,60,20,5,10)))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await host.PostAsync(Url,csrf,new JoustSettingsChange(0,new("10:00",7,120,90,10,400)))).StatusCode);
            using var saved=await host.PostAsync(Url,csrf,new JoustSettingsChange(0,schedule));Assert.Equal(HttpStatusCode.OK,saved.StatusCode);
            var value=(await saved.Content.ReadFromJsonAsync<JoustSettings>())!;Assert.Equal(1,value.Revision);Assert.Equal(schedule,value.Schedule);
            Assert.Equal(HttpStatusCode.Conflict,(await host.PostAsync(Url,csrf,new JoustSettingsChange(0,JoustSchedule.Default))).StatusCode);
            Assert.Single(await host.ReadAsync("SELECT operation_id FROM gm_audit WHERE action='ChangeJoustSchedule'"));
        }
        await using var restart=await GmTestHost.StartAsync(directory:directory,configureServices:Configure);await GmCeraHttpTests.Login(restart);
        var after=(await restart.Client.GetFromJsonAsync<JoustSettings>(Url))!;Assert.Equal(1,after.Revision);Assert.Equal(schedule,after.Schedule);
    }
    [Fact]
    public async Task Permission_and_audit_failure_cannot_leave_saved_settings()
    {
        await using(var denied=await GmTestHost.StartAsync(permissions:"QueryPlayers",configureServices:Configure))
        {var csrf=await GmCeraHttpTests.Login(denied);Assert.Equal(HttpStatusCode.Forbidden,(await denied.Client.GetAsync(Url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await denied.PostAsync(Url,csrf,new JoustSettingsChange(0,JoustSchedule.Default))).StatusCode);}
        var fault=new GmAuditFault();await using var host=await GmTestHost.StartAsync(fault:fault,configureServices:Configure);
        var token=await GmCeraHttpTests.Login(host);fault.Throw=true;Assert.Equal(HttpStatusCode.InternalServerError,(await host.PostAsync(Url,token,new JoustSettingsChange(0,JoustSchedule.Default))).StatusCode);
        Assert.Empty(await host.ReadAsync("SELECT id FROM event_joust_schedule"));Assert.Empty(await host.ReadAsync("SELECT operation_id FROM gm_audit WHERE action='ChangeJoustSchedule'"));
    }
}
