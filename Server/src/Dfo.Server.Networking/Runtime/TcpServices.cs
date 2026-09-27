using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

public interface IChannelTcpService : ITcpService;

public sealed class ChannelTcpService : DrainingTcpService, IChannelTcpService;

public interface IGameTcpService : ITcpService;

public sealed class GameTcpService : DrainingTcpService, IGameTcpService;

/// <summary>Drain the transport before TouchSocket returns its receive core to the pool.</summary>
/// <remarks>
/// TouchSocket 4.3.1 removes a closed session and recycles its TcpCore after OnTcpClosed.
/// Its session receive loop can finish before the transport's pending socket receive does.
/// Fast reconnects on Linux then reuse a SocketAsyncEventArgs still in flight and disconnect.
/// Await the transport (receive/send tasks), never the session CloseAsync from this callback:
/// the latter waits for this very callback and would deadlock.
/// </remarks>
public abstract class DrainingTcpService : TcpService<TcpSessionClient>, ITcpService
{
    protected override TcpSessionClient NewClient() => new DrainingSession();

    private sealed class DrainingSession : TcpSessionClient
    {
        protected override async Task OnTcpClosed(ClosedEventArgs e)
        {
            try
            {
                var result = await Transport.CloseAsync(e.Message).ConfigureAwait(false);
                if (!result.IsSuccess)
                    Logger?.Warning(this, $"Transport drain for connection {Id} failed: {result.Message}");
            }
            finally
            {
                await base.OnTcpClosed(e).ConfigureAwait(false);
            }
        }
    }
}
