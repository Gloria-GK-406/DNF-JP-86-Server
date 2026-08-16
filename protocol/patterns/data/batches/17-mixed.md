# 富化工作包 17-mixed

**上游主模块**：`mixed: Network/Handlers/InventoryHandler.ChronicleGrowth.cs, Network/Handlers/InventoryHandler.Disjoint.cs, Network/Handlers/InventoryHandler.EmblemCompound.cs, Network/Handlers/InventoryHandler.Equipment.cs, Network/Handlers/InventoryHandler.Experience.cs, Network/Handlers/InventoryHandler.InvestAmplifyOption.cs, Network/Handlers/InventoryHandler.ItemLock.cs, Network/Handlers/InventoryHandler.Move.cs, Network/Handlers/InventoryHandler.Purify.cs, Network/Handlers/InventoryHandler.RandomOption.cs, Network/Handlers/InventoryHandler.Repair.cs, Network/Handlers/InventoryHandler.ResetItemQuality.cs, Network/Handlers/InventoryHandler.Trade.cs`

**输出分片**：`protocol/patterns/data/meanings/17-mixed.tsv`（本包独占，勿写其他文件）

本包 26 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## CMD `0x010F` `UPGRADE_CHRONICLE`

- 文档：`protocol/game/client-to-server/0x010F.md`
- 上游文件：`Network/Handlers/InventoryHandler.ChronicleGrowth.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `19`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 2 | u16 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 1 | u8 | unknown |
| 13 | 2 | u16 | unknown |
| 15 | 4 | u32 | unknown |

## CMD `0x001A` `DISJOINT_ITEM`

- 文档：`protocol/game/client-to-server/0x001A.md`
- 上游文件：`Network/Handlers/InventoryHandler.Disjoint.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 2 | u16 | unknown |
| 5 | 4 | u32 | unknown |

## CMD `0x0100` `COMPOUND_EMBLEM`

- 文档：`protocol/game/client-to-server/0x0100.md`
- 上游文件：`Network/Handlers/InventoryHandler.EmblemCompound.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1 + n×6`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| (组内 4) | 2 | u16 | unknown |

## CMD `0x0050` `UPGRADE_ITEM`

- 文档：`protocol/game/client-to-server/0x0050.md`
- 上游文件：`Network/Handlers/InventoryHandler.Equipment.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `140`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 2 | u16 | unknown |
| 10 | 2 | u16 | unknown |
| 12 | 128 | lenprefixed? | unknown |

## CMD `0x00CE` `ADD_AVATAR_SOCKET`

- 文档：`protocol/game/client-to-server/0x00CE.md`
- 上游文件：`Network/Handlers/InventoryHandler.Equipment.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 2 | u16 | unknown |

## CMD `0x0110` `ENCHANT_BY_BEAD`

- 文档：`protocol/game/client-to-server/0x0110.md`
- 上游文件：`Network/Handlers/InventoryHandler.Equipment.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `6`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 2 | u16 | unknown |

## CMD `0x031C` `USE_EMBLEM_FOR_EQUIPMENT`

- 文档：`protocol/game/client-to-server/0x031C.md`
- 上游文件：`Network/Handlers/InventoryHandler.Equipment.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `8 + n×7`

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

## CMD `0x031D` `ADD_EQUIPMENT_SOCKET`

- 文档：`protocol/game/client-to-server/0x031D.md`
- 上游文件：`Network/Handlers/InventoryHandler.Equipment.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 2 | u16 | unknown |

## CMD `0x001E` `INCREASE_STATUS`

- 文档：`protocol/game/client-to-server/0x001E.md`
- 上游文件：`Network/Handlers/InventoryHandler.Experience.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## CMD `0x00CD` `INVEST_ITEM_AMPLIFY_OPTION`

- 文档：`protocol/game/client-to-server/0x00CD.md`
- 上游文件：`Network/Handlers/InventoryHandler.InvestAmplifyOption.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `142`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 4 | u32 | unknown |
| 7 | 2 | u16 | unknown |
| 9 | 4 | u32 | unknown |
| 13 | 1 | u8 | unknown |
| 14 | 128 | lenprefixed? | unknown |

## CMD `0x010B` `REQUEST_ITEM_LOCK`

- 文档：`protocol/game/client-to-server/0x010B.md`
- 上游文件：`Network/Handlers/InventoryHandler.ItemLock.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `3`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |

## CMD `0x010C` `REQUEST_ITEM_UNLOCK`

- 文档：`protocol/game/client-to-server/0x010C.md`
- 上游文件：`Network/Handlers/InventoryHandler.ItemLock.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x010D` `REQUEST_ITEM_UNLOCK_CANCEL`

- 文档：`protocol/game/client-to-server/0x010D.md`
- 上游文件：`Network/Handlers/InventoryHandler.ItemLock.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## NOTI `0x00FB` `ITEM_LOCK_LIST`

- 文档：`protocol/game/server-to-client/NOTI-0x00FB.md`
- 上游文件：`Network/Handlers/InventoryHandler.ItemLock.cs`, `Network/Handlers/InventoryRefreshSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2 + n×8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 1 | u8 | unknown |
| (组内 1) | 2 | u16 | unknown |
| (组内 3) | 1 | u8 | unknown |
| (组内 4) | 4 | u32 | unknown |

## NOTI `0x00FC` `ITEM_UNLOCK`

- 文档：`protocol/game/server-to-client/NOTI-0x00FC.md`
- 上游文件：`Network/Handlers/InventoryHandler.ItemLock.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `3`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |

## CMD `0x02CA` `REQUEST_ITEM_SORT_LOCK`

- 文档：`protocol/game/client-to-server/0x02CA.md`
- 上游文件：`Network/Handlers/InventoryHandler.Move.cs`, `Network/Handlers/InventoryRefreshSender.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x02CB` `REQUEST_ITEM_SORT_UNLOCK`

- 文档：`protocol/game/client-to-server/0x02CB.md`
- 上游文件：`Network/Handlers/InventoryHandler.Move.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x00CC` `PURIFY_ITEM`

- 文档：`protocol/game/client-to-server/0x00CC.md`
- 上游文件：`Network/Handlers/InventoryHandler.Purify.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `12`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 2 | u16 | unknown |
| 8 | 4 | u32 | unknown |

## CMD `0x0191` `UNSEAL_RANDOM_OPTION`

- 文档：`protocol/game/client-to-server/0x0191.md`
- 上游文件：`Network/Handlers/InventoryHandler.RandomOption.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |

## CMD `0x01B6` `CHANGE_RANDOM_OPTION`

- 文档：`protocol/game/client-to-server/0x01B6.md`
- 上游文件：`Network/Handlers/InventoryHandler.RandomOption.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 1 | u8 | unknown |

## CMD `0x0017` `REPAIR_EQUIPMENT`

- 文档：`protocol/game/client-to-server/0x0017.md`
- 上游文件：`Network/Handlers/InventoryHandler.Repair.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `7`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 2 | u16 | unknown |
| 5 | 2 | u16 | unknown |

## CMD `0x0051` `RESET_ITEM_ATTR`

- 文档：`protocol/game/client-to-server/0x0051.md`
- 上游文件：`Network/Handlers/InventoryHandler.ResetItemQuality.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x0015` `BUY_ITEM`

- 文档：`protocol/game/client-to-server/0x0015.md`
- 上游文件：`Network/Handlers/InventoryHandler.Trade.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `16`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 4 | u32 | unknown |

## CMD `0x0016` `SELL_ITEM`

- 文档：`protocol/game/client-to-server/0x0016.md`
- 上游文件：`Network/Handlers/InventoryHandler.Trade.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `13`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 2 | u16 | unknown |
| 5 | 4 | u32 | unknown |
| 9 | 4 | u32 | unknown |

## CMD `0x0131` `CREATE_ACCOUNT_CARGO`

- 文档：`protocol/game/client-to-server/0x0131.md`
- 上游文件：`Network/Handlers/InventoryHandler.Trade.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## NOTI `0x0033` `MONEY_FULL`

- 文档：`protocol/game/server-to-client/NOTI-0x0033.md`
- 上游文件：`Network/Handlers/InventoryHandler.Trade.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |
| 5 | 4 | u32 | unknown |

