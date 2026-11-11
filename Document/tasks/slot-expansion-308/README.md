# 辅助装备／魔法石开槽任务（#308）

- 基线：`main` `c758121cd`；分支 `fix/308-slot-expansion`。
- 范围：完成任务 649/650 后开启辅助装备槽（左）、魔法石槽（右），并为已完成任务的老角色补开。
- 协议结论：[NOTI 0x0002 subtype 1](../../../protocol/game/server-to-client/NOTI-0x0002.md#2026-11-11-subtype-1-额外装备栏字节308)、
  [FINISH_QUEST 0x0022](../../../protocol/game/client-to-server/0x0022.md#2026-11-11-开槽任务649650的完成边界308)。

## 根因

1. `[slot expansion]` 解析为 `QuestRewardKind.SlotExpansion`，但完成流程不消费；`characters.ex_equip_slot_stat` 无写入。
2. subtype 1 把开槽字节写在 88B 属性块 offset 87，A21 读的是块后第一个字节，库里有值也只读到 0。

## 方案与职责

| 层 | 改动 |
|---|---|
| Data | `ExtraEquipmentSlots`（bit0 辅助装备、bit1 魔法石） |
| Abstractions | `QuestCompletionRewardPlan.ExtraEquipmentSlot`、`QuestRewardRejection.InvalidSlotExpansionReward`、`OpenExtraEquipmentSlotEffect`/`ExtraEquipmentSlotChange`、`ICharacterTable.OpenExtraEquipmentSlotsAsync` |
| Application | `QuestRewardRuleService` 校验唯一一条、值 ∈ {0,1}；`QuestCompletionEffectRuleService` 加 effect；handler 与登记；footprint 映射到 `CharacterAppearance`（重放时含 subtype 1） |
| Persistence | `UPDATE characters SET ex_equip_slot_stat = ex_equip_slot_stat | @slots`（未删除角色） |
| Protocol | subtype 1 字节移到属性块后；`GameFinishQuestCommand` 在开槽时于提交后补发 subtype 1（不广播，subtype 0 不含此字段）；FINISH_QUEST 保持 chain=0 |
| DbMigrate | 3→4 只补数据：已完成 649 → bit0，650 → bit1，按位或 |

不做：服务端穿戴校验（客户端自行锁定未开的槽，用户确认不需要）；chain=23（客户端无对应行为，且会丢掉 chain=0 携带的金币奖励条目）。

## 自动验证

- `ExtraEquipmentSlotWireTests`：subtype 1 在状态 0..3 时块后字节即状态值、块尾为 `100,0,0`、件数紧随。
- `QuestRewardRuleServiceTests`：0→辅助装备、1→魔法石、无奖励为 None；非法值、多值、两条奖励均拒绝。
- `QuestProtocolCommandFlowTests.Slot_expansion_…`：真实结算同事务置位（保留已有位），帧序 `22,25,2,15`，subtype 1 offset 116 为新状态。
- `ExplicitEffectExecutionTests`：新 effect 的重放需求为 `CharacterAppearance`。
- `SchemaVersionTests.Version_three_upgrade_…`：649/650/两者/已有位/未完成标记/无关任务六种角色。

全量（PVF 为 `DNFClient-A21-protocol/Script.pvf`）：HttpApi 146、Pvf 82、Infrastructure 1033、DbMigrate 26、
Persistence 457、Networking 214、Host 243 全部通过。Protocol 25 条、Application 26 条失败与未改动的
`main` `c758121cd` 在同一 PVF 下逐条相同，属既有问题，与本改动无关。不带 PVF 的 `Category=Unit`（CI 口径）全部通过。

## 用户测试环境

- 数据库：主干玩家库 `Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`（schema v0）经 SQLite backup API
  复制到 `artifacts/slot-expansion-308/db/`，再用 DbMigrate 0→4。原库未改动。
- 测试数据（只在副本上）：GM 官方邮件给角色 6 发 3033/3034/3035/3036 各 1000、3299×100；角色 6 预置 674、675 已完成，
  跳过两条讨伐任务；角色 8 预置「修复前已完成 649」后把版本退回 3，再真实运行 DbMigrate 3→4，得到 `ex_equip_slot_stat=1`。
- 服务：本分支 Debug 构建，`artifacts/slot-expansion-308/run-test-server.sh`；启动器 41080、频道 41001、游戏 41010/41011/41100。
- 客户端：`DNFClient-A21-protocol3`，`Launcher.ini` 的 BaseUrl 改为 41080（原文件备份为 `Launcher.ini.before-308`）。

## 用户测试步骤

账号 4。

1. 角色 8（Lv70）：进游戏打开装备栏。预期辅助装备槽已开、魔法石槽仍锁（迁移补位 + bit0=辅助装备）。
2. 角色 6（Lv86）：从邮箱领取材料。到任务 NPC 处，确认 649、650 都可接。
3. 接 649 并交付（扣 30 万金币与材料）。不重登，辅助装备槽立即解锁，能拖入一件辅助装备。
4. 接 650 并交付。不重登，魔法石槽立即解锁，能穿戴魔法石。
5. 回选人界面再进；再请求我重启服务端后进入。两槽保持开启，已穿的装备还在。
6. 角色 1123：两槽仍锁（未做任务的角色不受影响）。
7. （可选）双开 protocol2（同样改到 41080）查看角色 6 的详情，两槽显示一致。

## 用户测试结果（2026-11-11，ClientVerified）

- 主流程：用户在 1123（账号 4，Lv86）上实际走完整条链 674 → 649 → 676 → 675 → 650，服务日志无错误。
  `ex_equip_slot_stat` 由 0 变为 1，再变为 3。左右槽都在交付当场解锁，并能穿戴。用户报告重新登录后保持开启。
- 老角色补开：在战斗法师（角色 6）上写入旧服务端会留下的状态（649、650 已完成，开槽位为 0），把版本退回 3 后，
  真实运行 DbMigrate 3→4，结果为 3，其他角色未变，`quick_check` 正常。重启服务端后，用户登录确认左右槽都已开启。
- 未实测：角色 8 只补 bit0 的情形（左开右锁）、另一客户端查看详情（subtype 3）、服务端重启后 1123 的状态。
  这些只有自动测试或已有的 subtype 3 ClientVerified 覆盖。
- 服务日志：`artifacts/slot-expansion-308/test-host-run1.log`（任务链）、`test-host.log`（迁移后登录），都不提交。
