---
kind: rks-verification
decision: pass
---

# 新鲜验证：物品有自己的身份，槽只说它在哪

独立上下文。与执行方、控制器、评审方均非同一上下文。
**本文件里的每一条证据都是我自己在当前工作区跑出来或读出来的**；评审方与执行方的断言只用作
「要去核什么」的线索，不作为证据本身。

## 被评估的确切主张

> 成果「物品有自己的身份，槽只说它在哪」已完成：`items` 变成世界表、`item_slots` 说位置，
> 物品获得跨位置不变的 `item_uid`；服务器对客户端的行为逐值不变，
> 例外只有契约兽那两处被授权的变化。

## 我核到的工作区状态

| 项 | 我测到的值 |
|---|---|
| `git rev-parse HEAD` | `51cd99c29cb5ff1e9812ca7b48bfbe9795326db4` |
| `git status --porcelain=v1` 行数 | **79**（跟踪修改 60 ＋ 非 `.rks-agent` 未跟踪 18 ＋ `.rks-agent/` 目录项 1） |
| `git diff --stat HEAD` | 60 files, **+2905 / −476** |
| `git diff --stat HEAD -- Server/src` | 27 files, **+1969 / −266** |
| `git diff --stat HEAD -- Server/tests` | 32 files, **+747 / −210** |
| `.rks-agent/tasks/items-own-their-identity/` 下未跟踪产物 | **20** 个（19 份流程产物 ＋ `reviews/change-review.md` 本身） |
| 验证结束时 `git status` | 仍 **79** 行，`+2905 / −476` 未变（我的验证没有改动工作区） |

**未使用任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash` 一次都没有）。
我做的 PVF 枚举建在 scratchpad 的独立工程里，只 `<Reference>` 已构建的 `Dfo.Pvf.dll`，
**没有**在 `Server/src` 下产生 obj/bin，也没有加 `ProjectReference`。

### 评审之后那次收尾：我自己确认它只是注释与文档

评审复评当时记录的 `Server/src` 是 **+1946 / −266**；现在是 **+1969 / −266**
——**净增 23 行插入，删除数一字未变**。删除数不变意味着没有任何一行被删除或被改写
（改写会同时计一删一增）。这 23 行落在哪里，我逐行看过：

`Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs` 的
`CarriedAvatarsAsync` 的 `<remarks>` 里，从 `/// Three things in this file are true today…`
到 `/// …this paragraph is addressed to them.`，**连同前面一行分隔用的 `///` 恰好 23 行**，
且这 23 行**全部**匹配 `^\s*///`（我用 `grep -vcE "^\s*///"` 复算，非注释行数 = **0**）。

**结论：评审之后的收尾在生产侧确实只加了注释，没有触碰任何可执行逻辑。**
另两处改动（`behavior-change-list.md`、`Docs/ItemWorldModel.md`）都在未跟踪的文档里，
不进 `Server/src` 的 diffstat。

## 判据复算（我自己跑的一次全量）

命令：
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
**退出码 0。逐项目列出并相加，未用 `grep -c`：**

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 316 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |

相加：521+52 = 573；+60 = 633；+6 = 639；+316 = 955；+236 = 1191；+1025 = **2216**。

**2216 通过 / 2 跳过 / 0 失败**，与期望一致，且不低于基线 HEAD 的 2087。

- **解决方案里恰有 7 个 `*.Tests.csproj`**（`grep -c "Tests.csproj" Server/86JP.Server.slnx` = 7，
  逐个列出：Dfo.Pvf / Application / Host / Infrastructure / Persistence / Networking / Protocol），
  上表 7 行一一对应，**没有项目被静默跳过**。
- 那 2 个 skip 我点名核过，是
  `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
  `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
  ——**它们在档案存在时才跳过，所以这 2 个 skip 正是「PVF 档案已加载」的标志。**
- 另一路旁证：`Dfo.Server.Application.Tests` 跳过 **0**，而 SC-4/SC-6/SC-7 的判据全是
  `[SkippableFact]` ＋ `Skip.IfNot(ContentArchive.Available, …)`，**它们确实实跑了，不是被跳过。**
- 已知偶发失败（`ObjectDisposedException: 'SQLitePCL.sqlite3'`，栈根
  `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar`）
  **本次未出现**。
- `TreatWarningsAsErrors` / CS9113 由构建成功（退出码 0）间接证明。

## 线上字节锚

- `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`
  的 sha256 = `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`，
  `git show HEAD:` 同一文件的 sha256 **逐字符相同**。`git diff HEAD --stat` 对它输出为空。
  **相对 HEAD 一字未改。**
- 表本身：`AcceptedKnightFrameSha256` 数组第 32–93 行 = **62 条** SHA-256 字面量，
  由 `Assert.Equal(AcceptedKnightFrameSha256, hashes)` **整表比对**。
  该判据在 `Dfo.Server.Protocol.Tests` 的 521/0/0 里全绿。

## SC-1..SC-12 逐条判定

| SC | 判定 | 我自己取到的当前证据 |
|---|---|---|
| SC-1 | **达成** | 读 `WorldRowSurvivesTheMoveTests.AWornPieceKeepsOneIdentityThroughAMoveAWearAndATakeOff` 的方法体：三次移动（其中一次跨容器 Main→Equipment→Main），**每次都从库里重新读回身份**（`IdentityAtAsync`），断言 `identity` 不变、高水位 `HighWaterMarkAsync` 不变、源槽变 null、`SlotRow("world", identity, 1)` 落在目标槽，最后断言 core 逐值相同。实例状态：`CreatureStateFollowsTheIdentityTests.ACreatureKeepsAllEightOfItsFactsFieldByFieldWhenItMoves` 逐字段断言八项（CreatureKey / Field04 / ModeFlag / ProgressValue / Mode1Field0A / Mode1Field0B / FieldAfterValue / CreatureText / TailFlag），**并再对种子值断言一遍**以防「两次都读到空」的恒真；`AvatarDetailFollowsTheIdentityTests.AnAvatarKeepsAllSixOfItsFactsFieldByFieldWhenItMoves` 同型。全部实跑全绿 |
| SC-2 | **达成** | 我读了 `CharacterDatabaseSchema.cs:200` 的建表语句：`item_slots` **8 列**（owner_scope / owner_id / list_type / slot_index / kind / id / core_uid / count），`kind` 只允许 `('world','template')`，两类内容**列集合完全相同**，只在取值上不同；三条具名 CHECK 都在：`slot_count_is_positive`、`world_slot_holds_exactly_one`、`world_slot_names_its_own_row`。对照 `git show HEAD:` 的 `items`：HEAD 上 `items` 带 `owner_scope/owner_id/list_type/slot_index` ＋ `UNIQUE(owner_scope,…)`；**今天的 `items` 一列位置信息都没有**（19 列 = item_uid ＋ item_core ＋ cargo_character_id ＋ created_at ＋ updated_at ＋ 6 时装 ＋ 8 契约兽），位置只在 `item_slots` 一处表达。判据 `ItemCarrierShapeTests.TheSlotTableKeepsThePositionUniqueAndHoldsEachKindToItsCount` 与 `WorldAndSlotTableTests.A_slot_says_the_same_eight_things_whichever_kind_it_holds` 全绿 |
| SC-3 | **达成** | 机检 `ItemIdentityArchitectureTests` 三条，我读了实现：`AnIdentityIsAlwaysOptionalWhereverAnItemIsDescribed` 扫 5 个生产程序集的全部成员，**凡类型为不可空 `ItemIdentity` 的成员即违规**（`Assert.Empty`）；`NothingOutsideTheRowGatewaySaysAnItemsStorageRowKey` 禁止行键离开网关；`TheSweepFindsTheInventoryShapes` 是**防空扫**（断言 `Production.Distinct().Count() == 5`，且扫到 `Identity` 与 `ItemUid` 两个成员名）。拆堆：`SplitPileTests` 三条（拆完两堆各自可移动、可再合并、都不带任何继承标识）。全部实跑全绿。golden 侧我另行核到：堆的期望值今天全是 `Identity = null` |
| SC-4 | **达成** | 唯一实现 `ItemPlacementRuleService.ClassifyAsync(int itemId, CancellationToken)` ——**签名只收模板号，物理上取不到库里的 `ItemKind`**；实现体是 `placements.GetAsync(itemId).WorldClass ?? Interchangeable`，答案出自档案 `[equipment type]`（`PvfEntryLifecycleContentSource.ResolveWorldClass` → `ClassifyEquipmentType`）。生产调用点我自己 grep 到 **6 处，分别是**：`CharacterRosterService.cs:205`、`DailyRefillService.cs:105`、`InventoryEntryAssembly.cs:172`、`InventoryEntryAssembly.cs:280`、`ItemContainers.cs:283`、`ItemContainers.cs:598`（另有 `ItemPlacementRuleService.cs:56` 的类内自调），**都在写入路径上，不是一次性搬运**。「库里 kind 与档案不一致」的判据：`ItemClassificationTests.A_write_classifies_the_template_the_archive_declares_not_the_kind_the_row_carries` ——它**故意反着写两条**（契约兽写成消耗品 kind、堆写成装备 kind），断言 core 里的字节保持反的、而 `WorldClass` 取档案的答案。实跑（Application.Tests 跳过 0）全绿 |
| SC-5 | **达成（限定见下）** | `IWorldItemTable.ReadUnreferencedAsync` 存在（`InventoryTables.cs:234` 声明、`InventoryItemTables.cs:512` 实现）。判据 `WorldAndSlotTableTests.A_thing_no_position_holds_is_found_by_asking_for_those` 造出一个无引用世界行并查到它；配套的 `A_thing_a_position_holds_is_not_one_of_those` 与 `A_piles_row_is_not_one_of_those` 反向钉住判别力。契约对 SC-5 要求的证据**字面就是一条判据**，故达成。**限定（我自己复核过前提）**：`grep` 全仓 `IWorldItemTable`/`SqliteWorldItemTable` 的命中只有接口声明、实现、以及 3 处判据；`PersistenceModule` 无任何 `IWorldItemTable`／`IItemSlotTable` 注册，**今天只有判据能调它**。这是契约允许的形状，但读者不应把 SC-5 读成「服务器能查孤儿」 |
| SC-6 | **达成** | `CreatureGoesBackOnTests.A_creature_taken_off_can_be_put_back_on_and_is_the_same_creature`：装上→取下→**再装回**，三段每段都从库里读身份，三次都断言 `seeded` 不变，且源槽读回 null。同文件 `A_creature_aimed_at_a_slot_that_is_not_its_own_is_refused` 挡住「让契约兽随便进任一佩戴槽」的退化解。生产改动我自己看了 diff：`PvfEquipmentSlots.cs` **只加了一行可执行代码** `["[creature]"] = 24`（其余是注释）；该字典今天 **16 个键，分别是** weapon / title / title name / coat / shoulder / pants / shoes / waist / amulet / wrist / ring / support / magic stone / earring / support weapon / creature，映射到 13 个不同槽号。实跑全绿 |
| SC-7 | **达成** | `PvfEntryLifecycleContentSource.WornPieceMetadata` 今天对 `ItemWorldClass.Creature` 给 `CreatureItemKind`（**= 5**，同文件 :29），否则 `WornPieceItemKind`（**= 1**，:21）；`git show HEAD:` 同一文件是 `new RefillItemMetadata(1, 1, 9, 64)`，**HEAD 对契约兽给的就是 1**——变化属实且只此一处。存储侧读法 `EquippedCreatureQuery.CreatureItemKind = 5` 未动。判据 `CreatureKindAgreesWithTheArchiveTests` 的**第三条断言**（`new EquippedCreatureQuery().GetItemId(WornInTheCreatureSlot(stored.Core))` 必须答出模板号）是承重的那条——前两条「写进去的 == 档案给的」单独看恒真，判据自己的 `<remarks>` 也如实写了。实跑全绿 |
| SC-8 | **达成** | ① 全量 **2216 ≥ 基线 2087**，0 失败。② **字节锚 sha256 与 HEAD 逐字符相同**且整表 62 条全绿。③ `Server/tests` 的 diff 我自己量过：`git diff HEAD -U0 -- Server/tests` 中 **`-[Fact]/[Theory]/[SkippableFact]` 行数 = 0**（无用例被删）、无测试文件被删除或重命名（`--name-status` 全是 `M`）。④ 被删的断言与 InlineData **共 18 行，我逐行看过归属**：`ItemCarrierShapeTests` 12 行（`owner_scope`/`owner_id`/`list_type`/`slot_index`/`item_core`/`avatar_uid` 这些**已被本次建模移出 `items` 的列**）、`SchemaOwnershipTests` 1 行（`character_creatures(` 已退役）、`TitleBookCarrierTests` 1 行（`FROM items` → `FROM item_slots`，期望值 `2L` 未变）、`SqliteEntryInventoryMutationRepositoryTests` 与 `SqliteInventoryEntryRepositoryTests` 各 1 行（`StorageEntryId` → `Identity`）。⑤ golden 里的期望值改写**恰 6 行**，分别是 `CharacterItems[0..3].StorageEntryId = 2/3/1/4` 与 `AccountCargoItems[0..1].StorageEntryId = 6/5`，一律改为 `Identity = null`——**这 6 行是 SC-3「消耗品没有身份」直接要求的结果，保留旧期望反而会与 SC-3 冲突**，故属「随建模主体的必然改写」而非「为迁就实现的改写」（契约 Constraints 的原话正是「不得**为迁就实现**而改写」）。⑥ 唯一一处保护强度实质下降是 `avatar_uid` 上那条局部唯一索引随列一起退役（`ItemCarrierShapeTests` 的四条正向断言翻成 `DoesNotContain`），代之以 `FillAsync` 的语句级守卫 ＋ 三条行为判据；这是移除该列的必然代价，已在记载中写明 |
| SC-9 | **达成** | 两处授权变化各自的代码位置我**自己回到位置核过**：RQ-009 = `PvfEquipmentSlots.ByName` 的 `["[creature]"] = 24`（diff 里可执行改动只有这一行）；RQ-010 = `WornPieceMetadata` 的 `Creature ? CreatureItemKind(5) : WornPieceItemKind(1)`（HEAD 为常量 1）。连带后果我抽查了三条并核实：`MoveItemSpaceSlice.ErrorCodeFor` 确实把 `NotWearable` 与 `ItemCannotOccupySlot` **都**映射到 `MoveItemSpaceResponse.NotMovable`（后果 D 不上线属实）；`ItemSlotRuleService.AllowsQuickSlot` 确实把 `KindCreature/KindCreatureEquipment/KindCreatureConsumable/KindAvatar/KindAvatarEmblem` 排除（后果 B 属实）；`ItemPlacementRuleService.IsEquipmentAsync` 已改为 `ClassifyAsync(...) is not Interchangeable`——若仍用 HEAD 的 `ItemKind == 1`，契约兽由 1 变 5 会让它**静默翻成 false**，这一处保值改写属实。清单另有一节「新增的生产拒绝路径（全部 13 条）」，逐条带可达性与判据有无，详见下一节我的复核 |
| SC-10 | **达成** | 两个机检文件的 sha256 我自己算过，与 `git show HEAD:` **逐字符相同**：`CommandHandlerArchitectureTests.cs` = `ab9107bc2371d4a3…`、`NotificationHandlerArchitectureTests.cs` = `90f36a9203f6eb13…`。`git diff HEAD --stat` 对二者输出为空 ⇒ **判定谓词与具名豁免清单的 diff 为空**（两者都住在这两个文件里）。两者所在的 `Dfo.Server.Application.Tests` 1025/0/0 全绿 |
| SC-11 | **达成** | 我用 `grep -E "(class\|interface\|record\|struct)\s+\w*DomainStore\b"` 在工作区与 `HEAD` 各扫一遍，**两边命中集合完全相同，共 10 项，分别是**：接口 `ICharacterDomainStore`、`IDarkKnightComboDomainStore`、`IItemDomainStore`、`IQuestDomainStore`、`IDomainStore`、`IDomainStore<TSession>`；实现 `SqliteCharacterDomainStore`、`SqliteDarkKnightComboDomainStore`、`SqliteItemDomainStore`、`SqliteQuestDomainStore`。**本轮未新增任何以 `DomainStore` 结尾的类型**，四个具体实现逐个都是事务结算的真源；世界侧走 `IWorldItemTable`／`IItemSlotTable`，命名未被污染 |
| SC-12 | **达成** | `AGENTS.md` 纯追加 **+189 / −0**，新小节 `### 物品：有身份的东西，和它在哪，是两张表`，下辖五个子节：`本轮明确没有做的（逐条，不是「大概没做」）`、`被授权的两处行为变化`、`三处「结论对，但撑着它的不是代码」`、`已知缺陷与覆盖缺口（只记，本成果不修）`、`三条已被证伪的记载，订正`。「没做」一节**逐条点名**：定时清理器、掉落/交易托管/拍卖挂单/邮件附件、契约兽领域本身、24 还是 25 不裁、`character_condition` 五列不动、旧库作废重建。我抽查了记载与代码的一致性，逐条属实：`PvfEquipmentSlots.ByName` = 16 键 ✔；`CreatureItemKind = 5` / `WornPieceItemKind = 1` ✔；`AllowsQuickSlot` 排除 5 ✔；`ErrorCodeFor` 两码同映射 ✔；`ItemContainers.ConsumeAsync` 的生产调用方**恰 1 处**（`ItemUseEffectDomainService.cs:83`）✔；全仓 `ADD COLUMN` **恰 1 处** ✔；HEAD 的 `items` 建表语句确实**没有** `world_identity_fits_the_wire` CHECK ✔ |

## 派工点名要复核的既知事项

### 1. 第 4 条拒绝「今天不可达」的定性 —— 我自己查了档案，**定性成立**

那条守卫我读到了实处：`InventoryEntryAssembly.cs:173`
`if (worldClass == ItemWorldClass.Avatar && item.Core.ItemKind != 8) throw new InvalidDataException(...)`。
它要开火，需要库里存在「档案读作时装、但 core 里 kind ≠ 8」的行；能造出这种行的
只有 `ItemContainers.LandAsync`（把 `placement.ItemKind` 原样写进 core），
而 `WornPieceMetadata` 对非 Creature 一律给 1。

`LandAsync` 的入口我自己 grep 过：`AddAsync` 与 `RefillAsync` 两个。
`.AddAsync(` 在 `Server/src` 的命中共 6 处，**其中 `PlayingCharacter.cs:113` 是金币不是物品容器**，
**物品容器的 AddAsync 恰好 5 处，分别是** `SelectCardCommand.cs:327`、`GroundPickupSlice.cs:314`、
`AcceptQuestCommand.cs:131`、`FinishQuestCommand.cs:122`、`GrantTutorialRewardCommand.cs:57`
——**没有第六条**。`RefillAsync` 的生产调用方恰 1 处（`DailyRefillService.cs:129`）。

**档案侧我没有采信任何人的枚举，自己重跑了一遍。** 在 scratchpad 建了一个只
`<Reference>` 已构建 `Dfo.Pvf.dll` 的控制台工程，按仓库自己的读法
（`PvfListFile.Read`、`ResolveWorldClass`/`ClassifyEquipmentType`、
`PvfDropItemPoolSource.ReadCreatable`）逐文件走了真实档案 `Script.pvf`。**我测到的：**

```
equipment.lst entries = 118479
distinct ids          = 118479          <- 没有任何重复 id，"首条胜出" 是空转
[equipment type] tag resolved = True
Avatar=66328 Creature=707 Equipment=51444 sum=118479
fallback: file-not-found=0  no-tag-or-unreadable=0   <- 两条兜底分支触发 0 次
declares [creation rate]: Avatar=0 Creature=0 Equipment=15567
rate>0                  : Avatar=0 Creature=0 Equipment=6062
drop pool (rate>0,grade>=0,rarity>=0) rows=6062 distinct=6062 Avatar=0 Creature=0 Equipment=6062
declaredEquip - positiveEquip = 9505    <- 与本轮之前就写着的注释 "9505 pieces" 严丝合缝
63465 -> equipment/creature/WolfLee.equ  class=Creature
quest files listed = 3441 / opened = 3441
[depend give item] distinct ints = 20 ; avatars = 0 ; creatures = 0
[reward int data] distinct ints  = 1599; avatars = 0 ; creatures = 1  -> 该 id 正是 63465
dungeon files listed = 1047 / opened = 1047
[special passive object item] distinct ints = 185; avatars = 0; creatures = 0
serverparameter.etc [escalade tutorial reward] ints = [38,8474,1]; avatars = 0
```

计数口径说明：我在任务/地下城两处**取的是整段里出现的全部 Int32 的集合**
（因此 20 / 1599 / 185 比记载里的「不同物品 id 数」略大，是**严格超集**），
超集里时装数仍为 0，**结论只会更强而不会更弱**。两条兜底分支各触发 0 次这一点要紧：
`ResolveWorldClass` 在「文件找不到」与「无该标签」时都兜底为 `Equipment`，
若常触发就会系统性低估时装数——**实测 0 次**。

代码侧的闸门我也自己核了，且这三个文件**都不在本次变更集内**（`git diff --name-only HEAD -- Server/src`
无命中）：`DropRuleService.cs:99` 的 `if (kind == DropItemKind.Avatar) return null;`；
`PvfDropItemPoolSource.Read` 的 `if (creationRate <= 0 || grade < 0 || rarity < 0) continue;`
以及「没有 `[creation rate]` 段直接 `continue`」；`ClearRewardBlankItemSwitches` 三个开关
在 `ClearRewardRules.cs:86` 一处 `new()`，生产无处赋 true。
角色创建那条路另有判据 `PvfEquipmentSlotsTests.No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants`
把全部 32 个 job 的起始槽钉死为 weapon/coat/pants 三个非时装槽，本次实跑全绿。

**裁定：我认同「记为潜伏缺陷、不记为第三处被授权的行为变化」。**
契约意义上的「行为变化」要有**可达的输入**才成立；我自己穷尽了全部六条写入入口
（五条 `AddAsync` ＋ 一条 `RefillAsync`）与全部档案来源，**今天没有任何输入到得了它**，
所以服务器对客户端的可观察行为确实没变。它已记进 `AGENTS.md` 的「已知缺陷与覆盖缺口」，
位置紧邻其根因（时装 8／1 那对不一致）。

**顺带独立坐实**：`63465` = `equipment/creature/WolfLee.equ`，我的枚举把它分类为 `Creature`，
且它**确实出现在 `[reward int data]` 里**——授权变化二的两条后果有一个点名的可达实例，
不是纸面推论。

### 2. `SqliteWorldItemTable` 的守卫缺口 —— 前提我自己核过，属实

`grep -rn "IWorldItemTable\|IItemSlotTable\|SqliteWorldItemTable\|SqliteItemSlotTable" Server/`
的全部命中：接口声明 2 处（`InventoryTables.cs:189/260`）、实现 2 处
（`InventoryItemTables.cs:417/643`）、生产里唯一一处是**静态辅助** `SqliteItemSlotTable.Name(kind)`
（`InventoryItemTables.cs:182`），其余 5 处全在判据里。`PersistenceModule` 对二者**零注册**。
`ReadUnreferencedAsync` 的调用方全部在 `WorldAndSlotTableTests`。
**生产零调用方、零 DI 注册属实，缺口今天到不了生产。**

### 3. `.gitignore` 的 `*.pvf` 匹配目录 `Server/src/Dfo.Pvf`

按派工要求，这条**不属于本次验证的成功条件**，我未据此扣分，也未据此判 fail。
（我的 PVF 枚举工程建在 scratchpad 而非该目录下，正是为了不触碰它。）

## 我没有独立复核的部分（如实记录，不升级为证据）

- 「13 条新增拒绝路径」表里 **「判据有无」那一列**，我**没有**重跑执行方与评审方各自做过的
  变异实测。我核的是这一列所服务的结论（第 4 条的可达性定性），以及守卫本身确实存在于代码里。
  该列**不是任何一条成功条件的必需证据**，不影响本判定。
- 记载里其余数字（例如 `AGENTS.md` 那 50 条可核陈述）我做的是**抽样**复核（见 SC-12 一栏列出的
  8 项），不是逐条 50 条全核。抽样命中的每一项都属实。

## 判定

**pass。** SC-1..SC-12 每一条都有我自己在当前工作区跑出来或读出来的当前证据：
全量 2216 / 2 / 0（逐项目相加复算，退出码 0），字节锚 sha256 与 HEAD 逐字符相同，
两个注入面机检文件 sha256 与 HEAD 逐字符相同，`DomainStore` 类型集合与 HEAD 相同，
`items` 与 `item_slots` 的形状我逐列数过，分类规则的签名我读过，
两处授权变化的代码位置我逐处回到位置核过，
唯一一条「原本成功 → 现在抛错」的拒绝我**自己走了一遍真实档案**确认今天不可达。
评审之后的收尾我独立确认只加了 23 行注释（`Server/src` 净 +23 / −0，且这 23 行全部匹配 `///`）。
