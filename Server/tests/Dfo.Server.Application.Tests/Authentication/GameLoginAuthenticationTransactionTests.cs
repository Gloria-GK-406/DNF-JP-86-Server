using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Application.Tests.Authentication;

public sealed class GameLoginAuthenticationTransactionTests
{
    [Fact]
    public async Task PreparedIdentityStaysAnonymousUntilDurableCommitAndIgnoresLaterCancellation()
    {
        var scope = new DomainTransactionScope();
        var state = new OnlineStateRegistry(scope);
        var registered = Register(state, "Game:commit-order");
        using var cancellation = new CancellationTokenSource();
        await using var transaction = scope.Begin();
        var durable = scope.Current!.Enlist(
            "sqlite",
            () => new DurableParticipant(() =>
            {
                var beforePublication = state.GetRequiredUser(registered.OnlineUserId);
                Assert.Equal(OnlineUserState.Connected, beforePublication.State);
                Assert.Null(beforePublication.AccountId);
                cancellation.Cancel();
            }));
        state.PrepareAuthentication(registered.OnlineUserId, new AccountId(41));

        await transaction.CommitAsync(cancellation.Token);

        Assert.True(durable.Committed);
        var authenticated = state.GetRequiredUser(registered.OnlineUserId);
        Assert.Equal(new AccountId(41), authenticated.AccountId);
        Assert.Equal(OnlineUserState.Authenticated, authenticated.State);
    }

    [Fact]
    public async Task DurableCommitFailureLeavesThePreparedIdentityAnonymous()
    {
        var scope = new DomainTransactionScope();
        var state = new OnlineStateRegistry(scope);
        var registered = Register(state, "Game:commit-failure");
        await using var transaction = scope.Begin();
        scope.Current!.Enlist(
            "sqlite",
            () => new DurableParticipant(
                () => throw new InvalidOperationException("database commit failed")));
        state.PrepareAuthentication(registered.OnlineUserId, new AccountId(42));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transaction.CommitAsync(CancellationToken.None));

        Assert.Equal("database commit failed", failure.Message);
        var anonymous = state.GetRequiredUser(registered.OnlineUserId);
        Assert.Equal(OnlineUserState.Connected, anonymous.State);
        Assert.Null(anonymous.AccountId);
    }

    [Fact]
    public async Task TargetMissingAtPreparePreventsTheDurableCommit()
    {
        var scope = new DomainTransactionScope();
        var state = new OnlineStateRegistry(scope);
        var registered = Register(state, "Game:prepare-missing");
        await using var transaction = scope.Begin();
        var durable = scope.Current!.Enlist("sqlite", () => new DurableParticipant());
        state.PrepareAuthentication(registered.OnlineUserId, new AccountId(43));
        Assert.True(state.TryDisconnectConnection(registered.Connection.ConnectionId));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transaction.CommitAsync(CancellationToken.None));

        Assert.False(durable.Committed);
        Assert.False(state.TryGetByConnection(registered.Connection.ConnectionId, out _));
    }

    [Fact]
    public async Task TargetMissingAfterDurableCommitMakesPublicationANonFailingNoOp()
    {
        var scope = new DomainTransactionScope();
        var state = new OnlineStateRegistry(scope);
        var registered = Register(state, "Game:post-commit-missing");
        await using var transaction = scope.Begin();
        var durable = scope.Current!.Enlist(
            "sqlite",
            () => new DurableParticipant(() =>
                Assert.True(state.TryDisconnectConnection(registered.Connection.ConnectionId))));
        state.PrepareAuthentication(registered.OnlineUserId, new AccountId(44));

        await transaction.CommitAsync(CancellationToken.None);

        Assert.True(durable.Committed);
        Assert.False(state.TryGetByConnection(registered.Connection.ConnectionId, out _));
    }

    [Fact]
    public async Task RollbackNeverPublishesThePreparedIdentity()
    {
        var scope = new DomainTransactionScope();
        var state = new OnlineStateRegistry(scope);
        var registered = Register(state, "Game:rollback");
        await using (scope.Begin())
        {
            state.PrepareAuthentication(registered.OnlineUserId, new AccountId(45));
        }

        var anonymous = state.GetRequiredUser(registered.OnlineUserId);
        Assert.Equal(OnlineUserState.Connected, anonymous.State);
        Assert.Null(anonymous.AccountId);
    }

    private static OnlineUserSnapshot Register(OnlineStateRegistry state, string connectionId) =>
        state.RegisterConnection(new NetworkConnectionContext(
            new NetworkConnectionId(connectionId),
            NetworkEndpointKind.Game,
            10011));

    private sealed class DurableParticipant(Action? onCommit = null) : ITransactionParticipant
    {
        public bool CommitCanFail => true;

        public bool Committed { get; private set; }

        public ValueTask PrepareAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            onCommit?.Invoke();
            this.Committed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
