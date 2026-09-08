# Routing Decision

- TaskContract: `.rks-agent/tasks/every-frame-draws-the-current-character/attempts/spec-003-a-restated-move-does-not-undo-itself/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

The outcome is observable and the direction is known. A discriminator exists in the frame itself -- the
client names the item on the end it is coming from, and a restatement therefore names an end the store
disagrees with -- and the value that carries it is already decoded, merely dropped at the command boundary.
The mirror of a guard that already exists is what is being added.

What is uncertain is deliberately in Assumptions rather than left implicit: nobody has observed a
restatement, so whether one carries the original bytes is a premise. The contract makes both outcomes
acceptable and requires the premise to be labelled, so the uncertainty changes what gets written down
rather than what gets built.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: One acceptance boundary -- either a restated move stops undoing itself without any legitimate
  drag being refused, or the finding that it cannot be told apart -- and one rollback boundary independent
  of the outcome that follows. The Direct failure path here is sharper than in the previous two outcomes
  and this repository has produced it before: adding a refusal to a shared path, enumerating only what the
  change was aimed at, and shipping refusals nobody listed. The earlier round of this same work reported
  thirteen new production refusals only after the fact. Worse here, a refusal that misfires does not
  produce a stack trace -- it silently declines something the player did on purpose, which reads as the
  server ignoring them. The controls that catch it are an acceptance boundary that names the misfire cases
  before implementation (C-2, C-3, C-6), a complete-result review by someone who did not write the change,
  and fresh verification.
- Reroute conditions: If no signature can be found that separates a restatement from a fresh drag without
  guessing, the outcome ends as a recorded finding with no code change; that is a legitimate result, not a
  block, and it does not affect the remaining outcome. If separating them turns out to need the client
  measured -- a capture of an actual restatement -- stop and return to the root map rather than inventing
  the shape of one, because that needs the owner at a running client. If the guard cannot be written
  without also reaching the creature-and-earring case of issue #13, stop: that judgement was added at this
  spot once and removed for that reason, and reintroducing it is a different outcome.

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-bugfix`
- Rationale: The failure this has to survive is a refusal that fires on something legitimate, and what
  catches that is the acceptance boundary naming the misfire cases before implementation plus a
  complete-result review -- both of which Lite configures. Standard's distinctive control is a durable Plan
  resolving coupled decisions before work starts, and the previous outcome earned it because the seam it
  needed was a real choice with propagating consequences. Here there is no such choice: the discriminator
  is forced by what the client sends, the value that carries it is already decoded and needs only to reach
  the same method that already holds its mirror, and there is one call site. A Plan would record a sequence
  that has no branches. Governed's controls protect against escaped high-consequence failure; the worst
  outcome here is a costume that does not come off, in a path with no credential, no external system, no
  irreversible mutation and no data-loss route, so no hard floor applies. `code-bugfix` because this is a
  defect with a demonstrated mechanism, even though the triggering frame has never been observed.
