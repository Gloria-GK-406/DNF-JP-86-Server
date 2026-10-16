# 婚姻一期：参考实现与当前服务端接缝调查

日期：2026-10-16
性质：只读调查；未改生产源码、数据库、PVF、客户端或运行服务
当前仓基线：`DNF-JP-86-Server` `task/next-round-20261013` @ `b4212fced67aba7e0d0cdcc0dc534fcf8d4a4c63`

## 1. 结论先行

1. **[UpstreamReference / Confirmed] 在所列 `ServerS4A21` checkout 与本次源码扫描范围内，未发现可复用的婚姻关系实现。**它有 A21 婚姻 opcode 枚举和三帧固定“婚礼回放”，但包体不读取角色、配偶、关系或数据库状态；本次范围未定位到求婚、接受/拒绝、婚礼完成、离婚、关系恢复的 handler、领域状态机或持久化表。它适合作为固定 UI 回放样本，不能当婚姻业务实现。
2. **[UpstreamReference / Confirmed] 在所列旧单体 `E:\Project\86JP` checkout 与本次源码扫描范围内，未发现婚姻状态机。**本次关键词命中只有 opcode 枚举和商城婚戒商品注释/自测；未定位到婚姻业务代码或表结构。
3. **[70Reference / High] 指定 70 版 `df_game_r` 的 ELF 函数符号和可打印 ASCII 在本次词表扫描中零命中。**这是边界明确的负证据，不证明匿名符号、非 ASCII、间接调用或其他语言命名的代码不存在。
4. **[CurrentImplementation / Confirmed] 在当前仓所列 checkout 的生产源码扫描范围内，未发现婚姻领域、关系表、婚姻 handler 或持久化投影。**协议机器记录误把 `0x03F0/0x03F1` 的 21/22 字节总帧长度当成 body，且没有实帧；新增 A21 静态取证已确认真实 body 是 7/8B，并拆为 ID/value、action 和 opaque/outcome 字段，详见 [A21 专项报告](../a21/a21.md)。字段业务语义仍是部分已知，不能再概括为“字段均未知”。新增 PVF 取证还确认了 Wedding quest 链、最低等级 30、`[job][all]`、三档金币/礼包和离婚费用等内容规则；性别规则、婚礼 tuple 末列语义及正常协议实帧仍 Unknown，详见 [PVF 专项报告](../pvf/pvf.md)。
5. **[CurrentImplementation / Confirmed] 当前事务、双账号锁、会话身份、邮件和 durable domain event 提供了一期所需的关键基础。**应复用 `IMultiAccountCommand` 和现有命令管线，不另造婚姻专用锁；现有 effect settlement 只绑定一个 primary recipient，不能直接承担双方扣费/发奖，仍需专用多收件人编排。
6. **[Proposal / High] 一期服务端策略建议建立持久关系聚合，再把协议和奖励接到它上面。**建议最小状态为 `Pending -> AcceptedAwaitingCeremony -> Completed`，拒绝、取消、过期进入终态；婚礼完成在一个双账号事务中原子完成关系、费用和奖励记账，在线通知在提交后发。这是待实现模型，不是当前实现事实或官方规则结论。
7. **[CurrentImplementation / Confirmed] `0x03D8` 现在存在会伤害婚姻一期的语义冲突。**A21 将它命名为 `COUPLE_ROOM`，而当前仓仍按 A12 增率彩票的 204 字节布局编码。空态 204 个零曾与参考入场逐字节一致，但这不验证非零彩票载荷。
8. **[Proposal / High]** 一期服务端策略可以暂缓非空婚房功能，但必须禁止在 A21 上发送非零旧彩票形状；由专门的 `COUPLE_ROOM` 空态兼容投影保留已有 WireCaptured 的 204 零入场体。

## 2. 证据边界与基线

| 标签 | 调查对象 | 基线 | 本报告可以证明什么 |
|---|---|---|---|
| `UpstreamReference` | `E:\Project\DNF\ServerS4A21` | `c20dba5fa52c2faf4b5087a3b477676be8434e8f` | 当前检出的固定回放、旧彩票实现和缺失的婚姻业务面 |
| `UpstreamReference` | `E:\Project\86JP` | `25e6f0af600b32e550a9126fff5758b602f42ce4`，本地落后 remote 231 commit | 当前本地旧单体源码里可见的代码；不代表 remote 最新状态 |
| `70Reference` | `E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r` | SHA-256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7` | 符号表和 ASCII 的限定扫描结果 |
| `CurrentImplementation` | 当前仓 | `b4212fced67aba7e0d0cdcc0dc534fcf8d4a4c63` | 当前可复用接缝和缺口 |

标签含义：`CurrentImplementation`、`UpstreamReference`、`70Reference` 表示证据所在层；`Proposal` 表示服务端设计建议而非现状。置信度中，`Confirmed` 是源码/工件直接显示的事实；`High` 是由多处一致证据支持的结论或策略建议；`Hypothesis` 是待产品规则、PVF 或实机证据确认的假设。

## 3. `ServerS4A21`：固定回放，不是婚姻实现

### 3.1 实际存在的代码

- **[UpstreamReference / Confirmed]** `Server/DfoServer/Network/Core/PacketTypesA21.cs:980-994,2239-2253` 只定义 A21 婚姻/婚房的 NOTI 和 CMD 标识，包括 `WEDDING_REQUEST 0x03CD/0x03F0`、`WEDDING_RESPONSE 0x03CE/0x03F1`、`WEDDING_INFO 0x03D0`、`WEDDING_CEREMONY 0x03D4`、`COUPLE_ROOM 0x03D8`。
- **[UpstreamReference / Confirmed]** `Server/DfoServer/Network/Builders/Init/WeddingReplayBodyBuilders.cs:7-53` 的三段内容全部是常量：
  - `WEDDING_INFO`：15 字节固定 hex；
  - `WEDDING_CHARAC`：31 个零；
  - `COUPLE_ROOM`：113 字节固定 hex。
  `TryBuild` 不读取 `snapshot`，因此不能表达“这个角色与谁结婚”。
- **[UpstreamReference / Confirmed]** `Server/DfoServer/Network/Builders/Init/InitPacketBuilderRegistry.cs:94-109,118-126` 同时注册 A12 彩票形状的 `LotteryBufferBodyBuilder` 和婚礼回放；同一 `0x03D8` 的第 0 次 occurrence 走彩票 builder，第 1 次 occurrence 被特殊改成 `CoupleRoomBodyBuilder`。
- **[UpstreamReference / Confirmed]** `Server/DfoServer/Game/SelectCharacter/NewCharacterInitSequence.cs:105-109` 选角尾部无条件加入三帧回放。`Server/DfoServer/Network/Handlers/InventoryRefreshSender.cs:148-170` 在换宠、进本、回城等刷新点再次无条件补发同一组常量。注释称其恢复结婚图标和婚房家具属性，这只能证明该固定 UI 效果曾被使用，不能证明服务器知道真实关系。
- **[UpstreamReference / Confirmed]** `Server/DfoServer/SelfTests/A21StartupProtocolSelfTest.cs:91-103` 只验证初始化顺序，没有按角色关系、配偶或重登状态断言内容。

### 3.2 本次 checkout 与扫描范围未找到的能力

在所列 commit、排除备份目录并按本次婚姻业务词扫描后，命中仅剩上述回放/注释和商品资料；本次范围未发现：

- 求婚请求/响应 handler；
- 待处理求婚、接受/拒绝/超时状态；
- 双方关系唯一性约束；
- 婚礼完成、扣费、奖励事务；
- 配偶查询或重登按关系重建；
- 婚姻/参与者数据库表。

因此结论是 **stub/replay**，不应从它推断一期领域规则。

## 4. 旧单体 `E:\Project\86JP`

- **[UpstreamReference / Confirmed]** `Server/DfoServer/Network/Core/PacketTypes.cs:874-888,1963-1977` 只有旧版本 wedding/couple-room opcode 枚举。
- **[UpstreamReference / Confirmed]** 其他命中为 `Game/Shop/CeraShopProductCatalog.cs:86` 的婚戒礼包注释和 `SelfTests/CeraShopSelfTest.cs:59` 的商品检查，不是关系实现。
- **[UpstreamReference / High]** 本次本地源码扫描未发现婚姻表、repository、handler 或状态机。由于该 checkout 落后 remote 231 commit，这个负结论只适用于所列 commit。

### 4.1 可对照但不可移植的旧 `0x03D8` 彩票代码

`ServerS4A21`/旧单体真正有状态的 `0x03D8` 是旧增率彩票，而不是婚姻：

- `Server/DfoServer/Game/Lottery/IncreaseChanceLotteryProgressRepository.cs:35-66,69-115` 从 `account_increase_chance_lottery_progress` 读取并保存账号级 `item_template_id/reward_index`；
- `Server/DfoServer/Network/Builders/IncreaseChanceLotteryPacketBuilder.cs:10-55` 固定构造 204 字节，非空时 offset 0 写 `i32 2`，后接当前项和最多 8 个 24 字节记录；
- `Server/DfoServer/Network/Handlers/LotteryItemResponseSender.cs:67-75,127-154` 在开罐后发送非空、空或清除态 `0x03D8`；
- `Server/DfoServer/Network/Builders/Init/LotteryBufferBodyBuilder.cs:28-37` 在选角从账号进度重建它。

这解释了当前仓历史来源，但 **[High] 不构成 A21 非零 `0x03D8` 的线证据**。

## 5. 指定 70 版二进制

### 5.1 扫描结果

脚本：[collect_70.py](collect_70.py)；输出：[70-bounded-scan.txt](70-bounded-scan.txt)。

- ELF 函数符号条目：119,333；唯一函数地址：97,021；
- 词表：`marri|wedding|spouse|couple|proposal|propose|engage|divorc|ceremon|lover|love point|magadha`；
- 函数符号命中：0；可打印 ASCII 命中：0。

### 5.2 正确解释

**[70Reference / High]** 未找到可定位的英文命名婚姻函数或 ASCII 文本。
**[70Reference / Confirmed]** 该扫描没有做控制流恢复、协议号交叉引用、非 ASCII/本地化资源分析或匿名函数语义识别。
所以不能写成“70 版绝无婚姻实现”；只能写成“现有符号/ASCII 证据没有提供参考实现”。

## 6. 当前仓协议现状

### 6.1 有静态结构，缺业务语义和实帧

- **[ClientStatic / Confirmed]** `protocol/game/client-to-server/0x03F0.md:20-58` 的机器记录把 21 字节总帧长度误作 body；[A21 专项报告](../a21/a21.md) 已确认真实 body 为 7B：`u32 id/value + u8 action + u8 opaque + u8 opaque`。action 已见 `1/2/5/6/7`，业务名称仍部分 Unknown，且尚无实帧。
- **[ClientStatic / Confirmed]** `protocol/game/client-to-server/0x03F1.md:20-58` 同样把 22 字节总帧长度误作 body；真实 body 为 8B：`u32 id/value + u8 action + 2B opaque + u8 outcome`。接受、拒绝、取消与 action/outcome 的完整映射仍需双客户端实帧约束。
- **[ClientStatic / Confirmed]** 现有协议机器记录仍只给出部分字段下界；[A21 专项报告](../a21/a21.md) 已进一步确认 CMD `03F0/03F1/03F7` 回包分别固定消费 11/12/4B，NOTI `03CD/03CE/03D0/03D4/03D7` 分别一次读取 11/12/15/79/68B。字段业务语义、间接/变长部分和真实时序仍需实帧补全。
- **[CurrentImplementation / Confirmed]** 在当前 checkout 的生产源码扫描范围内，未定位到这些婚姻消息的 decoder、encoder、handler 或领域服务。

### 6.2 `0x03D8` 冲突必须先隔离

- `protocol/game/server-to-client/NOTI-0x03D8.md:102-112` 记录：A21 表把 `0x03D8` 叫 `COUPLE_ROOM`，参考入场曾发送 204 个零；当前空彩票 encoder 也恰好产生 204 个零。
- `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs:410-416` 仍把它实现为 `LotteryProgressStateMessage`；`SelectedCharacterStateMessageFactory.cs:111` 在选角总是加入该消息。
- `StateSynchronizationPayloadCodec.Entry.cs:44-74` 可编码非零旧彩票记录；`SelectCharacterPayloadCodecTests.cs:230-245` 甚至覆盖了 offset 0 为 `2` 的非零体，但 `protocol/catalog.md:1052` 只把它列为“仅证明 encoder 可产生该 identity”。
- 全仓生产源码仅 `SelectedCharacterStateMessageFactory.cs:111` 构造该消息；当前 `LotteryOpeningSlice` 不在开罐后发送它。故账户旧彩票表非空时，危险载荷的生产发送面目前是选角/重登。

**[ClientStatic / Confirmed]** [A21 专项报告](../a21/a21.md) 已确认 `COUPLE_ROOM` 的消费结构为 `u32 count + count * 6B + 13B tail`，即 `17 + 6n`；每个 6B 条目按 `u16 + u32` 消费。204 个零只相当于 `count=0` 的空房兼容体：客户端消费前 17 字节并容忍 187 字节尾随。旧彩票非空体 offset 0 的 `2` 会被当成两个家具条目。这与当前源码的非零 encoder 形成直接风险。

因此：

- **[WireCaptured / Confirmed]** 204 零已有真实 A21 入场帧；静态 parser 将其解释为空条目和零 tail，但本次没有新增 ClientVerified 证据；
- **[ClientStatic / Confirmed]** A21 婚房 handler 的 framing 是 `17 + 6n`，条目宽度和读取类型已经闭合，不再把“非空形状”整体列为 Unknown；
- **[High]** 非零彩票 `0x03D8` 会进入错误的 A21 婚房 handler；
- **[Unknown]** 6B 条目的业务语义、13B tail 中房主/配偶等字段含义、合法非空婚房的正常实帧和服务端取值；
- **建议：**一期不实现婚房；在 A21 发送路径把 `0x03D8` 固定为空婚房兼容体或暂不承载旧彩票进度，绝不继续发送非零旧彩票形状。

### 6.3 当前彩票闭环不能替 `0x03D8` 背书

- `LotteryOpening/LotteryOpeningSlice.cs:35-65,98-113` 的实机开罐结果是 CMD `0x001B`，随后投影绝对库存；mode 1 另发 CMD `0x036F` premium snapshot。
- `StateSynchronization/LotteryCounterExperiment.cs:5-18` 的 NOTI `0x0353` 是 2 字节受控 UI 计数实验；`protocol/game/server-to-client/NOTI-0x0353.md:55-75` 明确它控制购买确认提示，不是服务端额度或持久进度，当前部署固定 `00 00` 且开罐后不重发。
- `protocol/game/server-to-client/CMD-0x001B.md:32-49` 的普通/随机/双倍开罐验收只证明 CMD 结果、奖励和 036F 额度闭环。

**[CurrentImplementation / Confirmed]** 当前没有可以称为 A21“持久增率彩票进度”的 NOTI；不得把普通开罐 ClientVerified 外推为非零 `0x03D8` 已验证，也不得猜测迁移到 `0x0442`。

## 7. 当前可复用接缝

| 能力 | 证据 | 一期用法 |
|---|---|---|
| 身份 | `AccountId`、`CharacterId`；`CharacterTables.cs:88-90,136-137` 提供 owner 和 identity 查询 | 关系以 `CharacterId` 为主体，同时保存/校验 owner `AccountId` |
| 精确会话 | `ISessionPresence.cs:10-28` 可按账号、角色、wire user、connection 查询；`OnlineUserModels.cs:14-40` 带 connection 和 generation | 发起/接受/仪式前后都验证 exact selected session，拒绝旧连接重放 |
| 双账号锁 | `CommandContracts.cs:58-62` 的 `IMultiAccountCommand`；`AccountExecutionBehavior.cs:23-49` 去重并按 account id 升序加锁、逆序释放 | 所有双人变更都实现 `IMultiAccountCommand`，复用现有死锁规避 |
| 单写事务 | `TransactionalExecutionBehavior.cs:3-35` 在 SQLite 部署中串行事务；`UnitOfWorkBehavior.cs:6-70` 管理 commit/rollback | 关系、费用、奖励记录在同一 command transaction 中落地 |
| 事务参与者 | `DomainTransaction.cs:7-15,85-155` 先 prepare、唯一可失败 commit，再执行 after-commit callback | 不在提交前向客户端宣告婚礼成功 |
| Durable event | `DomainEvents/EventService.cs:20-36,56-66` 在可写事务中持久化并提交后唤醒；`DomainEventDispatcher.cs:25-105,217-223` 有串行分组和重试 | `MarriageCompleted`、`ProposalAccepted` 等用于提交后通知/奖励投影 |
| Effect 管线 | `Effects/DomainEffects.cs:18-28` 提供统一 validate/apply；`EffectOperationContext.cs:55-76,115-133` 把 primary settlement 与一个捕获的 recipient 严格绑定 | 单收件人的扣费/入包可评估复用现有 effect；双人结算不能视为直接复用，必须设计并验证专用多收件人编排 |
| 离线邮件 | `IMailboxRepository.cs:14-30`、`MailboxDomainService.cs:28-92` 可向角色插入资产邮件并发 `MailCreated` | 适合离线奖励投递；需额外用 marriage/source key 保证只发一次 |
| 邀请模式 | `PartyService.cs:18-24,117-210` 有 30 秒邀请、self/stale/expiry/generation 校验 | 借它的校验模式，不复用其内存字典作持久求婚存储 |
| 时间/后台循环 | party 和 domain-event dispatcher 都注入 `TimeProvider`；`DomainEventDispatcher.cs:189-223` 的 hosted loop 用于事件重试 | 可以借定时循环形状；当前没有通用的 durable 婚姻到期调度器，建议事务内惰性过期并按需增加可恢复 sweep |
| Quest 内容 | `QuestContent.cs:43-66`、`PvfQuestDefinitionSource.cs:77-100` 识别 `Wedding` grade；`QuestAvailabilityRuleService.cs:188-210` 有意不把它放普通可接列表 | [PVF 专项报告](../pvf/pvf.md) 已确认 20737→20738 的双人前置链；它是内容规则输入，不能代替持久婚姻状态 |

PVF 还确认当前选中的 WestCoast/town graph 没有 NPC 20013，旧 Hendon Myre 才放置该 NPC；教堂图含 NPC 20014 但未接入当前 town graph。**[PVF / Confirmed]** 这证明当前地图图谱尚未提供可直接到达的既有入口；**[Unknown]** 它不证明 A21 求婚 UI 必须经 NPC，也不证明教堂不能由协议动态进入。另需注意：当前普通 offer 列表排除 Wedding grade，而通用接任务路径未完整校验 Wedding 的等级和前置，若原生客户端能直接发请求，存在绕过内容门的实现缺口。

Effect 复用有一条关键边界。`EffectOperationContext.BeginMain` 在 `EffectOperationContext.cs:55-76` 要求主 command 的 `Session` 等于网络操作捕获的 recipient；`Record` 在 `:123-133` 又要求 effect owner 的 `Session/AccountId/CharacterId` 全部与该 recipient 一致。`IMultiAccountCommand` 只负责两个账号的稳定加锁，不会放宽这个单 recipient 投影约束。因此，婚礼中同时向双方扣费或发奖不能简单地在一个请求里给双方各套一个新 context；需要专门的多收件人事务编排、每方独立且提交后才可见的 replay/notification projection，以及覆盖第二收件人失败和重放的测试。

### 7.1 待新增的领域边界（Proposal）

- marriage 数据 DTO/ID/状态；
- `IMarriageRepository`/table，支持按角色查 active/pending 关系及带 version 的条件更新；
- `IMarriageDomainService`，负责提案、答复、取消/过期、婚礼完成；
- 协议 decoder/encoder/handler 和错误映射；
- 提交后 notification projection；
- 奖励幂等键/发放记录；
- 重登时婚姻摘要恢复消息。恢复用哪个 A21 包、各字段怎么编码仍需抓包。

不要让 Application 引用 Infrastructure；接口进 Abstractions，SQLite 实现在 Persistence/Infrastructure，遵守当前分层。

## 8. 一期最小关系模型（Proposal）

### 8.1 状态

建议状态：

```text
Pending
  ├─ accept ─> AcceptedAwaitingCeremony ── complete ─> Completed
  ├─ reject ─> Rejected
  ├─ cancel ─> Cancelled
  └─ expire ─> Expired
```

- **[Proposal / High]** 服务端策略建议只有 `Completed` 算配偶关系；`AcceptedAwaitingCeremony` 只表示双方同意。
- **[Proposal / High]** 服务端策略建议终态保留最小审计信息和 request id/version，以支持重放、故障恢复和 GM 调查。
- **[Proposal / Hypothesis]** 求婚有效期、是否允许接受后取消、是否需要仪式场景/道具仍需结合实机与产品规则决定；PVF 已给出婚礼档次和时长候选，不等于求婚超时规则。

### 8.2 表和约束

推荐 `marriage_relation` + `marriage_participant`，而不是只在角色表互写 spouse id：

- relation：`id`、`state`、`version`、发起/接受/完成/终止时间、`expires_at`、`request_id`；
- participant：`relation_id`、`character_id`、`account_id`、`role`、`active`；
- `UNIQUE(relation_id, role)`、`UNIQUE(relation_id, character_id)`；
- 对 `active=1` 的 `character_id` 建 partial unique index，防止角色跨 proposer/target 两列重复进入有效关系；
- relation 用 canonical pair（`character_low < character_high`）及唯一键阻止同一对重复；
- 接受/完成用 `WHERE id=? AND state=? AND version=?` 条件更新，零行即 stale/replay。

`active` 覆盖哪些状态是产品选择。安全基线建议至少覆盖 `AcceptedAwaitingCeremony` 和 `Completed`；若要求“一人同一时刻只能有一个待处理求婚”，也覆盖 `Pending`。

### 8.3 最小校验规则

| 规则 | 建议 | 证据等级 |
|---|---|---|
| 同角色 | 必须拒绝 | `Proposal / High`：二元关系基本不变量 |
| 同账号不同角色 | 一期拒绝 | `Proposal / Hypothesis`：防自刷且符合双账号事务模型；参考代码没有官方规则证据 |
| 已完成/已接受的多婚 | 必须拒绝，DB unique 作最后防线 | `Proposal / High` |
| 多个 pending | 建议一期也限制每角色一个，简化 UI/重放 | `Proposal / Hypothesis` |
| 性别 | 不硬编码 | `Unknown`：PVF 未见性别限制字段，不能据此推断允许或禁止同性 |
| 职业 | 按 PVF `[job][all]` 不限制 | `PVF / Confirmed` |
| 等级 | 按 PVF 最低等级 30；上限与角色等级有效域一并核对 | `PVF / Confirmed`（任务 20737/20738 为 Lv30-99） |
| 双方在线 | 一期服务端策略建议发起、接受、婚礼完成均要求双方 exact current session | `Proposal / High`：最利于保证一期 UI 和并发；官方离线规则未知 |
| 拒绝/过期后的离线恢复 | 建议持久状态可查，在线通知尽力投影 | `Proposal / High` |

### 8.4 原子性和幂等

每个变更 command：

1. 解析角色到两个账号；
2. 以 `IMultiAccountCommand.AccountIds` 交给现有排序锁；
3. 事务内重读双方 owner、关系状态/version、资格和 exact session；
4. 婚礼完成时在同一事务内扣费、把关系改为 `Completed`、写 reward/outbox 幂等记录；
5. commit 后再向双方投影成功通知；失败不得展示已完成；
6. 重复 request id 或同 version 重放返回同一已提交结果，不重复扣费/奖励。

奖励如果直接入包，需要两个角色资产变化都加入同一事务；如果通过邮件发，`MailboxDomainService` 本身可以事务化插入，但当前 mailbox schema 没有 marriage source unique key，因此应先建立 `marriage_reward_delivery(relationship_id, character_id, reward_kind)` 唯一记录，再投递/重试。

## 9. 离线与重登规则

- **[Proposal / High]** 服务端策略建议把关系作为持久权威数据，不能由在线 presence 或固定回放推导。
- **[Proposal / High]** 服务端策略建议重登读取 `Completed` 关系并投影婚姻摘要；无关系时投影明确空态。具体使用 `WEDDING_INFO/WEDDING_CHARAC` 哪些字段待 wire 取证。
- **[Proposal / High]** 一期策略建议 proposal/accept/ceremony 要求双方在线，但服务器查询 spouse 不要求对方在线。
- **[Proposal / Hypothesis]** 对方掉线时可保留 `AcceptedAwaitingCeremony`，仪式事务不完成、不扣费；恢复后重新发起完成。
- **[Unknown]** 官方是否允许离线求婚、离线接受、离婚单方生效和邮件通知。

## 10. 爱之摩伽陀所需最小只读契约（Proposal）

一期只需给地下城资格层一个无副作用、与 presence 解耦的查询：

```csharp
public interface IMarriageQueryDomainService
{
    ValueTask<ActiveMarriageRelation?> FindCompletedAsync(
        CharacterId characterId,
        CancellationToken cancellationToken);
}

public sealed record ActiveMarriageRelation(
    MarriageRelationId RelationId,
    CharacterId Self,
    CharacterId Spouse,
    DateTimeOffset CompletedAt,
    long Version);
```

契约只返回 `Completed`；pending/accepted 不算配偶。爱之摩伽陀的资格服务再独立验证：

1. 当前队伍恰好两名真实玩家；
2. 两人的 exact selected session 仍有效；
3. A 的 spouse 是 B，B 的 spouse 是 A，relation/version 一致；
4. 地下城自己的门票、次数、等级等规则另行判断。

不要把在线状态、队伍状态、婚房家具或婚礼 UI 塞进 marriage query，也不应为了摩伽陀提前实现婚房全套。

## 11. 尚待实机/PVF/产品决策

- `0x03F0/0x03F1/0x03F7` 的 21/22/23B 是总帧而非 body；真实 body 已确认为 7/8/9B。`03F0` 是 `u32 + u8 action + 2B opaque`，`03F1` 是 `u32 + u8 action + 2B opaque + u8 outcome`，`03F7` 是 `u32 + u32 + u8`；仍待 action/outcome 与求婚/接受/拒绝/取消的完整映射、错误码和实帧；
- `0x03CD/0x03CE/0x03D0` 已分别确认接收侧固定读取 11/12/15B，`0x03D4` 已确认至少读取 79B 并进入仪式/UI 逻辑；仍待字段语义、变长/间接部分、消息时序和正常实帧；
- 正常求婚、拒绝、接受、婚礼完成、取消、超时、重登各一组双端抓包；
- 已确认 PVF 三档金币（500,000 / 2,000,000 / 0）、礼包 `10002629/30/31`、豪华档额外材料 `2683401×1`、离婚双方各 500,000；仍需确认这些内容字段与 A21 请求 subtype、实际扣费/发奖时点和失败回包的映射；
- 性别规则、频道限制和正常 UI/NPC 入口仍 Unknown；职业已由 `[job][all]` 确认为不限制，最低等级已确认为 30；
- 官方是否允许同账号、离线操作和多 pending；
- 非空 `COUPLE_ROOM` 的 framing 已确认为 `17 + 6n`；仍待 6B 条目与 13B tail 的业务语义、合法服务端取值和正常实帧；
- Wedding quest `20737 -> 20738` 与求婚、接受、仪式完成之间的实际协议触发关系；
- 婚礼完成与爱之摩伽陀解锁是否同事务、同日或另有任务门。

在这些问题闭合前，一期可先完成持久关系内核、双账号并发约束、幂等和只读 spouse 契约；协议字段不要根据固定回放或旧 A12 彩票布局猜填。
