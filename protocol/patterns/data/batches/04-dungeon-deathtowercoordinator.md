# 富化工作包 04-dungeon-deathtowercoordinator

**上游主模块**：`Network/Handlers/Dungeon/DeathTowerCoordinator.cs`

**输出分片**：`protocol/patterns/data/meanings/04-dungeon-deathtowercoordinator.tsv`（本包独占，勿写其他文件）

本包 10 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## CMD `0x0013` `MOVE_ITEMSPACE`

- 文档：`protocol/game/client-to-server/0x0013.md`
- 上游文件：`Network/Handlers/Dungeon/DeathTowerCoordinator.cs`, `Network/Handlers/InventoryHandler.Move.cs`, `Network/Handlers/KnightShieldHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `24`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 4 | u32 | unknown |
| 7 | 4 | u32 | unknown |
| 11 | 1 | u8 | unknown |
| 12 | 2 | u16 | unknown |
| 14 | 4 | u32 | unknown |
| 18 | 4 | u32 | unknown |
| 22 | 1 | u8 | unknown |
| 23 | 1 | u8 | unknown |

## CMD `0x0014` `SORT_ITEM`

- 文档：`protocol/game/client-to-server/0x0014.md`
- 上游文件：`Network/Handlers/Dungeon/DeathTowerCoordinator.cs`, `Network/Handlers/InventoryHandler.Move.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `3`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 1 | u8 | unknown |

## CMD `0x002C` `USE_STACKABLE`

- 文档：`protocol/game/client-to-server/0x002C.md`
- 上游文件：`Network/Handlers/Dungeon/DeathTowerCoordinator.cs`, `Network/Handlers/InventoryHandler.Package.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `10`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 4 | u32 | unknown |

## NOTI `0x001E` `FINISH_LOADING`

- 文档：`protocol/game/server-to-client/NOTI-0x001E.md`
- 上游文件：`Network/Handlers/Dungeon/DeathTowerCoordinator.cs`, `Network/Handlers/TownHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 1 | u8 | unknown |

## NOTI `0x0027` `GET_ITEM`

- 文档：`protocol/game/server-to-client/NOTI-0x0027.md`
- 上游文件：`Network/Handlers/Dungeon/DeathTowerCoordinator.cs`, `Network/Handlers/Dungeon/DungeonCombatHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |

## NOTI `0x008E` `DEATH_TOWER_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x008E.md`
- 上游文件：`Network/Handlers/Dungeon/DeathTowerCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `6`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 2 | u16 | unknown |

## NOTI `0x008F` `START_DEATH_TOWER_MAP`

- 文档：`protocol/game/server-to-client/NOTI-0x008F.md`
- 上游文件：`Network/Handlers/Dungeon/DeathTowerCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `23`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 2 | u16 | unknown |
| 8 | 1 | u8 | unknown |
| 9 | 4 | u32 | unknown |
| 13 | 2 | u16 | unknown |
| 15 | 4 | u32 | unknown |
| 19 | 1 | u8 | unknown |
| 20 | 1 | u8 | unknown |
| 21 | 1 | u8 | unknown |
| 22 | 1 | u8 | unknown |

## NOTI `0x0090` `DEATH_TOWER_STATE_RANKING`

- 文档：`protocol/game/server-to-client/NOTI-0x0090.md`
- 上游文件：`Network/Handlers/Dungeon/DeathTowerCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `283 + n×268`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |
| 5 | 4 | u32 | unknown |
| 9 | 1 | u8 | unknown |
| 10 | 4 | u32 | unknown |
| 14 | 1 | u8 | unknown |
| 15 | 256 | dstringw | unknown |
| 271 | 1 | u8 | unknown |
| 272 | 1 | u8 | unknown |
| 273 | 2 | u16 | unknown |
| 275 | 4 | u32 | unknown |
| 279 | 4 | u32 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 256 | dstringw | unknown |
| (组内 256) | 1 | u8 | unknown |
| (组内 257) | 1 | u8 | unknown |
| (组内 258) | 2 | u16 | unknown |
| (组内 260) | 4 | u32 | unknown |
| (组内 264) | 4 | u32 | unknown |

## NOTI `0x0091` `DEATH_TOWER_STATE_REWARD`

- 文档：`protocol/game/server-to-client/NOTI-0x0091.md`
- 上游文件：`Network/Handlers/Dungeon/DeathTowerCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `5 + n×8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| (组内 4) | 4 | u32 | unknown |

## NOTI `0x0092` `DEATH_TOWER_STATE_EPLP`

- 文档：`protocol/game/server-to-client/NOTI-0x0092.md`
- 上游文件：`Network/Handlers/Dungeon/DeathTowerCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

