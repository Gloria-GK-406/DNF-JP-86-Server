using System.Globalization;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Accounts;
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

    public async ValueTask<PvpRatingStanding> ReadRatingAsync(CharacterId character, int seasonId, CancellationToken cancellationToken) =>
        await database.ReadFirstAsync(
            """
            SELECT placement_games,rp_after,peak_rp
            FROM pvp_match_rating_results
            WHERE character_id=@cid AND season_id=@season
            ORDER BY rating_result_id DESC LIMIT 1
            """,
            parameters =>
            {
                parameters.AddWithValue("@cid", character.Value);
                parameters.AddWithValue("@season", seasonId);
            },
            reader => new PvpRatingStanding(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
            cancellationToken).ConfigureAwait(false) ?? PvpRatingStanding.Initial;

    public async ValueTask<PvpRatingStanding> ReadTotalMatchRatingAsync(CharacterId character, int seasonId,
        CancellationToken cancellationToken) =>
        await database.ReadFirstAsync(
            """
            SELECT r.placement_games,r.gp_after,r.peak_gp
            FROM characters c JOIN account_pvp_total_match_rating_results r ON r.account_id=c.account_id
            WHERE c.character_id=@cid AND r.season_id=@season
            ORDER BY r.rating_result_id DESC LIMIT 1
            """,
            parameters =>
            {
                parameters.AddWithValue("@cid", character.Value);
                parameters.AddWithValue("@season", seasonId);
            },
            reader => new PvpRatingStanding(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
            cancellationToken).ConfigureAwait(false) ?? PvpRatingStanding.Initial;

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
            var updatesRecord = settlement.RecordsResult && fighter.UpdatesRecord;
            var streak = !updatesRecord ? before.WinStreak
                : fighter.Outcome == PvpOutcome.Win ? checked(before.WinStreak + 1) : 0;
            if (updatesRecord)
                await RecordAsync(fighter, streak, cancellationToken).ConfigureAwait(false);
            var result = new PvpFighterResult(
                fighter.Character, fighter.Seat, fighter.Team, fighter.Outcome, fighter.Kills, fighter.Deaths,
                fighter.Fled, fighter.Decision.CountsTowardDaily, before.Grade, fighter.Decision.Grade,
                fighter.Decision.ExperienceChange, fighter.Decision.Experience, streak)
            {
                Rating = fighter.Rating,
            };
            await InsertResultAsync(match, result, cancellationToken).ConfigureAwait(false);
            if (result.Rating is { } rating)
                await InsertRatingAsync(match, result.Character, rating, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        foreach (var (account, rating) in settlement.TotalMatchRatings)
            await InsertTotalMatchRatingAsync(match, account, rating, cancellationToken).ConfigureAwait(false);
        return new PvpSettleOutcome.Created(results);
    }

    private async ValueTask InsertTotalMatchRatingAsync(string match, AccountId account, PvpRatingChange rating,
        CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            """
            INSERT INTO account_pvp_total_match_rating_results
                (match_id,account_id,season_id,placement_games,gp_before,gp_after,peak_gp)
            VALUES(@match,@account,@season,@games,@before,@after,@peak)
            """,
            parameters =>
            {
                parameters.AddWithValue("@match", match);
                parameters.AddWithValue("@account", account.Value);
                parameters.AddWithValue("@season", rating.SeasonId);
                parameters.AddWithValue("@games", rating.Standing.PlacementGames);
                parameters.AddWithValue("@before", rating.PreviousPoints);
                parameters.AddWithValue("@after", rating.Standing.Points);
                parameters.AddWithValue("@peak", rating.Standing.PeakPoints);
            },
            cancellationToken).ConfigureAwait(false);

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

    private async ValueTask InsertRatingAsync(string match, CharacterId character, PvpRatingChange rating,
        CancellationToken cancellationToken) =>
        _ = await database.ExecuteAsync(
            """
            INSERT INTO pvp_match_rating_results(match_id,character_id,season_id,placement_games,rp_before,rp_after,peak_rp)
            VALUES(@match,@cid,@season,@games,@before,@after,@peak)
            """,
            parameters =>
            {
                parameters.AddWithValue("@match", match);
                parameters.AddWithValue("@cid", character.Value);
                parameters.AddWithValue("@season", rating.SeasonId);
                parameters.AddWithValue("@games", rating.Standing.PlacementGames);
                parameters.AddWithValue("@before", rating.PreviousPoints);
                parameters.AddWithValue("@after", rating.Standing.Points);
                parameters.AddWithValue("@peak", rating.Standing.PeakPoints);
            },
            cancellationToken).ConfigureAwait(false);

    private ValueTask<IReadOnlyList<PvpFighterResult>> ResultsAsync(string match, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            $"""
            SELECT {ResultColumns},season_id,rp_before,placement_games,rp_after,peak_rp
            FROM pvp_match_results LEFT JOIN pvp_match_rating_results USING(match_id,character_id)
            WHERE match_id=@match ORDER BY seat,character_id
            """,
            parameters => parameters.AddWithValue("@match", match),
            Result,
            cancellationToken);

    private static PvpFighterResult Result(SqliteDataReader reader) => new(
        new CharacterId(reader.GetInt32(0)), Narrow(reader.GetInt64(1), "seat"), Narrow(reader.GetInt64(2), "team"),
        (PvpOutcome)reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6) != 0,
        reader.GetInt32(7) != 0, Narrow(reader.GetInt64(8), "previous grade"), Narrow(reader.GetInt64(9), "grade"),
        reader.GetInt64(10), reader.GetInt64(11), reader.GetInt32(12))
    {
        Rating = reader.IsDBNull(13) ? null
            : new PvpRatingChange(reader.GetInt32(13), reader.GetInt32(14),
                new PvpRatingStanding(reader.GetInt32(15), reader.GetInt32(16), reader.GetInt32(17))),
    };

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
        if (settlement.Fighters.Select(fighter => fighter.Character).Distinct().Count() != settlement.Fighters.Count)
            throw new ArgumentException("Each character settles once.", nameof(settlement));
        var ratings = settlement.Fighters.Where(fighter => fighter.Rating is not null).ToArray();
        if (ratings.Length > 0 && (ratings.Length != PvpMatchAdmission.FighterCount(settlement.Mode)
            || ratings.Length != settlement.Fighters.Count || settlement.OutcomeKind == PvpMatchOutcomeKind.Cancelled
            || ratings.All(fighter => fighter.Fled) || ratings.Any(fighter => fighter.Rating!.SeasonId != ratings[0].Rating!.SeasonId)))
            throw new ArgumentException("A rated match writes one change per fighter in the same season.", nameof(settlement));
        foreach (var seat in settlement.Fighters.GroupBy(fighter => fighter.Seat))
            if (seat.Count() > 1 && (settlement.Mode != PvpBattleMode.TotalMatch
                || seat.Count() > PvpTotalMatchTeam.MemberCount || seat.Select(fighter => fighter.Team).Distinct().Count() != 1))
                throw new ArgumentException("Only members of the same total-match team may share a seat.", nameof(settlement));
        foreach (var fighter in settlement.Fighters)
        {
            ArgumentNullException.ThrowIfNull(fighter.Decision);
            if (fighter.Kills < 0 || fighter.Deaths < 0 || fighter.Decision.Experience < 0
                || (fighter.Fled && fighter.Outcome != PvpOutcome.Loss)
                || (fighter.Decision.CountsTowardDaily && (!settlement.RecordsResult || !fighter.UpdatesRecord))
                || (!fighter.UpdatesRecord && fighter.Decision.ExperienceChange != 0)
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
