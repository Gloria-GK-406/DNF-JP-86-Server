---
kind: rks-spec-result
state: completed
---

# Spec Result: Typed Command Dispatch

The command/result/handler triple is now bound by generic constraints at the registration
site, the dispatch path carries the static result type end to end, and the enum and the
runtime checks that existed only to bridge the old dynamic join are gone. Observable
execution semantics are unchanged.

**204 tests pass, 1 skipped, 0 warnings** under warnings-as-errors.

## What changed

`ApplicationCommandType` is deleted. Registration is now
`AddCommandHandler<TCommand, TResult, THandler>(...)` with `TCommand : ICommand<TResult>`
and `THandler : ICommandHandler<TCommand, TResult>`, so the three cannot disagree. The
registry keys on the command type.

The pipeline carries `TResult` rather than `object`: behaviours are generic and register as
open generics, and the terminal handler is reached through a `CommandInvoker<TCommand,
TResult>` that closes over the command type at registration — necessary because
`SendAsync<TResult>` knows only the result type at the call site.

Deleted: `ICommandHandler.CanHandle`, the CLR-mismatch throw in the handler base, the
`is TResult` reconciliation in `CommandBus`, and the registry's "every enum value must have
a handler" check.

## SC-2 evidence: three mismatch kinds, three compile errors

Each probe registers one deliberately wrong triple. All three fail to compile with
`CS0311`; the control build without them succeeds.

| Probe | Error |
|---|---|
| handler that does not handle this command | `CS0311: 'AcknowledgeGameExitCommandHandler' … no implicit reference conversion … to 'ICommandHandler<SelectCharacterCommand, SelectCharacterResult>'` |
| command that does not declare this result | `CS0311: 'SelectCharacterCommand' … no implicit reference conversion … to 'ICommand<GetChannelInfoResult>'` |
| handler that produces a different result | `CS0311: 'SelectCharacterCommandHandler' … no implicit reference conversion … to 'ICommandHandler<AcknowledgeGameExitCommand, AcknowledgeGameExitResult>'` |

Independently reproduced by the reviewer.

## SC-7 evidence: every assertion change, and why

Two tests were deleted and one rewritten. Each asserted behaviour this task removes by
design; none is still required and left unguarded.

| Test | Change | Why |
|---|---|---|
| `SendAsync_RejectsACommandWhoseClrTypeDoesNotMatchTheRegisteredHandler` | rewritten as `SendAsync_RejectsACommandThatHasNoRegisteredHandler` | It asserted that a handler paired with a command it cannot handle is rejected at dispatch. That pairing is now unrepresentable — the first compile probe above shows registration rejects it. The rewrite covers the failure that remains: a command nobody registered (RQ-005). |
| `Registry_RejectsMissingCommandTypeRegistrations` | deleted | It asserted the "every enum value must have a handler" check. TaskContract Scope and spec External Behavior both remove that check deliberately: unlike a client message, which arrives off the wire and must always find a handler, a command exists only because code constructed it. The accepted consequence is that a forgotten registration now surfaces at first dispatch instead of at startup, named by RQ-005. |
| `SendAsync_RejectsAPipelineResultOfTheWrongType` | deleted | It asserted that a behaviour short-circuiting with a wrong-typed value is rejected. `ICommandPipelineBehavior<TResult>.ExecuteAsync` returns `ValueTask<TResult>`, so such a value cannot be produced; the test cannot be re-expressed. |
| `BuildProvider` back-fill loop | removed | It existed only to satisfy the deleted completeness check. |

Three tests were added, a net coverage increase:

- `AccountScopedRegistration_RequiresAnAccountCommand` — AC-008 for the account gate, which
  had **no** coverage at all before this task, despite guarding the account-exclusive
  access that `SelectCharacterCommand` relies on.
- `ExclusiveAccountLease_IsReleasedWhenHandlerThrows` — the account gate's release limb.
- `EveryProductionBehaviourResolvesForACommandResultType` — asserts the exact four closed
  behaviour types in `Order` sequence.

## The risk this task was routed to protect against, and how it was closed

The routing decision named one failure mode: behaviours moved to open-generic registration,
and one silently failing to resolve would leave the chain quietly shorter while tests still
passed. Two of the four guard concurrency invariants, so the symptom would be rare data
races rather than a red test.

`EveryProductionBehaviourResolvesForACommandResultType` addresses it directly, and the
reviewer confirmed by mutation that deleting either concurrency behaviour's registration
makes tests fail. Presence is therefore proven twice over: by resolution, and by the
per-behaviour tests that prove each one executes.

## Correction found during review

`ICommand<out TResult>` was covariant, so a command could be widened to `ICommand<object>`
and dispatched, yielding an `InvalidCastException` naming internal adapter types — worse
diagnostics than the check this task removed. The reviewer demonstrated it by running it.

Removing the variance closes it at compile time (`CS0029`) rather than detecting it at
runtime, which would have partly undone the task's goal. No call site relied on covariance.

Two code comments claimed guarantees stronger than the code provided and were corrected:
the invoker's cast is protected by exact-type keying, not by commands being sealed.

## Recorded gaps, not introduced here

- RQ-008's **success-path** lease release is unexercised for both gates; the tests prove
  release after failure only. Holds identically at the baseline.
- The two account-gated production commands have no end-to-end dispatch test.

## Deliberately unchanged

The message-dispatch side keeps its enum and its `CanHandle`. Its identifier arrives off
the wire as a number, so the dynamic lookup is inherent; the completeness check is a real
defence there because any known message number must find a handler. The remaining
`CanHandle` in the tree is `IClientMessageHandler`'s, which is how the change is confirmed
not to have leaked past its boundary.
