---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan

1. RED：DungeonRun 共享 ClearOpen、revision 和幂等 change 测试。
2. GREEN：领域状态与 `DungeonClearDomainService` 统一评估。
3. RED：双参与者投影、离场/replacement 过滤测试。
4. GREEN：`DungeonClearProjectionDelivery`。
5. 将 MonsterDeath 直发清关帧改为“死亡响应完成后评估并投影”；GroundPickup 在事务成功投影后调用同一入口。
6. 增加 Boss-dead-then-final-pickup、ordinary、solo 回归，完成独立审查和 Release 验证。
