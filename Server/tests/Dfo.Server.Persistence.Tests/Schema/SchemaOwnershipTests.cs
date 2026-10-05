using System.Text;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Schema;

/// <summary>
/// What shape the database actually ends up in, and what decides it.
/// </summary>
/// <remarks>
/// Tables used to be declared by whichever components happened to be constructed. Several of them
/// declared the same table differently, and <c>CREATE TABLE IF NOT EXISTS</c> means the first
/// declaration to run wins while the rest are silently ignored -- so the shape of a table was a
/// function of construction order rather than of anything written down.
///
/// That question is settled: one component declares the tables and every repository is handed it,
/// so there is no longer an order to get wrong. What is left to pin is the shape itself, that
/// building the database is enough on its own, and that bringing an existing database into shape
/// again leaves it as it was -- which is not free, because bringing it into shape also tops up
/// columns on tables that are already there, and a top-up that stopped noticing they were there
/// would fail on the second attempt.
/// </remarks>
public sealed class SchemaOwnershipTests
{
    /// <remarks>
    /// Bringing a database into shape happens every time one is opened, including one that has
    /// been in shape for a long time. If this fails, doing it a second time changed something --
    /// which for a database with players in it is a change nobody asked for, and the failure
    /// message names every column that differs.
    ///
    /// A database built from nothing has every column already, so this exercises the guard on the
    /// top-ups rather than the top-ups themselves. That is what it is for: a guard that stopped
    /// working would make the second attempt fail, and nothing else here would notice.
    /// </remarks>
    [Fact]
    public async Task BringingADatabaseIntoShapeAgainLeavesItAsItWas()
    {
        var once = await ShapeAsync(times: 1);
        var again = await ShapeAsync(times: 2);

        Assert.Equal(once, again);
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
        var shape = await ShapeAsync();

        // Counted rather than sampled: naming a handful of tables would stay green while forty of
        // the declarations went missing.
        Assert.Equal(58, shape.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("daily_refill_settings(id:INTEGER,input:TEXT,revision:INTEGER,updated_at:INTEGER,updated_by:TEXT)", shape, StringComparison.Ordinal);
        Assert.Contains("initialization_settings(input:TEXT,kind:TEXT,revision:INTEGER,updated_at:INTEGER,updated_by:TEXT)", shape, StringComparison.Ordinal);
        Assert.Contains("schema_migrations(applied_at:TEXT,name:TEXT)", shape, StringComparison.Ordinal);
        Assert.Contains("gm_administrators(enabled:INTEGER,id:INTEGER,login_name:TEXT,password_record:TEXT,permissions:INTEGER)", shape, StringComparison.Ordinal);
        Assert.Contains("gm_sessions(administrator_id:INTEGER,expires_at:INTEGER,token_hash:TEXT)", shape, StringComparison.Ordinal);
        Assert.Contains("gm_audit(action:TEXT,administrator_id:INTEGER,administrator_name:TEXT,occurred_at:INTEGER,operation_id:TEXT,outcome:TEXT,reason:TEXT,target:TEXT)", shape, StringComparison.Ordinal);
        Assert.Contains("gm_mail_drafts(administrator_id:INTEGER,document:TEXT)", shape, StringComparison.Ordinal);
        Assert.Contains("gm_mail_templates(administrator_id:INTEGER,document:TEXT,id:TEXT,name:TEXT)", shape, StringComparison.Ordinal);
        Assert.Contains("gm_mail_operations(administrator_id:INTEGER,committed_at:INTEGER,payload_hash:TEXT,receipt:TEXT,request_id:TEXT)", shape, StringComparison.Ordinal);
        Assert.Contains("gm_cera_operations(administrator_id:INTEGER,committed_at:INTEGER,payload_hash:TEXT,receipt:TEXT,request_id:TEXT)", shape, StringComparison.Ordinal);
        Assert.Contains("characters(", shape, StringComparison.Ordinal);
        Assert.Contains("items(", shape, StringComparison.Ordinal);
        Assert.Contains("item_slots(", shape, StringComparison.Ordinal);
        Assert.Contains("accounts(", shape, StringComparison.Ordinal);
        Assert.Contains("account_purchase_restrictions(account_id:INTEGER,end_time:INTEGER,restriction_id:INTEGER)", shape, StringComparison.Ordinal);
        Assert.DoesNotContain("login_tickets(", shape, StringComparison.Ordinal);
        Assert.Contains("character_active_quests(", shape, StringComparison.Ordinal);
        Assert.Contains("npc_shop_purchase_counts(count:INTEGER,item_id:INTEGER,npc_id:INTEGER,owner_id:INTEGER,owner_scope:TEXT,period:TEXT)",
            shape, StringComparison.Ordinal);

        // The creatures table is named here as one that is gone. Its eight facts are columns of
        // the world row the creature is, so a database still declaring it would be one where a
        // creature could be recorded twice and the two could disagree.
        Assert.DoesNotContain("character_creatures(", shape, StringComparison.Ordinal);
        // One table spelled out column by column, so that a declaration quietly losing one fails
        // here rather than only wherever it happens to be read. The gold limits used to be a table
        // of their own; they are caps derived from level, so they sit with the other derived
        // numbers now, and this is where that shows.
        Assert.Contains(
            "character_settings(character_id:INTEGER,character_option_blob:BLOB,user_state_bits:INTEGER)",
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
            _ = new SqliteDatabase(path, new DomainTransactionScope());

            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN "
                + "('characters','items','accounts','character_active_quests')";

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
    private static async Task<string> ShapeAsync(int times = 1)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-schema-{Guid.NewGuid():N}.db");
        try
        {
            for (var attempt = 0; attempt < times; attempt++)
            {
                _ = new SqliteDatabase(path, new DomainTransactionScope());
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
