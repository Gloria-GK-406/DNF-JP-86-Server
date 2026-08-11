---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Complete World, Content and Session Entry State

## Why and Outcome

Character selection currently produces a business result whose world/content and
session-adjacent limbs are placeholders: one holds a timestamp, the other an identity pair.
Every remaining group in the migration waits on them — Networking cannot project the G5
packets from state that does not exist, and the integrated entry lifecycle cannot be
assembled on top of an absent world view.

The previous attempt at this outcome produced working code and was still refused, because
its content was empty and no honest reading of the result could call it complete. That is
the outcome that must change. Afterwards, selection yields world/content and
session-adjacent state that a consumer can act on, where everything present was genuinely
loaded, everything absent is distinguishable from empty, and everything whose meaning is
unconfirmed says so rather than borrowing a business name.

## Task Perspective

- Core outcome: a selected character's business result carries the world, content and
  session-adjacent state a consumer needs, composed before publication, with each value
  either loaded from a real source or explicitly marked as constant or unconfirmed.
- Necessary support: a channel identity for the listener, so admission, spawn, position
  persistence and PvP applicability are decided rather than assumed; a town and area
  definition source, without which spawn cannot be resolved at all; and runtime registries
  that genuinely exist, so that an empty read view means "consulted and empty" rather than
  "not implemented".
- Peripheral assurance: the record of which items are carried as constant or unconfirmed,
  and the check that no archive vocabulary is observable outside the infrastructure
  boundary.

## Scope and Non-Goals

### In Scope

- World and content state for migration matrix rows 12, 14, 32, 33 and the content portion
  of row 58, composed into the selection result.
- Town and area definitions sufficient to resolve a selected character's spawn.
- Channel identity for a listener, and the admission outcome derived from it.
- Spawn resolution, position-persistence decision, town-entry permission and PvP-profile
  applicability, all derived from channel identity.
- Read views over town presence, PvP lobby, dungeon rejoin and expert-store membership.
- Declared handling of the fixed and unconfirmed protocol baselines in rows 17, 31, 34,
  49 through 53, and 56.
- Explicit failure, before publication, when a required source is missing or malformed.

### Non-Goals

- Encoding, ordering or emitting any packet.
- Registering, replacing, cleaning up or otherwise mutating session state; this Spec
  supplies the query side and the registries, not their writers.
- Character-owned and account-owned progress, already delivered.
- Assigning business meaning to any archive field or protocol slot whose meaning is not
  confirmed.
- Content categories beyond those the listed rows require.

## Constraints

- An empty read view is honest only when the registry it consulted exists. A view that
  reports emptiness because its source is absent is fabricated state and is not acceptable.
- A value that is genuinely inapplicable is distinguishable from an applicable value that
  loaded as empty, and both are distinguishable from a load failure, which remains an error.
- World, content and session-adjacent values are siblings of character state in the result,
  never fields of it.
- The result carries answers, not ingredients. A consumer of the result must not have to
  combine static definitions with character progress to obtain what a packet carries,
  because that combination is business logic and the consumer is Networking.
- Archive vocabulary is not observable outside the infrastructure boundary.
- Protocol claims are code-reversed; no packet capture is available, and no claim may be
  presented as capture-confirmed.
- Accepted character-owned and account-owned behavior is unchanged.

## Definitions

| Term | Meaning |
|---|---|
| Channel identity | What a listener is, as far as entry policy is concerned: whether it is the level-restricted channel, whether it is the free-duel channel, and whether it is enabled |
| Admission outcome | The decision, made before anything is published, that a selected character either may enter on this listener or is refused with a stated reason |
| Read view | A projection of runtime state that answers a question without altering the state it read |
| Registry | Server-owned runtime state that exists for the lifetime of the server and can be consulted whether or not anything has yet been written to it |
| Constant baseline | A protocol value the legacy server emits unconditionally, independent of any loaded state |
| Unconfirmed item | A protocol slot or archive field whose meaning is not established by current evidence |

## External Behavior and Data Rules

- Selection on the level-restricted channel refuses a character below that channel's
  minimum level, and the refusal carries the same error code and operator-facing message the
  legacy server sends. Nothing about the selection is published when it is refused.
- Spawn on the level-restricted channel is transient and its position is not persisted; on
  any other channel the resolved position is persisted. A character whose persisted town is
  unset spawns at the default town.
- The PvP skill profile is applicable only on the free-duel channel; on any other channel it
  is inapplicable rather than empty.
- The dungeon-rejoin read view is read-only. The legacy lookup extends an idle deadline and
  can expire a stale entry as a side effect of being asked; those transitions belong to the
  entry lifecycle, not to a query, and are deliberately not reproduced here. This is a
  recorded deviation from the legacy grouping, not an omission.
- A dungeon-rejoin lookup distinguishes having no offer, which is an ordinary outcome, from
  an identity mismatch, an unusable party or an unexpected attachment state, which are
  anomalies.
- Town presence, PvP lobby and expert-store views are scoped to the listener the character
  entered on, and exclude the entering character.
- The PvP lobby view is inapplicable when the free-duel channel is not the entry channel or
  is not enabled.
- A constant baseline and an unconfirmed item are both distinguishable, by inspection of the
  composed state, from a value that was loaded.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | The selection result carries the already-computed business results for matrix rows 12, 14, 32 and 33 and the content portion of row 58, so that a consumer needs no further combination of static definitions with character progress. |
| RQ-002 | Content state is composed before the selection is published. |
| RQ-003 | Spawn for a selected character is resolved from town and area definitions obtained from the archive. |
| RQ-004 | A missing or malformed town or area definition fails explicitly and names what was missing. |
| RQ-005 | A listener resolves to a channel identity before the selection command executes. |
| RQ-006 | Admission produces either an accepted outcome or a refusal carrying the legacy error code and message, and a refusal publishes nothing. |
| RQ-007 | The position-persistence decision, town-entry permission and PvP-profile applicability are each derived from channel identity, and PvP-profile applicability reports inapplicable rather than empty off the free-duel channel. |
| RQ-008 | Withdrawn on direct evidence. It required dungeon permission to combine persisted character permission with a channel admission overlay, following the migration matrix's description of row 18. The legacy projection takes no channel or listener input at all — the search for either term in it returns nothing — and the permissions come straight from the character's own persisted rows. Row 18 therefore has no world or channel limb, and what remains of it is character-owned data already delivered. Implementing the described overlay would have invented behaviour the legacy server does not have. |
| RQ-009 | The town-presence view returns the other occupants of the selected character's area on the entry listener, and returns empty when there are none. |
| RQ-010 | The PvP lobby view returns the existing ready participants and the published rooms for the entry listener, and reports inapplicable when the entry channel is not an enabled free-duel channel. |
| RQ-011 | The dungeon-rejoin view distinguishes no offer from an identity mismatch, an unusable party and an unexpected attachment state, and alters no registry state when consulted. |
| RQ-012 | The expert-store view returns the store-area occupants for the entry listener and whether the selected character owns an open store. |
| RQ-013 | Each of the four runtime views consults a registry that exists independently of whether anything has been written to it, so an empty result means consulted-and-empty. |
| RQ-014 | Rows 17, 31, 34, 49 through 53 and 56 are carried as declared constant or unconfirmed items, distinguishable by inspection from loaded state, and no unconfirmed item carries a business name. |
| RQ-015 | A missing or malformed required source fails before publication rather than producing partial state. |
| RQ-016 | No archive-format vocabulary is observable outside the infrastructure boundary. |
| RQ-017 | World, content and session-adjacent values are siblings of character state in the result, not fields of it. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001 | A selected character whose progress makes a different answer correct than a default character would get, and the archive available | Selection completes | The result already states the available quest list, the daily challenge state, both collection-box projections and whether a pet welcome line applies, each reflecting that character's progress rather than the raw catalog |
| AC-002 | RQ-002, RQ-017 | A character being selected | Selection completes | The content state is present in the result at publication time, as a sibling of character state rather than a field of it |
| AC-003 | RQ-003 | A character whose persisted town is a known town, and the archive available | Selection resolves the spawn | The spawn town, area and position match that town's gate |
| AC-004 | RQ-003 | A character whose persisted town is unset | Selection resolves the spawn | The spawn is the default town's gate |
| AC-005 | RQ-004 | A town identifier with no definition, or a definition with no gate area | Selection resolves the spawn | The failure is explicit and names the town that could not be resolved |
| AC-006 | RQ-005, RQ-006 | A character below the minimum level entering on the level-restricted channel | Selection runs | It is refused with the legacy error code and message, and nothing is published |
| AC-007 | RQ-005, RQ-006 | A character at or above the minimum level entering on the level-restricted channel | Selection runs | It is admitted and the result is published |
| AC-008 | RQ-007 | The level-restricted channel and an ordinary channel | Selection resolves entry policy on each | The restricted channel yields a transient spawn whose position is not persisted, the ordinary channel yields a persisted position, and town-entry permission differs accordingly |
| AC-009 | RQ-007 | The free-duel channel and an ordinary channel | Selection resolves PvP-profile applicability on each | It is applicable on the free-duel channel and inapplicable, not empty, on the other |
| AC-011 | RQ-009, RQ-013 | A registry holding other characters in the selected character's area on the same listener, and another holding none | Selection composes the town-presence view | The first returns those occupants excluding the entering character, the second returns empty |
| AC-012 | RQ-009 | A registry holding characters in a different area, or on a different listener | Selection composes the town-presence view | Those characters are absent from the view |
| AC-013 | RQ-010, RQ-013 | An enabled free-duel listener with existing ready participants and published rooms, and the same listener with neither | Selection composes the PvP lobby view | The first returns those participants and rooms, the second returns empty |
| AC-014 | RQ-010 | An entry channel that is not an enabled free-duel channel | Selection composes the PvP lobby view | The view reports inapplicable rather than empty |
| AC-015 | RQ-011, RQ-013 | A rejoin registry with a valid detached offer for the selected identity, and one with no entry | Selection composes the rejoin view | The first returns the offer with its party and participants, the second reports no offer |
| AC-016 | RQ-011 | A rejoin registry whose entry belongs to a different identity, has an unusable party, or is not detached | Selection composes the rejoin view | Each is reported as its own distinct anomaly, separate from no offer |
| AC-017 | RQ-011 | A rejoin registry with a valid offer | The rejoin view is consulted twice | The registry state is identical after the first and second consultation |
| AC-018 | RQ-012, RQ-013 | A registry with other characters in the store area and an open store owned by the selected character, and a registry with neither | Selection composes the expert-store view | The first returns those occupants and reports the owned store, the second returns empty and reports none |
| AC-019 | RQ-014 | The composed world state | It is inspected | Each of the listed rows is identifiable as a declared constant or an unconfirmed item, and no unconfirmed item carries a business name |
| AC-020 | RQ-015 | A required content source that is absent or malformed | Selection runs | The failure occurs and nothing is published |
| AC-021 | RQ-016 | The composed result and the layers above the infrastructure boundary | They are inspected for archive vocabulary | No archive-format concept appears |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002, RQ-017 | AC-001, AC-002 | Tests computing each row's business result from the composed state and stand-in progress, plus review of the result's shape |
| EV-002 | RQ-003, RQ-004 | AC-003, AC-004, AC-005 | Tests resolving spawn against the real archive for a known town and for an unset town, plus an explicit-failure test |
| EV-003 | RQ-005, RQ-006 | AC-006, AC-007 | Paired admitted and refused tests, the refusal asserting both the stated reason and that nothing was published |
| EV-004 | RQ-007 | AC-008, AC-009 | Tests contrasting channel identities for spawn transience, position persistence, town-entry permission and PvP applicability |
| EV-005 | RQ-009, RQ-010, RQ-012, RQ-013 | AC-011, AC-012, AC-013, AC-014, AC-018 | Populated and empty tests for each view, plus scope tests proving listener and area filtering |
| EV-006 | RQ-011 | AC-015, AC-016, AC-017 | Tests for each distinct rejoin outcome, plus a repeated-consultation test proving the registry is unchanged |
| EV-007 | RQ-014 | AC-019 | Review of the composed state together with the recorded list of constant and unconfirmed items |
| EV-008 | RQ-015 | AC-020 | A failure test per required source class, each asserting failure precedes publication |
| EV-009 | RQ-016 | AC-021 | Dependency and vocabulary inspection above the infrastructure boundary |
| EV-010 | All | All | Full solution test run with the archive configured, reported together with any changed assertion and its justification |

## Assumptions

- The four delivered content sources are the authoritative interface for quest, daily
  activity, collection-box and creature definitions and are consumed rather than reworked.
- The runtime registries created here have no writer until the entry lifecycle is migrated,
  so their production result is empty; their behavior is proven by consulting them directly
  in both populated and empty states.
- Matrix rows 49 through 51 carry per-character persisted data whose character-owned limb is
  already accepted; only their unconfirmed status is established here.
- The archive is excluded from version control and must be placed where the workspace can
  reach it. When it is not reachable, archive-backed checks skip rather than fail, so its
  reachability is a precondition of honest verification rather than a convenience.
