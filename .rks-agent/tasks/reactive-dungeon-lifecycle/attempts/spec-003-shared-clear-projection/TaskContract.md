---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-3
  - SC-6
  - SC-7
depends_on:
  - ../spec-002-quest-item-seeking/spec-result.md
---

# TaskContract: 共享 Boss 清关变更与投影

## Context

Boss 死亡路径只在死亡提交后即时检查一次清关条件。剧情任务物品由 Boss 或最后一只怪掉落时，该检查发生在拾取之前；随后拾取将 seeking Trigger 归零，却没有再次评估清关，因此玩家会留在已清空 Boss 房且没有结算入口。

## Goal

把“Boss 房已清空且冻结剧情条件满足”建模为 DungeonRun 的共享、幂等清关事实；怪物死亡和权威拾取只是触发同一个领域评估入口，协议层按领域 change 投影给所有当前有效参与者。

## Scope

### In

- DungeonRun 持有共享 ClearOpen 状态、单调 runtime revision 和语义 recipients。
- 领域清关评估统一检查 Boss 房、房间怪物死亡事实和冻结队长的剧情完成条件。
- MonsterDeath 与 GroundPickup 成功提交后调用同一评估入口。
- 清关投影统一发送 `ENABLE_CLEAR_DUNGEON` 与秘密商店帧，并过滤离场、旧 generation 和 replacement connection。
- 普通地下城与单人剧情地下城继续使用同一清关评估，不继承剧情任务门槛。

### Out

- 不在本 Spec 重构参与者结算、牌面计时、自动翻牌或回城；这些消费 ClearOpen 前置的流程属于后续 Spec。
- 不处理副本内重连或发送失败后的恢复投递。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| C3-SC-1 | Boss 死亡时条件已满足，ClearOpen 只提交一次并向全部当前参与者投影 | 双人死亡、重复报告与 revision 测试 |
| C3-SC-2 | Boss 死亡时 seeking 未完成不清关；最后任务物品拾取并提交后重新评估并清关 | pickup-after-boss 集成测试 |
| C3-SC-3 | participant 离场、旧 generation 和 replacement connection 不接收旧局清关投影 | 投影接收者测试 |
| C3-SC-4 | 单人剧情与 ordinary Boss 清关保持 | 显式回归测试 |
| C3-SC-5 | Release 构建及受影响完整测试通过 | 构建与测试证据 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根任务授权：“其余的可以按你说的处理。你来进行修改重构吧” | 在隔离工作区实现、验证并提交；不含合并、部署或清理 |
