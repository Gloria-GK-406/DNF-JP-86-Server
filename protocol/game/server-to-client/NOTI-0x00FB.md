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

NOTI 的 body **没有**结果信封，整个 body 归 handler——这与 CMD 相反，见 [game-receive](../../patterns/game-receive.md#cmd-body-的两字节结果信封每一条-cmd-都有)。

支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

## 证据与样本

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

尚无实机样本。取得后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，并在此说明产生它的客户端动作。

## 服务端实现映射

尚未实现。

## 待实机补全

- [ ] 触发时机（什么玩家动作/服务端状态导致它出现）
- [ ] 响应流程（是否有响应、响应身份与顺序）
- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->
