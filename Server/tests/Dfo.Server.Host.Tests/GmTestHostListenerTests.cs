using System.Net;
using System.Net.Sockets;
using Dfo.Server.GmWeb;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

public sealed class GmTestHostListenerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_ephemeral_port_remains_bound_until_Kestrel_starts(bool https)
    {
        await using var host = await GmTestHost.StartAsync(https: https, wrapFeature: feature =>
        {
            // This runs before Build/StartAsync, inside the former release/rebind gap.
            using var competitor = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var failure = Assert.Throws<SocketException>(() =>
                competitor.Bind(new IPEndPoint(IPAddress.Loopback, feature.ListenUrl!.Port)));
            Assert.Equal(SocketError.AddressAlreadyInUse, failure.SocketErrorCode);
            return feature;
        });

        Assert.True(host.Address.Port > 0);
        Assert.Equal(https ? "https" : "http", host.Address.Scheme);
        Assert.Equal(host.Address.Port, host.Services.GetRequiredService<GmWebOptions>().ListenUrl!.Port);
        using var response = await host.Client.GetAsync("/api/gm/v1/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_explicit_listener_port_can_be_reused_after_the_previous_host_stops()
    {
        int port;
        await using (var first = await GmTestHost.StartAsync())
            port = first.Address.Port;

        await using var restarted = await GmTestHost.StartAsync(listenerPort: port);
        Assert.Equal(port, restarted.Address.Port);
        using var response = await restarted.Client.GetAsync("/api/gm/v1/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Failure_before_Kestrel_starts_releases_the_reserved_socket()
    {
        Uri? reservedAddress = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => GmTestHost.StartAsync(wrapFeature: feature =>
        {
            reservedAddress = feature.ListenUrl;
            throw new InvalidOperationException("Injected failure before host construction.");
        }));

        Assert.NotNull(reservedAddress);
        using var nextOwner = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        nextOwner.Bind(new IPEndPoint(IPAddress.Loopback, reservedAddress.Port));
    }
}
