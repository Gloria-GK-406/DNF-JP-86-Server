---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 2
state: active
phase: root_evaluation
phase_state: in_progress
active_attempt: null
last_transition: child-accepted
transition_evidence: >-
  spec-001 的 spec-result.md 以 outcome: completed 封存；评审 reviews/change-review.md 三轮后
  decision: approved 且无未决阻断项；verification/completion-evidence.md decision: pass，
  自动化 791 通过 / 2 跳过 / 0 失败，实机走查由用户于 2026-08-18 在教学关完成。
  accept 前已证 HEAD 等于 2eacf09 且变更集全部属本尝试所有，提交后 HEAD 为
  cf010d506ac93251fdabe5bb4bb8f7a77c27e105，工作区干净，源分支 main 仍为 2eacf09。
  Spec Attempts 第 1 行已就地封存，active_attempt 已清空，无 running 行残留。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/inventory-container-refactor/.rks-agent/tasks/inventory-container-refactor
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/inventory-container-refactor
  branch: inventory-container-refactor
  baseline: cf010d506ac93251fdabe5bb4bb8f7a77c27e105
  baseline_evidence: >-
    工作区自 2eacf09 检出；spec-001 经评审与新鲜验证后请求一次 accept，提交前证得 HEAD 仍为
    2eacf09 且变更集全部属该尝试所有，提交后 HEAD 为 cf010d5、工作区干净，
    源检出 D:/project/DNF/DNF-JP-86-Server 的 main 仍停在 2eacf09 未被推进。
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

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-item-container-abstraction/TaskContract.md` | 道具读写经容器完成，角色对当前容器有显式绑定，对外行为零变化 | SC-1, SC-2, SC-3, SC-4, SC-5 | `completed` | `completed` | `cf010d5` |

## Root Evaluation

于 `cf010d5` 重新评估：

- SC-1..SC-5 均已 satisfied，证据见上表，全部来自已接受的基线。
- SC-6、SC-7、SC-8 仍为 not_started，构成剩余缺口：只读的道具可用性判定、
  承载使用后状态变更的效果服务、以及以角色与共享标识为键的冷却记录。
- 全部约束仍为 aligned。成果一未改变任何对外行为，因而没有为成果二引入需要先行修复的偏差；
  「`unlimited*` 道具被扣减」按约束留在原处，正是成果二要改变的行为。

结论：`gaps-remain`。下一个子成果的边界（SC-6..SC-8）在根契约内，无需扩大授权；
但其内部设计尚未与用户确认，故在 `child_modeling` 之前暂停。
