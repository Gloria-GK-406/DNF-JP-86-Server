# Routing Decision

- TaskContract: `.rks-agent/tasks/domain-rule-service-taxonomy/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The naming requirement, the categories and the whole-project single
  pass were settled across a design conversation that produced `Docs/领域结构重构设计.md`. The
  observable result is fixed and mechanical — each service interface's name states its category and
  no behavior moves.
- Requirement stability: The requirements are enumerable to the individual interface. A survey at
  `f894034` classified all 22 `*Service` interfaces into 10 world-state, 2 content-only, 8 pure
  projection and 1 excluded, and measured 196 references across 69 files.
- Execution direction: Known. Rename by category, update references, separate `UserService` into
  three classes; the user delegated the `UserService` shape explicitly.
- Remaining uncertainty: Only local naming choices for the separated `UserService` classes, which do
  not change task meaning.

## Marginal Control Value

- User precision signal: The user asked for the scope to be surveyed before authorizing, chose a
  single whole-project pass over incremental waves, and required subagent review. That is a
  precision preference, not a speed preference.
- Direct failure mode: A rename touching 196 references across 69 files invites two specific
  failures — a behavior change smuggled in alongside the rename, where a green suite would not
  distinguish the two; and the `UserService` separation splitting one lock into three, which would
  silently break the cross-dictionary invariant that no test currently targets directly.
- Useful control: A Spec that fixes the category assignment per interface before any edit, and an
  independent subagent review that reads the diff for behavior changes hiding inside identifier
  changes. The contract's SC-2 makes that reviewable by requiring the test diff to be
  identifier-only.
- Time-for-quality rationale: The mechanical part is large but low-risk; the judgment part is small
  but high-consequence and invisible to the test suite. A Spec plus review costs little against a
  refactor of this width and is the only thing that separates "renamed" from "renamed and quietly
  changed".

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: One coherent outcome with one acceptance. The parts cannot be accepted
  independently — renaming an interface without updating its references does not compile, so there
  is no intermediate state that could be integrated or rolled back on its own.
- Why Direct is insufficient: Direct provides no artifact that separates the mechanical rename from
  a behavior change, and the failure it must catch is one the test suite cannot see.
- Why Multi is unnecessary: There is a single integration boundary and a single rollback boundary.
  File count and reference count do not create independent outcomes.
- Reroute conditions: Reroute to Direct if the category assignment turns out to be already settled
  and the work collapses to a mechanical rename with no judgment. Reroute to Multi Spec if the
  `UserService` separation proves to require a behavior change, since that would become a second
  outcome with its own acceptance and rollback.

## Controller Selection

- Controller: `sdd-lite`
- Profile source: repository default
- Why this is sufficient: The outcome is mechanical and fully covered by an existing 619-test suite
  plus one contract condition (identifier-only test diff) that makes the dangerous failure visible.
  A Spec fixing per-interface category assignment plus the required subagent review supplies the
  judgment control; nothing further changes the outcome.
- Why lighter is insufficient: Without a Spec recording the category per interface, the assignment
  decisions live only in the diff, and the reviewer has nothing to check them against.
- Why heavier is unnecessary: The task touches access-control-adjacent code — `IAccountService`
  carries `LoginAsync`, and the separated online table holds the account-to-character binding — so
  the Governed hard floor was considered rather than skipped. It does not apply on reach: the
  binding invariant is already named as a contract constraint, the login path performs no
  authentication to weaken, no protected data or irreversible external mutation is involved, and
  the one control Governed would add over Lite here is independent challenge, which the required
  subagent review already supplies. Governed's remaining controls — independent artifact approval
  and audit-strength evidence — tie to no failure path this task can produce.
