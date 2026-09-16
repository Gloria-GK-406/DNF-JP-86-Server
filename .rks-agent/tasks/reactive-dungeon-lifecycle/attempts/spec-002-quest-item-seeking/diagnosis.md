---
kind: rks-diagnosis
state: ready
---

# Diagnosis: 剧情任务物品与 seeking 派生进度

## Observed Evidence

- A21 `Script.pvf` 的任务 1779 指向地下城 145，目标类型为 `seeking`，`[int data]` 是 `10149163 15`。
- 同一任务的 `[monster reward item]` 为两个七元组：`65601 145 -1 10149163 1 100 15` 与 `65305 145 -1 10149163 1 100 15`。
- 上游 `QuestFile` 将七元组解释为 MonsterCode、DungeonId、Difficulty、ItemId、Count、DropRate、MaxStack；当前 `PvfQuestDefinitionSource` 完全没有读取该区段。
- 当前 `SelectCharacterQuestEntryService` 能按背包投影 seeking Trigger，但只在角色选择后运行；`PickUpGroundDropCommand` 成功入包后直接提交地面占用，不触发该派生逻辑。
- `DungeonSettlementRuleService` 已按 QuestLinked 冻结任务日志决定是否允许清关，因此缺失任务掉落/运行期 Trigger 会表现为 Boss 房清空却没有结算。

## Supported Cause

缺陷不是 Boss 协议首先失效，而是任务内容与运行期背包派生链缺了两段：PVF 任务物品来源没有进入规则层；拾取后的权威背包变化没有驱动 seeking Trigger。于是任务 1779 永远无法通过正常战斗到达 Trigger 0，现有剧情清关门槛正确地拒绝结算。

## Regression Target

在 QuestLinked 1779 中杀死 65601/65305 时产生归属冻结队长的 10149163；队长拾取后在同一命令事务内更新背包与 Trigger，15 个时归零并投影。显式普通地下城、非目标怪和非归属成员保持无此效果。
