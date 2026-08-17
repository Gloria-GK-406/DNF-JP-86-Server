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
已读出字段合计 = 260   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 聊天模式 mode（决定投递范围：私聊/组队/区域等） | High | 上游 Network/Handlers/ChatHandler.cs:526 WriteByte(mode) 为 body 首字节；:117-165 按同一 mode 值选择收件人，取自 `[ebp - 0x205]` |
| 1 | 2 | `u16` | 发送者 UserId | High | 上游 Network/Handlers/ChatHandler.cs:527 WriteUInt16(senderUniqueId)，:89 实参为 session.Player.UserId，取自 `[ebp - 0x20c]` |
| 3 | 1 | `u8` | 服务器组 serverGroup；上游调用点恒传 0 | High | 上游 Network/Handlers/ChatHandler.cs:528 WriteByte(serverGroup)，:90 实参 serverGroup: 0，取自 `[ebp - 0x206]` |
| 4 | 256 | `dstringw` | 消息正文 dstr（i32 长度 + 原始字节） | High | 上游 Network/Handlers/ChatHandler.cs:529 WriteDstr(messageBytes)，取自 `[ebp - 0x204]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x009312E0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
