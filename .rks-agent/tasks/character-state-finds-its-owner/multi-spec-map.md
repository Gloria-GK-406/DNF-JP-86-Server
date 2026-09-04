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
  子成果 spec-001 已创建并激活。激活前的校验结果全部为空错误：
  身份与路径——`spec-001` 是首个编号（表原为空，无重用）；子目录
  `attempts/spec-001-owners-for-character-state/`，经 realpath 规范化后证明包含于 task_root 之内
  （大小写不敏感比较，contained=True）。
  子契约——`state: ready`，`parent: ../../TaskContract.md`，`contributes_to` 11 项
  （SC-1..SC-8、SC-13..SC-15），`depends_on: []`。空列表按依赖链接校验规则合法且无需成员分类：
  本成果没有未解决的外部前置契约，已接受的内部历史由 `workspace.baseline` 表示而不是依赖边。
  路由决策——已就位并填完 SDD readiness、marginal control value、child-specific 的 Single
  structural selection；`Controller Selection` 按规则留空，由 `single-spec-workflow` 在实例化
  Profile 之前补齐。
  活动性——追加本行之前表内无 running/active 行，追加之后恰有一行。`active_attempt` 置为 spec-001。
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/character-state-finds-its-owner/.rks-agent/tasks/character-state-finds-its-owner
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/character-state-finds-its-owner
  branch: character-state-finds-its-owner
  baseline: 427331bcb07f60121c97920287ac341c9cb14dee
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
| SC-1 | 角色实例能回答自己的身份，现读不缓存；入场序列之外不从会话运行态聚合读身份 | not_started | none | 身份today只作为入场投影快照的一个字段存在，角色实例不暴露它 | aligned |
| SC-2 | 角色坐标由一个专门的领域服务持有并回答，不由角色实例回答；会话聚合不再是坐标的取数与写回位置 | not_started | none | 坐标现由会话聚合持有，6 处读 + 2 处整体写回 | aligned |
| SC-3 | 角色的按键与选项设置由一个领域服务回答 | not_started | none | 设置现只作为入场投影的一个字段，无任何领域接缝 | aligned |
| SC-4 | 暗帝连招页由领域 store 与其实例持有；恒为 null 的单数残留字段在全仓不再存在 | not_started | none | 连招页现只有行网关与入场投影；单数残留字段仍在 | aligned |
| SC-5 | 入场之后读任务状态的那一处改问已有的任务簿实例，且没有为任务新建第二条通路 | not_started | none | 那一处仍读入场投影快照 | aligned |
| SC-6 | 疲劳有一个接缝，进本处真的向它报告消耗；第一批里它不记账，且这一点可从代码本身读出 | not_started | none | 疲劳今天没有任何接缝，值写死在角色名单服务里 | aligned |
| SC-7 | 第一批里疲劳的调用点在被改坏时判据会红 | not_started | none | 无调用点，故无判据 | aligned |
| SC-8 | 第一个成果做完时，服务器对客户端的行为逐值不变 | not_started | none | 第一个成果尚未开始 | aligned |
| SC-9 | 疲劳按「用了多少」记账；上限由基础值与账号权益现算，不存在存放剩余量的位置 | not_started | none | 疲劳无存储、无上限规则 | aligned |
| SC-10 | 跨过每日重置时刻之后疲劳消耗归零，且不依赖任何定时任务或后台触发器 | not_started | none | 无重置机制 | aligned |
| SC-11 | 疲劳耗尽时进本被拒，用的是既有的那个拒绝码 | not_started | none | 该拒绝码定义了但全仓零引用 | aligned |
| SC-12 | 每日补货的行为不变，其日边界不因疲劳而改动 | not_started | none | 尚未开始；这一条守的是不要为了疲劳去改每日补货那条 UTC+8 0 点边界 | aligned |
| SC-13 | 命令处理器注入面判据仍然通过，判定谓词与具名豁免清单未被放宽或扩充 | not_started | none | 基线上该判据通过；本轮每个成果都要复核它没被放宽 | aligned |
| SC-14 | 领域层中 `store` 一词仍只有一种读法：事务结算的真源 | not_started | none | 基线上成立；第一个成果要新增一个 store，必须仍然只有这一种读法 | aligned |
| SC-15 | 仓库约定记下这几样状态各自归谁，且如实写下会话运行态聚合仍然存在 | not_started | none | 尚未记载 | aligned |

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
| 1 | `spec-001` | `./attempts/spec-001-owners-for-character-state/TaskContract.md` | 角色的身份、坐标、设置、连招页各自找到主人；入场后那一处任务读点重指已有的任务簿；疲劳立一个有会红判据的占位接缝。行为逐值不变 | SC-1, SC-2, SC-3, SC-4, SC-5, SC-6, SC-7, SC-8, SC-13, SC-14, SC-15 | `running` | pending | `active` |
