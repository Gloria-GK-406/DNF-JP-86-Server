---
kind: rks-review
verdict: changes_required
---

# Complete non-code result review — spec-001-directory-blocker-closure

## Review target

- Revision: worktree `task/party-directory-completion` at `9a3a0b492da11f48525d1225d02513e0dd1cd458`, with the uncommitted child task/evidence artifacts present at review time.
- Scope: root and child TaskContract, child Spec/Plan/TASK-01/evidence, and the five hash-pinned reports under `E:/Project/DNF/Document`.
- Applied criteria: complete non-code deliverable review for completeness, correctness, source fidelity, usability, scope, and unsupported claims; acceptance checked against AC-001..AC-005.

## Findings

### [P1] Reconcile the governing root contract before creating the implementation Spec

The accepted evidence changes the implementation boundary, but the governing root `TaskContract.md` still requires an `S2C 0x023C` codec, online directory state, mode 0/1/2 records, and `0x0009/0x023C` ordering (`Scope` lines 43-52; `SC-2/SC-3` lines 79-80). The child Spec and verdict now correctly require the opposite for ID `0x11`: its rows derive from `0x0002 + 0x0009`, and the successor must not add `0x023C` or a directory store (`spec.md` lines 31-35; `evidence/blocker-verdict.md` lines 14-19). Leaving both boundaries active gives the next implementation Spec mutually exclusive acceptance requirements and can either reintroduce the disproved ID `0x14` design or make the implementation fail the root contract despite following the evidence.

Minimum correction: before advancing the root controller to an implementation child, revise the root contract revision and its SC progress so ID `0x11` is governed by the town-visible `0x0002 -> 0x0009` observer projection. Move `0x023C/0x0283` registration-list work to the explicitly separate ID `0x14` outcome (or mark it out of this root scope), and update the dependent SC-2/SC-3 evidence wording. The successor Spec must bind to that reconciled root revision.

### [P2] Do not state that ID `0x11` never reads the `0x023C` manager

`evidence/blocker-verdict.md` line 17 says the `0x023C` manager belongs to ID `0x14` and ID `0x11` does not read it. The pinned wire-source report is more precise: ID `0x11` calls the manager at `0xF749xx` only to determine whether the current party is registered, while ID `0x14` owns the `0xF88CA0/0xF89520` row renderer/filter (`A21-party-directory-wire-sources-followup.md` lines 40-46). Fresh xref reproduction also found manager-getter calls at `0xF74976` and `0xF749FD`. The main directory row source remains `CNUserManager + CParty`, so the no-`0x023C` implementation decision is not overturned; the absolute wording nevertheless erases a real cross-window state query and fails the requested no-confusion boundary.

Minimum correction: change the final verdict (and any exclusive wording in the Task result/AC explanation) to distinguish **row production/rendering** from **current-party registration state**: ID `0x11` does not use `0x023C` records to build or render its main rows, but it does query that manager for registration-related UI state; ID `0x14` owns the record list renderer/filter. Keep this as `Confirmed static`, not `ClientVerified`.

## Acceptance assessment

| AC | Result | Evidence-backed assessment |
|---|---|---|
| AC-001 | changes required | Factory table and constructors do separate ID `0x11` (`0xF74790`, final vptr `0x31FFFD4`) from ID `0x14` (`0xF89240`, final vptr `0x3201ABC`), but the final verdict overcorrects the consumer boundary by denying ID `0x11`'s real `0xF749xx` manager query. |
| AC-002 | pass | The main-directory report closes the row path to `0x0002` CNUser plus `0x0009` CParty and proves the non-exchangeable leader-CNUser-before-roster association; fresh disassembly of `0x19E6BE0` confirms missing CNUser lookup does not create or later backfill the association. |
| AC-003 | changes required | `matching-info` is reproducibly local (`Etc/PartyMatchingInfo.etc`), and `0x0183` is not on the minimum ID `0x11` row chain. The `0x023C` ownership statement needs the P2 precision above rather than exclusive assignment of every manager read to ID `0x14`. |
| AC-004 | pass, subject to P1 | Source audit supports a successor centered on town-visible non-member `0x0009` refresh/clear, exact-session audience, lifecycle hooks, and the shared per-generation stream; a new `0x023C` codec/store would be over-implementation for ID `0x11`. The root contract must first be reconciled so that this valid verdict is actually governable. |
| AC-005 | fail | All pinned hashes, line counts, B1 ImageBase/path, dstr path, branch and baseline matched fresh checks, and the remaining runtime matrix is present. This review has unresolved P1/P2 findings, so AC-005's no-P1/P2 condition is not met. |

## Evidence and boundary checks

- All five report SHA-256 values and line counts exactly match `evidence/input-manifest.md`.
- `DNFClient-A21-debug/DNF.unpacked-b1-debug.exe` hashes to `DC322D8FB2A8A33FCE31065CEEAC5801ED854F34409190FE257AACF2663306E9`; `dstr.dat` hashes to `60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10`.
- Worktree branch/HEAD match the manifest baseline. Git status showed only the task artifact tree as untracked; no production path was changed by this result.
- Fresh B1 reproduction confirmed jump-table entries `0x11 -> 0x23C4DE2`, `0x14 -> 0x23C4E56`; constructors embed IDs `0x11/0x14` and final vptrs `0x31FFFD4/0x3201ABC`; `flushPartyList` reads `CNUserManager`; the matching-info resource literal is locally loaded.
- No new static conclusion was incorrectly labeled `ClientVerified`. The only such status carried through is the previously runtime-verified `0x0009` wire primitive. ID `0x11` row visibility, add/update/delete behavior, category behavior, full-party behavior, and exact-session cleanup remain correctly reserved for runtime verification.

## Re-review conditions

Re-review the amended result after both findings are resolved. Approval requires: (1) a single non-contradictory root boundary for the successor implementation; (2) precise ID `0x11` versus ID `0x14` manager-consumer wording; and (3) fresh hashes/status showing the reviewed inputs and production baseline did not drift.

## Round 2 re-review

```yaml
kind: rks-review
verdict: changes_required
```

### Target and prior-finding disposition

- Target: amended root `TaskContract.md` revision 2, matching `routing-decision.md`/`multi-spec-map.md`, corrected child verdict, and the direct regression surface of those changes; worktree HEAD remains `9a3a0b492da11f48525d1225d02513e0dd1cd458`.
- Round 1 P1: **resolved**. Root Scope, Constraints and SC-2/SC-3 now consistently make ID `0x11` a town-visible exact-session `0x0002 -> 0x0009` projection and explicitly exclude a new `0x023C` codec/store.
- Round 1 P2: **resolved**. `evidence/blocker-verdict.md` lines 17-18 now preserve the ID `0x11` `0xF749xx` current-party registration query while assigning the `0xF88CA0/0xF89520` row consumers to ID `0x14`.

### [P2] Remove the remaining revision-1 `matching-info` misassignment from the root boundary

The revised root `TaskContract.md` correctly says `matching-info` is local at line 30, but its `Out` scope at line 58 groups `matching-info` under the ID `0x14` candidate window. The pinned evidence proves the opposite window ownership: `this+0x1E0` belongs to ID `0x11`, is selected from local `Etc/PartyMatchingInfo.etc`, and participates in ID `0x11` category/dungeon filtering (`A21-party-directory-matching-info-followup.md` lines 36-72, 134-145; `A21-party-main-directory-data-source-followup.md` lines 82-93). ID `0x14` instead owns the `0x0283 -> 0x023C` registration records and their row renderer/filter. The revised routing decision also retains revision-1 wording that treats “required fields and matching-info” as potentially unavailable unresolved blockers (lines 8-9 and 17-18), despite revision 2 having excluded those record fields and resolved matching-info as a local resource.

This does not overturn the successor's `0x0002 -> 0x0009` implementation direction, but it reintroduces an inaccurate ID `0x11`/`0x14` ownership statement into the governing contract and could send later runtime diagnosis or rerouting back toward the wrong window.

Minimum correction: remove `matching-info` from the ID `0x14` Out bullet and retain it as the already-resolved local ID `0x11` filter input/runtime observation boundary. Update the root routing decision to remove the obsolete unknown-required-field/matching-info blocker language and express only current revision-2 reroute conditions. No child evidence or production change is required.

### Round 2 acceptance assessment

| AC | Result | Assessment |
|---|---|---|
| AC-001 | changes required | The original final-verdict consumer error is fixed, but root revision 2 now misassigns local ID `0x11` matching-info in its Out boundary. |
| AC-002 | pass | The `0x0002 CNUser -> 0x0009 CParty` chain and non-exchangeable leader association remain intact. |
| AC-003 | changes required | The child evidence correctly resolves matching-info, `0x023C`, and `0x0183`; the root revision/routing must reflect that resolution without the stale P2 wording. |
| AC-004 | pass | The root now gives a sufficiently bounded, non-overreaching successor direction: existing codec reuse, exact town-visible observers, shared party gate, and no new directory store/protocol. |
| AC-005 | fail | Fresh hashes, paths, addresses, branch and baseline still pass, and no static result was upgraded to `ClientVerified`; the new P2 prevents the required no-P1/P2 review result. |

### Fresh checks

- All five report hashes still match `evidence/input-manifest.md`; B1 and dstr hashes are unchanged.
- Root TaskContract, routing decision and multi-spec map all bind to revision 2.
- Branch remains `task/party-directory-completion` at the accepted baseline; status still contains only the task artifact tree and no production changes.
- No other new P1/P2 was found in the directly affected boundary.

## Round 3 final re-review

```yaml
kind: rks-review
verdict: approved
```

### Target and remediation disposition

- Target: root `TaskContract.md` revision 2, its current `routing-decision.md` and `multi-spec-map.md`, the child Spec/Plan/TASK-01/evidence result, and the five unchanged hash-pinned input reports; worktree HEAD `9a3a0b492da11f48525d1225d02513e0dd1cd458`.
- Round 1 P1: **resolved**. The root implementation boundary is consistently `0x0002 -> 0x0009` town-visible exact-session projection and excludes the disproved `0x023C` directory codec/store.
- Round 1 P2: **resolved**. ID `0x11`'s `0xF749xx` registration-state query is preserved without confusing it with ID `0x14`'s `0xF88CA0/0xF89520` record-list consumers.
- Round 2 P2: **resolved**. Root Scope now keeps matching-info as the already-closed local ID `0x11` filter configuration, while excluding only ID `0x14`'s `0x0283 -> 0x023C` records/pagination/filtering. Routing no longer treats required record fields or matching-info as unresolved blockers and has revision-2-compatible reroute conditions.

### Final acceptance assessment

| AC | Result | Assessment |
|---|---|---|
| AC-001 | pass | Factory IDs, constructors, final vptrs, resources and consumer roles are separated with the legitimate cross-window registration-status query explicitly retained. |
| AC-002 | pass | The main row chain is supported as `0x0002 CNUser + 0x0009 CParty`; leader CNUser must pre-exist the roster refresh, and reverse order requires a repeated `0x0009`. |
| AC-003 | pass | Matching-info is a local ID `0x11` resource, `0x023C/0x0283` belongs to the ID `0x14` registration-list data path, and unresolved C2S `0x0183` is not a main-row implementation blocker. |
| AC-004 | pass | The successor verdict is sufficient and not overbroad: extend existing `0x0009` projection to the exact `VisibleTownArea` non-member audience, preserve lifecycle/order/generation guarantees, and add neither a new directory protocol nor store. Runtime-only UI/filter claims remain outside implementation proof. |
| AC-005 | pass | Five report hashes/line counts, B1/dstr identities, key address anchors, branch and accepted baseline all revalidated; no new static conclusion is labeled `ClientVerified`; the runtime matrix and stop boundary remain explicit. |

### Final findings and residual boundary

- No unresolved P1 or P2 finding remains within this complete non-code result.
- Approval establishes that the evidence result is safe input for the successor implementation Spec; it does **not** establish native UI behavior or root-task completion. ID `0x11` add/update/delete, category behavior, full-party presentation, and disconnect/replacement behavior still require the declared runtime verification.
- The root controller may now consume this approval, run fresh completion verification for spec-001, and then update the still-pending multi-spec cursor; those controller transitions are not part of this review verdict.
