using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Raids;

internal sealed class SqliteAntonRaidRewardLedger(SqliteDatabase database) : IAntonRaidRewardLedger
{
    public ValueTask<bool> TryClaimAsync(AntonRaidRewardKey key, CancellationToken cancellationToken) =>
        InsertAsync(
            """
            INSERT INTO anton_raid_reward_ledger(instance, phase, character_id, kind)
            VALUES ($instance, $phase, $character, $kind)
            ON CONFLICT (instance, phase, character_id, kind) DO NOTHING
            """,
            p =>
            {
                p.AddWithValue("$instance", key.Instance.ToString("N"));
                p.AddWithValue("$phase", key.Phase);
                p.AddWithValue("$character", key.Character.Value);
                p.AddWithValue("$kind", (byte)key.Kind);
            }, cancellationToken);

    public ValueTask<bool> TryCountQuestClearAsync(CharacterId character, int quest, byte phase,
        DateOnly period, CancellationToken cancellationToken) =>
        InsertAsync(
            """
            INSERT INTO anton_raid_quest_periods(character_id, quest_id, phase, period)
            VALUES ($character, $quest, $phase, $period)
            ON CONFLICT (character_id, quest_id, phase, period) DO NOTHING
            """,
            p =>
            {
                p.AddWithValue("$character", character.Value);
                p.AddWithValue("$quest", quest);
                p.AddWithValue("$phase", phase);
                p.AddWithValue("$period", period.DayNumber);
            }, cancellationToken);

    private async ValueTask<bool> InsertAsync(string sql, Action<SqliteParameterCollection> bind,
        CancellationToken cancellationToken)
    {
        var inserted = false;
        await database.WriteAsync(async (command, ct) =>
        {
            command.CommandText = sql;
            bind(command.Parameters);
            inserted = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
        }, cancellationToken).ConfigureAwait(false);
        return inserted;
    }
}
