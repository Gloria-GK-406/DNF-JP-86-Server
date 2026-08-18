using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

/// <summary>
/// Decodes the five-byte 0x008F body <c>{u32 id, u8 flag}</c>. All fourteen client builder sites
/// write exactly these two fields with no branches, loops or optional tail, so any other length is
/// not this message. See protocol/game/client-to-server/0x008F.md.
/// </summary>
internal static class TaggedIdReportPayloadCodec
{
    public const int BodyLength = 5;

    /// <summary>
    /// <c>u8 result=1</c>, <c>u8 count</c>, then <c>u16 slot, u32 itemId, u32 count</c> per item.
    /// </summary>
    /// <remarks>
    /// The count describes what was granted, not what the archive declares. Reporting a declared
    /// item that never reached the inventory would tell the client to draw something the server
    /// does not have.
    /// </remarks>
    public static byte[] EncodeAcknowledgement(IReadOnlyList<GrantedTutorialItem> granted)
    {
        using var stream = new MemoryStream(2 + (granted.Count * 10));
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(checked((byte)granted.Count));
        foreach (var item in granted)
        {
            writer.Write(item.Slot);
            writer.Write(item.ItemId);
            writer.Write(item.Count);
        }

        return stream.ToArray();
    }

    public static TaggedIdReportRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            throw new InvalidDataException(
                $"0x008F requires exactly {BodyLength} body bytes; received '{body.Length}'.");
        }

        // No value is rejected: twelve of the fourteen sites hard-code the flag to 1 and two
        // compute it, so the domain of the computed sites is not yet known and must not be
        // guessed at by the parser.
        return new TaggedIdReportRequest(
            metadata,
            BinaryPrimitives.ReadUInt32LittleEndian(body),
            body[4]);
    }
}

internal sealed class TutorialFlagAcknowledgementEncoder
    : GameCmdMessageEncoder<TutorialFlagAcknowledgement>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        TutorialFlagAcknowledgement message) =>
        OnePayload(
            GameProtocol.TaggedIdReportType,
            TaggedIdReportPayloadCodec.EncodeAcknowledgement(message.Granted));
}
