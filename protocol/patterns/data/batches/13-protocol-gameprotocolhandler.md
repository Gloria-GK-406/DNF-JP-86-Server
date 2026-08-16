# 富化工作包 13-protocol-gameprotocolhandler

**上游主模块**：`Network/Protocol/GameProtocolHandler.cs`

**输出分片**：`protocol/patterns/data/meanings/13-protocol-gameprotocolhandler.tsv`（本包独占，勿写其他文件）

本包 11 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## CMD `0x02BC` `DAILY_CHALLENGE_REWARD`

- 文档：`protocol/game/client-to-server/0x02BC.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |

## CMD `0x02D7` `REJOIN_DUNGEON`

- 文档：`protocol/game/client-to-server/0x02D7.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |

## CMD `0x02D8` `CANCEL_REJOIN_DUNGEON`

- 文档：`protocol/game/client-to-server/0x02D8.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |

## CMD `0x0351` `P2P_HOLE_PUNCHING_SUCCESS_RATE`

- 文档：`protocol/game/client-to-server/0x0351.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |
| 5 | 4 | u32 | unknown |

## CMD `0x0353` `USE_TITLE_CHANGE_ITEM`

- 文档：`protocol/game/client-to-server/0x0353.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |

## CMD `0x035D` `UPGRADE_CARRY_GOLD`

- 文档：`protocol/game/client-to-server/0x035D.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `14`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 14 | bytes | unknown |

## CMD `0x0388` `SELECT_COLLECTBOX`

- 文档：`protocol/game/client-to-server/0x0388.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x0389` `ADD_COLLECTBOX_ITEM`

- 文档：`protocol/game/client-to-server/0x0389.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 4 | u32 | unknown |

## CMD `0x038A` `REMOVE_COLLECTBOX_ITEM`

- 文档：`protocol/game/client-to-server/0x038A.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |

## CMD `0x03B6` `VERY_DIFFICULT_HELL_PARTY`

- 文档：`protocol/game/client-to-server/0x03B6.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `17`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 17 | bytes | unknown |

## NOTI `0x0286` `DAILY_CHALLENGE`

- 文档：`protocol/game/server-to-client/NOTI-0x0286.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `28`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |
| 12 | 4 | u32 | unknown |
| 16 | 4 | u32 | unknown |
| 20 | 4 | u32 | unknown |
| 24 | 4 | u32 | unknown |

