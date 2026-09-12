---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
phase: child_execution
phase_state: in_progress
active_attempt: spec-001
last_transition: child-created
transition_evidence: spec-001-party-settings-foundation has a ready contained child TaskContract, valid empty depends_on list, matching Single Spec routing decision, unique deterministic identity, and no prior active row; root task authorization covers its bounded outcome
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-mode-rules/.rks-agent/tasks/party-mode-rules
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/party-mode-rules
  branch: task/party-mode-rules
  baseline: d717f1b0538793c86f347fce784acaaa2eab3c49
  baseline_evidence: source main and task worktree both at d717f1b0538793c86f347fce784acaaa2eab3c49; canonical Git/common-dir identity and containment verified; only .rks-agent/tasks/party-mode-rules is untracked; dotnet build Server/86JP.Server.slnx -c Release --nologo passed with 0 warnings and 0 errors
---

# Multi-Spec Map

## Multi-Spec Boundary

- 初始共享模型与 `0x000C` 形成第一个可独立审查、接受和回退的能力基线。
- Normal、Story、Cycle 各自需要不同客户端证据和模式特有行为验收；一个模式失败不能撤销已接受的前序
  模式结果。
- 全部模式接受后的建模审计是独立结果：它可能仅确认模型，也可能在强证据下形成一项聚焦重构。

## Overall Direction

在同一个根 worktree 中串行积累：保守共享规则与严格 `0x000C` → Normal → Story → Cycle →
整体建模审计。共享连接保持为“协议 handler 调 Command，Command 调领域/规则服务，通知在行为成功后
投影”；具体字段和模式差异由每个子规格的最新证据决定，不在根图中预先固化。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 初始类型化模型与兼容默认普通行为 | not_started | none | 建模、实现、测试和构建证据 | aligned |
| SC-2 | 严格 `0x000C` 创建/编辑与无污染拒绝 | not_started | none | A21 形状证据、实现和状态测试 | aligned |
| SC-3 | 原子设置更新与正确通知投影 | not_started | none | 并发、代际、顺序与客户端消费证据 | aligned |
| SC-4 | Normal 模式闭环 | not_started | none | Normal 专属规则、抓包与 UI 验收 | aligned |
| SC-5 | Story 模式闭环 | not_started | none | Story 专属规则、抓包与 UI 验收 | aligned |
| SC-6 | Cycle 模式闭环 | not_started | none | Cycle 专属规则、抓包与 UI 验收 | aligned |
| SC-7 | 与现有组队生命周期组合无回归 | not_started | none | 每次接受后的回归与最终全量验证 | aligned |
| SC-8 | 协议资料与证据等级一致 | not_started | none | 逐规格同步和最终一致性审查 | aligned |
| SC-9 | 最终建模审计与必要的聚焦重构 | not_started | none | 汇总全部已接受模式证据后审计 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `./attempts/spec-001-party-settings-foundation/TaskContract.md` | 类型化队伍设置、保守共享规则、严格 `0x000C` 基础闭环与 type-1 投影 | `SC-1, SC-2, SC-3, SC-7, SC-8` | `running` | `pending` | `active` |
