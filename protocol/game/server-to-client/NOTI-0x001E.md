# NOTI `0x001E` `FINISH_LOADING`

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
| Type | `0x001E` | 同上 |
| 名称 | `FINISH_LOADING` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 5   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x18]` |
| 4 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0xd]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x008FB420` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本帧尚无 recv 侧抓包，但**已由服务端实际发出并生效**（实机 2026-08-18）。

## 它是加载条的闸门

客户端发 [`0x0025 FINISH_LOADING`](../client-to-server/0x0025.md) 后，服务端回一个 CMD ACK
再推本帧，加载条才走满。缺本帧的实机表现是：进度条停在最后一小格，数秒后客户端报连接失败。

body 为 **5 字节全零**（上游 `Network/Builders/Town/FinishLoadingBuilder.cs`：
`WriteInt32(0x00000000)` + `WriteByte(0x00)`）。两个字段都恒为常量，无可变部分。

## 服务端实现映射

`Server/src/Dfo.Server.Protocol/Game/FinishLoading/FinishLoadingSlice.cs`。已实现并实机验证。
