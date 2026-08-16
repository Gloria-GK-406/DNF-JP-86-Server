# NOTI `0x000D` `ITEM_LIST`

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
| Type | `0x000D` | 同上 |
| 名称 | `ITEM_LIST` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 93   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 物品列表类型 listType / 物品空间 id；上游枚举 InventoryListType：0=主背包 1=时装 2=个人仓库 3=装备 7=宠物 12=账号金库 | Confirmed | 上游 Network/Builders/Init/ItemListPacketBuilder.cs:227、:106、:147 三条构造路径首字节都写 (byte)listType；枚举值见 Game/Inventory/InventoryListType.cs:5-10，取自 `[0x306428c]` |
| 1 | 2 | `u16` | 紧随 listType 的列表级 u16，含义随 listType 分支而异：普通列表写 listParam16（该列表的容量参数），账号金库写 AccountCargo.SelectionKey，宠物列表此处写的却是条目数 count | High | 上游 ItemListPacketBuilder.cs:228 写 listParam16、:148 写 SelectionKey、:107 宠物分支在同一位置写 count，取自 `[ebp - 0xd4]` |
| 3 | 2 | `u16` | 普通列表分支（主背包/时装/个人仓库/装备）在此写 u16 条目数 count，其后即条目区；账号金库分支此处起是 4 字节 Money（金库内金币），与 offset 5 合起来构成该 u32 | High | 上游 ItemListPacketBuilder.cs:220-231 普通分支 u8+u16+u16(count)；:146-152 账号金库分支 u8+u16+u32(Money)+u16，取自 `[ebp - 0x100]` |
| 5 | 2 | `u16` | 账号金库分支下属于 body[3..6] 的 Money(u32) 的后半——该 u32 在客户端被拆成两次 u16 读，故此处出现字段边界；普通列表分支此偏移已进入条目区 | Hypothesis | 上游 ItemListPacketBuilder.cs:149 在 listType+SelectionKey 之后写 4 字节 Money，恰好占 body[3..6]，取自 `[ebp - 0xb8]` |
| 7 | 2 | `u16` | 账号金库分支的条目数 count：上游头部为 u8 listType + u16 SelectionKey + u32 Money，count 正落在 offset 7，其后紧跟条目区 | High | 上游 ItemListPacketBuilder.cs:146-152 依次 WriteByte/WriteUInt16/WriteInt32/WriteUInt16(count)/WriteBytes(entries)，取自 `[ebp - 0xd8]` |
| 9 | 84 | `bytes` | 物品条目数据区起点，普通物品条目固定 84 字节/条（时装条目为 126 字节）；账号金库分支头部恰好 9 字节，故首条条目正从 body[9] 开始 | High | 上游 Network/Builders/Inventory/ItemListProtocolWriter.cs:9 Entry84Size=84、:128-153 WriteEntry84；头部长度见 ItemListPacketBuilder.cs:146-151，取自 `[ebp - 0xb0]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00925300` |
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
