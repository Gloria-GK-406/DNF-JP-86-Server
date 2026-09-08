---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Both rosters draw what is worn

## Delivered

- The roster frame inside the entry burst draws each character's appearance from what that character
  wears, through the same listing the character-selection frame asks
  (`ICharacterDomainService.GetAccountRosterWithAppearanceAsync` -> `DrawnAppearance`). It no longer
  decodes `characters.appearance_blob`.
- The roster presence check at the top of `PrepareAsync` still uses the cheap listing, which is one of the
  four call sites that listing was split off to protect.
- A summary whose appearance is null is refused rather than drawn as a character wearing nothing.
- Three checks in `SelectCharacterProductionStateTests`, which runs the production path, and one existing
  check renamed because its name asserted something this work falsified.
- `behavior-change-list.md`: three forward consequences, three reverse, two wire-value entries, three
  moved check expectations, the two decisions this attempt was required to make, and the mutation record.
- Four repository records corrected.

## Verification

- `verification/completion-evidence.md` -- 2401 passed / 2 skipped / 0 failed against a 2398 / 2 / 0
  baseline, condition-by-condition outcomes for C-1..C-8, and the boundary proofs.

## Repository Result

- Changed paths: eight tracked files (one production file with a behaviour change, two with corrected
  remarks, three check files, one document, the root map) plus this attempt's untracked artifact folder.
- Baseline or accepted result: built on `77ff3236`.
- Parent rollback required: no.

## Successor Guidance

- **`characters.appearance_blob` now has no reader at all.** It is still written once at creation by
  `CharacterRosterService.Appearance` and still loaded into `CharacterEntryState.Public.Appearance` by two
  SELECTs, and nothing consumes it. Retiring it means the schema and `CharacterBodyShapeTests`' field list,
  which is a migration of existing saves. **Retire the whole unit -- column, its writer, the two loads,
  `Public.Appearance`, the decoder, and `spec-001`'s six checks -- or none of it.** Taking one link out
  leaves the rest and splits the work in two.
- **A corrupt stored blob can no longer stop a player entering the world.** The decoder's two refusals are
  unreachable from production. If that is later judged wrong, the place to restore it is not this frame.
- **The byte anchor does not protect the entry roster frame and never did.** `AccountRosterStateMessage` is
  a `SupplementalSynchronizationMessage` built at `SelectCharacterHandler.cs:354`, outside the factory the
  anchor encodes. Anything that changes what that frame carries needs its own check;
  `SelectCharacterProductionStateTests` is that check.
- **The entry path reads the account's summaries twice per entry**, once for the presence check and once
  for the drawn listing. The window between them is recorded and not closed: a character deleted in
  between passes the check and is absent from the frame.
- **Two counting mistakes in this attempt shared one cause and it will recur.** Both times a set was
  assumed rather than derived -- once by adopting a claim from someone else without checking its premise,
  once by counting carefully inside a class set that came from two stack traces rather than from the
  failure list. The arithmetic was never the problem. When a record states a count, derive the set first
  and write the command that derives it.
- **A green suite after a source-of-truth change means nothing until a check exercises the new source.**
  This change was invisible to all 2398 existing checks once two stand-ins were taught to answer; the
  three new checks are what make it visible.

## Artifacts

- `spec.md`, `plan.md` (see `DP-001` for the seam decision and why the alternatives were rejected)
- `behavior-change-list.md`
- `reviews/change-review.md` -- `approved`, after two rounds of `changes_required` carrying six blocking
  findings between them.
