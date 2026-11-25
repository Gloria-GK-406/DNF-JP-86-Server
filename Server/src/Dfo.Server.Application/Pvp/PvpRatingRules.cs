using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Pvp;

internal static class PvpRatingRules
{
    public static bool AppliesTo(PvpMatchConclusion battle) =>
        battle.Origin == PvpRoomOrigin.Matchmaking
        && battle.Channel.SupportsMatchmaking
        && battle.Mode is PvpBattleMode.Individual or PvpBattleMode.Relay
        && battle.Fighters.Length == PvpMatchAdmission.FighterCount(battle.Mode)
        && battle.Terminal.Kind != PvpMatchOutcomeKind.Cancelled
        && !battle.Fighters.All(fighter => fighter.Fled);

    public static bool AppliesToTotalMatch(PvpMatchConclusion battle) =>
        battle.Mode == PvpBattleMode.TotalMatch && battle.Origin == PvpRoomOrigin.Matchmaking
        && battle.Channel.AllowsTotalMatchmaking
        && battle.Fighters.Length == 2 && !battle.TotalMatchAppearances.IsDefaultOrEmpty
        && battle.Terminal.Kind != PvpMatchOutcomeKind.Cancelled && !battle.Fighters.All(fighter => fighter.Fled);

    public static Dictionary<CharacterId, PvpRatingChange> Rate(PvpMatchConclusion battle,
        IReadOnlyDictionary<CharacterId, PvpRatingStanding> standings, PvpRatingSeason season)
    {
        var fighters = battle.Fighters;
        if (battle.Mode == PvpBattleMode.Relay)
        {
            var averages = fighters.GroupBy(fighter => fighter.Team)
                .ToDictionary(team => team.Key, team => team.Average(fighter => standings[fighter.CharacterId].Points));
            return fighters.ToDictionary(fighter => fighter.CharacterId, fighter =>
            {
                var opponent = fighter.Team == PvpTeam.First ? PvpTeam.Second : PvpTeam.First;
                var score = fighter.Fled ? 0.0
                    : battle.Terminal.Kind == PvpMatchOutcomeKind.Draw ? 0.5
                    : battle.Terminal.Winner == (byte)fighter.Team ? 1.0 : 0.0;
                return Update(standings[fighter.CharacterId], averages[opponent] - averages[fighter.Team], score, season);
            });
        }

        var firstId = fighters[0].CharacterId;
        var secondId = fighters[1].CharacterId;
        var first = standings[firstId];
        var second = standings[secondId];
        var firstScore = fighters[0].Fled ? 0.0
            : fighters[1].Fled ? 1.0
            : battle.Terminal.Kind == PvpMatchOutcomeKind.Draw ? 0.5
            : battle.Terminal.Winner == fighters[0].Seat ? 1.0 : 0.0;
        return new()
        {
            [firstId] = Update(first, (double)second.Points - first.Points, firstScore, season),
            [secondId] = Update(second, (double)first.Points - second.Points, 1 - firstScore, season),
        };
    }

    internal static PvpRatingChange Update(PvpRatingStanding own, double opponentAdvantage, double score, PvpRatingSeason season)
    {
        var placing = own.PlacementGames < PvpRatingStanding.PlacementLimit;
        var expected = 1 / (1 + Math.Pow(10, opponentAdvantage / 400));
        var change = (int)Math.Round((placing ? 40 : 20) * (score - expected), MidpointRounding.AwayFromZero);
        var points = (int)Math.Clamp((long)own.Points + change, PvpRatingStanding.MinimumPoints, int.MaxValue);
        var placed = own.PlacementGames + (placing ? 1 : 0);
        var peak = placed == PvpRatingStanding.PlacementLimit ? Math.Max(own.PeakPoints, points) : 0;
        return new(season.Id, own.Points, new(placed, points, peak));
    }
}
