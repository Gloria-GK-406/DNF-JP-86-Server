---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 2
phase: child_execution
phase_state: pending
active_attempt: none
last_transition: child-accepted
transition_evidence: spec-001 independent review approved after RunId, recipient and connection-generation remediation; affected tests and Release build passed; accepted result is ready to commit before selecting spec-002
workspace:
  repository_root: E:\Project\DNF\DNF-JP-86-Server
  task_root: E:\Project\DNF\DNF-JP-86-Server\.worktrees\reactive-dungeon-lifecycle\.rks-agent\tasks\reactive-dungeon-lifecycle
  code_root: E:\Project\DNF\DNF-JP-86-Server\.worktrees\reactive-dungeon-lifecycle
  branch: task/reactive-dungeon-lifecycle
  baseline: 1677e4e28b289ed0a49d091cb5a2bde935fa15c3
  baseline_evidence: HEAD and source main both 1677e4e28b289ed0a49d091cb5a2bde935fa15c3; branch task/reactive-dungeon-lifecycle; linked git-dir proven; dotnet restore succeeded; dotnet build Server/86JP.Server.slnx -c Release --no-restore succeeded with 0 warnings and 0 errors
---

# Multi-Spec Map

## Multi-Spec Boundary

- 远端拾取表现与运行期共享变更、剧情任务物品与 derived Trigger、结算翻牌与独立回城分别具有独立验收、复审和回滚边界；后一结果依赖前一结果提供的统一地下城变更语言，但任何未通过的后续结果不应推翻已接受的较早结果。

## Overall Direction

先建立不改变 P2P 移动的运行期地下城变更与拾取投影，再接入权威任务物品/背包派生进度，最后让 Boss 清关、参与者结算、牌面计时、翻牌和回城共享同一实例变更与接收者模型。共享事实由 DungeonRun 实例持有，参与者私有进度保存在该实例的参与者状态，连接代次和 wire 编码只存在于协议投影边界。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 拾取权威变更唯一，且不与客户端 P2P 动作形成第二真源 | done | spec-001：单一 `DungeonPickupChange`、逐接收者投影、A21 0x0027/P2P 责任边界、旧会话过滤；独立复审 approved | 实机确认共享消失维持现状；远端动作仍由客户端 P2P 负责 | aligned |
| SC-2 | 任务 1779 怪物任务物品与 seeking Trigger 形成运行期闭环 | not_started | none | PVF 来源、获取、背包派生 Trigger 与在线投影 | aligned |
| SC-3 | 共享 Boss 清关一次并向全部有效参与者开放结算 | not_started | none | 实例清关变更、幂等与接收者投影 | aligned |
| SC-4 | 参与者独立结算、牌面计时与幂等翻牌 | not_started | none | 参与者阶段、自动/手动竞态、队伍翻牌投影 | aligned |
| SC-5 | 独立回城且旧局通知不能到离开或替换连接 | not_started | none | 离场状态、接收者收缩、过期计时过滤 | aligned |
| SC-6 | 单人剧情与显式普通地下城回归保持 | not_started | none | 定向与综合回归 | aligned |
| SC-7 | Release 构建和测试无新增失败 | not_started | none | 各子结果及根级完整验证 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001` | `attempts/spec-001-reactive-pickup-projection/TaskContract.md` | 响应式拾取投影与远端拾取表现 | `SC-1, SC-3, SC-5` | `accepted` | `attempts/spec-001-reactive-pickup-projection/spec-result.md` | `pending commit` |
