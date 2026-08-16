# 富化工作包 15-mixed

**上游主模块**：`mixed: Network/Handlers/Dungeon/AntonNormalConquestNotificationSender.cs, Network/Handlers/Dungeon/CardRewardNotificationSender.cs, Network/Handlers/Dungeon/DungeonBuffNotificationSender.cs, Network/Handlers/Dungeon/DungeonEntryHandler.cs, Network/Handlers/Dungeon/DungeonKillApplicationService.cs, Network/Handlers/Dungeon/DungeonMapHandler.cs, Network/Handlers/Dungeon/DungeonMechanismNotificationSender.cs, Network/Handlers/Dungeon/DungeonNpcItemDropCoordinator.cs, Network/Handlers/Dungeon/DungeonProgressNotificationProjector.cs, Network/Handlers/Dungeon/DungeonRejoinCoordinator.cs, Network/Handlers/Dungeon/DungeonSettlementHandler.cs`

**输出分片**：`protocol/patterns/data/meanings/15-mixed.tsv`（本包独占，勿写其他文件）

本包 26 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## NOTI `0x0005` `DUNGEON_PERMISSION`

- 文档：`protocol/game/server-to-client/NOTI-0x0005.md`
- 上游文件：`Network/Handlers/Dungeon/AntonNormalConquestNotificationSender.cs`, `Network/Handlers/Dungeon/DungeonSettlementHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2 + n×3`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 1 | u8 | unknown |

## NOTI `0x025B` `SEQUENTIAL_DUNGEON_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x025B.md`
- 上游文件：`Network/Handlers/Dungeon/AntonNormalConquestNotificationSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 1 | u8 | unknown |
| 5 | 4 | u32 | unknown |

## CMD `0x0045` `SCORE_SCROLL_STATE`

- 文档：`protocol/game/client-to-server/0x0045.md`
- 上游文件：`Network/Handlers/Dungeon/CardRewardNotificationSender.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x0046` `CARD_SELECT_RIGHT_STATE`

- 文档：`protocol/game/client-to-server/0x0046.md`
- 上游文件：`Network/Handlers/Dungeon/CardRewardNotificationSender.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x0047` `SELECT_CARD`

- 文档：`protocol/game/client-to-server/0x0047.md`
- 上游文件：`Network/Handlers/Dungeon/CardRewardNotificationSender.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |

## CMD `0x0048` `EPLP_COMMAND`

- 文档：`protocol/game/client-to-server/0x0048.md`
- 上游文件：`Network/Handlers/Dungeon/CardRewardNotificationSender.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |

## NOTI `0x01E8` `CHARACTER_ADD_BUFF`

- 文档：`protocol/game/server-to-client/NOTI-0x01E8.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonBuffNotificationSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `17`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |
| 5 | 4 | u32 | unknown |
| 9 | 4 | u32 | unknown |
| 13 | 4 | u32 | unknown |

## NOTI `0x01E9` `CHARACTER_DEL_BUFF`

- 文档：`protocol/game/server-to-client/NOTI-0x01E9.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonBuffNotificationSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1 + n×4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |

## NOTI `0x01EA` `CHARACTER_BUFF_DUNGEON`

- 文档：`protocol/game/server-to-client/NOTI-0x01EA.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonBuffNotificationSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2 + n×4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |

## NOTI `0x001A` `UDP_HOST`

- 文档：`protocol/game/server-to-client/NOTI-0x001A.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonEntryHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## NOTI `0x001B` `ENTER_SELECT_DUNGEON`

- 文档：`protocol/game/server-to-client/NOTI-0x001B.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonEntryHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |

## NOTI `0x001C` `DUNGEON_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x001C.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonEntryHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `6`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 1 | u8 | unknown |
| 5 | 1 | u8 | unknown |

## NOTI `0x0026` `DIE_MONSTER`

- 文档：`protocol/game/server-to-client/NOTI-0x0026.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonKillApplicationService.cs`, `Network/Handlers/Dungeon/SpecialDungeonSettlementCoordinator.cs`
- 提取状态：`structure-unknown`（polymorphic-payload），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## NOTI `0x001D` `START_MAP`

- 文档：`protocol/game/server-to-client/NOTI-0x001D.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonMapHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `14`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 4 | u32 | unknown |
| 7 | 1 | u8 | unknown |
| 8 | 1 | u8 | unknown |
| 9 | 4 | u32 | unknown |
| 13 | 1 | u8 | unknown |

## NOTI `0x015B` `USER_APC_INFO_TOD`

- 文档：`protocol/game/server-to-client/NOTI-0x015B.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonMapHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `518`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 255 | dstringw | unknown |
| 256 | 1 | u8 | unknown |
| 257 | 1 | u8 | unknown |
| 258 | 1 | u8 | unknown |
| 259 | 255 | dstringw | unknown |
| 514 | 4 | u32 | unknown |

## NOTI `0x0183` `TOURNAMENT_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x0183.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonMapHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `16 + n×6`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 1 | u8 | unknown |
| 5 | 1 | u8 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 1 | u8 | unknown |
| 8 | 2 | u16 | unknown |
| 10 | 4 | u32 | unknown |
| 14 | 1 | u8 | unknown |
| 15 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| (组内 4) | 2 | u16 | unknown |

## NOTI `0x0184` `TOURNAMENT_MAP_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x0184.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonMapHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `27 + n×18`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 1 | u8 | unknown |
| 8 | 2 | u16 | unknown |
| 10 | 1 | u8 | unknown |
| 11 | 2 | u16 | unknown |
| 13 | 4 | u32 | unknown |
| 17 | 2 | u16 | unknown |
| 19 | 2 | u16 | unknown |
| 21 | 1 | u8 | unknown |
| 22 | 1 | u8 | unknown |
| 23 | 1 | u8 | unknown |
| 24 | 1 | u8 | unknown |
| 25 | 1 | u8 | unknown |
| 26 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 1 | u8 | unknown |
| (组内 1) | 2 | u16 | unknown |
| (组内 3) | 4 | u32 | unknown |
| (组内 7) | 4 | u32 | unknown |
| (组内 11) | 2 | u16 | unknown |
| (组内 13) | 1 | u8 | unknown |
| (组内 14) | 2 | u16 | unknown |
| (组内 16) | 2 | u16 | unknown |

## NOTI `0x02A6` `HELL_PARTY_MONSTER_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x02A6.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonMapHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4 + n×8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| (组内 4) | 4 | u32 | unknown |

## NOTI `0x0138` `COMPLETE_CONDITION_PASS_GATE`

- 文档：`protocol/game/server-to-client/NOTI-0x0138.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonMechanismNotificationSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 1 | u8 | unknown |

## CMD `0x0253` `EVENT_NPC_DROP_ITEM_`

- 文档：`protocol/game/client-to-server/0x0253.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonNpcItemDropCoordinator.cs`, `Network/Parsers/Dungeon/DungeonCommandParser.cs`, `Network/Protocol/GameProtocolHandler.cs`, `SelfTests/DungeonNpcItemDropSelfTest.cs`, `SelfTests/SpecialDungeonSelfTest.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## NOTI `0x0015` `ACCEPTABLE_QUEST_LIST`

- 文档：`protocol/game/server-to-client/NOTI-0x0015.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonProgressNotificationProjector.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `3 + n×2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |

## NOTI `0x02A7` `DISCONN_DUNGEON_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x02A7.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonRejoinCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 1 | u8 | unknown |

## NOTI `0x02A8` `REJOIN_DUNGEON`

- 文档：`protocol/game/server-to-client/NOTI-0x02A8.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonRejoinCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## NOTI `0x02A9` `CANCEL_REJOIN_DUNGEON`

- 文档：`protocol/game/server-to-client/NOTI-0x02A9.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonRejoinCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## NOTI `0x02AA` `REJOINABLE_DUNGEON`

- 文档：`protocol/game/server-to-client/NOTI-0x02AA.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonRejoinCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `259`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 255 | dstringw | unknown |

## NOTI `0x001F` `ENABLE_CLEAR_DUNGEON`

- 文档：`protocol/game/server-to-client/NOTI-0x001F.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonSettlementHandler.cs`
- 提取状态：`structure-unknown`（polymorphic-payload），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

