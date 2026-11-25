# TASK-07 — 协议切片（二）：对局上报、结算帧、离场投影、选角战绩

## Task Unit Contract

- Revision: 1
- Outcome and references: 线上可用的对局协议与投影：CMD `0x012A`、`0x012B`、`0x006E`、`0x0037`、`0x0038`、`0x003A`、`0x0039`；NOTI `0x002E`、`0x0031`、`0x002F`、`0x0030`；`PvpBattleProjection` 三个接缝（截止到点、结算、停服取消）与放弃结算的回等待；离场 sink 实现与"结果界面期间按接收者延后"的座位移除投影队列；选角时补发 `0x0030`；回等待与销毁的大厅增量。Plan T-006（对局部分，A2）；Spec RQ-030–035（投影侧）、RQ-041、RQ-055、RQ-072、RQ-075（`0x0037`、`0x0038`）；AC-030–035、AC-041、AC-055、AC-075。
- Owned boundary: `Data/Networking/NetworkMessages.cs` 目录成员；新切片 `Protocol/Game/PvpBattle/`（已有内部命令文件，在此扩展）及必要时 `Protocol/Game/PvpRecord/`；`ProtocolModule` 接线；`Protocol/Game/SelectCharacter/` 中 PvP 战绩补发阶段；`Application/Sessions/SelectCharacterLifecycleServices.cs` 中 `DepartRoomAsync` 的 sink 调用（finally 交付、逐个 sink 隔离——TASK-05 审查移交）；放弃结算日志措辞；测试 `Protocol.Tests/Game/Pvp*`、选角相关测试；协议页人工区与 catalog（本单元协议）。不改领域语义。
- Required checks: 构建 `-m:1`；Protocol.Tests（新切片、选角、注册/Opcode），Application.Tests（Sessions、Commands 架构、Pvp），Networking.Tests `SelectionLifecycleWholeCommandTests`；每次一个项目、单线程；与基线对比无新增失败。
- Dependencies: TASK-06
- Result review: required — 结算帧长度（`0x002F` 26+19n、`0x0030` 51B）与帧序只有字节基线能守住；延后投影的释放规则复杂且实机前无客户端校验。

## Detailed Plan

### 入站
- `0x012A`、`0x0039`、`0x006E`：空体。`0x012B`：`u8 n + n×(u8 seat, u8 status)`。`0x0037`：只接受 5 字节 `u16 victimUid, u16 killerUid, u8 flag`（发送者须为死者本人，victim 与发送者不符则拒收记日志；killer 0/未知 → 无击杀者），其他长度拒收并记日志。`0x0038`：只接受 32 字节，原样交给领域（一期只记日志）。`0x003A`：长度不校验内容（≥0 均接受，作为确认）。
- 各 CMD 调用对应领域方法；结果带 `SettlementDue` 时在同一写单元内调用 `IPvpSettlementDomainService.SettleAsync`；`Refused` 只记日志不发包。

### 出站与帧序（design-r1 §4 b、research.md §2）
- 死亡：房内成员 `0x002E`（`u8 deadSeat, u8 killerSeat/0xFF`）；终局随后 `0x0031`（空体）。
- 结算：逐人 `0x002F`（`u8 winnerSeat, i32 0, u8 本人段位, i32 本人经验变化, u8 n` + n×(`u16 uid, i32 kills, u8 grade, i32 experience, i32 0, i32 0`) + `u16 0xFFFF, u16 0xFFFF, i32 0, u8 isDraw, u8 0xFF, i32 0, u8 0`；n 用开局冻结名单；团队模式 winnerSeat 的取值按上游实现核对）→ 紧接 `0x0030`（51B：胜、负、累计经验（为空取段下限）、本段下限、本段上限、u8 段位、u8 评级、i32 -1、i32 rankPoint 0、i32 peak 0、i32 定级场 0、u8 0、i32 0、i32 0、i32 0）。房间上下文是否要追加 3×26B 以上游实现为准，协议页记录取舍。
- 回等待（全员 `0x0039` 或截止、放弃结算）：房内成员与大厅 `0x002A`（state 1）及 TASK-06 的大厅增量函数。
- 取消（开战前离场、停服）：房内与大厅回等待帧；停服取消可不投影（连接将断），但不得抛错。
- 投影来源两处都要实现：内部命令处理器的 `PvpBattleProjection.DeadlinePassedAsync/SettledAsync/CancelledForShutdownAsync`（改为可注入 outbox/projections 的实现），以及离场 sink 收到的 `settled`。

### 离场 sink 与延后队列
- 实现 `IPvpRoomDepartureSink`：Fighting 中离场 → 房内 `0x002E(seat, 0xFF)`，终局再 `0x0031`；`settled` 非空 → 编结算帧；Waiting 离场 → 座位/房间/大厅帧。
- 结果界面期间（AwaitRank/AwaitResult）离开者的座位移除投影按接收者入队（照 `TowerDepartureProjectionQueue` 的入队 + 扫描认领模式，或在 sink 内直接按接收者保存），释放条件：该接收者 `0x0039` 被领域接受、下一局 `MatchStarted`、该接收者离房、其会话被替换；确认超时不释放；房间销毁丢弃。迟到 `0x0039` 不触发释放。
- `DepartRoomAsync`：sink 调用放 finally（结算不确定异常时 `settled=null` 也交付已提交的离场），逐个 sink 包裹隔离，最后重抛原异常。

### 选角
- 选角 PvP 阶段与非 PvP 频道都补发本人 `0x0030`（经 `IPvpRecordDomainStore.ReadAsync` 只读 + 规则侧上下界——注意 CMD 不能注入规则服务：上下界需由领域服务提供，若现有领域接口没有，增加一个只读领域查询（例如 `IPvpRecordDomainService.ViewAsync` 返回含上下界的视图），并说明）；位置按上游：第二次 USERINFO0 与 HOTKEY_OPTION 之后。若录制转写类测试（已在基线失败清单）受影响，在 Result 说明。

### 测试
每个入站长度合法/非法；`0x002F` 对 n=2/4 的字节基线（26+19n）、`0x0030` 51B 基线（含经验为空取下限）；死亡→终局→结算→回等待的帧序；截止到点判平/取消的投影；放弃结算回等待投影；离场 sink：Fighting 断线、外层事务回滚无投影、settled 非空编结算帧；延后队列释放的四种条件与确认超时不释放、迟到 `0x0039`；选角帧序含 `0x0030`。

### TASK-06 交付的可复用件与移交
- 复用 `Protocol/Game/PvpRoom/PvpRoomProjection.cs` 的 `PvpRoomProjector.ProjectAsync(changed, ct)`（对 DeadlinePassed、CancelledForShutdown、SettlementAbandoned、ResultAcknowledged 回等待、最后一人离场销毁走通用分支，发 `0x002A`/座位增量）、`PvpRoomLobbyFrames.For`、`Listener`、`BroadcastAsync`、`PvpRoomBroadcast`。
- 通用分支对 `Released` 立即发座位移除；结果界面期间（AwaitRank/AwaitResult）离开者的移除须按接收者延后（RQ-035），在调用通用分支前过滤并入延后队列；被送回城的离开者保持"先回城帧、后房间帧"的顺序。Fighting 中离场是否发 `0x002B` 由本单元按上游决定。

## Result

- Outcome: done
- Changed（31 个已有文件 +592/−56，新增 14 个文件约 1800 行；未提交）:
  - 消息目录 `Data/Networking/NetworkMessages.cs`：C→S `GameCompleteLoadPvp(0x012A)`、`GameConnectP2pPvp(0x012B)`、`GamePvpHeartbeat(0x006E)`、`GameDiePvpCharacter(0x0037)`、`GamePvpTimeOut(0x0038)`、`GamePvpRankResponse(0x003A)`、`GameEndPvpResult(0x0039)`；S→C NOTI `GameDiePvpCharacter(0x002E)`、`GameRequestPvpRank(0x0031)`、`GameEndPvp(0x002F)`、`GamePvpRecord(0x0030)`。
  - Abstractions（最小补充，不改语义）：`PvpRecords.cs` 加 `PvpRecordView` 与只读领域查询 `IPvpRecordDomainService.ViewAsync`；新 `PvpBattleProjections.cs`：`PvpDepartureEntryId`、`PvpCommittedDeparture`、`IPvpDepartureProjectionDomainService`（离场投影队列）、`IPvpResultScreenDeferralDomainService`（延后座位欠帧）。
  - Application：新 `Pvp/PvpRecordDomainService.cs`（读战绩 + 规则侧上下界，经验为空取下限）并在 `PvpRegistration` 登记；`Sessions/SelectCharacterLifecycleServices.cs` 的 `DepartRoomAsync` 改为"已提交的离场总交给每个 sink、逐个隔离、最后重抛"；`PvpRoomDomainService` 只改放弃结算的日志措辞（不再断言"无战绩变化"）。
  - Protocol 新切片 `Game/PvpBattle/`：`Messages.cs`、`PvpBattlePayloadCodec.cs`（7 个 decoder、4 个 encoder）、`PvpBattleCommands.cs`（`GamePvpBattleCommandHandler` 一类七个 CMD、薄 handler）、`PvpBattleProjection.cs`（`PvpBattleProjector`、`PvpDepartureProjectionQueue`、`PvpResultScreenDeferrals`）、`PvpBattleRegistration.cs`；`PvpBattleDeadlines.cs` 删去静态空接缝，`PvpBattleDeadlineCommandHandler` 注入投影依赖并新增 `CapturePvpDeparturesCommand`/`ProjectPvpDepartureCommand`，来源每轮（含停服轮）先认领离场；`ProtocolModule` 一行接线。
  - `Game/PvpRoom/`：`PvpRoomProjector` 加可选 `deferrals`（开局前补发、对局中座位移除按接收者延后、离房补发、会话结束/房间销毁丢弃、公开 `ReleaseAsync`/`ReleaseFrames`）；`GamePvpRoomCommandHandler` 注入 `IPvpResultScreenDeferralDomainService`（组队接受邀请的构造不变）。
  - `Game/SelectCharacter/GameSelectCharacterCommand.cs`：注入 `IPvpRecordDomainService`，只读单元 `ReadSelectedCharacterPvpRecord` 读本人战绩，`WithPvpRecord` 把 `0x0030` 插在 HOTKEY_OPTION 之后。
  - 测试：新 `Protocol.Tests/Game/PvpBattle/`（Codec 21、Command 28、Projection 16 例，`PvpBattleWorld` 替身）与 `SelectCharacter/SelectCharacterPvpRecordTests`；`PvpRoomStandIns` 对局写面改为可脚本；`PvpRoomCommandTests` 补构造参数；Application `PvpBattleDomainTests` +5 例（不确定结算仍交付、sink 隔离、真实队列在回滚/提交下的条目、战绩视图下限）；写入口表登记两个新状态持有者（7 行）；`PvpShutdownServiceTests` 改为断言"先认领离场 → 取消 → 列待结算"；`SelectCharacterLifecycleTests` 允许新只读单元并在 5 处期望帧序加 `0x0030`；`SelectionStageBehaviorDriver`、`Support/ProtocolAlgorithmFixture`（默认"未记过的战绩"）补参数。
  - 协议页人工区：`0x012A`、`0x012B`、`0x006E`、`0x0037`、`0x0038`、`0x003A`、`0x0039`、`NOTI-0x002E`、`NOTI-0x002F`、`NOTI-0x0030`、`NOTI-0x0031`，以及 `NOTI-0x002B`（对局期间座位移除延后）；`catalog.md` 新增"对局侧"一节并改房间侧一节的指向句。
- 线上布局：
  - 入站：`0x012A`/`0x006E`/`0x0039` 只收空体（机器区静态 BodyLength = 0）；`0x012B` 恰 `1 + 2n`、n ≤ 8、seat < 8，状态只记日志；`0x0037` 恰 5 字节 `u16 victim, u16 killer, u8 flag`，victim 须为发送者 user id，killer 按冻结名单的 user id 换角色（0/未知 → 无击杀者），flag 只记日志；`0x0038` 恰 32 字节原样交领域；`0x003A` 长度不校验。畸形与被领域忽略的上报只记日志，无回包。
  - 出站：`0x002E` `u8 victim, u8 killer/0xFF`；`0x0031` 空体；`0x002F` `26 + 19n`（见下）；`0x0030` 51 字节，不追加 3×26 房间上下文（上游普通房结算与选角都只发 51 字节基本体，尾部只属总力战房）。
- 结算帧字节基线（`PvpBattleCodecTests`）：
  - `0x002F` n=2：`00 00000000 01 64000000 02 | 0201 01000000 01 4C040000 00000000 00000000 | 0403 00000000 00 B6030000 00000000 00000000 | FFFF FFFF 00000000 00 FF 00000000 00`（64 字节）；n=4 平局 102 字节，第四人条目与尾部（draw=1）逐字节核对。胜方座位：个人为胜者座位，团队为胜方队伍里仍存活的最小座位（上游 `FindTerminalWinnerSeat`），平局 0xFF。
  - `0x0030`：`07000000 04000000 DC050000 E8030000 D0070000 03 02 FFFFFFFF 00000000 00000000 00000000 00 00000000 00000000 00000000`；满级哨兵超出 i32 截为 `0x7FFFFFFF`；经验为空取下限由 `PvpRecordDomainService` 给出并在 Application 测试中核对。
- 帧序：死亡 → 房内成员 `0x002E`，终局随后 `0x0031`；结算写入后逐个在座战斗者 `0x002F` → `0x0030`，再投影房间到结果界面；`0x0039` 被接受时先给本人补欠帧（8 座位 `0x002B` → `0x002A`），全员确认回等待时成员与大厅 `0x002A(1)`；截止判平 `0x0031`、心跳超时 `0x002E(seat,0xFF)`、Loading/PeerLinking 截止取消与放弃结算均为成员与大厅 `0x002A(1)`；停服取消逐房投影，单房投影失败只记 Warning 不中断停服轮。`Death_ranking_results_and_the_room_waiting_again_arrive_in_that_order` 固定整条序列。
- 延后队列的释放实现：
  - 状态：`PvpResultScreenDeferrals` 只记"会话 → 欠帧的房间"（锁内字典，写成员全部 `GameplayWriteAssertion`），不存帧；补发时按当时房间现编 `ReleaseFrames`（8 座位一帧 `0x002B` + `0x002A`）。
  - 入账：`PvpRoomProjector.ChangedAsync` 中，变化有 `Released` 且变化后房间仍在对局阶段（非 Waiting）时，变化后在座、且尚未确认结果界面（`Reports` 不含 `ResultAcknowledged`）的成员记欠帧并从大厅受众中排除，已确认者立即收到座位帧；大厅其余就绪者立即收到座位/房主帧。Fighting 中离场同样延后（上游 `deferCombatDeparture` 以"房间处于开战状态"判定，这是本单元对"Fighting 中离场是否发 0x002B"的决定：成员不发、大厅发）。
  - 释放：该会话 `0x0039` 被领域接受（`GamePvpBattleCommandHandler` 在投影变化前调用 `ReleaseAsync`）；下一局 `MatchStarted`（`StartedAsync` 先对每个成员按开局前房间补发，再发 `0x002C`/`0x002D`/`0x002A(2)`）；该会话退房或被踢（回城帧与本次房间帧之后补发当前房间）。
  - 丢弃：该会话以 `Departed` 离座（断线、回选角、被替换）；房间销毁（`DropRoom`）；欠的不是本房。确认超时回等待不释放；迟到 `0x0039` 被领域拒绝，不释放。
- 两处投影：
  - 离场 sink：`PvpDepartureProjectionQueue : IPvpRoomDepartureSink` 入队 `(departure, settled)` 并唤醒截止调度；调度轮（`PvpBattleDeadlineSource.RunAsync` 与 `ShutdownAsync`）先 `CapturePvpDeparturesCommand` 再逐条 `ProjectPvpDepartureCommand`（`TryClaim` 只认队头），投影 = 对局帧 + 房间帧 + `settled` 非空时的结算帧。外层事务回滚时队列为空、不唤醒（Application 测试用真实队列核对）。
  - `PvpBattleDeadlineCommandHandler`：`AdvancePvpRoom`（变化 + 同单元结算）、`SettlePvpMatch`、`AbandonPvpSettlement`（回等待帧）、`CancelPvpBattlesForShutdown` 都在写单元返回后经同一 `PvpBattleProjector` 投影；结算帧所需战绩在提交后另开只读单元经 `IPvpRecordDomainService` 读取。
- 选角 `0x0030`：所有频道（决斗与否）在状态批里 HOTKEY_OPTION（`0x01C7`）之后紧接一帧本人 `0x0030`，即两次 USERINFO0 与 HOTKEY 之后（上游 `NewCharacterInitSequence`）。工厂批中没有 HOTKEY 时（只有测试替身工厂如此）追加在批尾。
- Why:
  - `0x0030` 的上下界需要规则服务，而 CMD 不能注入规则服务（`CommandHandlerArchitectureTests`），故新增只读领域查询 `IPvpRecordDomainService`（Abstractions 接口、Application 实现，无状态）。选角与结算两处共用。
  - 离场投影与延后欠帧都是进程内状态，接口按 `DomainService` 后缀放 Abstractions（与塔离场队列同一写法）以便 CMD 注入；实现放 Protocol，登记进写入口表，状态普查通过。
  - 离场投影挂在既有 PvP 截止调度轮的开头，而不是另起扫描服务：保证"离场帧先于同轮截止/结算帧"，停服轮也先清队列；sink 入队时唤醒调度。
  - `DepartRoomAsync`：离场一步提交失败时不交付（是否提交未知）；离场已提交后，结算普通失败记 Warning、`settled=null`，结算不确定异常同样 `settled=null` 交付后重抛；sink 逐个 try/catch，失败记 Error，无原异常时重抛 sink 失败（单个原样、多个 `AggregateException`），由清理链汇总为 `SelectedSessionCleanupFailure`。
  - 放弃结算日志措辞：之前断言"no record changed"，但先前一次不确定提交可能已写入，改为只陈述本步所做并提示查 `pvp_matches`。
  - 没有改变领域语义；领域结果已足够投影，未补字段。
- Checks（最终源码，`PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`，`-m:1`，单项目、`MaxParallelThreads=1`）：
  - `dotnet build Server/86JP.Server.slnx -m:1`：0 警告 0 错误。
  - Protocol.Tests 全量：2487 通过 0 失败（含新 PvpBattle 64 例；审查后补 1 例，见下、选角 0x0030 1 例）。
  - Application.Tests 过滤 `Execution|Commands|Notifications|Registr|Architecture|Pvp|Sessions|Census|DependencyGraph|Select|Timer|DomainEvents`：1522 通过、5 失败，5 项均在 `baseline-failed.txt`（`DarkElfRuinsTests.Licensed_star…` 4 例、`GuardianAltarLifecycleTests.Managed_disable…`）。
  - Networking.Tests `SelectionLifecycleWholeCommandTests|TransportDrain|SelectCharacter`：4 通过、5 失败，5 项均在 `baseline-failed.txt`（`SelectCharacterSuccessCharacterizationTests` 1 例、`SelectCharacterCompensationCharacterizationTests` 4 例；`SelectionLifecycleWholeCommandTests` 与 `TransportDrain` 全过）。
  - Host.Tests `Opcode|Architecture|Pvp|Startup|ComponentLifecycle|HostShutdownOrder`：57 通过、1 跳过（既有 Sigterm 用例，Windows 不可用）。
  - 反例：把 `PvpRoomProjector` 大厅受众的欠帧排除去掉后，`PvpBattleProjectionTests` 两例失败；把 `DepartRoomAsync` 对不确定异常的捕获去掉后，`A_departure_whose_settlement_outcome_is_unknown…` 失败。均已还原。
  - 换行：`git diff --numstat` 与 `--ignore-cr-at-eol` 一致；新文件 LF。
  - 审查后修正（已确认结果界面的成员不再记欠帧；`ReadRelationsAsync` 注释移回原位）后重跑：构建 0 警告 0 错误；Protocol.Tests 过滤 `PvpBattle|PvpRoom|SelectCharacter` 228 通过 0 失败（含新用例 `A_member_who_closed_its_result_screen_is_shown_a_later_departure_at_once`）；Application.Tests 过滤 `Pvp|Execution` 552 通过 0 失败。反例：去掉该过滤后新用例失败，已还原。
- 录制转写：`SelectCharacterSuccessCharacterizationTests.Login_create_and_select_produce_the_recorded_transcript` 与四个 `SelectCharacterCompensationCharacterizationTests` 原在基线失败清单；成功用例当前第一处差异即新增的 `0x0030`（帧序位置 27，紧跟 `0x01C7`，48 号长度 51 的 NOTI），录制需在实机抓包后更新。
- 偏离 Detailed Plan：
  - 延后范围扩到 Fighting 起的全部对局阶段（计划写 AwaitRank/AwaitResult），依上游且由计划授权"Fighting 中离场是否发 0x002B 按上游决定"；Loading/PeerLinking 离场使整局取消、房间回等待，不延后。
  - 补发帧用一帧 8 座位 `0x002B`，不像上游逐座位各一帧，也不补发离场者的 USER_LEAVE（本仓等待阶段离场同样不发 USER_LEAVE）。
  - `0x0037` 不收上游的 2 字节兼容形（Spec RQ-075 只收 5 字节）；flag 不限 0/1（上游拒收 > 1）。
  - 测试与基线比较之外未新增集成测试；真实 TCP 的完整一局与断线判负留 TASK-08。
- Remaining:
  - 实机待验：`0x0037` 实际长度（三方冲突）、`0x0039` 后是否靠 `0x002A` 解除结果等待、8 座位合帧与无 USER_LEAVE 的补发是否足够、`0x002F` 团队胜方座位取值、选角 `0x0030` 位置。
  - 截止调度把认领离场放在每轮开头：离场到投影之间有一次调度唤醒的延迟（同一 gameplay 执行器，毫秒级）。
  - 欠帧只在进程内；重启后房间本身也不存在，无需持久化。
  - 清理路径的结算提交结果不确定、而实际已提交时，sink 收到 `settled=null`，在座者收不到 `0x002F`/`0x0030`，只能等 AwaitResult 截止回等待（概率极低）。

## Result Review

- Verdict: approved（独立快速审查）。审查者在当前源码上跑 Protocol 281 项、Application 915 项全过；结算帧与上游 `BuildEndPvpBody`/`PvpRecordBodyBuilder` 逐字段一致；离场投影接口与塔的 `TowerDepartureProjectionQueue` 同形，归属合规。
- 提交前修正：已确认 `0x0039` 的成员不再被重新欠帧（新用例 + 反例）；`GameSelectCharacterCommand` 注释归位。主控复核 worker 复跑结果：Protocol PvpBattle/PvpRoom/SelectCharacter 228 项、Application Pvp/Execution 552 项全过。
- 移交 TASK-08/09：上游补发时附带被扣下的 USER_LEAVE，本仓不发；离场投影晚一轮调度（真实 TCP 留意）；`Owes` 只被测试使用；塔与 PvP 投影状态用 `*DomainService` 后缀的命名可另立技术债统一。
