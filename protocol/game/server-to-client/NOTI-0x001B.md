# NOTI `0x001B` `ENTER_SELECT_DUNGEON`

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
| Type | `0x001B` | 同上 |
| 名称 | `ENTER_SELECT_DUNGEON` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 4   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | 上游恒写常量 1，无任何参数参与 | Confirmed | 上游 Network/Builders/Dungeon/EnterSelectDungeonStateBuilder.cs:42 BuildEnterSelectDungeon 第一个 WriteInt32(0x01)；调用点 Network/Handlers/Dungeon/DungeonEntryHandler.cs:129-134，取自 `[ebp - 0x480]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D90190` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- rks:a12:begin -->

> **本区结论得出于 A12 客户端**（SHA-256 `F78F0AEA…`），**A21 上尚未复核**。
> 按迁移规则，本协议在 A21 上重做完成后整区删除；在那之前它是脚手架，不是 A21 的记录。
> 迁移状态见[协议清单](../../catalog.md#a12-内容的退场)。

## 证据与样本

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

进图选择界面自身的状态。转录自参照实现的
`EnterSelectDungeonStateBuilder.BuildEnterSelectDungeon`，单人 19 字节：

```text
i32 1
u16 0
u8  count = 1
  per user: u16 userId, u8 0
i32 0
u16 towerOfDespairFloor    ← 单人时位于 body 偏移 14
3 个零字节
```

参照实现在注释里明确说明"单人时客户端从 body 偏移 14 读这个 u16，每多一名队员后移 3 字节"。
我们只产出单人形状。绝望之塔层数暂固定为 1。

## 服务端实现映射

同 [`DungeonSelectionSlice.cs`](../../../Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs)。

## 待实机补全

- [ ] 开头 `i32 1` 与 `u16 0` 的含义
- [ ] 绝望之塔层数的真实来源（参照实现有 `TowerOfDespairProgress`）
- [ ] 多人形状

## 逆向工作日志

**2026-08-17**：随进入世界流程打通而记录。机制背景见[进入世界](../../patterns/world-entry.md)。

> **本条尚无任何客户端侧证据，不要按"已确认"读。** 布局全部转录自参照实现的 builder；
> 同批的 [`0x0017`](NOTI-0x0017.md) / [`0x0003`](NOTI-0x0003.md) / [`0x001A`](NOTI-0x001A.md)
> 都出现在运行时读取跟踪里（客户端确实读了它们），**本条没有**——这与
> [进入世界](../../patterns/world-entry.md)记载的"`0x000F` 那条路径至今没被实机执行过"
> 一致：那三条另有别的投递路径，而只在 `ENTER_SELECT_DUNGEON` 上出现的本条一次也没发出去。
> 因此"首次发出/确认"这个说法在本条上不成立。

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->

<!-- rks:a12:end -->
