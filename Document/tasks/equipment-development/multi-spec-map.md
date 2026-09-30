---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
phase: child_execution
phase_state: in_progress
active_attempt: spec-002
last_transition: child-created
transition_evidence: "spec-002-equipment-enchantment has a ready contained TaskContract, parent resolves to the root, contributes_to SC-2, depends_on is an empty YAML sequence, routing selects Single Spec, and the sole prior attempt spec-001 is sealed. No other row is active."
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap/Document/tasks/equipment-development
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap
  branch: task/solo-play-roadmap
  baseline: e1d4c216d702aa829fa69a0eba3c95c1ea9301db
  baseline_evidence: "2026-09-29: linked Git worktree; main checkout stayed at 10ccbad2; status had only Document/tasks/equipment-development/ as declared task artifacts; dotnet build Server/86JP.Server.slnx --no-restore passed with 0 warnings and 0 errors."
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
| SC-2 | 装备附魔闭环 | in_progress | `spec-002` 的 `0x0110` 宝珠路径已完成自动验证与独立审查，尚未接受 | `0x025A` 附魔师摊位是否纳入本次附魔范围待用户答复；用户 A21 GUI 验收待后续进行 | at_risk |
| SC-3 | 魔法封印闭环 | not_started | none | 70/上游/A21/PVF 探查、方案、实现、验证与验收清单 | aligned |
| SC-4 | 异次元属性闭环 | not_started | none | 70/上游/A21/PVF 探查、方案、实现、验证与验收清单 | aligned |
| SC-5 | 锻造闭环 | not_started | none | 70/上游/A21/PVF 探查、方案、实现、验证与验收清单 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | spec-001 | attempts/spec-001-reinforcement/TaskContract.md | 装备普通强化 | SC-1 | completed | attempts/spec-001-reinforcement/spec-result.md | e1d4c216d702aa829fa69a0eba3c95c1ea9301db |
| 2 | spec-002 | attempts/spec-002-equipment-enchantment/TaskContract.md | 装备附魔 | SC-2 | running | pending | active |
