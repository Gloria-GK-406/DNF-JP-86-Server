---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-003
last_transition: child-created
transition_evidence: >-
  root_evaluation：在基线 078d762 上重算。SC-2、SC-3、SC-4、SC-5 已满足并各有证据；
  SC-1、SC-7、SC-8、SC-9、SC-10 为 in_progress；只剩 SC-6 仍 not_started。
  十条约束全部 aligned。据此 gaps-remain 进入 successor_decision。
  successor_decision：最小稳定成果是「快照成为领域形状、段布局归协议」，即最后一块。
  该成果落在根 task 授权范围内，未扩张范围，故 safe-successor 进入 child_modeling。
  child_modeling：建模时逐一核实并**推翻了根契约与设计文档共有的一处描述**——
  它们都说包形状的那个 record 是 `ActiveCharacterState`，而它其实已经是领域形状
  （身份、进度、疲劳、位置）。真正以协议段命名的是两族：存储侧的
  `CharacterSubtype0/1StorageRecord`（由装配器从四张与两张表按段序拍平），
  与入场侧的 `CharacterSubtype0/1EntryState`（由一段 45 字段的按位复制从前者转来，
  每字段做一次位宽收窄）。根契约已推进到 revision 2、设计文档 R-5 已订正，两处都写明了理由。
  这是本根任务第三次「建模期核实推翻了先前的判断」，前两次分别是
  spec-002 的「两条注释级规则其实已有守卫」与本次；判据都是真改真跑或逐类型清点，不是复述。
  child-created 前的校验：无 active 子成果、无 running 行、attempts/ 下无同名目录；
  子契约与子路由决定各一份、均已写全且 state: ready；contributes_to = [SC-1, SC-6, SC-7, SC-8, SC-9, SC-10]，
  depends_on 为空；两份工件的路径均证明包含在 task_root 之下。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-instances-and-snapshots/.rks-agent/tasks/domain-instances-and-snapshots
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-instances-and-snapshots
  branch: domain-instances-and-snapshots
  baseline: 078d76228328c2ce4417947d2ee8db518a59b5f2
  baseline_evidence: >-
    git rev-parse HEAD = 078d76228328c2ce4417947d2ee8db518a59b5f2，提交后 git status --short 为空；
    该提交建立在 7478e2e 之上，源分支 main 仍为
    531c0bbd8a35fd579c3add6e478965307bd083d4 未变。
    本基线的证据：dotnet build Server/86JP.Server.slnx 零警告零错误；
    dotnet test Server/86JP.Server.slnx -m:1 = 1172 通过 / 2 跳过 / 0 失败。
    历次基线：main @ 531c0bb = 1132/2/0；7478e2e = 1151/2/0。净增逐块可解释。
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
| SC-1 | 仓库类同时提供快照与实例两种产物，两者都有生产调用方 | in_progress | 提交 7478e2e：背包仓库交出的实例是可操作的东西——三个领域动词，三处生产调用方。提交 078d762：人物仓库同时交出快照（`LoadForSelectionAsync` 等）与实例（`Get`），实例有生产调用方（用消耗品） | **只差一样**：快照仍是包形状的一整个 record，不是一组领域记录 | aligned |
| SC-2 | 「到货的东西放哪个格子」在整个代码库中只有一处定义 | satisfied | 提交 7478e2e：全仓 `case ItemArrival.` 在生产代码中共 3 处，同属 `ItemContainers.LandAsync` 的三种到货各一支；教程奖励与每日补充两条路都经由它。两处到货**规则**本就不同且都保留（`ForGrant` 认叠加上限并溢出，`ForDailyRefill` 并入任一堆），收敛的是把判决变成写入的那一段 | 无 | aligned |
| SC-3 | 判定物品移动是否允许的规则不在协议工程内 | satisfied | 提交 7478e2e：`Dfo.Server.Protocol` 对摆放、槽位、到货三类规则服务的引用归零；`MoveItemSpaceCommandHandler` 只剩主列表判断、取背包、转发、把领域拒绝翻成客户端码。五种拒绝与两种非拒绝逐条可构造，其中半栈交换与拆分为本轮新补——它们此前全仓无人构造 | 无 | aligned |
| — | *（建模期发现，已随 spec-001 处理）* | 已处理 | 提交 7478e2e：两处注释均已重写，全仓已无「拼装属于上层」「是协议的问题」这类指向旧划分的说法 | 无 | aligned |
| SC-4 | 存在人物实例，且人物侧至少一个业务用例经由它表达 | satisfied | 提交 078d762：`ICharacter` / `PlayingCharacter`，由 `ICharacterDomainService.Get` 交出；「能不能用这件物品」经由它表达，`UseStackableCommandHandler` 不再持有可用性服务。接缝本身有三条用例，用工厂而非现成实例构造仓库 | 无 | aligned |
| SC-5 | 规则服务不解析身份 | satisfied | 提交 078d762：全仓十个规则/判定服务接口逐个清点，签名中无一出现 `CharacterId` 或 `AccountId`。唯一带账号形状的是 `IDungeonEffectRuleService.CanCarryOut(int recordedAccountId, …)`——那是被比较的值而非要解析的身份，此区分已写入 R-6 | 无 | aligned |
| SC-6 | 快照是一组领域记录；包的段布局发生在协议侧 | not_started | none | `ActiveCharacterState` 是一个包形状的大 record，其注释自承产出 subtype-0 / subtype-1 记录（`CharacterEntryAssembly.cs:104`、`:130`） | aligned |
| SC-7 | 对外协议行为一字不变 | in_progress | 提交 7478e2e 与 078d762：三份整记录表征用例的 `git diff` 全程为空；每日补充原有九条与可用性原有六条的期望值一字未动；全量 1172/2/0。物品侧曾有一处会改变下发内容的回归（补充后下发陈旧数量），由评审捕获并已加守卫 | 快照侧尚未改动，那是下发帧风险最集中的一块；根层的实机走查待全部落地后进行 | aligned |
| SC-8 | 每个子成果在被接受时是绿的 | in_progress | 两个已接受成果处各自：7478e2e = 1151/2/0，078d762 = 1172/2/0，均零错误零警告 | 还剩一个子成果 | aligned |
| SC-9 | 上一轮确立的持久层形状未回退 | in_progress | 7478e2e 处：仍 43 张表；背包未自建表，反而减少一处重叠（`ICharacterItemTable` 上两个直接写 `items` 的成员随最后一个调用方删除）。078d762 处：人物实例不碰持久层，它读的是运行时目录 | 快照重塑后须再复核一次 | aligned |
| SC-10 | 设计文档与落地一致 | in_progress | 7478e2e：新增 R-7 与「Settled by the item-side implementation」。078d762：**改写 R-4**（分界是注册而非分配开销）、**细化 R-6**（判据是强身份类型，不是身份形状的数值）、新增「Settled by the character-side implementation」，并裁定了三处未决项中的第一处 | 还剩两处未决项：`ConsumeAsync` 不在 R-7 覆盖内、`ItemUseEffectDomainService.ApplyAsync` 的归属 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-item-rules-in-the-bag/TaskContract.md` | 物品规则收回背包实例：背包获得放入、补充、移动三个领域动词，各自内部调用注入的规则服务；到货判决的落地在全仓只剩一处；协议工程不再持有物品规则；三处调用方各自的失败语义原样保留 | `SC-2`, `SC-3` | `sealed` | `completed` | `accepted as 7478e2e` |
| `2` | `spec-002` | `attempts/spec-002-the-character-instance/TaskContract.md` | 人物成为可操作的实例：由人物仓库按需交出（无注册故无生命周期，见契约 revision 2 与 R-4 的改写）；「能不能用这件物品」经由它表达；全仓最后一个签名带身份的规则服务随之拆成「实例供身份、规则服务作判决」两半 | `SC-4`, `SC-5` | `sealed` | `completed` | `accepted as 078d762` |
| `3` | `spec-003` | `attempts/spec-003-the-snapshot-in-domain-shape/TaskContract.md` | 快照成为一组领域记录：存储侧按概念承载而非按段拍平；入场侧的段布局与位宽收窄移入协议工程；领域里不再有以协议段命名的记录类型。下发帧一字节不变 | `SC-6`, `SC-1` | `running` | `pending` | `active` |
