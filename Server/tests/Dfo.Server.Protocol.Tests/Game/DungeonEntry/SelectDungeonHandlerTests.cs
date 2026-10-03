using Dfo.Server.Protocol.Game.Fatigue;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Protocol.Tests.Game.TownProjection;
using Microsoft.Extensions.Logging.Abstractions;
using EnteredDungeon = Dfo.Server.Abstractions.World.DungeonEntry;
using QuestTaken = Dfo.Server.Abstractions.Quests.QuestAcceptance;

namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;

/// <summary>
/// Which quest state the maze is chosen against.
/// </summary>
/// <remarks>
/// The facts this handler gathers decide the layout, and there are two places they could come
/// from: the character's own quest log, and the copy the entry batch left in the published
/// session. Nothing refreshes that copy, so a handler reading it would not throw and would not
/// crash -- it would quietly choose the maze for the quests the character held when they entered
/// the world, however many they have taken on or handed in since.
///
/// That is the mistake this test exists to catch, which is why the log here answers with quests
/// the session's copy does not mention and the session's copy names one the log does not. Reading
/// the wrong one produces a recognisably wrong set rather than coinciding with the right one.
/// </remarks>
public sealed class SelectDungeonHandlerTests
{
    /// <summary>
    /// The facts handed to the run are the log's carried quests, not the session's copy.
    /// </summary>
    [Fact]
    public async Task Entry_asks_the_quest_log_which_quests_are_carried()
    {
        var visits = new RecordingVisits();
        var handler = Handler(visits);

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        var facts = Assert.Single(visits.Facts);
        Assert.Equal([CarriedInLog, AlsoCarriedInLog], facts.ActiveQuestIds);
    }

    /// <summary>
    /// The cleared list comes from the log too, which is where the difference shows plainest: the
    /// session's copy has never had a quest in it.
    /// </summary>
    [Fact]
    public async Task Entry_asks_the_quest_log_which_quests_are_cleared()
    {
        var visits = new RecordingVisits();
        var handler = Handler(visits);

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        var facts = Assert.Single(visits.Facts);
        Assert.Equal([ClearedInLog], facts.ClearedQuestIds);
    }

    [Fact]
    public async Task DirectedSelectionContextIsPassedToTheEntryRule()
    {
        var visits = new RecordingVisits();
        var handler = Handler(
            visits,
            intent: DungeonEntryIntent.Directed(TutorialDungeonId));

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        Assert.Equal(
            DungeonEntryIntent.Directed(TutorialDungeonId),
            Assert.Single(visits.Facts).Intent);
    }

    [Fact]
    public async Task MissingSelectionContextIsRefusedWithoutReadingQuestState()
    {
        var visits = new RecordingVisits();
        var sender = new RecordingSender();
        var handler = new SelectDungeonHandler(
            Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain(),
            new UnchangedState(),
            new OneSession(Identity),
            new RuntimeCurrentView(new OneRuntimeSession(Entry())),
            new NoPartyService(),
            new NoopRuns(),
            new DungeonRunDeliveryRegistry(),
            new IntentStore(null),
            visits,
            new OneLog(new UnaskedLog()),
            new RecordingFatigue(),
            sender,
            new NoopConnectionCloser(),
            TownAreaProjectionTestFactory.Create(
                new OneSession(Identity),
                new OneRuntimeSession(Entry()),
                sender),
            NullLogger<SelectDungeonHandler>.Instance,
            new SelectedSessionTransitionGate());

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        Assert.Empty(visits.Facts);
        var refusal = Assert.IsType<SelectDungeonRefusal>(Assert.Single(sender.Sent));
        Assert.Equal(SelectDungeonRefusal.InvalidSelectionState, refusal.ErrorCode);
    }

    [Fact]
    public async Task DirectedSelectionForAnotherDungeonIsRefused()
    {
        var visits = new RecordingVisits();
        var sender = new RecordingSender();
        var handler = Handler(
            visits,
            sender,
            intent: DungeonEntryIntent.Directed(TutorialDungeonId + 1));

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        Assert.Empty(visits.Facts);
        var refusal = Assert.IsType<SelectDungeonRefusal>(Assert.Single(sender.Sent));
        Assert.Equal(SelectDungeonRefusal.InvalidSelectionState, refusal.ErrorCode);
    }

    /// <summary>
    /// The difficulty reaches the run and the entry projection now supplies the complete avatar,
    /// user-state and host prelude before START_MAP.
    /// </summary>
    /// <remarks>
    /// The character is level sixty, so the selection state is not deferred to this frame.
    /// </remarks>
    [Fact]
    public async Task The_answer_to_an_entry_is_what_it_was()
    {
        var visits = new RecordingVisits();
        var sender = new RecordingSender();
        var handler = Handler(visits, sender);

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        Assert.Equal(Difficulty, Assert.Single(visits.Facts).Difficulty);
        Assert.Equal(TutorialDungeonId, Assert.Single(visits.Dungeons));
        Assert.Collection(
            sender.Sent,
            frame => Assert.IsType<ExtendedUserStateMessage>(frame),
            frame => Assert.IsType<EnterSelectDungeonAcknowledgement>(frame),
            frame => Assert.IsType<UserStateNotification>(frame),
            frame => Assert.IsType<UdpHostNotification>(frame),
            frame => Assert.IsType<SupportTagNotification>(frame),
            frame => Assert.IsType<DungeonInfoNotification>(frame),
            frame => Assert.Null(Assert.IsType<StartMapNotification>(frame).Run.Participant),
            frame => Assert.Equal((ushort)1, Assert.IsType<FatigueNotification>(frame).Balance.Used));
    }

    /// <summary>
    /// A character with an empty log enters with empty facts rather than with the session's copy.
    /// </summary>
    /// <remarks>
    /// The "nothing to find" case. Asking the log has no separate answer for it -- a character
    /// with no quest rows has an empty log rather than none -- so what has to be shown is that
    /// empty does not fall back to the stale copy, which does hold a quest.
    /// </remarks>
    [Fact]
    public async Task An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy()
    {
        var visits = new RecordingVisits();
        var handler = Handler(visits, new RecordingSender(), new OneLog(new StandInLog([], [])));

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        var facts = Assert.Single(visits.Facts);
        Assert.Empty(facts.ActiveQuestIds);
        Assert.Empty(facts.ClearedQuestIds);
    }

    /// <summary>Projection must not charge an entry already committed by the visit domain.</summary>
    [Fact]
    public async Task Entry_projection_does_not_charge_again()
    {
        var fatigue = new RecordingFatigue();
        var handler = Handler(new RecordingVisits(), fatigue: fatigue);

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        // The visit domain owns charging; projection must never charge it again.
        Assert.Empty(fatigue.Spent);
    }

    /// <summary>A refused selection reports nothing to the fatigue seam.</summary>
    /// <remarks>
    /// The other half of <see cref="Entry_projection_does_not_charge_again"/>. That one
    /// says a report happens; this one says where, and it is the half a call site slides past
    /// unnoticed: reported before the run has answered rather than after, a selection the archive
    /// refuses costs the character a point for a dungeon they never stood in. Nothing else in the
    /// handler changes shape when it moves -- the refusal frame is the same frame either way.
    /// <para>
    /// Which is why the refusal is asserted alongside the empty report list. Without it an empty
    /// list would equally mean the handler stopped before reaching this branch at all, and the
    /// test would go on passing for a reason that has nothing to do with what it is named for.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_refused_selection_reports_nothing_to_the_fatigue_seam()
    {
        var fatigue = new RecordingFatigue();
        var sender = new RecordingSender();
        var sessions = new OneSession(Identity);
        var runtime = new OneRuntimeSession(Entry());
        var areas = new SessionPresenceDomainService();
        areas.Admit(new SessionPresence(
            Identity.CharacterId,
            Identity.ListenerPort,
            3,
            6,
            true,
            Identity,
            true,
            11));
        var townProjection = new TownAreaProjectionDelivery(
            areas,
            sessions,
            runtime,
            new NoopNotifications(),
            new NoopPartyTownProjection(),
            sender,
            SharedTownMovementDomainService.Instance);
        var handler = new SelectDungeonHandler(Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain(),
            new UnchangedState(),
            sessions,
            new RuntimeCurrentView(runtime),
            new NoPartyService(),
            new NoopRuns(),
            new DungeonRunDeliveryRegistry(),
            new IntentStore(DungeonEntryIntent.Interactive),
            new RefusingVisits(),
            new OneLog(new StandInLog([], [])),
            fatigue,
            sender,
            new NoopConnectionCloser(),
            townProjection,
            NullLogger<SelectDungeonHandler>.Instance,
            new SelectedSessionTransitionGate());

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        var refusal = Assert.IsType<SelectDungeonRefusal>(Assert.Single(sender.Sent));
        Assert.Equal(SelectDungeonRefusal.DungeonNotFound, refusal.ErrorCode);
        Assert.Empty(fatigue.Spent);
        Assert.True(Identity.Owns(
            ((ITownAreaDomainService)areas).Find(Identity.CharacterId)!));
    }

    [Fact]
    public async Task Missing_progress_rolls_back_before_leaving_the_town_area()
    {
        var sessions = new OneSession(Identity);
        var runtime = new OneRuntimeSession(Entry());
        var areas = new SessionPresenceDomainService();
        areas.Admit(new SessionPresence(
            Identity.CharacterId,
            Identity.ListenerPort,
            3,
            6,
            true,
            Identity,
            true,
            11));
        var sender = new RecordingSender();
        var townProjection = new TownAreaProjectionDelivery(
            areas,
            sessions,
            runtime,
            new NoopNotifications(),
            new NoopPartyTownProjection(),
            sender,
            SharedTownMovementDomainService.Instance);
        var character = new Dfo.Server.Application.Tests.Support.CurrentCharacterStub
        {
            Id = Identity.CharacterId,
            Progress = null,
        };
        var handler = new SelectDungeonHandler(
            Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain(character),
            new UnchangedState(),
            sessions,
            new RuntimeCurrentView(runtime),
            new NoPartyService(),
            new NoopRuns(),
            new DungeonRunDeliveryRegistry(),
            new IntentStore(DungeonEntryIntent.Interactive),
            new RecordingVisits(),
            new OneLog(new StandInLog([], [])),
            new RecordingFatigue(),
            sender,
            new NoopConnectionCloser(),
            townProjection,
            NullLogger<SelectDungeonHandler>.Instance,
            new SelectedSessionTransitionGate());

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        Assert.True(Identity.Owns(
            ((ITownAreaDomainService)areas).Find(Identity.CharacterId)!));
    }

    /// <summary>The guard in front of the read reports nothing to the fatigue seam either.</summary>
    /// <remarks>
    /// The refusal above is one of the two ways this frame ends without an entry; this is the
    /// other, and it is not covered by the same assertion because it leaves by a different
    /// return. A report put here would charge a character for a frame that was answered before
    /// anything was even looked up.
    /// <para>
    /// Kept apart from <see cref="Without_a_published_session_nothing_is_entered"/>, which holds
    /// no reference to the seam it hands the handler and so says nothing about it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Without_a_published_session_nothing_is_reported_to_the_fatigue_seam()
    {
        var visits = new RecordingVisits();
        var fatigue = new RecordingFatigue();
        var handler = new SelectDungeonHandler(Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain(),
            new UnchangedState(),
            new NoSession(),
            new RuntimeCurrentView(new OneRuntimeSession(Entry())),
            new NoPartyService(),
            new NoopRuns(),
            new DungeonRunDeliveryRegistry(),
            new IntentStore(DungeonEntryIntent.Interactive),
            visits,
            new OneLog(new UnaskedLog()),
            fatigue,
            new RecordingSender(),
            new NoopConnectionCloser(),
            TownAreaProjectionTestFactory.Create(
                new NoSession(),
                new OneRuntimeSession(Entry()),
                new RecordingSender()),
            NullLogger<SelectDungeonHandler>.Instance,
            new SelectedSessionTransitionGate());

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        Assert.Empty(visits.Facts);
        Assert.Empty(fatigue.Spent);
    }

    /// <summary>Without a published session nothing is entered and nothing is sent.</summary>
    /// <remarks>
    /// The guard in front of the read, kept as it was. It answers before the log is asked, which
    /// is why the log here refuses to answer at all.
    /// </remarks>
    [Fact]
    public async Task Without_a_published_session_nothing_is_entered()
    {
        var visits = new RecordingVisits();
        var sender = new RecordingSender();
        var handler = new SelectDungeonHandler(Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain(),
            new UnchangedState(),
            new NoSession(),
            new RuntimeCurrentView(new OneRuntimeSession(Entry())),
            new NoPartyService(),
            new NoopRuns(),
            new DungeonRunDeliveryRegistry(),
            new IntentStore(DungeonEntryIntent.Interactive),
            visits,
            new OneLog(new UnaskedLog()),
            new RecordingFatigue(),
            sender,
            new NoopConnectionCloser(),
            TownAreaProjectionTestFactory.Create(
                new NoSession(),
                new OneRuntimeSession(Entry()),
                sender),
            NullLogger<SelectDungeonHandler>.Instance,
            new SelectedSessionTransitionGate());

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        Assert.Empty(visits.Facts);
        Assert.Empty(sender.Sent);
    }

    /// <summary>The quests the log says the character is carrying, in slot order.</summary>
    private const int CarriedInLog = 910;

    private const int AlsoCarriedInLog = 911;

    /// <summary>The quest the log says the character has finished.</summary>
    private const int ClearedInLog = 920;

    /// <summary>
    /// The quest the session's copy carries, which the log does not mention.
    /// </summary>
    /// <remarks>
    /// The number the entry batch's fixture puts in the published snapshot. Named here so that a
    /// reading of the wrong source is a visibly wrong number rather than an empty list.
    /// </remarks>
    private const int CarriedInTheSessionsCopy = 55;

    private const ushort TutorialDungeonId = 10000;

    private const DungeonDifficulty Difficulty = (DungeonDifficulty)3;

    private static SelectDungeonHandler Handler(
        RecordingVisits visits,
        RecordingSender? sender = null,
        OneLog? logs = null,
        RecordingFatigue? fatigue = null,
        DungeonEntryIntent? intent = null)
    {
        var actualSender = sender ?? new RecordingSender();
        var sessions = new OneSession(Identity);
        var runtimes = new OneRuntimeSession(Entry());
        return new(
            Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain(),
            new UnchangedState(),
            sessions,
            new RuntimeCurrentView(runtimes),
            new NoPartyService(),
            new NoopRuns(),
            new DungeonRunDeliveryRegistry(),
            new IntentStore(intent ?? DungeonEntryIntent.Interactive),
            visits,
            logs ?? new OneLog(new StandInLog(
                [
                    new CarriedQuest(0, CarriedInLog, 4),
                    new CarriedQuest(1, AlsoCarriedInLog, 0),
                ],
                [new ClearedQuest(ClearedInLog, IQuestLog.ClearedMark)])),
            fatigue ?? new RecordingFatigue(),
            actualSender,
            new NoopConnectionCloser(),
            TownAreaProjectionTestFactory.Create(sessions, runtimes, actualSender),
            NullLogger<SelectDungeonHandler>.Instance,
            new SelectedSessionTransitionGate());
    }

    private static SelectDungeonRequest Request() =>
        new(GameMessageMetadata.Empty, TutorialDungeonId, Difficulty, 0, 0);

    private sealed class NoopNotifications : INotificationBus
    {
        public ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification> =>
            ValueTask.FromResult(new NotificationDelivery([]));
    }

    private sealed class NoopPartyTownProjection : IPartyTownProjectionDelivery
    {
        public ValueTask RefreshObservedPartiesAsync(
            IReadOnlyList<SelectedSessionIdentity> recipients,
            IReadOnlyList<SelectedSessionIdentity> representedSessions,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RefreshMovedPartyAsync(
            SelectedSessionIdentity mover,
            PartyDirectoryScope? previousScope,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    /// <summary>
    /// A published session whose quest copy is the one entry took, and stale.
    /// </summary>
    private static SelectCharacterResult Entry()
    {
        var result = SelectCharacterPayloadTestData.Create();
        Assert.Equal(
            CarriedInTheSessionsCopy,
            Assert.Single(result.CharacterEntry.Quests.ActiveQuests).QuestId);
        Assert.Empty(result.CharacterEntry.Quests.ClearedQuestIds);
        return result;
    }

    private static NetworkDispatchContext Dispatch() =>
        new(new NetworkConnectionContext(
            new NetworkConnectionId("one"), NetworkEndpointKind.Game, 10010));

    private static SelectedSessionIdentity Identity { get; } = SelectedSessionIdentity.Create(
        new AccountId(1),
        new CharacterId(0x1234),
        new NetworkConnectionId("one"),
        connectionGeneration: 1,
        listenerPort: 10010);

    /// <summary>The maze the run under test is given, which these tests do not vary.</summary>
    private static readonly DungeonLayout Tutorial = new(
        DungeonId: TutorialDungeonId,
        MazeIndex: 0,
        Width: 5,
        Height: 3,
        StartCell: new MazeCell(0, 1),
        BossCell: new MazeCell(3, 0),
        StartRoomMapId: 61000,
        Seed: 0x0BADF00D,
        Rooms: new Dictionary<MazeCell, int> { [new MazeCell(0, 1)] = 61000 });

    /// <summary>The run as it stands on entry: in the start room, having seen only it.</summary>
    private static readonly DungeonRunState AtStart = new(
        Tutorial,
        Tutorial.StartCell,
        Tutorial.StartRoomMapId,
        new HashSet<MazeCell> { Tutorial.StartCell },
        DateTimeOffset.UnixEpoch,
        Difficulty,
        RunId: DungeonRunId.New(),
        CurrentCellState: DungeonCellSeed.CompleteEmpty(
            Tutorial.StartCell, Tutorial.StartRoomMapId).Snapshot());

    /// <summary>
    /// The run, recording the facts it was handed and answering with a fixed room.
    /// </summary>
    /// <remarks>
    /// It answers rather than refusing, so that the whole handler runs and the frames it sends can
    /// be checked alongside the facts. The facts are what these tests are about; the room is a
    /// constant so that nothing else varies with them.
    /// </remarks>
    private sealed class RecordingVisits : IDungeonVisitDomainService
    {
        public List<DungeonEntryFacts> Facts { get; } = [];

        public List<int> Dungeons { get; } = [];

        public bool DefersSelectionState(byte characterLevel, bool tutorialCompleted = false) => false;

        public ValueTask<EnteredDungeon?> EnterAsync(
            CharacterId characterId,
            int dungeonId,
            DungeonDifficulty difficulty,
            DungeonEntryFacts facts,
            CancellationToken cancellationToken)
        {
            this.Dungeons.Add(dungeonId);
            this.Facts.Add(facts);
            return ValueTask.FromResult<EnteredDungeon?>(
                new EnteredDungeon(Tutorial, new DungeonRoomVisit(AtStart, true, [], [], 1)));
        }

        public ValueTask<DungeonRoomVisit?> MoveAsync(
            CharacterId characterId,
            DungeonRunState current,
            MazeCell target,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon walks into no adjoining room.");
    }

    /// <summary>
    /// The run that declares the dungeon unenterable, which is the handler's only refusal.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RecordingVisits"/> rather than a flag on it: every other test in
    /// this file is about an entry that happens, and a fixture that could answer either way would
    /// have to be told which every time it is built.
    /// </remarks>
    private sealed class RefusingVisits : IDungeonVisitDomainService
    {
        public bool DefersSelectionState(byte characterLevel, bool tutorialCompleted = false) => false;

        public ValueTask<EnteredDungeon?> EnterAsync(
            CharacterId characterId,
            int dungeonId,
            DungeonDifficulty difficulty,
            DungeonEntryFacts facts,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<EnteredDungeon?>(null);

        public ValueTask<DungeonRoomVisit?> MoveAsync(
            CharacterId characterId,
            DungeonRunState current,
            MazeCell target,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("A refused selection walks into no room.");
    }

    /// <summary>Records accidental double charging by the projection layer.</summary>
    private sealed class RecordingFatigue : ICharacterFatigueDomainService
    {
        public List<(CharacterId CharacterId, ushort Points)> Spent { get; } = [];

        public ValueTask SpendAsync(
            CharacterId characterId, ushort points, CancellationToken cancellationToken)
        {
            this.Spent.Add((characterId, points));
            return ValueTask.CompletedTask;
        }

        public ValueTask<CharacterFatigueBalance> BalanceOfAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CharacterFatigueBalance(188, 1));

        public ValueTask RefundAsync(
            CharacterId characterId, ushort points, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon gives no fatigue back.");
    }

    private sealed class OneLog(IQuestLog log) : ICharacterQuestLogDomainService
    {
        public IQuestLog LogOf(AccountId accountId, CharacterId characterId)
        {
            Assert.Equal(Identity.AccountId, accountId);
            Assert.Equal(Identity.CharacterId, characterId);
            return log;
        }
    }

    private sealed class IntentStore(DungeonEntryIntent? intent) : IDungeonEntryIntentDomainService
    {
        private DungeonEntryIntent? _intent = intent;

        public void Record(SelectedSessionIdentity session, DungeonEntryIntent value) => _intent = value;

        public DungeonEntryIntent? Consume(SelectedSessionIdentity session)
        {
            var current = _intent;
            _intent = null;
            return current;
        }

        public void RestoreIfAbsent(SelectedSessionIdentity session, DungeonEntryIntent intent) => _intent ??= intent;

        public bool ClearExact(SelectedSessionIdentity session)
        {
            var found = _intent is not null;
            _intent = null;
            return found;
        }
    }

    /// <summary>A log holding exactly what it was built with.</summary>
    /// <remarks>
    /// Only the two reads are answered. Entering a dungeon changes no quest, so a verb reached
    /// from here would be a defect this fixture should report rather than absorb.
    /// </remarks>
    private class StandInLog(
        IReadOnlyList<CarriedQuest> carried, IReadOnlyList<ClearedQuest> cleared) : IQuestLog
    {
        public CharacterId CharacterId => Identity.CharacterId;

        public virtual ValueTask<IReadOnlyList<CarriedQuest>> CarriedAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(carried);

        public virtual ValueTask<IReadOnlyList<ClearedQuest>> ClearedAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(cleared);

        public ValueTask<CarriedQuest?> FindCarriedAsync(
            int questId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon asks for the whole list.");

        public ValueTask<IReadOnlyList<int>> TrackedAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon reads no tracking selection.");

        public ValueTask<QuestTaken> AcceptAsync(
            QuestDefinition definition, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon takes no quest on.");

        public ValueTask<bool> RecordProgressAsync(
            CarriedQuest asLastSeen, int triggerValue, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon moves no quest.");

        public ValueTask<QuestAdvance> AdvanceAsync(
            QuestDefinition definition,
            byte selector,
            bool increment,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon moves no quest.");

        public ValueTask<bool> CompleteAsync(
            int questId, byte mark, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon finishes no quest.");

        public ValueTask<IReadOnlyList<int>> CompleteWithoutRewardsAsync(
            IReadOnlyCollection<int> questIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon finishes no quest.");

        public ValueTask<bool> GiveUpAsync(int questId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon gives no quest up.");

        public ValueTask TrackAsync(
            IReadOnlyList<int> questIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon changes no tracking selection.");
    }

    /// <summary>A log that must not be asked anything at all.</summary>
    private sealed class UnaskedLog() : StandInLog([], [])
    {
        public override ValueTask<IReadOnlyList<CarriedQuest>> CarriedAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The guard answers before the log is asked.");

        public override ValueTask<IReadOnlyList<ClearedQuest>> ClearedAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The guard answers before the log is asked.");
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
            throw new NotSupportedException("Entering a dungeon rewrites no session.");

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
            throw new InvalidOperationException("Entering a dungeon answers as one delivery.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Entering a dungeon answers as one delivery.");

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Sent.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopConnectionCloser : INetworkConnectionCloser
    {
        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken ct) => RequestCloseAsync(expected.ConnectionId, ct);
        public ValueTask RequestCloseAsync(
            NetworkConnectionId connectionId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class NoPartyService : IPartyService
    {
        public PartyMembershipSnapshot? FindByKey(PartyKey key) => null;
        public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope) { }
        public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => [];
        public PartySnapshot FindParty(PartyCharacterSnapshot character) =>
            PartySnapshot.Virtual(character, new PartySettings(new byte[PartySettings.WireLength]));
        public PartyOperationResult CreateOrUpdateSettings(PartyCharacterSnapshot character, PartySettings settings, PartyTitle title) => throw new NotSupportedException();
        public PartyOperationResult RequestInvite(PartyCharacterSnapshot inviter, PartyCharacterSnapshot target, PartyInviteDisplayState display) => throw new NotSupportedException();
        public PartyOperationResult AcceptInvite(PartyCharacterSnapshot invitee, PartyCharacterSnapshot inviter) => throw new NotSupportedException();
        public PartyOperationResult TransferLeadership(CharacterId currentLeaderCharacterId, CharacterId nextLeaderCharacterId) => throw new NotSupportedException();
        public PartyOperationResult RemoveMember(CharacterId currentLeaderCharacterId, CharacterId targetCharacterId) => throw new NotSupportedException();
        public PartyOperationResult LeaveParty(CharacterId characterId) => throw new NotSupportedException();
        public PartyOperationResult CleanupCharacter(CharacterId characterId) => throw new NotSupportedException();
    }

    private sealed class UnchangedState : ICharacterStateProjectionDomainService
    {
        public ValueTask<SelectCharacterResult> RefreshAsync(
            SelectCharacterResult result,
            CancellationToken cancellationToken) => ValueTask.FromResult(result);
    }

    private sealed class NoopRuns : IDungeonRunDomainService
    {
        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, DungeonDifficulty difficulty) => throw new NotSupportedException();
        public DungeonRunState? Find(CharacterId characterId) => null;
        public DungeonRunState? TryMove(CharacterId characterId, MazeCell target) => null;
        public DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort groundDropNumber) => null;
        public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence) => null;
        public DungeonRunState? TrySettle(CharacterId characterId) => null;
        public IReadOnlyList<MonsterDrop> AllocateMonsterDrops(CharacterId characterId, int declaredDropCount, ushort actorSequence, IReadOnlyList<RolledDrop> rolled) => [];
        public DungeonRunState? FixClearRewardCards(CharacterId characterId, ClearRewardCardFace free, ClearRewardCardFace paid, int? paidFlipCost) => null;
        public ClearRewardCardSlot? TryClaimClearRewardCard(CharacterId characterId, ClearRewardCardKind kind, int index) => null;
        public bool End(CharacterId characterId) => true;
    }
}
