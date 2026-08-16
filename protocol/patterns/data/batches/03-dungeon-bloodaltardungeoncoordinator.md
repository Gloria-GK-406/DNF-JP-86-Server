# 富化工作包 03-dungeon-bloodaltardungeoncoordinator

**上游主模块**：`Network/Handlers/Dungeon/BloodAltarDungeonCoordinator.cs`

**输出分片**：`protocol/patterns/data/meanings/03-dungeon-bloodaltardungeoncoordinator.tsv`（本包独占，勿写其他文件）

本包 7 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## NOTI `0x00D0` `BLOOD_DUNGEON_STATE_RANKING`

- 文档：`protocol/game/server-to-client/NOTI-0x00D0.md`
- 上游文件：`Network/Handlers/Dungeon/BloodAltarDungeonCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `24`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 4 | u32 | unknown |
| 16 | 4 | u32 | unknown |
| 20 | 4 | u32 | unknown |

## NOTI `0x00D1` `BLOOD_DUNGEON_STATE_REWARD`

- 文档：`protocol/game/server-to-client/NOTI-0x00D1.md`
- 上游文件：`Network/Handlers/Dungeon/BloodAltarDungeonCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2 + n×9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 1 | u8 | unknown |
| (组内 1) | 4 | u32 | unknown |
| (组内 5) | 4 | u32 | unknown |

## NOTI `0x00D2` `BLOOD_MONSTER_SPAWN`

- 文档：`protocol/game/server-to-client/NOTI-0x00D2.md`
- 上游文件：`Network/Handlers/Dungeon/BloodAltarDungeonCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4 + n×17`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 1 | u8 | unknown |
| (组内 1) | 2 | u16 | unknown |
| (组内 3) | 4 | u32 | unknown |
| (组内 7) | 1 | u8 | unknown |
| (组内 8) | 1 | u8 | unknown |
| (组内 9) | 2 | u16 | unknown |
| (组内 11) | 2 | u16 | unknown |
| (组内 13) | 2 | u16 | unknown |
| (组内 15) | 2 | u16 | unknown |

## NOTI `0x00D3` `START_BLOOD_MAP`

- 文档：`protocol/game/server-to-client/NOTI-0x00D3.md`
- 上游文件：`Network/Handlers/Dungeon/BloodAltarDungeonCoordinator.cs`, `Network/Handlers/Dungeon/DungeonMapHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 1 | u8 | unknown |

## NOTI `0x00D7` `BLOOD_ROUND_INTERVAL_TIME`

- 文档：`protocol/game/server-to-client/NOTI-0x00D7.md`
- 上游文件：`Network/Handlers/Dungeon/BloodAltarDungeonCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |

## NOTI `0x0104` `ITEM_EFFECT`

- 文档：`protocol/game/server-to-client/NOTI-0x0104.md`
- 上游文件：`Network/Handlers/Dungeon/BloodAltarDungeonCoordinator.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `6`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |

## NOTI `0x0129` `ULTIMATE_DIFFICULTY_UI`

- 文档：`protocol/game/server-to-client/NOTI-0x0129.md`
- 上游文件：`Network/Handlers/Dungeon/BloodAltarDungeonCoordinator.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 2 | u16 | unknown |

