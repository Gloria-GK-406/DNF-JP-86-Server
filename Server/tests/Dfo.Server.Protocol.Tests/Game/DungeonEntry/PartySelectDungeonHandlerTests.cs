using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Protocol.Tests.Game.TownProjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using EnteredDungeon = Dfo.Server.Abstractions.World.DungeonEntry;

namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;

public sealed class PartySelectDungeonHandlerTests
{
    [Fact]
    public async Task The_leader_opens_one_selection_context_for_every_party_member()
    {
        var sessions = new Sessions(Leader, Member);
        var runtime = new Runtime(SelectCharacterPayloadTestData.CreateComplete(), [Leader, Member]);
        var intents = new Intents();
        var sender = new Sender();
        var handler = new EnterSelectDungeonHandler(
            new UnchangedState(),
            sessions,
            runtime,
            new PartyVisits(),
            intents,
            sender,
            TownAreaProjectionTestFactory.Create(sessions, runtime, sender),
            NullLogger<EnterSelectDungeonHandler>.Instance,
            new Parties(Snapshot()));

        await handler.HandleAsync(
            Dispatch(Leader),
            new EnterSelectDungeonRequest(GameMessageMetadata.Empty, DungeonId),
            CancellationToken.None);

        var expectedUsers = new[] { Leader.WireUserId, Member.WireUserId };
        Assert.Equal(
            expectedUsers,
            Assert.Single(sender.Messages(Leader.ConnectionId)
                .OfType<EnterSelectDungeonNotification>()).WireUserIds);
        Assert.Equal(
            expectedUsers,
            Assert.Single(sender.Messages(Member.ConnectionId)
                .OfType<EnterSelectDungeonNotification>()).WireUserIds);
        Assert.NotNull(intents.Consume(Leader));
        Assert.NotNull(intents.Consume(Member));
    }

    [Fact]
    public async Task The_leader_cancels_selection_for_every_member_and_restores_the_shared_town()
    {
        var sessions = new Sessions(Leader, Member);
        var runtime = new PartyRuntime(
            (Leader, EntryFor(Leader)),
            (Member, EntryFor(Member)));
        var intents = new Intents();
        intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));
        intents.Record(Member, DungeonEntryIntent.Directed(DungeonId));
        var sender = new Sender();
        using var services = new ServiceCollection()
            .AddSingleton<ISelectedSessionDomainService>(sessions)
            .AddSingleton<ISelectedSessionRuntimeDirectory>(runtime)
            .AddSingleton<ICharacterPositionDomainService>(new PartyPositions())
            .AddSingleton<IDungeonRunDomainService>(new RollbackRuns())
            .AddSingleton(new DungeonRunDeliveryRegistry())
            .AddSingleton<IDungeonEntryIntentDomainService>(intents)
            .AddSingleton<IPartyService>(new Parties(Snapshot()))
            .AddSingleton<IGameMessageSender>(sender)
            .AddSingleton(TownAreaProjectionTestFactory.Create(sessions, runtime, sender))
            .AddSingleton<SelectedSessionTransitionGate>()
            .AddSingleton<Microsoft.Extensions.Logging.ILogger<BackToVillageHandler>>(
                NullLogger<BackToVillageHandler>.Instance)
            .BuildServiceProvider();
        var handler = ActivatorUtilities.CreateInstance<BackToVillageHandler>(services);

        await handler.HandleAsync(
            Dispatch(Leader),
            new BackToVillageRequest(GameMessageMetadata.Empty),
            CancellationToken.None);

        Assert.IsType<BackToVillageResponse>(sender.Messages(Leader.ConnectionId).First());
        Assert.DoesNotContain(
            sender.Messages(Member.ConnectionId),
            frame => frame is BackToVillageResponse);
        foreach (var participant in new[] { Leader, Member })
        {
            var messages = sender.Messages(participant.ConnectionId);
            Assert.Contains(messages, frame => frame is UserStateNotification state
                && state.WireUserId == participant.WireUserId
                && state.UserState == 0);
            var roster = Assert.Single(messages.OfType<AreaUsersNotification>());
            Assert.Equal(
                new[] { Leader.WireUserId, Member.WireUserId }.Order(),
                roster.Users.Select(value => value.WireUserId).Order());
            Assert.Contains(
                messages.OfType<CurrentUserStateMessage>(),
                frame => frame.Character.Identity.CharacterId != participant.CharacterId);
        }
        Assert.Null(intents.Consume(Leader));
        Assert.Null(intents.Consume(Member));

        var memberFramesBeforeLateRequest = sender.Messages(Member.ConnectionId).Count;
        await handler.HandleAsync(
            Dispatch(Member),
            new BackToVillageRequest(GameMessageMetadata.Empty),
            CancellationToken.None);

        var lateFrames = sender.Messages(Member.ConnectionId)
            .Skip(memberFramesBeforeLateRequest)
            .ToArray();
        Assert.IsType<BackToVillageResponse>(Assert.Single(lateFrames));
    }

    [Fact]
    public async Task A_party_member_cannot_select_the_story_dungeon_for_the_party()
    {
        var world = World();
        world.Intents.Record(Member, DungeonEntryIntent.Directed(DungeonId));

        await world.Handler.HandleAsync(Dispatch(Member), Request(), CancellationToken.None);

        var refusal = Assert.IsType<SelectDungeonRefusal>(
            Assert.Single(world.Sender.Messages(Member.ConnectionId)));
        Assert.Equal(SelectDungeonRefusal.NotPartyLeader, refusal.ErrorCode);
        Assert.Equal(0, world.Visits.PartyEntries);
        Assert.Empty(world.Registry.Find(world.Visits.RunId));
    }

    [Fact]
    public async Task An_unattachable_member_refuses_the_whole_party_before_a_run_is_created()
    {
        var world = World(memberRuntimeAvailable: false);
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));

        await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        var refusal = Assert.IsType<SelectDungeonRefusal>(
            Assert.Single(world.Sender.Messages(Leader.ConnectionId)));
        Assert.Equal(SelectDungeonRefusal.MemberBlocked, refusal.ErrorCode);
        Assert.Equal(1, refusal.Context);
        Assert.Equal(0, world.Visits.PartyEntries);
        Assert.Empty(world.Fatigue.Spent);
    }

    [Fact]
    public async Task The_leader_enters_every_frozen_member_and_supplies_a_missing_selection_prelude()
    {
        var world = World();
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));

        await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        Assert.Equal(1, world.Visits.PartyEntries);
        Assert.Collection(
            world.Visits.Participants,
            participant =>
            {
                Assert.Equal(Leader.CharacterId, participant.CharacterId);
                Assert.True(participant.IsLeader);
                Assert.Equal(0, participant.Slot);
            },
            participant =>
            {
                Assert.Equal(Member.CharacterId, participant.CharacterId);
                Assert.False(participant.IsLeader);
                Assert.Equal(1, participant.Slot);
            });
        Assert.Equal(world.Visits.RunId, world.Visits.Visits[Leader.CharacterId].Run.RunId);
        Assert.Equal(world.Visits.RunId, world.Visits.Visits[Member.CharacterId].Run.RunId);
        Assert.Equal(
            world.Visits.Visits[Leader.CharacterId].Run.CurrentCell,
            world.Visits.Visits[Member.CharacterId].Run.CurrentCell);
        Assert.Equal(
            [Leader, Member],
            world.Registry.Find(world.Visits.RunId));

        Assert.Collection(
            world.Sender.Messages(Leader.ConnectionId),
            frame => Assert.IsType<EnterSelectDungeonAcknowledgement>(frame),
            frame => Assert.IsType<SupportTagNotification>(frame),
            frame => Assert.IsType<DungeonInfoNotification>(frame),
            frame => Assert.IsType<StartMapNotification>(frame));
        Assert.Collection(
            world.Sender.Messages(Member.ConnectionId),
            frame => Assert.IsType<ExtendedUserStateMessage>(frame),
            frame => Assert.IsType<EnterSelectDungeonAcknowledgement>(frame),
            frame => Assert.IsType<UserStateNotification>(frame),
            frame => Assert.IsType<UdpHostNotification>(frame),
            frame => Assert.IsType<EnterSelectDungeonNotification>(frame),
            frame => Assert.IsType<EnterSelectDungeonAcknowledgement>(frame),
            frame => Assert.IsType<SupportTagNotification>(frame),
            frame => Assert.IsType<DungeonInfoNotification>(frame),
            frame => Assert.IsType<StartMapNotification>(frame));
        Assert.Equal(
            new[] { Leader.WireUserId, Member.WireUserId },
            Assert.Single(world.Sender.Messages(Member.ConnectionId)
                .OfType<EnterSelectDungeonNotification>()).WireUserIds);
        Assert.Equal(
            [Leader.CharacterId, Member.CharacterId],
            world.Fatigue.Spent.Select(value => value.CharacterId));
    }

    [Fact]
    public async Task An_interactive_ordinary_selection_does_not_become_a_story_party_entry()
    {
        var world = World();
        world.Intents.Record(Leader, DungeonEntryIntent.Interactive);

        await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        var participant = Assert.Single(world.Visits.Participants);
        Assert.Equal(Leader.CharacterId, participant.CharacterId);
        Assert.Empty(world.Sender.Messages(Member.ConnectionId));
        Assert.Equal([Leader], world.Registry.Find(world.Visits.RunId));
    }

    [Fact]
    public async Task Entering_an_interactive_solo_run_removes_that_member_from_the_old_story_delivery()
    {
        var world = World();
        var oldRunId = DungeonRunId.New();
        var oldRun = world.Visits.Visits.Count == 0
            ? new DungeonRunState(
                PartyVisits.LayoutForTest(),
                new MazeCell(0, 0),
                61000,
                new HashSet<MazeCell> { new(0, 0) },
                new HashSet<GroundDropId>(),
                new HashSet<RoomActorId>(),
                DateTimeOffset.UnixEpoch,
                RunId: oldRunId)
            : throw new InvalidOperationException();
        world.Runs.SetCurrent(Leader.CharacterId, oldRun);
        world.Runs.SetCurrent(Member.CharacterId, oldRun);
        world.Registry.Register(oldRunId, [Leader, Member]);
        world.Intents.Record(Leader, DungeonEntryIntent.Interactive);

        await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        Assert.Equal([Member], world.Registry.Find(oldRunId));
        Assert.Equal([Leader], world.Registry.Find(world.Visits.RunId));
    }

    [Fact]
    public async Task A_generation_change_during_delivery_preparation_rolls_back_without_run_frames()
    {
        var world = World(replaceMemberDuringPrelude: true, withTownProjection: true);
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));

        await world.Handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        Assert.Empty(world.Registry.Find(world.Visits.RunId));
        Assert.Equal(
            [Leader.CharacterId, Member.CharacterId],
            world.Runs.Ended.OrderBy(value => value.Value));
        Assert.DoesNotContain(world.Sender.Messages(Leader.ConnectionId), frame => frame is StartMapNotification);
        Assert.DoesNotContain(world.Sender.Messages(Member.ConnectionId), frame => frame is StartMapNotification);
        Assert.True(Leader.Owns(world.Areas.Find(Leader.CharacterId)!));
    }

    [Fact]
    public async Task Cancelling_while_acquiring_commit_gates_rolls_back_before_leaving_town()
    {
        var world = World(withTownProjection: true);
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));
        var blocker = await world.TransitionGate.AcquireAsync(
            Leader.AccountId,
            UserAccessMode.Exclusive,
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var pending = world.Handler.HandleAsync(
            Dispatch(Leader), Request(), cancellation.Token).AsTask();
        await Task.Yield();
        Assert.False(pending.IsCompleted);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await blocker.DisposeAsync();

        Assert.Equal(
            [Leader.CharacterId, Member.CharacterId],
            world.Runs.Ended.OrderBy(value => value.Value));
        Assert.True(Leader.Owns(world.Areas.Find(Leader.CharacterId)!));
        Assert.True(Member.Owns(world.Areas.Find(Member.CharacterId)!));
    }

    [Fact]
    public async Task A_projection_failure_cannot_split_party_members_between_town_and_run()
    {
        var world = World(
            withTownProjection: true,
            partyTownProjection: new ThrowingPartyTownProjection());
        world.Intents.Record(Leader, DungeonEntryIntent.Directed(DungeonId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Handler.HandleAsync(
            Dispatch(Leader), Request(), CancellationToken.None).AsTask());

        Assert.Null(world.Areas.Find(Leader.CharacterId));
        Assert.Null(world.Areas.Find(Member.CharacterId));
        Assert.Empty(world.Runs.Ended);
    }

    private static TestWorld World(
        bool memberRuntimeAvailable = true,
        bool replaceMemberDuringPrelude = false,
        bool withTownProjection = false,
        IPartyTownProjectionDelivery? partyTownProjection = null)
    {
        var sessions = new Sessions(Leader, Member);
        var runtime = new Runtime(Entry(), memberRuntimeAvailable ? [Leader, Member] : [Leader]);
        var visits = new PartyVisits();
        var intents = new Intents();
        var registry = new DungeonRunDeliveryRegistry();
        var runs = new RollbackRuns();
        var fatigue = new Fatigue();
        var sender = new Sender();
        var areas = new SessionPresenceDomainService();
        foreach (var member in new[] { Leader, Member })
        {
            areas.Admit(new SessionPresence(
                member.CharacterId,
                member.ListenerPort,
                1,
                1,
                true,
                member,
                true,
                1));
        }
        var townProjection = new TownAreaProjectionDelivery(
            areas,
            sessions,
            runtime,
            new NoopNotificationBus(),
            partyTownProjection ?? new NoopPartyTownProjection(),
            sender,
            SharedTownMovementDomainService.Instance);
        var transitionGate = new SelectedSessionTransitionGate();
        var state = new UnchangedState(() =>
        {
            if (!replaceMemberDuringPrelude)
                return;
            sessions.Replace(
                Member.CharacterId,
                Member with
                {
                    ConnectionId = new NetworkConnectionId("member-replacement"),
                    ConnectionGeneration = Member.ConnectionGeneration + 1,
                });
        });
        var handler = new SelectDungeonHandler(
            CurrentCharacterStub.Domain(),
            state,
            sessions,
            runtime,
            new Parties(Snapshot()),
            runs,
            registry,
            intents,
            visits,
            new RecordingQuestLogs(),
            fatigue,
            sender,
            townProjection,
            NullLogger<SelectDungeonHandler>.Instance,
            transitionGate);
        return new TestWorld(
            handler, visits, intents, registry, runs, fatigue, sender, areas, transitionGate);
    }

    private static SelectCharacterResult Entry() => SelectCharacterPayloadTestData.Create();

    private static SelectCharacterResult EntryFor(SelectedSessionIdentity session)
    {
        var entry = SelectCharacterPayloadTestData.CreateComplete();
        return entry with
        {
            Character = entry.Character with
            {
                Identity = entry.Character.Identity with
                {
                    CharacterId = session.CharacterId,
                    Name = [(byte)(session.WireUserId & byte.MaxValue)],
                },
            },
        };
    }

    private static SelectDungeonRequest Request() =>
        new(GameMessageMetadata.Empty, DungeonId, Difficulty, 0, 0);

    private static NetworkDispatchContext Dispatch(SelectedSessionIdentity session) =>
        new(new NetworkConnectionContext(
            session.ConnectionId, NetworkEndpointKind.Game, session.ListenerPort));

    private static PartySnapshot Snapshot() => new(
        55,
        1,
        1,
        Leader.WireUserId,
        new PartySettings([0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Story, 0, 0]),
        PartyTitle.Empty,
        ImmutableArray.Create(
            new PartyMemberSnapshot(Character(Leader), 0, null, 100),
            new PartyMemberSnapshot(Character(Member), 1, null, 100)));

    private static PartyCharacterSnapshot Character(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId, new PartyDirectoryScope(1, 1, 1));

    private static readonly SelectedSessionIdentity Leader = SelectedSessionIdentity.Create(
        new AccountId(1), new CharacterId(1001), new NetworkConnectionId("leader"), 11, 10010);

    private static readonly SelectedSessionIdentity Member = SelectedSessionIdentity.Create(
        new AccountId(2), new CharacterId(1002), new NetworkConnectionId("member"), 21, 10011);

    private const ushort DungeonId = 10000;
    private const byte Difficulty = 3;

    private sealed record TestWorld(
        SelectDungeonHandler Handler,
        PartyVisits Visits,
        Intents Intents,
        DungeonRunDeliveryRegistry Registry,
        RollbackRuns Runs,
        Fatigue Fatigue,
        Sender Sender,
        ITownAreaDomainService Areas,
        SelectedSessionTransitionGate TransitionGate);

    private sealed class Parties(PartySnapshot snapshot) : IPartyService
    {
        public PartySnapshot? FindParty(CharacterId characterId) => snapshot;
        public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope) { }
        public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => [snapshot];
        public PartyOperationResult CreateOrUpdateSettings(PartyCharacterSnapshot character, PartySettings settings, PartyTitle title) => throw new NotSupportedException();
        public PartyOperationResult RequestInvite(PartyCharacterSnapshot inviter, PartyCharacterSnapshot target, PartyInviteDisplayState display) => throw new NotSupportedException();
        public PartyOperationResult AcceptInvite(PartyCharacterSnapshot invitee, PartyCharacterSnapshot inviter) => throw new NotSupportedException();
        public PartyOperationResult TransferLeadership(CharacterId currentLeaderCharacterId, CharacterId nextLeaderCharacterId) => throw new NotSupportedException();
        public PartyOperationResult LeaveParty(CharacterId characterId) => throw new NotSupportedException();
        public PartyOperationResult CleanupCharacter(CharacterId characterId) => throw new NotSupportedException();
    }

    private sealed class Sessions : ISelectedSessionDomainService
    {
        private readonly List<SelectedSessionIdentity> connections;
        private readonly Dictionary<CharacterId, SelectedSessionIdentity> currentByCharacter;

        public Sessions(params SelectedSessionIdentity[] sessions)
        {
            connections = [.. sessions];
            currentByCharacter = sessions.ToDictionary(value => value.CharacterId);
        }

        public void Replace(CharacterId characterId, SelectedSessionIdentity replacement)
        {
            currentByCharacter[characterId] = replacement;
            connections.Add(replacement);
        }

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            connections.SingleOrDefault(value => value.ConnectionId == connectionId);
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            currentByCharacter.GetValueOrDefault(characterId);
        public SelectedSessionIdentity? Find(AccountId accountId) =>
            currentByCharacter.Values.SingleOrDefault(value => value.AccountId == accountId);
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            currentByCharacter.Values.SingleOrDefault(value => value.WireUserId == wireUserId);
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    private sealed class Runtime(
        SelectCharacterResult result,
        IReadOnlyList<SelectedSessionIdentity> available) : ISelectedSessionRuntimeDirectory
    {
        public bool TryGet(SelectedSessionIdentity session, out SelectCharacterResult? found, out bool persist)
        {
            found = available.Contains(session) ? result : null;
            persist = false;
            return found is not null;
        }
        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) => result;
        public void Publish(SelectedSessionIdentity session, SelectCharacterResult value, bool persist) => throw new NotSupportedException();
        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult value) => throw new NotSupportedException();
        public bool RemoveExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    private sealed class PartyRuntime(
        params (SelectedSessionIdentity Session, SelectCharacterResult Result)[] entries)
        : ISelectedSessionRuntimeDirectory
    {
        private readonly Dictionary<SelectedSessionIdentity, SelectCharacterResult> results =
            entries.ToDictionary(value => value.Session, value => value.Result);

        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool persist)
        {
            persist = false;
            return results.TryGetValue(session, out result);
        }

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) => results[session];
        public void Publish(SelectedSessionIdentity session, SelectCharacterResult value, bool persist) =>
            throw new NotSupportedException();
        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult value) =>
            throw new NotSupportedException();
        public bool RemoveExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    private sealed class PartyPositions : ICharacterPositionDomainService
    {
        public CharacterPositionState? Find(CharacterId characterId) =>
            characterId == Leader.CharacterId
                ? new CharacterPositionState(1, 1, 400, 300, 1)
                : characterId == Member.CharacterId
                    ? new CharacterPositionState(1, 1, 430, 300, 3)
                    : null;

        public void Place(SelectedSessionIdentity session, CharacterPositionState position) =>
            throw new NotSupportedException();
        public CharacterPositionState CrossInto(
            SelectedSessionIdentity session, byte townId, byte areaId, short x, short y) =>
            throw new NotSupportedException();
        public CharacterPositionState StepTo(
            SelectedSessionIdentity session, short x, short y, byte direction) =>
            throw new NotSupportedException();
        public bool ForgetExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    private sealed class Intents : IDungeonEntryIntentDomainService
    {
        private readonly Dictionary<SelectedSessionIdentity, DungeonEntryIntent> values = [];
        public void Record(SelectedSessionIdentity session, DungeonEntryIntent intent) => values[session] = intent;
        public DungeonEntryIntent? Consume(SelectedSessionIdentity session) =>
            values.Remove(session, out var value) ? value : null;
        public bool ClearExact(SelectedSessionIdentity session) => values.Remove(session);
    }

    private sealed class PartyVisits : IDungeonVisitDomainService
    {
        public int PartyEntries { get; private set; }
        public DungeonRunId RunId { get; } = DungeonRunId.New();
        public IReadOnlyList<DungeonRunParticipant> Participants { get; private set; } = [];
        public IReadOnlyDictionary<CharacterId, DungeonRoomVisit> Visits { get; private set; } =
            new Dictionary<CharacterId, DungeonRoomVisit>();

        public bool DefersSelectionState(byte characterLevel) => false;
        public ValueTask<EnteredDungeon?> EnterAsync(CharacterId characterId, int dungeonId, byte difficulty, DungeonEntryFacts facts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<DungeonPartyEntry?> EnterPartyAsync(IReadOnlyList<DungeonRunParticipant> participants, int dungeonId, byte difficulty, DungeonEntryFacts facts, CancellationToken cancellationToken)
        {
            PartyEntries++;
            Participants = participants.ToArray();
            var layout = Layout();
            Visits = participants.ToDictionary(
                value => value.CharacterId,
                value => new DungeonRoomVisit(
                    new DungeonRunState(
                        layout,
                        layout.StartCell,
                        layout.StartRoomMapId,
                        new HashSet<MazeCell> { layout.StartCell },
                        new HashSet<GroundDropId>(),
                        new HashSet<RoomActorId>(),
                        DateTimeOffset.UnixEpoch,
                        difficulty,
                        RunId: RunId,
                        Participant: value,
                        Participants: participants),
                    true,
                    [],
                    [],
                    1));
            return ValueTask.FromResult<DungeonPartyEntry?>(new DungeonPartyEntry(layout, Visits));
        }
        public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId characterId, DungeonRunState current, MazeCell target, CancellationToken cancellationToken) => throw new NotSupportedException();

        public static DungeonLayout LayoutForTest() => Layout();

        private static DungeonLayout Layout() => new(
            DungeonId,
            0,
            2,
            1,
            new MazeCell(0, 0),
            new MazeCell(1, 0),
            61000,
            123,
            new Dictionary<MazeCell, int>
            {
                [new MazeCell(0, 0)] = 61000,
                [new MazeCell(1, 0)] = 61001,
            });
    }

    private sealed class RollbackRuns : IDungeonRunDomainService
    {
        private readonly Dictionary<CharacterId, DungeonRunState> current = [];
        public List<CharacterId> Ended { get; } = [];
        public void SetCurrent(CharacterId characterId, DungeonRunState run) =>
            current[characterId] = run;
        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, byte difficulty) => throw new NotSupportedException();
        public DungeonRunState? Find(CharacterId characterId) => current.GetValueOrDefault(characterId);
        public DungeonRunState? TryMove(CharacterId characterId, MazeCell target) => null;
        public DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort groundDropNumber) => null;
        public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence) => null;
        public DungeonRunState? TrySettle(CharacterId characterId) => null;
        public IReadOnlyList<MonsterDrop> AllocateMonsterDrops(CharacterId characterId, int declaredDropCount, ushort actorSequence, IReadOnlyList<RolledDrop> rolled) => [];
        public DungeonRunState? FixClearRewardCards(CharacterId characterId, ClearRewardCardFace free, ClearRewardCardFace paid, int? paidFlipCost) => null;
        public ClearRewardCardSlot? TryClaimClearRewardCard(CharacterId characterId, ClearRewardCardKind kind, int index) => null;
        public bool End(CharacterId characterId)
        {
            Ended.Add(characterId);
            return true;
        }
        public bool End(CharacterId characterId, DungeonRunId expectedRunId)
        {
            Ended.Add(characterId);
            return true;
        }
    }

    private sealed class Fatigue : ICharacterFatigueDomainService
    {
        public List<(CharacterId CharacterId, ushort Points)> Spent { get; } = [];
        public ValueTask SpendAsync(CharacterId characterId, ushort points, CancellationToken cancellationToken)
        {
            Spent.Add((characterId, points));
            return ValueTask.CompletedTask;
        }
        public ValueTask<CharacterFatigueBalance> BalanceOfAsync(CharacterId characterId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask RefundAsync(CharacterId characterId, ushort points, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnchangedState(Action? onRefresh = null) : ICharacterStateProjectionDomainService
    {
        public ValueTask<SelectCharacterResult> RefreshAsync(
            SelectCharacterResult result,
            CancellationToken cancellationToken)
        {
            onRefresh?.Invoke();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Sender : IGameMessageSender
    {
        private readonly Dictionary<NetworkConnectionId, List<IGameServerMessage>> sent = [];
        public IReadOnlyList<IGameServerMessage> Messages(NetworkConnectionId connectionId) =>
            sent.TryGetValue(connectionId, out var messages) ? messages : [];
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken)
        {
            Add(requiredDirectConnectionId, delivery.RequiredDirectMessages);
            foreach (var target in delivery.BestEffortTargets)
                Add(target.ConnectionId, target.OrderedMessages);
            return ValueTask.CompletedTask;
        }
        private void Add(NetworkConnectionId connectionId, IReadOnlyList<IGameServerMessage> messages)
        {
            if (!sent.TryGetValue(connectionId, out var destination))
            {
                destination = [];
                sent.Add(connectionId, destination);
            }
            destination.AddRange(messages);
        }
    }

    private sealed class NoopNotificationBus : INotificationBus
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

    private sealed class ThrowingPartyTownProjection : IPartyTownProjectionDelivery
    {
        public ValueTask RefreshObservedPartiesAsync(
            IReadOnlyList<SelectedSessionIdentity> recipients,
            IReadOnlyList<SelectedSessionIdentity> representedSessions,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RefreshMovedPartyAsync(
            SelectedSessionIdentity mover,
            PartyDirectoryScope? previousScope,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("projection failed");
    }
}
