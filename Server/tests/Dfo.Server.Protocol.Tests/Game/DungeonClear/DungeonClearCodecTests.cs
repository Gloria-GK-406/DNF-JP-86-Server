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
/// accounted for exactly: ten per-object experience entries this server has none of.
/// <para>
/// <b>The card tail is not asserted by offset anywhere below.</b> Its cases go through
/// <see cref="CardPanelFrameReader"/>, which consumes the frame's counts the way the client does;
/// the only byte offsets left are the fixed fields in front of the tail and the whole-frame shape
/// of a clear that fixed no cards, which is byte-identical to the reference's own.
/// </para>
/// </remarks>
public sealed class DungeonClearCodecTests
{
    /// <summary>The exit opens on one byte, and the byte is zero.</summary>
    [Fact]
    public void Party_reward_row_preserves_each_seats_gold_and_equipment()
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(7, 9,
            partyCards: [new(0, [Holding(0, 31001, 1)], 39), new(2, [Holding(0, 31002, 2)], 57)]);
        var tail = CardPanelFrameReader.ReadClearDungeonReward(body);
        Assert.Equal(39, tail.FreeRow.Columns[0].Gold);
        Assert.Equal(new CardEntry(31001, 1), tail.FreeRow.Columns[0].Piece);
        Assert.Equal(0, tail.FreeRow.Columns[1].Gold);
        Assert.Null(tail.FreeRow.Columns[1].Piece);
        Assert.Equal(57, tail.FreeRow.Columns[2].Gold);
        Assert.Equal(new CardEntry(31002, 2), tail.FreeRow.Columns[2].Piece);
    }

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

        // The card tail of a clear that fixed no cards, which is byte-identical to the
        // reference's own. Read by offset here on purpose: the point of this case is that the
        // whole frame did not move. What the bytes mean is asserted through the reader instead.
        Assert.Equal(0, body[159]); // no item-acquisition records
        Assert.Equal(1, body[160]); // the free row's first column declares its gold and no piece
        // The reference writes these same count bytes at 249, 258, 267, 276, 285, 294 and 303,
        // and its paid-card cost at 312 -- eighty bytes further along, which is its entry list.
        foreach (var column in (int[])[169, 178, 187, 196, 205, 214, 223])
            Assert.Equal(1, body[column]);
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
    /// Where the card tail begins in the bodies this encoder writes.
    /// </summary>
    /// <remarks>
    /// <b>Used only to prove the tail did not move</b>, never to reach into it. Three of the
    /// fields before it are counts and this encoder writes all three as zero, which is what puts
    /// the tail here; every assertion about the tail's own contents goes through
    /// <see cref="CardPanelFrameReader"/>, which consumes those counts instead of assuming them.
    /// </remarks>
    private const int CardTailOffset = 159;

    /// <summary>
    /// The clear's reward is declared on the free row as its gold and then its piece.
    /// </summary>
    /// <remarks>
    /// <b>Read back the way the client reads it.</b> Every assertion in this section runs on what
    /// <see cref="CardPanelFrameReader"/> walks out of the frame rather than on a byte offset. The
    /// previous attempt at this feature asserted offsets, agreed with its own encoder by
    /// construction, and passed every case while the panel displayed nothing.
    /// <para>
    /// The two entries are ordered and the order is what gives them their meaning: the first is
    /// the card's gold whatever identifier sits beside it, the second is the piece. A live run
    /// that wrote a real identifier into the first entry still saw its amount displayed as gold.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_reward_is_declared_on_the_free_row_as_gold_then_the_piece()
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0,
            rankBonus: 0,
            freeCards: [Holding(0, itemId: 31001, quantity: 5)],
            cardGold: 273);
        var card = CardPanelFrameReader.ReadClearDungeonReward(body).FreeRow.Columns[0];

        Assert.Equal(2, card.Entries.Count);
        Assert.Equal(273, card.Gold);
        Assert.Equal(new CardEntry(31001, 5), card.Piece);
    }

    /// <summary>A card with nothing behind it declares its gold and stops there.</summary>
    /// <remarks>
    /// One entry rather than none: the card still exists and still pays what it pays, which here
    /// is nothing. Declaring no entries at all is what the two rows this server does not deliver
    /// do, and that is a different statement.
    /// </remarks>
    [Fact]
    public void A_card_with_nothing_behind_it_declares_only_its_gold()
    {
        var card = Tail(Empty(0)).FreeRow.Columns[0];

        Assert.Single(card.Entries);
        Assert.Equal(0, card.Gold);
        Assert.Null(card.Piece);
    }

    /// <summary>
    /// The gold entry's identifier is never the piece's, whatever the piece is.
    /// </summary>
    /// <remarks>
    /// The client does not read this field at all, so its value cannot be observed in play -- but
    /// writing the piece's identifier there is exactly the mistake the previous attempt made, and
    /// it is what pushed the piece one entry too early and left the panel with a gold amount where
    /// its item should have been.
    /// </remarks>
    [Fact]
    public void The_gold_entrys_identifier_is_not_the_pieces()
    {
        var card = Tail(Holding(0, itemId: 31001, quantity: 5)).FreeRow.Columns[0];

        Assert.Equal(0, card.Entries[0].ItemId);
        Assert.Equal(31001, card.Entries[1].ItemId);
    }

    /// <summary>
    /// The card carries the gold the settlement handed this frame, and carries it whether or not
    /// anything is behind the card.
    /// </summary>
    /// <remarks>
    /// <b>The two are independent, and that is the point.</b> A free card is a floor: it pays for
    /// being turned over rather than for what came out of it, so a card with nothing behind it
    /// still announces its gold. Tying the two together is what used to leave a player who turned
    /// over an empty card with no sign that anything had happened.
    /// <para>
    /// The figure itself is not this frame's to decide. It is settled with the face, from the
    /// archive's per-level base and per-difficulty multiplier, and this encoder writes what it is
    /// handed -- which is why the case passes a figure of its own rather than any constant.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_card_carries_the_gold_the_settlement_handed_it(bool holds)
    {
        const int Gold = 273;
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0,
            rankBonus: 0,
            freeCards: [holds ? Holding(0, 31001, 1) : Empty(0)],
            cardGold: Gold);

        var card = CardPanelFrameReader.ReadClearDungeonReward(body).FreeRow.Columns[0];

        Assert.Equal(Gold, card.Gold);
        Assert.Equal(holds, card.Piece is not null);
    }

    /// <summary>Only the card the reward is on carries gold; the other seven carry none.</summary>
    /// <remarks>
    /// The gold belongs to the one card a flip can reach, not to the row. A figure spread across
    /// the row would announce seven payments no player can be given.
    /// </remarks>
    [Fact]
    public void Only_the_card_the_reward_is_on_carries_gold()
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0, rankBonus: 0, freeCards: [Holding(0, 31001, 1)], cardGold: 273);

        var row = CardPanelFrameReader.ReadClearDungeonReward(body).FreeRow;

        Assert.Equal(273, row.Columns[0].Gold);
        for (var column = 1; column < CardPanelFrameReader.ColumnsPerRow; column++)
            Assert.Equal(0, row.Columns[column].Gold);
    }

    /// <summary>
    /// Only the position the flip claims reaches the wire.
    /// </summary>
    /// <remarks>
    /// <b>The client never says which card was clicked</b> -- the request's position byte is a
    /// constant written into the client's own packer -- so the position the claim takes is fixed
    /// and the positions after it have no way to reach a player. Putting them on the wire would
    /// declare rewards nobody can be handed. This is the case that would fail if the encoder went
    /// back to spreading a face across the columns.
    /// </remarks>
    [Fact]
    public void Only_the_position_the_flip_claims_reaches_the_wire()
    {
        var tail = Tail(
            Holding(0, 31001, 1),
            Holding(1, 31002, 2),
            Holding(2, 31003, 3),
            Holding(3, 31004, 4));

        Assert.Equal(new CardEntry(31001, 1), tail.FreeRow.Columns[0].Piece);
        for (var column = 1; column < CardPanelFrameReader.ColumnsPerRow; column++)
        {
            Assert.Single(tail.FreeRow.Columns[column].Entries);
            Assert.Equal(0, tail.FreeRow.Columns[column].Gold);
            Assert.Null(tail.FreeRow.Columns[column].Piece);
        }
    }

    /// <summary>
    /// The gold row and the black-diamond row are declared, and declared empty.
    /// </summary>
    /// <remarks>
    /// They are the second and third rows of the same panel and their contents are what a player
    /// sees after paying for a flip of them. This server delivers neither, so every column of both
    /// declares nothing at all -- which is a different statement from a card that exists and pays
    /// nothing, and is why these two are checked for no entries rather than for an empty one.
    /// <para>
    /// The previous attempt wrote the clear's reward into the second row, which is what put a
    /// piece behind a gold card nobody had paid for.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_gold_and_black_diamond_rows_are_declared_empty()
    {
        var tail = Tail(Holding(0, 31001, 1));

        foreach (var row in (CardRow[])[tail.GoldRow, tail.BlackDiamondRow])
        {
            Assert.Equal(CardPanelFrameReader.ColumnsPerRow, row.Columns.Count);
            Assert.All(row.Columns, column => Assert.Empty(column.Entries));
        }
    }

    /// <summary>The frame grows by one entry when a piece sits behind the card, and by nothing else.</summary>
    [Theory]
    [InlineData(false, 274)]
    [InlineData(true, 282)]
    public void The_frame_grows_by_one_entry_when_a_piece_sits_behind_the_card(
        bool holds, int expected)
    {
        var body = Body(holds ? Holding(0, 31001, 1) : Empty(0));

        Assert.Equal(expected, body.Length);
        Assert.Equal(
            expected,
            DungeonClearPayloadCodec.ClearRewardBodyLengthFor(
                [holds ? Holding(0, 31001, 1) : Empty(0)]));
    }

    /// <summary>A clear that fixed no cards writes a tail that hands nothing to anybody.</summary>
    [Fact]
    public void A_clear_that_fixed_no_cards_hands_nothing_to_anybody()
    {
        var tail = CardPanelFrameReader.ReadClearDungeonReward(
            DungeonClearPayloadCodec.EncodeClearDungeonReward(clearBase: 0, rankBonus: 0));

        Assert.All(tail.FreeRow.Columns, column => Assert.Equal(0, column.Gold));
        Assert.All(tail.FreeRow.Columns, column => Assert.Null(column.Piece));
        Assert.All(tail.GoldRow.Columns, column => Assert.Empty(column.Entries));
        Assert.All(tail.BlackDiamondRow.Columns, column => Assert.Empty(column.Entries));
        Assert.Equal(0, tail.PaidFlipCost);
        Assert.All(tail.Trailer, value => Assert.Equal(0, value));
    }

    /// <summary>What a paid flip costs sits between the first two rows and is carried whole.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(10300)]
    [InlineData(int.MaxValue)]
    public void What_a_paid_flip_costs_sits_between_the_first_two_rows(int paidFlipCost)
    {
        var tail = CardPanelFrameReader.ReadClearDungeonReward(
            DungeonClearPayloadCodec.EncodeClearDungeonReward(
                clearBase: 0,
                rankBonus: 0,
                freeCards: [Holding(0, 31001, 1)],
                paidFlipCost: paidFlipCost));

        Assert.Equal(paidFlipCost, tail.PaidFlipCost);
    }

    /// <summary>What a paid flip costs does not change how long the frame is.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(10300)]
    [InlineData(int.MaxValue)]
    public void The_body_keeps_its_length_whatever_a_paid_flip_costs(int paidFlipCost)
    {
        var body = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0, rankBonus: 0, freeCards: [Empty(0)], paidFlipCost: paidFlipCost);

        Assert.Equal(DungeonClearPayloadCodec.ClearRewardBodyLength, body.Length);
    }

    /// <summary>
    /// The marker for a level the archive prices no flip at is not a free flip.
    /// </summary>
    /// <remarks>
    /// Zero is a price and not an absence, so a settlement that names no price writes a value the
    /// archive's own cost table cannot produce rather than giving the flip away.
    /// </remarks>
    [Fact]
    public void The_marker_for_a_level_with_no_price_is_not_a_free_flip()
    {
        var tail = CardPanelFrameReader.ReadClearDungeonReward(
            DungeonClearPayloadCodec.EncodeClearDungeonReward(
                clearBase: 0,
                rankBonus: 0,
                freeCards: [Holding(0, 31001, 1)],
                paidFlipCost: DungeonClearPayloadCodec.UnpricedPaidFlipCost));

        Assert.NotEqual(0, tail.PaidFlipCost);
        Assert.Equal(DungeonClearPayloadCodec.UnpricedPaidFlipCost, tail.PaidFlipCost);
    }

    /// <summary>
    /// A face changes the card tail and nothing in front of it.
    /// </summary>
    /// <remarks>
    /// The three counts before the tail decide where the tail begins. A face that reached one of
    /// them would move the whole tail without changing a single field this server means to change,
    /// and the client would read the frame apart. Comparing everything in front of the tail whole
    /// is what says none of that happened.
    /// </remarks>
    [Fact]
    public void A_face_changes_the_card_tail_and_nothing_in_front_of_it()
    {
        var bare = DungeonClearPayloadCodec.EncodeClearDungeonReward(clearBase: 7, rankBonus: 9);
        var full = DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 7, rankBonus: 9, freeCards: [Holding(0, 31001, 5)]);

        Assert.Equal(
            bare.AsSpan(0, CardTailOffset).ToArray(),
            full.AsSpan(0, CardTailOffset).ToArray());
        Assert.Equal(0, CardPanelFrameReader.ReadClearDungeonReward(bare).AcquisitionRecordCount);
        Assert.Equal(0, CardPanelFrameReader.ReadClearDungeonReward(full).AcquisitionRecordCount);
    }

    /// <summary>
    /// Every face this encoder can be given produces a frame that can be walked to its end.
    /// </summary>
    /// <remarks>
    /// The check the previous attempt had no equivalent of. A frame whose declared counts and
    /// carried bytes disagree is one the client desynchronises on, and no assertion about a
    /// particular field would notice it -- the reader refuses such a frame outright.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Every_face_produces_a_frame_that_can_be_walked_to_its_end(int holding)
    {
        var face = Enumerable.Range(0, 4)
            .Select(index =>
                index < holding ? Holding(index, 31001 + index, index + 1) : Empty(index))
            .ToArray();

        var tail = CardPanelFrameReader.ReadClearDungeonReward(
            DungeonClearPayloadCodec.EncodeClearDungeonReward(
                clearBase: 0, rankBonus: 0, freeCards: face));

        Assert.Equal(CardPanelFrameReader.ColumnsPerRow, tail.FreeRow.Columns.Count);
        Assert.Equal(CardPanelFrameReader.TrailerLength, tail.Trailer.Count);
    }

    /// <summary>One position of a face, holding one piece of equipment.</summary>
    private static ClearRewardCardSlot Holding(int index, int itemId, int quantity) =>
        new(index, new ClearRewardCardItem(itemId, quantity, DropRarity.Common, Durability: 100));

    /// <summary>One position of a face, holding nothing.</summary>
    private static ClearRewardCardSlot Empty(int index) => new(index, null);

    /// <summary>The body this encoder writes for a face.</summary>
    private static byte[] Body(params ClearRewardCardSlot[] cards) =>
        DungeonClearPayloadCodec.EncodeClearDungeonReward(
            clearBase: 0, rankBonus: 0, freeCards: cards);

    /// <summary>That body's card tail, walked back out the way the client walks it.</summary>
    private static ClearRewardTail Tail(params ClearRewardCardSlot[] cards) =>
        CardPanelFrameReader.ReadClearDungeonReward(Body(cards));
}
