---
kind: rks-review
verdict: approved
---

# Change Review — Revision 3

## Current verdict — re-review after F-002 remediation

`approved`. This verdict supersedes the post-live `changes_required` verdict below for the current
remediation target. It approves the consistency repair only; it does **not** establish TASK-08 or root
completion.

F-002 is closed:

- `NOTI 0x0008` and `NOTI 0x000B` now remain `Implemented`; both dedicated records explicitly separate
  the successful aggregate party UI observation from the still-missing per-frame proof, and their pending
  checks require attributable send/read evidence. `protocol/game/index.md` and `protocol/catalog.md`
  carry the same scoped statuses.
- `TASK-08-final-validation.md` keeps the functional/UI live oracle and 3,050/3,050 automated result,
  but states that the Task Unit remains `in_progress` pending the contract-required captured-frame rerun.
- `spec-result.md` is `partial`, `completion-evidence.md` is `manual_required`, and
  `sdd-execution.yaml` is at `current_stage: verification`, root `state: in_progress`, with TASK-08
  `in_progress`. These artifacts no longer claim that SC-1..SC-10 or the root workflow are complete.
- The successor note now accurately says C2S `0x000C SET_PARTY_INFO` is not implemented; the only current
  source hit is a comment describing the 12-byte information block projected by `NOTI 0x0009`, not a
  decoder or handler registration.

No production or test code changed as part of this remediation, and
`git diff --check 355aaf5cb6714044b4781185ec3118007bc97bec` remains clean. The remaining captured-frame live
oracle is correctly represented as a manual completion prerequisite, not as an unresolved defect in
the remediated review target.

## Current verdict — post-live evidence re-review

`changes_required`. This verdict supersedes the prior `approved` verdict below for the current
post-live target. No new production or test-code defect was found; the blocker is the mismatch between
the completion claim, the governing runtime-evidence requirements, and the evidence actually archived.

### F-002 — Completion and two S2C `ClientVerified` promotions exceed the archived live evidence

TaskContract SC-1 and SC-2 require current A21 inbound/outbound packet capture in addition to the UI
result (`TaskContract.md:60-61`). SC-9 separately requires the corresponding server send record, and
SC-10 requires the corresponding frame record (`TaskContract.md:68-69`). The new live record explicitly
states that `0100_NetworkHook` was not deployed, no real S2C body was captured, and the current
Information log does not print per-frame S2C output
(`protocol/patterns/data/traces/a21-town-party-detail-20260912.md:5-8,23-36`). Its concrete runtime rows
are C2S semantic/handler logs plus aggregate UI observations. The deterministic Release encoders,
delivery code, and 3,050 passing tests prove the intended implementation shape, but they are not a
runtime record that those individual S2C frames were sent, read, or consumed in the claimed order.

This leaves two protocol promotions specifically over-asserted even after their useful qualifiers:

- `NOTI 0x0008` says the inviter successfully consumed the current 7-byte acknowledgement
  (`NOTI-0x0008.md:56-70`), but the visible two-person roster is produced by the later `NOTI 0x0009`;
  the archived observation cannot distinguish successful consumption of `0x0008` from that frame being
  ignored or absent.
- `NOTI 0x000B` says both clients consumed the current endpoint projection
  (`NOTI-0x000B.md:63-88`), but the live oracle did not exercise dungeon P2P and contains neither a
  per-frame send record nor a client read/body trace. Stable town-party UI does not by itself demonstrate
  consumption of the endpoint payload.

The directly visible promotions (invitation UI, roster/clear UI, initial HP, area visibility, and detail
window), together with the accepted C2S operations, retain their documented narrow evidence boundaries;
this finding does not dispute those observations. It also does not dispute the automated result:
independent parsing still finds 10 final TRXs and 3,050/3,050 `UnitTestResult` rows Passed, and the current
`git diff --check 355aaf5cb6714044b4781185ec3118007bc97bec` is clean.

Minimum resolution is either to repeat the current A21 oracle with an archived C2S/S2C frame capture
and enough selector/send or client-read evidence to identify the NOTI frames, or to keep the unsupported
individual protocols at `Implemented` and keep TASK-08/root completion open. In either case,
`completion-evidence.md` must not claim SC-1..SC-10 all pass, and `spec-result.md` / `sdd-execution.yaml`
must not remain completed, until the TaskContract's required runtime evidence exists (or the governing
contract is explicitly revised and re-approved rather than weakened retrospectively by a result file).

## Current verdict — re-review after F-001 remediation

`approved`. This verdict supersedes the prior `changes_required` verdict below for the latest target.

The only prior blocking finding, F-001, is resolved. `protocol/game/index.md:886` now states `NOTI 0x0009` refresh length `65`, agreeing with the dedicated record (`NOTI-0x0009.md:93`), `PartyRosterEncoder`'s `new byte[65]` (`PartyNotificationSlice.cs:204`), and the vector assertion (`PartyNotificationTests.cs:53`). `git diff --check 355aaf5cb6714044b4781185ec3118007bc97bec` remains clean.

Since the prior review, no business or test code changed. The new target adds only the one-line index correction and TASK-08 automated-readiness/live-oracle documentation. The readiness arithmetic remains reproducible: all 10 `TestResults/task08-final` TRXs contain 3,050 `UnitTestResult` entries and all are `Passed`; the separate diagnostic TRX contains the stated seven isolated test rows and all seven pass. The live oracle preserves the required UI path and explicitly says initial HP must appear without a manual `0x00A6`; it does not promote any pending protocol to `ClientVerified` or mark TASK-08 complete.

The two non-blocking documentation clarifications observed during this re-review are resolved in the latest target:

- `TASK-08-final-validation.md:19` now accurately says `TestResults/task08-diagnostics` contains the successful 7/7 isolated rerun and that the initially failing same-name final TRX was overwritten, rather than claiming the original failure artifact is retained.
- The ordinary-member leave oracle now requires the departing member to become partyless while the leader retains a one-member party and receives a refresh, matching SC-3/AC-009 without accepting a full-party clear.

These wording-only corrections introduce no new implementation or evidence defect. The latest target therefore remains `approved`.

Current A21 two-client validation remains a downstream completion prerequisite for AC-009/AC-012/AC-013, not an unresolved code-review defect. TASK-08 and the SDD controller correctly remain `in_progress`, and the affected protocol pages correctly remain `Implemented` / pending live validation.

## Target and scope

- Review target: the current `town-party-lifecycle` worktree, compared with baseline commit `355aaf5cb6714044b4781185ec3118007bc97bec`.
- Governing sources: `TaskContract.md` revision 3, `spec.md` revision 3, `plan.md`, and `TASK-08-final-validation.md`.
- Applied criteria: complete code review plus deliverable review, including their code/document consistency boundary.
- Per controller direction, the already accepted TASK-01..TASK-06 results are retained by reference rather than re-reviewed from scratch. Fresh deep inspection concentrates on the changes since TASK-06 Round 6: roster-after-HP ordering, GET_USERINFO mode 3, their production registrations/tests/protocol records, and the lifecycle interfaces they touch. The full changed-path inventory, current build, current diff hygiene, and final TRX set were also checked for regressions.

## Prior verdict — superseded by the current re-review

`changes_required`. The new HP ordering and character-detail code paths have no demonstrated blocking defect, but the mixed deliverable still contains one authoritative protocol-index contradiction. RQ-012 / SC-6 therefore do not pass on this target.

## Prior blocking finding — resolved in the current target

### F-001 — `NOTI 0x0009` refresh length in the protocol index is the known-invalid 60-byte value

The prior target's `protocol/game/index.md:886` stated `refresh 60`. The implementation allocated a 65-byte body in `PartyRosterEncoder` (`PartyNotificationSlice.cs:204`), its byte-vector test asserted 65 (`PartyNotificationTests.cs:53`), and the dedicated protocol record stated `type=0` refresh is 65 bytes (`NOTI-0x0009.md:93`). That record also documents that 60 bytes was the earlier malformed shape which made both A21 clients read past the body and exit (`NOTI-0x0009.md:96-100`).

This is not a cosmetic discrepancy: `protocol/game/index.md` is a navigation/summary authority required by RQ-012, SC-6 and AC-008. Leaving the known crash-causing length there can misdirect later protocol work and makes the code, dedicated record, and index disagree.

Minimum sufficient correction was to change the `0x0009` row's refresh length from `60` to `65`, then recheck that the index, dedicated protocol page, encoder, and vector test all state the same shape. The current re-review above confirms that correction.

## Decisive inspected evidence

- Formation delivery is phased as `0x0099 -> 0x0008(inviter only) -> 0x000B -> 0x0009 -> 0x0099`; the final realtime publication uses the same immutable snapshot and the same all-member audience after the roster (`PartyNotificationSlice.cs:451-479`). `Formation_is_published_in_cross_recipient_phases` verifies both recipients, order, and snapshot identity (`PartyNotificationTests.cs:78-109`). The explicit `0x00A6` path remains requester-only and is not a prerequisite for the final formation HP publication.
- GET_USERINFO decoding keeps the existing `target=0xFFFF, mode=2` branch and separately emits the typed non-sentinel `mode=3` request (`GetUserInfoPayloadCodec.cs:30-50`). The protocol handler acquires requester/target gates in stable account order and revalidates both exact sessions before Command dispatch (`OtherCharacterDetailSlice.cs:99-198`). The Application query requires a current requester, a unique current same-listener target, and the target's exact runtime (`OtherCharacterDetailQueryDomainService.cs:13-53`); the Notification projection revalidates both generations immediately before response composition (`OtherCharacterDetailSlice.cs:43-68`). Production selected-session admission already rejects cross-account character/wire-id collisions, so the production `FindByWireUserId` path is not ambiguous.
- The subtype-3 response has a 165-byte empty minimum and variable equipment/two-skill-page/guild/quest sections. Encoder offsets and the corrected three-byte skill-entry accounting agree with the dedicated protocol record and current byte-vector tests.
- Fresh current-tree command: `dotnet build Server/86JP.Server.slnx --configuration Release --no-restore` succeeded with 0 warnings and 0 errors.
- Independent TRX parsing of all 10 files under `TestResults/task08-final` found 3,050 `UnitTestResult` elements, all `Passed`; every counter reports zero failed, error, timeout, aborted, inconclusive, not-runnable, not-executed, disconnected, warning, in-progress, and pending results. This includes separate Infrastructure runs with the PVF archive present (301 passed) and absent (2 passed). The only target edit after those TRXs is the test-comment correction from “4-byte” to “3-byte” skill entries; the current-tree build above covers that non-semantic delta.
- `git diff --check 355aaf5cb6714044b4781185ec3118007bc97bec` is clean.

## Downstream completion prerequisite

TASK-08 and the SDD execution state correctly remain `in_progress`. Current A21 two-client proof for initial HP, the mode-3 detail window, and active leave is still required by AC-009/AC-012/AC-013 before root completion or any `ClientVerified` promotion. Its absence is not recast here as a code-review defect: the affected protocol pages correctly retain `Implemented` / pending-live-validation status, and completion verification owns that evidence gate.
