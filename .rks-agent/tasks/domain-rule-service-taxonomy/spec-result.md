---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Explicit Domain, Rule and Projection Service Taxonomy

## Delivered

- Every service interface in `Dfo.Server.Abstractions` states its category in its own name: 11
  `I*DomainService`, 2 `I*RuleService`, 8 `I*Projection`. The 45 interfaces whose suffix already
  carried their category were left alone.
- `IServerConfigService` is the single deliberate exception, recorded with its reason.
- Implementation types follow their interfaces, in production and in tests, and the seven source
  files plus three test-support files whose own names were renamed types were renamed to match.
- `UserService` no longer exists. Its three responsibilities are `AccountDomainService`,
  `OnlineUserDomainService` and `ActiveCharacterDomainService`, over one `OnlineStateStore` that
  keeps the three collections and the single lock.
- `AGENTS.md` records the taxonomy: the categories, the assignment rule, the exception, and the
  convention's scope.
- No observable server behavior changed.

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `verification/completion-evidence.md` — 67 interfaces counted from scratch; 21 categorised, 1 exception; classification independently checked against constructor dependencies |
| SC-2 | Same — clean build then 619 passed / 70 skipped / 0 failed, identical to baseline; test diff collapses to 0 differing files after removing renamed identifiers |
| SC-3 | Same — eight multi-collection operations each inside one `lock (sync)`; no `async`/`await` in the store; one `TryAddSingleton<OnlineStateStore>()` and zero `new OnlineStateStore` in `Server/src` |
| SC-4 | Same — `TreatWarningsAsErrors=true` with `0 个警告`; four former-name occurrences remain, all in historical documents AC-003 permits |
| SC-5 | `reviews/change-review.md` — three independent subagent reviewers; nine findings, five fixed and four dismissed with stated reasons; no unresolved blocking finding |

## Repository Result

- Changed paths: `Server/src` (Abstractions, Application, Protocol), `Server/tests`, `AGENTS.md`,
  `Docs/`, `protocol/` documentation pointers, plus this task folder
- Baseline or accepted result: `f894034`
- Parent rollback required: no

## Successor Guidance

- The taxonomy's scope is `Dfo.Server.Abstractions`. Five Application-internal interfaces —
  `ISelectedSessionCleanupService`, `ISelectCharacterSessionEntryService`,
  `ISelectCharacterPreparationService`, `ISelectCharacterQuestEntryService`,
  `ISelectCharacterPetEntryService` — read and write world state but keep the bare suffix. They were
  left alone because renaming them would expand the authorized scope. `AGENTS.md` names all five.
- The online-state lock must stay single. The three collections express one invariant, and several
  operations only preserve it while updating more than one of them together. `AGENTS.md` records
  this next to the convention.
- `Server/tests/.../Support/UserDomainServices.cs` is a test fixture that composes the three real
  services over one real store. It forwards only. If a future change gives the three services
  different lifetimes or stores, this fixture stops representing production and must change with it.
- `ChannelHostStartupTests.InvalidRequiredChannelScript_FailsBeforeListenersStart(kind: "unreadable")`
  is flaky at baseline: five runs on `f894034` produced one failure. It is unrelated to this change
  and remains unfixed.
- The design basis and the remaining execution order are in `Docs/领域结构重构设计.md`, whose §12
  now records this as step 0.

## Artifacts

- `.rks-agent/tasks/domain-rule-service-taxonomy/TaskContract.md`
- `.rks-agent/tasks/domain-rule-service-taxonomy/routing-decision.md`
- `.rks-agent/tasks/domain-rule-service-taxonomy/spec.md`
- `.rks-agent/tasks/domain-rule-service-taxonomy/sdd-execution.yaml`
- `.rks-agent/tasks/domain-rule-service-taxonomy/reviews/change-review.md`
- `.rks-agent/tasks/domain-rule-service-taxonomy/verification/completion-evidence.md`
