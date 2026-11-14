using System.Globalization;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Pvp;

/// <summary>
/// <c>character_pvp_record</c>, <c>pvp_matches</c> and <c>pvp_match_results</c>.
/// </summary>
/// <remarks>
/// The match row is written first and only when absent, so whether this call settles the battle is
/// answered by the database inside the caller's transaction rather than by a read before it: a
/// second settlement of the same battle, from a retry or after a restart, finds the row and reads
/// the results back instead of applying them again.
/// </remarks>
internal sealed class SqlitePvpRecordDomainStore(SqliteDatabase database) : IPvpRecordDomainStore
{
    private const string ResultColumns =
        "character_id,seat,team,outcome,kills,deaths,fled,counts_toward_daily,previous_grade,grade,"
        + "experience_change,experience,win_streak";

    public async ValueTask<PvpRecord> ReadAsync(CharacterId character, CancellationToken cancellationToken) =>
        await database.ReadFirstAsync(
            "SELECT grade,rating_grade,grade_experience,win,lose,draw,win_streak,peak_win_streak "
            + "FROM character_pvp_record WHERE character_id=@cid",
            parameters => parameters.AddWithValue("@cid", character.Value),
            reader => new PvpRecord(
                Narrow(reader.GetInt64(0), "grade"), Narrow(reader.GetInt64(1), "rating grade"),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7)),
            cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidDataException($"Character {character.Value} has no duel record.");

    public async ValueTask<PvpDailyCounts> DailyCountsAsync(
        CharacterId character, DateOnly gameDay, CancellationToken cancellationToken) =>
        (await database.ReadFirstAsync(
            """
            SELECT COALESCE(SUM(r.outcome=0),0), COALESCE(SUM(r.outcome=1),0)
            FROM pvp_match_results r JOIN pvp_matches m ON m.match_id=r.match_id
            WHERE r.character_id=@cid AND r.counts_toward_daily=1 AND m.game_day=@day
            """,
            parameters =>
            {
                parameters.AddWithValue("@cid", character.Value);
                parameters.AddWithValue("@day", Day(gameDay));
            },
            reader => new PvpDailyCounts(reader.GetInt32(0), reader.GetInt32(1)),
            cancellationToken).ConfigureAwait(false))!;

    public async ValueTask<PvpSettleOutcome> SettleAsync(
        PvpMatchSettlementRecord settlement, CancellationToken cancellationToken)
    {
        Validate(settlement);
        var match = settlement.MatchId.ToString("N");
        var inserted = await database.ExecuteAsync(
            """
            INSERT INTO pvp_matches(match_id,room_id,listener_port,channel_env,battle_mode,outcome_kind,winner,
                records_result,game_day,completed_at)
            VALUES(@match,@room,@port,@env,@mode,@kind,@winner,@records,@day,@at)
            ON CONFLICT(match_id) DO NOTHING
            """,
            parameters =>
            {
                parameters.AddWithValue("@match", match);
                parameters.AddWithValue("@room", (int)settlement.RoomId);
                parameters.AddWithValue("@port", settlement.ListenerPort);
                parameters.AddWithValue("@env", settlement.ChannelEnvironment is { } env ? (object)(int)env : DBNull.Value);
                parameters.AddWithValue("@mode", (int)settlement.Mode);
                parameters.AddWithValue("@kind", (int)settlement.OutcomeKind);
                parameters.AddWithValue("@winner", settlement.Winner is { } winner ? (object)(int)winner : DBNull.Value);
                parameters.AddWithValue("@records", settlement.RecordsResult ? 1 : 0);
                parameters.AddWithValue("@day", Day(settlement.GameDay));
                parameters.AddWithValue("@at", settlement.CompletedAt.ToUnixTimeMilliseconds());
            },
            cancellationToken).ConfigureAwait(false);
        if (inserted == 0)
            return new PvpSettleOutcome.AlreadySettled(await ResultsAsync(match, cancellationToken).ConfigureAwait(false));

        var results = new List<PvpFighterResult>(settlement.Fighters.Count);
        foreach (var fighter in Ordered(settlement.Fighters))
        {
            var before = await this.ReadAsync(fighter.Character, cancellationToken).ConfigureAwait(false);
            var streak = !settlement.RecordsResult ? before.WinStreak
                : fighter.Outcome == PvpOutcome.Win ? checked(before.WinStreak + 1) : 0;
            if (settlement.RecordsResult)
                await RecordAsync(fighter, streak, cancellationToken).ConfigureAwait(false);
            var result = new PvpFighterResult(
                fighter.Character, fighter.Seat, fighter.Team, fighter.Outcome, fighter.Kills, fighter.Deaths,
                fighter.Fled, fighter.Decision.CountsTowardDaily, before.Grade, fighter.Decision.Grade,
                fighter.Decision.ExperienceChange, fighter.Decision.Experience, streak);
            await InsertResultAsync(match, result, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return new PvpSettleOutcome.Created(results);
    }

    private async ValueTask RecordAsync(PvpFighterSettlement fighter, int streak, CancellationToken cancellationToken)
    {
        var touched = await database.ExecuteAsync(
            """
            UPDATE character_pvp_record SET grade=@grade,grade_experience=@experience,
                win=win+@win,lose=lose+@lose,draw=draw+@draw,
                win_streak=@streak,peak_win_streak=MAX(peak_win_streak,@streak)
            WHERE character_id=@cid
            """,
            parameters =>
            {
                parameters.AddWithValue("@grade", (int)fighter.Decision.Grade);
                parameters.AddWithValue("@experience", fighter.Decision.Experience);
                parameters.AddWithValue("@win", fighter.Outcome == PvpOutcome.Win ? 1 : 0);
                parameters.AddWithValue("@lose", fighter.Outcome == PvpOutcome.Loss ? 1 : 0);
                parameters.AddWithValue("@draw", fighter.Outcome == PvpOutcome.Draw ? 1 : 0);
                parameters.AddWithValue("@streak", streak);
                parameters.AddWithValue("@cid", fighter.Character.Value);
            },
            cancellationToken).ConfigureAwait(false);
        if (touched != 1)
            throw new InvalidDataException($"Character {fighter.Character.Value} has no duel record to settle.");
    }

    private async ValueTask InsertResultAsync(string match, PvpFighterResult result, CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            $"INSERT INTO pvp_match_results(match_id,{ResultColumns}) "
            + "VALUES(@match,@cid,@seat,@team,@outcome,@kills,@deaths,@fled,@counts,@previous,@grade,@change,@experience,@streak)",
            parameters =>
            {
                parameters.AddWithValue("@match", match);
                parameters.AddWithValue("@cid", result.Character.Value);
                parameters.AddWithValue("@seat", (int)result.Seat);
                parameters.AddWithValue("@team", (int)result.Team);
                parameters.AddWithValue("@outcome", (int)result.Outcome);
                parameters.AddWithValue("@kills", result.Kills);
                parameters.AddWithValue("@deaths", result.Deaths);
                parameters.AddWithValue("@fled", result.Fled ? 1 : 0);
                parameters.AddWithValue("@counts", result.CountsTowardDaily ? 1 : 0);
                parameters.AddWithValue("@previous", (int)result.PreviousGrade);
                parameters.AddWithValue("@grade", (int)result.Grade);
                parameters.AddWithValue("@change", result.ExperienceChange);
                parameters.AddWithValue("@experience", result.Experience);
                parameters.AddWithValue("@streak", result.WinStreak);
            },
            cancellationToken).ConfigureAwait(false);

    private ValueTask<IReadOnlyList<PvpFighterResult>> ResultsAsync(string match, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            $"SELECT {ResultColumns} FROM pvp_match_results WHERE match_id=@match ORDER BY seat,character_id",
            parameters => parameters.AddWithValue("@match", match),
            Result,
            cancellationToken);

    private static PvpFighterResult Result(SqliteDataReader reader) => new(
        new CharacterId(reader.GetInt32(0)), Narrow(reader.GetInt64(1), "seat"), Narrow(reader.GetInt64(2), "team"),
        (PvpOutcome)reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6) != 0,
        reader.GetInt32(7) != 0, Narrow(reader.GetInt64(8), "previous grade"), Narrow(reader.GetInt64(9), "grade"),
        reader.GetInt64(10), reader.GetInt64(11), reader.GetInt32(12));

    /// <summary>The order results are stored and read back in, so a first settlement and a repeat answer alike.</summary>
    private static IEnumerable<PvpFighterSettlement> Ordered(IReadOnlyList<PvpFighterSettlement> fighters) =>
        fighters.OrderBy(fighter => fighter.Seat).ThenBy(fighter => fighter.Character.Value);

    private static void Validate(PvpMatchSettlementRecord settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(settlement.Fighters);
        if (settlement.MatchId == Guid.Empty)
            throw new ArgumentException("A battle needs an identity to be settled once.", nameof(settlement));
        if (settlement.OutcomeKind == PvpMatchOutcomeKind.Cancelled && settlement.RecordsResult)
            throw new ArgumentException("A cancelled battle changes no record.", nameof(settlement));
        if (settlement.Fighters.Select(fighter => fighter.Character).Distinct().Count() != settlement.Fighters.Count
            || settlement.Fighters.Select(fighter => fighter.Seat).Distinct().Count() != settlement.Fighters.Count)
            throw new ArgumentException("Each fighter settles once, from a seat of their own.", nameof(settlement));
        foreach (var fighter in settlement.Fighters)
        {
            ArgumentNullException.ThrowIfNull(fighter.Decision);
            if (fighter.Kills < 0 || fighter.Deaths < 0 || fighter.Decision.Experience < 0
                || (fighter.Fled && fighter.Outcome != PvpOutcome.Loss)
                || (fighter.Decision.CountsTowardDaily && !settlement.RecordsResult)
                || !Enum.IsDefined(fighter.Outcome))
                throw new ArgumentException(
                    $"Character {fighter.Character.Value}'s settlement is not one a battle can produce.", nameof(settlement));
        }
    }

    private static byte Narrow(long value, string what) =>
        value is >= byte.MinValue and <= byte.MaxValue
            ? (byte)value
            : throw new InvalidDataException($"A stored duel {what} of {value} is out of range.");

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
