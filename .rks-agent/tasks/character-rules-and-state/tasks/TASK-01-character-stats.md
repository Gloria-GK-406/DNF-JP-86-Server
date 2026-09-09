# TASK-01 — 职业与基础属性规则


## Task Unit Contract

- Revision: 1
- Outcome and references: ../plan.md T-001；../spec.md RQ-001/002，AC-001/002。职业表示、PVF 完整成长、初始化只读表与创建接入。
- Owned boundary: Data/Characters 职业及属性值；Abstractions/Content 和 Characters 属性契约；Infrastructure/Content 属性源；Application/Characters 属性与创建规则、模块注册及 Host 初始化；对应测试。Document 下属性证据说明。不得修改技能规则或运行库。
- Required checks: 真实 PVF 初值回归、全支持职业组合 1—86 遍历、转职/觉醒边界独立计算、无效编码、初始化及查询性能。运行本工作树相关项目测试，记录命令和 TRX，不提交。

## Execution result — Round 1（2026-09-09）

- **Changed:** 在基线 `8b19eaac9863e23da4703fa644bfd0cb882b6cd6` 上完成职业值、完整 PVF 属性声明、共享属性规则、创建接入及 Host 初始化。唯一写入工作树为 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/character-rules-and-state`，无提交。14 个源码/测试/说明文件的实际 SHA-256 清单在 [owned-files.sha256](../verification/task01/owned-files.sha256)；本任务记录与验证工件不包含在源码清单中。
- **Why:** Career 只检查 job 字节、转职/觉醒 nibble 结构范围，PVF 支持性由规则表单独判断；协议低四位/高四位与既有技能来源一致。保留成长原始精度直到最终 ×10 投影，避免分数增量逐级量化丢失。采用完整准备后一次发布的私有 FrozenDictionary，返回不可变共享结果。Host 只在档案存在时预热，缺档案继续原有启动行为。参考服务端只用于分段算法，未作为实机证明。
- **Checks:** 真实 A21 PVF job 0—13、174 个声明组合、14,964 等级结果和每行 19 属性均通过独立 token→文本/正则解析及闭式分段 oracle；重复查询对象共享、未声明组合拒绝、256 个 packed 往返、非法结构、等级边界、小数成长、不同觉醒和取消后重试均通过。完整说明见 [CharacterBaseStatRules.md](../../../../Document/CharacterBaseStatRules.md)。
- **Remaining:** 无本 TASK 已知实现缺口。等待主控安排独立 quick review；后续技能/当前角色接入属于 TASK-02/03。本执行者未修改执行 YAML 或写审查结论。

### TDD 与验证命令

TDD 价值判断：保护创建和后续当前角色查询可观察的基础属性；可现实发生的错误为 PVF growtype 偏移、15/50 分界偏一或重复舍入，现有仅初值测试不能发现。真实 HP 数值与后续独立全量属性 oracle 不依赖服务内部结构，维护成本低于这些规则错误的影响。已有接口迁移测试只做适配，没有将编译错误计作 RED。

以下 PowerShell 命令均从该工作树执行，PVF 环境仅设在测试进程，不写系统配置：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests --filter FullyQualifiedName~CharacterStatRuleServiceTests --logger 'trx;LogFileName=task01-red.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task01
dotnet test Server/tests/Dfo.Server.Application.Tests --filter 'FullyQualifiedName~CharacterStatRuleServiceTests|FullyQualifiedName~CharacterLifecycleServiceTests|FullyQualifiedName~CharacterRepositoryProductsTests' --logger 'trx;LogFileName=task01-green.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task01 --no-restore
dotnet test Server/tests/Dfo.Server.Application.Tests --filter 'FullyQualifiedName~CharacterStat' --logger 'trx;LogFileName=task01-stats.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task01 --no-restore
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --logger 'trx;LogFileName=task01-infrastructure.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task01
dotnet test Server/tests/Dfo.Server.Application.Tests --no-build --logger 'trx;LogFileName=task01-application.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task01
dotnet test Server/tests/Dfo.Server.Host.Tests --logger 'trx;LogFileName=task01-host.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task01
$env:PVF_ARCHIVE_PATH=$null
dotnet test Server/tests/Dfo.Server.Host.Tests --no-build --logger 'trx;LogFileName=task01-host-no-pvf.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task01
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --no-build --filter 'FullyQualifiedName~A_missing_archive' --logger 'trx;LogFileName=task01-infrastructure-no-pvf.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task01
git diff --check
```

| Evidence | Exit | UnitTestResult |
|---|---:|---|
| task01-red.trx | 1 | 1 Failed：编译成功后，新成长规则尚未实现，InitializeAsync 抛 NotSupportedException；非接口编译错误 |
| task01-green.trx | 0 | 22 Passed |
| task01-stats.trx | 0 | 13 Passed |
| task01-infrastructure.trx | 0 | 266 Passed，2 NotExecuted（存在 PVF 时跳过缺档案用例） |
| task01-application.trx | 0 | 1,141 Passed |
| task01-host.trx | 0 | 6 Passed |
| task01-host-no-pvf.trx | 0 | 6 Passed |
| task01-infrastructure-no-pvf.trx | 0 | 2 Passed |
| git diff --check | 0 | 无空白错误 |

已经用 PowerShell 逐个读取所有 TRX 的 `TestRun.Results.UnitTestResult`，未只看汇总。首个 GREEN 构建曾因测试替身换行文本替换不匹配出现 CS0535，适配后以上 GREEN 命令通过；该编译失败不作为行为 RED。代码末尾换行已规范化，最终 diff --check 通过。

性能记录在 task01-stats.trx 的 Every_declared_career_and_level_matches_independent_archive_arithmetic 输出：初始化 357.947 ms（含档案打开），100 万查询 59.448 ms，校验和 20,186,173,950，Debug 单轮，不作为硬阈值。

### 下游共享接口

- Data：`CharacterCareer(int job, int growType=0, int awakening=0)`，只读 Job/GrowType/Awakening，`FromPacked(byte job,int packedGrowType)`，`PackedGrowType`。
- Abstractions/Characters：`ICharacterStatRuleService.InitializeAsync(CancellationToken=default)`、`SupportedCareers`、`GetStats(CharacterCareer,int level)`。
- GetStats 只读已准备表并返回共享 CharacterBaseStats；调用方若不经 Host，应先 InitializeAsync。缺失组合抛 InvalidOperationException，等级超界抛 ArgumentOutOfRangeException。
- 创建上限仍为 job 12；属性规则支持档案中 job 13。此任务未修改 PlayingCharacter、CharacterEntryAssembly、任何技能规则或运行数据库。

## Independent Quick Review — IQR-TASK01-20260909-01

- Reviewed execution: Execution result — Round 1（2026-09-09）。Task Unit Contract Revision 1，无修订；限定 plan T-001 与 spec RQ-001/002、AC-001/002，未审查后续 TASK 或完整 Spec 验收。
- Owned-diff identity: 基线 `8b19eaac9863e23da4703fa644bfd0cb882b6cd6`；逐个阅读 `verification/task01/owned-files.sha256` 所列 14 个当前文件的修改/新增内容。清单自身 SHA-256 为 `72C4116D1A20ED466D6F8B4DD5FCF5EC2D1CBFE7027B161AAB9307EC59079D22`；审查前后 14 个文件哈希均与清单相同。真实 PVF SHA-256 与执行说明一致。
- Evidence inspected: 逐项读取 task01 全部 8 份 TRX 的 UnitTestResult；RED 1 Failed（预期）、GREEN 22 Passed、stats 13 Passed、Application 1,141 Passed、Infrastructure 266 Passed/2 NotExecuted、Host 有/无 PVF 各 6 Passed、缺档案 Infrastructure 2 Passed，与执行记录一致。stats TRX 的 174 组合、14,964 行、19 属性 oracle 和性能输出与声明相符。
- Fresh representative check: 在当前工作树设置进程级 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，执行 `dotnet test Server/tests/Dfo.Server.Application.Tests --no-restore --filter 'FullyQualifiedName~CharacterStat|FullyQualifiedName~CharacterLifecycleServiceTests|FullyQualifiedName~CharacterRepositoryProductsTests' --logger 'console;verbosity=normal'`，重新编译成功，34 项 Passed，exit 0。此代表复跑输出保留在审查工具记录，未覆盖执行者 TRX。`git diff --check` exit 0。
- Local assessment: 未发现职业 nibble 编码、结构与 PVF 支持分离、完整声明解析、15/50 分段及最终定点投影的明显缺陷；完整准备后原子发布、私有表与不可变共享结果符合只读初始化边界。创建读取未转职 1 级并保留 job 12 上限；Host 预热位置在网络启动前，缺档案分支保持兼容。选角/当前角色迁移仍按既定下游边界处理。
- Finding QR-01（文档归属）: `Document/CharacterBaseStatRules.md:3,25,48-52` 将 TASK-01 身份、TASK-03 后续安排及本次全回归流水放进长期规则说明，同时开头缺日期和基准提交。这违反仓库 AGENTS.md「每种事实只有一个归处」与「不写的」中设计推导须带日期/基准、成果过程只进自身记录和不写后续安排的明确要求；长期文档因此混入会随执行轮次过期的过程信息。最小修正：补日期/基准；移除 TASK 身份和后续排程，把本次测试/性能执行统计留在本任务记录；保留公式、证据置信度、PVF 身份、支持集合及查询/初始化契约等可支持技术决策的内容。无需改变实现。
- Verdict: **changes_required**。唯一阻塞项为 QR-01；该结论仅属于此 Task 的独立快速检查，接受/后续处置由主控执行流程负责。

## Controller disposition / Execution result — Round 2

- **Disposition:** task-rework，接受 QR-01 为本地文档修正，不改变任务契约。
- **Changed:** 仅修正 Document/CharacterBaseStatRules.md：补日期和基准提交，固定参考服务端提交身份，删除 TASK 身份、后续安排及回归过程统计。可支持初始化策略决策的单轮成本测量连同口径保留。
- **Checks:** 13 个代码/测试文件哈希与 Round 1 一致，文档重新核读、git diff --check exit 0；最新 14 文件清单为 [owned-files-round2.sha256](../verification/task01/owned-files-round2.sha256)。未重跑未变的代码测试，沿用 Round 1 TRX 和上一独立审查的当前源码 34 项复跑证据。

## Independent Quick Review — IQR-TASK01-20260909-02

- Reviewed execution: Controller disposition / Execution result — Round 2；Task Unit Contract Revision 1，无契约修订。仅复审 Round 1 唯一 finding QR-01 及文档修正的直接回归，不重开完整实现审查。
- Owned-diff identity: 基线 `8b19eaac9863e23da4703fa644bfd0cb882b6cd6`；最新 [owned-files-round2.sha256](../verification/task01/owned-files-round2.sha256) 自身 SHA-256 为 `DE721A38D4089C2638FB77A3092B46B29404FE69BEF922B4845A2929376236EE`。独立逐项比较 Round 1 清单（SHA-256 `72C4116D1A20ED466D6F8B4DD5FCF5EC2D1CBFE7027B161AAB9307EC59079D22`）与新清单：两者均为相同 14 路径，仅 `Document/CharacterBaseStatRules.md` 哈希变化，13 个代码/测试文件完全一致；重新计算的全部当前文件哈希均匹配新清单。文档当前 SHA-256 为 `ECBF25EBDCF8B9F1266165AF876FF6B35EEEC7AD86B5A1BF85FEAC0B7C9AF36B`。
- QR-01 disposition: **已解决**。按 `AGENTS.md:924-967`「记什么，不记什么」核读当前说明：开头已补 2026-09-09 与基准提交；参考算法固定至 ServerS4A21 提交 `2db3a135ad2c51508426f3c1f5b2e030e0dce9d6` 的确切文件，已用该仓 `git rev-parse HEAD` 和 `git cat-file -e <commit>:<path>` 核验存在。TASK 身份、后续排程和本次回归流水已移除；公式、证据等级、档案身份、支持集合、初始化与查询契约仍完整。保留的单轮成本用于评估完整预热/内存查询策略，明确 Debug、首次打开档案、数据规模及负载/缓存限制，属于有口径的决策依据，不是任务执行流水。
- Evidence inspected: 复核原 [task01-stats.trx](../verification/task01/task01-stats.trx) 输出，174 组合、14,964 行、初始化 357.947 ms、100 万查询 59.448 ms、校验和 20,186,173,950 均与文档一致。再次逐份读取原 8 份 TRX 的 `UnitTestResult`：RED 1 Failed（预期）；GREEN 22 Passed；stats 13 Passed；Application 1,141 Passed；Infrastructure 266 Passed / 2 NotExecuted；Host 有/无 PVF 各 6 Passed；缺档案 Infrastructure 2 Passed。结合上一轮 IQR-TASK01-20260909-01 对同一代码哈希的 34 项代表复跑证据，原检查仍适用于本轮。文档未引入执行流水或后续安排的直接回归，当前 `git diff --check` exit 0；未重跑未变代码。
- Verdict: **passed**。本轮没有发现新的阻塞缺陷；该结论仅覆盖 QR-01 修正及直接回归，Task 接受与后续处置仍由主控负责。
