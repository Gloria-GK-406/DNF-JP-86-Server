using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.PlayerPortal;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.PlayerPortal;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.PlayerPortal;

public sealed class SqlitePlayerPortalPersistenceTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
    private static readonly DateOnly Today = new(2027, 1, 15);

    [Fact]
    public async Task SettingsReadAsDefaultUntilSavedAndRefuseAStaleRevision()
    {
        await WithStore(async (store, scope) =>
        {
            Assert.Equal(PlayerPortalSettings.Default, await store.ReadSettingsAsync(default));
            Assert.False(PlayerPortalSettings.Default.Enabled);
            Assert.True(PlayerPortalSettings.Default.CeraEnabled && PlayerPortalSettings.Default.MailEnabled);

            var first = new PlayerPortalSettings(true, false, true, new PlayerPortalLimits(1000, 1500, 200, 300, 5000, 3), 99);
            await using (var transaction = scope.Begin())
            {
                Assert.True(await store.SaveSettingsAsync(first, 0, default));
                Assert.False(await store.SaveSettingsAsync(first with { Enabled = false }, 0, default));
                await transaction.CommitAsync(default);
            }
            Assert.Equal(first with { Revision = 1 }, await store.ReadSettingsAsync(default));

            var second = first with { MailEnabled = false, Limits = PlayerPortalLimits.None };
            await using (var transaction = scope.Begin())
            {
                Assert.False(await store.SaveSettingsAsync(second, 2, default));
                Assert.True(await store.SaveSettingsAsync(second, 1, default));
                await transaction.CommitAsync(default);
            }
            Assert.Equal(second with { Revision = 2 }, await store.ReadSettingsAsync(default));
        });
    }

    [Fact]
    public async Task SessionsAreFoundListedAndRemovedByHashAccountExpiryOrAll()
    {
        await WithStore(async (store, scope, database) =>
        {
            await using (var transaction = scope.Begin())
            {
                _ = await database.ExecuteAsync("INSERT INTO accounts(account_id,m_id) VALUES(7,'alice'),(8,'bob');", _ => { }, default);
                await store.SaveSessionAsync(new("a1", 7, At, At.AddHours(8)), default);
                await store.SaveSessionAsync(new("a2", 7, At.AddMinutes(5), At.AddHours(8).AddMinutes(5)), default);
                await store.SaveSessionAsync(new("b1", 8, At.AddHours(-9), At.AddHours(-1)), default);
                await transaction.CommitAsync(default);
            }

            Assert.Equal(new PlayerPortalSession("a1", 7, At, At.AddHours(8)), await store.FindSessionAsync("a1", default));
            Assert.Null(await store.FindSessionAsync("missing", default));
            var active = await store.ListActiveSessionsAsync(At, default);
            Assert.Equal([new(7, "alice", At.AddMinutes(5), At.AddHours(8).AddMinutes(5)), new PlayerPortalSessionEntry(7, "alice", At, At.AddHours(8))], active);

            await using (var transaction = scope.Begin())
            {
                Assert.Equal(1, await store.DeleteExpiredSessionsAsync(At, default));
                Assert.True(await store.DeleteSessionAsync("a1", default));
                Assert.False(await store.DeleteSessionAsync("a1", default));
                await store.SaveSessionAsync(new("b2", 8, At, At.AddHours(8)), default);
                Assert.Equal(1, await store.DeleteAccountSessionsAsync(7, default));
                await transaction.CommitAsync(default);
            }
            Assert.Equal(new PlayerPortalSessionEntry(8, "bob", At, At.AddHours(8)), Assert.Single(await store.ListActiveSessionsAsync(At, default)));
            Assert.Null(await store.FindSessionAsync("a2", default));

            await using (var transaction = scope.Begin())
            {
                await store.SaveSessionAsync(new("a3", 7, At, At.AddHours(8)), default);
                Assert.Equal(2, await store.DeleteAllSessionsAsync(default));
                await transaction.CommitAsync(default);
            }
            Assert.Empty(await store.ListActiveSessionsAsync(At, default));
        });
    }

    [Fact]
    public async Task OperationsAreKeyedByAccountAndRequest()
    {
        await WithStore(async (store, scope) =>
        {
            var id = Guid.NewGuid();
            var cera = Operation(7, id, PlayerPortalOperationKind.Cera, "TokenCera", 500, Today, At);
            var mail = Operation(7, Guid.NewGuid(), PlayerPortalOperationKind.Mail, null, 0, Today, At.AddSeconds(1));
            await using (var transaction = scope.Begin())
            {
                await store.SaveOperationAsync(cera, default);
                await store.SaveOperationAsync(mail, default);
                await store.SaveOperationAsync(cera with { AccountId = 8 }, default);
                await Assert.ThrowsAsync<SqliteException>(async () => await store.SaveOperationAsync(cera with { Amount = 1 }, default));
                await transaction.CommitAsync(default);
            }

            Assert.Equal(cera, await store.FindOperationAsync(7, id, default));
            Assert.Equal(mail, await store.FindOperationAsync(7, mail.RequestId, default));
            Assert.Equal(cera with { AccountId = 8 }, await store.FindOperationAsync(8, id, default));
            Assert.Null(await store.FindOperationAsync(9, id, default));
        });
    }

    /// <summary>Cera and token cera have their own daily limits, so their usage never adds up.</summary>
    [Fact]
    public async Task UsageCountsOnlyTheSameAccountKindCurrencyAndDay()
    {
        await WithStore(async (store, scope) =>
        {
            await using (var transaction = scope.Begin())
            {
                foreach (var op in new[]
                         {
                             Operation(7, Guid.NewGuid(), PlayerPortalOperationKind.Cera, "Cera", 1000, Today, At),
                             Operation(7, Guid.NewGuid(), PlayerPortalOperationKind.Cera, "Cera", 300, Today, At.AddSeconds(1)),
                             Operation(7, Guid.NewGuid(), PlayerPortalOperationKind.Cera, "TokenCera", 40, Today, At.AddSeconds(2)),
                             Operation(7, Guid.NewGuid(), PlayerPortalOperationKind.Cera, "Cera", 7000, Today.AddDays(-1), At.AddDays(-1)),
                             Operation(8, Guid.NewGuid(), PlayerPortalOperationKind.Cera, "Cera", 9000, Today, At),
                             Operation(7, Guid.NewGuid(), PlayerPortalOperationKind.Mail, null, 250, Today, At.AddSeconds(3)),
                             Operation(7, Guid.NewGuid(), PlayerPortalOperationKind.Mail, null, 0, Today, At.AddSeconds(4)),
                             Operation(7, Guid.NewGuid(), PlayerPortalOperationKind.Mail, null, 0, Today.AddDays(1), At.AddDays(1)),
                         })
                    await store.SaveOperationAsync(op, default);
                await transaction.CommitAsync(default);
            }

            Assert.Equal(new PlayerPortalUsage(2, 1300), await store.UsageAsync(7, PlayerPortalOperationKind.Cera, "Cera", Today, default));
            Assert.Equal(new PlayerPortalUsage(1, 40), await store.UsageAsync(7, PlayerPortalOperationKind.Cera, "TokenCera", Today, default));
            Assert.Equal(new PlayerPortalUsage(1, 7000), await store.UsageAsync(7, PlayerPortalOperationKind.Cera, "Cera", Today.AddDays(-1), default));
            Assert.Equal(new PlayerPortalUsage(2, 250), await store.UsageAsync(7, PlayerPortalOperationKind.Mail, null, Today, default));
            Assert.Equal(new PlayerPortalUsage(0, 0), await store.UsageAsync(9, PlayerPortalOperationKind.Cera, "Cera", Today, default));
        });
    }

    [Fact]
    public async Task OperationPagesAreNewestFirstOwnAccountOnlyAndFilterByKind()
    {
        await WithStore(async (store, scope) =>
        {
            var ops = Enumerable.Range(0, 5).Select(i => Operation(7, Guid.NewGuid(), i % 2 == 0 ? PlayerPortalOperationKind.Cera : PlayerPortalOperationKind.Mail,
                i % 2 == 0 ? "Cera" : null, i, Today, At.AddMinutes(i))).ToArray();
            await using (var transaction = scope.Begin())
            {
                foreach (var op in ops) await store.SaveOperationAsync(op, default);
                await store.SaveOperationAsync(Operation(8, Guid.NewGuid(), PlayerPortalOperationKind.Cera, "Cera", 1, Today, At.AddHours(1)), default);
                await transaction.CommitAsync(default);
            }

            var first = await store.OperationsAsync(7, null, 1, 2, default);
            Assert.Equal((5L, 1, 2), (first.Total, first.Page, first.PageSize));
            Assert.Equal([ops[4], ops[3]], first.Items);
            Assert.Equal([ops[0]], (await store.OperationsAsync(7, null, 3, 2, default)).Items);
            var mails = await store.OperationsAsync(7, PlayerPortalOperationKind.Mail, 1, 20, default);
            Assert.Equal(2, mails.Total);
            Assert.Equal([ops[3], ops[1]], mails.Items);
            var cera = await store.OperationsAsync(7, PlayerPortalOperationKind.Cera, 1, 20, default);
            Assert.Equal([ops[4], ops[2], ops[0]], cera.Items);
            Assert.Equal(0, (await store.OperationsAsync(9, null, 1, 20, default)).Total);
        });
    }

    [Fact]
    public async Task WritesNeedAnOwnedTransaction()
    {
        await WithStore(async (store, _) =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.SaveSettingsAsync(PlayerPortalSettings.Default with { Enabled = true }, 0, default));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.SaveSessionAsync(new("x", 7, At, At.AddHours(8)), default));
            Assert.Equal(PlayerPortalSettings.Default, await store.ReadSettingsAsync(default));
        });
    }

    private static PlayerPortalOperation Operation(int account, Guid id, PlayerPortalOperationKind kind, string? currency, long amount, DateOnly day, DateTimeOffset at) =>
        new(account, id, kind, currency, amount, "hash-" + id.ToString("N")[..8], "{\"amount\":" + amount + "}", day, at);

    private static Task WithStore(Func<SqlitePlayerPortalPersistence, DomainTransactionScope, Task> body) =>
        WithStore((store, scope, _) => body(store, scope));

    private static async Task WithStore(Func<SqlitePlayerPortalPersistence, DomainTransactionScope, SqliteDatabase, Task> body)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "dfo-player-portal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "test.db");
            var scope = TestRequired.TransactionScope();
            var database = new SqliteDatabase(path, scope);
            try
            {
                await body(new SqlitePlayerPortalPersistence(database), scope, database);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(directory, true);
            }
        });
    }
}
