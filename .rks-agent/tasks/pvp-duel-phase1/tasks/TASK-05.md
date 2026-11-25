# TASK-05 — 对局阶段、判定、结算、离场与停服（领域层）

## Task Unit Contract

- Revision: 1
- Outcome and references: 在 TASK-04 的房间 owner 上交付开局之后的全部领域行为：阶段推进（Loading→PeerLinking→Fighting→AwaitRank→AwaitResult→Waiting，及开战前取消）、死亡与超时判定、终局冻结、结算领域服务与 `PvpMatchSettled` 事件、截止时间来源、离场（`Depart`）接入会话清理、停服 drain。Plan T-005；Spec RQ-030–035（领域侧）、RQ-040–042、RQ-050（结算侧）、RQ-051–054、RQ-071；AC-030–035（领域部分）、AC-041（领域侧）、AC-042、AC-050、AC-051（连胜由 Store 已测）、AC-054。
- Owned boundary: `Abstractions/Pvp/**`、`Application/Pvp/**`、`Application/Sessions/SelectCharacterLifecycleServices.cs`（清理链加离场步骤与 `AffectedSessions`）、`Application/DomainEvents` 中事件登记位置（按现有事件注册方式）、宿主或 Application 中停服 drain 注册；`Protocol/WorldMaintenanceCommandRegistration.cs` 或新的内部命令注册位置（内部 CMD 处理器若需放 Protocol 层则放在 Protocol 新目录 `Game/PvpBattle/` 但只含内部命令，不含线上消息）；测试 `Application.Tests/Pvp/**`、写入口表、状态普查、`TimerSourceReconciliationTests`/`TimerSourceJobFailureTests`。**不做**线上消息、codec、协议投影（TASK-06）。
- Required checks: 构建 `-m:1`；Application.Tests 过滤 Pvp、Sessions、Execution、Commands、DomainEvents、Timer；Host.Tests 架构/启动类；新增测试覆盖本单元 AC；一次只跑一个测试项目且关闭并行；与基线对比无新增失败。
- Dependencies: TASK-04
- Result review: required — 结算、断线判负、停服语义是本期最容易出错且后果持久（写入玩家战绩）的部分；TASK-06 的协议层完全建立在这些结果类型上。

## Detailed Plan

### 对局状态（扩展 `PvpMatch`，在房间 owner 内）
- 每名参战者：存活、击杀数、死亡数、逃离标记、本阶段已上报（加载完成、P2P 连通、排名确认、结果确认）、最近心跳时间。
- 终局结果（冻结后不可变）：`PvpTerminal`（Win(胜方：个人模式为座位、团队模式为队伍) / Draw / Cancelled），附冻结的结算名单（开局名单，逃离者标记已定）。
- 截止时间按 Spec 阶段表：Loading 60s、PeerLinking 30s、Fighting 兜底 = `BattleTimeLimit(mode)` + 30s、心跳 30s（只在 Fighting 计）、AwaitRank 10s、AwaitResult 10s。

### 写面新增（`IPvpRoomDomainService`，全部在调用方事务内）
- `ReportLoadedAsync`、`ReportPeerLinkedAsync`（全员到齐推进阶段）、`HeartbeatAsync`。
- `ReportDeathAsync(session, killerCharacterId?)`：发送者须为存活参战者本人；击杀者有效（本局、存活、敌方）才计击杀；按能力表 `Terminal` 判终局；全灭判平；终局则冻结并进入 AwaitRank。
- `ReportTimeoutAsync(session, values)`：只在 Fighting、发送者为未逃离参战者、已过时间 ≥ 时限 − 5s 时接受第一份；一期一律判平（数值只记日志，不参与判定）。
- `AcknowledgeRankAsync`、`AcknowledgeResultAsync`：只在对应阶段、只认冻结名单内会话；迟到（房间不在该阶段）忽略。
- `DepartAsync(session, reason)`：按阶段——Fighting 记阵亡/逃离/无击杀者并判终局；Loading/PeerLinking 整局取消回 Waiting；AwaitRank/AwaitResult 不改结果，只移出确认等待；Waiting 按退房（含继任/销毁）。结果中列出需要投影的成员（含结果界面期间延后释放的座位移除，按接收者记录）。
- `CancelForShutdownAsync()`：未终局对局取消；已终局未结算的交给结算服务。
- `AdvanceDueAsync(now)`：供截止来源调用，按阶段到点推进（Loading/PeerLinking 到点取消；Fighting 兜底到点判平；心跳超时者按 Depart；AwaitRank 到点进入结算；AwaitResult 到点回 Waiting）。返回需要结算的对局与需要投影的变化。
- 回 Waiting 时清准备与对局状态；下一局开局时释放该房延后的投影条目（条目归属 TASK-06 的队列，本单元只在结果里给出"释放"信号）。

### 结算领域服务 `IPvpSettlementDomainService`（Abstractions 接口，Application 实现）
- `SettleAsync(PvpRoomId, PvpMatchId)`：读房间冻结结果 → 对每个结算名单成员读 `IPvpRecordDomainStore.ReadAsync` 与 `DailyCountsAsync(char, GameDayOf(开局时间))` → `PvpSettlementRuleService.Decide`（经验为空时取 `LowerBound`，N3）→ 权益 `EntitlementsOf(snapshot.Channel)`；不记录频道传与当前战绩一致的决定（N4）→ `Store.SettleAsync` → `Created` 才 `IEventService.EnqueueAsync(new PvpMatchSettled(...))`（同事务）→ 房间进入 AwaitResult（暂存）。返回每人的结算视图（供 TASK-06 编 `0x002F`/`0x0030`）。
- 事件 `PvpMatchSettled`（Abstractions，payload 只含 ID 与结果数据）在组合根按现有事件注册方式登记稳定名称/版本；一期没有订阅者也要能注册（若注册机制要求至少一个订阅者，写明处理方式）。

### 截止时间来源与内部命令
- `PvpDeadlineSource`（TASK-04 骨架的来源）：扫描内部 CMD 取得到期房间，逐项提交推进内部 CMD；推进命令在一个写工作单元内调用 `AdvanceDueAsync`，需要结算的再调用结算服务（同一或下一工作单元，保证幂等）。照 `Protocol/Game/Altar/AltarDeadlines.cs` 与 `WorldMaintenanceCommandRegistration`。把调度器从 `TimerSourceJobFailureTests` 豁免移到对账表。
- 内部命令处理器需要的投影（例如到点判平后给房间成员发 `0x002E`/`0x0031`）由 TASK-06 补；本单元的内部 CMD 只做领域推进，但返回的结果类型要带足投影所需信息，并在处理器里留一个明确的接缝（例如调用一个 `IPvpBattleProjection` 接口的空实现由 TASK-06 替换，或直接把这些内部 CMD 处理器放在 Protocol 层以便 TASK-06 扩展）——选一种并说明。

### 离场接入
- `SelectedSessionCleanupService`：在现有 `pvp.ClearReadyExact` 附近加一步 `rooms.DepartAsync(session, Disconnected)`（用 `SettleStepAsync` 加入已有事务或自开事务，照塔离场写法），结果交给 `IEnumerable<IPvpRoomDepartureSink>`（Abstractions 接口；本单元无实现或空实现，TASK-06 由离场投影队列实现）；若终局需要结算，经结算服务在同一事务完成。
- `AffectedSessions`：加入该会话所在房间的其他成员会话。
- 心跳超时与回选角走同一 `DepartAsync`。

### 停服
- 在 Application 注册 `PvpShutdownDrain : IGameplayProducerDrain`（DI 顺序在传输 drain 之前，核对 `GameplayExecutorLifetime` 的枚举顺序），`DrainAsync` 提交内部 CMD：先对已终局未结算的对局结算，再 `CancelForShutdownAsync`。之后传输拆连接触发的离场看到取消状态不判负。登记状态普查（若无状态则 StatelessEntries）。

### 测试（领域 + 内部命令，不含线上字节）
阶段矩阵（不属于该阶段的上报被忽略）；Loading/PeerLinking 截止取消且无战绩；死亡判定（非本人、已死、无效击杀者、个人/团队终局、全灭平）；超时准入（早到、非参战者、逃离者、第二份）与判平；终局冻结后胜者在 AwaitRank / 结算后 / AwaitResult 断线结果不变；Fighting 断线判负逃离；结算幂等（二次 AlreadySettled、事件一次）；写库失败时房间阶段不前进、无事件、不唤醒（带反例）；频道 8/13 不改战绩；经验为空取下限；跨游戏日开局归属；停服取消无负场、已终局先结算；清理链在外层写事务回滚时 sink 不收到条目（或条目随回滚丢弃）。

## Result

- Outcome: done（第二轮：按独立审查 changes_required 返工）
- Changed:
  - 契约 `Abstractions/Pvp/PvpRooms.cs`：`PvpFighter` 加战斗进度（`Alive/Kills/Deaths/Fled/Departed/Reports/LastHeartbeat`，init 属性，身份字段仍开局冻结）；`PvpMatch` 加 `FightingSince/Terminal/Results` 与 `FighterOf(session)`；`PvpRoomRefusal` 加 `WrongPhase/NotFighter/NotAlive/TooEarly/AlreadyReported/ShuttingDown`；`PvpRoomChangeKind` 加 `Loaded/PeerLinked/Heartbeat/Died/TimedOut/RankAcknowledged/Settled/ResultAcknowledged/Departed/DeadlinePassed/CancelledForShutdown/SettlementAbandoned`；`PvpSeatReleaseReason.Departed`；`PvpRoomOutcome.Changed` 加 `Death/Concluded/SettlementDue`；`IPvpRoomDomainService` 加 `ReportLoaded/ReportPeerLinked/Heartbeat/ReportDeath/ReportTimeout/AcknowledgeRank/AcknowledgeResult/Depart/AdvanceDue/CancelForShutdown/AbandonSettlement`（写）与 `Deadlines/PendingConclusions`（读）。
  - 新 `Abstractions/Pvp/PvpBattles.cs`：`PvpFighterReports`、`PvpDepartureReason{Disconnected,HeartbeatLost}`、`PvpMatchTerminal`、`PvpDeath`、`PvpMatchConclusion`、`PvpDeadlineScan(DueRooms, DueSettlements, Abandoned, Next)`、`IPvpRoomDepartureSink.DepartedAsync(departure, PvpSettlementOutcome? settled, ct)`、`IPvpSettlementDomainService` + `PvpSettlementOutcome{NotPending,Settled}`、事件 `PvpMatchSettled`/`PvpMatchSettledFighter`。`PvpMaps.cs` 的 `IPvpRoomRuleService` 加七个时长（含 `SettlementGiveUp`）。
  - `Application/Pvp`：`PvpRoomDomainService` 加对局状态机（共用 `Stage` 收口：战斗判终、终局冻结生成 conclusion、排名无人等待即 `SettlementDue`、结果界面无人等待即回 Waiting、离座继任/销毁）；已终局待结算表 `conclusions` 与停服标志 `closing` 都经事务参与者 `Changes` 暂存、提交生效；唤醒改为"截止时间提前或新出现才唤醒"；`Deadlines` 退避重试与放弃；`AbandonSettlementAsync`；停服后开局拒绝 `ShuttingDown`。`PvpRoomRules` 加 30s/30s/30s/10s/10s/5s 与放弃时限 5min。新 `PvpSettlementDomainService`。`PvpDeadlineScheduler` 加 `PvpDeadlineSource.ShutdownAsync`、`ShutdownAsync` 与停服挂点 `PvpShutdownService : IHostedLifecycleService`。`PvpRegistration` 登记结算服务与事件 `pvp.match-settled` v1。
  - `Application/Sessions/SelectCharacterLifecycleServices.cs`：`AffectedSessions` 加同房成员；`pvp.ClearReadyExact` 之后加 `DepartRoomAsync`；新增构造参数 `pvpRooms`、`pvpSettlement`、`pvpDepartures`。
  - Protocol：新 `Game/PvpBattle/PvpBattleDeadlines.cs`（6 个内部命令：扫描、推进房间、结算、列出待结算、停服取消、放弃结算；`PvpBattleDeadlineCommandHandler`；`PvpBattleProjection` 空投影接缝；`PvpBattleDeadlineSource`）；`WorldMaintenanceCommandRegistration` 登记内部命令与来源。
  - 测试：新 `Application.Tests/Pvp/PvpBattleDomainTests.cs`（39 例）、`Execution/PvpShutdownServiceTests.cs`（2 例）、`Host.Tests/PvpShutdownHostTests.cs`（1 例）；`PvpRoomFixture` 加内存战绩 Store（提交才落，可注入写失败）、事务化事件记录、`FailingWrite`、可选真实 `DomainTransactionScope`、`StartAsync/FightAsync/Run/SettleAsync`；写入口表 +15 行；`TimerSourceReconciliationTests` 加 `timer:PvpDeadlineScheduler/rooms`、`TimerSourceJobFailureTests` 删调度器豁免；5 处清理服务构造补参数。
- Why:
  - **停服挂点**：所有 `IGameplayProducerDrain` 都在 `GameplayExecutorLifetime.StoppedAsync` 里跑，而泛型主机先跑完所有托管服务的 `StopAsync`，TouchSocket 的 TCP 服务会在自己的 `StopAsync` 里关会话、派发断线，所以第一轮的 drain 晚于断线清理。现改为 `PvpShutdownService.StoppingAsync`：主机在任何 `StopAsync` 之前对所有生命周期服务调用 `StoppingAsync`，此时执行器仍在运行（它只在 `StoppedAsync` 里停）。轮内顺序改为**先取消（同时置停服标志、此后拒绝开局）、再结算全部待结算**。这样两步之间不会冒出新的终局：Fighting 已全部取消，也不再有开局。因此不必另立"停服期间离场按取消"的规则。
  - 已终局待结算记录 `PvpMatchConclusion` 独立于房间：房间可能在结算前因最后一人离开而销毁，结算只读冻结的 conclusion。AwaitRank 的截止由 conclusion 的 `SettleBy` 驱动，房间扫描不推进 AwaitRank。
  - 结算服务依赖具体 `PvpRoomDomainService` 的 internal `FindConclusion/CompleteSettlement`，不进公共接口，避免协议层绕过结算直接推进房间。
  - **清理里离场与结算拆开**：
    - 自开事务（断线）：离场一步先提交，若有 `SettlementDue` 再单开一步结算。结算失败只记 Warning（排除 `TransactionOutcomeUncertainException`），交给调度器：离场时已把 `SettleBy` 置为现在，并唤醒了调度器。之后 sink 收到 `(departure, settled)`，留下的成员能拿到 `0x002F/0x0030` 所需结果。
    - 加入外层事务（选角替换）：只做离场，sink 经 `RegisterAfterCommit` 收到 `(departure, null)`，结算由提交后立即被唤醒的调度器完成。外层回滚则 sink 什么也收不到。
  - **结算失败的重试与放弃**：
    - 退避：有过期项时，下一轮间隔为"最早过期项已过期时长的 1/4"，夹在 1s–30s 之间。
    - 放弃：conclusion 过期超过 `SettlementGiveUp`（5 分钟）后，扫描把它列入 `Abandoned`，来源提交 `AbandonPvpSettlementCommand`：删除 conclusion，房间若仍停在该局的 AwaitRank 就回 Waiting，记 Error，不改任何战绩。
  - 离场语义：
    - 会话结束（断线/回选角/被替换）一律 `Disconnected`：让出座位，不回城。
    - 心跳超时 `HeartbeatLost`：只记战斗逃离，保留座位。
    - Fighting 中离场按 Spec RQ-040 字面一律记"阵亡+逃离"，已阵亡者离场同样记逃离。
  - 投影接缝选择把内部 CMD 处理器放在 Protocol：提交后调用静态空实现 `PvpBattleProjection`，TASK-06 在这里注入 `IGameCommandOutbox`/`ICommandProjections` 编帧。不用通知类型做接缝，因为没有处理器的通知类型会让服务器启动失败。
  - 偏离 Detailed Plan 之处：
    - `AdvanceDueAsync` 按房间逐个推进，签名为 `(PvpRoomId, now)`，每房一个内部 CMD 项，失败互相隔离。
    - `SettleAsync` 以 `PvpMatchId` 为键，因为房间可能已销毁。
  - 事件 `PvpMatchSettled` 一期无订阅者；`EnqueueAsync` 不要求订阅者，只登记稳定名称和版本即可。
  - AC-033 三种离场时机用例：
    - when=0：在 AwaitRank 离场，排名超时后结算。
    - when=1：结算写库提交后离场，此刻房间已在 AwaitResult，"结算写库后"与"AwaitResult 刚开始"是同一时刻。
    - when=2：AwaitResult 中、另一方已确认后离场，房间随即回 Waiting。
    - 三者写入的战绩相同。
- Checks（最终源码，`PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`，`-m:1`，单项目、`MaxParallelThreads=1`）：
  - `dotnet build Server/86JP.Server.slnx -m:1`：0 警告 0 错误。
  - Application.Tests 过滤 `Execution|Commands|DomainEvents|Timer|Sessions|Pvp|Architecture|Registr|Census|Cycle|DependencyGraph|RequiredDependency|Select|OnlineUser`：1632 通过、13 失败。13 项全部在 `baseline-failed.txt` 中（`GuardianAltarLifecycleTests` 9 例、`DarkElfRuinsTests.Licensed_star…` 4 例）。
  - Host.Tests `Architecture|ContentWarmup|ChannelHostStartup|Startup|ComponentLifecycle|HostShutdownOrder|Pvp`：64 通过、1 跳过（既有 Sigterm 用例，Windows 不可用；含新 `PvpShutdownHostTests`）。
  - `PvpShutdownHostTests` 反例：把挂点临时改回 `StoppedAsync`（等同第一轮 drain 时机）后，该用例失败，`Expected: Waiting, Actual: Fighting`。已改回，最终源码上通过。
  - Protocol.Tests `Registr|Architecture|Internal|SelectCharacter|Cleanup|Pvp|SupplementalSynchronization`：139 通过。Networking.Tests `SelectionLifecycleWholeCommandTests|TransportDrain`：4 通过。
  - 换行：`git diff --numstat` 与 `--ignore-cr-at-eol` 结果一致。
- Remaining:
  - 线上消息、codec、`PvpBattleProjection` 的帧内容、离场投影队列（`IPvpRoomDepartureSink` 实现）、0x0039 延后释放队列都属 TASK-06；本单元没有 sink 实现。
  - 心跳超时离场在 `AdvanceDue` 结果里表现为 After 中该战斗者的 `Fled/Departed`，`Death` 为空；是否为它发 `0x002E` 由 TASK-06 决定。
  - 主机层停服用例里，断线由一个替身托管服务在 `StopAsync` 中经生产 `INetworkConnectionLifecycleDispatcher` 派发，与 TouchSocket 关会话的派发点相同；没有用真实 TCP 客户端走完登录。真实连接的完整停服链路留给 T-007 集成测试。
  - 进程崩溃时未提交与未结算的对局全部丢失（Spec RQ-042 允许）。

## Result Review

- 第一轮：changes_required（停服挂点晚于 TCP 断线派发会判负；清理路径丢弃结算结果；离场与结算同步失败留下幽灵座位；结算失败无限重试；AC-033 用例缺一种时机；停服窗口可开新局）。
- 第二轮：approved（独立审查）。审查者重建后跑 Host 停服/架构 20 项、Application Pvp 等 258 项全过；亲自把挂点临时改回 `StoppedAsync` 验证反例失败后按 SHA-256 还原。
- 移交 TASK-07：清理路径结算抛不确定异常时 sink 未收到已提交离场、多个 sink 未逐个隔离（放进 finally 并逐个包裹）；放弃结算（`SettlementAbandoned`）须补发回等待与大厅帧；结算投影两处都要实现（sink 的 `settled`，以及调度器 `PvpBattleProjection.SettledAsync`）；放弃日志措辞改保守。
- 残余风险：停服取消步失败则标志不置位（只改内存，难失败）；停服轮占用主机停服超时。
