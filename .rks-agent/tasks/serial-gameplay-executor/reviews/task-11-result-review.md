---
kind: rks-review
target: tasks/TASK-11.md
scope: Task Unit result quick review（TASK-11 / T-004b4，Contract Revision 1 ＋「主控方裁定（执行前）」10 条，执行轮次：Result 第 1 版，未提交）
verdict: approved
date: 2026-10-22
---

# TASK-11 结果快速审查

## 审查对象与依据

- 目标：`tasks/TASK-11.md`（契约、Detailed Plan、待裁定、主控方裁定、Result）；HEAD `ca71eb26` ＋ 未提交改动：`git diff HEAD` 36 个文件（＋699 −255），其中 `Server/src` 25 个、既有测试 9 个、`task-07-conflicts.md`、`plan.md`；未跟踪的 `GameplayTimerWork.cs`、8 个新测试文件、`verification/logs/task-11-*`。
- 依据：`plan.md` 共享决定「定时源」；`spec.md` RQ-006、RQ-009、RQ-010、RQ-013～RQ-015、附录 A 第 5～7、11 行、附录 F；`TASK-08.md` 拆分表与裁定（第 254 行 RQ-013 时机口径）；`TASK-10.md` §9 分类与 Result；`AGENTS.md`。
- 引用的审查准则：reviewing-work 的 Task Unit、Code（局部范围）。
- 本审查未运行全量；自己做的检查见文末「审查者的独立检查」。

## 结论

没有阻断项。生产改动与契约、裁定一致：22 个托管服务、5 个调度任务、Joust 启动恢复、两种卡牌计时器都把一次运行的整段逻辑放进执行器作业；卡牌协调器的排空与 `Dispose` 均不取锁；冲突用例的改写已登记，`plan.md` 第 54 行只改了措辞。下面 7 条非阻断项都是证据表述或测试强度的问题，没有一条构成本单元的生产缺陷。

## 重点逐项

### 1. 逐服务迁移（全部 22 个服务、调度器、协调器都读过 diff）

- **一次 tick 的整段逻辑都在作业内，作业外只剩「等下一次到期」**：
  - Fatigue、Premium，以及 Mirror、Dimension、GoldSilverHorn、ReviveCoin、AltarQuota、TournamentQuota、Heras、ItemUsage（共 10 个 `*DailyRefreshService`）：原 `RefreshAsync` 的体逐字改名为 `RefreshCoreAsync`，`delivered` 字典、快照读取、会话门、发送都在体内。循环里只有 `WaitForNextTickAsync`。
  - `DungeonPartyWipeService`、`TournamentRewardSweepService`、`CreatureClockService`、`WeaknessRecoveryService`、`MarriageWeddingScheduler`：扫描（`FindExpired*`、`PendingRewards`、`DueSessions`／`DrainProjections`、`AdvanceWeaknessRecoveryCommand`、`ActiveWeddingsAsync`）连同逐项的门与命令，整体搬进私有 `TickAsync`。
  - `AltarScheduler`、`RoomMechanismMaintenanceService`：`Pending()`、`AdvanceRoomMechanisms`、投影门、`DirectAsync`、`delivered` 写入都在作业内，`next` 经闭包带出，作业外只有 `wake.WaitAsync`。
  - 两塔：作业依次执行 `DrainDeparturesAsync`、`SweepAsync`；循环的 `catch` 留在原位。
  - Joust：`ticks` 信号量在 `TickCoreAsync` 内取；周期与启动两种 CMD 共用同一个作业体（周期经合并源，启动直接入队，来源 `StartupRecovery`）。
  - 重试排空：`drain.DrainAsync` 是作业体，`catch` 与日志在作业外。
  - 调度任务：scope 的创建、释放和 `Execute` 在作业内；取槽、`Started`／`Requested` 的加锁、日志、重试间隔在作业外，顺序未变。
- **门与事务都在作业内取得**：所有 `transitions.AcquireAsync`、`projections.AcquireAsync`、`gate.AcquireAsync`、`commands.SendAsync` 都在作业体里。管线事务随过渡期的内联 `SendAsync` 落在执行器上。
- **E1 的 27 行**：经真实入口驱动——`TimerSourceHost` 用生产组合，只启动被测的托管服务，推进 `SchedulerClock`；Joust 启动那一行走真实 `StartAsync`。断言在执行器上也成立。但这里的「在执行器上」记录于作业入口，强度不够，见非阻断项 N1。

### 2. 停机语义

- **主控方对裁定第 7 条读法的确认是正确的**：
  - 以 `CancellationToken.None` 入队，宿主停机路径（`StoppedAsync` → 排空 → `executor.StopAsync` 执行完队列）上，已入队的定时 CMD 一定开始执行。例外只有执行器 `DisposeAsync` 超时放弃，以及循环故障；
  - 作业体拿生产者的停止令牌，调度任务卡住的长等待因此可以取消（改写后的 `Blocked_task_postpones…` 守着）；
  - 卡牌计时器同样以 `None` 入队，入队前再检查一次计时器令牌，所以排空之后不会再入队；
  - 风险评估见 N2。
- **逐个看「作业体开头就检查令牌」的位置**，停机时它们会让已入队的 tick 实际不做事：
  - 调度任务：`token.ThrowIfCancellationRequested()`；
  - 卡牌布局与自动翻牌：`cancellationToken.ThrowIfCancellationRequested()`；
  - Joust：`ticks.WaitAsync(ct)`；
  - 每日刷新、生物、衰弱、婚礼、比武、全灭：第一次取门或发命令时就会因令牌而抛出。

  这些令牌在迁移前就是同一个 `stoppingToken`／计时器令牌，用在同样的位置。所以没有哪一处「迁移前会完成的状态写入」被新跳过。持久写都在 Transactional 命令里，中途取消会回滚；内存态随进程结束。
- **执行器能否排空**：托管服务的循环在 `StopAsync` 期间等待自己那次 tick（`None` 入队，不可取消），执行器此时仍在运行（`GameplayExecutorLifetime` 只在 `StoppedAsync` 动作），所以能排空。代价见 N2。
- **卡牌协调器排空**：
  - `DrainAsync` 置位、取消全局 CTS、按在途计数用 TCS 等待，全程不取 `gate`；
  - `StartDetached` 同步递增计数，自动翻牌在布局作业内启动时，布局自己的计数仍在，所以排空不会在两者之间漏判；
  - `Dispose` 只做 `Stop()`，不取锁；
  - C7 断言「排空等已在等作业的计时器，放行后返回，之后不再入队」，C5 在真实宿主上断言计时器在执行器停止前被取消；
  - V6 下 C5、C6 都红，原因与 Result 的解释一致；
  - 有一处排空次序依赖，Result 没写，见 N3。

### 3. 合并（RQ-006）

- 周期循环一律经各自的 `CoalescingGameplaySource`。循环在等这次 tick 完成之后，才进入下一次 `WaitForNextTickAsync`，所以循环自身不可能在上一次未完成时再入队。也正因如此，合并源在顺序循环里实际上从不触发「合并」，真正起作用的是「等完成」（V2 的变异正是去掉等待后 E3 变红）。
- 直接入队的入口（10 个 `RefreshAsync`、Joust `TickAsync`、两塔 `TickAsync`）不合并，理由成立：这些入口与循环是不同来源，经合并源会被静默吞掉。生产代码中唯一的直接调用是 Joust `StartAsync`，它在 `base.StartAsync` 启动循环之前完成，不会与循环重叠（检索：`Server/src` 中其余 `RefreshAsync(` 命中都属于无关的领域服务）。所以 RQ-006 在生产中成立。
- 调度任务不另加合并源：`job.Execution` 非空时按 `MissedRun` 记 `Pending`，同一任务至多一个执行。成立。

### 4. 冲突用例改写

- `:109` 改名为 `Party_auto_flips_run_one_after_the_other_and_award_each_member_once`，「旧断言 → 新断言 → 意图」已登记在 Result 与 `task-07-conflicts.md` §9。旧的「争用后重试」交给 `:235` 守，理由成立（串行执行下，两名成员的自动翻牌不再可能争用）。小瑕疵见 N4、N6。
- `ScheduledTaskTests.Different_task…` 改名为 `Blocked_task_postpones_rather_than_loses_another_and_stop_cancels_it`，在 Result（Changed 与 Why）和冲突清单中都明确登记为「RQ-013 规定的意图变化」，写出了旧断言 → 新断言 → 理由。新断言与裁定第 6 条的「推迟而不丢失」一致。原计划 §8.4 写的「排队任务随令牌取消、不执行」已被裁定第 7 条推翻，Result 没有沿用旧写法，这是对的。
- 两条「断言前加等待」（`Early_scroll_request…`、`Early_party_layout_requests…`）：diff 只多出一行 `await WaitUntilAsync(...)`，原 `Assert.Equal(n, Pending(4s))` 原样保留（差异核对中这两行在多重集合里仍在）。只改了等待。
- `NotificationRegistrationTests` 补 `AddGameplayExecution()`：只改测试组合，断言的多重集合不变（16＝16），属于 Owned boundary 中「按 §9 处置的既有测试文件」。生产组合 `ApplicationModule` 同时调用 `AddTaskScheduling`／`AddCommandDispatching` 与 `AddNotificationDispatching`（第 101、875 行），不受影响。依赖缺口已在 Remaining 第 7 条登记。不越界。

### 5. 探针

- §3.8 的 12 条记录改归 §3.9，这个归因可信：
  - Joust 的启动 CMD 由执行器 TCS（`RunContinuationsAsynchronously`）在线程池上完成；
  - 续体同步恢复 Joust `StartAsync`，再同步回到 `Host.StartAsync` 的 foreach，调用下一个托管服务 `GmBootstrapService.StartAsync`；
  - 因此 Joust 的帧在栈底，而 `fam.py` 按最外层入口帧分组。

  旁证：
  - TASK-10 Host 原始记录中 `entry Host.StartAsync` 有 280 条，本单元为 120 条，同时新出现 12 条 `entry JoustMaintenanceService.StartAsync`；
  - TASK-10 §3.9 中有 GmBootstrap 264 次命中；
  - 12 条的命中合计为 P1 45＋25＋7＋4＋4＋1＝86、P3 同为 86，86＋86＝172，与汇总一致。

  证据上的缺口见 N7。
- 其他族：§3.9 命中 654 → 423（改正后），差额主要就是 `Dispose` 的 216（654 − 216 ＝ 438，余 15 属波动）；§3.10 下降已按要求说明。§3.5、§3.7 的变化只需并列写出，不要求归因。

### 6. 数字、日志与更正

- 全量日志逐项目的总数与 Result 一致：50＋80＋1701＋475＋969＋173＋4487＋166＝8101。失败只有 Application 的 2 条，另有 Infrastructure、Networking 各 2 条跳过。构建 0 警告 0 错误。
- 变异日志 V1～V8 的红绿与 Result 一致，每项都有 `cmp-equal=True`。
- 耗时比对：自行解析两份日志，≥9 秒的增量前五条与 Result 列出的 5 条完全一致（＋15／＋13／＋12／＋10／＋10 秒）；隔离复跑日志里它们都在 2 秒以内。
- **对 TASK-10 HttpApi/Pvf 时间的更正成立**：TASK-10 Result 第 553 行写「Pvf … → 8.6 s；HttpApi … → 11.2 s」，而 `task-10-build-test.txt` 中 50 条（HttpApi）那块是 8.6202 秒，80 条（Pvf）那块是 11.2472 秒，确实写反了。
- `plan.md` 第 54 行：旧文「调度任务去掉 `Task.Run`」，新文「每次尝试作为执行器作业；`Task.Run` 外壳与并发槽在 T-005 随锁一起删除」。新文与 TASK-08 第 254 行的裁定、附录 F「删除」栏、计划中 T-005 一行（「并发槽」）一致，终态要求（RQ-013）不变，只改措辞。

## 非阻断项

**N1. E1 不区分「作业体在执行器上」与「只是入队了一个作业」。**
- 现象：`RecordingGameplayQueue` 在包装后的作业入口读取 `IsOnExecutor`／`Current`。凡是入队到执行器的作业，这一项都恒为真。因此 E1 证明的是「该源入队了该类型、该来源的 CMD，并且完成了」，证明不了「tick 的逻辑在作业里」。
- 实验：把 `DungeonPartyWipeService` 改成入队一个空作业、扫描在作业外执行，E1 的全灭那一行仍然通过（见文末）。
- 「整段在作业内」今天靠的是：代码审读（本审查逐个读过）、E2a～E2d 对 4 个服务的下游证据，以及探针 §3.8 归零。
- 影响：Result 的「E1 27 行逐行证明」与对账行把 E1 写成了逐行的「整段在执行器上」证据，说过了头。今后若有人只靠 E1 判断一处迁移是否完整，会漏掉「空作业」形状的回退。
- 最小修正：在 Result Checks 10 与对账行注明 E1 是入队证据，「整段在作业内」的证据是代码审读 ＋ E2 ＋ 探针。

**N2. 裁定第 7 条的读法正确，但「执行完」只在作业层面成立；停机多了一段不可取消的等待。**
- 已入队的 tick 会开始执行，但多数作业体在第一处令牌检查就退出（见重点 2）。它的实际效果与原方案「撤回排队的 tick」几乎相同，裁定理由里「宁可多跑一次 tick」在多数服务上并不会真的多跑。
- 新代价：托管服务的 `StopAsync` 现在要等自己那次 tick 排到并结束。执行器上若有长作业（例如每日任务遍历全部在线会话），停机会相应变长，上限是宿主的 `ShutdownTimeout`（`BackgroundService.StopAsync` 到期即放弃）。
- 不成环：等待中的作业体都会观察各自的停止令牌，执行器上的作业也不等托管服务。
- 建议主控方把这一读法写进裁定第 7 条正文。现在的字面「停止令牌只用来停止下一次等待到期和入队」与实现（作业体拿停止令牌）相矛盾。Result 已在 Why 第 1 条、Remaining 第 6 条标明。

**N3. 协调器排空与传输排空的次序其实有关系，Result／计划的「谁先谁后都不影响结果」不准确。**
- 若协调器排空先于 `TouchSocketTransportDrain` 执行，仍在执行器队列里的入站结算 CMD 稍后执行，会调用 `ScheduleAfterSettlement`／`OpenOnRequestAsync` → `Replace`，在业务 CMD 内抛出 `ObjectDisposedException`。
- 今天的组合是 `GameServerHostingExtensions.cs:19` 先 `NetworkingModule` 后 `ProtocolModule`，传输排空先跑，并等待所有拆除 CMD 完成；按 FIFO，在它们之前入队的入站 CMD 也已执行完。所以当前是安全的。
- 但没有判据钉住这一次序。影响限于停机窗口。
- 建议在 Result 或注册行旁注明这一依赖。

**N4. `:109` 改写新增的 `SelectCardResponse` 断言与等待条件之间有一个窄竞争。**
- `CardCommandBus.SendAsync` 在返回之前就递增 `Selections`，随后协调器才在世界执行器上执行 `projection.DeliverAsync`、发出 `SelectCardResponse`。
- 用例等到 `Bus.Selections == 2` 后，在另一个执行器上立即断言每人一帧 `SelectCardResponse`，两者可能并发。
- 10 轮加全量都通过，触发概率很低。
- 最小修正：等待条件改为「两人各收到一帧 `SelectCardResponse`」。

**N5. `:149` 的后继用例对「扫描以当前状态复核」的区分力有限。**
- tick 运行时，旧 run 已经经 handler 回城，新 run 没有到期的回城。即使扫描不复核，也找不到候选，用例同样会通过。
- 它符合计划 §8.3（已获批），原交错在串行下也不再可能发生，记作 T-004c 承接时的注意点。

**N6. 文档标签与登记有两处小出入。**
- Result §9 主动复核表用的 (i)～(vi) 实际是本单元 §9 的规则编号：(ii)＝包裹、(iv)＝加等待、(v)＝新交错冲突。而 `task-07-conflicts.md` §9 按 TASK-10 的分类，把同一条 `ScheduledTaskTests` 记为「第 (ii) 类」。在 TASK-10 的分类里，(iv) 指「本单元改动的缺陷」。读者对照两处会混淆。
- `:109` 在冲突清单与 Result 中都没写明：建世界、调度与断言块被移进了 `TestGameplay.Run`（`:235` 那一行写了）。

**N7. 探针改归的直接证据没有落盘。** `task-11-probe-Host.txt` 中那 12 条 (A) 记录只有表头，写的是 `entry JoustMaintenanceService.StartAsync (nearest UnitOfWorkBehavior…)`，全文件没有 `GmBootstrapService` 字样。「最近入口帧是 GmBootstrap」出自 scratchpad 里的 `probe-analyze.py`，它的输出未保存。上面的旁证足以支持这一归因。建议把这 12 条的栈或分析输出落进 `logs/`，供 TASK-13 复核。

## 审查者的独立检查

- 构建：`dotnet build` 两个测试项目（Release），0 警告 0 错误。
- 定向测试（未跑全量）：
  - Application 141 条全部通过：`TimerSource*`、`ScheduledTask*`、`NotificationRetryDrain*`、`JoustIntegrationTests`、`IllusionTowerPublicFlowTests`、`TournamentRewardSweepTests`、`OnlineExpiredItem*`；
  - Protocol 123 条全部通过：`CardReward*`、`UsageQuotaProjectionTests`、`ItemUsageProjectionTests`。
- 临时变异（N1）：
  - 做法：备份到 scratchpad `t11review/DungeonPartyWipeService.cs.bak`；把循环改为「`TickAsync(ticks, …, _ => ValueTask.CompletedTask, …)` 之后在作业外 `await TickAsync(stoppingToken)`」；构建后运行 E1 全灭行，结果通过（1／1）。
  - 还原：从备份复制回原位，`cmp` 相等；文件 sha1 前后都是 `761905066c702ca054ed3d755c498f817d9f9f1c`；`git status --short | shasum` 前后都是 `f2984b5b04f0db4daab96de74eaf80961faefb39`；`git diff HEAD --stat` 仍为 36 个文件、＋699 −255；还原后重新构建，0 警告 0 错误。
- 未 commit，未 stash，只写了本审查文件。

```yaml
kind: rks-review
verdict: approved
```
