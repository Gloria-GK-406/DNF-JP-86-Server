# SELECT_CHARACTER G4 Account Entry Coverage

This document is a code-derived migration map, not a captured-wire specification. The legacy evidence comes from `SqliteSelectCharacterDataSource`, its packet builders, and `item_schema.sql` on `main`; later packet capture may revise names or ownership.

`AccountEntryState` is loaded once by `AccountId` before active-character publication. It contains four cohesive account slices. The already accepted `AccountInventoryEntryState` remains a separate field of the same `SelectCharacterResult`; account cargo and virtual cube resources are reused through that exact object reference and are not duplicated into account state.

| Matrix identity | Legacy normalized source | Account result field/service | Notes |
|---|---|---|---|
| row 1 account portions | `account_premiums`, `accounts.cera` | `Entitlements.Premiums`, `Wallet.Cera`; wallet/entitlement service | Normalized `premium_type` remains signed 32-bit and absolute `end_time` remains signed 64-bit. Only Networking's later row-1 ACK projection may derive a byte-sized presentation type. |
| row 6 account cargo | accepted inventory repository | `SelectCharacterResult.AccountInventory` | Reference reuse; intentionally absent from `AccountEntryState`. |
| row 9 account portions | `accounts.honor_exp`, account roster levels | `Progression.HonorExperience`, `Progression.Adventure.CharacterLevels`; progression/event service | Static honor/adventure thresholds remain outside G4; progression carries no character identity. |
| row 15 | `account_settings.main_game_option`, `quickchat_bank0/1` | `Settings.MainGameOptions`, `QuickChatBank0/1`; settings service | A missing/null main option uses the exact legacy `AccountSettings.DefaultMainGameOption`; loaded-null quick-chat banks are explicit empty byte arrays. |
| row 16 account portion | `account_settings.hotkey_key_type`, `hotkey_slots` | `Settings.HotkeyKeyType`, nullable singular `AccountHotkeySlot`; settings service | Legacy `ExtractAccountScopedHotkeySlots` owns only the first 16-bit prefix. Empty remains absent, a one-byte prefix is malformed, and an oversized historical blob is truncated to its first pair so character slots are never duplicated. |
| row 30 | `character_buy_restrict_items` joined through all active account roster rows | `Relations.Unknown730Entries`; relation/compatibility service | Meaning remains deliberately neutral. Three signed 32-bit values are promoted to account state only when every active roster character has the same persisted ordered sequence; otherwise loading fails. No commerce policy is inferred. |
| row 35 account portion | `accounts.lucky_star` | `Wallet.LuckyStar`; wallet/entitlement service | Rental equipment and time are not account state. |
| row 38 | `character_mercenary_support` with both endpoints ownership-checked | `Relations.CharacterRelations`; relation/compatibility service | The only account view that intentionally names roster `CharacterId` values. |
| row 41 account portion | `account_premiums` | `Entitlements.Premiums`; wallet/entitlement service | Signed 32-bit types preserve Devil Contract slots 580-587; character usage and static premium layout remain separate inputs for later composition. |
| row 42 | `account_increase_chance_lottery_progress` | `Progression.LotteryProgress`; progression/event service | Item and reward order is normalized and duplicates/ranges are rejected. |
| row 43 | `accounts.lucky_star` | `Wallet.LuckyStar`; wallet/entitlement service | Same loaded wallet value as row 35. |
| row 52 provisional portion | legacy `character_init_flags.pc_room_state` across the active account roster | `Relations.PcRoomPlayTimeState`; relation/compatibility service | Ownership remains unresolved. A value is exposed only when every active roster entry has the same persisted byte; missing/inconsistent data fails instead of inventing zero/null/unavailable. |
| row 55 | `accounts.cera`, `token_cera`, `happy_token_cera` | `Wallet.Cera`, `TokenCera`, `HappyTokenCera`; wallet/entitlement service | Wire-width signed 32-bit range is enforced. |
| X-04 account portion | `characters` by account, ordered slot | `Relations.RosterReferences` plus `Progression.Adventure.CharacterLevels` | Character identity appears only in the explicit account-wide roster-relation view; progression carries only ordered levels. |
| X-05 | `accounts.honor_exp` | `Progression.HonorExperience` | No fabricated honor level; static tables derive display fields later. |
| X-06 account portion | `accounts.growth_capsule_exp` | `Progression.GrowthCapsuleExperience` | Selected-character max-level condition and skill-point fields remain outside account state. |

Transactional repository reads validate the mandatory account row, malformed settings, wallet and progression widths, roster slot uniqueness, relation endpoint ownership, lottery uniqueness/ranges, and the row-52 consensus before returning any slice. The only load-time settings default is the demonstrated legacy main-option baseline above.
