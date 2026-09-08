---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Both rosters draw what is worn

## Approach

`BuildRosterEntryAsync` stops decoding the stored blob and asks `DrawnAppearance` instead, from one
account-scoped read of what every character on the account wears. The projection and the read both already
exist and are what the character-selection frame uses, so nothing new is designed -- the entry path is
pointed at the answer that already has an owner.

The order matters and is not cosmetic. The seam is settled first, because everything after it is measured
against it; then the change; then the measurement of the anchor, which is a fact to be discovered rather
than a step to be completed; then the readers of what is left behind; then the enumeration and the records.

## Decisions Before Work

**DP-001 -- Where the entry path gets what each character wears: a read inside `BuildRosterEntryAsync`,
not a switch of the roster listing.**

Three ways were available.

| Option | What it does | Why not / why |
|---|---|---|
| Switch `PrepareAsync`'s `GetAccountRosterAsync` to `GetAccountRosterWithAppearanceAsync` | The presence check starts carrying appearances | **Rejected.** That call is one of the four the cheap listing exists for. Its job is to answer whether the selected character is on the roster, and making it work out appearances puts the equipment read behind a question that does not need it. |
| Give `BuildRosterEntryAsync` the worn rows, read once for the account | One extra read on a path that already performs `1 + N` | **Chosen.** |
| Derive appearance per character from each `CharacterEntryState` already loaded in the loop | No new read at all | **Rejected unless the entry state already carries worn items in a form `DrawnAppearance` accepts.** To be checked against live code as CH-001; if it does, it is strictly better and the plan takes it. |

The chosen option's cost is stated exactly rather than waved at: `BuildRosterEntryAsync` already calls
`entries.LoadAsync` once per character on the account. The path performs `1 + N` store loads before any
appearance exists, and a failure in any of them already stops entry. One account-scoped read of worn rows
adds a read to the path that is already the expensive one; it does not give entry a failure mode it lacked.

**DP-002 -- The stored column and its decoder are not removed as part of the same step that stops reading
them.** The change lands first, the readers are enumerated after, and removal is a separate decision made
from that enumeration. Removing them in the same motion would make the enumeration a formality performed
on code that is already gone.

## Work and Validation

| ID | Covers | Work and boundary | Check and expected result |
|---|---|---|---|
| T-001 | RQ-001, RQ-003 | Settle DP-001 against live code (CH-001): does `CharacterEntryState` already carry the character's worn items in a shape `DrawnAppearance.Of` accepts? Record the answer either way. Touch nothing yet. | The answer, with the type and member named, in the change list. |
| T-002 | RQ-001, RQ-002 | In `SelectCharacterPreparationService.BuildRosterEntryAsync`, replace `DecodeAppearances(entry.Public.Appearance.EncodedEntries)` with `DrawnAppearance.Of(...)` over what that character wears, obtained by the seam T-001 settles. `PrepareAsync`'s `GetAccountRosterAsync` call is untouched. | Builds; `CommandHandlerArchitectureTests` and `NotificationHandlerArchitectureTests` still pass. |
| T-003 | RQ-001 | A check that creates a character wearing one thing, dresses it in another, prepares the entry burst, and asserts the roster frame names what it wears now. The fixture's two answers must differ, so the check cannot pass against the stored blob. | AC-001. Fails before T-002, passes after. |
| T-004 | RQ-001 | A check that a character wearing nothing yields an empty appearance rather than a null or stale one. | AC-002. |
| T-005 | RQ-002 | A convergent check: one mutation of `DrawnAppearance` reddens cases on both frames. Not two checks pinning the same expected bytes -- those stay green when both sides are broken together. | AC-003, AC-012. The mutation record names which cases reddened on each side and how each reddened. |
| T-006 | RQ-003 | Prove the presence check is unchanged: `PrepareAsync` still calls `GetAccountRosterAsync`, and the number of store reads that call performs is the same. | AC-004. |
| T-007 | RQ-004 | Run the byte anchor and compare all 62 hashes element-wise against the committed list. Report every change with old value, new value and reason. **If none changes, establish why** -- inspect the transcript fixture's worn items and stored blob and say what makes both sources agree. | AC-005. A result either way; an unexplained result is a blocker, not a pass. |
| T-008 | RQ-005 | Enumerate every reader of `characters.appearance_blob` and of `DecodeAppearances` after T-002. Walk up from each and ask what symbol the next caller up actually writes down: an ordinary method gives its name; a command handler gives the **command record's** type name; a message handler gives a type resolved in `ProtocolModule`; "nothing -- a registry resolves it" means going and reading that registry. Walk down too: a value constructed here reaches an encoder that runs later. | AC-006. The enumeration with its method and the frontiers crossed. |
| T-009 | RQ-005 | From T-008's result, keep or retire the column and the decoder, and record the decision with what was found. If retiring needs a migration of existing saves, **stop and report to the root map** -- that is outside this attempt. | AC-007. If the decoder goes, `spec-001`'s six checks go with it and the record says why that is a result. |
| T-010 | RQ-006 | The forward-and-reverse change list, derived from the code. | AC-008. |
| T-011 | RQ-007 | Correct the records this falsifies: the remarks in `CharacterRosterService.ListByAccountWithAppearanceAsync` and in `DrawnAppearance` that say the two frames disagree, `Docs/CharacterEntryFacesInventory.md`, and any count stated in prose. | AC-009. Each correction names what falsified it. |
| T-012 | RQ-008, all | Full suite; output kept and searched for `[FAIL]`, `error CS`, `MSB<digit>`; no new encoder registration; no frame layout change. | AC-010, AC-011. 2398 + added cases / 2 / 0. |

T-002 consumes T-001's answer. T-007 and T-008 consume T-002's change. T-009 consumes T-008. Nothing else
is ordered beyond running the suite last.

## Choices During Implementation

- Where the new checks live. The frames are built in `Application` and written in `Protocol`; the
  convergent check for T-005 has to reach both, and which project can see both decides where it sits.
- Whether the worn rows are read once for the account or joined into an existing read. Both satisfy
  DP-001; the live shape of `ICharacterItemTable` decides.

## Risks

- **The anchor may not move at all.** If the transcript's character wears nothing, both sources agree and
  every hash is unchanged. That would make the change invisible to the strongest check this repository has,
  which is why T-003 builds a fixture whose two answers differ instead of relying on the anchor.
- **`spec-001`'s checks may become checks on dead code.** That is anticipated by its own sealed result and
  is a legitimate outcome of T-009, not a regression to be avoided.
