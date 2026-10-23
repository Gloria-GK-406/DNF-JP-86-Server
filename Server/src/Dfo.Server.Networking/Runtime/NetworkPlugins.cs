using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Microsoft.Extensions.Logging;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

internal abstract class NetworkPluginBase(
    TouchSocketConnectionRegistry connections,
    INetworkConnectionLifecycleDispatcher lifecycle,
    NetworkEndpointKind endpointKind) : PluginBase, ITcpConnectedPlugin, ITcpClosedPlugin
{
    protected TouchSocketConnectionRegistry Connections { get; } = connections;

    protected NetworkEndpointKind EndpointKind { get; } = endpointKind;

    // Stream boundaries, like the receive callbacks. The registry is transport state and changes
    // here, before the lifecycle CMD is enqueued. A lifecycle job that fails has already registered the
    // close of this connection after its other entries (RQ-031(c)) and the executor has logged it, so
    // the failure is not passed on to TouchSocket, whose own close would overtake those frames.
    public Task OnTcpConnected(ITcpSession client, ConnectedEventArgs e) => GameplayJob.RunDetachedAsync(async () =>
    {
        var context = Connections.Add(client, EndpointKind);
        try
        {
            await lifecycle.DispatchConnectedAsync(context, CancellationToken.None);
        }
        catch (Exception error) when (GameplayJob.EndedAJob(error))
        {
            return;
        }

        await e.InvokeNext();
    }).AsTask();

    // Counted as in flight from the removal until its lifecycle CMD has run, so the transport drain
    // cannot see an empty registry while a teardown is still to be enqueued.
    public Task OnTcpClosed(ITcpSession client, ClosedEventArgs e) => GameplayJob.RunDetachedAsync(async () =>
    {
        Connections.BeginClosing();
        try
        {
            var connectionId = TouchSocketConnectionRegistry.CreateConnectionId(client, EndpointKind);
            if (Connections.TryRemove(connectionId, out var context))
            {
                await lifecycle.DispatchDisconnectedAsync(context!, CancellationToken.None);
            }
        }
        finally
        {
            Connections.EndClosing();
        }

        await e.InvokeNext();
    }).AsTask();
}

internal sealed class ChannelNetworkPlugin(
    TouchSocketConnectionRegistry connections,
    INetworkConnectionLifecycleDispatcher lifecycle,
    INetworkMessageDispatcher dispatcher,
    ChannelMessageDecoderRegistry decoders,
    ILogger<ChannelNetworkPlugin> logger)
    : NetworkPluginBase(connections, lifecycle, NetworkEndpointKind.Channel), ITcpReceivedPlugin
{
    // Stream boundary: whatever context TouchSocket runs this on, the dispatch it starts is off the executor.
    public Task OnTcpReceived(ITcpSession client, ReceivedDataEventArgs e) => GameplayJob.RunDetachedAsync(async () =>
    {
        if (e.RequestInfo is ChannelFrameRequestInfo frame)
        {
            var connectionId = TouchSocketConnectionRegistry.CreateConnectionId(client, NetworkEndpointKind.Channel);
            if (Decode(frame, connectionId) is { } message)
            {
                var connection = Connections.Get(connectionId).Context;
                try
                {
                    await dispatcher.DispatchAsync(
                        new NetworkDispatchContext(connection),
                        message,
                        CancellationToken.None);
                }
                catch (Exception error)
                {
                    // A job's own failure was logged by the executor with its chain (F-30): Debug here.
                    var failed = GameplayJob.EndedAJob(error);
                    logger.Log(
                        failed ? Microsoft.Extensions.Logging.LogLevel.Debug : Microsoft.Extensions.Logging.LogLevel.Error,
                        error,
                        "Channel handler failed {ConnectionId}: message=0x{MessageNumber:X2} "
                            + "handler={Message}",
                        connectionId,
                        frame.MessageNumber,
                        message.GetType().Name);
                    // The failed job closes this connection itself, after what its outbox kept (RQ-031(c)).
                    if (failed)
                        return;
                    throw;
                }
            }
        }

        await e.InvokeNext();
    }).AsTask();

    private IClientNetworkMessage? Decode(ChannelFrameRequestInfo frame, NetworkConnectionId connectionId)
    {
        var decoder = decoders.Find(frame.MessageNumber);
        if (decoder is null)
        {
            logger.LogWarning(
                "Unhandled Channel frame {ConnectionId}: message=0x{MessageNumber:X2} " +
                "classification=0x{Classification:X2} body({BodyLength}B)={Body}",
                connectionId,
                frame.MessageNumber,
                frame.Classification,
                frame.Body.Length,
                Convert.ToHexString(frame.Body.Span));
            return null;
        }

        var message = decoder.Decode(
            new ChannelMessageMetadata(
                frame.Classification,
                frame.MessageNumber,
                frame.Checksum,
                frame.Acknowledgement),
            frame.Body.Span);

        if (message is null)
        {
            logger.LogWarning(
                "Channel decoder {Decoder} declined {ConnectionId}: message=0x{MessageNumber:X2} " +
                "body({BodyLength}B); body withheld",
                decoder.GetType().Name,
                connectionId,
                frame.MessageNumber,
                frame.Body.Length);
        }

        return message;
    }
}

internal sealed class GameNetworkPlugin(
    TouchSocketConnectionRegistry connections,
    INetworkConnectionLifecycleDispatcher lifecycle,
    INetworkMessageDispatcher dispatcher,
    GameMessageDecoderRegistry decoders,
    ILogger<GameNetworkPlugin> logger)
    : NetworkPluginBase(connections, lifecycle, NetworkEndpointKind.Game), ITcpReceivedPlugin
{
    private readonly ConditionalWeakTable<NetworkConnectionContext, GameInboundFrameCounter> frameCounters = new();

    // Stream boundary: whatever context TouchSocket runs this on, the dispatch it starts is off the executor.
    public Task OnTcpReceived(ITcpSession client, ReceivedDataEventArgs e) => GameplayJob.RunDetachedAsync(async () =>
    {
        if (e.RequestInfo is GameClientFrameRequestInfo frame)
        {
            var connectionId = TouchSocketConnectionRegistry.CreateConnectionId(client, NetworkEndpointKind.Game);
            var connection = Connections.Get(connectionId).Context;
            var (ordinal, fresh) = frameCounters.GetValue(connection,
                _ => new GameInboundFrameCounter()).Observe(frame.Sequence);
            if (Decode(frame, connectionId, ordinal, fresh) is { } message)
            {
                try
                {
                    await dispatcher.DispatchAsync(
                        new NetworkDispatchContext(connection),
                        message,
                        CancellationToken.None);
                }
                catch (Exception error)
                {
                    // Logged, because a handler that throws after its commands succeeded has no other
                    // reporting path. A job that failed is not rethrown (DR-15, RQ-031(c)): its outbox
                    // closes this connection after the entries it kept, where a rethrow would let
                    // TouchSocket close it at once and overtake them. A refusal to run the job (the
                    // executor stopped, the depth limit) registered nothing and is still rethrown, so
                    // the transport closes the connection as before. A job's own failure was logged by
                    // the executor with its chain and connection (F-30), so this line is Debug then.
                    var failed = GameplayJob.EndedAJob(error);
                    logger.Log(
                        failed ? Microsoft.Extensions.Logging.LogLevel.Debug : Microsoft.Extensions.Logging.LogLevel.Error,
                        error,
                        "Game handler failed {ConnectionId}: selector=0x{Selector:X2} "
                            + "type=0x{Type:X4} message={Message}",
                        connectionId,
                        frame.Selector,
                        frame.Type,
                        message.GetType().Name);
                    if (failed)
                        return;
                    throw;
                }
            }
        }

        await e.InvokeNext();
    }).AsTask();

    /// <remarks>
    /// The first-byte selector is not interpreted here. It is part of the wire key, so a frame whose
    /// selector no decoder registered under cannot match and is dropped by the lookup itself. Its
    /// protocol meaning is stated where the decoders are registered.
    ///
    /// The two ways a frame reaches no handler are logged apart, because they mean opposite things.
    /// An unregistered wire key is an ordinary fact about traffic -- the client sends protocols this
    /// server has not implemented yet -- and its body is dumped, since recovering that body is the
    /// whole point of noticing. A registered decoder returning null is a server-side defect instead,
    /// and its body is withheld: the decoders that can decline include ones whose bodies carry
    /// account credentials, and a decode failure must not turn into a credential dump. Naming the
    /// decoder identifies the defect without printing what it failed to read.
    ///
    /// A decoder that THROWS is the third way, and until 2026-08-25 it was the silent one. Only the
    /// dispatch call had a catch, so an exception raised while decoding escaped the receive
    /// callback, the connection was torn down, and nothing was written anywhere. That is not a
    /// hypothetical: A21's CHANGE_TUTORIAL_FLAG is six bytes and this server's decoder demanded
    /// exactly five, so every tutorial entry ended with the client reporting a lost connection and
    /// the server reporting nothing at all. The length was the bug; the silence is what made it
    /// expensive. Rethrown, not swallowed -- a frame this server cannot read is not one it can
    /// continue a session on, so the connection still drops. Only the silence is removed.
    /// </remarks>
    private IClientNetworkMessage? Decode(GameClientFrameRequestInfo frame,
        NetworkConnectionId connectionId, long ordinal, bool fresh)
    {
        var decoder = decoders.Find(frame.Selector, frame.Type);
        if (decoder is null)
        {
            logger.LogWarning(
                "Unhandled Game frame {ConnectionId}: selector=0x{Selector:X2} type=0x{Type:X4} " +
                "seq={Sequence} body({BodyLength}B)={Body}",
                connectionId,
                frame.Selector,
                frame.Type,
                frame.Sequence,
                frame.Body.Length,
                Convert.ToHexString(frame.Body.Span));
            return null;
        }

        IClientNetworkMessage? message;
        try
        {
            message = decoder.Decode(
                new GameMessageMetadata(
                    frame.Selector,
                    frame.Type,
                    frame.Checksum,
                    frame.Sequence) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }.WithRawBody(frame.Body.Span),
                frame.Body.Span);
        }
        catch (Exception error)
        {
            // The body is withheld for the same reason a declining decoder's is: some of these
            // carry credentials, and a decode failure must not become a dump. For a length defect
            // the length is the part that matters, and it is here.
            logger.LogError(
                error,
                "Game decoder {Decoder} threw {ConnectionId}: selector=0x{Selector:X2} " +
                "type=0x{Type:X4} seq={Sequence} body({BodyLength}B); body withheld",
                decoder.GetType().Name,
                connectionId,
                frame.Selector,
                frame.Type,
                frame.Sequence,
                frame.Body.Length);
            throw;
        }

        if (message is null)
        {
            logger.LogWarning(
                "Game decoder {Decoder} declined {ConnectionId}: selector=0x{Selector:X2} " +
                "type=0x{Type:X4} body({BodyLength}B); body withheld",
                decoder.GetType().Name,
                connectionId,
                frame.Selector,
                frame.Type,
                frame.Body.Length);
        }

        return message;
    }
}
