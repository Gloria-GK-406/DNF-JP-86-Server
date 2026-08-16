# 富化工作包 11-protocol-gameprotocolhandler

**上游主模块**：`Network/Protocol/GameProtocolHandler.cs`

**输出分片**：`protocol/patterns/data/meanings/11-protocol-gameprotocolhandler.tsv`（本包独占，勿写其他文件）

本包 26 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## CMD `0x007A` `CREATURE_SCRIPT_MESSAGE`

- 文档：`protocol/game/client-to-server/0x007A.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 4 | u32 | unknown |
| 7 | ? | lenprefixed? | unknown |

## CMD `0x0086` `CHANGE_LETTER_STAT`

- 文档：`protocol/game/client-to-server/0x0086.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `6`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 2 | u16 | unknown |

## CMD `0x009F` `DEATH_TOWER_STAGE_CMD`

- 文档：`protocol/game/client-to-server/0x009F.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x00A6` `CALL_PARTY_MEMBER_REALTIME_INFO`

- 文档：`protocol/game/client-to-server/0x00A6.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x00BF` `DUNGEON_EVENT_STORY_PAUSE`

- 文档：`protocol/game/client-to-server/0x00BF.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |

## CMD `0x00C6` `SAVE_GAME_OPTION_2`

- 文档：`protocol/game/client-to-server/0x00C6.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x00C9` `USE_EMBLEM`

- 文档：`protocol/game/client-to-server/0x00C9.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed-parametric`（counted-repeat-group），BodyLength = `8 + n×7`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 4 | u32 | unknown |
| 7 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 4 | u32 | unknown |
| (组内 6) | 1 | u8 | unknown |

## CMD `0x00FE` `CHANGE_EMOTION`

- 文档：`protocol/game/client-to-server/0x00FE.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## CMD `0x0104` `CHANGE_ANOTHER_SKILL_TREE`

- 文档：`protocol/game/client-to-server/0x0104.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x0129` `SECRET_SHOP_OPEN_CLOSE`

- 文档：`protocol/game/client-to-server/0x0129.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x0133` `DEPOSIT_MONEY`

- 文档：`protocol/game/client-to-server/0x0133.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |

## CMD `0x0134` `WITHDRAW_MONEY`

- 文档：`protocol/game/client-to-server/0x0134.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |

## CMD `0x013B` `MULTI_MAILBOX_SEND`

- 文档：`protocol/game/client-to-server/0x013B.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x013C` `OPERATE_RIDABLE_OBJECT`

- 文档：`protocol/game/client-to-server/0x013C.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `7`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |
| 5 | 1 | u8 | unknown |
| 6 | 1 | u8 | unknown |

## CMD `0x0144` `QUERY_CHARAC_INFO_MAILBOX`

- 文档：`protocol/game/client-to-server/0x0144.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | ? | lenprefixed? | unknown |

## CMD `0x0152` `USE_LIMIT_CUBE`

- 文档：`protocol/game/client-to-server/0x0152.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 2 | u16 | unknown |

## CMD `0x0170` `SAVE_GAME_OPTION_QUICKCHATTING`

- 文档：`protocol/game/client-to-server/0x0170.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x0197` `REGENERATION_RANDOM_OPTION`

- 文档：`protocol/game/client-to-server/0x0197.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 2 | u16 | unknown |

## CMD `0x019C` `TITLE_BOOK_PUT`

- 文档：`protocol/game/client-to-server/0x019C.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x019D` `TITLE_BOOK_GET`

- 文档：`protocol/game/client-to-server/0x019D.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x019E` `MONSTERCARD_BIND`

- 文档：`protocol/game/client-to-server/0x019E.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed-parametric`（counted-repeat-group），BodyLength = `2 + n×2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |

## CMD `0x01A1` `ACHIEVEMENT_TRIGGER`

- 文档：`protocol/game/client-to-server/0x01A1.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `10`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 2 | u16 | unknown |
| 8 | 2 | u16 | unknown |

## CMD `0x01A3` `CREATE_GROUP`

- 文档：`protocol/game/client-to-server/0x01A3.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（variable-width-primitive），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | ? | lenprefixed? | unknown |

## CMD `0x01A8` `OTHER_USER_TITLE_BOOK_LIST`

- 文档：`protocol/game/client-to-server/0x01A8.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## CMD `0x01B3` `PARTY_TELEPORT`

- 文档：`protocol/game/client-to-server/0x01B3.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `7`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 1 | u8 | unknown |

## CMD `0x01B7` `UPGRADE_ITEM_SEPARATE`

- 文档：`protocol/game/client-to-server/0x01B7.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 4 | u32 | unknown |
| 7 | 2 | u16 | unknown |
| 9 | ? | lenprefixed? | unknown |
| ? | 1 | u8 | unknown |

