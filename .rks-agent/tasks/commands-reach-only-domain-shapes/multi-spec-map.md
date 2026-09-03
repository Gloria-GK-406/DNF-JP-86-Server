---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-001
last_transition: child-created
transition_evidence: >-
  标识与路径按确定规则算出（下一个编号 1，无沿用）：
  attempts/spec-001-guard-the-injection-surface/，已规范化并证明包含在 task_root 之内。
  子契约 state: ready，parent 指向 ../../TaskContract.md，contributes_to 为
  SC-1/2/3/4/7/8/9，depends_on 为空列表（合法：无未解决的外部前置）。子路由决策已就位，
  SDD 就绪、边际控制价值、Single 结构选择三节填完，Controller Selection 留给
  single-spec-workflow。激活前校验无错，此前无活跃子成果、无 running 行。
  上一次转移的证据（safe-successor）：
  根评估（基线 9c0de6c6）：九条成功条件全部 not_started，无任何已接受证据，五项约束全部
  aligned，尚无既往 Spec 结果，故非 root-satisfied，应用 gaps-remain。后继决策：契约
  Authorization 的 task 行覆盖「配置的执行与验证」，无需扩权；当前缺口里最小且自足的一个
  有界成果是「让注入面有正确的名字并有判据守着它」——它同时消化三个零消费方接缝（退役后
  白名单要分类的类型更少）、三个领域 store 的正名，以及两处硬违规与 14 次命名不到位的注入。
  该成果可独立接受或回滚，不依赖任何未接受的兄弟结果，故应用 safe-successor。
  上一次转移的证据（workspace-ready）：
  主检出为 main/9c0de6c6，git-dir 与 common-dir 相同故非链接工作区，非子模块；工作区仅有本任务
  两份未跟踪产物。`.worktrees/` 已由 .git/info/exclude:7 排除，未改 .gitignore。以
  `git worktree add -b commands-reach-only-domain-shapes .worktrees/commands-reach-only-domain-shapes
  9c0de6c6` 建任务工作区，建后 toplevel/branch/HEAD 三项自证，status 为空。两份启动产物以
  sha256 校验后转移到工作区内相同的仓库相对路径
  （TaskContract.md 077f5932…b3f，routing-decision.md 79f9b1e6…91d），源副本经校验后删除；
  源检出 status 恢复为空，源分支 tip 仍为 9c0de6c6，未产生启动提交。
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/commands-reach-only-domain-shapes/.rks-agent/tasks/commands-reach-only-domain-shapes
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/commands-reach-only-domain-shapes
  branch: commands-reach-only-domain-shapes
  baseline: 9c0de6c689964b5e791b5adb33cf6bc9bcb9b4ed
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
| SC-1 | 每个命令处理器的每个构造器参数都是领域服务、领域 store、实例（或交出实例的工厂），或在具名豁免清单里 | not_started | none | 基线 55 个依赖中 19 次落在白名单外；豁免清单尚不存在 | aligned |
| SC-2 | 没有命令处理器直接注入规则服务、静态规则类、PVF 内容源或行网关 | not_started | none | 两处硬违规待消除：`ICharacterTable`、`ISelectedSessionPositionPersistence` | aligned |
| SC-3 | 违反 SC-1/SC-2 会让判据变红 | not_started | none | 现有判据只守「不许注入 `ICommandBus`」一条黑名单 | aligned |
| SC-4 | 领域层「store」只指事务结算的真源且名字这么说；摆摊语义仍可从名字读出 | not_started | none | 三个 `IDomainStore` 实现待改名；`OnlineStateStore` 与六个容器的一词多义待消解 | aligned |
| SC-5 | 三个内存容器改说领域话并私有化；大聚合不再被当作可读写的角色状态持有 | not_started | none | 三个注册表待转领域服务；大聚合 16 处读、9 处 `with` 写回待拆 | aligned |
| SC-6 | 等级、经验、剩余 SP/TP 由角色实例回答，不再从入场镜像读 | not_started | none | 四个经验帧发送方仍读 `result.CharacterEntry.Skills.ProtocolMirror` | aligned |
| SC-7 | 三个零消费方接缝及只写不读的角色状态残留副本不再存在 | not_started | none | 三个接缝连同各自测试待退役；`ActiveCharacterState` 副本 A 待处置 | aligned |
| SC-8 | 除 SC-6 那一处外行为逐值不变 | not_started | none | 待各子成果分别取证并在根层汇总对账 | aligned |
| SC-9 | 词汇表与对应判据写进仓库约定；`AGENTS.md` 与代码的冲突消解或如实记载 | not_started | none | 五个 `Application` 内部服务的跨层状态待裁决；`AGENTS.md:403` 待随判据更新 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001` | `./attempts/spec-001-guard-the-injection-surface/TaskContract.md` | 注入面可由类型本身判定并被判据守住：领域 store 正名、两处硬违规消除、十四次命名不到位归类、三个死件退役 | SC-1, SC-2, SC-3, SC-4, SC-7, SC-8, SC-9 | `running` | pending | `active` |
