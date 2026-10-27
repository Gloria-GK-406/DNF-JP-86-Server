using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.IO.Pipelines;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Outbound;

internal interface IOutboundTransport
{
    /// <summary>The live connection registered as exactly <paramref name="expected"/>, or null (RQ-035).</summary>
    IOutboundConnection? Resolve(NetworkConnectionContext expected);
}

internal interface IOutboundConnection
{
    /// <summary>Returns once <paramref name="frames"/> have left the transport's send pipe.</summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> frames, CancellationToken cancellationToken);

    /// <summary>Whether the failed send observed a closed or completed write side.</summary>
    bool HasClosedSendSide(Exception error);

    ValueTask CloseAsync();
}

/// <summary>
/// The TouchSocket send pipe setting that makes "a send returned" mean "its bytes left the pipe".
/// </summary>
/// <remarks>
/// TouchSocket 4.3.1 writes a session's sends into <c>BaseTransport.m_pipeSend</c> and returns after
/// <c>PipeWriter.FlushAsync</c>; a separate loop (<c>TcpTransport.RunSend</c>) hands the buffer to the
/// socket and only then calls <c>AdvanceTo</c>. Closing (<c>TcpTransport.CloseAsync</c> → <c>Close</c>:
/// <c>Shutdown(Both)</c>, <c>Socket.Close(0)</c>) does not drain that pipe, which is how a close
/// overtook frames that had been "sent" (TASK-01). With the default 64 KiB/32 KiB thresholds the
/// flush returns while bytes are still queued. A pause and resume threshold of 1 make every flush
/// wait until the reader has advanced past all of it, i.e. until the socket accepted the bytes.
/// Read from the IL of TouchSocket 4.3.1; pinned by the real-socket tests.
/// </remarks>
public static class OutboundTransportOptions
{
    public static PipeOptions CreateSendPipeOptions() => new(
        pool: null,
        readerScheduler: PipeScheduler.ThreadPool,
        writerScheduler: PipeScheduler.ThreadPool,
        pauseWriterThreshold: 1,
        resumeWriterThreshold: 1,
        minimumSegmentSize: -1,
        useSynchronizationContext: false);

    public static TouchSocketConfig ConfigureSendPipe(TouchSocketConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.SetTransportOption(option => option.SendPipeOptions = CreateSendPipeOptions());
    }

    public static bool ConfirmsFlush(PipeOptions? options) =>
        options is { PauseWriterThreshold: 1, ResumeWriterThreshold: 1 };

    internal static bool ConfirmsFlush(ITcpSessionClient session) =>
        session is IConfigObject { Config: { } config }
        && ConfirmsFlush(config.GetValue(TouchSocketConfigExtension.TransportOptionProperty)?.SendPipeOptions);
}

internal sealed class TouchSocketOutboundTransport(TouchSocketConnectionRegistry connections) : IOutboundTransport
{
    public IOutboundConnection? Resolve(NetworkConnectionContext expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return connections.TryGet(expected.ConnectionId, out var entry)
            && entry is not null
            && ReferenceEquals(entry.Context, expected)
            && entry.Session.Online
                ? new Connection(entry.Session)
                : null;
    }

    /// <remarks>
    /// The only TCP write point (pinned by <c>TransportWriteSourceTests</c>). Both members refuse a job
    /// (RQ-003): only the sender's detached loops may wait on the network.
    /// </remarks>
    private sealed class Connection(ITcpSessionClient session) : IOutboundConnection
    {
        public async ValueTask SendAsync(ReadOnlyMemory<byte> frames, CancellationToken cancellationToken)
        {
            GameplayNetworkWaitAssertion.Touching("OutboundConnection.SendAsync");
            if (!OutboundTransportOptions.ConfirmsFlush(session))
                throw new InvalidOperationException(
                    "The connection's send pipe does not confirm flushes; configure it with OutboundTransportOptions.");
            await ((ISender)session).SendAsync(frames, cancellationToken).ConfigureAwait(false);
            // A close cancels the pending flush instead of failing it, so a send that "returned" on a
            // connection that is gone is not a delivery.
            if (!session.Online)
                throw new IOException("The connection closed before its send pipe drained.");
        }

        public bool HasClosedSendSide(Exception error)
        {
            if (session.ClosedToken.IsCancellationRequested)
                return true;

            // TouchSocket 4.3.1 RunSend completes its PipeWriter before RunReceive cancels the
            // ClosedToken. Match the pinned .NET writer throw-site as well as its exact error;
            // an unrelated InvalidOperationException (even with this text) is still a warning.
            return error.GetType() == typeof(InvalidOperationException)
                && error.Message == "Writing is not allowed after writer was completed."
                && new StackTrace(error).GetFrames().Any(frame =>
                    frame.GetMethod() is { Name: "ThrowInvalidOperationException_NoWritingAllowed", DeclaringType: { } type }
                    && type.FullName == "System.IO.Pipelines.ThrowHelper"
                    && type.Assembly == typeof(Pipe).Assembly);
        }

        public async ValueTask CloseAsync()
        {
            GameplayNetworkWaitAssertion.Touching("OutboundConnection.CloseAsync");
            Exception? closeFailure = null;
            try
            {
                if (session is not IOrderedTcpSessionClose ordered)
                    throw new InvalidOperationException("Ordered close requires the production DrainingTcpService session.");
                using var physicalClose = new CancellationTokenSource(OutboundLimits.DeliveryTimeout);
                await ordered.CloseSocketAfterFlushAsync(physicalClose.Token).ConfigureAwait(false);
            }
            catch (Exception error) { closeFailure = error; }
            try
            {
                using var cleanup = new CancellationTokenSource(OutboundLimits.DeliveryTimeout);
                var result = await session.CloseAsync("outbound close", cleanup.Token).WaitAsync(cleanup.Token).ConfigureAwait(false);
                if (!result.IsSuccess)
                    throw new IOException($"The socket closed but session cleanup failed: {result.Message}");
            }
            catch (Exception cleanup) when (closeFailure is not null)
            {
                throw new AggregateException("Ordered socket close and session cleanup both failed.", closeFailure, cleanup);
            }
            if (closeFailure is not null) ExceptionDispatchInfo.Throw(closeFailure);
        }
    }
}
