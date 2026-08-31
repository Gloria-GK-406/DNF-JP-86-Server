# TASK-03 — Quest content answers come from one rule service

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: One rule service owns quest definitions and parameters. The five remaining `IQuestDefinitionSource` consumers, the two `IQuestParameterSource` consumers, and the existing world-content rule service all obtain quest content from it, so one owner remains.
- Referenced spec requirements: RQ-001, RQ-005, RQ-008
- Referenced plan sections: T-003; boundaries BD-003, BD-013, BD-014, BD-015, BD-017
- Owned boundary: BD-003 quest-content rule seam in `Server/src/Dfo.Server.Abstractions/Quests/` and `Server/src/Dfo.Server.Application/Quests/`; the quest-source portions of BD-013 and BD-014; the quest registration in BD-015; quest construction sites in BD-017.
- Permitted files or interfaces: the new quest-content rule service and its interface; `WorldContentEntryRuleService`; the character entry-state and quest entry domain services; the accept, finish and set-trigger quest command handlers and the quest log domain service; `ApplicationModule.cs`; affected test construction sites.
- Dependencies: TASK-02
- Required dependency outputs: a tree green at TASK-02's accepted state.
- Acceptance conditions: the only consumers of `IQuestDefinitionSource` and `IQuestParameterSource` in `Server/src` are the quest-content rule service and the infrastructure implementations; no command handler injects either source; the suite is green at the baseline count.
- Task-specific authorization: the root task grant. Implementation only.
- Required methods: `test-driven-development` in its behaviour-preserving form.
- Required checks: CK-006 `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set; CK-007 non-command enumeration of both quest sources' consumers.
- Required evidence: suite result against 1690/2/0, the CK-007 enumeration, and the owned diff.

## Execution Result — round 1

### What was built

`IQuestContentRuleService` (`Server/src/Dfo.Server.Abstractions/Quests/`) and
`QuestContentRuleService` (`Server/src/Dfo.Server.Application/Quests/`), taking both
`IQuestDefinitionSource` and `IQuestParameterSource`. Four answers: `AllAsync`, `FindAsync`,
`TrainingAvailabilityAsync`, `Parameters`.

The two sources were joined rather than given a rule service each, because no quest outcome is
decided without both: a completion needs the quest's own definition and the shared parameter table
that says what the level gap is worth.

### Consumers redirected

| Consumer | Layer | Source it held |
|---|---|---|
| `WorldContentEntryRuleService` | rule service | `IQuestDefinitionSource` — the one already-compliant reader, now behind the single owner |
| `CharacterEntryStateDomainService` | Application | `IQuestDefinitionSource` |
| `SelectCharacterQuestEntryService` | Application | `IQuestDefinitionSource` |
| `AcceptQuestCommandHandler` | command handler | `IQuestDefinitionSource` |
| `SetQuestTriggerCommandHandler` | command handler | `IQuestDefinitionSource` |
| `FinishQuestCommandHandler` | command handler | both — two constructor parameters became one |
| `GrantClearExperienceCommandHandler` | command handler | `IQuestParameterSource` |

### Checks

| Check | Command or method | Result |
|---|---|---|
| build | `dotnet build Server/86JP.Server.slnx --nologo` | 0 errors, 0 warnings |
| CK-006 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passed, 2 skipped, 0 failed |
| CK-007 | `grep -rn` over both quest sources in `Server/src` | Only `QuestContentRuleService`, plus the interface declarations, the two PVF implementations and their `InfrastructureModule` registrations |

### The suite went red in the middle, and that was the control working

Nine tests failed after the first green build: eight in `WorldContentArchiveTests` and one in
`SelectCharacterPayloadCodecTests`. Both build their own partial DI graph rather than using the whole
composition root, and both registered `IQuestDefinitionSource` without `IQuestParameterSource` —
which nothing had needed until the two were joined behind one rule service. Neither is a behaviour
defect; both were fixed by giving those fixtures the second registration, one as an explicit
construction and one as a `NoQuestParameters` stub that throws rather than inventing a reward value.
Recorded because joining two sources behind one owner is exactly the move that can break a partial
graph, and the next task that joins sources should expect it.

## Executor Self-check — round 1

- Acceptance "both quest sources consumed only by the rule service": met, CK-007.
- Acceptance "no command handler injects either source": met. Four command handlers now take
  `IQuestContentRuleService`; TASK-09 owns removing rule services from handlers, and its amendment
  already records the growth.
- Acceptance "suite green at the baseline count": met, 1690/2/0.
- Behaviour preservation: every redirect is a rename of the call, method for method.
  `AllAsync`/`FindAsync`/`TrainingAvailabilityAsync`/`Parameters` each forward to the source call
  they replaced with no argument or ordering change.
- Recovered exception, twice, neither reaching the result:
  1. The rewrite helper re-sorted the `using` block of six files, which this repository does not keep
     alphabetical. Caught by reading the diff, and reverted so the block keeps its original order
     with only the new line inserted.
  2. The nine test failures above.
- Test-diff discipline: the only assertion-shaped line in the whole `Server/tests` diff is still
  TASK-02's `Assert.Equal(first, Rooms.FirstActorSequenceFor(AtStart))`, a changed symbol rather than
  a changed expectation.

## Quick Review — round 1

- Verdict: `passed`
- Reviewer context: inline, for the reason recorded in TASK-01's review round.

### Findings

1. **`Parameters()` is called at the same two points and nowhere else.** `FinishQuestCommandHandler`
   used `questParameters.Load().ExperienceFor(...)` and now uses `quests.Parameters().ExperienceFor(...)`;
   `GrantClearExperienceCommandHandler` passed `questParameters.Load()` as an argument and now passes
   `quests.Parameters()`. `Load()` is not cached by the rule service, so the number of archive reads
   is unchanged as well as the values.
2. **`WorldContentEntryRuleService` consulting another rule service is the intended shape**, not a
   layering violation: a rule service may consult a rule service, and this is what leaves one owner
   of the quest declarations rather than two readers that happen to agree.
3. **The `NoQuestParameters` stubs throw rather than return a value.** Checked deliberately: a stub
   returning a made-up `QuestParameters` would let a future change silently start paying invented
   rewards on those paths and still pass. Throwing means a test that starts needing the value fails
   loudly and gets a real one.
4. **Scope check.** No file outside the permitted list changed. `IQuestParameterSource` registrations
   added to two test fixtures are wiring inside those fixtures.
