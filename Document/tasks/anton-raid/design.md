# 安图恩攻坚服务设计

2026-11-19。A21 安图恩攻坚团本的服务端方案，覆盖领域服务、状态字段、与现有组队、副本、库存、任务的接缝，以及服务端与客户端之间的协议往来。小队容器与 `PartyService` 的边界见 [安图恩团本领域设计](../../AntonRaidDesign.md)，本文不重复。

本文是实现前方案，不是已证事实。依据见 `research/`：

| 文件 | 内容 |
| --- | --- |
| [01-upstream-recruitment.md](research/01-upstream-recruitment.md) | 上游招募、编队、开团准备、离团、重入、断线 |
| [02-upstream-progression.md](research/02-upstream-progression.md) | 上游攻坚进行中：地图、触发器、计时、阶段、失败 |
| [03-upstream-rewards.md](research/03-upstream-rewards.md) | 上游门票、结算、翻牌、任务、持久化 |
| [04-jp86-protocol-client-pvf.md](research/04-jp86-protocol-client-pvf.md) | 本仓协议页、客户端静态证据、PVF 内容 |

来源标签沿用 [证据规则](../../../protocol/evidence.md)：`ClientStatic`、`PvfContent`、`UpstreamStatic`、`ThirdPartyStatic`、`Unknown`。上游代码只是线索。目前没有任何团本抓包或实机记录。

## 1. 结论先行

1. **攻坚实例全在内存。** 和 `DungeonRun` 一样，进程重启即失效。落库的只有门票扣除、奖励入账和任务进度，三者都带幂等键。
2. **团员身份与连接解耦。** 掉线保留 300 秒（PVF `[DISCONN MEMBER WAIT TIME]`），期间可 `REJOIN_RAID` 回到团内。
3. **每支出战小队就是一局普通副本。** 新增玩法种类 `AntonRaid`，入场由攻坚实例裁决。攻坚只消费五种局内回报：进图、通关、死亡、复活、离图。
4. **阶段机制由 PVF 驱动。** `etc/raid/anton.etc` 每阶段是一套词汇封闭的「条件 → 动作」触发脚本，做成规则服务加纯规则解释器。地图状态、符号、计时都是它的状态。手写的只有脚本之外的部分：通关阈值、孵化随机与感染、屏障回复、结算。
5. **小队由客户端在开团准备期自建，服务端按团编组校验。** 上游就是这样做的：`PREPARE_START_RAID` 之后，组内第一人发 `SET_PARTY_INFO` 并邀请队友，服务端按冻结的编组接受（`UpstreamStatic`，`PartyHandler.cs:1816-1842`）。团员发出的普通组队请求因此要交给攻坚实例裁决，通过租借句柄写入小队容器，见 §4.3。
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
| `RaidNumber` | u32 | 线上团号，沿用上游 `(频道号 << 16) \| 团长 cid`，冲突顺延（`UpstreamStatic`）。客户端是否从高位取频道，`Unknown` |
| `ChannelId` | int | 所在团本频道 |
| `Title` | `RaidTitle`（原样字节） | 只有团长能改 |
| `LeaderCharacterId` | `CharacterId` | 团长 |
| `LeadershipVersion` | long | 移交团长时加 1，旧审批随之失效 |
| `Members` | 有序 `RaidMember` 列表，≤20 | 顺序有业务含义：同一编组内第一人是小队队长（`UpstreamStatic`） |
| `AssignmentVersion` | long | 成员增减、编组变化、移交时加 1，用于改编的乐观校验 |
| `Squads` | 5 个 `RaidSquad` | 小队容器租借，见 §3.3 |
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
| `SquadIndex` | 0 = 未编队，1..5 = 小队序号 |
| `Presence` | `Online(SessionGeneration)` 或 `Disconnected(Since, Deadline, Generation)` |
| `PhaseClears` | 本阶段个人通关次数（成员表显示与评级用） |

### 3.3 小队 `RaidSquad`

| 字段 | 含义 |
| --- | --- |
| `Index` | 1..5 |
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
| `Maps` | `dungeonId → RaidMapState`：状态字（open/close/hide/clear）、通关计数、所需次数（PVF `HP INFO`）、同图小队上限（`PARTY MAX`） |
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
| `Preparing(Generation, FrozenRoster, NextPhase)` | (0, 0) 或间歇中 (5, 0) | 3 秒准备窗口，客户端自建小队 |
| `Attacking(Phase)` | (2, Phase) | 攻坚中 |
| `Settling(Phase)` | (3, 0) | 阶段结算：结果、动画、翻牌、大奖 |
| `Break` | (5, 0) | 两阶段间歇 600 秒（上游实际排 595 秒） |
| `Completed` | (4, 0) | 二阶段完成，等待成员离开 |
| `Failed(Phase)` | 不发 `RAID_STATE`，改发失败 `RAID_RESULT` | 进攻超时 |

线上值来自上游（`UpstreamStatic`）。State 1 上游不用，第三方在二阶段开打前发 (1,1)，含义 `Unknown`。

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
- PVF 计时：守图（类型 1）、关闭后重开（类型 2）、周期效果（类型 3）、`RESERVE DUNGEON STATE` 延时开放；
- 孵化开启、屏障回复（1 秒）；
- 结算各段：动画提示 2 秒、自动翻牌 10 秒、大奖入账 5 秒、翻牌开启兜底（新增，见 §6.3）；
- 掉线保留到期。

同键重排就把 `Version` 加 1，到点命令带着 `(InstanceId, Key, Version)`，对不上就丢弃。上游的裸 `Task.Delay` 全部改为这里的截止时间。

## 4. 与现有领域的接缝

### 4.1 频道与城镇

- 团本频道已有：`[raid]` 行 type 23 → `ChannelEntryKind.Raid`，团本城镇 19（`CurrentImplementation`，`ChannelScript.cs:78`、`IChannelSources.cs:37`）。
- **待核：落点。** PVF 写的等待室是 town 19 area 2，NPC 327 负责开团和离开攻坚区。本仓现在落在 gate 区，要实机确认 NPC 是否可达。
- **门槛：** 任务 12829 完成后才能进团队模式频道（`PvfContent`）。本仓频道准入还没有这一条，放进频道准入规则。
- 到场时只给本人发空 body 的 `0x04BD RAID_USER_FINISH_LOAD`，再刷新团目录。

### 4.2 组队：小队容器

容器、共享归属索引、租借句柄、归还规则都以 [领域设计](../../AntonRaidDesign.md#小队容器与租借) 为准。本方案按资料补两点：

1. **容器何时有人。** 招募期编组只改 `SquadIndex`，不往容器里放人，也不发 `0x0009`（上游如此）。容器在开团准备期由客户端自建小队的请求填入，在攻坚中与间歇期由团长改编时服务端直接替换。
2. **普通组队入口改为交给所有者。** 领域设计原来写的是「普通入口遇到借出容器一律拒绝」。资料表明团员在准备期和攻坚中都会走普通组队协议，所以改成：组队类协议命令先识别涉及的角色是不是团员，是团员就交给攻坚领域服务的 `HandlePartyRequest` 裁决，由它通过租借句柄写容器；不是团员才走 `PartyService`。`PartyService` 仍然拒绝任何直接作用于借出容器的普通操作，这条保留，用来兜底。

### 4.3 团员发出的普通组队请求

| 请求 | 准备期（`Preparing`） | 攻坚中与间歇（`Attacking`、`Break`） | 其他状态 |
| --- | --- | --- | --- |
| `0x000C SET_PARTY_INFO` | 组内第一人：把自己放进本组容器、设为队长，设置块强制为团本小队设置（容量 4，`[9]=2`） | 已在小队：忽略设置变更，回成功 | 招募期团员：拒绝，团员不能组普通队 |
| `0x000A` 邀请（子类型 0） | 只接受同组的第一人邀请同组成员，转发 `0x0007` | 团员之间：当作编组变更交给改编规则；有一方是非团员则拒绝 | 拒绝 |
| `0x000B` 应答 | 按冻结编组校验后加入容器 | 同上 | 拒绝 |
| `0x000D` 退队 | 让准备作废 | 不在副本时移回未编队区并移出容器 | — |
| `0x000E` 踢人、`0x0079` 换队长 | 拒绝 | 只有团长能改编，按改编规则处理 | — |

准备期的具体帧序是实机首要验证项。如果 A21 客户端收到 `PREPARE_START_RAID` 后并不自建小队，就改为由服务端在准备结束时直接按编组填容器，再推送 `0x0009`、`0x0099`、`0x000B`。这两种做法的接缝都在攻坚领域服务的开团准备部分，换做法不影响其他部分。

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
| 孵化中心入口 | 第三方从 218 局内四角直接转入孵化中心，PVF 的「安徒恩的筋」机关可以旁证（`ThirdPartyStatic` + `PvfContent`）。首版按普通选图从城镇进入；如果实机证实是局内转入，再加「局内换图」入场路径 |

### 4.5 库存、邮件、任务

- **门票。** 10096296 ×1，在 NPC 326 的商店 108 购买，每角色每日限购 1（`PvfContent`）。扣除点见 §6.1。
- **入账。** 在线成员直接提交背包；离线或背包提交失败的，走系统邮件，`delivery_key = raid:{InstanceId}:{phase}:{cid}:{kind}`。
- **任务。** **必须先改** `QuestObjectiveAuthority`：`raid phase clear` 现在列在客户端计数里（`CurrentImplementation`，`QuestObjectiveAuthority.cs:61-71`），客户端可以自己报完成。改为服务端计数，由阶段完成提交，事件 id 用 `(InstanceId, Phase)`。三元组 `阶段 次数 角色` 里的角色（-1 不限、0 攻坚队长、1 小队队长、2 队员）要一并解析，称号书 6742–6744 依赖它（`PvfContent`）。

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

- **建团。** 先走普通离队流程让建团者退出原有普通队，再建团（领域设计已裁定）。借出 5 个空容器，成为团长。已在别的团则先离开旧团，并给旧团广播离团（上游漏了这一步，`UpstreamStatic`）。
- **入团。** 申请（`0x0364`）或团长邀请（`0x000A` 子类型 10），对方用 `0x000B` 子类型 10 应答，60 秒过期。条件：团在 `Recruiting` 或 `Break`；人数 <20；入团者不在任何团；同账号不能已在团内；双方在同一团本频道的城镇且不在副本中。同意后先让入团者退出原普通队，再加入。`Break` 期间入团的人没付过门票，二阶段开打时补扣（与第三方相同；上游只允许招募期入团）。
- **编组**（`0x029E` op0）。团长可以改任何人，普通团员只能改自己（`UpstreamStatic`）。招募期只改 `SquadIndex`，序号 1..5，超过 5 拒绝。每队人数在开团时才校验。A21 是否会发 6..10，待实机确认。
- **移交团长**（`0x029E` op1）。目标须是在线团员，准备中禁止。
- **离团**（`0x029A`）。u16 字段按第三方读作目标 uid：等于自己是离团，否则是团长踢人（`ThirdPartyStatic`，宽度与 `ClientStatic` 吻合）。团长离团时把团长移交给团序中第一位在线成员，没人则收尾。上游与第三方是直接解散，这里有意不同，避免攻坚中团长掉线让全团前功尽弃。
- **改标题**（`0x029F`）。只有团长能改，改后刷新目录。

### 5.2 开团准备与开始

1. 团长 `START_RAID`。检查：团长身份；`Recruiting` 或 `Break`；人数 ≥ PVF `[RAID START MINIMUM MEMBER]`（10）。只认 PVF，不另设配置；测试时要调低就改 PVF；每个团员持有门票（`Break` 时只查未付过的）；每个非空编组 ≤4 人。
2. 冻结名单，生成准备代次，排 3 秒截止时间。广播计时与 `PREPARE_START_RAID`。
3. 3 秒内客户端自建小队（§4.3），由租借句柄写入容器。
4. 到点：团长在线；冻结名单未变；每个非空编组的容器成员与编组完全一致，队长是组内团序第一人。
5. 成功则在同一事务里扣门票并进入 `Attacking`；失败则回到原状态，不扣票，向全团发 `RAID_STATE(0,0)` 或 `(5,0)` 与门票就绪表，给团长发提示。

未编队团员（`SquadIndex = 0`）不必组队，在攻坚中作为单人组。上游允许，但单人能否出战待实机。

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
- 重入（`0x029D`，u32 推测是团号）：校验仍是团员且在保留期内，恢复在线，然后给本人补发完整态势：团对象、成员表、`RAID_STATE`、地图状态整表、全部非动画符号、计时（按剩余时间重算）、buff、怪物表、出战快照。在攻坚中或间歇期，放回原 `SquadIndex` 的容器。上游只补计时与通关数，缺地图与符号（`UpstreamStatic`）。
- `0x0328 RAID_DISCONN_INFO` 很可能是让客户端弹出重入提示（地址邻近推测，`Unknown`）。首版在团员重新登录进入团本频道时，给本人发一帧。

## 6. 结算

### 6.1 门票

- 在「准备完成 → 进入攻坚」的状态转换里扣，转换与扣票同成同败，全团一笔事务。
- 理由：「全团带票开打」是一个业务事实，全部角色同在一个 SQLite 库。[玩法执行](../../GameplayExecution.md#总线与显式工作单元) 禁止把逐成员结算合成一笔，这里不是逐成员结算。这一解释已经确认。
- 失败不退票。PVF 的退路是 `[RETRY ITEM] 10099897`：阶段失败时发给参与者，使用后重置门票每日限购（`PvfContent`）。在 `Failed` 时给本阶段参与者用系统邮件发放，键为 `raid:{InstanceId}:retry:{cid}`。它的使用效果 `[init anton raid try]`（重置门票每日限购）的客户端包未知，取证后单独实现。

### 6.2 资格

本阶段奖励资格 = 阶段完成时仍是团员（含保留期内离线者），且属于 `ClearParticipants`（本阶段任意一张图通关时实际在图内）。名单在进入 `Settling` 时冻结。

离线的有资格者照常获得奖励：翻牌按自动翻牌处理，结果经邮件投递。上游掉线即丢奖励，这里有意不同。

### 6.3 流程

1. 进入 `Settling`：逐人单发 `RAID_RESULT`（无资格者 `rewardOption=1`），全团 `RAID_STATE(3,0)`；2 秒后若还没开翻牌，全团发 `RAID_MOVIE_SKIP`。
2. 任一客户端发 `0x0294 [01]` 就开启翻牌，给本人发 `RAID_STATE(4,·)` 和两帧 `RAID_REWARD_LIST`（type 0 金币、type 1 道具），并排 10 秒自动翻牌。
   - **新增兜底：** 结算开始后若一直没人发 `0x0294`，到某个时限也自动开启，避免卡在 State 3（上游有此风险）。时限首版取 15 秒，待实机定。
3. `0x0295 SELECT_RAID_REWARD_CARD`：只接受 type 0、1。type 2 一律拒绝，上游与第三方都有拿它多领金币的漏洞。
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

回包列指同 type 的 CMD 帧。「无 handler」指客户端没有为该 type 注册 CMD 响应处理，服务端不回。客户端为 `0x0293`、`0x0294`、`0x029E` 注册的响应 handler 是空函数（`ClientStatic`），回 1 字节 ACK 无害。

| type | 名称 | body（客户端写法） | 领域操作 | 回包 |
| --- | --- | --- | --- | --- |
| 0x0299 | CREATE_RAID | u8 + dstring 标题 | 建团 | `01`+u32 团号 / `00`（上游）。上游自测有抓包来源的样本 `00 03 00 00 00 31 32 33`，即 u8 0、i32 长度、字节；与本仓页面的偏移差异待样本核对 |
| 0x029A | LEAVE_RAID | u16 | 离团或踢人 | `01 00` 自己离团，`01 01` 给被踢者（`ThirdPartyStatic`） |
| 0x029B | START_RAID | 空 | 开团 / 二阶段开打 | 1 字节；失败时客户端按错误码 0x11 / 0xD9 读一个数组（`ClientStatic`），布局待读 |
| 0x029C | SET_RAID_WAITING | u8 + u8 | 登记等待表 | 21 字节（上游），含义 `Unknown` |
| 0x029D | REJOIN_RAID | u32 | 重入 | 客户端有 handler，上游不回；布局待静态读取 |
| 0x029E | RAID_MANAGER_WORK | u32 op、u32 uid、u32 参数 | op0 编组，op1 移交 | 1 字节 |
| 0x029F | MODIFY_RAID_INFO | u8 + dstring | 改标题 | 无 handler，不回 |
| 0x0293 | RAID_ENTRY_COST_INFO | u8 | 请求门票就绪表 | 1 字节 ACK + 全团 `0x0257` |
| 0x0294 | RAID_MOVIE_SKIP | u8（1 = 动画结束） | 开启翻牌 | 1 字节 |
| 0x0295 | SELECT_RAID_REWARD_CARD | u8 type、u8 牌位 | 翻牌 | 1 字节 |
| 0x0296 | RAID_DO_BEHAVIOR | u32 + u16（≥6 字节） | 原样广播 `0x0247` | 无 handler。上游要求 8 字节，会拒收客户端的帧；按客户端长度接收 |
| 0x0297 | RAID_SET_SYMBOL | u32 符号、u32 值、u8 op | 屏障扣减等 | 无 handler |
| 0x02B7 | RAID_BUFF_SYSTEM | u8 类型、u16 小队、n×u16 目标 | 施放 buff | 无 handler；施放权限 `Unknown`，首版只允许团长 |
| 0x02B8 | RAID_MONSTER_HP | u8 n、n×u32 | 缓存并广播 `0x027B`；n=0 时补登出战 | 无 handler |
| 0x0363 | RAID_REQUEST_RAID_MEMBERS | u32 | 补发团状态 | 无 handler |
| 0x0364 | RAID_CHECK_RAID_USER | u8 频道、u16 团长 uid | 申请入团 | 客户端有 handler，上游不回；布局待读 |
| 0x033C | RAID_OTHER_CHANNEL_LIST | u8 | 1 = 等待列表，否则团目录 | 无 handler |
| 0x0334 | RAID_RECENT_FRIEND_LIST | 未知 | 最近同团玩家 | 首版回空表 |
| 0x0298 | RAID_MESSAGE | u8 + dstring + u8 | 团内消息 | 上游团聊走 `0x0011` mode 52/53，`0x0298` 何时发 `Unknown` |
| 0x0331–0x0333 | 跨频道招募 | 部分可得 | 首版不实现 | — |
| 0x000A–0x000E、0x0079 | 普通组队 | — | 团员的请求交给攻坚，见 §4.3；`0x000A` / `0x000B` 子类型 10 是攻坚队邀请、申请与应答 | 沿用普通组队 |

### 7.3 服务端 → 客户端

| type | 名称 | 布局 | 受众 | 何时发 |
| --- | --- | --- | --- | --- |
| 0x024F | RAID_LIST | u32 条数 + 每条（团对象 + u8 人数） | 团本频道 | 建团、入离团、解散、移交、改标题、到场 |
| 0x0250 | RAID_MODIFY | u32 团号、u32 op；op0 团对象 + 成员表，op1 删除，op2 团对象，op3 成员表 | 团本频道 | 团对象或成员变化 |
| 0x024C | RAID_STATE | u8 state、u8 arg | 全团 | 生命周期变化 |
| 0x0253 | PREPARE_START_RAID | 空（上游） | 全团 | 进入准备 |
| 0x0255 | RAID_SET_TIMER | u8 类型、u32 图号、u32、u32 | 全团 | 计时启动或重排；后两个 u32 哪个是截止时刻、哪个是时长，`Unknown` |
| 0x0256 | RAID_REMAIN_TIME | u8 种类、u32 剩余秒 | 全团 | 与类型 0 计时成对 |
| 0x0257 | RAID_ENTRY_COST_INFO | u32 条数 + 每条（u16 uid、u8 就绪、u16 持有数） | 全团 | 入团、请求、准备失败 |
| 0x024A | RAID_DUNGEON_STATE | u8 0、u8 n、n×(u32 图号, u8 状态)、u32 感染图号 | 全团 | 阶段开始、孵化开启、重入 |
| 0x024B | RAID_CHANGE_DUNGEON_STATE | u32 图号、u8 0、u8 状态 | 全团 | 单图状态变化 |
| 0x0248 | RAID_SET_SYMBOL | u8 n、n×(u32 符号, u32 值) | 全团 | 符号变化 |
| 0x0249 | RAID_DUNGEON_PARTICIPATION_INFO | u8 1、u32 图号、u8 op、u8 n、n×u16 uid | 全团 | 进图 op 0 再 1、离图 0、通关 4 |
| 0x027A | RAID_BUFF_SYSTEM | u8 组数、每组 u8 类型 + u8 n + n×(u16 小队, u16 uid, u32 生效止, u32 冷却止) | 全团 | 阶段开始、施放、重入 |
| 0x027B | RAID_MONSTER_HP | u8 组数、每组 u16 小队 + u8 n + n×u16 uid + u32 已用币 + u8 m + m×u32 | 全团 | 进图、通关、离图、复活、改编 |
| 0x024E | RAID_MEMBER_STATE | u16 uid、u8 状态 | 全团 | 死亡与复活；取值待实机 |
| 0x0247 | RAID_DO_BEHAVIOR | 转发 `0x0296` body | 全团 | — |
| 0x025A | RAID_RESULT | u8 结果、u8 阶段、u32 用时、u16 死亡、u8 名次、u8 资格 | 本人 | 结算开始、失败 |
| 0x0258 | RAID_MOVIE_SKIP | 上游 u32、u32；客户端结构未知 | 全团 | 结算 2 秒后 |
| 0x0259 | RAID_REWARD_LIST | u8 type、u8 n、n×(u16 uid、u8 牌位、u8 金边、u32 道具、u16 数量) | 本人 / 同小队 / 全团 | 翻牌开启、揭示、大奖 |
| 0x0328 | RAID_DISCONN_INFO | u32 + u16 | 本人 | 重新登录且有可重入的团，见 §5.4 |
| 0x04BD | RAID_USER_FINISH_LOAD | 空 | 本人 | 到达团本城镇 |
| 0x02EB | RAID_OTHER_CHANNEL_WAITING_LIST | u32 条数 + 39 字节记录 | 本人 | 等待列表；频道与服务器序号的先后有争议 |
| 0x0007 / 0x000B / 0x0009 / 0x0099 | 普通组队帧 | 沿用 | 相关成员 | 攻坚队邀请（子类型 10）；小队形成与改编 |
| 0x01F7 | SERVER_NOTICE_MESSAGE | u8 + dstring | 本人 / 全团 | 入团、拒绝、失败提示 |
| 0x001F | ENABLE_CLEAR_DUNGEON | 空 | 在图内者 | 阶段结束、失败 |

首版不发：`0x0251`、`0x0252`、`0x0273`、`0x028A`、`0x0314`、`0x02EA`、`0x02EC`–`0x02F0`，以及 `0x0327 RAID_MEMBERS`。两个参照服务端都没发过它们，等需要时再补。

### 7.4 主要帧序

**建团**：`→0x0299` → 建团者：USERINFO + `0250 op0` → 频道其他人：USERINFO + `0250 op0` → 建团者：CMD `0299` ACK → 频道：`024F`。上游是 NOTI 先于 ACK。

**入团（申请）**：`→0x0364` → 团长：USERINFO + `0007` 子类型 10 → `→0x000B` 子类型 10（团长） → 入团者：`0250 op0` + `op2` + `op3` + `0257` + 计时快照 → 频道：`0250 op0` + `op3` + `024F` → 入团者与全团：`01F7` 提示。

**开团**：`→0x029B` → 团长：ACK → 频道：`0250 op0` → 全团：`0255`(0,0,3)、`0256`(0,3)、`0253` → 客户端自建小队（普通组队帧） → 3 秒到点 → 全团：`0250 op2/op3`、`024C`(2,0)、`024A` 整表、`027A`、`027B`、`0248` 初值、`0255`(0,0,2400)、`0256`(0,2400)。

**出战一张图**：普通选图与进图 → 读图完成 → 全团：`0249` op0、op1、`027B` → 通关（首领房击杀） → 全团：`0250 op3`、`024B`、`0249` op4、触发器产生的 `024B` / `0248` / `0255`，最后 `027B` → 离图 → 全团：`0249` op0、`027B`。

**阶段结算**：每人 `025A` → 全团 `024C`(3,0) → 2 秒 `0258` → `→0x0294[01]` → 本人 `024C`(4,·)、`0259` type 0 与 type 1 → `→0x0295` ×2 → 同小队 `0259` 揭示、本人 ACK → 2 秒，全团 `0259` type 3 → 5 秒入账 → 全团 `024C`(5,0)、`0255`(0,0,595)、`0256`(1,595)、`001F`。

**重入**：`→0x029D` → 本人：USERINFO + `0250 op0/op2/op3`、`0257`、`024C`、`024A`、`0248`（非动画符号）、各 `0255` / `0256`、`027A`、`027B`、出战中的 `0249`。

## 8. 待定与验证

### 8.1 已裁定

1. 团长离团或掉线保留期满：移交给团序第一位在线成员，没人在线才收尾（§5.1）。
2. 开团最低人数：只认 PVF 的 `[RAID START MINIMUM MEMBER]`，测试时调低就改 PVF（§5.2）。
3. 间歇期允许入团，二阶段开打时对没付过票的人补扣门票（§5.1）。
4. 开团时全团门票在一笔事务里扣除（§6.1）。
5. 阶段失败时用邮件给参与者发 10099897 重试证书，使用效果取证后再做（§6.1）。

### 8.2 首轮实机取证（按阻断程度）

1. 建团、申请、同意、编组的完整帧序与 CMD 回包；编组序号范围（5 还是 10）。
2. `PREPARE_START_RAID` 之后客户端是否自建小队；团本小队面板的退队、邀请发哪些帧。
3. 地图状态字节 1 与 3 哪个是 close、哪个是 clear（上游和第三方互相矛盾）。
4. 210 进图、读图、通关、回城的双向帧，确定 `0249` / `024B` / `0248` / `027B` 的表现。
5. 孵化中心是不是从 218 局内转入。
6. `0x0296` 的长度、`0x0297` 上报哪些符号、客户端是否自行执行部分触发器。
7. 结算：`0258` 布局、客户端何时发 `0x0294`、翻一张牌发一帧还是两帧、名次与金币档的对应。
8. 掉线与重入：`0x0328` 的触发、`0x029D` 的回包、名单中离线的显示。

### 8.3 静态可补的

- `0x029B`、`0x029D`、`0x0364` 的 CMD 响应 handler 布局。
- `0x0258`、`0x0273`、`0x028A`、`0x0314` 的接收布局。
- 客户端符号名到 ID 的映射。上游是硬编码，102、111 在 PVF 里没有名字。

### 8.4 协议页需要修正

ClientHack 仓的 `proto-*.md` 已经手工读出大部分 raid NOTI 的字段顺序，但还没抄进协议页。另有这些页面的含义或标题需要按 `protocol/README.md` 的规则修正：

- **0x029E**：「只接受 op0」已过时，现在还有 op1 移交团长。
- **NOTI 0x024C**：超时失败「写 3」已过时，现在超时改发 `RAID_RESULT`。
- **NOTI 0x024E**：死亡取值写的是旧上游。
- **NOTI 0x0007**：漏了子类型 10。
- **NOTI 0x0259**：条目数与 cardType 的偏移标反了。
- **NOTI 0x025A**：+8 应为名次。
- **NOTI 0x0248**：组内首字段应为 u32。
- **H1 标题与身份表不一致**：0x0327/0x0391 互相错位；0x0333、0x0335、0x0336、0x033C、0x0364 也对不上。

## 9. 实现顺序建议

1. **前置修正**：`raid phase clear` 改为服务端计数；协议页按 §8.4 修正；频道准入加 12829 门槛；核对团本城镇落点。
2. **组队容器**：拆出容器，实现共享归属索引、租借句柄、借出与归还、普通入口的所有者路由（见领域设计）。
3. **招募**：`IAntonRaidDomainService` 与 `AntonRaidInstance` 的招募部分，以及 0x0299、0x029A、0x029E、0x029F、0x0364、子类型 10 邀请、`024F` / `0250` 投影。到这一步就可以做第一轮实机取证。
4. **开团准备与门票**：0x029B、准备期组队路由、扣票事务。
5. **攻坚**：`AntonRaidRuleService` 与触发器解释器（先用 PVF 全量 dump 做单元测试），出战接缝，截止时间来源，一阶段跑通后再做二阶段。
6. **结算**：翻牌、大奖、任务、邮件补发。
7. **掉线与重入**。
