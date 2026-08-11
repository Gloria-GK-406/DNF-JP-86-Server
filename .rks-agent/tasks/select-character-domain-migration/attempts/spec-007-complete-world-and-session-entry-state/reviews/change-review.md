---
kind: rks-change-review
contract:
  path: ../TaskContract.md
  revision: 1
verdict: approved
---

# Change Review: Complete World, Content and Session Entry State

## Reviewer independence

**This was a self-review by the implementing agent, not an independent one.** The prior
task in this workspace used a fresh reviewer with no context, which caught a real defect the
implementer had missed. No such reviewer was engaged here, so the assurance this review
provides is weaker in exactly the way that comparison suggests: it cannot catch what the
implementer's own model of the change is blind to. Recorded rather than glossed, because the
verdict below should be read with that discount applied.

## Verdict

**Approved**, after two defects found during review were fixed and covered.

## Defects found and fixed

### F-1 — the transient arrival read whatever map the configured area pointed at

The restricted channel computes its arrival point from the geometry of an area that leads
out of town. The implementation looked the area up by identifier and went straight to its
map without checking what kind of area it was.

The legacy resolver requires the area to be a dungeon gate. Without that check, pointing the
configuration at an ordinary area would still produce a coordinate — the map would parse,
some rectangle would be found, an arrival would be computed — and the character would be
placed somewhere they do not belong, with nothing downstream to question it. A wrong
coordinate is not a visibly wrong coordinate.

Fixed by requiring the area kind and naming the kind actually found in the failure, and
covered by `An_area_that_does_not_lead_out_of_town_is_not_an_arrival_point`.

### F-2 — the lobby view invented a location for participants

The duelling-lobby view reused the town-presence record for its participants. That record
carries a town and an area, which a lobby participant does not have, so the code filled them
from the presence registry and fell back to zero when the participant was not in it.

Zero is a real town and a real area. This is precisely the fabrication the Spec's central
constraint forbids, arrived at not by carelessness but by reusing a type whose shape
demanded a value that did not exist.

Fixed by giving the lobby its own participant record carrying only the character, so there
is no field to fill in. The type now makes the fabrication unrepresentable rather than
merely avoided.

## Requirements checked against the change

| ID | Verdict | Evidence |
|---|---|---|
| RQ-001 | met | The composed content carries computed answers, not catalogs. Proven twice: rule tests against controlled definitions, and six tests running the real archive through the production registration. |
| RQ-002 | met | `CharacterEntryService.EnterAsync` populates content before `activeCharacters.Publish`. Every failure test asserts the previously published character survives. |
| RQ-003 | met | Spawn comes from the town source. `An_ordinary_arrival_resolves_against_the_real_archive` resolves town 1's gate to 474, 234 from the archive. |
| RQ-004 | met | Absent town, absent gate, absent map, wrong area kind and no walkable neighbour each fail explicitly, and each names its input. |
| RQ-005 | met | The handler resolves the channel from the listener before dispatch; the command carries it. |
| RQ-006 | met | Refusal carries error code `0xFD` and the legacy message, and `CharacterEntry_RefusedByTheChannelPublishesNothing` asserts the previously published character is untouched. |
| RQ-007 | met | Contrasting-channel tests cover transience, persistence, town-entry permission and duelling applicability. |
| RQ-008 | withdrawn | See the Spec. The overlay it required does not exist in the legacy code. |
| RQ-009 | met | Populated, empty, wrong-area and wrong-listener tests, plus self-exclusion and stable ordering. |
| RQ-010 | met | Applicable-and-empty is distinct from inapplicable; unpublished rooms and other listeners are excluded. |
| RQ-011 | met | Five distinct outcomes each have a test; `Consulting_the_registry_twice_leaves_it_unchanged` and `An_expired_run_is_still_recorded_after_being_reported_expired` prove the read does not write. |
| RQ-012 | met | Own-store and someone-else's-store are distinguished. |
| RQ-013 | met | `RegistryExistenceTests` resolves the production graph, consults all four views with nothing written, and proves a write is visible to the next read. |
| RQ-014 | met | The coverage record classifies every listed row; reflection guards fail if a member is ever named after an unconfirmed slot or a protocol constant. |
| RQ-015 | met | One failure test per required source class added here, each asserting nothing was published. |
| RQ-016 | met | `Pvf` appears zero times in Abstractions, Application, Networking and Data. Only the infrastructure project references the format library. |
| RQ-017 | met | World and session state are siblings on `SelectCharacterResult`, not fields of `ActiveCharacterState`. |

## Deviations from the plan, and why

- **Registries live in Application, not Infrastructure.** The plan placed them under
  `Dfo.Server.Infrastructure/Sessions/`. The branch's own precedent is the opposite: the
  accepted online-user and active-character registries are in Application, in `UserService`.
  They hold no external technology. Following the plan here would have created a second,
  contradictory convention.
- **Test construction moved behind a factory.** The entry service reached ten constructor
  parameters, and each new limb was breaking seven call sites. `EntryServices.Create`
  defaults the collaborators a test is not about.
- **Application's test project now references Infrastructure.** Without it the archive-to-rule
  path could only be tested with a stub on each side, which is the gap that let the previous
  attempt look complete. The Networking test project already pairs these references.

## Observations not fixed

- **`CharacterEntryService` takes ten constructor dependencies** and is now essentially a
  composition script. It is coherent and each dependency is used exactly once, but it is at
  the point where a further limb should prompt a split rather than an eleventh parameter.
  Left alone because restructuring it is not this attempt's outcome.
- **Overflow on malformed content is an `OverflowException`, not the task's own failure
  type.** A town identifier or gate coordinate outside `byte`/`short` would surface as a
  checked-arithmetic failure rather than `EntryPlacementUnavailableException`. Both fail
  before publication, so no fabricated state escapes; only the message is less specific. No
  town in the archive triggers it.
- **The wire participant identifier is absent from the ownership check** on returning to a
  dungeon, because this layer has no such identifier. Recorded in the coverage document.

## Behaviour preservation

The accepted G1 through G4 slices are untouched apart from `SelectCharacterResult` gaining
content in two limbs that previously held placeholders, and `EnterAsync` gaining the channel
it enters on. Their tests pass unchanged. The account-exclusive registration of
`SelectCharacterCommand` is unchanged, and the command's new member is a record parameter,
so the typed dispatch constraints added by the previous task still bind it at compile time.
