using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

/// <summary>Finding live characters by exact name bytes and reading several by identifier.</summary>
/// <remarks>
/// Every column is a different number and the names differ only in ways a loose comparison would
/// erase -- case, a trailing byte, the legacy text column -- so a wrong column or a forgiving match
/// moves an assertion.
/// </remarks>
public sealed class CharacterIdentityQueryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"dfo-identity-query-{Guid.NewGuid():N}");
    private readonly SqliteCharacterIdentityRepository repository;

    public CharacterIdentityQueryTests()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "identity.db");
        repository = new SqliteCharacterIdentityRepository(new SqliteDatabase(path, TestRequired.TransactionScope()));
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        // 72 has only the legacy text name; 73 is deleted and shares its name with live 75.
        command.CommandText = """
            INSERT INTO accounts(account_id,m_id) VALUES(1,'one');
            INSERT INTO characters(character_id,account_id,name,name_bytes,job,grow_type,level,exp,delete_flag) VALUES
                (71,1,'Alpha',X'416C706861',3,1,61,0,0),
                (72,1,'legacy',NULL,4,2,62,0,0),
                (73,1,'Twin',X'5477696E',5,0,63,0,1),
                (75,1,'Twin',X'5477696E',6,3,64,0,0),
                (76,1,'bytes',X'E3818200',7,0,65,0,0);
            """;
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task A_name_matches_its_exact_bytes_only_among_live_characters()
    {
        var alpha = await repository.FindLiveByNameAsync("Alpha"u8.ToArray(), default);
        Assert.NotNull(alpha);
        Assert.Equal((new CharacterId(71), 61, 3, 1), (alpha.Character, alpha.Level, alpha.Job, alpha.GrowType));
        Assert.Equal("Alpha"u8.ToArray(), alpha.Name);

        Assert.Null(await repository.FindLiveByNameAsync("alpha"u8.ToArray(), default));
        Assert.Null(await repository.FindLiveByNameAsync("Alph"u8.ToArray(), default));
        Assert.Equal(new CharacterId(72), (await repository.FindLiveByNameAsync("legacy"u8.ToArray(), default))?.Character);
        Assert.Equal(new CharacterId(75), (await repository.FindLiveByNameAsync("Twin"u8.ToArray(), default))?.Character);
        Assert.Equal(new CharacterId(76), (await repository.FindLiveByNameAsync([0xE3, 0x81, 0x82, 0x00], default))?.Character);
        Assert.Null(await repository.FindLiveByNameAsync([0xE3, 0x81, 0x82], default));
        Assert.Null(await repository.FindLiveByNameAsync("Nobody"u8.ToArray(), default));
    }

    [Fact]
    public async Task Several_characters_read_at_once_leave_out_the_deleted_and_the_missing()
    {
        var read = await repository.ReadLiveAsync([new(76), new(73), new(71), new(9999), new(72), new(71)], default);

        Assert.Equal([71, 72, 76], read.Select(x => x.Character.Value).Order());
        var legacy = Assert.Single(read, x => x.Character == new CharacterId(72));
        Assert.Equal("legacy"u8.ToArray(), legacy.Name);
        Assert.Equal((62, 4, 2), (legacy.Level, legacy.Job, legacy.GrowType));
        Assert.Empty(await repository.ReadLiveAsync([], default));
    }

    [Fact]
    public async Task A_read_larger_than_one_statement_still_answers_every_character()
    {
        var many = Enumerable.Range(10_000, 1500).Select(x => new CharacterId(x)).Append(new CharacterId(75)).ToArray();
        Assert.Equal([new CharacterId(75)], (await repository.ReadLiveAsync(many, default)).Select(x => x.Character));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { /* A file the runtime still holds is left for the temp directory to reap. */ }
    }
}
