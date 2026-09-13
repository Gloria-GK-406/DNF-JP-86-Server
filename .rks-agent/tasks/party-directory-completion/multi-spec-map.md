---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 2
phase: child_disposition
phase_state: in_progress
active_attempt: spec-002
last_transition: child-sealed
transition_evidence: spec-002 result passed; latest independent Round 2 approved with F-001/F-002/F-003 resolved; fresh completion evidence passed build, 110 Protocol, 79 Application, all 3129 full-suite outcomes, layering, diff and zero-023C checks; observed HEAD remains prior baseline 2530f35b and every changed path is owned by this Spec
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-directory-completion/.rks-agent/tasks/party-directory-completion
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-directory-completion
  branch: task/party-directory-completion
  baseline: 2530f35bb0b5fb6d82603fbfaa9a492e37fc6375
  baseline_evidence: spec-001 completed result, independent approval, fresh completion verification, and clean accepted product state were sealed in commit 2530f35bb0b5fb6d82603fbfaa9a492e37fc6375
---

# Multi-Spec Map

## Multi-Spec Boundary

- 固定 A21 的协议/客户端阻塞闭合是只读取证结果；它必须在任何目录业务值进入生产发送路径前独立接受。
- 服务端 codec、在线目录投影与通知顺序构成一个可自动验证、可独立回滚的实现结果。
- 原生 UI 实机验收依赖用户动作与 exact-session 证据；失败时只产生聚焦修复，不撤销已经审查通过的数据层。

## Overall Direction

从接受基线 `9a3a0b49` 串行推进：阻塞证据闭合 → 服务端目录实现 → 当前 A21 原生 UI 验收与必要修复 →
完整一致性和回归审计。快速组队、Story/Cycle、PVP 均不进入本任务。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 两窗口、协议归属和主目录数据源裁决 | complete | spec-001 passed; independent Round 3 approved; fresh completion evidence | none | aligned |
| SC-2 | town-visible `0x0002 -> 0x0009` 非成员投影 | in_progress | current B1 static chain, accepted spec-001 and wire primitives | spec-002 implementation and tests | aligned |
| SC-3 | lifecycle/scope/session projection and ordering | in_progress | accepted party/town baseline `9a3a0b49` and corrected server audit | spec-002 implementation and tests | aligned |
| SC-4 | native ID 0x11 UI add/update/delete | not_started | CNUser/party-pointer/renderer path statically identified | server implementation and exact-session runtime | blocked_by_spec_002 |
| SC-5 | regression, docs and independent review | not_started | accepted baseline test evidence | accumulated implementation and runtime result | blocked_by_spec_003 |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `./attempts/spec-001-directory-blocker-closure/TaskContract.md` | 固定 A21 目录阻塞证据闭合与实现输入裁决 | `SC-1` | `passed` | `./attempts/spec-001-directory-blocker-closure/spec-result.md` | `2530f35b` |
| `2` | `spec-002` | `./attempts/spec-002-town-party-observer-projection/TaskContract.md` | town-visible 非成员 party 投影与生命周期收敛 | `SC-2, SC-3, SC-5` | `running` | `pending` | `active` |
