# 邮箱系统现状与接入边界（2026-09-24）

范围：本服 `DNF-JP-86-Server` 的静态源码清点；`E:\Project\86JP` 只作旧行为线索。本文不裁定邮件业务模型，也不代表 A21 客户端实机验收。后续人偶邮件的原计划见 [`mail.md`](../../掉落重构与任务助手后续计划/mail.md)。

## 本服已有的能力

| 层面 | 现状与证据 |
|---|---|
| 建表 | `mailbox_messages`、`mailbox_recipients`、`mailbox_attachments` 已建。消息包含发件人、收件角色、标题/正文、金币、类型、来源协议、创建/到期与无限期标志；收件人包含文件夹、已读/保存/删除/金币已收标志；附件含物品 ID、数量、来源位置、旧分解字段、`item_core` 和 `detail_json`。见 `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:264-266`。后续追加列兼容声明见同文件 `:375-405`。表存在不等于有发送或领取写路径。 |
| 存储接口 | `IMailboxRecipientTable` 只有 `ReadInboxAsync`，`IMailboxMessageTable` 只有页头/正文读，`IMailboxAttachmentTable` 只有 `ReadAsync`；对应 SQLite 实现也只见读方法。见 `Server/src/Dfo.Server.Abstractions/Persistence/CharacterEntryTables.cs:240-273`、`Server/src/Dfo.Server.Persistence/Characters/CharacterMailboxTables.cs:14-27,49-122,125-146`。三个表已注册到容器，见 `Server/src/Dfo.Server.Persistence/PersistenceModule.cs:155-160`。 |
| 入场读取 | 只取角色 `folder=0` 且未保存、未删除的收件行；到期判断用数据库时钟；活跃信件按创建时间与 ID 取最老 20 封，再倒序展示。已领金币标志仅影响入场显示金额；附件逐封读取。见 `CharacterMailboxTables.cs:16-26,49-75,95-121`、`Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs:499-550`。 |
| 客户端投影 | 入场状态转为 `CharacterMailboxEntryState`，`SelectedCharacterStateMessageFactory` 把它加入入场序列，`MailboxStateMessage` 编为 Game `NOTI 0x0061`。见 `Server/src/Dfo.Server.Application/Characters/CharacterEntryStateServices.cs:200-226`、`Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectedCharacterStateMessageFactory.cs:110`、`Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs:433-437,624`。编码器可写附件/金币摘要和信件列表，见 `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs:626-663`。协议目录仅确认 encoder 身份，并明确目前没有独立邮箱切片；见 `protocol/catalog.md:588`、`protocol/game/server-to-client/NOTI-0x0061.md:138-149`。 |

## 缺口与协议边界

本次在 `Server/src/Dfo.Server.Protocol` 内按 `Mailbox/mailbox` 与相关 opcode 检索，仅找到上述 `0x0061` 入场同步，没有邮箱收件、打开、发送、读取状态、领取、删除、在线新邮件通知的切片或注册。当前三张表的抽象与实现也无写方法。因此离线创建、原子领取、防重复投递和在线刷新尚不能由本服现成邮箱服务完成。这是源码范围结论，不推断客户端是否发送过这些帧。

客户端协议目录列有 CMD `0x005E MAILBOX_SEND`、`0x005F MAILBOX_EXTRACT_ITEM`、`0x0060 MAILBOX_OPEN`、`0x0086` 邮件操作、`0x013B MULTI_MAILBOX_SEND`、`0x0144 QUERY_CHARAC_INFO_MAILBOX`；S2C 还有 `NOTI 0x0061` 列表、`0x0062` 移除、`0x0063` 提醒。见 `protocol/game/index.md:110-112,275-278,967-969`、`protocol/game/client-to-server/0x0086.md:30-31`。`0x005F` 的 4 字节请求体布局已由客户端构包确认，但 `claimObjectId` 语义取自旧上游 handler，不能据此声称本服领取已工作或 A21 实机效果已验证；见 `protocol/game/client-to-server/0x005F.md:20-44`。`0x005E/0x0060/0x013B` 在目录中仍是结构未知，`0x0144` 仅部分可得；见 `protocol/game/index.md:110-112,275-278`。

## 邻接能力与可能复用点（非设计裁定）

- 本服已有一次用例一个事务的环境：`DomainTransactionScope.Begin` 开启，提交/释放负责收口；SQLite 写操作要求当前事务。见 `Server/src/Dfo.Server.Application/Transactions/DomainTransactionScope.cs:13-59`、`Server/src/Dfo.Server.Persistence/Database/SqliteDatabase.cs:85-102`。邮件记录与附件领取如何加入同一结算边界，需要单独设计。
- 角色持有金币的 `AddGoldAsync` 由携带上限规则与持久化原子加法处理，达到上限可能只加部分；邮件领取须明确全额失败、部分领取或留待后续等语义。见 `Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:108-142`。
- `IItemGrantDomainService.GrantAsync` 能按物品分类进入账号材料袋或角色容器，角色容器 `AddAsync` 用到达规则判堆叠/空位；二者是可调查的领取接入点，不表示它们已支持邮件附件的完整实例属性、锁定与到期语义。见 `Server/src/Dfo.Server.Abstractions/Inventory/IAccountMaterialBag.cs:45-63`、`Server/src/Dfo.Server.Application/Inventory/AccountMaterialBag.cs:125-148`、`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:409-424`。
- 旧文档提过溢出奖励占位并待接邮件，见 `Docs/新版背包架构业务接入规范.md:78`；当前源码未找到邮箱溢出投递实现，不能把该文档当作已接入证明。
- `mail.md` 明确先做 #44 邮件基础，再做人偶每日发放；四个候选物品、6 点规则、未领取阻止次日发放、召唤能力都待核实，不能随邮箱基础一并假定。见 `Document/掉落重构与任务助手后续计划/mail.md:1-19`。

## 旧实现只提供行为线索

`E:\Project\86JP\Server\DfoServer\Game\Mailbox\MailboxService.cs:17-113` 暴露玩家发送、系统单封/批量邮件、活动批处理、过期维护、入箱读取与领取；旧 handler 有打开、发送、领取、删除/标记及刷新路径，见 `E:\Project\86JP\Server\DfoServer\Network\Handlers\MailboxHandler.cs:112-125,165-178,323-369,448-520`。旧仓储有系统邮件幂等/审计/活动表线索，见 `E:\Project\86JP\Server\DfoServer\Game\Mailbox\MailboxRepository.cs:1897-1950,2193-2247,1417-1525,2449-2497`。这些是旧服务端自身行为；其架构和数据库扩展不应直接移植为本服事实或 A21 客户端协议结论。

## 共同设计时须决定/验证

1. #44 的最小边界是仅系统邮件与领取，还是同时做玩家邮件的查询、发送、保存、删除、提醒与在线刷新？每个纳入的 CMD/NOTI 都需要本服协议记录、解码/编码、命令与实帧验证。
2. 系统邮件的收件人归属、离线投递、发件身份、模板/附件快照、有效期、审计、重复请求键与失败重试如何表达？现有三张表未提供明确的系统投递幂等键或审计表。
3. 领取的原子边界要覆盖收件人归属及未删除/未过期检查、附件/金币状态、防重、背包放置与金币上限；附件领取状态是删除附件、加标记还是另设领取记录？当前附件表没有领取标志，不能仅凭入场读取推定旧处理方式。
4. `item_core`/`detail_json` 和旧分解列如何与当前物品领域一致？`Docs/Refactor/TableOwnershipRuling.md:494-518,627,641` 将附件留给邮箱领域，并将旧形态转换列为待邮件收发落地时裁定的问题。
5. 人偶日投递在 #44 能力上应另行核对业务日起点、首次购买/续购、离线与跨日、未领取影响、订阅到期，以及四个 PVF 物品和 APC 使用能力；见 `mail.md:1-19`。
