using System.Text;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Infrastructure.Accounts;
using Dfo.Server.Infrastructure.Characters;
using Dfo.Server.Infrastructure.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Tests.Schema;

/// <summary>
/// What shape the database actually ends up in, and what decides it.
/// </summary>
/// <remarks>
/// Tables are declared by whichever components happen to be constructed, several of them declare
/// the same table differently, and <c>CREATE TABLE IF NOT EXISTS</c> means the first declaration
/// to run wins while the rest are silently ignored. So the shape of a table has been a function of
/// construction order rather than of anything written down.
///
/// These tests pin that shape before it is moved anywhere, so that moving it can be shown to
/// change nothing, and ask the question the move exists to settle: does the order actually matter
/// today?
/// </remarks>
public sealed class SchemaOwnershipTests
{
    /// <remarks>
    /// If this fails, the database has more than one possible shape and which one a server runs
    /// with depends on the order its components happened to be built. That is the defect the whole
    /// move exists to remove, and the failure message names every column that differs.
    /// </remarks>
    [Fact]
    public async Task TheShapeOfTheDatabaseDoesNotDependOnWhatIsBuiltFirst()
    {
        var forwards = await ShapeAsync(reversed: false);
        var backwards = await ShapeAsync(reversed: true);

        Assert.Equal(forwards, backwards);
    }

    /// <remarks>
    /// Every table a component declares must be reachable after building only that component, or
    /// the component is quietly relying on someone else having been built first. The item store
    /// depends on exactly this today, which is why its own tests construct two repositories to get
    /// one store's tables.
    /// </remarks>
    [Fact]
    public async Task EveryTableIsDeclaredSomewhereRatherThanByAccident()
    {
        var shape = await ShapeAsync(reversed: false);

        // Counted rather than sampled: naming a handful of tables would stay green while forty of
        // the declarations went missing.
        Assert.Equal(52, shape.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("characters(", shape, StringComparison.Ordinal);
        Assert.Contains("character_new_items(", shape, StringComparison.Ordinal);
        Assert.Contains("accounts(", shape, StringComparison.Ordinal);
        Assert.Contains("character_active_quests(", shape, StringComparison.Ordinal);
        Assert.Contains("character_creatures(", shape, StringComparison.Ordinal);
        Assert.Contains("character_gold_limits(auction_gold_limit:INTEGER,character_id:INTEGER,gold_carry_limit:INTEGER,updated_at:TEXT)",
            shape, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Building one component and nothing else has to be enough. The item store relies on exactly
    /// this: it declares no table of its own, and used to work only because some repository had
    /// been constructed first and happened to declare the ones it writes.
    /// </remarks>
    [Fact]
    public async Task OneComponentBuiltAloneFindsEverythingItNeeds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-alone-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteCharacterPvpSkillProfileRepository(path);

            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN "
                + "('characters','character_new_items','accounts','character_active_quests')";

            Assert.Equal(4L, Convert.ToInt64(await command.ExecuteScalarAsync()));
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

    /// <summary>Builds a database and reports every table and column it ended up with.</summary>
    private static async Task<string> ShapeAsync(bool reversed)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-schema-{Guid.NewGuid():N}.db");
        try
        {
            var builders = new List<Action>
            {
                () => _ = new SqliteAccountRepository(path),
                () => _ = new SqliteAccountEntryStateRepository(path),
                () => _ = new SqliteCharacterRepository(path),
                () => _ = new SqliteCharacterEntryStateRepository(path, new GoldLimitRuleService()),
                () => _ = new SqliteCharacterPvpSkillProfileRepository(path),
                () => _ = new SqliteInventoryEntryRepository(path),
                () => _ = new SqliteEntryInventoryMutationRepository(
                    path, new DailyRefillRuleService(), new ItemArrivalRuleService()),
            };

            if (reversed)
            {
                builders.Reverse();
            }

            foreach (var build in builders)
            {
                build();
            }

            return await DescribeAsync(path);
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

    private static async Task<string> DescribeAsync(string path)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        await connection.OpenAsync();

        var tables = new List<string>();
        await using (var names = connection.CreateCommand())
        {
            names.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var reader = await names.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var shape = new StringBuilder();
        foreach (var table in tables)
        {
            var columns = new List<string>();
            await using var info = connection.CreateCommand();
            info.CommandText = $"PRAGMA table_info('{table}')";
            await using var reader = await info.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add($"{reader.GetString(1)}:{reader.GetString(2)}");
            }

            columns.Sort(StringComparer.Ordinal);
            shape.Append(table).Append('(').AppendJoin(',', columns).Append(')').Append('\n');
        }

        return shape.ToString();
    }
}
