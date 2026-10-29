using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Actual_two_departures_isolate_first_host_encoder_failure_and_register_the_later_host()
    {
        using var fixture = Fixture();
        await TowerDepartureProjectionCases.RunAsync(fixture, true);
    }
}

public sealed partial class IllusionTowerDomainServiceTests
{
    [Fact]
    public async Task Actual_two_departures_isolate_first_host_encoder_failure_and_register_the_later_host()
    {
        using var fixture = Fixture();
        await TowerDepartureProjectionCases.RunAsync(fixture, false);
    }
}

internal static class TowerDepartureProjectionCases
{
    internal static async Task RunAsync(ShopFixture fixture, bool boss)
    {
        SelectedSessionIdentity first = null!, second = null!;
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            fixture.SetLevel((byte)(boss ? 65 : 50));
            await fixture.Seed(65, boss ? 2749110 : 4183, 3);
            first = await TowerSourceMembers.AddAsync(fixture, 3, 2, boss ? 65 : 50, boss ? 2749110 : 4183);
            var third = await TowerSourceMembers.AddAsync(fixture, 4, 3, boss ? 65 : 50, boss ? 2749110 : 4183);
            second = await TowerSourceMembers.AddAsync(fixture, 5, 4, boss ? 65 : 50, boss ? 2749110 : 4183);
            foreach (var cohort in new[] { new[] { fixture.Session, first }, new[] { third, second } })
            {
                if (boss)
                {
                    var towers = fixture.Services.GetRequiredService<IBossTowerDomainService>();
                    var run = (await towers.EnterAsync([new(cohort[0], 0), new(cohort[1], 1)], cohort[0], 42, default)).Run!;
                    await towers.LoadAsync(new(cohort[0], run.RunId, run.Room.Version), default);
                    await towers.LoadAsync(new(cohort[1], run.RunId, run.Room.Version), default);
                    var departure = await TestEffectFootprints.Run(cohort,
                        cap => towers.LeaveAsync(new(cohort[0], run.RunId, run.Room.Version), false, cap, default));
                    fixture.Services.GetRequiredService<BossTowerDepartureProjectionQueue>().Enqueue(departure);
                }
                else
                {
                    var towers = fixture.Services.GetRequiredService<IIllusionTowerDomainService>();
                    var run = (await towers.EnterAsync([new(cohort[0], 0), new(cohort[1], 1)], cohort[0], 42, default, 11001)).Run!;
                    await towers.LoadAsync(new(cohort[0], run.RunId, run.Room.Version), default);
                    await towers.LoadAsync(new(cohort[1], run.RunId, run.Room.Version), default);
                    var departure = await TestEffectFootprints.Run(cohort,
                        cap => towers.LeaveAsync(new(cohort[0], run.RunId, run.Room.Version), false, cap, default));
                    fixture.Services.GetRequiredService<IllusionTowerDepartureProjectionQueue>().Enqueue(departure);
                }
            }
        });
        var cause = new IOException("first departure actual host encoder failure");
        var failures = 0;
        fixture.Sender.Outbound.Fail(first.ConnectionId, message => message.MessageType == ServerMessageType.GameUdpHost,
            () => { failures++; return cause; });
        var source = boss
            ? (Func<Task>)(() => fixture.Services.GetServices<IHostedService>().OfType<BossTowerReturnSweepService>().Single().TickAsync(default))
            : () => fixture.Services.GetServices<IHostedService>().OfType<IllusionTowerReturnSweepService>().Single().TickAsync(default);

        await source();

        Assert.Equal(1, failures);
        Assert.DoesNotContain(fixture.Sender.Outbound.MessagesFor(first.ConnectionId), message => message.MessageType == ServerMessageType.GameUdpHost);
        Assert.Contains(fixture.Sender.Outbound.MessagesFor(second.ConnectionId), message => message.MessageType == ServerMessageType.GameUdpHost);
        Assert.Equal([first.ConnectionId], fixture.Sender.Outbound.Closes);
        TestGameplay.Run(fixture.Services, () =>
        {
            if (boss)
            {
                var towers = fixture.Services.GetRequiredService<IBossTowerDomainService>();
                Assert.Equal(TowerRoomPhase.Fighting, towers.Find(first)!.Room.Phase);
                Assert.Equal(TowerRoomPhase.Fighting, towers.Find(second)!.Room.Phase);
            }
            else
            {
                var towers = fixture.Services.GetRequiredService<IIllusionTowerDomainService>();
                Assert.Equal(TowerRoomPhase.Ready, towers.Find(first)!.Room.Phase);
                Assert.Equal(TowerRoomPhase.Ready, towers.Find(second)!.Room.Phase);
            }
        });
        var delivered = fixture.Sender.Outbound.Entries.Count;
        await source();
        Assert.Equal(delivered, fixture.Sender.Outbound.Entries.Count);
        Assert.Equal(1, failures);
    }
}
