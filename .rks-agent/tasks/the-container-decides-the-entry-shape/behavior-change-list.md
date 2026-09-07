# 变化清单：`the-container-decides-the-entry-shape`

基线 `f5c00fb8`。**正向**＝改了什么；**反向**＝从代码出发扫「哪些路径的可观察行为因此不同」。
计数口径写在每节开头。

## 正向：生产改动 4 条

| ID | 位置 | 改动 |
|---|---|---|
| F-1 | `GameInventoryDelivery.cs` `Write` 签名 | 新增必填参数 `InventoryContainerType containerType`（**不是可选参数**，所以任何调用点漏传都编译不过） |
| F-2 | 同文件，`Write` 末尾 | 按 `CarriesAppearanceBlock(containerType, core.ItemKind)` 决定是否 `WriteAppearanceBlock` |
| F-3 | 同文件，新增 | `CarriesAppearanceBlock`（三行规则）、`WriteAppearanceBlock`、`WireJewelBytes = 18`、`ColourLength = 4` |
| F-4 | 两个调用方 | `InventoryDeltaMessage.EncodeBody` 传 `this.ContainerType`；`StateSynchronizationPayloadCodec.Inventory` 两处传 `containerType` |

## 反向：可观察行为因此不同的路径

**扫法（本轮口径，含搜索集）**——之所以把口径写出来，是因为本轮记载的错法有一层正是
「搜索集本身没被检查」（见契约 C-1 补充的第三层）：

- **搜索集**：本任务目录下 6 份产物（`TaskContract.md`、`routing-decision.md`、`spec.md`、
  本文件、`sdd-execution.yaml`、`verification/mutation-evidence.md`）＋ `Server/` 下全部 `.cs` ＋
  `protocol/game/server-to-client/` 下本轮改动的两篇。**`routing-decision.md` 是本轮补进来的**
  ——它此前从未进过任何一轮扫描口径，`42` 就是这样在里面活到第五轮的。
- **搜索集之外、已知未处理的一处**：上一件成果 `entries-are-what-the-client-reads` 封存的
  `spec-result.md` 写着「时装条目不再是 143 字节」，本轮已推翻它，但那份产物已随
  `f5c00fb8` 提交，**跨任务边界，本轮不改**。记在这里是为了它有一个前向指针。
- **搜法**：按**说法**搜而不只按数字（`u32(30)`、`30 字节珠宝`、`沿用 A12`、
  `已实机验证的形状`、`内容一直是对的`、`被客户端忽略` 等），再按数字（`42`、`143`、`126`、`0x7E`）兜一遍。

代码侧扫法：`GameInventoryItemEntry.Write` 的全部调用点 ＝ **3 个**
（`GameInventoryDelivery.cs:309`、`Inventory.cs:147`、`Inventory.cs:162`）；
`new InventoryDeltaMessage` 的构造点 ＝ **2 个**（`:335` 从 `update.ContainerType` 取、
`:359` 硬写 `Main`）；仓内是否还有别处假设装扮条目是 143 或追加 42 ＝ **0 处**
（命中的 `143` 全是无关数字）。

### 行为变了的：**2 条**

| ID | 路径 | 变化 | 谁走到 |
|---|---|---|---|
| R-1 | 容器为 `Avatar` 的**任何**条目，经三个调用点中的任意一个 | 条目由 101 变为 **131** 字节：追加 `u32(18) + 18 字节珠宝槽 + u32(4) + 两个 u16 颜色` | `EntryWriterContainerShapeTests` 四条、`SelectCharacterPayload…`、字节锚第 18 帧 |
| R-2 | 容器为 `Equipment` 且 `ItemKind == 8` 的条目 | 同上，由 101 变 131 | `TheEquipmentListCarriesTheBlockOnlyForAnAvatar`。**今天没有生产路径走到**，见下 |

### 明确**没有**变的（扫到并逐一确认）：**4 条**

| ID | 路径 | 为什么没变 |
|---|---|---|
| N-1 | 主背包、个人货柜、宠物、账号金库、公会勋章的条目 | `_ => false`，恒 101。**含其中的时装**——这正是上一件写错的那一半，现由 `AnOrdinaryContainerNeverCarriesTheBlockNotEvenForAnAvatar` 正面钉住 |
| N-2 | `WriteVirtualInventoryItem`（主背包虚拟槽） | 不经 `Write`，仍写 `10 + (CommonLength - 10)` = 101 |
| N-3 | 已穿戴时装（NOTI-2） | `StateSynchronizationPayloadCodec.Entry.cs` 是另一个编码器，本轮未改。**注意它写的珠宝仍是 30 字节**——见下方「一处已知的不一致」 |
| N-4 | `rec+0x00..0x64` 这 101 字节本身 | 一个字节未动，包括 `+0x2F` 的夹紧与 17 字节零填充 |

## 三处必须点名的事

**一、R-2 今天是空转的。** 入场快照只送 Main/Avatar/PersonalCargo/Pet/AccountCargo
（`SelectedCharacterStateMessageFactory.cs:148-152`），**不送装备栏**；
增量帧的容器类型来自 `update.ContainerType`，理论上可以是装备栏，但今天没有已知路径这样发。
**这条规则是按客户端代码实现的，不是按本仓需求实现的**，记下来免得后来的人以为它被用过。

**二、一处已知的不一致，本轮不改：NOTI-2 那条路仍发 30 字节珠宝。**
`StateSynchronizationPayloadCodec.Entry.cs:186-189` 对已穿戴时装写完整的 30 字节珠宝槽，
而本轮把 ITEM_LIST 家族改成了 A21 的 18。上游对**两条路都用 18**
（`ItemListProtocolWriter.WriteNoti2AvatarBlock` 同样写 `A21AvatarJewelBytes`）。
所以本仓现在两条路不一致。**没有在本轮改，因为 NOTI-2 不在本任务范围**，
且改它会动另一处字节锚。**这是本轮留下的最明确的一件后续。**

**三、装备栏那一格用 `itemKind == 8` 代理了客户端的 `0x166EBC0(itemId)`。**
客户端的判定是拿 itemId 去问一个函数（`0x01637740` 的选择子 3 分支），本仓用「核心里的
种类字节等于 8」近似它。两者今天是否等价**未验证**，而 R-2 说明这条路今天没有生产路径走到，
**所以判据也看不住它**。这与「一」是同一类隐患：**用本仓的近似替代客户端的真实判定，
且不可达因而无人看守。** 若日后装备栏真的经这个编码器发出，这是第一个要复核的地方。

**四、`WireJewelBytes = 18` 是读上游定的，不是判据或实机发现的。**
块是自描述的（先读 u32 长度），发 30 也对齐、也不崩；实机那 11 件时装珠宝槽全零，
18 个零与 30 个零在屏幕上无差别。**「实机不崩」在这条上什么都证明不了**——
本轮之前 30 与 18 对判据与实机都是无差别的。定成 18 的依据是上游 A21 的
`A21AvatarJewelBytes = 18`、珠宝槽 `5 孔 × 6 字节` 只发前 3 孔、
以及那个函数名 `WriteAvatarEntry126` 里的 126 正是 A12 的总长。

## 本仓两次教训在本轮的应对

- **「漏报新增路径」**（上上轮漏了 13 条新增拒绝）：本轮 R-2 就是一条**今天没人走**的新路径，
  已在上面点名，并说明它为何空转。
- **「计数/长度类字段只核偏移不核取值范围」**（上轮把 `rec+0x2F` 标成 ✓）：
  本轮的 `WireJewelBytes` 正是长度字段，两端都核了——上游写 18，
  客户端那个目标缓冲 `[ebp-0x58]` 到 /GS cookie 只有 72 字节、仅约 28 字节被清零，
  发 18 稳妥，发 30 会落在相邻局部上而不报错。
