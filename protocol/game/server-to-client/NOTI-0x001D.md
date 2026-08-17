# NOTI `0x001D` `START_MAP`

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
| Type | `0x001D` | 同上 |
| 名称 | `START_MAP` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 14   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 当前房间在迷宫格子中的 X 坐标（maze.X） | Confirmed | 上游 DungeonNotificationBuilder.cs:110 BuildStartMap 首字节 WriteByte((byte)maze.X)；重访构造 :194 同位置同值，取自 `[ebp - 0x44c]` |
| 1 | 1 | `u8` | 当前房间在迷宫格子中的 Y 坐标（maze.Y） | Confirmed | 上游 DungeonNotificationBuilder.cs:111 WriteByte((byte)maze.Y)；重访构造 :195 同位置同值，取自 `[ebp - 0x412]` |
| 2 | 1 | `u8` | 分层房间标记 layeredRoomFlag | Confirmed | 上游 DungeonNotificationBuilder.cs:112 WriteByte(layeredRoomFlag)；重访构造 :196 该位置恒写 0 并注明"分层房间标记"，取自 `[ebp - 0x423]` |
| 3 | 4 | `u32` | 本房间的随机种子 randomSeed（i32） | Confirmed | 上游 DungeonNotificationBuilder.cs:113 WriteInt32(randomSeed)；重访构造 :197 同位置写 run.Seed，取自 `[ebp - 0x44c]` |
| 7 | 1 | `u8` | 深渊派对（Hell Party）模式 hellPartyMode | Confirmed | 上游 DungeonNotificationBuilder.cs:114 WriteByte(hellPartyMode)；重访构造 :198 该位置恒写 0 并注明"深渊模式"；取值来源见 Network/Handlers/Dungeon/DungeonEntryHandler.cs:1243 run.HellPartyMode |
| 8 | 1 | `u8` | unknown | — | 接收指令 |
| 9 | 4 | `u32` | 房间状态值 roomStateValue（u32） | Confirmed | 上游 DungeonNotificationBuilder.cs:116 WriteInt32(unchecked((int)roomStateValue))，形参默认值见 :101 roomStateValue = 1；重访构造 :200 该位置恒写 1 并注明"房间状态值"，取自 `[ebp - 0x460]` |
| 13 | 1 | `u8` | 房间状态标记 roomStateFlag：首次进入写 1，重访写 0 | Confirmed | 上游 DungeonNotificationBuilder.cs:117 WriteByte(roomStateFlag)，形参默认值见 :102 roomStateFlag = 1；重访构造 :201 该位置恒写 0 并注明"重访为 0"，取自 `[ebp - 0x419]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00905110` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
