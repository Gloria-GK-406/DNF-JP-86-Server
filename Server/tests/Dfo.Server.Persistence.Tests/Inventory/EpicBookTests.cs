using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Inventory;

public sealed class EpicBookTests
{
    private const int Piece = 10300000;
    [Fact]
    public async Task Saturation_account_isolation_reopen_and_read_only_queries()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            await using (var tx = fixture.Scope.Begin())
            {
                var first = await fixture.Book.AddPiecesAsync(new(1), Piece, int.MaxValue - 2, default);
                Assert.Equal(int.MaxValue - 2, first.Total);
                var next = await fixture.Book.AddPiecesAsync(new(1), Piece, 20, default);
                Assert.Equal(2, next.Added);
                Assert.Equal(int.MaxValue, next.Total);
                Assert.Equal(0, (await fixture.Book.AddPiecesAsync(new(1), Piece, 1, default)).Added);
                await tx.CommitAsync(default);
            }
            var reopened = new EpicBookDomainService(new EpicBookRuleService(new Catalog()),
                new SqliteEpicBookDomainStore(new SqliteDatabase(fixture.Path, fixture.Scope)), fixture.Scope);
            await using (var tx = fixture.Scope.Begin(readOnly: true))
            {
                Assert.Equal(int.MaxValue, Assert.Single(await reopened.GetBalancesAsync(new(1), default)).Count);
                Assert.Empty(await reopened.GetBalancesAsync(new(2), default));
                Assert.Equal(0, Assert.Single(await reopened.GetBookAsync(new(2), default)).Count);
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await reopened.AddPiecesAsync(new(1), Piece, 1, default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM account_epic_pieces"));
            await Assert.ThrowsAsync<ArgumentException>(async () => await fixture.Book.AddPiecesAsync(new(1), 999, 1, default));
        });
    }

    [Fact]
    public async Task Rollback_discards_addition_and_missing_owner_is_rejected()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            await using (fixture.Scope.Begin()) await fixture.Book.AddPiecesAsync(new(1), Piece, 3, default);
            Assert.Empty(await fixture.Book.GetBalancesAsync(new(1), default));
            await using var tx = fixture.Scope.Begin();
            await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Book.AddPiecesAsync(new(99), Piece, 3, default));
        });
    }

    [Fact]
    public async Task Legacy_characters_and_account_cargo_merge_once_without_consuming_ordinary_items()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            fixture.Legacy("character", 1, 0, 1, Piece, 7);
            fixture.Legacy("character", 2, 0, 1, Piece, 11);
            fixture.Legacy("account", 1, 12, 1, Piece, 13);
            fixture.Legacy("character", 3, 0, 1, Piece, 17);
            fixture.Legacy("character", 1, 0, 2, 3037, 9);
            await fixture.Book.InitializeAsync(default);
            await fixture.Book.InitializeAsync(default);
            Assert.Equal(31, Assert.Single(await fixture.Book.GetBalancesAsync(new(1), default)).Count);
            Assert.Equal(17, Assert.Single(await fixture.Book.GetBalancesAsync(new(2), default)).Count);
            Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM item_slots"));
            Assert.Equal(9, fixture.Scalar("SELECT count FROM item_slots WHERE id=3037"));
        });
    }

    [Fact]
    public async Task Migration_saturates_and_a_failure_rolls_back_all_custody_and_balances()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            fixture.Legacy("character", 1, 0, 1, Piece, int.MaxValue);
            fixture.Legacy("character", 2, 0, 1, Piece, 5);
            fixture.Sql("CREATE TRIGGER refuse_delete BEFORE DELETE ON item_slots WHEN OLD.owner_id=2 BEGIN SELECT RAISE(ABORT,'forced'); END;");
            await Assert.ThrowsAsync<SqliteException>(async () => await fixture.Book.InitializeAsync(default));
            Assert.Equal(2, fixture.Scalar("SELECT COUNT(*) FROM item_slots"));
            Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM account_epic_pieces"));
            fixture.Sql("DROP TRIGGER refuse_delete");
            await fixture.Book.InitializeAsync(default);
            Assert.Equal(int.MaxValue, Assert.Single(await fixture.Book.GetBalancesAsync(new(1), default)).Count);
            Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM item_slots"));
        });
    }

    [Fact]
    public async Task Invalid_legacy_core_refuses_conversion_without_losing_custody()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            fixture.Legacy("character", 1, 0, 1, Piece, 7);
            fixture.Sql("UPDATE items SET item_core=zeroblob(82)");
            await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Book.InitializeAsync(default));
            Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM item_slots"));
            Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM account_epic_pieces"));
        });
    }

    private sealed class Catalog : IEpicBookDefinitionSource
    {
        public IReadOnlyList<EpicPieceDefinition> Load() => [new(Piece, 450004)];
    }
    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"epic-book-{Guid.NewGuid():N}.db");
        public DomainTransactionScope Scope { get; } = new();
        public EpicBookDomainService Book { get; }
        private readonly SqliteConnection connection;
        public Fixture()
        {
            var database = new SqliteDatabase(Path, Scope);
            Book = new(new EpicBookRuleService(new Catalog()), new SqliteEpicBookDomainStore(database), Scope);
            connection = new($"Data Source={Path}"); connection.Open();
            Sql("INSERT INTO accounts(account_id,m_id) VALUES(1,'epic1'),(2,'epic2');"
                + "INSERT INTO characters(character_id,account_id,name) VALUES(1,1,'one'),(2,1,'two'),(3,2,'three')");
        }
        public void Legacy(string scope, int owner, int list, int slot, int id, int count)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO items(item_core) VALUES(@core);"
                + "INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) "
                + "VALUES(@scope,@owner,@list,@slot,'template',@id,last_insert_rowid(),@count)";
            command.Parameters.AddWithValue("@core", InventoryItemCore.Encode(StackableItemCore.Create(14, id, count)));
            command.Parameters.AddWithValue("@scope", scope); command.Parameters.AddWithValue("@owner", owner);
            command.Parameters.AddWithValue("@list", list); command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@count", count);
            command.ExecuteNonQuery();
        }
        public void Sql(string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
        public long Scalar(string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(command.ExecuteScalar()); }
        public void Dispose() { connection.Dispose(); SqliteConnection.ClearAllPools(); File.Delete(Path); }
    }
}
