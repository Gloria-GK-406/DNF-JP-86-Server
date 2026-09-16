using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonClear;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.DungeonClear;

/// <summary>
/// The two frames the client sends around the result screen: its own word that the boss is down,
/// and the scroll reporting that it has finished drawing.
/// </summary>
/// <remarks>
/// Both went unhandled on 2026-08-29 and only one of them mattered. The boss confirmation is a
/// report the client expects nothing back from; the scroll's state is the frame the whole ending
/// waits on. Keeping them in one place is deliberate -- they arrived in the same log as the same
/// kind of gap, and the difference between them is the point.
/// </remarks>
public sealed class ResultScreenTests
{
    /// <summary>
    /// The body of the <c>0x0075</c> the client sent on 2026-08-29, transcribed.
    /// </summary>
    /// <remarks>
    /// Thirty-nine bytes of which this server reads four. The run it belonged to had reported
    /// actor <c>0x0EE9</c> dead in the boss room shortly before, under wire user 1, which is what
    /// makes the two leading numbers more than a reading borrowed from the reference server.
    /// </remarks>
    private static readonly byte[] CapturedConfirmation =
    [
        0x01, 0x00, 0xE9, 0x0E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x2F, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x71, 0x02, 0x0D,
        0x01, 0x92, 0x01, 0x3E, 0x01, 0x0A, 0x00, 0x08, 0x00, 0x11, 0x00,
    ];

    /// <summary>The actor the captured frame names, which is the one that run had killed.</summary>
    private const ushort CapturedActorSequence = 0x0EE9;

    /// <summary>The wire user the captured frame names.</summary>
    private const ushort CapturedUserId = 1;

    /// <summary>
    /// The wire type of the scroll frame, transcribed off the session that went unanswered.
    /// </summary>
    /// <remarks>
    /// From the live log: <c>Unhandled Game frame: selector=0x01 type=0x0045 seq=131 body(0B)=</c>.
    /// Transcribed rather than read from the constant under test, for the reason the connection
    /// probe's frame is transcribed: the decoder is registered under the same constant the encoder
    /// emits, so changing it changes both and no test would notice while the client stopped being
    /// answered.
    /// </remarks>
    private const ushort ObservedScrollType = 0x0045;

    /// <summary>The captured frame decodes to the actor the run had already killed.</summary>
    [Fact]
    public void The_captured_confirmation_names_the_actor_the_run_killed()
    {
        var request = (BossDieCheckRequest)new BossDieCheckDecoder()
            .Decode(GameMessageMetadata.Empty, CapturedConfirmation)!;

        Assert.Equal(CapturedUserId, request.ReportedUserId);
        Assert.Equal(CapturedActorSequence, request.ReportedActorSequence);
    }

    /// <summary>
    /// A body too short to carry those two numbers is declined, and declining is not throwing.
    /// </summary>
    /// <remarks>
    /// The distinction is the run. A decoder that throws is rethrown by the frame pipeline and the
    /// connection goes with it; on a frame that arrives at settlement that costs the player the
    /// dungeon they just cleared. Declining drops this frame alone. Asserting the exception would
    /// have locked in exactly the behaviour AC-005 forbids.
    /// </remarks>
    [Fact]
    public void A_confirmation_too_short_to_carry_its_two_numbers_is_declined_not_thrown()
    {
        var decoded = Record.Exception(() =>
            Assert.Null(new BossDieCheckDecoder().Decode(GameMessageMetadata.Empty, new byte[3])));

        Assert.Null(decoded);
    }

    /// <summary>
    /// The confirmation has no way to be answered: its handler takes no sender.
    /// </summary>
    /// <remarks>
    /// The client registers a response handler for 671 command types and <c>0x0075</c> is not one
    /// of them, so a reply would be a frame with nowhere to go. Asserting that no frame went out
    /// would be theatre -- this handler could not send one if it wanted to -- so what is asserted
    /// is the reason: it does not take the seam that sends. That is what a "fix" for the unhandled
    /// warning would have to add first, and it is what this turns red.
    /// <para>
    /// The limit is worth naming: this proves the dependency is absent, not that no byte can
    /// possibly leave by some other route. Nothing in this codebase sends without that seam, and
    /// the review of this change is what covers the rest.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_confirmation_handler_cannot_answer_at_all()
    {
        var dependencies = typeof(BossDieCheckHandler)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(IGameMessageSender), dependencies);
    }

    /// <summary>
    /// The confirmation changes nothing about the run, however many times it arrives.
    /// </summary>
    /// <remarks>
    /// This is the whole reason it is safe to leave unguarded. The reference server clears the
    /// dungeon off this frame; doing that here would let a client clear a run by asserting it, and
    /// would make a replay a second settlement. Nothing but <see cref="IDungeonRunDomainService.Find"/>
    /// is reachable from this handler, and the fixture refuses every other member.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task The_confirmation_changes_nothing_about_the_run(int arrivals)
    {
        var runs = new OneRun(deadActor: CapturedActorSequence);

        await Confirm(runs, arrivals);

        Assert.Equal(arrivals, runs.TimesRead);
    }

    /// <summary>An actor this room never killed changes nothing either.</summary>
    /// <remarks>
    /// The disagreement is worth a log line and nothing else: the exit has already opened on the
    /// server's own accounting, and a frame naming an actor it does not recognise is not grounds
    /// to undo that.
    /// </remarks>
    [Fact]
    public async Task An_actor_this_room_never_killed_changes_nothing()
    {
        var runs = new OneRun(deadActor: 1);

        await Confirm(runs, arrivals: 1);

        Assert.Equal(1, runs.TimesRead);
    }

    /// <summary>A confirmation from a character in no run is ignored.</summary>
    [Fact]
    public async Task A_confirmation_from_a_character_in_no_run_is_ignored()
    {
        var runs = new OneRun(deadActor: CapturedActorSequence, inARun: false);

        await Confirm(runs, arrivals: 1);

        Assert.Equal(1, runs.TimesRead);
    }

    /// <summary>A confirmation on a connection with no published session never reaches the run.</summary>
    [Fact]
    public async Task A_confirmation_without_a_published_session_is_ignored()
    {
        var runs = new OneRun(deadActor: CapturedActorSequence);
        var handler = new BossDieCheckHandler(
            new NoSession(), runs, NullLogger<BossDieCheckHandler>.Instance);

        await handler.HandleAsync(
            Dispatch(),
            new BossDieCheckRequest(GameMessageMetadata.Empty, CapturedUserId, CapturedActorSequence),
            CancellationToken.None);

        Assert.Equal(0, runs.TimesRead);
    }

    /// <summary>The scroll's state is acknowledged, which is what moves the run off the screen.</summary>
    /// <remarks>
    /// The acknowledgement comes first and the card panel's layout follows it in the same delivery.
    /// The order is asserted rather than the count: the client's handler for the acknowledgement is
    /// what dismisses the result screen, so a layout ahead of it would be drawn over a screen still
    /// standing. What the layout itself contains is <c>Game/CardReward</c>'s to assert.
    /// </remarks>
    [Fact]
    public async Task The_scroll_state_is_acknowledged()
    {
        var sender = new RecordingSender();

        await Scroll(sender, new OneSession(Identity));

        Assert.IsType<ScoreScrollStateResponse>(sender.Sent[0]);
        Assert.IsType<CardSelectRightStateResponse>(sender.Sent[1]);
    }

    /// <summary>
    /// It is acknowledged even with no published session behind the connection.
    /// </summary>
    /// <remarks>
    /// The same choice <c>GIVEUP_GAME</c> makes. The scroll has already been drawn by the time this
    /// arrives and the client has no way to ask twice, so a refusal strands it exactly as sending
    /// nothing did. There is nothing to protect either: the answer grants nothing and reads
    /// nothing.
    /// </remarks>
    [Fact]
    public async Task The_scroll_state_is_acknowledged_without_a_published_session()
    {
        var sender = new RecordingSender();

        await Scroll(sender, new NoSession());

        Assert.IsType<ScoreScrollStateResponse>(sender.Sent[0]);
        Assert.IsType<CardSelectRightStateResponse>(sender.Sent[1]);
    }

    /// <summary>The acknowledgement goes out under the type the client's frame arrived on.</summary>
    [Fact]
    public void The_acknowledgement_carries_the_type_the_scroll_frame_arrived_on()
    {
        var frame = Assert.Single(
            new ScoreScrollStateResponseEncoder().Encode(new ScoreScrollStateResponse()));

        Assert.Equal(ObservedScrollType, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        Assert.Equal(0x01, frame[0]);
        Assert.Equal(new byte[] { 0x01 }, frame[GameServerFrameEncoder.HeaderLength..]);
    }

    private static async Task Confirm(OneRun runs, int arrivals)
    {
        var handler = new BossDieCheckHandler(
            new OneSession(Identity), runs, NullLogger<BossDieCheckHandler>.Instance);

        for (var arrival = 0; arrival < arrivals; arrival++)
        {
            await handler.HandleAsync(
                Dispatch(),
                new BossDieCheckRequest(
                    GameMessageMetadata.Empty, CapturedUserId, CapturedActorSequence),
                CancellationToken.None);
        }
    }

    private static async Task Scroll(RecordingSender sender, ISelectedSessionDomainService sessions)
    {
        var handler = new ScoreScrollStateHandler(
            sessions, sender, NullLogger<ScoreScrollStateHandler>.Instance);

        await handler.HandleAsync(
            Dispatch(),
            new ScoreScrollStateRequest(GameMessageMetadata.Empty),
            CancellationToken.None);
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

    /// <summary>
    /// A run in its boss room with one actor down, which answers reads and refuses writes.
    /// </summary>
    /// <remarks>
    /// Refusing rather than recording is what makes "changes nothing" checkable: a handler that
    /// grew a settle, a move or an end would throw here rather than quietly pass.
    /// </remarks>
    private sealed class OneRun(ushort deadActor, bool inARun = true) : IDungeonRunDomainService
    {
        /// <summary>How many times the run was looked at, which is all this handler may do.</summary>
        public int TimesRead { get; private set; }

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

        public DungeonRunState? Find(CharacterId characterId)
        {
            this.TimesRead++;
            return inARun
                ? new DungeonRunState(
                    Layout,
                    Cell,
                    CurrentRoomMapId: 55611,
                    VisitedCells: new HashSet<MazeCell> { Cell },
                    StartedAt: DateTimeOffset.UnixEpoch,
                    CurrentCellState: new DungeonCellState(
                        Cell,
                        MapId: 55611,
                        Revision: 1,
                        FirstActorSequence: deadActor,
                        Actors:
                        [
                            new DungeonCellActorState(
                                deadActor,
                                new DungeonRoomActor(1, 1, 0),
                                IsAiCharacter: false,
                                IsDead: true,
                                PendingLoot: []),
                        ],
                        GroundItems: []))
                : null;
        }

        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, byte difficulty) =>
            throw new NotSupportedException("The boss confirmation may only read the run.");

        /// <summary>Not exercised here: nothing in these tests kills anything.</summary>
        public IReadOnlyList<MonsterDrop> AllocateMonsterDrops(
            CharacterId characterId,
            int declaredDropCount,
            ushort actorSequence,
            IReadOnlyList<RolledDrop> rolled) => [];

        public bool End(CharacterId characterId) =>
            throw new NotSupportedException("The boss confirmation may only read the run.");

        public DungeonRunState? TryMove(CharacterId characterId, MazeCell target) =>
            throw new NotSupportedException("The boss confirmation may only read the run.");

        public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence) =>
            throw new NotSupportedException("The boss confirmation may only read the run.");

        public DungeonRunState? TrySettle(CharacterId characterId) =>
            throw new NotSupportedException("The boss confirmation may only read the run.");

        public DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort groundDropNumber) =>
            throw new NotSupportedException("The boss confirmation may only read the run.");

        public DungeonRunState? FixClearRewardCards(
            CharacterId characterId,
            ClearRewardCardFace free,
            ClearRewardCardFace paid,
            int? paidFlipCost) =>
            throw new NotSupportedException("The boss confirmation may only read the run.");

        public ClearRewardCardSlot? TryClaimClearRewardCard(
            CharacterId characterId, ClearRewardCardKind kind, int index) =>
            throw new NotSupportedException("The boss confirmation may only read the run.");
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
