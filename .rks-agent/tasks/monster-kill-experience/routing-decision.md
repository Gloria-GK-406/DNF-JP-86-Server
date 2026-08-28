# Routing Decision

- TaskContract: `.rks-agent/tasks/monster-kill-experience/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The outcome is one sentence and has been fixed since it was
  reported from live play -- killing a monster must be worth experience. What was unstable a day
  ago was the arithmetic, and it is now closed: a run against the reference server on 2026-08-27
  produced four distinct awards with every intermediate value logged, so the answer is checkable
  rather than argued.
- Requirement stability: Eight success conditions, six of them checkable without a person. The
  four captured awards are exact numbers, not tolerances. The two that need a live client are the
  ones a suite genuinely cannot see -- that the client's popup moves, and that the entry batch is
  still byte-identical.
- Execution direction: Settled with the user across roughly a dozen turns and accepted in their
  words -- 「可以，这样看着就很干净了」. What is closed (what a kill is worth), what is open (what a
  player carries on top), where granting lives, and why the collection interface is synchronous
  were each argued and decided, including two proposals of the agent's that the user rejected.
- Remaining uncertainty: Only local. Whether simultaneous bonuses add or compound cannot be decided
  from samples that never had two at once; the contract records it as an assumption, and with one
  entitlement implemented no observable result depends on it.

## Marginal Control Value

- User precision signal: Precision, unusually explicitly. The user rejected the reference server as
  an authority (「上游也不是权威」), asked for that to be written into memory, pushed back on an
  agent design that would have frozen a snapshot, and asked twice for evidence before code. They
  also asked for a clean baseline before this task began rather than after.
- Direct failure mode: This change refactors a path that already works and is already live-verified
  -- quest completion's granting. The concrete risk is that the refactor silently changes what a
  quest pays, and nothing notices, because the number a quest pays is invisible to everyone until
  someone plays that quest again. A second, related risk is the one already realised once in this
  code: a granting path that reads the character from the wrong place and writes the same total
  twice. Both are omissions rather than mistakes, and both survive a working build.
- Useful control: A Spec that states the quest path's existing behaviour as a requirement in its own
  right rather than leaving it as something the refactor is assumed not to break, plus a
  complete-result review before verification -- the review being the control that reads the whole
  change against the contract rather than the diff it happens to be looking at.
- Time-for-quality rationale: The work spans five layers and touches two paths that are already
  live. The Spec costs a single pass; the failure it protects against is a wrong number in a
  shipped path that nobody can see by looking. The user's own sequencing -- baseline first, then
  start -- is the same trade being made one step earlier.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: One coherent outcome with one acceptance boundary. Valuation, bonus
  collection, granting and the frames are a single chain: none of them is separately acceptable,
  because a valuation nothing grants changes nothing observable, and a granting path with no
  valuation has nothing to grant. They also share one rollback boundary -- the refactor of the
  quest path cannot be accepted or reverted independently of the kill path it exists to serve.
- Why Direct is insufficient: Direct gives no place to state "quest completion still pays what it
  paid" as a requirement before the refactor that could break it, and no complete-result review
  that would notice its absence.
- Why Multi is unnecessary: There is no second independently acceptable outcome. The only candidate
  -- the shared granting implementation -- is not deliverable on its own: extracted with no second
  caller it is a refactor nobody asked for, and it earns its existence only when the kill path uses
  it.
- Reroute conditions: Reroute to Multi if the elixir's timed-effect concept is pulled into scope,
  which would add a genuinely separate acceptance boundary with its own storage and lifetime.
  Reroute to Direct if the quest-path refactor turns out to be already shared and the work reduces
  to the kill path alone. Reroute back to intent alignment if a live sample shows the captured
  awards are not reproducible, since the arithmetic would then be unsettled again.

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-feature`
- Why this is sufficient: The two controls that carry the risk here are a durable Spec and a
  complete-result review by a fresh reader, and Lite has both. The acceptance boundary is already
  numeric -- four captured awards -- so there is little for a Spec to leave ambiguous, and a review
  that reads the whole change against the contract is what catches the quest path being quietly
  changed.
- Why lighter is insufficient: `small-task-workflow` has no review stage. The failure mode named
  above is an omission that a passing build and a passing suite both tolerate, which is exactly
  what a fresh complete-result reader is for.
- Why heavier is unnecessary: Standard buys a durable Plan, and the plan here is already written --
  the layering, its order, and what each layer may not know were settled with the user turn by turn
  and are recorded in the contract's scope. Writing them again as a Plan would restate an agreed
  decision rather than resolve an open one. Governed buys independent challenge of the Spec and
  drift control, and nothing here is high-consequence in the way that earns them: the change is
  local, reversible, and its acceptance is a number that either matches a capture or does not.
