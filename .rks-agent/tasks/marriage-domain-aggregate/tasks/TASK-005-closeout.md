# TASK-005 — 副本迁移、文档落地与回归清单

## Task Unit Contract

- Revision: 1
- Outcome and references：对应 Plan T-005；Spec RQ-012、AC-007（副本部分）、AC-012、EV-002、EV-003。具体交付四项：
  - 在近期真实运行库的**副本**上执行迁移，并记录结果；
  - 在设计稿开头补上落地状态，写明与实现的偏差；
  - 同步受影响的仓库文档；
  - 交付双端实机回归清单。
- Owned boundary：
  - 设计稿 `Document/MarriageAggregateDesign-20261017.md` 及同目录的 HTML 关系图；
  - 新增回归清单 `Document/MarriageAggregateRegression-20261017.md`；
  - 本任务目录下的 `evidence/`；
  - 内容已过时的婚姻实现说明：只有当它描述了被本次重构改变的服务结构时才改，并在结果中列出改了哪些。
  - **不改**生产代码和测试代码。**不碰**任何运行中的库，以及 `artifacts/` 下的原始文件，只使用 scratchpad 里的副本。
- Required checks：
  - 副本迁移得到预期阶段，异常计数为 0；`integrity_check` 返回 ok；旧表行数不变；
  - 文档中提到的类名和方法名在代码里都能找到。
- Dependencies：TASK-004 已完成。

## Detailed Plan

1. **副本迁移**
   - 副本已经放在会话 scratchpad 的 `mda-dbcopies/`：
     - `npc-shop-stopped.db`，来自 `artifacts/npc-shop-gain-fix-20261017/current-stopped.db`；
     - `wedding-force-stopped.db`，来自 `artifacts/wedding-force-position-20261017/current-stopped.db`。
   - 迁移前统计：每份都有求婚 Engaged 1 条、Rejected 1 条，预约 Reserved 1 条，婚礼 phase=1 1 条，没有 `marriage_relations` 表。
   - 用 worktree 构建出的程序集执行一次 `SqliteDatabase` 初始化。可以写一次性程序，或者用测试项目里的方法，但都不放进仓库。
   - 迁移后预期：一条 Married（ceremony_id 指向该婚礼，married_at 等于 completed_at，心意点来自婚礼行），一条 Closed，异常计数 0。
   - 记录每个阶段的计数、`integrity_check` 结果、旧表行数的前后对比，写入 `evidence/TASK-005-db-copy-migration.txt`（EV-003）。
2. **设计稿落地状态**：在开头加一段，写明以下内容：
   - 状态：已在 `task/marriage-domain-aggregate` 实现，尚未合并；
   - 实现与设计稿的偏差：
     - 接受预约保持两步，新增 `OpenWeddingAsync`；
     - Venue 上是 `PrepareChurchAsync`，没有做成只读的 `ChurchAsync`；
     - Query 上保留了 `IsMarriedAsync` 和 `WeddingPhaseAsync`；
     - 婚礼 store 拆出了宾客 store；
     - 试炼订阅经实体转交给组件；
     - 对方资料由 Eligibility 构造；
     - `LovePoints` 用 `Func<>` 延迟解析，打破 DI 环；
     - 统一判定放在账本，`MustCommitAsync` 返回提交后的关系；
     - `ActiveWeddingsAsync` 返回 ceremony id 列表。
   - 修正清单 B1-1 到 B1-5，引用 TASK-004；
   - 已知限制：
     - 心意点旧列仍在双写，需要后续单独停写；
     - 回退到旧二进制期间推进的阶段，切回新版本后不会补进已有的关系行；
     - 「组件互不依赖」由架构判据守着。
   - 正文保留原样，只在明显会误导读者的地方加「见落地状态」。
   - HTML 关系图按实际的组件和方法名更新（Venue 增加 `PrepareChurchAsync`，Couple 增加 `OpenWeddingAsync`），不重新发布 Artifact。
3. **回归清单**（`Document/MarriageAggregateRegression-20261017.md`）：
   - 以 `Opus-WeddingInvestigation-Report-20261016.md` 的 §6 为骨架。那份报告在主 checkout 里，没有被 git 跟踪，**引用它的步骤，不要复制全文**，并标明它的位置。
   - 补上以下几项：
     - 心意点使用一次、到上限各验证一遍；
     - 祝词簿：宾客写、新人读和删、未入场宾客不能写；
     - 重启之后阶段保持不变（关系表生效）；
     - 旧库升级后已婚角色的 03D0 仍为 4。
   - 每一步写明观察点：日志关键字、03D0 state、金币或物品数量。
   - 明确写出：这份清单由用户执行，Agent 没有做过实机验证。
4. **其它文档**：
   - 检查 `AGENTS.md` 和 `Document/` 下描述婚姻服务结构的文档（如 `MarriageWeddingFlow-20261016.md`）里，有没有现在不成立的类名或结构说法。
   - 有，就按 AGENTS「过期的记载怎么办」的规则处理：带日期的文档在开头补落地状态；现状图类的文档直接改。
   - 没有，就在结果里写明已经检查过、无需修改。

## Result

### Changed

没有改生产代码和测试代码，没有碰 `artifacts/` 原件、运行库和 Host，没有做 git 写操作，`sdd-execution.yaml` 未动。worktree 内的写入只有下面 5 个文件：

| 文件 | 改动 |
|---|---|
| `evidence/TASK-005-db-copy-migration.txt`（新） | EV-003：输入与哈希、迁移程序、迁移前统计、两次打开的输出、逐表前后对比、幂等对比 |
| `Document/MarriageAggregateDesign-20261017.md` | 头部元数据之后插入「落地状态」引用块：今天的形状；偏差表 13 行（Task 列出的 9 项占 10 行，其中「统一判定」与「`MustCommitAsync` / `Stale`」分成两行；外加 `stage` CHECK 放宽到 15、03D0 实际由快照布尔值拼出、未按 4 个 PR 拆分这 3 行）；修正清单 B1-1～B1-5（引用 TASK-004）；三条已知限制；副本迁移结论；回归清单链接。正文只在 11 处加了「见落地状态」：§4 代码里 `ChurchAsync` 与 `RespondReservationAsync` 两行注释、§4.1 三行、§5.3 第 3 条、§5.4 标题、§5.5 订阅注释、§6.1 `stage` CHECK 行、§8 B1、§9 标题。头部「状态：设计稿，未实施」按 `Docs/ItemWorldModel.md` 的做法保留 |
| `Document/MarriageAggregateDesign-20261017.html` | 头部加一行落地说明；计数「11 张表」改为「12 张表」（3+1+2+2+1+1+2 = 12，含新表 `marriage_relations`）。Query 去掉 `ChurchAsync`，加 `IsMarriedAsync / WeddingPhaseAsync`；Couple 加 `OpenWeddingAsync`；Venue 加 `PrepareChurchAsync`。组件职责：Ledger 写明统一判定和心意点写入；StageRules 注明 `WireState` 只有测试调用；Eligibility 加 03F3 对方资料，接口标记由 `c` 改为 `qc`；TrialQuests 改为「经实体转交」；LovePoints 改为「无表，点数由实体经 Ledger 写入，效果服务用 Func 延迟解析」。组件规则第 2、3 条改成实际的 `MarriageRelation` 和两种失败。只改了数据字符串，没有改布局和脚本逻辑；没有重新发布 Artifact |
| `Document/MarriageAggregateRegression-20261017.md`（新） | 回归清单：观察手段（阶段 ↔ 03D0 ↔ 大司祭菜单对照、03D0 正文偏移、日志关键字表、可选 SQL）；R1 旧库升级；按 Opus 报告 §6 第 1～9 步逐步补观察点（只按步骤号引用，不复制原文，注明该报告在主 checkout、未被 git 跟踪）；R2 心意点一次；R3 心意点到上限；R4 祝词簿；R5 重启后阶段不变。开头写明由用户执行、Agent 没有做过实机验证 |
| `Document/MarriageWeddingFlow-20261016.md`（已跟踪，+2 行） | 开头补一句落地状态：「Completed 同时作为当前关系的已婚事实」已不成立，阶段在 `marriage_relations.stage`，心意点以关系行为准、过渡期双写；正文不动 |

scratchpad 里的东西（不进仓库）：`mda-migrate/tool/`（一次性迁移程序）、`mda-migrate/{snapshot,compare}.py`、各次运行的输出和快照、`mda-dbcopies/migrated/`（迁移过的副本）、`mda-task005-full.log`（全量测试日志）。

### Why

- **副本迁移不启动 Host，而是直接构造 `SqliteDatabase`。** 要验证的就是 `CharacterDatabaseSchema.Ensure` 里那一步回填，这正是 `MarriageRelationMigrationTests` 的打开方式。启动 Host 会同时拉起婚礼调度器等后台服务，它们可能在副本上推进状态，还会占端口。启动审计 `MarriageRelationAudit` 没有运行，但它读的计数 `CountUnsettledReservationsAsync` 由程序直接调用了，结果为 0，审计不会写 Warning。迁移在 `mda-dbcopies/` 两份副本的再一层拷贝上做，那两份保持原样，可以重跑。
- **偏差表多出的 3 行是核对代码时发现的，都记成事实，不是要求修改。**
  - 03D0 的线上字节仍由 `MarriageEngagementPayload.Info` 从快照的 `IsMarried`、`IsCeremonyReserved` 拼出。这两个值由实体按阶段填写，取值与 Spec 的投影表一致；但 `MarriageStageRules.WireState` 没有生产调用方。
  - `stage` CHECK 已放宽到 15，设计稿 §6.1 仍写 5。
  - 设计稿 §9 写的是 4 个 PR，实际按用户选定的流程合成一个提交。
- **同样在核对中确认、写进落地状态的「没有生产调用方」**：`IsMarriedAsync`、`ViewAsync`（只有测试调用），以及 `CoupleLedger.CommitAsync`（生产路径只用 `MustCommitAsync`，「阶段不符返回 `Stale`」是改子记录之前的阶段检查）。偏差表里「CAS 失败返回 `Stale`」那一行按这个实际情况写，没有沿用 TASK-002 结果里「`CommitAsync` 失败返回 false」的笼统说法。
- **`ActiveWeddingsAsync` 那一行与设计稿 §4 的签名其实一致**（设计稿本来就写 `IReadOnlyList<Guid>`），变化是相对旧 Wedding 服务返回整行婚礼而言的。Task 要求列出，所以照列，并写明「与 §4 签名一致」。
- **只给 `MarriageWeddingFlow-20261016.md` 补了落地状态**。它是唯一一份写了「已婚从哪张表来」这类被本次重构改变的结构说法的带日期文档，其余文档的检查结论见 Checks。
- **回归清单的心意点上限一项要求改 `marriage_relations.love_points`**：TASK-002 冲突 A 已经证明只改旧列不再生效，所以在清单里明写。

### Checks

**副本迁移（EV-003）**，全部来自实际运行，原始输出在 scratchpad `mda-migrate/`：

- 输入：两份副本与 `artifacts/` 原件 SHA256 一致（9a61a9ba…、314661c0…）。迁移所用的 `Dfo.Server.Persistence.dll`、`Dfo.Server.Application.dll` 取自 worktree 当前构建（`dotnet build`：0 警告 0 错误），SHA256 与 `src/*/bin` 下同名文件一致。
- 迁移前：两份都是 92 张表，没有 `marriage_relations`；求婚 Rejected 1、Engaged 1，预约 Reserved 1，婚礼 phase=1 1；`integrity_check` = ok。
- 迁移后，两份结果相同：
  - 阶段计数 Proposed 0、Engaged 0、Requested 0、Preparing 0、Married 1、Closed 1，合计 2，等于求婚行数；
  - 异常计数（`CountUnsettledReservationsAsync`）0；
  - Married 那条：`ceremony_id` 指向该婚礼，`married_at` 等于 `completed_at`（npc-shop 1792226736402，wedding-force 1792211202138），心意点等于婚礼行（0 / 5），revision 1；Closed 那条的 `ceremony_id`、`married_at` 为空；
  - `integrity_check` = ok；
  - 原有 92 张表逐表比较行数、内容摘要、DDL 摘要，没有一张变化。行数合计不变：npc-shop 36454 → 36454，wedding-force 36654 → 36654。婚姻旧表 11 张逐张行数和内容都相同。没有新增或删除索引、触发器。
- 第二次打开：计数仍为 0，两条 `ReadAsync` 输出逐字段相同（含 `stage_since`），全部 93 张表无任何变化。
- 结束时 `mda-dbcopies/` 两份原样副本 SHA256 未变。

**文档中的类名和方法名**：用脚本取出新增内容（设计稿落地状态块、回归清单、WeddingFlow 落地句里反引号内的标识符，以及 HTML 里形如 `*Async` / `*Service` / `*Store` 等组件名的标识符），逐个 `grep -rlw --include=*.cs` 搜 `Server/src` 与 `Server/tests`（排除 `obj/`）。在代码里搜不到的只有这些：

- `ChurchAsync`、`LoadPair`：写在偏差表「设计稿」一列，本来就是设计稿里的名字、代码里不存在；
- `DeleteGuestbook`：HTML 原有的「Read / Post / DeleteGuestbook」简写，没有改；
- `Opus`、`WeddingInvestigation`、`x03D0`：文件名片段。
- 另外 `TownProjectionSlice` 不是类名，已改写成文件名 `TownProjectionSlice.cs`（该文件存在）。

其余标识符都能搜到。单独核对过的几项事实：

- 组件构造器各自接受的 store：`IMarriageWeddingGuestDomainStore` 只在 `GuestList`，`IMarriageWeddingDomainStore` 只在 `WeddingSettlement`，关系 store 只在 `CoupleLedger`；
- `IsMarriedAsync`、`ViewAsync` 在 `Server/src` 中除接口与实体外零调用，`WeddingPhaseAsync` 只被 `TownProjectionSlice.cs:362` 调用；
- `MarriageStageRules.WireState` 只在 `MarriageRelationLedgerTests.cs` 中被调用；
- `CoupleLedger.CommitAsync` 只在 `MarriageRelationLedgerTests.cs` 中被调用；
- 日志关键字逐条取自 `MarriageProposalSlice.cs`、`MarriageChurchSlice.cs`、`MarriageWeddingSlice.cs`、`CoupleLedger.cs`、`CommandLoggingBehavior.cs` 的日志模板；
- 03D0 正文偏移取自 `MarriageEngagementPayload.Info`；
- 祝词簿各访问路径的返回值取自 `MarriageDomainService.Venue.cs` 的 `GuestbookAccessAsync` 与 `Guestbook.Accepts`；
- 预约待答跨重启按到期处理，依据是 `ReservationLapsed` 用 `MarriageEligibility.IsCurrent`（会话记录相等）判断。

HTML 的脚本部分抽出后用 `node --check` 检查，语法通过。

**检查过的其它文档**：

| 文档 | 结论 |
|---|---|
| worktree `AGENTS.md` | 没有任何婚姻相关的记载（`grep -i "marriage\|婚"` 零命中），无需修改 |
| `Document/MarriageWeddingFlow-20261016.md` | 「Completed 同时作为当前关系的已婚事实」不再成立 → 已在开头补落地状态 |
| `Document/MarriageEngagement-20261016.md` | 「持久 Engaged 才取得婚前任务资格」：今天按关系阶段 Engaged 及之后判定，与原先按求婚状态 Engaged 判定是同一集合（求婚行婚后仍是 Engaged）。没有写到服务结构，无需修改 |
| `Document/MarriageWeddingSixIssues-20261017.md`、`MarriageWeddingSixFixVerification-20261017.md` | 「同一事务加共享心意点」等说法仍成立（心意点仍在同一事务内累加、双方共享），没有写到服务结构，无需修改 |
| `Document/MarriageProposalImplementation-20261016.md`、`MarriageReservation-20261016.md`、`MarriageChurchEntry-20261016.md`、`MarriageChurchEntryEvidence-20261016.md`、`MarriageEngagementCrashFix-20261016.md`、`MarriageTimeoutProtocolEvidence-20261016.md`、`MarriageWeddingFollowupInvestigation-20261016.md`、`MarriageWeddingForcedPosition-20261017.md`、`MarriageWeddingRepair-20261017.md`、`MarriagePhase1Research-20261016.md`、`MarriagePhase1PreDesign-20261016.md` | 只写协议、流程、证据或当时的预设计，不涉及被本次重构改变的服务结构，无需修改 |
| `Document/`、`Docs/`、`protocol/` 全文 | 搜旧 8 个实现类名、7 个旧接口名、`IsReservedAsync`、`MarriedAsync`：除设计稿正文（历史推导）外零命中。`Document/outputs/marriage-research-20261016/reference/reference.md` 里的 `IMarriageQueryDomainService` 出现在当时的「建议接口」中，属于带日期的调查提案，不是现状描述，无需修改 |

**构建与全量测试（EV-002）**：

- `dotnet build Server/86JP.Server.slnx`：0 警告，0 错误。
- `dotnet test Server/86JP.Server.slnx --no-build --logger "console;verbosity=detailed"`：日志在会话 scratchpad 的 `mda-task005-full.log`。8 个项目都显示「测试运行成功」，失败 0；`ObjectDisposedException` 出现 0 次。

  | 项目 | 总数 | 通过 | 跳过 |
  |---|---:|---:|---:|
  | Application | 4016 | 3590 | 426 |
  | Protocol | 1581 | 1521 | 60 |
  | Persistence | 470 | 470 | 0 |
  | Infrastructure | 933 | 596 | 337 |
  | Host | 159 | 149 | 10 |
  | Networking | 92 | 92 | 0 |
  | HttpApi | 50 | 50 | 0 |
  | Pvf | 80 | 71 | 9 |

  各项数字与 TASK-004 相同。这次只改了文档，这次运行是收尾取证。
- `git diff --stat -- Server/tests`：仍是 7 个文件、+177 / −161，与 TASK-004 结束时的逐文件数字相同（6+80+201+28+20+1+2 = 338 = 177+161）。`git status` 中本单元新增或修改的只有上面 5 个文件。

### Remaining

- **实机回归没有执行。** 清单交给用户；Agent 不宣称任何客户端表现。
- **EV-002 的日志留在 scratchpad**（`mda-task005-full.log`），没有复制进 `evidence/`；是否需要归档由控制者决定。
- **`protocol/` 里几处 CurrentImplementation 的措辞仍写着旧的数据来源，没有改。** 线上字节和取值都没变，所以不算被推翻的协议结论；但读者按字面去找实现会找到旧来源。涉及：
  - `NOTI-0x03D0.md`：「独立预约Reserved记录投影state2」，以及「保存completed_at毫秒与love_points」，今天投影读的是关系行的 stage、`married_at`、`love_points`；
  - `patterns/flow-marriage-proposal.md`：action2 那一行「独立预约Reserved，投影03D0阶段2」；
  - `0x03F7.md`：「真实普通档Reserved关系」，今天的判定是关系阶段 Preparing，且引用的正是这份预约。

  不在本 Task 的文档范围内（Task 只要求检查 `AGENTS.md` 和 `Document/`），请控制者决定是否另行补记。
- 回归清单引用的 `Opus-WeddingInvestigation-Report-20261016.md` 只存在于主 checkout、未被 git 跟踪；它若一直不入库，合并后别的 checkout 里这个引用无法打开。

## Controller Decision（round 1）

- **结果接受**。执行者提出的 Remaining 处置如下：
  - **EV-002 全量日志**：不归档 scratchpad 里的日志。验证阶段会重新生成，并把摘要写入 `verification/completion-evidence.md`。
  - **`protocol/` 中三处 CurrentImplementation 措辞**：由控制者补落地注记，原文保留，按 `protocol/README.md` 保留历史的规则处理。改动位置：
    - `NOTI-0x03D0.md` 两处；
    - `0x03F7.md` 一处；
    - `patterns/flow-marriage-proposal.md` 的 action2 一行。
    - 注记都指向设计稿的落地状态。
  - **回归清单引用了未入库的报告**：由控制者把第 2 节改成自包含，每一步写明操作，不再依赖原报告。
- **新发现的缺口：03D0 没有经过阶段投影**。不属于本 Task 的边界，另立 TASK-006 处理。设计稿落地状态的偏差表与 HTML 关系图在 TASK-006 之后同步更新。
