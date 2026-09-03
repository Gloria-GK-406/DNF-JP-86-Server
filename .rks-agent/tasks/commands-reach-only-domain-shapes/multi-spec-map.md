---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-002
last_transition: child-created
transition_evidence: >-
  根评估（基线 70a3e54e）：SC-1/2/3/4/7 已满足，SC-8/SC-9 进行中，**SC-5 与 SC-6 仍未开始**，
  五项约束全部 aligned，故 gaps-remain。后继决策：契约 task 行覆盖本轮，无需扩权；当前缺口里
  最小且自足的有界成果是 SC-5 的前一半——把暴露成可注入接缝的会话与在场内存容器改说领域话，
  并顺带结掉 spec-001 明写为「必须在后续成果收口」的那层透传壳。它可独立接受或回滚，不依赖
  未接受的兄弟结果，故 safe-successor。标识与路径按确定规则算出（下一个编号 2，无沿用）：
  attempts/spec-002-containers-speak-domain-verbs/，已证明包含在 task_root 之内；子契约 ready，
  depends_on 为空列表；子路由决策四节齐全（controller 取 sdd-lite，理由见其中）。激活前校验
  无错，此前无活跃子成果、无 running 行。
  上一次转移的证据（child-accepted）：
  spec-001 交回 spec-result.md，outcome completed，terminal_stage verification。独立评审
  reviews/change-review.md verdict approved（第一轮 changes_required 的两条阻断已整改并经第二轮
  复核关闭；评审者自建反射探针、自做三次生产代码变异、自行逐文件重算用例数对账，并逐字符比对
  两轮 git diff 证明整改零生产代码改动）。新鲜验证 verification/completion-evidence.md decision
  pass：构建 0 警告 0 错误，全量 2007 通过 / 2 跳过 / 0 失败。接受前证明：HEAD 仍等于先前基线
  9c0de6c6，变更集 81 项全在 Server/ 与 AGENTS.md 之下加本任务产物目录，无 CRLF 污染，Spec 明列
  的 Non-Goals 逐条确认未被触碰。以一次提交接受：70a3e54e，提交后工作区干净、源分支 main 的 tip
  仍为 9c0de6c6、任务分支只前进一步。基线随之推进。
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/commands-reach-only-domain-shapes/.rks-agent/tasks/commands-reach-only-domain-shapes
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/commands-reach-only-domain-shapes
  branch: commands-reach-only-domain-shapes
  baseline: 70a3e54e10faccd65ca522f4e22af37355f88f06
  baseline_evidence: >-
    在任务工作区内跑
    `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`：
    2015 通过 / 2 跳过 / 1 失败。那 2 个 skip 是档案已加载的标志。那 1 个失败是
    Dfo.Server.Application.Tests.Characters.OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed，
    抛 ObjectDisposedException: 'SQLitePCL.sqlite3'（SQLite 句柄在 fixture 建库时已被释放）。
    立刻单独复跑 Dfo.Server.Application.Tests 得 933 通过 / 0 失败，故判定为既有的偶发失败而非回归——
    本工作区是 9c0de6c6 的逐字节检出，未改动任何代码。逐项目计数
    60 + 52 + 488 + 255 + 221 + 6 + 933 = 2015，与主检出一致。
    **给后续子成果的提醒：再见到这一条不要当成自己引入的回归，复跑该项目即可区分。**
---

# Multi-Spec Map

## Multi-Spec Boundary

- 三个子成果各有**独立的验收口径**，而不是同一个口径下的三段工作量。前两个的判据是「服务器
  行为逐值不变」；第三个**必须**改变一处可观察行为（升级之后报出的剩余 SP 从入场陈值变为按
  新等级重算的值）。把它们放进一个 Spec，会让前两个的行为保持判据失去意义——任何差异都能
  被归给「那是第三部分该改的」。
- 每一个单独完成都是可交付的成果，且可以在不牵动另外两个的前提下被接受或回滚。第一个立起
  的机检约束会约束后两个，但那是单向的加固，不是双向的依赖。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 每个命令处理器的每个构造器参数都是领域服务、领域 store、实例（或交出实例的工厂），或在具名豁免清单里 | satisfied | `attempts/spec-001-guard-the-injection-surface/verification/completion-evidence.md` C-1：27 个处理器 55 个参数零违规，独立评审者以外部反射探针复核同一结论 | — | aligned |
| SC-2 | 没有命令处理器直接注入规则服务、静态规则类、PVF 内容源或行网关 | satisfied | `attempts/spec-001-guard-the-injection-surface/verification/completion-evidence.md` C-2 | — | aligned |
| SC-3 | 违反 SC-1/SC-2 会让判据变红 | satisfied | `attempts/spec-001-guard-the-injection-surface/verification/completion-evidence.md` C-4：拒绝用例加遍历非空断言；评审者另做三次生产代码变异逐次被抓 | — | aligned |
| SC-4 | 领域层「store」只指事务结算的真源且名字这么说；摆摊语义仍可从名字读出 | satisfied | `attempts/spec-001-guard-the-injection-surface/verification/completion-evidence.md` C-5：含 `Store` 的类型逐个分类对照，摆摊一族一字未动 | `OnlineStateStore` 仍叫 Store 而非 `IDomainStore`；它是内存容器，归后续「容器转领域服务」处理 | aligned |
| SC-5 | 三个内存容器改说领域话并私有化；大聚合不再被当作可读写的角色状态持有 | not_started | none | 三个注册表待转领域服务；大聚合 16 处读、9 处 `with` 写回待拆。**新增负担**：spec-001 为消除注入面违规，给 `ISelectCharacterSessionEntryDomainService` 加了三个逐字透传容器动词的成员，容器既没私有化也没改说领域话，须在此一并收口 | aligned |
| SC-6 | 等级、经验、剩余 SP/TP 由角色实例回答，不再从入场镜像读 | not_started | none | 四个经验帧发送方仍读 `result.CharacterEntry.Skills.ProtocolMirror` | aligned |
| SC-7 | 三个零消费方接缝及只写不读的角色状态残留副本不再存在 | satisfied | `attempts/spec-001-guard-the-injection-surface/verification/completion-evidence.md` C-6：五个标识符全仓 0 处；注册项收敛为两个字段且各有存活读出点，反向索引随之退役 | — | aligned |
| SC-8 | 除 SC-6 那一处外行为逐值不变 | in_progress | `attempts/spec-001-guard-the-injection-surface/verification/completion-evidence.md` C-7：2007 通过 / 2 跳过 / 0 失败，逐文件用例数对账 2015 − 21 + 11 + 2 闭合 | 后两个子成果各自的行为保持仍待取证 | aligned |
| SC-9 | 词汇表与对应判据写进仓库约定；`AGENTS.md` 与代码的冲突消解或如实记载 | in_progress | `attempts/spec-001-guard-the-injection-surface/verification/completion-evidence.md` C-8：`AGENTS.md` 四处改动，五个内部服务的跨层记载已据实改写，分层判据自陈已更新 | 后两个子成果的结果仍需补记 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001` | `./attempts/spec-001-guard-the-injection-surface/TaskContract.md` | 注入面可由类型本身判定并被判据守住：领域 store 正名、两处硬违规消除、十四次命名不到位归类、三个死件退役 | SC-1, SC-2, SC-3, SC-4, SC-7, SC-8, SC-9 | `sealed` | `completed`（`./attempts/spec-001-guard-the-injection-surface/spec-result.md`） | `70a3e54e` |
| 2 | `spec-002` | `./attempts/spec-002-containers-speak-domain-verbs/TaskContract.md` | 会话与在场内存容器改说领域话；拆掉 spec-001 为通过判据而留下的透传壳 | SC-4, SC-5, SC-8, SC-9 | `running` | pending | `active` |
