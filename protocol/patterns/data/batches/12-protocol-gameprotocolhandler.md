# 富化工作包 12-protocol-gameprotocolhandler

**上游主模块**：`Network/Protocol/GameProtocolHandler.cs`

**输出分片**：`protocol/patterns/data/meanings/12-protocol-gameprotocolhandler.tsv`（本包独占，勿写其他文件）

本包 26 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段填写有证据的可能含义。

## CMD `0x01C0` `SAVE_CHARACTER_OPTION`

- 文档：`protocol/game/client-to-server/0x01C0.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x01DC` `IMAGE_COMMUNICATION_EQUIPMENT_USE`

- 文档：`protocol/game/client-to-server/0x01DC.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x01E0` `VERIFY_CREATURE_QUEST`

- 文档：`protocol/game/client-to-server/0x01E0.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x01E5` `REQUEST_CHARAC_SKILL_INFO`

- 文档：`protocol/game/client-to-server/0x01E5.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## CMD `0x01E8` `SELECT_STRIKER`

- 文档：`protocol/game/client-to-server/0x01E8.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `3`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |

## CMD `0x01EC` `SKILL_INIT`

- 文档：`protocol/game/client-to-server/0x01EC.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-builder-site-found），BodyLength = `未知`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x01FB` `SAVE_QUEST_NOTIFY`

- 文档：`protocol/game/client-to-server/0x01FB.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed-parametric`（counted-repeat-group），BodyLength = `1 + n×4`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 4 | u32 | unknown |

## CMD `0x01FD` `COMBO_SKILL_INFO`

- 文档：`protocol/game/client-to-server/0x01FD.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed-parametric`（counted-repeat-group），BodyLength = `2 + n×5`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |
| (组内 2) | 1 | u8 | unknown |
| (组内 3) | 2 | u16 | unknown |

## CMD `0x01FF` `COMBO_SKILL_EXTENSION_QUICK_SLOT_RESET`

- 文档：`protocol/game/client-to-server/0x01FF.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x0218` `UPDATE_CONTRACT_OF_CUBE_INFO`

- 文档：`protocol/game/client-to-server/0x0218.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |

## CMD `0x0259` `CLOSE_EXPERT_JOB_STORE`

- 文档：`protocol/game/client-to-server/0x0259.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x025C` `UPGRADE_CARD`

- 文档：`protocol/game/client-to-server/0x025C.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed-parametric`（counted-repeat-group），BodyLength = `4 + n×3`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| 3 | 1 | u8 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 1 | u8 | unknown |
| (组内 1) | 2 | u16 | unknown |

## CMD `0x0270` `GAIN_EAT_OBJECT`

- 文档：`protocol/game/client-to-server/0x0270.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（conditional-fields），BodyLength = `20`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 1 | u8 | unknown |
| 9 | 2 | u16 | unknown |
| 11 | 4 | u32 | unknown |
| 15 | 4 | u32 | unknown |
| 19 | 1 | u8 | unknown |

## CMD `0x0293` `RAID_ENTRY_COST_INFO`

- 文档：`protocol/game/client-to-server/0x0293.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x0294` `RAID_MOVIE_SKIP`

- 文档：`protocol/game/client-to-server/0x0294.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

## CMD `0x0295` `SELECT_RAID_REWARD_CARD`

- 文档：`protocol/game/client-to-server/0x0295.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 1 | u8 | unknown |

## CMD `0x0296` `RAID_DO_BEHAVIOR`

- 文档：`protocol/game/client-to-server/0x0296.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `8`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |

## CMD `0x0297` `RAID_SET_SYMBOL`

- 文档：`protocol/game/client-to-server/0x0297.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `9`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 1 | u8 | unknown |

## CMD `0x0299` `CREATE_RAID`

- 文档：`protocol/game/client-to-server/0x0299.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（variable-width-primitive），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | ? | lenprefixed? | unknown |

## CMD `0x029A` `LEAVE_RAID`

- 文档：`protocol/game/client-to-server/0x029A.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x029B` `START_RAID`

- 文档：`protocol/game/client-to-server/0x029B.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`structure-unknown`（no-field-access-found），BodyLength = `0`
- **无字段表**：结构未知，本包不要为它编造字段。若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。

## CMD `0x029E` `RAID_MANAGER_WORK`

- 文档：`protocol/game/client-to-server/0x029E.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `12`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | 4 | u32 | unknown |
| 8 | 4 | u32 | unknown |

## CMD `0x029F` `MODIFY_RAID_INFO`

- 文档：`protocol/game/client-to-server/0x029F.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（variable-width-primitive），BodyLength = `未知`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 4 | u32 | unknown |
| 4 | ? | lenprefixed? | unknown |

## CMD `0x02A8` `LOAD_EXTEND_CHARACS`

- 文档：`protocol/game/client-to-server/0x02A8.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed`（—），BodyLength = `2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | u16 | unknown |

## CMD `0x02B7` `RAID_BUFF_SYSTEM`

- 文档：`protocol/game/client-to-server/0x02B7.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`closed-parametric`（counted-repeat-group），BodyLength = `3 + n×2`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |
| 1 | 2 | u16 | unknown |
| *重复组1 ×n* | | | |
| (组内 0) | 2 | u16 | unknown |

## CMD `0x02B8` `RAID_MONSTER_HP`

- 文档：`protocol/game/client-to-server/0x02B8.md`
- 上游文件：`Network/Protocol/GameProtocolHandler.cs`
- 提取状态：`partial`（builder-sites-disagree），BodyLength = `1`

| offset | size | enc | 当前含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | unknown |

