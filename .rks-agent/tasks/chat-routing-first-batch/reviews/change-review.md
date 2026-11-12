---
kind: rks-review
verdict: approved
---

# Change Review: 聊天单入口路由与第一批频道

## 目标与范围

- 目标：worktree `E:\Project\DNF\DNF-JP-86-Server-chat`，分支 `feature/61-chat-routing`，`git diff c49428c55..c914f06b2`
  （实现提交 `7ef96c72d`、文档提交 `c914f06b2`），工作树干净。
- 治理：`TaskContract.md`（revision 3）、`spec.md`、`plan.md`（direct）。
- 评审类型：完整结果评审（代码 + 协议文档），独立评审者，未参与实现。
- 采用标准：reviewing-work 的 Code 与 Deliverable 参考；仓库 `AGENTS.md`、`Document/ArchitectureReview.md`、
  `Document/Architecture.md`、`Document/ProtocolCommands.md`、`Document/EventsAndNotifications.md`、`protocol/evidence.md`。
- 不在本评审内：用户实机部分（AC-016 实机、SC-9/AC-033 测试服就绪），由后续新鲜验证与用户反馈承接。

## 决定性证据

- `dotnet build Server/86JP.Server.slnx`：0 警告 0 错误（TreatWarningsAsErrors）。
- `Dfo.Server.Application.Tests`（含 `CommandHandlerArchitectureTests`、`NotificationHandlerArchitectureTests`、
  `RegistrationArchitectureTests`、`DependencyGraphArchitectureTests`、`OpcodeArchitectureTests` 及新增
  `SendChatCommandTests`、`ChatScopeDomainServiceTests`）：6581 通过，0 失败。
- `Dfo.Server.Protocol.Tests`：2186 通过；`Dfo.Server.Networking.Tests`（含真实 TCP `ChatWireTests`）：215 通过，均 0 失败。
- 逐行阅读了全部生产代码、测试与协议文档差异；核对了 `IMailboxQueryDomainService.FindRecipientAsync` 的 SQL、
  `OnlineUserConnectionObserver` 的频道编号来源、`MaterialsConsumedHandler` 的槽位补发写法、`TownAreaMember`／`TownAreaSnapshot` 语义。

## RQ/AC 对照

| AC | 实现 | 自动测试证据 | 结论 |
|---|---|---|---|
| AC-001 | `SendChatRegistration` 一对解码器／命令绑定；mode 只在 `ChatWireModes`（Protocol） | 注册闭合由架构测试守护；Abstractions/Application 的 Chat 源码无 wire 数值 | 满足 |
| AC-002 | `ChatScope` 六个嵌套 sealed record + 私有构造；`MembersOf` 单参，`_ => throw NotSupportedException` | `ChatScopeDomainServiceTests` 每子类一测 | 满足（见建议 1：外部派生实际可行） |
| AC-003 | `ChatScopeDomainService` 只注入会话快照／会话／城镇区域／组队，无字段集合 | 架构测试通过 | 满足 |
| AC-004 | `PassesPreSendChecks` 唯一一处，放行 | 所有投递测试经过该步 | 满足 |
| AC-010 | `AreaOf` 经 `ITownAreaDomainService`，按当前会话过滤 | `Area_line_reaches_…`（B 收、C 异区、D 副本均不收，A 含）、45/46 | 满足 |
| AC-011 | `PartyOf` 经 `IPartyService.KeyOf/FindByKey` | 队伍（B 在副本）、无队静默 | 满足 |
| AC-012 | `FindRecipientAsync` → `Character(t)`；剔除自己 | 在线只 B 收、名字为 A；离线／不存在／自己静默，后续区域聊天照常 | 满足 |
| AC-013 | 物品 37，`ConsumeAsync` 扣 1，`ChannelOf` 按 ListenerPort | 3→2、同频道收、决斗频道不收 | 满足 |
| AC-014 | 11/12/57 对应 36/898/10151255，`Server` 跨端口 | 三个 mode 各测、含决斗频道与副本中角色 | 满足 |
| AC-015 | `GuildOf` 恒空 | `Guild_line_reaches_no_one…` | 满足 |
| AC-016（自动） | 区域／队伍／喇叭含发言者，私聊不含；成功无 `0x0011`；频道字节 = `ResolveAsync(ListenerPort).ChannelId`（与 NOTI 0x0001 同源，已核对 `OnlineUserConnectionObserver`） | 各测试断言发言者收件与 `DoesNotContain SendChatRefusal`；喇叭断言 ChannelId、ServerId、0x0076 后紧跟 0x000E | 自动部分满足；实现依据已写入协议记录 |
| AC-020 | 未映射 → `[0, 0x11]` | 0、4、0x0A、0x10–0x1E、43、48、14、15、34、50、56、52–55、36–38、5、8、200、255 逐个；物品不变；TCP 被拒后同连接区域聊天回到发言者且连接未关 | 满足 |
| AC-021 | `ItemLinkChatDecoder` 不读体，回 `0x01A9` `[0,0x11]` | 帧首字节 1、type 0x01A9、body `00 11` | 满足 |
| AC-022 | 空槽／物品不符／槽位越界拒绝且不扣 | 4 组空或错物品 + 2 组越界槽位 | 满足 |
| AC-023 | 空、上限、上限+1：0x000C/0x0041 255，0x0076 511 | 7 组边界 | 满足 |
| AC-024 | 解码器长度不足、负长度、>1024、名字 >64、缺尾字节抛 `InvalidDataException` | 编解码测试 9 例 + 命令层 6 例「不投递、不改库存」 | 满足 |
| AC-030 | 编码器 `MaximumBodyLength` 263/296/554，`ChatDString.Require` 拒绝超限 | 上限长度编码等于常量；超限与空字段抛异常 | 满足 |
| AC-031 | 五个协议页人工区 + 机器区（由新 tsv）+ catalog 段落 | — | 满足；标签 ClientStatic/70Reference/CurrentImplementation，无 ClientVerified/WireCaptured |
| AC-032 | 构建与测试 | 见上 | 满足（以测试运行为准） |

重点核对项：

- 拒绝集合完整，且 `Every_other_mode_is_unmapped` 穷举 0–255 只放行 12 个第一批 mode。
- 成功不回 `0x0011`；喇叭失败（空槽、物品不符、槽位越界、频道编号越界、名字超长）都在 `ConsumeAsync` 之前或由其拒绝，库存不变。
- 回显：`ChatMessagePostedHandler` 对发言者输出 `[Line, .. SpeakerFollowUp]`，对他人只输出 `Line`，0x000E 在 0x0076 之后；补发写法与既有 `MaterialsConsumedHandler` 一致（绝对状态／清槽）。
- 扇出上限：正文、名字在命令层按目标 NOTI 上限拒绝，编码器再兜底；解码上限 1024/64 只防异常长度。

## 架构（按 ArchitectureReview）

结论：**通过：附建议**。

- 分层：wire mode、物品表、包体布局只在 `Dfo.Server.Protocol/Game/SendChat/`；Abstractions/Application 只有领域范围。
- CMD 注入面：领域服务（会话、聊天范围、角色、邮箱查询、频道入场、材料消耗）+ 工作单元／投影／发件箱／时钟／日志，`CommandHandlerArchitectureTests` 通过。
- NotificationHandler 零依赖；扇出走 `ICommandProjections.ProjectAsync(TheseCharacters)`，在 `WriteAsync` 提交后调用；
  仅「无收件人但有补发」时用本连接的 `RegisterExplicitAsync`，属本次操作主体自己的连接，符合豁免。
- 范围服务不存名单：每次向 owner 现查，无可写字段。
- 注册在切片 `AddSendChatSlice()` 与 `ChatRegistration.AddChat()`，`ProtocolModule`／`ApplicationModule` 各一行。
- 目录成员 7 个，值与现有成员不冲突。
- `IMailboxQueryDomainService.FindRecipientAsync` 的 SQL 是「未删除角色按名字字节查找」，语义与私聊按名找人一致，是复用而非另写判定；
  名字归属在 Mail 领域是命名问题（建议 2），非阻断。
- `ICharacterDomainService.Get(id).IdentityAsync().Name` 是角色名的 owner，合理。
- `GameProtocol.ServerIndex` 上提：0x0001 与 0x0076 两个切片共用的 wire 常量，按 `ProtocolCommands.md`「要共用就上提到 endpoint 层」处理，合理。
- 喇叭物品 id（36/898/37/10151255）写在 Protocol：来源是客户端发送函数里写死的 mode→物品表（VA `01A6426C`），属于 wire mode 的含义而不是 PVF 规则输入，可接受（建议 3）。

## 阻塞发现

无。

## 非阻断建议

1. `Server/src/Dfo.Server.Abstractions/Chat/ChatScope.cs:11-13` 注释称私有构造器把所有形态限定在本文件。实测（本地最小工程）抽象 record 会合成 protected 拷贝构造器，外部 record 可经 `base((ChatScope)x)` 派生。
   行为上 `MembersOf` 的 `_ => throw` 仍然兜底，所以不影响正确性；建议把注释改为「外部派生无法完全阻止，未知形态在 `MembersOf` 抛出」，与 `EventsAndNotifications.md` 对 `NotificationAudience` 的表述一致。AC-002 中「外部无法再派生」也应按此理解。
2. 私聊按名找人借用 `IMailboxQueryDomainService`，长期看可以把「按名字找角色」上移到 Characters 领域，由邮件与聊天共用。
3. 喇叭 mode→物品表在 Protocol，若以后要让 GM／内容配置喇叭物品，再考虑移入规则服务。
4. `AreaOf` 用 `area.Members` 全员，没有按 `TownAreaSnapshot.VisibleTo` 的 `SharesTownPresence` 过滤。在「非共享在场」区域里，互相看不见的人仍会互相收到区域聊天。是否需要过滤取决于 A21 实际行为，建议在测试流程中记录或留作后续问题。
5. `Document/tasks/chat-routing/用户测试流程.md` 第 15 步预期写的是「正常显示或被客户端截断」。但客户端允许不到 255 个宽字符，多字节正文可能超过 255 字节而被服务端拒绝（reason 0x11）。建议把「可能被拒绝」写进预期，避免误判为缺陷。
6. 测试流程中的数据库是「#308 测试库的一致副本」，契约要求「当前主干实际使用的数据库」。是否同一库请在 SC-9 新鲜验证时给出证据（不属于本代码评审）。
7. `ChatMessagePostedHandler` 只按 `CharacterId` 判断发言者，`MaterialsConsumedHandler` 还核对了 ConnectionId。同一作业内不会出现换连接，影响可忽略；如要与既有写法一致可补上。
8. `NetworkMessages.cs` 中三个 NOTI 成员插在组队拒绝成员之间，位置不直观，可移到聊天相关成员附近。
9. 喇叭测试只断言补发消息的类型名为 `InventoryDeltaMessage`，没有断言剩余数量。建议补一条断言剩余 2（末个清槽的情形已覆盖）。
10. `check-server-documentation-sync.py` 对全部注册普遍报「无法解析」，包括基线已有的 SaveQuickChat。这是工具既有问题，不是本变更引入的。

## 测试运行

评审者于 2026-11-12 在 `c914f06b2` 上独立运行，环境变量 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`：

- `dotnet build Server/86JP.Server.slnx`：0 警告，0 错误。
- `Dfo.Server.Application.Tests`：6581 通过，0 失败。已知基线失败 MirrorDungeonEntry quota 本次未出现。
- `Dfo.Server.Protocol.Tests`：2186 通过，0 失败。
- `Dfo.Server.Networking.Tests`：215 通过，0 失败，包括真实 TCP 测试 `ChatWireTests`。

本评审的通过不代替 SC-1～SC-10 的完成验证，也不代替用户实机验收。
