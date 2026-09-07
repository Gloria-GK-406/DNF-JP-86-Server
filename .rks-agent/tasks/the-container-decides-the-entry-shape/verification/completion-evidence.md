---
kind: rks-verification
decision: pass
---

# 完成验证：`the-container-decides-the-entry-shape`

**被验证的声明**：worktree `E:/Project/DNF/DNF-JP-86-Server/.worktrees/the-container-decides-the-entry-shape`、
分支 `the-container-decides-the-entry-shape`、基线 `f5c00fb8`、尚未提交的这一份变更集，
满足 TaskContract（revision 2，含 C-1 与 C-2）的 **SC-1..SC-9** 与 Constraints，可以进入接受提交。

**取证立场**：我是独立验证方，fresh context。**执行方的 `behavior-change-list.md`、
`verification/mutation-evidence.md`，以及评审方 `reviews/change-review.md` 的 `verdict: approved`，
一律不作为成功证据采信**——它们只被我当作「待复核的断言清单」。下面每一条的证据都是我本次自己取的。
凡我没有自己取到证据的，明写「未独立复核」。

**工作树在验证前后一致**：`git status --porcelain` 恒为 **7 改 + 2 未跟踪**（本文件写入前）。
探针与变异**全部用文件级备份 + `sha256sum` 还原**，**全程未使用任何 git 还原类命令**
（未用 `git checkout --` / `git restore` / `git stash`）。未用 `--logger trx`，故无 `TestResults/` 需要清理
（`find . -maxdepth 4 -type d -name TestResults` 空）。

---

## 0. 判据的执行环境

跑前查进程：`Dfo.Server.Host.exe` **PID 31276** 在跑，`wmic` 取到它的映像路径是
`E:\...\.worktrees\entries-are-what-the-client-reads\Server\src\Dfo.Server.Host\bin\Debug\net10.0\Dfo.Server.Host.exe`
——属于**另一个 worktree**，未终止它。两次全量日志里 `MSB3027` 命中数 **0**，构建无锁文件问题。

判据命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

| 次序 | 树的状态 | 结果 | EXIT |
|---|---|---|---:|
| 全量 #1 | 交回原样 | **2281 通过 / 2 跳过 / 0 失败** | 0 |
| 全量 #2（末次，探针与变异全部还原之后） | 交回原样 | **2281 / 2 / 0** | 0 |

逐工程（两次相同，计数口径＝`dotnet test` 每个测试 dll 的汇总行，共 **7 个工程**）：
`Networking 52` ＋ `Protocol 559` ＋ `Pvf 60` ＋ `Host 6` ＋ `Persistence 329` ＋
`Infrastructure 236（跳过 2）` ＋ `Application 1039` ＝ **2281**。

那 2 个跳过我从日志里指名了：
`Dfo.Server.Infrastructure.Tests.Content.PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `...PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。
二者只在档案不可达时才跑，**跳过即档案确已加载**。

**+13 的去向我自己核了**：`--filter "FullyQualifiedName~EntryWriterContainerShapeTests"` 实测
**恰好 13 条通过**（口径＝该类展开后的用例数：`TheAvatarListCarriesTheBlockWhateverTheItemIs` 3 ＋
`TheAvatarListsBlockIsTwoLengthPrefixedRuns` 1 ＋ `TheEquipmentListCarriesTheBlockOnlyForAnAvatar` 3 ＋
`AnOrdinaryContainerNeverCarriesTheBlockNotEvenForAnAvatar` 4 ＋ `AvatarListEntriesLineUpBackToBack` 1 ＋
`AnItemWithNoAvatarFactsStillWritesTheBlockInTheAvatarList` 1）。
`546 + 13 = 559`，其余六个工程计数相加与总数无残差，故 **+13 全部来自本轮新增判据**。

→ **SC-6 达成**（2281 ≥ 2268，退出码 0）。

---

## 1. 生产侧探针（本轮最要紧的一条，我独立重做）

**做法**：文件级备份 `GameInventoryDelivery.cs`（基准 `sha256`
`26dfe6c118b9bc4099b239630af9d70fca701f7aca53b5f442d92b46f74ba4d8`），
在 `GameInventoryItemEntry.Write` 首尾取 `writer.BaseStream.Position`，写入两段探针：

1. **拦截**：容器不是 `Avatar` / `Equipment` 且本条写出 > `CommonLength`（101）→ 抛 `InvalidDataException`；
2. **普查**：把每个出现过的 `(容器, ItemKind, 本条字节数)` 三元组去重后落盘。

**结果（全量，EXIT=0）：2281 / 2 / 0，探针一次都没触发，七个工程全绿。**

**这与交办时的预期不同，如实报告**：交办说「预期只红本轮新增的 `EntryWriterContainerShapeTests`」，
实测是**一条都没红**。原因是这个探针在**实现正确时本就不会触发**——
`EntryWriterContainerShapeTests` 里那些「把 kind 8 放进普通容器」的用例，
写出的正是 101 字节，没有越线。所以「全绿」是与 SC-3 一致的结果，不是探针失灵。

**为证明探针确实是活的，我又跑了一次「探针 + 变异 M3」**（Protocol 工程）：
探针立刻抛出四条，异常文本逐字为
`System.IO.InvalidDataException : PROBE: container Main wrote 131 bytes for kind 8.`
以及 `PersonalCargo` / `Pet` / `AccountCargo` 三条同形。**探针可触发，只是本实现不触发它。**

**普查表（全量判据下，`GameInventoryItemEntry.Write` 见过的全部三元组，共 20 行）**：

| 容器 | 见过的 ItemKind → 本条字节数 |
|---|---|
| `Avatar` | 0→**131**、2→**131**、8→**131** |
| `Equipment` | 0→101、2→101、8→**131** |
| `Main` | 0→101、1→101、2→101、3→101、**8→101** |
| `PersonalCargo` | 0→101、2→101、**8→101** |
| `Pet` | 0→101、2→101、**8→101** |
| `AccountCargo` | 0→101、2→101、**8→101** |

**整个判据套件里只有这 6 个容器到达过 `Write`**；`GuildMedal` 等其余枚举值一次都没到达。
这张表是我自己从生产代码里量出来的，不依赖任何判据的断言文本。

还原：`cp` 备份回去，`sha256sum -c` 输出 `OK`（回到 `26dfe6c1…`）。

---

## 2. SC-1 / SC-2 / SC-4：装扮栏与装备栏

- **SC-1（装扮栏每条都带 30 字节块）**：普查表 `Avatar|kind=0/2/8 → 131` 三行；
  加下面第 3 节对第 18 帧的逐字节拆解。**「拿掉会红」我自己做了变异 M1**
  （`InventoryContainerType.Avatar => true` 改 `=> false`）：Protocol 工程 **8 红**，指名为
  `EntryWriterContainerShapeTests.TheAvatarListCarriesTheBlockWhateverTheItemIs(kind: 0 / 2 / 8)`、
  `.TheAvatarListsBlockIsTwoLengthPrefixedRuns`、`.AvatarListEntriesLineUpBackToBack`、
  `.AnItemWithNoAvatarFactsStillWritesTheBlockInTheAvatarList`、
  `SelectCharacterPayloadCodecTests.SelectCharacterPayload_InventoryPreservesCommonAndAvatarEntryShapes`、
  `SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline`。
  → **达成**。
- **SC-2（装备栏只有 kind 8 带，两侧都钉）**：普查表 `Equipment|8→131`、`Equipment|0→101`、
  `Equipment|2→101` 三行是我自己量的**行为**证据；判据侧
  `TheEquipmentListCarriesTheBlockOnlyForAnAvatar` 的三个 `InlineData` 正好钉住两侧，全量下通过。
  → **达成**。（变异 M2 我**未独立重做**——SC-2 的文字要求是「判据钉住两侧」，
  两侧的当前行为与断言我都已直接取到；这一点如实标明。）
- **SC-4（多条连续对齐）**：`AvatarListEntriesLineUpBackToBack` 用 3 条 kind 8 条目，
  断言体长 ＝ `3 + 3×(101+30)`，并逐条从 `3 + index×131` 处读回 i16 槽位等于 index
  ——**读的是槽位而不是总长**，所以它证的是对齐而非只证总长。它在 M1 下会红（见上）。
  另外我对第 18 帧的拆解显示条目区确实按 `101 + 30 = 131` 的步长排布。
  → **达成**。

---

## 3. SC-5 字节锚：四个数我逐个自己复核

要复核的是：**(a) 62 帧，(b) 恰好第 18 帧变，(c) `121 → 151`、差正好 30，(d) 其余 61 帧逐字节不变。**

**方法一（哈希表元素级对比）**：用 `git show f5c00fb8:<字节锚文件>`（只读，非还原命令）取基线数组，
与工作区数组各自抽出 64 位十六进制串后逐元素比较：

- 基线 **62** 个，当前 **62** 个；
- 不同的下标：**只有 `[18]`**（0-based）；相同的 **61** 个；
- `29A34E1F0F1F342B9C28C5D1C50DC6B50A9558AEEE1EA1C698A07D7F5EFDEEFB`
  → `D4C7DEED02A813ED7592A9E9AFF8A624D6B971BC56B2B7A708EAC8FB57D6588A`。

**方法二（临时探针复刻编码路径，取真长度）**：在测试工程内新建一个临时用例复刻
`SelectedCharacterStateMessageFactory.Create` → 已注册编码器的整条路径，导出 62 帧的
`len` 与 `sha256`。当前树实测 `IDX=18 type=0x000D len=151 sha=D4C7DEED…`，与断言表一致。

第 18 帧逐字节拆开（我自己解的，非引用他人）：

```
帧头 15 字节：00 | 0D 00 | 97 00 00 00 (=151) | 00 x8
体   136 字节：01(=Avatar) 00 00 | 01 00 (count=1) | 条目 131 字节
条目 131 = 记录 101 + 块 30
块 30 = 12 00 00 00 (u32 18) | 000102...1011 (18 字节) | 04 00 00 00 (u32 4) | 22 11 | 44 33
```

珠宝那 18 字节是 fixture 存储的 30 字节里的 **前 18**（`00..0x11`），与上游 `WriteFixedBytes` 的
「拷 `min(len)`」截断方向一致。

**(c) 的 `121` 我不是靠算术推的，是量出来的**：把变异 M1（装扮栏 `=> false`）加上，
同一探针跑出 `IDX=18 len=121 sha=29A34E1F0F1F342B9C28C5D1C50DC6B50A9558AEEE1EA1C698A07D7F5EFDEEFB`
——**与 `f5c00fb8` 记的基线哈希逐位相同**。这一步比「151 − 30 = 121」强得多：
它证明**去掉这 30 字节后整帧回到基线的每一个字节**，因此这一帧的变化量**恰好且仅仅**是被授权的那 30 字节。
同一次对比里，62 帧中除 `IDX=18` 外**其余 61 帧的 len 与 sha 全部一致**。

→ **SC-5 达成**。探针文件跑完删除，工作树回到 7 改 + 2 未跟踪。

---

## 4. SC-3「其余容器永不带」——上一件成果栽的那一半

### (a) 有正面判据

`EntryWriterContainerShapeTests.AnOrdinaryContainerNeverCarriesTheBlockNotEvenForAnAvatar`，
`[MemberData]` 覆盖 4 个容器，每个再内循环 kind ∈ {0, 2, 8}，断言体长恒为 `3 + 101`。
另有 `EntryWriterAvatarShapeTests` 的两条（`AnAvatarWithItsThirtyBytesIsWrittenAsTheSameHundredAndOneByteEntry`、
`AnAvatarCarriesNoJewelBytesAnywhereInsideTheEntry`）走 `InventoryContainerType.Main`
（`EntryWriterAvatarShapeTests.cs:138`），构成第二道正面看守。**这是正面要求，不是补集。**

### (b) 变异会红——**我独立重做了 M3**（不照抄）

把 `_ => false,` 改成 `_ => itemKind == 8,`，跑**全量**：**EXIT=1，6 红，全部落在 Protocol**，
指名为：

1. `EntryWriterContainerShapeTests.AnOrdinaryContainerNeverCarriesTheBlockNotEvenForAnAvatar(container: Main)`
2. `…(container: PersonalCargo)`
3. `…(container: Pet)`
4. `…(container: AccountCargo)`
5. `EntryWriterAvatarShapeTests.AnAvatarWithItsThirtyBytesIsWrittenAsTheSameHundredAndOneByteEntry`
6. `EntryWriterAvatarShapeTests.AnAvatarCarriesNoJewelBytesAnywhereInsideTheEntry`

其余六个工程**全绿**（Networking 52、Pvf 60、Host 6、Persistence 329、Infrastructure 236/跳过 2、Application 1039）。
**字节锚在 M3 下一声不吭**——我复核了原因：字节锚 fixture 的主背包里没有 kind 8 的物品
（普查表里 `Main|kind=8` 只由新判据产生）。**所以专门的容器形状判据不是冗余，它是这条规则唯一的看守。**
还原后 `sha256sum -c` 输出 `OK`。

### (c) 覆盖了哪些容器、哪些没覆盖、今天可不可达

`InventoryContainerType` 枚举共 **15 个成员**（`Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs:6-23`）：
`Main 0`、`Avatar 1`、`PersonalCargo 2`、`Equipment 3`、`Pet 7`、`AccountCargo 12`、
`TitleBookGeneral 19`、`TitleBookSpecific 20`、`TitleBookPvp 21`、`TitleBookDespair 22`、
`TitleBookEvent 23`、`QuickSlot 29`、`KnightShieldEquipped 33`、`KnightShieldCatalog 34`、`GuildMedal 38`。

- 归 `Avatar` / `Equipment` 两格的：**2 个**。
- 归「其余」一格的：**13 个**。其中被 SC-3 判据**正面覆盖 4 个**（Main、PersonalCargo、Pet、AccountCargo
  ——恰好是契约点名的四个），**未覆盖 9 个**，分别是：TitleBookGeneral、TitleBookSpecific、
  TitleBookPvp、TitleBookDespair、TitleBookEvent、QuickSlot、KnightShieldEquipped、
  KnightShieldCatalog、GuildMedal。

**这 9 个今天可不可达？我自己扫的路径（口径＝到 `GameInventoryItemEntry.Write` 的全部静态入口）**：

- `GameInventoryItemEntry.Write` 的调用点 **3 个**：`GameInventoryDelivery.cs:309`（`InventoryDeltaMessage`）、
  `StateSynchronizationPayloadCodec.Inventory.cs:147` 与 `:162`（快照家族）。
- `new InventorySnapshotMessage` 的构造点 **1 个**：`SelectedCharacterStateMessageFactory.cs:161`，
  容器来自同函数 `order` 数组，**写死 5 个**：Main、Avatar、PersonalCargo、Pet、AccountCargo。
- `new InventoryDeltaMessage` 的构造点 **2 个**：`GameInventoryDelivery.cs:338`（`update.ContainerType`，
  理论上任意值）与 `:362`（硬写 `Main`）。
- 运行期普查（第 1 节那张表）：整个判据套件里到达 `Write` 的容器**只有 6 个**，那 9 个**一个都没到达**。
- 入场序列里那第 6 个 `0x000D` 帧（我探针量到 `container=38`、帧长 18）来自
  `StateSynchronizationMessages.cs:224`，体是硬写的 `[(byte)GuildMedal, 0, 0]` 三字节，
  **根本不经过 `Write`**。所以「快照只送 5 个容器」与「入场有 6 个 `0x000D` 帧」并不矛盾；
  `behavior-change-list.md` 的说法在它自己声明的范围内准确，这里点出来只是免得读者把两个数对不上。

**结论**：那 9 个今天**均不可达**，且它们与被覆盖的 4 个走的是**同一条 `_ => false` 分支**，
故行为正确性不依赖判据是否逐个列举它们。**但判据的看守力确实只到这 4 个**——
将来往枚举里加一个容器，没有任何判据会提醒它落在哪一格。评审 **O-1**（改成从
`Enum.GetValues<InventoryContainerType>()` 去掉 Avatar / Equipment）是对这一点的正确处置；
我确认它是**改进项而非阻断项**：SC-3 的文字只要求「至少覆盖」那四个。

→ **SC-3 达成**。

---

## 5. `WireJewelBytes = 18`：我自己去上游核了，并给出我的判断

我读的是 `E:/Project/DNF/ServerS4A21`（只读）：

| 我核到的 | 位置 | 内容 |
|---|---|---|
| `A21AvatarJewelBytes = 18` | `Network/Builders/Inventory/ItemListProtocolWriter.cs:13` | 确认 |
| `A21AvatarEntrySize = 84+17+4+18+4+4` | 同文件 `:14-15` | 算得 **131**，确认 |
| `Entry84Size = 84`、`A21CommonEntryExtra = 17` | 同文件 `:10-11` | 普通条目 **101**，确认 |
| `WriteAvatarEntry126` 里 `WriteInt32(18)` + `WriteFixedBytes(…, JewelSocket, 18)` | 同文件 `:72-92` | 确认 |
| `WriteFixedBytes` 的**截断行为** | 同文件 `:417-434` | `Buffer.BlockCopy(value, 0, buffer, 0, Math.Min(value.Length, length))`——**从头截前 18**，不是重排、不是取后 18。本仓 `stored.AsSpan(0, WireJewelBytes)` 与之一致 |
| 存储侧是 30 | `bin/Debug/Sqlite/item_schema.sql:109` 的 `CHECK(length(jewel_socket) = 30)`；`Models/JewelSocket.cs:8-10` 的 `SocketCount=5, EntrySize=6, Size=30` | 确认，「5 孔 × 6 字节、只发前 3 孔」成立 |
| 函数名里的 `126` | `WriteAvatarEntry126` | `84+4+30+4+4 = 126` 恰是 A12 总长；**该函数今天实际写出 131**，即函数名是滞后的旧名 |
| NOTI-2 那条路上游也用 18 | 同文件 `:189-194` 的 `WriteNoti2AvatarBlock` 写 `A21AvatarJewelBytes` | 确认（这条支撑第 8 节的 O-2） |

**我的判断：证据足以支撑把本仓定成 18，且现有记载没有写过头。** 理由与保留同时说清：

- 判据与实机**都区分不了 18 与 30**（块自描述长度；实机珠宝槽全零）——这一点契约 Assumptions、
  `GameInventoryDelivery.cs` 的 `WireJewelBytes` 注释、新判据 `BlockLength` 的注释里**都已明说**，
  没有拿「实机 11 件全过」去冒充长度证据。这是本轮记载最该守住的一条，它守住了。
- 支撑 18 的是**上游 A21 源码读出**（同协议版本的参考实现里那个常量、那种截断写法），
  外加 `WriteAvatarEntry126` 这个滞后函数名反证 30 属于 A12。几条互相独立，指向同一结论。
- **我未能独立复核的一处**：`WireJewelBytes` 注释里「客户端接收这段数据的目标缓冲是一个栈局部，
  离 /GS cookie 的距离远小于 30」这句（`behavior-change-list.md` 写作 `[ebp-0x58]`、约 28 字节被清零）
  来自**另一个 agent 的静态反汇编**，我没有客户端二进制可核。
  它是「多发 12 字节会踩坏相邻局部」这个**动机**的唯一依据；即便它不成立，
  「按上游 A21 发 18」这个**结论**仍由上面几条独立支撑，所以不影响本次判定。
  **但这句在 `GameInventoryDelivery.cs` 里没有按 Constraints 要求标明来源强度**
  （「静态反汇编」「上游源码读出」「实机观测」要分别标明）——同一段落里上游那半和实机那半都标了，
  只有这半没标。**这是一处记载瑕疵，够不上阻断**：它没有把弱证据说成强证据，只是没说自己是哪一种。
  记为观察项 **V-1**。

---

## 6. Constraints 逐条判定

| 约束 | 判定 | 我的证据 |
|---|---|---|
| 除装扮栏容器与装备栏里的时装外**逐值不变** | **满足** | 62 帧中 61 帧 sha 与基线逐位相同；第 18 帧在去掉块后回到基线哈希（第 3 节）；生产侧普查表里所有非 Avatar/Equipment 容器恒 101（第 1 节）；探针全量未触发 |
| 既有判据的期望值与断言**不得为迁就实现而改写** | **满足** | 见下方专表 |
| 不新增协议帧 | **满足** | 帧数 62 → 62；`AcceptedStaticIdentities`（选择子/类型序列）在 diff 里**零改动**；无新增消息类型（`ConcreteStateSynchronizationMessages_EachHaveExactlyOneGameEncoderRegistration` 与 `RegisteredGameMessages_HaveExactlyOneCmdOrNotiClassification` 全量下通过） |
| `CommandHandlerArchitectureTests` / `NotificationHandlerArchitectureTests` 继续通过 | **满足** | 两者位于 `Server/tests/Dfo.Server.Application.Tests/Commands/` 与 `.../Notifications/`；两次全量下该工程 **1039/1039、0 跳过、0 失败**；两个文件在 `git status` 中**未被修改**，故判定谓词与具名豁免清单不可能被放宽或扩充 |
| 未触碰主检出 `ClientHack/`、`.worktrees/client-debug-lab`、`.worktrees/entries-are-what-the-client-reads` | **满足** | 本轮 diff 里零命中这三处。主检出确有未提交内容（`ClientHack/tools/crossmodule-relocs-a21.csv` 及 `Document/` 三份），但 mtime 分别是 `2026-09-06 22:46` 与 `2026-09-07 11:08`，**都早于本任务 worktree 目录的创建时间（2026-09-07 11:55）**，属另一个 agent 的在制品。另两个 worktree 的 `git status` 与它们各自的在制品一致（`entries-are-what-the-client-reads` 只有 1 个自有 yaml 被改；`client-debug-lab` 只有 3 份自有 `Docs/a21-vmp1/*.md` 未跟踪），无本轮痕迹 |
| `TreatWarningsAsErrors` / CS9113 是错误 | **满足** | 两次全量构建 EXIT=0，未因警告中断 |

**判据改写的逐处归因**（口径＝把 `git diff -- Server/tests/` 剥掉注释行与空行后剩下的行，
**恰好 6 行**）：

| # | 改动 | 归因 |
|---|---|---|
| 1 | 字节锚第 18 帧哈希 `29A34E1F…` → `D4C7DEED…` | 被授权的 30 字节。**已用 M1 证明去掉即回到旧哈希**（第 3 节），故不是「为迁就实现而改」 |
| 2 | `Assert.Equal(5 + 101, avatar.Length)` → `Assert.Equal(5 + 101 + 30, avatar.Length)` | 同上；`+30` 就是块长 |
| 3-6 | 新增 4 行断言：偏移 106 读 `18`、128 读 `4`、132 读 `0x1122`、134 读 `0x3344` | 块内四个值。偏移与我第 3 节拆出的字节流逐一吻合（体内块起点 = `5 + 101 = 106`）。**这是新增的钉子，不是对旧断言的放松** |

**没有任何一条既有断言被删除或放松**：`avatar[0]`（容器类型）与 `avatar.AsSpan(3,2)`（count）两条原断言原样保留；
`EntryWriterAvatarShapeTests` 的改动**全部在 `<remarks>` 里，零行代码变动**。

---

## 7. SC-7 / SC-8 / SC-9

**SC-7（三处记载与证据一致，且 `f5c00fb8` 那句有指名的修正案）——达成。** 我核到的修正案有 **4 处**，分别是：

1. `GameInventoryDelivery.cs` 的 `<remarks>`：先 `Correction (2026-09-07)`，
   再 `Correction to that correction (same day)`，**逐句指名**「那个追加块是旧客户端布局／是错的」
   这句错在哪（内容对、条件错：按物品种类而非按容器），并给出正确规则与两次方向相反的崩溃。
2. `EntryWriterAvatarShapeTests` 的 `<remarks>`：同样的两级更正，并把形状断言的归属移交给新判据。
3. 字节锚第 18 帧上方的注释：把 `163 → 121 → 151` 三个长度**并列写出**，
   逐个说明「谁带块、块多长」三次不同的答案，而不是把旧值抹掉。
4. `protocol/game/server-to-client/NOTI-0x000D.md`：三处「**……已被……推翻，原地留记**」引用块
   （字段表 `listParam16`「是容量」一处、`#### 一处仍然存在的功能缺口` 上方一处、该小节内一处），
   **旧文原地保留**，新结论写在同日第二次补记里。另 `NOTI-0x000E.md` 把 `0x7E`=126 更正为 131/101，
   并说明 126 是 A12 总长。

**SC-8（`listParam16` 按上游更正 + 客户端侧按实测记）——达成。** 我自己去上游核了这几条：

| 文档里的说法 | 我核到的位置 | 结论 |
|---|---|---|
| `GetAvatarOpenRange(int avatarListParam16)` **收下参数直接丢掉**，恒返回 `0..209` | `Game/Inventory/Services/ItemSlotBoundService.cs:248-251`，函数体只有 `return new ItemSlotRange(0, 209);` | 属实，**210 格** |
| `InitDefaultListParams()` 对装扮栏写 0 | `Game/Inventory/Services/InventoryService.cs:724-729`，`_listParams[Avatar] = 0` | 属实 |
| 主背包写的 24 是**扩容阶段键**不是容量 | `ItemSlotBoundService.cs:31` 的 `MainExpandStageFull = 24`；`InventoryService.cs:726` 用它做默认值 | 属实 |
| 个人货柜才是容量 | `InventoryService.cs:728` 用 `CargoModel.DefaultCapacity`；`GetPersonalCargoOpenRange` 真的用了该参数 | 属实 |

文档里客户端侧那段（`483` → 已全部扩容 / `0` → 需要扩容）**写成了实机对照**，并**自己写明了残余风险**
（「n=1 的 A/B，没有做回摆，不排除重启时还有别的东西跟着变」），也写明 483 是调试期凑数、
且与主检出那份文档里的 483 不是同一次观测。**没有替客户端下超出观测的断言**，
也没有据上游「没做扩容」推出「装扮栏不能扩容」——文档里明确禁止了这一步。→ 达成。

**SC-9（变化清单正反向都报）——达成。** `behavior-change-list.md` 报了正向 **4 条**（F-1..F-4）、
反向**行为变了 2 条**（R-1、R-2）与**明确没变 4 条**（N-1..N-4），并另点名 4 件事。
我复核了其中可核的计数：`Write` 调用点 **3 个**、`new InventoryDeltaMessage` **2 个**——
与我第 4(c) 节自己扫出的数字一致；我另补出它没列的 `new InventorySnapshotMessage` **1 个**（`:161`），
结论方向不变。计数/长度类字段（`WireJewelBytes`）两端都核过：上游写 18（第 5 节），
下游能吃多大由自描述长度决定（第 3 节拆解），符合契约对这类字段「除核偏移外还要核取值范围」的要求。

---

## 8. 三件评审交办给我的事

**一、干净环境重跑判据** —— 已做两次，见第 0 节：**2281 / 2 / 0，EXIT=0，无 MSB3027**，
跑前确认在跑的 host 进程属于另一个 worktree。

**二、那次未归类的红，照原样记，不升格**

> 本轮曾有**一次**全量出现 **1 条**失败，**未捕获用例名**；随后 11 次复跑全绿。
> 本仓判别「既知偶发」的口径是三条：**异常类型 + 栈根 + 复跑即绿**。
> 该次只满足「复跑即绿」**一条**，另两条因用例名缺失而无从取得。
> **故记为「未归类」，不是已确认的既知偶发。**
>
> 我这一轮**没有**再复现它：我自己跑的两次全量，以及三次带变异/探针的定向跑，
> 出现的失败全部可归因到我自己施加的变异（M1 8 红、M3 6 红），**无任何计划外失败**。
> 我**没有为抓这个名字盲跑**（交办明确不要为此盲跑），也**没有**用 `--logger trx`。
> 这一条**保持未归类**，交给后续处置。

**三、O-2 点名** —— 我独立复核并确认：

> **`Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs:186-189`
> （NOTI-2，已穿戴时装那条路）仍然写 `writer.Write(30); writer.Write(item.AvatarDetail.JewelSocket);`，
> 即 30 字节珠宝**；而本轮把 ITEM_LIST 家族（`0x000D` / `0x000E`）改成了 A21 的 **18**。
> **上游对两条路都用 18**（`ItemListProtocolWriter.WriteNoti2AvatarBlock`，`:189-194`，我已核）。
> **所以本仓现在两条路不一致。**
>
> 本轮不改是对的——契约只授权 ITEM_LIST 那一处字节变化，改 NOTI-2 会动另一处字节锚。
> **但仓内今天对这处不一致零痕迹**：我在 `Server/` 与 `protocol/` 下搜
> `A21AvatarJewelBytes` / `发 18` / `18 字节珠宝`，命中 **5 处**，分别是
> `GameInventoryDelivery.cs:205`、`EntryWriterContainerShapeTests.cs:45`、
> `NOTI-0x000D.md:257`、`:317-318`、`:326-329`——**全部只讲 ITEM_LIST 家族，没有一处提到 NOTI-2 仍是 30**。
> `Entry.cs` 那段代码周围也没有任何注释说明这件事。
> **这是本轮留下最明确的一件后续，且它只活在任务产物里；接受提交后仓内仍将无痕。**

---

## 9. 观察项（不影响本次判定）

- **V-1** `GameInventoryDelivery.cs` 的 `WireJewelBytes` 注释里，「客户端目标缓冲离 /GS cookie 很近」
  这句未标明来源强度（它是另一个 agent 的静态反汇编，我无法独立复核）。同段落里上游与实机两半都标了，
  只有这半没标。见第 5 节。
- **V-2** SC-3 的正面判据只列举 4 个容器；枚举里另外 9 个「其余」成员无判据看守（今天均不可达）。
  与评审 O-1 同一件事。见第 4(c) 节。
- **V-3** 装备栏那一格用 `itemKind == 8` 近似客户端的 `0x166EBC0(itemId)`，两者是否等价未验证；
  且该路径今天没有生产发送方（`new InventorySnapshotMessage` 唯一构造点写死 5 个容器，不含装备栏），
  **因而判据之外无人看守**。执行方已在 `behavior-change-list.md` 点名，我确认属实。
- **V-4** 评审列的 O-4 / O-11 / O-13 我抽查过：小节指针指得不准、`NOTI-0x000E.md:35` 证据列未加
  「只管空条目」的限定、C-2 引用的小节名在契约里 Ctrl-F 找不到——三处确实存在，
  与评审判断一致：**是记载精度问题，不改变任何结论**。

---

## 10. 判定

**`pass`。** SC-1..SC-9 与 Constraints 的每一条，我都在**当前工作树**上取到了自己的证据：
全量判据 2281 / 2 / 0 且 EXIT=0（两次）；生产侧探针全量零触发并被单独证明可触发；
变异 M1 与 M3 独立重做且红的用例逐条指名；字节锚四个数逐个复核，其中 `121` 是量出来的而非算出来的；
`WireJewelBytes = 18` 的上游依据逐条核实，其唯一不可核的一环不影响结论。

**留给接受提交之后的两件**：O-2（NOTI-2 发 30、ITEM_LIST 发 18，仓内无痕）与
V-2 / O-1（「其余容器」判据只覆盖 4 个枚举成员）。
**那次未归类的红保持未归类**，未升格为既知偶发。
