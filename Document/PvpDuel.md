# 决斗场

修改决斗房间、对局阶段、结算、离场、停服或决斗大厅投影前读本文。协议布局、帧序证据与待实机项的逐条记录在
`protocol/`：[catalog.md](../protocol/catalog.md) 的两节"决斗场一期 房间侧／对局侧"及其链接的协议页；
本文只写服务端规则、设计考量与坑，不复制字节布局。

## 归属与代码位置

| 职责 | 位置 |
|---|---|
| 契约（房间快照、阶段、拒绝原因、对局结果、战绩、权益） | `Server/src/Dfo.Server.Abstractions/Pvp/` |
| 房间与对局的唯一 owner | `Application/Pvp/PvpRoomDomainService.cs`（写面 `IPvpRoomDomainService`，读面 `IPvpRoomQueryDomainService`） |
| 结算 | `Application/Pvp/PvpSettlementDomainService.cs` → `Persistence/Pvp/SqlitePvpRecordDomainStore.cs` |
| 规则 | `PvpRoomRuleService` + 静态 `PvpRoomRules`（时长、分队、开局、继任）；`PvpSettlementRuleService` + 静态 `PvpGradeRules`（权益、经验、段位） |
| 内容 | `Infrastructure/Content/PvfPvpGradeDefinitionSource.cs`（`etc/pvp_ref.etc`）、`PvfPvpMapDefinitionSource.cs`（`etc/pvpmapbasicdata.etc`） |
| 截止调度与停服 | `Application/Pvp/PvpDeadlineScheduler.cs`（调度器、`PvpShutdownService`）；来源与内部命令在 `Protocol/Game/PvpBattle/PvpBattleDeadlines.cs` |
| 启动校验 | `Application/Pvp/PvpStartupVerification.cs`，由 `GameServer/GameServerStartupService` 调用 |
| 协议切片 | `Protocol/Game/PvpChannelInfo/`、`PvpRoom/`（房间命令、大厅增量 `PvpRoomLobbyFrames`、`PvpRoomProjector`）、`PvpBattle/`（对局上报、结算帧、离场投影队列、结果界面欠帧）；决斗邀请在 `Party/` 的 REQUEST_PEER／RESPONSE_PEER 内按 type 2 分派；选角补发 `0x0030` 在 `SelectCharacter/` |
| 大厅就绪名单 | `Application/Sessions/PvpLobbyDomainService.cs`：只记谁在决斗大厅就绪，不保存房间 |

战绩表结构与 v10→v11 迁移见 [数据库结构版本与迁移](DatabaseSchemaMigration.md)。

## 房间 owner 与事务

- 房间运行态只在 `PvpRoomDomainService` 的内存里。每个写方法加入调用方的可写事务（无事务即抛），改动经事务参与者
  `Changes` 暂存，Prepare 乐观校验、Commit 应用；`CommitCanFail=false`，SQLite 才是那个可失败的参与者。
  外层事务回滚时房间、邀请、已终局待结算记录与停服标志都不变。
- 进房离城、退房回城在参与者 Commit 内执行：位置服务不是事务化的，在写方法体内移动会在写库失败后留下
  "不在城镇也不在房间"的角色。移动结果经 `IPvpTownMoves` 在提交时填入，**只能在 `WriteAsync` 返回后读取**；
  Commit 内移动失败只记 Error。
- `RoomsOn` 只返回已提交房间（即大厅可见的房间）；`Find`／`FindSeated`／`IsSeated` 在可写事务内看本事务暂存。
  不要在编码器或 Commit 回调里调用它们。
- 唯一 `u16` 房间号是 owner 分配的显示编号；身份是 `PvpRoomId`／`PvpMatchId`（Guid）。
- 与组队互斥：在队不能建房／进房、在房不能邀请组队或被邀请组队。房间一侧的检查在写单元内；组队一侧
  （`GameRequestPeerCommandHandler`、`GameResponsePeerCommandHandler` 的 type 0）读 `IsSeated` 发生在组队写入之外，
  两者之间理论上有一个极小窗口，单执行器串行下不可达，后续若并行化须收进同一写单元。

## 对局阶段与截止时间

| 阶段 | 进入 | 离开 | 截止（`PvpRoomRules`） |
|---|---|---|---|
| Waiting | 建房、回等待 | 房主开局（`RefuseStart`：≥2 人、非房主全就绪、团队模式两队人数相等；模式只开 1 个人、2 团队） | 无 |
| Loading | 开局 | 全员 `0x012A` | 60 s 未齐 → 整局取消回 Waiting |
| PeerLinking | 全员加载 | 全员 `0x012B` | 30 s 未齐 → 整局取消 |
| Fighting | 全员连通 | 终局（个人：剩一人；团队：剩一队）或判平 | PVF 对战时长 + 30 s → 判平；心跳 30 s 无 → 该人离场（只记逃离，保留座位） |
| AwaitRank | 终局 | 全员 `0x003A` 或截止 → 结算 | 10 s |
| AwaitResult | 结算写入 | 全员 `0x0039` 或截止 → 回 Waiting（清准备与存活） | 10 s |

- `0x0038`（客户端超时上报）只在 PVF 对战时长前 5 s 之后才被受理（时钟漂移余量），早于此拒收 `TooEarly`。
- 截止时间都在房间 owner 里；调度器不持有截止表，每轮经内部命令扫描（`Deadlines`）推进到期房间，失败按项隔离。
  房间变化只在截止时间提前或新出现时唤醒调度器。
- 已终局待结算的记录 `PvpMatchConclusion` 独立于房间：房间可能在结算前因最后一人离开而销毁，结算只读冻结的结论。
  AwaitRank 的截止由结论的 `SettleBy` 驱动。

## 结算与幂等

- `PvpSettlementDomainService` 读冻结结论与今日计数 → 规则服务决策 → Store `SettleAsync`。`pvp_matches.match_id`
  是幂等键：重复结算回读既有结果（`AlreadySettled`），不改战绩、不重发事件；首次写入才发 `pvp.match-settled` v1
  （一期无订阅者）。写库失败则房间阶段不前进。
- 结算失败退避重试：下一轮间隔为最早逾期项已逾期时长的 1/4，夹在 1–30 s；逾期超过 5 min 放弃
  （`AbandonPvpSettlementCommand`）：删结论，房间若仍停在该局 AwaitRank 就回 Waiting，记 Error，不改战绩。
  先前一次提交结果不确定的结算可能已经写入，查 `pvp_matches` 确认。
- 权益按频道身份（`PvpSettlementRuleService.EntitlementsOf`）：自由决斗频道一律不记录；决斗频道按登录环境，
  8 不记录，24 记录并计段位经验（每游戏日前 50 胜加经验、前 10 负扣经验）。表外环境是配置错误。
  不记录的频道仍写对局与结果行，只是战绩不变。
- 经验规则（`PvpGradeRules`）：经验为空读作当前段位下限；0 段首胜直接到 1 段下限；满段胜不加；负按段位扣、不低于 0；
  逃离按负；平局不变。游戏日按每日重置时刻划分（[每日重置](每日重置时间与周期计数.md)），以开局时刻归属。

## 离场

清理链（断线、回选角、选角替换）在 `SelectCharacterLifecycleServices.DepartRoomAsync` 调 `Depart`；心跳超时由截止调度触发。

| 离场时阶段 | 结果 |
|---|---|
| Waiting | 按退房处理（让出座位，房主离开则继任，最后一人离开则销毁） |
| Loading／PeerLinking | 整局取消，房间回 Waiting，不计战绩 |
| Fighting | 记阵亡、无击杀者、逃离，可能导致终局；逃离者结算为负 |
| AwaitRank／AwaitResult | 结果不变，只从确认等待中移除 |

- 会话结束（断线／回选角／被替换）一律让出座位、不回城；心跳超时只记逃离，保留座位。
- 自开事务（断线）：离场一步先提交；若因此该结算，再单开一步结算，普通失败只记 Warning 交给调度器重试。
  加入外层事务（选角替换）：只做离场，结算由提交后被唤醒的调度器完成；外层回滚则什么也不发生。
- 离场投影：已提交的离场总交给每个 `IPvpRoomDepartureSink`（逐个隔离，最后重抛原异常）。协议侧
  `PvpDepartureProjectionQueue` 入队并唤醒截止调度；调度每轮**先**认领离场投影，再看截止，保证离场帧先于同轮的截止／结算帧。
  因此离场帧比离场提交晚一次调度唤醒（同一执行器，毫秒级）。唤醒走信号而不是时钟，冻结时钟下照样投影。
- 结算结果不确定（`TransactionOutcomeUncertainException`）而实际已提交时，sink 收到 `settled = null`，在座者收不到
  `0x002F`／`0x0030`，只能等 AwaitResult 截止回等待。

### 结果界面期间的座位延后

客户端在关闭结果界面前保留对局角色，Fighting 起的对局阶段里空出的座位不立即给房内成员看：

- 入账：变化后房间仍在对局阶段时，尚未确认结果界面的在座成员记"欠这个房间的座位"，并从这次大厅广播中排除；
  已确认者与大厅其余人立即收到。
- 释放（按当时房间现编一帧 8 座位 `0x002B` + `0x002A`）：该会话的 `0x0039` 被受理、下一局开局、该会话退房或被踢。
- 丢弃：该会话以离场离座（断线、回选角、被替换）、房间销毁。确认超时回等待不释放；迟到的 `0x0039` 被领域拒绝，不释放。
- 欠帧只在进程内（`PvpResultScreenDeferrals`），重启后房间也不存在，无需持久化。补发不附上游那条 USER_LEAVE。

## 停服

`PvpShutdownService.StoppingAsync`：主机在任何托管服务 `StopAsync` 之前调用它，此时执行器仍在运行。轮内顺序：
先认领离场投影 → 取消一切未终局对局（同时置停服标志，此后开局拒绝 `ShuttingDown`）→ 结算全部已终局待结算对局。
先取消再结算，两步之间不会冒出新的终局，所以停服不产生负场。

**坑**：不能挂成 `IGameplayProducerDrain`。drain 在 `GameplayExecutorLifetime.StoppedAsync` 里跑，而 TouchSocket 的 TCP 服务
在自己的 `StopAsync` 里关会话、派发断线，drain 会晚于断线清理，正在对战的人被判逃离。`PvpShutdownHostTests` 守着这一点。
进程崩溃时未结算的对局全部丢失：对局只在内存里，崩溃不结算、不判负是接受的边界，已提交的结算不受影响。

## 启动校验

`PvpStartupVerification` 在有 PVF 时、内容预热之前运行：遍历"监听端口 ∪ 频道广告端口"，经 `IChannelEntryDomainService`
判出的每个决斗频道都要能取到权益，并强制加载两份 PVF 表；任一失败即启动失败（内容预热只记 Warning，不能当校验）。
没有 PVF 的部署不跑这项校验。

## 投影要点

- 大厅受众是同监听端口 `IPvpLobbyDomainService.ReadyOn` 的全部就绪会话，房内成员也在其中。
- `PvpRoomLobbyFrames.For(before, after)` 是唯一的增量映射：新房整房 `0x0029`，座位／模式变化 `0x002B`，
  房主／地图／状态变化 `0x002A`，销毁先发空座再发 state 0。准备标记不属于大厅视图。不变量：任一时刻大厅客户端由
  增量累积出的视图等于此刻新会话入大厅取得的 `0x0029` 快照；入场快照与增量共用 `PvpRoomLobbyView`。
- 离开者（退房、被踢）先走城镇回归（`ownState: false`：不发本人 USER_STATE），之后才收到房间变化；大厅其余人先收
  USER_STATE(离开者, 0) 再收房间帧。
- CMD 不能注入规则服务，`0x0030` 所需的段位上下界经只读领域查询 `IPvpRecordDomainService.ViewAsync` 取得。
- 房间命令的解码器从不抛异常（抛了会断开连接）：畸形体交给命令回拒绝码并记日志。

## 测试与证据边界

- 领域与规则：`Server/tests/Dfo.Server.Application.Tests/Pvp/`（含截止、离场各阶段、停服、结算幂等与回滚反例）；
  主机停服 `Dfo.Server.Host.Tests/PvpShutdownHostTests.cs`。
- 协议字节与帧序：`Dfo.Server.Protocol.Tests/Game/PvpRoom/`、`Game/PvpBattle/`、`SelectCharacter/SelectCharacterPvpRecordTests.cs`。
- 真实 TCP：`Dfo.Server.Networking.Tests/Integration/PvpDuelWireTests.cs`——三客户端大厅一致性（每步与新会话快照比对）、
  完整一局与战绩、断线判负与欠帧释放、决斗邀请、密码错误／房满／在房拒组队。测试主机时钟冻结，**截止类路径不在这里测**。
- 实机待验（A21 客户端行为，自动化证明不了）：`0x0037` 实际长度；`0x0039` 后靠 `0x002A` 解除结果等待是否足够；
  欠帧补发合成一帧 8 座位且不带 USER_LEAVE 是否足够；团队模式 `0x002F` 胜方座位；选角 `0x0030` 的位置；建房成功无 CMD 回包；
  `0x0034`／`0x0036`／`0x003B` 收到拒绝 CMD 的表现；房内 USERINFO1 的 routing uid；决斗邀请 NOTI `0x0007` type 2 与拒绝体；
  换模式／换队不清准备标记。逐条状态以协议页为准。
- 房间销毁时若多人同时被送回城，被送回者彼此收不到对方的 USER_STATE 0；当前领域每次变化只空出一个座位，此情形不出现。
