---
kind: rks-spec-result
decision: pass
spec: ./spec.md
review: ./reviews/change-review.md
---

# Spec Result: 响应式地下城入口可见性

| Acceptance | Result | Evidence |
|---|---|---|
| AC-001 | Pass | `DungeonRunDomainServiceTests` 验证双成员一次性进入同一 run、首房间、入口修订和语义接收者集合。 |
| AC-002 | Pass | 领域测试覆盖两种就绪顺序、幂等重复、错误 run/修订、非成员、离开与替换。 |
| AC-003 | Pass | 入口和加载投影测试覆盖冻结 session generation、投影前替换、CMD 期间替换与旧变化读取最新修订。 |
| AC-004 | Pass | 队伍入口测试逐接收者断言成员顺序、WireUserId、队伍槽位、共享房间内容、接收者专属 `participantSlot` 及 `START_MAP` 前置帧序。 |
| AC-005 | Pass | 加载协调器测试证明仅向当前提交者发送 `LoadingComplete`，且领域快照准确记录 ready participants。 |
| AC-006 | Pass | `SelectDungeonHandler` 与 `FinishLoadingHandler` 均委托共享协调器，不再实现成员选择和扇出循环。 |
| AC-007 | Pass | 测试覆盖映射失败、发送失败、精确回滚、连接隔离、较新修订保留和城镇成员不提前移除。 |
| AC-008 | Pass | 单人、普通队伍、剧情入口及完整解决方案回归通过，未迁移换房、战斗、掉落、Boss 或翻牌行为。 |

结论：Spec 已满足，候选结果达到双客户端实机验收条件；客户端可见性仍等待部署后的实机确认。
