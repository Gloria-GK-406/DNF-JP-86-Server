---
kind: rks-verification
decision: pass
---

# 独立验证：珠宝槽在线上只有一个长度

验证方为 fresh context，**未采信**执行方报告（`behavior-change-list.md`）与评审结论
（`reviews/change-review.md`，`verdict: approved`）的任何判定。下面每一条都是本次自己取的新鲜证据。

- 工作区 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/one-length-for-the-jewel-socket`
- 分支 `one-length-for-the-jewel-socket`，`HEAD = d94e184108c8ee07f889b0e2ac98dd510a0668b0`，**未提交**
- 验证期间**未修改任何产品或判据文件**；四次变异均用文件级备份还原并 `sha256sum -c` 核过
- 未使用任何 git 还原类命令；未触碰主检出、`client-debug-lab`、`entries-are-what-the-client-reads`；
  `ServerS4A21` 本次未写入（本次也未读它——18 的上游依据未由本验证独立复核，见「本验证未覆盖的部分」）

## 0. 前置

```
$ tasklist | grep -i "Dfo.Server.Host"
NO_HOST_PROCESS
```

工作区改动面（`git status --porcelain`），共 8 项：
`GameInventoryDelivery.cs`(M)、`StateSynchronizationPayloadCodec.Entry.cs`(M)、
`SelectCharacterPayloadCodecTests.cs`(M)、`NOTI-0x0002.md`(M)、`NOTI-0x000D.md`(M)、
`NOTI-0x0061.md`(M)、`AvatarJewelSocketOnTheWire.cs`(新)、`JewelSocketHasOneWireLengthTests.cs`(新)。
（`.rks-agent/tasks/…` 为流程产物，不计。）
**`SelectCharacterAcceptedTranscriptTests.cs` 不在此列——见 SC-5。**

---

## SC-1 穿戴条目与邮件附件里的时装，珠宝运行都是 `u32(18) + 18 字节`，内容是存储前 18，判据用可分辨填充钉住两者 —— **达成**

### 代码证据

`StateSynchronizationPayloadCodec.Entry.cs:186-188`（P2，穿戴条目）与 `:780-781`（P3，邮件附件）
现在都调用 `AvatarJewelSocketOnTheWire.WriteJewelRun`；该方法
（`AvatarJewelSocketOnTheWire.cs:55-61`）写 `writer.Write(18)` 后写 `stored[..18]`（不足补零）。

### 判据证据

`JewelSocketHasOneWireLengthTests.cs` 的填充是 `0xA0 + index`（`:57`），30 个字节**两两不同**，
不是全零——「发 30」「发后 18」「发 18 个自造零」「发对的前 18」四种错法互相可分辨。

- 长度：`AssertSendsTheFirstEighteen` 先 `Assert.Equal(18, DeclaredJewelLength(frame))`，
  再在帧内定位前 18 字节并断言其**前 4 字节读出 18**（`:176-181`）。
- 内容：定位用的针就是 `StoredSocket()[..18]`，即存储的**前** 18。
- 排除多发：`TheTwelveBytesThatStayBehindReachNoFrame` 断言 `stored[18..30]` 这 12 字节
  作为连续串**不出现在任何一帧**。

覆盖三条路的四个发送方：`ItemListFrame`(P1)、`SupportCharacterFrame`(P2/`0x019F`)、
`UserInfoFrame`(P2/`NOTI 0x0002` 子类型 1)、`MailboxFrame`(P3/`0x0061`)。

### 「改坏会红」——本验证实跑的变异

见下表 M-A、M-C（详见「变异」一节）。M-A（P2 回退 30）红 6 条，含
`TheSupportCharacterFrameSendsEighteen…` 与 `TheUserInfoFrameSendsEighteen…`；
M-C（P3 回退 30）红 4 条，含 `TheMailboxAttachmentSendsEighteen…`。

### 「邮件那条原为零判据覆盖」——本验证独立证实

M-C 红的 4 条**全部**是本轮新增的判据，既有判据**一条都没红**（详见下表）。
这直接证明改动前邮件那条路没有任何既有断言覆盖。

---

## SC-2 线上长度仓内只有一处定义、三条路都读它、截断/补零写法只有一处 —— **达成**（附一条记载缺口，见下）

### 唯一定义与唯一写法

```
$ grep -rn "WireJewelBytes" Server/ --include=*.cs
Server/src/.../AvatarJewelSocketOnTheWire.cs:45:    internal const int WireJewelBytes = 18;   ← 唯一产线定义
Server/src/.../AvatarJewelSocketOnTheWire.cs:49,57,58,59,60                                  ← 定义处自用与注释
Server/src/.../GameInventoryDelivery.cs:177                                                   ← 注释 <see cref>
Server/tests/.../EntryWriterContainerShapeTests.cs:53                                          ← 判据自己的独立期望值
Server/tests/.../JewelSocketHasOneWireLengthTests.cs:46 (ExpectedWireJewelBytes)               ← 同上

$ grep -rn "WriteJewelRun" Server/ --include=*.cs
Server/src/.../AvatarJewelSocketOnTheWire.cs:55   ← 唯一实现（截断/补零）
Server/src/.../GameInventoryDelivery.cs:192       ← P1
Server/src/.../StateSynchronizationPayloadCodec.Entry.cs:188  ← P2
Server/src/.../StateSynchronizationPayloadCodec.Entry.cs:302  ← 注释 <see cref>
Server/src/.../StateSynchronizationPayloadCodec.Entry.cs:781  ← P3
```

产线：**1 处定义、1 处写法、3 处调用**，与三条路一一对应。

判据侧的两处 `18` 是**故意的独立期望值**，不是第二处定义——M-B（把产线常量改成 20）
让它们变红，证明它们确实是独立预言而非「读被检查的值」。

### 「三条路」确认没有第四条

线上外观块 = 珠宝运行 + 颜色运行。产线里向 `BinaryWriter` 写 `Color1` 的位置只有三处：
`GameInventoryDelivery.cs:194`(P1)、`Entry.cs:195`(P2)、`Entry.cs:782`(P3)。
与 `WriteJewelRun` 的三个调用点完全重合。**没有第四条上线路径。**

### 仓内搜这个数：剩下的每个 `30`

口径：`grep -rn "30" --include=*.cs Server/src | grep -i "jewel\|socket"`，**产线，按 grep 行计，共 10 行**：

| # | 行 | 是什么 | 判定 |
|---|---|---|---|
| 1 | `Application/Inventory/InventoryEntryAssembly.cs:152` | 装配层校验 `JewelSocket.Length != 30` | 存储长度，对 |
| 2 | `Application/Inventory/InventoryEntryAssembly.cs:394` | 同上，另一处装配 | 存储长度，对 |
| 3 | `Application/Inventory/ItemContainers.cs:67` | `const int JewelSocketLength = 30`，用于 `:140` 新建时装的零填充 | 存储长度，对 |
| 4 | `Persistence/Schema/CharacterDatabaseSchema.cs:199` | 建表 `CHECK(length(avatar_jewel_socket) = 30)` | 存储长度，对 |
| 5 | `Protocol/Game/GameInventoryDelivery.cs:137` | P1 入口 `JewelSocket.Length != 30` 守卫 | 存储长度，对 |
| 6 | `Protocol/Game/GameInventoryDelivery.cs:138` | 上一行的错误文字 | 存储长度，对 |
| 7 | `Protocol/…/Entry.cs:187` | P2 守卫的错误文字（条件已改用 `StoredJewelBytes`） | 存储长度，对 |
| 8 | `Protocol/…/Entry.cs:300` | `StoredJewelBytes` 的 XML 注释，引述建表 `CHECK` | 存储长度，对 |
| 9 | `Protocol/…/Entry.cs:306` | `private const int StoredJewelBytes = 30` | 存储长度，对 |
| 10 | `Protocol/…/Entry.cs:780` | P3 守卫的错误文字（条件已改用 `StoredJewelBytes`） | 存储长度，对 |

**10 行全部是存储长度，没有一行是线上长度。** SC-2 的正面主张为真。

同一口径搜 `18`（`grep -rn "\b18\b" Server/src/Dfo.Server.Protocol/Game/`）：珠宝相关的只有
`AvatarJewelSocketOnTheWire.cs:27`（注释引上游常量名）与 `:45`（定义本身）；其余命中是日期
（`2026-08-18`）、`ContentsPlayInfoSlice.cs:62`、`GroundPickupSlice.cs:88`、
`AcceptQuestCommand.cs:33`、数组下标等无关物。

### 记载缺口（不阻塞）

`behavior-change-list.md` 第六节写「共 8 处，按代码位置计」，枚举覆盖上表的 1..7、9、10
（9 行），**漏列第 8 项 `Entry.cs:300`**。该行是被枚举项 `Entry.cs:306` 自己的 XML 注释，
且同样是存储长度。因此：

- **枚举不全属实**（读者按其自述命令得 10 行，清单只落到 9 行）；
- 但**不足以让 SC-2 不达成**：SC-2 的实质要求是「线上长度只有一处定义 / 三条路都读它 /
  截断写法只有一处 / 剩下的 30 都是存储长度」，四项本验证逐项独立核过全部成立；
  漏掉的那一行不构成反例，也没有让任何一句成为假话，只是计数与 grep 行数对不上。

---

## SC-3 有判据直接比较三条路发出的珠宝运行长度相同，任一路改回 30 会红 —— **达成**

判据：`JewelSocketHasOneWireLengthTests.EveryFrameDeclaresTheSameJewelLength`（`:96-107`）。
它把四个发送方的 `DeclaredJewelLength(...)` 放进一个数组，
`Assert.All(declared, v => Assert.Equal(declared[0].Length, v.Length))`——**不指名任何数值**。
`DeclaredJewelLength`（`:189-201`）自己在帧里找「一个 u32 后面正好跟着那么多字节的存储前缀」，
不假设 18 也不假设 30。

### 三次单边回退，本验证实跑

| 变异 | 改了什么 | 收敛判据 |
|---|---|---|
| M-A | `Entry.cs:188`（P2 穿戴条目）→ `writer.Write(30); writer.Write(...JewelSocket);` | **红** |
| M-C | `Entry.cs:781`（P3 邮件附件）→ `writer.Write(30); writer.Write(socket); writer.Write(4);` | **红** |
| M-B | `AvatarJewelSocketOnTheWire.cs:45` 常量 18→20（三条一起动） | **绿**（设计如此） |

M-A 的实际报错：

```
Assert.All() Failure: 2 out of 4 items in the collection did not pass.
[1]: Item:  Tuple ("message 0x019F support character", 30)
     Error: Assert.Equal() Failure: Values differ
            Expected: 18
            Actual:   30
```

P1 单边回退未由本验证单独做（M-B 已覆盖「常量变化会被逐帧判据抓住」这一面），
但 P1 与 P2/P3 在收敛判据里是对称的四项之一，M-A/M-C 的红已证明该判据对单边分歧敏感。

M-B 下收敛判据**绿**（其名未出现在失败清单中），这是对的：它只认「四个发送方彼此相等」，
不认 18。这一条与逐帧判据构成互补，缺一不可。

---

## SC-4 非时装占时装槽仍写 `u32(0)` 且不跟字节 —— **达成**

代码：`Entry.cs:190-193` 的 else 分支只写 `writer.Write(0)`，随后直接是颜色运行的 `writer.Write(4)`。

判据：`AnOrdinaryItemInACostumeSlotDeclaresNoJewelsAndGoesStraightToTheColours`（`:145-153`），
两条断言：帧内存在 `[0,0,0,0, 4,0,0,0, 0,0,0,0]` 这一形状；且非时装帧比同槽时装帧
**恰好短「时装珠宝运行的字节数」**。

### 本验证实跑的变异 M-D（钉住性检验）

考虑到本轮曾删掉一条会「假红」的断言，我独立检验了该判据是否仍能证伪：
把 else 分支改成 `writer.Write(0); writer.Write(new byte[18]);`（即「写了 L 个零」这个正要挡的错法）。

结果：该判据**红**，报 `Expected: 180 / Actual: 198`（长度差那一行）。
同时红的还有字节锚与 `SelectCharacterPayload_SupportRelation…`，共 3 条。
**删掉那条假红断言没有让 SC-4 失去证伪能力。**

---

## SC-5 除授权变化外其余帧逐字节不变，字节锚预期不变 —— **达成**

### 字节锚现在确实不在 diff 里

`git status --porcelain` 中**没有**
`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`。
该文件把 62 帧的 SHA-256 逐帧写死在文件内（`:163` `Assert.Equal(62, identities.Length)`），
判据 `AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline` 在当前树上**通过**。
基线未被改写 + 判据通过 ⇒ 62 帧逐帧字节未变。

### 「这不是因为它钉不住」——本验证独立变异 M-B

把共用常量 `WireJewelBytes` 由 18 改为 20（只此一处），字节锚**红**：

```
Dfo.Server.Protocol.Tests.Game.SelectCharacter.SelectCharacterAcceptedTranscriptTests
  .AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline [FAIL]
  Assert.Equal() Failure: Collections differ   ↓ (pos 18)
  Expected: "D4C7DEED02A813ED7592A9E9AFF8A624D6B971BC56B2B7A708…"
  Actual:   "516775566EB973C0F36B6BA2357BD61F2A26F701EFE1D52D41…"
```

**字节锚确实覆盖这段珠宝运行**（经 P1 的物品列表帧，第 18 帧）。它在本轮不变，
是因为参照会话里既没人穿时装、也没有带时装附件的邮件——不是因为它钉不住。

### 授权变化量恰好 12 字节

唯一被改的既有判据 `SelectCharacterPayloadCodecTests.…SupportRelationIsOwnerCorrect…`：
`347 → 335`（差 12）；`:206` 长度 `30→18`；`:210` 内容 30→18 字节；
颜色 `244/246 → 232/234`；生物槽 `276/277/300 → 264/265/288`——全部下移 12。
珠宝运行**之前**的 14 处断言逐值未动。

断言总数核对（防止悄悄放宽或删除）：

```
$ git show :…/SelectCharacterPayloadCodecTests.cs | grep -c "Assert\."   → 93
$ grep -c "Assert\." …/SelectCharacterPayloadCodecTests.cs               → 93
```

**没有断言被增删。**

### 其余帧未变的机制证据

P1（`WriteAppearanceBlock`）的改动是逐字节等价重构：旧代码 `Write(18); Write(len>=18 ? [0..18) : new byte[18])`，
新代码 `WriteJewelRun` 逐字同义。全量判据全绿 + 字节锚未变，共同支持这一点。
产线除这三处外无其他改动（`git diff -- Server/src` 已逐行读过）。

---

## SC-6 全量判据不低于基线 2281/2/0，退出码 0 —— **达成**

```
$ tasklist | grep -i Dfo.Server.Host   → NO_HOST_PROCESS
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

本验证跑了两次（第一次在检查开始时，第二次在全部变异还原并 `sha256sum -c` 通过之后），
两次结果相同：

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 567 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Application.Tests | 1039 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 329 | 0 | 0 |
| **合计** | **2289** | **2** | **0** |

`EXITCODE=0`。两个跳过是 `A_missing_archive_*`（档案确实加载了的标志）。
未出现既知偶发 `ObjectDisposedException: 'SQLitePCL.sqlite3'`。

差额 **+8** 全在 `Dfo.Server.Protocol.Tests`（基线 559 → 567），与新判据文件的
`grep -c "\[Fact\]" JewelSocketHasOneWireLengthTests.cs = 8` 完全吻合——
**没有任何既有判据被删除或改名换掉**。

（基线 **2281/2/0** 本验证未自行重跑 `d94e1841`；它取自任务给定的判据。上面的 +8 分解
使这一点不影响结论：即便基线数有偏差，本次实测 0 失败、退出码 0，且增量可完全归因。）

---

## SC-7 两处协议文档 —— **达成**

### `NOTI-0x0002.md`：记下时装槽外观块的形状与位置

新增 `### 2026-09-07：时装槽的条目在放大值之后多一段外观块`（`:697`）。核过三点：

1. **位置**与代码一致：`Entry.cs:171-196` 里外观块写在 `core.AmplifyValue` 之后、编年史条数之前；
   文档说「在 `22 amplifyValue` 之后、`24 编年史条数` 之前」，与同文件 `:655` 那张 60 字节表对齐。
2. **形状**算术自洽：`24(4) / 28(L) / 28+L(4) / 32+L(2) / 34+L(2)`，L=18 时相对条目起点为
   24/28/46/50/52；而 `SelectCharacterPayloadCodecTests` 里条目起于 182，断言落在
   206/210/232/234——182+24=206、182+50=232、182+52=234，**逐个对上**。
   「时装槽比普通槽长 30、放非时装时长 12」= 4+18+4+2+2 与 4+0+4+2+2，正确。
3. **证据强度已标明**：`:714` 写「依据是**上游源码读出**」并给出上游三处行号；
   `:718-720` 明写「这个长度实机验不了，也没有验过……上一个成果『11 件时装实机全过』
   证明的是外观块的形状机制成立，不是这个长度正确」。

小节归属核过：新小节位于 `### 2026-08-26：……60 字节` 一节的全部内容之后、
`### 2026-08-26：子类型 2` 之前，**没有把既有段落挂到新日期名下**（评审 BF-2 的问题已消失）。

### `NOTI-0x000D.md:261` 的「本轮未改动」有指名补记

`:261` 原句原地保留，其后加了 `<sup>[见「补记（2026-09-07 第三次）」]</sup>`；
文末 `:423` 有 `## 补记（2026-09-07，第三次）：「那条路本轮未改动」这句到此为止`，
说明它当时说的是什么、今天改了什么（三行表：背包家族 18/18、穿戴条目 30→18、邮件附件 30→18）、
以及 `:451` 「依据仍然只有一条，且仍然不是实机」。

（`NOTI-0x0061.md` 的新小节不是 SC-7 要求项，但同样标了「上游源码读出 / 没有实机证据，也不可能有」。）

---

## SC-8 变化清单正向反向都报、从代码出发独立扫、写进搜索集与搜法、显式列出推翻的说法 —— **达成**

`behavior-change-list.md` 具备四要素，本验证逐项核过其内容是否与代码相符：

- **搜索集与搜法**：第一节写明三条 grep 与 6 个范围（含 `.rks-agent/` 本轮四份产物这一类
  上一轮漏掉的范围），并写明未纳入的三个 worktree 及理由。
- **正向 3 条**（F-1 `NOTI 0x0002` 子类型 1、F-2 `0x019F`、F-3 `0x0061`）：与我从代码读出的
  三个 `WriteJewelRun` 调用点及两个 `WriteNoti2EquippedItem` 调用方一致。
  「没有第四条」我独立核过（见 SC-2 的 `Color1` 写入点比对）。
- **反向 1 条**（R-1：`WriteJewelRun` 对短输入补零，但 P2/P3 前置守卫使其不可达）：
  读 `Entry.cs:186` 与 `:780` 的守卫，属实。「新增/移除/改变条件的产线拒绝各 0 条」与 diff 一致——
  三处守卫的条件与错误文字逐字未变，只是字面量 30 换成同值常量。
- **推翻的说法 3 条**：含契约点名的「那条穿戴路径本轮未改动」（C-1）、「珠宝槽有两条上线路径」（C-2）、
  `0x0061`「尚未实现」（C-3，且如实收窄为「一处容易读岔的记载，原句不改」）。

我另行扫了 `protocol/`、`Docs/`、`Document/` 的 `珠宝|jewel`，未找到**本轮**推翻而漏报的说法。

一条**非阻塞观察**（不构成 SC-8 不达成）：`Docs/GM工具_新背包表结构与ItemCore语义.md:539`
写着「0x0D/0x0E 时装 detail 协议仍会写 `len=30 + 30字节jewel_socket`」。这句是**上一轮
（`308ac02c`）**推翻的，不是本轮推翻的，因此不属于 SC-8 要求列举的范围。
不过它暴露清单第一节那条 `grep -rn "珠宝" protocol/ Docs/ Document/` 的口径弱点：
该文件用的是 `jewel_socket` 而非「珠宝」，中文词 grep 命不中它。记在此供下一件参考。

---

## 变异汇总（全部由本验证亲自执行）

方法：`cp` 文件级备份 → `sed -i` 单行改动 → 跑 `Dfo.Server.Protocol.Tests` →
`cp` 还原 → `sha256sum -c` 核散列。**未使用任何 git 还原类命令。**
备份基准散列：
`Entry.cs = bf002d32f63ac75b113a6a748e951aa888b8277cdd3027e386efb1edaa08e529`、
`AvatarJewelSocketOnTheWire.cs = 36c4ef2746f85904d9f6e2f9eb1049c0a2fa65f2c85f9e06b17d0009ea52d7f9`。
四次变异后每次 `sha256sum -c` 均为两个 `OK`。

| 变异 | 改动 | 红条数 | 收敛判据 | 字节锚 | 关键实测 |
|---|---|---:|---|---|---|
| M-A | `Entry.cs:188` P2 回退 30 | 6 / 567 | **红** | 绿 | `EveryFrame…`: `2 of 4 did not pass`，`0x019F` 报 30 |
| M-B | `AvatarJewelSocketOnTheWire.cs:45` 18→20 | 15 / 567 | 绿（正确） | **红** | 字节锚第 18 帧散列不符 |
| M-C | `Entry.cs:781` P3 回退 30 | 4 / 567 | **红** | 绿 | 红的 4 条**全是新判据**，既有判据 0 红 |
| M-D | else 分支写 `0` + 18 个零 | 3 / 567 | 绿 | 红 | `AnOrdinaryItem…` 红：`Expected 180 / Actual 198` |

M-B 的 15 条逐条：`JewelSocketHasOneWireLengthTests` 5 条（四个逐帧 + `TheColoursFollow…`）、
`EntryWriterContainerShapeTests` 7 条、`SelectCharacterPayload_InventoryPreservesCommonAndAvatarEntryShapes`、
`SelectCharacterPayload_SupportRelationIsOwnerCorrect…`、字节锚，共 5+7+3=15。

---

## 特别判断的三件事

### 一、NB-15 的枚举缺口是否足以让 SC-2 不达成 —— **不足以**

我自己扫了一遍（见 SC-2 的 10 行表）。结论两句：

1. **NB-15 属实**：清单的「共 8 处」枚举确实漏了 `Entry.cs:300`；按其自述命令 grep 得 **10 行**，
   清单落到 9 行。
2. **SC-2 的正面主张为真**：这 10 行**无一例外**都是存储长度；线上长度在产线只有
   `AvatarJewelSocketOnTheWire.cs:45` 一处定义，截断写法只有 `:55` 一处，三条路各调用一次。
   漏列的那一行是被枚举项 `Entry.cs:306` 自己的 XML 注释，不构成反例。

因此判 SC-2 **达成**，并把枚举缺口记为不阻塞的记载缺陷。

### 二、字节锚不变是不是因为它钉不住 —— **不是**

M-B（只改共用常量 18→20）让
`SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline`
**红**在第 18 帧。字节锚确实经 P1 覆盖珠宝运行。M-D（只改 else 分支）也让它红。
所以「本轮它不变」是有信息量的结论，不是空断言——变的是穿戴条目与邮件附件，
而参照会话里既没人穿时装，也没有带时装附件的邮件。

### 三、有没有把「不崩」或上一轮「11 件时装实机全过」当成本件长度正确的证据 —— **没有**

逐处核过所有交付物里出现「实机 / 不崩 / 11 件 / eleven avatars」的地方，**每一处都是反向的免责声明**：

- `AvatarJewelSocketOnTheWire.cs:31-36`：「**No live test can confirm this number, and none should
  be claimed.** … What that live run proved was the shape of the block, not the size of this field.」
- `NOTI-0x0002.md:718-720`：「这个长度实机验不了，也没有验过 …「11 件时装实机全过」证明的是
  **外观块的形状机制**成立，不是这个长度正确。」
- `NOTI-0x0061.md:130-133`：「**没有实机证据，也不可能有**」。
- `NOTI-0x000D.md:451-456`：「依据仍然只有一条，且仍然不是实机 … 本轮同样没有、也不可能有实机证据」。
- `behavior-change-list.md:245-247`：「**本清单不把「不崩」当证据。**」

`NOTI-0x000D.md` 里既有的实机段落（`:337`、`:340-343`）是上一轮的，本轮 diff 未触及，
且其中 `:340-341` 本身就写着「『实机不崩』不能用来判定这个长度」。

**本验证不要求实机证据作为通过条件**，理由与契约 Assumptions 一致：块自描述长度，
测过的珠宝槽全为零，实机在原理上分辨不了 18 与 30。

---

## 本验证未覆盖的部分（如实标注，不影响 pass）

- **上游 `ServerS4A21` 的 `A21AvatarJewelBytes = 18` 本次未自行打开复核。** 18 这个数值的
  正确性依据仍然是契约与既有记载所述的「上游源码读出」。本验证核的是「本仓三条路一致、
  与记载所称的 18 相符、且记载没有把证据说强」，不是「18 相对上游为真」这一条。
  这与契约的验收口径一致（SC-1..SC-8 无一条要求重读上游）。
- **基线 2281/2/0 未在 `d94e1841` 重跑**，取自任务给定值；见 SC-6 的说明与 +8 分解。
- 未做实机验证（契约 Scope Out 明令不做）。

---

## 结论

**SC-1 ✅ SC-2 ✅ SC-3 ✅ SC-4 ✅ SC-5 ✅ SC-6 ✅ SC-7 ✅ SC-8 ✅ —— `decision: pass`。**

两条不阻塞的记载缺口已在正文写明，供接受提交时一并知悉：
`behavior-change-list.md` 第六节的「共 8 处」漏列 `Entry.cs:300`；
该清单第一节的中文词 grep 口径命不中 `Docs/` 里用 `jewel_socket` 写的过期句
（那句是上一轮而非本轮推翻的）。两者都不使任何已交付的主张成为假话。
