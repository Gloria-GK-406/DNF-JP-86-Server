# 决斗场（PvP）实现方案（只出方案，未改代码）

基线：`main@ef20398a3`（0.3.2）。证据标签按 `protocol/evidence.md`：**70Reference / 上游(第5级) / ClientStatic / CurrentImplementation / Unknown**；本文任何结论都还**没有 A21 实机（ClientVerified）**。

## 0. 结论速览

- **战斗本身是客户端 P2P**：伤害、HP 服务端都不参与（上游、70 一致）。服务端负责：频道/大厅/房间状态、开局、收死亡与超时上报并**由服务端判胜负**、两段确认结算、断线判负、持久化战绩。
- **本仓已有基础**：PvP 频道（环境 8/13/24）识别、决斗城镇 town 10 出生、PvP 独立技能档、选角进大厅的名册 0x0002 + 房间快照 0x0029 编码、`PvpLobbyDomainService`（房间 `Offer` 目前**无生产调用方**）、`character_pvp` 表。
- **最大的协议风险**：客户端发 `0x0039` 后要等某条 NOTI 才从 PREPAREWAIT 回 WAIT（vt+0xF0 @`0x026FE450`），不确定是哪条；上游推断是 `0x002A`。这条错了会卡死结算后的房间。
- **建议分三期**：一期 = 自由房 1v1/团队对战 + 结算（只频道 24 记战绩）；二期 = 接力/死斗/观战/胜点/战绩详情等；三期 = 匹配/段位排名/赛季/斗兽场/总力战（对应 #82–#85）。
- **P2P 依赖**：本机双客户端可直接测；跨 NAT 线上对战依赖 #298（UDP 中继）。

---

## 1. 参照调研

### 1.1 70 官方服务端（`Research/1995chen-dnf/other/等级补丁/70/df_game_r`，按函数分块反汇编）

| 功能 | 70 opcode | 关键函数（地址） | 置信度 |
|---|---|---|---|
| PvP 频道信息 | 198 | `Dispatcher_PvPChannelInfo` 0x082178c0 | Confirmed |
| 房间列表 | 无请求，进世界推送 | `CGameManager::SendPvpList` 0x08296642 | Confirmed |
| 建房 | 53 | `MakePVPRoom` 0x081f793c、`PvP_Room::create_room` 0x085d6908 | Confirmed |
| 进房 | 54 | `EnterPVPRoom` 0x081ed09a、`check_joinable`、`join_room`、`CheckPassword` | Confirmed |
| 快速加入 | 101 | `FindQuickJoinablePvPRoom` 0x082a3202 | Confirmed |
| 座位/踢人/离座 | 55 | `OnSetPVPSeatState` 0x085dd070、`WalkOut` 0x085dd4a4 | Confirmed |
| 准备/开局 | 56 | `set_ready_state`、`check_start_state`、`check_team_pvp`、`start_pvp` 0x085d8c3a | Confirmed |
| 死亡上报 | 58 | `DisPatcher_DiePvP` → `PvP_Room::die_charac` 0x085d9386 → `check_winner` 0x085d96ec | Confirmed |
| 超时上报 | 59 | `PvP_Room::time_out` 0x085d9b0e | Confirmed |
| 赛后统计 | 61 | `SavePvpRank`、`check_recv_pvp_rank`（10s 未报踢出） | High |
| 结果确认 | 60 | `end_pvp_result`、`battle_reset` | Confirmed |
| 心跳 | 113 | `CDisconnectDetecter`，30s 无心跳强制断线 | High |
| 接力出战 | 115 | `PvP_Room::request_fight`（不是城镇邀请） | Confirmed |

要点：
- **胜负服务端判**：个人 = 剩一名存活；团队/接力 = 只剩一队存活；死斗按击杀/死亡计数。死亡包只校验房间在战斗中、死者存活，**不校验击杀真实性**。
- **超时**：客户端上报 `int32[8]`（推测为承受伤害），个人取最小、团队按队求和取最小，全等判平；服务端算已过时间**只记日志不拒收**；时限来自 `pvp_ref.etc`（普通 180s、死斗 600s、接力 120s）。**服务端不主动结束对局**。
- **结算**：类 Elo 期望值矩阵，开局时预算；Δ经验 = M×1000（修正频道 ×1200/×400）；扣分有段位 ≤9 不扣、×0.9、按段位 50–90% 修正、段位 10–15 降段保护；胜点（win_point）按名次给，是商城货币；**没有独立决斗币**。
- **中途离开/断线一律判负**，扣开局时预算的最坏分数，再对剩余人 `check_winner`。
- **没有投降协议、没有城镇 1v1 邀请**。
- 持久化：`taiwan_cain.pvp_result`（win/lose/pvp_point/pvp_grade/win_point/各项 avg 统计）、`pvp_grade_ref`、`fair_pvp_score`（连胜在这里）。
- 注意 70 段位体系是 0–30，**A21 客户端是 0–20**（上游文档第 4 节），70 的公式常量不能直接照搬。

### 1.2 上游 ServerS4A21 / 第三方 v3.3

- 上游已完整实现房间制自由决斗：`Game/Pvp/FreeDuelRoom.cs`、`FreeDuelRoomRegistry.cs`、`PvpMatchSettlement.cs`、`SqlitePvpRecordRepository.cs`、`Network/Handlers/PvpRoomHandler.cs`（5312 行）、`PvpChannelInfoHandler.cs`、`Parsers/Pvp/*`、`Builders/Pvp/*`；业务说明 `ServerS4A21/Docs/决斗场业务实现.md`（S→C 布局附客户端 reader 地址，可作 High 线索）。
- 房间：8 座位；seatState 0 个人 / 1、2 队伍 / 3、4 观战 / 0xFE 关 / 0xFF 空；房间 1 等待 / 2 战斗；结算阶段 0–3；`(room_generation, match_generation)` 防重复结算；上限 600 房。
- battleMode：1 个人、2 团队（默认）、3 接力、4/5 未确认（70 说是死斗个人/团队）、6 练习房（开局被吞，未实现）。
- 结算：RANK(0x31) → 全员 0x3A 或 5s 超时 → 写库 → 逐人 END_PVP(0x2F)+PVP_RECORD(0x30) → 全员 0x39 或 5s → 房间回等待发 0x2A。
- 规则：**只有频道 24、模式 1–3 记战绩和经验**；经验来自 A21 PVF `etc/pvp_ref.etc` 的 `[pvp grade point]`（每段胜/负经验），每游戏日胜 50 / 负 10 场上限（客户端文本 72726），段位按经验阈值表；记连胜。胜点、奖励道具、ace/全杀都写 0。
- 已知缺口：PVP_TIME_OUT 忽略、战斗无超时、`TryForceCombatAbandonment` 死代码、结算后 0x2A 只发给房间成员（大厅可能看不到房间回等待）。
- 第三方新增：接力服务端选人与 roll、匹配队列、斗兽场门票、城镇 PK（StreetAssault，peerType=4）、后台改战绩、定级场计数。只参照行为，不拷代码。

### 1.3 客户端（A21）与本仓协议资料

- `protocol/catalog.md` 里 PvP **没有任何人工进度**，协议页人工区都空。名称来自上游枚举，不是客户端字符串表。
- 客户端 PvP 状态机已逆向（`DNF-JP-86-ClientHack/Docs/a21-units/unit-007.md`，`CNPVPModule`）：17 态，LOADING → WAIT → PREPARE_BATTLE →（普通）COUNT → BATTLE → RESULT → PREPAREWAIT → WAIT。倒计时 5000ms，客户端在 LOADING 发 0x012A、UDP 连通后发 0x012B、每 10s 发 0x006E、RESULT 淡出后发 0x0039。
- 纠正旧盘点：NOTI 0x0070/0x0071 是**接力**的换人/出战请求，不是决斗邀请；决斗邀请走 000A/000B/NOTI 0007 的 **type=2**（只能邀请进自己的房间）。`Game/Joust` 是竞猜下注活动，与决斗场无关。
- 地址换算：上游文档 / ClientHack 地址 = 本仓协议页地址 + 0x400000。

---

## 2. 协议清单（一期）

布局主要来自上游实现与文档，需逐条实机核对后写入协议页并更新 catalog。

| 方向 | Type | 名称 | 布局（上游） | 本仓现状 |
|---|---|---|---|---|
| C→S | 0x00C3 | PVP_CHANNEL_INFO | 空；成功回 `u8 1, i32 0, u8 0`，失败 `00 00` | 未实现 |
| S→C | 0x0002 st0 | 大厅名册 / 单条发布 | 已有 | 已实现，未实机 |
| S→C | 0x0029 | PVP_ROOM_INFO | `u16 n` + 每房（ID、名称、状态、房主座位、地图、模式、8×座位、有密码、匹配类型） | 编码器已有 |
| C→S | 0x0032 | MAKE_PVP_ROOM | `u8 nameType [Dstr≤29] i16 map u8 hasPwd [Dstr≤8] u8 special`；成功不回 CMD（存疑） | 无 |
| C→S | 0x0033 | ENTER_PVP_ROOM | `u16 roomId u8 hasPwd [Dstr]`；成功回 `01 + 8×ready` | 无 |
| C→S | 0x0034 | SET_PVP_SEAT_STATE | `u8 seat u8 state`（踢人/退房/关开位/换队/观战复用） | 无 |
| C→S | 0x0035 | SET_PVP_READY_STATE | `u8 ready`；房主 ready 即开局 | 无 |
| C→S | 0x0036 | SET_PVP_TEAM_MODE | `u8 mode` | 无 |
| C→S | 0x003B | SET_PVP_MAP_INDEX | `i16`，`pvpmapbasicdata.etc [pvp map order]` 下标，0=随机 | 无 |
| S→C | 0x002A / 2B / 2C | ROOM_STATE / SEAT_STATE / READY_STATE | 11B / `u16 u8 u8 n + n×4` / 2B | 无 |
| S→C | 0x000B | 房内 peer 端点 | `u8 n + n×22B` | 组队已 ClientVerified，可复用 |
| C→S / S→C | 0x000A / 0x000B / NOTI 0x0007 | 邀请进房（type 2） | 7B；拒绝不发包 | 组队路径对 type≠0 直接忽略 |
| S→C | 0x002D | START_PVP | `u8 map u8 mode` | 无 |
| C→S | 0x012A / 0x012B / 0x006E | 加载完成 / P2P 连通 / 心跳 | 0B / `u8 n + n×2` / 0B | 无 |
| C→S | 0x0037 | DIE_PVP_CHARACTER | **冲突**：上游 2B 或 5B；70 为 `u16 killer`（发送者=死者）；静态走图 ≥7B | 无 |
| S→C | 0x002E | DIE_PVP_CHARACTER | `u8 死者座位 u8 击杀者座位(0xFF)` | 无 |
| C→S | 0x0038 | PVP_TIME_OUT | 32B（8×i32），70 有判定语义 | 无 |
| S→C / C→S | 0x0031 / 0x003A | REQ_PVP_RANK / RES_PVP_RANK | 空 / 70B 统计 | 无 |
| S→C | 0x002F | END_PVP | 26+19n B（n = 开局冻结名单） | 无 |
| S→C | 0x0030 | PVP_RECORD | 51B（房间上下文 +3×26B）；**选角时也要发**，否则经验条 0/0 | 无 |
| C→S | 0x0039 | END_PVP_RESULT | 空；之后发 0x002A 回等待（推断） | 无 |

二期再接：0x0062 快速加入、0x0070/NOTI 0x0070/0x0071 接力、0x0177/0x0176 战绩查询、NOTI 0x0039/C 0x0042 周经验、0x0271 每日胜场、0x0143/0x0158 任务（**编号冲突待查**）、0x016F 再战、0x0078 复活、0x0265 断线通知。WARROOM、控偶、总力战、匹配、PvP 好友、锦标赛、延迟惩罚各族归三期或不做。

---

## 3. 领域设计（符合 PacketHandler → CMD → 领域服务 → IEventService → 唯一桥 → INotificationBus）

### 3.1 新领域目录 `Pvp/`

`Abstractions/Pvp/` 放契约，`Application/Pvp/` 放业务与规则，`Persistence/Pvp/` 放存储，内容源在 `Infrastructure/Content`。领域层不出现任何 opcode 和字节布局。

| 类型 | 职责 | 状态所有权 |
|---|---|---|
| `IPvpRoomDomainService` | 房间聚合：建、进、离、踢、座位、换队、改模式/地图、准备、开局、死亡、超时上报、结算阶段推进、断线判负。返回具名结果（`PvpRoomOutcome` 封闭层级），不返回字节 | **房间运行态唯一 owner**（内存，带 `RoomGeneration`/`MatchGeneration`） |
| `IPvpLobbyDomainService`（已有） | 只保留"谁在大厅就绪"。现有 `Offer`/`Withdraw`/`OfferedOn` **迁到房间服务并删除**，避免房间事实两份 | 大厅就绪者 |
| `IPvpRoomRuleService` + 静态 `PvpRoomRules` | 座位状态转移合法性、模式→分队、两队人数平衡、终局判定（存活/超时数值）、地图选择 | 无状态，读 `pvpmapbasicdata.etc` |
| `PvpBattleModeCapabilities`（能力表，同 `DungeonRunCapabilities` 写法） | 每个 battleMode 一行：`Supported`、`TeamAssignment`、`RequiresBalancedTeams`、`TerminalRule`、`Settles`。调用方只问能力，不写 `if (mode == 2)`；switch 列全成员、不写 default | 静态表 |
| `PvpChannelEntitlements`（以频道环境为键的权益表） | 频道身份 → 权益：`RecordsResult`、`AwardsGradeExperience`、`AwardsWinPoint`、`DailyCap`、`AllowsRoomCreation`。环境 8/13/24 各一行，未知环境启动失败。**不写 `isRanked` bool / `if (env == 24)`** | 规则配置，经 `IPvpSettlementRuleService` 读 |
| `IPvpSettlementRuleService` + 静态 `PvpGradeRules` | 读 A21 PVF `etc/pvp_ref.etc`（阈值、每段胜负经验、满级段、loss 表），输入 facts（参战名单、段位、胜负、今日计数）→ 每人 `PvpSettlementDecision` | 无状态 |
| `IPvpRecordDomainStore` | 战绩事务真源：`character_pvp_record`、`pvp_matches`、`pvp_match_results`；以 `(room_generation, match_generation)` 幂等 | SQLite，事务内 |
| `PvpDeadlineSource` | 邀请 30s、加载超时、心跳 30s、RANK/END 确认超时、对战兜底时限。照 `DungeonDeadlineScheduler` 的"拉取 + 状态所有者唤醒"写法；建议新增独立 `PvpDeadlineScheduler` 而不是把 PvP 塞进 `DungeonDeadlineKind`（后者语义是一局副本） | 截止时间只在房间状态里 |

现有 `character_pvp` 中 `pvp_stat_a / pvp_rank_point / pvp_win_streak` 是旧名、语义不清（上游文档已提醒）。建议一期迁移时改成语义明确的列并入 `character_pvp_record`，`pvp_grade`/`pvp_rating_grade` 保留单一来源，USERINFO 与 PVP_RECORD 都从它读。

### 3.2 事件与通知

- `PvpRoomChanged`（房间列表/状态/座位变化）：CMD 内 `ICommandProjections.ProjectAsync` 即时投影。受众：房间成员用精确会话人群 `TheseSessions`；大厅用现有"PvP 大厅公示"人群（同监听端口就绪者）。领域服务不查连接。
- `PvpMatchSettled`：`EnqueueAsync` 与写库同事务，Reliable 订阅留给二期的任务、成就、胜点、排名。一期 END_PVP/PVP_RECORD 仍由结算 CMD 即时投影，保证 0x2F→0x30→等 0x39 的顺序。
- 断线：`lifecycle:disconnected` 作业 → 房间服务 `Depart(reason: Disconnected)` → 若在战斗中按"阵亡、无击杀者"处理并可能触发终局；**离场者 UID 的座位变动推迟到结算界面退出后再下发**（上游 `deferCombatDeparture` 的教训：客户端结算时还要引用离场者 UID）。

### 3.3 协议切片（`Dfo.Server.Protocol/Game/`）

| 切片 | 消息 | CMD |
|---|---|---|
| `PvpChannelInfo/` | C 0x00C3 | `GamePvpChannelInfoCommand` |
| `PvpRoom/` | C 0x0032–0x0036、0x003B；NOTI 0x0029、0x002A–0x002C、房内 0x000B | 建/进/座位/准备/模式/地图各一个 CMD |
| `PvpBattle/` | C 0x012A、0x012B、0x006E、0x0037、0x0038、0x003A、0x0039；NOTI 0x002D、0x002E、0x0031、0x002F、0x0030 | 加载、心跳、死亡、超时、排名确认、结果确认 |
| `Party/`（已有）REQUEST_PEER | type 2 分支 → `GamePvpRoomInviteCommand` | 解码器共用，薄 handler 按 type 选 CMD |
| `SelectCharacter/`（已有） | 选角时补发 PVP_RECORD 0x0030 | 在现有 PvP 发布阶段登记 |

接线只改 `ProtocolModule`；`NetworkMessages.cs` 只加消息目录成员。

### 3.4 与现有系统的交互

- **频道/选角**：复用 `ChannelEntryDomainService` 的 FreeDuel/Pvp 分类和 `SelectCharacterLifecycleServices.PlanPvpAsync`。
- **位置**：进房时发 USER_AREA area=0xFE、USER_STATE=2；`OnlineCharacterLocationDomainService` 保留离开时的城镇落点，退房按"回原区域 → USER_STATE 0 → 座位 → 房间重置"顺序恢复。房间不是城镇区域，不给它造区域记录。
- **组队**：PvP 频道里有队伍时的行为见待拍板第 11 条。
- **地下城**：PvP 频道只能在 town 10，不需要 `DungeonRunStore`；只借鉴它的截止时间写法。
- **P2P 端点**：复用 C 0x0002 / NOTI 0x000B（组队已 ClientVerified），房内按接收者分别构造。
- **技能**：已有 PvP 独立技能档。

---

## 4. 数据模型与迁移

schema 版本 +1（以 `DatabaseSchemaMigration.md` 为准，`Tool/DbMigrate` 迁移旧库）：

- `character_pvp_record(character_id PK, win, lose, draw, grade_experience, grade, rating_grade, win_streak, peak_win_streak, win_point, updated_at)`：替换 `character_pvp` 的旧列，迁移时搬 `pvp_grade/pvp_rating_grade`，旧列有值则记入迁移报告。
- `pvp_matches(room_generation, match_generation, listener_port, channel_env, battle_mode, winner_seat, game_day, completed_at)`，PK 为两个 generation。
- `pvp_match_results(room_generation, match_generation, character_id, seat, team, outcome, kills, deaths, previous_grade, grade, experience_change, …)`：用于每日计数、连胜和以后的战绩详情。
- 一期不存 70 的 avg_* 统计（0x003A 只当确认），二期战绩详情再决定。
- 房间不落库（重启房间消失，进行中对局不结算，见待拍板第 8 条）。

---

## 5. 分期

**一期：最小可玩（自由房 1v1 / 团队 + 结算）**
1. 0x00C3 频道信息；大厅已有，补实机核对。
2. 建房（含密码）/ 进房 / 退房 / 踢人 / 关开位 / 换队 / 改模式（只开放 1、2，其余能力表 `Supported=false` 拒绝）/ 改地图 / 准备 / 开局。
3. 邀请进房 type 2。
4. 对战：加载、P2P 连通、心跳、死亡判定、超时上报判定、对战兜底时限。
5. 两段确认结算；频道 24 记胜负、经验、段位、连胜，频道 8/13 不记；选角补发 PVP_RECORD。
6. 断线 / 中途离开判负；心跳超时。
7. 协议页与 catalog 更新、任务文档 `Document/tasks/pvp-duel/`。

**二期**：接力（3）、死斗（4/5）、观战座位 3/4、快速加入、练习房（6）、胜点与商城、战绩详情 0x0177/0x0176、周经验/每日胜场、PvP 任务（先解决 0x0143/0x0158 编号冲突）、再战、结算后大厅可见性修正。

**三期**：匹配队列与排位/赛季（#83）、公平决斗数值投影（#82）、斗兽场（#84，先查 A21 日服是否开放）、总力战（#85）。城镇 1v1 PK（StreetAssault）70 没有，只有第三方有，如要做单独立项。

---

## 6. 测试策略（可失败的验收证据）

- **规则单测**：终局判定（个人剩一人、团队剩一队、全灭平局、超时数值最小/全等平局）；分队与人数平衡；座位转移合法性（非房主踢人、只剩一人时保留空位）；经验与段位计算对真实 A21 PVF `pvp_ref.etc` 跑表（不 mock 内容）。
- **能力表 / 权益表守护**：switch 不允许 default，新增模式或频道环境会编译失败；未知频道环境启动失败；各带反例测试。
- **领域测试**：并发入房抢最后一个座位、重复死亡上报、结算幂等（同 generation 二次结算回读）、断线发生在 0x31 之后 0x2F 之前。
- **编码字节基线**：每个 NOTI 一个字节级 golden 测试，长度对上 reader 消费链（0x002F = 26+19n、0x0030 = 51B）。
- **集成测试**：拉起 Host，两个假 TCP 客户端走 建房 → 进房 → 准备 → 开局 → 死亡 → 0x3A → 0x39，断言帧顺序与房间回等待；再跑断线判负路径。
- **实机验收**（A21-protocol + protocol2 双客户端，本机测试服，当前主干库副本）：每步截图 + ClientHack 抓包保存在 `Document/tasks/pvp-duel/`。**关键可失败检查**：结算后客户端能否回到 WAIT（卡在 PREPAREWAIT 即失败）；经验条是否非 0/0；重登后胜负场与段位一致；拔线判负后对方能正常结算。

---

## 7. 风险与未知点（需实机取证或小范围反汇编）

1. **0x0039 之后由哪条 NOTI 解除 PREPAREWAIT**（vt+0xF0 @`0x026FE450`），上游推断 0x002A。只反汇编这一个调用点的上游函数即可。
2. **MAKE_PVP_ROOM / SET_PVP_READY_STATE 成功时要不要 CMD 回包**：客户端有回包 handler `0x00D23E60` / `0x00D0CCE0`，上游建房成功不回。
3. **C 0x0037 长度**：静态 ≥7B、上游 2/5B、70 为 `u16 killer` 三方不一致，必须抓包。
4. **C 0x0038 语义**：70 为每座位数值、最小者胜；上游忽略。需确认 A21 数值含义。
5. NOTI 0x0070 组宽（5B vs 2B），二期接力前核。
6. 0x00C3 成功体后 5 字节含义。
7. 0x0143 / 0x0158 PvP 任务编号冲突。
8. 0x0031、0x013D、0x0265 结构未知。
9. **段位体系**：A21 是 0–20 + rating 0–9（显示 +20），与 70 的 0–30 不同；结算公式选型见待拍板第 3 条。
10. 结算后大厅是否能看到房间回等待（上游只发给房间成员）。
11. 跨 NAT 对战依赖 #298。
12. 密码、观战、模式 4/5/6 均无 A21 样本。

---

## 8. 需用户拍板的规则（附建议值）

| # | 规则 | 建议值 | 理由 |
|---|---|---|---|
| 1 | 一期开放的模式 | 1 个人、2 团队；3/4/5/6 二期 | 上游与 70 对 1/2 理解一致；3 需服务端选人，4/5 语义未确认 |
| 2 | 哪些频道记战绩 | 只频道环境 24 记胜负、经验、段位；8/13 不记 | 与客户端频道定义（24 传统排位、13 练习、8 自由房）和上游一致 |
| 3 | 经验 / 段位算法 | 用 A21 PVF `pvp_ref.etc [pvp grade point]` 每段固定胜/负经验（上游做法），不移植 70 的 Elo 矩阵 | A21 段位刻度和 70 不同，PVF 自带每段数值，是 A21 内容的真源；Elo 常量无 A21 依据 |
| 4 | 每日上限 | 胜 50 场 / 负 10 场后经验变化为 0，胜负场照记 | 客户端文本 72726 有此提示，上游同 |
| 5 | 降段 | 允许降段；经验下限 0；满级段不再加减 | 70 的 10–15 段保护基于 0–30 刻度，A21 无对应依据；如需保护再定 |
| 6 | 中途离开 / 断线 | 战斗中离开或断线判负并按负场扣经验；对方是否胜按剩余存活判 | 70 与上游一致，防止逃跑刷分 |
| 7 | 心跳超时 | 30s 无 0x006E 视为断线，按第 6 条判负 | 70 `CDisconnectDetecter` 为 30s |
| 8 | 对战时限 | 以客户端 0x0038 上报判定；服务端兜底 = PVF 时限（普通 180s）+30s 仍未结束则判平局、不加减经验 | 70 不主动结束会留下僵尸房；判平最不伤人 |
| 9 | 结算确认超时 | RANK、END 各 10s，超时按已到达处理，未回者不踢 | 70 是 10s，上游 5s 偏紧 |
| 10 | 奖励 | 一期只有胜负场、经验、段位、连胜；不发道具、胜点、决斗币 | 70 无决斗币；胜点牵涉商城，归二期 |
| 11 | 组队中建房 / 入房 | 拒绝并提示先退队 | 70 是自动退队并邀请队友，牵涉组队状态，一期不碰 |
| 12 | 装备差异 | 允许，不做公平化 | 公平决斗是 #82 的范围 |
| 13 | 密码房 | 一期支持（最长 8 字节） | 布局已知、成本低 |
| 14 | 观战 | 一期不开放座位 3/4（拒绝），二期实机验证后开放 | 无 A21 样本，避免状态机未知 |
| 15 | 服务器重启时进行中对局 | 房间丢弃、不结算、不扣分 | 房间不落库，判负对玩家不公平 |
| 16 | 每日对战次数限制 | 不限 | 70 / 上游都没有次数限制，只有经验上限 |

---

## 9. 关键参照文件

- 上游：`ServerS4A21/Server/DfoServer/Game/Pvp/*`、`Network/Handlers/PvpRoomHandler.cs`、`PvpChannelInfoHandler.cs`、`Network/Parsers/Pvp/*`、`Builders/Pvp/*`、`Docs/决斗场业务实现.md`、`Sqlite/SqliteMigrations.cs:338–412`。
- 第三方：`Research/third-party-s4a21-v3.3/decompiled/DfoServer/…/PvpRoomHandler.cs`（3054–3743 匹配），`StreetAssaultHandler.cs`。
- 70：`Research/1995chen-dnf/other/等级补丁/70/df_game_r`（函数地址见 1.1）、`init_sql.tgz` 的 `taiwan_cain.pvp_result`、70 PVF `etc/pvp_ref.etc`、`pvpchannellist.etc`、`serverparameter.etc`。
- 客户端：`DNF-JP-86-ClientHack/Docs/a21-units/unit-007.md`（PvP 状态机）、本仓 `protocol/game/client-to-server/0x00{32..3B}.md`、`server-to-client/NOTI-0x00{29..31}.md`、`NOTI-0x000B.md`、`0x000A.md`。
- 本仓：`Application/Sessions/PvpLobbyDomainService.cs`、`SelectCharacterLifecycleServices.cs:408-495`、`StateSynchronization/SupplementalSynchronizationCodec.cs:94-235`、`Application/Channel/ChannelEntryDomainService.cs:99-111`、`Persistence/Schema/CharacterDatabaseSchema.cs:427`。

Generated by Claude Code
