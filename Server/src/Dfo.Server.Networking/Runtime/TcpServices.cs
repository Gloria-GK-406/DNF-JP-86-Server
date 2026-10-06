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
    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (ServerState == ServerState.Running)
            return;

        // TouchSocket 4.3.1 arms Accept before marking the service Running. An
        // early connection can therefore stop the accept loop permanently.
        // Start without listeners, then bind them through the public API.
        var configuredOptions = Config.GetValue(TouchSocketConfigExtension.ListenOptionsProperty);
        var configuredHosts = Config.GetValue(TouchSocketConfigExtension.ListenIPHostsProperty);
        var listeners = new List<TcpListenOption>();
        if (configuredOptions is not null)
            listeners.AddRange(configuredOptions);
        if (configuredHosts is not null)
        {
            foreach (var host in configuredHosts)
                listeners.Add(new TcpListenOption
                {
                    IpHost = host,
                    ServiceSslOption = Config.GetValue(TouchSocketConfigExtension.ServiceSslOptionProperty),
                    ReuseAddress = Config.GetValue(TouchSocketConfigExtension.ReuseAddressProperty),
                    NoDelay = Config.GetValue(TouchSocketConfigExtension.NoDelayProperty),
                    Adapter = Config.GetValue(TouchSocketConfigExtension.SingleStreamDataHandlingAdapterProperty),
                    Backlog = Config.GetValue(TouchSocketConfigExtension.BacklogProperty),
                });
        }

        Config.SetValue(TouchSocketConfigExtension.ListenOptionsProperty, new List<TcpListenOption>());
        Config.SetValue(TouchSocketConfigExtension.ListenIPHostsProperty, Array.Empty<IPHost>());
        try
        {
            await base.StartAsync(cancellationToken).ConfigureAwait(false);
            foreach (var listener in listeners)
                AddListen(listener);
        }
        catch
        {
            await base.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            Config.SetValue(TouchSocketConfigExtension.ListenOptionsProperty, configuredOptions);
            Config.SetValue(TouchSocketConfigExtension.ListenIPHostsProperty, configuredHosts);
        }
    }

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
