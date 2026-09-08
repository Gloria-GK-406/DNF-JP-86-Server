---
kind: rks-review
verdict: approved
---

# Change review: spec-002 "The character changes as you dress it"

Scope `change`, gate `auto`, fresh reviewer. Reviewed against the root TaskContract (SC-1..SC-10),
the child TaskContract revision 1 (C-1..C-8), `spec.md` (RQ-001..RQ-006 / AC-001..AC-009 /
NA-001..NA-005 / AS-001..AS-002), `routing-decision.md`, `behavior-change-list.md`, and the complete
change between `e766d97152a6147323d3279248858ffb1ef4918c` and the uncommitted worktree state on
branch `dressing-a-character-and-seeing-it`. No adversarial challenge set was supplied, so there is
no Challenge Dispositions section.

This is the second pass. The first pass raised one blocking finding, B-1; the second pass re-derived
the fix and its consequences from the code and re-ran the evidence. Nothing below was taken from the
executor's account, in either pass.

**Verdict: approved.** B-1 is resolved, the fix is the right shape, and I found no regression it
brought with it.

## B-1 (round one) -- resolved

**What was wrong.** `AppearanceAsync` was awaited before `SendExplicitAsync`, and it awaits
`ICharacterInventoryEntryDomainService.LoadAsync` -- a database read plus
`InventoryEntryProjection.ProjectCharacter`, which raises `InvalidDataException` on several data
shapes. The move's transaction had already committed. A raise there meant the acknowledgement was
never sent: the dispatcher logs and rethrows (`NetworkPlugins.cs:60-76` and the Game plugin at
`:139-155`, both dispatching with `CancellationToken.None`), so the client got nothing for a move the
store had carried out, and this slice's own header records what that costs -- the client holds the
inventory shut until the acknowledgement arrives. The change list also asserted the opposite
("nothing that was sent before has stopped"), and the code's justification for the neighbouring
R-5 case was factually wrong about when the acknowledgement leaves.

**What I verified about the fix.**

*The acknowledgement can no longer be lost.* `MoveItemSpaceSlice.cs:679-712`: the `try` encloses
both the `await` and the `frames.Add`, so any raise leaves `frames` as `[response]`, and
`SendExplicitAsync` is outside the guarded block and always runs. There is no path from a raise
inside `AppearanceAsync` to a skipped send.

*The `when (!cancellationToken.IsCancellationRequested)` filter is correct, and in production it is
also inert.* Both network plugins dispatch with `CancellationToken.None`
(`NetworkPlugins.cs:63-66` and `:142-145`), so `IsCancellationRequested` is never true on this path
and the catch is effectively unconditional -- which is the behaviour B-1 needed. The filter does not
swallow a cooperative cancellation of this token, and it cannot cause the acknowledgement to be lost
in any reachable production state. Two residual imprecisions, neither of them defects:

- If a token were wired in and were already cancelled when a *non*-cancellation exception arrived,
  the filter would let it propagate and the acknowledgement would be lost. Under a cancelled token
  the send would be moot anyway, so this is the harmless direction.
- Conversely, an `OperationCanceledException` raised for some *inner* token (a command timeout, say)
  while this token is not cancelled is caught and logged as an appearance failure. That is also the
  right outcome here -- the move still stands and still needs answering.

This repository's own idiom keys on the exception type as well
(`TouchSocketNetworkMessageSender.cs:93`, `NotificationBus.cs:214`,
`NotificationRetryDrainService.cs:70`, all `catch (OperationCanceledException) when
(cancellationToken.IsCancellationRequested)`). Matching it would be marginally more precise, but
with `CancellationToken.None` on this path it would be equally inert, so I am not asking for it.

*Catching `Exception` is right here, and I would not narrow it.* The reachable raise set from
`AppearanceAsync` spans three layers: `InvalidDataException` from
`InventoryEntryProjection.ProjectCharacter`; whatever the persistence layer surfaces
(`SqliteException`, and the `ObjectDisposedException` this repository already tracks as a known
flake); and `InvalidOperationException` / `ArgumentException` from the runtime directory and the
`SingleOrDefault`. Naming that set in a Protocol handler would import persistence exception types
across the layering boundary, and would silently reinstate B-1 the first time a type outside the
list appeared -- which is exactly the failure mode the guard exists to prevent. A guard whose only
job is "the acknowledgement must survive" should catch broadly and log. The same layer already does
this in four places (`SelectCharacterHandler.cs:124` and `:145`,
`SelectCharacterPhaseCommands.cs:63` and `:117`, `DungeonSelectionSlice.cs:468`), so it is house
style rather than a one-off. The log line is at Error with the connection id and reads in the same
operational voice as the handler's other lines, so nothing is lost to silence.

*The reworded remarks are accurate.* `MoveItemSpaceSlice.cs:753-756` now reads: "the move stands
either way -- it was committed before this was asked, and the acknowledgement carrying it goes out
whether or not this answers". Both halves check out: `AppearanceAsync` is reached only after
`result?.Status is ItemMoveStatus.Moved`, which came from the completed command; and both of its
non-answering outcomes -- returning null, or raising into the guard -- leave `frames` at `[response]`
and fall through to the send.

*The new check is real, and it is the only thing that holds the guard up.* Re-ran M3 myself under
the contract's mutation discipline. `sha256sum` of
`Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs` before the mutation:
`1ffca317688157e7f6b4903e46a58789f035421a7f1799b191894b80298db45f`, the value the change list
reports. File-level copy to the session scratchpad; no git restoring command used at any point.
Mutation: the `try` / `catch` removed, leaving the bare `await` and `Add`. Ran the **whole suite**,
not just the file: **1 failed / 2383 passed / 2 skipped**, the single failure being
`A_move_is_still_acknowledged_when_the_appearance_cannot_be_built`, with nothing else anywhere in
the suite reddening. Restored from the backup and re-measured the hash as `1ffca317...`, identical;
`git status --short` shows the same nine modified paths and two untracked paths as before.

The check itself is well formed: `InventoryThatRaises.LoadAsync` throws `InvalidDataException` --
the same type `ProjectCharacter` actually raises -- and because `AppearanceAsync` is `async`, a
synchronous throw inside it surfaces at the handler's `await` exactly as an asynchronous fault
would, so the guard is exercised through the real path. It asserts one message, `ErrorCode` null and
`MoveValue` 1, so it pins the acknowledgement's *content*, not merely its count.

*No regression from the fix.* `AppearanceAsync`'s body is unchanged apart from its remarks, so the
round-one evidence for C-4 still applies to it; the eight pre-existing cases in the file are all
green, so the happy path is untouched; and the guard's only new behaviour is on a path that
previously threw. The one thing the guard can now mask is a genuine store failure showing up as a
character that does not visibly change -- but it is logged at Error with the connection id and
recorded as R-7, so it is discoverable rather than silent.

*R-7 is honest.* It names the path, says the first draft of the list missed it, says the review
found it, and names the shape (a newly reachable path a green suite cannot see) as the one this
repository keeps missing. That is the disclosure C-5 asks for.

## What I verified independently across both passes, and it held

### Suite (C-7 / AC-009 / SC-9)

`tasklist | grep -i Dfo.Server` empty before each run.
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
on the current tree returned exit code 0 with **2384 passed / 2 skipped / 0 failed**, summed from the
seven per-assembly lines the run printed: Networking 52, Protocol 608, Pvf 60, Host 6, Persistence
329, Application 1070, Infrastructure 259 plus the 2 skips. The child contract's floor is 2375/2/0,
so +9, all of them in Protocol.Tests, which is the nine cases in the new file. Round one measured
2383/2/0 on the pre-fix tree, so the fix adds exactly its one check. No flaky failure occurred in
either run. Met.

### The frame comes from the store, not the snapshot (C-4 / AC-003 / RQ-004)

Reproduced the executor's M2 in round one against the then-current file, whose hash was
`853f05f5234a4a608c0e293c0d51d31050a7525fd1a476c44878ad0a4c7b31db` before and after -- the value the
change list reports. Mutation: keep the `LoadAsync` call, discard its result, build `equipment` from
`state.CharacterInventory.Containers`. Result on the eight cases then present: 3 failed / 5 passed,
including `The_frame_follows_the_store_and_not_the_session_copy`. The named case is genuinely
load-bearing for C-4 and reddens for the right reason. The fix left `AppearanceAsync`'s body and that
assertion untouched, so the evidence carries forward.

### The three "acknowledgement alone" cases are not vacuous, and slot 22 is genuinely outside the read set

`A_move_inside_the_bag_...`, `A_move_between_the_bag_and_the_avatar_page_...` and
`A_move_onto_a_worn_slot_the_frame_ignores_...` assert `Assert.Single(world.Sender.Sent)` without
asserting that the move succeeded, so on paper each would also pass on a refusal. I settled this
empirically rather than by argument: reviewer's own mutation, `ChangesWhatTheCharacterLooksLike`
replaced by `message.MoveCount >= 0`. All three failed, and each failure message printed the
acknowledgement actually produced -- `ErrorCode = ` (null) with `MoveValue = 1`, including
`SourceListType = 3, SourceSlotIndex = 22`. The moves really do succeed; the cases really do pin the
absence of the frame. `A_refused_move_sends_the_refusal_alone` stayed green, correctly. Same backup
and hash discipline; restored and re-hashed.

Slot 22 is outside the read set by construction: `CurrentUserStateDrawsFrom(22)` is
`22 <= 12 || 22 == 23 || 22 == 24`, false on all three terms.

### The trigger misses nothing (AS-001, and the shape SC-3 was written for)

Derived from `MoveItemSpaceCommandHandler.HandleAsync`, not from the list. The equipment container is
written in exactly two places: `PutOnAsync` writes `worn[DestinationSlotIndex]`, and `TakeOffAsync`
writes or clears `worn[SourceSlotIndex]`. `CarryAcrossAsync` and the within-list branch touch only
the two carried lists. Equipment-to-equipment is not served at all -- both `CarriedListFor` calls
answer null, the two equip branches each need a carried list on the other side, and the request falls
through to `ListNotServed`. So a move can change a worn slot only by naming the equipment list at the
end that slot is on, which is exactly what `ChangesWhatTheCharacterLooksLike` asks about both ends.
The reversal paths (`PutOnAsync` handing to `TakeOffAsync(Reversed(...))` and back) exchange the two
ends without changing either list, and the predicate ORs both ends, so it is invariant under
reversal. No missed send.

`(byte)InventoryContainerType.Equipment` is 3 (`InventoryModels.cs:11`), which is the wire list number
the decoder reads at `body[0]` / `body[11]` and the number the upstream reference uses. The predicate
is asked only when the list is 3, so a bag or avatar slot number is never put to it.

Subtype zero reads its equipment argument in exactly two places -- the appearance run
(`SlotIndex <= 12 || == 23`) and `creature` (`SlotIndex == 24 && ItemKind == 5`, feeding
`WriteCreatureTriple`, which reads nothing else from equipment) -- and `CurrentUserStateDrawsFrom` is
the union of the two, so the creature slot 24 and slot 23 are both covered.

### The frame is built the same way entry builds it

`MoveItemSpaceSlice.cs:776-780` against `SelectedCharacterStateMessageFactory.cs:40-44`: the same four
arguments from the same places -- `state.Character`, `state.CharacterEntry.Public`,
`state.CharacterEntry.Features.Pets`, and the Equipment container's `Items`. Only the equipment's
source differs, which is the point of the change.

### Mixing a CMD acknowledgement and a NOTI frame in one delivery is a proven pattern

`TouchSocketNetworkMessageSender.DeliverExplicitAsync:69-80` sends each required message through the
same per-message path, so each carries its own class; `ValidateExplicitDelivery:107-128` imposes no
homogeneity. `GroundPickupSlice.cs:481-500` already sends `GetItemResponse` (CMD) followed by two
notifications in one `GameExplicitMessageDelivery`. No new wire risk, and the acknowledgement is
first (`frames = [response]`, then `Add`), as RQ-001 requires.

### No new frame, no relaxed architecture check, no byte change (C-6 / AC-007 / SC-8)

`CurrentUserStateMessage` already existed (`StateSynchronizationMessages.cs:67-76`, NOTI `0x0002`) and
`ProtocolModule.cs` is not in the diff, so nothing is registered that was not registered before.
`SelectCharacterAcceptedTranscriptTests`, `CommandHandlerArchitectureTests` and
`NotificationHandlerArchitectureTests` are not in the change set at all -- neither predicate nor
exemption list was touched -- and all three are green in the run above. Both new constructor
dependencies are already registered (`ApplicationModule.cs:245` and `:300`), and the build is clean
under `TreatWarningsAsErrors` with all six primary-constructor parameters used, so CS9113 does not
arise.

### `CurrentUserStateDrawsFrom` and the two constants

The filter's meaning is unchanged: `Noti2AppearanceLastSlot = 12` and `Noti2AppearanceExtraSlot = 23`
substituted into the same `<=` and `==` positions, and `Noti2CreatureSlot = 24` already existed at
`:315`. No byte can change, and the byte anchor agrees.

I judge the new predicate **not** a fourth slot filter. It introduces no new number, it is the union
of the two filters standing immediately below it in the same file, and it sits beside them so a
reader changing either has the union in view. Asking it from the handler is better than the handler
restating `0..12 || 23 || 24` locally, which is what would actually have created a fourth copy.

The deferral of reconciling the three disagreeing filters is right, and for a reason stronger than
"it is someone else's job": reconciling them changes bytes, and C-6 / RQ-006 forbid a byte change in
this outcome. Defect C has to make the world's picture and the selection screen's picture agree, so
it is the only place the decision can be made once and checked against both.

### Slice architecture

Not a violation introduced here. `using Dfo.Server.Protocol.Game.StateSynchronization` appears in 13
production files, twelve of them predating this change: `DungeonClearSlice`, `DungeonSelectionSlice`,
`GameLoginHandler`, `MonsterDeathSlice`, `OnlineUserConnectionObserver`, `AcceptQuestHandler`,
`FinishQuestHandler`, `PvpParticipantPublishedNotification`, `SelectCharacterHandler`,
`SelectedCharacterStateMessageFactory`, `TutorialLevelUpSlice` and `ProtocolModule` (counted from
`grep -rn "using Dfo.Server.Protocol.Game.StateSynchronization" Server/src --include=*.cs`, one hit
per file, minus the `MoveItemSpace` hit this change adds). `StateSynchronization` is in practice the
endpoint's shared state-frame material rather than another protocol, so the precedent the change list
cites is real and uniform rather than a single case. The residual point is that `AGENTS.md`'s literal
rule at line 338 no longer describes the tree; that is a documentation debt for the owner to settle,
not a reason to block this change.

The handler's own additions stay inside the permitted four kinds: a read-only domain-service query
and an explicit-delivery send. No command handler dispatches another command.

### The snapshot decision

`DungeonSelectionSlice.cs:418-421` does read its equipment from `result.CharacterInventory`, so
RQ-005's stated reason is factual rather than a rationalisation. I agree with leaving the snapshot
alone: refreshing it would change what an unrelated frame draws, `ReplaceExact`'s existing call sites
are all use-case completions rather than incidental writes, and the staleness predates this work. The
consequence is recorded in three places -- `spec.md` RQ-005, change list R-6 plus "Known and left",
and the `AppearanceAsync` remarks -- and `The_session_copy_is_left_as_it_was` pins it. I would not ask
for the refresh here. One precision point: "they disagreed before this frame existed" is true of the
two copies, but before this change nothing the player could see contradicted the snapshot, and now
the world and the dungeon-selection stat frame can disagree on screen. That is the follow-up defect C
inherits.

### The record annotations (C-8 / SC-10)

Both are correct about their own subject. The trace's request is `... 03 | 0F 00` -- list 3, slot
`0x000F` = 15 -- and `CurrentUserStateDrawsFrom(15)` is false on all three terms, so
"後面什麼都不跟" still holds for that frame, and neither original observation was rewritten.

The `0x0013.md` annotation was tightened after round one and is now accurate: it says upstream judges
by the same *shape* (is the changed slot in an appearance-slot table) while stating plainly that the
table is not this repository's, giving upstream's as `0..13` plus `24`, `25`, `29` with `12` and `13`
suppressed inside a dungeon, and concluding only that neither table contains 15. I checked every part
of that against source: `IsAppearanceEquipmentSlot` is
`IsA21RosterAppearanceSlot(slot) || slot == Creature || slot == NameTag`
(`InventoryHandler.Move.cs:247-254`); `IsA21RosterAppearanceSlot` is
`HatAvatar(0)..TitleName(13) || SupportWeapon(24)` (`EquipmentType.cs:117-125`); `Creature` is 25 and
`NameTag` is 29 (`EquipmentType.cs:34`, `:38`); and `ShouldUseDungeonEquipmentOnlyRefresh:240-245`
suppresses `Weapon(12)` and `TitleName(13)` while `CurrentRun` is non-null. Accurate, and no longer
overreaching.

### The sealed artifact

Correctly handled. `spec-001`'s `routing-decision.md` no longer appears in the change set, and its
content hashes identically to what the commit holds (`git show e766d971:<path> | sha256sum` and
`sha256sum <path>` both `78627629807...`). `git show ... > <path>` is a read plus a shell redirect,
not one of the three git restoring commands the contract forbids, and unlike them it cannot reach any
other file -- so it satisfies both the letter and the reason for the rule. Keeping that one file CRLF
rather than "tidying" a sealed accepted result is the right priority, and the change list says so.
`multi-spec-map.md` is still a whole-file line-ending rewrite, but it is a live controller artifact
carrying real content changes this outcome owns (25 insertions / 11 deletions under an EOL-blind
diff), which is a different situation from a sealed record.

## Non-blocking observations

1. **R-5's row still carries the sentence the code just stopped saying.**
   `behavior-change-list.md` R-5 reads "the move has already been acknowledged". That is the same
   statement about ordering that was wrong in the code comment and was corrected there; at the point
   R-5 returns null, the acknowledgement has not gone out yet -- it goes out afterwards. The
   *conclusion* R-5 draws is right either way, so this is wording, not substance, but it is the one
   place the corrected fact did not reach.
2. **"Upstream's own is wider still" is not quite the relation.** The two sets are not nested: ours
   has `23`, which upstream's appearance table does not, and upstream has `13`, `25` and `29`, which
   ours does not. "Wider" invites reading it as a superset. The numbers listed beside it are correct.
3. **N-4 still cites one precedent where there are twelve.** Unchanged from round one and still
   accurate as far as it goes; it just understates how settled the pattern is.
4. **Slot 24 is over-served, now recorded.** `CurrentUserStateDrawsFrom(24)` is true regardless of
   item kind while the encoder's `creature` requires `ItemKind == 5`, so a non-creature at 24 costs
   an inventory read and a frame with nothing new in it. Harmless, and the "Known and left" entry
   states it plainly.
5. **The frame can still raise during encoding, after the acknowledgement is out.** The entry path
   validates by calling `EncodeBody()` before sending (`SelectedCharacterStateMessageFactory`'s
   `CreateValidated`); this path does not. `DeliverExplicitAsync` sends message by message, so the
   acknowledgement is already written when the second one encodes -- B-1's consequence cannot recur.
   I read `EncodeCurrentUserState` for raises and found only `CountError` (unreachable: at most
   fourteen appearance entries) and `checked` casts over fields that already encoded successfully at
   entry plus the fresh item ids. Effectively unreachable; noted because it is the last place the
   frame build can still fail.
6. **The three "acknowledgement alone" cases could assert success.** As shown above they do exercise
   successful moves today, but nothing in them would notice if a later change started refusing those
   requests -- they would go on passing for the wrong reason. One `Assert.Null(...ErrorCode)` each
   closes it. `The_session_copy_is_left_as_it_was` likewise counts `ReplaceExact` calls where AC-006
   says "the same object"; equivalent in effect, noted only because the wording differs.
7. **No check resolves `MoveItemSpaceHandler` from the real container.** Both new dependencies are
   registered (`ApplicationModule.cs:245`, `:300`) and I confirmed that by reading, but
   `Dfo.Server.Host.Tests` is six cases about carried gold, so a DI regression on this handler would
   surface only at runtime. A pre-existing gap, widened slightly by two more constructor parameters.
8. **Cost is stated honestly.** One full `LoadAsync` per move that changed the character's look and
   none otherwise; only the equipment container of the loaded inventory is used. Confirmed by reading
   the guard.

## Contract disposition

| ID | Verdict |
|---|---|
| C-1 | met -- two cases assert the pair of frames in one delivery, acknowledgement first |
| C-2 | met -- three such moves, one inside a single list; proven non-vacuous by mutation here |
| C-3 | met -- `A_refused_move_sends_the_refusal_alone` |
| C-4 | met -- the discriminating mutation reproduced independently |
| C-5 | met -- R-7 closes the gap round one found, and names how it was found |
| C-6 | met |
| C-7 | met -- 2384 / 2 / 0, exit code 0, re-measured here |
| C-8 | met |

Root conditions this outcome carries: SC-4 and SC-5 are satisfied in their automated half, SC-3, SC-8
and SC-9 are met, and SC-10's share for this outcome is met. The owner's live confirmation for SC-4
and SC-5 remains outstanding by design and belongs to fresh verification, not to this review.
