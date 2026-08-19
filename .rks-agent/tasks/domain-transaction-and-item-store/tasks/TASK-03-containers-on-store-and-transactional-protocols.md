# TASK-03 — 容器改由存储承载，两条协议进入事务

## Task Unit Contract

- Contract revision: 2（见 `## Contract Amendment A1`）
- Goal and deliverables: 把容器的读写改由存储会话承载，复合写拆为基本读写组合，**并在同一任务内**把道具移动与道具使用两条命令改为事务型执行。
- Referenced spec requirements: RQ-002, RQ-003, RQ-005, RQ-008
- Referenced plan sections: `../plan.md` 的 `### T-003` 全节；BD-007、BD-008、BD-010、BD-015、BD-016；`## Implementation Approach` 第二段与 `## Risks and Rollback` 第一条
- Owned boundary: BD-007, BD-008, BD-010, BD-015, BD-016
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`
  - `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs`
  - `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs`
  - `Server/src/Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs`
  - `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`
  - `Server/tests/`（新增载体无关与事务外抛出的用例；既有用例只作机械调整）
- Dependencies: TASK-01, TASK-02
- Required dependency outputs: TASK-01 的用例边界与工作单元 behavior；TASK-02 的存储抽象与两种实现。
- Acceptance conditions:
  1. 移动、交换、合并、拆堆由基本读写组合表达；持久实现不再接受任何描述「移动」的输入。
  2. 两条命令的执行方式改为事务型，且与复合写的拆解在**同一任务内**一起落地。
  3. 持久容器在用例边界之外被改动时抛出；仅存内存的容器不抛。
  4. 移动与使用的业务逻辑中不出现任何开启、提交、回滚或事务对象。
  5. 同一段移动逻辑作用于持久容器与仅存内存的容器时走同一路径，可观察结果相同。
  6. 既有移动与使用用例的断言未被改写，全部通过。
- Task-specific authorization: 沿用根 TaskContract 的 `task` 授权。收窄：**不得**把「容器改用存储」与「命令标记为事务型」分成两个提交单元或两个 Task 交付；plan 显式排除该排法。
- Required methods: `test-driven-development`：载体无关与事务外抛出两条新保证先有会红的用例；既有用例作为回归网，语义不改。
- Required checks: CK-008、CK-009、CK-010、CK-011、CK-012、CK-013（见 `../plan.md` 对应任务的 Validation 表，命令与预期结果以该表为准）
- Required evidence: CK-010、CK-011 的 RED 与 GREEN 证据；CK-008、CK-009 的既有用例通过结果与断言未改的核对；CK-012 的检索结果；CK-013 的全套结果。

## Inherited Notes from TASK-02

TASK-02 结束时留给本任务的三项。它们不改变本任务的验收条件，只是本任务动手前应当知道的既有事实。

### 1. `ContainerId` 的铸造与解析已经存在，但还有一处生产代码没走它

TASK-02 的 `## Contract Amendment A1` 已就 `IItemContainer.cs` 作了切分：`ContainerId.ForCharacter` 与 `TryGetCharacterOwner` 是 TASK-02 加的，`plan.md` 的 BD-008 仍把该文件整体写给 T-003/T-004，未作标注（已批准的 plan 不在实现阶段改动）。本任务仍拥有该文件的其余部分——存储行标识与移动计划类型的移除照原计划归 T-003/T-004。

待办：`SelectCharacterEntryInventoryDomainService.cs:59` 仍在手写 `$"character:{characterId.Value}:{main.ContainerType}"`。它与 `ForCharacter` 的输出今天逐字节相同，但两处独立书写同一格式正是会漂移的形状；该文件属 BD-007，改用 `ForCharacter` 落在本任务范围内。

注意解析只接受 `ForCharacter` 会铸出的那一种拼法（数字段往返回原文、类型段等于 `Enum.GetName`）。若本任务要为地下城掉落等场景铸造非角色的容器标识符，它们会被持久存储拒绝——这是有意的，那些容器本就该落在仅存内存的实现上。

### 2. `character_item_locks` 的槽字段在物品移动后会陈旧

该表（`SqliteInventoryEntryRepository.cs:986-994`）以 `(character_id, equipment_lock_id)` 为键，把 `inventory_list_type` 与 `slot` 作为数据携带。物品换槽之后这两个字段不再指向它。

这是既有缺口，不是本次引入的：全库对该表只有 CREATE 与一处 SELECT（`:635-641`），没有任何写入，旧的按行移动同样让它陈旧；且 `EquipmentLockId` 本身在 core 第 81 字节里随物品走。容器改由存储承载时判断是否要让它跟随——若判断为「维持现状」，请把该判断记下来，而不是默认略过。

### 3. 两处存储读写谓词的不对称，已记录未修改

`SqliteItemStoreSession` 的读带 `AND character_id = @owner`，而 `EmptyAsync` 的删除不带（它逐字对齐唯一键 `UNIQUE(owner_scope, owner_id, list_type, slot_index)`）。于是 `character_id IS NULL` 的行对读不可见、却会被写删除。该行对其属主仓储同样不可见（`:300`），删它不丢活数据，另一条路是唯一键冲突并让整个用例失败。本任务若改动这两处谓词，需一并考虑这一点。

## Contract Amendment A1

- Amends: Permitted files or interfaces
- 时点：实现阶段。移除容器契约上的 `ConsumeAsync(long storageEntryId, …)` 之后，效果模型无处安放其寻址。

**增列以下文件为本任务的 Permitted files：**

| 文件 | 改动 | 为什么必须落在本任务 |
|---|---|---|
| `Server/src/Dfo.Server.Abstractions/Inventory/IItemUseEffects.cs`（BD-009） | `ConsumeFromContainerEffect` 的 `long StorageEntryId` 改为 `int SlotIndex`；`PlanAsync` 的入参同改；`ApplyAsync` 的返回由可空改为非空 | 容器契约上的消耗由行号寻址改为槽寻址，是「复合写拆为基本读写」的一部分。效果模型是该方法的唯一调用者，不同改就编译不过。BD-009 原派给 T-004，但 T-004 的工作是词汇清扫，不是把消耗从行号搬到槽 |
| `Server/src/Dfo.Server.Application/Inventory/ItemUseEffectDomainService.cs`（BD-009） | 同上的实现侧改动；消耗改为调用 `ItemContainerOperations.ConsumeAsync` | 同上 |
| `Server/src/Dfo.Server.Application/Inventory/SelectCharacterEntryInventoryDomainService.cs`（BD-007） | 构造 `PersistentItemContainer` 的三个新入参；手写的容器标识符改用 `ContainerId.ForCharacter` | 它是全库唯一构造持久容器的地方，容器的构造签名一变它就必须跟着变。改用 `ForCharacter` 是 TASK-02 移交的第 1 项 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs`、`Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs`（BD-015） | 注册顺序与 `IItemStore` 的接口映射 | BD-015 本就在本任务的 Owned boundary 内，此处仅为记全 |
| `Server/src/Dfo.Server.Application/Transactions/DomainTransactionScope.cs`、`DomainTransaction.cs`（BD-002） | `Current` 改为「已了结的事务视同没有事务」 | 见 `## Execution Result 1` 的「一处真实缺陷」。BD-002 属 TASK-01，但该缺陷只有在容器真正依赖环境事务之后才会暴露 |

授权：属根 TaskContract `task` 授权范围内的边界记录，不扩大对外可观察行为，留待 change review 确认。

## Execution Result 1

### 容器契约：只剩读与写

`IItemContainer` 现在是 `ReadSlotAsync` / `ReadSlotsAsync` / `SnapshotAsync` / `PutAsync` / `ClearAsync`。删去 `ApplyMoveAsync`、`ConsumeAsync`，以及 `ContainerMovePlan`、`ContainerMoveKind`（旧的那个）、`ContainerConsumption` 三个类型。`ContainerSlotItem` 由 `(long StorageEntryId, int SlotIndex, byte ItemKind, int ItemId, int Count)` 变为 `(int SlotIndex, InventoryItemCoreState Core)`，`ItemKind` / `ItemId` / `Count` 成为读取 core 的计算属性——**写要带上整个 core**，否则一件带附魔、带随机属性、带化妆装链接的装备换个槽就会掉东西。

移动、拆堆、交换、合并、消耗五种复合操作搬到 `ItemContainerOperations`（`ItemContainers.cs`，公开静态类），每一种都写成它由哪些 clear 与 put 组成。这一处是关键：这些规则从前有**两份**实现——`SqliteEntryInventoryMutationRepository.ApplyMainMoveAsync` 一份，`TransientItemContainer.Apply` 一份——两份只在有人不断核对时才一致。现在只有一份描述，两种载体跑同一段代码。

### 两种载体：同一个类，换一个存储

新增 `StoreBackedItemContainer` 抽象基类，持久与仅存内存两种容器都是它加一个不同的存储、以及一个不同的「没有用例边界时怎么办」：

- `PersistentItemContainer`：没有环境事务就**抛**。这种改动没有任何东西能把它收回来，也没有任何失败可供人看见，正是整个边界要杜绝的那种静默非原子写。
- `TransientItemContainer`：没有环境事务就自开一个只有它自己的事务并当即了结。它背后的存储声明提交不会失败，所以一个人的事务填完即settle。

读在两种载体上都不要求边界——容器会在并非用例的地方被查看，把看也拦下来会让这条边界变成关于「看」的规则。

`SnapshotAsync` 在持久容器上仍走入场加载器。存储的词汇里没有化妆装细节这类事实，而入场快照要用；「入场快照改由容器供给」本就是 Spec 的 Non-Goal。

### 两条协议

`MoveItemSpaceCommandHandler` 现在决定完之后按 kind 调 `ItemContainerOperations` 的对应组合。`UseStackableCommandHandler` 把槽号交给效果模型。两者都注册为 `CommandExecutionKind.Transactional`——**与复合写的拆解在同一次改动里**，plan 明确排除先拆后保护的排法，因为那个窗口里移动是好几次写而没有任何东西把它们绑在一起，且全套测试照样全绿。

### 一处真实缺陷，由本任务的改动暴露

`DomainTransactionScope.Current` 直接返回 `AsyncLocal` 的值。`DomainTransactionLease.DisposeAsync` 是异步方法，而异步方法写的是执行上下文的**副本**——`Release()` 置空不会回流到开启事务的那个调用方。TASK-01 里没有任何东西在事务了结之后还去问 `Current`，所以它一直没被发现；容器一接上环境事务，提交后紧跟一次读就立刻撞上「事务已了结，不再接纳参与者」。

改为 `Current` 在事务已了结时答「没有」。这比清槽位更可靠：读的是事务自己的状态，而不是指望一次写能回流。`Begin()` 的重入判断同改。

## Executor Self-check 1

| Acceptance condition | 结果 | 证据 |
|---|---|---|
| 1. 复合操作由基本读写组合表达；持久实现不再接受描述「移动」的输入 | 通过 | `ItemContainerOperations`；`SqliteItemStore` 与 `IItemStore` 中 `MovePlan\|MoveKind\|ApplyMainMove` 零命中 |
| 2. 两条命令改为事务型，且与拆解在同一任务内 | 通过 | `ProtocolModule.cs:140,142` 均为 `Transactional`，与容器改造在同一改动集内，无任务级提交把两者分开 |
| 3. 持久容器在边界外改动抛出；仅存内存的不抛 | 通过 | CK-011（`~OutsideTransaction`，4 条） |
| 4. 移动与使用的业务逻辑中无事务词汇 | 通过 | CK-012 零命中 |
| 5. 同一段移动逻辑作用于两种载体走同一路径、结果相同 | 通过 | CK-010（`~ContainerCarrier`，2 条）。且现在是**同一段代码**：操作只实现了一次 |
| 6. 既有移动与使用用例断言未改写，全部通过 | 通过 | CK-008 12 条、CK-009 13 条，断言逐条未动 |

### 既有测试的机械调整

三处依赖变化，断言一律未动：

1. `MoveItemSpaceHandlerTests` / `UseStackableHandlerTests` 的 `FakeInventory` 由实现 `IEntryInventoryMutationRepository` 改为继承新的 `FakeMainInventory`（持久容器 + 内存存储）。对外表面保持原样——移动侧的索引器与 `Rows()`、使用侧的构造签名与 `Count` / `IsEmpty`——所以每一条测试正文一个字未改。
2. `DirectCommandBus` 现在开启用例边界并以 `Transactional` 分派。不这么做就是拿一个持久容器永远不会遇到的环境去测它。
3. `ItemContainerCarrierEquivalenceTests`（原 `ItemContainerEquivalenceTests`）的脚本改用新词汇。**一处非机械的减项**：原脚本有三条容器级拒绝观察（陈旧、超量、自指），它们是 `ApplyMoveAsync` 的重校验行为，该方法已不存在。三条的去向各不相同，逐条说清：
   - **自指**（一个计划把同一件道具当作两端）由 `MoveItemSpaceHandlerTests.Naming_one_slot_twice_succeeds_without_moving_anything` 覆盖。
   - **超量**（移动数大于堆的数量）没有任何处理器用例覆盖——全库没有一条测试发送非零 `MoveCount`。它之所以仍然安全，是因为 `MoveItemSpaceSlice.cs` 的 `PlanAsync` 有一处未被改动的钳位（`requestedCount <= 0 || requestedCount > moving.Count` 即取整堆），使容器级的那道防线本就是冗余的。这是**论据的替换而非覆盖的替换**，据实记下。
   - **陈旧**（读之后槽位内容变了）现在按构造即不存在：读与写落在同一用例边界内，组合操作没有失败的出路。这正是 TASK-07 要下结论的那件事，本任务不预判。
   脚本的语义核心——两种载体对同一串操作逐步回答相同，且仅存内存的容器什么都不落库——原样保留。类名加上 `Carrier` 以对上 CK-010 的过滤器。

### Validation

| ID | 结果 |
|---|---|
| CK-008 | `已通过! - 失败: 0，通过: 12` |
| CK-009 | `已通过! - 失败: 0，通过: 13` |
| CK-010 | `已通过! - 失败: 0，通过: 2` |
| CK-011 | `已通过! - 失败: 0，通过: 4` |
| CK-012 | 两个处理器源码中 `IDomainTransaction\|DomainTransaction\|BeginTransaction\|CommitAsync\|RollbackAsync\|IItemStore\|TransactionScope\|Enlist` 零命中 |
| CK-013 | `dotnet test Server/86JP.Server.slnx -m:1` → `916 通过 / 2 跳过 / 0 失败`（Pvf 60、Application 419、Host 4、Infrastructure 187+2跳过、Networking 46、Protocol 200） |

### 留给后续任务

- **`SlotsChanged` 现在看来不可达**。组合操作不会失败，处理器也就没有路径产出该状态。这是 TASK-07 要下的结论，本任务不预判：枚举成员与 `ErrorCodeFor` 的映射原样保留。
- `ContainerSlotItem` 已不带行号，但 `InventoryItemState` 仍带（仅存内存的容器一律填 0）。领域词汇的清扫是 TASK-04。

## Independent Quick Review 1

复评判 **passed-with-findings**，**无 blocking**。评审逐条独立复核了最危险的三处并确认：

- **原子性空洞不存在**。`Write` / `Clear` 是同步的、只改内存中的暂存字典；唯一的落库点是 `PrepareAsync`，由 `DomainTransaction.CommitAsync` 驱动，而后者由 `UnitOfWorkBehavior` 在 handler 正常返回之后才调用。两次写之间抛出只会留下暂存项，`await using` 的释放把它们清掉且一条 SQL 都没发出。写之后再拒绝的路径不存在：两个处理器的每一条拒绝都排在每一次写之前。
- **五种操作与被退役的两份实现逐一等价**。评审把 `ItemContainerOperations` 与 `ApplyMainMoveAsync`、旧 `TransientItemContainer.Apply` 对照到字节：拆堆写出的新堆与旧 `CreateStackableCore` 逐字节相同（kind、itemId、count、偏移 21 的 `-1`、`core[66]=0xFF`），且 `EncodeItemCore` / `DecodeItemCore` 覆盖全部 82 字节无空档，故 clear+put 不丢附魔、随机属性与化妆装链接。
- **`AsyncLocal` 的诊断经实测成立**。评审在仓库之外写了独立探针：异步方法内对 `AsyncLocal` 的写不会回流给调用方（有无 `await` 皆然），同步方法的写会。故 `Release()` 确实清不掉开启者的槽位，「已了结的事务视同没有」是对的修法，且不掩盖泄漏——已了结的那个对象随 `UnitOfWorkBehavior.ExecuteAsync` 返回而消失，`Begin()` 的重入判断仍会对真正重叠的用例抛出。

### Remediation 1（针对非阻断项）

| findings | 处置 |
|---|---|
| `ApplyAsync` 由可空改为非空并未被 A1 的理由所强制，且它使 `ItemUseStatus.SlotChanged` 结构上不可产出——而移动侧的 `SlotsChanged` 我特意留给 TASK-07，两者处置不一致 | 记录，不回退。容器现在确实不会拒绝，让返回值继续可空是留一个永不为空的空。改为在 TASK-07 一并下**两条**不可达结论：移动侧的 `SlotsChanged` 与使用侧的 `SlotChanged`。RQ-011 只授权移除前者，后者的处置由 TASK-07 判断 |
| 减项理由「三种情形现由处理器覆盖」对其中两种言过其实 | 已改写为逐条说明（见上）。评审说得对：超量是靠 `PlanAsync` 的钳位而非靠处理器用例，陈旧则是按构造不存在 |
| `Release()` 已是死代码——它永远清不掉调用方的槽位 | 删除。`DomainTransactionLease` 不再持有 scope，其释放只做回滚，并把「为什么不必归还槽位」写进注释 |
| `AGENTS.md` 仍称「事务整个待在账号租约内」，而两条道具命令未声明账号访问方式，`AccountExecutionBehavior` 直接放行不取租约 | 已改为限定说法：仅对**声明了**账号访问方式的命令成立，并点名这两条不是 |
| 效果服务对「消耗指向一个空槽」由拒绝整次使用变为静默跳过 | 改为抛出。今天不可达（效果集只指向被使用道具自己的槽），但静默跳过意味着拿走道具却略过它的代价 |
| 仅存内存的存储在两次取锁之间重建再整体替换容器内容，两个并发写者可能丢更新；旧的 `TransientItemContainer` 全程一把锁 | 记录。属 TASK-02 的 `MemoryItemStore` 形状，且 `CreateTransient` 今天没有生产调用方。交由 Docs 记录，不在本任务改 |
| Relocate 与 Swap 现在会改变 `item_uid`（旧的 `MoveSlotAsync` 保留），若入场租约变脏则 `SaveAsync` 会报 stale | 记录。`TryMarkDirtyExact` 无生产调用方，`SaveAsync` 不会运行；拆堆与合并在本次改动之前就已是这个形状。带入 TASK-06 |
| `ApplyMainMoveAsync` 等四个成员现已是生产死代码，而 `MainInventoryMoveTests` 仍在测它们 | 符合计划：BD-011 / BD-012 在 T-006 退役它们，在本任务动手会越界 |

### 复验

`dotnet test Server/86JP.Server.slnx -m:1` → `916 通过 / 2 跳过 / 0 失败`，与评审独立复跑的结果一致。评审亦复跑 CK-008 12 条、CK-009 13 条、CK-010 2 条、CK-011 4 条、CK-012 零命中，并核对 29 个改动文件全为 CRLF、无孤立 LF。
