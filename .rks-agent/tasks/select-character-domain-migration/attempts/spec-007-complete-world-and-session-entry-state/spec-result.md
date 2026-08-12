---
kind: rks-spec-result
state: completed
---

# Spec Result: Complete World, Content and Session Entry State

G5 is implemented. The selection result's world and session-adjacent limbs, which held a
timestamp and an identity pair, now carry the content answers, entry policy and runtime
views the migration matrix assigns to that group. **348 passed, 2 skipped, 0 failed, 0
warnings**, from a baseline of 204 passed.

## What was delivered

- **Content answers, not catalogs.** Rows 12, 14, 32, 33 and the row-58 content portion are
  computed against the character's own progress before publication, so no consumer has to
  combine definitions with progress itself. That combination is business logic and the
  consumer is Networking.
- **A fifth content source.** Spawn resolution needs town and area geometry, which the four
  previously delivered sources do not cover. Town list, town files and map geometry are now
  read from the archive as tokens.
- **Channel identity and admission.** A listener resolves to a channel before dispatch;
  admission refuses below-level characters on the restricted channel with the legacy code
  and message, before anything is published.
- **Entry policy.** Spawn, position persistence, town-entry permission and duelling
  applicability all follow from channel identity, including the restricted channel's
  computed transient arrival point.
- **Four runtime views over registries that exist.** Town presence, expert store, duelling
  lobby and interrupted-run return, each with a populated and an empty case, and each proven
  to consult a registry the production graph actually resolves.

## Decisions worth carrying forward

**Answers, not ingredients.** RQ-001 originally required the result to carry enough to
compute each row. That conflicts with the root constraint that Networking may not perform
business logic: shipping catalogs and progress separately would leave the combining to it.
Corrected before implementation.

**Row 18 has no G5 limb.** The migration matrix describes it as persisted permissions
transformed by a channel admission overlay. `DungeonPermissionProjector` takes no channel or
listener — searching for either term in it returns nothing — and the permissions come
straight from the character's persisted rows. RQ-008 was withdrawn on that evidence.
Implementing the described overlay would have invented behaviour.

**A read that no longer writes.** The legacy rejoin lookup renews an idle deadline and can
retire a lapsed record as a side effect of being asked. Those are lifecycle transitions; a
question must not perform them, or merely composing an entry would keep a stale run alive.
This is a deliberate deviation from the legacy grouping, and two tests hold the line.

**Registries in Application.** The plan placed them in Infrastructure. The branch's own
precedent is the opposite — the accepted online-user and active-character registries are in
Application — and these hold no external technology. Following the plan would have created
a contradictory second convention.

## Defects the work surfaced

Every one produced a plausible answer rather than a failure, which is why they needed
finding rather than debugging.

Found by the self-review:

1. **A minimap rectangle read as spawn coordinates.** Gates parsed to (45, 188) instead of
   (474, 234).
2. **The transient arrival read any area's map**, without checking the area leads out of
   town.
3. **The lobby view invented a location** for participants, defaulting to town 0 area 0 —
   both real values. Fixed by giving the lobby a record with no location to fill in.

Found by the independent reviewer, after the self-review had approved:

4. **The pet welcome could never fire.** Equipment and creature identifiers are disjoint —
   zero values in common — and the equipped item was handed straight to the creature lookup.
   No test could have caught it: every pet test injected a creature identifier and skipped
   the failing step.
5. **Area kinds were read by position**, so modifier markers were taken as kinds and their
   numbers as spawn operands. 54 of 151 areas misclassified. The completion evidence had
   already recorded that number as a property of the archive.
6. **Repeatable quests were retired after one completion**, and no grade whitelist was
   applied, so quests the legacy server never offers were being offered.
7. **Town presence filtered by listener where legacy does not**, and omitted the condition
   that keeps characters who have walked into a dungeon off the list.
8. **The expert store was read off a presence record the entering character does not have**,
   so it always answered false.

One test assertion was mine, not the code's: 6 of 25 towns declare no gate, which makes the
explicit-failure path genuinely reachable.

## What this does not deliver

- **The four runtime views are empty in production** until the entry lifecycle admits to
  their registries. That emptiness is honest — the registry was consulted and held nothing —
  and is distinguished from the previous attempt's emptiness, which had no registry behind
  it. Proven by resolving the production graph and by showing a write is visible to the next
  read.
- **The offered quest list stays narrower than the legacy rule.** Nine filters need content
  the accepted source does not carry, including the pet-evolution eligibility matrix row 12
  names as an input. Tabulated in the coverage document.
- **The self-review was not a substitute for an independent one.** It approved a change with
  two blocking defects in it. Worth carrying forward as a fact about the process, not just
  this change.
- **`CharacterEntryService` now takes ten constructor dependencies.** Coherent, each used
  once, but at the point where a further limb should prompt a split.
- **No packet capture.** Every protocol claim is reversed from the legacy code.

Artifacts: `spec.md`, `plan.md`, `reviews/change-review.md`,
`verification/completion-evidence.md`, and `Docs/Refactor/SelectCharacterWorldEntryCoverage.md`.
