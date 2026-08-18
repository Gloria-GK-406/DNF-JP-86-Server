# Change Review

Complete-result review of the taxonomy refactor against `TaskContract.md` (revision 1) and
`spec.md`. Three independent reviewers, each in its own context, none of which produced the change.

- Worktree: `.worktrees/domain-rule-service-taxonomy`, branch `domain-rule-service-taxonomy`
- Baseline: `f894034`
- Reviewed state: 78 files changed (+302 / −539) plus three untracked source files

## Reviewer 2 — lock scope and atomicity

Scope: whether splitting `UserService` can produce an interleaving the original could not.

**Verdict: faithful. No new interleaving is possible.**

Evidence the reviewer produced:

- A `diff -u` of `f894034:…/UserService.cs` against `…/OnlineStateStore.cs` yields exactly three
  hunks: usings and the class header/doc comment, `LoginAsync` replaced by `Authenticate`, and the
  `GetRequired` → `GetRequiredCharacter` / `GetRequiredUser` renames. **Every other method body,
  including every `lock (sync)` region, is byte-identical to the baseline**, as are
  `GetRequiredAggregate`, the `ActiveCharacterRegistration` record and the nested `OnlineUser`
  aggregate.
- All six multi-dictionary operations — `TryDisconnect`, `TryDisconnectConnection`, both `Publish`
  overloads, `ClearExact`, `RegisterConnection` — still perform their whole read-then-write inside
  one acquisition.
- No operation was split into two acquisitions. No facade calls another facade; each facade member
  is a single pass-through to one store call, so no re-entrancy path exists. The store holds no
  injected dependency, so it cannot call out to user code under the lock.
- `OnlineStateStore.cs` contains no `async`/`await` at all, so no continuation can run while the
  lock is held.
- `AccountDomainService.LoginAsync` keeps the original shape: the repository `await` is outside any
  lock, the authenticate is inside it.
- Exactly one `OnlineStateStore` exists in production wiring — one `TryAddSingleton<OnlineStateStore>()`
  and three facades resolving that singleton. A repository-wide search found no second registration,
  no `new OnlineStateStore()` in `Server/src`, and no assembly scanning that could introduce one.
  `IOnlineUserDomainService` and `IOnlineUserQueryDomainService` both forward to the same concrete
  instance.

### Finding R2-1 — accepted, not a defect

`return account.AccountId` moved from inside the lock to after it. The reviewer judged this
harmless and gave the reason: `account` is a local captured before the lock is taken, so the return
reads no shared state. Recorded here because it is the one semantic difference in the moved code
and a later reader should not have to rediscover that it was noticed and assessed.

### Note carried forward, not a finding

Cross-facade composition by callers is unchanged: a consumer that previously called two methods on
the single `UserService` without an enclosing lock had no cross-call atomicity before and has none
now. The split neither introduced nor removed that.

## Reviewer 1 — behavior preservation

Scope: find anything in the diff that is not a pure rename or a pure move. Static review; the
reviewer did not build or run tests.

**Verdict: no hidden behavior change.** Of 146 changed hunks under `Server/`, 136 are pure
identifier substitution under the rename map. The remaining 10 are the intended split, the intended
deletion, and five incidental BOM strips.

Evidence the reviewer produced:

- Comparing the baseline `UserService.cs` from `RegisterConnection` to EOF against the new
  `OnlineStateStore.cs`, applying only the two documented method renames to the old side, **every
  line matches** — including the `checked(Generation + 1)` in `UnbindCharacter` against the bare
  `Generation++` elsewhere, the `Take(2)` cap, the `SingleOrDefault(...) ?? throw`, and both
  `KeyNotFoundException` message strings.
- Registration: all three services resolve the store through one `TryAddSingleton<OnlineStateStore>()`,
  so `IAccountDomainService` and `IActiveCharacterDomainService` provably share one store and one
  lock. All lifetimes remain singleton. `ApplicationModule.cs` is the only registration site for
  these interfaces repository-wide.
- The new test fixture creates one store and hands it to all three services, so the tests still
  exercise the shared-state path; every member is a single-expression forward.

### Finding R1-1 — same as R2-1, independently assessed

The `LoginAsync` lock narrowing. This reviewer added the reason the value is safe to read outside
the lock: `AccountRecord` is a record whose `AccountId` is a `readonly record struct`, so the
returned value is an immutable local that was never guarded by `sync`.

### Finding R1-2 — identity of the resolved instances changed

Four interfaces previously resolved to the *same* object; they now resolve to three distinct
objects sharing one store. The reviewer searched for code that could depend on that identity —
cross-interface casts, `is`/`as`, reference equality, and `lock (injectedService)` — and found none;
the only `lock (` sites in `Server/src` are on private fields. Accepted: no behavior risk.

### Finding R1-3 — `OnlineStateStore` is now container-activated

Previously the state was constructed by an explicit `new UserService(...)`; the store is now
activated by the container. It resolves because the class declares no constructor and therefore has
the implicit public parameterless constructor MS.DI requires, and `internal` accessibility does not
block activation. Exercised by `RegistryExistenceTests` and the networking integration suite, both
of which build the real module graph. Accepted.

### Finding R1-4 — five incidental BOM removals — REMEDIATE

Three source files and two protocol documents lost their UTF-8 BOM as a side effect of the rewrite:
`GetAccountCharacterRosterHandler.cs`, `CreateCharacterSlice.cs`, `DeleteCharacterSlice.cs`,
`protocol/game/client-to-server/0x0004.md`, `protocol/game/client-to-server/0x0006.md`.

The reviewer verified this carries no behavior risk — the three sources are now pure ASCII, and the
document generator reads and writes UTF-8 either way — but correctly classified it as **unrequested
churn**: 23 other files in the repository still carry BOMs, so this is inconsistent rather than a
cleanup, and this task permits renaming only. Restore the five BOMs.

### Observation — file names not renamed alongside their types

Carried into remediation as R3 below.

## Reviewer 3 — contract and spec conformance

Scope: adversarial audit of RQ-001..RQ-010 and SC-1..SC-5, including scope creep. This reviewer ran
the build and the suite.

**Verdict at time of review: the rename is correctly classified, but two requirements were not
satisfied.** It confirmed RQ-001/002/003 by checking each classification against the actual
constructor dependencies rather than trusting the suffix — no rule service leaks a repository, no
domain service is dependency-free, all eight projections declare no constructor at all. RQ-004,
RQ-005, RQ-007, RQ-008 met; build 0 warnings / 0 errors; 619 passed / 70 skipped / 0 failed.

### Finding R3-1 — RQ-006 only half done — FIXED

Production implementation types were renamed; **the test tree was missed entirely**. 22 test types
still derived their names from the former interface names — `StubEntryPlacementService` implementing
`IEntryPlacementRuleService`, `StubCharacterService`, four copies of `StubOnlineUserQueryService`,
and others. The spec's in-scope wording is unqualified, and a reader of `StubEntryPlacementService`
meets exactly the ambiguity this task set out to remove.

The reviewer also diagnosed why the first two reviewers missed it: Reviewer 1's framing counted the
stale stub names as *correct* because they had been consistently substituted.

Fixed: 95 identifiers across 11 test files, plus three support files renamed to match
(`StubEntryPlacementRuleService.cs`, `StubSessionAdjacentEntryDomainService.cs`,
`StubWorldContentEntryRuleService.cs`).

### Finding R3-2 — SC-5 not met at time of review — FIXED

R1-4 was marked REMEDIATE and had not been actioned, the review record documented only two of three
reviewers, and it was truncated mid-sentence. All five BOMs are restored, this section completes the
record, and the remediation is verified below.

The reviewer added a reason to restore the two `protocol/` BOMs that the first review did not have:
those documents are machine-regenerated and were deliberately byte-pinned by commit `ee5d6d1`
*"pin the knowledge base to LF so regeneration leaves no diff"*, so stripping their BOM risks a
spurious diff on the next regeneration.

### Finding R3-3 — AGENTS.md table incomplete — FIXED

The new section opens by asserting that an interface's suffix *is* its category, but the table
omitted `*Policy`, `*Catalog`, `*Query`, `*Reader`, `*Module`, `*Observer` and `*Closer`, so a
reader meeting `IChannelEntryPolicy` or `IEquippedCreatureQuery` found nothing. Rows added, and the
convention now states its own scope explicitly.

### Finding R3-4 — five `I*Service` remain in Application — ACCEPTED, NOT FIXED

`ISelectedSessionCleanupService`, `ISelectCharacterSessionEntryService`,
`ISelectCharacterPreparationService`, `ISelectCharacterQuestEntryService` and
`ISelectCharacterPetEntryService` read and write world state but keep the bare suffix. The reviewer
classified this as consistent rather than a breach: the TaskContract scopes the taxonomy to
`Dfo.Server.Abstractions`, and these are Application-internal.

Not fixed, because renaming them would expand the authorized scope. `AGENTS.md` now names all five
and states that the convention's scope is `Dfo.Server.Abstractions`, so the gap is recorded where a
reader will meet it rather than left implicit. Carried to the delivery report as a follow-up.

## Remediation

| ID | Action | Evidence |
|---|---|---|
| R1-4 | Restored five UTF-8 BOMs | All five files begin `EF BB BF` again |
| R3-1 | Renamed 22 test implementation types; three support files renamed to match | 95 identifiers across 11 files; build green |
| R3-3 | Completed the suffix table and stated the convention's scope | `AGENTS.md` |
| R3 (self) | Renamed seven source files whose own name was a renamed type | `git mv`, rename-detected |

Post-remediation verification:

- Build: succeeds. `TreatWarningsAsErrors=true` in `Server/Directory.Build.props`, so this is
  evidence of zero warnings rather than an assumption.
- Tests: 619 passed, 70 skipped, 0 failed — identical to the baseline figure.
- Test diff: 28 files compared (3 of them renamed); after collapsing every renamed identifier on
  both sides, **0 files still differ**. One added file, the test fixture.
- Former names: 4 occurrences remain, all in historical documents permitted by AC-003.

No unresolved blocking finding.
