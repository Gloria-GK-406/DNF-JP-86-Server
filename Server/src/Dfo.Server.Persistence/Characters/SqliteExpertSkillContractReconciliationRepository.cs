using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Characters;

internal sealed class SqliteExpertSkillContractReconciliationRepository(
    string databasePath, IExpertSkillContractRuleService rules)
    : IExpertSkillContractReconciliationRepository
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
        { DataSource = Path.GetFullPath(databasePath) }.ToString();

    /// <remarks>
    /// This reads, asks, and writes. What comes down and to what is decided by the rule service,
    /// which never sees a connection; what remains here is the reading, the writing, and the one
    /// transaction that makes them a single outcome.
    /// </remarks>
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
        var premiums = new List<(int PremiumType, long EndTimeUnixSeconds)>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT premium_type,end_time FROM account_premiums WHERE account_id=@aid";
            command.Parameters.AddWithValue("@aid", accountId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                premiums.Add((reader.GetInt32(0), reader.GetInt64(1)));
        }
        if (!rules.HasRunOut(premiums, overSkillPremiumTypes, DateTimeOffset.UtcNow))
        { await transaction.CommitAsync(cancellationToken); return false; }

        await using (var owner = connection.CreateCommand())
        {
            owner.Transaction = transaction;
            owner.CommandText = "SELECT 1 FROM characters WHERE character_id=@cid AND account_id=@aid AND delete_flag=0";
            owner.Parameters.AddWithValue("@cid", characterId.Value); owner.Parameters.AddWithValue("@aid", accountId.Value);
            if (await owner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
                throw new InvalidDataException("Expert-contract skill owner is unavailable.");
        }
        var skills = new List<(int Page, int Slot, int SkillId, int Level)>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT page_index,slot,skill_id,level FROM character_skills WHERE character_id=@cid";
            command.Parameters.AddWithValue("@cid", characterId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                skills.Add((reader.GetInt32(0), reader.GetInt32(1),
                    reader.GetInt32(2), reader.GetInt32(3)));
        }
        var changes = rules.CapsFor(skills, definitions, freeBaseline);
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
