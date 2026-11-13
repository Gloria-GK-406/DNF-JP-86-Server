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
        Assert.Equal(new byte[] { 1, 0, 0, 127 }, received.Buffer.AsSpan(1, 4).ToArray());
        Assert.Equal(local.Port, BinaryPrimitives.ReadUInt16LittleEndian(received.Buffer.AsSpan(5)));

        await service.StopAsync(CancellationToken.None);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("203.0.113.230", 5063)]
    [InlineData("198.51.100.7", 30459)]
    [InlineData("192.168.8.102", 53432)]
    public void Response_decodes_to_the_observed_endpoint_the_way_the_client_reads_it(
        string address, int port)
    {
        var observed = new IPEndPoint(IPAddress.Parse(address), port);

        var response = A21UdpDiscoveryService.EncodeResponse(observed);

        // CNRDUdpIdentify: mov eax,[edi+1]; htonl -> mapped address in network order.
        var mapped = (uint)IPAddress.HostToNetworkOrder(
            (int)BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(1)));
        var mappedBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(mappedBytes, mapped);
        // movzx ecx,word [edi+5]; htons: the wire value is the host-order port itself.
        var mappedPort = BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(5));

        Assert.Equal(7, response.Length);
        Assert.Equal(0x02, response[0]);
        Assert.Equal(observed.Address, new IPAddress(mappedBytes));
        Assert.Equal(port, mappedPort);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Response_accepts_an_ipv4_mapped_ipv6_source()
    {
        var observed = new IPEndPoint(IPAddress.Parse("203.0.113.230").MapToIPv6(), 5063);

        var response = A21UdpDiscoveryService.EncodeResponse(observed);

        Assert.Equal(new byte[] { 230, 113, 0, 203 }, response.AsSpan(1, 4).ToArray());
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
