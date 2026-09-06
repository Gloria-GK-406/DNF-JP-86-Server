# TASK-08 — 一件时装不是一堆东西

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

修掉本轮引入的一处可达缺陷：**一件时装的身份被当成了「这一格有几个」**。
后果是在背包里拖一件发放来的时装会把它拆成两半并损坏它，
下一次入场装载抛 `Avatar detail 1 is unavailable.`。

- Referenced spec requirements: RQ-003（「物品始终存在」），并保护 RQ-002 已交付的形状
- Referenced plan sections: 本任务由 QR-06-1 的发现新增，见 TASK-06 的 Controller Disposition D-06
- Owned boundary:

BD-004 `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`
BD-009 `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs`（**新增边界**）
BD-008 `Server/tests/`（只新增判据）

- Permitted files or interfaces:

- `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`
- `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs`
- `Server/tests/` 下的判据
- 因编译需要而必须同步的调用点（若有，逐个在执行结果里列出）

- Dependencies: TASK-02
- Required dependency outputs: `ArrivingAvatar.NewIdentity()` 落进 `Core.Value` 的那条通路
- Acceptance conditions:

1. **在背包里拖一件发放来的时装（目标落在自有段），它整件移动，不被拆开。**
   源格不残留 `Value = 身份 - 1` 的行；目标格不出现不带六列事实的新行。
2. **拖动之后再装载，那件时装仍然完整**：身份与六列事实与发放时相同，不抛异常。
3. **目标格有别的东西时的行为要查明并如实记**：是整件对调，还是被拒？
   两种都可接受，但**必须有判据钉住实际是哪一种**，不得只推演。
4. **堆叠物的移动行为一字不变**：拆堆、并堆、部分移动都照旧。
5. 上述每一条都有判据，且**每条判据都经过一次变异实测证明它不是恒真**。

- Task-specific authorization:

Task Unit **不建 Git 检查点、不提交**。接受提交由工作区控制器在完整评审与新鲜验证之后做一次。
变异验证**一律用文件级备份 + `sha256sum` 还原并核散列**；**禁止任何 git 还原类命令**。
不得触碰主检出与 `.worktrees/client-debug-lab`。

- Blocking conditions:

**若修法需要改变 `Core.Value` 对时装的语义（即不再把身份放在那一位），停下并提交阻塞报告。**
那会推翻 TASK-02 已被接受的中心裁定，属改「要求」，须由控制器裁决。

**若修法需要改动协议帧或任何帧的字节布局，停下并提交阻塞报告。**

- Required methods:

- `test-driven-development`：先让判据红，再让它绿。
- 覆盖主张必须由**变异实测**支撑，**不得只凭 grep 或阅读断言**。
- 记法：**不得出现任何全称量词**（唯一／任一…即足以／只有当…才／均为／总是／必然／全部／都会）。
  只写①观测「在 X 配置下施加变异 Y，观测到 Z」附散列与出处；②明写「这些实验不足以确定…」。
  **自查不要用词表搜**——本轮已证明换个措辞词表就整体失效。
  判准：**逐句问「这句在说一个个例，还是在说一类？」**
- 遇到未预期的失败时用 `systematic-debugging`，先取得证据再改。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-022 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Application.Tests --nologo` | 新判据证明拖动一件发放来的时装不会拆开它；拖动后再装载它仍完整 |
| CK-023 | Non-command：备份并核散列后，把修复处改回原样，重跑 CK-022，再还原并核散列 | 新判据变红；还原后恢复绿 |
| CK-024 | Non-command：备份并核散列后，逐条对堆叠物的拆堆／并堆／部分移动做变异 | 既有堆叠判据仍然承重，本次修复未把它们改成恒真 |
| CK-025 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败，退出码 0；用例数相对 **2230** 的差额逐项目归因；字节锚仍与基线逐字节相同 |

- Required evidence:

- 改了哪些文件、每处改了什么（按成员名说，不按行号）
- 四条检查的实际输出与退出码，用例数增量逐项目归因
- 每条新判据各自的变异实测：变异前后散列、哪条红了、红在哪一句
- 验收条件 3 的**实测结果原文**（目标格有东西时到底是对调还是被拒）
- 自查：逐条对照验收条件 1..5，每条凭据必须是真的做过的实测

- Constraints:

- 分层：Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host
- `TreatWarningsAsErrors` 开启，**CS9113 是错误**
- 不新增协议帧，不改任何帧的字节布局
- `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 必须继续通过，
  判定谓词与具名豁免清单**不得放宽或扩充**
- **既有断言的期望值不得为迁就实现而改写**
- 不写读者无法证伪的基数：写「N 处，分别是：…」并给计数口径
- 不给 `PvfEquipmentSlots.ByName` 补任何键

---

## 执行结果

### 改了哪些文件、每处改了什么

**1. `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs`（BD-009）**

改了一个成员：`ContainerSlotItem.Count`。

- 改前：`public int Count => Core.Value;`
- 改后：`public int Count => this.IsPile ? Core.Value : 1;`
- 同一成员的 XML 文档加了一段 `<remarks>`，按本仓「Amended；把改前的读法引出来」的写法记下：
  这个读只对堆是「有几个」；一件个体装备的 `Core.Value` 是身份
  （时装的身份是 `SqliteItemDomainStore.FillAsync` 查六列事实用的键，
  起始装备的身份由 `CharacterRosterService.EquipmentCore` 以同样方式抽取），
  以及分支为什么用 `IsPile` 而不是另起一个判据。
- 文件散列：改前 `e560584a576318309b4660584c4c376347b4a40fdbebbfe6752760f857214ce8`，
  改后 `a39f94f96f3000cb393c1f34c9631d10477a1d48d5c7a501d5afd04ae41cd659`。

**2. `Server/tests/Dfo.Server.Application.Tests/Inventory/DraggedAvatarStaysWholeTests.cs`（BD-008，新增）**

四条判据，跑操作者自己的 PVF 归档与真实 SQLite 库，容器与装载都走生产路径
（`InventorySide` 绑定 `PersistentItemContainer`，装载走 `CharacterInventoryEntryDomainService`）：

| 判据 | 它主张什么 |
|---|---|
| `A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from` | 发放来的时装被以 `count=1` 拖到自有段的空格：源格读回 `null`，目标格的 `Core.Value` 等于拖动前源格读到的那个身份，`ItemKind` 是 8，容器答的 `Count` 是 1 |
| `An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts` | 拖动后再走入场装载：身份与六列事实与拖动前装载到的相同，且源格不在装载结果里 |
| `A_drag_of_one_onto_another_avatar_trades_the_two_slots` | 目标格站着另一个模板的时装时，两端对调（见下「验收条件 3 的实测结果原文」） |
| `A_drag_of_one_off_a_pile_still_divides_the_pile` | 同一个请求形状（`count=1`）作用在 2 个一堆的可互换物上：源格剩 1、目标格 1 |

**没有改**：`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`（BD-004）。
它在 CK-024 的三次变异里被改过又按 `sha256sum` 还原；交付时散列
`8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db`，
与本任务开始时读到的散列逐字符相同。
没有出现「因编译需要而必须同步的调用点」——两次全量构建（CK-022、CK-025）没有报错或警告失败。

### 为什么选这个修法

三条理由，按分量排：

1. **这是缺陷本身所在的那一行，不是它的下游。**
   D-06 追出来的链条起点是「`Count` 无条件读 `Core.Value`」，
   `DecideMoveAsync` 只是第一个被这个读法误导的调用者。
   在 `DecideMoveAsync` 里对时装开一个特例，会把同一个错误读法留给
   `ItemContainerOperations.ConsumeAsync`（`remaining = item.Count - count`）
   和 `StoreBackedItemContainer.PilesOf`（`new ItemPile(item.SlotIndex, item.Count)`）——
   D-06 的第 3 条非阻断观察已经点了 `ConsumeAsync` 属同一条链。
   另外，`MoveToAsync` 最后答的是 `new ItemMoveOutcome.Moved(plan.MoveCount, ...)`，
   若在 `DecideMoveAsync` 里把 `moveCount` 取成 `moving.Count`，那个被抽出来的身份就会顺着这里出去。

2. **改后的行为正是这个成员的文档一直写着的那句话。**
   `/// <summary>How many the slot holds. For anything that does not stack, one.</summary>`
   这句在改动之前就在文件里，而实现给不堆叠的东西答的是 `Core.Value`。
   这是本仓 MEMORY 里记的「注释漂移到反面」的又一例：修法是让代码追上它已经许诺给调用者的话，
   不是新立一条要求。

3. **堆与非堆的分界用的是既有的那个。**
   `IsPile => this.WorldClass is ItemWorldClass.Interchangeable`，
   而 `WorldClass` 由 `StoreBackedItemContainer.ToSlotItemAsync` 向
   `IItemPlacementRuleService.ClassifyAsync` 问归档要，不读行上的 kind 字节。
   没有新造概念，也没有把「能不能数」和「两个是不是同一个东西」变成两个可以互相跑偏的答案。

**没有触及阻断条件**：`Core.Value` 对时装仍然装身份。
改的是「容器被问到这一格有几个时答什么」，不是「身份放在哪一位」。
装载、存盘、两个投影与 `FillAsync` 仍在 `Core.Value` 上找身份，
TASK-02 的中心裁定未被推翻。协议帧与帧的字节布局未动。

### 四条检查的实际输出与退出码

先取基线（改动之前，同一工作区，同一命令）：

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
EXIT=0
Protocol 524 / Networking 52 / Pvf 60 / Host 6 / Persistence 320 /
Infrastructure 236 通过 + 2 跳过 / Application 1032
通过数合计 2230
```

这与契约里 CK-025 写的 2230 一致，也与它给的「521→524 Protocol、316→320 Persistence、
1025→1032 Application」对得上：那三项增量已包含在这 2230 里。

| ID | 退出码 | 输出 |
|---|---|---|
| CK-022 | 0 | `已通过! - 失败: 0，通过: 1036，已跳过: 0，总计: 1036 - Dfo.Server.Application.Tests.dll` |
| CK-023 | 见下 | 变异后 `EXIT=1`，`失败: 3，通过: 1`；还原后 `EXIT=0`，`失败: 0，通过: 4` |
| CK-024 | 见下 | 三次变异各自 `EXIT=1`；还原后散列与变异前相同 |
| CK-025 | 0 | 七个项目零失败，通过数合计 2234 |

**CK-025 的用例数增量逐项目归因**（后一次运行减前一次运行，两次都是我自己跑的）：

| 项目 | 基线通过 | 改后通过 | 差额 | 归因 |
|---|---|---|---|---|
| Dfo.Server.Protocol.Tests | 524 | 524 | 0 | — |
| Dfo.Server.Networking.Tests | 52 | 52 | 0 | — |
| Dfo.Pvf.Tests | 60 | 60 | 0 | — |
| Dfo.Server.Host.Tests | 6 | 6 | 0 | — |
| Dfo.Server.Persistence.Tests | 320 | 320 | 0 | — |
| Dfo.Server.Infrastructure.Tests | 236（另 2 跳过） | 236（另 2 跳过） | 0 | 那 2 条跳过是 `ContentArchive` 缺席时的 Skip，两次运行读数一致 |
| Dfo.Server.Application.Tests | 1032 | 1036 | **+4** | `DraggedAvatarStaysWholeTests` 的四条新判据 |
| **合计** | **2230** | **2234** | **+4** | 差额落在 Application.Tests |

**字节锚**：`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`
散列 `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`，
与契约给的 `1424a4a8…` 相符，本任务开始与结束各测一次，两次读数相同。

**既知偶发**：这几次运行里没有出现 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，
也没有出现栈根为 `CharacterDatabaseSchema.KeepWritersAndReadersApart` 的失败。

### 每条新判据的变异实测

变异一律：`cp` 出文件级备份 → `perl -pi` 就地改 → `sha256sum` 记散列 → 跑 → `cp` 备份回来 → `sha256sum` 核对。
没有用过任何 git 还原类命令。

#### 先手的红（TDD，修复落地之前）

改动之前跑新判据文件：`EXIT=1`，`失败: 3，通过: 1`。
`IItemContainer.cs` 当时散列 `e560584a…`。三条各自红在：

- `A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from`
  红在 `Assert.Null(source)`，实际读到
  `ContainerSlotItem { SlotIndex = 9, … ItemKind = 8, ItemId = 101550507, Value = 322724258,
  … WorldClass = Avatar, Count = 322724258, IsPile = False }`。
- `A_drag_of_one_onto_another_avatar_trades_the_two_slots`
  红在 `Assert.Equal(new ItemMoveOutcome.Moved(1, AvatarItemId), moved)`，
  实际 `Refused { Reason = PartialSwapNotAllowed }`。
- `An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts`
  未走到比较就抛：`System.IO.InvalidDataException : Avatar detail 1300954228 is unavailable.`，
  栈根 `InventoryEntryAssembly.WithAvatarDetail`（`InventoryEntryAssembly.cs:400`）。
- 第四条 `A_drag_of_one_off_a_pile_still_divides_the_pile` 当时是绿的。

这次读数与 D-06 记的 RX-01／RX-02 是同一组现象：源格残留、目标格无事实、下一次装载抛
`Avatar detail … is unavailable.`、目标格有东西时 `PartialSwapNotAllowed`。

#### CK-023：修复处改回原样

变异：`public int Count => this.IsPile ? Core.Value : 1;` → `public int Count => Core.Value;`

| | 散列 |
|---|---|
| 变异前（=交付态） | `a39f94f96f3000cb393c1f34c9631d10477a1d48d5c7a501d5afd04ae41cd659` |
| 变异后 | `d0c8abe02bdaff2977576d005cc42adcadf505141acca09e248f5e528e39844f` |
| 还原后 | `a39f94f96f3000cb393c1f34c9631d10477a1d48d5c7a501d5afd04ae41cd659` |

变异后 `EXIT=1`，`失败: 3，通过: 1`，红的三条与红在哪一句和上面那次先手的红逐条相同
（这一次身份抽到的数不同：源格 `Value = 1599765288`，装载抛的是
`Avatar detail 443172841 is unavailable.`；抛点与断言位置相同）。
还原后 `EXIT=0`，`失败: 0，通过: 4`。

#### 让第四条判据自证不恒真

上面那次变异让三条时装判据红，堆的那条两边都绿——两边都绿说明它对那次变异不敏感，
不说明它不恒真。所以补做一次方向相反的变异：

变异：`public int Count => this.IsPile ? Core.Value : 1;` → `public int Count => 1;`
（即「什么都当成一件」，也就是「用整件移动去修，顺手把堆也压平」这种改法）

| | 散列 |
|---|---|
| 变异前 | `a39f94f96f3000cb393c1f34c9631d10477a1d48d5c7a501d5afd04ae41cd659` |
| 变异后 | `1ca8c5681174ce32093088c0bdf25f64bc5d188346cdab70e6e59d0073c9d6eb` |
| 还原后 | `a39f94f96f3000cb393c1f34c9631d10477a1d48d5c7a501d5afd04ae41cd659` |

`EXIT=1`，`失败: 1，通过: 3`：
`A_drag_of_one_off_a_pile_still_divides_the_pile` 红在 `Assert.NotNull(source)`
（`DraggedAvatarStaysWholeTests.cs:270`）——堆被整件搬走了，源格空。
三条时装判据这次是绿的。

两次变异合起来，四条判据各自至少红过一次，红的那一句都记在上面。

### 验收条件 3 的实测结果原文

**实测是整件对调，不是被拒。**

判据 `A_drag_of_one_onto_another_avatar_trades_the_two_slots`：
两件不同模板的时装（`101550507` 与 `101550511`）经 `AddAsync` 各自落格，
先读下两格的 `Core.Value`（各自的身份），再以 `MoveToAsync(第一格, 第二格, 1)` 拖，
拖完把两格重新读出来。改动之后这条是绿的，它断言的是：

- `Assert.Equal(new ItemMoveOutcome.Moved(1, AvatarItemId), moved)`
- 第一格现在是 `(OtherAvatarItemId, 拖动前第二格的身份)`
- 第二格现在是 `(AvatarItemId, 拖动前第一格的身份)`
- 两格都还能被入场装载服务出来，且 `AvatarDetail` 非空

**改动之前同一条判据的实际输出原文**（先手红那一次与 CK-023 变异那一次逐字相同）：

```
Assert.Equal() Failure: Values differ
Expected: Moved { MoveCount = 1, ItemId = 101550507 }
Actual:   Refused { Reason = PartialSwapNotAllowed }
```

即：改动之前是 `PartialSwapNotAllowed`（D-06 记的 RX-02），改动之后是整件对调。

**这次实验没有覆盖的**：目标格站着同一个模板的时装时会怎样，没有在判据里钉。
读代码可以看到 `DecideMoveAsync` 在 `displaced.ItemId == moving.ItemId` 时走的是合堆分支，
但这只是阅读，本次实测不足以确定那一路的实际读数是什么。
目标格站着可互换的堆、或站着别的 world class 的个体，也都没有实测。

### 堆叠物回归面的证据（CK-024）

**首先是修法本身的形状**：改后的表达式在 `IsPile` 为真时求值为 `Core.Value`，
与改前那个表达式写的是同一个东西；分支只在 `IsPile` 为假时才给出不同的值。
但这是阅读，所以下面是实测。

三次变异，每次都改 `ItemContainers.cs` 里堆叠物移动的一个承重点，
两个项目各跑一遍，看既有判据是否还会红：

| 变异 | 改了什么 | `ItemContainers.cs` 变异后散列 | 结果 |
|---|---|---|---|
| **MU-A 拆堆** | `SplitAsync` 的源格写值 `Value = moving.Count - moveCount` → `Value = moving.Count` | `58b69844f9412c42cc746c225a51d6d69180dba36deb695c8a67a9ca4df482f6` | Persistence `EXIT=1`，`失败: 3，通过: 317`；Application `EXIT=1`，`失败: 2，通过: 1034` |
| **MU-B 并堆** | `MergeAsync` 的目标格写值 `Value = checked(target.Count + moveCount)` → `Value = target.Count` | `d79acbc594512a80e9e957d1e0d6fc86d71c91c0fbfa459a2b9da009c5798dab` | Persistence `EXIT=1`，`失败: 1，通过: 319`；Application `EXIT=1`，`失败: 2，通过: 1034` |
| **MU-C 部分移动** | `DecideMoveAsync` 的 `moveCount == moving.Count ? Relocate : Split` → 恒取 `Relocate` | `af4399179f8934acd4adcc1f5744272b1da7c00951b4643f7139c9e3d6a09cec` | Persistence `EXIT=1`，`失败: 3，通过: 317`；Application `EXIT=1`，`失败: 2，通过: 1034` |

红的具体是哪些：

- **MU-A**：`SplitPileTests.SplittingAPileLeavesTwoUsablePilesAndMarksNeither`、
  `SplitPileTests.EitherHalfCanBeMovedOnItsOwnAfterTheSplit`、
  `SplitPileTests.TheTwoHalvesPourBackIntoOnePile`、
  `MoveItemSpaceHandlerTests.Dragging_part_of_a_pile_onto_an_empty_slot_leaves_the_rest_behind`、
  `DraggedAvatarStaysWholeTests.A_drag_of_one_off_a_pile_still_divides_the_pile`
- **MU-B**：`SplitPileTests.TheTwoHalvesPourBackIntoOnePile`、
  `MoveItemSpaceHandlerTests.Two_piles_of_one_item_merge`、
  `MoveItemSpaceHandlerTests.A_merge_within_the_stack_limit_still_happens`
- **MU-C**：`SplitPileTests` 的三条 + `MoveItemSpaceHandlerTests.Dragging_part_of_a_pile_onto_an_empty_slot_leaves_the_rest_behind`
  + `DraggedAvatarStaysWholeTests.A_drag_of_one_off_a_pile_still_divides_the_pile`

三次变异之后 `cp` 备份还原，散列
`8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db`，
与变异前逐字符相同。

**这些实验支持的读数**：本次修复之后，拆堆、并堆、部分移动这三处的既有判据仍会因对应的实现被改坏而变红，
没有变成恒真；而且在没有变异的运行里（CK-022、CK-025），这些判据都是绿的。

**这些实验不足以确定的**：它们只覆盖了这三个承重点和跑到的这些用例。
装备容器、货仓、地面拾取等别的容器通路上的堆叠行为没有单独变异；
`ConsumeAsync` 与 `PilesOf` 这两个同样读 `Count` 的地方，其读数在本次修复前后是否变化，
本次没有做专门的实测（它们在 CK-025 里跑到的用例是绿的，但那不等于测过这个差别）。

### 自查：逐条对照验收条件 1..5

**1. 「在背包里拖一件发放来的时装（目标落在自有段），它整件移动，不被拆开。」**
凭据：`A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from`，CK-022 里绿。
它读回源格得到 `null`（不是 `Value = 身份 - 1` 的行），
读回目标格得到 `ItemKind = 8`、`Core.Value` 等于拖动前源格的身份。
自有段是从归档问来的（`PlacementForAsync(...).SlotStart/SlotEnd`）而不是写死的数；
运行里时装落在槽 9，与 D-06 说的 9..64 起点一致。
「目标格不出现不带六列事实的新行」由下一条的装载来证。

**2. 「拖动之后再装载，那件时装仍然完整。」**
凭据：`An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts`，CK-022 里绿。
装载走的是 `CharacterInventoryEntryDomainService.LoadAsync`，
也就是改动前会抛 `Avatar detail … is unavailable.` 的那个读者。
比较的是拖动前装载到的与拖动后装载到的：`ItemId`、`ItemKind`、`Core.Value` 相等，
六列事实（`ExpireDate`／`ClearAvatarId`／`JewelSocket`／`Color1`／`Color2`／`DeleteDate`）逐项相等。

**3. 「目标格有别的东西时的行为要查明并如实记。」**
凭据：`A_drag_of_one_onto_another_avatar_trades_the_two_slots`。
实测是**整件对调**，原文与改动前的 `PartialSwapNotAllowed` 原文都抄在上面那一节。
没有只推演：改动前后各跑过一次，两次的实际输出都记了。
钉住「对调」而不是「移动了核但名字没跟着走」的是身份比较——
两格拖动前的 `Core.Value` 被先读下来，拖动后交叉比对。

**4. 「堆叠物的移动行为一字不变。」**
凭据：CK-024 的三次变异（拆堆／并堆／部分移动各一次），既有判据仍然会红；
加上 `A_drag_of_one_off_a_pile_still_divides_the_pile`——同一个 `count=1` 的请求形状，
2 个一堆的可互换物仍然拆成 1 + 1。
CK-025 里 Persistence.Tests 与 Protocol.Tests 的通过数与基线相同，跑到的用例里没有因这次改动改变结果的。
如上一节所写，这些实验没有覆盖别的容器通路，也没有专门测 `ConsumeAsync` 与 `PilesOf` 的读数差别。

**5. 「上述每一条都有判据，且每条判据都经过一次变异实测证明它不是恒真。」**
四条新判据：三条时装判据在「`Count => Core.Value`」这次变异下红（CK-023），
第四条堆判据在「`Count => 1`」这次变异下红。两次变异的前后散列都记了，红在哪一句也都记了。
被借来支持验收条件 4 的既有判据（`SplitPileTests` 三条、`MoveItemSpaceHandlerTests` 三条）
在 MU-A／MU-B／MU-C 里分别红过，见上表。
本轮那个恒真陷阱（`Assert.True(Core.Value > 0)` 那类）在这里没有重演：
新判据里没有只断言「非零」「非空」而不与一个独立取得的值比对的时装断言——
身份是拖动前先读下来再比的，六列事实是拖动前先读下来再逐项比的。

### 记法自查

逐句读了一遍上面的每一段，问的是「这句在说一个个例，还是在说一类」。
说到「不足以确定」的地方另起了段落明写，没有靠词表搜。
两处本来会写成一类的地方改成了个例加限定：
一是堆叠回归面（改成「本次跑到的这些承重点与这些用例」并列出没覆盖的），
二是验收条件 3（把「同模板时装会怎样」明写为未实测，只说读代码看到走的是哪一支）。

### 阻塞

无。没有触及两条阻断条件：`Core.Value` 对时装仍装身份，协议帧与帧的字节布局未动。

---

## Independent Quick Review QR-08-1

**判定：`passed`**

评审的契约：Task Unit Contract **revision 1**，无修正案。
评审者与执行者是不同的上下文，本轮从零读起，没有沿用上一次被中断的评审的任何结论
（上一次没有写下任何东西）。评审只读了本 Task 文件、TASK-06 的 Controller Disposition D-06、
本 Task 的所有权 diff 与它触到的源文件，以及自己跑出来的输出。

### 所有权 diff 的身份

工作区 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/server-agrees-what-an-avatar-is`，
未提交，`git rev-parse HEAD` = `565d33fee899556476f8a582605fb4f439261c0f`。
`git status --porcelain` 读到 12 行 ` M` 与 6 行 `??`（口径：porcelain 每一行算一条，
`.rks-agent/tasks/server-agrees-what-an-avatar-is/` 作为一个未跟踪目录算一行）。

本 Task 声称拥有的两处，散列在评审开始与结束各测一次，两次读数相同：

| 文件 | 散列 |
|---|---|
| `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs` | `a39f94f96f3000cb393c1f34c9631d10477a1d48d5c7a501d5afd04ae41cd659` |
| `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` | `8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db` |
| 字节锚 `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs` | `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69` |

`git diff -- Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs` 读到的改动是
一个成员体加一段 `<remarks>`：`public int Count => Core.Value;` →
`public int Count => this.IsPile ? Core.Value : 1;`。
`ItemContainers.cs` 的 `git diff` 里没有本 Task 的改动（它的改动来自本轮更早的 Task）。
`Server/tests/Dfo.Server.Application.Tests/Inventory/DraggedAvatarStaysWholeTests.cs` 是新增文件。
边界 BD-004／BD-009／BD-008 之外的文件在评审前后散列未变（对上表三个逐一测过）。

### 跑过的命令与读到的输出

| 编号 | 命令 | 退出码 | 读数 |
|---|---|---|---|
| RV-1 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`（交付态，评审开始时） | 0 | Networking 52／Protocol 524／Pvf 60／Host 6／Infrastructure 236＋2 跳过／Persistence 320／Application 1036 = **2234 通过 / 2 跳过 / 0 失败** |
| RV-2 | 变异 MU-REV 后 `dotnet test Server/tests/Dfo.Server.Application.Tests` | 1 | 失败 3，通过 1033，总计 1036 |
| RV-3 | 变异 MU-REV2（`Count => 1`）后跑全量 slnx | 1 | Persistence 失败 6／通过 314；Application 失败 16／通过 1020；其余四个项目零失败 |
| RV-4 | 变异 MU-B 后分别跑 Application.Tests 与 Persistence.Tests | 1／1 | Application 失败 2／通过 1034；Persistence 失败 1／通过 319 |
| RV-5 | 还原后再跑全量 slnx | 0 | 与 RV-1 逐项相同：**2234 / 2 跳过 / 0 失败** |

RV-1 与 RV-5 的读数与执行结果里 CK-025 写的 2234 相符；Application 1036 与 +4 的归因相符。
这几次运行里没有读到 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，
也没有读到栈根为 `CharacterDatabaseSchema.KeepWritersAndReadersApart` 的失败。

三次变异一律 `cp` 出文件级备份、`perl -pi` 就地改、跑完 `cp` 回来并 `sha256sum` 核对；
没有用过任何 git 还原类命令；每次变异用完当次就还原。
变异后散列与执行结果记的逐字符相同：
MU-REV `d0c8abe02bdaff2977576d005cc42adcadf505141acca09e248f5e528e39844f`、
MU-REV2 `1ca8c5681174ce32093088c0bdf25f64bc5d188346cdab70e6e59d0073c9d6eb`、
MU-B `d79acbc594512a80e9e957d1e0d6fc86d71c91c0fbfa459a2b9da009c5798dab`。

### 一、修法的读点面：`Count` 被谁读，谁此前依赖旧读法

计数口径：在 `Server/src` 下，取一个静态类型为 `ContainerSlotItem` 的表达式的 `.Count`。
按这个口径读到 **9 处，分别是**：

1. `ItemContainers.cs` `ItemContainerOperations.SplitAsync`：`moving.Count - moveCount`
2. `ItemContainers.cs` `MergeAsync`：`checked(target.Count + moveCount)`
3. `ItemContainers.cs` `MergeAsync`：`moveCount == moving.Count`
4. `ItemContainers.cs` `MergeAsync`：`moving.Count - moveCount`
5. `ItemContainers.cs` `ConsumeAsync`：`item.Count - count`
6. `ItemContainers.cs` `DecideMoveAsync`：`requestedCount > moving.Count ? moving.Count : requestedCount`
7. `ItemContainers.cs` `DecideMoveAsync`：`moveCount == moving.Count ? Relocate : Split`
8. `ItemContainers.cs` `DecideMoveAsync`：`displaced.Count + moveCount <= placement.StackLimit`
   与 `moveCount != moving.Count`（同一个判定的两句，合记为一处读点组）
9. `ItemContainers.cs` `PilesOf`：`new ItemPile(item.SlotIndex, item.Count)`

另有两处在别的文件：`DailyRefillService` 的
`held.Sum(item => Math.Max(0, item.Count))`，与 `ItemUseEffectDomainService.ApplyAsync` 的
`var remaining = item.Count;`。加上这两处，读点合计 11 处。
`Dfo.Server.Protocol` 里读容器的四处（`SelectCardCommand`、`GroundPickupSlice`、
`MoveItemSpaceSlice` 的两条装备通路、`UseStackableSlice`）读的是 `.Core`，
`grep -n Count` 在 `MoveItemSpaceSlice.cs` 上读到的 `Count` 都是请求帧自己的 `MoveCount`，
不是这个属性。`ItemArrivalRuleService` 读的是 `ItemPile.Count`，它的值由上面第 9 处喂进去。

逐处判断「此前是否依赖非堆时 `Count == Core.Value`」：

- 第 1、7 处：这正是缺陷所在。非堆现在 `Count == 1`，而请求的 `moveCount` 被
  第 6 处夹到 `[1, 1]`，于是 `moveCount == moving.Count` 成立，`Split` 对非堆走不到。
- 第 2、8 处：合堆那一支的前置是 `placement.StackLimit > 1`（`&&` 短路）。
  `PvfEntryLifecycleContentSource.WornPieceMetadata` 给走 equipment 索引解析出来的
  worn piece 的 limit 写死 `1`（`new(1, …)`），而 `ItemWorldClass` 里非 `Interchangeable` 的
  三个值（`Equipment`／`Avatar`／`Creature`）都由 `WornPieceMetadata` 产出。
  在这个解析下 `StackLimit > 1` 对非堆不成立，合堆分支落到 `MergeExceedsStackLimit`——
  改前（身份+1 > 1）与改后（1+1 > 1）落到同一个拒绝。
  这一支就是执行结果说「同模板时装未实测」的那一支；本评审也只做了代码阅读，未实测。
- 第 3、4 处：只在合堆分支内，同上。
- 第 5 处：`ConsumeAsync` 只被 `ItemUseEffectDomainService` 的 `ConsumeFromContainerEffect` 打到。
  非堆若走到这里，改前 `remaining = 身份 - count`（写回一个被减一的身份），
  改后 `remaining = 1 - 1 = 0`（清格）。改前那一读本身就在破坏身份。
  本评审没有实测这条路。
- 第 9 处 →`ItemArrivalRuleService.ForGrant`：`stackLimit = StackLimit > 0 ? StackLimit : int.MaxValue`
  对 worn piece 取 1，`stackLimit > 1` 不成立，那个遍历 pile 的循环走不到，
  于是第 9 处喂进去的非堆数值在 `ForGrant` 里读不到。
  `ForDailyRefill` 不看 `stackLimit`，会读到；但那要求某条每日补给规则点名一个 worn piece 模板。
  本评审没有实测这一路。
- `DailyRefillService` 的 `held.Sum(...)`：同一格若是 worn piece，改前累加的是身份，
  改后累加 1。同样要求补给规则点名 worn piece 模板。未实测。

**实测把这个面收紧到了很窄**：RV-2 把实现改回 `Count => Core.Value`，
Application.Tests 里只有本 Task 新增的三条时装判据变红，其余 1033 条在那次运行里是绿的——
也就是说，现有判据里没有一条在那次运行中因为「非堆时 `Count == Core.Value`」被拿掉而变红；
而 RV-1／RV-5 在交付态下七个项目零失败——现有判据里也没有一条因为改后的读法而变红。
两次运行合起来，是在这套判据跑到的范围内没有读到被这一改弄坏的东西。

**不足以确定的**：上面点名的三条路（`ConsumeAsync` 对非堆、`ForDailyRefill` 对 worn piece、
`DailyRefillService` 的求和对 worn piece）本评审没有实测，
这些运行也不足以确定它们在真实归档与真实规则下会不会被走到。
装备容器、货仓、地面拾取这些别的容器通路上的非堆移动同样没有单独实测。

### 二、`<summary>` 是本来就在，还是本轮新加——性质判定

`git show HEAD:Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs` 读到（HEAD = `565d33fe`）：

```
300:    /// <summary>How many the slot holds. For anything that does not stack, one.</summary>
301:    public int Count => Core.Value;
```

**这句 `<summary>` 在基线 `565d33fe` 里就已经在，本轮没有加它、也没有改它。**
本 Task 的 diff 只在它下面追加了 `<remarks>`，`<summary>` 那一行在 diff 里是上下文行而不是新增行。

于是这一改的性质是：**把实现改成它已经声明的契约所说的那样，不是改要求**。
按委托给的两分法，落在第一支。执行结果里「这是本仓 MEMORY 记的『注释漂移到反面』的又一例」
这句，就本文件这一处而言与 `git show HEAD:` 的读数一致。
**没有越出契约边界。** 两条阻断条件也没有被触到：
`Core.Value` 里装的仍然是时装的身份（diff 里没有一处改写它），
协议帧与帧的字节布局在本 Task 的 diff 里没有出现。

### 三、堆叠物回归面（重做 MU-B）

按契约的「至少自己重做其中一次」，重做了 MU-B：
`MergeAsync` 的 `target.Core with { Value = checked(target.Count + moveCount) }`
→ `target.Core with { Value = target.Count }`，
`ItemContainers.cs` 变异后散列 `d79acbc5…`（与执行结果记的逐字符相同）。
读到的红与执行结果记的逐条相同：

- Persistence：`SplitPileTests.TheTwoHalvesPourBackIntoOnePile`（失败 1／通过 319）
- Application：`MoveItemSpaceHandlerTests.Two_piles_of_one_item_merge`、
  `MoveItemSpaceHandlerTests.A_merge_within_the_stack_limit_still_happens`（失败 2／通过 1034）

另外 RV-3（`Count => 1`）在全量上读到 22 条红，分布在
`SplitPileTests` 3 条、`SqliteEntryInventoryMutationRepositoryTests` 3 条、
`MoveItemSpaceHandlerTests` 5 条、`UseStackableHandlerTests` 3 条、
`TutorialRewardPlacementTests` 3 条、`BagPlacementVerbTests` 3 条、
`PersistentContainerBoundaryTests` 1 条、`DraggedAvatarStaysWholeTests` 1 条。
这一读数说明：把非堆的答案推广到堆身上，会被现有判据打到；
而交付态在 RV-1／RV-5 下这 22 条是绿的。

### 四、缺陷是否真的修好

`DraggedAvatarStaysWholeTests` 走的是生产路径，本评审读代码核过夹具：
`InventorySide.BindMain` 注册的是 `PersistentItemContainer`（真 `SqliteItemDomainStore`
＋真 `ItemPlacementRuleService`，归档源由 `ContentArchive` 提供），
拖动调的是 `bag.MoveToAsync(...)`（进 `DecideMoveAsync`），
装载调的是 `CharacterInventoryEntryDomainService.LoadAsync` 打到 `InventoryEntryAssembly`——
`"Avatar detail {item.Core.Value} is unavailable."` 的抛点就在
`InventoryEntryAssembly.cs:400`。夹具里没有绕过这两处的替身。

RV-2 把实现改回去之后读到的三条红，与 D-06 记的链条对得上：

```
A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from
  → Assert.Null() Failure: Value is not null  （DraggedAvatarStaysWholeTests.cs:108，源格残留）
An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts
  → System.IO.InvalidDataException : Avatar detail 1114844642 is unavailable.
A_drag_of_one_onto_another_avatar_trades_the_two_slots
  → Expected: Moved { MoveCount = 1, ItemId = 101550507 }
    Actual:   Refused { Reason = PartialSwapNotAllowed }
```

交付态下这三条绿。在这条「发放 → 背包内拖 → 再装载」的路上，读到的是缺陷不再复现。

### 五、恒真复查

逐句读了四条判据的断言。三条时装判据比对的都是先独立读下来的值：
身份是拖动前从源格读下的 `Core.Value`（第一条 line 95→114；第三条两格交叉比对 line 213-218），
六列事实是拖动前从装载结果读下的 `AvatarDetail` 逐项比（line 156-161）。
堆那条比对的是 `(PileItemId, 1)` 对两格（line 272-273），
并且先向归档问了 `StackLimit >= 2` 与 `Interchangeable`（line 248-254），不是写死的数。

实测：RV-2 使三条时装判据红（红在上面抄的三句），RV-3 使
`A_drag_of_one_off_a_pile_still_divides_the_pile` 红。
按这两次运行，四条判据各自至少红过一次。执行结果给的反向变异散列 `1ca8c568…` 与本评审一致。

### 六、验收条件 3 的实测

自己核过：交付态下 `A_drag_of_one_onto_another_avatar_trades_the_two_slots` 绿，
断言的是两格身份交叉换位；MU-REV 下它红，实际读数是
`Refused { Reason = PartialSwapNotAllowed }`，期望是 `Moved { MoveCount = 1, ItemId = 101550507 }`。
**读到的是整件对调，不是被拒**，与执行结果记的原文逐字相同。
执行结果也如实写明了「目标格站着同一个模板的时装时会怎样，没有在判据里钉」，
并把它标成阅读而非实测——这一处记法与本评审的独立阅读一致（见上面第一点第 2、8 读点）。

### 七、计数与记法

RV-1／RV-5 两次全量各读到 2234 通过 / 2 跳过 / 0 失败、退出码 0，逐项目读数与执行结果的表相同。
Application.Tests 在 MU-REV 下总计仍是 1036（失败 3／通过 1033），
与「+4 全在 Application.Tests」的归因相符。
字节锚在评审开始与结束各测一次，散列 `1424a4a8…`。

记法逐句读过（判准是「这句在说个例还是说一类」，没有用词表搜）。
执行结果里把范围写死、把未覆盖另起段落明写的地方，与本评审的读数一致。
读到一处措辞比它的证据宽（见下面的非阻断观察），不构成阻断。

### 非阻断观察

- **NB-1（记法精确性）**：自查第 5 条里写「新判据里没有只断言『非零』『非空』而不与一个独立
  取得的值比对的时装断言」。`DraggedAvatarStaysWholeTests.cs:222-223` 的两句
  `Assert.NotNull((await OneItemAsync(side, …)).AvatarDetail)` 就是不与独立取得的值比对的非空断言。
  若把「断言」读成「判据（测试用例）」则这句成立，若读成「断言语句」则不成立。
  不阻断的理由：这句要支撑的是「不恒真」，而不恒真已由 RV-2／RV-3 两次变异各自打红实测掉了，
  它不承重。建议后续改成「这四条判据里没有哪一条只由非空断言承重」。
- **NB-2**：`ConsumeAsync`、`ForDailyRefill`、`DailyRefillService` 的求和这三处非堆读数在本次修复
  前后不同（见第一点），执行结果自己已把 `ConsumeAsync` 与 `PilesOf` 列为未专门实测。
  本评审补了一条阅读：`WornPieceMetadata` 把 worn piece 的 `StackLimit` 定为 1，
  在这个解析下 `ForGrant` 的 pile 循环与 `DecideMoveAsync` 的合堆分支对非堆走不到。
  这仍是阅读，留给完整评审或 TASK-06 重跑时决定要不要钉判据。
- **NB-3**：执行结果记反向变异 `Count => 1` 的读数是「失败 1，通过 3」，
  那是只跑新判据文件的读数；本评审跑全量在同一变异下读到 22 条红。
  两个读数不矛盾（跑的范围不同），但全量那个读数对「堆叠面被判据承住」的支撑更强。

### 需要的整改

无。NB-1 是建议，不作为整改要求。


## Controller Disposition D-08 — 接受 QR-08-1 的 `passed`

**判定**：接受。执行结果、自查、QR-08-1 的 `passed` 与被拥有的增量
（`IItemContainer.cs` 修改 ＋ `DraggedAvatarStaysWholeTests.cs` 新增）指向同一轮。TASK-08 置 `done`。

**性质判定已由 `git show HEAD:` 定案**：那句 `<summary>`
「How many the slot holds. **For anything that does not stack, one.**」
在基线 `565d33fe` 的该文件第 300 行**本来就在**，第 301 行才是 `Count => Core.Value`。
本任务 diff 里那一行是上下文行而非新增行。
**所以这是修实现去符合它自己已声明的契约，不是改要求，未越出契约边界。**
这是本成果第三次撞上「注释漂移到反面」。

**回归面已被两侧夹住**：`Count` 的读点 11 处逐个判过无破坏；
关键闸门是 `WornPieceMetadata` 把每个非堆来源的 `StackLimit` 写死为 1，
使合堆分支与发放的堆循环对非堆本就走不到。
另一侧：把 `Count` 改成恒等于 1，全量打红 **22 条、跨 8 个测试类**——
该属性被广泛承重，不是死角。

**三条未实测路径**（QR-08-1 已明写为「不足以确定」，转 TASK-06 记进清单）：
`ConsumeAsync` 对非堆、`ForDailyRefill` 对 worn piece、`DailyRefillService` 求和对 worn piece。

**QR-08-1 的三条非阻断观察**（NB-1 自查一句在某读法下被证伪但不承重；
NB-2 三条未实测路径的阅读补充；NB-3 `Count => 1` 读数的范围）
一并转 TASK-06 与下游完整变更评审，不在本任务再开一轮。
