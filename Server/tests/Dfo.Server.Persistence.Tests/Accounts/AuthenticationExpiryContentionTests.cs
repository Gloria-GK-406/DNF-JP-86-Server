using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Accounts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class AuthenticationExpiryContentionTests
{
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task ExpiryIsCheckedAfterACompetingSqliteWriterReleasesItsLock(long ticksAfterExpiry, bool accepted)
    {
        using var fixture = new AuthenticationFixture();
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var login = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        var expiry = login.ExpiresAtUtc!.Value;
        var clock = new AtomicClock(expiry.AddSeconds(-1));
        var persistence = new ObservedPersistence(fixture.Store);
        var service = new AuthenticationDomainService(persistence, new PasswordCredentialService(100), clock,
            AuthenticationRuleService.From(new ConfigurationBuilder().Build()));

        await using var blocker = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False");
        await blocker.OpenAsync();
        await using var writeLock = blocker.BeginTransaction(deferred: false);
        var consume = Task.Factory.StartNew(async () =>
        {
            await using var transaction = fixture.Scope.Begin();
            var account = await service.ConsumeTicketAsync("player", login.Ticket!, default);
            await transaction.CommitAsync(default);
            return account;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        var released = false;
        try
        {
            await persistence.WriteAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(consume.IsCompleted);
            clock.Set(expiry.AddTicks(ticksAfterExpiry));
            await writeLock.RollbackAsync();
            released = true;
            var account = await consume.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(accepted, account.HasValue);
            Assert.Equal(accepted ? 1L : 0L,
                await fixture.Scalar("SELECT COUNT(*) FROM login_tickets WHERE consumed_at IS NOT NULL"));
        }
        finally
        {
            if (!released) await writeLock.RollbackAsync();
            try { await consume; } catch { }
        }
    }

    private sealed class AtomicClock(DateTimeOffset now) : TimeProvider
    {
        private long ticks = now.UtcTicks;
        public void Set(DateTimeOffset value) => Interlocked.Exchange(ref ticks, value.UtcTicks);
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    }

    // Only observes entry into the real persistence operation. Both the blocker and consumer
    // use real SQLite connections and transactions; no database result or lock is simulated.
    private sealed class ObservedPersistence(IAuthenticationPersistence inner) : IAuthenticationPersistence
    {
        public TaskCompletionSource WriteAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask AcquireWriteIntentAsync(CancellationToken cancellationToken)
        {
            WriteAttempted.TrySetResult();
            return inner.AcquireWriteIntentAsync(cancellationToken);
        }
        public ValueTask<AccountId?> TryConsumeTicketAsync(string loginId, string digest, DateTimeOffset now, CancellationToken cancellationToken)
        {
            WriteAttempted.TrySetResult();
            return inner.TryConsumeTicketAsync(loginId, digest, now, cancellationToken);
        }
        public ValueTask<AccountRecord?> FindAsync(string loginId, CancellationToken cancellationToken) => inner.FindAsync(loginId, cancellationToken);
        public ValueTask<AccountId?> TryCreateAsync(string loginId, string passwordRecord, CancellationToken cancellationToken) => inner.TryCreateAsync(loginId, passwordRecord, cancellationToken);
        public ValueTask<bool> TryReplaceCredentialAsync(AccountId accountId, string expected, string replacement, CancellationToken cancellationToken) => inner.TryReplaceCredentialAsync(accountId, expected, replacement, cancellationToken);
        public ValueTask InsertTicketAsync(string digest, AccountId accountId, DateTimeOffset issuedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken) => inner.InsertTicketAsync(digest, accountId, issuedAt, expiresAt, cancellationToken);
    }
}
