---
kind: rks-review
verdict: approved
---

# Root review: dressing a character, and seeing it

Reviewed change: `git diff 8247182c6e555fd22204490272b6d2ada8008508..35aaedf342ba40f01e9ccf25ce3b8b2b88b22101`
on branch `dressing-a-character-and-seeing-it`, three commits (`e766d971`, `ce6839ed`, `35aaedf3`),
62 files, +6071/-116. Scope `change`, against the root `TaskContract.md` SC-1..SC-10.

Independent root review. The three child `change-review.md` verdicts were read as context; nothing
below rests on them. Every claim in this document was re-derived from the code, from my own greps,
from my own suite run, or from my own mutations.

## Verification I performed myself

**The suite.** `tasklist | grep -i Dfo.Server` returned nothing (exit 1) before the run, so the
MSB3027 lock condition was absent.
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
in the task worktree: **2392 passed / 2 skipped / 0 failed, exit code 0**. Per-assembly, adding to
2392: Pvf 60, Networking 52, Protocol 612, Host 6, Persistence 333, Infrastructure 259 (+2 skipped),
Application 1070. The two skips are the `A_missing_archive_*` pair. Against the 2361 root baseline
that is +31 and no regression. No failure of any kind occurred, so neither the known
`ObjectDisposedException: 'SQLitePCL.sqlite3'` flake nor a recurrence of
`PersistentContainerBoundaryTests.InsideAUseCaseItAccepts` was observed.

**Two mutations**, file-level backup plus `sha256sum`, no git restoring command used. Subject file
`Server/src/Dfo.Server.Application/Characters/DrawnAppearance.cs`, hash
`de2bf90a4783861c8eb2c5793e0e70140a7cae1f5f1b1a2239eb0e037a255186` before each mutation and
re-verified identical after each restore.

| Mutation | Reddened |
|---|---|
| RM1 -- `LastDrawnSlot` 12 → 11 | Exactly 2, and both at once: `BothScreensDrawTheSameCharacterTests.The_selection_screen_draws_exactly_the_slots_the_rule_names` and `.The_in_world_frame_draws_exactly_the_slots_the_rule_names`. Nothing else in `Dfo.Server.Protocol.Tests`. |
| RM2 -- `ExpansionLength` 4 → 5 | Exactly 4: the three `BothScreensDrawTheSameCharacterTests` cases and **`SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline`**. |

RM1 is the evidence that the two screens share one rule *structurally* rather than by agreement:
moving the rule reddens both screens' checks in the same edit. RM2 is the evidence that the byte
anchor is genuinely sensitive to the extracted projection's output bytes — so the anchor passing
unmutated is real evidence of byte-equivalence for the refactor, not an anchor that simply never
looks at this code.

## SC-1..SC-10, judged individually

| SC | Verdict | Basis |
|---|---|---|
| SC-1 | **Met for the part checks can carry.** Owner live observation outstanding. | Reversal is present in all three cross-list paths and the dispatch serves six ordered pairs; `EitherEndMayBeHoldingItTests` (725 new lines) covers them. Read `PutOnAsync`/`TakeOffAsync`/`CarryAcrossAsync` in full. |
| SC-2 | **Met.** | `BothSlotsEmpty` is returned only when the reversal's second read also comes back null, in all three paths. |
| SC-3 | **Met, with one residual gap I found and record below** (Observation A). | Three change lists exist and each was arrived at by reading code; the third produced a method I re-applied to the first two (see "Reverse enumeration, re-scanned"). |
| SC-4 | **Met for the part checks can carry.** Owner live observation outstanding. | `TheCharacterChangesAsYouDressItTests` (485 lines); the trigger is correct in composition, argued below. |
| SC-5 | **Met.** | `Unchanged` is not `Moved`; `MoveAlreadyMade` is a refusal; bag/avatar-page pairs name no equipment list. |
| SC-6 | **Met for the part checks can carry.** Owner has not seen the screen. | `CharacterRosterService.ListByAccountWithAppearanceAsync` derives from `item_slots`/`items`; RM1 confirms the shared rule. |
| SC-7 | **Met on the rendered line.** The owner's acceptance of the wording is theirs alone. | Both log lines now render `from list {FromList} slot {From} to list {ToList} slot {To}`; no monitoring vocabulary, no diagnosis residue. |
| SC-8 | **Met.** | No frame added and no message-type member added — verified independently: `git diff --name-only` matches nothing under `NetworkMessages`/`MessageType`/`Messages.cs`, and `ProtocolModule.cs` is not in the diff at all, so no encoder or handler registration changed. `SelectCharacterAcceptedTranscriptTests` is absent from the diff and green, and RM2 proves it is sensitive to the one production file the refactor moved bytes through. Authorized wire changes have a per-frame account (below). |
| SC-9 | **Met.** | Reproduced by me: 2392 / 2 / 0, exit 0. |
| SC-10 | **Met.** | Eight record files changed; I read every hunk and cross-checked the two falsifiable ones. |

**On the owner-observation half of SC-1, SC-4 and SC-6:** their Required evidence names live
observation by the owner, and the owner has not re-tested. That half is unmet and cannot be made
met by any check. I do not treat "the suite is green" as substituting for it, and I do not treat its
absence as grounds for `blocked` — everything a check can establish has been established.

## The interactions between the three results

This is the layer no earlier review saw. All four questions come out clean; the reasoning is
recorded because the conclusion is not obvious.

**spec-001's new admissions against spec-002's trigger.** The trigger is
`result.Status is Moved && ChangesWhatTheCharacterLooksLike(message)`, and the predicate is decided
from the *request's* two ends:
`(SourceListType == Equipment && DrawsFrom(SourceSlot)) || (DestinationListType == Equipment && DrawsFrom(DestinationSlot))`.
`Reversed` exchanges the two ends and leaves both list numbers in the pair, so the predicate is
invariant under reversal — an OR over both ends cannot change when the ends swap. That is why
spec-001's newly-passing reversed moves get the frame in exactly the cases the forward ones do.

- *Should send, does.* Reversed take-off (empty carried cell → occupied worn slot): the message's
  destination end is Equipment at the worn slot, checked. Reversed put-on: the message's source end
  is Equipment at the worn slot, checked.
- *Shouldn't send, doesn't.* `MoveAlreadyMade` is a refusal, so `Status` is not `Moved` and no frame
  goes out — correct, since nothing changed and the client already draws what it is describing.
  Main↔Avatar reversal names no equipment list at either end.
- *No missing case.* The appearance is drawn only from the equipment container, and the dispatch
  admits an equipment-container write only through `PutOnAsync` (destination must be Equipment) or
  `TakeOffAsync` (source must be Equipment). Equipment→Equipment lands on `ListNotServed` before any
  slot is read. So a `Moved` that altered a drawn worn slot always named the equipment list at the
  end that slot sits on.
- *No spurious case beyond the one disclosed.* `PutOnAsync` refuses when the archive's slot for the
  piece is not the named destination, so a `Moved` put-on always wrote the named worn slot; take-off
  always cleared it. The only over-serve is spec-002's own disclosed slot-24 case.

**One shared rule, not two.** Verified by reading, then by RM1.
`MoveItemSpaceSlice.ChangesWhatTheCharacterLooksLike` → `StateSynchronizationPayloadCodec.CurrentUserStateDrawsFrom`
→ `DrawnAppearance.DrawsFrom`; `EncodeCurrentUserState` → `DrawnAppearance.Of`;
`CharacterRosterService.ListByAccountWithAppearanceAsync` → `DrawnAppearance.Of`. spec-002's inline
projection is gone: the encoder's loop now writes eight fields off `part`, and I checked the wire
widths field by field against `CharacterRosterAppearanceState` — `byte, int, int, byte[4], byte,
int, uint, byte` = 23 bytes, the same eight writes in the same order and the same widths as the
removed inline code. No second copy survives. `grep` for slot filters across `Server/src` returns
only the three call sites above plus the creature's `== 24` and the creation-blob writer's
`<= LastAppearanceSlot`.

**"Which worn slots get drawn" across the three results.** Consistent where it must be. The two
drawing screens now share `0..12 ∪ {23}`; the in-world frame adds `{24}` for the creature triple,
which is a strict superset used only as a "would this frame differ" question and never as a drawing
filter. The one filter that still disagrees is the *creation blob writer's* `<= 13`, which is not a
consumer of the drawn appearance any more — it writes a column that only the entry-burst roster
frame still reads. That disagreement is deliberate, is inside the byte anchor, and is recorded in
four places (below).

**No result silently changed an earlier one's accepted behaviour.** I diffed each commit's
production reach against the others. spec-003 touched spec-002's file
(`StateSynchronizationPayloadCodec.Entry.cs`) and spec-002's caller path, and reported both —
`CurrentUserStateDrawsFrom` is stated in spec-003's "Not changed, checked rather than assumed" with
the old and new truth tables, which I re-derived and agree with (`<=12 || ==23 || ==24` vs
`(<=12 || ==23) || ==24`, identical for every input including negatives). Nothing else crosses.

## Reverse enumeration, re-scanned with spec-003's method

**The method itself holds up.** Its durable part is the invariant, not the table: *at every
frontier, ask what symbol the next caller up actually writes down; when the answer is "nothing, a
registry resolves it", go read that registry.* That is the correct generalisation of the three
failures in this task, and the list explicitly marks its own frontier table as incomplete. I found
no hole in the invariant. One thing it does not say and should be carried forward: the walk must
also go *downward* from newly-constructed values, because constructing a message reaches an encoder
that runs later and elsewhere — which is exactly where Observation A lives.

**Re-applied to spec-001.** Clean. I re-ran the closure myself:
`grep -rn "MoveItemSpaceCommand" Server/src Server/tests` gives exactly one production sender
(`MoveItemSpaceSlice.cs:798`, in `ResolveAsync`); `grep -rn "ItemMoveStatus\." Server/src` outside
the slice returns nothing, so the new enum member reaches no other production switch;
`MoveItemSpaceHandler` is registered once, at `ProtocolModule.cs:295` for
`ClientMessageType.GameMoveItemSpace` — one entry point, walk stops. The five/now-more check
construction sites all omit `SourceInstanceValue` and so cannot reach `MoveAlreadyMade`, as claimed.
I also confirmed `SourceInstanceValue` is a pre-existing decoded field (`body[3..7]`, request record
line 33) and not something this work put on the wire.

**Re-applied to spec-002.** One gap, recorded as Observation A.

**Re-applied to spec-003.** Clean, re-derived independently:
`grep -rn "AccountCharacterRosterResponse\|GetAccountCharacterRosterCommand" Server/src` gives
exactly two senders — `GetAccountCharacterRosterHandler.cs:29` and `CreateCharacterSlice.cs:138` —
and one encoder, `AccountCharacterRosterResponseEncoder`, which is the sole caller of
`AccountCharacterRosterPayloadCodec.Encode`. Both new interface members are registered
(`ApplicationModule.cs:245` and `:300` for the two dependencies spec-002 added; the repository and
domain-service members are on already-registered types). `CharacterSummaryRow.AppearanceBlob` indeed
has no production reader left — the only `AppearanceBlob` reads in `Server/src` are the entry
path's own row type. `DecodeAppearances` has one production call and zero test hits, which is the
coverage loss spec-003 declares.

## The wire face

Five things could have moved bytes; four did not.

1. **The move acknowledgement — unchanged.** `MoveItemSpacePayloadCodec.Encode` is untouched and
   `MoveItemSpaceResponse.Success/.Failure` still echo the request's own two ends, which is what a
   reversed move needs.
2. **`SelectCharacterAcceptedTranscriptTests` — byte-identical**, absent from all three diffs, green,
   and (RM2) genuinely sensitive to the code the refactor moved through.
3. **NOTI `0x0002` subtype 0 after an appearance-changing move — a new *occurrence*, not new bytes
   and not a new frame.** This is SC-4's own intent.
4. **The character-selection roster frame's body now varies with worn gear.** Wire-visible,
   unanchored, and SC-6's own intent.
5. **The roster refresh pushed after a successful creation — bytes change.** Wire-visible,
   unanchored, and *not* something the contract asked for; it falls out of routing both senders
   through the deriving listing. spec-003's F-11 accounts for it in three separate mechanisms
   (filter `<=13` vs `0..12 ∪ {23}`; grant order vs slot order; two per-part bytes becoming derived
   though still zero today). I checked the filter claim against `PvfEquipmentSlots.ByName`: slot 13
   is `[title]`, slot 23 is `[magic stone]`, and the weapon is slot 12, so a stock creation
   (swordman: weapon 27600, coat 10400, pants 12400) produces the same single weapon part either
   way — the difference bites only a job whose create list names a title or a magic stone, which
   spec-003 correctly declines to claim it measured.

So the answer to "is the create-push frame the only unanchored wire-visible change" is: **it is the
only one outside the contract's own Scope In.** Items 4 and 5 are both unanchored and both
wire-visible; 4 is the requested outcome and 5 is the disclosed consequence. Both have a per-frame
account, which is what SC-8's Required evidence asks for.

No new frame and no `ClientMessageType`/`ServerMessageType` member — established independently, not
taken from any list.

## Existing checks that were rewritten

Every rewrite corresponds to an authorized behaviour change and every one is separately explained.
I read each and found no expectation weakened to fit an implementation:

- `RefusedEquipIsRecordedWithItsTemplateTests` — two whole-string log assertions, corresponding to
  spec-001 R-7 (the log line's shape). Slot numbers, template numbers, status word and error code
  are untouched.
- Three fixtures that construct `MoveItemSpaceHandler` — given doubles for the two new
  dependencies. No expected value moved; spec-002 says so *and* says why the assertions now hold for
  a different reason, which is the honest form.
- Nine test doubles of `ICharacterDomainService`/`ICharacterRepository`/`ICharacterItemTable` —
  every one of them **throws** on the new member rather than returning something plausible
  (`SelectCharacterProductionStateTests` throws `NotSupportedException("The entry burst does not
  draw the roster.")`). That is strengthening, not weakening: it pins that those paths do not draw.
- `AccountCharacterRosterCodecTests` — one signature nullable, one case added. Nothing removed.
- `SqliteCharacterLifecycleTests` — the one real removal. Half of
  `TheRosterCarriesStoredAppearancePartsAndCopesWithNone` asserted that a hand-written blob decodes
  through `ListByAccountAsync`; that method no longer decodes a blob, so the assertion was asserting
  a path spec-003 authorizedly removed (R-1/R-2). Its loss is declared by spec-003 rather than
  quietly absorbed, and the replacement is named.

## Repository conventions

- **Layering** holds. `DrawnAppearance` sits in Application over `Dfo.Server.Data` only;
  `Protocol → Application` is the allowed direction; the new read is
  `Abstractions` (interface) + `Persistence` (SQL). No upward reference appears.
- **Slice architecture.** `MoveItemSpace` now names the `StateSynchronization` slice. The precedent
  is not one file but eleven: `grep` for that using-directive under `Server/src/Dfo.Server.Protocol/Game/`
  returns `DungeonClear`, `DungeonSelection`, `Login`, `MonsterDeath`, `OnlineUserConnectionObserver`,
  `QuestAcceptance`, `QuestCompletion`, three under `SelectCharacter`, and `TutorialLevelUp`.
  The state frames are shared material in this tree, not another endpoint's protocol.
- **`CommandHandlerArchitectureTests` and `NotificationHandlerArchitectureTests`** are absent from
  the diff — neither predicate nor exemption list was relaxed or extended. `ProtocolModule.cs` is
  absent too, so no wiring point moved.
- **`TreatWarningsAsErrors` / CS9113.** The suite built and ran, which is the enforcement.
- **No git restoring command** was used by me; my mutations used file-level backup plus `sha256sum`
  with the hash re-verified after each restore.

## Records

Eight record files changed. I read every hunk and found them mutually consistent — in particular the
one fact a reader most needs and could most easily miss, **that the two roster frames now disagree**,
is stated in four independent places that say the same thing rather than four variants:
`Docs/CharacterDomainCut.md` (external-appearance row plus a dated note),
`Docs/CharacterEntryFacesInventory.md` (two rows plus a dated note),
`Docs/ProtocolScaffoldDissonances.md` §7 and
`protocol/game/server-to-client/NOTI-0x0002.md` (a two-row table naming both encoders, both sources
and both slot filters). Each says the same three things: the selection screen and the create-push
draw live wear through `DrawnAppearance`; the entry burst's roster frame still decodes
`appearance_blob`; the boundary is the byte anchor and closing it moves anchored bytes. A reader
arriving at any one of the four gets the whole fact and a pointer to the others.

Two falsifiable claims I checked rather than accepted: the `CharacterRosterService.cs:147 → :214`
line-number correction in `AGENTS.md` and `Docs/DungeonEntryNotes.md` is right (line 214 is
`new CharacterFatigueState(188, 0)`), and "`appearance_blob` has zero UPDATEs in `Server/src`" is
right. Nothing overreaches: the `0x0013.md` entry is careful to mark "抓到的是一帧" and label the
eleven-costume conclusion as inferred from it rather than captured.

No contradiction found between any two records, and none of the eight claims more than its stated
evidence type.

## Leftover risk, as registered

The three `Successor Guidance` sections together do carry the items a reader needs, including all
four named in the review brief: the session inventory snapshot staying stale (spec-002), the blob
still being read by the entry burst (spec-003, twice), the `DecodeAppearances` coverage gap
(spec-003, split correctly into "this work's loss" and "inherited gap"), and the orphan
`item_slots` main-bag slot 3 / `core_uid 8474` row (spec-001). spec-002's first guidance item
("the projection is still only bytes; extracting it is Defect C's to do") was *discharged* by
spec-003 rather than left dangling. One item is registered but not connected across results, which
is Observation B.

# Non-blocking observations

None of these is a material blocking defect. Each is recorded so it is not rediscovered.

**A. spec-002's guard protects building the appearance, not encoding it — and the encoder's newly
reachable refusals are absent from its list.** `AppearanceAsync` is wrapped in `try/catch`, but the
projection and every `checked` cast actually run inside `EncodeCurrentUserState`, at send time,
outside that block: `DrawnAppearance.Of`'s `checked((byte)item.SlotIndex)`, the
`appearance.Count > byte.MaxValue` refusal, and the `SingleOrDefault` over slot 24 are now reachable
from a *move*, where before they were reachable only from character selection. spec-002's F-1 lists
only the frame and its R-7 names only "working out the appearance raises", so this class is
unlisted. The blast radius is smaller than it first appears, and I checked why:
`TouchSocketNetworkMessageSender.DeliverExplicitAsync` iterates `RequiredDirectMessages`
sequentially with no encode-ahead and no per-message try, so the acknowledgement — index 0 — is
already on the wire before the appearance frame is encoded. A raise there costs the connection, not
the acknowledgement, so spec-002's stated property survives, though for a reason its own comment
does not give. Reachability needs a negative or >255 `item_slots.slot_index`, which spec-003's F-4
establishes is storable but written by nothing in this repository. Recorded as an enumeration gap in
the composed change, not as a defect to fix.

**B. The composition turns spec-001's disclosed unguarded mirror into something the player will
see.** spec-001 discloses that a client restating a completed *take-off* (identity on the occupied
end, so `SourceInstanceValue` is zero and `MoveAlreadyMade` cannot fire) is reversed by F-2 and puts
the piece back on — left that way on upstream's precedent, not on a demonstration that it is safe.
Before spec-002 that was a store-only divergence the client would not redraw until re-entry. With
spec-002 in place the server now also pushes an appearance frame for it, so the costume visibly goes
back on. spec-001 wrote its note before spec-002 existed and could not have connected them;
spec-002's list does not carry it forward and neither Successor Guidance joins the two. Worth
putting in front of the owner *before* the live retest, because it predicts one specific
observation: a costume going back on by itself immediately after being taken off. spec-001 already
names the right first place to look.

**C. The root map's SC-8 row understates the wire face, and the worktree is not clean.**
`multi-spec-map.md` records SC-8 as satisfied with gap "none", citing only the byte anchor and the
absence of message-type members. Two frames' bytes do change outside the anchor (items 4 and 5 in
"The wire face"). SC-8's Required evidence — a per-frame account — does exist, but only inside
spec-003's `behavior-change-list.md`; the root-level row does not point at it, so a reader of the
map alone would conclude no bytes moved. Separately: that file is **uncommitted** in the worktree
(`git status` shows ` M .rks-agent/.../multi-spec-map.md`), contrary to the brief's statement that
the workspace is clean. Neither point is inside the reviewed diff; both are for the controller.

**D. `PersistentContainerBoundaryTests.InsideAUseCaseItAccepts`, assessed rather than left open.**
It did not recur in my run. Reading it: it creates a fresh temp SQLite database per invocation under
`Path.GetTempPath()` and exercises `PersistentItemContainer` + `SqliteItemDomainStore` — all three
files untouched by all three commits, and `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`
is absent from the diff entirely. Its dependency surface is the same SQLite-lifecycle machinery the
known flake family lives in. So the one-off failure has no causal path to this change. I am not
reclassifying it as the known flake — its evidence was not kept and the stated discriminator cannot
be applied retroactively — but it is not a reason to withhold approval.
