using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.TownProjection;

/// <summary>
/// The two handlers that move a character: which area it walks into, and where it stands.
/// </summary>
/// <remarks>
/// Both ask the service that owns where a character is standing, and both report their movement to
/// it. The session snapshot still carries a copy of the position, but nothing refreshes that copy
/// any more -- it is whatever entry put there, for as long as the session lasts. So a handler that
/// went back to reading it would not throw and would not crash: it would quietly answer with the
/// place the character entered at. That is the mistake these two tests exist to catch, and it is
/// why the service here is arranged to hold a position that differs from the snapshot's in every
/// field. Reading the wrong one produces a recognisably wrong answer rather than coinciding with
/// the right one.
///
/// The real <see cref="CharacterPositionDomainService"/> is used rather than a double, because what
/// is under test is these handlers being wired to the thing that actually owns the position.
/// </remarks>
public sealed class TownProjectionHandlerTests
{
    /// <summary>
    /// Walking into another area moves the character the service owns, facing as they were.
    /// </summary>
    /// <remarks>
    /// The facing is the assertion that separates the two sources. This command does not carry one,
    /// so the direction the client is told belongs to whoever holds the character's position; the
    /// stale snapshot's copy still says whatever entry placed them at.
    /// </remarks>
    [Fact]
    public async Task A_crossing_moves_the_character_the_service_owns()
    {
        var positions = Standing();
        var presenceRegistry = Presence();
        var sender = new RecordingSender();
        var notifications = new RecordingNotifications();
        var sessions = new OneSession(Identity);
        var runtimes = new OneRuntimeSession(Entry());
        var handler = new SetUserAreaHandler(
            sessions,
            runtimes,
            positions,
            presenceRegistry,
            new OneChannel(),
            new EveryTownDeclared(),
            new SelectedSessionTransitionGate(),
            new TownAreaProjectionDelivery(
                presenceRegistry,
                sessions,
                runtimes,
                notifications,
                new NoopPartyTownProjectionDelivery(),
                sender,
                new EveryTownDeclared()),
            NullLogger<SetUserAreaHandler>.Instance);

        await handler.HandleAsync(
            Dispatch(),
            new SetUserAreaRequest(
                GameMessageMetadata.Empty, CrossedTownId, CrossedAreaId, CrossedX, CrossedY),
            CancellationToken.None);

        var area = sender.Sent.OfType<UserAreaNotification>().Single();
        Assert.Equal(CrossedAreaId, area.AreaId);
        Assert.Equal(CrossedTownId, area.Spawn.TownId);
        Assert.Equal(CrossedX, area.Spawn.X);
        Assert.Equal(CrossedY, area.Spawn.Y);

        var users = sender.Sent.OfType<AreaUsersNotification>().Single();
        Assert.Equal(CrossedTownId, users.TownId);
        Assert.Equal(CrossedAreaId, users.AreaId);
        var presence = Assert.Single(users.Users);
        Assert.Equal(CrossedX, presence.X);
        Assert.Equal(CrossedY, presence.Y);
        Assert.Equal(WalkedDirection, presence.Direction);

        Assert.Equal(
            new CharacterPositionState(
                CrossedTownId, CrossedAreaId, CrossedX, CrossedY, WalkedDirection),
            positions.Find(Identity.CharacterId));
    }

    [Fact]
    public async Task AreaCrossingWaitsForTheSameAccountPartyTransitionGate()
    {
        var positions = Standing();
        var gate = new SelectedSessionTransitionGate();
        var presence = Presence();
        var sender = new RecordingSender();
        var notifications = new RecordingNotifications();
        var sessions = new OneSession(Identity);
        var runtimes = new OneRuntimeSession(Entry());
        var held = await gate.AcquireAsync(
            Identity.AccountId,
            Dfo.Server.Application.Commands.UserAccessMode.Exclusive,
            CancellationToken.None);
        var handler = new SetUserAreaHandler(
            sessions,
            runtimes,
            positions,
            presence,
            new OneChannel(),
            new EveryTownDeclared(),
            gate,
            new TownAreaProjectionDelivery(
                presence,
                sessions,
                runtimes,
                notifications,
                new NoopPartyTownProjectionDelivery(),
                sender,
                new EveryTownDeclared()),
            NullLogger<SetUserAreaHandler>.Instance);

        var pending = handler.HandleAsync(
                Dispatch(),
                new SetUserAreaRequest(
                    GameMessageMetadata.Empty, CrossedTownId, CrossedAreaId, CrossedX, CrossedY),
                CancellationToken.None)
            .AsTask();

        Assert.False(pending.IsCompleted);
        Assert.Equal(WalkedAreaId, positions.Find(Identity.CharacterId)!.AreaId);

        await held.DisposeAsync();
        await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(CrossedAreaId, positions.Find(Identity.CharacterId)!.AreaId);
    }

    [Fact]
    public async Task Crossing_areas_removes_the_mover_from_the_old_peers_and_publishes_to_the_new_peers()
    {
        var oldPeer = PeerIdentity(61, "old");
        var secondOldPeer = PeerIdentity(65, "old-second");
        var newPeer = PeerIdentity(62, "new");
        var otherListenerOldPeer = PeerIdentity(63, "old-other-listener", listenerPort: 10011);
        var otherListenerNewPeer = PeerIdentity(64, "new-other-listener", listenerPort: 10011);
        var sessions = new SelectedSessionDomainService();
        sessions.Select(Identity);
        sessions.Select(oldPeer);
        sessions.Select(secondOldPeer);
        sessions.Select(newPeer);
        sessions.Select(otherListenerOldPeer);
        sessions.Select(otherListenerNewPeer);
        var runtimes = new SelectedSessionRuntimeDirectory();
        var currentViews = new FixedCurrentViews();
        runtimes.Publish(
            Identity,
            shouldPersistPosition: false);
        currentViews.Publish(Identity, EntryFor(Identity, WalkedTownId, WalkedAreaId, WalkedX));
        runtimes.Publish(
            oldPeer,
            shouldPersistPosition: false);
        currentViews.Publish(oldPeer, EntryFor(oldPeer, WalkedTownId, WalkedAreaId, x: 700));
        runtimes.Publish(
            secondOldPeer,
            shouldPersistPosition: false);
        currentViews.Publish(secondOldPeer, EntryFor(secondOldPeer, WalkedTownId, WalkedAreaId, x: 750));
        runtimes.Publish(
            newPeer,
            shouldPersistPosition: false);
        currentViews.Publish(newPeer, EntryFor(newPeer, CrossedTownId, CrossedAreaId, x: 800));
        runtimes.Publish(
            otherListenerOldPeer,
            shouldPersistPosition: false);
        currentViews.Publish(otherListenerOldPeer,
            EntryFor(otherListenerOldPeer, WalkedTownId, WalkedAreaId, x: 900));
        runtimes.Publish(
            otherListenerNewPeer,
            shouldPersistPosition: false);
        currentViews.Publish(otherListenerNewPeer,
            EntryFor(otherListenerNewPeer, CrossedTownId, CrossedAreaId, x: 950));
        var positions = Standing();
        positions.Place(
            oldPeer,
            new CharacterPositionState(
                WalkedTownId, WalkedAreaId, 700, 300, WalkedDirection));
        positions.Place(
            secondOldPeer,
            new CharacterPositionState(
                WalkedTownId, WalkedAreaId, 750, 300, WalkedDirection));
        positions.Place(
            newPeer,
            new CharacterPositionState(
                CrossedTownId, CrossedAreaId, 800, 300, WalkedDirection));
        positions.Place(
            otherListenerOldPeer,
            new CharacterPositionState(
                WalkedTownId, WalkedAreaId, 900, 300, WalkedDirection));
        positions.Place(
            otherListenerNewPeer,
            new CharacterPositionState(
                CrossedTownId, CrossedAreaId, 950, 300, WalkedDirection));
        var presence = Presence();
        presence.Admit(new SessionPresence(
            oldPeer.CharacterId,
            oldPeer.ListenerPort,
            WalkedTownId,
            WalkedAreaId,
            StandingInTown: true,
            oldPeer,
            ChannelId: 10));
        presence.Admit(new SessionPresence(
            secondOldPeer.CharacterId,
            secondOldPeer.ListenerPort,
            WalkedTownId,
            WalkedAreaId,
            StandingInTown: true,
            secondOldPeer,
            ChannelId: 10));
        presence.Admit(new SessionPresence(
            newPeer.CharacterId,
            newPeer.ListenerPort,
            CrossedTownId,
            CrossedAreaId,
            StandingInTown: true,
            newPeer,
            ChannelId: 10));
        presence.Admit(new SessionPresence(
            otherListenerOldPeer.CharacterId,
            otherListenerOldPeer.ListenerPort,
            WalkedTownId,
            WalkedAreaId,
            StandingInTown: true,
            otherListenerOldPeer,
            ChannelId: 99));
        presence.Admit(new SessionPresence(
            otherListenerNewPeer.CharacterId,
            otherListenerNewPeer.ListenerPort,
            CrossedTownId,
            CrossedAreaId,
            StandingInTown: true,
            otherListenerNewPeer,
            ChannelId: 99));
        var order = new List<string>();
        var sender = new RecordingSender(order);
        var notifications = new RecordingNotifications(order);
        var partyProjection = new RecordingPartyTownProjectionDelivery(order);
        var handler = new SetUserAreaHandler(
            sessions,
            currentViews,
            positions,
            presence,
            new OneChannel(),
            new EveryTownDeclared(),
            new SelectedSessionTransitionGate(),
            new TownAreaProjectionDelivery(
                presence,
                sessions,
                currentViews,
                notifications,
                partyProjection,
                sender,
                new EveryTownDeclared()),
            NullLogger<SetUserAreaHandler>.Instance);

        await handler.HandleAsync(
            Dispatch(),
            new SetUserAreaRequest(
                GameMessageMetadata.Empty, CrossedTownId, CrossedAreaId, CrossedX, CrossedY),
            CancellationToken.None);

        Assert.Collection(
            notifications.Published,
            item =>
            {
                Assert.Equal(
                    [oldPeer.CharacterId],
                    Assert.IsType<NotificationAudience.TheseSessions>(item.Audience).Sessions
                        .Select(value => value.CharacterId));
                var roster = Assert.IsType<TownAreaRosterReconciled>(item.Notification);
                Assert.Equal(WalkedTownId, roster.TownId);
                Assert.Equal(WalkedAreaId, roster.AreaId);
                Assert.Equal(
                    [oldPeer.WireUserId, secondOldPeer.WireUserId],
                    roster.RemainingUsers.Select(user => user.WireUserId));
            },
            item =>
            {
                Assert.Equal(
                    [secondOldPeer.CharacterId],
                    Assert.IsType<NotificationAudience.TheseSessions>(item.Audience).Sessions
                        .Select(value => value.CharacterId));
                var roster = Assert.IsType<TownAreaRosterReconciled>(item.Notification);
                Assert.Equal(WalkedTownId, roster.TownId);
                Assert.Equal(WalkedAreaId, roster.AreaId);
                Assert.Equal(
                    [secondOldPeer.WireUserId, oldPeer.WireUserId],
                    roster.RemainingUsers.Select(user => user.WireUserId));
            },
            item =>
            {
                Assert.Equal(
                    [newPeer.CharacterId],
                    Assert.IsType<NotificationAudience.TheseSessions>(item.Audience).Sessions
                        .Select(value => value.CharacterId));
                var published = Assert.IsType<TownParticipantPublished>(item.Notification);
                Assert.Equal(Identity.CharacterId, published.State.Newcomer.Character.Identity.CharacterId);
                Assert.Equal(
                    newPeer.CharacterId,
                    Assert.Single(published.State.ExistingCharacters).Character.Identity.CharacterId);
            });
        Assert.Collection(
            sender.Sent,
            message => Assert.IsType<UserAreaNotification>(message),
            message => Assert.IsType<CurrentUserStateMessage>(message),
            message => Assert.IsType<UserVisibilityMessage>(message),
            message => Assert.IsType<UserAreaNotification>(message),
            message => Assert.IsType<AreaUsersNotification>(message));
        Assert.Equal(
            [
                "town-direct",
                "notify:TownAreaRosterReconciled",
                "notify:TownAreaRosterReconciled",
                "notify:TownParticipantPublished",
                "party:62<-60",
                "party:60<-62",
                "party-move:60@10:3:6",
            ],
            order);
    }

    /// <summary>
    /// A step moves the character the service owns, in the area the service says they are in.
    /// </summary>
    /// <remarks>
    /// The answer to this frame is the client's own report handed back, so it cannot tell the two
    /// sources apart. The town and area can: this command carries neither, so they come from
    /// wherever the step is applied, and the snapshot's copy is still the town the character
    /// entered in rather than the one they have since crossed into.
    /// </remarks>
    [Fact]
    public async Task A_step_moves_the_character_the_service_owns()
    {
        var positions = Standing();
        var presence = Presence();
        var sender = new RecordingSender();
        var notifications = new RecordingNotifications();
        var sessions = new OneSession(Identity);
        var runtimes = new OneRuntimeSession(Entry());
        var handler = new SetUserPositionHandler(
            sessions,
            runtimes,
            positions,
            presence,
            new TownAreaProjectionDelivery(
                presence,
                sessions,
                runtimes,
                notifications,
                new NoopPartyTownProjectionDelivery(),
                sender,
                new EveryTownDeclared()),
            NullLogger<SetUserPositionHandler>.Instance);

        await handler.HandleAsync(
            Dispatch(),
            new SetUserPositionRequest(
                GameMessageMetadata.Empty, SteppedX, SteppedY, SteppedDirection, Trailer),
            CancellationToken.None);

        Assert.Equal(
            new CharacterPositionState(
                WalkedTownId, WalkedAreaId, SteppedX, SteppedY, SteppedDirection),
            positions.Find(Identity.CharacterId));

        var answer = Assert.IsType<UserPositionNotification>(Assert.Single(sender.Sent));
        Assert.Equal(SteppedX, answer.X);
        Assert.Equal(SteppedY, answer.Y);
        Assert.Equal(SteppedDirection, answer.Direction);
        Assert.Equal(Trailer, answer.Trailer);
    }

    [Fact]
    public async Task A_step_is_published_to_the_other_characters_in_the_area()
    {
        var positions = Standing();
        var presence = Presence();
        var peer = PeerIdentity(61, "peer");
        var otherListenerPeer = PeerIdentity(62, "other-listener", listenerPort: 10011);
        presence.Admit(new SessionPresence(
            peer.CharacterId,
            peer.ListenerPort,
            WalkedTownId,
            WalkedAreaId,
            StandingInTown: true,
            peer,
            ChannelId: 10));
        presence.Admit(new SessionPresence(
            otherListenerPeer.CharacterId,
            otherListenerPeer.ListenerPort,
            WalkedTownId,
            WalkedAreaId,
            StandingInTown: true,
            otherListenerPeer,
            ChannelId: 99));
        var sessions = new SelectedSessionDomainService();
        sessions.Select(Identity);
        sessions.Select(peer);
        sessions.Select(otherListenerPeer);
        var notifications = new RecordingNotifications();
        var sender = new RecordingSender();
        var runtimes = new OneRuntimeSession(Entry());
        var handler = new SetUserPositionHandler(
            sessions,
            runtimes,
            positions,
            presence,
            new TownAreaProjectionDelivery(
                presence,
                sessions,
                runtimes,
                notifications,
                new NoopPartyTownProjectionDelivery(),
                sender,
                new EveryTownDeclared()),
            NullLogger<SetUserPositionHandler>.Instance);

        await handler.HandleAsync(
            Dispatch(),
            new SetUserPositionRequest(
                GameMessageMetadata.Empty, SteppedX, SteppedY, SteppedDirection, Trailer),
            CancellationToken.None);

        var (audience, notification) = Assert.Single(notifications.Published);
        Assert.Equal(
            [new CharacterId(61)],
            Assert.IsType<NotificationAudience.TheseSessions>(audience).Sessions
                .Select(value => value.CharacterId));
        var changed = Assert.IsType<TownPositionChanged>(notification);
        Assert.Equal(Identity.WireUserId, changed.WireUserId);
        Assert.Equal(SteppedX, changed.X);
        Assert.Equal(SteppedY, changed.Y);
        Assert.Equal(SteppedDirection, changed.Direction);
    }

    [Fact]
    public async Task A_step_does_not_follow_a_stale_presence_to_a_replacement_listener()
    {
        var positions = Standing();
        var presence = Presence();
        var oldPeer = PeerIdentity(61, "peer-old");
        presence.Admit(new SessionPresence(
            oldPeer.CharacterId,
            oldPeer.ListenerPort,
            WalkedTownId,
            WalkedAreaId,
            StandingInTown: true,
            oldPeer,
            ChannelId: 10));
        var sessions = new SelectedSessionDomainService();
        sessions.Select(Identity);
        sessions.Select(oldPeer);
        _ = sessions.Select(oldPeer with
        {
            ConnectionId = new NetworkConnectionId("peer-replacement"),
            ConnectionGeneration = 2,
            ListenerPort = 10011,
        });
        var notifications = new RecordingNotifications();
        var sender = new RecordingSender();
        var runtimes = new OneRuntimeSession(Entry());
        var handler = new SetUserPositionHandler(
            sessions,
            runtimes,
            positions,
            presence,
            new TownAreaProjectionDelivery(
                presence,
                sessions,
                runtimes,
                notifications,
                new NoopPartyTownProjectionDelivery(),
                sender,
                new EveryTownDeclared()),
            NullLogger<SetUserPositionHandler>.Instance);

        await handler.HandleAsync(
            Dispatch(),
            new SetUserPositionRequest(
                GameMessageMetadata.Empty, SteppedX, SteppedY, SteppedDirection, Trailer),
            CancellationToken.None);

        Assert.Empty(notifications.Published);
    }

    /// <summary>Where entry placed them, which is the copy the session snapshot keeps.</summary>
    private const byte PlacedTownId = 9;
    private const byte PlacedAreaId = 8;
    private const short PlacedX = 77;
    private const short PlacedY = 66;
    private const byte PlacedDirection = 4;

    /// <summary>Where they have since walked to, which only the service knows.</summary>
    private const byte WalkedTownId = 3;
    private const byte WalkedAreaId = 6;
    private const short WalkedX = 902;
    private const short WalkedY = 388;
    private const byte WalkedDirection = 7;

    /// <summary>The area the crossing under test asks for.</summary>
    private const byte CrossedTownId = 5;
    private const byte CrossedAreaId = 2;
    private const short CrossedX = 100;
    private const short CrossedY = 200;

    /// <summary>The step under test.</summary>
    private const short SteppedX = 410;
    private const short SteppedY = 275;
    private const byte SteppedDirection = 5;
    private const ushort Trailer = 104;

    /// <summary>The area state the channel's spawn carries, which the crossing reuses.</summary>
    private const byte SpawnAreaState = 7;

    /// <summary>
    /// The service, holding a character who entered in one town and has since crossed and walked.
    /// </summary>
    /// <remarks>
    /// Built by reporting movement rather than by placing the answer directly, because that is how
    /// the position gets there in production: entry places it once and every later frame reports a
    /// move. It is also what leaves the snapshot behind -- the copy entry took is never revisited.
    /// </remarks>
    private static CharacterPositionDomainService Standing()
    {
        var positions = new CharacterPositionDomainService();
        positions.Place(
            Identity,
            new CharacterPositionState(
                PlacedTownId, PlacedAreaId, PlacedX, PlacedY, PlacedDirection));
        positions.CrossInto(Identity, WalkedTownId, WalkedAreaId, 500, 400);
        positions.StepTo(Identity, WalkedX, WalkedY, WalkedDirection);
        return positions;
    }

    private static SessionPresenceDomainService Presence()
    {
        var presence = new SessionPresenceDomainService();
        presence.Admit(new SessionPresence(
            Identity.CharacterId,
            Identity.ListenerPort,
            WalkedTownId,
            WalkedAreaId,
            StandingInTown: true,
            Identity,
            ChannelId: 10));
        return presence;
    }

    /// <summary>
    /// A published session whose copy of the position is the one entry took, and stale.
    /// </summary>
    private static SelectCharacterResult Entry()
    {
        var result = SelectCharacterPayloadTestData.Create();
        return result with
        {
            Character = result.Character with
            {
                Position = new CharacterPositionState(
                    PlacedTownId, PlacedAreaId, PlacedX, PlacedY, PlacedDirection),
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

    private static SelectCharacterResult EntryFor(
        SelectedSessionIdentity identity,
        byte townId,
        byte areaId,
        short x)
    {
        var result = SelectCharacterPayloadTestData.CreateComplete();
        var position = new CharacterPositionState(townId, areaId, x, 300, WalkedDirection);
        return result with
        {
            Character = result.Character with
            {
                Identity = result.Character.Identity with { CharacterId = identity.CharacterId },
                Position = position,
            },
            World = result.World with
            {
                Channel = result.World.Channel with
                {
                    Spawn = result.World.Channel.Spawn with
                    {
                        TownId = townId,
                        AreaId = areaId,
                        X = x,
                        Y = 300,
                        Direction = WalkedDirection,
                    },
                },
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

    private static SelectedSessionIdentity PeerIdentity(
        int characterId,
        string connection,
        int listenerPort = 10010) =>
        SelectedSessionIdentity.Create(
            new AccountId(characterId),
            new CharacterId(characterId),
            new NetworkConnectionId(connection),
            connectionGeneration: 1,
            listenerPort);

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

    /// <summary>
    /// The runtime session, which accepts a rewrite rather than refusing one.
    /// </summary>
    /// <remarks>
    /// Deliberately permissive. A fixture that threw here would fail a handler that wrote the
    /// position back into the snapshot for the wrong reason -- with a fixture's exception instead
    /// of a wrong coordinate -- and would say nothing about which position the handler used.
    /// </remarks>
    private sealed class OneRuntimeSession(SelectCharacterResult entry)
        : ILegacySessionSnapshotFixture
    {
        public SelectCharacterResult Current { get; private set; } = entry;

        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            result = this.Current;
            shouldPersistPosition = false;
            return true;
        }

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) => this.Current;

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            this.Current = result;

        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result, bool persist) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool RemoveExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class OneChannel : IChannelEntryPolicy
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(
            int listenerPort, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                new ChannelEntryIdentity(10, listenerPort, ChannelEntryKind.General));

        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) =>
            throw new NotSupportedException("Crossing an area does not re-admit anyone.");
    }

    /// <summary>Content that declares every area and a channel that serves every town.</summary>
    private sealed class EveryTownDeclared : ITownMovementDomainService
    {
        public ValueTask<bool> DeclaresAreaAsync(
            int townId, int areaId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> CanEnterTownAsync(
            ChannelEntryIdentity channel, int townId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> SharesTownPresenceAsync(
            int townId, int areaId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(areaId != 0);
    }

    private sealed class RecordingSender(List<string>? order = null) : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Neither command answers on the CMD side.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken)
        {
            this.Sent.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Sent.AddRange(delivery.RequiredDirectMessages);
            order?.Add("town-direct");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingNotifications(List<string>? order = null) : INotificationBus
    {
        public List<(NotificationAudience Audience, object Notification)> Published { get; } = [];

        public ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification>
        {
            Published.Add((audience, notification));
            order?.Add($"notify:{notification.GetType().Name}");
            var attempts = audience is NotificationAudience.TheseSessions exact
                ? exact.Sessions.Select(session =>
                    new NotificationAttempt(session.CharacterId, NotificationOutcome.Delivered))
                    .ToArray()
                : [];
            return ValueTask.FromResult(new NotificationDelivery(attempts));
        }
    }

    private sealed class RecordingPartyTownProjectionDelivery(List<string> order)
        : IPartyTownProjectionDelivery
    {
        public ValueTask RefreshMovedPartyAsync(
            SelectedSessionIdentity mover,
            Dfo.Server.Data.Parties.PartyDirectoryScope? previousScope,
            CancellationToken cancellationToken)
        {
            order.Add(
                $"party-move:{mover.CharacterId.Value}@{previousScope?.ChannelId}:"
                + $"{previousScope?.TownId}:{previousScope?.AreaId}");
            return ValueTask.CompletedTask;
        }

        public ValueTask RefreshObservedPartiesAsync(
            IReadOnlyList<SelectedSessionIdentity> recipients,
            IReadOnlyList<SelectedSessionIdentity> representedSessions,
            CancellationToken cancellationToken)
        {
            order.Add(
                $"party:{string.Join(',', recipients.Select(value => value.CharacterId.Value))}"
                + $"<-{string.Join(',', representedSessions.Select(value => value.CharacterId.Value))}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopPartyTownProjectionDelivery : IPartyTownProjectionDelivery
    {
        public ValueTask RefreshMovedPartyAsync(
            SelectedSessionIdentity mover,
            Dfo.Server.Data.Parties.PartyDirectoryScope? previousScope,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RefreshObservedPartiesAsync(
            IReadOnlyList<SelectedSessionIdentity> recipients,
            IReadOnlyList<SelectedSessionIdentity> representedSessions,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
