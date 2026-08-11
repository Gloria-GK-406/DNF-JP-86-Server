# Completion Evidence: Complete World, Content and Session Entry State

Verified on .NET 10 / linux-x64 / Release, warnings-as-errors, **0 warnings**.

**322 passed, 2 skipped, 0 failed** (baseline `86ba115`: 204 passed, 1 skipped).

| Suite | Baseline | Now |
|---|---:|---:|
| `Dfo.Server.Application.Tests` | 55 | 164 |
| `Dfo.Server.Infrastructure.Tests` | 60 (+1 skip) | 69 (+2 skip) |
| `Dfo.Pvf.Tests` | 60 | 60 |
| `Dfo.Server.Networking.Tests` | 29 | 29 |

No existing test was deleted or had an assertion weakened. The second skip is the new
town-source counterpart of the existing missing-archive test: both can only run where no
archive is reachable, and an archive is reachable here.

`Script.pvf` is present at the worktree root, so the archive-backed tests ran rather than
skipped. This matters: without a reachable archive they skip silently, and the run would be
green while proving nothing about real content.

## Success Conditions

| ID | Verdict | Evidence |
|---|---|---|
| SC-1 | met | Rows 12, 14, 32, 33 and the row-58 content portion are computed into the result. Proven at two levels: rule tests against controlled definitions, and six tests driving the real archive through the production registration. `A_high_level_character_is_offered_a_different_quest_set_than_a_low_level_one` and `Two_jobs_are_offered_different_quest_sets` show the answer depends on the character, not just that a catalog was read. |
| SC-2 | met | `An_ordinary_arrival_resolves_against_the_real_archive` resolves town 1's gate from the archive. Absent town, absent gate, absent map, wrong area kind and no walkable neighbour each fail explicitly and name the input. |
| SC-3 | met | `A_character_below_the_minimum_level_is_refused_the_restricted_channel` gives code `0xFD`; `CharacterEntry_RefusedByTheChannelPublishesNothing` proves the previously published character is untouched. |
| SC-4 | met | Contrasting-channel tests over spawn transience, position persistence, town-entry permission and duelling applicability. |
| SC-5 | met | Each of the four views has a populated and an empty case, plus scope tests. `RegistryExistenceTests` proves the registries resolve from the production graph and that a write is visible to the next read; the rejoin tests prove reading does not write. |
| SC-6 | met | `Docs/Refactor/SelectCharacterWorldEntryCoverage.md` classifies every listed row. Fourteen reflection guards fail if a composed-state member is ever named after an unconfirmed slot or a protocol constant. |
| SC-7 | met | One failure test per required source class introduced here, each asserting nothing was published. |
| SC-8 | met | `Pvf` appears zero times across Abstractions, Application, Networking and Data. Only `Dfo.Server.Infrastructure` references the format library. |

## The geometry rule, checked against reality rather than itself

The restricted channel's arrival point is computed, not read: the exit rectangle's four
sides are offset by a clearance, points outside the walkable area are discarded, and the
nearest survivor wins. A rule like this can be subtly wrong and still return a plausible
coordinate.

It was therefore pinned twice. The expected point was worked out by hand from the archive's
own numbers — exit at (340, 218) sized 30 by 120, so centre (355, 278) and candidates
(332, 278), (378, 278), (355, 210), (355, 346), of which only the second falls inside a
walkable rectangle — and asserted as **(378, 278)** both against a hand-built map and
against the real archive. A wrong clearance or a wrong side changes the answer, so the
agreement is meaningful rather than tautological.

## Findings that changed the work

| Finding | Effect |
|---|---|
| The town area kind arrives as a quoted value, and a `[minimap rect]` block of four integers sits between the map path and it | First implementation read the rectangle's corner as the spawn position. Gates parsed to (45, 188) instead of (474, 234) — a plausible coordinate, silently wrong. Fixed and guarded. |
| Row 18's "admission overlay" does not exist | `DungeonPermissionProjector` takes no channel or listener; the search returns nothing. RQ-008 withdrawn on evidence rather than satisfied by inventing the overlay. |
| The legacy rejoin lookup writes while reading | Idle renewal and lapse retirement are lifecycle transitions, deliberately not reproduced. Two tests prove the read leaves the registry alone. |
| 58 of 151 town areas are unrecognised, and 6 of 25 towns have no gate | Both are real content. Blanket assertions to the contrary were the test's error, not the code's, and the gateless towns make the explicit-failure path genuinely reachable. |
| `Dfo.Server.Data` is a leaf project | The composed state cannot reference the content records, so Application maps between them — the same pattern the accepted entry states already use. |

## Not covered

- **No independent reviewer.** The review was performed by the implementing agent. The
  previous task used a fresh reviewer, which found a defect the implementer had missed; that
  assurance is absent here.
- **The four runtime views are empty on the production path**, because nothing admits to
  their registries until the entry lifecycle is migrated. Their behaviour is proven by
  writing to the registries directly and by proving those registries are the ones the
  production graph resolves. An end-to-end populated path is a later group's outcome.
- **Malformed content outside `byte` or `short` range** surfaces as a checked-arithmetic
  failure rather than the task's own failure type. It still fails before publication; only
  the message is less specific. No town in the archive triggers it.
- **The wire participant identifier** is not part of the ownership check on returning to a
  dungeon, because this layer has no such identifier yet.
- **No packet capture.** Every protocol claim is reversed from the legacy code.
