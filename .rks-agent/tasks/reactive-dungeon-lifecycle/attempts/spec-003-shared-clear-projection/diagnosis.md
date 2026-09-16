---
kind: rks-diagnosis
state: ready
---

# Diagnosis: Boss 清关检查只有死亡时刻

- `MonsterDeathHandler.ClearedAsync` 在 actor death 提交后检查 `IsCurrentRoomClearedAsync` 与 `DungeonSettlementRuleService.ForAsync`，然后由 Handler 直接拼两帧。
- `PickUpGroundDropCommand` 在事务内把最后任务物品入包并将 seeking Trigger 归零，但事务提交后没有清关评估。
- `BossDieCheckHandler` 明确只记录客户端报告，不改变状态，不能作为服务端权威补偿入口。
- DungeonRun 当前有共享 actor death、ground drop 和 participant settlement，却没有共享 ClearOpen 状态；因此重复通知、接收者和后续结算没有统一前置事实。

支持的根因是清关仍是 MonsterDeath Handler 的瞬时分支，而不是 DungeonRun 可由多个权威变化触发的一次性状态转换。
