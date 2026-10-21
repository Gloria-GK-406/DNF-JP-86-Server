using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GameplayLoad;

/// <summary>One server frame: 15-byte Game header plus body, stamped when its socket read returned.</summary>
internal sealed record Frame(byte Selector, ushort Type, byte[] Body, int Length, long Timestamp)
{
    public bool Is(byte selector, ushort type) => Selector == selector && Type == type;

    /// <summary>The leading u16 that town NOTIs (0x0016, 0x0017) use for the subject's wire user id.</summary>
    public ushort? LeadingU16 => Body.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(Body) : null;

    public override string ToString() => $"{(Selector == 1 ? "CMD " : "NOTI")} 0x{Type:X4} ({Body.Length}B)";
}

/// <summary>What one request produced on its own connection.</summary>
internal sealed record RequestOutcome(
    ushort Type,
    long SendTimestamp,
    long? FirstReplyTimestamp,
    Frame? FirstReply,
    long EndTimestamp,
    long BytesInWindow,
    bool TimedOut,
    bool Closed = false);

/// <summary>
/// A Game connection in the A21 framing (14-byte request header, 15-byte response header).
/// </summary>
/// <remarks>
/// Derived from Server/tests/Dfo.Server.Networking.Tests/Characterization/WireClient.cs (copied and
/// rewritten for timing, not linked). Frames are read by a dedicated thread blocking in
/// <see cref="Socket.Receive(byte[])"/> and stamped with <see cref="Stopwatch.GetTimestamp"/> as soon
/// as the read returns, so the arrival time does not include the client's own thread-pool queueing.
/// Fence answers (CMD 0x04DD) are matched FIFO; every other wait is a predicate over later frames.
/// </remarks>
internal sealed class WireConnection : IDisposable
{
    public const ushort FenceType = 0x04DD;
    private const int RequestHeaderLength = 14;
    /// <summary>
    /// Longer than Microsoft.Data.Sqlite's 30 s default busy timeout, so a request stuck behind a
    /// SQLITE_BUSY retry loop is recorded with its real latency instead of as a client-side timeout.
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private readonly Socket socket;
    private readonly Thread receiver;
    private readonly object sync = new();
    private readonly List<(Func<Frame, bool> Predicate, TaskCompletionSource<Frame> Completion)> waiters = [];
    private readonly Queue<TaskCompletionSource<Frame>> fences = new();
    private readonly object sendSync = new();
    private ushort sequence;
    private long bytesReceived;
    private long fenceBytes;

    private WireConnection(string label, Socket socket)
    {
        Label = label;
        this.socket = socket;
        // Registered before the receive thread starts: the server sends NOTI 0x0001 as soon as it
        // accepts, and a waiter registered after ConnectAsync returned could miss it.
        InitialNotice = WaitFor(frame => frame.Is(0x00, 0x0001));
        receiver = new Thread(ReceiveLoop) { IsBackground = true, Name = "recv " + label };
        receiver.Start();
    }

    /// <summary>The initial connection notice (NOTI 0x0001).</summary>
    public Task<Frame> InitialNotice { get; }

    public string Label { get; }

    public ushort WireId { get; set; }

    /// <summary>All frame bytes received, headers included, fence answers excluded.</summary>
    public long PayloadBytes => Interlocked.Read(ref bytesReceived) - Interlocked.Read(ref fenceBytes);

    public bool Closed { get; private set; }

    public string? CloseReason { get; private set; }

    /// <summary>Optional frame tap for the probe command.</summary>
    public Action<Frame>? Tap { get; set; }

    public static async Task<WireConnection> ConnectAsync(string label, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cancellationToken);
        return new WireConnection(label, socket);
    }

    public static byte[] Encode(ushort type, ReadOnlySpan<byte> body, ushort sequence)
    {
        var frame = new byte[RequestHeaderLength + body.Length];
        frame[0] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), checked((uint)frame.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12), sequence);
        body.CopyTo(frame.AsSpan(RequestHeaderLength));
        return frame;
    }

    /// <summary>Waits for the first later frame satisfying <paramref name="predicate"/>.</summary>
    public Task<Frame> WaitFor(Func<Frame, bool> predicate)
    {
        var completion = new TaskCompletionSource<Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync)
        {
            if (Closed) completion.SetException(new IOException($"{Label}: connection closed ({CloseReason})."));
            else waiters.Add((predicate, completion));
        }

        return completion.Task;
    }

    private void Forget(Task<Frame> wait)
    {
        lock (sync) waiters.RemoveAll(entry => entry.Completion.Task == wait);
    }

    /// <summary>
    /// Sends one request, optionally followed in the same write by a step fence, and returns when its
    /// first reply (by <paramref name="signature"/>) has arrived, or when the fence answer has.
    /// </summary>
    /// <remarks>
    /// The server handles one connection's frames in order, so with a fence the reply window is closed:
    /// whatever <paramref name="signature"/> did not see before the fence answer is "no reply".
    /// </remarks>
    public async Task<RequestOutcome> RequestAsync(
        ushort type,
        byte[] body,
        Func<Frame, bool>? signature,
        bool stepFence,
        CancellationToken cancellationToken)
    {
        var reply = signature is null ? null : WaitFor(signature);
        Task<Frame>? fence = null;
        long sent;
        var startBytes = PayloadBytes;
        lock (sendSync)
        {
            var request = Encode(type, body, ++sequence);
            byte[] bytes;
            if (stepFence)
            {
                fence = EnqueueFence();
                var fenceFrame = Encode(FenceType, [], ++sequence);
                bytes = [.. request, .. fenceFrame];
            }
            else
            {
                bytes = request;
            }

            sent = Stopwatch.GetTimestamp();
            SendAll(bytes);
        }

        var timedOut = false;
        var closed = false;
        try
        {
            var awaited = fence ?? reply ?? throw new ArgumentException("A request needs a signature or a fence.");
            await awaited.WaitAsync(RequestTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            timedOut = true;
        }
        catch (IOException)
        {
            // The server closed the connection (e.g. a refused selection). The reply, if any, is kept.
            closed = true;
        }

        Frame? first = reply is { IsCompletedSuccessfully: true } ? reply.Result : null;
        if (reply is not null && first is null) Forget(reply);
        return new RequestOutcome(type, sent, first?.Timestamp, first, Stopwatch.GetTimestamp(), PayloadBytes - startBytes, timedOut, closed);
    }

    /// <summary>Sends a bare fence and returns its answer.</summary>
    public async Task<RequestOutcome> FenceAsync(CancellationToken cancellationToken)
    {
        Task<Frame> fence;
        long sent;
        var startBytes = PayloadBytes;
        lock (sendSync)
        {
            fence = EnqueueFence();
            var frame = Encode(FenceType, [], ++sequence);
            sent = Stopwatch.GetTimestamp();
            SendAll(frame);
        }

        var timedOut = false;
        var closed = false;
        Frame? answer = null;
        try
        {
            answer = await fence.WaitAsync(RequestTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            timedOut = true;
        }
        catch (IOException)
        {
            closed = true;
        }

        return new RequestOutcome(FenceType, sent, answer?.Timestamp, answer, Stopwatch.GetTimestamp(), PayloadBytes - startBytes, timedOut, closed);
    }

    /// <summary>Sends a fence without waiting; used by the flood connection to keep a window in flight.</summary>
    public (long Sent, Task<Frame> Answer) SendFenceNoWait()
    {
        lock (sendSync)
        {
            var fence = EnqueueFence();
            var frame = Encode(FenceType, [], ++sequence);
            var sent = Stopwatch.GetTimestamp();
            SendAll(frame);
            return (sent, fence);
        }
    }

    private Task<Frame> EnqueueFence()
    {
        var completion = new TaskCompletionSource<Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync)
        {
            if (Closed) completion.SetException(new IOException($"{Label}: connection closed ({CloseReason})."));
            else fences.Enqueue(completion);
        }

        return completion.Task;
    }

    private void SendAll(byte[] bytes)
    {
        var offset = 0;
        while (offset < bytes.Length) offset += socket.Send(bytes, offset, bytes.Length - offset, SocketFlags.None);
    }

    private void ReceiveLoop()
    {
        var buffer = new byte[512 * 1024];
        var pending = new byte[1024 * 1024];
        var pendingLength = 0;
        try
        {
            while (true)
            {
                int read;
                try
                {
                    read = socket.Receive(buffer);
                }
                catch (SocketException error)
                {
                    Close("socket: " + error.SocketErrorCode);
                    return;
                }
                catch (ObjectDisposedException)
                {
                    Close("disposed");
                    return;
                }

                var stamp = Stopwatch.GetTimestamp();
                if (read == 0)
                {
                    Close("end of stream");
                    return;
                }

                if (pendingLength + read > pending.Length) Array.Resize(ref pending, Math.Max(pending.Length * 2, pendingLength + read));
                Buffer.BlockCopy(buffer, 0, pending, pendingLength, read);
                pendingLength += read;
                var offset = 0;
                while (pendingLength - offset >= 15)
                {
                    var total = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(pending.AsSpan(offset + 3)));
                    if (total < 15) throw new InvalidDataException($"{Label}: frame length {total}.");
                    if (pendingLength - offset < total) break;
                    var frame = new Frame(
                        pending[offset],
                        BinaryPrimitives.ReadUInt16LittleEndian(pending.AsSpan(offset + 1)),
                        pending.AsSpan(offset + 15, total - 15).ToArray(),
                        total,
                        stamp);
                    offset += total;
                    Deliver(frame);
                }

                if (offset > 0)
                {
                    Buffer.BlockCopy(pending, offset, pending, 0, pendingLength - offset);
                    pendingLength -= offset;
                }
            }
        }
        catch (Exception error)
        {
            Close("receive failure: " + error.Message);
        }
    }

    private void Deliver(Frame frame)
    {
        Interlocked.Add(ref bytesReceived, frame.Length);
        Tap?.Invoke(frame);
        List<TaskCompletionSource<Frame>>? ready = null;
        lock (sync)
        {
            if (frame.Is(0x01, FenceType) && fences.Count > 0)
            {
                Interlocked.Add(ref fenceBytes, frame.Length);
                (ready ??= []).Add(fences.Dequeue());
            }
            else
            {
                for (var index = 0; index < waiters.Count; index++)
                {
                    if (!waiters[index].Predicate(frame)) continue;
                    (ready ??= []).Add(waiters[index].Completion);
                    waiters.RemoveAt(index);
                    index--;
                }
            }
        }

        if (ready is null) return;
        foreach (var completion in ready) completion.TrySetResult(frame);
    }

    private void Close(string reason)
    {
        List<TaskCompletionSource<Frame>> failed;
        lock (sync)
        {
            if (Closed) return;
            Closed = true;
            CloseReason = reason;
            failed = [.. waiters.Select(entry => entry.Completion), .. fences];
            waiters.Clear();
            fences.Clear();
        }

        foreach (var completion in failed)
            completion.TrySetException(new IOException($"{Label}: connection closed ({reason})."));
    }

    public static byte[] DStrings(params string[] values)
    {
        using var body = new MemoryStream();
        Span<byte> length = stackalloc byte[4];
        foreach (var value in values)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            body.Write(length);
            body.Write(bytes);
        }

        return body.ToArray();
    }

    public void Dispose()
    {
        try
        {
            socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        socket.Dispose();
        Close("disposed");
    }
}
