---
kind: rks-review
verdict: changes_required
---

# Change Review: The session copy keeps up

## What was reviewed

Scope `change`. Branch `every-frame-draws-the-current-character`, baseline `1d887c59`, uncommitted
working tree. Read: this attempt's `TaskContract.md` (revision 1, C-1..C-9), `routing-decision.md`,
`reader-enumeration.md`, `spec.md`, `plan.md`, `behavior-change-list.md`, the parent
`../../TaskContract.md`, and `../spec-003-a-restated-move-does-not-undo-itself/spec-result.md`.

The change set, established by `git status --short` and `git diff --stat` rather than from the
report: three tracked files -- `.rks-agent/.../multi-spec-map.md` (41 lines), the slice
`Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs` (49), the checks
`Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/TheCharacterChangesAsYouDressItTests.cs`
(128) -- plus the untracked attempt folder. That matches what was claimed.

No executor number is carried over below. Everything stated as a count or a run result was produced
by a command run for this review.

## Checks run for this review

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test
E:/.../Server/86JP.Server.slnx --nologo`, run from an absolute path with no `Dfo.Server.Host`
process present, output kept at
`<scratchpad>/review-run-1.txt`:

- **2413 passed / 2 skipped / 0 failed**, exit 0. Per assembly: Protocol 627, Application 1076,
  Persistence 333, Infrastructure 259 (+2 skipped), Pvf 60, Networking 52, Host 6.
- Searched in the output body, not the summary line: `[FAIL]` **0**, `error CS` **0**,
  `MSB<digit>` **0**, and **7** 「的测试运行」 banners -- one per test assembly, so nothing silently
  failed to run.
- Against the 2409 / 2 / 0 baseline this is **+4, all in Protocol (623 -> 627)**. The four are
  accounted for by the diff: one existing case renamed (net zero) and four added --
  `Only_the_inventory_member_of_the_session_copy_is_replaced`,
  `A_move_that_changes_nothing_drawn_still_refreshes_the_copy`,
  `A_refused_move_leaves_the_session_copy_alone`,
  `A_session_that_will_not_accept_the_refresh_still_gets_its_acknowledgement`.
- No failure occurred, so no exception-type/stack-root classification was needed. The known
  `ObjectDisposedException: 'SQLitePCL.sqlite3'` flake did not appear.

Three mutations were applied for this review, each to a `sha256sum`-verified copy and restored
immediately; the file's hash after the last restore is
`d405a635c7d9e5888faca31e89acee064497110680b514004ca30ffef1fbea25`, equal to its hash before the
first. Results are in the findings below.

## Blocking Findings

### BF-1 -- C-3's evidence does not exist: no check reads the screen after a move

C-3 asks for "A check that performs a move and then reads that screen". `spec.md`'s EV-001 says the
same thing more narrowly: "checks over the dungeon selection frame before and after a move." AC-001
says "when the dungeon selection screen is asked afterwards, it names the equipment the character
has now."

Nothing does that. `grep -rln "DungeonSelection\|EnterSelectDungeon" Server/tests --include=*.cs`
returns five files (`DungeonClear/LeaveDungeonTests.cs`, `DungeonEntry/SelectDungeonHandlerTests.cs`,
`RosterScreenQueries/DungeonSelectionCodecTests.cs`,
`RosterScreenQueries/DungeonSelectionWiringTests.cs`,
`TownProjection/TownProjectionHandlerTests.cs`); none of them performs a move, and none of them was
touched by this change. All thirteen cases in `TheCharacterChangesAsYouDressItTests` read either the
move's own `CurrentUserStateMessage` or `RecordingRuntime.Replaced` -- the copy, never the screen.

What is delivered is one link short of the claim. The remaining link is real -- `DungeonSelectionSlice.cs:419`
takes `result.CharacterInventory.Containers`' Equipment container straight into the frame -- but it
is asserted nowhere, and the substitution of "the copy" for "the screen" is not disclosed in
`spec.md`, `plan.md` or `behavior-change-list.md`. The root outcome SC-9 is stated about a screen.

### BF-2 -- an admission was narrowed, and the change list says none was

`RefreshSessionAsync` calls `ReplaceExact` **before** it builds the frame and before
`if (!draw) return null;`. So when `ReplaceExact` raises, the appearance frame the baseline would
have sent is never built. That is a frame the client used to get and now does not, in a case where
the baseline had no failure at all on this path.

Demonstrated rather than reasoned: with the single line
`runtimeSessions.ReplaceExact(session, state with { CharacterInventory = reloaded });` deleted, the
case `A_session_that_will_not_accept_the_refresh_still_gets_its_acknowledgement` fails with
`Assert.Single() Failure: The collection contained 2 items` -- the second item being a
`CurrentUserStateMessage`. With the line present that frame is suppressed.

`behavior-change-list.md` states the opposite: "**R-2. Nothing else was removed.** No refusal
deleted, no admission narrowed, no reader's answer changed except through the member the move
changed." C-6 asks for every admission path this work changes, derived from the code. This one is
missing, and it is cheap to either list it or remove it (build the frame first, or scope the
try/catch to the write).

### BF-3 -- a live repository record this work falsifies was not corrected

C-8 says "Repository records this work falsifies are corrected, **including** the prose count at
`MoveItemSpaceSlice.cs:814`" -- including, not only. The code remark was corrected. This was not:

`AGENTS.md:193-196` states 「而 `ReplaceExact` 有 6 个生产调用点」 and then lists all six by file and
line (`SelectCharacterLifecycleServices.cs:195`, `DungeonClearSlice.cs:752`,
`MonsterDeathSlice.cs:490`, `AcceptQuestHandler.cs:106`, `FinishQuestHandler.cs:144`,
`TutorialLevelUpSlice.cs:241`). F-1 makes it seven; the seventh is
`MoveItemSpaceSlice.cs:858`. This is exactly the shape of record the contract's C-8 is aimed at --
a count stated in prose with its members enumerated -- and it sits in the repository's top-level
agent instructions, where a stale count is read by everyone.

Two further records to adjudicate rather than assume: `protocol/game/client-to-server/0x01E4.md:79`
and `protocol/patterns/data/traces/a21-tutorial-ending-20260826.md:83` both say
「会话快照眼下没有局部更新的路径」. This change creates precisely such a path for one member. Whether
those sentences are falsified depends on whether they mean "no partial-update path exists" or "the
two missing frames would need a full re-projection"; either way the question was not asked.

### BF-4 -- the mutation record's decisive row is not reproducible from what it says

P1 is recorded as "the whole result replaced instead of the one member", 1 red, and the record calls
it "the one that matters". I applied that description literally --

```
runtimeSessions.ReplaceExact(session, new Dfo.Server.Data.Game.SelectCharacterResult(
    state.Character, state.CharacterEntry, reloaded, state.AccountInventory,
    state.Account, state.World, state.Session, state.Roster, state.RosterEntry));
```

-- and the suite stayed **green: 0 failed, 13 passed**. Replacing the whole result is not what the
checks catch, because a `with` expression and a hand-written constructor hand over the same
instances.

What the checks do catch is a member losing its identity. With
`state with { CharacterInventory = reloaded, CharacterEntry = state.CharacterEntry with { } }`,
exactly one case reddens -- `Only_the_inventory_member_of_the_session_copy_is_replaced` -- with
`Assert.Same() Failure: Values are not the same instance`.

So the clause the correctness argument rests on **is** guarded, and it reddens by value mismatch
rather than by a raised exception, which is what AC-007 asks for. But the record names neither the
edit nor the manner, and the literal reading of what it does say produces no red at all. C-7 asks
the record to say "which cases reddened and **how** each reddened"; the table's four rows say only
which. Given this task's two prior rounds of hash- and count-drift, a mutation row a reader cannot
rerun is the same defect in a new place.

For what it is worth, the two rows I could reproduce exactly are sound: deleting the `ReplaceExact`
line reddens **4** -- `The_session_copy_follows_the_move`,
`Only_the_inventory_member_of_the_session_copy_is_replaced`,
`A_move_that_changes_nothing_drawn_still_refreshes_the_copy`,
`A_session_that_will_not_accept_the_refresh_still_gets_its_acknowledgement` -- and restoring the
narrow gate (`result?.Status is ItemMoveStatus.Moved && ChangesWhatTheCharacterLooksLike(message)`)
reddens exactly **1**, `A_move_that_changes_nothing_drawn_still_refreshes_the_copy`. Both match the
record.

### BF-5 -- RQ-005 / AC-005 / T-007 was not delivered and its absence is not recorded

AC-005 is "A move that changes the drawn appearance reads the inventory once, not twice." T-007 is
"A check counting the inventory loads a single appearance-changing move performs", and `plan.md`
even reserves a decision for how to count them.

No such check exists. The two stand-ins in the file, `StoreBackedInventory` and
`InventoryThatRaises`, have no counter and neither records a call. `spec.md`'s own Success Evidence
list (EV-001..EV-005) has no entry for AC-005 -- the omission is visible in the approved artifact
and was carried through.

`behavior-change-list.md` nonetheless asserts the property under F-3: "**Not a second read.** Where
the appearance frame is also built, the same reload serves both ... there is no move that now loads
twice." The property is true -- `RefreshSessionAsync` contains exactly one `inventories.LoadAsync`
and the frame is built from its result -- so this is an evidence gap, not a behavioural defect. But
it is a requirement with an acceptance criterion and a planned check, dropped silently, in a
contract whose standing rule is that a record never claims more than its evidence.

### BF-6 -- DP-003 and F-2 state a cause the code does not produce

Both say the raise is reachable from "a player who reconnected between the commit and the answer
being assembled". It is not, by that route. Three lines above the `ReplaceExact`, the same method
already asks `runtimeSessions.TryGet(session, out var state, out _)`, and `TryGet` returns false
unless `entry.Session == session` (`SelectedSessionRuntimeDirectory.cs:44`). A player whose
generation has moved on fails there and the method returns `null` -- no raise, no log line, nothing
to isolate.

What can actually raise is a `RemoveExact` or a `ReplaceExact` from elsewhere landing **between**
this method's `TryGet` and its `ReplaceExact`. The window is not instantaneous: it spans
`await inventories.LoadAsync(...)`, a store read.

Keeping the refresh inside the try/catch is right regardless. The finding is that the recorded
reason is not the code's reason, so a later reader reasoning from it will reason from a case the
code already handles somewhere else.

### BF-7 -- the refresh is a read-modify-write across an await, and the change list rules that out by assertion

`state` is read before `await inventories.LoadAsync(...)` and the other eight members are written
back after it. `ReplaceExact` checks only the session generation, not the content
(`SelectedSessionRuntimeDirectory.cs:21-29`), so the write is last-writer-wins on the result as a
whole. A move whose reload overlaps a refresh from one of the six existing sites therefore restores
that site's member to its pre-change value -- an accepted quest's `World.Content.AvailableQuests`,
a monster death's `CharacterEntry.Public.Extras.CharacterExperience`, and so on.

`behavior-change-list.md` closes R-2 with "no reader's answer changed except through the member the
move changed", which is stronger than the code supports. The hazard is already named as open in this
repository -- `Docs/CharacterEntryFacesInventory.md`, 「未查清的」 item 5: 「**`ReplaceExact` 的并发
语义**未读实现。若它不是 compare-and-swap，击杀与通关同时结算可能互相覆盖」. This change adds the
most frequent participant to that set -- every successful drag -- and the implementation has now
been read, so the question can be answered rather than left.

Either bound it (evidence that dispatch for one session is serialised, which would close the window
for the five message-driven sites) or list it. What is not available is the flat denial.

## Non-blocking observations

- **NB-1.** `plan.md`'s DP-002 attributes the set 「the appearance slots `0..12` and `23`」 to
  `ChangesWhatTheCharacterLooksLike`. That predicate is `CurrentUserStateDrawsFrom`, which is
  `DrawnAppearance.DrawsFrom(x)` -- `x <= 12 || x == 23` -- **or** `x == 24`, `Noti2CreatureSlot`
  (`StateSynchronizationPayloadCodec.Entry.cs:316`, `:831-832`). The decision is unaffected: 13 is in
  none of the three, so the slot-13 argument for the wider rule is **sound**, and the wider rule is
  what the code implements. An existing case in the same test file already states the union
  correctly ("the appearance takes 0..12 and 23, and the creature triple takes 24",
  `TheCharacterChangesAsYouDressItTests.cs:242-243`), so the two records in one file disagree. The
  new case's own remark says "the drawn appearance is worn slots zero to twelve and twenty-three",
  which is true of `DrawnAppearance` and is describing the gate; it reads as the same slip.
- **NB-2.** `A_session_that_will_not_accept_the_refresh_still_gets_its_acknowledgement` asserts the
  answer is a `MoveItemSpaceResponse` and nothing about it. Its sibling
  `A_move_is_still_acknowledged_when_the_appearance_cannot_be_built` asserts `ErrorCode` is null and
  `MoveValue` is 1. Today the move in the first case does succeed -- the P2 run printed the
  acknowledgement with `ErrorCode = ` empty and `MoveValue = 1` -- so the case is currently honest.
  But a refusal would satisfy it just as well, which is the shape spec-003's Successor Guidance was
  written about ("A negative assertion can let a refusal impersonate a success").
- **NB-3.** `reader-enumeration.md`'s independent cross-check does not reproduce as stated. Running
  its own command, `grep -rn "\.CharacterInventory\b" Server/src`, gives twelve lines as claimed, but
  they are not "two ... and the other ten in `SelectCharacterHandler` and
  `SelectedCharacterStateMessageFactory`": two are in those readers' methods, nine are in the two
  named files, and the twelfth is `SelectCharacterPostEntryServices.cs:25`, which the sentence does
  not name. The conclusion survives -- that site is `SelectCharacterQuestEntryService.SynchronizeAsync`,
  reached from `SelectCharacterPhaseCommands.cs:253` with the prepared result of the entry burst,
  not with the resident copy -- but the corroboration a reader is invited to rerun contradicts the
  sentence it corroborates.
- **NB-4.** `reader-enumeration.md` states no revision, and its "Writes back" column now reads false
  for `MoveItemSpaceSlice` -- this change makes that row wrong. It is not among the records the
  change list corrects.

## What was verified and is sound

Recorded so the next round does not re-derive it.

- **The enumeration's method and its count hold.** Running
  `grep -rn "\.GetRequired(\|\.TryGet(" Server/src --include=*.cs` gives exactly **16** lines, all of
  them on `runtimeSessions` or `runtimes`. The record's two exclusions turn out not to be
  load-bearing: `ISessionPresence.cs` declares the members and `SelectedSessionRuntimeDirectory.cs`
  defines them, and neither form matches a leading `.`, so the raw command already yields 16.
- **The frontier is right.** I swept every mention of `SelectCharacterResult` in `Server/src`
  (`grep -rn "SelectCharacterResult" Server/src --include=*.cs`, 57 lines across 20 files) looking
  for a second way to hold the copy. `Publish` hands the value on to `positions.Place` and
  `presence.Admit`, neither of which retains it (`SelectCharacterLifecycleServices.cs:200-207`); the
  `SelectCharacterPhaseCommands` records that carry a `SelectCharacterResult State` carry the
  prepared result through the entry burst and are not read after it; nothing else stores one. The
  directory is the only resident holder, and `GetRequired`/`TryGet` are the only ways out of it.
- **The 2 is right, and R-2 is real.** `SelectCharacterLifecycleServices.cs:155` is
  `CreatePvpParticipant(runtimes.GetRequired(owner))` inside `PlanPvp`, where `owner` comes from
  `pvp.ReadyOn(session.ListenerPort, session.CharacterId)` -- the **other** ready participants -- and
  `CreatePvpParticipant` (`:219-231`) reads `result.CharacterInventory.Containers`' Equipment
  container. `DungeonSelectionSlice.cs:380` -> `:419` does the same for the player's own screen. Both
  take the Equipment container of `CharacterInventory` and nothing else a move can touch. R-2's
  "on another player's screen" reading is correct.
- **The `CharacterEntry` confession is adequate and I found no counter-example.** The two members of
  `CharacterPublicEntryState` that could plausibly depend on equipment are `Appearance` and
  `CloneTitleItemId`; both are built from stored character columns
  (`CharacterEntryStateServices.cs:32,34` -- `new CharacterAppearanceCompatibilityState(1, [.. storage.AppearanceBlob])`
  and `storage.CloneTitleItemId`), not from any equipment list, which is the same shape of answer the
  enumeration gave for `DerivedStats`. `MoveItemSpaceCommandHandler` reaches the store only through
  `ICharacterContainerDomainService` and `IItemMovementDomainService`, so the "a move writes
  `item_slots` and `items` and nothing else" rule holds on the code as well as by assumption.
  `Roster` and `RosterEntry` -- which spec-002 made draw from what is worn -- are read at exactly one
  place, `SelectCharacterHandler.cs:354`, inside the entry burst, so they are not among the sixteen's
  reads at all.
- **DP-001's assertion coverage is complete.** `SelectCharacterResult` has exactly nine members
  (`SelectCharacterModels.cs:35-44`). `Only_the_inventory_member_of_the_session_copy_is_replaced`
  asserts `NotSame` on `CharacterInventory` and `Same` on the other eight -- all nine accounted for --
  and `state with { CharacterInventory = reloaded }` does replace exactly one.
- **The flipped case is an authorized rewrite, and it is the only one.** `The_session_copy_is_left_as_it_was`
  -> `The_session_copy_follows_the_move` corresponds to F-1 and says so in its own remarks, including
  why the earlier trade-off was made and what dissolved it. Reading the whole test diff: the only
  other changes are a new optional `Build` parameter and two new members plus a throw switch on
  `RecordingRuntime`. No other expected value or assertion in the suite was touched.
- **The hash is the delivered file's.** `sha256sum Server/src/.../MoveItemSpaceSlice.cs` computes
  `d405a635c7d9e5888faca31e89acee064497110680b514004ca30ffef1fbea25`, which is the value
  `behavior-change-list.md` states. This round fixed the "the hash pins the mutation moment"
  problem of the previous two.
- **Boundary.** Only the three tracked files above are modified. `SelectCharacterAcceptedTranscriptTests.cs`,
  `CommandHandlerArchitectureTests.cs` and `NotificationHandlerArchitectureTests.cs` are untouched and
  green in the run above. No new message record, no new `ProtocolModule` wiring, no new injected
  dependency -- `runtimeSessions` and `inventories` were both already constructor parameters of
  `MoveItemSpaceHandler`. No cross-protocol reference was added inside the slice. The build is clean
  under `TreatWarningsAsErrors` with CS9113 as an error, which also means no primary-constructor
  parameter was orphaned. Files are LF throughout and `git diff --stat` after each mutation and
  restore showed no line-ending rewrite.
- **The gate is right and its check earns its place.** The refresh runs under
  `result?.Status is ItemMoveStatus.Moved`, so a refusal does not refresh
  (`A_refused_move_leaves_the_session_copy_alone`), and the wider rule is distinguished from the
  narrower one by an actual check, verified above by restoring the narrow gate and watching exactly
  one case redden.

## Disposition

`changes_required`. BF-1, BF-2 and BF-5 are gaps between what the approved artifacts promised and
what was built; BF-3 is a C-8 miss on a record that is read repository-wide; BF-4, BF-6 and BF-7 are
records claiming more, or other, than the code and the runs support. None of them is a defect in the
refresh itself -- the mechanism is correct, minimal, and the one-member property is genuinely
guarded.
