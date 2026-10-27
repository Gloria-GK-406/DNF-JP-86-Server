using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace Dfo.Server.Networking.Tests.Characterization;

/// <summary>One server frame as it came off the socket: the 15-byte Game header and the payload.</summary>
internal sealed record WireFrame(byte[] Bytes)
{
    public const int HeaderLength = 15;
    public byte Selector => Bytes[0];
    public ushort Type => BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(1));
    public ReadOnlySpan<byte> Body => Bytes.AsSpan(HeaderLength);
    public bool IsCmd => Selector == 0x01;
    public bool IsNoti => Selector == 0x00;

    public bool Is(byte selector, ushort type) => Selector == selector && Type == type;
}

/// <summary>A transcript entry: a frame the client read, or a step marker the test wrote.</summary>
internal abstract record TranscriptEntry;

internal sealed record TranscriptStep(string Name) : TranscriptEntry;

internal sealed record TranscriptFrame(WireFrame Frame) : TranscriptEntry;

/// <summary>
/// A Game client that speaks the A21 framing (14-byte request header, 15-byte response header)
/// and keeps every frame the server sends, in arrival order, for whole-transcript comparison.
/// </summary>
/// <remarks>
/// Reads are explicit: the test decides when to read and how far. A step ends on a fence: CHECK_USER_CONNECTION
/// (CMD 0x04DD, empty body) is answered in-line on the same connection after everything earlier
/// on that connection has been handled, so its answer bounds the frames the earlier requests produced.
/// </remarks>
internal sealed class WireClient(string label, TcpClient client) : IDisposable
{
    public const ushort FenceType = 0x04DD;
    private const int RequestHeaderLength = 14;
    private readonly NetworkStream stream = client.GetStream();
    private readonly List<TranscriptEntry> transcript = [];
    private ushort sequence;

    public string Label { get; } = label;

    /// <summary>The server closed the connection, or the test disposed it.</summary>
    public bool IsClosed { get; private set; }

    public IReadOnlyList<TranscriptEntry> Transcript => transcript;

    public IEnumerable<WireFrame> Frames => transcript.OfType<TranscriptFrame>().Select(entry => entry.Frame);

    /// <summary>Character id of the one character this client created; also its wire user id.</summary>
    public ushort WireUserId { get; set; }

    public void Step(string name) => transcript.Add(new TranscriptStep(name));

    /// <summary>Sends a CMD request. The inbound sequence advances by one per frame, as the client's does.</summary>
    public async Task SendAsync(ushort type, byte[] body, CancellationToken cancellationToken)
    {
        var frame = new byte[RequestHeaderLength + body.Length];
        frame[0] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), checked((uint)frame.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12), ++sequence);
        body.CopyTo(frame.AsSpan(RequestHeaderLength));
        await stream.WriteAsync(frame, cancellationToken);
    }

    public async Task<WireFrame> ReadAsync(CancellationToken cancellationToken)
    {
        var header = new byte[WireFrame.HeaderLength];
        await ReadExactlyAsync(header, cancellationToken);
        var total = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3)));
        if (total < WireFrame.HeaderLength) throw new InvalidDataException($"{Label}: frame length {total}.");
        var bytes = new byte[total];
        header.CopyTo(bytes, 0);
        await ReadExactlyAsync(bytes.AsMemory(WireFrame.HeaderLength), cancellationToken);
        var frame = new WireFrame(bytes);
        transcript.Add(new TranscriptFrame(frame));
        return frame;
    }

    /// <summary>
    /// Takes whatever the socket holds in one large read, as the native client and WireSmoke do.
    /// </summary>
    /// <remarks>
    /// Reading only a header's worth at a time leaves later frames in the kernel buffer, and a
    /// connection the server closes right after its last frames (the compensation path) can then
    /// lose them. Draining eagerly makes what was delivered what is observed.
    /// </remarks>
    private async Task ReadExactlyAsync(Memory<byte> target, CancellationToken cancellationToken)
    {
        while (target.Length > 0)
        {
            if (buffered == consumed)
            {
                consumed = buffered = 0;
                var read = await stream.ReadAsync(receive, cancellationToken);
                if (read == 0) throw new EndOfStreamException($"{Label}: the server closed the connection.");
                buffered = read;
            }

            var count = Math.Min(target.Length, buffered - consumed);
            receive.AsMemory(consumed, count).CopyTo(target);
            consumed += count;
            target = target[count..];
        }
    }

    private readonly byte[] receive = new byte[256 * 1024];
    private int consumed;
    private int buffered;

    /// <summary>Reads until a frame satisfying <paramref name="last"/> has been read, and returns it.</summary>
    public async Task<WireFrame> ReadUntilAsync(Func<WireFrame, bool> last, CancellationToken cancellationToken)
    {
        // A step that never ends should fail as itself, not as the whole test budget.
        using var step = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        step.CancelAfter(StepBudget);
        try
        {
            while (true)
            {
                var frame = await ReadAsync(step.Token);
                if (last(frame)) return frame;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{Label}: the awaited frame did not arrive within {StepBudget.TotalSeconds} s; " +
                $"last frames: {string.Join(", ", Frames.TakeLast(8).Select(frame => $"{frame.Selector:X2}/{frame.Type:X4}"))}");
        }
    }

    private static readonly TimeSpan StepBudget = TimeSpan.FromSeconds(20);

    /// <summary>Asks CHECK_USER_CONNECTION and reads up to and including its answer.</summary>
    public async Task FenceAsync(CancellationToken cancellationToken)
    {
        await SendAsync(FenceType, [], cancellationToken);
        await ReadUntilAsync(frame => frame.Is(0x01, FenceType), cancellationToken);
    }

    /// <summary>Reads until the server closes the connection; any frame on the way is kept.</summary>
    public async Task ReadUntilClosedAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true) await ReadAsync(cancellationToken);
        }
        catch (EndOfStreamException)
        {
            IsClosed = true;
            CloseKind = "end of stream";
        }
        catch (IOException error)
        {
            // A reset after the server's close is still a close.
            IsClosed = true;
            CloseKind = "reset: " + error.Message;
        }
    }

    /// <summary>How <see cref="ReadUntilClosedAsync"/> saw the connection end, for failure messages.</summary>
    public string? CloseKind { get; private set; }

    public async Task LoginAsync(string mId, string ticket, CancellationToken cancellationToken)
    {
        Step("connect");
        await ReadUntilAsync(frame => frame.Is(0x00, 0x0001), cancellationToken);
        Step("login");
        await SendAsync(0x0001, DStrings(mId, ticket), cancellationToken);
        await FenceAsync(cancellationToken);
    }

    /// <summary>CREATE_CHARACTER with job 0, the body WireSmoke's wireclient.py sends.</summary>
    public async Task CreateCharacterAsync(string name, CancellationToken cancellationToken)
    {
        Step("create character");
        await SendAsync(0x0005, [0x00, .. DStrings(name), 0, 0, 0, 0], cancellationToken);
        var answer = await ReadUntilAsync(frame => frame.Is(0x01, 0x0005), cancellationToken);
        Assert.True(answer.Body.Length > 0 && answer.Body[0] == 1, $"{Label}: CREATE_CHARACTER was refused.");
        await FenceAsync(cancellationToken);
    }

    /// <summary>SELECT_CHARACTER for roster slot 0. Its answer is the first frame of the entry batch.</summary>
    public async Task SelectCharacterAsync(CancellationToken cancellationToken)
    {
        Step("select character");
        await SendAsync(0x0004, [0, 0], cancellationToken);
        var answer = await ReadUntilAsync(frame => frame.Is(0x01, 0x0004), cancellationToken);
        Assert.True(answer.Body.Length > 10 && answer.Body[0] == 1, $"{Label}: SELECT_CHARACTER was refused.");
        WireUserId = BinaryPrimitives.ReadUInt16LittleEndian(answer.Body[9..]);
        await FenceAsync(cancellationToken);
    }

    /// <summary>SET_USER_AREA into town 1 area 1, an area that shares town presence (the gate does not).</summary>
    public async Task MoveToSharedAreaAsync(CancellationToken cancellationToken)
    {
        Step("set user area 1:1");
        await SendAsync(0x0024, Convert.FromHexString("01013E07FF00050100000000000000"), cancellationToken);
        await FenceAsync(cancellationToken);
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
        IsClosed = true;
        client.Dispose();
    }
}
