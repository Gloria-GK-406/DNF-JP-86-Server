using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

internal interface IOrderedTcpSessionClose
{
    ValueTask CloseSocketAfterFlushAsync(CancellationToken cancellationToken);
}

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

    private sealed class DrainingSession : TcpSessionClient, IOrderedTcpSessionClose
    {
        private const string PinnedTcpTransport = "TouchSocket.Sockets.TcpTransport, TouchSocket, Version=4.3.1.0, Culture=neutral, PublicKeyToken=5f39d7da98dac6a9";
        private const string PinnedBaseTransport = "TouchSocket.Sockets.BaseTransport, TouchSocket, Version=4.3.1.0, Culture=neutral, PublicKeyToken=5f39d7da98dac6a9";
        private readonly object closeCoordination = new();
        private TaskCompletionSource? orderedSocketClosed;
        private bool closeCallbackEntered;

        public async ValueTask CloseSocketAfterFlushAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource completion;
            var ownsPhase = false;
            lock (closeCoordination)
            {
                if (orderedSocketClosed is { } existing) completion = existing;
                else
                {
                    if (closeCallbackEntered) return;
                    completion = orderedSocketClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    ownsPhase = true;
                }
            }
            if (!ownsPhase)
            {
                await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            try
            {
                var transport = Transport;
                var socket = TransportSocket(transport);
                Exception? failure = null;
                if (!TryBeginTransportClose(transport, "outbound close")) return;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SignalTransportClose(transport);
                    socket.LingerState = new LingerOption(false, 0);
                    socket.Shutdown(SocketShutdown.Both);
                    // Native receives in 4.3.1 have no cancellation token. Shutdown releases that
                    // owner; its two transport tasks must finish before this close-only reader starts.
                    await WaitForTransportClosedAsync(transport, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
                    var discarded = new byte[8192];
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (await socket.ReceiveAsync(discarded.AsMemory(), SocketFlags.None, cancellationToken).ConfigureAwait(false) == 0)
                            break;
                    }
                }
                catch (ObjectDisposedException) { }
                catch (SocketException error) when (error.SocketErrorCode is SocketError.NotConnected
                    or SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown) { }
                catch (Exception error) { failure = error; }
                try
                {
                    // An unread receive queue can reset TCP even on a default close. Once the
                    // local queue is drained, keep the accepted send queue with TCP rather than Close(0).
                    socket.Close();
                }
                catch (Exception cleanup)
                {
                    failure = failure is null ? cleanup : new AggregateException("Ordered socket shutdown and release both failed.", failure, cleanup);
                }
                if (failure is not null) ExceptionDispatchInfo.Throw(failure);
            }
            finally { completion.TrySetResult(); }
        }

        // Fixed TouchSocket 4.3.1 compatibility seam: upgrades must revalidate these exact
        // members, stopped receive ownership, the small-window FIN, and the close deadline.
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "m_socket")]
        private static extern ref Socket TransportSocket([UnsafeAccessorType(PinnedTcpTransport)] object transport);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "TryBeginClose")]
        private static extern bool TryBeginTransportClose([UnsafeAccessorType(PinnedBaseTransport)] object transport, string message);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "SignalClose")]
        private static extern void SignalTransportClose([UnsafeAccessorType(PinnedBaseTransport)] object transport);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "WaitForTransportClosedAsync")]
        private static extern Task WaitForTransportClosedAsync([UnsafeAccessorType(PinnedBaseTransport)] object transport, CancellationToken cancellationToken);

        protected override async Task OnTcpClosed(ClosedEventArgs e)
        {
            Task? ordered;
            lock (closeCoordination)
            {
                closeCallbackEntered = true;
                ordered = orderedSocketClosed?.Task;
            }
            if (ordered is not null) await ordered.ConfigureAwait(false);
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
