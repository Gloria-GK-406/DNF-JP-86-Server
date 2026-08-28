---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Killing a monster is worth experience

## Context

### Source Request

「编写sdd实现吧」, and immediately after: 「提交，然后检查基线干净。不干净就修复。然后启动」.

The request closes a design conversation held across 2026-08-27/28 in which the shape of the
calculation was settled turn by turn. Its subject was named earlier the same day, when the player
reported three things after a live dungeon run: monsters drawn as bosses, no experience from kills,
and fatigue stuck at 188. The first was a misreading of the archive and is fixed and committed. The
third is unimplemented and recorded in `Docs/DungeonEntryNotes.md`. This contract is the second.

### Current Situation

`MonsterDeathHandler` answers a kill with the experience the character already had. Nothing grants
anything: the frame is sent so the client's popup has a number, and the number never moves. The
same is true of every other combat reward -- drops are always empty, clear rewards are zero -- so a
character can only gain experience by handing in a quest.

What the calculation needs is in hand and was not a week ago:

- **The base is a formula, not a table.** `MonsterExperienceWorth` (committed at `dc4a9116`) gives
  what a monster of a level is worth, recovered as a closed form that reproduces ninety-nine of the
  reference server's hundred hard-coded numbers.
- **Six kill samples with the arithmetic behind them.** A run through dungeon 144 against the
  reference server on 2026-08-27 produced four distinct outcomes -- level 4 and 5 monsters, ordinary
  and champion, awarding 65, 75, 132 and 153 -- and that server's own log recorded every
  intermediate value, so each is reproducible step by step rather than only end to end.
- **The rates are in the archive.** `Etc/ServerParameter.etc` declares the difficulty and party
  rates; each dungeon declares its own `[experience increasing point]`.

Two facts about the surrounding code shape the work. The experience-granting half -- add, cross the
level thresholds, persist -- already exists inside the quest-completion command, where it was
written once and immediately produced a defect: two completions of one quest both wrote the same
total, because the level and experience were read from the entry-time session snapshot rather than
from the store. Adding a second copy for kills would reproduce that class of defect. And crossing a
level threshold has consequences beyond experience: the offer list is computed from the character's
level, and a quest whose level band has just been entered has to appear.

## Goal

Killing a monster in a dungeon increases the character's experience by an amount the reference
capture can be checked against, levels the character when the total crosses a threshold, and tells
the client the new standing -- with the granting itself implemented once for every source of
experience rather than once per source.

## Scope

### In

- Experience awarded for a monster kill, derived from the monster, the run, and the character.
- Experience-increasing entitlements the character carries, reported as named contributions rather
  than folded into one number, with the account premium entitlement as the worked example.
- One implementation of granting experience -- adding it, crossing level thresholds, persisting --
  reached by both the kill path and the existing quest-completion path.
- What the client is told after a kill, including whatever a level crossing requires beyond the
  experience frame itself.

### Out

- `MonsterExperienceWorth`, which is committed and unchanged by this task.
- Clear-settlement and quest-completion experience *amounts*. Only their granting is shared; the
  quest's own calculation stays as it is.
- Monster drops and gold, which are the other half of a kill's reward and have neither evidence nor
  a request behind them yet.
- The timed character effects an elixir would create. The rate such an item declares is content that
  this task reads for no one, because nothing in this server can carry an effect yet; introducing
  that concept is its own subject.
- Fatigue, recorded separately in `Docs/DungeonEntryNotes.md`.
- Party play. Every observed sample is solo, and a party rate that no evidence exercises would be a
  guess in the middle of a checked calculation.

## Constraints

- The wire stays as it is. Every frame this server already sends and has verified must keep its
  bytes; the kill path may add what a level crossing requires and nothing else.
- Experience granting must end up with one implementation. A second copy is the defect this task
  exists partly to remove, and leaving the quest path on its own copy would leave it in place.
- Numbers whose only source is the reference server must be visibly marked as such where they are
  written. That server is a reimplementation, not an authority; today it has twice been wrong in a
  way that cost hours, and the cost fell on values that looked settled.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | A kill in a dungeon increases the character's stored experience, and the client is told the new total. | A live session against this server: the log records the award, the stored experience is higher afterwards, and the client's experience frame carries the new value. |
| SC-2 | The award reproduces every one of the four captured outcomes -- level 4 and 5 monsters, ordinary and champion -- for the character levels those samples were taken at. | Automated checks over the captured values, failing if any of the four moves. |
| SC-3 | Crossing an experience threshold raises the character's level, and what a level change requires beyond the experience frame happens too. | A live session, or automated checks over a threshold crossing, showing the new level in the client's frame and the level-dependent state refreshed with it. |
| SC-4 | Quest completion and monster kill grant experience through the same implementation, and quest completion still awards what it did before. | The quest-completion path no longer carries its own granting; its existing checks still pass unchanged. |
| SC-5 | An award can be read back afterwards as its parts -- what the kill was worth and what each entitlement added, each named. | A live session's log showing an award broken into named parts. |
| SC-6 | An entitlement that increases experience is applied, and one that has expired is not. | Automated checks over a character with and without the entitlement, and over an expired one. |
| SC-7 | Every value carried over from the reference server without independent evidence is marked where it is written, and the level-differential penalty says which of its cells the capture actually covers. | The written code and its remarks. |
| SC-8 | The frames this server already sends are unchanged. | The existing suite passes, and an entry batch compared against the reference session still matches as it did before. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 「编写sdd实现吧」 (2026-08-28), following the agent's design proposal that the user had approved with 「可以，这样看着就很干净了」; then 「提交，然后检查基线干净。不干净就修复。然后启动」. | Implement kill experience as described here, including refactoring the existing quest-completion granting onto the shared implementation; create the task's own workspace and artifacts; accept one reviewed result as a local commit, or roll one unaccepted attempt back. |

## Assumptions

- The level-differential penalty is carried over from the reference server. Only one of its cells --
  a monster one to three levels above the character, which is every captured sample -- is supported
  by evidence; the rest are that server's word alone. Applying them is deliberate and provisional.
- The channel and premium rates have no declaration in the archive, so they are server policy rather
  than content. A rate an item declares is read from that item; these two are not.
- Whether simultaneous entitlements add their separately-rounded amounts or compound cannot be
  decided from the samples in hand, all of which have exactly one bonus in play. The two readings
  agree on every observed value and diverge only with two bonuses at once.
- Solo play. The party rate and the division by party size are part of the calculation and are
  exercised only at one member.
