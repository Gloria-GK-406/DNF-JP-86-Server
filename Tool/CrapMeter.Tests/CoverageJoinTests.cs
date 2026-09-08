using System.Collections.Immutable;
using CrapMeter;
using CrapMeter.Analysis;
using CrapMeter.Coverage;
using CrapMeter.Inventory;
using Xunit;

namespace CrapMeter.Tests;

public sealed class CoverageMergeTests
{
    private static RawPoint Point(string key, int line, long visits) =>
        new("M", key, "Server/src/M/F.cs", @"C:\repo\Server\src\M\F.cs", line, visits);

    private static OpenCoverReport Report(string name, params RawPoint[] points) =>
        new(name, [.. points], [], [], ImmutableSortedSet<string>.Empty);

    [Fact]
    public void The_same_point_seen_twice_sums_its_visits()
    {
        MergedCoverage merged = CoverageMerge.Merge([
            Report("a.xml", Point("Go#sp0", 10, 3)),
            Report("b.xml", Point("Go#sp0", 10, 4)),
        ]);

        MergedPoint point = Assert.Single(merged.SequencePoints);
        Assert.Equal(7, point.Hits);
    }

    // 一个点只要在任意一次运行里被走到就是被覆盖的：0 + 5 不该退化成 0。
    [Fact]
    public void A_point_missed_by_one_run_and_hit_by_another_ends_up_covered()
    {
        MergedCoverage merged = CoverageMerge.Merge([
            Report("a.xml", Point("Go#sp0", 10, 0)),
            Report("b.xml", Point("Go#sp0", 10, 5)),
        ]);

        Assert.Equal(5, Assert.Single(merged.SequencePoints).Hits);
    }

    [Fact]
    public void Distinct_points_are_unioned()
    {
        MergedCoverage merged = CoverageMerge.Merge([
            Report("a.xml", Point("Go#sp0", 10, 1)),
            Report("b.xml", Point("Go#sp1", 11, 1), Point("Other#sp0", 20, 0)),
        ]);

        Assert.Equal(3, merged.SequencePoints.Length);
    }

    // 快照一致性只能按序列点集合判：opencover 的 Module hash 是**插桩后**程序集的 MVID，
    // coverlet 给每个测试宿主各插一份，本来就每份都不同，拿它比会把每次正常采集都误报。
    [Fact]
    public void A_module_whose_point_set_differs_between_reports_is_a_snapshot_conflict()
    {
        MergedCoverage merged = CoverageMerge.Merge([
            Report("a.xml", Point("Go#sp0", 10, 1), Point("Go#sp1", 11, 1)),
            Report("b.xml", Point("Go#sp0", 10, 1), Point("Go#sp1", 12, 1)),
        ]);

        Assert.Equal("M", Assert.Single(merged.ModuleBuildConflicts));
    }

    [Fact]
    public void The_same_module_reported_identically_by_several_runs_is_not_a_conflict()
    {
        MergedCoverage merged = CoverageMerge.Merge([
            Report("a.xml", Point("Go#sp0", 10, 0), Point("Go#sp1", 11, 3)),
            Report("b.xml", Point("Go#sp0", 10, 5), Point("Go#sp1", 11, 0)),
            Report("c.xml", Point("Go#sp0", 10, 1), Point("Go#sp1", 11, 1)),
        ]);

        Assert.Empty(merged.ModuleBuildConflicts);
        Assert.Equal("M", Assert.Single(merged.Modules));
    }

    [Fact]
    public void Points_without_a_source_file_are_counted_rather_than_dropped()
    {
        OpenCoverReport report = Report("a.xml") with
        {
            SequencePoints = [new RawPoint("M", "Go#sp0", null, null, 10, 1)],
        };

        MergedCoverage merged = CoverageMerge.Merge([report]);
        Assert.Empty(merged.SequencePoints);
        Assert.Equal(1, merged.SequencePointsWithoutSourceFile);
    }
}

public sealed class CoverageJoinTests
{
    [Fact]
    public void Conflicting_modules_never_contribute_numeric_crap()
    {
        var coverage = Coverage(new MergedPoint("Sample", Relative, 8, 1))
            with { ModuleBuildConflicts = ImmutableSortedSet.Create("Sample") };
        var result = CoverageJoin.Join(Inventory(), coverage);
        Assert.All(result.Records, r => Assert.Null(r.Crap));
    }

    [Fact]
    public void Host_child_process_gaps_are_missing_instead_of_measured_zero()
    {
        var member = new SourceMember("Dfo.Server.Host", "Server/src/Dfo.Server.Host/Program.cs", "Program",
            "Program.<Main>", "<Main>(string[])", MemberKind.Method, 1, 3, 2, true);
        var inventory = new InventoryResult([new FileInventory(member.Project, member.RelativePath, [member], [])], [member.Project]);
        var coverage = new MergedCoverage([new MergedPoint(member.Project, member.RelativePath, 2, 0)], [], [],
            ImmutableSortedSet.Create(member.Project), [], [], 0, 0, []);
        var record = Assert.Single(CoverageJoin.Join(inventory, coverage).Records);
        Assert.Equal(EvidenceState.Missing, record.State);
        Assert.False(record.IsMeasuredZero);
        Assert.Null(record.Crap);
    }

    [Fact]
    public void Same_line_accessors_use_method_identity_or_remain_ambiguous()
    {
        var file = SourceInventory.ScanFile(Root, "Sample", Path.Combine(Root, Relative),
            "class C { int P { get => 1; set {} } }");
        var inventory = new InventoryResult([file], ["Sample"]);
        var identified = CoverageJoin.Join(inventory, Coverage(
            new MergedPoint("Sample", Relative, 1, 1, "System.Int32 C::get_P()#sp0"),
            new MergedPoint("Sample", Relative, 1, 0, "System.Void C::set_P(System.Int32)#sp0")));
        Assert.Equal(2, identified.Records.Count(r => r.State == EvidenceState.Computed));
        Assert.Equal(1, identified.Records.Count(r => r.IsMeasuredZero));
        var unknown = CoverageJoin.Join(inventory, Coverage(new MergedPoint("Sample", Relative, 1, 0)));
        Assert.All(unknown.Records, r => Assert.Equal(EvidenceState.Ambiguous, r.State));
    }

    private const string Root = @"C:\repo";
    private const string Relative = "Server/src/Sample/Sample.cs";

    private const string Source = """
        namespace N;
        public class C
        {
            private readonly int _seed = 1;

            public int Covered(int a)
            {
                if (a > 0) { return 1; }
                return 0;
            }

            public int NeverRun(int a)
            {
                if (a > 0) { return 1; }
                return 0;
            }

            public abstract class Contract
            {
                public abstract int Go();
            }
        }
        """;

    private static InventoryResult Inventory() =>
        new(
            [SourceInventory.ScanFile(Root, "Sample", Path.Combine(Root, "Server", "src", "Sample", "Sample.cs"), Source)],
            ["Sample"]);

    private static MergedCoverage Coverage(params MergedPoint[] points) =>
        new([.. points], [], [], ["Sample"], [], [], 0, 0, ["synthetic"]);

    private static FunctionRecord Find(JoinResult result, string symbol) =>
        Assert.Single(result.Records.Where(r => r.Member.QualifiedSymbol == symbol));

    [Fact]
    public void Points_are_attributed_to_the_member_whose_range_contains_them()
    {
        JoinResult result = CoverageJoin.Join(Inventory(), Coverage(
            new MergedPoint("Sample", Relative, 8, 5),
            new MergedPoint("Sample", Relative, 9, 0)));

        FunctionRecord covered = Find(result, "N.C.Covered");
        Assert.Equal(EvidenceState.Computed, covered.State);
        Assert.Equal(2, covered.SequencePointsTotal);
        Assert.Equal(1, covered.SequencePointsCovered);
        Assert.Equal(0.5, covered.Coverage);
        Assert.Equal(CrapMetric.Compute(2, 0.5), covered.Crap);
    }

    // 「测到了、一行没覆盖」与「没测到」对决策的含义相反，必须分开。
    [Fact]
    public void A_member_with_points_but_no_hits_is_measured_zero_not_missing()
    {
        JoinResult result = CoverageJoin.Join(Inventory(), Coverage(
            new MergedPoint("Sample", Relative, 14, 0),
            new MergedPoint("Sample", Relative, 15, 0)));

        FunctionRecord record = Find(result, "N.C.NeverRun");
        Assert.Equal(EvidenceState.Computed, record.State);
        Assert.True(record.IsMeasuredZero);
        Assert.Equal(0.0, record.Coverage);
        Assert.Equal(CrapMetric.Compute(2, 0.0), record.Crap);
    }

    [Fact]
    public void A_member_the_report_never_mentions_is_missing_and_has_no_crap()
    {
        JoinResult result = CoverageJoin.Join(Inventory(), Coverage(
            new MergedPoint("Sample", Relative, 8, 1)));

        FunctionRecord record = Find(result, "N.C.NeverRun");
        Assert.Equal(EvidenceState.Missing, record.State);
        Assert.Equal("no-coverage-rows-for-member", record.Reason);
        Assert.Null(record.Crap);
        Assert.Null(record.Coverage);
    }

    [Fact]
    public void A_project_absent_from_coverage_is_missing_rather_than_zero_covered()
    {
        JoinResult result = CoverageJoin.Join(Inventory(), new MergedCoverage(
            [], [], [], ["SomethingElse"], [], [], 0, 0, ["synthetic"]));

        Assert.Equal("Sample", Assert.Single(result.ProjectsWithoutModule));
        Assert.All(
            result.Records,
            r =>
            {
                Assert.Equal(EvidenceState.Missing, r.State);
                Assert.Equal("module-not-instrumented", r.Reason);
                Assert.Null(r.Crap);
            });
    }

    [Fact]
    public void Bodyless_declarations_are_excluded_not_counted_as_uncovered()
    {
        JoinResult result = CoverageJoin.Join(Inventory(), Coverage(
            new MergedPoint("Sample", Relative, 8, 1)));

        FunctionRecord record = Find(result, "N.C.Contract.Go");
        Assert.Equal(EvidenceState.Excluded, record.State);
        Assert.Equal("no-executable-units", record.Reason);
    }

    [Fact]
    public void A_field_initializer_point_is_recorded_as_unmapped_not_forced_onto_a_member()
    {
        JoinResult result = CoverageJoin.Join(Inventory(), Coverage(
            new MergedPoint("Sample", Relative, 4, 7)));

        UnmappedPoints unmapped = Assert.Single(result.Unmapped);
        Assert.Equal("field-initializer", unmapped.Kind);
        Assert.Equal(1, unmapped.Points);
        Assert.Equal(1, unmapped.CoveredPoints);
    }

    [Fact]
    public void A_point_in_a_file_outside_the_measured_scope_is_recorded_as_out_of_scope()
    {
        JoinResult result = CoverageJoin.Join(Inventory(), Coverage(
            new MergedPoint("Sample", "Server/tests/Whatever.cs", 3, 1)));

        Assert.Equal("file-not-in-scope", Assert.Single(result.Unmapped).Kind);
    }

    // 对账等式是防「静默丢行」的那道闸：任何一个成员都必须恰好落在一个状态里，
    // 每个序列点要么归到成员、要么进 unmapped。
    [Fact]
    public void Counts_reconcile_across_states_and_points()
    {
        JoinResult result = CoverageJoin.Join(Inventory(), Coverage(
            new MergedPoint("Sample", Relative, 4, 1),
            new MergedPoint("Sample", Relative, 8, 1),
            new MergedPoint("Sample", Relative, 14, 0),
            new MergedPoint("Sample", "Server/tests/Whatever.cs", 3, 1)));

        int computed = result.Records.Count(r => r.State == EvidenceState.Computed);
        int missing = result.Records.Count(r => r.State == EvidenceState.Missing);
        int ambiguous = result.Records.Count(r => r.State == EvidenceState.Ambiguous);
        int excluded = result.Records.Count(r => r.State == EvidenceState.Excluded);

        Assert.Equal(result.Records.Length, computed + missing + ambiguous + excluded);
        Assert.Equal(
            result.SequencePointsTotal,
            result.SequencePointsAttributed + result.SequencePointsUnmapped);
    }
}
