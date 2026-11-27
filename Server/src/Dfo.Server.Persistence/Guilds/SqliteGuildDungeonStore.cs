using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Guilds;

internal sealed class SqliteGuildDungeonStore(SqliteDatabase database, IGuildPointAccount points) : IGuildDungeonStore
{
    public async ValueTask<GuildDungeonPeriod> ResolvePeriodAsync(GuildId guild, GuildDungeonPeriod calendar, CancellationToken ct)
    {
        var extension = await database.ReadAsync<GuildDungeonPeriod?>(async (command, token) =>
        {
            command.CommandText = """
                SELECT period_start,reset_at FROM guild_dungeon_extensions
                WHERE guild_id=@guild AND period_start<=@month AND reset_at>@month
                ORDER BY period_start DESC LIMIT 1
                """;
            command.Parameters.AddWithValue("@guild", (long)guild.Value);
            command.Parameters.AddWithValue("@month", calendar.StartUnix);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false)
                ? new GuildDungeonPeriod(reader.GetInt64(0), reader.GetInt64(1)) : null;
        }, ct).ConfigureAwait(false);
        return extension ?? calendar;
    }

    public async ValueTask AddExtensionAsync(GuildId guild, GuildDungeonPeriod period, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO guild_dungeon_extensions(guild_id,period_start,reset_at)
            VALUES(@guild,@start,@reset)
            """, parameters =>
            {
                parameters.AddWithValue("@guild", (long)guild.Value);
                parameters.AddWithValue("@start", period.StartUnix);
                parameters.AddWithValue("@reset", period.EndUnix);
            }, ct).ConfigureAwait(false);

    public ValueTask<int> ReadRepeatBonusAsync(CharacterId character, CancellationToken ct) =>
        database.ReadAsync(async (command, token) =>
        {
            command.CommandText = "SELECT progress FROM guild_dungeon_repeat_bonus WHERE character_id=@character";
            command.Parameters.AddWithValue("@character", character.Value);
            return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
        }, ct);

    public async ValueTask WriteRepeatBonusAsync(CharacterId character, int progress, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO guild_dungeon_repeat_bonus(character_id,progress) VALUES(@character,@progress)
            ON CONFLICT(character_id) DO UPDATE SET progress=excluded.progress
            """, parameters =>
            {
                parameters.AddWithValue("@character", character.Value);
                parameters.AddWithValue("@progress", progress);
            }, ct).ConfigureAwait(false);

    public async ValueTask<IReadOnlyDictionary<int, int>> ReadProgressAsync(GuildDungeonAdmission admission, CancellationToken ct)
    {
        var rows = await database.ReadAllAsync("""
            SELECT dungeon_id,clear_count FROM guild_dungeon_progress
            WHERE guild_id=@guild AND period_start=@period AND style_id=@style
            """, parameters => Bind(parameters, admission),
            reader => new GuildDungeonProgress(reader.GetInt32(0), reader.GetInt32(1)), ct).ConfigureAwait(false);
        return rows.ToDictionary(row => row.DungeonId, row => row.ClearCount);
    }

    public async ValueTask<bool> TryRecordClearAsync(Guid run, CharacterId character, GuildDungeonAdmission admission,
        int dungeon, int contribution, CancellationToken ct)
    {
        var inserted = await database.ExecuteAsync("""
            INSERT INTO guild_dungeon_clears(run_id,character_id,guild_id,period_start,style_id,dungeon_id,contribution)
            SELECT @run,@character,@guild,@period,@style,@dungeon,@points
            FROM guild_members WHERE guild_id=@guild AND character_id=@character
            ON CONFLICT(run_id,character_id) DO NOTHING
            """, parameters =>
            {
                Bind(parameters, admission);
                parameters.AddWithValue("@run", run.ToString("N"));
                parameters.AddWithValue("@character", character.Value);
                parameters.AddWithValue("@dungeon", dungeon);
                parameters.AddWithValue("@points", contribution);
            }, ct).ConfigureAwait(false);
        if (inserted == 0) return false;

        if (await database.ExecuteAsync("""
            INSERT INTO guild_dungeon_progress(guild_id,period_start,style_id,dungeon_id,clear_count)
            VALUES(@guild,@period,@style,@dungeon,1)
            ON CONFLICT(guild_id,period_start,style_id,dungeon_id)
            DO UPDATE SET clear_count=clear_count+1
            """, parameters =>
            {
                Bind(parameters, admission);
                parameters.AddWithValue("@dungeon", dungeon);
            }, ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Guild dungeon progress was not advanced.");

        if (contribution > 0 && !await points.TryCreditAsync(admission.Guild, contribution, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Guild dungeon contribution could not be credited.");

        return true;
    }

    private static void Bind(SqliteParameterCollection parameters, GuildDungeonAdmission admission)
    {
        parameters.AddWithValue("@guild", (long)admission.Guild.Value);
        parameters.AddWithValue("@period", admission.Period.StartUnix);
        parameters.AddWithValue("@style", admission.StyleId);
    }
}
