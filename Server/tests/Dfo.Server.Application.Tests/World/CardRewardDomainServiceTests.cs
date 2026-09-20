using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// Fixing a clear's cards: which facts each face is drawn for, and which seed each is drawn from.
/// </summary>
/// <remarks>
/// The rules underneath are stubbed on purpose. What a card holds is asserted against the real
/// tables in <see cref="ClearRewardRuleServiceTests"/>; what is asserted here is the half the
/// rules are not allowed to know -- that a clear has two faces, that the two are asked for with
/// different seeds, and that both seeds come out of the run rather than out of a clock.
/// </remarks>
public sealed class CardRewardDomainServiceTests
{
    private static readonly CharacterId Character = new(1);

    /// <summary>The level, the party size and the difficulty these tests settle at.</summary>
    /// <remarks>
    /// None of the four is a default of anything and no two of them are equal, so an occasion
    /// assembled from constants rather than from the run and the settlement disagrees with all of
    /// them -- and one that took the character's level where the dungeon's belongs, or the other
    /// way round, disagrees too.
    /// </remarks>
    private const int Level = 77;

    /// <summary>
    /// What the dungeon these tests are fought in declares as its own <c>[basis level]</c>.
    /// </summary>
    /// <remarks>
    /// Nowhere near <see cref="Level"/>, because the two are what this file tells apart: the price
    /// is taken at this one and the threshold band at that one.
    /// </remarks>
    private const byte BasisLevel = 41;

    private const int PartySize = 3;

    private const int Difficulty = 2;

    private static readonly ClearRewardSettlement Settlement =
        new(Character, Level, PartySize);

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
        },
        BasisLevel: BasisLevel);

    /// <summary>
    /// The same dungeon laid out over a maze of a different size.
    /// </summary>
    /// <remarks>
    /// Two room counts rather than one, because a map count taken from a constant agrees with
    /// whichever single layout a test happens to use. Two disagree with every constant.
    /// </remarks>
    private static readonly DungeonLayout Sprawling = Tutorial with
    {
        Width = 3,
        Height = 2,
        Rooms = new Dictionary<MazeCell, int>
        {
            [new MazeCell(0, 1)] = 61000,
            [new MazeCell(1, 1)] = 61001,
            [new MazeCell(2, 1)] = 61002,
            [new MazeCell(1, 0)] = 61003,
            [new MazeCell(2, 0)] = 61005,
        },
    };

    /// <summary>
    /// A rule service that writes down what it was asked, and answers a different face each time.
    /// </summary>
    /// <remarks>
    /// Answering differently on every call is the device that makes "asked once" visible: a
    /// settlement that went back to the rules would come away with other item identifiers, and the
    /// assertions here would see them. What the real rules would have drawn is not this file's
    /// subject.
    /// </remarks>
    private sealed class RecordingRules : IClearRewardRuleService
    {
        private readonly TaskCompletionSource? offersEntered;
        private readonly TaskCompletionSource? releaseOffers;

        public RecordingRules(bool pauseOffers = false)
        {
            if (!pauseOffers)
                return;
            this.offersEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            this.releaseOffers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForOffersAsync() => this.offersEntered?.Task ?? Task.CompletedTask;
        public void ReleaseOffers() => this.releaseOffers?.TrySetResult();
        /// <summary>
        /// What this fixture charges for a flip, at <see cref="BasisLevel"/> and nowhere else.
        /// </summary>
        /// <remarks>
        /// Not a number the archive declares; what the archive declares is asserted where the
        /// archive is read. It is priced at one level only so that "a level this service prices
        /// nothing at" is reachable here without the fixture having to know the file's bounds.
        /// <para>
        /// That one level is the <b>dungeon's</b>, which is what a price is keyed on. A seam that
        /// asked at the character's level would be answered null here and would fix a clear with
        /// no price at all, so the wrong key fails loudly rather than by a figure being off.
        /// </para>
        /// </remarks>
        public const int Price = 5_150;

        private readonly List<(ClearRewardOccasion Occasion, ulong Seed)> _asked = [];

        private readonly List<int> _priced = [];

        public IReadOnlyList<(ClearRewardOccasion Occasion, ulong Seed)> Asked => this._asked;

        /// <summary>Every level this service was asked the price of, in the order it was asked.</summary>
        public IReadOnlyList<int> Priced => this._priced;

        public ClearRewardChance ChanceOf(ClearRewardOccasion occasion) => new(1, 1);

        public int? PaidFlipCost(int dungeonBasisLevel)
        {
            this._priced.Add(dungeonBasisLevel);
            return dungeonBasisLevel == BasisLevel ? Price : null;
        }

        /// <summary>
        /// Every dungeon offers a flip here, and nothing in this file turns that round.
        /// </summary>
        /// <remarks>
        /// A constant rather than a knob. What a dungeon's own switch does is asserted where the
        /// switch is read -- <c>DungeonPaidCardSwitchRuleTests</c> and
        /// <c>PaidCardFlipCostTests</c> -- and a knob nothing in this file sets would look like a
        /// case being covered here while every test ran down the one branch it has.
        /// </remarks>
        public async ValueTask<bool> OffersPaidFlipAsync(
            int dungeonId, CancellationToken cancellationToken)
        {
            this.offersEntered?.TrySetResult();
            if (this.releaseOffers is not null)
                await this.releaseOffers.Task.WaitAsync(cancellationToken);
            return true;
        }

        /// <summary>What this stub's faces say a flip of them pays.</summary>
        private const int FaceGold = 37;

        public ClearRewardCardFace FaceFor(ClearRewardOccasion occasion, ulong seed)
        {
            ArgumentNullException.ThrowIfNull(occasion);
            this._asked.Add((occasion, seed));
            var answer = this._asked.Count;
            return new ClearRewardCardFace(
                occasion.Kind,
                [.. Enumerable.Range(0, ClearRewardRules.CardSlotCount)
                    .Select(index => new ClearRewardCardSlot(
                        index,
                        new ClearRewardCardItem(
                            (answer * 100) + index, 1, DropRarity.Common, 0)))],
                FaceGold);
        }
    }

    private static (CardRewardDomainService Service, DungeonRunDomainService Runs, RecordingRules Rules)
        Build(DungeonLayout layout)
    {
        var runs = new DungeonRunDomainService();
        runs.Begin(Character, layout, (byte)Difficulty);
        var rules = new RecordingRules();
        return (new CardRewardDomainService(rules, runs), runs, rules);
    }

    private static int?[] ItemIds(ClearRewardCardFace face) =>
        [.. face.Slots.Select(slot => slot.Item?.ItemId)];

    /// <summary>What a run's two faces were asked for, in the order they were asked.</summary>
    private static async Task<(ulong Free, ulong Paid)> SeedsFor(DungeonLayout layout)
    {
        var (service, _, rules) = Build(layout);
        await service.SettleAsync(Settlement, CancellationToken.None);
        return (rules.Asked[0].Seed, rules.Asked[1].Seed);
    }

    /// <summary>
    /// <b>A clear fixes two faces, and the two are drawn from different seeds.</b>
    /// </summary>
    /// <remarks>
    /// The seed is what separates them here, and while every fallback switch is off it is the only
    /// thing that can. <b>The kind does not reach the odds</b> -- see
    /// <see cref="ClearRewardCardKind"/> for why the two faces of one
    /// clear carry the same odds, and for the one thing the kind does reach, and
    /// <see cref="ClearRewardRuleServiceTests.Neither_the_kind_nor_the_dungeons_level_reaches_the_odds"/>
    /// for that held down against a table declaring the two things a paid face could have been
    /// drawn differently by. The two also share a ladder, a grade band and a pool, so one seed
    /// handed to both draws them alike wherever no fallback is switched on, rather than merely
    /// wherever two rates happened to agree --
    /// <see cref="ClearRewardRuleServiceTests.One_seed_answers_the_same_face_for_both_kinds"/> is
    /// that, position by position. Which is why the seam has to hand them different seeds, and why
    /// that is what this asserts: the seam may not be left depending on a switch to keep two faces
    /// apart.
    /// </remarks>
    [Fact]
    public async Task A_clear_fixes_two_faces_drawn_from_different_seeds()
    {
        var (service, _, rules) = Build(Tutorial);

        var settled = await service.SettleAsync(Settlement, CancellationToken.None);

        Assert.NotNull(settled);
        var cards = settled.Cards;
        Assert.Equal(2, rules.Asked.Count);
        Assert.Equal(ClearRewardCardKind.Free, rules.Asked[0].Occasion.Kind);
        Assert.Equal(ClearRewardCardKind.Paid, rules.Asked[1].Occasion.Kind);
        Assert.NotEqual(rules.Asked[0].Seed, rules.Asked[1].Seed);

        Assert.Equal(ClearRewardCardKind.Free, cards.Free.Kind);
        Assert.Equal(ClearRewardCardKind.Paid, cards.Paid.Kind);
        Assert.Empty(cards.Claimed);
    }

    [Fact]
    public async Task A_clear_hands_the_runs_committed_kills_to_both_reward_faces()
    {
        const ushort sequence = 41;
        var actor = new DungeonRoomActor(
            61670, 5, (byte)MonsterRank.Champion);
        var runs = new DungeonRunDomainService();
        var participant = new DungeonRunParticipant(
            new AccountId(1), Character, Slot: 0, IsLeader: true);
        _ = runs.PreparePartyEntry(
            [participant],
            Tutorial,
            (byte)Difficulty,
            new DungeonCellSeed(
                Tutorial.StartCell,
                Tutorial.StartRoomMapId,
                sequence,
                [new DungeonCellActorState(
                    sequence,
                    actor,
                    IsAiCharacter: false,
                    IsDead: false,
                    PendingLoot: [],
                    IsMonster: true)],
                GroundItems: []),
            DungeonRunId.New(),
            DateTimeOffset.UtcNow);
        var death = Assert.IsType<DungeonActorDeathClaim>(
            runs.TryClaimActorDeath(Character, sequence));
        var killed = Assert.IsType<DungeonRunState>(runs.CommitActorDeath(death));
        Assert.NotNull(runs.TryOpenClear(Character, killed.RunId, killed.RoomVersion));
        var rules = new RecordingRules();
        var service = new CardRewardDomainService(rules, runs);

        var settled = await service.SettleAsync(Settlement, CancellationToken.None);

        Assert.NotNull(settled);
        Assert.Equal(2, rules.Asked.Count);
        Assert.All(
            rules.Asked,
            asked =>
            {
                var recorded = Assert.Single(asked.Occasion.Kills);
                Assert.Equal(actor.Code, recorded.MonsterCode);
                Assert.Equal(MonsterRank.Champion, recorded.Rank);
            });
    }

    [Fact]
    public async Task Card_resolution_for_an_old_run_cannot_fix_cards_on_its_replacement()
    {
        var runs = new DungeonRunDomainService();
        var old = runs.Begin(Character, Tutorial, (byte)Difficulty);
        var rules = new RecordingRules(pauseOffers: true);
        var service = new CardRewardDomainService(rules, runs);
        var pending = service.SettleAsync(
            Settlement with { RunId = old.RunId }, CancellationToken.None).AsTask();
        await rules.WaitForOffersAsync();

        var replacement = runs.Begin(Character, Tutorial, (byte)(Difficulty + 1));
        rules.ReleaseOffers();

        Assert.Null(await pending);
        Assert.Null(runs.Find(Character)!.ClearRewardCards);
        Assert.NotEqual(old.RunId, replacement.RunId);
    }

    /// <summary>
    /// <b>Both seeds are derived from the run, and from nothing outside it.</b>
    /// </summary>
    /// <remarks>
    /// Asserted on the seeds rather than on the faces they produce, because "the two faces came
    /// out different" is a statement about luck: two seeds may legitimately draw the same four
    /// pieces. What has to hold is that the seeds differ and that each is a function of the run --
    /// a settlement that reached for a clock or a shared generator would pass a comparison of
    /// faces and still hand a reconnecting player different cards.
    /// </remarks>
    [Fact]
    public async Task Both_seeds_are_derived_from_the_run_and_from_nothing_else()
    {
        var first = await SeedsFor(Tutorial);

        Assert.NotEqual(first.Free, first.Paid);

        // A second server and a second run of the same layout: the same two seeds. This is what a
        // repeated settlement of one clear rests on, and what a clock or a shared generator
        // would break.
        Assert.Equal(first, await SeedsFor(Tutorial));

        // And they are the run's own rather than two constants: change what the maze was laid out
        // from, or which dungeon it is a run of, and both seeds move.
        var otherSeed = await SeedsFor(Tutorial with { Seed = Tutorial.Seed + 1 });
        Assert.NotEqual(first.Free, otherSeed.Free);
        Assert.NotEqual(first.Paid, otherSeed.Paid);

        var otherDungeon = await SeedsFor(Tutorial with { DungeonId = Tutorial.DungeonId + 2 });
        Assert.NotEqual(first.Free, otherDungeon.Free);
        Assert.NotEqual(first.Paid, otherDungeon.Paid);
    }

    /// <summary>
    /// A second settlement of one clear keeps the cards it already has, and does not draw again.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The player must be shown the same cards, which is the run's business;
    /// and a re-sent result report must not walk the equipment pool a second time to be told its
    /// answer will be thrown away, which is this service's.
    /// </remarks>
    [Fact]
    public async Task A_second_settlement_keeps_the_cards_already_fixed()
    {
        var (service, runs, rules) = Build(Tutorial);

        var first = await service.SettleAsync(Settlement, CancellationToken.None);
        var second = await service.SettleAsync(Settlement, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(ItemIds(first.Cards.Free), ItemIds(second.Cards.Free));
        Assert.Equal(ItemIds(first.Cards.Paid), ItemIds(second.Cards.Paid));
        Assert.Equal(2, rules.Asked.Count);

        var found = runs.Find(Character);
        Assert.NotNull(found);
        Assert.NotNull(found.ClearRewardCards);
        Assert.Equal(ItemIds(first.Cards.Free), ItemIds(found.ClearRewardCards.Free));
        Assert.Equal(ItemIds(first.Cards.Paid), ItemIds(found.ClearRewardCards.Paid));

        // And what the flip costs comes back both times, because the first settlement fixed it
        // with the faces and the run kept it. It was once the other way round -- asked of the
        // rules again on every settlement, so that no copy of an archive answer could go stale --
        // and that lost to the frame: the price is announced beside these faces, and a figure
        // worked out again at a level the clear's own experience bought is not the announced one.
        // That last hazard is gone now the price is the dungeon's rather than the character's,
        // but the single entry below is what says the answer was kept rather than re-fetched, and
        // it would say so whichever level the price were keyed on.
        Assert.Equal(RecordingRules.Price, first.PaidFlipCost);
        Assert.Equal(first.PaidFlipCost, second.PaidFlipCost);
        Assert.Equal(first.PaidFlipCost, first.Cards.PaidFlipCost);
        Assert.Equal([(int)BasisLevel], rules.Priced);
    }

    /// <summary>
    /// The occasion is assembled from the run where the run has the fact, and from the settlement
    /// where it does not.
    /// </summary>
    /// <remarks>
    /// The difficulty, the maze and the dungeon's own level come off the run, so no caller can
    /// settle a clear against a difficulty the character did not enter at or against a level the
    /// dungeon is not set to. The character's level and the party size are handed in, because
    /// those are facts about who cleared it rather than about the run.
    /// <para>
    /// <b>Two levels, each from the side that owns it.</b> They are far apart on purpose: a seam
    /// that filled both from the settlement, or both from the run, satisfies one of these
    /// assertions and fails the other. Which table reads which is the rules' business and is
    /// asserted there.
    /// </para>
    /// <para>
    /// How far the run got is not among them. It was carried here for a task after the
    /// composition stopped reading it, and both the field and the assertion about it are gone; the
    /// run's own record of which rooms it entered is asserted where the run is tested.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_occasion_is_assembled_from_the_run_and_the_settlement(bool sprawling)
    {
        var layout = sprawling ? Sprawling : Tutorial;
        var (service, _, rules) = Build(layout);

        await service.SettleAsync(Settlement, CancellationToken.None);

        Assert.Equal(2, rules.Asked.Count);
        Assert.All(
            rules.Asked,
            asked =>
            {
                Assert.Equal(Level, asked.Occasion.CharacterLevel);
                Assert.Equal(BasisLevel, asked.Occasion.DungeonBasisLevel);
                Assert.Equal(PartySize, asked.Occasion.PartyMemberCount);
                Assert.Equal(Difficulty, asked.Occasion.Difficulty);
                Assert.Equal(layout.Rooms.Count, asked.Occasion.MapMaxCount);
            });

        // The two layouts hold different numbers of rooms, so a map count that came from anywhere
        // but the run cannot satisfy both rows of this theory.
        Assert.NotEqual(Tutorial.Rooms.Count, Sprawling.Rooms.Count);

        // And the two levels are not one number wearing two names.
        Assert.NotEqual(Level, (int)BasisLevel);
    }

    /// <summary>
    /// <b>The dungeon's own level is what the price is asked at, not the character's.</b>
    /// </summary>
    /// <remarks>
    /// The same clear settled by two characters of different levels: one price, asked once, at the
    /// level the run's dungeon is set to. Asserted on what the rules were asked as well as on the
    /// figure that came back, because a seam that asked at the character's level would be answered
    /// null by this fixture and the clear would come out unpriced -- a failure that names something
    /// else. Against the real archive and a real dungeon this is
    /// <see cref="ClearRewardArchiveTests.One_dungeon_bands_and_prices_alike_for_every_character_level"/>.
    /// </remarks>
    [Fact]
    public async Task The_price_is_asked_at_the_dungeons_level_and_not_the_characters()
    {
        var (service, _, rules) = Build(Tutorial);
        var (other, _, otherRules) = Build(Tutorial);

        var settled = await service.SettleAsync(Settlement, CancellationToken.None);
        var byAnother = await other.SettleAsync(
            Settlement with { CharacterLevel = Level + 9 }, CancellationToken.None);

        Assert.NotNull(settled);
        Assert.NotNull(byAnother);
        Assert.Equal(RecordingRules.Price, settled.PaidFlipCost);
        Assert.Equal(settled.PaidFlipCost, byAnother.PaidFlipCost);
        Assert.Equal([(int)BasisLevel], rules.Priced);
        Assert.Equal(rules.Priced, otherRules.Priced);
    }

    /// <summary>
    /// <b>The settlement hands back what a paid flip of this clear costs, asked of the rules.</b>
    /// </summary>
    /// <remarks>
    /// The price reaches a caller through this seam and through no other, because the archive is
    /// the rule service's to read: a settlement that went to the file itself would be a second
    /// reader of the table, and a caller that did would be a third. What is pinned is that the
    /// answer comes back with the cards, and that the level it is asked about is the run's dungeon
    /// rather than any other the fixture could have been asked.
    /// </remarks>
    [Fact]
    public async Task The_settlement_hands_back_what_a_paid_flip_costs()
    {
        var (service, _, rules) = Build(Tutorial);

        var settled = await service.SettleAsync(Settlement, CancellationToken.None);

        Assert.NotNull(settled);
        Assert.Equal(RecordingRules.Price, settled.PaidFlipCost);
        Assert.Equal([(int)BasisLevel], rules.Priced);
    }

    /// <summary>
    /// <b>A clear asked again at the level its own experience bought keeps the price it settled
    /// at.</b>
    /// </summary>
    /// <remarks>
    /// The two levels are the two a real run has: a clear settles at the level read before its
    /// experience is paid, and every flip of it happens after.
    /// <para>
    /// <b>The figure could no longer move even if the price were worked out again</b>, because a
    /// price is now the dungeon's and a dungeon does not level. So what carries this test is the
    /// list of levels the rules were priced at rather than the figures: "came back with the right
    /// number" and "never went and asked" are two claims, and only the second is still capable of
    /// failing here. It is kept for that second claim -- a settlement that consulted the rules
    /// again would be reading the file at a different moment for no reason, and the run's copy is
    /// what the result screen announced.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_clear_asked_again_after_a_level_up_keeps_the_price_it_settled_at()
    {
        var (service, _, rules) = Build(Tutorial);

        var announced = await service.SettleAsync(Settlement, CancellationToken.None);
        var afterTheAward = await service.SettleAsync(
            Settlement with { CharacterLevel = Level + 1 }, CancellationToken.None);

        Assert.NotNull(announced);
        Assert.NotNull(afterTheAward);
        Assert.Equal(RecordingRules.Price, announced.PaidFlipCost);
        Assert.Equal(announced.PaidFlipCost, afterTheAward.PaidFlipCost);
        Assert.Equal(announced.PaidFlipCost, afterTheAward.Cards.PaidFlipCost);
        Assert.Equal([(int)BasisLevel], rules.Priced);
    }

    /// <summary>
    /// A level the rules price no flip at settles with no price, not with a free one.
    /// </summary>
    /// <remarks>
    /// The null travels. Zero is a price -- a flip given away -- so a seam that answered one here
    /// would have decided, silently and in the wrong place, that a level nobody priced is free.
    /// What is done about such a clear is the frame's decision, and it cannot be made at all if
    /// this answer has already been rounded off to a number.
    /// <para>
    /// The unpriced level is the dungeon's, because that is the level a price is asked at. The
    /// same clear settled by any character reaches the same nothing, which is why no character
    /// level appears here at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_level_the_rules_price_no_flip_at_settles_with_no_price()
    {
        var (service, _, _) = Build(Tutorial with { BasisLevel = BasisLevel + 1 });

        var settled = await service.SettleAsync(Settlement, CancellationToken.None);

        Assert.NotNull(settled);
        Assert.Null(settled.PaidFlipCost);
        Assert.Equal(4, settled.Cards.Free.Slots.Count);
    }

    /// <summary>One position is handed over once, and each face is claimed on its own.</summary>
    [Fact]
    public async Task A_position_is_handed_over_once()
    {
        var (service, _, _) = Build(Tutorial);
        await service.SettleAsync(Settlement, CancellationToken.None);

        var claimed = await service.ClaimAsync(
            Character, ClearRewardCardKind.Free, 2, CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(2, claimed.Index);
        Assert.Null(
            await service.ClaimAsync(
                Character, ClearRewardCardKind.Free, 2, CancellationToken.None));
        Assert.NotNull(
            await service.ClaimAsync(
                Character, ClearRewardCardKind.Paid, 2, CancellationToken.None));
    }

    /// <summary>A character in no run settles nothing, and the rules are not consulted.</summary>
    [Fact]
    public async Task A_character_in_no_run_settles_nothing()
    {
        var rules = new RecordingRules();
        var service = new CardRewardDomainService(rules, new DungeonRunDomainService());

        Assert.Null(await service.SettleAsync(Settlement, CancellationToken.None));
        Assert.Null(
            await service.ClaimAsync(
                Character, ClearRewardCardKind.Free, 0, CancellationToken.None));
        Assert.Empty(rules.Asked);
    }

    /// <summary>Cards cannot be claimed before the clear has fixed any.</summary>
    [Fact]
    public async Task Nothing_is_claimed_before_the_clear_fixes_its_cards()
    {
        var (service, _, _) = Build(Tutorial);

        Assert.Null(
            await service.ClaimAsync(
                Character, ClearRewardCardKind.Free, 0, CancellationToken.None));
    }

    /// <summary>
    /// <b>The settlement service reaches the archive only through the rules.</b>
    /// </summary>
    /// <remarks>
    /// Enumerated rather than reasoned about. A business service that acquired a content source
    /// would become a second reader of the table the rule service owns, and both readings would
    /// still look like answers.
    /// </remarks>
    [Fact]
    public void The_settlement_service_reaches_the_archive_only_through_the_rules()
    {
        var constructor = Assert.Single(typeof(CardRewardDomainService).GetConstructors());

        Assert.Equal(
            [typeof(IClearRewardRuleService), typeof(IDungeonRunDomainService)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    /// <summary>The service is registered, and resolves against the module's own wiring.</summary>
    [Fact]
    public void The_registration_wires_the_settlement_service()
    {
        var services = new ServiceCollection();
        new ApplicationModule().ConfigureServices(
            services, new ConfigurationBuilder().Build());
        services.AddSingleton<IClearRewardRateSource>(
            new ClearRewardFixture.StubRates(ClearRewardFixture.Table()));
        services.AddSingleton<IDropItemPoolSource>(
            new ClearRewardFixture.RecordingPools(
                ClearRewardFixture.Pool(
                    new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1, 1))));
        services.AddSingleton<IDungeonDefinitionSource>(new ClearRewardFixture.StubDungeons());
        services.AddSingleton<IGoldDropTableSource>(new ClearRewardFixture.StubGoldTables());

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CardRewardDomainService>(
            provider.GetRequiredService<ICardRewardDomainService>());
    }
}
