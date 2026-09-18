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
/// has one proven reading and one unproven one, and the answer carries no reward at all -- it says
/// which column of which row is face up and whose flip it was, and the panel draws the contents
/// from the clear's settlement notification.
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

    /// <summary>What the reference server writes at an eligible party seat.</summary>
    /// <remarks>
    /// Transcribed, not understood. The captured frame is
    /// <c>01 01 00 FF FF FF FF FF FF FF FF FF FF FF FF FF FF</c> and the reference builder writes
    /// exactly that: an accepted envelope, then <c>0x0001</c>, then seven <c>0xFFFF</c>. The
    /// client's handler for this frame reads 16-bit words and nothing wider, so whatever these
    /// eight words select, none of them is an item.
    /// </remarks>
    private const ushort LayoutFirstPosition = 0x0001;

    /// <summary>What the reference server writes at an unoccupied party seat.</summary>
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

    /// <summary>The width of one record when it declares no entries, which is all of them here.</summary>
    private const int EmptyRecordLength = 4;

    /// <summary>What a record's seat bytes carry when nobody has turned that card over.</summary>
    /// <remarks>
    /// <c>0xFF</c>, and it is the one value whose effect is fully read: the client's handler for
    /// each of the two seat bytes returns immediately on it, touching neither row. Every other
    /// value is compared against a number the client fetches at run time.
    /// </remarks>
    private const byte NoSeat = 0xFF;

    /// <summary>
    /// The card panel's layout: which positions exist, and nothing else.
    /// </summary>
    /// <remarks>
    /// <b>No item reaches this frame, and none can.</b> It carries only the eligible party seats.
    /// The captured single-player body is reproduced when seat zero is the only member; see
    /// <c>protocol/game/client-to-server/0x0046.md</c>.
    /// </remarks>
    public static byte[] EncodeLayout(IReadOnlyList<byte>? eligibleSeats = null)
    {
        eligibleSeats ??= [0];
        var positions = new byte[WirePositionCount * 2];
        for (var position = 0; position < WirePositionCount; position++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                positions.AsSpan(position * 2), LayoutOtherPosition);
        }
        foreach (var seat in eligibleSeats)
        {
            if (seat >= WirePositionCount)
                throw new ArgumentOutOfRangeException(nameof(eligibleSeats));
            BinaryPrimitives.WriteUInt16LittleEndian(
                positions.AsSpan(seat * 2), LayoutFirstPosition);
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
    /// The whole card panel's state: which column of which row has been turned over, and by whom.
    /// </summary>
    /// <remarks>
    /// <b>A state snapshot, not a report of what was won.</b> Eight records go out every time, one
    /// per column, including the columns nobody has touched. Each record's two seat bytes say who
    /// turned that column's free card and who turned its gold card, and <c>0xFF</c> says nobody
    /// did.
    /// <para>
    /// <b>No reward reaches this frame.</b> What a card holds was already delivered in the clear's
    /// settlement notification, and the panel draws it from there -- see
    /// <c>protocol/game/server-to-client/NOTI-0x0023.md</c>. A live run that answered with every
    /// record empty still turned the clicked card over with its contents on it, which is what
    /// settles the division: this frame decides <i>whose</i> a column is, the settlement frame
    /// decides <i>what is on it</i>, and the player's own click decides which column turns.
    /// </para>
    /// <para>
    /// <b>Every record declares zero entries.</b> The reference server writes <c>0xFF</c> in the
    /// entry count of its last four records, which declares two hundred and fifty-five pairs
    /// inside a thirty-three byte frame. Nothing observable comes of it, but it is a frame that
    /// cannot be walked to its end, so this server does not copy it.
    /// </para>
    /// </remarks>
    public static byte[] EncodeSelectCardResponse(SelectCardResponse message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = new byte[WirePositionCount * EmptyRecordLength];
        var at = 0;
        for (var column = 0; column < WirePositionCount; column++)
        {
            var free = message.Selections.FirstOrDefault(selection =>
                selection.Kind is ClearRewardCardKind.Free && selection.Index == column);
            var paid = message.Selections.FirstOrDefault(selection =>
                selection.Kind is ClearRewardCardKind.Paid && selection.Index == column);
            payload[at++] = free is null ? NoSeat : free.Seat;
            payload[at++] = paid is null ? NoSeat : paid.Seat;
            payload[at++] = 0; // no entries follow
            payload[at++] = 0; // the record's trailing flag
        }

        return GameCmdEnvelope.Accept(payload);
    }
}
