# TASK-11 — T-004b4：定时源迁入执行器（后台服务、调度任务、两塔离场、卡牌计时器）

基准提交 `ca71eb26`（TASK-10 之后）。边界提纲见 `TASK-08.md` 文末「T-004b 拆分」§2 TASK-11 一行、§1 的跨单元规则和其后的主控方裁定（RQ-013 的时机口径）。TASK-10 的 Result、裁定第 5 条（卡牌协调器 `Dispose`），以及 TASK-06 主控方补充 N-3，是本单元的输入。本文件不重抄这些内容，只写本单元的做法。

## Task Unit Contract

- Revision: 1
- Outcome and references:
  - **定时源成为执行器上的定时 CMD**：§1 表中 22 个托管服务（含 5 个调度任务、两塔离场队列的消费方）与 2 种卡牌延时计时器，每次到期或被唤醒，都把这一次运行的整段逻辑作为一个执行器作业入队，并等待它完成。托管服务的外壳只负责按节奏入队，继续作为生产者存在。周期循环经 `CoalescingGameplaySource` 入队（RQ-006 接线）。调度任务的 `Task.Run` 外壳与并发槽按 TASK-08 裁定保留到 T-005；每次运行都在执行器上，所以语义上已经串行（RQ-013）。
  - **卡牌计时器是分离点**（TASK-06 待办 3e 的第三项）：计时器在 `ExecutionContext.SuppressFlow` 下启动，不继承调度它的作业的令牌与帧，到期后入队定时 CMD。
  - **卡牌协调器的停机与 `Dispose`**（TASK-10 裁定第 5 条）：协调器实现 `IGameplayProducerDrain`，停机时在执行器停止之前取消待触发的计时器，并等在途的回调结束。`Dispose` 不再取锁。附录 B 不新增例外。
  - **Joust 启动静默恢复**：随周期 tick 一起迁入，成为 `startup:` 启动恢复 CMD（待裁定第 1 条）。
  - **冲突用例**：`DungeonCardPresentationCoordinatorTests` 2 条（Protocol `:109`、`:235`）、`IllusionTowerPublicFlowTests:149`，按 §8 处置。
  - 覆盖：
    - 附录 A 第 5、6、7 行，以及第 11 行中的 Joust 部分（待裁定第 1 条）；
    - RQ-006（接线；机制由 TASK-04 的 `CoalescingGameplaySourceTests` 守着）、RQ-013、RQ-010 的定时部分；
    - AC-004；AC-006 中附录 A 第 5、6、7 行的对账；
    - RQ-009 中「生产者在执行器停止前安静下来」这一半，适用于卡牌计时器；托管服务一侧由宿主停止次序保证，见 §6。
- Owned boundary:
  - 生产代码，只限下列文件，每个文件只改其中的定时源类型：
    - Application：
      - `Execution/` 下新增一个定时 tick 辅助文件（§2.2，可选）；
      - `Scheduling/ScheduledTaskHostedService.cs`、`Scheduling/ScheduledTaskExtensions.cs`；
      - `Events/JoustMaintenanceService.cs`；
      - `Notifications/NotificationRetryDrainService.cs`。
    - Protocol（`Game/` 下）：
      - `CharacterDeath/DungeonPartyWipeService.cs`、`Fatigue/FatigueProjection.cs`、`Weakness/WeaknessRecoveryService.cs`；
      - `Creatures/CreatureRuntimeDelivery.cs`（只改 `CreatureClockService`）；
      - `StateSynchronization/` 下的 `PremiumDailyRefreshService.cs`、`UsageQuotaProjection.cs`（只改 `MirrorQuotaDailyRefreshService`）、`ReviveCoinQuotaDailyRefreshService.cs`、`GoldSilverHornQuotaDailyRefreshService.cs`、`DimensionQuotaDailyRefreshService.cs`、`ItemUsageProjection.cs`（只改 `ItemUsageDailyRefreshService`）；
      - `Altar/AltarScheduler.cs`、`Altar/AltarQuotaDailyRefreshService.cs`；
      - `Tournament/` 下的 `TournamentQuotaDailyRefreshService.cs`、`HerasQuotaDailyRefreshService.cs`、`TournamentRewardSweepService.cs`；
      - `MarriageProposal/MarriageWeddingSlice.cs`（只改 `MarriageWeddingScheduler`）；
      - `RoomMechanisms/RoomMechanismProjection.cs`（只改 `RoomMechanismMaintenanceService`）；
      - `BossTowerProjectionDelivery.cs`、`IllusionTowerProjectionDelivery.cs`（只改两个 `*ReturnSweepService`）；
      - `CardReward/DungeonCardPresentationCoordinator.cs`。
    - `Dfo.Server.Protocol/ProtocolModule.cs`：只加一行，把协调器单例注册为 `IGameplayProducerDrain`。
    - §9 第 (iv) 类确需的缺陷修正，限于上述文件。
  - **不改**：`GameplayExecutor`、`CoalescingGameplaySource`、`GameplayExecutorLifetime`、`LoginSessionRuntime`、`NetworkPlugins`、`DomainEventDispatcher`、GmWeb、`GameServerStartupService`、HttpApi，以及各定时源调用的领域服务与投递类。
  - 测试：
    - 新增：`Application.Tests/Execution/`、`Application.Tests/Scheduling/`、`Application.Tests/Notifications/`、`Protocol.Tests/Game/CardReward/` 下的测试文件；`IllusionTowerPublicFlowTests.cs` 里新增一个后继用例。
    - 按 §7、§8、§9 处置的既有测试文件。
  - 文档：
    - `verification/` 下的 `logs/task-11-*.txt`；
    - `task-07-conflicts.md` 追加 §9：3 条冲突用例的去向，以及本单元新判定的冲突（§8.4）。
- Required checks:
  1. `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 个警告，0 个错误。
  2. **全量**：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘到 `logs/task-11-build-test.txt`。
     - 总数 ＝ 8060（TASK-10 Result Checks 2）＋ N（新增）－ D（删除）。逐项目与 TASK-10 对照，写出加法。
     - 失败只能是那 2 条 `DimensionDungeonTests`。跳过 20 条；新增的 Skippable 用例如果在本机跳过，另写加法。表征测试 8 通过、2 跳过。没有挂起转储。
     - **日志信号计数**：
       - `direct send deadline exceeded`、`did not drain within`、`GameplaySelfWait`；
       - 以 `Gameplay timer:` 或 `Gameplay startup:` 开头的 `failed`；
       - `Draining gameplay producer … failed`；
       - `Dungeon card layout timer failed`、`Dungeon card auto flip failed`；
       - §5.4 新增的「执行器已停止，计时器未入队」日志。
       - 每一项非零的都要逐条说明来源。
     - **耗时比对**：与 `logs/task-10-build-test.txt` 逐条比，列出多出 ≥ 9 秒的用例，逐条判定：负载波动、释放时的 10 秒排空、作业里的 SQLite 忙等，或本单元引入的「定时作业排在测试作业之后」。写出各项目总耗时的对照。
  3. **隔离复跑**：Application.Tests、Protocol.Tests、Networking.Tests、Host.Tests 各一次，落盘到 `logs/task-11-isolated.txt`。超时形态的失败先按 AGENTS.md「满载假红」的规则隔离复跑，再归类。
  4. **新增测试 ×10**：§7 的全部新增用例隔离连续跑 10 次，在全量负载下再跑 1 次，全部通过，落盘到 `logs/task-11-new-x10.txt`。需要 PVF 的用例在本机没有档案时跳过，须在 Result 写明。
  5. **表征测试**：8 通过、2 跳过，断言未改。
  6. **变异核对**：每项都用 scratchpad 备份，逐字节还原后 `cmp`，再重新构建。
     - V1：`FatigueDailyRefreshService.RefreshAsync` 改为直接调用作业体，不入队。E2a 与 E1 中 Fatigue 那一行变红。
     - V2：周期循环绕过合并源，改为不等待地 `queue.RunAsync`。E3 变红。
     - V3：调度任务的一次尝试改回在执行器外执行。E2c 变红。
     - V4：卡牌布局计时器到期后直接调用 `OpenLayoutAsync`，不入队。C1 变红。
     - V5：去掉计时器启动处的 `Task.Yield`。C3 变红（自我等待）。
     - V6：删掉 `ProtocolModule` 里的排空注册。C6 变红；C5 是否变红，Result 里写明原因。
     - V7：`NotificationRetryDrainService` 的一次排空改回在执行器外。E2d 变红。
     - V8：Joust 的 `TickAsync` 改回在执行器外执行作业体。E1 中 Joust 两行变红。
     - 「`Dispose` 不取锁」没有可观察的测试替身，由 Required check 9 的探针 §3.9 计数证明。
  7. **测试改动核对**：
     - 新增测试逐条列名，N 写出加法。
     - §9 处置过的每个既有测试文件写明类别与理由。只改构造实参、包裹方式或夹具开关的文件，用 TASK-07 的差异核对脚本证明方法集合与断言行多重集合不变。
     - 改写的冲突用例写成「旧断言 → 新断言 → 保护意图」。顺延的冲突用例，给出它在本单元全量中「已通过」的记录和后继用例。
  8. **生产改动的边界与不变量**：
     - `git diff --stat -- Server/src` 只含 Owned boundary 所列文件。
     - `grep -rn "IGameplayQueue" Server/src`（排除 bin/obj）：与 TASK-10 Result Checks 9 相比，新增的命中只在本单元的文件里。
     - `grep -rn "SuppressFlow" Server/src`：新增的命中只有协调器一处。
     - `grep -rn "RunDetached" Server/src`：与 TASK-10 相同。
     - `grep -n "Task.Run\|SemaphoreSlim" Server/src/Dfo.Server.Application/Scheduling/ScheduledTaskHostedService.cs`：两者仍在，留给 T-005。
     - **部分迁移期间的不变量**（拆分原则第 3 条）：逐个列出等待定时作业的执行器外代码，确认它们都没有「持门等待已迁入的入口」。这些代码是：各托管服务的循环、调度任务的 `RunAsync` 任务、卡牌计时器的分离任务、Joust 的 `StartAsync`、协调器的排空。其中调度任务在等待作业时占着并发槽。并发槽不是门：执行器上的作业从不等它，所以不构成环。Result 要写明这一点。
  9. **探针复跑**（`verification/probe-tools/`，授权见下）：
     - 项目：Application、Protocol、Host、Networking。原始记录 `PROBE_LOG_PREFIX=task-11-probe`，落盘到 `logs/task-11-probe-<Project>.txt`。
     - **本族归零**（以 TASK-10 p2 为基线）：
       - §3.6 的卡牌计时器族：24／141 → 0；
       - §3.8 中入口帧属于 §1 表的 22 个服务（含 `*.RefreshAsync`、`TickAsync`、`ScheduledTaskHostedService.*`、`OnlineExpiredItemCleanupJob.RunAsync`、`TournamentRewardSweepService.SweepAsync`）的记录为 0；
       - §3.9 中 `DungeonCardPresentationCoordinator.Dispose` 的 55／216 → 0；
       - `JoustMaintenanceService.StartAsync` 为 0（以待裁定第 1 条通过为前提）。
     - 残留记录只允许来自 §8 中顺延的冲突用例（`:149` 及它带出的 (C) 栈顶 `SelectDungeonHandler.HandleMessageAsync`、`IllusionTowerProjectionDelivery.LeaveAndReturnAsync`），并用排除复跑证明。排除之后仍有的，按缺陷处理。
     - 本族以外各族的 (A) 计数，与 TASK-10 p2 并列写出，作为 TASK-12～14 的输入。§3.10 命令总线族里，调度任务的命令原先计在 §3.8，迁入之后不再出现；如果 §3.10 下降，要说明原因。
     - 还原证明的三项同 TASK-07 Required check 6；之后先 `--no-incremental` 重新构建，再跑正式全量。
  10. **对账行**：
      - 附录 A 第 5、6、7 行，以及第 11 行的 Joust 部分，按 §1 表逐项写成「来源 → CMD 类型 → 证据（测试名或探针计数）」，标明哪一部分留给 T-005、T-006、T-007；
      - 写出分离点清单（3e）在本单元之后的现状；
      - 写出 5a 中 `CreatureRuntimeDomainService.cs:274` 的现状：它的调用入口 `CreatureClockService` 已在执行器上，反例仍归 T-004c 的判据。
- Dependencies：
  - TASK-10：执行器生命周期服务、`IGameplayProducerDrain`、关连接器的 `SuppressFlow`、探针基线；
  - TASK-09：dispatcher 在执行器上，卡牌计时器由入站作业调度；
  - TASK-08：容器执行器注册、直发 2 秒期限、探针工具；
  - TASK-04：`GameplayExecutor`、`CoalescingGameplaySource`、作业令牌；
  - TASK-06、TASK-07：`TestGameplay` 与冲突总清单。
- Result review: required。
  - 改动跨两个项目约 27 个生产文件、一处 `ProtocolModule` 接线，以及十几个测试文件。
  - 协调器的停机语义改为排空并取消，`Dispose` 不再取锁。T-004c 开启判据时直接依赖这一点；做错了，要到那时才以「停机时在执行器外取锁」暴露。
  - 每个服务里「哪些留在作业外」是逐个判断出来的。漏放一处读或写，全量测试大多仍是绿的，只有探针与 E1 的逐行对账能看出来。而探针本身会被 40 帧上限截断。
  - 冲突用例的改写，以及 `ScheduledTaskTests` 中那条用例被规格反转了保护意图（§8.4），都要靠人来判断。
- Task-specific authorization:
  - 探针复跑（Required check 9）与变异核对（Required check 6）期间，允许临时改动 `Server/src`，用后按三项还原证明。探针与变异的改动不提交，不 `git stash`。
  - 只允许删除或改写「因本单元的改动而失败或挂起」的用例，每条都在 Result 中列出；其余冲突用例保持原样，留给所属单元。

## Detailed Plan

### 1. 定时源清单与迁移形状

`research/01` §3 的 33 个托管服务按归属分开（加法见表后）。本单元负责的 22 个如下。

- 「门」一栏的简称同 `research/01`：会话门＝`SelectedSessionTransitionGate`，投影门＝`DungeonProjectionGate` 与两塔的投影门、进入门。「管线」指命令管线里由命令声明决定的会话门、账号门、全局门与工作单元。
- CMD 类型一律 `NewRoot(Timer)`，Joust 启动那一行例外。

| # | 服务（位置） | 触发 | 每次运行做什么 | 今天取的门／信号量 | 迁移形状：作业粒度 ／ CMD 类型 ／ 合并源 ／ 入口 |
|---|---|---|---|---|---|
| 1 | `ScheduledTaskHostedService`（Application/Scheduling） | 单个计时循环按各任务的 schedule 到期；`Task.Run` 启动，最多并发 4；失败按 `RetryDelay` 重试 | 每次尝试新建 DI scope，调用该任务的 `Execute`；5 个任务见下表 | 自有并发槽 `SemaphoreSlim`；任务内部经管线取门 | 一次尝试一个作业，作业内含 scope 的创建与释放；重试间隔与取槽留在作业外。CMD 类型 `timer:ScheduledTaskHostedService/<任务 ID>`。不另加合并源：调度器已经保证同一任务同时至多一个执行（`Execution` 非空时按 `MissedRun` 记 `Pending` 或丢弃），这就是该任务的 RQ-006 合并。入口是 `RunAsync` 内部 |
| 2 | `NotificationRetryDrainService`（Application/Notifications） | `PeriodicTimer(RetryInterval)` | `INotificationRetryDrain.DrainAsync`：重发运输层重试队列 | 无（读在线身份） | 一次排空一个作业；`timer:NotificationRetryDrainService`；循环持有合并源。`catch` 与原日志留在作业外，等待 `Completion` 的地方。整体随 T-006 删除 |
| 3 | `JoustMaintenanceService`（Application/Events）周期 | 每秒 | `TickAsync`：推进 `AdvanceJoustLifecycleCommand`，`DirectAsync` 公告 | 自有 `ticks` 信号量；管线（全局门） | `TickAsync` 本身成为入队入口，作业体是它今天的内容，`ticks` 在作业内取；`timer:JoustMaintenanceService`；循环经合并源调用 |
| 3′ | 同上，`StartAsync` 静默恢复 | 宿主启动 | `TickAsync(suppressNotifications: true)` | 同上 | `startup:JoustMaintenanceService`，来源 `StartupRecovery`，同一个作业体（待裁定第 1 条） |
| 4 | `DungeonPartyWipeService` | 每 250 ms | 扫描到期的通关返回与全灭返回，逐个 `ReturnAsync`：投影门 → 各账号会话门 → `EndClearReturn` 或 `EndPartyWipe` → 取消卡牌、移出投递注册 → 回城投影；祭坛与比武分支 | 投影门 ＋ 会话门；管线 | 一次 tick（两次扫描加全部返回）一个作业；`timer:DungeonPartyWipeService`；合并源 |
| 5 | `FatigueDailyRefreshService` | 每分钟 | `RefreshAsync`：对在线快照逐个取会话门，读疲劳余额，有变化才 `SendNotiAsync` | 每会话一次会话门 | `RefreshAsync` 成为入队入口，作业体是它今天的内容；`timer:FatigueDailyRefreshService`；循环经合并源调用 |
| 6 | `PremiumDailyRefreshService` | 每分钟 | `RefreshAsync`：逐会话取会话门，`ReadPremiumSnapshotCommand`，按差异发 NOTI／CMD | 会话门；管线 | 同第 5 行；`timer:PremiumDailyRefreshService` |
| 7～13 | `Mirror`、`Dimension`、`GoldSilverHorn`、`ReviveCoin`、`AltarQuota`、`TournamentQuota`、`HerasQuota` 这 7 个 `*DailyRefreshService` | 每分钟 | `RefreshAsync`：逐会话取会话门，`ProjectAsync` 配额视图，`Delivered` 才记本期已送（DR-6） | 会话门 | 同第 5 行；`timer:<各自类型名>` |
| 14 | `ItemUsageDailyRefreshService` | 每分钟 | `RefreshAsync`：逐会话取会话门，发使用次数与冷却 | 会话门 | 同第 5 行；`timer:ItemUsageDailyRefreshService` |
| 15 | `WeaknessRecoveryService` | `RecoveryInterval` | `AdvanceWeaknessRecoveryCommand`；对变化的角色取会话门，`SendCurrentAsync` | 管线；会话门；投递的 `deliveryGate` 信号量 | 一次 tick 一个作业；`timer:WeaknessRecoveryService`；合并源。`deliveryGate` 在作业内不会被争用，留给 T-005 |
| 16 | `CreatureClockService` | 每秒 | `DueSessions` 逐个取会话门并 `AdvanceCreatureClockCommand`；`DrainProjections` 逐个取会话门发包，失败就关连接 | 会话门；管线 | 一次 tick 一个作业；`timer:CreatureClockService`；合并源。关连接走 TASK-10 的分离关闭 |
| 17 | `AltarScheduler` | `AltarWakeSignal`，或最近的截止时间 | 对到期项逐个 `AdvanceAsync`：投影门 → 会话门 → `AltarActionCommand` → 投影 | 投影门 ＋ 会话门；管线 | 循环体（处理到期项、计算下一个截止时间）一个作业，返回下一个截止时间；`wake.WaitAsync` 留在作业外；`timer:AltarScheduler`；合并源 |
| 18 | `MarriageWeddingScheduler` | 每秒 | 逐个进行中的婚礼：取双方会话门，`AdvanceMarriageWeddingCommand`，广播位置、过场、`MoveAsync`（`CrossInto`）、清理命令 | 会话门；管线 | 一次 tick 一个作业；`timer:MarriageWeddingScheduler`；合并源 |
| 19 | `TournamentRewardSweepService` | 每 50 ms | `PendingRewards` 逐个 `SweepAsync`：投影门 → 会话门 → 逐行 `SelectTournamentRewardCommand` → 显式发送 | 投影门 ＋ 会话门；管线 | 一次 tick（扫描加全部候选）一个作业；`timer:TournamentRewardSweepService`；合并源。空闲时每秒 20 个空作业，见 §5.3 |
| 20 | `RoomMechanismMaintenanceService` | 唤醒信号，或最近的机制截止时间 | `AdvanceRoomMechanisms` 直接改运行态；对变化的视图取投影门，`DirectAsync` 发包 | 领域内的短锁；投影门 | 循环体一个作业，返回下一个截止时间；`delivered` 字典只在作业里读写；`wake.WaitAsync` 留在外面；`timer:RoomMechanismMaintenanceService`；合并源 |
| 21 | `BossTowerReturnSweepService` | 每 500 ms | `DrainDeparturesAsync`：排空离场队列，`ProjectDisconnectedActionAsync`；`SweepAsync`：到期回城与结算 | 投影门 ＋ 进入门 ＋ 会话门（在投递里） | 一次 tick（排空离场队列，再扫描）一个作业；`timer:BossTowerReturnSweepService`；合并源。另加一个公开 tick 入口，供 §8.3 的后继用例使用 |
| 22 | `IllusionTowerReturnSweepService` | 每 500 ms | 同第 21 行 | 同第 21 行 | 同第 21 行；`timer:IllusionTowerReturnSweepService` |

**调度任务的 5 个注册**（第 1 行展开）。每个都是在作业里对在线快照逐个 `SendAsync` 一条 `Transactional` 命令，命令声明了会话与账号独占：

| 任务 ID | schedule | 命令 |
|---|---|---|
| `character.name-tag.expiration` | 每分钟 | `ExpireCharacterNameTagCommand` |
| `inventory.expiration.online` | 每日重置 | `CleanupOnlineExpiredItemsCommand`（`OnlineExpiredItemCleanupJob`） |
| `tau-kingdom.ticket-refill.online` | 每日重置 | `RefillOnlineTauTicketCommand` |
| `southern-dale.ticket-refill.online` | 每日重置 | `RefillOnlineSouthernTicketCommand` |
| `boss-tower.ticket-refill.online` | 每日重置 | `RefillOnlineBossTowerTicketCommand` |

**卡牌计时器与两塔离场队列**：

| 源 | 今天 | 迁移形状 |
|---|---|---|
| 卡牌布局（附录 A 第 6 行） | 入站作业里 `ScheduleAfterSettlement` → `_ = RunLayoutAsync`。等 2 秒后，在执行器外调用 `OpenLayoutAsync`：读运行态、`TryOpenCardLayout`、直发、`ScheduleClearReturn`，并启动自动翻牌。`finally` 里 `Complete` 取锁 | 一次到期一个作业，`timer:DungeonCardPresentationCoordinator/layout`，作业体内含 `Complete`。不用合并源：每个（副本局，角色）同时至多一个计时器，`Replace` 会取消前一个，计时器只触发一次 |
| 卡牌自动翻牌（附录 A 第 6 行） | 由布局启动；等 4 秒后在执行器外最多 3 次 `SelectCardCommand`，成功即投影 | 一次到期一个作业，`timer:DungeonCardPresentationCoordinator/auto-flip`，3 次尝试都在同一个作业里 |
| 两塔离场队列（附录 A 第 7 行） | 拆除时由 sink 写入 `ConcurrentQueue`，第 21、22 行的扫描消费 | 保留队列，在 tick 作业里消费。附录 A 写的是「定时 CMD（或离场时直接入队）」，本单元取前者，因为 sink 本身就在拆除 CMD 里调用，从作业里入队并等待就是自我等待 |

**33 个服务里不归本单元的 11 个**：

| 服务 | 归属 | 理由 |
|---|---|---|
| `LoginSessionMaintenanceService` | 已由 TASK-10 迁入 | — |
| `DomainEventHostedService` | TASK-12 | 事件分发 |
| `GameServerStartupService`、`GmBootstrapService` | TASK-13 | 启动期工作（3j） |
| `MarriageRelationAudit` | TASK-13 列入附录 B | 启动时只读 SQLite 计数 |
| `ChannelScriptStartupService` | TASK-13 列入附录 B | 启动时加载内容 |
| `HttpApiStartupService` | 不迁 | 只设置就绪标志，不碰玩法状态 |
| 3 个 `*RegistryValidationService` | 不迁 | 启动时检查注册表，不碰玩法状态 |
| `A21UdpDiscoveryService` | 附录 B 已列 | 只回 UDP 包 |

加法：本单元 22 ＋ TASK-10 的 1 ＋ TASK-12 的 1 ＋ TASK-13 的 4 ＋ 不迁的 5 ＝ 33，其中「不迁」是 `HttpApiStartupService`、3 个校验服务、UDP 发现。

**只读或只发包的服务**照样迁入。第 2、5～14 行不写玩法状态，但它们读在线身份（快照、`IsInWorld`），并且产生出站消息，RQ-010 要求这类工作在 CMD 内进行，附录 B 也没有它们的位置。它们与写状态的服务形状相同，没有特殊处理。

**`StartAsync` 里同步做工作的服务**：

| 服务 | `StartAsync` 做什么 | 归属 |
|---|---|---|
| Joust | 静默恢复：`TickAsync(suppressNotifications: true)`，推进并建立观察基线 | 本单元，第 3′ 行（待裁定第 1 条） |
| `NotificationRetryDrainService` | 只构造 `PeriodicTimer`、启动循环 | 不涉及玩法状态，不改 |
| `GmBootstrapService`、`GameServerStartupService` | 经总线 bootstrap；`EpicBook` 迁移、经验存储策略安装 | TASK-13 |
| `MarriageRelationAudit`、`ChannelScriptStartupService`、`HttpApiStartupService`、3 个校验服务 | 只读计数、加载内容、设置就绪标志、检查注册表 | TASK-13（附录 B）或不迁 |

### 2. 共同的接线形状

#### 2.1 每个周期服务

```
private readonly CoalescingGameplaySource ticks = new(queue);      // 一个服务实例一个合并源

// 入口方法：今天测试直接调用、ExecuteAsync 每次到期调用的那个方法（RefreshAsync、TickAsync、两塔新增的 tick 入口）
public Task RefreshAsync(CancellationToken ct) => queue.RunAsync(Tick(), ct).AsTask();

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(…, clock);                // 节奏不变
    try
    {
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ticks.TryRun(Tick(), stoppingToken).Completion;   // 等这次 CMD 完成，再等下一次到期
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }   // 原有的 catch 与日志保持原位
}

private GameplayWork Tick() => new(GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.Timer),
    "timer:<类型名>", RefreshCoreAsync);                            // 作业体 = 今天一次 tick 的全部内容，逐字搬迁
```

- **入口方法直接入队，不经合并源。** 合并源只给 `ExecuteAsync` 的循环用，因为 RQ-006 说的是「同一个定时源」。测试直接调用入口、或者 Joust 的 `StartAsync` 调用入口，都是另一个来源。如果它们也走合并源，与循环撞上时会被合并掉，调用方就会在什么都没执行的情况下返回。今天这两条路靠 `ticks` 信号量这类机制都会执行，所以入口方法必须直接 `queue.RunAsync`。
- **没有入口方法的服务**（第 4、15～20 行），循环直接用合并源。今天 tick 内部那些 `internal` 方法（`ReturnAsync`、`SweepAsync(candidate)`、`AdvanceAsync`、`DrainDeparturesAsync`、`OnlineExpiredItemCleanupJob.RunAsync`），在本单元之后是作业体的组成部分，不是入口。测试直接调用它们，属于「直接调用内部」，按 §9 包裹。
- **构造参数**：每个服务增加注入 `IGameplayQueue`。所有生产组合都调用了 `AddCommandDispatching`，因此能解析。
- **循环原有的 `catch` 与日志留在原位**，即等待 `Completion` 的那一层。作业里抛出的异常原样回到这里，与今天的结果相同。执行器另外会记一条 `Gameplay timer:X failed`（Error），这是 TASK-09 待裁定第 4 条所说的双日志，交 T-008。
- **令牌**：作业以 `stoppingToken` 入队，作业体拿到的也是它（见 §6、待裁定第 7 条）。
- **作业体内容逐字搬迁**：门、命令、`DirectAsync`／`ProjectAsync`、直发、本地 `delivered` 字典，原样放进作业体。只有「等下一次到期」（`WaitForNextTickAsync`、`wake.WaitAsync`）留在作业外。唤醒式循环（第 17、20 行）的作业体通过闭包变量返回下一个截止时间，循环在作业外据此等待。
- **扫描放进作业**：`FindExpired*`、`PendingRewards`、`DueSessions`、`Pending()`、`AdvanceRoomMechanisms` 读写的都是附录 F 中「改为执行器内访问」的玩法状态。拆分原则第 2 条允许把「读取到期项（只读）」留在执行器外，但这些读与执行器上的写会形成竞争，探针也会把它们记为 (A)。所以一律放进作业。两塔的离场队列是 `ConcurrentQueue`，放在外面也安全；为了让一次 tick 只有一个作业，同样放进作业。

#### 2.2 定时 tick 辅助（可选）

- 22 处会重复出现「`NewRoot(Timer)` ＋ `World` ＋ CMD 类型 ＋ `TryRun(...).Completion`」。
- 执行者可以在 `Application/Execution/` 新增一个小的静态辅助，只负责构造 `GameplayWork` 与等待合并源。也可以像 TASK-10 那样逐处展开写。以改动更小、更易审为准。
- 辅助不得带「执行器不在就内联执行」的分支。`LoginSessionMaintenanceService` 不回头改。

#### 2.3 调度任务

- `RunAsync` 中每次尝试原来是 `await using (var scope = scopes.CreateAsyncScope()) await Execute(...)`，改为 `await queue.RunAsync(new GameplayWork(World, NewRoot(Timer), $"timer:ScheduledTaskHostedService/{Id}", async t => { 同样的 scope 与 Execute }), ct)`。
- 取槽、释放槽、`Started`／`Requested` 的加锁、成功与失败的日志、重试间隔，都留在作业外，顺序不变。重试间隔因此不占执行器，`Retry_delay_does_not_hold_the_global_execution_slot` 的语义保持不变。
- `AddTaskScheduling` 调用 `AddGameplayExecution()`。这与 TASK-08 中 `AddCommandDispatching` 的做法一致：凡是有调度器的容器都有执行器。调用是幂等的，因为里面都是 `TryAdd`。由此带来的测试改动见 §9.1。
- `Task.Run` 外壳与并发槽保留（TASK-08 裁定）。注释改为说明：过渡期它们只决定哪个任务先排队，执行本身已在执行器上串行；T-005 删除。

#### 2.4 Joust

- `TickAsync(ct, suppressNotifications)` 成为入队入口：
  - 周期调用：来源 `Timer`，CMD 类型 `timer:JoustMaintenanceService`，由循环经合并源调用；
  - `StartAsync` 调用：来源 `StartupRecovery`，CMD 类型 `startup:JoustMaintenanceService`，直接入队。
  - 作业体是今天的「取 `ticks` → `AdvanceAndPublishAsync` → 释放」。
- `StartAsync` 在宿主启动阶段入队，执行器在第一次入队时启动（Plan「执行器的生命周期」），不依赖托管服务的启动顺序。
- `ThrowsAsync<SqliteException>(() => StartAsync)` 这类断言仍然成立：执行器把原异常对象放进结果（`GameplayExecutor` 的 `TrySetException(failure)`）。
- 测试直接调用 `TickAsync`，从此就是经入口驱动，不需要包裹。

### 3. 卡牌协调器

#### 3.1 计时器的启动与到期

```
ScheduleAfterSettlement(session, runId):          // 由入站作业调用（DungeonClearSlice）
  var cancellation = Replace(...)                   // 仍在调用方的作业里取锁，与今天相同
  StartDetached(() => RunLayoutTimerAsync(session, runId, cancellation.Token))

StartDetached(start):                               // SuppressFlow；已处于 SuppressFlow 时不嵌套（同 GameplayExecutor.EnsureStarted）
  using (ExecutionContext.SuppressFlow()) _ = start();

RunLayoutTimerAsync(session, runId, token):
  inflight 加一（排空要等它）
  try
    await Task.Yield()                              // 离开调用方的调用栈；零延迟时也不会同步续在作业上
    await delay.WaitAsync(LayoutDelay, token)
    await queue.RunAsync(new GameplayWork(World, NewRoot(Timer), "timer:DungeonCardPresentationCoordinator/layout",
        async ct =>
        {
            try { await OpenLayoutAsync(session, runId, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e) { 原有的 "Dungeon card layout timer failed …" Error }
            finally { Complete(session.CharacterId, runId, token); }   // 取锁，移进作业
        }), token)
  catch (OperationCanceledException) when (token.IsCancellationRequested) { }  // 被 Cancel/Replace/停机取消：Complete 不必做
  catch (GameplayExecutorStoppedException) { 记一条 Information「执行器已停止，计时器未入队」（§5.4） }
  catch (Exception e) { 原有的 Error }
  finally { inflight 减一；如果已停止且归零，就通知排空 }
```

- 自动翻牌同形，CMD 类型为 `/auto-flip`，作业体是今天 `RunAutoFlipAsync` 中 `delay` 之后的循环，`finally` 里同样调用 `Complete`。`OpenLayoutAsync` 末尾启动自动翻牌，也走 `StartDetached`，因为它此时在布局作业里。
- **为什么要 `Task.Yield`**：`SuppressFlow` 只是不让后续续体捕获上下文，当前线程上的 AsyncLocal（作业令牌）仍然可见。如果 `delay.WaitAsync` 同步完成，比如零延迟，续体会在调度它的那个作业的调用栈上同步执行 `queue.RunAsync`，于是触发 `GameplaySelfWaitException`。先 `Yield`，就和 TASK-10 关连接器的写法（N-1）一致。C3 守这一点。
- **为什么 `Complete` 移进作业**：它取协调器的锁，而协调器状态属于附录 F「改为执行器内访问」。只有在计时器真的运行之后，`Complete` 才需要做事。如果在 delay 期间被 `Cancel`、`Replace` 或停机取消，表项要么已被移除，要么已无关紧要，跳过也不留错误状态。停机时留下的表项随进程结束。
- **令牌**：入队用计时器自己的令牌。停机或 `Cancel` 时，还在排队的计时器作业会被执行器取消，不再执行（`GameplayExecutor` 的待执行取消）。这与今天「取消之后就不再打开布局」相同。

#### 3.2 停机与 `Dispose`

- **全局停止令牌**：`Replace` 创建的每个计时器 CTS 改为 `CreateLinkedTokenSource(stopping.Token)`，`stopping` 是协调器自己的 CTS。
- **`DrainAsync`**（`IGameplayProducerDrain`）：
  1. 以 `Interlocked` 置位已停止标志；
  2. `stopping.Cancel()`，不取锁；
  3. 等在途计时器计数归零，用 TCS 等，不轮询，受 `ct` 约束。
  - 返回时，没有计时器会再入队；在途的那些不是已执行完、被取消，就是被执行器以「已停止」拒绝。
  - 整个过程不取 `gate`。
- **`Dispose`**：置同一个标志，`stopping.Cancel()`，不取锁，不等待。已经排空过的，再调用一次也无害。
  - `pending` 里剩下的 CTS 不再逐个 `Dispose`。它们都已被链式取消，进程或容器随后结束，这是有意识接受的代价。
  - 这条路径今天在容器释放时取锁（TASK-10 §7，探针 §3.9 中 55／216），本单元之后不再取锁。
- **`Replace` 的已停止检查**：继续 `ObjectDisposedException.ThrowIf`，读的是同一个标志。
  - 停止后，只有入站 handler 还可能调度计时器，而入站在 TCP 服务停止时就已结束。
  - 拆除（生命周期 CMD）只调用 `Cancel`，`Cancel` 在停止后仍然可用。
- **注册**：在 `ProtocolModule` 已有的 `TryAddSingleton<DungeonCardPresentationCoordinator>()` 之后加一行 `services.AddSingleton<IGameplayProducerDrain>(sp => sp.GetRequiredService<DungeonCardPresentationCoordinator>())`，让排空与协调器是同一个实例。
  - 排空按注册顺序执行，与 `TouchSocketTransportDrain` 谁先谁后都不影响结果。传输排空兜底拆除时只会调用 `Cancel`。
  - 只要两次排空都在执行器停止之前完成，就满足 RQ-009。
- **附录 B 不新增例外**（TASK-10 裁定第 5 条）。

#### 3.3 TASK-06 主控方补充 N-3 的复核

- `DungeonCardPresentationCoordinatorTests` 里 21 处作业内轮询，都写成 `TestGameplay.RunAsync(async () => …)`，不带容器。按 `TestGameplay` 的选择顺序，每次调用新建一个执行器。
- 本单元让测试的 `World`／`PartyWorld` 各自持有一个执行器，并把它的 `GameplayQueue` 注入协调器，计时器 CMD 就落在这个执行器上。
- 轮询作业与计时器 CMD 因此在不同的执行器上，不会互等。Result 要逐个确认 21 处都属于这种形状。
- 反过来的危险是：如果有人把 `world` 的执行器传给 `TestGameplay`，作业内的轮询就会等一个排在自己后面的计时器 CMD，2 秒后超时。所以 `World` 不暴露 `IServiceProvider`；`World` 的执行器字段的注释里写明这一点。
- `World.Dispose` 先释放协调器，取消计时器，再释放执行器（`DisposeAsync` 排空）。
- 这三条在 TASK-07 §6 中「有意留在作业外」。本单元之后的状态：
  - 「`ControlledDelay.ReleaseAsync` 触发的回调」，现在只是在分离任务里放行等待，回调随后入队，属于入口驱动；
  - 「`World.Dispose` → `Coordinator.Dispose()`」不再取锁，探针 (B) 中的那 7 条应当消失；
  - Result 更新这两行的结论。

### 4. 两塔离场与 `IllusionTowerPublicFlowTests:149`

- 第 21、22 行的 tick 作业依次执行 `DrainDeparturesAsync` 与 `SweepAsync(clock.GetUtcNow())`，两者都在同一个作业里，顺序与今天相同。
- 新增一个公开 tick 入口，例如 `TickAsync(CancellationToken)`：直接入队，作业体就是上面这两步。`ExecuteAsync` 经合并源构造同一个作业。
- 公开的 `SweepAsync(now)` 与 `DrainDeparturesAsync` 保留，作为作业体的组成部分；测试直接调用它们，按 §9 包裹。
- 这个入口只为 §8.3 的后继用例而加。如果后继用例能用 `SchedulerClock` 推进宿主循环来驱动，就不加入口。由执行者选，Result 写明。

### 5. 风险与可见差异

1. **定时作业排在入站之后，入站也排在定时作业之后**。这是 RQ-001 要的结果。需要关注的是长作业：
   - 每日任务、`Premium`、配额刷新，在一个作业里遍历全部在线会话。会话多时，单个作业会长，属于 RQ-091 单条时长阈值关注的对象。
   - 本单元不拆开它们，这是纯搬迁。M-1 时按 `Gameplay timer:… completed` 的 Debug 耗时观察；如果超阈值，由 T-008 的内部 CMD 拆成每会话一条（待裁定第 3 条）。
2. **直发 2 秒期限扩大到定时 CMD**：定时作业里的直发（Fatigue、Premium、婚礼、比武、卡牌、生物投影）从此受 TASK-08 的期限约束，不读包的客户端会在 2 秒后被断开。VC-8 提前的范围从入站、生命周期再扩大到定时，登记进 Result。
3. **空闲作业的频率**：50 ms 的比武扫描、250 ms 的全灭扫描、两个 500 ms 的两塔扫描、每秒一次的生物、婚礼与 Joust，以及重试排空，空闲时合计约每秒 30 个空作业。单个空作业只有一次锁与一次分配，入站排队延迟的增量可以忽略；M-1 时记录。
4. **执行器已停止时的计时器**：只有在没有经过排空的停止路径上才会出现，即不带宿主的容器释放。这时到期的计时器入队会得到 `GameplayExecutorStoppedException`，按 §3.1 记 Information 后丢弃，与今天「容器释放时取消全部计时器」的结果相同。
5. **卡牌计时器不再继承调度它的作业的 AsyncLocal 帧**（环境事务、效果操作帧、门借用帧）。今天 2 秒后续上来的回调会带着早已结束的入站作业的帧运行，那是缺陷，不是依赖。全量与卡牌线级用例（`CardRewardReconnectIntegrationTests`）守着行为不变。
6. **执行器外仍持门的生产者**（事件分发、GM/HTTP，分别归 TASK-12、13）可能让定时作业在门上等待。这只是延迟，不会成环：那些持门者从不等待定时作业（拆分原则第 3 条的反向情形）。
7. **测试里的作业内轮询**：凡是在带容器的 `TestGameplay` 区域里等待某个定时效果的写法，现在都会等一个排在自己后面的定时 CMD。按 §9 (iii) 处置。

### 6. 停机

- **托管服务一侧不需要新的排空。** 宿主停止的顺序是：各托管服务的 `StopAsync`（取消 `stoppingToken`，并等 `ExecuteAsync` 结束），然后在 `StoppedAsync` 里执行排空并停止执行器（TASK-10 §6.1）。所以每个周期服务停止时，执行器仍在运行：
  - 正在执行的 tick 作业看到 `stoppingToken` 被取消，按今天的 `catch` 退出；
  - 还在排队的 tick 作业因入队令牌已取消，被执行器取消，不执行，循环以 `OperationCanceledException` 退出。
  - 「停机时尚未开始的 tick 不执行」与今天「停机时不再 tick」相同（待裁定第 7 条）。
- **调度任务**：`ExecuteAsync` 的 `finally` 照旧等全部 `executions`。每个执行在等它的作业，作业也随令牌取消或结束。`Different_task…stop_cancels_it` 的改写见 §8.4。
- **卡牌计时器**不是托管服务，可能在 `StopAsync` 与执行器停止之间到期，所以要有 §3.2 的排空。
- **Joust 的 `StartAsync`** 只在启动时运行，与停机无关。
- 新增测试 C5 在真实宿主上证明：带着一个待触发的布局计时器停止宿主，没有 `layout timer failed`、没有 `GameplayExecutorStoppedException`；排空在 `executor.StopAsync` 之前返回。

### 7. 新增测试

**放在 Application.Tests/Execution 与 Scheduling**（Protocol 的类型都是公开的，`internal` 的那两个经 `IHostedService` 枚举按类型名取出，写法同 `OnlineExpiredItemScheduleTests`）：

| 条 | 情形 | 断言 |
|---|---|---|
| E1 | **全量对账**（Theory，一行一个源）：第 2～22 行各 1 行（第 3′ 行单独 1 行），加上调度任务 5 个 ID 各 1 行。<br>容器用 `ApplicationModule` ＋ 持久化与基础设施模块 ＋ `ProtocolModule`，使用临时 SQLite；TimeProvider 为 `SchedulerClock`；`IGameplayQueue` 替换为记录型装饰器，它把作业体包一层，在作业里记下 `IsOnExecutor` 与 `Current`。<br>只启动被测的那一个托管服务，推进时钟到它的下一次到期（唤醒式的启动即运行第一轮；每日任务推进一天） | 记录中出现该行的 CMD 类型，来源是 `Timer`（第 3′ 行是 `StartupRecovery`）；作业体执行时 `IsOnExecutor` 为真；作业成功完成。这是 AC-006「来源 → CMD 类型 → 证据」的逐行对账。需要 PVF 的行改为 `PvfFact`，Result 写明 |
| E2a | 周期类的下游证据：`FatigueDailyRefreshService`，记录型 `IGameMessageSender`，一个在线会话，余额有变化。分别经 `ExecuteAsync`（推进时钟）与经入口 `RefreshAsync` 驱动 | 替身被调用时，`IsOnExecutor` 为真，`Current` 为 `timer:FatigueDailyRefreshService`／`Timer` |
| E2b | 唤醒类的下游证据：`AltarScheduler`，替身 `IAltarDungeonDomainService` 在 `Pending()` 里记录 | `Pending()` 每次被调用都在执行器上；`wake` 触发后再运行一轮，仍在执行器上 |
| E2c | 调度任务：用 `AddScheduledTask` 注册一个记录型回调，失败一次后成功 | 每次尝试都在执行器上，类型为 `timer:ScheduledTaskHostedService/<ID>`；两次尝试是两个不同的作业；在重试间隔期间，另一个入队的作业能执行，说明重试间隔不占执行器 |
| E2d | `NotificationRetryDrainService`：`CountingDrain` 在 `DrainAsync` 里记录 | 每一轮都在执行器上，类型为 `timer:NotificationRetryDrainService`；抛出的那一轮之后循环继续（原有的保护意图） |
| E3 | **RQ-006／AC-004 接线**：Fatigue 服务；先用一个等待 TCS 的作业占住执行器，推进时钟两次 | 占住期间，装饰器只记到 1 次 `timer:FatigueDailyRefreshService` 入队；放行后它执行完，下一次到期才再入队 1 次 |

**放在 Protocol.Tests/Game/CardReward**，沿用 `DungeonCardPresentationCoordinatorTests` 的 `World` 与 `ControlledDelay`，另加记录型替身：

| 条 | 情形 | 断言 |
|---|---|---|
| C1 | 布局计时器到期 | 记录型 sender 收到布局两帧时，`IsOnExecutor` 为真，`Current` 为 `…/layout`／`Timer`，且落在 `World` 的执行器上 |
| C2 | 自动翻牌到期 | `CardCommandBus` 收到 `SelectCardCommand` 时在执行器上，类型为 `…/auto-flip` |
| C3 | 用真实的 `SystemDungeonCardPresentationDelay`，零延迟，在 `World` 执行器的一个作业里调用 `ScheduleAfterSettlement` | 没有 `GameplaySelfWaitException`；布局在这个作业结束之后才打开 |
| C4 | `Cancel` 或 `Replace` 发生在延迟期间 | 计时器 CMD 不执行；`pending` 表项按今天的规则移除 |
| C5 | 真实宿主（`ApplicationModule` ＋ `ProtocolModule`），调度一个布局计时器，在它到期前停止宿主 | 没有 `layout timer failed` 与 `GameplayExecutorStoppedException`；排空在执行器 `StopAsync` 之前返回；协调器的计时器已被取消 |
| C6 | 注册断言 | `GetServices<IGameplayProducerDrain>()` 中恰有一个就是 `DungeonCardPresentationCoordinator` 单例 |
| C7 | 排空等在途回调：一个计时器正在等它的作业（执行器被一个 TCS 作业占住），这时调用 `DrainAsync` | 排空在放行之前不返回，放行之后返回；之后再到期的计时器不入队 |

**后继用例**：§8.3 的 `IllusionTowerPublicFlowTests` 新用例（`PvfFact`）。

先写测试，再改生产代码：E1、E2a～E2d、E3、C1～C3、C5～C7 在改动前应当红，Result 记录红的形态。C4 守的是迁移后不被破坏。

### 8. 冲突用例

行号是 `task-07-conflicts.md` 的基准行号（`0223e646`），当前文件里已经后移，**以方法名为准**。

#### 8.1 `:109` `Simultaneous_party_auto_flips_retry_a_contended_column_and_award_each_member_once`

- **预判：本单元失败**（等待 2 秒后超时）。`PartyWorld` 的 `CardCommandBus` 带 `FirstAttemptGate(2)`，它要两名成员的第一次 `SendAsync` 都到达后才放行。迁入之后，两个自动翻牌 CMD 串行执行：第一个 CMD 在门闩上等第二个成员，而第二个成员的 CMD 排在它后面。
- **处置**：改写。
  - 旧断言：两名成员同时争用、重试后各得一张，索引互不相同；
  - 新断言：两个到期 CMD 先后执行，每名成员恰好一次 `Flipped`，所得索引互不相同，总选择数为 2；
  - 保护意图：每名成员得到且只得到一张。「争用后重试同一个个人确认」由 `:235` 守。
  - 用例改名，旧名在 Result 的映射里保留。
- **夹具**：`BuildParty` 增加一个开关，默认不使用 `FirstAttemptGate`。同样走 `BuildParty` 的 `Late_party_layout_requests_do_not_repeat_the_panel_or_cancel_auto_flip` 与 `Early_party_layout_requests_open_once_and_retire_the_layout_timers` 也会因这个门闩而自锁，它们只改用默认值，断言不变，按 §9 (iii) 记录。`FirstAttemptGate` 只剩 `:109` 的旧写法需要它；改写后如果没有用处，就删除。

#### 8.2 `:235` `Auto_flip_retries_the_same_personal_confirmation_after_a_transient_conflict`

- **预判：本单元失败**。它在 `ReleaseAsync(2s)` 返回后立刻同步断言 `Pending(4s) == 1`，前提是布局在 `ReleaseAsync` 的工作线程上同步完成。迁入之后，布局是另一个执行器上的作业，`ReleaseAsync` 返回时布局多半还没执行。
- **处置**：改写等待方式。同步断言改为 `WaitUntilAsync`，`ScheduleAfterSettlement` 放进 `TestGameplay` 作业，与同文件其他用例一致。断言内容不变：只有一次选择，`Attempts == [0, 0]`，索引为 0。
  - 保护意图（瞬时冲突后重试的是同一个个人确认，而不是换一列）不变。

#### 8.3 `IllusionTowerPublicFlowTests:149` `Sweep_captured_for_an_old_run_cannot_return_or_reward_its_replacement`

- **预判：本单元仍然通过**。它在 `Task.Run` 里直接调用 `IllusionTowerProjectionDelivery.SweepAsync`，并直接调用各 handler，本单元没有改动这条路径。
- **处置**：按拆分原则第 5 条顺延到 T-004c。在本单元写后继用例（`PvfFact`）：
  - 旧 run 的回城到期之后，先经 handler 回城并重新进塔，得到新的 run；
  - 然后经两塔扫描的 tick 入口（§4）运行一次真实的定时 CMD；
  - 断言新 run 不受影响：没有结算帧，经验、金币不变，不回城。
  - 「扫描捕获候选之后、执行之前 run 被顶替」这种交错，在串行执行器下不会发生，因为整次扫描是一个 CMD。剩下要守的是「扫描以当前状态复核」，后继用例守的就是它。
- 它带出的 (C) 栈顶 `SelectDungeonHandler.HandleMessageAsync`（TASK-09 更正）与 `IllusionTowerProjectionDelivery.LeaveAndReturnAsync`，随顺延一起留在探针残留里，用排除复跑证明。

#### 8.4 本单元新判定的冲突：`ScheduledTaskTests.Different_task_can_finish_while_long_task_is_blocked_and_stop_cancels_it`

- 机制：一个任务在作业里无限等待，另一个任务应在此期间执行完。迁入之后，两者在同一个执行器上串行，第二个任务在停止之前不会执行，等待 `other` 会在 10 秒后超时。
- 这是 RQ-013 直接反转了这条用例的保护意图：调度任务不再并发。归入 §9 (ii)，追加到 `task-07-conflicts.md` §9。
- **处置**：改写。
  - 旧断言：一个任务被卡住时，另一个任务仍能完成；停止会取消被卡住的那个；
  - 新断言：被卡住的任务占着执行器时，另一个任务不执行；停止会取消被卡住的那个，并释放它的 scope；另一个排着队的任务随令牌取消，不执行；
  - 保护意图：「停止能取消一个卡住的任务」保留；「任务之间互不阻塞」被规格删除。Result 中引用 RQ-013。
- 预判同文件的其他用例仍然通过：
  - `Global_limit…`：并发槽为 1，本来就串行；
  - `Retry_delay…`：重试间隔在作业外；
  - `Slow_task_never_overlaps…`：单个任务；
  - `Failure_retries…`：同步回调。
- 如果实际结果不同，按 §9 分类。

冲突总清单的新分布：

- T-004b：7 －（本单元 3 条）＝ 4。剩下的是 TASK-12 的 1 条、TASK-13 的 2 条，以及原已标「顺延 T-004c」的 `ReplayStateSnapshotTests:206` 1 条。
- T-004c 加上顺延的条数（预计 1）。
- 新判定的 1 条计入总数（136 ＋ 1）。
- 加法在 `task-07-conflicts.md` §9 写出。

### 9. 测试侧的处置分类

沿用 TASK-10 §9 的 (i)～(vi)，本单元特有的处置规则如下：

1. **构造实参**：
   - 直接 `new` 定时服务的测试，需要补一个 `IGameplayQueue`：`ItemUsageProjectionTests`，`UsageQuotaProjectionTests` 中的 `GoldSilverHorn`、`TournamentQuota`、`Mirror`，`NotificationRetryDrainServiceTests.Service(...)`，以及协调器测试的 `Build`／`BuildParty`。实参用测试自己持有并释放的执行器，`new GameplayQueue(new GameplayShardSelector(executor))`。
   - 这些用例原来直接调用 `RefreshAsync`，从此经入口驱动，落在那个执行器上。TASK-06 §7 说的「4 条每日刷新 worker 入口用例」由此成为附录 A 的对账证据，只改构造实参与执行器声明行。
   - `ActivatorUtilities.CreateInstance(...)` 与 `GetRequiredService` 的测试从容器里解析，不需要改。
   - `ScheduledTaskTests.Running` 中 `Assert.Single(Provider.GetServices<IHostedService>())` 改为按类型取调度器，因为 `AddTaskScheduling` 现在也注册了执行器生命周期服务。
2. **直接调用作业体的内部方法**：
   - 涉及的方法：`TournamentRewardSweepTests.Sweep`（反射调用 `SweepAsync(candidate)`）、`OnlineExpiredItemCleanupTests` 对 `OnlineExpiredItemCleanupJob.RunAsync` 的 3 处调用，以及全量或探针中发现的其他同类调用（`ReturnAsync`、`AdvanceAsync`、`DrainDeparturesAsync`、两塔的 `SweepAsync`）。
   - 这些调用包进 `TestGameplay.RunAsync(<容器>, …)`。它们是 T5 式的直接调用，不是入口。只改包裹方式，用差异核对证明断言不变。
   - 冲突用例 `:149` 不包，保持原样。
3. **经入口驱动、但处在带容器的包裹区域里**，报自我等待：把入口调用移出包裹，即 (iii)。可能涉及的有 `FatigueRefreshTests`、`DungeonBuffUsageTests`、`WeaknessGoldRecoveryTests` 中调用 `RefreshAsync` 的位置，以及 `JoustIntegrationTests` 中经 `Fixture.Run` 调用 `TickAsync` 的位置。
4. **作业内轮询定时效果**：解开包裹，或改为每次求值一个短作业（拆分原则第 5 条）。
5. **新的交错型冲突**按 (ii) 判定，追加到 `task-07-conflicts.md` §9，并更新总数加法。

**主动复核**（即使全绿，也逐项写结论）：

- 启动了 §1 表中任一托管服务、又用到 `TestGameplay(<容器>)` 的测试文件：`JoustIntegrationTests`、`OnlineExpiredItemScheduleTests`、`ScheduledTaskTests`，以及 Networking、Host 中拉起整个宿主的用例。确认没有 (iii)、(iv) 两类的写法：在作业里等待定时效果，或在作业里停止宿主。
- `CardRewardReconnectIntegrationTests`：真实的 2 秒与 4 秒计时器。确认它的轮询仍在作业外（TASK-07 §6）。
- §3.3 中 N-3 的 21 处轮询逐个确认。
- TASK-07 Remaining 中「Weakness 等并发包裹」那几个文件（`WeaknessGoldRecoveryTests` 等），确认在定时 CMD 迁入后没有互等。

### 10. 探针

- 做法同 TASK-10 Required check 10：
  1. `PROBE_WORK` 指向 scratchpad，`patch.py apply`；
  2. 构建；
  3. `runall.sh` 跑四个项目；
  4. `patch.py revert`；
  5. 三项还原证明。
- `classify.py` 的入口帧名单不改。迁入之后，这些帧只出现在执行器外的生产者栈上，而生产者在执行器外只做入队，不触达挂点，所以不会再被记录。
- 判读：
  - §3.6、§3.8 本族与 §3.9 的 `Dispose` 按 Required check 9 归零；
  - 残留只允许来自 `:149`；
  - 本族以外各族与 TASK-10 p2 并列。
- 探针轮中的失败不作为任何结论的证据（TASK-07 §7）。

### 11. 执行顺序

建议按三批推进，每批结束时构建并跑相关项目（单元内分批，最后一次提交；拆分的备选见待裁定第 8 条）：

1. **第 0 步**：在基准上单跑 `DungeonCardPresentationCoordinatorTests`、`ScheduledTaskTests`、`IllusionTowerPublicFlowTests`，记下基线结果。
2. **批 A（Application 侧）**：
   - E2c → 调度任务、`AddTaskScheduling`、`ScheduledTaskTests.Running`、§8.4；
   - E2d → 重试排空与它的测试构造；
   - Joust（第 3、3′ 行）→ 跑 `JoustIntegrationTests`；
   - 跑 Application.Tests。
3. **批 B（Protocol 周期服务）**：
   - E2a、E3 → 第 5～14 行，以及 §9.1 的构造实参；
   - E2b → 第 4、15～22 行，以及 §9.2 的包裹；
   - E1；
   - 跑 Application.Tests、Protocol.Tests。
4. **批 C（卡牌与两塔）**：
   - C1～C7 → 协调器、`ProtocolModule` 注册、协调器测试的 `World`；
   - §8.1、§8.2；
   - §4 与 §8.3 的后继用例；
   - 跑 Protocol.Tests、Networking.Tests。
5. 全量，带挂起检测。按 §9 逐条分类，重跑到稳定；做耗时比对。
6. 变异核对。
7. 探针复跑与归零分析，还原证明。
8. 对账行。
9. 正式全量 → 隔离复跑 → 新增测试 ×10 → 测试改动核对 → 边界 grep 与不变量复核 → 写 Result。

### 12. 为什么除可见差异外行为不变

- 每个定时源每次运行的工作内容、内部顺序、门、命令与发送，都与今天逐行相同，只是换到执行器上运行。节奏（`PeriodicTimer`、唤醒信号、schedule）不变。
- 跨来源从并发变为串行，这是 RQ-001 的要求；调度任务之间不再并发，这是 RQ-013 的要求。
- 可见差异见 §5 第 2、4、5 条。另有一条日志差异：失败的 tick 多一条执行器的 Error。
- 线上字节不变，由表征测试与既有线级测试守着。

## 待裁定

1. **Joust 启动静默恢复的归属**（§2.4）。
   - 本方案：本单元一并迁入，作为 `startup:JoustMaintenanceService`（`StartupRecovery`），从 TASK-13 移出。
   - 理由：
     - 恢复与周期 tick 共用 `TickAsync` 和 `ticks` 信号量；如果只把循环的那次调用放进作业，`TickAsync` 在两个单元之间就会有两种形状；
     - TASK-12 也在自己的单元里迁入事件分发的启动扫描，「生产者自带它的启动恢复」是同一个口径；
     - TASK-13 保留 GM bootstrap、`EpicBook` 迁移、经验存储策略安装、预热，以及附录 B 的启动期清单。
   - 备选：本单元只在 `ExecuteAsync` 里包住对 `TickAsync` 的调用，`StartAsync` 留给 TASK-13；`JoustIntegrationTests` 中直接调用 `TickAsync` 的位置（文件内 `TickAsync` 共出现 20 处，含辅助方法）改为包裹。
2. **入口方法的划分**（§2.1）。
   - 今天就是一整次 tick 的公开或内部方法（10 个 `RefreshAsync`、Joust 的 `TickAsync`，以及两塔新增的 tick 入口），改为直接入队的入口。测试直接调用它们，就成了入口证据。
   - 只是 tick 一部分的内部方法，保持为作业体，测试的直接调用改为包裹。
   - 请确认。
3. **作业粒度**：一次 tick 一个作业（§1、§5 第 1 条），不拆成每会话或每个副本局一个作业。这是纯搬迁。长作业的风险留到 M-1 观察，拆分归 T-008。请确认。
4. **调度任务**：
   - 不另加合并源，调度器自身的记账就是 RQ-006 合并；`AddTaskScheduling` 调用 `AddGameplayExecution`。
   - **时机口径上的不一致**：Plan 共享决定「定时源」一段写「调度任务去掉 `Task.Run`」，而 TASK-08 裁定是留到 T-005 删除，与附录 F 一致。本单元按裁定执行。建议主控方把 `plan.md` 那一句改为「`Task.Run` 外壳与并发槽在 T-005 删除；T-004 起每次运行都是执行器上的作业」，以免漂移。
5. **卡牌协调器**（§3.2）：
   - 以 `IGameplayProducerDrain` 在执行器停止前取消并等待计时器；
   - `Dispose` 不取锁；
   - `ProtocolModule` 加一行注册。TASK-10 曾把 `ProtocolModule` 列为不改，本单元需要这一处接线。
   - 附录 B 不新增停机期例外。
   - 请确认。
6. **冲突用例**：
   - `:109`、`:235` 预计本单元失败，就地改写；
   - `:149` 预计通过，顺延 T-004c，后继用例写在本单元；
   - 新判定 `ScheduledTaskTests.Different_task…` 为冲突，按 RQ-013 改写；
   - `BuildParty` 夹具的门闩改为可选，影响另外 2 条非冲突用例，只改夹具开关。
   - 另一种做法是 `:149` 也在本单元删除或改写。它守的交错在生产中已经不再可能发生，代价是违反「只在使它失败的单元执行」。建议按规则顺延。
7. **停机时还在排队的定时 CMD**（§6）：定时 CMD 以生产者的 `stoppingToken` 入队，停机时尚未开始的被取消、不执行。
   - 本方案认为这不违反 RQ-009：RQ-009 要执行完的是「已入队的 CMD，包括断开引起的生命周期 CMD」，而一次被生产者自己撤回的 tick，等同于今天停机时不再发生的那次 tick。
   - 备选：以 `CancellationToken.None` 入队、只把 `stoppingToken` 交给作业体。这样排队的 tick 在停机时仍会执行一次，作业体多半立刻以取消退出，停机时间可能略长。
   - 请选择。
8. **单元规模**：约 27 个生产文件、十几个测试文件、22 个服务加两种计时器。
   - 本方案在单元内分三批（§11），一次提交。
   - 备选是拆成两个单元：
     - **TASK-11a**：附录 A 第 5、7 行与 Joust，即 §1 表的 22 个托管服务，含两塔离场；冲突 `ScheduledTaskTests` 1 条与 `:149`；
     - **TASK-11b**：附录 A 第 6 行，即卡牌计时器、协调器的排空与 `Dispose`、`ProtocolModule` 注册；冲突 `:109`、`:235`。
     - 拆开后，N-3 的风险与协调器语义集中在一个小单元里，便于审查；代价是多一次结果审查，并且 11b 的探针 §3.9 归零要等 11b 完成。
   - 建议不拆，因为卡牌部分只占一个文件和一个测试文件。如果主控方更重视审查的聚焦度，就按上面的边界拆开。
9. **CMD 类型命名**，请确认：
   - `timer:<类型名>`；
   - `timer:ScheduledTaskHostedService/<任务 ID>`；
   - `timer:DungeonCardPresentationCoordinator/layout`、`/auto-flip`；
   - `startup:JoustMaintenanceService`；
   - 全部取新根。调用链继承由 T-007 处理：卡牌计时器的 CMD 是否应继承调度它的入站 CMD 的链，届时一并决定。
10. **`NotificationRetryDrainService` 在 T-006 删除之前先迁入**：它读在线身份并重发消息，探针 §3.8 有它的记录；不迁入，TASK-14 的进入条件「(A) 只剩 §3.10」就不成立。代价是 T-006 删除时要连同本单元加的那几行一起删。请确认。

## 主控方裁定（执行前）

1. **Joust 启动静默恢复**：本单元一并迁入，作为 `startup:JoustMaintenanceService`，从 TASK-13 移出。理由是它和周期 tick 共用 `TickAsync` 与信号量；TASK-12 也是自己迁移事件的启动扫描，口径一致。
2. **入口方法的划分**：接受。代表一整次 tick 的方法改为入队入口；只是 tick 一部分的内部方法仍作为作业体，测试里对它们的直接调用改为包裹。
3. **作业粒度**：一次 tick 一个作业，接受。长作业由 M-1 观察，拆分归 T-008。
4. **调度任务**：不另加合并源，`AddTaskScheduling` 调用 `AddGameplayExecution`，接受。`plan.md` 第 54 行已改为：调度任务的每次尝试作为执行器作业运行，`Task.Run` 外壳与并发槽在 T-005 删除。这一处修正随本单元提交，只改措辞、不改要求。
5. **卡牌协调器**：接受。走排空接口，`Dispose` 不再取锁，`ProtocolModule` 允许加这一行注册。
6. **冲突用例**：
   - 卡牌 `:109`、`:235` 在本单元改写；
   - `:149` 顺延到 T-004c，本单元写一条后继用例；
   - `ScheduledTaskTests.Different_task_can_finish_while_long_task_is_blocked…` 按 RQ-013 改写。它原来保护的「调度任务之间可以并发」，正是规格要消除的行为。改写后断言新的意图：调度任务串行执行，长任务不会让别的任务丢失，只是推迟。在 Result 中把这条记为「RQ-013 规定的意图变化」，写出旧断言 → 新断言；
   - `BuildParty` 夹具的 `FirstAttemptGate` 改为可选开关，受牵连的 2 条用例断言不变。
7. **停机时已入队的定时 CMD**：采用备选方案，生产者以 `CancellationToken.None` 入队，已入队的定时 CMD 在执行器停止前照常执行完。理由：RQ-009 字面要求「已经入队的 CMD 全部执行完」，宁可停机时多跑一次 tick，也不偏离字面。生产者的停止令牌只用来停止下一次「等待到期」和入队。补一条测试：停机时队列中的定时 CMD 被执行。
8. **规模**：本单元内分三批推进，一次提交。每批结束都要构建，并跑受影响项目的测试。
9. **CMD 类型命名**：按本文件，全部取新根，接受。
10. **`NotificationRetryDrainService`**：先迁入，由 T-006 删除，接受。

## Result

### Outcome

- **22 个托管服务与 5 个调度任务**：每次到期把这一次运行的整段逻辑作为一个执行器作业入队，并等待它完成。CMD 类型一律 `timer:<类型名>`（调度任务为 `timer:ScheduledTaskHostedService/<任务 ID>`），`NewRoot(Timer)`。
  - 周期循环经各服务自己持有的 `CoalescingGameplaySource` 入队；入口方法（10 个 `RefreshAsync`、Joust 的 `TickAsync`、两塔新增的公开 `TickAsync`）直接入队，不经合并源（§2.1）。
  - 唤醒式循环（祭坛、房间机制）的作业返回下一个截止时间，`wake.WaitAsync` 留在作业外。
  - 调度任务每次尝试一个作业，作业里含 scope 的创建与释放；取槽、加锁、日志、重试间隔留在作业外。`Task.Run` 外壳与并发槽保留给 T-005。`AddTaskScheduling` 调用 `AddGameplayExecution`。
  - Joust 启动静默恢复为 `startup:JoustMaintenanceService`（`StartupRecovery`），与周期 tick 同一个作业体；`ticks` 信号量移进作业。
- **共同辅助** `Application/Execution/GameplayTimerWork.cs`（§2.2）：`RunAsync`（直接入队）与 `TickAsync`（经合并源）。
  - 按裁定第 7 条，以 `CancellationToken.None` 入队，作业体拿到生产者自己的令牌（这是待裁定第 7 条备选方案的原文，读法见 Why 第 1 条）。
  - 作业体经该令牌抛出的取消被带出作业，在生产者原来的 `catch` 处重新抛出。因此执行器不把它记为失败，外层逻辑与今天相同。
  - 没有「执行器不在就内联」的分支。
- **卡牌协调器**：
  - 布局与自动翻牌计时器在 `SuppressFlow` 下启动（分离点 3e 的第三项），先 `Task.Yield`，等过延时后入队 `timer:DungeonCardPresentationCoordinator/layout`／`/auto-flip` 并等待。
  - 作业体就是今天 `RunLayoutAsync`／`RunAutoFlipAsync` 去掉 delay 后的内容，`Complete` 随之移进作业。
  - 计时器 CMD 同样以 `None` 入队：已入队的照常执行；作业体先检查计时器令牌，被 `Cancel`／`Replace`／停机取消的什么都不做。入队前再检查一次令牌，所以停止之后不再入队。
  - 实现 `IGameplayProducerDrain`：`DrainAsync` 置停止标志、取消全局 CTS（每个计时器 CTS 都与它链接）、用 TCS 等在途计数归零，全程不取 `gate`。`Dispose` 只置标志并取消，不取锁、不等待。
  - `ProtocolModule` 加一行注册，排空与协调器是同一单例。附录 B 未新增例外。
- **冲突用例**：`:109`、`:235` 改写；`:149` 在正式全量中通过，顺延 T-004c，写了后继用例；新判定的 `ScheduledTaskTests.Different_task…` 按 RQ-013 改写（意图变化）。见 `verification/task-07-conflicts.md` §9。
- **对账**：附录 A 第 5、6、7 行与第 11 行的 Joust 部分全部经执行器。E1 27 行逐行证明，探针本族归零（Checks 9、10）。

### Changed

**生产代码**：修改 25 个文件（＋478 −205），新增 1 个（`GameplayTimerWork.cs`，66 行），全部在 Owned boundary 内（`logs/task-11-boundary.txt`）。

| 文件 | 改动 |
|---|---|
| `Application/Execution/GameplayTimerWork.cs`（新） | 定时 tick 辅助 |
| `Application/Scheduling/ScheduledTaskHostedService.cs` | 注入队列；每次尝试一个作业；注释写明 `Task.Run`／并发槽留到 T-005 |
| `Application/Scheduling/ScheduledTaskExtensions.cs` | `AddTaskScheduling` 调 `AddGameplayExecution` |
| `Application/Events/JoustMaintenanceService.cs` | `TickAsync` 成为入口（timer／startup 两种 CMD），循环经合并源 |
| `Application/Notifications/NotificationRetryDrainService.cs` | 一次排空一个作业，`catch` 与日志留在原位 |
| Protocol 的 10 个 `*DailyRefreshService`（Fatigue、Premium、Mirror、Dimension、GoldSilverHorn、ReviveCoin、AltarQuota、TournamentQuota、Heras、ItemUsage） | `RefreshAsync` 成为入口，原体改名 `RefreshCoreAsync`；循环经合并源 |
| `DungeonPartyWipeService`、`WeaknessRecoveryService`、`CreatureClockService`、`MarriageWeddingScheduler`、`TournamentRewardSweepService` | 一次 tick 的内容搬进私有 `TickAsync`，循环经合并源 |
| `AltarScheduler`、`RoomMechanismMaintenanceService` | 循环体一个作业，返回下一个截止时间；房间机制的 `delivered` 字典只在作业里读写 |
| `BossTowerReturnSweepService`、`IllusionTowerReturnSweepService` | 新增公开 `TickAsync` 入口；一次 tick（排空离场队列，再扫描）一个作业 |
| `CardReward/DungeonCardPresentationCoordinator.cs` | 见 Outcome |
| `ProtocolModule.cs` | 一行：协调器注册为 `IGameplayProducerDrain` |

每个服务的构造器只增加一个 `IGameplayQueue` 参数；作业体逐字搬迁（婚礼调度器搬进方法时去掉了一层块，缩进少一级）。

**新增测试 N ＝ 34 ＋ 7 ＝ 41，删除 D ＝ 0**：

- Application 34：
  - `Execution/TimerSourceReconciliationTests`（E1，Theory）27 行：第 2～22 行各 1 行共 21，第 3′ 行 1，调度任务 5 个 ID 各 1 行共 5；21 ＋ 1 ＋ 5 ＝ 27；
  - `Execution/TimerSourceDownstreamTests` 3 条：E2a `A_fatigue_refresh_sends_from_its_timer_cmd_whether_the_loop_or_the_entry_drives_it`、E3 `A_due_tick_while_the_previous_one_waits_is_not_enqueued_beside_it`、E2b `An_altar_wake_up_round_reads_its_deadlines_on_the_executor`；
  - `Scheduling/ScheduledTaskExecutorTests` 1 条：E2c `Each_attempt_is_its_own_timer_job_and_the_retry_delay_leaves_the_executor_free`；
  - `Notifications/NotificationRetryDrainExecutorTests` 2 条：E2d `Every_pass_runs_on_the_executor_and_a_throwing_pass_does_not_end_the_loop`，以及裁定第 7 条要求补的 `A_pass_already_queued_when_the_producer_stops_still_runs`；
  - `World/IllusionTowerPublicFlowTests` 1 条（`PvfFact`）：§8.3 后继 `A_sweep_tick_after_the_old_runs_return_was_due_leaves_its_replacement_alone`；
  - 加法：27 ＋ 3 ＋ 1 ＋ 2 ＋ 1 ＝ 34。
  - 辅助（不含测试）：`Execution/TimerSourceTestSupport.cs`（记录型队列装饰器、占住执行器的作业）、`Execution/TimerSourceHost.cs`（生产组合 ＋ 临时 SQLite ＋ `SchedulerClock`）。
- Protocol 7：
  - `CardReward/DungeonCardPresentationCoordinatorTests.Timers.cs`（原类改为 `partial`）5 条：C1 `A_due_layout_timer_sends_its_frames_from_a_timer_cmd_on_the_worlds_executor`、C2 `A_due_auto_flip_selects_the_card_from_a_timer_cmd`、C3 `A_zero_delay_scheduled_from_a_job_opens_the_layout_after_that_job`、C4 `A_timer_cancelled_or_replaced_during_its_delay_never_runs_its_cmd`、C7 `The_drain_waits_for_a_timer_already_waiting_on_its_cmd_and_stops_the_rest`；
  - `CardReward/CardTimerShutdownTests` 2 条：C6 `The_coordinator_is_registered_as_one_of_the_producer_drains`、C5 `A_host_stopping_with_a_pending_layout_timer_cancels_it_before_the_executor_stops`；
  - 加法：5 ＋ 2 ＝ 7。
- **需要 PVF 的只有 §8.3 后继 1 条（`PvfFact`）。** E1 不需要：把测试二进制复制到工作区外重跑（配置路径、环境变量与各级祖先目录都找不到 `Script.pvf`），E1 27 行与其余新增 Application 用例全部通过，只有后继用例跳过；Protocol 新增 7 条同样全部通过。

**红的形态**（`logs/task-11-red.txt`）：

- E2c：解析不到 `IGameplayQueue`；
- E2a、E2b、E2d：`OnExecutor` 断言为假；
- E3 与「已入队的排空照常执行」：在 10 秒等待处超时；
- E1：在批 A 之后、批 B 之前 8 行通过，19 行（全部 Protocol 源）在 15 秒内没有记到该类型的 CMD；
- C1～C7：在改协调器之前编译红（CS1729、CS1061）。
- 行为上的红由变异核对补证（Checks 6）。

**既有测试文件 9 个**（差异核对 `logs/task-11-diff-check.txt`，脚本 scratchpad `t07/diffcheck7.py ca71eb26`）：

| 文件 | 类别 | 改动与理由 | 方法 ／ 断言行 |
|---|---|---|---|
| `Application.Tests/Notifications/NotificationRetryDrainServiceTests.cs` | (i) 构造实参 | 4 条各加一行自持执行器，`Service(...)` 多传队列 | 4＝4 ／ 4＝4 |
| `Protocol.Tests/Game/ItemUsageProjectionTests.cs` | (i) | 同上，1 处 | 3＝3 ／ 19＝19 |
| `Protocol.Tests/Game/UsageQuotaProjectionTests.cs` | (i) | 同上，GoldSilverHorn、TournamentQuota、Mirror 3 处 | 12＝12 ／ 65＝65 |
| `Application.Tests/Notifications/NotificationRegistrationTests.cs` | (i) 组合（计划未列出，批 A 跑 Application 时发现） | `The_registry_is_asked_for_before_the_first_hosted_service_starts` 的宿主只调 `AddNotificationDispatching`。重试排空服务现在要 `IGameplayQueue`，宿主在构造托管服务时先失败，错误信息里没有被测的通知名。加一行 `AddGameplayExecution()`；生产组合都经 `AddCommandDispatching` 有执行器 | 7＝7 ／ 16＝16 |
| `Application.Tests/World/TournamentRewardSweepTests.cs` | (ii) 直接调用作业体，包裹 | 反射调用 `SweepAsync(candidate)` 包进 `TestGameplay.RunAsync(shop.Services, …)`，断言行原样留在块里 | 4＝4 ／ 18＝18 |
| `Application.Tests/Inventory/OnlineExpiredItemCleanupTests.cs` | (ii) | `OnlineExpiredItemCleanupJob.RunAsync` 4 处包裹。计划写 3 处，第 4 处在 `Assert.ThrowsAsync` 里，探针 p1 记到了它；整条断言原样放进包裹块 | 7＝7 ／ 35＝35 |
| `Application.Tests/Scheduling/ScheduledTaskTests.cs` | (i) ＋ §8.4 改写 | 3 处 `Assert.Single(...GetServices<IHostedService>())` 改为按类型取调度器（`AddTaskScheduling` 现在也注册执行器生命周期服务）；`Different_task…` 按 RQ-013 改写 | 12＝12（1 条改名）／ 31→33，见下 |
| `Protocol.Tests/Game/CardReward/DungeonCardPresentationCoordinatorTests.cs` | 夹具 ＋ §8.1／§8.2 改写 ＋ (iv) | `World`／`PartyWorld` 各持执行器与记录队列并注入协调器，`Dispose` 先协调器后执行器；`BuildParty` 的门闩改为开关，默认关闭；`Build` 可传真实延时；记录型 sender／总线加上执行位置的记录；类改为 `partial` | 10＝10（1 条改名）／ 43→44，见下 |
| `Application.Tests/World/IllusionTowerPublicFlowTests.cs` | 新增后继用例 | 只加 1 个方法和 `SettlementFrames` 辅助，原 2 条未动 | 2→3 ／ 53→67（新增 14 行全属后继用例） |

逐条「旧断言 → 新断言 → 保护意图」：

- `ScheduledTaskTests`（3 处，同一形状）：`Assert.Single(provider.GetServices<IHostedService>())` → `Assert.Single(provider.GetServices<IHostedService>().OfType<ScheduledTaskHostedService>())` → 意图不变：恰有一个调度器。
- `ScheduledTaskTests.Different_task_can_finish_while_long_task_is_blocked_and_stop_cancels_it` → 改名 `Blocked_task_postpones_rather_than_loses_another_and_stop_cancels_it`。**这是 RQ-013 规定的意图变化。**
  - 旧：被卡住的任务占着时，另一任务能完成；停止会取消被卡住的那个并释放 scope；
  - 新：被卡住时另一任务不执行（`Assert.Equal(0, shortRuns)`）；放行后它执行一次（`Assert.Equal(1, shortRuns)`，推迟而不丢失）；停止会取消随后再次卡住的那个、释放其 scope，计时器归零；
  - 意图：「任务之间互不阻塞」被规格删除，「停止能取消卡住的任务」保留。
- `:109` `Simultaneous_party_auto_flips_retry_a_contended_column_and_award_each_member_once` → 改名 `Party_auto_flips_run_one_after_the_other_and_award_each_member_once`。
  - 旧：两人同时争用同一列，重试后各得一张，索引不同；
  - 新：保留「选择数 2、两人各一、索引不同」三条，加「`CardSelections.Count == 2`」与「每人恰好一帧 `SelectCardResponse`」；原来等 `CardSelections.Count == 2` 的那一行等待去掉，只等 `Bus.Selections == 2`；
  - 意图：每名成员得到且只得到一张；「争用后重试同一个个人确认」由 `:235` 守。
- `:235` `Auto_flip_retries_the_same_personal_confirmation_after_a_transient_conflict`（名字不变）：
  - 旧：`ReleaseAsync(2s)` 后同步断言 `Assert.Equal(1, world.Delay.Pending(4s))`；
  - 新：改为 `await WaitUntilAsync(() => world.Delay.Pending(4s) == 1)`；`ReleaseAsync(4s)` 之后先等 `Bus.Selections == 1`，再保留原断言；`Attempts == [0, 0]` 与索引 0 两条不变；
  - 意图不变：瞬时冲突后重试的是同一个个人确认。
- (iv) `Early_scroll_request_sends_the_ordered_pair_once_and_retires_layout_timer`、`Early_party_layout_requests_open_once_and_retire_the_layout_timers`：断言不变，各在 `Assert.Equal(n, Pending(4s))` 之前加一行等待。
  - 第一次全量中这两条失败（`Expected 1/2, Actual 0`）。原因：计时器先 `Task.Yield`（§3.1 要求），自动翻牌的延时因此异步登记，原来的同步断言在负载下抢在登记之前。
- `Late_party_layout_requests…` 与 `Early_party_layout_requests…` 靠 `BuildParty` 的默认值关闭门闩，断言不变。`FirstAttemptGate` 只在开关为真时使用，本单元没有调用方传真；保留它，供需要并发驱动首轮尝试的用例使用。

**文档**：

- `verification/task-07-conflicts.md` 追加 §9；
- 日志 `logs/task-11-{step0,red,build-test,isolated,new-x10,mutations,probe-summary,boundary,diff-check,slow-isolated}.txt` 与 `logs/task-11-probe-{Application,Protocol,Host,Networking}.txt`。

### Why（本地决定与偏离）

- **裁定第 7 条的读法**：裁定写「采用备选方案，生产者以 `None` 入队……停止令牌只用来停止下一次等待到期和入队」。备选方案原文是「以 `None` 入队、只把 `stoppingToken` 交给作业体」。
  - 本单元照备选方案原文做：作业体拿生产者令牌。
  - 理由：裁定第 6 条要保留「停止能取消卡住的调度任务」，这只能靠作业体看到停止令牌；作业体若拿 `None`，卡住的任务永不结束，执行器排空不了。
  - 代价：停机时已入队的 tick 会执行，但多半在第一处检查令牌时以取消退出。补测 `A_pass_already_queued_when_the_producer_stops_still_runs` 证明「已入队的会执行」。
  - 如果主控方的本意是作业体也不带停止令牌，需要另定调度任务的取消方式（Remaining 第 6 条）。
- **卡牌计时器也以 `None` 入队**，与 §3.1 原文「入队用计时器自己的令牌」不同。
  - C7 的期望（排空要等一个已在等作业的计时器）在原文方案下不成立：停止会让执行器直接取消排队中的计时器作业，排空立刻返回。
  - 改为 `None` 后 C7 成立，也与裁定第 7 条的口径一致。「取消之后不再打开布局」由作业体开头的令牌检查保证（C4）。
- **取消在作业里捕获再带出**：若让作业体直接抛取消，执行器会因调用方令牌是 `None` 而把它记成 `Gameplay timer:X failed`（Error）。那样每次停机都会出现这条日志，日志信号就失去意义。
- **两塔加了公开 `TickAsync` 入口**（§4 允许）：后继用例所用的 `ShopFixture` 时钟不能驱动宿主计时器。
- **`:149` 的后继用例**守的是「扫描以当前状态复核」。它不覆盖「捕获之后被顶替」这种交错，因为那种交错在串行下不再发生（§8.3）。
- **E1 的容器**用生产组合加 `NetworkingModule`（不开监听），构造全部托管服务，只启动被测的那一个。`SchedulerClock` 每推进一次让每个手动计时器触发一次，所以统一推进一天。重试排空服务用真实 `PeriodicTimer`，容器里把间隔设为 20 ms。

### §9 主动复核（逐项结论）

| 项 | 结论 |
|---|---|
| (i)～(vi) 计数 | (i) 4 个文件（含计划未列出的 `NotificationRegistrationTests`）；(ii) 2 个文件 5 处包裹；(iii) 0（全量 `GameplaySelfWait` 0）；(iv) 2 条（`Early_*` 两条，加等待）；(v) 新交错型冲突 1（§8.4，已改写）；(vi) 0 |
| 启动托管服务又用 `TestGameplay(<容器>)` 的文件 | `JoustIntegrationTests`：`StartAsync`／`TickAsync` 都在包裹外调用，包裹只用于 setup，没有在作业里等定时效果或停宿主；`OnlineExpiredItemScheduleTests`：作业里只有 `ReleaseExact`，等待命令记录在作业外；`ScheduledTaskTests`：不用 `TestGameplay`；Networking、Host 拉宿主的用例：全量与隔离中全部通过，`GameplaySelfWait`、`did not run` 均为 0 |
| `CardRewardReconnectIntegrationTests` | 真实 2／4 秒计时器，轮询在作业外，全量与隔离中通过 |
| N-3 的 21 处轮询 | 基准文件里 `WaitUntilAsync` 在包裹内 21 处，全部是 `TestGameplay.RunAsync(async () => …)`（不带容器，每次新建执行器）；计时器 CMD 落在 `World` 自己的执行器上，两者不互等。本单元又加 2 处同形（共 23 处）；包裹外 6 处 |
| `World.Dispose` | 先 `Coordinator.Dispose()`（不取锁），再 `Executor.Dispose()`；探针中协调器 `Dispose` 的记录为 0 |
| Weakness 等并发包裹（TASK-07 Remaining） | `WeaknessGoldRecoveryTests`、`FatigueRefreshTests`、`DungeonBuffUsageTests` 调 `RefreshAsync` 的位置都在包裹外，全量通过，没有互等 |

### Checks

1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 个警告，0 个错误（`logs/task-11-build-test.txt` 开头）。
2. **全量**（`logs/task-11-build-test.txt`；运行前后 `yes` 进程都是 0）：
   - 总数 50 ＋ 80 ＋ **1701** ＋ 475 ＋ 969 ＋ 173 ＋ **4487** ＋ 166 ＝ 8101 ＝ 8060 ＋ 41 － 0。
   - 逐项对照 TASK-10：Protocol 1694 → 1701（＋7）；Application 4453 → 4487（＋34）；HttpApi 50、Pvf 80、Persistence 475、Infrastructure 969、Networking 173、Host 166 相等。
   - 通过 50 ＋ 80 ＋ 1701 ＋ 475 ＋ 967 ＋ 171 ＋ 4469 ＋ 166 ＝ 8079；失败 2，只有那 2 条 `DimensionDungeonTests`；跳过 2 ＋ 2 ＋ 16 ＝ 20，新增用例没有跳过；8079 ＋ 2 ＋ 20 ＝ 8101。
   - 表征测试 8 通过、2 跳过；挂起转储 0；`## exit 1` 来自那 2 条已知失败。
   - 日志信号：
     - 为 0 的：`direct send deadline exceeded`、`did not drain within`、`GameplaySelfWait`；`Gameplay timer:… failed`、`Gameplay startup:… failed`；`Draining gameplay producer … failed`；`Dungeon card layout timer failed`、`Dungeon card auto flip failed`；§5.4 的「`… was not enqueued: the gameplay executor has stopped`」；`GameplayExecutorStoppedException`。
     - 非零的两项都与本单元无关：`Gameplay inbound:X failed` 3（`GameSellItem` 1、`GameSelectCharacter` 2，与 TASK-09／10 相同，是用例故意注入的失败）；`the drain tears it down` 2（TASK-10 的丢失关闭回调兜底，Game 与 Channel 各 1）。
   - **耗时比对**（与 `logs/task-10-build-test.txt` 逐条比，共同用例 7998 条，阈值 9 秒）：
     - 多出 ≥ 9 秒的 5 条：`PvfHellRewardDefinitionSourceTests.Real_archive…(level: 85)` ＋15 s、`GmDailyRefillHttpTests.Authentication_permission…` ＋13 s、`GoldSilverHornTests.Three_successful_entries…` ＋12 s、`GmDailyRefillHttpTests.Overrides_disable…` ＋10 s、`JoustIntegrationTests.Minimum_odds_mail_rewards…` ＋10 s。
     - 隔离复跑全部在 0.6～2 秒（`logs/task-11-slow-isolated.txt`），全部判为负载波动：没有 30 秒的 SQLite 忙等形状；没有 10 秒的释放排空（`did not drain within` 0）；也不是「定时作业排在测试作业之后」，那种情况在隔离复跑中同样会出现。
     - 各项目「总时间」（TASK-10 → 本单元，按测试数对应）：HttpApi 8.6 → 8.0 s；Pvf 11.2 → 12.9 s；Protocol 12.5 → 12.7 s；Persistence 28.0 → 27.0 s；Infrastructure 51.5 → 54.9 s；Networking 58.2 s → 1.02 min；Application 2.56 → 2.66 min；Host 2.72 → 2.67 min。**更正 TASK-10 Result**：它把前两项写反了。日志里 50 条用例那一块（HttpApi）是 8.6 秒，80 条那一块（Pvf）是 11.2 秒。
     - 共同用例逐条耗时按项目求和（TASK-10 → 本单元）：Application 1230.6 → 1243.7 s；Host 951.4 → 970.7 s；Networking 122.6 → 122.9 s；Protocol 47.4 → 38.6 s；Infrastructure 364.5 → 354.6 s；Persistence 25.6 → 24.4 s。
   - 第一次全量（scratchpad，已作废）中 `Early_*` 两条失败，处置见 Changed 的 (iv)。
3. **隔离复跑**（`logs/task-11-isolated.txt`）：Application 4487 条（4469 通过、2 失败即那 2 条、16 跳过）；Protocol 1701 条全部通过；Networking 173 条（171 通过、2 跳过）；Host 166 条全部通过。没有超时形态的失败。
4. **新增测试 ×10**（`logs/task-11-new-x10.txt`）：41 条隔离连续 10 轮，Application 34 × 10 ＋ Protocol 7 × 10 ＝ 410 次通过，0 失败，0 跳过；正式全量负载下 41 条全部通过。本机有 PVF，后继用例运行、未跳过；没有 PVF 时它跳过（见 Changed）。
5. **表征测试**：8 通过、2 跳过，`Characterization/` 目录未改。
6. **变异核对**（`logs/task-11-mutations.txt`；每项都用 scratchpad 备份，逐字节还原，`filecmp(shallow=False)` 相等，全部做完之后重新构建）：
   - V1 Fatigue 入口与循环直接调用作业体：E2a 红，E1 中 Fatigue 那一行红（其余 26 行通过）；
   - V2 Fatigue 循环绕过合并源，改为不等待地 `queue.RunAsync`：E3 红；
   - V3 调度任务的一次尝试改在执行器外执行：E2c 红；
   - V4 布局计时器到期后直接调用作业体，不入队：C1 红；
   - V5 去掉计时器起头的 `Task.Yield`：C3 红，异常为 `GameplaySelfWaitException: Waiting for gameplay work timer:DungeonCardPresentationCoordinator/layout must not happen on the gameplay executor`；
   - V6 删掉 `ProtocolModule` 的排空注册：C6 红；**C5 也红**。没有排空时，待触发的布局计时器要到宿主释放、协调器 `Dispose` 时才被取消，那已在执行器停止之后，用例在 20 秒内等不到「执行器停止前已取消」；
   - V7 重试排空改回执行器外：E2d 两条都红；
   - V8 Joust 的 `TickAsync` 与循环改回执行器外：E1 中 Joust 两行都红；
   - 「`Dispose` 不取锁」由探针 §3.9 的计数证明（Checks 9）。
7. **测试改动核对**：新增 41 条见 Changed（34 ＋ 7 ＝ 41）；既有 9 个文件的类别、差异核对与「旧断言 → 新断言」见 Changed。只改构造实参、组合或包裹方式的 6 个文件，方法集合与断言行多重集合全部相等。顺延的 `:149` 在正式全量中通过，后继用例见 Changed。
8. **生产改动的边界与不变量**（`logs/task-11-boundary.txt`）：
   - `git diff --stat -- Server/src`：25 个文件，外加未跟踪的 `GameplayTimerWork.cs`，都在 Owned boundary 内；
   - `grep IGameplayQueue`：相对 TASK-10 新增的命中只在本单元的 19 个 Protocol 服务、调度器、协调器、Joust、重试排空与 `GameplayTimerWork.cs`；
   - `grep SuppressFlow`：新增只有协调器一处；其余 4 处（`GameplayExecutor`、`ExecutorSendDeadline`、关连接器、未接线的 `OutboundSender`）同 TASK-10；
   - `grep RunDetached`：与 TASK-10 相同（定义 4 处、`cref` 1 处、`NetworkPlugins.cs` 中使用 4 处）；
   - `ScheduledTaskHostedService.cs` 里 `SemaphoreSlim`（第 69 行）与 `Task.Run`（第 129 行）仍在，留给 T-005；
   - **部分迁移期间的不变量**（拆分原则第 3 条）。等待定时作业的执行器外代码有：
     - 各托管服务的循环：只持 `PeriodicTimer` 或唤醒信号；
     - 调度任务的 `RunAsync` 任务：等待作业时占着并发槽；
     - 卡牌计时器的分离任务：只占在途计数；
     - Joust 的 `StartAsync`：宿主启动期，不持任何东西，`ticks` 信号量已移进作业；
     - 协调器的排空：等在途计数，不取 `gate`。
     - 它们都不持门。并发槽与在途计数都不是门：执行器上的作业从不等待它们，所以不构成环。
9. **探针复跑**（`logs/task-11-probe-summary.txt`；原始记录取 p2，`PROBE_LOG_PREFIX=task-11-probe`）：
   - **本族归零**（四个项目）：
     - §3.6 卡牌计时器：24／141 → 0／0；
     - §3.8 中最近入口帧属于 22 个服务或 `OnlineExpiredItemCleanupJob` 的记录：0；
     - §3.9 `DungeonCardPresentationCoordinator.Dispose`：55／216 → 0／0；
     - 以 `JoustMaintenanceService.StartAsync` 自身为最近入口的记录：0。
   - 说明一：`fam.py` 按最外层入口帧分组，把 Host 的 12 条记录（172 次）放进了 §3.8。这些记录的最近入口是 `GmBootstrapService.StartAsync` → 总线（P1、P3）；宿主在 Joust `StartAsync` 的续体上启动 `GmBootstrapService`，所以 Joust 的帧在栈底。它们属于 §3.9（TASK-13）。改正后 §3.8 ＝ 12 － 12 ＝ 0 条、172 － 172 ＝ 0 次；§3.9 ＝ 147 ＋ 12 ＝ 159 条、251 ＋ 172 ＝ 423 次。
   - 说明二：p1 中 §3.8 另有 2 条，来自 `OnlineExpiredItemCleanupTests.Failure_does_not_skip…` 在 `Assert.ThrowsAsync` 里直接调用作业体。包裹之后，p2 中为 0。
   - **残留与排除复跑**：`:149` 带出的 (C) 栈顶 `IllusionTowerProjectionDelivery.LeaveAndReturnAsync` 1／1、`SelectDungeonHandler.HandleMessageAsync` 2／4。排除 `:149` 复跑（x1，Application）后两者都为 0，本族各项也都为 0。
   - **本族以外各族**（TASK-10 p2 → 本单元 p2，记录／命中）：3.1 dispatcher/Received 1／1 → 1／1；3.1 Connected/Closed 0／0 → 0／0；3.2 1／1 → 1／1；3.3 79／114 → 79／114；3.4 0／0 → 0／0；3.5 事件 450／1912 → 459／1902；3.7 GM/HTTP 923／1968 → 868／1872；3.9（改正后）373／654 → 159／423；3.10 命令总线 3534／10688 → 3509／10698。
     - 3.10 的记录少 25 条、命中多 10 次，不逐条归因：原先计在 3.8 的周期扫描与调度任务所发的总线命令已在执行器上，剩下的 3.10 是测试直接调用总线。
   - **还原证明**：
     - p1、p2、x1 三轮快照前后的 sha1 都是 `4e83ae2ca59a52072a5c25b302c0b7754b1b7753`，`cmp` 相等；
     - 未跟踪文件 `GameplayTimerWork.cs` 前后 sha1 相等（`290c7c5a3de31eee25387ad5db8197da21fc1a99`）；
     - `snapcheck`：1581／1581，相等 1556，不等的 25 个就是本单元的 25 个修改，未跟踪 1 个；
     - `patch.py revert` 恢复 25 个文件并删除 `GameplayProbe.cs`；`grep TestGameplay|GameplayProbe Server/src` 为 0；
     - 之后以 `--no-incremental` 重新构建，再跑正式全量。
   - 探针轮的失败不作为证据：三轮都只有那 2 条 `DimensionDungeonTests`。
10. **对账行**：
    - 附录 A 第 5 行（后台服务与调度）：来源 Timer → `timer:<类型名>`（第 2、4～22 行共 20 个服务）、`timer:ScheduledTaskHostedService/<任务 ID>`（5 个）→ E1 对应的 25 行；下游证据 E2a（Fatigue）、E2b（祭坛）、E2c（调度任务）、E2d（重试排空）；RQ-006 接线 E3；探针 §3.8 为 0。
      - 留给 T-005：调度任务的 `Task.Run` 外壳与并发槽；各服务里已无人争用的门与信号量（含 Weakness 投递的 `deliveryGate`、Joust 的 `ticks`）。
      - 留给 T-006：直发改发件箱；删除重试排空服务（连同本单元加的那几行）。
      - 留给 T-007：一律新根改为继承链。
    - 附录 A 第 6 行（卡牌布局与自动翻牌计时器）：来源 Timer → `timer:DungeonCardPresentationCoordinator/layout`、`/auto-flip` → C1、C2、C3、C4、C7，停机 C5、C6；探针 §3.6 为 0、§3.9 `Dispose` 为 0。留给 T-007：计时器 CMD 是否继承调度它的入站 CMD 的链。
    - 附录 A 第 7 行（两塔离场队列）：保留队列，在 `timer:BossTowerReturnSweepService`／`timer:IllusionTowerReturnSweepService` 的 tick 作业里消费 → E1 两行；后继用例经 `IllusionTowerReturnSweepService.TickAsync` 驱动。
    - 附录 A 第 11 行的 Joust 部分：来源 StartupRecovery → `startup:JoustMaintenanceService`，周期来源 Timer → `timer:JoustMaintenanceService` → E1 两行，V8 证明区分力；`JoustIntegrationTests` 全部经入口驱动。
    - **分离点清单（3e）本单元之后**：`ExecutorSendDeadline.StartDetachedClose`（TASK-08，`SuppressFlow`）；关连接器（TASK-10，`SuppressFlow`）；卡牌计时器（本单元，`SuppressFlow` ＋ `Task.Yield`，到期后入队并等待）；TASK-05 的准入循环与排空循环（未接线，T-006 复核）；`RunDetached` 位置不变（插件的 Connected、Closed、两个 Received）。
    - **5a 中 `CreatureRuntimeDomainService.cs:274`**：它的调用入口 `CreatureClockService` 的每次 tick 已在执行器上（E1 一行）。那条门外写回退路径本身仍没有用例走到，反例归 T-004c 的判据。

### Remaining

1. **调度任务的 `Task.Run` 与并发槽**仍在，过渡期只决定哪个任务先排队，T-005 删除。一次尝试在等待作业时占着槽；槽不是门，不成环。
2. **长作业**：每日任务、Premium、配额刷新在一个作业里遍历全部在线会话。单作业时长由 M-1 按 `Gameplay timer:… completed` 的 Debug 耗时观察，拆分归 T-008（裁定第 3 条）。
3. **VC-8 提前的范围再扩大**：定时作业里的直发（Fatigue、Premium、婚礼、比武、卡牌、生物投影）受 2 秒期限约束，不读包的客户端会在 2 秒后被断开。T-006 之后消失。
4. **空闲作业**：50 ms 的比武扫描、250 ms 的全灭扫描、两个 500 ms 的两塔扫描、每秒一次的生物／婚礼／Joust，以及重试排空，空闲时约每秒 30 个空作业；M-1 时记录入站的排队延迟。
5. **双 Error 日志**：定时作业失败时，执行器与生产者各记一条（与入站、生命周期相同），交 T-008。
6. **裁定第 7 条的读法**（Why 第 1 条）：作业体拿生产者的停止令牌。如果主控方要求作业体不带停止令牌，需要同时给调度任务另定取消方式。
7. **`AddNotificationDispatching` 不自带执行器**：重试排空服务现在需要 `IGameplayQueue`，只调它的容器会在构造托管服务时失败。生产组合都有执行器。`NotificationDispatchingExtensions.cs` 不在本单元边界内，只改了暴露这一点的那条测试的组合；T-006 删除重试排空服务时，这一依赖随之消失。
8. **`:149`** 顺延 T-004c，其 (C) 残留由排除复跑证明。
9. **未提交**：没有 commit，也没有 stash。

### 主控方补充（结果审查通过之后，提交之前）

- **补充裁定第 7 条**（审查 N2）：
  - 「已入队的定时 CMD 在执行器停止前照常执行完」，在作业层面成立：以 `CancellationToken.None` 入队，作业一定会开始执行。
  - 作业体会拿到生产者的停止令牌。它和迁移前一样，在同一处检查同一个令牌；被取消时可以提前退出。这样做是为了让卡住的调度任务能被取消，执行器能排空。
  - 审查逐个服务核对过：没有哪一处原本会完成的状态写入被新跳过，持久写都在 Transactional 命令里。
  - 托管服务停机时，要等它自己那一次 tick 排到并结束，上限是 `ShutdownTimeout`。
- **更正 E1 的证明力**（审查 N1）：
  - E1 在作业入口记录 `IsOnExecutor`，只能证明「这个源经执行器入队」，不能证明「一次 tick 的整段逻辑都在作业内」。
  - 后者靠三样支撑：本单元的代码审读、E2a～E2d 的下游证据、探针各族归零。
  - Result 和对账行中凡是把 E1 写成「整段在作业内」的证据，都按这里理解。
- **交给 TASK-14**（审查 N3）：协调器排空与传输排空的先后顺序是有影响的。如果协调器先排空，队列中还没执行的入站结算 CMD 会抛出 `ObjectDisposedException`。现在模块顺序是 Networking 在 Protocol 之前，所以是安全的，但没有测试钉住这一点。TASK-14 要补一条判据，固定排空顺序（传输先于协调器），或者让协调器对排空之后的结算请求容错。
- **登记，不改**：
  - N4：`:109` 新增的 `SelectCardResponse` 断言与等待条件之间有一个窄竞争。如果它出现偶发失败，就把等待条件改为等帧到达。
  - N5：`:149` 的后继用例区分力有限，它符合计划 §8.3。
  - N6：Result 与 `task-07-conflicts.md` 用的标签体系不同；`:109` 的代码被移进了 `TestGameplay.Run`。
  - N7：探针改归时依据的栈和分析输出没有落盘。
