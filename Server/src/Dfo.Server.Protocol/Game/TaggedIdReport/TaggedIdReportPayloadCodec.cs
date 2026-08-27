using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

/// <summary>
/// Decodes the 0x008F body: <c>u8 mode</c>, <c>u32 flagIndex</c>, <c>u8 rewardFlag</c>.
/// </summary>
/// <remarks>
/// This read four bytes of id at offset zero and a flag at four, and rejected any length but five.
/// That is the older client. A21 puts a mode byte in front, so the same six bytes mean something
/// else entirely: the session that reaches the tutorial sends <c>00 1E 00 00 00 01</c>, which is
/// mode 0, flag index 30, reward 1 -- not id 7680 with the flag clear.
///
/// The length check was worse than the misread. A21's shortest form is six bytes, so every
/// CHANGE_TUTORIAL_FLAG threw <c>InvalidDataException</c> out of the decoder, and the decoder is
/// called outside the dispatch layer's try/catch: the exception escaped the receive callback and
/// the connection was dropped with nothing logged. The client showed "network connection
/// interrupted" while the server said nothing at all. Reproduced at the wire level on
/// 2026-08-25 -- the connection closes on this frame and no other.
///
/// The client may also append nine reserved bytes, which the A21 reference calls the capture
/// layout; both forms carry these fields at the same offsets, so anything from six bytes up is
/// accepted and only the first six are read.
///
/// See protocol/game/client-to-server/0x008F.md.
/// </remarks>
internal static class TaggedIdReportPayloadCodec
{
    /// <summary>The shortest form A21 sends. Longer bodies carry the same fields up front.</summary>
    public const int BodyLength = 6;

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
        if (body.Length < BodyLength)
        {
            throw new InvalidDataException(
                $"0x008F requires at least {BodyLength} body bytes; received '{body.Length}'.");
        }

        // No value is rejected: the reward flag is hard-coded at most client sites and computed at
        // a couple, so the domain of the computed ones is not yet known and must not be guessed at
        // by the parser.
        return new TaggedIdReportRequest(
            metadata,
            body[0],
            BinaryPrimitives.ReadUInt32LittleEndian(body[1..]),
            body[5]);
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
