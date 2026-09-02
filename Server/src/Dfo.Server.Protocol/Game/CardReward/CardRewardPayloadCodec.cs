using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.CardReward;

/// <summary>
/// The bytes of the two card-panel exchanges: the layout that puts the cards down, and the answer
/// that turns them over.
/// </summary>
/// <remarks>
/// Both shapes are recorded in <c>protocol/game/client-to-server/0x0046.md</c> and
/// <c>protocol/game/client-to-server/0x0047.md</c>, which is where the evidence for each field
/// lives. What is worth saying here is the split: the layout is a transcribed constant, the request
/// has one proven reading and one unproven one, and the answer has a single position byte that no
/// live client has confirmed.
/// </remarks>
internal static class CardRewardPayloadCodec
{
    /// <summary>
    /// How many positions the layout declares, and how many records the answer carries.
    /// </summary>
    /// <remarks>
    /// Eight in both, and the same eight: the layout writes one 16-bit word per position and the
    /// answer writes one record per position. A face has fewer than that -- see
    /// <see cref="ClearRewardRules.CardSlotCount"/> -- and this does not read that number, because
    /// how wide the frame is, is a fact about the frame.
    /// </remarks>
    public const int WirePositionCount = 8;

    /// <summary>The layout frame's whole body, envelope included.</summary>
    public const int LayoutBodyLength = 1 + (WirePositionCount * 2);

    /// <summary>What the reference server writes in the first position's word.</summary>
    /// <remarks>
    /// Transcribed, not understood. The captured frame is
    /// <c>01 01 00 FF FF FF FF FF FF FF FF FF FF FF FF FF FF</c> and the reference builder writes
    /// exactly that: an accepted envelope, then <c>0x0001</c>, then seven <c>0xFFFF</c>. The
    /// client's handler for this frame reads 16-bit words and nothing wider, so whatever these
    /// eight words select, none of them is an item.
    /// </remarks>
    private const ushort LayoutFirstPosition = 0x0001;

    /// <summary>What the reference server writes in the other seven positions.</summary>
    private const ushort LayoutOtherPosition = 0xFFFF;

    /// <summary>The two bytes the client writes when a card is clicked.</summary>
    public const int SelectCardRequestLength = 2;

    /// <summary>The face byte's free value.</summary>
    public const byte FreeFaceByte = 0x00;

    /// <summary>The face byte's paid value.</summary>
    public const byte PaidFaceByte = 0x01;

    /// <summary>
    /// The highest position number a request may name.
    /// </summary>
    /// <remarks>
    /// The wire's bound rather than the face's. The reference server drops any request whose second
    /// byte is above three before it looks at a run at all, so a request naming a fifth card is not
    /// a request for a position this server happens not to have -- it is outside what this type can
    /// say. Keeping the number here rather than reading the face's slot count is what stops a
    /// protocol reading from depending on a rule input.
    /// </remarks>
    public const int HighestRequestablePosition = 3;

    /// <summary>The three bytes every described record opens with.</summary>
    private const int RecordHeaderLength = 3;

    /// <summary>The identifier and the count that every declared item is a pair of.</summary>
    private const int ContentPairLength = 8;

    /// <summary>The byte each described record closes with.</summary>
    /// <remarks>
    /// Zero, which is what the reference builder writes for every record it describes. The client
    /// keeps it against the record's own number; what it then does with it has not been read.
    /// </remarks>
    private const byte RecordTail = 0x00;

    /// <summary>The byte a record that describes nothing is filled with.</summary>
    /// <remarks>
    /// <c>FF FF FF FF</c>, transcribed from the reference builder and from the captured
    /// thirty-three byte answer. The leading <c>0xFF</c> is what takes the record out of
    /// consideration; the other three follow it so the frame keeps the width the capture had.
    /// </remarks>
    private const byte AbsentRecordByte = 0xFF;

    /// <summary>The width of a record that describes nothing.</summary>
    private const int AbsentRecordLength = 4;

    /// <summary>
    /// The card panel's layout: which positions exist, and nothing else.
    /// </summary>
    /// <remarks>
    /// <b>No item reaches this frame, and none can.</b> It takes no argument at all: the cards are
    /// face down at this point, and what is behind them is not told until one is turned over. The
    /// seventeen bytes are the captured ones byte for byte -- see
    /// <c>protocol/game/client-to-server/0x0046.md</c>.
    /// </remarks>
    public static byte[] EncodeLayout()
    {
        var positions = new byte[WirePositionCount * 2];
        BinaryPrimitives.WriteUInt16LittleEndian(positions, LayoutFirstPosition);
        for (var position = 1; position < WirePositionCount; position++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                positions.AsSpan(position * 2), LayoutOtherPosition);
        }

        return GameCmdEnvelope.Accept(positions);
    }

    /// <summary>
    /// Reads a flip request, or declines it.
    /// </summary>
    /// <remarks>
    /// Declining rather than throwing, for the reason the boss confirmation declines: a frame that
    /// arrives at settlement must not be able to take the connection down with it, and the player
    /// who would lose it has just cleared a dungeon. A face byte outside the two this type has, and
    /// a position above <see cref="HighestRequestablePosition"/>, are both dropped here -- which is
    /// what "the request is discarded" means on this seam.
    /// <para>
    /// <b>The two faces are read apart on purpose.</b> The free reading has a capture behind it and
    /// the paid one does not; keeping them in separate functions is what makes overturning the paid
    /// shape a change to <see cref="ReadPaidRequest"/> alone.
    /// </para>
    /// </remarks>
    public static SelectCardRequest? ReadSelectCardRequest(
        GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < SelectCardRequestLength)
            return null;

        return body[0] switch
        {
            FreeFaceByte => ReadFreeRequest(metadata, body),
            PaidFaceByte => ReadPaidRequest(metadata, body),
            _ => null,
        };
    }

    /// <summary>
    /// The free flip: two bytes, the face and the position.
    /// </summary>
    /// <remarks>
    /// The proven one. The client's click packer writes the face byte as a literal zero and the
    /// position after it, and the reference server reads the same two bytes back.
    /// </remarks>
    private static SelectCardRequest? ReadFreeRequest(
        GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        int position = body[1];
        return position > HighestRequestablePosition
            ? null
            : new SelectCardRequest(metadata, ClearRewardCardKind.Free, position);
    }

    /// <summary>
    /// The paid flip, read the way the free one is.
    /// </summary>
    /// <remarks>
    /// <b>Not proven.</b> The client has a second packer for this type which writes a single byte,
    /// and the protocol census marks the type divergent because of it. No capture of a paid flip
    /// exists, so what that one byte is has not been read. What is done here is what the reference
    /// server does: require the same two bytes the free flip has, and take the second as the
    /// position. A one-byte body is declined rather than guessed at.
    /// <para>
    /// This function is the whole of that guess. Overturning it changes nothing about
    /// <see cref="ReadFreeRequest"/>, which is why the two are not one function with a parameter.
    /// </para>
    /// </remarks>
    private static SelectCardRequest? ReadPaidRequest(
        GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        int position = body[1];
        return position > HighestRequestablePosition
            ? null
            : new SelectCardRequest(metadata, ClearRewardCardKind.Paid, position);
    }

    /// <summary>
    /// Every position of the flipped face, each record naming the position it describes.
    /// </summary>
    /// <remarks>
    /// Eight records. The face's positions fill the first of them in their own order and the rest
    /// are written as records describing nothing, so a four-position face with nothing behind any
    /// of it comes to thirty-three bytes -- the length of the captured answer.
    /// <para>
    /// A position holding nothing is written as a record declaring no contents, which is how it is
    /// said out loud rather than by omission. That distinction is the point of the frame: the
    /// client is meant to turn all four cards face up, and a position left out of the answer is one
    /// it would leave face down.
    /// </para>
    /// </remarks>
    public static byte[] EncodeSelectCardResponse(SelectCardResponse message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var described = Math.Min(message.Slots.Count, WirePositionCount);
        var length = ((WirePositionCount - described) * AbsentRecordLength)
            + (described * (RecordHeaderLength + 1));
        for (var record = 0; record < described; record++)
        {
            if (message.Slots[record].Item is not null)
                length += ContentPairLength;
        }

        var payload = new byte[length];
        var at = 0;
        for (var record = 0; record < described; record++)
            at = WriteRecord(payload, at, message.Kind, message.Slots[record]);

        while (at < length)
            payload[at++] = AbsentRecordByte;

        return GameCmdEnvelope.Accept(payload);
    }

    /// <summary>One record: whose face it is, which position, and what is behind it.</summary>
    private static int WriteRecord(
        byte[] payload, int at, ClearRewardCardKind kind, ClearRewardCardSlot slot)
    {
        payload[at++] = AnsweredFaceByte(kind);
        payload[at++] = ContentOwningPosition(slot.Index);
        payload[at++] = slot.Item is null ? (byte)0 : (byte)1;

        if (slot.Item is { } item)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(at), (uint)item.ItemId);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(at + 4), (uint)item.Quantity);
            at += ContentPairLength;
        }

        payload[at++] = RecordTail;
        return at;
    }

    /// <summary>
    /// <b>The one byte this whole feature rests on, and no live client has confirmed it.</b>
    /// </summary>
    /// <remarks>
    /// It is read as the number of the card position a record's contents belong to. Two things
    /// point that way and neither of them is a measurement:
    /// <list type="bullet">
    /// <item>the client hands this byte to a lookup that addresses a per-position list --
    /// <c>this + 0x1E8 + 100*b + 60</c> -- which is an index into positions rather than a
    /// flag;</item>
    /// <item>the client skips a record's contents when the byte is above seven, and seven is the
    /// last of the <see cref="WirePositionCount"/> positions the frame carries -- so the bound is
    /// the frame's own width rather than a number from somewhere else.</item>
    /// </list>
    /// The reference server writes <c>paidSelected ? 0x00 : 0xFF</c> here, and only in the first of
    /// its eight records -- its other seven never carry contents at all (<c>86JP</c>:
    /// <c>CardRewardNotificationSender.BuildCardInfoAck</c>). So the only position it can point at
    /// is zero, which is why its own panel has one card with anything behind it. Turning all four
    /// face up is this reading being taken at its word.
    /// <para>
    /// <b>Every use of that reading is this function.</b> One live flip settles it -- put a pair on
    /// position two and see whether the third card lights up -- and if it is wrong, this is the
    /// only place that changes. See <c>protocol/game/client-to-server/0x0047.md</c>.
    /// </para>
    /// </remarks>
    private static byte ContentOwningPosition(int index) => checked((byte)index);

    /// <summary>Which of the two faces a record belongs to.</summary>
    /// <remarks>
    /// Also inferred, and recorded beside the position byte rather than mixed into it. The client
    /// hands this byte to the same lookup its own one-byte <c>SELECT_CARD</c> packer feeds, which
    /// is what makes "the face" the reading; and a record whose first byte is <c>0xFF</c> is left
    /// alone, which is what makes <c>0xFF</c> mean "no record here". The reference server writes
    /// zero for a free face, so the free value has a capture behind it and the paid value is the
    /// enumeration taken at its word.
    /// </remarks>
    private static byte AnsweredFaceByte(ClearRewardCardKind kind) => (byte)kind;
}
