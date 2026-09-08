---
kind: rks-verification
decision: pass
---

# Verification: spec-003 "The selection screen shows what is worn"

## The claim being evaluated

That the change in the task worktree, uncommitted on `dressing-a-character-and-seeing-it` over
`ce6839ed`, satisfies the child TaskContract's C-1 through C-8 — that the character-selection
screen draws each character wearing what that character wears, by the same rule the in-world frame
uses, in one place.

## What ran

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
in the task worktree, `tasklist` first confirming no `Dfo.Server.Host` process.

Exit code 0. **2392 passed / 2 skipped / 0 failed**: Pvf 60, Protocol 612, Networking 52,
Persistence 333, Host 6, Infrastructure 259 (+2 skipped), Application 1070. Baseline at `ce6839ed`
was 2384 / 2 / 0. The independent reviewer ran the same command three times and reached the same
counts.

**One unclassified failure is on the record and is not being explained away.** During implementation
a single run reported `PersistentContainerBoundaryTests.InsideAUseCaseItAccepts` failing. Its
exception type and stack root were not kept, so it **cannot** be matched against the known flaky
signature and is not claimed as it. It has not recurred in the several full runs since, by the
executor or by the reviewer. Full output is now retained for any future occurrence.

## Condition by condition

| ID | Condition | Evidence | Met |
|---|---|---|---|
| C-1 | Changing what a character wears changes what the selection screen draws | `ChangingWhatACharacterWearsChangesWhatTheRosterDraws`, against a real SQLite store through the real roster service. Mutation M3 (revert the drawing listing to the blob) reddens it. | yes |
| C-2 | The selection screen and the in-world frame draw the same character the same way | `BothScreensDrawTheSameCharacterTests` compares the two part for part rather than against literals. | yes |
| C-3 | A bare character is drawn bare, and characters do not borrow each other's appearance | `TheRosterDrawsWhatEachCharacterWearsAndNothingWhenBare`, two characters on one account, one of them bare. | yes |
| C-4 | The rule exists in one place, **structurally** | Both sides reach `DrawnAppearance`. Proved by mutation, not by assertion: moving `LastDrawnSlot` reddens cases on both sides at once, while the check that compares the two screens *to each other* stays green — which is why AC-003 pins each side separately as well. The reviewer reproduced this independently. | yes |
| C-5 | Every production path added or changed is listed forward and reverse, from the code | `behavior-change-list.md`. **Two rounds of blocking findings were needed to get here** and both are recorded rather than smoothed over: the first version made four unrelated request paths pay a read they never use, and the second missed that character creation is a second path that does. | yes |
| C-6 | No protocol frame added, no frame's bytes changed | `NetworkMessages.cs`, `SelectCharacterAcceptedTranscriptTests.cs`, `SelectCharacterPreparationService.cs` and both architecture test files are absent from `git diff --name-only ce6839ed`. No message-type member is added (grep count 0). The in-world frame's bytes are unchanged, checked field width by field width by the reviewer and held by the anchor, which mutation M2 reddens. | yes |
| C-7 | The suite is no weaker than its baseline | 2392 / 2 / 0, exit 0, against 2384 / 2 / 0. | yes |
| C-8 | Records corrected, and the two screens now disagreeing written down | Four search commands stated, run and read. Five records corrected: `CharacterDomainCut.md`, `CharacterEntryFacesInventory.md` (two places), `ProtocolScaffoldDissonances.md`, `NOTI-0x0002.md`, plus two line-number citations this change falsified (`DungeonEntryNotes.md`, `AGENTS.md`). The disagreement — selection screen derived, entry burst still on the blob — is written into `CharacterEntryFacesInventory.md` with the reason the boundary is where it is. | yes |

## What the two review rounds changed, and why it is worth recording

**Round one** returned `changes_required` on scope, not on correctness: the new account-scoped read
had been placed on a listing four unrelated paths already used, so entry, character creation,
deletion and roster-position resolution all inherited a read they never consume. It was narrowed to
the one path that draws, with the summary's appearance made nullable so that handing the cheap
listing to the drawing screen fails loudly instead of quietly putting the whole account back on
screen undressed.

While writing the check for that, the executor **disproved half of the controller's own stated
reason**: the harm quoted (a wrong-length or null item core failing an account's whole listing) is
refused by the table's own `CHECK` constraints and is unreachable. It said so rather than keeping a
justification whose main clause it had just falsified. The narrowing stands on what survives — a
momentarily busy store failing a login, a creation and a deletion; and four callers not depending on
a read they never use.

**Round two** returned `changes_required` because the change list had missed that creating a
character pushes a roster refresh through the command bus and therefore *does* take the drawing
listing. This is the third consecutive result in this task to under-report a newly reachable path,
and the three share one root: the enumeration only covered the side the change started from. The
remedy is not the corrected entries but the corrected method, which is now the change list's opening
section — walk up from the change and, at every frontier, ask what symbol the next caller up
actually writes down; when the answer is "nothing, a registry resolves it", go and read the
registry. The reviewer then showed that the executor's table of frontier kinds was itself
incomplete, one of the missing kinds being on this very path, and the rule was generalised to the
question rather than the table.

## Not verified here

**Nothing here is live evidence.** Whether the selection screen now draws the costumes is the
owner's to see.

**The two screens disagree by design and that is not a defect being hidden.** The selection screen
derives; the roster frame inside the entry burst still reads the blob, because changing it moves the
byte anchor and it is not the screen that was reported. It is recorded in
`Docs/CharacterEntryFacesInventory.md` so the next reader finds it stated rather than discovers it.

**One coverage loss is on the record.** Replacing the check that asserted a hand-written blob comes
back out removed the only place a non-empty blob was decoded. It is written into the change list as
lost in this round, kept apart from the pre-existing gap that `DecodeAppearances`'s two refusals
have never had a check of their own.
