# TASK-03 — Compose the Exact Static Sequence

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Compose all 58 legacy static occurrences from an explicit descriptor sequence, preserving duplicate identities, conditional omission, row order and the X-01 injection point.
- Referenced spec requirements: RQ-001, RQ-003, RQ-005; AC-001, AC-002, AC-003, AC-005.
- Referenced plan sections: T-003; BD-005, BD-007; CK-004.
- Owned boundary: Static sequence descriptor/composer and complete-stream Networking tests.
- Permitted files or interfaces: `Server/src/Dfo.Server.Networking/Framing/SelectCharacter*.cs`; `Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacter*`.
- Dependencies: TASK-02.
- Required dependency outputs: Accepted deterministic body codecs for every static row.
- Acceptance conditions: Descriptor identity count is exactly 58; emitted order matches the matrix; duplicates remain separate; inapplicable conditional rows omit only themselves; row 38 always emits exactly once; X-01 appears at most once immediately after row 11.
- Task-specific authorization: Implement and test sequence composition only; do not implement lifecycle triggers, delivery or mutation; do not commit or push.
- Required methods: Behavior-first mechanical identity and representative complete-stream cases.
- Required checks: CK-004; Networking project tests; `git diff --check`.
- Required evidence: RED/GREEN sequence evidence, exact identity comparison, conditional scenarios, changed paths, checks, owned diff identity and executor self-check appended to this file.

## Execution Result ER-001

- Status: complete.
- Implemented one explicit `SelectCharacterStaticSequence` containing exactly 58 row-numbered descriptors. Each descriptor preserves command, type, legacy occurrence, inventory-container identity and its own applicability condition; the composer delegates bodies to accepted TASK-02 codecs and frames them without querying business state.
- Exact mechanics: rows are mechanically `1,2,...,58`; the descriptor count is 58; inventory rows 2-6 retain distinct container identities while their legacy occurrence remains zero; all five title-book, both collection-box, three schedule and three user-info occurrences remain separate.
- Conditional behavior: row 10 requires the Knight job and loaded deck; row 58 requires a configured welcome plus an equipped creature matching loaded pet runtime; omission removes only that descriptor. X-01 is not counted among the 58 and is emitted at most once for job 9 with applicable combo state, immediately after row 11 and before row 12. Row 38 is unconditional and has no injection/fallback path, so it emits exactly once.
- Behavioral RED: CK-004 initially failed compilation with `CS0103` because `SelectCharacterStaticSequence` did not exist. A separate LINQ inference error in the first test draft was corrected before GREEN and is not claimed as behavior evidence.
- Fresh GREEN/check evidence:
  - CK-004 `--filter "FullyQualifiedName~SelectCharacterResponseComposer|FullyQualifiedName~SelectCharacterStaticSequence"` — passed 4/4.
  - Full Networking project — passed 49/49.
  - `git diff --check` — passed.
- Behavior vectors: exact 58 descriptor identity/occurrence/container comparison; baseline omission of only Knight/pet rows with row-38 exactly once; Dark Knight X-01 adjacency and multiplicity; Knight+pet scenario emitting all 58 static rows without X-01.
- Mechanical identity evidence: source descriptor extraction returned count `58` and row sequence `1,2,3,...,58` with no gap or duplicate; the exact tuple list is independently frozen in the focused test.
- Owned paths:
  - `Server/src/Dfo.Server.Networking/Framing/SelectCharacterStaticSequence.cs`
  - `Server/src/Dfo.Server.Networking/Framing/SelectCharacterResponseComposer.cs`
  - `Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacterResponseComposerTests.cs`
- Complete owned diff identity: `ddda230f1d5f49a6919550b4b6f394162720c98e`, generated from `git diff --binary HEAD` over the three owned paths using an isolated alternate index with intent-to-add for the untracked descriptor file (231 insertions, 98 deletions). The shared index was not changed.
- Boundary: no supplemental X-02+, lifecycle trigger, recipient, sender, transport, service/repository query, mutation, controller/review/result, commit or push was added.

## Executor Self-check SC-001

- Exact sequence: PASS — 58 descriptors match the reviewed matrix one-for-one and in order, including repeated identities and inventory containers.
- Conditions: PASS — Knight and pet conditions omit only themselves; X-01 is outside the static count and adjacent to row 11; row 38 is unconditional and exactly once.
- Architecture: PASS — composer consumes immutable completed result values and accepted body codecs; it owns ordering/framing only.
- Tests: PASS — focused mechanical/complete-stream cases and full Networking regression are fresh and green.
- Hygiene/ownership: PASS — complete hash includes the untracked descriptor source, `git diff --check` passes, and no out-of-contract artifact was edited.

## Independent Quick Review — IQR-001-20260812

- Verdict: `changes_required`
- Reviewed Task contract revision: 1; reviewed ER-001 and SC-001.
- Owned diff identity: independently reproduced complete Git blob hash `ddda230f1d5f49a6919550b4b6f394162720c98e` from an isolated alternate index over the three declared owned paths (3 files, 231 insertions, 98 deletions), including the untracked descriptor source without changing the shared index.
- Commands/evidence inspected: current TASK-03; its three-path owned diff; plan T-003, BD-005/BD-007 and CK-004; accepted TASK-02 ER-003/SC-003/IQR-003 and its cited deterministic pet-welcome codec/test fixture; reviewed migration-matrix rows 1-58 and X-01; independently rerun CK-004 (4/4 passed), full Networking (49/49 passed), and `git diff --check` (passed).
- Finding F-001: row 58's applicability recomputes an equipment/runtime join that is not part of the accepted TASK-02 codec input, so a valid supplied pet-welcome projection can be silently omitted. The accepted codec needs only non-null `World.Content.PetWelcome` with at least one line and directly encodes that resolved value (`Server/src/Dfo.Server.Networking/Framing/SelectCharacterStaticBodyFamilies.cs:340-347`). Its accepted complete fixture supplies exactly such a value (`Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacterPayloadCodecTests.cs:501-507`), but TASK-03's `HasPetWelcome` additionally requires an equipment slot-24 item of kind 5 whose `Core.Value` matches a character pet runtime (`Server/src/Dfo.Server.Networking/Framing/SelectCharacterStaticSequence.cs:68-78`). The same accepted fixture has neither that slot nor that identifier relationship (`SelectCharacterPayloadCodecTests.cs:415-425,458-460`), and the new baseline test consequently treats row 58 as inapplicable even though its accepted body input is present (`Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacterResponseComposerTests.cs:49-62`). This contradicts T-003's requirement to consume the completed static codec inputs and makes conditional omission broader than the row's resolved applicability.
- Confirmed unaffected mechanics: descriptor count/order and command/type/occurrence tuples exactly match all 58 matrix rows; inventory container identities are preserved; row 38 is one unconditional descriptor; X-01 is outside the 58 and is emitted at most once immediately after row 11; Knight and X-01 predicates are locally consistent with their typed inputs.
- Required remediation: make row 58 applicability consume the already-resolved pet-welcome projection supplied to the accepted TASK-02 codec (emit for a valid non-null/non-empty projection; omit only the inapplicable representation), without rediscovering equipment/runtime state in Networking. Add focused positive and negative row-58 stream cases that prove a valid supplied projection emits exactly once and an inapplicable projection omits only row 58, then rerun CK-004, full Networking, `git diff --check`, and report a new complete owned-diff identity.

## Execution Result ER-002 — IQR-001 Remediation

- Status: complete; supersedes ER-001 only for row-58 applicability evidence.
- F-001 correction: `PetWelcome` applicability now consumes the resolved G5 input only: non-null with at least one line emits row 58; null or an empty line collection omits row 58. Networking no longer rediscovers equipment slot, item kind, runtime pet key or any other owner join.
- Behavioral RED: the valid accepted complete projection initially failed two positive stream vectors because row 58 was absent (`Expected ... 00/0077`, actual ended at row 57). The null/empty negative vector was added in the same cycle and proves each representation omits only row 58.
- Fresh GREEN/check evidence:
  - CK-004 — passed 5/5.
  - Full Networking — passed 50/50.
  - `git diff --check` — passed.
- Coverage: valid resolved welcome emits exactly once even without equipment/runtime correlation in Networking; null and empty-lines each omit only row 58 and leave row 57/order intact; all prior 58-descriptor, duplicate, row-38 and X-01 mechanics remain covered.
- Complete owned diff identity: `842def9aae8a31b89e08a56b2cbb70d495215c2f`, generated from the complete three-path binary diff with an isolated alternate index including the untracked sequence source (240 insertions, 98 deletions). Shared index remained untouched.

## Executor Self-check SC-002

- F-001: PASS — applicability is exactly the accepted codec precondition and consumes only resolved G5 content.
- Conditional locality: PASS — valid emits once; null/empty omit only row 58; no owner join exists in Networking.
- Regression/hygiene: PASS — focused and full Networking checks plus diff check are fresh and green.
- Boundary: PASS — no review/controller/result, lifecycle, sender, mutation, commit or push change.

## Independent Quick Review — IQR-002-20260812

- Verdict: `passed`
- Reviewed Task contract revision: 1; reviewed the latest remediation ER-002 and SC-002 against plan T-003, BD-005/BD-007 and CK-004.
- Owned diff identity: independently reproduced complete Git blob hash `842def9aae8a31b89e08a56b2cbb70d495215c2f` from an isolated alternate index over the three declared owned paths, including the untracked sequence source without changing the shared index (240 insertions, 98 deletions).
- Commands/evidence inspected: current TASK-03 through SC-002; exact three-path owned diff; plan T-003, BD-005/BD-007 and CK-004; row-58 accepted codec precondition and complete projection fixture; independently rerun CK-004 (5/5 passed), full Networking (50/50 passed), and `git diff --check` (passed).
- F-001 disposition: resolved. Row 58 now trusts only the completed `World.Content.PetWelcome` projection: a non-null value with at least one line emits the row exactly once, while `null` or an empty line collection omits only row 58 (`Server/src/Dfo.Server.Networking/Framing/SelectCharacterStaticSequence.cs:68-71`; `Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacterResponseComposerTests.cs:49-62,85-130`). This matches the accepted codec precondition (`Server/src/Dfo.Server.Networking/Framing/SelectCharacterStaticBodyFamilies.cs:340-347`) and no longer rediscovers equipment/runtime ownership state.
- Regression finding: no obvious blocking defect. The exact 58-row descriptor identities/order and inventory container identities remain frozen; row 38 remains one unconditional descriptor; conditional omission preserves neighbors; X-01 remains outside the static count and occurs at most once immediately after row 11. The fresh focused and full checks cover these mechanics.
- Required remediation: none for this Task Unit. This quick-review pass permits TASK-03 acceptance logic only and is not approval of the complete Spec or delivery.
