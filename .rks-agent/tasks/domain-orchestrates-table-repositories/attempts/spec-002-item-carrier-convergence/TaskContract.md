---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-6, SC-7]
depends_on: []
---

# TaskContract: item 载体收敛与无用构造退役

## Context

上一次交付（spec-001）把九个仓储退回单表读写、把跨表组装移进领域层。这一次处理它留下的另一半：
一批为「副本运行时的临时背包」准备、但至今没有任何生产调用方的构造。

四个类型：

| 类型 | 位置 | 行数 | 生产调用方 |
|---|---|---:|---|
| `MemoryItemStore` | `Application/Inventory/MemoryItemStore.cs` | 239 | 无（`ApplicationModule.cs:114` 注册，无人解析）|
| `TransientItemContainer` | `Application/Inventory/ItemContainers.cs:329` | 41 | 无（只被 `ItemContainerFactory` 造）|
| `ItemContainerFactory` | `Application/Inventory/ItemContainers.cs:364` | 6 | 无（`ApplicationModule.cs:115` 注册，无人解析）|
| `IItemContainerFactory` | `Abstractions/Inventory/IItemContainer.cs:172` | 4 | 无 |

它们服务的那个东西不存在：本作没有副本运行状态，`Docs/DungeonEntryNotes.md` 写着
「副本运行状态：没有。没有 run、没有实例、没有房间、没有迷宫模型」。

`MemoryItemStore` 另有一重身份——它是八个测试文件里的内存替身。其中三个是**专为测试这几个待退役
类型而存在**的，随之一起走；另外五个只是拿它当便宜的 `IItemStore` 用，退役后需要另有着落。

## Goal

把这四个没有生产调用方的构造从生产代码里退役，并让依赖过 `MemoryItemStore` 的测试仍然成立。

## Scope

- 删除 `MemoryItemStore`、`TransientItemContainer`、`ItemContainerFactory`、`IItemContainerFactory`
  及其 DI 注册。
- 删除只为测试它们而存在的三个测试文件：
  `Application.Tests/Inventory/MemoryItemStoreTests.cs`（15 例）、
  `Application.Tests/Inventory/TransientContainerNeedsNoIdentifierTests.cs`（2 例）、
  `Application.Tests/Inventory/ItemContainerCarrierEquivalenceTests.cs`（6 例）。
- 让另外五个把 `MemoryItemStore` 当替身用的测试文件改用别的东西，语义不变：
  `Application.Tests/Characters/OutsideTransactionTests.cs`、
  `Application.Tests/Characters/SelectCharacterEntryLifecycleServiceTests.cs`、
  `Application.Tests/Inventory/CharacterContainerDomainServiceTests.cs`、
  `Application.Tests/Inventory/ItemUseFixtures.cs`、
  `Persistence.Tests/Inventory/TwoCarriersInOneTransactionTests.cs`。
- 退役后重新证明对外可观察行为未变。

## Non-Goals

- 不给副本运行时的临时背包补一个实现。要不要有临时背包是一个产品决定，不是这次的事。
- 不动 `PersistentItemContainer` 与 `StoreBackedItemContainer`：它们有生产调用方。
- 不动 spec-001 留下的九条一致性欠账（见 `attempts/spec-001-.../reviews/change-review.md` 第五节）。

## Constraints

| # | Constraint |
|---|---|
| C-1 | 对外可观察行为一字不变；协议帧的字节与时序与退役前逐字相同 |
| C-2 | 既有测试的语义只作因类型消失而必需的机械调整；不得借退役之名放宽任何断言 |
| C-3 | 删除任何东西之前先证明它在生产代码里没有调用方，证明写进任务记录 |
| C-4 | 全程不产生任务级提交 |
| C-5 | 不得改动 `Tool/` 与 `.worktrees/legacy-reference/` |
| C-6 | C# 文件 CRLF、无 BOM UTF-8；net10.0、`TreatWarningsAsErrors` |

## Success Conditions

| # | Condition | Evidence |
|---|---|---|
| SC-A | 四个构造在 `Server/src/` 下零命中 | 逐名检索 `Server/src/`，命中数为 0 |
| SC-B | 删除的每一个都事先被证明无生产调用方 | 任务记录里逐个列出检索命令与结果 |
| SC-C | 依赖过 `MemoryItemStore` 的五个测试文件仍然成立，断言未被放宽 | `git diff` 中这五个文件的 `Assert.` 增删逐条交代 |
| SC-D | 对外可观察行为未变 | 协议用例全过且断言未改；`Server/tests/Dfo.Server.Protocol.Tests` 的字节与十六进制字面量与退役前一字节不差 |
| SC-E | 全量不低于退役前，减量全部来自被删的三个测试文件 | `dotnet test Server/86JP.Server.slnx -m:1`；退役前 1037 通过 / 2 跳过 / 0 失败，减量应恰为 23 |

## Authorization

| Grant | Evidence | Authorized scope |
|---|---|---|
| task | 用户原话：「按multispec来吧，第一个干掉除了 item 以外的大部分问题。然后第二个 spec 完成 item 的切分，退役掉大量无用的东西。」以及「好，那就开始任务吧。整个任务会比较大，但是应该是没难度的。全部完成后再和我说吧」 | 本 Spec 的执行与验证；不覆盖合并、推送、PR、分支或工作树清理 |

## Assumptions

| # | Assumption |
|---|---|
| A-1 | 五个把 `MemoryItemStore` 当替身用的测试，其真正要证的事与「这个替身是内存的」无关；改用真实的 SQLite 存储或一个测试程序集自有的替身，都不改变它们证的东西。若某一处不成立，以保住该测试要证的事为准。 |

## Verification Environment

- `dotnet test Server/86JP.Server.slnx -m:1`
- `PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`
- 退役前：1037 通过 / 2 跳过 / 0 失败
- 已知假红：`Host.Tests` 与 `Networking.Tests/Integration/` 在满载全量下会撞 10 秒预算。判据是隔离复跑。
