---
kind: rks-review
verdict: approved
---

# 完整变更评审：`entries-are-what-the-client-reads`

> **本文含三轮，verdict 以文末第三轮为准：`approved`。**
>
> - **第一轮**（下方「一～四」与「Blocking Findings」）：初评，`changes_required`，提出 B-1、B-2。
> - **第二轮**（「复评（第二轮）」一节）：B-2 已解决；B-1 只解决了一半——假话被换成了
>   **另一句假话**，我用生产侧探针实测证伪（新的 B-3），`changes_required`。
> - **第三轮**（「复评（第三轮）」一节）：**B-3 已解决**，四处说法彼此一致且都在证据范围内，
>   我把生产侧探针跑到了**全解决方案**独立复现。**`approved`**，余两条措辞级非阻塞观察。
>
> 判据：基线 2250 → 第一轮 2266 → 现在 **2268 / 2 跳过 / 0 失败，EXIT=0**。
> **生产可执行代码自第一轮起一个字节未动**。

被评审对象：worktree `E:/Project/DNF/DNF-JP-86-Server/.worktrees/entries-are-what-the-client-reads`
分支 `entries-are-what-the-client-reads`，基线 `445cb5f7`，未提交。
变更集＝`git status --short` 的 5 个已跟踪修改 + 1 个新增判据文件 + `.rks-agent/` 流程产物。
契约 revision 1（SC-1..SC-8）、spec state ready（RQ-001..RQ-008 / AC-001..AC-010 / EV-001..EV-005）。

评审方式：**不采信执行方结论**。下面每一条「已核」都注明我自己用什么办法核的。

---

## 一、我独立核过的事实

### 1. 21 个字段的偏移——**与权威表一致，无重大发现**

我没有读执行方的偏移表，而是把 `GameInventoryItemEntry.Write`
（`Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs:53-125`）按 `writer.Write` 的
调用顺序，配 `InventoryItemCoreState`（`Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs:82-116`）
的字段类型逐个累加宽度，再与 `vmp1-9.md` 第三节并排：

`SlotIndex` i16 → `+0x00`；`ItemId` i32 → `+0x02`；Data i32 → `+0x06`；`Attribute` u8 → `+0x0A`；
`Durability` u16 → `+0x0B`；`SealFlag` u8 → `+0x0D`；`EnchantCardId` i32 → `+0x0E`；
`EnchantUpgradeCount` → `+0x12`；`AmplifyType` → `+0x13`；`AmplifyValue` u16 → `+0x14`；
`Marker16` i32 → `+0x16`；`PopulatedCount` → `+0x1A`；`ChronicleOption0.Id` → `+0x1B`、
`ChronicleOption1.Id` → `+0x1F`；八个属性字节 → `+0x23..0x2A`；`ExpireTime` → `+0x2B`；
选项计数 → `+0x2F`；`EmblemId1` → `+0x30`、`EmblemId2` → `+0x34`；`Rune` u16 → `+0x38`；
随机选项计数 → `+0x3A`，其数组 → `+0x3B..0x49`；`GenuineUpgrade` → `+0x4A`；
`EmancipateEquipmentLevel` → `+0x4B`；`TradeRestriction` → `+0x4C`；
`TailUnknown0..3 / RemainUseCount / SortLockFlag` → `+0x4D..0x53`；
零填充 17 字节 → `+0x54..0x64`。合计 **101**，`WrittenLength = 84 = 0x54` 自洽。

权威表里那句「属性字节@`+0x23/0x25/0x27/0x29`」是**按元素下标 `ebx` 取址**的
（`rec+0x23+ebx` 等），所以元素 0 落 `0x23/0x25/0x27/0x29`、元素 1 落 `0x24/0x26/0x28/0x2A`——
本仓正是 `Job0,Job1,Grow0,Grow1,Equip0,Equip1,Opt0,Opt1` 的交错写法，**逐字节吻合**。
**布局本来就是对的，本轮确实没有重排。**

### 2. `+0x2F` 夹到 2 的依据——**成立；不丢真实数据**

`+0x30` 之后本编码器实际写出的 dword 只有 `EmblemId1`、`EmblemId2` 两个，第三个写入是
`Rune`（u16）。所以「2」这个数**是我自己数出来的，确实是 2**。

会不会丢真实数据：

- 存储侧不变。`Server/src/Dfo.Server.Persistence/Inventory/InventoryItemCore.cs:36`
  `core[45] = value.EmblemSocketCount` 与对应 `Decode` 均未改，库里仍是原值。
- 生产侧**没有任何路径写出 >0 的值**：全仓 `EmblemSocketCount` 在 `Server/src/` 只有 6 处引用
  （模型声明、`InventoryItemCore` 编/解码、`GameInventoryDelivery.cs:92`、
  `GameInventoryDelivery.cs:292` 的字面量 `0`、`StateSynchronizationPayloadCodec.Entry.cs:222`
  与 `:710` 的 `Math.Min(2, ...)`）。取值只可能来自 `item_core[45]`。
- 即便库里有 >2 的值，那些字节本编码器**从来没写过**，所以被夹掉的不是数据而是一句谎话。
  装备路径 `Entry.cs:222/:710` 早已同口径夹 2，本轮是把背包这条对齐过去。

**判断：夹紧正当，`WrittenOptionEntries = 2` 与实际写出条数一致。**

### 3. 字节锚——**实测复核通过：62 帧、只有第 18 帧变、正好 −42**

我没有采信执行方的元素级比对，自己做了两件事：

**(a) 临时探针。** 在 `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/` 下新建
一个一次性用例，用与字节锚同一条路径（`SelectedCharacterStateMessageFactory.Create` →
注册编码器）导出 62 帧的序号/选择子/类型/长度，并导出 fixture 各容器的物品种类。结果：

- `TOTAL=62`；
- `#18 sel=0 type=0x000D len=121`；
- fixture 各容器：`Main items=1 kinds=[1]`、`Avatar items=1 kinds=[8]`、`PersonalCargo 0`、
  `Equipment items=1 kinds=[2]`、`Pet 0`、`cargoItems=0`。
  **整份入场序列里 `ItemKind == 8` 的物品只有一件，在 Avatar 容器里**，所以尾块移除
  在结构上只可能碰到那一帧。
- 同一探针给出各容器帧的成帧开销：`#17` 主背包 `len=1535 = 15 + 5 + 15×101`，
  可解出成帧开销 15。于是 `#18 = 15 + 5 + 101 = 121`；基线时该帧同一算式是
  `15 + 5 + 143 = 163`。**163 → 121，差 42，与被删掉的 `4+30+4+2+2` 逐项对上。**
- 各容器的 `EmblemSocketCount` 全为 `0`，所以夹紧碰不到字节锚——这一点我另有实测（见第 4 条）。

探针用完即删，随后 `sha256sum -c` 核过 6 个文件的散列，全部 `OK`，`git status --short`
回到评审开始时的样子（5 个 `M` + 2 个 `??`）。**未使用任何 git 还原类命令。**

**(b) 哈希数组的静态核对。** `git show 445cb5f7:` 与当前文件里符合
`^\s+"[0-9A-F]{64}",` 的行数**都是 64**，`git diff` 在该数组内**只有一去一来一条哈希**。
配合下面第 6 条的全绿读数，这就证明：**其余 61 条哈希与基线逐字节相同**，
不是「改期望值改出来的绿」。SC-5 / 修正后的 AC-008 成立。

### 4. 变异 M1——**我自己重做了一遍，结论与执行方一致**

文件级备份（`sha256 5713…5011`）后，把 `GameInventoryDelivery.cs:92` 改回
`writer.Write(core.EmblemSocketCount)`，跑 `Dfo.Server.Protocol.Tests`：

```
失败!  - 失败: 7，通过: 537，总计: 544
```

7 条全部是 `EntryWriterOptionCountTests`：
`TheOptionCountNeverExceedsTheOptionsTheWriterActuallyWrites` 的
`stored: 3/10/11/127/128/255` 六个用例，加
`NoStoredEmblemSocketCountCanPutAnOverLargeCountOnTheWire`。
**字节锚与其余 537 条全绿。** 这同时证明三件事：

- AC-003 的变异验证是真的，红在指名的判据上；
- 那条穷举 0..255 的用例**不是恒真**——它是实测会红的；
- 「全量判据全绿」对这条路径确实什么都证明不了。

还原后 `sha256sum -c` 六个文件全部 `OK`。

其余四处变异（M2 破坏 `+0x1A` 上界、M3 加回尾块、M4/M5 移除两条时装拒绝）我没有重做，
但它们的必红性可由判据形状直接判定，不依赖执行方陈述：
M4/M5 对应 `EntryWriterAvatarShapeTests.AnItemThatIsNotAnAvatarMayNotCarryAvatarFacts`
与 `AnAvatarWhoseJewelSocketIsNotThirtyBytesIsRefused`，都是 `Assert.Throws` + 消息全等；
M2 对应 `TheEmblemCountCountsTheTwoChronicleIdsAndCannotExceedThem` 的五组精确值断言；
M3 会同时打红 `AnAvatarWithItsThirtyBytesIsWrittenAsTheSameHundredAndOneByteEntry` 的长度断言
与字节锚第 18 帧。

### 5. 反向扫描——**我自己扫的，与清单的三个计数一致**

计数口径：`grep -rn --include=*.cs`，`Server/` 下排除 `bin`/`obj`。

- `GameInventoryItemEntry` 命中 **5** 处：1 处类型声明、**3 处 `Write` 调用点**
  （`GameInventoryDelivery.cs:218`、`StateSynchronizationPayloadCodec.Inventory.cs:147`、
  `:162`）、1 处 `CommonLength` 引用（`Inventory.cs:179`）。加上声明文件内部 2 处，
  **`CommonLength` 共 3 处引用**。与清单一致。
- 仓内假设时装条目为 143 字节或「+42」的地方：**0 处**。`\b143\b` 在 `Server/**/*.cs`
  只命中 2 条无关数字（PVF 任务计数、随机选项值 143）；`protocol/` 与 `AGENTS.md` 的 `143`
  全是地址/编号。
- 提到 `JewelSocket` 的文档：**2 个文件**——`protocol/game/server-to-client/NOTI-0x000D.md`
  与 `AGENTS.md:501`。`AGENTS.md` 那处是数据库六列清单，与线上字节无关，**不需要修正案**，
  契约里「若与 AGENTS.md 冲突则修正」的条件未触发。
- 没有任何地方 `catch (InvalidDataException)`（`Server/src/` 仅 Pvf 与 Infrastructure 两处，
  与本路径无关），所以「拒绝抛出后整条写入作废」成立，R-3 判「无可观察差异」正确。

### 6. 判据读数——**我自己跑的**

跑之前确认无 `Dfo.Server.Host.exe` 进程。

`PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`：
Protocol **544**、Networking 52、Pvf 60、Persistence 329、Host 6、Infrastructure 236(+2 跳过)、
Application 1039 ＝ **2266 通过 / 2 跳过 / 0 失败，EXIT=0**。
基线 2250 → 2266，**+16 全在 Protocol（528 → 544）**，与新增判据条数吻合
（`EntryWriterOptionCountTests` 9+1+5 = 15，`EntryWriterAvatarShapeTests` 净 +1）。
变异还原之后我又完整跑了一遍，读数相同。未见既知偶发失败。SC-6 / AC-009 成立。

---

## 二、反向行为——我从代码出发独立扫的结论

生产改动只有一个文件，可观察行为的改变**只有 3 条**，我逐条核过，**没有发现清单遗漏**：

| 我扫到的 | 清单对应 | 判定 |
|---|---|---|
| `:92` 计数由存储值变为 `Math.Min(2, ·)`，经 3 个调用点上线 | R-1 | 一致，且「静默截断、不拒绝」这一取舍被点名了 |
| `ItemKind == 8` 的条目 143 → 101，珠宝槽与两色不再上线 | R-2 | 一致 |
| 「珠宝槽必须 30 字节」的拒绝由嵌套 `if` 改为合并条件 | R-3 | 一致；求值位置仍在写完 101 字节之后，抛出后整份 payload 作废，**确无可观察差异** |

我另外逐一确认了清单 N-1..N-5 全部属实：`Entry.cs:186-197` 的 NOTI-2 动态编码器未改
（已穿戴时装仍带珠宝与颜色）；`Entry.cs:222/:710` 的 `Math.Min(2, ·)` 原样；
`WriteVirtualInventoryItem` 仍是 `10 + (CommonLength - 10) = 101`；
`InventoryEntryAssembly.cs:152/:394-396` 的 Application 层校验未触；`item_core[45]` 存储不变。

**没有新增的拒绝路径**——本轮没有任何 `throw` 是新加的，这与上一个成果漏掉 13 条新增拒绝的
失败模式不同类。**新增的是一条夹紧（放行但改值）**，清单把它列成 R-1 并明说是静默截断，
这一点做对了。

---

## 三、既有断言的改写——逐处归因

契约要求「每一处改写都要单独说明它对应哪一条授权变化」。我逐处核了，**四处改写全部可归因**：

| 改写 | 归因 |
|---|---|
| `SelectCharacterPayloadCodecTests.cs`：`5 + 101 + 42` → `5 + 101` | 授权变化一（尾块移除） |
| 同上：删掉读 `avatar[106]`（socket 长度）、`[144]`/`[146]`（两色）的三条断言 | 同上——那三处偏移正落在被删的 42 字节里，字节已不存在 |
| `EntryWriterAvatarShapeTests.cs`：`ObservedAvatarTailLength = 42` 常量删除、长度断言 `3+101+42` → `3+101`、用例改名 | 同上 |
| `SelectCharacterAcceptedTranscriptTests.cs`：第 18 项哈希替换 | 同上（我已实测该帧 163 → 121） |

`+0x2F` 的夹紧**没有导致任何既有断言被改写**（M1 实测：夹紧移除后字节锚仍全绿），
这一点与授权范围相符。

覆盖面没有净减少：被删的三条断言换来了
`EntryWriterAvatarShapeTests.AnAvatarCarriesNoJewelBytesAnywhereInsideTheEntry`
（把时装条目 `rec+0x1A` 之后与同核心值的普通条目整段比对，再断言 `0xA5` 填充的珠宝字节
不出现在条目任何位置），比原来的三个定点读更能挡住「把珠宝挪进定长区」这种改法。
该用例的数组相等断言同时钉住了「时装条目长度 == 普通条目长度」，SC-1 的第二半成立。

---

## 四、契约成功条件

| SC | 判定 | 依据 |
|---|---|---|
| SC-1 | **达成** | 我按写入顺序累加得 101，无 `ItemKind` 分支影响长度；长度性质由两条判据钉住，M3 必红 |
| SC-2 | **达成** | 夹紧值恒 `<= 2`；M1 我亲手重做，红的正是「服务器交出的计数大于实际写出条数」那条，失败消息就是这么写的 |
| SC-3 | **达成** | `+0x1A` 取自 `PopulatedCount(两个 Id)`，值域 `{0,1,2}` 由构造保证，依据写在 RQ-003 与判据注释里 |
| SC-4 | **达成** | 明确不写，理由（会覆盖有权威表背书的 `+0x2B`/`+0x2F`；长度吻合不是证据）与实机反证（4153 天）都写下来了，且三处记载都称它为**缺口**而非完成态 |
| SC-5 | **达成** | 62 帧中恰好第 18 帧变、−42、其余 61 帧哈希与基线逐字符相同——我用探针 + 哈希行数 + 全绿三路核过 |
| SC-6 | **达成** | 2266/2/0，EXIT=0，我自己跑的 |
| SC-7 | **达成** | 三处记载与权威表一致（我逐字段核过）；两句被证伪的旧话**原文保留**在 `NOTI-0x000D.md:154-158`，另起「补记（2026-09-07）」逐句点名推翻，代码与判据注释各有对应的 Correction 段 |
| SC-8 | **未达成** | 清单的**条目本身**正确无遗漏，但它据以定性 R-1 的那条事实是假的——见 B-1 |

---

## Blocking Findings（第一轮提出；处置见文末复评一节）

### B-1 「`Server/tests` 里没有任何 fixture 设过 `EmblemSocketCount`」是**假的**，而且被写进了永久注释

**错在哪。** 一条 grep 就能证伪：

```
Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs:177
        EmblemSocketCount = 1,
Server/tests/Dfo.Server.Persistence.Tests/Inventory/InventoryLoadCharacterizationTests.cs:150/208/266/324/438/496
        = 175 / 205 / 145 / 85 / 125 / 105
```

计数口径：`grep -rn --include=*.cs "EmblemSocketCount" Server/tests/`，命中 **7 处赋值，
分布在 2 个文件**（另有 3 处是新判据里的标识符与消息文本，不是赋值）。

**写在哪。** 同一句假话出现在**三个产物**里，其中一处是会长期留在仓里的源码注释：

1. `Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/EntryWriterOptionCountTests.cs:34-36`
   ——「No fixture in `Server/tests` sets `EmblemSocketCount`, so every recorded frame hash
   carries a zero there」；
2. `spec.md` AC-008「预判」第 2 条——「`Server/tests` 的**源码**里没有任何地方设过
   `EmblemSocketCount`（只有编译产物匹配）」，且修正案还把它追认为「**经变异验证成立**」；
3. `behavior-change-list.md` R-1 的「谁走到」列——「改动前：**零覆盖**（`Server/tests`
   源码里没有任何 fixture 设过该字段）」。

**为什么这是阻塞项，而不是措辞小疵。**

- 它是**同一个取证错误的第二次发作**。AC-008 的修正案刚刚老实交代过第一次：那次的 grep 问的是
  「哪个文件提到时装」，答的却被当成「字节锚含不含时装」。这一次的 grep 同样问错了问题——
  真正该问的是「**哪些 fixture 会流经 `GameInventoryItemEntry.Write`**」，而不是
  「哪些文件出现过这个标识符」。修正案在承认第一次的同一段落里，把第二次原样保留下来，
  还给它盖了「经变异验证成立」的章。**变异验证证明的是「字节锚不会因夹紧而变」，
  不是「没有 fixture 设过这个字段」**——用后者的证据去坐实前者的陈述，正是本轮要防的过头记载。
- 契约的约束里有一条「记载不得写成比证据强」，SC-8 要的就是这份记载本身。
  记载的准确性**是本轮的交付物**，不是附属品。
- 代价极低：把三处改成经得起检验的说法即可，代码一行不用动。

**属实的说法应该是**（三处都可以照抄）：
`Server/tests` 里有 7 处 fixture 设过 `EmblemSocketCount`，但**没有一处会流经
`GameInventoryItemEntry.Write` 并带一个大于 2 的值**：
`SelectCharacterPayloadCodecTests.cs:177` 的 `= 1` 走的是 NOTI-0x0002 的
`StateSynchronizationPayloadCodec.Entry.cs`（且 1 本来就 `<= 2`，夹与不夹同值），
`InventoryLoadCharacterizationTests.cs` 的六处 85..205 是 Persistence 装载金样，
根本到不了 Protocol。**这条路在改动前确实零覆盖，但理由是路径不通，不是字段没人设过**——
而这个更强的结论正是 M1 实测出来的（夹紧移除，字节锚 62 帧一帧不差，只有新判据红 7 条）。

顺带一提，`InventoryLoadCharacterizationTests` 那六个 85..205 反而是对本轮有利的证据：
它们说明一个 `>2` 的字节**确实能原样穿过 `Decode` 到达 `InventoryItemCoreState`**，
夹紧不是纸上防御。清单把这条现成的证据丢掉了。

### B-2 AC-008 的修正案只改了 AC-008，没有改跟它绑定的 EV-003，留下一条自相矛盾且未满足的证据条款

`spec.md` 的 Success Evidence 表里，EV-003（Covers: AC-008）原文仍是：

> `SelectCharacterAcceptedTranscriptTests.cs` 通过，且该文件与 `git show 445cb5f7:` **逐字符相同**
> （证明它不是被改写才通过的）。

AC-008 已被修正案从「完全不变」改成「恰好第 18 帧变化…其余 61 帧不变」，而这条改动**必须**
落在那个文件里（就地改哈希、就地写明哪一帧为什么变）。于是 EV-003 现在**必然不成立**：
该文件与基线相差 12 行。修正案没有一并修订它。

后果不是纸面洁癖：EV-003 是全套证据里**唯一**用来堵「把期望值改到迁就实现」这条路的条款，
现在它作废了，而修正案没有给出替代。替代其实是现成的、而且成立的——我在上面第 3(b) 条用的
就是它：**哈希行数不变（64 == 64）、`git diff` 在该数组内只有一去一来一条哈希、
其余 61 条与基线逐字符相同、且全套判据在这些期望值下全绿**。请把 EV-003 改写成这条，
或明确记为「随 AC-008 修正而作废，由 X 替代」。

**对「AC-008 那条修正案是否诚实」的独立判断**（这是评审委托里点名要答的）：
**放宽本身正当，不是迁就结果。** 三条理由——
(a) 契约 SC-5 从一开始就是「不含时装、计数未越界的帧逐字节相同；**会变的帧要指名**」，
是 spec 的 AC-008 自己收得比契约更紧；修正案是把 AC 拉回契约，不是把契约拉低。
(b) 触发停止点的条件是「**不含时装**的帧变了」，而实际变的那一帧含时装，
所以本就不必停；执行方仍然停下来查清了。
(c) 修正后的验收比原来的等号**更强**：它要求指名帧号、指名字节差、并证明差额等于被删的 42。
唯一的不诚实之处是它顺手追认了 B-1 那条假事实，以及漏改 EV-003。

---

## Non-blocking Observations（第一轮提出；处置见文末复评一节）

- **O-1 EV-002 的「变异验证记录」没有落地成产物。** `sdd-execution.yaml` 声明了
  `artifacts.result: ./spec-result.md`，但该文件不存在；`verification/` 与 `reviews/` 目录都是空的。
  五处变异里只有 M1 的结果被记在 `behavior-change-list.md` 末节、M5 被 R-3 一句带过，
  M2/M3/M4 没有任何留痕。我按判据形状判定这三处必红（`Assert.Throws` + 消息全等、
  五组精确值断言、长度断言 + 字节锚），**所以不阻塞**，但 EV-002 要的「每次变异指名哪一条
  判据失败 + 还原后核 sha256」目前不可复核。建议在 verification 阶段补齐或明确豁免。
- **O-2 AC-001 字面上要求「至少覆盖 `ItemKind` 0、2、8」，`0` 没有覆盖到。**
  现有覆盖是 kind 8（`EntryWriterAvatarShapeTests`、字节锚 #18）、kind 2
  （`EntryWriterOptionCountTests`、字节锚）、kind 1（字节锚主背包）。
  `Write` 除 `== 8` 与 `== 2` 外没有按 kind 分支，长度与 kind 无关是构造性的，
  所以这是字面缺口而非实质缺口，SC-1 不受影响。
- **O-3 生产注释那句「every field this writer emits lands at the offset the A21 client reads it
  from」比契约自己的表更硬。** 契约表对 `+0x1A`、`+0x1B..0x2A`、`+0x2F`、`+0x30..0x49`
  这几行标的是「✓ **位置**」——只核了位置，没核语义（本仓的 `EmblemSocketCount` 落在客户端叫
  「附加选项条目数」的槽，`ChronicleOption*` 落在客户端叫「珠宝/纹章条目」的子数组）。
  注释用的是客户端的名字加不定冠词（"an emblem count@0x1A"、"an additional-option count@0x2F"），
  没有直接断言本仓符号等于客户端字段，所以尚在可接受范围；但把「位置对上」写成
  「每个字段都落在客户端读它的偏移上」，读者容易读成语义也对上了。
  夹紧的正当性不依赖这个语义（「计数不得超过实际写出条数」本身就成立），所以不阻塞。
- **O-4 `NOTI-0x000D.md` 新增那节说「去掉就不崩」，但没提装扮栏那条线仍然必崩。**
  文中已把范围限定在「主背包」，没有过头。但 `Document/reply-to-vmp-agent.md` 第五节的隔离
  矩阵写着：listType 1 只要非空就崩，且「42 字节尾块 / 定长 101 字节」两种记录内容**都崩**，
  崩法固定、与我们发什么无关。那条记载目前只存在于 `Document/` 下，`protocol/` 里没有。
  读者读完新增这节，很可能得出「0x000D 现在安全了」的结论。一行交叉引用即可，
  属 Scope Out 的邻接语境补齐，不是本轮欠账。
- **O-5 `WrittenOptionEntries = 2` 与「实际写出两个 dword」之间无结构约束。**
  同一个 `2` 现在散在四处（本文件常量、`Entry.cs:222`、`Entry.cs:710`、判据常量）。
  日后若在 `+0x30` 之后加写第三个选项 dword，靠人记得改常量。注释把耦合方向说对了
  （绑到本 writer 的输出而不是客户端的数字），暂不构成缺陷。
- **O-6 `Assert.DoesNotContain(JewelSocketFill, avatar)` 依赖 `0xA5` 不与任何字段值相撞。**
  当前 fixture 下成立（已实测绿）。若日后改 `Core(...)` 的取值使某个 dword 含 `0xA5`，
  这条会假红。可接受的脆性，记下备查。

---

## 第一轮结论

代码是对的：布局我自己重算过、夹紧的数我自己数过、字节锚我自己探针量过、M1 我自己重做过，
四项全部支持执行方的结论，**没有发现任何未授权的字节变化，也没有发现被漏报的行为路径**。
SC-1..SC-7 达成。

拦下的是 SC-8 那一关，而且拦点与上一个成果被拦的位置同源：**不是清单漏了条目，是清单
（连同 spec 与一条永久源码注释）把一条一 grep 即倒的假事实当成了定性依据**，
并在承认同类错误的同一段落里给它盖章（B-1）；修正案又只改了 AC 没改与之绑定的 EV（B-2）。
两条都只需改字，不需改代码。改完请交回同一名独立评审重审这两处。

## 第一轮评审留下的痕迹

1. 新建 `Server/tests/…/Game/SelectCharacter/ZzReviewProbeTests.cs`（临时探针）→ 已 `rm`；
2. 改 `Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs:92`（变异 M1）→
   已用文件级备份 `cp` 还原。

两次之后均执行 `sha256sum -c`，6 个文件全部 `OK`。

---

## 复评（第二轮，2026-09-07）

复评基准：生产文件 `sha256 = 83468f8d57bbf94327a137e0e6b9bea09ae910d083c421ad15a7d13cb91778c2`
（与执行方给出的新基准一致，我自己算过）。字节锚与 `SelectCharacterPayloadCodecTests`
两个文件的散列与第一轮**完全相同**，即整改没有回头去动它们。
生产文件的 `git diff` 我逐行看过：**可执行代码与第一轮逐字符相同**，只有 `<remarks>` 变了。

全量判据我自己跑（跑前确认无 `Dfo.Server.Host.exe`）：
Protocol **546**、Networking 52、Pvf 60、Host 6、Persistence 329、Infrastructure 236(+2 跳过)、
Application 1039 ＝ **2268 通过 / 2 跳过 / 0 失败，EXIT=0**。基线 2250，**+18**，与执行方读数一致。

## B-2 —— **已解决**

新 EV-003 我逐项复核，**四项全部成立**（口径：`git diff 445cb5f7 -- <字节锚文件>`）：

| 新 EV-003 的断言 | 我的复核办法 | 结果 |
|---|---|---|
| 哈希数组基线与当前都是 62 项 | `awk` 只在 `AcceptedKnightFrameSha256 = [ … ];` 之间数哈希行，基线用 `git show 445cb5f7:` | **62 / 62** |
| diff 只有一去一来一条哈希行 | `grep -cE '^\+ *"[0-9A-F]{64}",$'` 与 `^\-` 同式 | **1 / 1** |
| 新增的解释性注释不含任何断言 | 取 diff 全部 `+` 行，剔除注释行与那一条哈希行 | **剩 0 行** |
| 其余 61 项逐字符未动 | 由上三条 + 判据全绿共同得出 | 成立 |

**它还堵不堵得住「改期望值迁就实现」？——堵得住，而且是当前可得的最紧条件。**
新条款必然比「逐字符相同」弱，因为授权变化本身就要求改那一行；关键是它把**允许改动的范围
量化到了「恰好一行哈希 + 纯注释」**：任何为迁就实现而改的第二个期望值，都会在同一条 `grep`
计数里显形（变成 2/2）。它也没有把「哪一帧、改成什么」交出去——那由 AC-008 自己
（第 18 帧、−42、其余 61 帧不变）管着，而那三件事我在第一轮已用探针实测过。
所以这不是换成了一条更容易满足的条件，是把一条已不可能成立的条件换成了**等效可核**的条件。

## B-1 —— **只解决了一半**：假话被换成了另一句假话（升级为 B-3）

**解决了的部分**（我复核过，属实）：

- 「7 处赋值」这个数与位置正确；六处 `InventoryLoadCharacterizationTests` 的值
  85/105/125/145/175/205 正确；
- 成因（`grep … | head -10` 被 `bin`/`obj` 的二进制命中占满十行，从截断输出读出「不存在」）
  是可信的，而且写下来了；
- 「那句『经变异验证成立』是错误援引」这一条**判得对**，措辞也没有过头；
- 第一轮指出被丢掉的有利证据（六个 85..205 证明 `>2` 能穿过存储与装载）确实补回来了，
  且这一条**属实**：`InventoryItemCore.Decode` 原样读 `core[45]`，没有任何夹紧。

**没解决的部分见 B-3。**

---

## Blocking Findings（第二轮）

### B-3 「第七处流经了 `GameInventoryItemEntry.Write`」是假的——我用生产侧探针实测证伪

**被证伪的句子**，四处产物**口径一致地**都这么写（一致，但一致地错）：

| # | 位置 | 原话 |
|---|---|---|
| 1 | `EntryWriterOptionCountTests.cs` 类注释 | 「The seventh is in `SelectCharacterPayloadCodecTests`, **which is the fixture the recorded frame hashes are taken from**, and it sets one — at or below the bound, so **the clamp is a no-op there**」 |
| 2 | `behavior-change-list.md` 修正案表 | 列头「会不会流经 `GameInventoryItemEntry.Write`」，`SelectCharacterPayloadCodecTests.cs:177` 一行填 **「会**，且它正是字节锚取值的那个 fixture」 |
| 3 | `spec.md` 再修正案 | 「而后者**正是字节锚取值的那个 fixture**……**第七处流经了**，但值在界内，夹紧对它是空操作」 |
| 4 | `verification/mutation-evidence.md` | 「真实原因是：会流经本编码器的那**一处** fixture（`:177`）设的是 `1`，在界内，夹紧对它是空操作」 |

**实测证伪（决定性探针，非阅读推断）。** 我在生产侧插了一句一次性探针：

```csharp
var avatar = item.AvatarDetail;
if (core.EmblemSocketCount != 0) throw new InvalidDataException("REVIEW-PROBE …");
```

即「**只要有任何判据让本编码器拿到非零 `EmblemSocketCount`，就炸**」。跑 Protocol 全套：

```
失败!  - 失败: 9，通过: 537，总计: 546
```

红的 **9 条全部**是本轮新增的 `EntryWriterOptionCountTests`（8 个非零 `stored` 的 Theory 用例
＋ 那条穷举）。**`SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty`
绿着通过。** 若 `:177` 的 `= 1` 真的流经本编码器，它必然被这个探针打红。
探针用完立即用文件级备份还原，`sha256sum -c` 六文件全 `OK`。

**代码侧的成因**（与实测互相印证，五条独立证据）：

1. 那个 `= 1` 不在共享 fixture 里——它是测试方法内用 `CoreForTest(1, 111, 22) with { … }`
   现建的局部值，塞进方法内现建的 `supportInventory`，**从未回到
   `SelectCharacterPayloadTestData`**；
2. 该用例只调 `EncodeSelectedStateBody(source, 0x019F)`，而该 helper 的 `0x019F` 分支
   只构造 `SupportCharacterStateMessage`，不构造任何 ITEM_LIST；
3. `SupportCharacterStateMessage` → `StateSynchronizationPayloadCodec.EncodeSupportCharacter`
   → `WriteNoti2EquippedItem`，走的是 **NOTI-2 装备编码器**（它自己有
   `Entry.cs:222` 的 `Math.Min(2, …)`），与本编码器是两条路；
4. 全仓 `GameInventoryItemEntry` 只在 `GameInventoryDelivery.cs` 与
   `StateSynchronizationPayloadCodec.Inventory.cs` 两个文件被引用，`Entry.cs` **一次也没有**；
5. 我第一轮的容器探针已经量过：字节锚用的 `CreateComplete()` 里，
   Main / Avatar / PersonalCargo / Equipment / Pet 每一件物品的 `EmblemSocketCount` **都是 0**，
   账号金库 0 件。所以「字节锚取值的那个 fixture」根本不带这个 1。

**为什么仍然阻塞。**

- 这是同一类取证错误的**第三次**，而且这一次是**在专门为纠正它而写的段落里**发生的。
  前两次都是「没查到」，这一次是**新造了一条没查过的正向断言**——「会流经」比「没人设过」
  更强，需要的证据也更多，却一次都没验。
- 它把一个**正确的结论换成了错误的结论**。初版说 R-1 改动前「零覆盖」——**那个结论是对的**
  （我的探针刚刚证明：本轮之前没有任何判据让本编码器见过非零值），只是理由说错了。
  整改保留了理由的错误方向，还把结论一起改坏了：现在四处产物都说
  「有一处流经、只是值在界内」，等于宣称这条路径改动前**有覆盖**。
- 连带把「M1 下字节锚为什么不变」的解释也解释反了。真解释是**字节锚 fixture 通篇是 0**，
  不是「有个 1 被夹成了空操作」。`mutation-evidence.md` 把这句写成了 M1 那一节的落脚点。

**改成这样就对了**（四处可照抄，我已实测过每一句）：

> `Server/tests` 里有 7 处赋值，**没有任何一处会流经 `GameInventoryItemEntry.Write`**。
> 六处在 `InventoryLoadCharacterizationTests`（85/105/125/145/175/205），只走存储→装载；
> 第七处在 `SelectCharacterPayloadCodecTests.cs:177`，是该文件内一个**测试方法局部**构造的
> core，只经 `EncodeSelectedStateBody(…, 0x019F)` 交给 NOTI-2 的
> `WriteNoti2EquippedItem`，不进本编码器；它也**不属于** `SelectCharacterPayloadTestData`，
> 所以字节锚不含它。字节锚 fixture 的每一件物品该字段都是 `0`——**这才是 M1 下
> 62 帧一帧不差的原因**。所以 R-1 在本轮之前确实是**零覆盖**，
> 只是初版把这个正确结论挂在了一条错误的理由上。

需要改的只有措辞，代码一行不用动；`WrittenOptionEntries = 2` 与全部判据都不受影响。

---

## 第二轮非阻塞观察的处置

| 第一轮 | 处置 | 我的复核 |
|---|---|---|
| **O-1** EV-002 无留痕 | 新增 `verification/mutation-evidence.md`，五处变异在新基准上重做 | **已解决**。我独立重做了 **M2**（把 `+0x1A` 的 `PopulatedCount(...)` 改写成常量 `3`）：实测 **8 红**，指名的判据与记录逐条吻合——`TheEmblemCountCountsTheTwoChronicleIdsAndCannotExceedThem` 五个用例、字节锚两条（`AllStaticFrameBytesMatchAcceptedBaseline`、`InventoryDelta_UsesTheSnapshotItemEncodingAndMatchesAcceptedFrameBytes`）、`InventoryDeltaLiftTests.The_entry_paths_delta_frame_is_unchanged_by_the_lift`。还原后散列 `OK`。该文件里唯一需要改的是 B-3 那一段 |
| **O-2** kind 0 未覆盖 | 新增 `AnItemThatIsNotAnAvatarIsWrittenAsTheSameHundredAndOneByteEntry`（Theory，kind 0 与 2） | **已解决，且不是凑数**。我核过 kind 0 是真实生产形状：`GameInventoryDelivery.CarriedGoldCore` 就写 `ItemKind: 0`，经 `InventoryDeltaMessage` 进本编码器，所以注释里「kind 0 是携带金币的形状」属实。判据 544 → 546 |
| **O-3** 注释把「位置对上」写成「字段对上」 | 改成明说只核了偏移与宽度，并点出两处命名分歧、且**不裁定** | **已解决，且没有过头**。新句「this writer calls the byte at 0x1A a chronicle-option count and the byte at 0x2F an emblem-socket count, where the client's own strings call them an emblem count and an additional-option count. Which naming is right is not settled here」与代码逐字相符（`+0x1A` 写 `PopulatedCount(两个编年史 Id)`、`+0x2F` 写 `EmblemSocketCount`），也与契约表「✓ 位置」的口径一致 |
| **O-4** NOTI-0x000D.md 未提装扮栏仍必崩 | 未动 | 维持**非阻塞**。文中已限定「主背包」，没有过头，只是缺一句邻接语境；属 Scope Out 那条线，不升级 |
| **O-5** `WrittenOptionEntries` 与实际写出条数无结构约束 | 未动 | 维持**非阻塞** |
| **O-6** `0xA5` 相撞的脆性 | 未动 | 维持**非阻塞** |

## 第二轮契约判定

SC-1..SC-7 与第一轮相同：**达成**（SC-6 的读数更新为 2268/2/0）。
**SC-8 仍未达成**，但性质变了：清单的**条目**依然无遗漏，`+0x2F`/`+0x1A` 的夹紧、
时装尾块的移除、那条形状变化的拒绝，正向反向都在册；卡住的是清单里**用来给 R-1
定性的那条事实**，它由「假的否定」变成了「假的肯定」（B-3）。

## 复评结论

**verdict: changes_required**，唯一未决项是 **B-3**，四处措辞。

值得说清楚的是：这一轮的整改本身是扎实的——成因写进去了、被丢掉的有利证据补回来了、
EV-003 换成了真能复核的条款、三条非阻塞观察里两条动了手且都动对了、五处变异在新基准上
重做并留了痕，我抽验的 M2 逐条吻合。**代码从第一轮到现在一个字节没动，判据从 2266 涨到 2268。**
B-3 不是新缺陷，是旧缺陷的余数：为了把一句假话改成真话，顺手补了一条没验过的正向断言。
把那四处改成上面那段实测过的说法，我就可以给 `approved`。

## 第二轮评审留下的痕迹（见下，另有第三轮）

对工作区的写操作与还原，全部集中在
`Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs` 一个文件：

1. 变异 M2（`PopulatedCount(...)` → `(byte)3`）→ 文件级备份 `cp` 还原；
2. 生产侧一次性探针（`EmblemSocketCount != 0` 即抛）→ 文件级备份 `cp` 还原。

两次之后均 `sha256sum -c`，六个文件全部 `OK`，生产文件回到
`83468f8d57bbf94327a137e0e6b9bea09ae910d083c421ad15a7d13cb91778c2`；
`git status --short` 与复评开始时一致（5 个 `M` + 2 个 `??`）。
**未使用 `git checkout --` / `git restore` / `git stash`；未提交任何东西；
未新建任何遗留文件；未触碰主检出与 `.worktrees/client-debug-lab`（仅只读）。**

---

## 复评（第三轮，2026-09-07）

复评基准：生产文件 `sha256` 仍是
`83468f8d57bbf94327a137e0e6b9bea09ae910d083c421ad15a7d13cb91778c2`——我自己算的，
与第二轮**逐字节相同**。用第二轮的散列清单核 6 个文件，**只有
`EntryWriterOptionCountTests.cs` 一个 `FAILED`（＝被改了），其余 5 个全 `OK`**。
所以「本轮只改记载与判据注释、生产逻辑与注释均未动」**属实**，
且没有回头去动字节锚或 `SelectCharacterPayloadCodecTests`。

判据（跑前确认无 `Dfo.Server.Host.exe`）：Protocol 546、Networking 52、Pvf 60、Host 6、
Persistence 329、Infrastructure 236(+2 跳过)、Application 1039
＝ **2268 / 2 / 0，EXIT=0**。与第二轮相同。

### B-3 —— **已解决**，我把探针跑到全解决方案独立复现

我没有采信你的读数，自己把同一个生产侧探针（`Write` 开头
`if (core.EmblemSocketCount != 0) throw`）插进去，跑 **`Server/86JP.Server.slnx` 全量**：

```text
Protocol        失败 9 / 通过 537
Networking 52 · Pvf 60 · Host 6 · Persistence 329 · Infrastructure 236(+2) · Application 1039   全绿
```

与你的读数**逐项相同**。要点在于 **Persistence.Tests 329/329 全绿**——那六个
85..205 就住在这个项目里，它们绿着通过，等于当场证明它们到不了本编码器。
探针还原后 `sha256` 回到 `83468f8d…`。

### 你要我复核的三点

#### 1. 四处说法彼此一致，且都在探针实际证明的范围内 —— **是**

我逐处读过 `EntryWriterOptionCountTests.cs` 类注释、`behavior-change-list.md`、
`spec.md` 三修正案、`verification/mutation-evidence.md`，四处的**结论完全一致**：
判据套件里没有任何物品带非零值到达本编码器；七处没有一处到得了；
六处走存储→装载，第七处走 NOTI-2 已穿戴物品写法；R-1 改动前**确实零覆盖**，
初版结论对、理由错；字节锚在 M1 下不变的真因是「到达本编码器的每一件物品该字段都是 0」。

**都没有比证据说得更强**，尤其这三处收得对：

- 没有一处说「生产环境不会有非零值」——恰恰相反，四处都保留了「夹的不是假想值」；
- `EntryWriterOptionCountTests` 把范围明写成 `Nothing else **in the suite**`；
- 「那六个 85..205 是有利证据」被收窄成「进到本编码器**会拿到的那种** core 里」
  （英文是 `exactly the kind of core this writer is handed`），
  而不是第一轮那种「进到本编码器随后拿到的那个 core」。这一处改得准。

#### 2. 「七处没有一处到得了这里」这句全称否定 —— **它在证据范围内，不需要收窄**

理由不是「探针跑了全量」，而是**这七处的作用域本身是封闭的**：

- 六处在 `InventoryLoadCharacterizationTests` 的黄金转储里，对应的种子只在那两个用例内
  写进一个**临时 SQLite 文件**，用例结束即删；它们在别处根本不存在；
- 第七处是一个**测试方法局部**的 core，出不了那个方法。

也就是说，这七处**全部**被判据套件执行到了，而探针在全量下没有被它们触发一次。
所以「七处没有一处到得了这里」是一句**被穷尽覆盖**的全称否定，不是外推。

**真正需要收窄的是另外两句**，它们省了范围限定词（非阻塞，见 O-8）：
`verification/mutation-evidence.md` 与 `spec.md` 末尾各有一句
「**到达本编码器的每一件物品**，该字段都是 0」。上下文（紧接着「跑全量」「为什么字节锚不变」）
把范围框住了，但单独摘出来会读成一句关于所有物品的话——而生产环境恰恰可能有非零值，
这正是夹紧存在的理由。补四个字「判据套件里」即可。

#### 3. 三节自我批评 —— **没有写过头，但有一处可省的重复**

我按「后来的读者要拿它做决定」这个标准衡量，结论是**留着**：

- 三次的记法是**错法形态**（问错问题 / 信截断输出 / 新造未验断言），不是自责，
  一个后来的人可以照着这三条检查自己的取证；
- 每一条都注明**谁发现的**（判据 / 独立评审 / 独立评审的生产侧探针），
  这让读者能判断「哪一类错误靠什么关卡能挡住」——这是有操作价值的信息；
- 篇幅是一张三行表加两句，没有反复致歉，也没有把结论淹掉；
- 那句「纠错本身也是要取证的，而且纠错时更容易松懈，因为人会觉得『我正在认错，
  所以我是严谨的』」是三节里唯一谈人而不谈记录的一句。它是**可推广的判断**而不是姿态，
  且只出现一次，我认为该留。

唯一的成本：同一份三次叙事现在**在三个产物里各有一份**（`spec.md` 的三修正案、
`behavior-change-list.md` 的「被改错过两次」节，`mutation-evidence.md` 里还有一段简述）。
后来的读者读到第三遍时，得到的信息量为零，还得判断三份是不是同一件事。
若要收，我会把完整叙事只留在 `behavior-change-list.md`（它是 SC-8 的交付物、
也是最可能被翻出来的那份），另两处各留一行指路。**这是建议，不是要求**——
重复的记载不是缺陷，我不为此扣住 approve。

### 第三轮契约判定

**SC-1..SC-8 全部达成。**

SC-8 是三轮里唯一反复的一条，现在成立：清单的**条目**从第一轮起就无遗漏
（正向 3 条、反向「行为变了 2 条 / 语义未变 1 条 / 明确没变 5 条」，我从代码独立扫过），
而它给 R-1 定性所依据的那条事实，现在是**实测的**而不是**推断的**——
且是用一条「能证伪它」的探针测出来的，正是这份契约从头到尾在要求的东西。

### 遗留的非阻塞观察

- **O-7 「7 处赋值」这个词不准，六处不是赋值。**
  `InventoryLoadCharacterizationTests` 那六行（`:150 :208 :266 :324 :438 :496`）在
  `ExpectedCharacter` / `ExpectedAccount` 两个**原始字符串字面量**里，是**黄金转储的断言行**；
  真正的种子是 `Core(int from, …)` 里的 `core[index] = (byte)(from + index)`，
  于是 `core[45] = from + 45`，落成 175 / 205 / 145 / 85 / 125 / 105。
  **这个用词是我第一轮的发现里带进去的，责任在我，不在整改。**
  实质结论不受影响，反而更强：那六个值不是 fixture 里写死的字段，
  而是**真的从 82 字节存储 blob 里解出来的**，所以「>2 能穿过存储与装载」这条有利证据
  比原来说得还硬。`EntryWriterOptionCountTests` 的英文其实已经描述对了机制
  （`seeds … through storage and asserts they load back unchanged`），只是名词仍叫 set。
  建议把三处的「赋值」改成「命中」或「出现」。
- **O-8 两句全称句缺范围限定词**：`verification/mutation-evidence.md` 与 `spec.md` 末尾的
  「到达本编码器的每一件物品，该字段都是 0」，建议补成「**判据套件里**到达本编码器的
  每一件物品」。理由见上面第 2 点。
- 第一轮的 **O-4**（`NOTI-0x000D.md` 未交叉引用装扮栏仍必崩）、**O-5**（`WrittenOptionEntries`
  与实际写出条数无结构约束）、**O-6**（`0xA5` 相撞的脆性）维持非阻塞，未变。

### 第三轮结论

**approved。** 三轮下来，代码从第一轮到现在**可执行部分一个字节未动**，
判据 2250 → 2268 全绿；布局、夹紧的上界、字节锚的 −42、五处变异里的 M1 与 M2、
以及这条「零覆盖」的全称否定，我都自己量过一遍。

最后一条阻塞项的解决方式值得记下来：它不是靠改措辞过关的，
是靠**把一条断言改写成能被一次实验证伪的形状**（「插一句非零就抛，看谁红」）
再去跑那次实验过关的。这正是本轮契约 SC-8 想要的东西，
也是前两次整改都没做而这一次做了的那一步。

O-7 与 O-8 是措辞级别，不影响判定；随手改掉更好，不改也不构成缺陷。

### 第三轮评审留下的痕迹

对工作区的写操作只有一次，在
`Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs`：插入一次性生产侧探针
（`EmblemSocketCount != 0` 即抛），跑完全解决方案后用文件级备份 `cp` 还原，
`sha256` 回到 `83468f8d57bbf94327a137e0e6b9bea09ae910d083c421ad15a7d13cb91778c2`，
随后又跑了一遍干净的全量判据（2268 / 2 / 0，EXIT=0）。
`git status --short` 与复评开始时一致（5 个 `M` + 2 个 `??`）。
**未使用 `git checkout --` / `git restore` / `git stash`；未提交任何东西；
未新建任何遗留文件；未触碰主检出与 `.worktrees/client-debug-lab`（仅只读）。**

---

## 批准范围确认（含 O-7 / O-8 整改，2026-09-07）

**上面的 `approved` 覆盖当前这一版变更集**，包括 O-7（用词改准）、O-8（补范围限定词）
与三次错法叙事的去重。核对办法与结果：

- **代码一个字节没动。** `GameInventoryDelivery.cs` 的 `sha256` 仍是
  `83468f8d…1778c2`；`EntryWriterOptionCountTests.cs` 仍是 `5fa870da…9561d7`，
  即**与我跑出 2268 / 2 / 0 的那一版逐字节相同**；字节锚、`SelectCharacterPayloadCodecTests`、
  `EntryWriterAvatarShapeTests`、`NOTI-0x000D.md` 用第二轮的散列清单核，全部 `OK`。
  本次改动只落在 `.rks-agent/` 的三个产物里，**所以不需要重跑判据**——
  没有任何被判据覆盖的字节发生变化。
- **O-7 属实且改对了。** `Core(40, …)` → `core[45] = 40 + 45 = 85`，与 `:608` 的
  `Core(40, 8, 2500, 4242)` 对得上；「出现」比「赋值」准。
  `behavior-change-list.md` 把「证据更强」的理由也写进去了，措辞没有过头。
- **O-8 的第三处该补，补对了。** 我原先只点了两处，`behavior-change-list.md:67` 的结论句
  确实漏着同一个限定词，自行扫出来补上是对的。三处现在都是「判据套件里……」。
- **「生产环境不然——`item_core[45]` 是无约束的字节」这句成立。** 我核过两条：
  建表语句里 `items` 对 `item_core` 只有 `CHECK (length(item_core) = 82)`，
  **对第 45 个字节没有任何约束**；`EmblemSocketCount` 在 `Server/src/` 的 6 处引用
  没有一处是校验（模型声明、`InventoryItemCore` 编/解码、本编码器的夹紧与一个字面量 `0`、
  `Entry.cs` 的两处 `Math.Min(2, …)`）。这句不但准确，还把「为什么要夹」说清楚了。
- **去重之后只读 `spec.md` 的人不缺东西。** 我按「只看这一份能不能判断 AC-008」读过：
  错的两条事实、正确事实、探针实测读数、「经变异验证成立」是错误援引、三种错法各是什么、
  以及 AC-008 的放宽为什么不受影响——**都在 spec 里**。被移走的只有逐次的
  「谁发现的」与那句可推广的教训，那是背景不是依据。判断正确。
- **没有引入新的不准。** 三类改动我逐处读过，没有出现新的正向断言，
  也没有把范围重新放宽。

第三轮遗留的 O-7 / O-8 至此**均已关闭**；仍开着的只有第一轮的 O-4 / O-5 / O-6 三条非阻塞观察。
