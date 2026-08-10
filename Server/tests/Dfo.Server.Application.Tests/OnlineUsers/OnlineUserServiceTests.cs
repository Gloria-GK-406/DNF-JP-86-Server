using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Application.Tests.OnlineUsers;

public sealed class OnlineUserServiceTests
{
    [Fact]
    public void RegisterConnection_CreatesOneAnonymousUserResolvableByConnection()
    {
        var service = new OnlineUserService();
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
    public void Authenticate_BindsAccountAndAdvancesGeneration()
    {
        var service = new OnlineUserService();
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:18"),
            NetworkEndpointKind.Game,
            10011);
        var registered = service.RegisterConnection(connection);

        var authenticated = service.Authenticate(
            registered.OnlineUserId,
            new AccountId(42));

        Assert.Equal(OnlineUserState.Authenticated, authenticated.State);
        Assert.Equal(new AccountId(42), authenticated.AccountId);
        Assert.Equal(registered.Generation + 1, authenticated.Generation);
    }

    [Fact]
    public void TryDisconnect_RejectsStaleGenerationAndPreservesCurrentUser()
    {
        var service = new OnlineUserService();
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("Game:19"),
            NetworkEndpointKind.Game,
            10011);
        var registered = service.RegisterConnection(connection);
        var authenticated = service.Authenticate(
            registered.OnlineUserId,
            new AccountId(99));

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
        var service = new OnlineUserService();
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
        var authenticated = service.Authenticate(registered.OnlineUserId, new AccountId(100));
        await identityTransition.DisposeAsync();
        await disconnect;

        Assert.Equal(authenticated, service.GetRequired(registered.OnlineUserId));
        Assert.True(service.TryGetByConnection(connection.ConnectionId, out var current));
        Assert.Equal(authenticated, current);
    }
}
