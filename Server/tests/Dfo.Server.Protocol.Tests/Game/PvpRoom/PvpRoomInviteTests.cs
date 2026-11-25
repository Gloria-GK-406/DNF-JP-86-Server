using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.PvpRoom;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.Party;
using Dfo.Server.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.PvpRoom;

/// <summary>
/// REQUEST_PEER / RESPONSE_PEER type 2 into a duel room, and the party side of the room/party exclusion
/// (Spec RQ-015, RQ-020–RQ-022; AC-013, AC-020 protocol side).
/// </summary>
public sealed class PvpRoomInviteTests
{
    [Fact]
    public Task An_invitation_sends_the_invitee_the_inviter_identity_then_the_type_two_request() => Run(async world =>
    {
        world.Rooms.NextInvite = new PvpInviteOutcome.Sent(
            new PvpInvitation(Guid.NewGuid(), new PvpRoomId(Guid.NewGuid()), world.A, world.B, DateTimeOffset.MaxValue),
            PvpRooms.Room(1, PvpBattleMode.Team, 0, seats: [(0, PvpRooms.Taken(world.A))]));

        Assert.True(await world.RequestAsync(world.A, world.B, 2, 64));

        Assert.Equal([nameof(IPvpRoomDomainService.InviteAsync)], world.Rooms.Calls);
        Assert.Equal(64, world.Rooms.LastPeerToken);
        Assert.Empty(world.Outbox.Sent);
        Assert.Collection(world.Projections.FramesFor(world.B),
            frame => Assert.IsType<PvpParticipantPublicationMessage>(frame),
            frame => Assert.Equal(new PvpRoomInviteRequestMessage(world.A.WireUserId, 64), frame));
        Assert.Empty(world.Projections.FramesFor(world.A));
    });

    [Theory]
    [InlineData(PvpRoomRefusal.Blacklisted, PartyRefusalCode.Refused)]
    [InlineData(PvpRoomRefusal.TargetInParty, PartyRefusalCode.AlreadyInParty)]
    [InlineData(PvpRoomRefusal.TargetSeated, PartyRefusalCode.Refused)]
    [InlineData(PvpRoomRefusal.TargetNotInLobby, PartyRefusalCode.Refused)]
    [InlineData(PvpRoomRefusal.NotWaiting, PartyRefusalCode.Refused)]
    [InlineData(PvpRoomRefusal.Full, PartyRefusalCode.PartyFullOrEmptySlot)]
    [InlineData(PvpRoomRefusal.NotSeated, PartyRefusalCode.Refused)]
    public Task A_refused_invitation_answers_the_inviter_with_type_two(PvpRoomRefusal refusal, byte reason) => Run(async world =>
    {
        world.Rooms.NextInvite = new PvpInviteOutcome.Refused(refusal);
        Assert.False(await world.RequestAsync(world.A, world.B, 2, 64));
        Assert.Equal(new RequestPeerRefusal(reason, 2), AssertSingleTo(world, world.A));
        Assert.Empty(world.Projections.Projected);
    });

    [Fact]
    public Task An_invitation_of_someone_offline_or_in_the_nine_byte_form_never_reaches_the_room_owner() => Run(async world =>
    {
        var offline = SelectedSessionIdentity.Create(new AccountId(77), new CharacterId(77), new NetworkConnectionId("x"), 1, PvpRooms.Port);
        Assert.False(await world.RequestAsync(world.A, offline, 2, 64));
        Assert.Equal(new RequestPeerRefusal(PartyRefusalCode.PeerLeftGame, 2), AssertSingleTo(world, world.A));
        world.Outbox.Sent.Clear();
        Assert.False(await world.RequestAsync(world.A, world.B, 2, 64, tail: [0, 0]));
        Assert.Equal(new RequestPeerRefusal(PartyRefusalCode.Refused, 2), AssertSingleTo(world, world.A));
        Assert.Empty(world.Rooms.Calls);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task A_party_invitation_from_or_to_a_seated_character_is_refused(bool inviterSeated) => Run(async world =>
    {
        world.Rooms.AlsoSeated.Add(inviterSeated ? world.A.CharacterId : world.B.CharacterId);
        Assert.False(await world.RequestAsync(world.A, world.B, 0, 0));
        Assert.Equal(new RequestPeerRefusal(PartyRefusalCode.Refused, 0), AssertSingleTo(world, world.A));
        Assert.Equal(PartyKeyKind.Virtual, world.Parties.KeyOf(world.A.CharacterId).Kind);
        Assert.Empty(world.Rooms.Calls);
    });

    [Fact]
    public Task Accepting_enters_the_room_as_an_entry_without_a_command_reply() => Run(async world =>
    {
        var before = PvpRooms.Room(1, PvpBattleMode.Team, 0, seats: [(0, PvpRooms.Taken(world.A))]);
        var after = before with { Seats = before.Seats.SetItem(1, PvpRooms.Taken(world.B, PvpTeam.Second)) };
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.InviteAccepted, before, after);

        Assert.True(await world.RespondAsync(world.B, world.A, 2, 64));

        Assert.Equal([nameof(IPvpRoomDomainService.AcceptInviteAsync)], world.Rooms.Calls);
        // The echoed peer token goes to the room owner, which answers a mismatch as no invitation.
        Assert.Equal(64, world.Rooms.LastPeerToken);
        var own = world.Outbox.Sent.Where(x => x.Connection == world.B.ConnectionId).Select(x => x.Message).ToArray();
        Assert.DoesNotContain(own, frame => frame is IGameCmdMessage);
        Assert.IsType<PvpSeatStateMessage>(own[^2]);
        Assert.Equal(PvpRoomProjector.RoomAreaId, Assert.IsType<UserAreaNotification>(own[^1]).AreaId);
        Assert.Contains(world.Projections.FramesFor(world.A), frame => frame is ExtendedUserStateMessage);
    });

    [Theory]
    [InlineData(PvpRoomRefusal.InviteExpired, PartyRefusalCode.Refused)]
    [InlineData(PvpRoomRefusal.InviteMissing, PartyRefusalCode.Refused)]
    [InlineData(PvpRoomRefusal.SessionNotCurrent, PartyRefusalCode.Refused)]
    [InlineData(PvpRoomRefusal.NotWaiting, PartyRefusalCode.Refused)]
    [InlineData(PvpRoomRefusal.InParty, PartyRefusalCode.Refused)]
    [InlineData(PvpRoomRefusal.Full, PartyRefusalCode.PartyFullOrEmptySlot)]
    public Task A_refused_acceptance_answers_with_type_two(PvpRoomRefusal refusal, byte reason) => Run(async world =>
    {
        world.Rooms.Next = new PvpRoomOutcome.Refused(refusal);
        Assert.False(await world.RespondAsync(world.B, world.A, 2, 64));
        Assert.Equal(new ResponsePeerRefusal(reason, 2), AssertSingleTo(world, world.B));
        Assert.Empty(world.Projections.Projected);
    });

    [Fact]
    public Task Declining_a_room_invitation_sends_nothing_and_changes_nothing() => Run(async world =>
    {
        Assert.False(await world.RespondAsync(world.B, world.A, 2, 64, tail: new byte[9]));
        Assert.Empty(world.Rooms.Calls);
        Assert.Empty(world.Outbox.Sent);
        Assert.Empty(world.Projections.Projected);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Accepting_a_party_invitation_while_either_side_is_seated_is_refused(bool inviteeSeated) => Run(async world =>
    {
        world.Rooms.AlsoSeated.Add(inviteeSeated ? world.B.CharacterId : world.A.CharacterId);
        Assert.False(await world.RespondAsync(world.B, world.A, 0, 0));
        Assert.Equal(new ResponsePeerRefusal(PartyRefusalCode.Refused, 0), AssertSingleTo(world, world.B));
        Assert.Equal(PartyKeyKind.Virtual, world.Parties.KeyOf(world.B.CharacterId).Kind);
    });

    private static IGameServerMessage AssertSingleTo(World world, SelectedSessionIdentity session)
    {
        var (connection, message) = Assert.Single(world.Outbox.Sent);
        Assert.Equal(session.ConnectionId, connection);
        return message;
    }

    private static Task Run(Func<World, Task> body) => TestGameplay.RunAsync(() => body(new World()));

    private sealed class World
    {
        private readonly PvpRoomCommandTests.World rooms = new();

        public SelectedSessionIdentity A => rooms.A;
        public SelectedSessionIdentity B => rooms.B;
        public ScriptedPvpRooms Rooms => rooms.Rooms;
        public RecordingCommandOutbox Outbox => rooms.Outbox;
        public RecordingProjections Projections => rooms.Projections;
        public IPartyService Parties { get; } = ProtocolAlgorithmFixture.Default<IPartyService>();

        public Task<bool> RequestAsync(SelectedSessionIdentity from, SelectedSessionIdentity to, byte type, int peer, byte[]? tail = null) =>
            SendAsync(new GameRequestPeerCommandHandler(rooms.Sessions, ProtocolAlgorithmFixture.Default<ITownAreaDomainService>(), Parties,
                    ProtocolAlgorithmFixture.Default<IPartyMemberStatusDomainService>(), Projections,
                    ProtocolAlgorithmFixture.Default<IPartyProjectionStateDomainService>(),
                    ProtocolAlgorithmFixture.Default<IOutboundOrdering>(), ProtocolAlgorithmFixture.Default<IDungeonRunQueryDomainService>(),
                    DefaultAnswers.Create<Dfo.Server.Abstractions.Raids.IAntonRaidDomainService>(), Outbox, Rooms, rooms.Views, ProtocolAlgorithmFixture.Default<ICommandWorkUnits>(),
                    NullLogger<GameRequestPeerCommandHandler>.Instance, ProtocolAlgorithmFixture.Default<Dfo.Server.Protocol.Game.AntonRaid.AntonRaidSquadProjection>()),
                new GameRequestPeerCommand(PvpRoomCommandTests.Dispatch(from),
                    new RequestPeerRequest(GameMessageMetadata.Empty, to.WireUserId, type, peer, tail ?? [])));

        public Task<bool> RespondAsync(SelectedSessionIdentity from, SelectedSessionIdentity inviter, byte type, int peer, byte[]? tail = null) =>
            SendAsync(new GameResponsePeerCommandHandler(rooms.Sessions, ProtocolAlgorithmFixture.Default<ITownAreaDomainService>(), Parties,
                    Projections, ProtocolAlgorithmFixture.Default<IPartyProjectionStateDomainService>(),
                    ProtocolAlgorithmFixture.Default<IOutboundOrdering>(), ProtocolAlgorithmFixture.Default<IDungeonRunQueryDomainService>(),
                    DefaultAnswers.Create<Dfo.Server.Abstractions.Raids.IAntonRaidDomainService>(), Outbox, Rooms, rooms.Lobby, rooms.Views, DefaultAnswers.Create<IOnlineCharacterRuntimeDomainService>(),
                    ProtocolAlgorithmFixture.NoOnline(), DefaultAnswers.Create<IGameSessionEndpointDomainService>(),
                    ProtocolAlgorithmFixture.Default<IWeaknessDomainService>(), ProtocolAlgorithmFixture.Default<ICommandWorkUnits>(),
                    NullLogger<GameResponsePeerCommandHandler>.Instance, ProtocolAlgorithmFixture.Default<Dfo.Server.Protocol.Game.AntonRaid.AntonRaidSquadProjection>(),
                    ProtocolAlgorithmFixture.Default<Dfo.Server.Abstractions.Pvp.IPvpTotalMatchCandidateQueryDomainService>(), TimeProvider.System),
                new GameResponsePeerCommand(PvpRoomCommandTests.Dispatch(from),
                    new ResponsePeerRequest(GameMessageMetadata.Empty, inviter.WireUserId, type, peer, tail ?? [])));

        private static async Task<bool> SendAsync<TCommand>(ICommandHandler<TCommand, bool> handler, TCommand command)
            where TCommand : class, IProtocolCommand<bool>
        {
            var request = new CommandRequestContext.ProtocolNonEffect(command.Dispatch, command.Message, command.Frame, () => true);
            return await handler.HandleAsync(new(Guid.NewGuid(), typeof(TCommand), handler.GetType()) { Request = request }, command, default);
        }
    }
}
