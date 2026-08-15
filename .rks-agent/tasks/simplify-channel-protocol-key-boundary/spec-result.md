---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Channel Protocol Key Ownership

## Delivered

- `ChannelConnectHandler` now gets the legacy daily key from a Protocol-owned service and sends the
  existing response directly, with no command-bus dispatch.
- ScriptVersion, Script and Info encoders use the same singleton `ChannelProtocolKeyProvider`.
- The key-only `EstablishChannelConnectionCommand` family, its registration, the Application key
  implementation and the Abstractions key interface were removed.
- The exact local-time/invariant key formula, all encoder bodies, AES/zlib choices and Channel frame
  construction remain unchanged.
- The global rule prohibiting command handlers from injecting `ICommandBus` was preserved in a
  dedicated architecture test instead of being deleted with the obsolete command test.
- Maintained architecture guidance now distinguishes deterministic protocol interactions from
  domain behavior without loosening the handler business-semantics prohibition.

## Success Evidence

| Condition | Result |
|---|---|
| SC-1 | Direct handler and exact real-TCP Connect frame verified at a fixed local time. |
| SC-2 | ScriptVersion golden ciphertext and Script/Info AES+zlib payloads verified at the same fixed time. |
| SC-3 | Obsolete command and cross-layer provider symbols absent from source, tests and registration. |
| SC-4 | Build 0 warnings/errors; 522 passed, 65 existing archive-dependent skips, 0 failed; diff hygiene clean. |

## Repository Result

- Product changes are limited to Channel protocol ownership, corresponding Application/Abstractions
  removal, focused tests and `AGENTS.md`.
- Game Login/bootstrap, Networking framing/runtime and project references were not changed.
- The task is based on local baseline `9112a8d1c6ede4faf3d5dbcb518b93944903c0da` and is ready for
  the single authorized local result commit.
- No push, PR, branch deletion or unrelated cleanup was performed.

## Artifacts

- `TaskContract.md`
- `routing-decision.md`
- `spec.md`
- `reviews/change-review.md` (`approved`)
- `verification/completion-evidence.md` (`pass`)
