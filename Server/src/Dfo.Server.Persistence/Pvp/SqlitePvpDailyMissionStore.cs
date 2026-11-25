using System.Globalization;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Pvp;

internal sealed class SqlitePvpDailyMissionStore(SqliteDatabase database) : IPvpDailyMissionStore
{
    public async ValueTask<int> ProgressAsync(CharacterId character, DateOnly day, ushort mission, CancellationToken cancellationToken) =>
        (await database.ReadFirstAsync(
            """
            SELECT progress FROM pvp_daily_missions WHERE character_id=@character AND game_day=@day AND mission_id=@mission
            """,
            parameters => Bind(parameters, character, day, mission),
            reader => new MissionProgress(reader.GetInt32(0)), cancellationToken).ConfigureAwait(false))?.Count ?? 0;

    public async ValueTask<IReadOnlyDictionary<ushort, int>> ReadDayAsync(CharacterId character, DateOnly day,
        CancellationToken cancellationToken)
    {
        var rows = await database.ReadAllAsync(
            "SELECT mission_id, progress FROM pvp_daily_missions WHERE character_id=@character AND game_day=@day",
            parameters => BindDay(parameters, character, day),
            reader => (Id: checked((ushort)reader.GetInt32(0)), Progress: reader.GetInt32(1)),
            cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, row => row.Progress);
    }

    public async ValueTask<IReadOnlyDictionary<int, int>> ReadMetricsAsync(CharacterId character, DateOnly day, ushort mission,
        CancellationToken cancellationToken)
    {
        var rows = await database.ReadAllAsync(
            """
            SELECT metric, value FROM pvp_daily_mission_metrics
            WHERE character_id=@character AND game_day=@day AND mission_id=@mission
            """,
            parameters => Bind(parameters, character, day, mission),
            reader => (Metric: reader.GetInt32(0), Value: reader.GetInt32(1)), cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Metric, row => row.Value);
    }

    public async ValueTask<int> RecordMetricAsync(CharacterId character, DateOnly day, ushort mission, int metric, bool increment,
        CancellationToken cancellationToken) =>
        (await database.WriteReturningAsync(
            """
            INSERT INTO pvp_daily_mission_metrics(character_id,game_day,mission_id,metric,value)
            VALUES(@character,@day,@mission,@metric,@increment)
            ON CONFLICT(character_id,game_day,mission_id,metric) DO UPDATE
                SET value=CASE WHEN @increment=1 THEN value+1 ELSE 0 END
            RETURNING value
            """,
            parameters =>
            {
                Bind(parameters, character, day, mission);
                parameters.AddWithValue("@metric", metric);
                parameters.AddWithValue("@increment", increment ? 1 : 0);
            },
            reader => new MissionProgress(reader.GetInt32(0)), cancellationToken).ConfigureAwait(false))!.Count;

    public async ValueTask<bool> AdvanceAsync(CharacterId character, DateOnly day, ushort mission, int goal, PvpMatchId match,
        CancellationToken cancellationToken, int amount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(goal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        var progress = await database.WriteReturningAsync(
            """
            INSERT INTO pvp_daily_missions(character_id,game_day,mission_id,progress,reward_match_id)
            VALUES(@character,@day,@mission,MIN(@amount,@goal),CASE WHEN @amount>=@goal THEN @match ELSE NULL END)
            ON CONFLICT(character_id,game_day,mission_id) DO UPDATE SET
                progress=MIN(progress+@amount,@goal),
                reward_match_id=CASE WHEN progress+@amount>=@goal THEN @match ELSE NULL END
            WHERE progress<@goal AND reward_match_id IS NULL
            RETURNING progress
            """,
            parameters =>
            {
                Bind(parameters, character, day, mission);
                parameters.AddWithValue("@goal", goal);
                parameters.AddWithValue("@amount", amount);
                parameters.AddWithValue("@match", match.Value.ToString("N"));
            },
            reader => new MissionProgress(reader.GetInt32(0)), cancellationToken).ConfigureAwait(false);
        return progress?.Count == goal;
    }

    private sealed record MissionProgress(int Count);

    private static void Bind(SqliteParameterCollection parameters, CharacterId character, DateOnly day, ushort mission)
    {
        BindDay(parameters, character, day);
        parameters.AddWithValue("@mission", (int)mission);
    }

    private static void BindDay(SqliteParameterCollection parameters, CharacterId character, DateOnly day)
    {
        parameters.AddWithValue("@character", character.Value);
        parameters.AddWithValue("@day", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
