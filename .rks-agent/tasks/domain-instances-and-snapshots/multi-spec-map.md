---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-001
last_transition: child-created
transition_evidence: >-
  root_evaluation：十条成功条件全部 not_started，十条约束全部 aligned，缺口逐条记在
  Success Condition Progress 中并各自指到文件与行。据此 gaps-remain 进入 successor_decision。
  successor_decision：最小稳定成果是「物品规则收回背包实例」——它有一个连贯的验收边界
  （到货落地只剩一处、协议工程不再持有物品规则），可独立评审，且回滚不牵动其他成果。
  该成果落在根 task 授权范围内，未扩张范围，故 safe-successor 进入 child_modeling。
  child_modeling：本次派生对四块的切法作了一处调整并记录理由。根路由决定原设想的第一块是
  「仓库获得两种产物」的纯增量地基，建模时判定它**没有可独立观察的验收**——其成果会是一组
  没有调用方的接口，与根 SC-1「两者在生产代码中都有调用方」直接冲突。按根路由决定
  Reroute conditions 中已写明的条件（「若某一块……无独立回滚价值，把它降级」），
  该地基不单独立项，其内容并入需要它的成果。四块随之重切为：
  spec-001 物品规则收回背包（SC-2、SC-3）；spec-002 人物实例（SC-4 及 SC-1 的实例一半）；
  spec-003 规则服务去身份（SC-5）；spec-004 快照成为仓库产物并改为领域形状（SC-6 及 SC-1 的快照一半）。
  四块合起来仍覆盖全部十条成功条件，数量与串行次序不变。
  child-created 前的校验：无 active 子成果、无 running 行、attempts/ 下无同名目录；
  子契约与子路由决定各一份、均已写全且 state: ready；子契约 parent 指向 ../../TaskContract.md，
  contributes_to = [SC-2, SC-3, SC-8, SC-9, SC-10]，depends_on 为空；
  两份工件的路径均证明包含在 task_root 之下。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-instances-and-snapshots/.rks-agent/tasks/domain-instances-and-snapshots
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-instances-and-snapshots
  branch: domain-instances-and-snapshots
  baseline: 531c0bbd8a35fd579c3add6e478965307bd083d4
  baseline_evidence: >-
    git rev-parse HEAD = 531c0bbd8a35fd579c3add6e478965307bd083d4；
    工作树内 git status --short 只有三份已声明的持久工件（未跟踪）；
    源分支 main 仍为同一提交。
    测试基线取自 main @ 531c0bb：dotnet test Server/86JP.Server.slnx -m:1 =
    1132 通过 / 2 跳过 / 0 失败；dotnet build Server/86JP.Server.slnx 零警告零错误。
    PVF_ARCHIVE_PATH 与 Pvf__ArchivePath = D:/project/DNF/DNFClient-debug/Script.pvf。
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
| SC-1 | 仓库类同时提供快照与实例两种产物，两者都有生产调用方 | not_started | none | 人物仓库只返回快照，没有可操作的实例；物品仓库返回实例，其快照在实例上而不在仓库上 | aligned |
| SC-2 | 「到货的东西放哪个格子」在整个代码库中只有一处定义 | not_started | none | `ItemArrival` 的三分支写了两遍：`GrantTutorialRewardCommand.cs:70` 与 `DailyRefillService.cs:105`。两处的到货**规则**不同（`ForGrant` 认叠加上限并溢出，`ForDailyRefill` 并入任一堆），失败语义也不同（前者静默跳过，后者抛），但把判决变成写入的那一段相同——spec-001 收敛的是后者 | aligned |
| SC-3 | 判定物品移动是否允许的规则不在协议工程内 | not_started | none | `MoveItemSpaceCommandHandler.PlanAsync`（`MoveItemSpaceSlice.cs:313`，约 50 行规则）在 `Dfo.Server.Protocol` 内 | aligned |
| — | *（建模期发现，记在此处以免遗失）* | — | — | 两处既有注释与设计裁定相反，须随 spec-001 一并订正：`IItemContainer.cs` 接口注释（约 :78-93）「读和写就是全部，拼装属于上层」，其理由「其下的存储也要被告知」在今天已不成立，因为 `ItemContainerOperations` 正是在不告知存储的前提下拼装的；以及 `ItemContainers.cs:51`「那些是协议的问题」 | aligned |
| SC-4 | 存在人物实例，且人物侧至少一个业务用例经由它表达 | not_started | none | 没有人物实例；`LoadForSelectionAsync` 返回 `ActiveCharacterState`，一个无行为的 record | aligned |
| SC-5 | 规则服务不解析身份 | not_started | none | `IItemUsabilityDomainService.CanUseAsync(characterId, …)` 在第 71 行解析身份后才判断 | aligned |
| SC-6 | 快照是一组领域记录；包的段布局发生在协议侧 | not_started | none | `ActiveCharacterState` 是一个包形状的大 record，其注释自承产出 subtype-0 / subtype-1 记录（`CharacterEntryAssembly.cs:104`、`:130`） | aligned |
| SC-7 | 对外协议行为一字不变 | not_started | none | 尚未改动 | aligned |
| SC-8 | 每个子成果在被接受时是绿的 | not_started | none | 尚无子成果 | aligned |
| SC-9 | 上一轮确立的持久层形状未回退 | not_started | none | 起点即满足（43 张表、一类一表、组装在领域层），须在每个子成果后复核未回退 | aligned |
| SC-10 | 设计文档与落地一致 | not_started | none | 文档已写但尚无落点；三处「等有实物再判」的条目待相应子成果裁定并写回 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-item-rules-in-the-bag/TaskContract.md` | 物品规则收回背包实例：背包获得放入、补充、移动三个领域动词，各自内部调用注入的规则服务；到货判决的落地在全仓只剩一处；协议工程不再持有物品规则；三处调用方各自的失败语义原样保留 | `SC-2`, `SC-3` | `running` | `pending` | `active` |
