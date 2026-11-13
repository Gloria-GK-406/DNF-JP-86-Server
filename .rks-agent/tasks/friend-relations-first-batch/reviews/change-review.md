---
kind: rks-review
verdict: changes_required
---

# Change Review: 角色关系系统与好友第一批（好友 + 黑名单）

## 目标与范围

- 目标：worktree `E:\Project\DNF\DNF-JP-86-Server-friends`，分支 `feature/60-friend-relations`，
  `git diff 17eb734d4..bb2821ac9`（12 个提交：planning、AM-1–AM-3、TASK-01–TASK-07 与一次 catalog 补正），审查时工作树干净。
- 治理：`TaskContract.md`（revision 1，SC-1–SC-10）、`spec.md`（RQ/AC 与 AM-1–AM-3）、`plan.md`（task-units，SD-1–SD-8）、
  `research/client-evidence.md`、`tasks/TASK-01.md`–`TASK-07.md` 的 Result 与快审记录。
- 评审类型：完整结果审查（代码 + 协议文档 + 用户测试流程），独立评审者，未参与实现。
- 采用标准：reviewing-work 的 Code 与 Deliverable 参考；仓库 `AGENTS.md`（Code Review Rules）、`Document/ArchitectureReview.md`、
  `Document/Architecture.md`、`Document/ProtocolCommands.md`、`Document/EventsAndNotifications.md`、`Document/GameplayExecution.md`
  （按需）、`Document/DatabaseSchemaMigration.md`、`protocol/README.md`。按委托要求，功能正确性也一并审查。
- 不在本评审内：用户实机部分（AC-071；SC-1–SC-7 的实机项；SC-10 的用户反馈），标为「待实机」，不计为缺陷。

## 决定性证据

- 逐行阅读了全部生产代码差异：`Abstractions/Relations`、`Application/Relations`、`Persistence/Relations`、
  角色身份读取（`ICharacterIdentityQueryDomainService` 及其 SQLite 仓储）、`Game/FriendFrames.cs`、`Game/BlacklistFrames.cs`、
  `Game/FriendPresenceNotifications.cs`、`Game/UnitedFriend/`、`Game/Blacklist/`、聊天与 1:1 改动、选角／回选人／断线／删角色挂点、
  `SelectedSessionCleanupService`、领域事件、`OutboundLimits`、结构 v5 与 `Tool/DbMigrate`。
- 核对了与既有能力的关系：`SqliteMailboxRepository.FindRecipientAsync`、`MailRecipient`、`CharacterEntryTables.MarkDeletedAsync`
  （唯一的删角色写入点）、`SetGrowTypeAsync`／`SetProgressAsync` 的全部调用点、`ChannelEntryDomainService.ResolveAsync`、
  既有 LiveOnly 订阅者先例、客户端证据中 RQ-004／RQ-005／RQ-051 与「CMD 未请求也会被处理」一节。
- 复算 `OutboundLimits.SelectionOutboundBound`：450,680 + (17,480 − 8) + 2,370 = 470,522 B 正文，238 帧 × 15 = 3,570，合计 474,092，与常量一致。
- 本次复跑（设 `PVF_ARCHIVE_PATH=…\pvf-merged-20261111\Script.pvf`，不构建 Host、不跑 Host.Tests／Networking.Tests）：见下「测试复核」。

## RQ/AC 对照

| AC | 实现 | 自动测试证据 | 结论 |
|---|---|---|---|
| AC-001 | `research/client-evidence.md`；各协议页人工区；AM-1–AM-3 | — | 满足（RQ-005 的场景入侵问题按 AM-2 绕开，同频道显示频道号留实机） |
| AC-010 | 一个实现 `CharacterRelationDomainService` 同时实现读写两接口，注册解析到同一单例；读方（聊天、1:1、推送处理器、订阅者、选角、清理计划）只注入 `ICharacterRelationQueryDomainService`；写接口只在 0x0122/0x0123/0x0076/0x0077 与删角色 CMD | `RelationPassivityArchitectureTests`（构造参数白名单 + 可达性 + 反例 + 同实例）；表无名字列 | 满足 |
| AC-011 | `AddAsync`：Self → 存在性（经身份读取）→ AlreadyPresent → Full → Insert | `CharacterRelationDomainServiceTests`、`FriendCommandTests`（离线、重复、自己、不存在／已删除、满） | 满足 |
| AC-012 | `RemoveAsync` 只删一行 | 领域与命令测试 | 满足 |
| AC-013 | 黑名单 74/76/78/0/75 映射在 `BlacklistPayloadCodec` | `BlacklistCommandTests`、`BlacklistCodecTests`；好友与黑名单互不影响 | 满足 |
| AC-014 | `PurgeAsync` 同事务先读「把 C 加为好友的有效 owner」再删两向全部行 | 领域与存储测试；`FriendPresencePushTests` 删角色用例 | 满足 |
| AC-015 | SQLite 持久化 | `SqliteCharacterRelationDomainStoreTests` 重开库一致 | 满足 |
| AC-030 | 五个 CMD 编解码与 NOTI 0x0111（0/2）、0x0112 | `FriendCodecTests`、`BlacklistCodecTests`、构建器测试（截断、尾随、超长、空表、重名跳过） | 满足 |
| AC-031 | 加：应答 → 全量 → 0x0078；删：应答 → subcmd 2 → 全量 → 0x0078；选角入场 0x0111 原位换真实列表，紧跟 CMD 0x0078 | `FriendCommandTests`、`SelectCharacterLifecycleTests.Friends`、22 份 golden、63 帧序列断言 | 满足 |
| AC-040 | `GameSendChatCommand` 在成员集合确定后剔除 `ObserversOfAsync(speaker, Blacklist)`，喇叭扣除在前 | `SendChatCommandTests.Blacklist`：区域、队伍、私聊、频道喇叭、服务器喇叭、1:1 内发言、发言者自己的黑名单不影响、解除后恢复 | 满足 |
| AC-041 | `ConversationCommands` 双向 `ExistsAsync`，只给发起方直投 `[0x4D]`，先于「会话已开」判断 | 双向参数化用例 | 满足 |
| AC-050 | 上线（入场发布后）、下线三投递点（断线 `CleanupNotificationDelivery`、回选人、顶替）共用 `FriendDepartureNotices`；升级／转职 LiveOnly 订阅按角色合并；删角色同单元 Purge 后投影 | `FriendPresencePushTests`（上线 ch、同频道仍给频道号、bl 位、下线 ch=0、升级／转职帧序 0x0111→0x0078、非观察者、顺序、重连只达新会话、删角色、删除失败不动）；`CleanupNotificationDeliveryTests`、`ReturnToSelectionTests`、`SelectCharacterLifecycleTests.Friends`（顶替时先下线后上线）、`SelectedSessionPartyCleanupTests`（读失败只丢好友通知） | 满足 |
| AC-060 | `Version=5`；`Steps[4]=CharacterRelationsUpgrade.Apply`，语句与声明逐字一致（含 `ON DELETE CASCADE`） | `SchemaVersionTests` 4→5 用例与结构一致性 | 满足 |
| AC-070 | catalog 两个新段落、进度表、decoder/encoder 普查；协议页人工区；标签 ClientStatic／CurrentImplementation，无 ClientVerified | — | 满足 |
| AC-071 | `Document/tasks/friend-relations/用户测试流程.md`（16 步 + 已知限制 + 收尾）；TASK-07 就绪记录 | — | **待实机**（用户进行中） |

TaskContract SC 对照：SC-1–SC-7 自动部分满足、实机部分待实机（SC-1 的一处字面差异见非阻断 N3）；SC-8 满足；
SC-9 满足（以本次与 TASK-06 记录的测试运行为准）；SC-10 测试服与流程就绪，用户反馈待实机，未建 PR。

## 用户硬约束

| 约束 | 核对 | 结论 |
|---|---|---|
| 关系只有一个 owner，读写接口分离 | 唯一实现、同一单例、读写两契约；读方只注入只读接口；所有写入经写接口（GM 本期无写入口） | 满足 |
| 只存角色 ID | `character_relations(owner_id,target_id,kind,created_at)`；名字、等级、职业在展示时按 ID 读 `characters` 当前值 | 满足 |
| 关系服务被动、不推送 | 构造依赖只有 store、身份只读、事务访问器、时钟；架构测试守住可达性且带反例；所有推送由 CMD／订阅者／清理投递完成 | 满足 |
| 婚姻没有动 | 差异中无任何 Marriage 文件 | 满足 |
| 师徒、最近联系人未实现但结构能容纳 | `CharacterRelationKind` 封闭枚举按存储值往后加；表 `kind BETWEEN 1 AND 255`；`Limit` 对未知 kind 抛错 | 满足 |

## 跨 Task 一致性

- **共享帧与构建器**：`FriendListMessage`／`FriendRemovedMessage`／`FriendPresenceMessage` 与 `BlacklistListResponse` 各只有一个编码器；
  入场的 `FriendListStateMessage`／`BlacklistListStateMessage` 直接调用同一 `EncodeBody`，不会漂移。全量列表在所有路径都经
  `FriendListRefresh.BuildAsync`（加、删、选角、升级／转职／删角色推送），名字上限、重名跳过、等级越界跳过只有一处判定。
- **推送顺序**：凡发全量 0x0111 的地方都紧跟 0x0078——CMD 用 `RegisterAsync`（NOTI 后 CMD），处理器返回 `Messages`
  （同一收件人一批），入场批次两帧相邻。删除路径是 subcmd 2 → 全量 → 0x0078。与 RQ-004 的结论一致。
- **三个下线投递点**：断线 `CleanupNotificationDelivery`、回选人 `GameReturnToSelectionCommand`、顶替 `GameSelectCharacterCommand`
  都在 1:1 离开通知之后调用同一个 `FriendDepartureNotices.For(plan)`，各有测试。读取在 `SelectedSessionCleanupService` 中、会话释放前完成，
  失败只丢好友通知并记 Warning（TASK-06 Round 2 修正）。换频道时旧会话晚于新会话入场才清理的情形，由「本角色已有另一个在世界中的会话则不发下线」覆盖。
- **选角进入序列**：关系列表在入场批次组装前的只读单元读取；上线通知在入场发布之后、顶替清理投递之后，顺序为先下线后上线。

## 架构（按 ArchitectureReview）

结论：**不通过：实现归位**（一项阻断级错位，见 B1）；其余部分由现有架构完整承接。

架构对应：
- 关系事实只有 `Application/Relations` 一个 owner，`Persistence/Relations` 只做数据操作，规则（自己、存在性、重复、上限）全部在领域服务；
  上限常量只在一处。
- 包体布局只在 Protocol；共享帧按 `ProtocolCommands.md`「切片内不跨协议引用，要共用就上提到 endpoint 层」放在 `Game/` 层文件，编码器登记各一次。
- 扇出一律 `TheseCharacters` 投影，按角色解析连接；领域侧（清理计划）只用 `TryGetInWorld` 判断在场，不读 `ConnectionId`，符合
  `EventsAndNotifications.md` 的边界说明，并与既有 `ConversationDepartures` 同形。
- 转职事实补成领域事件 `CharacterGrowTypeChanged`，在两处唯一的 `SetGrowTypeAsync` 成功点同事务 `EnqueueAsync`；
  订阅者为 LiveOnly，与仓内多个 Protocol 层 LiveOnly 订阅者先例一致。
- 删角色在同一写入单元调用 `PurgeAsync`，属于 CMD 编排多个领域能力；读路径另按 `delete_flag=0` 过滤，作为第二道保证。
- `SelectionOutboundBound` 调整只改常量与推导注释，Networking 未认识任何协议。

## 阻塞发现

### B1 按名查找有效角色出现第二套实现（阻断：同一判定另写一套）

- 位置：新增 `Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterIdentityRepository.cs:21-27`（`FindLiveByNameAsync`）
  与新记录 `Server/src/Dfo.Server.Abstractions/Characters/ICharacterIdentityQueryDomainService.cs:7`（`CharacterPublicIdentity`）；
  既有 `Server/src/Dfo.Server.Persistence/Mail/SqliteMailboxRepository.cs:128-133`（`FindRecipientAsync`）与
  `Server/src/Dfo.Server.Data/Mail/Mail.cs:95`（`MailRecipient`）。
- 事实：两条 SQL 逐字相同（`delete_flag=0 AND COALESCE(name_bytes,CAST(name AS BLOB))=@name LIMIT 1`，返回同样的五个字段），
  承担同一责任「按名字字节找有效角色」。本分支之后两者并存，而且同一功能内混用：黑名单与好友命令用新的
  （`BlacklistCommands.cs:49`、`:106`，`UnitedFriendCommands.cs:64`、`:136`），1:1 建会话的目标解析与私聊仍用邮件的
  （`ConversationCommands.cs:77`，`GameSendChatCommand.cs:139`）。于是「拉黑 X」与「对 X 开 1:1 时检查黑名单」由两套查找分别决定 X 是谁。
- 适用约束：`Document/ArchitectureReview.md`「判断实现错位的严重程度」阻断行「同一判定另写一套算法或口径——与既有能力承担相同责任、语义和不变量」；
  `Document/Architecture.md`「同一事实只由一个 owner 保存」。以后任何对名字匹配口径的修改（例如排除某类角色、规范化）都要靠两处同步维持一致。
  Plan SD-4 写明「若需要上提到角色领域，由 T-002 决定，不在协议层另写查询」；T-002 选择了上提，却没有让原能力复用它，TASK-02 Remaining 把收口列为后续。
  这属于实现错位，不是架构限制：角色领域是名字查找的正确 owner，原 owner 内即可收口。
- 最小修正：保留角色领域的这一套为唯一实现，让邮件侧复用它——`MailboxQueryDomainService.FindRecipientAsync` 改为调用
  `ICharacterIdentityQueryDomainService.FindByNameAsync` 并映射成 `MailRecipient`（或直接让 `MailRecipient` 退场、调用方改用
  `CharacterPublicIdentity`），删除 `IMailboxRepository.FindRecipientAsync` 与其 SQL；补一条测试证明邮件、私聊、1:1 与好友／黑名单解析同一名字得到同一角色。
  若用户认为这次不该越界改邮件领域，则须由用户明确把它作为本 PR 的具名架构例外接受（审查记录无法代为放行）。

## 非阻断发现

- **N1 上线通知的读取放在入场发布之后**（`Server/src/Dfo.Server.Protocol/Game/SelectCharacter/GameSelectCharacterCommand.cs:389`、`:834-842`）。
  `ReadFriendObservers` 只读单元若失败，选角命令在角色已入场后失败，连接会被关闭（TASK-06 快审 N1，未处理）。概率低（同一命令刚读过同库），
  但后果是已进场的玩家被断开。建议把观察者读取并入发布前的 `ReadSelectedCharacterRelations` 单元，发送时再按在场过滤；或像下线读取一样捕获后只丢通知。
- **N2 升级与转职都推全量列表**（`Server/src/Dfo.Server.Protocol/Game/FriendPresenceNotifications.cs:159-185`）。符合 Spec RQ-052，但每次升级都会让所有在线观察者重收
  0x0111 与 0x0078。客户端证据记录了两个相关风险：临时遮断的好友在全量重建时会让其后的好友整段读不进来（`client-evidence.md`「其他发现」）；
  未经请求的 CMD 0x0078 在写出器标志置位时会被丢弃，该标志含义未追（High，非 Confirmed），一旦丢弃，黑名单页在下次请求前为空。
  服务端黑名单生效不受影响。客户端另有只就地更新 ch／level／job／转职、不碰黑名单的 subcmd 3，可在后续批次评估用于升级与转职。实机时建议顺带观察副本中被推送的情形。
- **N3 SC-1 的「添加自己时客户端有失败表现」无法满足**。客户端对 0x0122 只对 0x04／0x07／0x15／0x5A 弹提示，加自己回 0x01 静默；拉黑自己同样静默。
  AM-1 已「只记录，不改要求」，用户测试流程第 5 步与「已知限制」也写明「无提示（本期设计）」。这是 TaskContract 字面与实现之间的差异，建议用户实测时明确接受，避免验收时被当作失败。
- **N4 删角色只通知把它加为好友的人**（`GameDeleteCharacterCommand.cs:83-95`）。拉黑了被删角色的在线玩家，客户端黑名单页仍显示该名字直到重登或下次 0x0078；
  此时解除会得到 75「重连后刷新」。Spec 未要求，服务端数据已清，记为残留。
- **N5 删角色不变量只由协议 CMD 编排保证**（`GameDeleteCharacterCommand.cs:103-109`）。当前 `MarkDeletedAsync` 只有这一条调用链，符合 CMD 编排；
  将来新增 GM 删角色或其他删除入口时必须同单元调用 `PurgeAsync`。读路径的 `delete_flag=0` 过滤可兜底列表显示，但不清行。
- **N6 未配置频道的监听端口**：`FriendListBuilder` 给出 ch=0（客户端显示离线），`FriendCameOnlineHandler` 对每个收件人抛错记失败。测试服脚本已为三个端口配置频道，
  正式部署若有未进频道目录的端口会出现「好友在线却显示离线」。已记入 NOTI-0x0111 页，属部署约束。
- **N7 残留清理**：`Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs:277` `RequestBlacklistType` 已无引用（TASK-03 Remaining）；
  `protocol/patterns/data/check-server-documentation-sync.py` 在本分支报 459 条与本任务无关的失败，catalog 普查区为手改，建议 PR 前在主干状态下复核一次。
- **N8 测试缺口（低）**：回选人与顶替两条下线路径的测试用替身计划验证投递，计划生成另由 `SelectedSessionPartyCleanupTests` 覆盖，缺少一条从真实清理到投递的端到端用例
  （TASK-06 快审 R2，可选）。三处共用 `FriendDepartureNotices`，风险有限。

## 测试复核

本次在 HEAD `bb2821ac9` 上重新构建并运行（设 `PVF_ARCHIVE_PATH`；构建只涉及测试项目及其依赖的 `src/*/bin`，
未构建 Host，未触碰正在运行的测试服产物），全部 0 失败、0 跳过：

| 项目 | 通过 |
|---|---|
| `Tool/DbMigrate.Tests` | 27 |
| `Dfo.Server.Persistence.Tests` | 466 |
| `Dfo.Server.Protocol.Tests` | 2282 |
| `Dfo.Server.Application.Tests`（含各架构测试与 `RelationPassivityArchitectureTests`） | 6700 |

`Dfo.Server.Host.Tests`（243 通过／1 跳过）与 `Dfo.Server.Networking.Tests`（218 通过，含 golden 选角特征测试）引用 Host，
为避免覆盖运行中的测试服产物本次未复跑，以 TASK-06 Round 2 记录为准。上述构建过程同时证明相关项目在 TreatWarningsAsErrors 下 0 错误。

## 处理方向

- 修 B1 后需要对改动部分做一次独立复审；B1 只涉及邮件领域的查找收口，不影响用户正在进行的好友与黑名单实测，实测可以继续。
- N1、N3 建议在 PR 前处理或由用户明确接受；其余非阻断项可记入 #60 的后续批次。

---

# Round 2（复审）：B1 修正

```yaml
kind: rks-review
verdict: approved
```

## 目标与范围

- 目标：`git diff bb2821ac9..HEAD`，HEAD = `5436e0dfc`（单个修正提交「remediation single name lookup owned by the character domain」），
  6 个文件、+34／−13。完整变更范围仍为 `17eb734d4..5436e0dfc`。
- 评审类型：对 Round 1 阻塞发现 B1 的独立复审，并快速确认 Round 1 其余结论未回归；独立评审者，未参与修正。
- 采用标准：reviewing-work 的 Code 参考；`Document/Architecture.md`（同一事实一个 owner、依赖注入与依赖环条款、接口类别）、
  `Document/ArchitectureReview.md`（错位严重程度表）。
- 文件开头的 frontmatter 是 Round 1 的结论，按要求未改；本轮结论以本节 verdict 为准。

## 决定性证据

- 修正差异逐行阅读：
  - `Abstractions/Mail/IMailboxRepository.cs`：删除 `FindRecipientAsync`。
  - `Persistence/Mail/SqliteMailboxRepository.cs`：删除对应 SQL（原 128–133 行）。
  - `Application/Mail/MailboxQueryDomainService.cs`：构造器新增 `ICharacterIdentityQueryDomainService identities`，
    `FindRecipientAsync` 改为 `identities.FindByNameAsync` 后映射为 `MailRecipient(Character, Name, Level, Job, GrowType)`，字段一一对应。
  - `Protocol/Game/GameProtocol.cs`：删除 `RequestBlacklistType`（Round 1 N7 的一部分）。
  - 测试：`FriendCommandTests.Every_name_lookup_resolves_the_same_live_character`（真实组合的容器与 SQLite；有效角色两条路径得同一 ID 与名字，
    已删除角色两条路径都为空）；`Persistence.Tests/Support/CharacterSide.cs` 手工构造处补上真实的 `CharacterIdentityQueryDomainService`。
- 全仓检索（`Server/`、`Tool/` 的 `*.cs`）：
  - 按名字查有效角色的 SQL 现在只剩 `SqliteCharacterIdentityRepository.FindLiveByNameAsync` 一处；`IMailboxRepository` 已无该成员，也无残留实现。
  - 调用方：邮件发送 `MailboxSendDomainService.cs:19`、邮件查询 `GameMailboxCommand.cs:87`、私聊 `GameSendChatCommand.cs:139`、
    1:1 建会话 `ConversationCommands.cs:77` 经 `IMailboxQueryDomainService.FindRecipientAsync`，现委托到角色领域；
    好友 `UnitedFriendCommands.cs:64/136`、黑名单 `BlacklistCommands.cs:49/106` 直接用 `ICharacterIdentityQueryDomainService.FindByNameAsync`。
    五类入口最终都落到同一个 `CharacterIdentityQueryDomainService.FindByNameAsync`。
  - `RequestBlacklistType` 在代码、`protocol/`、`Document/` 中已无引用。
- 测试运行：采用主代理在 HEAD 上带 PVF 的全量结果——Application 6701（较 Round 1 的 6700 多出新增用例）、Protocol 2282、Persistence 466、
  Host 243（1 跳过）、Networking 218、DbMigrate 27，全部通过。本轮未复跑。

## B1 处置：已解决

- **重复判定已消除**：只剩角色领域一套「按名字字节找有效角色」实现，邮件侧不再持有自己的口径；以后改匹配规则只需改一处。
- **同一个 owner**：私聊、1:1、邮件经邮件查询服务委托，好友、黑名单直接调用，全部经 `ICharacterIdentityQueryDomainService`。
  Round 1 指出的「拉黑 X」与「对 X 开 1:1 时查黑名单」由两套查找决定 X 是谁的问题不再存在。
- **新测试**能拦住两条路径再次分叉（有效角色一致、已删除角色都找不到）。私聊与 1:1 经同一 `IMailboxQueryDomainService` 实例，
  由该测试间接覆盖，已足够。
- **行为差异**：角色领域对空名字直接返回空而不查库，原邮件 SQL 会去查。只有存在空名字有效角色时结果才会不同，现实中不出现；
  邮件发送、查询拿到空结果后的处理（`CharacterNotFound`／返回无收件人）不变。参数为 null 时两者都抛异常。不构成回归。
- **事务**：`MailboxSendDomainService` 在 `AcquireWriteAsync` 之后查收件人。两个仓储共用同一个 `SqliteDatabase`，与原实现处于同一连接与环境事务，
  加锁后读取的语义不变。

## 依赖合规

- 方向：`Application/Mail` 的查询服务依赖 `Abstractions/Characters` 的只读查询契约，属于业务服务依赖另一领域的读能力，没有越层，
  也没有读取内容源或规则输入；项目引用图未变（同在 Application 与 Abstractions 内）。
- 依赖环：`CharacterIdentityQueryDomainService` 只依赖 `ICharacterIdentityRepository`，是叶子节点；既有的
  `CharacterEntryAssembly → IMailboxQueryDomainService` 加上本次的 `MailboxQueryDomainService → ICharacterIdentityQueryDomainService`
  不成环。两者都是单例，没有生命周期俘获。`DependencyGraphArchitectureTests`（生产组合容器的环检查）、
  `RequiredDependencyArchitectureTests`、`ValidateOnBuild`／`ValidateScopes` 都在本次通过的 Application／Host 测试内。
- 是否算 ArchitectureReview 阻断行「用转发门面绕开依赖环与所有权」：不算。这里没有要绕开的环，所有权明确在角色领域；
  `MailboxQueryDomainService.FindRecipientAsync` 是邮件契约对 `MailRecipient` 的适配，正是 Round 1 给出的最小修正之一。

## Round 1 其余结论：无回归

本次修正只动邮件查找、一个协议常量与两处测试支撑，没有触及关系领域、好友／黑名单切片、推送、选角、清理计划、结构 v5 与协议文档。
Round 1 的 AC 对照、用户硬约束、跨 Task 一致性与架构结论保持不变。非阻断项更新如下：

- N7：`RequestBlacklistType` 已删除；`check-server-documentation-sync.py` 的主干复核建议仍在。
- N1–N6、N8：未处理，结论与建议同 Round 1。N1、N3 仍建议在 PR 前处理或由用户明确接受。

## 新的非阻断发现

- **N9 注释方向已反**（`Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterIdentityRepository.cs:10-11`）：remarks 写的是
  「The name match is the mailbox recipient lookup's」，说明匹配口径来自邮件收件人查找；现在邮件查找已删除，并委托到这里。
  建议改成直接陈述口径（存储字节，旧文本列转字节兜底，精确比较），不再引用邮件。
- **N10 私聊与 1:1 经邮件契约找人（命名级，修正前就存在）**：`GameSendChatCommand` 与 `ConversationCommands` 注入
  `IMailboxQueryDomainService` 只为按名找角色，现在它只是转到角色领域。按 ArchitectureReview「名称或接口形状可能误导，但职责与依赖正确」属于非阻断；
  后续可让两者直接注入 `ICharacterIdentityQueryDomainService`，邮件查询服务只保留给邮件用。

## 处理方向

- B1 已关闭，本轮无阻塞发现，完整变更范围的代码审查结论为通过。实机部分（AC-071、SC-1–SC-7 实机项、SC-10 用户反馈）仍待用户完成，不在本结论内。
- N9 改动很小，建议在 PR 前顺手改掉；N1、N3 处置见 Round 1；其余记入 #60 后续批次。
