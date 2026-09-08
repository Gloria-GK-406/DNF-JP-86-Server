using System.Collections.Immutable;
using CrapMeter.Coverage;
using CrapMeter.Inventory;

namespace CrapMeter.Analysis;

/// <summary>归并不掉的序列点，按文件与成因分组。它们不是被丢弃，是被单独记账。</summary>
public sealed record UnmappedPoints(
    string RelativePath,
    string Kind,
    int Points,
    int CoveredPoints);

public sealed record JoinResult(
    ImmutableArray<FunctionRecord> Records,
    ImmutableArray<UnmappedPoints> Unmapped,
    ImmutableSortedSet<string> ModulesWithPointsOutsideScope,
    ImmutableSortedSet<string> ProjectsWithoutModule,
    int SequencePointsTotal,
    int SequencePointsAttributed,
    int SequencePointsAmbiguous)
{
    public int SequencePointsUnmapped => Unmapped.Sum(u => u.Points);
}

/// <summary>
/// 按 <c>(规范化文件路径, 行号)</c> 把覆盖率点归到最内层包含它的源码成员。
///
/// 这条规则一次性解决重载、分部类、泛型、显式接口实现、lambda、局部函数、
/// <c>async</c>/迭代器状态机：编译器生成的方法虽然在 IL 里是独立方法，
/// 但它们的序列点行号仍落在声明处成员的区间内，所以自然归并回去，
/// 不需要去解析 <c>&lt;Foo&gt;b__0_1</c> 这类名字改写规则。
/// </summary>
public static class CoverageJoin
{
    private const int NoMember = -1;
    private const int AmbiguousMember = -2;

    public static JoinResult Join(InventoryResult inventory, MergedCoverage coverage)
    {
        var byPath = inventory.Files.ToDictionary(
            f => f.RelativePath, StringComparer.OrdinalIgnoreCase);

        var members = inventory.Files
            .SelectMany(f => f.Members.Select(m => (File: f, Member: m)))
            .ToImmutableArray();

        var indexOf = new Dictionary<SourceMember, int>();
        for (int i = 0; i < members.Length; i++)
        {
            indexOf[members[i].Member] = i;
        }

        var lineMaps = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach (FileInventory file in inventory.Files)
        {
            lineMaps[file.RelativePath] = BuildLineMap(file, indexOf);
        }

        var sequenceTotals = new int[members.Length];
        var sequenceCovered = new int[members.Length];
        var branchTotals = new int[members.Length];
        var branchCovered = new int[members.Length];
        var ambiguous = new bool[members.Length];
        var evidence = Enumerable.Range(0, members.Length).Select(_ => new List<MergedPoint>()).ToArray();
        var unmapped = new Dictionary<(string Path, string Kind), (int Points, int Covered)>();
        var modulesWithPointsOutsideScope = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
        int attributed = 0;
        int ambiguousPoints = 0;

        // isSequence 决定这一轮是否记账：归并不掉的点与对账计数只按序列点统计，
        // 否则分支点会混进序列点的对账等式里，等式就不再成立。
        void Attribute(MergedPoint point, int[] totals, int[] covered, bool isSequence)
        {
            if (!byPath.TryGetValue(point.RelativePath, out FileInventory? file))
            {
                if (isSequence)
                {
                    Record(unmapped, point, "file-not-in-scope");
                    modulesWithPointsOutsideScope.Add(point.Module);
                }

                return;
            }

            int[] map = lineMaps[file.RelativePath];
            int index = point.Line >= 0 && point.Line < map.Length ? map[point.Line] : NoMember;
            if (point.Module != file.Project)
            {
                if (isSequence) Record(unmapped, point, "module-does-not-match-source-project");
                return;
            }
            // Initializers belong to constructors, not a get/set sharing their source line.
            if (point.Key is { } key && (key.Contains("::.ctor(", StringComparison.Ordinal)
                    || key.Contains("::.cctor(", StringComparison.Ordinal))
                && file.NonMemberRanges.Any(r => r.Kind == "property-initializer"
                    && r.StartLine <= point.Line && point.Line <= r.EndLine))
            {
                if (isSequence) Record(unmapped, point, "property-initializer");
                return;
            }
            if (index == AmbiguousMember && point.Key is not null)
            {
                var candidates = file.Members.Where(m => m.StartLine <= point.Line && point.Line <= m.EndLine
                    && AccessorMatches(m, point.Key)).ToArray();
                if (candidates.Length == 1) index = indexOf[candidates[0]];
            }

            switch (index)
            {
                case NoMember:
                    if (isSequence)
                    {
                        Record(unmapped, point, ClassifyNonMember(file, point.Line));
                    }

                    break;

                case AmbiguousMember:
                    if (isSequence)
                    {
                        Record(unmapped, point, "ambiguous-member-range");
                        ambiguousPoints++;
                    }

                    MarkAmbiguous(file, point.Line, indexOf, ambiguous);
                    break;

                default:
                    totals[index]++;
                    if (point.Hits > 0)
                    {
                        covered[index]++;
                    }

                    if (isSequence)
                    {
                        attributed++;
                        evidence[index].Add(point);
                    }

                    break;
            }
        }

        foreach (MergedPoint point in coverage.SequencePoints)
        {
            Attribute(point, sequenceTotals, sequenceCovered, isSequence: true);
        }

        foreach (MergedPoint point in coverage.BranchPoints)
        {
            Attribute(point, branchTotals, branchCovered, isSequence: false);
        }

        var ilComplexity = new int[members.Length];
        foreach (RawMethod method in coverage.Methods)
        {
            if (method.RelativePath is not { } path
                || !lineMaps.TryGetValue(path, out int[]? map)
                || method.FirstLine < 0
                || method.FirstLine >= map.Length)
            {
                continue;
            }

            int index = map[method.FirstLine];
            if (index >= 0)
            {
                ilComplexity[index] += method.IlComplexity;
            }
        }

        var projectsWithoutModule = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (string project in inventory.Projects)
        {
            if (inventory.Files.Any(f => f.Project == project && f.Members.Length > 0)
                && !coverage.Modules.Contains(project))
            {
                projectsWithoutModule.Add(project);
            }
        }

        var records = ImmutableArray.CreateBuilder<FunctionRecord>(members.Length);
        for (int i = 0; i < members.Length; i++)
        {
            SourceMember member = members[i].Member;
            bool moduleCovered = coverage.Modules.Contains(member.Project);
            (EvidenceState state, string? reason) = Classify(
                moduleCovered, ambiguous[i], sequenceTotals[i], member.HasBody);
            if (coverage.ModuleBuildConflicts.Contains(member.Project))
                (state, reason) = (EvidenceState.Missing, "coverage-module-snapshot-conflict");
            else if (member.Project == "Dfo.Server.Host")
                (state, reason) = (EvidenceState.Missing, "child-process-coverage-not-collected");

            records.Add(new FunctionRecord
            {
                Member = member,
                State = state,
                Reason = reason,
                SequencePointsTotal = sequenceTotals[i],
                SequencePointsCovered = sequenceCovered[i],
                BranchesTotal = branchTotals[i],
                BranchesVisited = branchCovered[i],
                IlComplexity = ilComplexity[i] > 0 ? ilComplexity[i] : null,
                CoveragePoints = evidence[i],
                CoverageReports = coverage.SourceReports,
            });
        }

        return new JoinResult(
            records.ToImmutable(),
            [.. unmapped
                .Select(kv => new UnmappedPoints(kv.Key.Path, kv.Key.Kind, kv.Value.Points, kv.Value.Covered))
                .OrderByDescending(u => u.Points).ThenBy(u => u.RelativePath, StringComparer.Ordinal)],
            modulesWithPointsOutsideScope.ToImmutable(),
            projectsWithoutModule.ToImmutable(),
            coverage.SequencePoints.Length,
            attributed,
            ambiguousPoints);
    }

    private static bool AccessorMatches(SourceMember member, string key)
    {
        if (member.Kind is not (MemberKind.PropertyAccessor or MemberKind.IndexerAccessor
            or MemberKind.EventAccessor or MemberKind.AutoProperty or MemberKind.AutoEvent)) return false;
        int dot = member.Signature.LastIndexOf('.');
        if (dot < 0) return false;
        string name = member.Signature[..dot];
        string accessor = member.Signature[(dot + 1)..].Split('(')[0];
        if (accessor == "init") accessor = "set";
        if (name == "this") name = "Item";
        int explicitDot = name.LastIndexOf('.');
        string metadata = explicitDot < 0 ? accessor + "_" + name
            : name[..(explicitDot + 1)] + accessor + "_" + name[(explicitDot + 1)..];
        return key.Contains("::" + metadata + "(", StringComparison.Ordinal);
    }

    private static (EvidenceState State, string? Reason) Classify(
        bool moduleCovered, bool ambiguous, int sequencePoints, bool hasBody)
    {
        if (ambiguous)
        {
            return (EvidenceState.Ambiguous, "overlapping-member-ranges");
        }

        if (!moduleCovered)
        {
            return (EvidenceState.Missing, "module-not-instrumented");
        }

        if (sequencePoints > 0)
        {
            return (EvidenceState.Computed, null);
        }

        return hasBody
            ? (EvidenceState.Missing, "no-coverage-rows-for-member")
            : (EvidenceState.Excluded, "no-executable-units");
    }

    private static int[] BuildLineMap(FileInventory file, Dictionary<SourceMember, int> indexOf)
    {
        int maxLine = file.Members.Length == 0 ? 0 : file.Members.Max(m => m.EndLine);
        var map = new int[maxLine + 1];
        Array.Fill(map, NoMember);

        foreach (SourceMember member in file.Members)
        {
            int index = indexOf[member];
            for (int line = member.StartLine; line <= member.EndLine; line++)
            {
                map[line] = map[line] == NoMember ? index : AmbiguousMember;
            }
        }

        return map;
    }

    private static void MarkAmbiguous(
        FileInventory file, int line, Dictionary<SourceMember, int> indexOf, bool[] ambiguous)
    {
        foreach (SourceMember member in file.Members)
        {
            if (member.StartLine <= line && line <= member.EndLine)
            {
                ambiguous[indexOf[member]] = true;
            }
        }
    }

    private static string ClassifyNonMember(FileInventory file, int line)
    {
        foreach (NonMemberRange range in file.NonMemberRanges)
        {
            if (range.StartLine <= line && line <= range.EndLine)
            {
                return range.Kind;
            }
        }

        return "unclassified";
    }

    private static void Record(
        Dictionary<(string Path, string Kind), (int Points, int Covered)> unmapped,
        MergedPoint point,
        string kind)
    {
        var key = (point.RelativePath, kind);
        (int points, int covered) = unmapped.GetValueOrDefault(key);
        unmapped[key] = (points + 1, covered + (point.Hits > 0 ? 1 : 0));
    }
}
