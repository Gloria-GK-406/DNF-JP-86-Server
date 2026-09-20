using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Protocol.Tests.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Game.Party;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.DungeonClear;

/// <summary>
/// Walking out of a finished dungeon: the command that does it, and what follows it.
/// </summary>
/// <remarks>
/// The command is <c>CMD 0x0048 EPLP_COMMAND</c> and not <c>GIVEUP_GAME</c>. A complete reference
/// session that cleared a dungeon and returned to town sent <c>0x0048</c> twice and <c>0x002A</c>
/// never; this server had no such type, which is what a player reported as returning to town not
/// working.
/// </remarks>
public sealed class LeaveDungeonTests
{
    /// <summary>
    /// The exchange as captured, transcribed.
    /// </summary>
    /// <remarks>
    /// From <c>packet_log.upstream-killexp-20260827.txt</c>, the second of the two the session
    /// sent -- the one the town state follows:
    /// <code>
    /// RECV cmd=0x01 type=0x0048 len=16 body=(2B) [01 02]
    /// SEND cmd=0x01 type=0x0048 len=18 body=(3B) [01 01 02]
    /// </code>
    /// Transcribed rather than derived from the constants under test: request and answer share one
    /// type, so a wrong constant changes both and no self-consistent test would notice.
    /// </remarks>
    private static readonly byte[] CapturedLeaveBody = [0x01, 0x02];

    private static readonly byte[] CapturedAnswerBody = [0x01, 0x01, 0x02];

    private const ushort ObservedType = 0x0048;

    /// <summary>The state and option of that captured request.</summary>
    private const byte LeaveState = 0x01;

    private const byte CapturedOption = 0x02;

    [Fact]
    public void The_captured_request_decodes_to_its_state_and_option()
    {
        var request = (EplpCommandRequest)new EplpCommandDecoder()
            .Decode(GameMessageMetadata.Empty, CapturedLeaveBody)!;

        Assert.Equal(LeaveState, request.State);
        Assert.Equal(CapturedOption, request.Option);
    }

    /// <summary>
    /// A request missing its option is declined, and declining is not throwing.
    /// </summary>
    /// <remarks>
    /// A throwing decoder takes the connection with it, and this is the frame a player sends to
    /// leave a finished dungeon. Dropping them for a malformed press is the one answer that must
    /// not happen.
    /// </remarks>
    [Fact]
    public void A_request_missing_its_option_is_declined_not_thrown()
    {
        var thrown = Record.Exception(() =>
            Assert.Null(new EplpCommandDecoder().Decode(GameMessageMetadata.Empty, new byte[1])));

        Assert.Null(thrown);
    }

    /// <summary>The answer is the captured one, under the captured type.</summary>
    /// <remarks>
    /// The two echoed bytes are not decoration. The client's reply handler reads the success byte
    /// and then the state, and runs the leave path only when the state it gets back is 1 -- so an
    /// answer that dropped or altered them would be answered and still not leave.
    /// </remarks>
    [Fact]
    public void The_answer_echoes_the_state_and_option_under_the_captured_type()
    {
        var frame = Assert.Single(
            new EplpCommandResponseEncoder()
                .Encode(new EplpCommandResponse(LeaveState, CapturedOption)));

        Assert.Equal(0x01, frame[0]);
        Assert.Equal(ObservedType, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        Assert.Equal(CapturedAnswerBody, frame[GameServerFrameEncoder.HeaderLength..]);
    }

    /// <summary>Leaving ends the run and projects the town, in that order behind the answer.</summary>
    [Fact]
    public async Task Leaving_ends_the_run_and_projects_the_town()
    {
        var runs = new OneRun();
        var sent = await Leave(LeaveState, runs);

        Assert.True(runs.Ended);
        Assert.Equal(
            [typeof(EplpCommandResponse), typeof(UserStateNotification),
             typeof(UserAreaNotification), typeof(AreaUsersNotification)],
            sent.Select(frame => frame.GetType()));
    }

    /// <summary>The projection carries where the character is standing, not the channel's spawn.</summary>
    /// <remarks>
    /// A player who walked to the dungeon gate should come back to the gate. This is the assertion
    /// that catches projecting the spawn point instead, which would silently teleport them across
    /// town every time they finished a run.
    /// </remarks>
    [Fact]
    public async Task The_projection_carries_where_the_character_is_standing()
    {
        var sent = await Leave(LeaveState, new OneRun());

        var area = sent.OfType<UserAreaNotification>().Single();
        Assert.Equal(StandingAreaId, area.AreaId);

        var users = sent.OfType<AreaUsersNotification>().Single();
        Assert.Equal(StandingTownId, users.TownId);
        Assert.Equal(StandingAreaId, users.AreaId);
        var presence = Assert.Single(users.Users);
        Assert.Equal(StandingX, presence.X);
        Assert.Equal(StandingY, presence.Y);
    }

    /// <summary>Every other state is answered and moves nothing.</summary>
    /// <remarks>
    /// The captured session sent <c>02 02</c> before <c>01 02</c> and only the second was followed
    /// by the town state. Treating every state as an exit would end the run under the player's feet
    /// the first time the client used this command for anything else.
    /// </remarks>
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)2)]
    [InlineData((byte)9)]
    public async Task A_state_that_is_not_the_exit_is_answered_and_moves_nothing(byte state)
    {
        var runs = new OneRun();
        var sent = await Leave(state, runs);

        Assert.False(runs.Ended);
        var answer = Assert.IsType<EplpCommandResponse>(Assert.Single(sent));
        Assert.Equal(state, answer.State);
    }

    /// <summary>
    /// Leaving with no run left to end is still answered and still projects the town.
    /// </summary>
    /// <remarks>
    /// The button has already been pressed by the time this arrives, and the client cannot ask
    /// again. Refusing would leave it exactly as unanswered did -- which is the defect.
    /// </remarks>
    [Fact]
    public async Task Leaving_without_a_run_is_still_answered_and_still_projects_the_town()
    {
        var runs = new OneRun(hasRun: false);
        var sent = await Leave(LeaveState, runs);

        Assert.Equal(4, sent.Count);
        Assert.Contains(sent, frame => frame is AreaUsersNotification);
    }

    [Fact]
    public async Task Leaving_removes_only_this_participant_from_the_frozen_delivery_set()
    {
        var runs = new OneRun();
        var helper = SelectedSessionIdentity.Create(
            new AccountId(2),
            new CharacterId(61),
            new NetworkConnectionId("helper"),
            connectionGeneration: 1,
            listenerPort: 10010);
        var deliveries = new DungeonRunDeliveryRegistry();
        deliveries.Register(runs.RunId, [Identity, helper]);
        var handler = new EplpCommandHandler(
            new OneSession(Identity),
            new OneRuntimeSession(Entry()),
            new StandingWhereTheyAre(),
            runs,
            deliveries,
            new RecordingSender(),
            TownAreaProjectionTestFactory.Create(
                new OneSession(Identity),
                new OneRuntimeSession(Entry()),
                new RecordingSender()),
            NullLogger<EplpCommandHandler>.Instance,
            new SelectedSessionTransitionGate());

        await handler.HandleAsync(
            Dispatch(),
            new EplpCommandRequest(GameMessageMetadata.Empty, LeaveState, CapturedOption),
            CancellationToken.None);

        Assert.Equal([helper], deliveries.Find(runs.RunId));
    }

    [Fact]
    public async Task GiveUpEndsTheRunAndProjectsTheSameTownState()
    {
        var runs = new OneRun();
        var deliveries = new DungeonRunDeliveryRegistry();
        deliveries.Register(runs.RunId, [Identity]);
        var sender = new RecordingSender();
        var commands = new RecordingCommandBus();
        var handler = new GiveUpGameHandler(
            new OneSession(Identity),
            new OneRuntimeSession(Entry()),
            new StandingWhereTheyAre(),
            runs,
            deliveries,
            sender,
            TownAreaProjectionTestFactory.Create(
                new OneSession(Identity),
                new OneRuntimeSession(Entry()),
                sender),
            NullLogger<GiveUpGameHandler>.Instance,
            new SelectedSessionTransitionGate(),
            new SessionPresenceDomainService(),
            commands,
            new PartyNotificationDelivery(new NoopNotificationBus()));

        await handler.HandleAsync(
            Dispatch(),
            new GiveUpGameRequest(GameMessageMetadata.Empty),
            CancellationToken.None);

        Assert.True(runs.Ended);
        Assert.Equal(
            [typeof(GiveUpGameResponse), typeof(UserStateNotification),
             typeof(UserAreaNotification), typeof(AreaUsersNotification)],
            sender.Sent.Select(frame => frame.GetType()));
        Assert.Equal(
            StandingAreaId,
            Assert.Single(sender.Sent.OfType<UserAreaNotification>()).AreaId);
        Assert.Empty(deliveries.Find(runs.RunId));
        Assert.Equal(
            Identity.CharacterId,
            Assert.IsType<LeavePartyCommand>(Assert.Single(commands.Commands)).CharacterId);
    }

    [Fact]
    public async Task GiveUp_without_an_active_run_does_not_leave_the_party()
    {
        var commands = new RecordingCommandBus();
        var handler = new GiveUpGameHandler(
            new OneSession(Identity),
            new OneRuntimeSession(Entry()),
            new StandingWhereTheyAre(),
            new OneRun(hasRun: false),
            new DungeonRunDeliveryRegistry(),
            new RecordingSender(),
            TownAreaProjectionTestFactory.Create(
                new OneSession(Identity),
                new OneRuntimeSession(Entry()),
                new RecordingSender()),
            NullLogger<GiveUpGameHandler>.Instance,
            new SelectedSessionTransitionGate(),
            new SessionPresenceDomainService(),
            commands,
            new PartyNotificationDelivery(new NoopNotificationBus()));

        await handler.HandleAsync(
            Dispatch(),
            new GiveUpGameRequest(GameMessageMetadata.Empty),
            CancellationToken.None);

        Assert.Empty(commands.Commands);
    }

    /// <summary>Nothing is answered on a connection with no published session.</summary>
    [Fact]
    public async Task A_command_without_a_published_session_is_ignored()
    {
        var sender = new RecordingSender();
        var handler = new EplpCommandHandler(
            new NoSession(),
            new NoRuntimeSession(),
            new StandingNowhere(),
            new OneRun(),
            new DungeonRunDeliveryRegistry(),
            sender,
            TownAreaProjectionTestFactory.Create(
                new NoSession(),
                new NoRuntimeSession(),
                sender),
            NullLogger<EplpCommandHandler>.Instance,
            new SelectedSessionTransitionGate());

        await handler.HandleAsync(
            Dispatch(),
            new EplpCommandRequest(GameMessageMetadata.Empty, LeaveState, CapturedOption),
            CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    /// <summary>
    /// Cancelling out of the selection screen projects the same town frames, from the same source.
    /// </summary>
    /// <remarks>
    /// The projection was extracted out of that handler so this one could use it. This is the test
    /// that keeps them one thing: written twice they drift, and the drift shows up as returning to
    /// town working from the gate and not from the dungeon.
    /// </remarks>
    [Fact]
    public async Task Cancelling_out_of_the_selection_screen_projects_the_same_frames()
    {
        var runs = new OneRun();
        var deliveries = new DungeonRunDeliveryRegistry();
        deliveries.Register(runs.RunId, [Identity]);
        var sender = new RecordingSender();
        var handler = new BackToVillageHandler(
            new OneSession(Identity),
            new RuntimeCurrentView(new OneRuntimeSession(Entry())),
            new StandingWhereTheyAre(),
            runs,
            deliveries,
            sender,
            TownAreaProjectionTestFactory.Create(
                new OneSession(Identity),
                new OneRuntimeSession(Entry()),
                sender),
            NullLogger<BackToVillageHandler>.Instance,
            new SelectedSessionTransitionGate(),
            new VirtualOnlyPartyService());

        await handler.HandleAsync(
            Dispatch(),
            new BackToVillageRequest(GameMessageMetadata.Empty),
            CancellationToken.None);

        Assert.Equal(
            [typeof(BackToVillageResponse), typeof(UserStateNotification),
             typeof(UserAreaNotification), typeof(AreaUsersNotification)],
            sender.Sent.Select(frame => frame.GetType()));

        // The same values the dungeon's exit projects, asserted the same way rather than by
        // comparing the two message objects: the roster notification holds a list, and records
        // compare lists by reference, so an object-level equality here would pass or fail for a
        // reason that has nothing to do with the projection agreeing.
        var users = sender.Sent.OfType<AreaUsersNotification>().Single();
        Assert.Equal(StandingTownId, users.TownId);
        Assert.Equal(StandingAreaId, users.AreaId);
        var presence = Assert.Single(users.Users);
        Assert.Equal(StandingX, presence.X);
        Assert.Equal(StandingY, presence.Y);
        Assert.Equal(SpawnAreaState, presence.AreaState);
        Assert.Empty(deliveries.Find(runs.RunId));
    }

    [Fact]
    public async Task A_return_waiting_on_the_transition_gate_cannot_overwrite_a_replacement()
    {
        var sessions = new SelectedSessionDomainService();
        sessions.Select(Identity);
        var runtimes = new SelectedSessionRuntimeDirectory();
        var runtime = Entry();
        runtime = runtime with
        {
            Character = runtime.Character with
            {
                Identity = runtime.Character.Identity with
                {
                    CharacterId = Identity.CharacterId,
                },
            },
        };
        runtimes.Publish(Identity, false);
        var currentViews = new FixedCurrentViews();
        currentViews.Publish(Identity, runtime);
        var areas = new SessionPresenceDomainService();
        areas.Admit(new SessionPresence(
            Identity.CharacterId,
            Identity.ListenerPort,
            StandingTownId,
            StandingAreaId,
            true,
            Identity,
            true,
            10));
        var sender = new RecordingSender();
        var transitionGate = new SelectedSessionTransitionGate();
        var projection = new TownAreaProjectionDelivery(
            areas,
            sessions,
            currentViews,
            new NoopNotificationBus(),
            new NoopPartyTownProjection(),
            sender,
            SharedTownMovementDomainService.Instance);
        var handler = new EplpCommandHandler(
            sessions,
            currentViews,
            new StandingWhereTheyAre(),
            new OneRun(),
            new DungeonRunDeliveryRegistry(),
            sender,
            projection,
            NullLogger<EplpCommandHandler>.Instance,
            transitionGate);
        var blocker = await transitionGate.AcquireAsync(
            Identity.AccountId,
            UserAccessMode.Exclusive,
            CancellationToken.None);
        var pending = handler.HandleAsync(
            Dispatch(),
            new EplpCommandRequest(GameMessageMetadata.Empty, LeaveState, CapturedOption),
            CancellationToken.None).AsTask();
        Assert.False(pending.IsCompleted);

        var replacement = SelectedSessionIdentity.Create(
            Identity.AccountId,
            Identity.CharacterId,
            new NetworkConnectionId("replacement"),
            Identity.ConnectionGeneration + 1,
            Identity.ListenerPort);
        sessions.Select(replacement);
        areas.Admit(new SessionPresence(
            replacement.CharacterId,
            replacement.ListenerPort,
            StandingTownId,
            StandingAreaId,
            true,
            replacement,
            true,
            10));
        await blocker.DisposeAsync();
        await pending;

        Assert.True(replacement.Owns(
            ((ITownAreaDomainService)areas).Find(replacement.CharacterId)!));
        Assert.Empty(sender.Sent);
    }

    private static async Task<IReadOnlyList<IGameServerMessage>> Leave(byte state, OneRun runs)
    {
        var sender = new RecordingSender();
        var handler = new EplpCommandHandler(
            new OneSession(Identity),
            new OneRuntimeSession(Entry()),
            new StandingWhereTheyAre(),
            runs,
            new DungeonRunDeliveryRegistry(),
            sender,
            TownAreaProjectionTestFactory.Create(
                new OneSession(Identity),
                new OneRuntimeSession(Entry()),
                sender),
            NullLogger<EplpCommandHandler>.Instance,
            new SelectedSessionTransitionGate());

        await handler.HandleAsync(
            Dispatch(),
            new EplpCommandRequest(GameMessageMetadata.Empty, state, CapturedOption),
            CancellationToken.None);

        return sender.Sent;
    }

    private const byte StandingTownId = 3;
    private const byte StandingAreaId = 6;
    private const short StandingX = 902;
    private const short StandingY = 388;

    /// <summary>The area state the channel's spawn carries, which the projection reuses.</summary>
    private const byte SpawnAreaState = 7;

    /// <summary>Where the character actually is, which the projection reads.</summary>
    private static CharacterPositionState Standing { get; } = new(
        StandingTownId, StandingAreaId, StandingX, StandingY, Direction: 1);

    /// <summary>
    /// An entry whose spawn and whose stale snapshot are both somewhere else.
    /// </summary>
    /// <remarks>
    /// Three different places, deliberately, and every coordinate differs. The spawn is one wrong
    /// answer -- projecting it would teleport a player across town for finishing a run. The copy of
    /// the position the entry snapshot still carries is the other: it is the one the projection
    /// used to read, and it stops being corrected the moment a step is reported to the service that
    /// owns where the character stands. Either mistake fails every assertion below rather than
    /// coinciding with the right answer.
    /// </remarks>
    private static SelectCharacterResult Entry()
    {
        var result = SelectCharacterPayloadTestData.Create();
        return result with
        {
            Character = result.Character with
            {
                Position = new CharacterPositionState(
                    TownId: 9, AreaId: 8, X: 77, Y: 66, Direction: 4),
            },
            World = result.World with
            {
                Channel = new ChannelEntryPolicyState(
                    ChannelId: 10,
                    Spawn: new EntrySpawnState(
                        TownId: 1, AreaId: 1, X: 100, Y: 100,
                        Direction: 0, AreaState: SpawnAreaState, IsTransient: false),
                    PersistsPosition: true,
                    PvpSkillProfileApplies: false),
            },
        };
    }

    private static NetworkDispatchContext Dispatch() =>
        new(new NetworkConnectionContext(
            new NetworkConnectionId("one"), NetworkEndpointKind.Game, 10010));

    private static SelectedSessionIdentity Identity { get; } = SelectedSessionIdentity.Create(
        new AccountId(1),
        new CharacterId(60),
        new NetworkConnectionId("one"),
        connectionGeneration: 1,
        listenerPort: 10010);

    /// <summary>A run that records whether it was ended, and refuses everything else.</summary>
    private sealed class OneRun(bool hasRun = true) : IDungeonRunDomainService
    {
        private static readonly DungeonLayout Layout = new(
            144, 0, 1, 1,
            new MazeCell(0, 0), new MazeCell(0, 0), 8153, 7,
            new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 8153 },
            5);

        public bool Ended { get; private set; }

        public DungeonRunId RunId { get; } = DungeonRunId.New();

        /// <summary>Not exercised here: nothing in these tests kills anything.</summary>
        public IReadOnlyList<MonsterDrop> AllocateMonsterDrops(
            CharacterId characterId,
            int declaredDropCount,
            ushort actorSequence,
            IReadOnlyList<RolledDrop> rolled) => [];

        public bool End(CharacterId characterId)
        {
            this.Ended = true;
            return hasRun;
        }

        public DungeonRunState? Find(CharacterId characterId) => hasRun && !this.Ended
            ? new DungeonRunState(
                Layout,
                Layout.StartCell,
                Layout.StartRoomMapId,
                new HashSet<MazeCell> { Layout.StartCell },
                DateTimeOffset.UnixEpoch,
                RunId: this.RunId,
                CurrentCellState: DungeonCellSeed.CompleteEmpty(
                    Layout.StartCell, Layout.StartRoomMapId).Snapshot())
            : null;

        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, DungeonDifficulty difficulty) =>
            throw new NotSupportedException("This fixture only ends.");

        public DungeonRunState? TryMove(CharacterId characterId, MazeCell target) =>
            throw new NotSupportedException("This fixture only ends.");

        public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence) =>
            throw new NotSupportedException("This fixture only ends.");

        public DungeonRunState? TrySettle(CharacterId characterId) =>
            throw new NotSupportedException("This fixture only ends.");

        public DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort groundDropNumber) =>
            throw new NotSupportedException("This fixture only ends.");

        public DungeonRunState? FixClearRewardCards(
            CharacterId characterId,
            ClearRewardCardFace free,
            ClearRewardCardFace paid,
            int? paidFlipCost) =>
            throw new NotSupportedException("This fixture only ends.");

        public ClearRewardCardSlot? TryClaimClearRewardCard(
            CharacterId characterId, ClearRewardCardKind kind, int index) =>
            throw new NotSupportedException("This fixture only ends.");
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
            throw new NotSupportedException("Leaving does not rewrite the session.");

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

    /// <summary>The service that owns where the character is, answering the one place they are.</summary>
    private sealed class StandingWhereTheyAre : ICharacterPositionDomainService
    {
        public CharacterPositionState? Find(CharacterId characterId) => Standing;

        public void Place(SelectedSessionIdentity session, CharacterPositionState position) =>
            throw new NotSupportedException("Leaving a dungeon does not place anyone.");

        public CharacterPositionState CrossInto(
            SelectedSessionIdentity session, byte townId, byte areaId, short x, short y) =>
            throw new NotSupportedException("Leaving a dungeon reports no movement.");

        public CharacterPositionState StepTo(
            SelectedSessionIdentity session, short x, short y, byte direction) =>
            throw new NotSupportedException("Leaving a dungeon reports no movement.");

        public bool ForgetExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("Leaving a dungeon does not end the session.");
    }

    /// <summary>Nobody is standing anywhere, which is what no published session looks like.</summary>
    private sealed class StandingNowhere : ICharacterPositionDomainService
    {
        public CharacterPositionState? Find(CharacterId characterId) => null;

        public void Place(SelectedSessionIdentity session, CharacterPositionState position) =>
            throw new NotSupportedException("This fixture holds nothing.");

        public CharacterPositionState CrossInto(
            SelectedSessionIdentity session, byte townId, byte areaId, short x, short y) =>
            throw new NotSupportedException("This fixture holds nothing.");

        public CharacterPositionState StepTo(
            SelectedSessionIdentity session, short x, short y, byte direction) =>
            throw new NotSupportedException("This fixture holds nothing.");

        public bool ForgetExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture holds nothing.");
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("These frames are sent explicitly.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("These frames are sent explicitly.");

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Sent.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingCommandBus : ICommandBus
    {
        public List<ICommand> Commands { get; } = [];

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Commands.Add(command);
            return ValueTask.FromResult(
                (TResult)(object)PartyOperationResult.Success());
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
}
