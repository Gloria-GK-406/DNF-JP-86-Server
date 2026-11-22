# NOTI `0x024A` `RAID_DUNGEON_STATE`

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
| Type | `0x024A` | 同上 |
| 名称 | `RAID_DUNGEON_STATE` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 6 + n×5   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 顶层此处=固定常量 0（上游写死）；重复组内此处=副本 ID | Confirmed | 上游 RaidPacketBuilder.cs:337 恒写 0，:341 写 dungeonState.Key（BuildDungeonState），取自 `[ebp - 3]` |
| 1 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 1]` |
| 2 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xc]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 8]` |
| &nbsp;&nbsp;↳&nbsp;4 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 2]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D480B0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## A21 静态证据

来源为 `ClientStatic`。原版 A21、分析镜像 SHA-256 见 [客户端证据绑定](../../../Document/tasks/anton-raid/research/04-jp86-protocol-client-pvf.md#0-绑定版本与标签)，以下地址为 VA。

handler 读取 `u8 + u8 n + n×(u32 图号, u8 状态) + u32`。首字节读后未用，状态送到 `0x00C2C5B0` 原样存入。尾项由 `0x00C27C20` 更新感染图并同步符号 8，非零时还置符号 7。状态枚举见 [NOTI 0x024B](NOTI-0x024B.md)。`ClientStatic`，读宽及赋值 **Confirmed**，发送与 UI 时序待实机。
