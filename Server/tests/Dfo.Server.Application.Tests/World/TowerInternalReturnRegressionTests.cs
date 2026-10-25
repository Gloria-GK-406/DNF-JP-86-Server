using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Legal_internal_timeout_return_removes_the_actual_run_and_releases_its_occupancy()
    {
        using var fixture = Fixture();
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            fixture.SetLevel(65);
            await fixture.Seed(65, 2749110, 3);
            Assert.NotNull((await Enter(fixture)).Run);
        });
        fixture.Clock.Advance(TimeSpan.FromSeconds(121));
        await fixture.Services.GetServices<IHostedService>().OfType<BossTowerReturnSweepService>().Single().TickAsync(default);
        TestGameplay.Run(fixture.Services, () => Assert.Null(Tower(fixture).Find(fixture.Session)));
    }
}

public sealed partial class IllusionTowerDomainServiceTests
{
    [Fact]
    public async Task Legal_internal_timeout_return_removes_the_actual_run_and_releases_its_occupancy()
    {
        using var fixture = Fixture();
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await fixture.Seed(65, 4183, 3);
            var run = (await Enter(fixture)).Run!;
            var context = Context(run, fixture.Session);
            await Tower(fixture).LoadAsync(context, default);
            await Tower(fixture).StartAsync(context, default);
            await TestEffectFootprints.Run([fixture.Session], cap => Tower(fixture).DieAsync(context, cap, default));
            Assert.True((await Tower(fixture).MarkSettledAsync(context, new([]), default)).Changed);
        });
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        await fixture.Services.GetServices<IHostedService>().OfType<IllusionTowerReturnSweepService>().Single().TickAsync(default);
        TestGameplay.Run(fixture.Services, () => Assert.Null(Tower(fixture).Find(fixture.Session)));
    }
}
