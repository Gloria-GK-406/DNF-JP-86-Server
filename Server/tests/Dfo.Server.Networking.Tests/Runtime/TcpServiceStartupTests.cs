using System.Net;
using System.Net.Sockets;
using Dfo.Server.Networking.Runtime;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Tests.Runtime;

public sealed class TcpServiceStartupTests
{
    [Fact]
    public async Task Listeners_are_bound_only_after_running_and_survive_restart()
    {
        using var service = new ObservedService();
        await service.SetupAsync(new TouchSocketConfig()
            .SetListenIPHosts(new IPHost("127.0.0.1:0"))
            .SetListenOptions(list => list.Add(new TcpListenOption { IpHost = new IPHost("127.0.0.1:0") })));
        for (var iteration = 0; iteration < 2; iteration++)
        {
            await service.StartAsync();
            Assert.Equal(2, service.Monitors.Count());
            await service.StartAsync();
            Assert.Equal(2, service.Monitors.Count());
            await service.StopAsync();
        }
        Assert.Equal(4, service.StatesAtBind.Count);
        Assert.All(service.StatesAtBind, state => Assert.Equal(ServerState.Running, state));
    }

    [Fact]
    public async Task Failed_bind_cleans_up_listeners_and_can_retry_original_configuration()
    {
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var port = ((IPEndPoint)occupied.LocalEndpoint).Port;
        using var service = new ObservedService();
        await service.SetupAsync(new TouchSocketConfig().SetListenIPHosts(
            new IPHost("127.0.0.1:0"), new IPHost($"127.0.0.1:{port}")));
        await Assert.ThrowsAsync<SocketException>(() => service.StartAsync());
        Assert.Empty(service.Monitors);
        Assert.Equal(ServerState.Stopped, service.ServerState);
        occupied.Stop();
        await service.StartAsync();
        Assert.Equal(2, service.Monitors.Count());
        await service.StopAsync();
    }

    private sealed class ObservedService : DrainingTcpService
    {
        public List<ServerState> StatesAtBind { get; } = [];

        protected override void PreviewBind(TcpNetworkMonitor monitor)
        {
            StatesAtBind.Add(ServerState);
            base.PreviewBind(monitor);
        }
    }
}
