```yaml
kind: rks-review
verdict: approved
```

# 完整结果审查：婚姻单一实体与阶段记录

## 目标与范围

- 分支 `task/marriage-domain-aggregate`，HEAD = 基线 `5a0cbed0`，审查对象是全部未提交改动。
- 审查时 `git status --short`：33 个 M、8 个 D（8 个旧婚姻领域服务文件）、36 个 ??。未跟踪的包括本任务目录、设计稿 md 与 html、回归清单、15 个新的生产文件和 11 个新的测试文件。
- 目的：SDD review 阶段的完整结果审查，产出是代码加文档。
- 治理依据：
  - `TaskContract.md`（revision 3，含 Amendments）、`spec.md`、`plan.md`；
  - `tasks/TASK-001…006`（只作为证据指针）；
  - 仓库 `AGENTS.md`；
  - 设计稿 `Document/MarriageAggregateDesign-20261017.md`（含落地状态）。
- 所用准则：`reviewing-work` SKILL 及其 `references/code.md`、`references/deliverable.md`。产出是代码加文档，两套判准都用，并核对文档与代码是否一致。
- 审查者独立于产出者，只读审查。唯一写入的是本文件。

## 决定性证据（本次独立取得）

1. **构建与全量测试**
   - `dotnet build Server/86JP.Server.slnx`：0 警告，0 错误。
   - `dotnet test Server/86JP.Server.slnx --no-build --logger "console;verbosity=detailed"`，日志在会话 scratchpad 的 `review-full.log`。8 个测试项目全部「测试运行成功」，失败 0：

     | 项目 | 通过 | 跳过 |
     |---|---:|---:|
     | Application | 3583 | 426 |
     | Protocol | 1528 | 60 |
     | Persistence | 470 | 0 |
     | Infrastructure | 596 | 337 |
     | Host | 149 | 10 |
     | Networking | 92 | 0 |
     | HttpApi | 50 | 0 |
     | Pvf | 71 | 9 |

   - `ObjectDisposedException` 出现 0 次。Host.Tests 这次没有端口假红。
   - `Application.Tests.Marriage` 命名空间 88 条通过、3 条 PVF Skippable 跳过，与 TASK-004 的 95 条减去移到 Protocol 的 7 条 `Wire_state_projects_each_stage` 相符。
2. **与基线逐行对照生产代码**：从 `5a0cbed0` 取出原 8 个服务，与新实体逐个方法比对。范围包括：
   - 失败码顺序：Proposal、Ceremony 的每一步；
   - 副作用顺序：开婚礼时先教堂准备、入场，再扣费、建婚礼行；到点完成或取消时的站位、回礼、礼金分成、退款、离场坐标和清理队列；
   - Wedding、Church、Guestbook、LovePoint、Quest、Engagement 的守卫。

   除下文列出的刻意修正和已记录的偏差外，没有发现清单外的行为变化。
3. **旧名与旧推导的搜索**（`Server/src`，排除 `obj/`）：
   - 旧 8 个实现类名：0 处。
   - 除 `IMarriageQuestDomainService` 外的 7 个旧接口名：src 和 tests 都是 0 处。
   - `IsReservedAsync`、`MarriedAsync`：0 处。
   - Application 和 Protocol 里剩下的 42 处 `MarriageWeddingPhase` / `MarriageCeremonyState` / `MarriageProposalState` 引用，都是子记录自身的明细，或者 Protocol 读本次命令结果。没有一处拿它们推导「已订婚 / 已预约 / 已婚」。
4. **测试断言的改动**：用改名规则归一化后，对已跟踪的测试文件逐行 diff 基线。
   - `MarriageCeremonyTests`：残差 0。
   - `MarriageChurchTests`：只剩以下几类：
     - `Ask`、`Pair` 两个取服务 helper，以及一行 `using`；
     - 353、354、511 三处（TASK-003 第 7 点已批准）；
     - 508 种子行（TASK-002 Controller Decision 已批准）；
     - `EnterGuestAsync` 改为 `EnterChurchAsync(…, 1)` 的接线。
   - Proposal、Engagement、CeremonyHandler 三个测试文件只改了类型名和方法名。
   - `EffectOperationReplayTests` 多了 1 行种子，`SchemaOwnershipTests` 的表数从 92 改为 93，都在已批准范围内。
   - codec 测试只改了两处快照构造（TASK-006）。
   - 未跟踪的测试文件：scratchpad 里有 TASK-004 开工前的副本，与当前逐文件对比。差异只有新增的 `MarriagePairCorrectionTests.cs`，以及 TASK-006 把 `Wire_state_projects_each_stage` 移出 `MarriageRelationLedgerTests`。TASK-001 当时的版本没有留存，无法逐字核对，这部分采信 TASK-003 结果中的接线脚本证据。
5. **回填 SQL**（`MarriageRelationMigration`）对照 Spec 回填表逐行检查：
   - Rejected / Expired → 5；Pending → 0；
   - Engaged：Married（`m.ceremony_id`，`completed_at>0` 才写，心意点取婚礼行）> Preparing（`w.ceremony_id`）> Requested（state=0 预约的 id）> 其它 Engaged。Reserved 但没有婚礼行的，`ceremony_id` 为 NULL。
   - 三个 LEFT JOIN 各自至多命中一行，由现有唯一索引保证：`ux_marriage_ceremonies_active_relation`（state IN (0,1)）、`ux_marriage_weddings_married`（phase=1），以及 phase=0 婚礼必然挂在唯一那条活跃预约上。所以 `INSERT OR IGNORE` 不会因为重复行而任意取值。
   - `MarriageRelationMigrationTests` 的 11 条求婚覆盖了每一行，包括 `completed_at=0`、Q3 形状和第二次打开幂等。
   - 副本迁移证据见 `evidence/TASK-005-db-copy-migration.txt`。
6. **迁移表与不变量**
   - `MarriageStageRules.Next` 与 T1–T9 一致，穷举测试是 7 × 9。
   - `CoupleLedger.TryCommitAsync` 按目标阶段决定引用：Requested 取新预约，Preparing / Married 沿用原引用，其余置空。加上表上的 `CHECK((stage IN (2,3,4)) = (ceremony_id IS NOT NULL))`，I2 在结构上成立。
   - CAS 的条件是 `stage=@old AND revision=@revision`。
   - I3、I4 在 T6、T8、T9 后核对婚礼行数（`Ceremony_and_wedding_transitions_…`）。
   - I1 由 `ux_marriage_proposal_character` 保证。
   - 两种 CAS 失败路径：
     - 改子记录之前先检查阶段，不接受就返回 `Stale`（`A_relation_out_of_step_…`）；
     - 子记录改过之后，`MustCommitAsync` 失败就抛异常、整笔回滚（`A_relation_that_moved_after_its_child_record_changed_…`）。
7. **分层与注入**
   - Application 没有引用 Protocol，只有 csproj 里对 Protocol.Tests 的 `InternalsVisibleTo`，这是原本就有的。
   - 8 个婚姻 store 接口在 `Server/src` 中各自只被一个组件构造器接受；关系 store 只在 `CoupleLedger`。
   - 实体构造器只接 9 个组件和 `TimeProvider`。
   - `MarriageArchitectureTests` 的反例用例确实覆盖了每一类违规：实体接 store、实体接领域服务、组件接组件、第二个类型接关系 store。正向判据另外断言组件数恰为 9，所以把非组件的具体类塞进实体构造器也会被拦下。
   - `LovePoints` 的 `Func<IApplyEffectsDomainService>`：从根 provider 延迟解析，`IApplyEffectsDomainService` 本来就是 singleton，与基线里旧心意点服务直接注入它的生命周期一致；只在 `SpendAsync` 运行时调用，那时实体早已构造完毕。没有发现问题。
8. **线上字节**：03D0 的 state 现在由 Protocol 的 `MarriageEngagementPayload.WireState(snapshot.Stage)` 计算，映射与 Spec 的投影表一致。`IsEngaged` 为 false 时仍然整帧为 0。codec 测试的期望字节没有改。

## 逐项结论

| 条目 | 结论 | 依据 |
|---|---|---|
| SC-1 / RQ-001 / AC-001 | 满足 | 搜索 0 处；`The_four_faces_resolve_to_one_instance`；调用方都只注入用到的面（Couple 只注入 3 个命令处理器，Venue 只注入 7 个命令处理器，读取方只注入 Query） |
| SC-2 / RQ-002 / AC-002 | 满足 | 阶段存在 `marriage_relations.stage`；快照的 `IsMarried` / `IsCeremonyReserved` 都由 `Stage` 派生；03D0 由 Protocol `WireState` 投影；投影测试覆盖 null 和 0–5 |
| SC-3 / RQ-006 / AC-006 | 满足 | 架构判据附反例；CAS 冲突用例；实体构造器里没有 store |
| SC-4 / RQ-007 / RQ-008 / AC-007 / AC-008 | 满足 | 回填表每一行都有用例，幂等，`integrity_check` 为 ok，旧四表不变；副本迁移记录；心意点双写由用例证明两列相等 |
| SC-5 / RQ-009 / AC-009 | 满足 | 全量测试通过；断言改动只在批准的位置（证据 4）；B-01…B-16 的覆盖对照见 TASK-001 结果，相应用例本次全部通过 |
| SC-6 / RQ-010 / AC-010 | 满足 | B1-1、B1-2、B1-4、B1-5 都有对照用例；B1-3 我按代码复核，判定顺序没有变 |
| SC-7 / RQ-011 / RQ-012 / AC-011 / AC-012 | 满足 | 构建 0 警告；`CommandHandlerArchitectureTests` 通过；设计稿有落地状态；回归清单覆盖 §10.6，心意点、祝词簿各一遍；`MarriageWeddingFlow` 和 `protocol/` 三处注记准确，引用的类名、方法名都存在 |
| 刻意修正 B1-1…B1-5 | 如实 | 修正前后的值与代码一致；没有找到清单外的可见变化（证据 2） |
| 注释密度与约定 | 符合 | 新增和修改的生产文件，注释与非空行之比都远低于基线 0.46；`ViewAsync`、`IsMarriedAsync` 没有生产调用方、`CommitAsync` 只有测试调用，这几点已写进落地状态 |

## Findings

**没有阻塞项。** 以下都是非阻塞的观察，不影响本关通过。

| # | 严重度 | 位置 | 观察 | 实际后果 | 建议（可选） |
|---|---|---|---|---|---|
| N1 | Low | `Server/src/Dfo.Server.Application/Marriage/TrialQuests.cs:16`（`IMarriageDungeonContentSource dungeons`） | 业务组件直接注入内容源，与 AGENTS「内容源只被规则服务注入」以及 RQ-011 中「组件按三层约定向规则服务取规则」的字面不符。这是从基线 `MarriageQuestDomainService` 原样带过来的，不是本次新增；AC-011 写的判据（构建、`CommandHandlerArchitectureTests`、没有新增 Protocol 引用）都满足 | 运行时没有影响；分层漂移会延续下去 | 把 `IsOrdinaryMapAsync` 收进某个规则服务；或者在落地状态的偏差表里补一行，说明这是沿用的既有偏差 |
| N2 | Low | `CoupleLedger.cs` 的 `CommitAsync`；Spec AC-004 中「得到 `Stale`」 | 生产路径上，关系 CAS 冲突一律经 `MustCommitAsync` 抛异常回滚；返回 bool 的 `CommitAsync` 只有测试调用。AC-004 的字面由这个只供测试的方法满足 | 实际冲突已被账号闸与子记录 CAS（返回 `Stale`）挡在前面，抛异常回滚是更保守的结果。设计稿偏差表已经如实记录 | 在 `CommitAsync` 上注明它今天没有生产调用方；或者让并发用例改为断言生产路径的行为 |
| N3 | Low | 设计稿落地状态第 3 行「（TASK-001～005）」；同一处对启动审计的描述 | 已有 TASK-006，这里漏了。启动审计实际还会计入「Reserved 预约 + 只剩已取消婚礼」（store 的注释写的是 missing or cancelled），设计稿只写了「没有婚礼行」 | 读者按字面理解会略微偏差 | 改成「TASK-001～006」，审计描述补上「或婚礼已取消」 |
| N4 | Info | `MarriagePairCorrectionTests.cs` 类注释 | 写了「TASK-004 corrections」这种过程编号，与 AGENTS「不写过程」相悖（测试代码） | 无 | 只保留 B1-x 编号即可 |

## 非阻塞的残余风险

- **回退窗口的后果比文档写得重。**
  - 已知限制只说「回退期间推进的阶段不会补进已有的关系行」。实际上，如果在回退期间由旧二进制开出婚礼（phase=0），切回新版本后，关系行仍是 Requested 或 Engaged。到点时 `AdvanceAsync` 的 `MustCommitAsync` 会抛异常、整笔回滚，调度器每秒记一次 `Wedding advancement failed`，这场婚礼既不完成，也不退款。
  - 回归清单已写明「回退期间不要做婚姻操作，或切回前恢复备份」，所以不算交付缺陷。建议在已知限制里点明这个后果。
- **架构判据只看构造器的直接参数。** `Func<>`、`Lazy<>`，或者经由实体对外接口的间接依赖，都不会被拦下。现有 `LovePoints → Func<IApplyEffectsDomainService> → … → IMarriageQuestDomainService`（实体本身）就是这样一条运行期回边。今天没有功能影响，落地状态也已经说明这是判据守着、不是编译期保证。
- **阶段值 6–15 已经预留，但 `MarriageEngagementPayload.WireState` 遇到这些值会抛异常。** 将来加入离婚阶段时，必须同时补投影。
- **`MarriageEngagementSnapshot.Stage` 默认值是 Engaged。** 构造快照时忘了设 Stage，会静默投影成 state 1。生产路径总是显式赋值，这只是对日后调用方的提醒。
- **实机回归还没有执行**，由用户按清单完成。本审查不宣称任何客户端表现。

## Round 2（审查后修正）

```yaml
kind: rks-review
verdict: approved
```

### 目标与范围

- 同一 worktree、同一分支，HEAD 仍为基线 `5a0cbed0`，改动未提交。
- 只审查上一轮 approved（本文件写于 2026-10-17 23:16:24）之后的修正：
  1. `Document/MarriageAggregateDesign-20261017.md` 落地状态（对应 N3、N1、残余风险 1/3/4、架构判据补注）；
  2. `MarriagePairCorrectionTests.cs` 类注释去掉过程编号（对应 N4）。
- 同时核对「没有其它审查后改动」。所用准则同上一轮（`reviewing-work` 的 `references/code.md`、`references/deliverable.md`）。审查者独立、只读，唯一写入是本节。

### 证据

1. **没有其它审查后改动**
   - `git status --porcelain`：33 M、8 D、36 ??，与上一轮记录的清单数一致。
   - 对整个 worktree（排除 `bin/`、`obj/`、`.git`）列出修改时间晚于 23:16:24 的文件，只有本文件、设计稿 md、`MarriagePairCorrectionTests.cs` 三个，后两者都是 23:17:26。设计稿 html、回归清单、`protocol/` 三处、全部生产代码的修改时间都早于上一轮审查。
   - 局限：没有找到设计稿修正前的副本，无法逐字 diff；设计稿这部分的核对方式是通读落地状态段（第 8–59 行）并在全文里搜过程用语，没有发现落地状态段以外的可疑改动。
2. **N3**
   - 第 10 行已是「TASK-001～006」；全仓搜「TASK-001～00」，本任务范围内只剩这一处。
   - 第 16 行审计描述「已订婚、仍有 Reserved 预约、但没有进行中或已完成的婚礼行，含婚礼已取消的情况」。对照 `SqliteMarriageRelationDomainStore.CountUnsettledReservationsAsync`（第 81–85 行）：`r.stage=1` 且 `c.state=1`（`MarriageCeremonyState.Reserved`），并且 `NOT EXISTS` phase IN (0,1) 的婚礼行（`MarriageWeddingPhase` Preparing=0、Completed=1、Cancelled=2）。所以没有婚礼行、只剩 Cancelled 婚礼行的都会计入，描述准确。
3. **新增已知限制：回退窗口里由旧二进制开出的婚礼**
   - `MarriageWeddingScheduler` 用 `PeriodicTimer(TimeSpan.FromSeconds(1))`，异常在循环内被捕获并记 `Wedding advancement failed`，下一秒再试，所以「每秒重试一次」成立。
   - `AdvanceAsync`（`MarriageDomainService.Venue.cs` 第 81–100 行）：教堂房间由 `SqliteMarriageChurchDomainStore` 持久化，切换二进制重启后仍在。到点时先 `settlement.FinishAsync` 改了婚礼行，再 `MustCommitAsync(WeddingCompleted / WeddingCancelled)`。
   - `MarriageStageRules.Next` 只接受 `Preparing + WeddingCompleted → Married`、`Preparing + WeddingCancelled → Engaged`。关系行停在 Engaged 或 Requested 时，`TryCommitAsync` 返回 null，`MustCommitAsync` 抛 `InvalidOperationException`，整笔回滚：婚礼行回到 Preparing，结算、退款都不发生。描述的后果与代码一致。
4. **`TrialQuests` 直接注入内容源**：`TrialQuests.cs` 第 16 行接 `IMarriageDungeonContentSource dungeons`；基线 `5a0cbed0` 的 `MarriageQuestDomainService` 构造器同样接 `IMarriageDungeonContentSource dungeons`。「沿用基线」属实。
5. **阶段 6–15 与 `Stage` 默认值**
   - `MarriageEngagementPayload.WireState` 的 `_ =>` 分支抛 `ArgumentOutOfRangeException`。
   - `MarriageEngagementSnapshot.Stage` 的初值是 `MarriageStage.Engaged`（`MarriageProposalModels.cs` 第 83 行），投影成 1。
   - 两句描述准确。
6. **架构判据补注**：`MarriageArchitectureTests` 第 17–18 行只取 `GetConstructors().SelectMany(GetParameters).Select(ParameterType)`，不展开泛型参数，`Func<IApplyEffectsDomainService>` 不会被识别成依赖。「只看构造器直接参数，`Func<>` 间接依赖拦不住」属实。
7. **N4**：`MarriagePairCorrectionTests.cs` 第 22–25 行类注释只保留 B1-1、B1-2、B1-4、B1-5 和设计稿出处。`Server/tests/.../Marriage/` 下搜 `TASK-` 为 0 处。
8. **构建与测试**
   - `dotnet build Server/86JP.Server.slnx`：0 警告，0 错误。测试程序集写于 23:17:35，晚于源文件修改，说明新注释已经编译进去。
   - `dotnet test Server/tests/Dfo.Server.Application.Tests --no-build --filter FullyQualifiedName~Dfo.Server.Application.Tests.Marriage`：通过 88、跳过 3、失败 0，与上一轮一致。
   - 本轮只改了注释和文档，没有重跑全量测试，全量证据沿用上一轮。

### Findings

没有阻塞项。N1（以文档方式处理）、N3、N4 已处理；残余风险 1、3、4 已写进已知限制；残余风险 2 已补进判据描述。N2 本轮没有动，仍是上一轮的非阻塞观察。

| # | 严重度 | 位置 | 观察 | 实际后果 | 建议（可选） |
|---|---|---|---|---|---|
| R2-1 | Info | 设计稿第 52 行「关系行仍停在 Engaged 或 Requested」 | 列举不全。新版本下发起求婚（关系行 Proposed），回退期间由旧二进制接受求婚、预约、开婚礼，切回后关系行停在 Proposed。这种情况同样被迁移表拒绝，后果与修复办法和文中一样 | 只是枚举少一项。后果和修复办法（按婚礼行人工修正关系行）对这种情况同样适用 | 改成「停在 Preparing 以外的阶段」，或者补上 Proposed |
