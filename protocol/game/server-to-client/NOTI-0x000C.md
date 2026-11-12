# NOTI `0x000C` `MESSAGE`

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
| Type | `0x000C` | 同上 |
| 名称 | `MESSAGE` | upstream NotiPacketType |
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
| 0 | 1 | `u8` | 聊天模式 mode；≥0x3B 丢弃，0x2D/0x2E 改写为 3 | Confirmed | 客户端 1F8244D3… handler VA 01189860；分派 VA 01A644C0，取自 `[ebp - 0x205]` |
| 1 | 2 | `u16` | 发送者 uid（u16） | Confirmed | 客户端 1F8244D3… handler VA 01189860，取自 `[ebp - 0x20c]` |
| 3 | 1 | `u8` | serverGroup；≥0xFA 丢弃，写 0 时客户端自行补 | Confirmed | 客户端 1F8244D3… handler VA 01189860，取自 `[ebp - 0x206]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D89860` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## A21

地址为 VA，客户端 SHA-256 `1F8244D3…`；**无实机抓包或实机确认**。

### 布局（ClientStatic，Confirmed）

handler VA `01189860`：

```text
u8   mode           # ≥ 0x3B 丢弃整帧；0x2D / 0x2E 改写为 3
u16  senderUid
u8   serverGroup    # ≥ 0xFA 丢弃整帧；写 0 时客户端自行补
dstr text           # i32 长度 + 字节，长度 1..255
```

之后无字段。mode 分派 VA `01A644C0`（表 VA `01A647E4` / `01A647FC`），通用路径 VA `01A600C0`，
颜色表 VA `01A54C60`：私聊 `ABFFA9`、队伍 `EDD568`、普通白、公会 `F54CFF`、喇叭 `00F0FF`、
GM 喇叭 `1EFF96`、系统 `9BC8E6`。

**发言者自己的那一行**：[CMD 0x0011](../client-to-server/0x0011.md) 的主发送函数（VA `01A62E20`）写包后没有
加行调用，客户端发言后不在本地显示（ClientStatic，Confirmed），发言者看到自己的话只能靠服务端回推。

70Reference：`make_packet_chat_msg`（`086c954c`）同布局。

### 本仓实现（CurrentImplementation）

用于区域与队伍聊天，收件人**含发言者本人**：

| 字段 | 取值 |
|---|---|
| mode | 请求 mode 原值 |
| senderUid | 发言者 `WireUserId` |
| serverGroup | 0 |
| text | 请求正文原样 |

**最大 body 263 字节** = 1 + 2 + 1 + 4 + 255。依据：正文超过 255 字节的请求被命令拒绝，编码器对空或超限
正文直接抛异常，不会构造出超限帧。代码：
[`SendChat/`](../../../Server/src/Dfo.Server.Protocol/Game/SendChat/)；自动测试：
[`SendChatCodecTests`](../../../Server/tests/Dfo.Server.Protocol.Tests/Game/SendChat/SendChatCodecTests.cs)（布局字节、上限长度、超限不编码）、
[`SendChatCommandTests`](../../../Server/tests/Dfo.Server.Application.Tests/Chat/SendChatCommandTests.cs)、
[`ChatWireTests`](../../../Server/tests/Dfo.Server.Networking.Tests/Integration/ChatWireTests.cs)。

| 问题 | 最便宜的证据 |
|---|---|
| 发言者是否恰好看到一次、颜色与发送者名是否正确 | 实机区域、队伍各发一条 |
