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
