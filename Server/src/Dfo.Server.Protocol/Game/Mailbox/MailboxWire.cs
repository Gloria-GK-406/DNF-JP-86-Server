using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.Mailbox;

public sealed record MailboxClaimRequest(GameMessageMetadata Metadata, uint AttachmentId) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameMailboxClaim; }
public sealed record MailboxActionRequest(GameMessageMetadata Metadata, uint MailId, ushort Action) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameMailboxAction; }
public sealed record MailboxOpenRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameMailboxOpen; }
public sealed record MailboxQueryRequest(GameMessageMetadata Metadata, byte[] Name) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameMailboxQuery; }
internal sealed class MailboxClaimDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => body.Length == 4
        ? new MailboxClaimRequest(metadata, BinaryPrimitives.ReadUInt32LittleEndian(body)) : throw new InvalidDataException("Mail claim needs u32 ID.");
}
internal sealed class MailboxActionDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => body.Length == 6
        ? new MailboxActionRequest(metadata, BinaryPrimitives.ReadUInt32LittleEndian(body), BinaryPrimitives.ReadUInt16LittleEndian(body[4..])) : throw new InvalidDataException("Mail action needs u32 ID/u16 action.");
}
internal sealed class MailboxOpenDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => body.IsEmpty
        ? new MailboxOpenRequest(metadata) : throw new InvalidDataException("Unverified nonempty mailbox open request.");
}
internal sealed class MailboxQueryDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < 5 || body.Length > 23 || BinaryPrimitives.ReadUInt32LittleEndian(body) != body.Length - 4 || body[4..].Contains((byte)0))
            throw new InvalidDataException("Mail recipient requires a bounded name.");
        return new MailboxQueryRequest(metadata, body[4..].ToArray());
    }
}
public sealed record MailboxClaimResponse(uint AttachmentId, byte Error = 0) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMailboxClaim; }
public sealed record MailboxActionResponse(uint MailId, ushort Action, byte Error = 0) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMailboxAction; }
public sealed record MailboxOpenResponse(ushort NotLoaded, byte Error = 0) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMailboxOpen; }
public sealed record MailboxQueryResponse(MailRecipient? Recipient, byte Error = 21) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMailboxQuery; }
public sealed record MailboxAlarm(ushort Count) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMailboxAlarm; }
internal sealed class MailboxClaimEncoder : GameCmdMessageEncoder<MailboxClaimResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MailboxClaimResponse m) => OnePayload(m, MailboxWire.Ack(m.Error, w => w.Write(m.AttachmentId)));
}
internal sealed class MailboxActionEncoder : GameCmdMessageEncoder<MailboxActionResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MailboxActionResponse m) => OnePayload(m, MailboxWire.Ack(m.Error, w => { w.Write(m.MailId); w.Write(m.Action); }));
}
internal sealed class MailboxOpenEncoder : GameCmdMessageEncoder<MailboxOpenResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MailboxOpenResponse m) => OnePayload(m, MailboxWire.Ack(m.Error, w => w.Write(m.NotLoaded)));
}
internal sealed class MailboxQueryEncoder : GameCmdMessageEncoder<MailboxQueryResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MailboxQueryResponse m) => OnePayload(m, MailboxWire.Ack(m.Recipient is null ? m.Error : (byte)0, w =>
    {
        var r = m.Recipient!;
        w.Write(r.Name.Length); w.Write(r.Name); w.Write((byte)0);
        w.Write(checked((ushort)r.Level)); w.Write(checked((byte)r.Job)); w.Write(checked((byte)r.GrowType)); w.Write((byte)0); w.Write((byte)0);
    }));
}
internal sealed class MailboxAlarmEncoder : GameNotiMessageEncoder<MailboxAlarm>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MailboxAlarm m)
    { var body = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(body, m.Count); return OnePayload(m, body); }
}
internal static class MailboxWire
{
    public static byte[] Ack(byte error, Action<BinaryWriter> body)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(error == 0 ? (byte)1 : (byte)0);
        if (error != 0) w.Write(error); else body(w);
        return s.ToArray();
    }
}
