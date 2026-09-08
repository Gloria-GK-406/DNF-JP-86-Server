namespace CrapMeter;

/// <summary>源码里声明的成员种类。生成的方法不在此列——它们按行号归并回声明处。</summary>
public enum MemberKind
{
    Method,
    Constructor,
    StaticConstructor,
    Destructor,
    Operator,
    ConversionOperator,
    PropertyAccessor,
    IndexerAccessor,
    EventAccessor,
    AutoProperty,
    AutoEvent,
}

/// <summary>
/// 证据状态。每个在范围内的成员落在且只落在一个状态里。
/// <see cref="Computed"/> 含实测零覆盖——后者由 <see cref="FunctionRecord.IsMeasuredZero"/> 单独标出，
/// 因为「测到了、一行没覆盖」与「没测到」对决策的含义相反。
/// </summary>
public enum EvidenceState
{
    Computed,
    Missing,
    Ambiguous,
    Excluded,
}

/// <summary>Roslyn 扫出来的一条源码成员。行号为 1 起，区间闭合。</summary>
public sealed record SourceMember(
    string Project,
    string RelativePath,
    string Container,
    string QualifiedSymbol,
    string Signature,
    MemberKind Kind,
    int StartLine,
    int EndLine,
    int Complexity,
    bool HasBody)
{
    public string Id => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes($"{Project}|{RelativePath}|{Container}|{Signature}|{Kind}|{HasBody}")));
}

/// <summary>合并后的一个序列点：某文件某行的访问计数。</summary>
public sealed record CoveragePoint(
    string Module,
    string RelativePath,
    int Line,
    long VisitCount,
    int BranchesTotal,
    int BranchesVisited);

/// <summary>成员 + 覆盖率证据 + CRAP。未舍入值全部保留，只在展示时舍入。</summary>
public sealed class FunctionRecord
{
    public required SourceMember Member { get; init; }
    public required EvidenceState State { get; init; }

    /// <summary>取不到值时为 null。</summary>
    public string? Reason { get; init; }

    public int SequencePointsTotal { get; init; }
    public int SequencePointsCovered { get; init; }
    public int BranchesTotal { get; init; }
    public int BranchesVisited { get; init; }

    /// <summary>opencover 自报的 IL 圈复杂度之和，仅作交叉核对，不参与计算。</summary>
    public int? IlComplexity { get; init; }

    public IReadOnlyList<CrapMeter.Coverage.MergedPoint> CoveragePoints { get; init; } = [];
    public IReadOnlyList<string> CoverageReports { get; init; } = [];

    public double? Coverage => SequencePointsTotal == 0
        ? null
        : (double)SequencePointsCovered / SequencePointsTotal;

    public double? BranchCoverage => BranchesTotal == 0
        ? null
        : (double)BranchesVisited / BranchesTotal;

    /// <summary>报告证明存在可执行单位且一个都没覆盖。</summary>
    public bool IsMeasuredZero =>
        State == EvidenceState.Computed && SequencePointsTotal > 0 && SequencePointsCovered == 0;

    public double? Crap => State == EvidenceState.Computed && Coverage is { } cov
        ? CrapMetric.Compute(Member.Complexity, cov)
        : null;
}

/// <summary>CRAP(f) = C(f)^2 * (1 - Cov(f))^3 + C(f)</summary>
public static class CrapMetric
{
    public const double WarningLine = 30.0;

    public static double Compute(int complexity, double coverage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(complexity);
        if (coverage is < 0 or > 1 || double.IsNaN(coverage))
        {
            throw new ArgumentOutOfRangeException(
                nameof(coverage), coverage, "覆盖率必须归一到 [0, 1]。");
        }

        double c = complexity;
        double uncovered = 1.0 - coverage;
        return (c * c * uncovered * uncovered * uncovered) + c;
    }
}
