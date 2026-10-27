# TASK-35 — spec-001 完整结果评审后的修正

基准提交 `9aeb638c`（TASK-34 之后）；代码冻结点 `2bdb1afd`。本单元是 spec-001 完整结果评审（`reviews/spec-001-final-review-code.md`、`spec-001-final-review-tests.md`、`spec-001-final-review-spec.md`）之后的修正单元，按 Single Spec 流程，在封存前完成。冻结点因本单元的生产改动而前移，新的冻结点为本单元的提交。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **后台服务吸收作业失败**（生产维度 L1，阻断）：约 15 个定时后台服务的 `ExecuteAsync` 只捕获 OCE，作业以黏滞程序错误失败时，异常抛回 `BackgroundService`；宿主按默认 `StopHost` 停掉整个服务器。基准上同样的异常会被逐项吞掉，所以这是 A 引入的失效模式。修法由执行者在两条里选一条，并写明理由：
     - (a) 在 `GameplayTimerWork.TickAsync` 统一吸收；
     - (b) 逐个服务统一为 BossTower 的写法。
     
     无论选哪条，都要满足以下要求：
     - 吸收时按 TASK-31 已定的日志规则记一条（作业体失败由执行器记 Error，生产者这一侧按 `EndedAJob` 降级），服务继续下一个 tick；
     - 拒绝入队，以及作业体之外的异常，仍按原规则处理；
     - 补反例用例：一个定时服务的作业以程序错误失败后，服务仍在运行，宿主没有停；
     - 用源码或反射判据守住「所有 `GameplayTimerWork`／定时 `BackgroundService` 都吸收作业失败」，或写明为什么不设判据。
  2. **XO-1 有序作用域收窄**（生产维度 L3）：`DungeonEntryProjectionDelivery.cs:340` 的 `using var` 改为块作用域，与 `BossTowerProjectionDelivery.cs:167-172`、`IllusionTowerProjectionDelivery.cs:165-170` 一致；补一条用例，或说明现有 XO-1 用例的覆盖范围。
  3. **发送端两项保留行为补用例**（测试维度 T-1）：在 `OutboundSenderTests` 补两条用例：
     - 超时后才失败的发送，不留未观察的异常；
     - 关闭时抛出异常，只记一条 Warning。
     
     同时改正 `t006-test-dispositions.md` TASK-23 R8、R12 两行的承接写法：只能追加更正说明，不改原文。
  4. **文档修正**（规格维度 M-1、M-2、L-1～L-5；测试维度 T-3、T-4；生产维度 L2、L4）：
     - M-1：`a-final-report.md` 新增「交外部审核重点」完整表。每项写明性质、授权来源（用户本人还是主控方）、事前授权还是事后追认，以及证据位置。必须覆盖：
       - AC-007 按连接计；
       - VC-3 扩展到两塔（TASK-23 命中停止条件 2 后由主控方事后接受）；
       - VC-9；
       - VC-10（可见变化从 TASK-23 起就在代码里，到 R4 才写进附录 G）；
       - RQ-053(d)；
       - RQ-050 末句的实现精度；
       - `NotificationOutcome.Delivered` 含义变化；
       - RQ-034 编码位置；
       - 附录 A 第 4 行；
       - 附录 F 扩大；
       - RQ-031(d)／AC-011 第 4 条；
       - RQ-091（用户修订）；
       - R3 拆分（用户决定）；
       - F-33、F-107、F-108；
       - F-115 修复与 VC-9；
       - F-125、F-127、F-130；
       - 本单元的 L1 修正。
     - M-2：AC-009（A 一半）改为「满足，附条件（F-88、F-93）」；`t009-closure.md` §1、§7.1 同步改。
     - L-1：统一状态词口径（AC-016 与 SC-6；依赖解读的 AC-019、AC-022），写明划分规则。
     - L-2：为 RQ-050 末句的偏离新增 F-131，并定去向。
     - L-4：`multi-spec-map.md` 与 B 草案里「移交 B 的 11 项」更新为实际清单；封存核对清单加上「B 草案移出 `verification/`」。注意 `multi-spec-map.md` 是根级文件，本单元只准备更新内容，写进 `t009-closure.md` 的封存数据，由主控方封存时落盘。
     - L-5：RQ-061 正文中残留的「附录 B 的启动期例外除外」按事实更正，记进修订记录 R4 追加行。
     - L2：RQ-033 结论句改为「每个未完成的前序各等一个上限」，与 TASK-05 评审的选择一致；记进 R4。
     - L4：「提交后组装失败会被记为已确认回滚」作为给 B 的前提提醒，写进过渡机制清单或 B 交接，并登记 F 编号。
     - T-3：把只在 scratchpad 的证据（`t34/formal-ev` 的原始取证、TASK-10～TASK-18 的变异脚本，以及判据引用到的其它脚本）复制进 `verification/evidence/`，并在 `t009-criteria.md`、`a-final-report.md` 的引用处指向新位置；复制不了的写明不随仓库保存。
     - T-4：`a-final-report.md` §6「`exe: 0`」一项补上限定：只能读作「没有看到」，不能读作「已证明没有」。
     - T-2：F-130 的去向改为「子 Spec B 首个单元（只改测试）」。
- Owned boundary：
  - 生产代码只限上述第 1、2 条涉及的文件（后台服务、`GameplayTimerWork`、`DungeonEntryProjectionDelivery.cs`）。
  - 测试：新增与相关测试文件。
  - 文档：
    - `spec.md`（只限 RQ-061、RQ-033 结论句，以及修订记录 R4 追加行）；
    - `verification/` 下的 `a-final-report.md`、`t009-closure.md`、`t009-criteria.md`、`transition-mechanisms.md`、`t006-test-dispositions.md`（只追加）、`follow-ups.md`、`reroute-t008-spec-002-draft.md`（只更新移交清单）、新建 `verification/evidence/`；
    - `logs/task-35-*.txt`。
  - **不改**：根契约、子契约、`routing-decision.md`、`multi-spec-map.md`、`plan.md`、AGENTS.md（除非第 1 条的修法改变了其中描述的失败规则；若要改，只改那一处）。
- Required checks：
  1. Release `--no-incremental` 构建，0 警告。
  2. 新用例先红后绿：第 1 条反例在修复前红、修复后绿；T-1 两条若在基准上就是绿，写明理由（守的是保留行为），并以变异证明区分力。
  3. 变异：去掉第 1 条的吸收，反例变红；判据若有，去掉一处吸收，判据变红；T-1 两条各配一项变异。
  4. 一次带取证汇的正式全量：总数 8315 ＋ N；失败只能是两条基线 `DimensionDungeonTests`，或按 AGENTS.md 判定为已登记的偶发；表征 10 过 0 跳过；取证反例类外 0。
  5. 第 1、2 条的改动不碰热路径（作业执行、发件箱、发送端），说明为什么 M-3 的数字仍代表封存状态；若碰到了，停下回报。
  6. `git -c core.whitespace=cr-at-eol diff --check` 无输出；行尾保持原样。
  7. 收口引用核对。
- Dependencies：TASK-34（`9aeb638c`）。
- Result review：required。修正后由三位完整评审者各自定点复核各自的发现。
- 停止条件：
  1. 第 1 条的修法需要改执行器核心语义，或改共享契约；
  2. 正式全量出现不可归类的失败；
  3. 第 5 条不成立；
  4. 文档修正需要改动验收本身，而不只是披露或事实更正。
- 不 commit；不 `git stash`；不 push。

## Detailed Plan

执行者在读过三份完整评审之后补写（2026-10-24，HEAD `9aeb638c`）。

### 1. 第 1 条：选 (a)，在 `GameplayTimerWork.TickAsync` 统一吸收

- **理由**：
  1. 定时循环的每一拍今天都经 `TickAsync` 入队（20 个调用点）。吸收放在这里，一处改动覆盖全部，新增的定时服务也自动得到。(b) 要在 17 个 `ExecuteAsync` 里各写一遍同样的 catch，漏一处就回到全服停机，只能靠源码文本判据去守。
  2. 不动执行器核心：`GameplayExecutor`、作业令牌、`EndedAJob` 的判定都不改。改的是 Application 里定时生产者的辅助类，合同第 1 条把它列为可选修法，不触发停止条件 1。
  3. 「拒绝入队」与「作业体之外的异常」天然留在原规则：`TickAsync` 只吸收 `GameplayJob.EndedAJob(error)` 为真的异常；拒绝（`Rejected` 的 `Completion`）、调用方令牌的取消（`Run.cancelled` 重抛）照旧抛给 `ExecuteAsync`。
- **日志**：执行器照旧记一条 `Error`（带链）；`TickAsync` 在吸收处记一条**不附异常**的 `Warning`（`{CommandType}`，「服务继续下一拍」），与 TASK-31 定的两塔扫描、Joust 同形（带额外事实的，降为不附异常的 Warning）。为此 `TickAsync` 增加一个 `ILogger` 参数，所有调用点传入服务自己的 logger。
- **随之要处理的**：
  - BossTower、IllusionTower、Joust 三处 `catch (Exception) when (GameplayJob.EndedAJob(error))` 成为不可达分支，删去；其后的 `catch (Exception)`（记 `Error`、继续）保留不动，所以这三个服务对拒绝的处理不变。
  - `LoginSessionMaintenanceService` 今天绕过 `TickAsync`，直接 `sweeps.TryRun(..., stoppingToken)`：改为经 `TickAsync`，构造器加 `ILogger<LoginSessionMaintenanceService>`（DI 解析；测试辅助方法补 `NullLogger`）。入队令牌随之由 `stoppingToken` 变为 `CancellationToken.None`，与其余定时源一致（RQ-009：已入队的运行在执行器停止前照常执行）；作业体是同步的 `SweepExpired()`，不读令牌。
  - 唤醒式循环（`AltarScheduler`、`RoomMechanismMaintenanceService`）吸收后接着 `wake.WaitAsync(next, …)`。黏滞程序错误时作业体已跑完，`next` 已算好；作业体直接抛出时 `next` 可能为 null，于是等到下一次唤醒。基准上后一种情形同样停宿主，所以不是退化，写进 Result 的 Remaining。
- **受影响服务**：`GameplayTimerWork.cs` 1 个；经 `TickAsync` 的 20 个文件（19 个 Protocol、1 个 Application）——`TournamentRewardSweepService`、`FatigueProjection.cs`（`FatigueDailyRefreshService`）、`WeaknessRecoveryService`、`CreatureRuntimeDelivery.cs`（`CreatureClockService`）、`DungeonPartyWipeService`、`MarriageWeddingSlice.cs`（`MarriageWeddingScheduler`）、`RoomMechanismProjection.cs`（`RoomMechanismMaintenanceService`）、`AltarScheduler`、`AltarQuotaDailyRefreshService`、`DimensionQuotaDailyRefreshService`、`GoldSilverHornQuotaDailyRefreshService`、`ReviveCoinQuotaDailyRefreshService`、`TournamentQuotaDailyRefreshService`、`HerasQuotaDailyRefreshService`、`PremiumDailyRefreshService`、`ItemUsageProjection.cs`（`ItemUsageDailyRefreshService`）、`UsageQuotaProjection.cs`（`MirrorQuotaDailyRefreshService`）、`BossTowerProjectionDelivery.cs`（`BossTowerReturnSweepService`）、`IllusionTowerProjectionDelivery.cs`（`IllusionTowerReturnSweepService`）、`JoustMaintenanceService`；外加 `LoginSessionRuntime.cs` 1 个。1 ＋ 20 ＋ 1 ＝ 22 个生产文件。
  - 不改：`ScheduledTaskHostedService`（用 `RunAsync`，已按 `EndedAJob` 记 Warning 并结束该次计划，`ScheduledTaskHostedService.cs:155-163`）、`DomainEventDispatcher`（事件消费，已按 `EndedAJob` 处理，`DomainEventDispatcher.cs:300`）。`GameplayTimerWork.RunAsync` 不吸收：入口方法与这两个循环要看到失败。
- **判据**（行为判据，不用源码文本）：新文件 `Application.Tests/Execution/TimerSourceJobFailureTests.cs`。
  - Theory「定时源的一拍以黏滞程序错误失败后，服务仍在运行并再走一拍」：行 ＝ `TimerSourceReconciliationTests` 的全部定时行（去掉 startup 行）＋ `LoginSessionMaintenanceService`。在 `TimerSourceHost` 的队列内再包一层故障注入：目标 CMD 类型的第一个作业，作业体先调用 `GameplayJob.AssertNotOnExecutor` 并吞掉异常（即评审描述的形态：作业体吞掉了，作业照样失败），再执行原作业体。驱动方式同对账用例：推进手动时钟，并唤醒两个唤醒式信号。断言：`BackgroundService.ExecuteTask` 没有结束，并且故障之后同类型的作业又跑完了一次。
  - Fact「组合里的每个 `BackgroundService` 要么在上面的 Theory 里，要么在带理由的豁免名单里」：从 `TimerSourceHost` 的组合解析全部 `IHostedService`，取 `BackgroundService` 的类型名，与 Theory 行 ∪ 豁免名单比较，要求相等。新增的定时服务不加行会让它变红，加了行就要过 Theory。豁免名单按实际组合定，每项写理由。
  - Fact「真实宿主：定时作业失败后宿主不停」：`Host.CreateApplicationBuilder` 装生产模块与手动时钟，对 `timer:FatigueDailyRefreshService` 注入同一故障，故障之后又跑一拍，断言 `IHostApplicationLifetime.ApplicationStopping` 未触发、该服务的 `ExecuteTask` 未结束。
  - 单测 `GameplayTimerWorkTests`：作业失败被吸收，执行器 1 条 `Error`、生产者 1 条不附异常的 `Warning`；被拒（执行器已停）照旧抛 `GameplayExecutorStoppedException`，生产者 0 条日志；调用方令牌的取消照旧抛 OCE。
- **先红后绿**：Theory 与宿主 Fact 只经 DI 驱动生产服务，在基准代码上能编译，在基准上应为红（已经吸收的 BossTower、IllusionTower、Joust 与计划任务除外）。单测依赖新签名，记为「保留规则的守护＋新日志」，不作 RED 证据。
- **变异**：M1 去掉 `TickAsync` 的吸收 → Theory、宿主 Fact、单测变红；M2 把 `LoginSessionMaintenanceService` 改回直接 `TryRun` → 它那一行变红（判据抓得住绕过）；M3 从 Theory 删一行 → 完备性 Fact 变红；M4 吸收处改记带异常的 `Error` → 单测变红。

### 2. 第 2 条：XO-1 作用域

- 读码事实：`DungeonEntryProjectionDelivery.cs:340` 的 `using var order` 写在 `try { … }` 块内（`:334-356`）。C# 的 using 声明在所在块结束时释放，所以有序组在 `try` 块末尾就已结束，`:365-368` 的 `LeaveManyAsync` 不在 XO-1 组里。评审 L3 的前提不成立。
- 仍按合同改成块作用域 `using (…) { foreach … }`，与 XO-3、XO-4 写法一致，避免再次误读；这是语义等价的改写。
- 用例：在 `PartySelectDungeonHandlerTests` 补一条「进本后城镇离场发给旁观者的条目不在 XO-1 组里」。若测试世界加旁观者的代价过大，改为写明现有两条 XO-1 用例只覆盖队员与请求者的批次。补了用例时配变异：把有序组移到 `try` 之外、包住 `LeaveManyAsync` → 变红。

### 3. 第 3 条：发送端两项保留行为

- `OutboundSenderTests` 新增：
  - 「超时后才以异常结束的发送不留未观察的异常」：替身发送的闸门在期限过后以异常结束；订阅 `TaskScheduler.UnobservedTaskException`，只认本用例的异常实例，强制 GC 与终结器后断言没有收到。变异：删去超时分支的 `Observe(send)` → 变红。
  - 「关闭抛出异常只记一条 Warning，发送端继续处理」：`FakeConnection` 加 `CloseFailWith`；一次发送失败触发关闭，断言恰好一条 `Warning`「Closing an outbound connection failed」、没有 `Error`，之后提交给另一连接的条目照常交付。变异：删去 `CloseAsync` 的 catch → 变红。
- 两条在基准上就是绿：守的是保留下来的行为，区分力由变异证明。
- `t006-test-dispositions.md` 文末追加「TASK-35 更正」一节，改正 TASK-23 §2 R8、R12 的承接，原文不动。

### 4. 第 4 条：文档逐项落点

| 项 | 落点 |
|---|---|
| M-1 | `a-final-report.md` 新增 §10「交外部审核重点」完整表（性质、授权来源、事前或事后、证据位置）；§8 原「外部审核时说明的已接受偏离」一组改为引用 §10 |
| M-2 | `a-final-report.md` §9.1 AC-009（A 一半）改「满足，附条件（F-88、F-93）」；`t009-closure.md` §1 表行、覆盖栏 RQ-023 一句、§7.1「保住保护意图」一行同步 |
| L-1 | `a-final-report.md` §9 状态词说明补划分规则：依赖 §4.2 解读才成立的行记附条件，并写出所依赖的解读；AC-016 与 SC-6 一致记附条件；AC-019、AC-022 按规则改 |
| L-2 | `follow-ups.md` 新增 F-131（RQ-050 末句的实现精度），去向定为子 Spec B；汇报 §8、§9 改为引用 F-131 |
| L-4 | `t009-closure.md` 新增 §7.4「`multi-spec-map.md` 的更新内容」（「11 项」改为实际清单），§7.3 加「B 草案移出 `verification/`」；`reroute-t008-spec-002-draft.md` 只更新移交清单那一段 |
| L-5 | `spec.md` RQ-061 去掉「附录 B 的启动期例外除外」；R4 追加行 |
| L2 | `spec.md` RQ-033 结论句改为「每个未完成的前序各等一个上限」；R4 追加行 |
| L4 | `transition-mechanisms.md` 新增「给 B 的前提提醒」一节；`follow-ups.md` 新增 F-132 |
| T-2 | `follow-ups.md` F-130 去向改为「子 Spec B 首个单元（只改测试）」；`t009-closure.md` §6、汇报 §8 同步 |
| T-3 | 新建 `verification/evidence/`：`t34-formal-ev/`（7 个 jsonl）、`mutation-scripts/`（TASK-10～TASK-18 的变异脚本）、`scripts/`（`t009-closure.md` 引用的 `t34/*.py`、`formal.sh` 等）；`t31/formal-raw.txt`（3.9 MB）只摘录被引用的几行。`t009-criteria.md` §4 前加对照说明，汇报 §6、`t009-closure.md` 的 scratchpad 引用改指新位置 |
| T-4 | 汇报 §6「`exe:` 0」补限定 |
| L1 | 汇报 §10 列入本单元的修正；不另开 F 编号（本单元内修完）；汇报 §6 补本单元的正式全量 |

### 5. 检查

按合同 Required checks 1～7。正式全量用 TASK-34 的命令与取证汇，总数应为 8315 ＋ N，N 为本单元新增例数，逐项写出。

## Result

- **Outcome: done**（执行轮次 1，2026-10-24，基准 `9aeb638c`，未提交，待独立结果评审与检查点提交）。
- Required checks 1～7 已满足。原 check 4 命中停止条件 2 的 Blocker，已按文末主控方处置 (A) 完成第五个栈根登记；依据补入 `verification/t009-closure.md` §2.3。正式全量不重跑，原始失败与复跑证据保留如下；这不表示清池竞争已被观测或根因已修复。

### 原阻断项（历史证据；已按处置 A 解除）

- **失败**：`JoustIntegrationTests.Statistics_failure_rolls_back_mail_completion_and_accounting_marker_then_retry_counts_once`（`logs/task-35-build-test-raw.txt` 第 7832 行起）。
- **身份**：`ObjectDisposedException: Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'`，抛在 `sqlite3_prepare_v2`；栈根 `SqliteDatabaseParticipant.CommandAsync → SqliteConnection.BeginTransaction → SqliteCommand.ExecuteNonQuery`，由 `JoustDomainService.Write → SqliteEventPersistence.AcquireWriteAsync` 经 `JoustIntegrationTests.Fixture.Run` 进入。
- **为什么不能直接归类**：异常类型与 AGENTS.md「判据偶发失败」第 1 条一致，复跑即绿（第 2 条，见下）；但栈根不在登记表的四条之内（第 3 条不满足）。按 AGENTS.md，清单外的新栈根不得直接归入，要另给不归因的明证，确认后追加进登记表。这一步由主控方决定（TASK-34 同类情形由主控方处置）。
- **已取得的证据**：
  1. 失败路径上的文件本单元都没改：`git diff --stat 9aeb638c -- Server/src/Dfo.Server.Persistence Server/src/Dfo.Server.Application/Events/JoustDomainService.cs Server/tests/Dfo.Server.Application.Tests/Events/JoustIntegrationTests.cs Server/tests/Support/TestGameplay.cs Server/src/Dfo.Server.Application/Execution/GameplayExecutor.cs` 为空。本单元改过 `JoustMaintenanceService.cs`（删去 `ExecuteAsync` 里不可达的一支），失败用例不经过它的 `ExecuteAsync`。
  2. 有判别力的复跑：`Application.Tests` 整个项目跑 3 次（scratchpad `t35/repro.sh`），每次 4626 ＝ 4608 ＋ 2 ＋ 16，失败的只有两条基线 `DimensionDungeonTests`，输出里 `SQLitePCL.sqlite3` 0 次（`logs/task-35-build-test.txt` 末节）。复现 0／3。
  3. **与本单元的改动有交集，不在失败路径上**：推断的触发机制是进程级 `SqliteConnection.ClearAllPools()` 与并行测试类（F-130）。本单元新增的 `TimerSourceJobFailureTests` 有 28 例各调用一次 `ClearAllPools`（27 例经 `TimerSourceHost` 释放，1 例自建宿主；都经既有的 `SqliteTestFiles.Delete`），可能提高了这条偶发的出现概率（推断，未观测）。调用它的文件数不变（25）。
  4. 这是本仓第一次见到这个栈根：`verification/logs/` 里此前没有 `SqliteDatabaseParticipant.CommandAsync` 出现在失败栈里的记录。
- **需要主控方决定**（任选其一，或另定）：
  - (A) 按 TASK-34 的先例登记：以上 1～4 为不归因依据，把栈根追加进 AGENTS.md 登记表（第五条），check 4 按「已登记的偶发」判定，本单元不重跑正式全量；
  - (B) 先降低本单元带来的触发次数再重跑：让 `TimerSourceJobFailureTests` 不调用进程级清池（例如给 `TimerSourceHost` 加一个只删目录、不清池的释放方式，或把这个类的清池改为按路径），这会提前做一部分 F-130，超出合同 T-2 的「去向改为 B」，需要授权；
  - (C) 其他处置。

### 已完成的事实

**Changed**

- 生产（23 个文件 ＝ 第 1 条 22 ＋ 第 2 条 1，`git diff --stat 9aeb638c -- Server/src`）：
  - `Dfo.Server.Application/Execution/GameplayTimerWork.cs`：`TickAsync` 增加 `ILogger` 参数，改为异步方法；作业失败（`GameplayJob.EndedAJob` 为真）在这里吸收，记一条不附异常的 `Warning`「Timer {CommandType} failed; the hosted service continues with its next tick」。拒绝入队与调用方令牌的取消照旧抛出。
  - 经 `TickAsync` 的 20 个文件，每个调用点传入服务自己的 `logger`（脚本 scratchpad `t35/addlogger.py`，20 个文件各 1 处）。
  - `BossTowerProjectionDelivery.cs`、`IllusionTowerProjectionDelivery.cs`、`JoustMaintenanceService.cs`：删去已不可达的 `catch … when (GameplayJob.EndedAJob(error))` 一支；其后记 `Error`、继续的 `catch (Exception)` 不动。
  - `Dfo.Server.Application/Authentication/LoginSessionRuntime.cs`：`LoginSessionMaintenanceService` 改为经 `TickAsync` 入队，构造器加 `ILogger<LoginSessionMaintenanceService>`；入队令牌随之由 `stoppingToken` 变为 `CancellationToken.None`，与其余定时源一致。
  - `Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntryProjectionDelivery.cs`：XO-1 的 `using var order` 改为 `using (…) { … }` 块，与 XO-3、XO-4 同形。
- 测试（新增 2 个文件，改 5 个文件）：
  - 新增 `Application.Tests/Execution/TimerSourceJobFailureTests.cs`：Theory 26 行（对账的 25 个定时行 ＋ 登录过期清理）、组合完备性 1 条、真实宿主 1 条，共 28 例；
  - 新增 `Application.Tests/Execution/GameplayTimerWorkTests.cs`：4 例（吸收 2 行、被拒 1、取消 1）；
  - `TimerSourceHost.cs` 加可选的队列包装参数；`LoginSessionMaintenanceExecutorTests.cs` 的构造补 `NullLogger`；
  - `Networking.Tests/Outbound/OutboundSenderTests.cs` 新增 2 例，`OutboundTestSupport.cs` 的 `FakeConnection` 加 `CloseFailWith`；
  - `Protocol.Tests/Game/DungeonEntry/PartySelectDungeonHandlerTests.cs` 新增 1 例 `The_town_departure_after_a_party_entry_is_not_in_the_entry_group`。
  - N ＝ 28 ＋ 4 ＋ 2 ＋ 1 ＝ 35。
- `AGENTS.md`：「失败与日志」一段加一句（定时源经 `TickAsync` 吸收作业失败、绕过它的定时循环会让宿主停下、由哪条判据守着），只改这一处。理由：合同允许在修法改变所述失败规则时改那一处；新写定时源的人要据此写循环。
- 文档：按 Detailed Plan §4 的表逐项落点，另有：
  - `spec.md`：RQ-033 结论句、RQ-061 括注、R4 追加 2 行与 1 句说明；
  - `a-final-report.md`：文首 TASK-35 说明、§3 VC-3 授权栏、§6（TASK-35 的正式全量、`exe:` 限定、取证位置）、§8（B 列表加 F-130～F-132，外部审核一组改为引用 §10，RQ-050 末句改引 F-131）、§9（状态词口径与 15 行 AC、6 行 SC），新增 §10（26 项）；
  - `t009-closure.md`：文首说明、§1 AC-009 行与覆盖栏、scratchpad 引用改指 `evidence/`、§6（18 ＋ 1 ＋ 7 ＋ 12 ＋ 1 ＋ 2 ＋ 11 ＝ 52）、§7.1（SC-1、SC-4、SC-6、SC-8、SC-9、SC-10、SC-11 与「保住保护意图」）、§7.3 追加 2 条、新增 §7.4；
  - `t009-criteria.md` §4 前加变异脚本的位置；`transition-mechanisms.md` 文首说明、`AGENTS.md` 行号随本单元的两行改正、新增 §5；`t006-test-dispositions.md` 文末追加「TASK-35 更正」；`follow-ups.md` 文首一行、F-130 去向、新增 F-131、F-132；`reroute-t008-spec-002-draft.md` 只改移交清单一段；
  - 新建 `verification/evidence/`：`t34-formal-ev/` 7 个、`t35-formal-ev/` 7 个、`mutation-scripts/` 10 个、`scripts/` 8 个、`t31-formal-raw-excerpt.txt` 1 个（7 ＋ 7 ＋ 10 ＋ 8 ＋ 1 ＝ 33 个文件）；
  - 日志 `logs/task-35-red.txt`、`-mutations.txt`、`-targeted.txt`、`-build-test.txt`、`-build-test-raw.txt`（约 3.9 MB）、`-violations.txt`、`-evidence.txt`。

**Why**

- 第 1 条选 (a)：理由见 Detailed Plan §1。补充一点：BossTower、IllusionTower、Joust 的 `EndedAJob` 一支在 (a) 之后不可达，留着会让读者以为吸收还在各服务里，所以删去。
- 第 2 条：评审 L3 的前提不成立。`using var` 写在 `try` 块里，有序组在块末就结束，`LeaveManyAsync` 从来不在 XO-1 组里；变异 B1（还原原写法）新用例照样通过，M7（把有序组放到 `try` 之外）新用例变红。仍按合同改成块作用域，只为不再被误读。
- L-1 选「依赖 §4.2 解读的行一律记附条件」，而不是加一栏：另一种做法会让 AC-007 由附条件变成满足，看起来像放宽。代价是 AC-005、006、011、013、016、017、019、020、021、022 与 VC-6 的条件栏都写出所依赖的解读，SC-1、4、8、9、10 随之记附条件。没有一行由附条件变成满足。

**Checks**

1. 构建：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 警告、0 错误（`logs/task-35-build-test.txt` 文首）。
2. 先红后绿：
   - RED（生产代码为基准，只加新测试）：`TimerSourceJobFailureTests` 28 例中 20 红 8 绿；Theory 26 行中 18 红、8 绿，绿的 8 行是已经自己吸收的 BossTower、IllusionTower、Joust 与计划任务 5 行（3 ＋ 5 ＝ 8），18 ＋ 8 ＝ 26；完备性 Fact 只因豁免名写错红（`DomainEventDispatcher` 应为 `DomainEventHostedService`，已改）；宿主 Fact 红，断言「The host is stopping after a timer CMD failed」（`logs/task-35-red.txt`）。
   - GREEN：Application 定向 142／142、Networking `OutboundSenderTests` 23／23、Protocol `PartySelectDungeonHandlerTests` 26／26（`logs/task-35-targeted.txt`）。
   - T-1 两条在基准上就是绿（`OutboundSender.cs` 本单元未改）：守的是保留行为，区分力由 M5、M6 证明。XO-1 新用例在原写法上也是绿（B1），区分力由 M7 证明。
3. 变异（`logs/task-35-mutations.txt`，脚本全文在内）：M1 去掉吸收 → 21 红（单测 2 ＋ Theory 18 ＋ 宿主 1）；M2 登录清理改回直接 `TryRun` → 它那一行红；M3 Theory 去掉一行 → 完备性红；M4 吸收处改记带异常的 `Error` → 单测 2 红；M5 删超时分支的 `Observe(send)` → 红（跑了 2 次，都红）；M6 删 `CloseAsync` 的 catch → 红；M7 有序组包住城镇离场 → 红；B1 还原原写法 → 26／26 绿。每次都按哈希还原文件。
4. 正式全量（`logs/task-35-build-test.txt`）：总数 57 ＋ 80 ＋ 1753 ＋ 477 ＋ 970 ＋ 190 ＋ 4626 ＋ 197 ＝ 8350 ＝ 8315 ＋ 35；通过 8329、失败 3、跳过 18，8329 ＋ 3 ＋ 18 ＝ 8350。失败 3 ＝ 两条基线 `DimensionDungeonTests` ＋ 上面已按处置 (A) 登记的偶发。表征 10 通过、0 跳过；挂起转储 0。取证 73 ＝ 2 ＋ 1 ＋ 1 ＋ 69，反例类与对照之外 0，`exe:` 0，没有测试帧 0，7 个程序集各 1 条对照（`logs/task-35-violations.txt`）。**本项已按主控方处置 (A) 满足：第五个栈根已登记，原始失败和复跑证据保留，见 `verification/t009-closure.md` §2.3。**
5. 见下「第 5 条：热路径」，成立。
6. `git -c core.whitespace=cr-at-eol diff --check` 无输出；`git status --porcelain --untracked-files=all` 的 82 个文件都没有 CR，改动前各文件也都是 LF。
7. 收口引用核对（scratchpad `t35/refcheck.py`）：本单元写进文档的 `evidence/`、`logs/task-35-*` 路径、新用例名、F-130～F-132 行、变异脚本名，共查 201 处（含本节自身），缺的只有 1 个名字，出现在 2 处：`t006-test-dispositions.md` 里的 `OutboundSenderTests.A_batch_that_cannot_be_encoded_sends_none_of_it` 在今天的测试里找不到。它是 TASK-23 时写下的既有引用，不是本单元写的，按「只追加」没有改（本节引用它一次，所以计 2 处）。
- 测试结束后 `dotnet build-server shutdown`，`pgrep -x yes` 为 0，`testhost` 进程 0。

### 第 5 条：热路径

- 作业执行（`GameplayExecutor`、`GameplayJob`）、发件箱（`Application/Execution/Outbox/`）、发送端（`Networking/Outbound/`）本单元一行未改（`git diff 9aeb638c` 在这三处为空）。
- `TickAsync` 的改动在定时生产者一侧、作业结束之后：成功路径上只多一个异步状态机，失败路径多一个 catch。作业体本身（`Run`）不变，所以执行器量到的单条作业时长不受影响；定时作业约每秒 30 个（F-08），多出的开销不进执行器。
- `LoginSessionMaintenanceService` 只改了入队令牌，作业体相同；它 30 秒一次。
- XO-1 的改写语义等价：有序组在同一处结束，登记的条目与次序相同（B1 与新用例同为绿）。
- 删去的三处 catch 只在失败路径上。
- 所以 M-3（`2bdb1afd`）量到的执行器、发件箱、发送端与入站路径，在 TASK-35 之后行为与代价相同，M-3 的数字仍代表封存状态。

### 交接修正自检（2026-10-24）

- 按文末已授权处置 (A)，`AGENTS.md` 栈根表从四行改为五行，新增本单元 Joust 失败栈根；`t009-closure.md` §2.3 保留无交集、整项目复现 0／3 与触发源有交集但未观测的限定。
- F-130 已在本单元结果中记录 27 ＋ 1 ＝ 28 次清池调用，本次只追加第五栈根登记与 B 首单元优先修复的关联，不重复计数。
- 本次仅改四份文档，没有改生产或测试；原失败、备选处置和正式全量数字未删除。Result 仍待主控安排独立评审，不能据此宣称 Spec 已封存。
- `git -c core.whitespace=cr-at-eol diff --check` 无输出；文档定点自检确认栈根表五行、唯一 Result 且无未解除的 Blocker 标题、原日志含用例与 SQLite 栈根、四份文件 containment 与 LF 均成立。

### Remaining

- 唤醒式循环（`AltarScheduler`、`RoomMechanismMaintenanceService`）吸收失败后接着按 `next` 等待。黏滞程序错误时作业体已跑完，`next` 已算好；作业体直接抛出时 `next` 可能为空，要等下一次唤醒。基准上后一种情形同样会停宿主，不是退化；是否加退避交 B 或后续，未登记 F 编号。
- 评审 L-3（AGENTS.md「端到端示例：GameLogin」的旧名字）不在合同第 4 条的清单内，本单元没有改，仍归 F-128 ①。
- 合同里写的「约 15 个定时后台服务」，实际经 `TickAsync` 的是 20 个文件，另有绕过它的登录过期清理 1 个。

## 主控方处置（Blocker 之后，2026-10-24）

采用 **(A)**，沿用 TASK-34 的先例：把新栈根 `SqliteDatabaseParticipant.CommandAsync → BeginTransaction → ExecuteNonQuery`（见于 `JoustIntegrationTests.Statistics_failure_rolls_back_mail_completion_and_accounting_marker_then_retry_counts_once`）登记为 SQLite 并行释放偶发的第五个栈根。正式全量不重跑。

依据统一按 TASK-34 第 1 轮评审后的口径书写：
- 与失败路径无交集，复现 0/3；
- 新增的 `TimerSourceJobFailureTests` 每例调用一次进程级 `ClearAllPools`，与推断的触发机制（F-130）有交集，可能提高了出现概率，这一点是推断，没有观测到。

接手者要做的事：
1. 在 AGENTS.md「判据偶发失败」一节的栈根表追加这一行，表名「四条」改为「五条」；
2. 在 `t009-closure.md` 写明依据；
3. 把本文件的 `## Blocker` 改为 Result，并在 Result 中说明 Blocker 已按本处置处理；
4. F-130 补记：TASK-35 又新增 28 处清池调用，进一步说明子 Spec B 首个单元应先修 F-130。

本处置由主控方写下。用户在此时暂停了本会话，后续执行由接手的会话完成，交接见 `verification/handoff-2026-10-24.md`。

## 独立快审与主控处置（2026-10-24）

`reviews/task-35-result-review.md`（a_task35_quick，Round1，approved）已核对本Result及当前改动；三维完整结果Round2均approved。接受本单元结果，执行TASK-35检查点提交；新鲜构建/全量和Spec封存另行执行。主控将closure§7.1第五栈根引用补为§2.2/§2.3，未改历史运行事实。
