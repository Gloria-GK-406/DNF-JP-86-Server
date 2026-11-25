# 决斗场

修改决斗房间、对局阶段、结算、离场、停服或决斗大厅投影前读本文。协议布局、帧序证据与待实机项的逐条记录在
`protocol/`：[catalog.md](../protocol/catalog.md) 的两节"决斗场一期 房间侧／对局侧"及其链接的协议页；
本文只写服务端规则、设计考量与坑，不复制字节布局。

## 归属与代码位置

| 职责 | 位置 |
|---|---|
| 契约（房间快照、阶段、拒绝原因、对局结果、战绩、权益） | `Server/src/Dfo.Server.Abstractions/Pvp/` |
| 自由房和普通匹配业务 | `PvpDuelDomainService`，维护普通匹配入口、定级和 RP。 |
| 传统与巅峰业务 | `PvpTraditionalDuelDomainService`，维护正式结算权益、巅峰参赛阵容与 GP。 |
| 房间与对局的唯一 owner | `Application/Pvp/PvpRoomDomainService.cs`（写面 `IPvpRoomDomainService`，读面 `IPvpRoomQueryDomainService`） |
| 结算 | `PvpSettlementDomainService` 根据冻结结论的业务归属调用对应的业务服务，再通过 `SqlitePvpRecordDomainStore` 提交。 |
| 规则 | `PvpRoomRuleService` + 静态 `PvpRoomRules`（地图、共用截止和继任）；`PvpBattleModeRules`（分队、开局、战斗推进与判胜）；`PvpSettlementRuleService` + 静态 `PvpGradeRules`（权益、经验、段位） |
| 王者战编队 | `PvpTotalMatchTeamDomainService` 管账号队名、三角色编队和花名册位置换算；`SqlitePvpTotalMatchTeamDomainStore` 存角色 ID |
| 内容 | `Infrastructure/Content/PvfPvpGradeDefinitionSource.cs`（`etc/pvp_ref.etc`）、`PvfPvpMapDefinitionSource.cs`（`etc/pvpmapbasicdata.etc`） |
| 截止调度与停服 | `Application/Pvp/PvpDeadlineScheduler.cs`（调度器、`PvpShutdownService`）；来源与内部命令在 `Protocol/Game/PvpBattle/PvpBattleDeadlines.cs` |
| 启动校验 | `Application/Pvp/PvpStartupVerification.cs`，由 `GameServer/GameServerStartupService` 调用 |
| 协议切片 | `Protocol/Game/PvpChannelInfo/`、`PvpRoom/`（房间命令、大厅增量 `PvpRoomLobbyFrames`、`PvpRoomProjector`）、`PvpBattle/`（对局上报、结算帧、离场投影队列、结果界面欠帧）；决斗邀请在 `Party/` 的 REQUEST_PEER／RESPONSE_PEER 内按 type 2 分派；选角补发 `0x0030` 在 `SelectCharacter/` |
| 大厅就绪名单 | `Application/Sessions/PvpLobbyDomainService.cs`：只记谁在决斗大厅就绪，不保存房间 |

战绩表结构与 v10→v11 迁移见 [数据库结构版本与迁移](DatabaseSchemaMigration.md)。

## 频道类型与技能权限

频道入口从频道目录解析 `ChannelEntryKind`。客户端登录环境 8 对应自动匹配决斗，24 对应传统决斗，13 对应自由决斗。同类型频道共享玩法规则，新增频道只需在目录中使用对应环境。

三类决斗频道共用决斗技能组。`PvpSkillEditPolicy` 根据频道类型开放技能操作：传统决斗允许加点、换槽和切页，自动匹配决斗允许给当前页加点。技能操作还需通过会话及候场状态校验，对局和结算期间锁定。

## 普通决斗 1vs1 匹配

自动匹配频道按监听器维持 1vs1 队列。以精确选角会话登记，按加入顺序在不同账号之间配对。取消、断线和选角切换清理该会话。组队、已占决斗座位及离开大厅的会话不参与配对。

队列和临时房由房间领域服务在同一事务提交。临时房只向两名匹配者发布，不参加房间列表、快速加入或邀请。双方确认对阵并报告各自加载和对手 P2P 就绪后，复用个人战的开战、KO、段位结果与结算。入场超时或断线会释放两席，未开战时不记结果。双方完成结算后回普通决斗大厅。A21 包形和验收步骤见 [CH60 匹配实机测试](tasks/pvp-duel/普通决斗场匹配实机测试.md)。

普通擂台用同一房间领域服务管理独立队列。六名选角会话配为两队，全员确认后加载，每人完成到同局其他五个座位的连通才开战。匹配入场完成后复用房间擂台的轮换与终局，整场共用 1vs1 的定级场次、赛季和 RP 流水。见 [普通决斗场擂台赛](tasks/pvp-duel/普通决斗擂台赛.md)。

## 巅峰王者战匹配

频道目录以 `AllowsTotalMatchmaking` 开放类别 7。两名不同账号的代表按各自的选角会话排队。三名候选需穿齐武器、称号、五件防具、三件首饰。配对时重新读取双方阵容并固定整场候选资料。临时房以两个代表会话完成对阵确认、加载和连接，开战后沿用王者战的选人、轮换与判胜。

完成整局或开始实际战斗后一方弃权时，按账号记录一场定级。GP 初值为 1000，20 场定级期间使用 K=40，定级完成后使用 K=20，预期胜率与普通决斗 RP 使用相同公式。正式对战的决斗经验按实际出场角色计算，共用每日经验额度。GP 独立于普通决斗 RP，同账号更换代表仍继承本赛季的定级与 GP。加载和首轮开打前取消、双方逃离不计分。CH68 自由王者战仍沿用该频道的结算权益。

## 决斗频道与每日任务

频道目录用 `ChannelEntry:TotalMatchmakingChannelIds` 把登录环境 8 的指定频道解析为 `TotalMatchDuel`，其余环境 8 频道为 `MatchmakingDuel`。A21 的登录环境沿用 8，客户端频道脚本须包含相同编号的条目。普通匹配的原有对战入口、RP 和巅峰 GP 按实际模式结算。

普通自动匹配大厅使用 PVF 的 `[casual daily mission]`，巅峰大厅使用 `[total match daily mission]`。按角色、游戏日、任务池、组号从每组的 PVF 权重选一项。已有当天进度优先固定该组任务，因此旧版已推进的任务 136 可继续。首次进入大厅及整局结算返回时推送完整任务快照。环境 13 的自由房与环境 24 的传统房沿用房间玩法和结算规则。

任务条目从 `pvp_mission/mission.lst` 和 `.msn` 读取。匹配正式结算时，参赛、胜场、职业、击倒、连胜与巅峰队员结果使用冻结结果。无色技能、HP 条件和胜利姿势由 A21 上报，经选角会话、对局阶段及任务类型校验后进入同一次结算。无色按当前出场者已学习且 PVF 消耗物是 3037 的技能使用次数计。胜利姿势按三个编号分别计数，三项的最小值为已完成轮数。连胜多组采用非重叠的连续胜场组，原版分组细节待实机核对。

其他任务完成时在同一 SQLite 事务写任务进度、奖励对局凭据和「胜利的证明」虚拟槽余额。任务包 `0x0143` 在测试服活动 2207 未开启时每项 10 字节。发奖使用 `0x000E` 同步绝对余额。非零进度、无色、HP 和胜利姿势的 A21 实机表现仍待复测。见 [任务通知](../protocol/game/server-to-client/NOTI-0x0143.md)。

## 斗兽场

斗兽场在普通 1v1 与巅峰王者战的匹配之上记录一轮逐场挑战。入口按频道类型路由。普通由角色完成定级并达到 PVF 的 RP 门槛。巅峰由账号完成巅峰定级，当前角色需为代表且满足王者战编队与装备要求。普通挑战绑定角色，巅峰挑战绑定账号及开轮时的代表。两边沿用各自的排位结算，队列内允许和未开挑战的一方配对。巅峰混排和额外 GP 门槛待 A21 实机核对。

每轮从 PVF `etc/pvpcolosseum.etc` 读取入场费、胜负上限和奖池。当前配置消耗 200 个胜利的证明，10 胜或 3 负结束。当前基准每日开轮上限为 10，PVF 第二个上限值所适用的条件待核。扣费与开轮同事务。排队和临时房逐座位带上挑战 ID，取消及未开始的战斗不改变进度。完成一场有胜负的正式对局时，挑战胜负与日常任务一起提交。巅峰按整场结算一次，只有参赛胜方推进其任务池内的「斗兽场三胜」。平局保留挑战进度。

奖励按最终胜场逐组抽取，奖项在挑战结束时保存。进入及返回匹配大厅同步状态，待领奖时推送奖励列表。确认后一次性发放，背包不足的实物由系统邮寄。因离场判负并达到终局的实物奖励直接发邮件。重登时恢复未结束挑战或待领奖品。布局和实机验收边界见 [斗兽场状态](../protocol/game/server-to-client/NOTI-0x0262.md) 与 [斗兽场奖励](../protocol/game/server-to-client/NOTI-0x0263.md)。

普通斗兽场参赛者按已有段位规则结算经验，与其他正式决斗共用每日经验上限。胜负与经验提交后，结算页打开前推送本轮胜负，回大厅后再推送任务和待领奖状态。

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
- 个人战、组队战和擂台战的成员可在等待房切换自己的观战座位。观战占用房间座位，切换时清除准备。房主观战时仍可在参战者就绪后开局。分队、开战人数和战绩只看参战座位。
- 观战成员随房收到开局、淘汰和结果。结算视图读取观战角色的现有段位，经验变化为零。观战者退出不判负，结果确认只释放该客户端暂缓的座位更新。房间回到等待时也给仍在座的观战者补发暂缓帧。参战者决定何时结算和回房。

## 对局阶段与截止时间

个人战、组队战和擂台战各有一个无状态规则实例。房间根据已提交的模式选取实例，等候阶段切换模式时用目标规则重新分队。对局状态继续随房间快照提交。领域服务统一推进房间生命周期、登记截止和冻结结算。

| 阶段 | 进入 | 离开 | 截止（`PvpRoomRules`） |
|---|---|---|---|
| Waiting | 建房、回等待 | 房主开局（`RefuseStart`：≥2 人、非房主全就绪，团队和接力模式两队人数相等） | 无 |
| Fighting | 开局 | 终局（个人：剩一人；团队或接力：剩一队）或判平 | 普通对局从开局起算 PVF 时长 + 30 s 兜底。接力从每轮出场起算，覆盖 A21 的 180 s 实际时限和 4.64 s 倒计时后，再加 30 s。心跳 30 s 无则记逃离并保留座位。 |
| AwaitRank | 终局 | 全员 `0x003A` 或截止 → 结算 | 10 s |
| AwaitResult | 结算写入 | 全员 `0x0039` 或截止 → 回 Waiting，临时匹配房回大厅 | 自由房 10 s，自动匹配 20 s |

- 开局即 Fighting，没有加载与连通阶段（Spec SA-1）：普通房间的 A21 客户端开局后不发 `0x012A`（加载完成）与 `0x012B`
  （P2P 连通），2026-11-15 实测服务端从未收到，旧实现因此 60 s 后取消整局、随后的 KO 被拒，表现为"个人战 KO 后不结算"。
  70 服务端 `start_pvp` 也是开局即置对战状态（70Reference）。这两条 CMD 仍解码，只记 Information 日志，不进领域。
- 普通对局的 `0x0038`（客户端超时上报）只在开局后 PVF 对战时长前 5 s 之后受理（时钟漂移余量），早于此拒收 `TooEarly`。接力客户端按本轮计时，超时分支走本地淘汰路径。服务端仅在轮换后启动新的兜底截止，等待接力换人期间由出场截止推进。详见 [`0x0038`](../protocol/game/client-to-server/0x0038.md)。
- 接力模式 3 的出场状态存在 `PvpMatch.ModeState`，随同一场对局提交。首轮与补位分别采用参照服的 7 秒和 3 秒延时。当前出战者保持上场，空缺优先选活着且申请出战的队员，再按座位顺序。仅处理出战者的 KO，轮换未耗尽某队成员时继续使用同一场对局。该时序和优先顺序待 A21 实机确认。
- 王者战模式 8 由 `PvpTotalMatchBattleRules` 管理每边的三名冻结候选，座位始终对应代表会话。每轮淘汰只扣掉当前候选，3 秒内接受双方各一条有效死亡报告。还有候选的一方补位，幸存者继续出战。出场后重算整轮时间，客户端按 PVF 的赛事擂台时限处理到点。耗尽一边或代表离场触发整场结算。CH68 已实测两次补位后结算并回到等待房。
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
- 权益按频道类型（`PvpSettlementRuleService.EntitlementsOf`）：自由决斗和自动匹配决斗不记录战绩。传统决斗记录战绩并计段位经验（每游戏日前 50 胜加经验、前 10 负扣经验）。
  不记录的频道仍写对局与结果行，只是战绩不变。
- 经验规则（`PvpGradeRules`）：经验为空读作当前段位下限；0 段首胜直接到 1 段下限；满段胜不加；负按段位扣、不低于 0；
  逃离按负；平局不变。游戏日按每日重置时刻划分（[每日重置](每日重置时间与周期计数.md)），以开局时刻归属。
- 王者战在具备正式决斗权益时，每位实际出场的角色按自己的段位和所属队伍的整场胜负结算一次，与普通决斗共用角色每日额度。开打前中断、服务端取消与双方逃离不改角色战绩。巅峰匹配应用正式权益。CH68 自由房的经验变化为零。见 [王者战决斗经验规则](tasks/pvp-duel/王者战可行性方案.md#正式匹配决斗经验规则)。
- 库中段位高于 PVF 满级（迁移前的旧数据可能有）不抛错：选角与结算的 `0x0030` 上下界、空经验的起点都按满级段取，
  显示的段位与库值不变并记 Warning；结算会按经验重算并写回合法段位。

## 离场

清理链（断线、回选角、选角替换）在 `SelectCharacterLifecycleServices.DepartRoomAsync` 调 `Depart`；心跳超时由截止调度触发。

| 离场时阶段 | 结果 |
|---|---|
| Waiting | 按退房处理（让出座位，房主离开则继任，最后一人离开则销毁） |
| Fighting（开局后任何时刻） | 记阵亡、无击杀者、逃离，可能导致终局；逃离者结算为负（与 70 `GiveOuterPenalty` 一致） |
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

客户端在关闭结果界面前保留对局角色，开局（Fighting）起的对局阶段里空出的座位不立即给房内成员看：

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
- 座位 uid：占用座位写会话 uid，空座与关闭座位写 `0xFFFF`——客户端房间对象以 `0xFFFF` 判"无人"并据此计人数，写 0 会让每个房间显示
  8/8（Spec SA-2，b1 静态 High）。规则只在 `SupplementalSynchronizationCodec.PvpSeatUserId`，`0x0029` 与所有 `0x002B`（大厅增量、
  欠帧补发、销毁前空座）共用。
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
- 真实 TCP 另有一例"不发 `0x012A`/`0x012B` 直接 KO"得到 `0x002E`→`0x0031`→`0x002F`→`0x0030`（SA-1 回归）。
- 实机待验（A21 客户端行为，自动化证明不了）：修复后房间人数显示（SA-2）与 KO 结算（SA-1）；`0x0037` 实际长度；`0x0039` 后靠 `0x002A` 解除结果等待是否足够；
  欠帧补发合成一帧 8 座位且不带 USER_LEAVE 是否足够；团队模式 `0x002F` 胜方座位；选角 `0x0030` 的位置；建房成功无 CMD 回包；
  `0x0034`／`0x0036`／`0x003B` 收到拒绝 CMD 的表现；房内 USERINFO1 的 routing uid；决斗邀请 NOTI `0x0007` type 2 与拒绝体；
  换模式／换队不清准备标记。逐条状态以协议页为准。
- 房间销毁时若多人同时被送回城，被送回者彼此收不到对方的 USER_STATE 0；当前领域每次变化只空出一个座位，此情形不出现。
