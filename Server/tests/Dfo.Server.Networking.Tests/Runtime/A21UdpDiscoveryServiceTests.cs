using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Networking.Tests.Runtime;

public sealed class A21UdpDiscoveryServiceTests
{
    [Fact]
    public async Task Binding_probe_returns_the_observed_ipv4_and_port_from_the_receiving_port()
    {
        var (portA, portB) = ReserveDistinctUdpPorts();
        using var service = new A21UdpDiscoveryService(
            portA, portB, NullLogger<A21UdpDiscoveryService>.Instance);
        await service.StartAsync(CancellationToken.None);

        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await client.SendAsync([0x01], 1, new IPEndPoint(IPAddress.Loopback, portA));
        var received = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));

        var local = Assert.IsType<IPEndPoint>(client.Client.LocalEndPoint);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, portA), received.RemoteEndPoint);
        Assert.Equal(7, received.Buffer.Length);
        Assert.Equal(0x02, received.Buffer[0]);
        Assert.Equal(IPAddress.Loopback.GetAddressBytes(), received.Buffer.AsSpan(1, 4).ToArray());
        Assert.Equal(local.Port, BinaryPrimitives.ReadUInt16LittleEndian(received.Buffer.AsSpan(5)));

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Change_port_probe_is_answered_from_the_other_configured_port()
    {
        var (portA, portB) = ReserveDistinctUdpPorts();
        using var service = new A21UdpDiscoveryService(
            portA, portB, NullLogger<A21UdpDiscoveryService>.Instance);
        await service.StartAsync(CancellationToken.None);

        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await client.SendAsync([0x05], 1, new IPEndPoint(IPAddress.Loopback, portA));
        var received = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(new IPEndPoint(IPAddress.Loopback, portB), received.RemoteEndPoint);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Keepalive_does_not_produce_a_response()
    {
        var (portA, portB) = ReserveDistinctUdpPorts();
        using var service = new A21UdpDiscoveryService(
            portA, portB, NullLogger<A21UdpDiscoveryService>.Instance);
        await service.StartAsync(CancellationToken.None);

        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await client.SendAsync([0x00], 1, new IPEndPoint(IPAddress.Loopback, portB));

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.ReceiveAsync().WaitAsync(TimeSpan.FromMilliseconds(250)));

        await service.StopAsync(CancellationToken.None);
    }

    private static (int PortA, int PortB) ReserveDistinctUdpPorts()
    {
        using var first = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var second = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return (
            Assert.IsType<IPEndPoint>(first.Client.LocalEndPoint).Port,
            Assert.IsType<IPEndPoint>(second.Client.LocalEndPoint).Port);
    }
}
