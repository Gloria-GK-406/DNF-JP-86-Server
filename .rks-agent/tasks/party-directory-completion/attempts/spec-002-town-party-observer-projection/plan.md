---
kind: rks-plan
contract:
  path: ./TaskContract.md
  revision: 1
spec: ./spec.md
mode: task-units
state: ready
---

# Plan: town-visible 非成员 party 投影

## Task Units

| ID | Outcome | Dependencies | Primary checks |
|---|---|---|---|
| TASK-01 | 领域结果冻结稳定 exact observer union | none | Application RED/GREEN: create/update/accept/leave/disband + scope/replacement |
| TASK-02 | 共享 generation stream 投递 member/observer refresh-clear 并修复 commit-on-failure | TASK-01 | Protocol RED/GREEN: sequence, dedupe, clear-before-refresh, failure/revision |
| TASK-03 | select/move 的 `0x0002 -> 0x0009` current-truth read-repair | TASK-02 | handler RED/GREEN: newcomer/existing/mover, dedupe, current revision, no-party |
| TASK-04 | 组合回归、协议资料与 runtime handoff | TASK-03 | focused/full tests, Release build, static boundary, independent review |

## Implementation Direction

- 在 Data plan 末尾增加不可变 `TownObservers`，由 `PartyDomainService` 在 lock 内从 member/clear anchors 的
  `VisibleTownArea` union 捕获。
- 将 party projection stream 抽成唯一共享 singleton；成员 mutation 和 town entry read-repair必须使用同一
  generation gate。成员 audience 改为 `TheseSessions`。
- 新 observer notification 只包装现有 `PartyRosterMessage.Refresh/Clear`；不复制 encoder。
- read-repair 输入是 exact recipients + represented exact sessions，在 gate 内 requery/dedupe/sort。
- SelectCharacter 与 SetUserArea 在既有 town batch/publication成功后调用 read-repair，保持 CNUser 先到。

## Verification Strategy

- 每个行为先单测 RED，记录命令、exit 与 decisive assertion，再最小 GREEN。
- 每个 Task 后跑对应 party/town regression；TASK-04 跑 Release solution、全量 TRX 逐项统计、分层检查、
  `git diff --check` 和 `rg 0x023C` 生产增量审计。
- 完整结果独立 review；修复后再次 review，最后 fresh completion verification。
