---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Both rosters draw what is worn

## Why and Outcome

A player who dresses a character sees it dressed on the character-selection screen and sees it in its
creation clothes on the roster inside the entry burst. The two frames answer the same question from
different places, and one of those places has not been written to since the character was made.

The outcome is that both answer from what the character wears, through the one projection that already
exists for it, and that whatever stops reading `characters.appearance_blob` is accounted for rather than
left behind.

## The Two Paths as They Are

Read from this repository at `77ff3236`.

| | Character-selection frame | Entry burst's roster frame |
|---|---|---|
| Built by | `CharacterRosterService.ListByAccountWithAppearanceAsync` | `SelectCharacterPreparationService.BuildRosterEntryAsync` |
| Appearance from | `ReadWornByAccountAsync` -> `DrawnAppearance.Of` | `DecodeAppearances(entry.Public.Appearance.EncodedEntries)` |
| That source is written | continuously, by wearing things | once, by `CharacterEntryTables.InsertAsync` at creation |

`BuildRosterEntryAsync` receives the roster from `PrepareAsync`, which obtained it from
`characters.GetAccountRosterAsync` -- the cheap listing, whose summaries carry a null appearance.

## What the Cheap Listing Was Protecting, and From What

`CharacterRosterService` was split in two so that working out an appearance -- which means reading
everything every character on the account wears, and can fail on its own -- would not sit on paths where a
moment's trouble reading equipment would stop something unrelated. Its remarks name those paths: resolving
a roster position, counting slots before a creation, finding a character by name before a deletion, and the
entry burst's check that the selected character is on the roster.

**That reasoning does not extend to the entry burst's roster frame, and this Spec says so plainly rather
than tiptoeing around it.** `BuildRosterEntryAsync` already calls `entries.LoadAsync` once per character on
the account, in a loop, to get each one's progress values. The path therefore already performs `1 + N`
loads against the same store before any appearance is worked out, and a failure in any of them already
stops entry. Adding one account-scoped read of worn rows does not introduce a failure mode this path did
not have; it adds one more read to a path that is already the expensive one.

What must not happen is the reverse: making the *presence check* -- `GetAccountRosterAsync` at the top of
`PrepareAsync`, which only asks whether the selected character is on the roster -- start doing the
appearance read. That check is one of the four the split exists for.

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | The entry burst's roster frame draws each character's appearance from what that character wears, through `DrawnAppearance`. |
| RQ-002 | The appearance in that frame and the appearance in the character-selection frame come from one projection, so that neither can be changed without the other following. |
| RQ-003 | The roster presence check at the top of `PrepareAsync` keeps using the listing that works nothing out. Its cost and failure surface do not change. |
| RQ-004 | The effect on the 62 hashes in `SelectCharacterAcceptedTranscriptTests` is measured. Whatever the measurement is, it is reported frame by frame with the reason. |
| RQ-005 | After the change, the readers of `characters.appearance_blob` and of `DecodeAppearances` are enumerated, and each is kept or retired according to what that enumeration finds. |
| RQ-006 | Every refusal and admission path the change adds, removes or alters is enumerated in both directions from the code, naming the frontiers the walk crossed. |
| RQ-007 | Repository records the change falsifies are corrected, including the remarks in `CharacterRosterService` and `DrawnAppearance` that describe the two frames as disagreeing. |
| RQ-008 | No frame's field layout changes and no frame is added. Values inside the existing appearance block may change; its shape may not. |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | Given a character created wearing one thing and then dressed in another, when the entry burst is prepared, the roster frame's appearance names what it wears now. A check that would pass equally against the stored blob does not satisfy this: the two must differ in the fixture. |
| AC-002 | RQ-001 | A character wearing nothing is drawn wearing nothing -- an empty appearance, not a null one and not a stale one. |
| AC-003 | RQ-002 | A mutation of `DrawnAppearance` reddens checks on both frames. A mutation of one call site reddens only that side; that is expected and is not what this criterion asks for. |
| AC-004 | RQ-003 | `GetAccountRosterAsync` is still what `PrepareAsync` calls for the presence check, and the count of store reads it performs is unchanged. |
| AC-005 | RQ-004 | The anchor run is reported with every changed hash named, its old and new value given, and the reason stated. If no hash changes, the report says so **and** explains what about the transcript's fixture makes both sources agree -- silence is not a result. |
| AC-006 | RQ-005 | The enumeration of readers is derived from the code with its method stated, and covers readers reached across a dispatch boundary or through a registry as well as direct callers. The decision that follows names what was found. |
| AC-007 | RQ-005 | If the decoder is retired, `spec-001`'s six checks go with it and the record says why that is a result rather than a loss of coverage. If it is kept, those six checks are still green. |
| AC-008 | RQ-006 | The change list reports forward and reverse, derived from the code rather than from the list of intended edits. |
| AC-009 | RQ-007 | Each corrected record names what falsified it. |
| AC-010 | RQ-008 | No new `AddGame*MessageEncoder` or `ProtocolModule` registration, and no change to any frame's field widths or order. |
| AC-011 | all | The suite passes against the 2398 / 2 / 0 baseline, with counts stated and the output searched for `[FAIL]`, `error CS` and `MSB<digit>`. Any failure is identified by exception type and stack root before it is judged. |
| AC-012 | all | At least one mutation reddens a check through a value mismatch rather than a raised exception. |

## Success Evidence

- EV-001 (AC-001..AC-004): the new and existing checks over both frames.
- EV-002 (AC-003, AC-012): the mutation record, with what each mutation changed, which cases reddened, and
  **how** each reddened.
- EV-003 (AC-005): the byte-anchor account.
- EV-004 (AC-006, AC-007): the reader enumeration and the recorded decision.
- EV-005 (AC-008): the change list.
- EV-006 (AC-011): the retained run output.

## Assumptions

- The transcript fixture's effect on the anchor is unknown at the time of writing. Both outcomes -- hashes
  move, hashes do not move -- are acceptable results of a correct change; only an unexplained one is not.
- `spec-001`'s checks are the control for whether this change altered decoding behaviour it did not intend
  to. If the decoder survives, they must stay green without being edited.
