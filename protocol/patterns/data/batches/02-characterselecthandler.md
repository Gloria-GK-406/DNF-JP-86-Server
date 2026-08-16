# 富化工作包 02-characterselecthandler

**上游主模块**：`Network/Handlers/CharacterSelectHandler.cs`

**输出分片**：`protocol/patterns/data/meanings/02-characterselecthandler.tsv`（本包独占，勿写其他文件）

本包 8 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## CMD `0x0005` `CREATE_CHARACTER`

- 文档：`protocol/game/client-to-server/0x0005.md`
- 上游文件：`Network/Handlers/CharacterSelectHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（variable-width-primitive），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | ? | lenprefixed? | unknown |
| ? | 1 | u8 | unknown |
| ? | 1 | u8 | unknown |
| ? | 1 | u8 | unknown |
| ? | 1 | u8 | unknown |

## CMD `0x0006` `DELETE_CHARACTER`

- 文档：`protocol/game/client-to-server/0x0006.md`
- 上游文件：`Network/Handlers/CharacterSelectHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（variable-width-primitive），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | ? | lenprefixed? | unknown |

## CMD `0x0007` `RETURN_SELECT_CHARACTER`

- 文档：`protocol/game/client-to-server/0x0007.md`
- 上游文件：`Network/Handlers/CharacterSelectHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x0127` `CHANGE_CHARAC_SLOT`

- 文档：`protocol/game/client-to-server/0x0127.md`
- 上游文件：`Network/Handlers/CharacterSelectHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |

## CMD `0x0239` `SET_CLONE_TITLE`

- 文档：`protocol/game/client-to-server/0x0239.md`
- 上游文件：`Network/Handlers/CharacterSelectHandler.cs`, `Network/Handlers/InventoryHandler.Trade.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |

## CMD `0x0286` `CHARAC_VIEW_HIDDEN_CHARAC_INFO`

- 文档：`protocol/game/client-to-server/0x0286.md`
- 上游文件：`Network/Handlers/CharacterSelectHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x02B5` `CHECK_DOUBLE_CHARACTER_NAME`

- 文档：`protocol/game/client-to-server/0x02B5.md`
- 上游文件：`Network/Handlers/CharacterSelectHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（variable-width-primitive），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | ? | lenprefixed? | unknown |

## NOTI `0x0165` `CHARAC_INVISIBLE_FALGS`

- 文档：`protocol/game/server-to-client/NOTI-0x0165.md`
- 上游文件：`Network/Handlers/CharacterSelectHandler.cs`, `Network/Handlers/SettingsHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `3`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |

