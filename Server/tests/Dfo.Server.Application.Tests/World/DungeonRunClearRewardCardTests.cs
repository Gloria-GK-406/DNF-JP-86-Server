using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// What a run remembers about the cards its clear turned up.
/// </summary>
/// <remarks>
/// The cards live on the run rather than in a registry of their own, which is what makes both of
/// the things this file asserts free: a clear's cards last exactly as long as the run does, and
/// the exits that end a run take them away without a line of cleanup written for them.
/// </remarks>
public sealed class DungeonRunClearRewardCardTests
{
    private static readonly CharacterId Character = new(1);

    /// <summary>What the fixture's clear charges for a paid flip.</summary>
    /// <remarks>
    /// Not a figure the archive declares; what the archive charges is asserted where the archive
    /// is read. All this one has to be is a price, and to differ from the one a second fix brings.
    /// </remarks>
    private const int Price = 4_400;

    /// <summary>The price a later fix arrives with, which the run must not take.</summary>
    private const int OtherPrice = 9_900;

    private static readonly DungeonLayout Tutorial = new(
        DungeonId: 10000,
        MazeIndex: 0,
        Width: 3,
        Height: 2,
        StartCell: new MazeCell(0, 1),
        BossCell: new MazeCell(2, 0),
        StartRoomMapId: 61000,
        Seed: 0x0BADF00D,
        Rooms: new Dictionary<MazeCell, int>
        {
            [new MazeCell(0, 1)] = 61000,
            [new MazeCell(1, 1)] = 61001,
            [new MazeCell(2, 0)] = 61005,
        });

    /// <summary>What the hand-written faces below say a flip of them pays.</summary>
    private const int FaceGold = 37;

    /// <summary>A face written out by hand; null in a position means it holds nothing.</summary>
    private static ClearRewardCardFace Face(
        ClearRewardCardKind kind, params int?[] itemIds) =>
        new(
            kind,
            [.. itemIds.Select(
                (itemId, index) => new ClearRewardCardSlot(
                    index,
                    itemId is { } id
                        ? new ClearRewardCardItem(id, 1, DropRarity.Common, 0)
                        : null))],
            FaceGold);

    private static int?[] ItemIds(ClearRewardCardFace face) =>
        [.. face.Slots.Select(slot => slot.Item?.ItemId)];

    /// <summary>A run that has not settled has no cards.</summary>
    [Fact]
    public void A_run_opens_with_no_cards()
    {
        var runs = new DungeonRunDomainService();

        var run = runs.Begin(Character, Tutorial, difficulty: 0);

        Assert.Null(run.ClearRewardCards);
        Assert.Null(runs.TryClaimClearRewardCard(Character, ClearRewardCardKind.Free, 0));
    }

    /// <summary>
    /// Cards are fixed once, and a second fix leaves the first pair standing.
    /// </summary>
    /// <remarks>
    /// The client says when a run is over, and it may say so more than once. What the player has
    /// already been shown cannot change underneath them, so the second arrival is answered with
    /// the cards of the first and the pair it brought is dropped.
    /// <para>
    /// The price is fixed by the same rule and asserted the same way, because it is shown to the
    /// player on the same frame the faces are: a run that kept the first pair and took the second
    /// price would quote a figure that belonged to a clear it threw away.
    /// </para>
    /// </remarks>
    [Fact]
    public void Cards_are_fixed_once_and_a_second_fix_leaves_the_first_pair_standing()
    {
        var runs = new DungeonRunDomainService();
        var run = runs.Begin(Character, Tutorial, difficulty: 0);

        var first = runs.FixClearRewardCards(
            Character,
            Face(ClearRewardCardKind.Free, 11, 12, null, 14),
            Face(ClearRewardCardKind.Paid, 21, 22, 23, 24),
            Price);

        Assert.NotNull(first);
        Assert.NotNull(first.ClearRewardCards);
        Assert.Equal([11, 12, null, 14], ItemIds(first.ClearRewardCards.Free));
        Assert.Equal([21, 22, 23, 24], ItemIds(first.ClearRewardCards.Paid));
        Assert.Equal(Price, first.ClearRewardCards.PaidFlipCost);

        var second = runs.FixClearRewardCards(
            Character,
            Face(ClearRewardCardKind.Free, 91, 92, 93, 94),
            Face(ClearRewardCardKind.Paid, 81, 82, 83, 84),
            OtherPrice);

        Assert.NotNull(second);
        Assert.NotNull(second.ClearRewardCards);
        Assert.Equal([11, 12, null, 14], ItemIds(second.ClearRewardCards.Free));
        Assert.Equal([21, 22, 23, 24], ItemIds(second.ClearRewardCards.Paid));

        // The second price is dropped with the second pair. The two differ on purpose: equal
        // figures could not tell a kept price from a replaced one.
        Assert.NotEqual(Price, OtherPrice);
        Assert.Equal(Price, second.ClearRewardCards.PaidFlipCost);

        // And what the run holds afterwards is the first pair too, not only what was answered.
        var found = runs.Find(Character);
        Assert.NotNull(found);
        Assert.NotNull(found.ClearRewardCards);
        Assert.Equal([11, 12, null, 14], ItemIds(found.ClearRewardCards.Free));
        Assert.Equal(Price, found.ClearRewardCards.PaidFlipCost);
    }

    /// <summary>A position is taken once; a second flip of it comes away empty-handed.</summary>
    [Fact]
    public void A_position_is_claimed_once()
    {
        var runs = new DungeonRunDomainService();
        runs.Begin(Character, Tutorial, difficulty: 0);
        runs.FixClearRewardCards(
            Character,
            Face(ClearRewardCardKind.Free, 11, 12, 13, 14),
            Face(ClearRewardCardKind.Paid, 21, 22, 23, 24),
            Price);

        var claimed = runs.TryClaimClearRewardCard(Character, ClearRewardCardKind.Free, 1);

        Assert.NotNull(claimed);
        Assert.Equal(1, claimed.Index);
        Assert.Equal(12, claimed.Item?.ItemId);
        Assert.Null(runs.TryClaimClearRewardCard(Character, ClearRewardCardKind.Free, 1));

        var found = runs.Find(Character);
        Assert.NotNull(found);
        Assert.NotNull(found.ClearRewardCards);
        Assert.True(found.ClearRewardCards.IsClaimed(ClearRewardCardKind.Free, 1));
        Assert.False(found.ClearRewardCards.IsClaimed(ClearRewardCardKind.Free, 2));
    }

    /// <summary>
    /// The free face and the paid face are claimed independently.
    /// </summary>
    /// <remarks>
    /// Two faces of four positions each, not one face of four. Taking position zero of the free
    /// face has nothing to do with position zero of the paid one, and a record that kept only the
    /// index would close both with one flip.
    /// </remarks>
    [Fact]
    public void The_two_faces_are_claimed_independently()
    {
        var runs = new DungeonRunDomainService();
        runs.Begin(Character, Tutorial, difficulty: 0);
        runs.FixClearRewardCards(
            Character,
            Face(ClearRewardCardKind.Free, 11, 12, 13, 14),
            Face(ClearRewardCardKind.Paid, 21, 22, 23, 24),
            Price);

        var free = runs.TryClaimClearRewardCard(Character, ClearRewardCardKind.Free, 0);
        var paid = runs.TryClaimClearRewardCard(Character, ClearRewardCardKind.Paid, 0);

        Assert.Equal(11, free?.Item?.ItemId);
        Assert.Equal(21, paid?.Item?.ItemId);
        Assert.Null(runs.TryClaimClearRewardCard(Character, ClearRewardCardKind.Free, 0));
        Assert.Null(runs.TryClaimClearRewardCard(Character, ClearRewardCardKind.Paid, 0));
    }

    /// <summary>
    /// A position that holds nothing is still claimed, and still only once.
    /// </summary>
    /// <remarks>
    /// An empty position is an ordinary outcome of the draw, so "you took it and it held nothing"
    /// has to stay distinguishable from "you may not take it". Answering null for both would make
    /// an empty card flippable for ever.
    /// </remarks>
    [Fact]
    public void A_position_that_holds_nothing_is_still_claimed_once()
    {
        var runs = new DungeonRunDomainService();
        runs.Begin(Character, Tutorial, difficulty: 0);
        runs.FixClearRewardCards(
            Character,
            Face(ClearRewardCardKind.Free, null, null, null, null),
            Face(ClearRewardCardKind.Paid, 21, 22, 23, 24),
            Price);

        var claimed = runs.TryClaimClearRewardCard(Character, ClearRewardCardKind.Free, 2);

        Assert.NotNull(claimed);
        Assert.True(claimed.IsEmpty);
        Assert.Equal(2, claimed.Index);
        Assert.Null(runs.TryClaimClearRewardCard(Character, ClearRewardCardKind.Free, 2));
    }

    /// <summary>A position the face does not have is refused, and refusing it records nothing.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void A_position_the_face_does_not_have_is_refused(int index)
    {
        var runs = new DungeonRunDomainService();
        runs.Begin(Character, Tutorial, difficulty: 0);
        runs.FixClearRewardCards(
            Character,
            Face(ClearRewardCardKind.Free, 11, 12, 13, 14),
            Face(ClearRewardCardKind.Paid, 21, 22, 23, 24),
            Price);

        Assert.Null(runs.TryClaimClearRewardCard(Character, ClearRewardCardKind.Free, index));

        var found = runs.Find(Character);
        Assert.NotNull(found);
        Assert.NotNull(found.ClearRewardCards);
        Assert.Empty(found.ClearRewardCards.Claimed);
        Assert.Null(found.ClearRewardCards.SlotAt(ClearRewardCardKind.Free, index));
    }

    /// <summary>A character in no run has no cards to fix and none to claim.</summary>
    [Fact]
    public void Nothing_is_fixed_or_claimed_for_a_character_in_no_run()
    {
        var runs = new DungeonRunDomainService();

        Assert.Null(
            runs.FixClearRewardCards(
                Character,
                Face(ClearRewardCardKind.Free, 11, 12, 13, 14),
                Face(ClearRewardCardKind.Paid, 21, 22, 23, 24),
                Price));
        Assert.Null(runs.TryClaimClearRewardCard(Character, ClearRewardCardKind.Free, 0));
    }

    /// <summary>
    /// Ending the run takes its cards with it, and nothing had to be written to make that so.
    /// </summary>
    /// <remarks>
    /// The whole argument for putting the cards on the run. Cards that outlived the run would be a
    /// second lifetime to manage, with its own way of leaking; here the exits a run already has
    /// are the only exits the cards have.
    /// </remarks>
    [Fact]
    public void Ending_the_run_takes_its_cards_with_it()
    {
        var runs = new DungeonRunDomainService();
        runs.Begin(Character, Tutorial, difficulty: 0);
        runs.FixClearRewardCards(
            Character,
            Face(ClearRewardCardKind.Free, 11, 12, 13, 14),
            Face(ClearRewardCardKind.Paid, 21, 22, 23, 24),
            Price);

        Assert.True(runs.End(Character));

        Assert.Null(runs.Find(Character));
        Assert.Null(runs.TryClaimClearRewardCard(Character, ClearRewardCardKind.Free, 0));

        // And the next run in the same dungeon opens with none, rather than inheriting them.
        Assert.Null(runs.Begin(Character, Tutorial, difficulty: 0).ClearRewardCards);
    }

    /// <summary>Settling does not by itself fix any cards; the settlement asks for them.</summary>
    /// <remarks>
    /// Kept apart because they are refused differently. A second settle is refused outright, and a
    /// second fix is answered with the cards already standing; folding the two together would give
    /// one of them the other's answer.
    /// </remarks>
    [Fact]
    public void Settling_a_run_does_not_by_itself_fix_its_cards()
    {
        var runs = new DungeonRunDomainService();
        var run = runs.Begin(Character, Tutorial, difficulty: 0);
        Assert.NotNull(runs.TryOpenClear(Character, run.RunId, run.RoomVersion));

        var settled = runs.TrySettle(Character);

        Assert.NotNull(settled);
        Assert.True(settled.IsSettled);
        Assert.Null(settled.ClearRewardCards);
    }
}
