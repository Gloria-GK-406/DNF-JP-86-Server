using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Authentication;

public sealed class LoginSessionCommitRaceTests
{
    [Fact]
    public async Task DisconnectBetweenPrepareAndCommitStartsGraceInsteadOfLeavingAnImmortalBinding()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = new DomainTransactionScope();
            var clock = new Clock();
            var sessions = new LoginSessionRegistry(scope, clock, new LoginSessionOptions());
            LoginSessionRegistry.IssuedLoginSession issued;
            await using (var tx = scope.Begin())
            {
                issued = sessions.Issue(new AccountId(3), clock.Now, clock.Now.AddSeconds(180));
                await tx.CommitAsync(default);
            }
            var first = new NetworkConnectionId("lost-during-commit");
            await using (var tx = scope.Begin())
            {
                Assert.NotNull(sessions.TryBind(first, issued.MId, issued.Ticket));
                scope.Current!.Enlist("durable", () => new DurableAction(() => sessions.Disconnected(first)));
                await tx.CommitAsync(default);
            }
            clock.Now = clock.Now.AddSeconds(299);
            await using (scope.Begin())
                Assert.NotNull(sessions.TryBind(new NetworkConnectionId("retry-rollback"), issued.MId, issued.Ticket));
            clock.Now = clock.Now.AddSeconds(1);
            await using (scope.Begin())
                Assert.Null(sessions.TryBind(new NetworkConnectionId("expired"), issued.MId, issued.Ticket));
        });
    }

    [Fact]
    public async Task RolledBackHandoffDoesNotCloseTheExistingConnection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = new DomainTransactionScope();
            var clock = new Clock();
            var sessions = new LoginSessionRegistry(scope, clock, new LoginSessionOptions());
            LoginSessionRegistry.IssuedLoginSession issued;
            await using (var tx = scope.Begin())
            {
                issued = sessions.Issue(new AccountId(3), clock.Now, clock.Now.AddSeconds(180));
                await tx.CommitAsync(default);
            }
            var first = new NetworkConnectionId("first");
            await using (var tx = scope.Begin())
            {
                Assert.NotNull(sessions.TryBind(first, issued.MId, issued.Ticket));
                await tx.CommitAsync(default);
            }
            await using (scope.Begin())
                Assert.NotNull(sessions.TryBind(new NetworkConnectionId("aborted"), issued.MId, issued.Ticket));
            Assert.False(sessions.IsSuperseded(first));
            Assert.False(sessions.CloseRequests.TryRead(out _));
            clock.Now = clock.Now.AddHours(2);
            await using (scope.Begin())
                Assert.NotNull(sessions.TryBind(new NetworkConnectionId("after-rollback"), issued.MId, issued.Ticket));
        });
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class DurableAction(Action action) : ITransactionParticipant
    {
        public bool CommitCanFail => true;
        public ValueTask PrepareAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken cancellationToken) { action(); return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
