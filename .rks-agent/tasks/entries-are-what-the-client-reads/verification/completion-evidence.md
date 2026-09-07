---
kind: rks-verification
decision: pass
---

# 新鲜验证：`entries-are-what-the-client-reads`

**被验证的声明**：本变更集满足 TaskContract（revision 1）的 SC-1..SC-8，可以进入接受提交。

**验证者立场**：独立验证方，fresh context。执行方的 `verification/mutation-evidence.md`、
`behavior-change-list.md` 与评审方的 `reviews/change-review.md` 只作为**待核对的说法**读入；
下面每一条判定都注明我自己跑了什么、看到了什么。

**被验证的工作区状态**（验证结束时实测，与验证开始时一致）：

- worktree `E:/Project/DNF/DNF-JP-86-Server/.worktrees/entries-are-what-the-client-reads`，
  分支 `entries-are-what-the-client-reads`，`git log --oneline -1` = `445cb5f7`，**未提交**。
- `git status --porcelain`：5 个已跟踪修改 + 1 个新增判据文件 + `.rks-agent/` 流程产物。
- 5 个已跟踪文件与 1 个新增文件的 `sha256`（验证结束时实测）：

| 文件 | sha256 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs` | `83468f8d57bbf94327a137e0e6b9bea09ae910d083c421ad15a7d13cb91778c2` |
| `…/SelectCharacterAcceptedTranscriptTests.cs` | `a1cebfd58d626e37a02d3673a241d6a97315c88891dc94154ddd38bba7c175ea` |
| `…/SelectCharacterPayloadCodecTests.cs` | `b24daca151a281d68d61f5b9f087e27e7afb03053900e1e98e1cce5c896bd319` |
| `…/EntryWriterAvatarShapeTests.cs` | `0430cdfab3290d88941e337dbcfdd811b1e3e66f0962b8dd6e3687cb68fb5bdb` |
| `protocol/game/server-to-client/NOTI-0x000D.md` | `79955c5b77efb0f4740d8ad9b9ef2df9cf2bee986a9e59fd5eb2858162081416` |
| `…/EntryWriterOptionCountTests.cs`（新增） | `5fa870dac4ddcfd13d12e8e79f19cd8083752527ab37d1d94b9d30a2a59561d7` |

跑判据前实测 `tasklist | grep -i Dfo` 无命中，无 `Dfo.Server.Host` 进程；构建未出现 MSB3027。

---

## 判据基线（SC-6）

命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

我跑了两遍全量（一遍在变异实验之前、一遍在全部变异还原之后），两遍结果相同：

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Server.Protocol.Tests | 546 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 329 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Application.Tests | 1039 | 0 | 0 |
| **合计** | **2268** | **2** | **0** |

`EXIT=0`。日志：`<scratchpad>/full-run-1.log`、`<scratchpad>/full-run-final.log`。

那 2 个跳过实测是
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`，
即契约所说「档案确实加载了」的标志。

**基线 2250 → 现在 2268，差 +18。这 18 条我逐条数得出**（不是接受执行方的数字）：
`EntryWriterOptionCountTests` 15 条（`TheOptionCountNeverExceeds…` 9 个 `InlineData`
＋ `NoStoredEmblemSocketCountCanPutAnOverLargeCountOnTheWire` 1 ＋
`TheEmblemCountCountsTheTwoChronicleIdsAndCannotExceedThem` 5 个 `InlineData`）
＋ `EntryWriterAvatarShapeTests` 新增 3 条（`AnItemThatIsNotAnAvatarIsWrittenAsTheSame…`
2 个 `InlineData` ＋ `AnAvatarCarriesNoJewelBytesAnywhereInsideTheEntry` 1）。
15 + 3 = 18，与差额闭合，说明没有既有用例被静默删除。

未出现既知偶发失败（`ObjectDisposedException: 'SQLitePCL.sqlite3'`），未做复跑。

**SC-6：满足。**

---

## 我自己做的三个实验

### 实验 A：生产侧探针（本轮最要紧的一条，独立重做）

在 `GameInventoryItemEntry.Write` 里 `var avatar = item.AvatarDetail;` 之后插入：

```csharp
if (core.EmblemSocketCount != 0)
    throw new InvalidDataException("VERIFIER PROBE: non-zero EmblemSocketCount reached GameInventoryItemEntry.Write.");
```

跑**全量**判据。结果（`<scratchpad>/probe-run.log`）：

- `Dfo.Server.Protocol.Tests`：**失败 9 / 通过 537 / 总计 546**。
- 其余六个项目**全部 0 失败**（Networking 52、Pvf 60、Persistence 329、Infrastructure 236+2 跳过、
  Host 6、Application 1039）。
- 9 条失败**全部**在 `Dfo.Server.Protocol.Tests.Game.StateSynchronization.EntryWriterOptionCountTests`：
  `NoStoredEmblemSocketCountCanPutAnOverLargeCountOnTheWire` 1 条，
  `TheOptionCountNeverExceedsTheOptionsTheWriterActuallyWrites` 的 `stored` ∈ {1,2,3,10,11,127,128,255} 共 8 条
  （`stored: 0` 那条不红，因为探针只对非零抛）。

**与执行方/评审方的说法一致：红 9 条、全在 `EntryWriterOptionCountTests`、其余项目全绿。
没有出现重大发现。** 这条独立复现钉住了：`+0x2F` 非零那条路径在本轮之前**零覆盖**，
所以「全量判据全绿」对它什么都证明不了——这正是契约 SC-8 点名要防的失败模式。

### 实验 B：M1 变异（拿掉 `+0x2F` 的夹紧）

把 `writer.Write(WrittenOptionCount(core.EmblemSocketCount));` 改回
`writer.Write(core.EmblemSocketCount);`（并保留一处 `_ = WrittenOptionCount(0);` 以免 CS 未使用告警
在 `TreatWarningsAsErrors` 下变成错误），跑**全量**（`<scratchpad>/m1-run.log`）：

- 失败 **7 条，全部在 `EntryWriterOptionCountTests`**（`stored` ∈ {3,10,11,127,128,255} 共 6 条
  ＋ `NoStored…` 1 条；`stored` 0/1/2 本就不受夹紧影响，故不红）。
- 其余六个项目全绿；**`Dfo.Server.Protocol.Tests` 里的字节锚
  `AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline` 没有红**——
  即夹紧对参照会话的 62 帧是空操作。
- 失败消息实测为：
  `stored EmblemSocketCount 3 reached the wire as 3, which tells the client to read 3 option dwords where the writer wrote 2.`
  **它指名的正是「服务器交出了一个会越界的计数」**，不是别的性质。
- 因为 `NoStored…` 穷举 0..255 且在 M1 下变红，它**不是恒真断言**。

### 实验 C：M3 变异（把 42 字节尾块加回来）

把删掉的 `if (core.ItemKind == 8) { …; writer.Write(30); writer.Write(avatar.JewelSocket);
writer.Write(4); writer.Write(avatar.Color1); writer.Write(avatar.Color2); }` 原样加回，
跑 `Dfo.Server.Protocol.Tests`（`<scratchpad>/m3-run.log`）：失败 **4 条**，逐条是

1. `SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline`
2. `SelectCharacterPayloadCodecTests.SelectCharacterPayload_InventoryPreservesCommonAndAvatarEntryShapes`
3. `EntryWriterAvatarShapeTests.AnAvatarCarriesNoJewelBytesAnywhereInsideTheEntry`
4. `EntryWriterAvatarShapeTests.AnAvatarWithItsThirtyBytesIsWrittenAsTheSameHundredAndOneByteEntry`

与 `mutation-evidence.md` 的 M3 行**逐条相同**。M2/M4/M5 我未独立重做（契约只要求抽验至少一条，
我抽了两条：M1 与 M3）——这三条的结论在本报告中不作为已验证事实引用。

### 变异与探针的还原证明

手法：**文件级备份 + `sha256sum` 还原并核散列**。
`GameInventoryDelivery.cs` 的基准散列 `83468f8d57bbf94327a137e0e6b9bea09ae910d083c421ad15a7d13cb91778c2`
（备份于 `<scratchpad>/GameInventoryDelivery.cs.BACKUP`）。
共发生 **5 次**对该文件的临时改动（HEAD 版换入 ×2、探针 ×1、M1 ×1、M3 ×1），
**每次用完立刻 `cp` 备份回原位并 `sha256sum` 核对**，5 次核对结果均为上述基准散列；
验证结束时再核一次，仍为该散列。

**全程未使用任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash`）。
取 HEAD 版内容用的是 `git show HEAD:<path> > <scratchpad 里的临时文件>`（只读输出到工作区之外），
再用 `cp` 放置、用 `cp` 从备份还原。

另外我临时新建过一个**只 dump 不断言**的探针判据文件
`Server/tests/…/Game/SelectCharacter/ZzVerifierTempProbe.cs`（用于取字节锚的逐帧长度与十六进制），
用完 `rm` 删除；验证结束时 `git status --porcelain` 只剩交付集的 6 项，无残留。
**除此之外没有改过任何产品或判据文件。**

---

## 逐条成功条件

### SC-1 — 满足

**声明**：`Write` 对任何 `ItemKind`（含 8）都只写 101 字节；有一条判据钉住时装条目长度＝非时装条目长度，
且拿掉这个性质会红。

**我的证据**：

- 读代码：定长区 `WrittenLength = 84` ＋ `writer.Write(new byte[CommonLength - WrittenLength])`
  = 101，之后**没有任何 `writer.Write`**（实测 `diff` 去注释后的代码，见「授权变化」一节）。
- 实测出线字节：我的 dump 探针取到入场序列第 18 帧（`0x000D`，容器类型字节 = `1` = Avatar，
  声明条目数 1）**帧长 121**，减去 15 字节帧头 = 106 = `5`（容器头）+ `101`。
  即时装条目实测就是 101 字节。
- 判据存在且非空转：`EntryWriterAvatarShapeTests.AnAvatarWithItsThirtyBytesIsWrittenAsTheSameHundredAndOneByteEntry`
  与 `AnItemThatIsNotAnAvatarIsWrittenAsTheSameHundredAndOneByteEntry(kind 0/2)` 并存
  ——「时装长度＝非时装长度」两侧都被钉住。
- **拿掉会红**：实验 C 实测，加回尾块后上述第一条与另外三条一起变红。

### SC-2 — 满足

**声明**：`+0x2F` 恒 `<=` 所选上界；有一条判据在夹紧被移除时会红，且该判据指名的是
「服务器交出了一个会越界的计数」。

**我的证据**：

- 上界 = 2，取值依据是「本编码器在 `+0x30` 之后实际写出的 option dword 条数」。
  我自己数过 `Write` 的调用序列：`+0x30` 之后是 `EmblemId1`(i32)、`EmblemId2`(i32)，
  第三个写入 `Rune` 是 u16，不属于该数组 —— **确实是 2**。
- 判据 `NoStoredEmblemSocketCountCanPutAnOverLargeCountOnTheWire` 穷举 `stored` 0..255，
  逐个断言出线字节 `<= 2`；实测偏移正确（M1 下 `stored=3` 出线读到 3，说明读的确是 `+0x2F`）。
- **移除夹紧会红**：实验 B 实测红 7 条，失败消息原文指名
  「reached the wire as 3, which tells the client to read 3 option dwords where the writer wrote 2」，
  即「服务器交出了一个会越界的计数」，不是别的性质。
- 2 同时远低于权威表给的两个客户端阈值（`0x0D` >10 dword、`0x0E` >49 dword），
  故契约 Requires Confirmation 第 2 条（两处理器取不到同一个值）**未触发**。

### SC-3 — 满足

**声明**：`+0x1A` 同样有上界，且该上界有写下来的依据。

**我的证据**：

- 上界为 2，由构造保证：`PopulatedCount(core.ChronicleOption0.Id, core.ChronicleOption1.Id)`
  统计两元素中的非零个数，`checked((byte)…)` ⇒ 值域 {0,1,2}。这是我读代码得出的，不是接受说法。
- 依据写下来了，且写在判据里而不是只在提交信息里：
  `EntryWriterOptionCountTests.TheEmblemCountCountsTheTwoChronicleIdsAndCannotExceedThem`
  的 `<remarks>` 说明「界＝其后写出的两个 id 数」，并注明这条路在客户端走 `push_back`
  （权威表第四节）而非定长栈缓冲，**不是崩溃向量**，与 `+0x2F` 区别对待。
  该判据以 5 个 `InlineData` 断言取值并另断言 `<= 2`。
- 我核对了权威表第四节：`rec+0x1A` 计数驱动 `rec+0x1B` 起 stride 4 的 dword 数组 ＋
  `rec+0x23/0x25/0x27/0x29` 起的属性字节，走 `vector.push_back`，与说明一致。

### SC-4 — 满足

**声明**：时装的 `JewelSocket`/`Color1`/`Color2` 要么落在有证据支持的位置，要么明确不写并写下理由；
两种情形都不得留下「按旧客户端布局猜」的痕迹。

**我的证据**：

- **实测确实没写**：我把 HEAD 版与当前版的入场序列逐帧十六进制取出来比对，
  第 18 帧 HEAD 比当前多出的**恰好 42 字节**是
  `1E000000` `000102…1D`(30 字节) `04000000` `2211` `4433`
  ——即 `30 / JewelSocket / 4 / Color1(0x1122) / Color2(0x3344)`，当前版一个字节都没有。
- **理由写下来了，且是「没有证据」而不是「按旧布局猜」**：
  `EntryWriterAvatarShapeTests` 的 `<remarks>`、`NOTI-0x000D.md` 的补记、
  `behavior-change-list.md` 的 R-2 三处一致地说：权威表把 30 字节消费方放在 `rec+0x1A..0x37`，
  但本仓 `+0x2B` 写 `period`、`+0x2F` 写选项计数**也都有权威表背书**，
  「时装记录让这两处让位」没有证据，**长度吻合不是证据**；因此记成缺口而非完成态。
  我核对权威表第三、四节，确认这一「表内自相矛盾」是真实存在的（表同时给出 `+0x1A..0x37`
  的 30 字节区与 `+0x2B` period / `+0x2F` 计数），执行方如实转述了它，没有替它下结论。
- 旧代码里那条「按旧客户端布局猜」的痕迹（追加块与其注释）已被删除，
  并在同一处留下 Correction 说明它是 A12 布局。
- 「珠宝槽必须恰好 30 字节」的拒绝**保留**，并由 `AnAvatarCarriesNoJewelBytesAnywhereInsideTheEntry`
  额外钉住「珠宝字节也没有被挪进条目内部」（该判据用 `0xA5` 填充，断言整帧不含该字节，
  且时装条目自 `rec+0x1A` 起与同参数的普通条目逐字节相等）。

### SC-5 — 满足（我自己复核了执行方声称的每一个数）

**声明**：不含时装、计数未越界的帧与 HEAD 逐字节相同；会变的帧被逐帧指名并解释。
执行方声称：恰好第 18 帧变化、正好 −42 字节、62 帧总数不变、其余 61 帧不变。

**我的做法**：写一个只 dump 不断言的临时判据，把入场序列（Job 12，与字节锚同一 fixture）
的每一帧完整十六进制导出；分别在**当前生产文件**与**HEAD 生产文件**下各跑一次，
逐帧比对（元素级，不是靠「改动很小」推断）。

**实测结果**：

| 声称 | 我实测 | 判定 |
|---|---|---|
| 62 帧总数不变 | HEAD 62 帧，当前 62 帧 | ✔ |
| 只有第 18 帧变 | 逐帧比对，字节不同的帧下标集合 = `[18]`，其余 **61 帧逐字节相同** | ✔ |
| 正好 −42 字节 | 帧长 `163 → 121`，差 42；HEAD 帧 = 当前帧 ＋ 末尾 42 字节，且**除帧头长度字段**（`A3000000 → 79000000`）**外前 121 字节完全一致** | ✔ |
| 第 18 帧是时装容器 | 该帧 type `0x000D`，容器类型字节 = `1`(Avatar)，声明条目数 1；也是唯一在「加回尾块」下改变的帧，故是唯一带 kind 8 条目的帧 | ✔ |

另：另外那 61 帧里所有 `0x000D` 条目的 `rec+0x2F` 实测**恒为 0**，所以夹紧对字节锚是空操作
（与实验 B 的「M1 下字节锚不红」互相印证）。

判据文件里对第 18 帧的说明（哪一帧、为什么变、变成什么、其余 61 帧同）与我实测**逐项吻合**。

契约 Requires Confirmation 第 1 条（定长化改变了不含时装的帧）**未触发**。

### SC-6 — 满足

见上文「判据基线」。2268 / 2 跳过 / 0 失败，`EXIT=0`，不低于基线 2250，且 +18 的来源已闭合。

### SC-7 — 满足

**声明**：三处记载与权威表一致；两句已证伪的旧说法各有修正案。

**我的证据**：权威表读的是
`E:/Project/DNF/DNF-JP-86-Server/.worktrees/client-debug-lab/Docs/a21-vmp1/vmp1-9.md` 第三节
（**只读，未写入、未触碰该 worktree**，见「边界」一节的证明）。

- **一致性**：我把 `NOTI-0x000D.md` 补记里的偏移表与 `GameInventoryItemEntry` 注释里列的偏移串，
  跟权威表第三节逐行并排，**21 个具名偏移逐个相同**：
  `+0x00 SlotIndex / +0x02 itemIndex / +0x06 Data / +0x0A ext_data1 / +0x0B Durability /
  +0x0D isSealed / +0x0E enchantIndex / +0x12 enchantUpgrade / +0x13 amplify_type /
  +0x14 amplify_value / +0x1A 珠宝(纹章)条目数（其后子数组）/ +0x2B period /
  +0x2F 附加选项条目数（其后 dword 数组）/ +0x4A genuineUpgrade / +0x4B emancipateEquipmentLevel /
  +0x4C tradeRestriction / …+0x64 记录被用满至此（含 +0x61 dword）`。无一处出入、无一处多写。
- **强度标注**：三处都区分了「静态反汇编，未实机」（`rep movsd` 崩溃向量）与「实机观测」
  （`+0x06` 的 4153 天、去掉尾块就不崩加回就崩）。`GameInventoryItemEntry` 注释还明写
  「What was checked is the offsets, not the names」——把「偏移已核对」与「字段名之争未定」分开，
  符合契约「记载不得写成比证据强」。
- **修正案一**（「那 17 个字节位置不可读 / 是补零」）：
  `GameInventoryDelivery.cs` 的 `Correction (2026-09-07)` 明写此前那句是错的；
  `NOTI-0x000D.md` 的「**推翻第一句：那 17 字节不是补零，是未知字段**」。两处都保留了旧说法原文再更正，
  没有悄悄改掉。
- **修正案二**（「时装条目在定长区后追加珠宝块」）：
  `GameInventoryDelivery.cs`、`EntryWriterAvatarShapeTests`、`SelectCharacterPayloadCodecTests`
  三处各有 `Correction (2026-09-07)`，`NOTI-0x000D.md` 有「**推翻第二句**」。
- **`AGENTS.md` 是否需要修正**：我按契约要求查了。`AGENTS.md:632-636` 唯一相关的一句是
  「`NOTI-0x000D.md`（A21 的时装条目未被观察到）」——这句说的是「参照会话里没有观察到时装条目」，
  今天**依然为真**（本轮的新证据来自客户端反汇编与实机崩溃复现，不是抓到一条 A21 时装条目）。
  **无冲突，故未改 `AGENTS.md` 是正确的**，不是遗漏。

### SC-8 — 满足

**声明**：交回一份正向与反向都报的变化清单，覆盖本轮新增或改变的每一条拒绝、夹紧与放行路径。

**我的证据**（重点核这一关，因为上一个成果正是在这里被拦下）：

- 清单在 `behavior-change-list.md`，正向 3 条（F-1/F-2/F-3）、反向分「行为变了 2 条 / 形状变语义未变 1 条 /
  明确没变 5 条」，**每节开头写了计数口径**。
- **我独立重做了它的反向扫描口径**：
  - `GameInventoryItemEntry.Write` 的调用点：`grep -rn --include=*.cs`（排除 `bin`/`obj`）
    实测 **3 处** —— `GameInventoryDelivery.cs:222`、
    `StateSynchronizationPayloadCodec.Inventory.cs:147` 与 `:162`。与清单一致。
  - `CommonLength` 引用点：实测 **3 处**（`:52` 定义、`:126`、
    `StateSynchronizationPayloadCodec.Inventory.cs:179`）。与清单一致。
  - 仓内是否还有别处假设时装条目 143 字节：`grep 143` 命中全部为无关数字
    （奖励额 8143、任务计数 143、随机选项值 143、地址串里的 `0143`）。**0 处遗留**。与清单一致。
- **「新增拒绝」这一关我单独核了**：把 HEAD 版与当前版的 `GameInventoryDelivery.cs`
  去掉注释行后 `diff`，可执行代码差异**只有 3 处**，与 F-1/F-2/F-3 一一对应；
  `throw` 站点 HEAD **6 个**、当前 **6 个**，位置、条件与消息文本逐条相同
  （其中 `Avatar jewel socket must contain exactly 30 bytes.` 的触发条件由
  `if (kind==8) { if (len!=30) throw; …}` 改写为 `if (kind==8 && len!=30) throw;`，
  **逻辑等价，且仍在写完定长区之后求值**，故清单把它记为 R-3「语义未变、形状变了」是对的）。
  **本轮新增拒绝 0 条**——这正是上一个成果漏报 13 条的那一类东西，本轮不存在，
  清单没有漏报。
- 清单点名了 R-1 是**静默截断**（存储 3..255 出线变 2，不报错不拒绝），并说明为什么选静默不选拒绝，
  以及「日后查清 `+0x2F` 语义时第一个被翻出来」。这是契约要求的「正向与反向都报」的实质。
- 清单里那条最关键的「改动前零覆盖」，我用实验 A 独立复现了（红 9 条、全在新判据、其余项目全绿）。

---

## 契约 Constraints 的逐条判定

| 约束 | 判定 | 我的证据 |
|---|---|---|
| 正常数据上逐值不变，唯二授权变化＝时装尾块移除 + 计数夹紧 | **满足** | 62 帧逐帧十六进制比对：61 帧逐字节相同；第 18 帧 = 当前帧 + 42 字节尾块（内容实测为 `30/socket/4/color1/color2`）+ 帧头长度字段。**没有第三处字节变化。** 计数夹紧在参照数据上是空操作（所有条目 `+0x2F` 实测为 0），亦由 M1 下字节锚全绿佐证 |
| 不含时装、计数未越界的帧与 HEAD 逐字节相同；会变的帧被指名 | **满足** | 同上；第 18 帧被指名、给了原因与新旧长度 |
| 既有判据的期望值与断言不得为迁就实现而改写，每处改写须归因到两条授权变化之一 | **满足**（附一条非阻塞观察） | 逐处见下表 |
| 不新增协议帧 | **满足** | `AcceptedStaticIdentities`（62 个 `(selector,type)`）在 diff 中未改动；实测帧数 HEAD 62 / 当前 62，type 序列一致 |
| `CommandHandlerArchitectureTests` / `NotificationHandlerArchitectureTests` 继续通过，谓词与豁免清单不得放宽 | **满足** | 两文件 `git diff HEAD -- <两文件>` **为空**（未被触碰，故谓词与豁免清单不可能被放宽）；单独跑该过滤器实测 **26 通过 / 0 失败** |
| `TreatWarningsAsErrors` 开启，CS9113 是错误 | **满足** | 全量构建无告警即错误，`EXIT=0` |
| 分层与切片架构 | **满足** | 改动全部落在 `Dfo.Server.Protocol/Game/`（端点层共用条目写法，非某切片内部），未新增跨层引用；全量构建通过即项目引用约束成立 |
| 判据全绿，不允许攒红 | **满足** | 最终态全量 0 失败 |
| 不得触碰主检出 `ClientHack/` 与 `.worktrees/client-debug-lab` | **满足** | 见「边界」一节 |
| 禁止 git 还原类命令；变异用文件级备份 + `sha256sum`，用完立刻还原并核散列 | **满足** | 见「还原证明」一节 |
| 记载不得写成比证据强 | **满足** | SC-7 的「强度标注」一项 |

### 三处判据改写的归因（逐处）

| 改写处 | 内容 | 归因 | 判定 |
|---|---|---|---|
| `SelectCharacterAcceptedTranscriptTests` 第 18 帧哈希 | `E10DF2DA…` → `29A34E1F…` | 授权变化一（时装尾块移除） | **成立**。我实测 HEAD 帧哈希确为 `E10DF2DA1FDD5905…`、当前为 `29A34E1F0F1F342B…`，且两帧只差那 42 字节 + 长度字段 |
| `EntryWriterAvatarShapeTests` 的长度断言 | `101 + 42` → `101` | 授权变化一 | **成立**，且**同时补强**：新增「非时装同长」两例与「条目内不含珠宝字节」一例；M3 实测这几条都会红 |
| `SelectCharacterPayloadCodecTests` 的长度与字段读 | `5+101+42` → `5+101`；删掉对尾块内 socket 长度与两颜色的读取，换成容器类型与条目数 | 授权变化一 | **成立**（被读的字节已不在线上）。**非阻塞观察**：替换上来的断言比原来弱；但该用例在 M3 下仍会红（实测），说明未沦为空转，且「时装条目＝101 且内部无珠宝字节」这条性质已由 `EntryWriterAvatarShapeTests` 更强地钉住 |
| —— | 没有任何判据改写归因到授权变化二（夹紧） | | 与「夹紧路径改动前零覆盖」一致（实验 A/B 实测） |

---

## 边界证明

- **未触碰主检出**：`E:/Project/DNF/DNF-JP-86-Server` 的 `git status --porcelain` 仅
  ` M ClientHack/tools/crossmodule-relocs-a21.csv` 与
  `?? ClientHack/tools/crossmodule-relocs-a21.csv.pre-5boot`。
  两者 mtime 分别为 `2026-09-06 22:46:53` 与 `2026-09-06 22:48:09`，**都早于本任务**，
  是契约 Current Situation 里点名的另一个 agent 的客户端重建产物。我没有读写过它们。
- **未触碰 `.worktrees/client-debug-lab`**：其 `git status --porcelain` 仅 3 个未跟踪文件
  `Docs/a21-vmp1/vmp1-8.md / vmp1-9.md / vmp1-10.md`，mtime 分别 `09-06 23:36`、`09-07 00:02`、
  `09-07 08:14`，**均为另一个 agent 所写**。我对 `vmp1-9.md` 只做过 `sed -n` / `grep` 读取，
  未产生任何写入（mtime 未变）。
- **我的产出只有** `.rks-agent/tasks/entries-are-what-the-client-reads/verification/completion-evidence.md`
  （本文件）。临时文件全部落在 scratchpad；工作区内唯一的临时文件是那个 dump 探针判据，已删除。
- **未提交任何东西**：`git log --oneline -1` 仍为 `445cb5f7`。

---

## 非阻塞观察（不影响判定，供接受方知情）

1. `protocol/game/server-to-client/NOTI-0x000E.md:35` 仍写着「单条长度随 offset 0 的列表类型而定
   （时装为 0x7E 字节等）」，来源标注是**上游泄漏源** `ItemListUpdateBuilder.GetUpdateEntrySize`。
   它与本轮确立的「A21 定长 101」并列时容易被误读。该文件**不在 SC-7 点名的三处之内**，
   且同一文件的 A21 实测段落（`:72`、`:121`）写的正是定长 101，故不构成契约违反。
   若日后有人按 `0x7E` 写时装条目，会重现本轮修掉的崩溃——值得单独一件收口。
2. `SelectCharacterPayloadCodecTests` 里被替换上来的断言（容器类型 + 条目数）弱于被删掉的那三条。
   如上表所述该用例仍非空转，但它已不再是「时装条目形状」的主要判据——主要判据现在是
   `EntryWriterAvatarShapeTests`。
3. `mutation-evidence.md` 的 M2 / M4 / M5 三行我未独立重做，本报告不引用其结论。
   M1 与 M3 我独立重做，结果与记载逐条相同。

---

## 结论

SC-1 至 SC-8 **每一条**都有我在当前工件上自取的证据，契约 Constraints 逐条成立，
两条 Requires Confirmation 均未触发，工作区在验证前后散列一致、未提交。

**decision: `pass`** —— 可以进入接受提交。
