using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CardReward;
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
        Assert.Equal(1, body[160]); // no item behind the free position; see NOTI-0x0023.md
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
    /// The free position's marker and seven card-seat flags are the only non-zero bytes a paid-out
    /// run and an empty one share. Asserting the rest is zero is what keeps a future field from
    /// being added here silently -- a number on the result screen this server cannot account for.
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

    /// <summary>
    /// The byte that says whether the free position has anything behind it.
    /// </summary>
    /// <remarks>
    /// <c>protocol/game/server-to-client/NOTI-0x0023.md</c> records it as
    /// <c>u8 免费翻牌位数(牌后有物品时为 2,否则 1)</c>: a value, not a count of entries after it.
    /// </remarks>
    private const int FreeFlipPositionsOffset = 160;

    /// <summary>Where the free-card pair's two halves sit in this body.</summary>
    private const int FreeCardItemOffset = 161;

    /// <summary>
    /// The half after it, which the same record names <c>i32 免费牌金币</c> -- gold, not a count.
    /// </summary>
    private const int FreeCardGoldOffset = 165;

    /// <summary>The first seat's flag; each seat after it is nine bytes further along.</summary>
    private const int FirstSeatOffset = 169;

    /// <summary>The width of one card seat: a flag, an identifier and an amount.</summary>
    private const int SeatStride = 9;

    /// <summary>Where the card tail ends and the paid-card cost begins.</summary>
    private const int PaidCardCostOffset = 232;

    /// <summary>
    /// The card tail carries the free face position for position, and nothing else moves.
    /// </summary>
    /// <remarks>
    /// Four positions with four distinguishable pairs, and one of them deliberately empty in the
    /// middle. A single filled position could not tell "each position is written where it belongs"
    /// from "the face's first position is written everywhere", and equal quantities could not tell
    /// an identifier from a count -- so every number below is different from every other.
    /// <para>
    /// The frame's own positions are the free-card pair and the seven seats after it, in that
    /// order, so the face's position zero is the pair and its positions one to three are the
    /// first three seats. The four seats behind them stay empty, which is what says the face's
    /// contents stop where the face does.
    /// </para>
    /// <para>
    /// The free position's second field is gold on
    /// <c>protocol/game/server-to-client/NOTI-0x0023.md</c> and not the item's count, so what
    /// stands there is what a flip of that position pays and not the five handed in beside it --
    /// which is why the quantity at position zero appears nowhere below.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_card_tail_carries_the_free_face_position_for_position()
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0,
            rankBonus: 0,
            freeCards:
            [
                Holding(0, itemId: 31001, quantity: 5),
                Empty(1),
                Holding(2, itemId: 31002, quantity: 6),
                Holding(3, itemId: 31003, quantity: 7),
            ]);

        Assert.Equal(274, body.Length);
        Assert.Equal(31001, Int32At(body, FreeCardItemOffset));
        Assert.Equal(
            GameProtocol.ClearRewardCardGold, Int32At(body, FreeCardGoldOffset));
        Assert.Equal((0, 0), SeatAt(body, 0));
        Assert.Equal((31002, 6), SeatAt(body, 1));
        Assert.Equal((31003, 7), SeatAt(body, 2));
        foreach (var seat in (int[])[3, 4, 5, 6])
            Assert.Equal((0, 0), SeatAt(body, seat));
    }

    /// <summary>
    /// A face whose contents sit further along is written further along, not at the front.
    /// </summary>
    /// <remarks>
    /// The other half of the position check, and the one that a mapping which ignored the position
    /// number would fail on its own: a single piece of equipment on the face's last position must
    /// reach the third seat, and the free-card pair must stay empty.
    /// </remarks>
    [Fact]
    public void A_position_at_the_end_of_the_face_is_written_at_the_end_of_the_tail()
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0,
            rankBonus: 0,
            freeCards: [Empty(0), Empty(1), Empty(2), Holding(3, itemId: 42001, quantity: 3)]);

        Assert.Equal(0, Int32At(body, FreeCardItemOffset));
        Assert.Equal(0, Int32At(body, FreeCardGoldOffset));
        Assert.Equal((0, 0), SeatAt(body, 0));
        Assert.Equal((0, 0), SeatAt(body, 1));
        Assert.Equal((42001, 3), SeatAt(body, 2));
    }

    /// <summary>
    /// A full face changes the card tail and nothing else, and the frame keeps its length.
    /// </summary>
    /// <remarks>
    /// The card tail is the only part of this body a face may move. A frame that grew would be
    /// refused by the encoder's own length check, and a frame that stayed the right length while
    /// spilling into the counts at 109, 110 and 155 would be misread rather than merely wrong. So
    /// the bytes before the tail are compared whole, and the tail's own shape bytes -- the free
    /// position's marker and the seven seat flags -- are asserted one by one after it.
    /// </remarks>
    [Fact]
    public void A_face_with_contents_disturbs_nothing_outside_the_card_tail()
    {
        var empty = DungeonClearPayloadCodec.EncodeClearDungeonReward(clearBase: 0, rankBonus: 0);
        var filled = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0,
            rankBonus: 0,
            freeCards:
            [
                Holding(0, itemId: 31001, quantity: 5),
                Holding(1, itemId: 31002, quantity: 6),
                Holding(2, itemId: 31003, quantity: 7),
                Holding(3, itemId: 31004, quantity: 8),
            ]);

        Assert.Equal(empty.Length, filled.Length);
        Assert.Equal(
            empty.AsSpan(0, FreeFlipPositionsOffset).ToArray(),
            filled.AsSpan(0, FreeFlipPositionsOffset).ToArray());
        Assert.Equal(
            empty.AsSpan(PaidCardCostOffset).ToArray(),
            filled.AsSpan(PaidCardCostOffset).ToArray());

        // The marker and the seven flags are what the client reads the tail's shape off. The seat
        // flags are 1 whether the face holds anything or not; the marker is 2 here because this
        // face has something behind its free position.
        Assert.Equal(2, filled[FreeFlipPositionsOffset]);
        foreach (var seat in (int[])[0, 1, 2, 3, 4, 5, 6])
            Assert.Equal(1, filled[FirstSeatOffset + (seat * SeatStride)]);
    }

    /// <summary>
    /// The free position's marker is two when something sits behind that position.
    /// </summary>
    /// <remarks>
    /// <c>protocol/game/server-to-client/NOTI-0x0023.md</c> records the byte before the free-card
    /// pair as <c>u8 免费翻牌位数(牌后有物品时为 2,否则 1)</c> -- a value saying whether that
    /// position has anything behind it, not a count of entries after it. The pair does not move
    /// for it: the same record accounts for the reference's 354-byte frame against this body's 274
    /// as a flat eighty bytes of per-object experience, with the seven seat flags at 169..223 here
    /// either way. The length is asserted alongside because that is the whole of the claim -- one
    /// value changes and the body does not.
    /// </remarks>
    [Fact]
    public void The_free_position_declares_two_when_an_item_sits_behind_it()
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0,
            rankBonus: 0,
            freeCards: [Holding(0, itemId: 31001, quantity: 5), Empty(1), Empty(2), Empty(3)]);

        Assert.Equal(2, body[FreeFlipPositionsOffset]);
        Assert.Equal(31001, Int32At(body, FreeCardItemOffset));
        Assert.Equal(DungeonClearPayloadCodec.ClearRewardBodyLength, body.Length);
        Assert.Equal(1, body[FirstSeatOffset]);
        Assert.Equal(0, Int32At(body, PaidCardCostOffset));
    }

    /// <summary>
    /// The free position's marker is one when nothing sits behind that position.
    /// </summary>
    /// <remarks>
    /// The other half of the record's rule, and the half a face holding nothing at all could not
    /// tell from the marker being one because the marker is always one: this face does hold
    /// something, at its last position, and the free position is still empty. So a marker that
    /// answered "does this face hold anything" rather than "does the free position hold anything"
    /// fails here.
    /// </remarks>
    [Fact]
    public void The_free_position_declares_one_when_nothing_sits_behind_it()
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0,
            rankBonus: 0,
            freeCards: [Empty(0), Empty(1), Empty(2), Holding(3, itemId: 42001, quantity: 3)]);

        Assert.Equal(1, body[FreeFlipPositionsOffset]);
        Assert.Equal(0, Int32At(body, FreeCardItemOffset));
        Assert.Equal(DungeonClearPayloadCodec.ClearRewardBodyLength, body.Length);
        Assert.Equal((42001, 3), SeatAt(body, 2));
    }

    /// <summary>
    /// The free position's second field is gold, not the count its seats carry.
    /// </summary>
    /// <remarks>
    /// Two different fields on <c>protocol/game/server-to-client/NOTI-0x0023.md</c>: the free
    /// position's pair is <c>i32 免费牌物品 / i32 免费牌金币</c>, while each seat after it is
    /// <c>u8 标记=1, i32 物品, i32 数量</c>. So the free position's second field carries what a
    /// flip of it pays while a seat's carries its count.
    /// <para>
    /// Handing the same quantity to both is the point of the case: with different quantities, a
    /// count written into the free position's gold field would look like an ordinary value, and
    /// what is being pinned is that the count does not go there at all. Nine is chosen for it
    /// because it is neither zero nor what a flip pays, so neither an encoder that wrote the count
    /// there nor one that wrote nothing there can pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_free_positions_second_field_is_gold_and_not_the_items_count()
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0,
            rankBonus: 0,
            freeCards:
            [
                Holding(0, itemId: 31001, quantity: 9),
                Holding(1, itemId: 31002, quantity: 9),
                Empty(2),
                Empty(3),
            ]);

        Assert.Equal(31001, Int32At(body, FreeCardItemOffset));
        Assert.Equal(
            GameProtocol.ClearRewardCardGold, Int32At(body, FreeCardGoldOffset));
        Assert.NotEqual(9, Int32At(body, FreeCardGoldOffset));
        Assert.Equal((31002, 9), SeatAt(body, 0));
    }

    /// <summary>
    /// The free position's gold is what this server pays for turning that position over.
    /// </summary>
    /// <remarks>
    /// The frame reports the payment rather than describing a second one of its own, which is the
    /// whole of what this field had to be settled to. A position holding something is worth
    /// <see cref="GameProtocol.ClearRewardCardGold"/> when it is flipped, so that is what the
    /// result screen says sits behind it; a position holding nothing is flipped for nothing, so
    /// the field is zero.
    /// <para>
    /// The figure is asserted through the name rather than as a literal, because it is one
    /// decision in one place -- see <see cref="GameProtocol.ClearRewardCardGold"/>. What the decision currently
    /// is, is pinned on its own in
    /// <see cref="The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision"/>: with
    /// only this test, a figure that quietly became zero would still agree with itself.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_free_positions_gold_is_what_a_flip_of_that_position_pays()
    {
        var held = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0,
            rankBonus: 0,
            freeCards: [Holding(0, itemId: 31001, quantity: 1), Empty(1), Empty(2), Empty(3)]);
        var empty = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0,
            rankBonus: 0,
            freeCards: [Empty(0), Empty(1), Empty(2), Holding(3, itemId: 31001, quantity: 1)]);

        Assert.Equal(
            GameProtocol.ClearRewardCardGold, Int32At(held, FreeCardGoldOffset));
        Assert.Equal(0, Int32At(empty, FreeCardGoldOffset));
    }

    /// <summary>
    /// The figure a flipped card pays is one, and it is this server's decision rather than a
    /// reading of the archive.
    /// </summary>
    /// <remarks>
    /// The one place the decision is written down, asserted so that changing it is a deliberate
    /// act rather than a drift. It is not checked against the archive because there is nothing in
    /// the archive to check it against: the clear-reward table declares no gold on a flip and
    /// holds no section that would turn one into an amount, which is exactly why the figure had to
    /// be decided rather than read. See <see cref="GameProtocol.ClearRewardCardGold"/> for the whole of the
    /// argument.
    /// </remarks>
    [Fact]
    public void The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision()
    {
        Assert.Equal(1, GameProtocol.ClearRewardCardGold);
    }

    /// <summary>
    /// The frame never claims gold behind a free position it says is empty.
    /// </summary>
    /// <remarks>
    /// <c>protocol/game/server-to-client/NOTI-0x0023.md</c> describes what the pair carries when
    /// the position holds something and when it holds nothing, and says nothing about a position
    /// carrying gold and no item. That combination was unreachable while this server paid no gold
    /// at all; it stays unreachable because the gold is conditioned on the same position lookup
    /// the item and the marker byte are, and this asserts it over every arrangement of a face
    /// rather than over the two the other cases happen to use.
    /// </remarks>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, true)]
    public void Gold_behind_the_free_position_never_arrives_without_an_item(
        bool first, bool second, bool third, bool fourth)
    {
        var face = new[] { first, second, third, fourth }
            .Select((holds, index) => holds ? Holding(index, 31001 + index, index + 1) : Empty(index))
            .ToArray();

        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0, rankBonus: 0, freeCards: face);

        if (Int32At(body, FreeCardGoldOffset) != 0)
        {
            Assert.NotEqual(0, Int32At(body, FreeCardItemOffset));
            Assert.Equal(2, body[FreeFlipPositionsOffset]);
        }
        else
        {
            Assert.Equal(0, Int32At(body, FreeCardItemOffset));
            Assert.Equal(1, body[FreeFlipPositionsOffset]);
        }
    }

    /// <summary>A clear with no cards writes the tail this frame wrote before cards existed.</summary>
    /// <remarks>
    /// Both ways of having none: a settlement that fixed no face at all, and a face whose every
    /// position came up empty. Neither may put anything on the result screen, and the byte-for-byte
    /// comparison against the no-argument call is what says so.
    /// </remarks>
    [Fact]
    public void A_clear_with_nothing_behind_its_cards_writes_the_tail_it_always_wrote()
    {
        var before = DungeonClearPayloadCodec.EncodeClearDungeonReward(clearBase: 7, rankBonus: 3);

        Assert.Equal(
            before,
            DungeonClearPayloadCodec.EncodeClearDungeonReward(
                clearBase: 7, rankBonus: 3, freeCards: []));
        Assert.Equal(
            before,
            DungeonClearPayloadCodec.EncodeClearDungeonReward(
                clearBase: 7,
                rankBonus: 3,
                freeCards: [Empty(0), Empty(1), Empty(2), Empty(3)]));
    }

    /// <summary>
    /// What a paid flip costs is written where the reference writes it, and moves nothing else.
    /// </summary>
    /// <remarks>
    /// The field the reference's own frame carries at 312 and this one at 232, which is what the
    /// seven seat flags before it fix. Compared against the frame that prices nothing rather than
    /// asserted alone: a price written four bytes off would satisfy a single read at the offset it
    /// landed on, and nothing else in this body may move for a price at all.
    /// </remarks>
    [Fact]
    public void What_a_paid_flip_costs_is_written_at_the_end_of_the_card_tail()
    {
        const int Cost = 6_780;

        var priced = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0, rankBonus: 0, freeCards: [], paidFlipCost: Cost);
        var unpriced = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0, rankBonus: 0);

        Assert.Equal(Cost, Int32At(priced, PaidCardCostOffset));
        Assert.Equal(
            unpriced.AsSpan(0, PaidCardCostOffset).ToArray(),
            priced.AsSpan(0, PaidCardCostOffset).ToArray());
        Assert.Equal(
            unpriced.AsSpan(PaidCardCostOffset + sizeof(int)).ToArray(),
            priced.AsSpan(PaidCardCostOffset + sizeof(int)).ToArray());
    }

    /// <summary>
    /// <b>The body is the same length whatever a flip costs, marker and all.</b>
    /// </summary>
    /// <remarks>
    /// The frame is read positionally and its length is fixed: the encoder's own check at the end
    /// of the build refuses a body that is not <c>ClearRewardBodyLength</c>, so a price written at
    /// some other width would not merely be wrong here, it would be refused. Swept over the two
    /// values the settlement can choose between and a price between them, with a full face in the
    /// tail so that the widest case is the one measured.
    /// </remarks>
    [Theory]
    [InlineData(DungeonClearPayloadCodec.NoPaidFlipCost)]
    [InlineData(10_300)]
    [InlineData(DungeonClearPayloadCodec.UnpricedPaidFlipCost)]
    public void The_body_keeps_its_length_whatever_a_paid_flip_costs(int paidFlipCost)
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 4_000_000_000,
            rankBonus: 800_000_000,
            freeCards:
            [
                Holding(0, itemId: 31001, quantity: 5),
                Holding(1, itemId: 31002, quantity: 6),
                Holding(2, itemId: 31003, quantity: 7),
                Holding(3, itemId: 31004, quantity: 8),
            ],
            paidFlipCost: paidFlipCost);

        Assert.Equal(274, DungeonClearPayloadCodec.ClearRewardBodyLength);
        Assert.Equal(DungeonClearPayloadCodec.ClearRewardBodyLength, body.Length);
        Assert.Equal(paidFlipCost, Int32At(body, PaidCardCostOffset));
    }

    /// <summary>
    /// The marker for a level the archive prices no flip at is not a free flip.
    /// </summary>
    /// <remarks>
    /// The one property the marker is chosen for. Zero at this offset is a price -- the price of a
    /// flip given away -- so a clear whose level the archive does not price may not be written with
    /// it; that the value written instead is one the archive's own cost table never declares is
    /// pinned against the archive, in <c>ClearRewardArchiveTests</c>.
    /// </remarks>
    [Fact]
    public void The_marker_for_a_level_with_no_price_is_not_a_free_flip()
    {
        Assert.Equal(0, DungeonClearPayloadCodec.NoPaidFlipCost);
        Assert.Equal(int.MaxValue, DungeonClearPayloadCodec.UnpricedPaidFlipCost);

        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0,
            rankBonus: 0,
            freeCards: [],
            paidFlipCost: DungeonClearPayloadCodec.UnpricedPaidFlipCost);

        Assert.NotEqual(0, Int32At(body, PaidCardCostOffset));
        Assert.Equal(
            DungeonClearPayloadCodec.UnpricedPaidFlipCost, Int32At(body, PaidCardCostOffset));
    }

    /// <summary>One position of a face, holding one piece of equipment.</summary>
    private static ClearRewardCardSlot Holding(int index, int itemId, int quantity) =>
        new(index, new ClearRewardCardItem(itemId, quantity, DropRarity.Common, Durability: 100));

    /// <summary>One position of a face, holding nothing.</summary>
    private static ClearRewardCardSlot Empty(int index) => new(index, null);

    private static int Int32At(byte[] body, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(offset));

    /// <summary>What one card seat declares: an identifier and an amount.</summary>
    private static (int ItemId, int Amount) SeatAt(byte[] body, int seat)
    {
        var at = FirstSeatOffset + (seat * SeatStride);
        return (Int32At(body, at + 1), Int32At(body, at + 5));
    }
}
