# NOTI `0x00FB` `ITEM_LOCK_LIST`

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
| Type | `0x00FB` | 同上 |
| 名称 | `ITEM_LOCK_LIST` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 2 + n×8   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 2 | `u16` | 锁定条目数 count，u16；上游按条目数量写出，后续为等量重复组 | Confirmed | 上游 Network/Builders/Inventory/EquipmentItemLockBuilder.cs:59 BuildLockList 首个 WriteUInt16 写 entries.Count，取自 `[ebp - 0x10]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 1 | `u8` | 该条目所在背包列表类型 ListType，按 InventoryListType 解释 | Confirmed | 上游 Network/Builders/Inventory/EquipmentItemLockBuilder.cs:64 WriteByte((byte)entry.ListType)，取自 `[ebp - 2]` |
| &nbsp;&nbsp;↳&nbsp;1 | 2 | `u16` | 该条目的装备槽位 SlotIndex，i16 | Confirmed | 上游 Network/Builders/Inventory/EquipmentItemLockBuilder.cs:65 WriteInt16(entry.SlotIndex)，取自 `[ebp - 0xc]` |
| &nbsp;&nbsp;↳&nbsp;3 | 1 | `u8` | 锁定状态 State，u8；上锁与取消解锁两条路径都写 1，写 2 时表示处于解锁倒计时 | Confirmed | 上游 Network/Builders/Inventory/EquipmentItemLockBuilder.cs:66 WriteByte(entry.State)，:67 以 State==2 决定是否追加倒计时；调用点 Network/Handlers/InventoryHandler.ItemLock.cs:54/139 传 state=1，:150-151 State==2 才带 RemainingSeconds，取自 `[ebp - 1]` |
| &nbsp;&nbsp;↳&nbsp;4 | 4 | `u32` | 解锁剩余秒数 RemainingSeconds，i32；上游仅在 State==2 时写出，故单条目实际为 4 字节或 8 字节变长 | Confirmed | 上游 Network/Builders/Inventory/EquipmentItemLockBuilder.cs:67-68 if (entry.State == 2) WriteInt32(entry.RemainingSeconds)，取自 `[ebp - 0x10]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x0090DA80` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- rks:a12:begin -->

> **本区结论得出于 A12 客户端**（SHA-256 `F78F0AEA…`），**A21 上尚未复核**。
> 按迁移规则，本协议在 A21 上重做完成后整区删除；在那之前它是脚手架，不是 A21 的记录。
> 迁移状态见[协议清单](../../catalog.md#a12-内容的退场)。

## 实测字段表（运行时读取跟踪）

客户端**实际**读取这条协议的方式，由运行时读取跟踪测得（做法见 [game-receive](../../patterns/game-receive.md#四个原语是唯一出口)）。

**本节优先于机器区的 Body 表。** 机器区那张表由静态走图产出，在 S→C 上普遍止步于打包区，给出的是下界；载荷多形时它只描述其中一形，却读起来像描述了整条协议。本节是实测，但**只给边界，不给含义**——`含义` 一列留给人填，偏移已确证之后用实机改值做差分是最便宜的填法。

### 形状 body 2 字节

| Offset | Size | Enc | 读取者 RVA | 实测值 | 含义 |
|---:|---:|---|---|---|---|
| 0 | 2 | `u16` | `0x0090DAA2` | 0 | — |

读取 1 次，消费到偏移 **2**。


## 证据与样本

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

观察记录见上方「实测字段表」，其证据是归档的运行时读取跟踪
[read-trace-20260817-entry-batch](../../patterns/data/traces/read-trace-20260817-entry-batch.log)。
尚无单帧 hex 样本；取得后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`。

## 服务端实现映射

尚未实现。**注意**：本协议已在实机流量里出现（见上方实测字段表），"尚未实现"指的是
服务端侧没有对应 slice，不是"没观察到"。

## 待实机补全

- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->

<!-- rks:a12:end -->
