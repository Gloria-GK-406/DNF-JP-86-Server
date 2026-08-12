using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;

namespace Dfo.Server.Application.Tests.OnlineUsers;

public sealed class UserServiceTests
{
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
        var character = CreateCharacter(1001, 2);

        service.Publish(accountId, character);

        var snapshot = service.GetRequired(registered.OnlineUserId);
        Assert.Equal(character.Identity.CharacterId, snapshot.CurrentCharacterId);
        Assert.Equal(OnlineUserState.CharacterActive, snapshot.State);
        Assert.Same(character, service.GetRequired(accountId));

        var replacement = CreateCharacter(1004, 5);
        service.Publish(accountId, replacement);

        var replacedSnapshot = service.GetRequired(registered.OnlineUserId);
        Assert.Equal(replacement.Identity.CharacterId, replacedSnapshot.CurrentCharacterId);
        Assert.Same(replacement, service.GetRequired(accountId));
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
        var character = CreateCharacter(1002, 3);
        service.Publish(accountId, character);
        var active = service.GetRequired(registered.OnlineUserId);

        Assert.False(service.TryDisconnect(
            active.OnlineUserId,
            connection.ConnectionId,
            authenticated.Generation));
        Assert.Same(character, service.GetRequired(accountId));

        Assert.True(service.TryDisconnect(
            active.OnlineUserId,
            connection.ConnectionId,
            active.Generation));
        Assert.Throws<KeyNotFoundException>(() => service.GetRequired(accountId));
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
        service.Publish(firstSession, CreateCharacter(1101, 1));

        var secondConnection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:new"), NetworkEndpointKind.Game, 10011);
        var second = service.RegisterConnection(secondConnection);
        await service.LoginAsync(second.OnlineUserId, "new", "hash", CancellationToken.None);
        var secondAuthenticated = service.GetRequired(second.OnlineUserId);
        var replacement = SelectedSessionIdentity.Create(accountId, new CharacterId(1102),
            secondConnection.ConnectionId, secondAuthenticated.Generation, 10011);
        var replacementCharacter = CreateCharacter(1102, 2);
        service.Publish(replacement, replacementCharacter);

        Assert.False(service.ClearExact(firstSession));
        Assert.Same(replacementCharacter, service.GetRequired(accountId));
        Assert.True(service.ClearExact(replacement));
        Assert.Throws<KeyNotFoundException>(() => service.GetRequired(accountId));
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
        var previous = CreateCharacter(1003, 4);
        service.Publish(accountId, previous);
        var snapshotBeforeFailure = service.GetRequired(registered.OnlineUserId);
        var entry = EntryServices.Create(
            new MissingCharacterService(),
            new NotReachedInventoryEntryService(),
            new NotReachedCharacterEntryStateService(),
            new NotReachedAccountEntryStateService(),
            service);

        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await entry.EnterAsync(accountId, 5, StubChannelEntryPolicy.GeneralChannel, CancellationToken.None));

        Assert.Same(previous, service.GetRequired(accountId));
        Assert.Equal(snapshotBeforeFailure, service.GetRequired(registered.OnlineUserId));
    }

    [Fact]
    public async Task DisconnectObservedBeforeAuthentication_DoesNotRemoveNewerGeneration()
    {
        var service = CreateService(new AccountId(100));
        var gate = new OnlineUserExecutionGate();
        var observer = new OnlineUserConnectionObserver(
            service, gate, new SelectedSessionTransitionGate(),
            new EmptySelectedSessions(), new NoopCleanup());
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
            service, gate, new SelectedSessionTransitionGate(), sessions, cleanup);
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:24"), NetworkEndpointKind.Game, 10011);
        var registered = service.RegisterConnection(connection);
        await service.LoginAsync(registered.OnlineUserId, "login-104", "hash", CancellationToken.None);
        var beforeBind = service.GetRequired(registered.OnlineUserId);
        var selected = SelectedSessionIdentity.Create(
            accountId, new CharacterId(1004), connection.ConnectionId, beforeBind.Generation, 10011);
        sessions.RegisterReplacing(selected);
        service.Publish(selected, CreateCharacter(1004, 4));

        await observer.DisconnectedAsync(connection, CancellationToken.None);

        Assert.Equal(selected, cleanup.Cleaned);
        Assert.False(service.TryGetByConnection(connection.ConnectionId, out _));
    }

    [Fact]
    public async Task DisconnectWithoutSelectedSession_DoesNotWaitForAccountLifecycleGate()
    {
        var service = CreateService(new AccountId(105));
        var lifecycleGate = new SelectedSessionTransitionGate();
        var observer = new OnlineUserConnectionObserver(
            service, new OnlineUserExecutionGate(), lifecycleGate,
            new EmptySelectedSessions(), new NoopCleanup());
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

    private static UserService CreateService(AccountId accountId = default)
    {
        var repository = new StubAccountRepository(
            accountId == default ? new AccountId(1) : accountId);
        return new UserService(() => repository);
    }

    private static ActiveCharacterState CreateCharacter(int characterId, ushort slotIndex) =>
        new(
            new CharacterIdentityState(
                new CharacterId(characterId),
                slotIndex,
                [0x41],
                Job: 1,
                GrowType: 0),
            new CharacterProgressionState(Level: 10, Experience: 100),
            new CharacterFatigueState(Total: 156, Used: 0),
            new CharacterPositionState(TownId: 1, AreaId: 1, X: 10, Y: 20, Direction: 0),
            DateTimeOffset.UnixEpoch,
            DarkKnightCombo: null);

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

    private sealed class NoopCleanup : ISelectedSessionCleanupService
    {
        public ValueTask CleanupAsync(SelectedSessionIdentity session, bool includeOwner,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class ClearingCleanup(IActiveCharacterService active) : ISelectedSessionCleanupService
    {
        public SelectedSessionIdentity? Cleaned { get; private set; }

        public ValueTask CleanupAsync(SelectedSessionIdentity session, bool includeOwner,
            CancellationToken cancellationToken)
        {
            Cleaned = session;
            Assert.True(active.ClearExact(session));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NotReachedInventoryEntryService : ITestInventoryEntryServices
    {
        ValueTask<CharacterInventoryEntryState> ICharacterInventoryEntryService.LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not load when character resolution fails.");

        ValueTask<AccountInventoryEntryState> IAccountInventoryEntryService.LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not load when character resolution fails.");
    }

    private sealed class NotReachedCharacterEntryStateService : ICharacterEntryStateService
    {
        public ValueTask<CharacterEntryState> LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Character entry state must not load when character resolution fails.");
    }

    private sealed class NotReachedAccountEntryStateService : IAccountEntryStateService
    {
        public ValueTask<AccountEntryState> LoadAsync(
            AccountId accountId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Account entry state must not load when character resolution fails.");
    }

    private sealed class MissingCharacterService : ICharacterService
    {
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
    }
}
