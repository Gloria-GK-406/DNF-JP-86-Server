# TASK-04 — Encode Supplemental Projections

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Define neutral packet-ready values and deterministic body/batch projections for X-01 through X-18 without deciding triggers, discovering recipients or mutating lifecycle state.
- Referenced spec requirements: RQ-003, RQ-004, RQ-005, RQ-006; AC-002, AC-003, AC-004, AC-005, AC-006.
- Referenced plan sections: T-004; BD-001, BD-005, BD-007; CK-005.
- Owned boundary: Neutral supplemental projection contracts, Networking supplemental codecs and focused behavior tests.
- Permitted files or interfaces: `Server/src/Dfo.Server.Data/Game/SelectCharacter*.cs`; `Server/src/Dfo.Server.Networking/Framing/SelectCharacter*.cs`; `Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacter*`.
- Dependencies: TASK-03.
- Required dependency outputs: Accepted static sequence and body codecs.
- Acceptance conditions: Every X identity has deterministic positive/negative/empty/multi form as applicable; X-08 is an ordered rejection pair; X-13/X-14 adjacency and static row36/X-11 distinction are preserved; recipients are explicit values and no socket/service/repository appears in codecs.
- Task-specific authorization: Implement only projection contracts and encoders; G7 owns triggers, recipients, state transitions and rollback; do not commit or push.
- Required methods: Behavior-first supplemental identity/order/multiplicity cases.
- Required checks: CK-005; Networking project tests; `git diff --check`; lifecycle-dependency scan.
- Required evidence: RED/GREEN commands, X coverage table, changed paths, checks, owned diff identity and executor self-check appended to this file.

## Execution Result ER-001

- Status: complete.
- Test-value gate: passed. X-08 pair ordering, X-09/X-10 direct/publication separation, X-13/X-14 adjacency and X-15/X-16 multiplicity are observable protocol behavior with high regression risk; behavior-first tests materially protect them.
- Behavioral RED: CK-005 initially failed compilation with missing `SelectCharacterSupplementalProjectionCodec` and the packet-ready supplemental contracts (`CS0103`/`CS0246`). The same focused command passed after implementing the contracts/codecs.
- X coverage:

| Identities | Deterministic projection behavior |
|---|---|
| X-01 | Optional Dark Knight combo frame, sharing the accepted loaded-state precondition/body. |
| X-02/X-03 | Visibility and clone-title frames from explicit wire user/state and title values. |
| X-04 | Routed subtype-2 roster body with complete per-character appearance entries; unavailable appearance collection fails rather than fabricating an empty value. |
| X-05/X-06 | Honor frame and optional fixed-layout growth-capsule frame from explicit account/skill/progression values. |
| X-07 | Zero-or-more owner-produced inventory-update bodies framed in supplied group order. |
| X-08 | Exactly ordered SELECT_CHARACTER rejection command followed by server notice. |
| X-09/X-10 | Ordered identity-roster and room-snapshot direct frames, followed by a separate newcomer publication frame value. |
| X-11/X-12 | Distinct trailing equipment-lock occurrence and optional loaded-empty/populated accepted-quest body. |
| X-13/X-14 | Null means no batch; a value always produces revival immediately followed by creature state. |
| X-15/X-16 | Null means no offer; a value produces one offer then one participant frame per supplied user id in registry order. |
| X-17/X-18 | Pure expert-store-close and town-leave bodies; target fan-out remains outside the codec. |

- Fresh checks:
  - CK-005 `--filter "FullyQualifiedName~SelectCharacterSupplemental|FullyQualifiedName~SelectCharacterRejection"` — passed 8/8.
  - Full Networking project — passed 58/58.
  - Full solution with `PVF_ARCHIVE_PATH="$PWD/Script.pvf"` — passed: PVF 60/60, Application 199/199, Networking 58/58, Infrastructure 90/92 with the two documented archive-negative tests skipped.
  - Lifecycle-dependency scan for sockets, TouchSocket, services, repositories, SQLite and PVF in the two product files — no matches.
  - `git diff --check` — passed.
- Owned paths:
  - `Server/src/Dfo.Server.Data/Game/SelectCharacterSupplementalModels.cs`
  - `Server/src/Dfo.Server.Networking/Framing/SelectCharacterSupplementalProjectionCodec.cs`
  - `Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacterSupplementalProjectionTests.cs`
- Complete owned diff identity: `cb0e8b8e9f9d0c139e67b9b3af38a5e5c51a4925`, generated from the full binary cached diff of all three owned untracked files in an isolated alternate index (609 insertions); the shared index was not changed.
- Boundary: codecs consume immutable values only. They do not decide lifecycle applicability, discover recipients, query registries/repositories, mutate entry state or send frames. X-07/X-09/X-10/X-12-X-18 triggers, target fan-out and state transition/rollback remain G7 work.

## Executor Self-check SC-001

- Completeness: PASS — each X-01 through X-18 has a typed deterministic projection entry and focused behavior coverage by family.
- Ordering/multiplicity: PASS — X-08 pair, X-09/X-10 direct/publication boundary, X-13/X-14 adjacency and X-15/X-16 participant order are frozen by tests; X-11 remains a distinct occurrence.
- Required data: PASS — X-04 appearance and other required collections/bodies fail closed; optional/loaded-empty states are represented separately and no required value is replaced with a convenient zero.
- Architecture: PASS — Data contracts contain values only; Networking has no lifecycle, target discovery, transport, repository, Infrastructure, PVF or business-service dependency.
- Verification/hygiene: PASS — focused, full Networking, full solution, dependency scan and diff check are fresh and green; owned diff hash includes every new file.
- Ownership: PASS — no controller, sdd-execution, review/result, other Task, commit or push was changed.

## Independent Quick Review — IQR-001-20260812

- Verdict: `changes_required`
- Reviewed Task contract revision: 1; reviewed ER-001 and SC-001 against plan T-004, BD-001/BD-005/BD-007 and CK-005.
- Owned diff identity: independently reproduced complete Git blob hash `cb0e8b8e9f9d0c139e67b9b3af38a5e5c51a4925` from an isolated alternate index over the three declared owned paths, including all three untracked files without changing the shared index (609 insertions).
- Commands/evidence inspected: current TASK-04; exact three-path owned diff; plan T-004, BD-001/BD-005/BD-007 and CK-005; accepted TASK-03 ER-002/SC-002/IQR-002 and its cited static-sequence/body-codec seams; independently rerun CK-005 (8/8 passed), full Networking (58/58 passed), lifecycle-dependency scan (no matches), and owned-path `git diff --check` (passed).
- Finding F-001: X-01 is not exposed as a trigger-neutral codec seam. `EncodeDarkKnightCombo` accepts the complete `SelectCharacterResult` and calls `SelectCharacterStaticSequence.HasDarkKnightCombo`, which inspects both character job and loaded combo state to return either a frame or no frames (`Server/src/Dfo.Server.Networking/Framing/SelectCharacterSupplementalProjectionCodec.cs:13-16`; `Server/src/Dfo.Server.Networking/Framing/SelectCharacterStaticSequence.cs:64-66`). Its focused test explicitly treats changing the job from the baseline to job 9 as the negative/positive decision (`Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacterSupplementalProjectionTests.cs:13-23`). This contradicts T-004's output boundary and Task-specific authorization that supplemental codecs encode supplied packet-ready values while G7 owns triggers/applicability; it also contradicts ER-001/SC-001's claim that the codecs do not decide lifecycle applicability.
- Confirmed unaffected mechanics: X-02 through X-18 each have a concrete codec seam. X-04 rejects a null per-character appearance collection and invalid/null expansion values; X-08 emits the ordered rejection pair; X-09/X-10 retain direct roster/room order and separate newcomer publication; X-11 is a dedicated trailing occurrence seam; X-13/X-14 are one adjacent pair; X-15/X-16 emit one offer followed by participants in supplied multiplicity/order; X-17/X-18 encode pure bodies. No target discovery, transport send, repository/service dependency or mutation is present in the owned product diff.
- Required remediation: replace the X-01 result-driven applicability decision with a neutral packet-ready input (for example, an optional already-resolved body/value) whose presence is decided by its caller, then prove absent/present encoding without inspecting job or lifecycle state. Rerun CK-005, full Networking, the lifecycle-dependency scan and `git diff --check`, and report a new complete owned-diff identity.

## Execution Result ER-002 — IQR-001 Remediation

- Status: complete; supersedes ER-001 only for X-01 supplemental seam and its boundary evidence.
- Reproduction/root cause: the accepted review evidence showed `EncodeDarkKnightCombo(SelectCharacterResult)` calling `SelectCharacterStaticSequence.HasDarkKnightCombo`, so the codec inspected job/combo applicability and chose presence. The falsifiable hypothesis was that replacing the complete result with an optional packet-ready body would remove all static/lifecycle inspection while retaining deterministic encoding; the implementation and source scan confirmed it.
- Behavioral RED: after changing the focused vector to call `EncodeDarkKnightCombo(null)` and `EncodeDarkKnightCombo(new SelectCharacterDarkKnightComboProjection([1,2,3]))`, CK-005 failed with `CS0246` for the absent neutral contract and `CS8625` because the existing result-driven signature rejected null.
- Correction: `SelectCharacterDarkKnightComboProjection` now carries only the already-resolved body. Null returns no supplemental frame; a present value produces exactly one `00/01C0` frame with that body. The supplemental codec contains no `HasDarkKnightCombo` or `SelectCharacterStaticSequence` reference. TASK-03's accepted static sequence remains unchanged.
- Fresh checks:
  - CK-005 — passed 8/8.
  - Full Networking project — passed 58/58.
  - Lifecycle-dependency scan — no socket, TouchSocket, service, repository, SQLite or PVF match.
  - X-01 source-boundary scan — only the explicit encode method and null/present behavior calls remain; no static-sequence/applicability reference exists.
  - `git diff --check` — passed.
- Complete owned diff identity: `a38e6e8dfe798dd4b2fbddfecc8879f925e73764`, generated from the full binary diff of all three owned untracked files in an isolated alternate index (609 insertions); the shared index was not changed.
- Boundary: no trigger, job check, lifecycle state, target discovery, mutation, static TASK-03 change, review/controller/result change, commit or push was introduced.

## Executor Self-check SC-002

- F-001: PASS — supplemental X-01 consumes only optional packet-ready input; presence is an explicit caller decision.
- Behavior: PASS — null produces zero frames and a supplied body produces exactly one correctly identified frame without transformation or inference.
- Regression: PASS — all prior X-02-X-18 behavior remains covered and full Networking is green.
- Architecture/hygiene: PASS — dependency and X-01 boundary scans plus diff check are clean; the complete owned hash includes all three files.
- Ownership: PASS — only TASK-04 owned product/tests and this appended execution evidence changed.

## Independent Quick Review — IQR-002-20260812

- Verdict: `passed`
- Reviewed Task contract revision: 1; reviewed only the latest remediation ER-002 and SC-002 against plan T-004, BD-001/BD-005/BD-007 and CK-005.
- Owned diff identity: independently reproduced complete Git blob hash `a38e6e8dfe798dd4b2fbddfecc8879f925e73764` from an isolated alternate index over the three declared owned paths, including all three untracked files without changing the shared index (609 insertions).
- Commands/evidence inspected: current TASK-04 through SC-002; exact three-path owned diff; plan T-004, BD-001/BD-005/BD-007 and CK-005; accepted TASK-03 static-sequence/body-codec output needed by this Task; independently rerun CK-005 (8/8 passed), full Networking (58/58 passed), lifecycle-dependency and X-01 boundary scans, owned-path `git diff --check`, and the complete owned-diff identity calculation.
- F-001 disposition: resolved. X-01 now consumes only an optional packet-ready `SelectCharacterDarkKnightComboProjection`; `null` emits no frame and a supplied body emits exactly one `00/01C0` frame (`Server/src/Dfo.Server.Networking/Framing/SelectCharacterSupplementalProjectionCodec.cs:13-20`; `Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacterSupplementalProjectionTests.cs:13-17`). The codec no longer accepts `SelectCharacterResult` for X-01 and contains no `SelectCharacterStaticSequence`, `HasDarkKnightCombo`, job check or other applicability inference.
- Regression finding: no obvious blocking defect. X-02 through X-18 retain their concrete seams and focused behavior: X-04 required appearance validation; ordered X-08 pair; separated X-09/X-10 direct/publication batches; distinct trailing X-11; loaded-empty X-12; adjacent optional X-13/X-14 pair; ordered multiplicity for X-15/X-16; and pure X-17/X-18 bodies. No socket, transport send, recipient discovery, repository/service query or lifecycle mutation is present in the owned product diff.
- Required remediation: none for this Task Unit. This quick-review pass permits TASK-04 acceptance logic only and is not approval of the complete Spec or delivery.
