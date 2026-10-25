using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace Dfo.Server.Application.Tests.World;
internal static class TowerTestSources
{
    internal static Task Tick(ShopFixture fixture, bool boss, CancellationToken token = default) => boss
        ? fixture.Services.GetServices<IHostedService>().OfType<BossTowerReturnSweepService>().Single().TickAsync(token)
        : fixture.Services.GetServices<IHostedService>().OfType<IllusionTowerReturnSweepService>().Single().TickAsync(token);
}
