---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: completed
active_attempt: null
last_transition: all-children-completed
transition_evidence: 'spec-001 已封存为 completed：变更评审 approved 且无未解决阻塞发现，新鲜构建 0 警告、新鲜全量 1037 通过 / 2 跳过 / 0 失败（满载一处已知假红经隔离复跑 4/4）。SC-1..SC-5、SC-7 由 verification/completion-evidence.md 逐条举证。结果封存于 attempts/spec-001-.../spec-result.md，代码未提交，工作树保持在基线 ea95d49 之上。'
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-orchestrates-table-repositories/.rks-agent/tasks/domain-orchestrates-table-repositories
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-orchestrates-table-repositories
  branch: domain-orchestrates-table-repositories
  baseline: ea95d491fd9d706bf4f7bc7e1b465040081223ef
  baseline_evidence: 'dotnet test Server/86JP.Server.slnx -m:1 于 ea95d49：1011 通过 / 2 跳过 / 1 满载假红（ChannelHostStartupTests.InvalidRequiredChannelScript unreadable，TaskCanceledException 于 Process.WaitForExitAsync 10 秒预算）。按 AGENTS.md 记载的判据隔离复跑 Dfo.Server.Host.Tests：4/4 通过，8 秒。故基线为 1011 通过 / 2 跳过 / 0 失败。工作树 D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-orchestrates-table-repositories，分支 domain-orchestrates-table-repositories，HEAD ea95d49，status 仅含未跟踪任务工件目录；源工作树 main 保持 ea95d49 且 status 干净；工件迁移经 sha256 校验一致。'
---

# Multi-Spec Map

## Multi-Spec Boundary

用户给出两个各自可接受的结果，且它们的接受边界确实独立。

**其一，九个仓储按表拆分并接入环境连接。** 交付后即是完整可用状态——item 路径原样保留，系统照常运行，回滚只需还原仓储与领域编排。

**其二，item 载体收敛与无用构造退役。** 它消费前者留下的环境连接，但前者的成果不因它失败而作废：一个删的是容器与工厂，一个改的是仓储与编排，改动路径不重叠，可各自接受也可各自回滚。

单一 Spec 装不下的原因不是改动大，而是**中间那个状态本身值得被接受**。九个仓储接入环境连接之后系统是完整的，此时停下来交付是有意义的；把退役并进同一个接受边界，等于让一个已经可用的成果去等一个与它无关的删除动作。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 每个仓储的读写不越出单张表 | done | 65 个持久化类逐类清点，提及 >1 张表的为 0（verification/completion-evidence.md） | 无 | aligned |
| SC-2 | 持久化实现不自开连接 | done | `new SqliteConnection` 恰 3 处，58 个表仓储中 0 处（verification/completion-evidence.md） | 无 | aligned |
| SC-3 | 一次用例内的多次读看到同一份快照 | done | UseCaseTransactionCoversReadsTests 7 条（verification/completion-evidence.md） | 无 | aligned |
| SC-4 | 一次用例内的多处改动全有或全无 | done | OneUseCaseSettlesEveryTableTests 3 条（verification/completion-evidence.md） | 无 | aligned |
| SC-5 | 跨表组装归领域层且只有一处 | done | 三个入场记录各一处构造，全在 Application（verification/completion-evidence.md） | 无 | aligned |
| SC-6 | 已确认无生产调用方的内存载体构造已退役 | done | 四个名字在 `Server/src/` 零命中；两处 DI 注册已删；退役前逐个证明无生产调用方（spec-002 verification 第三节）| 无 | aligned |
| SC-7 | 对外可观察行为一字未变 | done | 两次接受各自重证：spec-001 处三份整记录金样本原样通过、协议 203/203；spec-002 处协议 203/203 且相关文件字节字面量与基线逐个一致 | 无 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-table-repositories-on-ambient-connection/TaskContract.md` | 九个仓储退回单表读写并接入用例边界；跨表组装移入领域层且唯一 | SC-1, SC-2, SC-3, SC-4, SC-5, SC-7 | `completed` | `attempts/spec-001-table-repositories-on-ambient-connection/spec-result.md` | `uncommitted` |
| `2` | `spec-002` | `attempts/spec-002-item-carrier-convergence/TaskContract.md` | item 载体收敛，已确认无生产调用方的内存构造退役 | SC-6, SC-7 | `completed` | `attempts/spec-002-item-carrier-convergence/spec-result.md` | `uncommitted` |
