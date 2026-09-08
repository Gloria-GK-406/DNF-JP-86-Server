using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CrapMeter.Analysis;
using CrapMeter.Runtime;

namespace CrapMeter.Reporting;

public sealed record ReportInput(
    Manifest Manifest,
    ImmutableArray<FunctionRecord> Records,
    ImmutableArray<UnmappedPoints> Unmapped,
    JoinResult Join,
    QuadrantThresholds Thresholds,
    Distribution Whole,
    ImmutableArray<Distribution> ByProject,
    ImmutableArray<Distribution> ByFile,
    ImmutableArray<Distribution> ByClass,
    int TopN,
    string? DegradedReason);

public static class ReportWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void WriteDistributions(
        string directory, ReportInput input)
    {
        Write(Path.Combine(directory, "distribution-whole.md"), "全域", [input.Whole], input);
        Write(Path.Combine(directory, "distribution-project.md"), "项目", input.ByProject, input);
        Write(Path.Combine(directory, "distribution-file.md"), "文件", input.ByFile, input);
        Write(Path.Combine(directory, "distribution-class.md"), "类", input.ByClass, input);

        var all = new
        {
            schema = "crapmeter-distributions/1",
            percentileMethod = Percentiles.Definition,
            riskBands = RiskBands.Definition,
            quadrantThresholds = input.Thresholds.Definition,
            whole = input.Whole,
            byProject = input.ByProject,
            byFile = input.ByFile,
            byClass = input.ByClass,
        };

        File.WriteAllText(
            Path.Combine(directory, "distributions.json"),
            JsonSerializer.Serialize(all, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
    }

    private static void Write(
        string path, string label, IReadOnlyList<Distribution> rows, ReportInput input)
    {
        var text = new StringBuilder();
        text.AppendLine(Inv, $"# CRAP 函数分布 — 按{label}");
        text.AppendLine();
        text.AppendLine(Inv, $"运行：`{input.Manifest.Run.Timestamp}`　快照：`{input.Manifest.Repository.Commit}`");
        text.AppendLine();
        text.AppendLine("**这一层没有「一个 CRAP 分数」**——只有函数级 CRAP 的分布。");
        text.AppendLine(Inv, $"分位数口径：{Percentiles.Definition}");
        text.AppendLine();
        text.AppendLine(Inv, $"风险分档：{RiskBands.Definition}");
        text.AppendLine();
        text.AppendLine(Inv, $"四象限阈值：{input.Thresholds.Definition}");
        text.AppendLine();
        text.AppendLine(
            "| 范围 | 在范围内 | 已算 | 实测零覆盖 | 缺失 | 歧义 | 排除 | " +
            "min | 中位 | P75 | P90 | P95 | max | >30 | >30 占比 | " +
            "高C低Cov | 高C高Cov | 低C低Cov | 低C高Cov | 函数分数均值 | 函数分数总和 |");
        text.AppendLine(
            "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (Distribution row in rows)
        {
            text.Append(Inv, $"| `{row.Key}` | {row.InScope} | {row.Computed} | {row.MeasuredZero} ");
            text.Append(Inv, $"| {row.Missing} | {row.Ambiguous} | {row.Excluded} ");
            text.Append(Inv, $"| {N(row.Min)} | {N(row.Median)} | {N(row.P75)} | {N(row.P90)} | {N(row.P95)} | {N(row.Max)} ");
            text.Append(Inv, $"| {row.AboveWarning} | {P(row.AboveWarningShare)} ");
            text.Append(Inv, $"| {row.HighComplexityLowCoverage} | {row.HighComplexityHighCoverage} ");
            text.Append(Inv, $"| {row.LowComplexityLowCoverage} | {row.LowComplexityHighCoverage} ");
            text.AppendLine(Inv, $"| {N(row.FunctionScoreMean)} | {N(row.FunctionScoreSum)} |");
        }

        text.AppendLine();
        text.AppendLine(
            "「函数分数均值」与「函数分数总和」是**函数级 CRAP 的均值与总和**，对函数个数敏感："
            + "函数越多的范围总和天然越大，拆分一个大函数会同时改变两者。它们只作补充，"
            + "不能替代分布与尾部集中度，更不是这一层的 CRAP 分数。");
        text.AppendLine();
        text.AppendLine("各范围的 Top N（含函数 ID、签名、源码位置）见 distributions.json 的 TopFunctions；分档占比分母为该范围已算函数数。");
        text.AppendLine();
        text.AppendLine("| 范围 | low 数/占比 | moderate 数/占比 | elevated 数/占比 | >30 数/占比 | 可测函数占比（已算/非排除） |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (Distribution row in rows)
            text.AppendLine(Inv, $"| `{row.Key}` | {row.Low} / {P(row.LowShare)} | {row.Moderate} / {P(row.ModerateShare)} | {row.Elevated} / {P(row.ElevatedShare)} | {row.AboveWarning} / {P(row.AboveWarningShare)} | {P(row.MeasuredShare)} |");

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
    }

    public static void WriteReport(string path, ReportInput input)
    {
        var text = new StringBuilder();
        Manifest manifest = input.Manifest;

        if (input.DegradedReason is { } degraded)
        {
            text.AppendLine("# 采集降级：这不是一份 CRAP 结果");
            text.AppendLine();
            text.AppendLine(Inv, $"**{degraded}**");
            text.AppendLine();
            text.AppendLine(
                "取不到可信的方法级覆盖率映射时，本工具不输出任何 CRAP 数值——"
                + "静态复杂度单独存在不构成变更风险评估。下面只列已取到的证据与失败点。");
            text.AppendLine();
            AppendEvidence(text, input);
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
            return;
        }

        text.AppendLine("# CRAP 变更风险报告");
        text.AppendLine();
        text.AppendLine(Inv, $"运行 `{manifest.Run.Timestamp}`　状态 **{manifest.Run.Status}**");
        text.AppendLine(Inv, $"快照 `{manifest.Repository.Commit}`（分支 `{manifest.Repository.Branch}`，工作区{(manifest.Repository.Dirty ? "**有未提交改动**" : "干净")}）");
        text.AppendLine();
        text.AppendLine("> CRAP 是变更风险信号。它不证明缺陷、不证明断言强弱、不证明设计好坏、");
        text.AppendLine("> 不证明漏洞、不证明性能问题，也不代表业务重要性。");
        text.AppendLine();

        AppendEvidence(text, input);
        AppendShape(text, input);
        AppendFindings(text, input);
        SourceFindings.Append(text, input);
        AppendOptions(text, input);
        AppendLimitations(text, input);

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
    }

    private static void AppendEvidence(StringBuilder text, ReportInput input)
    {
        Manifest manifest = input.Manifest;
        Manifest.ReconciliationInfo? rec = manifest.Reconciliation;

        text.AppendLine("## 1. 证据有效性");
        text.AppendLine();

        if (manifest.Execution is { } execution)
        {
            bool green = execution.AllTestsPassed == true;
            text.AppendLine(green
                ? "**测试执行全绿**；覆盖证据的完备性单独列于下方，不能由测试通过推定。"
                : "**测试不是全绿**——下面的覆盖率来自一次不完整或有失败的执行，"
                  + "漏执行可能低估覆盖率并抬高 CRAP，但不能断言每个值均偏高。这是诊断运行。");
            text.AppendLine();
            text.AppendLine("| 测试项目 | 总计 | 通过 | 失败 | 跳过 |");
            text.AppendLine("|---|---:|---:|---:|---:|");
            foreach (Manifest.TestOutcome outcome in execution.Tests)
            {
                text.AppendLine(Inv, $"| {outcome.TestProject} | {outcome.Total} | {outcome.Passed} | {outcome.Failed} | {outcome.Skipped} |");
            }

            text.AppendLine(Inv, $"| **合计** | **{execution.Tests.Sum(t => t.Total)}** | **{execution.Tests.Sum(t => t.Passed)}** | **{execution.Tests.Sum(t => t.Failed)}** | **{execution.Tests.Sum(t => t.Skipped)}** |");
            text.AppendLine();
        }

        text.AppendLine(Inv, $"**覆盖证据状态：{manifest.MeasurementStatus}**");
        foreach (string limitation in manifest.MeasurementLimitations) text.AppendLine("- " + limitation);
        text.AppendLine();
        if (manifest.Run.StatusReason is { } reason) text.AppendLine("采集说明：" + reason);

        if (rec is not null)
        {
            text.AppendLine("### 函数计数对账");
            text.AppendLine();
            text.AppendLine(Inv,
                $"清册 {rec.InventoryMembers} = 已算 {rec.Computed} + 缺失 {rec.Missing} + 歧义 {rec.Ambiguous} + 排除 {rec.Excluded}"
                + $"　→ **{(rec.MemberCountsBalance ? "成立" : "不成立")}**");
            text.AppendLine();
            text.AppendLine(Inv,
                $"其中「实测零覆盖」{rec.MeasuredZero} 个，计入「已算」——报告证明这些成员有可执行单位且一个都没被覆盖，"
                + $"与「缺失」（对不上覆盖率证据）是两回事，不得混为一谈。");
            text.AppendLine();
            text.AppendLine("### 序列点对账");
            text.AppendLine();
            text.AppendLine(Inv,
                $"合并后序列点 {rec.SequencePointsMerged} = 归到成员 {rec.SequencePointsAttributed} + 归并不掉 {rec.SequencePointsUnmapped}"
                + $"　→ **{(rec.SequencePointCountsBalance ? "成立" : "不成立")}**");
            text.AppendLine();

            if (rec.SequencePointsWithoutSourceFile > 0)
            {
                text.AppendLine(Inv,
                    $"另有 {rec.SequencePointsWithoutSourceFile} 个点在覆盖率报告里没有可用源码文件引用，未进入对接。");
                text.AppendLine();
            }

            if (rec.ModuleBuildConflicts.Count > 0)
            {
                text.AppendLine(Inv,
                    $"**以下模块的序列点集合存在冲突，不能建立同快照证据：**"
                    + $" {string.Join(", ", rec.ModuleBuildConflicts)}");
                text.AppendLine();
            }

            if (rec.ProjectsWithoutCoverageModule.Count > 0)
            {
                text.AppendLine(Inv,
                    $"以下项目在覆盖率里没有对应模块，其全部成员记为 `Missing / module-not-instrumented`（**不是零覆盖**）："
                    + $" {string.Join(", ", rec.ProjectsWithoutCoverageModule)}");
                text.AppendLine();
            }
        }

        if (input.Unmapped.Length > 0)
        {
            text.AppendLine("### 归并不掉的序列点");
            text.AppendLine();
            text.AppendLine(
                "这些点落在源码文件里，但不属于任何源码声明的成员——多为字段初始化器、"
                + "record 合成成员、主构造函数。它们被单独记账，不计入任何函数的覆盖率。");
            text.AppendLine();
            text.AppendLine("| 成因 | 点数 | 其中已覆盖 |");
            text.AppendLine("|---|---:|---:|");
            foreach (var group in input.Unmapped
                .GroupBy(u => u.Kind, StringComparer.Ordinal)
                .OrderByDescending(g => g.Sum(u => u.Points)))
            {
                text.AppendLine(Inv, $"| `{group.Key}` | {group.Sum(u => u.Points)} | {group.Sum(u => u.CoveredPoints)} |");
            }

            text.AppendLine();
        }
    }

    private static void AppendShape(StringBuilder text, ReportInput input)
    {
        Distribution whole = input.Whole;
        text.AppendLine("## 2. 分布形态与风险集中");
        text.AppendLine();
        text.AppendLine(Inv,
            $"在范围内 {whole.InScope} 个成员，其中 {whole.Computed} 个算出了 CRAP"
            + $"（含实测零覆盖 {whole.MeasuredZero} 个），缺失 {whole.Missing}，歧义 {whole.Ambiguous}，排除 {whole.Excluded}。");
        text.AppendLine();
        text.AppendLine(Inv,
            $"已算集合的 CRAP：min {N(whole.Min)}，中位 {N(whole.Median)}，P75 {N(whole.P75)}，"
            + $"P90 {N(whole.P90)}，P95 {N(whole.P95)}，max {N(whole.Max)}。");
        text.AppendLine();
        text.AppendLine(Inv,
            $"越过警戒线（CRAP > 30）的有 **{whole.AboveWarning}** 个，占已算集合的 **{P(whole.AboveWarningShare)}**。");
        text.AppendLine();
        text.AppendLine(Inv, $"四象限（{input.Thresholds.Definition}）：");
        text.AppendLine();
        text.AppendLine("| | 低覆盖 | 高覆盖 |");
        text.AppendLine("|---|---:|---:|");
        text.AppendLine(Inv, $"| **高复杂度** | {whole.HighComplexityLowCoverage} | {whole.HighComplexityHighCoverage} |");
        text.AppendLine(Inv, $"| **低复杂度** | {whole.LowComplexityLowCoverage} | {whole.LowComplexityHighCoverage} |");
        text.AppendLine();

        text.AppendLine("### 按项目");
        text.AppendLine();
        text.AppendLine("| 项目 | 在范围内 | 已算 | 缺失 | 中位 | P90 | max | >30 | >30 占比 |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (Distribution row in input.ByProject)
        {
            text.AppendLine(Inv,
                $"| `{row.Key}` | {row.InScope} | {row.Computed} | {row.Missing} | {N(row.Median)} | {N(row.P90)} | {N(row.Max)} | {row.AboveWarning} | {P(row.AboveWarningShare)} |");
        }

        text.AppendLine();
        text.AppendLine("### 尾部最集中的类（按越线函数数）");
        text.AppendLine();
        text.AppendLine("| 类 | 已算 | >30 | P90 | max |");
        text.AppendLine("|---|---:|---:|---:|---:|");
        foreach (Distribution row in input.ByClass.Where(d => d.AboveWarning > 0).Take(15))
        {
            text.AppendLine(Inv, $"| `{row.Key}` | {row.Computed} | {row.AboveWarning} | {N(row.P90)} | {N(row.Max)} |");
        }

        text.AppendLine();
    }

    private static void AppendFindings(StringBuilder text, ReportInput input)
    {
        text.AppendLine(Inv, $"## 3. 发现（按 CRAP 排序，Top {input.TopN}）");
        text.AppendLine();
        text.AppendLine(
            "下表每一格都是**实测**：复杂度来自源码语法树，覆盖率来自本次测试执行，"
            + "CRAP 由二者按公式算出，分位取自本次已算集合。"
            + "「分类」是按阈值的**机械判定**，不是对代码的评价。");
        text.AppendLine();

        List<FunctionRecord> computed = [.. input.Records
            .Where(r => r.State == EvidenceState.Computed)
            .OrderByDescending(r => r.Crap!.Value)];
        List<double> sorted = [.. computed.Select(r => r.Crap!.Value).Order()];

        text.AppendLine("| # | 方法 | 位置 | C | Cov | 分支Cov | CRAP | 分位 | 分档 | 分类 |");
        text.AppendLine("|---:|---|---|---:|---:|---:|---:|---:|---|---|");

        int rank = 0;
        foreach (FunctionRecord record in computed.Take(input.TopN))
        {
            rank++;
            double crap = record.Crap!.Value;
            text.AppendLine(Inv,
                $"| {rank} | `{record.Member.QualifiedSymbol}{ParametersOf(record)}` "
                + $"| `{record.Member.RelativePath}:{record.Member.StartLine}` "
                + $"| {record.Member.Complexity} | {P(record.Coverage)} | {P(record.BranchCoverage)} "
                + $"| {N(crap)} | {P(PercentileOf(sorted, crap))} | {RiskBands.Of(crap)} "
                + $"| {Classify(record, input.Thresholds)} |");
        }

        text.AppendLine();
        text.AppendLine(
            "**CRAP 不能确立的事**：这些方法是否真的出过缺陷、断言是否够强、"
            + "业务上是否关键、变更频度多高、爆炸半径多大——这些要人去看源码、看历史、看调用方，"
            + "本报告不作推断。");
        text.AppendLine();

        var evidenceGaps = input.Records
            .Where(r => r.State is EvidenceState.Missing or EvidenceState.Ambiguous)
            .GroupBy(r => (r.Member.Project, r.Reason))
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToList();

        if (evidenceGaps.Count > 0)
        {
            text.AppendLine("### 证据缺口");
            text.AppendLine();
            text.AppendLine("| 项目 | 成因 | 成员数 | 其中复杂度 ≥ 5 |");
            text.AppendLine("|---|---|---:|---:|");
            foreach (var group in evidenceGaps)
            {
                text.AppendLine(Inv,
                    $"| `{group.Key.Project}` | `{group.Key.Reason}` | {group.Count()} | {group.Count(r => r.Member.Complexity >= 5)} |");
            }

            text.AppendLine();
            text.AppendLine(
                "**这些成员没有 CRAP 值，也没有被当成 0 覆盖。** 把它们按 0 算会得到一个"
                + "系统性偏高的分布——如果需要那个悲观情景，要单独标注，不能混进实测分布。");
            text.AppendLine();
        }
    }

    private static void AppendOptions(StringBuilder text, ReportInput input)
    {
        text.AppendLine("## 4. 改进选项");
        text.AppendLine();
        text.AppendLine(
            "每条选项绑定到上面观察到的风险来源。反事实一列是**计算**，不是承诺："
            + "它只说「若覆盖率到某个值，公式会给出什么」，不预测补测的实际成本或效果。");
        text.AppendLine();

        List<FunctionRecord> computed = [.. input.Records
            .Where(r => r.State == EvidenceState.Computed)
            .OrderByDescending(r => r.Crap!.Value)
            .Take(input.TopN)];

        // 第二个反事实用的是四象限的复杂度切分线（全域已算集合的 P75），不是中位数——
        // 表头必须照实写出它是哪个基准，否则「降到中位数就能到 12」是句错话。
        int line = input.Thresholds.Complexity;
        text.AppendLine(Inv,
            $"| 方法 | 风险来源 | 选项 | 反事实：Cov→0.8 | 反事实：C→P75({line}) |");
        text.AppendLine("|---|---|---|---:|---:|");

        foreach (FunctionRecord record in computed)
        {
            string source = Classify(record, input.Thresholds);
            string option = source switch
            {
                "combined" => "combined：先补特征化测试锁住现有行为，再小步拆分条件与嵌套，每接受一小步就重跑本采集，确认风险没有只是被搬进新函数",
                "coverage-dominant" => "test-first：补分支、边界与失败路径的覆盖，结构先不动",
                "complexity-dominant" => "先核实断言与行为保护是否充分；保护成立后再考虑简化条件、降低嵌套或分离副作用，覆盖率本身不授权直接重构",
                _ => "defer：复杂度与覆盖率都不是这一条的风险来源，它能排进前列只说明本仓尾部很短",
            };

            text.AppendLine(Inv,
                $"| `{record.Member.QualifiedSymbol}` | {source} | {option} "
                + $"| {N(CrapMetric.Compute(record.Member.Complexity, 0.8))} "
                + $"| {N(CrapMetric.Compute(line, record.Coverage!.Value))} |");
        }

        text.AppendLine();

        int missing = input.Whole.Missing + input.Whole.Ambiguous;
        if (missing > 0)
        {
            text.AppendLine(Inv,
                $"**evidence repair**：还有 {missing} 个成员没有可信的覆盖率证据。"
                + $"在按分数排序去动代码之前，先修这部分——否则排序本身就是错的。");
            text.AppendLine();
        }

        text.AppendLine(
            "**defer**：稳定、少改、影响面小的热点可以先不动。"
            + "但这个判断需要变更频度与业务关键性，本工具不掌握，必须由人补上并写明理由。");
        text.AppendLine();
    }

    private static void AppendLimitations(StringBuilder text, ReportInput input)
    {
        text.AppendLine("## 5. 局限");
        text.AppendLine();
        text.AppendLine("- CRAP 只在方法级存在。上面所有类/文件/项目/全域的行只是**分布**，不是那一层的分数。");
        text.AppendLine("- 覆盖率口径是序列点（行）覆盖；分支覆盖只并列展示，不参与 CRAP 计算。");
        text.AppendLine("- 复杂度是源码级圈复杂度（Roslyn 语法），与 opencover 自报的 IL 复杂度定义不同，明细里两者并列可对照。");
        text.AppendLine("- 覆盖率证明代码被执行过，**不证明有断言在检查它**。高覆盖不等于有保护。");
        text.AppendLine("- 跨次比较前必须先比清单：范围、函数宇宙政策、复杂度定义、覆盖率口径、测试集合、排除规则、构建配置、工具版本，任何一项不同都会让分数不可比。");

        if (input.Manifest.Execution?.AllTestsPassed != true)
        {
            text.AppendLine("- **本次测试不是全绿，这是诊断运行。** 漏执行可能低估覆盖率、抬高 CRAP，不能断言全部分数均偏高。");
        }

        if (input.Manifest.Repository.Dirty)
        {
            text.AppendLine("- **采集时工作区有未提交改动**，以 snapshot-before.json / snapshot-after.json 的逐文件 SHA256 和提交号识别快照。");
        }

        text.AppendLine();
    }

    private static string ParametersOf(FunctionRecord record)
    {
        string signature = record.Member.Signature;
        int paren = signature.IndexOf('(');
        return paren < 0 ? string.Empty : signature[paren..];
    }

    private static string Classify(FunctionRecord record, QuadrantThresholds thresholds)
    {
        bool highComplexity = record.Member.Complexity > thresholds.Complexity;
        bool lowCoverage = record.Coverage < thresholds.Coverage;
        return (highComplexity, lowCoverage) switch
        {
            (true, true) => "combined",
            (false, true) => "coverage-dominant",
            (true, false) => "complexity-dominant",
            // 低复杂度 + 高覆盖也可能排进 Top N——那只说明本仓尾部短，不是它有风险。
            // 把它并进 complexity-dominant 会凭空造出一条「该重构」的结论。
            (false, false) => "neither-dominant",
        };
    }

    /// <summary>某个值在已算集合里的百分位（小于等于它的比例）。</summary>
    private static double PercentileOf(List<double> sorted, double value)
    {
        int index = sorted.BinarySearch(value);
        if (index < 0)
        {
            index = ~index;
        }
        else
        {
            while (index + 1 < sorted.Count && sorted[index + 1] <= value)
            {
                index++;
            }

            index++;
        }

        return sorted.Count == 0 ? 0 : (double)index / sorted.Count;
    }

    private static string N(double? value) =>
        value is { } actual ? actual.ToString("0.##", Inv) : "—";

    private static string P(double? value) =>
        value is { } actual ? (actual * 100).ToString("0.#", Inv) + "%" : "—";
}
