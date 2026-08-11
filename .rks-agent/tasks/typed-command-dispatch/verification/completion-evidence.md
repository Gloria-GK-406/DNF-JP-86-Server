# Completion Evidence: Typed Command Dispatch

Verified on .NET 10.0.302 / linux-x64 / Release, warnings-as-errors, **0 warnings**.

**204 passed, 1 skipped, 0 failed** (baseline `7031862`: 203 passed, 1 skipped).
The one skip is the PVF missing-archive case, unrelated to this task.

| Suite | Baseline | Now |
|---|---|---|
| `Dfo.Server.Application.Tests` | 54 | 55 |
| `Dfo.Server.Networking.Tests` | 29 | 29 |
| `Dfo.Pvf.Tests` | 60 | 60 |
| `Dfo.Server.Infrastructure.Tests` | 60 (+1 skip) | 60 (+1 skip) |

Application went 54 → 55: two tests deleted, one rewritten, three added. Every change is
justified in `../spec-result.md`.

## Success Conditions

| ID | Verdict | Evidence |
|---|---|---|
| SC-1 | met | `grep -rn ApplicationCommandType Server/ --include=*.cs` returns nothing. All eight commands are now bare `sealed record X : ICommand<R>;`. |
| SC-2 | met | Three compile probes, one per mismatch kind, each producing `CS0311`; see `../spec-result.md` for the exact messages. Independently reproduced by the reviewer. |
| SC-3 | met | `grep` for `ValueTask<object` and `is TResult` under `Commands/` returns nothing. The reviewer confirmed the word `object` does not appear in that folder at all. `CommandBus` ends in a bare `return await next()`. |
| SC-4 | met | No `CanHandle` and no CLR-mismatch throw on the command side. The surviving `CanHandle` is `IClientMessageHandler`'s, on the message side, explicitly out of scope — its presence confirms the change did not leak. |
| SC-5 | met | `SendAsync_RejectsACommandThatHasNoRegisteredHandler` asserts the failure names the command. |
| SC-6 | met | See the per-requirement table below. The reviewer additionally proved by mutation that dropping either concurrency behaviour's registration makes tests fail. |
| SC-7 | met | 203 → 204 passing, 0 failures, 0 warnings; every assertion change explained in `../spec-result.md`. |

## Observable Requirements

| ID | Verdict | Evidence |
|---|---|---|
| RQ-001 | met | Compile probes (AC-001). Not expressible as a runtime test by construction. |
| RQ-002 | met | No `object` in the dispatch path; `SendAsync` returns the handler's value directly. |
| RQ-003 | met | Repo-wide grep empty. |
| RQ-004 | met | `CanHandle`, the CLR-mismatch throw and the result-type recovery are all gone. |
| RQ-005 | met | `SendAsync_RejectsACommandThatHasNoRegisteredHandler`. |
| RQ-006 | met | `Registry_RejectsDuplicateCommandTypeRegistrations`. |
| RQ-007 | met | `Pipeline_ExecutesBehaviorsInOrderAroundTheTerminalHandler` plus `EveryProductionBehaviourResolvesForACommandResultType`, which asserts the exact four closed behaviour types in `Order` sequence. Mutation-verified to fail when a registration is removed. `Order` values are unchanged from the baseline. |
| RQ-008 | met, with a recorded gap | Online shared-throw, online exclusive-cancel and account exclusive-throw all pass, as do both "requires the interface" tests. The **success-path** release limb is not exercised for either gate — a gap that holds identically at the baseline and is recorded rather than claimed as covered. |
| RQ-009 | met | `SendAsync_RejectsNestedCommandDispatch`; the `AsyncLocal` guard and its `finally` are byte-identical to the baseline. |
| RQ-010 | met | `SendAsync_ExecutesOneHandlerAndReturnsItsTypedResultInANewScope` asserts distinct scope identities across two dispatches. |
| RQ-011 | met | `LoggingBehavior_RecordsSuccessfulAndFailedExecutions`. The reviewer additionally confirmed the logger category is still the plain non-generic name, so generic behaviours do not disturb category-based log configuration. |

## Behaviour preservation, checked line by line

The reviewer compared each behaviour against `git show HEAD:<path>` and found them
identical apart from the signature and the command-identity rendering. Specifically
unchanged: the `await using var lease = await gate.AcquireAsync(...)` / `return await
next()` shape in both gates, the `Order` values 0 / 50 / 55 / 100, the nested-dispatch
`AsyncLocal` depth guard and its `finally`, the single `CreateAsyncScope()` per dispatch,
and the transactional rejection. The invoker is resolved before the pipeline runs, exactly
as the handler was, so handler-construction failures still occur outside the gates.

## Correction made during review

The reviewer found, and demonstrated by running, that `ICommand<out TResult>` was
covariant: a command could be widened to `ICommand<object>` and dispatched, producing an
`InvalidCastException` naming internal adapter types — worse diagnostics than the check
this task removed. Removing the variance closes it at compile time:

```
error CS0029: Cannot implicitly convert type 'SelectCharacterCommand' to 'ICommand<object>'
```

No call site relied on covariance and the full suite passes unchanged. This was preferred
over adding a runtime guard, which would have partly undone the task's own goal.

## Not covered

- RQ-008's success-path lease release, for both gates. Pre-existing.
- End-to-end dispatch tests for the two account-gated production commands. Pre-existing.
- The message-dispatch side, which keeps its enum and `CanHandle` by design: its
  identifier arrives off the wire, so the dynamic lookup is inherent there.
