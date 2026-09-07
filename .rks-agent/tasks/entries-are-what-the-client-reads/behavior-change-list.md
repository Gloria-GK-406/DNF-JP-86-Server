# 变化清单：`entries-are-what-the-client-reads`

基线 `445cb5f7`。**正向**＝我改了什么；**反向**＝从代码出发扫「哪些路径的可观察行为因此不同」，
不是核对正向条目。计数口径写在每节开头。

## 正向：生产改动 3 条

全部在 `Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs`。

| ID | 位置 | 改动 |
|---|---|---|
| F-1 | `:121` | `writer.Write(core.EmblemSocketCount)` → `writer.Write(WrittenOptionCount(core.EmblemSocketCount))` |
| F-2 | 原 `:110-119` | 删掉时装条目后面那 42 字节追加块（`4 + 30 + 4 + 2 + 2`）；其中「珠宝槽必须恰好 30 字节」的拒绝**保留**，改写成 `core.ItemKind == 8 && avatar!.JewelSocket.Length != 30` 的独立条件 |
| F-3 | `:133,:159` | 新增 `private const byte WrittenOptionEntries = 2` 与 `private static byte WrittenOptionCount(byte)` |

## 反向：可观察行为因此不同的路径

扫法：`GameInventoryItemEntry.Write` 与 `CommonLength` 的全部引用点（`grep -rn --include=*.cs Server/`，
排除 `bin`/`obj`）＝ **3 个 `Write` 调用点 + 3 个 `CommonLength` 引用点**；
再扫 `JewelSocket` / `Color1` / `Color2` 的全部读点 ＝ **28 处**；
再扫仓内是否还有别处假设时装条目是 143 字节或 `+42` ＝ **0 处**（命中的 `143` 全是无关数字）。

### 行为变了的：**2 条**

| ID | 路径 | 变化 | 谁走到 |
|---|---|---|---|
| R-1 | 任何 `EmblemSocketCount ∈ 3..255` 的物品，经**三个** `Write` 调用点中的任意一个 | `rec+0x2F` 由存储值变为 `2`。**这是静默的取值改变，不是拒绝**：不报错、不拒绝，客户端收到的数字变小 | 改动前：**零覆盖**（口径见下）。改动后：`EntryWriterOptionCountTests` 16 个用例 |
| R-2 | 任何 `ItemKind == 8` 的物品，经同样三个调用点 | 条目 143 → 101 字节；珠宝槽与两个颜色不再上线 | `EntryWriterAvatarShapeTests` 两条新判据、`SelectCharacterPayload_InventoryPreservesCommonAndAvatarEntryShapes`、字节锚第 18 帧 |

**三个 `Write` 调用点**（R-1 与 R-2 都经由它们，不止增量帧一条）：

- `GameInventoryDelivery.cs:218` — NOTI `0x000E` 增量帧
- `StateSynchronizationPayloadCodec.Inventory.cs:147` — NOTI `0x000D` 入场快照，主背包分支
- `StateSynchronizationPayloadCodec.Inventory.cs:162` — NOTI `0x000D` 入场快照，其余容器分支

### 语义未变、但代码形状变了的：**1 条**

| ID | 路径 | 说明 |
|---|---|---|
| R-3 | 「珠宝槽必须恰好 30 字节」的拒绝 | 触发条件仍是 `ItemKind == 8`，仍在写完定长区之后求值，抛出后整条写入作废，所以**没有可观察差异**。变异 M5 证明它仍被判据钉住 |

### 明确**没有**变的（扫到并逐一确认，写下来免得被当成遗漏）：**5 条**

| ID | 路径 | 为什么没变 |
|---|---|---|
| N-1 | 已穿戴时装的珠宝槽与颜色 | `StateSynchronizationPayloadCodec.Entry.cs:186-197` 是 NOTI-2 的动态分段编码器，**另一个编码器**，本轮未改。所以 R-2 的缺口**只限背包类容器** |
| N-2 | 装备路径 `rec+0x2F` 的夹紧 | `Entry.cs:222` 与 `:710` 的 `Math.Min(2, ...)` 原样。本轮是让背包这条与它一致，不是改它 |
| N-3 | `WriteVirtualInventoryItem` | 仍写 `10 + (CommonLength - 10)` = 101 字节。虚拟槽从来不带时装，也从不写 `rec+0x2F` 的非零值 |
| N-4 | `InventoryEntryAssembly` 的装载/保存校验 | `:152`、`:394-396` 对珠宝槽长度与两个颜色的拒绝在 Application 层，本轮未触 |
| N-5 | `item_core[45]` 的存储 | `InventoryItemCore.cs:36` 照旧存原值。夹紧只发生在**上线那一刻**，库里存的还是真实值，日后查清语义可无损改法 |

## 两处必须点名的取舍

**一、R-1 是静默截断。** 存储值 3..255 会被改成 2 送出去，服务器**不报错也不拒绝**。
选静默而不是拒绝，是因为今天没有任何数据带非零值（三个调用点上线以来 `Server/tests`
零覆盖，参照会话也从未发过），一条会拒绝正常入场的规则风险大于收益；而无上界地送出去
则是已知的客户端崩溃向量。**这是本轮唯一一处「让数据变形而不告诉任何人」的地方，
记在这里以便日后查清 `+0x2F` 真实语义时第一个被翻出来。**

**二、R-2 留下一个功能缺口。** 背包里的时装不再带珠宝与颜色。权威表把客户端那个 30 字节
消费方放在 `rec+0x1A..0x37`，长度与珠宝槽吻合，但本仓在 `+0x2B` 写 `period`、`+0x2F` 写
选项计数，两处都有权威表背书；「时装记录让这两处给珠宝数据让位」**没有证据**。
所以本轮不写，并记成缺口。已穿戴的时装不受影响（N-1）。

## R-1 的「零覆盖」口径，以及同一类取证错误的三次发作

**结论（生产侧探针实测）**：**判据套件里**没有任何物品带着非零 `EmblemSocketCount`
到达 `GameInventoryItemEntry.Write`。（生产环境不然——`item_core[45]` 是无约束的字节，
那正是 R-1 夹紧存在的理由。）手法是在该方法开头插一句「拿到非零值就抛」，
跑全量：**红 9 条，全部在本轮新增的 `EntryWriterOptionCountTests` 里，其余每个项目全绿**。
所以 R-1 那条路径在本轮之前确实**零覆盖**。

`Server/tests` 源码里确有 **7 处**出现，但**没有一处到得了这里**：

| 位置 | 值 | 为什么到不了 |
|---|---|---|
| `Dfo.Server.Persistence.Tests/Inventory/InventoryLoadCharacterizationTests.cs` `:150 :208 :266 :324 :438 :496` | 175, 205, 145, 85, 125, 105 | 持久化装载表征判据，只走存储→装载，不进协议层 |
| `Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs:177` | 1 | 是 `SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty` 里的**方法局部** `CoreForTest(...)`，从未进 `SelectCharacterPayloadTestData`；它的字节走 NOTI-2 的已穿戴物品写法 |

**那六个 85..205 是有利证据，而且比初看更强**：它们不是 fixture 里写死的字段，而是
`ExpectedCharacter` / `ExpectedAccount` 两个原始字符串字面量里的**黄金转储断言行**。
真正的种子是 `Core(int from, …)` 的 `core[index] = checked((byte)(from + index))`，
于是 `core[45] = from + 45`（`Core(40, …)` → 85）。也就是说这些值是**从真的 82 字节存储
blob 解出来的**，不是有人手写上去的。所以 R-1 夹的不是假想值：远大于 2 的字节确实能穿过
存储与装载，进到本编码器会拿到的那种 core 里。

### 这一段被改错过两次，都由外部发现

| 次序 | 写过的话 | 错在哪 | 谁发现 |
|---|---|---|---|
| 初版 | 「`Server/tests` 源码里没有任何 fixture 设过 `EmblemSocketCount`」 | **结论对、理由假**。确有 7 处。错法：`grep ... \| head -10`，`bin`/`obj` 的二进制命中占满十行，从**被截断的输出**里读出「不存在」 | 独立评审 |
| 第一次整改 | 「第七处 `= 1` **会**流经本编码器，夹紧对它是空操作」 | **把对的结论也改坏了**。那是一条比原话更强的**正向断言**，而我没有验过它 | 独立评审（生产侧探针） |
| 现在 | 上表 | 由全量探针实测 | — |

同一类错误连着三次：第一次问错了问题，第二次信了截断的答案，第三次为了纠正前两次
**新造了一条没验过的断言**。共同点是**取证没有做到能证伪为止**。

`spec.md` 里 AC-008 那条修正案承认的是同一类错误的第一次发作。

### 「全量判据全绿」在本轮证明不了什么——实测

移除 F-1 的夹紧（变异 M1）后：**字节锚全绿，62 帧一帧不差**，只有本轮新增的
`EntryWriterOptionCountTests` 红了 7 个用例。这是**实测**，不是论证。

字节锚不变的原因就是上面那条：**判据套件里**到达本编码器的每一件物品该字段都是 0，
夹紧对它们全是空操作。上一个成果漏掉 13 条新增拒绝就是这个失败模式。
