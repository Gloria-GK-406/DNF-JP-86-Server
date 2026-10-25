using Dfo.Server.Application.Tests.Inventory;
using Xunit;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Every_declared_exact_physical_survives_a_previous_session_only_participant_entry()
    {
        using var fixture = Fixture(configure: TowerPhysicalProtectionProbe.Install);
        var observation = await TowerPhysicalProtectionProbe.RunAsync(fixture);
        Assert.True(observation.FirstBlocked);
        Assert.True(observation.SecondBlocked);
        Assert.Equal([fixture.Session.ConnectionId, observation.Second.ConnectionId], fixture.Sender.Outbound.Closes);
    }
}
