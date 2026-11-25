using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Pvp;

namespace Dfo.Server.Application.Tests.Pvp;

[Trait("Category", "Unit")]
public sealed class PvpColosseumRuleServiceTests
{
    [Theory]
    [InlineData(true, 1, 0, 2, 0)]
    [InlineData(false, 0, 0, 0, 1)]
    public void The_challenge_draws_prizes_at_the_configured_win_or_loss_limit(
        bool won, int wins, int losses, int finalWins, int finalLosses)
    {
        var prize = new PvpColosseumPrize(101, 5, 0);
        var policy = new PvpColosseumRules(102, 3, 2, 2, 1, 900,
            new Dictionary<int, IReadOnlyList<PvpColosseumRewardOption>>
            {
                [finalWins] = [new(0, 1, prize)],
            });
        var rules = new PvpColosseumRuleService(new Rules(policy));
        var run = new PvpColosseumRun(Guid.NewGuid(), new(PvpColosseumKind.Individual, 1), new(1),
            new(2026, 11, 26), wins, losses, []);

        Assert.True(rules.CanStart(1));
        Assert.False(rules.CanStart(2));
        Assert.True(rules.CanResume(run, new(1)));
        Assert.False(rules.CanResume(run, new(2)));
        var result = rules.Advance(run, won);
        Assert.Equal(finalWins, result.Wins);
        Assert.Equal(finalLosses, result.Losses);
        Assert.Equal(prize, Assert.Single(result.Prizes));
        Assert.False(rules.CanResume(result, new(1)));
    }

    private sealed record Rules(PvpColosseumRules Policy) : IPvpColosseumRuleSource
    {
        public PvpColosseumRules Load() => Policy;
    }
}
