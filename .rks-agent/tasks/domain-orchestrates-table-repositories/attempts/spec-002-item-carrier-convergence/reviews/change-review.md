---
kind: rks-change-review
spec: ./../spec.md
contract: ./../TaskContract.md
scope: spec-002 的改动（与 spec-001 的未提交成果同处一个工作树，本次只评审前者）
state: approved
rounds: 2
---

# spec-002 变更评审

## 一、第一轮

独立评审者只读核实，不采信执行记录里的数。

### 通过的部分

| 项 | 结论 |
|---|---|
| RQ-001 / RQ-002 | 四个名字在 `Server/src/` 零命中；`CreateTransient` 全仓零命中；两处注册已删 |
| RQ-003 | 回到基线独立验证：四个构造在生产代码里只有自身声明与两处 DI，无第三方调用方 |
| RQ-004（替身） | `VolatileItemStore.cs` 与基线的 `MemoryItemStore.cs` 逐字比对，除命名空间、两处类型名与类注释外**逻辑一字未改** |
| RQ-005 | Protocol.Tests 203/203；相关文件的 `0x` 字面量逐个一致，`Assert.` 计数与基线相同 |
| 截断删除 | `ItemContainers.cs` 尾去 53 行、`IItemContainer.cs` 尾去 11 行，两文件的 using 仍全部在用 |
| 约束 | 无 BOM、100% CRLF、构建 0 警告、`Tool/` 与 `.worktrees/` 零改动 |

### 两条阻塞

被删的两个测试文件里，有四条断言证的是**仍然存在的**东西，删掉它们等于静默拿走保护：

- **CR-1**：`ItemContainerCarrierEquivalenceTests.cs` 内的 `ItemContainerOutsideTransactionTests`
  三条，证的是 `PersistentItemContainer` 的边界——用例外写抛出、用例外读放行、用例内接受且可见。
  契约 Non-Goals 明写「不动 `PersistentItemContainer`」。评审者核实：全仓无替代，
  删掉 `ItemContainers.cs` 里那句拒绝**不会让任何用例转红**。
- **CR-2**：`TransientContainerNeedsNoIdentifierTests.cs` 里的反射守卫，
  断言 `ContainerSlotItem` 不得出现 Entry/Uid/RowId 属性。该类型仍在，该守卫全仓唯一。

### 一处偏离，评审判为合理

契约 SC-E 写的减量是 23（假设三个测试文件全删）。执行者保留了其中 15 例
（`MemoryItemStoreTests` → `VolatileItemStoreTests`），因为它测的是仍然存在的测试替身。

评审判断：**保留合理，不应照契约全删**。`VolatileItemStore` 是全仓唯一的非持久 `IItemStore`，
而服务器真正有的两条规则——易失存储可在用例外更改、任意多个易失参与者可与唯一持久者并存——
只能靠它测；替身若静默失真，`OutsideTransactionTests` 与 `TwoCarriersInOneTransactionTests`
会变成假绿。正确做法是改契约的数，不是删测试。见下方合同修订。

## 二、补救与第二轮

新增 `Application.Tests/Inventory/PersistentContainerBoundaryTests.cs`，把那四条搬进来。
另删掉一句已失去对象的悬空注释，以及一条已成同义反复的用例。

第二轮独立核实结果：

| 项 | 结论 |
|---|---|
| CR-1 三条 | 逐字同断言、同期望值；`Persistent(...)` 的参数完全一致 |
| 建表替换 | 原用两个已被 spec-001 删除的仓储建表，现用 `new SqliteDatabase(path, …)`，其构造即建全表，是原来的超集，用例只依赖 `character_new_items` |
| 已退役的那条 | `ChangingTheTransientContainerOutsideATransactionDoesNotThrow` 未被搬回，全仓零命中——正确，它证的是已退役的载体 |
| CR-2 | 反射守卫逐字相同；去掉原先附带的 Put 不削弱所证，因为守卫只看类型形状 |
| 能红性 | `ItemContainers.cs` 的 `AmbientSession() ?? throw` 是唯一拒绝点，Put 与 Clear 都经此路；删掉它该用例必红 |
| 删掉的同义反复 | 它证的是已退役的载体；存活载体的同四项在 `SelectCharacterEntryLifecycleServiceTests` 有覆盖 |
| 减量 | 实跑 1032 通过 / 2 跳过 / 0 失败 = 1037 − 8 + 4 − 1，新文件实数 4 个 `[Fact]` |
| 回归 | 构建 0 警告 0 错误；新文件无 BOM、纯 CRLF；本轮无假红 |

新 finding：无。

## 三、合同修订（本次评审授权）

**SC-E 的减量由 23 改为 5。** 原数假设三个测试文件全删；实际删两个（8 例）、保留一个（15 例，
测的是仍然存在的测试替身）、搬回四条被误删的保护、删掉一条同义反复，净 −5。
RQ-006 的措辞随之改为「减量的构成逐条可交代，且没有任何仍然生效的保护随之消失」。

## 四、一处备案

`MemoryItemStore` 之所以存在，是为了让 `AmbientStoreAccess` 与容器按 `IsDurable` 分叉。
它退役之后，服务器里没有任何一个非持久的 `IItemStore`——也就是说那两条分叉在生产上都只走一边。
这批分支是否也该退役，是一个与「要不要有临时背包」同源的产品问题，
不在本 Spec 边界内，记在此处。

## 五、结论

**approved，无未解决的阻塞发现。**
