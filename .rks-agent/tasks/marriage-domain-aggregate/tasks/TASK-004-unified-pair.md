# TASK-004 — 统一「是否为一对」与一次加载

## Task Unit Contract

- Revision: 1
- Outcome and references（Plan T-004；Spec RQ-010(a)、AC-010，以及 TASK-003 Controller Decision 转来的两项）：
  - 实体中六处「是否为一对」的判定改为经账本统一判定，口径见下文；
  - 每个公开操作只经账本解析一次关系，并往下传；
  - `ViewAsync` 在缺任务内容时不抛异常；
  - 对每一项可见变化写修正前后的对照用例，整理成修正清单。
- Owned boundary：
  - `Server/src/Dfo.Server.Application/Marriage/` 内的实体与 `CoupleLedger`，其它组件只在需要接收已解析关系时改签名；
  - 婚姻测试：新增对照用例；表征测试中**被本次修正改变的**断言可以改，每一处都要在结果里注明对应哪项修正（B1-x）以及原断言。
  - 不改：Protocol、接口签名、线上字节与状态码取值、业务规则。
- Required checks：
  - 构建 0 警告；全量测试通过；
  - 修正清单中的每一项都有对照用例；
  - 测试断言的 diff 只出现在修正清单列出的位置。
- Dependencies：TASK-003 已完成。

## 统一口径（Spec Definitions「一对的判定」）

- **会话属于关系**：账本按会话的角色找到活跃关系；该角色对应的参与者账号等于会话账号；并且阶段不是 Closed。
- **一对**：两个会话属于同一条关系，并且分别是两个不同的参与者。
- **已结成的一对**：属于关系，并且阶段满足 `MarriageStageRules.IsCouple`（Engaged 及之后）。
- **祝词簿按 key 访问**：key 是对方角色 id。
  - 新人路径：会话属于该关系，并且 key 是自己的对方。
  - 宾客路径：保持现状，按请柬判定；但关系必须没有关闭。

## Detailed Plan

1. **账本**：新增按会话解析（含账号核验）和按一对解析两个方法，返回关系或 null。现有不核账号的 `FindAsync` 只留给确实需要它的内部场景（例如按 key 取关系），其它判定一律改用新方法。
2. **实体**：
   - 把 `ReservationPairAsync`、`ReservedAsync`、`QuestRelationAsync` 以及心意点、祝词簿、快照中的关系判定都改为调用账本的统一判定；
   - 每个公开操作在开头解析一次关系，后面的守卫、组件调用和提交都用这一份；
   - 写路径上 TASK-002 遗留的重复读取（例如申请预约时再读一次关系）在此合并。
3. **已知的可见变化**（以代码对照为准，执行中发现更多就补进清单）：
   - **B1-1 心意点**：`CoupleAccountsAsync` 与 `UseLovePointAsync` 开始核对账号。账号不匹配时，账号列表退回只含本人，使用返回 -1 或 -3（以实现后的实际码为准，并在清单中写明）。原先只看角色。
   - **B1-2 祝词簿新人路径**：开始核对账号。原先只比角色 id。
   - **B1-3 失败码先后**：一次加载可能改变某些组合下先返回 `Stale` 还是 `Ineligible`。如果确实改变了，逐条列出组合和新旧结果；如果没有改变，就在结果里写明已核对、没有变化。
4. **`ViewAsync`**：缺任务内容时 `TrialsCleared = false`，不抛异常，补一条用例。
5. **对照用例**：每项 B1-x 至少一条。账号不匹配可以这样构造：在会话注册表里登记一个账号与角色不一致的会话，参照现有测试中构造 `SelectedSessionIdentity` 的方式。断言写成「修正后的行为」，用例名或数据能看出修正前的行为是什么。

## Result

### Changed

生产代码只动了 `Server/src/Dfo.Server.Application/Marriage/` 下的 6 个文件（相对改动前快照：+109 / −88 行）。Abstractions、Data、Persistence、Protocol 的婚姻目录与改动前快照逐文件比对无差异（`diff -rq` 无输出）；接口签名、线上字节、状态码取值集合都没有变。

| 文件 | 要点 |
|---|---|
| `CoupleLedger.cs` | 新增统一判定：`ForSessionAsync(session)`、`ForParticipantAsync(account, character)`（按角色找活跃关系，核对该参与者账号，排除 Closed）、`ForPairAsync(first, second)`（两个会话各自属于、且是同一条关系、角色不同）；纯函数 `Belongs(relation, session)` / `Belongs(relation, account, character)` / `IsPair(relation, first, second)`，用于已按 id 读到的关系。删除 `ReadForAsync`。`MustCommitAsync` 改为返回提交后的关系（Stage、Revision+1、CeremonyId、StageSince、MarriedAt），供同一操作继续往下用；`CommitAsync` 签名不变（仍返回 bool）。`FindAsync` 保留，只剩祝词簿按 key 取关系一处调用 |
| `MarriageDomainService.cs` | 删除 `ReservationPairAsync`；`ReservedAsync` 拆成「按会话解析」和「对已解析关系做同一判定」两个重载；`QuestRelationAsync` 改走 `ForParticipantAsync`；新增私有 `CoupleAccounts(relation, actor)`；`ActiveReservationAsync` 改为接收关系、返回 (预约, 提交后的关系) |
| `MarriageDomainService.Couple.cs` | `RequestReservationAsync`：`ForPairAsync` 解析一次，惰性到期后用返回的关系继续，末尾的 `ReadForAsync` 重读换成对手上关系查迁移表（TASK-002 遗留的重复读取在此合并）。`RespondReservationAsync`：接受时的一对复核改为对已按 id 读到的关系做 `IsPair`，不再按两个角色各查一次。`OpenWeddingAsync`：关系只读一次，两位新人的教堂准备与入场都用这一份（原先每人经 `PrepareChurchAsync`、`EnterNewlywedAsync` 各查一次）。`UseLovePointAsync`：`ForSessionAsync` 一次，账号列表与 -3 判定共用 |
| `MarriageDomainService.Query.cs` | `ViewAsync` 改走 `ForSessionAsync`；`TrialsCleared` 经私有 `TrialsClearedAsync` 计算，缺任务内容（`FileNotFoundException` / `InvalidDataException`）时为 false。`SnapshotAsync` 改走 `ForParticipantAsync`。`CoupleAccountsAsync` 改走 `ForSessionAsync` + `CoupleAccounts` |
| `MarriageDomainService.Quest.cs` | `RecordTrialClearAsync` 的逐人账号核对改用 `CoupleLedger.Belongs`，判定内容不变 |
| `MarriageDomainService.Venue.cs` | 祝词簿新人路径由「角色是参与者」改为 `CoupleLedger.Belongs(relation, actor)`；宾客路径不变（按请柬，关系须 `IsCouple`，即未关闭） |

六处判定改造后的对应：

| 原判定 | 原口径 | 现在 | 可见变化 |
|---|---|---|---|
| Ceremony `ReservationPairAsync` | 双方账号+角色、同一关系、`IsCouple` | `ForPairAsync` / `IsPair` + 调用处 `IsCouple` | 无 |
| Church `ReservedAsync` | 只核本人账号、Preparing | `ForSessionAsync` + Preparing | 无 |
| Quest `QuestRelationAsync` | 核账号、`IsCouple` | `ForParticipantAsync` + `IsCouple` | 无 |
| Engagement `SnapshotAsync` | 核本人账号、`IsCouple` | `ForParticipantAsync` + `IsCouple` | 无 |
| LovePoint `CoupleAccountsAsync` / `UseLovePointAsync` | 不核账号 | `ForSessionAsync` | **B1-1** |
| Guestbook 新人路径 | 只比角色 | `Belongs` | **B1-2** |
| （快照之外）`ViewAsync` | 核本人账号，不排除 Closed | `ForSessionAsync` | **B1-4** |

测试：新增 `Server/tests/Dfo.Server.Application.Tests/Marriage/MarriagePairCorrectionTests.cs`（4 条）。没有修改任何已有测试文件，包括 `MarriageHarness`。账号不匹配的会话由文件内的 `Impostor` 构造：先 `GoOffline` 真实会话，再按 `AddCharacter` 的方式在 `OnlineStateRegistry` 和 `ISelectedSessionDomainService` 里登记「账号 9 + 角色 71」。

### 修正清单

「修正前」一列不是推断：先把 4 条对照用例写好，在**未改生产代码**的状态下运行（4 条全部失败），再用一个临时探针用例逐项记录每个入口的实际返回值；探针在拿到修正后的同样记录后删除。两次记录都在会话 scratchpad：`t004-before-run.log`、`t004-before-probe.log`、`t004-after-marriage.log`。

| 编号 | 行为 | 修正前（实测） | 修正后（实测） | 对照用例 | 被改动的旧断言 |
|---|---|---|---|---|---|
| B1-1 | 心意点：会话账号不拥有该已婚角色（账号 9 选中角色 71） | `CoupleAccountsAsync` → `[1, 2]`；`UseLovePointAsync(accounts=[1,2])` → 抛 `InvalidOperationException`（"Effect character does not belong to this account."，事务回滚，券 3 张、点数 0）；`UseLovePointAsync(accounts=[9])` → **-1** | `CoupleAccountsAsync` → `[9]`；`accounts=[1,2]` → **-1**；`accounts=[9]` → **-3**；券仍 3 张、点数 0；对方会话的账号列表仍为 `[1, 2]` | `MarriagePairCorrectionTests.B1_1_love_points_refuse_an_account_that_does_not_own_the_spouse_where_the_character_alone_was_enough` | 无 |
| B1-2 | 祝词簿新人路径：会话账号不拥有该新人角色 | 读 key 72 → **0**；删除宾客留言 → **0**（留言被删，行数 0）；写 → -1 | 读 → **-1**；删 → **-1**（行数仍 1）；写 → -1；真实对方仍可读到 1 条 | `MarriagePairCorrectionTests.B1_2_guestbook_owner_path_refuses_an_account_that_does_not_own_the_newlywed_where_the_character_alone_was_enough` | 无 |
| B1-3 | 一次加载后 `Stale` / `Ineligible` 先后 | — | 已逐个核对，**没有变化**，见 Why | 现有用例照常通过（见 Checks） | 无 |
| B1-4 | 已关闭关系（库中 stage=5、参与者仍 active）在各入口 | `ViewAsync` 返回 Stage = Closed 的视图；其余入口：Snapshot = Empty、账号列表 `[1]`、心意点 -3、申请预约 Ineligible、教堂准备 null、祝词簿 -1、可接任务空、`CanAccept` false | `ViewAsync` → **null**；其余入口结果与修正前相同 | `MarriagePairCorrectionTests.B1_4_a_closed_relation_is_no_pair_at_any_entry_where_the_view_used_to_show_it_as_closed` | 无 |
| B1-5 | `ViewAsync` 在缺任务内容时 | 抛 `FileNotFoundException`（"Script.pvf was not found."） | 返回视图，Stage = Engaged、`TrialsCleared = false` | `MarriagePairCorrectionTests.B1_5_the_view_reports_trials_not_cleared_when_quest_content_is_missing_where_it_used_to_throw` | 无 |

B1-1 中 `accounts=[9]` 由 -1 变为 -3：账号列表按统一口径退回「只含本人」后与请求一致，于是落到「该会话不属于任何已婚关系」的 -3。Task 允许 -1 或 -3，以实现后的实际码为准，这里两种请求分别得到 -1 和 -3。

B1-4 的 stage=5 + 参与者 active 只能用 SQL 构造：正常流程里关系关闭时求婚 store 会把参与者置为 inactive，`FindActiveAsync` 本来就找不到它。

### Why

- **统一判定放在账本，纯谓词也放在账本。** 按 id 读到关系的操作（应答预约、开婚礼、到点推进）不必再按角色查一次：对手上的关系调用 `Belongs` / `IsPair`，与 `ForSessionAsync` 是同一个口径。在 I1（一个角色至多一条活跃关系）成立时，「按角色找到的活跃关系」和「按 id 读到的、仍未关闭的关系」是同一条，所以这一换不改变结果。
- **`MustCommitAsync` 返回提交后的关系**，是为了让「只解析一次」在中途发生惰性到期时仍然成立：`RequestReservationAsync` 的惰性 T7 提交之后，用返回的关系继续往下，不再重读。`CommitAsync` 的 bool 签名保留，是因为 `MarriageRelationLedgerTests` 直接断言它的返回值。
- **祝词簿与心意点账号列表仍要求 `IsCouple`（Engaged 及之后）。** 统一口径只定义「会话是否属于关系」；阶段门槛是业务规则，原样保留，所以订婚前的关系照旧不能用祝词簿、账号列表照旧只含本人。
- **`ViewAsync` 只吞 `FileNotFoundException` 和 `InvalidDataException`。** 前者是归档不存在（`PvfArchiveProvider` 抛出），后者是内容无法解析（`PvfMarriageProposalContentSource` 抛出）；Application 里按 `InvalidDataException` 降级已有先例（各地下城入场服务）。其它异常照常上抛。
- **B1-3 逐个核对的结论：没有变化。**
  - `RequestReservationAsync`：失败码的判定顺序是 Invalid → Stale（会话）→ Invalid（同号）→ Unavailable → Ineligible（不是一对 / 未订婚 / 已婚）→ Occupied / 幂等 → Ineligible（任务）→ InsufficientFunds → Stale（阶段已不接受申请）。合并读取后每一步的位置不变。最后一步原先重读库，现在查手上的关系；同一用例里能改动关系的只有前面那次惰性 T7，而它的结果已经通过返回值带回，所以两者看到的阶段相同。
  - `RespondReservationAsync`：Stale（关系不是 Requested 或不引用这份预约）仍在到期处理和接受复核之前；接受复核里的一对判定仍排在 Unavailable 之后、任务检查之前，只是从「按两个角色各查一次」换成对同一条关系求 `IsPair`。
  - `OpenWeddingAsync`、`UseLovePointAsync`、`CanCrossAsync`、祝词簿与任务各入口：没有 `Stale` 失败码，只是把多次查询合成一次。
  - `ProposeAsync` / `RespondProposalAsync`：按求婚记录而不是按会话找关系，本来就各读一次，未改。
  - 佐证：TASK-001 / TASK-002 / TASK-003 中钉住失败码先后的用例，断言零改动，照常通过（名单见 Checks）。

### Checks

所有命令都在 worktree 的 `Server/` 下实际运行。

- **构建**：`dotnet build 86JP.Server.slnx` → 0 个警告，0 个错误。
- **全量测试**：`dotnet test 86JP.Server.slnx --no-build --logger "console;verbosity=detailed"`，日志在会话 scratchpad 的 `mda-task004-final.log`。8 个项目都显示「测试运行成功」，失败 0；日志中 `ObjectDisposedException` 出现 0 次。

  | 项目 | 总数 | 通过 | 跳过 | 失败 |
  |---|---:|---:|---:|---:|
  | Application | 4016 | 3590 | 426 | 0 |
  | Protocol | 1581 | 1521 | 60 | 0 |
  | Persistence | 470 | 470 | 0 | 0 |
  | Infrastructure | 933 | 596 | 337 | 0 |
  | Host | 159 | 149 | 10 | 0 |
  | Networking | 92 | 92 | 0 | 0 |
  | HttpApi | 50 | 50 | 0 | 0 |
  | Pvf | 80 | 71 | 9 | 0 |

  Application 4016 = TASK-003 的 4012 + 本单元新增 4 条；其余项目与 TASK-003 相同。跳过的都是 Skippable（本机无 PVF 或客户端档案）。
- **婚姻过滤集**：`dotnet test tests/Dfo.Server.Application.Tests --no-build --filter "FullyQualifiedName~Marriage"` → 总数 101，通过 98，跳过 3，失败 0（含 `Inventory.MarriageConsumableProtectionTests` 3 条）。同一份全量日志里 `Application.Tests.Marriage` 命名空间通过 95、跳过 3。Protocol 婚姻过滤集 36 / 36 通过。
- **对照用例**：B1-1、B1-2、B1-4、B1-5 四条在全量日志中都是「已通过」；在未改生产代码时同样四条全部失败（`t004-before-run.log`：分别报 `Expected [9] / Actual [1, 2]`、`Expected -1 / Actual 0`、`Assert.Null` 拿到 Stage = Closed 的视图、`FileNotFoundException`）。
- **钉住失败码先后的现有用例**（B1-3 佐证），全量日志中均为「已通过」：`MarriageRequestCharacterizationTests.Proposal_guards_answer_their_own_failure_codes`、`…Reservation_guards_answer_their_own_failure_codes_and_failed_rechecks_expire`、`…Expired_requests_answered_with_acceptance_are_stale_yet_recorded_expired`、`MarriageRelationLedgerTests.A_relation_out_of_step_with_its_ceremony_is_stale_before_the_ceremony_changes`、`MarriageEntityTests.A_relation_that_moved_after_its_child_record_changed_rolls_the_whole_use_case_back`；B-14 / B-15 的 `MarriageCoupleCharacterizationTests.Love_point_refusals_answer_minus_one_two_or_three_by_cause`、`…Guestbook_access_matrix_text_limits_and_capacity` 也照常通过。
- **架构判据**：`MarriageArchitectureTests` 2 条、`CommandHandlerArchitectureTests` 13 条通过。实体构造器、组件构造器、store 注入面都没动，账本仍是唯一写关系的类型。
- **测试断言 diff 只在清单位置**：清单中没有任何一项需要改旧断言。
  - 未跟踪的婚姻测试文件：开工前把 `tests/.../Marriage/` 整个复制到 scratchpad（`t004-before/tests-marriage`），结束时 `diff -rq` 只报出 `Only in …: MarriagePairCorrectionTests.cs`。
  - 已跟踪的测试文件：`git diff --stat -- Server/tests` 仍是 TASK-003 结束时的数字（`MarriageCeremonyHandlerTests` 6、`MarriageCeremonyTests` 80、`MarriageChurchTests` 201、`MarriageEngagementTests` 28、`MarriageProposalTests` 20、`EffectOperationReplayTests` 1、`SchemaOwnershipTests` 2），本单元没有碰。
- **账本外不再有不核账号的按角色查找**：`grep -rn "ledger\.FindAsync" Server/src/Dfo.Server.Application/Marriage` 只剩 `MarriageDomainService.Venue.cs` 祝词簿按 key 取关系一处；`ReadForAsync`、`ReservationPairAsync` 在 `Server/src` 中 0 处。
- 改动的 6 个文件保持原有的 LF 行尾（中途脚本写成过 CRLF，已改回，`file` 核对无 CRLF）。

### Remaining

- 心意点、祝词簿在账号不匹配时的实机表现（客户端看到 -1 / -3）不由本单元宣称，留给 T-005 的实机回归清单。
- 设计稿落地状态中需要补的内容（统一判定落在账本、`MustCommitAsync` 返回提交后的关系、B1-1…B1-5）留给 T-005。
- 没有把探针记录或运行日志写进仓库：Task 的 Owned boundary 不含 `evidence/`；日志都在会话 scratchpad，文件名见上。

## Controller Decision（round 1）

结果接受。控制者重新跑了全量构建和测试：0 警告，8 个测试项目都显示「测试运行成功」，失败 0。日志在会话 scratchpad 的 `mda-task004-verify.log`。修正清单 B1-1…B1-5 交给 T-005，写进设计稿的落地状态和实机回归清单。
