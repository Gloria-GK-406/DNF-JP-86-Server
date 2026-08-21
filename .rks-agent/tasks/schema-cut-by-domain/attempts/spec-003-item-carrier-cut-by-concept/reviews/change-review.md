# Change Review — spec-003 物品承载按概念重切

被评审的完整改动：基线 `5aba3a9dcea2b302797eb33097ae923a7a824a45` 之上的未提交工作区，24 个已跟踪文件（791 增 716 删）加两个新测试文件。
验收依据：本 attempt 的 `TaskContract.md`、`spec.md`、`plan.md`，以及设计权威 `Docs/Refactor/TableOwnershipRuling.md`（本次一并改动，改动本身也在评审范围内）。

评审在一个独立的新上下文中进行，只拿到改动、验收边界与代码。全量套件由评审自己重跑核实：**1092 通过 / 2 跳过 / 0 失败**，与实施者的报告一致。`ON CONFLICT` 的冲突目标语义与称号册的删除后果各以一次独立的 SQLite 复现验证，不靠推断。

## 判定

`changes_required`，两条 A 级。

## A 级发现

### A-1 一次脏物品租约保存会删光该角色的称号册

`SqliteCharacterItemTable.DeleteOwnExceptAsync`（`Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs:99-116`）合并后是：

```sql
DELETE FROM items WHERE owner_scope='character' AND owner_id=@cid
  AND item_uid NOT IN (...)
```

**没有 `list_type` 约束**。而称号册的行现在就住在 `items` 里，`owner_scope='character'`、`owner_id` 等于同一个角色、`list_type` 为 19..23。

调用它的是 `InventoryEntryAssembly.SaveDirtyAsync`（`Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs:118-122`），`keep` 取自租约的容器：

```csharp
var present = lease.Inventory.Containers
    .Where(value => value.ContainerType != InventoryContainerType.AccountCargo)
    .SelectMany(value => value.Items).Where(value => value.StorageEntryId > 0)
    .Select(value => value.StorageEntryId).Distinct().ToArray();
```

租约里**永远不可能有称号册的行**：`ParseContainerType`（同文件 `:518-529`）只认 0/1/2/3/7（以及仓库的 12），`CharacterContainerOrder`（`InventoryEntryServices.cs:38-45`）就是这五个容器，而 `ReadByCharacterAsync` 又用 `list_type NOT BETWEEN 19 AND 23` 把称号册排除在读取之外。两头对上：称号册的 `item_uid` 不会出现在 `present` 里，于是每一次保存都把它整片删掉。

失败场景与后果，逐步可复现：

1. 角色 22 有背包物品（`list_type=0, slot=5`）与称号册一条（`list_type=19, slot=1`）；
2. 该角色的物品租约被标脏并保存；
3. `present = [背包那一行]`，`DELETE ... item_uid NOT IN (背包那一行)` 把 `list_type=19` 那行删掉。

评审用一份与 `CharacterDatabaseSchema` 逐字相同的 `items` 声明复现过这一步：`before: [(1,0,5),(2,19,1)] → after: [(1,0,5)]`。

后果落在下发帧上：称号册经 `CharacterEntryAssembly` → `SelectedCharacterStateMessageFactory.cs:63` 进入选人下发，删空之后该角色下一次选人收到的是五个空类目，而不是它存着的那一条。这正面违反 SC-8 / RQ-011「对外协议行为一字不变」。

**改动前这件事结构上不可能**：那条 `DELETE` 指名的是 `character_new_items`，而称号册在 `character_new_titlebook` 里，且该表在整个 `Server/src` 下**只有一条读取语句、没有任何写入**（`git grep character_new_titlebook 5aba3a9 -- Server/src` 只命中声明、可补列清单与一条 `SELECT`）。合并把这层结构性保护拿掉了，而没有在删除语句上补回等价的边界。契约「源表的每一条约束在其列的落点上存续」在这里失守的不是一条 `UNIQUE`，是分表本身提供的隔离。

缓解事实（不改变定级）：`TryMarkDirtyExact` 今天在 `Server/src` 下没有任何调用方，所以实机暂时走不到 `SaveDirtyAsync`。但它是已注册、已被 `SqliteEntryInventoryMutationRepositoryTests` 直接驱动的持久化契约，缺陷是既成的，只等一个调用方。

整改方向：把删除的作用域收回租约拥有的那几个容器（`AND list_type IN (0,1,2,3,7)`，或至少 `AND list_type NOT BETWEEN 19 AND 23`，并把理由写在语句旁边——它现在是**唯一**一处需要知道称号册容器号的写入语句），并补一条「持有称号册的角色保存脏租约后称号册仍在」的用例。

### A-2 SC-2 的必需证据没有产出，计划的 T-002 第 4 步被静默跳过

契约 SC-2 的 Required evidence 逐项写明四件事要有用例：`category` 0..4 写入后以 `list_type` 19..23 存在；按 `category` 读回得到原值；同一角色的背包 0 号槽与称号册 0 类 0 号槽共存而不违反 `UNIQUE`；角色背包的读取排除 19..23。计划 T-002 第 4 步一字不差地要求了同样四条断言。

实际产出的 `ItemCarrierShapeTests.cs`（27 条断言）**一条称号册变换的断言都没有**——全部是列集、约束文本与索引。清点全仓，称号册在测试里只出现两次，都是同一行：

- `Server/tests/Dfo.Server.Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs:126`
- `Server/tests/Dfo.Server.Persistence.Tests/Characters/SqliteCharacterEntryStateRepositoryTests.cs:81`

两处都是 `VALUES('character',22,19,1,@core)`，走的都是 `CharacterSide`（角色本体载入），**不碰物品载入**。于是：

- 逆变换只有 `19 → 0` 一例被覆盖，20..23 无覆盖；
- **没有任何一个角色同时拥有物品行与称号册行**，共存那一条无覆盖；
- `SqliteCharacterItemTable.ReadByCharacterAsync`（`InventoryItemTables.cs:40`）那句 `AND list_type NOT BETWEEN 19 AND 23` 无覆盖。

可证伪性检验：把 `:40` 那一行整句删掉，全量套件**仍然全绿**——因为没有一份夹具让称号册的行走到物品载入里去。而实机上那个角色一进游戏就撞 `ParseContainerType`，抛 `Inventory container type 19 is not an entry container`，选人直接失败。一条挡在选人路径上的过滤条件，删掉不会红。

同一个空洞正是 A-1 漏过去的原因：任何一份「角色同时持有背包物品与称号册」的夹具，只要跑一次脏租约保存，A-1 立刻现形。这与 spec-002 的教训是同一条——唯一的真缺陷出现在唯一没有证据覆盖的那条路上。

实施者的 `implementation-state.md` 把 T-002 记为「完成，27 条断言，列清单取自裁定的逐表列表」，没有提到第 4 步，也没有把它记进「计划内的有界调整」。这是无记录的计划偏离。

整改方向：补一份称号册变换的用例——0..4 五个类目各存各读；一个角色同时持背包 0 号槽与称号册 0 类 0 号槽，两行共存且各自读回；该角色的物品载入只得到背包那一件。三条断言同时也把 A-1 变成红灯。

## B 级发现（记录，未改）

1. **裁定对 D-15 的强度自述不准确。** `Docs/Refactor/TableOwnershipRuling.md:741` 写「This reproduces today's strength and not one bit more」，但同一节上面自己说过的「strictly stronger」在**同一角色**这一半上仍然成立：今天两条物品行携带同一时装 UID 是允许的（两行共用一条 `PRIMARY KEY(item_uid)` 的详情行），合并后第二次写入被 `idx_items_avatar_uid` 或 `ON CONFLICT ... DO NOTHING` 拒绝。普查只证明了**跨角色**那一半有用例覆盖，没有证明同角色那一半不可达。收紧本身是合理的（一件时装两份实例本来就是数据破洞），但裁定的措辞把「恢复」说成了「等价」。实机不可构造：一次租约里两件 kind 8 且核心块 5..8 相同的物品，客户端不产生。
2. **`CarriedAvatarsAsync` 跨归属取事实。** `SqliteItemStore.cs:207-212` 的 `SELECT ... FROM items WHERE avatar_uid = @uid` **没有 owner 条件**：角色乙的 item store 若写入一件核心块里带着角色甲时装 UID 的 kind 8 物品，会把甲的时装事实读出来准备写到乙的行上。今天救场的只有 `idx_items_avatar_uid` ——插入随即撞唯一索引抛 `SqliteException`，事务整体回滚，所以没有实际泄漏。但「一个角色写不到另一个角色的时装列」这条规则在这条路径上是靠一个索引兜着，而不是靠语句自己说清楚；索引若哪天被放宽或这条插入被加上 `ON CONFLICT`，泄漏就成立。加一个 `AND owner_scope='character' AND owner_id=@owner` 就与 `UpdateOwnAsync` 的做法一致了。
3. **`ReadCharacterItemsAsync` 的所有权拒绝已成死代码。** `InventoryEntryAssembly.cs:190-194` 仍在检查 `ownerType != Character || row.OwnerId != characterId.Value`，而读取语句已经把两者都写进 `WHERE`。取代它的新用例 `LoadAsync_LeavesOutTheItemsOfEveryOwnerButTheOneAskedFor` 守的是过滤本身，评审判定这次取代**等价且更强**（旧用例证的是一个不可构造的状态；新用例证的是过滤真的排除了别人的物品），但那段 `throw` 现在永远到不了。
4. **仓库侧的 `RequireUniqueSlots` 失去了唯一的覆盖。** `InventoryEntryAssembly.cs:89` 的 `RequireUniqueSlots(items, "account cargo")` 与 `InventoryEntryServices.ValidateItems` 里对应的重复槽检查，此前由 `LoadAccountAsync_RejectsInvalidStoredAccountCargo("duplicate")` 覆盖，该例被删。评审核实其理由成立：`idx_items_account_slot` 恢复后重复行写不进去，两处检查结构上不可达，取代用例 `TheCargoRefusesASecondItemInASlotItAlreadyHolds` 断言的正是这一点，覆盖无净减少。记录的是两段守卫现在没有用例、也不可能有用例这一状态。
5. **角色侧的 `duplicate` 用例与仓库侧处置不一致。** `LoadAsync_RejectsInvalidStoredCharacterItems("duplicate")` 保留了，但它跑在 `SeedSchemaAsync` 手搭的 `items` 上——那份声明没有 `UNIQUE(owner_scope, owner_id, list_type, slot_index)`、没有两条 `CHECK`。也就是说角色侧的重复同样写不进真库，处置却与仓库侧相反（一个留用例，一个换用例）。这是继承自改动前的既有做法，不是本次引入。
6. **`Avatar(reader, ordinal)` 用 `avatar_uid` 单列判在场。** `InventoryItemTables.cs:249-258`：`avatar_uid` 非空即认为七列都在，随后 `(byte[])reader.GetValue(ordinal + 3)` 直取镶嵌槽。七列之间没有任何 `CHECK` 把它们绑在一起，所以一行 `avatar_uid` 有值而 `avatar_jewel_socket` 为 NULL 会抛 `InvalidCastException` 而不是 `InvalidDataException`。生产写入路径（`BindAvatar`、`FillAsync`）都是七列全写或全 NULL，构造不出来；直接播种可以。
7. **`SqliteItemStore` 的类注释过时。** `SqliteItemStore.cs:326-331`：「Account cargo is refused along with the rest: ... its rows live in another table keyed by account」——仓库的行现在就在 `items` 里，只是 `owner_scope` 不同。拒绝仍然正确，理由已经不是这个。
8. **抽象层仍指名一张不存在的表。** `Server/src/Dfo.Server.Abstractions/Persistence/CharacterEntryTables.cs:49` 与 `:197` 的注释仍写 `character_new_titlebook`。`Docs/GM工具_新背包表结构与ItemCore语义.md` 与 `Docs/新版背包架构业务接入规范.md` 同样仍按旧表名描述。契约未要求同步文档。
9. **裁定里两处小瑕。** `TableOwnershipRuling.md:878` 写「One index is added rather than carried」，随后列了两个；`:882` 与 `:962` 的 D-15 锚点写成 `#d-15--the-avatar-detail-s-primary-key-...`，而标题的实际 slug 是 `...avatar-details-primary-key...`（同页 D-16 的锚点 `#d-16--the-cargos-slot-uniqueness-is-restored` 写法是对的），两处链接跳不到。

## 评审核实无误的部分

- **九张源表的每一条约束逐条追到落点，全部存续或有结论。** `character_new_items` 的主键/自增、`owner_scope` 的 `CHECK`、三个 `NOT NULL`、82 字节 `CHECK`、两个时间戳默认值与四段 `UNIQUE` 原样落位；`account_cargo_new_items` 的 `UNIQUE(account_id, slot_index)` 由 `idx_items_account_slot ON items(owner_id, slot_index) WHERE owner_scope='account'` 恢复，**强度恰好相等**（原约束同样不含 `list_type`）；`character_new_titlebook` 的 `PRIMARY KEY(character_id, category, slot_index)` 被四段 `UNIQUE` 覆盖，三个键列由可空转 `NOT NULL`、核心块由「无检查」转「恰好 82」，两处都是「取最严」；`character_avatar_detail` 的五个 `NOT NULL DEFAULT 0` 按稀疏规则转可空无默认，镶嵌槽的无条件 30 字节转条件式，`PRIMARY KEY(item_uid)` 由 `avatar_uid` 加部分唯一索引恢复；两张租借表本来无键，合并后首次声明 `(character_id, item_id)`；`character_item_values` 的 `PRIMARY KEY(character_id, list_kind, sort_order)` 拆成两个 `(character_id, sort_order)`，语义等价（原本可共存的 `('cooltime',5)` 与 `('effect',5)` 现在分居两表，仍可共存）。九张源表**没有一条外键**，所以无外键可搬。六条索引的去向与 `EveryIndexThatNamedARetiredTableIsGoneAndTheOneThatMovedWasRebuilt` 一致。
- **仓库里的时装确实落在部分唯一索引之外。** 没有任何写入路径给 `owner_scope='account'` 的行写时装列（`InsertOwnAsync`/`UpdateOwnAsync`/`InsertStackAsync`/`FillAsync` 全部硬写 `'character'`），表征用例播的仓库时装也不带 `avatar_uid`。裁定「a cargo avatar sharing a UID with a character's avatar is permitted today and stays permitted」成立，没有收得比原来紧。
- **`ON CONFLICT(avatar_uid) WHERE avatar_uid IS NOT NULL DO NOTHING` 只吞时装 UID 这一种冲突。** 评审在同一份 `items` 声明上实测四种情形：同 UID → 0 行（吞）；槽位 `UNIQUE` 冲突（非时装行）→ `UNIQUE constraint failed: items.owner_scope, items.owner_id, items.list_type, items.slot_index` 照旧抛；槽位冲突（带新 UID 的时装行）→ 同样照旧抛；`length(item_core)=82` 的 `CHECK` → 照旧抛。指定冲突目标后 upsert 只对该索引生效，没有把别的失败静默成「没写进去」。`UpdateOwnAsync` 的 `NOT EXISTS` 同理：`@avatarUid` 为 NULL 时 `held.avatar_uid = NULL` 恒不成立，非时装行不受影响；`held.item_uid <> @uid` 把自己排除在外，原地更新自己的时装不会自锁。
- **三条写不成 `CHECK` 的规则都还在，且都比改动前多了用例。** 哪些行有时装列——`WithAvatarDetail` 的 `ItemKind != 8` 早退，写入侧 `item.Core.ItemKind == 8 ? item.AvatarDetail : null`；仓库行永远没有——`SqliteAccountCargoItemTable` 干脆不 `SELECT` 那几列；非零 UID——`WithAvatarDetail` 的 `Core.Value <= 0` 与 `SaveDirtyAsync` 的前置检查。新增的 `AvatarCarrierRulesTests` 5 条把此前无人守的两条补上了。
- **时装列的写入与清除三问全部答对。** (a) 跨角色写不进去：`UpdateOwnAsync` 的 `WHERE ... owner_id=@cid` 拦住行本身，`NOT EXISTS` 拦住 UID，`InsertOwnAsync` 的 `DO NOTHING` 拦住 UID，三处都让调用方原有的 `touched != 1` 抛同一种异常，`DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision` 仍然覆盖并断言角色 70 的时装未被触碰。(b) 从时装变成非时装：`UpdateOwnAsync` 无条件写七列，`BindAvatar` 在 `detail is null` 时全给 `DBNull`，陈旧时装列不会留下——这是裁定新记的第一条「合并带来的后果」，实现与它一致。(c) 经 item store 移动：`CarriedAvatarsAsync` 在第一次 `DELETE` 之前按 UID 读回六列，`FillAsync` 随插入写回，`Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 仍绿——这是第二条「后果」，同样一致。
- **两处读取顺序对任何行集合都与原来相同。** 冷却与效果：原来的 `ORDER BY CASE list_kind WHEN 'cooltime' THEN 0 WHEN 'effect' THEN 1 ELSE 2 END, sort_order` 与「先读冷却按 `sort_order`、再读效果按 `sort_order`」逐行等价；原来落在 `ELSE 2` 的第三种取值今天写不进库，那条 `InvalidDataException` 随判别列一起消失，不是覆盖损失。租借：改动前是两张无 `ORDER BY` 的表 concat，改动后是单表 `ORDER BY item_id`，但 `ReadRentalsAsync` 末尾有 `.OrderBy(ItemId).ThenBy(InventoryTemplateId)`（`InventoryEntryAssembly.cs:494`），**最终顺序与读取顺序无关**；重复检查的顺序变了，但 item id 重复与模板 id 重复两条路径抛的是同一条消息，任何行集合下结论相同。
- **表征期望值的改动只有实施者说的那两处，理由成立。** `InventoryLoadCharacterizationTests` 的 diff 里期望常量只动了 `AccountCargoItems[0].StorageEntryId = 2 → 6` 与 `[1] = 1 → 5`，其余全落在 `INSERT` 与注释上；`EntryLoadCharacterizationTests` 的期望值零改动，只改了一条 `INSERT`；`AccountEntryLoadCharacterizationTests` 完全未动。`StorageEntryId` 确实到不了客户端：全仓引用只有三类——`SaveDirtyAsync` 的 `present`/分支、拒绝消息的文本（`Missing($"AvatarDetail[{item.StorageEntryId}]")` 等三处），以及 `NormalizeCharacterItems`/`NormalizeAccountCargoItems` 里的 `.ThenBy(item => item.StorageEntryId)`；而后者是 `.OrderBy(SlotIndex)` 之后的次级键，同一容器同一槽在 `ValidateItems` 里就被拒绝，所以这个次级键永远不参与比较，代理键换号不改变任何一条下发顺序。
- **被删的迁移守卫用例核对无误。** `LoadAsync_LegacyOnlyInventoryRequiresMigration`（2 例）断言的是「上一代两张表非空时拒绝入场」，两张表已退役，情形结构上不可构造；由此产生的后果（旧形状的库载入得到空背包而不是拒绝）已按契约要求写进 spec/裁定，是决定而非事故。删除正确。
- **领域记录确实没动。** `git diff --stat Server/src/Dfo.Server.Data/` 为空，`InventoryModels.cs` 与 `IInventoryEntryServices.cs` 逐字未改；`InventoryStorageItem.CharacterId` 由 `owner_id` 供给，字段与取值不变。
- **一类一表成立。** `SqliteCharacterItemTable`、`SqliteAccountCargoItemTable`、`SqliteCharacterTitleBookTable`、`SqliteItemStoreSession` 各自只指名 `items`；`SqliteCharacterRentalTable`、`SqliteCharacterItemCooldownTable`、`SqliteCharacterItemEffectTable` 各只指名自己那一张。跨表组装仍全在 `InventoryEntryAssembly` 与 `CharacterEntryAssembly`。
- **可补列清单删 36 条、不为新表补条目是对的。** `Reconcile` 只对 `PRAGMA table_info` 非空的表补列（`CharacterDatabaseSchema.cs:138-141` 的 `present.Count == 0 → continue`），四张目标表在任何既有库里都不存在，一律走 `CREATE TABLE` 建全。为它们写条目不会被执行，还会把「这一列可以事后补」这个错误暗示留在清单里。
- **裁定的改动有依据且与代码一致。** `items` 16 列的账本与 `CharacterDatabaseSchema` 的声明、`ItemCarrierShapeTests.Items` 三处逐列相同；列数算术自洽（184→185 落位、45→44 退役、165→166 目标列、161→162 解析列，全部 ±1）；D-5 改名 `cargo_character_id` 在附录映射表与判别列表两处同步；D-6 的写入者清点评审复核过（三插三删两改，无第四个写入者）；D-7 列出的四条值检查在 `WithAvatarDetail` 与 `SaveDirtyAsync` 里逐条可指认；两条「合并带来的后果」都有对应实现。裁定先改后实施的程序符合契约。

## 结论

`changes_required`。两条 A 级互为因果：A-1 是一条会改变下发帧的实际缺陷——脏物品租约保存把角色的称号册整片删掉；A-2 是让它漏过去的证据空洞——契约 SC-2 与计划 T-002 第 4 步点名要求的四条称号册断言一条都没写，全仓没有任何一个角色同时持有物品行与称号册行。补上 A-2 那份夹具，A-1 立刻变红；两条一并整改，才算把这一块的「必要支撑」交齐。

九条 B 级随记录交出，均不影响正确性。除这两条外，形状、约束落点、三条规则、时装列的写入与清除、两处读取顺序、表征期望值、领域记录与一类一表，评审逐条核实无误。

## 第二轮

第二轮在另一个全新上下文中进行，范围窄：只核实第一轮两条 A 级的整改、实施者接受的那条 B 级改动（裁定 D-15 的措辞），以及九条 B 级有没有哪条在整改后升级。不重做第一轮已核实的部分。

全量套件本轮自行复跑核实：**1100 通过 / 2 跳过 / 0 失败**（`Dfo.Pvf` 60、`Application` 494、`Host` 4、`Infrastructure` 99 加 2 跳、`Networking` 46、`Persistence` 194、`Protocol` 203），与实施者报告一致。可证伪性做了五次真改真跑的试验（下称 E1..E5），每次试验后文件还原并以 `sha256sum` 与试验前逐字节比对相同；交回时工作区无任何试验残留。

### 判定

`passed`。两条 A 级确已修好，修法未引入新的 A 级；九条 B 级无一升级。本轮新记四条，最高 B 级。

### 一、A-1 已修，且修法没有引入新问题

**过滤对称。** `ReadByCharacterAsync`（`Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs:40`）与 `DeleteOwnExceptAsync` 的两个分支（同文件 `:111`、`:113`）用的是同一条谓词 `list_type NOT BETWEEN 19 AND 23`。两侧界定的行集合完全一致：载入读到哪些行，保存就删哪些行；载入读不到的，保存也碰不到。`:99-102` 新写的注释把这条对称关系连同理由（保留名单只可能来自五个背包容器）写在了语句旁边。

**没有别的写入路径够得着称号册。** `Server/src` 下指名 `items` 的写入语句共八条，逐条看过：

| 语句 | 位置 | 它能碰到 19..23 吗 |
|---|---|---|
| `DeleteOwnExceptAsync` 的两个分支 | `InventoryItemTables.cs:110-114` | 不能，本次加的过滤就在这里 |
| `UpdateOwnAsync` | `:147-155` | 不能。语句本身没有 `list_type` 约束，但 `@uid` 只能是 `InventoryItemState.StorageEntryId`，而这个值全仓唯一的产地是 `InventoryEntryServices.cs:211` 的 `ToItemState`——即只能来自一次载入，而载入已排除 19..23。协议侧没有任何地方构造 `StorageEntryId`（全仓引用只有保存分支、两处次级排序键、三处拒绝文案） |
| `InsertOwnAsync` | `:175-181` | 不能。`@list` 取自租约容器，`SaveDirtyAsync`（`InventoryEntryAssembly.cs:108-112`）先拒绝账号仓库，其余只能是 `ParseContainerType` 认过的 0/1/2/3/7 |
| `SetStackCountAsync` | `:211` | 不能。`WHERE item_uid=@uid` 确实没有任何作用域条件，但 uid 只来自 `ReadMainStacksAsync`（`:71-72`，`list_type=0`） |
| `InsertStackAsync` | `:243-244` | 不能，硬写 `list_type` 0 |
| `SqliteItemStoreSession.EmptyAsync` | `SqliteItemStore.cs:252-256` | 不能。`list_type` 取自 `OwnerOf`（`:341-345`），只放行 Main/Avatar/PersonalCargo/Equipment/Pet |
| `SqliteItemStoreSession.FillAsync` | `SqliteItemStore.cs:274-281` | 同上 |

账号仓库侧一条写入语句都没有。称号册接口 `ICharacterTitleBookTable`（`Server/src/Dfo.Server.Abstractions/Persistence/CharacterEntryTables.cs:198-202`）只有 `ReadAsync`，全仓没有第二个写入者。所以称号册的行今天只有那条 `DELETE` 够得着，过滤加在了唯一需要它的地方。

**没有反过来漏删。** 租约拥有的五个容器 0/1/2/3/7 全部落在 19..23 之外，仍在删除范围内。既有用例 `SqliteEntryInventoryMutationRepositoryTests.DirtyLeaseSave_IsAuthoritativeForDeleteMoveUpdateInsertAndRollsBackWrongOwner`（`Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteEntryInventoryMutationRepositoryTests.cs:88-124`）播两件 `list_type=0` 的物品（`:95-96`）、租约只留一件并新插一件，`:112` 断言载回来恰好是 `[100, 300]`——本该被扫掉的那一行仍然被扫掉。该用例本轮全绿。

**E1（可证伪性）。** 把两个分支的 `AND list_type NOT BETWEEN 19 AND 23` 去掉 → `TitleBookCarrierTests.SavingTheInventoryLeavesTheTitleBookWhereItIs` 立刻红（`Assert.Equal() Failure: Values differ`），其余 7 条绿。第一轮那条「一次脏租约保存删光称号册」的缺陷现在有守卫了。

### 二、A-2 已修，新增用例逐条可证伪

`Server/tests/Dfo.Server.Persistence.Tests/Inventory/TitleBookCarrierTests.cs`，8 条（Theory 5 例加 3 个 Fact）。契约 SC-2 与计划 T-002 第 4 步点名的四件事，逐条对照：

| SC-2 要求 | 断言位置 | 让它变红的生产改动 | 结论 |
|---|---|---|---|
| `category` 0..4 存为 `list_type` 19..23 | `:51-53` | **没有任何一处**——见下面 R2-1 | 不可证伪，但不是 A 级 |
| 按 `category` 读回得到原值 | `:57-58`（五例） | **E3**：`InventoryItemTables.cs:351` 的 `FirstTitleBookContainer` 由 19 改 18 → 五例全红（外加另两条） | 可证伪 |
| 背包 0 号槽与称号册 0 类 0 号槽共存不撞 `UNIQUE` | `:76` 的 `COUNT(*)=2`，配 `:82-86` 两侧各自读回 | **E4**：`CharacterDatabaseSchema.cs:159` 的 `UNIQUE(owner_scope, owner_id, list_type, slot_index)` 去掉 `list_type` → 共存与保存两条红 | 可证伪 |
| 角色背包读取排除 19..23 | `:105` 的 `Assert.Empty`，以及 `:82-84` | **E2**：`InventoryItemTables.cs:40` 整句删掉 → 三条红 | 可证伪 |

另加一次：**E5**，`InventoryItemTables.cs:357` 的 `BETWEEN 19 AND 23` 改成 `BETWEEN 19 AND 21` → category 3、4 两例加保存那条共三条红，说明「称号册的行由这个区间界定」这件事也被钉住了。

第一轮说「把 `:40` 那一行整句删掉，全量套件仍然全绿」——本轮 E2 实测该行删掉后 `TheInventoryReadLeavesTheTitleBookOutRatherThanChokingOnIt`、`ACharacterMayHoldBothTheFirstMainSlotAndTheFirstTitleBookSlot`、`SavingTheInventoryLeavesTheTitleBookWhereItIs` 三条同时红。那条挡在选人路径上的过滤条件现在删掉会红。

**夹具真实。** `WithACharacterAsync`（`TitleBookCarrierTests.cs:161-191`）不手写角色行：先 `AccountSide.GetOrCreateAsync` 建账号，再 `CharacterSide.PerCall.CreateAsync` 走 `CharacterRosterService.CreateAsync`（`Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs:83-115`）。核实该方法确实写六张本体表：`characters` 的插入（`:95-97`）加 `:108-113` 的五条 `InsertIfAbsentAsync`——`character_condition`、`character_stats`、`character_pvp`、`character_settings`、`character_expert_job`，源码注释写的就是「The rows without which the character cannot be entered」。它一条 `items` 行都不写，所以 `:76` 的 `COUNT(*)=2` 计到的两行确实只有用例自己播的那两行。`:172` 的 `Assert.Equal(Character, characterId)` 顺带把「这个 id 是服务器给的」钉住了。

### 三、实施者接受的那条 B 级：D-15 的改写与代码一致

`Docs/Refactor/TableOwnershipRuling.md:741` 现在写的是「reproduces today's strength on the case that matters and is stricter on one that does not」，`:747-752` 单起一段说明收紧的是哪一半、为何接受。逐句核对：

- 「跨角色那半边等同」——基线 `UpsertOwnAsync` 的 `ON CONFLICT(item_uid)` 更新条件绑在写入者身上，冲突时触碰 0 行、调用方抛 `InvalidDataException`；合并后 `InsertOwnAsync` 的 `ON CONFLICT(avatar_uid) WHERE avatar_uid IS NOT NULL DO NOTHING`（`InventoryItemTables.cs:181`）与 `UpdateOwnAsync` 的 `NOT EXISTS`（`:152-154`）同样触碰 0 行、抛同一个异常类型。强度与失败形态都对得上。
- 「同角色两行携带同一 UID 那半边更严」——基线 `SqliteCharacterAvatarDetailTable.FindAsync`（`5aba3a9` 的 `InventoryItemTables.cs:270`）按 `item_uid` 取一条详情，两条核心块相同的物品行会解析到同一条详情、三项一致性检查全过、载入成功；合并后 `idx_items_avatar_uid ON items(avatar_uid) WHERE avatar_uid IS NOT NULL`（`CharacterDatabaseSchema.cs:208`）是全表唯一、不带 owner 条件，第二行无论走哪条写入路径都进不去。裁定这段话说的就是这件事。
- 「仓库的时装留在索引之外」——`InsertOwnAsync`/`UpdateOwnAsync`/`InsertStackAsync`/`FillAsync` 四处全部硬写 `owner_scope='character'`，账号侧没有任何写入语句，所以 `owner_scope='account'` 的行永远 `avatar_uid IS NULL`，落在部分索引之外。成立。

**一致，B-1 已消。** 附一条不改变结论的边角：`UpdateOwnAsync` 的 `NOT EXISTS` 子查询没有 owner 条件，所以「仓库的时装不进索引」这句对索引成立，但若真存在一条 `owner_scope='account'` 且 `avatar_uid` 非空的行（今天没有任何写入路径造得出），它会连带挡住角色的更新。这与第一轮 B-2 是同一片区域的同一个成因，不影响 D-15 这段文字的正确性。

### 四、九条 B 级的复核：无一升级

| 编号 | 现状 | 是否升级 |
|---|---|---|
| B-1 裁定 D-15 强度自述不准确 | 已按上一节改写并核实一致 | 已消 |
| B-2 `CarriedAvatarsAsync` 跨归属取事实 | 原样（`SqliteItemStore.cs:207-212` 仍无 owner 条件）。复核救场链路仍成立：`EmptyAsync` 只删同角色同容器同槽的行，所以被借用 UID 的那一行在插入时还在，`idx_items_avatar_uid` 抛 `SqliteException`、事务整体回滚，无泄漏 | 否 |
| B-3 `ReadCharacterItemsAsync` 的所有权拒绝已成死代码 | 原样（`InventoryEntryAssembly.cs:190-194`） | 否 |
| B-4 仓库侧 `RequireUniqueSlots` 失去唯一覆盖 | 原样 | 否 |
| B-5 角色侧 `duplicate` 用例与仓库侧处置不一致 | 原样 | 否 |
| B-6 `Avatar(reader, ordinal)` 用单列判在场 | 原样（`InventoryItemTables.cs:255-264`）。新增用例播的行七列全 NULL，不构成新暴露面 | 否 |
| B-7 `SqliteItemStore` 类注释过时 | 原样（`SqliteItemStore.cs:329-331` 仍写「its rows live in another table keyed by account」） | 否 |
| B-8 抽象层仍指名 `character_new_titlebook` | 原样（`CharacterEntryTables.cs:49`、`:197`） | 否 |
| B-9 裁定两处小瑕 | 原样：`:885-890` 仍写「One index is added」后面跟两个；D-15 的锚点在 `:890` 与 `:970` 两处仍与实际 slug 不符。本轮新写的 D-5 锚点写法是对的 | 否 |

### 五、本轮新记（均不影响判定）

**R2-1（B 级）SC-2 的「写入后存为 19..23」这半边没有主体。** `TitleBookCarrierTests.cs:51-53` 的 `Assert.Equal(containerType, ScalarAsync("SELECT list_type FROM items WHERE owner_id=1"))` 断言的是夹具自己在 `:201` 用 `{19 + category}` 写进去的字面量，没有任何生产代码参与——所以它不可证伪。但这不是漏写：称号册今天只有读路径（`ICharacterTitleBookTable` 只有 `ReadAsync`），正变换在 `Server/src` 下没有实现者，也没有迁移会执行它。也就是说 SC-2 这半边在当前代码下无法由任何用例满足，不存在「代码坏了却仍绿」的守卫，只存在一个还没有守卫对象的约定。逆变换那半边（`:57-58`）已由 E3 证明可证伪。建议记入 spec 的已知边界，等称号册有写入者时再补真守卫。

**R2-2（C 级）新用例里一句注释与生产注释矛盾。** `TitleBookCarrierTests.cs:91` 写「parses a container type that admits only 0, 1, 2, 3 and 12」，而 `ParseContainerType`（`InventoryEntryAssembly.cs:518-529`）在 `allowAccountCargo: false` 下认的是 0/1/2/3/7，12 只在账号仓库读取时才认。同一次改动里 `InventoryItemTables.cs:21-22` 写的「only 0, 1, 2, 3 and 7」是对的。两处相邻文字互相打架。

**R2-3（B 级，记录）删除的作用域是补集而不是白名单。** `DeleteOwnExceptAsync` 现在删的是「除 19..23 之外该角色的全部行」，而不是「租约拥有的那五个容器」。今天两者等价：`items` 里角色行只可能是 0/1/2/3/7 与 19..23，真出现第六种也会先在 `ParseContainerType` 把载入打掉、租约根本不成立。但结构上，将来若有人把第六种角色容器（例如 29 快捷栏）落进 `items` 并给它自己的读路径而没有同时改这条 `DELETE`，一次租约保存会把它整片扫掉——与 A-1 一模一样的形状。第一轮给的另一种写法 `AND list_type IN (0,1,2,3,7)` 对这一类结构免疫。当前正确性不受影响，实施者的选法与读取侧对称也有其道理，故只记录。

**R2-4（C 级）夹具查询少一个作用域条件。** `TitleBookCarrierTests.cs:53`、`:76`、`:151-152` 的三处直查都写 `WHERE owner_id=1`，而本用例里账号 id 与角色 id 同为 1，`owner_scope` 没有出现在条件里。今天没有仓库行所以结论正确，但只要将来有人给这个夹具加一条 `owner_scope='account'` 的行，这三条断言会开始量错东西。`:53` 另外还没有 `ORDER BY`/`LIMIT`，靠「只有一行」成立。

### 第二轮结论

`passed`。

A-1 的过滤加在了唯一够得着称号册的那条语句上，与读取侧同谓词、同边界；八条写入语句逐条清点，没有第二条路径能碰到 19..23；本该被清掉的行仍然被清掉，既有用例仍绿。A-2 补的八条断言里，SC-2 四件事有三件经真改真跑证明可证伪，第四件（正变换）今天在生产代码里没有主体，属于约定而非失守的守卫；夹具走的是真实的账号与角色创建路径，六张本体表的行确由 `CharacterRosterService.CreateAsync` 写出。裁定 D-15 改写后的措辞与 `idx_items_avatar_uid`、`ON CONFLICT ... DO NOTHING` 与 `NOT EXISTS` 三处的实际效果逐句对得上。九条 B 级无一升级，本轮另记四条，最高 B 级。

全量 1100 通过 / 2 跳过 / 0 失败；五次可证伪性试验后文件均已还原并逐字节校验，工作区无试验残留。
