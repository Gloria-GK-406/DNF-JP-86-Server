using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dfo.Server.Abstractions.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

public sealed class GmServerInfoHttpTests
{
    [Fact]
    public async Task ServerInfoReportsConfiguredListenersVersionAndArchiveIdentity()
    {
        await using var host = await GmTestHost.StartAsync(permissions: "QueryPlayers", configuration: new Dictionary<string, string?>
        {
            ["Networking:ChannelPort"] = "7101", ["Networking:GamePorts:0"] = "10110", ["Networking:GamePorts:1"] = "10111",
        }, configureServices: services => services.AddSingleton<IContentArchiveIdentity>(new FixedIdentity("ab12")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/gm/v1/server-info")).StatusCode);
        using var login = await host.LoginAsync(await host.CsrfAsync());
        var info = await host.Client.GetFromJsonAsync<JsonElement>("/api/gm/v1/server-info");
        var version = info.GetProperty("version").GetString()!;
        Assert.Matches(@"^\d+\.\d+\.\d+", version); Assert.DoesNotContain('+', version);
        Assert.False(info.GetProperty("networkEnabled").GetBoolean());
        Assert.Equal(7101, info.GetProperty("channelPort").GetInt32());
        Assert.Equal([10110, 10111], info.GetProperty("gamePorts").EnumerateArray().Select(port => port.GetInt32()));
        Assert.Equal(host.Address.Port, info.GetProperty("gmPort").GetInt32());
        Assert.Equal("ab12", info.GetProperty("pvfSha256").GetString());
    }

    private sealed class FixedIdentity(string? hash) : IContentArchiveIdentity
    {
        public ValueTask<string?> Sha256Async(CancellationToken cancellationToken) => ValueTask.FromResult(hash);
    }
}
