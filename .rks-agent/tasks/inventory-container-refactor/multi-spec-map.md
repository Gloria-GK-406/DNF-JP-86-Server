---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 2
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-001
last_transition: child-created
transition_evidence: >-
  确定性身份 spec-001-item-container-abstraction，路径
  attempts/spec-001-item-container-abstraction/ 已规范化并证明包含于持久根内，编号未重用；
  子 TaskContract 为 ready，parent 指向 ../../TaskContract.md，contributes_to 为 SC-1..SC-5，
  depends_on 为空列表（无未解外部前置，链接校验结果为空）；同目录已按 using-rks-agent
  资产写入 routing-decision.md，已完成 SDD 就绪、边际控制价值与子级结构选择
  （single-spec-workflow），仅留 Controller Selection 交由 single-spec-workflow 补全；
  追加前 Spec Attempts 表为空且 active_attempt 为 null，追加后仅此一行为 running/active。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/inventory-container-refactor/.rks-agent/tasks/inventory-container-refactor
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/inventory-container-refactor
  branch: inventory-container-refactor
  baseline: 2eacf0970d928413b866549e850524c09c44b51b
  baseline_evidence: >-
    源检出 D:/project/DNF/DNF-JP-86-Server 分支 main 位于 2eacf09 且工作树干净；
    任务工作区自同一修订检出，HEAD 与之相等，除已声明持久工件外无其他变更。
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
| SC-1 | 道具移动与道具使用两条协议不再直接依赖持久化仓储，改经容器操作 | not_started | none | 全部 | aligned |
| SC-2 | 当前容器可作为背包投影内容的来源 | not_started | none | 全部 | aligned |
| SC-3 | 同一类型下可注册多个容器，可列出、可切换、可移除，且首个注册者为初始当前容器 | not_started | none | 全部 | aligned |
| SC-4 | 移除当前容器后角色仍绑定到一个可用容器；无可回落容器时以异常暴露 | not_started | none | 全部 | aligned |
| SC-5 | 第一个成果完成后对外行为一字未变 | not_started | none | 全部 | aligned |
| SC-6 | 道具能否使用可由角色与道具标识单独问出，调用方无需自行组装事实 | not_started | none | 全部 | aligned |
| SC-7 | 归档中 `[stackable type]` 以 `unlimited` 开头的道具使用后数量不减 | not_started | none | 全部 | aligned |
| SC-8 | 冷却按归档声明的共享标识生效：同组任一道具使用后，同组其余道具在冷却期内不可用 | not_started | none | 全部 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-item-container-abstraction/TaskContract.md` | 道具读写经容器完成，角色对当前容器有显式绑定，对外行为零变化 | SC-1, SC-2, SC-3, SC-4, SC-5 | `running` | `pending` | `active` |
