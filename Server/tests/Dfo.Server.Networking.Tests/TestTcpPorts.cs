using System.Net;
using System.Net.Sockets;

namespace Dfo.Server.Networking.Tests;

/// <summary>Probes the same IPv4 Any/non-reusable endpoint used by NetworkingModule.</summary>
/// <remarks>
/// Never offer a port twice in this test process, including after a previous host stops.
/// This excludes earlier fixture ports, including hosts with lingering TIME_WAIT connections.
/// The probe must still close before TouchSocket binds: external processes and ephemeral
/// client connections can race that handoff. Startup assertions report such failures.
/// </remarks>
internal static class TestTcpPorts
{
    private static readonly object Gate = new();
    private static readonly HashSet<int> Allocated = [];

    internal static int Allocate()
    {
        lock (Gate)
        {
            for (var attempt = 0; attempt < 1024; attempt++)
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                socket.Listen(1);
                var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
                if (Allocated.Add(port)) return port;
            }
            throw new InvalidOperationException(
                $"Could not allocate a new test TCP port after 1024 probes; {Allocated.Count} ports already used in this process.");
        }
    }

    internal static int[] Allocate(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return Enumerable.Range(0, count).Select(_ => Allocate()).ToArray();
    }
}
