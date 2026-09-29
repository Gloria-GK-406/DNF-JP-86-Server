using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Persistence;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.CardReward;

/// <summary>
/// The two exchanges that put a clear's cards in front of the player: the layout that lays them
/// face down, and the flip that turns them face up.
/// </summary>
/// <remarks>
/// The expectations here are byte sequences rather than shapes, because both frames have captures
/// behind them: the seventeen-byte layout and the thirty-three byte answer were both observed. Where
/// this server departs from the reference -- naming a position in every record instead of only the
/// first -- the departure is asserted position by position rather than on one sample, since a
/// single sample cannot tell "each record names its own position" from "every record says zero".
/// </remarks>
public sealed class CardRewardTests
{
    private static readonly ClearRewardCardFace BonusFace = new(
        ClearRewardCardKind.BlackDiamond, [new(0, new(7777, 1, DropRarity.Rare, 10))], 0);

    [Fact]
    public async Task Free_card_grants_both_rewards_once_and_paid_card_does_not_grant_the_bonus()
    {
        var world = new World(blackDiamond: BonusFace);
        var result = await world.Flip(ClearRewardCardKind.Free, 0);
        Assert.NotNull(result.Delivered);
        Assert.NotNull(result.BlackDiamondDelivered);
        Assert.Equal(2, world.Bag.Slots.Count);
        Assert.Equal(StartingGold + FaceGold, world.Gold.Total);
        Assert.Equal(SelectCardStatus.AlreadyFlipped, (await world.Flip(ClearRewardCardKind.Free, 1)).Status);
        Assert.Equal(2, world.Bag.AddCalls);
        Assert.Null((await world.Flip(ClearRewardCardKind.Paid, 0)).BlackDiamondDelivered);
        Assert.Equal(3, world.Bag.AddCalls);
    }

    [Fact]
    public async Task Black_diamond_cannot_be_requested_as_a_third_selectable_card()
    {
        var world = new World(blackDiamond: BonusFace);
        Assert.Equal(SelectCardStatus.NoSuchPosition,
            (await world.Flip(ClearRewardCardKind.BlackDiamond, 0)).Status);
        Assert.Equal(0, world.Bag.AddCalls);
    }

    [Fact]
    public async Task Second_reward_failure_rolls_back_assets_and_releases_the_claim_for_retry()
    {
        var scope = new DomainTransactionScope();
        var world = new World(blackDiamond: BonusFace, transactions: scope);
        world.Bag.RefuseAtCall = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var lease = scope.Begin();
            scope.Current!.Enlist(world, () => new AwardParticipant(world));
            await world.Flip(ClearRewardCardKind.Free, 0);
            await lease.CommitAsync(default);
        });
        Assert.Empty(world.Bag.Slots);
        Assert.Equal(StartingGold, world.Gold.Total);
        await using (var lease = scope.Begin())
        {
            scope.Current!.Enlist(world, () => new AwardParticipant(world));
            var retry = await world.Flip(ClearRewardCardKind.Free, 0);
            Assert.Null(retry.Change);
            await lease.CommitAsync(default);
            Assert.NotNull(retry.Change);
        }
        Assert.Equal(2, world.Bag.Slots.Count);
        Assert.Equal(StartingGold + FaceGold, world.Gold.Total);
        Assert.Equal(SelectCardStatus.AlreadyFlipped, (await world.Flip(ClearRewardCardKind.Free, 0)).Status);
    }

    // Transactional asset stand-in; persistence's SQLite atomicity is covered separately.
    private sealed class AwardParticipant(World world) : ITransactionParticipant
    {
        private readonly int gold = world.Gold.Total;
        private readonly Dictionary<int, InventoryItemCoreState> slots = new(world.Bag.Slots);
        public bool CommitCanFail => true;
        public ValueTask PrepareAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public async ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            world.Bag.Slots.Clear();
            foreach (var entry in this.slots) world.Bag.Slots.Add(entry.Key, entry.Value);
            await world.Gold.AddGoldAsync(this.gold - world.Gold.Total, cancellationToken);
        }
    }

    /// <summary>
    /// The layout frame's body, transcribed from the reference session.
    /// </summary>
    /// <remarks>
    /// From <c>protocol/game/client-to-server/0x0045.md</c>:
    /// <c>SEND cmd=0x01 type=0x0046 len=32 body=(17B) [01 01 00 FF ...]</c>. Written out here rather
    /// than built from the encoder's own constants, so that changing one of those constants turns
    /// this red instead of moving the goalposts with it.
    /// </remarks>
    private static readonly byte[] CapturedLayout =
    [
        0x01, 0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    ];

    /// <summary>The wire type of the layout frame, transcribed rather than read from the constant.</summary>
    private const ushort ObservedLayoutType = 0x0046;

    /// <summary>The wire type of the flip frame, transcribed rather than read from the constant.</summary>
    private const ushort ObservedFlipType = 0x0047;

    /// <summary>
    /// How long the answer is when nothing at all is behind the four cards.
    /// </summary>
    /// <remarks>
    /// Thirty-three bytes, which is the length of the captured answer -- one envelope byte and
    /// eight four-byte records. This server fills the first four of those records differently from
    /// the reference, but not more widely, so the empty case still measures what was captured.
    /// </remarks>
    private const int CapturedEmptyAnswerLength = 33;

    /// <summary>A piece this server has actually seen a dungeon declare.</summary>
    private const int FirstItemId = 10088621;

    /// <summary>A piece dungeon 120 names in its own card declaration.</summary>
    private const int SecondItemId = 3329;

    /// <summary>What a card's piece is created carrying, as the fixture's face declares it.</summary>
    private const ushort HeldDurability = 25;

    /// <summary>The moment a flip is answered at, fixed so the frame it builds is reproducible.</summary>
    private static readonly DateTimeOffset Flipped = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// What the fixture's rules charge for a paid flip.
    /// </summary>
    /// <remarks>
    /// A price, and only a price. It is deliberately not the archive's figure: what the archive
    /// charges at a level is asserted against the archive itself in the application tests, and a
    /// number transcribed here would look like a second reading of the file while proving nothing
    /// about it. All this one has to be is something an account can be one coin short of.
    /// </remarks>
    private const int FixturePrice = 250;

    /// <summary>What the fixture's character carries before a flip, comfortably above the price.</summary>
    private const int StartingGold = 1000;

    /// <summary>The level the fixture's flips are priced by when a command is built here.</summary>
    private const int FlipLevel = 42;

    /// <summary>
    /// The level the fixture's published session carries, which is a different one on purpose.
    /// </summary>
    /// <remarks>
    /// A command built by the handler has to carry what the session publishes rather than whatever
    /// this file happens to hand its own commands, and two equal numbers could not tell those
    /// apart.
    /// </remarks>
    private const int PublishedLevel = 57;

    // ---- the layout ------------------------------------------------------------------------

    /// <summary>The layout is the seventeen bytes the reference sent, byte for byte.</summary>
    [Fact]
    public void The_layout_is_the_body_the_capture_carried()
    {
        var frame = Assert.Single(
            new CardSelectRightStateResponseEncoder().Encode(new CardSelectRightStateResponse()));

        Assert.Equal(CapturedLayout, frame[GameServerFrameEncoder.HeaderLength..]);
    }

    [Fact]
    public void Party_layout_marks_each_eligible_seat_without_changing_the_wire_shape()
    {
        var frame = Assert.Single(new CardSelectRightStateResponseEncoder().Encode(
            new CardSelectRightStateResponse([0, 1])));
        var body = frame.AsSpan(GameServerFrameEncoder.HeaderLength);

        Assert.Equal(17, body.Length);
        Assert.Equal(1, body[0]);
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(body[1..]));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(body[3..]));
        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(body[5..]));
    }

    /// <summary>It goes out as a CMD under the type the client registered a handler for.</summary>
    [Fact]
    public void The_layout_goes_out_under_the_type_the_client_registered()
    {
        var frame = Assert.Single(
            new CardSelectRightStateResponseEncoder().Encode(new CardSelectRightStateResponse()));

        Assert.Equal(0x01, frame[0]);
        Assert.Equal(ObservedLayoutType, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
    }

    /// <summary>
    /// The layout carries only occupied party seats, never reward items.
    /// </summary>
    /// <remarks>
    /// The only state the layout message may carry is the set of occupied party seats. It cannot
    /// carry an item while the cards are still face down.
    /// </remarks>
    [Fact]
    public void The_layout_message_has_nowhere_to_put_an_item()
    {
        var carried = typeof(CardSelectRightStateResponse)
            .GetProperties()
            .Where(property => property.Name != nameof(CardSelectRightStateResponse.MessageType))
            .Select(property => property.Name);

        Assert.Equal([nameof(CardSelectRightStateResponse.EligibleSeats)], carried);
    }

    /// <summary>The scroll's acknowledgement is followed by the layout, in that order.</summary>
    /// <remarks>
    /// The order is the whole of it. The client's handler for the acknowledgement is what takes the
    /// run off the result screen; a layout arriving first would be a panel drawn over a screen that
    /// has not been dismissed.
    /// </remarks>
    [Fact]
    public async Task The_scroll_acknowledgement_is_followed_by_the_layout()
    {
        var sender = new RecordingSender();
        var handler = new ScoreScrollStateHandler(
            new OneSession(Identity), sender, NullLogger<ScoreScrollStateHandler>.Instance);

        await handler.HandleAsync(
            Dispatch(), new ScoreScrollStateRequest(GameMessageMetadata.Empty), CancellationToken.None);

        Assert.Collection(
            sender.Sent,
            first => Assert.IsType<ScoreScrollStateResponse>(first),
            second => Assert.IsType<CardSelectRightStateResponse>(second));
    }

    /// <summary>The layout request is answered with the layout on its own.</summary>
    [Fact]
    public async Task The_layout_request_is_answered_with_the_layout()
    {
        var sender = new RecordingSender();
        var handler = new CardSelectRightStateHandler(
            new OneSession(Identity), sender, NullLogger<CardSelectRightStateHandler>.Instance);

        await handler.HandleAsync(
            Dispatch(),
            new CardSelectRightStateRequest(GameMessageMetadata.Empty),
            CancellationToken.None);

        Assert.IsType<CardSelectRightStateResponse>(Assert.Single(sender.Sent));
    }

    // ---- the flip's answer -----------------------------------------------------------------

    /// <summary>
    /// The answer declares all eight columns, the untouched ones included.
    /// </summary>
    /// <remarks>
    /// <b>Read back the way the client reads it, not compared against a byte layout.</b> Every
    /// assertion in this section runs on what <see cref="CardPanelFrameReader"/> walks out of the
    /// frame, so a count byte written wrong desynchronises the reader and fails the case instead
    /// of quietly agreeing with the encoder. The last attempt at this feature passed every
    /// byte-offset assertion it had while the panel showed nothing at all.
    /// </remarks>
    [Fact]
    public void The_answer_declares_every_column_including_the_untouched_ones()
    {
        var records = ReadAnswer(ClearRewardCardKind.Free, turnedColumn: 0);

        Assert.Equal(CardPanelFrameReader.AnswerRecordCount, records.Count);
        Assert.All(records, record => Assert.Empty(record.Entries));
    }

    /// <summary>An answer carrying nothing is the length the capture had.</summary>
    [Fact]
    public void An_answer_carrying_nothing_is_the_length_the_capture_had()
    {
        var body = Encode(ClearRewardCardKind.Free, turnedColumn: 0);

        Assert.Equal(CapturedEmptyAnswerLength, body.Length);
    }

    /// <summary>
    /// The column that was turned over carries this server's seat, and no other column does.
    /// </summary>
    /// <remarks>
    /// One sample could not tell "the turned column carries the seat" from "every column does",
    /// so all four are exercised and the other seven columns are checked each time.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Only_the_column_that_was_turned_over_carries_a_seat(int turned)
    {
        var records = ReadAnswer(ClearRewardCardKind.Free, turned);

        for (var column = 0; column < records.Count; column++)
        {
            Assert.Equal(
                column == turned ? AnsweringSeat : NoSeatByte, records[column].FreeSeat);
            Assert.Equal(NoSeatByte, records[column].GoldSeat);
        }
    }

    /// <summary>
    /// A flip of the gold row names the gold row's seat and leaves the free row alone.
    /// </summary>
    /// <remarks>
    /// The two rows are separate bytes with separate handlers on the client's side, and the last
    /// attempt turned a gold card over on every free flip by writing a column number into the
    /// second one. Nothing this server sends may touch a row the player did not flip.
    /// </remarks>
    [Theory]
    [InlineData(ClearRewardCardKind.Free)]
    [InlineData(ClearRewardCardKind.Paid)]
    public void A_flip_names_its_own_row_and_leaves_the_other_alone(ClearRewardCardKind kind)
    {
        var turned = ReadAnswer(kind, turnedColumn: 2)[2];

        Assert.Equal(
            kind is ClearRewardCardKind.Free ? AnsweringSeat : NoSeatByte, turned.FreeSeat);
        Assert.Equal(
            kind is ClearRewardCardKind.Paid ? AnsweringSeat : NoSeatByte, turned.GoldSeat);
    }

    /// <summary>
    /// The seat byte is a seat number, and this server answers as seat zero.
    /// </summary>
    /// <remarks>
    /// A live run answered <c>0x00</c>, <c>0x01</c>, <c>0x02</c> and <c>0x03</c> on four records
    /// and only the <c>0x00</c> one took effect, which a boolean could not produce. Zero is what
    /// the client's own run-time number came to for a lone player, so it is what this server may
    /// answer with -- and it is wrong the moment a party exists, which is why it is one constant
    /// rather than a literal repeated per record.
    /// </remarks>
    [Fact]
    public void The_seat_this_server_answers_as_is_zero()
    {
        var records = ReadAnswer(ClearRewardCardKind.Free, turnedColumn: 0);

        Assert.Equal(0x00, records[0].FreeSeat);
        Assert.Equal(0xFF, records[0].GoldSeat);
    }

    /// <summary>
    /// Every record declares no entries, so the whole answer can be walked to its end.
    /// </summary>
    /// <remarks>
    /// The reference server writes <c>0xFF</c> in the entry count of its last four records, which
    /// declares two hundred and fifty-five pairs inside a thirty-three byte frame. This is the
    /// case that stops that shape from being copied back in: the reader below refuses a frame it
    /// cannot walk to the end, so a filler byte of <c>0xFF</c> fails here rather than in a live
    /// client.
    /// </remarks>
    [Fact]
    public void The_whole_answer_can_be_walked_to_its_end()
    {
        var body = Encode(ClearRewardCardKind.Free, turnedColumn: 1);
        var records = CardPanelFrameReader.ReadSelectCardAnswer(body);

        Assert.All(records, record => Assert.Empty(record.Entries));
        Assert.All(records, record => Assert.Equal(0x00, record.Trailer));
    }

    [Fact]
    public void Empty_paid_reward_keeps_the_positional_gold_entry()
    {
        foreach (var column in (int[])[0, 1, 2, 3])
        {
            Assert.All(ReadAnswer(ClearRewardCardKind.Free, column), record => Assert.Empty(record.Entries));
            var paid = ReadAnswer(ClearRewardCardKind.Paid, column);
            Assert.Equal([new CardEntry(0, 0)], paid[column].Entries);
            Assert.All(paid.Where((_, index) => index != column), record => Assert.Empty(record.Entries));
        }
    }

    /// <summary>The answer goes out as a CMD under the type the flip arrived on.</summary>
    [Fact]
    public void The_answer_goes_out_under_the_type_the_flip_arrived_on()
    {
        var frame = Assert.Single(new SelectCardResponseEncoder().Encode(
            new SelectCardResponse(ClearRewardCardKind.Free, 0)));

        Assert.Equal(0x01, frame[0]);
        Assert.Equal(ObservedFlipType, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
    }

    [Fact]
    public void A_party_answer_carries_the_complete_free_and_paid_seat_snapshot()
    {
        var message = new SelectCardResponse(
        [
            new DungeonCardSelection(
                ClearRewardCardKind.Free, 0, new CharacterId(10), Seat: 0),
            new DungeonCardSelection(
                ClearRewardCardKind.Free, 2, new CharacterId(20), Seat: 1),
            new DungeonCardSelection(
                ClearRewardCardKind.Paid, 1, new CharacterId(20), Seat: 1),
        ]);
        var frame = Assert.Single(new SelectCardResponseEncoder().Encode(message));
        var records = CardPanelFrameReader.ReadSelectCardAnswer(
            frame[GameServerFrameEncoder.HeaderLength..]);

        Assert.Equal(0, records[0].FreeSeat);
        Assert.Equal(1, records[1].GoldSeat);
        Assert.Equal(1, records[2].FreeSeat);
        Assert.Equal(NoSeatByte, records[3].FreeSeat);
        Assert.Equal(NoSeatByte, records[3].GoldSeat);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 123)]
    [InlineData(1, 123)]
    public void Paid_equipment_and_gold_survive_complete_panel_encoding(byte seat, int gold)
    {
        var paid = new DungeonCardSelection(ClearRewardCardKind.Paid, seat, new CharacterId(20), seat)
        {
            Gold = gold,
            Item = new ClearRewardCardItem(31001, 1, DropRarity.Common, 10),
        };
        var frame = Assert.Single(new SelectCardResponseEncoder().Encode(new SelectCardResponse(
            [new DungeonCardSelection(ClearRewardCardKind.Free, 0, new CharacterId(10), 0), paid])));
        var records = CardPanelFrameReader.ReadSelectCardAnswer(frame[GameServerFrameEncoder.HeaderLength..]);
        Assert.Equal(seat, records[seat].GoldSeat);
        Assert.Equal([new CardEntry(0, gold), new CardEntry(31001, 1)], records[seat].Entries);
        // A21 golden-card renderer requires count > 1, then reads index 1.
        Assert.Equal(new CardEntry(31001, 1), records[seat].Entries[1]);
        Assert.All(records.Where((_, index) => index != seat), record => Assert.Empty(record.Entries));
    }

    // ---- the flip's request ----------------------------------------------------------------

    /// <summary>A free flip names the face and the position it clicked.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_free_flip_names_the_position_it_clicked(byte position)
    {
        var request = (SelectCardRequest)new SelectCardDecoder()
            .Decode(GameMessageMetadata.Empty, [0x00, position])!;

        Assert.Equal(ClearRewardCardKind.Free, request.Kind);
        Assert.Equal(position, (byte)request.Index);
    }

    /// <summary>A paid flip is read the same way, from its own reading.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_paid_flip_names_the_position_it_clicked(byte position)
    {
        var request = (SelectCardRequest)new SelectCardDecoder()
            .Decode(GameMessageMetadata.Empty, [0x01, position])!;

        Assert.Equal(ClearRewardCardKind.Paid, request.Kind);
        Assert.Equal(position, (byte)request.Index);
    }

    /// <summary>A face this type does not have is dropped rather than answered.</summary>
    [Theory]
    [InlineData(0x02)]
    [InlineData(0x03)]
    [InlineData(0xFF)]
    public void A_face_the_type_does_not_have_is_dropped(byte face)
    {
        Assert.Null(new SelectCardDecoder().Decode(GameMessageMetadata.Empty, [face, 0x00]));
    }

    /// <summary>A position above the last card is dropped, on either face.</summary>
    [Theory]
    [InlineData(0x00, 4)]
    [InlineData(0x00, 7)]
    [InlineData(0x00, 255)]
    [InlineData(0x01, 4)]
    [InlineData(0x01, 255)]
    public void A_position_above_the_last_card_is_dropped(byte face, byte position)
    {
        Assert.Null(new SelectCardDecoder().Decode(GameMessageMetadata.Empty, [face, position]));
    }

    /// <summary>A body too short to carry both bytes is declined, not thrown.</summary>
    /// <remarks>
    /// Declining is the requirement rather than an implementation detail: a decoder that throws
    /// takes the connection with it, and the player who would lose it has just cleared a dungeon.
    /// The client's own second packer for this type writes a single byte, so a one-byte body is a
    /// shape that really arrives.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_body_too_short_to_carry_both_bytes_is_declined(int length)
    {
        var thrown = Record.Exception(() =>
            Assert.Null(new SelectCardDecoder().Decode(GameMessageMetadata.Empty, new byte[length])));

        Assert.Null(thrown);
    }

    // ---- turning a card over ---------------------------------------------------------------

    /// <summary>Turning one card over answers with all four positions.</summary>
    /// <remarks>
    /// Whichever one is turned. The requirement is that the whole face is revealed by any flip, so
    /// flipping position three has to reveal position zero just as flipping zero reveals three.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Turning_one_card_over_reveals_the_whole_face(int flipped)
    {
        var world = new World();

        var result = await world.Flip(ClearRewardCardKind.Free, flipped);

        Assert.Equal(SelectCardStatus.Flipped, result.Status);
        Assert.Equal(4, result.Slots.Count);
        Assert.Collection(
            result.Slots,
            first => Assert.Equal(FirstItemId, first.Item!.Value.ItemId),
            second => Assert.True(second.IsEmpty),
            third => Assert.Equal(SecondItemId, third.Item!.Value.ItemId),
            fourth => Assert.True(fourth.IsEmpty));
    }

    /// <summary>The same position turned over twice hands nothing over the second time.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task The_same_position_turned_over_twice_hands_nothing_over(int flipped)
    {
        var world = new World();

        var first = await world.Flip(ClearRewardCardKind.Free, flipped);
        var second = await world.Flip(ClearRewardCardKind.Free, flipped);

        Assert.Equal(SelectCardStatus.Flipped, first.Status);
        Assert.Equal(SelectCardStatus.AlreadyFlipped, second.Status);
        Assert.Empty(second.Slots);
    }

    /// <summary>
    /// The two faces are claimed apart: taking one position of one leaves the other's alone.
    /// </summary>
    [Fact]
    public async Task Taking_a_position_of_one_face_leaves_the_other_face_alone()
    {
        var world = new World();

        var free = await world.Flip(ClearRewardCardKind.Free, 0);
        var paid = await world.Flip(ClearRewardCardKind.Paid, 0);

        Assert.Equal(SelectCardStatus.Flipped, free.Status);
        Assert.Equal(SelectCardStatus.Flipped, paid.Status);
    }

    /// <summary>A character in no run is handed nothing.</summary>
    [Fact]
    public async Task A_character_in_no_run_is_handed_nothing()
    {
        var world = new World(inARun: false);

        var result = await world.Flip(ClearRewardCardKind.Free, 0);

        Assert.Equal(SelectCardStatus.NotInARun, result.Status);
    }

    /// <summary>A run whose cards are not fixed yet is handed nothing.</summary>
    [Fact]
    public async Task A_run_whose_cards_are_not_fixed_is_handed_nothing()
    {
        var world = new World(cardsFixed: false);

        var result = await world.Flip(ClearRewardCardKind.Free, 0);

        Assert.Equal(SelectCardStatus.NoCardsFixed, result.Status);
    }

    /// <summary>A position the face does not have is handed nothing.</summary>
    [Fact]
    public async Task A_position_the_face_does_not_have_is_handed_nothing()
    {
        var world = new World();

        var result = await world.Flip(ClearRewardCardKind.Free, 4);

        Assert.Equal(SelectCardStatus.NoSuchPosition, result.Status);
    }

    /// <summary>Nothing on this path asks the rules what a card is worth.</summary>
    /// <remarks>
    /// The fixture's rule service throws on every member, so a flip that reached it would fail here
    /// rather than quietly redraw a face the player is already looking at.
    /// </remarks>
    [Fact]
    public async Task Turning_a_card_over_never_asks_the_rules_anything()
    {
        var world = new World();

        await world.Flip(ClearRewardCardKind.Free, 1);

        Assert.Equal(0, world.RuleQuestions);
    }

    // ---- the handler -----------------------------------------------------------------------

    /// <summary>A flip that took something is answered with the whole face.</summary>
    [Fact]
    public async Task A_flip_that_took_something_is_answered_with_the_whole_face()
    {
        var world = new World();
        var sender = new RecordingSender();

        await world.Handle(sender, ClearRewardCardKind.Free, 1);

        var answer = Assert.IsType<SelectCardResponse>(sender.Sent[0]);
        Assert.Equal(0, answer.TurnedColumn);
        AssertCarriedGold(sender.Sent[^2], StartingGold + FaceGold);
    }

    /// <summary>A second flip of the same position is answered with nothing at all.</summary>
    [Fact]
    public async Task A_second_flip_of_the_same_position_is_answered_with_nothing()
    {
        var world = new World();
        var sender = new RecordingSender();

        await world.Handle(sender, ClearRewardCardKind.Free, 1);
        var afterFirst = sender.Sent.Count;
        await world.Handle(sender, ClearRewardCardKind.Free, 1);

        Assert.Equal(afterFirst, sender.Sent.Count);
    }

    /// <summary>A flip on a connection with no published session never reaches the run.</summary>
    [Fact]
    public async Task A_flip_without_a_published_session_never_reaches_the_run()
    {
        var world = new World();
        var sender = new RecordingSender();

        await world.Handle(sender, ClearRewardCardKind.Free, 1, new NoSession());

        Assert.Empty(sender.Sent);
        Assert.Equal(0, world.Flips);
    }

    // ---- the piece reaching the bag ---------------------------------------------------------

    /// <summary>
    /// A flipped piece is in the bag afterwards, under its own identifier and count.
    /// </summary>
    /// <remarks>
    /// Both held positions, not one. The face's two pieces differ in identifier and in how many
    /// there are, so a delivery that handed over a constant -- the first piece, or one of
    /// something -- passes on one of these and fails on the other.
    /// </remarks>
    [Theory]
    [InlineData(0, FirstItemId, 1)]
    [InlineData(2, SecondItemId, 2)]
    public async Task A_flipped_piece_is_in_the_bag_afterwards(
        int index, int itemId, int quantity)
    {
        var world = new World();

        var result = await world.Flip(ClearRewardCardKind.Free, index);

        Assert.Equal(SelectCardStatus.Flipped, result.Status);
        var landed = world.Bag.Slots[Bag.OpenedSlot];
        Assert.Equal(itemId, landed.ItemId);
        Assert.Equal(quantity, landed.Value);
    }

    /// <summary>
    /// A piece that opened a slot of its own is not banked broken.
    /// </summary>
    /// <remarks>
    /// The bag opens a slot with no durability at all, and a piece at zero durability is a broken
    /// one. What this piece carries was decided when the face was fixed, and it travels on the card
    /// for exactly this moment.
    /// </remarks>
    [Fact]
    public async Task A_piece_that_opened_a_slot_carries_the_durability_the_card_declared()
    {
        var world = new World();

        await world.Flip(ClearRewardCardKind.Free, 0);

        Assert.Equal(HeldDurability, world.Bag.Slots[Bag.OpenedSlot].Durability);
    }

    /// <summary>
    /// A piece that joined a pile leaves the pile's own durability alone.
    /// </summary>
    /// <remarks>
    /// The pile already carries one, and it is the pile's rather than this arrival's. Writing the
    /// card's over it would rewrite what was already in the bag, which no arrival may do.
    /// </remarks>
    [Fact]
    public async Task A_piece_that_joined_a_pile_leaves_the_piles_durability_alone()
    {
        var world = new World(bagAnswer: BagAnswer.JoinsAPile);

        await world.Flip(ClearRewardCardKind.Free, 0);

        Assert.Equal(Bag.PileDurability, world.Bag.Slots[Bag.PileSlot].Durability);
        Assert.Equal(2, world.Bag.Slots[Bag.PileSlot].Value);
    }

    /// <summary>
    /// The answer goes out first and the bag change after it, in one exchange.
    /// </summary>
    /// <remarks>
    /// The ground pick-up's order, which is the reference server's. Asserted as one exchange
    /// carrying two frames rather than as two frames having been sent, because a flat count cannot
    /// tell that apart from two exchanges of one -- and it is the exchange the client reads in one
    /// go.
    /// </remarks>
    [Fact]
    public async Task The_answer_comes_first_and_the_bag_change_after_it_in_one_exchange()
    {
        var world = new World();
        var sender = new RecordingSender();

        await world.Handle(sender, ClearRewardCardKind.Free, 0);

        var exchange = Assert.Single(sender.Exchanges);
        Assert.Collection(
            exchange,
            first => Assert.IsType<SelectCardResponse>(first),
            second => Assert.IsType<InventoryDeltaMessage>(second),
            third => AssertCarriedGold(third, StartingGold + FaceGold),
            fourth => Assert.IsType<Dfo.Server.Protocol.Game.StateSynchronization.PremiumServiceStateMessage>(fourth));
    }

    /// <summary>
    /// The frame that tells the client is the endpoint layer's own, not the entry slice's.
    /// </summary>
    /// <remarks>
    /// This is the lift, asserted rather than described: the card slice may not reach into the
    /// state-synchronization slice, so the frame the two share has to live above both of them. If
    /// it moved back inside a slice this fails, whatever else still worked.
    /// </remarks>
    [Fact]
    public async Task The_frame_that_tells_the_client_lives_in_the_endpoint_layer()
    {
        var world = new World();
        var sender = new RecordingSender();

        await world.Handle(sender, ClearRewardCardKind.Free, 0);

        var told = Assert.IsType<InventoryDeltaMessage>(sender.Sent[1]);
        Assert.Equal("Dfo.Server.Protocol.Game", told.GetType().Namespace);
    }

    /// <summary>
    /// The bag change says which container, which slot, which piece and how many.
    /// </summary>
    /// <remarks>
    /// Read off the encoded body rather than off the message, because what the client gets is the
    /// body. Either visual choice still awards the one personal result fixed at settlement.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task The_bag_change_carries_the_slot_the_piece_and_how_many(
        int index)
    {
        var world = new World();
        var sender = new RecordingSender();

        await world.Handle(sender, ClearRewardCardKind.Free, index);

        var frame = Assert.Single(
            new InventoryDeltaEncoder().Encode(Assert.IsType<InventoryDeltaMessage>(sender.Sent[1])));
        Assert.Equal(0x00, frame[0]);
        Assert.Equal((ushort)0x000E, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));

        var body = frame[GameServerFrameEncoder.HeaderLength..];
        Assert.Equal((byte)InventoryContainerType.Main, body[0]);
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(1)));
        Assert.Equal((short)Bag.OpenedSlot, BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(3)));
        Assert.Equal(FirstItemId, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(9)));
        Assert.Equal(HeldDurability, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(14)));
    }

    /// <summary>The visual column does not select a different personal reward.</summary>
    [Fact]
    public async Task The_visual_column_does_not_select_a_different_personal_reward()
    {
        var world = new World();
        var sender = new RecordingSender();

        await world.Handle(sender, ClearRewardCardKind.Free, 1);

        Assert.IsType<SelectCardResponse>(sender.Sent[0]);
        AssertCarriedGold(sender.Sent[^2], StartingGold + FaceGold);
        Assert.Equal(FirstItemId, world.Bag.Slots[Bag.OpenedSlot].ItemId);
    }

    /// <summary>
    /// A bag that could not take the piece is still answered with the face, and told nothing.
    /// </summary>
    /// <remarks>
    /// Three ways for the bag to refuse and all three answered the same way. The face was revealed
    /// by the flip and cannot be un-revealed, so the answer goes out either way; what must not go
    /// out is a bag change describing a slot nothing landed in.
    /// </remarks>
    [Theory]
    [InlineData(BagAnswer.NoRoom, true)]
    [InlineData(BagAnswer.UnknownItem, true)]
    [InlineData(BagAnswer.OpensASlot, false)]
    public async Task A_bag_that_could_not_take_the_piece_is_still_answered_with_the_face(
        BagAnswer answer, bool bagBound)
    {
        var world = new World(bagBound: bagBound, bagAnswer: answer);
        var sender = new RecordingSender();

        await world.Handle(sender, ClearRewardCardKind.Free, 0);

        var told = Assert.IsType<SelectCardResponse>(sender.Sent[0]);
        Assert.Equal(0, told.TurnedColumn);
        AssertCarriedGold(sender.Sent[^2], StartingGold + FaceGold);
    }

    /// <summary>
    /// The frame that tells the client what the character now carries, and the figure in it.
    /// </summary>
    /// <remarks>
    /// Aimed at the main list's first virtual slot with item id zero, which is where the client
    /// draws carried gold. Asserting the figure and not merely the frame's presence is the point:
    /// the defect this whole change exists to remove was a server that had the right number and a
    /// client that was never told it.
    /// </remarks>
    private static void AssertCarriedGold(IGameServerMessage message, int expected)
    {
        var told = Assert.IsType<InventoryDeltaMessage>(message);

        Assert.Equal(InventoryContainerType.Main, told.ContainerType);

        var entry = Assert.Single(told.Items);
        Assert.Equal(0, entry.SlotIndex);
        Assert.Equal(0, entry.Core.ItemId);
        Assert.Equal(expected, entry.Core.Value);
    }

    /// <summary>What the fixture's settled face says a flip of it pays.</summary>
    /// <remarks>
    /// A figure of the fixture's own, not a constant out of the server. What a card pays is
    /// settled with the face from the archive's own numbers; these cases are about the flip
    /// handing over whatever that turned out to be.
    /// </remarks>
    private const int FaceGold = 37;

    /// <summary>
    /// What the fixture's paid face pays: nothing, which is what production settles for it.
    /// </summary>
    /// <remarks>
    /// <b>Different from the free face's on purpose.</b> The floor is the free card's -- a paid
    /// card was already bought at the archive's price -- so two faces carrying the same number
    /// would let a flip credit either one and look right, which is how a paid flip drawing the
    /// free card's floor went unnoticed.
    /// </remarks>
    private const int PaidFaceGold = 0;

    // ---- the money -------------------------------------------------------------------------

    /// <summary>
    /// A flip pays the card's gold whether or not the position held anything.
    /// </summary>
    /// <remarks>
    /// <b>Both positions, because the figure alone would be satisfied by paying only for the one
    /// that held something</b> -- which is what this server used to do, and what left a player who
    /// turned over an empty card with nothing at all to show for it. A free card is a floor: it
    /// pays for being turned over. The figure comes from the settled face, so this case reads it
    /// off the face rather than off any constant.
    /// </remarks>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task A_flip_pays_the_cards_gold_whether_or_not_it_held_anything(
        int index, bool holds)
    {
        var world = new World();

        var result = await world.Flip(ClearRewardCardKind.Free, index);

        Assert.Equal(holds, result.Delivered is not null);
        Assert.Equal(FaceGold, result.GoldPaid);
        Assert.Equal(StartingGold + FaceGold, world.Gold.Total);
    }

    /// <summary>
    /// A card whose piece the bag could not take is still paid for.
    /// </summary>
    /// <remarks>
    /// The card is what was turned over, and a full bag does not make it turn over less. It is
    /// also what the result screen already announced: that frame writes this gold beside the
    /// card's contents, long before any bag is asked about them.
    /// </remarks>
    [Fact]
    public async Task A_card_the_bag_could_not_take_is_still_paid_for()
    {
        var world = new World(bagAnswer: BagAnswer.NoRoom);

        var result = await world.Flip(ClearRewardCardKind.Free, 0);

        Assert.Null(result.Delivered);
        Assert.Equal(FaceGold, result.GoldPaid);
        Assert.Equal(StartingGold + FaceGold, world.Gold.Total);
    }

    /// <summary>A free flip is never charged, whatever the account is carrying.</summary>
    /// <remarks>
    /// A character carrying nothing on purpose: a free flip that reached the charging seam at all
    /// would be refused here rather than merely charged nothing.
    /// </remarks>
    [Fact]
    public async Task A_free_flip_is_never_charged()
    {
        var world = new World(startingGold: 0);

        var result = await world.Flip(ClearRewardCardKind.Free, 0);

        Assert.Equal(SelectCardStatus.Flipped, result.Status);
        Assert.Equal(0, result.GoldCharged);
        Assert.Equal(0, world.Gold.Charges);
    }

    /// <summary>
    /// A paid flip an account can afford costs the settlement's price and hands the card over.
    /// </summary>
    /// <remarks>
    /// What the character carries afterwards is the price out and the card's own gold in, which is the arithmetic
    /// of the whole exchange rather than of either half -- a flip that charged and paid nothing,
    /// or paid and charged nothing, lands somewhere else.
    /// </remarks>
    [Fact]
    public async Task A_paid_flip_costs_the_price_the_settlement_names_and_hands_the_card_over()
    {
        var world = new World();

        var result = await world.Flip(ClearRewardCardKind.Paid, 0);

        Assert.Equal(SelectCardStatus.Flipped, result.Status);
        Assert.Equal(FixturePrice, result.GoldCharged);
        Assert.Equal(StartingGold - FixturePrice + PaidFaceGold, world.Gold.Total);
        Assert.Equal(FirstItemId, world.Bag.Slots[Bag.OpenedSlot].ItemId);
    }

    /// <summary>
    /// An account one coin short of the price is refused, and is left with the coin and no card.
    /// </summary>
    /// <remarks>
    /// One coin short rather than empty, so that a charge which compared the wrong way round --
    /// or not at all -- cannot pass by the account being obviously unable to pay. Three things are
    /// asserted afterwards and they are three different failures: the money is untouched, the bag
    /// is untouched, and the position was never claimed, which the flip after the top-up shows.
    /// </remarks>
    [Fact]
    public async Task An_account_that_cannot_afford_a_paid_flip_keeps_its_money_and_its_card()
    {
        var world = new World(startingGold: FixturePrice - 1);

        var refused = await world.Flip(ClearRewardCardKind.Paid, 0);

        Assert.Equal(SelectCardStatus.NotEnoughGold, refused.Status);
        Assert.Empty(refused.Slots);
        Assert.Equal(0, refused.GoldCharged);
        Assert.Equal(FixturePrice - 1, world.Gold.Total);
        Assert.Empty(world.Bag.Slots);

        await world.Gold.AddGoldAsync(1, CancellationToken.None);
        Assert.Equal(
            SelectCardStatus.Flipped, (await world.Flip(ClearRewardCardKind.Paid, 0)).Status);
    }

    /// <summary>
    /// A paid flip at a level nothing prices is refused rather than given away.
    /// </summary>
    /// <remarks>
    /// The archive answers nothing for a level it does not list, which is not the same as pricing
    /// it at nothing. Refusing keeps that distinction: a flip this server could not price is one
    /// it does not hand out, and the result screen already told the client as much.
    /// </remarks>
    [Fact]
    public async Task A_paid_flip_at_an_unpriced_level_is_refused_rather_than_free()
    {
        var world = new World(paidFlipPrice: null);

        var result = await world.Flip(ClearRewardCardKind.Paid, 0);

        Assert.Equal(SelectCardStatus.NotPriced, result.Status);
        Assert.Equal(StartingGold, world.Gold.Total);
        Assert.Equal(0, world.Gold.Charges);
        Assert.Empty(world.Bag.Slots);
    }

    /// <summary>
    /// A concurrent reservation refuses the second flip before any money moves.
    /// </summary>
    /// <remarks>
    /// Reserving first closes the old check/charge/claim race. The losing request is refused with
    /// its carried gold untouched, and releasing the winning reservation makes the card retryable.
    /// </remarks>
    [Fact]
    public async Task A_paid_flip_losing_the_reservation_moves_no_money()
    {
        var world = new World();
        var reservation = world.Reserve(ClearRewardCardKind.Paid, 0);

        var result = await world.Flip(ClearRewardCardKind.Paid, 0);

        Assert.Equal(SelectCardStatus.AlreadyFlipped, result.Status);
        Assert.Equal(0, world.Gold.Charges);
        Assert.Equal(StartingGold, world.Gold.Total);
        world.Release(reservation);
        Assert.Equal(
            SelectCardStatus.Flipped,
            (await world.Flip(ClearRewardCardKind.Paid, 0)).Status);
    }

    [Fact]
    public async Task A_failed_persistent_card_delivery_releases_the_position_for_one_retry()
    {
        var world = new World();
        world.Bag.FailNextAdd = true;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await world.Flip(ClearRewardCardKind.Free, 0));

        Assert.Equal(
            SelectCardStatus.Flipped,
            (await world.Flip(ClearRewardCardKind.Free, 0)).Status);
        Assert.Equal(
            SelectCardStatus.AlreadyFlipped,
            (await world.Flip(ClearRewardCardKind.Free, 0)).Status);
        Assert.Single(world.Bag.Slots);
    }

    /// <summary>
    /// A paid flip asks the rules nothing either, because its price was fixed with its faces.
    /// </summary>
    /// <remarks>
    /// The counterpart of the free flip's "asks the rules nothing", and it says the same thing --
    /// which it did not while the price was worked out afresh on every flip. A flip that went back
    /// to the rules for a figure would fail here rather than quietly charge a price the result
    /// screen never announced, and the flip is charged all the same, so this is not a flip that
    /// merely skipped the question.
    /// </remarks>
    [Fact]
    public async Task A_paid_flip_asks_the_rules_nothing_because_its_price_was_already_fixed()
    {
        var world = new World();

        var result = await world.Flip(ClearRewardCardKind.Paid, 0);

        Assert.Equal(0, world.RuleQuestions);
        Assert.Equal(FixturePrice, result.GoldCharged);
    }

    /// <summary>
    /// The command carries the account and the level the published session holds.
    /// </summary>
    /// <remarks>
    /// Both are the handler's to fill in, and neither is the client's: the account is which one the
    /// character belongs to, and the level is what the price is read at. The published level differs
    /// from the one this file hands its own commands, so a handler that made one up would show.
    /// </remarks>
    [Fact]
    public async Task The_command_carries_the_account_and_character_identity()
    {
        var world = new World();
        var sender = new RecordingSender();

        await world.Handle(sender, ClearRewardCardKind.Free, 0);

        Assert.NotNull(world.LastCommand);
        Assert.Equal(Identity.AccountId, world.LastCommand.AccountId);
        Assert.Equal(Identity.CharacterId, world.LastCommand.CharacterId);
    }

    /// <summary>A flip on a connection whose session was never published never reaches the run.</summary>
    /// <remarks>
    /// A flip requires a published game session before the command may be dispatched.
    /// </remarks>
    [Fact]
    public async Task A_flip_without_a_published_entry_never_reaches_the_run()
    {
        var world = new World();
        var sender = new RecordingSender();

        await world.Handle(
            sender, ClearRewardCardKind.Free, 1, runtimeSessions: new NoRuntimeSession());

        Assert.Empty(sender.Sent);
        Assert.Equal(0, world.Flips);
    }

    // ---- wiring ----------------------------------------------------------------------------

    /// <summary>
    /// The production graph can build the message registry, which is where an unhandled decoder
    /// would show.
    /// </summary>
    /// <remarks>
    /// The registry's own check is closure: every catalogue member a registered decoder declares it
    /// can produce must have a registered handler, or building it throws. A decoder registered
    /// without its handler is the exact shape of a half-finished slice, and nothing else in this
    /// suite would notice it -- the frames would simply stop being answered on a live client.
    /// </remarks>
    [Fact]
    public void The_card_protocols_are_wired_into_the_production_graph()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IGameMessageSender>(new RecordingSender());
        var configuration = new ConfigurationBuilder().Build();
        new ApplicationModule().ConfigureServices(services, configuration);
        new InfrastructureModule().ConfigureServices(services, configuration);
        new PersistenceModule().ConfigureServices(services, configuration);
        new ProtocolModule().ConfigureServices(services, configuration);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<MessageHandlerRegistry>();

        Assert.Equal(
            typeof(CardSelectRightStateHandler),
            registry.GetHandlerType(ClientMessageType.GameCardSelectRightState));
        Assert.Equal(
            typeof(SelectCardHandler),
            registry.GetHandlerType(ClientMessageType.GameSelectCard));
        Assert.NotNull(provider.GetRequiredService<DungeonCardPresentationCoordinator>());
        Assert.NotNull(provider.GetRequiredService<CardSelectRightStateHandler>());
        Assert.NotNull(provider.GetRequiredService<SelectCardHandler>());
        Assert.NotNull(provider.GetRequiredService<SetPlayResultHandler>());
        // Transactional, not workflow. The flip puts a row in a bag, and the bag's own container
        // refuses a write with no boundary open -- so this is not bookkeeping about which enum a
        // registration carries, it is whether the arrival can happen at all. A claim marked taken
        // whose piece never landed is the state the boundary exists to make impossible.
        var registration = Assert.Single(
            provider.GetServices<CommandHandlerRegistration>(),
            value => value.CommandType == typeof(SelectCardCommand));
        Assert.Equal(CommandExecutionKind.Transactional, registration.ExecutionKind);
    }

    // ---- fixtures --------------------------------------------------------------------------

    private static byte[] Encode(ClearRewardCardKind kind, int turnedColumn)
    {
        var frame = Assert.Single(
            new SelectCardResponseEncoder().Encode(new SelectCardResponse(kind, turnedColumn)));
        return frame[GameServerFrameEncoder.HeaderLength..];
    }

    /// <summary>The answer, walked back out the way the client walks it.</summary>
    private static IReadOnlyList<FlipRecord> ReadAnswer(
        ClearRewardCardKind kind, int turnedColumn) =>
        CardPanelFrameReader.ReadSelectCardAnswer(Encode(kind, turnedColumn));

    /// <summary>The seat number this server answers as while it serves one player.</summary>
    private const byte AnsweringSeat = 0x00;

    /// <summary>What a seat byte carries when nobody turned that card over.</summary>
    private const byte NoSeatByte = 0xFF;

    private static ClearRewardCardSlot Held(int index, int itemId, int quantity) =>
        new(index, new ClearRewardCardItem(
            itemId, quantity, DropRarity.Common, Durability: HeldDurability));

    private static ClearRewardCardSlot Empty(int index) => new(index, null);

    private static NetworkDispatchContext Dispatch() =>
        new(new NetworkConnectionContext(
            new NetworkConnectionId("one"), NetworkEndpointKind.Game, 10010));

    private static SelectedSessionIdentity Identity { get; } = SelectedSessionIdentity.Create(
        new AccountId(1),
        new CharacterId(60),
        new NetworkConnectionId("one"),
        connectionGeneration: 1,
        listenerPort: 10010);

    /// <summary>
    /// A run with its cards already fixed, on the real services that keep them.
    /// </summary>
    /// <remarks>
    /// The run registry and the card service are the production ones, not stand-ins. What "the same
    /// position cannot be taken twice" means is decided in them, and a fixture that decided it here
    /// would be asserting its own behaviour. The rule service is a stand-in that throws, because
    /// nothing on this path may reach it.
    /// </remarks>
    private sealed class World
    {
        private static readonly MazeCell Cell = new(0, 0);

        private static readonly DungeonLayout Layout = new(
            DungeonId: 144,
            MazeIndex: 0,
            Width: 1,
            Height: 1,
            StartCell: Cell,
            BossCell: Cell,
            StartRoomMapId: 55611,
            Seed: 0,
            Rooms: new Dictionary<MazeCell, int> { [Cell] = 55611 },
            BasisLevel: 5,
            ExperienceWeight: 1.45);

        private readonly DungeonRunDomainService _runs;
        private readonly RefusingRules _rules;
        private readonly SelectCardCommandHandler _handler;

        public World(
            bool inARun = true,
            bool cardsFixed = true,
            bool bagBound = true,
            BagAnswer bagAnswer = BagAnswer.OpensASlot,
            int startingGold = StartingGold,
            int? paidFlipPrice = FixturePrice,
            ClearRewardCardFace? blackDiamond = null,
            IDomainTransactionAccessor? transactions = null)
        {
            this._runs = transactions is null ? new() : new(transactions);
            this._rules = new RefusingRules();
            this.Bag = new Bag(bagAnswer);
            this.Gold = new CarriedGold(startingGold);

            ICardRewardDomainService cards = new CardRewardDomainService(this._rules, this._runs, new Dfo.Server.Protocol.Tests.Support.CardPremiums());
            this._handler = new SelectCardCommandHandler(
                cards,
                this._runs,
                new StubContainers(bagBound ? this.Bag : null),
                new OnePlayingCharacter(this.Gold),
                new FixedClock(Flipped),
                new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(), new BaseCost());

            if (!inARun)
                return;

            var run = this._runs.Begin(Identity.CharacterId, Layout, difficulty: Dfo.Server.Data.World.DungeonDifficulty.Adventure);
            if (cardsFixed)
            {
                // The price goes down with the faces, which is where a clear fixes it. Handing it
                // to the run rather than to the rule service is not a shortcut: after settlement
                // there is nothing left for the rules to be asked, which is what the fixture's
                // rule service refusing every member now says.
                this._runs.FixClearRewardCards(
                    Identity.CharacterId, run.RunId,
                    Face(ClearRewardCardKind.Free),
                    Face(ClearRewardCardKind.Paid),
                    paidFlipPrice, blackDiamond);
            }

            Assert.NotNull(this._runs.TryOpenClear(
                Identity.CharacterId, run.RunId, run.RoomVersion));
            var settlement = Assert.IsType<DungeonSettlementClaim>(
                this._runs.TryClaimSettlement(Identity.CharacterId, run.RunId));
            Assert.NotNull(this._runs.CommitSettlement(settlement));
            if (cardsFixed)
            {
                Assert.NotNull(this._runs.TryOpenCardLayout(
                    Identity.CharacterId, run.RunId));
            }
        }

        /// <summary>The character's own bag, which is where a flipped piece has to end up.</summary>
        public Bag Bag { get; }

        /// <summary>The gold this character carries, which is where a flip's gold goes both ways.</summary>
        public CarriedGold Gold { get; }

        /// <summary>How many times the rules were asked anything, which must stay at zero.</summary>
        public int RuleQuestions => this._rules.Questions;

        /// <summary>How many flips reached the command, which a dropped frame must not raise.</summary>
        public int Flips { get; private set; }

        /// <summary>The last command that reached the handler, whoever built it.</summary>
        public SelectCardCommand? LastCommand { get; private set; }

        public DungeonClearRewardCardClaim Reserve(ClearRewardCardKind kind, int index)
        {
            var run = this._runs.Find(Identity.CharacterId)!;
            return this._runs.TryReserveClearRewardCard(
                Identity.CharacterId, run.RunId, kind, index)!;
        }

        public void Release(DungeonClearRewardCardClaim claim) =>
            Assert.True(this._runs.ReleaseClearRewardCard(claim));

        public ValueTask<SelectCardResult> Flip(ClearRewardCardKind kind, int index) =>
            this.Send(
                new SelectCardCommand(
                    Identity.AccountId, Identity.CharacterId, kind, index));

        public async ValueTask<SelectCardResult> Send(SelectCardCommand command)
        {
            this.Flips++;
            this.LastCommand = command;
            return await this._handler.HandleAsync(
                new CommandExecutionContext(
                    Guid.NewGuid(),
                    typeof(SelectCardCommand),
                    typeof(SelectCardCommandHandler),
                    CommandExecutionKind.Workflow),
                command,
                CancellationToken.None);
        }

        public async ValueTask Handle(
            RecordingSender sender,
            ClearRewardCardKind kind,
            int index,
            ISelectedSessionDomainService? sessions = null,
            ISelectedCharacterCurrentViewDomainService? runtimeSessions = null)
        {
            var handler = new SelectCardHandler(
                sessions ?? new OneSession(Identity),
                runtimeSessions ?? new OneRuntimeSession(PublishedEntry()),
                this._runs,
                new OneCommandBus(this),
                sender,
                NullLogger<SelectCardHandler>.Instance);

            await handler.HandleAsync(
                Dispatch(),
                new SelectCardRequest(GameMessageMetadata.Empty, kind, index),
                CancellationToken.None);
        }

        /// <summary>
        /// One face: a piece, nothing, a piece, nothing.
        /// </summary>
        /// <remarks>
        /// Both shapes on one face on purpose. A face that was all pieces or all empty would let an
        /// encoder that wrote one record per piece look correct.
        /// </remarks>
        private static ClearRewardCardFace Face(ClearRewardCardKind kind) =>
            new(
                kind,
                [Held(0, FirstItemId, 1), Empty(1), Held(2, SecondItemId, 2), Empty(3)],
                kind is ClearRewardCardKind.Free ? FaceGold : PaidFaceGold);
    }

    /// <summary>A bus that carries exactly the one command this slice sends.</summary>
    private sealed class OneCommandBus(World world) : ICommandBus
    {
        public async ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var flip = Assert.IsType<SelectCardCommand>(command);
            var result = await world.Send(flip);
            return (TResult)(object)result;
        }
    }

    /// <summary>
    /// A rule service that refuses every question, because a flip has none left to ask.
    /// </summary>
    /// <remarks>
    /// Drawing a face is what may not happen on this path: the cards were fixed when the clear
    /// settled and redrawing them would change what the player is looking at. The price is now in
    /// the same position -- fixed with the faces and read back off them -- so asking what a flip
    /// costs is a question about this clear that this clear already answered, and one that came
    /// back with a different figure would be the server charging something other than it quoted.
    /// Every question is counted as well as refused, so a test can say a flip asked none rather
    /// than only that it survived asking.
    /// </remarks>
    private sealed class RefusingRules : IClearRewardRuleService
    {
        public int Questions { get; private set; }

        public ClearRewardChance ChanceOf(ClearRewardOccasion occasion)
        {
            this.Questions++;
            throw new NotSupportedException("Turning a card over may not ask the rules anything.");
        }

        public ClearRewardCardFace FaceFor(ClearRewardOccasion occasion, ulong seed)
        {
            this.Questions++;
            throw new NotSupportedException("Turning a card over may not ask the rules anything.");
        }

        public int? PaidFlipCost(int dungeonBasisLevel)
        {
            this.Questions++;
            throw new NotSupportedException("Turning a card over may not ask the rules anything.");
        }

        public ValueTask<bool> OffersPaidFlipAsync(
            int dungeonId, CancellationToken cancellationToken)
        {
            this.Questions++;
            throw new NotSupportedException("Turning a card over may not ask the rules anything.");
        }
    }

    /// <summary>A published session standing at <see cref="PublishedLevel"/>.</summary>
    private static SelectCharacterResult PublishedEntry()
    {
        var entry = SelectCharacterPayloadTestData.Create();
        return entry with
        {
            Character = entry.Character with
            {
                Progression = new CharacterProgressionState(PublishedLevel, 0),
            },
        };
    }

    private sealed class OneRuntimeSession(SelectCharacterResult entry)
        : ILegacySessionSnapshotFixture
    {
        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            result = entry;
            shouldPersistPosition = false;
            return true;
        }

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) => entry;

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            throw new NotSupportedException("Turning a card over does not rewrite the session.");

        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result, bool persist) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool RemoveExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class NoRuntimeSession : ILegacySessionSnapshotFixture
    {
        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            result = null;
            shouldPersistPosition = false;
            return false;
        }

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture holds nothing.");

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            throw new NotSupportedException("This fixture holds nothing.");

        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result, bool persist) =>
            throw new NotSupportedException("This fixture holds nothing.");

        public bool RemoveExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture holds nothing.");
    }

    private sealed class OneSession(SelectedSessionIdentity identity) : ISelectedSessionDomainService
    {
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => identity;

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => identity;

        public SelectedSessionIdentity? Find(AccountId accountId) => identity;

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => identity;

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool ReleaseExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class NoSession : ISelectedSessionDomainService
    {
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => null;

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => null;

        public SelectedSessionIdentity? Find(AccountId accountId) => null;

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => null;

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool ReleaseExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];

        /// <summary>
        /// What went out together, one entry per call.
        /// </summary>
        /// <remarks>
        /// Kept apart from <see cref="Sent"/> because "the answer and the bag change go out in the
        /// same exchange" is a claim about grouping, and a flat list of what was sent cannot tell
        /// one exchange carrying two frames from two exchanges carrying one each.
        /// </remarks>
        public List<IReadOnlyList<IGameServerMessage>> Exchanges { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken)
        {
            this.Sent.Add(message);
            this.Exchanges.Add([message]);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken)
        {
            this.Sent.Add(message);
            this.Exchanges.Add([message]);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Sent.AddRange(delivery.RequiredDirectMessages);
            this.Exchanges.Add([.. delivery.RequiredDirectMessages]);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>What the bag does with the piece it is offered.</summary>
    public enum BagAnswer
    {
        /// <summary>It opens a slot of its own, which is what equipment gets.</summary>
        OpensASlot,

        /// <summary>It joins a pile that is already there, which carries its own durability.</summary>
        JoinsAPile,

        /// <summary>The band has nothing free.</summary>
        NoRoom,

        /// <summary>The archive declares nothing holdable under that identifier.</summary>
        UnknownItem,
    }

    /// <summary>
    /// A bag that answers one way, and remembers exactly what was written into it.
    /// </summary>
    /// <remarks>
    /// A stand-in rather than the persistent container, for the reason the ground pick-up's tests
    /// use one: the persistent container refuses a write with no transaction open, and what is under
    /// test here is what this slice hands it, not how a row reaches SQLite.
    /// </remarks>
    private sealed class Bag(BagAnswer answer) : IItemContainer
    {
        public bool FailNextAdd { get; set; }
        public int AddCalls { get; private set; }
        public int RefuseAtCall { get; set; }

        /// <summary>The slot a pile already sits in, for the pile answer.</summary>
        public const int PileSlot = 7;

        /// <summary>What that pile carries before anything joins it.</summary>
        public const ushort PileDurability = 11;

        /// <summary>The slot an opened one takes, chosen so it cannot be confused with the pile's.</summary>
        public const int OpenedSlot = 12;

        public Dictionary<int, InventoryItemCoreState> Slots { get; } =
            answer == BagAnswer.JoinsAPile
                ? new Dictionary<int, InventoryItemCoreState>
                {
                    [PileSlot] = StackableItemCore.Create(2, FirstItemId, 1)
                        with { Durability = PileDurability },
                }
                : [];

        public ContainerId Id => new("bag");

        public InventoryContainerType Type => InventoryContainerType.Main;

        public ContainerShape Shape => new(InventoryContainerType.Main, 56);

        public bool IsRemovable => false;

        public ValueTask<ItemPlacementOutcome> AddAsync(
            int itemId, int count, CancellationToken cancellationToken)
        {
            this.AddCalls++;
            if (this.AddCalls == this.RefuseAtCall)
                return ValueTask.FromResult<ItemPlacementOutcome>(new ItemPlacementOutcome.NoRoom());
            if (this.FailNextAdd)
            {
                this.FailNextAdd = false;
                throw new InvalidOperationException("injected persistent card delivery failure");
            }
            switch (answer)
            {
                case BagAnswer.OpensASlot:
                    var openedSlot = OpenedSlot + this.Slots.Count;
                    this.Slots[openedSlot] = StackableItemCore.Create(2, itemId, count);
                    return ValueTask.FromResult<ItemPlacementOutcome>(
                        new ItemPlacementOutcome.PutInEmptySlot(openedSlot, count));

                case BagAnswer.JoinsAPile:
                    var pile = this.Slots[PileSlot];
                    this.Slots[PileSlot] = pile with { Value = pile.Value + count };
                    return ValueTask.FromResult<ItemPlacementOutcome>(
                        new ItemPlacementOutcome.AddedToPile(
                            PileSlot, count, pile.Value + count));

                case BagAnswer.NoRoom:
                    return ValueTask.FromResult<ItemPlacementOutcome>(
                        new ItemPlacementOutcome.NoRoom());

                default:
                    return ValueTask.FromResult<ItemPlacementOutcome>(
                        new ItemPlacementOutcome.UnknownItem());
            }
        }

        public ValueTask<ContainerSlotItem?> ReadSlotAsync(
            int slotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                this.Slots.TryGetValue(slotIndex, out var core)
                    ? new ContainerSlotItem(slotIndex, core, ItemWorldClass.Interchangeable)
                    : null);

        public ValueTask PutAsync(
            int slotIndex, InventoryItemCoreState core, CancellationToken cancellationToken)
        {
            this.Slots[slotIndex] = core;
            return ValueTask.CompletedTask;
        }

        public ValueTask<ContainerSlotPair> ReadSlotsAsync(
            int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture is only added to and read back.");

        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "The delta is built from what was written, not from the entry projection.");

        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture is only added to and read back.");

        public ValueTask ClearAsync(int slotIndex, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture is only added to and read back.");

        public ValueTask<ItemPlacementOutcome> RefillAsync(
            ItemRefill refill, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture is only added to and read back.");

        public ValueTask<ItemMoveOutcome> MoveToAsync(
            int fromSlotIndex, int toSlotIndex, int count, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture is only added to and read back.");
    }

    /// <summary>A character bound to one bag, or to none.</summary>
    private sealed class StubContainers(IItemContainer? bag) : ICharacterContainerDomainService
    {
        public IItemContainer? FindContainer(
            CharacterId characterId, InventoryContainerType type) => bag;

        public IItemContainer GetContainer(CharacterId characterId, InventoryContainerType type) =>
            bag ?? throw new InvalidOperationException("This character has no bag.");

        public IReadOnlyList<IItemContainer> GetContainers(
            CharacterId characterId, InventoryContainerType type) => bag is null ? [] : [bag];

        public void Register(CharacterId characterId, IItemContainer container) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool Remove(CharacterId characterId, ContainerId containerId) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool UseContainer(CharacterId characterId, ContainerId containerId) =>
            throw new NotSupportedException("This fixture only answers.");

        public void Clear(CharacterId characterId) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class BaseCost : ICardFlipCostDomainService
    {
        public ValueTask<CardFlipCostSettlement?> SettleAsync(AccountId account, CharacterId character,
            ClearRewardCardKind kind, int? price, CancellationToken token) =>
            ValueTask.FromResult<CardFlipCostSettlement?>(kind == ClearRewardCardKind.Paid && (price is null || price < 0)
                ? null : new(kind == ClearRewardCardKind.Free ? 0 : price!.Value, new([]), []));
    }

}
