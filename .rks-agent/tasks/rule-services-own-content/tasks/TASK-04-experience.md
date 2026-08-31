# TASK-04 — Experience answers come from one rule service

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: One rule service owns what a kill, a clear and a channel are worth. It takes the four experience-related sources, is the only caller of `MonsterExperienceRules`, `DungeonClearExperienceRules` and `ExperienceBonusRules`, and holds the fixed-reward-slot constant with its existing justification.
- Referenced spec requirements: RQ-001, RQ-003, RQ-004, RQ-005, RQ-008
- Referenced plan sections: T-004; boundaries BD-004, BD-013, BD-014, BD-015, BD-017
- Owned boundary: BD-004 experience rule seam in `Server/src/Dfo.Server.Abstractions/World/` and `Server/src/Dfo.Server.Application/World/`; the experience portions of BD-013 and BD-014; the experience registration in BD-015; experience construction sites in BD-017.
- Permitted files or interfaces: the new experience rule service and its interface; the consumers of `IExperienceParameterSource`, `IDungeonExperienceProgressionPolicySource`, `IDungeonRankGradeSource` and `IChannelExperienceSource`; the three experience static rule classes; `CharacterExperienceGrant`, `DungeonEffectRecoveryService`, `ExperienceBonusService`; the kill, clear and tutorial-level command handlers; `MonsterDeathSlice.cs`; `DungeonClearSlice.cs`; `ApplicationModule.cs`; affected test construction sites.
- Dependencies: TASK-03
- Required dependency outputs: a tree green at TASK-03's accepted state.
- Acceptance conditions: the only consumers of the four experience sources in `Server/src` are the experience rule service and the infrastructure implementations; the three static rule classes are called only from it; the fixed-reward-slot constant lives in it with an unchanged value and justification; no handler injects an experience source or calls an experience static rule class; the suite is green at the baseline count.
- Task-specific authorization: the root task grant. Implementation only.
- Required methods: `test-driven-development` in its behaviour-preserving form.
- Required checks: CK-008 `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set; CK-009 non-command enumeration of the three static rule classes' call sites.
- Required evidence: suite result against 1690/2/0, the CK-009 enumeration, and the owned diff.

## Contract Amendment 1 — two game-rule configuration values the survey did not count

- Raised by: this Task, before execution.
- Contract revision: 2

The baseline survey counted four `*Options` types. RQ-002 is broader than that: it says *every*
game-rule configuration value. Two experience ones are not named `*Options` and were therefore
missed:

- `ExperienceTuning`, built by `ExperienceTuningSource.From(configuration)` and injected straight
  into `GrantKillExperienceCommandHandler` — "the curve, the divisor, the rank rates and the
  level-gap bands", explicitly "only what the archive does not declare".
- `ExperienceEntitlementPolicy`, built from `Experience:PremiumRates` and injected into
  `ExperienceBonusService`.

Both are game rules an operator dials, both are read outside a rule service, both belong to this
Task's subject. Added to its inputs, permitted files and acceptance conditions.

`ExperienceDiagnostics` is deliberately **not** added. It is a measurement switch that makes a kill
pay distinct amounts into each named line so the client labels them — instrumentation, of the same
family as the host deployment configuration the user excluded, not a rule the game plays by.

## Contract Amendment 2 — the fixed-reward-slot constant is reclassified out

- Raised by: this Task, from reading its declaration site.
- Contract revision: 3

The Plan assigned `FixedRewardSlotCount = 23` (`DungeonClearSlice.cs`) to the experience rule
service. Read in place, it is not a rule constant: it is the number of four-byte zero slots the
clear-reward frame writes, and it sits beside `ClearRewardBodyLength = 274`, which is the same
frame's measured length. Both express packet layout, not archive or game semantics, and the frame's
own length check depends on them agreeing.

RQ-004 covers constants expressing archive or game semantics. This one does not, so it stays in the
slice with the length it belongs to. Removed from this Task's inputs and acceptance conditions;
recorded in TASK-11 so the inventory states the classification rather than showing a gap.

## Execution Result — round 1

### What was built

`IExperienceRuleService` (`Server/src/Dfo.Server.Abstractions/World/`) and `ExperienceRuleService`
(`Server/src/Dfo.Server.Application/World/`), taking all four experience sources, the quest content
rule service, and both configuration values. Five answers: `WorthOfKill`, `IsMonster`,
`WorthOfClear`, `ModifiersFor`, `Progression`. One new carrier:
`ClearExperienceWorth(Base, RankBonus)`.

All three static rule classes are now called from it and from nowhere else.

### Consumers redirected

| Consumer | Layer | What it held |
|---|---|---|
| `GrantKillExperienceCommandHandler` | command handler | `IExperienceParameterSource` + `ExperienceTuning` + `MonsterExperienceRules` |
| `GrantClearExperienceCommandHandler` | command handler | quest rules + `IExperienceParameterSource` + `IDungeonRankGradeSource` + `DungeonClearExperienceRules` — four dependencies became one |
| `MonsterDeathHandler` | protocol handler | `MonsterExperienceRules.IsMonster` |
| `ExperienceBonusService` | Application | `ExperienceEntitlementPolicy` + `IChannelExperienceSource` + `ExperienceBonusRules` |
| `CharacterExperienceGrant` | Application | `IDungeonExperienceProgressionPolicySource` |
| `DungeonEffectRecoveryService` | Application | same |
| `TutorialLevelUpSlice` | protocol handler | same |

`ExperienceBonusService` is the shape change worth naming: it kept exactly what it is for — reading
the published session — and now hands the premiums, the clock reading, the channel and the dungeon
to the rules instead of fetching the policy and the catalogue itself. Its own doc already said
"fetching and deciding are separate here on purpose"; the deciding half has moved to where it says
it belongs.

### Checks

| Check | Command or method | Result |
|---|---|---|
| build | `dotnet build Server/86JP.Server.slnx --nologo` | 0 errors, 0 warnings |
| CK-008 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passed, 2 skipped, 0 failed |
| CK-009 | `grep -rn` for the three static rule classes' call sites in `Server/src` | Only `ExperienceRuleService`, six call sites, no others |
| CK-009 extended | the four sources plus `ExperienceTuning` and `ExperienceEntitlementPolicy` | Only `ExperienceRuleService`, plus the declarations, the PVF implementations and the `ApplicationModule` registrations |

## Executor Self-check — round 1

- Acceptance "four sources consumed only by the rule service": met.
- Acceptance "three static rule classes called only from it": met, CK-009.
- Acceptance, amended, "both configuration values consumed only by it": met.
- Acceptance "no handler injects an experience source or calls an experience static rule class": met.
  Three handlers now take `IExperienceRuleService`; TASK-09 owns that half.
- Acceptance "suite green at the baseline count": met, 1690/2/0.
- Behaviour preservation, checked argument by argument:
  - `WorthOfKill` passes the same `KillCircumstances`, the same `parameters.Load()` and the same
    tuning instance to `MonsterExperienceRules.WorthOf`.
  - `WorthOfClear` reads `parameters.Load()` **once** and passes that one instance to both
    `BaseWorth` and `RankBonus`, which is what the handler did with its local `settings`. Reading it
    twice would have been the quiet change here.
  - `ModifiersFor` calls the two bonus rules in the same order into the same list, so the modifier
    order the client sees is unchanged.
  - `Progression()` is `progression.Load()`, uncached, so the read count is unchanged too.
- Test doubles: the three tests that assert what an occasion *pays* — kill worth, clear worth,
  channel bonus — were wired to the real `ExperienceRuleService` over their existing stub sources,
  so they still test the arithmetic. The four that only need the level thresholds were given an
  `IExperienceRuleService` double that answers `Progression()` and throws for everything else,
  because inventing kill or clear values there would make a future defect invisible.

## Quick Review — round 1

- Verdict: `passed`
- Reviewer context: inline, for the reason recorded in TASK-01's review round.

### Findings

1. **The single `parameters.Load()` in `WorthOfClear` is the point of the whole task.** The handler
   held `var settings = parameters.Load()` and used it twice; a careless move would have written
   `parameters.Load()` into both call sites. It reads once. Checked by eye against the original.
2. **`GrantClearExperienceCommandHandler` lost its quest dependency entirely**, which is correct:
   its only use of the quest rules was `Parameters()` feeding `BaseWorth`, and that now happens
   inside the rule service where both quest parameters and experience parameters are already at hand.
3. **`ExperienceBonusService`'s early return changed from returning the empty list it had just built
   to returning an empty collection expression.** Same value; the list was only built early so it
   could be filled later, and it no longer is.
4. **The reclassification of `FixedRewardSlotCount` was checked in place, not assumed.** It is used
   in one loop writing 23 zero ints, in the same method as `ClearRewardBodyLength = 274`, and the
   method ends with a length assertion tying them together. Moving it would have split a pair.
5. **Scope check.** `DungeonClearSlice.cs` was not touched. No file outside the amended permitted
   list changed.
