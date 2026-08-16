# 富化工作包 19-mixed

**上游主模块**：`mixed: Network/Handlers/RaidHandler.Situation.cs, Network/Handlers/RaidHandler.cs, Network/Handlers/RentalHandler.cs, Network/Handlers/SecretShopHandler.cs, Network/Handlers/ShopCoinEventHandler.cs, Network/Handlers/SkillHandler.cs, Network/Handlers/StaminaHandler.cs, Network/Handlers/TownHandler.cs, Network/Parsers/Dungeon/DungeonCommandParser.cs, SelfTests/ChronicleRefineSelfTest.cs`

**输出分片**：`protocol/patterns/data/meanings/19-mixed.tsv`（本包独占，勿写其他文件）

本包 21 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## NOTI `0x027B` `RAID_MONSTER_HP`

- 文档：`protocol/game/server-to-client/NOTI-0x027B.md`
- 上游文件：`Network/Handlers/RaidHandler.Situation.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `11 + n×4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 4 | u32 | unknown |
| 10 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |

## NOTI `0x0247` `RAID_DO_BEHAVIOR`

- 文档：`protocol/game/server-to-client/NOTI-0x0247.md`
- 上游文件：`Network/Handlers/RaidHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |

## CMD `0x0372` `RENT_EQUIPMENT_ITEM`

- 文档：`protocol/game/client-to-server/0x0372.md`
- 上游文件：`Network/Handlers/RentalHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `21`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 21 | bytes | unknown |

## CMD `0x0128` `SECRET_SHOP_BUY_ITEM`

- 文档：`protocol/game/client-to-server/0x0128.md`
- 上游文件：`Network/Handlers/SecretShopHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `20`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 4 | u32 | unknown |
| 16 | 4 | u32 | unknown |

## CMD `0x00CF` `SHOP_COIN_EVENT`

- 文档：`protocol/game/client-to-server/0x00CF.md`
- 上游文件：`Network/Handlers/ShopCoinEventHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## NOTI `0x00B1` `SHOP_COIN_EVENT`

- 文档：`protocol/game/server-to-client/NOTI-0x00B1.md`
- 上游文件：`Network/Handlers/ShopCoinEventHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x001C` `CHANGE_SKILLSLOT`

- 文档：`protocol/game/client-to-server/0x001C.md`
- 上游文件：`Network/Handlers/SkillHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `3`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 1 | u8 | unknown |

## CMD `0x001D` `BUY_SKILL`

- 文档：`protocol/game/client-to-server/0x001D.md`
- 上游文件：`Network/Handlers/SkillHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `3 + n×4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 1 | u8 | unknown |
| (组内 3) | 1 | u8 | unknown |

## CMD `0x014B` `SKILL_COMMAND_CUSTOMIZING`

- 文档：`protocol/game/client-to-server/0x014B.md`
- 上游文件：`Network/Handlers/SkillHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed-parametric`（counted-repeat-group），BodyLength = `1 + n×5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 1 | u8 | unknown |
| (组内 3) | 1 | u8 | unknown |
| (组内 4) | 1 | u8 | unknown |

## CMD `0x014C` `SKILL_COMMAND_ALL_DEFAULT`

- 文档：`protocol/game/client-to-server/0x014C.md`
- 上游文件：`Network/Handlers/SkillHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x0009` `RECOVER_STAMINA`

- 文档：`protocol/game/client-to-server/0x0009.md`
- 上游文件：`Network/Handlers/StaminaHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## NOTI `0x0021` `FAIL_CLEAR_DUNGEON`

- 文档：`protocol/game/server-to-client/NOTI-0x0021.md`
- 上游文件：`Network/Handlers/StaminaHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x0025` `FINISH_LOADING`

- 文档：`protocol/game/client-to-server/0x0025.md`
- 上游文件：`Network/Handlers/TownHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |

## CMD `0x00ED` `TELEPORT`

- 文档：`protocol/game/client-to-server/0x00ED.md`
- 上游文件：`Network/Handlers/TownHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 1 | u8 | unknown |

## NOTI `0x0006` `USER_LEAVE`

- 文档：`protocol/game/server-to-client/NOTI-0x0006.md`
- 上游文件：`Network/Handlers/TownHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## NOTI `0x0016` `USER_POSITION`

- 文档：`protocol/game/server-to-client/NOTI-0x0016.md`
- 上游文件：`Network/Handlers/TownHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 2 | u16 | unknown |

## CMD `0x00FF` `DIE_BLOOD_MONSTER`

- 文档：`protocol/game/client-to-server/0x00FF.md`
- 上游文件：`Network/Parsers/Dungeon/DungeonCommandParser.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed-parametric`（counted-repeat-group），BodyLength = `1 + n×2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |

## CMD `0x0102` `BLOOD_ROUND_UI_PREPARE_FINISH_`

- 文档：`protocol/game/client-to-server/0x0102.md`
- 上游文件：`Network/Parsers/Dungeon/DungeonCommandParser.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x013D` `SELECT_ULTIMATE_DIFFICULTY`

- 文档：`protocol/game/client-to-server/0x013D.md`
- 上游文件：`Network/Parsers/Dungeon/DungeonCommandParser.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x026D` `SEA_CHASE_MINI_GAME_RESULT`

- 文档：`protocol/game/client-to-server/0x026D.md`
- 上游文件：`Network/Parsers/Dungeon/DungeonCommandParser.cs`, `Network/Protocol/GameProtocolHandler.cs`, `SelfTests/SpecialDungeonSelfTest.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |

## CMD `0x0172` `ENCHANT_3RD_CHRONICLE_ITEM`

- 文档：`protocol/game/client-to-server/0x0172.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`, `SelfTests/ChronicleRefineSelfTest.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `14`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 2 | u16 | unknown |
| 9 | 4 | u32 | unknown |
| 13 | 1 | u8 | unknown |

