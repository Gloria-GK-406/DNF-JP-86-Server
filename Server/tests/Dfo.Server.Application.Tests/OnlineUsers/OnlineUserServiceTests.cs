using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Protocol.Game.InitialConnection;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Application.Tests.OnlineUsers;

public sealed class UserServiceTests
{
    [Fact]
    public async Task ConnectedGame_RegistersIdentityBeforeSendingInitialNotice()
    {
        var service = CreateService();
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:initial"), NetworkEndpointKind.Game, 10011);
        var preparedAt = new DateTimeOffset(2026, 8, 13, 3, 0, 0, TimeSpan.Zero);
        var sender = new RecordingInitialSender(() =>
            Assert.True(service.TryGetByConnection(connection.ConnectionId, out _)));
        var observer = CreateObserver(
            service, new EmptySelectedSessions(), new NoopCleanup(), sender,
            new FixedTimeProvider(preparedAt));

        await observer.ConnectedAsync(connection, CancellationToken.None);

        var sent = Assert.IsType<GameInitialConnectionNotice>(Assert.Single(sender.Messages));
        Assert.Equal(11, sent.ChannelId);
        Assert.Equal(preparedAt, sent.PreparedAt);
        Assert.Equal("192.0.2.20", sent.EndpointHost);
    }

    [Fact]
    public async Task ConnectedChannel_DoesNotRegisterOrSendGameNotice()
    {
        var service = CreateService();
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Channel:initial"), NetworkEndpointKind.Channel, 7001);
        var sender = new RecordingInitialSender();
        var observer = CreateObserver(
            service, new EmptySelectedSessions(), new NoopCleanup(), sender,
            TimeProvider.System);

        await observer.ConnectedAsync(connection, CancellationToken.None);

        Assert.False(service.TryGetByConnection(connection.ConnectionId, out _));
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task FailedInitialNotice_RemovesExactRegisteredIdentity()
    {
        var service = CreateService();
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:failed-initial"), NetworkEndpointKind.Game, 10011);
        var observer = CreateObserver(
            service, new EmptySelectedSessions(), new NoopCleanup(),
            new RecordingInitialSender(fail: true), TimeProvider.System);

        await Assert.ThrowsAsync<IOException>(async () =>
            await observer.ConnectedAsync(connection, CancellationToken.None));

        Assert.False(service.TryGetByConnection(connection.ConnectionId, out _));
    }

    [Fact]
    public async Task ConnectedGame_UsesTheChannelResolvedFromItsExactListener()
    {
        var service = CreateService();
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:restricted-initial"), NetworkEndpointKind.Game, 10161);
        var sender = new RecordingInitialSender();
        var observer = CreateObserver(
            service, new EmptySelectedSessions(), new NoopCleanup(), sender,
            TimeProvider.System,
            new FixedChannelEntryPolicy(StubChannelEntryPolicy.RestrictedChannel));

        await observer.ConnectedAsync(connection, CancellationToken.None);

        var sent = Assert.IsType<GameInitialConnectionNotice>(Assert.Single(sender.Messages));
        Assert.Equal(100, sent.ChannelId);
    }

    [Fact]
    public async Task ConnectedGame_WithUnresolvedListener_RemovesRegisteredIdentityAndSendsNothing()
    {
        var service = CreateService();
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:unresolved-initial"), NetworkEndpointKind.Game, 19999);
        var sender = new RecordingInitialSender();
        var observer = CreateObserver(
            service, new EmptySelectedSessions(), new NoopCleanup(), sender,
            TimeProvider.System,
            new FixedChannelEntryPolicy(
                new ChannelEntryIdentity(0, connection.ListenerPort, ChannelEntryKind.General)));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await observer.ConnectedAsync(connection, CancellationToken.None));

        Assert.False(service.TryGetByConnection(connection.ConnectionId, out _));
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public void RegisterConnection_CreatesOneAnonymousUserResolvableByConnection()
    {
        var service = CreateService();
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:17"),
            NetworkEndpointKind.Game,
            10011);

        var registered = service.RegisterConnection(connection);

        var snapshot = service.GetRequired(registered.OnlineUserId);
        Assert.Equal(registered, snapshot);
        Assert.Equal(OnlineUserState.Connected, snapshot.State);
        Assert.Null(snapshot.AccountId);
        Assert.Equal(1, snapshot.Generation);
        Assert.True(service.TryGetByConnection(connection.ConnectionId, out var byConnection));
        Assert.Equal(snapshot, byConnection);
    }

    [Fact]
    public async Task Login_BindsAccountAndAdvancesGeneration()
    {
        var service = CreateService(new AccountId(42));
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:18"),
            NetworkEndpointKind.Game,
            10011);
        var registered = service.RegisterConnection(connection);

        await service.LoginAsync(
            registered.OnlineUserId,
            "login-42",
            "hash",
            CancellationToken.None);
        var authenticated = service.GetRequired(registered.OnlineUserId);

        Assert.Equal(OnlineUserState.Authenticated, authenticated.State);
        Assert.Equal(new AccountId(42), authenticated.AccountId);
        Assert.Equal(registered.Generation + 1, authenticated.Generation);
    }

    [Fact]
    public async Task TryDisconnect_RejectsStaleGenerationAndPreservesCurrentUser()
    {
        var service = CreateService(new AccountId(99));
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:19"),
            NetworkEndpointKind.Game,
            10011);
        var registered = service.RegisterConnection(connection);
        await service.LoginAsync(
            registered.OnlineUserId,
            "login-99",
            "hash",
            CancellationToken.None);
        var authenticated = service.GetRequired(registered.OnlineUserId);

        Assert.False(service.TryDisconnect(
            registered.OnlineUserId,
            connection.ConnectionId,
            registered.Generation));
        Assert.Equal(authenticated, service.GetRequired(registered.OnlineUserId));

        Assert.True(service.TryDisconnect(
            authenticated.OnlineUserId,
            connection.ConnectionId,
            authenticated.Generation));
        Assert.False(service.TryGetByConnection(connection.ConnectionId, out _));
        Assert.Throws<KeyNotFoundException>(() => service.GetRequired(authenticated.OnlineUserId));
    }

    [Fact]
    public async Task PublishActiveCharacter_UpdatesSnapshotAndActiveStateTogether()
    {
        var accountId = new AccountId(101);
        var service = CreateService(accountId);
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:21"),
            NetworkEndpointKind.Game,
            10011);
        var registered = service.RegisterConnection(connection);
        await service.LoginAsync(
            registered.OnlineUserId,
            "login-101",
            "hash",
            CancellationToken.None);
        var authenticated = service.GetRequired(registered.OnlineUserId);
        var session = SelectedSessionIdentity.Create(
            accountId, new CharacterId(1001), connection.ConnectionId,
            authenticated.Generation, 10011);

        service.Publish(session);

        var snapshot = service.GetRequired(registered.OnlineUserId);
        Assert.Equal(session.CharacterId, snapshot.CurrentCharacterId);
        Assert.Equal(OnlineUserState.CharacterActive, snapshot.State);

        // Binding advanced the online user's generation, so the replacing session owns the
        // generation the first publication left behind.
        var replacement = SelectedSessionIdentity.Create(
            accountId, new CharacterId(1004), connection.ConnectionId,
            snapshot.Generation, 10011);
        service.Publish(replacement);

        var replacedSnapshot = service.GetRequired(registered.OnlineUserId);
        Assert.Equal(replacement.CharacterId, replacedSnapshot.CurrentCharacterId);
        Assert.Equal(OnlineUserState.CharacterActive, replacedSnapshot.State);
        // The account's active entry moved with it: only the exact replacing session clears.
        Assert.False(service.ClearExact(session));
        Assert.True(service.ClearExact(replacement));
    }

    [Fact]
    public async Task Disconnect_OnlyAcceptedOwnerRemovalClearsActiveCharacter()
    {
        var accountId = new AccountId(102);
        var service = CreateService(accountId);
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:22"),
            NetworkEndpointKind.Game,
            10011);
        var registered = service.RegisterConnection(connection);
        await service.LoginAsync(
            registered.OnlineUserId,
            "login-102",
            "hash",
            CancellationToken.None);
        var authenticated = service.GetRequired(registered.OnlineUserId);
        var session = SelectedSessionIdentity.Create(
            accountId, new CharacterId(1002), connection.ConnectionId,
            authenticated.Generation, 10011);
        service.Publish(session);
        var active = service.GetRequired(registered.OnlineUserId);

        Assert.False(service.TryDisconnect(
            active.OnlineUserId,
            connection.ConnectionId,
            authenticated.Generation));
        Assert.Equal(
            session.CharacterId,
            service.GetRequired(active.OnlineUserId).CurrentCharacterId);

        Assert.True(service.TryDisconnect(
            active.OnlineUserId,
            connection.ConnectionId,
            active.Generation));
        // The account holds no active entry any more: a clear naming the exact session that was
        // published is refused, and the online user it was bound to is gone with it.
        Assert.False(service.ClearExact(session));
        Assert.Throws<KeyNotFoundException>(() => service.GetRequired(active.OnlineUserId));
    }

    [Fact]
    public async Task ExactActiveClearRejectsStaleSelectedGeneration()
    {
        var accountId = new AccountId(104);
        var service = CreateService(accountId);
        var firstConnection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:old"), NetworkEndpointKind.Game, 10011);
        var first = service.RegisterConnection(firstConnection);
        await service.LoginAsync(first.OnlineUserId, "old", "hash", CancellationToken.None);
        var firstAuthenticated = service.GetRequired(first.OnlineUserId);
        var firstSession = SelectedSessionIdentity.Create(accountId, new CharacterId(1101),
            firstConnection.ConnectionId, firstAuthenticated.Generation, 10011);
        service.Publish(firstSession);

        var secondConnection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:new"), NetworkEndpointKind.Game, 10011);
        var second = service.RegisterConnection(secondConnection);
        await service.LoginAsync(second.OnlineUserId, "new", "hash", CancellationToken.None);
        var secondAuthenticated = service.GetRequired(second.OnlineUserId);
        var replacement = SelectedSessionIdentity.Create(accountId, new CharacterId(1102),
            secondConnection.ConnectionId, secondAuthenticated.Generation, 10011);
        service.Publish(replacement);

        Assert.False(service.ClearExact(firstSession));
        Assert.Equal(
            replacement.CharacterId,
            service.GetRequired(second.OnlineUserId).CurrentCharacterId);
        Assert.True(service.ClearExact(replacement));
        var afterClear = service.GetRequired(second.OnlineUserId);
        Assert.Null(afterClear.CurrentCharacterId);
        Assert.Equal(OnlineUserState.Authenticated, afterClear.State);
    }

    [Fact]
    public async Task CharacterEntry_MissingSlotPreservesPreviouslyPublishedCharacter()
    {
        var accountId = new AccountId(103);
        var service = CreateService(accountId);
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:23"),
            NetworkEndpointKind.Game,
            10011);
        var registered = service.RegisterConnection(connection);
        await service.LoginAsync(
            registered.OnlineUserId,
            "login-103",
            "hash",
            CancellationToken.None);
        var authenticated = service.GetRequired(registered.OnlineUserId);
        var previous = SelectedSessionIdentity.Create(
            accountId, new CharacterId(1003), connection.ConnectionId,
            authenticated.Generation, 10011);
        service.Publish(previous);
        var snapshotBeforeFailure = service.GetRequired(registered.OnlineUserId);
        var entry = EntryServices.Create(
            new MissingCharacterDomainService(),
            new NotReachedInventoryEntryService(),
            new NotReachedCharacterEntryStateDomainService(),
            new NotReachedAccountEntryStateDomainService(),
            service);

        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await entry.EnterAsync(accountId, 5, StubChannelEntryPolicy.GeneralChannel, CancellationToken.None));

        Assert.Equal(
            previous.CharacterId,
            service.GetRequired(registered.OnlineUserId).CurrentCharacterId);
        Assert.Equal(snapshotBeforeFailure, service.GetRequired(registered.OnlineUserId));
    }

    [Fact]
    public async Task DisconnectObservedBeforeAuthentication_DoesNotRemoveNewerGeneration()
    {
        var service = CreateService(new AccountId(100));
        var gate = new OnlineUserExecutionGate();
        var observer = new OnlineUserConnectionObserver(
            service, gate, new SelectedSessionTransitionGate(),
            new EmptySelectedSessions(), new NoopCleanup(), new StubChannelEntryPolicy(),
            new StubServerConfigService(), new RecordingInitialSender(), TimeProvider.System);
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:20"),
            NetworkEndpointKind.Game,
            10011);
        var registered = service.RegisterConnection(connection);
        await using var identityTransition = await gate.AcquireAsync(
            registered.OnlineUserId,
            UserAccessMode.Exclusive,
            CancellationToken.None);

        var disconnect = observer.DisconnectedAsync(connection, CancellationToken.None).AsTask();
        await Task.Yield();
        Assert.False(disconnect.IsCompleted);
        await service.LoginAsync(
            registered.OnlineUserId,
            "login-100",
            "hash",
            CancellationToken.None);
        var authenticated = service.GetRequired(registered.OnlineUserId);
        await identityTransition.DisposeAsync();
        await disconnect;

        Assert.Equal(authenticated, service.GetRequired(registered.OnlineUserId));
        Assert.True(service.TryGetByConnection(connection.ConnectionId, out var current));
        Assert.Equal(authenticated, current);
    }

    [Fact]
    public async Task DisconnectSelectedSession_RemovesConnectionAfterExactCleanupAdvancesGeneration()
    {
        var accountId = new AccountId(104);
        var service = CreateService(accountId);
        var gate = new OnlineUserExecutionGate();
        var sessions = new SelectedSessionDirectory();
        var cleanup = new ClearingCleanup(service);
        var observer = new OnlineUserConnectionObserver(
            service, gate, new SelectedSessionTransitionGate(), sessions, cleanup,
            new StubChannelEntryPolicy(), new StubServerConfigService(),
            new RecordingInitialSender(), TimeProvider.System);
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:24"), NetworkEndpointKind.Game, 10011);
        var registered = service.RegisterConnection(connection);
        await service.LoginAsync(registered.OnlineUserId, "login-104", "hash", CancellationToken.None);
        var beforeBind = service.GetRequired(registered.OnlineUserId);
        var selected = SelectedSessionIdentity.Create(
            accountId, new CharacterId(1004), connection.ConnectionId, beforeBind.Generation, 10011);
        sessions.RegisterReplacing(selected);
        service.Publish(selected);

        await observer.DisconnectedAsync(connection, CancellationToken.None);

        Assert.Equal(selected, cleanup.Cleaned);
        Assert.False(service.TryGetByConnection(connection.ConnectionId, out _));
    }

    [Fact]
    public async Task DisconnectSelectedSession_AttemptsResolvedNotificationsWhenCleanupFails()
    {
        var accountId = new AccountId(106);
        var service = CreateService(accountId);
        var sessions = new SelectedSessionDirectory();
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:cleanup-failure"), NetworkEndpointKind.Game, 10011);
        var registered = service.RegisterConnection(connection);
        await service.LoginAsync(
            registered.OnlineUserId, "login-106", "hash", CancellationToken.None);
        var authenticated = service.GetRequired(registered.OnlineUserId);
        var selected = SelectedSessionIdentity.Create(
            accountId, new CharacterId(1006), connection.ConnectionId,
            authenticated.Generation, 10011);
        sessions.RegisterReplacing(selected);
        var peerConnection = new NetworkConnectionId("Game:cleanup-peer");
        var plan = new SelectedSessionCleanupNotificationPlan(
            connection.ConnectionId,
            [new SelectedSessionCleanupNotice(
                SelectedSessionCleanupNoticeKind.ExpertStoreClosed, selected.WireUserId)],
            [new SelectedSessionCleanupNotificationTarget(
                peerConnection,
                [new SelectedSessionCleanupNotice(
                    SelectedSessionCleanupNoticeKind.TownDeparture, selected.WireUserId)])]);
        var sender = new RecordingExplicitSender();
        var observer = CreateObserver(
            service, sessions, new FailingCleanup(plan), sender, TimeProvider.System);

        await observer.DisconnectedAsync(connection, CancellationToken.None);

        var delivery = Assert.Single(sender.Deliveries);
        Assert.Equal(connection.ConnectionId, delivery.ConnectionId);
        Assert.Equal("ExpertStoreClosedMessage",
            Assert.Single(delivery.Delivery.RequiredDirectMessages).GetType().Name);
        var target = Assert.Single(delivery.Delivery.BestEffortTargets);
        Assert.Equal(peerConnection, target.ConnectionId);
        Assert.Equal("TownDepartureMessage", Assert.Single(target.OrderedMessages).GetType().Name);
        Assert.False(service.TryGetByConnection(connection.ConnectionId, out _));
    }

    [Fact]
    public async Task DisconnectWithoutSelectedSession_DoesNotWaitForAccountLifecycleGate()
    {
        var service = CreateService(new AccountId(105));
        var lifecycleGate = new SelectedSessionTransitionGate();
        var observer = new OnlineUserConnectionObserver(
            service, new OnlineUserExecutionGate(), lifecycleGate,
            new EmptySelectedSessions(), new NoopCleanup(), new StubChannelEntryPolicy(),
            new StubServerConfigService(), new RecordingInitialSender(), TimeProvider.System);
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:25"), NetworkEndpointKind.Game, 10011);
        var registered = service.RegisterConnection(connection);
        await service.LoginAsync(
            registered.OnlineUserId, "login-105", "hash", CancellationToken.None);
        await using var blocker = await lifecycleGate.AcquireAsync(
            new AccountId(105), UserAccessMode.Exclusive, CancellationToken.None);

        await observer.DisconnectedAsync(connection, CancellationToken.None).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(service.TryGetByConnection(connection.ConnectionId, out _));
    }

    private static UserDomainServices CreateService(AccountId accountId = default)
    {
        var repository = new StubAccountRepository(
            accountId == default ? new AccountId(1) : accountId);
        return new UserDomainServices(() => repository);
    }

    private static OnlineUserConnectionObserver CreateObserver(
        IOnlineUserDomainService service,
        ISelectedSessionDirectory sessions,
        ISelectedSessionCleanupDomainService cleanup,
        IGameMessageSender sender,
        TimeProvider timeProvider,
        IChannelEntryPolicy? channelPolicy = null) =>
        new(
            service,
            new OnlineUserExecutionGate(),
            new SelectedSessionTransitionGate(),
            sessions,
            cleanup,
            channelPolicy ?? new StubChannelEntryPolicy(),
            new StubServerConfigService(),
            sender,
            timeProvider);

    private sealed class FixedChannelEntryPolicy(ChannelEntryIdentity channel) : IChannelEntryPolicy
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(
            int listenerPort,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(channel with { ListenerPort = listenerPort });

        public ChannelAdmissionRefusal? Refuse(
            ChannelEntryIdentity channel,
            int characterLevel) => null;
    }

    private sealed class RecordingInitialSender(
        Action? beforeSend = null,
        bool fail = false) : IGameMessageSender
    {
        public List<IGameServerMessage> Messages { get; } = [];

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken)
        {
            beforeSend?.Invoke();
            if (fail)
                return ValueTask.FromException(new IOException("initial notice failed"));
            Messages.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Initial connection sends a NOTI message.");

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The initial notice path does not use explicit-recipient delivery.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class StubAccountRepository(AccountId accountId) : IAccountRepository
    {
        public ValueTask<AccountRecord> GetOrCreateAsync(
            string loginId,
            string passwordHash,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AccountRecord(
                accountId,
                loginId,
                passwordHash,
                DateTimeOffset.UtcNow));
    }

    private sealed class EmptySelectedSessions : ISelectedSessionDirectory
    {
        public SelectedSessionIdentity? RegisterReplacing(SelectedSessionIdentity session) => null;
        public bool TryRemoveExact(SelectedSessionIdentity session) => false;
        public SelectedSessionIdentity? Find(AccountId accountId) => null;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => null;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => null;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => null;
        public IReadOnlyList<SelectedSessionIdentity> Current() => [];
    }

    private sealed class NoopCleanup : ISelectedSessionCleanupDomainService
    {
        public ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
            SelectedSessionIdentity session, bool includeOwner,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(SelectedSessionCleanupNotificationPlan.Empty(session));
    }

    private sealed class FailingCleanup(SelectedSessionCleanupNotificationPlan plan)
        : ISelectedSessionCleanupDomainService
    {
        public ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
            SelectedSessionIdentity session, bool includeOwner,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<SelectedSessionCleanupNotificationPlan>(
                new SelectedSessionCleanupFailure(plan, [new IOException("cleanup failed")]));
    }

    private sealed class RecordingExplicitSender : IGameMessageSender
    {
        public List<(NetworkConnectionId ConnectionId, GameExplicitMessageDelivery Delivery)> Deliveries
            { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            Deliveries.Add((requiredDirectConnectionId, delivery));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ClearingCleanup(IActiveCharacterDomainService active) : ISelectedSessionCleanupDomainService
    {
        public SelectedSessionIdentity? Cleaned { get; private set; }

        public ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
            SelectedSessionIdentity session, bool includeOwner,
            CancellationToken cancellationToken)
        {
            Cleaned = session;
            Assert.True(active.ClearExact(session));
            return ValueTask.FromResult(SelectedSessionCleanupNotificationPlan.Empty(session));
        }
    }

    private sealed class NotReachedInventoryEntryService : ITestInventoryEntryServices
    {
        ValueTask<CharacterInventoryEntryState> ICharacterInventoryEntryDomainService.LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not load when character resolution fails.");

        ValueTask<AccountInventoryEntryState> IAccountInventoryEntryDomainService.LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not load when character resolution fails.");
    }

    private sealed class NotReachedCharacterEntryStateDomainService : ICharacterEntryStateDomainService
    {
        public ValueTask<CharacterEntryState> LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Character entry state must not load when character resolution fails.");
    }

    private sealed class NotReachedAccountEntryStateDomainService : IAccountEntryStateDomainService
    {
        public ValueTask<AccountEntryState> LoadAsync(
            AccountId accountId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Account entry state must not load when character resolution fails.");

        public ValueTask<AccountSettingsEntryState> LoadSettingsAsync(
            AccountId accountId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Account settings must not load when character resolution fails.");
    }

    private sealed class MissingCharacterDomainService : ICharacterDomainService
    {

        public ICharacter Get(CharacterId characterId) =>
            throw new NotSupportedException("This stand-in answers no questions about a character.");
        public ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
            AccountId accountId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ActiveCharacterState> LoadForSelectionAsync(
            AccountId accountId,
            ushort slotIndex,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<ActiveCharacterState>(
                new KeyNotFoundException($"Missing slot '{slotIndex}'."));

        public ValueTask<ActiveCharacterState> LoadByRosterPositionAsync(
            AccountId accountId,
            ushort rosterIndex,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<ActiveCharacterState>(
                new KeyNotFoundException($"Missing roster position '{rosterIndex}'."));

        public ValueTask<CharacterNameStatus> CheckNameAsync(
            byte[] rawName,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CharacterCreationResult> CreateCharacterAsync(
            AccountId accountId,
            byte job,
            byte[] rawName,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CharacterDeletionResult> DeleteCharacterAsync(
            AccountId accountId,
            ushort slotIndex,
            byte[] rawName,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
