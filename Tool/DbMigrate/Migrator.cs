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
            [13] = PvpRecordSplitUpgrade.Apply,
            [14] = PvpSkillBuildUpgrade.Apply,
            [15] = PvpTotalMatchTeamsUpgrade.Apply,
            [16] = PvpMatchRatingResultsUpgrade.Apply,
            [17] = PvpTotalMatchRatingResultsUpgrade.Apply,
            [18] = PvpDailyMissionsUpgrade.Apply,
            [19] = PvpDailyMissionMetricsUpgrade.Apply,
            [20] = PvpColosseumUpgrade.Apply,
            [21] = AddGuilds,
            [22] = AddRecommendedGuildChannel,
            [23] = AddGuildDonationPoints,
            [24] = AddGuildDonationHistory,
            [25] = AddGuildAttendance,
            [26] = AddGuildRankSettings,
            [27] = AddGuildMemberRank,
            [28] = AddGuildAppearance,
            [29] = AddGuildContents,
            [30] = AddTimedGuildContentsAndWarehouse,
            [31] = AddGuildWarehouseItems,
            [32] = AddGuildDungeonProgress,
            [33] = AddGuildWarehouseHistory,
            [34] = AddGuildBoard,
            [35] = AddGuardianGemKeys,
            [36] = AddGuildMemberMemo,
            [37] = AddGuildAlliances,
            [38] = AddGuildGifts,
            [39] = AddGuildRedPackets,
            [40] = AddGuildDailyContributions,
            [41] = AddGuildClearContributions,
            [42] = AddGuildDungeonRepeatBonus,
            [43] = AddGuildMonthlyContributions,
            [44] = AddGuildHallStage,
            [45] = AddGuildDungeonExtensions,
            [46] = AddGuildMedalAura,
            [47] = GuildMembershipUpgrade.Apply,
            [48] = connection => CharacterDatabaseSchema.DeclareConsumableCooldowns(connection),
        };

    private static void AddGuildMedalAura(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (!Shape.HasColumn(connection, transaction, "guilds", "medal_aura_symbol"))
        {
            command.CommandText = """
                ALTER TABLE guilds ADD COLUMN medal_aura_symbol INTEGER NOT NULL DEFAULT -1
                    CHECK(medal_aura_symbol BETWEEN -1 AND 2147483647)
                """;
            command.ExecuteNonQuery();
        }
        if (!Shape.HasColumn(connection, transaction, "guilds", "medal_aura_frame"))
        {
            command.CommandText = """
                ALTER TABLE guilds ADD COLUMN medal_aura_frame INTEGER NOT NULL DEFAULT -1
                    CHECK(medal_aura_frame BETWEEN -1 AND 2147483647
                        AND (medal_aura_frame = -1) = (medal_aura_symbol = -1))
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void AddGuildDungeonExtensions(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildDungeonExtensions(connection, transaction);
        transaction.Commit();
    }

    private static void AddGuildHallStage(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        if (!Shape.HasColumn(connection, transaction, "guilds", "hall_stage"))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "ALTER TABLE guilds ADD COLUMN hall_stage INTEGER NOT NULL DEFAULT 1 CHECK(hall_stage BETWEEN 1 AND 5)";
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void AddGuildMonthlyContributions(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildMonthlyContributions(connection, transaction);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE VIEW IF NOT EXISTS guild_contribution_entries AS
            SELECT guild_id,donor_character_id AS character_id,donor_name AS character_name,points,
                donated_at AS earned_at,3 AS source,donation_id AS source_id FROM guild_gold_donations
            UNION ALL
            SELECT guild_id,character_id,character_name,points,earned_at,source,event_id
            FROM guild_monthly_contribution_events
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void AddGuildDungeonRepeatBonus(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildDungeonRepeatBonus(connection, transaction);
        transaction.Commit();
    }

    private static void AddGuildClearContributions(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildClearContributions(connection, transaction);
        transaction.Commit();
    }

    private static void AddGuildDailyContributions(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildDailyContributions(connection, transaction);
        transaction.Commit();
    }

    private static void AddGuildRedPackets(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildRedPackets(connection, transaction);
        transaction.Commit();
    }

    private static void AddGuildGifts(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildGifts(connection, transaction);
        transaction.Commit();
    }

    private static void AddGuildAlliances(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildAlliances(connection, transaction);
        transaction.Commit();
    }

    private static void AddGuildMemberMemo(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        if (!Shape.HasColumn(connection, transaction, "guild_members", "memo"))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "ALTER TABLE guild_members ADD COLUMN memo TEXT NOT NULL DEFAULT ''";
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void AddGuardianGemKeys(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        if (!Shape.HasColumn(connection, transaction, "items", "guardian_gem_keys"))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "ALTER TABLE items ADD COLUMN guardian_gem_keys INTEGER NOT NULL DEFAULT 0";
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void AddGuildBoard(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildBoard(connection, transaction);
        transaction.Commit();
    }

    private static void AddGuildWarehouseHistory(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildWarehouseHistory(connection, transaction);
        transaction.Commit();
    }

    private static void AddGuildDungeonProgress(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildDungeonProgress(connection, transaction);
        transaction.Commit();
    }

    private static void AddGuildWarehouseItems(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildWarehouseItems(connection, transaction);
        transaction.Commit();
    }

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

    private static void AddGuildMemberRank(SqliteConnection connection)
    {
        if (Shape.HasColumn(connection, null, "guild_members", "member_rank"))
            return;

        using var command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE guild_members ADD COLUMN member_rank INTEGER NOT NULL DEFAULT 4
                CHECK(member_rank BETWEEN 2 AND 5);
            """;
        command.ExecuteNonQuery();
    }

    private static void AddGuildAppearance(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (!Shape.HasColumn(connection, transaction, "guilds", "announcement"))
        {
            command.CommandText = "ALTER TABLE guilds ADD COLUMN announcement TEXT NOT NULL DEFAULT '';";
            command.ExecuteNonQuery();
        }
        if (!Shape.HasColumn(connection, transaction, "guilds", "mark_id"))
        {
            command.CommandText = "ALTER TABLE guilds ADD COLUMN mark_id INTEGER NOT NULL DEFAULT 1 CHECK(mark_id BETWEEN 0 AND 15);";
            command.ExecuteNonQuery();
        }
        if (!Shape.HasColumn(connection, transaction, "guilds", "spent_gm_points"))
        {
            command.CommandText = """
                ALTER TABLE guilds ADD COLUMN spent_gm_points INTEGER NOT NULL DEFAULT 0
                    CHECK(spent_gm_points BETWEEN 0 AND 2147483647);
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void AddGuildContents(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareGuildContents(connection, transaction);
        transaction.Commit();
    }

    private static void AddTimedGuildContentsAndWarehouse(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        if (!Shape.HasColumn(connection, transaction, "guild_contents", "expires_at"))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "ALTER TABLE guild_contents RENAME TO guild_contents_v22;";
            command.ExecuteNonQuery();
            CharacterDatabaseSchema.DeclareGuildContents(connection, transaction);
            command.CommandText = """
                INSERT INTO guild_contents(guild_id,kind,product_id,value,remaining_uses)
                SELECT guild_id,kind,product_id,value,remaining_uses FROM guild_contents_v22;
                DROP TABLE guild_contents_v22;
                """;
            command.ExecuteNonQuery();
        }
        CharacterDatabaseSchema.DeclareGuildWarehouse(connection, transaction);
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
        var version = GuildBranchUpgrade.BeginIfNeeded(connection, from);
        while (version < CharacterDatabaseSchema.Version)
        {
            if (version == 21)
            {
                version = GuildBranchUpgrade.ResumeGuildSteps(connection);
                if (version == CharacterDatabaseSchema.Version)
                    break;
            }
            if (!Steps.TryGetValue(version, out var step))
                throw new InvalidOperationException($"No step takes a database from schema version {version}.");
            step(connection);
            CharacterDatabaseSchema.Stamp(connection, null, checked((int)(version + 1)));
            version++;
        }

        return new(from, CharacterDatabaseSchema.Version, copy);
    }

    /// <summary>A consistent copy beside the database, taken before anything changes.</summary>
    private static string Backup(SqliteConnection connection, string databasePath, long version)
    {
        var copy = $"{Path.GetFullPath(databasePath)}.v{version}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.bak";
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO @copy;";
        command.Parameters.AddWithValue("@copy", copy);
        command.ExecuteNonQuery();
        return copy;
    }
}
