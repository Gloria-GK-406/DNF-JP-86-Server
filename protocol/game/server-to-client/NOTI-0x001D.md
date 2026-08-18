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

本帧尚无 recv 侧抓包，但**已由服务端实际发出并被客户端接受**（实机 2026-08-18）：
角色因此站进了教程副本的第一个房间。以下是机器区之外的内容，机器区的字段计数
（14）是静态分析在间接调用处停下的位置，不是体长。

## 机器区之外：偏移 14 起还有这些

上游 `Network/Builders/Dungeon/DungeonNotificationBuilder.cs` 的 `BuildStartMap` 在偏移 14
之后继续写：

| Offset | Size | Enc | 含义 | 来源 |
|---:|---:|---|---|---|
| 14 | 2 | `u16` | **本房间的地图 id** | builder `WriteUInt16((ushort)maze.Index)` |
| 16 | 1 | `u8` | 怪物条数 n | `WriteByte((byte)maze.Monsters.Count)` |
| 17 | 18×n | | 每怪：`u16 模板序` `i32 包内序号` `u16 全局序号` `i32 code` `u8 等级` `u8 类型` `u8 flag0` `u8 flag1` `i32 额外状态` | 同上循环体 |
| — | 1 | `u8` | 预生成建筑掉落条数 m | |
| — | 19×m | | 每项 19 字节：对象序号 / 全局序 / 模板 id / 值 / 耐久 / 增幅型 / 增幅值 / 2 字节扩展 / 1 字节扩展 | |
| — | 1 | `u8` | 深渊迷雾标记 | |
| — | 1 | `u8` | 可骑乘对象**分组数**；为 0 时后面直接跟下一字段 | |
| — | ... | | 每组：`u8 本组数量` + 每个 `i32 x` `i32 y` `i32 对象序号` `i32 阵营` `i32 0` | |
| — | 1 | `u8` | 队伍位次，单人为 `0xFF` | |

空房间（无怪、无掉落、无可骑乘）的体长因此是 **21 字节**。

## 偏移 14 是地图 id，不是迷宫序号——用错会崩

上游那个字段在类型里叫 `MazeSumInfo.Index`，名字读起来像"迷宫序号"，**它不是**。两条旁证：

- 血色祭坛分支往同一个位置传的是 `(ushort)sentMapId`（`DungeonMapHandler.cs:568`）
- 深渊那段日志把它和另一个 map id 并列打印：`hellMap={maze.Index} normalMap={hellRoomInfo.NormalMapId}`

迷宫序号另有其位——它是 [`0x001C`](NOTI-0x001C.md) 里的 `modeFlag`，而且是 `u8`。

**实机判别（2026-08-18）**：这里误写迷宫序号 `0` 时，客户端进度条正常走满，
随后**切换时黑屏、等待数秒、进程崩溃**——即"加载一个不存在的地图"的表现。
改成房间地图 id（教程起点房间 `61000`）后角色正常站入。

*教训*：上游的字段名是转录来的线索，不是定义。一个叫 `Index` 的字段可以是 id。

## 服务端实现映射

`Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs`。
当前**怪物条数固定发 0**：本帧全部字段都来自上游转录，先用最小载荷换"客户端认不认这个形状"，
已获肯定答复；怪物待迷宫怪物表建模后填入。

## 待实机补全

- [ ] 房间内有怪时客户端的表现，以及 18 字节怪物记录各字段的实机含义
- [ ] 换房（重访）时 `roomStateFlag` 写 0 的实际差异
