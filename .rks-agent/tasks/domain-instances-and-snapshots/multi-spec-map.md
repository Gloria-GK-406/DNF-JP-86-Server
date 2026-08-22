---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-002
last_transition: child-created
transition_evidence: >-
  root_evaluation：在基线 7478e2e 上重算。SC-2 与 SC-3 已满足并各有证据；
  SC-1、SC-7、SC-8、SC-9、SC-10 为 in_progress；SC-4、SC-5、SC-6 仍 not_started。
  十条约束全部 aligned。据此 gaps-remain 进入 successor_decision。
  successor_decision：最小稳定成果是「人物成为可操作的实例，并因此让最后一个不纯的规则服务变纯」。
  本次把原计划的 spec-002（人物实例）与 spec-003（规则服务去身份）**合成一个成果**，理由已核实：
  全仓签名带身份的规则服务只剩可用性判定一处（槽位、到货、每日补充三个都已是纯的；
  按表仓储与容器注册表带身份，但它们是仓库类不是规则服务），而把它提纯需要有个东西来供身份——
  正是人物实例。分开做会留下一个只有一个方法、且那个方法只为证明实例存在而生的中间态，
  它没有可独立观察的验收，也没有独立的回滚价值。这与根路由决定 Reroute conditions 里
  「若某一块……无独立回滚价值，把它降级」是同一条判据。四块因此成为三块：
  spec-001 物品规则收回背包（已接受）；spec-002 人物实例与规则去身份（SC-4、SC-5，及 SC-1 的实例一半）；
  spec-003 快照成为仓库产物并改为领域形状（SC-6，及 SC-1 的快照一半）。三块合起来仍覆盖全部十条成功条件。
  该成果落在根 task 授权范围内，未扩张范围，故 safe-successor 进入 child_modeling。
  child-created 前的校验：无 active 子成果、无 running 行、attempts/ 下无同名目录；
  子契约与子路由决定各一份、均已写全且 state: ready；子契约 parent 指向 ../../TaskContract.md，
  contributes_to = [SC-1, SC-4, SC-5, SC-8, SC-9, SC-10]，depends_on 为空；
  两份工件的路径均证明包含在 task_root 之下。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-instances-and-snapshots/.rks-agent/tasks/domain-instances-and-snapshots
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-instances-and-snapshots
  branch: domain-instances-and-snapshots
  baseline: 7478e2eb80eb130802ace4f5798265959b557e84
  baseline_evidence: >-
    git rev-parse HEAD = 7478e2eb80eb130802ace4f5798265959b557e84，提交后 git status --short 为空；
    该提交建立在起点 531c0bb 之上，源分支 main 仍为
    531c0bbd8a35fd579c3add6e478965307bd083d4 未变。
    本基线的证据：dotnet build Server/86JP.Server.slnx 零警告零错误；
    dotnet test Server/86JP.Server.slnx -m:1 = 1151 通过 / 2 跳过 / 0 失败。
    起点 main @ 531c0bb = 1132 通过 / 2 跳过 / 0 失败，净增 19 条且逐条可解释。
    PVF_ARCHIVE_PATH 与 Pvf__ArchivePath = D:/project/DNF/DNFClient-debug/Script.pvf。
    已知假红：Dfo.Server.Host.Tests 满载下撞 10 秒预算，判据是隔离复跑。
---

# Multi-Spec Map

## Multi-Spec Boundary

四个成果各有独立的可接受边界与独立的回滚边界：

1. **仓库两种产物**是纯增量——不删任何东西，可独立接受，回滚只需撤掉新增。
2. **物品侧迁实例**可独立验收（那处 `ItemArrival` 重复塌成一处是可当场清点的结果），
   回滚不影响 (1)。
3. **人物实例与规则拆分**依赖 (1) 的产物，但不依赖 (2)。
4. **快照重塑**不依赖前三块中的任何一块，体量最大（`CharacterEntryAssembly` 624 行 +
   `InventoryEntryAssembly` 578 行 + 三份整记录表征用例），风险最高，单独回滚的价值最高。

把四块压成一个 Spec，会让第四块的失败连带撤掉前三块已经可以独立成立的成果。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 仓库类同时提供快照与实例两种产物，两者都有生产调用方 | in_progress | 提交 7478e2e：背包仓库交出的实例现在是一个**可操作**的东西——三个领域动词，三处生产调用方 | 人物仓库仍只返回快照，没有可操作的实例；快照作为仓库产物、且为领域形状，尚未成立 | aligned |
| SC-2 | 「到货的东西放哪个格子」在整个代码库中只有一处定义 | satisfied | 提交 7478e2e：全仓 `case ItemArrival.` 在生产代码中共 3 处，同属 `ItemContainers.LandAsync` 的三种到货各一支；教程奖励与每日补充两条路都经由它。两处到货**规则**本就不同且都保留（`ForGrant` 认叠加上限并溢出，`ForDailyRefill` 并入任一堆），收敛的是把判决变成写入的那一段 | 无 | aligned |
| SC-3 | 判定物品移动是否允许的规则不在协议工程内 | satisfied | 提交 7478e2e：`Dfo.Server.Protocol` 对摆放、槽位、到货三类规则服务的引用归零；`MoveItemSpaceCommandHandler` 只剩主列表判断、取背包、转发、把领域拒绝翻成客户端码。五种拒绝与两种非拒绝逐条可构造，其中半栈交换与拆分为本轮新补——它们此前全仓无人构造 | 无 | aligned |
| — | *（建模期发现，已随 spec-001 处理）* | 已处理 | 提交 7478e2e：两处注释均已重写，全仓已无「拼装属于上层」「是协议的问题」这类指向旧划分的说法 | 无 | aligned |
| SC-4 | 存在人物实例，且人物侧至少一个业务用例经由它表达 | not_started | none | 没有人物实例；`LoadForSelectionAsync` 返回 `ActiveCharacterState`，一个无行为的 record | aligned |
| SC-5 | 规则服务不解析身份 | not_started | none | `IItemUsabilityDomainService.CanUseAsync(characterId, …)` 在第 71 行解析身份后才判断 | aligned |
| SC-6 | 快照是一组领域记录；包的段布局发生在协议侧 | not_started | none | `ActiveCharacterState` 是一个包形状的大 record，其注释自承产出 subtype-0 / subtype-1 记录（`CharacterEntryAssembly.cs:104`、`:130`） | aligned |
| SC-7 | 对外协议行为一字不变 | in_progress | 提交 7478e2e：三份整记录表征用例的 `git diff` 为空；每日补充原有九条期望值一字未动；全量 1151/2/0。第一轮评审曾发现一处**会**改变下发内容的回归（补充后下发陈旧数量，见 A-1），已修并加守卫 | 人物侧与快照侧尚未改动；根层的实机走查待全部落地后进行 | aligned |
| SC-8 | 每个子成果在被接受时是绿的 | in_progress | 提交 7478e2e 处：构建零错误零警告，全量 1151/2/0 | 其余三个子成果尚未开始 | aligned |
| SC-9 | 上一轮确立的持久层形状未回退 | in_progress | 提交 7478e2e 处复核：仍 43 张表；背包经既有按表类与既有环境事务落地，未自建表；本轮反而**减少**了一处重叠——`ICharacterItemTable` 上两个直接写 `items` 的成员随其最后一个调用方一并删除 | 其余三个子成果后须各复核一次 | aligned |
| SC-10 | 设计文档与落地一致 | in_progress | 提交 7478e2e：文档新增 R-7（动词返回前应用已决定的写入）与「Settled by the item-side implementation」一节，记下四处确证或修正；`ItemContainerOperations` 那段「What does not move」已补上此前漏记的第二处注释 | 三处「等有实物再判」的条目仍未判；R-7 尚有一处已知未覆盖面（`ConsumeAsync`）已写入文档 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-item-rules-in-the-bag/TaskContract.md` | 物品规则收回背包实例：背包获得放入、补充、移动三个领域动词，各自内部调用注入的规则服务；到货判决的落地在全仓只剩一处；协议工程不再持有物品规则；三处调用方各自的失败语义原样保留 | `SC-2`, `SC-3` | `sealed` | `completed` | `accepted as 7478e2e` |
| `2` | `spec-002` | `attempts/spec-002-the-character-instance/TaskContract.md` | 人物成为可操作的实例：由人物仓库按需交出（无注册故无生命周期，见契约 revision 2 与 R-4 的改写）；「能不能用这件物品」经由它表达；全仓最后一个签名带身份的规则服务随之拆成「实例供身份、规则服务作判决」两半 | `SC-4`, `SC-5` | `running` | `pending` | `active` |
