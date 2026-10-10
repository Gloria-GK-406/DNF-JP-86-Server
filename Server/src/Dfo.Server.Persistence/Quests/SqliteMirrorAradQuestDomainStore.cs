using System.Text.Json;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Quests;

internal sealed class SqliteMirrorAradQuestDomainStore(SqliteDatabase database) : IMirrorAradQuestDomainStore
{
    public ValueTask<MirrorQuestState?> ReadAsync(CharacterId character, DungeonRunId run, CancellationToken ct) =>
        database.ReadAsync<MirrorQuestState?>(async (command, token) =>
        {
            command.CommandText = "SELECT state_json FROM character_mirror_quests WHERE character_id=@character AND run_id=@run";
            command.Parameters.AddWithValue("@character", character.Value);
            command.Parameters.AddWithValue("@run", run.Value.ToString("N"));
            var json = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
            return json is null ? null : JsonSerializer.Deserialize<MirrorQuestState>(json)
                ?? throw new InvalidDataException("Invalid mirror quest state.");
        }, ct);

    public ValueTask WriteAsync(AccountId account, MirrorQuestState state, CancellationToken ct) =>
        database.WriteAsync(async (command, token) =>
        {
            await RequireOwnerAsync(command, account, state.CharacterId, token).ConfigureAwait(false);
            command.CommandText = "INSERT INTO character_mirror_quests(character_id,run_id,state_json) VALUES(@character,@run,@json) "
                + "ON CONFLICT(character_id,run_id) DO UPDATE SET state_json=excluded.state_json";
            command.Parameters.AddWithValue("@run", state.RunId.Value.ToString("N"));
            command.Parameters.AddWithValue("@json", JsonSerializer.Serialize(state));
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);

    public async ValueTask<bool> DeleteAsync(CharacterId character, DungeonRunId run, CancellationToken ct)
    {
        var removed = false;
        await database.WriteAsync(async (command, token) =>
        {
            command.CommandText = "DELETE FROM character_mirror_quests WHERE character_id=@character AND run_id=@run";
            command.Parameters.AddWithValue("@character", character.Value);
            command.Parameters.AddWithValue("@run", run.Value.ToString("N"));
            removed = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 0;
        }, ct).ConfigureAwait(false);
        return removed;
    }

    public ValueTask<MirrorDailyQuota> ReadQuotaAsync(CharacterId character, DateOnly day, CancellationToken ct) =>
        database.ReadAsync(async (command, token) =>
        {
            command.CommandText = "SELECT entries,clears,daily_reward_claimed FROM character_mirror_daily WHERE character_id=@character AND day=@day";
            command.Parameters.AddWithValue("@character", character.Value);
            command.Parameters.AddWithValue("@day", day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false)
                ? new MirrorDailyQuota(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2) != 0) : new();
        }, ct);

    public ValueTask WriteQuotaAsync(AccountId account, CharacterId character, DateOnly day, MirrorDailyQuota quota, CancellationToken ct) =>
        database.WriteAsync(async (command, token) =>
        {
            await RequireOwnerAsync(command, account, character, token).ConfigureAwait(false);
            command.CommandText = "INSERT INTO character_mirror_daily(character_id,day,entries,clears,daily_reward_claimed) "
                + "VALUES(@character,@day,@entries,@clears,@claimed) ON CONFLICT(character_id,day) DO UPDATE SET "
                + "entries=excluded.entries,clears=excluded.clears,daily_reward_claimed=excluded.daily_reward_claimed";
            command.Parameters.AddWithValue("@day", day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@entries", quota.Entries);
            command.Parameters.AddWithValue("@clears", quota.Clears);
            command.Parameters.AddWithValue("@claimed", quota.DailyRewardClaimed ? 1 : 0);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);

    private static async ValueTask RequireOwnerAsync(SqliteCommand command, AccountId account, CharacterId character, CancellationToken ct)
    {
        command.CommandText = "SELECT 1 FROM characters WHERE account_id=@account AND character_id=@character AND delete_flag=0";
        command.Parameters.AddWithValue("@account", account.Value);
        command.Parameters.AddWithValue("@character", character.Value);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new InvalidDataException("Mirror quest owner does not match.");
    }
}
