namespace Dfo.Server.Host.Tests;

public sealed class LauncherAuthenticationEndToEndTests
{
    [Fact]
    public async Task HttpChannelAndGame_UseOnlyTheAdvertisedTuplesAndPreserveTheAccountIdentity()
    {
        await using var harness = await EndToEndHostHarness.StartAsync();

        await harness.VerifySuccessfulAdvertisedRouteAsync();
    }
}
