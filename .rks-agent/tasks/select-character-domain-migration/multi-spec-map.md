---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-007
last_transition: child-created
transition_evidence: "Child spec-007 activated after an empty validation result. Identity: next attempt number 7 is one greater than the greatest sealed number (6) and has never been used. Path attempts/spec-007-complete-world-and-session-entry-state/ is canonical and contained within the durable task root /workspace/DNF-JP-86-Server-pvf/.rks-agent/tasks/select-character-domain-migration. Dependency linkage: depends_on is an empty YAML sequence, which is valid; accepted internal history is carried by workspace.baseline, not by a dependency edge. No active child and no running or active row existed before activation — spec-006 is sealed blocked. A ready child TaskContract and a child-local routing decision were both written before the row was appended; the routing decision completes SDD readiness, marginal control value and Single structural selection, and leaves Controller Selection for single-spec-workflow. Exactly one running row was appended. Prior transition, for the record: safe-successor established one bounded stable outcome inside the root's existing authorized scope: complete G5 — world/content entry state plus channel admission and session-adjacent read views, covering matrix rows 12, 14, 17-18, 31-34, 49-53, 56 and the row-58 content portion, X-08 through X-10, X-15 through X-18, and L-04 and L-06. It contributes to SC-2 and SC-3 and unblocks SC-5. No scope expansion is needed: the root TaskContract Scope/In already reads 'Define and implement the character, account, world/content, and session service boundaries required by the select-character flow', so the existing root task authorization covers child creation and execution. The route is bounded on two independent legs — the static-content leg has a delivered, verified source (four Abstractions-level definition interfaces implemented over Dfo.Pvf), and the runtime leg has complete legacy implementations to reverse-engineer in the main worktree, enumerated in the Resume Record. The at_risk no-fabricated-empty-state constraint is addressed rather than invalidated: the child must build real registries whose emptiness is genuine, which is what spec-006 could not do. Accepted evidence from G1-G4 is not disturbed, since G5 fills sibling limbs of SelectCharacterResult that are currently placeholders. Prior evidence, for the record: root evaluation at baseline 86ba115 recomputed all six success conditions and every root constraint from accepted evidence; see the Success Condition Progress and Constraint classification tables. SC-1 and SC-4 are satisfied (SC-4 re-confirmed after 86ba115 rewrote command registration). SC-2, SC-3 and SC-6 are in_progress and SC-5 is not_started, all gated on G5. Direct code evidence for the gap: GameWorldEntryState is a one-field placeholder at CharacterModels.cs:53, CharacterEntryService.cs:50-51 constructs it and CharacterSessionEntryState from nothing but a timestamp and the identity pair, and repo-wide search finds no admission, session-directory, town-presence, PvP-lobby, dungeon-rejoin or expert-store service. Five of six constraints are aligned; the no-fabricated-empty-state constraint is at_risk because it is the exact constraint that sealed spec-006 and it governs any successor that must expose runtime read views. Gaps remain, so the root is not satisfied."
workspace:
  repository_root: /workspace/DNF-JP-86-Server
  task_root: /workspace/DNF-JP-86-Server-pvf/.rks-agent/tasks/select-character-domain-migration
  code_root: /workspace/DNF-JP-86-Server-pvf
  branch: codex/pvf-content-infrastructure
  baseline: 86ba115ec26bb759348c2a49bb715a06d1d683ed
  baseline_evidence: "HEAD 86ba115 on branch codex/pvf-content-infrastructure in a clean worktree; b8e817c (the prior accepted G4 baseline) is a proven ancestor. The six commits between them were produced by two separately authorized task contracts, NOT by this root's attempts: 052754b and b4aba0d carry this root's own paused-state documentation and checkpoint merge; 8574bc2, 30119c9 and 37693c1 are the pvf-content-infrastructure task; 7031862 and 86ba115 are the typed-command-dispatch task. Advancing this root's baseline records where the shared code root now is; it does not claim those commits as this root's accepted progress. Fresh verification at this baseline: 204 passed, 1 skipped, 0 failed (Application 55, Networking 29, Dfo.Pvf 60, Infrastructure 60 + 1 skip), Release, warnings-as-errors."
  archive_input: "Script.pvf (58,781,889 bytes) is present at the root of this worktree, where the parent-directory search finds it with no configuration. It was copied from the main worktree because the archive is gitignored (.gitignore:26 '*.pvf') and therefore absent from a fresh worktree; git status confirms it stays untracked. This matters for evidence integrity: when the archive is NOT reachable, archive-backed tests skip rather than fail, so a run without it is misleadingly green. Verified after the copy with no environment variable set: 204 passed, 1 skipped, 0 failed. The one skip is the missing-archive test, which by construction cannot run where an archive is reachable."
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
| SC-2 | Complete separated entry-result composition | in_progress | `fa51169`: inventory; `927e6ed`: G3 character state; `b8e817c`: complete G4 AccountId-owned state with inventory reuse | G5 remains unmet. `GameWorldEntryState` is a one-field placeholder (`CharacterModels.cs:53`, holding only `PreparedAt`) and `CharacterSessionEntryState` carries only the identity pair, so the world/content and session-adjacent limbs of the composition are absent, not merely thin | at_risk |
| SC-3 | Service-driven peripheral data access | in_progress | Accepted active-character, inventory, G3 and G4 normalized repository/service slices; SQLite remains confined to Infrastructure. The G5 static-content half now has a driven source: `IQuestDefinitionSource`, `IDailyActivityDefinitionSource`, `ICollectionBoxDefinitionSource` and `ICreatureDefinitionSource` are Abstractions-level interfaces implemented only in Infrastructure over `Dfo.Pvf` | G5's channel-admission and session-adjacent read views have no service or registry at all: repo-wide search finds no admission service, no session directory, no town-presence, PvP-lobby, dungeon-rejoin or expert-store registry. `IChannelCatalog` supplies only the login-time channel list, not per-listener admission | at_risk |
| SC-4 | Exact and consistent account-exclusive selection | satisfied | `3e70094`: exact-slot load precedes one locked AccountId-indexed publication; stale disconnect and load-failure preservation verified. Re-confirmed at the new baseline: the typed-command-dispatch refactor (`86ba115`) rewrote command registration, and `SelectCharacterCommand` is still registered account-exclusive; that task added the first direct `AccountExecutionBehavior` coverage and mutation-verified that dropping its registration fails tests | none | aligned |
| SC-5 | Result-only ordered Networking projection | not_started | Baseline `b0a3312` supplies one-to-many sender seam and ACK composer | Requires an accepted complete G5 result before G6/G7 projection | at_risk |
| SC-6 | Integrated verification and honest documentation | in_progress | `fd39009`: reverse-engineering confidence documented; accepted checkpoint `b8e817c` remains a proven ancestor of the current baseline; `Docs/Refactor/SelectCharacterMigrationIncomplete.md` records the paused state honestly | Root completion cannot be verified while G5 and downstream projection remain incomplete. The incomplete-status document is now partly stale: its "Immediate priority" and "Required PVF Resolution" sections describe a blocker that is resolved | at_risk |

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
| 7 | spec-007 | `attempts/spec-007-complete-world-and-session-entry-state/TaskContract.md` | Complete world, content and session entry state | SC-2, SC-3 | running | pending | active |
