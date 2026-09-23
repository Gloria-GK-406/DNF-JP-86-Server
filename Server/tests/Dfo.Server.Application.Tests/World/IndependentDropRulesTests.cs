using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.World;

public sealed class IndependentDropRulesTests
{
    [Theory]
    [InlineData(1, 4, 1000000, 1)]
    [InlineData(3, 4, 1000000, 3)]
    [InlineData(5, 4, 1000000, 4)]
    [InlineData(8, 4, 1000000, 4)]
    [InlineData(8, 4, 0, 0)]
    [InlineData(0, 4, 1000000, 0)]
    [InlineData(8, 0, 1000000, 0)]
    public void Counts_respect_trials_probability_and_cap(int attempts, int cap, int probability, int expected) =>
        Assert.Equal(expected, IndependentDropRules.SuccessfulAttempts(attempts, cap, probability, 1000000, new(123)));

    [Fact]
    public void Above_basis_saturates_to_guaranteed_drop() =>
        Assert.Equal(4, IndependentDropRules.SuccessfulAttempts(4, 4, 3500000, 1000000, new(123)));

    [Fact]
    public void Weighted_selection_uses_exact_half_open_intervals()
    {
        Assert.Equal(new[] { 0, 2, 2, 2 }, Enumerable.Range(0, 4)
            .Select(ticket => IndependentDropRules.Select([1, 1, 4], ticket)));
        Assert.Throws<ArgumentOutOfRangeException>(() => IndependentDropRules.Select([1, 4], 4));
    }

    [Fact]
    public void Four_player_sixty_percent_case_matches_capped_binomial_expectation()
    {
        var random = new DropRandom(123456);
        var sum = 0;
        for (var i = 0; i < 100000; i++)
            sum += IndependentDropRules.SuccessfulAttempts(8, 4, 600000, 1000000, random);
        Assert.InRange(sum / 100000d, 3.747347, 3.787347);
    }
}
