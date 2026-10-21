using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Administration;
using Dfo.Server.Persistence.Administration;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Administration;

public sealed class GmAuditTransactionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BusinessMutationAndItsAuditShareCommitOrRollback(bool commit)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "dfo-gm-audit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "test.db"); var scope = new DomainTransactionScope();
            var database = new SqliteDatabase(path, scope); IGmAdministrationPersistence persistence = new SqliteGmAdministrationPersistence(database);
            await using (var transaction = scope.Begin())
            {
                await persistence.CreateAdministratorAsync("transaction-test", "safe-fixture-record", GmPermission.QueryPlayers, default);
                await persistence.AppendAuditAsync(new("test-operation", DateTimeOffset.UtcNow, "TestMutation", null, null, "fixture", "Succeeded", "隔离事务验证"), default);
                if (commit) await transaction.CommitAsync(default);
            }
            Assert.Equal(commit, await persistence.HasAdministratorAsync(default));
            Assert.Equal(commit ? 1 : 0, (await persistence.QueryAuditAsync(1, 20, GmAuditFilter.All, default)).Total);
            using var connection = new SqliteConnection($"Data Source={path}"); SqliteConnection.ClearPool(connection);
            Directory.Delete(directory, true);
        });
    }
    [Fact]
    public async Task AuditFilterIsAppliedToItemsAndTotal()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "dfo-gm-audit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "test.db"); var scope = new DomainTransactionScope();
            var persistence = new SqliteGmAdministrationPersistence(new SqliteDatabase(path, scope)); var at = DateTimeOffset.UtcNow;
            await using (var transaction = scope.Begin())
            {
                var rows = new[] { ("Login", "Succeeded"), ("Login", "Rejected"), ("SendOfficialMail", "RolledBack"), ("SendOfficialMail", "Succeeded"), ("GrantCera", "Unknown") };
                for (var i = 0; i < rows.Length; i++) await persistence.AppendAuditAsync(new("op-" + i, at.AddSeconds(i), rows[i].Item1, null, null, "fixture", rows[i].Item2, "筛选验证"), default);
                await transaction.CommitAsync(default);
            }
            var attention = await persistence.QueryAuditAsync(1, 2, new(Outcome: GmAuditFilter.Attention), default);
            Assert.Equal(3, attention.Total); Assert.Equal(["op-4", "op-2"], attention.Items.Select(r => r.OperationId));
            Assert.Equal(2, (await persistence.QueryAuditAsync(1, 20, new("SendOfficialMail"), default)).Total);
            Assert.Equal("op-3", Assert.Single((await persistence.QueryAuditAsync(1, 20, new("SendOfficialMail", "Succeeded"), default)).Items).OperationId);
            Assert.Equal(5, (await persistence.QueryAuditAsync(1, 20, GmAuditFilter.All, default)).Total);
            using var connection = new SqliteConnection($"Data Source={path}"); SqliteConnection.ClearPool(connection); Directory.Delete(directory, true);
        });
    }
    [Fact]
    public async Task AuditCannotWriteOutsideAnOwnedTransaction()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dfo-gm-audit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.db"); var database = new SqliteDatabase(path, new DomainTransactionScope());
        var persistence = new SqliteGmAdministrationPersistence(database);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await persistence.AppendAuditAsync(new("test", DateTimeOffset.UtcNow, "Test", null, null, "fixture", "Succeeded", "隔离事务验证"), default));
        Assert.Equal(0, (await persistence.QueryAuditAsync(1, 20, GmAuditFilter.All, default)).Total);
        using var connection = new SqliteConnection($"Data Source={path}"); SqliteConnection.ClearPool(connection); Directory.Delete(directory, true);
    }
}
