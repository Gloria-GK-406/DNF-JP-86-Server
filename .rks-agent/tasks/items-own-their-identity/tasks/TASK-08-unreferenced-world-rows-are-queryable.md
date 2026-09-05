# TASK-08 — 无人引用的有身份物品可被查询

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 提供一条能列出「存在于世界表但没有任何槽引用」的读通路，并如实记录本轮不清扫它们。
- Referenced spec requirements: RQ-007
- Referenced plan sections: T-008
- Owned boundary: BD-004, BD-014
- Permitted files or interfaces: `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`、`Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs`、`Server/tests/`
- Dependencies: TASK-07
- Required dependency outputs: 世界表已承载三类有身份物品的状态
- Acceptance conditions: 造出一件无人引用的物品后查询能列出它；任务记录如实写明本轮**不**清扫，清扫属于后续成果。
- Task-specific authorization: Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做一次。范围外一律不碰：定时清理器、旧库自动升级、地上掉落／交易托管／拍卖挂单／邮件附件的启用、契约兽领域本身、契约兽槽位 24 还是 25 的裁定（沿用 24）、`Pet=7` 容器装什么、契约兽技能、`character_condition` 的五列。
- Required methods: test-driven-development；改动前先让判据表达期望，每个 Task 结束时全量判据全绿，**不允许攒红**。计划里已定、本 Task 不得自行推翻的五条：世界侧**一张**表不是三张；`items` 就地变成世界表且位置另立槽表；
今天的 DELETE+INSERT 结算**原样保留只换作用对象**（物品行 → 槽行），不得为唯一键冲突另发明机制；
契约兽对外标识用**单一窄空间**（i32）加分配守卫，不得另设第二个标识；**不引入新的 `store` 读法**，
世界侧不新增以 `DomainStore` 结尾的类型。
- Required checks:

- CK-017 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → 不低于 2131 通过 / 2 跳过 / 0 失败
- CK-018 `Non-command: 复核今天是否存在能销毁有身份物品的生产通路`

- Required evidence: 查询通路的 diff；孤儿查询判据名；CK-018 的复核结论与其依据。

### 阻塞条件（硬性停止点）

CK-018 若发现**存在**能销毁有身份物品的生产通路，**停下并提交阻塞报告给控制器**。那是 Spec 假设（本轮不会产生孤儿）被证伪的情形，**不得把清理器塞进本轮**，也不得当作可以硬做完的事。

今天已核实的依据：清空槽位的五处调用全部发生在移动过程中（清空源槽），消耗到零只作用于无身份的东西。若这一结论在本 Task 被推翻，即触发上述停止点。

## Execution Result — round 1

### 改了哪三个文件，每处为什么

| 文件 | 改动 | 为什么 |
|---|---|---|
| `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs` | `IWorldItemTable` 新增一个成员 `ReadUnreferencedAsync(CancellationToken)` → `IReadOnlyList<WorldItemRow>`，附完整 XML 说明 | RQ-007 要的那条读通路。文档里写清三件事：本轮**不清扫**（清扫是后续成果，一条会偷偷删的读会让后续那件事无从评审）；**堆叠物的 core 行不是孤儿**及其区分依据；以及**这个答案的可信度等于槽表的可信度**（债二，见下） |
| `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs` | `SqliteWorldItemTable` 实现该成员；并把 `FindAsync` 原先内联的 17 列 reader lambda 提成 `private static WorldItemRow Read(SqliteDataReader)`，两处读共用 | 实现 + 去掉两份一模一样的 17 列映射。**没有改动 `FindAsync` 的语句、列序或行为**，只是把 lambda 换成同名方法组 |
| `Server/tests/Dfo.Server.Persistence.Tests/Inventory/WorldAndSlotTableTests.cs` | 新增判据小节 `-- what nothing holds --`（3 条）＋ 一个 `Potion(count, itemId)` 造核助手 | AC-009 / EV-005 |

**没有改允许清单以外的任何文件。** 没有改 schema、没有改结算、没有改抬起／落下、没有新增 `store` 读法、没有新增以 `DomainStore` 结尾的类型、没有引入凭证往返（这条读只交出 `WorldItemRow`，不回写）。

### 孤儿查询怎么区分「无人引用的世界行」与「堆叠物的 core 行」

语句是**两条 `NOT EXISTS`**，第二条才是难点所在：

```sql
FROM items
WHERE NOT EXISTS (                                  -- (1) 没有槽以「世界行」的名义指向这一行
          SELECT 1 FROM item_slots holder
          WHERE holder.kind = 'world' AND holder.id = items.item_uid)
  AND NOT EXISTS (                                  -- (2) 这一行不是「某个装模板的槽」脚下那张 core 行
          SELECT 1 FROM item_slots pile
          WHERE pile.kind = 'template'
            AND pile.owner_scope = items.owner_scope
            AND pile.owner_id  = items.owner_id
            AND pile.list_type = items.list_type
            AND pile.slot_index = items.slot_index)
ORDER BY item_uid;
```

- **只有 (1) 一定是错的**：按「更正 C-1」，堆叠物**照样在 `items` 里留一行**（core 里的 `Marker16 = -1`、`RandomOptionChangedIndex = 255` 上线，丢不掉），
  而它的槽行 `kind='template'`、`id` 是**模板号**——**永远不会等于那一行的 `item_uid`**。
  所以单靠 (1)，库里**每一件堆叠物都会被报成孤儿**，正是控制器点名的债一。
- **(2) 是区分处**：堆叠物的 core 行**站在一个「槽行说这里放的是模板」的位置上**；真正没人要的世界行**站不到这种位置上**。
- **(2) 用「位置」做索引是安全的**，因为 `items` 上有 `UNIQUE(owner_scope, owner_id, list_type, slot_index)`
  （`CharacterDatabaseSchema.cs:186`）——一行的位置上找到的槽行，**要么是它自己的，要么谁的也不是，绝不会是别的 `items` 行的**。
- **代价必须记下**：(2) **依赖那四个位置列**，而那正是本模型要退役的残留（T-011）。
  列一旦退役，「这行是堆叠物的 core」这件事**必须落到行自己身上**——今天行上**没有**任何别的东西能说这件事，
  core 里的 kind 字节恰恰是「分类之所以存在是因为它不可信」的那个值。**这一条已写进实现的 `<remarks>`，交给 T-011。**

**判据怎么钉住这个区分**（`WorldAndSlotTableTests`，3 条新判据）：

1. `A_thing_no_position_holds_is_found_by_asking_for_those`
   —— 只往世界表放一件（含契约兽八列）、不写任何槽行，断言查得到、身份对、契约兽状态还在。这条直接对应 **AC-009**。
2. `A_thing_a_position_holds_is_not_one_of_those`
   —— 世界行 ＋ `kind='world'` 槽行，断言结果为空。钉住 (1)。
3. `A_piles_row_is_not_one_of_those_although_no_slot_names_it`
   —— **同一个库里**同时造三样：一堆药水（`items` 一行 ＋ `kind='template'` 槽行，正是 `FillAsync`＋`HoldAsync` 写出的形状）、
   一件被穿着的装备（`items` 行 ＋ `kind='world'` 槽行）、一件没人引用的契约兽。
   断言 `ReadUnreferencedAsync` 交出的**恰好只有那件契约兽**。
   这条把区分**两个方向一起**钉住：漏掉 (2) 会多出药水，写错 (1) 会多出装备。

**变异验证（自愿做的，非本 Task 要求）**：按纪律先 `cp` 到 scratchpad 备份，删掉第二条 `NOT EXISTS`，重建重跑：
`失败: 1，通过: 17`，唯一变红的就是判据 3，失败信息 `Expected [3] / Actual [1, 3]`——即**药水那行被报成了孤儿**，与债一的描述逐字吻合。
随后 `cp` 还原，`sha256sum` 核对：还原前后同为 `f091388fd917cf805422305c1fb82e211f6c9e91efd28de4090ebef3b0e9df7e`，**逐字节一致**。
全程未用任何 git 还原类命令。

### 债二（悬空槽行 / 不写槽表的写入方）的查证结果 —— 未修，如实报出

**查证方法**：全仓 `grep` `INSERT/UPDATE/DELETE ... items|item_slots` 与 `FROM item_slots`，逐处读代码。

**结论：`item_slots` 今天在生产侧是「只写不读」的，而且只有结算一条路在写。**

| 位置 | 写 `items` | 写 `item_slots` | 后果 |
|---|---|---|---|
| `SqliteItemDomainStore` 结算（`Fill/Land/Empty/Hold`，`SqliteItemDomainStore.cs:500-604`） | 是 | **是**（`Empty` 两表一起删，`Hold` 写槽行） | 两表一致 |
| `CharacterRosterService.CreateAsync` → `InsertOwnAsync`（`CharacterRosterService.cs:214`） | 是 | **否** | 新角色的初始装备**只有世界行，没有槽行** |
| `InventoryEntryAssembly.SaveAsync` → `UpdateOwnAsync`／`InsertOwnAsync`（`InventoryEntryAssembly.cs:181,185`） | 是 | **否** | 入场存盘写出的每一行**都没有槽行** |
| `DeleteOwnExceptAsync`（`InventoryItemTables.cs:80-100`，两条 SQL 分支） | 删 | **否** | **留下悬空槽行**（指向已不存在的世界行） |
| `SqliteWorldItemTable` / `SqliteItemSlotTable`（`InventoryItemTables.cs`） | 是 | 是 | 生产侧无人到达，只有判据调用 |

读侧：`FROM item_slots` 全仓只出现在 `SqliteItemSlotTable`（判据专用）与**本轮新增的这条查询**里。
入场装载走的是 `ReadByCharacterAsync`，只读 `items`。**所以在本轮之前，两表不一致谁也看不见。**

**这对本交付物意味着什么（必须写明，否则就是失真）**：

1. **误报方向**：凡是经入场存盘的 `InsertOwnAsync`／`UpdateOwnAsync`、或建角色的 `InsertOwnAsync` 写出的行，**根本没有任何槽行**，
   既不满足 (1) 的排除也不满足 (2) 的排除，**会被这条查询列为孤儿**。
   在一个跑过入场存盘的真实库上，这条查询几乎会列出该角色的**全部**物品。
   这不是查询写错了——查询对槽表的陈述是准确的；是**槽表今天还没有被这些写入方维护**。
   `IWorldItemTable.ReadUnreferencedAsync` 的 `<remarks>` 已把这一条原文写进去，指名了那两个写入方。
2. **漏报方向**：`DeleteOwnExceptAsync` 留下的悬空 `kind='template'` 槽行，
   若日后有一行 `items` 落到同一位置，(2) 会把它挡掉。这是 (2) 依赖位置列所付的第二笔代价。
3. **另一个次生风险（顺带发现，未修）**：`HoldAsync`（`SqliteItemDomainStore.cs:589`）的 `INSERT INTO item_slots` **没有 `ON CONFLICT`**，
   而 `item_slots` 有 `UNIQUE(owner_scope,owner_id,list_type,slot_index)`。结算内部先 `Empty` 再 `Hold`，所以自洽；
   一条由存盘路径留下的悬空槽行若停在结算即将 `Hold` 的位置上，`Empty` 会先删掉它，所以**目前也撞不上**。记在这里是因为它只差一步。

**为什么不在本轮修**：修它要动 `CharacterRosterService.cs`、`InventoryEntryAssembly.cs`（都在允许清单**之外**），
并且会把「入场存盘也走两张表」这件事塞进本 Task——那是把 T-008 变成另一个 Task。
**交回控制器裁决**：建议作为独立 Task（或并进 T-011 退役位置列的那一步，因为二者的正解是同一个：让写入方都写槽表、让世界行不再靠位置列被认出）。

### CK-018 复核结论与依据 —— 未触发停止点

**结论：今天不存在能销毁一件有身份物品的生产通路。TASK-01 时期的结论在本轮仍然成立。**

依据（逐条可当场核）：

1. **清空容器槽位的调用共五处**，与 TASK-01 记的数目一致：
   - `ItemContainers.cs:97` `RelocateAsync` —— `Clear(源槽)` 紧跟 `Put(目标槽)`，**同一批**；
     结算的抬起／落下让那一行原样活下来（`FlushAsync` 先 `Lift` 幸存者再 `Empty`），世界行未被销毁。
   - `ItemContainers.cs:153` `MergeAsync` —— 只在整堆倒空时 `Clear`。**合并被 `DecideMoveAsync`（`ItemContainers.cs:487-492`）用
     `placement.StackLimit > 1` 挡住**，有身份的东西 StackLimit 为 1，走不到 Merge。
   - `ItemContainers.cs:191` `ConsumeAsync` —— 只在 `remaining == 0` 时 `Clear`。
   - `MoveItemSpaceSlice.cs:336` `PutOnAsync` —— 只在目标槽原本为空时 `Clear(源槽)`，而同一批已 `Put` 到目标槽。
   - `MoveItemSpaceSlice.cs:400` `TakeOffAsync` —— 同上，镜像。

   **五处全部发生在移动过程中（清空源槽），没有一处是净销毁。**
2. **消耗到零只作用于无身份的东西**：`ConsumeAsync` 的唯一调用方是
   `ItemUseEffectDomainService.ApplyAsync`（`ItemUseEffectDomainService.cs:83`），它只对 `ConsumeFromContainerEffect` 生效，
   而该效果来自档案的 `IsConsumedOnUse`。**诚实的限定**：这一条由**档案数据**保证，**代码里没有一道守卫**
   拦住「档案给一件被判为 Equipment/Avatar/Creature 的物品挂上消耗效果」。今天的档案不这么做，但这是数据保证不是代码保证。
3. **入场存盘的对账不销毁有身份物品**：`InventoryEntryAssembly.SaveAsync` 的保留清单
   （`InventoryEntryAssembly.cs:139-146`）取自租约里**每一件有身份物品的 `Identity`**，
   而租约本身就是从同一批行装载出来的；一件物品要被对账删掉，只能先从容器里消失，
   而让它从容器里消失的只有上面那五处——全是移动。
4. **删角色不销毁**：`CharacterRosterService.SoftDeleteAsync` 只做 `MarkDeletedAsync`，不删 `items`。
5. **`SqliteWorldItemTable.DeleteAsync` 生产侧无人到达**（只有判据调用）。

**并且，一个对 Spec 假设更强的观察**：即使上述任何一条被推翻，**销毁也不会产生孤儿**——
`EmptyAsync`（`SqliteItemDomainStore.cs:500-506`）在同一条命令里**同时删 `items` 与 `item_slots`**。
今天真正会产生的两表不一致是**反方向的悬空槽行**（债二，`DeleteOwnExceptAsync`），不是无人引用的世界行。
所以 Spec 的假设「本轮不会实际产生无人引用的物品」**成立，且比它自己声称的理由更稳**。

**未触发阻塞条件，不需要提交阻塞报告。**

### 两项检查的实际输出

**CK-017** `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

逐项目（从这一次运行的每个测试程序集摘要行逐个抄下，未用 `grep -c`）：

| 测试程序集 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Persistence.Tests | **311** | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 233 | 2 | 0 |
| Dfo.Server.Application.Tests | 1022 | 0 | 0 |
| **合计** | **2205** | **2** | **0** |

521+60+52+6+311+233+1022 = **2205**。基线 2202 → **2205 通过 / 2 跳过 / 0 失败**，增量 **+3** 恰为本轮新增的三条判据（全在 Persistence，308 → 311）。

字节锚复核：`SelectCharacterAcceptedTranscriptTests.cs` **一字未改**（`git status` 里不出现该文件）；
单跑 `SelectCharacterAcceptedTranscriptTests` ＋ `InventoryDeltaLift`：**19 通过 / 0 失败 / 0 跳过**，与硬约束一致。

**CK-018** `Non-command: 复核今天是否存在能销毁有身份物品的生产通路` → **不存在**，依据见上一小节。**未触发停止点。**

## Executor Self-check — round 1

### 我发现的问题

1. **债一按要求处理了，并且用变异验证证明区分是真的被判据钉住的**（删掉区分条款 → 恰好判据 3 变红，且失败信息就是「药水被当成孤儿」）。
2. **债二没有修，而且它对本交付物的影响比控制器描述的更大**：控制器点的是「悬空槽行」这个方向；
   查下来更要紧的是**反方向**——入场存盘与建角色**根本不写槽表**，
   所以在跑过存盘的真实库上这条查询会误报大量非孤儿。这一条我写进了接口文档，也写进了上面的报告。
   **我认为这不构成「查询写错了」**：查询对槽表的陈述是准确的，是槽表还没被那两条写入方维护；但**控制器需要知道**，
   因为「孤儿可查询」这句话在今天的库上还不等于「查出来的都是孤儿」。
3. **(2) 依赖位置列**，而位置列 T-011 要退役。退役那一步必须同时给世界行一个「这行是不是堆叠物 core」的自述，否则本轮的区分会随列一起消失。已写进实现注释。
4. **顺带发现（未修，未扩范围）**：`HoldAsync` 的 `INSERT INTO item_slots` 无 `ON CONFLICT`，靠 `EmptyAsync` 先删才没撞唯一键。今天安全，但只差一步。

### 我**没有**验证的东西（明说）

- **没有在真实数据库上跑过这条查询**。三条判据用的是判据自己的临时 SQLite 库（`Fixture`），
  「入场存盘写出的行会被误报」这一条是**读代码得出的静态结论，不是我跑出来的**。
- **没有验证 `EXPLAIN QUERY PLAN`／没有量过性能**。两条相关子查询在 `item_slots` 上：
  (1) 走不到索引（`kind`＋`id` 上没有索引），(2) 能走 `UNIQUE(owner_scope,owner_id,list_type,slot_index)` 的前缀。
  在今天的规模下无所谓，但**我没有测过**，如果日后清理器要定期扫全库，这里值得复看。
- **没有验证「档案里不存在一件被判为有身份、同时又挂着消耗效果的物品」**。CK-018 结论 2 依赖这一点，
  我核的是代码里没有守卫、且调用链只有 item-use 一条；**没有去 PVF 档案里穷举**。
- **没有跑过 `Server/tests/Integration`，也没跑 `Dfo.Server.Abstractions.Tests`／`Dfo.Server.Utilities.Tests`**——
  这三个在 `dotnet test Server/86JP.Server.slnx` 的输出里**没有出现测试程序集摘要行**，与基线口径（7 个程序集）一致。我按 slnx 实际跑出来的 7 行抄的数。
- **没有做本 Task 不要求的那种「撤回修复」全量变异验证**；我只对「区分堆叠物」那一条做了定点变异。

### 不确定的地方

- **命名 `ReadUnreferencedAsync`**：仓库里更常见的措辞是「什么也不 hold 它」。我选了与 Task 标题／Spec 措辞
  （「没有任何槽引用」）对齐的词。若评审偏好 `ReadUnheldAsync` 之类，改名是纯机械的。
- **`FindAsync` 的 reader lambda 提成 `Read` 方法**：这一处不是 RQ-007 必需的，是为了不把 17 列映射写两遍。
  语句、列序、行为都没动，`WorldAndSlotTableTests` 里既有的 `FindAsync` 判据全绿。若评审认为这超出了本 Task 该碰的范围，可以退回内联，两处各自一份。
- **本轮不清扫这一点，我只写在了接口文档与本记录里，没有写进 `AGENTS.md`**——按范围划分，`AGENTS.md` 属 T-012。

## Independent Quick Review — round 1 (QR-08-1)

- **Verdict: `passed`**
- 评审的契约版本：Contract revision 1，无修正案。
- Owned diff 身份：HEAD `51cd99c2`，全部未提交。本轮所属改动＝
  `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`（新增 `ReadUnreferencedAsync` 声明及其 `<remarks>`，`IWorldItemTable` 内 `189-222` 行区段）、
  `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs`（`ReadUnreferencedAsync` 实现 `378-425`，抽出的 `private static WorldItemRow Read(SqliteDataReader)` `427-432`）、
  `Server/tests/Dfo.Server.Persistence.Tests/Inventory/WorldAndSlotTableTests.cs`（`-- what nothing holds --` 三条判据 `357-435`，`Potion` 助手 `33-34`）。
  工作区其余 63 个条目属 TASK-02..07，未计入本轮。
- 评审与执行方为不同上下文；本轮只读代码与任务文件，唯一的写是下述受控变异，已逐字节还原。

### 我实际跑过 / 读过的

| 项 | 我做的 | 结果 |
|---|---|---|
| CK-017 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`，逐程序集摘要行抄录，未用 `grep -c` | Protocol 521/0/0、Networking 52/0/0、Pvf 60/0/0、Host 6/0/0、Infrastructure 233/2/0、Persistence 311/0/0、Application 1022/0/0 → **2205 通过 / 2 跳过 / 0 失败**，与执行方逐格一致 |
| 字节锚 | 对 `SelectCharacterAcceptedTranscriptTests.cs` 跑 `git diff --stat` 与 `git status --short` | 两者均为空，文件未动 |
| 字节锚单跑 | `--filter "…SelectCharacterAcceptedTranscriptTests\|…InventoryDeltaLift"` | **19 通过 / 0 失败 / 0 跳过** |
| 定点变异 | 文件级备份到 scratchpad → `sed -i '412,418d'` 删掉第二条 `NOT EXISTS` → 重建重跑 `WorldAndSlotTableTests` | **失败 1 / 通过 17**，唯一变红者 `A_piles_row_is_not_one_of_those_although_no_slot_names_it`，报文 `Expected [3] / Actual [1, 3]`，其中 `1` 正是先插入的药水行 —— 即「药水被报成孤儿」，与声称逐字吻合 |
| 还原 | `cp` 回原文件 → `sha256sum` | 变异前后同为 `f091388fd917cf805422305c1fb82e211f6c9e91efd28de4090ebef3b0e9df7e`，**逐字节一致**；重建重跑 `WorldAndSlotTableTests` **18 通过 / 0 失败**。全程未用任何 git 还原类命令 |
| BOM | 三个改动文件首三字节 | 均为 `757369`（`usi`），无 `ef bb bf` |
| 工作区完整性 | `git status --short \| wc -l` | 评审前后同为 66，无增删 |

### 一、孤儿查询的区分是否成立 —— 成立，且它依赖什么我独立确认过

我读的是 `InventoryItemTables.cs:398-425` 的语句本身，不是注释。

- 条款 (1)（`409-411`）排除「有 `kind='world'` 槽行以 `id = items.item_uid` 指名的行」。
- 条款 (2)（`412-418`）排除「四个位置列与某条 `kind='template'` 槽行完全相同的行」。
- **区分成立的两个前提我逐条核过**：
  - `CharacterDatabaseSchema.cs:186` `items` 上有 `UNIQUE(owner_scope, owner_id, list_type, slot_index)`；`:187` `item_slots` 上有同一组四列的 `UNIQUE`。
    所以「一行的位置上找到的槽行」至多一条，且必是它自己位置上的，绝不会是别的 `items` 行的。执行方这句记准了。
  - 结算确实按这个形状写：`SqliteItemDomainStore.cs:586-598` 的 `HoldAsync` 用 `item.WorldClass is Interchangeable` 决定 `kind`，
    堆叠物写 `'template'` ＋ `id = item.Core.ItemId`（模板号），有身份物品写 `'world'` ＋ `id = identity`。
    所以「槽行对堆叠物指的是模板号、永远不等于 `item_uid`」是代码事实，不是推测。
- 判据 `Potion` 助手（`WorldAndSlotTableTests.cs:33-34`）用 `StackableItemCore.Create(2, itemId, count)`，`ItemKind == 2` 正是「更正 C-1」里 `Marker16 = -1` 原样上线的那一类，造核形状忠实。
- 三条判据把两个方向一起钉住，且变异验证证明判据 3 真的会因丢掉 (2) 而变红。**这一项无异议。**

### 二、CK-018 —— 我逐条核过，同意「不存在销毁通路」，未触发停止点

| 执行方的依据 | 我核到的位置 | 我的判断 |
|---|---|---|
| 清空槽位五处 | `ItemContainers.cs:97`（Relocate，Clear 后紧跟 `PutAsync` 到目标槽）、`:153`（Merge，整堆倒空才 Clear，且已先 `Put` 到目标）、`:191`（Consume，`remaining == 0` 才 Clear）、`MoveItemSpaceSlice.cs:336`、`:400` | `grep -rn "ClearAsync" Server/src/` 只有这五处调用（另三处是 `IItemContainer.cs:158`、`InventoryTables.cs:263` 的声明与 `ItemContainers.cs:290` 的实现）。**属实，五处全在移动中** |
| Merge 被 `StackLimit > 1` 挡住 | `ItemContainers.cs:490-492`：`placement.StackLimit > 1 && displaced.Count + moveCount <= placement.StackLimit ? Merge : 拒绝 MergeExceedsStackLimit` | 属实。有身份物品 `StackLimit == 1` 时走拒绝分支 |
| Consume 只经 item-use | `grep -rn "ConsumeAsync" Server/src/` 唯一调用方 `ItemUseEffectDomainService.cs:83`；效果由 `:34-35` 的 `rule.IsConsumedOnUse` 产生 | 属实，**并且执行方的自陈也属实**：代码里确实没有任何守卫拦住「档案给一件有身份物品挂上 `IsConsumedOnUse`」。这是数据保证不是代码保证，执行方明说了，我认可这个诚实度 |
| `DeleteOwnExceptAsync` 的保留清单含每一件有身份物品 | `InventoryEntryAssembly.cs:139-147` 保留清单＝租约里每一件 `Identity`；`InventoryItemTables.cs:80-100` 两条 SQL 只按 `item_uid NOT IN (…)` 与 `list_type NOT BETWEEN 19 AND 23` 删 `items` | 属实。物品要被对账删掉必须先离开容器，而离开容器只有上面五处 |
| 删角色是软删 | `CharacterRosterService.cs:233-235` 只调 `MarkDeletedAsync` | 属实 |
| `SqliteWorldItemTable.DeleteAsync` 生产侧无人到达 | 全仓 `grep`，只有判据 | 属实 |
| 「销毁也不会产生孤儿」 | `SqliteItemDomainStore.cs:493-510` `EmptyAsync` 在同一条命令里删 `items` 与 `item_slots` | 属实，两条 DELETE 就在那里 |

**我没有发现任何销毁通路，阻塞条件未触发。** 我独立试图证伪的一条也没成：`FlushAsync`（`:205-245`）先 `LiftAsync`
（`:442-453`，把幸存者的 `list_type` 置 `NoContainer = -1`、`slot_index = item_uid`）再 `EmptyAsync`，
所以移动中幸存的行不会落进 `EmptyAsync` 的位置谓词。

### 三、既有判据未被削弱 —— 我看了，是纯重构

`FindAsync`（`InventoryItemTables.cs:361-375`）现在把内联 lambda 换成 `Read`（`427-432`）。
17 列的 `SELECT` 列序与 `ReadUnreferencedAsync` 完全相同，`Read` 的取数序 `0→item_uid`、`1→core`、`2→Avatar`、`9→Creature` 与列序对齐。
**无法与提交基线逐字对比**：`SqliteWorldItemTable` 在 HEAD `51cd99c2` 里不存在（`git show HEAD:…` 无输出），
所以我用行为侧证据代替 —— TASK-06／07 落下的 `AvatarDetailFollowsTheIdentityTests`、`CreatureStateFollowsTheIdentityTests`
与 `WorldAndSlotTableTests` 里既有的 `FindAsync` 判据在我这次全量跑里全部通过。
**没有看到任何断言被削弱或删除。** 这处抽取落在允许清单内，不算越界。

### 四、特别请求的判断（一）：这个交付物满足 AC-009 吗 —— **满足，不欠交付**

我独立读了 spec 原文，不采信控制器的定向结论：

- **AC-009（`spec.md:124`）**：「一件有身份的物品存在于世界侧，且没有任何槽引用它 → 查询无人引用的物品 → **它出现在结果中**」。
  **EV-005（`spec.md:143`）**：「造出无人引用的物品并查询到它」。
  两处都只要求**包含**，没有一处要求**恰好**或**不含他物**。
  `A_thing_no_position_holds_is_found_by_asking_for_those`（`WorldAndSlotTableTests.cs:365-377`）正是这个场景，我跑过，绿。
  **按字面，AC-009 被满足。**
- **误报是否让它不满足？我判断不。** 三条理由：
  1. **误报的根在允许清单之外，本 Task 无权修**。我复核了执行方指的两个写入方：
     `CharacterRosterService.cs:214` 的 `items.InsertOwnAsync`（建角色的初始装备；其后 `:220-225` 只写六张状态表，全文无 `item_slots`）、
     `InventoryEntryAssembly.cs:181/185` 的 `UpdateOwnAsync`／`InsertOwnAsync`。
     `grep -rn "item_slots" Server/src/` 全仓只有 11 处，写侧只有 `SqliteItemDomainStore.cs:503/589` 与 `SqliteItemSlotTable`；
     那两个写入方所在的文件都不在本 Task 的允许清单里。
  2. **在允许清单内写不出一条不误报的查询**。入场存盘写出的行与真正的孤儿在两张表上**状态完全相同** ——
     都是「`items` 有行、`item_slots` 无行、位置上无 `template` 槽行」。没有任何谓词能分开它们。这不是执行方偷懒，是边界内不可解。
  3. **限制被如实、就地记下了**：`InventoryTables.cs:213-220` 的 `<remarks>` 原文点名了那两个写入方
     （`InsertOwnAsync`／`UpdateOwnAsync` 与「建角色时穿着的装备」），措辞与代码一致。
- **但我要给最终评审留一条承接风险（不是本轮缺陷）**：RQ-007 的「可以被查询出来」成立，
  「查出来的都是孤儿」**不成立**；在跑过入场存盘的真实库上这条读没有安全的生产消费者。
  T-011 必须真的补完槽表写入，否则这条读永远只在判据里可用。这一条应由最终评审在 T-011 之后回看。
- **顺带一条执行方没写、我认为控制器该知道的**：`DeleteOwnExceptAsync`（`InventoryItemTables.cs:80-100`）
  删除的恰好就是 `ReadUnreferencedAsync` 会报出的那一类行（该角色名下、不在租约身份清单里、`list_type` 不在 19-23）。
  也就是说**一个角色的孤儿在下一次入场存盘时已被对账顺手清掉**。这不违反「本轮不清扫」（那说的是不新增清理器），
  但它意味着这条读对经过存盘的角色给出的答案是**跨存盘易逝的**。

### 五、特别请求的判断（二）：位置列依赖 —— **记准了，且不构成本轮缺陷**

- **记准了**。`InventoryItemTables.cs:390-397` 与 `InventoryTables.cs:210-216` 两处都写了；
  我对着 `CharacterDatabaseSchema.cs:186/187` 的两个 `UNIQUE` 核过，唯一性论证成立，措辞没有夸大。
- **「行上今天没有别的东西能说这件事」这句我特意去证伪，没证伪掉**。
  `StackableItemCore.Create`（`ItemContainers.cs:31-39`）确实写死 `Marker16 = -1` 与 `RandomOptionChangedIndex = byte.MaxValue`，
  但那两个值**不是堆叠物专属** —— 本 Task 的判据里 `Sword()`（`WorldAndSlotTableTests.cs:29-30`）也经同一个 `Create` 拿到 `Marker16 = -1`。
  所以 core 里没有可信的「我是堆叠物」自述，执行方这句成立。
- **不构成本轮缺陷**，因为边界内没有替代解：给行加一个自述列要改 `CharacterDatabaseSchema.cs`（**不在允许清单**），
  而改用 core 的 kind 字节被 RQ-006／AC-007 的设计明确禁止（分类以档案为准，存储里的 kind 值恰恰是不可信的那个）。
  这是**纯粹的后续债，且已带着代价交给 T-011**，不是本轮该还的。

### 六、其余核对与遗留观察（均非阻塞）

1. **契约的禁令都守住了**：没有新增以 `DomainStore` 结尾的类型（新成员挂在 `IWorldItemTable` 上）；
   没有改 schema、结算、抬起／落下；没有引入凭证往返（`ReadUnreferencedAsync` 只交出 `WorldItemRow`，不回写）。
2. **「本轮不清扫」写到位了**：`InventoryTables.cs:199-203` 的 `<remarks>`（"Nothing here removes them"）＋任务记录，符合 T-008 步骤 3。
3. **`HoldAsync` 无 `ON CONFLICT`**（`SqliteItemDomainStore.cs:588-591`）属实，`item_slots` 确有四列 `UNIQUE`（`CharacterDatabaseSchema.cs:187`）。
   今天靠 `EmptyAsync` 先删而不撞，执行方的描述准确。范围外，不要求本轮修。
4. **TDD 次序无直接证据**：任务记录没写「判据先红后绿」。我不把它当缺陷 ——
   定点变异验证提供了同一保证的更强形式（判据确实会因实现被撤而变红），且我独立复现了它。
5. **一处我未查清、也不要求查清**：`ReadUnreferencedAsync` 是全表扫描、无所有者过滤，执行方自陈没量过性能也没看 `EXPLAIN QUERY PLAN`。
   今天无生产消费者，AC-009 不要求，留给清理器那件事。
6. **未见偶发失败**：本轮的全量与定向运行没有出现任何非确定性失败，无需按「异常类型＋栈根＋复跑即绿」归因。

### 需要的整改

**无。** 本轮未发现可在边界内就地修复的缺陷。

## Controller Disposition — DSP-TASK-08-01

`QR-08-1` 给出 `passed`，独立重做了变异并逐条核了停止点。控制器采纳，并记三条。

### 一、AC-009 满足，本轮不欠交付——采纳快审的判断

快审的理由控制器认同且比原先的说法更硬：
**误报在本 Task 的边界内不可解**——入场存盘写出的行与真孤儿在两张表上**状态完全相同**，
任何谓词都分不开；而两个写入方所在文件不在允许清单里。
AC-009 / EV-005 的措辞只要求「它出现在结果中」，**没有要求「恰好」**。

限制已原文写进 `InventoryTables.cs:213-220` 的 `<remarks>`。
→ 承接风险已在 `AMD-TASK-11-01` 定向：**T-011 不补完槽表，这条读就永远只在判据里可用。**

### 二、快审查出、两方都没写的一条——记进成果记载

**`DeleteOwnExceptAsync`（`InventoryItemTables.cs:80-100`）删除的，恰好就是
`ReadUnreferencedAsync` 会报出的那一类行。**

即一个角色的孤儿**在下一次入场存盘时已被对账顺手清掉**——**这条读的答案是跨存盘易逝的**。

不违反「本轮不清扫」（那说的是不新增清理器），但它同时解释了孤儿为什么不会堆积，
也进一步限定了这条查询今天的实际用处。
→ **写进 T-012 的成果记载**，与「T-011 不补完槽表则这条读只在判据里可用」并列，
   **不要只写「孤儿可查询」就了事。**

### 三、CK-018 的结论带一个限定，必须原样带下去

「今天不存在能销毁一件有身份物品的生产通路」成立，快审逐条核过。
**但其中一条依据是有限定的**：`ConsumeAsync` 只经 item-use 这一点，
**由档案数据保证，代码里没有守卫**（`ItemUseEffectDomainService.cs:34-35` 只看 `rule.IsConsumedOnUse`）。
执行方自陈属实，快审复核属实。
→ **写进 T-012 的成果记载**。这与 TASK-07 那条「CHECK 只对新建库无条件成立」是同一类：
   **结论对，但支撑它的前提不是代码给的。**

### 四、位置列依赖是纯后续债，非本轮缺陷——采纳

快审特意去**证伪**「行上有没有别的东西能说它是堆叠物」，**没证伪掉**
（`StackableItemCore.Create` 的 `Marker16 = -1`，判据里的 `Sword()` 也带着）。
替代解要么改 schema（不在允许清单），要么信 kind 字节（**被 RQ-006 / AC-007 禁止**）。
依赖已记准并交给 T-011。
