---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-003
last_transition: child-created
transition_evidence: >-
  根评估（基线 68a12119）：SC-1/2/3/4/7 已满足，SC-8/SC-9 进行中，**SC-5 只满足前半、SC-6 仍未
  开始**，五项约束全部 aligned，故 gaps-remain。后继决策：剩余缺口是同一件事的两面——会话聚合
  里角色自己的那三个数（等级、经验、剩余 SP/TP）仍以入场镜像为取数来源，其中剩余 SP 自入场后
  永不重算，正是所有者实机报告的「升级后 SP 不变」。把它们归位即同时补上 SC-5 的后半与 SC-6，
  是一个有界且自足的成果，可独立接受或回滚，故 safe-successor。标识与路径按确定规则算出
  （下一个编号 3，无沿用）：attempts/spec-003-a-character-answers-for-itself/，已证明包含在
  task_root 之内；子契约 ready，depends_on 为空列表；子路由决策四节齐全（controller 取
  sdd-standard，理由是「剩余 SP 的计算换位置后由谁持有三个输入」这处耦合值得 Plan 先行裁决，
  定错的代价是同一个算法出现两份实现）。**注意本成果的验收口径与前两批不同：它必须改变一处
  可观察行为**，因此「判据数字不变」这道护栏在此失效，改动的期望值须逐条对应到那一处授权的
  变化。激活前校验无错，此前无活跃子成果、无 running 行。
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/commands-reach-only-domain-shapes/.rks-agent/tasks/commands-reach-only-domain-shapes
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/commands-reach-only-domain-shapes
  branch: commands-reach-only-domain-shapes
  baseline: 68a121196e270b4d90b9916bc83dc7c819769c11
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
| SC-4 | 领域层「store」只指事务结算的真源且名字这么说；摆摊语义仍可从名字读出 | satisfied | `attempts/spec-002-containers-speak-domain-verbs/verification/completion-evidence.md` C-4：`OnlineStateStore` → `OnlineStateRegistry`，`store` 只剩一种读法。加 `attempts/spec-001-guard-the-injection-surface/verification/completion-evidence.md` C-5：含 `Store` 的类型逐个分类对照，摆摊一族一字未动 | — | aligned |
| SC-5 | 三个内存容器改说领域话并私有化；大聚合不再被当作可读写的角色状态持有 | in_progress | `attempts/spec-002-containers-speak-domain-verbs/verification/completion-evidence.md` C-1/C-2：四个接缝（含 `ISelectedSessionDirectory`）已成领域服务，成员无整表列举，容器为私有字段；spec-001 留下的透传壳已拆 | **后半未做**：`ISelectedSessionRuntimeDirectory` 与它持有的 `SelectCharacterResult` 仍是纯容器加可读写聚合。另 `ReplaceRuntimeExact` 仍是对它的逐字转发 | aligned |
| SC-6 | 等级、经验、剩余 SP/TP 由角色实例回答，不再从入场镜像读 | not_started | none | 四个经验帧发送方仍读 `result.CharacterEntry.Skills.ProtocolMirror` | aligned |
| SC-7 | 三个零消费方接缝及只写不读的角色状态残留副本不再存在 | satisfied | `attempts/spec-001-guard-the-injection-surface/verification/completion-evidence.md` C-6：五个标识符全仓 0 处；注册项收敛为两个字段且各有存活读出点，反向索引随之退役 | — | aligned |
| SC-8 | 除 SC-6 那一处外行为逐值不变 | in_progress | `attempts/spec-001-guard-the-injection-surface/verification/completion-evidence.md` C-7：2007 通过 / 2 跳过 / 0 失败，逐文件用例数对账 2015 − 21 + 11 + 2 闭合 | 已加计 spec-002（`attempts/spec-002-containers-speak-domain-verbs/verification/completion-evidence.md` C-5，2007/2/0 与前一基线逐项目相同）；spec-003 的行为保持仍待取证 | aligned |
| SC-9 | 词汇表与对应判据写进仓库约定；`AGENTS.md` 与代码的冲突消解或如实记载 | in_progress | `attempts/spec-001-guard-the-injection-surface/verification/completion-evidence.md` C-8：`AGENTS.md` 四处改动，五个内部服务的跨层记载已据实改写，分层判据自陈已更新 | 已加计 spec-002（判别式补入后缀表之后、自相矛盾的布局句订正）；spec-003 的结果仍需补记 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001` | `./attempts/spec-001-guard-the-injection-surface/TaskContract.md` | 注入面可由类型本身判定并被判据守住：领域 store 正名、两处硬违规消除、十四次命名不到位归类、三个死件退役 | SC-1, SC-2, SC-3, SC-4, SC-7, SC-8, SC-9 | `sealed` | `completed`（`./attempts/spec-001-guard-the-injection-surface/spec-result.md`） | `70a3e54e` |
| 2 | `spec-002` | `./attempts/spec-002-containers-speak-domain-verbs/TaskContract.md` | 会话与在场内存容器改说领域话；拆掉 spec-001 为通过判据而留下的透传壳 | SC-4, SC-5, SC-8, SC-9 | `sealed` | `completed`（`./attempts/spec-002-containers-speak-domain-verbs/spec-result.md`） | `68a12119` |
| 3 | `spec-003` | `./attempts/spec-003-a-character-answers-for-itself/TaskContract.md` | 角色自己的数（等级、经验、剩余 SP/TP）由角色实例现读现算；升级后报出的 SP 是新等级的值 | SC-5, SC-6, SC-8, SC-9 | `running` | pending | `active` |
