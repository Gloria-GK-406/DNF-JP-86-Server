---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-009-project-complete-entry-stream
last_transition: child-created
transition_evidence: "The ready child contract and routing decision at attempts/spec-009-project-complete-entry-stream bind one independently acceptable G6 outcome: a complete result-only ordered projection with no lifecycle mutation. The parent-managed task workspace is reused at accepted baseline 501c2ca; row 9 is the sole running attempt and all earlier attempts are terminal."
workspace:
  repository_root: /Users/puxianguo/project/86JP-main
  task_root: /Users/puxianguo/project/86JP-main/.worktrees/select-character-domain-migration-completion/.rks-agent/tasks/select-character-domain-migration
  code_root: /Users/puxianguo/project/86JP-main/.worktrees/select-character-domain-migration-completion
  branch: codex/select-character-domain-migration-completion
  baseline: 501c2ca330fc6f506261dc9016e82327dccc73fe
  baseline_evidence: "HEAD 501c2ca on codex/select-character-domain-migration-completion in a clean linked worktree; it contains accepted spec-008 product commit a7af583 and its controller acceptance record. Independent review approved; fresh real-PVF verification: 378 passed, 2 expected skips, 0 failed. Source codex/architecture-refactor remains unchanged at 94616f4."
  archive_input: "Script.pvf (58,781,889 bytes) is present as a gitignored copy at the root of this task worktree, copied from /Users/puxianguo/Downloads/Script.pvf. Dfo.Pvf tests execute 60/60 with the archive reachable; the two Infrastructure negative-path tests are expected to skip when an archive is deliberately present."
---

# Multi-Spec Map

## Resume Record

This root was sealed `blocked` by `no-safe-successor` and has been resumed because that
block's recorded condition changed. The block named **two** causes. They did not change in
the same way, and the distinction governs the successor's scope.

**Cause 1 — absent `Script.pvf` or equivalent validated export. Resolved.** The operator
supplied the archive (58,781,889 bytes, at `/workspace/DNF-JP-86-Server/Script.pvf`). The
separately authorized `pvf-content-infrastructure` task then delivered and verified a read
path for it: a zero-domain-knowledge format library (`Dfo.Pvf`) plus four Infrastructure
content sources behind Abstractions-level interfaces. Its completion evidence proves the
four definition categories G5 depends on can be combined with character progress to produce
the business results matrix rows 12, 14, 32/33 and 58 carry — not merely that definitions
parse.

**Cause 2 — no normalized source for live town/PvP/rejoin/expert-store state. Re-classified,
not supplied.** This state is server-owned runtime state, not an absent external artifact,
so it cannot be "provided" the way the archive was; it has to be built. The route is
nonetheless bounded, because the legacy implementations to reverse-engineer are all present
in the main worktree:

| Legacy source | Lines | Covers |
|---|---:|---|
| `Network/Handlers/PvpRoomHandler.cs` | 5,792 | X-09, X-10 |
| `Network/Handlers/CharacterSelectHandler.cs` | 953 | L-04, L-06 |
| `Network/Handlers/TownHandler.cs` | 899 | X-18 |
| `Network/Handlers/ExpertJobStoreHandler.cs` | 878 | X-17 |
| `Network/Handlers/CharacterSessionLifecycleCoordinator.cs` | 766 | X-08, L-01, L-05, L-07, L-10 |
| `Game/Session/ISessionDirectory.cs`, `Game/Session/SessionDirectory.cs` | — | session registry shape |
| `Network/Core/GameChannelAdmissionPolicy.cs`, `GameChannelSpawnPolicy.cs` | — | X-08, L-04 |

The constraint that sealed spec-006 still binds and is the successor's principal hazard:
an empty **registry that exists** is honest loaded-empty, whereas an **absent** registry
rendered as an empty view is fabricated state. The successor must build the registries, not
report emptiness in their absence.

### Scope note on the successor's size

The controller rule is to activate the smallest stable outcome, and the static-content half
of G5 alone would have been one — independently acceptable and independently rollbackable,
with its source already delivered. The operator was shown that split and chose the complete
G5 group instead, including the channel-admission and session-adjacent read views. That is a
larger single acceptance boundary than the rule's default, recorded here so the choice is
visible rather than inferred.

## Multi-Spec Boundary

- The legacy coverage matrix is an independently reviewable documentation outcome that controls all later implementation scope.
- Character-owned, account-owned, world/session-owned service results can be accepted separately while preserving earlier service boundaries if a later slice needs rollback.
- Networking projection depends on accepted business results but has its own packet-order and dependency evidence, so it must not share one unreviewed attempt with all data loading work.

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | Complete legacy initialization migration matrix | satisfied | `fd39009` and `Docs/Refactor/SelectCharacterMigrationMatrix.md`: 58 static, 18 supplemental, 10 lifecycle rows; approved review and fresh verification | none | aligned |
| SC-2 | Complete separated entry-result composition | satisfied | G1-G4 accepted slices; `01cb491` + `21e5a1a` G5 world/session state; `94616f4` explicit orchestration; `a7af583` complete G5 quest eligibility | none; protocol user identity and target ordering belong to G6/G7 projection/lifecycle | aligned |
| SC-3 | Service-driven peripheral data access | satisfied | Accepted active-character, inventory, G3 and G4 slices; SQLite confined to Infrastructure. `01cb491` adds a fifth content source for town and map geometry, a channel entry policy, an entry placement service, and three runtime registries with their query service. Every one sits behind an Abstractions interface; `Pvf` appears zero times in Abstractions, Application, Networking and Data, and only the infrastructure project references the format library | none | aligned |
| SC-4 | Exact and consistent account-exclusive selection | satisfied | `3e70094`: exact-slot load precedes one locked AccountId-indexed publication; stale disconnect and load-failure preservation verified. Re-confirmed at the new baseline: the typed-command-dispatch refactor (`86ba115`) rewrote command registration, and `SelectCharacterCommand` is still registered account-exclusive; that task added the first direct `AccountExecutionBehavior` coverage and mutation-verified that dropping its registration fails tests | none | aligned |
| SC-5 | Result-only ordered Networking projection | not_started | `94616f4` supplies the one-to-many sender seam and one incomplete ACK composer | Rows 2-58, the complete mixed-domain ACK, X-01-X-18 projections, exact order, duplicates and target sets remain | at_risk |
| SC-6 | Integrated verification and honest documentation | in_progress | `fd39009`: reverse-engineering confidence documented; accepted G1-G5 and orchestration checkpoints are ancestors of `94616f4`; fresh resumed baseline is 355 passed, 2 expected skips, 0 failed with real PVF | G5 fidelity, G6, G7, Host/client-facing verification and the stale paused-status document remain | at_risk |

### Constraint classification at this baseline

| Root constraint | Class | Basis |
|---|---|---|
| Networking may not query repositories, call services to fill gaps, or mutate business state | aligned | No G5/G6 projection exists yet to violate it; the accepted G1-G4 slices keep Networking result-only |
| Handlers coordinate one workflow, no nested commands | aligned | `SendAsync_RejectsNestedCommandDispatch` still passes at this baseline; the `AsyncLocal` depth guard is byte-identical through `86ba115` |
| No fabricated empty domain state; `null` means inapplicable, empty means loaded-and-empty, load failure stays an error | at_risk | This is the exact constraint that sealed spec-006. It governs the successor directly: an empty runtime registry that exists and has no entries is honest loaded-empty, but an absent registry rendered as an empty view would violate it |
| Account, world/content and session data stay outside `ActiveCharacterState` | aligned | `SelectCharacterResult` already holds World and Session as sibling limbs rather than character fields |
| Repository/SQLite details stay out of Application and Networking | aligned | Verified during the pvf-content-infrastructure task: `Dfo.Server.Application` contains no PVF reference and `Dfo.Server.Abstractions` takes no dependency on `Dfo.Pvf` |
| Existing accepted behavior is the baseline unless the matrix shows a correction is required | aligned | No accepted behavior was altered by the two intervening tasks; 204/1/0 at the new baseline |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | spec-001 | `attempts/spec-001-map-legacy-select-character-initialization/TaskContract.md` | Map legacy select-character initialization | SC-1 | completed | `attempts/spec-001-map-legacy-select-character-initialization/spec-result.md` | `fd39009` accepted |
| 2 | spec-002 | `attempts/spec-002-publish-active-character-consistently/TaskContract.md` | Publish active character consistently | SC-3, SC-4 | completed | `attempts/spec-002-publish-active-character-consistently/spec-result.md` | `3e70094` accepted |
| 3 | spec-003 | `attempts/spec-003-load-inventory-entry-state/TaskContract.md` | Load inventory entry state | SC-2, SC-3 | completed | `attempts/spec-003-load-inventory-entry-state/spec-result.md` | `fa51169` accepted |
| 4 | spec-004 | `attempts/spec-004-load-character-entry-state/TaskContract.md` | Load character entry state | SC-2, SC-3 | completed | `attempts/spec-004-load-character-entry-state/spec-result.md` | `927e6ed` accepted |
| 5 | spec-005 | `attempts/spec-005-load-account-entry-state/TaskContract.md` | Load account entry state | SC-2, SC-3 | completed | `attempts/spec-005-load-account-entry-state/spec-result.md` | `b8e817c` accepted |
| 6 | spec-006 | `attempts/spec-006-load-world-entry-state/TaskContract.md` | Load world/content entry state | SC-2, SC-3 | blocked | `attempts/spec-006-load-world-entry-state/spec-result.md` | rolled back to `b8e817c` |
| 7 | spec-007 | `attempts/spec-007-complete-world-and-session-entry-state/TaskContract.md` | Complete world, content and session entry state | SC-2, SC-3 | completed | `attempts/spec-007-complete-world-and-session-entry-state/spec-result.md` | `01cb491` + `21e5a1a` accepted |
| 8 | spec-008 | `attempts/spec-008-complete-quest-eligibility/TaskContract.md` | Complete G5 quest eligibility semantics | SC-2, SC-3, SC-6 | completed | `attempts/spec-008-complete-quest-eligibility/spec-result.md` | `a7af583` accepted |
| 9 | spec-009 | `attempts/spec-009-project-complete-entry-stream/TaskContract.md` | Project complete Select Character entry stream | SC-5, SC-6 | running | pending | active |
