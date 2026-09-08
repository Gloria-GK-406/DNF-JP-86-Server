using System.Collections.Immutable;
using System.Text.RegularExpressions;
using CrapMeter;
using CrapMeter.Analysis;
using CrapMeter.Reporting;
using CrapMeter.Runtime;
using Xunit;

namespace CrapMeter.Tests;

public sealed class ReportWriterTests
{
    private static FunctionRecord Record(int complexity, int total, int covered) => new()
    {
        Member = new SourceMember(
            "P", "Server/src/P/F.cs", "N.C", "N.C.Hot", "Hot(int)",
            MemberKind.Method, 10, 40, complexity, HasBody: true),
        State = EvidenceState.Computed,
        SequencePointsTotal = total,
        SequencePointsCovered = covered,
    };

    private static ReportInput Input(string? degraded, params FunctionRecord[] records)
    {
        ImmutableArray<FunctionRecord> all = [.. records];
        var thresholds = new QuadrantThresholds(3, 0.5);
        var join = new JoinResult(all, [], [], [], 0, 0, 0);

        var manifest = new Manifest
        {
            Run = new Manifest.RunInfo
            {
                Timestamp = "2026-01-01_00-00-00",
                StartedAt = "2026-01-01T00:00:00+08:00",
                TimeZone = "T",
                UtcOffset = "08:00:00",
                Status = degraded is null ? "complete" : "failed",
                Command = "dotnet run --project Tool/CrapMeter/CrapMeter.csproj -c Release --",
            },
            Repository = new Manifest.RepositoryInfo
            {
                Root = "/repo", Commit = "abc1234", Branch = "main", Dirty = false, DirtyPaths = [],
            },
        };

        return new ReportInput(
            manifest, all, [], join, thresholds,
            Distributions.Build("whole", "ALL", all, thresholds),
            Distributions.ByScope("project", all, r => r.Member.Project, thresholds),
            Distributions.ByScope("file", all, r => r.Member.RelativePath, thresholds),
            Distributions.ByScope("class", all, r => r.Member.Container, thresholds),
            TopN: 30,
            DegradedReason: degraded);
    }

    private static string WriteAndRead(ReportInput input)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".md");
        try
        {
            ReportWriter.WriteReport(path, input);
            return File.ReadAllText(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_normal_report_leads_with_evidence_then_shape_then_findings_then_options_then_limits()
    {
        string report = WriteAndRead(Input(null, Record(10, 10, 2)));

        int evidence = report.IndexOf("## 1. 证据有效性", StringComparison.Ordinal);
        int shape = report.IndexOf("## 2. 分布形态与风险集中", StringComparison.Ordinal);
        int findings = report.IndexOf("## 3. 发现", StringComparison.Ordinal);
        int options = report.IndexOf("## 4. 改进选项", StringComparison.Ordinal);
        int limits = report.IndexOf("## 5. 局限", StringComparison.Ordinal);

        Assert.True(evidence >= 0 && evidence < shape, "证据有效性必须在最前");
        Assert.True(shape < findings);
        Assert.True(findings < options);
        Assert.True(options < limits);
    }

    [Fact]
    public void Findings_cite_the_measured_pair_and_the_source_location()
    {
        string report = WriteAndRead(Input(null, Record(10, 10, 2)));

        Assert.Contains("`N.C.Hot(int)`", report, StringComparison.Ordinal);
        Assert.Contains("`Server/src/P/F.cs:10`", report, StringComparison.Ordinal);
        // C=10, Cov=0.2 → 100 * 0.512 + 10 = 61.2
        Assert.Contains("61.2", report, StringComparison.Ordinal);
        Assert.Contains("above-warning", report, StringComparison.Ordinal);
    }

    // 取不到可信的方法级覆盖率映射时不得编造 CRAP：报告标题就要说清这不是 CRAP 结果，
    // 而且正文里一个 CRAP 数值都不能出现。
    [Fact]
    public void A_degraded_report_says_so_up_front_and_carries_no_crap_value()
    {
        string report = WriteAndRead(Input(
            "没有任何被测项目对得上覆盖率模块，取不到可信的方法级覆盖率映射。",
            Record(10, 10, 2)));

        Assert.StartsWith("# 采集降级：这不是一份 CRAP 结果", report, StringComparison.Ordinal);
        Assert.DoesNotContain("## 3. 发现", report, StringComparison.Ordinal);
        Assert.DoesNotContain("61.2", report, StringComparison.Ordinal);
        Assert.DoesNotContain("above-warning", report, StringComparison.Ordinal);
        Assert.Empty(Regex.Matches(report, @"\bCRAP\s*[:：=]\s*\d"));
    }

    [Fact]
    public void Counterfactual_scenarios_are_labelled_as_calculations_not_promises()
    {
        string report = WriteAndRead(Input(null, Record(10, 10, 2)));

        Assert.Contains("反事实", report, StringComparison.Ordinal);
        Assert.Contains("不是承诺", report, StringComparison.Ordinal);
        // Cov→0.8：100 * 0.008 + 10 = 10.8
        Assert.Contains("10.8", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Mean_and_sum_are_disclosed_as_scale_sensitive_supplements()
    {
        string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            ReportWriter.WriteDistributions(directory, Input(null, Record(10, 10, 2)));
            string whole = File.ReadAllText(Path.Combine(directory, "distribution-whole.md"));

            Assert.Contains("没有「一个 CRAP 分数」", whole, StringComparison.Ordinal);
            Assert.Contains("对函数个数敏感", whole, StringComparison.Ordinal);
            Assert.Contains("不能替代分布与尾部集中度", whole, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(directory, "distribution-project.md")));
            Assert.True(File.Exists(Path.Combine(directory, "distribution-file.md")));
            Assert.True(File.Exists(Path.Combine(directory, "distribution-class.md")));
            Assert.True(File.Exists(Path.Combine(directory, "distributions.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
