using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Actual_tick_drains_departures_before_taking_the_due_scan_clock()
    {
        var trace = new TowerReturnSourceTrace();
        var clock = new TowerReturnOrderClock(trace.Steps);
        using var fixture = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>();
        });
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            fixture.SetLevel(65);
            await fixture.Seed(65, 2749110, 3);
            var peer = await AddMember(fixture, 3);
            var run = (await Tower(fixture).EnterAsync([new(fixture.Session, 0), new(peer, 1)], fixture.Session, 42, default)).Run!;
            await Tower(fixture).LoadAsync(Context(run, fixture.Session), default);
            await Tower(fixture).LoadAsync(Context(run, peer), default);
            var departure = await TestEffectFootprints.Run([fixture.Session, peer],
                cap => Tower(fixture).LeaveAsync(Context(run, fixture.Session), false, cap, default));
            fixture.Services.GetRequiredService<BossTowerDepartureProjectionQueue>().Enqueue(departure);
            Assert.NotNull((await Enter(fixture)).Run);
            Assert.Empty(Tower(fixture).FindReturnsDue(clock.GetUtcNow()));
        });
        clock.Observe = true;
        fixture.Sender.Outbound.BeforeExplicit = () =>
        {
            trace.Steps.Add("departure-projection");
            clock.Now = clock.Now.AddMinutes(5);
        };

        await fixture.Services.GetServices<IHostedService>().OfType<BossTowerReturnSweepService>().Single().TickAsync(default);

        var leave = Assert.Single(trace.Attempts, attempt => attempt.Command is TowerUnitObserved observed && observed.Session == fixture.Session);
        Assert.Equal(fixture.Session, leave.Session);
        Assert.Null(leave.Error);
        Assert.True(trace.Steps.IndexOf("departure-projection") < trace.Steps.IndexOf("party-start-boss-tower"));
        Assert.Equal("clock", trace.Steps[trace.Steps.IndexOf("party-leave-boss-tower") - 1]);
    }
}

public sealed partial class IllusionTowerDomainServiceTests
{
    [Fact]
    public async Task Actual_tick_drains_departures_before_taking_the_due_scan_clock()
    {
        var trace = new TowerReturnSourceTrace();
        var clock = new TowerReturnOrderClock(trace.Steps);
        using var fixture = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>();
        });
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await fixture.Seed(65, 4183, 3);
            var peer = await AddMember(fixture, 3);
            var run = (await Tower(fixture).EnterAsync([new(fixture.Session, 0), new(peer, 1)], fixture.Session, 42, default, 11001)).Run!;
            await Tower(fixture).LoadAsync(Context(run, fixture.Session), default);
            await Tower(fixture).LoadAsync(Context(run, peer), default);
            var departure = await TestEffectFootprints.Run([fixture.Session, peer],
                cap => Tower(fixture).LeaveAsync(Context(run, fixture.Session), false, cap, default));
            fixture.Services.GetRequiredService<IllusionTowerDepartureProjectionQueue>().Enqueue(departure);
            run = (await Enter(fixture)).Run!;
            var context = Context(run, fixture.Session);
            await Tower(fixture).LoadAsync(context, default);
            await Tower(fixture).StartAsync(context, default);
            await TestEffectFootprints.Run([fixture.Session], cap => Tower(fixture).DieAsync(context, cap, default));
            Assert.True((await Tower(fixture).MarkSettledAsync(context, new([]), default)).Changed);
            Assert.Empty(Tower(fixture).FindReturnsDue(clock.GetUtcNow()));
        });
        clock.Observe = true;
        fixture.Sender.Outbound.BeforeExplicit = () =>
        {
            trace.Steps.Add("departure-projection");
            clock.Now = clock.Now.AddMinutes(5);
        };

        await fixture.Services.GetServices<IHostedService>().OfType<IllusionTowerReturnSweepService>().Single().TickAsync(default);

        var leave = Assert.Single(trace.Attempts);
        Assert.Equal(fixture.Session, leave.Session);
        Assert.Null(leave.Error);
        Assert.True(trace.Steps.IndexOf("departure-projection") < trace.Steps.IndexOf("party-leave-illusion-tower"));
        Assert.Equal("clock", trace.Steps[trace.Steps.IndexOf("party-leave-illusion-tower") - 1]);
    }
}

internal sealed class TowerReturnOrderClock(List<string> steps) : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    internal bool Observe { get; set; }
    public override DateTimeOffset GetUtcNow()
    {
        if (Observe) steps.Add("clock");
        return Now;
    }
}
