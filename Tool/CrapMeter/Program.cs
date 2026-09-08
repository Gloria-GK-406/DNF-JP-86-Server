using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using CrapMeter;
using CrapMeter.Analysis;
using CrapMeter.Coverage;
using CrapMeter.Inventory;
using CrapMeter.Reporting;
using CrapMeter.Runtime;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var options = Options.Parse(args);
if (options is null)
{
    Options.PrintUsage();
    return 64;
}

string repositoryRoot = options.RepositoryRoot ?? Options.FindRepositoryRoot(Directory.GetCurrentDirectory());
repositoryRoot = Path.GetFullPath(repositoryRoot);
string scanRoot = Path.Combine(repositoryRoot, "Server", "src");
string solution = Path.Combine("Server", "86JP.Server.slnx");

if (!Directory.Exists(scanRoot))
{
    Console.Error.WriteLine($"找不到被测源码根：{scanRoot}");
    return 66;
}

// 前置条件：这两件事任何一件不满足，采集出来的都是另一份口径的东西，不能静默继续。
string? pvfArchive = options.PvfArchive ?? Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH");
if (string.IsNullOrWhiteSpace(pvfArchive) || !File.Exists(pvfArchive))
{
    Console.Error.WriteLine(
        "缺少可用的 PVF 档案：设 PVF_ARCHIVE_PATH 环境变量，或传 --pvf-archive <路径>。\n"
        + "没有它，Infrastructure 的 A_missing_archive_* 两个用例会改走另一条分支，\n"
        + "覆盖率口径与判据基线都不再是同一件事，所以这里直接停。");
    return 78;
}

if (Process.GetProcessesByName("Dfo.Server.Host").Length > 0)
{
    Console.Error.WriteLine(
        "检测到 Dfo.Server.Host 进程在跑。它锁住 Dfo.Server.Host/bin 下的 dll，\n"
        + "构建会以 MSB3027 失败，看起来像判据坏了其实不是。请先停掉它。");
    return 78;
}

DateTimeOffset startedAt = DateTimeOffset.Now;
RunDirectory run = RunDirectory.Create(Path.Combine(repositoryRoot, ".crap"), startedAt);
Console.WriteLine($"运行目录：{run.Path}");

pvfArchive = Path.GetFullPath(pvfArchive);
GitSnapshot? before = null;
string settingsPath = run.File("coverlet.runsettings");
string testResults = Path.Combine(run.RawDirectory, "tests");
string[] buildArguments = ["build", solution, "-c", options.Configuration, "--nologo"];
string[] testArguments =
[
    "test", solution, "-c", options.Configuration, "--no-build", "--nologo",
    "--settings", settingsPath, "--collect:XPlat Code Coverage", "--logger", "trx",
    "--logger", "console;verbosity=detailed", "--results-directory", testResults,
];
string rerun = RerunScript.Create(repositoryRoot, pvfArchive, options.Configuration, options.TopN, options.SimulateFailure);
var manifest = new Manifest
{
    Run = new Manifest.RunInfo
    {
        Timestamp = run.Name,
        StartedAt = startedAt.ToString("O", CultureInfo.InvariantCulture),
        TimeZone = TimeZoneInfo.Local.Id,
        UtcOffset = startedAt.Offset.ToString(),
        Status = "running",
        Command = rerun,
    },
    Repository = new Manifest.RepositoryInfo
    {
        Root = Paths.Normalize(repositoryRoot),
        Commit = "not captured",
        Branch = "not captured",
        Dirty = false,
        DirtyPaths = [],
    },
    Scope = Scope(null),
    Policy = Policy(null),
    Tools = Tools(repositoryRoot),
    Execution = Execution(["dotnet --version", "dotnet " + string.Join(' ', buildArguments.Select(QuoteArgument)),
        "dotnet " + string.Join(' ', testArguments.Select(QuoteArgument))], null),
};

string manifestPath = run.File("manifest.json");
RegisterArtifacts();

int exitCode;
try
{
    File.WriteAllText(run.File("rerun.ps1"), rerun);
    File.WriteAllText(settingsPath, Options.RunSettings);
    manifest.Save(manifestPath);
    exitCode = Collect();
}
catch (Exception exception)
{
    manifest.Run.Status = "failed";
    manifest.Run.StatusReason = exception.GetType().Name + ": " + exception.Message;
    File.AppendAllText(run.File("logs/error.log"), exception + Environment.NewLine);
    Finish();
    Console.Error.WriteLine(exception);
    return 70;
}

return exitCode;

void Finish()
{
    if (before is not null && manifest.After is null)
    {
        try { SaveAfter(); }
        catch (Exception exception)
        {
            File.AppendAllText(run.File("logs/error.log"), exception + Environment.NewLine);
            manifest.Run.Status = "failed";
            manifest.Run.StatusReason += "；无法保存结束快照。";
        }
    }
    DateTimeOffset finishedAt = DateTimeOffset.Now;
    manifest.Run.FinishedAt = finishedAt.ToString("O", CultureInfo.InvariantCulture);
    manifest.Run.DurationSeconds = (finishedAt - startedAt).TotalSeconds;
    manifest.Save(manifestPath);
    Console.WriteLine($"状态：{manifest.Run.Status}　运行目录：{run.Path}");
}

int Collect()
{
    before = GitSnapshot.Read(repositoryRoot);
    manifest.Before = before;
    before.Save(run.File("snapshot-before.json"));
    manifest.Repository = new Manifest.RepositoryInfo
    {
        Root = Paths.Normalize(repositoryRoot), Commit = before.Commit, Branch = before.Branch,
        Dirty = before.Dirty, DirtyPaths = before.DirtyPaths,
    };
    manifest.Save(manifestPath);
    InventoryResult inventory = SourceInventory.Scan(repositoryRoot, scanRoot);
    File.WriteAllText(run.File("raw/source-inventory.json"), System.Text.Json.JsonSerializer.Serialize(inventory,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    manifest.Scope = Scope(inventory);
    if (inventory.Members.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != inventory.Members.Count())
        throw new InvalidDataException("源码清册存在重复函数身份，拒绝静默合并。");
    manifest.Execution!.PvfArchiveSha256 = FileDigest(pvfArchive);
    manifest.Execution!.ExpectedTestProjects = TrxReader.ExpectedProjects(Path.Combine(repositoryRoot, solution));
    manifest.Save(manifestPath);
    ProcessResult sdk = ProcessRunner.Run("dotnet", ["--version"], repositoryRoot, run.File("logs/sdk.log"));
    manifest.Execution.ActualProcesses.Add(sdk);
    manifest.Tools!.DotnetSdk = sdk.ExitCode == 0 ? sdk.StandardOutput : "query failed";
    manifest.Save(manifestPath);
    if (sdk.ExitCode != 0 || string.IsNullOrWhiteSpace(sdk.StandardOutput))
        throw new InvalidOperationException("dotnet --version 未返回实际 SDK 版本。");
    Directory.CreateDirectory(testResults);

    ProcessResult build = ProcessRunner.Run(
        "dotnet",
        buildArguments,
        repositoryRoot,
        Path.Combine(run.LogDirectory, "build.log"));
    manifest.Execution.BuildExitCode = build.ExitCode;
    manifest.Execution.ActualProcesses.Add(build);
    manifest.Save(manifestPath);

    if (build.ExitCode != 0)
    {
        manifest.Run.Status = "failed";
        manifest.Run.StatusReason = "构建失败，未采集覆盖率。见 logs/build.log。";
        Finish();
        return 2;
    }

    ProcessResult test = ProcessRunner.Run(
        "dotnet",
        testArguments,
        repositoryRoot,
        Path.Combine(run.LogDirectory, "test.log"),
        new Dictionary<string, string> { ["PVF_ARCHIVE_PATH"] = pvfArchive });
    manifest.Execution.TestExitCode = test.ExitCode;
    manifest.Execution.ActualProcesses.Add(test);
    manifest.Execution.AllTestsPassed = false;
    manifest.Save(manifestPath);

    List<Manifest.TestOutcome> outcomes = TrxReader.ReadAll(testResults);
    manifest.Execution.Tests.AddRange(outcomes);
    manifest.Save(manifestPath);
    TrxReader.ValidateExpected(outcomes, manifest.Execution.ExpectedTestProjects);
    manifest.Execution.AllTestsPassed = test.ExitCode == 0 && outcomes.All(o => o.Failed == 0);
    manifest.Save(manifestPath);

    if (options.SimulateFailure == "after-tests")
    {
        throw new InvalidOperationException(
            "--simulate-failure after-tests：这是为了验证失败的运行目录仍被保留而人为注入的中断。");
    }

    Console.WriteLine("读取覆盖率报告…");

    // vstest 把同一份覆盖率既留在测试宿主的 GUID 目录里，又拷进 TRX 的附件目录，
    // 所以同一份内容会被找到两次。按内容去重——重复读进来虽然不改覆盖率比值
    // （点按身份合并，命中只看是否 > 0），但会让「几份报告」这个数字撒谎。
    List<string> reports = [.. Directory
        .EnumerateFiles(testResults, "coverage.opencover.xml", SearchOption.AllDirectories)
        .OrderBy(p => p, StringComparer.Ordinal)
        .DistinctBy(FileDigest, StringComparer.Ordinal)];

    if (reports.Count == 0)
    {
        manifest.Run.Status = "failed";
        manifest.Run.StatusReason = "测试跑完但没有产出任何 opencover 报告。";
        Finish();
        return 2;
    }

    for (int i = 0; i < reports.Count; i++)
    {
        string target = Path.Combine(run.RawDirectory, "coverage",
            (i + 1).ToString("00", CultureInfo.InvariantCulture) + ".opencover.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(reports[i], target, overwrite: false);
    }

    MergedCoverage coverage = CoverageMerge.Merge(
        [.. Directory.GetFiles(Path.Combine(run.RawDirectory, "coverage"), "*.xml")
            .Order(StringComparer.Ordinal).Select(p => OpenCoverReader.Read(p, repositoryRoot))]);
    Console.WriteLine(
        $"  {reports.Count} 份报告，{coverage.Modules.Count} 个模块，"
        + $"合并后 {coverage.SequencePoints.Length} 个序列点。");

    SaveAfter();
    if (!before.SameContent(manifest.After!))
        throw new InvalidDataException("采集期间 HEAD 或受测源码/测试/构建配置/工具内容发生变化，拒绝计算 CRAP。");
    if (FileDigest(pvfArchive) != manifest.Execution.PvfArchiveSha256)
        throw new InvalidDataException("采集期间 PVF 档案内容发生变化，拒绝计算 CRAP。");
    if (coverage.ModuleBuildConflicts.Count > 0)
        throw new InvalidDataException("覆盖率模块构建身份冲突，拒绝计算 CRAP："
            + string.Join(", ", coverage.ModuleBuildConflicts));

    JoinResult join = CoverageJoin.Join(inventory, coverage);
    ImmutableArray<FunctionRecord> records = join.Records;

    string? degraded = !records.Any(r => r.State == EvidenceState.Computed)
        ? "没有任何被测项目对得上覆盖率模块，取不到可信的方法级覆盖率映射。"
        : null;

    QuadrantThresholds thresholds = Distributions.ThresholdsFrom(records);
    Distribution whole = Distributions.Build("whole", "ALL", records, thresholds, options.TopN);

    int computed = whole.Computed;
    var reconciliation = new Manifest.ReconciliationInfo
    {
        InventoryMembers = records.Length,
        Computed = computed,
        MeasuredZero = whole.MeasuredZero,
        Missing = whole.Missing,
        Ambiguous = whole.Ambiguous,
        Excluded = whole.Excluded,
        MemberCountsBalance =
            records.Length == computed + whole.Missing + whole.Ambiguous + whole.Excluded,
        SequencePointsMerged = join.SequencePointsTotal,
        SequencePointsAttributed = join.SequencePointsAttributed,
        SequencePointsUnmapped = join.SequencePointsUnmapped,
        SequencePointCountsBalance =
            join.SequencePointsTotal == join.SequencePointsAttributed + join.SequencePointsUnmapped,
        SequencePointsWithoutSourceFile = coverage.SequencePointsWithoutSourceFile,
        ModuleBuildConflicts = [.. coverage.ModuleBuildConflicts],
        ProjectsWithoutCoverageModule = [.. join.ProjectsWithoutModule],
    };

    manifest.Reconciliation = reconciliation;
    manifest.Policy = Policy(thresholds);
    manifest.MeasurementStatus = "partial";
    manifest.MeasurementLimitations.Add("Host 集成测试的子进程命中未进入覆盖成果；Host 成员标 Missing，不计算 CRAP。"
        + "该子进程还执行其他生产模块，因此它们的覆盖率也可能被低估、CRAP 可能偏高；本次仅有测试宿主回收的证据。");
    manifest.MeasurementLimitations.Add("未映射的生成成员单独记账；映射歧义和缺失成员不进入实测分布。"
        + "Run.Status=complete 仅代表声明的采集流程成功，不代表覆盖证据覆盖了全域执行。");

    Console.WriteLine("写产物…");
    DetailWriter.WriteJson(run.File("function-details.json"), records);
    DetailWriter.WriteCsv(run.File("function-details.csv"), records);

    // 状态必须在写报告之前定下来：报告开头就要说这次采集算不算数。
    bool green = manifest.Execution?.AllTestsPassed == true;
    int exit;
    if (degraded is not null)
    {
        manifest.Run.Status = "failed";
        manifest.Run.StatusReason = degraded;
        exit = 2;
    }
    else if (!green || !reconciliation.MemberCountsBalance || !reconciliation.SequencePointCountsBalance)
    {
        manifest.Run.Status = "partial";
        manifest.Run.StatusReason = !green
            ? "测试未全绿，属于诊断运行；漏执行可能低估覆盖率、抬高 CRAP，不能断言每个值均偏高。"
            : "计数对账不成立，分布与明细不一致。";
        exit = 1;
    }
    else
    {
        manifest.Run.Status = "complete";
        exit = 0;
    }

    var input = new ReportInput(
        manifest,
        records,
        join.Unmapped,
        join,
        thresholds,
        whole,
        Distributions.ByScope("project", records, r => r.Member.Project, thresholds, inventory.Projects, options.TopN),
        Distributions.ByScope("file", records, r => r.Member.RelativePath, thresholds, topN: options.TopN),
        Distributions.ByScope("class", records, r => r.Member.Project + "::" + r.Member.Container, thresholds, topN: options.TopN),
        options.TopN,
        degraded);

    ReportWriter.WriteDistributions(run.Path, input);
    ReportWriter.WriteReport(run.File("report.md"), input);

    Finish();
    return exit;
}

static string FileDigest(string path)
{
    using FileStream stream = File.OpenRead(path);
    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
}

void SaveAfter()
{
    manifest.After = GitSnapshot.Read(repositoryRoot);
    manifest.After.Save(run.File("snapshot-after.json"));
    manifest.Save(manifestPath);
}

static string QuoteArgument(string argument) => "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

Manifest.ScopeInfo Scope(InventoryResult? inventory) => new()
{
    ScanRoot = Paths.Relative(repositoryRoot, scanRoot),
    Projects = inventory is null ? [] : [.. inventory.Projects],
    SourceFiles = inventory?.Files.Length ?? 0,
    Excluded = ["Server/tests/** (测试代码本身)", "Tool/** (含本工具自身)", "ClientHack/**", "**/bin/**", "**/obj/**"],
};

Manifest.ExecutionInfo Execution(List<string> commands, List<Manifest.TestOutcome>? outcomes)
{
    var execution = new Manifest.ExecutionInfo
    {
        Solution = Paths.Normalize(solution),
        BuildConfiguration = options.Configuration,
        Commands = commands,
        PvfArchivePath = Paths.Normalize(pvfArchive!),
        TopN = options.TopN,
        SimulateFailure = options.SimulateFailure,
    };

    if (outcomes is not null)
    {
        execution.Tests.AddRange(outcomes);
    }

    return execution;
}

static Manifest.PolicyInfo Policy(QuadrantThresholds? thresholds) => new()
{
    CrapFormula = "CRAP(f) = C(f)^2 * (1 - Cov(f))^3 + C(f)，仅在方法/成员级计算",
    FunctionUniverse =
        "源码中声明的成员：方法、构造函数、终结器、运算符、属性/索引器/事件的访问器、"
        + "自动属性/字段式事件也逐访问器记录，顶层语句形成一个 Program.<Main>；显式接口实现保留限定名。"
        + "声明身份不随换行改变，带 ref/out/in 等参数修饰符。",
    GeneratedMethodRule =
        "编译器生成的方法（lambda、局部函数、async/迭代器状态机、record 合成成员）不是独立单位："
        + "按 (模块, 文件, 行号) 归并回包含它的源码成员，同一行访问器再用 IL 方法名区分，无法裁定则 Ambiguous。落在类型内但不属于任何成员的点"
        + "（字段初始化器、类型声明头）进 unmapped 分区单独记账，不静默丢弃。",
    ComplexityDefinition = CyclomaticComplexity.Definition,
    CoverageBasis =
        "sequence-point (line) coverage: covered = 访问计数 > 0 的序列点数 / 序列点总数。"
        + "分支覆盖并列记录但不参与 CRAP。",
    PercentileMethod = Percentiles.Definition,
    RiskBands = RiskBands.Definition,
    QuadrantThresholds = thresholds?.Definition ?? "high complexity: C > whole-scope P75 of C among computed functions; low coverage: Cov < 0.5",
    // 状态是按**这次采集实际归到该成员的序列点数**判的，不是按成员种类判的。
    // 同一种写法（比如自动属性）会落在不同状态里：coverlet 给编译器生成的
    // get_X 报了一个序列点，它就是 Computed；没报，它才是 Excluded。
    EvidenceStates = new Dictionary<string, string>
    {
        ["Computed"] = "归到该成员的序列点数 > 0，覆盖率与 CRAP 已算（含实测零覆盖）",
        ["measuredZero"] = "Computed 的子集：有可执行单位且一个都没覆盖",
        ["Missing"] = "模块缺报告、有体声明无序列点、模块快照冲突、或 Host 子进程命中未收集。不得按 0 覆盖处理",
        ["Ambiguous"] = "成员行区间互相重叠，多个竞争映射无法安全裁定。不得按 0 覆盖处理",
        ["Excluded"] = "序列点数为 0 且该声明本就没有可执行体（抽象、接口成员、partial 无体声明、"
            + "以及未被报告为可执行的自动属性/自动事件）",
    },
};

static Manifest.ToolInfo Tools(string repositoryRoot) => new()
{
    CrapMeter = typeof(CrapMetric).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown",
    DotnetSdk = "not queried (see logs/sdk.log)",
    CoverletCollector = Options.PackageVersion(
        Path.Combine(repositoryRoot, "Server", "Directory.Packages.props"), "coverlet.collector"),
    Roslyn = typeof(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown",
    CoverageFormat = "opencover (coverlet)",
};

void RegisterArtifacts()
{
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("manifest.json", "采集清单：范围、口径、工具、命令、对账", "json"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("report.md", "人可读报告：证据有效性→分布→发现→选项→局限", "markdown"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("function-details.json", "函数明细（机器可读主格式）", "json"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("function-details.csv", "函数明细（同内容，便于直接看）", "csv"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("distributions.json", "四层分布聚合（机器可读）", "json"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("distribution-whole.md", "全域函数分布", "markdown"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("distribution-project.md", "按项目的函数分布", "markdown"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("distribution-file.md", "按文件的函数分布", "markdown"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("distribution-class.md", "按类的函数分布", "markdown"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("coverlet.runsettings", "本次采集用的覆盖率配置（钉住格式）", "xml"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("logs/build.log", "构建的完整输出", "text"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("logs/test.log", "测试的完整输出", "text"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("raw/tests/", $"原始 TRX 与 vstest 产物", "directory"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("raw/coverage/", "去重的原始 opencover 报告", "directory"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("raw/source-inventory.json", "测试前的源码复杂度清册", "json"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("snapshot-before.json", "采集前 HEAD 与内容 SHA256 清单（含已追踪删除项）", "json"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("snapshot-after.json", "结束时 HEAD 与内容 SHA256 清单", "json"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("rerun.ps1", "完整选项与 PVF 的独立复跑入口", "powershell"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("logs/sdk.log", "dotnet --version 实际 SDK", "text"));
    manifest.Artifacts.Add(new Manifest.ArtifactInfo("logs/error.log", "异常与堆栈（仅异常时产生）", "text"));
}

internal sealed record Options(
    string? RepositoryRoot,
    string? PvfArchive,
    string Configuration,
    int TopN,
    string? SimulateFailure)
{
    public const string DocumentedCommand =
        "dotnet run --project Tool/CrapMeter/CrapMeter.csproj -c Release --";

    public const string RunSettings = """
        <?xml version="1.0" encoding="utf-8"?>
        <!-- CrapMeter 钉住的覆盖率配置。格式必须是 opencover：只有它同时给出
             每个序列点的行号与访问计数、分支点，以及方法自报的 IL 复杂度。
             刻意不设 ExcludeByAttribute：lambda 与 async 状态机必须留在报告里，
             它们的序列点行号是把生成方法归并回源码成员的唯一依据。 -->
        <RunSettings>
          <DataCollectionRunSettings>
            <DataCollectors>
              <DataCollector friendlyName="XPlat code coverage">
                <Configuration>
                  <Format>opencover</Format>
                  <DeterministicReport>false</DeterministicReport>
                  <UseSourceLink>false</UseSourceLink>
                </Configuration>
              </DataCollector>
            </DataCollectors>
          </DataCollectionRunSettings>
        </RunSettings>
        """;

    public static void PrintUsage() => Console.Error.WriteLine(
        """
        CrapMeter — 本仓的 CRAP 变更风险采集器

          dotnet run --project Tool/CrapMeter/CrapMeter.csproj -c Release -- [选项]

        选项：
          --repo <路径>            仓库根，默认从当前目录向上找 .git
          --pvf-archive <路径>     PVF 档案，默认读环境变量 PVF_ARCHIVE_PATH
          --configuration <名>     构建配置，默认 Debug
          --top <n>                报告里列出的热点条数，默认 30
          --simulate-failure after-tests
                                   在测试之后人为中断，用来验证失败的运行目录仍被保留

        退出码：0 = complete，1 = partial，2 = failed，其他 = 前置条件不满足。
        """);

    public static Options? Parse(string[] args)
    {
        string? repo = null;
        string? pvf = null;
        string configuration = "Debug";
        int top = 30;
        string? simulate = null;

        for (int i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (args[i])
            {
                case "--repo": repo = Next(); break;
                case "--pvf-archive": pvf = Next(); break;
                case "--configuration": configuration = Next() ?? configuration; break;
                case "--top":
                    if (int.TryParse(Next(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    {
                        top = parsed;
                    }

                    break;
                case "--simulate-failure": simulate = Next(); break;
                case "--help" or "-h": return null;
                default:
                    Console.Error.WriteLine($"无法识别的参数：{args[i]}");
                    return null;
            }
        }

        return new Options(repo, pvf, configuration, top, simulate);
    }

    public static string FindRepositoryRoot(string start)
    {
        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        return start;
    }

    /// <summary>从中央包管理里读出版本号，而不是在工具里再抄一份——抄的那份会漂。</summary>
    public static string PackageVersion(string propsPath, string package)
    {
        if (!File.Exists(propsPath))
        {
            return "unknown";
        }

        foreach (string line in File.ReadLines(propsPath))
        {
            if (!line.Contains(package, StringComparison.Ordinal))
            {
                continue;
            }

            int start = line.IndexOf("Version=\"", StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            start += "Version=\"".Length;
            int end = line.IndexOf('"', start);
            if (end > start)
            {
                return line[start..end];
            }
        }

        return "unknown";
    }
}
