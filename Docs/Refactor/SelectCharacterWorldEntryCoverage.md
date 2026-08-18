# Select Character — World and Session Entry Coverage

What the G5 world, content and session-adjacent entry state covers, and what it
deliberately does not. Recorded so a later reader can tell a gap that was decided from a
gap that was forgotten.

Every protocol claim here is reversed from the legacy code. No packet capture was
performed, and nothing below should be read as capture-confirmed.

## Modelled: rows that produce business state

| Row | What it carries | Where it lands |
|---|---|---|
| 12 | The quest list offered to this character, with level, job, grow type and cleared set already applied | `WorldContentEntryState.AvailableQuests` |
| 14 | Today's challenge state: slots the character's level opens, the reward band they earn, whether they claimed it | `WorldContentEntryState.DailyChallenge` |
| 32, 33 | Each configured collection box's grid with the character's collected items marked | `WorldContentEntryState.CollectionBoxes` |
| 58 (content) | The equipped creature's welcome lines, or nothing when none applies | `WorldContentEntryState.PetWelcome` |
| X-08 | Whether the channel admits this character, and why not when it does not | Raised as a refusal before publication |
| L-04 | Where the character stands, and whether that position is written back | `ChannelEntryPolicyState.Spawn`, `.PersistsPosition` |
| L-06 | Whether a duelling skill profile applies at all | `ChannelEntryPolicyState.PvpSkillProfileApplies` |
| X-18 | Who else is in the arrival area on the same listener | `SessionAdjacentEntryState.TownPresence` |
| X-17 | Whether the entering character owns an open expert store | `SessionAdjacentEntryState.ExpertStore` |
| X-09, X-10 | The duelling lobby's ready characters and offered rooms, or nothing when duelling does not apply | `SessionAdjacentEntryState.PvpLobby` |
| X-15, X-16 | The interrupted run the character may return to | **Not modelled.** This game has no rejoin, so the transcribed machinery was removed on 2026-08-18. See `Docs/领域结构重构设计.md` section 7. |

## Not modelled: protocol constants

These are emitted unconditionally by the legacy server and depend on no loaded state.
They are Networking's to emit, and creating business state for them would invent a source
that does not exist.

| Row | Legacy body | Basis |
|---|---|---|
| 17 | Three bytes: a zero count and a zero tail | `EventInfoBodyBuilder` sends a fixed empty list. Its own comment records that the capture-era event list is meaningless for a standalone server and that every character but the seed one has always been empty and worked. |
| 31 | Eleven bytes: count 2, then `(0, 0)` and `(2, 0)` | `ShowEffectBodyBuilder` holds the body as a literal and records it as fixed across the whole real-machine sample. |
| 53 | Empty body | `EmptyBodyBuilder` reads no source data. The marker's purpose is not established. |
| 56 | Fixed empty friend list | `UnitedServerFriendInfoBodyBuilder`. Cross-server social state has no source on a standalone server; empty is the legacy behaviour, not a stand-in for one. |

## Not modelled: unconfirmed meaning

Naming these would assert a meaning the evidence does not support.

| Row | What is there | Why it stays unnamed |
|---|---|---|
| 34 | A fixed integer zero | `BossTowerBodyBuilder` calls it a placeholder and no loaded state feeds it. The migration matrix explicitly directs that no character state be created for it. |
| 49, 50, 51 | Three tuples of four integers, persisted per character | The legacy source is named `Unknown725Packets`. The tuples are real and character-owned, and G3 already carries them; only their meaning is unknown. |
| 52 | One byte from an initialization field | The legacy field is `PcRoomPlayTimeState`. Both its ownership and its applicability are unclear, so it is not attributed to account or character state here. |

## Not modelled: a matrix claim the code does not support

**Row 18 — dungeon permission "with admission overlay".** The migration matrix describes
this row as persisted character permissions transformed by account and channel login
policy, and assigns it jointly to the dungeon progress and channel admission services.

The legacy projection takes no channel input. `DungeonPermissionProjector.ProjectForClient`
neither mentions nor receives a channel or listener — a search for either term in that file
returns nothing — and the permissions arrive directly from the character's own persisted
rows. The projection it does perform is deduplication by dungeon, keeping the highest clear
state, in first-seen order, over entries that have been cleared at all.

Row 18 therefore has no world or channel limb. Its requirement was withdrawn from this work
with the evidence recorded, rather than satisfied by implementing an overlay the legacy
server does not have.

## Row 12 selectable-quest predicate matrix

This is the complete decision set reversed from
`GameWorld/QuestRelationIndex.ComputeAcceptableQuests`, including the evaluator it calls in
`GameWorld/QuestPrerequisiteDefinition`. Definition fields come from `n_quest/quest.lst`
and its `.qst` files unless another file is named. Character values are the already-loaded
entry state; no repository or archive type crosses into the application rule.

Predicate category index for the completeness check: exposed, event, creature, expert,
training, target, prerequisite, collision, question, and job-change.

“Apply” below is the required G5 disposition. “Reject” means omit that one definition, not
return a partial placeholder. Failure to read the authoritative quest catalogue fails
composition, so selection cannot be published. This is stricter failure signalling than the
legacy catalogue's log-and-empty fallback, but it preserves the legacy property that missing
definitions never make more quests visible.

| # | Legacy predicate | Definition source | Character/server source | G5 disposition and uncertainty |
|---:|---|---|---|---|
| 1 | First authoritative, client-representable identity | `quest.lst` order and quest ID | none | Apply: retain the first occurrence of each ID in list order; reject IDs outside `1..29999`, a missing quest file, or a malformed mandatory definition. A catalogue/list read failure aborts composition. |
| 2 | Exposed by NPC | `[exposed by npc]` | none | Apply: numeric `0` is hidden. Absence is an optional unrestricted value; a declared non-integer is malformed and rejected rather than inheriting the legacy parser's accidental `-1` permission. |
| 3 | Not event-only | `[event]` | standalone server feature set | Apply: a non-zero event declaration is excluded. The standalone entry path has no event authority that could make it offerable. |
| 4 | Creature-evolution kind | `[creature kind]`, with `[creature level]` used to establish the equipped creature's eligible evolution state | equipped creature definition, identity and level | Apply: a declared kind must be in the eligible-kind set derived from the equipped creature; absent creature state is not a match. Missing declaration is unrestricted. |
| 5 | Expert-job gate | `[expertjob level]` type/level pair | selected character expert-job facts, if an evidenced level can be established | Conservatively reject every declared expert-job restriction, matching the complete legacy selectable loop. That loop receives no expert-job subject and rejects all six such definitions; current expert-job persistence does not by itself prove a different offerability rule. |
| 6 | Daily training availability | `[grade] == [training]`, quest `[npc index]`, plus `n_quest/TrainingQuest.lst` level-to-NPC pairs | character level and server training catalogue | Apply: the exact `(level, NPC)` pair must exist. The supplied `TrainingQuest.lst` contains only one scalar in each `[level]` block and no NPC operands; running the exact legacy loading algorithm therefore produces no level-to-NPC mapping. Production truthfully loads an empty mapping, so every training quest fails closed on this archive. Missing or malformed training data is likewise no match; focused fixtures preserve the exact-pair rule for other valid archives. |
| 7 | Selectable grade | `[grade]` | none | Apply the exact whitelist: absent, `[normal]`, `[side]`, `[sub]`, `[epic]`, `[training]`, `[achievement]`, `[daily]`, `[daily random]`, `[normaly repeat]`, `[special daily]`, `[common unique]`, `[system]`. Reject other or malformed declarations. |
| 8 | Target character | `[target character]` | character numeric job mapped through the legacy base/AT family table | Apply the legacy family-name containment test. Absence is unrestricted; an unknown job or a declared target with no matching family is rejected. The embedded grow-number tuples are not a separate predicate in the legacy loop. |
| 9 | Inclusive level band | `[level]`, default `1..99` in the legacy loop | character level | Apply inclusive minimum/maximum. Absence is optional and unrestricted under the legacy default; a malformed declared band is rejected. |
| 10 | Job family | `[job]` | the same legacy base/AT job mapping | Apply: absent/empty and `[all]` are unrestricted; otherwise the declaration must contain the mapped family token. Unknown character job or malformed/unknown declaration is no match. |
| 11 | Job-change grow-type branch | `[grow type]` and `[job change quest]` | character grow type | Apply three distinct branches: values `2`/`3` always compare a declared quest grow type to `characterGrowType & 0xF`, including when the character value is negative; values `1`/`10`/`20` exempt the grow restriction; otherwise a declared quest grow type is compared exactly only when `characterGrowType >= 0`. A negative character value therefore does not exclude an ordinary-branch quest in the legacy loop. G5 must not use that permissive sentinel for missing mandatory character state: failure to establish the selected character's grow type fails composition before this predicate. Missing quest grow type (`-1`) remains unrestricted in every branch. |
| 12 | Cleared non-repeatable exclusion | quest ID and `[grade]` | complete character cleared-quest IDs | Apply: exclude a cleared quest unless its grade is exactly `[daily]`, `[normaly repeat]`, or `[special daily]`. `[daily random]` is selectable but not repeatable. |
| 13 | Completed-quest prerequisites | every `[pre required quest]` occurrence | complete character cleared-quest IDs | Apply: IDs within one occurrence are AND; repeated occurrences are OR alternatives. An empty declaration contributes no restriction. A candidate group containing an unknown quest is discarded; if no declared group remains valid, reject the owner definition. Other malformed/self/non-positive references reject it. |
| 14 | Question-answer prerequisite | every `[pre required quest answer]` quest/zero-based-answer pair | complete character cleared-flag map; the stored required value is `answerIndex + 1` | Apply: all pairs must match exactly. Odd, negative, unknown, self-referential or otherwise malformed pairs reject the owner definition. The question quest's `[type]` is descriptive; the actual offerability predicate is this flag comparison. |
| 15 | Character collision | every `[collision quest]` occurrence | complete character cleared IDs; active quest IDs only where that caller supplies them | Apply: any known collision already cleared or active rejects the quest. The select-character legacy caller supplies no active set, so its observed entry answer tests cleared collisions only. Unknown collision references are ignored as legacy diagnostics; malformed/self/non-positive declarations reject the owner definition. |
| 16 | Account collision | every `[account collision quest]` occurrence | account-cleared quest IDs | Apply fail-closed when declared: without complete account state the definition is rejected. The supplied archive declares none, so this branch is presently inapplicable rather than silently implemented as character collision. |

The supplied `Script.pvf` was scanned through the production source rather than selected
text samples. `quest.lst` has 2,740 unique in-range, file-backed entries; validation retains
2,719 definitions after rejecting eight combined/non-selectable grade declarations and
thirteen prerequisite owners with no valid alternative. The retained catalogue has 341
explicit exposure values, 306 event declarations, 15 creature restrictions, six expert-job
restrictions and 156 target-character restrictions. Answer and collision relationships are
populated and validated, and no retained definition declares an account collision.

A direct legacy-parser execution against the same `TrainingQuest.lst` confirms zero exact
level-to-NPC mappings; level scalars are not NPC identifiers. These facts establish the
production rule inputs but do not prove the client expects row 12's bytes. As with every
protocol statement in this document, packet shape and client interpretation remain
code-reversed, not capture-confirmed.

## Town co-presence spans channels

The legacy directory takes the listener as an optional filter, and the caller that announces
arrivals and departures passes none. Town co-presence is therefore cross-channel there: two
characters in town 1 area 0 on different channels see each other. That reads like an
oversight, but it is the accepted behaviour and narrowing it here would silently change who
sees whom, so it is reproduced and recorded instead.

A character is only reported as present when they are actually standing where their record
says. The legacy directory excludes anyone who has walked into a dungeon, because their town
coordinates are left over from before they entered; it also requires arrival to have
finished, no blocking user state, and a live connection. All of that is carried as a single
flag on the presence record, which the lifecycle that admits a character owns keeping true.

## Scope of the identity check on returning to a dungeon

The legacy registry checks ownership of an interrupted run on account, character and a wire
participant identifier. This layer has no wire participant identifier yet — it identifies
characters by `CharacterId` and leaves wire numbering to the projection — so ownership is
checked on account and character only. That is a subset of the legacy check, recorded here
rather than quietly widened or quietly narrowed.

## A read that no longer writes

The legacy lookup for an interrupted run renews an idle deadline and can retire a lapsed
record as a side effect of being asked. Those are lifecycle transitions and are deliberately
not reproduced: composing an entry must not keep a stale run alive. The view notices that a
deadline has passed and reports it, but leaves the record alone. Retiring it belongs to the
entry lifecycle, which is not migrated yet.

## The duelling lobby loses an ordering the protocol can see

The legacy lobby orders its ready participants by the wire user identifier, and that order
is visible in the roster the client receives. This layer identifies characters by
`CharacterId` and leaves wire numbering to the projection, so the lobby is ordered by
character instead — stable, but not the same sequence. The projection cannot restore the
legacy order from what the view carries. Whether the client depends on it is unconfirmed.

The legacy publish check also requires a constructed game session and a clear user state
before a character counts as ready. Here, being marked ready *is* the condition; the finer
checks belong to whoever marks them, which is the duelling flow and is not migrated.

## Channel identity depends on two configuration sections agreeing

The legacy check is a hardcoded port comparison and cannot fail. Here the listener is
matched against the channel catalogue, and an unmatched listener resolves to an ordinary
channel. Listeners come from the networking ports; the catalogue comes from the channel
section when it is present. Configure a channel list that omits the restricted channel while
still listening on its port, and an under-level character is admitted and their transient
position persisted. The fallback is deliberate — refusing an unlisted listener would make an
unconfigured deployment unplayable — but the coupling is a real trap. The legacy proxy port
for the restricted channel has no analogue here either.

## What is empty because nothing writes to it yet

The presence, duelling-lobby and interrupted-run registries exist for the server's lifetime
and always answer. Nothing admits to them on the production path, because the code that
would — session registration, lobby readiness, dungeon detachment — belongs to the entry
lifecycle, which is a later group.

So on a running server today every one of these views answers empty, and that answer is
honest: the registry was consulted and held nothing. It is not the same as the previous
attempt's emptiness, which came from having no registry at all. The tests cover both a
populated registry and an empty one, and separately prove the registries resolve from the
production service graph and that a write is visible to the next read.
