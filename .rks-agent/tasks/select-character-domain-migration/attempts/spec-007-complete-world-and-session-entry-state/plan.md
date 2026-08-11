---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Complete World, Content and Session Entry State

## Implementation Approach

The Spec's core outcome is a selection result whose world/content and session-adjacent
limbs are genuinely loaded. Its necessary support is a channel identity, a town/area source
and registries that exist; its peripheral assurance is the record of constant and
unconfirmed items plus the boundary vocabulary check.

The earliest true core slice is content composition. The four content sources already exist
and are verified, so T-001 replaces the `GameWorldEntryState` placeholder with real content
state without waiting on anything. No task precedes it.

Support then lands where the core work first consumes it: the town/area source immediately
before spawn needs it (T-002), channel identity immediately before admission and entry
policy derive from it (T-003, T-004). The session-adjacent leg follows as three vertical
slices, each carrying its own registry and views, ordered so the shared presence registry is
settled once (T-005) before the two views that reuse its listener/area scoping (T-006,
T-007). Peripheral assurance closes at T-009 through T-011, after the core is composable.

Registrations are wired inside the task that introduces each component rather than deferred
to a single wiring task, so no task leaves the solution unbuildable.

Existing `TimeProvider` injection and the established Abstractions/Data/Infrastructure
layering are reused; no new layering convention is introduced.

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Data/Characters/CharacterModels.cs` | Replace the `GameWorldEntryState` placeholder and expand `CharacterSessionEntryState` into the composed world and session-adjacent limbs | Existing entry-state records | Domain records for world content, entry policy and session-adjacent views |
| BD-002 | `Server/src/Dfo.Server.Abstractions/Content/` | Add a town and area definition source interface and its boundary records | Existing content-source conventions | `ITownDefinitionSource` and town/area/spawn-anchor records |
| BD-003 | `Server/src/Dfo.Server.Infrastructure/Content/` | Implement the town source over the archive | `Dfo.Pvf` reader, existing PVF source conventions | Archive-backed town definition source |
| BD-004 | `Server/src/Dfo.Server.Abstractions/Channel/IChannelSources.cs` | Add channel identity and admission abstractions alongside the existing channel catalog | Listener port, character level | Channel identity resolver and admission outcome records |
| BD-005 | `Server/src/Dfo.Server.Application/Channel/` | Implement channel identity, admission and entry policy as Application rules | Channel identity, town definitions, character level and persisted town | Admission outcome, spawn, persistence decision, town-entry permission, PvP applicability |
| BD-006 | `Server/src/Dfo.Server.Abstractions/Sessions/` | Add session-adjacent query interfaces and their read-view records | Character identity, listener, town and area | Town-presence, PvP-lobby, expert-store and dungeon-rejoin query interfaces and views |
| BD-007 | `Server/src/Dfo.Server.Infrastructure/Sessions/` | Implement the runtime registries backing those queries | Registry entries written by tests; no production writer yet | Presence, PvP-lobby and dungeon-rejoin registries with read-only projections |
| BD-008 | `Server/src/Dfo.Server.Application/Characters/CharacterEntryService.cs` | Compose the world/content and session-adjacent limbs into the selection result before publication | Content sources, entry policy, session queries | Complete `SelectCharacterResult` |
| BD-009 | `Server/src/Dfo.Server.Application/Commands/Game/SelectCharacterCommand.cs` and `Server/src/Dfo.Server.Application/Game/SelectCharacterHandler.cs` | Carry the resolved channel identity into command execution | `NetworkConnectionContext.ListenerPort` | Command carrying channel identity |
| BD-010 | `Server/src/Dfo.Server.Application/ApplicationModule.cs` and `Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs` | Register the new sources, rules, registries and queries | New components | Resolvable service graph |
| BD-011 | `Server/tests/Dfo.Server.Application.Tests/` and `Server/tests/Dfo.Server.Infrastructure.Tests/` | Add the tests the Spec's evidence classes require | Spec acceptance scenarios | Passing focused tests |
| BD-012 | `Docs/Refactor/SelectCharacterWorldEntryCoverage.md` | Record which items are carried as constant or unconfirmed and why | Migration matrix rows 17, 31, 34, 49-53, 56 | Reviewable coverage record |

## Ordered Tasks

### T-001: Content state composed into the selection result

- Covers: RQ-001, RQ-002, RQ-017
- Boundaries: BD-001, BD-008, BD-010, BD-011
- Inputs: the four delivered content-source interfaces; existing `SelectCharacterResult`
- Outputs: world content state present in the result at publication time

#### Steps

1. Replace the `GameWorldEntryState` placeholder with records expressing the **computed**
   results the listed rows carry — the available quest list, the daily challenge state, the
   two collection-box projections and pet welcome applicability — keeping them siblings of
   character state.
2. Express those records in `Dfo.Server.Data` without referencing the content-source
   records. `Dfo.Server.Data` is the leaf project with no project references, and the
   content records live in `Dfo.Server.Abstractions`, which depends on Data; the dependency
   cannot be inverted. Application performs the mapping, which is the same pattern the
   accepted character and account entry states already use.
3. Combine the four content sources with the already-loaded character progress in the entry
   composition, and populate the computed state before the active character is published.
4. Register any new Application-side dependency needed for the composition.
5. Add tests asserting the computed results reflect the specific character's progress, using
   progress that makes a different answer correct than a default character would get.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet test Server/86JP.Server.slnx -c Release --nologo` | Solution builds with zero warnings and all tests pass |
| CK-002 | Non-command: read the composition and confirm content state is populated before the publication call | Population precedes publication in the same method |
| CK-003 | Non-command: confirm each of rows 12, 14, 32, 33 and 58 has a test computing its business result from the composed state | One test per row, each asserting a computed result rather than a definition read |

### T-002: Town and area definitions resolve a spawn

- Depends on: T-001
- Covers: RQ-003, RQ-004
- Boundaries: BD-002, BD-003, BD-010, BD-011
- Inputs: `town/town.lst`, referenced town files and `map/*` in the archive
- Outputs: a town definition source and a resolved spawn anchor

#### Steps

1. Add a town and area definition source interface with boundary records for a town, its
   areas and a spawn anchor, using domain vocabulary only.
2. Implement it over the archive, reading the town list, the referenced town file and the
   map entry the gate area points at.
3. Fail explicitly, naming the town or area that could not be resolved, when the list entry,
   town file, gate area or map anchor is missing or malformed.
4. Register the source in the infrastructure module.
5. Add tests resolving a known town's gate against the real archive, resolving the default
   town when the persisted town is unset, and failing explicitly for an absent definition.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `dotnet test Server/86JP.Server.slnx -c Release --nologo --filter FullyQualifiedName~Town` | Town source tests pass against the real archive |
| CK-005 | Non-command: confirm the failure message names the unresolved town or area | Message identifies the specific input |

### T-003: Channel identity and admission

- Depends on: T-001
- Covers: RQ-005, RQ-006
- Boundaries: BD-004, BD-005, BD-009, BD-010, BD-011
- Inputs: `NetworkConnectionContext.ListenerPort`; character level
- Outputs: channel identity resolved before command execution; an accept-or-refuse outcome

#### Steps

1. Add a channel identity abstraction distinguishing the level-restricted channel, the
   free-duel channel and whether that channel is enabled, resolved from a listener port.
2. Resolve channel identity in the select-character message handler and carry it on the
   command.
3. Implement admission as an Application rule: refuse a character below the level-restricted
   channel's minimum level, carrying the legacy error code and operator-facing message;
   accept otherwise.
4. Make a refusal end the entry before anything is published.
5. Register the resolver and rule.
6. Add paired admitted and refused tests, the refusal asserting both the stated reason and
   that no publication occurred.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet test Server/86JP.Server.slnx -c Release --nologo --filter FullyQualifiedName~Admission` | Admission tests pass |
| CK-007 | Non-command: confirm the refusal path performs no publication | No publish call is reachable on the refusal path |

### T-004: Entry policy derived from channel identity

- Depends on: T-002, T-003
- Covers: RQ-007
- Boundaries: BD-001, BD-005, BD-008, BD-011
- Inputs: channel identity; town definitions; persisted character town and dungeon permission
- Outputs: spawn, position-persistence decision, town-entry permission, PvP applicability

#### Steps

1. Derive from channel identity: whether the spawn is transient, whether the resolved
   position is persisted, whether a target town may be entered, and whether the PvP skill
   profile is applicable.
2. Express PvP-profile applicability so that inapplicable is distinguishable from applicable
   and empty.
3. Add tests contrasting the level-restricted channel with an ordinary channel for spawn
   transience, persistence and town-entry permission, and the free-duel channel with an
   ordinary channel for PvP applicability.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-008 | `dotnet test Server/86JP.Server.slnx -c Release --nologo --filter FullyQualifiedName~EntryPolicy` | Entry-policy tests pass |
| CK-009 | Non-command: confirm inapplicable PvP profile is a distinct state from an empty one | The two are distinguishable without inspecting collection length |

### T-005: Presence registry with town-presence and expert-store views

- Depends on: T-003
- Covers: RQ-009, RQ-012
- Boundaries: BD-001, BD-006, BD-007, BD-008, BD-010, BD-011
- Inputs: character identity, listener, town and area
- Outputs: a presence registry and two read views over it

#### Steps

1. Add a presence registry holding entries keyed by character and indexed by listener, town
   and area, with a read-only projection.
2. Add the town-presence view returning the other occupants of the entering character's area
   on the entry listener.
3. Add the expert-store view returning the store-area occupants for the entry listener and
   whether the entering character owns an open store.
4. Exclude the entering character from both views.
5. Register the registry and both queries, and compose the views into the result.
6. Add populated and empty tests for both views, plus scope tests proving a different area
   and a different listener are excluded.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-010 | `dotnet test Server/86JP.Server.slnx -c Release --nologo --filter FullyQualifiedName~Presence` | Presence and expert-store view tests pass |
| CK-011 | Non-command: confirm both views exclude the entering character | Self-exclusion asserted in a test rather than assumed |

### T-006: PvP lobby view

- Depends on: T-005
- Covers: RQ-010
- Boundaries: BD-001, BD-006, BD-007, BD-008, BD-010, BD-011
- Inputs: channel identity; the PvP lobby registry
- Outputs: a lobby view reporting participants and rooms, or inapplicable

#### Steps

1. Add a PvP lobby registry holding ready participants and published rooms per listener.
2. Add the lobby view returning both for the entry listener, excluding the entering
   character.
3. Report inapplicable, distinct from empty, when the entry channel is not an enabled
   free-duel channel.
4. Register the registry and query, and compose the view into the result.
5. Add populated, empty and inapplicable tests.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-012 | `dotnet test Server/86JP.Server.slnx -c Release --nologo --filter FullyQualifiedName~PvpLobby` | Lobby view tests pass, including the inapplicable case |

### T-007: Dungeon rejoin registry and read-only view

- Depends on: T-005
- Covers: RQ-011
- Boundaries: BD-001, BD-006, BD-007, BD-008, BD-010, BD-011
- Inputs: account, character and participant identity; the rejoin registry
- Outputs: a rejoin view distinguishing no offer from each anomaly, altering no state

#### Steps

1. Add a rejoin registry holding attachment entries with an owning identity, an attachment
   state, a party and participants.
2. Add the rejoin view returning the offer when the identity matches, the entry is detached
   and the party is usable.
3. Report no offer, identity mismatch, unusable party and unexpected attachment state as
   four distinct outcomes.
4. Perform no idle extension, expiry or timer replacement during the lookup; record in the
   view's own documentation that these legacy side effects are deliberately excluded as
   lifecycle concerns.
5. Register the registry and query, and compose the view into the result.
6. Add one test per outcome, plus a repeated-consultation test asserting registry state is
   identical afterwards.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-013 | `dotnet test Server/86JP.Server.slnx -c Release --nologo --filter FullyQualifiedName~Rejoin` | All four outcome tests and the idempotent-consultation test pass |
| CK-014 | Non-command: confirm the lookup path contains no state mutation | No assignment to registry state on the read path |

### T-008: Registry existence guaranteed for every runtime view

- Depends on: T-005, T-006, T-007
- Covers: RQ-013
- Boundaries: BD-007, BD-010, BD-011
- Inputs: the three registries and four views
- Outputs: an empty result that provably means consulted-and-empty

#### Steps

1. Ensure each view's registry is resolvable from the service graph independently of whether
   anything was written to it.
2. Add a test that resolves the production service graph and consults all four views with
   nothing written, asserting each returns an explicit empty or inapplicable result rather
   than failing or yielding a fabricated value.
3. Ensure a view whose registry cannot be resolved fails rather than reporting empty.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-015 | `dotnet test Server/86JP.Server.slnx -c Release --nologo --filter FullyQualifiedName~RegistryExistence` | Every view returns explicit empty or inapplicable from an unwritten registry |
| CK-016 | Non-command: confirm an unresolvable registry produces a failure rather than an empty view | Missing registry is an error path, not an empty result |

### T-009: Constant and unconfirmed baselines recorded

- Depends on: T-004
- Covers: RQ-014
- Boundaries: BD-001, BD-011, BD-012
- Inputs: migration matrix rows 17, 31, 34, 49-53 and 56
- Outputs: declared constant and unconfirmed items, and their coverage record

#### Steps

1. Carry the fixed baselines of rows 17, 31, 34, 53 and 56 as declared constants,
   distinguishable by inspection from loaded state.
2. Carry rows 49 through 52 as unconfirmed items with no business name.
3. Write the coverage record listing each item, its matrix row, its classification and the
   evidence for that classification.
4. Add a test asserting no unconfirmed item is exposed under a business name.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-017 | Non-command: read the coverage record against the matrix rows it claims to cover | Every listed row appears with a classification and its basis |
| CK-018 | `dotnet test Server/86JP.Server.slnx -c Release --nologo --filter FullyQualifiedName~Unconfirmed` | The naming test passes |

### T-010: Failure before publication for every required source

- Depends on: T-002, T-005, T-006, T-007, T-009
- Covers: RQ-015
- Boundaries: BD-008, BD-011
- Inputs: each required source class
- Outputs: explicit pre-publication failure

#### Steps

1. Ensure every required source is consulted before the active character is published.
2. Ensure a missing or malformed required source raises rather than yielding partial state.
3. Add one failure test per required source class, each asserting the failure occurred and
   nothing was published.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-019 | `dotnet test Server/86JP.Server.slnx -c Release --nologo --filter FullyQualifiedName~FailsBeforePublication` | Each required source class has a passing failure test |

### T-011: Boundary vocabulary and full verification

- Depends on: T-001, T-002, T-003, T-004, T-005, T-006, T-007, T-008, T-009, T-010
- Covers: RQ-016
- Boundaries: BD-001, BD-002, BD-004, BD-006, BD-011
- Inputs: the complete change
- Outputs: proof that archive vocabulary stops at the infrastructure boundary, and a full run

#### Steps

1. Inspect the composed result, Abstractions and Application for archive-format vocabulary
   and remove any that leaked.
2. Confirm no project above Infrastructure references the format library.
3. Run the full suite with the archive configured and record any changed assertion with its
   justification.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-020 | `grep -rn "Pvf" Server/src/Dfo.Server.Abstractions Server/src/Dfo.Server.Application Server/src/Dfo.Server.Networking --include=*.cs \| grep -v /obj/` | No match |
| CK-021 | `grep -rln "Dfo.Pvf" Server/src --include=*.csproj` | Only the infrastructure project matches |
| CK-022 | `dotnet test Server/86JP.Server.slnx -c Release --nologo` | All tests pass with zero warnings |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001 | BD-001, BD-008 | CK-001, CK-003 |
| RQ-002 | T-001 | BD-008 | CK-002 |
| RQ-003 | T-002 | BD-002, BD-003 | CK-004 |
| RQ-004 | T-002 | BD-003 | CK-004, CK-005 |
| RQ-005 | T-003 | BD-004, BD-009 | CK-006 |
| RQ-006 | T-003 | BD-005 | CK-006, CK-007 |
| RQ-007 | T-004 | BD-001, BD-005 | CK-008, CK-009 |
| RQ-009 | T-005 | BD-006, BD-007 | CK-010, CK-011 |
| RQ-010 | T-006 | BD-006, BD-007 | CK-012 |
| RQ-011 | T-007 | BD-006, BD-007 | CK-013, CK-014 |
| RQ-012 | T-005 | BD-006, BD-007 | CK-010, CK-011 |
| RQ-013 | T-008 | BD-007, BD-010 | CK-015, CK-016 |
| RQ-014 | T-009 | BD-001, BD-012 | CK-017, CK-018 |
| RQ-015 | T-010 | BD-008 | CK-019 |
| RQ-016 | T-011 | BD-001, BD-002, BD-004, BD-006 | CK-020, CK-021, CK-022 |
| RQ-017 | T-001 | BD-001 | CK-002 |

## Risks and Rollback

- Risk: the four runtime views have no production writer until the entry lifecycle is
  migrated, so a passing suite does not demonstrate a populated production path. Mitigation
  is T-008, which proves the registries are resolvable and their emptiness is a consulted
  result, and the Spec's explicit assumption recording the limit.
- Risk: the town and map structures may not parse with the delivered format library, which
  would make T-002 a bounded problem of its own rather than a step. Mitigation is to attempt
  T-002 early, immediately after the first core slice, so the finding arrives before the
  session-adjacent legs are built on the assumption that it succeeded.
- Risk: carrying channel identity on the command changes a command shape that the
  just-completed dispatch refactor constrains at compile time. Mitigation is that the
  constraint is a compile-time one, so a mismatch fails the build rather than escaping.
- Rollback: the attempt is unaccepted until its single gated commit, so the bounded rollback
  is a reset of the task worktree to baseline `86ba115` with no other path touched.

## Assumptions

- The archive sits at this worktree's root, where the parent-directory search finds it with
  no configuration, so the command checks above need no environment variable. It is
  gitignored and stays untracked. Were it absent, archive-backed checks would skip rather
  than fail, so its presence is itself a precondition of honest evidence.
- The delivered content sources are consumed unchanged.
- The legacy implementations in the main worktree are the reverse-engineering authority for
  admission, spawn, presence, lobby, expert store and rejoin behavior.
