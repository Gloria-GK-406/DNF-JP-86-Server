# TASK-05 — Drop answers come from one rule service

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: One rule service owns what an occasion drops. It takes `IDropRateTableSource`, `IGoldDropTableSource`, `IDropItemPoolSource` and `DropRateOptions`, and is the only caller of `DropRules`. `MonsterDropService` is reduced to asking for rates, asking for a draw, allocating numbers and logging.
- Referenced spec requirements: RQ-001, RQ-002, RQ-003, RQ-005, RQ-008
- Referenced plan sections: T-005; boundaries BD-005, BD-013, BD-015, BD-017
- Owned boundary: BD-005 drop rule seam in `Server/src/Dfo.Server.Abstractions/World/` and `Server/src/Dfo.Server.Application/World/`; the drop portion of BD-013; the drop registration in BD-015; drop construction sites in BD-017.
- Permitted files or interfaces: the new drop rule service and its interface; `DropRules.cs` and `DropRateOptions`; `MonsterDropService.cs`; `IDropRateAdjuster`; `ApplicationModule.cs`; affected test construction sites.
- Dependencies: TASK-04
- Required dependency outputs: a tree green at TASK-04's accepted state, and the room-composition rule service from TASK-02, which `MonsterDropService` already consults by then.
- Acceptance conditions: the only consumers of the three drop sources and `DropRateOptions` in `Server/src` are the drop rule service and the infrastructure implementations; `DropRules` is called only from the drop rule service; the rate basis is still read from configuration and passed in rather than defaulted inside the rules; the suite is green at the baseline count.
- Task-specific authorization: the root task grant. Implementation only.
- Required methods: `test-driven-development` in its behaviour-preserving form.
- Required checks: CK-010 `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set; CK-011 non-command enumeration of `DropRules` call sites and of the consumers of the three drop sources and `DropRateOptions`.
- Required evidence: suite result against 1690/2/0, the CK-011 enumeration, the rate-basis confirmation, and the owned diff.

## Execution Result — round 1

### What was built

`IDropRuleService` (`Server/src/Dfo.Server.Abstractions/World/`) and `DropRuleService`
(`Server/src/Dfo.Server.Application/World/`), taking `IDropRateTableSource`, `IGoldDropTableSource`,
`IDropItemPoolSource` and `DropRateOptions`. It is the only caller of `DropRules` — all six entry
points, `RatesFor`, `Gate`, `KindOf`, `GoldAmount`, `RollRarity` and `Pick`.

Two questions rather than one, and the seam between them is the point:

- `RatesFor(kill)` answers what the archive's tables say this kill is worth, or null when the rate
  table declares no band covering the monster's level.
- `DrawFor(kill, rates)` derives the seed and draws at whatever rates it is handed.

`IDropRateAdjuster` stays **outside** the rule service, in `MonsterDropService`, between the two
calls. Its own documentation says what will sit behind it "counts a character's kills, flips and
levels since the last piece of gear, reads their warehouse" — world state. A rule service holding it
would acquire world-state access by proxy, which is exactly what `Docs/领域结构重构设计.md` §6
forbids and what this task's Constraints name. Handing the adjusted rates back in keeps the line.

### What `MonsterDropService` is now

Flow only: ask for rates, raise them on the character's behalf, ask for a draw, fetch how many
pieces the room declares, number them, log. It went from eight constructor dependencies to five, and
from 200 lines to 88. Nothing it holds reads content any more.

### Checks

| Check | Command or method | Result |
|---|---|---|
| build | `dotnet build Server/86JP.Server.slnx --nologo` | 0 errors, 0 warnings |
| CK-010 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passed, 2 skipped, 0 failed |
| CK-011 | `grep -rn` for `DropRules.` call sites and for the three sources and `DropRateOptions` | Only `DropRuleService`; six `DropRules` call sites, all in it |
| rate basis | read `ApplicationModule` and the rule service | Still read from `DropRates:RateBasis`, still defaulted to `new DropRateOptions().RateBasis`, still passed into `DropRules.RatesFor` as its `basis` argument rather than defaulted inside the rules |

### A flake, reported rather than smoothed over

The first full-suite run after the change reported one failure in `Dfo.Server.Application.Tests`
(770 passed of 771). The project run on its own immediately afterwards passed 771 of 771, and the
next full run passed 1690/2/0. I did not capture which test it was before rerunning, so this is
recorded as an unidentified flake rather than as a diagnosed one. `AGENTS.md` documents two
load-dependent false-red shapes in this repository — the real-process and real-socket cases, and the
30-second SQLite read-then-write contention — and this run had a full solution build in flight
alongside it. It is named here because "it passed on the retry" is exactly how a real intermittent
defect gets buried, and the next run of this suite should be watched for a repeat.

## Executor Self-check — round 1

- Acceptance "three drop sources and `DropRateOptions` consumed only by the rule service": met.
- Acceptance "`DropRules` called only from the drop rule service": met, CK-011.
- Acceptance "the rate basis is still read from configuration and passed in rather than defaulted
  inside the rules": met, and checked deliberately — this is the one number the archive does not
  declare, and the routing decision named a defaulted constant as the failure mode this repository
  has already been bitten by twice.
- Acceptance "suite green at the baseline count": met on the rerun, with the flake above disclosed.
- Behaviour preservation, the two places it was not a pure move:
  - The rate-table `Load` now happens twice per kill — once in `RatesFor` and once in `DrawFor` —
    where it happened once before. `IDropRateTableSource` is a deterministic cached content source,
    so the value is identical; only the number of reads changed, and the drawn results are
    unaffected because the same table instance content feeds both.
  - The "rate table covers no level" warning moved from inside the roll to the caller, which is
    why `RatesFor` answers null rather than empty rates. Same message, same fields, same point in
    the flow.
  - The seed derivation moved with the draw, unchanged in all five of its inputs.
- Not done: nothing. `IDropRateAdjuster` was deliberately left where it is, with the reason above.

## Quick Review — round 1

- Verdict: `passed`
- Reviewer context: inline, for the reason recorded in TASK-01's review round.

### Findings

1. **The `Draw` method moved verbatim**, including the whole remark about why the avatar slot draws
   nothing and why the rarity is rolled outside it. Compared line by line against the original: the
   only change is the enclosing class.
2. **`RatesFor` and `DrawFor` each `Load` the rate table.** Flagged and accepted: the source is
   documented as deterministic and cacheable, the two loads are of the same table, and no drawn
   value depends on how many times it was read. The alternative — carrying the table across the two
   calls — would have put a content object in the domain service's hands, which is what this task
   removes.
3. **The compensation seam is on the correct side of §6.** Checked against `IDropRateAdjuster`'s own
   remarks, which describe a future implementation reading persisted per-character state. Had it
   gone into the rule service, this task would have satisfied RQ-001 by breaking RQ-006.
4. **The test was rewired to the real `DropRuleService`** over its existing stub tables, pools and
   basis, so `MonsterDropServiceTests` still exercises the real gate, rarity ladder and pick rather
   than a fixture's idea of them.
5. **Scope check.** `DropRules.cs` itself was not edited; the rules are unchanged, only their
   caller. No file outside the permitted list changed.
