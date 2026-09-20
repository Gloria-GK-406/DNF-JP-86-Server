using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Transactions;
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
    private CharacterDungeonDifficultyDomainService Service => new(new SqliteCharacterDungeonPermissionTable(new SqliteDatabase(path, scope)));

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
        Assert.Equal(3, rows.Count);
        Assert.All(rows.Where(row => row.DungeonId == 150), row => Assert.Equal(3, row.ClearState));
        Assert.Equal(2, rows.Single(row => row.DungeonId == 151).ClearState);
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
