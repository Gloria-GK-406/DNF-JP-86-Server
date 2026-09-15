# Routing Decision

- TaskContract: `.rks-agent/tasks/reactive-dungeon-entry-visibility/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

目标、第一步范围、排除项和自动化/实机验收边界均已稳定；现有共享地下城聚合、城镇响应式投影实现和已验证上游入口链路提供了可执行方向，不需要补充用户决策。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求任务契约并强调逐步验收。若直接实现，最可信的遗漏是只修复某次入口封包，却没有把加载状态、语义接收者、会话代际和延迟修订统一到响应式边界，导致后续同步功能再次出现多个通知真源。一个持久 Spec、显式依赖计划和完整结果独立审查能够在入口骨架阶段阻止该偏移，其时间成本相对于重做地下城通知链是值得的；所有变化共同服务一个“响应式入口可见”验收结果，不存在可独立接受或回滚的子结果。
- Reroute conditions: 若实现证据证明入口可见必须同时修改客户端补丁、P2P 协议族或纳入战斗/结算状态，任务语义与授权范围将发生变化，应停止并重新对齐；若计划发现可见性只需一个局部映射修复且响应式边界无需变化，可降级为 focused delivery。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: Lite 的最终审查不足以提前固定实例变化、加载阶段、会话解析与封包顺序之间的依赖；Standard 的短计划可以在编码前确定迁移顺序和回归路径。当前没有安全、权限、数据损失或不可逆外部操作风险，普通独立结果审查与新鲜验证足够，不需要 Governed 的独立挑战和更强审计控制。
