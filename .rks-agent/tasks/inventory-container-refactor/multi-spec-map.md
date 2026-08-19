---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 3
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-002
last_transition: child-created
transition_evidence: >-
  连续三次转移的证据。`gaps-remain`：于 `cf010d5` 重新评估，SC-1..SC-5 已 satisfied，
  SC-6..SC-8 为 not_started，全部约束 aligned，记录见 `## Root Evaluation`。
  `safe-successor`：剩余缺口构成一个有界成果，记录见 `## Successor Decision`；
  其中 SC-9 超出 revision 2 的授权范围，已于 2026-08-18 取得用户的扩容授权，
  根契约随之升至 revision 3，SC-9 与新增的 Scope / Constraint / Assumption 行一并写入。
  `child-created`：子契约 `attempts/spec-002-item-use-rules-and-effects/TaskContract.md`
  已 ready（revision 1，parent 与 contributes_to 指向 SC-6..SC-9，depends_on 为空），
  同目录 routing-decision.md 已完成 SDD 就绪、边际控制价值与结构选择三节，
  仅 Controller Selection 留给 single-spec-workflow 前已一并填好；标识与路径经容器包含性校验，
  创建前证得无 active 子任务、无 running 行，Spec Attempts 第 2 行已追加。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/inventory-container-refactor/.rks-agent/tasks/inventory-container-refactor
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/inventory-container-refactor
  branch: inventory-container-refactor
  baseline: a0149f5ac61675b4f3b04500125c1b85302b334c
  baseline_evidence: >-
    工作区自 2eacf09 检出；spec-001 经评审与新鲜验证后请求一次 accept，提交前证得 HEAD 仍为
    2eacf09 且变更集全部属该尝试所有，提交后 HEAD 为 cf010d5、工作区干净，
    源检出 D:/project/DNF/DNF-JP-86-Server 的 main 仍停在 2eacf09 未被推进。
    控制器状态另记为 a0149f5——它记录的正是 cf010d5，故无法被其自身包含；
    spec-002 的回滚目标为 a0149f5，即当前分支尖端。
---

# Multi-Spec Map

## Multi-Spec Boundary

- 两个成果各自独立可验收，且第一个单独交付即有价值。成果一的验收是「对外行为零变化 +
  既有测试基线全绿 + 实机表现一致」，完全不触及成果二引入的任何新行为；成果二的验收是
  「`unlimited*` 道具不再被扣减 + 冷却按共享标识生效」，是可观察的行为变更。
- 两者的接受与回滚边界互不重叠：成果一被接受后，成果二失败也不需要回退成果一。

## Deferred Follow-up

这些事实已被证实，但不属于当前任一成果的接受边界。记录于此，以免它们随对话上下文一起丢失。

- 选角入场时一次性产生的五份初始背包快照（Main / Avatar / PersonalCargo / Pet / AccountCargo）
  仍走原有的整体加载路径，未迁到容器之上。它与道具值、锁、租赁等一并读出，迁移会拖入整条
  入场路径，与成果一「行为零变化」的接受边界冲突，因此成果一的投影只覆盖由道具移动与使用
  引发的那部分。入场快照改由当前容器供给，是多容器切换真正生效的前提——否则切换后重新登录
  会看到旧容器的内容。这项工作应在多容器切换的持久化结构与触发协议一并落地时处理，
  届时需要一次新的根评估与授权。
- 与之相关：道具移动与道具使用两条协议至今只回 CMD 应答，不推送 `0x000D` / `0x000E`。
  成果一因此只交付「容器可作为投影内容的来源」并以逐字节一致为证，没有新增推送——
  新增推送本身即是行为变更。真正由容器驱动的推送，与上一条同属多容器切换那一刀。

- 不消耗类道具「使用后数量不减」的**实机**确认未取得，其存储侧行为已由 spec-002 的自动化证据覆盖。
  走查依次尝试 `51`、`1120`、`1143`、`10008052` / `10008051` / `2749196` 六个 `[unlimited *]` 道具，
  客户端对每一个都拒绝发出使用请求，各自的门槛不同：未声明 `[stack limit]`、需要未实现的发言协议、
  需要角色没有的材料、以及「超过使用期限」（把核心里的 `ExpireTime` 改成一年后仍无改善）。
  服务端因此收不到帧，实现正确与否观察不到差别。子契约 spec-002 的 AC-6 据此重述为不含该项
  （revision 2，用户 2026-08-19 授权）。要补上它，先得弄清客户端对 `[unlimited *]` 道具的那道
  有效期检查究竟看什么——走查发现同样 `ExpireTime=0` 时 `[waste]` 药剂照用不误、
  三个 `[unlimited waste]` 却报过期，提示 `unlimited` 的语义可能是「使用不消耗，改由有效期约束」。
  这项属客户端行为的逆向，不属任何一个已定义成果，待其被弄清时另行评估。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 道具移动与道具使用两条协议不再直接依赖持久化仓储，改经容器操作 | satisfied | `cf010d5`：两个处理器的构造依赖为 `ICharacterContainerDomainService`；`CharacterContainerDomainServiceTests` 以反射断言其中不含 `IEntryInventoryMutationRepository` | 无 | aligned |
| SC-2 | 当前容器可作为背包投影内容的来源 | satisfied | `cf010d5`：`ContainerInventoryProjectionTests` 在同一份 SQLite 存储上比对容器与入场加载路径两条独立读取的 `0x000D` / `0x000E` 编码字节，一致；去掉列表过滤该用例会失败 | 无（生产路径改从容器取，见 Deferred Follow-up） | aligned |
| SC-3 | 同一类型下可注册多个容器，可列出、可切换、可移除，且首个注册者为初始当前容器 | satisfied | `cf010d5`：`CharacterContainerDomainServiceTests` 覆盖注册、列出、切换、移除四项 | 无 | aligned |
| SC-4 | 移除当前容器后角色仍绑定到一个可用容器；无可回落容器时以异常暴露 | satisfied | `cf010d5`：回落与无回落两个用例通过，后者抛出前不改动绑定与列表 | 无 | aligned |
| SC-5 | 第一个成果完成后对外行为一字未变 | satisfied | `cf010d5`：791 通过 / 2 跳过 / 0 失败（基线 780/2/0 加 11 个新用例），既有用例仅机械适配；实机走查 2026-08-18 通过，日志 `item 8474 Relocate 3->4 count=2` 与 `consumed 1 of item 8474 from slot 4, 1 left` | 无 | aligned |
| SC-6 | 道具能否使用可由角色与道具标识单独问出，调用方无需自行组装事实 | not_started | none | 全部 | aligned |
| SC-7 | 归档中 `[stackable type]` 以 `unlimited` 开头的道具使用后数量不减 | not_started | none | 全部 | aligned |
| SC-8 | 冷却按归档声明的共享标识生效：同组任一道具使用后，同组其余道具在冷却期内不可用 | not_started | none | 全部 | aligned |
| SC-9 | 移动与使用的失败原因由命令结果承载；所选提示码或与原因相符，或客户端不显示提示 | not_started | none | 全部 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-item-container-abstraction/TaskContract.md` | 道具读写经容器完成，角色对当前容器有显式绑定，对外行为零变化 | SC-1, SC-2, SC-3, SC-4, SC-5 | `completed` | `completed` | `cf010d5` |
| `2` | `spec-002` | `attempts/spec-002-item-use-rules-and-effects/TaskContract.md` | 道具使用分离为只读判定与效果，不消耗类道具不再被销毁，冷却按共享标识生效，拒绝原因由命令结果承载并只用诚实的提示码 | SC-6, SC-7, SC-8, SC-9 | `running` | `pending` | `active` |

## Root Evaluation

于 `cf010d5` 重新评估：

- SC-1..SC-5 均已 satisfied，证据见上表，全部来自已接受的基线。
- SC-6、SC-7、SC-8 仍为 not_started，构成剩余缺口：只读的道具可用性判定、
  承载使用后状态变更的效果服务、以及以角色与共享标识为键的冷却记录。
- 全部约束仍为 aligned。成果一未改变任何对外行为，因而没有为成果二引入需要先行修复的偏差；
  「`unlimited*` 道具被扣减」按约束留在原处，正是成果二要改变的行为。

结论：`gaps-remain`。下一个子成果的边界（SC-6..SC-8）在根契约内，无需扩大授权；
但其内部设计尚未与用户确认，故在 `child_modeling` 之前暂停。

补记于根契约 revision 3：SC-9 在上述评估之后由用户追加授权，其 Progress 为 `not_started`，
与 SC-6..SC-8 同属剩余缺口，不改变 `gaps-remain` 的结论。

## Successor Decision

剩余缺口 SC-6..SC-9 构成一个有界且稳定的成果：判定、效果、冷却、失败语义四者共享同一个
接受与回滚边界，任何一半单独交付都不成立——只有判定没有效果，`unlimited*` 道具仍被销毁；
只有效果没有判定，冷却与场所无处安放；失败语义若不同时改，判定新增的拒绝分支只能继续
复用那句与原因不符的「金库空间不足」。

授权：SC-6..SC-8 在根契约 revision 2 的 `task` 行范围内。SC-9 超出该范围，已于 2026-08-18
取得用户的一次扩容授权（「并入spec2一起处理……这里我要说commnd要承载失败的语义，
让handler能返回正确的错误给client显示」，随后「继续开始吧」），根契约据此升至 revision 3。
未再拆分为 authoring 与 execution 两次授权。

约束核对：全部 aligned。成果一未改变任何对外行为，没有为本成果引入需要先行修复的偏差。
根契约「第一个成果不得改变任何对外可观察行为」这条只约束成果一，已随 `cf010d5` 履行完毕；
本成果的行为变更（不消耗、冷却、场所、拒绝码）正是 SC-6..SC-9 所要求的。

前置事实已在建模前落定，不留给实施阶段发明：

- 客户端确有提示能力。`0x0013` 与 `0x002C` 的完整码表已由二进制探查取得并归档于
  `protocol/patterns/data/probes/cmd-reason-messages-20260818.md`，含每个码走屏幕还是
  仅走日志、以及 CN/JP/KR 文案。`0x0013` 现用的 `0x02` 根本不在客户端分支链里，
  `0x04` 显示的是「金库空间不足」。
- 判定所需的四类事实在归档中逐条有据：`[usable job]` 34,533、`[minimum level]` 28,673、
  `[cool time]` 6,254（其中 `[cooltime group]` 1,713）、`[action usable place]` 792，
  实测自 `stackable/stackable.lst`。因此 SC-6 的四类拒绝分支都可达，无需收窄。
  疲劳值没有服务端模型，本就不在 SC-6 之内。
- 命令承载失败语义的形状不需要发明：同一代码库已有七个 Game 切片走 `ICommandBus`
  并从类型化结果读失败原因，两条道具协议是仅剩的例外。

选定路由：`single-spec-workflow`，子任务 `spec-002-item-use-rules-and-effects`。
