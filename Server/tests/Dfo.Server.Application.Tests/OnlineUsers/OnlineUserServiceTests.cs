using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Accounts;
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
            OnlineUserAccessMode.Exclusive,
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
}
