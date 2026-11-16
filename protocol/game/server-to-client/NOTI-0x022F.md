# NOTI `0x022F` `MINIMAP_ICON_INFO`

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
| Type | `0x022F` | 同上 |
| 名称 | `MINIMAP_ICON_INFO` | upstream NotiPacketType |
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
| 0 | 2 | `u16` | 小地图图标条目数 count | Confirmed | 上游 Network/Builders/Dungeon/SpecialDungeonNotificationBuilder.cs:66 WriteUInt16((ushort)count)，取自 `[ebp - 0x14]` |
| 2 | 1 | `u8` | 第 1 个图标的 X 坐标（小地图格子坐标） | High | 上游 Network/Builders/Dungeon/SpecialDungeonNotificationBuilder.cs:69 循环首次迭代 WriteByte(entries[i].X)，取自 `[ebp - 1]` |
| 3 | 1 | `u8` | 第 1 个图标的 Y 坐标（小地图格子坐标） | High | 上游 Network/Builders/Dungeon/SpecialDungeonNotificationBuilder.cs:70 循环首次迭代 WriteByte(entries[i].Y)，取自 `[ebp - 2]` |
| 4 | 4 | `u32` | 第 1 个图标对应的怪物代码 monsterCode | High | 上游 Network/Builders/Dungeon/SpecialDungeonNotificationBuilder.cs:71 循环首次迭代 WriteInt32(entries[i].MonsterCode)，取自 `[ebp - 0x14]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D4C580` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## A21：入场条件目标的小地图图标（#357）

**body：`u16 n` + `n × (u8 x, u8 y, u32 怪物代码)`**（ClientStatic，handler `0x0114C580`，High）。

- **按代码匹配图标**：客户端遍历 dgn 的 `[dungeon minimap icon setting]` 图标表，找 `[with monster]` 项中代码相同的（`+0x58 == code`），把它的格子坐标设为包里的 x/y。坐标由服务端给出，图标与动画由 dgn 决定。
- **`[grid]` 不受影响**：2016 用的 `[grid]` 项坐标写死在 dgn 里，不经过本帧。
- **图标移除方式未知**：包里的坐标是 u8，无法写回初始的 -1，所以靠重发本帧删不掉图标。救出后图标怎样消失还没找到（Hypothesis：小地图本地处理），待实机观察。
- **与 0x001C 无关**：DUNGEON_INFO 的偏移 11 起也有一组格子坐标，存到地下城 `+0xA88`，但它与本帧的图标表在代码上没有关系，本仓保持 0 组。

**本仓实现：** 进本时紧接 DUNGEON_INFO、在 START_MAP 之前发，每个入场条件目标一条（上游同序），由 `DungeonEntranceProjection.IconsFor` 生成。
