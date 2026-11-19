# 安图恩攻坚：入场费用、奖励、结算、任务与持久化（上游参照调研）

2026-11-19。只读调研，供 JP86 安图恩攻坚设计使用。范围：门票、阶段奖励资格、奖励内容与翻牌、结算时序、
任务与次数、持久化。攻坚前招募编队、攻坚中地图机制（地下城开闭、孵化场、屏障、复活币、buff）不在本文，
只在交界处提一句。

## 约定

- 来源标签：`UpstreamStatic`（ServerS4A21 `1870c85` 源码）、`ThirdPartyStatic`（第三方商业端 v3.3 反编译）、
  `ClientStatic`（A21 客户端静态读出，含协议页机器区；本文把读 Script.pvf 的结论也记作 `ClientStatic(PVF)`，
  PVF 为 dev 通道 `content:0.3.3-dev.1`，SHA-256 `a97132dd…`，用服务端仓 `Tool/PvfProbe` 读出）、`Unknown`。
  上游和第三方代码只能说明它们怎么做，不能证明 A21 客户端或原版服务端的行为。
- 路径缩写：
  - `U:` = `E:\Project\DNF\ServerS4A21\Server\DfoServer\`
  - `UL:` = `E:\Project\DNF\ServerS4A21\Tool\PvfLib\`
  - `T:` = `E:\Project\DNF\Research\third-party-s4a21-v3.3\decompiled\DfoServer\`
  - `P:` = 本仓 `protocol/game/`
- 上游阶段编号：`PhaseIndex` 0 = 一阶段「阻截安徒恩」，1 = 二阶段「灭杀安徒恩」。
  上游团状态值：0 招募、2 攻坚中、3 阶段结算（翻牌）、4 终局（`StateArgument` 0 成功／1 超时失败）、5 阶段间歇。

---

## 1. opcode 总表

收件人「本人」= 发起请求的会话；「全团」= `raid.Members` 全部在线会话（`U:Network/Handlers/RaidHandler.cs:394-398`）；
「同小队」= 同 `PartyIndex` 的成员。CMD 响应统一是同 type、1 字节 `1`/`0` 的 ACK（`U:Network/Handlers/RaidHandler.cs:413-416`）。

| 方向 | type | 名称 | 上游 handler／builder | body 布局（上游写法） | 触发与收件人 |
|---|---|---|---|---|---|
| C→S | `0x0293` | RAID_ENTRY_COST_INFO | 注册 `U:Network/Protocol/GameProtocolHandler.cs:493`；`U:Network/Handlers/RaidHandler.Admission.cs:42-72` | `+0 u8 stage` | 客户端在团本城镇请求。回 ACK；`stage==0` 时另发团本 buff 与怪物状态（属地图机制）；然后向全团广播 NOTI `0x0257`。另外 `BUY_ITEM` 成功后也刷新一次（`GameProtocolHandler.cs:559-566`） |
| S→C | `0x0257` | RAID_ENTRY_COST_INFO | `U:Network/Builders/Raid/RaidPacketBuilder.cs:496-515`，状态取值 `RaidHandler.Admission.cs:75-99` | `+0 u32 n`；每成员 5 字节：`u16 userId`、`u8 ready`、`u16 ownedCount`（持有门票数，封顶 65535） | 全团。`ready = (团状态 != 0) \|\| 持有数 >= 1`，开战后恒为 1 |
| C→S | `0x0294` | RAID_MOVIE_SKIP | 注册 `GameProtocolHandler.cs:498`；`RaidHandler.Rewards.cs:267-300` | `+0 u8`：`1` = 过场结束 | 仅团状态 3 且结算流程已开始时 ACK=1。`1` 时：开启翻牌、给本人发 `RAID_STATE(4, arg)`、给本人发两帧 `0x0259`（type 0 金币、type 1 道具，含同小队已翻开的牌） |
| S→C | `0x0258` | RAID_MOVIE_SKIP | `RaidPacketBuilder.cs:362-368`；发送点 `RaidHandler.Rewards.cs:327-338` | `+0 u32 movieId`、`+4 u32 option`，上游恒发 `(0,0)` | 全团。结算开始 2 s 后若翻牌仍未开启则广播一次，作「可跳过」提示 |
| C→S | `0x0295` | SELECT_RAID_REWARD_CARD | 注册 `GameProtocolHandler.cs:499`；`RaidHandler.Rewards.cs:438-474`，解析 `:506-521` | `+0 u8 rewardType`（≤2）、`+1 u8 cardIndex`（≤3），body 必须恰好 2 字节 | 见 §3.4。翻开后向同小队广播 `0x0259`，再回 ACK，再入账 |
| S→C | `0x0259` | RAID_REWARD_LIST | `RaidPacketBuilder.cs:370-395` | `+0 u8 rewardType`、`+1 u8 n`；每条 10 字节：`u16 userId`、`u8 cardType`、`u8 flags`、`u32 itemId`、`u16 quantity` | rewardType 0 金币牌（itemId=0，quantity=金额封顶 65535）、1 道具牌（cardType=牌位 0..3）、3 小队大奖（cardType 恒 1，flags=金边，全团广播）。翻牌揭示只发同小队 |
| S→C | `0x025A` | RAID_RESULT | `RaidPacketBuilder.cs:347-360`；成功 `RaidHandler.Rewards.cs:340-352`，失败 `RaidHandler.Progression.cs:912-919` | `+0 u8 resultType`（0 成功／1 超时失败）、`+1 u8 phaseIndex`、`+2 u32 clearTimeSeconds`、`+6 u16 deathCount`（封顶 65535）、`+8 u8 rank`（PVF 名次 + 2；失败恒 0）、`+9 u8 rewardOption`（0 有奖励资格／1 无） | 成功：逐成员单发（资格不同 option 不同）。失败：全团单发，且失败态下 `BroadcastRaidStateAsync` 也改发这帧（`RaidHandler.cs:388-392`） |
| S→C | `0x024C` | RAID_STATE | `RaidPacketBuilder.cs:292-306` | `u8 state`、`u8 arg` | 结算开始广播 state 3；过场结束只给本人发 state 4（服务端仍为 3）；阶段完成广播 5 或 4（细节属招募/机制 agent，列此只为时序） |
| S→C | `0x0314` | ALEADY_RAID_REWARD | 上游、第三方都**未使用**（只有枚举 `U:Network/Core/PacketTypesA21.cs:795`） | 机器区：未发现取字段，疑为纯触发 | `ClientStatic`：其 handler 入口 `0x01E31720` 与 `0x0293`/`0x0294` 的 CMD 响应 handler 是同一个地址，像通用桩。是否需要发送 `Unknown` |
| S→C | `0x0391` | OBJECT_DESTROY_REWARD | 上游、第三方未使用（`PacketTypesA21.cs:920`） | 机器区：324 字节块（forwarder） | 与团本结算无已知关系，`Unknown` |
| S→C | `0x0273` | CLEAR_RAID_BUFF | 上游、第三方未使用（`PacketTypesA21.cs:634`） | 机器区：结构未知 | 名称指向攻坚 buff 清除，归地图机制 agent；结算路径上游不发 |
| S→C | `0x01F7` | SERVER_NOTICE_MESSAGE | `RaidHandler.Rewards.cs:879-888` | 文本公告 | 本人。金币超过 65535 或撞携带上限时补报实际到账额（上游自创文案） |
| S→C | `0x001F` | ENABLE_CLEAR_DUNGEON | `RaidHandler.Rewards.cs:790-808` | 空 | 阶段奖励结束后给仍在团本地下城里的成员，允许返城 |

### 与 JP86 协议页机器区对照

| 协议页 | 结构对照 | 含义标注问题 |
|---|---|---|
| `P:client-to-server/0x0293.md` | 1 字节，一致 | 无 |
| `P:client-to-server/0x0294.md` | 1 字节，一致 | 引用行号过期（写 `Rewards.cs:254-257`，现为 `:293-300`） |
| `P:client-to-server/0x0295.md` | 2 字节，一致 | 引用行号过期（现为 `:506-521`）；「1 取 party_card，否则取 gold」漏了 type 2 的漏洞（§3.6） |
| `P:server-to-client/NOTI-0x0257.md` | 读出 `u32 + n×(u16,u8,u16)`，与上游 5 字节条目一致 | `+0` 一格把顶层计数与组内 userId 混写；组内 `+3 u16` 标 unknown，上游为持有门票数 |
| `P:server-to-client/NOTI-0x0259.md` | 读出 `u8,u8,u16,u8,u8,u32,u16` = 2 + 10，与上游一致 | **含义错位**：`+1` 才是条目数（页上标 unknown），`+4` 是首条 `cardType`（页上标「条目数」）；`+0` 注释称「上游唯一发送点传 3」已过期，上游发 0/1/3；引用的 `WriteUInt32(rewardType)` 是旧版写法。应改 `patterns/data/meanings/*.tsv` |
| `P:server-to-client/NOTI-0x025A.md` | 读出 `u8,u8,u32,u16,u8,u8` = 10，与上游一致 | **含义错位**：`+8` 是名次（u8），页上标「clearTimeSeconds」；用时在 `+2 u32`。引用行号过期 |
| `P:server-to-client/NOTI-0x0327.md` | — | 页标题写 `OBJECT_DESTROY_REWARD`，机器区与 `protocol-inventory.tsv:1514` 都是 `RAID_MEMBERS` |
| `P:server-to-client/NOTI-0x0391.md` | — | 页标题写 `SKILL_INDEX`，机器区与 `protocol-inventory.tsv:1564` 都是 `OBJECT_DESTROY_REWARD`。任务描述里的「OBJECT_DESTROY_REWARD 0x0327」来自这个错位标题 |
| `P:server-to-client/NOTI-0x0258.md`、`NOTI-0x0273.md`、`NOTI-0x0314.md` | 结构未知 | 需样本 |

上游自测对字节的断言可作旁证（仍是 `UpstreamStatic`）：失败结果帧 25 字节（15 头 + 10 体，`U:SelfTests/A21RaidProtocolSelfTest.cs:1983-1984`）；
空奖励列表 17 字节、单条 27 字节、四条 57 字节，金边在每 10 字节条目的第 4 字节（`:3705-3767`，`:885`）。

---

## 2. 入场费用

### 2.1 规则与数值来源

| 结论 | 来源 |
|---|---|
| PVF `etc/raid/anton.etc`：`[ENTRY COST] 10096296 1`、`[RETRY] 1`、`[RETRY ITEM] 10099897 1`、`[DISCONN MEMBER WAIT TIME] 300`、`[RAID START MINIMUM MEMBER] 10`、`[RAID MEMBER MAX] 20`、`[PHASE TIME OVER] 2400 2400`、`[PHASE BREAK TIME] 600`、`[START DELAY TIME] 3` | `ClientStatic(PVF)` |
| 门票 `10096296`「强力的魔能吸食阻断装置」（安徒恩团队模式入场材料）：`[stack limit] 1`、`[daily purchase limit] charac 1`、`[reset daily purchase item]`、`[daily delete item] 1`、`[impossible contents] charac cargo ban redeemitem`、`[price] 200000`、`[need material] 3341 50`（魔刹石 50） | `ClientStatic(PVF)` |
| 门票在 NPC 326「奈恩·希格」商店 108（`itemshop/naen_raid.shp`）出售；NPC 326 同时是任务 12830 的 NPC、`[WAITING ROOM NPC] 326 327` | `ClientStatic(PVF)` |
| `10099897`「皇室的支援证书」：说明为「灭杀/阻截失败时可以获得」，使用后重置奈恩处门票的每日购买限制；`[action type] [init anton raid try]`、`[use action packet] 1`、`[action usable place] [village] [seria room]`、Lv85 | `ClientStatic(PVF)` |
| 原版的「每天一次」很可能就是靠门票「角色每日限购 1 + 每日删除 + 堆叠上限 1」实现，任务 12830 文案也写「通关次数每天只能进行1次」 | 推断，`Unknown` |
| 上游把门票 ID 与数量硬编码为 `10096296 × 1`，不读 `[ENTRY COST]` | `UpstreamStatic` `U:Game/Raid/RaidEntryCostCommitService.cs:24`、`RaidHandler.Admission.cs:87,115` |
| 上游与第三方都没有实现 `[RETRY ITEM]` 发放和 `[init anton raid try]` 使用效果 | `UpstreamStatic`、`ThirdPartyStatic`（全仓搜 `10099897` 无命中） |
| 只有一阶段开战扣票；二阶段不扣 | `UpstreamStatic` `RaidHandler.Progression.cs:785-839` 对比 `:841-888` |

### 2.2 扣除时机与事务（上游）

1. 团长发 `START_RAID`（`0x029B`，空 body）：先 `HasAllEntryCosts` 检查全员在线、lease 归属当前会话、各持 ≥1 张；不满足回 ACK=0
   并给团长发公告「攻坚队中存在未准备入场材料的队员。」（`RaidHandler.Progression.cs:24-35`，`RaidHandler.Admission.cs:101-122`）。
   这一步**不扣票**。`UpstreamStatic`
2. 进入 3 s 准备倒计时（`[START DELAY TIME]`），期间发 `PREPARE_START_RAID` 等（属招募 agent）。
3. 倒计时到点 `CompleteRaidPreparationAsync`（`RaidHandler.Progression.cs:785-839`）：
   - 团长掉线或小队未就绪 → 取消准备，重发 `0x0257`，日志写明「no materials consumed」（`:788-807`）。
   - 否则在 `RaidManager` 锁内调用 `TryCompletePreparation(expected, commitEntryCosts)`（`U:Game/Raid/RaidManager.cs:589-600`）：
     核对实例、准备代次、团长、状态 0 后，执行回调「小队就绪 && 扣票」，成功才把团切到状态 2。
   - 扣票 `RaidEntryCostCommitService.TryConsume`（`RaidEntryCostCommitService.cs:26-151`）：按角色号排序逐个 `Monitor.Enter(lease.SyncRoot)`，
     复核每个 lease 仍是当前会话的；任何人不足 1 张即失败；要求全员同库；**一个 SQLite 事务**里逐人扣 1 张并 `SaveDirtyInTransaction`，
     全部成功才提交；失败时从库重载所有人的在线背包（`InventoryRollbackRecoveryService.ReloadOnlineInventory`）。
   - 扣票成功后逐人发背包槽刷新（`RaidHandler.Progression.cs:824-827`），随后开战广播。
4. 扣票失败 → 取消准备，重发 `0x0257`（`:809-823`）。

自测覆盖：重复 owner 拒绝、两人一笔提交、第二人失败两人都回滚、失效 lease 不能扣票也不能发奖（`U:SelfTests/RaidPersistenceSelfTest.cs`）。`UpstreamStatic`

### 2.3 退还

| 情形 | 上游 | 第三方 |
|---|---|---|
| 准备阶段失败 | 不扣，无需退 | 同 |
| 开战后超时失败、解散、掉线、服务器重启 | **不退** | 开战扣票的同一事务写 `raid_ticket_refunds`；60 分钟（可配 1–1440）内无二阶段通关则用系统邮件补发 1 张，见 §6 |
| `[RETRY ITEM]` 补购资格 | 未实现 | 未实现 |

`UpstreamStatic`／`ThirdPartyStatic`。原版是否退票：`Unknown`；PVF 只给出「失败给 `10099897`，用它重置限购」这一条线索。

---

## 3. 奖励

### 3.1 资格判定（上游）

- **出战通关集合**：`TryClearDungeon` 在某次进图（participation）首次通关时，把该次进图的全部 `MemberKeys` 并入本团的 `_clearParticipants`
  （`RaidManager.cs:890-944`）。同一 participation 只计一次（`Cleared` 标志）。`UpstreamStatic`
- **阶段奖励资格** = 阶段结束那一刻仍在 `raid.Members` 里、且在 `_clearParticipants` 中的成员（`RaidHandler.Progression.cs:455-458`、`:770-773`）。
  只要本阶段**任意一张图**通关过就有资格，不看通关的是哪张、不看贡献。资格名单在 `PhaseRewardFlow` 构造时冻结（`RaidHandler.Rewards.cs:63-66`）。
- 二阶段开战时清空 `_clearParticipants` 并重置成员阶段计数（`RaidManager.cs:1256-1259`），所以两阶段资格独立。
- **不是**开团冻结名单：上游没有开团名单快照；中途离团或掉线者已从 `Members` 移除，自然无资格。
- **掉线者**：上游会话断开即 `LeaveLocked`（`RaidManager.cs:988-996`、`:1278-1319`），团长掉线或团空即解散整团。所以掉线者无资格；团长掉线时全团连奖励流程一起被清（`RaidHandler.Runtime.cs:372-387`）。
- 无资格者也收到 `RAID_RESULT`，`rewardOption=1`（`RaidHandler.Rewards.cs:347`）；拿不到翻牌列表（列表只含有资格的同小队成员，`:356`）。
- 入账前每次都要求「会话仍是成员登记的那个会话、背包 lease 归它所有」（`RaidHandler.Rewards.cs:866-899`），否则静默返回 false——**奖励丢失，无补发**。

第三方（`ThirdPartyStatic`）：资格分两层——`participant`（通关集合，用于任务、称号、竞拍）与 `reward eligible`（再过一遍次数上限），见 §6。

### 3.2 奖励组成与数值来源

每阶段每名有资格者得三份：**金币牌**（rewardType 0）、**小队道具牌**（rewardType 1）、**个人小队大奖**（REWARD_LIST type 3，PVF 名 `squad_item`）。
PVF 还定义了 `pcroom_card`（网吧牌），上游不发。

PVF `[CLEAR REWARD ITEM]` 每行 `[STATE REWARD] 类型 state 权重 奖池道具 flags`（解析 `UL:Models/RaidEtcFile.cs:254-277`）。奖池道具是 `[upgradable legacy]` 消耗品，
其 `[int data]` 为 `道具 权重 数量` 三元组，道具 0 = 金币（上游 `RaidHandler.Rewards.cs:828-855` 按权重抽一条）。`ClientStatic(PVF)`：

**一阶段「阻截安徒恩」**

| 类型 | state | 权重 | 奖池 | 奖池内容 |
|---|---:|---:|---|---|
| party_card | -1 | 1 | 10094735 | 浓缩的魔能石 ×3 或 ×4 |
| gold | -1 | 1 | 10094732 | 金币 50 000（名「第1阶段C级」） |
| gold | 2 | 1 | 10094734 | 金币 70 000（「第1阶段A级」） |
| gold | 3 | 1 | 10094786 | 金币 80 000（「第1阶段 S级」） |
| pcroom_card | -1 | 1 | 10094733 | 浓缩的魔能石 ×1 |
| squad_item | -1 | 50 | 10157773 | 深渊派对邀请函 100–150 |
| squad_item | -1 | 31 | 10157774 | 浓缩的魔能石 3–5 |
| squad_item | -1 | 3 | 10157775 (flag 1) | 安徒恩传说装备 |
| squad_item | -1 | 1 | 10157776 (flag 1) | 安徒恩传说卡片 |
| squad_item | -1 | 15 | 10157777 (flag 1) | 史诗装备 Lv60–85 |

**二阶段「灭杀安徒恩」**

| 类型 | state | 权重 | 奖池 | 奖池内容 |
|---|---:|---:|---|---|
| party_card | -1 | 65 / 31 / 3 / 1 | 10157778 / 79 / 80 / 81 | 邀请函 100–150 / 魔能石 1–2 / 传说装备 / 传说卡片 |
| gold | -1 / 2 / 3 | 1 | 10094789 / 88 / 87 | 金币 120 000 / 140 000 / 160 000（名「B／A／S级」） |
| pcroom_card | -1 | 1 | 10094733 (flag 1) | 魔能石 ×1 |
| squad_item | -1 | 775 / 145 / 50 / 25 / 5（均 flag 1） | 10157782 / 83 / 84 / 85 / 86 | 史诗 Lv60–85 / 安徒恩的灵魂碎片 / 源助力武器 / 贪食·源助力史诗 / 安徒恩的心脏卡片 |

注意：dev PVF 的 squad_item 与二阶段 party_card 奖池（`101577xx`）和上游代码里的兜底常量（`10094731/37/84/85`、`10094739` 等，
`U:Game/Raid/AntonRaidRewardProvider.cs:22-41`）不同。只要 PVF 能解析，上游用 PVF；兜底只在读不到 `anton.etc` 时生效（`:169-197`、`:223-290`）。

**名次（rank / state）**：`[CONDITION FOR RANK] [DEAD COUNT] 最小 最大 名次`，两阶段都是 `0-1→0`、`2-5→1`、`6-9→2`、`10-14→3`，超出取最大区间（`UL:Models/RaidEtcFile.cs:70-85`）。
死亡数是全团本阶段合计（`RaidManager.cs:628-645`）。上游直接把名次当作 `state` 选奖池（`AntonRaidRewardProvider.cs:139-197`），`RAID_RESULT` 发 `名次+2`（`RaidHandler.Rewards.cs:580-583`）。

> **疑点（`Unknown`，待实机/逆向）**：按上游映射，死亡 0–1 次拿「C级」5 万，死亡 10–14 次拿「S级」8 万，死得越多金币越多。
> 要么 `[DEAD COUNT]` 第三列的含义与上游理解相反，要么 `state` 不等于名次，要么奖池名与面额对应关系另有规则。设计时不要照搬上游映射。

### 3.3 金币与「金边」倍率规则（上游自创）

- `AntonRaidGoldRate`（`U:Game/Raid/AntonRaidGoldRate.cs`）只作用于**二阶段 squad_item**：把每个候选奖池按其内容分类，
  全部条目都在金边白名单里 = 金池，都不在 = 普通池，混合则保持原权重（`:47-56`）；`TargetGoldProbability = 1.0`（`:12`）把普通池权重压到 0（`:16-30`）。
  白名单是硬编码 102 件装备、20 张卡片、1 个灵魂碎片（`U:Game/Raid/AntonRaidGoldRewards.cs:8-38`）。
- 后果：dev PVF 中权重 775/1000 的「史诗装备 Lv60–85」池（10157782）条目不在白名单，**上游永不发出**；二阶段大奖恒为金边。
  这是上游服务器的经营调整，不是 PVF 规则。`UpstreamStatic`
- `0x0259` 的 `flags` 字节（金边）也由白名单按**实际抽中的道具**重算，不用 PVF 的 flags（`RaidHandler.Rewards.cs:617-625`）。
- 金币：牌面 `quantity` 是 u16，超过 65535 时显示 65535，实发全额并用公告补报；撞携带上限时按实际到账额入账并公告（`RaidHandler.Rewards.cs:866-890`、`:918-929`，
  `U:Game/Raid/RaidRewardCommitService.cs:16-28`）。二阶段金币 12 万–16 万必然超过 u16。`UpstreamStatic`

### 3.4 翻牌流程时序（上游）

```text
最后一张必需图通关
 ├─ 一阶段 CompletePhaseOneAsync (RaidHandler.Progression.cs:754-783)
 │    团状态 3、记录用时；关地图；冻结资格名单 → PhaseRewardFlow
 │    等 2 s → 符号 105=1 → StartPhaseOneResultMovieAsync
 └─ 二阶段 CompletePhaseTwoAsync (:444-461)：同上但不等 2 s、无符号 105

StartPhaseOneResultMovieAsync (Rewards.cs:302-325)
  逐成员 NOTI 0x025A(0, phase, 用时, 死亡, 名次+2, option)
  全团 RAID_STATE(3, 0)
  +2 s 若无人报过场结束 → 全团 NOTI 0x0258(0,0)                 (:327-338)

任一客户端 CMD 0x0294 [01]                                       (:267-291)
  ACK(1)；开启翻牌；本人 RAID_STATE(4, arg)
  本人 NOTI 0x0259 type0 + type1（同小队已翻的牌）
  首次时启动自动翻牌定时器 10 s                                  (:363-366, :391-426)

成员 CMD 0x0295 [type, card]                                     (:438-474)
  分配牌位（同小队不可重复，冲突时取最小空位）
  同小队广播 NOTI 0x0259(type, 1 条揭示) → 本人 ACK → 立即入账
  全员两类都翻完 → +2 s → 小队大奖

自动翻牌（首个过场结束后 10 s）
  对所有未翻完者按最小空位先翻 type1 再翻 type0，揭示并入账
  → +2 s → 小队大奖                                              (:476-486)

小队大奖 ShowPhaseOneSquadRewardsAsync                            (:603-653)
  每个有资格者抽 squad_item → 全团 NOTI 0x0259 type3（全部条目）
  等 5 s → 复核实例/阶段/流程未变 → 逐人入账
  → FinishPhaseOneRewardsAfterDelayAsync（名为 delay，实际立即）    (:655-685)
      TryCompletePhase：一阶段 → 状态 5；二阶段 → 状态 4
      逐资格者提交 raid phase clear 任务进度                         (:687-760)
      一阶段：全团 RAID_STATE、SET_TIMER/REMAIN_TIME(600-5=595 s)、ENABLE_CLEAR_DUNGEON，排二阶段自动开战定时器
      二阶段：取消定时器、ENABLE_CLEAR_DUNGEON、清理运行态
```

- 牌的归属：金币牌与道具牌都按 **(小队号, rewardType, 牌位)** 缓存首次抽中的结果（`RaidHandler.Rewards.cs:174-196`），
  同小队成员必须选不同牌位，所以四人各得一张。同一成员两类必须是同一个牌位（`:208-211`）。
- 上游用 `PartyIndex` 分小队；`PartyIndex 0`（未编队）的成员会被当成同一「小队」抢牌位（`:356`、`:409-411`），第三方已改为每人自成一组（§6）。
- 二阶段大奖全团可见；翻牌揭示只给同小队。

### 3.5 重复领取防护（上游）

- 全部在内存 `PhaseRewardFlow`：`_rewardOperations` 位图（bit0 金币、bit1 道具）保证每人每类只记一次，`recordedNow` 为 false 时只回 ACK 不入账
  （`RaidHandler.Rewards.cs:198-228`、`:456-468`）；各阶段性步骤用 `Interlocked` 一次性闸门（`:73-85`、`:230-264`）。
- 流程以 `InstanceId` 为键（`RaidHandler.Runtime.cs:142-147`）；回调复核 `TryGetCurrentRaid` 与流程引用相同（`RaidHandler.Rewards.cs:637-644`）。
- 入账本身**没有幂等键**：`RaidRewardCommitService` 每次就是一笔新的背包提交（`RaidRewardCommitService.cs:25`、`:46-65`）。
  进程重启后流程丢失，不会重复，但也不会补发。
- 自测：「重复点击只回 ACK 不重复入账」「晚到的过场结束只重放已翻开的牌」（`A21RaidProtocolSelfTest.cs:3739-3758`）。`UpstreamStatic`

### 3.6 已知漏洞（`UpstreamStatic` 推断，第三方同样存在）

`SELECT_RAID_REWARD_CARD` 接受 `rewardType == 2`（`RaidHandler.Rewards.cs:516`、`:204`）。`type 2` 的牌不揭示，但仍走
`GrantPhaseOnePartyRewardAsync`：`rewardType==1 ? party_card : gold`，于是按金币奖池再抽一次并入账（`:456-468`、`:585-601`）。
改包客户端每阶段可多领一份金币。第三方 `T:DfoServer.Network.Handlers/RaidHandler.cs:5208-5228`、`:380` 同样放行。
JP86 的 type 2 不产生奖励。A21 可能在一次正常选牌中追加 type 2，兼容应答由实机确认。

### 3.7 卡死风险（上游）

翻牌只有在某个客户端发 `0x0294 [01]` 后才开启，自动翻牌定时器也只在那时启动（`RaidHandler.Rewards.cs:363-366`）。
若所有有资格者都不发（过场被打断、客户端不发），团会一直停在状态 3，奖励与二阶段都不推进；状态 3 期间没有总超时。
第三方补了「无资格者时直接开启」与「掉线者立即代翻」（`T:DfoServer.Network.Handlers/RaidHandler.cs:5012-5016`、`:5116-5138`），仍依赖在线者发过场结束。

### 3.8 竞拍／分配

上游没有竞拍；分配就是 §3.4 的「小队内各翻一张 + 个人大奖」。第三方有自建金币竞拍（§6）。原版 A21 是否有拍卖：`Unknown`。

---

## 4. 任务与次数

### 4.1 `[raid phase clear]` 任务（PVF）

`ClientStatic(PVF)`，`n_quest/quest.lst` 中该类型共 6 个：

| 任务 | 名称 | `[int data]` | 说明 |
|---|---|---|---|
| 12830 | [团队安徒恩]贪食之巨兽 | `0 5 -1 1 5 -1` | 两阶段各 5 次；NPC 326；前置 12829；Lv85–99；选择奖励 5 件「贪食」史诗 |
| 8514 / 8515 | 2015 活动「安徒恩的抵抗者」等 | `0 5 -1` 等 | 活动任务 |
| 6742 / 6743 / 6744 | 称号书 | `1 15 2` / `1 15 1` / `1 15 0` | 二阶段 15 次，分别要求队员 / 小队队长 / 攻坚队长 |

三元组是 `阶段 次数 角色`，角色 `-1` 不限、`0` 攻坚队长、`1` 小队队长、`2` 队员（由 6742–6744 文案对应，`ClientStatic(PVF)`）。

### 4.2 上游实现

- 解析只接受 1–3 组、阶段 0/1 不重复、次数 1..511、**角色必须 -1**（`U:GameWorld/QuestData.cs:479-519`）；所以称号书 6742–6744 不会被推进。
- 每组占一个 9 位触发通道，存剩余次数；阶段完成时对匹配通道减 1（`U:Game/Quests/QuestObjectiveEvaluator.cs:180-220`）。
  旧数据触发值为 1 时先按初始值修复再减（同处 `:200-206`）。
- 提交点：`FinishPhaseOneRewardsAfterDelayAsync` 里阶段状态提交成功之后（`RaidHandler.Rewards.cs:664`）；仅成功阶段
  （一阶段到状态 5、二阶段到状态 4 且 `StateArgument==0`，`:762-767`）；只对冻结的资格名单逐角色调用，失败重试 3 次（`:687-760`）。
- 幂等：事件 ID = `InstanceId` 的第 16 字节异或 `phase+1`（`:769-778`），经 `quest_progress_event_inbox(character_id, activation_id, event_id, event_kind)` 去重
  （`U:Sqlite/item_schema.sql:864-872`，`U:Game/Quests/QuestService.cs:417-445`）。
- 客户端上报：上游把 `raid phase clear` 定为 `EchoOnly`，客户端 `SET_QUEST_TRIGGER` 不能推进（`U:Game/Quests/QuestClientTriggerAuthority.cs:116-117`）。
- 规范原文：`E:\Project\DNF\ServerS4A21\Docs\副本架构业务接入规范.md:867`、设计 `Docs/superpowers/specs/2026-09-22-anton-raid-phase-quest-design.md`。`UpstreamStatic`

### 4.3 次数限制与重置

| 机制 | 上游 | 第三方 | PVF／原版 |
|---|---|---|---|
| 每日入场次数 | 无独立计数；靠门票数量 | 同上 | 门票角色每日限购 1、每日删除（`ClientStatic(PVF)`）；日界按 JP86 既有每日重置 |
| 奖励次数 | 无 | `raid_reward_runs` 按周期计数，默认不限（配置 3 次，周期 unlimited/daily/weekly/monthly，北京时间零点/周一/月初） | `Unknown` |
| 失败重试 | 无 | 无 | `10099897` 重置限购（`ClientStatic(PVF)`），发放时机原文「灭杀/阻截失败时」 |
| 每日奖励任务 | 无 | 自定义任务 21001（dev PVF 中不存在） | — |

---

## 5. 持久化（上游）

上游**没有任何团本专用表**。团、成员、阶段、资格、翻牌状态全在内存（`RaidManager`、`PhaseRewardFlow`），
规范自述「进程内能力，不承诺跨服务端重启恢复」（`E:\Project\DNF\ServerS4A21\Docs\副本架构业务接入规范.md:685`、`:862`）。

| 写入 | 时机 | 载体 | 幂等键 |
|---|---|---|---|
| 扣门票 | 一阶段准备倒计时到点、团切状态 2 之前，`RaidManager` 锁内 | 背包表，全队一个 SQLite 事务（`RaidEntryCostCommitService.cs:80-116`） | 无；靠团状态 0→2 的单次转换 |
| 翻牌金币／道具 | 每次 `0x0295` 首次记录或自动翻牌时，逐人逐类 | `OnlineInventoryMutationCommitCoordinator.TryCommit`，标签 `raid-reward-gold`／`raid-reward-item`（`RaidRewardCommitService.cs:25`、`:46`） | 无；内存位图防重 |
| 小队大奖 | 广播后 5 s，逐人 | 同上 | 无 |
| 任务进度 | 阶段状态提交后，逐资格者 | 任务表 + `quest_progress_event_inbox` | `InstanceId ⊕ phase` 派生事件 ID + activation |

重启／重登语义：重启后团消失，门票不退，已入账的翻牌保留，未入账的丢失；任务进度已提交的不会重复。
掉线即离团，重登后不是团员（`REJOIN_RAID` 的行为属招募 agent）。完成阶段后回城可重放城镇 UI（`RaidManager.TryGetCompletedTownReturn`，
自测 `A21RaidProtocolSelfTest.cs:2635-2678`），这是读内存，不是持久化。`UpstreamStatic`

团本地下城本身：`DungeonRewardPolicy.AntonRaid` 关闭怪物掉落与任务掉落，保留经验、任务进度、通关提交与结算（`U:Game/Dungeon/DungeonRewardPolicy.cs:24-32`、`U:GameWorld/DungeonRewardPolicyData.cs:22-23`）。

---

## 6. 第三方商业端差异（`ThirdPartyStatic`）

路径均在 `T:` 下。

| 主题 | 差异 | 位置 |
|---|---|---|
| 奖励配置 | 可选本地 JSON `anton-raid-rewards.config.json`（可从 PVF 导入、默认关闭），含奖池、权重、名次区间、`MinimumStartMembers`、`AutoReturnToTownOnClear`；校验名次 0..3 区间连续、gold 池只能出金币 | `DfoServer.Game.Raid/AntonRaidLocalRewards.cs` |
| 奖励次数上限 | `raid_reward_runs(character_id, raid_instance, first_awarded_ms)`，主键 (角色, 实例)；结算开始时 `CanReward` 预判，**入账事务里** `TryClaim` 加锁复核并插入；同一实例两阶段只占一次 | `DfoServer.Game.Raid/RaidRewardLimitConfiguration.cs`、`RaidRewardLimitRepository.cs`、`DfoServer.MySql/MySqlRaidRewardLimitSchema.cs`；`DfoServer.Network.Handlers/RaidHandler.cs:4853-4880`、`:5635-5716` |
| 超限提示 | 有通关但超限者 `rewardOption=1`，另发公告「本角色当前周期的安徒恩翻牌奖励次数已用完…」 | `RaidHandler.cs:5030-5046` |
| 门票补发 | 扣票同事务写 `raid_ticket_refunds(raid_instance, character_id, started_ms, due_ms, status 0..3, settled_ms, retry_after_ms)`；二阶段通关时对本阶段有通关的成员标 1；到期未通关的由 5 s 轮询经系统邮件补 1 张，幂等键 `anton-ticket-refund:{instance}:{cid}`，失败 30 s 后重试 | `RaidEntryCostCommitService.cs`、`RaidTicketRefundRepository.cs`、`RaidTicketRefundConfiguration.cs`、`DfoServer.Network.Handlers/RaidTicketRefundScheduler.cs`、`DfoServer.MySql/MySqlRaidTicketRefundSchema.cs` |
| 掉线保留 | 读 PVF `[DISCONN MEMBER WAIT TIME]`（300 s）保留掉线成员；掉线者的牌立即自动代翻 | `RaidHandler.cs:2696-2717`、`:5116-5138` |
| 掉线者入账 | 仍要求在线会话与 lease，掉线者的奖励**入账失败、无邮件补发** | `RaidHandler.cs:5349-5372`、`:5635-5641` |
| 翻牌窗口 | 每人单独「窗口已开」才能翻；自动翻牌按人 10 s | `RaidHandler.cs:146-243`、`:5095-5114`、`:5181-5234` |
| 小队分组 | `PartyIndex 0` 每人一组（`65536+userId`） | `DfoServer.Game.Raid/RaidRewardPartyLayout.cs` |
| 晚到重放 | 已完成阶段的奖励快照保留（≤128 个、有过期），晚到的 `0x0294/0x0295` 重放 | `RaidHandler.cs:4882-4936` |
| 阶段任务 | 不用 inbox 去重，直接改触发值（**非幂等**）；阶段允许 0..2；一阶段附带「沙之通行证」记录 | `DfoServer.Game.Quests/RaidPhaseClearQuestProgressService.cs` |
| 每日奖励任务 | 自定义任务 21001（二阶段通关 1 次），在奖励入账同一事务内用 `raid_daily_reward_{instance}` 日标记清零通道 | `DfoServer.Game.Quests/RaidDailyRewardQuestProgressService.cs` |
| 称号书 | 按角色 0 团长 / 1 小队队长 / 2 队员推进称号成就，回执 `raid-title:{instance}:{phase}` 写 `inventory_operation_journal` 防重 | `DfoServer.Game.TitleBook/RaidTitleAchievementService.cs`、`RaidHandler.cs:5481-5536` |
| 竞拍 | 二阶段完成后对通关参与者开金币竞拍：3–8 件拍品、每件 30 s、出价延时 15 s、手续费 5%、托管金、邮件交付；复用 NOTI `0x03B9`（ROGER_LEVINE_AUCTION_BIDDING）与公告 | `DfoServer.Game.RaidAuction/*`、`DfoServer.Network.Handlers/RaidAuctionHandler.cs`、`RaidHandler.cs:5446-5459` |
| 扣票日志 | 扣票写玩家日志「团本/副本扣除」 | `RaidEntryCostCommitService.cs` |

第三方的竞拍、次数上限、补票、21001 都是商业端自建功能，PVF 和客户端里没有对应依据。

---

## 7. 对 JP86 设计的影响

### 7.1 结合点

| 需求 | JP86 现有能力 | 说明 |
|---|---|---|
| 门票每日 1 张、每日删除 | NPC 商店限购（`Server/src/Dfo.Server.Infrastructure/Content/PvfNpcShopDefinitionSource.cs:21`，`Document/NpcShop-20260909.md:14-15`）、物品每日删除期限（`Document/ItemExpiration-20261014.md:13`） | 商店 108 已能卖票。先实机确认 `[reset daily purchase item]` 在 JP86 是否按日界重置 |
| 扣票 | 背包领域、单一执行器 | 全团一次扣票。JP86 一个 SQLite 库就是一个事务参与者（`Document/GameplayExecution.md`「领域事务与存储载体」），全团同库可做成一笔。但同文写明「不能把选角、补偿或逐成员结算合成一笔事务」，所以全团扣票是否算一个业务事实要先定，见 §7.3 |
| 失败补票／`10099897` | 邮件领域 `delivery_key`（`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:435`，`MailboxDomainService.RevokeSystemDeliveryAsync` 按 key 核对） | 若实现补票或失败发 `10099897`，用 `raid:{instanceId}:{cid}` 一类键投递；离线也能收 |
| 翻牌金币／道具入账 | `ICardRewardDomainService`（`Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs:111`）、`IGoldLimitRuleService`／`IGoldCarryLimitSource` | 普通副本翻牌是「结算时固定牌面、逐位置领取」，团本是「小队四张共享牌、成员各取一张」。可以复用牌面固定与入账，小队共享层要在攻坚实例里 |
| 离线／掉线者奖励 | `dungeon_persistent_effect_outbox(source_event_id, effect_kind, …)`、`domain_events`／`domain_event_deliveries` | 上游掉线即丢奖励。JP86 若保留团员身份（设计草案已决定），资格名单里的离线者应改走 outbox 或邮件，事件键用 `(instance, phase, cid, kind)` |
| 阶段任务 | `QuestObjectiveAuthority` | **冲突**：JP86 把 `raid phase clear` 列为客户端计数（`Server/src/Dfo.Server.Application/Quests/QuestObjectiveAuthority.cs:61-71`），上游当前版本是 `EchoOnly`。实现团本前必须改为服务端计数，否则客户端可自报完成 |
| 任务幂等 | JP86 是否有任务进度事件 inbox：本次未查到同名表 | 需要「(角色, activation, 事件)」去重；事件 ID 用 `攻坚实例 ID + 阶段` |
| 定时推进 | `DungeonDeadlineScheduler` 的模式（`Document/DungeonRunKinds.md`「一局之内的到点推进」） | 结算各段延时（2 s、10 s、5 s）和间歇 595 s 由攻坚实例持有截止时间、按代次核对；攻坚实例跨多局，不挂在某个 `DungeonRun` 上，需要单独的截止时间来源 |
| 结算时机 | 设计草案「安图恩实例服务」 | 资格名单在进入结算态时由实例冻结，与「开团冻结名单」「出战名单」分开保存，草案已预留 |

### 7.2 事务边界建议

1. **扣票**：在「准备完成 → 开战」这个状态转换里做，转换与扣票同成同败。全团一笔（上游做法）或逐人扣、失败逐人退（退走背包加回或邮件）二选一，需裁定。
2. **翻牌**：每次「记录这一类牌 + 入账」是一笔，先在攻坚实例记录「已领」再提交背包，提交失败要能回滚记录，避免内存说已领而库里没有。
   建议给每笔入账一个幂等键 `(instance, phase, cid, rewardType)`，使重启或重放不会重复，也能补发。
3. **小队大奖**：抽签结果在广播前固定（上游先抽后广播、5 s 后入账）；入账逐人一笔，键 `(instance, phase, cid, squad)`。
4. **阶段完成**：「团状态推进 + 任务进度」不必同一事务，但任务进度必须幂等，且只在状态推进成功后提交。
5. 所有结算回调都核对攻坚实例 ID、阶段与流程代次（上游用 `InstanceId` + 流程引用，第三方再加准备代次）。

### 7.3 待裁定的冲突与取舍

- 资格口径：上游「本阶段任意一张图通关过的现团员」。是否沿用，还是只算二阶段打心脏的小队，或给全团员？需要原版资料。
- 掉线者：上游掉线即无奖励；JP86 草案保留团员身份，则需决定离线团员是否保留阶段奖励资格、怎么投递。
- 名次与金币奖池映射疑点（§3.2）：先查清再定。
- 二阶段大奖「100% 金边」是上游私改，JP86 默认按 PVF 权重。
- `pcroom_card`：不实现，并拒绝 `rewardType 2`。
- 是否实现 `[RETRY ITEM]` 与补票：PVF 有依据的是前者。
- 翻牌开启依赖客户端 `0x0294 [01]`：需要服务端兜底（例如结算开始后固定时限自动开启），避免卡在状态 3。

交界：招募编队、开战准备（`0x029B`、小队就绪）、地图开闭与机制、`REJOIN_RAID` 归其他 agent；本文只用到「准备完成」「阶段完成」两个事件作为入口。

---

## 8. 未知与待实机项

| 项 | 为什么需要实机 |
|---|---|
| `0x0259` 各字段含义与客户端行为，包括 type 2、type 3 的 UI 与条目内标志 | [客户端分支](../../../../protocol/game/server-to-client/NOTI-0x0259.md) 已确认读 0/1、2、3，仍需对照画面 |
| `0x025A` `rank` 的 +2 偏移、`rewardOption` 的 UI 效果 | 上游自测只断言自己的字节 |
| `0x0258` 实际发送长度、触发画面及 `0x0294` 两种请求的线上时序 | [提示与回调](../../../../protocol/game/server-to-client/NOTI-0x0258.md) 已有静态证据，另有发 `[00]` 的本地输入路径 |
| 原版选牌分支的 type 2 启用条件与响应反馈 | [选牌入口](../../../../protocol/game/client-to-server/0x0295.md) 已证同一次选择发 1、0，条件追加 2 |
| `0x0314 ALEADY_RAID_REWARD` 是否需要、何时发（例如重复领取或次数用尽） | 两个参照服务端都没用；handler 像通用桩 |
| `[DEAD COUNT]` 第三列与金币 `state` 的对应关系 | §3.2 疑点 |
| 原版资格口径、掉线者是否得奖、是否退票 | 参照端都是自行决定 |
| `[RETRY ITEM]` 何时发放、`[init anton raid try]` 使用时客户端发什么包 | PVF 有 `[use action packet] 1`，协议未知 |
| 商店 108 的每日限购在 JP86 是否已按日界重置；门票每日删除是否生效 | 用已有商店与期限实现实测 |
| 过场结束到翻牌窗口、大奖展示的真实时长 | 上游的 2 s／10 s／5 s 是经验值 |
| 二阶段完成后的终局：团是否自动解散、何时可回城 | 上游停在状态 4、等成员自行离团 |
