using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.RoomMechanisms;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

public sealed class RoomMechanismProjectionTests
{
    [Theory]
    [InlineData(0, DungeonRoomMechanismOutcome.Running)]
    [InlineData(3, DungeonRoomMechanismOutcome.Normal)]
    [InlineData(4, DungeonRoomMechanismOutcome.Crash)]
    public void Elevator_frame_is_NOTI_FE_with_exactly_two_state_bytes(byte stage, DungeonRoomMechanismOutcome outcome)
    {
        var frame = Assert.Single(new ElevatorStateEncoder().Encode(new ElevatorStateMessage(stage, outcome)));
        Assert.Equal(17, frame.Length);
        Assert.Equal(0, frame[0]);
        Assert.Equal(0x00FE, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(new byte[] { stage, (byte)outcome }, frame[15..]);
    }

    [Fact]
    public async Task Old_stage_room_or_session_cannot_overwrite_the_current_mechanism()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = new SelectedSessionIdentity(new(1), new(1), new("mechanism-original"), 1, 1, 10011);
            var sessions = new Sessions(session);
            var runs = new DungeonRunDomainService();
            var cell = new MazeCell(0, 0);
            var layout = new DungeonLayout(98765, 0, 1, 1, cell, cell, 12345, 9,
                new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(12345) });
            var definition = new DungeonRoomMechanismDefinition(DungeonRoomMechanismKind.Elevator, TimeSpan.FromSeconds(15), 4);
            var seed = new DungeonCellSeed(cell, 12345, 1,
                [new DungeonCellActorState(1, new(123, 1, 0), false, false, [])], []) { Mechanisms = [definition] };
            var run = runs.PreparePartyEntry([new(session.AccountId, session.CharacterId, 0, true)],
                layout, 0, seed, DungeonRunId.New(), DateTimeOffset.UtcNow).Run;
            runs.TryMarkParticipantReady(session.CharacterId, run.RunId, 0);
            var view = Assert.Single(runs.AdvanceRoomMechanisms(DateTimeOffset.UtcNow));
            var handler = new RoomMechanismNotificationHandler(runs, sessions);
            var recipient = new NotificationRecipient(session.CharacterId, session.ConnectionId, session);
            Assert.Single(await handler.ComposeAsync(recipient, new(view), default));
            Assert.Empty(await handler.ComposeAsync(recipient, new(view with { RoomVersion = 9 }), default));
            Assert.Empty(await handler.ComposeAsync(recipient, new(view with { RunId = DungeonRunId.New() }), default));
            var claim = Assert.IsType<DungeonActorDeathClaim>(runs.TryClaimActorDeath(session.CharacterId, 1));
            runs.CommitActorDeathChange(claim);
            Assert.Empty(await handler.ComposeAsync(recipient, new(view), default));
            var ended = Assert.Single(runs.AdvanceRoomMechanisms(DateTimeOffset.UtcNow));
            Assert.Single(await handler.ComposeAsync(recipient, new(ended), default));
            sessions.Current = session with { ConnectionId = new("mechanism-replacement") };
            Assert.Empty(await handler.ComposeAsync(recipient, new(ended), default));
            runs.End(session.CharacterId);
            Assert.Empty(await handler.ComposeAsync(recipient, new(ended), default));
        });
    }

    private sealed class Sessions(SelectedSessionIdentity current) : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

        public SelectedSessionIdentity Current = current;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId id) => Current.ConnectionId == id ? Current : null;
        public SelectedSessionIdentity? FindByCharacter(CharacterId id) => Current.CharacterId == id ? Current : null;
        public SelectedSessionIdentity? Find(AccountId id) => Current.AccountId == id ? Current : null;
        public SelectedSessionIdentity? FindByWireUserId(ushort id) => Current.WireUserId == id ? Current : null;
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }
}
