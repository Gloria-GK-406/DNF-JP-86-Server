# TASK-07 — 仓库约定如实记下这一轮做到哪一步

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

让后来的读者能从仓库约定里读到本轮做成了什么、**没做**什么，以及槽号出处为何一格都补不上。

- Referenced spec requirements: RQ-009
- Referenced plan sections: plan.md 的 T-007（Steps 1–5，CK-019..CK-021）；Owned Boundaries 的 BD-007
- Owned boundary:

BD-007 `AGENTS.md`

- Permitted files or interfaces:

- `AGENTS.md`

- Dependencies: TASK-06
- Required dependency outputs: TASK-06 的变化清单与字节归因表
- Acceptance conditions:

1. 记下本轮做成了什么：时装的种类只有一个说法，发放通路能造出完整的时装，唯一性守卫对新入库的行开始生效。
2. 记下**没做**的部分：**时装仍然穿不上**，因为槽号一格都没有出处；量具是为此留的。
3. 记下槽号出处的穷尽结论：档案里时装名后面那个数**恒为零**所以不是槽号；实测里没有任何一帧显示角色穿着时装；上游转写只到带宽度与第 11 格，且**自陈不是量出来的**。
4. 记下装备类型对照今天认不出的全部类型名（**11 个时装名 ＋ `[artifact red]`／`[artifact blue]`／`[artifact green]`／`[charm]`／`[flag]`／`[name tag]`，共 17 个，覆盖 66,591 个模板**），以及其中在档案里从未被任何模板使用过的两个键（`[title]` 与 `[earring]`，全档案 118,479 个模板里 0 次）。每个数目都附带构成它的各项与计数口径。
5. 记下那条无出处的译名：底层判据说的名字、档案里另一个同样以该词开头的名字、以及两者件数的差别。**按仓规这是补记修正案，不是抹去原文。**

- Task-specific authorization:

Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做**一次**。
变异验证**一律用文件级备份 + `sha256sum` 还原并核散列**；**禁止任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash`）——本仓曾因此丢过未提交的工作。
不得触碰主检出与 `.worktrees/client-debug-lab`。

- Blocking conditions:

**如实报告义务**：不得把本轮写成比实际更完整。凡是没做到的，逐条写下来。

- Required methods:

- `test-driven-development`：先让判据红，再让它绿。
- 覆盖主张必须由**变异实测**支撑，**不得只凭 grep 断言**——本仓已两次证明按消息文本或标识符搜索会给出错误的覆盖读数。
- 记法：**不写读者无法证伪的基数**。给出数目时写「N 处，分别是：…」并说明计数口径；按成员名索引，**不按行号**。
- 遇到未预期的失败时用 `systematic-debugging`，先取得证据再改。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-019 | Non-command：逐句核对新写下的每一条记载 | 每一句都能被指回一处实测或一处代码；**没有一句比代码说得更强** |
| CK-020 | Non-command：检查记载里出现的每一个数目 | 每个都同时给出构成它的各项与计数口径，读者可自行复算 |
| CK-021 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败，退出码 0 |

- Required evidence:

- 三条检查的实际输出。
- 新写下段落的全文。
- 每个数目的复算过程。

- Constraints:

- 分层：Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host
- `TreatWarningsAsErrors` 开启，**CS9113（未读的主构造器参数）是错误**
- 不新增协议帧，不改任何帧的字节布局
- `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 必须继续通过，判定谓词与具名豁免清单**不得放宽或扩充**
- 领域层 `store` 只指事务结算的真源
- 既有断言的期望值**不得为迁就实现而改写**；唯一例外是正对应 `spec.md` 列出的某条行为变化，且须逐条归因
- 不裁定契约兽槽位 24 还是 25；**不给 `PvfEquipmentSlots.ByName` 补任何键**
- 注释是参考材料不是铁律，判断以代码设计为准；但**改已被证伪的记载要补记修正案**，改「要求」必须停下来问


## Contract Amendment A-07 — 纳入 TASK-01 快审移交的 F-2

由控制器在处置 TASK-01 的快审结果（R-01）时记录。approved Spec 的边界未变
（RQ-009 要求仓库约定如实记载，本条正属其中）。

**新增的验收条件第 6 条（F-2）**：`AGENTS.md` 里用**行号**索引那四处谓词的记载，
**在本轮基线上就已经是错的**——它记 438/568，实测为 453/595。

处置要求：
1. 订正它，并**按仓规说明原文说的是什么、为什么不再成立**，不是默默改数字。
2. 改成**按成员名索引**，因为行号会随任何改动漂移——本条正是本仓
   「不按行号索引」那条规矩自己被违反的实例，订正时把这一点写下来，
   使它成为该规矩的一个有出处的例证而不是又一处会再漂的数字。
3. 这是**基线就有**的缺陷，不是本轮引入的；记载里要说清这一点，
   不得写成「本轮修好了自己弄坏的东西」。


## Contract Amendment A-07b — 给所有者的实机须知，必须写进记载

由控制器在处置 QR-05-A 时记录。仍属 RQ-009。

**新增的验收条件第 7 条**：仓库约定里那段「量具怎么用」必须包含下面两条，
**用所有者读得懂的话写，不要只写代码事实**：

1. **看到 `item 0` 不是缺陷。** 若客户端把时装拖拽的目标列写成 `Avatar`（=1）
   而非 `Equipment`（=3），请求会在更早的一处被拒，记录读作
   `item 0 …->N refused as ListNotServed`——**槽号仍在，模板号丢失**。
   **槽号才是要量的东西**，一次拖一件时模板号由操作上下文可知。
   出处 QR-05-A；**证据类型是代码路径读出，客户端实际写哪个列号尚未观测**。
2. **取下那条路径不会说出模板号**（`TakeOffAsync` 的三个拒绝返回全走单参 `Refused`，
   `{ItemId}` 为 0）。量映射靠的是**拖上去**那一次。


## Contract Amendment A-07c — 本成果的影响面超出了时装，记载必须说出这一点

由控制器在处置 QR-06-2 时记录。仍属 RQ-009。

**新增的验收条件第 8 条**：仓库约定必须写下**本成果改变了普通装备的拖动行为**，
用后来者与所有者都读得懂的话，并标明证据类型。

事实链（出处：`behavior-change-list.md` 的 RV-23／RP-08，
经 QR-06-1 发现、QR-06-2 独立复核）：

1. `CharacterRosterService.EquipmentCore` 给起始装备的 `Value` 位写
   `Random.Shared.Next(1, int.MaxValue)`；
   `GroundPickupSlice.CarryOverAsync` 在一件装备落进背包后把该格重写成
   `Value = found.Instance`。
2. **在基线上** `ContainerSlotItem.Count => Core.Value`，于是这样一件普通装备
   在背包内被拖动时，`MoveCount=1` 与那个数不等 → 目标格空走 `Split`、
   有东西返回 `PartialSwapNotAllowed`。
3. 本成果为修时装而做的 `Count => this.IsPile ? Core.Value : 1`
   **顺带把普通装备的这条毛病一起消掉了**。

**记载时要说清三件事**：
- 这是**变好**：此前拖动一件捡来的装备会被拆开或被拒，现在整件移动／对调。
- 它**不在本 Spec 原本列出的行为变化里**，是同一处修复强制带出的；
  要保留旧行为反而得给时装开特例，那会让普通装备继续坏着。
- **证据类型：代码级读出，未实测**，也未实机。
  所有者实机测装备时若看到与线上老服不同的拖动行为，**不是新引入的缺陷**。

**并且要如实写下：五份记载都没发现这一条。**
清单第一版、QR-06-1、TASK-08 的执行结果、QR-08-1、控制器处置 D-08——
全都把它当成时装专属的问题。它是重跑反向扫时**从代码里扫出来的**，
不是从任何一份记载里读出来的。**这一条本身就是「反向扫为什么必须存在」的例证。**


## 执行结果（TASK-07，第一轮执行）

**边界**：只改了 `AGENTS.md` 一个文件。收尾 `git status --porcelain` 读到 **19 行**
（12 个 ` M` ＋ 6 个 `??` ＋ 新增的 ` M AGENTS.md`），比 TASK-06 收尾时记的 18 行多出的正是 `AGENTS.md` 本身。
生产代码与判据一行未动（下面 CK-021 四次读数相同即其旁证）。
**未建 Git 检查点，未提交。**

交付态散列：`AGENTS.md` `5b68938e201ac6d04aff4a65455d1a430bf6058e7226becbb9460a05d9c2d839`
（开工前是 `cff1cd23f62eba83a47a9c5107b155c5344589214d9869ad0dcaae4a4be53411`，
备份留在会话临时目录，本任务未使用任何 git 还原类命令）。
`git diff --numstat -- AGENTS.md` ＝ `230 3`，文件由 824 行变 1051 行。

### 一、改了哪几段，逐段前后对照

**改动共两处：一处原地订正（3 行 → 10 行），一处新增两个 `####` 小节（追加 220 行）。**

#### 改动 1（原地订正，F-2）：世界表小节里那句「4 处 SQL」的索引方式

位置：`### 物品：有身份的东西，和它在哪，是两张表` 里「同一个时装 UID 全库至多一行」那一段的末尾。

**改前（逐字，原文 3 行）：**

```
不再读一份列副本。**该谓词文本在 `Server/src` 今天出现在 4 处 SQL 里，分别是**
`InventoryItemTables.cs:229`、`:264`（这两处是常量的两个使用点）、
`SqliteItemDomainStore.cs:414`（`CarriedAvatarsAsync` 的检索键）、`:544`（`FillAsync` 的守卫）。
```

**改后（逐字，10 行）：**

```
不再读一份列副本。**该谓词文本在 `Server/src` 今天出现在 4 处 SQL 里，按成员名分别是**
`SqliteCharacterItemTable.UpdateOwnAsync` 与 `SqliteCharacterItemTable.InsertOwnAsync`
（同文件 `InventoryItemTables.cs`，这两处是常量 `SqliteCharacterItemTable.CarriesTheAvatar` 的两个使用点）、
`SqliteItemDomainStoreSession.CarriedAvatarsAsync` 的检索键（写作 `AND substr(item_core, 6, 4) = @uid`）、
`SqliteItemDomainStoreSession.FillAsync` 的守卫
（写作 `AND substr(standing.item_core, 6, 4) = substr(@core, 6, 4)`），后两处在 `SqliteItemDomainStore.cs`。
计数口径：在 `Server/src` 下按字符串 `item_core, 6, 4` 检索，命中 3 行；其中
`InventoryItemTables.cs` 那一行是常量声明，被上面点名的两个成员各引用一次，故算 4 处 SQL。
**这一句原先用行号索引，那是错的**；订正与来龙去脉见下面「这一轮补记的四条修正案」其一。
```

**原文说的是什么、为什么不再成立**（写在新增的「修正案 其一」里，此处摘要）：
原文用四个行号索引那四处谓词，写作 `:229`、`:264`、`:414`、`:544`。
前两个至今仍对；后两个**在本轮基线 `565d33fe` 上就已经指错了行**。本任务亲自复测：

| 时点 | `CarriedAvatarsAsync` 的检索键 | `FillAsync` 的守卫 | 测法 |
|---|---:|---:|---|
| `AGENTS.md` 原文所记 | 414 | 544 | —— |
| 基线 `565d33fe` 实测 | **438** | **568** | `git show 565d33fe:Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs \| grep -n` 两个谓词串，各命中一行 |
| TASK-01 落地后（快审 R-01 所记） | 453 | 595 | 出处：`tasks/TASK-01-…md` 的「F-2」一节 |
| 本任务交付态实测 | **468** | **642** | `grep -rn 'item_core, 6, 4' Server/src/` |

即：这对数在本轮之内又漂了两次，加上基线本来就错，**同一对数字在一轮里对不上三回**。
已改成按成员名索引，并在新小节里写明「这是基线就有的缺陷，不是本轮弄坏又修好的」。
成员名由本任务核对过：`awk` 扫 `SqliteItemDomainStore.cs` 的方法声明，468 落在
`private async ValueTask<IReadOnlyDictionary<int, object?[]>> CarriedAvatarsAsync(`（第 448 行）之后、
下一个方法声明之前；642 落在 `private async ValueTask<int> FillAsync(`（第 620 行）之后。
`InventoryItemTables.cs` 的 229 与 264 分别落在 `UpdateOwnAsync` 与 `InsertOwnAsync` 内，逐段读过。

#### 改动 2（新增）：两个 `####` 小节，插在「三条已被证伪的记载，订正」之后、「判据偶发失败」之前

**改前**：该位置只有一个空行，`#### 三条已被证伪的记载，订正` 直接接 `### 判据偶发失败：先取身份，再谈别的`。
**没有删改任何既有小节**——`git diff` 的 `-` 行只有改动 1 的那 3 行。

**改后**新增：

- `#### 「服务端同意时装是什么」这一轮：做到哪一步`（约 170 行）
- `#### 这一轮补记的四条修正案`（约 50 行）

第一个小节的骨架，逐段：

1. 抬头 ＋ **读法提醒**：本文件上面几节里的「本轮」指上一个成果 `items-own-their-identity`，
   本节起的「本轮／这一轮」指 `server-agrees-what-an-avatar-is`。
   （加这一段是因为本文件此前每一节都说「本轮」，不加会让两个成果混在一起。）
2. **做成了三件事**：种类只剩一个说法／发放通路造得出完整时装（身份 ＋ 六列事实，六项逐项列出）／
   唯一性守卫对新入库的行开始生效。每条附一个判据名与一次变异编号。
3. **没做的那一件：时装仍然穿不上**——`ByName` 的 16 个键里以 `avatar]` 结尾的是 0 个，
   本轮没给这张表补键；留下的是量具（拒绝记录里的 `item {ItemId} {From}->{To}`）。
4. **槽号出处：三处，逐处写结论**（档案里那个数恒为 0／`protocol/` 没量到过／上游转写自陈不是量出来的）。
5. **认不出的 17 个类型名**：十七行表 ＋ 计数口径 ＋ 测法 ＋ 复算 ＋ `[title]`／`[earring]` 零命中。
6. **给所有者的实机须知两条**（`item 0` 不是缺陷／取下那条路不说模板号）。
7. **影响面超出时装**：普通装备的拖动行为也变了，三步事实链 ＋ 要说清的三件事 ＋ 五份记载都没发现它。

第二个小节四条修正案：其一 F-2 行号索引；其二时装 8／1 不一致已消失；
其三潜伏的存盘矛盾随根因消失；其四 `aurora-illusion avatar` 这条无出处的译名。

**修正案其四的前后对照**（这是「补记，不是抹去」的那一条）：

- **不改的原文**（在 `Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs` 的类型级 `<remarks>` 里，
  本任务**没有碰这个文件**）：「A21 inserted the aurora-illusion avatar ahead of the weapon, which is why
  these are not the numbers an older client used…」
- **新写下的修正案**（在 `AGENTS.md` 里），核心是一张三行表：

  | 说法 | 出处 | 本轮实测 |
  |---|---|---|
  | `AuraSkinAvatar` | `protocol/game/server-to-client/NOTI-0x0002.md`（「A21 在武器之前插了 `AuraSkinAvatar`（槽位 11）」）与 `protocol/patterns/data/traces/a21-starting-gear-and-skills-20260826.md`（「0..11 是时装槽（A21 在武器前插了 `AuraSkinAvatar`）」） | 底层记载说的是这个名字 |
  | `aura skin avatar` | `ClientHack/tools/literals-a21.csv` 第 27941 行的客户端字面量 | 档案里以它作 `[equipment type]` 的模板 **0 个** |
  | `[aurora avatar]` | 档案 `equipment/*.equ` 的 `[equipment type]` | **423 个模板**，后面那个数取值集合 `{0}` |

  结论句：三个是三样东西，`aurora-illusion avatar` 哪一个都不是；按仓规不改注释正文，本条即其修正案。

### 二、三条检查的实际结果

#### CK-021（命令）

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

本任务跑了**四次**：改动前一次（取基线读数）、插入新小节后一次、逐句自查改写后一次、末次收尾一次。四次逐个程序集读数相同：

| 程序集 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| `Dfo.Server.Protocol.Tests` | 524 | 0 | 0 |
| `Dfo.Server.Persistence.Tests` | 320 | 0 | 0 |
| `Dfo.Server.Application.Tests` | 1036 | 0 | 0 |
| `Dfo.Server.Infrastructure.Tests` | 236 | 2 | 0 |
| `Dfo.Pvf.Tests` | 60 | 0 | 0 |
| `Dfo.Server.Networking.Tests` | 52 | 0 | 0 |
| `Dfo.Server.Host.Tests` | 6 | 0 | 0 |
| **合计** | **2234** | **2** | **0** |

复算：524 ＋ 320 ＋ 1036 ＋ 236 ＋ 60 ＋ 52 ＋ 6 ＝ 2234。退出码四次均为 **0**。
与契约写下的基线（2234 / 2 / 0，其中 Protocol 524、Persistence 320、Application 1036）逐项相同——
**改文档没有改变它，也证明本任务没有碰到别的东西。**

#### CK-019（逐句核对：每一句都能指回一处实测或一处代码）

新写下的段落逐句过了一遍，按下面四类归档，**没有一句比代码说得更强**：

- **观测（判据名 ＋ 变异编号）**：三条「做成了什么」各一条，量具那段一条。
  引用的判据名逐个在工作区文件里核对过存在：`GrantedAvatarIsWholeTests` 三条、
  `TwoAvatarsDoNotShareOneIdentityTests` 三条、`RefusedEquipIsRecordedWithItsTemplateTests` 三条。
- **代码级读出（点名成员 ＋ 语句文本）**：`ByName` 的 16 个键、`PutOnAsync` 的两个拒绝返回、
  `HandleAsync` 的 `ListNotServed` 那一句、`TakeOffAsync` 的三个单参返回、
  `ContainerSlotItem.Count`／`IsPile`、`ArrivingAvatar.ItemKind`／`NewIdentity`／`Facts`、
  `CharacterRosterService.EquipmentCore`、`GroundPickupSlice.CarryOverAsync`。
  这一批本任务逐处打开原文核对过，写下的语句文本与文件逐字符相符。
- **档案实测（本任务亲自跑的一次性程序）**：118,479／66,328／66,596／268／423／1,796／26／0 这几个数。
- **明写未确立**：客户端实际写哪个列号未观测；`protocol/` 的 1,819 个文件未逐一穷尽；
  那 5 行差额的来历未查明；11 个时装名里哪一个坐在槽 11 未确立；
  普通装备那条链未实测也未实机；创建角色那张表里出现哪些名字未测。

**记法口径自查（不用词表，逐句问「这句在说个例还是说一类」）。** 三处按此改过：

1. 原写「`protocol/` 里没有量到过一件穿在身上的时装」——是在说一类，且我并未穷尽该目录。
   改为「在 `protocol/` 下按『时装』检索命中 19 个文件、逐个读过，其中四条记载明写没量到」，并附检索口径。
2. 原写「唯一一份写出角色身上有哪几格的实机跟踪是……」——「唯一」是全称。
   改为「那 19 个文件里，写出角色身上有哪几格的是……」。
3. 原写「此前拖一件捡来的装备**会**被拆开或被拒」与「客户端写的 `MoveCount` **是** 1」——
   前者在说一类，后者把单次样本说成常态。改为「同一次拖动此前落在哪一支、现在落在哪一支」
   与「`MoveItemSpaceSlice` 的 `<remarks>` 抄下的那次实机请求在这四个字节上是 `01 00 00 00`」。
4. 另把「补进去的每个数都会是编造的」改成「本轮一个槽号也没量出来，补键就等于写下一个没有出处的数」。

**唯一保留的全称语气**在两处引文里（「本仓的角色从来没有穿戴任何东西」「`equipCount` 恒为 0」），
它们是**逐字引用**的持久记载原文，出处已写明，不是本节自己的断言。
引用逐字核对过：两句分别在 `protocol/game/server-to-client/NOTI-0x0002.md` 与
`protocol/game/server-to-client/NOTI-0x000D.md`，出处与文字逐字符相符。

#### CK-020（每个数目都附构成它的各项与计数口径）

见下面第三节，逐个数给了口径与复算。

### 三、每个数目的复算过程

**测法（档案侧的数共用这一套）**：把 `Server/src/Dfo.Pvf` 的 11 个源文件原样复制到工作区**之外**
（会话临时目录），写一个只调 `PvfArchive.Open`／`FindFile`／`OpenScript`／`GetTag`／`ResolveString`
的一次性程序跑一遍。取标签与取值那两步与 `PvfEquipmentSlotDefinitionSource.Read` 逐句相同
（同一个 `[equipment type]`、同一组 `TryMovePast` ＋ `TryReadSectionValue`）。
`equipment/equipment.lst` 的读法与 `PvfListFile.Read` 相同（整数与字符串交替，取首次出现）。
**工作区里的文件一个都没有被改动**，也没有向仓库里加任何文件。

| 数 | 构成 | 口径与复算 |
|---:|---|---|
| **118,479** | `equipment/equipment.lst` 的行数 | 实测：行 118,479、互不相同的 id 118,479、互不相同的相对路径 118,479，id ≤ 0 的 0 个。这三个数相等，说明「按 id 去重」与「按路径去重」都不改变这个数 |
| **0** | 打不开的文件数／缺 `[equipment type]` 的模板数／类型名后面不跟 `Int32` 的模板数 | 三个数分别由程序单独计，都是 0；于是「读到类型名的模板」也是 118,479 |
| **11** | 以 `avatar]` 结尾的类型名个数 | 名字先 `Replace("\`","")`、`Trim()`、`ToLowerInvariant()` 再判后缀（与 `PvfEntryLifecycleContentSource.ClassifyEquipmentType` 同一套归一化）。11 个名字见 `AGENTS.md` 表的前 11 行 |
| **66,328** | 8,391 ＋ 8,325 ＋ 8,194 ＋ 8,070 ＋ 7,929 ＋ 7,565 ＋ 7,028 ＋ 7,000 ＋ 2,954 ＋ 449 ＋ 423 | 逐项累加：16,716 → 24,910 → 32,980 → 40,909 → 48,474 → 55,502 → 62,502 → 65,456 → 65,905 → **66,328** |
| **268** | 65 ＋ 50 ＋ 47 ＋ 47 ＋ 31 ＋ 28 | ＝ 268 |
| **66,596** | 66,328 ＋ 268 | ＝ 66,596 |
| **17** | 11 ＋ 6 | `ByName` 按 `OrdinalIgnoreCase` 查不到的**不同名字**数 |
| **16** | `PvfEquipmentSlots.ByName` 的键数 | 逐键数：weapon／title／title name／coat／shoulder／pants／shoes／waist／amulet／wrist／ring／support／magic stone／earring／support weapon／creature ＝ 16 |
| **0** | 16 个键里以 `avatar]` 结尾的 | 逐键取末尾七个字符比对 |
| **0** ×2 | `[title]` 与 `[earring]` 被用到的模板数 | 118,479 个模板读到的类型名里，按 `OrdinalIgnoreCase` 与这两个键相等的各 0 个 |
| **1,796** / **26** | `[title name]` / `[support weapon]` 的模板数 | 同一次扫描的读数，写在这里是为了说明「`[title]` 零命中」不等于「档案里没有称号」 |
| **423** / **0** | `[aurora avatar]` / `aura skin avatar` 的模板数 | 同一次扫描；后者按四种写法各探一次（`[aura skin avatar]`、`aura skin avatar`、`aurora avatar`、`[aurora avatar]`），只有 `[aurora avatar]` 有命中 |
| **{0}** | 11 个时装名后面那个数的取值集合 | 每个名字下把跟随的 `Int32` 收进一个集合；11 个集合都只有一个元素 0。反向核对：后面那个数 ≠ 0 的时装模板 **0 个**。对照组：`[weapon]` 的集合有 10 个元素、`[coat]` 10 个、`[ring]` 7 个 |
| **19** | `protocol/` 下含「时装」二字的文件数 | `grep -rln "时装" protocol/ \| wc -l` |
| **1,819** | `protocol/` 下的文件总数 | `find protocol -type f \| wc -l` |
| **4** | 那段谓词文本出现的 SQL 处数 | `grep -rn 'item_core, 6, 4' Server/src/` 命中 3 行；其中 `InventoryItemTables.cs:46` 是常量声明，被 `UpdateOwnAsync`（:229）与 `InsertOwnAsync`（:264）各引用一次，故 2 ＋ 2 ＝ 4 处 SQL |
| **438／568／453／595/468／642** | F-2 那对行号在四个时点的读数 | 见第一节的四行表，每行给了测法 |
| **2234 / 2 / 0** | CK-021 的三个数 | 见第二节的七行表，逐程序集相加 |
| **3** | `PvfEquipmentSlots.cs` 里 `avatar` 的出现处 | `grep -in avatar` 命中第 7、8、55 行，三行都以 `///` 开头。**这一条本任务只是复核了 `AGENTS.md` 既有的说法，没有改写它** |

**一个不吻合，如实记下**：契约验收条件第 4 条与本任务收到的派工把这两个数写作
**66,323** 与 **66,591**，而本任务按上面的口径测了两遍，两遍都是 **66,328** 与 **66,596**，各多 5。
本任务另外核对了三件事以缩小可能性，**都没有解释这 5**：
（a）`equipment.lst` 里没有重复 id、没有重复路径，所以不是去重口径的差别；
（b）时装模板里没有打不开的文件、没有缺标签的文件，所以不是「跳过了 5 个」；
（c）`AGENTS.md` 上一个成果早已写下的「**66328** 个时装模板」与本次重测**相同**。
三个数（66,323 / 66,328 与 66,591 / 66,596）已一并写进 `AGENTS.md`，并注明差额来历未查明，
让读者能自己复算并自行判断。**没有把契约的数照抄成本任务的观测，也没有把本任务的数说成契约错了。**

### 四、自查：逐条对照验收条件与三份修正案

| 条 | 要求 | 凭什么算达成 |
|---|---|---|
| 1 | 记下做成了什么 | 新小节「做成了三件事」，三条分别点名 `WornPieceMetadata`／`ArrivingAvatar` ＋ `LandAsync`／`FillAsync` 的守卫，各附一个判据名与一次变异编号；六项事实逐项列出并与世界表小节的「时装 6 列」对齐 |
| 2 | 记下**没做**的：时装仍穿不上，槽号无出处，量具是为此留的 | 新小节「没做的那一件」整段；点名 `ByName` 零个 avatar 键 → `GetAsync` 答 null → `PutOnAsync` 拒绝这条链，并写明「本轮没有给这张表补任何键」及其旁证（该文件不在 `git status` 里）；量具那段写出拒绝记录的模板串与两端的含义 |
| 3 | 槽号出处的穷尽结论（三处） | 新小节「槽号出处：本轮找过三处」：档案那个数取值集合 `{0}`（附口径与对照组）；`protocol/` 四条记载逐字引用 ＋ 检索口径 ＋ **明写未穷尽 1,819 个文件**；上游转写只给了十二格边界与槽 11 的名字，且引用它自陈「不是量出来的」 |
| 4 | 认不出的 17 个类型名、覆盖 66,591、`[title]`／`[earring]` 在 118,479 里零命中；每个数附构成项与口径 | 十七行表逐名给数；计数口径与测法各一段；复算 11 ＋ 6 ＝ 17、66,328 ＋ 268 ＝ 66,596。**与契约的 66,591 差 5，已如实并列写下并说明未查明**（见第三节末） |
| 5 | 那条无出处的译名，按仓规补记修正案 | 「修正案 其四」的三行表 ＋ 结论句；**未改 `PvfEquipmentSlots.cs`**（该文件不在本任务的改动里） |
| A-07 / F-2 | 订正行号索引、说明原文说的是什么、改成成员名索引、写下这是本仓规矩自己被违反的实例、说清是基线就有的缺陷 | 改动 1（原地）＋「修正案 其一」（四行时点表 ＋ 「同一对数在一轮之内漂了三次」＋「这正是本仓『按成员名索引、不按行号』那条规矩自己被违反的实例」＋「这是基线就有的缺陷，不是本轮弄坏又修好的」）。**没有写成「本轮修好了自己弄坏的东西」** |
| A-07b | 实机须知两条，用所有者读得懂的话，标证据类型 | 「给所有者的实机须知，两条」：`item 0` 那条写清列号 1／3、被拒的位置、记录长什么样、槽号仍在模板号丢失、**证据类型是代码路径读出且客户端写哪个列号尚未观测**；取下那条列出三个单参返回并点名「要量的那次是拖上去」 |
| A-07c | 影响面超出时装：三步事实链、三件要说清的事、五份记载都没发现 | 「这一轮的影响面超出了时装」整段：起始装备与地面拾取两句写身份、基线 `Count => Core.Value` 的两个后果、本轮改法；三件事逐条（变好／不在原清单里／证据类型代码级读出未实测未实机 ＋ 「不是新引入的缺陷」）；末尾点名那五份记载并写明它是重跑反向扫时从代码里扫出来的 |
| 记法口径 | 不写全称量词，只写观测与明写未确立；不按词表自查 | 逐句问过「这句说个例还是说一类」，四处改写记在第二节 CK-019 里；保留的两处全称语气是逐字引用，出处已写明并核对逐字符相符 |
| 硬约束 | 只改 `AGENTS.md`；不建检查点不提交；不写读者无法证伪的基数 | `git status` 只多出 ` M AGENTS.md`；未执行任何 git 写命令；每个数都给了口径，档案侧的数另给了可重跑的测法 |

**本任务没有做到的、留给后手的一件事**：66,323／66,328 那 5 行的差额来历未查明。
它不影响任何结论的方向（两个口径都指向「时装名后面那个数恒为 0，槽号补不上」），
但它是一处**两份记载对不上的数**，已在 `AGENTS.md` 与本节各写一遍，不藏起来。


## Independent Quick Review QR-07-1（TASK-07 第一轮执行）

- **判定：`passed`**
- 评审的契约版本：Contract revision 1 ＋ 修正案 **A-07／A-07b／A-07c**（三份全部纳入核对）
- 被评审的 owned diff：**只有 `AGENTS.md`**。评审时自测：`git status --porcelain` **19 行**、
  `git diff --numstat -- AGENTS.md` ＝ **`230 3`**、`sha256sum AGENTS.md` ＝
  `5b68938e201ac6d04aff4a65455d1a430bf6058e7226becbb9460a05d9c2d839`、
  基线 `git show 565d33fe:AGENTS.md | sha256sum` ＝
  `cff1cd23f62eba83a47a9c5107b155c5344589214d9869ad0dcaae4a4be53411`、行数 824 → **1051**。
  与执行结果所记逐字符相同。`git diff` 的 `-` 行**只有 3 行**（改动 1 那 3 行），
  既有小节一节未删未改；新增两节落在 661 与 903 两个既有标题之间（`#### … 682`、`#### … 857`）。
  **生产代码与判据一行未动**（其余 12 个 ` M` 与 6 个 `??` 的 numstat 与 TASK-06 收尾态一致）。
- 评审期间**未修改工作区任何文件**；临时探针建在会话临时目录，用完已 `rm -rf`，
  上面的 19 行/`230 3`/散列即是删除之后测的。**未执行任何 git 写命令或还原类命令。**

### 一、CK-021 重跑（评审方独立执行）

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

| 程序集 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| `Dfo.Server.Protocol.Tests` | 524 | 0 | 0 |
| `Dfo.Server.Persistence.Tests` | 320 | 0 | 0 |
| `Dfo.Server.Application.Tests` | 1036 | 0 | 0 |
| `Dfo.Server.Infrastructure.Tests` | 236 | 2 | 0 |
| `Dfo.Pvf.Tests` | 60 | 0 | 0 |
| `Dfo.Server.Networking.Tests` | 52 | 0 | 0 |
| `Dfo.Server.Host.Tests` | 6 | 0 | 0 |
| **合计** | **2234** | **2** | **0** |

`EXIT=0`。524＋320＋1036＋236＋60＋52＋6 ＝ 2234。**与执行结果所记逐程序集相同**，无偶发。
跳过的两条是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。

### 二、CK-020：评审方自写探针重测档案侧的数

探针建在会话临时目录（把 `Server/src/Dfo.Pvf` 的 11 个 `.cs` 复制出去，自写 `Program.cs`），
读法照 `PvfEquipmentSlotDefinitionSource.Read`：`PvfListFile.Read(archive, "equipment/equipment.lst")`
→ `TryAdd` 建「id → 相对路径」→ `archive.FindFile("equipment/" + 路径)` → `archive.GetTag("[equipment type]")`
→ `TryMovePast` → `TryReadSectionValue` → `ResolveString`。**探针已删除**（见上）。

**读数与执行者所记逐个相同**，其中被点名要抽查的三处：

| 数 | 评审方实测 | 与记载 |
|---|---:|---|
| `equipment.lst` 行数／互不相同 id／互不相同路径 | **118479 / 118479 / 118479** | 相同；`id ≤ 0` 的 0 个 |
| 打不开的文件／缺 `[equipment type]`／类型名不是字符串 | **0 / 0 / 0**，读到类型名 **118479** | 相同 |
| `ByName` 认不出的**不同名字** | **17** | 相同（11 个 `avatar]` ＋ `[artifact red]`／`[charm]`／`[artifact blue]`／`[flag]`／`[artifact green]`／`[name tag]`） |
| 17 个名字覆盖的模板 | **66596**（时装 **66328** ＋ 其他 **268**） | **与记载相同，与契约第 4 条的 66,591／66,323 各差 5** |
| 11 个时装名逐个 | 8391／8325／8194／8070／7929／7565／7028／7000／2954／449／423 | 与 `AGENTS.md` 十七行表逐行相同 |
| 11 个时装名后面那个数的取值集合 | 每个都是 **`{0}`**，后面不跟 `Int32` 的 **0 个** | 相同 |
| 对照组 `[weapon]`／`[coat]`／`[ring]` 的取值集合大小 | **10 / 10 / 7** | 相同 |
| `[title]`／`[earring]` 命中 | **0 / 0** | 相同 |
| `[title name]`／`[support weapon]`／`[aurora avatar]` | **1796 / 26 / 423** | 相同 |
| `aura skin avatar` 四种写法作 `[equipment type]` | **0** | 相同 |
| `ByName` 键数／其中以 `avatar]` 结尾 | **16 / 0** | 相同 |

仓库侧的数亦逐个复算：`find protocol -type f | wc -l` ＝ **1819**；
`grep -rln "时装" protocol/ | wc -l` ＝ **19**；
`ClientHack/tools/literals-a21.csv` 第 **27941** 行读作 `0x0293A180,0x0362F62C,aura skin avatar`。

### 三、那 5 的差额——评审方独立裁定：**数是 66,596（时装 66,328），差额来历已查明**

**评审方自测的答案是 66,596 与 66,328**，与执行者两遍重测一致，与本文件
「已知缺陷与覆盖缺口」里早已记着的「66328 个时装模板」一致，
与上一个成果 `items-own-their-identity` 的三处独立记载（`behavior-change-list.md:269`、
`change-review.md:433`、`completion-evidence.md:135` 的 `Avatar=66328 Creature=707 Equipment=51444 sum=118479`）一致。
**契约第 4 条的 66,591／66,323 是错的。**

**差额来历（执行者记作「未查明」，评审方查明了）**：它不是一处口径差别，
更不是 5 个真实存在的模板行，而是**一次加法错误**，源头是
`.rks-agent/tasks/server-agrees-what-an-avatar-is/findings-archive-measurement.md`：

- 该文件 **Q2 一节的探针输出原文**（第 113–124 行）逐名列的 11 个数是
  423／7565／8391／7028／7929／8325／8194／8070／2954／7000／449
  ——**与今天重测逐个相同**；
- 这 11 个数相加 ＝ **66328**；
- 但同文件第 **138** 行的结论句写「这 11 个名字覆盖 **66,323** 个模板」，
  **与它自己上面那张表对不上**；
- 这个错值再被传播到同文件第 **188** 行（`66,591 ＝ 66,323 ＋ 268`）与第 **284** 行，
  再传到 `TaskContract.md:65` 与 `:91`，再传到本 Task 契约的验收条件第 4 条。

计数口径：非时装六项 65＋47＋31＋50＋47＋28 ＝ **268**，两版一致；
差额 5 **全部落在时装小计这一项**（66,591−66,323 ＝ 268 ＝ 66,596−66,328），
与「一处小计写错、其余照抄」这一解释吻合，也排除了「少数了 5 个模板」。

**这不构成阻断项。** 执行者没有照抄契约的数，也没有断言契约错了，而是三数并列、
标明未查明并给出可重跑的测法——按本仓记法这是正确处置。
唯一建议（**非阻断**）：`AGENTS.md` 里「那 5 行的来历本轮**未查明**」一句可在后续一轮
改成「来历已查明：`findings-archive-measurement.md` 结论句与其自身逐名表的一次加法错误，
并沿契约传播」，并把「5 **行**」改成「5 的差额」——现说法容易让读者以为档案里另有 5 行模板。

### 四、CK-019：逐句核（评审方自行开原文核对，未用词表搜）

新写下的两节逐句过了一遍，**没有找到可证伪且为假的句子，也没有找到比代码说得更强的句子**。
逐处开原文核对过的代码级断言（全部属实、成员名与语句文本相符）：

- `PvfEquipmentSlots.ByName` **16 个键**，以 `avatar]` 结尾 **0 个**；该文件**不在 `git status` 里**，
  类型级 `<remarks>` 的 `aurora-illusion avatar` 原文**仍在**。
- `PvfEquipmentSlotDefinitionSource.Read` → `ByName.TryGetValue` 查不到答 `null`；
  `MoveItemSpaceCommandHandler.PutOnAsync:339` ＝ `Refused(ItemMoveStatus.NotWearable, piece.ItemId)`。
- `MoveItemSpaceHandler.ResolveAsync`（`MoveItemSpaceSlice.cs:469`）的日志模板
  `"… item {ItemId} {From}->{To} refused as {Status}"`（:518–520）逐字符相符。
- `InventoryContainerType`：`Main = 0`、**`Avatar = 1`**、**`Equipment = 3`** 属实。
- `MoveItemSpaceCommandHandler.HandleAsync:279` 的
  `if (command.SourceListType != MainListType || command.DestinationListType != MainListType)`
  → `ItemMoveResult.Refused(ItemMoveStatus.ListNotServed)` **单参**；单参重载（:202）写作
  `new(status, 0, 0)`，故 `{ItemId}` 位置确为 **0**。
- `TakeOffAsync` 的三个拒绝（:390 `NotWearable`、:393 `ItemCannotOccupySlot`、
  :407 `DisplacedCannotOccupySlot`）**都是单参**；同方法更早的 `NoBoundContainer`(:379) 与
  `BothSlotsEmpty`(:384) 也是单参。属实。
- `ContainerSlotItem.Count => this.IsPile ? Core.Value : 1`（:324）与
  `IsPile => this.WorldClass is ItemWorldClass.Interchangeable`（:327）；
  基线 `git show 565d33fe:…/IItemContainer.cs` 第 301 行确为 `Count => Core.Value`。
- `CharacterRosterService.EquipmentCore`（:283–288）第 3 个位置实参是
  `Random.Shared.Next(1, int.MaxValue)`，而 `InventoryItemCoreState` 第 3 个形参正是 `Value`；
  `GroundPickupSlice.CarryOverAsync:404` ＝ `Value = checked((int)found.Instance)`。逐字符相符。
- `MoveItemSpaceDecoder` 从 `body[7..]`(:144) 读 `MoveCount`；同文件 :310 抄下的实机请求
  `00 09 00 40 38 00 00 01 00 00 00 03 0F 00` 在第 7..10 字节上确为 `01 00 00 00`。
- `ArrivingAvatar.Facts()`（`ItemContainers.cs:115–121`）六项 ＝ `ExpireDate: int.MaxValue`、
  `ClearAvatarId: 0`、`JewelSocket: new byte[30]`、`Color1: 0`、`Color2: 0`、`DeleteDate: 0`，
  与 `SqliteItemDomainStoreSession.FillAsync` 的
  `avatar_expire_date, avatar_clear_id, avatar_jewel_socket, avatar_color1, avatar_color2, avatar_delete_date`
  六列逐项对齐；`LandAsync` 的 `IntoEmptySlot` 分支(:478–491) 确写
  `Value = ArrivingAvatar.NewIdentity()` 与 `ArrivingAvatar.Facts()`。
- `FillAsync` 的守卫确为 `WHERE @expire IS NULL OR NOT EXISTS (…)`。
- `PvfEntryLifecycleContentSource.WornPieceMetadata`(:210–217) 对 `ItemWorldClass.Avatar` 答
  `AvatarItemKind`（该常量 ＝ 8）。
- `PvfInitialEquipmentDefinitionSource:122` 确实也读 `PvfEquipmentSlots.ByName`。
- 九条被点名的判据名**逐个存在**：`GrantedAvatarIsWholeTests` 的三条（:77／:124／:219）、
  `TwoAvatarsDoNotShareOneIdentityTests` 的三条（:75／:118／:161）、
  `RefusedEquipIsRecordedWithItsTemplateTests`（文件在 `??` 里）。

逐字引用的四条持久记载**逐字符核对属实**：`NOTI-0x0002.md:660`、同文件 `:690`、`:692`、
`NOTI-0x000D.md:157–158`、`character-lifecycle.md:198`；
`a21-starting-gear-and-skills-20260826.md:74–78` 的三行确为槽 **12／14／16**，三格都不在 0..11 内。

**记法口径（第 7 点）**：逐句读过，**未发现读者无法证伪的全称量词**。
出现的每一处「全部／没有一条／一个也没有」都同时给了可枚举的口径
（16 个键、11 个名字、118,479 个模板、19 个文件、MU-20 的 3 条红与 MU-21 的 22 条红），
且「本轮没有逐文件穷尽 `protocol/` 的 1,819 个文件」一句明写了未穷尽。
保留的两处全称语气（「从来没有穿戴任何东西」「`equipCount` 恒为 0」）确系逐字引用，出处已核对。
「读法提醒」那句关于本文件上文「本轮」所指——上文 4 处「本轮」（:463、:547、:603、:616）
都落在 `items-own-their-identity` 那几节内（:558 「被授权的两处行为变化」点着它），**属实**。

### 五、F-2 订正（A-07）：**合规**

| 要求 | 核实 |
|---|---|
| 写出原文 | 「修正案 其一」逐字写出 `:229`、`:264`、`SqliteItemDomainStore.cs:414`、`:544` |
| 基线实测 438/568 | `git show 565d33fe:…/SqliteItemDomainStore.cs \| grep -n 'item_core, 6, 4'` → **438** 与 **568**。属实，原文在基线上就已指错行 |
| R-01 的 453/595 | 引自 TASK-01 快审，记载已标出处 |
| 交付态 468/642 | `grep -rn 'item_core, 6, 4' Server/src/` → **468** 与 **642**。属实 |
| 改成按成员名索引 | 已改。四个成员名**全部属实**：`InventoryItemTables.cs` 的 `SqliteCharacterItemTable.UpdateOwnAsync`（声明 :211，谓词在 :229 内）与 `.InsertOwnAsync`（声明 :251，谓词在 :264 内）；`SqliteItemDomainStore.cs` 的 `SqliteItemDomainStoreSession.CarriedAvatarsAsync`（声明 :448，:468 在其内）与 `.FillAsync`（声明 :620，:642 在其内）。两个外层类名亦属实（`SqliteCharacterItemTable` :30、`SqliteItemDomainStoreSession` :75） |
| 「4 处」的计数口径 | 记载写「按 `item_core, 6, 4` 检索命中 3 行，其中一行是常量声明、被两个成员各引用一次，故 2＋2 ＝ 4」。评审方复算：`grep` 确为 **3 行**（`InventoryItemTables.cs:46`、`SqliteItemDomainStore.cs:468`、`:642`），`CarriesTheAvatar` 的使用点确为 **2 处**（:229、:264）。**口径成立，可复算** |
| 写明是本仓规矩自己被违反的实例 | 已写：「**这正是本仓「按成员名索引、不按行号」那条规矩自己被违反的实例**」 |
| **不得写成「本轮修好了自己弄坏的东西」** | **合规**。写的是「**这是基线就有的缺陷，不是本轮弄坏又修好的**——本轮开工之前它就已经指错了行」 |

### 六、A-07b／A-07c：**合规，证据类型标对**

- `item 0` 那条：写清列号 1／3、被拒的那一句、单参 `Refused` 的 `ItemId` 为 0、
  「**槽号仍在，模板号丢失**」、「要量的东西是槽号」，
  并标 **「证据类型：代码路径读出。真实客户端在这一帧里写的是哪个列号，本轮没有观测过」**。**标对。**
- 取下那条：三个单参返回逐个点名，标 **「证据类型：代码级读出」**。**标对。**
- 普通装备那条（A-07c）：三步事实链逐环点名（评审方逐处核对属实），三件要说清的事齐备，
  标 **「证据类型：代码级读出，未实测，也未实机」**。**标对。**
  「无覆盖」的凭据与 `behavior-change-list.md` 的 **RV-23**（3.2）与 **RP-08**（3.3）一致，
  两条都记作「无覆盖」，凭据同为 MU-20 的 3 条红与 MU-21 的 22 条红。
- **「五份记载都没发现这一条」如实写下**，五份逐个点名（清单第一版、QR-06-1、TASK-08 执行结果、
  QR-08-1、D-08），与 `behavior-change-list.md:261` RV-23 那一栏的说法一致。

### 七、译名修正案（第 6 点）：**合规**

- **未改动 `PvfEquipmentSlots.cs`**：该文件不在 `git status --porcelain` 的 19 行里；
  评审方开原文确认类型级 `<remarks>` 的
  「A21 inserted the aurora-illusion avatar ahead of the weapon…」**原句仍在**。
- 三行表三项出处逐个核实：`AuraSkinAvatar` 在 `NOTI-0x0002.md:690` 与
  `a21-starting-gear-and-skills-20260826.md:83`；`aura skin avatar` 在
  `literals-a21.csv:27941`，作 `[equipment type]` **0 个**；`[aurora avatar]` **423 个**、取值集合 `{0}`。
- 「补记而非抹去」的写法与本仓规矩一致。另核：修正案 其二点名的**四处生产注释**
  与 `AGENTS.md:616–620` 的原文列表逐项相同，且抽查
  `ItemPlacementContent.ItemWorldClass` 与 `IItemPlacementRuleService.ClassifyAsync` 两处，
  都是「保留原文引述 ＋ 补记 Amended 段」，与记载所述相符；`AGENTS.md` 原文（:616 起那一条）**未删**。

### 八、非阻断观察（不要求本轮整改）

1. **（值得后手接手）**「那 5 行的来历本轮未查明」已由本轮评审查明为
   `findings-archive-measurement.md:138` 的一次加法错误并沿契约传播；措辞「5 **行**」建议改为「5 的差额」。
   **同时建议控制器把契约验收条件第 4 条与 `TaskContract.md:65／:91` 的 66,591／66,323 一并记一处修正案**
   ——错的是契约，不是交付物。
2. `AGENTS.md` 写「24 号槽走的是 `[support weapon]`，26 个模板」。属实，但 `ByName` 里
   `[earring]`／`[support weapon]`／`[creature]` 三个键都是 24，其中 `[creature]` 在档案里有 **707** 个模板。
   该句在上下文里只为解释「`[earring]` 零命中不等于 24 号槽没东西」，不构成错句，可补一个数更完整。
3. 执行结果自查里「写下的语句文本与文件逐字符相符」略强：`AGENTS.md` 的
   `Value: Random.Shared.Next(1, int.MaxValue)` 是位置参数的**具名转写**（第 3 位形参确为 `Value`），
   不是源文件的逐字文本。`AGENTS.md` 正文本身没有把它标成逐字引用，故**不影响记载的可证伪性**。
4. 文风与详略未计入判定。

### 九、结论

契约验收条件 1–5、修正案 A-07／A-07b／A-07c 的第 6／7／8 条**逐条达成**；
CK-019／CK-020／CK-021 三条检查由评审方**独立重做**，结论与执行结果一致；
边界未越（只改 `AGENTS.md`，生产代码与判据未动，未提交、未建检查点、未用 git 还原类命令）。
**未发现阻断项。判定：`passed`。**
（`passed` 仅表示本 Task Unit 可继续走执行者的接受逻辑，不是最终质量批准。）


## Controller Disposition D-07 — 接受 QR-07-1 的 `passed`，并派一次定向记载返工

**判定**：接受交付。QR-07-1 逐条核实无误，`AGENTS.md` 的改动是本轮唯一增量
（`git diff --numstat` = `230 3`，`-` 行只有 3 行即 F-2 那句，**生产代码与判据一行未动**）。

**那 5 的差额已查明，而且错的是契约不是交付物**：
`findings-archive-measurement.md` 的表是对的，**它的结论句算错了小计**；
错值传入 `TaskContract.md` 与本任务验收条件第 4 条。
契约已升 revision 3 并补记 **C-3**；findings 文件已补记 **F-1**。

**TASK-07 的处置是对的**——三数并列、不照抄契约、不断言契约错、给出可重跑的测法。
**正是这个处置让这个错被逮住。** 若它照抄了契约的 66,591，这个错会一路进入 `AGENTS.md`。

### 一处需要定向返工的（记载已被自己的后续查证推翻）

`AGENTS.md` 里现在写着「那 5 行的来历**未查明**」。**它已经查明了。**
需要一次**只改记载**的返工：
1. 「未查明」改为已查明，并写出成因（同一份取证文件的**表**与**结论句**对不上，是一次加法错误）
2. 「5 **行**」改为「5 的**差额**」——它不是 5 个模板行
3. 正确的数写成 **66,596**（时装 **66,328**），并注明契约原写 66,591 已由 C-3 订正
4. 顺带落实 QR-07-1 的两条非阻断小观察：24 号槽可补 `[creature]` 707 个模板；
   自查里「逐字符相符」对 `Value:` 一处略强

**不得改动任何生产代码或判据。**

## 执行结果（TASK-07，第二轮：那 5 的差额已查明）

按 **Controller Disposition D-07** 派下的定向记载返工。**只改记载**：本轮的 owned diff 仍然只有
`AGENTS.md` 一个文件，**生产代码与判据一行未动**，未提交、未建检查点、未执行任何 git 还原类命令。
`TaskContract.md` 的 **C-3** 与 `findings-archive-measurement.md` 的 **F-1** 由控制器已改好，本轮未动。

### 一、`AGENTS.md` 那一段的前后对照

**改动 1（D-07 第 1／2／3 条）——「17 个认不出的名字」那张表底下的复算段。**

改前（`AGENTS.md`，紧跟 17 行表的那 4 行）：

```
复算：11 个时装名相加 **66,328**；6 个非时装名相加 **268**；两数相加 **66,596**；行数 11 ＋ 6 ＝ **17**。
（本 Spec 的中间记载把这两个数写作 66,323 与 66,591，各比这次重测少 5。本轮按上面的口径测了两遍，
两遍都是 66,328 与 66,596，另与本文件上面「已知缺陷与覆盖缺口」里早已记着的「66328 个时装模板」相符；
那 5 行的来历本轮**未查明**，三个数一并记在这里。）
```

改后（首行未动，其后整段重写）：

```
复算：11 个时装名相加 **66,328**；6 个非时装名相加 **268**；两数相加 **66,596**；行数 11 ＋ 6 ＝ **17**。
加法的每一项都在上面那张 17 行表里，可当场重算。

**这里曾经差 5，来历已查明：那是一次加法错误，不是 5 个模板行。**
本 Spec 的中间取证文件 `.rks-agent/tasks/server-agrees-what-an-avatar-is/findings-archive-measurement.md`
把这两个数写作 66,323 与 66,591，各比这里少 5。错在哪已经落到句子上：
**同一份文件的表与它自己的结论句对不上**——该文件的探针输出原文逐名列的 11 个数是
423／7565／8391／7028／7929／8325／8194／8070／2954／7000／449，**这 11 个数相加是 66328**，
与本轮按上面口径测的两遍、以及 QR-07-1 评审方另写探针跑的第三遍**逐个相同**；
而同一个文件的结论句写成「这 11 个名字覆盖 66,323 个模板」。**表是对的，小计算错了。**
计数口径与旁证：非时装六项 65＋47＋31＋50＋47＋28 ＝ **268**，两版相同，
**5 的差额整个落在时装小计这一项**（66,591 − 66,323 ＝ 268 ＝ 66,596 − 66,328）；
另与本文件上面「已知缺陷与覆盖缺口」里早已记着的「66328 个时装模板」相符。
所以按上面那一遍 118,479 个模板的口径，**档案里并没有另外 5 个时装模板可数**——
差的是一个写错的小计，不是 5 个能被打开的模板文件。
错值后来被下游照抄，本轮点到的落点有两处：`TaskContract.md` 与 TASK-07 的验收条件第 4 条
（本轮没有逐文件穷尽本 Spec 的中间记载，所以这里只写点到的这两处）。
**订正已经落好**：契约原写的 66,591 已由 **Contract Amendment C-3** 订正为 **66,596**（其中时装 **66,328**），
`findings-archive-measurement.md` 已补记 **修正案 F-1**；两处都保留原文、另起一段订正。

**这一处留下的教训比那个数本身有用。** 那份取证文件记的是档案侧那一遍穷尽扫描
（118,479 个模板逐个读过），它同时给了表和小计；
**而上面点到的两处下游（`TaskContract.md`、本任务验收条件第 4 条）照抄的都是它的结论句，不是它的表**
——读的人看不见加法，也就没有机会发现小计错了。所以：
**一个由多项相加得出的数，要么当场把加法写出来（各项、项数、和，三样齐），要么就不要写这个小计。**
上面那张 17 行表加紧跟其后的那行复算，就是按这条写的。
按 D-07 的处置意见，这个错能被逮住，是因为上一轮没有照抄契约的 66,591，而是把三个数并列写了出来。
```

**改动 2（D-07 第 4 条前半）——`[title]`／`[earring]` 零命中那段的括注。**

改前（1 行）：

```
（称号在档案里走的是 `[title name]`，1,796 个模板；24 号槽走的是 `[support weapon]`，26 个模板。）
```

改后（7 行）：

```
（称号在档案里走的是 `[title name]`，1,796 个模板；24 号槽走的是 `[support weapon]`，26 个模板。
补一个数：`PvfEquipmentSlots.ByName` 里槽号写作 **24** 的键有三个——`[earring]`、`[support weapon]`、
`[creature]`，其中 `[creature]` 的模板数是 **707**。这 707 的出处是 QR-07-1 评审方那一遍探针，
与 `findings-archive-measurement.md` 的类型名表、以及上一个成果 `items-own-their-identity` 记下的
`Avatar=66328 Creature=707 Equipment=51444 sum=118479` 三处相同；
本轮执行者那一遍只统计了 `ByName` **认不出**的名字，没有单独复算它。
所以「`[earring]` 一次都没被用到」说的只是这一个键，不是 24 号槽在档案里没有东西。）
```

**改动 3（D-07 第 4 条后半）——自查里「逐字符相符」那句的订正，写在这里而不是改原处。**

D-07 第 4 条要求「按实情改写」的那句在本文件**第一轮执行结果**的「CK-019 记法口径自查」里：
「**这一批本任务逐处打开原文核对过，写下的语句文本与文件逐字符相符。**」
D-07 明写「不要重写或删改任何既有小节」，故**原句保留不动，在此补记订正**（与本仓「补记而非抹去」一致）：

> **订正**：那句话对其中一处略强。`AGENTS.md` 里写的
> `CharacterRosterService.EquipmentCore` 给起始装备写 `Value: Random.Shared.Next(1, int.MaxValue)`，
> 源文件里那第 3 个实参是**位置参数**，`Value` 是 `InventoryItemCoreState` 第 3 个**形参名**——
> 这是一次**具名转写**，不是源文件的逐字文本。
> 该批其余被点名的语句（`ByName` 的 16 个键、`PutOnAsync` 的两个拒绝返回、`HandleAsync` 的
> `ListNotServed` 那一句、`TakeOffAsync` 的三个单参返回、`ContainerSlotItem.Count`／`IsPile`、
> `ArrivingAvatar.ItemKind`／`NewIdentity`／`Facts`、`GroundPickupSlice.CarryOverAsync`）
> 经 QR-07-1 逐处开原文核对属实。
> 准确的说法是：**该批逐处开原文核对过语句属实，其中 `Value:` 这一处是具名转写而非逐字引用。**
> QR-07-1 已认定这一点**不影响记载的可证伪性**（`AGENTS.md` 正文没有把它标成逐字引用），
> 本轮**未改动 `AGENTS.md` 的这一处**。

**再无第四处落笔**：D-07 只派了上面三处；`TaskContract.md`（C-3）与
`findings-archive-measurement.md`（F-1）由控制器改好，本轮**一个字节未动**。

### 二、四条改动逐条说明

| D-07 条目 | 落在哪 | 怎么改的 |
|---|---|---|
| 1. 「未查明」改为已查明，写出成因 | 改动 1 | 写成「来历已查明：一次加法错误」，并把成因落到句子上——**同一份取证文件的表与它自己的结论句对不上**，表列的 11 个数相加是 66328，结论句却写 66,323；**表是对的，小计算错了**。同时写明这不是口径差别 |
| 2. 「5 行」改为「5 的差额」 | 改动 1 | 全段不再出现「5 行」。写的是「这里曾经差 5」「**5 的差额**整个落在时装小计这一项」，并明写「**不是 5 个模板行**」「差的是一个写错的小计，不是 5 个能被打开的模板文件」 |
| 3. 正确的数 66,596（时装 66,328），注明 C-3 与 F-1 | 改动 1 | 表下复算行保持 66,328 ／ 268 ／ 66,596；新增一句「契约原写的 66,591 已由 **Contract Amendment C-3** 订正为 **66,596**（其中时装 **66,328**），`findings-archive-measurement.md` 已补记 **修正案 F-1**；两处都保留原文、另起一段订正」 |
| 4a. 24 号槽可补 `[creature]` 707 | 改动 2 | 括注里补出 `ByName` 槽号为 24 的三个键与 `[creature]` 的 **707**，并写明这 707 的三处出处、以及本轮执行者那一遍**没有单独复算它**（那一遍只统计认不出的名字）。按本仓规矩**按成员名索引**（`PvfEquipmentSlots.ByName`），不写行号 |
| 4b. 自查「逐字符相符」略强 | 改动 3（本小节） | 既有小节不动，在此补记订正：`Value:` 那一处是**具名转写**（第 3 位形参名确为 `Value`），不是逐字文本；改述为「逐处开原文核对过语句属实，其中 `Value:` 是具名转写」 |
| 教训（D-07 的写法要求） | 改动 1 末段 | 写进正文：那份取证文件同时给了表和小计，**而点到的两处下游照抄的是它的结论句、不是它的表**，读的人看不见加法就没机会发现小计错了；因此——**一个由多项相加得出的数，要么当场把加法写出来（各项、项数、和，三样齐），要么就不要写这个小计** |

**记法口径自查（不用词表搜，逐句问「这句在说个例还是说一类」）。** 本轮新写下的每一句都过了一遍，
三处按此改过（改后即上文引的定稿）：

1. 原写「那份取证文件记的是**本轮唯一一次**穷尽档案的扫描」——「唯一」是全称，而我没有穷尽本 Spec 的中间记载。
   改为「那份取证文件记的是**档案侧那一遍**穷尽扫描（118,479 个模板逐个读过）」。
2. 原写「它的表被后续**多份产物**引用，而**被引用的是它的结论句，不是它的表**」——在说一类，且「多份」未枚举。
   改为「**上面点到的两处下游（`TaskContract.md`、本任务验收条件第 4 条）照抄的都是它的结论句，不是它的表**」，
   两处可逐个打开核。
3. 原写「这个错能被逮住，也正是因为上一轮没有照抄契约的 66,591」——这是因果断言，不是本轮的观测。
   改为「**按 D-07 的处置意见**，这个错能被逮住，是因为……」，标明是引述控制器的处置。

保留的两处带「并没有／没有」的说法都给了可复算的口径：
「按上面那一遍 **118,479 个模板**的口径，档案里并没有另外 5 个时装模板可数」（重跑上文写明的测法即可证伪）；
「本轮没有逐文件穷尽本 Spec 的中间记载，所以这里只写点到的这两处」（明写未穷尽）。

### 三、收尾检查的实际输出

1. **`git status --porcelain` ＝ 19 行**（`wc -l` 实测 **19**），与 QR-07-1 记的 19 行**逐行相同**：

```
 M AGENTS.md
 M Server/src/Dfo.Server.Abstractions/Content/ItemPlacementContent.cs
 M Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs
 M Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs
 M Server/src/Dfo.Server.Abstractions/Inventory/IItemPlacementRuleService.cs
 M Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs
 M Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs
 M Server/src/Dfo.Server.Application/Inventory/ItemPlacementRuleService.cs
 M Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs
 M Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs
 M Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs
 M Server/tests/Dfo.Server.Application.Tests/Inventory/ItemClassificationTests.cs
 M Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteItemDomainStoreTests.cs
?? .rks-agent/tasks/server-agrees-what-an-avatar-is/
?? Server/tests/Dfo.Server.Application.Tests/Inventory/AvatarSlotRulesAfterTheKindChangeTests.cs
?? Server/tests/Dfo.Server.Application.Tests/Inventory/DraggedAvatarStaysWholeTests.cs
?? Server/tests/Dfo.Server.Application.Tests/Inventory/GrantedAvatarIsWholeTests.cs
?? Server/tests/Dfo.Server.Persistence.Tests/Inventory/TwoAvatarsDoNotShareOneIdentityTests.cs
?? Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/RefusedEquipIsRecordedWithItsTemplateTests.cs
```

`AGENTS.md` 仍是那唯一一个本轮改动的 ` M`。**本轮新增的文件 0 个，删除的文件 0 个。**

2. **`git diff` 的文件构成**：`git diff --numstat` 列出 13 个文件——`AGENTS.md` 与
   TASK-06 收尾态就已经在那里的 12 个 ` M`。**这 12 个本轮一个字节也没有碰**
   （本轮只对 `AGENTS.md` 做过写操作）：

```
259     3       AGENTS.md
14      6       Server/src/Dfo.Server.Abstractions/Content/ItemPlacementContent.cs
24      1       Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs
35      0       Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs
9       2       Server/src/Dfo.Server.Abstractions/Inventory/IItemPlacementRuleService.cs
15      1       Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs
133     8       Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs
11      3       Server/src/Dfo.Server.Application/Inventory/ItemPlacementRuleService.cs
32      5       Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs
116     13      Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs
29      3       Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs
14      8       Server/tests/Dfo.Server.Application.Tests/Inventory/ItemClassificationTests.cs
37      0       Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteItemDomainStoreTests.cs
```

   `AGENTS.md` 由第一轮的 `230 3` 变成 **`259 3`**：**`−` 仍是 3 行**（还是第一轮改动 1 那 3 行，
   本轮没有再删任何基线行），`+` 多了 **29** 行 ＝ 改动 1 的 ＋25 ＋ 改动 2 的 ＋6 − 被本轮替换掉的
   第一轮新增行 2（那 2 行是第一轮新增的，不计入基线的 `−`）。
   文件行数 **1051 → 1080**；`sha256sum AGENTS.md` ＝
   `3f70c9eab0ff1cfdc1653c1b9bd27d5671bd6bb01507900dc59688ccc7ff8904`
   （第一轮为 `5b68938e201ac6d04aff4a65455d1a430bf6058e7226becbb9460a05d9c2d839`）。

3. **CK-021 全量重跑**：

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

| 程序集 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| `Dfo.Server.Protocol.Tests` | 524 | 0 | 0 |
| `Dfo.Server.Persistence.Tests` | 320 | 0 | 0 |
| `Dfo.Server.Application.Tests` | 1036 | 0 | 0 |
| `Dfo.Server.Infrastructure.Tests` | 236 | 2 | 0 |
| `Dfo.Pvf.Tests` | 60 | 0 | 0 |
| `Dfo.Server.Networking.Tests` | 52 | 0 | 0 |
| `Dfo.Server.Host.Tests` | 6 | 0 | 0 |
| **合计** | **2234** | **2** | **0** |

524 ＋ 320 ＋ 1036 ＋ 236 ＋ 60 ＋ 52 ＋ 6 ＝ **2234**。**`EXIT=0`**。
跳过的两条仍是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`，
与 QR-07-1 记的两条同名。本轮只改了 Markdown，计数与第一轮、与 QR-07-1 的重跑**逐程序集相同**。

4. **未提交、未建检查点、未执行任何 git 还原类命令**；未触碰主检出与 `.worktrees/client-debug-lab`。
   本轮用到的临时脚本建在会话临时目录（工作区之外），只对 `AGENTS.md` 做写操作。

### 四、本轮未做的事（明写）

- **没有重跑档案侧的探针**。本轮不新增档案读数：66,328／66,596／268／707 全部引自已有的三处记载
  （第一轮执行结果、QR-07-1、上一个成果 `items-own-their-identity`），出处已在正文写明。
- **没有改动 `TaskContract.md` 与 `findings-archive-measurement.md`**（C-3／F-1 由控制器已落）。
- **没有改动生产代码与判据**，也没有改 `PvfEquipmentSlots.cs`。
- **没有改动本文件的任何既有小节**：本节是新增小节，第一轮执行结果、QR-07-1、D-07 原文一字未动。
