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
using Dfo.Server.Application.Users;

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
        var entry = new CharacterEntryService(
            new MissingCharacterService(),
            new NotReachedInventoryEntryService(),
            service,
            TimeProvider.System);

        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await entry.EnterAsync(accountId, 5, CancellationToken.None));

        Assert.Same(previous, service.GetRequired(accountId));
        Assert.Equal(snapshotBeforeFailure, service.GetRequired(registered.OnlineUserId));
    }

    [Fact]
    public async Task DisconnectObservedBeforeAuthentication_DoesNotRemoveNewerGeneration()
    {
        var service = CreateService(new AccountId(100));
        var gate = new OnlineUserExecutionGate();
        var observer = new OnlineUserConnectionObserver(service, gate);
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

    private sealed class NotReachedInventoryEntryService : IInventoryEntryService
    {
        public ValueTask<InventoryEntryState> LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not load when character resolution fails.");
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
