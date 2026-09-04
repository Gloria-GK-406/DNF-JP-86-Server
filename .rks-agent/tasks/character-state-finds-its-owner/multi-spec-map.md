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
  spec-001 交回 spec-result.md，outcome completed。完整变更评审 verdict approved（独立 fresh-context，
  无未决阻断）；新鲜验证 decision pass（独立验证者自跑：构建 0 警告 0 错误，2069 通过 / 2 跳过 / 0 失败，
  契约成功条件 C-1..C-11 逐条有当前证据，含验证者自做的两次变异验证）。以一次提交接受：0d8e960e，
  工作区干净、源分支 main 仍为 427331bc、分支只前进一步。

  **随后所有者裁定停止本根任务，不做 spec-002。** 原话：「那个spec要先不做了。重新起一个新的任务契约和
  spec来做这次的任务。」——工作重心转向通知服务（事件/广播机制），疲劳记账留待将来另起。
  因此本根任务以 **15 条成功条件里 11 条满足、4 条未开始** 的状态封存，不是全部达成。
  未开始的是 SC-9..SC-12（疲劳按用量记账、跨期归零、耗尽拒绝、每日补货边界不变），它们原属计划中的
  spec-002，该子成果**从未被创建**。
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/character-state-finds-its-owner/.rks-agent/tasks/character-state-finds-its-owner
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/character-state-finds-its-owner
  branch: character-state-finds-its-owner
  baseline: 0d8e960ea791262d7d9102a0b6e22fc558424741
  baseline_evidence: >-
    在任务工作区内跑
    `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`：
    2024 通过 / 2 跳过 / 0 失败。逐项目计数 60 + 52 + 498 + 255 + 6 + 223 + 930 = 2024，与主检出一致。
    **那 2 个 skip 是 PVF 档案已加载的标志**——不设 PVF_ARCHIVE_PATH 时读档案的判据会静默跳过而不是
    报红，所以判据命令必须带这个环境变量。

    **两个已知的偶发失败，见到不要当成自己引入的回归：**
    其一，`Dfo.Server.Application.Tests.Characters.OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed`
    会抛 `ObjectDisposedException: 'SQLitePCL.sqlite3'`（SQLite 句柄在 fixture 建库时已被释放），
    单独复跑 Dfo.Server.Application.Tests 即恢复全绿。这一条上一轮任务已经遇到并记录过。
    其二，本次建立基线时 `Dfo.Server.Networking.Tests` 在全量并行下出现过一次失败（51/52），
    **失败用例名未被抓到**——单独复跑该项目得 52/52，随后再跑一次全量得 2024/2/0，两次都没有重现。
    本工作区是 427331bc 的逐字节检出、未改动任何代码，故判定为负载下的一次性偶发。
    如果后续成果再遇到 Networking 项目的失败，请把用例名记下来补进这里。

    **spec-001 期间修正的一条认识**：那个 SQLite 句柄偶发**不是绑在某一条用例上的**。
    TASK-09 的第二轮独立快审遇到 `Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests`
    `.APageTheEntryLoadWouldNotCarryIsRefused` 抛同一个 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，
    栈根同为 `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar`，
    与 `OutsideTransactionTests` 那条是**同一个并行释放竞态**，只是打中了别的用例。
    两次复跑（单独跑该项目 962/0、再跑一次全量 2069/2/0）均恢复。
    **所以判别依据是「异常类型 + 栈根 + 复跑即绿」，不是用例名。** 按名字对不上就当成回归是错的。
---

# Multi-Spec Map

## Multi-Spec Boundary

- 两个子成果的**验收口径互斥**，这是分开的实质理由，不是工作量的切分。第一个的判据是
  「服务器对客户端逐值不变」；第二个**必须**改变可观察行为（疲劳真的会扣、扣光会被既有的
  拒绝码拒）。放进同一个 Spec，任何差异都能被归给「那是第二个该改的」，第一个的逐值不变判据
  就失去了意义。上一轮任务 `commands-reach-only-domain-shapes` 分三批用的是同一条理由。
- 各自单独完成都是可交付成果。第一个做完，角色状态已经各归各位，即使第二个不做也成立；第二个
  不牵动第一个的任何归宿决定。回滚边界同样独立。
- **反过来说，第一个成果内部的六件事不构成边界**：它们共享同一个口径（逐值不变），按上一轮
  根图立下的规矩，那是「同一个口径下的几段工作量」而不是几个成果，属于一个 Spec。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 角色实例能回答自己的身份，现读不缓存；入场序列之外不从会话运行态聚合读身份 | satisfied | `attempts/spec-001-owners-for-character-state/verification/completion-evidence.md` C-1 | — | aligned |
| SC-2 | 角色坐标由一个专门的领域服务持有并回答，不由角色实例回答；会话聚合不再是坐标的取数与写回位置 | satisfied | `attempts/spec-001-owners-for-character-state/verification/completion-evidence.md` C-2 | — | aligned |
| SC-3 | 角色的按键与选项设置由一个领域服务回答 | satisfied | `attempts/spec-001-owners-for-character-state/verification/completion-evidence.md` C-3；生产调用方 0，如实报出 | — | aligned |
| SC-4 | 暗帝连招页由领域 store 与其实例持有；恒为 null 的单数残留字段在全仓不再存在 | satisfied | `attempts/spec-001-owners-for-character-state/verification/completion-evidence.md` C-4 | — | aligned |
| SC-5 | 入场之后读任务状态的那一处改问已有的任务簿实例，且没有为任务新建第二条通路 | satisfied | `attempts/spec-001-owners-for-character-state/verification/completion-evidence.md` C-5 | — | aligned |
| SC-6 | 疲劳有一个接缝，进本处真的向它报告消耗；第一批里它不记账，且这一点可从代码本身读出 | satisfied | `attempts/spec-001-owners-for-character-state/verification/completion-evidence.md` C-6 | — | aligned |
| SC-7 | 第一批里疲劳的调用点在被改坏时判据会红 | satisfied | `attempts/spec-001-owners-for-character-state/verification/completion-evidence.md` C-7，验证者自做两次变异 | — | aligned |
| SC-8 | 第一个成果做完时，服务器对客户端的行为逐值不变 | satisfied | `attempts/spec-001-owners-for-character-state/verification/completion-evidence.md` C-8；既有断言一字未改 | — | aligned |
| SC-9 | 疲劳按「用了多少」记账；上限由基础值与账号权益现算，不存在存放剩余量的位置 | not_started | none | **所有者裁定暂不做。** 原属计划中的 spec-002，该子成果从未被创建 | aligned |
| SC-10 | 跨过每日重置时刻之后疲劳消耗归零，且不依赖任何定时任务或后台触发器 | not_started | none | **所有者裁定暂不做。** 原属计划中的 spec-002，该子成果从未被创建 | aligned |
| SC-11 | 疲劳耗尽时进本被拒，用的是既有的那个拒绝码 | not_started | none | **所有者裁定暂不做。** 原属计划中的 spec-002，该子成果从未被创建 | aligned |
| SC-12 | 每日补货的行为不变，其日边界不因疲劳而改动 | not_started | none | **所有者裁定暂不做。** 原属计划中的 spec-002，该子成果从未被创建 | aligned |
| SC-13 | 命令处理器注入面判据仍然通过，判定谓词与具名豁免清单未被放宽或扩充 | satisfied | `attempts/spec-001-owners-for-character-state/verification/completion-evidence.md` C-9；谓词与豁免清单 diff 为空 | — | aligned |
| SC-14 | 领域层中 `store` 一词仍只有一种读法：事务结算的真源 | satisfied | `attempts/spec-001-owners-for-character-state/verification/completion-evidence.md` C-10 | — | aligned |
| SC-15 | 仓库约定记下这几样状态各自归谁，且如实写下会话运行态聚合仍然存在 | satisfied | `attempts/spec-001-owners-for-character-state/verification/completion-evidence.md` C-11；数字逐个复算吻合 | — | aligned |

## Planned Decomposition

预定两个子成果。**这是路由时的规划，不是承诺**——每个子成果都在它自己的 `child_modeling`
阶段按当时的已接受证据重新裁定，控制器不得凭这一节预先创建后续 Spec。

- **spec-001「角色的每样状态各自找到主人」** → SC-1..SC-8、SC-13..SC-15。口径：行为逐值不变。
  六件事：身份归实例、坐标归专门的领域服务、设置归领域服务、连招归 store + 实例（并删掉恒 null
  的单数残留）、任务那一处读点重指已有的任务簿、疲劳占位接缝（含证明调用点会红的变异验证）。
- **spec-002「疲劳真的记账」** → SC-9..SC-12，并复核 SC-13..SC-15。口径：必须改变可观察行为。

**一个 reroute 条件**：若连招 store 那件事在执行中发展出自己的验收边界（例如事务参与者的接线
牵动了别的 store 的结算顺序，使得它必须能被独立接受或回滚），则 spec-001 的边界不再成立，
应停下重新切分而不是硬做完。

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001` | `./attempts/spec-001-owners-for-character-state/TaskContract.md` | 角色的身份、坐标、设置、连招页各自找到主人；入场后那一处任务读点重指已有的任务簿；疲劳立一个有会红判据的占位接缝。行为逐值不变 | SC-1..SC-8, SC-13..SC-15 | `sealed` | `completed`（`./attempts/spec-001-owners-for-character-state/spec-result.md`） | `0d8e960e` |

## Root Evaluation（spec-001 接受之后，所有者裁定暂停）

十五条成功条件里 **十一条满足，四条未开始**：

- SC-1..SC-8、SC-13..SC-15 满足，证据见上表各行，全部来自 spec-001 的新鲜验证。
- **SC-9..SC-12 未开始**（疲劳按用量记账、跨期归零、耗尽拒绝、每日补货边界不变）。它们原属计划中的
  spec-002「疲劳真的记账」，**该子成果从未被创建**。

**为什么停在这里。**

所有者在 spec-001 接受之后裁定暂不做疲劳记账，把工作重心转向通知服务（事件与广播机制）。原话：
「那个spec要先不做了。重新起一个新的任务契约和spec来做这次的任务。」

这不是阻塞也不是失败——根契约与它的四条剩余条件都仍然成立，只是**当前没有继续的授权**。控制器按推断
替所有者继续，正是授权边界明令禁止的。因此本图停在根评估，如实记下差口，不宣称完成。

**留给将来重启 spec-002 的人的东西**，都在 `attempts/spec-001-owners-for-character-state/spec-result.md`
的 Successor Guidance 里，其中与疲劳直接相关的三条：

1. 占位无条件报告消耗 `1`，而教程本被既有判据钉着 `ConsumesFatigue = false`——按写下的规则该副本
   进本消耗是 **0**。记真账时调用点**必须先问档案该副本扣不扣**
2. 「每日 6 点重置」这个**时刻仓内无出处**。代码只写 `reset daily`；`Docs/DungeonEntryNotes.md` §八
   有基础 156、黑钻 +32、抗疲劳药剂，独独没有时刻。实现每日边界前需要所有者给一个可追溯落点
3. 疲劳接缝已经有会红的判据钉住调用点（移除调用、数值改错、被拒绝路径误报，三种变异各有判据变红），
   所以把空转换成真账时，**调用面是已经被证明过的**
