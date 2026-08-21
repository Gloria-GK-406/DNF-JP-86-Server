---
kind: rks-spec
contract: ./TaskContract.md
contract_revision: 1
state: ready
---

# Spec: item 载体收敛与无用构造退役

## Why and Outcome

服务器里住着一批为「副本运行时的临时背包」准备的构造，而副本运行状态并不存在——没有 run、
没有实例、没有房间。它们从未被任何生产代码调用过，却仍然注册在容器里、仍然出现在
`Abstractions` 的公开契约里、仍然需要读它们的人判断一遍「这是不是我要找的东西」。

要的结果是它们不在了：生产代码里找不到，容器里没有，契约里没有，而服务器的对外行为一字未变。

## Task Perspective

- **核心结果**：四个没有生产调用方的构造从生产代码消失。
- **必要支撑**：把它们当内存替身用的五个测试另有着落——不是为了这些测试本身，
  而是因为删掉替身会让它们编译不过，而它们证的事与替身无关，不该跟着一起消失。
- **周边保证**：删之前逐个证明无调用方；删之后重新证明对外行为未变。

## Scope and Non-Goals

### Scope

四个构造及其容器注册；只为测试它们而存在的三个测试文件；五个把其中一个当替身用的测试文件。

### Non-Goals

- 不为临时背包补实现。
- 不动 `PersistentItemContainer` 与 `StoreBackedItemContainer`。
- 不动 spec-001 留下的一致性欠账。

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | `MemoryItemStore`、`TransientItemContainer`、`ItemContainerFactory`、`IItemContainerFactory` 在 `Server/src/` 下没有任何命中。 |
| RQ-002 | 容器里没有这四个之中任何一个的注册。 |
| RQ-003 | 删除的每一个，在删除之前都有「生产代码中无调用方」的检索证据。 |
| RQ-004 | 曾经把 `MemoryItemStore` 当替身用而不是拿它当被测对象的测试，仍然成立，且其断言未被放宽。 |
| RQ-005 | 对外可观察行为不变：协议帧的字节与时序与退役前逐字相同。 |
| RQ-006 | 全量的减量恰好等于被删的三个测试文件所含的用例数，没有别的用例消失。 |

## Acceptance Scenarios

| ID | Given | When | Then |
|---|---|---|---|
| AC-001 | 退役完成后的代码树 | 逐名检索 `Server/src/` | 四个名字各自零命中 |
| AC-002 | 退役完成后的 `ApplicationModule` | 读它注册了什么 | 四个之中一个也没有 |
| AC-003 | 任务记录 | 读它对每一次删除的交代 | 每一个都有检索命令与「无调用方」的结果 |
| AC-004 | 曾用 `MemoryItemStore` 作替身的五个测试文件 | 看它们相对退役前的 `git diff` | `Assert.` 的增删逐条有交代，无一条是放宽 |
| AC-005 | 协议测试与其中的字节字面量 | 跑协议用例并比对字面量 | 全过；字面量与退役前一字节不差 |
| AC-006 | 全量结果 | 与退役前 1037 通过 / 2 跳过 / 0 失败相比 | 减量恰为被删三个文件的用例数，其余不变 |

## Success Evidence

| ID | Evidence |
|---|---|
| EV-001 | 四个名字在 `Server/src/` 下的检索输出为空（AC-001、AC-002）|
| EV-002 | 任务记录中逐个删除对象的无调用方证据（AC-003）|
| EV-003 | 五个测试文件的 `Assert.` 增删逐条交代（AC-004）|
| EV-004 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj` 全过，且该目录字节字面量与退役前一致（AC-005）|
| EV-005 | `dotnet test Server/86JP.Server.slnx -m:1` 的结果与减量构成（AC-006）|

## Constraints

| # | Constraint |
|---|---|
| C-1 | 对外可观察行为一字不变 |
| C-2 | 既有测试语义只作因类型消失而必需的机械调整 |
| C-3 | 删除之前先证明无生产调用方 |
| C-4 | 全程不产生任务级提交 |
| C-5 | 不得改动 `Tool/` 与 `.worktrees/legacy-reference/` |
| C-6 | C# 文件 CRLF、无 BOM UTF-8；net10.0、`TreatWarningsAsErrors` |
