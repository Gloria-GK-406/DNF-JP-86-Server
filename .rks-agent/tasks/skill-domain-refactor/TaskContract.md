---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: 技能领域重构——收拢技能状态并引入技能组

## Context

### Source Request

2026-11-16 项目对话，用户原话（按顺序）：

1. 「现在主要是pvp的东西真的很多，也比较独立。比如技能，这应该是从角色的技能存储派生出来的，但是sp显示65535看起来并不对。……我怕现在我们的设计会导致在主要逻辑区域里出现一堆枝干。所以我觉得可能要重新思考pvp模式的架构设计和实现思路」
2. 「先不急着实现，首先要确定落地方案。比如技能，可以在频道里加使用的skill类型，这样就能用技能组来直接拿对应的设置，而不是询问是否是pvp。……」
3. 「先看看技能组是不是支持根据类型拿到技能加点」
4. 「那么这个pvp构建要先暂停了，我们要先把skill部分重构一下。也就是角色的技能和技能相关的如技能放在哪个槽，黑武士的连招存储之类的都拆出来放到一个技能领域服务里。为后续直接获取技能组信息作铺垫。这样合理吗？」

项目协调会话随后转达用户决定：PvP 构建暂停，先做技能领域重构；分两步（纯搬迁行为不变 → 引入技能组）；默认额外 SP/TP 共用、连招页与盾牌组随技能组、技能组设置用静态配置；本阶段只建 issue 与任务契约，不写实现代码。

### Current Situation

基线 `origin/main@317b85bbb`。调研（2026-11-16，见 GitHub issue #358 与 `feature/pvp-duel-phase1` 分支 `Document/tasks/pvp-duel/PvP架构重设计.md`）结论：

- 技能事实没有唯一 owner：学技能、换槽、切页、重置、等级调和、SP 计算、技能书与退点、转职/觉醒补发、技能重放、选角技能包、建角初始化分散在 `Application/Characters`（`CharacterSkillLearningDomainService`、`CharacterSkillStateDomainService`、`CharacterSkillPointDomainService`、`CharacterStateProjection(DomainService)`、`SelectCharacterPreparationService`、`CharacterCurrentStateQueryDomainService`、`CharacterEntryAssembly`、`CharacterRosterService`、`SelectCharacterPersistenceServices.PvpSkillProfileService`）以及 `Effects/ConsumableStateDomainService`、`Effects/ReplayStateDomainService`、`Inventory/SpecialConsumableEffectPlanRuleService`、`Quests/QuestCompletionEffectRuleService`。
- 存储：`character_skills.mode IN ('pve','pvp')` 是唯一带组维度的表；`CharacterSkillTables` 的 6 个普通方法写死 `mode='pve'`，决斗另有只读/只插的两个方法；`character_skill_builds`、`characters.active_skill_build`、`bonus_sp/bonus_tp`、黑暗武士连招页、骑士盾牌组均无组维度；`characters.skill_tree_index` 无写入方（#194）。
- 已与组无关、可复用：`SkillPointLedgerRules.Calculate`、技能规则（花费、免费等级、找空槽）、数据形状 `CharacterSkillsEntryState`、全部技能协议编解码（SKILLINFO 0x0013、BUY_SKILL 0x001D、换槽 0x001C、切页 0x0104）。
- 决斗频道下：选角与当前视图 `if (IsPvp)` 二选一；SP/TP 镜像写死 `ushort.MaxValue`（客户端照数显示 65535）；学技能、换槽、切页一律拒绝；决斗页只首次播种；技能重放与状态刷新始终用普通页。
- 频道只给出布尔值 `ChannelEntryPolicyState.PvpSkillProfileApplies`。

## Goal

角色技能成为一个独立的技能领域：技能相关状态与规则只有这一个 owner，其他领域只经它的接口读写。在此基础上引入技能组：频道声明使用哪个技能组，技能系统按技能组取得该组的技能加点（各页已学技能、等级、槽位、技能树、连招页、盾牌组）与该组设置（能否学技能、换槽、切页，SP 来源，初始化与补发策略，页数上限），主逻辑不再出现 PvP 判断；决斗频道的技能页由此成为普通的另一个技能组，SP/TP 按账本正确显示。

## Scope

### In

- **第 1 步（纯搬迁，行为不变）**：建立技能领域（契约与实现），把上述散落的技能读写、规则与投影收拢进去；其他领域（选角、当前视图、效果、任务、物品、重放、建角、入场装配）改为调用技能领域接口；对外保持现有普通组语义与决斗频道现有行为不变；`characters.skill_tree_index` 无写入方的问题一并收口（#194 相关条目）。
- **第 2 步（引入技能组，语义变更）**：技能组键贯穿存储、领域、账本、重放、效果与补发；以技能组为键的静态设置（随版本发布、规则服务读取）；频道声明使用的技能组（取代 `PvpSkillProfileApplies` 与技能相关的 `IsPvp` 分支）；数据库结构升版本并提供迁移（现有普通数据归默认组，现有决斗数据归决斗组）；合并决斗专用读写；决斗组 SP/TP 按账本计算（消除 65535）。
- 相关文档更新（技能领域说明、`Document/CurrentCharacterFacts.md` 中"自由决斗无限点数"的已证伪记载按跨界修正记载处理）、协议页如有影响则更新、自动测试与架构边界测试。
- 按仓库流程准备用户测试服与编号测试步骤。

### Out

- PvP 战绩、段位、房间、对局及其他 PvP 功能（由 #81 在本任务合入后继续）。
- 快捷栏（热键）存储（默认不纳入，见 Assumptions）。
- 角色基础事实（职业、等级、转职、经验）的归属调整。
- 70 式"按决斗段位授技能"与公平决斗技能树（只要求技能组设置能容纳另一种初始化策略，不实现）。
- 推送、创建 PR、合并、版本号修改。

## Root Intent Coverage

| Intent ID | Source anchor and material intent | Disposition | Contract location or distinct active owner / authority | Rationale and evidence |
|---|---|---|---|---|
| RI-1 | 请求 1：SP 显示 65535 不对 | in_scope | Goal；SC-5 | 根因是决斗技能平行链，第 2 步消除 |
| RI-2 | 请求 1：PvP 不应在主逻辑里长出枝干 | in_scope | Goal；Constraints「边界」；SC-6 | 技能部分在本任务内去除 PvP 分支；其余 PvP 枝干由 #81 重设计承接 |
| RI-3 | 请求 1：重新思考 PvP 架构 | preserved_upstream | GitHub issue #81（open）；`PvP架构重设计.md` | 本任务是其技能部分的前置 |
| RI-4 | 请求 2：频道声明使用的技能类型，用技能组拿设置 | in_scope | Scope In 第 2 步；SC-4 | 用户原话 |
| RI-5 | 请求 4：技能及槽位、黑武士连招等拆进一个技能领域服务 | in_scope | Scope In 第 1 步；SC-1、SC-2 | 用户原话 |
| RI-6 | 请求 4：为后续直接获取技能组信息作铺垫 | in_scope | Scope In 第 2 步；SC-3、SC-4 | 用户原话 |
| RI-7 | 请求 4：PvP 构建暂停 | in_scope | Scope Out 第 1 条 | 用户原话 |
| RI-8 | 协调转达的默认决策：额外 SP/TP 共用、连招页与盾牌组随组、组设置为静态配置 | in_scope | Constraints「已定」 | 项目协调会话转达的用户决定 |
| RI-9 | 仓库流程：独立工作区、先方案后动码、纯搬迁与语义改动分开、用户测试通过前不建 PR、合并与推送须用户确认 | in_scope | Constraints；Requires Confirmation；SC-7 | `AGENTS.md`、`Document/FeatureDevelopment.md`、项目约定 |

## Constraints

- **已定**：额外 SP/TP 各技能组共用；黑暗武士连招页、骑士盾牌组随技能组分开；技能组设置为随版本发布的静态配置，由规则服务读取，不进玩家库。
- **分步**：第 1 步只做搬迁，行为不变，与第 2 步分开提交与审查；第 2 步才改变语义与表结构。一次迁移到位，不留兼容层。
- **边界**：技能领域依赖角色领域的中立事实（职业、等级、转职），角色领域不依赖技能领域内部；技能领域与其调用方不出现 PvP 判断（`IsPvp`、`PvpSkillProfileApplies`、`Pvp`/`Duel` 专用技能方法）。
- **架构**：遵守 `AGENTS.md`、`Document/Architecture.md`、`Document/RefactorPreferences.md`、`Document/DatabaseSchemaMigration.md`；规则输入只经规则服务；同一事实只有一个 owner。
- **运行**：构建与测试控制内存（单进程构建、一次一个测试项目、关闭测试并行）；测试服端口 41000–48999，不重启用户正在运行的服务。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 技能相关状态（各页已学技能与等级、槽位、技能树与当前树、连招页、盾牌组、SP/TP 账本与额外点数）只由技能领域读写；其他领域不直接访问技能存储接口 | 源码与 DI 可达性检查（架构测试，带反例） |
| SC-2 | 第 1 步完成后行为不变：普通频道与决斗频道的技能包字节、学技能、换槽、切页、重置、技能书、转职/觉醒补发结果与重构前一致 | 现有测试全部通过；技能包等字节基线对比；全量测试相对 main 基线无新增失败 |
| SC-3 | 技能存储、领域、账本、重放、效果、补发以技能组为参数工作，没有写死普通组的读写 | 代码与测试 |
| SC-4 | 频道声明技能组；技能组设置（能否学技能/换槽/切页、SP 来源、初始化与补发策略、页数上限）以技能组为键读取；未登记的技能组使启动失败 | 规则与启动测试（带反例） |
| SC-5 | 决斗频道技能页 SP/TP 按账本显示正确值（不再是 65535）；决斗组按其设置允许或拒绝学技能、换槽、切页；转职/觉醒后按设置补发 | 自动测试；用户实机 |
| SC-6 | 技能领域及其调用方中没有 PvP 判断；数据库结构升版本后旧库可迁移，普通数据与决斗数据分别进入对应技能组，迁移前后各页技能不变 | 源码扫描测试；DbMigrate 迁移测试 |
| SC-7 | 测试服基于本分支、使用当前主干实际数据库副本（已备份、已迁移），提供编号测试步骤；用户反馈通过前不创建 PR | 测试服就绪说明与测试步骤文档；用户反馈 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 请求 4「那么这个pvp构建要先暂停了，我们要先把skill部分重构一下……」；项目协调会话转达用户决定：先建 issue 与任务契约，不写实现代码 | 本阶段：在工作区 `E:\Project\DNF\DNF-JP-86-Server-skill`（分支 `refactor/358-skill-domain`，基于 `origin/main@317b85bbb`）建立任务契约并本地提交 |

## Requires Confirmation

- 开始实现（用户过目本契约与方案之后）。
- 停止或替换用户正在运行的服务，或在其运行时写入其数据库。
- 推送分支、创建 PR、合并到 `main`。

## Assumptions

- 快捷栏（热键）不纳入技能领域：它同时放技能与道具，属于界面设置。若用户要求纳入，修订本契约。
- 技能组初始只有两个：默认组（普通）与决斗组；决斗组的设置（能否编辑、SP 来源、补发）在第 2 步方案中给出建议并由用户确认。
- 协议层（技能相关 CMD/NOTI 的编解码）不需要改变布局。
