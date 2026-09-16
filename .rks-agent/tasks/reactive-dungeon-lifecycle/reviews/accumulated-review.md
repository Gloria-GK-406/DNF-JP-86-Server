---
kind: rks-review
verdict: approved
reviewed_at: 2026-09-16
baseline: 1677e4e2
---

# Accumulated Review

根级独立审查覆盖响应式拾取、剧情任务物品与 seeking Trigger、共享 Boss 清关、参与者结算/翻牌/离场四个已接受结果的组合。

首轮发现一个跨 Spec 阻断：最后任务物品已经提交 SQLite 背包、Trigger 与共享 ground change 后，清关重评仍位于拾取投影内部；picker replacement 或离场会先触发 transport 过滤并跳过权威 `ClearOpen`。修正后：

- `DungeonPickupPostCommitCoordinator` 在 CommandBus 返回后、任何网络过滤前重评清关，且使用 `CancellationToken.None`，连接生命周期不能取消已提交拾取后的权威推进。
- `DungeonRunDomainService.Find(DungeonRunId)` 返回当前 active participant view；picker 离场后仍可由 helper 推进同一 run。
- `DungeonClearDomainService` 对组队剧情继续使用冻结 leader 的任务条件，对 `TryOpenClear` 使用当前 active participant；单人正常路径仍使用 reporting session identity。
- 拾取与清关投影彼此独立过滤 frozen generation。旧 picker 与 replacement 不接收旧局拾取或清关帧，仍在线 helper 接收唯一 clear。
- 真实 SQLite 纵向测试覆盖正常、picker replaced、picker departed 三种时序，均验证 bag=15、Trigger=0、ClearOpen exactly-once。

SC-1～SC-7 组合边界均满足，未发现新的双真源、Session/connection 侵入领域模型、剧情规则污染普通地下城或旧 timer 投递问题。最终 verdict 为 `approved`。
