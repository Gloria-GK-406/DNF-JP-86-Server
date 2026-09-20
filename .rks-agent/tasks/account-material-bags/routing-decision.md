# Routing Decision

- TaskContract: `.rks-agent/tasks/account-material-bags/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

目标、范围和可观察结果已经稳定：两类账号材料成为单一真源，分解按类型原子结算，宇宙灵魂保留角色材料语义，并完成协议投影和文档区分。执行方向已由现有账号晶体读模型、领域事务和分解流程提供，不需要补充用户决策。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 直接修改容易遗漏账号写入、角色槽清除、通知投影或事务回滚中的一项，形成两个真实源；Spec、显式计划和完整结果审查能将这些耦合边界一起约束。所有改动共同服务一个可验收结果，不能独立接受或回滚，因此使用一个 Single Spec。
- Reroute conditions: 若发现账号材料持久化必须跨越独立数据库或需要可单独上线的数据迁移，则重新评估为独立验收边界；若现有领域事务不能覆盖角色物品与账号资源，则先回到计划阶段重定方案。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 相比 Lite，显式 Plan 能固定持久化聚合、事务写入、协议投影和通知次序，避免早期所有权选择向后扩散；当前不涉及不可逆生产数据操作或安全/权限边界，普通完整结果审查足够，不需要 Governed 的独立挑战链。
