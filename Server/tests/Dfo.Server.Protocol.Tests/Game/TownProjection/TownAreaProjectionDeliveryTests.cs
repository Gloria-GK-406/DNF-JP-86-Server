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
        var returningResult = Entry(returning, x: 100);
        var existingResult = Entry(existing, x: 300);
        runtimes.Publish(returning, returningResult, false);
        runtimes.Publish(existing, existingResult, false);
        var areas = new SessionPresenceDomainService();
        var key = new TownAreaKey(11, 3, 6);
        ((ITownAreaDomainService)areas).Enter(Member(existing, key, x: 300));
        var notifications = new RecordingNotificationBus();
        var sender = new RecordingSender();
        var delivery = new TownAreaProjectionDelivery(
            areas,
            sessions,
            runtimes,
            notifications,
            new NoopPartyProjection(),
            sender);

        var admission = delivery.TryAdmitForTownReturn(
            returning,
            returningResult,
            new CharacterPositionState(3, 6, 100, 200, 1));
        Assert.NotNull(admission);
        Assert.True(await delivery.SendTownReturnAsync(
            admission,
            [],
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
        var firstResult = Entry(first, 100);
        var secondResult = Entry(second, 200);
        var existingResult = Entry(existing, 300);
        runtimes.Publish(first, firstResult, false);
        runtimes.Publish(second, secondResult, false);
        runtimes.Publish(existing, existingResult, false);
        var areas = new SessionPresenceDomainService();
        var key = new TownAreaKey(11, 3, 6);
        ((ITownAreaDomainService)areas).Enter(Member(existing, key, 300));
        var sender = new RecordingSender();
        var delivery = new TownAreaProjectionDelivery(
            areas,
            sessions,
            runtimes,
            new RecordingNotificationBus(),
            new NoopPartyProjection(),
            sender);
        var firstAdmission = delivery.TryAdmitForTownReturn(
            first, firstResult, new CharacterPositionState(3, 6, 100, 200, 1));
        var secondAdmission = delivery.TryAdmitForTownReturn(
            second, secondResult, new CharacterPositionState(3, 6, 200, 200, 1));

        Assert.True(await delivery.SendTownReturnBatchAsync(
            first.ConnectionId,
            first,
            [Assert.IsType<TownReturnAdmission>(firstAdmission),
             Assert.IsType<TownReturnAdmission>(secondAdmission)],
            [],
            CancellationToken.None));

        var batch = Assert.Single(sender.Deliveries).Delivery;
        Assert.Equal(
            [second.CharacterId, existing.CharacterId],
            batch.RequiredDirectMessages.OfType<CurrentUserStateMessage>()
                .Select(message => message.Character.Identity.CharacterId));
        var secondFrames = Assert.Single(batch.BestEffortTargets).OrderedMessages;
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
        runtimes.Publish(first, Entry(first, 100), false);
        runtimes.Publish(second, Entry(second, 200), false);
        var areas = new SessionPresenceDomainService();
        var store = (ITownAreaDomainService)areas;
        var key = new TownAreaKey(11, 3, 6);
        var oldSnapshot = store.Enter(Member(first, key, 100)).CurrentArea!;
        store.Enter(Member(second, key, 200));
        var notifications = new RecordingNotificationBus();
        var delivery = new TownAreaProjectionDelivery(
            areas,
            sessions,
            runtimes,
            notifications,
            new NoopPartyProjection(),
            new RecordingSender());

        await delivery.PublishRosterAsync(oldSnapshot, CancellationToken.None);

        var rosters = notifications.Publications
            .Select(publication => publication.Notification)
            .OfType<TownAreaRosterReconciled>()
            .ToArray();
        Assert.Equal(2, rosters.Length);
        Assert.All(rosters, roster => Assert.Equal(2, roster.RemainingUsers.Count));
    }

    [Fact]
    public void Late_old_session_cannot_replace_the_current_area_lease()
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
        var replacementResult = Entry(replacement, 300);
        runtimes.Publish(replacement, replacementResult, false);
        var areas = new SessionPresenceDomainService();
        var store = (ITownAreaDomainService)areas;
        var key = new TownAreaKey(11, 3, 6);
        store.Enter(Member(replacement, key, 300));
        var delivery = new TownAreaProjectionDelivery(
            areas,
            sessions,
            runtimes,
            new RecordingNotificationBus(),
            new NoopPartyProjection(),
            new RecordingSender());

        var admission = delivery.TryAdmitForTownReturn(
            old,
            Entry(old, 100),
            new CharacterPositionState(3, 6, 100, 200, 1));

        Assert.Null(admission);
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

    private sealed class RecordingNotificationBus : INotificationBus
    {
        public List<Publication> Publications { get; } = [];

        public ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification>
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
