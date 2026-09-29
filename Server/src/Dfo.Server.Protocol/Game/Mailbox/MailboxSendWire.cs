using System.Text;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.Mailbox;
public sealed record MailboxSendRequest(GameMessageMetadata Metadata, bool Multi, PlayerMailRequest Mail) : IClientNetworkMessage
{ public ClientMessageType MessageType => Multi ? ClientMessageType.GameMailboxSendMulti : ClientMessageType.GameMailboxSend; }
internal sealed class MailboxSendDecoder : IGameMessageDecoder
{ public IClientNetworkMessage Decode(GameMessageMetadata m, ReadOnlySpan<byte> body) => MailSendWire.Read(m, body, false); }
internal sealed class MailboxSendMultiDecoder : IGameMessageDecoder
{ public IClientNetworkMessage Decode(GameMessageMetadata m, ReadOnlySpan<byte> body) => MailSendWire.Read(m, body, true); }
internal static class MailSendWire
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static MailboxSendRequest Read(GameMessageMetadata metadata, ReadOnlySpan<byte> bytes, bool multi)
    {
        if (bytes.Length > 1024) throw new InvalidDataException("Mail request is too large.");
        try
        {
            using var stream = new MemoryStream(bytes.ToArray()); using var r = new BinaryReader(stream);
            var name = ReadString(r, 19);
            if (name.Length == 0) throw new InvalidDataException("Missing recipient.");
            var gold = r.ReadInt32();
            if (gold < 0) throw new InvalidDataException("Negative attached gold.");
            var count = multi ? r.ReadUInt16() : 1;
            if (count > 10) throw new InvalidDataException("Too many attachments.");
            var selections = new List<MailSendSelection>();
            for (var i = 0; i < count; i++)
            {
                var type = multi && i == 0 ? (byte)0 : r.ReadByte();
                var slot = r.ReadUInt16(); var item = r.ReadInt32(); var quantity = r.ReadInt32();
                if (!multi && item == 0 && quantity == 0 && type == 0 && slot is 0 or ushort.MaxValue) continue;
                if (item <= 0 || quantity <= 0 || type is not (0 or 7)) throw new InvalidDataException("Invalid mail attachment.");
                BusinessSlot business;
                try { business = new InventorySlotMappingRuleService().ToBusinessSlot(
                    new GameSlot((InventoryContainerType)type, slot)); }
                catch (InventorySlotMappingException e)
                { throw new InvalidDataException("Invalid mail attachment slot.", e); }
                selections.Add(new(business, item, quantity));
            }
            var body = Utf8.GetString(ReadString(r, 512));
            if (r.ReadUInt32() != 0 || stream.Position != stream.Length) throw new InvalidDataException("Unverified mail tail.");
            return new(metadata, multi, new(name, gold, body, selections));
        }
        catch (Exception e) when (e is EndOfStreamException or DecoderFallbackException)
        { throw new InvalidDataException("Malformed mail request.", e); }
    }
    private static byte[] ReadString(BinaryReader r, int maximum)
    {
        var length = r.ReadInt32();
        if (length < 0 || length > maximum) throw new InvalidDataException("Mail string length is invalid.");
        var bytes = r.ReadBytes(length);
        if (bytes.Length != length || bytes.Contains((byte)0)) throw new InvalidDataException("Truncated or zero-containing mail string.");
        _ = Utf8.GetString(bytes); return bytes;
    }
}
public sealed record MailboxSendResponse(bool Multi, byte Error = 0) : IGameCmdMessage
{ public ServerMessageType MessageType => Multi ? ServerMessageType.GameMailboxSendMulti : ServerMessageType.GameMailboxSend; }
internal sealed class MailboxSendEncoder : GameCmdMessageEncoder<MailboxSendResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MailboxSendResponse m) =>
        OnePayload(m.Multi ? (ushort)0x013B : (ushort)0x005E, MailboxWire.Ack(m.Error, _ => { }));
}
public sealed class MailboxSendHandler(MailboxDelivery delivery) : ClientMessageHandler<MailboxSendRequest>
{
    protected override ValueTask HandleMessageAsync(NetworkDispatchContext c, MailboxSendRequest m, CancellationToken t) =>
        delivery.HandleAsync(c, MailboxOperation.Send, 0, 0, null, t, m);
}
