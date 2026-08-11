---
kind: rks-change-review
contract:
  path: ../TaskContract.md
  revision: 1
verdict: approved
---

# Change Review: Typed Command Dispatch

Independent review by a fresh reviewer with no prior context on the work, against the
TaskContract and spec. The reviewer built and ran the suite on both the baseline and the
change, ran mutation checks, and produced its own compile probes.

## Verdict

**Approved**, with one required follow-up (write the result artifact) and two comments
that claimed stronger guarantees than the code provided.

## What the reviewer verified by running

| Check | Result |
|---|---|
| Release build | succeeded, 0 warnings under warnings-as-errors |
| Suite on baseline `7031862` | 203 passed, 1 skipped |
| Suite on the change | 204 passed, 1 skipped |
| Mutation: drop `OnlineUserExecutionBehavior<>` registration | 2 tests fail |
| Mutation: drop `AccountExecutionBehavior<>` registration | 2 tests fail |
| Compile probes: wrong handler / wrong command / wrong result | CS0311 each |
| Is the handler-reflection guard vacuous? | No — matches all 8 handlers |
| Logger category under a generic behaviour | unchanged (`CommandLoggingBehavior`), no log-config regression |

The mutation checks are the important ones: they answer the primary risk named in the
routing decision by proving the behaviour-set assertion actually fails when a behaviour
is dropped, rather than being inferred from tests that happen to need one.

## Findings

### F-1 — comment overstated why a cast is safe (fixed)

`CommandInvoker.InvokeAsync` justified its `(TCommand)command` cast by saying commands are
sealed. Sealing is not what makes it safe: the registry keys on the exact command type, so
a subclassed command would miss the lookup and fail there, never reaching the cast. The
comment now says that.

### F-2 — covariance let a widened command reach a mismatched invoker (fixed at compile time)

`ICommand<out TResult>` was covariant, so `ICommand<object> x = new SomeCommand()` was a
legal widening and `SendAsync(x)` threw `InvalidCastException` naming internal adapter
types — worse diagnostics than the check this refactor removed. The reviewer verified this
by running it.

Rather than reintroduce a runtime check, the variance was removed. The widening now fails
to compile:

```
error CS0029: Cannot implicitly convert type 'SelectCharacterCommand' to 'ICommand<object>'
```

Normal dispatch is unaffected, the full suite still passes, and no call site relied on
covariance. This closes the hole instead of detecting it, and leaves the bus with no
result-type check at all.

### F-3 — result artifact missing (addressed)

`spec-result.md` did not exist, so SC-2's compile-probe evidence and SC-7's required
explanation of assertion changes had nowhere to live. Both are now recorded in
`spec-result.md` and `verification/completion-evidence.md`.

### F-4 — pre-existing coverage gaps, not introduced here (recorded, not fixed)

- The lease-release tests prove release after a failure, not after a success. RQ-008 names
  the success limb; it remains unexercised for both gates.
- The two account-gated production commands have no end-to-end dispatch test.

Both hold identically at the baseline. Fixing them is outside this task's scope; they are
recorded so the gap is not mistaken for coverage this refactor provides.

### F-5 — cosmetic (accepted as-is)

A test helper parameter widened from `ICommandHandler` to `object` where it only calls
`GetType()`; several `Assert.IsType<T>` calls in handler tests are now statically
guaranteed and can no longer fail; one `using` sits out of alphabetical order in a block
that was already unsorted.

## Test changes, audited

| Test | Change | Reviewer verdict |
|---|---|---|
| `SendAsync_RejectsACommandWhoseClrTypeDoesNotMatchTheRegisteredHandler` | rewritten as `...ThatHasNoRegisteredHandler` | Legitimate. The pairing it asserted is now unrepresentable — the compile probe confirms registration rejects it. |
| `Registry_RejectsMissingCommandTypeRegistrations` | deleted | Legitimate. Contract Scope and spec both remove the completeness check by design. |
| `SendAsync_RejectsAPipelineResultOfTheWrongType` | deleted | Legitimate. A behaviour returning `ValueTask<TResult>` cannot produce a wrong-typed value, so the test cannot be re-expressed. |
| `BuildProvider` back-fill loop | removed | Necessary; it existed only to satisfy the deleted check. |
| three new tests | added | Net coverage increase. AC-008 was previously untested for the account gate. |

## Unmet at review time

SC-7's documentation limb and SC-2's recorded evidence. Both were the missing result
artifact, now written.
