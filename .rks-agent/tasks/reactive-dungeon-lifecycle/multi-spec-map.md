---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 2
phase: accumulated_review
phase_state: running
active_attempt: none
last_transition: child-accepted
transition_evidence: spec-004 independent remediation review approved after durable card commit, prepared/leave, bounded auto-free and controllable timing findings were closed
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
| SC-2 | 任务 1779 怪物任务物品与 seeking Trigger 形成运行期闭环 | done | spec-002：真实 PVF 七元组、QuestLinked owned drop、owner-only pickup、SQLite 原子 bag/Trigger、commit 后共享 change 与当前连接投影；独立复审 approved | 实机确认任务物品数量与客户端任务栏表现 | aligned |
| SC-3 | 共享 Boss 清关一次并向全部有效参与者开放结算 | done | spec-003：共享 ClearOpen/revision、统一 death/pickup evaluator、冻结 leader 规则、generation-safe 投影、真实 SQLite 最后任务物品纵向测试；独立复审 approved | 实机确认双端同时进入结算 | aligned |
| SC-4 | 参与者独立结算、牌面计时与幂等翻牌 | done | spec-004：参与者结算、共享 seat 快照、durable commit 后发布、2s/4s 可控计时、手动/自动/瞬态列竞争有界幂等；独立复审 approved | 双客户端实机确认结算与翻牌表现 | aligned |
| SC-5 | 独立回城且旧局通知不能到离开或替换连接 | done | spec-004：EPLP/GiveUp 只结束发起者；recipients 收缩；离场/replacement/new run 取消或过滤旧 timer | 双客户端实机确认先后回城 | aligned |
| SC-6 | 单人剧情与显式普通地下城回归保持 | done | spec-002～004 定向与综合回归全部通过 | 实机抽样 | aligned |
| SC-7 | Release 构建和测试无新增失败 | done | Release 0 warnings/0 errors；Application 1488、Protocol 924；顺序全项目合计 3334 passed、2 intentional skipped | 根级 accumulated review | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001` | `attempts/spec-001-reactive-pickup-projection/TaskContract.md` | 响应式拾取投影与远端拾取表现 | `SC-1, SC-3, SC-5` | `accepted` | `attempts/spec-001-reactive-pickup-projection/spec-result.md` | `38fbceff` |
| 2 | `spec-002` | `attempts/spec-002-quest-item-seeking/TaskContract.md` | 剧情任务物品与 seeking 派生进度 | `SC-2, SC-6, SC-7` | `accepted` | `attempts/spec-002-quest-item-seeking/spec-result.md` | `a7687ae0` |
| 3 | `spec-003` | `attempts/spec-003-shared-clear-projection/TaskContract.md` | 共享 Boss 清关变更与投影 | `SC-3, SC-6, SC-7` | `accepted` | `attempts/spec-003-shared-clear-projection/spec-result.md` | `199ed9c8` |
| 4 | `spec-004` | `attempts/spec-004-participant-settlement-cards-exit/TaskContract.md` | 参与者结算、共享牌面与独立离场 | `SC-4, SC-5, SC-6, SC-7` | `accepted` | `attempts/spec-004-participant-settlement-cards-exit/spec-result.md` | `pending` |
