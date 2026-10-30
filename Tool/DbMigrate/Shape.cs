using Microsoft.Data.Sqlite;

namespace DbMigrate;

/// <summary>What a database being migrated actually has, asked rather than assumed.</summary>
internal static class Shape
{
    public static bool HasTable(SqliteConnection connection, SqliteTransaction? transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@table";
        command.Parameters.AddWithValue("@table", table);
        return command.ExecuteScalar() is not null;
    }

    public static bool IsEmpty(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        return (long)command.ExecuteScalar()! == 0;
    }

    public static bool HasColumn(SqliteConnection connection, SqliteTransaction? transaction, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM pragma_table_info(@table) WHERE name=@column COLLATE NOCASE";
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return command.ExecuteScalar() is not null;
    }
}
