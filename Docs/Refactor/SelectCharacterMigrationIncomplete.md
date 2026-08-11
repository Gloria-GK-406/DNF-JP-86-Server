# Select Character Migration — Incomplete and Paused

## Status

- State: **incomplete / paused**
- Recorded: 2026-08-11
- Task branch (historical): `codex/select-character-domain-migration`
- Local integration target: `codex/architecture-refactor`
- Accepted baseline: `b8e817c482795e805fb001bd5d0627d77848b987`
- Immediate priority: resolve the authoritative `Script.pvf` input and prove parser compatibility before continuing the select-character migration.

This document records an intentionally incomplete migration. It must not be read as evidence that the complete `SELECT_CHARACTER` initialization or lifecycle has been implemented.

## Accepted Work

The following checkpoints are locally accepted on the task branch:

| Checkpoint | Commit | Result |
|---|---|---|
| Legacy initialization matrix | `fd39009` | Maps 58 static entries, conditional/trailing packets, lifecycle actions, ownership and confidence. |
| Active-character publication | `3e70094` | Resolves exact `AccountId + SlotIndex` and publishes user/character state consistently. |
| Inventory entry state | `fa51169` | Separates character and account inventory data behind domain-facing abstractions. |
| G3 character entry state | `927e6ed` | Loads typed character-owned entry state from normalized persistence sources. |
| G4 account entry state | `b8e817c` | Loads typed account-owned entry state without leaking SQLite into Application or Networking. |

Fresh verification of the accepted baseline passed all 118 solution tests: 53 Application, 36 Infrastructure and 29 Networking tests.

## Unfinished Work

### G5 — World, Content and Session-Adjacent State

G5 is blocked. It still requires:

- authoritative quest definitions used to combine character quest progress;
- daily activity definitions;
- collection-box definitions;
- pet/creature definitions and optional welcome-script content;
- channel/world admission policy composition;
- town presence, PvP lobby, dungeon rejoin and expert-store/session-adjacent read views;
- honest handling of fixed or unresolved protocol baselines.

An attempted G5 implementation proved that checked-in configuration cannot represent complete production content. Its product changes were not accepted and were rolled back to `b8e817c`. Only the blocked attempt evidence remains under `.rks-agent`.

### G6 — Networking Projection

The complete initialization projection is not implemented. Networking still needs to:

- project the ordered 58-entry initialization sequence from completed business results;
- preserve duplicate occurrences and conditional packet placement;
- project mixed character/account/world packets without querying services or repositories;
- emit conditional and trailing packets only when their typed business result is available;
- avoid placeholder packets for unavailable required data.

Although some G1–G4 packet projection could be split out independently, no such successor is being started while this task is paused.

### G7 — Integrated Entry Lifecycle

The complete lifecycle workflow is not implemented. Remaining behavior includes inventory lease preparation, previous-character cleanup, displaced-session cleanup, position and appearance preparation, daily refill mutation, post-entry lock refresh, quest synchronization, pet runtime transitions, dungeon rejoin, town/PvP publication and failure rollback.

### Root Completion

The root task cannot yet satisfy complete result composition, complete Networking projection, integrated Host startup, final review or end-to-end verification.

## PVF Blocker

The legacy server reads required static content from `Script.pvf` through the existing `Tool/PvfLib` project. Legacy mainline contains only `main:Server/DfoServer/Data/Pvf/README.md` at the expected data location; neither the refactor branch nor its working resources contain `Script.pvf` or an equivalent complete exported catalog.

The missing input affects at least these archive paths:

- `n_quest/quest.lst` and referenced quest files;
- `etc/dailychallengetable.etc`;
- `localization/collectbox/collectboxinfo.etc`;
- `Creature/Creature.lst`, referenced creature files and applicable pet scripts.

The existing parser is useful evidence, but compatibility with the actual 86 JP archive has not been demonstrated without the real file. Empty JSON, generated defaults or tests populated with invented catalogs are not acceptable substitutes.

## Required PVF Resolution

Before resuming G5, provide one of the following:

1. the authoritative `Script.pvf`; or
2. a reproducible, validated export containing every required quest, activity, collection and pet definition with source identity.

The input should then pass all of these checks:

- `PvfArchive.Open` validates and opens the archive;
- the archive identity is recorded with size and SHA-256;
- every required root path exists;
- referenced mandatory entries parse without silent skipping;
- IDs, paths, ordering and duplicate rules are validated;
- collection boxes required by the migration are present;
- the production Host can resolve the configured external path;
- missing or malformed required content fails explicitly before active-character publication.

The recommended architecture is to keep `PvfLib` inside Infrastructure, map parsed values into pure Data DOs, and expose only domain-facing source/service abstractions to Application. No `PvfLib` type should enter Data, Abstractions, Application or Networking contracts.

## Separate Runtime-Source Gap

Resolving PVF supplies static content only. Town presence, PvP lobby membership, dungeon-rejoin candidates and expert-store/session registries are runtime state and still require typed online/session services. They must not be fabricated from PVF or represented as loaded-empty when the source is unavailable.

## Resume Point

Resume from accepted baseline `b8e817c` after the PVF resolution checks succeed. The next accepted checkpoint should complete G5 with real static content and real session-adjacent sources. Only then should the complete G6 packet stream and G7 lifecycle be integrated and verified.

This paused result is intended for local integration into `codex/architecture-refactor`, after which the temporary task worktree and task branch may be removed. No push or pull request is part of this paused state.
