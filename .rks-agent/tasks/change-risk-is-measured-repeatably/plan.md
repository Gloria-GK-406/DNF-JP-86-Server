---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 一条命令量出本仓的方法级变更风险

## Approach

在 `Tool/CrapMeter/` 放一个 .NET 10 控制台程序，作为唯一入口。它按顺序做四件事：

1. **建清册**：用 Roslyn 语法树扫 `Server/src/**/*.cs`，为每个源码声明的成员建一条记录
   （限定名、签名、源码区间、成员种类、圈复杂度）。这一步不依赖任何覆盖率产物，
   满足 RQ-007。
2. **采覆盖率**：对 `Server/86JP.Server.slnx` 里的 7 个测试项目逐个跑
   `dotnet test --collect:"XPlat Code Coverage"`，格式为 `opencover`，
   把 7 份 XML 按「同一序列点取访问数之和」合并成一份。
3. **对接**：把合并后每个序列点/分支点按 `(规范化文件路径, 行号)` 归到**最内层包含它的
   源码成员**。这条规则同时解决重载、分部类、泛型、显式接口实现、lambda、局部函数、
   `async`/迭代器状态机——因为编译器生成的方法，其序列点的行号仍落在声明处成员的区间内。
   落在类型内但不属于任何成员的点（字段/属性初始化器、record 合成成员）进
   `generated-unmapped` 分区计数。
4. **算与写**：算 CRAP，出四层分布与报告，全部写进 `.crap/<时间戳>/`。

不用 `dotnet-coverage` 也不用 `reportgenerator`：opencover 一种格式就同时给出行级访问
计数和分支点，够用；少一个工具就少一处版本兼容风险。

**已实测的可行性**（本轮在本工作区做的探针，不是推断）：
`coverlet.collector 10.0.1` 在 net10.0 上跑通，对 `Dfo.Server.Networking.Tests`
产出 10.3 MB 的 `coverage.opencover.xml`，含 8 个模块、5226 个方法
（其中 586 个被访问过），每个 `<Method>` 带 `cyclomaticComplexity`、`<FileRef>`、
以及带 `sl`/`vc`/`bec`/`bev` 的 `<SequencePoint>`。**未被覆盖的方法也在报告里**，
所以 `measured-zero` 与 `missing` 能真正区分开。

## Decisions Before Work

### DP-1 复杂度取 Roslyn 源码级，不取 opencover 的 `cyclomaticComplexity`

opencover 那个值是 coverlet 从 IL 算的，lambda 与状态机是独立 IL 方法各带一个值，
要合回声明处只能求和，那已经不是圈复杂度的定义了；而且 IL 会被编译器改写
（字符串 switch 降级、模式匹配展开），与人读到的控制流对不上。Roslyn 直接数源码判定点，
与 D-1「按源码声明的成员」同口径。opencover 的值仍写进明细做**交叉核对字段**，
两者差异过大的成员在报告的数据质量一节点名。

### DP-2 圈复杂度的判定点集合（写进清单，报告里可引用）

`C = 1 + ` 下列节点计数，统计范围是该成员的**整个体**，含其中的 lambda 与局部函数：

`if` / `while` / `do` / `for` / `foreach`（含 `await foreach`）/ 每个 `case` 标签
（`default` 不计）/ 每个 switch 表达式 arm / `catch` 子句 / `when` 过滤器 /
`?:` / `&&` / `||` / `??` / `??=` / `?.` 与 `?[]` / 模式里的每个 `and` 与 `or`。

不计：`else`、`finally`、`try`、`goto`、LINQ 查询子句。这个集合原样写进运行清单，
不同版本之间比较前先比清单。

### DP-3 `Cov` 的口径是序列点（行）覆盖

`Cov = 被访问的序列点数 / 序列点总数`，归到该成员名下（含并入的生成方法序列点）。
这是 CRAP 的常规输入。分支覆盖（`bev/bec` 之和）另存为明细字段并在报告里并列展示，
但**不参与 CRAP 计算**。口径写进清单。

序列点总数为 0 的成员（抽象方法、接口成员、`partial` 无体声明、纯声明属性）
状态为 `excluded`，理由记 `no-executable-units`。

### DP-4 `.crap` 的排除写进随仓分发的 `.gitignore`

AC-003 明确要求排除来源不是 `.git/info/exclude`。在 `.gitignore` 末尾加 `.crap/` 一节，
带一句为什么。这是本任务唯一一处对既有被追踪文件的改动（另加两处见 T-001）。

### DP-5 工具独立于 Server 解决方案，使用局部中央包配置

Tool/CrapMeter 与 Tool/CrapMeter.Tests 是独立工程，显式遵循 net10.0、Nullable、TreatWarningsAsErrors。
版本统一放在 Tool/CrapMeter/Directory.Packages.props，测试项目通过自己的同名文件导入；只作用于这两个新工程，不改变其他 Tool 项目的依赖策略。Server 的 coverlet 版本仍在 Server/Directory.Packages.props。
### DP-6 测试执行的前置条件由入口显式检查

`PVF_ARCHIVE_PATH` 决定 Infrastructure 的 2 个 `A_missing_archive_*` 是跳过还是执行，
是判据基线的一部分。入口从环境变量读，也接受 `--pvf-archive`；**取不到就直接停**，
不静默跑出一份口径不同的覆盖率。另在采集前检查有无 `Dfo.Server.Host` 进程占用
（会导致 MSB3027，看起来像判据坏了），有就报错退出。

## Work and Validation

| ID | Covers | Work and boundary | Check and expected result |
|---|---|---|---|
| T-001 | RQ-020, DP-4, DP-5 | 仓库接线：`Server/tests/Directory.Build.props`（链式 import 上层 props + 给 7 个测试项目统一加 `coverlet.collector`）、`Server/Directory.Packages.props` 加 `coverlet.collector 10.0.1`、`.gitignore` 加 `.crap/` | `dotnet build Server/86JP.Server.slnx` 通过（TreatWarningsAsErrors 下）；`git check-ignore -v .crap/x` 指向 `.gitignore` |
| T-002 | RQ-007, RQ-011, DP-2 | `Tool/CrapMeter` 工程骨架 + 清册器：Roslyn 语法扫描 `Server/src`，产出成员记录（项目、规范化路径、容器、限定符号、签名、区间、种类、复杂度） | 单元测试：给定构造好的源码片段，各类判定点的复杂度等于手算值；重载/分部类/泛型/显式接口/局部函数/lambda 各有一例 |
| T-003 | RQ-014, DP-6 | 覆盖率采集：前置检查（PVF 路径、Host 进程），逐个测试项目跑 `dotnet test --collect:"XPlat Code Coverage" ... Format=opencover`，原样保留 7 份 XML 与 stdout 日志，记录每个项目的通过/跳过/失败数与退出码 | 真跑一次：7 份 XML 落盘，日志里的数字与 `dotnet test` 尾部一致 |
| T-004 | RQ-008 | opencover 解析 + 合并：按 `(模块, 文件全路径, uspid)` 合并序列点访问数，按 `(模块, 文件, 起止行, 序号)` 合并分支点；模块并集须覆盖 10 个 src 项目中所有产出 dll 的 | 单元测试：两份构造的 XML 合并后访问数为和、点集为并；真跑后断言模块并集包含 `Dfo.Server.Host` |
| T-005 | RQ-009, RQ-010, D-3 | 对接与状态判定：行号包含法归属；`computed` / `measured-zero` / `missing` / `ambiguous` / `excluded` / `generated-unmapped` 六类计数；CRAP 计算 | 单元测试：CRAP 公式对 `(C=1,Cov=1)`、`(C=10,Cov=0)`、`(C=5,Cov=0.5)` 等已知值；对账等式 `清册总数 = computed + missing + ambiguous + excluded` 成立 |
| T-006 | RQ-003, RQ-005, RQ-006 | 运行目录与清单：采集前建 `.crap/<YYYY-MM-DD_HH-mm-ss>/`（同秒加 `-2` 后缀），状态 `running`→`complete`/`partial`/`failed`；清单写快照、范围、政策、工具版本、命令、产物索引 | 连跑两次得两个兄弟目录；人为在 T-003 之后抛错，目录仍在且状态为 `failed` |
| T-007 | RQ-011, RQ-012, RQ-013 | 明细落盘（JSON + CSV）与四层聚合（类/文件/项目/全域）：计数、min/中位/P75/P90/P95/max、风险分档含 `>30`、Top N、复杂度×覆盖率四象限 | 单元测试：分位数算法对已知序列；真跑后用独立脚本从明细复算聚合，与聚合表逐格相等 |
| T-008 | RQ-015, RQ-016, RQ-017, RQ-021 | Markdown 报告生成：证据有效性 → 分布与集中 → 发现 → 选项 → 局限；发现按 CRAP 排序并带上下文分类；反事实场景显式标注；覆盖率不可信时走降级文案且不出 CRAP 数值 | 真跑后人读报告，抽三条发现核回明细；构造一次「零模块匹配」的降级路径，确认报告里没有 CRAP 数值 |
| T-009 | RQ-018 | 跑出基线运行，留在 `.crap/` 下 | 运行状态为 `complete`（或明确标注的 `partial`），产物齐全 |
| T-010 | RQ-019 | `Document/CrapMeasurement.md`：入口命令、前置条件、产物含义、`missing` 与 `measured-zero` 的区别、口径变更时怎么办 | 照文档在干净 shell 里复跑一次成功 |
| T-011 | RQ-001, RQ-002, RQ-020 | 收尾核验：清掉 `bin`/`obj` 与中间目录后从新 shell 复跑；重跑判据确认基线未退化 | 复跑退出码 0；判据 ≥2392 通过 / 0 失败 |

依赖：T-002 与 T-004 的输出同时喂给 T-005；T-005 喂 T-007，T-007 喂 T-008。
T-003 是 T-004 的真实数据来源，但 T-004 的单元测试用构造 XML，不必等 T-003。
T-006 的目录与状态机是 T-003 起所有落盘的前提。

## Choices During Implementation

- **明细格式**：JSON 做机器可读主格式，另出一份 CSV 方便直接看。字段名与 RQ-011 一一对应。
- **Top N 的 N**：先取 30，若尾部集中度显示不足再调，调了就在报告里写明。
- **风险分档边界**：`>30` 是必须有的警戒线；其余分档在拿到真实分布后按分位定，
  不预设。
- **`generated-unmapped` 的呈现**：先按类型聚合计数。若这个桶大到影响结论
  （比如 record 密集的 `Dfo.Server.Data`），在报告的数据质量一节单列，并考虑给出
  「归并到该类型构造函数」的补充规则——但那要作为**第二种明确标注的口径**，不替换主口径。

## Risks

- **R-1 覆盖率采集耗时**：无覆盖率的全量判据约 20 秒；插桩后 7 个项目逐个跑会显著变慢，
  Host.Tests 还要起真实 TCP。若某项目在覆盖率下超时或失败，按 RQ-014 记为部分执行并在
  报告里标明偏向，不得当作全绿基线。
- **R-2 `generated-unmapped` 可能很大**：`Dfo.Server.Data` 是 12 个文件的纯 DTO，
  `Dfo.Server.Abstractions` 110 个文件多为契约类型，record 合成成员会集中落进这个桶。
  这不是缺陷，但必须在报告里先说清楚，否则「全域覆盖率」会被读错。
- **R-3 `Dfo.Server.Abstractions` 与 `Dfo.Server.Data` 没有对应测试项目**：
  它们会以大量 `measured-zero` 出现（被别的测试间接执行到的除外）。这是真实结论
  不是采集缺陷，但报告要把「没有直接测试项目」这个事实与分数并列，否则会被读成代码质量问题。

## 已落实的实现选择

从 slnx 取得生产/测试项目身份，空项目显式保留；顶层入口与逐访问器清册固定函数粒度。同行访问器使用 IL 方法身份消歧，剩余歧义不计分。原始点键与报告路径进入 JSON 明细，各聚合范围带 TopFunctions 和全部分档占比。

工具通过一次解决方案级 dotnet test 调度七个测试工程，与现有原生入口一致。采集前记录源代码/测试/配置/工具内容哈希、PVF 哈希，采集后比较；模块覆盖点集合冲突直接阻断计算。Host 子进程命中缺口使 MeasurementStatus 为 partial，Host 函数记 Missing；Run.Status 描述采集流程成功与否。

源码优先项采用已核查内容的 SHA256 守卫，实测数值取自当前运行，未匹配内容不沿用旧分析。