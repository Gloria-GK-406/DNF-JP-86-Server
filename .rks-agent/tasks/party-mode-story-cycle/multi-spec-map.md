---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
phase: child_execution
phase_state: pending
active_attempt: spec-002
last_transition: child-created
transition_evidence: spec-002 identity/path and containment passed; ready child contract contributes only SC-2/3/4 with an empty valid dependency list; child routing selects Single Spec; spec-001 is sealed, and no active or running row existed before activation
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-mode-story-cycle/.rks-agent/tasks/party-mode-story-cycle
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-mode-story-cycle
  branch: task/party-mode-story-cycle
  baseline: 9a3a0b492da11f48525d1225d02513e0dd1cd458
  baseline_evidence: rev-parse root/git-dir/common-dir, branch, HEAD and porcelain status passed; task root is contained in code root; source main identity was read separately
---

# Multi-Spec Map

## Multi-Spec Boundary

- Story 和 Cycle 各自需要模式专属协议/规则/客户端验收；一类受阻不应撤销另一类已经审查、验证并接受的结果。
- 三类模式证据齐备后的模型审计是独立结果；它可能确认现有抽象，也可能产生一个可单独审查和回滚的聚焦
  重构提交。

## Overall Direction

从已接受 `9a3a0b49` 串行推进：Story 模式闭环 → Cycle 模式闭环 → 三类模型审计/必要聚焦重构。
公共目录由 GitHub Issue #20 跟踪，不参与本根任务；异步静态探索结果在安全边界作为新证据吸收，但不与
当前子规格并发修改相同代码。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | Story 模式闭环 | not_started | sealed blocked spec-001 retains static field/lifecycle evidence only; no accepted product result | current A21 native Story create/edit/特有操作帧、合法 target/default 裁决、实现和客户端验收 | at_risk |
| SC-2 | Cycle 模式闭环 | not_started | none | Cycle 专属静态/动态证据、实现和客户端验收 | aligned |
| SC-3 | 既有组队/频道能力无回退 | in_progress | accepted baseline `9a3a0b49` | 每个后继结果的组合回归与最终验证 | aligned |
| SC-4 | 协议资料与证据等级一致 | in_progress | accepted Normal/protocol baseline | Story/Cycle 专页、样本与最终一致性检查 | aligned |
| SC-5 | 三类模式建模审计 | not_started | none | Story/Cycle 接受后执行整体审计 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `./attempts/spec-001-story-party-mode/TaskContract.md` | A21 Story 设置、规则、直接生命周期与客户端闭环 | `SC-1, SC-3, SC-4` | `blocked` | `./attempts/spec-001-story-party-mode/spec-result.md` | `rollback-proven; baseline 9a3a0b492da11f48525d1225d02513e0dd1cd458; durable evidence preserved` |
| `2` | `spec-002` | `./attempts/spec-002-cycle-party-mode/TaskContract.md` | A21 Cycle 设置、区域规则、直接生命周期与客户端闭环 | `SC-2, SC-3, SC-4` | `running` | `pending` | `active` |

## Spec-001 Disposition

- Outcome: `blocked`；直接依据为 `./attempts/spec-001-story-party-mode/spec-result.md`、TASK-01 Round 1、
  `evidence/task01/story-verdict.md` 与 `runtime-capture-plan.md`。
- Durable evidence: 根 controller 与 `./attempts/spec-001-story-party-mode/**` 全部保留；当前 A21 固定探针
  为 `19/19` byte anchors、`6/6` resources、`5/5` upstream blobs PASS。
- Repository classification: HEAD 未离开接受基线；全部状态路径均在根 task folder 内，且只含 durable
  SDD/取证文件。没有生产、测试、协议权威页、客户端或外部系统改动，也没有派生缓存。
- Rollback proof: 无 attempt-owned 产品改动需要恢复；`git diff --check` 通过，产品/客户端 diff 为空，
  baseline 保持 `9a3a0b49`。
- Required changed state for a Story successor: 当前固定 A21 原生 Story create/edit 与委任阻断时间线，
  能裁决 target/default 及 raw `[3..7]`；不得语义恢复或改写已封存 spec-001。
