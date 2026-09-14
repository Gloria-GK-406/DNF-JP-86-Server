using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Networking.Runtime;

/// <summary>
/// Answers the A21 client's pre-party NAT discovery probes on the two endpoints advertised by
/// NOTI 0x0001. This is endpoint discovery only; dungeon P2P traffic never traverses this service.
/// </summary>
internal sealed class A21UdpDiscoveryService : BackgroundService
{
    private readonly int portA;
    private readonly int portB;
    private readonly ILogger<A21UdpDiscoveryService> logger;
    private Socket? socketA;
    private Socket? socketB;

    public A21UdpDiscoveryService(ILogger<A21UdpDiscoveryService> logger)
        : this(A21UdpDiscoveryProtocol.PortA, A21UdpDiscoveryProtocol.PortB, logger)
    {
    }

    internal A21UdpDiscoveryService(
        int portA,
        int portB,
        ILogger<A21UdpDiscoveryService> logger)
    {
        this.portA = portA;
        this.portB = portB;
        this.logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        socketA = Bind(portA);
        try
        {
            socketB = Bind(portB);
            return base.StartAsync(cancellationToken);
        }
        catch
        {
            socketB?.Dispose();
            socketA?.Dispose();
            socketB = null;
            socketA = null;
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var first = socketA ?? throw new InvalidOperationException("UDP discovery port A is not bound.");
        var second = socketB ?? throw new InvalidOperationException("UDP discovery port B is not bound.");
        logger.LogInformation(
            "A21 UDP discovery listening on {PortA} and {PortB}", portA, portB);

        try
        {
            await Task.WhenAll(
                    ReceiveAsync(first, second, stoppingToken),
                    ReceiveAsync(second, first, stoppingToken))
                .ConfigureAwait(false);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
            socketA = null;
            socketB = null;
        }
    }

    private static Socket Bind(int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task ReceiveAsync(
        Socket receivingSocket,
        Socket alternateSocket,
        CancellationToken cancellationToken)
    {
        var request = new byte[1];
        EndPoint anyRemote = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var received = await receivingSocket.ReceiveFromAsync(
                        request, SocketFlags.None, anyRemote, cancellationToken)
                    .ConfigureAwait(false);
                if (received.ReceivedBytes != 1
                    || received.RemoteEndPoint is not IPEndPoint remote
                    || remote.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var responseSocket = request[0] switch
                {
                    A21UdpDiscoveryProtocol.BindingRequest => receivingSocket,
                    A21UdpDiscoveryProtocol.ChangePortRequest => alternateSocket,
                    A21UdpDiscoveryProtocol.KeepaliveRequest => null,
                    _ => null,
                };
                if (responseSocket is null)
                {
                    continue;
                }

                var response = EncodeResponse(remote);
                await responseSocket.SendToAsync(
                        response, SocketFlags.None, remote, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static byte[] EncodeResponse(IPEndPoint remote)
    {
        var response = new byte[A21UdpDiscoveryProtocol.ResponseLength];
        response[0] = A21UdpDiscoveryProtocol.BindingResponse;
        remote.Address.GetAddressBytes().CopyTo(response, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(5), checked((ushort)remote.Port));
        return response;
    }
}
