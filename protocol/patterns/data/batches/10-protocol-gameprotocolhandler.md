# 富化工作包 10-protocol-gameprotocolhandler

**上游主模块**：`Network/Protocol/GameProtocolHandler.cs`

**输出分片**：`protocol/patterns/data/meanings/10-protocol-gameprotocolhandler.tsv`（本包独占，勿写其他文件）

本包 26 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## CMD `0x0002` `SET_UDP_IP_PORT`

- 文档：`protocol/game/client-to-server/0x0002.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x0003` `EXIT`

- 文档：`protocol/game/client-to-server/0x0003.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x0008` `GET_USERINFO`

- 文档：`protocol/game/client-to-server/0x0008.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `3`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |

## CMD `0x000A` `REQUEST_PEER`

- 文档：`protocol/game/client-to-server/0x000A.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `7`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 4 | u32 | unknown |

## CMD `0x000F` `ENTER_SELECT_DUNGEON`

- 文档：`protocol/game/client-to-server/0x000F.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## CMD `0x0010` `SELECT_DUNGEON`

- 文档：`protocol/game/client-to-server/0x0010.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 1 | u8 | unknown |
| 5 | 4 | u32 | unknown |

## CMD `0x0011` `SEND_MESSAGE`

- 文档：`protocol/game/client-to-server/0x0011.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（variable-width-primitive），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 4 | u32 | unknown |
| 7 | ? | lenprefixed? | unknown |
| ? | ? | lenprefixed? | unknown |
| ? | 1 | u8 | unknown |

## CMD `0x0019` `COMPOUND_ITEM`

- 文档：`protocol/game/client-to-server/0x0019.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 1 | u8 | unknown |
| 5 | 2 | u16 | unknown |
| 7 | 1 | u8 | unknown |
| 8 | 1 | u8 | unknown |

## CMD `0x001F` `ACCEPT_QUEST`

- 文档：`protocol/game/client-to-server/0x001F.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |

## CMD `0x0020` `GIVEUP_QUEST`

- 文档：`protocol/game/client-to-server/0x0020.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |

## CMD `0x0022` `FINISH_QUEST`

- 文档：`protocol/game/client-to-server/0x0022.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `10`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 2 | u16 | unknown |
| 8 | 2 | u16 | unknown |

## CMD `0x0023` `SET_USER_POSITION`

- 文档：`protocol/game/client-to-server/0x0023.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `7`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 1 | u8 | unknown |
| 5 | 2 | u16 | unknown |

## CMD `0x0024` `SET_USER_AREA`

- 文档：`protocol/game/client-to-server/0x0024.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `15`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 2 | u16 | unknown |
| 9 | 2 | u16 | unknown |
| 11 | 4 | u32 | unknown |

## CMD `0x0027` `DIE_MONSTER`

- 文档：`protocol/game/client-to-server/0x0027.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `33 + n×10`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 4 | u32 | unknown |
| 16 | 4 | u32 | unknown |
| 20 | 1 | u8 | unknown |
| 21 | 2 | u16 | unknown |
| 23 | 2 | u16 | unknown |
| 25 | 1 | u8 | unknown |
| 26 | 1 | u8 | unknown |
| 27 | 1 | u8 | unknown |
| 28 | 1 | u8 | unknown |
| 29 | 2 | u16 | unknown |
| 31 | 1 | u8 | unknown |
| 32 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 2 | u16 | unknown |
| (组内 4) | 4 | u32 | unknown |
| (组内 8) | 2 | u16 | unknown |

## CMD `0x0028` `DIE_CHARACTER`

- 文档：`protocol/game/client-to-server/0x0028.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |

## CMD `0x002A` `GIVEUP_GAME`

- 文档：`protocol/game/client-to-server/0x002A.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x002D` `MOVE_MAP`

- 文档：`protocol/game/client-to-server/0x002D.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `24 + n1×2 + n2×2 + n3×4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 4 | u32 | unknown |
| 10 | 4 | u32 | unknown |
| 14 | 4 | u32 | unknown |
| 18 | 1 | u8 | unknown |
| 19 | 2 | u16 | unknown |
| 21 | 2 | u16 | unknown |
| 23 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| *重复组2 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| *重复组3 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |

## CMD `0x002E` `SET_PLAY_RESULT`

- 文档：`protocol/game/client-to-server/0x002E.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `42 + n×7`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 1 | u8 | unknown |
| 5 | 4 | u32 | unknown |
| 9 | 4 | u32 | unknown |
| 13 | 4 | u32 | unknown |
| 17 | 4 | u32 | unknown |
| 21 | 4 | u32 | unknown |
| 25 | 4 | u32 | unknown |
| 29 | 4 | u32 | unknown |
| 33 | 1 | u8 | unknown |
| 34 | 4 | u32 | unknown |
| 38 | 4 | u32 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 4 | u32 | unknown |
| (组内 6) | 1 | u8 | unknown |

## CMD `0x002F` `DROP_ITEM`

- 文档：`protocol/game/client-to-server/0x002F.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## CMD `0x0041` `GEN_CERATICKET`

- 文档：`protocol/game/client-to-server/0x0041.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x005E` `MAILBOX_SEND`

- 文档：`protocol/game/client-to-server/0x005E.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x005F` `MAILBOX_EXTRACT_ITEM`

- 文档：`protocol/game/client-to-server/0x005F.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |

## CMD `0x0060` `MAILBOX_OPEN`

- 文档：`protocol/game/client-to-server/0x0060.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x0064` `RENAME_CREATURE`

- 文档：`protocol/game/client-to-server/0x0064.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（variable-width-primitive），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | ? | lenprefixed? | unknown |
| ? | 1 | u8 | unknown |
| ? | ? | lenprefixed? | unknown |
| ? | 1 | u8 | unknown |
| ? | 1 | u8 | unknown |
| ? | 1 | u8 | unknown |
| ? | 1 | u8 | unknown |

## CMD `0x0066` `HATCH_CREATURE`

- 文档：`protocol/game/client-to-server/0x0066.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x0079` `CHANGE_HOST`

- 文档：`protocol/game/client-to-server/0x0079.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

