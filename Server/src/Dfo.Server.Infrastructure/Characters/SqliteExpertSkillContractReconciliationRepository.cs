using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Characters;

internal sealed class SqliteExpertSkillContractReconciliationRepository(string databasePath)
    : IExpertSkillContractReconciliationRepository
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
        { DataSource = Path.GetFullPath(databasePath) }.ToString();

    public async ValueTask<bool> ReconcileAsync(AccountId accountId, CharacterId characterId,
        IReadOnlySet<int> overSkillPremiumTypes,
        IReadOnlyDictionary<int, SkillPointCostDefinition> definitions,
        IReadOnlyDictionary<int, byte> freeBaseline, CancellationToken cancellationToken)
    {
        if (overSkillPremiumTypes.Count == 0) return false;
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); var expired = false;
        await using (var premiums = connection.CreateCommand())
        {
            premiums.Transaction = transaction;
            premiums.CommandText = "SELECT premium_type,end_time FROM account_premiums WHERE account_id=@aid";
            premiums.Parameters.AddWithValue("@aid", accountId.Value);
            await using var reader = await premiums.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!overSkillPremiumTypes.Contains(reader.GetInt32(0))) continue;
                if (reader.GetInt64(1) > now) { await transaction.CommitAsync(cancellationToken); return false; }
                expired = true;
            }
        }
        if (!expired) { await transaction.CommitAsync(cancellationToken); return false; }

        await using (var owner = connection.CreateCommand())
        {
            owner.Transaction = transaction;
            owner.CommandText = "SELECT 1 FROM characters WHERE character_id=@cid AND account_id=@aid AND delete_flag=0";
            owner.Parameters.AddWithValue("@cid", characterId.Value); owner.Parameters.AddWithValue("@aid", accountId.Value);
            if (await owner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
                throw new InvalidDataException("Expert-contract skill owner is unavailable.");
        }
        var changes = new List<(byte Page, short Slot, int? Level)>();
        await using (var skills = connection.CreateCommand())
        {
            skills.Transaction = transaction;
            skills.CommandText = "SELECT page_index,slot,skill_id,level FROM character_skills WHERE character_id=@cid";
            skills.Parameters.AddWithValue("@cid", characterId.Value);
            await using var reader = await skills.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt32(2); if (!definitions.TryGetValue(id, out var definition) || definition.IsFixedLevel) continue;
                if (definition.OrdinaryMaximumLevel is not { } maximum) continue;
                if (freeBaseline.TryGetValue(id, out var free) && free > maximum) maximum = free;
                var level = reader.GetInt32(3); if (level <= maximum) continue;
                changes.Add((checked((byte)reader.GetInt32(0)), checked((short)reader.GetInt32(1)),
                    maximum <= 0 ? null : maximum));
            }
        }
        foreach (var change in changes)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = change.Level is null
                ? "DELETE FROM character_skills WHERE character_id=@cid AND page_index=@page AND slot=@slot"
                : "UPDATE character_skills SET level=@level WHERE character_id=@cid AND page_index=@page AND slot=@slot";
            command.Parameters.AddWithValue("@cid", characterId.Value); command.Parameters.AddWithValue("@page", change.Page);
            command.Parameters.AddWithValue("@slot", change.Slot);
            if (change.Level is not null) command.Parameters.AddWithValue("@level", change.Level.Value);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException("Expert-contract skill row changed concurrently.");
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return changes.Count > 0;
    }
}
