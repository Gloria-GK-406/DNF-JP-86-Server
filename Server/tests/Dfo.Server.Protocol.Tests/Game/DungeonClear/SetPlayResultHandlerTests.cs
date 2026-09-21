using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.DungeonClear;

/// <summary>
/// What answering the client's result report does, now that a clear pays.
/// </summary>
/// <remarks>
/// Until 2026-08-29 a whole dungeon paid nothing: this handler read the character's entry-time
/// total and echoed it back, and filled the reward frame with zeroes. What is checked here is the
/// three things that follow from paying -- the session is corrected before the frames are built, a
/// level crossed at settlement has the same consequences as one crossed on a kill, and the result
/// screen's figures are the ones that were actually paid.
/// </remarks>
public sealed class SetPlayResultHandlerTests
{
    private const int ClearBase = 552;
    private const int RankBonus = 66;

    /// <summary>The result screen reports what was paid, and the frames come in the reference's order.</summary>
    [Fact]
    public async Task TheResultScreenReportsWhatWasPaid()
    {
        var sent = await Settle();

        Assert.Collection(
            sent,
            frame => Assert.IsType<PlayResultNotification>(frame),
            frame => Assert.IsType<ExperienceNotification>(frame),
            frame =>
            {
                var reward = Assert.IsType<ClearDungeonRewardNotification>(frame);
                Assert.Equal((uint)ClearBase, reward.ClearBase);
                Assert.Equal((uint)RankBonus, reward.RankBonus);
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheResultScreenIdentifiesTheFrozenRunPurpose(bool questLinked)
    {
        var purpose = questLinked
            ? DungeonRunPurpose.QuestLinked(1776, DungeonQuestConnectionState.Active)
            : DungeonRunPurpose.Ordinary;

        var sent = await Settle(purpose: purpose);

        Assert.Equal(
            questLinked,
            Assert.Single(sent.OfType<PlayResultNotification>()).QuestMaze);
    }

    [Fact]
    public async Task ARunWithoutVerifiedClearCannotBeSettled()
    {
        var bus = new FixedAward(leveledUp: false);
        var purpose = DungeonRunPurpose.QuestLinked(
            1776,
            DungeonQuestConnectionState.Active);

        var sent = await Settle(bus: bus, purpose: purpose, canComplete: false);

        Assert.Empty(sent);
        Assert.Equal(0, bus.TimesAsked);
    }

    [Fact]
    public async Task Party_helper_uses_the_shared_run_clear_rule_not_the_leaders_quest_log()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(9),
            new CharacterId(59),
            Slot: 0,
            IsLeader: true);
        var rules = new PurposeSettlementRules(canComplete: true);

        var sent = await Settle(
            purpose: DungeonRunPurpose.QuestLinked(
                1776,
                DungeonQuestConnectionState.Active),
            rules: rules,
            leader: leader);

        Assert.NotEmpty(sent);
        Assert.Equal(new AccountId(1), rules.AccountId);
        Assert.Equal(new CharacterId(60), rules.CharacterId);
    }

    [Fact]
    public async Task Solo_run_keeps_using_the_current_session_for_story_completion()
    {
        var placeholderLeader = new DungeonRunParticipant(
            new AccountId(0),
            new CharacterId(60),
            Slot: 0,
            IsLeader: true);
        var rules = new PurposeSettlementRules(canComplete: true);

        var sent = await Settle(
            purpose: DungeonRunPurpose.QuestLinked(
                1776,
                DungeonQuestConnectionState.Active),
            rules: rules,
            leader: placeholderLeader,
            includeOnlyLeader: true);

        Assert.NotEmpty(sent);
        Assert.Equal(new AccountId(1), rules.AccountId);
        Assert.Equal(new CharacterId(60), rules.CharacterId);
    }

    /// <summary>
    /// Every point paid is reported somewhere: the two figures on the screen, and the total.
    /// </summary>
    /// <remarks>
    /// This is the constraint the whole settlement was scoped around. The experience notice reports
    /// a total, from which the client works out a gain; the result screen reports the gain in
    /// parts. Paying an amount that appears in neither breakdown -- an entitlement the frame has no
    /// measured slot for -- leaves the screen short by the difference, and nothing but a player's
    /// own arithmetic would catch it.
    /// </remarks>
    [Fact]
    public async Task NothingIsPaidThatTheScreenCannotAccountFor()
    {
        var sent = await Settle();

        var reward = sent.OfType<ClearDungeonRewardNotification>().Single();
        var experience = sent.OfType<ExperienceNotification>().Single();

        Assert.Equal((uint)(ClearBase + RankBonus), reward.ClearBase + reward.RankBonus);
        Assert.Equal(StartingExperience + ClearBase + RankBonus, experience.TotalExperience);
    }

    /// <summary>The experience frame carries where the character stands after the award.</summary>
    /// <remarks>
    /// Not before it. This handler used to report the session's entry-time total, which is how a
    /// character could clear a dungeon and watch the bar not move.
    /// </remarks>
    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public async Task TheExperienceFrameCarriesTheStandingAfterTheAward(
        bool leveledUp, byte expectedLevel)
    {
        var sent = await Settle(leveledUp);

        var experience = sent.OfType<ExperienceNotification>().Single();
        Assert.Equal(StartingExperience + ClearBase + RankBonus, experience.TotalExperience);
        Assert.Equal(expectedLevel, experience.Level);
    }

    /// <summary>A level crossed at settlement refreshes what the character may take.</summary>
    /// <remarks>
    /// The same consequence a kill has, for the same reason: a level decides which quests are
    /// offered, so a clear that crosses one has to have that worked out again. A clear is more
    /// likely to cross a threshold than a kill is, because it pays in one lump.
    /// </remarks>
    [Fact]
    public async Task ALevelCrossedAtSettlementRefreshesTheOfferList()
    {
        var content = new StubWorldContentEntryRuleService();
        var sent = await Settle(leveledUp: true, content: content);

        Assert.Equal(4, content.LastSubject?.Level);
        Assert.Contains(sent, frame => frame is AvailableQuestListMessage);
        // And after the standing, before the breakdown -- it is about the new level, which the
        // frame before it announced.
        Assert.Equal(
            [typeof(PlayResultNotification), typeof(ExperienceNotification),
             typeof(AvailableQuestListMessage), typeof(ClearDungeonRewardNotification)],
            sent.Select(frame => frame.GetType()));
    }

    /// <summary>
    /// The points reported are the ones the character has after the award, not before it.
    /// </summary>
    /// <remarks>
    /// <b>The whole reason this frame stopped reading the session.</b> A level buys skill points,
    /// and the level is bought by this very clear -- so a figure taken before the award is a level
    /// out of date, and it is the figure the player is left staring at until they log out again.
    /// <para>
    /// A clear that crosses nothing asks once, and that half matters too: nothing else a clear
    /// does can move the ledger, so asking again would be a second walk through the archive for an
    /// answer that cannot have changed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePointsReportedAreTheOnesTheAwardLeftBehind()
    {
        var crossed = new SkillPointLedger();
        var reported = await Settle(leveledUp: true, ledger: crossed);

        Assert.Equal(2, crossed.TimesAsked);
        Assert.Equal(
            SkillPointLedger.AfterTheLevel,
            reported.OfType<ExperienceNotification>().Single().SkillPoints);

        var stayed = new SkillPointLedger();
        var unchanged = await Settle(ledger: stayed);

        Assert.Equal(1, stayed.TimesAsked);
        Assert.Equal(
            SkillPointLedger.BeforeTheLevel,
            unchanged.OfType<ExperienceNotification>().Single().SkillPoints);
    }

    /// <summary>A settlement that crosses no threshold sends no quest list.</summary>
    /// <remarks>
    /// Sending it anyway would be harmless-looking and wrong: the client would redraw a list it
    /// already has, on every clear, for no reason anybody could point at later.
    /// </remarks>
    [Fact]
    public async Task ASettlementThatCrossesNoThresholdSendsNoQuestList() =>
        Assert.DoesNotContain(await Settle(), frame => frame is AvailableQuestListMessage);

    /// <summary>
    /// An award that did not happen declares nothing, rather than what it would have been.
    /// </summary>
    /// <remarks>
    /// The grant answers with nothing when there is no such character, or when the row would not
    /// take the write. Neither throws. Reporting the two figures anyway would put a breakdown on
    /// the result screen for experience the character never received -- the exact inverse of what
    /// this settlement was scoped around, and invisible except to a player checking their own bar.
    /// </remarks>
    [Fact]
    public async Task AnAwardThatDidNotHappenDeclaresNothing()
    {
        var sent = await Settle(awarded: false, ledger: new SkillPointLedger { Progress = new CharacterProgressionState(9, 6000) });

        var reward = sent.OfType<ClearDungeonRewardNotification>().Single();
        Assert.Equal(0u, reward.ClearBase);
        Assert.Equal(0u, reward.RankBonus);

        // And the standing reported is the one the character actually holds.
        var experience = sent.OfType<ExperienceNotification>().Single();
        Assert.Equal(6000u, experience.TotalExperience);
        Assert.Equal(9, experience.Level);
    }

    /// <summary>
    /// Without a skill-point ledger the clear is neither paid nor reported.
    /// </summary>
    /// <remarks>
    /// The same refusal a kill makes, for the same reason: the experience frame carries the
    /// character's remaining points as well as the total, so without them there is no frame to
    /// report a new standing on. Paying regardless would credit the character, spend the run, and
    /// leave the client drawing the total it entered with.
    /// </remarks>
    [Fact]
    public async Task WithoutASkillLedgerTheClearIsNeitherPaidNorReported()
    {
        var bus = new FixedAward(leveledUp: false);
        var sent = await Settle(bus: bus, withLedger: false);

        Assert.False(bus.WasAsked);
        Assert.Collection(
            sent,
            frame => Assert.IsType<PlayResultNotification>(frame),
            frame =>
            {
                var reward = Assert.IsType<ClearDungeonRewardNotification>(frame);
                Assert.Equal(0u, reward.ClearBase);
                Assert.Equal(0u, reward.RankBonus);
            });
    }

    /// <summary>
    /// A clamped award is reported as what landed, not as what the clear was worth.
    /// </summary>
    /// <remarks>
    /// At the top of the experience table the grant hands back less than it was asked to pay, and
    /// with the parts no longer attributable it hands the whole of it back as the base. The result
    /// screen has to follow: reporting the command's two figures beside a total that moved by less
    /// would put experience on the screen the character did not receive.
    /// <para>
    /// This is the half of the fix a test would otherwise miss. The command's figures and the
    /// award's agree whenever nothing is clamped, so reading the wrong one is invisible until they
    /// do not -- which is exactly here.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AClampedAwardIsReportedAsWhatLanded()
    {
        var sent = await Settle(bus: new FixedAward(leveledUp: false, clamped: true));

        var reward = sent.OfType<ClearDungeonRewardNotification>().Single();
        Assert.Equal(ClampedLanding, reward.ClearBase);
        Assert.Equal(0u, reward.RankBonus);
    }

    /// <summary>What the ceiling left room for, which is less than the clear was worth.</summary>
    private const uint ClampedLanding = 40;

    /// <summary>A run pays once however many times its result is reported.</summary>
    /// <remarks>
    /// The client says when a run is over. Before the settlement paid anything a repeated report
    /// was harmless; now it would be a repeated payment, and the run stays in progress until the
    /// character walks out, so nothing else would refuse it. The kill path has carried the same
    /// guard since it started paying, and for the same reason.
    /// </remarks>
    [Fact]
    public async Task ARunIsPaidOnceHoweverManyTimesItsResultIsReported()
    {
        var bus = new FixedAward(leveledUp: false);
        var sent = await Settle(bus: bus, reports: 3);

        Assert.Equal(1, bus.TimesAsked);
        Assert.Single(sent.OfType<ClearDungeonRewardNotification>());
    }

    /// <summary>The result screen reports the cards this clear fixed, not an empty tail.</summary>
    /// <remarks>
    /// The face the settlement seam answered, position for position. The bytes those positions
    /// come to are <c>DungeonClearCodecTests</c>'s to check; what is checked here is that the
    /// frame is handed the clear's own face rather than nothing, which is what it carried before
    /// cards existed.
    /// </remarks>
    [Fact]
    public async Task TheResultScreenReportsTheCardsThisClearFixed()
    {
        var cards = new RecordingCards();

        var sent = await Settle(cards: cards);

        var reward = sent.OfType<ClearDungeonRewardNotification>().Single();
        Assert.Equal(RecordingCards.FreeFace.Slots, reward.FreeCards);
    }

    /// <summary>
    /// The gold the settlement fixed reaches the card, walked back out of the frame itself.
    /// </summary>
    /// <remarks>
    /// <b>The seam the last two attempts at this feature both fell through.</b> A settled figure
    /// that never reaches the frame shows the player nothing while every criterion either side of
    /// the gap passes: the rule service is asked about a number, the encoder is handed a number,
    /// and nobody checks that they are the same number. So this reads the frame the way the client
    /// reads it and compares what comes out against what the settlement declared.
    /// </remarks>
    [Fact]
    public async Task TheGoldTheSettlementFixedReachesTheCard()
    {
        var sent = await Settle(cards: new RecordingCards());

        var reward = sent.OfType<ClearDungeonRewardNotification>().Single();
        var body = Assert.Single(new ClearDungeonRewardEncoder().Encode(reward))
            [GameServerFrameEncoder.HeaderLength..];
        var card = CardPanelFrameReader.ReadClearDungeonReward(body).FreeRow.Columns[0];

        Assert.Equal(RecordingCards.FaceGold, reward.CardGold);
        Assert.Equal(RecordingCards.FaceGold, card.Gold);
    }

    /// <summary>
    /// The result screen says what a paid flip of this clear costs, and says the settlement's own
    /// figure.
    /// </summary>
    /// <remarks>
    /// The price reaches the frame from the settlement rather than from the frame's own reading of
    /// anything: the archive is the rule service's to read, the settlement is what asks it, and
    /// this handler carries the answer across. A frame that still wrote the placeholder would
    /// report a flip this server gives away.
    /// </remarks>
    [Fact]
    public async Task TheResultScreenSaysWhatAPaidFlipCosts()
    {
        var sent = await Settle(cards: new RecordingCards());

        var reward = sent.OfType<ClearDungeonRewardNotification>().Single();
        Assert.Equal(RecordingCards.PaidFlipCost, reward.PaidFlipCost);
        Assert.NotEqual(DungeonClearPayloadCodec.NoPaidFlipCost, reward.PaidFlipCost);

        // The other two breakdowns this handler can send are the ones that report no experience:
        // a clear with no skill-point ledger to answer on, and one whose award did not happen.
        // What a flip costs has nothing to do with either, and both say so.
        foreach (var frames in new[]
                 {
                     await Settle(cards: new RecordingCards(), withLedger: false),
                     await Settle(cards: new RecordingCards(), awarded: false),
                 })
        {
            Assert.Equal(
                RecordingCards.PaidFlipCost,
                frames.OfType<ClearDungeonRewardNotification>().Single().PaidFlipCost);
        }
    }

    /// <summary>
    /// <b>A level the archive prices no flip at is not reported as a free flip.</b>
    /// </summary>
    /// <remarks>
    /// The decision this task had to make, held down. The archive answers nothing for a level its
    /// cost table has no row for, and nothing is not zero: zero is a price, and it is the price of
    /// a flip given away. So such a clear is reported with the frame's own marker for a flip this
    /// server could not price -- see <see cref="DungeonClearPayloadCodec.UnpricedPaidFlipCost"/>
    /// for why that value and not another -- and what this pins is that it is not the placeholder
    /// the field carried before there was anything to put in it.
    /// </remarks>
    [Fact]
    public async Task ALevelTheArchivePricesNoFlipAtIsNotReportedAsAFreeOne()
    {
        var sent = await Settle(cards: new RecordingCards(priced: false));

        var reward = sent.OfType<ClearDungeonRewardNotification>().Single();
        Assert.Equal(DungeonClearPayloadCodec.UnpricedPaidFlipCost, reward.PaidFlipCost);
        Assert.NotEqual(DungeonClearPayloadCodec.NoPaidFlipCost, reward.PaidFlipCost);
        Assert.NotEqual(0, reward.PaidFlipCost);
    }

    /// <summary>
    /// A run fixes one pair of faces however many times its result is reported.
    /// </summary>
    /// <remarks>
    /// Which is what puts the settlement behind the claim rather than in front of it. The seam is
    /// idempotent of its own accord, so this does not check that the player keeps the card they
    /// were shown -- it checks where the call sits: asked ahead of the claim it would be asked
    /// three times here, and a repeated report would be reaching the rules on every arrival.
    /// </remarks>
    [Fact]
    public async Task TheCardsAreFixedOnceHoweverManyTimesTheResultIsReported()
    {
        var cards = new RecordingCards();

        await Settle(cards: cards, reports: 3);

        Assert.Single(cards.Settlements);
    }

    /// <summary>
    /// The cards are fixed at the level the run was cleared at, not the one the clear bought.
    /// </summary>
    /// <remarks>
    /// The other end of where the call sits. A settlement asked after the award would be asked
    /// about level four here, because this clear crosses a threshold -- and the rate band and the
    /// grade band a card is drawn from would both be the ones the character only reached by
    /// finishing the run whose cards they are.
    /// </remarks>
    [Fact]
    public async Task TheCardsAreFixedAtTheLevelTheRunWasClearedAt()
    {
        var cards = new RecordingCards();

        await Settle(leveledUp: true, cards: cards, ledger: new SkillPointLedger { Progress = new CharacterProgressionState(2, 500) });

        var settlement = Assert.Single(cards.Settlements);
        Assert.Equal(2, settlement.CharacterLevel);
        Assert.Equal(new CharacterId(60), settlement.CharacterId);
        Assert.Equal(1, settlement.PartyMemberCount);
    }

    [Fact]
    public async Task Party_size_reaches_each_participants_card_settlement()
    {
        var cards = new RecordingCards();
        var leader = new DungeonRunParticipant(
            new AccountId(2), new CharacterId(61), 0, IsLeader: true);

        var sent = await Settle(cards: cards, leader: leader);

        Assert.Equal(2, cards.Settlements.Count);
        Assert.All(cards.Settlements, value => Assert.Equal(2, value.PartyMemberCount));
        Assert.Equal(new[] { new CharacterId(60), new CharacterId(61) },
            cards.Settlements.Select(value => value.CharacterId).OrderBy(value => value.Value));
        var board = Assert.Single(sent.OfType<ClearDungeonRewardNotification>()).PartyCards;
        Assert.NotNull(board);
        Assert.Equal(new byte[] { 0, 1 }, board.Select(value => value.Seat));
        Assert.All(board, value => Assert.Equal(RecordingCards.FreeFace.Slots, value.Cards));
    }

    [Fact]
    public async Task A_failed_durable_clear_award_releases_the_settlement_for_one_retry()
    {
        var bus = new FixedAward(false, failuresBeforeSuccess: 1);

        var sent = await Settle(
            bus: bus, reports: 2, continueAfterFailure: true);

        Assert.Equal(2, bus.TimesAsked);
        Assert.Single(sent.OfType<ClearDungeonRewardNotification>());
    }

    /// <summary>
    /// A clear that cannot be paid still fixes its cards and still reports them.
    /// </summary>
    /// <remarks>
    /// The refusal above is about experience: without a skill-point ledger there is no frame to
    /// report a new standing on. It is not about cards, and the panel the client goes on to ask
    /// for has nothing behind it unless this clear fixed some -- so the settlement sits ahead of
    /// that refusal, and this is what says so.
    /// </remarks>
    [Fact]
    public async Task AClearThatCannotBePaidStillFixesAndReportsItsCards()
    {
        var cards = new RecordingCards();

        var sent = await Settle(cards: cards, withLedger: false);

        Assert.Single(cards.Settlements);
        var reward = sent.OfType<ClearDungeonRewardNotification>().Single();
        Assert.Equal(0u, reward.ClearBase);
        Assert.Equal(RecordingCards.FreeFace.Slots, reward.FreeCards);
    }

    /// <summary>What the character held before the run.</summary>
    private const uint StartingExperience = 1000;

    private static async Task<IReadOnlyList<IGameServerMessage>> Settle(
        bool leveledUp = false,
        StubWorldContentEntryRuleService? content = null,
        bool awarded = true,
        FixedAward? bus = null,
        bool withLedger = true,
        SkillPointLedger? ledger = null,
        int reports = 1,
        RecordingCards? cards = null,
        DungeonRunPurpose purpose = default,
        bool canComplete = true,
        PurposeSettlementRules? rules = null,
        DungeonRunParticipant? leader = null,
        bool includeOnlyLeader = false,
        bool continueAfterFailure = false)
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(1),
            new CharacterId(60),
            new NetworkConnectionId("one"),
            connectionGeneration: 1,
            listenerPort: 10010);
        var entry = Entry();
        var runtime = new OneRuntimeSession(entry);
        var sender = new RecordingSender();

        var handler = new SetPlayResultHandler(
            new OneSession(session),
            runtime,
            new OnePlayingCharacter(ledger ?? new SkillPointLedger(withLedger)),
            new OneRun(purpose, leader, includeOnlyLeader),
            rules ?? new PurposeSettlementRules(canComplete),
            cards ?? new RecordingCards(),
            bus ?? new FixedAward(leveledUp, awarded),
            new QuestOfferRefreshDomainService(
                content ?? new StubWorldContentEntryRuleService(), new RecordingQuestLogs(), Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain()),
            sender,
            NullLogger<SetPlayResultHandler>.Instance);

        for (var report = 0; report < reports; report++)
        {
            try
            {
                await handler.HandleAsync(
                    new NetworkDispatchContext(
                        new NetworkConnectionContext(
                            new NetworkConnectionId("one"), NetworkEndpointKind.Game, 10010)),
                    new SetPlayResultRequest(default!, ClientRankPoint: 80),
                    CancellationToken.None);
            }
            catch (InvalidOperationException) when (continueAfterFailure && report + 1 < reports)
            {
                // The next report is the retry this fixture is explicitly exercising.
            }
        }

        return sender.Sent;
    }
    /// <summary>The published session this settlement starts from, at level three.</summary>
    /// <remarks>
    /// Shared with the three other slices that send an experience frame. It used to be built here
    /// with its skill projection left unset, so a settlement that went back to reading the session
    /// failed on a null rather than on a wrong figure -- caught, but for a reason that would stop
    /// holding the day the fixture happened to carry one. The shared builder fills it with a figure
    /// the ledger never gives out instead, so the revert is caught for what it actually is.
    /// </remarks>
    private static SelectCharacterResult Entry() =>
        ExperienceFrameEntry.At(3, StartingExperience);

    /// <summary>A bus that answers the clear command with a fixed award.</summary>
    /// <remarks>
    /// Fixed rather than computed, because what the clear is worth is checked against the archive
    /// next door. What is checked here is what the handler does with the answer.
    /// </remarks>
    private sealed class FixedAward(
        bool leveledUp,
        bool awarded = true,
        bool clamped = false,
        int failuresBeforeSuccess = 0)
        : ICommandBus
    {
        private int remainingFailures = failuresBeforeSuccess;
        /// <summary>Whether the settlement got as far as pricing the run.</summary>
        public bool WasAsked => this.TimesAsked > 0;

        /// <summary>How many times it did, which a replayed report would raise.</summary>
        public int TimesAsked { get; private set; }

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Assert.IsType<GrantClearExperienceCommand>(command);
            this.TimesAsked++;
            if (this.remainingFailures-- > 0)
                throw new InvalidOperationException("injected durable clear failure");
            // Clamped is the shape the grant returns when the ceiling took part of the award:
            // what landed, reported whole, with no parts to attribute it to.
            var award = clamped
                ? new ExperienceAward(
                    Base: ClampedLanding,
                    Contributions: [],
                    Gained: ClampedLanding,
                    Level: leveledUp ? (byte)4 : (byte)3,
                    Total: StartingExperience + ClampedLanding,
                    LeveledUp: leveledUp)
                : new ExperienceAward(
                    Base: ClearBase,
                    Contributions: [new ExperienceContribution("clear rank", 0.12, RankBonus)],
                    Gained: ClearBase + RankBonus,
                    Level: leveledUp ? (byte)4 : (byte)3,
                    Total: StartingExperience + ClearBase + RankBonus,
                    LeveledUp: leveledUp);
            // The two figures stand even when nothing was awarded: they are what the clear was
            // worth, and the handler is what has to decide they may not be reported.
            var result = new ClearExperienceResult(
                awarded ? award : null, ClearBase, RankBonus);
            return ValueTask.FromResult((TResult)(object)result);
        }
    }


    /// <summary>
    /// A settlement seam that fixes one known pair of faces and remembers every asking.
    /// </summary>
    /// <remarks>
    /// The face it answers is not the empty one, and the position holding equipment is not the
    /// first: a handler that reported the wrong face, or none, or a face of its own, all differ
    /// from this one. What a real face comes to is checked where the rules are.
    /// </remarks>
    /// <param name="priced">
    /// Whether the archive prices a flip at this clear's level. False is the settlement answering
    /// nothing, which is what a level outside the archive's cost table comes back as.
    /// </param>
    private sealed class RecordingCards(bool priced = true) : ICardRewardDomainService
    {
        /// <summary>
        /// What this seam says a paid flip of its clear costs.
        /// </summary>
        /// <remarks>
        /// Not a price the archive gives -- what the archive gives is asserted where the archive
        /// is read -- and not zero either, so a frame that dropped the price on the floor cannot
        /// pass by writing the placeholder it used to write.
        /// </remarks>
        public const int PaidFlipCost = 4_210;

        /// <summary>Every settlement this seam was asked for, in the order it was asked.</summary>
        public List<ClearRewardSettlement> Settlements { get; } = [];

        /// <summary>What the hand-written faces say a flip of them pays.</summary>
        public const int FaceGold = 37;

        /// <summary>The free face it fixes: four positions, the second of them holding a piece.</summary>
        public static readonly ClearRewardCardFace FreeFace = new(
            ClearRewardCardKind.Free,
            [
                new ClearRewardCardSlot(0, null),
                new ClearRewardCardSlot(
                    1, new ClearRewardCardItem(52001, 4, DropRarity.Common, Durability: 100)),
                new ClearRewardCardSlot(2, null),
                new ClearRewardCardSlot(3, null),
            ],
            FaceGold);

        /// <summary>
        /// What a paid face pays: nothing, which is what production settles for it.
        /// </summary>
        /// <remarks>
        /// <b>Different from the free face's, and that is the whole point of the figure.</b> The
        /// floor exists so that turning a <i>free</i> card over is never silent; a paid card was
        /// already bought at the archive's price. Two faces carrying the same number would let the
        /// frame read either one and look right, which is how this seam passed while writing the
        /// wrong face's gold.
        /// </remarks>
        public const int PaidFaceGold = 0;

        /// <summary>The paid face, deliberately unlike the free one.</summary>
        private static readonly ClearRewardCardFace PaidFace = new(
            ClearRewardCardKind.Paid,
            [
                new ClearRewardCardSlot(
                    0, new ClearRewardCardItem(52002, 9, DropRarity.Rare, Durability: 100)),
                new ClearRewardCardSlot(1, null),
                new ClearRewardCardSlot(2, null),
                new ClearRewardCardSlot(3, null),
            ],
            PaidFaceGold);

        public ValueTask<ClearRewardSettlementResult?> SettleAsync(
            ClearRewardSettlement settlement, CancellationToken cancellationToken)
        {
            this.Settlements.Add(settlement);
            return ValueTask.FromResult<ClearRewardSettlementResult?>(
                new ClearRewardSettlementResult(
                    new ClearRewardCardSet(
                        FreeFace,
                        PaidFace,
                        ImmutableHashSet<ClearRewardCardId>.Empty,
                        priced ? PaidFlipCost : null),
                    priced ? PaidFlipCost : null));
        }

        public ValueTask<ClearRewardCardSlot?> ClaimAsync(
            CharacterId characterId,
            ClearRewardCardKind kind,
            int index,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("A settlement fixes cards; it turns none over.");
    }

    private sealed class OneRun(
        DungeonRunPurpose purpose,
        DungeonRunParticipant? leader = null,
        bool includeOnlyLeader = false) : IDungeonRunDomainService
    {
        private bool _settled;
        private bool _settlementClaimed;

        /// <summary>The run, claimed once, exactly as the real service claims it.</summary>
        public DungeonRunState? TrySettle(CharacterId characterId)
        {
            if (this._settled)
                return null;

            this._settled = true;
            return this.Find(characterId);
        }

        public DungeonSettlementClaim? TryClaimSettlement(CharacterId characterId)
        {
            if (this._settled || this._settlementClaimed || this.Find(characterId) is not { } run)
                return null;
            this._settlementClaimed = true;
            return new DungeonSettlementClaim(characterId, run, 1, run.RunId);
        }

        public DungeonRunState? CommitSettlement(DungeonSettlementClaim claim)
        {
            if (!this._settlementClaimed || this._settled)
                return null;
            this._settlementClaimed = false;
            this._settled = true;
            return this.Find(claim.CharacterId);
        }

        public bool ReleaseSettlement(DungeonSettlementClaim claim)
        {
            if (!this._settlementClaimed)
                return false;
            this._settlementClaimed = false;
            return true;
        }

        private readonly DungeonLayout _layout = new(
            DungeonId: 144,
            MazeIndex: 0,
            Width: 1,
            Height: 1,
            StartCell: new MazeCell(0, 0),
            BossCell: new MazeCell(0, 0),
            StartRoomMapId: 1,
            Seed: 0,
            Rooms: new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 1 },
            BasisLevel: 5,
            ExperienceWeight: 1.45,
            Purpose: purpose);

        public DungeonRunState? Find(CharacterId characterId) => new(
            this._layout,
            new MazeCell(0, 0),
            CurrentRoomMapId: 1,
            VisitedCells: new HashSet<MazeCell>(),
            StartedAt: DateTimeOffset.UnixEpoch,
            Participant: leader is null
                ? null
                : new DungeonRunParticipant(
                    new AccountId(1), characterId, 1, false),
            Participants: leader is null
                ? null
                : includeOnlyLeader
                    ? [leader]
                :
                [
                    leader,
                    new DungeonRunParticipant(
                        new AccountId(1), characterId, 1, false),
                ],
            CurrentCellState: DungeonCellSeed.CompleteEmpty(
                new MazeCell(0, 0), 1).Snapshot());

        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, DungeonDifficulty difficulty) =>
            throw new NotSupportedException("This fixture only answers.");

        /// <summary>Not exercised here: nothing in these tests kills anything.</summary>
        public IReadOnlyList<MonsterDrop> AllocateMonsterDrops(
            CharacterId characterId,
            int declaredDropCount,
            ushort actorSequence,
            IReadOnlyList<RolledDrop> rolled) => [];

        public bool End(CharacterId characterId) =>
            throw new NotSupportedException("This fixture only answers.");

        public DungeonRunState? TryMove(CharacterId characterId, MazeCell target) =>
            throw new NotSupportedException("This fixture only answers.");

        public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence) =>
            throw new NotSupportedException("This fixture only answers.");

        public DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort groundDropNumber) =>
            throw new NotSupportedException("This fixture only answers.");

        public DungeonRunState? FixClearRewardCards(
            CharacterId characterId,
            ClearRewardCardFace free,
            ClearRewardCardFace paid,
            int? paidFlipCost) =>
            throw new NotSupportedException("This fixture only answers.");

        public ClearRewardCardSlot? TryClaimClearRewardCard(
            CharacterId characterId, ClearRewardCardKind kind, int index) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class PurposeSettlementRules(bool canComplete) : IDungeonSettlementRuleService
    {
        public AccountId? AccountId { get; private set; }

        public CharacterId? CharacterId { get; private set; }

        public ValueTask<DungeonSettlementRules> ForAsync(
            AccountId accountId,
            CharacterId characterId,
            DungeonRunState run,
            CancellationToken cancellationToken)
        {
            this.AccountId = accountId;
            this.CharacterId = characterId;
            return ValueTask.FromResult(
                new DungeonSettlementRules(canComplete, run.Layout.Purpose.IsQuestLinked));
        }
    }
}
