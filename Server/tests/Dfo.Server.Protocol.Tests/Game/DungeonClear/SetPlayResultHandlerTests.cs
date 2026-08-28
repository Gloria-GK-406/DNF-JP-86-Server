using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Quests;
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
        var sent = await Settle(awarded: false);

        var reward = sent.OfType<ClearDungeonRewardNotification>().Single();
        Assert.Equal(0u, reward.ClearBase);
        Assert.Equal(0u, reward.RankBonus);

        // And the standing reported is the one the character actually holds.
        var experience = sent.OfType<ExperienceNotification>().Single();
        Assert.Equal(StartingExperience, experience.TotalExperience);
        Assert.Equal(3, experience.Level);
    }

    /// <summary>
    /// Without a skill-point mirror the clear is neither paid nor reported.
    /// </summary>
    /// <remarks>
    /// The same refusal a kill makes, for the same reason: the experience frame carries the mirror
    /// as well as the total, so without one there is no frame to report a new standing on. Paying
    /// regardless would credit the character, spend the run, and leave the client drawing the
    /// total it entered with.
    /// </remarks>
    [Fact]
    public async Task WithoutASkillMirrorTheClearIsNeitherPaidNorReported()
    {
        var bus = new FixedAward(leveledUp: false);
        var sent = await Settle(bus: bus, withMirror: false);

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

    /// <summary>What the character held before the run.</summary>
    private const uint StartingExperience = 1000;

    private static async Task<IReadOnlyList<IGameServerMessage>> Settle(
        bool leveledUp = false,
        StubWorldContentEntryRuleService? content = null,
        bool awarded = true,
        FixedAward? bus = null,
        bool withMirror = true,
        int reports = 1)
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(1),
            new CharacterId(60),
            new NetworkConnectionId("one"),
            connectionGeneration: 1,
            listenerPort: 10010);
        var entry = Entry(withMirror);
        var runtime = new OneRuntimeSession(entry);
        var sender = new RecordingSender();

        var handler = new SetPlayResultHandler(
            new OneSession(session),
            runtime,
            new OneRun(),
            bus ?? new FixedAward(leveledUp, awarded),
            new QuestOfferRefresher(
                content ?? new StubWorldContentEntryRuleService(), new RecordingQuestLogs()),
            sender,
            NullLogger<SetPlayResultHandler>.Instance);

        for (var report = 0; report < reports; report++)
        {
            await handler.HandleAsync(
                new NetworkDispatchContext(
                    new NetworkConnectionContext(
                        new NetworkConnectionId("one"), NetworkEndpointKind.Game, 10010)),
                new SetPlayResultRequest(default!, ClientRankPoint: 80),
                CancellationToken.None);
        }

        return sender.Sent;
    }

    /// <summary>An entry at level 3 holding <see cref="StartingExperience"/>.</summary>
    /// <remarks>
    /// Only the four members this handler reads are filled in: the character's standing, the
    /// stored total, the skill-point mirror the experience frame carries, and a subject for the
    /// offer list to be recomputed from. The rest of the entry stays as the shared builder leaves
    /// it, so the day this handler starts reading a fifth is a day this fixture fails loudly.
    /// </remarks>
    private static SelectCharacterResult Entry(bool withMirror = true)
    {
        var result = SelectCharacterPayloadTestData.Create();
        return result with
        {
            Character = result.Character with
            {
                Progression = new CharacterProgressionState(3, StartingExperience),
            },
            CharacterEntry = result.CharacterEntry with
            {
                Public = new CharacterPublicEntryState(
                    null!, null!, null!, null!, null!, null!,
                    new CharacterEntryExtrasState(
                        SkillTreeIndex: 0,
                        NameTagItemId: 0,
                        NameTagExpireTime: 0,
                        ManageLevel: 0,
                        CharacterExperience: StartingExperience,
                        ExtraEquipmentSlotState: 0,
                        Dimensions: [],
                        DimensionFlags: (0, 0, 0, 0),
                        SpecialRewardQuestIds: []),
                    null!,
                    VisibilityBits: 0,
                    CloneTitleItemId: 0),
                Skills = new CharacterSkillsEntryState(
                    Pages: [],
                    SkillPointSlots: [],
                    SkillPointBasis: null!,
                    DarkKnightCombos: null,
                    KnightShieldDeck: null,
                    ProtocolMirror: withMirror
                        ? new CharacterSkillProtocolMirrorState(11, 22, 33, 44)
                        : null),
            },
            World = result.World with
            {
                Subject = new WorldContentSubject(
                    3, Job: 0, GrowType: 0,
                    ClearedQuestFlags: new Dictionary<int, int>(),
                    CarriedQuestIds: [],
                    ClaimedDailyChallengeGroupIds: [],
                    CollectionBoxes: [],
                    EquippedCreatureItemId: 0,
                    EquippedCreatureLevel: 0),
            },
        };
    }

    /// <summary>A bus that answers the clear command with a fixed award.</summary>
    /// <remarks>
    /// Fixed rather than computed, because what the clear is worth is checked against the archive
    /// next door. What is checked here is what the handler does with the answer.
    /// </remarks>
    private sealed class FixedAward(bool leveledUp, bool awarded = true) : ICommandBus
    {
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
            var award = new ExperienceAward(
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


    private sealed class OneRun : IDungeonRunDomainService
    {
        private bool _settled;

        /// <summary>The run, claimed once, exactly as the real service claims it.</summary>
        public DungeonRunState? TrySettle(CharacterId characterId)
        {
            if (this._settled)
                return null;

            this._settled = true;
            return this.Find(characterId);
        }

        private static readonly DungeonLayout Layout = new(
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
            ExperienceWeight: 1.45);

        public DungeonRunState? Find(CharacterId characterId) => new(
            Layout,
            new MazeCell(0, 0),
            CurrentRoomMapId: 1,
            VisitedCells: new HashSet<MazeCell>(),
            TakenGroundDrops: new HashSet<GroundDropId>(),
            DeadActors: new HashSet<RoomActorId>(),
            StartedAt: DateTimeOffset.UnixEpoch);

        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, byte difficulty) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool End(CharacterId characterId) =>
            throw new NotSupportedException("This fixture only answers.");

        public DungeonRunState? TryMove(CharacterId characterId, MazeCell target) =>
            throw new NotSupportedException("This fixture only answers.");

        public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence) =>
            throw new NotSupportedException("This fixture only answers.");

        public DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort groundDropNumber) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class OneSession(SelectedSessionIdentity identity) : ISelectedSessionDirectory
    {
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => identity;

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => identity;

        public SelectedSessionIdentity? Find(AccountId accountId) => identity;

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => identity;

        public IReadOnlyList<SelectedSessionIdentity> Current() => [identity];

        public SelectedSessionIdentity? RegisterReplacing(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool TryRemoveExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class OneRuntimeSession(SelectCharacterResult entry)
        : ISelectedSessionRuntimeDirectory
    {
        private SelectCharacterResult _entry = entry;

        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            result = this._entry;
            shouldPersistPosition = false;
            return true;
        }

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            this._entry = result;

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) => this._entry;

        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result, bool persist) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool RemoveExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The result screen's frames are sent explicitly.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The result screen's frames are sent explicitly.");

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Sent.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
    }
}
