---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Explicit Domain, Rule and Projection Service Taxonomy

## Context

### Source Request

Following a design conversation that produced `Docs/领域结构重构设计.md`, the user required that an
interface's category be legible from its name, asked for the whole project structure to be
restructured in one pass rather than in waves, and asked for the scope to be surveyed first:

> 命名中要看得出来接口是领域服务还是规则服务。然后我们一次性将整个项目的结构都重构。你看看范围多大，
> 先收集前置信息，在我说执行任务后我们才写任务契约和启动一次sdd重构。

After the survey was presented — 67 abstraction interfaces, 22 of them named `*Service`, 196
references across 69 files, three candidate naming schemes, and two open questions (the nine
dependency-free services and how to split `UserService`) — the user granted execution:

> 可以，UserService你先按你想的拆。写任务契约然后直接执行吧。注意审核要用subagent。

### Current Situation

Baseline `f894034`, clean tree, full suite green (619 passing, 70 skipped).

`Dfo.Server.Abstractions` declares 67 interfaces. Forty-five of them already carry their category in
their suffix — `Repository` (11), `DefinitionSource` (8), `Registry` (5), `Directory` (2),
`Persistence` (2), plus the transport suffixes `Decoder`, `Encoder`, `Sender`, `Dispatcher`,
`Handler`, `Closer`, `Observer`. The remaining 22 are named `*Service`, and that suffix conflates
three kinds of thing that a caller must treat differently:

- **World-state services (10).** Their implementations take a repository, registry or directory.
  Calling one reads or writes the game world, so ordering and locking matter. Examples:
  `ICharacterService`, `IAccountService`, `ISessionAdjacentEntryService`.
- **Content-only services (2).** `IEntryPlacementService` takes only `ITownDefinitionSource`;
  `IWorldContentEntryService` takes only content sources. They are deterministic functions of their
  inputs and hold no state.
- **Pure projections (8).** Every `*EntryService` in `IAccountEntryStateServices.cs` and
  `ICharacterEntryStateServices.cs` exposes a single synchronous `Build(storageRecord) → entryState`
  with no constructor dependencies and no I/O.

`IServerConfigService` is the twenty-second and belongs to none of the three: it is
`T GetConfig<T>(string key)`, a host configuration accessor.

Because the suffix does not distinguish them, a reader cannot tell from a call site whether an
operation is order- and lock-sensitive or is a pure function. `Docs/领域结构重构设计.md` §2–§4
records the categories and the test that separates them.

`UserService` implements three of these interfaces — `IAccountService`, `IOnlineUserService`,
`IActiveCharacterService` — over three dictionaries (`byId`, `byConnection`, `activeByAccount`)
guarded by one lock. That lock spans the groups rather than merely each of them: `BindCharacter`
writes `activeByAccount[accountId]` using an `OnlineUserId` read from the online-user dictionaries,
so the invariant "an account has exactly one active character, and that character's session belongs
to a live connection" is only maintained while all three are updated together.

## Goal

Make every Abstractions service interface state its category in its name, and separate
`UserService`'s three responsibilities into distinct implementation classes, without changing any
observable behavior of the server.

## Scope

### In

- Rename the world-state services to `I*DomainService` and the content-only services to
  `I*RuleService`.
- Rename the eight `Build(storageRecord) → entryState` services to `I*Projection`.
- Update every reference to the renamed interfaces, including dependency-injection registration and
  test code.
- Separate `UserService` into distinct implementation classes for its account, online-user and
  active-character responsibilities, keeping cross-group updates atomic.
- Record the resulting taxonomy, including the deliberate exclusion of `IServerConfigService`, where
  a later reader will find it.

### Out

- Any change to observable behavior, frame bytes, persistence, ordering or timing.
- Any change to a member signature beyond the interface type name it mentions.
- Splitting `SelectedSessionIdentity`, the six-tuple identity type.
- The party aggregate, dungeon-entry work, the projector layer, and moving `MaxJobId` to content —
  all deferred by `Docs/领域结构重构设计.md` §12.
- Restructuring character creation into a policy plus a factory. It was proposed as a first wave
  before the user chose a single whole-project pass, and is the natural follow-up rather than part
  of this task.
- Renaming the 45 interfaces whose suffix already carries their category.

## Constraints

- Behavior must be preserved exactly. This task changes names and class boundaries only.
- Cross-group updates in the separated `UserService` responsibilities must remain atomic with
  respect to each other. Splitting one lock into several would break the invariant described in
  Current Situation, which is a behavior change and therefore out of scope.
- Review must be performed by subagents.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Every Abstractions service interface states its category in its name | An inventory of the interfaces in `Dfo.Server.Abstractions` shows each former `*Service` carrying `DomainService`, `RuleService` or `Projection`, with `IServerConfigService` the only exception and its exclusion recorded |
| SC-2 | No behavior changed | The full solution test suite passes, and the diff of `Server/tests` contains only identifier renames — no changed assertion, expected value, test name or test case |
| SC-3 | The separated online-state responsibilities still update atomically across groups | The operations that read one group and write another do so without an interleaving window, demonstrated in the implementation and covered by the existing online-user and select-character tests passing |
| SC-4 | No stale references remain | The solution builds without new warnings, and a search for the former interface names returns no occurrences outside historical documents |
| SC-5 | The result has been reviewed by subagents | Subagent review findings are recorded, and each is either fixed or dismissed with a stated reason |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | After the scope survey was presented, the user replied: “可以，UserService你先按你想的拆。写任务契约然后直接执行吧。注意审核要用subagent。” | Author this TaskContract and execute the taxonomy rename across `Server/src` and `Server/tests`, separate `UserService` at the Agent's discretion, verify by building and running the existing suite, review by subagent, and commit the verified result on the current branch |

## Assumptions

- The survey figures — 67 interfaces, 22 named `*Service`, 196 references across 69 files, 10 / 2 /
  8 / 1 in the four categories — were measured at `f894034` and are expected to hold, but the exact
  counts are not themselves acceptance criteria.
- Test files reference the interface type names, so their diffs cannot be empty. SC-2 treats an
  identifier-only test diff as evidence of preserved behavior.
- `IOnlineUserQueryService` is implemented by `UserService` alongside the other three; the survey
  script failed to match it by name only.
