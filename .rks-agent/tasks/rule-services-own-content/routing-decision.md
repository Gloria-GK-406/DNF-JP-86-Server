# Routing Decision

- TaskContract: `.rks-agent/tasks/rule-services-own-content/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The user stated the architecture directly — a CMD handler calls a
  business service, the business service consults a rule service, and the rule service reads the
  content and configuration the rules need. `Docs/领域结构重构设计.md` §2 already declares the same
  line, so the target shape is not being invented here. The observable result is that rule inputs
  reach the server only through rule services, which is checkable by looking at who injects them.
- Requirement stability: Every explicit requirement is enumerable at the baseline. A whole-repository
  survey at `8af7990c` produced the concrete list — 31 PVF content-source read points, 4 options
  types, 6 static rule classes, 4 Protocol-layer calls that skip the domain service — and each entry
  is individually reviewable.
- Execution direction: Known and demonstrated in-repo. `EntryPlacementRuleService`, which takes
  `ITownDefinitionSource` together with `ChannelEntryOptions`, is already the intended shape, so the
  direction is a shape to be propagated rather than a design to be discovered.
- Remaining uncertainty: Local only — which rule service owns a given source when several consumers
  share it, and what each new interface is called. Neither changes the task's meaning. The one
  boundary question that would have changed meaning, whether host deployment parameters count as
  rule inputs, the user settled: they do not.

## Marginal Control Value

- User precision signal: Strongly toward precision. The user declined to advance the feature
  backlog before judging the structure — "比起盲目推进度还是先看看服务结构是否合理" — then, given
  the survey, chose whole-repository correction over a local fix, asked for SDD explicitly, and
  required a stop before execution so the artifacts can be read first.
- Direct failure mode: Two, both specific. First, a partial migration that looks finished: with
  35+ read points across three layers and no enumerated list to check against, "全量" has no
  observable meaning and a missed consumer reads as done. Second, a value that changes while being
  moved. Relocating a hard-coded rule constant into a rule service is exactly where a default
  quietly shifts, and this repository has been bitten twice by that class of defect — the two
  experience-notice slots and the source of the channel rate — both of which looked entirely
  ordinary in review and were invisible in play.
- Useful control: A Spec that enumerates every rule input as an explicit requirement, so an omission
  is visible rather than inferred; a Plan that fixes each source's owning rule service before its
  consumers are touched and orders the migration accordingly; and complete-result review against
  that enumeration followed by fresh verification against the 1690-assertion suite.
- Time-for-quality rationale: The entire value of this task is completeness. Without the enumeration
  it degrades into a large tidy-up whose success cannot be stated, which is the failure the user
  pre-empted by refusing to advance on feel. The suite already exists and runs green on this
  machine, so the added workflow time is the Spec and Plan themselves rather than new test
  infrastructure.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: One architectural invariant with one acceptance boundary. SC-1 is
  whole-repository by construction — rule inputs enter only through rule services — so no proper
  subset of the work satisfies the contract's goal, and there is nothing to accept independently.
  Rollback is likewise single: the change is behaviour-preserving throughout, and reverting part of
  it leaves the repository in neither the old shape nor the new one.
- Why Direct is insufficient: Direct keeps the enumeration in conversation, where it cannot survive
  the session boundary this task will certainly cross, and offers no artifact against which
  "complete" can be checked. Both named failure modes are failures of completeness and of quiet
  value drift, and neither is caught by a compiler or by a green suite alone.
- Why Multi is unnecessary: The obvious groupings — quests, inventory, dungeon entry, experience,
  drops, channel — are sequencing units inside one Plan, not independently acceptable outcomes. They
  share one test suite, one merge, one rollback point, and one invariant that is unsatisfied until
  the last of them lands. Step count and duration do not create a Multi boundary.
- Reroute conditions: Return to `multi-spec-workflow` if a subsystem turns out to require a
  behaviour change to be movable, because that outcome would then need its own acceptance and
  rollback rather than riding on a behaviour-preserving contract. Return to intent alignment if
  relocating content access forces rule services to take world state in more than an isolated case,
  since the §6 constraint and this task's constraint would then be in genuine conflict and the
  contract's premise would be wrong. Reconsider the control level if the suite proves not to be a
  sufficient behaviour oracle — that is, if a relocation passes the suite while changing a frame.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: selected by `sdd-standard` from its bundled Profiles
- Why this is sufficient: The two named failure modes are both reached by ordinary controls. The
  Spec's enumeration makes an omitted read point visible; the Plan settles each shared source's
  owner before its consumers are edited, which is what keeps the migration from rework; complete
  ordinary review against the enumeration plus fresh verification against the existing suite catches
  a value that moved while being relocated, since the contract requires the test diff to change no
  assertion or expected value.
- Why lighter is insufficient: Lite carries no `plan.md`, and the ordering decisions here are real
  rather than incidental. Several sources have many consumers spread across two layers —
  `IItemPlacementDefinitionSource` has six, `IQuestDefinitionSource` four,
  `IDungeonExperienceProgressionPolicySource` three — so the choice of owning rule service
  propagates into every one of those call sites. Under Lite that choice is made inline and only
  tested at complete-result review, by which point correcting it means redoing the call sites in
  bulk. Fixing ownership first, durably, is precisely the control Lite lacks.
- Why heavier is unnecessary: Governed's distinctive controls — independent challenge, independent
  artifact approval, drift control, audit-strength evidence — have no material failure path to
  attach to here. This is an internal, behaviour-preserving refactor of a private game server: no
  safety, credential, authorization, privacy, financial, legal or regulatory reach; no irreversible
  external mutation; no durable-data-loss path. Recovery is a single `git reset` on one branch
  against a clean, verified baseline, and the behaviour oracle is a suite that already runs green.
  Buying independent challenge would spend time without reducing a named escape.
