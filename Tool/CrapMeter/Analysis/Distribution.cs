using System.Collections.Immutable;

namespace CrapMeter.Analysis;

/// <summary>
/// 分位数：最近秩法（nearest-rank）。<c>index = ceil(p/100 * N)</c>，1 起，钳到 [1, N]。
/// 口径写进清单——换算法会让两次运行不可比。
/// </summary>
public static class Percentiles
{
    public const string Definition =
        "nearest-rank on the ascending sorted values: index = ceil(p/100 * N), 1-based, clamped to [1, N].";

    /// <param name="sorted">升序排好的值。空集合返回 null。</param>
    public static double? At(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            return null;
        }

        int rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Count);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }
}

/// <summary>风险分档。<c>&gt; 30</c> 是 CRAP 的常规警戒线，其余三档为本仓所取，写进清单。</summary>
public static class RiskBands
{
    public const string Definition =
        "low: CRAP <= 5 | moderate: 5 < CRAP <= 15 | elevated: 15 < CRAP <= 30 | above-warning: CRAP > 30";

    public static string Of(double crap) => crap switch
    {
        <= 5 => "low",
        <= 15 => "moderate",
        <= CrapMetric.WarningLine => "elevated",
        _ => "above-warning",
    };
}

/// <summary>一个聚合范围（全域/项目/文件/类）里函数级 CRAP 的分布。CRAP 本身没有这一层的分数。</summary>
public sealed record Distribution(
    string Scope,
    string Key,
    int InScope,
    int Computed,
    int MeasuredZero,
    int Missing,
    int Ambiguous,
    int Excluded,
    double? Min,
    double? Median,
    double? P75,
    double? P90,
    double? P95,
    double? Max,
    int Low,
    int Moderate,
    int Elevated,
    int AboveWarning,
    double? AboveWarningShare,
    double? FunctionScoreMean,
    double? FunctionScoreSum,
    int HighComplexityLowCoverage,
    int HighComplexityHighCoverage,
    int LowComplexityLowCoverage,
    int LowComplexityHighCoverage)
{
    public double? LowShare => Computed == 0 ? null : (double)Low / Computed;
    public double? ModerateShare => Computed == 0 ? null : (double)Moderate / Computed;
    public double? ElevatedShare => Computed == 0 ? null : (double)Elevated / Computed;
    public double? MeasuredShare => InScope - Excluded == 0 ? null : (double)Computed / (InScope - Excluded);
    public IReadOnlyList<RankedFunction> TopFunctions { get; init; } = [];
}

public sealed record RankedFunction(string Id, string Symbol, string Signature, string Path, int Line,
    int Complexity, double Coverage, double Crap);

/// <summary>四象限的切分阈值，一次算好、全部范围共用，这样子范围之间可比。</summary>
public sealed record QuadrantThresholds(int Complexity, double Coverage)
{
    public string Definition =>
        $"high complexity: C > {Complexity} (whole-scope P75 of C among computed functions); " +
        $"low coverage: Cov < {Coverage.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
}

public static class Distributions
{
    /// <summary>
    /// 复杂度切分取全域已算集合的 <b>P75</b>，不取中位数。
    ///
    /// 本仓（以及多数 C# 仓）里过半成员是访问器与直通方法，复杂度就是 1，
    /// 中位数因此也是 1——按它切，「高复杂度」会把所有带一个 <c>if</c> 的方法都算进去，
    /// 象限分不出东西。P75 落在有实际控制流的那一段上。
    /// 阈值是项目相对的，所以跨仓库不可比；跨次比较前先比清单里的这行定义。
    /// </summary>
    public static QuadrantThresholds ThresholdsFrom(IReadOnlyList<FunctionRecord> records)
    {
        List<double> complexities = [.. records
            .Where(r => r.State == EvidenceState.Computed)
            .Select(r => (double)r.Member.Complexity)
            .Order()];

        return new QuadrantThresholds((int)(Percentiles.At(complexities, 75) ?? 1), 0.5);
    }

    public static Distribution Build(
        string scope, string key, IReadOnlyList<FunctionRecord> records, QuadrantThresholds thresholds, int topN = 30)
    {
        List<FunctionRecord> computed = [.. records.Where(r => r.State == EvidenceState.Computed)];
        List<double> craps = [.. computed.Select(r => r.Crap!.Value).Order()];

        int aboveWarning = craps.Count(c => c > CrapMetric.WarningLine);

        return new Distribution(
            Scope: scope,
            Key: key,
            InScope: records.Count,
            Computed: computed.Count,
            MeasuredZero: computed.Count(r => r.IsMeasuredZero),
            Missing: records.Count(r => r.State == EvidenceState.Missing),
            Ambiguous: records.Count(r => r.State == EvidenceState.Ambiguous),
            Excluded: records.Count(r => r.State == EvidenceState.Excluded),
            Min: craps.Count == 0 ? null : craps[0],
            Median: Percentiles.At(craps, 50),
            P75: Percentiles.At(craps, 75),
            P90: Percentiles.At(craps, 90),
            P95: Percentiles.At(craps, 95),
            Max: craps.Count == 0 ? null : craps[^1],
            Low: craps.Count(c => RiskBands.Of(c) == "low"),
            Moderate: craps.Count(c => RiskBands.Of(c) == "moderate"),
            Elevated: craps.Count(c => RiskBands.Of(c) == "elevated"),
            AboveWarning: aboveWarning,
            AboveWarningShare: craps.Count == 0 ? null : (double)aboveWarning / craps.Count,
            // 均值与总和是补充量，对规模敏感，不能替代分布与尾部集中度。
            FunctionScoreMean: craps.Count == 0 ? null : craps.Average(),
            FunctionScoreSum: craps.Count == 0 ? null : craps.Sum(),
            HighComplexityLowCoverage: Quadrant(computed, thresholds, high: true, low: true),
            HighComplexityHighCoverage: Quadrant(computed, thresholds, high: true, low: false),
            LowComplexityLowCoverage: Quadrant(computed, thresholds, high: false, low: true),
            LowComplexityHighCoverage: Quadrant(computed, thresholds, high: false, low: false))
        {
            TopFunctions = computed.OrderByDescending(r => r.Crap).ThenBy(r => r.Member.Id, StringComparer.Ordinal)
                .Take(topN).Select(r => new RankedFunction(r.Member.Id, r.Member.QualifiedSymbol, r.Member.Signature,
                    r.Member.RelativePath, r.Member.StartLine, r.Member.Complexity, r.Coverage!.Value, r.Crap!.Value)).ToArray(),
        };
    }

    private static int Quadrant(
        IEnumerable<FunctionRecord> computed, QuadrantThresholds thresholds, bool high, bool low) =>
        computed.Count(r =>
            r.Member.Complexity > thresholds.Complexity == high
            && r.Coverage < thresholds.Coverage == low);

    public static ImmutableArray<Distribution> ByScope(
        string scope,
        IReadOnlyList<FunctionRecord> records,
        Func<FunctionRecord, string> keySelector,
        QuadrantThresholds thresholds, IEnumerable<string>? includedKeys = null, int topN = 30)
    {
        var groups = records.GroupBy(keySelector, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList());
        foreach (string key in includedKeys ?? []) groups.TryAdd(key, []);
        return [.. groups
            .Select(g => Build(scope, g.Key, g.Value, thresholds, topN))
            .OrderByDescending(d => d.AboveWarning)
            .ThenByDescending(d => d.P90 ?? 0)
            .ThenBy(d => d.Key, StringComparer.Ordinal)];
    }
}
