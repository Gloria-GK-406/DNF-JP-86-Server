# TASK-06 — Verify and Document the G6 Handoff

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Produce exact static/X coverage documentation and fresh integrated verification that proves the complete G6 result-only projection and clearly hands lifecycle triggering, targeting, mutation and rollback to G7.
- Referenced spec requirements: RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-007; AC-001 through AC-007.
- Referenced plan sections: T-006; BD-007, BD-008; CK-008, CK-009, CK-010.
- Owned boundary: Select-character G6 coverage documentation, matrix status annotations and only test corrections required to close integrated acceptance evidence.
- Permitted files or interfaces: `Docs/Refactor/SelectCharacterMigrationMatrix.md`; `Docs/Refactor/SelectCharacterG6Coverage.md`; directly affected SelectCharacter tests; this Task file.
- Dependencies: TASK-05.
- Required dependency outputs: Accepted typed inputs, body codecs, static sequence, supplemental projections and delivery integration from TASK-01 through TASK-05.
- Acceptance conditions: Rows 1-58 and X-01-X-18 map exactly to implemented projections; code-reversed confidence remains explicit; G7 responsibilities are not claimed complete; full real-PVF solution, dependency and diff checks pass.
- Task-specific authorization: Complete documentation and integrated evidence only; do not add lifecycle mutation, run an external client, commit, push or alter controller/review/result artifacts.
- Required methods: Mechanical coverage comparison, honest manual boundary review and fresh full verification.
- Required checks: CK-008, CK-009 and CK-010; stale-placeholder scan.
- Required evidence: Exact coverage count/list, full test totals, expected skips, dependency/diff results, changed paths, risks, owned diff identity and executor self-check appended to this file.

## Execution Result ER-001

- Status: complete.
- Documentation delivered: `SelectCharacterG6Coverage.md` records every static and supplemental identity with its implemented typed source, codec/occurrence, condition, and remaining G7/caller responsibility. The migration matrix now links to this audit and marks G6 result-only projection coverage without changing any legacy confidence label.
- Confidence/boundary: both documents state that protocol structure and meaning remain code-reversed without packet-capture or real-client confirmation. G6 is claimed only as a result-to-wire and explicit-delivery capability. G7 lifecycle triggering, target discovery, preparation mutation and rollback remain explicitly incomplete.
- Mechanical coverage evidence:
  - Coverage static table contains 58 numbered rows; successor `SelectCharacterStaticSequence` contains 58 descriptors; both lists equal `1..58` and equal each other in order.
  - Coverage supplemental table contains 18 IDs and equals `X-01..X-18` in order.
  - Static row-number/wire-identity pairs are byte-for-byte equal to the reviewed migration-matrix columns after mechanical extraction; X ID/wire-identity pairs are likewise equal.
  - The descriptor source has exactly two conditional static entries, rows 10 and 58; the coverage table records those exact conditions. Row 38 and the X-01 row-11 adjacency remain called out separately.
  - Both coverage tables have consistent column counts; stale `TODO`, `TBD`, `NotImplemented`, temporary and hack markers have zero matches.
- Fresh checks:
  - CK-008 with the real `Script.pvf` path — passed: PVF 60/60; Application 199/199; Networking 61/61; Infrastructure 90/92 with exactly the two documented archive-negative tests skipped.
  - CK-009 `git diff --check` — passed.
  - CK-010 manual/mechanical comparison — passed with exact 58 static and 18 supplemental identities and explicit per-entry G7/caller responsibility.
  - Corrected dependency boundary from TASK-05 A-001 — Networking repository/PVF/SQLite scan: zero matches; Application concrete SQLite/PVF scan: zero matches.
- Changed paths:
  - `Docs/Refactor/SelectCharacterG6Coverage.md`
  - `Docs/Refactor/SelectCharacterMigrationMatrix.md`
- Complete owned diff identity: `de869a33e15d9c4b34734335a14edf43893fa478`, generated from the full binary diff of both documentation paths in an isolated alternate index including the new coverage file (160 insertions); the shared index was not changed.
- Risks retained: field meanings remain inferred until capture/client verification; supplemental encoders and delivery are seams, not proof that G7 invokes them at every correct lifecycle point. No product/test, lifecycle, controller/review/result, commit or push change was made.

## Executor Self-check SC-001

- Exact coverage: PASS — rows 1–58 and X-01–X-18 are complete, ordered and identity-equal to the reviewed matrix.
- Conditions/duplicates: PASS — rows 10/58, row 38, row 36 versus X-11, and X ordering/multiplicity seams are recorded without collapsing occurrences.
- Source honesty: PASS — every row/X maps to current typed input and codec; unavailable G7-owned composition such as X-04 roster appearance is described as caller work rather than claimed wired.
- G6/G7 boundary: PASS — trigger, target-discovery, mutation and rollback responsibilities are explicit and G7 is not marked complete.
- Confidence: PASS — code-reversed status and the need for packet capture/real-client verification remain prominent in both documents.
- Verification/hygiene: PASS — real-PVF full solution, dependency scans, mechanical comparisons, stale-marker scan and diff check are fresh and green; owned hash includes both documentation files.
- Ownership: PASS — only permitted documentation and this Task's result/self-check were changed.

## Independent Quick Review — IQR-001-20260812

- Verdict: `changes_required`
- Reviewed Task contract revision/amendments: revision 1; reviewed ER-001 and SC-001 against plan T-006, BD-007/BD-008 and CK-008/CK-009/CK-010.
- Owned diff identity: the declared identity `de869a33e15d9c4b34734335a14edf43893fa478` was not reproduced. An isolated alternate index over the two declared documentation paths produced the complete binary/full-index diff identity `41854ea49a1242b51d09915c54907a340714418b` with 160 insertions and 0 deletions, without changing the shared index.
- Commands/evidence inspected: current TASK-06; exact two-document owned diff; plan T-006, BD-007/BD-008 and CK-008/CK-009/CK-010; accepted TASK-05 ER-001/SC-001/IQR-001 output and amendment A-001; mechanical extraction/diff of matrix and coverage identities; table-width and stale-marker checks; independently rerun CK-008, corrected dependency scans inherited from TASK-05 A-001, CK-009, and complete owned-diff identity calculation.
- Findings: exact functional/documentation coverage is present. The matrix and coverage document each contain 58 static identities and 18 supplemental identities; their row/ID and wire-identity pairs compare equal in order, the coverage sequences are exactly `1..58` and `X-01..X-18`, table widths are consistent, and stale-marker matches are zero (`Docs/Refactor/SelectCharacterG6Coverage.md:30-124`). Code-reversed confidence remains explicit in both documents (`SelectCharacterMigrationMatrix.md:3-16`; `SelectCharacterG6Coverage.md:3-10,147-149`). The G6 claim is limited to result-to-wire capability and G7 retains triggering, target discovery, mutation and rollback, including the named X-07 and X-09-X-18 responsibilities (`SelectCharacterG6Coverage.md:126-145`), so no G7 completion is claimed.
- Verification disposition: CK-008 freshly passed with PVF 60/60, Application 199/199, Networking 61/61, and Infrastructure 90/92 with exactly the two documented archive-negative skips. Corrected dependency scans reported zero prohibited Networking repository/PVF/SQLite matches and zero concrete Application SQLite/PVF matches. CK-009 passed.
- Blocking defect: ER-001 and SC-001 claim that the complete owned diff identity is `de869a33e15d9c4b34734335a14edf43893fa478`, but the current complete two-path binary diff reproducibly hashes to `41854ea49a1242b51d09915c54907a340714418b`. Therefore the required owned-diff evidence does not identify the reviewed documentation round.
- Required remediation: append a new execution result and self-check for the current unchanged two-document diff using the reproducible complete owned-diff identity, or restore the exact owned content that yields the declared identity and rerun the required checks. Then launch a fresh quick-review round. No product, documentation, controller state, commit or push remediation is authorized by this review.

## Execution Result ER-002 — IQR-001 Evidence Correction

- Status: complete; supersedes ER-001 only for owned-diff identity evidence. The two reviewed documentation files are unchanged.
- Root cause: ER-001 hashed the alternate-index binary diff with abbreviated index object IDs. The reviewer uses `--binary --full-index`; that flag changes the diff text by emitting complete object IDs and therefore produces a different hash without any content difference.
- Reproduction: the unchanged two-path alternate index hashes to `de869a33e15d9c4b34734335a14edf43893fa478` without `--full-index` and to the reviewer-reproduced `41854ea49a1242b51d09915c54907a340714418b` with `--binary --full-index --cached HEAD`.
- Correct complete owned diff identity: `41854ea49a1242b51d09915c54907a340714418b`, covering both declared documentation paths in an isolated alternate index, including the untracked coverage document (160 insertions, 0 deletions); the shared index was not changed.
- Fresh checks: static coverage/descriptors remain exactly 58 and equal `1..58`; supplemental coverage remains exactly 18 and equal `X-01..X-18`; `git diff --check` passes.
- Boundary: no documentation content, product, test, controller/review/result, commit or push change was made in this remediation.

## Executor Self-check SC-002

- F-001: PASS — the reported hash exactly matches the reviewer's full-index calculation method and current unchanged two-document diff.
- Coverage regression: PASS — fresh 58+18 mechanical comparisons remain green.
- Hygiene/ownership: PASS — diff check passes; only this Task's append-only execution evidence changed.

## Independent Quick Review — IQR-002-20260812

- Verdict: `passed`
- Reviewed Task contract revision/amendments: revision 1; bounded R2 review of ER-002 and SC-002 remediation for IQR-001 F-001 against plan T-006, BD-008, CK-009 and CK-010. No earlier functional evidence was reopened.
- Owned diff identity: an isolated alternate index over exactly `Docs/Refactor/SelectCharacterG6Coverage.md` and `Docs/Refactor/SelectCharacterMigrationMatrix.md` reproduced `41854ea49a1242b51d09915c54907a340714418b` from `git diff --binary --full-index --cached HEAD`. The same unchanged two-document content reproduced `de869a33e15d9c4b34734335a14edf43893fa478` without `--full-index`, confirming ER-002's hash-method correction. The owned diff remains 160 insertions and 0 deletions (coverage 149/0; matrix 11/0).
- Commands/evidence inspected: current TASK-06 ER-002/SC-002; plan T-006 and BD-008; isolated alternate-index full-index and abbreviated-index hash calculations; two-path name/numstat and cached diff check; whole-worktree `git diff --check`; fresh mechanical extraction and ordered identity diff between both documentation tables.
- Findings: F-001 is remediated and the corrected owned-diff identity exactly identifies the current two-document content, demonstrating that the documents did not drift after IQR-001. Fresh extraction found 58 matrix static rows and 58 coverage static rows, ordered 1 through 58, plus 18 matrix and 18 coverage supplemental rows, ordered X-01 through X-18; both ordered row/ID plus wire-identity comparisons were empty. CK-009 passed for both the owned alternate-index diff and the current worktree. The unchanged documents retain explicit code-reversed confidence and no real-client claim (`SelectCharacterMigrationMatrix.md:3-16`; `SelectCharacterG6Coverage.md:3-10,147-149`) and retain the T-006 handoff of triggering, targeting, mutation and rollback to G7 (`SelectCharacterG6Coverage.md:126-145`).
- Required remediation: none. This pass is limited to the TASK-06 R2 quick-review boundary and permits controller acceptance logic; it is not a Spec or final-delivery approval.
