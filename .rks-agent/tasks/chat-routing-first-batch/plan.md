---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 聊天单入口路由与第一批频道

## Planning Choice

一份 Plan 直接指导：工作集中在一个新领域服务和一个协议切片，各部分顺序依赖清楚（目录成员 → 领域范围服务 → 协议切片 → 测试 → 文档 → 测试服），不需要分别交接或按结果再细化的单元。

## Approach and Shared Decisions

**分层与归属**

| 层 | 内容 | 位置 |
|---|---|---|
| Data | 目录成员：`ClientMessageType.GameSendChat = 0x0001_0011`、`GameItemLinkChat = 0x0001_01A9`；`ServerMessageType.GameSendChatRefusal = 0x0001_0011`、`GameItemLinkChatRefusal = 0x0001_01A9`、`GameChatMessage = 0x0000_000C`、`GameWhisperMessage = 0x0000_0041`、`GameMegaphoneMessage = 0x0000_0076` | `Dfo.Server.Data/Networking/NetworkMessages.cs` |
| Abstractions | `ChatScope` 封闭层级（`PartyOf`、`AreaOf`、`ChannelOf`、`GuildOf`、`Server`、`Character`）与 `IChatScopeDomainService.MembersOf(ChatScope) → IReadOnlyList<CharacterId>` | `Dfo.Server.Abstractions/Chat/` |
| Application | `ChatScopeDomainService` 与 `ChatRegistration.AddChat()`，由 `ApplicationModule` 调用 | `Dfo.Server.Application/Chat/` |
| Protocol | 切片目录 `SendChat`（不叫 `Chat`，避免与 `Abstractions.Chat` 命名空间同名）：消息、解码器、wire mode 映射、命令、编码器、通知与处理器、注册 | `Dfo.Server.Protocol/Game/SendChat/` |

**已定的局部选择**

- **范围服务（RQ-002、RQ-003）**：同步实现，依赖 `ISelectedSessionSnapshotQuery`、`ISelectedSessionDomainService`、`ITownAreaDomainService`、`IPartyService`，不持有任何可写集合。各分支：
  - `Server`：快照中 `IsInWorld` 的会话的角色。
  - `ChannelOf(a)`：锚点在世界内时，与锚点会话 `ListenerPort` 相同的在世界内会话。一个监听端口就是一个频道。
  - `AreaOf(a)`：`townAreas.Find(a)` 得到区域，取该区域成员中在世界内的。
  - `PartyOf(a)`：`party.KeyOf(a)`；虚拟队伍（无队）返回空，否则 `FindByKey` 后取在世界内的成员。
  - `GuildOf`：空。
  - `Character(t)`：`TryGetInWorld(t)` 成立时为 `[t]`，否则为空。
  - 最后一个分支 `_ => throw`。
- **wire 语义只在协议层**：`ChatWireModes` 把 mode 映射为频道种类，并给出喇叭 mode 对应的物品 id（11→36、12→898、57→10151255、13→37）。这张表来自客户端发送函数内写死的物品表（VA `01A6426C`），属于协议事实，所以放在 Protocol。区域 3/45/46、队伍 2/9、私聊 1/7、公会 6 映射为各自的种类，其余一律为 `null`（拒绝）。
- **单入口命令 `SendChatCommand`**：处理步骤如下，任一步失败即停止。
  1. 取发言者会话，要求在世界内；不在则不做任何事。
  2. mode 映射为 `null` 时拒绝。
  3. 正文为空或超过该种类回推协议的上限时拒绝。上限：`0x000C`／`0x0041` 正文 ≤ 255 字节，`0x0076` 正文 ≤ 511 字节。
  4. `PassesPreSendChecks`：唯一的发送前检查，现阶段恒为放行（RQ-004）。
  5. 喇叭：u32 槽位经 `A21InventoryLayout.ToBusinessSlot(new GameSlot(Main, slot))` 换算，越界或映射异常时拒绝；在 `units.WriteAsync` 内调用 `IMaterialConsumptionDomainService.ConsumeAsync(session, [new(slot, itemId, 1)])`，有拒绝时回拒绝。
  6. 构造范围：私聊先用 `IMailboxQueryDomainService.FindRecipientAsync(名字字节)` 取角色，找不到时静默；其余种类直接给出 `AreaOf`、`PartyOf`、`GuildOf`、`ChannelOf` 或 `Server`，锚点都是发言者。
  7. `MembersOf` 得到收件人。私聊不含发言者，其余种类天然包含发言者（RQ-016）。
  8. 发言者名字取自 `ISelectedCharacterCurrentViewDomainService`；`ProjectAsync(TheseCharacters(收件人), ChatMessagePosted(消息))`。
  9. 喇叭在投影之后再登记本人的背包变化：用 `GameInventoryDelivery.Notification` 或 `ClearedSlotNotification`，状态取自 `Debits` 的剩余量。这样客户端先收到自己的 `0x0076` 做本地扣减，再被背包状态覆盖为服务端值（见 Assumptions）。
  10. 成功与静默都不回 `0x0011`。
- **拒绝应答**：`u8 0, u8 reason`（RQ-018）。reason 统一用 `0x11`，即 70 的聊天失败码（70Reference）；A21 客户端显示什么以实机为准，写入测试步骤。
- **`0x01A9`**：解码器不解析正文，命令回 `0x01A9` 拒绝（RQ-021）。
- **通知 `ChatMessagePosted`**：放在 Protocol 切片里，携带已经算好的出站消息（`0x000C`／`0x0041`／`0x0076` 三种之一）。处理器原样返回该消息，没有任何依赖，满足 NotificationHandler 的注入限制。
- **字段取值（RQ-017）**：
  - `0x000C`：`mode`（原值）、`WireUserId`、`serverGroup 0`、正文。
  - `0x0041`：`mode`（原值）、状态 `0`、发言者名字、`srv 0`、正文。
  - `0x0076`：`mode`、`serverId`、`serverGroup 0`、频道编号、`WireUserId`、名字、正文。频道编号取发言者 `ListenerPort` 经 `IChannelEntryDomainService.ResolveAsync` 得到的 `ChannelId`，与 `0x0001` 下发的频道编号同源，实现时核对 `GameInitialConnectionPayloadCodec`。`serverId` 用 `0x0001` 下发的服务编号常量（`ServerIndex`）；客户端比较值的来源未读出，对不上时走跨频道分支（Hypothesis）。
- **dstr**：`i32 长度 + 字节`，名字与正文按 UTF-8。切片内写一个私有写法，参照 `GameServerNotice.EncodePayload`。
- **解码上限（RQ-024、RQ-030）**：
  - 正文长度字段 < 0 或 > 1024 时抛 `InvalidDataException`，交给既有解码失败路径，不进入命令。
  - 私聊名字长度 > 64 时同样处理。
  - 扇出帧的最大编码长度：`0x000C` = 1+2+1+4+255；`0x0041` = 1+1+4+29+1+4+255；`0x0076` = 1+1+1+1+2+4+29+4+511。名字上限取角色名存储上限，实现时核对。

## Work and Dependencies

| ID | Work | Covers | Depends |
|---|---|---|---|
| T-001 | 在 `NetworkMessages.cs` 加上述目录成员，核对与现有成员（如 `GameSetPartyInfo = 0x0001_000C`）不冲突 | RQ-001 | — |
| T-002 | Abstractions：`ChatScope` 与 `IChatScopeDomainService`。Application：`ChatScopeDomainService`、`AddChat()` 登记、`ApplicationModule` 接线。先写测试 `ChatScopeDomainServiceTests`（每个子类一组：在世界内过滤、无队为空、离线目标为空、频道按端口区分、全服跨端口），再实现 | RQ-002, RQ-003 | — |
| T-003 | Protocol 切片 `SendChat`：消息 record、`SendChatDecoder`、`ItemLinkChatDecoder`、`ChatWireModes`、四种编码器、`ChatMessagePosted` 及其处理器、薄 handler、`SendChatCommand`、`ItemLinkChatCommand`、`SendChatRegistration`；`ProtocolModule` 加一行。先写编解码字节测试，再写命令测试 | RQ-001, RQ-004, RQ-010–RQ-024 | T-001, T-002 |
| T-004 | 命令测试，放在 Protocol.Tests 或 Application.Tests，按现有夹具选择（`TestSessions`、`RecordedOutbound`、`GameOutboxFixture`；带库存的参照 `ItemUseFixtures` 与 `TeleportProtocolCommandFlowTests`）。覆盖 AC-010～AC-024：收件人集合、发言者回显、私聊不回发送者、成功不回 `0x0011`、拒绝集合逐个 mode、`0x01A9`、喇叭扣除与失败不扣、正文边界 | AC-010–AC-024 | T-003 |
| T-005 | 真实 TCP 集成测试：参照 `PartyTitleFanOutWireTests`／`CreatureHatchFrameIntegrationTests`，单个客户端进入世界后发 mode 0 收到 `0x0011` 拒绝，再发 mode 3 收到自己的 `0x000C` | AC-020 | T-003 |
| T-006 | 跑构建与全部相关测试，含 `CommandHandlerArchitectureTests`、`NotificationHandlerArchitectureTests`、`OpcodeArchitectureTests`、`RegistrationArchitectureTests`、`DependencyGraphArchitectureTests`。若命令注入了非领域形态接口，按架构测试规则处理，不扩大白名单理由 | RQ-032 | T-004, T-005 |
| T-007 | 协议记录：在人工区更新 `0x0011.md`（完整布局、mode 表、u32 含义修正、1:1 为 43、成功不回应答、拒绝 reason）、`0x01A9.md`、`NOTI-0x000C.md`、`NOTI-0x0041.md`、`NOTI-0x0076.md`（布局、取值来源、最大编码长度、证据标签）；在 `catalog.md` 登记；修正方案文档中私聊一栏 | RQ-030, RQ-031 | T-003 |
| T-008 | 任务文档 `Document/tasks/chat-routing/`：实现说明与编号用户测试流程。流程包括地下城内用普通频道发言（暴露 mode 5／8）、喇叭数量与重登、拒绝提示观察 | RQ-033 | T-006 |
| T-009 | 准备测试服：核实主干服务实际数据库路径与运行进程；一致备份；需要停服或换进程时先问用户；用本分支构建启动并给出就绪说明 | RQ-033 | T-006, T-008 |

## Verification

- 每项工作附带的测试，在 T-006 一次性完整跑通，构建无警告。
- 完整结果审查（review 阶段）对照 TaskContract、Spec 与本 Plan。
- 新鲜验证逐条对照 TaskContract 的 SC-1～SC-10。用户实机观察的条目（AC-016 及各频道显示）在用户反馈前只记录实现依据和测试服就绪证据，不声称通过。

## Assumptions

- 背包变化 `0x000E` 携带的是槽位的绝对状态。若实现时发现它是增量，改为只依赖客户端本地扣减并在重登时校正，并把这一点写进测试步骤。

## Amendment for TaskContract revision 5: 1:1 对话窗口

覆盖 Spec RQ-040～RQ-046 与 RQ-002／RQ-003／RQ-020／RQ-031 的修订。仍为 direct：会话 owner、协议切片、清理接入三块顺序依赖，一份 Plan 可以直接指导。

### 归属与已定选择

| 层 | 内容 | 位置 |
|---|---|---|
| Data | `ChatConversationId(uint)`；`ChatConversationMember(CharacterId, SelectedSessionIdentity Session, byte[] Name)`；`ChatConversation(Id, First, Second)`；`ChatConversationDeparture(SelectedSessionIdentity Recipient, ChatConversationId Conversation, byte[] LeaverName)`；目录成员 `ClientMessageType.GameOpenConversation = 0x0001_01A3`、`GameLeaveConversation = 0x0001_01A5`、`GameConversationUserState = 0x0001_01A6`，`ServerMessageType.GameConversationOpened = 0x0000_016A`、`GameConversationLeft = 0x0000_016E`、`GameConversationMessage = 0x0000_016F` | `Dfo.Server.Data/Chat/ChatConversationModels.cs`、`NetworkMessages.cs` |
| Abstractions | `ChatScope.Conversation(ChatConversationId Id)`；`IChatConversationDomainService`：`Open(requester, target) → ChatConversation?`（进行中已有则 null）、`Find(id)`、`IsMember(id, session)`、`Leave(id, session) → ChatConversationDeparture?`、`EndAllOf(session) → IReadOnlyList<ChatConversationDeparture>` | `Dfo.Server.Abstractions/Chat/` |
| Application | `ChatConversationDomainService`：单例，内存字典加锁；按两人角色 id 的无序对索引；id 计数器跳过 0、`0xFFFFFFFF` 与在用 id。`Open` 发现同对会话但其绑定会话已不是双方当前会话时替换。`ChatScopeDomainService` 增加 `Conversation` 分支：取会话两名成员中绑定会话仍是当前在世界内会话的 | `Dfo.Server.Application/Chat/` |
| Application 清理 | `SelectedSessionCleanupService` 注入 `IChatConversationDomainService`，在 `runtime.EndExact` 之前 `Run(() => EndAllOf(session))`；departures 里只保留对方仍在世界内且是绑定会话的项。`SelectedSessionCleanupNotificationPlan` 加 init 属性 `ConversationDepartures`（默认空），不改既有构造器 | `Sessions/SelectCharacterLifecycleServices.cs`、`Data/World/SessionEntryModels.cs` |
| Protocol | 新切片 `Game/ChatConversation/`：`0x01A3`／`0x01A5`／`0x01A6` 消息与解码器、三个命令与薄 handler、`0x016A`／`0x016E`／`0x016F` 编码器、通知 `ChatConversationOpened`（处理器按收件人组 [对方, 自己]）与 `ChatConversationLeft`（处理器原样返回）、注册 | `Dfo.Server.Protocol/Game/ChatConversation/` |
| Protocol 发言 | `ChatWireModes` 加 `43 => Conversation`；`SendChat` 命令对 Conversation 种类所有失败走静默而不是 `RefuseAsync`，范围为 `Conversation(new(aux))` 且先要求 `IsMember`；收件人去掉发言者；行为 `ConversationMessage`（NOTI `0x016F`） | `Game/SendChat/` |
| Protocol 清理投递 | `CleanupNotificationDelivery` 在组队投影之后，对每个 departure 用 `NotificationAudience.TheseSessions([recipient])` 投影 `ChatConversationLeft` | `Game/StateSynchronization/SupplementalSynchronizationMessages.cs` |

- 名字：建会话与发言都用服务端保存的角色名（`ICharacterDomainService.Get(id).IdentityAsync`），目标按 `IMailboxQueryDomainService.FindRecipientAsync(名字字节)` 查，与私聊一致。会话成员保存建立时读到的名字，离开通知用它，不再读库。
- 上限（RQ-046）：名字 `< 128` 字节，`0x016F` 正文 `< 1024` 字节；解码 `0x01A3` 名字长度 `1..64`，否则 `InvalidDataException`（同私聊名字上限）。`0x01A5` body 必须恰为 4 字节。`0x01A6` body 不解析。
- 最大编码长度：`0x016A` = 1+4+1+2×(4+127)；`0x016E` = 4+4+127；`0x016F` = 4+4+127+4+1023。
- 发送前检查：`0x01A3` 复用与发言同一个检查点（放行）。黑名单结果码 77 不实现。
- 建会话的读取（两人名字、目标查找）走 `units.ReadOnlyAsync`；会话 owner 是内存状态，不进写事务。
- `0x01A6` 命令只记 Debug 日志并返回 true。

### Work

| ID | Work | Covers | Depends |
|---|---|---|---|
| T-010 | Data 模型与目录成员；Abstractions 接口与 `ChatScope.Conversation`。先写 `ChatConversationDomainServiceTests`（建立、重复请求为 null、过期会话被替换、id 跳过保留值、离开与 EndAllOf 的 departures、非成员离开为 null），再实现 Application 会话 owner 与范围分支（补 `ChatScopeDomainServiceTests`） | RQ-040, RQ-043, RQ-044, RQ-002, RQ-003 | — |
| T-011 | 协议切片 `ChatConversation`：编解码字节测试先行（三个 NOTI 布局与上限、`0x01A3`／`0x01A5` 截断与越界），再实现消息、解码器、编码器、通知与处理器、命令、注册、`ProtocolModule` 接线 | RQ-040, RQ-043, RQ-045, RQ-046 | T-010 |
| T-012 | `SendChat` 接 mode 43：命令测试先行（只给对方 `0x016F`、发言者无帧、各静默路径无 `0x0011`、mode 33 被拒、拒绝集合穷举更新） | RQ-041, RQ-042, RQ-020 | T-010, T-011 |
| T-013 | 清理接入：Application 清理测试（departures 进入计划、对方不在时不进）与 Protocol 投递测试（`0x016E` 发给对方）；实现 | RQ-044 | T-010, T-011 |
| T-014 | 真实 TCP 集成测试：两个客户端进入世界，A 发 `0x01A3` 双方收到 `0x016A`，A 发 mode 43 只有 B 收到 `0x016F`，A 发 `0x01A5` B 收到 `0x016E` | AC-040, AC-041, AC-043 | T-011, T-012 |
| T-015 | 构建与全部相关测试（含架构测试） | RQ-032 | T-012～T-014 |
| T-016 | 协议记录与 catalog（`0x01A3`、`0x01A5`、`0x01A6`、NOTI `0x016A`／`0x016E`／`0x016F`，`0x0011` mode 43 与 aux 含义），测试流程增加 1:1 步骤，重启测试服 | RQ-031, RQ-033 | T-015 |
