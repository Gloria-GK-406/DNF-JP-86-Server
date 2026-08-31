using System.Buffers.Binary;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DungeonClear;

namespace Dfo.Server.Protocol.Tests.Game.DungeonClear;

/// <summary>
/// The frames that end a run: the exit, and the result screen behind it.
/// </summary>
/// <remarks>
/// Every one of these is pinned against a working A21 session's own bytes. The reward breakdown is
/// the interesting one -- 354 bytes there against 274 here -- and the eighty-byte difference is
/// accounted for exactly: ten per-object experience entries this server has none of. The offsets
/// asserted below are the ones that frame carries a non-zero value at, which is what makes the
/// layout checkable rather than merely transcribed.
/// </remarks>
public sealed class DungeonClearCodecTests
{
    /// <summary>The exit opens on one byte, and the byte is zero.</summary>
    [Fact]
    public void The_exit_opens_on_a_single_zero()
    {
        Assert.Equal([0x00], DungeonClearPayloadCodec.EncodeEnableClearDungeon());
    }

    [Fact]
    public void The_result_screens_merchant_is_named_by_its_own_identifier()
    {
        Assert.Equal(
            [0xE8, 0x03, 0x00, 0x00],
            DungeonClearPayloadCodec.EncodeSecretShopNpc(GameProtocol.SecretShopNpcId));
    }

    /// <remarks>
    /// The captured body, fifty-three bytes, of which the reference server reads exactly one. Its
    /// own answer echoed 0x5A back at offset six of PLAY_RESULT, which is what makes offset ten
    /// more than a guess.
    /// </remarks>
    [Fact]
    public void The_captured_report_carries_the_rank_the_client_worked_out()
    {
        byte[] body =
        [
            0x00, 0x01, 0x00, 0x01, 0x01, 0x00, 0x05, 0x00, 0x00, 0x00, 0x5A, 0x5A, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFB, 0xFF, 0xFF, 0xFF, 0x57, 0x05, 0x00, 0x00,
            0x61, 0x05, 0x00, 0x00, 0x57, 0x05, 0x00, 0x00, 0x61, 0x05, 0x00, 0x00, 0x09, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x6E, 0xA9, 0x5E, 0xBA,
        ];

        var request = (SetPlayResultRequest)new SetPlayResultDecoder()
            .Decode(GameMessageMetadata.Empty, body)!;

        Assert.Equal(90, request.ClientRankPoint);
    }

    [Fact]
    public void A_report_too_short_to_carry_the_rank_is_refused()
    {
        Assert.Throws<InvalidDataException>(() =>
            new SetPlayResultDecoder().Decode(GameMessageMetadata.Empty, new byte[10]));
    }

    /// <remarks>
    /// The reference's own frame, field for field:
    /// <c>00 EE 43 02 00 00 5A 00 01 01 00 EE 43 02 00 00</c> -- rank zero, 148462 milliseconds,
    /// no time bonus, the client's rank of 90, not a quest maze, one member, user 1, the same
    /// clear time again, and not a new best.
    /// </remarks>
    [Fact]
    public void The_result_is_the_reference_frame_field_for_field()
    {
        var body = DungeonClearPayloadCodec.EncodePlayResult(
            wireUserId: 1, clearTimeMilliseconds: 148462, clientRankPoint: 90);

        Assert.Equal(
            [0x00, 0xEE, 0x43, 0x02, 0x00, 0x00, 0x5A, 0x00, 0x01, 0x01, 0x00, 0xEE, 0x43, 0x02,
             0x00, 0x00],
            body);
    }

    /// <summary>
    /// The reward breakdown is the reference's layout with every figure zero.
    /// </summary>
    /// <remarks>
    /// The offsets are the ones the reference's 354-byte frame writes something at, less the
    /// eighty bytes of per-object experience this server declares none of. A layout that drifted
    /// would move one of them, and the client reads this frame positionally.
    /// </remarks>
    [Fact]
    public void The_reward_breakdown_keeps_the_reference_layout_and_pays_nothing()
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(clearBase: 0, rankBonus: 0);

        Assert.Equal(274, body.Length);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body)); // clear base experience
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4))); // score bonus
        Assert.Equal(0, body[109]); // first counted list
        Assert.Equal(0, body[110]); // second counted list
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(155))); // no entries

        // The card tail, which begins where the entry list would have ended.
        Assert.Equal(0, body[159]); // reserved before the free-card pair
        Assert.Equal(1, body[160]); // one free-card seat, because no item sits behind it
        // The reference writes these same flags at 249, 258, 267, 276, 285, 294 and 303, and its
        // paid-card cost at 312 -- eighty bytes further along, which is exactly its entry list.
        foreach (var flag in (int[])[169, 178, 187, 196, 205, 214, 223])
            Assert.Equal(1, body[flag]);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(232))); // paid-card cost
    }

    /// <summary>
    /// Everything the reference frame does not write, this one leaves alone.
    /// </summary>
    /// <remarks>
    /// Eight card-seat flags and a count are the only non-zero bytes a paid-out run and an empty
    /// one share. Asserting the rest is zero is what keeps a future field from being added here
    /// silently -- a number on the result screen this server cannot account for.
    /// </remarks>
    [Fact]
    public void Nothing_else_in_the_reward_breakdown_carries_a_value()
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(clearBase: 0, rankBonus: 0);
        int[] expected = [160, 169, 178, 187, 196, 205, 214, 223];

        var carried = Enumerable.Range(0, body.Length).Where(offset => body[offset] != 0).ToArray();

        Assert.Equal(expected, carried);
    }

    /// <summary>What was paid is written where the result screen reads it, and nowhere else.</summary>
    /// <remarks>
    /// Two figures, at the frame's first eight bytes. Their positions are structural; which of the
    /// two is the clear's own worth and which is the rank bonus is the reference server's naming,
    /// unmeasured -- so they are given distinguishable values here and a live result screen is what
    /// settles the pairing.
    /// </remarks>
    [Fact]
    public void What_the_clear_paid_is_written_at_the_frames_first_two_figures()
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 243, rankBonus: 29);

        Assert.Equal(274, body.Length);
        Assert.Equal(243u, BinaryPrimitives.ReadUInt32LittleEndian(body));
        Assert.Equal(29, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4)));
    }

    /// <summary>
    /// Paying does not disturb any other field, including the ones read as lengths.
    /// </summary>
    /// <remarks>
    /// Two counted lists sit at 109 and 110 and a per-object count at 155. A figure that spilled
    /// into one of them would be read as a length, and the client would go on consuming entries
    /// that are not there -- which is not a wrong number on a screen but a misread frame.
    /// </remarks>
    [Fact]
    public void Paying_leaves_every_other_field_exactly_as_an_empty_run_leaves_it()
    {
        var paid = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 4_000_000_000, rankBonus: 800_000_000);
        var empty = DungeonClearPayloadCodec.EncodeClearDungeonReward(clearBase: 0, rankBonus: 0);

        Assert.Equal(empty.AsSpan(8).ToArray(), paid.AsSpan(8).ToArray());
    }
}
