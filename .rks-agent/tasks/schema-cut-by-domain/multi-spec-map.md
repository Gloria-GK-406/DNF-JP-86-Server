---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 2
state: active
phase: successor_decision
phase_state: pending
active_attempt: null
last_transition: gaps-remain
transition_evidence: >-
  以最终基线 6275535 重算全部十条成功条件与十条约束。
  九条已满足：SC-1..SC-7、SC-9、SC-10，各自的证据写在上表里。
  **SC-8 仍为 in_progress，且它的缺口只剩一样：契约点名要求的实机走查一次选人与一次物品移动。**
  该条的其余证据（下发帧不变、全量测试、表征期望值）均已具备；实机走查只能由用户在客户端上跑，
  不是任何子 Spec 能执行的成果。十条约束逐条分类，全部 aligned。
  因此 successor_decision 的结论是**不创建后继子任务**：唯一剩下的缺口落在根 task 授权
  所能覆盖的执行范围之外，不构成 safe-successor。根工作流停在此处等待用户的实机走查；
  在那之前不做 root-satisfied，也不跑根层评审与根层终验。
  合并、推送、PR、分支与工作树清理仍需用户单独授权，本轮一概未做。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/schema-cut-by-domain/.rks-agent/tasks/schema-cut-by-domain
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/schema-cut-by-domain
  branch: schema-cut-by-domain
  baseline: 6275535f6ea838574223b3c92e92e2fa7ca6174f
  baseline_evidence: >-
    任务工作树 HEAD = 6275535f6ea838574223b3c92e92e2fa7ca6174f，提交后 git status --short 为空；
    该提交建立在前一基线 1a77ef8 之上，源分支 main 仍为 fc6467af30eb42bfca8319da8fc238082e00116f 未变。
    产品测试证据取自本基线：dotnet test Server/86JP.Server.slnx -m:1 = 1132 通过 / 2 跳过 / 0 失败
    （PVF_ARCHIVE_PATH 与 Pvf__ArchivePath = D:/project/DNF/DNFClient-debug/Script.pvf）。
    历次基线：main @ fc6467a = 1032/2/0；9c535a9 = 1032/2/0（只产出记录）；5aba3a9 = 1061/2/1，
    那一条红是 Host.Tests 满载下撞 10 秒预算的已知假红，隔离复跑 4/4 通过；1a77ef8 = 1100/2/0。
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
| SC-1 | 任意归属下的一件物品由同一处承载，且不存在仅用于空判的上一代物品表 | satisfied | 提交 1a77ef8：`character_new_items`、`account_cargo_new_items`、`character_new_titlebook`、`character_avatar_detail` 并入 `items`；`character_items` 与 `account_cargo_items` 连同两处「migration required」守卫退役。`ItemCarrierShapeTests` 逐列比对裁定的目标形状；spec-003 verification 判 pass | 无 | aligned |
| SC-2 | 「按件的附加信息」与「按件的到期或锁定状态」各自只有一处承载 | satisfied | 提交 1a77ef8：租借由 `character_rentals` 一处承载；时装的附加事实是 `items` 的列；`character_item_values` 按 X-1 拆成冷却与效果两张，`list_kind` 不复存在。三处到期分属时装、租借与邮件附件，是三件不同的事（裁定 S-15、C-5），各留一处即满足本条 | 无 | aligned |
| SC-3 | 角色本体的每张表都能用一个概念说清边界，无一能由协议分段解释 | satisfied | 提交 5aba3a9：`characters` / `character_stats` / `character_condition` / `character_pvp` / `character_settings` / `character_expert_job` 六张表各由一个概念界定；`character_subtype0_fields` 与 `character_subtype1_fields` 已不存在。`CharacterBodyShapeTests` 逐列比对裁定的目标形状；spec-002 verification 判 pass | 无 | aligned |
| SC-4 | 每角色至多一行的状态不再单独成表 | satisfied | 提交 5aba3a9：`character_init_flags`、`character_gold_limits`、`character_crystal_contract`、`character_dimension_flags`、`character_pvp_skill_state` 五张已不存在，其列落入六张概念表；`character_expert_job` 保留是因为它是一个概念而不是因为它窄——裁定 S-9 给出理由。`CharacterBodyCreationTests` 证明六张表各得一行、八个非零默认值就位 | 无 | aligned |
| SC-5 | 技能不因 PVE 与 PVP 分表；职业专属技能页不因职业分表且非本职业持有仍被拒绝 | satisfied | 提交 6275535：`character_pvp_skills` 已不存在，技能由 `character_skills` 一处承载，`mode` 只选一组行而不重新解释任何一列。两张职业专属页表**保持分开**——合并需要一个用其值决定载荷列含义的判别列（页面正文 BLOB 对盾牌 item id），红线所禁；根契约 revision 2 已按用户的决定把本条重述为结果层，裁定记为 C-3。「非本职业持有即拒绝」由 `AJobSpecificPageHeldByAnotherJobIsRefused`（两例）覆盖，此前全仓无人守 | 无 | aligned |
| SC-6 | 账号侧每个概念只有一处承载 | satisfied | 裁定按三条判据判定账号设置**不与账号本体合并**——同键同生命周期成立，写入者不成立（存客户端偏好不是注册账号或动钱包的那个动作），记为 S-1；根契约 revision 2 已按用户的决定把本条重述为结果层。该结果在最终基线 6275535 上成立：`accounts` 是这个账号是谁与它的钱包，`account_settings` 是玩家选的，`account_premiums` 是计时权益，`account_increase_chance_lottery_progress` 是一次性进度，`account_cargo_state` 是仓库整体的一行——五张各一个概念，裁定的逐表裁定与红线检查逐张核过。账号侧唯一的重复（`account_cargo_items` 与 `account_cargo_new_items`）已随 spec-003 退役 | 无（账号侧没有自己的工作，裁定的结论是「三块，不是四块」） | aligned |
| SC-7 | 每处合并与每处「相近但未合并」都有判据裁定，邮件附件归属有明确结论 | satisfied | `Docs/Refactor/TableOwnershipRuling.md`（提交 9c535a9）；spec-001 的 verification/completion-evidence.md 判 pass，十轮评审末两轮 passed 且阻塞级为零 | 无 | aligned |
| SC-8 | 对外协议行为一字不变 | in_progress | 三块全部举证完毕：三份整记录表征用例通过，期望输出在整个根任务里只改过两个数——账号仓库两行的代理键，两个自增键空间并成一个的必然结果，经评审核实不出现在任何下发帧里；两个领域记录文件的 `git diff` 全程为空；全量 1132/2/0，较起点 1032/2/0 净增 100 条且逐条可解释 | **只差一样：实机走查一次选人与一次物品移动。** 这一条契约点名要求，而它只能由用户在实机上跑——本任务的所有其他证据均已具备 | aligned |
| SC-9 | 今天由整记录表征用例守住的列在新形状下仍被守住 | satisfied | 三块各自演示且各自先红后绿：角色本体两次、物品侧四次、技能域五次；三轮评审另做十七次独立真改真跑实测，全部会红。三份整记录表征用例在每一块之后都跑通，期望输出全程只改过两个代理键 | 无 | aligned |
| SC-10 | 上一轮确立的形状未回退 | satisfied | 在最终基线 6275535 上清点：68 个持久化类，**零个指名超过一张表**；跨表组装仍在 `CharacterEntryAssembly` 与 `InventoryEntryAssembly` 两处，四个领域记录文件的 `git diff` 全程为空——每条记录仍只有一个定义点 | 无 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-table-by-table-ruling/TaskContract.md` | 对改动前的 53 张表与 420 列逐一裁定归属，合成目标形状，并定下邮件附件与金币上限两处归属及由此确定的实施先后 | `SC-7` | `sealed` | `completed` | `accepted as 9c535a9` |
| `2` | `spec-002` | `attempts/spec-002-character-body-cut-by-concept/TaskContract.md` | 按裁定的目标形状重切角色本体：三张本体表与五张至多一行的表变为六张概念表，另完成两处已授权的超范围变更 | `SC-3`, `SC-4` | `sealed` | `completed` | `accepted as 5aba3a9` |
| `3` | `spec-003` | `attempts/spec-003-item-carrier-cut-by-concept/TaskContract.md` | 按裁定的目标形状重切物品承载：四张表并入 `items`、两张租借表并为 `character_rentals`、判别列表拆为两张、上一代两张表连同迁移守卫退役 | `SC-1`, `SC-2` | `sealed` | `completed` | `accepted as 1a77ef8` |
| `4` | `spec-004` | `attempts/spec-004-skills-cut-by-concept/TaskContract.md` | 按裁定的目标形状重切技能：`character_skills` 加 `mode` 列吸收 `character_pvp_skills`，四条只在 PvP 一侧声明的保证逐条存续；两张职业专属页按红线保持分开 | `SC-5` | `sealed` | `completed` | `accepted as 6275535` |
