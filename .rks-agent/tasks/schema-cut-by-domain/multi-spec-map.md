---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 2
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-003
last_transition: child-created
transition_evidence: >-
  三个转换连续做出，各自的证据如下。
  root_evaluation / gaps-remain：以推进后的基线 5aba3a9 重算，SC-3 与 SC-4 已满足，
  SC-8、SC-9、SC-10 由角色本体一块部分举证而未满足，SC-1、SC-2、SC-5、SC-6 仍为 not_started；
  十条约束逐条分类，全部 aligned，无 at_risk 与 violated。故有缺口。
  successor_decision / safe-successor：裁定的次序结论为「角色本体先行，物品侧与技能侧随后、
  彼此之间任意次序」，两处跨块耦合（金币上限的读取、PvP 技能种子的标记）都已由 spec-002 消化，
  物品侧因此是一个边界确定的可行成功者，且完全落在根 task 授权的范围内，无需扩权。
  child_modeling / child-created：最大已封存尝试号为 2，故编号 spec-003，
  子标识 spec-003-item-carrier-cut-by-concept，目录经解析后证明包含于
  D:/project/DNF/DNF-JP-86-Server/.worktrees/schema-cut-by-domain/.rks-agent/tasks/schema-cut-by-domain。
  子 TaskContract 已写为 state: ready，parent 指向 ../../TaskContract.md，
  contributes_to 为 [SC-1, SC-2, SC-8, SC-9, SC-10]，depends_on 为空序列——
  依赖联结校验通过（空列表合法；spec-002 的成果由 workspace.baseline 表示而非依赖边）。
  子路由决定已写在同目录，四节完整（含 Controller Selection：sdd-standard / code-refactor）。
  激活前证明无活动子任务、无 running/active 行；随后追加恰好一行并置 active_attempt。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/schema-cut-by-domain/.rks-agent/tasks/schema-cut-by-domain
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/schema-cut-by-domain
  branch: schema-cut-by-domain
  baseline: 5aba3a9dcea2b302797eb33097ae923a7a824a45
  baseline_evidence: >-
    任务工作树 HEAD = 5aba3a9dcea2b302797eb33097ae923a7a824a45，提交后 git status --short 为空；
    该提交建立在前一基线 9c535a9 之上，源分支 main 仍为 fc6467af30eb42bfca8319da8fc238082e00116f 未变。
    产品测试证据取自本基线：dotnet test Server/86JP.Server.slnx -m:1 = 1061 通过 / 2 跳过 / 1 失败，
    唯一那条红为 Host.Tests 满载下撞 10 秒预算的已知假红，隔离复跑 4/4 通过
    （PVF_ARCHIVE_PATH 与 Pvf__ArchivePath = D:/project/DNF/DNFClient-debug/Script.pvf）。
    起点基线（main @ fc6467a）的产品测试为 1032 通过 / 2 跳过 / 0 失败。
---

# Multi-Spec Map

## Multi-Spec Boundary

根契约含四个可独立验收的成果，各自作用于互不相交的表集合、各自有独立的表征守卫、各自可在不牵动未接受兄弟成果的前提下被接受或回滚：

- 物品域（SC-1、SC-2）：让任意归属下的一件物品由同一处承载，退役只做空判的上一代物品表，把按件的附加信息与按件的到期或锁定状态各自收敛到一处。单独交付即可验收，不依赖角色本体是否重切。
- 角色本体与至多一行状态（SC-3、SC-4）：把切分线从协议分段换成概念线，并把六处「每角色至多一行」的状态折入本体。
- 技能域（SC-5）：技能不因 PVE 与 PVP 分表，职业专属技能页不因职业分表。
- 账号侧（SC-6）：至多一行的设置并入账号本体，计时权益与一次性进度各自收敛到一处。

四者之间存在需要排序而非合并的耦合：金币上限今天由物品侧读取，把它折入角色本体会改动物品侧的读路径，因此这两块的先后须由控制器在成功者决定时裁定。

SC-7 至 SC-10 是贯穿性验收，任何单个子成果都无法独占证明，由根控制器在各子成果之上合成。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 任意归属下的一件物品由同一处承载，且不存在仅用于空判的上一代物品表 | not_started | none | 物品仍分属多张表，上一代的两张仍在被空判使用 | aligned |
| SC-2 | 「按件的附加信息」与「按件的到期或锁定状态」各自只有一处承载 | not_started | none | 租借仍有两处列语义相同的承载；锁定、按件数值、时装详情各自独立 | aligned |
| SC-3 | 角色本体的每张表都能用一个概念说清边界，无一能由协议分段解释 | satisfied | 提交 5aba3a9：`characters` / `character_stats` / `character_condition` / `character_pvp` / `character_settings` / `character_expert_job` 六张表各由一个概念界定；`character_subtype0_fields` 与 `character_subtype1_fields` 已不存在。`CharacterBodyShapeTests` 逐列比对裁定的目标形状；spec-002 verification 判 pass | 无 | aligned |
| SC-4 | 每角色至多一行的状态不再单独成表 | satisfied | 提交 5aba3a9：`character_init_flags`、`character_gold_limits`、`character_crystal_contract`、`character_dimension_flags`、`character_pvp_skill_state` 五张已不存在，其列落入六张概念表；`character_expert_job` 保留是因为它是一个概念而不是因为它窄——裁定 S-9 给出理由。`CharacterBodyCreationTests` 证明六张表各得一行、八个非零默认值就位 | 无 | aligned |
| SC-5 | 技能不因 PVE 与 PVP 分表；职业专属技能页不因职业分表且非本职业持有仍被拒绝 | not_started | none | 技能仍分两表，职业专属页仍分两表 | aligned |
| SC-6 | 账号侧至多一行的设置并入账号本体，计时权益与一次性进度各自只有一处 | not_started | none | 账号设置仍单独成表 | aligned |
| SC-7 | 每处合并与每处「相近但未合并」都有判据裁定，邮件附件归属有明确结论 | satisfied | `Docs/Refactor/TableOwnershipRuling.md`（提交 9c535a9）；spec-001 的 verification/completion-evidence.md 判 pass，十轮评审末两轮 passed 且阻塞级为零 | 无 | aligned |
| SC-8 | 对外协议行为一字不变 | in_progress | 角色本体一块已举证：三份整记录表征用例的**期望输出一字未改**且通过，领域记录文件 `git diff` 为空；全量 1061/2/1（唯一红为已知假红，隔离 4/4） | 物品域、技能域、账号侧尚未改动，其下发帧比对待各自子成果举证；根层的实机走查一次选人与一次物品移动待全部落地后进行 | aligned |
| SC-9 | 今天由整记录表征用例守住的列在新形状下仍被守住 | in_progress | 角色本体一块已举证：表征用例的播种改到目标形状而期望值零改动，且经两次可证伪性演示（列对调、空值语义回退）先红后绿 | 物品域与账号侧的表征用例仍针对旧形状，须随各自子成果改写并各自演示可证伪 | aligned |
| SC-10 | 上一轮确立的形状未回退 | in_progress | 角色本体一块已举证：脚本清点 `Dfo.Server.Persistence` 下每个类 SQL 中的表名，零个类指名超过一张表；跨表组装仍在 `CharacterEntryAssembly`，领域记录 diff 为空 | 每落一块须重新清点一次；根层终验时以当时的基线重跑 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-table-by-table-ruling/TaskContract.md` | 对改动前的 53 张表与 420 列逐一裁定归属，合成目标形状，并定下邮件附件与金币上限两处归属及由此确定的实施先后 | `SC-7` | `sealed` | `completed` | `accepted as 9c535a9` |
| `2` | `spec-002` | `attempts/spec-002-character-body-cut-by-concept/TaskContract.md` | 按裁定的目标形状重切角色本体：三张本体表与五张至多一行的表变为六张概念表，另完成两处已授权的超范围变更 | `SC-3`, `SC-4` | `sealed` | `completed` | `accepted as 5aba3a9` |
| `3` | `spec-003` | `attempts/spec-003-item-carrier-cut-by-concept/TaskContract.md` | 按裁定的目标形状重切物品承载：四张表并入 `items`、两张租借表并为 `character_rentals`、判别列表拆为两张、上一代两张表连同迁移守卫退役 | `SC-1`, `SC-2` | `running` | `pending` | `active` |
