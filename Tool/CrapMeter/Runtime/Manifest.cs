using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrapMeter.Runtime;

/// <summary>
/// 采集清单。别的 Agent 或维护者只读这一份，就该能复现本次的范围、口径与命令，
/// 并认出哪些函数即使没被覆盖也必须出现在结果里。
///
/// 状态在采集开始前就写成 <c>running</c>，结束时改成 <c>complete</c> /
/// <c>partial</c> / <c>failed</c>——中断或失败的运行必须能和成功的区分开。
/// </summary>
public sealed class Manifest
{
    public string Schema { get; init; } = "crapmeter-manifest/1";

    public required RunInfo Run { get; set; }

    public required RepositoryInfo Repository { get; set; }

    public GitSnapshot? Before { get; set; }

    public GitSnapshot? After { get; set; }

    public ScopeInfo? Scope { get; set; }

    public PolicyInfo? Policy { get; set; }

    public ToolInfo? Tools { get; set; }

    public ExecutionInfo? Execution { get; set; }

    public ReconciliationInfo? Reconciliation { get; set; }

    public List<ArtifactInfo> Artifacts { get; } = [];

    public List<string> Notes { get; } = [];

    public string MeasurementStatus { get; set; } = "pending";

    public List<string> MeasurementLimitations { get; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public void Save(string path)
    {
        foreach (ArtifactInfo artifact in Artifacts)
        {
            string fullPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, artifact.Path);
            artifact.Exists = artifact.Path == "manifest.json" || File.Exists(fullPath) || Directory.Exists(fullPath);
        }
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options));
        File.Move(temporary, path, overwrite: true);
    }

    public sealed class RunInfo
    {
        public required string Timestamp { get; init; }

        /// <summary>规范的 ISO 8601 瞬时，带偏移量。</summary>
        public required string StartedAt { get; init; }

        public required string TimeZone { get; init; }

        public required string UtcOffset { get; init; }

        /// <summary>running | complete | partial | failed</summary>
        public required string Status { get; set; }

        public string? StatusReason { get; set; }

        public string? FinishedAt { get; set; }

        public double? DurationSeconds { get; set; }

        public required string Command { get; init; }
    }

    public sealed class RepositoryInfo
    {
        public required string Root { get; init; }

        public required string Commit { get; init; }

        public required string Branch { get; init; }

        public required bool Dirty { get; init; }

        public required List<string> DirtyPaths { get; init; }
    }

    public sealed class ScopeInfo
    {
        public required string ScanRoot { get; init; }

        public required List<string> Projects { get; init; }

        public required List<string> Excluded { get; init; }

        public required int SourceFiles { get; init; }
    }

    public sealed class PolicyInfo
    {
        public required string CrapFormula { get; init; }

        public required string FunctionUniverse { get; init; }

        public required string GeneratedMethodRule { get; init; }

        public required string ComplexityDefinition { get; init; }

        public required string CoverageBasis { get; init; }

        public required string PercentileMethod { get; init; }

        public required string RiskBands { get; init; }

        public required string QuadrantThresholds { get; init; }

        public required Dictionary<string, string> EvidenceStates { get; init; }
    }

    public sealed class ToolInfo
    {
        public required string CrapMeter { get; init; }

        public required string DotnetSdk { get; set; }

        public required string CoverletCollector { get; init; }

        public required string Roslyn { get; init; }

        public required string CoverageFormat { get; init; }
    }

    public sealed class ExecutionInfo
    {
        public required string Solution { get; init; }

        public required string BuildConfiguration { get; init; }

        public required List<string> Commands { get; init; }

        public string? PvfArchivePath { get; set; }
        public string? PvfArchiveSha256 { get; set; }

        public List<TestOutcome> Tests { get; } = [];

        public bool? AllTestsPassed { get; set; }

        public List<string> ExpectedTestProjects { get; set; } = [];

        public int? BuildExitCode { get; set; }

        public int? TestExitCode { get; set; }

        public List<ProcessResult> ActualProcesses { get; } = [];

        public int TopN { get; set; }

        public string? SimulateFailure { get; set; }
    }

    public sealed record TestOutcome(
        string TestProject,
        int Total,
        int Passed,
        int Failed,
        int Skipped,
        string TrxFile);

    public sealed class ReconciliationInfo
    {
        public required int InventoryMembers { get; init; }

        public required int Computed { get; init; }

        public required int MeasuredZero { get; init; }

        public required int Missing { get; init; }

        public required int Ambiguous { get; init; }

        public required int Excluded { get; init; }

        public required bool MemberCountsBalance { get; init; }

        public required int SequencePointsMerged { get; init; }

        public required int SequencePointsAttributed { get; init; }

        public required int SequencePointsUnmapped { get; init; }

        public required bool SequencePointCountsBalance { get; init; }

        public required int SequencePointsWithoutSourceFile { get; init; }

        public required List<string> ModuleBuildConflicts { get; init; }

        public required List<string> ProjectsWithoutCoverageModule { get; init; }
    }

    public sealed record ArtifactInfo(string Path, string Role, string Format)
    {
        public bool Exists { get; set; }
    }
}
