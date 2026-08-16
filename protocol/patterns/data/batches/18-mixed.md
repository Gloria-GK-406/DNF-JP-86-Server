# 富化工作包 18-mixed

**上游主模块**：`mixed: Network/Handlers/InventoryHandler.Trade.cs, Network/Handlers/InventoryHandler.cs, Network/Handlers/InventoryRefreshSender.cs, Network/Handlers/LoginHandler.cs, Network/Handlers/LotteryItemHandler.cs, Network/Handlers/LotteryItemResponseSender.cs, Network/Handlers/LuckyStarClientNotifier.cs, Network/Handlers/Pets/PetCreatureHandler.cs, Network/Handlers/Pets/PetCreatureRuntimeService.cs, Network/Handlers/RaidHandler.Rewards.cs, Network/Handlers/RaidHandler.Runtime.cs, Network/Handlers/RaidHandler.Situation.cs`

**输出分片**：`protocol/patterns/data/meanings/18-mixed.tsv`（本包独占，勿写其他文件）

本包 26 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## NOTI `0x0168` `ACHIEVEMENT_COMPLETE`

- 文档：`protocol/game/server-to-client/NOTI-0x0168.md`
- 上游文件：`Network/Handlers/InventoryHandler.Trade.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `18`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 4 | u32 | unknown |
| 16 | 2 | u16 | unknown |

## NOTI `0x0056` `SERVER_BROADCAST`

- 文档：`protocol/game/server-to-client/NOTI-0x0056.md`
- 上游文件：`Network/Handlers/InventoryHandler.cs`, `Network/Handlers/LotteryItemResponseSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## NOTI `0x000E` `UPDATE_ITEM_LIST`

- 文档：`protocol/game/server-to-client/NOTI-0x000E.md`
- 上游文件：`Network/Handlers/InventoryRefreshSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `87`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 84 | bytes | unknown |

## NOTI `0x0069` `CREATURE_ITEM_LIST`

- 文档：`protocol/game/server-to-client/NOTI-0x0069.md`
- 上游文件：`Network/Handlers/InventoryRefreshSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1 + n×44`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| (组内 4) | 1 | u8 | unknown |
| (组内 5) | 1 | u8 | unknown |
| (组内 6) | 4 | u32 | unknown |
| (组内 10) | 1 | u8 | unknown |
| (组内 11) | 1 | u8 | unknown |
| (组内 12) | 1 | u8 | unknown |
| (组内 13) | 30 | dstringw | unknown |
| (组内 43) | 1 | u8 | unknown |

## CMD `0x0001` `LOGIN`

- 文档：`protocol/game/client-to-server/0x0001.md`
- 上游文件：`Network/Handlers/LoginHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（variable-width-primitive），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | ? | lenprefixed? | unknown |
| ? | ? | lenprefixed? | unknown |
| ? | 1 | u8 | unknown |
| ? | 4 | u32 | unknown |
| ? | 16 | bytes | unknown |
| ? | 4 | u32 | unknown |
| ? | 2 | u16 | unknown |
| ? | 4 | u32 | unknown |
| ? | 1 | u8 | unknown |
| ? | 1 | u8 | unknown |

## NOTI `0x0001` `CHANNELINFO`

- 文档：`protocol/game/server-to-client/NOTI-0x0001.md`
- 上游文件：`Network/Handlers/LoginHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |

## NOTI `0x01A1` `INFORM_NOTICE`

- 文档：`protocol/game/server-to-client/NOTI-0x01A1.md`
- 上游文件：`Network/Handlers/LoginHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1 + n×1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 1 | u8 | unknown |

## CMD `0x001B` `USE_LOTTERY_ITEM`

- 文档：`protocol/game/client-to-server/0x001B.md`
- 上游文件：`Network/Handlers/LotteryItemHandler.cs`, `Network/Handlers/LotteryItemResponseSender.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |

## CMD `0x00D9` `OVERFLOW_INFO`

- 文档：`protocol/game/client-to-server/0x00D9.md`
- 上游文件：`Network/Handlers/LotteryItemHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `3`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |

## CMD `0x03F6` `INCREASE_CHANCE_LOTTERY_RESET`

- 文档：`protocol/game/client-to-server/0x03F6.md`
- 上游文件：`Network/Handlers/LotteryItemHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `21`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 21 | bytes | unknown |

## NOTI `0x03D8` `INCREASE_CHANCE_LOTTERY_ALL`

- 文档：`protocol/game/server-to-client/NOTI-0x03D8.md`
- 上游文件：`Network/Handlers/LotteryItemResponseSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `204`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 204 | bytes | unknown |

## CMD `0x00C5` `SAVE_GAME_OPTION_1`

- 文档：`protocol/game/client-to-server/0x00C5.md`
- 上游文件：`Network/Handlers/LuckyStarClientNotifier.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x0373` `CHARGE_RENTPOINT`

- 文档：`protocol/game/client-to-server/0x0373.md`
- 上游文件：`Network/Handlers/LuckyStarClientNotifier.cs`, `Network/Handlers/LuckyStarHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `21`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 21 | bytes | unknown |

## NOTI `0x0065` `RENAME_CREATURE`

- 文档：`protocol/game/server-to-client/NOTI-0x0065.md`
- 上游文件：`Network/Handlers/Pets/PetCreatureHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `22`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 20 | dstringw | unknown |

## NOTI `0x0067` `CREATURE_STATE`

- 文档：`protocol/game/server-to-client/NOTI-0x0067.md`
- 上游文件：`Network/Handlers/Pets/PetCreatureHandler.cs`, `Network/Handlers/Pets/PetCreatureRuntimeService.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |

## NOTI `0x0077` `CREATURE_SCRIPT_MESSAGE`

- 文档：`protocol/game/server-to-client/NOTI-0x0077.md`
- 上游文件：`Network/Handlers/Pets/PetCreatureHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `260`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 256 | dstringw | unknown |

## NOTI `0x0064` `DIED_CREATURE`

- 文档：`protocol/game/server-to-client/NOTI-0x0064.md`
- 上游文件：`Network/Handlers/Pets/PetCreatureRuntimeService.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## NOTI `0x0066` `GAIN_EXP_CREATURE`

- 文档：`protocol/game/server-to-client/NOTI-0x0066.md`
- 上游文件：`Network/Handlers/Pets/PetCreatureRuntimeService.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 1 | u8 | unknown |

## NOTI `0x006A` `EVOLUTE_CREATURE`

- 文档：`protocol/game/server-to-client/NOTI-0x006A.md`
- 上游文件：`Network/Handlers/Pets/PetCreatureRuntimeService.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |

## NOTI `0x006B` `REVIVAL_CREATURE`

- 文档：`protocol/game/server-to-client/NOTI-0x006B.md`
- 上游文件：`Network/Handlers/Pets/PetCreatureRuntimeService.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## NOTI `0x0258` `RAID_MOVIE_SKIP`

- 文档：`protocol/game/server-to-client/NOTI-0x0258.md`
- 上游文件：`Network/Handlers/RaidHandler.Rewards.cs`
- 提取状态：`structure-unknown`（polymorphic-payload），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## NOTI `0x0259` `RAID_REWARD_LIST`

- 文档：`protocol/game/server-to-client/NOTI-0x0259.md`
- 上游文件：`Network/Handlers/RaidHandler.Rewards.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8 + n×15`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 1 | u8 | unknown |
| (组内 3) | 4 | u32 | unknown |
| (组内 7) | 4 | u32 | unknown |
| (组内 11) | 4 | u32 | unknown |

## NOTI `0x024B` `RAID_CHANGE_DUNGEON_STATE`

- 文档：`protocol/game/server-to-client/NOTI-0x024B.md`
- 上游文件：`Network/Handlers/RaidHandler.Runtime.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `12`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |

## NOTI `0x024C` `RAID_STATE`

- 文档：`protocol/game/server-to-client/NOTI-0x024C.md`
- 上游文件：`Network/Handlers/RaidHandler.Runtime.cs`, `SelfTests/RaidProtocolSelfTest.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |

## NOTI `0x025A` `RAID_RESULT`

- 文档：`protocol/game/server-to-client/NOTI-0x025A.md`
- 上游文件：`Network/Handlers/RaidHandler.Runtime.cs`, `SelfTests/RaidProtocolSelfTest.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `21`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 4 | u32 | unknown |
| 16 | 4 | u32 | unknown |
| 20 | 1 | u8 | unknown |

## NOTI `0x027A` `RAID_BUFF_SYSTEM`

- 文档：`protocol/game/server-to-client/NOTI-0x027A.md`
- 上游文件：`Network/Handlers/RaidHandler.Situation.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `15`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 2 | u16 | unknown |
| 5 | 2 | u16 | unknown |
| 7 | 4 | u32 | unknown |
| 11 | 4 | u32 | unknown |

