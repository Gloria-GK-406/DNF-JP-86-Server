---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 2
state: ready
---

# Specification: Rule Services Own Every Rule Input

## Why and Outcome

`Docs/领域结构重构设计.md` §2 declares that a rule service's inputs are content, facts and an
explicit seed, while an aggregate's input is a command. §6 enforces one half of that line: a rule
service may not query world state. The other half was never enforced, so business services and
protocol handlers read archive content and game-rule configuration directly, and the rules they
then apply sit wherever the reading happened.

The outcome is that the declared line holds in both directions. After this change a rule input
enters the server only through a rule service; a business service expresses the use case and asks a
rule service what the rules say; a protocol handler drives a business service and reads no rules of
its own. The server behaves exactly as it does today, value for value.

## Task Perspective

- Core outcome: rule inputs reach the server only through rule services, so that a reader can tell
  from where a value is consumed whether it is business flow or a rule, and a later change to a
  rule has one place to happen.
- Necessary support: behaviour is preserved value for value, because a structural move that alters
  what the rules produce would defeat the reason for making the structure legible; and the existing
  §6 constraint continues to hold, because relieving one direction of the line by breaking the other
  leaves the line no more real than before.
- Peripheral assurance: the inventories that demonstrate no rule input is consumed outside a rule
  service, and the suite run that demonstrates preserved behaviour. These prove the result; they are
  not the result. The conventions record is the one apparent assurance that is genuinely part of the
  outcome, because the current record is what allowed the drift.

## Scope and Non-Goals

### In Scope

- Every archive content source, every game-rule configuration value, and every constant expressing
  archive or game semantics that is currently consumed outside a rule service.
- The rule answers that protocol and command handlers currently obtain without going through the
  business service that owns the use case.
- Every pure rule function currently invoked from outside a rule service.
- The repository's own record of the layering, to the extent that a reader applying it today would
  reproduce the drift rather than the intended structure.

### Non-Goals

- Host deployment configuration. `IServerConfigService` and its consumers are outside the rule layer
  and remain exactly as they are, including the note recording that exclusion.
- The handler capabilities the repository conventions already sanction, including endpoint
  resolution.
- Any change to what the server does: frames, persisted data, ordering, timing, and every value the
  rules produce stay as they are.
- Establishing archive provenance for constants that declare none today. A constant keeps the value
  and the justification it already carries.
- Rules that do not exist yet, including the deferred card-flip reward.
- The six existing rule services that hold their rules without reading content. They are already on
  the correct side of the line and are not required to acquire inputs they do not need.

## Constraints

- Behaviour is preserved value for value. This Spec relocates where rule inputs are read and where
  rules are evaluated; it decides nothing anew.
- `Docs/领域结构重构设计.md` §6 must still hold at completion. No rule service may acquire the
  ability to read or write world state in order to receive a relocated rule.
- The archive must be reachable when behaviour is demonstrated. Archive-dependent checks skip
  silently rather than failing when it is not, so a run made without it does not count as evidence.

## Definitions

| Term | Meaning |
|---|---|
| Rule input | A value the game's rules depend on that originates outside the running world: archive content, game-rule configuration, or a constant expressing archive or game semantics. Host deployment configuration is not a rule input. |
| Rule service | A service that answers what the rules say from rule inputs plus facts handed to it. It holds no world state and reads none. |
| Business service | A domain service that carries out a use case and owns the world-state transitions the use case makes. |
| Pure rule function | A rule expressed as a stateless function that performs no I/O. |
| Sanctioned handler capability | A capability the repository conventions already permit a protocol handler to exercise directly, including connection identity, read-only queries and endpoint resolution, deterministic protocol capability without domain state, and command dispatch with response sending. |
| Rule-input inventory | An enumeration of every place a rule input is consumed, naming the consuming type and the input consumed. |

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Every archive content source is consumed only by a rule service. |
| RQ-002 | Every game-rule configuration value is consumed only by a rule service, and its declaration sits with the rules it configures rather than with a protocol concern. |
| RQ-003 | Every pure rule function is invoked only by a rule service. |
| RQ-004 | Every constant expressing archive or game semantics resides with the rule service that owns its subject. |
| RQ-005 | A protocol or command handler obtains a rule answer only through the business service that owns the use case, except where it exercises a sanctioned handler capability. |
| RQ-006 | No rule service reads or writes world state. |
| RQ-007 | Host deployment configuration is consumed as it is today, by the same consumers, and its recorded status as an exclusion is unchanged. |
| RQ-008 | Every value the server produces is the value it produced at the baseline, for every input that produced it there. |
| RQ-009 | The repository's record of the layering describes business service, rule service and pure rule function as successive layers rather than as alternatives, so that a reader applying the record produces the structure this Spec establishes. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001 | The completed solution | A rule-input inventory is taken over archive content sources | Every consuming type it names is a rule service, and no consumer is unaccounted for |
| AC-002 | RQ-002 | The completed solution | A rule-input inventory is taken over game-rule configuration values | Every consuming type it names is a rule service, and each value is declared alongside the rules it configures |
| AC-003 | RQ-003 | The completed solution | The call sites of every pure rule function are enumerated | Every caller is a rule service |
| AC-004 | RQ-004 | A constant that expressed archive or game semantics outside a rule service at the baseline | Its present home is examined | It resides with the rule service owning its subject, carrying the same value and the same stated justification |
| AC-005 | RQ-005 | The completed solution | Every protocol and command handler's declared dependencies are enumerated | None consumes a rule input or a rule service, apart from sanctioned handler capabilities, which are individually identifiable as such |
| AC-006 | RQ-006 | The completed solution | Every rule service's declared dependencies are examined | None is a repository, registry, directory or store, nor any other reader or writer of world state |
| AC-007 | RQ-007 | The completed solution | Host deployment configuration consumers are compared against the baseline | They are unchanged, and the record naming the exclusion is unchanged |
| AC-008 | RQ-008 | The completed solution with the archive reachable | The full behavioural suite is run | It passes at no fewer assertions than the baseline, with no failure |
| AC-009 | RQ-008 | The completed solution | The behavioural suite's own diff against the baseline is examined | No assertion, expected value or test case differs; only the identifiers and wiring the relocation requires |
| AC-010 | RQ-009 | A reader who knows only the repository's recorded conventions | They are asked where a newly needed archive value should be read | The record leads them to a rule service, and offers no reading in which a business service or handler may read it directly |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002 | AC-001, AC-002 | A complete rule-input inventory over content sources and game-rule configuration, each entry resolved to a rule service, with the count of unresolved entries stated as zero |
| EV-002 | RQ-003, RQ-004 | AC-003, AC-004 | A call-site enumeration for every pure rule function, and a per-constant record of its home and preserved value and justification |
| EV-003 | RQ-005 | AC-005 | A dependency enumeration over protocol and command handlers, with each remaining rule-adjacent dependency identified as a named sanctioned capability |
| EV-004 | RQ-006 | AC-006 | A dependency enumeration over rule services showing no world-state reader or writer |
| EV-005 | RQ-007 | AC-007 | A baseline comparison of host deployment configuration consumers and of the record naming the exclusion |
| EV-006 | RQ-008 | AC-008, AC-009 | A full-suite result obtained with the archive reachable, reported with pass, skip and fail counts against the baseline, together with the behavioural-suite diff |
| EV-007 | RQ-009 | AC-010 | The revised conventions record, read back against the structure the completed solution presents |

## Assumptions

- The baseline inventory measured at `8af7990c` — roughly 49 content-source injection points across
  31 files over 24 source interfaces, of which 7 are already inside the four rule services; 4
  game-rule configuration values, 2 already inside rule services; 6 pure rule functions, none reached
  through a rule service; and 4 protocol-layer calls that bypass the business service — is expected
  to hold. The counts are context, not acceptance criteria; the inventories in Success Evidence are.
- Behavioural checks name the types being relocated, so their diffs cannot be empty. AC-009 treats an
  identifier-and-wiring-only diff as evidence of preserved behaviour, which is the reading the
  preceding taxonomy task used for the same situation.
- The existing suite is the practical behaviour oracle available here. Two dungeon frames and the
  breakable-object drop counts have no live confirmation, so preservation is established against the
  suite and the archive rather than against a running client.
- Which rule service owns a rule input shared by several consumers is an open implementation choice
  at this stage. Every allocation that satisfies the requirements above is acceptable to this Spec;
  fixing the allocation before consumers are touched is a planning concern.
