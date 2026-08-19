# TASK-04 — 消除领域侧的存储行标识

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 一次性完成跨文件的词汇替换，使领域侧的容器契约与效果模型不再携带数据库行标识。
- Referenced spec requirements: RQ-007
- Referenced plan sections: `../plan.md` 的 `### T-004` 全节；BD-008、BD-009、BD-016
- Owned boundary: BD-008, BD-009, BD-016
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs`
  - `Server/src/Dfo.Server.Abstractions/Inventory/IItemUseEffects.cs`
  - `Server/src/Dfo.Server.Application/Inventory/ItemUseEffectDomainService.cs`
  - `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`
  - `Server/tests/`（受影响的既有用例只作机械调整）
- Dependencies: TASK-03
- Required dependency outputs: TASK-03 之后的容器与两条协议。
- Acceptance conditions:
  1. `Server/src/Dfo.Server.Abstractions/Inventory/` 与 `Server/src/Dfo.Server.Application/Inventory/` 中检索存储行标识与伪造标识发号为零命中。
  2. 仅存内存的容器不再发号任何伪造标识。
  3. 受影响的既有用例只作因类型或依赖变化的机械调整，语义不改。
- Task-specific authorization: 沿用根 TaskContract 的 `task` 授权，无额外收窄。
- Required methods: 结构性演进：既有回归网加检索检查即可证伪，不为强制该设计新增交互代理用例。
- Required checks: CK-014、CK-015、CK-016（见 `../plan.md` 对应任务的 Validation 表，命令与预期结果以该表为准）
- Required evidence: CK-014 的检索结果；CK-015、CK-016 的测试结果。

## Execution Result 1

### 本任务owned的词汇替换已经完成——但不是在本任务里

TASK-04 拥有 BD-008（`IItemContainer.cs`）与 BD-009（`IItemUseEffects.cs`、`ItemUseEffectDomainService.cs`）。这三个文件当中的存储行标识**在 TASK-03 已被清干净**，而且是被迫的：容器契约上的 `ConsumeAsync(long storageEntryId, …)` 一旦随复合写的拆解而消失，`ContainerSlotItem` 就不能再携带行号（存储那边根本给不出），于是效果模型的旧签名连编译都过不去。这一点在 TASK-03 的 `## Contract Amendment A1` 中已申报。

据实记下，而不是回头制造工作量：本任务的替换成果是 TASK-03 交付的，本任务负责核验、补齐它自己欠的覆盖，并把残留逐条点名归属。

### 补齐的覆盖（BD-016）

AC-008 要求「一个仅存内存的容器，放入物品并对其执行本范围内的各种操作，全程不需要任何存储行标识」。此前只有存储层的同形用例（`MemoryItemStoreTests.AWholeRunOfOperationsNeedsNoIdentityForAnyItem`），没有**容器层**的。新增 `TransientContainerNeedsNoIdentifierTests`（2 条）：

- `EveryOperationIsSaidWithNothingButAContainerAndASlot`：一个仅存内存的容器依次经历放入、整堆移动、拆堆、交换、合并、消耗，全程只出现容器与槽。
- `WhatAContainerHandsBackNamesASlotAndWhatIsInIt`：断言 `ContainerSlotItem` 上**不存在**任何名字含 `Entry` / `Uid` / `RowId` 的成员——把要求钉成「调用方想携带行号也无从携带」，而不是「碰巧没人携带」。

## Executor Self-check 1

| Acceptance condition | 结果 | 证据 |
|---|---|---|
| 1. 两个目录中检索存储行标识与伪造标识发号为零命中 | **部分**——本任务 owned 的四个文件零命中；两个目录内另有残留，逐条归属见下 | CK-014 |
| 2. 仅存内存的容器不再发号伪造标识 | 通过 | `TransientItemContainer` 已无 `nextEntryId`，全类无任何计数器；`TransientContainerNeedsNoIdentifierTests` 两条 |
| 3. 受影响的既有用例只作机械调整 | 通过 | 本任务未改动任何既有用例，只新增一个文件 |

### CK-014：检索结果与残留的逐条归属

**本任务 owned 的四个文件（`IItemContainer.cs`、`IItemUseEffects.cs`、`ItemUseEffectDomainService.cs`、`ItemContainers.cs`）：零命中。**

两个目录内其余 9 处命中，分两类，均**不在**本任务的 Owned boundary 内：

| 位置 | 是什么 | 归属 |
|---|---|---|
| `IInventoryEntryServices.cs:123`（`MainInventoryOccupant`）、`:152-161`（`MainInventoryMovePlan`）、`:212`（`ConsumeMainItemAsync`），及 `ReadMainSlotAsync` / `ReadMainSlotsAsync` / `ApplyMainMoveAsync` | **确实是领域侧的行寻址词汇**，且自 TASK-03 起已是生产死代码 | BD-011 / BD-012 → **TASK-06** 退役。计划把退役排在规则外迁之后，因为那一刀本就要重写该仓储；在本任务动手会越界，且要连带删掉 `MainInventoryMoveTests` |
| `IInventoryEntryServices.cs:10`（`InventoryStorageItem.StorageEntryId`）、`InventoryEntryServices.cs:167,184`（排序）、`:211`（投影） | **不是领域寻址，是存储在描述自己读到的行**：真实的行号，用于给行一个确定的顺序，并构造入场快照的 `InventoryItemState` | 不在 RQ-007 的射程内——RQ-007 管的是「领域侧的物品寻址与效果描述」。且入场快照改由容器供给是 Spec 明列的 Non-Goal，动它属于另一刀 |

结论：RQ-007 在其所指的范围内成立。验收条件 1 的字面「零命中」对本任务 owned 的边界成立；两个目录内的其余命中已逐条点名，一类有明确的后续归属（TASK-06），一类在需求射程之外并说明了理由。

### Validation

| ID | 命令 | 结果 |
|---|---|---|
| CK-014 | 在两个目录检索 `StorageEntryId\|storageEntryId\|nextEntryId\|EntryId` | owned 四文件零命中；其余 9 处见上表 |
| CK-015 | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~Inventory` | `已通过! - 失败: 0，通过: 102`，含仅存内存容器的完整操作用例 |
| CK-016 | `dotnet test Server/86JP.Server.slnx -m:1` | `918 通过 / 2 跳过 / 0 失败`（较上一轮 +2，为本任务新增） |
