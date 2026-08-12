---
kind: rks-review
verdict: approved
---

# Root completion review: Select Character domain migration

## Review basis

This is a fresh independent root review of the accepted result at
`248abac2356a4a5275f00dde2134ae9da5782e22` against the root TaskContract and the
current Multi-Spec map. I reviewed the complete accepted G1-G7 history, every terminal
child result and the relevant sealed review/verification evidence, including the blocked
and rolled-back Spec 006, its accepted Spec 007 successor, the approved G6 projection in
Spec 009, and the approved/aligned/verified G7 lifecycle in Spec 010. I also inspected the
current integrated code, tests and maintained protocol documentation. No adversarial
challenge set was supplied for this root review.

All accepted child commits recorded by the controller are ancestors of `248abac`. The
worktree product baseline is exactly that commit; the only pre-existing tracked worktree
change during this review is the controller-owned map transition into `root_review`.

## Success-condition disposition

| Condition | Verdict | Root-level evidence |
|---|---|---|
| SC-1: complete migration matrix | satisfied | `SelectCharacterMigrationMatrix.md` contains exactly 58 static rows, X-01-X-18 and L-01-L-10, with identity, condition, source/owner, target responsibility, side effects and confidence. Spec 001 is sealed completed and approved. |
| SC-2: separated complete business composition | satisfied | Accepted G1-G5 services compose `SelectCharacterResult` from sibling character, character-inventory, account-inventory, account, world/content and session results. `ActiveCharacterState` remains character-owned; optional/applicable-empty semantics are explicit and tested. |
| SC-3: service-driven peripheral access | satisfied | The integrated flow is Handler -> account-exclusive Command -> cohesive application services -> abstraction ports -> Infrastructure adapters. Fresh source/project scans find no concrete SQLite/PVF dependency in Application or Networking and no repository/data-loading path in Networking. |
| SC-4: exact consistent selection | satisfied | Selection remains exact `AccountId + SlotIndex`, `SelectCharacterCommand` remains an `IAccountCommand`, and G7 adds exact connection-generation registration, replacement and compensation. Preflight precedes lifecycle effects; active/user publication occurs only after completed required preparation and delivery. Invalid-slot, replacement, stale disconnect and rollback behavior is covered by the green suite. |
| SC-5: result-only Networking projection | satisfied | Networking owns the descriptor-driven 58-row static sequence, conditional rows, X-01 adjacency and typed X-01-X-18 encoding/delivery. Application supplies completed typed results and explicit targets; no domain lookup, recipient discovery or business mutation occurs in Networking. |
| SC-6: integrated verification and honest documentation | satisfied | Fresh root verification passes 488 tests with two expected archive-present skips and builds Host with zero warnings/errors. Sealed Spec 010 evidence additionally records successful Host start/shutdown. Current matrix, G6, G7 and historical-pause documents consistently label protocol claims code-reversed and reserve client/packet-capture confirmation as external validation. |

## Root-constraint audit

- **Networking boundary:** aligned. Frame construction is confined to Networking. The
  public typed projection/sender seam carries business projections and explicit target
  batches; Networking does not query services or mutate lifecycle state.
- **One cohesive workflow / no nested commands:** aligned. The message handler performs
  one command-bus dispatch and the Select Character command coordinates narrow services;
  no nested command dispatch was found in the application workflow.
- **Truthful absence semantics:** aligned. Applicable loaded-empty collections, genuinely
  inapplicable nullable facts and load/poison failures remain distinct through G1-G7.
  The accepted G7 reconciliation, outbox and lease repairs preserve those distinctions.
- **Domain ownership:** aligned. Account, world/content and session facts are sibling
  result limbs and exact runtime registries, not convenience fields on character state.
- **Peripheral isolation:** aligned. SQLite/PVF implementation and schema control remain
  in Infrastructure behind abstractions; neither Application nor Networking references
  their concrete types.
- **Accepted-baseline preservation:** aligned. Spec 010 is based exactly on accepted G6
  commit `1ab5f0d`; review-driven fidelity corrections are traced to the legacy matrix and
  the complete regression suite remains green. The failed Spec 006 attempt was rolled back
  and contributes no unresolved implementation state.

## Integrated G1-G7 conclusion

The accepted children form one coherent root result: the legacy surface is mapped; exact
selection and publication are preserved; inventory, character, account, world/content and
session views are loaded through owned services; quest/content applicability is truthful;
the full static and supplemental stream is projected from typed results; and G7 supplies
generation-safe preparation, publication, post-entry work and rollback. No unresolved
correctness, ownership, dependency, lifecycle, schema, Networking-boundary or documentation
blocker remains.

Real-client execution and packet capture may still reveal protocol corrections, but the
root contract explicitly excludes treating such evidence as already available. The docs
state that boundary honestly, so it is follow-up validation rather than an incomplete root
deliverable.

## Fresh checks

- `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test Server/86JP.Server.slnx --no-restore --logger "console;verbosity=minimal"`: **488 passed, 2 expected skips, 0 failed** (PVF 60, Application 245, Networking 64, Infrastructure 119).
- `dotnet build Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj --no-restore --verbosity minimal`: **0 warnings, 0 errors**.
- Dependency/boundary scans: no concrete SQLite/PVF reference in Application or
  Networking; no Networking repository lookup; no raw Select Character frame construction
  in Application; no nested Select Character command dispatch.
- Matrix mechanical count: **58 static, 18 supplemental, 10 lifecycle**.
- Accepted ancestry check: every controller-recorded successful checkpoint through
  `248abac` is present in the final history; `git diff --check` is clean.

This approval is the root independent-review gate. The controller may proceed to its
separate root completion verification and sealing transition.
