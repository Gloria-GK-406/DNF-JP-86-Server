---
kind: rks-review
verdict: approved
---

# 完整变更评审：物品有自己的身份，槽只说它在哪

独立上下文，scope = `change`。与执行方、控制器、十八轮快审均非同一上下文。
未提供 adversarial challenge set，故无 `Challenge Dispositions` 一节。

## 评审范围与其完整性

我自行确认了变更集，未只看 `git diff HEAD`：

- `git status --porcelain=v1` 共 **79** 行：跟踪修改 **60**（`git diff --stat HEAD` = 60 files, +2867/−476）、
  未跟踪 **19** 项（其中 `.rks-agent/tasks/items-own-their-identity/` 是一个目录项）。
- `git ls-files --others --exclude-standard` 展开后：非 `.rks-agent` 新文件 **18**
  （4 个 `Docs/*.md` ＋ 14 个新判据文件），`.rks-agent/tasks/items-own-their-identity/` 下 **19** 个流程产物
  （TaskContract / spec / plan / routing-decision / sdd-execution.yaml / behavior-change-list ＋ 13 个 TASK 文件）。

三部分都在本次评审范围内，与派工描述一致。

## 判据复算（我自己跑的）

命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`，
在本工作区运行，**退出码 0**。逐项目列出并相加，未用 `grep -c`：

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 316 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |

相加：52+521=573；+60=633；+316=949；+6=955；+1025=1980；+236=**2216**。
跳过 2，均在 `Dfo.Server.Infrastructure.Tests`，是
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
——它们在档案**存在**时跳过，故这 2 个 skip 确是「PVF 档案已加载」的标志。
**2216 / 2 / 0，与成果记录声称的数字逐项目吻合。**

`TreatWarningsAsErrors` 与 CS9113 由构建成功间接证明（退出码 0）。

## 硬约束逐条复核

| 约束 | 结论 | 证据 |
|---|---|---|
| 字节锚一字未改 | **成立** | `git diff HEAD --stat -- '*SelectCharacterAcceptedTranscriptTests*'` 输出为空；`AcceptedKnightFrameSha256` 表与 `AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline` 全绿 |
| `CommandHandlerArchitectureTests` / `NotificationHandlerArchitectureTests` 判定谓词与豁免清单未放宽 | **成立** | `git diff HEAD --stat -- '*CommandHandlerArchitectureTests.cs' '*NotificationHandlerArchitectureTests.cs'` 输出为空 |
| 分层由项目引用强制，无反向依赖 | **成立** | 逐 csproj 的 `ProjectReference`：Data→∅；Abstractions→Data；Application→Abstractions,Data；Infrastructure/Persistence/Networking→Abstractions,Data；Protocol→Abstractions,Application,Data,Networking；Host→其余全部。无反向边 |
| 凡触达持久写的命令一律 `Transactional` | **成立且被加强** | `CommandRegistrationTests.The_commands_that_reach_a_persistent_write_are_transactional` 为新增，钉住 10 个命令；纯增量 |
| `store` 只有一种读法 | **成立** | 全仓以 `DomainStore` 结尾的类型恰 4 个具体实现（`SqliteCharacterDomainStore`、`SqliteDarkKnightComboDomainStore`、`SqliteItemDomainStore`、`SqliteQuestDomainStore`）＋ 其接口，逐个都是事务结算的真源；本轮**未新增**任何 `DomainStore` 结尾的类型（世界侧走 `IWorldItemTable`／`IItemSlotTable`，命名未污染） |

## SC-1..SC-12 逐条独立判定

| SC | 判定 | 我核到的证据（按成员名索引） |
|---|---|---|
| SC-1 | **达成** | `SqliteItemDomainStoreSession.LandAsync` 只写 `item_core` 与 `updated_at`，不触身份、创建时间、时装六列、契约兽八列；`EmptyAsync` 以 `AND item_uid NOT IN (<spared>)` 免删幸存者。判据：`WorldRowSurvivesTheMoveTests.AWornPieceKeepsOneIdentityThroughAMoveAWearAndATakeOff` / `.AMoveNeitherDeletesNorBuildsAWorldRow`、`AvatarDetailFollowsTheIdentityTests.AnAvatarKeepsAllSixOfItsFactsFieldByFieldWhenItMoves`、`CreatureStateFollowsTheIdentityTests.ACreatureKeepsAllEightOfItsFactsFieldByFieldWhenItMoves` |
| SC-2 | **达成** | `item_slots` 8 列对两类内容列集合相同，只在 `kind`/`id`/`count` 取值上不同；三条具名 CHECK（`slot_count_is_positive`、`world_slot_holds_exactly_one`、`world_slot_names_its_own_row`）。无第二种槽表达。判据：`ItemCarrierShapeTests.TheSlotTableKeepsThePositionUniqueAndHoldsEachKindToItsCount`、`WorldRowSurvivesTheMoveTests.APositionSaysWhichOfTheTwoItHoldsAndHowMany` |
| SC-3 | **达成** | `ItemIdentityArchitectureTests` 三条：`TheSweepFindsTheInventoryShapes`（防空扫）、`AnIdentityIsAlwaysOptionalWhereverAnItemIsDescribed`、`NothingOutsideTheRowGatewaySaysAnItemsStorageRowKey`。全仓 `StorageEntryId` 今天只剩 1 处命中，就在该判据的禁用名单里。拆堆：`SplitPileTests` 三条 |
| SC-4 | **达成** | 唯一实现 `ItemPlacementRuleService.ClassifyAsync(int itemId, …)`，**签名只收模板号**，物理上取不到库里的 `ItemKind`。生产调用点恰 6 处：`CharacterRosterService`、`DailyRefillService`、`InventoryEntryAssembly`×2、`ItemContainers`×2（另有 `IsEquipmentAsync` 内部自调 1 处）。判据：`ItemClassificationTests.A_write_classifies_the_template_the_archive_declares_not_the_kind_the_row_carries`、`PvfEntryLifecycleContentSourceTests.EquipmentType_IsReadAsTheAvatarTheCreatureOrOrdinaryGear`（10 行 InlineData，含 avatar 分支） |
| SC-5 | **达成（但只在判据里可达，见 O-2）** | `IWorldItemTable.ReadUnreferencedAsync` ＋ `WorldAndSlotTableTests`（造出无引用行并查询到它） |
| SC-6 | **达成** | `CreatureGoesBackOnTests.A_creature_taken_off_can_be_put_back_on_and_is_the_same_creature`，并断言 `seeded` 身份跨取下／装回不变。该判据本轮实跑（未在 2 个 skip 之列） |
| SC-7 | **达成** | `CreatureKindAgreesWithTheArchiveTests`；我核过成果记录自陈的「恒真陷阱」——承重的确是第三条断言（档案给的数正是 `EquippedCreatureQuery` 用的 5），前两条单独看恒真，记录如实写了这一点 |
| SC-8 | **未完全达成 —— 见 F-1** | 字节锚未改且全绿；`Server/tests` 无测试方法被删、无文件断言总数下降。但存在**清单外的新增拒绝路径**（F-1），且既有判据有 5 处期望值改写不属「类型改名」也不对应 RQ-009/RQ-010（O-6） |
| SC-9 | **未完全达成 —— 见 F-1** | RQ-009／RQ-010 两处本身及其连带后果 A–D 我**逐条回到声称位置核过，全部属实**（复算见下）。但「清单之外没有其它行为变化」这句**不成立** |
| SC-10 | **达成** | 两个机检文件 diff 为空，全量绿 |
| SC-11 | **达成** | 见上表 |
| SC-12 | **达成** | `AGENTS.md` 新小节的 **50 条可核陈述逐条对着代码核过，无一为假**（详见下节）；「本轮明确没有做的」六条逐条列出，含孤儿清理器、掉落/交易/拍卖/邮件附件、契约兽领域本身、24/25 不裁、`character_condition` 五列不动、旧库作废重建 |

### 我复算过的记载数字（抽样，全部属实）

- `items` **19 列**：2（`item_uid`/`item_core`）＋3（`cargo_character_id`/`created_at`/`updated_at`）
  ＋6 时装＋8 契约兽 = 19。逐列数过 `CREATE TABLE IF NOT EXISTS items`。✔
- `item_slots` **8 列**。✔
- `IndexDeclarations` **3 条**：`idx_item_slots_core`、`idx_item_slots_account_slot`、`idx_characters_account`。✔
- `PvfEquipmentSlots.ByName` **16 个键**。✔
- `WornSlotForAsync` 真正的生产消费点**恰 2 处**，都在 `MoveItemSpaceSlice`（`PutOnAsync`、`TakeOffAsync`）。✔
- `placement.ItemKind` **3 个文件 5 行**：`ItemArrivalRuleService`×3、`ItemContainers`×1、`ItemSlotRuleService`×1。✔
- 「同一时装 UID 至多一行」的谓词今天在 **4 处 SQL**：`InventoryItemTables.cs:229`、`:264`
  （`CarriesTheAvatar` 常量的两个使用点，定义在 :45）、`SqliteItemDomainStore` 的
  `CarriedAvatarsAsync` 与 `FillAsync` 各一处内联。✔
- `LiftAsync` / `NoContainer` 全仓**零命中**；`avatar_uid` 全仓只剩 2 处，均为
  `ItemCarrierShapeTests` 里的反向断言。✔
- `character_creatures` 已完全退役；我另核了 **HEAD 上它同样没有任何生产写入方**
  （只有读与一条 `field04` UPDATE），所以契约兽状态搬家在生产上确实逐值不变。✔
- `Docs/ItemWorldModel.md` 「更正 C-1」属实：`StackableItemCore.Create` 确写死
  `Marker16 = -1` 与 `RandomOptionChangedIndex = byte.MaxValue`，而
  `GameInventoryDelivery.cs:65` 对 `ItemKind == 2` 不钳。✔

### `AGENTS.md` 记载的独立事实核查（50 条，另一路独立核对）

本成果反复栽的跟头是「记载与代码不一致」，所以我对 `AGENTS.md` 的
「物品：有身份的东西，和它在哪，是两张表」与「已知缺陷与覆盖缺口」两节，
把**每一条可核陈述与每一个数字**都对着代码走了一遍（50 条）。

**结论：无一条为假。** 上面「我复算过的记载数字」那节是我亲手复算的抽样，与这一路结论一致。
另有以下需要读者知道的限定：

- **1 条不可核（不是假）**：「**发布版的 `items` 表没有 `world_identity_fits_the_wire` CHECK**」。
  仓里没有任何已发布/被跟踪的数据库可查——`bin/` 下的 `dfo-server.db` 都是**新** schema 跑出来的产物
  （里面能扫到该 CHECK 的文本，`idx_items_avatar_uid` 只作为已释放页的残留出现）。
  该句的**机制半边成立**（`Reconcile` 只发 `ALTER TABLE … ADD COLUMN`，既存表永远补不上 CHECK；
  溢出落到 `InventoryEntryAssembly` 里的 `checked((int)itemUid)`）。这条限定记载自己也写了。
- **3 条属实但带保留**：(a) 「`ClassifyAsync` 生产侧 6 个调用点」——外部调用方确为所列 6 处，
  另有 `ItemPlacementRuleService.IsEquipmentAsync` 的**类内自调**一处（不影响「唯一生效处」的结论）；
  (b) 「以档案为准**并拒绝**」——「以档案为准」处处成立，但**「拒绝」只存在于入场存盘这一条路径上**
  （`InventoryEntryAssembly.SaveAsync`），`ItemContainers.PutAsync` / `ToSlotItemAsync` 取档案答案但不拒绝
  ——**这正是 F-1 第 1 条所指的那处拒绝**；(c) 两处「变异实测」（删守卫仍绿、`CarriedAvatarsAsync` 恒空则红 1 条）
  本轮**未重跑变异**，只复核了零命中的 grep 与代码机制，两者都自洽；`316` 这个数被重新测过，精确。

**跨文档一致性**：`AGENTS.md` 与 `Docs/ItemWorldModel.md` 的「更正 C-1」逐句对得上，无冲突。
`ItemWorldModel.md` 正文停在基线时点（自称「未改动任何代码」），其若干设计提案与交付形状不同
（三张世界表 vs 一张、堆叠物不留世界行 vs 留、时装六列迁出 vs 留在 `items`、
`core_uid` 这一列是正文没有的），这些是「提案 vs 交付」而非事实冲突，
且 C-1 已经收回了其中可行性那一半。**但见 O-9。**

### 「两处授权变化」本身：逐条核过，全部属实

- **后果 A**（装得回去）：`PutOnAsync` 的 `slot is null` 分支不再命中 —— 属实。
- **后果 B**（槽 24 已有契约兽时成为互换）：`PutOnAsync` 既有逻辑第一次对契约兽可达 —— 属实。
- **后果 C**（源槽为 24 取下时成为互换，无判据）：`displacedSlot != command.SourceSlotIndex`
  只在源槽为 24 时不再成立 —— 属实，且如实标注了无判据。
- **后果 D**（`NotWearable` → `ItemCannotOccupySlot`，同一错误码）：属实，`ErrorCodeFor` 两者同映射。
- **RQ-010 后果 A/B/C**：`BandsFor` 两条分支产出相同（kind 1 走 `== EquipmentKind`，
  kind 5 走 `!AllowsQuickSlot(5)`，均只 yield `own` 后 break）—— 属实，无变化。
  `CanOccupyMainSlot` 对槽 3..8 由允许变拒绝 —— 属实，是第二处真实可观察变化，如实记了「无判据覆盖」。
  `DailyRefillService` 一路事实为空 —— 属实：`DailyRefill_RealArchiveProducesAuthoritativeRulesWithStackLimits`
  把真档案六条规则全部钉死为 kind 4，`WornPieceMetadata` 走不到。
- **`IsEquipmentAsync` 的改写是保值的**（我另行验算，记录未提但必须成立）：HEAD 是
  `placement?.ItemKind == 1`；今天是 `ClassifyAsync(...) is not Interchangeable`。
  契约兽的 `ItemKind` 本轮由 1 变 5，若仍用旧谓词会**由 true 翻成 false**；
  改成按 `WorldClass` 判恰好保住了它。时装、普通装备、堆叠物、未声明模板四类逐类等值。
  **这是一处很容易变成静默行为变化而没有变成的地方。**

---

## Blocking Findings

### F-1 「清单之外没有其它行为变化」这句不成立：至少 6 处新增的生产拒绝路径未被列出，其中 1 处可达且会把原本成功的存盘变成抛错

我按 `git diff HEAD -U0 -- Server/src/` 逐条扫了新增／删除的 `throw`，得到本轮**净新增的生产拒绝**如下
（计数口径：只数 `Server/src` 下 `+` 行里的 `throw` 语句，不数注释里的 "throw" 字样，不数改写文案的既有 throw；
区分大小写）：

| # | 位置（按成员名） | 新增的拒绝 | 是否已在清单里 | 是否有判据 |
|---|---|---|---|---|
| 1 | `InventoryEntryAssembly.SaveAsync` | `InvalidDataException`「The archive declares item {id} an avatar, but the row in slot {n} carries kind {k}.」 | **否** | 有：`ClassificationRefusalTests.AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot` |
| 2 | `CharacterRosterService.CreateAsync` | `InvalidDataException`「Starting piece {id} is not an individual piece the archive declares wearable…」 | **否** | 有：`ClassificationRefusalTests.CreationRefusesAStartingPieceTheArchiveReadsAsAPile` |
| 3 | `CharacterRosterService.CreateAsync` | `InvalidDataException`「Starting piece {id} could not be put into the world.」（HEAD 上 `InsertOwnAsync` 的返回值被忽略） | **否** | 无 |
| 4 | `CharacterRosterService.CreateAsync` | `InvalidDataException`「Two starting pieces claim equipment slot {n}.」（HEAD 上同一情形由 `UNIQUE(owner,list,slot)` 抛 `SqliteException`——**与 O-2 同类的异常类型变化**） | **否** | 无 |
| 5 | `InventoryEntryAssembly.SaveAsync` | `InvalidDataException`「Two of this character's things claim container {l} slot {s}.」 | **否** | 无 |
| 6 | `SqliteItemDomainStoreSession.Write` | `ArgumentException`「A write says what the archive makes of the thing being written…」（`WorldClass is null`） | **否** | 有：`WorldRowSurvivesTheMoveTests.AWriteThatDoesNotSayWhatTheArchiveMakesOfItIsRefused` |
| — | `InventoryEntryAssembly.SaveAsync` | 「A dirty inventory names the same thing in more than one position.」 | **是**（账 12） | 有：`EntrySaveReconciliationTests.ASaveNamingOneThingInTwoPositionsIsRefusedAndChangesNothing`（我核过，断言 `Assert.Contains("more than one position", …)`，账 12 的说法属实） |
| — | `SqliteItemDomainStoreSession.FillAsync` ／ `SqliteCharacterItemTable.InsertOwnAsync` | `InvalidOperationException`「…named no identity」／「no world identity left to give…」 | **部分**（O-2 只记了前者，且只在 TASK-13 的订正小节里） | 前者有判据，后者（RQ-008 的上界拒绝）无判据 |

**其中第 1 条是可达的，并且方向是「原本成功 → 现在抛错」：**

- `ItemContainers.AddAsync → LandAsync → IntoEmptySlot` 把 `placement.ItemKind` 原样写进 core：
  `StackableItemCore.Create(itemKind, itemId, opened.Count)`。
- 对一件**档案声明为时装**的模板，`WornPieceMetadata` 今天仍给 `WornPieceItemKind = 1`
  （时装那对不一致本轮**明确不修**，记录自己也这么写）。于是该行以 `ItemKind = 1` 落库。
- 下一次入场存盘走 `InventoryEntryAssembly.SaveAsync`：`ClassifyAsync` 答 `ItemWorldClass.Avatar`，
  而 `item.Core.ItemKind != 8` → **抛 `InvalidDataException`，整次存盘回滚**。
- 在 HEAD 上，同一行是良性的：`WithAvatarDetail` 对 `ItemKind != 8` 原样放行，
  `GameInventoryItemEntry` 也只对 kind 8 要求 `AvatarDetail`，存盘成功。
- `AddAsync` 的生产入口是 `SelectCardCommand`、`GroundPickupSlice`、`AcceptQuestCommand`、
  `FinishQuestCommand`、`GrantTutorialRewardCommand` 五处。**代码通路无守卫**；
  是否有真实内容（任务奖励／掉落表）会送来一个 `[* avatar]` 模板，我**读代码判定不了**，
  需要档案侧证据。这一点请勿当作我已排除。

**为什么这构成阻塞而不是观察：**

1. 契约 RQ-012 / AC-014 要求的正是「清单之外没有其它行为变化」，而这是**派工点名要我独立判定的那句**。它不成立。
2. 清单自己立的标准是「**清单要记的是变化，不是只记穿线的变化**」——后果 D 正是据此被收录的。
   按同一把尺子，上面 6 条都该在清单上。
3. 第 1 条不是「不上线的内部变化」：它把一次成功的存盘变成失败，玩家侧可观察。
4. 执行方**知道**这条拒绝存在（为它写了判据 `AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot`），
   却没有把它带进那份以「清单之外没有其它行为变化」收尾的清单——这正是 routing-decision 预判的失败模式。

**需要的处置（范围很小，主要是记载 ＋ 一次裁定）：**

- (a) 在 `behavior-change-list.md` 新开一节（例如「未上线／内部的行为变化」），
  用与 RQ-009／RQ-010 同等的严格度逐条列出上表 6 条＋已列的 2 条，每条给出位置、可达性、判据有无。
  「被授权的行为变化」仍然只有两处，不必改。
- (b) 对第 1 条单独取得处置：要么由所有者裁定为**第三处被接受的行为变化**；
  要么收窄该守卫（例如只在 `ItemKind` 与档案都非 1／8 的组合上拒绝，或不拒绝而是记录）；
  要么给出档案侧证据证明没有任何 grant 通路能送来时装模板。**三选一，不要默默留着。**
- (c) 第 4 条（`SqliteException` → `InvalidDataException`）与 O-2 是同一类。请**一并**在同一节里处理，
  这样「异常类型变了」这件事在清单里只有一种记法。

---

## 对派工点名两条待裁观察的答复

**O-1（在 `SqliteItemDomainStore.FillAsync` 的 `<remarks>` 补一句「等价性挂在 `CarriedAvatarsAsync` 的
`avatar_expire_date IS NOT NULL` 过滤上，是耦合不是不变量」）：赞成，但维持为建议，不作为阻塞。**
理由：我复核了这条耦合确实存在且承重——`@expire IS NULL` 短路放行那一支，其正确性完全依赖
「`CarriedAvatarsAsync` 读回来的第一列必然非空」。这是跨方法的隐含前提，值得写下。
但它不改变行为，也不改变任何判据结论，不足以挡住成果。
**另有一条同型耦合建议一并写下**（见 O-4）。

**O-2（`FillAsync` 被拒时异常类型由 `SqliteException` 变 `InvalidOperationException`，
要不要升格为一条编号的行为变化）：不升格为「被授权的行为变化」，但必须并入 F-1(a) 的新一节。**
理由：授权只有 RQ-009／RQ-010 两条，把它编号进那一节会让「授权了几处」这个数失真。
但把它孤零零留在 TASK-13 的订正小节里，会让读者以为它是个别现象——**它不是**：
上表第 4 条是完全同型的另一处（`UNIQUE` 冲突的 `SqliteException` → 自写的 `InvalidDataException`）。
两处放在同一节、同一句话说清「异常类型变了、回滚行为不变、不上线」，才是可核的记法。

---

## 对派工点名的已知事项的独立复核

**「`SqliteWorldItemTable` 零调用方、零 DI 注册」—— 前提属实，处置我同意。**
`grep -rn "WorldItemTable" Server/` 全部命中：`InventoryTables.cs:189`（接口声明）、
`InventoryItemTables.cs:417`（实现）、`WorldAndSlotTableTests.cs:455/463`、
`CreatureIdentityFitsTheWireTests.cs:55/107`。`PersistenceModule` 只注册
`ICharacterItemTable`、`IAccountCargoItemTable`、`SqliteItemDomainStore`／`IItemDomainStore`，
**没有** `IWorldItemTable`、**没有** `IItemSlotTable`。守卫缺口今天到不了生产，有意留着是对的。

**并且我另行验算了一处记载没写、但必须成立的事**：`LandAsync` 的
`UPDATE items SET item_core = @core` 也会改写 core 里那 4 个字节的时装 UID，而它**同样没有守卫**。
它安全，但安全的原因不是守卫，而是 `Survivors` 三趟谓词都不允许 `core.Value` 改变
（前两趟要求 core 字节完全相同，第三趟 `SameThing` 显式比 `core.Value`）。
**这是又一条「结论对，但撑着它的不是守卫」**，建议随 O-1 一起写进记载（见 O-4）。

**三条「结论对，但撑着它的不是代码」与覆盖缺口：我抽样复核，记得准。**
`world_identity_fits_the_wire` 只对新建库成立（`Reconcile` 只发 `ALTER TABLE … ADD COLUMN`，
且 `Columns` 清单在 HEAD 与今天都**不含任何 `items` 行**，所以这不是本轮引入的退化）；
`ConsumeAsync` 的单一生产调用方与「门是档案数据不是代码」；
「清空先于填入」已不承重而由 `spared` 扛住——三条都属实。

---

## Non-blocking Observations

**O-1（承 F-1 之外的记载建议）** `AGENTS.md` 新小节与 `behavior-change-list.md` 都**没有**提到
上表那 6 条新增拒绝。F-1(a) 落实后，`AGENTS.md` 的「已知缺陷与覆盖缺口」也应同步一句。

**O-2 SC-5 只在判据里可达。** `ReadUnreferencedAsync` 的唯一实现在 `SqliteWorldItemTable` 上，
该类型零 DI 注册、零生产调用方。契约对 SC-5 要求的证据就是一条判据，**故 SC-5 字面达成**；
但读者容易把它读成「服务器能查孤儿」。建议记载里明说「今天只有判据能调它」。
（`behavior-change-list.md` 的订正 T12-1 已写了「`IItemSlotTable` 在生产里仍然无人调用」，
但没有对 `IWorldItemTable` 说同样的话。）

**O-3 契约兽八列在世界行被重建时不会被抢救，而时装六列会。**
`CarriedAvatarsAsync` ＋ `FillAsync` 的 `facts` 参数只搬时装六列；`FillAsync` 的
`INSERT INTO items(...)` **完全不写 creature_* 八列**。所以一件契约兽若在同一批次里
**既换位置、core 字节又变了**，三趟 `Survivors` 全不认，其世界行被删后重建，
**八列静默变 NULL，该契约兽从清单里消失**。
今天到不了生产（生产没有任何通路创建契约兽世界行），且纯移动必被第二趟 `SameContent` 接住，
所以不构成阻塞。但**时装有抢救、契约兽没有**这个不对称没有任何注释说明，
后来者接通契约兽领域时极易踩到。建议在 `FillAsync` 或 `CarriedAvatarsAsync` 的 `<remarks>` 里点一句。

**O-4 建议与 O-1 合并写下的第二条耦合**：`LandAsync` 无时装唯一性守卫，
其安全性挂在 `Survivors` 三趟谓词都固定 `core.Value` 上（推导见上）。
这与 O-1 是同一类「跨方法的隐含前提」，一起写才成体系。

**O-5 `InventorySlotContent` 与 `InventoryItemState.Holds` 生产零消费。**
`grep -rn "InventorySlotContent\|\.Holds\b" Server/src` 的命中里，
`Dfo.Server.Data/Inventory/InventoryModels.cs` 是**定义**，其余无一处生产读取；
唯一消费者是判据 `SplitPileTests`（`item => Assert.Equal(ItemSlotKind.Template, item.Holds.Kind)`）。
SC-2 的实质由 `item_slots` 的列形状与 `ItemSlotRow` 承担（`CharacterRosterService` 与
`InventoryEntryAssembly` 都在用），**所以 SC-2 不受影响**；但按本仓「不得留一段没有理由的机制」，
这两个类型今天没有生产理由。请裁定：删掉，或在记载里写明它是给后续成果预留的形状。

**O-6 `Server/tests` 有 5 处期望值改写既不属「类型改名的符号更新」，也不对应 RQ-009/RQ-010。**
（独立审计 ＋ 我抽样复核过其中两处最重的。没有任何测试方法被删除，没有任何文件的断言总数下降，
没有 InlineData 行被净删除，两份 golden 除下列 6 行外逐字未动。）

1. `ItemCarrierShapeTests.TheAvatarDetailsPrimaryKeyIsRestoredByAPartialIndex`
   → 改名 `TheAvatarsUidIsNoLongerAKeyOfTheTable`，四条正向断言（`UNIQUE`、`ON items(avatar_uid)`、
   `WHERE avatar_uid IS NOT NULL`）翻转为三条 `DoesNotContain`。
   **这是本组里唯一实质性的保护强度下降**：一条对**所有**写路径生效的库级不变式，
   降级为三条语句各自的条件 ＋ 三条行为判据。
   缓解是真实的（`DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision`、
   新增的 `DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds`、
   新增的 `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`，后者有变异实测），
   记载也如实写了「只剩 `SqliteWorldItemTable` 一个写入方不被覆盖」——**故不阻塞**。
   但同文件 `ItemsCarriesEveryConstraintItsSourcesDeclared` 的 `<remarks>` 仍宣称
   「源表声明的每条约束都必须到达某处」，而这条约束现在无处可到——**该注释已与代码不符，建议改**。
2. `SqliteEntryInventoryMutationRepositoryTests.DailyRefill_IsVisibleToTheEntryReloadInsideTheSameUseCase`：
   `Assert.True(item.StorageEntryId > 0)` → `Assert.Null(item.Identity)`（断言被反转）。
3. `SqliteInventoryEntryRepositoryTests.LoadAsync_LeavesOutTheItemsOfEveryOwnerButTheOneAskedFor`：
   `Assert.Equal(301, item.StorageEntryId)` → `Assert.Null(item.Identity)`。
   判别力由同测试保留的 `Assert.Equal(3003, item.Core.ItemId)` 实质接住（种子里 302/303 的 ItemId 不同）。**严重度低。**
4. `InventoryLoadCharacterizationTests` 两份 golden 共 **6 行**字面量
   （`CharacterItems[0..3].StorageEntryId = 2/3/1/4`、`AccountCargoItems[0..1].StorageEntryId = 6/5`）
   → 一律 `Identity = null`。该文件自述的方法论是「每个字段取互不相同的值，任何列置换都会失败」，
   这 6 行原是少数能抓行序错配的乱序字面量，现退化为 6 个恒定常量。
   **但期望值是对的**：该 fixture 的物品档案不声明，分类必为 `Interchangeable`，堆无身份。
   测试自己的 `<remarks>` 也如实改写并说明了。**严重度中低。**
5. `ItemCarrierShapeTests.EveryIndexThatNamedARetiredTableIsGoneAndTheOneThatMovedWasRebuilt`：
   `Contains("ON items(owner_id, list_type, slot_index)")` → `Contains("ON item_slots(core_uid)")`
   ＋ 新增 `DoesNotContain("idx_items_owner_space")`。理由（唯一键的隐式索引以 `owner_scope` 开头，
   可覆盖同类查询）技术上成立。**严重度低。**记载已承认该判据「名字不再描述它证明的东西」。

这 5 处**全部**对应的是「物品自持身份、堆无身份」这个重构主体（RQ-005 / RQ-002），
而不是对实现的迁就。合并判断：**契约那条「不得为迁就实现而改写」在实质上被守住了**，
但契约字面写的例外只有「类型改名的符号更新」，这 5 处不在其内。
建议在成果记录里把这一类单独列一小节说清（「随建模主体改写的既有期望值，共 5 处，分别是…」），
而不是让它隐没在 diff 里。

**O-7 `item_slots.count` 对堆叠物写了但不承重。** `HoldAsync` 写
`PileOf(item.Core) = Math.Max(core.Value, 1)`，而投影读的是 `core.Value`（`GameInventoryDelivery`）。
`SqliteItemDomainStoreSession.PileOf` 的 `<remarks>` 承认了这一点。
后果是这一列可以与 core 漂移而**没有任何东西会发现**。今天无害（无人读它），
但它是一个「看起来是真源、其实不是」的列——与本成果反复强调要消灭的那类东西同型。建议记一笔。

**O-8 字节锚的作用面比「入场物品帧逐值不变」窄一格。**
`AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline` 的输入是
`SelectCharacterPayloadTestData.CreateComplete()`（内存 fixture），不是一次库读。
所以它钉的是**编码器**，不是「存储 → 投影 → 编码器」整条链。
整条链由 `EntryLoadCharacterizationTests` / `InventoryLoadCharacterizationTests` 的 golden 接住，
而后者正是 O-6.4 里被改了 6 行的那份。两者合起来仍然充分，**不阻塞**；
但「线上字节由字节锚守着」这句在记载里应加上这半格限定。

**O-9 `Docs/ItemWorldModel.md` 第七节注 1 的行号引用已经过时。**
它引 `PvfEntryLifecycleContentSource.cs:101-102` 作为「档案解析器对同一件物品给 1」的出处，
而那两行今天已不是那个常量，且档案对契约兽现在给 5（正是本轮授权变化二）。
`AGENTS.md` 在这一点上是对的，过时的是设计文档。该文档是本变更集里的**新增未跟踪文件**，
在评审范围内。它已经带了一条「更正 C-1」，建议**同一处再补一条更正**（或直接改这条注），
否则后来者会拿一份自相矛盾的设计文档去读代码——而这正是本成果反复栽跟头的那个模子。
（顺带：`ItemWorldModel.md` 用行号索引，`behavior-change-list.md` 明确说本仓「按内容索引，不写行号」
因为有过行号全部漂移的先例；这条注就是那条纪律的一个反例。）

---

## 总评

这是一次质量很高的架构级重构。判据我自己跑过并逐项目复算（2216 / 2 / 0）；
字节锚与两个注入面机检的 diff 确为空；分层无反向边；`store` 词汇未被污染；
分类规则确实收敛成了单一处且**签名上取不到**库里的种类字节——这一点做得比契约要求的更硬。
成果记录的严格度罕见：`AGENTS.md` 那两节的 50 条可核陈述**无一为假**，我抽样复算的每一个数字都精确对上，
`Docs/ItemWorldModel.md` 的「更正 C-1」属实，账 10 的自我订正属实，
`SqliteWorldItemTable` 那个「零调用方零注册」的前提属实。
`IsEquipmentAsync` 由 `ItemKind == 1` 改为按 `WorldClass` 判——如果没改，
契约兽的 kind 由 1 变 5 会让它静默翻成 false；这是本轮最容易出事而没出事的地方。

挡住它的只有一件事：**「清单之外没有其它行为变化」这句不成立**。
本轮净新增了至少 6 处生产拒绝路径，清单一处未提；其中
`InventoryEntryAssembly.SaveAsync` 那条时装种类拒绝，经
`ItemContainers.AddAsync` 是**可达**的，方向是把原本成功的存盘变成抛错。
按执行方自己立的尺子（「清单要记的是变化，不是只记穿线的变化」），这些都该在清单上。

处置范围很小——一节记载 ＋ 对那条可达拒绝取一次明确裁定。
补齐并经独立复评后，我预期这份成果可以接受。

---

# 复评（F-1 整改后）

复评对象：整改后的变更集。**我重新独立核过，没有采信整改说明的任何结论。**

## 变更集与「生产代码一行未改」的核对

- `git status --porcelain=v1` 仍是 **79** 行，与初评时逐项相同。
- `Server/src` 的 diffstat **逐字未变**：27 files, **+1946 / −266**，与初评时一致。
- `Server/tests` 的 diffstat 亦未变：32 files, +747 / −210。
- 三个相关生产文件的 sha256 我自己算过，与整改说明给出的前缀**逐字符吻合**：
  `CharacterRosterService.cs` = `fa77fb827756b7ee…`、
  `InventoryEntryAssembly.cs` = `4a04f19f66945256…`、
  `SqliteItemDomainStore.cs` = `642d426fd71fc3f2…`。
- 全量插入数由 2867 增至 **2882**（+15，全在 `AGENTS.md`：174 → 189 行），
  删除数 476 **未变**；`behavior-change-list.md` 由 356 行增至 **466** 行。

**结论：改动确实只落在两份记载上，生产代码与判据一行未动。** 「mtime 变新但内容未变」的说法属实。

## 判据复算（我自己重跑）

`2216 通过 / 2 跳过 / 0 失败`，退出码 0。逐项目相加：
52+521=573；+60=633；+6=639；+316=955；+236=1191；+1025=**2216**。跳过仍是那 2 条 `A_missing_archive_fails_*`。

## 我自己做的变异实测（针对「无判据」那三条）

整改说明点名要我复核 #2、#3、#6。**我没有采信它的变异结论，自己做了一次。**

- 备份：`CharacterRosterService.cs` / `InventoryEntryAssembly.cs` 复制到 scratchpad，
  记下 sha256（`fa77fb82…` / `4a04f19f…`）。**全程未用任何 git 还原类命令。**
- 变异：把 **#2、#3、#5、#6** 四条的异常类型改成 `InvalidTimeZoneException`（#5 作对照组）。
- 全量结果：**恰好 1 条红** ——
  `Dfo.Server.Persistence.Tests.Inventory.EntrySaveReconciliationTests.ASaveNamingOneThingInTwoPositionsIsRefusedAndChangesNothing`
  （`失败: 1，通过: 315，总计: 316`），其余六个项目全绿。
- 还原后两文件 sha256 **逐字节回到 `fa77fb82…` / `4a04f19f…`**，`InvalidTimeZoneException` 命中数归 0，
  `git status` 仍 79 行，`Server/src` diffstat 仍 27 files/+1946/−266。

**据此：**

- **#2、#3、#6 确实无判据覆盖 —— 表里这三格是对的。**
- **#5 确实有判据 —— 这一格也是对的。**

## 13 条表的逐条核对

| 核查项 | 结论 |
|---|---|
| 总数 13 | **对**（推导有误，见 R-1） |
| #7 / #8 归属 `SqliteWorldItemTable.InsertAsync` | **对**（`InventoryItemTables.cs` 472 / 478 两行都在该方法体内）。**初评时是我错了**——我把这两条记到了 `SqliteCharacterItemTable.InsertOwnAsync` 名下，那个方法根本不抛，它返回 `long?` 由调用方判空。**这一格整改说明比我准。** |
| #12 / #13 归属 `SqliteItemDomainStore.FillAsync` | **对**（`SqliteItemDomainStore.cs` 561 / 568） |
| #2 / #3 / #6 无判据 | **对**（我自己变异实测，见上） |
| #1 / #4 / #5 / #11 / #12 有判据 | **对**（#5 我变异实测；#1/#4 整改说明的变异结论与我初评的判据定位一致；#11/#12 我初评已核过判据存在） |
| #3 的异常类型变化说法 | **对**：`git show HEAD:` 的建表语句里 `items` 确带 `UNIQUE(owner_scope, owner_id, list_type, slot_index)`；今天 `item_slots` 仍带同一条唯一键，库依旧兜底 |
| 「不升格为被授权的行为变化」 | **同意**，理由与我初评给的一致 |

## 档案侧「不可达」证据的核对

我自己核过的部分（这些都是**本轮未改动**的既有代码，故不是为本结论现做的）：

- `DropRuleService`（`Server/src/Dfo.Server.Application/World/DropRuleService.cs:99-100`）
  确有 `if (kind == DropItemKind.Avatar) return null;` —— 该文件**不在变更集内**。
- `PvfDropItemPoolSource` 的 `[creation rate]` 闸门与那句「9505 pieces of equipment 主动把
  creation rate 设成零 / 118479 archive files」注释，确是**本轮之前就写着的**
  —— 该文件同样**不在变更集内**。所以 15567 − 6062 = 9505 这个交叉验证是**真的独立**，
  不是拿本轮自己的数去对本轮自己的数。
- `ClearRewardBlankItemSwitches` 三个开关确是默认 `false` 的自动属性，
  `ClearRewardRules.cs:86` 处 `new()` 一次，**生产无任何一处赋 true**（其余命中全是文档注释）。
  该文件亦不在变更集内。
- **发放通路数**：我自己 grep `\.AddAsync(` 得到的物品容器调用点恰好就是那五处
  （`SelectCardCommand`、`GroundPickupSlice`、`AcceptQuestCommand`、`FinishQuestCommand`、
  `GrantTutorialRewardCommand`），**没有第六条 `AddAsync` 通路**。
- `FinishQuestCommandHandler` 的奖励确实走 `container.AddAsync`
  （`FinishQuestCommand.cs:122`），所以 63465 若确为任务奖励，后果 A/B 的可达性成立。

**档案侧的枚举我另派了一路只读复核**（仓库零改动，`git status` 前后同为 79 行；
未用 `ProjectReference`，改引已构建的 `Dfo.Pvf.dll`，避免在 `Server/src` 下写 obj/bin）。
七条声称**逐条为真**：

| 声称 | 复核所得 |
|---|---|
| `equipment.lst` 118479 条 | **118479** |
| Avatar 66328 / Creature 707 / Equipment 51444 | **完全一致，和 = 118479** |
| 66328 个时装声明 `[creation rate]` 者 = 0 | **0** |
| 707 个契约兽声明者 = 0 | **0** |
| 52151 中 15567 声明、6062 > 0、差 9505 | **完全一致** |
| 63465 → `equipment/creature/WolfLee.equ`，`[equipment type]` = `[creature]` | **正是如此** |
| 可掉落集合里时装 = 0 | **0** |

**而且复核比原声称更强，有两点原声称没做：**

1. **`equipment.lst` 里没有任何重复 id**（118479 条 → 118479 个不同 id，`TryAdd` 一条没丢）。
   所以 `ReadEquipmentPaths` 的「首条胜出」策略在这份档案上是**空转**的，
   它不可能把某个时装 id 顶掉而让时装数被低估。
2. **118479 条路径全部解析得到、全部读出 `[equipment type]`**。
   这一条要紧：`ResolveWorldClass` 在「文件找不到」与「无该标签」两种情况下都**兜底为
   `Equipment`**，若这两支常触发，Avatar 数就会被系统性低估。**实测两支触发 0 次。**
3. 复核还从**池子那一侧**独立重建了一次（按 `rate > 0 && grade >= 0 && rarity >= 0`）：
   **6062 行、6062 个不同 id、Avatar 0 / Creature 0 / Equipment 6062。**

**裁定：这组档案证据够格支撑「今天不可达」。** 加上我自己核到的
「`AddAsync` 恰好五处调用点、无第六条」与「闸门代码本轮未改」，
「不可达」这个结论是**查出来的**，不是推出来的。

## 对四个提问的答复

### 1. 13 条表是否逐条准确 —— 结论全对，但**支撑它的两句证据不成立**

表里每一格的结论我都核过，**没有一格是错的**（含我自己变异实测的 #2/#3/#5/#6，
以及我初评就直接核过判据的 #1/#4/#11/#12）。**但有两处必须改的文字错误，见 R-1、R-2。**

### 2. 档案侧穷尽够不够格 —— 够，且没有第六条通路

见上。补一条精度问题（R-4）：`ItemContainers.LandAsync` —— 也就是唯一把
`placement.ItemKind` 喂给 `StackableItemCore.Create` 的地方 —— 有**两个**入口：
`AddAsync`（:306）与 **`RefillAsync`（:321）**。表里「五条发放通路（都调 `AddAsync`）」
漏了 `RefillAsync`。**结论不受影响**：补给规则的六条真值早已被
`DailyRefill_RealArchiveProducesAuthoritativeRulesWithStackLimits` 钉死为 kind 4 的 stackable，
本文件更早的一节也已论证它「事实为空」。但那张表自称是**穷尽**，就该把这条入口也摆上。

### 3. 记为「潜伏缺陷」而不是「第三处行为变化」—— 这个定性我认

理由：契约意义上的「行为变化」要有**可达的输入**才成立；今天没有任何输入到得了它，
服务器对客户端的可观察行为就是没变。而且**记成潜伏缺陷比记成第三处变化更有用**——
它把「什么会让它开火」（档案内容或发放表变动）和「什么会让它消失」（修 8／1 那对不一致）
都写在了同一条里，而「第三处行为变化」这个标签两样都不说。
`AGENTS.md` 那条的位置也选对了：紧挨在它指认的根因（8／1 不一致）上面。

### 4. 我初评的非阻塞观察 —— 哪些必须现在做

**没有一条是阻塞的。** 分三档：

**建议本轮就做（都只是记一笔，与本轮刚做的 #4 属同一物种，且下一个成果会直接踩上）：**

- **O-3 契约兽八列在世界行被重建时不被抢救，而时装六列会。** 这是本轮**唯一**一条
  「生产代码里的潜伏不对称」，与刚记下的 #4 完全同类。下一个成果就是契约兽领域，
  它一开工就会创建契约兽世界行，届时这条从「不可达」变成「可达」。**现在记一行，
  比让下一个成果自己撞上便宜得多。**
- **O-4 `LandAsync` 没有时装唯一性守卫**，其安全性挂在 `Survivors` 三趟谓词都固定
  `core.Value` 上。与 O-1 同属「结论对，但撑着它的不是守卫」。
- **O-1**（`FillAsync` 与 `CarriedAvatarsAsync` 的耦合）**仍维持为建议**，不是要求。
  但既然要写 O-3、O-4，**三条并成一个小节写下最省事**，也最成体系。
- **O-9 `Docs/ItemWorldModel.md` 第七节注 1 的行号引用已过时**（它引的两行今天不是那个常量，
  且档案对契约兽现在给 5）。该文档是本变更集里的**新增文件**，且已带一条「更正 C-1」，
  再补一条是两行的事。**本仓反复栽的跟头就是记载与代码不一致，不该留一份自相矛盾的新文档进仓。**

**可以留给后续成果：**

- **O-5** `InventorySlotContent` / `InventoryItemState.Holds` 生产零消费——需要一次「删掉还是留着」的裁定，
  但它不误导任何人，SC-2 的实质由 `item_slots` 与 `ItemSlotRow` 承担。
- **O-7** `item_slots.count` 对堆叠物写了但不承重。
- **O-8** 字节锚是编码器层而非「存储→线上」整条链——补半格限定即可。

**不必再动：**

- **O-6**（`Server/tests` 那 5 处期望值改写）。我初评已判定它们实质上守住了契约那条红线，
  且每一处都能归到 RQ-005 / RQ-002 这个建模主体。**若要做，只做其中一件**：
  `ItemCarrierShapeTests.ItemsCarriesEveryConstraintItsSourcesDeclared` 的 `<remarks>`
  仍宣称「源表声明的每条约束都必须到达某处」，而时装唯一性这条今天无处可到——**那句注释与代码不符。**

## 顺带：一条与本成果无关但值得知道的仓库隐患

档案复核那一路顺手发现：`.gitignore` 第 26 行是 `*.pvf`，而这个模式会匹配**目录**
`Server/src/Dfo.Pvf`。已跟踪的 12 个文件不受影响（跟踪文件不看 gitignore），
但**今后在该目录下新建的任何文件都会对 `git add` 静默不可见**。
本成果没碰它，也不该由本成果修；**记在这里以免将来有人丢文件。**

## 必须改的文字错误（不需要再走一轮独立评审，正确的值我已算出）

### R-1 计数推导与自己写的口径对不上（总数 13 是对的）

节里的口径写的是「`+` 行里的 **`throw` 语句**」，而算式写的是「共 14 行新增 `throw` 减 1 条改写 = 13」。
**按写下的口径复算是 18 行，不是 14**（多出的 4 行是协议层
`GameInventoryItemEntry` 与 `StateSynchronizationPayloadCodec` 里那四条
`throw Missing(...)`，它们同样是「HEAD 上已存在、只改了消息文本」的改写）。
**两种读法殊途同归**：`throw new` 口径 14 − 1 = 13；`throw` 口径 18 − 5 = 13。
**改法二选一**：要么把口径写成「`throw new` 语句」，要么把算式改成「18 行减 5 条改写 = 13」
并把那 4 条协议层改写与 `is stale or missing` 并列点名。**总数 13 不动。**

### R-2 「恰好 2 条红」这句不成立 —— 正确答案是 3 条

节里写「把下表**前五条**的异常类型改成 `InvalidTimeZoneException` 跑全量：**恰好 2 条红**」。
**前五条包含 #5**，而 #5 的判据
`EntrySaveReconciliationTests.ASaveNamingOneThingInTwoPositionsIsRefusedAndChangesNothing`
断言的正是 `Assert.ThrowsAsync<InvalidDataException>`——改掉异常类型它必红。
**我实测过**：单独变异 #2/#3/#5/#6 四条，`Dfo.Server.Persistence.Tests` **红 1 条，正是 #5**。
所以变异 #1..#5 的正确结果是 **3 条红**（#1、#4、#5 各一条），不是 2 条。

**这一处必须改，而且它出现的位置很不巧**：正是那段宣称「判据有无是变异实测出来的，
不是搜消息串出来的」的文字。**表里的结论没错，错的是那句自证。**
建议改成：变异 #1..#5 → 3 条红（列出三条判据名）；或改成我实际做的那次
（变异 #2/#3/#5/#6 → 1 条红），并注明 #1/#4 的判据由直接阅读断言确认。

### R-3 文件开头第 5 行仍写着「清单之外没有其它行为变化。」

第 158 行的订正说「那句断言不成立」，而**第 5 行原话还在**，同一份文件里前后打架。
更要紧的是开头那段的**范围陈述**——「下面逐条列出这两处本身、以及每一处的**全部连带后果**」
——描述的仍是一份只讲两处授权变化的文档；**那 13 条拒绝不是 RQ-009／RQ-010 的连带后果**，
它们是独立的。读者读完前 5 行不会知道下面还有一节。
**改法**：开头点名新增那一节，并把「清单之外没有其它行为变化」改成
能同时涵盖「两处授权变化 ＋ 13 条新增拒绝」的说法。

### R-4 「五条发放通路」应补上 `RefillAsync`

见上文第 2 问。一句话的事，结论不变。

## 复评结论

**F-1 的实质已经解决，而且做得比我要求的多。**

- (a) 那一节不但存在，还从我列的 6 条扩到 **13 条**，每条带**可达性**与**判据有无**两栏
  ——比我的表多出一个维度。我逐格核过，**没有一格结论是错的**；
  其中 #7/#8 的归属**比我初评时更准**（我把它们记错到了 `InsertOwnAsync` 名下）。
- (b) 那条唯一「原本成功 → 现在抛错」的拒绝，取的是我给的第三条路，
  并且档案证据经我另一路独立枚举**逐条为真**，还额外堵上了两个我都没想到要问的口子
  （重复 id、兜底分支触发次数）。定性为**潜伏缺陷**我认，理由见第 3 问。
- (c) 两处异常类型变化并进同一节、同一种记法、不升格为授权变化 —— 正是我建议的做法。
- 附带那条「原核法**结构上**看不见新增拒绝，因为它只顺着两个符号往下游追，
  而全量判据全绿**恰恰**意味着没有用例走到新拒绝上」——**这是整份记载里最有价值的一句**，
  它把一次具体的遗漏变成了一条后来者能用的方法。

**生产代码一行未改**（三个文件 sha256 我自己算过，与初评一致），
**全量判据我自己重跑 2216 / 2 / 0**，**我自己做的变异实测支持表里每一格结论**。

剩下的 R-1..R-4 全是**记载文字**层面的错，没有一条改变任何结论，
且正确的值我在上面都算出来了，控制器可以机械地核对每一处。
**因此改为 `approved`：R-1..R-4 必须在接受提交前改掉，但不需要再走一轮独立评审。**

**一句话总评**：这次整改把一个「记载漏了东西」的问题，变成了一份**比我要求的更严的记载**，
还顺带查实了授权变化二的可达实例（63465）。它自己新引入的三处文字错误
（R-1/R-2/R-3）都属同一个老毛病——**写下一个没有当场复算的数**——
但这一次，错的是自证的算式，不是结论。

---

## 控制器处置（非评审内容，由控制器记录）

复评 `approved`。评审点名「不需再走一轮评审、正确值已算出」的四处文字错误，以及它建议本轮做的
三条观察，**全部已做**：

| 项 | 处置 |
|---|---|
| R-1 计数口径「`throw` 语句」与算式不符 | 改为 **`throw new`**（含 `? throw new` 与 `_ => throw new` 两种表达式形式），算式仍为 14 − 1 = 13 |
| R-2 「前五条变异 → 恰好 2 条红」不成立 | **已改，但没有照抄「3 条红」**：实际变异的是第 1／2／3／4／6 条**而非表的前五行**，对那次实验「2 条红」是准的。照抄会让记载描述一次从未做过的实验。现在两次变异（执行方的与复评自己做的）并列写出，合起来盖住全部存疑格；并留了一句「一句写在『我用变异不用 grep』旁边的自证，自己却没被核对过」 |
| R-3 文件第 5 行原话仍在，与订正打架 | 已改开头：明说本文件分两部分，并点名「清单之外没有其它行为变化。」这句是假的、由 F-1 查出 |
| R-4 `LandAsync` 漏了 `RefillAsync` 这个入口 | 已补，并写明它的 kind 来自 `refill.ItemKind`、既有判据把六条补给规则钉死为 kind 4，送不出时装 |
| O-1／O-3／O-4 | **三条并成一段 `<remarks>`** 写在 `SqliteItemDomainStore.CarriedAvatarsAsync` 上（「三件今天成立、但理由住在别的方法里的事」）：守卫等价性挂在 `CarriedAvatarsAsync` 的过滤上；`LandAsync` 无守卫而安全是因为 `Survivors` 三趟都固定 `core.Value`；**时装六列有抢救、契约兽八列没有**，并点名下一个成果的人就是会踩到它的人 |
| O-9 | `Docs/ItemWorldModel.md` 追加**更正 C-2**：那条注对契约兽已不成立（正确的例子是时装）、行号引用已失效，并留作「按内容索引不写行号」这条纪律的反面教材 |

O-5／O-7／O-8 按评审意见留给后续成果；O-6 评审已判定实质守住了契约红线，不阻塞。

改动只涉及 `SqliteItemDomainStore.cs` 的**注释**、`behavior-change-list.md`、`Docs/ItemWorldModel.md`。
全量判据重跑：**2216 / 2 / 0**，退出码 0。

**另记一条与本成果无关的仓库隐患**（评审附带发现，**本成果不修**）：
`.gitignore:26` 的 `*.pvf` 会匹配**目录** `Server/src/Dfo.Pvf`。已实测坐实：本仓
`core.ignorecase = true`，在该目录下新建文件后 `git check-ignore -v` 命中该行、
`git status --porcelain` **无输出**——**今后在这个项目里新建的文件会对 `git add` 静默不可见**。
已跟踪的文件不受影响。探针文件已删除。**这条要单独处理，需所有者裁定。**
