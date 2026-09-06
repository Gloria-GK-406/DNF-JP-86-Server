# TASK-01 — 世界的写入能说出一件时装带着哪六个事实

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

让一次写入可以自带时装的六个事实，并让结算把它们写进世界行。
**本任务行为中性**：结束时仍然没有任何生产代码供给事实，全量判据必须仍是 2216 通过 / 2 跳过 / 0 失败。

- Referenced spec requirements: RQ-002
- Referenced plan sections: plan.md 的 T-001（Steps 1–4，CK-001..CK-003）；Owned Boundaries 的 BD-001、BD-002、BD-008
- Owned boundary:

BD-001 `Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs`
BD-002 `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`
BD-008 `Server/tests/`（只新增判据）

- Permitted files or interfaces:

- `Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs`
- `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`
- `Server/tests/Dfo.Server.Persistence.Tests/` 下的判据
- 因编译需要而必须同步的调用点（若有，逐个在执行结果里列出）

- Dependencies: 无
- Required dependency outputs: 无
- Acceptance conditions:

1. `StoredItem` 能承载时装的六个事实，其契约按 `WorldClass` 已有的形状写清：读的方向不答，写的方向对时装必须答。
2. 结算写世界行六列时**优先采用本次写入自带的事实**；既有事实的回捞只用于「一行既有时装被重建」。
3. **行为中性**：全量判据仍为 2216 通过 / 2 跳过 / 0 失败，退出码 0。
4. `SqliteItemDomainStore` 里那三条「今天成立但原因在别的方法里」的跨方法耦合记载已按改动后的真实情形更正，且**按仓规补记修正案**（写明它此前说的是什么），不是抹去原文。

- Task-specific authorization:

Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做**一次**。
变异验证**一律用文件级备份 + `sha256sum` 还原并核散列**；**禁止任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash`）——本仓曾因此丢过未提交的工作。
不得触碰主检出与 `.worktrees/client-debug-lab`。

- Blocking conditions:

**若发现无法在不改变既有行为的前提下完成本任务，停下并提交阻塞报告。**
**不得顺手把 T-002 的改动提前做进来**——那会在树上留下一条「下次装载必炸」的行。

- Required methods:

- `test-driven-development`：先让判据红，再让它绿。
- 覆盖主张必须由**变异实测**支撑，**不得只凭 grep 断言**——本仓已两次证明按消息文本或标识符搜索会给出错误的覆盖读数。
- 记法：**不写读者无法证伪的基数**。给出数目时写「N 处，分别是：…」并说明计数口径；按成员名索引，**不按行号**。
- 遇到未预期的失败时用 `systematic-debugging`，先取得证据再改。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 2216 通过 / 2 跳过 / 0 失败，退出码 0 |
| CK-002 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Persistence.Tests --nologo` | 新判据证明一次自带事实的写入把六列写进了世界行，值与写入所给的相同 |
| CK-003 | Non-command：通读被改动的那几处记载 | 仍然成立的说法保持原样；不再成立的说法已就地更正并写明它此前说的是什么 |

- Required evidence:

- 三条检查的实际输出与退出码。
- 被改动记载的前后对照（原文与更正后各一份）。
- 一句明确的自述：本任务**没有**给任何生产调用方接线。

- Constraints:

- 分层：Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host
- `TreatWarningsAsErrors` 开启，**CS9113（未读的主构造器参数）是错误**
- 不新增协议帧，不改任何帧的字节布局
- `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 必须继续通过，判定谓词与具名豁免清单**不得放宽或扩充**
- 领域层 `store` 只指事务结算的真源
- 既有断言的期望值**不得为迁就实现而改写**；唯一例外是正对应 `spec.md` 列出的某条行为变化，且须逐条归因
- 不裁定契约兽槽位 24 还是 25；**不给 `PvfEquipmentSlots.ByName` 补任何键**
- 注释是参考材料不是铁律，判断以代码设计为准；但**改已被证伪的记载要补记修正案**，改「要求」必须停下来问

## 执行结果

### 改了哪些文件（3 个，按仓库路径列全）

计数口径：本任务在工作区里内容发生变化的文件，全部列出，一个不省。

**1. `Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs`**

- `StoredItem` 增加成员 `AvatarDetail`，类型 `InventoryAvatarDetailState?`（既有类型，未新建）。
  它承载时装的六个事实：到期日、清除标识、珠宝槽、颜色一、颜色二、删除日期。
- `StoredItem` 的 `<remarks>` 增加四段，按 `WorldClass` 已有的形状写清它的契约：
  - 方向与 `WorldClass` 相同——进来的方向答，出去的方向不答；读只从行里取出位置与 core，
    所以读到 null 的含义是「没被问」而不是「一件什么都不带的时装」。
  - 加粗一句写明义务：**结算要写这六列，所以一次把时装带进世界的写入必须交代它们**。
  - 写明它与 `WorldClass` 的**不同之处**：这里的沉默不在写入处被拒，因为结算对这一项**有**
    第二读法（世界里已有的那一行会在重建时被交还自己的六个事实），而对 `WorldClass` 没有；
    因此这是一条**义务**而不是一道检查——在写入发生的那一刻，没有任何东西能把
    「带一件时装进来」和「搬动一件已经站着的时装」分开。
  - 写明保留自己那一行的东西保留自己那六列，这里的答复不会覆盖它们。

**2. `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`**

- `FillAsync` 的 `facts` 取值改为**优先采用本次写入自带的事实**：
  `item.AvatarDetail` 非空时用它，否则退回 `carried`（原有的既有事实回捞）。
  `CarriedAvatarsAsync` **未删除、未改动其查询**。
- `FillAsync` 的 `<remarks>` 增加一段 `<para>`，说明这两条来源回答的是**不同的问题**、
  谁也替代不了谁：带着事实的写入是一件时装**正在进入世界**（那时根本没有行可问），
  回捞是一件世界已经持有的时装**正在重建它的行**（那时写入没有什么可补充）。
- 新增私有成员 `FactsOf(InventoryAvatarDetailState)`：把六个事实排成 `AvatarColumns` 绑定的次序。
  其 `<remarks>` 写明两个颜色被加宽到 `int` 的理由（列是 INTEGER，SQLite 不存十六位的东西，
  两条来源必须绑同一个值），以及珠宝槽为 null 时与「回捞没找到」一样按 null 绑定、
  长度是否为三十由表上的 CHECK 说了算。
- 更正 `CarriedAvatarsAsync` `<remarks>` 里那三条跨方法耦合记载中的**第一条**（见下文对照）。
  第二条、第三条**原样保留**，因为它们在改动后仍然成立（`Survivors` 三趟匹配未动；
  `FillAsync` 的 INSERT 仍然完全不写那八个契约兽列）。

**3. `Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteItemDomainStoreTests.cs`**

- 新增判据 `AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates`（**只新增，未改动任何既有断言**）。
  它对一个**这个库里从未出现过**的时装做一次自带六个事实的写入，结算之后逐个核对六列，
  用的是文件里既有的 `ReadAvatarDetailAsync` 帮手。因为库里没有对应的行，
  回捞什么也找不到，所以落在六列上的值只可能来自这次写入。
  六个值逐条断言而不是整体比较——「六个里绑上五个、剩一个是 null」正是半截接线会留下的形状。

**没有为编译而同步任何调用点**：`AvatarDetail` 是可选 `init` 成员，既有构造处一处未动。

### 一句自述

**本任务没有给任何生产调用方接线。** 全仓对 `StoredItem.AvatarDetail` 赋值的地方只有 1 处，
就是上面那条新判据（口径：`grep -rn "AvatarDetail = " Server/tests Server/src` 中目标类型为
`StoredItem` 的赋值；其余命中属于既有的 `InventoryItemState` / `InventoryStorageItem`，与本任务无关）。
`Server/src` 下无任何一处向 `StoredItem.AvatarDetail` 供给事实。

### CK-001 —— 全量判据

命令：

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

改动**前**（基线，565d33fe 的工作区状态）实测输出：

```
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52 - Dfo.Server.Networking.Tests.dll
已通过! - 失败:     0，通过:   521，已跳过:     0，总计:   521 - Dfo.Server.Protocol.Tests.dll
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60 - Dfo.Pvf.Tests.dll
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6 - Dfo.Server.Host.Tests.dll
已通过! - 失败:     0，通过:   316，已跳过:     0，总计:   316 - Dfo.Server.Persistence.Tests.dll
已通过! - 失败:     0，通过:   236，已跳过:     2，总计:   238 - Dfo.Server.Infrastructure.Tests.dll
已通过! - 失败:     0，通过:  1025，已跳过:     0，总计:  1025 - Dfo.Server.Application.Tests.dll
EXITCODE=0
```

合计 **2216 通过 / 2 跳过 / 0 失败，退出码 0**——与契约写的期望值逐项目一致。

改动**后**实测输出：

```
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52 - Dfo.Server.Networking.Tests.dll
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60 - Dfo.Pvf.Tests.dll
已通过! - 失败:     0，通过:   521，已跳过:     0，总计:   521 - Dfo.Server.Protocol.Tests.dll
[SKIP] Dfo.Server.Infrastructure.Tests.Content.PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message
[SKIP] Dfo.Server.Infrastructure.Tests.Content.PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog
已通过! - 失败:     0，通过:   317，已跳过:     0，总计:   317 - Dfo.Server.Persistence.Tests.dll
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6 - Dfo.Server.Host.Tests.dll
已通过! - 失败:     0，通过:   236，已跳过:     2，总计:   238 - Dfo.Server.Infrastructure.Tests.dll
已通过! - 失败:     0，通过:  1025，已跳过:     0，总计:  1025 - Dfo.Server.Application.Tests.dll
EXITCODE=0
```

合计 **2217 通过 / 2 跳过 / 0 失败，退出码 0**。

**与契约期望值的差异，如实记下**：契约 CK-001 写的是「2216 通过」，实测 2217。
差额恰好 **+1**，全部落在 `Dfo.Server.Persistence.Tests`（316 → 317），
其余六个测试项目的计数**逐项目完全不变**。这 1 条就是 CK-002 要求新增的那条判据。
换句话说，2216 是**本任务开始前**的基线数，而 CK-002 同时要求新增判据——
两条检查的期望值在算术上互相排斥，不可能同时字面成立。
本任务按「行为中性」的实义交付：**既有 2216 条一条不少、一条不红、跳过仍是同样那 2 条、退出码 0**，
增量只来自新增判据。**这一点提请评审裁定，不由本任务自行改写契约。**

### CK-002 —— Persistence 判据

命令：

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Persistence.Tests --nologo
```

输出：

```
已通过! - 失败:     0，通过:   317，已跳过:     0，总计:   317，持续时间: 10 s - Dfo.Server.Persistence.Tests.dll (net10.0)
EXITCODE=0
```

新判据 `AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates` 证明：
一次自带事实的写入把六列写进了世界行，六个值与写入所给的相同
（21 / 22 / 三十个 0x17 字节 / 24 / 25 / 26，逐条断言）。

#### 先红后绿（TDD）

只加了 `StoredItem.AvatarDetail`、**尚未改 `FillAsync`** 时跑这条判据：

```
[xUnit.net] ...SqliteItemDomainStoreTests.AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates [FAIL]
  错误消息: Assert.NotNull() Failure: Value is null
失败!  - 失败:     1，通过:     0，已跳过:     0，总计:     1
EXITCODE=1
```

红的原因正是命题本身：没有供给通路时六列全为 NULL，`ReadAvatarDetailAsync` 取不到行。
改完 `FillAsync` 之后转绿。

#### 变异实测（两次，均用文件级备份 + `sha256sum` 还原并核散列；**未使用任何 git 还原类命令**）

备份散列（`Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`）：

```
f820c25890b59188c93b028f0c3d14de24288137b6eebd038ebc0bf3f7b54cdd
```

**变异 M1 —— 让写入自带的事实不再被真正写下**：把 `FactsOf` 的六个取值中的前五个改成 `null`
（结构保留，仅值被掏空）。结果：

```
[xUnit.net] ...SqliteItemDomainStoreTests.AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates [FAIL]
失败!  - 失败:     1，通过:   316，已跳过:     0，总计:   317
EXITCODE=1
```

**恰好 1 条红，就是新判据**；既有 316 条全绿——证明新增的绑定路径没有搭在任何既有判据上，
也证明这条判据钉住的确实是「写入自带的事实被写进了行」。

**变异 M2 —— 让既有事实的回捞不再起作用**：把 `carried` 命中后的取值改成恒 `null`
（`? held :` 改为 `? (held is null ? null : null) :`）。结果：

```
[xUnit.net] ...SqliteItemDomainStoreTests.TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding [FAIL]
失败!  - 失败:     1，通过:   316，已跳过:     0，总计:   317
EXITCODE=1
```

红的是**另一条**判据，而新判据保持绿。这一次变异同时坐实两件事：
回捞今天仍然是承重的（所以不能删），而新判据**不依赖**回捞（所以它证明的确实是自带的那条路）。

两次变异之后各自由备份还原并核散列，均为
`f820c25890b59188c93b028f0c3d14de24288137b6eebd038ebc0bf3f7b54cdd`，与备份一致。

### CK-003 —— 被改动记载的前后对照

被改动的记载共 **1 处**，就是 `SqliteItemDomainStore.CarriedAvatarsAsync` 的 `<remarks>` 里
「三条跨方法耦合」中的**第一条**。计数口径：本任务改动的注释段落数（不含新增的段落）。

**原文（改动前，逐字）**：

```
/// First, the guard in <see cref="FillAsync"/> is equivalent to the unique index that used to
/// stand on the retired avatar key only because the SELECT above filters on
/// avatar_expire_date IS NOT NULL. That filter is what makes a returned row's first column
/// never null, which is what makes "this row carries no facts" and "no row was found" the same
/// question. Loosen the filter and the guard silently starts refusing rows the old index let
/// through. It is a coupling between two methods, not a property of either.
```

**它为什么不再成立**：这段话的承重词是 "only"。改动之后 `@expire` 有**两个**来源——
这次回捞，以及写入自带的六个事实。一次自带事实的写入会在**这里什么也没找到**的情况下
使 `@expire` 非空，于是守卫对**新入库的时装**开始真的生效。所以「守卫等价于那条退役唯一索引，
**只**因为上面这条 SELECT 过滤了 avatar_expire_date IS NOT NULL」这句话不再为真。
它说对的那一半（对本方法这条过滤条件的耦合）仍然为真，所以**更正而不是抹去**。

**更正后（逐字，含仓规要求的修正案——写明它此前说的是什么）**：

```
/// First -- <b>amended; what this paragraph said before is quoted at the end of it</b> -- this
/// SELECT is no longer the only thing that decides when the guard in <see cref="FillAsync"/>
/// engages. That guard runs when the @expire the insert binds is not null, and @expire now has
/// two sources: this retrieval, and the six facts a write carried itself. What this retrieval
/// contributes is unchanged and still coupled the same way -- it filters on
/// avatar_expire_date IS NOT NULL, which is what makes a returned row's first column never
/// null, which is what makes "this row carries no facts" and "no row was found" the same
/// question here; loosen this filter and the guard still starts refusing rows the old index let
/// through. What is no longer true is the word "only". A write carrying its own facts makes
/// @expire non-null with nothing found here at all, and that is precisely the case the retired
/// index covered and this retrieval never could reach: an avatar entering the world for the
/// first time, which no standing row can be asked about. So the guard is still not a property
/// of either method, and it is now coupled to what the write carries as well as to this filter.
/// <para>
/// What this paragraph said before: "the guard in FillAsync is equivalent to the unique index
/// that used to stand on the retired avatar key only because the SELECT above filters on
/// avatar_expire_date IS NOT NULL". That was true while this retrieval was @expire's only
/// source, and it stopped being true when a write gained the ability to carry the six facts
/// itself. It is corrected rather than deleted because the half it got right -- the coupling to
/// this filter -- is still the reason to read this paragraph.
/// </para>
```

**通读结论——仍然成立、因此原样未动的说法（4 处，分别是）**：

计数口径：`CarriedAvatarsAsync` 与 `FillAsync`／`LandAsync`／`FlushAsync` 上，
本任务读过并逐句核对过、判定仍然为真的记载段落。

1. `CarriedAvatarsAsync` 三条耦合的**第二条**（`LandAsync` 没有自己的时装唯一性守卫，
   靠 `Survivors` 三趟都按 core 里的 value 匹配）。`Survivors` 本任务一字未动，仍然为真。
2. `CarriedAvatarsAsync` 三条耦合的**第三条**（跨重建被救回的只有六个时装列，
   `FillAsync` 的 INSERT 完全不写那八个契约兽列）。该 INSERT 的列清单本任务一字未动，仍然为真。
3. `CarriedAvatarsAsync` 开头两段（时装是 kind 8、UID 在 core 里、行被替换会丢掉事实所以读回来重写；
   幸存的行不问，因为没人删它）。仍然为真——回捞的查询与触发条件本任务一字未动。
4. `LandAsync` 的 `<remarks>`（只写 core，不碰身份、创建时间和时装／契约兽列，
   「这就是状态跟着东西走而不是跟着位置走的意思」）。仍然为真：`LandAsync` 本任务一字未动。
   写入自带的事实在落到幸存行时确实被忽略，而这正是这段话说的规则本身，不是它的例外；
   这一点已写进 `StoredItem` 的契约最后一段，让写入方在写入处就读得到。

### 自查：逐条对照契约的验收条件

**验收 1**「`StoredItem` 能承载时装的六个事实，其契约按 `WorldClass` 已有的形状写清：
读的方向不答，写的方向对时装必须答」——**达成**。
凭据：`StoredItem` 上新增成员 `AvatarDetail`，类型是既有的 `InventoryAvatarDetailState`
（未新建类型），其六个分量正是那六列。`<remarks>` 与 `<summary>` 按 `WorldClass` 的形状写：
同样一句加粗的义务陈述（「结算要写这六列，所以把时装带进世界的写入必须交代它们」）、
同样的方向说明（读为 null 意为「没被问」）。
**与 `WorldClass` 形状唯一的偏离已在注释里就地写明并给了理由**：不在 `Write` 里抛。
理由是结算对这一项有第二读法而对 `WorldClass` 没有，且在写入那一刻无法把
「带进来」与「搬动已有的」分开；若在此抛出，`AnAvatarsOwnColumnsStayWhereTheyAreThroughAMove`、
`TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`、
`Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 三条既有判据会立刻变红，
而它们钉住的正是回捞那条路——**改它们等于为迁就实现改写既有断言，是契约明令禁止的**。

**验收 2**「结算写世界行六列时优先采用本次写入自带的事实；既有事实的回捞只用于
『一行既有时装被重建』」——**达成**。
凭据：`FillAsync` 的 `facts` 先看 `item.AvatarDetail`，命中即用 `FactsOf` 展开，
不命中才落到 `carried`。`CarriedAvatarsAsync` 未删、查询未动，其触发条件依旧是
「这次批里被登记、且不在 survivors 里的时装」，即「一行既有时装被重建」。
变异 M1 与 M2 各自只杀死一条不同的判据，证明两条路都真的通电且互不冒充。

**验收 3**「行为中性」——**达成其实义，计数与契约字面差 +1，已如实标出并提请裁定**。
凭据：改动前后逐项目对照，六个项目计数完全相同，只有 Persistence 从 316 变 317，
增量恰为新增的那 1 条判据；失败 0、跳过仍是同样那 2 条、退出码 0。
生产侧对 `StoredItem.AvatarDetail` 的供给方为零，因此生产行为没有任何可观察的变化。

**验收 4**「三条跨方法耦合记载已按改动后的真实情形更正，且按仓规补记修正案」——**达成**。
凭据：逐条通读了三条。第一条不再成立，已就地更正，并在同一段末尾以
「What this paragraph said before: "..."」原样引出它此前说的话、写明它何时为真、何时起不再为真、
以及为什么是更正而不是删除。第二、三条经核对仍然成立（依据的 `Survivors` 与 INSERT 列清单
本任务一字未动），按 CK-003 的要求**保持原样**。

### 未做的事（边界自查）

- 未给任何生产调用方接线（T-002 的范围）。
- 未动 `Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs`、
  `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`、
  `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs`（T-002/T-005 的范围）。
- 未新增协议帧、未改任何帧的字节布局。
- 未给 `PvfEquipmentSlots.ByName` 补键，未裁定契约兽槽位。
- 未建 Git 检查点、未提交、未使用任何 git 还原类命令。
- 未触碰主检出与 `.worktrees/client-debug-lab`。
- 无阻塞。


## Contract Amendment A-01 — 控制器订正两处写错了的契约字面

由控制器在审阅执行结果时提出并记录。**两处都是契约本身写错了，不是交付的缺陷**；
approved Spec 的边界未变（RQ-002 谈的是「入库之后那一行携带身份与合法细节」，
不谈判据总数，也不谈是否在写入处抛异常）。

### A-01-1：CK-001 的期望值与 CK-002 在算术上互斥

**原文**：「2216 通过 / 2 跳过 / 0 失败，退出码 0；本任务不改变任何既有行为」。

**为什么不成立**：同一份契约的 CK-002 要求本任务**新增**一条判据。
新增一条判据必然让全量计数从 2216 变成 2217，因此 CK-001 与 CK-002
**不可能同时字面成立**。执行者在自查里指出了这一点，没有为迁就字面去删判据，
也没有自行改写契约，处置正确。

**订正后的期望值**：零失败，退出码 0；**既有 2216 条一条不少、一条不红**，
跳过仍是同样那 2 条；增量全部来自本任务新增的判据，并**按项目逐条归因**。

**据此判定**：实测 2217 通过 / 2 跳过 / 0 失败，逐项目 52+60+521+317+6+236+1025，
只有 Persistence 由 316 增至 317，其余六个项目计数**完全不变**——**满足订正后的 CK-001**。

`plan.md` 的 CK-001 已同步订正，以免后续任务继承同一处错误。

### A-01-2：验收条件第 1 条把「必须答」写成了强制检查

**原文**：「读的方向不答，写的方向**对时装必须答**」。

**为什么写强了**：一次**移动**一件已有时装同样走 `Write`，而那时调用方手上本来就
没有那六个事实——它们在库里那一行上。**写入的那一刻无法区分「把一件时装带进世界」
与「搬动一件已经站着的时装」**，所以「必须答」只能是一条**义务**，不能是一道检查。
若在 `Write` 处抛出，一批只是搬动时装的既有判据会立刻变红，
而改它们等于为迁就实现改写既有断言——契约明令禁止。

**订正后的验收条件第 1 条**：`StoredItem` 能承载时装的六个事实；其契约按 `WorldClass`
已有的形状写清**方向**（读不答、写才答），并**就地写明**：这一项对「把时装带进世界」
的写入是义务而非检查，以及为什么不能是检查（结算对这一项有第二读法即回捞，
对 `WorldClass` 没有；写入那一刻分不出两种写）。

**据此判定**：`StoredItem.AvatarDetail` 的 remarks 已逐条写下方向、义务、
以及「不在写入处拒绝」的理由——**满足订正后的验收条件第 1 条**。


## Independent Quick Review R-01 —— 独立快审（fresh context，非执行方）

- **判定：`passed`**
- 评审依据的契约版本：Contract revision 1 **＋ Contract Amendment A-01**（A-01-1 订正 CK-001 的期望值，
  A-01-2 订正验收条件第 1 条）。**按订正后的契约判**。
- 被拥有的 diff 身份：工作区 `server-agrees-what-an-avatar-is`，基线 `565d33fe`，未提交。
  `git status --porcelain` 实测恰为三行 ` M` 加一行 `?? .rks-agent/tasks/server-agrees-what-an-avatar-is/`。
  `git diff --numstat` 实测：
  `IItemDomainStore.cs` 33/0、`SqliteItemDomainStore.cs` 53/10、`SqliteItemDomainStoreTests.cs` 37/0。
- 审阅时 `SqliteItemDomainStore.cs` 的 sha256 = `f820c25890b59188c93b028f0c3d14de24288137b6eebd038ebc0bf3f7b54cdd`，
  与执行者记录的备份散列一致——即本轮审的是执行者跑过的那份树。

### 我自己跑过／做过的事（不采信转述）

| # | 动作 | 实测结果 |
|---|---|---|
| V1 | 全量 CK-001（同一条命令，同一 `PVF_ARCHIVE_PATH`） | 52+521+60+6+317+236+1025 = **2217 通过 / 2 跳过 / 0 失败，退出码 0** |
| V2 | 逐项目与基线计数对照 | 只有 `Dfo.Server.Persistence.Tests` 由 316→317；其余六个项目**逐项目完全相同**；跳过仍是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog` **同样那 2 条** |
| V3 | `grep -rn "AvatarDetail" Server/src` ＋ `grep -rn "new StoredItem\|StoredItem(" Server/src` | `Server/src` 里 `StoredItem` 的构造点共 **3 处，分别是**：`ItemContainers.cs` 的 `new StoredItem(slotIndex, core) { WorldClass = worldClass }`（写入侧，**不给** `AvatarDetail`）、`SqliteItemDomainStore` 的两个读出口（`ReadAllAsync`／`ReadAsync` 各一）。**零处**向 `StoredItem.AvatarDetail` 供给事实。`Server/src` 其余 `AvatarDetail` 命中全部属于 `InventoryItemState`／`InventoryStorageItem`／`AvatarDetailRow`，与 `StoredItem` 无关（计数口径：`Server/src` 下 `.cs` 文件中出现 `new StoredItem` 或 `StoredItem(` 的位置） |
| V4 | `grep -rn "AvatarDetail = " Server/tests` | 目标类型为 `StoredItem` 的赋值 **1 处**，即新判据 `SqliteItemDomainStoreTests.cs` 的 `Worn(9, Avatar()) with { AvatarDetail = detail }`；其余 7 处目标类型分别是 `InventoryItemState` 快照文本与 `InventoryStorageItem` |
| V5 | **我自己的变异 M1′**：把 `FillAsync` 的 `facts` 取值**整体退回改动前的写法**（只留 `carried` 一路，`FactsOf` 以丢弃调用保活以免未使用告警） | `AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates` **单条红**，其余 **316 条全绿** |
| V6 | **我自己的变异 M2′**：保留自带一路，把 `carried` 命中后的取值改成恒 `null`（`? held :` → `? (held is null ? null : null) :`） | `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` **单条红**，新判据**保持绿**，其余 315 条全绿 |
| V7 | 两次变异各自用**文件级备份还原并核散列**（`cp` 备份 ＋ `sha256sum`），**全程未使用任何 git 还原类命令** | 两次还原后散列均为 `f820c258…54cdd`，`git status --porcelain` 与 `git diff --numstat` 与审前逐字相同 |

### 逐条核对（对应控制器点名的七处）

**1. 行为中性——真。** V1/V2 自跑复现。既有 2216 条一条不少、一条不红，跳过仍是同样那 2 条，退出码 0。
V5 更强：把新增的那一路整体撤掉，316 条既有判据**无一变色**，说明新代码的 `else` 分支与改动前的表达式
在全部既有判据上行为等价，行为中性不是"没跑到"，而是"跑到了且相同"。既知偶发的
`ObjectDisposedException: 'SQLitePCL.sqlite3'` **本轮一次未出现**。

**2. 没有给任何生产调用方接线——真。** V3/V4。`Server/src` 零供给点；唯一供给点是新判据本身。

**3. 回捞没有被削弱——真，且两条路各自通电、互不冒充。** V5 只杀新判据（自带一路），
V6 只杀 `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`（回捞一路）。
两次变异**杀死的判据集合不相交**，因此不存在互相冒充。`CarriedAvatarsAsync` 的查询与触发条件
在 diff 中一字未动（diff 的 10 行删除全部是那 6 行旧注释加 4 行旧 `facts` 表达式，无一行 SQL）。

**4. 修正案写得对——是。** 逐条核：
   - **被更正的那一半确实不再成立**：承重词 "only" 已被 `@expire` 的第二个来源打破。
     `FillAsync` 现在 `item.AvatarDetail` 非空即产生非空 `@expire`，而此时 `CarriedAvatarsAsync`
     可以什么都没找到——正是原文断言不可能发生的情形。
   - **仍然成立的那一半没有被误删**：`avatar_expire_date IS NOT NULL` 这条过滤、
     它使返回行首列永不为空、以及"本行不带事实"与"没找到行"在这里是同一个问题——三句原样保留。
     唯一措辞变动是原文 "the guard **silently** starts refusing" 改为 "the guard **still** starts refusing"，
     语义未反转（"still" 承接"这条后果仍然成立"），**"silently" 一词丢失属可接受的轻微弱化，不构成阻塞**。
   - **第二条、第三条确实不需要改**：第二条依赖 `Survivors` 的三趟匹配，第三条依赖 `FillAsync` 的
     INSERT 列清单；两者在 diff 中**一字未动**（INSERT 语句文本零改动，已逐字比对）。
     另核：自带事实的写入落到幸存行时走 `LandAsync`，而 `LandAsync` 不写那六列，
     因此第二条"不需要自己的唯一性守卫"仍成立（幸存行本来就带着同一个 UID）。
   - **别处的记载**：见下面"非阻塞发现"F-2（在本任务的 owned boundary 之外，不能也不该由本任务修）。

**5. 新判据钉住的正是它自称钉住的东西——是，无恒真陷阱。**
   六个期望值 `21 / 22 / 三十个 0x17 字节 / 24 / 25 / 26` **无一等于默认值**：
   库里没有该 UID 的行，六列的默认落值是 NULL，`ReadAvatarDetailAsync` 会返回 `null` 从而
   在 `Assert.NotNull` 处红。半截接线（只绑上几个）也红——`ReadAvatarDetailAsync` 用的是
   `reader.GetInt32(...)`，任一列为 NULL 会直接抛而不是悄悄给零。V5 的实测把这一点坐实：
   撤掉实现，这条判据是唯一变红的那一条。判据名与它证明的东西一致。

**6. 既有断言未被改写——真。** `SqliteItemDomainStoreTests.cs` 的 `--numstat` 是 **37/0**，
   纯新增，零删除、零改写。

**7. 分层与编译约束——合规。**
   `Dfo.Server.Abstractions.csproj` 已有 `ProjectReference` 指向 `Dfo.Server.Data.csproj`（既有，非本次新增），
   而 `Dfo.Server.Data.csproj` **不含任何 `ProjectReference`**——无反向依赖。
   `Server/Directory.Build.props` 的 `TreatWarningsAsErrors` 为 `true`，全解决方案编译通过，
   故 CS9113 等一切告警均未触发（本次未新增任何主构造器参数）。
   `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 所在的
   `Dfo.Server.Application.Tests` 计数 1025 未变且全绿。

### 非阻塞发现（不构成 `changes_required`，留给完整评审／后续任务）

- **F-1（记法，`IItemDomainStore.cs` / `StoredItem.AvatarDetail` 的 `<summary>`）**：
  该行写作「Required on a write that brings an avatar into the world; null on a read, and null on
  the write of anything that is not an avatar」，只列了两种为 null 的情形，**漏掉了 A-01-2 承认的第三种**
  ——搬动一件已经站着的时装时它也**理应**为 null。完整规则写在其上方的 `<para>` 里，
  单读 `<summary>` 的人会得到不完整的规则。属措辞收紧建议，不是错误陈述。
- **F-2（越界，`AGENTS.md`）**：`AGENTS.md` 中「该谓词文本在 `Server/src` 今天出现在 4 处 SQL 里」
  一句用**行号**索引（`SqliteItemDomainStore.cs:414`、`:544`）。实测这两个行号在**基线 565d33fe 上就已经错了**
  （基线实为 438 与 568），本次改动后为 453 与 595。这是**先于本任务已存在的漂移**，
  且 `AGENTS.md` 属 BD-007，**不在 T-001 的 owned boundary 内**，执行者无权也不应在本任务里改。
  记在这里，交给拥有 BD-007 的那个任务一并订正（并顺带把行号索引改成成员名索引）。
- **F-3（覆盖缺口，`SqliteItemDomainStore.FactsOf` 的 `<remarks>`）**：
  该段写「A jewel socket left null arrives here as null and is bound as null」，
  而 `InventoryAvatarDetailState.JewelSocket` 的声明类型是**不可空**的 `byte[]`，
  该防御路径在启用可空引用类型的前提下不可由类型系统到达。属防御性描述，未被任何判据钉住，不影响行为。
- **F-4（覆盖缺口，非本任务欠账）**：修正案新主张的那件事——「一次自带事实的写入使守卫**对新入库的时装**
  开始真的生效」——**今天没有任何判据钉住**（V6 显示守卫只被回捞那一路的
  `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` 覆盖）。T-001 的 Outputs 与 CK-002 均未要求它，
  故不阻塞；但 plan.md 的 BD-002 把「其唯一性守卫因此对新行生效」写成了产出，
  **建议在 T-002 落地供给方时补一条判据**，否则这条主张会以「只写在注释里」的形式留在树上。

### 结论

在本轮受限输入内**未能证明任何可本地修复的阻塞缺陷**：交付满足订正后的 CK-001、CK-002、CK-003
与订正后的四条验收条件；边界未越、无无关改动；既有断言零改写；证据可复现且我已独立复现。
判定 **`passed`**。按快审技能的定义，这只表示本 Task Unit 可进入执行者的接受流程，**不是最终质量批准**；
F-1..F-4 移交完整评审与后续任务处置。


## Controller Disposition D-01 — 接受 R-01 的 `passed`，并分派四条非阻断发现

**判定**：接受。最新的执行结果、执行者自查、快审 R-01 的 `passed` 与被拥有的 diff
（`IItemDomainStore.cs`、`SqliteItemDomainStore.cs`、`SqliteItemDomainStoreTests.cs` 三个文件）
指向同一轮。登记表中 TASK-01 置 `done`。

R-01 的核实是**独立重做**而非采信转述，并且比执行者的自查更强：
它把 `FillAsync` 的 `facts` **整体退回旧写法**，证明新代码的 else 分支与旧表达式
**在全部 316 条既有判据上等价**——这比「只新判据红」多证了一件事。

**四条非阻断发现的去向**，逐条：

| 发现 | 内容 | 去向 | 理由 |
|---|---|---|---|
| F-1 | `StoredItem.AvatarDetail` 的 `<summary>` 漏掉第三种情形（搬动一件已站着的时装时也应为 null），该情形在 `<para>` 里有 | **TASK-03**（经契约修正案把 BD-001 纳入其边界） | 一句话的记载订正，且与 F-3 同族；单独为它开一轮快审不划算 |
| F-2 | `AGENTS.md` 用行号索引那四处谓词，**基线上就已经错**（记 438/568，实为 453/595） | **TASK-07** | 它属 BD-007，本就在 T-007 的边界内；且它正是本仓「按内容索引不写行号」那条规矩自己被违反的实例 |
| F-3 | `FactsOf` 注释里「jewel socket 为 null」在不可空 `byte[]` 下类型上不可达 | **TASK-03** | 落在 BD-002，T-003 已拥有该边界 |
| F-4 | 修正案新主张的「守卫对新入库的时装开始真的生效」今天无判据钉住 | **TASK-03** | 这**正是 T-003 的既定职责**（RQ-004、CK-008、CK-009），不是新增工作，此处只是点名它必须覆盖到这一条主张 |

**F-2 值得单独说一句**：它是**基线就有**的缺陷，不是本轮引入的。
按仓规「改已被证伪的记载要补记修正案」，T-007 处理它时应**订正并说明原文说的是什么**，
而不是默默改数字。
