using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.TownProjection;

public sealed class TownAreaProjectionDeliveryTests
{
    [Fact]
    public async Task Returning_to_town_receives_existing_players_and_publishes_itself_to_them()
    {
        var returning = Session(1, "returning");
        var existing = Session(2, "existing");
        var sessions = new SelectedSessionDomainService();
        sessions.Select(returning);
        sessions.Select(existing);
        var runtimes = new SelectedSessionRuntimeDirectory();
        var currentViews = new FixedCurrentViews();
        var returningResult = Entry(returning, x: 100);
        var existingResult = Entry(existing, x: 300);
        runtimes.Publish(returning, false);
        runtimes.Publish(existing, false);
        currentViews.Publish(returning, returningResult);
        currentViews.Publish(existing, existingResult);
        var areas = new SessionPresenceDomainService();
        var key = new TownAreaKey(11, 3, 6);
        ((ITownAreaDomainService)areas).Enter(Member(existing, key, x: 300));
        var notifications = new RecordingNotificationBus();
        var sender = new RecordingSender();
        var delivery = new TownAreaProjectionDelivery(
            areas,
            sessions,
            currentViews,
            notifications,
            new NoopPartyProjection(),
            sender,
            SharedTownMovementDomainService.Instance);

        Assert.Equal(1, await delivery.EnterAsync(
            [new TownAreaEntry(
                returning,
                returningResult,
                new CharacterPositionState(3, 6, 100, 200, 1))],
            CancellationToken.None));
        var frames = Assert.Single(sender.Deliveries).Delivery.RequiredDirectMessages;

        Assert.Contains(
            frames.OfType<CurrentUserStateMessage>(),
            frame => frame.Character.Identity.CharacterId == existing.CharacterId);
        var publication = Assert.Single(notifications.Publications);
        Assert.Equal(
            [existing],
            Assert.IsType<NotificationAudience.TheseSessions>(publication.Audience).Sessions);
        var arrival = Assert.IsType<TownParticipantPublished>(publication.Notification);
        Assert.Equal(returning.CharacterId, arrival.State.Newcomer.Character.Identity.CharacterId);
        Assert.Equal(existing.CharacterId,
            Assert.Single(arrival.State.ExistingCharacters).Character.Identity.CharacterId);
        Assert.Equal(
            [returning.CharacterId, existing.CharacterId],
            ((ITownAreaDomainService)areas).Find(key)!.Members
                .Select(member => member.CharacterId));
    }

    [Fact]
    public async Task Returning_to_a_shared_area_does_not_inherit_the_hidden_login_gate_policy()
    {
        var returning = Session(1, "returning");
        var existing = Session(2, "existing");
        var sessions = new SelectedSessionDomainService();
        sessions.Select(returning);
        sessions.Select(existing);
        var runtimes = new SelectedSessionRuntimeDirectory();
        var currentViews = new FixedCurrentViews();
        var returningBaseline = Entry(returning, x: 100);
        var returningResult = returningBaseline with
        {
            World = returningBaseline.World with
            {
                Channel = returningBaseline.World.Channel with
                {
                    Spawn = returningBaseline.World.Channel.Spawn with
                    {
                        SharesTownPresence = false,
                    },
                },
            },
        };
        var existingResult = Entry(existing, x: 300);
        runtimes.Publish(returning, false);
        runtimes.Publish(existing, false);
        currentViews.Publish(returning, returningResult);
        currentViews.Publish(existing, existingResult);
        var areas = new SessionPresenceDomainService();
        var key = new TownAreaKey(11, 3, 6);
        ((ITownAreaDomainService)areas).Enter(Member(existing, key, x: 300));
        var notifications = new RecordingNotificationBus();
        var sender = new RecordingSender();
        var delivery = new TownAreaProjectionDelivery(
            areas,
            sessions,
            currentViews,
            notifications,
            new NoopPartyProjection(),
            sender,
            towns: new SharedTownMovement());

        Assert.Equal(1, await delivery.EnterAsync(
            [new TownAreaEntry(
                returning,
                returningResult,
                new CharacterPositionState(3, 6, 100, 200, 1))],
            CancellationToken.None));
        var frames = Assert.Single(sender.Deliveries).Delivery.RequiredDirectMessages;

        Assert.Contains(
            frames.OfType<CurrentUserStateMessage>(),
            frame => frame.Character.Identity.CharacterId == existing.CharacterId);
        Assert.IsType<TownParticipantPublished>(Assert.Single(notifications.Publications).Notification);
    }

    [Fact]
    public async Task Batch_return_builds_every_direct_roster_from_the_final_area_state()
    {
        var first = Session(1, "first");
        var second = Session(2, "second");
        var existing = Session(3, "existing");
        var sessions = new SelectedSessionDomainService();
        sessions.Select(first);
        sessions.Select(second);
        sessions.Select(existing);
        var runtimes = new SelectedSessionRuntimeDirectory();
        var currentViews = new FixedCurrentViews();
        var firstResult = Entry(first, 100);
        var secondResult = Entry(second, 200);
        var existingResult = Entry(existing, 300);
        runtimes.Publish(first, false);
        runtimes.Publish(second, false);
        runtimes.Publish(existing, false);
        currentViews.Publish(first, firstResult);
        currentViews.Publish(second, secondResult);
        currentViews.Publish(existing, existingResult);
        var areas = new SessionPresenceDomainService();
        var key = new TownAreaKey(11, 3, 6);
        ((ITownAreaDomainService)areas).Enter(Member(existing, key, 300));
        var sender = new RecordingSender();
        var delivery = new TownAreaProjectionDelivery(
            areas,
            sessions,
            currentViews,
            new RecordingNotificationBus(),
            new NoopPartyProjection(),
            sender,
            SharedTownMovementDomainService.Instance);
        Assert.Equal(2, await delivery.EnterAsync(
            [new TownAreaEntry(
                 first, firstResult, new CharacterPositionState(3, 6, 100, 200, 1)),
             new TownAreaEntry(
                 second, secondResult, new CharacterPositionState(3, 6, 200, 200, 1))],
            CancellationToken.None));

        var firstFrames = sender.Deliveries
            .Single(value => value.ConnectionId == first.ConnectionId)
            .Delivery.RequiredDirectMessages;
        Assert.Equal(
            [second.CharacterId, existing.CharacterId],
            firstFrames.OfType<CurrentUserStateMessage>()
                .Select(message => message.Character.Identity.CharacterId));
        var secondFrames = sender.Deliveries
            .Single(value => value.ConnectionId == second.ConnectionId)
            .Delivery.RequiredDirectMessages;
        Assert.Equal(
            [first.CharacterId, existing.CharacterId],
            secondFrames.OfType<CurrentUserStateMessage>()
                .Select(message => message.Character.Identity.CharacterId));
    }

    [Fact]
    public async Task Delayed_old_roster_projection_re_reads_the_latest_revision()
    {
        var first = Session(1, "first");
        var second = Session(2, "second");
        var sessions = new SelectedSessionDomainService();
        sessions.Select(first);
        sessions.Select(second);
        var runtimes = new SelectedSessionRuntimeDirectory();
        var currentViews = new FixedCurrentViews();
        runtimes.Publish(first, false);
        runtimes.Publish(second, false);
        currentViews.Publish(first, Entry(first, 100));
        currentViews.Publish(second, Entry(second, 200));
        var areas = new SessionPresenceDomainService();
        var store = (ITownAreaDomainService)areas;
        var key = new TownAreaKey(11, 3, 6);
        var oldSnapshot = store.Enter(Member(first, key, 100)).CurrentArea!;
        store.Enter(Member(second, key, 200));
        var notifications = new RecordingNotificationBus();
        var delivery = new TownAreaProjectionDelivery(
            areas,
            sessions,
            currentViews,
            notifications,
            new NoopPartyProjection(),
            new RecordingSender(),
            SharedTownMovementDomainService.Instance);

        await delivery.PublishRosterAsync(oldSnapshot, CancellationToken.None);

        var rosters = notifications.Publications
            .Select(publication => publication.Notification)
            .OfType<TownAreaRosterReconciled>()
            .ToArray();
        Assert.Equal(2, rosters.Length);
        Assert.All(rosters, roster => Assert.Equal(2, roster.RemainingUsers.Count));
        Assert.Equal(2, currentViews.LoadCount);
    }

    [Fact]
    public async Task Late_old_session_cannot_replace_the_current_area_lease()
    {
        var old = Session(1, "old");
        var replacement = SelectedSessionIdentity.Create(
            old.AccountId,
            old.CharacterId,
            new NetworkConnectionId("replacement"),
            2,
            10011);
        var sessions = new SelectedSessionDomainService();
        sessions.Select(old);
        sessions.Select(replacement);
        var runtimes = new SelectedSessionRuntimeDirectory();
        var currentViews = new FixedCurrentViews();
        var replacementResult = Entry(replacement, 300);
        runtimes.Publish(replacement, false);
        currentViews.Publish(replacement, replacementResult);
        var areas = new SessionPresenceDomainService();
        var store = (ITownAreaDomainService)areas;
        var key = new TownAreaKey(11, 3, 6);
        store.Enter(Member(replacement, key, 300));
        var delivery = new TownAreaProjectionDelivery(
            areas,
            sessions,
            currentViews,
            new RecordingNotificationBus(),
            new NoopPartyProjection(),
            new RecordingSender(),
            SharedTownMovementDomainService.Instance);

        Assert.Equal(0, await delivery.EnterAsync(
            [new TownAreaEntry(
                old,
                Entry(old, 100),
                new CharacterPositionState(3, 6, 100, 200, 1))],
            CancellationToken.None));
        Assert.Equal(replacement.TownAreaLease(), store.Find(old.CharacterId)!.Lease);
    }

    private static SelectedSessionIdentity Session(int id, string connection) =>
        SelectedSessionIdentity.Create(
            new AccountId(id),
            new CharacterId(id),
            new NetworkConnectionId(connection),
            1,
            10011);

    private static TownAreaMember Member(
        SelectedSessionIdentity session,
        TownAreaKey key,
        short x) =>
        new(
            session.CharacterId,
            session.TownAreaLease(),
            key,
            new CharacterPositionState(key.TownId, key.AreaId, x, 200, 1),
            true);

    private static SelectCharacterResult Entry(SelectedSessionIdentity session, short x)
    {
        var result = SelectCharacterPayloadTestData.CreateComplete();
        var position = new CharacterPositionState(3, 6, x, 200, 1);
        return result with
        {
            Character = result.Character with
            {
                Identity = result.Character.Identity with { CharacterId = session.CharacterId },
                Position = position,
            },
            World = result.World with
            {
                Channel = result.World.Channel with
                {
                    ChannelId = 11,
                    Spawn = result.World.Channel.Spawn with
                    {
                        TownId = 3,
                        AreaId = 6,
                        X = x,
                        Y = 200,
                        Direction = 1,
                        SharesTownPresence = true,
                    },
                },
            },
        };
    }

    private sealed record Publication(NotificationAudience Audience, object Notification);

    private sealed class RecordingNotificationBus : Dfo.Server.Testing.ProjectionEventService
    {
        public List<Publication> Publications { get; } = [];

        public override ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
        {
            Publications.Add(new Publication(audience, notification));
            return ValueTask.FromResult(new NotificationDelivery([]));
        }
    }

    private sealed class NoopPartyProjection : IPartyTownProjectionDelivery
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

    private sealed class SharedTownMovement : ITownMovementDomainService
    {
        public ValueTask<bool> DeclaresAreaAsync(
            int townId, int areaId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> SharesTownPresenceAsync(
            int townId, int areaId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> CanEnterTownAsync(
            Dfo.Server.Data.Channel.ChannelEntryIdentity channel,
            int townId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
    }

    private sealed record SentDelivery(
        NetworkConnectionId ConnectionId,
        GameExplicitMessageDelivery Delivery);

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<SentDelivery> Deliveries { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            Deliveries.Add(new SentDelivery(requiredDirectConnectionId, delivery));
            return ValueTask.CompletedTask;
        }
    }
}
