---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 2
phase: between_children
phase_state: pending
active_attempt: null
last_transition: child-accepted
transition_evidence: spec-001 result passed; independent review Round 3 approved with all P1/P2 closed; fresh verification passed 7/7 input hashes, baseline identity, stale-assertion search and diff check
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-directory-completion/.rks-agent/tasks/party-directory-completion
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-directory-completion
  branch: task/party-directory-completion
  baseline: 9a3a0b492da11f48525d1225d02513e0dd1cd458
  baseline_evidence: canonical repository root, git dir/common dir, branch, HEAD, porcelain status, containment and worktree ignore were verified before task materialization
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
| SC-2 | town-visible `0x0002 -> 0x0009` 非成员投影 | not_started | current B1 static chain and accepted wire primitives | implementation and tests | blocked_by_spec_001 |
| SC-3 | lifecycle/scope/session projection and ordering | not_started | accepted party/town baseline `9a3a0b49` | implementation and tests | blocked_by_spec_001 |
| SC-4 | native ID 0x11 UI add/update/delete | not_started | CNUser/party-pointer/renderer path statically identified | server implementation and exact-session runtime | blocked_by_spec_002 |
| SC-5 | regression, docs and independent review | not_started | accepted baseline test evidence | accumulated implementation and runtime result | blocked_by_spec_003 |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `./attempts/spec-001-directory-blocker-closure/TaskContract.md` | 固定 A21 目录阻塞证据闭合与实现输入裁决 | `SC-1` | `passed` | `./attempts/spec-001-directory-blocker-closure/spec-result.md` | `accepted evidence-only result; commit pending` |
