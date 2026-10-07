using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

public sealed class CharacterDungeonDifficultyTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"difficulty-{Guid.NewGuid():N}.db");
    private readonly DomainTransactionScope scope = new();
    private CharacterDungeonDifficultyDomainService Service => new(new SqliteCharacterDungeonPermissionTable(new SqliteDatabase(path, scope)), new DungeonDifficultyRuleService(new Definitions()));

    public CharacterDungeonDifficultyTests()
    {
        _ = Service;
        Execute("INSERT INTO accounts(account_id,m_id) VALUES(1,'a'),(2,'b'); INSERT INTO characters(character_id,account_id,name) VALUES(1,1,'a1'),(2,2,'b1'),(3,1,'a2');").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Default_is_normal_and_unlock_is_monotonic_isolated_and_durable()
    {
        var character = new CharacterId(1);
        var service = Service;
        Assert.Equal(DungeonDifficulty.Normal, await service.GetMaximumAsync(character, 150, default));
        await using (var transaction = scope.Begin())
        {
            Assert.Equal(DungeonDifficulty.Adventure, await service.UnlockAsync(character, 150, DungeonDifficulty.Adventure, default));
            Assert.Equal(DungeonDifficulty.Adventure, await service.UnlockAsync(character, 150, DungeonDifficulty.Adventure, default));
            Assert.Equal(DungeonDifficulty.Adventure, await service.UnlockAsync(character, 150, DungeonDifficulty.Normal, default));
            await transaction.CommitAsync(default);
        }
        var reloaded = Service;
        Assert.Equal(DungeonDifficulty.Adventure, await reloaded.GetMaximumAsync(character, 150, default));
        Assert.Equal(DungeonDifficulty.Normal, await reloaded.GetMaximumAsync(character, 151, default));
        Assert.Equal(DungeonDifficulty.Normal, await reloaded.GetMaximumAsync(new CharacterId(2), 150, default));
        Assert.Equal(DungeonDifficulty.Adventure, await reloaded.GetMaximumAsync(new CharacterId(3), 150, default));
        Assert.Single(await reloaded.GetRecordedAsync(character, default));
    }

    [Fact]
    public async Task Rollback_does_not_unlock_or_cache_uncommitted_progress()
    {
        var service = Service;
        await using (scope.Begin())
            await service.UnlockAsync(new CharacterId(1), 150, DungeonDifficulty.Adventure, default);
        Assert.Equal(DungeonDifficulty.Normal, await service.GetMaximumAsync(new CharacterId(1), 150, default));
        Assert.Empty(await Service.GetRecordedAsync(new CharacterId(1), default));
    }

    [Fact]
    public async Task Shared_permission_queries_are_read_only_and_do_not_materialize_legacy_rows()
    {
        await Execute("INSERT INTO character_dungeon_permissions VALUES(1,0,150,2);");
        var service = Service;
        await using (var query = scope.Begin(readOnly: true))
        {
            Assert.Equal(DungeonDifficulty.Tier2, await service.GetMaximumAsync(new(3),150,default));
            Assert.Single(await service.GetRecordedAsync(new(3),default));
            await query.CommitAsync(default);
        }
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM account_dungeon_permissions;";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Legacy_duplicates_keep_maximum_and_other_rows_unchanged()
    {
        var service = Service;
        await Execute("INSERT INTO character_dungeon_permissions VALUES(1,0,150,0),(1,1,150,1),(1,2,151,2);");
        Assert.Equal(DungeonDifficulty.Adventure, await service.GetMaximumAsync(new CharacterId(1), 150, default));
        await using (var transaction = scope.Begin())
        {
            await service.UnlockAsync(new CharacterId(1), 150, DungeonDifficulty.Tier3, default);
            await transaction.CommitAsync(default);
        }
        var rows = await new SqliteCharacterDungeonPermissionTable(new SqliteDatabase(path, scope)).ReadAsync(new CharacterId(1), default);
        Assert.Equal(4, rows.Count);
        Assert.Equal(3, rows.Where(row => row.DungeonId == 150).Max(row => row.ClearState));
        Assert.Equal(2, rows.Single(row => row.DungeonId == 151).ClearState);
        Assert.Equal(DungeonDifficulty.Tier3, await service.GetMaximumAsync(new CharacterId(3), 150, default));
    }

    [Fact]
    public async Task Clear_unlocks_next_supported_tier_once_and_rollback_leaves_it_locked()
    {
        var service = new CharacterDungeonDifficultyDomainService(
            new SqliteCharacterDungeonPermissionTable(new SqliteDatabase(path, scope)),
            new DungeonDifficultyRuleService(new Definitions()));
        await using (scope.Begin())
            Assert.Equal(DungeonDifficulty.Adventure,
                (await service.UnlockAfterClearAsync(new(1), 150, DungeonDifficulty.Normal, default))!.Maximum);
        Assert.Equal(DungeonDifficulty.Normal, await service.GetMaximumAsync(new(3), 150, default));
        await using (var transaction = scope.Begin())
        {
            Assert.NotNull(await service.UnlockAfterClearAsync(new(1), 150, DungeonDifficulty.Normal, default));
            Assert.Null(await service.UnlockAfterClearAsync(new(3), 150, DungeonDifficulty.Normal, default));
            Assert.Equal(DungeonDifficulty.Tier3,
                (await service.UnlockAfterClearAsync(new(1), 150, DungeonDifficulty.Adventure, default))!.Maximum);
            Assert.Null(await service.UnlockAfterClearAsync(new(1), 150, DungeonDifficulty.Tier3, default));
            Assert.Null(await service.UnlockAfterClearAsync(new(1), 151, DungeonDifficulty.Normal, default));
            await transaction.CommitAsync(default);
        }
        Assert.Equal(DungeonDifficulty.Tier3, await Service.GetMaximumAsync(new(3), 150, default));
    }

    private sealed class Definitions : IDungeonDefinitionSource
    {
        public ValueTask<DungeonDefinition?> GetAsync(int dungeonId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<DungeonDefinition?>(new(dungeonId, "dungeon/test.dgn", 1, true, [],
                SupportedDifficulties: dungeonId == 150 ? [0,1,3] : [0]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(255)]
    public async Task Unknown_legacy_states_are_rejected_without_silent_rewrite(int raw)
    {
        var service = Service;
        await Execute($"INSERT INTO character_dungeon_permissions VALUES(1,0,150,{raw});");
        await Assert.ThrowsAsync<InvalidDataException>(async () => await service.GetMaximumAsync(new CharacterId(1), 150, default));
        var rows = await new SqliteCharacterDungeonPermissionTable(new SqliteDatabase(path, scope)).ReadAsync(new CharacterId(1), default);
        Assert.Equal(raw, Assert.Single(rows).ClearState);
    }

    private async Task Execute(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
    }
}
