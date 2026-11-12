# NOTI `0x016E` `LEAVE_USER_FROM_GROUP`

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
| Type | `0x016E` | 同上 |
| 名称 | `LEAVE_USER_FROM_GROUP` | upstream NotiPacketType |
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
| 0 | 4 | `u32` | 会话 id（u32），按它找窗口；其后为离开者名字 dstr（< 128 字节） | Confirmed | 客户端 1F8244D3… handler VA 024EA5E0，取自 `[ebp - 0x134]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x020EA5E0` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## A21

地址均为 VA（RVA = VA − `0x400000`，见 [game-frame](../../patterns/game-frame.md)），客户端 SHA-256
`1F8244D3…`（全值见 [evidence.md](../../evidence.md)）。来源标签：ClientStatic = 该客户端静态反汇编；
70Reference = 70 版服务端 `df_game_r` 对照；CurrentImplementation = 本仓实现。**无实机抓包或实机确认。**

1:1 对话的「对方离开」推送。

### 布局（ClientStatic，Confirmed）

handler VA `024EA5E0`：

```text
u32  conversationId
dstr name            # 离开者；< 128 字节
```

### 客户端行为（ClientStatic，Confirmed）

按 id 找窗口，从中删去同名成员，显示文案 46460（「%s さんが退場しました。」）。**窗口不关、id 不变**，留守方仍可在
窗口里发 [CMD 0x0011](../client-to-server/0x0011.md) mode 43。

### 本仓实现（CurrentImplementation）

`ConversationLeftMessage` / `ConversationLeftEncoder`；`ChatConversationLeftHandler` 只给留守方组帧。
代码：[`OneToOneChat/`](../../../Server/src/Dfo.Server.Protocol/Game/OneToOneChat/)。

| 字段 | 取值 |
|---|---|
| conversationId | 结束的会话 |
| name | 离开者在开会话时读取的存储角色名 |

- 两个来源：成员关窗（[CMD 0x01A5](../client-to-server/0x01A5.md)），或成员的选中会话结束（下线、回选人、换频道；
  由 `SelectedSessionCleanupService` 结束其全部会话，`CleanupNotificationDelivery` 在其余清理通知之后发出）。
- 只发给仍在开窗时那个选中会话里的留守方；留守方已离线或换了选中会话则不发。
- 本帧发出时会话已在服务端结束。留守方之后在该窗口发的 mode 43 被服务端静默丢弃；双方再次发起 0x01A3 会开新会话、
  各收一条 NOTI 0x016A，即各多一个新窗口，旧窗口留在原处。
- 名字 1..127 字节，超限编码器抛异常。**最大 body 135 字节** = 4 + 4 + 127。

自动测试：
[`ConversationCodecTests`](../../../Server/tests/Dfo.Server.Protocol.Tests/Game/OneToOneChat/ConversationCodecTests.cs)（`Left_frame_is_id_and_the_leavers_name`）、
[`SendChatCommandTests.Conversation`](../../../Server/tests/Dfo.Server.Application.Tests/Chat/SendChatCommandTests.Conversation.cs)（`Only_the_member_left_behind_is_told_who_left`、`Closing_the_window_ends_the_conversation_and_tells_the_other_member`）、
[`SelectedSessionPartyCleanupTests`](../../../Server/tests/Dfo.Server.Application.Tests/Sessions/SelectedSessionPartyCleanupTests.cs)、
[`CleanupNotificationDeliveryTests`](../../../Server/tests/Dfo.Server.Protocol.Tests/Game/CleanupNotificationDeliveryTests.cs)、
[`ChatWireTests`](../../../Server/tests/Dfo.Server.Networking.Tests/Integration/ChatWireTests.cs)（真实 TCP）。

### 未解问题

| 问题 | 最便宜的证据 |
|---|---|
| 离开提示是否显示、留守方之后发言（被丢弃）时窗口有无异常 | 两个客户端实机，一方关窗或下线 |
