---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
phase: child_execution
phase_state: in_progress
active_attempt: spec-002
last_transition: child-created
transition_evidence: ./attempts/spec-002-administrator-access/activation.md
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin/Document/tasks/gm-web-admin
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin
  branch: feature/gm-web-admin
  baseline: 0630348d2bace7a35887b43b8aaa0c145b0b66a2
  baseline_evidence: ./workspace.md
---

# Multi-Spec Map

## Multi-Spec Boundary

启动骨架、认证、查询、邮件、点券分别可以独立审查、验证和提交，所有子任务串行共用一个工作区。

## Overall Direction

按项目与启动骨架 → 管理员和审计 → 服务/账号/角色查询 → 官方邮件 → 账号点券发放 → 集成收尾推进。只创建当前最小稳定子合同，后续具体设计从已验收结果继续收敛。

## Controller Cursor

按 multi-spec-workflow 的 transition/validation 引用执行；每次激活或恢复检查唯一 active child、合同与依赖链接、HEAD 基线和路径包含关系。

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 模块启动和前端发布 | in_progress | spec-001，0630348d | 最终管理配置集成 | aligned |
| SC-2 | 管理员权限与追踪 | not_started | none | 完整能力 | aligned |
| SC-3 | 查询和在线状态 | not_started | none | 完整能力 | aligned |
| SC-4 | 官方邮件 | not_started | none | 完整能力 | aligned |
| SC-5 | 点券发放 | not_started | none | 完整能力 | aligned |
| SC-6 | 集成和验收材料 | in_progress | spec-001 运行说明与发布 smoke | 标准 CI、完整回归和最终验收表 | aligned |

## Spec Attempts

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | spec-001 | attempts/spec-001-project-foundation/TaskContract.md | 项目与启动骨架 | SC-1, SC-6 | completed | attempts/spec-001-project-foundation/spec-result.md | 0630348d |
| 2 | spec-002 | attempts/spec-002-administrator-access/TaskContract.md | 管理员认证与审计 | SC-2, SC-6 | running | pending | active |
