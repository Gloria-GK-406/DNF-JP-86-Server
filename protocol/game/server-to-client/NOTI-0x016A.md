# NOTI `0x016A` `CREATE_GROUP`

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
| Type | `0x016A` | 同上 |
| 名称 | `CREATE_GROUP` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 6   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 结果码：77 弹文案 4763（黑名单）并结束；100、101 静默结束；其余任何值（含 0）按成功继续读 id 与名单 | Confirmed | 客户端 1F8244D3… handler VA 024EAFA0，取自 `[ebp - 4]` |
| 1 | 4 | `u32` | 会话 id（u32），仅结果为成功时存在；写入新建窗口 +0x230，客户端不按它查重 | Confirmed | 客户端 1F8244D3… handler VA 024EAFA0，取自 `[ebp - 0x150]` |
| 5 | 1 | `u8` | 成员名数量 n（u8），其后 n 个名字 dstr（0 < len < 128 字节）；首个名字写入窗口 +0x184 作为对方；n = 0 不建窗 | Confirmed | 客户端 1F8244D3… handler VA 024EAFA0；读取原语 ReadDStrW VA 0274FCB0，取自 `[ebp - 0x131]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x020EAFA0` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## A21

地址均为 VA（RVA = VA − `0x400000`，见 [game-frame](../../patterns/game-frame.md)），客户端 SHA-256
`1F8244D3…`（全值见 [evidence.md](../../evidence.md)）。来源标签：ClientStatic = 该客户端静态反汇编；
70Reference = 70 版服务端 `df_game_r` 对照；CurrentImplementation = 本仓实现。**无实机抓包或实机确认。**

1:1 对话窗口的开窗推送，是 [CMD 0x01A3](../client-to-server/0x01A3.md) 的结果（该 CMD 没有 CMD 应答）。

### 布局（ClientStatic，Confirmed）

handler VA `024EAFA0`（NOTI 注册序列 VA `024EC373`–`024EC416`）：

```text
u8   result             # 77、100、101 时到此为止
-- 仅 result ∉ {77, 100, 101} --
u32  conversationId
u8   n
n ×  dstr name          # 0 < len < 128 字节
```

名字经 ReadDStrW（VA `0274FCB0`）读取：长度不在 (0, 128) 内时只消耗 4 字节长度字段并用默认名，其后字段全部错位。

### 客户端行为

| result | 行为（ClientStatic，Confirmed） |
|---|---|
| 77 | 弹文案 4763（黑名单），不建窗 |
| 100、101 | 静默，不建窗 |
| 其余任何值（含 0） | 按成功继续读 id 与名单 |

码值名称（70Reference：auction DWARF `ENUM_ERROR`，`DNFShared/CSCommonError.h`；值 Confirmed，game 进程同值 High）：
77 `BLACK_USER`、100 `NOT_EXIST_USER`、101 `CREATE_GROUP_FAILED`、102 `NOT_EXIST_GROUP`。

- n = 0 不建窗（Confirmed）。
- **每条成功帧都新建一个窗口，不按 id 查已有窗口**（Confirmed）：窗口 `+0x184 = names[0]`、`+0x230 = conversationId`、
  `+0x1F4 = 0`。同一会话推两次，接收方就有两个窗口。
- **名单首位是对方**（Confirmed：`names[0]` 写入窗口的对方字段）。标题（High）：成员 ≤ 1 时显示自己的名字，> 1 时取
  `names[0]`。客户端本地建窗时名单就是 [对方, 自己]，因此每个接收方都应收到 [对方, 自己]。
- 发过 0x01A3 的一方（全局 `[0x3B75084]` = 1）收到后窗口抢焦点；另一方窗口创建但只闪烁（High）。
  双方各自收到一条才各有窗口。

**不采用上游参照服务端的写法**：它把名单写成 [接收方自己, 对方]，与 `names[0]` 是对方相反；它对已开的会话会再发
本帧，按上面的不去重行为会多出窗口。机器区相应几行改由
[`meanings/chat-a21-20261112.tsv`](../../patterns/data/meanings/chat-a21-20261112.tsv) 生成。

70Reference（Confirmed）：70 没有实现 1:1 对话（见 [0x01A3](../client-to-server/0x01A3.md)）。70 的 NOTI 编号比 A21
小 1：70 `0x169`–`0x16F` 对应 A21 `0x16A`–`0x170`。

### 本仓实现（CurrentImplementation）

成功形态 `ConversationOpenedMessage` / `ConversationOpenedEncoder`，`ChatConversationOpenedHandler` 为两名成员各组一条；
黑名单形态 `ConversationRefusedMessage` / `ConversationRefusedEncoder`，命令直投发起方。
代码：[`OneToOneChat/`](../../../Server/src/Dfo.Server.Protocol/Game/OneToOneChat/)。

| 字段 | 取值 |
|---|---|
| result | 0 |
| conversationId | 服务端分配，不用 0 与 `0xFFFFFFFF`（客户端窗口的 -1） |
| n | 2 |
| names | [对方, 接收方自己]，各为开会话时读取的存储角色名 |

- 只在 0x01A3 成功开会话时发，每个会话对每方只发一次；会话已开时不重发；不发 100／101（原因见 0x01A3）。
- **结果码 77**：0x01A3 发起时任一方把另一方加入了黑名单，只给发起方的连接发 body `4D`（1 字节，result = 77，
  其后无字段），客户端弹 4763、不建窗；对方不收任何帧，不建会话。解除拉黑后同一请求按成功形态发。
- 名字须为 1..127 字节：命令遇到不合规的名字就不开会话，编码器对超限名字直接抛异常。
- **最大 body 268 字节** = 1 + 4 + 1 + 2 × (4 + 127)。

自动测试：
[`ConversationCodecTests`](../../../Server/tests/Dfo.Server.Protocol.Tests/Game/OneToOneChat/ConversationCodecTests.cs)（`Opened_frame_is_success_id_two_names_other_first`、`Blacklist_refusal_is_result_77_alone`、`Every_frame_stays_inside_what_the_client_reads_and_its_stated_maximum`）、
[`SendChatCommandTests.Conversation`](../../../Server/tests/Dfo.Server.Application.Tests/Chat/SendChatCommandTests.Conversation.cs)、
[`SendChatCommandTests.Blacklist`](../../../Server/tests/Dfo.Server.Application.Tests/Chat/SendChatCommandTests.Blacklist.cs)（双向拉黑各发起一次：发起方只收 `4D`、对方无帧；解除后正常建立）、
[`ChatWireTests`](../../../Server/tests/Dfo.Server.Networking.Tests/Integration/ChatWireTests.cs)（真实 TCP：双方 body 逐字节）。

### 未解问题

| 问题 | 最便宜的证据 |
|---|---|
| 双方窗口、标题、发起方抢焦点与被邀方闪烁 | 两个客户端实机发起一次 1:1 对话 |
| 名字按字节原样下发时客户端显示是否正确 | 同上，用含非 ASCII 字符的角色名 |
| 结果码 77 的弹框（4763）实机显示 | 一方拉黑另一方后发起 1:1 对话 |
