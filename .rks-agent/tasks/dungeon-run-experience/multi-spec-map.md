---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-001-named-bonus-lines
last_transition: child-created
transition_evidence: >-
  root_evaluation: SC-1..SC-6 not_started, SC-7 in_progress, SC-8 not_started, every
  constraint aligned -> gaps-remain. successor_decision: the smallest stable outcome is
  the notice's own named lines, which nothing else depends on and which everything else
  will report through -> safe-successor. child_modeling: spec-001-named-bonus-lines
  contract ready and contained, routing decision complete, no other active row.
  Workspace evidence: git worktree add .worktrees/dungeon-run-experience -b dungeon-run-experience ff413e0e;
  in-worktree git rev-parse --show-toplevel =
  D:/project/DNF/DNF-JP-86-Server/.worktrees/dungeon-run-experience;
  git branch --show-current = dungeon-run-experience;
  git rev-parse HEAD = ff413e0e; git status --short = only the untracked task folder;
  source checkout main still at ff413e0e with a clean tree.
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/dungeon-run-experience/.rks-agent/tasks/dungeon-run-experience
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/dungeon-run-experience
  branch: dungeon-run-experience
  baseline: ff413e0eb1fa62df7bf337ec23fd286e3a4bd4fe
  baseline_evidence: >-
    ff413e0e "Put the dials this server invented into a file an operator can edit";
    source tree clean at creation; suite at that commit 1501 passed / 2 skipped / 0 failed
    (Dfo.Server.Host.Tests must run alone -- it binds port 7001 and collides with a running host).
---

# Multi-Spec Map

## Multi-Spec Boundary

三个成果各自独立可验收、独立可回滚：

1. **具名加成行。** 判据是通告明细自洽。单独交付即有价值——帧不再把付出去的加成算进基础值。
2. **频道奖励按归属。** 判据是组内付、组外不付。与 (1) 无依赖：它要用的槽已经存在。
3. **通关结算发经验。** 最大的一块，且带一个真实的阻塞风险：它依赖两张尚未证实能取得的表（`[clear rank exp bonusrate]`，以及结算基数是否真与交任务共用 `[exp reward table]`）。若证据不足，它应当能单独阻塞而不拖掉前两个已经验收的成果。

这个边界不是按文件数或工时切的，是按「哪一个失败了不该连累另外两个」切的。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 每一笔已支付的加成落在客户端为它单列的那一行，明细与总数自洽 | not_started | none | 只有 `SeriaBlessing` 一种加成能被申报，其余进 `Unlisted` | aligned |
| SC-2 | 每个加成种类的槽位与叫法可追到实测出处，两个不自洽的槽被挡住 | not_started | none | `ExperienceBonusKind` 只有两个成员，无阻挡 | aligned |
| SC-3 | 频道奖励只在本局副本属于该频道声明的副本组时支付，比率取自该行 | not_started | none | 现为固定 0.05，无归属判断 | aligned |
| SC-4 | 无声明、不在组内、读不到时不支付且不影响其余加成 | not_started | none | 同上 | aligned |
| SC-5 | 通关结算经由同一发放件支付，跨等级线后果与击杀一致 | not_started | none | 结算完全不发经验 | aligned |
| SC-6 | 结算帧报出的数字与实际支付一致 | not_started | none | 274 个零字节 | aligned |
| SC-7 | 档案的数从档案读，服务端自己定的数仍可配置且缺省不变 | in_progress | ff413e0e 已把服务端自己定的数配置化 | 频道比率现在错在配置侧，应回到档案侧 | aligned |
| SC-8 | 全套测试通过，构建零警告零错误 | not_started | none | 尚未开工 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001-named-bonus-lines` | `attempts/spec-001-named-bonus-lines/TaskContract.md` | 通告为它支付的每一笔加成单列一行，两个不自洽的槽被挡住 | SC-1, SC-2, SC-8 | `running` | pending | `active` |
