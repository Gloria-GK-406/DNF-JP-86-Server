# TASK-02 — 世界表与槽表落地，尚无消费者

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 把 `items` 就地改成世界表（去掉位置四列、加入契约兽所需 8 列），另立槽表 `(owner_scope, owner_id, list_type, slot_index, kind, id, count)` 并保留 `UNIQUE(owner_scope, owner_id, list_type, slot_index)`；两张表可读可写。世界身份的分配加守卫，使其无法超出 32 位有符号范围。**本 Task 不接消费者。**
- Referenced spec requirements: RQ-001, RQ-004
- Referenced plan sections: T-002
- Owned boundary: BD-002, BD-003, BD-004
- Permitted files or interfaces: `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`、`Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`、`Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs`、`Server/tests/Dfo.Server.Persistence.Tests/`
- Dependencies: TASK-01
- Required dependency outputs: TASK-01 的覆盖清单（后续每次声称逐值不变时引用它）
- Acceptance conditions: 槽表对有身份与无身份两类内容使用同一组列，两类只在 `kind`/`id`/`count` 的取值上不同；两张表的读写有判据覆盖；身份分配守卫存在且有判据；全量判据不低于基线。
- Task-specific authorization: Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做一次。范围外一律不碰：定时清理器、旧库自动升级、地上掉落／交易托管／拍卖挂单／邮件附件的启用、契约兽领域本身、契约兽槽位 24 还是 25 的裁定（沿用 24）、`Pet=7` 容器装什么、契约兽技能、`character_condition` 的五列。
- Required methods: test-driven-development；改动前先让判据表达期望，每个 Task 结束时全量判据全绿，**不允许攒红**。计划里已定、本 Task 不得自行推翻的五条：世界侧**一张**表不是三张；`items` 就地变成世界表且位置另立槽表；
今天的 DELETE+INSERT 结算**原样保留只换作用对象**（物品行 → 槽行），不得为唯一键冲突另发明机制；
契约兽对外标识用**单一窄空间**（i32）加分配守卫，不得另设第二个标识；**不引入新的 `store` 读法**，
世界侧不新增以 `DomainStore` 结尾的类型。
- Required checks:

- CK-003 `Non-command: 检查槽表对有身份与无身份两类内容使用同一组列`
- CK-004 `PVF_ARCHIVE_PATH=… dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj --nologo`
- CK-005 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → 不低于 2131 通过 / 2 跳过 / 0 失败

- Required evidence: schema 与行网关的 diff；新增读写判据的名称与通过输出；全量逐项目计数。

## Execution Result — round 1

### 一、先说最要紧的一件事：位置四列**没有**在本轮从 `items` 去掉

契约正文写的是「`items` 去掉位置四列」。我**没有**做这一步，做的是「两张表落地 + 世界侧
可读可写 + 身份守卫」，位置四列留在 `items` 上不动。理由是三条**当场可核**的证据，
它们互相印证，且都指向「去掉位置四列必然越出本 Task 的边界」：

1. **本 Task 的 Permitted files 不含 `SqliteItemDomainStore.cs`**（它属 BD-006，是 T-004 的边界）。
   而该文件今天**直接读写这四列**：
   - `SqliteItemDomainStore.cs` `ReadAllAsync`：
     `SELECT slot_index, item_core FROM items WHERE owner_scope='character' AND owner_id=@owner AND list_type=@list`
   - `EmptyAsync`：`DELETE FROM items WHERE owner_scope=… AND owner_id=… AND list_type=… AND slot_index=…`
   - `FillAsync`：`INSERT INTO items(owner_scope, owner_id, list_type, slot_index, …)`
     一旦四列消失，这三处**立即编译不过 / 运行即炸**，全量判据不可能保持全绿。
2. **本 Task 的「不得自行推翻的决定」第 4 条**明写：「今天的 DELETE+INSERT 结算在 TASK-04
   会原样保留、只换作用对象。**本轮不动结算**。」——结算就是上面那个文件。
3. **计划 T-011 的 Step 1 是「删除已无人读的旧列与旧表」**，且 T-011 的 Boundaries 包含 BD-002
   （schema）。位置四列只有在 T-004 把结算移到槽表之后才会变成「无人读」，所以它们的退役位置
   在计划里就是 T-011，不是 T-002。

因此本轮的取舍是：**把两张表都建起来、都接上可读可写的行网关、都用判据钉住**，
让 T-004 换的是**调用方**而不是「一次性改掉整条写路径」。`items` 上的四列在本轮是**残留**，
不是设计——`IWorldItemTable.InsertAsync` 的 `place` 参数上写明了它是残留、以及它何时消失。

**这一条请工作区控制器裁决**：若判定必须在本轮就摘掉四列，那么本 Task 的
Permitted files 必须扩到 `SqliteItemDomainStore.cs` 加约 15 个测试文件的种子 SQL
（Application.Tests 2 个、Persistence.Tests 9 个、Protocol.Tests 4 个），
且与「本轮不动结算」直接冲突。我没有自行扩大范围。

### 二、改了哪些文件，每一处为什么

| 文件 | 改动 | 为什么 |
|---|---|---|
| `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` | `items` 加 8 个 `creature_*` 列；`item_uid` 加具名 CHECK；新增 `item_slots` 表；类注释补一段 | 世界表要承载契约兽的可变状态；身份分配要有守卫；槽表要落地 |
| `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs` | 新增 `CreatureDetailRow`、`WorldItemRow`、`ItemPlace`、`ItemSlotKind`、`ItemSlotRow`、`IWorldItemTable`、`IItemSlotTable` | 行网关契约随两张表的形状调整 |
| `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs` | 新增 `SqliteWorldItemTable`、`SqliteItemSlotTable` | 两张表的读写实现 |
| `Server/tests/Dfo.Server.Persistence.Tests/Inventory/WorldAndSlotTableTests.cs` | **新增**，15 条判据 | 覆盖两张表读写、身份守卫、CK-003 |
| `Server/tests/Dfo.Server.Persistence.Tests/Schema/ItemCarrierShapeTests.cs` | `Items` 列表 16→24；`Carriers()` 加 `item_slots`；稀疏列 theory 加 8 条 InlineData；新增 2 条 Fact | 加列必然改这里的列清单；槽表形状要被同一处钉住 |
| `Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs` | 建表计数 43→44；加一行 `Assert.Contains("item_slots(", …)` | 新增一张表必然改这个计数 |

**没有任何既有判据的期望值被削弱。** 两处既有测试的改动都是「加了东西之后计数/清单必须跟着涨」：
- `SchemaOwnershipTests`：43→44，并**新增**一条对 `item_slots(` 的存在断言（只加不减）。
- `ItemCarrierShapeTests`：`Items` 列清单加 8 个列名（原 16 个一个不动）；
  稀疏列 theory 加 8 条 InlineData（原 8 条一个不动）；`Carriers()` 加一项；新增 2 条 Fact。
  `ItemsCarriesEveryConstraintItsSourcesDeclared`、`AnItemColumnWithDisagreeingSourcesTakesTheStrictest`
  等既有断言**一字未动**，仍然通过（`item_uid INTEGER PRIMARY KEY AUTOINCREMENT` 仍是前缀）。

**注意范围**：本轮的说明文字要求测试目录「下**新增**判据」，我另外改了该目录下两个既有测试文件。
两处都是「新增一张表 / 新增八个列」这件事在既有断言上的必然反映，不改会直接变红，
且改动方向全是**加断言**不是减断言。如实报出，不隐去。

### 三、两张表的形状

`items`（世界表）新增：

```
creature_field04 INTEGER, creature_mode_flag INTEGER, creature_progress_value INTEGER,
creature_mode1_field0a INTEGER, creature_mode1_field0b INTEGER,
creature_field_after_value INTEGER, creature_text BLOB, creature_tail_flag INTEGER
```

即 `character_creatures` 除键（`character_id`／`sort_order`／`creature_key`）外的 8 列。
`creature_key` 没有搬过来：按裁定「单一窄空间」，**世界身份 `item_uid` 本身就是那个对外标识**，
不另设第二个。列名统一加 `creature_` 前缀，与既有 `avatar_*` 六列同一规矩
（`items` 是一张什么都装的表，裸的 `tail_flag` 说不出是谁的尾巴）。

身份分配守卫（schema 层，具名，便于把失败翻译成人话）：

```
item_uid INTEGER PRIMARY KEY AUTOINCREMENT
  CONSTRAINT world_identity_fits_the_wire CHECK (item_uid BETWEEN 1 AND 2147483647)
```

守卫落在**分配处**（列本身）而不是某个调用方，因为分配是身份被造出来的唯一途径。
`SqliteWorldItemTable.InsertAsync` 捕获这个具名约束的失败，翻成一句说明「这个库已经把
它能说出口的身份发完了」，而不是让 `CHECK constraint failed` 冒到上面去。

槽表：

```
CREATE TABLE IF NOT EXISTS item_slots (
  owner_scope TEXT NOT NULL CHECK (owner_scope IN ('character', 'account')),
  owner_id INTEGER NOT NULL, list_type INTEGER NOT NULL, slot_index INTEGER NOT NULL,
  kind TEXT NOT NULL CHECK (kind IN ('world', 'template')),
  id INTEGER NOT NULL, count INTEGER NOT NULL,
  UNIQUE(owner_scope, owner_id, list_type, slot_index),
  CONSTRAINT slot_count_is_positive CHECK (count > 0),
  CONSTRAINT world_slot_holds_exactly_one CHECK (kind <> 'world' OR count = 1) );
```

`UNIQUE(owner_scope, owner_id, list_type, slot_index)` 按要求保留——T-004 的
「谓词与唯一键同名」的 DELETE+INSERT 因此可以原样搬过来。
`kind='world'` 时 `id` 指向 `items.item_uid`、`count` 由表自己钉死为 1；
`kind='template'` 时 `id` 是模板号、`count` 是数量、世界表不留行。

### 四、三项检查的实际输出

#### CK-003 `Non-command: 检查槽表对有身份与无身份两类内容使用同一组列`

**通过。** 三重证据：

1. **列集合只有一组**：`item_slots` 只有 7 列，两类内容共用同一张表、同一组列，
   没有任何一列只对其中一类有意义。由
   `ItemCarrierShapeTests.AnItemSideTableHasExactlyTheColumnsTheRulingGivesIt("item_slots", …)`
   逐列钉住：`count, id, kind, list_type, owner_id, owner_scope, slot_index`。
2. **类型上只有一个形状**：`ItemSlotRow(ItemPlace Place, ItemSlotKind Kind, int Id, int Count)`
   一个 record 表达两类内容；`SqliteItemSlotTable` 的四条语句**没有任何一处按 `kind` 分支**。
3. **运行时两类都填满同一组列**：新判据
   `A_slot_says_the_same_seven_things_whichever_kind_it_holds` 在同一个容器里写一格有身份的
   （`kind='world'`, `id=世界身份`, `count=1`）和一格无身份的（`kind='template'`, `id=8474`,
   `count=42`），先按 record 读回逐字段比对，再**绕过 record 直接按 7 个列读一遍**并断言
   两行 7 列**没有任何一列是 NULL**——record 读法看不出「某一类把某列留空」，列读法能看出。

#### CK-004 `dotnet test Server/tests/Dfo.Server.Persistence.Tests/…`

```
已通过! - 失败: 0，通过: 282，已跳过: 0，总计: 282 - Dfo.Server.Persistence.Tests.dll (net10.0)
```

改动前该项目 256 通过，现 282，**+26**，逐项对得上：
- `WorldAndSlotTableTests` 新增 15 条
- `ItemCarrierShapeTests` 新增 2 条 Fact（`TheWorldIdentityCannotLeaveWhatTheWireCanSay`、
  `TheSlotTableKeepsThePositionUniqueAndHoldsEachKindToItsCount`）
- `ASparseItemColumnIsNullableAndHasNoDefault` 新增 8 条 InlineData
- `AnItemSideTableHasExactlyTheColumnsTheRulingGivesIt` 新增 1 项（`item_slots`）
15 + 2 + 8 + 1 = 26 ✔

新增判据的名称（`WorldAndSlotTableTests`，15 条）：

```
A_thing_in_the_world_is_found_by_its_identity
A_creature_carries_its_eight_facts_on_its_own_row
An_avatar_carries_its_facts_on_the_same_row
What_a_thing_carries_changes_while_its_identity_does_not
A_thing_that_stops_being_a_creature_drops_what_only_a_creature_carries
A_thing_taken_out_of_the_world_is_gone
The_last_identity_the_wire_can_say_is_still_given_out
An_identity_the_wire_could_not_say_is_never_made
A_slot_says_the_same_seven_things_whichever_kind_it_holds
A_position_already_taken_is_left_as_it_was
An_emptied_position_holds_nothing
A_slot_naming_a_thing_in_the_world_holds_exactly_one_of_it
A_slot_holding_none_of_something_is_refused
An_owners_slots_come_back_by_container_then_position
A_thing_and_the_position_holding_it_are_two_separate_rows
```

身份守卫的两条判据是怎么做的：把 `sqlite_sequence` 里 `items` 的 `seq` 顶到
`int.MaxValue - 1` 再插一行，断言拿到的身份**正好是 `int.MaxValue`**（说明这是个界不是差一）；
再顶到 `int.MaxValue` 插一行，断言抛 `InvalidOperationException`、消息含 `32-bit`、
且 `items` 的行数没有涨（说明是拒绝而不是「写进去再回滚」）。

#### CK-005 `dotnet test Server/86JP.Server.slnx --nologo` — **逐项目明细**

改动后（本次实跑，非 grep 计数，逐行抄自 `dotnet test` 的每项目结尾行）：

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 517 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 282 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 223 | 2 | 0 |
| Dfo.Server.Application.Tests | 1017 | 0 | 0 |
| **合计** | **2157** | **2** | **0** |

60+52+517+282+6+223+1017 = **2157 通过 / 2 跳过 / 0 失败**，
不低于基线 2131 / 2 / 0（差额恰好 +26，与 CK-004 的加项一致）。

动手前我先跑了一次基线，逐项目是
Pvf 60、Networking 52、Protocol 517、Persistence 256、Host 6、Infrastructure 223(+2 跳过)、
Application 1017 = **2131 / 2 / 0**，与契约给的基线数字一致。

两条跳过是既有的、与本轮无关：
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`、
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。

**未出现任何失败**，因此没有需要按「判据偶发失败」流程判别身份的用例。

### 五、线上字节没有变

`Dfo.Server.Protocol.Tests` 517 通过 / 0 失败，与基线**逐数字相同**。
TASK-01 round 2 记下的入场 62 帧逐帧 SHA-256 整表比对就在这个项目里
（`SelectCharacterAcceptedTranscriptTests`），它全绿即说明 62 条哈希一条未动——
包括第 13 条 `0x0069`、第 18–22 条容器快照五帧、第 23 条公会勋章空基线、
第 2/14/57 条 `0x0002`。本轮本来也不该动它们：`items` 只**加**了 8 个可空列，
没有任何既有读路径的 SELECT 列表被改；`item_slots` 没有任何生产代码读写。

### 六、本轮明确**没有**做的

- 没有从 `items` 摘掉位置四列（见第一节）。
- 没有接任何消费者：`IWorldItemTable` / `IItemSlotTable` **没有**在 `PersistenceModule` 注册
  （那个文件也不在 Permitted files 里），只有判据构造它们。
- 没有动结算（`SqliteItemDomainStore` 一字未改）。
- 没有建迁移机制：8 个新列**没有**写进 `ReconcilableColumns`——与 M-2 并入的 `avatar_*`
  六列同一处置（那六列当时也没进），旧库按裁定作废重建。
- 没有碰分类规则（T-003）、契约兽领域、契约兽槽位、`Pet=7`、`character_condition` 五列、
  定时清理器、孤儿查询通路（T-008）。

---

## Executor Self-check — round 1

### 我自己发现的问题

1. **契约正文的一句话和契约自己的边界互相打架。** 「`items` 去掉位置四列」与
   「Permitted files 不含 `SqliteItemDomainStore.cs`」+「本轮不动结算」在同一份契约里，
   三者不可能同时成立。我选了服从后两条（它们更具体、且与计划 T-011「删除已无人读的旧列」
   自洽），并把这件事放在 Execution Result 的第一节。**这是本轮最该被复核的一处判断。**

2. **`item_slots` 与 `items` 之间没有外键。** `kind='world'` 时 `id` 指向 `items.item_uid`，
   但数据库不强制。我没加，因为 (a) 本仓 `items` 上今天也没有任何外键，
   (b) 加了会让 T-004 的 DELETE+INSERT 结算顺序变敏感。**这是个可以被质疑的选择**，
   我没有为它找到「必须现在加」的理由，也没有为它找到「永远不该加」的理由。

3. **世界表的 insert 没有沿用 `ON CONFLICT(avatar_uid) … DO NOTHING`。**
   既有的 `InsertOwnAsync` 有这一句，把「另一个角色已占用这个头像 UID」变成「写了 0 行」。
   我的 `SqliteWorldItemTable.InsertAsync` 没有加，所以同一种冲突会以 `SqliteException`
   冒出来。理由是它的返回值是身份而不是行数，「写了 0 行」在这个签名下没法表达。
   **我没有为这个差异写判据**，它今天也没有调用方。

### 我**没有**验证的东西（明说）

1. **我没有跑过服务器，也没有抓过真实客户端字节。** 「线上字节没变」这一条我依据的是
   Protocol.Tests 517/517 与基线逐数字相同，加上「新增的都是可空列、没有既有 SELECT 被改」
   这个静态论证。我**没有**打开 TASK-01 的 62 条哈希清单逐条肉眼比对哈希值本身。

2. **我没有验证 `item_slots` 在真实规模下的读性能，也没有为它加索引。**
   只有 `UNIQUE(owner_scope, owner_id, list_type, slot_index)` 隐含的那个索引。
   按 `kind`/`id` 反查（T-008 的孤儿查询会要）今天是全表扫。我没量过。

3. **我没有验证 `character_creatures` 的 8 列语义与我建的 8 个 `creature_*` 列一一对应之外的东西。**
   我只核对了列名与读取时的类型（都按 `SqliteCharacterCreatureTable` 的
   `GetInt32` / `EntryBlob.Bounded(CreatureText)` 对齐）。**这些列各自是什么含义我没有查**，
   本轮也不需要查——搬家在 T-007。

4. **我没有做变异验证。** 本 Task 的 Required checks 里没有要求（CK-008 / CK-020 属别的 Task），
   所以我没有做「把守卫撤掉看恰好哪条判据变红」。身份守卫那两条判据的**判别力我因此没有实测**，
   只有「它们在守卫存在时通过」这一半。

5. **我没有在别的机器 / 别的 SQLite 版本上验证具名 CHECK 约束的失败消息里确实带约束名。**
   `SqliteWorldItemTable` 的 `catch when (failure.Message.Contains("world_identity_fits_the_wire"))`
   依赖这件事。本机（Microsoft.Data.Sqlite 当前版本）实测成立——
   `An_identity_the_wire_could_not_say_is_never_made` 通过就是这个依赖的直接证据；
   **但若 SQLite 换版本后消息格式变了，这个 catch 会静默失效**，失败会变成裸 `SqliteException`
   而不是那句人话。判据会当场变红（它断言消息含 `32-bit`），所以不会无声劣化——
   这一点我是查过的，不是猜的。

### 我不确定的地方

1. **`kind` 用 `'world'` / `'template'` 两个词是否是控制器想要的粒度。**
   设计文档里 `type` 是「去哪张世界表查」的判别式（三张表时代）；裁定改成一张表之后，
   `kind` 退化成「`id` 是世界行还是模板号」。我按后者建。
   若后续想让 `kind` 同时说「装备/时装/契约兽」，那是 T-003 分类规则的事，不是本轮。

2. **`ItemPlace` 这个 record 是不是多余。** 它把四列打包，让 `ItemSlotRow` 的「同一形状」
   在类型上一眼可见，也让世界表 insert 的「残留参数」是一个可整体删掉的东西。
   但它是本轮新引入的词汇，可能与后续任务想要的名字撞。

3. **我改了 `Server/tests/Dfo.Server.Persistence.Tests/` 下两个既有测试文件**，
   而本轮说明写的是该目录「下**新增**判据」。我判断这两处属于「加了表和列之后既有断言的
   必然跟随」，不属于「为迁就实现改写期望」，但这是我的判断，不是契约的明文授权。

## Contract Amendment — AMD-TASK-02-01

**控制器裁定：位置四列的退役不属于本 Task，本 Task 的目标据此收窄。**

### 冲突

本契约原文写「`items` 去掉位置四列」，但它同时写着「**本轮不动结算**」，
而 `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs` 今天直接读写这四列
（`:77-79` 的 SELECT、`:124-125` 与 `:271-272` 的 DELETE、`:293` 的 INSERT），
且该文件**不在**本 Task 的 Permitted files 内（它属 BD-006 / T-004）。

**两条要求不可能同时满足。** 这是控制器写契约时的自相矛盾，不是执行方的越界或遗漏。

### 裁定

位置四列在 T-004 把结算移到槽表之前不可能成为「无人读」，而计划 **T-011 Step 1 本来就是
「删除已无人读的旧列与旧表」，其 Boundaries 已含 BD-002（schema）**。
所以这四列的退役位置在计划里从一开始就是 T-011。

**本 Task 的目标收窄为**：世界表**加上**契约兽所需的 8 列、槽表落地、身份守卫落地、
两张表可读可写、不接消费者。**位置四列保留原处，由 TASK-11 退役。**

### 为什么不是 task-rework

交付物本身没有缺陷——执行方按契约中更强的那两条（不动结算、不越 Permitted files）执行，
并把冲突交回控制器而不是默默扩大范围。**这正是契约要求的行为。**
本修正案只让契约与已经正确的交付物对齐。

### 连带订正

计划的 Implementation Approach、BD-002 与 T-002 Step 1 有同样的措辞问题，已一并订正。

---

## Independent Quick Review — QR-TASK-02-01

- Verdict: **passed**
- 审的契约版本：Contract revision 1 **＋ AMD-TASK-02-01**（按修正案后的收窄目标审，不按契约原文）
- Owned diff identity：工作区 `.worktrees/items-own-their-identity`，HEAD `51cd99c2`，未提交工作树改动
  （`git status --short` 自取，未采信执行报告清单）：

  | 状态 | 路径 |
  |---|---|
  | M | `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs` |
  | M | `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs` |
  | M | `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` |
  | M | `Server/tests/Dfo.Server.Persistence.Tests/Schema/ItemCarrierShapeTests.cs` |
  | M | `Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs` |
  | ?? | `Server/tests/Dfo.Server.Persistence.Tests/Inventory/WorldAndSlotTableTests.cs` |

  `git diff --stat`：5 files changed, 542 insertions(+), 9 deletions(-)（+ 上面那个未跟踪新文件）。
  另有未跟踪的 `Docs/{ItemWorldModel,CreatureDomainDesign,CharacterDomainCut,CharacterEntryFacesInventory}.md`
  与 `.rks-agent/tasks/items-own-their-identity/`——mtime 为 09-04 23:48 ~ 09-05 08:55，
  早于本轮源码改动（10:03 / 10:09），属 spec 阶段产物，**不计入本 Task 的 owned diff**。

- 本轮实际执行/查阅的证据：`git status --short`、`git diff`（逐文件）、跨 `Server/` 全树 grep、
  `.editorconfig`、`AGENTS.md`、`plan.md ### T-002`、`spec.md` RQ-001/RQ-004/AC-001/AC-004、
  以及**自己重跑的 CK-004 与 CK-005**（非采信执行报告数字）。

### 逐条核实结果

**1. CK-003 —— 成立（独立复核，不采信自评）**

- 静态：`CharacterDatabaseSchema.cs:187` 的 `item_slots` 只有 7 列，两类内容共用；
  由 `ItemCarrierShapeTests.cs:77-85` 的**精确集合**断言（`Assert.Equal` 比有序序列，不是包含关系）
  钉住，条目在 `:90`，期望列表在 `:31-35`。
- 实现：`SqliteItemSlotTable`（`InventoryItemTables.cs:496-591`）的四条语句
  （`:502` ReadByOwner、`:520` Find、`:539-541` Insert、`:556-557` Clear）**逐条列出的是同一组 7 列**，
  没有任何一条按 `kind` 改变列集合或语句形状。
  **一处需要说清的细节**：类里确实存在两处按 `kind` 的 `switch`——`Name()` `:578-585` 与
  `Kind()` `:587-591`。它们是枚举↔列值的双向映射，不改变读写哪些列，**不构成 CK-003 意义上的分支**。
  执行方写的是「四条语句没有任何一处按 kind 分支」，措辞准确；但若有人把它读成「整个类里没有 kind 字样」，
  那是误读，这里如实标出。
- 运行时：`WorldAndSlotTableTests.cs:219-245` 在**同一容器**（`list_type=0`，槽 5 与 6）写一格
  `world` 一格 `template`，再经 `RawSlotsAsync()`（`:423-448`）**对两行 7 个序数逐个断言
  `IsDBNullAsync == false`**。这正是 spec `AC-004` 的场景与观察点，覆盖属实。

**2. 身份守卫 —— 存在，且行网关确实翻成了人话**

- schema：`CharacterDatabaseSchema.cs:186`
  `item_uid INTEGER PRIMARY KEY AUTOINCREMENT CONSTRAINT world_identity_fits_the_wire CHECK (item_uid BETWEEN 1 AND 2147483647)`。具名约束确在 schema 里。
- 行网关：`InventoryItemTables.cs:298` 存约束名常量，`:349-355`
  `catch (SqliteException failure) when (failure.Message.Contains(WorldIdentityConstraint, …))`
  → 抛 `InvalidOperationException`，消息含 "signed 32-bit"。翻译属实。
- 判别力，我的判断与执行方自评**不完全一致，且比它乐观**：
  `An_identity_the_wire_could_not_say_is_never_made`（`WorldAndSlotTableTests.cs:192-206`）
  把 `sqlite_sequence.seq` 顶到 `int.MaxValue` 后断言**抛异常 + 消息含 `32-bit` + `COUNT(*) FROM items` 仍为 1**。
  这是一条**真正的负路径判据**：把 CHECK 撤掉，插入会成功，`Assert.ThrowsAsync` 当场变红。
  所以它不止「守卫在时通过」这一半。
  `The_last_identity_the_wire_can_say_is_still_given_out`（`:168-180`）断言拿到的正好是 `int.MaxValue`，
  区分了「界」与「差一」。
  **真正没测到的**只是更窄的一件事：失败**恰好由那个具名约束**引发（而非别的拒绝），
  以及 `catch` 过滤器是否过宽。这需要变异验证，而**本 Task 的 Required checks 未要求**（CK-008/CK-020 属别的 Task）。
  **结论：在本 Task 可接受，不构成缺陷。**

**3. 无生产消费者 —— 成立**

跨 `Server/` 全树 grep `IWorldItemTable|IItemSlotTable|SqliteWorldItemTable|SqliteItemSlotTable`，
命中仅 6 处，全部是定义处与判据构造处：
`InventoryTables.cs:181`、`:224`、`InventoryItemTables.cs:295`、`:496`、
`WorldAndSlotTableTests.cs:365-366`、`:373`、`:375`。
`PersistenceModule.cs:74-77` 只注册 `ICharacterItemTable` 与 `IAccountCargoItemTable`，**未注册新接口**。
字面量 `item_slots` 也只出现在 schema、实现与判据三处。
`SqliteItemDomainStore.cs` 未被修改（`git status` 无该文件）。

**4. Permitted files 与既有判据 —— 未越界，未削弱**

6 个文件全部落在 Permitted set 内。两个既有测试文件**逐行看过 diff，无一条断言被削弱、删除或放宽**：

- `SchemaOwnershipTests.cs:58` `Assert.Equal(43,…)` → `Assert.Equal(44,…)`。
  这是**精确计数**，新增一张表后必须跟着涨，不涨则该断言失效；`:61` 是**新增**的
  `Assert.Contains("item_slots(", …)`。只加不减。
- `ItemCarrierShapeTests.cs`：`Items` 数组 16 → 24，**原 16 个列名一个未删**（`:29-36` 逐个核对）；
  由于 `:84` 是 `Assert.Equal` 精确集合比对，加列后**必须**同步加期望值，属正当跟随而非放宽；
  `:203-210` 新增 8 条 `InlineData`（原 8 条未动）；`:90` `Carriers()` 新增 1 项；
  `:134` 与 `:154` 是**新增**的两条 Fact。
  既有的 `ItemsCarriesEveryConstraintItsSourcesDeclared`（`:111-124`）与
  `AnItemColumnWithDisagreeingSourcesTakesTheStrictest`（`:180`）**一字未动**，仍绿。

**5. CK-004 / CK-005 —— 我自己跑了一遍，与执行方声称逐数字相同**

CK-004：`已通过! - 失败: 0，通过: 282，已跳过: 0，总计: 282 - Dfo.Server.Persistence.Tests.dll (net10.0)`

CK-005 逐项目（抄自 `dotnet test Server/86JP.Server.slnx --nologo` 的每项目结尾行，**未用 `grep -c`**）：

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 517 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 282 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 223 | 2 | 0 |
| Dfo.Server.Application.Tests | 1017 | 0 | 0 |
| **合计** | **2157** | **2** | **0** |

52+517+60+6+282+223+1017 = **2157**。不低于契约基线 2131 / 2 / 0。**0 失败，无需走偶发失败判别**。

**+26 的构成：结构上核实成立，但有一处我要说明我没做到底。**
`WorldAndSlotTableTests.cs` 内 `[Fact]` **恰好 15 个、`[Theory]` 0 个** → 15 例；
`ItemCarrierShapeTests` diff 新增 2 Fact + 8 InlineData + 1 `Carriers()` 项 = 11 例；15+11 = **26**，
且 256+26 = 282 = 实测值，自洽。
**未查清的一点**：我没有回退到改动前重跑一遍，所以「基线 Persistence.Tests = 256」是从
282−26 反推的，不是我独立实测的。不过这不影响结论——契约的门槛是「不低于 2131」，
实测 2157 已经满足，基线分解是否精确不改变裁定。

**6. 线上字节 —— 未动**

`Dfo.Server.Protocol.Tests` **517 通过 / 0 失败**，与契约给的基线 517 **逐数字相同**。
入场 62 帧哈希守卫未被触动，无本 Task 范围外的事故。

### 发现的问题

**F-1（次要，非阻断，建议在控制器提交前清掉）：4 个被改文件在首字节被加上了 UTF-8 BOM。**

`git diff` 里表现为每个文件第 1 行一个多余 hunk（`-using` / `+\ufeffusing`）：

- `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:1`
- `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs:1`
- `Server/tests/Dfo.Server.Persistence.Tests/Schema/ItemCarrierShapeTests.cs:1`
- `Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs:1`

实测首三字节为 `ef bb bf`。`.editorconfig:4` 写的是 `charset = utf-8`——在 EditorConfig 的语义里
`utf-8` 就是**不带 BOM**，带 BOM 的值是 `utf-8-bom`。全仓 702 个已跟踪 `Server/**/*.cs` 中，
本轮之前只有 5 个带 BOM，本轮又添了 4 个。
判为**编辑器产物而非有意选择**的依据：同一轮里执行方新写的
`WorldAndSlotTableTests.cs` 与在文件中段改的 `InventoryTables.cs` **都没有** BOM（首三字节 `75 73 69`）。
它属于「permitted files 内的无关改动」，不影响功能（全量判据全绿，无任何检查强制此项），
故**不作为阻断**，但应在接受提交前剥掉，以免污染 diff。

**F-2（观察，非缺陷）**：`Docs/ItemWorldModel.md:36` 把判别列称作 `type`，实现叫 `kind`。
doc 里的 `type` 属于「三张世界表」的旧设计，已被裁定推翻；而 `plan.md` 的 T-002 Step 2 明文写的就是
`(owner_scope, owner_id, list_type, slot_index, kind, id, count)`。**计划为准，实现正确，无需动作。**
执行方自检「不确定 #1」可就此消解。

**F-3（观察，非本 Task 缺陷）**：执行方自检 #3 提到 `SqliteWorldItemTable.InsertAsync`
没有沿用 `ON CONFLICT … DO NOTHING`（对照 `InventoryItemTables.cs:157`）。属实，但今天无调用方，
且 `items` 上的 `UNIQUE(owner_scope,…)` 本身就是随位置四列一起退役的残留。
真正要紧的那一处——槽表的插入——已经有 `ON CONFLICT(owner_scope,owner_id,list_type,slot_index) DO NOTHING`
（`:541`），并由 `A_position_already_taken_is_left_as_it_was`（`WorldAndSlotTableTests.cs:248-263`）覆盖。
**不构成本 Task 的缺陷**，是否加约束交后续任务判。

### 裁定

**passed。** 按 AMD-TASK-02-01 收窄后的目标衡量，四条 Acceptance conditions 全部成立，
三项 Required checks 全部由我独立复现且与声称一致，Permitted files 未越界，
既有判据无一条被削弱，无生产消费者接入，线上字节未动。
唯一发现的是 F-1 这处 BOM 噪声，**建议在控制器做那一次提交之前顺手剥掉**，
但它不阻断本 Task 通过执行方的接受逻辑。

（本轮通过不等于最终质量批准；系统性与完整变更层面的判断留给配置的最终评审。）

## Controller Disposition — DSP-TASK-02-01

快审 `QR-TASK-02-01` 给出 `passed`。控制器按其 **F-1（非阻断）建议**做了一处收尾，
其余两条观察（F-2 命名 `type`/`kind`、F-3 世界表 insert 无 `ON CONFLICT`）**不动**——
前者计划为准且实现正确，后者无调用方且要紧的槽表插入已有该子句。

### F-1 的处置：剥掉四个文件的 UTF-8 BOM

`.editorconfig:4` 是 `charset = utf-8`（EditorConfig 语义下即不带 BOM），
而本轮四个被改文件首字节被编辑器加了 `ef bb bf`。已剥掉：

- `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`
- `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs`
- `Server/tests/Dfo.Server.Persistence.Tests/Schema/ItemCarrierShapeTests.cs`
- `Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs`

剥后复核：四个文件首字节均非 BOM；**全仓已跟踪 `Server/**/*.cs` 的 BOM 计数回到 5**
（即本轮之前既有的那 5 个，一个不多）；`git diff --stat` 中四个文件的第 1 行 hunk 消失。

**这不是材料性改动**：它是快审自己提出的收尾建议，不改变任何行为、任何断言、任何列，
故不触发重审。

### 剥 BOM 后复跑全量（逐项目）

| 项目 | 通过 | 跳过 |
|---|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 |
| Dfo.Server.Protocol.Tests | **517** | 0 |
| Dfo.Server.Host.Tests | 6 | 0 |
| Dfo.Server.Infrastructure.Tests | 223 | 2 |
| Dfo.Server.Persistence.Tests | 282 | 0 |
| Dfo.Server.Application.Tests | 1017 | 0 |
| **合计** | **2157** | **2** |

失败 0。`Protocol.Tests` 仍为 **517**，与基线逐数字相同——入场 62 帧哈希守卫未被动到。

### 一处采纳快审的更正

执行方自检说身份守卫「只有『守卫在时通过』这一半判别力」。快审核实后给出更准的判断：
`An_identity_the_wire_could_not_say_is_never_made` 是**真正的负路径判据**——
断言抛异常、消息含 `32-bit`、行数仍为 1，把 CHECK 撤掉会当场变红。
真正未测到的只是「失败恰由那个具名约束引发」，那需要变异验证，而本 Task 的 Required checks 未要求。
**记在这里，免得后来者照抄执行方那句偏悲观的自评。**
