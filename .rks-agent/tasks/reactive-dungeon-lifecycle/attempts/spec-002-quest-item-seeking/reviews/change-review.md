---
kind: rks-review
verdict: approved
reviewed_at: 2026-09-16
---

# Change Review

独立审查首轮发现并阻断了两个问题：共享地面消耗早于 SQLite/CAS 提交，以及 owned drop 在 delivery registry 缺项时错误退化为 reporter owner。

修正后复审通过：

- `DungeonRunDomainService` 将地面消耗建模为 commit-infallible transaction participant；SQLite 是唯一可失败 commit，失败时释放 claim，地面物仍存在。
- 真实 CommandBus + SQLite CAS 失配测试证明 Trigger、背包 slot 和共享地面均未提交。
- owned drop 直接由 `OwnerCharacterId` 稳定转换为 wire user ID；leader 已移出 delivery registry 时仍保持正确归属。
- 普通地下城、不匹配目标和 replacement connection 边界均保持。

Boss 最后一件任务物品在死亡后拾取所需的清关重评属于下一 Spec，不阻断本结果。
