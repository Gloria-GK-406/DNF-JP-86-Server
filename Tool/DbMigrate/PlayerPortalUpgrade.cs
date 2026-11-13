using Microsoft.Data.Sqlite;

namespace DbMigrate;

/// <summary>
/// Adds the player portal's settings, sessions and operations, and says who did each audited operation (schema 9 to 10).
/// </summary>
/// <remarks>
/// The statements are copied from the version-10 declaration rather than read from it: this step must keep producing
/// the version-10 shape after the declaration moves on.
///
/// No settings row is written. Settings that were never saved read as the portal closed, both features open and no
/// limits, which is exactly what a new database has, so a migrated server starts with the portal closed.
///
/// The two audit columns are added only when missing. A database from before schema versions passes through the
/// legacy step first, which creates absent tables from the current declaration; one that never had <c>gm_audit</c>
/// already has both columns when it gets here. Existing audit rows take the column default and read as the GM's.
/// </remarks>
internal static class PlayerPortalUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS player_portal_settings(id INTEGER PRIMARY KEY CHECK(id=1),enabled INTEGER NOT NULL CHECK(enabled IN(0,1)),cera_enabled INTEGER NOT NULL CHECK(cera_enabled IN(0,1)),mail_enabled INTEGER NOT NULL CHECK(mail_enabled IN(0,1)),cera_per_grant INTEGER NOT NULL CHECK(cera_per_grant>=0),cera_per_day INTEGER NOT NULL CHECK(cera_per_day>=0),token_cera_per_grant INTEGER NOT NULL CHECK(token_cera_per_grant>=0),token_cera_per_day INTEGER NOT NULL CHECK(token_cera_per_day>=0),mail_gold_per_mail INTEGER NOT NULL CHECK(mail_gold_per_mail>=0),mails_per_day INTEGER NOT NULL CHECK(mails_per_day>=0),revision INTEGER NOT NULL CHECK(revision>=1));
                CREATE TABLE IF NOT EXISTS player_portal_sessions(token_hash TEXT PRIMARY KEY,account_id INTEGER NOT NULL,created_at INTEGER NOT NULL,expires_at INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS player_portal_operations(account_id INTEGER NOT NULL,request_id TEXT NOT NULL,kind TEXT NOT NULL CHECK(kind IN('cera','mail')),currency TEXT,amount INTEGER NOT NULL CHECK(amount>=0),payload_hash TEXT NOT NULL,receipt_json TEXT NOT NULL,period_day TEXT NOT NULL,committed_at INTEGER NOT NULL,PRIMARY KEY(account_id,request_id));
                CREATE INDEX IF NOT EXISTS ix_player_portal_sessions_account ON player_portal_sessions(account_id);
                CREATE INDEX IF NOT EXISTS ix_player_portal_sessions_expiry ON player_portal_sessions(expires_at);
                CREATE INDEX IF NOT EXISTS ix_player_portal_operations_usage ON player_portal_operations(account_id,kind,period_day);
                CREATE INDEX IF NOT EXISTS ix_player_portal_operations_time ON player_portal_operations(account_id,committed_at DESC,request_id DESC);
                """;
            command.ExecuteNonQuery();
        }

        AddColumnIfMissing(connection, transaction, "actor_kind", "ALTER TABLE gm_audit ADD COLUMN actor_kind TEXT NOT NULL DEFAULT 'gm';");
        AddColumnIfMissing(connection, transaction, "account_id", "ALTER TABLE gm_audit ADD COLUMN account_id INTEGER;");
        transaction.Commit();
    }

    private static void AddColumnIfMissing(SqliteConnection connection, SqliteTransaction transaction, string column, string statement)
    {
        if (Shape.HasColumn(connection, transaction, "gm_audit", column)) return;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = statement;
        command.ExecuteNonQuery();
    }
}
