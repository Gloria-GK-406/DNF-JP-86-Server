---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: root_evaluation
phase_state: in_progress
active_attempt: null
last_transition: child-accepted
transition_evidence: >-
  spec-003 交回 spec-result.md，outcome completed。独立评审 verdict approved（第一轮
  changes_required：一条阻断是 C-2/RQ-003 写「这三个数」而实现只做到剩余 SP/TP；另有一条以变异
  实证的覆盖缺口——把击杀路径整段回退到读入场镜像，全量仍全绿。整改：三条零判据路径各补 3 条
  用例并做变异验证全部变红，副本结算那条由「靠空引用红」强化为「靠值不对红」；阻断按控制者的
  契约修订处理，C-2/RQ-003 升 revision 2 重述到证据支持的层面，理由与「这是控制者做出的收窄」
  写进契约。评审复核后判定该修订成立，但指出修订依据里有一句事实错误的论断，控制者核实后订正
  并把错误与订正一并留痕）。新鲜验证 decision pass：构建 0 警告 0 错误，2024 通过 / 2 跳过 /
  0 失败。以一次提交接受：f5a44ba7，工作区干净、源分支 main 仍为 9c0de6c6、分支只前进一步。
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/commands-reach-only-domain-shapes/.rks-agent/tasks/commands-reach-only-domain-shapes
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/commands-reach-only-domain-shapes
  branch: commands-reach-only-domain-shapes
  baseline: f5a44ba74662f6ee55ae747901226bce7c4a38b0
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
| SC-5 | 三个内存容器改说领域话并私有化；大聚合不再被当作可读写的角色状态持有 | in_progress | 前半满足（spec-002 C-1/C-2）。后半部分满足：`attempts/spec-003-a-character-answers-for-itself/verification/completion-evidence.md` C-2——会变陈的那个角色数值（剩余 SP/TP）已不从大聚合取，角色实例能回答三个数 | **后半未完**：大聚合仍持有并改写角色的等级与经验（升级时 `ReplaceExact` 写回，11 处读取）与坐标；`ReplaceRuntimeExact` 仍是对它的逐字转发。**这超出所有者批准的三批范围**，见根评估一节 | aligned |
| SC-6 | 等级、经验、剩余 SP/TP 由角色实例回答，不再从入场镜像读 | satisfied | `attempts/spec-003-a-character-answers-for-itself/verification/completion-evidence.md` C-1/C-2/C-3：实例能回答三者且现读现算；入场镜像（只装 SP/TP）的读取点除入场序列外已清零；升级后报出的剩余 SP 是重算值，四条路径各有会红的判据 | — | aligned |
| SC-7 | 三个零消费方接缝及只写不读的角色状态残留副本不再存在 | satisfied | `attempts/spec-001-guard-the-injection-surface/verification/completion-evidence.md` C-6：五个标识符全仓 0 处；注册项收敛为两个字段且各有存活读出点，反向索引随之退役 | — | aligned |
| SC-8 | 除 SC-6 那一处外行为逐值不变 | satisfied | 三批各自的验证证据：spec-001 C-7（逐文件用例数对账 2015 − 21 + 11 + 2 闭合）、spec-002 C-5（2007/2/0 与前一基线逐项目相同）、spec-003 C-4（唯一一处可观察变化即升级后的剩余 SP，改动的期望值逐条归因，无第二处） | — | aligned |
| SC-9 | 词汇表与对应判据写进仓库约定；`AGENTS.md` 与代码的冲突消解或如实记载 | satisfied | spec-001 C-8（词汇表与判据、五个内部服务跨层记载据实改写、分层判据自陈更新）、spec-002 C-6（判别式补入、自相矛盾的布局句订正）、spec-003 C-6（「角色自己的数，问角色」一节并如实写下现在做到了哪一步）。三轮评审均逐句核过无一句比代码强 | — | aligned |

## Root Evaluation（三批完成后）

九条成功条件里 **八条满足，一条未完**：

- SC-1、SC-2、SC-3、SC-4、SC-6、SC-7、SC-8、SC-9 满足，证据见上表各行。
- **SC-5 的后半未完。** 「大聚合不再被当作可读写的角色状态持有」——会变陈的那个角色数值
  （剩余 SP/TP）已经不从它取，角色实例也能回答等级与经验；但大聚合仍持有并改写角色的等级与
  经验（升级时 `ReplaceExact` 写回，11 处读取）以及坐标，`ReplaceRuntimeExact` 仍是对它的
  逐字转发。

**为什么停在这里，而不是自己开第四个子成果。**

根契约的 In scope 那一条写的是「拆解大聚合：**角色自己的数**改由角色实例回答」，而 SC-5 的
措辞「大聚合不再被当作可读写的角色状态持有」比这条 scope 宽。所有者批准的是三批分工，
三个子成果的 Non-Goals 也逐个明写「会话运行态里与角色自身无关的部分不动」。

把剩下的部分拆完（坐标、在场、世界内容、背包投影、账号侧状态），是一件与已完成三批体量相当
的工作，而且**没有被所有者的三批授权覆盖**。控制者按推断替所有者扩大范围，正是授权边界明令
禁止的。因此在此停下，把差口如实报出，由所有者决定是否继续。

已完成的三批已各自接受并提交，工作区干净，判据全绿。

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001` | `./attempts/spec-001-guard-the-injection-surface/TaskContract.md` | 注入面可由类型本身判定并被判据守住：领域 store 正名、两处硬违规消除、十四次命名不到位归类、三个死件退役 | SC-1, SC-2, SC-3, SC-4, SC-7, SC-8, SC-9 | `sealed` | `completed`（`./attempts/spec-001-guard-the-injection-surface/spec-result.md`） | `70a3e54e` |
| 2 | `spec-002` | `./attempts/spec-002-containers-speak-domain-verbs/TaskContract.md` | 会话与在场内存容器改说领域话；拆掉 spec-001 为通过判据而留下的透传壳 | SC-4, SC-5, SC-8, SC-9 | `sealed` | `completed`（`./attempts/spec-002-containers-speak-domain-verbs/spec-result.md`） | `68a12119` |
| 3 | `spec-003` | `./attempts/spec-003-a-character-answers-for-itself/TaskContract.md` | 角色自己的数由角色实例现读现算；升级后报出的 SP 是新等级的值 | SC-5, SC-6, SC-8, SC-9 | `sealed` | `completed`（`./attempts/spec-003-a-character-answers-for-itself/spec-result.md`） | `f5a44ba7` |
