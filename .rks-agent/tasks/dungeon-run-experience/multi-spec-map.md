---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-003-clear-settlement-experience
last_transition: child-created
transition_evidence: >-
  child-sealed: spec-001 sealed completed at verification. child-accepted: prior baseline
  ff413e0e confirmed unchanged before the commit; one accepted-result commit 2d121cf9
  "Give every bonus the client draws its own line to be drawn on"; task worktree clean
  afterwards; source branch main still at ff413e0e. Independent review verdict approved
  (second round; the first returned changes_required on a test that passed a transposed
  table, and the remediation was re-reviewed). Fresh suite 1506 passed / 2 skipped / 0
  failed with PVF_ARCHIVE_PATH set to the A21 archive. root_evaluation at 2d121cf9:
  SC-2 satisfied, SC-1/SC-7/SC-8 in_progress, SC-3..SC-6 not_started, every constraint
  aligned -> gaps-remain. successor_decision: the next bounded outcome is the channel
  reward's missing rule, which nothing depends on and which must land before settlement
  can pay a channel bonus of its own -> safe-successor. child_modeling:
  spec-002 sealed completed and accepted as 3d22d384 (review approved, second pass confirmed
  after test strengthening; suite 1539/2/0). root_evaluation at 3d22d384: SC-2/SC-3/SC-4
  satisfied, SC-1/SC-7/SC-8 in_progress, SC-5/SC-6 not_started, constraints aligned ->
  gaps-remain. successor_decision: the remaining gap is the settlement, which pays nothing at
  all -> safe-successor. child_modeling: spec-003-clear-settlement-experience contract ready
  and contained, routing decision complete, no other active row.
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/dungeon-run-experience/.rks-agent/tasks/dungeon-run-experience
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/dungeon-run-experience
  branch: dungeon-run-experience
  baseline: 3d22d384
  baseline_evidence: >-
    3d22d384 "Pay the channel's reward only where the archive says the channel earns it",
    accepted from 2d121cf9; task worktree clean; suite 1539 passed / 2 skipped / 0 failed. Every test run
    needs PVF_ARCHIVE_PATH=/d/project/DNF/DNFClient-A21-protocol/Script.pvf -- without it
    ItemPathLiveTests fails for want of the archive, which reads as a regression and is not
    one. Dfo.Server.Host.Tests binds port 7001 and must not run while a host is up.
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
| SC-1 | 每一笔已支付的加成落在客户端为它单列的那一行，明细与总数自洽 | in_progress | 2d121cf9 逐种类落点；3d22d384 频道那一笔端到端 | 实机截图待三个成果齐了一次性验收 | aligned |
| SC-2 | 每个加成种类的槽位与叫法可追到实测出处，两个不自洽的槽被挡住 | satisfied | 2d121cf9；三元组逐条比对实测转写；九种变异实验 | 无 | aligned |
| SC-3 | 频道奖励只在本局副本属于该频道声明的副本组时支付，比率取自该行 | satisfied | 3d22d384；解析、归属、四个抓包样本端到端复现 | 实机截图待一次性验收 | aligned |
| SC-4 | 无声明、不在组内、读不到时不支付且不影响其余加成 | satisfied | 3d22d384；无行/五种残缺/重复声明/空目录各有测试，不可解析脚本下实机启动成功 | 无 | aligned |
| SC-5 | 通关结算经由同一发放件支付，跨等级线后果与击杀一致 | not_started | none | 结算完全不发经验 | aligned |
| SC-6 | 结算帧报出的数字与实际支付一致 | not_started | none | 274 个零字节 | aligned |
| SC-7 | 档案的数从档案读，服务端自己定的数仍可配置且缺省不变 | in_progress | ff413e0e 配置化；3d22d384 把频道比率退回档案侧 | 结算侧的档案表尚未接入 | aligned |
| SC-8 | 全套测试通过，构建零警告零错误 | in_progress | 3d22d384 处 1539 通过 / 2 跳过 / 0 失败，build 0/0 | 第三个成果重跑 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001-named-bonus-lines` | `attempts/spec-001-named-bonus-lines/TaskContract.md` | 通告为它支付的每一笔加成单列一行，两个不自洽的槽被挡住 | SC-1, SC-2, SC-8 | `sealed` | completed | `2d121cf9` |
| 2 | `spec-002-channel-reward-by-dungeon-group` | `attempts/spec-002-channel-reward-by-dungeon-group/TaskContract.md` | 频道奖励按档案声明的频道-副本组归属发放，比率不再是服务端配置 | SC-3, SC-4, SC-7, SC-8 | `sealed` | completed | `3d22d384` |
| 3 | `spec-003-clear-settlement-experience` | `attempts/spec-003-clear-settlement-experience/TaskContract.md` | 通关结算通过同一发放件支付经验，并在结算屏上如实报出 | SC-5, SC-6, SC-7, SC-8 | `running` | pending | `active` |
