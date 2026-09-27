# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

用户已确定单进程组件分层和共用持久化，不存在进程间状态设计问题。入口兼容与就绪/启停可用真实 Host 检查。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 一个集成验收边界。最容易遗漏的是 HTTP 自建第二个容器和预热迁移后入口过早开放；明确生命周期计划及独立结果审查能在迁移早期约束这些问题，符合用户要求的细致契约。
- Reroute conditions: 若需要改变认证/票据规则或多进程，返回意图与契约，不在重构中悄悄实施。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 必须先确定同一 Generic Host 的 HTTP 集成与预热顺序，才能迁移入口和测试；Lite 无计划容易让入口/就绪决定相互冲突。仅搬迁现有认证装配、不改授权边界，已有端到端测试和新增生命周期检查加独立审查足够，不需要 Governed 的额外挑战门。

## Workspace

- Root: `E:/Project/DNF/DNF-JP-86-Server/.worktrees/host-component-composition`
- Branch: `task/host-component-composition`
- Baseline: `d32ecfc9b4228a658c01efdfa36b3d41b23aba78`
- 原生 worktree 工具在对话根 E:/Project/DNF 返回 Not a git repository，改用 git worktree；路径已由 check-ignore 确认忽略。初始 HEAD 与基线相同，初始工作区干净。主干不变。
