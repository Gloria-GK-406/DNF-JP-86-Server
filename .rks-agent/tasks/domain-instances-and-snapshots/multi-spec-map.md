---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 2
state: completed
phase: completed
phase_state: completed
active_attempt: null
last_transition: verification-passed
transition_evidence: >-
  用户免除了 SC-7 唯一剩下的那样证据并授权收尾，原话：
  「不查了，我们后续要换client了。直接合并和清理工作区就好。这个任务的复查SC不用做，我临时豁免了」。
  据此 SC-7 判为 satisfied（其余证据齐备，实机走查记为**已免除**，见该行）；
  十条成功条件全部满足，十条约束全部 aligned。
  封存前逐条证明：state 为 active、无 running 行、active_attempt 为 null、三个尝试号唯一、
  每条成功条件都有最终证据。根层终验的证据取自最终基线 d387bcb 的新鲜运行（见 Root Verification）。
  **根层评审没有单独跑过一轮**：三个子成果各自跑了独立评审，共七轮
  （spec-001 两轮、spec-002 两轮、spec-003 三轮），其中四轮判 changes_required 并各自整改验证；
  用户免除了进一步的复查，所以这里如实记为「以七轮子评审为准，未另跑根层评审」，
  而不是记成一次通过的根层评审。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-instances-and-snapshots/.rks-agent/tasks/domain-instances-and-snapshots
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-instances-and-snapshots
  branch: domain-instances-and-snapshots
  baseline: d387bcb428d0532bffb2cc04039f26f71df60ca5
  baseline_evidence: >-
    git rev-parse HEAD = 078d76228328c2ce4417947d2ee8db518a59b5f2，提交后 git status --short 为空；
    该提交建立在 7478e2e 之上，源分支 main 仍为
    531c0bbd8a35fd579c3add6e478965307bd083d4 未变。
    本基线的证据：dotnet build Server/86JP.Server.slnx 零警告零错误；
    dotnet test Server/86JP.Server.slnx -m:1 = 1180 通过 / 2 跳过 / 0 失败。
    历次基线：main @ 531c0bb = 1132/2/0；7478e2e = 1151/2/0；078d762 = 1172/2/0。净增逐块可解释。
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
| SC-1 | 仓库类同时提供快照与实例两种产物，两者都有生产调用方 | satisfied | 7478e2e：背包仓库交出的实例是可操作的东西——三个领域动词，三处生产调用方。078d762：人物仓库同时交出快照与实例（`Get`），实例有生产调用方。d387bcb：快照成为一组领域记录，存储侧与入场侧共用同一族，从装配器一路传到编解码器 | 无 | aligned |
| SC-2 | 「到货的东西放哪个格子」在整个代码库中只有一处定义 | satisfied | 提交 7478e2e：全仓 `case ItemArrival.` 在生产代码中共 3 处，同属 `ItemContainers.LandAsync` 的三种到货各一支；教程奖励与每日补充两条路都经由它。两处到货**规则**本就不同且都保留（`ForGrant` 认叠加上限并溢出，`ForDailyRefill` 并入任一堆），收敛的是把判决变成写入的那一段 | 无 | aligned |
| SC-3 | 判定物品移动是否允许的规则不在协议工程内 | satisfied | 提交 7478e2e：`Dfo.Server.Protocol` 对摆放、槽位、到货三类规则服务的引用归零；`MoveItemSpaceCommandHandler` 只剩主列表判断、取背包、转发、把领域拒绝翻成客户端码。五种拒绝与两种非拒绝逐条可构造，其中半栈交换与拆分为本轮新补——它们此前全仓无人构造 | 无 | aligned |
| — | *（建模期发现，已随 spec-001 处理）* | 已处理 | 提交 7478e2e：两处注释均已重写，全仓已无「拼装属于上层」「是协议的问题」这类指向旧划分的说法 | 无 | aligned |
| SC-4 | 存在人物实例，且人物侧至少一个业务用例经由它表达 | satisfied | 提交 078d762：`ICharacter` / `PlayingCharacter`，由 `ICharacterDomainService.Get` 交出；「能不能用这件物品」经由它表达，`UseStackableCommandHandler` 不再持有可用性服务。接缝本身有三条用例，用工厂而非现成实例构造仓库 | 无 | aligned |
| SC-5 | 规则服务不解析身份 | satisfied | 提交 078d762：全仓十个规则/判定服务接口逐个清点，签名中无一出现 `CharacterId` 或 `AccountId`。唯一带账号形状的是 `IDungeonEffectRuleService.CanCarryOut(int recordedAccountId, …)`——那是被比较的值而非要解析的身份，此区分已写入 R-6 | 无 | aligned |
| SC-6 | 快照是一组领域记录；包的段布局发生在协议侧 | satisfied | 提交 d387bcb：`Dfo.Server.Application`、`Dfo.Server.Abstractions`、`Dfo.Server.Data` 三处搜索以协议段命名的记录类型均**零命中**；段的次序与位宽收窄发生在 `writer.Write(...)` 那一行；`CharacterProfileEntryProjection` 由 45 字段按位复制变为传递且零收窄。（本条的判据在建模期被订正过：包形状的不是 `ActiveCharacterState`，而是存储侧与入场侧各一族的 subtype 记录，见根契约 revision 2） | 无 | aligned |
| SC-7 | 对外协议行为一字不变 | satisfied | 三块全部举证：物品侧与人物侧期间三份整记录表征用例 `git diff` 全程为空；快照侧因形状改变而重写了其中一份的 golden，但**值多重集与基线逐条一致**（215/215，两轮评审各自独立重做过该比对），另两份仍为空；协议编解码用例的期望字节全程零改动；全量 1180/2/0。物品侧曾有一处会改变下发内容的回归，由评审捕获并已加守卫 | 无。实机走查一次选人与一次物品移动**由用户明确免除**——原话「不查了，我们后续要换client了……这个任务的复查SC不用做，我临时豁免了」。这一条如实记录为**免除**而不是完成：帧级证据来自表征用例、编解码期望字节与全量，实机上没有跑过 | aligned |
| SC-8 | 每个子成果在被接受时是绿的 | satisfied | 三个已接受成果处各自：7478e2e = 1151/2/0，078d762 = 1172/2/0，d387bcb = 1180/2/0，均零错误零警告 | 无 | aligned |
| SC-9 | 上一轮确立的持久层形状未回退 | satisfied | 7478e2e：仍 43 张表；背包未自建表，反而减少一处重叠。078d762：人物实例不碰持久层。d387bcb：只改记录的分组，未动任何表、列或索引；持久层形状经第三轮评审独立复核无命中 | 无 | aligned |
| SC-10 | 设计文档与落地一致 | satisfied | 7478e2e：新增 R-7 与落地记录。078d762：改写 R-4、细化 R-6、裁定第一处未决项。d387bcb：订正 R-5 中对包形状记录的误述（连同根契约推进到 revision 2）、新增「Settled by the snapshot-side implementation」，并把「我先给了一个站不住的理由、被评审用实验推翻」这件事本身写进文档。七条规则逐条可在代码中指出落点 | 两处未决项仍在，各自已写明归属与理由：`ConsumeAsync` 不在 R-7 覆盖内、`ItemUseEffectDomainService.ApplyAsync` 的归属 | aligned |

## Root Review

根层没有单独跑过一轮评审，这里如实记录实际发生的事。

三个子成果各自在独立的新上下文里跑了评审，共七轮：

| 子成果 | 轮次与判决 |
|---|---|
| spec-001 物品规则收回背包 | 第一轮 `changes_required`（一条 A 级：换写入通道改变了写入的可见时机，新开格子那天整个选人入场回滚；逐行 diff 看不出来）；第二轮 `passed` |
| spec-002 人物实例与规则去身份 | 第一轮 `changes_required`（一条 A 级：「由仓库交出」这条接缝全仓无测试，把 `Get` 改成交出另一个角色全量仍全绿）；第二轮 `passed`，并另发现两条整改自身引入或遗漏的问题 |
| spec-003 快照改为领域形状 | 第一轮 `changes_required`（一条 A 级：四条宽度拒绝随字段迁出扁平记录而丢失）；第二轮 `changes_required`（一条 A 级：为「四处收窄未下沉」写的理由，四处里三处不属实，评审真改真跑当场证伪）；第三轮 `passed` |

四条 A 级里，三条是「搬运时丢掉一条只活在代码里的规则」，一条是「为没做的事编了一个站不住的理由」。
**四条全部由评审捕获，零条由测试捕获**——这与上一个同规格根任务的实测一致。
另有二十余条 B 级观察记录在各自的 `reviews/change-review.md` 里。

用户免除了进一步的复查。根层评审门因此**未运行**，本节记录的是替代它的那七轮，不是它本身。

## Root Verification

证据取自最终基线 `d387bcb`，运行于封存前：

| 事实 | 结果 |
|---|---|
| 构建 | `dotnet build Server/86JP.Server.slnx` 零错误零警告 |
| 全量测试 | **1180 通过 / 2 跳过 / 0 失败**（起点 main @ 531c0bb = 1132 / 2 / 0） |
| 净增 | +48，逐块可解释：物品侧 19、人物侧 21、快照侧 8 |
| 仓库两种产物 | 人物仓库交出快照与实例；背包仓库交出实例，两者各有生产调用方 |
| 领域里的协议段命名类型 | `Dfo.Server.Application`、`Dfo.Server.Abstractions`、`Dfo.Server.Data` 三处搜索**零命中** |
| 规则服务是否解析身份 | 十个规则/判定服务接口逐个清点，签名中无一出现 `CharacterId` 或 `AccountId` |
| 到货落地处数 | 生产代码中一处 |
| 持久层形状 | 仍 43 张表；本任务未动任何表、列或索引 |
| 表征用例 | 三份中两份 `git diff` 全程为空；第三份因形状改变重写 golden，值多重集 215/215 与基线逐条一致 |
| 协议编解码期望字节 | 全程零改动 |
| 设计文档 | 七条规则逐条可在代码中指出落点；三处未决项裁定一处、另两处各写明归属与理由 |
| 提交 | 四个：三个子成果的已接受结果，一个根控制器状态记录 |

**没有在实机上跑过。** 这是用户免除的那一样，记在 SC-7 那一行。

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-item-rules-in-the-bag/TaskContract.md` | 物品规则收回背包实例：背包获得放入、补充、移动三个领域动词，各自内部调用注入的规则服务；到货判决的落地在全仓只剩一处；协议工程不再持有物品规则；三处调用方各自的失败语义原样保留 | `SC-2`, `SC-3` | `sealed` | `completed` | `accepted as 7478e2e` |
| `2` | `spec-002` | `attempts/spec-002-the-character-instance/TaskContract.md` | 人物成为可操作的实例：由人物仓库按需交出（无注册故无生命周期，见契约 revision 2 与 R-4 的改写）；「能不能用这件物品」经由它表达；全仓最后一个签名带身份的规则服务随之拆成「实例供身份、规则服务作判决」两半 | `SC-4`, `SC-5` | `sealed` | `completed` | `accepted as 078d762` |
| `3` | `spec-003` | `attempts/spec-003-the-snapshot-in-domain-shape/TaskContract.md` | 快照成为一组领域记录：两族以协议段命名的记录全部**删除**而非移动；段的次序与位宽收窄落到写字节那一行；领域里不再有以协议段命名的记录类型。下发帧一字节不变 | `SC-6`, `SC-1` | `sealed` | `completed` | `accepted as d387bcb` |
