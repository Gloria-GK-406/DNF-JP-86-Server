# Character Entry State G3 Coverage

This implementation map is code-reversal evidence, not a claim that every legacy name is protocol truth. Sources were inspected with `git show`/`git grep` from `main:Server/DfoServer`, principally `Sqlite/item_schema.sql`, `Game/SelectCharacter/SqliteSelectCharacterDataSource.cs`, `Game/SelectCharacter/Snapshots/*`, `Game/CharacterData/*` and `Network/Builders/Init/*`.

Declared G3 IDs (51): `1, 7, 8, 9, 10, 11, 12, 13, 14, 16, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 32, 33, 37, 39, 40, 41, 44, 45, 46, 47, 48, 49, 50, 51, 54, 57, 58, X-01, X-02, X-03, X-06, X-12, X-13, X-14, X-17, L-02, L-04, L-06, L-07, L-08, L-09`.

## Persistence boundary

`CharacterEntryAssembly` (Application/Characters) reads the normalized legacy tables through one repository per table, all of them sharing the running use case's single transaction, and validates ownership itself. It does not persist or deserialize a precomposed domain snapshot. The three mandatory one-to-one rows (`character_subtype0_fields`, `character_subtype1_fields`, `character_init_flags`) must be written by character creation; the entry reader neither backfills nor fabricates them. Collection absence remains an explicit loaded-empty state. Malformed or missing required rows fail before active-character publication.

Published compatibility bytes are source-specific and bounded: versioned `characters.appearance_blob` is limited to 16 KiB, the subtype-0 creature buffer is absent or exactly 8 bytes, Dark Knight page bodies are limited to 64 KiB, and creature text is limited to 4 KiB. Title item cores are accepted only at exactly 82 bytes and decoded into typed fields before publication. None is a cross-domain snapshot.

| Matrix row | Typed character-owned outcome | Normalized source / derivation | Excluded overlay |
|---|---|---|---|
| 1 character portion | existing identity/fatigue plus typed active quest state | `characters`, `character_active_quests` | account premium/wallet and ACK encoding |
| 7 | exact creature key/mode/progress/text/tail records | `character_creatures` | pet definitions |
| 8, 9 character portion, 54 | typed subtype-0/subtype-1 scalars, bounded versioned appearance, visibility and clone-title | `characters`, `character_subtype0_fields`, `character_subtype1_fields` | honor/adventure/account fields |
| 10 | nullable five-slot Knight Shield deck | `character_knight_shield_deck`; job 12 applicability | shield catalog |
| 11 | ordered skill page/index and entry slot/id/level/extras | `character_skills`; protocol header/tail mirrors require static skill-cost composition and are deliberately absent from the G3 character-owned model | static skill definitions and SP/TP protocol projection |
| 12 character portion, 13 | active quest slot/id/trigger, notify IDs and 30,000-byte clear flags | `character_active_quests`, `character_quest_notify_selections`, `character_invisible_falgs` | quest definitions |
| 14 character portion | level, grouped daily challenge IDs, both entry values, six claim flags and tail IDs | `characters.level`, `character_daily_challenge_groups/entries/claims/tail_ids` | activity definitions |
| 16 character portion, 19, X-02 | ordered character hotkeys, bounded option bitmap and visibility | `character_hotkey_slots`, `character_init_flags`, subtype-0 visibility | account input mode |
| 18 character portion | ordered dungeon ID/clear state | `character_dungeon_permissions`; difficulty is not character-owned normalized state and is absent from the G3 model | account/channel admission and difficulty derivation |
| 20 | growth-weapon stage sequence | `character_growth_weapon_stages` | none |
| 21-26 | five explicit title categories; each entry preserves slot, item/value, attribute, durability, seal, enchant and amplify fields | `character_new_titlebook.item_core` decoded from the validated 82-byte record | title definitions |
| 27, X-06 character portion | learned skill rows, non-null applicable-empty G3 point-slot collection and character bonus-SP/TP basis | `character_skills`, `characters.bonus_sp/bonus_tp`; `SkillPointLedger` plus static skill costs produces a separate later mixed-domain projection | account capsule/honor and static cost tables |
| 32-33 | boxes 3/4 with typed slot/item assignments | `character_collectbox_slots` | collection definitions |
| 37 | typed profession, experience, give-up count, disjoint/enchanter machine values and recipe IDs | subtype-0, `character_expert_job`, `character_expert_job_recipes` | profession definitions |
| 39 | daily revive-coin claim | `character_daily_counters['revive_coin_daily_claim']` | reset mutation |
| 40 | cube type/grade | `character_crystal_contract`; absent row has the legacy 0/255 domain default | cube definitions |
| 41 character portion | lottery premium-use count | `character_daily_counters['lottery_double_reward_used']` | account entitlement |
| 44 | typed signed-i32 champion key plus mode/value | `character_init_flags` | business name remains provisional |
| 45 | gold limit upgrade level derived losslessly from synchronized stored limits | `character_stats.gold_carry_limit` / `auction_gold_limit` (the folded-away `character_gold_limits`) and legacy `GoldLimitDataProvider.ResolveUpgradeLevel` thresholds | carried-gold limits, which cap a gold this server does not keep yet |
| 46 | fatigue acceleration value/state | no surviving normalized writer exists; legacy snapshot also leaves its documented zero defaults | none |
| 47 | oldest active folder-0 inbox page selected oldest-first then emitted newest-first, plus distinct not-loaded count; saved/deleted/expired rows excluded | `mailbox_messages`, required `mailbox_recipients` state | mailbox mutation and attachment inventory |
| 48 | ordered mission ID/progress | `character_pvp_missions` | PvP definitions |
| 49-51 | three exact four-value schedule tuples | `character_daily_schedule_states`; absent rows use the builder's explicit three zero-tuple default | schedule definitions |
| 57 | existing typed position | `characters` via accepted G1 character service | channel spawn policy |
| 58 character portion | creature runtime is present in `Features.Pets`; equipped item identity comes from accepted G2 inventory | creature table plus G2 | pet script body |
| X-01 | nullable ordered bounded Dark Knight page bodies | `character_dark_knight_combo_skill_pages`; job 9 applicability | none |
| X-03 character portion | appearance and clone-title fields | `characters` | command encoding |
| X-12, L-08 | active quest source state | quest tables above | synchronization mutation/sending (G7) |
| X-13-X-14, L-09 pet portion | exact creature runtime source state | creature table above plus G2 equipped pet | revival/session mutation (G7) |
| X-17 | typed expert persistence state | expert tables above | open-store area/session runtime (G7) |
| L-02, L-09 dungeon portion | dungeon permission/progress source state | dungeon table above | persistent-effect recovery (G7); rejoin mutation removed 2026-08-18 |
| L-04 character portion | G1 position plus typed appearance | characters/profile sources above | channel policy (G5/G7) |
| L-06 | learned skill state and optional combo | skill tables above | channel/PvP preparation (G5/G7) |
| L-07 | no distinct G3 non-inventory state | G1 session and accepted G2 equipment locks already own its inputs | PvP/session projection (G7) |

All 51 declared G3 IDs are accounted for. `null` is used only for non-applicable job features; applicable empty tables become non-null empty collections. Wrong ownership, missing mandatory one-to-one rows, oversized bounded values, invalid identifiers/ranges, orphan claims, malformed fixed-size item cores, incompatible class rows and incomplete schedule tuples are explicit errors.
