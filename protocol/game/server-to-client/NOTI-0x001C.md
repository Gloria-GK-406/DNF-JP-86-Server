# NOTI `0x001C` `DUNGEON_INFO`

## 2026-10-07 主动深渊当前投影

**CurrentImplementation / UpstreamImplementation**：offset8/9 写入共享run冻结的深渊cell X/Y，普通维持0/0；offset10维持普通0，主动+练习组合在准入时拒绝。原生RVA00D523AE/BC将两字节写入对象+AF4/+AFC，坐标业务语义仍按上游参照，不升级为ClientVerified。普通32B样本不是所有条件分支的固定包长证明。见[实现边界](../../../Document/tasks/manual-hell-entry/implementation.md)。

## 2026-10-07 深渊动态结构的边界

本轮实际阅读RVA`00D51E10`：前缀为u32与四u8，之后两u8、一u8、u8 groupCount；每group再读u8 pairCount与多组u8/u8。其后仍有多类型字段和条件分支。**ClientStatic / Confirmed**说明该消息可变长，不能把普通模式的32B编码样本推广为所有深渊场景固定包长。

坐标/标志的具体深渊含义未在本次沿写入/消费链完整命名，旧上游字段名不能据此升级。绑定b1 SHA256`CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`；[指令定位](../../../Document/abyss-research-20261007/client.md#地图通知真实前缀)。无新深渊实帧。

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
已读出字段合计 = 8   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | 副本 id dungeonId（上游按有符号 i16 写出） | Confirmed | 上游 Network/Builders/Dungeon/DungeonNotificationBuilder.cs:38 WriteInt16((short)dungeonId)；调用点 Network/Handlers/Dungeon/DungeonEntryHandler.cs:741-742 传 req.DungeonId，取自 `[ebp - 0x240]` |
| 4 | 1 | `u8` | Boss 房在迷宫格子中的 X 坐标；无 boss 位置时写 0 | Confirmed | 上游 DungeonNotificationBuilder.cs:41 WriteByte(bossX)；调用点 DungeonEntryHandler.cs:745 传 bossPos[0]，bossPos 由 :439 RandomizeBossPosition 产生，取自 `[ebp - 0x239]` |
| 5 | 1 | `u8` | Boss 房在迷宫格子中的 Y 坐标；无 boss 位置时写 0 | Confirmed | 上游 DungeonNotificationBuilder.cs:42 WriteByte(bossY)；调用点 DungeonEntryHandler.cs:746 传 bossPos[1]，取自 `[ebp - 0x248]` |
| 6 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x244]` |
| 7 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x242]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D51E10` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- rks:a12:begin -->

> **本区结论得出于 A12 客户端**（SHA-256 `F78F0AEA…`），**A21 上尚未复核**。
> 按迁移规则，本协议在 A21 上重做完成后整区删除；在那之前它是脚手架，不是 A21 的记录。
> 迁移状态见[协议清单](../../catalog.md#a12-内容的退场)。

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本帧尚无 recv 侧抓包，但**已由服务端实际发出并被客户端接受**（实机 2026-08-18），
与 [`0x001D`](NOTI-0x001D.md)、[`0x019F`](NOTI-0x019F.md) 三帧一同作为
[`0x0010`](../client-to-server/0x0010.md) 的应答。

## 机器区之外：偏移 6 起还有这些

机器区的"已读出字段合计 = 6"是静态分析停下的位置，不是体长。上游
`DungeonNotificationBuilder.BuildDungeonInfo` 在 bossY 之后继续写：

| Offset | Size | Enc | 含义 |
|---:|---:|---|---|
| 6 | 1 | `u8` | 深渊房格 X；**非深渊写 `0xFF`** |
| 7 | 1 | `u8` | 深渊房格 Y；同上 |
| 8 | 1 | `u8` | dungeonMode（调用点恒传 0） |
| 9 | 1 | `u8` | 附加坐标对**分组数** g |
| 10 | 变长 | | 每组：`u8 本组对数` + 每对 `u8` `u8`（小地图图标，普通副本通常为空） |
| — | 2 | `u16` | 深渊是否开启 |
| — | 2 | `u16` | value1 |
| — | 1 | `u8` | value2（深渊时 `0x0B`） |
| — | 1 | `u8` | flagA（有图标分组时 1） |
| — | 4 | `i32` | packetSeed，builder 默认 `0xFFFFFFFF` |
| — | 3 | `u8`×3 | paramA / paramB / paramC |
| — | 3 | `u8`×3 | tailFlag0 / 1 / 2 |
| — | 4 | `i32` | tailReserved |

无图标分组时体长为 **30 字节**。

`0xFF` 而不是 0 是有意的：`(0,0)` 是普通迷宫可以占用的格子，用它表示"没有深渊房"会与真实坐标混淆。

偏移 3 的 `modeFlag` 是**迷宫序号**（`(byte)selection.Index`），与 [`0x001D`](NOTI-0x001D.md)
偏移 14 的**房间地图 id** 是两回事，别互换——后者写错会崩。

## 服务端实现映射

`Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs`。
无实机对照的字段一律按 builder 的**默认值**写出，而不是写 0：
上游选定的默认值是证据，我们自选的 0 不是。

<!-- rks:a12:end -->

## A21

**2026-08-26。** 上面的机器区由 **A12** 客户端（SHA-256 `F78F0AEA…`）的静态普查生成。
本节记录 **A21** 客户端（SHA-256 `1F8244D3…`）上不同的部分。判据是 A21 参照会话的字节
（[原始字节](../../patterns/data/traces/a21-reference-entry-bytes-20260825.md)）与本仓的线级重放
（[副本段对照](../../patterns/data/traces/a21-dungeon-wire-20260826.md)），以及产出那份参照的
服务端自己的构包器。

**32 字节，不是 30，差在副本号的宽度。**

三处与本仓此前不同，都由 A21 自己的构包器对着参照的帧读出来：

1. **副本号是 `i32`，不是 `i16`。** 这就是那两个字节，也正是它把结尾的 `FF FF FF FF` 哨兵
   从偏移 16 推到参照所在的 18。
2. **没有「无深渊房」的 `FF FF`。** 那两个字节在 A21 是地狱队标记与地狱队启用标志，
   两者皆无时都写 0。旧客户端的 `0xFF/0xFF` 哨兵不是 A21 的写法。
3. **小地图组数之后有一个固定的 `1`。** A21 的构包器把它写在动态组列表之外，
   而且它自己的注释拒绝把这个字节读成某个开关，所以这里照抄不解释。

### 2026-10-13 更正：场景楼层不是固定标记

上述第 3 点的“固定 1”是普通副本样本的历史解释。**ClientStatic / Confirmed**：A21 handler 在 VA `01152BF6` 读取小地图组之后的第一个 `u16`，在 `01152C19` 存入场景资料 `+0x43c`；无图标组时是 body offset 12..13。随后另一个 `u16` 进入 `+0x444`，再读两个 `u8`，既有 u32 哨兵仍位于 offset 18。不能将 `01 00` 拆成固定字节与保留字节。

绝望塔楼层牌物件 10386 的初始化 VA `01BC2F00` 在模式 11 分支读取 `+0x43c`，按个位／十位组合 PVF 的数字动画。因此 **CurrentImplementation** 改为发送 `layout.Despair.Floor`，普通副本保留原来的 u16 1。32 字节体长、地图标识、深渊坐标和其他尾部均保持；不修改 START_MAP 的房间状态值，也不照搬 70 版报文尾部。

证据与哈希见[绝望塔场景楼层牌](../../../Document/outputs/despair-floor-sign-20261013/evidence.md)。本轮仅 ClientStatic／CurrentImplementation，尚未部署或取得新的 WireCaptured／ClientVerified。
