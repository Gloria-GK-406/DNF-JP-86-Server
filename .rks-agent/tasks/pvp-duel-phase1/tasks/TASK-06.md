# TASK-06 — 协议切片（一）：频道信息、房间、大厅增量、决斗邀请

## Task Unit Contract

- Revision: 1
- Outcome and references: 线上可用的 CMD `0x00C3`、`0x0032`–`0x0036`、`0x003B` 与 NOTI `0x0029`（增量用）、`0x002A`、`0x002B`、`0x002C`、房内 NOTI `0x000B`；REQUEST_PEER / RESPONSE_PEER 在现有 CMD 内按 type 分派到决斗邀请；组队邀请一侧的房间互斥；房间与大厅的全部增量投影；进退房的城镇投影；领域拒绝到线上错误码的映射。Plan T-006（房间部分）；Spec RQ-001、RQ-003、RQ-010–015、RQ-020–022、RQ-075（`0x0036`）；AC-001、AC-003（领域+投影部分）、AC-010–013、AC-020。
- Owned boundary: `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`（目录成员）、新协议切片 `Server/src/Dfo.Server.Protocol/Game/PvpChannelInfo/`、`Game/PvpRoom/`、`ProtocolModule` 接线、`Game/Party/` 中 REQUEST_PEER/RESPONSE_PEER 两个 CMD 处理器与邀请通知（type 2 分支与组队侧互斥检查）、`Game/StateSynchronization/SupplementalSynchronizationCodec.cs`（0x0029 编码复用/拆出）；测试 `Server/tests/Dfo.Server.Protocol.Tests/Game/Pvp*/`、受影响的 Party 测试、`OpcodeArchitectureTests` 等注册检查；协议页人工区（`protocol/game/client-to-server/0x00C3.md`、`0x0032`–`0x0036`、`0x003B`，`server-to-client/NOTI-0x0029`–`NOTI-0x002C`）记录本单元采用的布局与证据标签（实机前不标 ClientVerified）。**不做**对局阶段与结算协议（TASK-07）。
- Required checks: 构建 `-m:1`；Protocol.Tests（新切片、Party、Opcode/注册类），Application.Tests 的 Commands 架构与注册类，Networking.Tests 的 Party 相关用例与 `SelectionLifecycleWholeCommandTests`；每次一个测试项目、单线程；与基线对比无新增失败。
- Dependencies: TASK-04、TASK-05
- Result review: required — 线上字节与帧序错误只能靠字节基线与集成测试发现，且实机前没有客户端校验；邀请改动触碰现有组队 CMD。

## Detailed Plan

### 布局来源
以 `Document/tasks/pvp-duel/design-r1.md` §5、`research.md` §2 为准；帧序参照上游 `E:\Project\DNF\ServerS4A21\Server\DfoServer\Network\Handlers\PvpRoomHandler.cs` 与 `Builders/Pvp/PvpRoomNotificationBuilder.cs`、`Parsers/Pvp/*`（只读参照，不拷代码），以及 `E:\Project\DNF\ServerS4A21\Docs\决斗场业务实现.md`。协议页的机器区给出客户端 reader 地址与静态长度下界。

### 入站（各一个切片目录、薄 handler、typed CMD）
- `0x00C3`：空体；CMD 回 `u8 1, i32 0, u8 0`（决斗频道），否则 `00 00`。
- `0x0032`：`u8 nameType`；nameType==0 时 `i32 len(0..29) + bytes`；`i16 map`；`u8 hasPwd`；hasPwd==1 时 `i32 len(0..8) + bytes`；`u8 special`（1 → 拒绝，练习房一期不开）。多余尾字节拒收。
- `0x0033`：`u16 number`；`u8 hasPwd`；hasPwd==1 时 `i32 len + ≤8 bytes`。
- `0x0034`：`u8 seat`；`u8 state`：0/1/2 换队，0xFE 踢人/退房/关位（按操作者与目标座位区分），0xFF 开位；3/4 观战一期拒绝。
- `0x0035`：`u8 ready`。`0x0036`：只接受 1 字节 `u8 mode`，其他长度拒收并记日志。`0x003B`：`i16 index`。

### 出站与投影
- 拒绝：CMD `00 err`（挂在原命令号），错误码映射集中一处（`PvpRoomRefusal` → 上游码：状态不对 19、房间不存在/未全准备 22、密码错 6、房满 4、无权限 8、两队不等 14；其余按上游最接近的码，并在映射处注明依据）。建房成功不回 CMD（上游做法，协议页标为待抓包）；进房成功回 `01 + 8×u8 ready`（邀请入房不发）。
- 房间帧：`0x0029`（房间快照，复用现有编码器）、`0x002A`（`u16 number, u8 state(1 等待/2 开战), u8 ownerSeat, i16 map, u8 mode, i32 matchingType`；销毁时 state 0、mode 2）、`0x002B`（`u16 number, u8 mode, u8 n, n×(u8 seat, u8 state, u16 uid)`）、`0x002C`（`u8 seat, u8 ready`）。座位状态线上值（0/1/2 队伍、0xFE 关、0xFF 空）映射在 codec 内，不直接用 `PvpTeam` 枚举值（TASK-04 审查移交）。
- 帧序按 design-r1 §4 a（建房、进房、退房、换队、改模式、改地图、准备、开局）。开局帧 `0x002D` 在本单元一并发出（它是 `SetReadyAsync` 的结果），其余对局帧归 TASK-07。
- 受众：房内成员用 `NotificationAudience.TheseSessions`（快照中的精确会话）；大厅用同监听端口的大厅就绪者（复用现有 PvP 大厅公示的受众写法）；回给请求方的直接经 `IGameCommandOutbox`。投影在写工作单元返回之后登记（`IPvpTownMoves` 只能在此时读）；不要在编码或 Commit 回调里调用 `Find`/`FindSeated`。
- 城镇：进房离城投影（复用 `TownDepartureProjection`）、退房/被踢回城投影（复用 `TownArrivalProjection`），USER_AREA 0xFE 与 USER_STATE 2/0 按上游顺序。
- 大厅增量：建房、成员变化、改模式、改地图、开战、回等待、销毁时发相应 `0x0029`/`0x002A`/`0x002B` 与 USER_STATE，使大厅视图与新入大厅快照一致（AC-003 的投影部分；回等待与销毁的触发点在 TASK-07 的对局投影里复用本单元的大厅投影函数）。
- 房内 P2P 端点：进房时向新人发已有成员的 `0x000B`、向已有成员发新人的 `0x000B`（复用 `PartyEndpointsMessage` 编码与端点来源，按接收者构造）。

### 邀请
- `GameRequestPeerCommandHandler`：type 0 原路径；type 2 → `IPvpRoomDomainService.InviteAsync`，成功则向被邀请者先发邀请人 USERINFO（现有单条 USERINFO 编码）再发 NOTI `0x0007`（type 字节写 2、peer token 写邀请 token；现有编码器 body[2] 固定 0 的问题按需扩展），拒绝回 `0x000A` 拒绝体（现有 `RequestPeerRefusal`）。
- `GameResponsePeerCommandHandler`：type 0 原路径，但先检查 `IPvpRoomQueryDomainService.IsSeated(本人)` 为真则拒绝组队（互斥）；type 2 接受 → `AcceptInviteAsync`，成功按入房投影（不发 `0x0033` 成功体），失败回 `0x000B` 拒绝体 `00 err 02`；type 2 拒绝不发包、不调领域。
- type 0 请求的目标若在房间中，组队邀请同样拒绝（互斥另一侧）。

### 测试
codec 字节测试（每个入站正常/越界/尾字节、`0x0036` 2 字节拒收、每个出站帧）；CMD 测试（成功与每类拒绝的回包、帧序、受众）；邀请（type 0 原行为不变的回归、type 2 成功/过期/在队/在房/黑名单、拒绝不发包）；在房接受组队被拒；大厅一致性在协议层用投影累积 vs 快照比较（真实 TCP 的三客户端集成放 TASK-08）。

## Result

- Outcome: done（第三轮：按第二轮审查 changes_required 返工）
- Changed:
  - 消息目录 `Data/Networking/NetworkMessages.cs`：C→S `GamePvpChannelInfo(0x00C3)`、`GameMakePvpRoom(0x0032)`…`GameSetPvpTeamMode(0x0036)`、`GameSetPvpMapIndex(0x003B)`；S→C 同号 CMD 应答与 NOTI `GamePvpRoomInfo(0x0029)`、`GamePvpRoomState(0x002A)`、`GamePvpSeatState(0x002B)`、`GamePvpReadyState(0x002C)`、`GameStartPvp(0x002D)`。
  - 新切片 `Protocol/Game/PvpChannelInfo/`；`Protocol/Game/PvpRoom/`（`Messages.cs`、`PvpRoomPayloadCodec.cs`（六个 decoder、七个 encoder、`PvpRoomRefusalCode` 唯一映射）、`PvpRoomCommands.cs`（`GamePvpRoomCommandHandler` 一类六个 CMD）、`PvpRoomProjection.cs`（`PvpRoomBroadcast` 通知与处理器、纯函数 `PvpRoomLobbyFrames`、`PvpRoomProjector`）、`PvpRoomRegistration.cs`）；`ProtocolModule` 两行接线。
  - Party：REQUEST_PEER / RESPONSE_PEER 在原 CMD 内按 type 分派（type 2 → `InviteAsync`/`AcceptInviteAsync`，传请求的 peer 值）；type 0 两侧加房间互斥（`00 13 00`）；`PvpRoomInviteRequestMessage`（NOTI 0x0007 type 2，7 字节）。
  - `StateSynchronization`：0x0029 编码拆出 `EncodePvpRoomInfoBody`，座位/房间状态字节显式映射，**每座位去掉多写的 1 字节**；`EncodePvpIdentityRoster` 改 internal，新 `PvpParticipantRosterMessage`（一帧多人 USERINFO0）并登记编码器。
  - `PvpRoomLobbyView` 由 Application 移到 `Abstractions/Pvp`（public，内容不变）。
  - 城镇回归投影加 PvP 专用选项：`TownReturnProjection.Frames(..., ownState = true)`、`TownArrivalProjection.ProjectAsync(..., ownState = true)` 及 `TownCaptureDeliveriesAsync` 透传；默认值保持副本回城帧不变，只有 `PvpRoomProjector` 传 `ownState: false`（不发本人 USER_STATE 0）。
  - 领域最小补充：`PvpInvitation.PeerToken`（init 属性）；`IPvpRoomDomainService.InviteAsync(..., int peerToken = 0)`、`AcceptInviteAsync(..., int? peerToken = null)`，接受时回显值与邀请值不等按 `InviteMissing` 拒绝且不消费原邀请（null 不检查，旧调用方行为不变）。
  - 测试：新 `Protocol.Tests/Game/PvpRoom/`（Codec、Command、Invite、ChannelInfo、TownReturn、StandIns；TownReturn 用真实 `SessionPresenceDomainService` + `TownTransitDomainService` 产生非空 `Town.Returns`，按登记顺序断言离开者帧序 USER_AREA → AREA_USERS → 0x002B（房主离开再加 0x002A）且无自身 USER_STATE，另断言副本回城仍以 USER_STATE 0 开头；进房名册加新人 uid 最小的排序例）；`CommandRegistrationTests` 加一例；`SupplementalSynchronizationTests` 改 0x0029 期望；Party 测试补构造参数；`Application.Tests` `PvpRoomDomainServiceTests` 加 token 不符一例，写入口表两行签名更新。
  - 协议页人工区：`0x00C3`、`0x0032`–`0x0036`、`0x003B`、`0x000A`、`0x000B`、`NOTI-0x0007`、`NOTI-0x000B`、`NOTI-0x0029`–`NOTI-0x002D`；`catalog.md` 新增一节并更新 `0x000A`/`0x000B` 两行。
- 线上布局与错误码：
  - 入站：`0x00C3` 空体；`0x0032` `u8 nameType [i32 len≤29 + bytes] i16 map u8 hasPwd(0/1) [i32 len≤8 + bytes] u8 special`；`0x0033` `u16 number u8 hasPwd [i32 len≤8 + bytes]`；`0x0034` 恰 2B；`0x0035` 恰 1B；`0x0036` 恰 1B；`0x003B` 恰 2B。畸形不抛（抛会断连接），CMD 记日志并回拒绝：`0x0032`/`0x0033` 19，其余 8。0x0034：0/1/2 换队（`TeamOf` 显式映射）、0xFE 自己=退房/他人=踢/空=关、0xFF 开、3/4 观战回 8。
  - 出站：拒绝 `00 reason` 挂原命令号；`0x00C3` `01 00000000 00` / `00 00`；`0x0033` 成功 `01 + 8×ready`；`0x0029` 每房 `u16, u8 nameType[, dstr], u8 state, u8 owner, i16 map, u8 mode, 8×(u8 state, u16 uid), u8 hasPwd, i32 0`；`0x002A` 11B（销毁体 `0,0,0,2,0`）；`0x002B` `u16, u8 mode, u8 n, n×(u8 seat, u8 state, u16 uid)`；`0x002C` 2B；`0x002D` `u8 map(已抽定), u8 mode`；NOTI 0x0007 type 2 `u16 uid, u8 2, i32 peer`。
  - `PvpRoomRefusalCode.Of`：19（SessionNotCurrent、NotDuelChannel、InParty、AlreadySeated、NotSeated、NotWaiting、InvalidPassword、UnsupportedMode、InvalidMap、LastOpenSeat、邀请类、对局类、ShuttingDown）；22（RoomMissing、NotAllReady、TooFewMembers）；6 WrongPassword；4 Full；8（NotOwner、InvalidSeat、InvalidTeam）；14 UnbalancedTeams。邀请 `00 reason 02`：请求 3/4/0x12/其余 0x13；接受 4/其余 0x13。
- 帧序（`PvpRoomProjector`，全部在 `WriteAsync` 返回后登记）：
  - 建房：房主 USERINFO1 → 0x0029(此房) → USER_AREA(0xFE)；其他大厅就绪者 0x0029 → USER_STATE(房主,2)；离城投影。
  - 进房：新人一帧 USERINFO0 名册（全部成员含新人，按 uid 排序，上游形态）→ 已有成员 USERINFO1(routing=其 uid) → 0x000B(已有成员) → 本人 USERINFO1 → 0x000B(本人) → CMD 0x0033 成功（邀请入房不发）→ 0x002B → USER_AREA(0xFE)；已有成员 USERINFO0 → USERINFO1 → 0x000B(新人)；大厅就绪者（不含新人）0x002B → USER_STATE(新人,2)；离城投影。
  - 准备：非房主只给成员 0x002C；开局成员 0x002C(房主,1) → 0x002D → 0x002A(2)，其余大厅 0x002A。
  - 其余变化：等待阶段准备变化给成员 0x002C；大厅就绪者**排除被送回城的离开者**，收 USER_STATE(离开者,0) → `PvpRoomLobbyFrames.For`（变化座位 0x002B，改模式列全部占用座位，房主/地图/状态变化 0x002A，销毁先发空座再发销毁体）；离开者本人先走 `TownArrivalProjection`（`ownState: false`：USER_AREA → [同屏角色帧] → AREA_USERS，无本人 USER_STATE，上游 `PublishTownReturnAsync`），之后才单独收到同样的 0x002B/0x002A，同样不含自己的 USER_STATE（上游主动退房、`CompleteKickedMemberExitAsync`、`CompleteOwnerExitAsync`、`PublishReturnedPvpStateAsync`）。
- Why:
  - 0x0029 每座位多 1 字节是证伪的记载（上游 `WriteRoomInfo` 与文档、协议页静态读取一致）。
  - `PvpRoomLobbyView` 移到 Abstractions：增量投影须与入场快照同一映射，避免重复事实；领域语义未改。
  - peer token：上游要求回显 token 与邀请一致；领域原无 token，补成可选参数与 init 属性，向后兼容，不一致不消费原邀请（上游"保留更新的替换邀请"）。
  - 0x00C3 按请求者频道判定：审查者静态核对 b1 `0x1119FB0`，成功（状态≠10）与非 0x15 失败都进 `0x26BAC60` 打开决斗频道选择框，由 `0x26BAB90` 判有无频道；普通频道回失败体同样能打开，判定正确。已写入协议页与处理器注释，删去条件句。
  - 大厅受众 = `ReadyOn(port)` 的精确会话（含房内成员），上游即对同端口全部就绪会话广播。
- Checks（最终源码，`PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`，`-m:1`、单项目、`MaxParallelThreads=1`）：
  - `dotnet build Server/86JP.Server.slnx -m:1`：0 警告 0 错误。
  - Protocol.Tests 全量：2422 通过 0 失败（PvpRoom 88 例；城镇投影、副本回城相关用例全过）。反例：把 `ownState: false` 临时改回 true，`PvpRoomTownReturnTests` 两例失败；已还原。
  - Application.Tests 过滤 `Execution|Commands|Notifications|Registr|Architecture|Pvp|Sessions|Census|DependencyGraph`：1134 通过 0 失败。
  - Host.Tests `Opcode|Architecture`：19 通过。
  - Networking.Tests `Party|SelectionLifecycleWholeCommandTests|Town|Return`：13 通过、9 失败，9 项全在 `baseline-failed.txt`（组队/塔录制转写差异）。
  - 换行：`git diff --numstat` 与 `--ignore-cr-at-eol` 一致；新文件 LF。
  - `check-server-documentation-sync.py` 基线即 521 项失败（不认现有注册写法），新增项同类，未改工具。
- Remaining / 移交 TASK-07：
  - 可复用：`PvpRoomProjector.ProjectAsync`（回等待、取消、放弃结算、最后一人离场销毁走通用分支）、`PvpRoomLobbyFrames.For`、`Listener`、`BroadcastAsync`、`Full`/`Identity`、`PvpRoomBroadcast`。
  - 通用分支对 `Released` 立即发座位移除；AwaitResult 期间按接收者延后（RQ-035）及 Fighting 中离场是否发 0x002B 由 TASK-07 决定。
  - 进房离城投影只以空 `IPvpTownMoves` 单测；退房回城已用真实城镇服务单测；TCP 集成与实机留 TASK-08。
  - 房间销毁且多人同时被送回城时，被送回者彼此收不到对方的 state 0（上游逐个回城时会收到）；当前领域每次只空出一个座位，实机待验。
  - 实机待验：建房成功无 CMD、无回包 handler 的 0x0034/0x0036/0x003B 收拒绝 CMD 的表现、房内 USERINFO1 routing uid、NOTI 0x0007 type 2 七字节、邀请拒绝体。

## Result Review

- 第一轮 changes_required：离开者帧序与上游相反（阻断）；进房 USERINFO0 形态、0x00C3 协议页结论、peer token 未核对（非阻断）。审查者按函数分块读 b1 客户端 `0x1119FB0` 应答处理，确认普通频道回失败体也会打开决斗频道选择框，0x00C3 判定功能正确、Spec 无需修订。
- 第二轮 changes_required：离开者回城仍以本人 USER_STATE 0 开头（副本回城帧序）。
- 第三轮 approved：`ownState` 选项只在 PvP 路径关闭；`PvpRoomTownReturnTests` 用非空 `Town.Returns` 断言帧序且有反例；副本回城无回归。审查者复跑 Protocol 全量 2422 项全过、Networking Town/Return/Party 子集仅基线失败。
- 移交：多人销毁时被送回者彼此收不到 state 0、回城帧序缺真实 TCP 验证（TASK-08/09）；组队侧房间互斥在写单元外（窗口极小，TASK-08 记录）。
