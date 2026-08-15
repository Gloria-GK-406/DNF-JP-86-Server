# TASK-02 — Concrete State Messages

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Replace the SelectCharacter raw static type/occurrence dispatcher and aggregate multi-frame response with reusable concrete state-synchronization message contracts and narrow encoders while preserving exact frame bytes.
- Referenced spec requirements: RQ-002, RQ-003, RQ-004, RQ-006, RQ-008, RQ-010.
- Referenced plan sections: T-003; BD-004; BD-005; BD-007.
- Owned boundary: Static rows 1–58, row-11 Dark Knight insertion, full inventory snapshots and encoder registration. Handler/lifecycle mutation and supplemental X-02–X-18 remain out of this unit.
- Permitted files or interfaces: `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/**`; static-related files in `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/**`; `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`; `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`; related protocol tests and registry tests; domain record files only when a narrow message input cannot reuse an existing domain record.
- Dependencies: TASK-01.
- Required dependency outputs: Accepted transcript oracle and corrected dispatch boundary.
- Acceptance conditions: Each encoder is reached by a concrete CLR message; no encoder routes by raw wire `type` or `occurrence`; no state-synchronization encoder accepts `SelectCharacterResult`; full inventory snapshot and committed delta semantics share item encoding without SelectCharacter naming; required/empty/optional validation and all accepted bytes remain unchanged; every message is registered exactly once for the game endpoint.
- Task-specific authorization: Covered by the root task authorization; do not change wire order, bytes, conditions or runtime lifecycle sequencing.
- Required methods: For each message family, run the existing byte fixture before and after extraction; add a behavior test only when existing coverage lacks a faithful byte/absence oracle. Refactor in small green steps.
- Required checks: CK-002, CK-006, CK-007, CK-008, CK-011 from `plan.md` as applicable to this unit; focused protocol and registry tests; `git diff --check`.
- Required evidence: Old-to-new semantic message map, focused test results, source scan proving no raw dispatcher/aggregate encoder input, registration evidence and owned diff paths.

## Contract Amendment — A1

- Reason: The sealed unit wording said to replace the aggregate multi-frame response while explicitly leaving handler/lifecycle migration to TASK-03. Removing that response before the handler can send concrete messages would create a deliberately non-runnable dependency state, contrary to plan T-003/T-004 ordering.
- Amended intermediate acceptance: TASK-02 must introduce and register all concrete static state messages, narrow their encoders, and make the current SelectCharacter response adapter expand through those concrete messages without using the raw type/occurrence dispatcher. The compatibility response/adapter may remain only until TASK-03 replaces its producer with handler-driven sends; it is not an accepted final Spec result.
- Unchanged obligations: no concrete sync encoder may consume `SelectCharacterResult`; exact bytes, order, conditions, inventory semantics and registry completeness remain required. TASK-03 still owns deletion of `SelectCharacterResponse`, the adapter and legacy sequencing artifacts.

## Execution Result — Round 1

- Executor: root agent.
- Owned production paths: `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/**`; `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectedCharacterStateMessageFactory.cs`; compatibility edits in `SelectCharacterResponseComposer.cs` and `SelectCharacterSupplementalProjectionCodec.cs`; deletion of the old static body/dispatcher/descriptor files; `ProtocolModule.cs`; `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`.
- Owned test paths: accepted transcript, payload codec and supplemental projection tests under `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/**`.
- TDD method: TASK-01's 32 passing SelectCharacter behavior tests were the pre-change byte/order baseline. This structural extraction was driven against those existing faithful oracles rather than manufacturing collaborator tests. The concrete-registration completeness test was added after implementation and is reported as verification evidence, not as a RED/GREEN claim.
- Deliverable: introduced 48 concrete `GameStateSynchronizationMessage` CLR types, each with one closed generic single-frame encoder registration for the Game endpoint. Each message owns one semantic state shape; repeated USERINFO placement sends `CurrentUserStateMessage` twice, while title books, collection boxes and schedules carry their domain entries rather than raw occurrence indexes.
- Input narrowing: state message encoders consume only existing account, character, inventory or world records needed for that meaning. `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/**` contains no `SelectCharacterResult`, raw `EncodeNotification` dispatcher or `occurrence` routing parameter.
- Transcript composition: `SelectedCharacterStateMessageFactory` remains in the SelectCharacter slice as the temporary explicit ordering adapter. It constructs concrete messages in the accepted order and contains the only aggregate workflow input; the old numeric descriptor table was deleted.
- Inventory reuse: `InventorySnapshotMessage` represents `00/000D`; `InventoryDeltaMessage` represents `00/000E`; both use the same narrow inventory item writer. The existing supplemental delta path now delegates to the concrete delta message.
- Old-to-new semantic map: row 1 -> `CharacterEntryAcceptedMessage`; rows 2-6 -> `InventorySnapshotMessage`; rows 8/54 -> `CurrentUserStateMessage`; row 9 -> `ExtendedUserStateMessage`; rows 21-25 -> `TitleBookStateMessage`; rows 32-33 -> `CollectionBoxStateMessage`; rows 49-51 -> `DailyScheduleStateMessage`; X-01 -> `DarkKnightComboStateMessage`; remaining static rows -> their named creature, quest, settings, progression, wallet, mailbox, PvP, position, pet or explicit baseline message in `StateSynchronizationMessages.cs`.
- Compatibility boundary: `SelectCharacterResponse` and its one thin adapter remain only because TASK-03 has not yet migrated handler sends. The adapter expands the aggregate result into the concrete messages and frames them without a raw switch; it is not the final Spec result.
- Check: focused SelectCharacter tests exited 0; 33 passed, 0 failed, 0 skipped.
- Check: full `Dfo.Server.Protocol.Tests` exited 0; 85 passed, 0 failed, 0 skipped.
- Check: focused `ServerMessageEncoderRegistry` tests exited 0; 4 passed, 0 failed, 0 skipped.
- Check: `dotnet build Server/86JP.Server.slnx --no-restore` exited 0 with 0 warnings and 0 errors; the final focused Protocol project build also exited 0 with 0 warnings and 0 errors.
- Execution note: one concurrent focused-test/build attempt contended on the same Protocol PDB and the test process exited 1 with CS2012. The build process completed successfully; rerunning the exact test sequentially exited 0 with 33 passing tests.
- Static evidence: scans for `EncodeNotification`, `occurrence` and `SelectCharacterResult` under the new state-synchronization directory returned no matches; scans for `SelectCharacterStaticPayloadCodec` and `SelectCharacterStaticSequence` across production/tests returned no matches; registration count is 48; `git diff --check` exited 0.

## Executor Self-check — Round 1

- RQ-002/RQ-004: ACK and every static state meaning now have concrete messages; encoder dispatch is CLR-type based and no raw type/occurrence switch remains.
- RQ-003/RQ-010: concrete encoder inputs are domain-owned records and do not consume the cross-domain SelectCharacter aggregate; the aggregate-to-message ordering adapter is correctly isolated in the SelectCharacter endpoint slice.
- RQ-006: required, loaded-empty and optional conditions continue to be validated by the migrated payload and transcript suite; exact bytes and frame identities are green.
- RQ-008: full inventory snapshot and committed delta are independently registered reusable messages sharing item encoding.
- A1 compliance: the only retained aggregate response path is the explicitly allowed compatibility adapter; TASK-03 must delete it when handler-driven sends become available.
- Residual risk: registration is deliberately explicit and long (48 entries); the completeness test compares all concrete subclasses to registrations so a future omitted line fails immediately.

## Independent Quick Review — IQR-01

- Reviewer: `/root/task02_quick_review` (fresh independent context).
- Verdict: `changes_required`.
- Reviewed contract: revision 1 plus Contract Amendment A1; verdict applies only to the Round 1 Execution Result, Executor Self-check and TASK-02 owned diff.
- Owned diff identity: `sha256:6bc0d2cfe2e4ca15d63d7d2cb9c906c93b2ffa871a9329af3eb215ac1cf15dac` for the combined binary patch/snapshots covering the TASK-02 production and test paths listed by Round 1, including the current accepted-transcript test snapshot but excluding TASK-01-only `AGENTS.md` and command-bus changes.
- Evidence inspected: the sealed Task Unit contract and A1, Round 1 result/self-check; `plan.md` T-003/BD-004/BD-005/BD-007 and CK-002/CK-006/CK-007/CK-008/CK-011; `spec.md` RQ-002/RQ-003/RQ-004/RQ-006/RQ-008/RQ-010 and their directly associated acceptance/evidence rows; repository `AGENTS.md`; TASK-01 accepted transcript output/IQR-02; the complete TASK-02 owned tracked and untracked patch; fresh CK-006 source scans; focused protocol tests (`33 passed, 0 failed, 0 skipped`); full Protocol tests (`85 passed, 0 failed, 0 skipped`); focused registry tests (`4 passed, 0 failed, 0 skipped`); solution build (`0 warnings, 0 errors`); registration/type counts (`48` and `48`); and tracked plus untracked `git diff --check` evidence (no diagnostics).
- Finding IQR-01-F1: the required exact-wire-byte preservation evidence is incomplete. `SelectCharacterAcceptedTranscriptTests` asserts command/type identity, order and conditions but never compares frame bodies (`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs:14`, `:45`, `:163`); `ApplicationProducedResult_EncodesTheCompleteStaticStream` asserts only the frame count (`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterProductionProjectionTests.cs:37`); and the all-family payload test merely asserts non-null output for most bodies (`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs:284`). The `00/000E` test checks only type, container and item count, not the encoded item bytes (`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterSupplementalProjectionTests.cs:60`). Therefore the passing CK-007 run cannot demonstrate the Task acceptance claim that all accepted bytes are unchanged, nor the required per-family before/after byte fixture evidence; a body regression in an otherwise non-empty frame could pass the cited suite.
- Confirmed non-findings: the 48 concrete message subclasses and 48 explicit Game registrations are complete and one-to-one; StateSynchronization messages/codecs do not consume `SelectCharacterResult` or route by raw type/occurrence; `InventorySnapshotMessage` (`00/000D`) and `InventoryDeltaMessage` (`00/000E`) share `WriteInventoryItem`; and the retained aggregate response path is confined to A1's temporary SelectCharacter factory/composer adapter and does not reintroduce the deleted raw dispatcher.
- Required remediation: add a faithful pre-extraction-versus-current exact-frame oracle for the complete accepted static transcript (including bodies) and exercise the registered concrete encoders, with the required/loaded-empty/optional condition variants. Add an exact `00/000E` item-body vector (or a direct snapshot/delta shared-item parity assertion) so the shared item encoding is behaviorally protected, then rerun CK-002/CK-006/CK-007/CK-008 and `git diff --check` in a new execution/review round.

## Execution Result — Round 2

- Executor: root agent.
- Remediation of IQR-01-F1: extracted the accepted pre-refactor codecs from baseline commit `40f701b1f27702d5cca6e5ee3e361378865f3cc2` into a temporary read-only oracle and recorded SHA-256 values for every complete frame, not merely its identity.
- Complete transcript oracle: `AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline` compares all 58 knight/pet-applicable frames against the accepted baseline while encoding every concrete message through its actual `ServerMessageEncoderRegistration`; the existing transcript tests continue to protect required, loaded-empty and optional omission/order behavior.
- Conditional body oracle: `AcceptedEntryTranscript_DarkKnightFrameBytesMatchAcceptedBaseline` protects the X-01 Dark Knight frame body using the actual registered concrete encoder.
- Inventory reuse oracle: `InventoryDelta_UsesTheSnapshotItemEncodingAndMatchesAcceptedFrameBytes` compares the complete `00/000E` frame against the accepted baseline and directly proves that the item vector inside `00/000D` and `00/000E` is byte-identical.
- Check: focused SelectCharacter tests exited 0; 36 passed, 0 failed, 0 skipped.
- Check: full `Dfo.Server.Protocol.Tests` exited 0; 88 passed, 0 failed, 0 skipped.
- Check: focused `ServerMessageEncoderRegistry` tests exited 0; 4 passed, 0 failed, 0 skipped.
- Check: `dotnet build Server/86JP.Server.slnx --no-restore` exited 0 with 0 warnings and 0 errors.
- Static checks: `git diff --check` exited 0; scans for `EncodeNotification`, `occurrence` and `SelectCharacterResult` under StateSynchronization returned no matches; scans for deleted raw dispatcher/sequence names across source and tests returned no matches.

## Executor Self-check — Round 2

- IQR-01-F1 is directly remediated: the complete accepted static transcript is protected frame-for-frame, and encoder resolution occurs through the same registered CLR-type seam used by runtime delivery.
- RQ-006 exact-byte evidence now covers all static families plus the job-9 insertion; the earlier condition tests protect omission semantics without duplicating unchanged bodies.
- RQ-008 now has both a complete accepted `00/000E` frame vector and direct shared-item parity with `00/000D`.
- No production scope was added during remediation; all Round 2 changes are behavior-preservation tests and this execution evidence.

## Independent Quick Review — IQR-02

- Reviewer: `/root/task02_round2_review` (fresh independent context).
- Verdict: `pass`; no blocking findings.
- Confirmed evidence: all 58 complete frames use accepted SHA-256 baselines and actual registered encoders; Dark Knight condition/order/body is protected; `00/000D` and `00/000E` share the 84-byte item encoding and the complete delta frame matches baseline; 48 concrete messages map one-to-one to 48 Game registrations.
- Confirmed boundary: no raw type/occurrence dispatcher or aggregate input exists under StateSynchronization; TASK-02 did not alter handler, command lifecycle, transition gate or connection behavior; the temporary aggregate adapter remains only under Amendment A1 for TASK-03 removal.
- Fresh checks observed by reviewer: SelectCharacter 36/36, Protocol 88/88, registry 4/4, solution build 0 warnings/errors and `git diff --check` clean.
- Scope of verdict: TASK-02 Round 2 only; TASK-03 and the complete Spec remain open.
