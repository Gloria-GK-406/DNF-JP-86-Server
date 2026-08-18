---
kind: rks-verification
decision: pass
---

# Completion Evidence

## Claim under evaluation

The taxonomy refactor described by `TaskContract.md` revision 1 is complete: every Abstractions
service interface states its category in its name, `UserService`'s three responsibilities are
separate implementation types whose cross-group updates remain atomic, and no observable server
behavior changed.

All evidence below was gathered after review remediation, against the current worktree state. The
build was cleaned first so the test run cannot be an incremental false green.

## Condition to evidence

### SC-1 — every service interface states its category

Interfaces counted from scratch under `Server/src/Dfo.Server.Abstractions`:

```text
total public interfaces: 67
  DomainService  11
  RuleService     2
  Projection      8
  bare Service    1   IServerConfigService
  other suffixes 45
categorised former *Service: 21
```

21 categorised plus the single recorded exception is the 22 the contract names. The exception and
its reason are stated in `AGENTS.md` under "接口后缀标明类别", together with the assignment rule and
the convention's scope.

Reviewer 3 independently checked the classification against each implementation's constructor
dependencies rather than trusting the suffix, and found no misclassification in either direction:
no rule service depends on a repository, no domain service is dependency-free, and all eight
projections declare no constructor at all.

**Met.**

### SC-2 — no behavior changed

```text
dotnet clean  →  dotnet build Server/86JP.Server.slnx
    已成功生成。  0 个警告

dotnet test --no-build
    Dfo.Pvf.Tests                 51 passed,  9 skipped
    Dfo.Server.Application.Tests 275 passed, 10 skipped
    Dfo.Server.Protocol.Tests    156 passed,  0 skipped
    Dfo.Server.Networking.Tests   46 passed,  0 skipped
    Dfo.Server.Infrastructure.Tests 87 passed, 51 skipped
    Dfo.Server.Host.Tests          4 passed,  0 skipped
    ----------------------------------------------------
    619 passed, 70 skipped, 0 failed
```

619 / 70 is exactly the baseline figure recorded in the contract's Current Situation.

Test-source difference: 28 test files compared, three of them detected as renames. After collapsing
every renamed identifier on both sides — including the prefixed test types such as
`StubEntryPlacementService` / `StubEntryPlacementRuleService` — **0 files still differ**. One test
file is added, the fixture described below.

**Met.**

#### Flaky test, isolated against the baseline

`ChannelHostStartupTests.InvalidRequiredChannelScript_FailsBeforeListenersStart(kind: "unreadable")`
failed on one fresh run. It was not accepted as known-flaky on assertion. The same assembly was run
five times on the untouched baseline `f894034` in the source checkout, and five times in the changed
worktree:

```text
baseline f894034 :  fail, pass, pass, pass, pass
this change      :  pass, pass, pass, pass, pass
```

The baseline — which contains none of this refactor — reproduces the failure. The flakiness is
pre-existing and unrelated to the change.

### SC-3 — cross-group updates remain atomic

Lock scope read off the current `OnlineStateStore.cs`, per public method that touches the shared
collections:

| Method | Collections touched | `lock (sync)` regions |
|---|---|---:|
| `RegisterConnection` | byConnection, byId | 1 |
| `TryDisconnect` | activeByAccount, byConnection, byId | 1 |
| `Publish(AccountId, …)` | activeByAccount, byId | 1 |
| `Publish(SelectedSessionIdentity, …)` | activeByAccount, byId | 1 |
| `ClearExact` | activeByAccount, byId | 1 |
| `TryDisconnectConnection` | activeByAccount, byConnection, byId | 1 |
| `GetRequiredCharacter` | activeByAccount | 1 |
| `TryGetByConnection` | byConnection, byId | 1 |

Every multi-collection operation performs its whole read-then-write inside one acquisition. The
store contains no `async` or `await`, so no continuation can run while the lock is held.

Single shared instance: `ApplicationModule.cs` has exactly one `TryAddSingleton<OnlineStateStore>()`
and three `GetRequiredService<OnlineStateStore>()` resolutions; `Server/src` contains zero
`new OnlineStateStore`. The online-user and select-character suites pass.

**Met.**

### SC-4 — no stale references, no new warnings

`TreatWarningsAsErrors=true` in `Server/Directory.Build.props`, and the clean build reports
`0 个警告`, so the build is evidence of zero warnings rather than an assumption.

A repository-wide search for all 21 former identifiers returns four occurrences, all in historical
documents that AC-003 permits:

- `Docs/领域结构重构设计.md` — the pain-point row, rewritten to past tense and dated as resolved.
- `Docs/Refactor/SelectCharacterG7Coverage.md` — a coverage matrix from a completed past migration.

Every live pointer — `AGENTS.md`, `Docs/DungeonEntryNotes.md`, `Docs/ProtocolSliceArchitectureNotes.md`,
four `protocol/game/client-to-server/*.md` and `protocol/patterns/world-entry.md` — was updated and
resolves.

**Met.**

### SC-5 — reviewed by subagents, findings fixed or dismissed with reason

Three independent reviewers in separate contexts, none of which produced the change; the record is
`reviews/change-review.md`. Findings and disposition:

| ID | Finding | Disposition |
|---|---|---|
| R2-1 / R1-1 | `LoginAsync` lock narrowed by one statement | Dismissed with reason: the returned value is an immutable local captured before the lock |
| R1-2 | Four interfaces resolve to three objects instead of one | Dismissed with reason: no code depends on that identity; the reviewer searched casts, `is`/`as`, reference equality and `lock (injectedService)` |
| R1-3 | Store is now container-activated | Dismissed with reason: implicit public parameterless constructor; covered by tests that build the real module graph |
| R1-4 | Five BOMs stripped as a side effect | **Fixed** — restored; verified all five files begin `EF BB BF` |
| R3-1 | RQ-006 half done: 22 test implementation types kept former names | **Fixed** — 95 identifiers across 11 files, three support files renamed |
| R3-2 | SC-5 not met at time of review; record incomplete | **Fixed** — remediation applied, record completed |
| R3-3 | `AGENTS.md` table omitted several suffixes | **Fixed** — rows added, scope stated |
| R3-4 | Five `I*Service` remain in `Dfo.Server.Application` | Dismissed with reason: outside the contract's scope, which is `Dfo.Server.Abstractions`; recorded in `AGENTS.md` and carried to the delivery report as a follow-up |
| self | Seven source files whose own name was a renamed type | **Fixed** — renamed, rename-detected by Git |

No unresolved blocking finding.

**Met.**
