verdict: passed
kind: rks-review

# 独立评审：spec-001 Item rules in the bag

评审对象：工作树 `domain-instances-and-snapshots` 的全部未提交改动（`git diff` 的 19 个文件 + 4 项未跟踪），接受基线 `531c0bb`。评审在全新上下文中进行，不采信执行者的自查结论。

结论：**changes_required**。一条 A 级发现：每日补充改走背包后，入场路径在**同一个事务内**重载库存时读不到背包的写入；新开格子的日子里整个入场会抛异常失败，并堆的日子里下发给客户端的是补充前的数量。现有全量测试全绿，是因为该路径的用例夹具 `MutationsPerCall` 每次调用各开一个事务，恰好绕开了真实命令的事务形状。

---

## 一、我实际执行过的独立实验

全部实验都在本工作树内真改真跑。每次改坏后都从改前的副本复原，并用 `sha256sum` 与评审开始时记录的基线逐一比对；最终工作树的文件集合与哈希与评审开始时完全一致（见本节末）。

环境：`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`，测试一律 `-m:1`。

### 基线复核（未改任何东西）

| # | 做了什么 | 结果 |
|---|---|---|
| B1 | `dotnet build Server/86JP.Server.slnx` | **0 警告 0 错误** |
| B2 | `dotnet test Server/86JP.Server.slnx -m:1` | **1147 通过 / 2 跳过 / 0 失败**（Pvf 60 + Application 507 + Host 4 + Infrastructure 100/2skip + Networking 46 + Persistence 227 + Protocol 203）。与执行者声称的数字一致；`Host.Tests` 本次满载下没有出现假红 |
| B3 | 三份整记录表征用例 | `Characters/EntryLoadCharacterizationTests.cs`、`Accounts/AccountEntryLoadCharacterizationTests.cs`、`Inventory/InventoryLoadCharacterizationTests.cs` **都不在改动集里**，一个字节未动 |

评审全部实验做完之后又跑了一遍 B1/B2，数字相同（0 警告 0 错误；1147/2/0），确认工作树已复原且仍是绿的。

### 探针实验（新写临时用例、跑完即删）

四条临时用例写在 `Server/tests/Dfo.Server.Persistence.Tests/Inventory/` 下，跑完已删除，`git status` 已确认无残留。

| # | 构造 | 期望 | 实际 |
|---|---|---|---|
| P1 | 一个 use case 内：`DailyRefillService.ApplyDailyRefillAsync`（新开格子）→ 紧接着 `InventoryEntryAssembly.LoadCharacterAsync` | 重载看到 1 件 | **看到 0 件**（grants=1，items=0） |
| P2 | 同上，但预置 slot 65 有 3 个、补充到 5 | 重载看到 5 | **看到 3** |
| P3 | 对照组：同一个 use case 内改用 `ICharacterItemTable.InsertOwnAsync`（即改动前那类直写）再重载 | 重载看到 1 件 | **看到 1 件，通过** |
| P4 | 复刻 `SelectCharacterEntryInventoryDomainService.ResolveCommittedItems` 的那次查找：`CharacterInventoryEntryDomainService.LoadAsync` 之后 `container.Items.SingleOrDefault(i => i.SlotIndex == grantedSlot)` | 非 null | **null** |

P1/P2/P4 红、P3 绿，三者合起来把 A-1 的机制与后果都钉死了：不是「读不到」这件事碰巧发生，而是**背包的写入被暂存在 `SqliteItemStoreSession` 里、要到事务 prepare 才落库**，而改动前那两个表方法是立即执行 SQL 的。

### 故意改坏实验（改生产代码 → 跑 → 复原 → 校验哈希）

**失败语义三处互换：**

| # | 改坏了什么 | 变红的 | 判读 |
|---|---|---|---|
| E1a | `DailyRefillService.cs` 里 `NoRoom` 从**抛**改成**静默 return**（换成教程奖励的反应） | 仅 `SqliteEntryInventoryMutationRepositoryTests.DailyRefill_RefusesWhenTheBandIsFull`（227 中 1 红） | 每日补充「遇满抛」有独立守卫 |
| E1b | `GrantTutorialRewardCommand.cs` 里 `NoRoom`/`UnknownItem` 从**静默跳过**改成**抛**（换成每日补充的反应） | `TutorialRewardPlacementTests.An_item_whose_band_is_full_is_left_out_and_the_rest_of_the_step_proceeds`、`An_item_the_archive_declares_nothing_for_is_left_out_and_the_rest_proceeds`（507 中 2 红）；每日补充用例不受影响 | 教程奖励「遇满静默跳过并排除在回执外」有独立守卫 |
| E1c | `MoveItemSpaceSlice.cs` 里 `Refused` 从**翻成拒绝码**改成**答 `Unchanged`** | Application 5 条（`Two_empty_slots…`、`A_slot_outside…`、`A_merge_past…`、`A_swap_whose_other_end…`、`Dragging_part_of_a_pile…`）+ Protocol 1 条（`ItemPathFrameTests.ARefusedMoveOfARealRowAcknowledgesTheSameBytesAsBefore`） | 移动「遇拒返回拒绝码」有独立守卫 |

三处互不牵连：E1a 只红补充，E1b 只红教程奖励，E1c 只红移动。**三处失败语义确实各自独立。**

**两条到货规则是否会互相顶替：**

| # | 改坏了什么 | 变红的 |
|---|---|---|
| E2 | `RefillAsync` 改成调 `arrivals.ForGrant` | `BagPlacementVerbTests.Adding_and_refilling_disagree_about_a_pile_that_is_already_full` |
| E2b | `AddAsync` 改成调 `arrivals.ForDailyRefill` | 上面那条 + `Adding_answers_that_there_is_no_room_rather_than_spilling_out_of_the_band` + `TutorialRewardPlacementTests.A_grant_larger_than_the_pile_can_take_places_only_what_fits` |

**两条规则不可互换，双向都有守卫。**

**移动的五种拒绝与两种非拒绝逐条验证。** 每次只改坏一处判定，并且是让它**不再生效**，而不是换个拒绝原因名——因为 `ErrorCodeFor` 把 `ItemCannotOccupySlot` / `PartialSwapNotAllowed` / `DisplacedCannotOccupySlot` 三者都映射到同一个客户端码 `NotMovable`，只换名字不会红：

| # | 改坏了什么 | 变红的 | 数量 |
|---|---|---|---|
| E3a | `BothSlotsEmpty` 改成答 `Unchanged` | `Two_empty_slots_are_refused_without_a_message` | 恰好 1 |
| E3b | 去掉 `!slotRules.CanOccupyMainSlot(placement, targetSlot)` 这半个条件 | `A_slot_outside_the_items_band_is_refused` | 恰好 1 |
| E3c | 合堆条件恒真（不再看叠加上限） | `A_merge_past_the_stack_limit_says_the_pile_is_full` | 恰好 1 |
| E3d | `moveCount != moving.Count` 这条拒绝恒不生效 | `Dragging_part_of_a_pile_onto_another_item_will_not_swap` | 恰好 1 |
| E3e | 去掉 `!slotRules.CanOccupyMainSlot(displacedPlacement, moving.SlotIndex)` 这半个条件 | `A_swap_whose_other_end_cannot_land_says_the_item_will_not_move` | 恰好 1 |
| E3g | `fromSlotIndex == toSlotIndex` 这条恒不生效 | `Naming_one_slot_twice_succeeds_without_moving_anything` | 恰好 1 |
| E3f | `Moved(plan.MoveCount, moving.ItemId)` 改成 `moving.ItemId + 1` | **什么都没红**（Application 507 全绿、Protocol 203 全绿） | 0 → 见 B-2 |
| E6 | `ContainerMoveKind.Split` 一律改判为 `Relocate` | **什么都没红**（Application 507 全绿、Protocol 203 全绿） | 0 → 见 B-3 |

执行者「其余四条是按已有测试断言的是错误码而非状态名推断的」这个推断，**逐条实测成立**：五种拒绝各自恰好红一条，且都是被点名的那一条。两种非拒绝里 `Unchanged` 成立；`Moved` 只有「实际移动的数量」有守卫，「物品编号」没有（B-2）。

**其余两处：**

| # | 改坏了什么 | 变红的 |
|---|---|---|
| E4 | `AddAsync` 改成在 use case 之外的一个新事务里读 `ReadAllAsync`（模拟「后件看不见前件」） | `TutorialRewardPlacementTests.The_second_of_two_declarations_sees_what_the_first_wrote` + `ExitFlushHandlerTests.TaggedIdReport_SettlesEveryItemOfOneStepTogether` |
| E5 | 删掉 `LandAsync` 里 `itemKind == 0` 的抛出 | `SqliteEntryInventoryMutationRepositoryTests.DailyRefill_RefusesToOpenASlotForARuleWithNoItemKindButStillJoinsAPile` |

合计：4 次探针 + 15 次故意改坏，其中 13 次红且只红被点名的那条，2 次暴露出无守卫。

### 复原核验

每次实验后都用改前副本覆盖并核验哈希。最终状态（与评审开始时逐字节一致）：

```
4277d45f2574d888e17ca6f1680ea36b1b46a58fb917cba1051c6c517263a09a  ItemContainers.cs
3efed29d45d13da2103fe2c366e3e01e74579fcf9fad12833f8dad19c27805b4  DailyRefillService.cs
0d93995e62e638d6ee9ecb772832652a82b9b8625f4a8a88ef9ff8e0f5d8e55c  GrantTutorialRewardCommand.cs
4a56c39434387e0a97b1491440806acddbf2894976fa0ca136b8db232c97ec00  MoveItemSpaceSlice.cs
4d8063ab6fb67b18e96eb0d17e93ee6581f6592d7ddbd93673e21f5581c6cd1d  ItemArrivalRuleService.cs
```

`git status --porcelain` 的文件集合与评审开始时完全相同（19 个 M + 4 个 ??）。未提交、未合并、未清理工作树。

**必须披露的一次操作失误。** 做 E1a 的复原时我误用了 `git checkout -- Server/src/Dfo.Server.Application/Inventory/DailyRefillService.cs`——它恢复的是**索引里的 531c0bb 版本**，把执行者未提交的改动抹掉了。我立刻用评审开始时捕获的完整 `git diff` 把该文件重建，重建后 sha256 = `3efed29d…`，与评审开始时记录的基线**逐字节一致**（`git diff --stat` 同样是 37 插入 / 44 删除 / 81 行）。此后所有复原都改用「改前副本覆盖 + 哈希比对」。该文件当前内容与评审开始时相同，无残留影响。

---

## 二、发现

### A 级（阻塞）

#### A-1　每日补充改走背包后，入场路径在同一事务内重载时读不到补充的写入

**落点**

- `Server/src/Dfo.Server.Application/Inventory/DailyRefillService.cs:104-106`——`await bag.RefillAsync(...)`，写入经由 `IItemStoreSession`。
- `Server/src/Dfo.Server.Application/Inventory/SelectCharacterEntryInventoryDomainService.cs:87-104`——`mutations.ApplyDailyRefillAsync(...)` 之后紧接着 `inventories.LoadAsync(...)`，再由 `ResolveCommittedItems` 在 `:125-128` 按落点查找。
- 机制方：`Server/src/Dfo.Server.Persistence/Inventory/SqliteItemStore.cs:61-67,137-151,160-183`（`Write` 只写进 `staged`，`FlushAsync` 由 prepare 调用）与 `Server/src/Dfo.Server.Persistence/Database/SqliteDatabaseParticipant.cs:117-135`（`PrepareAsync` 才 flush facet）。
- 对照方：`Server/src/Dfo.Server.Persistence/Database/SqliteDatabase.cs:57-73,80-95`——表类的读写都是**立即**在同一连接同一事务上执行语句。

**为什么是回归**

改动前 `DailyRefillService.GrantAsync` 用 `ICharacterItemTable.InsertStackAsync` / `SetStackCountAsync`，两者经 `SqliteDatabase.ExecuteAsync` **立即执行 SQL**，所以同一事务内随后的表读（`InventoryEntryAssembly.LoadCharacterAsync`）能看到。改动后写入走 `IItemStoreSession`，被暂存到事务 prepare 才落库，而重载走的仍是表类的原始 SQL，于是看不到。

**可复现的构造方式**（P1/P2/P4，已实测）

在一个 use case（`DomainTransactionScope.Begin()`）内依次调用 `DailyRefillService.ApplyDailyRefillAsync` 与 `InventoryEntryAssembly.LoadCharacterAsync`：

- 补充新开格子：`committed.Grants.Count == 1`，而重载 `CharacterItems.Count == 0`；复刻 `ResolveCommittedItems` 的那次 `SingleOrDefault` 返回 **null**。
- 补充并入已有堆（预置 3 个、补到 5）：重载读回的仍是 **3**。

同样的构造改用 `ICharacterItemTable.InsertOwnAsync` 直写（P3）则重载能看到——排除了「读路径本身有问题」这个解释。

**真实路径上的后果**

`ApplySelectedCharacterDailyRefillCommand` 是 `CommandExecutionKind.Transactional`（`ProtocolModule.cs:120-122`），补充与重载都在这**一个**事务里（`SelectCharacterPhaseCommands.cs:183-192` → `SelectCharacterEntryInventoryDomainService:87-104`）。

- **新开格子的日子**：`ResolveCommittedItems` 抛 `InvalidDataException("Committed inventory slot Main/<slot> was not reloaded.")`，异常穿过 `commandBus.SendAsync`（`SelectCharacterHandler.cs:245-247`），**整个选人入场失败并回滚**；因为计数器也一并回滚，下次入场会再走一遍同样的失败。这是玩家「进不去游戏」级别的后果。
- **并入已有堆的日子**：不抛，但 `InventoryDeltaMessage` 与 `leases.Replace` 拿到的都是**补充前**的数量，客户端被告知了错的数字。

需要分清判据宽窄：「重载看不到写入」是构造上确定的（P1/P2/P4 直接测出）；「入场因此抛异常失败」是由 `ResolveCommittedItems:125-128` 那两行确定性代码推出的——P4 已经把该处的输入（`SingleOrDefault` 返回 null）实测出来了，剩下的只是那一行 `?? throw`。

**为什么现有全量测试没红**

`SqliteEntryInventoryMutationRepositoryTests` 一律用 `InventorySide.MutationsPerCall`（`InventorySide.cs:100-101,126-143`），它给**每一次调用**各开一个事务并当场 commit，所以补充在 flush 之后才被读。而真实命令是「补充 + 重载」共用一个事务。这正是「判据比结论窄」的典型：改动前两种夹具形状等价，改动后不再等价，而用例只覆盖了等价的那一种。

**A 而非 B 的理由**：行为回归，落在主入场路径上，后果之一是整条入场失败；且没有任何用例守着它，只能靠事后实机发现。

### B 级（观察与建议）

#### B-1　移动的「两端同格」判定挪进背包后，与「没有绑定容器」的先后次序反了

`Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs:230-244`。改动前次序是「列表判断 → **两端同格 → Unchanged** → 容器为 null → NoBoundContainer」；改动后两端同格的判定搬进了 `StoreBackedItemContainer.MoveToAsync`（`ItemContainers.cs:368-372`），于是变成「列表判断 → 容器为 null → NoBoundContainer → 两端同格」。对一个没有绑定容器的角色发同格移动，改动前答成功（`Unchanged`，无错误码），改动后答 `NoBoundContainer` → `Silent`，帧字节不同。

判 B 而非 A：`BindContainers`（`SelectCharacterEntryInventoryDomainService.cs:56-74`）在入场时对没有主容器的角色直接抛，所以正常会话里 `FindContainer` 不会返回 null；这条差异在实机上构造不出来。plan T-005 明确授权了「两端同格可迁可留」，但没有记下次序变化这个副作用——建议在设计文档或注释里记一笔。

#### B-2　`ItemMoveOutcome.Moved` 携带的物品编号没有任何守卫

`Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs:262-265`。实测 E3f：把 `moving.ItemId` 改成 `moving.ItemId + 1`，Application 507 与 Protocol 203 全绿。原因是 `result.ItemId` 只进日志（`MoveItemSpaceSlice.cs:331-339`），不进帧。规格 RQ-3 写的是「搬成了（携带实际移动的数量与物品编号）」，数量有守卫、编号没有。判 B：它不进对外帧，错了也只污染日志。

#### B-3　`ContainerMoveKind.Split` 在全仓没有任何用例

实测 E6：把「部分拖到空格」一律改判为整堆 `Relocate`，Application 与 Protocol 全绿。四种执行方式里 `Relocate`（`An_ordinary_drag_moves_the_named_source`）、`Swap`（`Two_items_trade_places_when_the_target_is_taken`）、`Merge`（`Two_piles_of_one_item_merge`）各有用例，唯独 `Split` 没有。

判 B：这不是本轮搬丢的——改动前 `PlanAsync` 里同一段判断同样没有守卫，代码逐字搬过来没有变；规格 AC-3 点名的七条都齐了，`Split` 不在那七条里，所以不构成违约。但本成果的主题正是「把这段判断搬家」，而这条分支搬完仍然没人看着。建议补一条（拖 2 个到空格 → 源格剩 3、目标格 2）。

#### B-4　`itemKind == 0` 的抛出从「只管每日补充」扩到了「也管授予」——可接受，但建议记进文档

`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:331-338`。这一条现在写在两个动词共用的 `LandAsync` 里，所以 `AddAsync`（教程奖励）开新格时也会抛。改动前教程奖励在这种情况下会写下一行 `item_kind = 0` 的记录。

我核实了可达性：`PvfItemPlacementDefinitionSource.GetAsync`（`:37-47`）只在 `metadata.SlotEnd >= metadata.SlotStart` 时才返回非 null；`PvfEntryLifecycleContentSource.ResolveStackableItemKind` 对任何 stackable 至少返回 2，装备返回 1，其余走 `RefillItemMetadata(0, 0, 0, -1)` → placement 为 null → `UnknownItem`。**生产里非 null 的 placement 其 ItemKind 不可能为 0**，所以这条扩大的抛出在授予路径上不可达，而且方向是更安全的（旧行为是写下一行入场路径读不回来的记录）。

关于评审请求点名的那处「明确改变了的行为」：异常文本从 `Daily refill item {id} has no authoritative PVF item kind.` 变成 `Item {id} has no authoritative PVF item kind, so no slot can be opened for it.`，类型仍是 `InvalidDataException`、时机仍在写入之前、效果仍是整个入场失败，且新增的可达面为空。**这个改变可接受。** 但**需要记进文档**：它现在是背包的不变量而不再是每日补充的不变量，`Docs/Refactor/DomainInstanceDesign.md` 的「Settled by the item-side implementation」一节应当补一句，否则下一个读者会以为它仍然只属于补充。

#### B-5　判定用的槽位带一律按主列表问，与容器自身的类型无关

`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:437-441,478-481` 无条件调用 `slotRules.CanOccupyMainSlot(...)`，而 `StoreBackedItemContainer` 本身持有 `shape.ContainerType`。今天只有 Main 会被注册，`DecideMoveAsync` 的注释也把这一点说清了，所以不是缺陷；但判定从「只服务主列表的协议处理器」搬进「任意类型的容器」之后，这条隐含前提从结构上成立降级成了靠注释成立。判 B。

#### B-6　设计文档自己预告的那条陈旧注释，在文件被动过之后仍然陈旧

`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:207-211` 仍写着 “Both of the ones below are this class with a different store behind them”，而下面只剩 `PersistentItemContainer` 一个。`Docs/Refactor/DomainInstanceDesign.md` 的「What this rests on」一节明确写了 “Worth correcting when the file is next touched”。这一轮正是 next touched。判 B（不影响行为）。

#### B-7　夹具里 `null!` 依赖大量增加

`PersistentItemContainer` 的构造参数从 5 个涨到 9 个，五处夹具用 `null!` 填空：`ItemUseFixtures.cs:195-204`、`PersistentContainerBoundaryTests.cs:86-95`、`ContainerInventoryProjectionTests.cs:119-129`、`InventorySide.cs:74-87`（三个 `null!`）、`SelectCharacterEntryLifecycleServiceTests.cs`（四处 `null!, null!, null!`）。`InventorySide.BindMain` 传 `null!` 的 placements 意味着补充用例跑的是一个「一旦查摆放就 NRE」的容器——今天补充确实不查，但这条前提只写在注释里。判 B。

#### B-8　`ItemPathLiveTests.cs` 新增的 using 插在了非字母序的位置

`Server/tests/Dfo.Server.Protocol.Tests/Game/ItemPathLiveTests.cs:9` 把 `using Dfo.Server.Abstractions.Content;` 放在 `using Dfo.Server.Application;` 之后。纯风格，构建 0 警告。判 B。

#### B-9　三处 `ArgumentOutOfRangeException` 的 paramName 与实际传入的值不对应

`DailyRefillService.cs:130-131`（`nameof(rule)` 却传 `outcome`）、`GrantTutorialRewardCommand.cs:76-77`（`nameof(command)` 却传 `outcome`）、`MoveItemSpaceSlice.cs:256-257`（`nameof(command)` 却传 `outcome`）。都是 default 分支的不可达路径，判 B。

---

## 三、契约成功条件逐条核对（SC-A1..SC-A8）

| ID | 判定 | 证据 |
|---|---|---|
| SC-A1 | **成立** | `grep -rn "ItemArrival" Server/src`：分支落地（`case ItemArrival.*`）只出现在 `ItemContainers.cs:322/331/345` 这一个 switch 里，其余命中都是类型定义（`IItemArrivalRules.cs`）、规则服务自身的构造（`ItemArrivalRuleService.cs`）与依赖注入声明。教程奖励经 `AddAsync`、每日补充经 `RefillAsync`，两者都汇到 `LandAsync` |
| SC-A2 | **成立** | `grep -rn "IItemPlacementDefinitionSource\|IItemArrivalRuleService\|IItemSlotRuleService\|ItemPlacementDefinitionSource\|ItemArrivalRuleService\|ItemSlotRuleService" Server/src/Dfo.Server.Protocol` **零命中**。`MoveItemSpaceCommandHandler` 现在只剩：会话检查、主列表判断、容器绑定检查、一次 `MoveToAsync`、一个 `StatusFor` 翻译 |
| SC-A3 | **不成立** | 单元层面三处都有守卫（E1a/E1b/E1c 实测互不牵连）。但每日补充这一处**在入场边界上的对外行为变了**：见 A-1。契约 C-1 写的是「三处调用方的对外行为一字不变，含各自的失败语义」，这一条不满足 |
| SC-A4 | **成立** | 五种拒绝逐条改坏各恰好红一条（E3a/E3b/E3c/E3d/E3e）。构造方式与今天等价：`ItemMoveStatus` 十个值一个不减，`ErrorCodeFor` 未改（`git diff` 未触及） |
| SC-A5 | **成立** | 独立复核了契约里的假设，两半都站得住。①`grep -rn "StorageEntryId" Server/src/Dfo.Server.Protocol` 只有 5 处，全部在 `throw Missing($"…[{item.StorageEntryId}]")` 的异常字符串里（`StateSynchronizationPayloadCodec.Entry.cs:157/161/201/795`、`.Inventory.cs:200`）；`item_uid` 在 Protocol 工程零命中。②`InventoryEntryServices.cs:167/184` 的 `.ThenBy(item => item.StorageEntryId)` 主键是 `SlotIndex`，而槽位在一个容器内唯一有**两重保障**：DDL 上 `CharacterDatabaseSchema.cs:165` 的 `items` 表带 `UNIQUE(owner_scope, owner_id, list_type, slot_index)`；代码上 `InventoryEntryServices.ValidateItems` 对重复的 `(ContainerType, SlotIndex)` 直接抛。故该备选键确实不可触发。用例 `DailyRefill_LeavesTheProjectionAloneEvenIfTheRowsSurrogateKeyMoves` 断言了 `NotEqual(keyBefore, keyAfter)`、投影不变、以及槽位唯一性本身 |
| SC-A6 | **成立** | B1/B2：构建 0 警告 0 错误；全量 1147/2/0（评审前后各跑一次，数字相同） |
| SC-A7 | **成立** | `IItemContainer.cs:86-101` 与 `ItemContainers.cs:74-85` 两处注释都已重写，都点名 `ItemContainerOperations` 仍不判定并给出「拼装从来不需要存储知情」这个理由；旧注释里「其下的存储也要被告知」那句已删。`Docs/Refactor/DomainInstanceDesign.md` 新增「Settled by the item-side implementation」一节，记了四条修正与 R-2 未被触及。另见 B-4 / B-6 两条补充建议 |
| SC-A8 | **成立** | 执行者做了 6 次；我另做了 15 次独立的故意改坏，其中 13 次红且只红被点名的那条，2 次暴露出无守卫（B-2、B-3） |

---

## 四、规格验收逐条核对（AC-1..AC-10）

| ID | 判定 | 证据 |
|---|---|---|
| AC-1 | **成立** | `BagPlacementVerbTests` 四条：`Adding_joins_a_pile_that_has_room_and_says_how_much_it_took`（断言 SlotIndex/Added/Total 三个数，4 < 7）、`Adding_with_no_pile_opens_the_first_free_slot_of_the_band`、`Adding_answers_that_there_is_no_room…`、`Adding_something_the_archive_declares_nothing_for_writes_nothing`。占空格一例只断言两个数，是因为 `PutInEmptySlot` 只带两个字段——「两者相等」由类型结构保证而非漏断言；`DailyRefill_CommitsGrantAndClaimTogetherAndSecondRunIsNoOp` 另外断言了 GrantedCount == CommittedCount == 5 |
| AC-2 | **成立** | `Adding_and_refilling_disagree_about_a_pile_that_is_already_full`：同样持有 10、同样给 5，授予开 66 格、补充并到 65 格成 15。E2/E2b 双向证伪 |
| AC-3 | **成立** | 七条齐备且逐条可构造（E3a-E3e、E3g，以及 `Moved` 的数量部分）；另有 `The_reversed_request_moves_the_potion_onto_the_bar` 覆盖来源格为空时的两端对调。`Moved` 的物品编号无守卫（B-2）、`Split` 无用例（B-3），但都不在这七条里 |
| AC-4 | **成立** | 同 SC-A1 |
| AC-5 | **成立** | 同 SC-A2 |
| AC-6 | **不成立** | 九个格子在**单元层面**逐条有用例（教程奖励三条 + `The_second_of_two_declarations_sees_what_the_first_wrote`；每日补充成功 / 遇满抛 / 类别 0 抛三条；移动成功与拒绝两条）。但 RQ-6 要求的是「改动后必须仍然成立」的对外行为，每日补充在入场边界上不成立——见 A-1 |
| AC-7 | **成立** | `DailyRefill_LeavesTheProjectionAloneEvenIfTheRowsSurrogateKeyMoves` 断言 `keyBefore != keyAfter`、投影里 slot 65 / item 1234 / count 5、行数为 1、槽位去重后数量不变。注释里也写清了代理键在协议侧只出现于异常字符串与那个不可触发的排序备选键 |
| AC-8 | **成立** | 同 SC-A7 |
| AC-9 | **成立** | 同 SC-A6 |
| AC-10 | **成立** | 执行者 6 次 + 我 15 次 |

### 项目约束核对

| 约束 | 判定 | 证据 |
|---|---|---|
| 三份整记录表征用例期望值不改 | **满足** | 三个文件都不在 `git status` 的改动集里 |
| `SqliteEntryInventoryMutationRepositoryTests` 原有 9 条期望值不改 | **满足** | `git diff` 里对这 9 条的改动只有 `var repository = new InventorySide(path).MutationsPerCall;` → `var side = …; side.BindMain(…); var repository = side.MutationsPerCall;`，没有一个 `Assert` 被动过 |
| `ItemContainerOperations` 不得获得判定职责 | **满足** | 该类方法体 `git diff` 零改动，仍然只有 clear/put |
| 不改持久层形状；背包不自建表不自持数据库 | **满足** | `CharacterDatabaseSchema.cs` 未改；容器仍经 `IItemStore` + 环境事务。删掉的四个表方法与 `MainStackRow` 是**减少**表方法，不是形状变化 |
| 不为三个动词另开 `canX` | **满足** | `IItemContainer` 上只有 `AddAsync` / `RefillAsync` / `MoveToAsync`，没有并行查询 |
| CRLF、无 BOM、net10.0、`TreatWarningsAsErrors` | **满足** | 新增两个测试文件与改动的源文件首字节均为 `75 73 69`（`usi`，无 BOM），`file` 报 CRLF；构建 0 警告 |
| 不动 `Tool/` 与 `.worktrees/legacy-reference/` | **满足** | 不在改动集里 |
| 契约 Out（跨容器移动、人物实例、规则服务身份拆分、快照重塑、客户端错误码） | **满足，无越界** | 非主列表仍按 `ListNotServed` 拒绝；无人物实例类型新增；`IItemSlotRuleService` / `IItemArrivalRuleService` / `IItemPlacementDefinitionSource` 的签名 `git diff` 零改动；`SnapshotAsync` 未改；`ItemMoveStatus` 仍是 10 个值、`ErrorCodeFor` 未改。改动集里 19 个 M 文件与 4 项未跟踪逐一看过，都落在 In 一节内 |

### 「搬运时丢掉了一条只活在代码里的规则」专项核对

逐条对照 `git diff` 里被删掉的每一段：

| 被删掉的 | 新落点 | 判定 |
|---|---|---|
| `MoveItemSpaceCommandHandler.PlanAsync` 的全部五条判定 | `StoreBackedItemContainer.DecideMoveAsync`（`ItemContainers.cs:430-482`） | 逐字对应，E3a-E3e 各自实测可构造 |
| `PlanAsync` 里 `requestedCount <= 0 \|\| requestedCount > moving.Count` 的整堆读法 | `ItemContainers.cs:443-445` | 一致 |
| 处理器里的「来源格为空则两端对调」 | `ItemContainers.cs:374-377` | 一致，`The_reversed_request…` 守着 |
| 处理器里的「两端同格 → Unchanged」 | `ItemContainers.cs:368-372` | 语义保留，但相对「容器为 null」的次序反了 → B-1 |
| `DailyRefillService` 里 `rule.ItemKind == 0` 的抛出 | `LandAsync`（`ItemContainers.cs:331-338`） | 保留，时机仍在写入之前，异常文本变了 → B-4 |
| `DailyRefillService` 里两处 `!= 1` 的写入行数校验（`Daily refill item insert/update failed.`） | **无对应物** | 判为可接受：`IItemStoreSession` 是「写一个槽位」的语义，删+插由 `FlushAsync` 保证（`SqliteItemStore.cs:160-183`），没有「影响 0 行」这个概念可查；`DailyRefill_CommitsGrantAndClaimTogetherAndSecondRunIsNoOp` 用真库断言了写入确实落下 |
| `SqliteCharacterItemTable.InsertStackAsync` 里的两个魔数 `core[21..25] = -1`、`core[66] = 255` | `StackableItemCore.Create` 的 `Marker16 = -1` / `RandomOptionChangedIndex = byte.MaxValue`（`ItemContainers.cs:33-39`），偏移由 `InventoryItemCore.Encode:32,42` 对上（21 与 66，一致） | 保留。`Marker16` 有断言守着（该用例断言 `hex(substr(item_core,22,4)) == "FFFFFFFF"`）；`core[66]` 没有断言，但代码路径与 Split 共用同一个工厂，结构上一致 |
| `SetStackCountAsync` 的「先读回再写」（保住堆以外的字段） | `LandAsync` 的 `pile.Core with { Value = topped.Total }`（`ItemContainers.cs:322-329`） | 语义等价且更强（整个 core 原样带过） |
| `ReadMainStacksAsync` 的 `ORDER BY slot_index` | `SqliteItemStoreSession.ReadAllAsync` 用 `SortedDictionary<int, StoredItem>` 返回，同样按槽位升序 | 一致，`ForDailyRefill` 依赖的顺序未变 |
| `ICharacterItemTable` 的四个成员与 `MainStackRow` 记录 | 已删除 | **独立核实无残留调用方**：`grep -rn "ReadMainStacksAsync\|ReadMainOccupiedSlotsAsync\|SetStackCountAsync\|InsertStackAsync\|MainStackRow" --include=*.cs Server Docs`（排除 bin/obj）**全部零命中**，测试里也没有守着它们的断言 |
| 两处反向注释 | 已重写 | 新注释准确，没有把仍成立的约束一并删掉：旧注释里「use case 边界让四次写算一次变更」这条理由在新注释里原样保留了 |

唯一在搬运中真正改变了性质的，是**写入的可见时机**——那不在被删掉的代码里，而在「换了一条写入通道」这件事本身，所以逐段对照看不出来。这正是 A-1。

---

## 五、changes_required：必须修什么

1. **修 A-1。** 让每日补充之后的重载能看到补充的写入。三个方向，任选其一（这是评审意见，不是设计裁定）：
   - 让 `SelectCharacterEntryInventoryDomainService.ApplyDailyRefillAsync` 的重载走背包（或让 `IEntryInventoryMutationRepository.ReadMainItemsAsync` 走背包），使读写落在同一个视图上；
   - 给 `IItemStore` 一个「把已决定的写入落到本事务」的显式接缝，并在重载之前调用；
   - 不再重载，改为用背包已经答复的落点与数量直接组装 `CommittedInventoryUpdateState` 与 lease。

2. **补一条守着 A-1 的用例。** 现有 `SqliteEntryInventoryMutationRepositoryTests` 全部用 `MutationsPerCall`（每调用一个事务），恰好绕开这条。需要一条**把补充与重载放进同一个 use case** 的用例——`InventorySide.InUseCaseAsync` 已经现成，用它包住 `Mutations.ApplyDailyRefillAsync` + `CharacterEntries.LoadCharacterAsync` 即可，新开格子与并入已有堆两种情形都要覆盖。

3. **重跑构建与全量并确认净增。** 修完之后新增的两条用例应当绿，且原有 1147 条一条不掉。

不阻塞、建议一并处理：B-3（补 `Split` 的用例）、B-4（把「`itemKind == 0` 现在是背包的不变量、异常文本已变」写进 `Docs/Refactor/DomainInstanceDesign.md`）、B-1（把两端同格与绑定检查的次序变化记一笔）、B-6（顺手订正 `StoreBackedItemContainer` 那条设计文档已预告的陈旧注释）。

---

## 第二轮

verdict: **passed**

第二轮由另一位评审者在全新上下文中进行，只读第一轮记录与工作树本身，不采信执行者自查结论。评审对象：同一批未提交改动（23 个 M + 4 项未跟踪），接受基线 `531c0bb`，分支 `domain-instances-and-snapshots`。

结论：**第一轮的 A-1 已从根上修好**，并且修在正确的落点上。我用一条独立探针直接驱动 `SelectCharacterEntryInventoryDomainService.ApplyDailyRefillAsync`（真实命令的事务形状、真实 SQLite、两种日子），先绿后（去掉整改）红。执行者最担心的三件事——`HasStagedWork` 清空后事务还提不提交、回滚是否仍完整、prepare 时的第二次 flush 会不会重放或漏写——我各自构造实验，全部无问题。第一轮两条 B 级已闭合且都可证伪。未发现新的 A 级问题；发现 4 条新的 B 级观察，加上第一轮 5 条未处理的 B 级一并列出。

---

### 一、我实际执行过的独立实验

环境：`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`，一律 `-m:1`。

评审开始时对 26 个改动文件全部记下 sha256（`/tmp/review2/baseline.sha256`）。每次改坏都用改前副本覆盖复原，评审结束时 `sha256sum -c` **26 个文件全部 OK，零不匹配**；`git status --porcelain` 与开始时逐字相同（23 M + 4 ??）。未提交、未合并、未清理工作树。

#### 基线复核（未改任何东西）

| # | 做了什么 | 结果 |
|---|---|---|
| R1 | `dotnet build Server/86JP.Server.slnx -m:1` | **0 警告 0 错误** |
| R2 | `dotnet test Server/86JP.Server.slnx -m:1` | **1150 通过 / 2 跳过 / 0 失败**（Pvf 60 + Application 508 + Host 4 + Infrastructure 100/2skip + Networking 46 + Persistence 229 + Protocol 203）。`Host.Tests` 满载下没有出现假红 |
| R3 | 与第一轮的 1147 对账 | Application 507→508（+1，新增 Split 用例）、Persistence 227→229（+2，新守卫的两个 `InlineData`）。**净增 3，原有 1147 一条不掉** |
| R4 | 三份整记录表征用例 | `Characters/EntryLoadCharacterizationTests.cs`、`Accounts/AccountEntryLoadCharacterizationTests.cs`、`Inventory/InventoryLoadCharacterizationTests.cs` 都不在 `git status` 里，一个字节未动 |

所有实验做完、全部复原之后又跑了一遍 R1/R2：**0 警告 0 错误；1150 / 2 / 0**，数字相同。

#### 探针（新写临时用例 `Server/tests/Dfo.Server.Application.Tests/Inventory/ZzReviewProbeTests.cs`，跑完已删，`git status` 已确认无残留）

八条探针，全部用真 SQLite 文件、真 `DomainTransactionScope`、真 `InventorySide` 接线。

| # | 构造 | 期望 | 实际 |
|---|---|---|---|
| P1 | **直接驱动 `SelectCharacterEntryInventoryDomainService.ApplyDailyRefillAsync`**：真 `DailyRefillService`（不是 `MutationsPerCall`）+ 真 `CharacterInventoryEntryDomainService`（表通道重载），整段包在**一个** `Scope.Begin()` 里。两例：新开格子（seeded 0）与并入已有堆（seeded 3→5） | `CommittedUpdates` 里 slot 65 / item 1234 / count 5 | **两例都通过**（第一轮 P1/P2/P4 红的位置现在绿） |
| P2 | 同一 use case 内 `AddAsync` → `SnapshotAsync`（`snapshots.ReadMainItemsAsync`，表通道） | 快照看到 1 件 | **通过**——`SnapshotAsync` 被这次整改覆盖了 |
| P3 | 同一 use case 内**裸** `PutAsync` → `SnapshotAsync` | （不设期望，只测量） | **看到 0 件**——裸读写不在 R-7 覆盖内，见 B2-2 |
| P4 | 一个 use case 只做一次动词写入，然后 commit；换新连接读库 | 行落库 | **通过**——`PrepareAsync` 的 `HasStagedWork` 提前返回路径安全 |
| P5 | 动词写入 → 抛异常 → 回滚；换新连接读库 | 0 行 | **通过**——已 apply 的行确实被撤销 |
| P6 | 一个 use case 内连做三次 `MoveToAsync`：Swap(65↔66) → Split(66→67 取 3) → Merge(66→67)，**每步之后经表通道读回** | 三步中间态逐字相符，且最终库里恰好 2 行、槽位不重复 | **通过**（三步中间态：`65:9000x1\|66:1234x5` → `…\|66:1234x2\|67:1234x3` → `65:9000x1\|67:1234x5`） |
| P7 | 一个 use case 内：`AddAsync`（立即 apply）→ 裸 `PutAsync(80,…)`（只暂存）→ 裸 `PutAsync(65,…)`（重新暂存一个**已 apply 过的槽位**）→ commit | 2 行；slot 65 = 9（后写者赢）、slot 80 = 2 | **通过**——清空 staged 后 prepare 的第二次 flush 既不重放也不漏写 |
| P8 | （P1 的第二个 `InlineData`，见上） | — | — |

P1 是本轮的核心：第一轮那位是用 `DailyRefillService` + `InventoryEntryAssembly` 拼出来的，我这次把**真正的入场领域服务**整个跑起来（含 `ResolveCommittedItems` 那次 `SingleOrDefault ?? throw`），两种日子都走到了 `CommittedUpdates`。

#### 故意改坏（改生产代码 → 跑全量 → 复原 → 校验哈希）

| # | 改坏了什么 | 变红的 | 判读 |
|---|---|---|---|
| D-A | 删掉 `LandAsync` 两条落地分支里的 `ApplyDecidedWritesAsync`（`ItemContainers.cs:328,344`） | 仓库自身：`SqliteEntryInventoryMutationRepositoryTests.DailyRefill_IsVisibleToTheEntryReloadInsideTheSameUseCase` 的 **seeded:0 与 seeded:3 两例都红**；我的 P1（两例）与 P2 同时红 | A-1 确实由这两行修好，且新守卫**比执行者自报的强**——执行者说 D7 只红 seeded:0，我实测两例都红 |
| D-B | 只删 `MoveToAsync` 里的 `ApplyDecidedWritesAsync`（`:417`） | 仓库自身：**一条都没红**（1150 全绿）；只有我的 P6 红 | → **B2-1**：移动动词的 R-7 落地零守卫 |
| D-C | `ApplyDecidedWritesAsync` 里去掉 `this.staged.Clear()`（只 flush 不清空） | **一条都没红**，我的 P6/P7 也绿 | 第二次 flush 是「先删后插」，重放幂等，既不产生重行也不丢行。清空是省功，不是正确性前提；同时说明它自身无守卫（见 B2-5） |
| D-E | `ContainerMoveKind.Split` 一律改判 `Relocate` | 恰好 `MoveItemSpaceHandlerTests.Dragging_part_of_a_pile_onto_an_empty_slot_leaves_the_rest_behind`（+ 我的 P6） | 第一轮 B-3 已闭合且可证伪 |
| D-F | `Moved(plan.MoveCount, moving.ItemId)` → `moving.ItemId + 1` | 恰好 `MoveItemSpaceHandlerTests.Two_moves_arriving_at_once_are_both_carried_out_and_neither_is_refused` | 第一轮 B-2 已闭合且可证伪 |
| D-G | 复核第一轮 E1a：`DailyRefillService` 的 `NoRoom` 从抛改成静默 `return` | 恰好 `SqliteEntryInventoryMutationRepositoryTests.DailyRefill_RefusesWhenTheBandIsFull`（1 红）；移动与教程奖励零影响 | 每日补充「遇满抛」的独立守卫仍在 |
| D-H | 复核第一轮 E1c：`MoveItemSpaceSlice` 的 `Refused` 改答 `Unchanged` | 恰好 6 条：`Two_empty_slots…`、`A_slot_outside…`、`A_merge_past…`、`A_swap_whose_other_end…`、`Dragging_part_of_a_pile_onto_another_item_will_not_swap`、`ItemPathFrameTests.ARefusedMoveOfARealRowAcknowledgesTheSameBytesAsBefore`；补充与教程奖励零影响 | 移动「遇拒返回拒绝码」的独立守卫仍在，且与另外两处互不牵连 |

合计：8 条探针 + 7 次故意改坏。其中 5 次红且只红被点名的那条（组），2 次（D-B、D-C）暴露出无守卫的面。

#### 复原核验

```
sha256sum -c /tmp/review2/baseline.sha256   →  26 个文件全部 OK，零不匹配
git status --porcelain                      →  与评审开始时逐字相同（23 M + 4 ??）
dotnet build / dotnet test                  →  0 警告 0 错误 / 1150 通过 2 跳过 0 失败
```

本轮无操作失误，未使用过 `git checkout --`。

---

### 二、逐条回答本轮被点名的问题

**① A-1 修好了吗，是不是从根上？** 是。修法是「给存储一个显式接缝，动词在返回前调用」——第一轮给出的三个方向里的第二个。P1 用真实命令形状证明两种日子都看得见；D-A 证明拿掉这两行两种日子都回退（新开格子那例抛 `InvalidDataException` 使整段失败，并堆那例读回补充前的数量）。**这是构造上确定的，不是统计推断。**

**② R-7 的落点对不对？** 对，而且比放在调用方好。两点具体核验：

- **`PersistentItemContainer.SnapshotAsync` 也被覆盖了。** 它走 `snapshots.ReadMainItemsAsync`（`ItemContainers.cs:588-590`，表通道），P2 证明同一 use case 内 `AddAsync` 之后 `SnapshotAsync` 看得见；D-A 之后 P2 同时变红。**放在动词里而不是调用方，正是这一条得以被顺带覆盖的原因**——如果改在 `SelectCharacterEntryInventoryDomainService` 里，`SnapshotAsync` 这条路仍然会是坏的。今天 `SnapshotAsync` 在 `Server/src` 里没有生产调用方（只有接口与两条测试用例），所以这一条目前是「预防」而非「修复」。
- **每动词一次 flush，中途没有依赖「前一步已落库」的步骤。** `MoveToAsync` 内部的多次写全部经 `session.Write/Clear` 进 staged，`ItemContainerOperations` 的四个操作都不回读；`DecideMoveAsync` 的两次读发生在任何写之前（`MoveToAsync:369` 的 `ReadSlotsAsync`）。P6 用 Swap/Split/Merge 三连做了正面验证。
- **清空 staged 之后的第二次 flush 不重放也不漏写。** `FlushAsync` 对它命名的每个槽位都是「先 DELETE 再 INSERT」，所以即使不清空也只是重做一遍同样的结果——D-C 实测全绿即是证据；清空之后 prepare 只处理清空后新暂存的部分，P7（在已 apply 的槽位上再暂存一次）证明后写者赢且不产生重行。

**③ 整改自身引入的新问题？**

- **`SqliteDatabaseParticipant` 的 `HasStagedWork` 判定：安全。** `PrepareAsync:125` 在无 staged 时会提前返回，但它**先把 `prepared = true` 置上**再返回；`CommitAsync:151` 只在 `!prepared` 时才拒绝，随后无条件提交 `this.pending`——而 `pending` 在动词 apply 时就已由 `CommandAsync` 开出来了。P4 正面验证：一个 use case 只做一次动词写入，commit 后换新连接读得到行。另外 `DomainTransaction.Enlist` 在**登记时**就确定了 `commitFallible`，与 staged 有无无关，所以两阶段次序也没被绕开。
- **回滚仍然完整。** P5：动词写入 → 抛异常 → `DomainTransaction` 回滚 → 换新连接读库 0 行。因为 apply 出的语句就落在同一个尚未提交的 SQLite 事务里，`RollbackAsync` 把它们一并撤销。
- **性能未评**（按要求）。

**④ 第一轮已成立的结论没被破坏。** 见下方 SC / AC 两张表。其中 SC-A3 与 AC-6 由「不成立」翻为「成立」，其余保持。

**⑤ 超范围检查：`IItemStoreSession` 新增成员算不算超范围？——不算。** 理由三条：(a) 契约 Out 一节列的是跨容器移动、人物实例、规则服务身份拆分、快照重塑、客户端错误码、`ItemContainerOperations` 的判定职责，新增的这一条与五者都不沾边；(b) C-5 禁的是「改动持久层形状 / 容器自建表 / 容器自持数据库」，这次没有动 `CharacterDatabaseSchema.cs`，没有新表新列，容器仍然只经 `IItemStore` + 环境事务，新增的是**会话词汇**而不是持久层形状；(c) 它是为了满足 C-1（三处调用方对外行为一字不变）所必需的最小接缝——不加它，每日补充在入场边界上的对外行为就是变了的。抽象层公开面确实被加宽了一格，但 `IItemStoreSession` 全仓只有两个实现（`SqliteItemStoreSession`、测试用 `VolatileItemStoreSession`），两个都已实现，且注释明确写了「不是边界的替代品，不授予无边界写入的许可」——`PersistentItemContainer.WriteAsync` 无环境事务时仍然直接抛，这一点我核过。

---

### 三、发现

#### A 级（阻塞）

**无。**

#### B 级（观察与建议）

##### B2-1　移动动词的 R-7 落地零守卫

`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:417`。实测 D-B：只删这一行，全量 1150 一条不红。落地动词一共两处 apply（`LandAsync` 里一处服务 `AddAsync`/`RefillAsync`，`MoveToAsync` 里一处），前者有 `DailyRefill_IsVisibleToTheEntryReloadInsideTheSameUseCase` 守着，后者什么都没有。

判 B 而非 A：今天没有生产调用方在同一个 use case 内先 `MoveToAsync` 再经表通道读回（`MoveItemSpaceCommandHandler` 只翻译结果，不重载），所以现在构造不出实机后果。但 R-7 自己写的是「Every verb added from here inherits the same trap」，而三个动词里只有一条路被钉住——正是第一轮那类「判据比结论窄」。建议补一条：一个 use case 内 `MoveToAsync` 之后经 `ReadMainItemsAsync`（或 `SnapshotAsync`）读回，断言看得见。

##### B2-2　R-7 不覆盖裸 `PutAsync` / `ClearAsync`，而它们也被拼成复合动作

实测 P3：同一 use case 内 `PutAsync` 之后 `SnapshotAsync` 看到 **0 件**。`ItemContainerOperations.ConsumeAsync` 就是由这两个裸操作拼成的，唯一调用方是 `Server/src/Dfo.Server.Application/Inventory/ItemUseEffectDomainService.cs:80-82`。

判 B：`ApplyAsync` 之后直接用 `ItemUseApplication(item.ItemId, remaining)` 答复，不经表通道重载，所以今天同样构造不出后果；而且这条路在改动前就是这样，不是本轮搬丢的。但从 R-7 的措辞看，「verb」指的是三个领域动词，`ConsumeAsync` 在结构上是第四个复合动作却不在规则覆盖内——建议在 R-7 里把这条边界写明（是有意不覆盖，还是尚未覆盖）。

##### B2-3　`ReadAllAsync` 的注释在 R-7 之后已经不准确

`Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs:136-141`：

> Unlike `SnapshotAsync` this is the container's own contents, so inside a use case it sees what that use case has already written.

R-7 落地之后，`SnapshotAsync` 对**动词写入**同样看得见（P2 实测通过，D-A 之后变红）。两者的差别现在只剩裸 `PutAsync`/`ClearAsync` 那一段。这句话按字面还能勉强站住，但它给下一个读者的印象——「快照看不见本 use case 写的东西」——恰好是 R-7 刚刚推翻的那条。契约 SC-A7 的主题就是不留反向指引，建议顺手改掉。

##### B2-4　`FlushAsync` 的注释被新方法挤走了

`Server/src/Dfo.Server.Persistence/Inventory/SqliteItemStore.cs:152-175`。原本挂在 `FlushAsync` 上、描述其「先把命名到的槽位全部清空再逐个填入 / 头像随身事实在第一次 DELETE 之前读出」的那段 `<remarks>`，因为新方法插在它与 `FlushAsync` 之间，现在挂到了 `ApplyDecidedWritesAsync` 头上——于是 `ApplyDecidedWritesAsync` 带着**两个 `<remarks>`**，而 `FlushAsync`（那段话真正描述的方法）一条注释都没有。纯文档缺陷，构建 0 警告。

##### B2-5　`staged.Clear()` 无守卫，且不是正确性前提

`SqliteItemStore.cs:172`。D-C 实测：去掉它全量全绿，我的 P6/P7 也绿。它是省一次重复 flush 的优化（注释也是这么写的），不是正确性要求。列在这里只为记录判据宽窄：**「不会重放/漏写」是我实测出来的，不是靠这一行保证的**——即使有人日后删掉它，行为也不变。不需要动。

##### 第一轮未处理的 B 级，本轮复核仍然存在

| 第一轮编号 | 现状 | 位置 |
|---|---|---|
| B-1 两端同格与「没有绑定容器」的判定次序反了，且未记档 | **仍未记档**：`grep "NoBoundContainer\|same slot"` 在 `Docs/Refactor/DomainInstanceDesign.md` 零命中 | `MoveItemSpaceSlice.cs:237-239` vs `ItemContainers.cs:364-367` |
| B-5 槽位带一律按主列表问 | 仍然如此（今天只注册 Main，注释已说明） | `ItemContainers.cs:442,482` |
| B-6 「Both of the ones below」这条设计文档已预告的陈旧注释 | **仍然陈旧**，而这一轮该文件又被动过一次 | `ItemContainers.cs:208-210` |
| B-7 夹具 `null!` 依赖 | 仍然如此，本轮 `ContainerInventoryProjectionTests.cs:127-129` 又添三处 | 五处夹具 |
| B-8 `ItemPathLiveTests.cs:9` using 非字母序 | 仍然如此 | 同左 |
| B-9 三处 `ArgumentOutOfRangeException` 的 paramName 与传入值不对应 | 仍然如此 | `DailyRefillService.cs:134`、`GrantTutorialRewardCommand.cs:80`、`MoveItemSpaceSlice.cs:257` |

这六条都不阻塞。B-2、B-3（第一轮明确要求的两条）已闭合并各自可证伪。

---

### 四、契约成功条件逐条核对（SC-A1..SC-A8）

| ID | 第一轮 | 本轮 | 证据 |
|---|---|---|---|
| SC-A1 | 成立 | **成立** | `grep -rn "case ItemArrival\|switch (arrival)" Server/src`：只有 `ItemContainers.cs:320,322,332,347` 一处 switch。教程奖励经 `AddAsync`、每日补充经 `RefillAsync`，两者汇到 `LandAsync` |
| SC-A2 | 成立 | **成立** | `grep -rn "IItemPlacementDefinitionSource\|IItemArrivalRuleService\|IItemSlotRuleService\|…" Server/src/Dfo.Server.Protocol` **零命中** |
| SC-A3 | **不成立** | **成立** | 三条支柱都独立实测：①每日补充在入场边界上的对外行为已恢复（P1 两种日子绿，D-A 两种日子红）；②遇满抛的守卫仍在且只属于补充（D-G 恰好 1 红）；③移动遇拒返回拒绝码的守卫仍在且只属于移动（D-H 恰好 6 红，补充与教程奖励零影响）。教程奖励一侧（第一轮 E1b）本轮未重测，理由是 `GrantTutorialRewardCommand.cs` 的 sha256 与第一轮基线**逐字节相同**（`0d93995e…`），且 `TutorialRewardPlacementTests` 用例数未变——**这一条是推断，不是本轮实测**，特此分开 |
| SC-A4 | 成立 | **成立** | `ItemMoveStatus` 十个值一个不减、`ErrorCodeFor` 未改（`git diff` 未触及）。D-H 让五种拒绝一次性全红且都是被点名的那些；D-E/D-F 在 `ItemContainers.cs` 内部改坏各恰好红一条，说明该文件里的守卫仍然灵敏。**逐分支「恰好红一条」是第一轮 E3a–E3g 的实测，本轮未逐条重跑**，本轮是整体复核 |
| SC-A5 | 成立 | **成立** | `DailyRefill_LeavesTheProjectionAloneEvenIfTheRowsSurrogateKeyMoves` 断言未改且绿；`StorageEntryId` 在 Protocol 仍只出现在异常字符串；槽位唯一性由 DDL `UNIQUE(owner_scope,owner_id,list_type,slot_index)` 与 `InventoryEntryServices.ValidateItems` 双重保障 |
| SC-A6 | 成立 | **成立** | R1/R2 与最终复原后的重跑：0 警告 0 错误；**1150 / 2 / 0**；净增 3，原有 1147 一条不掉 |
| SC-A7 | 成立 | **成立（有保留）** | 两处反向注释已重写；`Docs/Refactor/DomainInstanceDesign.md` 新增 R-7 一节（`:197-213`）并在「Settled by the item-side implementation」补了本次失效（`:275-283`）与 `itemKind == 0` 移入背包（`:285-289`）两条。保留项：B2-3（`ReadAllAsync` 注释在 R-7 之后已不准确）、B2-4（`FlushAsync` 注释被挤走）、第一轮 B-6（陈旧注释在文件再次被动过后仍陈旧）、第一轮 B-1（次序变化仍未记档） |
| SC-A8 | 成立 | **成立** | 执行者 8 次；我本轮另做 8 条探针 + 7 次故意改坏，其中 5 次红且只红被点名的那条（组），2 次暴露出无守卫的面（B2-1、B2-5） |

---

### 五、规格验收逐条核对（AC-1..AC-10）

| ID | 第一轮 | 本轮 | 证据 |
|---|---|---|---|
| AC-1 | 成立 | **成立** | `BagPlacementVerbTests` 四条未改且绿；`DailyRefill_CommitsGrantAndClaimTogetherAndSecondRunIsNoOp` 断言 GrantedCount == CommittedCount == 5 |
| AC-2 | 成立 | **成立** | `Adding_and_refilling_disagree_about_a_pile_that_is_already_full` 未改且绿 |
| AC-3 | 成立 | **成立（更强）** | 七条齐备；本轮新增 `Dragging_part_of_a_pile_onto_an_empty_slot_leaves_the_rest_behind` 补上了第一轮 B-3 的 `Split` 空白（D-E 恰好红它），`Moved` 的物品编号也补上了断言（D-F 恰好红它）。第一轮列的两条缺口都已闭合 |
| AC-4 | 成立 | **成立** | 同 SC-A1 |
| AC-5 | 成立 | **成立** | 同 SC-A2 |
| AC-6 | **不成立** | **成立** | 九个格子在单元层面逐条有用例；每日补充在入场边界上的对外行为已恢复——P1 直接驱动 `SelectCharacterEntryInventoryDomainService.ApplyDailyRefillAsync`，新开格子与并入已有堆两种日子都拿到正确的 `CommittedUpdates`；D-A 拿掉整改后两种日子都红。仓库自身也有了 `DailyRefill_IsVisibleToTheEntryReloadInsideTheSameUseCase`（两个 `InlineData` 在 D-A 下**都**红） |
| AC-7 | 成立 | **成立** | 同 SC-A5 |
| AC-8 | 成立 | **成立** | 同 SC-A7 |
| AC-9 | 成立 | **成立** | 同 SC-A6 |
| AC-10 | 成立 | **成立** | 同 SC-A8 |

#### 项目约束核对

| 约束 | 判定 | 证据 |
|---|---|---|
| 三份整记录表征用例期望值不改 | **满足** | 三个文件都不在 `git status` 的改动集里 |
| `SqliteEntryInventoryMutationRepositoryTests` 原有 9 条期望值不改 | **满足** | 逐条看过 `git diff`：对这 9 条的改动只有 `var repository = new InventorySide(path).MutationsPerCall;` → `var side = …; side.BindMain(…); var repository = side.MutationsPerCall;` 六处，**没有一个 `Assert` 被动过**；本轮新增的三条（含 `[Theory]` 两例）是净增 |
| `ItemContainerOperations` 不得获得判定职责 | **满足** | 四个操作 + `ConsumeAsync` 方法体只有 clear/put，没有任何判定；本轮的 `ApplyDecidedWritesAsync` 也没有加进这里，而是在 `StoreBackedItemContainer` 的动词里 |
| 不改持久层形状；背包不自建表不自持数据库 | **满足** | `CharacterDatabaseSchema.cs` 不在改动集里；容器仍经 `IItemStore` + 环境事务；新增的是会话上的一个操作，不是表或列 |
| 不为三个动词另开 `canX` | **满足** | `IItemContainer` 上只有 `AddAsync`/`RefillAsync`/`MoveToAsync`，无并行查询 |
| CRLF、无 BOM、net10.0、`TreatWarningsAsErrors` | **满足** | 逐个核过 `IItemStore.cs`/`SqliteItemStore.cs`/`ItemContainers.cs`/`SqliteEntryInventoryMutationRepositoryTests.cs`/`MoveItemSpaceHandlerTests.cs`/`VolatileItemStore.cs`/`DomainInstanceDesign.md`：**全部 CRLF、零 LF-only 行、无 BOM**；构建 0 警告 |
| 不动 `Tool/` 与 `.worktrees/legacy-reference/` | **满足** | 不在改动集里 |
| 契约 Out（跨容器移动、人物实例、规则服务身份拆分、快照重塑、客户端错误码） | **满足，无越界** | 非主列表仍按 `ListNotServed` 拒绝；无人物实例类型新增；三个规则服务的签名 `git diff` 零改动；`SnapshotAsync` 签名未改（只是它现在能看见动词写入，这是 R-7 的效果不是重塑）；`ItemMoveStatus` 仍 10 个值、`ErrorCodeFor` 未改。`IItemStoreSession` 新增成员的越界判断见第二节⑤：**不越界** |

---

### 六、结论

**verdict: passed。**

第一轮的一条 A 级已闭合，闭合方式经独立构造验证；两条被点名的 B 级已闭合且各自可证伪；整改自身未引入 A 级问题，执行者最担心的三处（事务提交、回滚、二次 flush）经实验全部安全。本轮新增 4 条 B 级观察（B2-1 移动动词的 R-7 无守卫、B2-2 裸读写不在 R-7 覆盖内、B2-3 `ReadAllAsync` 注释已不准确、B2-4 `FlushAsync` 注释被挤走）与 1 条纯记录项（B2-5），加上第一轮遗留的 6 条 B 级，均**不阻塞**。

若要一并处理，建议优先级：B2-1（补一条移动动词的 R-7 守卫，它是这次 A-1 同类问题唯一还没被钉住的面）> B2-3 / B-6（两条注释，都属于 SC-A7 的主题）> B2-4 / B-1 / B-8 / B-9（文档与风格）。

> 说明：本文件顶部的 frontmatter `verdict` 已按评审技能要求更新为当前门决（`passed`）。第一轮的记录正文原样保留，未作任何修改。
