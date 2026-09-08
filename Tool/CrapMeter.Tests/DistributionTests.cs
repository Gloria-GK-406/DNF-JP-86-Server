using CrapMeter;
using CrapMeter.Analysis;
using Xunit;

namespace CrapMeter.Tests;

public sealed class PercentileTests
{
    // 最近秩法：index = ceil(p/100 * N)，1 起。N=10 时 P90 是第 9 个值。
    [Theory]
    [InlineData(50, 5.0)]
    [InlineData(75, 8.0)]
    [InlineData(90, 9.0)]
    [InlineData(95, 10.0)]
    [InlineData(100, 10.0)]
    public void Nearest_rank_picks_the_expected_element(double percentile, double expected)
    {
        double[] sorted = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        Assert.Equal(expected, Percentiles.At(sorted, percentile));
    }

    [Fact]
    public void The_lowest_percentile_never_falls_off_the_front() =>
        Assert.Equal(1.0, Percentiles.At([1.0, 2.0], 0));

    [Fact]
    public void An_empty_set_has_no_percentile() =>
        Assert.Null(Percentiles.At([], 50));

    [Fact]
    public void A_single_value_is_every_percentile() =>
        Assert.Equal(42.0, Percentiles.At([42.0], 95));
}

public sealed class RiskBandTests
{
    [Theory]
    [InlineData(0.0, "low")]
    [InlineData(5.0, "low")]
    [InlineData(5.001, "moderate")]
    [InlineData(15.0, "moderate")]
    [InlineData(30.0, "elevated")]
    [InlineData(30.001, "above-warning")]
    [InlineData(156.0, "above-warning")]
    public void Bands_split_at_the_documented_boundaries(double crap, string expected) =>
        Assert.Equal(expected, RiskBands.Of(crap));
}

public sealed class DistributionTests
{
    [Fact]
    public void Empty_scopes_and_each_scopes_top_functions_are_preserved()
    {
        var rows = Distributions.ByScope("project", new[] { Computed(10, 2, 0), Computed(2, 2, 1) },
            _ => "P", new QuadrantThresholds(3, 0.5), ["P", "Empty"], topN: 1);
        var empty = Assert.Single(rows.Where(r => r.Key == "Empty"));
        Assert.Equal(0, empty.InScope);
        Assert.Empty(empty.TopFunctions);
        Assert.Null(empty.LowShare);
        var measured = Assert.Single(rows.Where(r => r.Key == "P"));
        Assert.Equal(110, Assert.Single(measured.TopFunctions).Crap);
        Assert.Equal(1, measured.LowShare + measured.ModerateShare + measured.ElevatedShare + measured.AboveWarningShare);
    }

    private static FunctionRecord Computed(int complexity, int total, int covered) => new()
    {
        Member = new SourceMember(
            "P", "Server/src/P/F.cs", "N.C", "N.C.M", "M()",
            MemberKind.Method, 1, 2, complexity, HasBody: true),
        State = EvidenceState.Computed,
        SequencePointsTotal = total,
        SequencePointsCovered = covered,
    };

    private static FunctionRecord InState(EvidenceState state) => new()
    {
        Member = new SourceMember(
            "P", "Server/src/P/F.cs", "N.C", "N.C.M", "M()",
            MemberKind.Method, 1, 2, 1, HasBody: true),
        State = state,
    };

    [Fact]
    public void Every_state_is_counted_and_the_totals_reconcile()
    {
        List<FunctionRecord> records =
        [
            Computed(2, 2, 1),
            Computed(10, 2, 0),
            InState(EvidenceState.Missing),
            InState(EvidenceState.Ambiguous),
            InState(EvidenceState.Excluded),
            InState(EvidenceState.Excluded),
        ];

        Distribution d = Distributions.Build("whole", "ALL", records, new QuadrantThresholds(3, 0.5));

        Assert.Equal(6, d.InScope);
        Assert.Equal(2, d.Computed);
        Assert.Equal(1, d.MeasuredZero);
        Assert.Equal(1, d.Missing);
        Assert.Equal(1, d.Ambiguous);
        Assert.Equal(2, d.Excluded);
        Assert.Equal(d.InScope, d.Computed + d.Missing + d.Ambiguous + d.Excluded);
    }

    [Fact]
    public void Percentiles_and_bands_come_only_from_the_computed_set()
    {
        // CRAP：C=2/Cov=0.5 → 4*0.125+2 = 2.5；C=10/Cov=0 → 110
        List<FunctionRecord> records = [Computed(2, 2, 1), Computed(10, 2, 0), InState(EvidenceState.Missing)];
        Distribution d = Distributions.Build("whole", "ALL", records, new QuadrantThresholds(3, 0.5));

        Assert.Equal(2.5, d.Min);
        Assert.Equal(110.0, d.Max);
        Assert.Equal(1, d.AboveWarning);
        Assert.Equal(0.5, d.AboveWarningShare);
        Assert.Equal(1, d.Low);
        Assert.Equal(0, d.Moderate);
        Assert.Equal(0, d.Elevated);
    }

    [Fact]
    public void Quadrants_split_on_the_shared_thresholds()
    {
        List<FunctionRecord> records =
        [
            Computed(10, 10, 1),  // 高 C，低 Cov
            Computed(10, 10, 9),  // 高 C，高 Cov
            Computed(1, 10, 1),   // 低 C，低 Cov
            Computed(1, 10, 9),   // 低 C，高 Cov
        ];

        Distribution d = Distributions.Build("whole", "ALL", records, new QuadrantThresholds(5, 0.5));

        Assert.Equal(1, d.HighComplexityLowCoverage);
        Assert.Equal(1, d.HighComplexityHighCoverage);
        Assert.Equal(1, d.LowComplexityLowCoverage);
        Assert.Equal(1, d.LowComplexityHighCoverage);
        Assert.Equal(
            d.Computed,
            d.HighComplexityLowCoverage + d.HighComplexityHighCoverage
                + d.LowComplexityLowCoverage + d.LowComplexityHighCoverage);
    }

    // 均值与总和只是补充量；它们对函数个数敏感，不能替代分布。这里只钉住它们确实是
    // 「函数分数」的均值与总和，而不是别的什么聚合。
    [Fact]
    public void Mean_and_sum_are_over_function_scores()
    {
        List<FunctionRecord> records = [Computed(2, 2, 1), Computed(10, 2, 0)];
        Distribution d = Distributions.Build("whole", "ALL", records, new QuadrantThresholds(3, 0.5));

        Assert.Equal(112.5, d.FunctionScoreSum!.Value, precision: 10);
        Assert.Equal(56.25, d.FunctionScoreMean!.Value, precision: 10);
    }

    [Fact]
    public void A_scope_with_no_computed_function_has_no_percentiles_rather_than_zeros()
    {
        Distribution d = Distributions.Build(
            "class", "N.C", [InState(EvidenceState.Missing)], new QuadrantThresholds(3, 0.5));

        Assert.Null(d.Median);
        Assert.Null(d.Max);
        Assert.Null(d.AboveWarningShare);
        Assert.Null(d.FunctionScoreMean);
    }
}
