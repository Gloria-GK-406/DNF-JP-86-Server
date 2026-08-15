---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Refactor SelectCharacter State Synchronization

## Context

### Source Request

The user asked: “好，这下好像selectchar部分就很清晰了。你看看能不能启动任务契约来重构了”, then corrected the governing handler rule: “这里的handler禁止连续dispatch多条命令是伪造的限制。我只限制了命令中不能递归调用命令。是另一个agent自己主张扩展意图了。正好也一起清掉”.

### Current Situation

`SelectCharacterResponse` currently expands one outbound object into an ordered stream of roughly 58 static frames. `SelectCharacterStaticSequence` stores raw command/type/occurrence descriptors and `SelectCharacterStaticPayloadCodec.EncodeNotification` uses the future header type to select a body encoder. The result is that inventory, skill, quest, account, pet, mailbox, collection, position and other lifecycle-wide state notifications are structurally owned by the SelectCharacter slice and read from one cross-domain `SelectCharacterResult`.

The reviewed server history and current migration evidence support a clearer model: `CMD 01/0004` is the select-character acceptance message, while the following `NOTI 00/xxxx` frames are domain state snapshots or updates that may also be sent during the active game session. Inventory provides a concrete full/delta example through `00/000D` and `00/000E`.

The repository guidance also contains an unintended restriction saying a protocol handler cannot dispatch a second command. The user has clarified that only recursive/nested command dispatch from inside a command handler is prohibited; a protocol handler may dispatch multiple commands to coordinate one inbound protocol workflow.

## Goal

Make SelectCharacter an explicit entry-acceptance workflow that sends `01/0004` first and then reuses domain-owned state synchronization commands/results and concrete protocol encoders, so lifecycle-wide state synchronization no longer belongs to a SelectCharacter-specific multi-frame response, central type switch, or occurrence lookup. Correct the repository guidance to allow sequential multi-command dispatch from protocol handlers while retaining the prohibition on recursive command dispatch inside command handlers.

## Scope

### In

- Separate the `CMD 01/0004` selection acceptance message and its encoding from the subsequent state synchronization notifications.
- Rehome static SelectCharacter rows 2–58 into cohesive domain synchronization capabilities and concrete outbound protocol messages/encoders, with required versus inapplicable state represented explicitly.
- Let the SelectCharacter protocol handler coordinate the required domain commands/queries and sends, including multiple sequential command dispatches where the workflow needs them.
- Make domain synchronization results reusable outside SelectCharacter without carrying wire command/type, frame, socket, recipient, or SelectCharacter-only semantics.
- Remove the static stream's dependency on a central raw-type switch and occurrence-based body selection; repeated wire identities remain concrete message instances with explicit semantic inputs.
- Preserve the existing entry lifecycle protections: authentication/generation checks, selected-session transition serialization, preparation, publication, failure cleanup and connection handling.
- Preserve and test current packet identities, body bytes, multiplicity, conditional omission and compatibility order unless stronger client evidence justifies a separately reviewed change.
- Update protocol documentation, architectural guidance and tests to describe the acceptance-plus-state-synchronization model and the corrected command-dispatch rule.
- Remove or narrow the SelectCharacter-specific Networking projection-delivery exception when the resulting ownership makes it unnecessary.

### Out

- Changing client wire command/type identities or deliberately changing confirmed body layouts.
- Reordering, parallelizing or omitting existing compatibility messages solely because the client appears order-insensitive.
- Implementing every future gameplay mutation handler that may reuse the extracted synchronization messages.
- Introducing new database semantics or changing persisted gameplay state unrelated to the refactor.
- Treating pending Windows client analysis as confirmed protocol evidence without recording and reviewing it.
- Merge, push, pull request creation, release or task-worktree cleanup.

## Constraints

- `01/0004` must be the first successful wire message for an accepted selection; all required state must be representable before a plausible accepted flow is emitted.
- Domain commands, queries, services and results must remain free of protocol header/type/frame and socket-delivery semantics. Protocol message construction and encoding remain in Protocol.
- Protocol handlers may dispatch multiple commands sequentially. Command handlers must not recursively or indirectly dispatch another command through the command bus; reusable behavior belongs in domain services.
- Required state absence must fail visibly. Only genuinely inapplicable optional synchronization messages may be omitted.
- Networking must remain transport-oriented and must not acquire knowledge of individual domain protocols.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | An accepted selection emits a single `01/0004` acceptance message first, followed by the same static notification identities, multiplicity, conditions and compatibility order as the current reviewed stream. | Complete-stream behavior tests comparing decoded frame identities/order and representative or golden body bytes before and after the refactor. |
| SC-2 | `SelectCharacterResponse` is no longer one message that encodes the complete static stream, and no SelectCharacter central `type switch` plus `occurrence` lookup selects all notification bodies. | Source inspection and focused encoder tests showing concrete acceptance and domain notification message types/encoders. |
| SC-3 | Static state synchronization is organized by cohesive domain capabilities/results and can be projected without depending on `SelectCharacterResult` or SelectCharacter-only protocol types. | Dependency inspection plus focused tests that construct and encode each extracted domain synchronization result independently of SelectCharacter. |
| SC-4 | SelectCharacter coordinates domain work through handler-driven command dispatch and sending, while command handlers contain no nested command-bus dispatch or raw sender/socket orchestration for the extracted static synchronization flow. | Handler/command tests and source inspection covering sequential dispatch, send order and the nested-dispatch prohibition. |
| SC-5 | Existing lifecycle safety and compensation behavior is preserved on success, stale generation, admission refusal, required send failure and later workflow failure. | Focused lifecycle/integration tests covering session identity, cleanup, connection close behavior and failure propagation. |
| SC-6 | Full versus incremental synchronization remains explicit where currently evidenced, including inventory `00/000D` full snapshot and `00/000E` delta update, with reusable concrete encoders. | Protocol tests for both identities and documentation mapping their intended synchronization roles without broadening unconfirmed semantics. |
| SC-7 | Repository architecture guidance states that handlers may dispatch multiple commands, while command handlers may not perform nested/recursive dispatch. | Review of the updated `AGENTS.md` command and handler rules, consistent with executable command-bus tests or existing nested-dispatch protection. |
| SC-8 | The affected solution builds cleanly and relevant Protocol, Application, Networking and integration tests pass without weakening warning or validation settings. | Fresh build/test results and diff hygiene checks from the isolated task result. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | “你看看能不能启动任务契约来重构了” plus “正好也一起清掉” | Create and execute the bounded SelectCharacter state-synchronization refactor, including correcting the fabricated handler-dispatch restriction, task artifacts, an isolated worktree and one verified local result commit. |

## Requires Confirmation

- Merge, push, pull request creation, release, cleanup of the task worktree, or any client-side/Windows mutation.

## Assumptions

- `CMD 01/0004` is the accepted-entry message and the following static notifications are lifecycle-usable state synchronization messages. Pending client analysis may refine local dependencies but does not block a behavior-preserving ownership refactor.
- The existing static notification order remains the compatibility baseline even where no semantic dependency has yet been confirmed.
- The current project dependency direction remains authoritative; the task changes semantic ownership and orchestration without reversing project references.
