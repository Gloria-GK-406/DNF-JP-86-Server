using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Pvp;

public sealed class PvpTotalMatchExperienceTests
{
    [Fact]
    public async Task Only_actors_who_fought_earn_experience_once_using_their_own_grade_and_shared_daily_quota()
    {
        var fixture = new PvpRoomFixture();
        var conclusion = Match(fixture);
        var winner = new CharacterId(21);
        var uncappedLoser = new CharacterId(22);
        var cappedLoser = new CharacterId(23);
        fixture.Records.Records[winner] = new(2, 0, 250, 0, 0, 0, 0, 0);
        fixture.Records.Records[uncappedLoser] = new(1, 0, 150, 0, 0, 0, 0, 0);
        var day = fixture.BattleRules.GameDayOf(conclusion.StartedAt);
        for (var i = 0; i < 10; i++)
            await fixture.Run(() => fixture.Records.SettleAsync(new(Guid.NewGuid(), 2, 10050, 24, PvpBattleMode.Individual,
                PvpMatchOutcomeKind.Decided, 0, true, day, conclusion.Terminal.At,
                [new(cappedLoser, 1, 0, PvpOutcome.Loss, 0, 1, false, new(2, 250, 0, true))]), default));

        var settlement = await fixture.Settlement.BuildSettlementAsync(conclusion, default);
        var first = await fixture.Run(() => fixture.Records.SettleAsync(settlement, default));
        var builtAgain = await fixture.Settlement.BuildSettlementAsync(conclusion, default);
        var again = await fixture.Run(() => fixture.Records.SettleAsync(builtAgain, default));
        Assert.Equal(Assert.IsType<PvpSettleOutcome.Created>(first).Results,
            Assert.IsType<PvpSettleOutcome.AlreadySettled>(again).Results);

        var results = first.Results.ToDictionary(result => result.Character);
        Assert.Equal(5, results.Count);
        Assert.Equal((40L, 3, 0), (results[winner].ExperienceChange, results[winner].Kills, results[winner].Deaths));
        Assert.Equal(-20L, results[uncappedLoser].ExperienceChange);
        Assert.Equal(0L, results[cappedLoser].ExperienceChange);
        Assert.Equal(11, (await fixture.Records.DailyCountsAsync(cappedLoser, day, default)).Losses);
        Assert.Equal(new PvpDailyCounts(1, 0), await fixture.Records.DailyCountsAsync(winner, day, default));
        foreach (var representative in conclusion.Fighters)
        {
            Assert.Equal(0, results[representative.CharacterId].ExperienceChange);
            Assert.False(results[representative.CharacterId].CountsTowardDaily);
            Assert.Null(fixture.Records.Of(representative.Session).Experience);
        }
        Assert.Equal(290, (await fixture.Records.ReadAsync(winner, default)).Experience);
        Assert.DoesNotContain(new CharacterId(24), results.Keys);
    }

    [Theory]
    [InlineData("free")]
    [InlineData("before-combat")]
    [InlineData("double-forfeit")]
    [InlineData("cancelled")]
    public async Task Ineligible_matches_leave_every_character_record_unchanged(string reason)
    {
        var fixture = new PvpRoomFixture();
        var conclusion = Match(fixture);
        conclusion = reason switch
        {
            "free" => conclusion with { Channel = PvpRoomFixture.Channel },
            "before-combat" => conclusion with { TotalMatchAppearances = [] },
            "double-forfeit" => conclusion with
            {
                Terminal = conclusion.Terminal with { Kind = PvpMatchOutcomeKind.Draw, Winner = null },
                Fighters = [.. conclusion.Fighters.Select(fighter => fighter with { Fled = true })],
            },
            "cancelled" => conclusion with
            {
                Terminal = conclusion.Terminal with { Kind = PvpMatchOutcomeKind.Cancelled, Winner = null },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };
        var settlement = await fixture.Settlement.BuildSettlementAsync(conclusion, default);
        var result = await fixture.Run(() => fixture.Records.SettleAsync(settlement, default));

        Assert.False(settlement.RecordsResult);
        Assert.All(result.Results, actor => Assert.Equal(0, actor.ExperienceChange));
        Assert.Empty(fixture.Records.Records);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_played_role_inherits_a_draw_or_its_own_teams_forfeit(bool forfeit)
    {
        var fixture = new PvpRoomFixture();
        var conclusion = Match(fixture);
        conclusion = forfeit
            ? conclusion with { Fighters = conclusion.Fighters.SetItem(1, conclusion.Fighters[1] with { Fled = true }) }
            : conclusion with { Terminal = conclusion.Terminal with { Kind = PvpMatchOutcomeKind.Draw, Winner = null } };
        var loser = new CharacterId(22);
        fixture.Records.Records[loser] = new(1, 0, 150, 0, 0, 0, 0, 0);
        var settlement = await fixture.Settlement.BuildSettlementAsync(conclusion, default);
        var result = await fixture.Run(() => fixture.Records.SettleAsync(settlement, default));

        var played = result.Results.Single(actor => actor.Character == loser);
        Assert.Equal(forfeit ? PvpOutcome.Loss : PvpOutcome.Draw, played.Outcome);
        Assert.Equal(forfeit, played.Fled);
        Assert.Equal(forfeit ? -20L : 0L, played.ExperienceChange);
    }

    private static PvpMatchConclusion Match(PvpRoomFixture fixture)
    {
        var owner = fixture.Join(11);
        var guest = fixture.Join(12);
        var now = fixture.Clock.Now;
        return new(PvpMatchId.New(), PvpRoomId.New(), 1, new(50, 10050, ChannelEntryKind.TraditionalDuel, 24), PvpBattleMode.TotalMatch, now,
            new(PvpMatchOutcomeKind.Decided, (byte)PvpTeam.First, now),
            [new(0, owner.CharacterId, owner, PvpTeam.First), new(1, guest.CharacterId, guest, PvpTeam.Second)], now)
        {
            TotalMatchAppearances = [new(0, new(21), 3, 0), new(1, new(22), 0, 1), new(1, new(23), 0, 1)],
        };
    }
}
