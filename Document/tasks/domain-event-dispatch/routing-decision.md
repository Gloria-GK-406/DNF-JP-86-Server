# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户明确授权全面重构和独立检查，已接受类型化事件、提交后一对多分发及异步唤醒方向。即时协议投影不能重放，作为事件服务内独立投递路径，属于保持既有行为的实现选择。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 单一验收边界是统一发布入口与职责迁移。遗漏某个直接通知调用或将 CMD/会话投影后台重放会破坏真实客户端；Spec 的结构对照、逐类迁移和独立结果审查能直接降低这一风险，符合用户要求。
- Reroute conditions: 若出现需要外部中间件或独立跨进程部署/验收的事实，重新评估；称号业务规则不扩入本任务。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 必须先固定可靠事件/即时投影两种消费边界以及事务接缝，再迁移协议调用，否则错误早期接口将传播到所有 handler。显式计划避免后期大面积返工；普通独立审查与数据库故障/回滚测试足以保护本地可逆实现，无需额外人工审批。

## Workspace

- Root: `E:/Project/DNF/DNF-JP-86-Server/.worktrees/domain-event-dispatch`
- Branch: `task/domain-event-dispatch`
- Accepted baseline: `ef00741dc3d4ee826c14b3d273042f38c3a1c31f`
- Source branch: `task/abyss-research`，其基线相同；源未提交内容不搬移。
- Evidence: Git/common dir 分离，初始 status 空；主仓 .worktrees 已 ignore。原生工作区工具返回 Not a git repository，使用主仓 git worktree add 创建，无嵌套工作树。
