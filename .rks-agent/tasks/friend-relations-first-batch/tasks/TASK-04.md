# TASK-04 — 黑名单接入聊天与 1:1 对话

## Task Unit Contract

- Revision: 1
- Outcome and references: Plan T-006、SD-7。覆盖 RQ-041 – RQ-043，验收 AC-040、AC-041：
  - 收件人拉黑了发言者时，第一批全部聊天频道都不送达该收件人；
  - 任一方拉黑另一方时，1:1 建会话给发起方回结果码 77；
  - 解除拉黑后恢复。
- Owned boundary:
  - `Server/src/Dfo.Server.Protocol/Game/SendChat/GameSendChatCommand.cs`（收件人过滤）；
  - `Server/src/Dfo.Server.Protocol/Game/OneToOneChat/` 下的 `ConversationCommands.cs`、`Messages.cs`、`ConversationPayloadCodec.cs`、`OneToOneChatRegistration.cs`（拒绝帧）；
  - `Server/src/Dfo.Server.Protocol/Game/ChatOnTheWire.cs`：只改 `ChatPreSendChecks` 的注释，使之与「按收件人过滤在投递处」一致；
  - 对应测试：`Server/tests/Dfo.Server.Application.Tests/Chat/`、`Server/tests/Dfo.Server.Protocol.Tests/Game/OneToOneChat/`；
  - 协议页 `NOTI-0x016A.md`、`0x0011.md`、`0x01A3.md` 的人工区，以及聊天说明文档中关于黑名单检查点的段落（若存在，例如 `Document/tasks/` 或协议页里的聊天路由说明）。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx`；
  - `Dfo.Server.Application.Tests` 与 `Dfo.Server.Protocol.Tests` 全部通过（现有聊天测试不回归）；
  - 新增测试覆盖 AC-040 的每一种频道（区域、队伍、私聊、频道喇叭、服务器喇叭、1:1 窗口内发言）、「只有发言者拉黑了收件人」不影响投递、解除后恢复，以及 AC-041 的双向拒绝、77 帧字节和解除后可建立；
  - `Dfo.Server.Host.Tests` 全部通过（新增的出站 CLR 类型要有编码器登记）。
- Dependencies: TASK-02
- Result review: not-required — 改动集中在两个已有 CMD 与一个拒绝帧，边界清楚；现有聊天测试能发现投递回归，新增测试能直接判定过滤与 77 是否正确。

## Detailed Plan

**1. 聊天收件人过滤**（`GameSendChatCommand.HandleAsync`）
- 位置：`members` 算出、除去发言者自身之后，投影之前。
- 当 `members.Count != 0` 时，在 `units.ReadOnlyAsync` 中调用 `ICharacterRelationQueryDomainService.ObserversOfAsync(speaker.CharacterId, CharacterRelationKind.Blacklist)`，得到拉黑了发言者的角色集合，再从 `members` 中剔除。
  - 可以并入现有 `ReadFactsAsync` 那个只读单元，也可以单开一个；选择让事务次数最少的写法。注意 `ReadFactsAsync` 目前只在部分路由执行。
- 喇叭的扣除与 `followUp` 不受影响：
  - 剔除后 `members` 为空时，沿用现有分支，把 `followUp` 直投给发言者；
  - 发言者自己在收件人里时（区域、喇叭），他不会拉黑自己，不受影响。
- 私聊目标拉黑了发言者：`members` 变空，静默，不回错误，与「目标不可达」一致。

**2. 1:1 建会话拒绝**（`GameOpenConversationCommandHandler`）
- 在 `ReadMembersAsync` 找到目标之后，用两次 `ExistsAsync` 检查双向黑名单：requester→target、target→requester。任一存在，就结束为「被拒」。
  - 不建立会话；
  - 只给发起方的连接直投一条 NOTI 0x016A，body 为 `[77:u8]`（客户端在 77 时只读这一个字节并弹 4763）；
  - 目标不收任何消息。
- 新增出站消息 record，例如 `ConversationRefusedMessage(byte Result)`，`MessageType` 与 `ConversationOpenedMessage` 相同，都是 `GameConversationOpened`。为它新增编码器并登记，使用 `IGameCommandOutbox.RegisterNotiAsync`；这条回给请求方本人，不走人群投影。
- 结果码常量 77 写在 codec 内，注明证据：`research/conversation-re-20261112.md`，以及 `NOTI-0x016A.md` 已记录的 77 → 4763。

**3. 注释**

`ChatPreSendChecks` 的说明改为：只放与收件人无关的发言者检查（禁言、限频）；黑名单按收件人决定，在投递处过滤。

**4. 测试**（测试先行）
- 在现有聊天命令测试夹具上：
  - 预置 R 拉黑 S，逐个频道断言 R 收不到、其他人收到；
  - 喇叭照常扣除；
  - 只有 S 拉黑了 R 时，R 照常收到；
  - 解除后 R 收到。
- 1:1：
  - A 拉黑 B、B 拉黑 A 两种情况下，A 发起都只收到 `[77]` 帧，B 无帧，会话未建立；
  - 解除后可正常建立；
  - 拒绝帧的 codec 字节测试。

**5. 文档**
- `NOTI-0x016A.md`：本仓现会发结果码 77 的形态（CurrentImplementation）。
- `0x0011.md` 或聊天路由说明：补一句收件人黑名单过滤。
- `0x01A3.md`：补拒绝路径。

## Result

- Outcome: done
- Round: 1，worker 实现；owned change 为当前未提交 diff（TASK-05.md 除外）。
- Changed:
  - `SendChat/GameSendChatCommand.cs`：收件人去掉发言者之后、非空时，剔除 `ObserversOfAsync(speaker, Blacklist)` 得到的拉黑者。已经读取 facts 的路由在同一个只读单元里一起读出拉黑者；区域与队伍只在有收件人时才多开一个只读单元 `ChatBlockers`。剔除发生在喇叭扣除之后。
  - `OneToOneChat/ConversationCommands.cs`：双向 `ExistsAsync`。命中时只给发起方直投 `ConversationRefusedMessage`，body 为 `[0x4D]`，不建立会话。这个检查排在「会话已开」检查之前。
  - `OneToOneChat/Messages.cs`、`ConversationPayloadCodec.cs`、`OneToOneChatRegistration.cs`：拒绝帧 record、编码器与登记。
  - `ChatOnTheWire.cs`：注释改为「`ChatPreSendChecks` 只做与收件人无关的检查；黑名单在投递处与开会话处处理」。
  - 测试：`Application.Tests/Chat/SendChatCommandTests.Blacklist.cs`（新增）、`Protocol.Tests/Game/OneToOneChat/ConversationCodecTests.cs`。
  - 协议页人工区：`NOTI-0x016A.md`、`0x01A3.md`、`0x0011.md`。
- Why:
  - 拒绝帧做成无参数 record，常量 77 放在 codec 里，与 `SendChatRefusal` 写法一致。这样就不需要按结果码分支（SD-7 的措辞由此收窄）。
  - 私聊目标拉黑了发言者时静默，按 Spec RQ-041 执行。客户端 NOTI 0x0041 的状态 0x4D 会弹黑名单提示，这次没有用，留给用户决定。
- Checks:
  - build 0 警告 0 错误；
  - Protocol.Tests 2173 通过；Application.Tests 5964 通过（594 跳过为原有）；Host.Tests 230；Networking.Tests 189，均无失败；
  - RED：16 个失败均为断言不符；GREEN：103/103。
- Remaining：77 的弹框（4763）与私聊静默的表现待实机（T-007）；catalog 留待最终整理。
