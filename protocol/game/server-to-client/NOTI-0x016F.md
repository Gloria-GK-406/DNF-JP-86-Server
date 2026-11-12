# NOTI `0x016F` `MESSAGE_GROUP_CHAT`

<!-- rks:machine:begin -->

> **本区由静态分析自动生成，重新运行会整体覆盖；请勿手工编辑。**
> 需要修正字段含义时改 `patterns/data/meanings/*.tsv`，需要修正布局请改提取器。
> 本区之外的一切内容属于人工区，重新生成不会触碰。

## 身份

| 项 | 值 | 来源 |
|---|---|---|
| Endpoint | Game | 帧协议基线 |
| Direction | Server → Client | 接收注册表普查 |
| Command 族 | `0x00`（NOTI） | 客户端两张注册表的分表证据 |
| Type | `0x016F` | 同上 |
| 名称 | `MESSAGE_GROUP_CHAT` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 4   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | 会话 id（u32），按它找窗口，找不到则丢弃整条；其后为发送者名字 dstr（< 128 字节）与正文 dstr（< 1024 字节） | Confirmed | 客户端 1F8244D3… handler VA 024EA700，取自 `[ebp - 0x934]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x020EA700` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## A21

地址均为 VA（RVA = VA − `0x400000`，见 [game-frame](../../patterns/game-frame.md)），客户端 SHA-256
`1F8244D3…`（全值见 [evidence.md](../../evidence.md)）。来源标签：ClientStatic = 该客户端静态反汇编；
70Reference = 70 版服务端 `df_game_r` 对照；CurrentImplementation = 本仓实现。**无实机抓包或实机确认。**

1:1 对话窗口里对方说的一句话。

### 布局（ClientStatic，Confirmed）

handler VA `024EA700`：

```text
u32  conversationId
dstr sender          # < 128 字节
dstr text            # < 1024 字节
```

### 客户端行为

- 按 id 找窗口，找不到则静默丢弃（Confirmed）。
- **sender 等于接收方自己的名字时丢弃**（Confirmed）：发言方客户端在发出 [CMD 0x0011](../client-to-server/0x0011.md)
  mode 43 时已把自己的话写进窗口（High），所以本帧只需要发给对方。
- 另有一处疑似屏蔽名单检查（Hypothesis）。

机器区只读出首个 `u32`；其后两个 dstr 的偏移随名字长度变化。

### 本仓实现（CurrentImplementation）

`ConversationMessage` / `ConversationMessageEncoder`，由 [CMD 0x0011](../client-to-server/0x0011.md#会话窗口内发言)
mode 43 触发。代码：[`Game/ChatConversationFrames.cs`](../../../Server/src/Dfo.Server.Protocol/Game/ChatConversationFrames.cs)（帧与编码器），
发送在 [`SendChat/`](../../../Server/src/Dfo.Server.Protocol/Game/SendChat/)。

| 字段 | 取值 |
|---|---|
| conversationId | 请求的 aux |
| sender | 发言者的存储角色名（发言时读取，不取请求尾部的名字） |
| text | 请求正文原样 |

- 只发给会话中的另一方，且对方须仍在开窗时那个选中会话里；发言者本人不收。
- 正文 1..1023 字节、发言者名字 1..127 字节；不满足时这句话被静默丢弃（见 0x0011），编码器对超限字段直接抛异常。
- **最大 body 1162 字节** = 4 + 4 + 127 + 4 + 1023。

自动测试：
[`ConversationCodecTests`](../../../Server/tests/Dfo.Server.Protocol.Tests/Game/OneToOneChat/ConversationCodecTests.cs)（`Message_frame_is_id_sender_and_text`）、
[`SendChatCommandTests.Conversation`](../../../Server/tests/Dfo.Server.Application.Tests/Chat/SendChatCommandTests.Conversation.cs)（`A_conversation_line_reaches_only_the_other_member_and_is_never_answered`）、
[`ChatScopeDomainServiceTests`](../../../Server/tests/Dfo.Server.Application.Tests/Chat/ChatScopeDomainServiceTests.cs)（`Conversation_reaches_its_members_only_from_the_sessions_they_opened_it_on`）、
[`ChatWireTests`](../../../Server/tests/Dfo.Server.Networking.Tests/Integration/ChatWireTests.cs)（真实 TCP：对方收到的 body 逐字节）。

### 未解问题

| 问题 | 最便宜的证据 |
|---|---|
| 对方窗口的显示、发言方自己那一行是否只出现一次 | 两个客户端实机在窗口里互发 |
| 疑似屏蔽名单检查的条件 | 实现屏蔽名单后实机对照 |
