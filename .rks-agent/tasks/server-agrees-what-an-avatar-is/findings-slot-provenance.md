# 发现：时装槽 0..11 的出处（只读考据，spec 阶段）

考据范围：`Docs/`（含 `ClientEngine`、`a21-*`、`Refactor`）、`protocol/`（1741 个 `.md` 加
traces / samples / probes / batches）、`Server/src` 与 `Server/tests`、`ClientHack/`、
`AGENTS.md`、`README.md`、`.rks-agent/`。

「出处」只认两类：**档案内容**，或**实测转写**（抓包、客户端逆向、上游服务端代码转写）。

## 1. 结论先说：逐格归属，一格都没有出处

**全仓没有任何一处写下时装槽 0..10 各自是什么部位。**

有出处的只有两条：

| 事实 | 出处 | 强度 |
|---|---|---|
| 时装带的宽度是 **12 格（0..11）** | 上游 `EquipmentTypeInfo.IsCostumeBarSlot` 的代码转写，`protocol/game/server-to-client/NOTI-0x0002.md:690-692` | 代码转写。**该记载自陈「不是量出来的」** |
| 槽 **11 = `AuraSkinAvatar`** | 同上 | 同上。且同一段明写「A21 参照里槽位 11 没有物品」 |

其余十一格，**既无档案依据，也无实测转写**。

## 2. 三个对不齐的数，对上了

契约 Current Situation 记了三个互相矛盾的数。考据之后它们是一致的，矛盾来自口径不同：

| 出处 | 数 | 它数的是什么 |
|---|---|---|
| `PvfEquipmentSlots.cs` 注释「Zero through eleven」＋ `FirstWornSlot = 12` | **12** | 客户端**枚举**里时装带的格数 |
| `PvfEntryLifecycleContentSource.cs:32-35`「and the nine others」；`ItemPlacementContent.cs:7-10`「the eleven names」 | **11** | 档案里**有物品**的时装类型数 |
| `Docs/GM工具_新背包表结构与ItemCore语义.md:403`「穿戴时装 0-10」 | **11** | 同上（下标 0..10） |

**对上的关键**：`Docs/ClientEngine/ExtensionSurface.md:163-174` 对 `equipment/equipment.lst`
101,868 件的 `[equipment type]` 普查给出 **11 个** avatar 类型且各有件数
（coat 6973、hat 6887、pants 6807、shoes 6676、hair 6539、breast 6353、waist 5932、
face 5861、skin 2112、aurora avatar 322、weapon avatar 303）；
而 `ClientHack/tools/literals-a21.csv` 的客户端字符串池里以 `avatar` 结尾的名字是 **12 个**，
多出来的那个是 **`aura skin avatar`**——**它在档案里 0 件**。

所以：**带有 12 格，其中第 12 格（槽 11，`aura skin avatar`）今天没有任何模板。**
11 与 12 不是矛盾，是「有物品的类型数」与「枚举格数」两个口径。

**但 GM 文档那条仍然是真冲突，不只是差一格**：它把槽 11 划给「穿戴普通装备」
（`:404` 写 `3 | 11-23 | 穿戴普通装备`），而代码与上游转写把 11 划给时装带。
`Docs/InventoryCoordinateDissonances.md` 没有记这一条。

## 3. 客户端字符串池里那个「看起来能推出映射」的东西，不是出处

`literals-a21.csv` 里函数 `0x0293A180`（读 `[equipment type]` 的那个）引用的字符串块，
按 `literal_va` 升序恰好是 12 个 avatar 名：
`aura skin avatar` → `weapon avatar` → `aurora avatar` → `skin avatar` → `waist avatar` →
`breast avatar` → `shoes avatar` → `pants avatar` → `coat avatar` → `face avatar` →
`hair avatar` → `hat avatar`。

同一块里的非时装名按地址升序是：
`support`(22) → `ring`(21) → `wrist`(20) → `amulet`(19) → `waist`(18) → `shoes`(17) →
`shoulder`(15) → `title name`(13) → `aura skin avatar`(11)。

**地址升序＝槽号降序，这条规律在 8 个已知槽号上全部成立。** 照它外推会得到
hat=0、hair=1、face=2、coat=3、pants=4、shoes=5、breast=6、waist=7、skin=8、
aurora=9、weapon=10、aura skin=11——恰好填满 0..11。

**不采信它，三条理由：**

1. CSV **按 `literal_va` 排序**，那是转储工具的排序，不是枚举序；表里没有任何一列是索引。
2. 该块**不完整**：`hat avatar`(0x0362F7B0) 与 `support weapon`(0x0362F7F0) 之间空 0x40，
   而相邻间距其余都是 0x14–0x2C，**中间至少有一条字符串没进 CSV**。
   已知槽号里 12(weapon)、14(coat)、16(pants)、23(magic stone)、24(earring) 也不在这一串里，
   即这个「降序」是在一个有洞的子集上成立的。
3. `PvfEquipmentSlots.cs:9` 自己写着这些数 **「written out rather than derived from an
   ordering」**——从排布反推顺序正是它排除的做法。

**记下来是因为它值得被实机一次证实或推翻，不是因为它可以当依据用。**

## 4. 实测帧：一帧穿着时装的都没有

`protocol/` 全域：

- 参照会话的六帧 `NOTI 0x000D ITEM_LIST`（`a21-reference-entry-bytes-20260825.md:47-57`）里，
  **时装背包 listType 1 的 count = 0**。
- 穿戴栏（listType 3）根本不走 `ITEM_LIST`，走 `NOTI 0x0002` subtype 1；
  参照会话那里只有槽 **12（武器）、14（上衣）、16（下装）** 三件，另有 8/22 抓包的槽 15
  （`protocol/game/client-to-server/0x0013.md:193-199`，逐字节）。
- `NOTI-0x000D.md` 明写：「**A21 的时装条目未被观察到**，其珠宝槽块与这段零的先后是从 A12
  沿用的，不是量出来的。」
- `NOTI-0x0002.md:691` 明写：「A21 参照里槽位 11 没有物品。」

**带槽位下标的实测只覆盖普通穿戴槽 12、14、15、16。**

## 5. 顺带查实的两条记载缺陷（本刀之外，但要记）

**其一：`aurora-illusion avatar` 是一句无出处的断言，而且很可能指错了东西。**

`PvfEquipmentSlots.cs:8` 与
`StateSynchronizationPayloadCodec.Entry.cs:293` 都写「A21 inserted the **aurora-illusion**
avatar ahead of the weapon / added the aurora-illusion slot at eleven」。

但底层判据（`NOTI-0x0002.md:690`）说的是 **`AuraSkinAvatar`**。
而客户端字符串池里 `aura skin avatar`（`literals-a21.csv:27941`）与
`aurora avatar`（`:27943`）是**两个不同的字符串**，档案里前者 0 件、后者 322 件
（`ExtensionSurface.md:168`）。**仓库里没有任何一处把两者等同起来。**
即「aurora-illusion」这个译名把三个不同的东西混成了一个。

**其二：`FirstWornSlot` / `LastWornSlot` 在生产代码里零读点。**

全仓 5 处命中，分别是：定义两处（`PvfEquipmentSlots.cs:56`、`:59`），
判据三处（`PvfEquipmentSlotsTests.cs:53`、`:54`、`:58`）。**生产读者 0 个。**
生产里另有**四处各自写死的 24**，都不读 `LastWornSlot`：
`SelectCharacterPostEntryServices.cs:97`、`InventoryEntryServices.cs:339`、
`StateSynchronizationPayloadCodec.Entry.cs:305`、
`SelectCharacterPreparationService.cs:448`（字面量 `item.SlotIndex != 24`）。

## 6. 生产代码里唯一的 0..11 判定

`StateSynchronizationPayloadCodec.Entry.cs:296`：

```csharp
private static bool IsCostumeBarSlot(int slotIndex) => slotIndex is >= 0 and <= 11;
```

它被两处用（同文件 `:182-198` 写头像块、`:276-285` 写 9 字节短尾而非 17 字节长尾）。
**它是带宽度判定，不区分带内任何一格。**

另：穿戴容器**按条目遍历不按槽位遍历**（`Entry.cs:110-113`），
唯一硬编码上界是 `SlotIndex is >= 0 and <= 29`，该 29 全仓无注释无出处。
