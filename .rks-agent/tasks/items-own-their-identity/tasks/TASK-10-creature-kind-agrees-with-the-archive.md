# TASK-10 — 契约兽的种类判定与档案一致

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 让契约兽物品的种类判定取档案对同一件物品的解释，并逐条记录因此改变期望值的既有判据。
- Referenced spec requirements: RQ-010
- Referenced plan sections: T-010
- Owned boundary: BD-005, BD-014
- Permitted files or interfaces: 分类规则的生效处、`Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs`、`Server/tests/`
- Dependencies: TASK-09
- Required dependency outputs: 分类规则的单一生效处（TASK-03）与已修好的装回通路
- Acceptance conditions: 向存储与档案分别询问同一件物品的种类，两者一致；因本任务而改变期望值的既有判据被逐条列出，每条归到 RQ-010；**清单之外没有其它期望值改动**；全量判据不低于基线。
- Task-specific authorization: Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做一次。范围外一律不碰：定时清理器、旧库自动升级、地上掉落／交易托管／拍卖挂单／邮件附件的启用、契约兽领域本身、契约兽槽位 24 还是 25 的裁定（沿用 24）、`Pet=7` 容器装什么、契约兽技能、`character_condition` 的五列。
- Required methods: test-driven-development；改动前先让判据表达期望，每个 Task 结束时全量判据全绿，**不允许攒红**。计划里已定、本 Task 不得自行推翻的五条：世界侧**一张**表不是三张；`items` 就地变成世界表且位置另立槽表；
今天的 DELETE+INSERT 结算**原样保留只换作用对象**（物品行 → 槽行），不得为唯一键冲突另发明机制；
契约兽对外标识用**单一窄空间**（i32）加分配守卫，不得另设第二个标识；**不引入新的 `store` 读法**，
世界侧不新增以 `DomainStore` 结尾的类型。
- Required checks:

- CK-021 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → 不低于 2131 通过 / 2 跳过 / 0 失败
- CK-022 `Non-command: 列出因本任务而改变期望值的既有判据，逐条给出理由`

- Required evidence: 种类一致判据名与输出；期望值改动清单（逐条含理由与位置）；全量逐项目计数。

### 如实报告义务

**清单之外不得有其它期望值改动。** 若发现某处既有判据的期望值必须改而无法归到 RQ-010，那说明本轮引入了未授权的行为变化——停下报告，不要顺手改掉它。

**这是被授权的行为变化之二**，须进 TASK-12 的变化清单。

## Scope note — round 1

本轮要改一个**许可清单之外**的文件，先在此写明：

- `Server/src/Dfo.Server.Abstractions/Content/ItemPlacementContent.cs`
  —— **只改注释，不改代码。** 该文件 `ItemWorldClass` 的 `<remarks>` 把
  「a contract creature is stored as kind 5 while the archive's equipment index gives it kind 1」
  写成**当下事实**。RQ-010 落地后这句话为假。留一条已被自己推翻的记载，
  正是本成果反复吃亏的那种「注释漂移到反面」。
  改法是把例子换成**仍然成立**的那一对（时装：存储记 8、档案给 1 —— 本成果已声明不修），
  从而既纠正假陈述，又不削弱「分类不得读存储字节」这条理由本身。

同理由下改的**清单之内**的文件：`IItemPlacementRuleService.cs`、`ItemPlacementRuleService.cs`、
`ItemClassificationTests.cs` 的同一句话。

## Execution Result — round 1

### 改了哪些文件、每处为什么

**生产代码（4 个文件，只有第 1 个是行为变化，其余 3 个是把被自己推翻的记载改回真话）**

1. `Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs`
   —— **本轮唯一的行为变化。** 装备索引分支原先写死
   `new RefillItemMetadata(1, 1, 9, 64, ResolveWorldClass(...))`：**凡在
   `equipment/equipment.lst` 里的模板，档案一律给种类 1**，契约兽也不例外。
   抽出 `WornPieceMetadata(ItemWorldClass)`，让种类由**档案自己已经读出来的
   `[equipment type]`** 决定：`[creature]` → `5`，其余 → `1`。
   堆叠上限 1、槽段 9..64、`WorldClass` 一律不变。
   新增两个具名常量 `WornPieceItemKind = 1`、`CreatureItemKind = 5`，
   并在注释里**明写时装那一对（存储 8、档案 1）仍然不一致、且不属本成果**——
   免得契约兽修好之后读者以为所有种类都对上了。

2. `Server/src/Dfo.Server.Abstractions/Inventory/IItemPlacementRuleService.cs`（许可清单内）
   —— `ClassifyAsync` 的 `<remarks>` 用「契约兽存储记 5、档案给 1」当**现时事实**
   来解释「为什么这里只收模板号」。这句话被本轮变成假的。
   换成**仍然成立**的那一对（时装：存储 8、档案 1），理由本身一字未弱化，
   并补一句「契约兽曾是第二对，现在不是了」。

3. `Server/src/Dfo.Server.Application/Inventory/ItemPlacementRuleService.cs`（许可清单内）
   —— 同一句话的第二处副本，同样改法。

4. `Server/src/Dfo.Server.Abstractions/Content/ItemPlacementContent.cs`
   —— **许可清单之外，只改注释不改代码**，理由见上面的 `Scope note — round 1`。
   `ItemWorldClass` 的 `<remarks>` 是这句话的第三处副本。

**判据（1 改 1 新）**

5. `Server/tests/Dfo.Server.Application.Tests/Inventory/ItemClassificationTests.cs`
   —— 期望值改动，见下面的清单第 1 条。

6. `Server/tests/Dfo.Server.Application.Tests/Inventory/CreatureKindAgreesWithTheArchiveTests.cs`
   —— **新增**，AC-012 / EV-008 的判据。

**没有改任何别的文件。** 字节锚 `SelectCharacterAcceptedTranscriptTests.cs`
`git status --porcelain` 为空，一字未动。

### CK-021

命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

逐项目（照抄 runner 每行末尾的 dll 名，逐个相加，没有用 `grep -c`）：

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 311 | 0 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |
| **合计** | **2211** | **2** | **0** |

60+52=112；+521=633；+6=639；+236=875；+311=1186；+1025=**2211**。
基线 2210 / 2 / 0 → 本轮 **2211 / 2 / 0**，多出来的正是新增的那一条。

字节锚单跑：
`dotnet test Server/tests/Dfo.Server.Protocol.Tests --filter "FullyQualifiedName~SelectCharacterAcceptedTranscriptTests|FullyQualifiedName~InventoryDeltaLift"`
→ **19 通过 / 0 失败 / 0 跳过**。锚是绿的，种类改变没有穿到入场帧上。

### 种类一致的证据（AC-012 / EV-008）

判据名：
`Dfo.Server.Application.Tests.Inventory.CreatureKindAgreesWithTheArchiveTests.The_archive_and_storage_answer_the_same_kind_for_one_contract_creature`

同一件物品是 `63000`（`equipment/creature/faras.equ`，`[equipment type]` = `[creature]`）。
不打桩：档案是运营方自己的 `Script.pvf`（经生产的 `InfrastructureModule` 注册），
存储是真的 SQLite 库。三次询问：

1. **向档案问**：`PlacementForAsync(63000).ItemKind`，且 `ClassifyAsync(63000) == Creature`；
2. **向存储问**：经生产通路 `PersistentItemContainer.AddAsync(63000, 1)` 落库后
   把那一行读回来，断言 `stored.Core.ItemKind == fromTheArchive.ItemKind`；
3. **两者对上的那个数正是入场通路读契约兽的那个数**：把读回的 core 摆在装备容器 24 槽，
   交给 `EquippedCreatureQuery`（生产类型，私有常量 `CreatureItemKind = 5`），
   断言它认出 `63000`。

第 3 条是承重的那条：第 2 条单独看会被 `AddAsync` 写的就是 `placement.ItemKind`
这一点弄成恒真，第 3 条把「存储侧读契约兽用的是哪个数」这个**独立事实**接了进来。

**变异验证（文件级备份，未用任何 git 还原命令）**：
`cp` 到 scratchpad → 把 `worldClass == ItemWorldClass.Creature ? CreatureItemKind : WornPieceItemKind`
改回 `WornPieceItemKind` → 跑该判据 →

- `CreatureKindAgreesWithTheArchiveTests...` **FAIL**，`Assert.Equal() Expected: 63000 Actual: 0`
  （第 3 条断言：24 槽里站着一件种类 1 的东西，读契约兽的那一处根本看不见它）
- `ItemClassificationTests.The_archive_reads_the_contract_creature_as_a_creature_and_not_as_the_row_does`
  **FAIL**，`Expected: 5 Actual: 1`

→ `cp` 回来 → `sha256sum` 与改前一致：
`0db507d34eabcc652c217f50b1520d26aeab06d5b3fdb2c4da4bb36ef13094d6`。

### CK-022 —— 因本任务而改变期望值的既有判据（逐条）

**只有一条。**

1. **位置**：`Server/tests/Dfo.Server.Application.Tests/Inventory/ItemClassificationTests.cs`，
   `The_archive_reads_the_contract_creature_as_a_creature_and_not_as_the_row_does`，
   原 `Assert.Equal(EquipmentKindByte, placement!.ItemKind);`（`EquipmentKindByte = 1`）
   → 现 `Assert.Equal(CreatureKindByte, placement!.ItemKind);`（新增常量 `CreatureKindByte = 5`）。
   **理由**：这一条断的就是「档案给契约兽的种类号」，正是 RQ-010 要改的那个数本身。
   该判据由 TASK-03 写下时，其 `<remarks>` 已明写
   「Nothing here proposes changing what is stored; what the creature's kind byte ought to be is
   **another outcome's question**」——它当时就把这个数留给了本 Task。**归到 RQ-010。**
   同文件内被改的另外两处是 `<summary>`/`<remarks>` 文字（把已成假的「档案与字节不一致」
   的例子从契约兽换成时装），**不是断言**。该判据其余断言（`ClassifyAsync` 给
   `Creature` / `Interchangeable`；写入时不看行上的字节）**一字未动**，
   `EquipmentKindByte = 1` 也保留，因为它在第二个用例里仍表示「装备索引给普通穿戴件的种类」。

**清单到此为止。** 全量 2211 / 2 / 0 里没有第二条判据的期望值因本轮而改，
也没有出现「必须改期望值却归不到 RQ-010」的情况。

### 顺带查明、但没有产生任何期望值改动的下游

档案给契约兽的种类由 1 变 5，会流到三个读 `placement.ItemKind` 的地方。逐个查过：

- `ItemArrivalRuleService.BandsFor`：种类 1 走 `placement.ItemKind == EquipmentKind` 分支，
  种类 5 走 `!AllowsQuickSlot(5)` 分支，**两条分支产出完全相同**（只 yield 自己的槽段）。无变化。
- `ItemSlotRuleService.CanOccupyMainSlot`：**这里确实变了。** 契约兽原先（种类 1）可以被拖进
  快捷栏 3..8，现在不行。这与该文件自己的注释
  「Creatures and what they wear or eat may not occupy one at all」一致，
  是 RQ-010 的直接推论。**没有任何既有判据覆盖这个情形**，所以它没有产生期望值改动；
  在此写明，供 TASK-12 的变化清单收录。
- `StoreBackedItemContainer.AddAsync` 写入的种类字节：由 1 变 5 ——
  **这正是 RQ-010 授权的那处变化本身**（存储与档案对上）。

### 没有做的（范围外，一处未碰）

契约兽领域本身、24 还是 25、`Pet=7`、契约兽技能、`character_condition` 五列、
定时清理器、旧库升级、地上掉落／交易／拍卖／邮件、T-011 的迁移与退役、T-012 的记载。
**时装「存储 8 / 档案 1」的同类不一致明知而不修**，只在注释里点名，未动一行代码。

## Executor Self-check — round 1

### 我发现的问题

1. **「两者一致」这句话有一个恒真陷阱，我绕开了但要说清。**
   `AddAsync` 写进存储的就是 `placement.ItemKind`，所以「读回来的字节 == 档案给的字节」
   这一条**单独看永远为真**，修不修都绿。变异验证的输出正好证实了这点：
   把修复撤掉之后，失败的是第 3 条断言（行 104）而不是第 2 条。
   真正承重的是**「两者对上的数正是 `EquippedCreatureQuery` 读契约兽用的那个 5」**。
   我把这条写进了判据，也写进了判据自己的 `<remarks>`。
2. **同一句已被推翻的记载在仓里有三处副本**（`ItemPlacementContent.cs`、
   `IItemPlacementRuleService.cs`、`ItemPlacementRuleService.cs`），其中一处在许可清单外。
   我按 Scope note 只改注释。**若控制器认为清单外那处也不该动，把它改回即可，
   代价是留一条自知为假的记载。**
3. **快捷栏那处推论没有判据守着**（见上）。我没有新增判据去钉它——
   本 Task 的判据义务是 AC-012，新增一条「契约兽不许进快捷栏」属于超出授权范围地扩大成果。
   如实报出，请 TASK-12 收进变化清单。

### 我**没有**验证的东西（明说）

- **没有跑真实客户端**。「客户端看到种类 5 是对的」这件事我是从**服务端自身的三处读法**
  （`EquippedCreatureQuery`、`SelectCharacterPreparationService:448`、
  `SelectCharacterPostEntryServices` 的 `CreatureItemKind = 5`）推出来的，
  不是从线上抓包推出来的。若这三处本身就错，本轮是把档案对齐到了一个错的数上——
  但那样的话今天线上的契约兽根本不会被认出来，所以我认为这个风险很低。
- **没有验证「今天线上真的有契约兽经 `AddAsync` 到手」**。若没有任何通路给玩家发契约兽，
  这处行为变化在今天的线上是**不可观察**的（只影响未来的入库与快捷栏判定）。
  我查了 `AddAsync` 的调用方但**没有逐个追到底**是否有契约兽模板会走到那里。
- **没有对 `PvfEntryLifecycleContentSource` 做全档案扫描**去数「有多少模板的种类由 1 变成了 5」。
  按 `ClassifyEquipmentType` 的实现，变的只有 `[equipment type]` 恰为 `[creature]` 的那些
  （`WorldContentArchiveTests:143` 的注释说 63000 起有 745 条 creature-equipment 条目，
  但那是**契约兽装备**不是契约兽本身，我没有分别数过两者）。
- **没有跑第二遍全量**去排除偶发。全量只跑了两次（改完一次、变异复原后一次），
  两次都是 2211 / 2 / 0，没有出现过任何失败，所以没有触发「异常类型 + 栈根 + 复跑即绿」的判断。

### 不确定的地方

- **方向的裁定**：RQ-010 的字面是「种类判定取**档案**的解释」。我把它读成
  「档案已经说了它是 `[creature]`，那么它的种类号就该是表示 creature 的那个数（5）」，
  于是**改的是档案侧的读法，存储侧一个字节都没动**。
  另一种读法是「把存储改成档案今天给的 1」——那会让 24 槽的契约兽在入场时读不出来，
  与 AC-012 的「两者一致」虽也相符，却明显违背 RQ-011。我认为不存在真正的歧义，
  但这是本轮唯一的裁量点，写在这里备查。
- 我把 `CreatureItemKind = 5` 放在 Infrastructure 而不是与 `ItemWorldClass` 并列在
  Abstractions。放 Abstractions 会更「一处定义」，但那要动许可清单外文件的**代码**
  （并牵动 Application 里三处私有常量的改写），超出本 Task 授权，故没做。

## Controller Disposition — DSP-TASK-10-01

执行方交回两件。控制器**在快审之前**裁定。

### 一、快捷栏那条推论**是第二处可观察行为变化**，必须进变化清单

执行方发现：种类由 1 变 5 后，契约兽**不再能被拖进快捷栏 3..8**。它把这条报成「推论、无判据守着」。

**控制器复核并把定性说准**：`ItemSlotRuleService.cs:39` 是 `AllowsQuickSlot(placement.ItemKind)`，
而 `placement.ItemKind` 正是档案给的那个数；`:51-53` 把 `KindCreature`（5）排除在外。
**档案原先给 1 → 契约兽能进快捷栏；现在给 5 → 被排除。**

**这不是「推论」，是一处真实的可观察行为变化**——只是没有既有判据覆盖它，所以没产生期望值改动。
而且**新行为与 `ItemSlotRuleService` 自己的注释一致**，旧行为才是档案与存储不一致的产物。

→ **它必须作为 RQ-010 的第二条列进 T-012 的行为变化清单**，
   不得因为「没有判据改动」就漏掉。**清单记的是变化，不是只记判据改动。**

### 二、恒真陷阱被绕开了，这一点记下来

执行方指出：`AddAsync` 写进存储的**就是** `placement.ItemKind`，
所以「读回的字节 == 档案给的字节」**单独看永远绿**。承重的是第三条断言——
两者对上的那个数**正是 `EquippedCreatureQuery` 读契约兽用的 `5`**。
变异证实：撤掉修复后失败的正是这条（`Expected: 63000 / Actual: 0`），另一条报 `Expected: 5 / Actual: 1`。

**自己识别出恒真陷阱并绕开，是这一轮最该被记住的做法。**

### 三、时装那一对明知不修——正确

存储 8 / 档案 1 的不一致仍在，执行方只在注释里点名、未动。
**RQ-010 只授权了契约兽这一处**，动时装是未授权的行为变化。
→ 与「时装／宝珠取得下装不上」并列，**写进 T-012 作为已知的同类缺陷**。

### 四、越清单改的那一处注释——认可

`ItemPlacementContent.cs` 在允许清单外，执行方**先在 Task 文件写了 Scope note 才动**，
改的是被本轮变成假的那句记载（「契约兽存储 5 / 档案 1」），换成仍成立的时装那一对，理由未弱化。
**这正是前面几轮反复要求的做法。** 认可。

## Independent Quick Review — QR-TASK-10-01

- **Verdict: `passed`**
- 评审的合同修订：Contract revision 1 + `Scope note — round 1` + 控制器裁定 `DSP-TASK-10-01`（按裁定后的口径）
- 新鲜上下文，未读执行方之外的评审方法学；结论全部自跑自读，未照抄执行方或控制器的说法。

### Owned diff identity

工作区 `git status --porcelain` 共 70 条（TASK-02..09 的成果全在里面，**不属本轮**）。HEAD = `51cd99c2`。
本轮认领的 6 个文件：

- 四个已跟踪文件的 `git diff` 合并 sha256 = `21a447473474b75ce722283dd45af3ddd288b85a60cf5860440c3f32eee794d5`
- `ItemClassificationTests.cs` sha256 = `6c64cd45a76a730a679cbc469389b1c1f84f45789fab89b4e596816a9c03d3ec`
  （TASK-03 快审记的是 `035e5f6a…`，**本轮确实改了它**，与 CK-022 的自述相符）
- `CreatureKindAgreesWithTheArchiveTests.cs` sha256 = `11193b8a777152065ffdf4994f330ed28192959e927e81a19ac1f9c8b4e43272`
- `SelectCharacterAcceptedTranscriptTests.cs`：`git status --porcelain` 空、`git diff --stat` 无输出

### 我跑过／读过的东西

| # | 做的事 | 结果 |
|---|---|---|
| 1 | CK-021 全量（自跑） | **2211 / 2 / 0**，逐项目见下 |
| 2 | 变异（文件级备份，未用任何 git 还原命令） | 复现，见「三」 |
| 3 | 复原后全量复跑 | 2211 / 2 / 0 |
| 4 | 字节锚单跑 | **19 通过 / 0 失败 / 0 跳过** |
| 5 | 六个文件首三字节 | 无一为 `ef bb bf` |

CK-021 逐项目（照抄 runner 每行，未用 `grep -c`）：
Dfo.Pvf.Tests 60/0/0；Dfo.Server.Networking.Tests 52/0/0；Dfo.Server.Protocol.Tests 521/0/0；
Dfo.Server.Host.Tests 6/0/0；Dfo.Server.Infrastructure.Tests 236/**2**/0；
Dfo.Server.Persistence.Tests 311/0/0；Dfo.Server.Application.Tests 1025/0/0。
`60+52=112; +521=633; +6=639; +236=875; +311=1186; +1025=2211`。
与 TASK-09 已通过的基线（该 Task 文件 `:101`／`:427` 记 **2210 / 2 / 0**，Application.Tests 为 1024）
**逐项目相同，只有 Application.Tests 1024→1025**。这条数字本身就是「没有判据被删掉或放宽」的旁证。

### 一、改动的实质（核实：其余增量不是隐藏的行为改动）

`PvfEntryLifecycleContentSource.cs` 的 `git diff` 确为 **+123 / -8**，但**这不全是本轮的**。
`ReadEquipmentPaths`、`ResolveWorldClass`、`ClassifyEquipmentType`、`RefillItemMetadata` 的第五个
字段 `WorldClass`、装备索引由 `IReadOnlySet<int>` 改字典——**全部出自 TASK-03**
（见 `TASK-03-…md:99`：「新增 `ReadEquipmentPaths`、`ResolveWorldClass`、`ClassifyEquipmentType`」）。
HEAD 是 `51cd99c2`、十个任务都没提交，所以 `git diff` 把它们一并算了进来。

本轮真正拥有的只有三块，逐行核对如下：

1. `:20-29` 两个具名常量（`WornPieceItemKind = 1`、`CreatureItemKind = 5`）加各自注释，**共 10 行**。
   佐证：TASK-06 记 `AvatarEquipmentSuffix` 常量在 `:25`、注释在 `:21-24`（`TASK-06-…md:164`、`:475`），
   现在分别是 `:35` 与 `:31-34`，**整体位移正好 +10**，即纯插入、没有改写既有行。
2. `:141` 调用点：`new RefillItemMetadata(1, 1, 9, 64, ResolveWorldClass(…))`
   → `WornPieceMetadata(ResolveWorldClass(archive, equipmentPath))`。
3. `:166-193` 新的 `WornPieceMetadata` 及其注释。

**堆叠上限／槽段／`WorldClass` 是否真的全不变——是。** `:188-193` 展开后为
`new(1, worldClass == Creature ? 5 : 1, 9, 64, worldClass)`：
堆叠上限 `1`、`SlotStart = 9`、`SlotEnd = 64`、第五参数原样透传 `ResolveWorldClass` 的结果，
四项与本轮之前逐字相同，**唯一变的是第二个参数**。
且只对 `Creature` 变：`Avatar` 仍走 `WornPieceItemKind = 1`（`:190`）；
文件不解析／无 `[equipment type]` 的模板由 `ResolveWorldClass`（`:211-216`）给 `Equipment` → 仍是 1。
`ClassifyEquipmentType`（`:230-238`）本体一字未动（TASK-06 记的三个出口原样在）。

另三个文件（`IItemPlacementRuleService.cs`、`ItemPlacementRuleService.cs`、`ItemPlacementContent.cs`）
我逐 hunk 看过：**代码全是 TASK-03 的**，本轮只有注释里「契约兽 5/1」换成「时装 8/1」
并补一句「契约兽曾是第二对」。**Scope note 的自我约束属实。**

### 二、恒真陷阱：说法成立，断言里只有一条有判别力

我自己读了写入通路：`StoreBackedItemContainer.AddAsync`（`ItemContainers.cs:296-307`）取
`placement.ItemKind` 交给 `LandAsync`，后者经 `StackableItemCore.Create(itemKind, …)`
（`ItemContainers.cs:31-39`）把它原样放进 `Core.ItemKind`。
所以 `CreatureKindAgreesWithTheArchiveTests.cs:101` 的
`Assert.Equal(fromTheArchive.ItemKind, stored.Core.ItemKind)` **对任何取值都成立**——
它钉的是「写入通路不改种类字节」，钉不住「那个数是几」。**执行方的自述属实。**

判别力盘点（我的独立判断）：

- `:66` `NotNull`、`:98-99` `NotNull` / `ItemId` —— 前置，无判别力；
- `:67-69` `ClassifyAsync == Creature` —— **是 TASK-03 的行为**，本轮之前就绿，对 RQ-010 无判别力
  （只是防同向失效的护栏）；
- `:101` —— 恒真（见上）；
- `:104-106` `EquippedCreatureQuery().GetItemId(...) == 63000` —— **唯一承重的一条**。
  它把「存储侧读契约兽用的是哪个数」当独立事实接了进来：`EquippedCreatureQuery`
  （`InventoryEntryServices.cs:342`／`:355`）的私有常量是 `5`，与被测代码不共享任何符号。

跨文件看，本轮另有 `ItemClassificationTests.cs:78` 一条有判别力。**合计两条。**

### 三、变异我自己重做了一遍

`cp` 到 scratchpad（`sha256 = 0db507d34eabcc652c217f50b1520d26aeab06d5b3fdb2c4da4bb36ef13094d6`，
与执行方所记一致）→ 用 python 二进制替换把 `:190` 改回 `WornPieceItemKind,` → 单跑两个判据类：

```
[FAIL] Dfo.Server.Application.Tests.Inventory.CreatureKindAgreesWithTheArchiveTests
       .The_archive_and_storage_answer_the_same_kind_for_one_contract_creature
  Assert.Equal() Failure: Values differ  Expected: 63000  Actual: 0
  at ...\CreatureKindAgreesWithTheArchiveTests.cs:line 104
[FAIL] Dfo.Server.Application.Tests.Inventory.ItemClassificationTests
       .The_archive_reads_the_contract_creature_as_a_creature_and_not_as_the_row_does
  Assert.Equal() Failure: Values differ  Expected: 5  Actual: 1
  at ...\ItemClassificationTests.cs:line 78
失败! - 失败: 2，通过: 1，已跳过: 0，总计: 3
```

**失败的正是 `:104` 而不是 `:101`**——恒真陷阱的论断由此得到独立证实；
`A_write_classifies_the_template_the_archive_declares_not_the_kind_the_row_carries` 仍绿（它不看这个数）。
`cp` 回来后 `sha256` 仍为 `0db507d3…`，`cmp` 报逐字节一致；全量复跑回到 **2211 / 2 / 0**。
**全程未使用 `checkout --` / `restore` / `reset` / `clean` / `stash`。**

### 四、CK-022 的清单：完整，但有一句引文无法逐字核实

**清单之外没有其它期望值改动——核实通过。** 依据两条：

1. 逐项目计数与 TASK-09 基线**每一项相同，只有 Application.Tests +1**（新增判据本身）。
   若别处有判据被改期望值而没报，计数不会这么干净；若有判据被删，某个项目会掉数。
2. 我把可能受影响的既有判据挨个看过：
   `PvfEntryLifecycleContentSourceTests.cs` 的 `git diff` 里只有 TASK-03 的两处补参数
   与 TASK-06 的那条 10 行 `[Theory]`，**没有本轮的痕迹**；
   `ItemClassificationTests.cs` 里 `EquipmentKindByte = 1` 仍在 `:46`，仍被 `:124` / `:132` 使用；
   其余提到 `63000` 的判据（`CreatureGoesBackOnTests.cs:42`／`:207` 自带硬编码 5、
   `WorldContentArchiveTests.cs:157`、`WorldContentRuleTests.cs:512`）都不断种类字节，不受影响。

**一句未查清（逐字）**：执行方称该判据由 TASK-03 写下时其 `<remarks>` 已明写
「what the creature's kind byte ought to be is **another outcome's question**」。
`ItemClassificationTests.cs` 是未跟踪文件、没有 git 历史，全部 Task 文档里也没有任何一处复现这句话
（只有本 Task 文件 `:153-154` 自己在引用），**所以这句引文我无法逐字证实**。
但它的**实质有独立旁证**：`TASK-03-…md:129-131` 白纸黑字记着该判据当时断的是
「模板 `63000`…的档案种类字节是 `1`……**先钉住「不一致」确实存在**」——
TASK-03 记录的是不一致，不是主张修它。**归到 RQ-010 因此成立**；
这条不构成缺陷，只把「引文不可核」记在案。

### 五、快捷栏那条之外，我自己又找了一遍下游

我在 `Server/src` 全仓枚举了 `placement.ItemKind` 的**每一个**读者，**只有三处**：

- `ItemArrivalRuleService.cs:121-122`：种类 1 命中 `== EquipmentKind`；种类 5 命中
  `!AllowsQuickSlot(5)`（`ItemSlotRuleService.cs:52-53` 把 `KindCreature = 5` 排除）。
  **两条分支落进同一段 `yield return own; yield break;`（`:123-125`），产出确实完全相同。** 无变化。
- `ItemSlotRuleService.cs:39`：**变了**，即 `DSP-TASK-10-01` 裁定的第二处行为变化。
  补一条控制器未点出的细节：`CanOccupyMainSlot` 有**三个**调用点——
  `ItemContainers.cs:466`（拖动目标格）、`ItemContainers.cs:507`（交换时被顶开那件的落点）、
  `ItemMovementDomainService.cs:21`。三处是同一个行为的三个入口，**不是三条变化**。
- `ItemContainers.cs:307`：写进存储的种类字节，即 RQ-010 授权的变化本身。

**没有找到第三处独立的下游后果。** 两件相关的事记在这里供 T-012 参考，都不构成清单遗漏：

1. **执行方漏报了一条代码路径（后果为空）**：被改的 `ResolveItemMetadata` 还有第二个消费者——
   `GetDailyRefillRulesAsync`（`PvfEntryLifecycleContentSource.cs:44-46`）→ `DailyRefillRule.ItemKind`
   → `DailyRefillService.cs:130` → `ItemContainers.cs:322` 的 `RefillAsync` 写入。
   执行方的下游清单只提了 `AddAsync`。**我查了，这条路事实上是空的**：既有判据
   `PvfEntryLifecycleContentSourceTests.DailyRefill_RealArchiveProducesAuthoritativeRulesWithStackLimits`
   （`:97-107`）把真档案的**全部六条**补给规则钉死为 `ItemKind = 4`（全是可堆叠物），
   没有一条走装备分支。所以没有行为变化，也没有期望值改动，而且这条判据本身就守着它。
2. **「存储字节 1→5」这条已列变化的下游延伸**：契约兽以 5 入库后再穿到 24 槽，
   `SelectCharacterPreparationService.cs:448`（`SlotIndex == 24 && ItemKind == 5`）会给它挂上
   `Noti2CreatureMarker`，`StateSynchronizationPayloadCodec.Entry.cs:200-206` 于是写 marker
   而不再写 `core.Marker16` 兜底；`SelectCharacterPetEntryService.cs:117` 的复活也才够得着。
   我特意查了会不会因此多出一个「marker 缺失就抛 `Missing`」的新失败模式——
   **不会**：挂 marker 与写 marker 用的是同一个 `slot 24 && kind 5` 判据，两侧同进同退。
   这是本次修复**要的效果**，不是第三处独立变化。

### 六、其余逐项

- **字节锚未动**：`SelectCharacterAcceptedTranscriptTests.cs` 的 `git status --porcelain` 为空、
  `git diff --stat` 无输出；单跑该类 + `InventoryDeltaLift` = **19 通过 / 0 失败 / 0 跳过**。
  没有出现「锚红了去 rebase 哈希」的情况。
- **TASK-03 立的形状未被动过**：`IItemPlacementRuleService.cs:50` 与
  `ItemPlacementRuleService.cs:45-46` 的 `ClassifyAsync(int itemId, CancellationToken cancellationToken)`
  签名仍**只收模板号**，本轮对这两个文件的改动全是注释。**不构成阻断。**
- **BOM**：六个文件首三字节分别为 `757369`（`usi`）×5 与 `6e616d`（`nam`），**无一为 `ef bb bf`**。
- **偶发失败**：本轮我跑的两次全量与两次单跑中，除刻意变异外**零失败**，
  没有需要按「异常类型 + 栈根 + 复跑即绿」归因的情形。
- **时装那一对未修**：按 `DSP-TASK-10-01` 第三条不计缺陷；越清单改 `ItemPlacementContent.cs`
  一处注释按第四条不计缺陷。我核过它确实只改注释、未动一行代码。

### 结论

CK-021 自跑达标且高于基线，CK-022 的清单经数字与逐文件两路核实为完整，
本轮唯一的行为改动点经我自己的变异确认被有判别力的断言守住，
字节锚、分类唯一生效处的签名、BOM 三项均无问题。
**无阻断级缺陷，`passed`。**
需要 T-012 收进变化清单的仍是两条（`DSP-TASK-10-01` 已定的快捷栏排除 + 存储种类字节 1→5）；
本轮新增的备查两条见「五」，不改变变化清单的条数。

## Controller Disposition — DSP-TASK-10-02

`QR-TASK-10-01` 给出 `passed`。控制器采纳，记三条。

### 一、快审拆开了一个会误导后来者的 diff 计数

`PvfEntryLifecycleContentSource.cs` 的 `+123/-8` **不全是本轮的**——
十个任务全部未提交，`git diff` 把 TASK-03 的 `ReadEquipmentPaths` / `ResolveWorldClass` /
`ClassifyEquipmentType` / `RefillItemMetadata.WorldClass` 一并算了进来。
**本轮真正拥有的只有三块**：两个常量、一处调用点、`WornPieceMetadata`。
`ClassifyEquipmentType` 本体一字未动；堆叠上限 1、槽段 9..64、`WorldClass` 透传三项逐字未变。

→ **这个现象会贯穿剩余任务**（工作区累积未提交）。
   **写进 T-012 的成果记载**：读任一任务的 diff 时不能按整文件计数，要按块归属。

### 二、恒真陷阱经变异独立证实

快审复现变异后，`:101` **没有失败**——**这恰好证明那条断言是空的**。
三条断言里只有 `:104-106` 有判别力（`EquippedCreatureQuery` 的私有 `5` 与被测代码不共享符号），
跨文件另有 `ItemClassificationTests.cs:78`，合计两条。

**用变异证明一条断言是空的，比用它证明另一条是实的更少见。** 记在这里。

### 三、一处诚实的「未查清」，采纳不追

快审无法逐字证实 TASK-03 那句「契约兽的种类字节属另一个成果的问题」的注释
（该文件未跟踪、无 git 历史）。**它没有为了交差把旁证说成直证**，而是标了未查清并给出旁证
（`TASK-03-…md:129-131`）。归属 RQ-010 成立，不计缺陷。

另：快审补查出执行方漏报的一条下游路径（`GetDailyRefillRulesAsync → DailyRefillService.cs:130`），
但**事实为空**——既有判据把真档案全部六条补给规则钉死为 kind 4。**漏报无后果，记录在案。**
