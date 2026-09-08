---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: A restated move does not undo itself

## Why and Outcome

A move now reads whichever of its two named ends holds something. That is what made taking a costume off
work. It also means that if the client sends a take-off frame a second time, after the server has already
carried it out, the frame reads as the reverse move and the costume goes back on -- in front of the player,
because the server now pushes an appearance frame after a move.

The outcome is that a frame describing a move that has already happened is refused, and that no frame
describing a move the player is actually making can be refused by the same rule.

## The Shape of the Two Frames

Read from this repository at `19fa03d6`; the live observations are marked apart.

A request carries an instance value on each end, decoded from `body[3..7]` and `body[14..18]`. The client
names the item on the end the item is coming **from**:

| | Source end | Destination end |
|---|---|---|
| Fresh put-on (11 observed live, 2026-09-06) | slot the item sits in, `srcIV` = its id | slot to wear it in, `dstIV` = 0 |
| Take-off (1 observed live, 2026-09-08) | empty cell the player released over, `srcIV` = 0 | worn slot the item sits in, `dstIV` = its id |

`PutOnAsync` already guards a restated put-on: source empty, destination worn, and `srcIV` equal to the
worn item's id. Read as a sentence, that condition is **the client says the item is here, and the store
says it is there**.

A restated take-off produces the mirror of that sentence and does not reach the same branch. After the
take-off has been carried out, the source end holds the costume, so a verbatim restatement finds a piece
there and goes down the ordinary put-on path.

| After a take-off `1:0 -> 3:1`, `dstIV = 413560000` | |
|---|---|
| Source, avatar slot 0 | now holds 413560000 |
| Destination, equipment slot 1 | now empty |
| What the frame still says | the item is at the destination |

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | A frame whose named end disagrees with the store -- it names an item's identity on one end, the store holds that item on the other, and the end it names is empty -- does not carry out the move it describes. |
| RQ-002 | The rule is stated once and reads the same in both directions, rather than as two conditions that happen to be mirror images. |
| RQ-003 | The destination end's instance value reaches the place the decision is made. |
| RQ-004 | No frame whose naming agrees with the store is refused by this rule, whatever its instance values are: a fresh put-on, a fresh take-off, a swap onto an occupied slot, and a move whose instance values are zero all behave exactly as they did. |
| RQ-005 | The premise that a restatement carries the original frame's bytes is written down as a premise, together with what the guard does if it is false. |
| RQ-006 | Nothing about the creature-and-earring case of issue #13 changes. |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | Given a costume taken off, when the same frame arrives again, the store is unchanged and the answer is `MoveAlreadyMade` carrying the item's id. |
| AC-002 | RQ-001, RQ-002 | The already-guarded direction still answers `MoveAlreadyMade` for a restated put-on, and it does so through the same rule rather than a separate one left beside it. |
| AC-003 | RQ-004 | A fresh put-on from the same two cells is carried out. The fixture differs from AC-001 only in which end names the item, so a guard that ignored that distinction would redden this. |
| AC-004 | RQ-004 | A fresh take-off is carried out. |
| AC-005 | RQ-004 | A swap -- putting an item onto a slot that already holds a different one -- is carried out, including when `dstIV` names the item already worn. That value is the client naming what it is replacing, not a contradiction. |
| AC-006 | RQ-004 | A move with both instance values zero behaves as it did. This is what a client that does not fill them, or a frame shorter than 18 bytes, produces. |
| AC-007 | RQ-003 | The destination instance value is carried on the command, and every existing construction site still compiles without being edited. |
| AC-008 | RQ-001..RQ-004 | Mutations: widening the guard to ignore which end names the item reddens AC-003; removing it reddens AC-001; and at least one mutation reddens a check through a value mismatch rather than a raised exception. |
| AC-009 | RQ-005 | The record states the premise as a premise, says a restatement has never been observed, and says the guard simply never fires if the client rebuilds the frame instead of replaying it. |
| AC-010 | RQ-006 | The guard's condition is shown not to reach slot 24, either by a check or by a recorded reading of the condition against that case. |
| AC-011 | all | The suite passes against the 2401 / 2 / 0 baseline, output retained and searched for `[FAIL]`, `error CS` and `MSB<digit>`; any failure identified by exception type and stack root before it is judged. |

## The Rule, Stated Once

> A request names an item's identity on the end that item is coming from. If the end it names is empty and
> the store holds that item at the other end, the move it describes has already been carried out.

Both directions are that one sentence. Its safety comes from being a contradiction rather than a
resemblance: a fresh drag never names an item on an end that does not hold it, so a frame that does is not
a fresh drag under any reading.

## Success Evidence

- EV-001 (AC-001..AC-006): the checks, whose case names say which of the six shapes each is.
- EV-002 (AC-008): the mutation record, with what each changed, which cases reddened, and **how**.
- EV-003 (AC-009, AC-010): the record.
- EV-004 (AC-011): the retained run output.

## Assumptions

- **A restatement carries the original frame's bytes.** Never observed. If the client instead rebuilds the
  frame from its new state, the restatement is byte-identical to a fresh put-on and nothing distinguishes
  them; the guard then never fires and the defect stands. This costs nothing either way, because the guard
  can only refuse a self-contradictory frame.
- The eleven put-ons and the one take-off are the whole of the live evidence about which end carries the
  identity.
