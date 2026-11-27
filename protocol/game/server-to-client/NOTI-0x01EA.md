# NOTI `0x01EA` `CHARACTER_BUFF_DUNGEON`

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
| Type | `0x01EA` | 同上 |
| 名称 | `CHARACTER_BUFF_DUNGEON` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 2 + n×4   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 2 | `u16` | 本包携带的"生效中"buff 数量 count = buffIds.Count；上游每次都发完整生效列表，清空时发 count=0 的空列表 | Confirmed | 上游 SpecialDungeonNotificationBuilder.cs:24-25 BuildCharacterBuffDungeon 先算 count 再 WriteUInt16((ushort)count)；调用点 DungeonBuffNotificationSender.cs:39-40 传 activeBuffIds、:58-59 传 Array.Empty<int>()，取自 `[ebp - 4]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 4 | `u32` | 生效中的 buff id | Confirmed | 上游 SpecialDungeonNotificationBuilder.cs:26-27 循环 WriteInt32(buffIds[i])；调用点 DungeonBuffNotificationSender.cs:39-40 传 activeBuffIds，取自 `[ebp - 8]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00908190` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 2026-11-28 公会地下城组队 Buff

CurrentImplementation：组队角色首个房间加载就绪时发送 `0x01E8`，然后发送本通知激活 PVF 指定的 205、206 或 207。正文为 `u16 count=1` 加 `i32 buffId`。对应会话离场时先发 `0x01E9`，随后以正文 `00 00` 清空副本 Buff 列表。数据和验收范围见[公会地下城组队 Buff](../../../Document/tasks/guild-system/a21-guild-dungeon-party-buff.md)。
