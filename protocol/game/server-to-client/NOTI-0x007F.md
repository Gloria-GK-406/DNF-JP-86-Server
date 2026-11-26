# NOTI `0x007F` `GUILD_MEMBER_INFO`

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
| Type | `0x007F` | 同上 |
| 名称 | `GUILD_MEMBER_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 1   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 1]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D8EC10` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

**ClientStatic 与同版本上游参照 · High。** A21 handler VA `0118EC10` 读取一个 `u8`。ServerS4A21 `1870c85` 的 `GuildJoinPacketBuilder.Rank` 和《服务端业务开发规范》记录，客户端经 `01833DA0` 将它写到当前角色的 `+0x5C14`，管理权限判断读取该字段。

| 字节值 | 上游含义 |
| --- | --- |
| `00` | 无公会，清除上一角色的职级 |
| `01` | 会长 |
| `04` | 普通成员 |

**CurrentImplementation / WireCaptured。** 2026-11-26 的隔离线路探针收到 `GuildPilot` 选角的 `01`、`GuildAlt` 选角的 `00`，以及新建公会后、最终 ACK 前的 `01`。使用真实 A21 客户端确认按钮状态仍待完成。复测入口见 [公会创建验收](../../../Document/tasks/guild-system/attempts/spec-002-create-guild-in-a21/a21-user-test.md)。
