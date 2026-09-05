# TASK-13 — 时装那个第二身份键退役

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 让「这一行有没有时装细节」由细节列自己回答，退役 `items.avatar_uid`
  与唯一部分索引 `idx_items_avatar_uid`；并把因此变假的记载一并订正
  （`AGENTS.md` 的新增小节与 `behavior-change-list.md`）。
- Referenced spec requirements: RQ-003（可变状态随身份走）、RQ-013（`store` 只有一种读法）、
  RQ-015（仓库约定如实）。**注意：没有任何 RQ 直接要求退役它**——
  本 Task 由**所有者的范围裁定**创建，见下面的 Task-specific authorization。
- Referenced plan sections: T-011（退役旧形状）的延伸；本 Task 是控制器漏派的那一条的补做。
- Owned boundary: BD-002（schema）、BD-004（行网关）、BD-006（结算）、BD-013（`AGENTS.md`）、
  BD-014（判据）、BD-015（成果记录）。
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`
  - `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs`
  - `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`
  - `Server/tests/` 下的判据（已知涉及 7 个文件）
  - `AGENTS.md` 与 `.rks-agent/tasks/items-own-their-identity/behavior-change-list.md`（记载订正）
  - 本 Task 文件
- Dependencies: TASK-12
- Required dependency outputs: TASK-12 写下的 `AGENTS.md` 新增小节与 `behavior-change-list.md`
  ——**本 Task 会让其中关于 `avatar_uid` 的那几句变假，必须一并订正。**
- Acceptance conditions:
  1. `items` 上不再有 `avatar_uid`，`idx_items_avatar_uid` 不再存在；
  2. 「这一行有没有时装细节」由细节列自己回答，且该判定可指认为单一处；
  3. **线上字节不变**——字节锚 `SelectCharacterAcceptedTranscriptTests.cs` 一字未改，
     单跑该类 + `InventoryDeltaLift` 为 19 通过 / 0 失败；
  4. 全量判据不低于 **2215 通过 / 2 跳过 / 0 失败**；
  5. `AGENTS.md` 与行为变化清单中因本 Task 变假的陈述**已订正**，且订正**点名**原文而非悄悄替换；
  6. **不引入新的行为变化**——本成果只授权 RQ-009 与 RQ-010 两处。
- Task-specific authorization: 所有者 2026-09-05 就「`avatar_uid` 这个残留怎么处置」裁定
  **「本轮清掉」**。这是**范围裁定**，不是新的 RQ。
  Task Unit **不建 Git 检查点、不提交**；接受提交由工作区控制器在完整评审与新鲜验证之后做一次。
  范围外一律不碰：定时清理器；旧库自动升级；地上掉落／交易托管／拍卖挂单／邮件附件；
  契约兽领域本身；契约兽槽位 24 还是 25；`Pet=7` 容器；契约兽技能；
  `character_condition` 的五列；**时装／宝珠「取得下装不上」与时装种类不一致**（已知，本成果不修）。
- Required methods: test-driven-development；改动前先让判据表达期望；结束时全量全绿，**不允许攒红**。
  **任何 git 还原类命令一律禁止**（`checkout --`／`restore`／`reset`／`clean`／`stash`）——
  十二个任务的成果全部未提交且无第二份副本。变异验证**只用文件级备份**并以 `sha256sum` 核对还原。
- Required checks:

- CK-029 `Non-command: 指认「这一行有没有时装细节」的单一判定处，并确认 items 上无 avatar_uid、无 idx_items_avatar_uid`
- CK-030 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → 不低于 2215 通过 / 2 跳过 / 0 失败
- CK-031 `Non-command: 字节锚未改且单跑 19 通过 / 0 失败`
- CK-032 `Non-command: 列出因本 Task 而变假的记载，逐条给出订正位置`

- Required evidence: schema 与两个持久层文件的 diff（**按块归属，不要按整文件计数**——
  十二个任务全未提交）；单一判定处的位置；逐项目计数；字节锚输出；记载订正清单。

### 已知的取证起点（控制器已扫，不必重新发现）

`avatar_uid` 今天的全部落点：

| 位置 | 是什么 |
|---|---|
| `CharacterDatabaseSchema.cs:199` | `items` 的列声明 |
| `CharacterDatabaseSchema.cs:247` | `CREATE UNIQUE INDEX … idx_items_avatar_uid … WHERE avatar_uid IS NOT NULL` |
| `InventoryItemTables.cs:43 / :191 / :201 / :225 / :406 / :445 / :476 / :512` | 读写与投影 |
| **`InventoryItemTables.cs:230`** | **`ON CONFLICT(avatar_uid)` 的 upsert——承重，须查清它今天还起什么作用** |
| **`SqliteItemDomainStore.cs:407`** | **`WHERE avatar_uid = @u…`——正是 TASK-04 快审标为「近乎走不到」的 `CarriedAvatarsAsync`** |
| `SqliteItemDomainStore.cs:529` | 写入列清单 |

判据侧 7 个文件：`AvatarDetailFollowsTheIdentityTests`、`InventoryLoadCharacterizationTests`、
`SqliteEntryInventoryMutationRepositoryTests`、`SqliteInventoryEntryRepositoryTests`、
`SqliteItemDomainStoreTests`、`WorldRowSurvivesTheMoveTests`、`ItemCarrierShapeTests`。

### 两处要当心

1. **`ON CONFLICT(avatar_uid)` 曾是时装细节跨「删了重插」结算而不丢的机制**
   （M-2 的产物：`FillAsync` 用 `carried[item.Core.Value]` 重新挂回）。
   **T-011 之后世界行不再因移动而销毁**，所以这个机制可能已经失去存在理由——
   **查清再动，不要假设它还在起作用，也不要假设它已经没用。**
2. **`CarriedAvatarsAsync` 被 TASK-04 的快审标为「近乎走不到」**，且
   `Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 这条判据
   **现在靠「行没被删」通过，不再覆盖它名字所声称的机制**（`DSP-TASK-04-02` 第三节）。
   若本 Task 让它彻底走不到，**那条判据与那段代码的去留要一并交代**，不得留一段没有理由的机制。

---

## Execution Result — round 1

### 〇、这一轮改了哪些文件（按 mtime 圈定，不用 `git status`）

十二个任务全部未提交，`git status` 分不出单轮，所以**本轮改动一律以 mtime 判定**：
本轮第一次落笔在 `09-05 21:12`，取 `Server/`、`AGENTS.md`、`.rks-agent/` 下
mtime ≥ `09-05 21:00` 的文件，再剔除上一任务在 `21:03` 写完的
`tasks/TASK-12-conventions-and-the-change-list.md`。

**本轮改动 11 个文件，加上本 Task 文件共 12 个，分别是：**

生产侧 **3 个**：

1. `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`
2. `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs`
3. `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`

判据侧 **6 个**：

4. `Server/tests/Dfo.Server.Persistence.Tests/Schema/ItemCarrierShapeTests.cs`
5. `Server/tests/Dfo.Server.Persistence.Tests/Inventory/AvatarDetailFollowsTheIdentityTests.cs`
6. `Server/tests/Dfo.Server.Persistence.Tests/Inventory/InventoryLoadCharacterizationTests.cs`
7. `Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteEntryInventoryMutationRepositoryTests.cs`
8. `Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteInventoryEntryRepositoryTests.cs`
9. `Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteItemDomainStoreTests.cs`

记载侧 **2 个**：

10. `AGENTS.md`
11. `.rks-agent/tasks/items-own-their-identity/behavior-change-list.md`

**契约给的判据侧清单是 7 个文件，本轮只动了其中 6 个。没动的那一个是
`WorldRowSurvivesTheMoveTests`**：它里面的 `AvatarUid` 是一个 C# 常量（`= 777`），
被写进 core 的 `Value` 字段，**从来不是那个列名**；`SetAvatarColourAsync` /
`AvatarColourAsync` 都按 `item_uid` 定位行。所以它一字未改，且全绿。

**允许文件之外一个都没碰。**

### 一、逐块归属：每一处改了什么、为什么

#### 块 A —— `CharacterDatabaseSchema.cs`（BD-002），两处

- `TableDeclarations` 的 `CREATE TABLE … items`：删掉 `avatar_uid INTEGER,`。
  `items` 现在 **19 列**（逐列数：拆 `CREATE TABLE items ( … )` 括号内的顶层逗号，
  嵌套括号里的 `CHECK(...)` 不当作分隔），
  **分别是**：`item_uid`、`item_core`、`cargo_character_id`、`created_at`、`updated_at`、
  `avatar_expire_date`、`avatar_clear_id`、`avatar_jewel_socket`、`avatar_color1`、
  `avatar_color2`、`avatar_delete_date`、`creature_field04`、`creature_mode_flag`、
  `creature_progress_value`、`creature_mode1_field0a`、`creature_mode1_field0b`、
  `creature_field_after_value`、`creature_text`、`creature_tail_flag`
  ＝ 2（身份/core）＋ 3（归属/时间）＋ **6**（时装）＋ 8（契约兽）。
- `IndexDeclarations`：整行删掉
  `CREATE UNIQUE INDEX IF NOT EXISTS idx_items_avatar_uid ON items(avatar_uid) WHERE avatar_uid IS NOT NULL;`。
  该常量现在 **3 条，分别是**：`idx_item_slots_core`、`idx_item_slots_account_slot`、
  `idx_characters_account`。**没有任何后继索引**顶替被删的那条。
- `ReconcilableColumns` **未动**——`avatar_uid` 本来就不在里面（老库作废重建，不补列）。

#### 块 B —— `InventoryItemTables.cs` / `SqliteCharacterItemTable`（BD-004），六处

- **新增** `CarriesTheAvatar`（`internal const string`）：
  `"held.avatar_expire_date IS NOT NULL AND substr(held.item_core, 6, 4) = @avatarKey"`。
  这是「某一行是不是已经带着这个时装 UID 的细节」这个**写入侧**条件的唯一文本，
  下面两条写入共用它。UID 从 core 里读（`InventoryItemCore.Encode` 把 `Value` 写在偏移 5
  起的 4 字节小端，SQLite 数 BLOB 从 1 起，所以是 `substr(…, 6, 4)`）。
- **新增** `AvatarKey(InventoryItemCoreState)`：把 `core.Value` 转成那 4 个字节，供上面绑定。
- `ReadByCharacterAsync`：SELECT 列表去掉 `held.avatar_uid`；`Avatar(reader, 6)` 的调用**不变**，
  因为序号 6 现在正好是 `avatar_expire_date`。`<remarks>` 里「由 avatar UID 决定」那句改掉。
- `Avatar(SqliteDataReader, int)`：`IsDBNull(ordinal)` 现在问的是六列的第一列，
  六个字段的读取从 `ordinal+1..ordinal+6` 挪到 `ordinal..ordinal+5`。
  **这就是「这一行有没有时装细节」的单一判定处**（见第三节）。
- `BindAvatar`：不再绑 `@avatarUid`，改绑 `@avatarKey`（四字节，或 `DBNull`）；
  其余六个参数不变。
- `UpdateOwnAsync`：SET 子句去掉 `avatar_uid=@avatarUid`；
  `NOT EXISTS (… held.avatar_uid = @avatarUid …)` 换成
  `NOT EXISTS (SELECT 1 FROM items held WHERE {CarriesTheAvatar} AND held.item_uid <> @uid)`。
  **语义一字未变**：`avatar_uid` 从来就是 `core.Value` 的一份可索引副本，
  现在直接问 core。语句由原始字符串换成拼接串，只为把共用常量拼进去。
- `InsertOwnAsync`：列清单去掉 `avatar_uid`；
  `VALUES(...) ON CONFLICT(avatar_uid) WHERE avatar_uid IS NOT NULL DO NOTHING` 换成
  `SELECT @core,… WHERE NOT EXISTS(SELECT 1 FROM items held WHERE {CarriesTheAvatar})`。
  拒绝的表现完全一样：插入落空 → `RETURNING` 无行 → 返回 `null` →
  `InventoryEntryAssembly` 抛 `InvalidDataException` 并回滚。

#### 块 C —— `InventoryItemTables.cs` / `SqliteWorldItemTable`（BD-004），五处

- `InsertAsync` / `FindAsync` / `ReadUnreferencedAsync` 三条语句各去掉 `avatar_uid`；
- `Read(SqliteDataReader)`：`Avatar(reader, 2)` 不变（序号 2 现在是 `avatar_expire_date`），
  `Creature(reader, 9)` → `Creature(reader, 8)`；`<summary>` 的「seventeen columns」→「sixteen」；
- `UpdateAsync` 的 SET 子句去掉 `avatar_uid=@avatarUid`；
- `BindAvatarColumns` 少一个参数（不再需要 `core`）、少绑一个值，`<remarks>` 改掉；
- `Creature(...)` 的 `<remarks>`「the same way the avatar's presence is decided by its UID」
  已经变假，改成「the first of its six」。

#### 块 D —— `SqliteItemDomainStore.cs`（BD-006），四处

- `CarriedAvatarsAsync` 的 `WHERE avatar_uid = @uid` 换成
  `WHERE avatar_expire_date IS NOT NULL AND substr(item_core, 6, 4) = @uid`，
  `@uid` 改绑四字节。**读到的行与换之前逐行相同**（理由同块 B）。`<remarks>` 补上依据。
- **新增** `AvatarKeyOf(int)`——与块 B 的 `AvatarKey` 同一件事，但**没有跨文件复用**：
  这一处拿到的是一个 `int`（`item.Core.Value` 去重后的结果）而不是一个 core，
  且这两个类之间今天只有 `SqliteWorldItemTable` → `SqliteCharacterItemTable` 一个方向的引用。
  **这是本轮唯一一处知识重复（core 偏移 5 这件事写在了两个文件里），如实记下。**
- `FillAsync`：INSERT 列清单去掉 `avatar_uid`，不再绑 `@avatarUid`。
- 文件头补 `using System.Buffers.Binary;`。

#### 块 E —— 判据侧，逐文件

- `ItemCarrierShapeTests`：
  - `Items` 期望列表去掉 `"avatar_uid"`（**19 项**）；
  - `ASparseItemColumnIsNullableAndHasNoDefault` 去掉 `[InlineData("avatar_uid")]`
    ——它断言的是一个**已经不存在的列**，只能删，不是迁就实现改写；
  - `TheAvatarDetailsPrimaryKeyIsRestoredByAPartialIndex`（正向断言 `idx_items_avatar_uid`
    存在）**整条换成** `TheAvatarsUidIsNoLongerAKeyOfTheTable`：断言 `items` 无 `avatar_uid` 列、
    无 `idx_items_avatar_uid`、且**没有任何索引的 SQL 里出现 `avatar`**（大小写不敏感）。
    换掉它是所有者的范围裁定所要求的（退役那条索引），不是为迁就实现；
    它 `<remarks>` 里记的那条规则（「同一 UID 全库至多一行」）**没有跟着索引一起消失**，
    新的 `<remarks>` 点名了今天钉住它的两条判据。
- `AvatarDetailFollowsTheIdentityTests` / `InventoryLoadCharacterizationTests` /
  `SqliteEntryInventoryMutationRepositoryTests` / `SqliteInventoryEntryRepositoryTests`：
  只改**播种语句**（INSERT 少一列、少一个参数；`SqliteInventoryEntryRepositoryTests`
  那张手写 `items` 表少一列声明）。**没有一条期望值或断言被改。**
- `SqliteEntryInventoryMutationRepositoryTests` 另有两处：
  - 那条探针 `SELECT COUNT(*) FROM items WHERE avatar_uid=2000` 改成
    `WHERE avatar_expire_date IS NOT NULL AND substr(item_core,6,4)=X'D0070000'`
    （2000 的四字节小端）。**问的是同一件事、期望值仍是 `0`**：那一行没了。
  - **新增一条判据** `DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds`
    ——补的是「同一 UID 全库至多一行」的 **update 半边**（既有的
    `…RollsBackUidCollision` 只覆盖 insert 半边，且因为它用的是默认 `MainBandPlacements`
    ——每件东西都是堆叠物、没有身份——它走的其实是 insert 路径）。
    新判据自带一个 `NamedAvatars` 档案（模板 100 与 900 是时装），让那一行拿到身份，
    从而真的走 `UpdateOwnAsync`。加它的直接原因是那条 `[InlineData]` 删掉后
    持久化项目会掉到 314，而全量下限是 2215；**但它不是为了凑数**：
    第五节的变异实测证明它单独钉住了 update 那半边的条件。
- `SqliteItemDomainStoreTests`：`SeedAvatarDetailAsync` 不再写 `avatar_uid`，
  改用 `substr(item_core,6,4)=@uid` 一起限定行；`ReadAvatarDetailAsync` 的
  `WHERE avatar_uid=@uid` 同样换成「细节列非空 ＋ core 里的 UID 相等」；
  新增私有 `AvatarKeyOf(int)`。**断言一字未改。**

### 二、四项检查的实际输出

#### CK-030 全量

命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
（在 `.worktrees/items-own-their-identity` 下跑，退出码 **0**）

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 315 | 0 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |
| **合计** | **2215** | **2** | **0** |

逐项目相加：60+52+521+6+236+315+1025 = **2215**。与基线**持平**，不低于下限。
**没有偶发失败发生**，所以「异常类型＋栈根＋复跑即绿」那三样这一轮没有用上。

持久化项目仍是 315，来路是：删 1 条 `[InlineData]`（`avatar_uid` 的稀疏列断言）、
删 1 条 Fact（`TheAvatarDetailsPrimaryKeyIsRestoredByAPartialIndex`）、
加 1 条 Fact（`TheAvatarsUidIsNoLongerAKeyOfTheTable`）、
加 1 条 Fact（`DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds`），
315 − 1 − 1 + 1 + 1 = 315。

#### CK-031 字节锚

`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`
**一字未改**：`git status --short` 对该路径**无输出**（相对 HEAD `51cd99c2` 未修改），
mtime `09-05 09:04:19`，远早于本轮第一次落笔的 `09-05 21:12`。

`dotnet test … --filter "FullyQualifiedName~SelectCharacterAcceptedTranscriptTests|FullyQualifiedName~InventoryDeltaLift"`
→ **失败 0，通过 19，跳过 0，总计 19**。

#### CK-029 单一判定处

**「这一行有没有时装细节」的判定处只有一处**，位置（按成员名）：
`Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs` 的
**`SqliteCharacterItemTable.Avatar(SqliteDataReader, int)`**。
它的判据是**六列的第一列 `avatar_expire_date` 是不是 NULL**。
两个读取方都调它、都不另判一次：`SqliteCharacterItemTable.ReadByCharacterAsync`
与 `SqliteWorldItemTable.Read`（后者服务 `FindAsync` 与 `ReadUnreferencedAsync` 两条读）。

**`items` 上无 `avatar_uid`、无 `idx_items_avatar_uid`**，两条证据：

- **源码扫描**（`grep -rn "avatar_uid" Server/`，区分大小写，扫的是仓内 `Server/` 全树）：
  命中 **2 处，分别是** `ItemCarrierShapeTests.TheAvatarsUidIsNoLongerAKeyOfTheTable` 里的
  `Assert.DoesNotContain("avatar_uid", shape["items"])` 与
  `Assert.DoesNotContain("idx_items_avatar_uid", indexes.Keys)`——**两处都是否定断言**。
  `Server/src` 下命中 **0 处**。
  （**另有 2 处命中不计入**：`Server/tests/Dfo.Server.Host.Tests/bin/…/Data/dfo-server.db` 与
  `Server/tests/Dfo.Server.Protocol.Tests/bin/…/Data/dfo-server.db`，
  是**未被 git 跟踪的运行产物**——`git ls-files | grep '\.db$'` 空——里面是旧 schema 的库，
  正对应「旧库作废重建」，不在本 Task 范围内。）
  **不区分大小写**再扫 `avataruid`，`Server/src` 的 `.cs` 命中 **2 行，都是**
  `SqliteItemDomainStore.AvatarKeyOf(int avatarUid)` 自己的形参名，不是列名。
- **运行时断言**：新判据 `TheAvatarsUidIsNoLongerAKeyOfTheTable` 建一个全新库，
  用 `PRAGMA table_info('items')` 与 `sqlite_master` 直接查，三条断言全过。

#### CK-032 记载订正清单

见下面第四节。

### 三、两处「要当心」的查证结论与处置

#### 其一：`ON CONFLICT(avatar_uid)` 今天还起什么作用 —— **起作用，而且是承重的**

契约的怀疑（「T-011 之后世界行不再因移动而销毁，所以它可能已经失去存在理由」）
**经查不成立**，理由是它跟移动没关系：

- 它在 `SqliteCharacterItemTable.InsertOwnAsync` 上，唯一生产调用方是
  `InventoryEntryAssembly.SaveAsync`（另一个 `InsertOwnAsync` 调用方
  `CharacterRosterService` 建角色时传 `avatarDetail = null`，绑到的是 `DBNull`，从不触发它）。
  那条路径与 `SqliteItemDomainStore` 的「删了重插」是**两套结算**。
- 它做的事是**跨行唯一性**：一件时装 UID 全库至多一行带着它的细节。
  被拒时插入落空、返回 `null`、调用方抛 `InvalidDataException` 并回滚。
- **既有判据 `SqliteEntryInventoryMutationRepositoryTests`.`DirtyLeaseSave_PersistsAvatarDetails
  AuthoritativelyAndRollsBackUidCollision` 正是钉住这条拒绝的**：角色 60 的租约声称一件
  UID 9000 的时装，而 9000 在角色 70 手上 → 必须抛 + 回滚。

**契约把它说成「M-2 的产物：`FillAsync` 用 `carried[item.Core.Value]` 重新挂回」，
这一句对不上代码**：那套搬运在 `SqliteItemDomainStore`（`CarriedAvatarsAsync` + `FillAsync`），
而 `FillAsync` 的 INSERT **根本没有 `ON CONFLICT`**。两者是不同的东西，本节按代码为准。

**处置**：规则保留、换写法。`ON CONFLICT(avatar_uid) … DO NOTHING`
→ `INSERT … SELECT … WHERE NOT EXISTS(<CarriesTheAvatar>)`；
`UpdateOwnAsync` 里那条同源的 `NOT EXISTS` 同样只换取值来源。
被授权的两处行为变化之外**不新增行为变化**，靠的就是这一步。

#### 其二：`CarriedAvatarsAsync` 的去留 —— **留，且本轮没有改变它的可达性**

- **可达性没变**：本轮只把它的检索键从 `avatar_uid = @uid` 换成
  「`avatar_expire_date IS NOT NULL` ＋ `substr(item_core,6,4) = @uid`」。
  `avatar_uid` 从写入的第一天起就是 `core.Value` 的副本
  （`BindAvatar` / `BindAvatarColumns` / `FillAsync` 三个写入方都从 core 取值），
  所以两种写法**选出的行逐行相同**。既没有让它更走不到，也没有让它走得到。
- **它今天仍有的那一种理由**：`Survivors` 三趟分别是「同位同字节」「异位同字节」
  「同位同 id/kind/value」。一件时装**同一批次里既换了位置、core 字节又变了**，
  三趟都不认它，于是旧行被删、新行由 `FillAsync` 写——这时才轮到搬运。
  这一种情形没被三趟覆盖，所以这段机制不是「没有理由」。
- **它今天的覆盖是零**（本轮变异实测，见第五节）：让 `CarriedAvatarsAsync` 恒返回空表，
  **315 条持久化判据全绿**。这给 `AGENTS.md` 早就记着的「`Relocate_KeepsAnAvatars
  DetailsBecauseTheLinkIsInsideTheCore` 现在靠『行没被删』通过」补上了硬证据。
- **处置**：代码**不删**（理由还在），那条判据**不改**（它今天证明的
  「行没被删所以细节还在」本身值得钉住，只是名字比内容大——`AGENTS.md`
  的「两条判据的名字不再描述它证明的东西」已经记着，本轮把变异证据补了进去）。

### 四、记载订正清单（因本 Task 而变假的记载，逐条给出订正位置）

**变假的记载共 5 条，分别是：**

1. **`AGENTS.md`「`items` 今天 **20 列**」及其分组表里「时装 | 7 | `avatar_uid` ＋ 六列细节」。**
   → 订正位置：`AGENTS.md`「物品：有身份的东西，和它在哪，是两张表」小节，
   改成 **19 列** 与「时装 | 6 | 六列」。
2. **`AGENTS.md`「已知缺陷与覆盖缺口」里的整条「**`avatar_uid` 这个半身份还在。**」**
   （原话：「物品拿到了真身份 `item_uid`，但「这一行有没有时装细节」今天仍由 `avatar_uid`
   是否为 NULL 决定（`items` 上另有 `idx_items_avatar_uid` 唯一部分索引）。它的退役曾被指给
   「退役旧形状」那一步，而交付的形状仍是 `avatar_*7`——**那条派工没有落地。**」）
   → 订正位置：该条**整条删除**，并在同文件的
   「三条已被证伪的记载，订正」小节里**原文照录后说明何时为真、何时停止为真**
   （该小节标题本轮由「一条」改为「三条」）。
3. **`Docs/Refactor/TableOwnershipRuling.md` 的 D-15**（「时装细节表的主键靠
   `items.avatar_uid` 上的部分唯一索引复原」）**与该文档 `character_avatar_detail.item_uid`
   → `items.avatar_uid` 那一行。**
   → 订正位置：**不改那份历史裁定的正文**（按本仓「改已证伪的记载可越任务边界，但要补记
   修正案」的规矩，且它不在本 Task 的允许文件内），修正案写在 `AGENTS.md`
   「三条已被证伪的记载，订正」的**其三**：它所保的规则仍在，位置换成了两条写入语句自己的条件。
4. **`behavior-change-list.md`「额外发现：`avatar_uid` 这条派工没有落地」整节**，
   连同它结尾那句「**这不是行为变化**……但那条派工确实没有落地」。
   → 订正位置：**原节一字不改**，紧跟其后新增
   「**订正（TASK-13，2026-09-05）：上面这一节从「没有落地」变成「已经落地」**」，
   里面列全了退役后的形状、单一判定处、以及「这不是行为变化」那句今天要加的限定。
5. **两句生产注释**：
   `SqliteCharacterItemTable.ReadByCharacterAsync` 的 `<remarks>`
   「Whether a row has them is decided by whether the avatar UID is there」，
   与 `SqliteWorldItemTable.Creature(...)` 的 `<remarks>`
   「the same way the avatar's presence is decided by its UID」。
   → 订正位置：两处 `<remarks>` **就地改掉并点名**（前者改成「由六列的第一列决定」，
   后者改成「the first of its six」）。
   同时改掉的还有 `BindAvatar` / `BindAvatarColumns` 的「seven nulls」→「six nulls」
   与「repeated as a column of its own so that it can be indexed」，
   以及 `SqliteWorldItemTable.Read` 的「seventeen columns」→「sixteen columns」。

**另新增 1 条如实记录（不是订正，是新出现的覆盖缺口）**：`AGENTS.md`
「已知缺陷与覆盖缺口」新增一条——「同一个时装 UID 全库至多一行」今天只覆盖两条写入语句，
另外两个写入方（`SqliteWorldItemTable` 的 `InsertAsync`/`UpdateAsync`、
`SqliteItemDomainStore.FillAsync`）原本靠索引抛错，现在不再被拦。同一件事在
`behavior-change-list.md` 的订正小节里写得更细（含为什么这两处到不了生产上的重复）。

### 五、变异实测（文件级备份 + `sha256sum` 核对还原，全程未用任何 git 还原类命令）

| 变异 | 结果 |
|---|---|
| 把 `CarriesTheAvatar` 改成恒假（`"1=0 AND @avatarKey IS NOT NULL"`） | 持久化 **失败 2 / 通过 313**，红的**分别是** `DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision` 与 `DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds` |
| 在 `CarriedAvatarsAsync` 开头直接 `return carried;`（恒空表） | 持久化 **失败 0 / 通过 315**，**全绿** |

还原核对：
`InventoryItemTables.cs` `60877044b4d3070f9c927f4e13d5bf9a3be41d144f07ad0e155cff3f26a3e533`、
`SqliteItemDomainStore.cs` `4c3f49c6dc329974bdc8f98a7c025ed871c5ad7fda5146847584709651f047a8`
——变异前后**逐字节相同**。

## Executor Self-check — round 1

### 我发现的问题

1. **契约「已知的取证起点」里对 `ON CONFLICT(avatar_uid)` 的定性写错了。**
   它写「曾是时装细节跨『删了重插』结算而不丢的机制（M-2 的产物：`FillAsync` 用
   `carried[item.Core.Value]` 重新挂回）」——`FillAsync` 在另一个文件、另一套结算里，
   而且它的 INSERT **没有 `ON CONFLICT`**。这个 `ON CONFLICT` 是跨行唯一性守卫，
   由既有判据 `…RollsBackUidCollision` 钉着。我按代码办，没按契约那句话办。
2. **`IWorldItemTable`（`Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`）
   的类型级 `<remarks>` 说「Settlement reaches this now」，这句今天不成立。**
   证据：`grep -rn "IWorldItemTable\|SqliteWorldItemTable" Server/src Server/tests` 的
   src 侧命中只有接口声明与实现类声明，`PersistenceModule` 只注册了 `ICharacterItemTable`，
   `SqliteWorldItemTable` 只在 `WorldAndSlotTableTests` 与 `CreatureIdentityFitsTheWireTests`
   里被 new 出来。实现类自己的 `<remarks>`（「Nothing in the server reaches this yet.
   It is registered nowhere」）才是对的。**这不是本轮造成的，而那个文件也不在本 Task 的
   允许文件内，所以我没有改它，在此报出。**
3. **本轮唯一一处知识重复**：core 偏移 5 起 4 字节是时装 UID 这件事，现在写在
   `SqliteCharacterItemTable.AvatarKey` / `CarriesTheAvatar` 与
   `SqliteItemDomainStore.AvatarKeyOf` **两个文件**里。要消掉它得动
   `InventoryItemCore.cs`（加一个偏移常量），**那个文件不在允许文件内**，所以没动。
4. **`InventoryEntryAssembly.WithAvatarDetail` 里那条 `item.Core.Value <= 0` 的接线校验，
   `TASK-06` 曾说「它的正确退役位置是 `avatar_uid` 一起走的那一刻」——我没有拆它。**
   两个理由：`InventoryEntryAssembly.cs` 不在允许文件内；而且 TASK-06 给的
   理由 (a)（既有判据 `AvatarCarrierRulesTests.ACharactersAvatarWithoutAUsableUidIsRefused`
   钉着它）今天依然成立，拆它就得删既有判据。**留给控制器裁。**

### 我**没有**验证的东西（明说）

1. **我没有验证 SQLite 对 `substr()` 于 BLOB 上是否在所有构建下都按字节而非字符切。**
   我读的是 SQLite 的文档规则（BLOB 上按字节、下标从 1 起），实证只到「判据全绿」这一层
   ——包括那条按 `X'D0070000'` 找行的探针与两条守卫判据。没有单独写一条只测 `substr` 的判据。
2. **我没有验证老库（已经带着 `avatar_uid` 列和 `idx_items_avatar_uid` 的库）在新代码下的行为。**
   本成果已裁「旧库作废重建」，我按范围外处理。可预期的是：老库上那条唯一索引还在，
   而新代码不再写那一列，于是该索引的部分集合恒空、不再拦任何东西——**这只是推理，没有实测。**
3. **我没有验证「同一批次里既换位置、core 字节又变了」这条 `CarriedAvatarsAsync`
   仍然有理由的情形在生产上真的能发生。** 我只证明了 `Survivors` 的三趟不认它
   （读代码），以及全仓没有判据覆盖它（变异实测）。**能不能从某条用例走到那里，没查。**
4. **我没有跑第二遍全量。** 本轮全量一次通过、无偶发失败，所以没有触发
   「复跑即绿」那条判别流程。
5. **我没有对 `AGENTS.md` 与 `behavior-change-list.md` 之外的记载做全仓扫描。**
   `Docs/` 我只扫了 `avatar_uid` / `idx_items_avatar_uid` 两个字串（区分大小写），
   命中的 `Docs/Refactor/TableOwnershipRuling.md` 与两份中文设计稿里的
   `character_avatar_uid_sequence`（**那是另一张表的名字，与 `items.avatar_uid` 无关，不计入**）
   已在第四节交代。**没有扫「时装细节的键」这类同义说法。**

### 不确定的地方

1. **「不引入新的行为变化」这一条，我认为守住了主干、但没有守到分毫。**
   守住的：入场存盘那条生产路径上，插入与更新两半的拒绝**逐条保持**，
   且两半都有判据钉住（变异实测两条一起红）。
   没守到的：那条唯一索引**顺带**为另外两个写入方兜的底没有原样搬走
   （`SqliteWorldItemTable` 生产无人调用；`SqliteItemDomainStore.FillAsync` 的细节
   只可能来自 `CarriedAvatarsAsync`，而索引还在时那一行必然是本批次要删的那一行，
   否则插入当场撞索引）。**我判断这两处到不了生产，但这是推理不是实测，已如实记进
   `AGENTS.md` 与 `behavior-change-list.md`，并明确不作为被授权的行为变化。**
   如果控制器认为这已经越线，可选的补法是给这两处各补一条同样的 `NOT EXISTS` 条件
   ——但那会把「撞索引抛 `SqliteException`」换成「静默落空」，是另一种行为改动，
   所以我没有自作主张。
2. **新增判据 `DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds`
   是在改动之后补的，不是先写判据再改实现。** 这偏离了契约里的 test-driven-development。
   补救是第五节的变异实测（把守卫改成恒假，它确实红）。**如实记下这个偏离。**
3. **`ItemCarrierShapeTests` 那条 Fact 我是「换」而不是「删」。**
   我认为把正向断言（那条索引存在）换成负向断言（列与索引都不在、且没有后继索引顶替）
   是这次退役该有的形状，而不是为迁就实现改写既有期望——但这是一处判断，**请评审复核**。
4. **`InsertOwnAsync` 从 `INSERT … VALUES … ON CONFLICT` 变成 `INSERT … SELECT … WHERE NOT EXISTS`，
   并发语义我只推理到「SQLite 同一时刻只有一个写者，且这一批写入在同一个事务里」这一层，
   没有做并发实测。** 原来的唯一索引是存储级保证，现在是语句级条件；
   在本仓「一个数据库一个可失败参与者」的事务模型下我认为等价，**但没有并发判据能证明它。**

## Controller Disposition — DSP-TASK-13-01

执行方交回两件，**其中一件是控制器在 TASK-11 亲手写下的假话**。

### 一、`IWorldItemTable` 那段注释——**我写的那句也是假的，已改**

TASK-11 那轮，控制器改掉了该处三句变假的注释，并在新文里写了
「**Settlement reaches this now**」。

**执行方报出它也是假的。控制器复核属实**：`SqliteWorldItemTable` 只在 `InventoryItemTables` 里声明，
`PersistenceModule` **只注册 `ICharacterItemTable`**，该实现类**只在两个判据文件里被 `new`**
（`WorldAndSlotTableTests`、`CreatureIdentityFitsTheWireTests`）。
结算写的是同一张表，但走它自己的语句，**不经这个接口**。

**这是我改一段假记载时，在原地写下的第四句假话。**

已改写，并**两组句子都点名**——第一组（三句）为何曾为真、何时停止为真；
第二组（我写的那句）**在写下的当天就是错的**，错因是「变的是那张表，不是这个接口的调用方」。
新文末句记着这个教训：
**一段活得比它所描述的代码还久的注释比没有更糟，而一次引入自己假句的订正，正是这种注释的来路。**

该文件不在 TASK-13 的允许清单内。**按所有者裁定的规矩（改已证伪的记载可跨任务边界，须补修正案），
本节即是那份补记。** 改后全量复跑仍为 **2215 通过 / 2 跳过 / 0 失败**。

### 二、被删索引顺带兜的那个底——交快审独立判断，控制器不预先裁

执行方如实报出：那条唯一索引**顺带**为 `SqliteWorldItemTable`（生产无人调用）
与 `SqliteItemDomainStore.FillAsync` 兜的底，**没有复制成语句条件**——
原本会撞索引抛 `SqliteException`，现在会静默通过。
它判断两处到不了生产（**自述为推理，非实测**），已记进两份记载并**明确不作为被授权的行为变化**。

**控制器不预先表态。** 请快审独立判断：
(a) 那两处今天是否真的到不了生产；
(b) 「静默通过」与原先「抛异常」的差别，是否构成一次**未被授权的行为变化**；
(c) 若构成，本轮是否必须补回那道条件。

### 三、控制器写错的一处契约定性，记下来

契约的「要当心」第 1 条把 `ON CONFLICT(avatar_uid)` 说成
「时装细节跨『删了重插』结算而不丢的机制（M-2 的产物：`FillAsync` 用 `carried[…]` 重新挂回）」。

**执行方查明这是错的**：那是**另一文件**的 `CarriedAvatarsAsync` ＋ `FillAsync` 干的事，
而后者**根本没有 `ON CONFLICT`**；`ON CONFLICT(avatar_uid)` 是**跨行唯一性守卫**，
由既有判据 `DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision` 钉着。

**执行方没有照我的错误定性去动它，而是查清后保规则、换写法**，并做了变异验证。
**这正是「查清再动，不要假设它还在起作用，也不要假设它已经没用」要的结果。**

## Independent Quick Review — QRV-TASK-13-01

- 评审身份：独立快审（fresh context），与执行方、控制器均非同一上下文。
- 评审的契约版本：Contract revision 1，无修订案；连同 `DSP-TASK-13-01` 一并读。
- Owned diff 认定：**按 mtime 自行圈定**（`find Server AGENTS.md .rks-agent Docs -newermt "2026-09-05 21:00"`，
  剔除 `bin`/`obj`/`TestResults`，再剔除 `21:03:42` 的 TASK-12 两个文件）。
  结果与执行方所述**逐个吻合**：生产 3（`CharacterDatabaseSchema.cs` 21:12、
  `InventoryItemTables.cs` 21:21、`SqliteItemDomainStore.cs` 21:21）、判据 6（21:16 五个 ＋ 21:19 一个）、
  记载 2（`AGENTS.md` 21:24、`behavior-change-list.md` 21:25），共 11 个；
  另有控制器的 `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`（21:31）与本 Task 文件（21:33）。
  **允许清单之外没有第 12 个生产文件被碰。**

### 一、我自己跑／自己数的结果

**CK-030 全量（我自己跑，退出码 0）**——逐项目，不用 `grep -c`：
`Dfo.Server.Protocol.Tests` 521/0/0、`Dfo.Pvf.Tests` 60/0/0、`Dfo.Server.Networking.Tests` 52/0/0、
`Dfo.Server.Persistence.Tests` 315/0/0、`Dfo.Server.Host.Tests` 6/0/0、
`Dfo.Server.Infrastructure.Tests` 236/**2 跳过**/0、`Dfo.Server.Application.Tests` 1025/0/0。
相加 521+60+52+315+6+236+1025 = **2215 通过 / 2 跳过 / 0 失败**，与声称一致，无偶发失败。

**CK-031 字节锚**：`git status --short` 与 `git diff HEAD` 对
`SelectCharacterAcceptedTranscriptTests.cs` **均无输出**（相对 `51cd99c2` 未修改），mtime `09-05 09:04:19`。
单跑该类 ＋ `InventoryDeltaLift`：**失败 0，通过 19，总计 19**。

**1. `avatar_uid` 与索引确已退役（我自己拆逗号数的）**
`CREATE TABLE items` 括号内拆顶层逗号（`CHECK(...)` 内的不计），得 **19 列，分别是**
`item_uid`、`item_core`、`cargo_character_id`、`created_at`、`updated_at`、
`avatar_expire_date`、`avatar_clear_id`、`avatar_jewel_socket`、`avatar_color1`、`avatar_color2`、
`avatar_delete_date`、`creature_field04`、`creature_mode_flag`、`creature_progress_value`、
`creature_mode1_field0a`、`creature_mode1_field0b`、`creature_field_after_value`、
`creature_text`、`creature_tail_flag` ＝ 2＋3＋6＋8。无 `avatar_uid`。
`IndexDeclarations` **3 条，分别是** `idx_item_slots_core`、`idx_item_slots_account_slot`、
`idx_characters_account`；全仓扫 `CREATE INDEX` / `CREATE UNIQUE INDEX` 于 `Server/src` 只命中这 3 行，
**`items` 上今天一条索引都没有，无后继顶替**。
扫 `avatar_uid`（区分大小写，`--include=*.cs`，全 `Server/` 树）命中 **2 处，分别是**
`ItemCarrierShapeTests.TheAvatarsUidIsNoLongerAKeyOfTheTable` 的两条 `Assert.DoesNotContain`；
`Server/src` 下 **0 处**。判据侧期望列表 `ItemCarrierShapeTests.Items` 我逐个数为 **19 项**。

**2. 单一判定处 —— 读侧成立，但「只有一处」这句要收窄**
读侧确为 `SqliteCharacterItemTable.Avatar(SqliteDataReader, int)`，判据是六列第一列
`avatar_expire_date` 是否 NULL；两个读取方（`SqliteCharacterItemTable.ReadByCharacterAsync`、
`SqliteWorldItemTable.Read`）都调它、都不另判。**没有第二处把行变成 `AvatarDetailRow`。**
**但**「第一列非空 ＝ 这行是时装」这个判据文本在 `Server/src` 里今天出现 **3 处，分别是**：
`SqliteCharacterItemTable.Avatar`（`IsDBNull`）、`SqliteCharacterItemTable.CarriesTheAvatar`
（`held.avatar_expire_date IS NOT NULL`）、`SqliteItemDomainStore.CarriedAvatarsAsync`
（`WHERE avatar_expire_date IS NOT NULL`）。
**这不是本轮造成的回退**——退役前同样是三处（`Avatar` 的 `avatar_uid IS NOT NULL`、
部分索引的 `WHERE avatar_uid IS NOT NULL`、`CarriedAvatarsAsync` 的 `avatar_uid = @uid`）。
**但 `AGENTS.md` 那句「由细节列自己回答，只有一处」按字面读是过强的**，应写成「**读侧**只有一处」。
记为非阻塞的措辞不准，见第三节 N-1。

**3. `ON CONFLICT(avatar_uid)` 的处置 —— 三问逐条自查，全部证实**
(a) 既有判据 `SqliteEntryInventoryMutationRepositoryTests` 的
`DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision` 确实钉着它：
我读了该用例，它把 UID 9000 放在角色 70 手上、角色 60 的租约来抢，断言抛
`InvalidDataException` ＋ 回滚 ＋ 探针
`SELECT COUNT(*) … WHERE avatar_expire_date IS NOT NULL AND substr(item_core,6,4)=X'D0070000'` 为 0
（2000 的四字节小端确为 `D0 07 00 00`，我自己换算过）。
(b) 换写法后拒绝表现逐条不变：`InsertOwnAsync` 由 `INSERT … VALUES … ON CONFLICT DO NOTHING`
改为 `INSERT … SELECT … WHERE NOT EXISTS(<CarriesTheAvatar>) RETURNING item_uid`——落空即无行、
返回 `null`、`InventoryEntryAssembly` 抛 `InvalidDataException` 回滚，路径未变；
`UpdateOwnAsync` 的 `NOT EXISTS` 只换取值来源。UID 取值来源我核到底：
`InventoryItemCore.Encode` 里 `WriteInt32LittleEndian(core.AsSpan(5), value.Value)`，
故 `substr(item_core, 6, 4)` 正确；且 `git show HEAD:` 证实 `@avatarUid` 历来绑的就是
`detail is null ? DBNull : core.Value`，两种写法**选出的行确实逐行相同**。
(c) **变异我重做了一次**：把 `CarriesTheAvatar` 改成 `"1=0 AND @avatarKey IS NOT NULL"`
→ 持久化 **失败 2 / 通过 313**，红的**分别是**
`DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision` 与
`DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds`——与声称**逐条一致**。

**4. `CarriedAvatarsAsync` 的变异我也重做了，并且扩到全仓**
在 `CarriedAvatarsAsync` 开头插 `if (carried.Count == 0) { return carried; }`（恒返回空表；
用条件形式而非裸 `return` 以避开 CS0162），跑**全量**：
521+60+52+315+6+236+1025 = **2215 通过 / 2 跳过 / 0 失败**，一条不红。
**证实：这段搬运机制全仓零覆盖，不止持久化项目零覆盖。**

两次变异均用文件级备份还原，`sha256sum` 核对通过：
`InventoryItemTables.cs` `60877044b4d3070f9c927f4e13d5bf9a3be41d144f07ad0e155cff3f26a3e533`、
`SqliteItemDomainStore.cs` `4c3f49c6dc329974bdc8f98a7c025ed871c5ad7fda5146847584709651f047a8`
——与执行方所记的哈希也一字不差。**全程未用任何 git 还原类命令。**

**关于「留着它是否违反不得留一段没有理由的机制」——我的独立判断：不违反。**
T-011 摘掉抬起机制时的判据是「全仓 `LiftAsync` / `NoContainer` **零命中**」，即那段代码
**连调用点都没有**；而 `CarriedAvatarsAsync` 有生产调用点（`FlushAsync` 每次结算都调，
且 `SqliteItemDomainStore` 是 `PersistenceModule` 里注册的生产单例），
缺的是**判据覆盖**，不是**理由**。它守的那一格（同一批次里既换位置、core 字节又变了——
`Survivors` 三趟：同位同字节／异位同字节／同位同 id-kind-value，都不认）我读代码复核成立。
删掉它会引入一次**未被授权的行为变化**（那一格上时装细节静默丢失），恰恰违反验收条件 6。
**结论：留是对的；该记的是「零覆盖」这个缺口，执行方已记进 `AGENTS.md`。**

**7. 六个判据文件逐个看过，没有断言被削弱、删除或放宽**
（十三个任务未提交，`git diff HEAD` 是累计的，所以我按「删除行里含 `Assert`」筛，再逐处判归属）：
- `ItemCarrierShapeTests`：`ASparseItemColumnIsNullableAndHasNoDefault` 今天仍逐个钉住
  **六个时装细节列全部**（`avatar_expire_date`／`avatar_clear_id`／`avatar_jewel_socket`／
  `avatar_color1`／`avatar_color2`／`avatar_delete_date`）＋ `cargo_character_id` ＋ 八个契约兽列；
  只少了 `avatar_uid` 这个**已不存在的列**。
  `TheAvatarDetailsPrimaryKeyIsRestoredByAPartialIndex` → `TheAvatarsUidIsNoLongerAKeyOfTheTable`
  这个**换**我复核认可：新条不是弱化，它多断言了一件旧条不曾断言的事——
  「没有任何索引的 SQL 里出现 `avatar`」（大小写不敏感），即**没有后继索引顶替**；
  `<remarks>` 也点名了今天钉住那条规则的判据。
- `SqliteEntryInventoryMutationRepositoryTests`：删除侧只有那条探针的 SQL 文本，问的是同一件事、
  期望值仍是 `0`；新增的 `DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds`
  断言实在（抛 `InvalidDataException` ＋ 消息含 `stale or missing` ＋ 两侧角色各自回滚后的状态），
  且我的变异实测证明它单独钉住 update 半边。
- `InventoryLoadCharacterizationTests` / `SqliteItemDomainStoreTests`：删除行里 **0 处 `Assert`**。
- `SqliteInventoryEntryRepositoryTests`：删除行里唯一一处 `Assert` 是
  `Assert.Equal(301, item.StorageEntryId)`，被 `Assert.Null(item.Identity)` 顶替——
  那是 `StorageEntryId → Identity` 更名那一轮的事，**不属本轮**（本轮该文件只动播种）。
- `AvatarDetailFollowsTheIdentityTests`（未被 git 跟踪，无 diff 可比）：读全文，
  两条用例仍逐字段断言六个时装事实（`ExpireDate`／`ClearAvatarId`／`JewelSocket`／
  `Color1`／`Color2`／`DeleteDate`），未见弱化痕迹。
- 契约点名的第 7 个文件 `WorldRowSurvivesTheMoveTests` 确实**一字未动**（mtime `18:37:36`，早于本轮），
  其 `AvatarUid = 777` 确为 C# 常量而非列名。

**8. 记载订正完整，且点名原文**
`AGENTS.md`：「19 列」与分组表「时装 | 6」已订正；「已知缺陷与覆盖缺口」里
「`avatar_uid` 这个半身份还在」**整条删除**，并在「三条已被证伪的记载，订正」的**其二**
**原文照录**后写明何时为真、何时停止为真；**其三**为 `Docs/Refactor/TableOwnershipRuling.md`
D-15 的补记修正案（不改历史裁定正文，符合本仓规矩）；小节标题「一条」→「三条」已同步。
`behavior-change-list.md`：原节一字未改，其后新增「订正（TASK-13，2026-09-05）」，
并**主动收窄**了原节「这不是行为变化」那句。新增的覆盖缺口条目两处都在。
**我按 `avatar_uid` / `idx_items_avatar_uid` 重扫，未发现遗漏的变假陈述。**

**9. BOM**：本轮 13 个改动文件（含控制器那个与本 Task 文件）首三字节逐个查过，
`.cs` 全为 `75 73 69`（`usi`），三个 `.md` 为 `23 20 …`（`# `），**无一为 `ef bb bf`**。

**另核控制器自己那处改动（`IWorldItemTable` 的 `<remarks>`）**：
(a) 「Settlement reaches this now」确为假——`PersistenceModule` 只
`TryAddSingleton<ICharacterItemTable>`，`Server/src` 里除接口与实现类自身声明外**无任何生产引用**，
`SqliteWorldItemTable` 只在 `WorldAndSlotTableTests` 与 `CreatureIdentityFitsTheWireTests` 里被 `new`。
(b) 新写的那段准确，且**两组旧句子都点名**（第一组三句「何时为真、何时停止为真」；
第二组「写下当天就是错的」并说明错因是「变的是那张表，不是这个接口的调用方」）。
(c) 该文件是纯接口／record 声明，改动落在 `<remarks>` 内；全量在我这边 2215/2/0，**未影响任何判据**。

### 二、控制器点名的三问 —— 我的独立回答

**(a) 那两处今天是否真的到不了生产？——一处是，一处「未查清」，且执行方给的理由不成立。**

- `SqliteWorldItemTable`（`InsertAsync`／`UpdateAsync`）：**证实到不了生产。**
  依据同上：无 DI 注册、生产侧零引用、只在两个判据文件里被 `new`，且它自己的类级
  `<remarks>` 就写着「registered nowhere」。**这一半我认可。**
- `SqliteItemDomainStore.FillAsync`：**这一半不成立。** `SqliteItemDomainStore` 是
  `PersistenceModule` 注册的生产单例，经 `ItemContainers`／`SelectCharacterPostEntryServices`／
  `SelectCharacterEntryInventoryDomainService` 使用，`FlushAsync → FillAsync` **是生产路径**。
  执行方说「那一行必然是本批次要删掉的那一行，否则插入当场撞索引」——
  **这句是在复述旧代码会抛，不是在证明那个情形不会出现**，逻辑上是循环的。
  实际上 `CarriedAvatarsAsync` 的 `SELECT` **没有任何归属限定、也没有和本批次 displaced 集合绑定**，
  它按 UID 全库找第一行（连 `ORDER BY` 都没有）。于是至少有一条构造：
  某行 core 里带 UID X 但**不带细节**（`FillAsync` 自己就会造出这种行——kind 8 而 `carried` 无值时
  六列全 NULL），而另一行带着 X 的细节且**不在本批次的删除范围内**；此时旧代码撞索引抛
  `SqliteException` 整笔结算回滚，新代码**静默插入第二行同 UID 细节**，此后
  `CarriedAvatarsAsync` 选中哪一行不确定。**我无法证明这条构造能从某个用例走到，
  但执行方同样没有证明它走不到——这一半应记为「未查清」，而不是「到不了生产」。**

**(b) 构成一次未被授权的行为变化吗？——构成，但严重度低。**
差别不是取值差别，而是**失败方式**：一道最后防线由「响亮抛错并回滚」变成「静默通过并留下重复行」。
它落在生产已注册的类上、且全仓零覆盖，所以既不会被判据发现，也不会被运行时发现。
验收条件 6 写的是「不引入新的行为变化——本成果只授权 RQ-009 与 RQ-010 两处」，
这一处不在那两处里。执行方如实记载并声明「不作为被授权的行为变化」是诚实的，
但**记载不等于授权**。

**(c) 是否必须补回？——`FillAsync` 这一处：是，且成本很小；`SqliteWorldItemTable`：否。**
执行方不补的理由是「那会把撞索引抛 `SqliteException` 换成静默落空，是另一种行为改动」——
**这句对 `FillAsync` 不成立**。`FillAsync` 读的是 `ExecuteScalarAsync`，且下一行就是
`allocated is null ? throw new InvalidOperationException("The world took the item and named no identity.")`。
给它的 `INSERT` 加上同一段 `WHERE NOT EXISTS(…)` 之后，被拒时**依然是响亮抛错并回滚**，
只是异常类型从 `SqliteException` 变成 `InvalidOperationException`——
这比「抛错 → 静默重复」小得多。而合法的搬运路径**不受影响**：
`FlushAsync` 是先 `EmptyAsync` 再 `FillAsync`，源行此时已被删，`NOT EXISTS` 恒真。
`SqliteWorldItemTable` 我**不要求**动：它到不了生产已被证实，动它才是范围外。

### 三、发现汇总

**阻塞项（1 条）**

- **B-1｜`SqliteItemDomainStore.FillAsync` 丢掉了索引原本兜的唯一性守卫，构成未被授权的行为变化。**
  位置（按成员名）：`Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`
  的 `FillAsync`。该类是生产注册单例（`PersistenceModule` 的
  `TryAddSingleton(_ => new SqliteItemDomainStore(databasePath))`），
  `FlushAsync → FillAsync` 是生产路径；执行方所给的「到不了生产」理由是对旧代码行为的复述，非可达性论证。
  **要求的补救（`SqliteItemDomainStore.cs` 在允许文件内，改动很小）**：
  给 `FillAsync` 的 `INSERT` 加上与 `CarriesTheAvatar` 同义的条件
  （`INSERT … SELECT … WHERE NOT EXISTS(SELECT 1 FROM items held WHERE
  held.avatar_expire_date IS NOT NULL AND substr(held.item_core,6,4)=@avatarKey)`，
  仅在该行确实带细节时绑非空 `@avatarKey`），并补一条判据钉住它
  （被拒时仍抛错回滚，而合法搬运——源行已在 `EmptyAsync` 里删掉——照常插入）。
  若所有者／控制器裁定「零覆盖的纵深防线不计入验收条件 6 的行为变化」，
  那也应当以**一次明确裁定**落到记载里，而不是停在执行方自述为推理的那句上。

**非阻塞、建议随手改（2 条）**

- N-1｜`AGENTS.md`「**「这一行有没有时装细节」由细节列自己回答，只有一处。**」按字面过强。
  读侧确为一处，但判据文本在 `Server/src` 有 3 处（见第一节第 2 点）。建议改为「**读侧**只有一处」，
  并点名另两处是写入侧条件与结算侧检索条件。同样的措辞也出现在 `behavior-change-list.md` 的订正小节。
- N-2｜执行方自述的 TDD 偏离（新判据后补）我认可其补救充分：变异实测证明它单独钉住 update 半边。
  这一条如实记着即可，**不要求返工**。

**核实无误、不需处理的**：字节锚未动；全量与基线持平；哈希还原一致；BOM 无；
记载订正点名原文；判据无削弱；`CarriedAvatarsAsync` 留下的决定正确；
控制器对 `IWorldItemTable` 注释的再订正准确且不影响判据。

### 四、结论

**verdict: `changes_required`**

除 B-1 外，本 Task 的交付、取证与记载我逐条独立核实后全部成立，且执行方的自查坦白程度高
（它自己把 B-1 抬到了台面上，只是把它归到了「推理认为到不了生产」）。
B-1 的补救限于 `SqliteItemDomainStore.FillAsync` 一处加条件 ＋ 一条判据，
在本 Task 的允许文件与既有形状之内，不需要扩大范围。

## Execution Result — round 2

针对 `QRV-TASK-13-01` 唯一阻断项 B-1 的整改。**由控制器直接执行。**
round 1 与快审记录原样保留，不回改。

### 快审的论证成立，执行方不补的理由确实是错的

控制器逐条复核：

- `SqliteItemDomainStore` **是 `PersistenceModule` 注册的生产单例**
  （`TryAddSingleton(_ => new SqliteItemDomainStore(databasePath))`，再以它兑现 `IItemDomainStore`），
  `FlushAsync → FillAsync` **是生产路径**。
- `FillAsync` 用 `ExecuteScalarAsync` 拿回新身份，**紧接着就是**
  `allocated is null ? throw new InvalidOperationException("The world took the item and named no identity.")`。
  **所以加上条件之后仍然响亮抛错并回滚，不是「静默落空」**——执行方不补的理由不成立。

### 补回的守卫

`FillAsync` 的 `INSERT … VALUES(…)` 改成 `INSERT … SELECT … WHERE`，条件是：

> 新行**不带**时装细节（`@expire IS NULL`），**或者**没有任何**带着细节**的行
> 携带同一个时装 UID（`substr(item_core, 6, 4)` 比对）。

**这与被删的那条索引语义等价**，而非更严或更宽：旧索引是
`UNIQUE ON items(avatar_uid) WHERE avatar_uid IS NOT NULL`，而旧 `FillAsync` 写
`"@avatarUid", facts is null ? DBNull.Value : item.Core.Value`——
**即旧索引本来就只约束「带细节的行」**。`@expire IS NULL OR …` 正是这一点的镜像。

**这一条是控制器在写判据时才想清楚的**：第一版判据（两行只有一行带细节）**没有红**，
因为守卫按设计不该在那种情形下拦。**判据没红逼出了对语义边界的准确理解**。

### 补的判据与变异验证

新增 `SqliteItemDomainStoreTests.TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`：
清空一个带细节的时装行，然后写**两行**同一时装——两行都从被顶掉的那行拿到细节，
第二行正是旧索引所禁止的那一对。断言抛 `InvalidOperationException` 且消息含
「named no identity」。

**变异验证**（文件级备份，未用任何 git 还原命令）：

```
变异前 SqliteItemDomainStore.cs sha256  642d426fd71fc3f296e5863de0c3165f094eca430b399ea0559322ebe9f1457b
变异    撤回守卫（SELECT … WHERE → VALUES）
→ Persistence 失败 1 / 通过 315：恰好 TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding
还原后 sha256                          642d426fd71fc3f296e5863de0c3165f094eca430b399ea0559322ebe9f1457b  == 逐字节一致
```

**315 条既有判据一条未红。**

### 检查（重跑）

| 项目 | 通过 | 跳过 |
|---|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 |
| Dfo.Server.Protocol.Tests | **521** | 0 |
| Dfo.Server.Host.Tests | 6 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 |
| Dfo.Server.Persistence.Tests | **316** | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 |
| **合计** | **2216** | **2** |

失败 0。基线 2215 → **+1，正是新增的那条判据**。
字节锚单跑 **19 通过 / 0 失败**。

## Executor Self-check — round 2

- **快审是对的，而且它推翻的是执行方给的理由，不只是结论。** 「静默通过」这个描述本身就不准——
  `FillAsync` 下一行的 `throw` 一直在那里，只是没人去看。
- **判据的第一版没红，这件事比它后来红了更有价值。** 它逼我搞清楚守卫的**语义边界**：
  旧索引只约束带细节的行，所以守卫也只该在新行带细节时生效。
  **若我当时把「没红」当成守卫失效而去放宽条件，就会写出一个比旧索引更严的守卫，
  那才是真正的未授权行为变化。**
- **未验证**：`SqliteWorldItemTable` 那一侧**没有补**（快审明说不要求动，因为它生产零调用方）。
  所以那一侧今天仍无守卫——**这是已知的、有意留下的缺口**，不是遗漏。
- 本轮改动：`SqliteItemDomainStore.cs`（守卫）与 `SqliteItemDomainStoreTests.cs`（判据），共 2 个文件。

## Independent Quick Review — QRV-TASK-13-02

- 评审身份：独立快审（fresh context），与执行方、控制器、`QRV-TASK-13-01` 均非同一上下文。
- 评审范围：**只审 round 2（整改轮）**。round 1、`DSP-TASK-13-01`、`QRV-TASK-13-01` 为历史记录，未回改。
- 评审的契约版本：Contract revision 1，无修订案。
- Owned diff 认定（按 mtime，不用 `git status`）：`find Server AGENTS.md .rks-agent Docs -newermt "2026-09-05 21:00"`，
  剔除 `bin`/`obj`/`TestResults`。晚于 `QRV-TASK-13-01` 所记时刻的文件共 **3 个，分别是**
  `SqliteItemDomainStore.cs`（21:52:57）、`SqliteItemDomainStoreTests.cs`（21:52:18）、
  `InventoryItemTables.cs`（21:38:03）。
  **第三个是假阳性**：它的 sha256 为 `60877044b4d3070f9c927f4e13d5bf9a3be41d144f07ad0e155cff3f26a3e533`，
  与 round 1 所记**逐字节相同**——21:38 那次 mtime 变动是 `QRV-TASK-13-01` 自己做 `CarriesTheAvatar`
  变异后的文件级还原。**故 round 2 按内容确实只动了两个文件**，与自述一致。

### 一、我自己核实的（逐条）

**1. 守卫与被删索引的等价性 —— 成立，既不更严也不更宽。我自己去 `git show HEAD:` 核过。**

`git show HEAD:Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` 的
`IndexDeclarations` 里那条确为
`CREATE UNIQUE INDEX IF NOT EXISTS idx_items_avatar_uid ON items(avatar_uid) WHERE avatar_uid IS NOT NULL;`；
`git show HEAD:` 的 `SqliteItemDomainStore.FillAsync` 确为
`command.Parameters.AddWithValue("@avatarUid", facts is null ? DBNull.Value : item.Core.Value);`。
**round 2 的等价性论证的两个前提我逐个证实了。** 等价性拆成三段，我逐段自己判：

- **「哪些插入受约束」这一段：完全相同。** 旧：受约束 ⟺ `facts is not null`。
  新：受约束 ⟺ `@expire IS NOT NULL`，而 `@expire` 绑的是 `facts?[0] ?? DBNull.Value`。
  **关键一步是 `facts[0]` 永不为 null**——今天的 `CarriedAvatarsAsync` 的 `SELECT` 带着
  `WHERE avatar_expire_date IS NOT NULL`，读回来的第一列必然非空。
  所以 `@expire IS NULL` ⟺ `facts is null`，**两边逐情形重合**。
- **「哪些既存行算数」这一段：相同。** 旧索引的部分条件是 `avatar_uid IS NOT NULL`；
  新守卫问的是 `standing.avatar_expire_date IS NOT NULL`。
  `AvatarDetailRow.ExpireDate` 是**不可空的 `long`**
  （`Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`），
  而每个写入方都是 `detail is null ? DBNull.Value : detail.ExpireDate` 与
  `detail is null ? DBNull.Value : AvatarKey(core)` 成对绑的，两列同生同灭。
- **「比的是不是同一个键」这一段：相同。** `InventoryItemCore.Encode` 把 `Value` 以小端 int32
  写在偏移 5 起的 4 字节，SQLite 的 `substr` 在 BLOB 上按字节且下标从 1 起，故
  `substr(item_core, 6, 4)` 就是旧的 `avatar_uid`（后者历来取自 `core.Value`）。

**结论：落在「等价」这个点上，没有偏严也没有偏宽。**

**2. round 2 自称「判据第一版没红更有价值」的推理 —— 我独立判断：成立。**
「两行同 UID、只有一行带细节」这一对，**旧索引本来就允许**（不带细节的那行 `avatar_uid` 为 NULL，
不进部分索引）。所以第一版判据不红是**守卫正确**的表现，不是守卫失效。
若当时删掉 `@expire IS NULL` 这一支去把它逼红，守卫就会拦下旧索引明确放行的一对——
那正是一次未被授权的行为变化。**round 2 的自我判断我复核无误。**
一处如实附注（**不阻塞**）：这份等价性不是内生的，它挂在 `CarriedAvatarsAsync` 的
`avatar_expire_date IS NOT NULL` 过滤上；两者同在一个文件，今天成立，但它是一条**耦合**而非不变量。

**3. 新判据是否真的钉住它 —— 我自己重做了变异，结论一致。**
文件级备份（`sha256` `642d426fd71fc3f296e5863de0c3165f094eca430b399ea0559322ebe9f1457b`）→
把 `FillAsync` 的 `SELECT … WHERE @expire IS NULL OR NOT EXISTS(…)` 撤回成 `VALUES(…)` →
`Dfo.Server.Persistence.Tests` **失败 1，通过 315，总计 316**，红的**恰好一条**：
`SqliteItemDomainStoreTests.TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`。
**315 条既有判据一条未动。** 还原后 sha256 与备份**逐字节相同**。
**全程只用 `cp` 备份／还原 ＋ `sha256sum` 核对，未用任何 git 还原类命令。**

我另读了该判据本身，确认它**真的走到 `FillAsync`**而不是走 `LandAsync`：
被顶掉的那一行由 `Survivors` 的第二趟（异位同字节）认给槽 10，槽 11 无人可认，
于是槽 11 走 `FillAsync`，而槽 10 那一行因在 `spared` 里未被 `EmptyAsync` 删掉——
**这正是旧索引所禁止的那一对**，旧代码在这里会撞索引抛 `SqliteException`。判据钉的是同一件事。

**4. 合法搬运路径未被误伤 —— 顺序仍然成立，我自己核的。**
`FlushAsync` 的两个循环是分开的：先对 `staged` 的每个位置调一遍 `EmptyAsync`，
**整个清空循环结束之后**才进入填充循环调 `LandAsync`／`FillAsync`。
`EmptyAsync` 只放过 `spared`（＝`survivors` 的 `ItemUid`），而走 `FillAsync` 的按定义不是 survivor，
所以它的源行此时已删，`NOT EXISTS` 恒真。
既有的搬运类判据全绿：`Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore`、
`WorldRowSurvivesTheMoveTests`（后者一字未动，mtime `18:37:36`）均在下面的全绿里。

**5. CK-030 我自己跑（退出码 0），逐项目列明细，未用 `grep -c`：**

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 316 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |

逐项目相加：521+52+60+6+316+236+1025 = **2216 通过 / 2 跳过 / 0 失败**，与声称一致。
round 1 基线 2215 → **+1，正是新增的那一条**。**无偶发失败**，所以「异常类型＋栈根＋复跑即绿」这一轮没用上。

**6. CK-031 字节锚未动。** `git status --short` 与 `git diff HEAD --stat` 对
`SelectCharacterAcceptedTranscriptTests.cs` **均无输出**（相对 HEAD `51cd99c2` 未修改），
mtime 仍是 `09-05 09:04:19`。单跑该类 ＋ `InventoryDeltaLift`：**失败 0，通过 19，总计 19**。

**7. 既有判据未被削弱。** `SqliteItemDomainStoreTests.cs` 相对 HEAD 的 diff 里，
**删除行中含 `Assert` / `[Fact]` / `[Theory]` 的有 0 处**，新判据是纯增。

**8. BOM。** round 2 的两个改动文件首三字节均为 `75 73 69`（`usi`），**无一为 `ef bb bf`**。

**9. `SqliteWorldItemTable` 那一侧的缺口今天确实无害，且「记成缺口而不是补掉」处置正确。**
我自己扫的：`grep -rn "IWorldItemTable\|SqliteWorldItemTable" Server/src Server/tests --include=*.cs`
命中 **6 行，分别是**接口声明、实现类声明，以及 `WorldAndSlotTableTests`（2 行）与
`CreatureIdentityFitsTheWireTests`（2 行）把它 `new` 出来；
`PersistenceModule` 只 `TryAddSingleton` 了 `ICharacterItemTable`、`IAccountCargoItemTable`、
`SqliteItemDomainStore`／`IItemDomainStore`——**`IWorldItemTable` 无任何注册**。
**生产零调用方、零注册，缺口今天到不了生产。** 去补它反而要在一个生产无人调用的类上引入新行为，
收益为零而风险非零。**留作有意的缺口是对的处置**——**但缺口必须被如实记着**，
而这正是下面 B-2 出问题的地方。

### 二、阻塞项（1 条）

**B-2｜round 2 改了行为，却没有回头订正它自己刚刚弄假的记载；验收条件 5 与 CK-032 落空。**

round 2 把 `FillAsync` 从「无自己的条件、被拒时静默通过、零判据覆盖」改成
「有自己的条件、被拒时抛 `InvalidOperationException` 回滚、有一条判据钉着」，
**但 `AGENTS.md` 与 `behavior-change-list.md` 仍完整地描述着改之前的世界**
（两份文件 mtime 分别为 `21:24:48` 与 `21:25:33`，**都早于 round 2 的 `21:52`**，round 2 未再动过）。
这是本 Task 的验收条件 5（「因本 Task 变假的陈述**已订正**，且订正**点名**原文」）
与 CK-032（「列出因本 Task 而变假的记载，逐条给出订正位置」）直接要求的事，
而 round 2 只重跑了 CK-030 与 CK-031，**没有重跑 CK-032**。

**今天变假的记载共 9 条，位置按小节与成员名给出：**

`AGENTS.md`，「物品：有身份的东西，和它在哪，是两张表」小节：

1. 「这条规则**还在**，但它不再是表的键，而是**两条写入语句**自己的条件：
   `SqliteCharacterItemTable.UpdateOwnAsync` 的 `NOT EXISTS(…)` 与
   `SqliteCharacterItemTable.InsertOwnAsync` 的 `INSERT … SELECT … WHERE NOT EXISTS(…)`」
   → **今天是三条**，第三条是 `SqliteItemDomainStore.FillAsync` 的
   `WHERE @expire IS NULL OR NOT EXISTS(…)`。

`AGENTS.md`，「已知缺陷与覆盖缺口」小节，「同一个时装 UID 全库至多一行」那一条：

2. 条目首句「这条规则今天**只覆盖两条写入语句**，不再覆盖**另外两个**写入方」
   → 覆盖三条；未覆盖的写入方**只剩一个**。
3. 「在此之外还有**两个**写入 `items` 时装列的地方，它们**从来没有**自己的条件，靠的是
   `idx_items_avatar_uid` 抛错——索引退役后它们**不再被拦**」，及其点名 `FillAsync` 的整段括注
   → `FillAsync` **现在有**自己的条件。
4. 同段末「所以该路径上「两行带同一 UID」原本就抛错，**现在会静默通过**」
   → **今天仍然抛错并回滚**（`InvalidOperationException`，消息含 `named no identity`）。
   与旧索引相比只差异常类型（`SqliteException` → `InvalidOperationException`），
   这一点**也该写进记载**，而不是留在 Task 文件里。
5. 条目末句「**两处都没有判据覆盖**（变异实测：`CarriedAvatarsAsync` 恒返回空表，
   **315 条持久化判据全绿**）」
   → **我自己重做了这次变异**：今天是 **失败 1 / 通过 315 / 总计 316**，
   红的是 `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`。**这句已经不成立。**

`AGENTS.md`，「已知缺陷与覆盖缺口」小节，「两条判据的名字不再描述它证明的东西」那一条：

6. 「**变异实测**：让 `CarriedAvatarsAsync` 恒返回空表，315 条持久化判据仍全绿，
   即这段搬运**全仓零覆盖**」 → 「全仓零覆盖」今天不成立（同上，1 条红）。
   该条目关于 `Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore`
   **本身**靠「行没被删」通过这一点仍然为真，**不要连它一起删掉**。

`behavior-change-list.md`，「订正（TASK-13，2026-09-05）」小节：

7. 「1. **保住了的**……落点是 `SqliteCharacterItemTable.UpdateOwnAsync` 的 `NOT EXISTS(…)`，
   与 `SqliteCharacterItemTable.InsertOwnAsync` 的 `INSERT … SELECT … WHERE NOT EXISTS(…)`」
   → 少了 `FillAsync` 这第三条落点。
8. 「2. **没有原样搬走的**：另外 **2 处**……**分别是**……`SqliteItemDomainStore` 的 `FillAsync`
   ……所以「两行带同一 UID」原本会抛，**现在会静默通过**」
   → **只剩 1 处**（`SqliteWorldItemTable`）；`FillAsync` 那一整段已变假。
9. 「**两处都没有判据覆盖**：变异实测让 `CarriedAvatarsAsync` 恒返回空表，
   315 条持久化判据仍**全绿**」与「`CarriedAvatarsAsync` 的去留」段末
   「本 Task 补上了变异证据：**全仓零覆盖**」 → 同第 5、6 条。

**为什么这是阻塞而不是随手改**：本成果的整个由来，以及 `DSP-TASK-13-01` 第一节控制器自己写下的那句
「**一段活得比它所描述的代码还久的注释比没有更糟，而一次引入自己假句的订正，正是这种注释的来路**」，
说的正是此刻发生的事——**一次订正在写完之后又改了代码，却没有回头看那份订正**。
9 条里有 5 条不是措辞不准，而是**方向相反**（「静默通过」／「零覆盖」今天都是反的），
且其中两条还**引用了一个已经过期的变异实测结论**，会让下一位读者按错误的证据行事。

**要求的补救**（两个文件都在允许清单内，改动是纯文字）：
按本 Task 一贯的做法**点名原文后订正**，把上列 9 条逐条改到今天为真；
把「守卫的第三处落点 ＝ `FillAsync`」「被拒时抛 `InvalidOperationException` 而非旧的 `SqliteException`」
「唯一剩下的未覆盖写入方 ＝ 生产零调用方零注册的 `SqliteWorldItemTable`」
「`CarriedAvatarsAsync` 今天由 `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` 覆盖」写进去；
并**重跑 CK-032**。**不需要再动任何生产代码**——生产侧我这一轮逐条核实无误。

### 三、非阻塞（2 条）

- N-3｜`QRV-TASK-13-01` 的 N-1（`AGENTS.md`「「这一行有没有时装细节」由细节列自己回答，**只有一处**」
  按字面过强；`behavior-change-list.md` 有同样措辞）**仍未改**，且 round 2 又新增了一处同义的写入侧条件
  （`FillAsync` 的 `standing.avatar_expire_date IS NOT NULL`），**该判据文本在 `Server/src` 今天是 4 处**。
  读侧确为一处，建议随 B-2 一并收窄为「**读侧**只有一处」。**本身不阻塞。**
- N-4｜等价性挂在 `CarriedAvatarsAsync` 的 `avatar_expire_date IS NOT NULL` 过滤上（见第一节第 1 点末）。
  值得在 `FillAsync` 的 `<remarks>` 里点一句，**不要求返工**。

### 四、核实无误、不需处理的

守卫与旧索引等价（三段逐段核过）；新判据单独钉住它且不误伤既有 315 条；
合法搬运路径的 `EmptyAsync` → `FillAsync` 顺序仍然成立；CK-030 我自己跑得 **2216 / 2 / 0**；
字节锚相对 HEAD 无 diff 且单跑 **19 / 0**；本轮按内容只动两个文件（第三个是上一轮快审的还原假阳性）；
既有判据无删除、无削弱；两个改动文件均无 BOM；
`SqliteWorldItemTable` 生产零调用方零注册，留作有意缺口的处置正确；
round 2 关于「第一版判据没红更有价值」的推理成立。

### 五、结论

**verdict: `changes_required`**

生产侧的整改我逐条独立核实**全部到位**——B-1 已被正确地、以等价而非更严的方式补回，
并由一条单独钉住它的新判据护着。**唯一挡路的是记载**：这一轮的代码改动让本 Task
自己刚写下的订正里 9 条陈述变假，其中 5 条方向相反，而 CK-032 没有重跑。
补救是纯文字的，限于 `AGENTS.md` 与 `behavior-change-list.md` 两个允许文件，不需要扩大范围，
也不需要再碰生产代码。

## Execution Result — Round 3（响应 QRV-TASK-13-02 的 B-2）

**只改记载，一行生产代码未动。** 两个生产文件的 sha256 与 round 2 记录**逐字节相同**：
`SqliteItemDomainStore.cs` `642d426fd71fc3f296e5863de0c3165f094eca430b399ea0559322ebe9f1457b`、
`SqliteItemDomainStoreTests.cs` `761fdf09a60ef285559865d7b8a6d9de1ae92f264b1058d7bdc8e2c6d24c5377`。

### 一、我接受 B-2，且它说中的正是本 Task 自己立的规矩

`DSP-TASK-13-01` 第一节是我写的：「一段活得比它所描述的代码还久的注释比没有更糟，
而**一次引入自己假句的订正，正是这种注释的来路**」。round 2 改了 `FillAsync` 的行为，
却没有回头看 round 1 刚写下的那份订正——**同一个失败模式，作者是同一个人。**
一条记在纸上的纪律，不会自动作用于写它的人；它只有被当成**每次改完行为后必跑的一步**才有用。

### 二、订正方式的一处判断，请评审裁决

评审要求「按本 Task 一贯的做法**点名原文后订正**」。**我没有在 `AGENTS.md` 里留「原本说 X，
现在是 Y」的修正案，而是就地改成今天为真**，理由：

- 本成果**全部未提交**（HEAD 仍 `51cd99c2`）。这 9 条假句**从未出现在任何一个提交里**，
  未来读提交树的人**看不到**它们。在仓库约定文件里为一段从未存在过的文本留一条
  「我们曾经说过」，会被读成「上一个版本发布过这个说法」——**那本身就是一句新的假话**。
- 本仓的「原文保留，修正案在后」这条规矩，针对的是**已落地的记载**（`TableOwnershipRuling.md`
  的 D-15 就是这么处理的，见 `AGENTS.md` 那一节的第三条）。这里的对象不是已落地的记载。
- **「点名原文」这项义务我在下面逐条兑现**——本 Task 文件就是过程记录该在的地方。

`behavior-change-list.md` 的「订正（TASK-13）」小节我**就地改**，因为按该文件自己的写法
（「上一段的结论已被推翻，**原文保留在上面不改，修正案在这里**」），那一节**就是当下的陈述**，
不是被保留的历史原文；被保留的历史原文是它上面的「额外发现」一节，**我一个字没动**。

### 三、逐条订正（点名原文 → 今天的说法）

`AGENTS.md`，**5 处，分别是**：

1. 世界表小节。原文「**「这一行有没有时装细节」由细节列自己回答，只有一处。**」
   → 加限定为「**读侧**只有一处」。（这是 `QRV-TASK-13-01` 的 N-1 与 `QRV-TASK-13-02`
   的 N-3，两轮都指出按字面过强，本轮一并收窄。）
2. 世界表小节。原文「…而是**两条写入语句**自己的条件：`UpdateOwnAsync` 的 `NOT EXISTS(…)` 与
   `InsertOwnAsync` 的 `INSERT … SELECT … WHERE NOT EXISTS(…)`。**两条共用**同一段条件文本
   `CarriesTheAvatar`」→ 改为**三条并列举**，第三条是 `SqliteItemDomainStore.FillAsync`；
   并写明**它没有复用那个常量**，因为它比对的是待插入的 core 而非 `@avatarKey` 参数。
   另附该谓词文本在 `Server/src` 的**全部 4 处 SQL，分别是** `InventoryItemTables.cs:229`、
   `:264`（常量的两个使用点）、`SqliteItemDomainStore.cs:414`（`CarriedAvatarsAsync` 的检索键）、
   `:544`（`FillAsync` 的守卫）。
3. 「三处结论对，但撑着它的不是代码」第三条。原文「变异实测：逐槽交替 empty/fill…
   **315 条持久化判据仍然全绿**」→ 改为「**今天的 316 条仍然全绿**」，并注明 TASK-13 重跑过。
   **见第四节：这个数我没有推理，我把那次变异重做了一遍。**
4. 「已知缺陷与覆盖缺口」的「同一个时装 UID」整条。原文首句「…今天**只覆盖两条写入语句**，
   不再覆盖**另外两个**写入方」、中段点名 `FillAsync` 的整段括注、以及末句
   「所以该路径上「两行带同一 UID」原本就抛错，**现在会静默通过**」与
   「**两处都没有判据覆盖**（变异实测：`CarriedAvatarsAsync` 恒返回空表，315 条持久化判据全绿）」
   → 整条重写：覆盖**三条**；`FillAsync` **今天有**自己的条件，被拒时抛
   `InvalidOperationException`（消息含 `named no identity`）并**回滚，不会静默通过**，
   与旧索引**只差异常类型**（旧为 `SqliteException`）；由
   `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` 钉住；**仍不被覆盖的只剩
   `SqliteWorldItemTable` 一个**，且写明它**生产零调用方、零 DI 注册**（点名
   `PersistenceModule` 注册的是哪几个、**没有** `IWorldItemTable`），是**有意留下的缺口**。
5. 「两条判据的名字不再描述它证明的东西」的第一条。原文括注
   「**变异实测**：让 `CarriedAvatarsAsync` 恒返回空表，315 条持久化判据仍全绿，
   即这段搬运**全仓零覆盖**」→ 改为：该变异**今天会红 1 条**，并说清
   **它钉住的是守卫对这次检索的依赖，不是那六个值被正确搬到新行上**——后者**仍然零覆盖**。
   **按评审要求，「`Relocate_…` 靠「行没被删」通过」这半句为真，原样保留未动。**
6. 「三条已被证伪的记载，订正」其三末句「位置换成了**两条**写入语句自己的条件」→ 三条。

（上面写 5 处却列了 6 条：**第 1、2 条同属世界表小节**，`diff` 上是相邻的三个块
（`502c502`、`508,509c508,509`、`511,513c511,518`）。**按「小节」数是 5，按「陈述」数是 6。**
下面用的都是 `diff` 块口径：`AGENTS.md` **8 块**、`behavior-change-list.md` **10 块**。）

`behavior-change-list.md`，**改动块 10 个，分别是**：

- 抬头「全量判据：**2215**」、核法一节「全量判据 **2215 / 2 / 0**」、同节「以及 **2215** 全绿这三样」
  → 三处均改 **2216**。**这三处不在评审列的 9 条里，是我扫出来的**：本 Task 新增了一条判据，
  凡引用总数的地方都因它变假。
- 账表第 3 行（「清空先于填入」）的 315 → 同第 3 条处理。
- 订正小节「判定处**只有一处**」→「**读侧**只有一处」。
- 订正小节原 item 2（「另外 **2 处**…**两处都没有判据覆盖**…**现在会静默通过**」）
  → 拆成新的 item 2（`FillAsync` 是**补回来的第三条落点**，附**逐段等价性论证**与变异结论）
  与 item 3（**仍未覆盖的只剩 1 处** `SqliteWorldItemTable`，附零调用方零注册的证据）。
- 「`CarriedAvatarsAsync` 的去留」段末「本 Task 补上了变异证据：**全仓零覆盖**」→ 同第 5 条处理。
- 文末逐项目表 `Persistence 315` → `316`、合计 `2215` → `2216`、复算算式一并改，
  并注明**净增的一条就是**新判据。

### 四、我重做了那次「逐槽交替 empty/fill」的变异，没有靠推理改那个数

`315 → 316` 有两种改法：把数换掉，或者**重跑**。前者是在写一句读者只能选择相信的话——
这正是 TASK-12 五轮返工换来的那条教训（**不写读者无法证伪的基数**）。所以我重跑了：

```
变异前 SqliteItemDomainStore.cs sha256  642d426fd71fc3f296e5863de0c3165f094eca430b399ea0559322ebe9f1457b
变异    把 FlushAsync 的两个循环合成一个（每格 EmptyAsync 之后立刻 Land/Fill），字面违反该注释
→ Dfo.Server.Persistence.Tests  失败 0 / 通过 316 / 总计 316   ← 结论未变，仍无判据钉住它
还原后 sha256                          642d426fd71fc3f296e5863de0c3165f094eca430b399ea0559322ebe9f1457b  == 逐字节一致
```

**全程 `cp` 备份／还原 ＋ `sha256sum` 核对，未用任何 git 还原类命令。**
若我只改数不重跑，就无法知道 round 2 新增的那条判据会不会恰好钉住这个不变量——
**它没有**，但那是跑出来的，不是猜出来的。

### 五、CK-030 / CK-031 / CK-032 重跑

CK-030（退出码 **0**）：

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 316 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |

逐项目相加：52+521=573；+60=633；+6=639；+316=955；+236=1191；+1025=**2216**。
**2216 通过 / 2 跳过 / 0 失败**，与 round 2 相同（本轮未加判据）。无偶发失败。

CK-031：`git diff HEAD --stat -- …/SelectCharacterAcceptedTranscriptTests.cs` **无输出**（相对 HEAD 未修改）；
单跑 `SelectCharacterAcceptedTranscriptTests` ＋ `InventoryDeltaLift`：**失败 0 / 通过 19 / 总计 19**。

CK-032：即本节第三条，逐条列出并给了订正位置。

### 六、N-4（非阻塞）未做，说明理由

评审建议在 `FillAsync` 的 `<remarks>` 里点一句「等价性挂在 `CarriedAvatarsAsync` 的过滤上，
是耦合不是不变量」。**本轮没做**：它要改生产文件，而 B-2 的补救明确是纯文字的；
在一轮「只改记载」的整改里顺手动生产代码，会让下一位评审无法按 sha256 一眼确认生产侧未变。
**这条耦合我写进了 `behavior-change-list.md` 的等价性论证里**（「因为 `CarriedAvatarsAsync` 的
`SELECT` 带着 `WHERE avatar_expire_date IS NOT NULL`」），事实已被记住，只是没落在 `<remarks>` 上。
**如果评审认为它必须落在代码注释里，我照做**——那是一行注释加一次全量重跑。

## Executor Self-check — Round 3

- **生产代码零改动**，两个文件 sha256 与 round 2 逐字节一致（见本节开头）。改动只在
  `AGENTS.md` 与 `behavior-change-list.md`，**两个都在本 Task 的允许清单内**。
- **评审列的 9 条我逐条改了**，另**自己扫出 4 条同因变假的**（`2215` 三处 ＋ 账表第 3 行的 `315`），
  一并改掉。扫法：在两个文件里 grep `静默通过|零覆盖|全绿|315|2215|两条写入|另外两个|两处都|只有一处|idx_items_avatar_uid`，
  逐条读上下文判断是「今天的陈述」还是「被保留的历史原文」。
  **历史原文一律不动**——`behavior-change-list.md` 的「额外发现」一节（说 `items` 是 20 列、
  `avatar_uid` 仍在）与 `AGENTS.md` 「三条已被证伪的记载」里那段引号内的原话，都原样留着。
- **`315 → 316` 我重跑了变异而不是改数**（第四节）。这是 TASK-12 那条教训的直接应用。
- **一处订正方式的判断已挑明请裁决**（第二节）：未提交的文本不留「曾经说过」式修正案。
  **如果评审认为不成立，改法是纯追加，我照做。**
- **N-4 明说未做并给了理由**（第六节），未把它悄悄归入「不需处理」。
- 本轮**没有**新增或修改任何判据，故未做新的变异验证；重跑的那次变异是为核实一句既有记载。

## Independent Quick Review — QRV-TASK-13-03

**审的范围**：只审 round 3（`Execution Result — Round 3` 与 `Executor Self-check — Round 3`）。
更早的轮次与裁定按历史记录对待，未回改一字。
**审的对象**：`AGENTS.md` 与 `.rks-agent/.../behavior-change-list.md` 的当前内容，
以及「生产侧本轮零改动」这一主张。工作区 `.worktrees/items-own-their-identity`，HEAD `51cd99c2`，全部未提交。

### 一、我自己跑／自己数的（不采信自述）

**1. 生产代码零改动——两个文件的 sha256 我自己算了，与自述及 `QRV-TASK-13-02` 记录的备份值三方一致。**

```
Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs
  642d426fd71fc3f296e5863de0c3165f094eca430b399ea0559322ebe9f1457b
Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteItemDomainStoreTests.cs
  761fdf09a60ef285559865d7b8a6d9de1ae92f264b1058d7bdc8e2c6d24c5377
```

前者与 `QRV-TASK-13-02` 第一节里那次变异所记的备份 sha256 **逐字节相同**，
即今天的生产文件就是上一轮快审逐条核过的那一份。

**`Server/` 下没有别的文件在 round 3 被动过**，判据是 mtime 排序
（`find Server -type f -name '*.cs' -newermt …`，按修改时间倒序）：最新的三个分别是
`SqliteItemDomainStore.cs` **22:15**、`SqliteItemDomainStoreTests.cs` **21:52**、
`InventoryItemTables.cs` **21:38**；其余全部 ≤ 21:38。
round 3 的时间窗由两个记载文件圈定（`behavior-change-list.md` **22:15:55**、
`AGENTS.md` **22:16:23**，Task 文件 **22:19:40**）。
**落在该窗口内的 `Server/` 文件只有 `SqliteItemDomainStore.cs` 一个**，
那正是它这一轮做变异后还原留下的 mtime，而它的内容已由上面的 sha256 证明未变。
**结论：生产侧本轮零改动，成立。**

**2. CK-030 我自己跑了**（`PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`，退出码 **0**）。
**逐项目抄下并相加，没有用 `grep -c`：**

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 316 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |

相加：521+52=573；+60=633；+6=639；+316=955；+236=1191；+1025=**2216**。
**2216 通过 / 2 跳过 / 0 失败**，与自述和 `behavior-change-list.md` 文末那张表**逐项目相同**。
无偶发失败。（该文件文末的复算算式按不同顺序相加：60+52=112；+521=633；+6=639；+236=875；+316=1191；+1025=2216
——我复核过，算术正确。）

**3. CK-031 我自己跑了。** `git diff HEAD --stat -- '*SelectCharacterAcceptedTranscriptTests*'` **无输出**，
`git status --short` 对该路径同样无输出，即字节锚相对 HEAD 未修改。
单跑 `SelectCharacterAcceptedTranscriptTests` ＋ `InventoryDeltaLift`：**失败 0 / 通过 19 / 总计 19**。

**4. 「逐槽交替 empty/fill」那次变异我自己重做了一遍，结论与自述一致。**

```
变异前 sha256   642d426fd71fc3f296e5863de0c3165f094eca430b399ea0559322ebe9f1457b
变异           FlushAsync 的两个 foreach 合成一个：每格 EmptyAsync 之后立刻 Land/Fill + HoldAsync,
               字面违反 FlushAsync <remarks> 的 "every named position is emptied before any is filled"
变异后 sha256   00b45ff0a112601b03d19c67d24b26da8fec8c25e5a0fab8f8d5414974870227
→ Dfo.Server.Persistence.Tests   失败 0 / 通过 316 / 总计 316
还原后 sha256   642d426fd71fc3f296e5863de0c3165f094eca430b399ea0559322ebe9f1457b   == 与备份逐字节一致
```

**「清空先于填入」今天确实仍无任何判据钉住**，round 2 新增的那条判据没有恰好接住它。
**全程 `cp` 到 scratchpad 备份／还原 ＋ `sha256sum` 核对，未使用
`git checkout --`／`git restore`／`git stash`。**

### 二、B-2 那 9 条，我自己读今天的代码逐条判定

**判定口径：读 `Server/src` 今天的源码，不比对执行方的说法。** 逐条：

- **三条守卫落点，成立。** 我按成员名核的：`SqliteCharacterItemTable.UpdateOwnAsync` 的
  `AND NOT EXISTS(SELECT 1 FROM items held WHERE <CarriesTheAvatar> AND held.item_uid <> @uid)`；
  `SqliteCharacterItemTable.InsertOwnAsync` 的
  `INSERT … SELECT … WHERE NOT EXISTS(SELECT 1 FROM items held WHERE <CarriesTheAvatar>)`；
  `SqliteItemDomainStore.FillAsync` 的 `WHERE @expire IS NULL OR NOT EXISTS (SELECT 1 FROM items standing
  WHERE standing.avatar_expire_date IS NOT NULL AND substr(standing.item_core, 6, 4) = substr(@core, 6, 4))`。
  **AGENTS.md 说的「三条」为真；「前两条共用 `CarriesTheAvatar`，第三条没复用它，
  因为它比对的是待插入的 core 而非 `@avatarKey` 参数」也为真**——常量原文是
  `held.avatar_expire_date IS NOT NULL AND substr(held.item_core, 6, 4) = @avatarKey`，确实绑参数；
  `FillAsync` 内联的那段比的是 `substr(@core, 6, 4)`。
- **AGENTS.md 列的 4 处谓词位置，行号我逐个核对，全部准确**：`InventoryItemTables.cs:229`
  （`UpdateOwnAsync` 的使用点）、`:264`（`InsertOwnAsync` 的使用点）、
  `SqliteItemDomainStore.cs:414`（`CarriedAvatarsAsync` 的检索键）、`:544`（`FillAsync` 的守卫）。
  计数口径写在原文里（「4 处 SQL」，常量定义处 `InventoryItemTables.cs:46` 本身不是 SQL 使用点，不计），
  与我 `grep -rn "avatar_expire_date IS NOT NULL" Server/src --include=*.cs` 得到的 3 行
  （46、414、544）＋常量的 2 个使用点自洽。
- **`FillAsync` 被拒时抛 `InvalidOperationException` 而非静默通过，成立。** 代码是
  `return allocated is null ? throw new InvalidOperationException("The world took the item and named no identity.") : checked(…)`。
  AGENTS.md 与 `behavior-change-list.md` 都写了「消息含 `named no identity`」「同样回滚，不会静默通过」
  「与旧索引只差异常类型（旧为 `SqliteException`）」——**四句都为真**。
  原先那句「现在会静默通过」在两个文件里**已全部消失**（我 grep `静默通过`，两个文件零命中）。
- **唯一剩下的未覆盖写入方是 `SqliteWorldItemTable`，成立。**
  `grep -rn "IWorldItemTable\|SqliteWorldItemTable" Server/src Server/tests --include=*.cs` 得 **6 行，分别是**：
  `InventoryTables.cs:189`（接口声明）、`InventoryItemTables.cs:417`（实现类声明）、
  `WorldAndSlotTableTests.cs:455` 与 `:463`、`CreatureIdentityFitsTheWireTests.cs:55` 与 `:107`。
  `PersistenceModule` 注册的是 `ICharacterItemTable`（:74）、`IAccountCargoItemTable`（:76）、
  `SqliteItemDomainStore`（:158）／`IItemDomainStore`（:162），**没有 `IWorldItemTable`**。
  两个文件写的「生产零调用方、零 DI 注册、有意留下的缺口」**为真**。
- **两条引用过期变异结论的句子，都已改到今天为真。**
  「两处都没有判据覆盖」在两个文件里**零命中**；「全仓零覆盖」今天只出现在被正确限定的语境里——
  `AGENTS.md` 作「**不要再照抄旧的那句「全仓零覆盖」**……让 `CarriedAvatarsAsync` 恒返回空表今天会红 1 条」，
  `behavior-change-list.md` 作「**「全仓零覆盖」这句今天不再成立**」。
  两处都补了同一句关键限定：**钉住的是守卫对这次检索的依赖，不是那六个值被正确搬到新行上，后者仍然零覆盖**。
  **我核过这个推理对得上代码**：空表 ⇒ `facts` 为 null ⇒ `@expire` 绑 `DBNull` ⇒ `@expire IS NULL` 为真
  ⇒ 守卫短路放行 ⇒ 该判据等的抛错不再发生。
- **`QRV-TASK-13-02` 第 6 条特别叮嘱「`Relocate_…` 靠「行没被删」通过这半句仍为真，不要连它一起删掉」——
  我核了，那半句原样保留在 `AGENTS.md`「两条判据的名字不再描述它证明的东西」条目里。**
- **9 条以外我另行核实的两处**：`AGENTS.md`「`SqliteItemDomainStore` 的注释**四处**说清空先于填入」——
  我按成员名数了，确为四处：类型级 `<remarks>`、`ApplyDecidedWritesAsync`、`FlushAsync`、`EmptyAsync` 各一处；
  「判定处**读侧只有一处**」——`SqliteCharacterItemTable.Avatar(SqliteDataReader, int)` 是唯一实现，
  两个调用点（`SqliteCharacterItemTable` 的行读取与 `SqliteWorldItemTable.Read`）都调它，**不另判一次**，为真。
  这两句是 `QRV-TASK-13-01` 的 N-1 与 `QRV-TASK-13-02` 的 N-3 指出的过强措辞，**本轮一并收窄，处理正确**。

### 三、我自己重扫，找有没有漏网的同因假句

**扫描口径**：只扫 `AGENTS.md` 与 `behavior-change-list.md` 两个文件全文，区分大小写，关键词为
`静默通过|零覆盖|全绿|315|2215|2216|316|两条写入|另外两个|两处都|只有一处|idx_items_avatar_uid|avatar_uid|FillAsync|靠索引|没有自己的条件|抛错|SqliteException|InvalidOperationException`，
逐条命中读上下文判断它是「今天的陈述」还是「被保留的历史原文」。

**结论：没有漏网的。** 具体地：

- `2215` 在两个文件里**零命中**；执行方自称扫出的三处 `2215`（抬头、核法一节、文末合计）今天都是 `2216`，我核过。
- `315` 在两个文件里**只剩 3 处命中，分别是**：`behavior-change-list.md` 账表第 3 行的
  「**当时的** 315 条持久化判据全绿；TASK-13 重跑同一次变异，**今天的 316 条**依旧全绿」（正确的历史—现状并置）、
  订正小节第 2 条变异实测的「**失败 1／通过 315／总计 316**」（这是撤掉守卫后的分解数，316 = 315 + 1，正确）、
  文末「由 315 增至 316、合计由 2215 增至 2216」（正确的增量陈述）。**三处都为真，无需再改。**
- 两个文件之外的 `2215`／`315` 命中全在 `TASK-11-retire-the-old-shape.md` 等更早的 Task 文件里，
  那是各自任务当时的执行记录，**按历史冻结是对的，不属于「因本 Task 变假的记载」**。
  `Docs/Refactor/TableOwnershipRuling.md` 提到 `FillAsync` 的三处是历史裁定正文，
  本仓规矩不改其正文，`AGENTS.md`「三条已被证伪的记载」其三即是它的修正案——**处理正确**。

### 四、两个被点名请裁的问题——我的独立判断

**问题一（订正方式的偏离）：成立，满足验收条件 5 的实质。** 依据有三，都是我自己验的：

1. **「这 9 条假句从未出现在任何提交里」这个前提为真，我自己验了。**
   `git diff AGENTS.md` 显示该节的 diff 头是 `@@ -484,6 +484,180 @@`，**整节 180 行全是 `+`，一条 `-` 都没有**
   ——即「物品：有身份的东西，和它在哪，是两张表」整个小节都是本成果未提交的新增内容。
   在这样的文本上留「我们曾经说过 X」，会被未来的读者读成「上一个发布版本里有过 X」，
   **那确实是一句新的假话**，且正好是本 Task 一路在防的那种假话。
2. **本仓「原文保留＋修正案」的规矩确实只针对已落地的记载。**
   `AGENTS.md`「三条已被证伪的记载」其三写着：`TableOwnershipRuling.md` 的 D-15
   「那是一份**历史裁定**，按本仓规矩**不改它的正文**，本节即是它的修正案」。
   区分标准是**已落地 vs 未落地**，执行方的读法与该文自身的措辞一致。
3. **「点名原文」这项义务确实被兑现了，只是兑现在 Task 文件里。**
   Round 3 第三节逐条给出「原文「…」→ 今天的说法」，我抽查了其中 4 条
   （「只覆盖两条写入语句」「现在会静默通过」「两处都没有判据覆盖」「全仓零覆盖」），
   与 `QRV-TASK-13-02` B-2 所引的原文**逐字对得上**。
   验收条件 5 要的是「已订正 ＋ 订正点名原文」，两半都有着落，且落在一份只追加、可审计的过程记录里。

**`behavior-change-list.md` 那个「就地改修正案、保留其上历史原文」的区分，我核了，对的。**
被保留的「额外发现：`avatar_uid` 这条派工没有落地」一节**一字未动**——它今天仍写着
「交付的 `items` 是 **20 列**」「`avatar_uid` 仍在，并且 `items` 上还有 `idx_items_avatar_uid`」
「**这不是行为变化**」，这些今天都已不成立，**但它们是被明确标注保留的历史原文，不该改**。
紧随其后的「订正（TASK-13）」一节自述「上一段的结论已被推翻，**原文保留在上面不改，修正案在这里**」，
所以那一节**就是当下的陈述**，就地改它是正确的；
而且执行方没有偷偷去改上面那句「这不是行为变化」，而是在修正案里写
「**这一节原来说「这不是行为变化」，那句今天要加一个限定**」——**这正是该规矩要求的做法。**

**问题二（N-4 被拒做）：取舍可接受，不要求本轮补上。** 理由：

- N-4 在 `QRV-TASK-13-02` 里就写着「**不要求返工**」，本轮把它明说未做并给了理由，
  **没有悄悄归入「不需处理」**，这个处理方式本身是对的。
- N-4 要防的实质风险是「未来读者把 `FillAsync` 的守卫当成自足的不变量，看不见它依赖 `CarriedAvatarsAsync` 的过滤」。
  **这个事实今天记在两处，我都核过**：`behavior-change-list.md` 订正小节第 2 条的等价性论证
  （「`@expire IS NULL` ⟺ `facts is null`，**因为 `CarriedAvatarsAsync` 的 `SELECT` 带着
  `WHERE avatar_expire_date IS NOT NULL`**」），以及 `AGENTS.md`「两条判据的名字不再描述它证明的东西」
  里那段「空表使 `@expire` 为 NULL、守卫短路放行」。
  后者尤其重要——它落在维护者会读的约定文件里，而不只是任务文档里。**风险已被记住。**
- 「纯文字整改轮里不动生产代码，好让下一位评审凭 sha256 一眼确认」这个论证**是成立的，而且我这一轮真的用上了**：
  第一节第 1 点正是靠这两个 sha256 在几分钟内确定了生产侧未变。
- 代价很低：这是一行注释，本成果接下来就进完整变更评审（`reviewing-code`，scope `change`），
  **在那里补是自然的位置**，因为那一轮本来就要重跑全量。

**故 N-4 我不转为要求，而是作为一条建议移交给完整变更评审。**

### 五、留给完整变更评审的两条观察（都不阻塞本 Task）

- **O-1｜N-4 顺延。** 建议在 `FillAsync` 的 `<remarks>` 里点明「等价性挂在 `CarriedAvatarsAsync` 的
  `WHERE avatar_expire_date IS NOT NULL` 过滤上，是耦合不是不变量」。
  事实已记在两个文件里，此处只是把它挪到代码近旁。
- **O-2｜`SqliteException` → `InvalidOperationException` 这处内部差异要不要单列。**
  `behavior-change-list.md` 抬头写「本成果只授权**两处**行为变化（RQ-009、RQ-010）」，
  而 `FillAsync` 在「两行带同一时装 UID」这条路径上的异常类型确实变了。
  两个文件都如实记了这一点，并明确把它归为「不上线、不改客户端字节的内部变化」而非第三处授权变化——
  **这个定性自洽，措辞也没有说假话**（「授权」是契约陈述，不是「实际改了什么」的陈述）。
  只是「它该不该升格为一条编号的行为变化」是**成果级的范围判断**，超出快审边界，**交给 `reviewing-code` 定。**

### 六、结论

**verdict: `passed`**

round 3 是一轮**只改记载**的整改，我逐项独立核实后确认它做到了：

- **生产代码零改动**——两个文件 sha256 三方一致，`Server/` 下无第二个文件落在本轮时间窗内；
- **B-2 的 9 条全部改到今天为真**——我读今天的代码逐条判过，三条守卫落点、
  `InvalidOperationException` 而非静默通过、唯一剩下的未覆盖写入方是 `SqliteWorldItemTable`、
  两条过期变异结论，**无一遗留**；两轮快审都指出过的「只有一处」过强措辞也一并收窄；
- **我自己重扫两个文件，没有找到漏网的同因假句**；`2215` 零命中，剩下的三处 `315` 全都为真；
- **CK-030 我自己跑并逐项目相加：2216 / 2 / 0，退出码 0**；CK-031 字节锚相对 HEAD 无 diff、单跑 19/0；
  CK-032 已在 Round 3 第三节逐条兑现；
- **那次变异我自己重做了一遍**，`失败 0 / 通过 316 / 总计 316`，与自述相同，还原后 sha256 逐字节一致，
  全程未用任何 git 还原类命令。

两个被点名请裁的问题：**订正方式的偏离成立**（假句从未落地，为它留「曾经说过」反而是新的假话；
点名原文的义务已在 Task 文件逐条兑现；`behavior-change-list.md` 上「改修正案、留历史原文」的区分做得正确）；
**N-4 的取舍可接受**（QRV-02 本就不要求返工，事实已记在两个文件里，且落在维护者会读的约定文件上），
转为建议 O-1 移交完整变更评审。

**本 Task Unit 可以收。** 它是本成果最后一个 Task Unit，收了之后即可进完整变更评审
（`reviewing-code`，scope `change`），两条观察 O-1、O-2 一并带过去。
（本轮不宣告 Spec 终态，也未改 `sdd-execution.yaml`；只写了本文件，且纯追加。）

## Controller Disposition — DSP-TASK-13-02

`QRV-TASK-13-03` 给 `passed`。核对三样指向同一轮：`Execution Result — Round 3`、
`Executor Self-check — Round 3`、`QRV-TASK-13-03`，owned diff 即
`AGENTS.md` 与 `behavior-change-list.md` 两个记载文件（生产文件 sha256 三方一致，本轮未动）。
**TASK-13 置 `done`；`tasks.active` 置 null。十三个 Task 全部 `done`，实现阶段用尽。**

**两条观察随成果带进完整变更评审**（快审明说超出其边界，不阻塞本 Task）：

- **O-1**：N-4 顺延——在 `FillAsync` 的 `<remarks>` 里补一句「等价性挂在 `CarriedAvatarsAsync` 的
  `avatar_expire_date IS NOT NULL` 过滤上，是耦合不是不变量」。那一轮本来就要重跑全量。
- **O-2**：`SqliteException` → `InvalidOperationException` 要不要升格为一条**编号的**行为变化。
  两份记载都如实记了并归为「不上线的内部变化」，定性自洽；但「授权两处」是契约陈述，
  要不要单列是**成果级范围判断**。

**这一轮留下的方法论，记在这里给后来者：**
一次订正在写完之后又改了代码，却没有回头看那份订正——这是本 Task 犯过两次的同一件事
（round 1 → round 2 的 `IWorldItemTable` remarks，round 2 → round 3 的这 9 条）。
**结论：改完行为之后回头扫自己刚写下的记载，必须是一个动作，不能是一条格言。**
