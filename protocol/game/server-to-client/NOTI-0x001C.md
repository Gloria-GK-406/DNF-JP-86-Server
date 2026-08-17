# NOTI `0x001C` `DUNGEON_INFO`

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
| Type | `0x001C` | 同上 |
| 名称 | `DUNGEON_INFO` | upstream NotiPacketType |
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
| 0 | 2 | `u16` | 副本 id dungeonId（上游按有符号 i16 写出） | Confirmed | 上游 Network/Builders/Dungeon/DungeonNotificationBuilder.cs:38 WriteInt16((short)dungeonId)；调用点 Network/Handlers/Dungeon/DungeonEntryHandler.cs:741-742 传 req.DungeonId，取自 `[ebp - 0x224]` |
| 2 | 1 | `u8` | 副本难度 difficulty | Confirmed | 上游 DungeonNotificationBuilder.cs:39 WriteByte(difficulty)；调用点 DungeonEntryHandler.cs:743 传 req.Difficulty，取自 `[ebp - 0x216]` |
| 3 | 1 | `u8` | 本次选中的迷宫序号（builder 形参名 modeFlag，调用点传的是 selection.Index） | Confirmed | 上游 DungeonNotificationBuilder.cs:40 WriteByte(modeFlag)；调用点 DungeonEntryHandler.cs:744 传 mazeModeFlag，其值由 :508 的 (byte)selection.Index 得到，取自 `[ebp - 0x226]` |
| 4 | 1 | `u8` | Boss 房在迷宫格子中的 X 坐标；无 boss 位置时写 0 | Confirmed | 上游 DungeonNotificationBuilder.cs:41 WriteByte(bossX)；调用点 DungeonEntryHandler.cs:745 传 bossPos[0]，bossPos 由 :439 RandomizeBossPosition 产生，取自 `[ebp - 0x21a]` |
| 5 | 1 | `u8` | Boss 房在迷宫格子中的 Y 坐标；无 boss 位置时写 0 | Confirmed | 上游 DungeonNotificationBuilder.cs:42 WriteByte(bossY)；调用点 DungeonEntryHandler.cs:746 传 bossPos[1]，取自 `[ebp - 0x21c]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00900080` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
