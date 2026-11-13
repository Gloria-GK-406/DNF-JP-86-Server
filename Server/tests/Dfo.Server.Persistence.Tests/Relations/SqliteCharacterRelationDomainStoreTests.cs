using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Relations;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using static Dfo.Server.Abstractions.Relations.CharacterRelationKind;

namespace Dfo.Server.Persistence.Tests.Relations;

/// <summary>The relation rows as written, read back, and read back again from a reopened file.</summary>
public sealed class SqliteCharacterRelationDomainStoreTests : IDisposable
{
    private static readonly CharacterId A = new(71), B = new(72), C = new(73), Gone = new(74);
    private static readonly DateTimeOffset At = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private readonly string directory = Path.Combine(Path.GetTempPath(), $"dfo-relations-{Guid.NewGuid():N}");
    private readonly string path;
    private readonly DomainTransactionScope scope = TestRequired.TransactionScope();

    public SqliteCharacterRelationDomainStoreTests()
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "relations.db");
        _ = new SqliteDatabase(path, scope);
        Sql("""
            INSERT INTO accounts(account_id,m_id) VALUES(1,'one');
            INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,delete_flag) VALUES
                (71,1,'a',0,0,1,0,0),(72,1,'b',0,0,1,0,0),(73,1,'c',0,0,1,0,0),(74,1,'gone',0,0,1,0,1);
            """);
    }

    private SqliteCharacterRelationDomainStore Open() => new(new SqliteDatabase(path, scope));

    private Task WriteAsync(Func<SqliteCharacterRelationDomainStore, Task> body) => TestGameplay.RunAsync(async () =>
    {
        var store = Open();
        await using var transaction = scope.Begin();
        await body(store);
        await transaction.CommitAsync(default);
    });

    [Fact]
    public async Task Written_rows_read_back_the_same_after_the_database_is_reopened()
    {
        await WriteAsync(async store =>
        {
            Assert.True(await store.InsertAsync(A, C, Friend, At, default));
            Assert.True(await store.InsertAsync(A, B, Friend, At.AddSeconds(5), default));
            Assert.True(await store.InsertAsync(A, B, Blacklist, At.AddSeconds(9), default));
            Assert.True(await store.InsertAsync(B, A, Friend, At, default));
            Assert.False(await store.InsertAsync(A, C, Friend, At.AddSeconds(30), default));
        });

        SqliteConnection.ClearAllPools();
        var reopened = Open();
        Assert.Equal([new CharacterRelation(C, At), new CharacterRelation(B, At.AddSeconds(5))],
            await reopened.ListAsync(A, Friend, default));
        Assert.Equal([new CharacterRelation(B, At.AddSeconds(9))], await reopened.ListAsync(A, Blacklist, default));
        Assert.Equal([A], await reopened.OwnersOfAsync(B, Friend, default));
        Assert.Equal([A], await reopened.OwnersOfAsync(B, Blacklist, default));
        Assert.Equal([new CharacterRelation(A, At)], await reopened.ListAsync(B, Friend, default));
        Assert.Equal(2, await reopened.CountAsync(A, Friend, default));
        Assert.True(await reopened.ExistsAsync(A, B, Blacklist, default));
        Assert.False(await reopened.ExistsAsync(B, A, Blacklist, default));
    }

    [Fact]
    public async Task A_deleted_character_is_left_out_of_lists_counts_and_owners()
    {
        Sql($"""
            INSERT INTO character_relations(owner_id,target_id,kind,created_at) VALUES
                (71,74,{(int)Friend},1),(71,72,{(int)Friend},2),(74,71,{(int)Friend},1),(72,71,{(int)Friend},3);
            """);
        var store = Open();

        Assert.Equal([B], (await store.ListAsync(A, Friend, default)).Select(x => x.Target));
        Assert.Equal(1, await store.CountAsync(A, Friend, default));
        Assert.Equal([B], await store.OwnersOfAsync(A, Friend, default));
        // Only the owners are filtered: who holds a deleted character is still answered, for the purge.
        Assert.Equal([A], await store.OwnersOfAsync(Gone, Friend, default));
    }

    [Fact]
    public async Task Delete_says_whether_there_was_a_row_and_takes_only_that_one()
    {
        await WriteAsync(async store =>
        {
            await store.InsertAsync(A, B, Friend, At, default);
            await store.InsertAsync(B, A, Friend, At, default);
            await store.InsertAsync(A, B, Blacklist, At, default);
            Assert.True(await store.DeleteAsync(A, B, Friend, default));
            Assert.False(await store.DeleteAsync(A, B, Friend, default));
        });

        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM character_relations"));
        Assert.True(await Open().ExistsAsync(B, A, Friend, default));
        Assert.True(await Open().ExistsAsync(A, B, Blacklist, default));
    }

    [Fact]
    public async Task Purge_returns_the_live_friend_owners_and_removes_every_row_on_either_side()
    {
        Sql($"""
            INSERT INTO character_relations(owner_id,target_id,kind,created_at) VALUES
                (72,73,{(int)Friend},1),(71,73,{(int)Friend},1),(74,73,{(int)Friend},1),
                (71,73,{(int)Blacklist},1),(73,71,{(int)Friend},1),(73,72,{(int)Blacklist},1),(71,72,{(int)Friend},1);
            """);
        IReadOnlyList<CharacterId> observers = [];

        await WriteAsync(async store => observers = await store.PurgeAsync(C, default));

        Assert.Equal([A, B], observers);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM character_relations WHERE owner_id=73 OR target_id=73"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM character_relations"));
    }

    [Fact]
    public async Task The_table_refuses_a_self_relation_and_a_character_that_does_not_exist()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var store = Open();
            await using var transaction = scope.Begin();
            await Assert.ThrowsAsync<SqliteException>(() => store.InsertAsync(A, A, Friend, At, default).AsTask());
            await Assert.ThrowsAsync<SqliteException>(() => store.InsertAsync(A, new CharacterId(9999), Friend, At, default).AsTask());
        });
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM character_relations"));
    }

    [Fact]
    public async Task Writing_outside_a_use_case_is_refused()
    {
        var store = Open();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InsertAsync(A, B, Friend, At, default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync(A, B, Friend, default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PurgeAsync(A, default).AsTask());
    }

    private void Sql(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { /* A file the runtime still holds is left for the temp directory to reap. */ }
    }
}
