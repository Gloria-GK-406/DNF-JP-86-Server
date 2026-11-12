---
kind: rks-verification
decision: pass
---

# Completion Evidence

**Claim**：分支 `feature/61-chat-routing` 在 `37f1ff312` 交付了 TaskContract revision 6：第一批频道与 1:1 对话窗口，自动验证全部通过，用户实机验收通过。

## 已验证

| 条件 | 结果 | 证据 |
|---|---|---|
| SC-1～SC-6、SC-10 的自动部分（收件人集合、回显规则、拒绝集合 0–255 穷举、`0x01A9` 同 type 拒绝、喇叭扣除与失败不扣、正文与名字上限、公会 6 静默） | 通过 | `Server/tests/Dfo.Server.Application.Tests/Chat/SendChatCommandTests.cs`、`ChatScopeDomainServiceTests.cs`；`Server/tests/Dfo.Server.Protocol.Tests/Game/SendChat/SendChatCodecTests.cs`；真实 TCP `Server/tests/Dfo.Server.Networking.Tests/Integration/ChatWireTests.cs` |
| SC-11 1:1 开窗、窗口内收发、重复请求不重发、目标不在线无回包、mode 43 不回拒绝（自动部分） | 通过 | `SendChatCommandTests.Conversation.cs`、`ChatConversationDomainServiceTests.cs`、`Protocol.Tests/Game/OneToOneChat/ConversationCodecTests.cs`（字节布局与最大 body）、`ChatWireTests` 真实 TCP（双方 `0x016A` 名单顺序、`0x016F` 只到对方） |
| SC-12 关窗、下线、回选人、选角顶替时对方收到 `0x016E`（自动部分） | 通过 | `SendChatCommandTests.Conversation.cs`（关窗）；`SelectedSessionPartyCleanupTests`（清理计划）；`CleanupNotificationDeliveryTests`（断线投递）；`SelectCharacterLifecycleTests.A_selection_that_replaces_an_older_session_tells_the_members_left_in_its_conversations`（去掉投递即失败）；`ChatWireTests` 真实 TCP 回选人（去掉修正即失败）与同账号另一连接重新登录 |
| SC-7 成员来自 owner；会话成员只由会话 owner 内存保存一份；架构与状态普查 | 通过 | `ChatScopeDomainService` 只依赖在线会话、城镇区域、组队、会话 owner；`ChatConversationDomainService` 登记为写入口（`GameplayWriteEntryTableTests`），`GameplayStateCensusTests` 通过；结果评审 `reviews/change-review.md`（第一批，approved）、`reviews/change-review-conversation.md`（1:1，第 2 轮 approved） |
| SC-8 构建与测试 | 通过 | `artifacts/chat-routing/verify-conversation-f1.log`（不入库）：在含 F-1 修正的工作树上 `--no-incremental` 构建 0 警告 0 错误；Application 6606，Protocol 2204，Networking 218，Host 243 通过 1 跳过（既有跳过）。`artifacts/chat-routing/verify-37f1ff312.log`：在 `37f1ff312` 上 Application 6607、Protocol 2204 全过。`37f1ff312` 相对前一次全量只改了一条 Application 测试、一段 Protocol 注释与协议文档；Networking 与 Host 未在该提交上重跑（Host 输出被运行中的测试服占用），沿用前一次结果 |
| SC-8 协议记录 | 通过 | 提交 `c914f06b2`（第一批）、`bda5fda28` 与 `07322ce48`/`37f1ff312` 中的文档部分：`0x0011`、`0x01A9`、`0x01A3`、`0x01A5`、`0x01A6`、NOTI `0x000C`／`0x0041`／`0x0076`／`0x016A`／`0x016E`／`0x016F`、`catalog.md`；与客户端证据矛盾的上游含义行已替换；无 ClientVerified、无 WireCaptured |
| SC-9 测试服就绪与编号流程 | 通过（就绪部分） | `artifacts/chat-routing/run-test-server.sh` 运行中（Game 41010／41011／41100，启动器 41080，构建含 F-1 修正）；流程 `Document/tasks/chat-routing/用户测试流程.md` 第 1～23 步 |

## 用户实机验收

| 条件 | 结果 | 证据 |
|---|---|---|
| SC-1～SC-5 实机部分 | 通过 | 用户 2026-11-12 第一轮反馈：「其他的区域说话，频道说话，服务器说话，组队说话，悄悄话等都正常显示」；测试客户端 `DNFClient-A21-protocol`／`protocol2` 连本任务测试服 |
| SC-11、SC-12 实机部分 | 通过 | 第一轮发现 1:1 窗口不出现（未处理 `0x01A3`），按 revision 4–6 补齐后复测；窗口标题由客户端补丁（DNF-JP-86-ClientHack `task/conversation-title`）改为只显示对方名字，用户 2026-11-12：「符合预期，可以认为是验收成功了」 |
| SC-9 | 通过 | 测试服与编号流程如上；数据库按用户选择使用 #308 v4 副本 |

## 说明

- SC-9 的数据库：契约写「当前主干实际使用的数据库」。主检出 Release 库为 schema v0，用户在 2026-11-12 明确选择「复制 #308 副本」（v4）作为测试库，以用户选择为准。
- `protocol/patterns/data/check-server-documentation-sync.py` 不解析 `ClientMessageType`／`ServerMessageType` 枚举注册，主干上已有 438 项同类失败，不属于本任务验收条件；用能解析枚举的探针复核，本任务涉及的 type 无缺口。
- 用户验收是整体结论；喇叭数量逐次与重登一致、拒绝提示文字没有单独回报，协议页仍按 ClientStatic 记录，未标 ClientVerified。
