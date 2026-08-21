---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_disposition
phase_state: in_progress
active_attempt: spec-001
last_transition: child-sealed
transition_evidence: >-
  子 Spec 返回终态 completed，其 spec-result.md 已封存于
  attempts/spec-001-table-by-table-ruling/spec-result.md。
  评审十轮，末两轮判定 passed 且阻塞级为零，记录在 reviews/change-review.md；
  新鲜验证 decision: pass，记录在 verification/completion-evidence.md，
  七条子成功条件各有当前证据、四条约束均成立。
  观察到的仓库状态：HEAD 仍为 fc6467af30eb42bfca8319da8fc238082e00116f，
  工作区仅有两处未跟踪内容——.rks-agent/tasks/schema-cut-by-domain/ 与
  Docs/Refactor/TableOwnershipRuling.md，无任何已跟踪文件被修改，未产生任务级提交。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/schema-cut-by-domain/.rks-agent/tasks/schema-cut-by-domain
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/schema-cut-by-domain
  branch: schema-cut-by-domain
  baseline: fc6467af30eb42bfca8319da8fc238082e00116f
  baseline_evidence: >-
    源分支 main @ fc6467af30eb42bfca8319da8fc238082e00116f，源工作树 git status --short 为空；
    任务工作树 HEAD 与之相同，工作区除声明的两份持久工件外无改动；
    产品测试基线取自同一修订：dotnet test Server/86JP.Server.slnx -m:1 = 1032 通过 / 2 跳过 / 0 失败
    （PVF_ARCHIVE_PATH 与 Pvf__ArchivePath = D:/project/DNF/DNFClient-debug/Script.pvf）。
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
| SC-3 | 角色本体的每张表都能用一个概念说清边界，无一能由协议分段解释 | not_started | none | 本体三处的边界仍是协议包的段落 | aligned |
| SC-4 | 每角色至多一行的状态不再单独成表 | not_started | none | 六处至多一行的状态仍各自成表 | aligned |
| SC-5 | 技能不因 PVE 与 PVP 分表；职业专属技能页不因职业分表且非本职业持有仍被拒绝 | not_started | none | 技能仍分两表，职业专属页仍分两表 | aligned |
| SC-6 | 账号侧至多一行的设置并入账号本体，计时权益与一次性进度各自只有一处 | not_started | none | 账号设置仍单独成表 | aligned |
| SC-7 | 每处合并与每处「相近但未合并」都有判据裁定，邮件附件归属有明确结论 | not_started | none | 尚无逐表裁定记录 | aligned |
| SC-8 | 对外协议行为一字不变 | not_started | none | 尚未产生改动，无可比对的下发帧 | aligned |
| SC-9 | 今天由整记录表征用例守住的列在新形状下仍被守住 | not_started | none | 三份表征 golden 仍针对旧形状 | aligned |
| SC-10 | 上一轮确立的形状未回退 | not_started | none | 基线上成立，改动后需重新举证 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-table-by-table-ruling/TaskContract.md` | 对改动前的 53 张表与 420 列逐一裁定归属，合成目标形状，并定下邮件附件与金币上限两处归属及由此确定的实施先后 | `SC-7` | `running` | `pending` | `active` |
