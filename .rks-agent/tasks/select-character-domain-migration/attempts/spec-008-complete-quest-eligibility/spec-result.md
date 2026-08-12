---
kind: rks-spec-result
state: completed
---

# Spec Result: Complete G5 Quest Eligibility

G5 quest eligibility is complete and independently approved. `AvailableQuests` now carries
the deterministic offerable answer rather than a partially filtered catalogue.

## Delivered

- A 16-predicate source/disposition matrix grounded in the legacy selectable-quest loop and
  the supplied 86JP archive.
- Neutral typed restrictions for exposure, event, creature, expert job, training, target
  character, exact job declarations, grow branches and prerequisite relationships.
- Strict first-authoritative ID, client range, malformed relationship and optional-versus-
  declared validation at the PVF boundary.
- Complete Application projection over cleared flags, prerequisite alternatives, answer
  flags, collisions, repeatable grades, job/grow semantics and creature evolution viability.
- Failure-before-publication preservation and updated honest G5 coverage.

## Verification

Fresh full solution: 378 passed, 2 expected archive-present skips, 0 failed. Focused quest
source, creature source, world rule and select-character workflow checks pass; dependency
and diff gates pass. Independent complete code review is approved.

## Execution-time Correction

The real `TrainingQuest.lst` contains no level-to-NPC operands, and the exact legacy reader
produces an empty mapping. The implementation therefore keeps the exact mapping rule but
honestly offers no training quest for this archive rather than fabricating NPC values.

## Successor Guidance

G6 may now project the complete G1-G5 result. G7 remains responsible for session registry
writes, lifecycle mutations, target resolution, synchronization and rollback.
