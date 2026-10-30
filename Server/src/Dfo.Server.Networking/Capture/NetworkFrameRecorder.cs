using System.Buffers.Binary;
using System.Text.Json;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Networking.Capture;

/// <summary>Opt-in lossless transport evidence. No message decoding or business expectations.</summary>
internal sealed class NetworkFrameRecorder : IDisposable
{
    private readonly object gate = new();
    private readonly StreamWriter? writer;
    private long ordinal;
    private long nextSend;

    public bool Enabled => writer is not null;

    public NetworkFrameRecorder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // A collection is append-only evidence; reusing its name must not erase a prior run.
        writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { AutoFlush = true };
    }

    public void Receive(NetworkConnectionContext connection, GameClientFrameRequestInfo frame)
    {
        if (writer is null) return;
        var bytes = new byte[checked((int)frame.TotalLength)];
        bytes[0] = frame.Selector;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(1), frame.Type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(3), frame.TotalLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(7), frame.Checksum);
        bytes[11] = frame.Reserved;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), frame.Sequence);
        frame.Body.Span.CopyTo(bytes.AsSpan(GameClientFrameRequestInfo.HeaderLength));
        RecordFrame(connection, "C2S", bytes, 0);
    }

    public void Receive(NetworkConnectionContext connection, ChannelFrameRequestInfo frame)
    {
        if (writer is null) return;
        RecordFrame(connection, "C2S", ChannelFrameEncoder.Encode(frame.Classification, frame.MessageNumber,
            frame.Checksum, frame.Acknowledgement, frame.Body.Span), 0);
    }

    public long BeginSend(NetworkConnectionContext connection, ReadOnlyMemory<byte> frames)
    {
        if (writer is null) return 0;
        var sendId = Interlocked.Increment(ref nextSend);
        var channel = connection.EndpointKind == NetworkEndpointKind.Channel;
        var header = channel ? 11 : 15;
        var offset = 0;
        while (offset < frames.Length)
        {
            var remaining = frames.Span[offset..];
            if (remaining.Length < header) throw new InvalidDataException("Truncated outbound frame header in capture");
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(remaining[(channel ? 2 : 3)..]));
            if (length < header || length > DnfFrameProtocol.MaximumFrameLength || length > remaining.Length)
                throw new InvalidDataException("Invalid outbound frame length in capture");
            RecordFrame(connection, "S2C", remaining[..length], sendId);
            offset += length;
        }
        return sendId;
    }

    public void EndSend(long sendId, bool flushed, string? error = null)
    {
        if (writer is null) return;
        lock (gate)
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "send-result", order = ++ordinal, at = DateTimeOffset.UtcNow,
                sendId, flushed, error
            }));
    }

    private void RecordFrame(NetworkConnectionContext connection, string direction, ReadOnlySpan<byte> frame, long sendId)
    {
        var channel = connection.EndpointKind == NetworkEndpointKind.Channel;
        var headerLength = channel ? 11 : direction == "C2S" ? 14 : 15;
        var opcode = channel ? frame[1] : BinaryPrimitives.ReadUInt16LittleEndian(frame[1..]);
        var family = channel ? "Channel" : frame[0] switch { 0 => "NOTI", 1 => "CMD", _ => "Unknown" };
        ushort? sequence = !channel && direction == "C2S" ? BinaryPrimitives.ReadUInt16LittleEndian(frame[12..]) : null;
        lock (gate)
            writer!.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "frame", order = ++ordinal, at = DateTimeOffset.UtcNow,
                connectionId = connection.ConnectionId.Value, endpoint = connection.EndpointKind.ToString(),
                listenerPort = connection.ListenerPort, remoteAddress = connection.RemoteAddress?.ToString(),
                direction, family, selector = frame[0], opcode = $"0x{opcode:X4}", sequence,
                headerLength, length = frame.Length, bodyLength = frame.Length - headerLength,
                sendId, stage = direction == "C2S" ? "received" : "send-attempt",
                hex = Convert.ToHexString(frame)
            }));
    }

    public void Dispose()
    {
        lock (gate) writer?.Dispose();
    }
}
