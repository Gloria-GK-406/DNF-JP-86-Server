using System.Net;
using System.Net.Sockets;
using Dfo.Server.Networking.Configuration;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Tests.Runtime;

public sealed class TestTcpFixtureTests
{
    [Fact]
    public async Task Concurrent_and_later_allocations_do_not_reuse_ports()
    {
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(TestTcpPorts.Allocate)));
        var later = TestTcpPorts.Allocate(32);
        Assert.Equal(64, concurrent.Concat(later).Distinct().Count());
    }

    [Fact]
    public async Task A_failed_game_bind_reports_the_listener_state_ports_and_original_exception()
    {
        var ports = TestTcpPorts.Allocate(2);
        using var occupied = new TcpListener(IPAddress.Any, ports[1]);
        occupied.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
        occupied.Start();
        using var channel = new ChannelTcpService();
        using var game = new GameTcpService();
        await channel.SetupAsync(new TouchSocketConfig().SetListenIPHosts(new IPHost(ports[0])));
        await game.SetupAsync(new TouchSocketConfig().SetListenIPHosts(new IPHost(ports[1])));
        await channel.StartAsync();
        var bindFailure = await Assert.ThrowsAsync<SocketException>(() => game.StartAsync());
        var logs = new TestTcpStartupLog();
        logs.CreateLogger(nameof(IGameTcpService)).LogError(bindFailure, "Game bind failed");
        using var services = new ServiceCollection()
            .AddSingleton(new NetworkingOptions { Enabled = true, ChannelPort = ports[0], GamePorts = [ports[1]] })
            .AddSingleton<IChannelTcpService>(channel)
            .AddSingleton<IGameTcpService>(game)
            .BuildServiceProvider();

        var rejected = Assert.Throws<Xunit.Sdk.TrueException>(() =>
            TestTcpStartup.AssertReady(services, logs, ports[1]));
        Assert.Contains($"Channel={ports[0]}", rejected.Message);
        Assert.Contains($"Game=[{ports[1]}]", rejected.Message);
        Assert.Contains("Game state=Stopped", rejected.Message);
        Assert.Contains(bindFailure.ToString(), rejected.Message);
        await channel.StopAsync();
    }
}
