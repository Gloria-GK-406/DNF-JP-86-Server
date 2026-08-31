---
kind: rks-verification
decision: pass
---

# Completion Evidence: Rule Services Own Every Rule Input

## The claim being evaluated

That the TaskContract's goal is met: rule inputs reach this server only through rule services, so a
domain service expresses business flow and a rule service answers what the rules say; that the
layering is recorded where a later reader will find it; and that none of this changed what the
server does.

Evaluated against the working tree of `E:/Project/DNF/DNF-JP-86-Server/.worktrees/rule-services-own-content`,
branch `rule-services-own-content`, baseline `8af7990c`. Nothing is committed; the change is the
working tree. All evidence below was produced fresh at this stage, not carried forward.

## Condition to evidence

### SC-1 — Rule inputs enter the server only through rule services

**Content sources.** Every `I*DefinitionSource` / `I*ContentSource` / `I*Catalog` / `I*Source`
declared in `Dfo.Server.Abstractions` was enumerated, and each one's consumers across
`Dfo.Server.Application`, `Dfo.Server.Protocol` and `Dfo.Server.Host` were listed. 29 sources, each
consumed by exactly one rule service.

The sweep raised one apparent exception and it is not one: `IItemUseRuleDefinitionSource` is
injected in `Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:110` — into
`ItemUseJudgementService`, a rule service that happens to share a file with `PlayingCharacter`. Read
at the line, not inferred from the file name.

**Game-rule configuration values.** Four: `DropRateOptions` → `DropRuleService`;
`ChannelEntryOptions` → `ChannelEntryPolicy` and `EntryPlacementRuleService`, both rule services;
`ExperienceTuning` and `ExperienceEntitlementPolicy` → `ExperienceRuleService`.

The sweep raised a second apparent exception, also not one: `ExperienceEntitlementPolicy` appears in
`Server/src/Dfo.Server.Application/World/ExperienceBonusRules.cs` at line 27 as the type's own
declaration, line 30 as its `None` instance, and line 64 as a **method parameter** of a static pure
rule — the rules being handed the configuration, which is the direction this task establishes.

**Relocated constants.** Recorded with baseline and current values in
`tasks/TASK-11-completeness-evidence.md`; every value unchanged. Two constants were renamed while
moving (`EquipmentItemKind` → `EquipmentKind`, `DefaultSlotLimit` → `AccountSlotLimit`) and carry
their original justification.

**The named exception.** `diff` of every `IServerConfigService` line at `8af7990c` against the
current tree returns empty: identical. Its record in `AGENTS.md` as a deliberate exception is
unrevised.

**Other exceptions, all accounted for.** Two configuration types remain outside a rule service and
both are classified in the artifacts with reasons: `ExperienceDiagnostics` as a measurement switch,
`SelectCharacterDeliveryOptions` as protocol delivery behaviour. `GameProtocol.SecretShopNpcId` is
classified as frame content. `DungeonRunState.LastHostileActorType` is disclosed as a residual
game-semantic constant on a domain record whose only production path runs through
`DungeonRoomRuleService`.

**Met.**

### SC-2 — A static rule class is reached only through the rule service that owns its subject

Call sites of all five static rule classes, across the whole of `Server/src`:

| Static rule class | Callers |
|---|---|
| `DropRules` | `DropRuleService` |
| `MonsterExperienceRules` | `ExperienceRuleService` |
| `DungeonClearExperienceRules` | `ExperienceRuleService` |
| `ExperienceBonusRules` | `ExperienceRuleService` |
| `CharacterNameRules` | `CharacterCreationRuleService` |

`DungeonSelectionRules` no longer exists: it was a static class shared across two slices and is now
`DungeonSelectionRuleService`. The only other grep hit anywhere is a comment in `ApplicationModule`.

**Met.**

### SC-3 — The Protocol layer reaches rules only through domain services

Every rule-service, policy and content-source declaration under `Server/src/Dfo.Server.Protocol`:

| File | Declaration |
|---|---|
| `Game/OnlineUserConnectionObserver.cs:21` | `IChannelEntryPolicy channels` |
| `Game/SelectCharacter/SelectCharacterHandler.cs:54` | `IChannelEntryPolicy channels` |
| `Game/TownProjection/TownProjectionSlice.cs:211` | `IChannelEntryPolicy channels` |

Three, all the same thing: endpoint resolution, which `AGENTS.md` names as one of the four
capabilities an ordinary handler may exercise and which the TaskContract's Out scope excludes by
name.

Static rule class calls from the Protocol layer: **none**.

**Met.**

### SC-4 — The server still does exactly what it did

Fresh run, this stage, archive reachable:

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

| Project | Passed | Skipped | Failed |
|---|---|---|---|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 51 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 376 | 0 | 0 |
| Dfo.Server.Application.Tests | 771 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 244 | 0 | 0 |
| Dfo.Server.Host.Tests | 4 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 184 | 2 | 0 |
| **Total** | **1690** | **2** | **0** |

Exit code 0. Baseline at `8af7990c` was 1690 / 2 / 0 — identical, not merely "no worse".

The two skips were named in the output and are
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` and
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`.
They skip *because* the archive is present, so the skip count is itself the evidence that this run
was made with it. A run without the archive would show a different count and would prove nothing.

`git diff main -- Server/tests`:

| Measure | Count |
|---|---|
| `[Fact]` / `[Theory]` / `[SkippableFact]` / `[InlineData]` added | **0** |
| the same, removed | **0** |
| `Assert.` lines added / removed | 1 / 1 |

The single changed assertion is
`Assert.Equal(first, DungeonEntryPayloadCodec.FirstActorSequence(AtStart))` becoming
`Assert.Equal(first, Rooms.FirstActorSequenceFor(AtStart))` — the symbol called changed, the
expected value `first` did not.

**Met.**

### SC-5 — Rule services still receive facts rather than querying the world

Every `*RuleService`, `*Policy` and `ItemUseJudgementService` constructor in
`Server/src/Dfo.Server.Application` was scanned for a dependency whose name ends in `Repository`,
`Registry`, `Directory`, `Store`, `Persistence`, `Table` or `DomainService`. **None.**

Eighteen rule services. Every declared dependency is a content source, a game-rule configuration
value, or another rule service. `Docs/领域结构重构设计.md` §6 is now checkable by reading
constructors, which is what the success condition asked for.

**Met.**

### SC-6 — A later reader finds the layering recorded

`AGENTS.md` now carries, at the point where domain services are introduced:

- line 98, **「三层是上下关系，不是三选一。」** with the four layers drawn and the rule that each
  calls only the one below it;
- line 111, **「规则输入只经规则服务进入本服务器。」** naming the three kinds of rule input;
- line 118, **「宿主部署配置不是规则输入。」** pointing at the existing exception note rather than
  restating it.

Three rows of the interface-suffix table were restated so the reading that permitted the drift is
gone: `*RuleService` is now "内容源与规则配置的唯一读取点", `*Rules` is "只由规则服务调用，是规则
服务的下一层而不是它的替代", and the content-source row is "只被规则服务注入".

The flow diagram's domain arrow, which read
`领域服务 → 仓储与存储 / 内容源` — the drift drawn as the design — now routes content through a
rule service.

The stale honest-deviation entry claiming the three channel commands still read infrastructure
content sources was corrected.

**Met.**

## What this verification does not prove

- **Two dungeon frames and the breakable-object drop counts have no live confirmation.** Both the
  TaskContract and the Spec declare this. Behaviour preservation here is established against the
  suite and the archive, not against a running client, so "value for value" is bounded by what the
  suite covers.
- **`MoveMapHandler` has no direct test.** The second independent reviewer stated this plainly: the
  green suite proves almost nothing about the `MoveAsync` signature change made during remediation,
  whose correctness rests on source-level equivalence plus the immutability of `DungeonRunState`.
- **One intermittent test.** `QuestLogTests.ProgressRecordedAgainstAValueTheQuestNoLongerHoldsFails`
  failed once during TASK-05's suite run and once during TASK-09's, and passed in isolation and on
  every re-run since, including this one. It is SQLite-backed, matching the load-dependent
  read-then-write contention `AGENTS.md` documents. The exception text was never captured, so it is
  undiagnosed rather than dismissed.

None of the three prevents any success condition from being evidenced; each bounds what the evidence
means, and is recorded so a later reader is not misled by a clean result.
