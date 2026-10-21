# TASK-07 探针报告

- 日期 2026-10-22，工作区基于 `c19b3b7b`。探针只在运行期间改动 `Server/src`，用后全部还原，见 §8。
- 原始记录落盘（每条记录对应一个不同的「挂点 ＋ 栈」；(A) 一行一条，(B)、(C) 附 Dfo 栈）：
  - `logs/task-07-probe-Application.txt`（第 7 轮）；
  - `logs/task-07-probe-{Persistence,Protocol,Host,Networking}.txt`（第 6 轮）。
- 一次性工具都在 `/private/tmp/claude-501/-Users-puxianguo-project/19adbc1e-6ba1-4530-bca0-dcab5d727d8a/scratchpad/t07/probe/`：
  - 改源码：`patch.py apply|revert`；
  - 记录器：`GameplayProbe.cs`；
  - 分类：`classify.py`、`wrapat.py`、`plan.py`、`p1bsplit.py`；
  - 汇总：`agg.py`、`alist.py`；
  - 落盘：`emit.py`；
  - R-2 核对：`adiff.py`、`lostcaller.py`；
  - 快照：`snapshot.py`、`snapcheck.py`。
  - 各轮的完整原始转储（Application 一轮约 30 至 140 MB）也在这个目录的 `r0`～`r7`、`x1`～`x3` 下，不进仓库。

## 1. 方法

### 1.1 记录器与挂点（TASK-07 §3）

- **记录器** `Abstractions/Execution/GameplayProbe.cs`，探针运行期间临时新增：
  - 每次命中时，若 `!GameplayJob.IsOnExecutor`，记下挂点名和 `new StackTrace(1, false)` 的前 40 帧（声明类型、方法名、程序集名），按「挂点 ＋ 栈」去重计数；
  - 写到 `GAMEPLAY_PROBE_OUT/<进程 ID>.txt`，时机是进程退出（`ProcessExit`）和每 20 秒一次；
  - 最终一版另外在记录**第一次出现时**立刻追加到 `<进程 ID>.new.txt`，防止进程不经 `ProcessExit` 退出时丢掉最后 20 秒的记录。
- **P1**：`DomainTransactionScope.Begin`，`readOnly == false` 时。
- **P2**：附录 F「改为执行器内访问」那几个类里的全部 `lock (…)`，共 173 处，改为 `lock (GameplayProbe.Touch(x, "P2:<类名>"))`。
  - `Touch` 返回同一个对象，泛型保留原类型，锁的语义不变。
  - 实际的锁对象不只是 `sync`，还有 `gate`、`this.gate`、`owner.gate`、`owner.sync`，全部计入。
  - 涉及的类：`OnlineStateRegistry`、`LoginSessionRegistry`、`DungeonRunDomainService`（五个分部文件）、`CreatureRuntimeDomainService`、`PartyService`、`TownAreaDomainService`、`DungeonParticipationDomainService`、`DungeonCardPresentationCoordinator`、`BossTowerDomainService`、`IllusionTowerDomainService`、`TournamentDungeonDomainService`、`AltarDungeonEngine`。
  - `OnlineCharacterRuntimeDomainService` 不用锁，是 CAS 写法，改为在它对 `entries` 的 10 处访问上挂 `Touch`。
- **P3**：
  - `UserExecutionGate<T>.AcquireAsync`：覆盖 Online/Account 门、过渡门，以及城镇、副本、两塔的 Projection/Entry 门，挂点名带运行时类型名；
  - `SelectedSessionTransitionGate` 的 `AcquireManyExclusiveAsync` 与 `BorrowOrAcquireAsync`；
  - `TransactionalExecutionGate.AcquireAsync`；
  - `AchievementTitleProjectionGate.EnterAsync`；
  - 队伍按角色的信号量（`gates[acquired].WaitAsync`）；
  - `PartyDeliveryStream.Gate.WaitAsync`（2 处）。
- **P1b（补充挂点，计划之外）**：在 `Server/src` 里全部 5 处直接 `new DomainTransaction()` 之前记录。
  - 位置：`CharacterStateAccess.cs` 3 处、`ItemContainers.cs` 1 处、`CreatureRuntimeDomainService.cs:274`。
  - **理由**：TASK-03 记下的「门外自开事务写」里，`CharacterStateAccess.cs:159`（`AloneAsync`）与 `CreatureRuntimeDomainService.cs:274` 根本不经过 `DomainTransactionScope.Begin`，P1 看不见它们。不补这个挂点，就答不了 Required check 5「三处是否出现」那一问。
  - 它属于 P1 一族，即「写 SQLite 的工作单元开启」的补充。分类时按构造它的方法拆开：
    - `ReadAsync` 记为 **P1b-read**：边界外的读，开了就回滚，不是写单元；
    - `ChangeAsync` 记为 **P1b-volatile**：边界外写易失存储；
    - `AloneAsync` 记为 **P1b-teardown**：拆除时写位置到 SQLite；
    - `CreatureRuntimeDomainService` 那处记为 **P1b-creature**。
  - **「(B) 必须为 0」的约束只用在 P1、P2、P3 与 P1b 的写类上**。P1b-read 单列，作为给 T-004c 的信息，见 §4.3。

### 1.2 分类（TASK-07 §3.5，以及本单元对口径的补充）

对每条记录，从挂点往外找：

- **入口帧**：
  - 计划列出的那些：`CommandBus`、`Pipeline.*Behavior`、`NetworkMessageDispatcher`、`DomainEventDispatcher`、`OnlineUserConnectionObserver`、`LoginSession*Runtime/Maintenance*`、`ScheduledTask*`、`*HostedService`/`*MaintenanceService`/`*ClockService`/`*SweepService`/`*StartupService`/`*BootstrapService`/`*DrainService`、`NetworkPlugin` 系、TouchSocket 程序集、GmWeb/HttpApi 程序集；
  - 本单元的补充，逐条有出处：
    1. **泛型类型名去掉反引号元数**。否则 ``AccountExecutionBehavior`1`` 不会被 `\w*Behavior$` 认出来，经管线的记录会被误判成 (B) 或 (C)。
    2. **`TransactionalExecutionGate` 不算入口**。它也在 `Commands.Pipeline` 命名空间里，但幻影塔、领主塔、`TransactionalDungeonVisitDomainService`、`DungeonLifecycleDomainService`、`DomainEventDispatcher` 都直接持有它。四个分工者都独立报告了这一点。
    3. **补上计划名单漏掉的后台服务**：`*RefreshService`（11 个每日刷新服务）、`*Scheduler`（`AltarScheduler`、`MarriageWeddingScheduler`）、`*Job`（`OnlineExpiredItemCleanupJob` 与三个 `*DailyRefillJob`，不含 `GameplayJob`）、`DungeonPartyWipeService`、`WeaknessRecoveryService`、`MarriageRelationAudit`。它们都是 `BackgroundService`、`IHostedService` 或调度任务，属 §2.4「后台服务的 tick 与调度」。
    4. **附录 A 列明的入口**：`EffectOperationLedger`（效果账本重放），以及 `DungeonCardPresentationCoordinator.RunLayoutAsync`/`RunAutoFlipAsync`（卡牌延时计时器）。
    5. **容器释放与宿主启停**：`Microsoft.Extensions.DependencyInjection` 的 `Dispose`/`DisposeAsync`，以及 `Microsoft.Extensions.Hosting.Internal.Host`。这是停机路径（T-004b 3i），不是测试体在访问玩法状态。
- **测试帧**：程序集名以 `.Tests` 结尾的帧。
  - 执行器与辅助本身的帧（`Dfo.Server.Application.Execution`、`Dfo.Server.Abstractions.Execution`、`TestGameplay`）是管道，不算测试帧，也不算入口帧。
  - **测试归属**取「最靠近挂点、且是已知测试方法」的那一帧，不取最外层帧。
  - 原因：一个作业结束时，`TaskCompletionSource` 会在执行器线程上**同步**续上等待它的测试代码，而这段续体已经不在作业里。这时最外层的测试帧是 `TestGameplay.OnOwnExecutorAsync`，按最外层归属会张冠李戴。
- **(A)**：栈上有入口帧。
  - 计划原文是「入口帧比测试帧更靠近挂点，或者只有入口帧」。本单元按「栈上有入口帧」判定，因为被注入生产对象的测试替身（例如 `RecordingSender`）会以测试帧的身份出现在入口**内部**。按原文会把这些记录误判成 (B)。
- **(B)**：没有入口帧，有测试帧。
- **(C)**：两种帧都没有。原因有两个：栈被 await 恢复截断；或者栈太深，40 帧的上限截掉了外层入口帧。

### 1.3 轮次

| 轮 | 范围 | 用途 |
|---|---|---|
| r0 | Application，迁移前 | 迁移的检索工具（下段说明），也是 R-2 对比的基准 |
| r1～r5 | Application；r4、r5 是五个项目 | 每一批改动之后补包、复跑 |
| x1～x3 | 排除冲突总清单全部用例之后复跑 | 给截断的记录和替身记录定归属（§4、§5） |
| r6 | 五个项目，最终一版记录器（加 P1b） | Persistence、Protocol、Host、Networking 的正式记录 |
| r7 | Application（r6 之后又补了 6 处） | Application 的正式记录 |

**与计划顺序的偏离**：计划是先按静态检索迁移，再跑探针。本单元在迁移**之前**先跑了一轮（r0），把探针当成检索工具：

- (B) 记录直接给出「哪个测试、经哪个辅助或夹具」碰到了 T1～T3；
- 静态检索看不见这条经夹具的间接路径。r0 的 (B) 覆盖 1923 个测试方法，静态检索只能找到 231 个候选文件；
- 这仍在 Required check 5 的「补包重跑」循环里，`Server/src` 的临时改动范围同授权，每次还原都做了快照比对（§8）。

## 2. 计数（最终轮：Application 为 r7，其余为 r6）

单位：不同的「挂点 ＋ 栈」记录数 ／ 命中次数。

| 项目 | 族 | A 记录 | A 次数 | B 记录 | B 次数 | C 记录 | C 次数 |
|---|---|---:|---:|---:|---:|---:|---:|
| Application | P1 | 106 | 1585 | 11 | 12 | 0 | 0 |
| Application | P1b-read | 17 | 409 | 146 | 3496 | 8 | 21 |
| Application | P2 | 5785 | 19871 | 322 | 476 | 1 | 1 |
| Application | P3 | 2308 | 5360 | 112 | 1568 | 0 | 0 |
| Persistence | P1 | 3 | 59 | 1 | 1 | 0 | 0 |
| Persistence | P2 | 7 | 73 | 5 | 5 | 0 | 0 |
| Persistence | P3 | 30 | 61 | 1 | 1 | 0 | 0 |
| Protocol | P1 | 3 | 7 | 0 | 0 | 0 | 0 |
| Protocol | P1b-read | 0 | 0 | 1 | 1 | 0 | 0 |
| Protocol | P2 | 58 | 193 | 56 | 75 | 7 | 14 |
| Protocol | P3 | 17 | 28 | 61 | 83 | 18 | 29 |
| Host | P1 | 604 | 1359 | 0 | 0 | 0 | 0 |
| Host | P2 | 289 | 4569 | 1 | 1 | 0 | 0 |
| Host | P3 | 684 | 1621 | 0 | 0 | 0 | 0 |
| Networking | P1 | 84 | 689 | 0 | 0 | 1 | 2 |
| Networking | P1b-read | 87 | 555 | 1 | 1 | 10 | 93 |
| Networking | P1b-teardown | 8 | 26 | 0 | 0 | 0 | 0 |
| Networking | P2 | 1287 | 7529 | 27 | 99 | 16 | 55 |
| Networking | P3 | 274 | 1318 | 0 | 0 | 0 | 0 |

按族合计（只计 P1、P2、P3 三族；P1b 单列）：

- P1：A 106 ＋ 3 ＋ 3 ＋ 604 ＋ 84 ＝ 800；B 11 ＋ 1 ＋ 0 ＋ 0 ＋ 0 ＝ 12；C 0 ＋ 0 ＋ 0 ＋ 0 ＋ 1 ＝ 1。
- P2：A 5785 ＋ 7 ＋ 58 ＋ 289 ＋ 1287 ＝ 7426；B 322 ＋ 5 ＋ 56 ＋ 1 ＋ 27 ＝ 411；C 1 ＋ 0 ＋ 7 ＋ 0 ＋ 16 ＝ 24。
- P3：A 2308 ＋ 30 ＋ 17 ＋ 684 ＋ 274 ＝ 3313；B 112 ＋ 1 ＋ 61 ＋ 0 ＋ 0 ＝ 174；C 0 ＋ 0 ＋ 18 ＋ 0 ＋ 0 ＝ 18。
- P1b：
  - read：A 17 ＋ 87 ＝ 104；B 146 ＋ 1 ＋ 1 ＝ 148；C 8 ＋ 10 ＝ 18；
  - teardown：A 8，B 0，C 0；
  - volatile：0；creature：0。

迁移前（r0，只跑了 Application；那时没有 P1b，分类器是后来修正后的那一版）的 (B) 覆盖 1923 个测试方法。第一版分类器下：P1 B 3096、P2 B 25333、P3 B 301 条记录。最终 (B) 只剩下 §4 所列的那些。

## 3. (A) 按入口分组（交给 T-004b）

按附录 A 的入口族分组，再按具体入口列出。

- 「具体入口」取栈上最外层的那个非总线、非管线的 Dfo 入口帧；只有传输或宿主内部帧时，取那一帧。
- 「首个触达的状态」是离挂点最近的生产类型，按次数取前四个。
- 次数是最终轮五个项目合计；P1b 只列写类与读类各自的次数。

### 3.1 入站协议（dispatcher 与传输回调）

- `NetworkMessageDispatcher.DispatchAsync`（Application、Host、Networking）：P1 7，P1b-read 364，P2 10117，P3 1959。
  - 首个触达：`OnlineStateRegistry`、`UserExecutionGate`、`LoginSessionRegistry`、`SelectedSessionTransitionGate`。
- `GameNetworkPlugin.OnTcpReceived`（Host、Networking、Protocol）：P1 59，P1b-read 413，**P1b-teardown 12**，P2 2379，P3 397。
  - 首个触达：`LoginSessionRegistry`、`OnlineStateRegistry`、`SelectedSessionDomainService`、`AmbientStoreAccess`。
- `NetworkPluginBase.OnTcpClosed`（Host、Networking、Protocol）：P1 18，**P1b-teardown 13**，P2 981，P3 101。
  - 首个触达：`OnlineStateRegistry`、`UserExecutionGate`、`LoginSessionRegistry`、`OnlineCharacterRuntimeDomainService`。
- `NetworkPluginBase.OnTcpConnected`（Host、Networking、Protocol）：P2 78，首个触达 `OnlineStateRegistry`。
- `ChannelNetworkPlugin.OnTcpReceived`（Host、Networking）：P2 12，首个触达 `LoginSessionRegistry`。
- dispatcher 内的 lambda `NetworkMessageDispatcher.<DispatchAsync>b__0`／`b__1`：P2 126／P3 10。

### 3.2 效果账本重放

- `EffectOperationLedger.DispatchAsync`（Application）：P1b-read 31，P2 31，P3 441。
  - 首个触达：`TransactionalExecutionGate`、`StoreBackedItemContainer`、`OnlineStateRegistry`、`UserExecutionGate`。

### 3.3 连接观察者与关连接清理

- `OnlineUserConnectionObserver.DisconnectedAsync`（Application）：P2 84，P3 17。
- `OnlineUserConnectionObserver.ConnectedAsync`（Application）：P2 7。
- `OnlineUserConnectionObserver.CleanupSelectedAsync`（Application）：P2 6。
- `OnlineUserConnectionObserver.CleanupSelectedHeldAsync`（Networking）：P2 2，P3 2。首个触达 `PartyProjectionStreamRegistry`、`PartyNotificationDelivery`。
- 传输层的关闭回调，见 §3.1 的 `OnTcpClosed`。

### 3.4 登录会话运行时与维护

- `LoginSessionMaintenanceService.CloseSupersededAsync`（Host）：P2 1，首个触达 `OnlineStateRegistry`。

### 3.5 事件分发（订阅）

| 入口 | 项目 | 次数 |
|---|---|---|
| `DomainEventDispatcher.DrainOnceAsync` | Application | P1 243，P2 202，P3 249 |
| `DomainEventDispatcher.ReliableAsync` | Application、Host、Networking | P1 288，P2 7，P3 197 |
| `DomainEventDispatcher.WaitForWakeOrDueAsync` | 五个项目中的四个 | P1 140，P2 71，P3 241 |
| `DomainEventDispatcher.LiveOnlyAsync` | Application、Host、Networking | P1 19，P1b-read 52，P2 57 |
| `DomainEventDispatcher.RunGroupAsync` | 同上 | P1 22，P2 3，P3 35 |
| `DomainEventDispatcher.ProcessAsync` | Application、Host | P1 3，P1b-read 13，P2 4 |

首个触达：`TransactionalExecutionGate`、`DomainTransactionScope`、`OnlineStateRegistry`、`AltarStateStore`。

### 3.6 卡牌展示计时器

- `DungeonCardPresentationCoordinator.RunLayoutAsync`（Protocol）：P2 74。
- `DungeonCardPresentationCoordinator.RunAutoFlipAsync`（Protocol）：P2 67。
- 两者首个触达 `DungeonRunDomainService`、`DungeonCardPresentationCoordinator`。
- 在测试里由替身 `ControlledDelay` 触发，见 TASK-06 审查 N-3。

### 3.7 GM/HTTP 端点（Host、部分 Networking）

| 入口 | 次数 |
|---|---|
| `HttpApiExceptionMiddleware.InvokeAsync` | P1 552，P2 146，P3 805 |
| `GmAccessBoundary.InvokeAsync` | P1 2，P2 32，P3 93 |
| `CredentialTransportGateMiddleware.InvokeAsync` | P2 70，P3 22 |
| `GmCeraEndpoints.Grant` | P1 1，P2 38，P3 20 |
| `GmInitializationEndpoints.Safe` | P3 54 |
| `GmPlayerDirectoryEndpoints.Safe` | P2 16，P3 34 |
| `HttpListenerBoundaryMiddleware.InvokeAsync` | P1 3，P2 20，P3 9 |
| `GmAuthenticationEndpoints.LoginAsync` | P1 8 |
| `GmOfficialMailEndpoints.Send` | P3 6 |
| `GmPlayerDirectoryEndpoints.<Map>b__0_3` | P2 4 |
| `GmOfficialMailEndpoints.Safe` | P3 2 |
| `GmInitializationEndpoints` 与 `GmDailyRefillEndpoints` 的 3 个 lambda | 各 P1 1 |

### 3.8 后台服务与调度

| 入口 | 次数 | 首个触达 |
|---|---|---|
| `TournamentRewardSweepService.ExecuteAsync` | P2 2149 | `TournamentDungeonDomainService` |
| `AltarScheduler.ExecuteAsync` | P2 796 | `AltarDungeonEngine` |
| `DungeonPartyWipeService.ExecuteAsync` | P2 736 | `DungeonRunDomainService` |
| `RoomMechanismMaintenanceService.ExecuteAsync` | P2 208 | `DungeonRunDomainService` |
| `IllusionTowerReturnSweepService.ExecuteAsync` | P2 132 | `IllusionTowerDomainService` |
| `BossTowerReturnSweepService.ExecuteAsync` | P2 132 | `BossTowerDomainService` |
| `TournamentRewardSweepService.SweepAsync`（测试直接驱动） | P2 99，P3 31 | `OnlineStateRegistry`、`PartyService` |
| `CreatureClockService.ExecuteAsync` | P2 85，P3 6 | `CreatureRuntimeDomainService` |
| `ScheduledTaskHostedService`（`ExecuteAsync`／`WaitAsync`／`RunAsync`／`<Start>b__0`） | P2 47／63／1／13 | `DungeonCardPresentationCoordinator`、`OnlineStateRegistry` |
| `PremiumDailyRefreshService.RefreshAsync` | P2 62，P3 31 | `OnlineStateRegistry` |
| `JoustMaintenanceService.TickAsync`／`StartAsync` | P2 53，P3 53／P1 1，P3 2 | `TransactionalExecutionGate` |
| `OnlineExpiredItemCleanupJob.RunAsync` | P2 19，P3 8 | `OnlineStateRegistry`、`SelectedSessionTransitionGate` |
| `FatigueDailyRefreshService.RefreshAsync` | P2 10，P3 5 | `OnlineStateRegistry` |
| `NotificationRetryDrainService.DrainUntilStoppedAsync` | P2 12 | `OnlineStateRegistry` |
| `MirrorQuota`／`GoldSilverHornQuota`／`TournamentQuota`／`ItemUsage` 四个 `*DailyRefreshService.RefreshAsync` | P3 4／3／3／2 | `UserExecutionGate` |

### 3.9 启动与停机（宿主、容器释放）

| 入口 | 次数 | 首个触达 |
|---|---|---|
| `GmBootstrapService.StartAsync` | P1 113，P3 145 | `TransactionalExecutionGate`、`DomainTransactionScope` |
| `GameServerStartupService.StartingAsync` | P1 174 | `EpicBookDomainService`（3j：EpicBook 迁移） |
| `Host.ForeachService`／`Host.DisposeAsync`／`Host.Dispose`／`ServiceProvider.Dispose` | P2 82／5／2／1 | 全是 `DungeonCardPresentationCoordinator.Dispose` |

**给 T-004b 的提醒**：协调器的 `Dispose` 会在停机释放容器时取它的锁。如果 T-004c 的判据把它当作玩法状态写入口，停机时会在执行器外抛出。所以 3i 的停机顺序要么让协调器在执行器排空之前释放，要么把它列为判据的豁免。

### 3.10 命令总线（经总线进入的命令）

| 入口 | 项目 | 次数 |
|---|---|---|
| `CommandBus.SendAsync` | 五个项目 | P1 1839，P1b-read 17，P2 3816，P3 3053 |
| `CommandBus.<SendAsync>b__2` | Application、Host、Networking | P1b-teardown 1，P2 1236，P3 2 |

另有约 7 种「只剩管线行为帧」的记录：`SelectedSessionExecutionBehavior`、`CommandLoggingBehavior`、`OnlineUserExecutionBehavior`、`AccountExecutionBehavior`、`TransactionalExecutionBehavior`、`SelectedSessionSettlementBehavior`、`UnitOfWorkBehavior`。它们的 `CommandBus` 帧被 await 恢复或 40 帧上限截掉了，实质仍是经总线进入。

总线的调用方既有测试，也有生产入口。由测试直接调用总线的，按 §2.4 是入口用例，翻转（T-004c）后自然进入执行器。

### 3.11 TASK-03 记下的三处「门外自开事务写」

| 位置 | 是否出现 | 由哪些测试触发 |
|---|---|---|
| `SetUserAreaHandler` 跨区落位置 | **出现**：经 `GameNetworkPlugin.OnTcpReceived` → dispatcher → `SetUserAreaHandler` → `SelectedSessionPositionPersistence.AloneAsync`（P1b-teardown 12 次），同一路径上 P1（`DomainTransactionScope.Begin`）还有 17 次命中，都是 (A) | 栈上看不到测试帧（TouchSocket 接收线程）。按静态检索：Networking 表征测试里发 `SET_USER_AREA`（0x0024）的 `CharacterizationSteps.TwoPlayersInTownAsync` 与 `WireClient.MoveToSharedAreaAsync`，调用方是 `PartyCharacterizationTests` 4 条、`PartyTowerCharacterizationTests` 1 条（`SelectCharacterCompensationCharacterizationTests` 里的调用在跳过的用例中）。Protocol 的 `TownProjectionHandlerTests.AreaCrossingWaitsForTheSameAccountPartyTransitionGate`（冲突用例，T-004c）直接调用 handler，记为 (B) |
| `CharacterStateAccess.cs:159` 的 `AloneAsync`（拆除时落位置） | **出现**：P1b-teardown 共 26 次，全是 (A)。入口是 `NetworkPluginBase.OnTcpClosed`（13 次，断连拆除）、上一行的 `SetUserAreaHandler` 路径（12 次），以及 `CommandBus.<SendAsync>b__2`（1 次） | 断连拆除由 Networking 中「选角入世后断开连接」的真实 socket 用例触发，栈上同样没有测试帧。迁移前在 Application 由 `OutsideTransactionTests` 的 `WhereACharacterWasStandingIsSavedWhenNoUseCaseIsOpen`、`Teardown_in_a_town_without_an_inn_preserves_the_previous_inn`、`ChangingSomethingThatOutlivesNothingIsAllowedOutsideAUseCase`（易失写）与 `RememberedTownTests` 的 2 条直接调用（r6 的 (B)）；本单元已把这 5 条整方法包裹，r7 中它们在执行器上 |
| `CreatureRuntimeDomainService.cs:274`（访问器不是 `DomainTransactionScope` 时自建事务） | **未出现**：五个项目 0 次 | 所有测试容器和生产组合根注入的访问器都是 `DomainTransactionScope`，这个分支没有用例走到。T-004b 迁移它时需要自己补反例 |

## 4. (B) 逐条对照（Required check 5）

**口径**：P1、P2、P3 以及 P1b 的写类，按测试方法计。对照用的清单就是 `task-07-conflicts.md` 的机器可读版 `conflicts.py`。

| 项目 | 有 (B) 的测试（键） | 在冲突总清单里 | 在「有意留在作业外」一节（`task-07-conflicts.md` §6） | 另作归属 |
|---|---:|---:|---:|---:|
| Application | 82 | 78 | 0 | 4 |
| Persistence | 2 | 2 | 0 | 0 |
| Protocol | 40 | 29 | 7 | 4 |
| Host | 1 | 1 | 0 | 0 |
| Networking | 11 | 0 | 11 | 0 |

加法：82 ＝ 78 ＋ 4；2 ＝ 2；40 ＝ 29 ＋ 7 ＋ 4；1 ＝ 1；11 ＝ 11。

「另作归属」的 8 条都是**截断记录或替身记录**：栈上没有清单里的测试方法名，只有被生产代码回调的替身，或者门放开后续上的生产续体。

- **Application 4 条**：
  - `SelectCharacterLifecycleTests+RecordingSender.<SendClassifiedAsync>`；
  - `Notifications.RecordingSender.SendAsync`（K5 的 `NotificationDeliveryTests`；结果审查 N-1 更正）；
  - `ReplayStateSnapshotTests+DelayedInventory.LoadCharacterAsync`；
  - `CommonExperienceEffectTests+CallbackCloser.<RequestCloseExactAsync>`。
  - 归属证据，两轮排除复跑：
    - **x1**：只跑这三个类，排除其中的冲突方法（选角生命周期 5 条、重放快照 4 条、通用经验 4 条），(B) 0，(C) 0；
    - **x3**：跑整个 Application.Tests，排除整份冲突总清单（`excl.filter`），共 4340 条，(B) 0，(C) 0。
  - 所以这 4 条记录全部来自清单里的冲突用例。
- **Protocol 4 条**：
  - `BossTowerProjectionConcurrencyTests` 与 `IllusionTowerProjectionConcurrencyTests` 的 `SendAsync` 续体；
  - `SessionPresenceDomainService` 的 `Find`／`TransferExact`。
  - 栈顶都是 `SelectedSessionTransitionGate.CompleteAcquisitionAsync`／`AcquireGroupAsync`，即门放开后续上的等待者。
  - 归属证据：**x2** 跑整个 Protocol.Tests，排除整份清单，共 1654 条，(B) 只剩 §6 那 7 条 `Dispose`，(C) 0。
- **Protocol 的 7 条「有意留在作业外」**：`DungeonCardPresentationCoordinatorTests` 中用 `World`／`PartyWorld` 的 7 个用例，方法末尾 `Dispose` → `Coordinator.Dispose()`。这是 TASK-06 Remaining 第 3 条，按 TASK-07 §6.1 原样保留。
- **Networking 的 11 条「有意留在作业外」**：全部是轮询入口进度。
  - 逐条记录核过：每条 (B) 记录的栈里都有 `WaitUntilAsync`；只有 1 条例外，是 `BossTowerFrameIntegrationTests` 第 68 行的 `while (!online.TryGetByConnection(…)) await Task.Delay(…)`，同样是轮询。
  - 涉及 `GameLoginIntegrationTests`、`CardRewardReconnectIntegrationTests`、`IllusionTowerStageFrameIntegrationTests`、`BossTowerFrameIntegrationTests`。
- Host 的 1 条是 `GmPlayerSamplingHttpTests.OnlineListUsesOneCapturedSetForTotalAndItemsEvenWhenDisconnectOccursDuringDatabaseRead`，本单元把它补进了冲突总清单（T-004b），理由见那份清单。
- 补包重跑的过程（TASK-07 §3.6）：
  - r1 到 r7 之间共补包 1287 行，统计见 `task-07-diff-check.md` §7；
  - 其中按探针 (B) 补的 TASK-06 项目：Persistence 3 行（`EpicBookTests`）、Protocol 10 行（`ComposeAsync`）、Host 5 行、Networking 6 行（`GameLoginIntegrationTests` 的快照读）。

### 4.1 P1b-read 的 (B)（信息，不在「必须为 0」之内）

- Application 146 条记录、Protocol 1 条、Networking 1 条。
- 它们是测试在用例边界外读物品容器或角色状态：经 `StoreBackedItemContainer.ReadAsync`／`AmbientStoreAccess.ReadAsync` 开一次性事务，读完就回滚，按设计是「边界外读放行」。
- 分布在 28 个类里，多数是在作业外断言提交结果的那几行。
- 它们不是写单元，也不碰附录 F 的玩法状态，不属于 T1～T5。
- **给 T-004c**：判据如果挂在 `new DomainTransaction()` 的全部构造上，这些读都会被拒。所以只读的一次性事务要与写单元区分开。

## 5. (C) 分诊（每个不同的栈顶）

栈顶取栈里最外层的生产帧；没有入口帧，也没有测试帧。

| 栈顶 | 项目 | 族 | 结论 |
|---|---|---|---|
| `SelectedSessionTransitionGate.CompleteAcquisitionAsync` | Application、Protocol | P2、P3 | 门放开后续上的等待者，只在门上有争用时出现。x1、x2、x3 排除冲突用例后为 0，**归冲突用例** |
| `SelectedSessionTransitionGate.AcquireGroupAsync` | Protocol | P2、P3 | 同上 |
| `PartyProjectionStreamRegistry.EnterParticipantsAsync` | Protocol | P3 | 队伍流门的等待者续体。x2 排除后为 0，**归冲突用例**（`PartyNotificationTests`、`PartyTownProjectionDeliveryTests`） |
| `DomainEventDispatchingExtensions.<AddDomainSubscriber>b__2_1`、`ExpiredItemsNotificationSubscriber.HandleAsync`、`SouthernDailyRefillNotificationSubscriber.HandleAsync` | Application | P1b-read | 事件订阅者被分发器驱动后，await 恢复截断了栈。**入口驱动，归 (A)**，只读 |
| `IllusionTowerProjectionDelivery.LeaveAndReturnAsync` | Application | P1b-read | 直接调用投递的用例都已包裹，作业外的续体只能来自冲突用例 `IllusionTowerPublicFlowTests.Sweep_captured…`（`Task.Run` 扫描）。只读 |
| `SelectDungeonHandler.HandleMessageAsync` | Application、Networking | P1b-read | Application 中直接调用 handler 的用例都已包裹，作业外的执行只来自 dispatcher 路径。Networking 只经 socket。**入口驱动**，只读 |
| `ClientMessageHandler<T>.HandleAsync`、`EplpCommandHandler.HandleMessageAsync`、`ResponsePeerHandler.HandleMessageAsync`、`SetUserAreaHandler.HandleMessageAsync` | Networking | P2、P1b-read | 栈超过 40 帧，dispatcher 与 TouchSocket 帧被截掉。Networking 用例不直接调用 handler。**入口驱动，归 (A)**。x3 排除冲突用例后 Networking 仍有这 4 条，证明它们不来自冲突用例 |
| `TownAreaProjectionDelivery.ProjectTransferAsync`／`TransferAsync`、`DungeonLifecycleDomainService.RecordDepartureAsync`、`DungeonRunDomainService.EndAsync`、`ItemUsageProjectionDelivery.SendAsync` | Networking | P2、P1b-read | 同上，是 handler、断连拆除或每日刷新 worker 内部 await 恢复之后的执行。Networking 中这些类型只由入口驱动。**入口驱动，归 (A)** |
| `TransactionalExecutionGate.AcquireAsync` | Networking | P1 | `DungeonLifecycleDomainService.RecordDepartureAsync` 取门之后开事务（门的等待者续体），由断连拆除驱动。**入口驱动，归 (A)** |

结论：(C) 中没有漏包，全部归入 (A) 或冲突用例。

## 6. R-2 自动核对：迁移后消失的 (A) 记录

被误包的入口不会产生新记录，只会让原有的 (A) 记录消失。`adiff.py r0 r7` 按测试比较 Application 迁移前后的 (A) 集合：

- 迁移前有 (A) 的测试 904 个，迁移后 739 个；**全部 (A) 记录都消失**的测试 172 个。
- `lostcaller.py` 逐条查了消失的那个入口帧在迁移前的直接调用方：
  - **166 个**：调用方是生产代码，即测试直接调用的 handler 或投递在**内部**使用了真实总线。最多的几个：`DisjointItemHandler` 29、`SellItemHandler` 21、`RepairItemHandler` 15、`BuyItemHandler` 13、`RecoverStaminaHandler` 11、`MailboxDelivery` 10、`DeleteItemHandler` 15、`IllusionTowerProjectionDelivery` 各方法共 12。
    - 这符合「handler 在作业里」的既有判例（TASK-06 的 `LeaveDungeonTests`；审查已接受）。
    - handler 是 T5 直接调用，不是入口；它内部的总线调用在 T-004c 翻转后本来就在作业里内联执行。
  - **6 个**：调用方是测试 lambda。逐个复查，它们在最终轮仍有同一入口的 (A) 记录，只是因为插入新 lambda，编译器生成的名字从 `<…>b__0` 变成了 `b__1`、`b__2`，归属键跟着变了。
    - 涉及 `DungeonPickupTransactionTests`、`EffectOperationReplayTests.Love_coupon…`、`GameLoginCommandTransactionTests`、`MailboxProtocolIntegrationTests`、`MonsterDeathTransactionTests`、`VendingMachineTests`；
    - 不是误包。
- 保留了部分 (A)、但少了一种入口的测试有 3 个，都已核过：
  - `CommonExperienceEffectTests` 冲突用例：分类器修正后，入口名从 ``CommandLoggingBehavior`1`` 变成了 `CommandBus`；
  - `MagicSealHandlerTests.Change_updates…`：直接调用投递的 `RefreshAsync` 被包，属上面的 handler/投递判例；
  - `WeaknessGoldRecoveryTests.Daily_projection…`：`Recover` 辅助包住了 handler，worker 的 `RefreshAsync` 仍在外面。
- 补充的静态核对（`regions.py`）：在全部包裹区域的文本里查入口标记，命中 67 处。逐条看过，都是领域方法同名（塔的 `StartAsync`、准入的 `ExecuteAsync`、投递的 `RefreshAsync`/`SweepAsync`）、变量名（`laterObserver`），或 TASK-06 已审过的假总线。**没有包住真实入口**。
- 曾发现并修正 1 处：fork-2 在 `MailboxProtocolIntegrationTests.System_and_prepared_delivery…` 中把 `DrainOnceAsync`（入口）和作业外开的事务一起包进了作业。r3 中该用例因此失败，已改为只包 `await using (scope.Begin()) await Mail.SendAsync(…)` 这一条语句。

## 7. 探针的局限

- 只看三族挂点和 P1b。一个入口如果什么挂点都没碰到，误包它不会留下任何痕迹。§6 的 R-2 核对因此只覆盖「入口碰到了挂点」的那部分。
- 40 帧上限与 await 恢复会截栈，所以 (C) 要靠排除复跑和代码阅读来分诊（§5）。
- 探针运行期间的耗时与偶发失败不作为任何结论的证据（TASK-07 §3.4）。各轮的测试结果与正式运行一致：都只有那 2 条 `DimensionDungeonTests` 失败。
- 记录器只在作业外记录；作业**内**的访问是否发生在「正确的执行器」上，不在本探针的范围内（见 TASK-06 §1 第 3 种来源的代价）。

## 8. 还原证明（Required check 6）

1. **改动前快照**：第 0 轮应用之前取（`snapshot.py snap-r0-before.txt`），内容有三部分：
   - `git ls-files -s Server/src` 的 1576 条（mode、blob、stage、路径）；
   - 工作区每个已跟踪文件按 git blob 格式算的 sha1，共 1576 条；
   - 未跟踪文件列表（0 条）。
   - `snapcheck.py`：索引 blob 与工作区 blob 逐条相等，1576 ＝ 1576。
2. **中途还原一次**（换成最终一版记录器之前）：快照 `snap-mid-after.txt`，与改动前快照字节相等。
3. **最终还原**（`patch.py revert`：从字节级备份恢复 25 个文件，删除 `GameplayProbe.cs`）之后的快照 `snap-final-after.txt`：

   ```
   snap-r0-before.txt   sha1 5dbaa2e5d76575d490ed2e300cbd873110fbb5f4  index 1576 / worktree 1576 / equal 1576 / untracked 0
   snap-mid-after.txt   sha1 5dbaa2e5d76575d490ed2e300cbd873110fbb5f4  index 1576 / worktree 1576 / equal 1576 / untracked 0
   snap-final-after.txt sha1 5dbaa2e5d76575d490ed2e300cbd873110fbb5f4  index 1576 / worktree 1576 / equal 1576 / untracked 0
   cmp snap-r0-before.txt snap-final-after.txt  →  SNAPSHOT-BYTE-EQUAL
   ```

4. `git diff --stat -- Server/src`：空输出。
5. `git status --short -- Server/src`：空输出，没有残留的未跟踪探针文件。
6. `grep -rn "TestGameplay\|GameplayProbe" Server/src`：零命中，退出码 1。

正式全量运行（`logs/task-07-build-test.txt`）在第 3 步之后进行，先 `--no-incremental` 重新构建。日志开头再次记录了第 4、5 两项，都为空。
