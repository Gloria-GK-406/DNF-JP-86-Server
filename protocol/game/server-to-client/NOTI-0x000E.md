# NOTI `0x000E` `UPDATE_ITEM_LIST`

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
| Type | `0x000E` | 同上 |
| 名称 | `UPDATE_ITEM_LIST` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 104   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 物品空间/背包列表类型 itemSpace（InventoryListType：0=主背包 1=时装 2=个人仓库 3=装备 7=宠物 12=账号金库 29=快捷栏） | Confirmed | 上游 Network/Builders/Inventory/ItemListUpdateBuilder.cs:48 WriteByte((byte)itemSpace)（BuildItemSpaceUpdateBody）；空更新路径 :101 同位置同值；枚举见 Game/Inventory/InventoryListType.cs:5-18，取自 `[ebp - 0x268]` |
| 1 | 2 | `u16` | 其后条目区的条目数 count；上游每成功写入一条槽位条目就自增一次 | Confirmed | 上游 ItemListUpdateBuilder.cs:49 WriteUInt16(count)，count 由 :44-45 与 :69-78 每条自增；空更新路径 :102 写 slotIndexes.Count，取自 `[ebp - 0x268]` |
| 3 | 101 | `bytes` | 条目区：count 条槽位更新条目连续排列，单条长度随 offset 0 的列表类型而定（时装为 0x7E 字节等） | High | 上游 ItemListUpdateBuilder.cs:50 WriteBytes(entries)；单条长度表见 :114-123 GetUpdateEntrySize，取自 `[ebp - 0x218]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D7D720` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## A21

**2026-08-24。** 上面的机器区由 **A12** 客户端（SHA-256 `F78F0AEA…`）的静态普查生成。
本节记录 **A21** 客户端（SHA-256 `1F8244D3…`）上不同的部分。判据除另有注明外，都是一份
A21 参照会话，归档于
[a21-world-entry-20260823.log](../../patterns/data/traces/a21-world-entry-20260823.log)
（section L 是入场序列的逐帧顺序）。

A21 在 `0x0016 USER_POSITION` 之后发这一帧（section L 第 60 帧）。

参照 body 508 字节，结构由步长解出：

```text
[0]      标志/子类型（参照为 0）
[1..2]   u16 条目数（参照为 5）
之后     每条 101 字节，条目内偏移 69 处是 0xFF
```

`3 + 5×101 = 508`，闭合。参照的五条 id 是 `0xB1`..`0xB5`，与该角色 `items` 表里
`list_type=0` 的五行一一对应；每条的 `[2:]` 与库里的 `item_core` **前 81 字节相同**，
其后还有 18 字节未解释。

服务端目前**发空表**（`00 00 00`，条目数 0），因为条目内容尚未建模。

### 2026-08-25：入场批次逐帧对账

判据是本仓入场批次与 A21 参照会话的逐帧对账，归档于
[a21-entry-batch-diff-20260825](../../patterns/data/traces/a21-entry-batch-diff-20260825.md)。

**入场窗口里参照只发一帧，而且不是空信封。** 那一帧就是当日补给的实际结果（5 条，
id `0xB1`..`0xB5`），位置紧接 `0x0016 USER_POSITION` 之后、`0x0165` 之前。

本仓此前在批次里发一帧空表（`00 00 00`），补给的真结果在四帧之后另发一帧——于是参照有一帧
的地方本仓有两帧，且两帧都不在参照的位置上。现在补给命令与它的增量都提到闸点之后的尾帧
**之前**执行，空信封撤掉。

上文「服务端目前发空表」那句到此为止：它描述的是那次改动之前的状态。

### 补记：同一天第二次进场没有这一帧

当日补给每角色每天只发一次。撤掉空信封之后，`CommittedUpdates` 为空时这一帧一帧都不发——
所以「与参照多重集一致」只在当天首次进入该角色时成立。

参照那一局是不是首次进场无从判断，因此**无法区分「A21 总是发一帧 `0x000E`」与「因为补给
发生了才发」**。两种读法在现有证据下等价；如果后者为真，现在的行为是对的，如果前者为真，
非补给日就少一帧。取得一份「同一角色当天第二次进场」的参照即可判定。
