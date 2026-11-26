using DbMigrate.Legacy;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate;

/// <summary>Takes a database from the schema version it is at to the one the server needs.</summary>
/// <remarks>
/// Each step takes one version to the next and is listed under the version it starts from. A step
/// may run in several transactions of its own, so a database is backed up before the first one; a
/// step that fails leaves the version where it was, and the backup is what to go back to.
/// </remarks>
public static class Migrator
{
    private static readonly IReadOnlyDictionary<long, Action<SqliteConnection>> Steps =
        new Dictionary<long, Action<SqliteConnection>>
        {
            [0] = LegacyUpgrade.Apply,
            [1] = AddDropDrawMultiplier,
            [2] = ManagedEventActivationUpgrade.Apply,
            [3] = ExtraEquipmentSlotBackfill.Apply,
            [4] = CharacterRelationsUpgrade.Apply,
            [5] = AdventureGroupColumnRetirement.Apply,
            [6] = AuctionTablesUpgrade.Apply,
            [7] = PrivateStoreCustodyUpgrade.Apply,
            [8] = AuraSkinSlotUpgrade.Apply,
            [9] = PlayerPortalUpgrade.Apply,
            [10] = AuctionQuantityOwnershipUpgrade.Apply,
            [11] = GameplayParameterStorageRetirement.Apply,
            [12] = AntonRaidRewardUpgrade.Apply,
            [13] = AddGuilds,
            [14] = AddRecommendedGuildChannel,
            [15] = AddGuildDonationPoints,
            [16] = AddGuildDonationHistory,
            [17] = AddGuildAttendance,
            [18] = AddGuildRankSettings,
        };

    private static void AddGuilds(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuilds(connection, transaction);
        transaction.Commit();
    }

    private static void AddDropDrawMultiplier(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS drop_draw_multiplier(entry_key TEXT PRIMARY KEY,multiplier INTEGER NOT NULL CHECK(multiplier BETWEEN 1 AND 100));";
        command.ExecuteNonQuery();
    }

    private static void AddRecommendedGuildChannel(SqliteConnection connection)
    {
        if (Shape.HasColumn(connection, null, "guilds", "recommended_channel")) return;
        using var command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE guilds ADD COLUMN recommended_channel INTEGER NOT NULL DEFAULT 0 CHECK(recommended_channel >= 0);
            """;
        command.ExecuteNonQuery();
    }

    private static void AddGuildDonationPoints(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (!Shape.HasColumn(connection, transaction, "guilds", "gm_points"))
        {
            command.CommandText = """
                ALTER TABLE guilds ADD COLUMN gm_points INTEGER NOT NULL DEFAULT 0
                    CHECK(gm_points BETWEEN 0 AND 2147483647);
                """;
            command.ExecuteNonQuery();
        }
        if (!Shape.HasColumn(connection, transaction, "guild_members", "donation_points"))
        {
            command.CommandText = """
                ALTER TABLE guild_members ADD COLUMN donation_points INTEGER NOT NULL DEFAULT 0
                    CHECK(donation_points BETWEEN 0 AND 2147483647);
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void AddGuildDonationHistory(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildDonationHistory(connection, transaction);
        transaction.Commit();
    }

    private static void AddGuildAttendance(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildAttendance(connection, transaction);
        transaction.Commit();
    }

    private static void AddGuildRankSettings(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildRankSettings(connection, transaction);
        transaction.Commit();
    }

    /// <summary>The outcome of one run, for the caller to report.</summary>
    public sealed record Result(long From, long To, string? Backup);

    public static Result Migrate(string databasePath, bool backup = true)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("There is no database to migrate.", databasePath);

        // An offline migration must release its file handle on return, including when invoked in-process.
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        connection.Open();
        var from = CharacterDatabaseSchema.VersionOf(connection);
        if (from == CharacterDatabaseSchema.Version)
            return new(from, from, null);
        if (from > CharacterDatabaseSchema.Version)
            throw new InvalidOperationException(
                $"The database is at schema version {from}, newer than the {CharacterDatabaseSchema.Version} this tool knows.");
        if (from == 0 && Shape.IsEmpty(connection))
            throw new InvalidOperationException("The database has no tables; the server builds a new one itself.");

        var copy = backup ? Backup(connection, databasePath, from) : null;
        for (var version = from; version < CharacterDatabaseSchema.Version; version++)
        {
            if (!Steps.TryGetValue(version, out var step))
                throw new InvalidOperationException($"No step takes a database from schema version {version}.");
            step(connection);
            CharacterDatabaseSchema.Stamp(connection, null, checked((int)version + 1));
        }

        return new(from, CharacterDatabaseSchema.Version, copy);
    }

    /// <summary>A consistent copy beside the database, taken before anything changes.</summary>
    private static string Backup(SqliteConnection connection, string databasePath, long version)
    {
        var copy = $"{Path.GetFullPath(databasePath)}.v{version}-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO @copy;";
        command.Parameters.AddWithValue("@copy", copy);
        command.ExecuteNonQuery();
        return copy;
    }
}
