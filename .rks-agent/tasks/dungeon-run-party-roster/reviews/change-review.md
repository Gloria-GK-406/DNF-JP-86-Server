---
kind: rks-review
target: "完整改动 1cff7218..99e7ebc7（分支 task/dungeon-participants-20261027）"
verdict: approved
reviewer: independent subagent (reviewing-work code + deliverable)
---

# 完整结果审查：普通地下城副本只挂在队伍上，人员只问队伍

## 范围

- 对象：`git diff 1cff7218..99e7ebc7`，共 10 个提交（d407b186 规划 → 99e7ebc7 注释补救）。工作区干净，无未提交改动。
  - 生产代码 18 个文件（+366／−229），集中在 `Application/World/DungeonRunDomainService*.cs`、`Abstractions/World/IDungeonRunDomainService.cs`、`IPartyService.KeyOf`，以及调用方：`GamePartyTransitionCommandHandler*.cs`、`DungeonSelectionCommandPhases.cs`、`SelectCharacterLifecycleServices.cs`、`PartyProtocolCommands.cs`。
  - 文档 2 个：`Document/ReactiveDungeonCellRuntime.md`、`Document/StoryPartyDungeon-20260914.md`。
  - 测试约 80 个文件，含新增的 `Server/tests/Support/TestParties.cs`。
- 审查类型：complete-result review，代码与文档混合交付，同时适用 Code 与 Deliverable 准则。
- 本审查只读代码与 diff，未运行 build 或 test（调用方要求）。SC-8／AC-014 的测试结论来自执行记录：TASK-006 全量 0 失败，TASK-007 构建 0 错误 0 警告。完成验证由主代理另行负责。

## 依据

- `reviewing-work` SKILL.md、references/code.md、references/deliverable.md。
- TaskContract revision 1、spec.md（已按 80d77249 修订 RQ-008／AC-007）、plan.md（BD-001～BD-006）、routing-decision.md、TASK-001～TASK-007 的 Result 与 Review Round。
- 仓库规则：AGENTS.md、Document/Architecture.md、RefactorPreferences.md、ProtocolCommands.md、DocumentationGuidelines.md、ArchitectureReview.md；GameplayExecution.md 只按需对照，本次改动未触及执行器、事务划分或发件箱。

## 逐条对照

### Spec 需求（RQ）

| ID | 结论 | 证据 |
|---|---|---|
| RQ-001 成员只来自“队伍当前成员 ∩ 入场名单” | 满足 | 聚合 `Participants`（`DungeonRunDomainService.cs:1822`）只读 `parties.FindByKey(PartyKey)` 并按 `entryAccounts` 过滤，槽位与队长取队伍值。`ActiveParticipants` 已删除；人员相关字段只剩不可变的 `entryParticipants`、`entryAccounts`（:1768），以及键集固定的 `progressByCharacter`。原读 `ActiveParticipants` 的收件人、翻牌公开、全灭判定、入场中止比对，一律改读 `Participants` |
| RQ-002 必须依赖队伍服务 | 满足 | 无参、单事务参数、`internal (IPartyService)` 三个构造器已删除；唯一构造器 `parties ?? throw`（:66）；`parties is null` 两处回退已删除 |
| RQ-003 `KeyOf` | 满足 | `IPartyService.KeyOf`，无默认实现；`PartyService.KeyOf` 在 `lock (sync)` 下查 `membership`；装饰器 `PartyClientProjectionService` 直接转发；`PartyServiceTests` 的三个 `Key_of_*` 用例 |
| RQ-004 角色 → 队伍 → 副本，无按角色索引 | 满足（附建议 S1） | `runsByParty`（:40）与 `runsById` 是仅有的两个索引；`RunOf`（:1486）= `KeyOf` → `runsByParty` → `IsMember`；所有按角色入口都统一走 `RunOf`（含 `MoveCommit.Prepare`、对象破坏）。同步 `End(CharacterId)`（:1381）在 `RunOf` 失败时按 `HoldsRunState` 线性扫描，只有测试调用，见 S1 |
| RQ-005 个人离开 | 满足 | `Depart`（:1406）有三个分支：仍是成员且有他人时拒绝；已离队则只 `Release`；他是最后一名成员或已无成员时 `EndRun`。放弃副本改为经 `MutatePartyStateAsync` 先退队再 `EndAsync`（`GamePartyTransitionCommandHandler.cs:332-344`）。断线把 `EndAsync` 与 `ParticipantDeparted` 移到 `parties.CleanupCharacter` 之后，`Find` 仍在原位置先取（`SelectCharacterLifecycleServices.cs:120,169`）。退队、被踢原本就是先改队伍 |
| RQ-006 整局结束 | 满足 | `EndRunAsync`（:1343）供 Eplp（`.Exit.cs:25`）与选图回城（`DungeonSelectionCommandPhases.cs:80-90`，仅当请求者是所属队伍队长时调用）使用；清场回城、全灭、入场中止、开新局改调 `EndRun`；都不改变队伍 |
| RQ-007 运行中队伍不增员，单向依赖 | 满足 | `PartyRunAdmission.Refusal`（`PartyProtocolCommands.cs:257`）在 REQUEST_PEER 与 RESPONSE_PEER 两个 CMD 中、调用队伍写入之前检查双方。生产中队伍增员的写入者只有这两个 CMD（grep `RequestInvite/AcceptInvite`，装饰器除外）。`Application/Parties`、`Abstractions/Parties` 都不引用副本。图外角色无资格，因为 `RunOf` 与写入入口都以 `IsMember` 为门槛 |
| RQ-008 逐人副作用 | 满足 | `Release`（:1448）保留了基准 `DetachParticipant` 的全部逐人副作用：精确归属释放、`ResumeRecovery`、`ChangeCombat(false)`、准备与死亡标记、`EntryRevision++`、结算与翻牌领取清理（这几行在 diff 中未变）。`Remove` 保留了副本级 9 项清理。生命周期与失败虚弱：`EndAsync` 与 `EndRunAsync` 经 `RecordDeparturesAsync` 记录，并在 `FailsOnDeparture` 时设虚弱；全灭与清场回城的 Async 包装未改；入场中止与开新局两者都不产生。与修订后 RQ-008 及基准一致 |
| RQ-009 恰一名队长、不抛异常 | 满足 | 成员视图每次由队伍重算，没有第二份名单可以失步；`Views_have_exactly_one_leader_during_and_after_every_exit` 覆盖 8 条路径，并在清理过程中读取视图 |
| RQ-010 可观察行为不变 | 满足 | 表征测试零改动且通过（见“行为不变核查”）；变异检查记录在 TASK-001～TASK-003 中 |
| RQ-011 注释与文档 | 满足（附建议 S4） | `IDungeonRunDomainService` 类注释、`EndAsync`／`EndRunAsync`／`End` 注释、`IDungeonVisitDomainService`、`IMonsterDropDomainService` 已更正；副本相关源码中不再出现 “frozen participant”。文档见下 |

### Spec 验收（AC）

| ID | 结论 | 证据 |
|---|---|---|
| AC-001 | 满足 | `Transferring_leadership_in_a_run_moves_the_leader_and_nobody_departs`（领域）、`Transferring_leadership_in_a_run_keeps_both_members_and_moves_the_run_leader`（生产容器） |
| AC-002 | 满足 | 代码检查结论同 RQ-001、RQ-002、RQ-004；`Released` 不构成第二份名单，见“单一来源” |
| AC-003 | 满足 | `PartyServiceTests.Key_of_*` 三例 |
| AC-004 | 满足 | `Members_find_the_run_of_their_current_party_and_lose_it_when_they_leave`、`A_new_run_of_the_same_party_is_the_one_every_member_finds` |
| AC-005 | 满足（证据分层，见 R-c） | 领域：`Departing_member_leaves_only_their_own_state_and_the_party_keeps_the_run`（3 例）、`..._after_clear_gets_no_failure_weakness`。协议：放弃副本、断线、退队各覆盖 A、B 两种发起方，另有被踢用例。放弃副本与断线在协议层只断言了帧、投递登记、队伍与视图；占用、生命周期、虚弱由领域层“先退队再 `EndAsync`”的同一函数覆盖 |
| AC-006 | 满足 | `Giving_up_alone_ends_the_whole_run`（生产容器）、`Disconnecting_alone_ends_the_whole_run_and_sends_nothing`、`Solo_departure_ends_the_whole_run`、`The_last_member_departing_ends_the_whole_run` |
| AC-007 | 满足 | 清场回城、全灭、入场中止、开新局：`Departures.cs` 各一例。Eplp 与选图回城（队长）：协议表征中的 `AssertEachMemberDeparted`（TASK-003）。另有 `Ending_the_whole_run_*` 两例 |
| AC-008 | 满足 | `Back_to_village_by_a_member_does_not_end_the_run` |
| AC-009 | 满足 | `PartyCommandHandlerTests.RunningParty.cs` 三个 Theory（含对照组）与 `A_character_put_into_a_running_party_has_no_part_in_its_run`；队伍层未引用副本已用 grep 核实 |
| AC-010 | 满足 | 见 RQ-009 |
| AC-011 | 满足 | 见“行为不变核查” |
| AC-012 | 满足（依赖执行记录） | 全量测试 0 失败（执行记录）；`AltarLifecycleTests.Member_departure_…` 依赖 PVF，本机跳过，见 R-d |
| AC-013 | 满足 | 见文档部分 |
| AC-014 | 待完成验证 | 本审查未运行测试；执行记录为 0 失败，缺 PVF 的用例按规则跳过 |

### TaskContract 成功条件（SC）

| ID | 结论 | 说明 |
|---|---|---|
| SC-1 | 满足 | 同 RQ-001、RQ-002、RQ-004、AC-002 |
| SC-2 | 满足 | 同 AC-004 |
| SC-3 | 满足 | 同 AC-005、AC-010 |
| SC-4 | 满足 | 同 AC-007。Spec 把入场中止、开新局列为整局结束，范围比 SC-4 更宽 |
| SC-5 | 满足 | 同 AC-009 |
| SC-6 | 满足 | 同 AC-010 |
| SC-7 | 满足 | 同 AC-013 |
| SC-8 | 待完成验证 | 由主代理的完成验证给出完整 `dotnet test` 输出 |

## 单一来源（重点 2）

- 副本成员只有一个来源：`Participants` getter。`IsMember`、`RunOf`、`Leader`、`ViewFor`、收件人、全灭判定、翻牌公开都经由它。
- `Released`／`HoldsRunState`／`Unreleased()` 的全部使用点共 3 处（grep 结果）：
  1. `Depart` 的入口条件（:1408），保证同一角色只离开一次，避免重复记录生命周期；
  2. `EndRun`（:1478）用 `Unreleased()` 决定逐人清理谁；
  3. 同步 `End(CharacterId)` 的回退查找（:1389）。

  三处都没有参与成员判定、收件人、队长或视图读取。在合法操作可达的状态下，对同一入场者而言，“已离开”与“是队伍成员”互斥：非成员分支只清理已离队的人，最后一名成员分支会连同副本一起移除。所以它表达的是“本局状态是否已清理”，不是第二份名单。TASK-005 的变异检查（让 `Participants` 额外按 `Released` 过滤，有 5 例失败）也从反面证明，成员判定并不依赖它。
- 保留进度而不删除，是因为 `CommitPreparedClearRewardCard` 在离开后仍要读进度（:1264 附近）。基准同样从不删除进度，判定合理。
- 协议层 `DungeonRunDeliveryRegistry` 冻结的是连接 generation，靠 `ParticipantDeparted` 同步。它是既有结构，保存的是另一项事实（投递连接，不是领域成员），不在本任务范围内，本次没有新增同步义务。

## 行为不变核查（重点 3）

- 用 `git log` 查看表征文件在表征提交之后的改动：
  - `DungeonRunDomainServiceTests.Departures.cs` 与 `DungeonDepartureProtocolCharacterizationTests.cs` 在 032eb7ff 之后零改动（`git diff 032eb7ff..HEAD --stat` 为空）。
  - `PartyCommandHandlerTests.cs` 在 032eb7ff 之后只有：类改为 `partial`；第 45、84 行把构造改为 `TestParties.Runs(parties: parties)`；`DriveRequestPeer`／`DriveResponsePeer` 补传 `TestParties.Runs()`；替身 `RecordingParties` 补 `KeyOf`。没有断言改动。
- 对 032eb7ff..HEAD 全部测试 diff 的删除行做了扫描。唯一带 `Assert` 的删除行是 `Assert.Null(new DungeonRunDomainService().TrySettle(Character))`，改为等价的 `TestParties.Runs()` 构造，断言本身未变。其余删除行都是构造器、名单提取或入场辅助。另有 AltarLifecycleTests 把 `LeaveParty` 断言移到 `EndAsync` 之前，内容不变。
- 唯一被改写的既有断言：在 818d359a（表征阶段，生产代码仍是基准）中，`PartyCommandHandlerTests` 的投递登记断言由 `[Inviter, Invitee]` 改为 `[Invitee]`。原测试没有把登记注入处理器，断言实际是空转；注入后按基准真实行为改写，且发生在任何生产改动之前。TASK-002 审查已核实，本审查采信，并认为这不属于迁移中修改断言。

## 架构（重点 4）

- **依赖方向**：副本 → 队伍单向。新增的 `KeyOf` 只是读取。`RunOf` 在副本锁内调用队伍锁，这与基准中 `Participants` 调用 `FindByKey` 的锁序相同，队伍层不反向调用副本，不会出现锁序反转。
- **CMD 注入面**：两个组队 CMD 新增注入 `IDungeonRunDomainService`，属于 ProtocolCommands.md 允许的 `*DomainService`，`CommandHandlerArchitectureTests` 在全量测试中通过。
- **守护放置**：
  - Spec RQ-007 要求队伍服务不依赖副本，Plan BD-006 经用户审阅确定放在协议 CMD。
  - CMD 编排两个领域能力并给出正常拒绝，符合 ProtocolCommands.md 中“CMD 拥有……正常拒绝、领域调用”。同一 CMD 已有的“双方须在城镇”拒绝（`TryCapture`）也在这一层，是周边先例。
  - 判定本身只是“任一方是否有副本”，没有复制副本领域的成员算法：资格判定仍归 `RunOf`／`IsMember`。即使守护被绕过，领域层也不放行图外角色（AC-009 第二部分）。
  - 按 ArchitectureReview.md 的分级：这不是“消息 handler 承载业务”（那一项针对薄 handler），也没有形成第二个真源，因此判为通过。
- **选图回城的队长判定**：CMD 以请求者所在队伍的 `LeaderWireUserId` 判断是否整局结束。同一处理器的 `ResolveReturningMembersAsync`、Eplp 的队长判定都采用这一口径，没有另写一套算法，判为通过。
- **接口默认实现**：`EndRunAsync` 的默认实现抛 `NotSupportedException`，与同接口中 `PreparePartyEntry` 的写法一致。生产实现只有一个，不会静默降级。

结论：职责位置正确，现有架构完整承接，只有非阻断建议。

## 发现

没有阻断问题。

### 非阻断建议

- **S1 同步 `End(CharacterId)` 的回退扫描**（`DungeonRunDomainService.cs:1381-1392`）
  - 现状：`RunOf` 失败时，按 `HoldsRunState` 线性扫描全部副本。这是一条“由角色找副本”的第二路径，不经过队伍。
  - 影响：目前没有生产调用，约 155 处测试在用，所以不违反 RQ-004 的“不维护索引”；但它挂在公开接口上，将来的生产调用方可能把它当作通用的“按角色结束”。
  - 建议：在接口注释里写明它只用于“已离队后的状态清理”，不能当作按角色查找副本；或者把它收窄为测试辅助。
- **S2 断线／放弃副本在协议层没有直接断言逐人副作用**
  - 现状：两人场景下，协议层表征没有断言占用、生命周期、虚弱。
  - 判断：两条路径都调用领域层已经表征过的同一个 `EndAsync`，现有证据成立。若想在协议层直接钉住，可以给 `AssertDetached` 补占用登记与 `participant-left` 计数。
- **S3 局部变量命名**：`PreparePartyEntry` 里的局部变量 `frozen` 表示入场名单，建议改名 `entryRoster`，与新术语一致。
- **S4 文档补一句 R4 不变量**：见下节。
- **S5 A21 验收文档的场景已过时**：`Document/PartyKey-Dungeon-Membership-A21-Acceptance.md` 中“新加入队伍但未入场的 C”这一场景，现在已被显式守护拒绝（基准中也因城镇要求不可达）。建议把该场景的期望改为“邀请或接受被拒”。

## R4 建议：开新局只结束同队旧副本

- **差异**：基准在入场时把每名入场者从任意副本拆离（`runsByCharacter`）；现在只对同一 `PartyKey` 的旧副本 `EndRun`（`:141`，以及事务路径 `EntryCommit`）。差异只在一种状态下显现：某名入场者在另一队伍键的副本里仍持有未清理的本局状态。
- **可达性**：在合法操作下不可达。
  - 所有个人离开路径（退队、被踢、放弃副本、断线）都在同一作业内先离队、紧接着 `EndAsync` 清理。
  - 队伍解散只发生在最后一人离队时，同样会走 `EndAsync`。
  - 运行中的角色（包括独自处在虚拟队伍中的）无法加入其他队伍：基准的城镇要求，加上本次的 `PartyRunAdmission`。
  - 断线时队伍清理失败（R1），角色仍在原队伍，以后的入场也只会是同一队伍键。
- **若被违反的后果**：旧副本以后 `EndRun` 时，会对该角色调用 `ResumeRecovery` 与 `ChangeCombat(false)`，影响他在新副本中的虚弱回复暂停与宠物战斗标志。占用登记按归属精确释放，不会误删新局的占用。后果有界。
- **建议：接受并记录，不补“拆离任意副本”的防御。** 理由：
  1. 防御需要按 `HoldsRunState` 扫描全部副本，等于重新引入“按角色定位副本状态”的旁路，与本任务删除按角色索引的目标相悖。
  2. 在新模型下，替别的队伍结束或拆离他们的副本，语义本身就不对。
  3. 若日后要加防护，更合适的形式是 fail-fast：入场时检测到即拒绝并记录日志，暴露不变量被破坏，而不是静默拆离。
- **最小记录动作**（非阻断）：在 `ReactiveDungeonCellRuntime.md`“副本成员”一节，或在 `PreparePartyEntry` 的注释中，补一句不变量：“入场者不会在其他队伍的副本中持有本局状态，因为每条离队路径都在同一作业内清理；开新局因此只结束本队旧局。”

## 非阻断风险

- **R-a（TASK-005 R1）断线时队伍清理抛异常**：多人队伍中，随后的 `EndAsync` 会被拒绝，占用登记残留到整局结束；此时队伍与副本口径一致。文档已记录。
- **R-b（TASK-005 R3）已离队未清理者的生命周期记录口径不一**：在“已离队但未清理”的中间态，清场回城与全灭的生命周期只按快照成员记录，`EndRunAsync` 则按全部未清理者记录。该中间态不可达。
- **R-c 放弃副本的 `ended` 判定**：`ended` 由“退队成功且按角色已找不到原副本”推断（`GamePartyTransitionCommandHandler.cs:340`），没有使用内部 `EndAsync` 的返回值。在可达状态下两者等价。
- **R-d PVF 依赖的用例未执行**：`AltarLifecycleTests.Member_departure_…` 调整了退队顺序，但本机因缺 PVF 跳过。建议在具备 PVF 的环境中补跑，作为 AC-012 的补充证据。
- **R-e 守护未覆盖的另一条队伍键变化**：独自在副本中的角色若经 `CreateOrUpdateSettings` 建立真实队伍，`KeyOf` 会改变，原虚拟队伍的副本将不可达。该 CMD 要求角色在城镇（`TryCapture`），所以不可达，基准也同样如此。RQ-007 只要求“增员”守护，这里记录为残余边界。

## 文档（重点 6）

- 新增的“副本成员”一节（`ReactiveDungeonCellRuntime.md:26-48`）逐条与代码一致：
  - 成员定义 ↔ `Participants`；
  - 找副本 ↔ `RunOf`／`KeyOf`；
  - 一支队伍一个副本、开新局结束旧局 ↔ `runsByParty` 与 `PreparePartyEntry`；
  - 个人离开时的拒绝与“最后一人即整局结束” ↔ `Depart`；
  - 整局结束的六条路径，以及“非队长选图回城不结束” ↔ `EndRunAsync` 调用方；
  - 逐人副作用分路径的有无 ↔ `RecordDeparturesAsync` 与两个 TryEnd*Async。文档把 Eplp、选图回城与个人离开归为一类（记录生命周期并可能设失败虚弱），与基准逐人 `EndAsync` 和 TASK-003 表征一致。
  - 守护 ↔ `PartyRunAdmission`；断线残余 ↔ R-a。
- 一个事实一个归属：规则只在此节维护；`StoryPartyDungeon-20260914.md` 改为链接引用；职责表与目标段同步改为“所属队伍键与入场名单”。接口 XML 注释与 `PartyRunAdmission` remarks 是就地契约与不变量说明，符合规范中“仅在读某段代码时有用的原因或不变量”的就地注释许可。
- 正文没有演化叙述，也没有评审轮次。
- 残留的“ActiveParticipants／冻结参与者”只出现在 `Document/tasks/` 下的历史任务稿和 `CycleQuestNativeSubmit-20261020.md` 的取证记录里，不是现行规则文件，不要求修改。
- 只有 S4、S5 两项可选补充，判定满足 RQ-011／AC-013。

## 结论

```yaml
kind: rks-review
verdict: approved
```

完整改动满足 Spec 的 RQ-001～RQ-011。AC-001～AC-013 满足；AC-014 与 SC-8 待主代理的完成验证提供全量测试输出。

- 副本成员只有一个来源；`Released` 一类标记只表达本局状态清理。
- 表征测试的断言在结构迁移中零改动。
- 依赖保持“副本 → 队伍”单向；守护与队长判定的放置符合协议 CMD 指南。

没有阻断问题。R4 建议接受并在文档或注释中记录不变量。S1～S5 与 R-a～R-e 为非阻断建议和残余风险。本批准不替代完成验证。
