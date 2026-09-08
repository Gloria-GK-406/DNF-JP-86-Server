# Routing Decision

- TaskContract: `.rks-agent/tasks/dressing-a-character-and-seeing-it/attempts/spec-001-a-move-reads-both-of-its-ends/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

The defect is located, not suspected: three named methods read one of the two ends they were given,
and a fourth method in the same repository already reads both. The requirement is therefore the rule
that already exists, applied where it is missing, which leaves no room for the goal to shift during
execution. The live evidence points both ways deliberately -- eleven equips with the item on the
first end, one take-off with it on the second -- so the conditions are stated symmetrically and do
not depend on discovering a further pattern in how the client chooses.

What execution decides rather than discovers is where the rule lands: once before dispatch, or three
times inside the paths that lack it. Both have the same acceptance boundary, so this is a local
implementation choice, not an unresolved requirement.

The uncertainty that matters is not the fix but its blast radius. This turns a set of requests that
are refused today into requests that succeed and write to the store, across every pair of lists the
dispatch serves -- and each of those pairs has admission rules that were written expecting the item
to arrive from a particular side.

## Marginal Control Value

The control worth its time here is the reverse enumeration, and it is worth it because this
repository has failed at exactly this before: a previous result's change list missed thirteen new
production refusals, because the check followed the change's origin downstream and then leaned on a
full green suite -- which is precisely what a path no check reaches looks like. The same shape is
present again, one direction over: paths that used to refuse will now proceed.

A spec is worth writing because "the served pairs, in both orders" has to be pinned to something a
reader can falsify rather than to a list someone typed. A plan is not: there is one change, in one
file, with one already-written rule to follow, and no ordering or dependency for a plan to resolve.

An independent review of the complete change is the second control that earns its place, for the
same reason the enumeration does -- it is the step that catches a list checked against itself.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: One acceptance boundary -- which moves are carried out and what the store then holds.
  Nothing inside it can be accepted or rolled back separately from the rest.

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-bugfix`
- Rationale: The acceptance boundary and the execution direction are both settled -- one rule, already
  written elsewhere in this repository, applied where it is missing -- so the control that earns its
  time is not planning but the complete-change review that catches an under-reported blast radius.
  Lite buys exactly that: a durable Spec pinning what "the served pairs, in both orders" means, a
  direct implementation, and an independent review of the whole result before fresh verification.
  Standard would add a durable Plan, and there is nothing for it to sequence: one change in one file
  with one existing rule to follow, no ordered dependencies, and no early choice that could
  propagate before review could cheaply correct it. Governed would add independent challenge and
  audit-strength evidence; this touches no safety, credential, privacy, financial, or irreversible
  external boundary, and the durable-data path it does touch is an ordinary inventory write inside a
  transaction with a proven rollback boundary, so ordinary review plus fresh verification is
  adequate. The `code-bugfix` Profile fits because this is a demonstrated failure with a located
  cause, and its `systematic-debugging` and `test-driven-development` bindings are what turn the
  reverse enumeration from a promise into checks that fail when the rule is wrong.
