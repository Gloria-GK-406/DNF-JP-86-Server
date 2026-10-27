---
kind: rks-review
target: spec-001
dimension: tests-and-evidence
verdict: approved
---

# spec-001 封存前完整结果评审：测试与证据维度

```yaml
kind: rks-review
verdict: approved
```

- 日期 2026-10-24。评审对象：`git diff a02bce46 2bdb1afd -- Server/tests`（Spec 基准 → 代码冻结点），以及 `verification/` 下的收口与证据文件；工作区 HEAD `9aeb638c`。按 `AGENTS.md` 的描述，`git diff 2bdb1afd -- Server` 为空，所以工作区 `Server/` 就是冻结点，下文对生产代码与测试代码的读取都基于工作区。
- 采用的准则：reviewing-work 的 Code（完整变更）与 Deliverable（证据文件）两份，范围限于「测试与证据」维度；不评架构，也不评 Spec 文字。
- 独立性：本评审没有参与任何单元的生产。只读，唯一写入的是本文件。没有跑测试套件；只重跑了一次已持久化的取证汇总脚本 `verification/probe-tools/violations.py`，读的是 scratchpad 里现存的 `t34/formal-ev`。
- 我自己的核对脚本放在 `/private/tmp/claude-501/-Users-puxianguo-project/19adbc1e-6ba1-4530-bca0-dcab5d727d8a/scratchpad/rv/`：`methods.py`（逐提交的测试方法增删）、`succ.py`（处置记录里点名的承接者在冻结点上是否存在）、`mutcheck.py` 与 `mutcheck2.py`（变异锚点在冻结点上是否仍在）、`asserts.py`（断言语句级差异）。

## 结论

没有阻断封存的缺陷。累积的测试与证据链经得起抽查：
- 删除、改写与改名都能对到处置记录；
- 数量加法能从原始日志复算；
- 取证汇报的数可以复现；
- 判据的变异锚点绝大多数在冻结点上仍然存在；
- 测量判定没有用事后口径排除越线样本。

下面 4 条发现都属于「可移交」，按根契约 RI-16 交给 B 之后的修正子 Spec，或并进子 Spec B。

## 发现（按严重度）

### T-1（中，可移交）三项被保留的发送端行为，删掉原用例后没有测试守着；处置记录写的承接者不成立

- **位置**：`verification/t006-test-dispositions.md` TASK-23 §2 R8、R12 两行。受影响的生产代码是 `Server/src/Dfo.Server.Networking/Outbound/OutboundSender.cs:238-252`（`Observe` 与 `CloseAsync` 的 `catch`）。
- **问题**：
  1. 已删用例 `ExecutorSendDeadlineTests.An_abandoned_send_that_fails_later_leaves_no_unobserved_exception`，处置写的承接是「`OutboundSender.Observe`，由 `OutboundSenderTests.A_send_that_ignores_its_cancellation_still_times_out` 走到」。冻结点上那条用例的替身发送在 `release.SetResult()` 之后**成功完成**（`OutboundSenderTests.cs:272-288`；`FakeTransport.SendAsync` 只有设了 `FailWith` 才抛出，`OutboundTestSupport.cs:55-65`）。`Observe` 只挂 `OnlyOnFaulted` 续体，所以这条用例根本走不到它要守的分支。另一条超时用例 `A_timed_out_send_stays_a_warning_even_when_the_connection_is_gone` 用的是可取消的 `NeverAsync`，同样走不到出错分支。`Server/tests` 中检索 `UnobservedTaskException`，命中 0 处。
  2. 已删用例 `ExecutorSendDeadlineTests.A_failing_detached_close_is_logged_as_a_warning` 与 `DetachedCloseTests.A_failing_close_is_logged_as_a_warning_and_not_thrown_to_the_requester`，处置写的承接是「`OutboundSender.CloseAsync` 的 `catch`」：这是一处代码位置，不是用例。测试替身 `FakeTransport.CloseAsync` 不会失败（`OutboundTestSupport.cs:67-72`）。`Server/tests` 中检索 `Closing an outbound connection failed`，命中 0 处。
  3. 这三项都是**被保留的行为**，不属于「规格消除」。所以这里违反了处置规则「每条删除都写承接者（名字可检索）；没有承接者的写明保护意图为什么已由规格消除」（`t006-test-dispositions.md` 文首、TASK-17 主控方裁定第 7 条）。`t009-closure.md` §4 与 `a-final-report.md` §6 说删除「每个都指得到处置记录」，按文件粒度这句成立，但这三处的「承接」是空的。
- **后果**：关闭走 `Detached`（即 `Task.Run`，`OutboundSender.cs:87-91`）。如果有人删掉 `CloseAsync` 的 `catch`，关闭失败就会变成一个无人观察、也不留日志的异常；删掉 `Observe` 也一样。两种改动都不会让任何一条测试变红。这是 AGENTS.md「反向扫要扫写入方」要防的那类缺口：清单上记为「有覆盖」，实际没有覆盖。影响面小，因为 .NET 默认不会因为未观察的任务异常让进程崩溃，所以不阻断封存。
- **修法**（最小）：在 `Networking.Tests/Outbound/OutboundSenderTests` 补两条用例：
  - (a) 替身发送在期限过后才以异常结束，断言没有 `TaskScheduler.UnobservedTaskException`；可以强制 GC 并 `WaitForPendingFinalizers`，或者直接断言续体已经观察了异常；
  - (b) 替身的 `CloseAsync` 抛出，断言记了一条 Warning「Closing an outbound connection failed」，发送端照常处理下一条。

  做不到的话，就在 `follow-ups.md` 新开一条交子 Spec B，并把 R8、R12 两行的「承接」改写成「无用例承接，见 F-xxx」。

### T-2（低，可移交）A 自己新增的三处 `ClearAllPools`：新栈根已按 RQ-083 登记，根因修复没有排上日程

- **位置**：`t009-closure.md` §2.2；F-130；以下三处调用：
  - `Server/tests/Dfo.Server.Application.Tests/Execution/TimerSourceHost.cs:80`
  - `Execution/GameplayExecutorLifetimeTests.cs:139`
  - `OnlineUsers/SerialDisconnectTests.cs:91`
- **问题**：TASK-34 第 1 轮评审 B-4 已经指出「与 A 的改动有交集」，汇报也已改正。我核对过：失败栈确实在 `AchievementTitleBookTests.Fixture.Sql` 的局部连接上（原始输出第 6081～6107 行）；`TestGameplay` 会原样传播异常（`Support/TestGameplay.cs:104-131`），不吞失败；推断的机制与 AGENTS.md 已登记的三条栈根相同；登记合规。问题在于：`TimerSourceHost` 是定时源用例共用的宿主，每次释放都会做一次进程级清池，按推断，它对整个 Application.Tests 的偶发率贡献不小；而 F-130 的去向是「交主控方另行立项」，没有排期。
- **后果**：子 Spec B 期间的每一次正式全量，都更容易出现这条偶发，判读时就要反复「取身份再归入」。偶发一旦被当成常态，下一条真回归也就更容易被顺手归进去。
- **修法**：把 F-130 并进子 Spec B 的第一个单元。只改测试：把这三处改成按路径 `SqliteConnection.ClearPool`，或者放进一个不与其他类并行的 collection。不必等外部审核。

### T-3（低，可移交）部分证据只存在于会话 scratchpad，B 之后的外部审核无法复核

- **位置**：
  - 原始取证 `scratchpad/t34/formal-ev/*.jsonl`（7 个文件，约 185 KB）、`t34/iso-ev-*`；
  - TASK-10～TASK-18 的变异脚本 `scratchpad/t1x/mutate.py`。这几个单元的 `logs/task-1x-mutations.txt` 只写了变异名与红灯结果，没有嵌入替换文本；TASK-19 起的日志才把脚本全文嵌进去。
- **问题**：`t009-criteria.md` §4 的若干判据，区分力证据仍引这些早期日志，例如 N10～N12 引 `task-18-mutations.txt` R1～R12，N22 引 `task-15` V11，N23 引 `task-14` V1，N32 引 `task-10` 的 drain-unregistered。scratchpad 是会话级临时目录。根契约把外部审核安排在 B 完成之后，到那时这些文件很可能已经不在了。
- **后果**：外部审核只能看到「某变异使某用例变红」的结论，看不到具体改了什么，也就复核不了区分力。73 条取证的逐条原文同样看不到，只剩汇总。
- **修法**：封存提交前，把 `t34/formal-ev` 和 `t10`～`t18` 的 `mutate*.py` 复制进 `verification/probe-tools/`（或 `verification/raw/`）。这只是文件复制，不改结论。如果不复制，就在 `a-final-report.md` §6 写明「原始取证与早期变异脚本不随仓库保存」。

### T-4（信息，可移交）`exe: 0` 在打包进程一侧没有正向对照，汇报里不带限定

- **位置**：`a-final-report.md` §6「取证 73 条全在反例类与对照里，`exe:` 0」；`t009-closure.md` §2.1。
- **问题**：7 个进程内测试项目各有一条对照记录（`GameplayAssertionRecorderControlTests`），记录器生效这一点我已复核。打包进程一侧，只有在子进程把断言异常类型名打印到输出时才会记成 `exe:`；被吞掉、又没打印的违规看不见（F-61 残余）。正式全量里没有一次「打包进程确实产出 `exe:` 记录」的实跑对照，`HostOutputViolationTests` 只是解析器单测。所以 `exe: 0` 只能读作「没有看到」，不能读作「已证明没有」。
- **修法**：在汇报这一项后面加半句「打包进程只能识别打印出来的断言异常，见 F-61」。或者交子 Spec B：在 Host.Tests 里补一条让打包宿主真实触发并打印一次违规的对照。

## 逐项核对（无问题的要点）

1. **断言放宽的累积**：
   - 逐提交解析测试方法的增删（`rv/methods.py`），35 个提交里所有「真正消失」的方法都已对上：
     - TASK-09 的 1 条、TASK-17 的 41 条、TASK-18 的 2 条、TASK-21 的 6 条、TASK-22 的 24 条、TASK-23 的 38 条外加改名文件 3 条、TASK-24 的 6 条、TASK-25 的 1 条，都在处置记录的删除栏里；
     - TASK-11 的 2 条、TASK-12 的 1 条、TASK-18 的 1 条、TASK-31 的 26 条是有记录的改名，例如 `Simultaneous_party_auto_flips_…` 改为 `Party_auto_flips_run_one_after_the_other_…`，「冲突后重试」由 `Auto_flip_retries_the_same_personal_confirmation_after_a_transient_conflict` 守着；
     - TASK-24 的另外 24 条、TASK-30 的 6 条是我的解析器把方法归错了类，同名方法仍在；
     - 没有发现悄悄删掉的方法。
   - 处置记录中反引号点名、在冻结点上找不到的 54 个名字（`rv/succ.py`），逐个都能对到后续的删除行或 TASK-31 改名表，没有悬空的承接者。
   - 抽查了 30 余条删除与改写：TASK-21 K1～K5、G4，TASK-22 D1～D8，TASK-23 R1～R12，TASK-24 K1、K2、E1，TASK-17 §14.1 中的 A/B/C 各类，TASK-18 §15.1，TASK-25 的 `CollectionBox…`。除 T-1 的三项外，承接者都覆盖原意图，或者意图确实随门、重试、期限一起被规格消除。
   - 有 5 行 TASK-17 删除的承接者，后来在 TASK-22、TASK-24 又被删掉（〔门〕用例与流门用例）。它们守的是门机制本身，最终落在「规格消除」，可以接受；只是 `task-07-conflicts.md` 没有回写最终去向。
   - DR-16 的证据链：从 `Achievement_projection_waits_…` 到 K3，再到占位作业。实质收窄已在 AC-014 的附条件里写明。
   - 断言语句级抽查 7 个文件，共约 55 条被删的断言语句，都能对到处置行：`JoustIntegrationTests`、`NpcShopPurchaseTests`、`LoginSessionReconnectTests`、`TitleBookProtocolTests`、`DungeonLoadingProjectionDeliveryTests`、`OnlineUserServiceTests`、`LeaveDungeonTests`。例如 `NpcShopPurchaseTests` 的端点与帧长断言对到 `t006-test-dispositions.md:378`。
   - 表征测试：`git diff f01dc226 2bdb1afd -- …/Characterization` 只有 b1、b2 两处 `Skip` 被去掉；`Golden/` 为空。
   - `TestGameplay` 会传播异常，`Run` 拒绝 async lambda（`Support/TestGameplay.cs:104-140`），所以包装不会让断言变成空转。
2. **判据的区分力**：
   - 静态核对变异锚点在冻结点上是否仍在（`rv/mutcheck*.py`）：
     - TASK-22 的 S1～S13、TASK-23 的 S1～S25（除 S2、S24）、TASK-24 的 M1～M22、TASK-25 的 M1～M7r、TASK-26 的 MF1～MF5、TASK-27 的 MT1～MT4、TASK-29 的 MT1～MT11、TASK-30 的 MU1～MU12 与 MR1～MR11、TASK-31 的 MF1～MF11，锚点全部原样存在；TASK-21 的 S6、S7（VC-7）逐字存在。
     - TASK-23 S2 的锚点被 TASK-31 改了形，但等价语句 `if (failed) return;` 仍在（`NetworkPlugins.cs:195`）。TASK-19 S6 也一样，等价语句是 `return queue.RunAsync(new GameplayWork(`（`NetworkMessageDispatcher.cs:45`）。TASK-23 S24 是被 F-97 和 MU9 有意取代的。
     - 锚点已不存在的 TASK-10、TASK-12、TASK-17、TASK-18 变异，对应的代码已经删除或重写；最终判据的区分力不依赖它们。N32 的 drain-unregistered 锚点仍在；AC-015 的区分力由 TASK-22 S12 和 TASK-29 MT1～MT3 承担。
   - 这是锚点存在性检查，不是在冻结点上重新执行变异。
3. **取证汇**：用持久化的 `violations.py` 加原期望参数重跑：73 ＝ 2 ＋ 1 ＋ 1 ＋ 69；反例类与对照之外 0 条，`exe:` 0 条，没有测试帧 0 条；7 个程序集各有 1 条对照记录。
4. **测试数量**：
   - 基线逐项目 50 ＋ 1694 ＋ 80 ＋ 97 ＋ 475 ＋ 969 ＋ 4342 ＋ 165 ＝ 7872，冻结点 57 ＋ 80 ＋ 1752 ＋ 477 ＋ 970 ＋ 188 ＋ 4594 ＋ 197 ＝ 8315，都从原始日志的「测试总数」行读出；
   - ΣN 30 项之和为 596，ΣD 8 项之和为 153，我复算一致；
   - TASK-04、TASK-05 只新增了文件（`git diff --name-status`），D ＝ 0 在结构上成立；
   - 跳过数与基线相同（2 ＋ 16）。
5. **测量**：
   - M-1 的 F-83 与 M-2 的 F-119 都是事后裁定，记录都很诚实：第一次判定按字面写「停止」，再交主控方，原因查明后才不计。F-83 由 T-006 修掉，M-2 中为 0；F-119 在 M-3 之前写成规则 P，并在第一次运行之前落盘（`m3-summary.md` 文首）。
   - S3 按 2 s 读，出处是用户修订 `c3115f9a` 与 TASK-20 裁定。
   - M-3 的 18 次运行没有越线、没有重跑，也没有条件运行，没有任何豁免被用来排除越线样本。
6. **偶发与已知失败**：
   - 两条 `DimensionDungeonTests` 在基线与冻结点上的错误文字完全相同（迷宫 7100 内容问题），不是回归；
   - F-125 的 `PvfEquipmentDefinitionSource.cs` 在 A 期间没有改动；
   - F-55 和 F-103 的端口争用形状在基线上就有；
   - 新栈根见 T-2。

## 不在本维度内、未评

架构分层、Spec 解读的授权强度、过渡机制清单的完整性，由其他维度的评审负责。

## Round 2：TASK-35 修正定点复核（2026-10-24）

```yaml
kind: rks-review
verdict: approved
```

- 独立评审：`a_review_tests`；对象为 `9aeb638c` 加当前 TASK-35 改动，限 T-1～T-4 及直接回归。
- 使用 `reviewing-work` Code/Deliverable 准则，只读检查实际测试、替身、追加处置、变异与证据。另运行入库取证汇总并校验25个清单哈希前缀，未重复全量。
- T-1 已解决：新增测试在超时后令发送以唯一异常实例失败，释放引用后GC只计该实例；另测关闭失败恰一条Warning、无Error、其他连接仍Delivered。替身实际返回失败ValueTask。定向23/23；M5删除Observe后两次红，M6删除关闭catch红。处置记录只追加更正。
- T-2 已安排：F-130、汇报§8和B草案均指定B首单元；新增28次清池已披露，触发机制仍标推断。
- T-3 已解决持久化缺口：33个文件＝两次正式取证各7＋变异10＋辅助8＋摘录1；25个完整文件哈希前缀匹配。判据给出早期变异与替换脚本映射，明确TASK-13无变异日志；M1～M7/B1全文在日志。
- T-4 已解决：`exe:0`仅表示没有看到，汇报写明吞异常盲区与缺打包进程真实对照，不把解析器单测当该对照。
- 独立复算：73＝2＋1＋1＋69，七程序集各一对照，反例类/对照外0、exe0、无测试帧0，脚本退出0；不代替新鲜核验。

本维度没有未解决的阻断发现。读取时汇报§6仍有第五栈根“待处置”旧文案，已交主控同步；主控在落盘时确认该文案已改为已登记第五栈根并指向closure§2.3。
