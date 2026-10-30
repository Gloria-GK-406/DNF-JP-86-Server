using Dfo.Server.Testing;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Schema;

/// <summary>
/// The one table a character's skills live in, against the shape the ruling settled on.
/// </summary>
/// <remarks>
/// Two tables become one, and the asymmetry between them is the whole difficulty: the PvP table
/// declared four things the PvE table declared none of. Each has to arrive here, and the one that
/// is about a page has to be qualified by mode on the way, because a PvE page index of two is
/// ordinary while a PvP one is out of range.
///
/// The declaration is asserted rather than only the behaviour, because a constraint that vanishes
/// from a declaration refuses nothing and reports nothing.
/// </remarks>
public sealed class SkillCarrierShapeTests
{
    /// <summary>character_skills -- 7 columns.</summary>
    private static readonly string[] CharacterSkills =
    [
        "character_id", "extra_values", "level", "mode", "page_index", "skill_id", "slot",
    ];

    [Fact]
    public async Task TheSkillTableHasExactlyTheColumnsTheRulingGivesIt()
    {
        var shape = await ShapeAsync();

        Assert.True(shape.ContainsKey("character_skills"));
        Assert.Equal(CharacterSkills, shape["character_skills"]);
    }

    [Fact]
    public async Task ThePvpSkillTableIsGoneAndTheJobSpecificPagesAreNot()
    {
        var shape = await ShapeAsync();

        Assert.False(shape.ContainsKey("character_pvp_skills"));
        Assert.True(shape.ContainsKey("character_dark_knight_combo_skill_pages"));
        Assert.True(shape.ContainsKey("character_knight_shield_deck"));
    }

    /// <remarks>
    /// The four the PvP table declared and the PvE table did not, plus the key that now has to name
    /// the mode and the check that says which two words a mode may be.
    /// </remarks>
    [Theory]
    [InlineData("PRIMARY KEY (character_id, mode, page_index, slot)")]
    [InlineData("UNIQUE (character_id, mode, page_index, skill_id)")]
    [InlineData("CHECK (mode <> 'pvp' OR page_index BETWEEN 0 AND 1)")]
    [InlineData("CHECK (mode IN ('pve', 'pvp'))")]
    [InlineData("FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE")]
    public async Task TheSkillTableCarriesWhatOnlyOneOfItsSourcesDeclared(string constraint)
    {
        var sql = Squeeze(await DeclarationAsync("character_skills"));

        Assert.Contains(constraint, sql, StringComparison.Ordinal);
    }

    /// <remarks>
    /// The tightening: these were required of a PvP row and of nothing else. They bind both kinds
    /// now, which is safe because every path that writes either kind supplies all six.
    /// </remarks>
    [Theory]
    [InlineData("character_id")]
    [InlineData("mode")]
    [InlineData("page_index")]
    [InlineData("slot")]
    [InlineData("skill_id")]
    [InlineData("level")]
    public async Task EverySkillColumnButTheBlobIsRequired(string column)
    {
        var sql = Squeeze(await DeclarationAsync("character_skills"));
        var start = sql.IndexOf(column + " ", StringComparison.Ordinal);

        Assert.True(start >= 0, $"character_skills has no column named {column}.");
        Assert.Contains(
            "NOT NULL", sql[start..sql.IndexOf(',', start)], StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ShapeAsync() =>
        await WithAFreshDatabaseAsync(async connection =>
        {
            var tables = new List<string>();
            await using (var names = connection.CreateCommand())
            {
                names.CommandText =
                    "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                await using var reader = await names.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            var shape = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var table in tables)
            {
                var columns = new List<string>();
                await using var info = connection.CreateCommand();
                info.CommandText = $"PRAGMA table_info('{table}')";
                await using var reader = await info.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns.Add(reader.GetString(1));
                }

                columns.Sort(StringComparer.Ordinal);
                shape[table] = columns;
            }

            return (IReadOnlyDictionary<string, IReadOnlyList<string>>)shape;
        });

    private static async Task<string> DeclarationAsync(string table) =>
        await WithAFreshDatabaseAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@name";
            command.Parameters.AddWithValue("@name", table);

            return await command.ExecuteScalarAsync() as string
                ?? throw new InvalidOperationException($"{table} is not in the database at all.");
        });

    /// <summary>Whitespace made uniform, so an assertion is about the text and not its layout.</summary>
    private static string Squeeze(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static async Task<T> WithAFreshDatabaseAsync<T>(Func<SqliteConnection, Task<T>> read)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-skillshape-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, TestRequired.TransactionScope());

            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();

            return await read(connection);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }
        }
    }
}
