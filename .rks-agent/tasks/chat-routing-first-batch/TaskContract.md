---
kind: rks-task-contract
revision: 3
state: ready
---

# TaskContract: 聊天单入口路由与第一批频道

## Context

### Source Request

2026-11-11 对话中逐步形成，按时间顺序摘录用户原话：

1. 「现在我们要先看看游戏的通信系统，包括局域说话，团队，工会说话，以及频道，服务器说话等等。然后把服务器广播这条功能接进来。先看看70 server是怎么实现的，然后看看client 给我一个A21的实现方案」
2. 「先不管公告，而是现完成通信频道建模，也就是如何管理频道，服务器，小队，工会人员。这样才能在服务端快速的用一个入口接住所有的msg请求，并且能快速分流到所需的目标。」
3. 「比起你这种不如就一个IChatScopeServer然后MembersOf（charid，scope），scope可以是单类型，也可以是某个人的id。可以用基类和子类来实现。我觉得基类子类好一点，然后用is xxx来完成分流逻辑。」随后对 Agent 提出的「锚点放进范围子类、MembersOf 只收一个参数」的修订回复「按这个模型改一下看看」。
4. 「禁言和黑名单这个后续再说，在CMD或者发送前做个判断就行。等后面的好友系统弄完了再加也来得及。然后频道的话，实际上只有频道喇叭来发送，只能发送给自己所在的频道。服务器喇叭就是发送给自己服务器的所有频道。然后玩家发送服务器专用mode时候，只拒绝就行。」
5. 「私聊这种都是尽可能可达的，也就是掉线就不给发过去了，也不报错。剩下的在测试计划就行。然后私聊这个暂时就不报错吧，尽量可达形推送。」
6. 对 Agent 发布的模型第 4 版（已定项含第一批频道、服务器喇叭覆盖全部 28 个频道、`Channel(id)`/`Conversation(id)` 暂不加）回复：「ok，这个感觉还是蛮大的，走任务契约实现吧。完成后我实际验收看看效果。」

已对齐的模型：Artifact「A21 聊天路由模型」第 4 版（https://claude.ai/artifact/VfGeH8DT2Uc9uHst5qECcb）。
调查结论：工作区 `E:\Project\DNF\Document\聊天通信与服务器广播-A21方案-20261111.md`（70 服务端反汇编、A21 客户端静态分析、上游 ChatHandler 对照）。

### Current Situation

基线 `origin/main@9712ecacc`。服务端没有 CMD `0x0011 SEND_MESSAGE` 的解包器，收到后记 Warning 并丢弃；NetworkMessages 中没有 `0x0011`、`0x000C`、`0x0076` 成员。
现有可复用能力：
- 在线注册表 `OnlineStateRegistry`（三个及以上 GamePort 同进程，快照天然跨频道），会话带 `ListenerPort`、`WireUserId`。
- 城镇区域成员 `ITownAreaDomainService`。
- 组队 `IPartyService.KeyOf / FindByKey`。
- 人群投影 `IEventService.ProjectAsync(NotificationAudience.TheseCharacters, …)` 与 `INotificationHandler<T>`。

公会系统、黑名单、禁言均未实现。A21 布局结论来自 `DNF.unpacked-b1-debug.exe` 静态分析（ClientStatic），都没有 ClientVerified。

## Goal

玩家在 A21 客户端用聊天框发言时，服务端经一个入口接住 CMD `0x0011`，按聊天频道把消息投递给正确的在线收件人，收件人客户端按该频道显示；第一批覆盖区域、队伍、私聊、频道喇叭、服务器喇叭。投递范围由一个按「范围子类」分流的领域服务回答，各群组成员由各自现有 owner 提供，聊天不另存名单。

## Scope

### In

- CMD `0x0011` 的解包与唯一协议 CMD 入口；wire `mode` 在协议层映射为领域聊天频道。
- 第一批频道与 A21 mode：
  - 区域 3、45、46。
  - 队伍 2、9。
  - 私聊 1、7。
  - 频道喇叭 13（物品 37）。
  - 服务器喇叭 11（物品 36）、12（物品 898）、57（物品 10151255）。
- 喇叭按 CMD 中的背包槽位核对物品并消耗 1 个；槽位为空或物品与 mode 不符时回 `0x0011` 拒绝，不扣除、不投递。
- 正文为空或超过客户端对该回推协议的显示上限时回 `0x0011` 拒绝，不截断。
- 聊天范围领域服务：封闭基类加子类，单参数 `MembersOf(scope)` 返回人物列表。
  - 相对范围带锚点角色：PartyOf、AreaOf、ChannelOf、GuildOf。
  - 绝对范围：Server、Character。
- 投递：区域、队伍用 NOTI `0x000C`，私聊用 NOTI `0x0041`，喇叭用 NOTI `0x0076`。拒绝时给发言者回 `0x0011` 失败应答；成功时按客户端期待处理应答（A21 客户端不等成功应答，70 成功时不回）。
- 公会 mode 6：范围恒为空，静默不投递。
- `0x0011` 中第一批以外的 mode 一律回 `0x0011` 拒绝，不投递，连接保持。包括：
  - 服务器专用 mode：0、4、0x0A、0x10–0x1E。
  - 本期不支持的 mode：43、48（1:1 会话），14（GM 喇叭），15、34、50、56（其他喇叭），52–55（攻坚队／占领战），36、37、38（跨服私聊）。
  - 任何没有映射的 mode。
- 物品链接聊天是独立的 CMD `0x01A9`：回同 type `0x01A9` 的拒绝应答，不解析正文、不投递。应答布局在实现时查清并标注证据。
- 发送前预留检查点，供以后接禁言、黑名单、限频；本期不含判定逻辑。
- 协议知识库：更新 `0x0011`、`0x01A9`、NOTI `0x000C`、NOTI `0x0041`、NOTI `0x0076` 记录与 `catalog.md`，修正已知错误（1:1 会话 mode 是 43 不是 45；`0x000C` body 补全）。
- 自动测试；用当前主干实际使用的数据库准备用户测试服，提供编号测试流程。

### Out

- 系统公告、GM 全服公告、定时公告、维护倒计时，以及 `Channel(id)` 范围。
- 禁言、黑名单、限频、违禁词的判定逻辑。
- 公会系统本身与公会成员关系。
- 1:1 会话（`Conversation` 范围、NOTI `0x016F`）、GM 喇叭、攻坚队／占领战聊天、其他喇叭、物品链接聊天。
- 创建 PR、推送、合并、版本号修改。

## Root Intent Coverage

| Intent ID | Source anchor and material intent | Disposition | Contract location or distinct active owner / authority | Rationale and evidence |
|---|---|---|---|---|
| RI-1 | 请求 1：调查 70 服务端与 A21 客户端的通信系统并给出方案 | not_applicable | 已交付：`Document/聊天通信与服务器广播-A21方案-20261111.md` 与 Artifact 模型 | 调查与方案已在本契约之前完成，本契约只承接实现 |
| RI-2 | 请求 1：把服务器广播（公告）接进来 | authorized_change | 请求 2「先不管公告」 | 用户明确推迟，列入 Scope Out |
| RI-3 | 请求 2：一个入口接住所有 msg 请求并快速分流到目标 | in_scope | Goal；Scope In 第 1 项；SC-1、SC-6 | 本契约的核心结果 |
| RI-4 | 请求 2：管理频道、服务器、小队、公会人员的建模 | in_scope | Scope In「聊天范围领域服务」；SC-7 | 频道／服务器／队伍成员由现有 owner 回答；公会范围按 RI-5 处理 |
| RI-5 | 请求 2：公会人员 | preserved_upstream | GitHub issue #62「公会系统」（循环父 issue，open） | 公会成员关系由公会系统负责；本契约只保留 GuildOf 范围且恒为空 |
| RI-6 | 请求 3：范围用基类加子类、一个领域服务按子类分流；后续修订为锚点放进子类、单参数 MembersOf | in_scope | Constraints 第 1 条；Scope In | 用户明确选择的结构 |
| RI-7 | 请求 4：禁言、黑名单后续再说，在 CMD 或发送前做判断即可 | authorized_change | 请求 4；请求 6 认可的第 4 版「已定」第 3 条（禁言、黑名单、限频随好友系统再加） | 判定逻辑推迟，限频一并推迟；本期只留检查点（Scope In） |
| RI-8 | 请求 4：频道喇叭只发自己所在频道；服务器喇叭发自己服务器的所有频道 | in_scope | SC-4、SC-5 | 用户明确规则 |
| RI-9 | 请求 4：玩家发送服务器专用 mode 只拒绝 | in_scope | SC-6 | 用户明确规则 |
| RI-10 | 请求 5：私聊尽量可达，对方掉线就不发，不报错 | in_scope | SC-3；Constraints 第 2 条 | 用户明确规则 |
| RI-11 | 请求 6：按模型第 4 版的已定项实现（第一批频道、服务器喇叭覆盖全部 28 个频道、`Channel(id)`／`Conversation(id)` 暂不加） | in_scope | Scope In／Out；SC-5 | 用户对第 4 版回复「ok」并授权实现 |
| RI-12 | 请求 6：完成后实机验收 | in_scope | SC-9 | 本契约交付可连接的测试服与编号流程；验收结论由用户给出 |
| RI-13 | 仓库 `AGENTS.md` 与 `Document/FeatureDevelopment.md`：独立工作区、证据标签、当前主干数据库做用户测试、用户通过前不建 PR | in_scope | Constraints 第 3、4 条；SC-8、SC-9 | 仓库政策 |

## Constraints

- 聊天范围以封闭基类加子类表达，由一个领域服务按子类分流；需要锚点的范围在子类里带角色 id，入口只有一个 `scope` 参数（用户决定，请求 3）。
- 收件人解析尽量可达：私聊对象不在线、发言者没有队伍或公会时得到空集合，静默丢弃，不向发言者报错；拒绝只来自发送前的规则检查（用户决定，请求 5）。
- 群组成员只由其现有 owner 提供，聊天不保存第二份成员名单（用户接受的查询式模型；仓库 `Document/Architecture.md`「同一事实只由一个 owner 保存」）。
- 遵守仓库分层与架构规则（`AGENTS.md`、`Document/Architecture.md`、`Document/ProtocolCommands.md`、`Document/EventsAndNotifications.md`）：wire mode 与包体布局只在 Protocol 层出现。
- 用户测试使用当前主干服务实际使用的数据库；切换前做一致备份；数据库、备份不提交到 Git（`Document/FeatureDevelopment.md` 第 4 节）。
- 协议结论写入 `protocol/` 并标注证据标签；没有实机确认的不标 ClientVerified（`protocol/evidence.md`）。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 区域聊天（mode 3/45/46）投递给发言者所在城镇区域的在线角色，不投递给其他区域 | 自动测试覆盖收件人集合与 `0x000C` 字节；用户实机：两个客户端同区域可见、异区域不可见 |
| SC-2 | 队伍聊天（mode 2/9）投递给发言者队伍的在线成员（含副本中），不投递给非队员；无队伍时静默 | 自动测试；用户实机组队收发 |
| SC-3 | 私聊（mode 1/7）按对方名字投递给在线目标；目标不在线或不存在时不投递、不报错，发言者连接保持可用 | 自动测试；用户实机私聊在线与离线对象 |
| SC-4 | 频道喇叭（mode 13）消耗槽位上的物品 37 一个，只投递给发言者所在频道的在线角色，显示为喇叭消息 | 自动测试覆盖扣除与收件人；用户实机两客户端分处不同频道 |
| SC-5 | 服务器喇叭（mode 11/12/57）消耗对应物品一个，投递给本服务器全部频道（含决斗及特殊频道）的在线角色 | 自动测试；用户实机跨频道收到 |
| SC-6 | 第一批以外的 `0x0011` mode（服务器专用、本期不支持、未映射）收到 `0x0011` 拒绝应答；物品链接 `0x01A9` 收到同 type 拒绝应答；均不投递，连接保持可用 | 自动测试；真实 TCP 集成测试 |
| SC-10 | 喇叭槽位为空或物品不符、正文为空或超长时收到 `0x0011` 拒绝，物品数量不变，无人收到消息 | 自动测试覆盖拒绝与库存不变 |
| SC-7 | 收件人来自在线会话、城镇区域与组队的现有 owner，聊天模块没有自己的成员名单 | 代码结构；仓库架构测试全部通过 |
| SC-8 | 解决方案构建通过（TreatWarningsAsErrors），相关自动测试与架构测试通过；`protocol/` 中 `0x0011`、`0x01A9`、`0x000C`、`0x0041`、`0x0076` 记录与 `catalog.md` 已更新且标注证据 | 构建与测试输出；文档差异 |
| SC-9 | 测试服基于本分支构建、使用当前主干实际数据库（已备份）运行；向用户提供编号测试流程，覆盖上列场景及喇叭扣除数量、发言回显、重登后物品数 | 测试服就绪说明（分支／提交、连接方式、数据库路径、备份位置）与测试步骤文档 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 「ok，这个感觉还是蛮大的，走任务契约实现吧。完成后我实际验收看看效果。」（回复 Agent 发布的模型第 4 版与其已定项） | 在功能工作区 `E:\Project\DNF\DNF-JP-86-Server-chat`（分支 `feature/61-chat-routing`）实现本契约 Scope In，包括任务文档、本地提交、协议记录、自动测试，以及准备用户测试服：测试服使用当前主干服务实际使用的数据库，切换前做一致备份（备份只用于恢复）；需要停止或替换现有服务时另行确认 |

## Requires Confirmation

- 停止或替换用户正在运行的服务端进程，或在其运行时写入其数据库。
- 推送分支、创建 PR、合并到 `main`。

## Assumptions

- A21 布局与 mode 语义来自客户端静态分析：`0x000C` 读取 Confirmed；`0x0076` 读取 Confirmed，字段含义 High；喇叭 CMD 的 u32 是背包槽位，High。`0x0076` 的 serverId、serverGroup、频道号字节的取值要在实现时查清，并在实机测试中确认。
- 普通发言的 mode：A21 默认发 3；客户端功能开关 #12、#15、#16 打开时分别发 8、45、46，在类型 4 的地下城里发 5（ClientStatic，Confirmed；开关与类型 4 的含义未读出）。本契约假定测试客户端普通发言为 3／45／46；若实机发出 8 或 5，它们按「未映射」被拒绝，需回到用户决定是否纳入。
- 私聊协议依据：A21 NOTI `0x0041` handler（VA `01173D70`）读 `u8 mode, u8 状态, dstr 名字, u8 srv, dstr 正文`；70 私聊成功时给目标发 `0x41`、不给发送者回帧（70Reference，Confirmed）。发送方自己那一行的来源未读出，以实机为准。
- `0x01A9` 与 `0x0011` 在客户端注册的是同一个应答 handler（`0x00D1B480`，见 `protocol/game/client-to-server/0x01A9.md` 与 `0x0011.md`），因此推定 `0x01A9` 拒绝应答与 `0x0011` 同为 `ok, reason` 信封（High，待实现时核对）。
- 喇叭显示与扣除：客户端收到发送者名字等于自己的 `0x0076` 时会在本地扣喇叭；服务端扣除后是否再推背包变化，以实机数量一致为准，测试流程覆盖。
