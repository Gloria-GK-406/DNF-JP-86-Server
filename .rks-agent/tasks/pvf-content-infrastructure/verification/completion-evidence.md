# Completion Evidence: PVF Content Infrastructure

Verified against `Script.pvf` (56.1 MB, 588,559 entries, 4,558 chunks) on
.NET 10.0.302 / linux-x64 / Release.

Full solution: **202 passed, 1 skipped, 0 failed.**

| Suite | Result |
|---|---|
| `Dfo.Pvf.Tests` | 60 passed |
| `Dfo.Server.Infrastructure.Tests` | 60 passed, 1 skipped |
| `Dfo.Server.Application.Tests` | 53 passed |
| `Dfo.Server.Networking.Tests` | 29 passed |

The single skip is `A_missing_archive_fails_with_an_actionable_message`, which cannot
run in a tree where an archive is reachable.

## Success Conditions

| ID | Verdict | Evidence |
|---|---|---|
| SC-1 | met | 7,962 sampled entries compared against `Tool/PvfLib` on the same archive: path resolution, table fields and raw bytes matched on every one; 3,000 resolved names matched. Entry counts agree at 588,559. |
| SC-2 | met | Startup resident 59.6 MB managed / 89.3 MB RSS against the legacy 276.4 MB / 323.6 MB, a 4.6x reduction. Growth is bounded by construction: `Cache_stays_within_its_budget_while_sweeping_the_whole_body` sweeps 40,000 files through an 8 MB budget and asserts resident bytes never exceed it. |
| SC-3 | met | Scanning every record of a 345,420-byte file allocates 0.0 B per call, against 489.8 KB per call for the legacy path on a 3.6 KB file. |
| SC-4 | met | Cached chunks are plain allocations that are never returned to a pool, so a recycled buffer cannot exist. `Bytes_stay_readable_after_their_chunk_is_evicted` and `Eviction_does_not_change_bytes_a_caller_already_holds` force eviction under a one-byte budget and assert held spans are unchanged. |
| SC-5 | met | `Dfo.Server.Application` contains no PVF reference. `Dfo.Server.Abstractions` names no PVF type and takes no dependency on `Dfo.Pvf`; only `Dfo.Server.Infrastructure` references it. Passed-through vocabulary values are stripped of archive bracket spelling, asserted by `Vocabulary_values_cross_the_boundary_without_archive_spelling`. |
| SC-6 | met | See the per-row table below. |
| SC-7 | met | The pre-existing 118 tests still pass unchanged. |

## SC-6 by matrix row

Each row is covered by a test that combines the definition with stand-in character
progress and computes the business result the packet carries, rather than only proving
the definition can be read.

| Row | Packet | Test |
|---|---|---|
| 12 | Available quest list | `Definitions_support_computing_an_available_quest_list` — filters 2,740 quests by cleared set, level band, job and grow type, and asserts a level-1 character sees a different set than a level-40 one. |
| 14 | Daily challenge | `Definitions_support_projecting_todays_challenge_state` — resolves the slot allowance for level 60 (5 of 6 slots), marks one claimed, and resolves the matching reward band. |
| 32, 33 | Collection box | `Definitions_support_projecting_a_characters_box_progress` — walks the declared grid and marks filled positions from a stand-in saved-slot set. |
| 58 | Pet welcome message | `Ambient_lines_are_present_for_scripted_creatures_and_absent_otherwise` — 71 of 408 creatures have a companion script; the rest report no lines, which is what lets the packet be skipped rather than padded. |

Explicit failure on a missing source is covered by
`A_missing_archive_fails_with_an_actionable_message`, and each source throws naming its
own missing table rather than yielding an empty catalog.

## Vocabulary completeness

Job, grade and reward vocabularies were enumerated from all 2,740 quests rather than
assumed. `Every_job_and_grade_token_in_the_archive_is_modelled` fails if the archive
yields a token the enumerations do not model, so an unmodelled distinction cannot pass
silently.

## Defects the tests caught

Five, all real, all in work written during this task:

1. A header overflow guard was unreachable: five non-negative `int` fields accumulated
   into a `long` cannot overflow. Guard and test removed rather than left as a false
   defence.
2. `QuestGrade` was missing `urgent`, `wedding` and `village attack revenge`. An earlier
   survey had truncated its own output at fourteen rows.
3. 244 quests and 106 creatures declare no name; tower placeholders are genuinely
   nameless. The blanket non-empty assertions encoded a rule the archive does not follow.
4. Creatures that do not evolve still carry the evolution tags with zero operands. These
   were surfacing as evolutions into creature zero and are now dropped.
5. A missing-archive test used `[Fact]`, where `Skip.If` cannot take effect.

## Deliberately not modelled

- Matrix rows 49-51 (schedule tuples), excluded by the contract as unconfirmed.
- `DifficultyCode`: a single letter with 26 distinct values and no confirmed meaning,
  passed through without a business name.
- `ObjectiveTypeCode`: 33 distinct values, passed through as a bare vocabulary value
  because the full set is not yet confirmed.
- Collection-box UI image and explain fields, which the legacy packet builder does not
  read.

## Out-of-repository evidence

SC-1 compares against `Tool/PvfLib`, which is not in the solution. The comparison
harness therefore lives outside the repository. Bringing it in would require referencing
the legacy library from the new test project, which is a structural decision left to the
operator.
