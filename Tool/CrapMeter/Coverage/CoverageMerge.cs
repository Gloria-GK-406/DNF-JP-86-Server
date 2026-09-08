using System.Collections.Immutable;

namespace CrapMeter.Coverage;

/// <summary>合并后的一个点：同一身份在多份报告里的访问计数之和。</summary>
public sealed record MergedPoint(string Module, string RelativePath, int Line, long Hits, string? Key = null);

public sealed record MergedCoverage(
    ImmutableArray<MergedPoint> SequencePoints,
    ImmutableArray<MergedPoint> BranchPoints,
    ImmutableArray<RawMethod> Methods,
    ImmutableSortedSet<string> Modules,
    ImmutableSortedSet<string> ModuleBuildConflicts,
    ImmutableSortedSet<string> FilesOutsideRepository,
    int SequencePointsWithoutSourceFile,
    int BranchPointsWithoutSourceFile,
    ImmutableArray<string> SourceReports);

/// <summary>
/// 把多次测试运行的 opencover 报告合成一份。
///
/// 每个测试项目都会把它引用到的全部生产程序集报一遍，所以 7 份报告里同一个方法会出现多次。
/// 按点的身份取访问计数之和——不是把覆盖率求平均，也不是取某一份为准：
/// 一个序列点只要在任意一次运行里被走到，它就是被覆盖的。
/// </summary>
public static class CoverageMerge
{
    public static MergedCoverage Merge(IReadOnlyList<OpenCoverReport> reports)
    {
        var sequence = new Dictionary<(string Module, string Key), MergedPoint>();
        var branch = new Dictionary<(string Module, string Key), MergedPoint>();
        var methods = new Dictionary<(string Module, string Name), RawMethod>();
        var modules = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicts = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
        var outside = ImmutableSortedSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        int sequenceWithoutFile = 0;
        int branchWithoutFile = 0;

        foreach (OpenCoverReport report in reports)
        {
            outside.UnionWith(report.FilesOutsideRepository);

            // 不能用 opencover 的 Module hash 判快照身份：那是**插桩后**程序集的 MVID，
            // coverlet 给每个测试宿主各插一份，本来就每份都不同，拿它比会把每次正常采集
            // 都误报成「中途重新构建过」。真正能判的是同一模块的序列点集合本身——
            // 同一份构建产物插桩出来的点集必须逐点一致。
            foreach (IGrouping<string, RawPoint> group in report.SequencePoints.GroupBy(p => p.Module, StringComparer.Ordinal))
            {
                modules.Add(group.Key);
                string signature = SignatureOf(group);
                if (signatures.TryGetValue(group.Key, out string? seen) && seen != signature)
                {
                    conflicts.Add(group.Key);
                }
                else
                {
                    signatures[group.Key] = signature;
                }
            }

            sequenceWithoutFile += Accumulate(sequence, report.SequencePoints);
            branchWithoutFile += Accumulate(branch, report.BranchPoints);

            foreach (RawMethod method in report.Methods)
            {
                methods.TryAdd((method.Module, method.Name), method);
            }
        }

        return new MergedCoverage(
            [.. sequence.Values.OrderBy(p => p.Module, StringComparer.Ordinal)
                .ThenBy(p => p.RelativePath, StringComparer.Ordinal).ThenBy(p => p.Line)],
            [.. branch.Values.OrderBy(p => p.Module, StringComparer.Ordinal)
                .ThenBy(p => p.RelativePath, StringComparer.Ordinal).ThenBy(p => p.Line)],
            [.. methods.Values],
            modules.ToImmutable(),
            conflicts.ToImmutable(),
            outside.ToImmutable(),
            sequenceWithoutFile,
            branchWithoutFile,
            [.. reports.Select(r => r.SourcePath)]);
    }

    /// <summary>一个模块在某份报告里的序列点集合的指纹。同一份构建产物应当处处相同。</summary>
    private static string SignatureOf(IEnumerable<RawPoint> points)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join(
            '\n',
            points
                .Select(p => p.Key + "|" + p.RelativePath + "|" + p.Line)
                .Order(StringComparer.Ordinal)));

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    /// <returns>因为没有可用源码文件而无法参与对接的点数。</returns>
    private static int Accumulate(
        Dictionary<(string Module, string Key), MergedPoint> target,
        ImmutableArray<RawPoint> points)
    {
        int withoutFile = 0;

        foreach (RawPoint point in points)
        {
            if (point.RelativePath is not { } relativePath)
            {
                withoutFile++;
                continue;
            }

            var key = (point.Module, point.Key);
            target[key] = target.TryGetValue(key, out MergedPoint? existing)
                ? existing with { Hits = existing.Hits + point.VisitCount }
                : new MergedPoint(point.Module, relativePath, point.Line, point.VisitCount, point.Key);
        }

        return withoutFile;
    }
}
