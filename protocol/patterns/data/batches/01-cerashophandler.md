# 富化工作包 01-cerashophandler

**上游主模块**：`Network/Handlers/CeraShopHandler.cs`

**输出分片**：`protocol/patterns/data/meanings/01-cerashophandler.tsv`（本包独占，勿写其他文件）

本包 6 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## CMD `0x0040` `BUY_CERASHOP_ITEM`

- 文档：`protocol/game/client-to-server/0x0040.md`
- 上游文件：`Network/Handlers/CeraShopHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `19`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 1 | u8 | unknown |
| 5 | 1 | u8 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 4 | u32 | unknown |
| 11 | 1 | u8 | unknown |
| 12 | 1 | u8 | unknown |
| 13 | 4 | u32 | unknown |
| 17 | 2 | u16 | unknown |

## CMD `0x0132` `UPGRADE_ACCOUNT_CARGO`

- 文档：`protocol/game/client-to-server/0x0132.md`
- 上游文件：`Network/Handlers/CeraShopHandler.cs`, `Network/Handlers/InventoryHandler.Package.cs`, `Network/Handlers/InventoryHandler.Trade.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x0198` `UPGRADE_CARGO`

- 文档：`protocol/game/client-to-server/0x0198.md`
- 上游文件：`Network/Handlers/CeraShopHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## NOTI `0x000D` `ITEM_LIST`

- 文档：`protocol/game/server-to-client/NOTI-0x000D.md`
- 上游文件：`Network/Handlers/CeraShopHandler.cs`, `Network/Handlers/Dungeon/DeathTowerCoordinator.cs`, `Network/Handlers/InventoryRefreshSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `93`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 2 | u16 | unknown |
| 5 | 2 | u16 | unknown |
| 7 | 2 | u16 | unknown |
| 9 | 84 | bytes | unknown |

## NOTI `0x0035` `CERA`

- 文档：`protocol/game/server-to-client/NOTI-0x0035.md`
- 上游文件：`Network/Handlers/CeraShopHandler.cs`, `Network/Handlers/InventoryHandler.Package.cs`, `Network/Handlers/InventoryHandler.Trade.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |
| 5 | 4 | u32 | unknown |

## NOTI `0x0063` `MAILBOX_ALARM`

- 文档：`protocol/game/server-to-client/NOTI-0x0063.md`
- 上游文件：`Network/Handlers/CeraShopHandler.cs`, `Network/Handlers/CraneMiniGameHandler.cs`, `Network/Handlers/LotteryItemResponseSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

