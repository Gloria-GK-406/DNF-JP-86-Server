namespace Dfo.Server.Host.Tests;
public sealed class AvatarCompoundTcpTests
{
    [Fact]
    public async Task Packaged_host_routes_compound_and_malformed_requests_to_CMD_refusal_without_disconnect()
    {
        await using var harness = await EndToEndHostHarness.StartAsync();
        await harness.VerifyAvatarCompoundRefusalAsync();
    }
}
