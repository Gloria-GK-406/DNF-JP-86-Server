# 富化工作包 09-raidhandler-progression

**上游主模块**：`Network/Handlers/RaidHandler.Progression.cs`

**输出分片**：`protocol/patterns/data/meanings/09-raidhandler-progression.tsv`（本包独占，勿写其他文件）

本包 8 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## NOTI `0x0248` `RAID_SET_SYMBOL`

- 文档：`protocol/game/server-to-client/NOTI-0x0248.md`
- 上游文件：`Network/Handlers/RaidHandler.Progression.cs`, `Network/Handlers/RaidHandler.Runtime.cs`, `SelfTests/RaidProtocolSelfTest.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4 + n×8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| (组内 4) | 4 | u32 | unknown |

## NOTI `0x0249` `RAID_DUNGEON_PARTICIPATION_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x0249.md`
- 上游文件：`Network/Handlers/RaidHandler.Progression.cs`, `Network/Handlers/RaidHandler.Situation.cs`, `Network/Handlers/RaidHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `20`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 4 | u32 | unknown |
| 16 | 4 | u32 | unknown |

## NOTI `0x024A` `RAID_DUNGEON_STATE`

- 文档：`protocol/game/server-to-client/NOTI-0x024A.md`
- 上游文件：`Network/Handlers/RaidHandler.Progression.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `12 + n×8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |
| (组内 4) | 4 | u32 | unknown |

## NOTI `0x024E` `RAID_MEMBER_STATE`

- 文档：`protocol/game/server-to-client/NOTI-0x024E.md`
- 上游文件：`Network/Handlers/RaidHandler.Progression.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `3`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |

## NOTI `0x0253` `PREPARE_START_RAID`

- 文档：`protocol/game/server-to-client/NOTI-0x0253.md`
- 上游文件：`Network/Handlers/RaidHandler.Progression.cs`
- 提取状态：`structure-unknown`（polymorphic-payload），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## NOTI `0x0255` `RAID_SET_TIMER`

- 文档：`protocol/game/server-to-client/NOTI-0x0255.md`
- 上游文件：`Network/Handlers/RaidHandler.Progression.cs`, `Network/Handlers/RaidHandler.Rewards.cs`, `Network/Handlers/RaidHandler.Runtime.cs`, `Network/Handlers/RaidHandler.Situation.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `16`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 4 | u32 | unknown |

## NOTI `0x0256` `RAID_REMAIN_TIME`

- 文档：`protocol/game/server-to-client/NOTI-0x0256.md`
- 上游文件：`Network/Handlers/RaidHandler.Progression.cs`, `Network/Handlers/RaidHandler.Rewards.cs`, `Network/Handlers/RaidHandler.Situation.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |

## NOTI `0x0257` `RAID_ENTRY_COST_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x0257.md`
- 上游文件：`Network/Handlers/RaidHandler.Progression.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `4 + n×10`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 4 | u32 | unknown |
| (组内 6) | 4 | u32 | unknown |

