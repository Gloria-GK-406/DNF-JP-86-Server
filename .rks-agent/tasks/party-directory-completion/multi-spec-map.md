---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 2
phase: child_disposition
phase_state: in_progress
active_attempt: spec-003
last_transition: child-sealed
transition_evidence: spec-003 result passed; independent Round 2 approved after F-001/F-002 remediation; post-review build, helper, runtime/sample/hash, cleanup, zero-production-diff and all 3129 full-suite outcomes passed; observed HEAD remains prior baseline 365baed0 and every changed path is owned by this Spec
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-directory-completion/.rks-agent/tasks/party-directory-completion
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-directory-completion
  branch: task/party-directory-completion
  baseline: 365baed09ee5a19c08550a4f5dbdaecdafca7c8f
  baseline_evidence: spec-002 completed result, independent approval, fresh completion verification, exact prior parent 2530f35bb0b5fb6d82603fbfaa9a492e37fc6375 and clean accepted product state were sealed in commit 365baed09ee5a19c08550a4f5dbdaecdafca7c8f
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
| SC-2 | town-visible `0x0002 -> 0x0009` 非成员投影 | satisfied | spec-002 passed and accepted in `365baed0`; Protocol/Application/full-suite evidence | none | aligned |
| SC-3 | lifecycle/scope/session projection and ordering | satisfied | spec-002 accepted failure/revision/generation/replacement/lifecycle evidence | none | aligned |
| SC-4 | native ID 0x11 UI add/update/delete | not_started | CNUser/party-pointer/renderer path statically identified; server projection accepted | exact-session two-client native runtime add/update/delete | aligned |
| SC-5 | regression, docs and independent review | in_progress | spec-002 approved; build 0/0, Protocol 110/110, Application 79/79, full 3127 Passed + 2 NotExecuted | accumulated runtime result and root review/verification | aligned |

## Current Root Evaluation

- Accepted baseline: `365baed09ee5a19c08550a4f5dbdaecdafca7c8f`; clean immediately after the
  spec-002 accepted-result commit.
- Remaining outcome: fixed A21 native ID `0x11` must show one valid nonmember party, refresh it after a settings or
  membership change, and remove it after disband/departure, with exact-session `0x0002 -> 0x0009` plus UI-state evidence.
- Accumulated closeout: after that runtime result, reconcile protocol evidence and run complete root review plus fresh root
  verification.
- Constraints: town-visible scope, member/observer separation, `0x0002 -> 0x0009`, exact-session, no new directory
  `0x023C`, Application layering, no Computer Use, and one explicit user action request are all `aligned`.

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `./attempts/spec-001-directory-blocker-closure/TaskContract.md` | 固定 A21 目录阻塞证据闭合与实现输入裁决 | `SC-1` | `passed` | `./attempts/spec-001-directory-blocker-closure/spec-result.md` | `2530f35b` |
| `2` | `spec-002` | `./attempts/spec-002-town-party-observer-projection/TaskContract.md` | town-visible 非成员 party 投影与生命周期收敛 | `SC-2, SC-3, SC-5` | `passed` | `./attempts/spec-002-town-party-observer-projection/spec-result.md` | `365baed0` |
| `3` | `spec-003` | `./attempts/spec-003-native-party-directory-runtime/TaskContract.md` | 固定 A21 原生 ID `0x11` add/update/delete 运行时闭环 | `SC-4, SC-5` | `running` | `pending` | `active` |
