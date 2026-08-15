---
kind: rks-task-contract
revision: 2
state: ready
---

# TaskContract: Strong Game CMD and NOTI Semantics

## Context

### Source Request

The user accepted the proposed strong-semantics design and instructed:
“好，看起来没问题。那就按你说的方案重构吧，直接在当前分支重构就行。开始任务契约吧。不过先拉取一下代码，再开始开发任务。”

### Current Situation

Protocol evidence at baseline `f29123b` identifies the Game frame's first byte as the selector for
two independent client registries: CMD (`0x01`) is the bidirectional request/response family and
NOTI (`0x00`) is the server-to-client notification family. The client sends only CMD. Current code
preserves the byte but calls it `Command` in Networking, exposes raw `0/1` choices in individual
encoders and state-synchronization messages, and offers only a protocol-neutral `SendAsync` at Game
protocol call sites. A wrong byte silently selects the wrong client registry. Source inventory also
shows that workflow position does not determine category: accepted entry, premium service and clone
title frames are CMD, while most state pushes are NOTI; the current admission-rejection message even
aggregates one CMD frame followed by one NOTI frame.

## Goal

Make CMD and NOTI explicit, statically constrained Game protocol semantics from message creation
through encoding and sending, while keeping Networking protocol-neutral and preserving every
existing Game frame byte, payload, recipient, order and runtime behavior.

## Scope

### In

- Represent outbound Game CMD and NOTI as distinct Protocol-owned message categories.
- Provide Game protocol sending operations whose call sites explicitly state CMD or NOTI and accept
  only a compatible message category, while continuing to delegate transport to the generic sender.
- Make Game encoder definitions and registrations explicitly CMD or NOTI so the frame-class byte is
  fixed by the category rather than freely selected in each encoder.
- Express all client-to-server Game decoder registrations as CMD and retain `(class, type)` as the
  routing identity.
- Replace Networking's Game-specific `Command` terminology with a protocol-neutral name for the
  opaque first frame byte; Networking must not define or interpret CMD/NOTI.
- Remove raw command-byte choice from state-synchronization messages and supplemental frame helpers,
  preserving each frame's existing CMD/NOTI category and separating any one high-level message that
  currently spans both categories without changing frame adjacency or delivery behavior.
- Update focused tests and maintained architecture/protocol implementation guidance affected by the
  stronger distinction.

### Out

- Changing any Game type number, payload layout, encryption, checksum, sequence, recipient, delivery
  order, failure behavior or handler/domain workflow.
- Changing Game Login/GameOption fields or implementing conclusions about the suspected timestamp.
- Reinterpreting Channel framing or adding CMD/NOTI concepts to Channel.
- Expanding or revising the reverse-engineering conclusions introduced by `f29123b`, except for
  implementation mapping needed to describe the completed server change.
- Push, pull request, release, history rewriting or unrelated workspace cleanup.

## Constraints

- For the same server messages and state, complete outbound Game frames must remain byte-identical:
  CMD remains `0x01` and NOTI remains `0x00`.
- The category that determines the wire byte must be intrinsic to the message/encoder contract;
  a send call may express and constrain the category but must not independently override it.
- CMD/NOTI types and named operations belong to `Dfo.Server.Protocol`. The generic sender, frame
  parser and frame encoder in Networking may carry only an opaque byte and cannot reference Protocol.
- The user explicitly requested development in the current branch; no new worktree is created.
- Untracked `Diagnostics/` content is unrelated user work and must remain untouched and uncommitted.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Every outbound Game message is classified as CMD or NOTI and can be sent only through a category-compatible Game protocol operation. | Type/registration inspection plus compile-time or architecture tests covering mismatched categories and representative call sites. |
| SC-2 | Game encoders cannot freely choose a raw `0/1`; their category fixes the correct first frame byte. | Encoder hierarchy/registration inspection and exact-frame tests for representative CMD and NOTI messages, including the shared type `0x0001`. |
| SC-3 | Client-to-server Game decoding is explicitly CMD and retains class-plus-type routing without introducing a client NOTI path. | Decoder registration and registry tests plus source inspection. |
| SC-4 | Networking remains protocol-neutral: it parses/encodes an opaque first byte and exposes no Game CMD/NOTI sender API or enum. | Project/dependency and symbol scans with focused review. |
| SC-5 | State synchronization no longer stores or passes raw command bytes; every existing frame retains its current category, and the mixed admission-rejection pair becomes two category-intrinsic messages without changing adjacency or order. | Complete state/supplemental category inventory plus SelectCharacter and rejection frame/order regressions. |
| SC-6 | The complete refactor preserves behavior and leaves GameOption and unrelated work unchanged. | Fresh build, focused protocol/network integration tests, full enabled test suite, exact byte regressions and diff hygiene. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | “那就按你说的方案重构吧，直接在当前分支重构就行。开始任务契约吧。不过先拉取一下代码，再开始开发任务。” | Pull the selected branch, create and execute the bounded CMD/NOTI semantic refactor directly on it, update affected tests/guidance, verify the result and create one local result commit. |

## Requires Confirmation

- Push, pull request creation, release, history rewriting, branch deletion or cleanup outside this
  task's own temporary artifacts.

## Assumptions

- The confirm-grade protocol evidence committed at `f29123b` is the semantic authority for CMD
  (`0x01`) and NOTI (`0x00`); current exact-frame baselines determine each implemented message's
  category where workflow-oriented names are misleading. This task does not require further client
  reverse engineering.
