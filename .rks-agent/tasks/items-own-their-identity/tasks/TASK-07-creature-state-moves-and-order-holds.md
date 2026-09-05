# TASK-07 — 契约兽实例状态迁入世界表，清单顺序与标识宽度不变

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 把契约兽实例状态的 8 列迁入世界表；保持清单帧的条目顺序与计数上限语义；标识宽度有判据。
- Referenced spec requirements: RQ-003, RQ-008
- Referenced plan sections: T-007
- Owned boundary: BD-011, BD-014
- Permitted files or interfaces: `Server/src/Dfo.Server.Abstractions/Characters/ICharacterDomainStore.cs`、`Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs`、`Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterDomainStore.cs`、`Server/tests/`
- Dependencies: TASK-06
- Required dependency outputs: 世界表已承载时装细节，形状已验证
- Acceptance conditions: 契约兽清单帧哈希守卫仍绿；条目顺序在迁移前后等价且其依据可指认；存在判据断言契约兽标识可用 32 位有符号整数表示且写出不抛。
- Task-specific authorization: Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做一次。范围外一律不碰：定时清理器、旧库自动升级、地上掉落／交易托管／拍卖挂单／邮件附件的启用、契约兽领域本身、契约兽槽位 24 还是 25 的裁定（沿用 24）、`Pet=7` 容器装什么、契约兽技能、`character_condition` 的五列。
- Required methods: test-driven-development；改动前先让判据表达期望，每个 Task 结束时全量判据全绿，**不允许攒红**。计划里已定、本 Task 不得自行推翻的五条：世界侧**一张**表不是三张；`items` 就地变成世界表且位置另立槽表；
今天的 DELETE+INSERT 结算**原样保留只换作用对象**（物品行 → 槽行），不得为唯一键冲突另发明机制；
契约兽对外标识用**单一窄空间**（i32）加分配守卫，不得另设第二个标识；**不引入新的 `store` 读法**，
世界侧不新增以 `DomainStore` 结尾的类型。
- Required checks:

- CK-015 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → 不低于 2131 通过 / 2 跳过 / 0 失败
- CK-016 `Non-command: 检查契约兽清单帧的条目顺序在迁移前后一致`

- Required evidence: 迁移 diff；帧哈希守卫输出；顺序等价的依据；标识宽度判据名。

### 保持条件

契约兽清单帧的条目顺序今天由 `character_creatures.sort_order` 决定，**那是 wire order**——`0x0069` 按它逐条写。迁移后必须有等价的稳定全序，且条目计数上限（u8，最多 255 只）语义不变。

## Scope note — round 1 (written before the edit it declares)

One file outside the permitted list is touched, and only its documentation:

- `Server/src/Dfo.Server.Abstractions/Persistence/CharacterEntryTables.cs`, line 195 —
  the one-line summary above `ICharacterCreatureTable` says "The `character_creatures` table."
  After this round no statement anywhere reads or writes that table, so the sentence points at
  a table nobody reads. **Doc comment only; no signature, no behaviour.** Left alone it would be
  exactly the kind of comment that has already drifted to the opposite of the code in this repo.
  The table itself stays in the schema — retiring it is T-011.

## Execution Result — round 1

### 改了哪些文件，每处为什么

**生产代码（四个，其中一个已在上面的 Scope note 里声明）**

1. `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs`
   —— `SqliteCharacterCreatureTable` 改读 `items` 的契约兽行：
   `SELECT item_uid, creature_field04, creature_mode_flag, creature_progress_value,
   creature_mode1_field0a, creature_mode1_field0b, creature_field_after_value,
   creature_text, creature_tail_flag FROM items
   WHERE owner_scope='character' AND owner_id=@cid AND creature_field04 IS NOT NULL
   ORDER BY item_uid`。
   `CharacterPetStorageRow.CreatureKey` 取 `item_uid`；`SortOrder` 不再是存储列，
   由查询名次现算，**从 0 起密排**（旧列就是这个形状，且调用方 `CharacterEntryStateServices`
   仍按它排序）。「一行是不是契约兽」由八列的第一列决定，与时装靠 `avatar_uid` 判定同构。

2. `Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterDomainStore.cs`
   —— `CreatureSection` 的读写改到 `items`，键是 `item_uid`：
   读 `SELECT item_uid, creature_field04 FROM items WHERE owner_scope='character'
   AND owner_id=@character AND creature_field04 IS NOT NULL`
   （原来「`field04` 为 NULL 的行本节不持有」的逐行判断，原样搬进 WHERE，语义不变）；
   写 `UPDATE items SET creature_field04=@condition WHERE owner_scope='character'
   AND owner_id=@character AND item_uid=@key AND creature_field04 IS NOT NULL`
   （`OnlyWhenAtMostZero` 分支为 `AND creature_field04<=0`，在 SQLite 里已蕴含非 NULL）。
   **加 `IS NOT NULL` 不是保险丝而是必需**：这一列同时是「这行是不是契约兽」的判据，
   写到一个不是契约兽的行上，不是「写空了」而是「凭空造出一只」。
   `!= 1` 抛 `InvalidDataException` 的既有行为原样保留。

3. `Server/src/Dfo.Server.Abstractions/Characters/ICharacterDomainStore.cs`
   —— 仅文档：说明 `Creatures` 这一节的键就是世界身份，也就是清单帧每条第一个字段写的那个数；
   并给 `StoredCreature.CreatureKey` 补了一句 param 说明。**签名未改。**

4. `Server/src/Dfo.Server.Abstractions/Persistence/CharacterEntryTables.cs`
   —— 仅那一行 `<summary>`（见上面的 Scope note）。**这是权限清单之外的唯一一处，且只有注释。**

**判据（两个改编排、两个新增）**

5. `Server/tests/Dfo.Server.Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs`
   —— 种子从 `INSERT INTO character_creatures VALUES(22,0,400,1,2,3,4,5,6,X'07',8)`
   换成同值的 `items` 行，**显式给 `item_uid=400`**。
   **黄金文本一字未改**：`Features.Pets[0].SortOrder = 0`、`CreatureKey = 400`、
   八个值逐条仍是 1/2/3/4/5/6/0x07/8。期望值没有为迁就实现被改写。

6. `Server/tests/Dfo.Server.Persistence.Tests/Characters/SqliteCharacterEntryStateRepositoryTests.cs`
   —— 同一处种子的同样迁移（该用例本身不断言 Pets）。

7. **新增** `Server/tests/Dfo.Server.Persistence.Tests/Inventory/CreatureStateFollowsTheIdentityTests.cs`（6 条）
   - `TheListRunsInAscendingIdentityAndTheRankIsDenseFromZero`
   - `TheListDoesNotReorderWhenACreatureMoves`
   - `ACreatureKeepsAllEightOfItsFactsFieldByFieldWhenItMoves`
   - `TwoCreaturesSwappingPositionsEachCarryTheirOwnEightFacts`
   - `TheConditionWriteReachesTheCreatureNamedAndNoOtherRow`
   - `AConditionWrittenAgainstSomethingThatIsNotACreatureIsRefused`

8. **新增** `Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/CreatureIdentityFitsTheWireTests.cs`（3 条）
   - `TheLargestIdentityItWillAllocateIsWrittenIntoTheCreatureList`
   - `AnIdentityBeyondWhatTheWireCanSayIsRefusedAtAllocation`
   - `TheListCountsCreaturesInOneByteAndRefusesWhatItCannotCount`

### CK-015 实际输出（逐项目，手数不用 grep -c）

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 233 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 308 | 0 | 0 |
| Dfo.Server.Application.Tests | 1022 | 0 | 0 |
| **合计** | **2202** | **2** | **0** |

60+52=112；+521=633；+6=639；+233=872；+1022=1894；+308=**2202**。
基线 2193 / 2 / 0 → **不低于基线**（Protocol 518→521 是新增 3 条，
Persistence 302→308 是新增 6 条，其余逐项目与基线相同）。

**字节锚**：`SelectCharacterAcceptedTranscriptTests.cs` 的 `git diff --stat` 为空，**一字未改**。
单跑该类 + `InventoryDeltaLift`：**19 通过 / 0 失败**。
第 13 条（`0x0069`）的哈希 `D86BC0EF19406D5B6E0A2A3925B6A551778813BF82D04509EF3B82E0E4E385AE` 仍绿。
（该守卫走的是内存构造的 `SelectCharacterResult`，不经库；本轮改的是持久层与它上面的读，
所以它绿说明「编解码与投影形状没动」，**不能**单独说明库端顺序对——那由 CK-016 的判据说。）

### CK-016 顺序等价的依据（可指认）

**迁移前**：`SqliteCharacterCreatureTable` 是
`... FROM character_creatures WHERE character_id=@cid ORDER BY sort_order`，
`CharacterEntryStateServices.cs:167` 再 `OrderBy(SortOrder)`，
`StateSynchronizationPayloadCodec.Entry.cs:44` 按传入顺序逐条写。**wire order = `sort_order` 升序。**

**迁移后**：同一个读改成 `... FROM items ... ORDER BY item_uid`，名次现算为 0..n-1，
下游两处一字未动。**wire order = `item_uid` 升序。**

两者等价的依据是**三条可指认的事实**，不是「看起来差不多」：

1. **都是这个角色的契约兽上的全序。**
   `character_creatures` 的主键是 `(character_id, sort_order)`，故 `sort_order` 在一个角色内唯一；
   `items.item_uid` 是 `INTEGER PRIMARY KEY AUTOINCREMENT`，全表唯一，限制到该角色后仍唯一。
2. **都是取得顺序。** `sort_order` 是每角色密排序号；`item_uid` 由 `AUTOINCREMENT` 单调分配，
   永不重发，所以先取得的契约兽的号一定小。
3. **都不随位置变。** 这是最要紧的一条，也是唯一能把「等价」和「碰巧」分开的那条：
   全仓**没有任何语句写过 `character_creatures.sort_order`**（本轮前核对：
   对 `character_creatures` 的写只有 `SqliteCharacterDomainStore` 对 `field04` 的 UPDATE，
   零 INSERT、零对 `sort_order` 的 UPDATE），所以旧顺序不随移动改变；
   而 `item_uid` 是「移动被定义为不改变的那个东西」——TASK-04 的抬起／落下
   （`SqliteItemDomainStore.LiftAsync` / `LandAsync`）都以 `WHERE item_uid=@uid` 定位且不写该列。
   **改按位置排（`ORDER BY list_type, slot_index`）就不再等价**，这正是变异 M1 证的。

附带一条同源的性质：读的过滤条件是「这行是不是契约兽」而不是「这行在哪」，
所以一只被 TASK-04 抬起到 `list_type = -1` 的契约兽仍在清单里——
这与旧读法完全独立于位置是同一件事。

**变异证据（文件级备份 → 改 → 还原 → sha256 逐字节核对，四次全部还原成功）**

| 变异 | 改了什么 | 结果 |
|---|---|---|
| M1 | `ORDER BY item_uid` 改成 `ORDER BY list_type, slot_index` | `CreatureStateFollowsTheIdentityTests` **4 红**（含两条顺序判据） |
| M2 | 去掉写路径的 `AND creature_field04 IS NOT NULL` | `AConditionWrittenAgainstSomethingThatIsNotACreatureIsRefused` **红** |
| M3 | schema 的 `CHECK (item_uid BETWEEN 1 AND 2147483647)` 上界放宽到 4294967295 | `AnIdentityBeyondWhatTheWireCanSayIsRefusedAtAllocation` **红** |
| M4 | `SqliteItemDomainStore.LandAsync` 落下时顺手改一列契约兽状态 | `ACreatureKeeps...WhenItMoves` 与 `TwoCreaturesSwapping...` **红** |

还原核对（sha256 与改动前备份逐字节一致）：
`CharacterEntryStateTables.cs b53f01ac…`、`SqliteCharacterDomainStore.cs 6769b314…`、
`CharacterDatabaseSchema.cs 6fe6335b…`、`SqliteItemDomainStore.cs 6bb74a7f…`。
**全程未使用任何 git 还原类命令。**

### 计数上限语义如何保住

上限从来不在库里，在编码器：`StateSynchronizationPayloadCodec.Entry.cs:41`
`if (pets.Count > byte.MaxValue) throw CountError("creature", "u8");`，随后
`writer.Write(checked((byte)pets.Count))`。**这一行本轮一字未改**，
库端只换了这份列表从哪来、按什么排，没有换它有多长。

本轮把它从「没人看着」变成「有判据钉着」：
`TheListCountsCreaturesInOneByteAndRefusesWhatItCannotCount` 断言 255 条能编码且计数字节为 255，
256 条抛 `InvalidDataException("creature count cannot be represented as u8.")`
——两侧都钉，所以「悄悄截断成 255 条」这种改法也会红。此前全仓没有任何判据碰过这个上限。

### `creature_key` 与世界身份的对应关系怎么处理

**结论：`creature_key` 这个列退役，世界身份 `item_uid` 接管它的全部职责。不设第二个标识。**

- 库里：`character_creatures.creature_key` 现在**没有任何语句读或写**
  （核对确认：`character_creatures` 只剩 schema 声明与若干注释引用）。
  表本身仍留在 schema 里，`SchemaOwnershipTests` 对它的断言也照旧——**退役属 T-011**，本轮不动。
- 帧里：`0x0069` 每条的第一个 i32，本轮之前是 `creature_key`，之后是 `item_uid`。
  两者共用同一个编号空间（`CreatureDomainDesign.md`：「两个字段共用同一编号空间」），
  所以这不是换了一种编号，是**把这个空间的分配权交给了唯一有资格分配它的那张表**，
  分配上界由 TASK-02 的 `world_identity_fits_the_wire` 守住。

**必须如实写明的一处遗留耦合**：
`Server/src/Dfo.Server.Application/Characters/SelectCharacterPostEntryServices.cs:120`
仍是 `var creatureKey = (long)core.Value;`——它从装备槽 24 那件物品的 `item_core.Value`
取键，再拿这个键去 `Creatures` 一节查。

- 本轮**之前**，这条通路成立的前提是「库里种下的 `creature_key` 等于 `core.Value`」；
- 本轮**之后**，前提变成「该行的 `item_uid` 等于 `core.Value`」。

**耦合不是本轮引入的，但「必须对上的是哪个数」变了。**两侧都只靠数据自己对齐，没有约束保证。
**今天这不产生可观察后果**，因为全仓**零处生产代码创建契约兽**：
`character_creatures` 零 INSERT；`SqliteItemDomainStore.FillAsync` 新插入的行不写八列中的任何一列，
所以结算也造不出契约兽行。该复活通路的既有判据
`SelectCharacterEntryLifecycleServiceTests.PetRevival_EmitsOnlyTheCommittedDeadToAliveTransition`
走的是内存 `RecordingCharacterDomainStore`，与库端键无关，本轮仍绿。
**我没有改这一行**：它在权限清单之外，且它该改指 `InventoryItemState.Identity` 还是别的，
属于契约兽领域／T-009、T-010 的裁定，不该我单方面定。**请评审明确它归谁。**

### 标识宽度判据的名称

`Dfo.Server.Protocol.Tests.Game.StateSynchronization.CreatureIdentityFitsTheWireTests` 下两条：

- **`TheLargestIdentityItWillAllocateIsWrittenIntoTheCreatureList`** —— AC-010 正面：
  把 `sqlite_sequence` 推到 `int.MaxValue - 1`，经 `SqliteWorldItemTable.InsertAsync`
  **由列分配**出 `2147483647`（不是构造出来的数），再经 `SqliteCharacterCreatureTable` 读回、
  经 `CharacterFeatureEntryProjection`（入场自己用的那个投影）投影、
  经 `CreatureListMessage.EncodeBody()`（就是 `0x0069` 的体）写出：不抛，
  且体的第 1 字节为条目数 1、第 2..5 字节小端等于 `2147483647`。
- **`AnIdentityBeyondWhatTheWireCanSayIsRefusedAtAllocation`** —— AC-010 反面：
  序列推到 `int.MaxValue` 后再分配，被 `world_identity_fits_the_wire` 拒绝，
  抛出点名该约束的 `InvalidOperationException`（「no world identity left to give」）。

## Executor Self-check — round 1

### 我发现的问题

1. **遗留耦合一处，已在上面单列**：`SelectCharacterPostEntryServices.cs:120` 的
   `core.Value` 取键。**它今天不会出错**（没有生产通路能造出契约兽），但「必须对上的是哪个数」
   确实被本轮换了。我按「不得默默扩大范围」的要求没有改它，请评审裁定它属于哪个 Task。
2. **权限清单之外动了一个文件**：`CharacterEntryTables.cs` 的一行 `<summary>`。
   已在 Task 文件的 Scope note 里事先写明。只改注释，无签名、无行为。
   若评审认为不该动，还原成一行原文即可，不影响任何判据。
3. **`SortOrder` 从存储列变成现算名次。** `CharacterPetStorageRow` 仍有这个字段
   （该记录在 `ICharacterEntryStateServices.cs`，不在我的权限清单里，未改），
   `CharacterEntryAssembly` 对它的 `< 0` 校验也照旧。名次现算永远 ≥ 0，
   所以那条校验现在恒真——**不是死代码，但确实不再可能触发**。留着未动。
4. **未装备的契约兽今天没有约定的住处。** 旧模型里它只是一行 `character_creatures`，
   与位置无关；新模型里它必须是一行 `items`，而 `items` 有位置四列。
   我的判据把它们放在主背包槽里，**那是夹具的选择，不是生产代码的既有行为**
   （生产代码今天根本不造契约兽）。真正该放哪，属于契约兽领域／T-011。

### 我**没有**验证的东西（明说）

- **没有跑过真实客户端**，一次都没有。全部结论来自判据与字节锚。
- **没有验证 255 只契约兽经过真实数据库**的端到端路径。计数上限判据是直接构造
  255／256 个投影状态去打编码器的，不经 SQLite。
- **没有验证 `Pet = 7` 容器、槽位 24/25 之争、`character_condition` 那五列**——范围外，一律没碰。
- **没有验证孤儿（无槽引用）契约兽的查询**——属 T-008。
- **没有验证旧库升级**：`character_creatures` 里已有的行，本轮之后不再被任何读取到。
  这符合「旧库作废重建」的既定裁决，但我没有、也没被要求写任何迁移。
- **没有对新增的 9 条判据逐条做变异**。做了 4 次变异（M1–M4），覆盖到 6 条；
  未被变异直接击杀的是 `TheConditionWriteReachesTheCreatureNamedAndNoOtherRow`（M1 下红过，
  但那是顺序牵连不是它自己的靶心）、`TheLargestIdentityItWillAllocate...`、
  `TheListCountsCreaturesInOneByte...`。

### 不确定的地方

- **顺序等价的第 2 条依据（「都是取得顺序」）是推理，不是实测。**
  `sort_order` 全仓无写入者，所以「它按取得顺序排」这句在今天的代码里**无法被证伪也无法被证实**——
  没有数据产生者。可实测的是第 1 条和第 3 条，而第 3 条（不随位置变）才是把两种排法分开的那条，
  它有 M1 的变异证据。我认为这足以支撑「等价」，但把这层区别如实写在这里。
- `ICharacterDomainStore` 里这一节的键类型仍是 `long`（`IDomainStoreSession<…, long, StoredCreature>`）。
  收窄成 `int` 会连带改 `SelectCharacterPostEntryServices.cs` 与 `RecordingCharacterDomainStore`，
  超出权限清单，所以没做。**窄空间由 schema 的 CHECK 守，不由 C# 的位宽守**；
  这不违反「不设第二个标识」（没有第二个数），但如果评审要求类型上也窄，那要另开一轮。
- 我把契约兽判据文件放在 `Persistence.Tests/Inventory/` 而不是 `.../Characters/`，
  理由是它讲的是世界行而不是角色表；如果评审觉得该挪，纯位置问题。

## Controller Disposition — DSP-TASK-07-01

执行方交回两件。控制器**在快审之前**裁定，快审应按裁定后的口径审。

### 一、遗留耦合归 **T-011**，并为此扩其允许文件

`SelectCharacterPostEntryServices.cs:120` 仍是 `var creatureKey = (long)core.Value;`，
随后拿它去 `rows.Creatures.ReadAsync(place, creatureKey, …)`。

**控制器核实并把性质说准**：耦合本身不是本轮引入的，但**本轮换掉了「必须对上的是哪个数」**——
迁移前要 `creature_key == core.Value`，迁移后要 `item_uid == core.Value`，**两侧都没有约束保证**。
执行方说的「今天不产生可观察后果」属实（全仓零处生产代码能造出契约兽），
但**契约兽一旦可获得，这条复活路径会静默空转**——查不到行就 `return null`，不报错。

**归属裁定：T-011。** 理由是它正是「旧形状的寻址方式」——
按世界模型，槽行已经说了这一格放的是哪一个世界行，**这里根本不该再经 `core.Value` 绕一次**。
T-011 的题目就是「退役旧形状」。
→ **T-011 的允许文件扩至 `Server/src/Dfo.Server.Application/Characters/SelectCharacterPostEntryServices.cs`**，
   要求它**要么改成经身份寻址，要么写明为什么改不了**；两者都不做即为欠交付。
→ 若最终未改，**成果记录必须把它写成一颗具名的雷**，不得只说「今天不可达」。

### 二、越权限清单改的那一行注释——认可

`CharacterEntryTables.cs:195` 的 `<summary>` 原写「The `character_creatures` table.」，
迁移后指向一张没人读的表。执行方**先在 Task 文件写了 Scope note 再改**，只改注释、无签名无行为。

**这正是前面几轮反复要求的做法**（有一轮执行方未事先写明就动了 8 个文件，被记为流程违反）。认可。

### 三、控制器自核的两件（不采信自述）

**字节锚对本轮有真实证明力。** 控制器查过夹具：
`SelectCharacterPayloadCodecTests.cs:881` 有一只真契约兽
`new CharacterPetRuntimeState(44, 1, 1, 2, 3, 4, 5, [0x50], 6)`——
九个字段全非零，且 **`ModeFlag = 1`**，正是那个判别位，会让编码器多写两字节。
**所以第 13 条哈希走的是变长那一支，绿不是空转。**

**`character_creatures` 确已无生产读写。** 全仓命中只剩四处**注释**
（`InventoryTables.cs:58`、`CharacterEntryStateTables.cs:269`／`:280`、`SqliteCharacterDomainStore.cs:196`），
无任何 SQL。表声明留在 schema 属 T-011。

## Independent Quick Review — IQR-TASK-07-01

- Verdict: **passed**
- Reviewed contract revision: 1（含裁定 `DSP-TASK-07-01`，按裁定后口径审）
- Owned diff identity: HEAD `51cd99c2`，全部未提交。本轮所属改动 =
  `ICharacterDomainStore.cs`(+12/-1)、`CharacterEntryTables.cs`(1 行注释)、
  `CharacterEntryStateTables.cs`(+56/-24)、`SqliteCharacterDomainStore.cs`(+46/-…)、
  `EntryLoadCharacterizationTests.cs`／`SqliteCharacterEntryStateRepositoryTests.cs`(各 6 行种子)、
  新增 `CreatureStateFollowsTheIdentityTests.cs`、`CreatureIdentityFitsTheWireTests.cs`。
  未越出裁定后的允许范围。
- 检查过的命令与证据：全量 `dotnet test`（我自己跑的）、两次文件级变异并 sha256 还原核对、
  `git diff --stat`、多次全仓 grep、`head -c 3 | xxd -p`。
- **全程未使用任何 git 还原类命令。**

### 独立核实的结论（逐条，不采信自述）

**1. CK-016 的三条依据 —— 全部可指认，成立。**

- 全序：`character_creatures` 的 `PRIMARY KEY(character_id,sort_order)`
  在 `CharacterDatabaseSchema.cs:202`；`items.item_uid INTEGER PRIMARY KEY AUTOINCREMENT`
  在同文件 `:186`。两者都是该角色契约兽上的全序。✓
- 不随位置变（最要紧的一条）：全仓 grep `sort_order` 与 `character_creatures`，
  对 `character_creatures` 的**命中只剩** DDL(`:202`)、shape 清单(`:282–292`)、
  四处生产注释(`InventoryTables.cs:58`、`CharacterEntryStateTables.cs:269`/`:280`、
  `SqliteCharacterDomainStore.cs:196`)、三处判据注释与 `SchemaOwnershipTests.cs:64` 的断言——
  **零条 SQL**。本轮把两处判据种子也迁走后，`INSERT INTO character_creatures` 在全仓一条不剩。
  执行方「从未有语句写过 `sort_order`」的说法**核实为真**。
  另一侧：`SqliteItemDomainStore.cs:446`／`:472` 的抬起／落下都以 `WHERE item_uid=@uid` 定位，
  从不写该列。✓
- 取得顺序：确如执行方自陈是**推理不是实测**。我补一层更准的说法：既然全仓从无生产者
  创建过 `character_creatures` 行，旧顺序在生产上**根本不可观察**——这让等价性既无法被证伪
  也无法被打破。我认为这足以支撑 CK-016，但依据的强度应记成「旧序无生产者」而不是「旧序按取得排」。
- **观察（非缺陷）**：按身份排等于**放弃了未来让玩家自排契约兽清单的可能**。今天无任何需求要它，
  合同也只要求迁移前后等价，故不计。

**2. 变异 M1 —— 我自己重做，结论一致，并把红的是哪几条钉死。**
把 `CharacterEntryStateTables.cs` 的 `ORDER BY item_uid;` 改成 `ORDER BY list_type, slot_index;`：
`CreatureStateFollowsTheIdentityTests` **4 红 2 绿**，红的是
`TheListRunsInAscendingIdentityAndTheRankIsDenseFromZero`、
`TheListDoesNotReorderWhenACreatureMoves`、
`TwoCreaturesSwappingPositionsEachCarryTheirOwnEightFacts`、
`TheConditionWriteReachesTheCreatureNamedAndNoOtherRow`。
夹具本身有鉴别力：身份 7/3/11 分别落在槽 12/13/11，身份序、插入序、位置序**三者互不相同**，
不是自证。还原后 sha256 `b53f01ac3b5b1b02…` 与备份逐字节一致，重跑 **6 绿**。

**3. 字节锚的证明力 —— 控制器的核实属实。**
`SelectCharacterPayloadTestData` 起于 `SelectCharacterPayloadCodecTests.cs:773`，
`CreateComplete()` 起于 `:826`，`SelectCharacterAcceptedTranscriptTests` 的三处入口都用它。
`:881` 的 `new CharacterPetRuntimeState(44, 1, 1, 2, 3, 4, 5, [0x50], 6)` 对 `CharacterModels.cs:432`
的字段序为 PetKey=44 / Field04=1 / **ModeFlag=1** / Progress=2 / 0A=3 / 0B=4 / After=5 /
Text=[0x50] / Tail=6，**九字段全非零**，`ModeFlag==1` 命中
`StateSynchronizationPayloadCodec.Entry.cs:46` 的判别位，多写两字节。
`AcceptedStaticIdentities[12] == (0, 0x0069)`，即第 13 条确是契约兽清单帧。
**该哈希走的是变长那一支，绿不是空转。** 非阻断。

**4. `character_creatures` 无生产读写 —— 属实**（见第 1 条的 grep 明细）。

**5. 标识宽度的两条判据 —— 我实际变异了一次，结论：一正一负，负的那条钉得住。**
把 `CharacterDatabaseSchema.cs:186` 的
`CONSTRAINT world_identity_fits_the_wire CHECK (item_uid BETWEEN 1 AND 2147483647)` **整段拿掉**：
- `AnIdentityBeyondWhatTheWireCanSayIsRefusedAtAllocation` **红**——
  `Expected: InvalidOperationException / Actual: OverflowException`，
  栈根 `InventoryItemTables.cs:358`（即 `checked((int)allocated.ItemUid)`）。
  **它是真负路径判据，且钉的正是 schema 的那条 CHECK。**
- `TheLargestIdentityItWillAllocateIsWrittenIntoTheCreatureList` **仍绿**——
  这是对的：它是 AC-010 的**正面**（分配得出 `2147483647` 并整数写进帧体），
  拿掉上界本就不该让它红。两条合起来两侧都有。
还原后 sha256 `6fe6335b2215bd3e…` 与备份逐字节一致。

**6. u8 计数上限 —— 两侧都钉。**
守卫在 `StateSynchronizationPayloadCodec.Entry.cs:41`（`throw CountError`）与 `:43`
（`checked((byte)pets.Count)`），本轮未动。新判据 255 侧断言计数字节为 255 且首条身份正确，
256 侧断言 `InvalidDataException("creature count cannot be represented as u8.")`
**逐字比对消息**，所以「悄悄截断成 255」也会红。此前全仓确无判据碰过这个上限。

**7. CK-015 —— 我自己跑的，逐项目，未用 `grep -c`。**

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 233 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 308 | 0 | 0 |
| Dfo.Server.Application.Tests | 1022 | 0 | 0 |
| **合计** | **2202** | **2** | **0** |

与执行方所报**逐项目相同**，≥ 基线 2193/2/0，也 ≥ 合同下限 2131/2/0。两条跳过是既有的
`PvfQuestDefinitionSourceTests` 与 `PvfTownDefinitionSourceTests` 的缺档案跳过，非新增。

**8. 字节锚未动 —— 属实。**
`SelectCharacterAcceptedTranscriptTests.cs` 的 `git diff --stat` 为空。
单跑该类 + `InventoryDeltaLift`：**19 通过 / 0 失败**。

**9. 既有判据未被削弱 —— 属实。**
本轮只动了两个既有判据文件，diff **只有种子那一处**，无断言改动。
`EntryLoadCharacterizationTests.cs:339–348` 的黄金文本**一字未改**：
`SortOrder = 0`、`CreatureKey = 400`、八值仍是 1/2/3/4/5/6/0x07/8。
列序也核过：新 SELECT 的 9 列与 `CharacterPetStorageRow`（`ICharacterEntryStateServices.cs:119`）
的 9 个目标字段逐位对齐，`EntryBlob.Bounded(reader, 7, …)` 正对 `creature_text`。

**10. BOM —— 八个改动文件首三字节均为 `757369`（`usi`），无 `ef bb bf`。**

### 被要求独立判断的一件：分配 → 写进帧，能不能产出超 i32 的值

**结论：在本构建创建的数据库上，不能。这条链是闭合的。**

- **`PetKey` 只有一个来源。** 全仓生产代码里 `CharacterPetStorageRow` 只被
  `CharacterEntryStateTables.cs:308` 造出（取 `items.item_uid`），
  `CharacterPetRuntimeState` 只被 `CharacterEntryStateServices.cs:168` 造出（取前者的 `CreatureKey`），
  终点是 `StateSynchronizationPayloadCodec.Entry.cs:46` 的 `checked((int)pet.PetKey)`。
- **写进 `items` 的口只有三个**，且**没有一个显式给 `item_uid` 赋值**，全靠 AUTOINCREMENT：
  `InventoryItemTables.cs:325`（`SqliteWorldItemTable.InsertAsync`）、
  `SqliteItemDomainStore.cs:534`（`FillAsync`）、
  `InventoryItemTables.cs:151`（`InsertOwnAsync`）。
  CHECK 是**表级**的，所以三个口全部被它拦住。
  **「CHECK 只管 `items` 一张表」在今天恰好是够的——因为 `items.item_uid` 已经是契约兽对外标识的唯一来源。**
- `SelectCharacterPostEntryServices.cs:120` 的 `(long)core.Value` **只当查询键用**，
  `core.Value` 本身是 `int`；它唯一上线的地方是 `:148` 的 `checked((int)mutation.CreatureKey)`，
  由来源类型保证不溢出。**不是宽度漏洞。**

**但有两处必须点名，两处都在本 Task 边界之外，故不计缺陷：**

- **(a) `CREATE TABLE IF NOT EXISTS items` 不会给已存在的表补上这条 CHECK。**
  `git show HEAD:…/CharacterDatabaseSchema.cs` 的 `:167` 表明**当前发布版的 `items` 表没有该约束**。
  在这样一个既存库上，`item_uid` 无人约束，溢出会一路落到
  `Entry.cs:46` 的 `checked((int))`，在**编码时**炸成 `OverflowException`——
  正是这套设计声明不许发生的那种失败。
  这属 TASK-02 的 schema 与「旧库作废重建」既定裁决，执行方也已把「旧库升级未验证」写进自陈。
  **要记的是口径**：「分配上界由 `world_identity_fits_the_wire` 守住」这句
  **只对本构建新建的库无条件成立**，对既存库不成立。建议由控制器转记给 T-011／成果记录。
- **(b) `InsertOwnAsync`（`InventoryItemTables.cs:143–165`）是三个写口里唯一不把该约束失败
  翻译成具名 `InvalidOperationException` 的**，会漏出裸 `SqliteException`。
  它**仍造不出越界身份**（CHECK 照样拒），所以是**报错质量**问题不是宽度洞。边界外，不计。

### 对控制器裁定的看法（仅就性质判断）

`SelectCharacterPostEntryServices.cs` 归 T-011 —— 我不改这个归属，理由也认同。
性质判断**准确**：`:122` 的 `if (creature is null || creature.Field04 > 0) return null;`
确实是「查不到就静默返回」。

补一条**收窄**，请写进 T-011 的记载，免得读成比实际更糟：
本轮之后，`Creatures` 一节的键与帧里的 `PetKey` **是同一个 `item_uid`**，
所以只要 `:120` 的查询命中了，`:133` 的 `value.PetKey == mutation.CreatureKey` 就必然对上。
失败模式**只有「什么都查不到、静默不复活」一种**，
**不存在「复活错了另一只」**。这颗雷是哑的不是乱的。

### 需要的整改

无。本 Task Unit 无本地可整改的阻断缺陷。
上面 (a) 与「按身份排放弃了将来可自排」两条属跨 Task 记载，交由控制器分派，不作为本轮返工要求。

## Controller Disposition — DSP-TASK-07-02

`IQR-TASK-07-01` 给出 `passed`（无整改项），并做了两次真变异。控制器采纳其三条并记账。

### 一、必须带下去的限定：那条 CHECK **只对新建库无条件成立**

快审指出、控制器复核属实：

- `git show HEAD:…CharacterDatabaseSchema.cs` 里当前**发布版的 `items` 表没有**
  `world_identity_fits_the_wire`（实测命中 0）；工作区版本才有（命中 1）。
- schema 调和只有 `ALTER TABLE … ADD COLUMN`（`:159`），**补不了 CHECK**。

所以「世界身份的上界由那条 CHECK 守住」这句，**只对新建库无条件成立**。
既存库上溢出会落到 `Entry.cs:46` 的 `checked((int))`，在编码时炸 `OverflowException`。

这与所有者「旧库作废重建」的裁定**不冲突**，但**说法必须带这个限定**——
后来者读到「有 CHECK 守着」会以为它保护所有库。
→ **写进 T-012 的成果记载**，不得省略限定。

### 二、我对那颗雷的性质判断，被快审收窄了一档——采纳

`DSP-TASK-07-01` 说 `SelectCharacterPostEntryServices.cs:120` 是一颗雷。快审补了一条收窄：

> 本轮之后 `Creatures` 一节的键与帧里的 `PetKey` 是**同一个** `item_uid`，
> 只要 `:120` 命中，`:133` 的匹配必然对上——**失败模式只有「静默不复活」一种，
> 不存在「复活错另一只」**。

**这颗雷是哑的，不是乱的。** 采纳，并要求 T-011 与 T-012 按这个更准的口径写，
不要笼统写成「可能出错」。

### 三、一处报错质量问题，记债不修

`InsertOwnAsync`（`InventoryItemTables.cs:143-165`）是唯一不把约束失败翻成具名异常的写口，
会漏出裸 `SqliteException`。**造不出越界身份，只是报错质量。**
→ 记进完整变更评审的关注点，本轮不修。

### 四、快审对 CK-016 第二条依据给了更准的口径

执行方说新旧序「都是取得顺序」属推理。快审的口径更准：
**旧序在生产上根本不可观察**（`character_creatures` 无生产者），
这让等价**既无法证伪也无法被打破**。以快审的口径为准。
