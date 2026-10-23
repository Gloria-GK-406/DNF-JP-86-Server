# TASK-26 — T-006d：T-006 收口（人群项出站实测、F-61、F-21、F-108、`t006-closure.md` 与 M-2 进入条件）

基准提交 `a861c4de`（TASK-25／T-006c 已提交，结果审查 approved）。范围取自：
- `tasks/TASK-25.md` 文末「TASK-26 — T-006d」边界提纲整节，与「主控方裁定（执行前）」第 5、6、7 条；
- `tasks/TASK-23.md`「T-006 完成的条件」七条与主控方裁定第 1、2、4、5、6 条及 Revision 2 裁定第 1～3 条（收口要写明的解读与已接受差异）；
- `verification/follow-ups.md` 中归属 TASK-26 或 T-006 的未关闭条目：F-21、F-61、F-108、F-112（引用），以及每单元都要更新的 F-69（检索「TASK-26」「T-006」逐条收齐的结果见 §13）；
- `reviews/task-23-result-review.md`（非阻断残余 F-97、F-98、F-100）、`reviews/task-24-result-review.md`（发现 3、4）、`reviews/task-25-result-review.md`（F-112 措辞残余）中交收口的部分；
- T-006 期间登记的 Spec 修订：附录 A 第 4 行、附录 G VC-3 扩展到两塔、AC-011 第 4 条与 RQ-031(d) 末句、VC-6 的取值；
- `plan.md` 的 T-006 行与 M-2 行；`spec.md` RQ-008、RQ-012、RQ-031、RQ-036、RQ-061，AC-007、AC-011～AC-014、AC-017，附录 D、E、G。

本单元的产出是一份对账与三处小的测试侧补强，**不改生产代码**（人群项探针与变异是临时改动，用后逐字节还原）。

输入：
- TASK-25 的结果：替身清单终态 `logs/task-25-doubles.txt`、处置记录 `t006-test-dispositions.md` TASK-25 一节、正式全量 `logs/task-25-build-test.txt`（8232 条）与取证 `logs/task-25-violations.txt`（40 条）；
- TASK-24 的附录 E／G 逐行证据表（`t006-test-dispositions.md` TASK-24 §6）与检索脚本 scratchpad `t24/dr_evidence.py`；
- TASK-23 的出站探针 scratchpad `t23/probe.py`、`t23/probe_sel.py` 与实测 `logs/task-23-selection-bytes.txt`；
- TASK-05 的选角上界 `measurement/selection-outbound-bound.md` 与 `OutboundQueueLimitTests`；
- TASK-20 交给 M-2 的事项（`measurement/m1-summary.md` §8，TASK-20 评审 L1、L2）；
- 收口文件的写法与引用核对：`t005-closure.md`、scratchpad `t19/closure_refs.py`。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **人群项出站实测与上界**（§2；RQ-036、AC-013；TASK-25 提纲第 1 项；裁定第 6 条；待裁定第 1、2 条）：在 `a861c4de` 上用临时探针实测每类会给本人下发人群快照的 CMD 对请求者单连接登记的字节数，按代码推出每人字节上界与人数上界，按 CMD 判定「该 CMD 的单连接上界 ≤ 524,288 B（`QueueByteLimit` 1,048,576 的一半）」。结果补进 `measurement/selection-outbound-bound.md` 末尾一节（带日期与基准提交）。突破或不足以确定时按停止条件 1 处理，不改 `OutboundLimits`。
  2. **F-61 收口**（§3；裁定第 7 条）：
     - 识别名单 `HostViolationTypes` 补 `GameplaySelfWaitException`，`HostOutputViolationTests` 补一行，先 RED 后 GREEN；
     - 新增源码扫描判据：`Server/tests` 中凡启动进程（`ProcessStartInfo`）的文件，都把标准输出与标准错误逐行交给 `GameplayAssertionRecorder.ObserveHostOutput`；配反例，先 RED 后 GREEN；
     - 「子进程中被吞掉、又没打印的违规看不见」写进收口的已知残余；F-61 关闭。
  3. **F-21 收口**（§4）：核对停机排空的兜底拆除与 `TryRemove` 仲裁仍在，停机用例在正式全量中通过，变异确认有用例守着；关闭 F-21（或按 §4 的规则保留残余并新编号）。
  4. **AC-011 第 6 条的反例**（§5；待裁定第 3 条）：`CommandHandlerArchitectureTests.ATypeOutsideTheVocabularyIsRefused` 补 `INetworkMessageSender`、`IGameMessageSender` 两行，证明命令处理器注入判据拒绝发送器。
  5. **新建 `verification/t006-closure.md`**（§6；仿 `t005-closure.md`，只写「交付 → 哪个 TASK → 证据位置 → 状态」，未关闭事项只引 F 编号）：Plan T-006 行逐项、附录 F 投递三项与 AC-017、附录 E 十七行与 AC-014、附录 G、附录 D 与 AC-012、AC-007、AC-011（F-108：满足，其中一条已裁定降级）、AC-013（含 Outcome 1）、RQ-008、RQ-012、RQ-036、RQ-061 发件箱一半、VC-8、「T-006 完成的条件」七条、移交清单、M-2 进入条件、T-006 期间的 Spec 修订与解读（交外部审核）、已接受的差异与证明边界。引用 F-112：TASK-25 的 C2 用例不作为 RQ-031(b) 的证据，RQ-031(b) 的证据是 `OutboxWorkUnitTests`。
  6. **`follow-ups.md`**（§10）：关闭 F-21、F-61；更新 F-69、F-108；文首补 TASK-26 一行；新增从 F-113 起编号。
  7. **一次正式全量**：带取证汇，反例类与对照以外为 0，`exe:` 为 0；表征测试 10 通过、0 跳过，`Characterization/` 与金样本目录不改；变异核对（§9）。
- Owned boundary：
  - **测试**：
    - `Server/tests/Support/GameplayAssertionRecorder.cs`：只改 `HostViolationTypes` 名单（与它的 `<summary>` 中点名类型的那一句，若需要）；
    - `Server/tests/Dfo.Server.Host.Tests/HostOutputViolationTests.cs`：加一个 `InlineData` 行；
    - `Server/tests/Dfo.Server.Host.Tests/` 下新建源码扫描判据文件一个（名字由执行者定，例如 `HostOutputWiringSourceTests.cs`）；
    - `Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs`：只加两个 `InlineData` 行（待裁定第 3 条接受时）；
    - 扫描判据若发现第四个启动进程的测试文件没有接线，补上它的接线（只加 `ObserveHostOutput` 调用，逐个列名）。
  - **文档与证据**：新建 `verification/t006-closure.md`；`verification/measurement/selection-outbound-bound.md`（只在末尾追加一节，原文不动）；`verification/follow-ups.md`；`verification/logs/task-26-*.txt`；一次性脚本放 scratchpad `t26/`，全文附在对应日志末尾。
  - **不改**：
    - 全部生产代码（`Server/src`），包括 `OutboundLimits.cs`、`OutboundQueueLimitTests` 的判据数值；
    - `spec.md`、`plan.md`、`AGENTS.md`、`Docs/`；`Characterization/` 目录与金样本文件；
    - `t006-test-dispositions.md` 已有的 TASK-23～TASK-25 各节（本单元的测试增量写进 `t006-closure.md` §7 的加法与本 Task 的 Result，不另起处置节；若主控方要求单列，追加 TASK-26 一节，不改前文）；
    - `verification/probe-tools/violations.py` 的期望表（预期不变，§8）。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。探针还原之后与变异全部做完之后各一次。
  2. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-26-build-test.txt`。
     - **总数**：基准 8232（TASK-25 正式全量）。本单元为 8232 ＋ N，逐项目写加法，N 逐条列名（预期见 §8）。
     - **结果**：失败只能是 2 条 `DimensionDungeonTests`（`verification/baseline.md`）与 AGENTS.md 已登记的偶发项（三样一起取身份；打包进程用例在满载下的就绪超时，按 F-55／F-103 的形状取身份并隔离复跑）；挂起转储 0。
     - **跳过**：18，不变。
     - **表征测试**：10 通过、0 跳过；`Characterization/` 与金样本目录无改动（`git status` 为证）。
     - **日志信号**：与 TASK-25 Checks 2 同口径、同脚本（scratchpad `t19/analyze.py`）。应为 0 的七项仍为 0；其余各项与 TASK-25 逐条对照，预期不变（本单元不改生产代码），有增减逐条归因。
     - 耗时与内存只作信息（RQ-091 用户修订：单条 ≤ 2 s 即可接受，性能不作停止条件）。F-69：用 scratchpad `t24/elapsed.py` 与 TASK-25 的正式全量（scratchpad `t25/formal-raw.txt`）逐条对比，多出 ≥ 9 秒的逐条隔离复核。
  3. **零违规取证**（`logs/task-26-violations.txt`）：`violations.py` 的命令与 TASK-25 Checks 3 相同。预期先写进日志开头再跑：40 条，`bus-leaked-flow` 2 ＋ `network-wait` 1 ＋ `write-off-executor` 37，与 TASK-25 相同；反例类与对照以外 0；没有测试帧 0；**`exe:` 0**（名单补上 `GameplaySelfWaitException` 之后，打包进程的输出中仍没有任何断言异常）；退出码 0。
  4. **F-61 的 RED／GREEN**（`logs/task-26-f61.txt`）：两段，命令、退出码与关键输出逐段落盘（§3.3）。
  5. **人群项实测**（`logs/task-26-crowd.txt`）：探针补丁全文、各场景的逐 CMD 字节表、每人字节公式与上界、人数上界的出处（或「代码中没有上限」的检索证据）、按 CMD 的判定与加法；探针还原证明：备份、逐字节还原、`cmp` 退出码 0、还原前后 `git diff -- Server/src` 的 sha1 相同（都应为空差异的 sha1）。
  6. **变异核对**（`logs/task-26-mutations.txt`；程序同 TASK-25 Checks 8：每项 scratchpad 备份，逐字节还原后 `cmp`，前后 `git diff -- Server/src` 与 `-- Server/tests` 的 sha1 相同，全部做完后 `--no-incremental` 重建）：§9 每一项都有指定的用例变红，或如实记录不变红并按 §4 的规则处理。
  7. **新增测试 ×10**（`logs/task-26-new-x10.txt`）：本单元新增的方法与行隔离连续 10 次，全部通过；`HostOutputViolationTests`、`CommandHandlerArchitectureTests` 全类 ×3。耗时只作信息。
  8. **检索集**（`logs/task-26-retrievals.txt`，§7）：AC-017 检索式一、二；T-006 删除名称；`ExecutorSendDeadline` 与 2 秒常量；附录 E／G 证据名重跑 `t24/dr_evidence.py`（找到 N、找不到 0，找不到的按停止条件 2）；每个检索式先写预期再跑。
  9. **收口引用核对**（`logs/task-26-closure-refs.txt`）：按 `t19/closure_refs.py` 改写的 scratchpad `t26/closure_refs.py`，对 `t006-closure.md`：引用的用例名与类名都能在 `Server/tests` 找到；引用的 `logs/…` 都存在；引用的 F 编号都在 `follow-ups.md`；移交清单中的 F 编号在 `follow-ups.md` 中未关闭；收口报告「本单元关闭」的 F 编号在 `follow-ups.md` 中已关闭；归属栏写了动作的行状态不是「信息」。全部 0 处不符。
  10. **Spec 修订汇总的核对**（写进 `logs/task-26-retrievals.txt`）：`git diff a4422480 a861c4de -- .rks-agent/tasks/serial-gameplay-executor/spec.md` 的每一处改动行都对到 `t006-closure.md` §10 的一行，§10 中「文字已改」的每一行都对到一处改动；`git diff a861c4de -- spec.md` 为空。
  11. **测试改动核对**（`logs/task-26-diff-check.txt`）：`diffcheck7.py a861c4de --lines`（scratchpad `t07/diffcheck7.py`）加 `t21/methods.py`：方法集合只多了 N 中列出的、没有少的；断言行多重集合有变化的文件只有本单元新建的判据文件（新文件）与 Owned boundary 中点名的文件，逐处列名。
  12. **边界**（`logs/task-26-boundary.txt`）：`git diff --stat a861c4de -- Server/src` 为空；`Server/src` 下没有未跟踪文件；`spec.md`、`plan.md`、`AGENTS.md`、`Docs/` 无改动；`Server/tests` 的改动只含 Owned boundary 列出的文件。
  13. **隔离复跑**（`logs/task-26-isolated.txt`）：Host 一次；待裁定第 3 条接受时 Application 一次。都带取证目录，记录各自的反例类与对照记录数。
  14. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。改动的已有文件行尾与基准一致（规划时核对：Owned boundary 内的已有文件 `GameplayAssertionRecorder.cs`、`HostOutputViolationTests.cs`、`CommandHandlerArchitectureTests.cs`、`follow-ups.md`、`selection-outbound-bound.md` 都是 LF、0 个 CR；探针与变异要动的 `GameplayOutbox.cs`、`TouchSocketTransportDrain.cs` 也是 0 个 CR）。结束时执行 `dotnet build-server shutdown`；`pgrep -x yes` 为 0。
- Dependencies：
  - TASK-25（`a861c4de`）：替身清单终态、处置记录 TASK-25 一节、正式全量与取证、F-112、Remaining 2（M5 在改写后的用例里没有找到可变红的）。
  - TASK-24（`1ee1c91e`）：附录 E／G 逐行证据表、`t24/dr_evidence.py`、F-107、F-108、评审发现 3 与 4、Spec 修订（AC-011 第 4 条、RQ-031(d) 末句）。
  - TASK-23（`50d7bce4`）：判据（RQ-061 发件箱一半、RQ-003 网络等待、`TransportWriteSourceTests`、J1 名称规则）、XO 有序组与 N1～N14、探针 `t23/probe.py`、选角实测、Spec 修订（附录 A 第 4 行、VC-3）、裁定第 1、2、4、5 条的解读。
  - TASK-20（M-1）：交给 M-2 的工具与判定脚本事项；TASK-05：选角上界与 `OutboundQueueLimitTests`。
  - 脚本：scratchpad `t07/diffcheck7.py`、`t19/analyze.py`、`t19/closure_refs.py`、`t21/methods.py`、`t23/probe.py`、`t23/probe_sel.py`、`t24/dr_evidence.py`、`t24/elapsed.py`、`t25/mutate.py`、`t25/violations.sh`，规划时核对存在；执行开始时再核对，缺失时从 TASK-23～TASK-25 日志末尾附的全文恢复。
- Result review：required。
  - `t006-closure.md` 是 M-2 的依据，也是外部审核读 T-006 的入口。它是判断性的文档输出：引用错了、状态词用宽了（把「部分满足」写成「满足」）、漏掉一处 Spec 修订，全量测试都不会变红。Required check 9、10 只能证明「引用的东西存在」，证明不了「用这些证据下这个结论是对的」，要靠审查逐行核对。
  - 人群项上界的判定直接决定 AC-013 记为什么，以及共享常量 `OutboundLimits.QueueByteLimit` 是否需要另立单元调整；判错的方向是「把一个代码中没有上限的量当成有上限」，后果要到线上人多时才暴露。
  - F-61 的源码扫描判据与 AC-011 第 6 条的反例是此后各单元依赖的守卫：判据写得过宽（什么都放行）时，测试照样是绿的，只有反例与变异能揭穿。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. 人群项：任一 CMD 的单连接上界 ＞ 524,288 B，或人数上界在代码里找不到、因而不足以确定（裁定第 6 条）。若主控方在执行前已就待裁定第 1 条作出裁定，按裁定办，不再停下。
    2. 附录 E 某一行的证据用例在 TASK-25 之后找不到（`dr_evidence` 找不到 ＞ 0），且不能对到 TASK-25 处置记录里的改名或承接者。
    3. M-2 进入条件有一条不成立，且不能在收口内补齐（补齐只限本单元 Owned boundary 内的测试与文档）。
    4. 正式全量取证出现 `exe:` 记录，或反例类与对照以外的记录。
    5. 需要改任何生产文件（探针与变异的临时改动除外）、`OutboundLimits`、`spec.md`、`plan.md`。
    6. 表征测试或任何金样本需要改动；正式全量出现基线与已登记偶发项以外的失败。
    7. F-61 的源码扫描发现启动进程的文件中有不是测试夹具、又不能简单接线的（例如在测试里启动 `git` 之类的辅助进程），按「列为豁免并写理由」还是「接线」判不清。
  - 人群项探针与变异期间，允许临时改动 `Server/src` 与临时在测试项目里放一个场景用例文件（§2.3），用后按三项还原证明处理（备份、`cmp`、`git diff` sha1）。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 范围、与提纲的差异

**与提纲一致的部分**：提纲六项（人群项实测、F-61、F-21、F-108、`t006-closure.md`、`follow-ups.md`）全部在 Outcome 里；裁定第 5、6、7 条分别落在 Outcome 5（AC-011 记法）、Outcome 1 与停止条件 1、Outcome 2。

**规划时发现、提纲没有写到的**：
1. **人群项很可能「不足以确定」**（待裁定第 1 条）。城镇区域的在场名单没有代码上的人数上限：`SelectCharacterSessionEntryService.PlanTownPublicationAsync`（`Application/Sessions/SelectCharacterLifecycleServices.cs:279`）把同区所有 `SharesTownPresence` 的他人都放进快照；频道配置的 `MaximumUsers`（`Host/ScriptChannelConfiguration.cs:32`：100／150／250）只写进频道信息回包（`Channel/Info/ChannelInfoResponseEncoder.cs:19`），没有任何地方据它拒绝登录或入区（检索 `MaximumUsers` 只命中这两处与配置读取）。即便按这个显示值算，按提纲的约 582 B／人，150 人的频道里 149 名他人 ＝ 86,718 B，已超过余量 70,053 B。所以按裁定第 6 条，本单元极可能在 §2 停下。计划把 §2 排在最前，停下时其余步骤都还没开始；并把待裁定第 1 条列为执行前裁定项。
2. **`selection-outbound-bound.md`「未计入」的第一条**（无生产写入、取实测值的列表，最大的 0x01C0 多 131,072 B）也在 2 倍余量之外：454,235 ＋ 131,072 ＝ 585,307 ＞ 524,288。它不是人群项，提纲没点名，但 AC-013 的对账要写它怎么算（待裁定第 2 条）。
3. **AC-011 第 6 条**（判据阻止 CMD 直接调用发送器）：TASK-24 §8 留给收口对账。`CommandHandlerArchitectureTests` 按后缀判定，发送器接口不以 `DomainService`／`DomainStore` 结尾，所以会被拒；但它的反例行（`ATypeOutsideTheVocabularyIsRefused`）只有行网关、持久化接缝、运行时查询、命令总线与 `string`，没有发送器。RQ-072 要求判据配反例，这里补两行（待裁定第 3 条）。
4. **F-61 识别名单的范围**：`Abstractions/Execution` 下以 `Gameplay…Exception` 命名的类型有五个（`GameplayOffExecutorException`、`GameplayLeakedFlowException`、`GameplaySelfWaitException`、`GameplayDepthExceededException`、`GameplayExecutorStoppedException`）。裁定只要求补 `GameplaySelfWaitException`；另两个不补，理由见 §3.1，写进收口。
5. **F-14** 的状态栏只写了「『T-006 之后消失』一半已在 TASK-23 落实」，另一半（T-004c 核对）在 TASK-19 已完成（`t004-closure.md` §2「已核对，T-006 之后消失」），但状态栏没有「已关闭」字样。收口在 VC-8 一行引用它时，顺手把状态栏补成两半都已落实（§10）。

**本单元不做的**：调整 `OutboundLimits`、限制人群快照（都是生产改动，归待裁定第 1 条的裁定）；M-2 本身；T-007～T-009 的任何事项；`t006-test-dispositions.md` 前文的改动。

### 1. 基准核实（执行时重跑并落盘 `logs/task-26-verify.txt`）

| # | 问题 | 检索 | 规划时在 `a861c4de` 上的结果 |
|---|---|---|---|
| V1 | 工作区与基准 | `git status --short`；`git log -1` | 干净；HEAD `a861c4de` |
| V2 | 测试总数与取证基线 | 读 `logs/task-25-build-test.txt`、`logs/task-25-violations.txt` | 8232 ＝ HttpApi 54 ＋ Pvf 80 ＋ Protocol 1746 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 179 ＋ Application 4536 ＋ Host 190；失败 2（基线）、跳过 18；取证 40 ＝ 2 ＋ 1 ＋ 37 |
| V3 | 识别名单与断言异常类型 | 读 `tests/Support/GameplayAssertionRecorder.cs:54-58`；`grep -rnE "class Gameplay[A-Za-z]*Exception" Server/src` | 名单 2 项（`GameplayOffExecutorException`、`GameplayLeakedFlowException`）；类型 5 个，见 §0 第 4 条 |
| V4 | 启动进程的测试文件 | `grep -rn "ProcessStartInfo" --include='*.cs' Server/tests`（排除 bin、obj） | 3 个：`EndToEndHostHarness.cs:380`、`LauncherHttpHostStartupTests.cs:307`、`ChannelHostStartupTests.cs:236`；三者都把 `OutputDataReceived` 与 `ErrorDataReceived` 交给本文件的 `Append`，`Append` 调 `ObserveHostOutput`（`:542`、`:455`、`:278`）。`Tool/` 下另有 5 处（CrapMeter、GameplayLoad），不是测试夹具，不在扫描范围（§3.2） |
| V5 | 停机排空兜底与仲裁 | 读 `Networking/Runtime/TouchSocketTransportDrain.cs`、`NetworkPlugins.cs:40-60` | 兜底拆除 `:28-49`（快照里仍登记的连接，`TryRemove` 成功才拆除，失败的拆除不影响其他连接）；关闭回调一侧同样以 `TryRemove` 决定由谁拆除（`NetworkPlugins.cs:48`） |
| V6 | 停机与排空用例 | `grep -rn` 用例名 | `HostShutdownOrderTests.Sigterm_with_an_open_game_connection_stops_the_executor_after_its_teardown_and_exits_cleanly`、`ShutdownOrderWireTests.Stopping_the_host_runs_every_open_connections_teardown_on_the_executor_before_it_stops`、`ConnectionCallbackBoundaryTests.A_teardown_that_fails_in_the_drain_does_not_cost_the_other_connections_theirs`、`TransportDrainRegistrationTests`（执行时逐条读断言，确认哪条守兜底、哪条守仲裁） |
| V7 | 人群快照的产出点与容量 | 读 `PlanTownPublicationAsync`、`PlanPvpAsync`（`SelectCharacterLifecycleServices.cs:279`、`:323` 起）、`TownAreaProjectionDelivery`、`SessionAdjacentEntryDomainService.cs:69`、称号与成就投影的事件订阅；检索人数上限（`MaximumUsers`、区域容量、`ReadyOn` 的上限） | 城镇在场与 PvP 大厅都按运行时名单全量取，没有人数上限；`MaximumUsers` 只用于频道信息回包。称号与成就投影的条目数受 PVF 内容约束（`selection-outbound-bound.md` 已有 0x0166、0x0167 的上界） |
| V8 | 2 秒常量 | `grep -rnE "FromSeconds\(2\)|FromMilliseconds\(2000\)|TimeSpan\(0, 0, 2\)" Server/src` | 3 处：`OutboundLimits.cs:19`（`DeliveryTimeout`）、`:22`（`OrderedWaitLimit`），以及 `Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs:18`（卡牌布局计时，2 s 与 4 s，玩法节奏，与投递无关） |
| V9 | T-006 删除的名称 | `grep -rnE "ExecutorSendDeadline|TouchSocketNetworkMessageSender|TouchSocketNetworkConnectionCloser|NotificationRetryQueue|NotificationRetryDrainService|close-superseded" Server/src` | 0 行 |
| V10 | 命令处理器判据的反例 | 读 `CommandHandlerArchitectureTests.cs:95-110` | 反例 5 行，不含发送器（§0 第 3 条） |
| V11 | T-006 期间的 Spec 改动 | `git diff a4422480 a861c4de -- …/spec.md` | 4 行改动：RQ-031(d) 末句、AC-011 第 4 条、附录 A 第 4 行、附录 G VC-3；VC-6 文字未改 |
| V12 | 脚本 | `ls` scratchpad 下 Dependencies 列出的脚本 | 都在 |

V1～V12 与本表不一致时：V2 的总数不同，停下回报（说明 TASK-25 之后有未记录的改动）；V4 多出启动进程的测试文件，按 §3.2 处理；V7 找到了代码上的人数上限，按 §2.4 用它推上界；其余不一致记录后照常进行。

### 2. 人群项出站实测（Outcome 1）

**2.1 口径**：沿用 `selection-outbound-bound.md`：一次 CMD 对请求者单连接登记的全部帧（含 15 B 帧头）。判据是**每条 CMD 各自的**单连接上界 ≤ 524,288 B（RQ-036：队列上限不小于单条 CMD 对单个连接登记的最大发件箱的 2 倍）。不同 CMD 的条目在队列里叠加，正是 2 倍余量要吸收的，不相加。

**2.2 要判定的 CMD 与人群项**（执行时以 V7 读码为准）：

| CMD | 人群项 | 与选角上界的关系 |
|---|---|---|
| `inbound:GameSelectCharacter` | 城镇在场快照（同区共享在场的他人）、PvP 频道大厅快照（`PlanPvpAsync`） | 与选角上界 454,235 B 相加；两项是否会同时出现在同一次选角里，按读码判定，不能同时出现就取较大者 |
| 城镇进入／换区（`TownAreaProjectionDelivery` 给本人的同伴名单） | 同区他人 | 单独成立，不含选角上界 |
| PvP 大厅进入 | 大厅已就绪者 | 单独成立 |
| 选角之后的称号、成就投影（`event:` CMD，DR-16） | 不是人群项，条目数受 PVF 约束 | 单独成立；按 `selection-outbound-bound.md` 中 0x0166、0x0167 的上界核对，不重测 |

**2.3 实测**：
- 探针：复制 `t23/probe.py` 为 `t26/probe.py`，打到 `Application/Execution/Outbox/GameplayOutbox.cs`，按登记写一行（CMD 类型、根、连接、字节、帧数、消息类型）。备份 → 打补丁 → 构建 → 跑场景 → 逐字节还原 → `cmp` → 前后 `git diff -- Server/src` 的 sha1 相同（Required check 5）。
- 场景：先用现有用例（检索有多名玩家同区、或 PvP 大厅里有他人就绪的用例，例如 `PartyCharacterizationTests`、城镇投影与 PvP 大厅的 Protocol 用例）。要得到每人字节，需要同一场景在「他人 0 名」与「他人 k 名（k ≥ 2）」两种情形下各一次：差值 ÷ k。现有用例给不出时，在 scratchpad 写一个临时场景用例，复制进 `Networking.Tests/Integration/` 跑完即删（同探针的还原证明）。
- 每人字节的上界从编码器推出（名字长度上界、外观件数上界等，写法仿 `selection-outbound-bound.md` 的「公式 → 容量上界与出处」），用实测值自检：实测必须落在公式之内。

**2.4 人数上界与判定**：
- 在代码里找人数上限（V7）。找到了：上界 ＝ 该 CMD 的固定部分 ＋ 每人上界 × 人数上界，按 2.1 判定。
- 找不到：这一项写「不足以确定」，并给出两个数，不挑一个当上界：①「在 524,288 B 以内最多容得下的他人数」N_max ＝ ⌊(524,288 － 固定部分) ÷ 每人上界⌋；②实测场景中的字节数。然后按停止条件 1 停下（除非待裁定第 1 条已裁定）。
- 所有加法当场写出各项、项数与和（AGENTS.md「两条从踩坑里换来的记法」）。

**2.5 落盘**：`selection-outbound-bound.md` 末尾追加「## 人群项（TASK-26）」一节：日期、基准 `a861c4de`、口径、逐 CMD 的表（固定部分、每人公式与上界、人数上界及出处或「代码中没有」、实测、判定）、加法；「未计入」一节原文不动，只在新节里写它与待裁定第 2 条的关系。

### 3. F-61（Outcome 2）

**3.1 识别名单**：
- 补 `nameof(GameplaySelfWaitException)`。理由：TASK-23 开启的 RQ-003 网络等待检查与既有的自我等待检查都抛它（`GameplayAssertions.cs:67`、`GameplayJob.cs:28`、`:252`、`GameplayExecutor.cs:63`），子进程输出里出现它就是程序错误。
- 不补的两个，理由写进收口 §3.4：`GameplayExecutorStoppedException` 是停机时到达的工作的正常结果，打包进程的停机用例会合法地打印它，补进来会制造假记录；`GameplayDepthExceededException` 属 RQ-053 的超限处理（T-007），超限的处理方式是关连接与记日志，不是断言违规，T-007 落实时再判。
- `HostOutputViolationTests.An_assertion_exception_in_a_line_of_host_output_is_recognised` 加一行，行文仿控制台日志的打印形状，例如 `"      Dfo.Server.Abstractions.Execution.GameplaySelfWaitException: Waiting for the network (send) …"`，期望 `"GameplaySelfWaitException"`。

**3.2 源码扫描判据**（新文件，写法仿 `Networking.Tests/Outbound/TransportWriteSourceTests.cs`：读原文、钉名单、名单按处数比较、反例用内存中的文件表）：
- 扫描范围：`Server/tests` 下全部 `*.cs`（排除 `bin/`、`obj/`、`TestResults/`）。`Tool/` 不在范围：那里的进程启动属于测量与代码度量工具，不链接取证记录器，也不在 `dotnet test Server/86JP.Server.slnx` 之内。
- 规则：文件中出现 `new ProcessStartInfo(` 的，必须同时出现 `OutputDataReceived` 与 `ErrorDataReceived` 的订阅，以及 `GameplayAssertionRecorder.ObserveHostOutput(`。名单钉住今天的三个文件，各附一句理由；名单里的文件不再启动进程、或不在名单里的文件启动了进程，都被点名。
- 自身排除：判据文件的反例字符串里也会有这些记号，按路径把判据文件本身排除在扫描之外，或用拼接构造记号；执行者二选一，在类注释里写一句。
- 反例两条：①内存中加一个启动进程、但没有调 `ObserveHostOutput` 的文件，被点名；②名单里的一个文件改成不启动进程，被点名。
- 注释只写代码说不出的：它守的是 TASK-18 N4 指出的那段接线（`Append` → `ObserveHostOutput`），以及它守不住什么（子进程吞掉的违规）。

**3.3 RED／GREEN**（`logs/task-26-f61.txt`）：
1. 名单：先加 `InlineData` 行、不改名单，跑该 Theory，RED（`ViolationIn` 返回 null）；再改名单，GREEN。
2. 扫描判据：写好判据后，临时删去 `ChannelHostStartupTests.Append` 里的 `ObserveHostOutput` 调用，跑判据，RED，且失败信息点名该文件；逐字节还原、`cmp`，再跑，GREEN。这一步同时是变异 MF2（§9）。

**3.4 已知残余**：子进程中被 `catch` 吞掉、又没有打印到输出的违规仍然看不见；打包进程里没有取证记录器，只能看输出。写进收口 §11，F-61 状态写「已复核，残余为信息」后关闭。

### 4. F-21（Outcome 3）

- 读 V5 的代码，在收口 §6 写明兜底拆除与 `TryRemove` 仲裁的位置（文件:行）。
- 停机用例（V6）在正式全量中通过。
- 变异 MF4a（兜底不拆除：`TryRemove` 之后直接 `continue`）、MF4b（不经 `TryRemove` 仲裁：改为只读不删，使迟到的关闭回调与兜底都拆除一次）：各自应当有一条 V6 中的用例变红。
- 处理规则：两项都有用例变红，关闭 F-21；有一项没有用例变红，F-21 关闭「停服之外的关闭回调」一半（TASK-23 N9 已覆盖），没有区分性用例的那一半新编号（交测试基础设施或 T-009，执行时定），不在本单元补用例，也不算停止条件。

### 5. AC-011 第 6 条（Outcome 4，待裁定第 3 条）

- `ATypeOutsideTheVocabularyIsRefused` 加 `typeof(Dfo.Server.Abstractions.Networking.INetworkMessageSender)` 与 `IGameMessageSender`（命名空间执行时确认）两行；remarks 里「四种出过错的方式」一句若要提发送器，只加一个短语。
- 变异 MF5：让 `IsPermitted` 放行以 `Sender` 结尾的类型，两行变红。
- 收口 AC-011 第 6 条的证据写两半：①`ProductionCommandHandlers_OnlyTakeDomainShapes` 对全部生产命令处理器成立，新增的两行证明它拒绝发送器；②执行器上唯一的发送器实现是登记适配器，直发实现已删除（V9 检索为 0，J1 名称规则）。
- 待裁定第 3 条不接受时：本节不做，收口把 AC-011 第 6 条记为「判据成立，缺发送器反例，交 T-008（RQ-070(b) 重写时补）」，AC-011 整体记为部分满足。

### 6. `verification/t006-closure.md`（Outcome 5）

开头：日期、基准提交（`a861c4de` 加 TASK-26 未提交的改动）、用途（判定 T-006 是否完成，M-2 的进入条件是否成立；也是外部审核读 T-006 的入口）、写法、证据底稿（TASK-23～TASK-26 的 Result、`logs/task-23-*`～`task-26-*`、`t006-test-dispositions.md` 三节）、状态词（沿用 `t005-closure.md` 的「完成」「完成，部分移交」「部分满足」，加一个「满足，附条件」：Spec 验收按主控方裁定以降级证据或限定适用范围满足，条件写在同一行）、拆分（T-006a ＝ TASK-23，T-006b ＝ TASK-24，T-006c ＝ TASK-25，T-006d ＝ TASK-26，共 4 个单元；TASK-23 裁定第 0 条、TASK-25 裁定第 1 条）。

| 节 | 内容 | 主要证据来源 |
|---|---|---|
| §1 Plan T-006 行逐项 | 执行器上的发送器、`ProjectAsync`、关连接改为登记发件箱；工作单元提交钩子与组装点（RQ-031(0)）；RQ-031(a)～(d)（(d) 含 F-102 批次化）；接上发送端；附录 E（引 §3）；删除重试队列与排空服务；改写「发送即记录」替身（替身清单终态：100 ＝ 改写 70 ＋ 不改 3 ＋ 交 T-008 0 ＋ 投影 27）；开启 RQ-003「等待网络」检查。末尾写 T-006 结束时的全量与取证 | TASK-23 Result、N1～N14；TASK-25 Result 与 `logs/task-25-doubles.txt`；本单元正式全量 |
| §2 附录 F「删除」栏投递三项与 AC-017 | 三项逐项：删于哪个 TASK、J1 名称规则或检索式；AC-017 两半：「检索为零」（检索式重跑，§7）与「运行期判据在执行器外调用时抛出」（RQ-061 写断言自 TASK-18，反例 `GameplayWriteAssertionTests`；发件箱一半自 TASK-23，反例 N3 四条） | `logs/task-24-lock-scan.txt`、`logs/task-26-retrievals.txt` |
| §3 附录 E 十七行与 AC-014 | 以 TASK-24 §6 证据表为输入逐行写「保护意图 → 新承接方式 → 证据用例 → 状态」；`dr_evidence` 重跑全部找到；被 TASK-25 改写驱动的用例注明「TASK-25 驱动替换，断言不变」；RQ-031(b) 的证据只写 `OutboxWorkUnitTests`，并引 F-112 说明 C2 用例不是 RQ-031(b) 的证据（TASK-25 Remaining 2、M5） | TASK-24 §6、`logs/task-26-retrievals.txt`、TASK-25 处置记录 §6.4 |
| §4 附录 G | VC-1～VC-6、VC-8 逐行；VC-3 注明已扩展到两塔（TASK-23 F1 处置）；VC-6 写实际取值 `attempted`／`offline` 与提醒异常时的 HTTP 500（F-107）；VC-7 引 `t005-closure.md` §5 | TASK-24 §6 末段、F-107 |
| §5 附录 D 与 AC-012 | XO-1～XO-7 逐项：有序组的产出点、交付等级（默认 BestEffort，XO-5 所有者与 XO-6 阶段 Required）、「前序变慢仍成立」的用例、「前序超过等待上限即关闭、后续照常」的用例（XO 专属用例或发送端通用用例，逐项写明是哪一种）；单连接顺序的用例 | TASK-23 N8、§7；`xo-dispositions.md` |
| §6 验收与需求 | AC-007（断线、关连接、被顶替三种情形；由 CMD 发起的关闭在此前登记的帧之后——按连接计的解读，TASK-23 裁定第 2 条；自关来源不自锁 W7；停机落库，含 F-21 的结论）；AC-011（六条单元测试 ＋ 三条线级，逐条用例名；记为「满足，附条件：线级第 3 条按 TASK-24 裁定第 3 条以发送端层证据代替（F-108）」；第 6 条见 §5）；AC-013（发送端测试逐项；「被卡住的连接不影响其他连接」N9；队列上限判据 `OutboundQueueLimitTests`；选角全序列不触发超限 `logs/task-23-selection-bytes.txt`；人群项按 §2 的结果与待裁定第 1、2 条的裁定记）；RQ-008（执行器不等待关连接、交付或传输回调：RQ-003 网络等待检查 N4、作业结束只登记交付 N1、N9；收包回调与 HTTP 不等交付）；RQ-012；RQ-036（传输写入点判据 `TransportWriteSourceTests`、期限与队列上限 `OutboundSenderTests`、2 倍余量）；RQ-061 发件箱一半（反例 N3，F-97 的窄缺口交 T-009）；VC-8（N9、`OutboundSenderTests.A_delivery_that_exceeds_its_deadline_closes_the_connection`、F-14） | 各 TASK 的 Result 与处置记录 |
| §7 「T-006 完成的条件」七条 | 逐条：投递三项与 T-006 删除的名称在 `Server/src` 检索为零；`ExecutorSendDeadline` 为零、2 秒只在 `OutboundLimits` 两个约定位置（V8 的卡牌计时一处写明与投递无关）；作业内没有直接传输写入（运行期检查 ＋ `TransportWriteSourceTests`）；附录 E 每行有经执行器与发件箱的证据（§3）；b1、b2 严格形式开启并通过（`SelectCharacterCompensationCharacterizationTests` 两条）；每个单元一次正式全量、取证反例类外为 0（TASK-23 46 条、TASK-24 40、TASK-25 40、TASK-26 40，各引日志）；测试数量加法：T-006 开始时 8192（TASK-22）→ 8191（TASK-23）→ 8231（TASK-24，含评审后 2 行）→ 8232（TASK-25）→ 8232 ＋ N（TASK-26），每段的增删引各自的处置记录 | 各单元 `logs/task-2x-build-test.txt`、`-violations.txt` |
| §8 移交清单 | 只列 F 编号与归属（T-007、T-008、T-009、M-2 与之后、外部审核、测试基础设施、主控方）。规划时的草稿：T-008 增加 F-98、F-109、F-110、F-111、F-112；T-009 增加 F-97、F-99、F-105；外部审核 F-107、F-108；测试基础设施 F-103。执行时以 `follow-ups.md` 为准，由 Required check 9 核对 | `follow-ups.md` |
| §9 结论与 M-2 进入条件 | Plan Work 表中 M-2 依赖 T-006（T-003、T-005 早已完成）。逐条写成立与否：①T-006 的交付全部有证据（§1～§7）；②AC-011、AC-013 的状态及其附条件；③无未处置的停止条件。不成立的写缺什么、归谁。另列「M-2 规划时接手」：TASK-20 交代的新 dll 与执行器作业行解析、`judge.py` 中 F-83 豁免的去留（TASK-20 评审 L2）、更大的灌包窗口、F-02、F-77～F-82（不阻断） | `measurement/m1-summary.md` §8、TASK-20 |
| §10 T-006 期间的 Spec 修订与解读（交外部审核） | 见下表 | V11、各 TASK 的裁定 |
| §11 已接受的差异与证明边界 | 两塔进入的可见变化（VC-3 扩展）；GM Cera 对外字段（F-107）；F-102 的批次化（一个收件人多条消息由多批变一批，不可编码时一条都不登记）；XO-6 标 Required 的近似（F-98）；登记点先编码后判位置的取证窄缺口（F-97）；b1 对「关闭越过帧」不是区分性用例、由 N6 承担（F-100）；TASK-24 评审发现 3（D6、D7 关闭异常可能取代原始失败）与发现 4（T15 不证明真实选角序列中称号事件的入队时机）；F-61 的已知残余；RQ-031(b) 的证据边界（F-112） | 各 F 编号与评审记录 |

§10 的表，每行写「位置 → 原文 → 现文（或解读） → 授权 → 提交」：

| 位置 | 性质 | 授权 | 提交 |
|---|---|---|---|
| 附录 A 第 4 行（顶替后的关闭由顶替发生的那条 CMD 登记进它的发件箱，F-65） | 文字已改，事实性更新 | TASK-23 裁定第 6 条 | `50d7bce4` |
| 附录 G VC-3（扩展到两塔进入，依据补 DR-3） | 文字已改，放宽可见行为 | TASK-23 第 1 轮评审 F1 的主控方处置第 1 条 | `50d7bce4` |
| RQ-031(d) 末句（祭坛：封锁全部参与者，入站时来源连接关闭） | 文字已改，事实性更正 | TASK-24 裁定第 1 条 | `1ee1c91e` |
| AC-011 第 4 条（同上） | 文字已改，事实性更正 | TASK-24 裁定第 1 条 | `1ee1c91e` |
| VC-6 的取值（`attempted` 保留字符串、含义为「已提交发送」，`offline`；组装失败与提醒异常上抛，HTTP 500，`outcome` 为 `unknown`） | 文字未改，取值与对外字段的裁定（F-107） | TASK-24 裁定第 2 条、评审后处置第 1 条 | `1ee1c91e` |
| RQ-012、RQ-031(c)「此前登记的消息」按连接计 | 文字未改，解读 | TASK-23 裁定第 2 条 | `50d7bce4` |
| RQ-034「整批编码……在发送端保持」：编码改在登记点完成 | 文字未改，与字面的偏离 | TASK-23 裁定第 1 条（备选 B） | `50d7bce4` |
| `NotificationOutcome.Delivered` 改义为「已登记，提交发送」（Abstractions 共享契约） | 契约含义改变，不改名 | TASK-23 裁定第 4 条 | `50d7bce4` |
| AC-011 线级第 3 条降到发送端层（F-108） | 验收证据层级的偏离 | TASK-24 裁定第 3 条；TASK-25 裁定第 5 条 | `1ee1c91e` |
| AC-013 的人群项（本单元的结果） | 按待裁定第 1、2 条的裁定填写 | 待定 | 本单元 |

收口只写结论与证据位置；推导过程留在各 TASK 文件，不抄。

### 7. 检索集（Required check 8）

先写预期，再跑；每条写命令全文与输出行数：

| # | 检索 | 预期 |
|---|---|---|
| R1 | AC-017 检索式一（与 `logs/task-24-lock-scan.txt` 第 267 行相同） | 8 行，全是与队伍门无关的同名 `.Gate` 成员，没有一行在 `Protocol/Game/Party` 下 |
| R2 | AC-017 检索式二：`SemaphoreSlim` in `Protocol/Game/Party` | 0 行 |
| R3 | V9 的 T-006 删除名称 | 0 行 |
| R4 | V8 的 2 秒常量 | 3 行，`OutboundLimits.cs` 两行 ＋ 卡牌计时一行 |
| R5 | `t24/dr_evidence.py` 复制为 `t26/dr_evidence.py`，名单不变 | 全部找到，找不到 0 |
| R6 | 正式全量中 `LockRemovalSourceTests`、`TransportWriteSourceTests`、本单元的扫描判据的输出行 | 通过，名单与处数与 TASK-25 相同（扫描判据为新增） |

### 8. 数量与取证预期

- **N**（预计 4～6，执行时按实际运行例数写）：
  - Host：`HostOutputViolationTests` 1 行 ＋ 扫描判据 3 条（正向 1、反例 2）＝ 4；
  - Application：`CommandHandlerArchitectureTests` 2 行（待裁定第 3 条接受时）。
  - 预期总数 8232 ＋ 4 ＝ 8236，或 8232 ＋ 6 ＝ 8238；逐项目只有 Host（190 → 194）与 Application（4536 → 4538）变化。
- **D**：0。
- **取证**：40 条，与 TASK-25 相同（`bus-leaked-flow` 2 ＋ `network-wait` 1 ＋ `write-off-executor` 37）。新增用例都不经过执行器的违规路径：识别名单的 Theory 只调 `ViolationIn`，不记记录；扫描判据只读文件。`exe:` 0。

### 9. 变异（Required check 6）

| 变异 | 改动 | 应当变红 |
|---|---|---|
| MF1 | 名单删去 `GameplaySelfWaitException`（测试侧） | 新加的那一行 |
| MF2 | `ChannelHostStartupTests.Append` 删去 `ObserveHostOutput` 调用（测试侧；即 §3.3 第 2 步的 RED） | 扫描判据正向用例，失败信息点名该文件 |
| MF3 | 扫描判据的规则改为恒真（判据自身，代表「判据什么都放行」） | 两条反例 |
| MF4a | `TouchSocketTransportDrain` 兜底不拆除（生产侧临时） | V6 中守兜底的用例（执行时确定是哪条；没有就按 §4 处理） |
| MF4b | `TouchSocketTransportDrain` 不经 `TryRemove` 仲裁（生产侧临时） | V6 中守「只拆除一次」的用例（同上） |
| MF5 | `IsPermitted` 放行以 `Sender` 结尾的类型（测试侧；待裁定第 3 条接受时） | 新加的两行 |

人群项探针不是变异，但用同一套还原证明（Required check 5）。

### 10. `follow-ups.md`

- 关闭 F-61（「已复核，残余为信息」：名单与扫描判据的证据、残余的内容）；
- 关闭 F-21（或按 §4 拆分并新编号）；
- 更新 F-69（本单元与 TASK-25 的耗时对比）；
- 更新 F-108：归属栏由「TASK-25 收口」改为「已记入 `t006-closure.md` §6、§10（TASK-26）；外部审核」，状态保持「信息」；
- F-14：状态栏补「两半都已落实（T-004c 核对见 TASK-19，消失见 TASK-23），TASK-26 收口确认关闭」；
- 人群项若按待裁定第 1、2 条的裁定留下后续动作（例如调大上限、限制快照），新增一项，从 F-113 起编号；执行中发现的其他事项同样从 F-113 起编号；
- 文首补一行 TASK-26 的摘要（仿 TASK-25 那一行）。

### 11. 执行顺序

1. §1 核实（V1～V12），落盘 `task-26-verify.txt`。
2. **§2 人群项实测**：探针、场景、上界、判定。触发停止条件 1（且待裁定第 1 条未事先裁定）即停下回报；此时其余步骤都还没开始，工作区只有日志。
3. §3 F-61：名单 RED／GREEN、扫描判据 RED／GREEN。
4. §5 AC-011 第 6 条（待裁定第 3 条接受时）。
5. 构建（`--no-incremental`），Host、Application 过滤运行。
6. 正式全量与取证（Required checks 1～3），隔离复跑（check 13），×10／×3（check 7）。
7. §4 F-21 与 §9 变异，重建。
8. §7 检索集；写 `t006-closure.md`、`selection-outbound-bound.md` 新节、`follow-ups.md`。
9. 收口引用核对、Spec 修订核对、diff 核对、边界、`cr-at-eol` 口径 `diff --check`（checks 9～12、14）。

### 12. 表征测试、金样本与生产行为

- 本单元不改生产代码；探针与变异是临时改动，逐字节还原，正式全量在还原之后、变异之前跑。
- 表征测试 10 条与全部金样本不变；有一条需要改，按停止条件 6 处理。

### 13. 范围来源的去向

| 来源 | 本单元 |
|---|---|
| TASK-25 提纲第 1 项：人群项出站实测 | §2，Outcome 1 |
| TASK-25 提纲第 2 项与裁定第 7 条：F-61 | §3，Outcome 2 |
| TASK-25 提纲第 3 项：F-21 | §4，Outcome 3 |
| TASK-25 提纲第 4 项与裁定第 5 条：F-108 | §6 的 AC-011 行与 §10 表；`follow-ups.md` 更新 F-108 |
| TASK-25 提纲第 5 项：`t006-closure.md` 各节 | §6 |
| TASK-25 提纲第 6 项：`follow-ups.md` | §10 |
| 裁定第 6 条：人群项突破或不足以确定时停下，不改 `OutboundLimits` | 停止条件 1；待裁定第 1 条 |
| TASK-23「T-006 完成的条件」七条 | §6 的 §7 节、§7 检索集 |
| TASK-23 裁定第 1、2、4、5、6 条与 Revision 2 裁定第 1～3 条的解读与差异 | §6 的 §10、§11 节 |
| F-112（C2 用例不作为 RQ-031(b) 的证据） | §6 的 §3 节与 §11 节 |
| F-69 | 正式全量的耗时对比，§10 |
| F-14 | §6 的 VC-8 行，§10 |
| TASK-23 评审残余 F-97、F-98、F-100 | §6 的 §8、§11 节 |
| TASK-24 评审发现 3、4 | §6 的 §11 节 |
| TASK-25 评审残余（F-112 措辞） | 已在 TASK-25 提交前改正；§6 引用时用改正后的措辞 |
| F-107 | §6 的 §4、§10、§11 节 |
| T-006 期间的 Spec 修订（附录 A 第 4 行、VC-3、AC-011 第 4 条与 RQ-031(d) 末句、VC-6 取值） | §6 的 §10 节，Required check 10 |
| `plan.md` T-006 行 | §6 的 §1 节逐项 |
| `plan.md` M-2 行 | §6 的 §9 节 |
| TASK-24 §8：AC-011 第 6 条留给收口 | §5，待裁定第 3 条 |

## 待裁定

1. **人群项上界几乎一定「不足以确定」，建议执行前就定处理方式**（§0 第 1 条、§2.4；RQ-036、AC-013；裁定第 6 条）。
   - 规划时读码：城镇在场与 PvP 大厅的人数没有代码上限；`MaximumUsers` 只是频道信息回包里的显示值，不拒绝任何人。按约 582 B／人，同区他人超过约 120 人即突破 2 倍余量；150 人频道的显示值就已经超过。
   - 按裁定第 6 条，本单元会在 §2 停下。为免多一个来回，建议主控方在执行前从下面三项里选定：
   - **建议 C：接受，并在 AC-013 的对账中写明适用范围。**
     - AC-013 的字面是「队列上限不小于**实测**最大单 CMD 发件箱的 2 倍，选角全序列不会触发超限」。按实测口径（选角最大 41,891 B，加实测的人群项），满足。
     - 按保守上界口径，人群项没有上界。收口写明适用范围：同区共享在场的他人 ≤ N_max 人（§2.4 推出）时，选角 CMD 在 2 倍余量之内；超过时，请求者连接可能因队列超限被发送端关闭（RQ-036 的既定后果，不会拖累执行器与其他连接）。
     - 新增一项 F（交主控方立项，不阻断 M-2）：限制人群快照的单 CMD 字节数，或调大队列上限并重算判据。
     - AC-013 记为「满足，附条件：适用范围如上」。
     - 这改变了 AC-013 的记法，所以需要裁定；不改 Spec 文字。
   - 备选 A：另立单元调大 `QueueByteLimit` 并改 `OutboundQueueLimitTests`。代价：人数没有上界，调多大都只是推后，仍要写适用范围。
   - 备选 B：另立单元限制人群快照（分批或截断）。代价：改生产行为，客户端在人多时看到的在场名单会不同，属于附录 G 之外的可见变化，要改 Spec。
   - 不裁定：维持裁定第 6 条，执行到 §2 停下回报。

2. **`selection-outbound-bound.md`「未计入」第一条（无生产写入的列表）在 AC-013 中怎么算**（§0 第 2 条）。
   - 这些列表（最大的 0x01C0，2 页 × 64 KiB；0x0286 只受帧长约束）只有遗留导入或直接写库才会放大，生产代码里没有写入方。若计入，454,235 ＋ 131,072 ＝ 585,307 B，已超过 524,288 B。
   - **建议**：维持 TASK-05 的口径，不计入；收口在 AC-013 一行写明「无生产写入方的列表不计入上界，由导入工具与 GM 写库的约束承担」，并新增一项 F（交 T-009 或主控方）：给这几类写入方加长度约束，或在 GM／导入路径上做检查。
   - 备选：计入，则 AC-013 按保守口径不满足，转入第 1 条的同一组选项。

3. **AC-011 第 6 条补发送器反例**（§5）。
   - **建议接受**：`CommandHandlerArchitectureTests.ATypeOutsideTheVocabularyIsRefused` 加 `INetworkMessageSender`、`IGameMessageSender` 两行，只改测试，变异 MF5 证明有区分力。理由：RQ-072 要求判据配反例；TASK-24 §8 已把这一条留给收口对账；不补的话，收口只能把 AC-011 记为部分满足。
   - 备选：不补，AC-011 第 6 条交 T-008（RQ-070(b) 重写注入面判据时一并补反例），AC-011 记为部分满足。

4. **`plan.md` T-006 行是否写明实际拆分**。
   - T-006 实际为 TASK-23～TASK-26 四个单元。T-004 行由主控方按 F-74 改过同类的说明。
   - **建议**：本单元不改 `plan.md`（Owned boundary 之外）；由主控方在提交 TASK-26 时，仿 T-004 行的写法补一句「实际为 TASK-23～TASK-26 共 4 个单元，拆分与证据见 `verification/t006-closure.md`」。
   - 备选：授权本单元改这一句。

## 主控方裁定（执行前）

1. **人群项上界：采用选项 C。** 不改 `OutboundLimits`，不改 Spec 文字。
   - AC-013 按字面的「实测」口径判定。实测值为选角最大 41,891 B，加上实测的人群项。
   - 按保守上界口径，收口写明两条适用范围，并给出推导：
     - 同区他人不超过 N_max 人时，满足 2 倍余量（524,288 B）。
     - 超过 1,048,576 B 时，请求者连接按 RQ-036 被关闭，不拖累执行器与其他连接。
   - 以 RQ-091 的规模（20 人）对照，写明离这两条线有多远。
   - AC-013 记为「满足，附条件」。另新增一项 F，交主控方立项（调大 `QueueByteLimit` 或限制人群快照二选一），不阻断 M-2。
   - 有了这条裁定，停止条件 1 不再适用于「不足以确定」这种情况；只有实测值本身突破 524,288 B 时，才停下回报。
2. **`selection-outbound-bound.md`「未计入」第一条：维持 TASK-05 的口径，不计入，接受。** 收口写明理由。另新增一项 F：给导入路径和 GM 写库路径加长度约束。
3. **AC-011 第 6 条的发送器反例，同意补。** 在 `CommandHandlerArchitectureTests` 中补 `INetworkMessageSender`、`IGameMessageSender` 两行，只改测试。
4. **`plan.md` T-006 行本单元不改。** 由主控方提交时仿照 T-004 行，补写实际拆分（TASK-23～TASK-26）。

## Result

执行轮次 1（2026-10-23，合同 Revision 1，基准 `a861c4de`，未提交）。按「主控方裁定（执行前）」第 1～4 条执行：人群项采用选项 C，停止条件 1 只对实测值适用；补两行发送器反例；不改 `plan.md`。

- **Outcome**：done（待独立结果审查，Result review：required）。七条停止条件都没有触发；与计划或裁定不同之处见 Why。
- **Changed**：
  1. **测试**（4 个文件，全在 Owned boundary 内；生产代码 0 改动）：
     - `Server/tests/Support/GameplayAssertionRecorder.cs`：`HostViolationTypes` 加 `nameof(GameplaySelfWaitException)`；
     - `Server/tests/Dfo.Server.Host.Tests/HostOutputViolationTests.cs`：加一行 `InlineData`（控制台打印形状的 `GameplaySelfWaitException`）；
     - 新建 `Server/tests/Dfo.Server.Host.Tests/HostOutputWiringSourceTests.cs`：源码扫描判据，`Server/tests` 中凡 `new ProcessStartInfo(` 的文件都要有两个输出事件的订阅与 `GameplayAssertionRecorder.ObserveHostOutput(` 调用，名单钉住三个夹具；正向 1 条、反例 2 条；按路径排除自身；
     - `Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs`：`ATypeOutsideTheVocabularyIsRefused` 加 `INetworkMessageSender`、`IGameMessageSender` 两行，remarks 加一个短语。
  2. **文档**：新建 `verification/t006-closure.md`；`verification/measurement/selection-outbound-bound.md` 末尾追加「人群项（TASK-26）」一节，原文不动；`verification/follow-ups.md` 文首加 TASK-26 一行，关闭 F-14、F-21、F-61，更新 F-69、F-108，新增 F-113、F-114、F-115。
  3. **证据**：`verification/logs/task-26-{verify,crowd,f61,build-test,violations,isolated,new-x10,mutations,retrievals,closure-refs,diff-check,boundary}.txt`，共 12 份；一次性脚本在 scratchpad `t26/`，全文附在对应日志末尾。
- **Why**：
  1. **人群项的结论与计划的预估不同**（`logs/task-26-crowd.txt`）：
     - **选角时城镇在场快照不可达**：登录落点总是城镇的到达门，到达门的 `SharesTownPresence` 恒为假（`EntryPlacementRuleService.cs:131`、`:157`，`TownContent.cs:106`），所以选角不规划城镇在场，也不刷新观察到的队伍；26 次选角实测都没有。选角的人群项只剩 PvP 频道的大厅快照。
     - **城镇换区每人多一帧队伍名单**：计划只列了在场三帧。读码发现区域内本人不在的每个队伍还要给本人一帧（`RefreshObservedPartiesAsync`），所以每人上界是 539 ＋ 18 ＋ 25 ＋ 8 ＋ 80 ＝ 670 B。
     - **队伍标题没有长度上限**：只受 1 MiB 入站帧约束，登记为 F-115。宠物文本只有导入或写库才会非空，与「未计入」第一条同类，并入 F-114。
     - **PvP 大厅没有实测**：表征宿主只配一个普通频道，按公式计。
     - **N_max**：选角（PvP 频道）⌊(524,288 － 454,791) ÷ 521⌋ ＝ 133，超过 1 MiB 的人数是 1,140；`inbound:GameSetUserArea` ⌊(524,288 － 52) ÷ 670⌋ ＝ 782，超过 1 MiB 的人数是 1,565。20 人规模下分别是 464,690 B 和 12,782 B。实测最大：选角 41,891 B（人群项 0），城镇换区 936 B（他人 3、队伍 1），都没有突破 524,288 B。所以停止条件 1 不适用，AC-013 按裁定第 1 条记为「满足，附条件」，立项 F-113。
  2. **收口里「满足，附条件」比计划写得多**：计划只给 AC-011、AC-013 预定了这个状态词。逐行对证据后，AC-007 与 RQ-012（「此前登记的消息」按连接计，属于解读）、AC-012（超限一半除 XO-1 外由通用用例承担，加上 F-98）、AC-014（DR-16 的证明边界，TASK-24 评审发现 4）也带条件。按 TASK-26 的要求，不把这些写成无条件满足。
  3. **两行发送器反例写出即通过，不是 RED**：判据本来就按后缀拒绝发送器，这两行是反例证据。区分力由变异 MF5 证明：放行以 `Sender` 结尾的类型后，两行都红。
  4. **F-108 的归属栏**写成「外部审核（已记入 `t006-closure.md` §6 AC-011 与 §10……）」，而不是计划写的「已记入……（TASK-26）；外部审核」。原因是 Required check 9 的规则：归属栏写了动作的行，状态不能是「信息」；`TASK-26` 会被这条规则识别为动作。原归属「TASK-25 收口」本来就违反这条规则，这次一并改正。
  5. **F-21 关闭**：MF4a、MF4b 都有用例变红，按 §4 的规则关闭。守兜底与仲裁的是 `ConnectionCallbackBoundaryTests`（各 3 条变红）。两条停机用例在两项变异下都保持绿，与 `TransportDrainRegistrationTests` 类注释一致，记入 F-21 与收口。
  6. **F-14 关闭**：两半都已落实。状态栏改为以「**已在 TASK-26 收口确认关闭**」开头（计划 §10）。
  7. **探针也跑了 Application 与 Protocol**（沿用 `t23/probe.py` 的范围）。这两个项目用的是测试编码器，字节不是线上字节，只作参考；判定只用 Networking 与临时场景的数据。
- **Checks**（日志都在 `verification/logs/`）：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental` 做了四次，每次都是 0 个警告、0 个错误：两次探针还原后各一次（`task-26-crowd.txt`），正式全量前一次（`task-26-build-test.txt`），变异全部做完后一次（`task-26-mutations.txt`）。
  2. **正式全量**（`task-26-build-test.txt`，196 秒）：
     - 总数：HttpApi 54 ＋ Pvf 80 ＋ Protocol 1746 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 179 ＋ Application 4538 ＋ Host 194 ＝ 8238 ＝ 8232 ＋ 6 － 0；
     - N 6 ＝ Host 4（违规识别 1 行 ＋ 扫描判据 3 条）＋ Application 2（发送器反例 2 行）；Host 190 ＋ 4 ＝ 194，Application 4536 ＋ 2 ＝ 4538，其余六个项目不变；
     - 结果：通过 54 ＋ 80 ＋ 1746 ＋ 477 ＋ 968 ＋ 179 ＋ 4520 ＋ 194 ＝ 8218，失败 2（都是 `DimensionDungeonTests` 基线），跳过 18（不变），8218 ＋ 2 ＋ 18 ＝ 8238；挂起转储 0；
     - 表征测试 10 通过、0 跳过；`Characterization/` 与金样本无改动；
     - 日志信号：应为 0 的七项中，六项为 0，`GameplaySelfWait` 为 1。这 1 行是新加那一行用例的显示名，不是违规。bus 8、inbound 8、gm 2，与 TASK-25 逐条相同。`Outbound connection … closed` 为 6 ＝ 5 ＋ 1（TASK-25 为 8），差的 2 条是进程内宿主停机时的时序行，归因见日志文首；
     - F-69：多出 ≥ 9 秒的 9 条，隔离下 5～13 秒，是满载拉长。
  3. **取证**（`task-26-violations.txt`，预期写在日志开头）：40 ＝ 2 ＋ 1 ＋ 37，与 TASK-25 相同；反例类与对照以外 0，`exe:` 0，没有测试帧 0，退出码 0。
  4. **F-61 RED／GREEN**（`task-26-f61.txt`）：
     - 名单：只加用例行、不改名单时，退出码 1，新行得到 `Actual: null`；改名单后退出码 0，7 条通过；
     - 扫描判据：删去 `ChannelHostStartupTests.Append` 的调用后退出码 1，失败信息点名该文件；逐字节还原（`cmp` 0）后退出码 0；`git diff -- Server/tests` 的 sha1 前后相同。
  5. **人群项**（`task-26-crowd.txt`）：探针补丁全文、逐 CMD 字节表、每人公式与上界、「代码中没有人数上限」的检索证据、按 CMD 的判定与加法都在日志里。两次探针都按三项证明还原：备份、`cmp` 退出码 0、`git diff -- Server/src` 的 sha1 前后都是空差异的 sha1 `da39a3ee…`。第 2 次还核对了 `git diff -- Server/tests` 与 `git status --short -- Server` 的 sha1 前后相同，临时场景文件已删除。
  6. **变异**（`task-26-mutations.txt`）：MF1～MF3、MF4a、MF4b、MF5 每一项都有指定的用例变红；每项都逐字节还原、`cmp` 0，`git diff -- Server/src` 与 `-- Server/tests` 的 sha1 前后相同；做完后 `--no-incremental` 重建。
  7. **×10／×3**（`task-26-new-x10.txt`）：×10 共 10 × 6 ＋ 10 × 7 ＝ 130 次，×3 共 3 × 7 ＋ 3 × 15 ＝ 66 次，全部通过。
  8. **检索集**（`task-26-retrievals.txt`）：R1 8 行、R2 0 行、R3 0 行、R4 3 行，都与预期相符；R5 共 51 个名字，全部找到；R6 三个判据全部通过。
  9. **收口引用核对**（`task-26-closure-refs.txt`）：用例名 92 个、日志 14 份、F 编号 67 个，全部对上；移交清单的 60 个 F 编号都未关闭；本单元关闭的 3 个都已关闭；归属与状态规则 0 处不符。第一次运行查出 2 处问题，收口改正后通过（见日志文首）。
  10. **Spec 修订核对**（同在 `task-26-retrievals.txt`）：`a4422480..a861c4de` 的 4 处改动逐一对到 §10 标「文字已改」的 4 行，两边都没有对不上的；`git diff a861c4de -- spec.md` 为空。
  11. **测试改动核对**（`task-26-diff-check.txt`）：方法少 0、多 3；断言行多重集合有变化的已有文件 0 个；非白名单片段逐处对到 Changed 第 1 条。
  12. **边界**（`task-26-boundary.txt`）：`Server/src` 无差异、无未跟踪文件；`spec.md`、`plan.md`、`AGENTS.md`、`Docs/`、`t006-test-dispositions.md` 与 `probe-tools/` 都无改动；`Server/tests` 只有 Owned boundary 内的 4 个文件。
  13. **隔离复跑**（`task-26-isolated.txt`）：
      - Host 194 条全部通过，取证只有对照 1；
      - Application 4538 条，失败只有基线 2 条，取证 31 ＝ 反例 30 ＋ 对照 1；
      - 两者反例类与对照以外都是 0。
  14. **收尾**：`git -c core.whitespace=cr-at-eol diff --check` 无输出；改动与新建的文件都是 0 个 CR，并以 LF 结尾。`dotnet build-server shutdown` 退出码 0；`pgrep -x yes` 为 0（没有加负载）。
- **Remaining**（第 1 轮；第 1、2 条已被「第 1 轮评审后修改」取代：M-2 进入条件改为 TASK-26 与 TASK-27 都提交，F-115 归 TASK-27 且阻断）：
  1. **M-2 进入条件**：`t006-closure.md` §9 判定，TASK-26 经结果审查并提交后成立。AC-007、AC-011、AC-012、AC-013、AC-014、RQ-012、RQ-036 记为「满足，附条件」；交外部审核时要逐条说明，不得当作无条件满足引用。
  2. **新增 F-113、F-114、F-115 交主控方立项**，都不阻断 M-2。
  3. **`plan.md` T-006 行的实际拆分**由主控方提交时补写（裁定第 4 条）。

## 主控方处置（第 1 轮评审后）

第 1 轮快审 verdict `changes_required`，原因是发现 1。按 `task-rework` 处理，本单元仍不改生产代码。

1. **F-115 采用 (a)：在 T-006 内另立一个小单元 TASK-27 修复，不接受这项风险。**
   - 这是 T-006 引入、可由改包客户端触发的第三方断线（基准上 2 s 直发期限内能收完，不会被断开），风险等级为中。用「附条件满足」去承接一个攻击者能任意打破的条件，不成立。
   - TASK-27 的范围不只是队伍标题，还包括同类问题：由客户端提供、长度可变、又会扇出给其他连接的字段。它们都要按协议依据加上限（具体范围由 TASK-27 规划时排查）。
   - 本单元文档按以下方式改写：
     - F-115 改写为「T-006 引入、可由客户端触发的第三方断线」，写明与基准的对比、风险等级，归属 TASK-27；
     - `t006-closure.md` 的 AC-013 与 RQ-036 两行删去「队伍标题为空或很短」这条条件，改为写明「保守上界对恶意输入不成立，由 TASK-27 修复（F-115）」；
     - §9 改为：M-2 的进入条件是 TASK-26 与 TASK-27 都经评审并提交；在 TASK-27 完成之前，T-006 不算完成；
     - §11 末行同步修改；
     - TASK-27 完成后，由 TASK-27 在 `t006-closure.md` 末尾追加落地状态。
2. **发现 2**：扫描判据的触发改为 `\bProcessStartInfo\b` 或 `Process.Start(`，补一条用对象初始化器写法的反例，并在 §11 写明判据的识别范围。

处理后需做的检查：
- Host.Tests 中 `HostOutputWiringSourceTests` 的过滤运行，新反例要先红后绿；
- 收口引用核对重跑；
- `cr-at-eol` 口径的 `diff --check`；
- 在 Result 中补一节「第 1 轮评审后修改」。

## 第 1 轮评审后修改

按上面「主控方处置（第 1 轮评审后）」第 1～2 条修改，只改测试与文档，没有改生产代码（`git diff --stat a861c4de -- Server/src` 仍为空）。证据在 `verification/logs/task-26-review-fixes.txt`。

- **Changed**：
  1. **发现 1（F-115）**：
     - `verification/follow-ups.md`：F-115 改写为「T-006 引入、可由客户端触发的第三方断线」。
       - 写明与基准的对比：基准没有字节队列上限，只有 2 s 直发期限，约 1 MiB 的帧在期限内能收完。
       - 风险等级定为中；同类字段交 TASK-27 规划时排查。
       - 归属改为 TASK-27，写明修复前 T-006 不算完成、M-2 不进入。
       - 文首 TASK-26 一行同步改。
     - `verification/t006-closure.md`：
       - 拆分一行补写 TASK-27（T-006 共 5 个单元，TASK-27 尚未执行）；
       - AC-013 与 RQ-036 两行删去「队伍标题为空或很短」这条条件，改为「保守上界对恶意输入不成立，由 TASK-27 修复（F-115）」；
       - §7 补一句：评审后新增 1 条用例，工作区为 8239，正式全量没有重跑；
       - §8 移交清单单列「TASK-27：F-115」，从「主控方」一行移出；
       - §9 写明「T-006 尚未完成」，M-2 的进入条件改为 TASK-26 与 TASK-27 都经结果审查并提交，今天不成立；TASK-27 完成后由它在收口末尾追加落地状态；
       - §11 末行拆成两行：人群项的适用范围一行仍为「已接受」；队伍标题一行记为「**不接受**，由 TASK-27 修复」。
     - `verification/measurement/selection-outbound-bound.md`（本单元追加的那一节）与 `logs/task-26-crowd.txt` 文首的条件 (ii) 同步改写。
  2. **发现 2（扫描判据）**：
     - `Server/tests/Dfo.Server.Host.Tests/HostOutputWiringSourceTests.cs` 的触发条件由字面串 `new ProcessStartInfo(` 改为正则 `\bProcessStartInfo\b|\bProcess\.Start\(`；
     - 新增反例 `A_process_started_with_an_object_initializer_is_named`（对象初始化器写法、没有接线，应被点名 4 处）；
     - 类注释写明识别范围：其他写法认不出，例如不经这两个记号构造 `Process` 再调实例 `process.Start()`；只按文件判断，不分辨一个文件里有几个进程；
     - `t006-closure.md` §11 的 F-61 残余一行、`follow-ups.md` F-61 的状态栏同步改写。
- **Why**：
  1. **`EndToEndHostHarness.cs` 不需要另行处理**：新触发条件会认出它另用的 `Process.Start("kill", …)`（停机信号）。这个文件已在名单上且已接线，按文件判断，所以没有触发停止条件 7。这一点写进了 §11。
  2. **正式全量没有重跑**：只改了这一个判据文件和文档。正式全量的记录仍是 8238 条的那一次。
- **Checks**（`logs/task-26-review-fixes.txt`）：
  1. **新反例先红后绿**：
     - 只加反例、不改触发条件时，`HostOutputWiringSourceTests` 过滤运行退出码 1，新用例得到 Expected 4、Actual 0；
     - 改触发条件后，构建 0 个警告、0 个错误，4 条全部通过，退出码 0。
  2. **变异 MF2、MF3 在新规则上重跑**：MF2 下正向用例变红；MF3 下三条反例全红，含新的那条。逐字节还原，`cmp` 0；`git diff` 的 sha1 前后相同；`--no-incremental` 重建 0 个警告、0 个错误。
  3. **收口引用核对重跑**：用例名 93、日志 15、F 编号 67，§8 中 60 个；0 处不符，退出码 0。
  4. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。改动与新建的文件都是 0 个 CR，并以 LF 结尾。
  5. **测试改动核对**：方法只多 4 个（扫描判据的 4 条），没有少；已有文件的断言行多重集合不变。
- **Remaining**：TASK-27（F-115 及同类字段的上限）完成之前，T-006 不算完成，M-2 不进入。

## 主控方处置（第 2 轮复核后，提交前）

第 2 轮定点复核 verdict `approved`。4 条不阻断的小问题处理如下：
- 第 1 条：第 1 轮 Result 的 Remaining 标注为已被取代。
- 第 2 条：§9 那一句补上「F-115 作为待修回归另列」。
- 第 4 条：F-115 改为「基准的投递路径上没有字节队列上限」。
- 第 3 条交 TASK-27：标题上限定下之后，F-113 的每人上界改为 670 ＋ 上限，并重算 N_max，在 `t006-closure.md` 的落地状态中写明。
