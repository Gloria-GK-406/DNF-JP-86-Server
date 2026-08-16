# 富化工作包 08-partyhandler

**上游主模块**：`Network/Handlers/PartyHandler.cs`

**输出分片**：`protocol/patterns/data/meanings/08-partyhandler.tsv`（本包独占，勿写其他文件）

本包 9 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## CMD `0x000B` `RESPONSE_PEER`

- 文档：`protocol/game/client-to-server/0x000B.md`
- 上游文件：`Network/Handlers/PartyHandler.cs`, `Network/Handlers/PvpRoomHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `16`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 2 | u16 | unknown |
| 5 | 2 | u16 | unknown |
| 7 | 2 | u16 | unknown |
| 9 | 1 | u8 | unknown |
| 10 | 4 | u32 | unknown |
| 14 | 2 | u16 | unknown |

## CMD `0x000C` `SET_PARTY_INFO`

- 文档：`protocol/game/client-to-server/0x000C.md`
- 上游文件：`Network/Handlers/PartyHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（variable-width-primitive），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| 2 | ? | lenprefixed? | unknown |
| ? | 1 | u8 | unknown |
| ? | 2 | u16 | unknown |
| ? | 1 | u8 | unknown |
| ? | 1 | u8 | unknown |
| ? | 2 | u16 | unknown |
| ? | 1 | u8 | unknown |
| ? | 4 | u32 | unknown |

## CMD `0x000D` `LEAVE_PARTY`

- 文档：`protocol/game/client-to-server/0x000D.md`
- 上游文件：`Network/Handlers/PartyHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x000E` `WALKOUT_PARTY_MEMBER`

- 文档：`protocol/game/client-to-server/0x000E.md`
- 上游文件：`Network/Handlers/PartyHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## NOTI `0x0007` `REQUEST_PEER`

- 文档：`protocol/game/server-to-client/NOTI-0x0007.md`
- 上游文件：`Network/Handlers/PartyHandler.cs`, `Network/Handlers/PvpRoomHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `11`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 4 | u32 | unknown |
| 7 | 4 | u32 | unknown |

## NOTI `0x0008` `RESPONSE_PEER`

- 文档：`protocol/game/server-to-client/NOTI-0x0008.md`
- 上游文件：`Network/Handlers/PartyHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `7`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 1 | u8 | unknown |
| 3 | 4 | u32 | unknown |

## NOTI `0x0009` `PARTY_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x0009.md`
- 上游文件：`Network/Handlers/PartyHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `81 + n1×5 + n2×7`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 1 | u8 | unknown |
| 5 | 1 | u8 | unknown |
| 6 | 64 | dstringw | unknown |
| 70 | 1 | u8 | unknown |
| 71 | 1 | u8 | unknown |
| 72 | 2 | u16 | unknown |
| 74 | 1 | u8 | unknown |
| 75 | 1 | u8 | unknown |
| 76 | 1 | u8 | unknown |
| 77 | 1 | u8 | unknown |
| 78 | 1 | u8 | unknown |
| 79 | 1 | u8 | unknown |
| 80 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 1 | u8 | unknown |
| (组内 3) | 1 | u8 | unknown |
| (组内 4) | 1 | u8 | unknown |
| *重复组2 ×n* | | | |
| (组内 0) | 1 | u8 | unknown |
| (组内 1) | 4 | u32 | unknown |
| (组内 5) | 1 | u8 | unknown |
| (组内 6) | 1 | u8 | unknown |

## NOTI `0x000B` `USER_UDP_IP_PORT`

- 文档：`protocol/game/server-to-client/NOTI-0x000B.md`
- 上游文件：`Network/Handlers/PartyHandler.cs`, `Network/Handlers/PvpRoomHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1 + n×22`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 4 | u32 | unknown |
| (组内 6) | 4 | u32 | unknown |
| (组内 10) | 2 | u16 | unknown |
| (组内 12) | 4 | bytes | unknown |
| (组内 16) | 1 | u8 | unknown |
| (组内 17) | 4 | u32 | unknown |
| (组内 21) | 1 | u8 | unknown |

## NOTI `0x0099` `PARTY_MEMBER_REALTIME_INFO`

- 文档：`protocol/game/server-to-client/NOTI-0x0099.md`
- 上游文件：`Network/Handlers/PartyHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `1 + n×5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 1 | u8 | unknown |
| (组内 3) | 1 | u8 | unknown |
| (组内 4) | 1 | u8 | unknown |

