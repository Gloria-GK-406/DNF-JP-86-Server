using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Testing;
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
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.TownProjection;

public sealed class TownAreaProjectionDeliveryTests
{
    [Fact]
    public async Task Wedding_standing_forces_both_actors_on_each_church_client_including_their_own()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var bride = Session(1, "bride"); var groom = Session(2, "groom");
            var guest = Session(3, "guest"); var outside = Session(4, "outside");
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
            var areas = new SessionPresenceDomainService();
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, areas.Locations);
            var views = new FixedCurrentViews();
            foreach (var actor in new[] { bride, groom, guest, outside })
            {
                sessions.EnterWorldWithConnection(state, actor);
                var area = actor == outside ? (byte)6 : (byte)12;
                positions.Begin(actor, new(2, area, 100, 200, 5), 100);
                ((ITownAreaDomainService)areas).Enter(Member(actor, new(11, 2, area), 100));
            }
            var sender = new RecordingSender(); var notifications = new RecordingNotificationBus();
            var handler = ProtocolAlgorithmFixture.Create<GameMarriageCommandHandler>(sessions, views, positions, areas,
                SharedTownMovementDomainService.Instance, notifications, sender);
            var origin = new InternalCommandOrigin(Dfo.Server.Abstractions.Execution.GameplayCallSource.Timer,
                Dfo.Server.Abstractions.Execution.GameplayCallChain.NewRoot(Dfo.Server.Abstractions.Execution.GameplayCallSource.Timer), "test:wedding-standing");
            var request = new CommandRequestContext.TrustedInternal(origin);
            var context = new CommandExecutionContext(Guid.NewGuid(), typeof(AdvanceWeddingTickCommand), handler.GetType()) { Request = request };
            var stage = typeof(GameMarriageCommandHandler).GetMethod("WeddingPositionAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            try
            {
                await (ValueTask)stage.Invoke(handler, [context, new MarriageWeddingStanding(bride, new(2, 12, 670, 330, 5)), CancellationToken.None])!;
                await (ValueTask)stage.Invoke(handler, [context, new MarriageWeddingStanding(groom, new(2, 12, 685, 360, 5)), CancellationToken.None])!;
            }
            finally { await request.DrainOperationsAsync(); request.Close(); }
            var received = sender.Deliveries.SelectMany(sent =>
                sent.Delivery.RequiredDirectMessages.Select(message => (sent.ConnectionId, Message: message))
                    .Concat(sent.Delivery.BestEffortTargets.SelectMany(target => target.OrderedMessages.Select(message => (target.ConnectionId, Message: message)))))
                .ToArray();
            foreach (var recipient in new[] { bride, groom, guest })
            {
                var messages = received.Where(x => x.ConnectionId == recipient.ConnectionId).Select(x => x.Message).ToArray();
                Assert.Equal(2, messages.Length);
                Assert.All(messages, message => Assert.Equal((ServerMessageType)0x008A, message.MessageType));
                Assert.Equal(new[] { ((ushort)bride.WireUserId, (short)670, (short)330), ((ushort)groom.WireUserId, (short)685, (short)360) },
                    messages.Cast<ForcedTownPositionNotification>().Select(message => (message.UserId, message.X, message.Y)));
            }
            Assert.DoesNotContain(received, x => x.ConnectionId == outside.ConnectionId);
            Assert.Empty(notifications.Publications);
            Assert.Equal((short)670, ((ITownAreaDomainService)areas).Find(bride.CharacterId)!.Position.X);
            Assert.Equal((short)685, positions.PositionOf(groom.CharacterId)!.X);
        });
    }

    /// <summary>XO-7 (spec appendix D): both partners' standing positions are delivered before either cinema.</summary>
    [Fact]
    public async Task A_wedding_tick_delivers_both_positions_before_either_partners_cinema()
    {
        var bride = Session(1, "bride"); var groom = Session(2, "groom");
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(bride, groom);
        outbound.Transport.Slow(bride.ConnectionId, TimeSpan.FromMilliseconds(300));

        await outbound.RunAsync(async () =>
        {
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
            var areas = new SessionPresenceDomainService();
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, areas.Locations);
            var views = new FixedCurrentViews();
            foreach (var actor in new[] { bride, groom })
            {
                sessions.EnterWorldWithConnection(state, actor);
                positions.Begin(actor, new(2, 12, 100, 200, 5), 100);
                ((ITownAreaDomainService)areas).Enter(Member(actor, new(11, 2, 12), 100));
            }
            var delivery = global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.TownProjection.TownAreaProjectionDelivery>(areas, sessions, views, new RecordingNotificationBus(), new NoopPartyProjection(), outbound.Game, SharedTownMovementDomainService.Instance);
            var room = new MarriageChurchRoom(7, Guid.NewGuid(), 11, 2, 12, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
            var church = new MarriageChurchSnapshot(room, groom.CharacterId, [1], [2], new(2, 12, 670, 330));
            var ceremony = new MarriageCeremony(room.CeremonyId, Guid.NewGuid(), bride, groom, 1, 0,
                MarriageCeremonyState.Reserved, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
            var transition = new MarriageWeddingTransition(new MarriageWedding(ceremony, MarriageWeddingPhase.Cancelled, 0),
                [new MarriageWeddingAdmission(bride, church), new MarriageWeddingAdmission(groom, church)], [])
            {
                Standing = [new MarriageWeddingStanding(bride, new(2, 12, 670, 330, 5)), new MarriageWeddingStanding(groom, new(2, 12, 685, 360, 5))],
            };
            // Projection oracle with controlled committed transition; production timer/bus tests own lifecycle/FIFO.
            var handler = new GameMarriageCommandHandler(sessions,new OneWedding(ceremony.Id),ProtocolAlgorithmFixture.Default<IMarriageCoupleDomainService>(),ProtocolAlgorithmFixture.Default<IMarriageVenueDomainService>(),ProtocolAlgorithmFixture.Default<IQuestOfferRefreshDomainService>(),ProtocolAlgorithmFixture.Default<ICharacterDomainService>(),views,                positions,areas,ProtocolAlgorithmFixture.Default<IPartyService>(),new PartyProjectionStreamRegistry(),global::Dfo.Server.Testing.ProtocolAlgorithmFixture.QueryFor(sessions),                new WeddingTransitionUnits(transition),new global::Dfo.Server.Testing.TestCommandProjections(new RecordingNotificationBus()),                new Dfo.Server.Protocol.Tests.Support.GameOutboxFixture(outbound.Game),TimeProvider.System,                NullLogger<GameMarriageCommandHandler>.Instance,ordering: outbound.Ordering,housing: ProtocolAlgorithmFixture.Default<IMarriageHousingQueryDomainService>(),transit: ProtocolAlgorithmFixture.TownTransit(null, sessions, areas, positions, SharedTownMovementDomainService.Instance));
            var origin = new InternalCommandOrigin(Dfo.Server.Abstractions.Execution.GameplayCallSource.Timer,
                Dfo.Server.Abstractions.Execution.GameplayCallChain.NewRoot(Dfo.Server.Abstractions.Execution.GameplayCallSource.Timer), "test:wedding");
            var request = new CommandRequestContext.TrustedInternal(origin);
            try
            {
                await handler.HandleAsync(new(Guid.NewGuid(), typeof(AdvanceWeddingTickCommand), handler.GetType()) { Request = request }, new AdvanceWeddingTickCommand(), default);
            }
            finally { await request.DrainOperationsAsync(); request.Close(); }
        });
        await outbound.SettledAsync();

        var cinema = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == groom.ConnectionId
            && entry.Messages.Any(message => message is WeddingInfoMessage));
        Assert.Contains(outbound.Recorder.Entries.TakeWhile(entry => !ReferenceEquals(entry, cinema)),
            entry => entry.Connection.ConnectionId == bride.ConnectionId && entry.Messages.Any(message => message is ForcedTownPositionNotification));
        var cinemaAt = outbound.Transport.Delivered.ToList().LastIndexOf(groom.ConnectionId.Value);
        Assert.True(cinemaAt > outbound.Delivered(bride.ConnectionId), "a cinema was delivered before the bride's position");
    }

    private sealed class OneWedding(Guid ceremony) : Dfo.Server.Abstractions.Marriage.IMarriageQueryDomainService
    {
        public ValueTask<IReadOnlyList<Guid>> ActiveWeddingsAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<Guid>>([ceremony]);
        public ValueTask<MarriageEngagementSnapshot> SnapshotAsync(AccountId account, CharacterId character, CancellationToken ct) =>
            ValueTask.FromResult(MarriageEngagementSnapshot.Empty);
        public ValueTask<MarriageView?> ViewAsync(SelectedSessionIdentity actor, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<MarriageProposal?> ReceivedProposalAsync(CharacterId character, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<MarriageCeremony?> ReceivedReservationAsync(CharacterId character, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> IsMarriedAsync(Guid relation, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<MarriageWeddingPhase?> WeddingPhaseAsync(Guid ceremony, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<MarriageChurchSnapshot?> AppearanceAsync(SelectedSessionIdentity actor, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> CanCrossAsync(SelectedSessionIdentity actor, int channelId, byte townId, byte areaId, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> CanLeaveAsync(SelectedSessionIdentity actor, byte town, byte area, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ushort> RemainingInvitationsAsync(Guid ceremony, CharacterId actor, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<uint>> InvitedAsync(Guid ceremony, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AccountId>> CoupleAccountsAsync(SelectedSessionIdentity actor, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class WeddingTransitionUnits(MarriageWeddingTransition transition) : ICommandWorkUnits
    {
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull =>
            ValueTask.FromResult<CommandWriteResult<T>>(new CommandWriteResult<T>.Returned((T)(object)new MarriageWeddingAdvanceResult(transition)));
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
    }

    private sealed class UnusedQueue : Dfo.Server.Abstractions.Execution.IGameplayQueue
    {
        public ValueTask<T> RunAsync<T>(Dfo.Server.Abstractions.Execution.GameplayWork<T> work, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask RunAsync(Dfo.Server.Abstractions.Execution.GameplayWork work, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Wedding_entry_moves_both_partners_to_one_church_and_projects_their_presence()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1, "groom"); var second = Session(2, "bride");
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state); sessions.EnterWorldWithConnection(state, first); sessions.EnterWorldWithConnection(state, second);
            var views = new FixedCurrentViews(); views.Publish(first, Entry(first, 100)); views.Publish(second, Entry(second, 300));
            var areas = new SessionPresenceDomainService();
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, areas.Locations);
            foreach (var session in new[] { first, second })
            {
                positions.Begin(session, new(3, 6, 100, 200, 1), 100);
                ((ITownAreaDomainService)areas).Enter(Member(session, new(11, 3, 6), 100));
            }
            var notifications = new RecordingNotificationBus(); var sender = new RecordingSender();
            var projection = global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.TownProjection.TownAreaProjectionDelivery>(areas, sessions, views, notifications, new NoopPartyProjection(), sender, SharedTownMovementDomainService.Instance);
            var room = new MarriageChurchRoom(7, Guid.NewGuid(), 11, 2, 12, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
            var handler = MarriageChurchTestDriver.Create(sessions, sender, positions, areas, views, notifications, room);
            foreach (var session in new[] { first, second })
                await handler.HandleAsync(new NetworkDispatchContext(new(session.ConnectionId, NetworkEndpointKind.Game, 10011)),
                    new WeddingChurchEnterRequest(GameMessageMetadata.Empty, (uint)(session == first ? 2 : 1), room.AreaId, 0), default);
            Assert.Equal(new[] { first.CharacterId, second.CharacterId },
                ((ITownAreaDomainService)areas).Find(new TownAreaKey(11, 2, 12))!.Members.Select(x => x.CharacterId));
            Assert.Equal(12, positions.PositionOf(first.CharacterId)!.AreaId); Assert.Equal(12, positions.PositionOf(second.CharacterId)!.AreaId);
            Assert.Equal(2, sender.Deliveries.SelectMany(x => x.Delivery.RequiredDirectMessages).OfType<WeddingChurchEnterAck>().Count(x => x.Status == 0));
            Assert.Contains(sender.Deliveries.Where(x => x.ConnectionId == second.ConnectionId)
                .SelectMany(x => x.Delivery.RequiredDirectMessages).OfType<CurrentUserStateMessage>(),
                x => x.Character.Identity.CharacterId == first.CharacterId);
            Assert.Contains(notifications.Publications, x => x.Notification is TownParticipantPublished publication
                && publication.State.Newcomer.Character.Identity.CharacterId == second.CharacterId);
        });
    }

    [Fact]
    public async Task Returning_to_town_receives_existing_players_and_publishes_itself_to_them()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var returning = Session(1, "returning");
            var existing = Session(2, "existing");
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
            sessions.EnterWorldWithConnection(state, returning);
            sessions.EnterWorldWithConnection(state, existing);
            var currentViews = new FixedCurrentViews();
            var returningResult = Entry(returning, x: 100);
            var existingResult = Entry(existing, x: 300);
            currentViews.Publish(returning, returningResult);
            currentViews.Publish(existing, existingResult);
            var areas = new SessionPresenceDomainService();
            var key = new TownAreaKey(11, 3, 6);
            ((ITownAreaDomainService)areas).Enter(Member(existing, key, x: 300));
            AwayFrom(areas, returning, key);
            var notifications = new RecordingNotificationBus();
            var sender = new RecordingSender();
            var delivery = new TownArrivalProjectionDriver(areas, sessions, currentViews, notifications, new NoopPartyProjection(), sender, SharedTownMovementDomainService.Instance);

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
        });
    }

    [Fact]
    public async Task Returning_to_a_shared_area_does_not_inherit_the_hidden_login_gate_policy()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var returning = Session(1, "returning");
            var existing = Session(2, "existing");
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
            sessions.EnterWorldWithConnection(state, returning);
            sessions.EnterWorldWithConnection(state, existing);
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
            currentViews.Publish(returning, returningResult);
            currentViews.Publish(existing, existingResult);
            var areas = new SessionPresenceDomainService();
            var key = new TownAreaKey(11, 3, 6);
            ((ITownAreaDomainService)areas).Enter(Member(existing, key, x: 300));
            AwayFrom(areas, returning, key);
            var notifications = new RecordingNotificationBus();
            var sender = new RecordingSender();
            var delivery = new TownArrivalProjectionDriver(areas, sessions, currentViews, notifications, new NoopPartyProjection(), sender, new SharedTownMovement());

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
        });
    }

    [Fact]
    public async Task Batch_return_builds_every_direct_roster_from_the_final_area_state()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1, "first");
            var second = Session(2, "second");
            var existing = Session(3, "existing");
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
            sessions.EnterWorldWithConnection(state, first);
            sessions.EnterWorldWithConnection(state, second);
            sessions.EnterWorldWithConnection(state, existing);
            var currentViews = new FixedCurrentViews();
            var firstResult = Entry(first, 100);
            var secondResult = Entry(second, 200);
            var existingResult = Entry(existing, 300);
            currentViews.Publish(first, firstResult);
            currentViews.Publish(second, secondResult);
            currentViews.Publish(existing, existingResult);
            var areas = new SessionPresenceDomainService();
            var key = new TownAreaKey(11, 3, 6);
            ((ITownAreaDomainService)areas).Enter(Member(existing, key, 300));
            AwayFrom(areas, first, key);
            AwayFrom(areas, second, key);
            var sender = new RecordingSender();
            var delivery = new TownArrivalProjectionDriver(areas, sessions, currentViews, new RecordingNotificationBus(), new NoopPartyProjection(), sender, SharedTownMovementDomainService.Instance);
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
        });
    }

    [Fact]
    public async Task Delayed_old_roster_projection_re_reads_the_latest_revision()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1, "first");
            var second = Session(2, "second");
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
            sessions.EnterWorldWithConnection(state, first);
            sessions.EnterWorldWithConnection(state, second);
            var currentViews = new FixedCurrentViews();
            currentViews.Publish(first, Entry(first, 100));
            currentViews.Publish(second, Entry(second, 200));
            var areas = new SessionPresenceDomainService();
            var store = (ITownAreaDomainService)areas;
            var key = new TownAreaKey(11, 3, 6);
            var oldSnapshot = store.Enter(Member(first, key, 100)).CurrentArea!;
            store.Enter(Member(second, key, 200));
            var notifications = new RecordingNotificationBus();
            var delivery = global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.TownProjection.TownAreaProjectionDelivery>(areas, sessions, currentViews, notifications, new NoopPartyProjection(), new RecordingSender(), SharedTownMovementDomainService.Instance);

            await delivery.PublishRosterAsync(oldSnapshot, CancellationToken.None);

            var rosters = notifications.Publications
                .Select(publication => publication.Notification)
                .OfType<TownAreaRosterReconciled>()
                .ToArray();
            Assert.Equal(2, rosters.Length);
            Assert.All(rosters, roster => Assert.Equal(2, roster.RemainingUsers.Count));
            Assert.Equal(2, currentViews.LoadCount);
        });
    }

    [Fact]
    public async Task Late_old_session_cannot_replace_the_current_area_lease()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var old = Session(1, "old");
            var replacement = SelectedSessionIdentity.Create(
                old.AccountId,
                old.CharacterId,
                new NetworkConnectionId("replacement"),
                2,
                10011);
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
            sessions.EnterWorldWithConnection(state, old);
            sessions.EnterWorldWithConnection(state, replacement);
            var currentViews = new FixedCurrentViews();
            var replacementResult = Entry(replacement, 300);
            currentViews.Publish(replacement, replacementResult);
            var areas = new SessionPresenceDomainService();
            var store = (ITownAreaDomainService)areas;
            var key = new TownAreaKey(11, 3, 6);
            store.Enter(Member(replacement, key, 300));
            var delivery = new TownArrivalProjectionDriver(areas, sessions, currentViews, new RecordingNotificationBus(), new NoopPartyProjection(), new RecordingSender(), SharedTownMovementDomainService.Instance);

            Assert.Equal(0, await delivery.EnterAsync(
                [new TownAreaEntry(
                    old,
                    Entry(old, 100),
                    new CharacterPositionState(3, 6, 100, 200, 1))],
                CancellationToken.None));
            Assert.Equal(replacement, store.Find(old.CharacterId)!.Session);
        });
    }

    private static SelectedSessionIdentity Session(int id, string connection) =>
        SelectedSessionIdentity.Create(
            new AccountId(id),
            new CharacterId(id),
            new NetworkConnectionId(connection),
            2,
            10011);

    /// <summary>A returning session left town for a dungeon; the return lands its record back.</summary>
    private static void AwayFrom(SessionPresenceDomainService areas, SelectedSessionIdentity session, TownAreaKey key)
    {
        ((ITownAreaDomainService)areas).Enter(Member(session, key, x: 0));
        ((ITownAreaDomainService)areas).LeaveExact(session, TownAreaDepartureReason.DungeonEntry);
    }

    private static TownAreaMember Member(
        SelectedSessionIdentity session,
        TownAreaKey key,
        short x) =>
        new(
            session,
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

    /// <summary>
    /// What a real registration adapter registered, rebuilt as delivery plans from its outbox (a single send as a plan
    /// of its one message); every call goes to the real protocol sender over it (TASK-25).
    /// </summary>
    private sealed class RecordingSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public RecordedView<SentDelivery> Deliveries =>
            new(outbound, () => [.. outbound.GameCallDeliveries.Select(call => new SentDelivery(call.Direct, call.Delivery))]);

        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }
}
