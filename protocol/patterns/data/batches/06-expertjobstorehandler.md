# 富化工作包 06-expertjobstorehandler

**上游主模块**：`Network/Handlers/ExpertJobStoreHandler.cs`

**输出分片**：`protocol/patterns/data/meanings/06-expertjobstorehandler.tsv`（本包独占，勿写其他文件）

本包 10 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## CMD `0x00EB` `REQUEST_DISJOINT_ITEM`

- 文档：`protocol/game/client-to-server/0x00EB.md`
- 上游文件：`Network/Handlers/ExpertJobStoreHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 2 | u16 | unknown |
| 4 | 1 | u8 | unknown |

## CMD `0x00EC` `REPAIR_DISJOINT_MACHINE`

- 文档：`protocol/game/client-to-server/0x00EC.md`
- 上游文件：`Network/Handlers/ExpertJobStoreHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x00F0` `UPGRADE_DISJOINT_MACHINE`

- 文档：`protocol/game/client-to-server/0x00F0.md`
- 上游文件：`Network/Handlers/ExpertJobStoreHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x0257` `CREATE_EXPERT_JOB_STORE`

- 文档：`protocol/game/client-to-server/0x0257.md`
- 上游文件：`Network/Handlers/ExpertJobStoreHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `11`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |
| 5 | 2 | u16 | unknown |
| 7 | 2 | u16 | unknown |
| 9 | 2 | u16 | unknown |

## CMD `0x0258` `ENTER_EXPERT_JOB_STORE`

- 文档：`protocol/game/client-to-server/0x0258.md`
- 上游文件：`Network/Handlers/ExpertJobStoreHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## CMD `0x025A` `USE_ENCHANT_STORE`

- 文档：`protocol/game/client-to-server/0x025A.md`
- 上游文件：`Network/Handlers/ExpertJobStoreHandler.cs`, `Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `13`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |
| 2 | 4 | u32 | unknown |
| 6 | 1 | u8 | unknown |
| 7 | 1 | u8 | unknown |
| 8 | 2 | u16 | unknown |
| 10 | 1 | u8 | unknown |
| 11 | 2 | u16 | unknown |

## NOTI `0x00CB` `REQUEST_DISJOINT_ITEM`

- 文档：`protocol/game/server-to-client/NOTI-0x00CB.md`
- 上游文件：`Network/Handlers/ExpertJobStoreHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 4 | u32 | unknown |
| 5 | 4 | u32 | unknown |

## NOTI `0x0216` `USE_ENCHANT_STORE`

- 文档：`protocol/game/server-to-client/NOTI-0x0216.md`
- 上游文件：`Network/Handlers/ExpertJobStoreHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |

## NOTI `0x021B` `CREATE_EXPERT_JOB_STORE`

- 文档：`protocol/game/server-to-client/NOTI-0x021B.md`
- 上游文件：`Network/Handlers/ExpertJobStoreHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `271 + n×1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 256 | dstringw | unknown |
| 259 | 1 | u8 | unknown |
| 260 | 1 | u8 | unknown |
| 261 | 2 | u16 | unknown |
| 263 | 2 | u16 | unknown |
| 265 | 4 | u32 | unknown |
| 269 | 1 | u8 | unknown |
| 270 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 1 | u8 | unknown |

## NOTI `0x021C` `CLOSE_EXPERT_JOB_STORE`

- 文档：`protocol/game/server-to-client/NOTI-0x021C.md`
- 上游文件：`Network/Handlers/ExpertJobStoreHandler.cs`
- 提取状态：`partial`（polymorphic-tail），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

