# TASK-04 — 结算写两张表，世界行不因移动而销毁

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 结算时按分类决定：有身份的物品在世界表落一行并由槽行引用；无身份的东西只落槽行。位置改变只重建槽行；世界行从生到灭只被创建一次、删除一次。
- Referenced spec requirements: RQ-001, RQ-002
- Referenced plan sections: T-004
- Owned boundary: BD-006
- Permitted files or interfaces: `Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs`、`Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`、`Server/tests/`
- Dependencies: TASK-03
- Required dependency outputs: 分类规则的单一生效处
- Acceptance conditions: 移动路径不删除也不重建任何世界行；存在判据证明同一件有身份物品在移动前后身份值不变；全量判据不低于基线。
- Task-specific authorization: Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做一次。范围外一律不碰：定时清理器、旧库自动升级、地上掉落／交易托管／拍卖挂单／邮件附件的启用、契约兽领域本身、契约兽槽位 24 还是 25 的裁定（沿用 24）、`Pet=7` 容器装什么、契约兽技能、`character_condition` 的五列。
- Required methods: test-driven-development；改动前先让判据表达期望，每个 Task 结束时全量判据全绿，**不允许攒红**。计划里已定、本 Task 不得自行推翻的五条：世界侧**一张**表不是三张；`items` 就地变成世界表且位置另立槽表；
今天的 DELETE+INSERT 结算**原样保留只换作用对象**（物品行 → 槽行），不得为唯一键冲突另发明机制；
契约兽对外标识用**单一窄空间**（i32）加分配守卫，不得另设第二个标识；**不引入新的 `store` 读法**，
世界侧不新增以 `DomainStore` 结尾的类型。
- Required checks:

- CK-009 `Non-command: 检查移动路径不删除也不重建任何世界行`
- CK-010 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → 不低于 2131 通过 / 2 跳过 / 0 失败

- Required evidence: 结算实现的 diff；身份不变判据名与输出；全量逐项目计数。

### 保留条件

今天「谓词与唯一键同名」的清理办法**原样保留**，只是作用对象变成槽行——槽行不再承载身份与载荷，销毁重建它不损失任何东西。**不要为唯一键冲突另发明机制。**

## Execution Result — round 1

### 改了哪些文件、每处为什么

**允许边界内的两个 src 文件，没有第三个。**
`git diff --stat -- Server/src` 里除本轮这两个之外的 11 处全部是 TASK-01～03 未提交的既有改动，
行数与它们各自的记录逐项相同。

| 文件 | 改了什么 | 为什么 |
|---|---|---|
| `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs` | 结算重写：`FlushAsync` 由「清空→填充」两段变成「读被顶掉的行 → 抬起幸存行 → 清空 → 落下/新建 + 写槽行」四段 | 世界行不因移动而销毁的全部实现都在这里 |
| `Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs` | `Write` 的语义与 `StoredItem.WorldClass` 的注释改写为「写必须回答，读可以不回答」 | `WorldClass` 从此有真正的消费者，不再是「填了没人读」 |

**结算的几段，逐段的理由**

1. `DisplacedAsync` —— 先把这一批要动的每个位置上**现在站着的行**读出来（`item_uid` + 82 字节 core）。
   之后每一步都会改动它们，所以只能先读。
2. `Survivors` —— 决定哪一行是「这一批正在放下的某件东西**本身**」。三趟，窄的先走：
   ① 原位且 core 逐字节相同；② 任意被顶掉的行中 core 逐字节相同（这一趟接住移动／穿脱／换容器）；
   ③ 原位且模板号、种类字节、Value 三者相同（接住「原地改写载荷」）。
   **顺序是有理由的**：若第三趟先走，同模板两件互换位置时会互换身份。
   `WorldClass == Interchangeable` 的写**永远不进这个集合** —— 两份完全可互换，行上没有值得留的东西。
3. `LiftAsync` —— 每个幸存行先被 `UPDATE` 到 `list_type = -1, slot_index = item_uid`。
   这不是新发明的机制，是**同一套「先清空所有点名的位置，再填」**：只是必须活下来的那一行不是被删掉，
   而是被抬起来。抬到的位置以行自己的身份为槽号，两行不可能撞上；换位（cycle）因此不需要任何停车位。
4. `EmptyAsync` 原样保留 —— 谓词仍与唯一键同名，只是现在**同时删 `items` 与 `item_slots` 的那一行**；
   幸存行已经不在那儿了，所以它删掉的只是这一批真正要丢弃的东西。
5. `LandAsync` / `FillAsync` / `HoldAsync` —— 幸存行 `UPDATE` 回落（**只改位置四列与 core，不碰身份、
   不碰 `created_at`、不碰时装六列与契约兽八列**）；非幸存的走原来的 `INSERT`，加 `RETURNING item_uid`
   把身份取回来。两者之后都写一条 `item_slots`：有身份的 `kind='world', id=身份, count=1`，
   无身份的 `kind='template', id=模板号, count=数量`。

### CK-009 `Non-command: 检查移动路径不删除也不重建任何世界行`

**移动只触及槽行。世界行在移动中只被 `UPDATE`（抬起 + 回落），没有任何 `DELETE` 或 `INSERT` 到达它。**

证据不是读代码得出的，是钉在判据里的：`item_uid` 是 `INTEGER PRIMARY KEY AUTOINCREMENT`，
**发出去的号永不重用**，所以 `sqlite_sequence` 里 `items` 的水位线就是「至今为止插入过几行」的账本。

- `AMoveNeitherDeletesNorBuildsAWorldRow`：移动前后 `SELECT item_uid FROM items ORDER BY item_uid`
  的**整个集合**相同，且水位线不变，于是既没删也没建。
- `AWornPieceKeepsOneIdentityThroughAMoveAWearAndATakeOff`：连做三次移动（同容器换槽 → 装进
  Equipment → 脱回背包），每次之后从库里**重新读**身份，三次都等于最初那个数，水位线三次都不动；
  同时断言旧位置的槽行没了、新位置的槽行是 `('world', 身份, 1)`。
- `TwoWornPiecesSwappingPositionsEachKeepTheirOwnIdentity`：互换（cycle）后两个身份**各自跟着自己那件**
  到了对方的槽，水位线不动。这条同时钉住第 2 步三趟的顺序。
- `AContractCreatureTakenOffIntoTheBagIsStillTheSameCreature`：契约兽八列先写在世界行上，
  从装备槽取下放进背包之后身份不变、`creature_progress_value` 仍是 4242（AC-003）。
- `AnAvatarsOwnColumnsStayWhereTheyAreThroughAMove`：时装列**根本没被搬运** —— 行没走，列就在那儿。

**生产上的移动路径确实是一个批**（当场核过，不是推断）：
`ItemContainers.cs:558` 的 `ApplyDecidedWritesAsync` 每个**动词**调一次而不是每次写调一次；
`MoveItemSpaceSlice.PutOnAsync` / `TakeOffAsync`（`MoveItemSpaceSlice.cs:332-343` 与 `:396-407`）
是 `PutAsync` + `ClearAsync` 或两次 `PutAsync`，中间**没有** `ApplyDecidedWrites`。
所以清空源槽与写入目标槽同属一个 flush，`Survivors` 的第二趟能认出它们是同一件。

### CK-010 全量（逐项目，逐个相加，不是 `grep -c`）

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | **517** | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 223 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 295 | 0 | 0 |
| Dfo.Server.Application.Tests | 1019 | 0 | 0 |
| **合计** | **2172** | **2** | **0** |

60+52+517+6+223+295+1019 = 2172。基线 2159，**新增 13 条判据**（Persistence 282 → 295）。
**Protocol 仍是 517**：本轮没有改变任何线上字节。

### 身份不变判据（新增，全部在 `Server/tests/Dfo.Server.Persistence.Tests/Inventory/`）

`WorldRowSurvivesTheMoveTests.cs`（9 条）：
`AWornPieceKeepsOneIdentityThroughAMoveAWearAndATakeOff`、`AMoveNeitherDeletesNorBuildsAWorldRow`、
`TwoWornPiecesSwappingPositionsEachKeepTheirOwnIdentity`、
`AContractCreatureTakenOffIntoTheBagIsStillTheSameCreature`、
`AnAvatarsOwnColumnsStayWhereTheyAreThroughAMove`、`AWornPieceRewrittenWhereItStandsKeepsItsIdentity`、
`APositionSaysWhichOfTheTwoItHoldsAndHowMany`、`APileIsNamedByItsTemplateAndNotByAnyIdentityOfItsOwn`、
`AWriteThatDoesNotSayWhatTheArchiveMakesOfItIsRefused`。

**变异验证**：把 `FlushAsync` 里的 `var survivors = Survivors(...)` 换成空字典（即恢复「移动就是删加建」），
**恰好上面前 6 条变红，其余 285 条仍绿**（`失败: 6，通过: 285，总计: 291`）。还原后文件 SHA-256
`6bb74a7fae7493701d5a900817589c2b4cb97df16c74e95c3b8a41a851b748c1` 与变异前逐字节相同。

### `StoredItem.WorldClass` 的去向：**留下，并接上了真正的消费者**

它现在是结算**唯一**的判断依据：`Survivors` 用它决定一件东西要不要留住行，`HoldAsync` 用它决定槽行说
`world` 还是 `template`。除它之外结算没有第二个地方能读出「这是什么」。

同时把它从「可选」改成**写必答**：`SqliteItemDomainStoreSession.Write` 在 `WorldClass is null` 时
`throw new ArgumentException`。理由写在契约注释里 —— 沉默若被当作「一堆」，就会静悄悄地
把所有判断错的东西的身份丢掉，而这正是分类规则存在的理由。读仍然返回 null，因为读什么也不决定。
判据：`AWriteThatDoesNotSayWhatTheArchiveMakesOfItIsRefused`。

**这带来了三个既有判据文件的编排更新**（只补上「这是什么」，**没有改动任何期望值或断言**）：
`Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteItemDomainStoreTests.cs`（16 处，
新增两个局部工厂 `Pile(...)` 与 `Worn(...)`）、
`Server/tests/Dfo.Server.Application.Tests/Characters/CharacterDomainStoreSettlementTests.cs`（2 处）、
`Server/tests/Dfo.Server.Application.Tests/Transactions/OneDatabaseOneParticipantTests.cs`（2 处）。

### TASK-03 带过来的两条守卫：现在被哪条判据钉住

新文件 `Server/tests/Dfo.Server.Persistence.Tests/Inventory/ClassificationRefusalTests.cs`（4 条，
两条拒绝加两条对照）：

| 守卫 | 位置 | 钉住它的判据 |
|---|---|---|
| 建号初始装备：档案判为 `Interchangeable` 就拒 | `CharacterRosterService.cs:200-212` | `CreationRefusesAStartingPieceTheArchiveReadsAsAPile`（对照 `CreationWearsAStartingPieceTheArchiveReadsAsAWornPiece`） |
| 入场租约回写：档案判为 `Avatar` 而行的种类字节不是 8 就拒 | `InventoryEntryAssembly.cs:153-164` | `AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot`（对照 `AnEntrySaveKeepsARowThatSaysWhatTheArchiveSays`） |

**如何验证它们真的会红（实测，两次全量跑）**

- 把两处 `if (...) { throw ...; }` **整块换成 `_ = worldClass;`**（保留 `ClassifyAsync` 调用，
  否则 CS9113「参数 placementRules 未读」会先把编译打掉；这也是快审那次「删掉仍全绿」的真实形状）。
  结果：`失败: 2，通过: 293` —— **恰好上面两条拒绝判据变红**，其余 2170 条（含 Protocol 517）全绿。
- 还原后两个文件 SHA-256 分别为
  `5016b76f5125df3f369c058983e2eb412578993e88870df7d5cd644c47c108fe`（`CharacterRosterService.cs`）与
  `f1f6e116caf88f2f1a57a07bd86a14a033dc40ee2443e8fa5aba9289f8dcd0cd`（`InventoryEntryAssembly.cs`），
  与变异前逐字节相同；`git diff` 与 TASK-03 记录的那份逐行相同。

判据用替身内容源（`Declaring(ItemWorldClass)`）而不是真档案：被测的是**这条路径拿到答案之后做什么**；
「答案来自档案」由 TASK-03 的 `ItemClassificationTests` 负责，两者不重复也不互相顶替。

### 未达成的一条，如实交回控制器

**「无身份的东西……不在世界表留行」本轮没有做到。** 无身份的东西**仍然在 `items` 里留一行**
（与本轮之前完全一样：移动时销毁重建，不带身份）。

原因是硬的，不是取舍：`item_slots` 只有 `(kind, id, count)` 三列，**装不下 82 字节的 core**。
而 core 今天有两处读者 —— 本 store 自己的 `ReadAllAsync` / `ReadAsync`（容器的每一次读都走它）
与入场装配的 `SqliteCharacterItemTable.ReadByCharacterAsync` —— 都从 `items` 读。
把这两处读者搬到两张表上是 **BD-009**，计划把它排在 **T-006**，不在本 Task 的允许文件里。
若本轮就停止给无身份的东西写 `items` 行，容器会读不到消耗品、入场帧会少掉整片背包，
这既违反 RQ-011 也不是「不改线上字节」。

本轮**已经**做到的部分：无身份东西的那一行**不承载身份**，`item_slots` 指的是**模板号而不是那一行**，
没有任何东西引用它；它仍是可丢弃的。等 T-006 把读者搬走，删掉这条写入就是一行的事。

**这是与控制器指令第 1 点的一处未达成，交回控制器裁定要不要提前把 BD-009 的读者迁移并入本轮。**

## Executor Self-check — round 1

### 我发现的问题

1. **控制器指令第 1 点在 T-004 的允许文件内不可实现**（见上一节）。我没有越界去改 BD-009 的读者，
   也没有假装做到，而是把冲突原样交回。
2. **`items` 上的 `UNIQUE(owner_scope, owner_id, list_type, slot_index)` 是本轮真正的机械难点**，
   计划里没有写到它。世界行本轮仍必须持有真实位置（读者还在读它），于是换位（swap）会在批中途撞唯一键。
   我的处理是**沿用同一套「先清空、再填」**，把「删除」对必须活下来的行换成「抬起」，
   而不是另发明一套冲突处理。抬起用的 `list_type = -1` 在 T-011 位置四列退役后会一起消失。
   **我认为这不违反「不要为唯一键冲突另发明机制」，但这是我的判断，请评审复核。**
3. **`item_slots` 的 `count > 0` CHECK 与 core 的 Value 可能对不上。** 我写了
   `PileOf(core) => Math.Max(core.Value, 1)`：数量小于等于 0 的堆本身就是一条说不通的行，
   而客户端看到的数量取自 core 而不是这一列，所以槽表说 1 而不是把整次结算掀掉。
   **我没有实测过生产上是否真会出现 Value 小于等于 0 的无身份行** —— 这是个防御，不是对已知事实的应对。
4. `CharacterRosterService.CreateAsync` 与 `InventoryEntryAssembly.SaveAsync` 这两条写入路径
   **不写 `item_slots`**（它们不经过结算，且不在本 Task 的允许文件里）。本轮无人读 `item_slots`，
   所以没有可观察后果；但 T-006 / T-008 开始读它时，这两条路径必须补上。**记在这里，不要漏。**

### 我没有验证的东西（明说）

- **没有**在 Application 或 Protocol 层跑一条「真档案 + 真容器 + 真移动帧」的端到端身份不变判据。
  身份不变是在 store 层证的（`SqliteItemDomainStore` 的直接判据），
  「分类真的流到 store」由 TASK-03 的 `ItemClassificationTests` 证，两段是分别证的、没有接成一条。
- **没有**验证 `Survivors` 第二趟在「同一批里同模板同 core 的两件互换」时把身份配对到哪一件。
  两件逐字节相同时这不可观察，但我没有为它写判据，也没有断言它的行为。
- **没有**验证跨 store 的移动（例如往账号仓库搬）—— 那会离开本 store，世界行仍被删除。
  这与本轮之前一致，不是新引入的，但也不在我的判据覆盖里。
- **没有**验证旧库（已有数据的 `.db`）上的行为。所有者已裁定旧库作废重建，我按此不做。
- 偶发失败：**本轮没有遇到任何一次**。全量跑了 5 次（含两次变异跑），失败集合每次都与改动一一对应。

### 不确定的地方

- `Survivors` 第三趟（原地改写载荷仍算同一件）**今天没有生产调用方** —— 没有任何路径会原地修改
  一件有身份物品的 core。我为它写了判据（`AWornPieceRewrittenWhereItStandsKeepsItsIdentity`），
  但它服务的是 RQ-003 的将来，不是今天的行为。**若评审认为这属于提前实现，删掉第三趟与那条判据
  即可，其余判据不受影响。**
- `FlushAsync` 注释里的「四段」指的是**对数据库的四轮**（读 / 抬 / 清 / 落），`Survivors` 是纯计算不算一轮。
  若读起来别扭，是措辞问题不是逻辑问题。

### 一处过程失误，如实记下

做「删掉守卫看会不会红」的变异时，我用 `git checkout -- <两个文件>` 想还原，
**结果把 TASK-03 未提交的改动一起冲掉了**（工作区所有前序改动都未提交，checkout 取的是 HEAD）。
我当场按本会话里留存的 TASK-03 diff 逐字重建了两个文件，并用变异前记录的 SHA-256 核对，
**两个文件都逐字节相同**（哈希见上一节），`git diff` 与 TASK-03 记录的那份逐行一致。
此后的变异一律用文件副本加纯文本替换还原，不再对未提交的工作区用任何 git 还原命令。
**没有提交、没有建检查点、没有推送；`git status` 的集合与开工时一致，只多了本轮的产物。**

## Contract Amendment — AMD-TASK-04-01

**控制器裁定：「无身份的东西不在世界表留行」移出本 Task，并附一条此前无人识别的前置条件。**

### Spec 并不要求这一条

核过 `spec.md`：**RQ-001** 只管「**有身份的物品**的存在与位置分开」；
**RQ-004** 只管「槽对两类东西使用同一种形状」——已达成；
**RQ-007** 只管「没有任何槽引用的**有身份物品**可以被查询出来」。

**三条都没有要求无身份的东西不在世界表留行。** 这句出自控制器写的 T-004 Step 1 与设计文档，
**不在已批准的验收条件里**。计划相对 Spec 过度指定，本修正案予以收窄。

已达成的部分保留并记明：那一行**不承载身份**，槽行指向的是**模板号而不是那一行**，
没有任何东西引用它。

### 一条此前无人识别的前置条件（这才是要紧的）

要真的停止存储堆叠物的 core，**必须先证明线上那 101 字节条目的全部字段可以由「模板号 + 数量」重建**。
线上是**逐字段**写的（`GameInventoryDelivery.cs:53-108`，35 个字段），
其中 `Attribute`、`Durability`、`SealFlag`、`ExpireTime`、`RemainUseCount`、`SortLockFlag` 等
今天都是**从存储的 core 读出来的**。

**只要有一件堆叠物的 core 带着非默认值，丢掉 core 就会改变线上字节**——直接违反 RQ-011 逐值不变。

执行方与快审都没有识别到这一点；控制器也没有。**这条前置必须先证，不能顺手做掉。**

### 与 T-008 的冲突（必须在 T-008 之前解决）

堆叠物的 `items` 行**无人引用**。而 T-008 要交付「列出没有任何槽引用的物品」的查询。
若不加区分，**那个查询会把每一件堆叠物都报成孤儿**——交付物当场失真。

→ **写进 T-008 的派工**：孤儿查询必须能区分「无人引用的**世界行**」与「堆叠物的 core 行」，
   并要有判据钉住这个区分。

### 归属

真正的迁移（把 core 的读者从 `items` 挪开）落在 **BD-009 / T-006**，那里本就拥有入场装配的读者。
→ **写进 T-006 的派工**，连同上面那条前置条件。

## Controller Disposition — DSP-TASK-04-01

### 关于误用 `git checkout --` 冲掉未提交工作的事故

执行方如实报告：做守卫变异时误用 `git checkout --`，冲掉了 TASK-03 的未提交改动，
随后从会话内 diff 逐字重建。

**控制器已独立核验恢复完整，不采信自述**：`ItemContainers.cs` 的 sha256 为
`2c382d9f6ec420b1d0d90c0f8f97c7fb147353fa9d8d9e954c57c59b9fe959e3`，
与 **TASK-03 评审轮 `QR-TASK-03-01` 里记下的值逐字符相同**——那个值写于事故之前、由另一个代理记录，
是独立锚而非执行方自证。另核：TASK-03 的三份产物全部在位、`ClassifyAsync` 签名完整、
HEAD 仍为 `51cd99c2`、stash 为空、全量判据 2172/2/0 且含 TASK-03 的分类判据。**链条完整。**

**立一条规矩，写进后续每一份派工**：

> 在这个工作区里，**任何 git 还原类命令（`checkout --`、`restore`、`reset`、`clean`）一律禁止**。
> 全部工作未提交，一条还原命令就能抹掉前序任务的成果，而它们没有第二份副本。
> 做变异验证时**只用文件级备份**（复制走、改、复制回），并用哈希核对还原。

这次侥幸的原因是执行方手上恰好有会话内 diff。**下一次未必有。**

## Independent Quick Review — QR-TASK-04-01

- **Verdict: `passed`**
- 审的口径：Contract revision 1 + **AMD-TASK-04-01** + **DSP-TASK-04-01**。
  按修正案，「无身份的东西不在世界表留行」不在本 Task 的验收内，未就此计缺陷。
- Owned diff identity：HEAD 仍是 `51cd99c2`，全部未提交，`git status --short` 33 条（24 改 + 9 未跟踪），
  `git stash list` 为空。本轮所属文件的 sha256（写在这里作为下一轮的独立锚）：
  - `SqliteItemDomainStore.cs` `6bb74a7fae7493701d5a900817589c2b4cb97df16c74e95c3b8a41a851b748c1`
  - `IItemDomainStore.cs` `bbba6111ee43697746014e32eaea6d77547694fe2679c7b81d9add1254cda30b`
  - `WorldRowSurvivesTheMoveTests.cs` `40f6825aa2e88172c0c26b55ec4286cec2b0a256b3e1faf9ee0d502746a72fee`
  - `ClassificationRefusalTests.cs` `a56d64e651b1002a98d06cf54c5ccfd6ff50985ff22edf26ad4b23f17a919884`
  - 另核 `ItemContainers.cs` = `2c382d9f6ec420b1d0d90c0f8f97c7fb147353fa9d8d9e954c57c59b9fe959e3`，
    与 `DSP-TASK-04-01` 记下的值逐字符相同 —— 事故恢复链仍完整。
- 本轮**没有**改任何实现文件、没有用 git 还原命令、没有提交。变异只用文件级备份（复制到临时目录）。

### 1. 世界行在移动中不被销毁 —— 形状与三个追问

自己读了 `SqliteItemDomainStore.cs`。形状与执行方所述一致：
`FlushAsync`（`:205-245`）= `DisplacedAsync`（`:283`）到 `Survivors`（`:330`，纯计算）到
`CarriedAvatarsAsync`（`:385`）到 `LiftAsync` 循环（`:212-215`）到 `EmptyAsync` 循环（`:217-224`）到
`LandAsync`/`FillAsync` + `HoldAsync`（`:226-244`）。世界行在移动中只被 `UPDATE`（`:442-452` 抬起、
`:461-481` 落下），`DELETE`/`INSERT` 只到达被丢弃的行与槽行。

**(a) `list_type = -1` 会不会与真实容器号相撞 —— 不会。**
`NoContainer = -1`（`:261`）。容器号来自 `OwnerOf`（`:649-668`）的 `(int)containerType`，
而 `InventoryContainerType` 是 **`byte` 枚举**（`Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs:6-23`），
取值 0..255，永远不为负。停车位的槽号是 `slot_index = item_uid`，`item_uid` 是主键，两行不可能同号。
同一位置只可能被同一批里的一次抬起占用，撞不上。

一处**不是缺陷但要记下的相邻事实**：`items.list_type` 只有 `INTEGER NOT NULL`，**没有 CHECK**
（`CharacterDatabaseSchema.cs` 的 `TableDeclarations`），所以 -1 是被数据库默默接受的；
而 `InventoryItemTables.cs:40` 的入场读法是 `list_type NOT BETWEEN 19 AND 23`，**没有下界** ——
`list_type = -1` 的行会被它读出来。今天不可达（见 (b)），但 -1 的安全**靠的是时序不是任何查询的排除**。
T-011 拿掉位置四列时这一条会一起消失；在那之前，谁要给 `items` 加新读法都得先看这一行。

**(b) 中途失败会留下什么 —— 什么也不留，事务边界覆盖整段。**
自己顺了一遍：`FlushAsync` 的每条语句都走 `owner.CommandAsync`，而
`SqliteDatabaseParticipant.CommandAsync`（`:84-91`）把 `this.pending` 这一个事务钉在每个 command 上，
facet 拿不到连接、开不了第二个事务。`PrepareAsync`（`:123-143`）**最后**才置 `prepared`；
`DomainTransaction.CommitAsync`（`:78-96`）在 prepare 抛出时先 `RollbackEnlistedAsync` 再 rethrow；
`SqliteDatabaseParticipant.RollbackAsync`（`:173-186`）与 `SettleAsync`（`:194-219`）回滚并释放。
进程崩在中途则由 SQLite 的日志回滚。**所以「抬起后、落下前」失败不会留下任何 `list_type = -1` 的行。**
唯一能想到的残留路径要求调用方吞掉 flush 的异常再继续走到 prepare —— 今天没有这样的调用方
（`ItemContainers.ApplyDecidedWritesAsync` `:558-564` 不 catch），且这是既有形状不是本轮新引入的。

**(c) 没有为唯一键冲突另发明机制 —— 见下面的「特别判断」。** 就机制清单而言：
没有 `ON CONFLICT`、没有 UPSERT、没有可延迟约束、没有重编号、没有撞了重试。
`EmptyAsync`（`:493-511`）的谓词**逐字仍与唯一键同名**，「先清空所有点名的位置、再填」的次序原样保留。

### 2. CK-009 的判据是否真的证明了「既没删也没建」—— 成立

自己读了 `WorldRowSurvivesTheMoveTests.cs`。`AMoveNeitherDeletesNorBuildsAWorldRow`（`:94-114`）
断言 `SELECT item_uid FROM items ORDER BY item_uid` 的整个列表相等（`:112`）**且**
`sqlite_sequence` 里 `items` 的 seq 不变（`:113`）。

等价性成立：`item_uid` 是 `INTEGER PRIMARY KEY AUTOINCREMENT`（schema 里已核），
seq 单调不减且每次自动分配都会抬高它，所以 **seq 不变意味着这一段没有发生自动分配的 INSERT**；
集合相同再加上没有 INSERT，就**意味着也没有 DELETE**（删掉的号在 AUTOINCREMENT 下永不重发，补不回来）。
`FillAsync`（`:521-571`）不显式指定 `item_uid`，所以「显式插回旧号绕过 seq」这条理论漏洞不在本实现的可达路径上。
判据是充分的，不是「看起来充分」。

另核 `AWornPieceKeepsOneIdentityThroughAMoveAWearAndATakeOff`（`:51-83`）三次移动后
**每次都从库里重读**身份而不是记住写入值（`:74`），并同时断言旧槽行没了、新槽行是 `('world', 身份, 1)`。
`TwoWornPiecesSwappingPositionsEachKeepTheirOwnIdentity`（`:125-151`）钉住了三趟的顺序。

**生产上的移动确实是一个批**（自己核过，不是采信）：`MoveItemSpaceSlice.PutOnAsync`（`:332-342`）与
`TakeOffAsync`（`:396-406`）都是 `PutAsync` + `ClearAsync` 或两次 `PutAsync`，中间**没有**
`ApplyDecidedWritesAsync`；`ItemContainers` 里该调用只出现在 `:343`、`:359`、`:432` 三处，都在一个动词之后。

### 3. `StoredItem.WorldClass` 已有真正的消费者 —— 是，不再是死字段

结算里三处读它，且没有第二个判断依据：
`SqliteItemDomainStore.cs:336`（`Survivors` 用它把 `Interchangeable` 排除出幸存集合）、
`:586`（`HoldAsync` 用它决定槽行说 `world` 还是 `template`）、`:156-163`（`Write` 在为 null 时
`throw new ArgumentException`）。生产上**唯一**的生产者是 `ItemContainers.cs:277`；
全仓 `new StoredItem` 只有三处，另两处是 store 自己的读（`:94`、`:139`，读侧留 null）。
所以「写必答」不会打掉任何现存的生产写入路径。判据 `AWriteThatDoesNotSayWhatTheArchiveMakesOfItIsRefused`
（`WorldRowSurvivesTheMoveTests.cs:294-301`）钉住了拒绝。**上一轮记的死字段债，本轮已还。**

### 4. TASK-03 的两条守卫 —— 独立重做变异，恰好两条红

用文件级备份重做（复制到临时目录，**没有用任何 git 还原命令**）：

- 变异前 sha256：`CharacterRosterService.cs` = `5016b76f5125df3f369c058983e2eb412578993e88870df7d5cd644c47c108fe`、
  `InventoryEntryAssembly.cs` = `f1f6e116caf88f2f1a57a07bd86a14a033dc40ee2443e8fa5aba9289f8dcd0cd`
  —— 与执行方记录的两个值逐字符相同，说明文件确实处在被记录的那个状态。
- 变异：`CharacterRosterService.cs:206-212` 与 `InventoryEntryAssembly.cs:159-164` 两整块
  `if (...) { throw ...; }` 换成 `_ = worldClass;`（保留 `ClassifyAsync` 调用）。
- 全量结果：**失败 2，且恰好是**
  `ClassificationRefusalTests.CreationRefusesAStartingPieceTheArchiveReadsAsAPile` 与
  `ClassificationRefusalTests.AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot`；
  Persistence 293 通过 / 2 失败，**其余六个项目全绿（Protocol 仍 517）**。
- 还原后两个文件 sha256 与变异前**逐字节相同**，且全量复跑回到 2172/2/0。

两条对照判据（`CreationWearsAStartingPieceTheArchiveReadsAsAWornPiece`、
`AnEntrySaveKeepsARowThatSaysWhatTheArchiveSays`）是真的对照 —— 它们断言 `items` 里落了 1 行，
不是空断言。**守卫确实被钉住了。**

### 5. 既有判据有没有被削弱 —— 没有，逐个看过 diff

- `SqliteItemDomainStoreTests.cs`：16 处把 `new StoredItem(...)` 换成局部工厂 `Pile(...)` 与 `Worn(...)`，
  两个工厂新增在 `:355-366`。**每一处 `Assert` 的期望值逐字未动。**
- `CharacterDomainStoreSettlementTests.cs` 2 处、`OneDatabaseOneParticipantTests.cs` 2 处：
  只在对象初始化器里补 `{ WorldClass = ItemWorldClass.Interchangeable }` 并加一行 `using`。断言未动。
- 另外三个含 `WorldClass` 的既有测试文件（`Support/CharacterSide.cs`、`DungeonEntryCodecTests.cs`、
  `GroundPickupTests.cs`）的改动是 **TASK-03** 的分类源，不属本轮，未计入。

补构造参数是正当的，没有发现任何削弱。

### 6. CK-010 自己跑了一遍 —— 逐项目，不是 `grep -c`

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 517 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 223 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 295 | 0 | 0 |
| Dfo.Server.Application.Tests | 1019 | 0 | 0 |
| **合计** | **2172** | **2** | **0** |

60+52+517+6+223+295+1019 = **2172**，与执行方的数一致，不低于契约门槛 2131。
跑了两次（变异前、还原后），两次都是这个数。新增判据数自核：
`WorldRowSurvivesTheMoveTests.cs` 9 个 `[Fact]` 加 `ClassificationRefusalTests.cs` 4 个 `[Fact]` = **13**，
两个文件都在 Persistence.Tests，与 282 到 295 相符。两文件都没有 `[Theory]`。

**偶发失败：本轮三次全量跑（基线 / 变异 / 还原）失败集合每次都与改动一一对应，没有遇到任何一次。**

### 7. 线上字节 —— `Dfo.Server.Protocol.Tests` 三次跑都是 **517**

基线 517、变异跑 517、还原跑 517。不阻断。

### 8. BOM —— 全部干净

本轮改动的 7 个文件首三字节都是 `75 73 69`（`usi`），无 `EF BB BF`。`.editorconfig:4` 是 `charset = utf-8`。

### 特别判断：「抬起再落下」是否触碰了那两条禁令

**我的独立判断：没有触碰。理由如下，不采信执行方的自我归类。**

**关于「不得为唯一键冲突另发明机制」—— 没有触碰。**
这条禁令挂在「保留条件」下，它保的是**清理办法本身**：谓词与唯一键同名的 DELETE，
以及「先清空所有点名的位置、再填」的次序。这两样在 `EmptyAsync`（`:493-511`）里**逐字仍在**，
只是同时删了槽行。所谓「另发明机制」指的是拿别的东西**取代**这套办法 ——
ON CONFLICT / UPSERT、可延迟约束、重编号、撞了再重试。**这些一个都没有出现。**

`LiftAsync` 不是冲突处理器：它不在冲突发生时做任何事，它是**把必须活下来的那一行排除在那次删除之外**。
换句话说，改的是「删什么」，不是「撞了怎么办」。这一点可以由代码证伪 —— 抬起是无条件的、
在任何冲突之前就发生，且抬起的目标位置是构造上唯一的（`slot_index = item_uid`），
不含任何「试一下、不行再换」的成分。

而且这条禁令与本 Task 自己的验收条件「**移动路径不删除也不重建任何世界行**」（源自 RQ-001）是有张力的：
只要 `items` 还承载位置（读者要到 T-006 才搬走），换位就必然在批中途撞唯一键，
而「删掉再插回」恰恰是验收条件明令禁止的。二者只能保一个，**保验收条件是对的**。
执行方把张力如实交回而不是偷偷选一边，这是对的做法。

**关于「取凭证 → 换实例 → 操作 → 换回凭证 → 写回」的往返 —— 没有触碰。**
这条防的是让**调用方**手里过一次身份。核过 `IItemDomainStore.cs` 的 diff：
`IItemDomainStoreSession` 的签名一个都没变，`StoredItem` 只多了 `WorldClass`，**没有多出任何身份参数**；
调用方的词汇仍是「容器加槽」。抬起与落下**整段在 `FlushAsync` 内部**，
身份从不离开 store，也从未出现在任何返回值里。

**关于中途失败的中间状态是否等价于契约要防的风险 —— 不等价。**
契约要防的是**能被别人看见、能活过一次失败**的中间态。这里的抬起态两样都不是：
它在**同一个 SQLite 事务**里（(b) 已逐处核过），对事务外不可见，失败即随事务一起消失。
它不是持久化的凭证交换，是一次尚未提交的语句序列里的一步。

**唯一要记下的边界条件**（不构成缺陷，属于给 T-006 / T-008 / T-011 的告知）：
-1 这个哨兵的安全**全靠「事务内、无中断」**这一个前提，数据库层面既没有 CHECK 挡它、
入场读法（`InventoryItemTables.cs:40`）也没有下界排除它。谁要在抬起与落下之间插入任何东西，
或给 `items` 加不带 `list_type` 下界的读法，这个前提就没了。

### 观察（不阻断，不要求本轮改）

1. **`CarriedAvatarsAsync`（`:385-422`）实质上快成了走不到的路。**
   时装被档案判为 `Avatar`，永远不是 `Interchangeable`，所以只要批里有它自己被顶掉的那一行，
   它就是幸存者、就不进 `carried`。既有判据
   `SqliteItemDomainStoreTests.Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore`（`:151-167`）
   的断言没有被削弱，但它现在**是靠「行没被删」通过的，而不是靠「读回来再写上去」**——
   也就是它命名的那条机制已不再被它覆盖。这不是本轮的缺陷（断言与期望值都没动），
   但那段搬运代码今天已近乎无人验证，值得在最终评审里决定是留是删。
2. `ValidateSlot` 的注释（`:626-631`）说「先前的实现曾把行停在 slot -1，现在没有谁这么做了」。
   字面仍成立（现在停的是 `list_type = -1` 而不是 `slot_index = -1`），但读起来会让人以为停车位已经绝迹。
   注释不是铁律，不计缺陷，只是提一句。
3. 执行方自检第 4 点（`CharacterRosterService.CreateAsync` 与 `InventoryEntryAssembly.SaveAsync`
   **不写 `item_slots`**）我复核属实，且今天无人读 `item_slots`，所以本轮无可观察后果。
   **这条必须进 T-006 / T-008 的派工**，否则那两条路径落的行会在孤儿查询里失真。

### 结论

在本次快审的输入范围内，**没有发现可局部证明的阻断缺陷**。契约的两条禁令经独立判断均未触碰，
CK-009 的判据在逻辑上成立，CK-010 与线上字节自己跑过并复核，守卫的变异独立重做且恰好两条红，
既有判据没有被削弱，BOM 干净，越界文件没有。**verdict: `passed`。**
这只表示本 Task Unit 可以走执行方的接受流程，不是最终质量批准。

## Controller Disposition — DSP-TASK-04-02

快审 `QR-TASK-04-01` 给出 `passed`，并**自己用文件级备份重做了守卫变异**（遵守了禁用 git 还原的规矩）。
控制器处置其三条非阻断观察，并记一条由它的论证推出的简化。

### 一、由快审的论证推出的简化——`LiftAsync` 是暂时的

快审指出禁令与验收条件之间的张力：**只要 `items` 还承载位置，换位就必撞唯一键**，
而「删了再插」正是「移动不销毁世界行」所禁止的。抬起是这个张力下的正解。

**但那个张力本身会在 T-011 消失。** T-011 退役 `items` 的位置四列之后，
`items` 上再没有 `UNIQUE(owner, list, slot)` 可撞，**抬起／落下整套机制随之失去存在理由**。

→ **写进 T-011 的派工**：退役位置四列时，一并评估 `LiftAsync` 及其 `-1` 哨兵是否可以整体摘掉。
   若可以就摘掉——留着一套没有理由的机制，正是后来者最容易误读的东西。

### 二、`-1` 哨兵的安全论证很薄——带进 T-006 / T-011

`items.list_type` 无 CHECK，且入场读法 `InventoryItemTables.cs:40` 的 `NOT BETWEEN 19 AND 23`
**没有下界**。所以 `-1` 不被读到，**全靠「事务内、无中断」这一个前提**。

快审核实该前提今天成立（`SqliteDatabaseParticipant.CommandAsync:84-91` 挂同一事务，
`DomainTransaction.CommitAsync:78-96` 在 prepare 抛出时回滚）。**不是缺陷，是论证薄。**
→ 与第一条一并带进 T-011；若届时摘不掉抬起机制，则须给下界补一道硬约束。

### 三、一条判据现在靠别的理由通过——留给完整变更评审

`Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 现在靠「行没被删」通过，
**不再覆盖它名字所声称的那套搬运机制**（`CarriedAvatarsAsync:385-422` 近乎走不到了）。

这是本仓明确在意的形状：**判据的名字不再描述它证明的东西**。
本轮不改（改名或重写属判据整理，不在本 Task 边界），
→ **交给完整变更评审裁**：是改名、重写、还是随 `CarriedAvatarsAsync` 一并退役。

### 四、两条写入路径不写 `item_slots`——已定向

执行方自检第 4 点经快审复核属实。已在 `AMD-TASK-04-01` 里定向到 T-006 / T-008，此处不重复。

### 检查结果（快审独立复跑，非采信）

| 项目 | 通过 | 跳过 |
|---|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 |
| Dfo.Server.Protocol.Tests | **517** | 0 |
| Dfo.Server.Host.Tests | 6 | 0 |
| Dfo.Server.Infrastructure.Tests | 223 | 2 |
| Dfo.Server.Persistence.Tests | 295 | 0 |
| Dfo.Server.Application.Tests | 1019 | 0 |
| **合计** | **2172** | **2** |

失败 0，三次跑无偶发。`Protocol.Tests` 三次均为 517——线上字节未动。
守卫变异：**恰好 2 条红**，还原后逐字节相同。7 个改动文件无 BOM。
