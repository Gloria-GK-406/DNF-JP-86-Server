# 富化工作包 07-inventoryhandler-package

**上游主模块**：`Network/Handlers/InventoryHandler.Package.cs`

**输出分片**：`protocol/patterns/data/meanings/07-inventoryhandler-package.tsv`（本包独占，勿写其他文件）

本包 7 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## CMD `0x0063` `COMPOUND_AVATAR`

- 文档：`protocol/game/client-to-server/0x0063.md`
- 上游文件：`Network/Handlers/InventoryHandler.Package.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `22`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 2 | u16 | unknown |
| 10 | 4 | u32 | unknown |
| 14 | 4 | u32 | unknown |
| 18 | 2 | u16 | unknown |
| 20 | 1 | u8 | unknown |
| 21 | 1 | u8 | unknown |

## CMD `0x00A0` `USE_BOOSTER_ITEM`

- 文档：`protocol/game/client-to-server/0x00A0.md`
- 上游文件：`Network/Handlers/InventoryHandler.Package.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## CMD `0x00D0` `USE_RANDOMBOX_ITEM`

- 文档：`protocol/game/client-to-server/0x00D0.md`
- 上游文件：`Network/Handlers/InventoryHandler.Package.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 2 | u16 | unknown |

## CMD `0x0207` `OPEN_CERAPACKAGE`

- 文档：`protocol/game/client-to-server/0x0207.md`
- 上游文件：`Network/Handlers/InventoryHandler.Package.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed-parametric`（counted-repeat-group），BodyLength = `3 + n×5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| (组内 4) | 1 | u8 | unknown |

## CMD `0x0342` `ADD_EQUIPMENT_EFFECT`

- 文档：`protocol/game/client-to-server/0x0342.md`
- 上游文件：`Network/Handlers/InventoryHandler.Package.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `21`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 21 | bytes | unknown |

## CMD `0x03EA` `BIND_PLUS`

- 文档：`protocol/game/client-to-server/0x03EA.md`
- 上游文件：`Network/Handlers/InventoryHandler.Package.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `144`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 144 | bytes | unknown |

## CMD `0x03F3` `USE_RANDOMBOX_ITEM_EXPAND`

- 文档：`protocol/game/client-to-server/0x03F3.md`
- 上游文件：`Network/Handlers/InventoryHandler.Package.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `18`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 4 | u32 | unknown |
| 7 | 2 | u16 | unknown |
| 9 | 4 | u32 | unknown |
| 13 | 1 | u8 | unknown |
| 14 | 2 | u16 | unknown |
| 16 | 2 | u16 | unknown |

