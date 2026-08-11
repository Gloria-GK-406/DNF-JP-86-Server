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
| X-15, X-16 | The interrupted run the character may return to, and why they may not when they cannot | `SessionAdjacentEntryState.DungeonRejoin` |

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
