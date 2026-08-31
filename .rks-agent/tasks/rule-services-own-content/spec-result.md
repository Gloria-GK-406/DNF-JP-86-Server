---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Rule Services Own Every Rule Input

## Delivered

- Rule inputs reach this server only through rule services. 29 archive content sources, 4 game-rule
  configuration values and the relocated rule constants each have one owning rule service; at the
  baseline 42 of those injection points sat outside one.
- Twelve rule services now own those inputs, joining the six that already held their rules without
  reading content. Nine domain services carry the use cases that reach them.
- All five static pure-rule classes are called from a rule service and nowhere else. At the baseline
  none of the six was, and one was called from a protocol handler.
- The Protocol layer declares no rule input and no rule service except endpoint resolution through
  `IChannelEntryPolicy`, which the repository conventions sanction.
- No rule service takes a repository, registry, directory or store, so
  `Docs/领域结构重构设计.md` §6 can be checked by reading constructors.
- `AGENTS.md` states the layering as a stack — business service, rule service, pure rule function —
  where it previously listed `*RuleService` and `*Rules` as parallel alternatives.
- Four cross-slice references the slice rules forbid were resolved on the way: `DungeonSelectionRules`
  became a rule service; two payload-codec derivations and one NPC constant moved to the `Game`
  endpoint layer.
- `QuestOfferRefresher`, a business service living in the Protocol layer and reached directly by five
  handlers, moved to `Application` as `IQuestOfferRefreshDomainService`.
- The server behaves as it did: 1690 passed / 2 skipped / 0 failed, identical to the baseline, with
  zero test cases added or removed.

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `verification/completion-evidence.md` § SC-1 — per-source enumeration, the two apparent exceptions read at the line and resolved, `IServerConfigService` `diff`-identical to `8af7990c` |
| SC-2 | `verification/completion-evidence.md` § SC-2 — call-site table for all five static rule classes |
| SC-3 | `verification/completion-evidence.md` § SC-3 — three remaining Protocol declarations, all `IChannelEntryPolicy`; zero static rule class calls |
| SC-4 | `verification/completion-evidence.md` § SC-4 — fresh 1690/2/0 with the archive reachable and the two skips named; `Server/tests` diff adds and removes zero test cases, one assertion changed only in the symbol it calls |
| SC-5 | `verification/completion-evidence.md` § SC-5 — all 18 rule-service constructors scanned; none takes a world-state dependency |
| SC-6 | `verification/completion-evidence.md` § SC-6 — `AGENTS.md` lines 98, 111, 118, three restated taxonomy rows, and the corrected flow diagram |

## Repository Result

- Changed paths: 86 tracked files under `Server/` and `AGENTS.md`, plus 22 new source files and the
  task's own `.rks-agent/tasks/rule-services-own-content/` artifacts.
- Baseline: `8af7990c` on branch `rule-services-own-content`, which is also the tip of `main`.
- Parent rollback required: no.

## Successor Guidance

Four things this work found and deliberately did not change. Each is argued in
`tasks/TASK-11-completeness-evidence.md`; a successor should read the argument before acting.

- **One archive fact, three rule-service homes.** `EquipmentKind = 1` lives in
  `ItemPlacementRuleService`, `CharacterCreationRuleService` and `ItemArrivalRuleService`. Making the
  first the single owner was tried and withdrawn: it would require the item-definition archive source
  to be registered wherever a character is created, which broke eighteen tests. Unifying them is a
  design change with a coupling cost, not a relocation.
- **Three readings of the monster-or-ally actor-type boundary** — `<= MonsterRank.Boss`,
  `> LastHostileActorType = 3`, `>= FirstAiCharacterActorType = 5`. Actor type 4 is therefore an ally
  for clearing, not a monster for experience, and a monster for packet numbering. That is present
  behaviour; unifying it changes what the server does.
- **`DungeonRunState.LastHostileActorType`** is a game-semantic constant still on a domain record
  rather than in a rule service. Its only production path runs through `DungeonRoomRuleService`.
- **`DungeonRunState.FindGroundDrop` has no production caller.** It was not removed because the test
  using it asserts the renumbering semantics, and changing that test's subject is what this Spec's
  behaviour-preservation reading forbids.

Two more, from the independent reviews:

- **`IDungeonVisitDomainService.MoveAsync(CharacterId, DungeonRunState, …)` permits a caller to pass
  one character's identifier with another's run.** The single caller is correct; the signature is not
  self-enforcing. Closing it means `DungeonRunState` carrying its own `CharacterId`, or an assertion.
- **`MoveMapHandler` has no direct test.** The suite proves almost nothing about that path.

Three seams are one-line pass-throughs — `ExperienceValuationDomainService`,
`QuestDefinitionDomainService`, `ChannelDirectoryDomainService`, and two more in the same shape. They
are the mechanical consequence of RQ-005 for command handlers whose use case had no owning service,
and the user chose that cost with the table in front of them.

Also unfinished by design: the deferred card-flip reward system, and `HANDOFF.md`, which the user
intends to delete after this work.

## Artifacts

- `.rks-agent/tasks/rule-services-own-content/TaskContract.md` (revision 2)
- `.rks-agent/tasks/rule-services-own-content/spec.md`, `plan.md`, `routing-decision.md`
- `.rks-agent/tasks/rule-services-own-content/tasks/TASK-01..TASK-11` — sealed contracts, amendments,
  execution results, executor self-checks, inline quick reviews
- `.rks-agent/tasks/rule-services-own-content/reviews/change-review.md` — `approved`, two independent
  rounds
- `.rks-agent/tasks/rule-services-own-content/verification/completion-evidence.md` — `pass`
