---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Explicit Domain, Rule and Projection Service Taxonomy

## Why and Outcome

Twenty-two abstraction interfaces share the suffix `Service` while behaving in three incompatible
ways. Ten-plus of them read or write the game world, so calling one is order- and lock-sensitive.
Two are deterministic functions of archive content. Eight are pure record-to-value conversions with
no dependencies and no I/O. A caller reading a name or a call site cannot tell which it is holding,
and the difference decides whether ordering matters.

After this change, every one of those interfaces states its category in its own name, one
implementation no longer carries three unrelated responsibilities, and nothing the server does
differs from before.

## Task Perspective

- Core outcome: a developer can tell from an interface's name alone whether calling it touches the
  game world, computes from content, or converts a record — without opening the implementation.
- Necessary support: observable server behavior is unchanged, and the account-to-character binding
  invariant survives the separation of the online-state implementation.
- Peripheral assurance: the test suite, the shape of the test diff, and the subagent review that
  distinguishes a rename from a rename carrying a behavior change.

## Scope and Non-Goals

### In Scope

- Each of the twenty-two `*Service` abstraction interfaces carries a category-bearing name, or is
  recorded as a deliberate exception.
- Every consumer of those interfaces refers to them by their new names.
- Implementation types named after a renamed interface carry the matching name, so the category
  stays legible where the implementation is read.
- The online-state implementation exposes its account, online-user and active-character
  responsibilities as separate implementation types.
- The resulting taxonomy, including its single exception, is recorded where a later reader finds it.

### Non-Goals

- Any change to server behavior, emitted frames, persisted data, ordering or timing.
- Any change to a member's parameters, return type or semantics beyond the interface type names it
  mentions.
- Splitting the six-tuple selected-session identity type.
- The party aggregate, dungeon-entry work, a projector layer, deriving the job ceiling from content,
  and restructuring character creation into a policy plus a factory.
- Renaming interfaces whose suffix already carries their category, such as repositories,
  registries, directories, content sources, decoders, encoders and senders.

## Constraints

- Behavior preservation is absolute: this Spec permits renaming and boundary separation only.
- The account-to-character binding is maintained across three collections that are currently updated
  together. Any separation must keep those updates mutually atomic; weakening that atomicity is a
  behavior change and is therefore out of scope rather than a permitted trade.
- The final review is performed by a reviewer that did not produce the change.

## Definitions

| Term | Meaning |
|---|---|
| Domain service | An interface whose operations read or write live world state, making call order and concurrency significant |
| Rule service | An interface whose result is a deterministic function of archive content and supplied facts, holding no state |
| Projection | An interface whose single operation converts a storage record into a domain value, with no dependencies and no I/O |
| Cross-group update | An operation that reads one online-state collection and writes another, so the binding invariant holds only while both are updated together |

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Each interface whose operations read or write world state is named as a domain service |
| RQ-002 | Each interface whose result is a deterministic function of content and supplied facts is named as a rule service |
| RQ-003 | Each interface whose single operation converts a storage record into a domain value is named as a projection |
| RQ-004 | The one interface belonging to none of the three categories keeps its name and its exclusion is recorded with the reason |
| RQ-005 | No consumer, registration or test refers to a former interface name |
| RQ-006 | An implementation type whose name derives from a renamed interface carries the matching name |
| RQ-007 | The account, online-user and active-character responsibilities are separate implementation types |
| RQ-008 | Cross-group updates of the online state remain mutually atomic |
| RQ-009 | Server behavior is unchanged: no emitted frame, persisted value, ordering or timing differs |
| RQ-010 | The taxonomy and its single exception are recorded in project documentation |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-002, RQ-003 | The set of abstraction service interfaces after the change | Each name is inspected | Every one ends in a category-bearing suffix matching how its operations behave |
| AC-002 | RQ-004 | The interface that exposes typed configuration lookup | Its name is inspected | It is unchanged, and project documentation states why it is outside the taxonomy |
| AC-003 | RQ-005 | The whole repository after the change | A search for any former interface name is performed | No occurrence remains outside historical design or protocol documents |
| AC-004 | RQ-006 | An implementation type that took its name from a renamed interface | Its declaration is inspected | Its name carries the same category as the interface it implements |
| AC-005 | RQ-007 | The online-state implementation after the change | Its declarations are inspected | Account, online-user and active-character responsibilities are carried by separate types |
| AC-006 | RQ-008 | A login that binds a character while a disconnect for the same account is in flight | Both run | The account never observes a binding whose session belongs to a connection that is already gone, and no partially updated online state is visible |
| AC-007 | RQ-009 | The test suite that passed at the accepted baseline | It is run against the changed code | Every test passes, and the difference in the test sources consists only of renamed identifiers |
| AC-008 | RQ-009 | The select-character and world-entry paths | They execute | The same frames are produced in the same order with the same bytes as at the baseline |
| AC-009 | RQ-010 | Project documentation after the change | It is read | It states the three categories, the rule that assigns an interface to one, and the recorded exception |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002, RQ-003, RQ-004 | AC-001, AC-002 | An inventory of the abstraction service interfaces with each name and its assigned category |
| EV-002 | RQ-005 | AC-003 | A repository-wide search result for the former names showing only documentary occurrences |
| EV-003 | RQ-006, RQ-007 | AC-004, AC-005 | The declarations of the renamed implementation types and the separated online-state types |
| EV-004 | RQ-008 | AC-006 | The mechanism that keeps cross-group updates mutually atomic, shown in the implementation, together with the passing online-user and select-character tests |
| EV-005 | RQ-009 | AC-007, AC-008 | A full test-suite run with no failures, and a review of the test-source difference confirming it contains only renamed identifiers |
| EV-006 | RQ-009 | AC-007, AC-008 | An independent review of the complete change reporting no behavior difference |
| EV-007 | RQ-010 | AC-009 | The documentation passage stating the categories, the assignment rule and the exception |

## Assumptions

- The interfaces classified as domain services are those whose implementations depend on a
  repository, registry or directory; as rule services, those depending only on content sources; as
  projections, those with no dependencies and a single record-to-value operation. This
  classification was established by inspection at the accepted baseline.
- Test sources name the interfaces they exercise, so their difference cannot be empty; AC-007 treats
  an identifier-only difference as evidence of preserved behavior.
