# TASK-002 — 关系记录、回填与账本

## Task Unit Contract

- Revision: 1
- Outcome and references（Plan T-002；Spec RQ-002、RQ-003、RQ-004、RQ-005 写路径、RQ-007、RQ-008）：
  - 每条关系的阶段直接存储；
  - 旧的 8 个服务不再从婚礼 phase 或预约 state 推导「已婚」「已预约」，阶段的读写都经过账本；
  - 已有数据启动时幂等回填；
  - 心意点以关系为准并双写旧列。
  - RQ-005 读路径的「有效阶段」放到 T-003 的只读视图里做，本 Task 只做写路径的到期落库。
- Owned boundary：
  - `Server/src/Dfo.Server.Data/Marriage/`：新增阶段枚举、关系 record；
  - `Server/src/Dfo.Server.Abstractions/Marriage/`：新增关系 store 接口；删除不再使用的推导方法（`IsReservedAsync`、`MarriedAsync`、`IsMarriedAsync`、婚礼 store 的 `AddLovePointsAsync`）；
  - `Server/src/Dfo.Server.Persistence/`：表声明、回填迁移类、关系 store 实现、模块注册，以及上述被删方法的实现；
  - `Server/src/Dfo.Server.Application/Marriage/`：阶段规则、账本、启动审计、8 个服务中阶段相关的读写、`ApplicationModule` 注册；
  - 新增测试：`Server/tests/Dfo.Server.Application.Tests/Marriage/`、`Server/tests/Dfo.Server.Persistence.Tests/Schema/`。
  - **不改**：8 个服务的公开接口签名、Protocol 层、现有测试的断言（含 TASK-001 新增的）、各服务中「是否为一对」的身份判定逻辑（那是 T-004 的事）。
- Required checks：
  - 解决方案构建 0 警告；
  - Application.Tests 婚姻过滤集（基线 74 通过 / 3 跳过）全部通过，再加上本 Task 新增的用例；
  - Persistence.Tests 中的迁移用例；
  - Protocol.Tests 婚姻过滤集（36 条）；
  - `CommandHandlerArchitectureTests`；
  - 全仓搜索：`Server/src` 中不再调用 `IsReservedAsync`、`MarriedAsync`、`IsMarriedAsync`，也不再出现用 phase 或 state 判断「已婚 / 已预约」的业务代码。
- Dependencies：TASK-001 已完成（基线 EV-001，共享构造入口 `MarriageHarness`）。

## Detailed Plan

### 1. 数据与契约

- `Dfo.Server.Data/Marriage`：
  - `enum MarriageStage : byte { Proposed = 0, Engaged = 1, Requested = 2, Preparing = 3, Married = 4, Closed = 5 }`；
  - `MarriageRelation(Guid Id, MarriageStage Stage, long Revision, SelectedSessionIdentity Proposer, SelectedSessionIdentity Recipient, Guid? CeremonyId, DateTimeOffset StageSince)`，附 `MarriedAt`、`LovePoints` 两个 init 属性。双方身份取自现有求婚 payload，不重复存储。
- `Dfo.Server.Abstractions/Marriage`：`IMarriageRelationDomainStore`，方法：
  - 按活跃参与角色读取；按 id 读取；
  - 创建；
  - CAS 迁移：期望的 revision 和阶段 → 新阶段、预约 id、迁移时刻、结婚时刻；
  - 心意点条件累加；
  - 统计未决预约异常（见第 3 点）。
  - 签名细节由实现决定。

### 2. 表与 store

- **表**：在 `CharacterDatabaseSchema.TableDeclarations` 中新增 `marriage_relations`：
  - id 主键并引用 `marriage_proposals(id)`；
  - `stage CHECK BETWEEN 0 AND 15`；
  - revision ≥ 1、`stage_since`；
  - `ceremony_id` 可空，引用 `marriage_ceremonies`；
  - `married_at` 可空；
  - `love_points ≥ 0`，默认 0；
  - `CHECK((stage IN (2,3,4)) = (ceremony_id IS NOT NULL))`。
- **store**：`SqliteMarriageRelationDomainStore` 读取时 join `marriage_proposals.payload_json` 取得双方身份；按角色读取时沿用 `marriage_proposal_participants.active=1`。在 `PersistenceModule` 中注册。
- **心意点双写**：心意点累加在同一次调用中同时更新 `marriage_relations.love_points` 和旧的 `marriage_weddings.love_points`（phase=1 那一行），条件与现有的上限判断相同。这是过渡做法，在 T-005 / 设计稿里记为「待后续停写」。

### 3. 回填

- **迁移类**：新增 `Schema/MarriageRelationMigration.Apply(connection)`，在 `Ensure` 中排在 `Reconcile` 之后、索引之前，与其它迁移类同一个风格。
- **写法**：一条 `INSERT OR IGNORE … SELECT`，按 Spec 回填表的优先级取值：Married > Preparing > Requested > Engaged；Rejected / Expired → Closed；Pending → Proposed。
- **具体规则**：
  - **Married**：`ceremony_id` 取 phase=1 婚礼的 ceremony；`married_at` 取 `completed_at`，为 0 时写 NULL；`love_points` 从该婚礼行复制。
  - **Preparing**：`ceremony_id` 取 phase=0 婚礼的 ceremony。
  - **Requested**：`ceremony_id` 取 state=0 预约的 id。
  - `stage_since` 写迁移时刻（迁移执行时取一次当前时间），revision = 1。
- **异常**：指 Engaged 关系中，其活跃预约为 Reserved（state=1），但对应婚礼行不存在或为 Cancelled 的情况（这类记录可能来自 TASK-001 怪癖 Q3）。回填按 Engaged 处理；关系 store 提供计数查询。
- **审计**：Application 新增一个启动审计 hosted service，经账本调用计数查询，大于 0 时写 Warning。迁移本身仍不依赖日志。

### 4. 阶段规则与账本

- **阶段规则**：`MarriageStageRules`（static，纯函数）：
  - `Next(MarriageStage? from, MarriageTrigger trigger) → MarriageStage?`，按 Spec 迁移表 T1–T9 实现，不在表中的组合返回 null；
  - `WireState(MarriageStage?) → byte`，即 03D0 投影。
- **账本**：`CoupleLedger`（internal sealed，以具体类型注册为 singleton），依赖关系 store、`TimeProvider`。提供：
  - 按角色读取，**不做账号核验**：保持 T-004 之前各服务原有的身份判定；
  - `CreateAsync`：T1，在求婚创建的同一事务内调用；
  - `CommitAsync(relation, trigger, ceremony?, marriedAt?)`：先用阶段规则校验，再执行 CAS。CAS 失败时返回失败；调用方据此返回 `Stale`，或在子记录已经迁移时抛异常回滚，见第 5 点；
  - 心意点累加；
  - 异常计数。

### 5. 旧服务的接线（只改阶段读写，不改身份判定和接口）

| 服务 | 改动 |
|---|---|
| Proposal | `ProposeAsync` 创建求婚后 `CreateAsync`（T1）；`RespondAsync` 按结果提交 T2 / T3 / T4；`ActiveAsync` 惰性过期落库时提交 T4 |
| Ceremony | `RequestAsync` 创建预约后提交 T5（ceremony id）；`RespondAsync` 接受成功提交 T6 → Preparing（见下方说明），拒绝或到期提交 T7；`FailAndExpireAsync` 和 `ActiveAsync` 惰性过期提交 T7；「已婚」判断改读阶段 |
| Wedding | `PrepareAsync` 的守卫改为「阶段 = Preparing 且引用这份预约」，替代「ceremony Reserved 且未婚」；`AdvanceAsync` 完成提交 T8（married_at = finishedAt），取消提交 T9；`IsMarriedAsync` 改读阶段 |
| Church | `ReservedAsync` 改为「阶段 = Preparing」，经关系上的 `ceremony_id` 读取预约，保留同一对与 tier 的校验 |
| Engagement | IsMarried / MarriedAt / LovePoints 取自关系；`IsCeremonyReserved = 阶段 == Preparing` |
| LovePoint | 已婚判断改读阶段，点数取自关系，累加经账本 |
| Guestbook、Quest | 「关系有效」改为「阶段 ∉ {Proposed, Closed}」，与原先 proposal state == Engaged 等价 |

- **T6 时机**：T6 在 Ceremony 接受时就提交 Preparing，而不是等 `PrepareAsync`。原因是现有测试单独调用接受、并断言「未扣费」「IsCeremonyReserved = true」，生产路径则由命令处理器在同一事务里紧接着调用 `PrepareAsync`。I3 只在生产命令路径的提交点断言。
- **CAS 失败的处理**：
  - 子记录（求婚 / 预约 / 婚礼）的 CAS 已经成功、关系 CAS 却失败，说明发生了并发不一致，抛 `InvalidOperationException` 让事务整笔回滚；
  - 关系 CAS 在任何子记录变更之前就失败，返回现有的 `Stale`。
- **注入方式**：旧服务通过构造器注入 `CoupleLedger`。Church 现在把 Wedding store 作为可空参数注入，改为必需注入账本即可。

### 6. 新增测试

- **阶段规则**：6 个阶段（另加「无关系」）× 全部触发的穷举表；`WireState` 的全部映射。
- **账本**：CAS 冲突（同一 revision 第二次提交失败，阶段不变）。
- **迁移与行为**：
  - T1–T9 各经由现有服务或命令路径走一遍，断言关系阶段和 `ceremony_id`；
  - I3、I4：经 `MarriageCeremonyCommand` 走完 T6 后，检查有且只有一条 phase=0 婚礼；T8 后一条 phase=1；T9 后为 Engaged 且 `ceremony_id` 为 NULL。
- **心意点**：使用后两列相等。
- **Persistence 迁移**（`Persistence.Tests/Schema/MarriageRelationMigrationTests.cs`）：
  - 用原始 SQL 构造旧形状数据（含没有 relations 表的旧库），每个回填情形一行；
  - 打开 `SqliteDatabase`，断言阶段、ceremony、married_at（包括 0 → NULL）、心意点；
  - 断言异常计数；
  - 再次打开，结果不变；
  - `PRAGMA integrity_check` 返回 ok；
  - 旧表的行数和内容不变。
- 风格参照现有 `MarriageWeddingMigrationTests`。

### 7. 执行顺序

1 → 2 → 3（附迁移测试）→ 4（附规则 / 账本测试）→ 5 逐个服务接线，每接一个就跑一遍婚姻过滤集 → 6 → 全部检查。

## Result

### Changed

生产代码（`Server/src`）：

| 位置 | 要点 |
|---|---|
| `Dfo.Server.Data/Marriage/MarriageRelationModels.cs`（新） | `MarriageStage : byte`（0–5，6–15 预留）；`MarriageRelation(Id, Stage, Revision, Proposer, Recipient, CeremonyId, StageSince)`，另有 `MarriedAt`、`LovePoints` |
| `Dfo.Server.Abstractions/Marriage/MarriageRelationContracts.cs`（新） | `IMarriageRelationDomainStore`：`FindActiveAsync(character)`、`ReadAsync(id)`、`CreateAsync`、`TransitionAsync(expected, stage, ceremony, at, marriedAt)`（按 revision 和原阶段做 CAS）、`AddLovePointsAsync`（条件累加并双写旧列）、`CountUnsettledReservationsAsync` |
| `Dfo.Server.Abstractions/Marriage/MarriageProposalContracts.cs`、`MarriageWeddingContracts.cs` | 删除 `IMarriageCeremonyDomainStore.IsReservedAsync` 和 `IMarriageWeddingDomainStore.AddLovePointsAsync`（`MarriedAsync` / `IsMarriedAsync` 没删，见 Remaining 4） |
| `Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` | 新表 `marriage_relations`（按 Detailed Plan §2，`stage CHECK BETWEEN 0 AND 15`）；`Ensure` 在 `MigrateMailboxGold` 之后、建索引之前调用回填 |
| `Dfo.Server.Persistence/Schema/MarriageRelationMigration.cs`（新） | 一条 `INSERT OR IGNORE … SELECT`。优先级 Married > Preparing > Requested > Engaged；Rejected/Expired → Closed；Pending → Proposed。`married_at` 取 `completed_at`，0 写 NULL；心意点取 phase=1 婚礼行；`stage_since` 只取一次 `UtcNow`；revision = 1 |
| `Dfo.Server.Persistence/Marriage/SqliteMarriageRelationDomainStore.cs`（新） | 读取时 join `marriage_proposals.payload_json` 取双方身份；按角色读沿用 `marriage_proposal_participants.active=1`。心意点：先条件更新关系行（stage=4 且不超上限），成功后把 phase=1 婚礼行的 `love_points` 设成关系行的值。异常计数：stage=1、有 state=1 的预约、该预约没有 phase∈(0,1) 的婚礼行 |
| `SqliteMarriageCeremonyDomainStore.cs`、`SqliteMarriageWeddingDomainStore.cs` | 删除上面两个方法的实现 |
| `Dfo.Server.Persistence/PersistenceModule.cs` | 注册关系 store |
| `Dfo.Server.Application/Marriage/MarriageStageRules.cs`（新） | `MarriageTrigger`（9 个，对应 T1–T9）；`Next(from, trigger)` 按迁移表计算，表外组合返回 null；`WireState`；`IsCouple`（阶段 ∉ {Proposed, Closed}） |
| `Dfo.Server.Application/Marriage/CoupleLedger.cs`（新） | `CoupleLedger`（internal sealed，singleton）：`FindAsync`、`ReadAsync`、`ReadForAsync`（读取并预检迁移表）、`CreateAsync`（T1）、`CommitAsync`（规则校验加 CAS，失败返回 false）、`MustCommitAsync`（子记录已变更后调用，失败抛 `InvalidOperationException`）、`AddLovePointsAsync`、`CountUnsettledReservationsAsync`。同一文件里有 `MarriageRelationAudit : IHostedService`，计数大于 0 时写 Warning |
| `Dfo.Server.Application/ApplicationModule.cs` | 注册 `CoupleLedger` 和 `MarriageRelationAudit` |
| 8 个服务 | 按 Detailed Plan §5 接线，见下表 |

服务接线。公开接口签名都没变，身份判定逻辑原样保留：

| 服务 | 改动 |
|---|---|
| Proposal | `ProposeAsync` 建完求婚后调 `CreateAsync`（T1）。`RespondAsync` 先读关系，不是 Proposed 就返回 `Stale`；子记录 CAS 成功后按答复提交 T2 / T3 / T4；到期分支在 `changed` 时提交 T4，Q1 的返回值不变。`ActiveAsync` 惰性过期时提交 T4 |
| Ceremony | 构造器去掉求婚 store 和婚礼 store，改为注入账本。`RelationAsync` 改读关系：用 `IsCouple` 代替 `State == Engaged`，同 id 判定和账号+角色的正反向判定不变。「已婚」改为 `Stage == Married`。`RequestAsync` 在 `ActiveAsync` 之后用 `ReadForAsync` 重读关系，再建预约、提交 T5。`RespondAsync` 先校验「关系是 Requested 且引用这份预约」，否则返回 `Stale`；接受提交 T6；拒绝、客户端超时、到期、复核失败（`FailAndExpireAsync`）提交 T7。`ActiveAsync` 惰性过期时提交 T7 |
| Wedding | `IsMarriedAsync` 改读阶段。`PrepareAsync` 的守卫改为「阶段 = Preparing 且 `CeremonyId` 等于这份预约」。`AdvanceAsync` 在 `FinishAsync` 成功后提交 T8（`married_at = finishedAt`）或 T9 |
| Church | 构造器第一个参数从求婚 store 改为必需的账本；婚礼 store 仍是可空参数（宾客分支要用）。`ReservedAsync` 改为「阶段 = Preparing」；预约仍经 `FindActiveByRelationAsync` 取，用「`Id == relation.CeremonyId` 且 tier 为 0」代替 `State == Reserved`；同一对的校验保留 |
| Engagement | 构造器只剩账本、角色、库存三个依赖。`IsMarried`、`MarriedAt`、`LovePoints` 取自关系；`IsCeremonyReserved = Stage == Preparing` |
| LovePoint | 构造器去掉求婚 store 和婚礼 store。`AccountsAsync` 里的「关系有效」改为 `IsCouple`。`UseAsync` 的 -3 改为「关系不是 Married」；点数和上限判断读关系；累加经过账本 |
| Guestbook、Quest | 「关系有效」改为 `IsCouple`，其余不变 |

新增测试：

| 文件 | 用例 |
|---|---|
| `Server/tests/Dfo.Server.Application.Tests/Marriage/MarriageRelationLedgerTests.cs` | 共 14 条：`Every_stage_and_trigger_pair_follows_the_transition_table_or_is_refused`（7 × 9 穷举）；`Wire_state_projects_each_stage`（7 条 InlineData）；`A_second_commit_from_the_same_revision_is_refused_and_leaves_the_stage`；`Proposal_answers_and_expiry_move_the_relation_through_proposed_engaged_and_closed`（T1、T2、T3，以及 T4 的显式到期和惰性到期）；`Ceremony_and_wedding_transitions_carry_the_reservation_and_keep_one_wedding_per_stage`（T5、T7 拒绝与到期、经 `MarriageCeremonyCommand` 走命令总线的 T6、T9、T8，以及 I3 / I4 的婚礼行计数）；`A_relation_out_of_step_with_its_ceremony_is_stale_before_the_ceremony_changes`；`Using_a_love_coupon_writes_the_same_points_to_the_relation_and_the_legacy_wedding_row`；`Startup_audit_warns_only_when_an_engaged_relation_still_holds_a_reserved_ceremony` |
| `Server/tests/Dfo.Server.Persistence.Tests/Schema/MarriageRelationMigrationTests.cs` | `Every_backfill_case_gets_its_stage_once_and_old_tables_stay_untouched`。用没有 relations 表的旧库，11 条求婚覆盖回填表的每一行，包括：completed_at=0；带一场已取消旧婚礼的 Married；Q3 形状（已取消婚礼 + 仍为 Reserved 的预约）；Reserved 但没有婚礼行。断言内容：阶段、ceremony、married_at、心意点、revision；`stage_since` 只有一个值，且落在打开前后两个时刻之间；异常计数 = 2；旧四张表内容不变。第二次打开后结果不变，手工改过的 revision 被保留。另有两条 CHECK 断言，`integrity_check` = ok |

没有改动任何现有测试文件（包括 TASK-001 新增的 6 个），没有改 Protocol 层和 `sdd-execution.yaml`，没有做任何 git 写操作。

### Why

- **T6 在接受预约时提交**（照 Plan）。harness 的 `ReserveAsync` 和多条现有用例都是单独调 `RespondAsync(…, 0)`，然后断言 `IsCeremonyReserved`。生产路径上，`MarriageCeremonyCommandHandler` 在同一事务里紧接着调 `PrepareAsync`，失败就整笔回滚。
- **两种失败的分界**。每个写入口在改子记录之前，先读关系并确认它能接这个触发：Proposal 要求 Proposed；Ceremony 应答要求「Requested 且引用这份预约」；T5 用 `ReadForAsync`。不满足就返回现有的 `Stale`。子记录 CAS 成功以后一律调 `MustCommitAsync`，关系 CAS 失败就抛异常，让事务回滚。惰性过期路径（两个 `ActiveAsync` 和 Wedding 的 `AdvanceAsync`）没有失败码可返回，只走后一种。
- **`RequestAsync` 重读关系**。`ActiveAsync` 可能刚提交过 T7，手上那份关系的 revision 已经旧了，所以建预约前用 `ReadForAsync` 重读一次。
- **Church 用 id 比对代替 state 比对**。阶段 = Preparing 已经表达了「已预约」，再看预约的 state 等于第二次推导；改为核对预约正是关系引用的那一条。Plan 要求保留的 tier 和同一对校验都还在，`A_reserved_payload_bound_to_another_pair_cannot_allocate_or_reuse_a_church` 照常通过。
- **构造器去掉不再使用的 store**。构造器不是公开接口；留着没用的参数，只会让 T-003 的注入判据更难写。
- **心意点双写的写法**。关系行条件累加成功后，把旧列设成关系行的值，而不是两边各加 amount。这样即使旧列被别处改过，两列也不会继续分叉。心意点不是阶段，所以不推进 revision。
- **回填只用一条语句**。一条 `INSERT OR IGNORE … SELECT` 本身幂等、原子，已经存在的关系行（包括运行期推进过的阶段）不会被覆盖。迁移类不依赖日志，异常只由 store 计数，再由启动审计写 Warning。
- **关系身份不另外存**。读关系时 join 求婚 payload，满足「双方身份取自现有求婚 payload，不重复存储」。
- **RQ-005 的读路径没做**。Task 契约把「有效阶段」留给 T-003；本 Task 只在写路径上把到期落库。

### Checks

下面的数字都来自这次实际运行，工作目录是 worktree 根。

- 构建：`dotnet build Server/86JP.Server.slnx` → `0 个警告`、`0 个错误`。
- Application 婚姻过滤集：`dotnet test Server/tests/Dfo.Server.Application.Tests --no-build --filter "FullyQualifiedName~Marriage" --logger "console;verbosity=detailed"` → 总数 91，通过 85，**失败 3**，跳过 3（都是缺 PVF 的 Skippable）。91 = 基线 77 + 新增 14。新增的 14 条全部通过；失败的 3 条都是心意点种子冲突，见 Remaining 1。
- Persistence 迁移：`dotnet test Server/tests/Dfo.Server.Persistence.Tests --no-build --filter "FullyQualifiedName~MarriageRelationMigrationTests|FullyQualifiedName~MarriageWeddingMigrationTests"` → 2 / 2 通过。范围更宽的 `FullyQualifiedName~Migration` 过滤 → 通过 18，无失败。
- Protocol 婚姻过滤集：`dotnet test Server/tests/Dfo.Server.Protocol.Tests --no-build --filter "FullyQualifiedName~Marriage"` → 36 / 36 通过。
- `CommandHandlerArchitectureTests`：`--filter "FullyQualifiedName~CommandHandlerArchitectureTests"` → 13 / 13 通过。
- 变异确认。做法是改一处 → 构建 → 跑对应用例 → 从备份恢复，并用 `cmp` 确认与备份一致：
  1. 回填条件 `m.completed_at>0` 改成 `>=0` → 迁移用例失败（`p05` 的 married_at 变成 `0`）。已恢复。
  2. 关系 store 双写条件 `phase=1` 改成 `phase=9` → `Using_a_love_coupon_…` 失败（Expected 1，Actual 0）。已恢复。
  3. 删掉 Ceremony 到期分支的 T7 提交 → `Ceremony_and_wedding_transitions_…` 失败（Expected Engaged，Actual Requested）。已恢复。
- 全量测试（取证用，不是本 Task 的必需检查）：`dotnet test Server/86JP.Server.slnx --no-build --logger "console;verbosity=detailed"`，日志在会话 scratchpad 的 `full.log`，不在仓库里。共失败 5 条：上面 3 条婚姻用例，加上 `EffectOperationReplayTests.Love_coupon_real_dispatcher_lost_ack_replays_current_inventory_and_points_without_second_deduction` 和 `SchemaOwnershipTests.EveryTableIsDeclaredSomewhereRatherThanByAccident`，都是已识别的冲突（Remaining 2、3）。日志里没有 `ObjectDisposedException`。
- 旧推导方法搜索（`Server/src`，排除 `obj/`）：
  - 调用点：`grep -rn "\.IsReservedAsync(\|\.MarriedAsync(\|\.IsMarriedAsync(" Server/src` → **零行**。
  - 仍有的声明：`IMarriageWeddingDomainStore.MarriedAsync` / `IsMarriedAsync` 及其 Persistence 实现（见 Remaining 4）。`IMarriageWeddingDomainService.IsMarriedAsync` 是公开接口，按边界保留，实现已改为读阶段。`IsReservedAsync` 和婚礼 store 的 `AddLovePointsAsync` 已经不存在。
  - Application 里剩下的 `MarriageProposalState` / `MarriageCeremonyState` / `MarriageWeddingPhase` 读写，都属于子记录自己的明细：求婚和预约的待答幂等与 CAS，以及单场婚礼的 phase（请柬、礼金、离场锁、外观、清理、宾客进教堂、祝词簿排除已取消的婚礼）。已经没有拿它们推导关系「已订婚 / 已预约 / 已婚」的判断。

### Remaining

1. **冲突 A：三条用例只往旧列写心意点作种子，与 RQ-008「心意点以关系为准」冲突。** 这三条用例都用原始 SQL 改 `marriage_weddings.love_points`，再断言业务读到这个值。现在业务读的是关系行，旧列只是双写目标：
   - `MarriageEngagementCharacterizationTests.Wedding_info_state_byte_follows_each_relationship_stage`（TASK-001 新增）：`:61` 写入 7，`:62` 断言 03D0 心意点为 7，实际为 0。
   - `MarriageCoupleCharacterizationTests.Love_point_refusals_answer_minus_one_two_or_three_by_cause`（TASK-001 新增）：`:77` 写入 998，`:78` 断言 `Points == 999`，实际为 2。
   - `MarriageChurchTests.Love_coupon_requires_marriage_consumes_once_updates_shared_points_and_rolls_back`：`:508` 写入 999，`:509` 断言使用会失败，实际成功（关系行是 2）。`:511` 断言重开后旧列为 999，而那次成功的使用已经把旧列双写成关系行的值。

   说明这不是接线错误：三条用例中，种子行之前的断言全部通过，包括 03D0 的 0/1/2/4 和结婚时刻、-1 / -3 的分因、首次使用的 `Points == 1` 和对方角色、`Engagement.LovePoints == 1`。可选的处理方式：
   - (a) 只改这三处种子行，让它们同时写 `marriage_relations.love_points`。改的是准备步骤，不是断言，但仍需控制者批准。
   - (b) 加一个 SQLite 触发器，把写进旧列的值同步到关系行。这等于把旧列留作第二个可写的真源，偏离了共享决策。

   建议选 (a)。
2. **冲突 B：`EffectOperationReplayTests.Love_coupon_real_dispatcher_lost_ack_replays_current_inventory_and_points_without_second_deduction` 直接用 store 拼出已婚状态，绕过了账本。** 这条用例不在婚姻过滤集里。它用求婚 `CreateAsync` + `TransitionAsync`、预约 `CreateAsync`、婚礼 `CreateAsync` + `FinishAsync` 拼出状态，所以没有关系行，心意券不会被消费：`:62` 期望剩 2 张，实际剩 3 张。需要在它的准备步骤里补一条 Married 关系行，或者改为走服务；同样需要控制者批准。
3. **冲突 C：`SchemaOwnershipTests.EveryTableIsDeclaredSomewhereRatherThanByAccident` 在 `:58` 把表数固定为 92**，加了 `marriage_relations` 后变成 93。这个计数器按设计就应该随加表一起更新，改动是机械的（92 → 93，还可以加一条 `Contains("marriage_relations(")`）。但它是现有断言，留给控制者批准。
4. **`IMarriageWeddingDomainStore.MarriedAsync` / `IsMarriedAsync` 没有删除。** `Server/src` 里已经没有调用点，但 `MarriageChurchTests.cs:353`、`:354`、`:511` 的断言直接调用它们：删掉就编译不过，而断言又不能改。建议在 T-003 改测试接线时一起删掉，或者批准把这三处改为读阶段、读旧列 SQL。
5. Protocol 层还有对子记录状态的读取，超出本 Task 边界，没有动：`MarriageCeremonyCommand.cs:25` 用 `State == Reserved` 决定是否调 `PrepareAsync`；`MarriageProposalSlice`、`MarriageEngagementSlice`、`TownProjectionSlice` 读取结果明细。
6. 未覆盖：「子记录已迁移、关系 CAS 失败、抛异常回滚」这条路径没有专门的用例，因为要在读关系和提交之间插入一次并发写。I5 的架构判据按 Plan 属于 T-003。
7. 过渡期的限制，留给 T-005 写进设计稿：回填只补缺失的关系行，不修正已有的行。回退到旧二进制期间发生的阶段推进和心意点累加（旧二进制只写旧列），切回新二进制后不会反映到已有的关系行上。RQ-008 的双写只保证从新版本退回旧版本时不丢点数。

## Controller Decision（round 1）

处置：`task-rework`，由控制者直接完成。依据是执行者 Remaining 中的冲突 A–C。

- **冲突 A、B（心意点种子）**
  - 判定：被测行为没有冲突。冲突来自测试准备步骤用原始 SQL 绕过领域层，只写了旧列或旧表。
  - 允许的改动：只修改准备步骤，断言不动。
  - `MarriageEngagementCharacterizationTests`、`MarriageCoupleCharacterizationTests`、`MarriageChurchTests` 的种子 SQL 同时写入 `marriage_relations.love_points`。
  - `EffectOperationReplayTests` 在拼出已婚状态后补一条 Married 关系行。
  - 不采用触发器方案：它会留下第二个可写真源。
- **冲突 C**：新增一张声明表后，`SchemaOwnershipTests` 的表数从 92 改为 93。这是机械改动，是新表的直接结果。
- **第 4 项**：婚礼 store 的 `MarriedAsync` / `IsMarriedAsync` 推迟到 T-003，与测试接线一起删除。届时 AC-002 的搜索要以「`Server/src` 零调用」为准。
- **其它遗留**，带入后续单元：
  - Protocol 层 `MarriageCeremonyCommand.cs:25` 读取预约结果的 state → T-003 切换接口时一并处理；
  - 「子记录已迁移、关系 CAS 失败、回滚」的专门用例 → T-003 的账本 / 组件测试；
  - 回退期间的阶段漂移 → T-005 写进设计稿。

### Checks（控制者复核，round 1 改动之后）

- `dotnet build Server/86JP.Server.slnx`：0 个警告，0 个错误。
- `dotnet test Server/86JP.Server.slnx --no-build --logger "console;verbosity=detailed"`：全部测试项目都显示「测试运行成功」，失败 0。
  - Application：4006 条，通过 3580，跳过 426；
  - Protocol：1581 条，通过 1521，跳过 60；
  - Persistence：470 条，全部通过；
  - Infrastructure：933 条，通过 596，跳过 337；
  - Host：159 条，通过 149，跳过 10；
  - Networking：92 条，全部通过；
  - HttpApi：50 条，全部通过；
  - Pvf：80 条，通过 71，跳过 9。
  - 跳过的都是 Skippable 用例（本机缺 PVF 或客户端档案）。
  - 日志位于会话 scratchpad 的 `mda-task002-full.log`。
- 测试改动的 diff：`MarriageChurchTests.cs`、`EffectOperationReplayTests.cs`、`SchemaOwnershipTests.cs` 共 3 行，外加两份 TASK-001 新增文件中的种子行；断言零改动。
