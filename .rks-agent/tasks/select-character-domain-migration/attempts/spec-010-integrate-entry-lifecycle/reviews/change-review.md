---
kind: rks-review
verdict: approved
---

# Fresh final complete re-review: Spec 010 integrated entry lifecycle

## Review basis

This is an independent complete-change re-review of the current tracked and untracked
worktree against baseline `1ab5f0dd95ff03816a87ce55edc1fc03665b23e6`. I reread the
TaskContract, specification, plan, current parent-map entry, prior review and remediation;
inspected the complete lifecycle coordinator, exact runtime ownership, SQLite/PVF adapters,
typed Networking boundary, tests and maintained documentation; and compared the two latest
fidelity repairs with the accepted legacy implementations. No adversarial challenge set
was supplied for this Standard review.

## Finding dispositions

- **F-001R closed.**
  `SqliteDungeonPersistentEffectRecoveryRepository.ApplyExperienceAsync` uses
  `ExpectedDatabaseLevel/ExpectedDatabaseExp` only for the persisted stale-write check at
  `Server/src/Dfo.Server.Infrastructure/Characters/SqliteDungeonPersistentEffectRecoveryRepository.cs:151-160`.
  Honor splitting, ordinary gain, level-up, persistence and typed result now start from
  durable `PreviousLevel/PreviousExp` at lines 161-221. The distinct-baseline regression at
  `Server/tests/Dfo.Server.Infrastructure.Tests/Characters/SqliteEntryLifecycleRecoveryTests.cs:243-280`
  proves DB `85/84000` plus settlement `85/84990` and gain `25` persists `86/85000`, retains
  the session delta, grants the exact honor/capsule values, and stores the complete result.
- **F-002R closed.**
  `SkillPointCostDefinition.OrdinaryMaximumLevel` is now a discriminated nullable fact:
  null means advancement-inapplicable while zero is an applicable real-level cap
  (`Server/src/Dfo.Server.Abstractions/Content/SkillPointContent.cs:5-20`). The PVF source
  derives those cases separately at
  `Server/src/Dfo.Server.Infrastructure/Content/PvfSkillPointDefinitionSource.cs:125-151`;
  reconciliation skips only null, deletes cap-zero rows, clamps positive caps and applies
  the free baseline first at
  `Server/src/Dfo.Server.Infrastructure/Characters/SqliteExpertSkillContractReconciliationRepository.cs:56-76`.
  Real-PVF and SQLite tests cover null preservation, zero deletion and free-floor retention.

## Complete-change result

- Admission and wire identity remain side-effect free before exact registration; durable
  preparation occurs inside the registered compensation boundary.
- Same-connection refresh, cross-connection displacement, exact active/runtime cleanup,
  stale disconnect/rollback safety and close-after-gate behavior remain generation-safe.
- Dungeon outbox claim/commit/dead-letter remains atomic and typed; the narrowed dirty item
  lease persists core/avatar state authoritatively; refill, quest and pet mutations reload
  or replace the exact runtime before their truthful projections.
- Static/X delivery, publication, PvP direct-before-ready/peer isolation, trailing locks,
  rejoin CAS, X-17/X-18 listener targeting and persistent-position cleanup preserve the
  specified order and failure ownership.
- Application and Networking retain their abstraction boundaries: no concrete SQLite/PVF
  dependency, downstream `OnlineUserId`, nested command dispatch, recipient discovery or
  raw-frame construction was introduced.
- Host registration is complete, and G7 documentation remains explicitly code-reversed
  rather than claiming client or packet-capture confirmation.

No blocking correctness, boundary, failure-handling, schema-compatibility, documentation or
regression finding remains in the current complete change.

## Fresh verification

- Focused Infrastructure recovery/PVF/inventory checks: **17 passed, 0 failed**.
- Focused Application lifecycle/entry/session/online-user checks: **45 passed, 0 failed**.
- Full real-PVF solution: **488 passed, 2 expected skips, 0 failed** — PVF 60,
  Application 245, Networking 64, Infrastructure 119.
- Host build: **0 warnings, 0 errors**.
- Concrete peripheral/dependency, nested-command/downstream-identity scans and
  `git diff --check`: no findings.

Approval is the independent code-review gate only; final completion verification remains a
separate controller stage.
