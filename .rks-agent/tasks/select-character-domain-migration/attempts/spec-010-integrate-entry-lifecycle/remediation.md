# Spec 010 complete-review remediation

> Remediation implementation note, 2026-08-12. This is execution evidence, not a
> controller/review/result state change.

- F-001: owner static and X-02–X-06 are now required before daily refill mutation/reload;
  committed updates are delivered separately as required X-07. The fault/retry behavior
  test proves a static failure performs no claim/grant and one retry performs it once.
- F-002: command and disconnect observer share an account-keyed
  `SelectedSessionTransitionGate`. The command revalidates after taking the gate and checks
  exact current ownership around every post-registration fallible phase. Deterministic
  disconnect-first behavior proves no later preparation/mutation/publication occurs.
- F-003: quest and pet services return updated typed `SelectCharacterResult` plus their
  projection. The workflow exact-replaces the runtime after each committed mutation; pet
  revival updates the matching `CharacterPetRuntimeState.Field04` before X-13/X-14.
- F-004: cleanup area target enumeration includes the exact presence listener port; the
  mixed-port regression proves no cross-listener X-17/X-18.
- F-005: published runtime entries preserve `PersistsPosition`; cleanup invokes the narrow
  persistence adapter only for persistent channels. Persistent/transient regressions cover
  both dispositions.
- Follow-up deadlock: the disconnect observer acquires the transition gate only when the
  connection still has an exact selected session, then revalidates after acquiring it.
  Every command close site explicitly exact-removes the selected directory entry before
  awaiting transport close. A no-session close callback completes while the account gate
  is held; a selected-session callback still waits until the command releases ownership.

## Fresh-review remediation R01-R04

- F-R01: command execution now accumulates pending connection closes while it owns the
  account transition gate, captures the original exception, releases the gate in `finally`,
  and only then awaits transport close. Production-shaped close callbacks which themselves
  await the disconnect observer prove rollback and displacement cannot form a gate/close
  cycle.
- F-R02: selection recovers the legacy three-kind dungeon-effect outbox through a narrow
  repository before loading the entry snapshot. Records are isolated transactions; expired
  reservations are reclaimable, unsupported/invalid records dead-letter independently, and
  committed experience/lucky-star mutations share a transaction with the typed outbox
  result. Experience thresholds, honor cap and growth-capsule policy come from Script.pvf.
- F-R03: the lease registry exposes exact dirty snapshot/version state. After successful
  admission but before old-session cleanup, a dirty exact lease is transactionally saved;
  the selected inventory is then reloaded and registered before the static stream. The
  SQLite writer treats the lease as authoritative across deletion, update/move and insert.
- F-R04: fair-duel entry uses the independently persisted legacy
  `character_pvp_skill_state`/`character_pvp_skills` profile. First entry initializes it in
  one transaction from PVF initial/grow grants using the legacy active/group slot allocator;
  ordinary entry continues to reconcile the normal profile. Unlimited PvP mirrors feed row
  11 and X01 without leaking paid ordinary skills.

Fresh R01-R04 verification: solution build passed with zero warnings; real-PVF solution
tests passed (PVF 60, Application 239, Networking 64, Infrastructure 109 with the two
intentional missing-archive tests skipped); bounded Host smoke reached `Application
started` and shut down cleanly; dependency scans and `git diff --check` were clean.

All protocol claims remain code-reversed and await real-client verification.

## Complete re-review remediation F-001-F-005

- F-001: preparation is split into a read-only character/admission preflight and full
  preparation. Wire identity is checked before any session side effect; the exact selected
  session is then registered before dungeon recovery, expert reconciliation or PvP-profile
  initialization. Full-preparation failure runs exact rollback and closes after releasing
  the transition gate. Invalid-wire and late-failure behavior tests pin both boundaries.
- F-002: recovery validates legacy player scope `3`, exact character target and account
  owner. Each row is claimed by state/expired-lease CAS before applying; commit/dead-letter
  is conditioned on the same lease. Malformed JSON is atomically dead-lettered without a
  rollback/re-claim race. Lucky-star commits the typed `{granted,newTotal}` result, including
  the capped `granted:false` case. Wrong-scope/target, poison and overlap vectors are covered.
- F-003: dirty lease persistence atomically owns avatar core plus detail insert/update/delete,
  rejects cross-owner avatar-UID collisions and reloads the exact details. The dirty API is
  narrowed to `CharacterItemLeaseState` (character ID and item containers): virtual values,
  item-value lists, locks, rentals and gold limits remain explicitly outside this lease.
- F-004: ordinary entry invokes the expired expert-contract reconciliation transaction on
  every preparation and reloads changed skill truth. PVF supplies the exact advancement
  availability and level-derived ordinary maximum plus over-skill premium types. Free-duel
  replaces only learned pages and unlimited protocol mirror, retaining SP basis/slots,
  Dark Knight combos and knight shield deck.
- F-005: the former paused G5 handoff is now an explicitly historical record pointing to
  the current G1-G7 coverage and accepted path; real-client behavior remains an honest
  validation item rather than an implementation claim.

Fresh F-001-F-005 verification: real-PVF solution tests passed (PVF 60, Application 245,
Networking 64, Infrastructure 117; two intentional missing-archive negative tests skipped).
The bounded Host smoke reached `Application started` and shut down cleanly. Networking has
no repository/PVF/SQLite references, Application has no concrete SQLite/PVF references,
and `git diff --check` is clean.

## Residual fidelity remediation F-001R/F-002R

- F-001R: settlement recovery now uses `ExpectedDatabaseLevel/ExpectedDatabaseExp` only as
  the stale-write guard. Honor splitting, ordinary gain, level-up, persistence and typed
  result are calculated from durable `PreviousLevel/PreviousExp`, preserving unpersisted
  in-dungeon progress. The regression uses DB `85/84000`, settlement `85/84990` and gain
  `25`, and pins `86/85000`, honor `+15`, capsule `+15` and the complete result JSON.
- F-002R: `SkillPointCostDefinition.OrdinaryMaximumLevel` is nullable: null means the
  advancement is inapplicable, while zero is an applicable real-level cap. Reconciliation
  skips only null, deletes an expired-contract-only zero-cap row, and still applies a free
  baseline floor. Real-PVF coverage proves both null and zero are projected for distinct
  skills; SQLite coverage pins delete, preserve and free-floor branches.

Fresh residual verification: solution build passed with zero warnings; real-PVF solution
tests passed (PVF 60, Application 245, Networking 64, Infrastructure 119; two expected
missing-archive tests skipped). Host reached `Application started` and shut down cleanly;
dependency scans and `git diff --check` were clean.

## Fresh verification

- Focused lifecycle/service/session/online-user suite: 24 passed.
- Final real-PVF solution: PVF 60, Application 236, Networking 64 and
  Infrastructure 105 passed; the two archive-absence negative tests skipped as expected.
- Host bounded smoke reached `Application started` and shut down cleanly.
- Networking prohibited-dependency scan, Application concrete-peripheral scan and
  `git diff --check` produced no findings.
