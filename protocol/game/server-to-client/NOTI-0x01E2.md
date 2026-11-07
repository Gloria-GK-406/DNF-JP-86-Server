# NOTI `0x01E2` `GROWTH_ITEM_CHANGE_DATA`

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
| Type | `0x01E2` | 同上 |
| 名称 | `GROWTH_ITEM_CHANGE_DATA` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 12   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x1221]` |
| 1 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x1212]` |
| 2 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x1213]` |
| 3 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x1218]` |
| 5 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x1234]` |
| 9 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x1220]` |
| 11 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x1211]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D6B800` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 布局与语义（龙跃武器换星 / 重置）

ClientStatic / Confirmed（布局）、High（语义）。handler VA `0x0116B800`（= RVA `0x00D6B800` + 0x400000）。

```text
+0 u8 changeType   1 = 升星（按 newItemId 经 0x01A85220 重建物品），2 = reset（只改成长值与维持疲劳）
+1 u8 count
count × {
  u8  container
  u16 slot
  u32 newItemId
  u16 experience
  u8  maintained
}
```

GM 显示开启时，type 1 打印 `장비 성장[%s -> %s]`，type 2 打印 `성장 장비 reset[%s]`，除此之外 type 2 没有其他表现。

CurrentImplementation：`GrowthItemChangeNotice`。type 1 用于 1→2、2→3 星满值自动升星（击杀帧内下发）；
type 2 用于跨游戏日的成长值衰减在击杀时结算。入场时的跨日结算改走 0x000E 物品增量（入场批次已发出）。
未取得 WireCaptured。有限 ClientVerified：用户 2026-11-07 实测龙跃武器击杀成长与升星显示正常，见 [实测](../../../Document/tasks/dragon-weapon/user-test.md)。
