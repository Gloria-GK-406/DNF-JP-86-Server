# Routing Decision

- TaskContract: `Document/tasks/equipment-development/attempts/spec-001-reinforcement/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

普通强化的用户动作、目标结果、资产边界及与增幅/锻造的区分已明确。70、上游、A21、PVF 对照决定具体概率与材料规则；这些是子项内部可取证的设计输入，不改变其普通强化验收边界。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 子项只负责普通强化的一条玩家闭环。直接同时移植参考端业务会把协议、概率、消耗和破坏/保护的不同版本语义混淆，造成装备或资产错误；一份 Spec 与 Plan 能先固定 A21 证据和原子结算方案，再用完整结果复审和验证保护这条边界。该控制对用户后续集中实机验收有实际价值。
- Reroute conditions: 若 A21 实际请求证明普通强化与另一根任务结果不可独立接受，先回根控制器修订边界；若关键规则只能由实机确定且会决定破坏或扣费，停在该证据门而不猜测；若共享 Git 基线或宠物状态被外部改动，先核对所有权。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 70、上游、客户端及 PVF 的规则对照需要先导出有序方案；错误的状态或事务设计会同时影响成功、破坏与保护分支，Lite 的末端复审难以低成本修正。普通游戏装备可用隔离数据库验证与恢复，尚无 Governed 的高后果硬门槛。
