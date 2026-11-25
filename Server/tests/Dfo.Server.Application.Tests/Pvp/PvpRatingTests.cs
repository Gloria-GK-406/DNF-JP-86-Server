using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Tests.Pvp;

public sealed class PvpRatingTests
{
    [Fact]
    public async Task The_twentieth_match_publishes_rating_and_the_next_one_uses_the_official_rate()
    {
        var fixture = new PvpRoomFixture();
        var match = Match(fixture);
        var first = match.Fighters[0].CharacterId;
        var second = match.Fighters[1].CharacterId;
        fixture.Records.Ratings[(first, 1)] = new(19, 1000, 0);
        fixture.Records.Ratings[(second, 1)] = new(19, 1200, 0);
        Assert.Equal(0, (await fixture.Records.ReadRatingAsync(first, PvpRatingSeason.First.Id, default)).OfficialPoints);

        var placed = await SettleAsync(fixture, match);
        Assert.Equal(new PvpRatingStanding(20, 1030, 1030), placed.Results[0].Rating!.Standing);
        Assert.Equal(new PvpRatingStanding(20, 1170, 1170), placed.Results[1].Rating!.Standing);

        var next = await SettleAsync(fixture, match with { MatchId = PvpMatchId.New() });
        Assert.Equal(new PvpRatingStanding(20, 1044, 1044), next.Results[0].Rating!.Standing);
        Assert.Equal(new PvpRatingStanding(20, 1156, 1170), next.Results[1].Rating!.Standing);
        Assert.Equal(1044, (await fixture.Records.ReadRatingAsync(first, PvpRatingSeason.First.Id, default)).OfficialPoints);
        Assert.All(next.Results, result => Assert.Equal(0, result.ExperienceChange));
    }

    [Theory]
    [InlineData(false, 1000, 1000)]
    [InlineData(true, 1020, 980)]
    public async Task A_draw_or_a_single_forfeit_counts_for_both_characters(bool forfeit, int first, int second)
    {
        var fixture = new PvpRoomFixture();
        var match = Match(fixture);
        match = match with { Terminal = match.Terminal with { Kind = PvpMatchOutcomeKind.Draw, Winner = null } };
        if (forfeit)
            match = match with { Fighters = match.Fighters.SetItem(1, match.Fighters[1] with { Fled = true }) };

        var result = await SettleAsync(fixture, match);

        Assert.Equal(new PvpRatingStanding(1, first, 0), result.Results[0].Rating!.Standing);
        Assert.Equal(new PvpRatingStanding(1, second, 0), result.Results[1].Rating!.Standing);
        Assert.All(result.Results, fighter => Assert.Equal(0, fighter.Rating!.Standing.OfficialPoints));
    }

    [Fact]
    public async Task A_relay_uses_the_same_ladder_with_team_averages_and_an_individual_forfeit()
    {
        var fixture = new PvpRoomFixture();
        var match = Match(fixture);
        var fighters = Enumerable.Range(0, 6).Select(seat =>
        {
            var session = seat < 2 ? match.Fighters[seat].Session : fixture.Join(11 + seat);
            var team = seat % 2 == 0 ? PvpTeam.First : PvpTeam.Second;
            fixture.Records.Ratings[(session.CharacterId, PvpRatingSeason.First.Id)] = team == PvpTeam.First
                ? new(19, 1000, 0)
                : new(20, 1200, 1200);
            return new PvpFighter(seat, session.CharacterId, session, team) { Fled = seat == 4 };
        }).ToArray();
        match = match with
        {
            Mode = PvpBattleMode.Relay,
            Fighters = [.. fighters],
            Terminal = match.Terminal with { Winner = (byte)PvpTeam.First },
        };

        var result = await SettleAsync(fixture, match);

        Assert.Equal(new[] { 1030, 1185, 1030, 1185, 990, 1185 },
            result.Results.Select(fighter => fighter.Rating!.Standing.Points));
        Assert.All(result.Results, fighter => Assert.Equal(20, fighter.Rating!.Standing.PlacementGames));
        Assert.Equal(PvpOutcome.Loss, result.Results[4].Outcome);
        Assert.Equal(1200, result.Results[1].Rating!.Standing.PeakPoints);
    }

    [Theory]
    [InlineData("player-room")]
    [InlineData("free-channel")]
    [InlineData("cancelled")]
    [InlineData("double-forfeit")]
    public async Task Excluded_matches_do_not_change_placement_progress(string reason)
    {
        var fixture = new PvpRoomFixture();
        var match = Match(fixture);
        match = reason switch
        {
            "player-room" => match with { Origin = default },
            "free-channel" => match with { Channel = PvpRoomFixture.Channel },
            "cancelled" => match with { Terminal = match.Terminal with { Kind = PvpMatchOutcomeKind.Cancelled, Winner = null } },
            "double-forfeit" => match with { Fighters = [.. match.Fighters.Select(fighter => fighter with { Fled = true })] },
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

        var result = await SettleAsync(fixture, match);

        Assert.All(result.Results, fighter => Assert.Null(fighter.Rating));
        Assert.Empty(fixture.Records.Ratings);
    }

    private static async Task<PvpSettleOutcome> SettleAsync(PvpRoomFixture fixture, PvpMatchConclusion match)
    {
        var settlement = await fixture.Settlement.BuildSettlementAsync(match, default);
        return await fixture.Run(() => fixture.Records.SettleAsync(settlement, default));
    }

    private static PvpMatchConclusion Match(PvpRoomFixture fixture)
    {
        var first = fixture.Join(11);
        var second = fixture.Join(12);
        var now = fixture.Clock.Now;
        return new(PvpMatchId.New(), PvpRoomId.New(), 1, new(60, 10060, ChannelEntryKind.MatchmakingDuel, 8),
            PvpBattleMode.Individual, now, new(PvpMatchOutcomeKind.Decided, 0, now),
            [new(0, first.CharacterId, first, PvpTeam.First), new(1, second.CharacterId, second, PvpTeam.Second)], now)
        {
            Origin = PvpRoomOrigin.Matchmaking,
        };
    }
}
