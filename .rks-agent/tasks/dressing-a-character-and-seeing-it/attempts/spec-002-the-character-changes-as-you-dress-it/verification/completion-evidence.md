---
kind: rks-verification
decision: pass
---

# Verification: spec-002 "The character changes as you dress it"

## The claim being evaluated

That the change in the task worktree, uncommitted on `dressing-a-character-and-seeing-it` over
`e766d971`, satisfies the child TaskContract's C-1 through C-8 — that a move which changed what the
character is wearing tells the client so in the same exchange, that one which did not says nothing
extra, and that what it reports is the store's answer rather than the session's.

It claims nothing about the character-selection roster, which is defect C.

## What ran

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
in the task worktree, `tasklist` first confirming no `Dfo.Server.Host` process.

Exit code 0. **2384 passed / 2 skipped / 0 failed**: Pvf 60, Protocol 608, Networking 52,
Persistence 329, Host 6, Infrastructure 259 (+2 skipped), Application 1070. Baseline at `e766d971`
was 2375 / 2 / 0; the difference is the nine checks this work adds, all in
`Dfo.Server.Protocol.Tests` (599 → 608). The independent reviewer ran the same command
independently and reached the same counts.

## Condition by condition

| ID | Condition | Evidence | Met |
|---|---|---|---|
| C-1 | After a successful move that changed an appearance slot, the client is sent what the character now wears, in the same exchange as the acknowledgement | `Putting_a_costume_on_tells_the_client_what_the_character_now_wears` and `Taking_a_costume_off_tells_the_client_the_character_is_no_longer_wearing_it` assert two frames in one delivery, the acknowledgement first and the appearance carrying the post-move equipment. Mutation M1 (send the acknowledgement alone again) reddens three cases and nothing else. | yes |
| C-2 | A successful move that changed nothing the appearance is drawn from does not send it | Four checks: inside the bag, between bag and avatar page, onto worn slot 22, and after a refusal. The reviewer's own mutation — making the trigger always true — reddened the three success cases and showed their acknowledgements carry no error code and a move value of one, so those moves really do succeed rather than passing because they were refused. | yes |
| C-3 | A refused move sends nothing beyond its refusal | `A_refused_move_sends_the_refusal_alone`. | yes |
| C-4 | The appearance reported is what the store holds after the move | `The_frame_follows_the_store_and_not_the_session_copy` seeds the session's copy with a different costume from the store's. Mutation M2 (keep the load, use the session copy) reddens it. | yes |
| C-5 | Every production path added or changed is listed forward and reverse, from the code | `behavior-change-list.md`: 1 forward, 7 reverse, 4 shape-only, 4 mutations, 3 fixture changes, 2 record annotations, 2 left standing. **R-7 was missing from the first draft and was found by the independent review**, in this repository's recurring shape — a newly reachable path a green suite cannot see. It is now listed with that provenance stated. | yes |
| C-6 | No protocol frame added, no frame's bytes changed | The production diff since `e766d971` contains no occurrence of `ClientMessageType` or `ServerMessageType` (grep count 0). `SelectCharacterAcceptedTranscriptTests` passes and its file is not in the change set. The appearance filter's expression is unchanged in meaning — `<= 12 \|\| == 23` before and after, now through two named constants. | yes |
| C-7 | The suite is no weaker than its baseline | 2384 / 2 / 0, exit 0, against 2375 / 2 / 0. | yes |
| C-8 | Records this work falsifies are corrected | Search set `protocol/`, `Docs/`, `AGENTS.md` for claims about what follows a move and when subtype zero is sent — run and read. Two records saying 「后面什么都不跟」 were annotated rather than overturned: read against their own subject, an ordinary shoulder at equipment slot 15, neither was falsified, and the reviewer confirmed slot 15 is outside every run subtype zero reads. | yes |

Also held: `CommandHandlerArchitectureTests` and `NotificationHandlerArchitectureTests` pass and
neither file is in the change set. The build carries `TreatWarningsAsErrors` and produced 0 warnings
and 0 errors. The `MoveItemSpace` slice's new reference to `StateSynchronization` follows twelve
production files that already did so before this work — the reviewer counted them.

## What the review changed

The first submission was returned `changes_required` on one blocking defect, and it is worth
recording rather than smoothing over: the appearance was built **before** the acknowledgement was
sent, and building it reads the store after the transaction has closed. A raise there took the
acknowledgement with it — and the client holds the inventory shut until it is answered, which is the
symptom this whole work exists to remove. The comment written beside that path justified it with a
false claim about ordering ("the move itself has already been acknowledged"), which is what let the
mistake through.

Both are fixed: the build is guarded so a failure leaves the acknowledgement alone and logs at
error, and the comment now says what actually happens. Mutation M3 (guard removed) reddens exactly
the one new check. The re-review is `approved`.

## Not verified here

**Nothing here is live evidence.** Whether the character visibly changes as a costume goes on is
the owner's to re-test, together with defect A's take-off. Defect C — the character-selection
screen — is expected to remain until its own Spec lands.

**One new blind spot is accepted and recorded.** A real storage failure while building the
appearance now shows up as a character that did not change, rather than as a lost acknowledgement.
It is logged at error with the connection id, so it is findable rather than silent, and it is
written up as R-7.
