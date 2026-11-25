using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpMatchmakingRuleService
{
    public int FighterCount(PvpBattleMode mode) => PvpMatchAdmission.FighterCount(mode);

    public CharacterCareer? QueueCareer(CharacterPublicIdentity profile) =>
        profile.Job is < 0 or > byte.MaxValue ? null : new CharacterCareer(profile.Job, profile.GrowType & 0xF);

    public IEnumerable<PvpMatchmakingTicket> EligibleOpponents(PvpMatchmakingTicket ticket,
        IReadOnlyList<PvpMatchmakingTicket> waiting) =>
        waiting.Where(candidate => candidate.Mode == ticket.Mode
            && candidate.Session.ListenerPort == ticket.Session.ListenerPort
            && candidate.Session.AccountId != ticket.Session.AccountId
            && candidate.Session.CharacterId != ticket.Session.CharacterId
            && candidate.Session.WireUserId != ticket.Session.WireUserId
            && !ticket.ExcludedCareers.Contains(candidate.Career) && !candidate.ExcludedCareers.Contains(ticket.Career))
        .DistinctBy(candidate => candidate.Session.AccountId);

    public PvpTeam TeamForSeat(int seat) => seat % 2 == 0 ? PvpTeam.First : PvpTeam.Second;

    public bool AllowsOrdinaryQueue(ChannelEntryIdentity channel, PvpBattleMode mode) =>
        channel.SupportsMatchmaking && mode is PvpBattleMode.Individual or PvpBattleMode.Relay;

    public bool AllowsTotalMatchQueue(ChannelEntryIdentity channel, PvpBattleMode mode) =>
        channel.AllowsTotalMatchmaking && mode == PvpBattleMode.TotalMatch;

    public bool HasCompletedPlacement(PvpRatingStanding standing) =>
        standing.PlacementGames >= PvpRatingStanding.PlacementLimit;

    public bool MeetsRatingFloor(PvpRatingStanding standing, int minimumRp) =>
        HasCompletedPlacement(standing) && standing.Points >= minimumRp;

    public bool HasRequiredTotalMatchEquipment(PvpTotalMatchLineup lineup) =>
        lineup.Candidates.All(candidate =>
            Enumerable.Range(12, 10).All(slot => candidate.Equipment.Any(item => item.SlotIndex == slot)));

    public bool AppliesTo(PvpMatchConclusion battle) =>
        battle.Origin == PvpRoomOrigin.Matchmaking
        && battle.Channel.SupportsMatchmaking
        && battle.Mode is PvpBattleMode.Individual or PvpBattleMode.Relay
        && battle.Fighters.Length == FighterCount(battle.Mode)
        && battle.Terminal.Kind != PvpMatchOutcomeKind.Cancelled
        && !battle.Fighters.All(fighter => fighter.Fled);

    public bool AppliesToTotalMatch(PvpMatchConclusion battle) =>
        battle.Mode == PvpBattleMode.TotalMatch && battle.Origin == PvpRoomOrigin.Matchmaking
        && battle.Channel.AllowsTotalMatchmaking
        && battle.Fighters.Length == FighterCount(battle.Mode) && !battle.TotalMatchAppearances.IsDefaultOrEmpty
        && battle.Terminal.Kind != PvpMatchOutcomeKind.Cancelled && !battle.Fighters.All(fighter => fighter.Fled);

    public Dictionary<CharacterId, PvpRatingChange> Rate(PvpMatchConclusion battle,
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

    public IReadOnlyDictionary<AccountId, PvpRatingChange> RateTotalMatch(PvpMatchConclusion battle,
        PvpRatingStanding own, PvpRatingStanding opponent, PvpRatingSeason season)
    {
        var first = battle.Fighters[0];
        var second = battle.Fighters[1];
        var score = first.Fled ? 0.0 : second.Fled ? 1.0
            : battle.Terminal.Kind == PvpMatchOutcomeKind.Draw ? 0.5
            : battle.Terminal.Winner == (byte)first.Team ? 1.0 : 0.0;
        var advantage = (double)opponent.Points - own.Points;
        return new Dictionary<AccountId, PvpRatingChange>
        {
            [first.Session.AccountId] = Update(own, advantage, score, season),
            [second.Session.AccountId] = Update(opponent, -advantage, 1 - score, season),
        };
    }

    private static PvpRatingChange Update(PvpRatingStanding own, double opponentAdvantage, double score, PvpRatingSeason season)
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

internal sealed record PvpMatchmakingTicket(SelectedSessionIdentity Session, PvpBattleMode Mode, CharacterCareer Career,
    ImmutableArray<CharacterCareer> ExcludedCareers, Guid? ColosseumRun);
