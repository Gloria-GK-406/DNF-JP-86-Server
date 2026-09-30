---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
phase: child_execution
phase_state: pending
active_attempt: spec-004
last_transition: child-created
transition_evidence: "SC-4 remains open after sealed spec-001 through spec-003; spec-004-outworld-amplify is a ready contained TaskContract contributing only to SC-4, with no child dependencies and baseline 4dbd73b4."
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap/Document/tasks/equipment-development
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap
  branch: task/solo-play-roadmap
  baseline: 4dbd73b48952c3158004167de6f02be14767df39
  baseline_evidence: "2026-09-30: one accepted magic-seal commit 4dbd73b4 over 8ab85ebd; linked task worktree clean immediately after commit; main checkout stayed at 10ccbad2; fresh build and focused suites passed, with two known creature failures in real-PVF Application full run."
---

# Multi-Spec Map

## Multi-Spec Boundary

强化、附魔、魔法封印、异次元属性及锻造的玩家入口、规则、资产结果和 Human Test List 可以分别实现与接受；共享装备实例及事务基础属于已接受基线或逐项复用，不要求预先为未来项创建 Spec。每次只激活一个子项。

## Overall Direction

各子项按“70 端和上游参考 → A21 客户端与 PVF 取证 → 设计 → 实现 → 自动验证 → 待用户集中实机验收”闭环。先处理强化（用户列出的第一项），再依据已接受结果选择后续子项；各项的设计选择保持开放。共用的装备实例、槽位、事务、金币/材料消耗和客户端投影必须以当前代码证据为准，参考端不自动升级为本服事实。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 强化闭环 | satisfied | `spec-001` accepted commit `e1d4c216`; `attempts/spec-001-reinforcement/spec-result.md` 和验证记录 | 用户集中实机验收仍待执行，不计入本自动接受条件 | aligned |
| SC-2 | 装备附魔闭环 | satisfied | `spec-002` 的 `0x0110` 宝珠路径已在 `8ab85ebd` 接受，独立审查与自动验证见该子项记录 | 用户 A21 GUI 验收待后续进行；`0x025A` 附魔师摊位由用户明确排除 | aligned |
| SC-3 | 魔法封印闭环 | satisfied | `spec-003` accepted commit `4dbd73b4`; `attempts/spec-003-magic-seal/spec-result.md` 和验证记录 | 用户集中实机验收仍待执行；材料路径证据不足而安全拒绝 | aligned |
| SC-4 | 异次元属性闭环 | not_started | none | 70/上游/A21/PVF 探查、方案、实现、验证与验收清单 | aligned |
| SC-5 | 锻造闭环 | not_started | none | 70/上游/A21/PVF 探查、方案、实现、验证与验收清单 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | spec-001 | attempts/spec-001-reinforcement/TaskContract.md | 装备普通强化 | SC-1 | completed | attempts/spec-001-reinforcement/spec-result.md | e1d4c216d702aa829fa69a0eba3c95c1ea9301db |
| 2 | spec-002 | attempts/spec-002-equipment-enchantment/TaskContract.md | 装备宝珠附魔 | SC-2 | completed | attempts/spec-002-equipment-enchantment/spec-result.md | 8ab85ebdcd5c872d6d7c762e8766a21ad015a7f9 |
| 3 | spec-003 | attempts/spec-003-magic-seal/TaskContract.md | 魔法封印装备 | SC-3 | completed | attempts/spec-003-magic-seal/spec-result.md | 4dbd73b48952c3158004167de6f02be14767df39 |
| 4 | spec-004 | attempts/spec-004-outworld-amplify/TaskContract.md | 异界气息与异次元属性 | SC-4 | running | pending | active |
