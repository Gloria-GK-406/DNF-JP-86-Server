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

## Row 12 offers a narrower set than the legacy rule

The offered quest list applies the legacy grade whitelist, the repeatable exemption, the
level band, the job family and the grow type. The legacy index applies more, and the rest
need data the accepted content source does not carry:

| Legacy filter | Status here |
|---|---|
| Selectable grade | applied |
| Repeatable grades exempt from the cleared exclusion | applied |
| Level band, job family, grow type | applied |
| Exposed-by-NPC | not applied — the definition carries no exposure field |
| Event flag | not applied — not modelled |
| Expert-job requirement | not applied — not modelled |
| Creature kind / pet-evolution eligibility | not applied — not modelled, and matrix row 12 names it as an input |
| Target character | not applied — not modelled |
| Prerequisite chain | not applied — no prerequisite catalogue exists here |
| Job-change grow-type branch | not applied — the legacy branch masks the grow type, which this contract does not express |

A character is therefore offered more quests than the legacy server offers them. Closing
the gap needs the content source widened first, which is a separate outcome.

Two details worth keeping straight, because both read the other way round at a glance: a
*random* daily is offerable but **not** repeatable — the legacy exemption names only daily,
normally-repeat and special-daily — and the system grade **is** offerable, despite sounding
internal.

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
