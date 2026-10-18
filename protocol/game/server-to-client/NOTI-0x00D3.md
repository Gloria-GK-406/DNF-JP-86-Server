# NOTI `0x00D3` `START_BLOOD_MAP`

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
| Type | `0x00D3` | 同上 |
| 名称 | `START_BLOOD_MAP` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 8   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 地图格子坐标 X（迷宫 MazeSumInfo.X 钳位截断为 byte） | Confirmed | 上游 BloodAltarPacketBuilder.cs:21 BuildStartMap 写 x；调用点 DungeonMapHandler.cs:569 传 sentMapX（DungeonMapHandler.cs:297 sentMapX = maze.X），BloodAltarDungeonCoordinator.cs:1212 传 room.RoomKey.X，取自 `[ebp - 0x488]` |
| 1 | 1 | `u8` | 地图格子坐标 Y（迷宫 MazeSumInfo.Y 钳位截断为 byte） | Confirmed | 上游 BloodAltarPacketBuilder.cs:22 写 y；调用点 DungeonMapHandler.cs:570 传 sentMapY（DungeonMapHandler.cs:298 sentMapY = maze.Y），BloodAltarDungeonCoordinator.cs:1213 传 room.RoomKey.Y，取自 `[ebp - 0x417]` |
| 2 | 4 | `u32` | 房间随机种子 seed（u32） | Confirmed | 上游 BloodAltarPacketBuilder.cs:23 写 unchecked((int)seed)；调用点 DungeonMapHandler.cs:571 传 run.Seed，BloodAltarDungeonCoordinator.cs:1214 传 room.RoomState.Seed，取自 `[ebp - 0x488]` |
| 6 | 1 | `u8` | 上游恒写常量 0，无任何参数参与 | Confirmed | 上游 BloodAltarPacketBuilder.cs:24 writer.WriteByte(0)，取自 `[ebp - 0x488]` |
| 7 | 1 | `u8` | 首次进入该房间写 1、重访写 0；写 1 时其后还接 mapId(u16) + 3 个 0 字节，body 变成 13 字节，因此该字节同时是尾部存在与否的判别位 | Confirmed | 上游 BloodAltarPacketBuilder.cs:25 写 revisit ? 0 : 1，:26-32 仅在 !revisit 分支追加 mapId 与 3 个 0 字节；revisit 取值见 DungeonMapHandler.cs:301 初值 false 与 :317-319 命中已有 RoomStates 时置 true，取自 `[ebp - 0x41c]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D59ED0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 2026-10-13 祭坛A21复核

校正自动区u16/13B：A21首次地图是15B，x/y各u8、seed u32、0、1、mapId **u32**、三个u8空计数。RVA D59ED0，VA115A1BB读u32。本实现重访也完整地图＋空D2 tail0，重访表现待实机。

ClientStatic / CurrentImplementation，尚无本轮WireCaptured或ClientVerified。本人工结论覆盖旧自动区冲突推断。[查证、顺序和边界](../../../Document/tasks/altar-dungeons/research-and-implementation.md)。

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
