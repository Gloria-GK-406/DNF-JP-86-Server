# TASK-07 — 对 `SlotsChanged` 的可达性下结论

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 判定 `ItemMoveStatus.SlotsChanged` 在读与写进入同一用例边界、且用例被账号粒度闸门串行化之后是否仍可达，并按结论处置。仍可达即保留该拒绝路径并记录——这不算失败。
- Referenced spec requirements: RQ-011
- Referenced plan sections: `../plan.md` 的 `### T-007` 全节；BD-010、BD-016；`## Assumptions` 第一条
- Owned boundary: BD-010, BD-016
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs`
  - `Server/tests/Dfo.Server.Protocol.Tests/Game/ItemRefusalCodeTests.cs`
  - `Server/tests/Dfo.Server.Application.Tests/Game/MoveItemSpaceHandlerTests.cs`
- Dependencies: TASK-03
- Required dependency outputs: TASK-03 之后读与写已进入同一用例边界、且用例被账号粒度闸门串行化的事实。
- Acceptance conditions:
  1. 已实际尝试构造使该拒绝原因触发的情形（在同一用例边界内于读之后改变槽位内容），构造方式与其结果落纸。
  2. 不可构造时：移除该拒绝原因，并保留一条断言证明其余拒绝原因、触发条件与所选提示码均未变化。
  3. 可构造时：保留该拒绝路径与其提示码并记录构造方式，结论为「保留」而非失败。
- Task-specific authorization: 沿用根 TaskContract 的 `task` 授权。收窄：不得因「看起来不可达」而移除，必须有构造尝试的实际记录。
- Required methods: 结论性任务：先做构造尝试并留证，再依结论处置。
- Required checks: CK-023、CK-024（见 `../plan.md` 对应任务的 Validation 表，命令与预期结果以该表为准）
- Required evidence: CK-023 的拒绝原因逐条比对与提示码不变的结果；CK-024 的构造尝试记录与结论。

## Contract Amendment A1

Recorded during TASK-01, on evidence found while reading the two protocol slices. Amends
"Required dependency outputs" only; the acceptance conditions are unchanged.

**The premise this task was to test is already known false, and it was written into the task and
into the plan's Assumptions as if it held.** Both `plan.md` (`## Assumptions`, first bullet) and
this file's original "Required dependency outputs" line assert that the account-grained execution
gate serialises the two item protocols for one character. It does not:

`Server/src/Dfo.Server.Protocol/ProtocolModule.cs:136-139` registers both commands with no access
mode at all --

```
services.AddCommandHandler<MoveItemSpaceCommand, ItemMoveResult, MoveItemSpaceCommandHandler>(
    CommandExecutionKind.Direct);
services.AddCommandHandler<UseStackableCommand, ItemUseResult, UseStackableCommandHandler>(
    CommandExecutionKind.Direct);
```

-- so `OnlineUserAccessMode` and `AccountAccessMode` are both `Unscoped`, and
`AccountExecutionBehavior` returns straight through without taking a lease. The comment above the
registration says so in as many words: "neither reads or writes anything the account owns, so
neither takes it." `MoveItemSpaceHandler` takes no gate either; it goes from the session directory
to the bus.

Consequence for this task: only the "read and write enter the same use case boundary" half of the
premise will hold after TASK-03. Serialisation does not. The task therefore starts from the
expectation that `SlotsChanged` is **still reachable**, and its work is to construct the case and
record it rather than to remove the refusal path.

`plan.md` already provides for exactly this outcome — T-007 step 3 and the second half of the
Assumptions bullet both say a reachable `SlotsChanged` is retained and recorded, and is not a
failure. Nothing about the plan needs revising, and no downstream task loses its premise: the
transaction's all-or-nothing guarantee does not depend on serialisation.

Amended "Required dependency outputs": TASK-03's outcome that a move's read and write occur inside
one use case boundary. **No serialisation is available**, and none is to be added by this task —
introducing a gate would change the concurrency behaviour of two live protocols, which is outside
this Spec.


## Execution Result 1

### CK-024：构造尝试的做法与结果

尝试的是 A1 预期的那一种情形：**同一角色的两条移动同时到达**。做法写成了一条留在库里的用例，`MoveItemSpaceHandlerTests.Two_moves_arriving_at_once_are_both_carried_out_and_neither_is_refused`——两条命令各在自己的流上、各开自己的用例边界，经真实的命令处理器并发分派到同一个角色的同一个容器上，两条都盯着同一个目标槽。

**结果：构造不出来。两条都得到 `Moved`，一条都没有被拒绝。**

原因与 A1 预期的不同，值得说清楚。A1 说串行化不存在，因而预期该拒绝仍可达——这一半判断至今仍然正确：两条道具命令确实没有取任何账号租约，两条确实是并发跑的。**不可达的原因是 TASK-03 把产生它的那道检查一并拆掉了**：旧的 `ApplyMainMoveAsync` 在它自己的数据库事务里**重读**被顶掉的那一格，发现变了就答 false，处理器据此回 `SlotsChanged`。现在的移动是「一次读 + 若干次写」落在同一个用例边界内，没有第二次读去和第一次读较劲，于是后到的那次覆盖先到的那次，两次都算成功。

也就是说：这条拒绝原因不是因为竞态消失了才不可达，而是因为**检测竞态的那道检查不复存在**。这一点必须记下，不能让结论读起来像是并发问题被解决了。

### 处置：按验收条件 2 移除

`ItemMoveStatus.SlotsChanged` 与 `MoveItemSpaceHandler.ErrorCodeFor` 中它的映射一并删除。全库对该标识符零命中（唯一残留是上述用例注释里对它的追述）。

CK-023 的比对结果：移动的对外拒绝原因集合除该项外**逐条不变**，所选提示码也不变——

| 拒绝原因 | 提示码 | 变化 |
|---|---|---|
| `ListNotServed` | `NotMovable` (23) | 不变 |
| `ItemCannotOccupySlot` | `NotMovable` (23) | 不变 |
| `PartialSwapNotAllowed` | `NotMovable` (23) | 不变 |
| `DisplacedCannotOccupySlot` | `NotMovable` (23) | 不变 |
| `MergeExceedsStackLimit` | `StackLimitReached` (17) | 不变 |
| `NoSession` | `Silent` (2) | 不变 |
| `NoBoundContainer` | `Silent` (2) | 不变 |
| `BothSlotsEmpty` | `Silent` (2) | 不变 |
| `SlotsChanged` | `Silent` (2) | **移除** |

`ItemRefusalCodeTests` 中除删去点名 `SlotsChanged` 的那一行断言外未作任何改动，其余 9 条断言原样通过。该枚举是穷举匹配的（`_ =>` 分支抛出），并有用例走遍它，所以少一个成员不会让任何拒绝悄悄落入默认。

### 一条并发行为的实际变化，据实记下

移除的是一个**对外只回静默帧**的拒绝，但静默帧与成功帧的字节并不相同（`[0x00, 2, srcList, dstList]` 对 11 字节的成功体）。所以在「同一角色的两条移动真的并发到达」这一情形下，客户端从前收到一条静默失败、现在收到一条成功。

这条情形今天是否会真的发生：一个角色绑在一条连接上，而该连接的帧由 TouchSocket 顺序分派，所以两条移动不会同时进入。**该结论依据的是连接层的顺序分派，不是账号闸门**——A1 已证明账号闸门在这两条命令上不存在。这一点写在这里，是为了让后来者知道这条保证挂在哪儿：哪天有人让一个角色可以从两条连接被操作，或者让帧并行分派，这个洞就会张开，而现在没有任何东西会因此变红。

### 使用协议一侧的同一事实（记录，不处置）

`ItemUseStatus.SlotChanged` 现在同样不可产出：容器的消耗不再会被拒绝（TASK-03 把 `ApplyAsync` 的返回由可空改为非空）。**本任务不移除它**——RQ-011 只授权了移动一侧的那一个，收窄条款也要求有构造尝试才可移除，而本任务的构造尝试是针对移动的。留给变更评审定夺：或按同一理由移除，或补一条说明它为何保留。

## Executor Self-check 1

| Acceptance condition | 结果 |
|---|---|
| 1. 已实际尝试构造，做法与结果落纸 | 通过——尝试写成常驻用例，结果与原因均记录，且原因与 A1 的预期不同，已据实更正 |
| 2. 不可构造时移除，并保留断言证明其余拒绝原因与提示码不变 | 通过——见上表；`ItemRefusalCodeTests` 9 条断言原样通过 |
| 3. 可构造时保留 | 不适用 |

### Validation

| ID | 命令 | 结果 |
|---|---|---|
| CK-023 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests -m:1 --filter FullyQualifiedName~ItemRefusalCodeTests` | `已通过! - 失败: 0，通过: 9`。逐条比对见上表 |
| CK-024 | 构造尝试记录与结论 | 本节即是 |
| 回归 | `dotnet test Server/86JP.Server.slnx -m:1` | `940 通过 / 2 跳过 / 0 失败`（新增构造尝试 1 条，删去点名该项的断言不改变用例数） |
