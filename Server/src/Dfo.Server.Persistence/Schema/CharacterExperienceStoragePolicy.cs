using System.Globalization;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Schema;

internal sealed class CharacterExperienceStoragePolicy(SqliteDatabase database) : ICharacterExperienceStoragePolicy
{
    public async ValueTask InstallAsync(CharacterExperienceScale scale, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scale);
        // Schema setup owns this transaction, like CharacterDatabaseSchema.Ensure. No game use case is running.
        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var maximum = scale.MaximumExperience.ToString(CultureInfo.InvariantCulture);
        var level = "CASE " + string.Join(" ", scale.Thresholds.Select((value, index) =>
            $"WHEN exp < {value.ToString(CultureInfo.InvariantCulture)} THEN {index + 1}")) + $" ELSE {scale.TopLevel} END";
        var newLevel = level.Replace("exp <", "NEW.exp <", StringComparison.Ordinal);
        command.CommandText = $"""
            DROP TRIGGER IF EXISTS character_exp_insert_guard;
            DROP TRIGGER IF EXISTS character_exp_update_guard;
            DROP TRIGGER IF EXISTS character_level_insert_projection;
            DROP TRIGGER IF EXISTS character_level_update_projection;
            UPDATE characters SET exp=CAST(MIN({maximum},MAX(0,exp)) AS INTEGER)
            WHERE exp<0 OR exp>{maximum} OR typeof(exp)<>'integer';
            UPDATE characters SET level=({level}) WHERE level<>({level});
            CREATE TRIGGER character_exp_insert_guard BEFORE INSERT ON characters
            WHEN typeof(NEW.exp)<>'integer' OR NEW.exp<0 OR NEW.exp>{maximum}
            BEGIN SELECT RAISE(ABORT,'Character experience outside playable range'); END;
            CREATE TRIGGER character_exp_update_guard BEFORE UPDATE OF exp ON characters
            WHEN typeof(NEW.exp)<>'integer' OR NEW.exp<0 OR NEW.exp>{maximum}
            BEGIN SELECT RAISE(ABORT,'Character experience outside playable range'); END;
            CREATE TRIGGER character_level_insert_projection AFTER INSERT ON characters
            WHEN NEW.level<>({newLevel})
            BEGIN UPDATE characters SET level=({level}) WHERE character_id=NEW.character_id; END;
            CREATE TRIGGER character_level_update_projection AFTER UPDATE OF exp,level ON characters
            WHEN NEW.level<>({newLevel})
            BEGIN UPDATE characters SET level=({level}) WHERE character_id=NEW.character_id; END;
            """;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        transaction.Commit();
    }
}
