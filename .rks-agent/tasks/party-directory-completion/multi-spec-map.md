---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 2
phase: completed
phase_state: completed
active_attempt: null
last_transition: verification-passed
transition_evidence: fresh post-root-review Release build passed with 0 warnings and 0 errors; runtime helpers passed 14/14 plus receipt test; nine fresh TRX contain 3127 Passed, 2 intentional NotExecuted and 0 Failed; no Server/src 0x023C and accepted commit chain is exact
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-directory-completion/.rks-agent/tasks/party-directory-completion
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-directory-completion
  branch: task/party-directory-completion
  baseline: c5f76f68a003178ee4d2efa376be9b2605cad6bb
  baseline_evidence: spec-003 passed result, independent Round 2 approval, fresh post-review build/helper/runtime/sample/hash/cleanup/full-suite verification and exact parent 365baed09ee5a19c08550a4f5dbdaecdafca7c8f were sealed in commit c5f76f68a003178ee4d2efa376be9b2605cad6bb; tracked product state is clean and remaining untracked files are generated pyc/TRX outputs
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
| SC-4 | native ID 0x11 UI add/update/delete | satisfied | spec-003 accepted in `c5f76f68`; exact-session add/update/delete frames, native UI, stable CNUser/CParty pointer and title-index transition evidence | none | aligned |
| SC-5 | regression, docs and independent review | satisfied | accumulated independent root review approved with no blockers; fresh root Release build 0/0, helpers 14/14 plus receipt test, and full 3127 Passed + 2 intentional NotExecuted + 0 Failed; protocol records reconcile wire/UI confidence | none | aligned |

## Current Root Evaluation

- Accepted baseline: `c5f76f68a003178ee4d2efa376be9b2605cad6bb`; tracked product state is clean after the
  spec-003 accepted-result commit, with only generated pyc/TRX files untracked.
- Functional outcome: all four root functional success conditions have accepted evidence; spec-003 closes fixed A21 native
  ID `0x11` add/update/delete with exact-session `0x0002 -> 0x0009`, native UI and client-object snapshots.
- Accumulated closeout: run complete root drift/result review and fresh root verification across all three accepted children.
- Constraints: town-visible scope, member/observer separation, `0x0002 -> 0x0009`, exact-session, no new directory
  `0x023C`, Application layering, no Computer Use, and one explicit user action request are all `aligned`.

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `./attempts/spec-001-directory-blocker-closure/TaskContract.md` | 固定 A21 目录阻塞证据闭合与实现输入裁决 | `SC-1` | `passed` | `./attempts/spec-001-directory-blocker-closure/spec-result.md` | `2530f35b` |
| `2` | `spec-002` | `./attempts/spec-002-town-party-observer-projection/TaskContract.md` | town-visible 非成员 party 投影与生命周期收敛 | `SC-2, SC-3, SC-5` | `passed` | `./attempts/spec-002-town-party-observer-projection/spec-result.md` | `365baed0` |
| `3` | `spec-003` | `./attempts/spec-003-native-party-directory-runtime/TaskContract.md` | 固定 A21 原生 ID `0x11` add/update/delete 运行时闭环 | `SC-4, SC-5` | `passed` | `./attempts/spec-003-native-party-directory-runtime/spec-result.md` | `c5f76f68` |

## Root Review

- Scope: accepted root baseline `9a3a0b492da11f48525d1225d02513e0dd1cd458` through accepted result
  `c5f76f68a003178ee4d2efa376be9b2605cad6bb`.
- Verdict: `approved`; no P1/P2 or other blocking findings.
- Acceptance: SC-1 is covered by the fixed-B1 boundary correction; SC-2/SC-3 by town-scoped exact-session observer
  projection, lifecycle ordering and regression evidence; SC-4 by the same observer/party/CParty `titleIndex 0 -> 1`
  add-update-delete runtime chain and native UI; SC-5 by post-review Release build/full suite/layering/protocol checks.
- Drift assessment: `aligned`. The children compose as static boundary decision, server projection, then native runtime
  acceptance. No cross-Spec contradiction, confidence escalation, unauthorized production change, directory `0x023C`,
  quick-match or PVP path was found.
- Known inherited result: repository protocol scaffold checks still report the same unregistered `0x0075` and
  `CMD-0x0040` entries as the accepted baseline; this task did not introduce or worsen them.

## Root Verification

- Decision: `pass` at accepted baseline `c5f76f68a003178ee4d2efa376be9b2605cad6bb`, after the independent root
  review.
- Release build: `dotnet build Server/86JP.Server.slnx -c Release --no-restore` passed with `0 warnings / 0 errors`.
- Full suite: nine fresh TRX under the generated `RootVerificationResults/` contain `3127 Passed`, `2 NotExecuted`,
  `0 Failed`, total `3129`. The two skips are the PVF-present mutually exclusive missing-archive tests already classified
  by the accepted child evidence.
- Runtime helpers: directory capture/proxy and state-probe tests passed `4/4 + 10/10`; receipt UTC identity test passed.
- Boundaries: `Server/src` contains no `0x023C`; spec-003 has no production diff over `365baed0`; its committed diff and
  the final controller diff both pass `git diff --check`.
- Commit chain: `c5f76f68 -> 365baed0 -> 2530f35b -> 9a3a0b49`, matching the three accepted child results and original
  root baseline exactly.
- Workspace classification: the only tracked working-tree modification is this terminal controller record; untracked
  files are generated pyc/TRX verification outputs and are not part of the accepted product result.
