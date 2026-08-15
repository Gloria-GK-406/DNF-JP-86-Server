---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: SelectCharacter Acceptance and Reusable State Synchronization

## Why and Outcome

Character selection currently appears as one response that owns and expands the complete entry-state transcript, even though the first command message acknowledges entry and the following notifications synchronize domain state that can remain meaningful throughout the game session. This couples unrelated inventory, character, account, quest, skill, pet, mailbox, collection and world semantics to SelectCharacter and makes raw wire identity choose encoding behavior.

After this change, an accepted selection is observably one entry acceptance followed by reusable domain state synchronization. The client receives the same compatible transcript, but each synchronization result and notification has a cohesive owner and can be reused by other lifecycle triggers without depending on a SelectCharacter-wide result or response.

## Task Perspective

- Core outcome: SelectCharacter accepts entry and coordinates reusable domain state synchronization without owning all synchronized state or encoding it as one multi-frame response.
- Necessary support: The existing wire transcript, optionality, failure behavior, generation/session safety and full-versus-incremental synchronization distinctions remain correct.
- Peripheral assurance: Architecture guidance, dependency inspection, complete transcript comparison and fresh build/test evidence prove the new boundary and prevent the removed restriction or semantic leak from returning.

## Scope and Non-Goals

### In Scope

- Accepted and rejected SelectCharacter behavior and its entry-state synchronization transcript.
- Domain-cohesive state acquisition results usable by SelectCharacter and other session workflows.
- Independently identified outbound state notifications and their encoding behavior.
- Handler-driven sequential command coordination and the command-handler nested-dispatch prohibition.
- Existing static notification conditions, repetitions, full snapshots and evidenced delta messages.
- Entry lifecycle safety, compensation and documentation affected by the new ownership model.

### Non-Goals

- Changing client wire identities, confirmed field layouts or compatibility order.
- Proving that all post-acceptance notifications are freely reorderable or safe to send concurrently.
- Migrating every future gameplay mutation that could reuse the extracted synchronization capabilities.
- Adding persistence behavior or changing authoritative gameplay data.
- Treating pending client reverse-engineering results as confirmed evidence without a separate evidence update.

## Constraints

- The accepted-entry message remains the first successful outbound message of an accepted selection.
- Required synchronization state is validated before the server emits a plausible accepted transcript; inapplicable optional state may be omitted only under its established condition.
- Domain-facing state and behavior remain independent of command/type headers, encoded frames, sockets and recipients.
- Protocol handlers may coordinate multiple sequential commands; a command handler may not recursively or indirectly dispatch another command through the command bus.
- Transport remains unaware of individual domain protocols.

## Definitions

| Term | Meaning |
|---|---|
| Entry acceptance | The `CMD 01/0004` server message that confirms a selected character may enter the game session. |
| State synchronization notification | A server `NOTI 00/xxxx` message that supplies a full or partial current state for one cohesive domain and remains meaningful outside the SelectCharacter request itself. |
| Compatibility transcript | The reviewed ordered sequence of outbound command/type identities, repeated occurrences, conditional omissions and encoded bodies currently produced for an accepted selection. |
| Full snapshot | A synchronization message that supplies the complete applicable state for its represented scope, such as inventory `00/000D`. |
| Incremental update | A synchronization message that applies bounded changes to already synchronized state, such as inventory `00/000E`. |

## External Behavior and Data Rules

- Accepted entry emits `01/0004` first and then the current compatible state synchronization transcript on the same connection.
- A required state may be loaded-empty and still produce its defined empty notification; missing required state is an error rather than a silent omission.
- Class-, feature- or content-specific state produces a notification only when its established applicability condition is satisfied.
- Repeated wire identities remain distinct message instances with their established semantic input and multiplicity.
- Inventory entry synchronization uses the full snapshot identity `00/000D`; runtime bounded inventory changes retain the incremental identity `00/000E`.
- A failed required send or lifecycle operation retains the current failure propagation, exact-session cleanup and connection disposition behavior.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | An accepted SelectCharacter flow emits one entry-acceptance message first, followed by a state synchronization transcript with the same wire identities, order, multiplicity, conditions and encoded values as the reviewed baseline. |
| RQ-002 | Entry acceptance is independently representable and encodable; it is not a container whose encoding expands into every state synchronization notification. |
| RQ-003 | Each synchronized domain exposes cohesive state results that contain no SelectCharacter-only, wire-header, frame, socket or recipient semantics and are usable by a non-SelectCharacter trigger. |
| RQ-004 | Outbound state synchronization is selected by concrete semantic message identity and input rather than by a SelectCharacter-wide raw type/occurrence dispatch. |
| RQ-005 | The SelectCharacter protocol workflow may coordinate multiple sequential domain commands and sends, while command handlers remain unable to perform nested command-bus dispatch. |
| RQ-006 | Required, loaded-empty and inapplicable optional state remain distinguishable and produce failure, empty notification or omission respectively. |
| RQ-007 | Authentication, connection generation, selected-session serialization, preparation, publication, compensation cleanup and connection-close behavior remain equivalent for accepted, rejected, stale and failed flows. |
| RQ-008 | Full snapshot and incremental update capabilities remain distinct and reusable where the protocol evidence establishes both forms, including inventory `00/000D` and `00/000E`. |
| RQ-009 | Repository guidance and maintained protocol documentation describe the acceptance-plus-state-synchronization model and permit handler multi-command coordination while prohibiting command-handler nested dispatch. |
| RQ-010 | Project dependencies continue to keep domain state free of protocol and transport knowledge and transport free of individual protocol knowledge. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-002, RQ-004 | A fully populated accepted selection result including applicable class and pet state | The entry transcript is emitted | `01/0004` is first and every baseline static notification follows with matching identity, order, multiplicity and encoded body. |
| AC-002 | RQ-001, RQ-006 | Required collections are loaded-empty and conditional class or pet state is inapplicable | The entry transcript is emitted | Required empty notifications remain present, only inapplicable notifications are absent and neighboring order is unchanged. |
| AC-003 | RQ-003, RQ-004 | One cohesive domain synchronization result is available independently of SelectCharacter | A protocol consumer projects that result | The appropriate concrete notification is encoded without a SelectCharacter-wide result, raw-type switch or occurrence lookup. |
| AC-004 | RQ-005, RQ-009 | An inbound protocol workflow needs more than one domain command | Its handler coordinates the workflow | Sequential command dispatch is permitted and observable, while an attempted command-handler nested dispatch remains rejected or absent by construction. |
| AC-005 | RQ-006 | Required synchronization input is unavailable or invalid before acceptance | The flow is prepared | The server fails visibly and does not silently omit the required notification or emit a fabricated accepted transcript. |
| AC-006 | RQ-007 | The connection is stale, admission is rejected, a required send fails or a later lifecycle step fails | SelectCharacter executes | Existing refusal, propagation, exact-session cleanup and connection disposition remain intact. |
| AC-007 | RQ-008 | Inventory state is synchronized at entry and later changed during an active session | The corresponding protocol outputs are produced | Entry uses the complete `00/000D` snapshot shape and the bounded runtime change uses the `00/000E` incremental shape. |
| AC-008 | RQ-009, RQ-010 | The completed source and documentation are inspected | Ownership and dependency rules are evaluated | Domain code contains no wire/transport semantics, transport contains no domain protocol dispatch, and guidance states the corrected handler/command rule. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002, RQ-004, RQ-006 | AC-001, AC-002, AC-005 | Complete baseline-versus-result transcript comparison plus focused acceptance, repetition, optionality and body vectors. |
| EV-002 | RQ-003, RQ-004, RQ-010 | AC-003, AC-008 | Independent domain-result projection tests and dependency/source-boundary inspection. |
| EV-003 | RQ-005, RQ-009 | AC-004, AC-008 | Handler workflow tests, command-bus nested-dispatch protection and architecture-document review. |
| EV-004 | RQ-007 | AC-006 | Lifecycle and integration behavior covering stale, refusal, send-failure, compensation and close paths. |
| EV-005 | RQ-008 | AC-007 | Full/incremental inventory protocol vectors and reuse-focused behavior tests. |
| EV-006 | RQ-001, RQ-003, RQ-005, RQ-007, RQ-009, RQ-010 | AC-001, AC-003, AC-004, AC-006, AC-008 | Fresh affected-solution build, relevant test suites and complete-result code review. |

## Assumptions

- `01/0004` is the accepted-entry message and the existing static notifications are valid state synchronization messages; pending client analysis may refine local dependencies without changing the behavior-preserving ownership outcome.
- The current compatibility order remains authoritative until stronger reviewed client evidence supports a wire change.
- Some synchronization messages have unresolved business names; the refactor preserves honest compatibility naming rather than promoting inferred meanings to confirmed domain facts.
