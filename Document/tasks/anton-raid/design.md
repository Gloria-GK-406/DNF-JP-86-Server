# 安图恩攻坚服务设计

2026-11-19。A21 安图恩攻坚团本的服务端方案，覆盖领域服务、状态字段、与现有组队、副本、库存、任务的接缝，以及服务端与客户端之间的协议往来。小队容器与 `PartyService` 的边界见 [安图恩团本领域设计](../../AntonRaidDesign.md)，本文不重复。

本文是 JP86 的实现前方案。客户端指令可确认部分字段和本地分支，帧序及 UI 效果待实机。依据见 `research/`。

| 文件 | 内容 |
| --- | --- |
| [01-upstream-recruitment.md](research/01-upstream-recruitment.md) | 上游招募、编队、开团准备、离团、重入、断线 |
| [02-upstream-progression.md](research/02-upstream-progression.md) | 上游攻坚进行中：地图、触发器、计时、阶段、失败 |
| [03-upstream-rewards.md](research/03-upstream-rewards.md) | 上游门票、结算、翻牌、任务、持久化 |
| [04-jp86-protocol-client-pvf.md](research/04-jp86-protocol-client-pvf.md) | 本仓协议页、客户端静态证据、PVF 内容 |

来源标签沿用 [证据规则](../../../protocol/evidence.md)：`ClientStatic`、`PvfContent`、`UpstreamStatic`、`ThirdPartyStatic`、`Unknown`。静态结论绑定 [A21 原版 exe、分析镜像和 dev PVF](research/04-jp86-protocol-client-pvf.md#0-绑定版本与标签)。本文引用上游帧序作取证参照，目前暂无 A21 团本抓包或实机记录。

## 1. 结论先行

1. **攻坚实例全在内存。** 和 `DungeonRun` 一样，进程重启即失效。落库的只有门票扣除、奖励入账和任务进度，三者都带幂等键。
2. **团员身份与连接解耦。** 掉线保留 300 秒（PVF `[DISCONN MEMBER WAIT TIME]`），期间可 `REJOIN_RAID` 回到团内。
3. **每支出战小队就是一局普通副本。** 新增玩法种类 `AntonRaid`，入场由攻坚实例裁决。攻坚只消费五种局内回报：进图、通关、死亡、复活、离图。
4. **阶段机制由 PVF 驱动。** `etc/raid/anton.etc` 每阶段是一套词汇封闭的「条件 → 动作」触发脚本，做成规则服务加纯规则解释器。地图状态、符号、计时都是它的状态。手写的只有脚本之外的部分：通关阈值、孵化随机与感染、屏障回复、结算。
5. **开团准备需要接住 A21 发出的普通组队命令。** `PREPARE_START_RAID` 的静态路径能发单成员组建队、多成员组非首位成员主动应答。多人首位成员怎样建队、是否另有邀请待实机。攻坚实例按冻结编组裁决并通过租借句柄写入小队容器，见 §4.3。
6. **所有局内到点推进走现有的 `DungeonDeadlineScheduler`。** 新增一个来源 `AntonRaid`，截止时间存在攻坚实例里。这是第一个状态所有者不是某一局的来源。
7. **布局以客户端读法为准。** 当前上游（`1870c85`）的编码器与客户端读到的宽度一致。但本仓协议页的「可能含义」引用了旧版上游，多处过时或错位（见 §8.4），实现编码器时不能照抄页面含义。

## 2. 组件与职责

对外只有一个攻坚领域服务。从建团到收尾，团员、小队、阶段、地图、计时、结算都是同一个攻坚实例的状态，彼此参与同一组不变量。按 [架构与分层](../../Architecture.md#接口类别与代码形状) 的判据，它们属于一个聚合，由一个领域服务对外。

```
Protocol/Game/AntonRaid/*           薄 handler → 协议 CMD → IAntonRaidDomainService；攻坚投影（NOTI 组装与受众）
Abstractions/Raids/
  IAntonRaidDomainService           对外唯一的攻坚领域服务
Application/Raids/
  AntonRaidDomainService            实现，持有实例注册表：实例、团号、角色→团索引、频道目录
    AntonRaidInstance               聚合根，字段见 §3，方法守住实例内的不变量
    招募、准备、攻坚、出战、结算、在场    按用例分的 partial 文件，只是代码组织，不注册、不对外
  AntonRaidRuleService              规则服务：读 PVF raid 配置的唯一入口，返回不可变的 AntonRaidDefinition
  AntonRaidTriggerRules（静态）      纯规则：触发器解释器
  AntonRaidSettlementRules（静态）   纯规则：名次、奖池抽取、牌位分配
  接入适配                          现有扩展点的实现，只把事件转给攻坚领域服务，自身没有状态：
    AntonRaidDeadlineSource           DungeonDeadlineKind.AntonRaid 的来源
    AntonRaidBossClearHook            IDungeonBossClearHook（Kind = AntonRaid）
    AntonRaidEntryRoute               入场路由中的攻坚一项
    出战离场订阅、会话清理中的攻坚一步
Infrastructure/Content/
  PvfRaidDefinitionSource           解析 etc/raid/raid.lst、anton.etc、.buff、.symbol
```

`AntonRaidRuleService` 和两组纯规则单独存在，是分层的要求：业务服务问规则服务「这一阶段的地图表、触发器、计时、奖池是什么」，自己不读 PVF（[领域与规则](../../Architecture.md#领域与规则)）。接入适配是现有机制规定的形状，例如截止时间调度器要一个来源、首领房清场要一个钩子。它们不持有攻坚状态，所以不算领域服务。

本仓的 `DungeonRunStore` 是一个存储加 8 个按用例拆开的领域服务接口（`World/DungeonRunRegistration.cs`）。那是因为战斗、拾取、翻牌、计时等由许多不同的 CMD 分别注入，按用例收窄依赖面。攻坚只由自己的协议切片和上面几处适配调用，用一个入口更清楚，这里有意不照搬。

所有方法都在唯一执行器里运行，不加锁（[单一执行器](../../GameplayExecution.md#单一执行器与生产者)）。每个写方法返回具名结果，带上本次变化要发的投影计划（类似组队的 `PartyNotificationPlan`）。协议层按计划组装 NOTI 和受众，领域服务不碰连接。

### 2.1 对外方法

| 分组 | 方法 | 调用方 |
| --- | --- | --- |
| 招募 | `Create`、`RequestJoin`、`Invite`、`Respond`、`Leave`（含踢人）、`AssignSquad`、`TransferLeadership`、`ModifyTitle`、`SetWaiting` | 攻坚协议 CMD |
| 小队 | `HandlePartyRequest`：团员发出的普通组队请求，见 §4.3 | 组队协议 CMD 的分流 |
| 开团 | `Start`：一阶段开团与二阶段开打共用 | 攻坚协议 CMD |
| 出战 | `AdmitEntry`、`SortieEntered`、`SortieCleared`、`MemberDied`、`MemberRevived`、`SortieLeft` | 入场路由、清场钩子、离场订阅 |
| 机制 | `DoBehavior`、`SetSymbol`、`CastBuff`、`ReportMonsterHp` | 攻坚协议 CMD |
| 到点 | `NextDeadline`、`AdvanceDeadline(InstanceId, Key, Version)` | 截止时间来源与内部命令 |
| 结算 | `MovieFinished`、`SelectCard` | 攻坚协议 CMD |
| 在场 | `MemberDisconnected`、`Rejoin` | 会话清理、攻坚协议 CMD |
| 查询 | `FindByCharacter`、`Directory(channel)`、`Snapshot(InstanceId)` | 投影、重入补发、目录 |

## 3. 领域模型

### 3.1 攻坚实例 `RaidInstance`

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `InstanceId` | `RaidInstanceId`（单调 long） | 每次建团唯一，所有回调、计时、待定请求都核对它 |
| `RaidNumber` | u32 | 线上团号。`0x0250` 有对 `RaidNumber >> 16` 与本地频道值的比较，`0x0328` 从 bits 16–23 取比较值。JP86 分配团号时须维持高位的频道兼容性，低位分配不得进位破坏高位。上游合成 `cid` 的写法仅供参照。 |
| `ChannelId` | int | 所在团本频道 |
| `Title` | `RaidTitle`（原样字节） | 只有团长能改 |
| `LeaderCharacterId` | `CharacterId` | 团长 |
| `LeadershipVersion` | long | 移交团长时加 1，旧审批随之失效 |
| `Members` | 有序 `RaidMember` 列表，≤20 | 顺序有业务含义：同一编组内第一人是小队队长（`UpstreamStatic`） |
| `AssignmentVersion` | long | 成员增减、编组变化、移交时加 1，用于改编的乐观校验 |
| `Squads` | 按启用编组索引的 `RaidSquad` | A21 可表达 A–J，实际启用范围见 §5.1 |
| `Lifecycle` | `RaidLifecycle`（封闭记录集合） | 见 §3.5 |
| `Phase` | `RaidPhaseState?` | 攻坚中与阶段结算时有值 |
| `Settlement` | `RaidSettlement?` | 阶段结算时有值 |
| `Deadlines` | `RaidDeadlineBook` | 本实例全部截止时间，见 §3.7 |
| `EntryCostLedger` | 已付门票的角色集合 | 二阶段中途加入者补扣用 |

### 3.2 团员 `RaidMember`

| 字段 | 含义 |
| --- | --- |
| `CharacterId`、`AccountId`、`WireUserId` | 身份。同账号不能有两个角色在同一团（`UpstreamStatic`） |
| `Profile` | 名字、职业、成长类型，入团时冻结的显示快照 |
| `SquadIndex` | 0 = 未编队（参照规则）；A21 可构包 1–10。实际启用范围见 §5.1 |
| `Presence` | `Online(SessionGeneration)` 或 `Disconnected(Since, Deadline, Generation)` |
| `PhaseClears` | 本阶段个人通关次数（成员表显示与评级用） |

### 3.3 小队 `RaidSquad`

| 字段 | 含义 |
| --- | --- |
| `Index` | 已启用的 A21 编组序号 |
| `PartyKey` | 建团时借出的容器 key，整个实例生命周期不变 |
| `Formed` | 容器成员与编组一致且有队长；开团准备结束时校验 |
| `CoinAllowance` | 本阶段复活币额度（buff `INCREASE COIN` 可增加），用量见出战记录 |

容器的成员与队长就是小队名单的唯一事实。`RaidSquad` 不另存成员，只记编组目标：哪些团员的 `SquadIndex` 等于它。

### 3.4 阶段运行态 `RaidPhaseState`

| 字段 | 含义 |
| --- | --- |
| `PhaseIndex` | 0 = 阻截安徒恩，1 = 灭杀安徒恩 |
| `StartedAt`、`AttackDeadline`、`ExtendedSeconds` | 阶段时限 2400 秒（PVF `[PHASE TIME OVER]`），buff 可延长 |
| `DeathCount` | 全团本阶段死亡数，决定名次 |
| `Maps` | `dungeonId → RaidMapState`：状态字（A21 `open=0`、`close=1`、`hide=2`、`clear=3`）、通关计数、所需次数（PVF `HP INFO`）、同图小队上限（`PARTY MAX`） |
| `Symbols` | `symbolId → int`，地图状态以外的共享机制值（屏障 110、阶段成败 104/105 等） |
| `Infection` | 当前感染孵化中心 id 或无 |
| `Barrier` | 屏障值与回复状态（二阶段） |
| `Buffs` | `buffType → 激活记录（施放者、目标、生效截止、冷却截止）` |
| `Sorties` | `squadIndex → RaidSortie`：`RunId`、图号、实际进图者、是否已通关、已用复活币 |
| `ClearParticipants` | 本阶段至少一次通关时在场的角色，奖励资格的输入 |

### 3.5 生命周期 `RaidLifecycle`

| 记录 | 线上 `RAID_STATE`（State, Arg） | 说明 |
| --- | --- | --- |
| `Recruiting` | (0, 0) | 招募 |
| `Preparing(Generation, FrozenRoster, NextPhase)` | (0, 0) 或间歇中 (5, 0) | 3 秒准备窗口，处理客户端组队命令，时长暂沿上游 |
| `Attacking(Phase)` | (2, Phase) | 攻坚中 |
| `Settling(Phase)` | (3, 0) | 阶段结算：结果、动画、翻牌、大奖 |
| `Break` | (5, 0) | 两阶段间歇 600 秒（上游实际排 595 秒） |
| `Completed` | (4, 0) | 二阶段完成，等待成员离开 |
| `Failed(Phase)` | 不发 `RAID_STATE`，改发失败 `RAID_RESULT` | 进攻超时 |

表中发送时点来自上游。A21 `0x024C` 静态支持分派 0–5，状态 2 的第二字节 0/1 选择对应阶段。状态 1 有客户端分支；是否需要在二阶段开打前发送、失败和翻牌的帧序仍待实机。见 [0x024C](../../../protocol/game/server-to-client/NOTI-0x024C.md)。

### 3.6 结算 `RaidSettlement`

| 字段 | 含义 |
| --- | --- |
| `Phase`、`Rank`、`ClearSeconds` | 结果帧内容 |
| `Eligible` | 冻结的资格名单，见 §6.2 |
| `Cards` | `(squadIndex, rewardType, slot) → 抽中结果`，同小队共享 |
| `Claims` | `characterId → 已领位图`（金币牌、道具牌、大奖） |
| `SquadPrizes` | 每个有资格者的大奖抽取结果，广播前固定 |
| `Stage` | `ResultShown → CardsOpen → PrizeShown → Granted` |

### 3.7 截止时间簿 `RaidDeadlineBook`

每项是 `(Key, Due, Version)`，`Key = (Kind, DungeonId, Purpose)`。`Kind` 取以下几类：

- 准备结束（3 秒）、进攻超时、间歇结束；
- PVF 计时：守图、关闭后重开、周期效果（协议类型的用途见 §7.3）、`RESERVE DUNGEON STATE` 延时开放；
- 孵化开启、屏障回复（1 秒）；
- 结算各段：动画提示 2 秒、自动翻牌 10 秒、大奖入账 5 秒、翻牌开启兜底（新增，见 §6.3）；
- 掉线保留到期。

同键重排就把 `Version` 加 1，到点命令带着 `(InstanceId, Key, Version)`，对不上就丢弃。上游的裸 `Task.Delay` 全部改为这里的截止时间。

## 4. 与现有领域的接缝

### 4.1 频道与城镇

- 团本频道已有：`[raid]` 行 type 23 → `ChannelEntryKind.Raid`，团本城镇 19（`CurrentImplementation`，`ChannelScript.cs:78`、`IChannelSources.cs:37`）。
- **待核：落点。** PVF 写的等待室是 town 19 area 2，NPC 327 负责开团和离开攻坚区。本仓现在落在 gate 区，要实机确认 NPC 是否可达。
- **门槛：** 任务 12829 的 dev PVF 文案把完成任务列为准入条件。JP86 拟在频道准入规则中执行；客户端是否也自行拦截，待比较完成前后的表现。
- `0x04BD RAID_USER_FINISH_LOAD` 的 A21 handler 跳到团本管理器并记录当前时钟值，路径无 body 读取。本人到场时发空 body、随后刷新团目录是参照方案，实际发送节点及首次到场／副本回城是否共用待实机。见 [通知证据](../../../protocol/game/server-to-client/NOTI-0x04BD.md)。

### 4.2 组队：小队容器

容器、共享归属索引、租借句柄、归还规则都以 [领域设计](../../AntonRaidDesign.md#小队容器与租借) 为准。本方案按资料补两点：

1. **招募时先维护编组目标。** 招募期只改 `SquadIndex` 是 JP86 的拟定做法，参考上游。实际小队在开团准备期间形成，招募阶段是否也需要队伍通知待实机。攻坚和间歇期间由团长改编的边界见领域设计。
2. **普通组队入口按角色归属分流。** 涉及团员的普通请求交给攻坚领域服务的 `HandlePartyRequest`，由它通过租借句柄更新；其余请求走 `PartyService`。`PartyService` 拒绝直接作用于借出容器的普通操作。

### 4.3 团员发出的普通组队请求

A21 的 [准备通知](../../../protocol/game/server-to-client/NOTI-0x0253.md) 按本地编组收集 UID。以下为 JP86 的接入规则。多人首位成员建队的触发及普通邀请顺序由实机确定。

| 请求 | 准备期（`Preparing`） | 攻坚中与间歇（`Attacking`、`Break`） | 其他状态 |
| --- | --- | --- | --- |
| `0x000C SET_PARTY_INFO` | 按冻结编组校验发起者；A21 的准备通知已证实单成员组能发此命令。多人组首位成员若从其他入口发送，按相同编组规则校验 | 已在小队：忽略设置变更，回成功 | 招募期团员：拒绝，团员不能组普通队 |
| `0x000A` 邀请（子类型 0） | 若收到普通邀请，只接受组首发给同组成员，是否需要转发 `0x0007` 取决于实机帧序 | 团员之间：当作编组变更交给改编规则；有一方是非团员则拒绝 | 拒绝 |
| `0x000B` 应答（子类型 0） | 非组首可能主动发 `u16 组首 UID + u8 0 + u32 0`。以活动准备代次、发送者和组首的冻结编组身份校验；接入需容纳没有显式邀请记录的这条客户端路径 | 同上 | 拒绝 |
| `0x000D` 退队 | 让准备作废 | 不在副本时移回未编队区并移出容器 | — |
| `0x000E` 踢人、`0x0079` 换队长 | 拒绝 | 只有团长能改编，按改编规则处理 | — |

首轮实机至少同时覆盖一个单成员组和一个双成员组，采集团长、组首、非组首各自的收发帧。根据结果确定多人先建容器的时点、邀请是否先于应答、成功投影和准备结束的就绪条件。服务端应能接住先到的自动应答；实际写入容器前仍须校验准备代次与最终编组。

### 4.4 副本

| 接缝 | 做法 |
| --- | --- |
| 玩法识别 | 新增 `DungeonRunKind.AntonRaid`，`DungeonRunMode.AntonRaid(InstanceId, PhaseIndex, SquadIndex)` 冻结在 layout 上 |
| 入场 | 在 `IDungeonEntryRoutingDomainService` 里排最前：团本频道的选图一律交给攻坚领域服务的 `AdmitEntry` 裁决，不落到普通判断。检查：团处于 `Attacking`；请求者所在小队就是本团的某个容器，非空且有队长；图号属于当前阶段且地图是 open；同图小队数未超过 `PARTY MAX`；该小队没有进行中的出战 |
| 能力表 | `EndsOnBossClear` 保持默认；`DrawsClearCards=false`；`UnlocksDifficulty=false`；`AllowsOrdinaryRechallenge=false`；`Revival` 新增「小队复活币额度」策略；`AppliesFailureWeakness`、`PartyWipe` 先取普通值，列入实机项 |
| 掉落 | 照上游：保留经验与任务进度，关闭怪物掉落、任务掉落、随机精英 |
| 进图回报 | 加载就绪时登记出战，名单用**实际进图者**。上游用编组名单，会把没进图的队友算成参与 |
| 通关回报 | `AntonRaidBossClearHook` 在击杀事务里记一次通关，不依赖 `0x002E` 结算时机 |
| 死亡、复活 | 死亡计入 `DeathCount`；复活币从小队额度扣 |
| 离图 | 订阅 `DungeonParticipantLeft`，整队离开时结束该出战记录 |
| 反向操作 | 一阶段失守复位（PVF `KILL ALL CHARACTER`）、阶段结束、超时失败、团收尾时，要能结束出战中的局或放行回城。复用按 `RunId` 结束整局的入口，最终语义待实机 |
| 孵化中心入口 | 按 218 局内小队传送设计。A21 战况导航排除 221–224，dev PVF 的 218 四角机关按目标图开放状态生成小队传送门。真实换图帧序、源局结束方式和回城衔接待实机。见 [传送资产与指令](research/04-jp86-protocol-client-pvf.md#218-到孵化中心的传送) |

### 4.5 库存、邮件、任务

- **门票。** 10096296 ×1，在 NPC 326 的商店 108 购买，每角色每日限购 1（`PvfContent`）。扣除点见 §6.1。
- **入账。** 在线成员直接提交背包；离线或背包提交失败的，走系统邮件，`delivery_key = raid:{InstanceId}:{phase}:{cid}:{kind}`。
- **任务。** `QuestObjectiveAuthority` 目前把 `raid phase clear` 列为客户端计数。JP86 拟由服务端阶段完成事件推进，按 `(InstanceId, Phase)` 对同一完成事件去重，按 PVF 解析 `阶段 次数 角色` 和任务限制。12830 文案要求两阶段各 5 次、团队模式频道内每天只计 1 次，日限口径待实机。A21 成功结果路径会提交本地 `QUEST_EVENT_RAID_PHASE_CLEAR`。若随后收到 `0x0021`，只兼容其回显需求，计数仍由服务端阶段事件单独推进。上线时一起切换；实际是否发 `0x0021` 和需回显的字段待实机。见 [结果通知](../../../protocol/game/server-to-client/NOTI-0x025A.md)。

### 4.6 截止时间

在 `DungeonDeadlineKind` 加 `AntonRaid`，实现 `AntonRaidDeadlineSource`。它向攻坚领域服务要最近的截止时间（`NextDeadline`），逐项提交 `AdvanceRaidDeadlineCommand(InstanceId, Key, Version)`。实例状态变化时由攻坚领域服务在提交处调用 `Wake(AntonRaid)`。

截止时间随实例出现和消失，不重试、不补跑，过期项靠版本丢弃，这些都与调度器现有约定相符（[到点推进](../../DungeonRunKinds.md#一局之内的到点推进)）。唯一不同是状态所有者是攻坚实例而不是某一局，需要在该文档里补一句。

### 4.7 会话清理

在会话清理流程中登记攻坚一步，放在组队清理的位置：

1. 若在出战中，先让角色离开小队容器（这时副本离场才能进行）；
2. 团员身份转为 `Disconnected`，排到期离团；
3. 投影成员离线。

`CleanupNotificationStages` 的通知顺序见 [事件与通知](../../EventsAndNotifications.md)，攻坚投影排在队伍投影之后。

## 5. 状态机与主要用例

```
Recruiting ──START_RAID(团长)──▶ Preparing(gen) ──3s 到点且小队就绪且扣票成功──▶ Attacking(0)
     ▲                              │ 失败/作废
     └──────────────────────────────┘
Attacking(0) ──213、215 各满 2 次──▶ Settling(0) ──奖励完成──▶ Break ──团长 START_RAID 或 600s──▶ Preparing ──▶ Attacking(1)
Attacking(1) ──220 满 5 次──▶ Settling(1) ──奖励完成──▶ Completed ──成员各自离开──▶ 收尾
Attacking(n) ──进攻超时──▶ Failed(n) ──▶ 收尾
任意 ──团空──▶ 收尾
```

### 5.1 招募

- **建团。** 先走普通离队流程让建团者退出原有普通队，再建团（领域设计已裁定）。按启用编组借出空容器，建团者成为团长。已在别的团则先离开旧团，并给旧团广播离团（上游漏了这一步，`UpstreamStatic`）。
- **入团。** 申请（`0x0364` 预检后的申请流程，帧序待证）或团长邀请（`0x000A` 子类型 10），对方用 `0x000B` 子类型 10 应答，60 秒过期。条件：团在 `Recruiting` 或 `Break`；人数 <20；入团者不在任何团；同账号不能已在团内；双方在同一团本频道的城镇且不在副本中。同意后先让入团者退出原普通队，再加入。`Break` 期间入团的人没付过门票，二阶段开打时补扣（与第三方相同；上游只允许招募期入团）。
- **编组**（`0x029E` op0）。团长可以改任何人，普通团员只能改自己（`UpstreamStatic`）。招募期只改 `SquadIndex`。A21 的 A–J 控件能向 `0x029E op0` 写 1–10，协议解码保留这些原值。JP86 按实际启用编组租容器，单队最多 4 人和全团人数上限独立校验。当前 dev PVF 下 F–J 是否可达、是否作为可用编组开放，待实机确定。见 [团长操作](../../../protocol/game/client-to-server/0x029E.md)。
- **移交团长**（`0x029E` op1）。目标须是在线团员，准备中禁止。
- **离团**（`0x029A`）。A21 主动退团写自身 UID，随后发普通 `0x000D`；踢人确认写目标 UID。两条命令可能连续到达，团本退团时先释放小队关系，随后的退队按重复清理处理，避免作用到其他队伍。见 [离团协议](../../../protocol/game/client-to-server/0x029A.md)。团长离团时把团长移交给团序中第一位在线成员，没人则收尾。上游与第三方是直接解散，这里有意不同，避免攻坚中团长掉线让全团前功尽弃。
- **改标题**（`0x029F`）。只有团长能改，改后刷新目录。

### 5.2 开团准备与开始

1. 团长 `START_RAID`。检查：团长身份；`Recruiting` 或 `Break`；人数 ≥ PVF `[RAID START MINIMUM MEMBER]`（10）。只认 PVF，不另设配置；测试时要调低就改 PVF；每个团员持有门票（`Break` 时只查未付过的）；每个非空编组 ≤4 人。
2. 冻结名单，生成准备代次，排 3 秒截止时间。广播计时与 `PREPARE_START_RAID`。
3. 准备窗口接收 A21 发出的普通组队命令，按冻结编组通过租借句柄更新容器。多人组装配顺序见 §4.3。
4. 到点：团长在线；冻结名单未变；每个非空编组的容器成员与编组完全一致，队长是组内团序第一人。
5. 准备通过后执行全团扣票事务，提交成功再发布 `Attacking` 状态。失败回到原状态，向团员投影票据就绪情况；失败码及具体帧序按 §7.2 取证。

未编队团员（`SquadIndex = 0`）无须形成小队容器。当前入场设计要求小队容器，未编队角色是否可单人出战留待实机决定。

### 5.3 攻坚

- **阶段开始。** 由 `AntonRaidRuleService` 给出该阶段的地图表、符号初值、触发器和计时；初始化 `RaidPhaseState`，执行 `[2PHASE INIT]` 一类初始化触发，然后广播整套态势。
- **事件驱动。** 地图状态变化、计时到点、符号变化、出战通关都会喂给 `AntonRaidTriggerRules.Evaluate(state, event, script)`。它返回一组动作：设地图状态、预约地图状态、设符号、设或重置计时、开随机孵化中心、阶段完成。由攻坚领域服务应用这些动作，并产出投影。
- **手写规则**（脚本之外）：
  - 通关计数阈值（`HP INFO`）及对应的显示符号；
  - 孵化中心「4 开 3、1 个感染源」的随机，种子显式传入规则服务；
  - 屏障 0–10000 的扣减（`0x0297`）与回复（每秒 17，有感染时 34，`PvfContent` 加上游推断）；
  - buff 的施放、冷却与效果：延长阶段时限、返还小队复活币。
- **一阶段失守是复位，不是失败**，阶段总时限继续计。
- **进攻超时**：进入 `Failed`，结束全部出战，发失败结果，然后收尾。

### 5.4 掉线与重入

- 掉线时转为 `Disconnected`，排 300 秒到期；名单里按第三方的做法显示离线（`ThirdPartyStatic`，uid 写 0，cid 位置写断线时间），A21 实际表示 `Unknown`。
- 到期就离团；到期的是团长，就按 §5.1 的规则移交。
- 重入（`0x029D`，`u32` 取自断线通知）：校验角色、重入标识、实例与保留期。成功 CMD 回包仅结果信封，团对象、成员表、地图、符号、计时等快照另发 NOTI；完整补发集合和顺序待实机。恢复在场后由实例决定是否放回原编组的容器；只恢复团员或小队身份，保留中的旧局不会把该角色重新计入实际出战。
- [0x0328 RAID_DISCONN_INFO](../../../protocol/game/server-to-client/NOTI-0x0328.md) 有两条 A21 路径。本地尚无攻坚对象时，首个 `u32` 的 bits 16–23 命中本地比较值会立即回发携带原值的 `0x029D`；不匹配时进入 `0x263` 窗口路径；确认回调把第二字段与取出的位段传给 `moveChannel`。JP86 拟对尚有保留资格的角色发此通知。登录流程中的投递节点、换频道参数的完整语义和用户可见效果待实机。

## 6. 结算

### 6.1 门票

- 在「准备完成 → 进入攻坚」的状态转换里扣，转换与扣票同成同败，全团一笔事务。
- 理由：「全团带票开打」是一个业务事实，全部角色同在一个 SQLite 库。[玩法执行](../../GameplayExecution.md#总线与显式工作单元) 禁止把逐成员结算合成一笔，这里不是逐成员结算。这一解释已经确认。
- 失败不退票。PVF 的退路是 `[RETRY ITEM] 10099897`，物品说明为重置门票每日限购（`PvfContent`）。阶段失败时，JP86 按已定规则给本阶段参与者发系统邮件，键为 `raid:{InstanceId}:retry:{cid}`。客户端是否支持对应使用步骤、`[init anton raid try]` 的请求与反馈仍待取证。另有物品 10146912 使用 `[init anton raid buy item]`，两个动作各自取证。

### 6.2 资格

本阶段奖励资格 = 阶段完成时仍是团员（含保留期内离线者），且属于 `ClearParticipants`（本阶段任意一张图通关时实际在图内）。名单在进入 `Settling` 时冻结。

离线的有资格者照常获得奖励：翻牌按自动翻牌处理，结果经邮件投递。上游掉线即丢奖励，这里有意不同。

### 6.3 流程

1. 进入 `Settling` 后逐人投影结果，再投影全团状态。无资格时的结果末字段、`RAID_STATE(3,0)` 及结果出现后 2 秒发 `RAID_MOVIE_SKIP` 均是参照方案。A21 的 `RAID_MOVIE_SKIP` 可触发本地提示窗口，发送载荷和出现提示的条件见 [0x0258](../../../protocol/game/server-to-client/NOTI-0x0258.md)。
2. A21 的 [0x0294](../../../protocol/game/client-to-server/0x0294.md) 分别有条件发送 `[01]` 和 `[00]`。收到 `0x0258` 后，其对话框确认回调可触发 `[01]`；另一条本地输入分支可触发 `[00]`。`[01]` 推进到翻牌并发 `RAID_STATE(4,·)` 与两类牌列表，是上游参考；用实机确定各阶段、各结果下的触发及 `[00]` 的处理。JP86 的翻牌开启和自动翻牌按同一次结算做幂等推进。
   - **新增兜底：** 截止时若翻牌尚未开启则自动开启，收到 `[00]` 也不取消兜底，避免卡在 State 3（上游有此风险）。时限首版取 15 秒，待实机定。
3. 一次客户端选牌会按 `type 1 → type 0` 发送同一牌位；满足另一个本地条件还会追加 `type 2`。JP86 只为 type 0、1 计算与发放奖励；type 2 不产生奖励，回包形状和是否影响选牌交互待实机。见 [0x0295](../../../protocol/game/client-to-server/0x0295.md)。
   - 同小队的牌位不能重复，冲突时取最小空位；同一成员两类牌用同一个牌位。
   - 先记录已领，再向同小队揭示，回 ACK，入账。入账键为 `(InstanceId, phase, cid, rewardType)`。
4. 全员翻完或自动翻牌后，等 2 秒，全团发小队大奖（`RAID_REWARD_LIST` type 3）；再等 5 秒逐人入账。
5. 推进生命周期（`Break` 或 `Completed`），提交任务进度，发 `ENABLE_CLEAR_DUNGEON` 放行还在图里的人。

### 6.4 奖池与名次

- 奖池按 PVF `[CLEAR REWARD ITEM]` 的权重抽取，不照搬上游把二阶段大奖压成 100% 金边的改动。
- **名次与金币档对不上。** 按上游的映射，死亡越多拿到的金币档越高，属于疑点（`Unknown`）。首版在规则服务里集中一处映射，实机或逆向后再定。
- `pcroom_card` 不实现。
- 金币牌面是 u16，超过 65535 时牌面显示封顶，实发全额。

## 7. 协议

### 7.1 受众

| 受众 | 范围 | 用途 |
| --- | --- | --- |
| 本人 | 请求者或单个团员 | ACK、重入补发、结果帧 |
| 全团 | 当前在线团员 | 状态、态势、成员状态 |
| 同小队 | 同 `SquadIndex` 的在线团员 | 翻牌揭示 |
| 团本频道 | 该团本频道全部在线会话 | `RAID_LIST`、`RAID_MODIFY` |

现有通知受众里没有频道级人群（[事件与通知](../../EventsAndNotifications.md#消费边界)）。做法是投影层向频道在场查询取会话集合，再用 `TheseSessions` 投递，领域服务不碰连接。

发送任何带团员 uid 的帧（成员表、团长变更、申请弹窗）之前，先给收件人补发相关玩家的 `0x0002 USERINFO`，否则客户端解析不了 uid（`UpstreamStatic`）。

### 7.2 客户端 → 服务端

回包列指同 type 的 CMD 帧。「无 handler」表示当前 A21 CMD 表无同号处理入口。`0x0293`、`0x0294`、`0x029E` 的入口为空桩；是否保留结果 ACK 可按互操作需要决定。表中未标明客户端直接使用的业务含义仍按上游或待证处理。

| type | 名称 | body（客户端写法） | 领域操作 | 回包 |
| --- | --- | --- | --- | --- |
| 0x0299 | CREATE_RAID | `u8 0 + dstring` 标题 | 建团 | `01 + u32 团号`；失败 `00 + 错误码`，见 [0x0299](../../../protocol/game/client-to-server/0x0299.md) |
| 0x029A | LEAVE_RAID | u16 | 离团或踢人 | A21 成功读 `01 + u8`，非零有额外 UI；0/1 含义和受众暂按第三方参照 |
| 0x029B | START_RAID | 空 | 开团 / 二阶段开打 | 成功仅 `01`。失败 `0x11` / `0xD9` 在 `00 + 错误码` 后再读 `u32 n + n×u32`；见 [0x029B](../../../protocol/game/client-to-server/0x029B.md) |
| 0x029C | SET_RAID_WAITING | `00 00`（A21 构包） | 登记等待表（参照） | A21 只读信封，业务含义留待取证 |
| 0x029D | REJOIN_RAID | u32 重入标识 | 重入 | A21 成功仅结果信封；失败读信封错误码，见 [0x029D](../../../protocol/game/client-to-server/0x029D.md) |
| 0x029E | RAID_MANAGER_WORK | 3×u32。`op=1` 分支写第二字段为 `u16` 扩宽、第三字段为 0 | `op=0` 编组、`op=1` 移交，见 [0x029E](../../../protocol/game/client-to-server/0x029E.md) | 空桩 |
| 0x029F | MODIFY_RAID_INFO | `u8 0 + dstring` | 改标题 | 无 handler |
| 0x0293 | RAID_ENTRY_COST_INFO | `u8`，客户端路径分别写 0、1 | 请求门票就绪表（参照） | CMD 空桩；`0x0257` 的发送节点待证 |
| 0x0294 | RAID_MOVIE_SKIP | `u8`，已见 `[00]` 与 `[01]` 两条构包路径 | 结算动画相关，详见 §6.3 | 空桩 |
| 0x0295 | SELECT_RAID_REWARD_CARD | u8 type、u8 牌位 | 一次选择发 1、0，可条件追加 2 | 成功仅 `01`；失败码会影响客户端错误或请求状态 |
| 0x0296 | RAID_DO_BEHAVIOR | `u32 + u16`，已核实构包 6 字节 | `0x0247` 接收宽度相同；字段业务含义和合法受众待实机 | 无 handler |
| 0x0297 | RAID_SET_SYMBOL | `u32 + u32 + u8`，构包限定 `op=0..2` | 上报符号。屏障以外的调用者和操作含义待实机 | 无 handler |
| 0x02B7 | RAID_BUFF_SYSTEM | `u8 + u16 + n×u16`，目标数由载荷长度导出 | 施放 buff；类型及施放权限待证 | 无 handler |
| 0x02B8 | RAID_MONSTER_HP | `u8 n + n×u32`，含填 0 的路径 | 拟缓存已登记出战的显示状态；`n=0` 可补发快照。实际出战仍以服务端加载完成登记为准 | 无 handler |
| 0x0363 | RAID_REQUEST_RAID_MEMBERS | u32 | 补发团状态 | 无 handler |
| 0x0364 | RAID_CHECK_RAID_USER | `u8 + u16`，来自目标角色对象 | 入团前的客户端预检；实际申请发送时点待证 | 成功读 `01 + u32`，后续用于查团；见 [0x0364](../../../protocol/game/client-to-server/0x0364.md) |
| 0x033C | RAID_OTHER_CHANNEL_LIST | u8 | 1 = 等待列表，否则团目录 | 无 handler |
| 0x0334 | RAID_RECENT_FRIEND_LIST | 未知 | 最近同团玩家（参照） | 空表布局和是否需回包待取证 |
| 0x0298 | RAID_MESSAGE | 静态下界 `u8 + dstring`，参照另有尾字节 | 团内消息（参照） | 上游团聊走 `0x0011` mode 52/53；实际入口与余下布局待证 |
| 0x0331–0x0333 | 跨频道招募 | 部分可得 | 首版不实现 | — |
| 0x000A / 0x000B 子类型 10 | 攻坚邀请与应答 | `0x000A` 为 9B，`0x000B` 已见 7B 构包 | 优先路由到招募与入团审批。普通组队命令会跳过此子类型 | 复用解码器；应答与投影的帧序待证 |
| 0x000A–0x000E、0x0079 的普通分支 | 普通组队 | 见现有协议；准备期邀请和应答使用子类型 0 | 涉及团员时交给攻坚，见 §4.3 | 沿用普通组队信封 |

### 7.3 服务端 → 客户端

| type | 名称 | 布局 | 受众 | 何时发 |
| --- | --- | --- | --- | --- |
| 0x024F | RAID_LIST | `u32 n + n×(团对象 + u8)`。末字节在上游为人数，见 [目录解析](../../../protocol/game/server-to-client/NOTI-0x024F.md) | 团本频道 | 建团、入离团、解散、移交、改标题、到场 |
| 0x0250 | RAID_MODIFY | u32 团号、u32 op；0 团对象 + 成员表、1 删除、2 团对象、3 成员表、4 读目录尾字段的同一 `u8`。见 [分支解析](../../../protocol/game/server-to-client/NOTI-0x0250.md) | 团本频道 | 团对象或成员变化 |
| 0x024C | RAID_STATE | u8 state、u8 arg | 全团 | 生命周期变化 |
| 0x0253 | PREPARE_START_RAID | 上游发空 body，A21 已跟到本地编组路径 | 全团 | 进入准备，触发组队分支见 §4.3 |
| 0x0255 | RAID_SET_TIMER | `u8 类型 + u32 键 + u32 Unix 秒截止 + u32 持续秒数` | 全团 | 计时启动或重排；A21 以 `截止值 − 时长` 记起点；用途和重入补发口径待实机 |
| 0x0256 | RAID_REMAIN_TIME | u8 种类、u32 剩余秒（A21 乘 1000 加本地时钟） | 全团 | 上游常与 `0x0255` 的类型 0 成对发送 |
| 0x0257 | RAID_ENTRY_COST_INFO | u32 条数 + 每条（u16 uid、u8 就绪、u16 持有数） | 全团 | 入团、请求、准备失败 |
| 0x024A | RAID_DUNGEON_STATE | `u8 + u8 n + n×(u32 图号, u8 状态) + u32 感染图号`。首字节已见读取后丢弃 | 全团 | 阶段开始、孵化开启、重入 |
| 0x024B | RAID_CHANGE_DUNGEON_STATE | `u32 图号 + u8 + u8 状态`，中间字节读取后丢弃 | 全团 | 单图状态变化；A21 状态为 `0=open、1=close、2=hide、3=clear` |
| 0x0248 | RAID_SET_SYMBOL | `u8 n + n×(u32 ID, u32 值)`。`(0,0)` 清符号并提前结束本帧 | 全团 | 符号变化。清空项单独发，重新设值另发一帧，见 [符号通知](../../../protocol/game/server-to-client/NOTI-0x0248.md) |
| 0x0249 | RAID_DUNGEON_PARTICIPATION_INFO | `u8 组数 + 各组{u32 图号, u8 op, u8 n, n×u16 UID}` | 全团（参照） | 上游进图 op 0 再 1、离图 0、通关 4；A21 有 0/3、1/2、4 三组分派 |
| 0x027A | RAID_BUFF_SYSTEM | u8 组数、每组 u8 类型 + u8 n + n×(u16 小队, u16 uid, u32 生效止, u32 冷却止) | 全团 | 阶段开始、施放、重入 |
| 0x027B | RAID_MONSTER_HP | u8 组数、每组 u16 小队 + u8 n + n×u16 uid + u32 已用币 + u8 m + m×u32 | 全团 | 进图、通关、离图、复活、改编 |
| 0x024E | RAID_MEMBER_STATE | `u16 uid + u8`。A21 仅末字段为 0 时进入成员文本处理，其他值在该路径无动作 | 全团（参照） | 文本与触发场景待实机；死亡和复活分别按服务端战斗事件计数 |
| 0x0247 | RAID_DO_BEHAVIOR | 转发 `0x0296` body | 全团 | — |
| 0x025A | RAID_RESULT | `u8 结果 + u8 阶段 + u32 + u16 + u8 + u8`。0/2 选成功过场，1/3 选失败过场，阶段 0/1 选不同资源 | 本人（参照） | 结算开始、失败；名次、资格和金币档联动待实机 |
| 0x0258 | RAID_MOVIE_SKIP | 已追到的 A21 提示窗口路径未读 body；上游发 `u32 0 + u32 0` | 全团（参照） | 结算后提示，实际投递点与最短载荷待实机 |
| 0x0259 | RAID_REWARD_LIST | `u8 type + u8 n + n×(u16, u8, u8, u32, u16)`，A21 确认第二字节是条数。各条业务字段见协议页 | 本人 / 同小队 / 全团 | 翻牌开启、揭示、大奖 |
| 0x0328 | RAID_DISCONN_INFO | u32 + u16 | 本人 | 重新登录且有可重入的团，见 §5.4 |
| 0x04BD | RAID_USER_FINISH_LOAD | 空 | 本人 | 到达团本城镇 |
| 0x02EB | RAID_OTHER_CHANNEL_WAITING_LIST | u32 条数 + 39 字节记录 | 本人 | 等待列表；频道与服务器序号的先后有争议 |
| 0x0007 / 0x000B / 0x0009 / 0x0099 | 普通组队帧 | 沿用 | 相关成员 | 攻坚队邀请（子类型 10）；小队形成与改编 |
| 0x01F7 | SERVER_NOTICE_MESSAGE | u8 + dstring | 本人 / 全团 | 入团、拒绝、失败提示 |
| 0x001F | ENABLE_CLEAR_DUNGEON | 空 | 在图内者 | 阶段结束、失败 |

首版不发：`0x0251`、`0x0252`、`0x0273`、`0x028A`、`0x0314`、`0x02EA`、`0x02EC`–`0x02F0`，以及 `0x0327 RAID_MEMBERS`。两个参照服务端都没发过它们，等需要时再补。

### 7.4 上游帧序与 A21 取证参照

下面的上下行顺序和受众来自参照实现，供实机对拍。A21 静态确认的动作在各条中单独标记。计时括号列业务值；`0x0255` 仍须编码 §7.3 的四字段。

**建团**：`→0x0299` → 建团者：USERINFO + `0250 op0` → 频道其他人：USERINFO + `0250 op0` → 建团者：CMD `0299` ACK → 频道：`024F`。上游是 NOTI 先于 ACK。

**入团（上游申请序列）**：A21 的 `0x0364` 后还读同号成功 CMD 携带的团号，并走本地预检；原版后续申请待取证。上游序列是 `→0x0364` → 团长：USERINFO + `0007` 子类型 10 → `→0x000B` 子类型 10（团长） → 入团者：`0250 op0` + `op2` + `op3` + `0257` + 计时快照 → 频道：`0250 op0` + `op3` + `024F` → 入团者与全团：`01F7` 提示。

**开团**：`→0x029B` → 团长：ACK → 频道：`0250 op0` → 全团：`0255`(0,0,3)、`0256`(0,3)、`0253` → A21 按编组可发普通 `0x000C` 或 `0x000B`（§4.3） → 3 秒到点 → 全团：`0250 op2/op3`、`024C`(2,0)、`024A` 整表、`027A`、`027B`、`0248` 初值、`0255`(0,0,2400)、`0256`(0,2400)。

**出战一张图**：普通选图与进图 → 读图完成 → 全团：`0249` op0、op1、`027B` → 上游收到 `0x002E` 并进入 `ResultShown` → 全团：`0250 op3`、`024B`、`0249` op4、触发器产生的 `024B` / `0248` / `0255`，最后 `027B` → 离图 → 全团：`0249` op0、`027B`。JP86 拟在首领房清场时记通关，线上投影是否应等到 `0x002E` 由地图 210 的完整帧序裁决。

**阶段结算**：每人 `025A` → 全团 `024C`(3,0) → 2 秒 `0258` → `→0x0294[01]` → 本人 `024C`(4,·)、`0259` type 0 与 type 1 → A21 `→0x0295` 依次带 type 1、0，条件追加 type 2 → 同小队 `0259` 揭示、本人 ACK → 2 秒，全团 `0259` type 3 → 5 秒入账 → 全团 `024C`(5,0)、`0255`(0,0,595)、`0256`(1,595)、`001F`。

**重入**：A21 命中本地条件时 `0x0328` → `→0x029D`；JP86 拟回成功 CMD，并向本人投影：USERINFO + `0250 op0/op2/op3`、`0257`、`024C`、`024A`、`0248`（非动画符号）、各 `0255` / `0256`、`027A`、`027B`、出战中的 `0249`。

## 8. 待定与验证

### 8.1 已裁定

1. 团长离团或掉线保留期满：移交给团序第一位在线成员，没人在线才收尾（§5.1）。
2. 开团最低人数：只认 PVF 的 `[RAID START MINIMUM MEMBER]`，测试时调低就改 PVF（§5.2）。
3. 间歇期允许入团，二阶段开打时对没付过票的人补扣门票（§5.1）。
4. 开团时全团门票在一笔事务里扣除（§6.1）。
5. 阶段失败时用邮件给参与者发 10099897 重试证书，使用效果取证后再做（§6.1）。

### 8.2 A21 实机取证

A21 指令已经给出开团组队分支、可构包的组号、地图状态枚举和选牌请求顺序。下面只保留客户端线上行为、语义或时序仍需确认的项目。每组记录客户端版本、PVF 版本、双方收发帧与画面，空 body 和 CMD/NOTI 同号帧用服务端发送日志对齐。

| 场景 | 最小观察 | 要定的实现边界 |
| --- | --- | --- |
| 入场与招募 | 分别以任务 12829 完成前后进入团本频道，确认 town 19 area 2 与 NPC 327 可达性。用两个账号完成建团、申请、拒绝、同意与改组，操作 F–J；抓 `0x0364` CMD 回包、其后的普通邀请及 `0x04BD` | 确认服务端的落点、开放组号、预检与入团审批的顺序，以及团对象、成员表和普通小队投影时点 |
| 开团准备 | 同一团至少准备 3 个会话，组成一个单成员组和一个双成员组，分别抓团长、组首及非组首。按登记的 PVF 满足最低开团人数；若只用 3 人，登记最低人数为 3 的取证 PVF。记录 `0x0253`、`0x000C`、`0x0007`、`0x000A`、`0x000B` 及普通队伍通知；补测不足票和准备不齐 | 确认多人组首如何建队、自动应答是否先到、就绪条件和错误列表的含义 |
| 阶段和出战 | 在地图 210 记录选图、读图、通关、离图、死亡和复活。跨阶段记录 `0x024C`，用带已知时长的 `0x0255/0256` 对照倒计时；观察客户端发出的符号、行为、HP 与 buff 情况 | 确认参与和战况的投影时点、成员提示、计时投影、状态 1、符号操作权限及客户端是否自行执行触发器 |
| 孵化中心 | 在阶段二孵化开放前后分别查看城镇导航并操作 218 内机关，记录实际换图请求和归属的 `RunId` | 验证 dev PVF 的四角小队传送在开放和关闭时的表现，确认换图对实际出战名单、源局处理和返回路径的影响 |
| 阶段结算 | 分别记录两个阶段的成功与超时，含过场提示、确认/其他退出方式及一次选牌。对照名次、金币档、资格与牌面；有第三类选牌请求时记录 UI 应答 | 确认 `0x0258` 实际载荷、两个 `0x0294` 分支的用途、条件 type 2 的设置及兼容应答、结算投影顺序 |
| 任务及重试道具 | 用持有任务的角色完成两阶段并观察 `0x0021`、任务回显和同日重复完成；用已购票角色操作 10099897 并验证当天能否重购 | 确认任务日限与回显去重，取证 `[init anton raid try]` 的请求和反馈 |
| 退出与重入 | 测主动退团紧接普通退队、被踢、普通成员及团长掉线。保留期内分别在同频道和其他频道登录 | 确认 `0x0328` 的发送节点、换频道参数、自动重入和名单显示、重入快照及待命小队恢复 |

### 8.3 留待补证的静态边界

- `0x0298` 可变构包、`0x0334`、跨频道请求和非首版 NOTI 保留按需研究。注册、看到部分读取、看到 UI 资源分别能证明不同范围。
- `0x0258` 的已追踪 UI 路径未读 body。是否有其他路径及实际发包长度留给线级取证。
- dev PVF 已给出 `102=KILL ALL CHARACTER`、`103=KICK OUT CHARACTER`、`110=BLACK VOLCANO BARRIER`。上游使用的 `111` 在该 PVF 符号表未定位。
- 脚本解释器的完整分支、客户端资源配置和 UI 可达性需要同时观察地图状态、输入和其他会话。已确认的指令、布局与未确认的线上条件见 [协议清单](../../../protocol/catalog.md#2026-11-19-安图恩攻坚a21-静态取证待实机)。

### 8.4 协议资料维护

实现时采用各协议页人工区的客户端结论。生成区仍可能含早期上游转录的语义，缺口及版本差别见 [JP86 协议与客户端证据盘点](research/04-jp86-protocol-client-pvf.md#15-协议页层面的风险)。

## 9. 实现顺序建议

1. **入口与招募取证**。核对频道任务门槛、城镇落点及首批团本投影，按 §8.2 形成可复核帧序。
2. **组队容器**：拆出容器，实现共享归属索引、租借句柄、借出与归还、普通入口的所有者路由（见领域设计）。
3. **招募**。落地 `IAntonRaidDomainService` 的招募部分、预检回包、离团及编组、攻坚邀请和团目录。若参照服无法完成取证，用该切片补抓 §8.2 的招募场景。
4. **开团准备与门票**。接通 `0x029B`、`0x0253`、准备期组队路由和全团扣票事务，再补抓 §8.2 的开团准备场景。
5. **攻坚**：`AntonRaidRuleService` 与触发器解释器（先用 PVF 全量 dump 做单元测试），出战接缝，截止时间来源，一阶段跑通后再做二阶段。
6. **结算**。按 A21 构包处理翻牌，接入大奖、邮件和服务端任务计数，核对过场反馈、日限和任务回显。
7. **掉线与重入**。
