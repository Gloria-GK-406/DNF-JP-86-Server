# 富化工作包 14-mixed

**上游主模块**：`mixed: Game/Accounts/GrowthCapsuleSyncService.cs, Game/Accounts/HonorLevelSyncService.cs, Game/Premium/PremiumService.cs, Game/Quests/QuestNotificationProjector.cs, Network/Builders/AccountSettingsPacketBuilder.cs, Network/Builders/Auction/AuctionServiceNotificationBuilder.cs, Network/Builders/Init/OtherUserInfoResponseBuilder.cs, Network/Builders/Init/SelectCharacterPacketBuilder.cs, Network/Builders/KnightShieldDeckBodyBuilder.cs, Network/Builders/SecretShopClearPacketBuilder.cs, Network/Handlers/ChannelTownRestrictionSender.cs, Network/Handlers/CharacterSessionLifecycleCoordinator.cs, Network/Handlers/ChatHandler.cs, Network/Handlers/CollectionBoxHandler.cs, Network/Handlers/CraneMiniGameHandler.cs`

**输出分片**：`protocol/patterns/data/meanings/14-mixed.tsv`（本包独占，勿写其他文件）

本包 26 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## NOTI `0x0025` `EXP`

- 文档：`protocol/game/server-to-client/NOTI-0x0025.md`
- 上游文件：`Game/Accounts/GrowthCapsuleSyncService.cs`, `Network/Handlers/Dungeon/DungeonProgressNotificationProjector.cs`, `Network/Handlers/Dungeon/DungeonTutorialHandler.cs`, `Network/Handlers/ExperienceItemNotificationService.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `87 + n×8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |
| 5 | 4 | u32 | unknown |
| 9 | 4 | u32 | unknown |
| 13 | 2 | u16 | unknown |
| 15 | 2 | u16 | unknown |
| 17 | 2 | u16 | unknown |
| 19 | 2 | u16 | unknown |
| 21 | 4 | u32 | unknown |
| 25 | 4 | u32 | unknown |
| 29 | 1 | u8 | unknown |
| 30 | 4 | u32 | unknown |
| 34 | 4 | u32 | unknown |
| 38 | 4 | u32 | unknown |
| 42 | 4 | u32 | unknown |
| 46 | 4 | u32 | unknown |
| 50 | 1 | u8 | unknown |
| 51 | 4 | u32 | unknown |
| 55 | 4 | u32 | unknown |
| 59 | 4 | u32 | unknown |
| 63 | 4 | u32 | unknown |
| 67 | 4 | u32 | unknown |
| 71 | 4 | u32 | unknown |
| 75 | 4 | u32 | unknown |
| 79 | 4 | u32 | unknown |
| 83 | 4 | u32 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| (组内 4) | 4 | u32 | unknown |

## NOTI `0x0289` `HONOR_LEVEL_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x0289.md`
- 上游文件：`Game/Accounts/HonorLevelSyncService.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |

## CMD `0x0312` `PREMIUM_SERVICE`

- 文档：`protocol/game/client-to-server/0x0312.md`
- 上游文件：`Game/Premium/PremiumService.cs`, `Network/Handlers/LotteryItemResponseSender.cs`, `Network/Handlers/PremiumQueryHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `76`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 74 | bytes | unknown |

## NOTI `0x0042` `CERA_SPECIALITEM`

- 文档：`protocol/game/server-to-client/NOTI-0x0042.md`
- 上游文件：`Game/Premium/PremiumService.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## CMD `0x0021` `SET_QUEST_TRIGGER`

- 文档：`protocol/game/client-to-server/0x0021.md`
- 上游文件：`Game/Quests/QuestNotificationProjector.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `6`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 1 | u8 | unknown |
| 5 | 1 | u8 | unknown |

## NOTI `0x00AD` `GAME_OPTION`

- 文档：`protocol/game/server-to-client/NOTI-0x00AD.md`
- 上游文件：`Network/Builders/AccountSettingsPacketBuilder.cs`
- 提取状态：`structure-unknown`（polymorphic-payload），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## NOTI `0x01C7` `HOTKEY_OPTION`

- 文档：`protocol/game/server-to-client/NOTI-0x01C7.md`
- 上游文件：`Network/Builders/AccountSettingsPacketBuilder.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## NOTI `0x00B7` `AUCTION_NOTIFY_AUCTION_SERVICE`

- 文档：`protocol/game/server-to-client/NOTI-0x00B7.md`
- 上游文件：`Network/Builders/Auction/AuctionServiceNotificationBuilder.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |

## NOTI `0x0002` `USERINFO`

- 文档：`protocol/game/server-to-client/NOTI-0x0002.md`
- 上游文件：`Network/Builders/Init/OtherUserInfoResponseBuilder.cs`, `Network/Handlers/CeraShopHandler.cs`, `Network/Handlers/CharacterSelectHandler.cs`, `Network/Handlers/Dungeon/DungeonEntryHandler.cs`, `Network/Handlers/Dungeon/DungeonProgressNotificationProjector.cs`, `Network/Handlers/ExperienceItemNotificationService.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `203 + n×15`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 2 | u16 | unknown |
| 5 | 31 | dstringw | unknown |
| 36 | 1 | u8 | unknown |
| 37 | 1 | u8 | unknown |
| 38 | 1 | u8 | unknown |
| 39 | 1 | u8 | unknown |
| 40 | 1 | u8 | unknown |
| 41 | 1 | u8 | unknown |
| 42 | 1 | u8 | unknown |
| 43 | 4 | u32 | unknown |
| 47 | 1 | u8 | unknown |
| 48 | 1 | u8 | unknown |
| 49 | 1 | u8 | unknown |
| 50 | 1 | u8 | unknown |
| 51 | 8 | bytes | unknown |
| 59 | 1 | u8 | unknown |
| 60 | 4 | u32 | unknown |
| 64 | 1 | u8 | unknown |
| 65 | 4 | u32 | unknown |
| 69 | 31 | dstringw | unknown |
| 100 | 1 | u8 | unknown |
| 101 | 1 | u8 | unknown |
| 102 | 1 | u8 | unknown |
| 103 | 4 | u32 | unknown |
| 107 | 1 | u8 | unknown |
| 108 | 64 | dstringw | unknown |
| 172 | 4 | u32 | unknown |
| 176 | 1 | u8 | unknown |
| 177 | 1 | u8 | unknown |
| 178 | 1 | u8 | unknown |
| 179 | 1 | u8 | unknown |
| 180 | 4 | u32 | unknown |
| 184 | 1 | u8 | unknown |
| 185 | 4 | u32 | unknown |
| 189 | 2 | u16 | unknown |
| 191 | 1 | u8 | unknown |
| 192 | 1 | u8 | unknown |
| 193 | 2 | u16 | unknown |
| 195 | 4 | u32 | unknown |
| 199 | 4 | u32 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 1 | u8 | unknown |
| (组内 1) | 4 | u32 | unknown |
| (组内 5) | 1 | u8 | unknown |
| (组内 6) | 4 | u32 | unknown |
| (组内 10) | 4 | u32 | unknown |
| (组内 14) | 1 | u8 | unknown |

## NOTI `0x0166` `TITLE_BOOK_LIST`

- 文档：`protocol/game/server-to-client/NOTI-0x0166.md`
- 上游文件：`Network/Builders/Init/OtherUserInfoResponseBuilder.cs`, `Network/Handlers/InventoryHandler.Trade.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `11 + n×22`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 4 | u32 | unknown |
| 7 | 4 | u32 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 4 | u32 | unknown |
| (组内 6) | 4 | u32 | unknown |
| (组内 10) | 1 | u8 | unknown |
| (组内 11) | 2 | u16 | unknown |
| (组内 13) | 1 | u8 | unknown |
| (组内 14) | 4 | u32 | unknown |
| (组内 18) | 1 | u8 | unknown |
| (组内 19) | 1 | u8 | unknown |
| (组内 20) | 2 | u16 | unknown |

## NOTI `0x019F` `TAG_CHARACTER_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x019F.md`
- 上游文件：`Network/Builders/Init/SelectCharacterPacketBuilder.cs`, `Network/Handlers/Dungeon/DungeonEntryHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `67 + n×4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 31 | dstringw | unknown |
| 35 | 1 | u8 | unknown |
| 36 | 1 | u8 | unknown |
| 37 | 1 | u8 | unknown |
| 38 | 2 | u16 | unknown |
| 40 | 1 | u8 | unknown |
| 41 | 1 | u8 | unknown |
| 42 | 4 | u32 | unknown |
| 46 | 4 | u32 | unknown |
| 50 | 1 | u8 | unknown |
| 51 | 2 | u16 | unknown |
| 53 | 4 | u32 | unknown |
| 57 | 4 | u32 | unknown |
| 61 | 1 | u8 | unknown |
| 62 | 1 | u8 | unknown |
| 63 | 2 | u16 | unknown |
| 65 | 1 | u8 | unknown |
| 66 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 1 | u8 | unknown |
| (组内 1) | 2 | u16 | unknown |
| (组内 3) | 1 | u8 | unknown |

## NOTI `0x01C0` `COMBO_SKILL_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x01C0.md`
- 上游文件：`Network/Builders/Init/SelectCharacterPacketBuilder.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 2 | u16 | unknown |

## CMD `0x0292` `CHANGE_DECK_INFO`

- 文档：`protocol/game/client-to-server/0x0292.md`
- 上游文件：`Network/Builders/KnightShieldDeckBodyBuilder.cs`
- 提取状态：`closed-parametric`（counted-repeat-group），BodyLength = `0 + n×4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |

## NOTI `0x0245` `SEND_DECK_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x0245.md`
- 上游文件：`Network/Builders/KnightShieldDeckBodyBuilder.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `0 + n×4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |

## NOTI `0x0117` `SECRET_SHOP_NPC`

- 文档：`protocol/game/server-to-client/NOTI-0x0117.md`
- 上游文件：`Network/Builders/SecretShopClearPacketBuilder.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |

## NOTI `0x0118` `SECRET_SHOP_ITEM_LIST`

- 文档：`protocol/game/server-to-client/NOTI-0x0118.md`
- 上游文件：`Network/Builders/SecretShopClearPacketBuilder.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4 + n×22`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| (组内 4) | 1 | u8 | unknown |
| (组内 5) | 4 | u32 | unknown |
| (组内 9) | 4 | u32 | unknown |
| (组内 13) | 4 | u32 | unknown |
| (组内 17) | 4 | u32 | unknown |
| (组内 21) | 1 | u8 | unknown |

## NOTI `0x0017` `USER_AREA`

- 文档：`protocol/game/server-to-client/NOTI-0x0017.md`
- 上游文件：`Network/Handlers/ChannelTownRestrictionSender.cs`, `Network/Handlers/Dungeon/DungeonCombatHandler.cs`, `Network/Handlers/Dungeon/DungeonEntryHandler.cs`, `Network/Handlers/Dungeon/DungeonTownReturnCoordinator.cs`, `Network/Handlers/Dungeon/DungeonTutorialHandler.cs`, `Network/Handlers/PartyHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `10`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 2 | u16 | unknown |
| 8 | 1 | u8 | unknown |
| 9 | 1 | u8 | unknown |

## NOTI `0x01F7` `SERVER_NOTICE_MESSAGE`

- 文档：`protocol/game/server-to-client/NOTI-0x01F7.md`
- 上游文件：`Network/Handlers/ChannelTownRestrictionSender.cs`, `Network/Handlers/CharacterSessionLifecycleCoordinator.cs`, `Network/Handlers/Dungeon/DungeonEntryHandler.cs`, `Network/Handlers/InventoryHandler.Package.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x0004` `SELECT_CHARACTER`

- 文档：`protocol/game/client-to-server/0x0004.md`
- 上游文件：`Network/Handlers/CharacterSessionLifecycleCoordinator.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## NOTI `0x000C` `MESSAGE`

- 文档：`protocol/game/server-to-client/NOTI-0x000C.md`
- 上游文件：`Network/Handlers/ChatHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `260`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 256 | dstringw | unknown |

## NOTI `0x016A` `CREATE_GROUP`

- 文档：`protocol/game/server-to-client/NOTI-0x016A.md`
- 上游文件：`Network/Handlers/ChatHandler.cs`, `SelfTests/ChatBroadcastSelfTest.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `6 + n×128`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |
| 5 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 128 | dstringw | unknown |

## NOTI `0x016F` `MESSAGE_GROUP_CHAT`

- 文档：`protocol/game/server-to-client/NOTI-0x016F.md`
- 上游文件：`Network/Handlers/ChatHandler.cs`, `SelfTests/ChatBroadcastSelfTest.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1156`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 128 | dstringw | unknown |
| 132 | 1024 | dstringw | unknown |

## CMD `0x0012` `DELETE_ITEM`

- 文档：`protocol/game/client-to-server/0x0012.md`
- 上游文件：`Network/Handlers/CollectionBoxHandler.cs`, `Network/Handlers/Dungeon/DeathTowerCoordinator.cs`, `Network/Handlers/InventoryHandler.Trade.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `18`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 4 | u32 | unknown |
| 10 | 4 | u32 | unknown |
| 14 | 4 | u32 | unknown |

## NOTI `0x0381` `COLLECT_BOX`

- 文档：`protocol/game/server-to-client/NOTI-0x0381.md`
- 上游文件：`Network/Handlers/CollectionBoxHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `12`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 1 | u8 | unknown |
| 8 | 4 | u32 | unknown |

## CMD `0x01E6` `CRANE_START_USE`

- 文档：`protocol/game/client-to-server/0x01E6.md`
- 上游文件：`Network/Handlers/CraneMiniGameHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## CMD `0x01E7` `CRANE_PICKUP`

- 文档：`protocol/game/client-to-server/0x01E7.md`
- 上游文件：`Network/Handlers/CraneMiniGameHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `6`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |

