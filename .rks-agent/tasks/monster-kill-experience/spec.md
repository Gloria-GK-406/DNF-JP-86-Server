---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Killing a monster is worth experience

## Why and Outcome

A character can gain experience only by handing in a quest. Kills answer with the number the
character already had, so a dungeon run pays nothing and the client's popup shows a total that never
moves.

Afterwards, killing a monster in a dungeon raises the character's stored experience by an amount
that can be checked against a captured reference exchange, levels the character when the total
crosses a threshold, and tells the client where the character now stands. Experience granted by a
kill and experience granted by a quest reach the character through the same route.

## Task Perspective

- Core outcome: A kill is worth experience -- the character's stored total rises by a determinate
  amount, and the client is told the new standing.
- Necessary support: One route for granting experience, shared with quest completion, because two
  routes is how this code already produced a wrong total once; and a level crossing carrying its
  consequences, because the offer list is derived from level and a quest whose band has just been
  entered must become visible.
- Peripheral assurance: The award readable afterwards as its named parts, and every value taken from
  the reference server without independent evidence marked where it is written.

## Scope and Non-Goals

### In Scope

- The experience one monster kill is worth, determined by the monster, the run it happened in, and
  the character who made it.
- Experience-increasing entitlements a character carries, contributing named amounts on top of what
  the kill is worth, with an account-level premium entitlement as the worked case.
- Granting experience -- raising the total, crossing level thresholds, persisting -- as one
  behaviour reached by both the kill and the existing quest-completion path.
- What the client is told after a kill, including what a level crossing requires beyond the
  experience frame.

### Non-Goals

- The per-level worth of a monster, which is already settled and unchanged.
- What quest completion pays. Only the granting is shared; its own calculation is untouched.
- Drops and gold from a kill.
- Timed character effects, and therefore the elixir family of entitlements: nothing in this server
  can carry an effect with a lifetime, and introducing that is a separate subject.
- Fatigue.
- Party play. Every captured sample is solo.

## Constraints

- Frames this server already sends keep their bytes. The kill path may add what a level crossing
  requires, and nothing else.
- Granting experience ends with one behaviour, not one per source.
- A value whose only source is the reference server is marked as such where it is written, and the
  level-differential penalty states which of its cells the capture covers.

## Definitions

| Term | Meaning |
|---|---|
| Kill worth | What killing one monster is worth before anything the killing character carries is applied. Determined by the monster, the dungeon, and the level gap; the same for any character of the same level. |
| Entitlement contribution | An amount added on top of kill worth because of something the character or account carries. Named, so an award can be read back as its parts. |
| Level crossing | An award taking the stored total to or past the threshold at which the character's level increases. |
| Captured award | One of the four amounts the reference server put on the wire on 2026-08-27 for a known monster level, monster rank and character level. |

## External Behavior and Data Rules

- The client learns a kill's outcome from the experience notification it already receives, which
  carries the character's level and total; after this change the values in it move.
- Monster rank and monster level reach the calculation as the client was already told them in the
  room's actor list; a monster whose map declares no level is worth what its dungeon's basis level
  says, as it already is on the wire.
- An entitlement that has expired contributes nothing.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Killing a monster in a dungeon raises the killing character's stored experience. |
| RQ-002 | The amount a kill is worth equals the captured award for that monster level, monster rank and character level, for each of the four captured combinations. |
| RQ-003 | A monster's rank changes what its kill is worth, in the proportion the captures show between an ordinary monster and a champion of the same level. |
| RQ-004 | The gap between the character's level and the monster's changes what a kill is worth, and the one gap band the captures cover behaves as they show. |
| RQ-005 | An award that takes the stored total to or past a level threshold raises the character's level, and an award that does not leaves it unchanged. |
| RQ-006 | A level crossing refreshes the state derived from the character's level, so a quest that the new level makes offerable is offered without re-entering the world. |
| RQ-007 | The client is told the character's level and total after a kill, and both reflect the award. |
| RQ-008 | Experience granted for a quest completion and experience granted for a kill raise the total, cross thresholds and persist by the same behaviour. |
| RQ-009 | Quest completion continues to award what it awarded before, and continues to persist it. |
| RQ-010 | An award can be read back afterwards as its parts: what the kill was worth, and each entitlement's contribution under its own name. |
| RQ-011 | An account entitlement that increases experience adds a contribution to a kill's award; one whose term has ended adds nothing. |
| RQ-012 | Every value carried from the reference server without independent evidence is marked as such where it is written, and the level-differential penalty names the cell the captures cover. |
| RQ-013 | The frames this server already sends are byte-identical to before, apart from the values the experience notification carries. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-007 | A character in a dungeon with a known stored experience total | It kills a monster | The stored total is higher, and the client is told the new level and total |
| AC-002 | RQ-002 | A level-2 character in the captured dungeon at its captured difficulty | It kills an ordinary monster of level 5, then one of level 4 | The awards are the captured 75 and 65 |
| AC-003 | RQ-002, RQ-003 | The same character and dungeon | It kills a champion of level 5, then one of level 4 | The awards are the captured 153 and 132 |
| AC-004 | RQ-002, RQ-004 | A level-3 character in the captured dungeon | It kills ordinary monsters of level 5 and level 4 | The awards are again 75 and 65, the gap band being unchanged |
| AC-005 | RQ-005 | A character whose stored total is one short of its next level threshold | It receives an award larger than that shortfall | Its level is one higher and the surplus is kept |
| AC-006 | RQ-005 | A character far below its next threshold | It receives an award smaller than the shortfall | Its level is unchanged and the total has risen |
| AC-007 | RQ-006 | A character carrying no offer for a quest whose level band opens at the next level | A kill takes it across that threshold | That quest appears among the offers the client is told about, without re-entering the world |
| AC-008 | RQ-008, RQ-009 | A character carrying a quest whose objective is met | It hands the quest in | It is awarded what it was awarded before, and the total is persisted |
| AC-009 | RQ-010 | A character with one experience-increasing entitlement | It kills a monster | The award is recorded as the kill's worth plus that entitlement's contribution, each named |
| AC-010 | RQ-011 | A character whose account holds an unexpired experience entitlement, and one whose entitlement has ended | Each kills the same monster | The first award exceeds the second, and the second equals the kill's worth alone |
| AC-011 | RQ-012 | The delivered code | It is read | Values with no independent evidence are marked, and the level-differential penalty names the covered cell |
| AC-012 | RQ-013 | A character entering the world | Its entry exchange is compared against the reference session | It matches as it did before this change |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-002, RQ-003, RQ-004 | AC-002, AC-003, AC-004 | Automated checks pinned to the four captured awards, failing if any moves |
| EV-002 | RQ-005 | AC-005, AC-006 | Automated checks over a threshold crossing and a non-crossing award |
| EV-003 | RQ-008, RQ-009 | AC-008 | The quest-completion path's existing automated checks, unchanged and passing |
| EV-004 | RQ-010, RQ-011 | AC-009, AC-010 | Automated checks over a character with, without, and past an entitlement |
| EV-005 | RQ-001, RQ-006, RQ-007 | AC-001, AC-007 | A live session against this server: recorded award, higher stored total, and the client's own behaviour |
| EV-006 | RQ-012 | AC-011 | The delivered code read as evidence |
| EV-007 | RQ-013 | AC-012 | The existing suite passing, and an entry-batch comparison against the reference session |

## Assumptions

- The level-differential penalty is carried from the reference server. The captures cover one of its
  cells -- a monster one to three levels above the character -- and the rest are that server's word.
- The premium entitlement's rate is server policy: the archive declares no rate for it, unlike the
  rates an item declares for itself.
- Whether two simultaneous entitlements add their separately-rounded contributions or compound
  cannot be decided from the captures, all of which have one contribution in play. With one
  entitlement implemented, no observable result depends on the choice.
- The captured dungeon's difficulty and experience weight are read from the archive as they are
  today; the captures were taken at that difficulty.
