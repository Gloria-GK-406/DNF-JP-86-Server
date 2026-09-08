using CrapMeter;
using Xunit;

namespace CrapMeter.Tests;

public sealed class CrapMetricTests
{
    // 手算：CRAP = C^2 * (1-Cov)^3 + C
    [Theory]
    [InlineData(1, 1.0, 1.0)]              // 全覆盖的最简方法：只剩 C 本身
    [InlineData(10, 0.0, 110.0)]           // 实测零覆盖：100 * 1 + 10
    [InlineData(5, 0.5, 8.125)]            // 25 * 0.125 + 5
    [InlineData(4, 0.75, 4.25)]            // 16 * 0.015625 + 4
    [InlineData(12, 0.0, 156.0)]           // 144 + 12，远在警戒线之上
    [InlineData(6, 1.0, 6.0)]              // 全覆盖：复杂度自己就是分数
    public void Formula_matches_hand_calculation(int complexity, double coverage, double expected) =>
        Assert.Equal(expected, CrapMetric.Compute(complexity, coverage), precision: 10);

    [Fact]
    public void Warning_line_is_the_conventional_thirty() =>
        Assert.Equal(30.0, CrapMetric.WarningLine);

    // 覆盖率必须先归一到 [0,1]。传百分数进来会算出一个看着正常、其实错的分数，
    // 所以宁可炸掉也不静默接受。
    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(80.0)]
    [InlineData(double.NaN)]
    public void Coverage_outside_the_unit_interval_is_rejected(double coverage) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CrapMetric.Compute(3, coverage));

    [Fact]
    public void Negative_complexity_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CrapMetric.Compute(-1, 0.5));
}
