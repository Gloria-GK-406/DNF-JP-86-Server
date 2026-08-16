# 富化工作包 16-mixed

**上游主模块**：`mixed: Network/Handlers/Dungeon/DungeonSettlementHandler.cs, Network/Handlers/Dungeon/DungeonTutorialHandler.cs, Network/Handlers/Dungeon/SpecialDungeonNotificationSender.cs, Network/Handlers/Dungeon/TimeSpiralDungeonCoordinator.cs, Network/Handlers/Dungeon/TournamentDungeonCoordinator.cs, Network/Handlers/DungeonHandler.cs, Network/Handlers/EnchanterHandler.cs, Network/Handlers/ExpertJobCompoundHandler.cs, Network/Handlers/ExpertJobExtractionHandler.cs, Network/Handlers/ExpertJobGiveupHandler.cs, Network/Handlers/ExpertJobGiveupNotificationProjector.cs, Network/Handlers/GoldLimitHandler.cs, Network/Handlers/GrowthCapsuleHandler.cs, Network/Handlers/InventoryHandler.AvatarDisjoint.cs, Network/Handlers/InventoryHandler.AvatarOptionChange.cs`

**输出分片**：`protocol/patterns/data/meanings/16-mixed.tsv`（本包独占，勿写其他文件）

本包 26 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## NOTI `0x0022` `PLAY_RESULT`

- 文档：`protocol/game/server-to-client/NOTI-0x0022.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonSettlementHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `16`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |
| 5 | 1 | u8 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 1 | u8 | unknown |
| 8 | 1 | u8 | unknown |
| 9 | 2 | u16 | unknown |
| 11 | 4 | u32 | unknown |
| 15 | 1 | u8 | unknown |

## NOTI `0x0023` `CLEAR_DUNGEON_REWARD`

- 文档：`protocol/game/server-to-client/NOTI-0x0023.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonSettlementHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `167`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 4 | u32 | unknown |
| 16 | 1 | u8 | unknown |
| 17 | 4 | u32 | unknown |
| 21 | 4 | u32 | unknown |
| 25 | 4 | u32 | unknown |
| 29 | 4 | u32 | unknown |
| 33 | 4 | u32 | unknown |
| 37 | 4 | u32 | unknown |
| 41 | 4 | u32 | unknown |
| 45 | 4 | u32 | unknown |
| 49 | 4 | u32 | unknown |
| 53 | 4 | u32 | unknown |
| 57 | 4 | u32 | unknown |
| 61 | 4 | u32 | unknown |
| 65 | 4 | u32 | unknown |
| 69 | 4 | u32 | unknown |
| 73 | 4 | u32 | unknown |
| 77 | 4 | u32 | unknown |
| 81 | 4 | u32 | unknown |
| 85 | 4 | u32 | unknown |
| 89 | 4 | u32 | unknown |
| 93 | 4 | u32 | unknown |
| 97 | 4 | u32 | unknown |
| 101 | 4 | u32 | unknown |
| 105 | 4 | u32 | unknown |
| 109 | 4 | u32 | unknown |
| 113 | 4 | u32 | unknown |
| 117 | 1 | u8 | unknown |
| 118 | 4 | u32 | unknown |
| 122 | 4 | u32 | unknown |
| 126 | 1 | u8 | unknown |
| 127 | 4 | u32 | unknown |
| 131 | 4 | u32 | unknown |
| 135 | 4 | u32 | unknown |
| 139 | 4 | u32 | unknown |
| 143 | 4 | u32 | unknown |
| 147 | 4 | u32 | unknown |
| 151 | 4 | u32 | unknown |
| 155 | 4 | u32 | unknown |
| 159 | 4 | u32 | unknown |
| 163 | 4 | u32 | unknown |

## NOTI `0x015C` `TOD_CLEAR_REWARD`

- 文档：`protocol/game/server-to-client/NOTI-0x015C.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonSettlementHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `15`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 4 | u32 | unknown |
| 11 | 4 | u32 | unknown |

## NOTI `0x0282` `LINKED_DUNGEON_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x0282.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonSettlementHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |

## CMD `0x008F` `CHANGE_TUTORIAL_FLAG`

- 文档：`protocol/game/client-to-server/0x008F.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonTutorialHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 1 | u8 | unknown |

## CMD `0x01E4` `TUTORIAL_LEVEL_UP`

- 文档：`protocol/game/client-to-server/0x01E4.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonTutorialHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## NOTI `0x00AA` `DUNGEON_EVENT_STORY_PAUSE`

- 文档：`protocol/game/server-to-client/NOTI-0x00AA.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonTutorialHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 1 | u8 | unknown |

## CMD `0x0211` `SUMMON_MONSTER`

- 文档：`protocol/game/client-to-server/0x0211.md`
- 上游文件：`Network/Handlers/Dungeon/SpecialDungeonNotificationSender.cs`, `Network/Parsers/Dungeon/DungeonCommandParser.cs`, `Network/Protocol/GameProtocolHandler.cs`, `SelfTests/SpecialDungeonSelfTest.cs`
- 提取状态：`closed`（—），BodyLength = `19`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 4 | u32 | unknown |
| 10 | 4 | u32 | unknown |
| 14 | 2 | u16 | unknown |
| 16 | 2 | u16 | unknown |
| 18 | 1 | u8 | unknown |

## CMD `0x026B` `TIMER_MODIFY_INFO`

- 文档：`protocol/game/client-to-server/0x026B.md`
- 上游文件：`Network/Handlers/Dungeon/SpecialDungeonNotificationSender.cs`, `Network/Parsers/Dungeon/DungeonCommandParser.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |

## NOTI `0x022D` `GAUGE_OBJECT_BAR_DATA`

- 文档：`protocol/game/server-to-client/NOTI-0x022D.md`
- 上游文件：`Network/Handlers/Dungeon/SpecialDungeonNotificationSender.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |

## NOTI `0x022F` `MINIMAP_ICON_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x022F.md`
- 上游文件：`Network/Handlers/Dungeon/SpecialDungeonNotificationSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 4 | u32 | unknown |

## CMD `0x03AB` `BREAK_TRAP_RESULT`

- 文档：`protocol/game/client-to-server/0x03AB.md`
- 上游文件：`Network/Handlers/Dungeon/TimeSpiralDungeonCoordinator.cs`, `Network/Parsers/Dungeon/DungeonCommandParser.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x01CA` `TOURNAMENT_REWARD_SELECT_STATE`

- 文档：`protocol/game/client-to-server/0x01CA.md`
- 上游文件：`Network/Handlers/Dungeon/TournamentDungeonCoordinator.cs`, `Network/Parsers/Dungeon/DungeonCommandParser.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x01CB` `TOURNAMENT_REWARD_SELECT`

- 文档：`protocol/game/client-to-server/0x01CB.md`
- 上游文件：`Network/Handlers/Dungeon/TournamentDungeonCoordinator.cs`, `Network/Parsers/Dungeon/DungeonCommandParser.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |

## NOTI `0x0185` `TOURNAMENT_CLEAR_REWARD`

- 文档：`protocol/game/server-to-client/NOTI-0x0185.md`
- 上游文件：`Network/Handlers/Dungeon/TournamentDungeonCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8 + n1×14 + n2×10 + n3×10`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |
| 5 | 1 | u8 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 4 | u32 | unknown |
| (组内 6) | 4 | u32 | unknown |
| (组内 10) | 2 | u16 | unknown |
| (组内 12) | 1 | u8 | unknown |
| (组内 13) | 1 | u8 | unknown |
| *重复组2 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| (组内 4) | 4 | u32 | unknown |
| (组内 8) | 2 | u16 | unknown |
| *重复组3 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| (组内 4) | 4 | u32 | unknown |
| (组内 8) | 2 | u16 | unknown |

## CMD `0x01DE` `INFORM_NOTICE`

- 文档：`protocol/game/client-to-server/0x01DE.md`
- 上游文件：`Network/Handlers/DungeonHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |

## CMD `0x0264` `REPAIR_EXPERT_JOB_STORE`

- 文档：`protocol/game/client-to-server/0x0264.md`
- 上游文件：`Network/Handlers/EnchanterHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x00EE` `COMPOUND_ITEM_BY_EXPERT_JOB`

- 文档：`protocol/game/client-to-server/0x00EE.md`
- 上游文件：`Network/Handlers/ExpertJobCompoundHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 2 | u16 | unknown |

## NOTI `0x00CD` `EXPERT_JOB_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x00CD.md`
- 上游文件：`Network/Handlers/ExpertJobCompoundHandler.cs`, `Network/Handlers/ExpertJobExtractionHandler.cs`, `Network/Handlers/ExpertJobGiveupNotificationProjector.cs`, `Network/Handlers/ExpertJobStoreHandler.cs`, `Network/Handlers/InventoryHandler.Package.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |

## CMD `0x01A0` `EXPERT_EXTRACTION`

- 文档：`protocol/game/client-to-server/0x01A0.md`
- 上游文件：`Network/Handlers/ExpertJobExtractionHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `6`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 2 | u16 | unknown |

## CMD `0x00EF` `GIVEUP_EXPERT_JOB`

- 文档：`protocol/game/client-to-server/0x00EF.md`
- 上游文件：`Network/Handlers/ExpertJobGiveupHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## NOTI `0x0013` `SKILLINFO`

- 文档：`protocol/game/server-to-client/NOTI-0x0013.md`
- 上游文件：`Network/Handlers/ExpertJobGiveupNotificationProjector.cs`, `Network/Handlers/SkillHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `13`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 1 | u8 | unknown |
| 8 | 1 | u8 | unknown |
| 9 | 2 | u16 | unknown |
| 11 | 2 | u16 | unknown |

## NOTI `0x0331` `UPGRADE_CARRY_GOLD`

- 文档：`protocol/game/server-to-client/NOTI-0x0331.md`
- 上游文件：`Network/Handlers/GoldLimitHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x025B` `GET_EXPAND_EXP_GAGE_REWARD`

- 文档：`protocol/game/client-to-server/0x025B.md`
- 上游文件：`Network/Handlers/GrowthCapsuleHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x00CA` `DISJOINT_AVATAR`

- 文档：`protocol/game/client-to-server/0x00CA.md`
- 上游文件：`Network/Handlers/InventoryHandler.AvatarDisjoint.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `6`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |

## CMD `0x01CC` `AVATAR_OPTION_CHANGE`

- 文档：`protocol/game/client-to-server/0x01CC.md`
- 上游文件：`Network/Handlers/InventoryHandler.AvatarOptionChange.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `13`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 2 | u16 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 1 | u8 | unknown |

