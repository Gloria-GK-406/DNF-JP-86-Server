# 富化工作包 05-dungeon-dungeoncombathandler

**上游主模块**：`Network/Handlers/Dungeon/DungeonCombatHandler.cs`

**输出分片**：`protocol/patterns/data/meanings/05-dungeon-dungeoncombathandler.tsv`（本包独占，勿写其他文件）

本包 8 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## CMD `0x0029` `USE_COIN`

- 文档：`protocol/game/client-to-server/0x0029.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonCombatHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## CMD `0x002B` `GET_ITEM`

- 文档：`protocol/game/client-to-server/0x002B.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonCombatHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `20`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 1 | u8 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 2 | u16 | unknown |
| 8 | 2 | u16 | unknown |
| 10 | 2 | u16 | unknown |
| 12 | 2 | u16 | unknown |
| 14 | 2 | u16 | unknown |
| 16 | 2 | u16 | unknown |
| 18 | 2 | u16 | unknown |

## CMD `0x007B` `CHARACTER_STATISTIC`

- 文档：`protocol/game/client-to-server/0x007B.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonCombatHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `24`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 4 | u32 | unknown |
| 16 | 4 | u32 | unknown |
| 20 | 4 | u32 | unknown |

## NOTI `0x0003` `USER_STATE`

- 文档：`protocol/game/server-to-client/NOTI-0x0003.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonCombatHandler.cs`, `Network/Handlers/Dungeon/DungeonEntryHandler.cs`, `Network/Handlers/Dungeon/DungeonTownReturnCoordinator.cs`, `Network/Handlers/Dungeon/DungeonTutorialHandler.cs`, `Network/Handlers/PartyHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 1 | u8 | unknown |

## NOTI `0x0018` `AREA_USERS`

- 文档：`protocol/game/server-to-client/NOTI-0x0018.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonCombatHandler.cs`, `Network/Handlers/Dungeon/DungeonTownReturnCoordinator.cs`, `Network/Handlers/Dungeon/DungeonTutorialHandler.cs`, `Network/Handlers/PartyHandler.cs`, `Network/Handlers/PvpRoomHandler.cs`, `Network/Handlers/TownHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `12`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 2 | u16 | unknown |
| 8 | 2 | u16 | unknown |
| 10 | 1 | u8 | unknown |
| 11 | 1 | u8 | unknown |

## NOTI `0x0020` `DIE_STATE`

- 文档：`protocol/game/server-to-client/NOTI-0x0020.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonCombatHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 1 | u8 | unknown |

## NOTI `0x0028` `DROP_ITEM`

- 文档：`protocol/game/server-to-client/NOTI-0x0028.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonCombatHandler.cs`, `Network/Handlers/Dungeon/DungeonMechanismNotificationSender.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `54 + n1×4 + n2×3`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 2 | u16 | unknown |
| 6 | 2 | u16 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 1 | u8 | unknown |
| 13 | 4 | u32 | unknown |
| 17 | 2 | u16 | unknown |
| 19 | 4 | u32 | unknown |
| 23 | 1 | u8 | unknown |
| 24 | 1 | u8 | unknown |
| 25 | 2 | u16 | unknown |
| 27 | 4 | u32 | unknown |
| 31 | 1 | u8 | unknown |
| 32 | 2 | u16 | unknown |
| 34 | 1 | u8 | unknown |
| 35 | 1 | u8 | unknown |
| 36 | 1 | u8 | unknown |
| 37 | 1 | u8 | unknown |
| 38 | 1 | u8 | unknown |
| 39 | 1 | u8 | unknown |
| 40 | 1 | u8 | unknown |
| 41 | 1 | u8 | unknown |
| 42 | 1 | u8 | unknown |
| 43 | 1 | u8 | unknown |
| 44 | 2 | u16 | unknown |
| 46 | 1 | u8 | unknown |
| 47 | 1 | u8 | unknown |
| 48 | 1 | u8 | unknown |
| 49 | 1 | u8 | unknown |
| 50 | 1 | u8 | unknown |
| 51 | 1 | u8 | unknown |
| 52 | 2 | u16 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| *重复组2 ×n* | | | |
| (组内 0) | 1 | u8 | unknown |
| (组内 1) | 1 | u8 | unknown |
| (组内 2) | 1 | u8 | unknown |

## NOTI `0x00CA` `VILLAGE_OBJECT_LIST`

- 文档：`protocol/game/server-to-client/NOTI-0x00CA.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonCombatHandler.cs`, `Network/Handlers/Dungeon/DungeonTownReturnCoordinator.cs`, `Network/Handlers/Dungeon/DungeonTutorialHandler.cs`, `Network/Handlers/PartyHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |

