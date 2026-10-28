# TASK-005 — 副本改为挂在队伍上：内部结构与离开路径切换

## Task Unit Contract

- Revision: 1
- Outcome and references: 交付 Plan T-003（BD-003～BD-005），覆盖 Spec RQ-001、RQ-004～RQ-006、RQ-008、RQ-009，以及 AC-001、AC-002、AC-004～AC-008、AC-010、AC-012。
  - 副本成员视图只由“副本的队伍当前成员 ∩ 入场名单”得出。
  - 副本服务不再持有 `ActiveParticipants` 与 `runsByCharacter`。
  - 角色经 `KeyOf` 找到所属队伍的副本。
  - 个人离开与整局结束按 Spec 语义执行，所有调用方按新语义调整。
- Owned boundary:
  - 生产代码：
    - `Server/src/Dfo.Server.Application/World/DungeonRunDomainService*.cs`；
    - `Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs`：新增整局结束入口，调整 `EndAsync` 的文档注释；
    - 所有调用 `End`、`EndAsync`、`TryAbortEntry` 或依赖逐人结束语义的生产调用方，至少包括 `GamePartyTransitionCommandHandler*.cs`、`DungeonSelectionCommandPhases.cs`、`SelectCharacterLifecycleServices.cs`，其余以 grep 结果为准。
  - 测试：`Server/tests/` 下与上述改动直接相关的测试，以及新增测试。
  - 不在本任务：运行中队伍不能增员的守护（T-004），接口注释与文档的最终整理（T-005）。
- Required checks:
  - 构建无错误。
  - 完整 `dotnet test Server/86JP.Server.slnx` 0 失败。
  - TASK-001～TASK-003 的表征测试文件断言零改动，以 `git diff` 证明；若必须改动构造或夹具，逐行列出。
  - 新增覆盖 AC-004、AC-006、AC-008、AC-010 的测试；AC-001、AC-005、AC-007 由既有表征测试覆盖。
  - AC-002 的代码检查结论写进 Result：`DungeonRunAggregate` 无可变成员集合、服务无按角色到副本的索引。
- Dependencies: TASK-004
- Result review: required — 这是整个任务的核心改动，影响所有普通地下城玩法，并改变多条离开路径的内部顺序。漏掉一个调用方或一项逐人副作用，就会出现“已退队但副本仍留着本局状态”，或者整局结束时漏记生命周期这类问题。

## Detailed Plan

### 副本内部（`DungeonRunDomainService`）

1. **索引**：删除 `runsByCharacter`，新增 `PartyKey → 聚合` 的索引；`runsById` 保留。
   - `Find(CharacterId)`：用 `parties.KeyOf(角色)` 得到队伍键，查到副本后，再确认该角色属于副本成员。
   - 服务内部所有按角色找聚合的地方都改用同一个私有查找。
2. **成员**：删除 `ActiveParticipants`。
   - `participantsByCharacter` 收窄为入场名单（角色 → 账号）。
   - `Participants` 等于队伍当前成员中属于入场名单的角色，槽位与队长取自队伍；队伍不存在时为空。
   - 原先读 `ActiveParticipants` 的地方，一律改用当前成员集合。
   - 每名成员的运行状态（进度、准备、死亡、增益、翻牌等）仍按角色存放，在清理时移除。
3. **逐人清理**：把 `DetachParticipant` 拆成两部分：
   - **逐人清理**：承载 Spec RQ-008 中所有路径共有的副作用；
   - **移除副本**：承载副本级记录的清理。
4. **结束入口**：
   - `EndAsync(角色, runId)` 按 `runId` 找到副本：
     - 角色已不是成员、而副本还有其他成员：只做逐人清理，外加生命周期与有条件的失败虚弱；
     - 角色是副本唯一的成员（含单人虚拟队伍），或已没有任何成员：对剩余者做同样处理，然后移除副本；
     - 角色仍是成员、且还有其他成员：拒绝，返回 false。这是违反不变量的调用，调用方必须先让他退队。
   - 新增按 `runId` 的整局结束（接口方法，名称在实现时确定，例如 `EndRunAsync`）：对全体成员执行与 `EndAsync` 相同的逐人副作用（生命周期加有条件的失败虚弱），然后移除副本，返回被结束的成员。供整队退出与选图回城使用。
   - 清场回城、全灭、入场中止、开新局：保留各自现有的副作用组合（见 Spec RQ-008），改为调用共用的逐人清理和副本移除。
   - `PreparePartyEntry`：同一队伍键已有副本时，先对旧副本全体做“只拆离”（无生命周期、无失败虚弱），再移除旧副本。
   - `TryAbortEntry`：用当前成员集合与 `expectedRecipients` 比对。

### 调用方

5. **放弃副本**（`GamePartyTransitionCommandHandler`，约 320-345 行）：
   - 先确认存在副本。真实队伍先退队，再 `EndAsync`；虚拟队伍直接 `EndAsync`，这时会整局结束。
   - 保持“没有副本就不退队”的现有行为，见既有测试 `GiveUp_without_an_active_run_does_not_leave_the_party`。
   - 帧的产生顺序必须与 TASK-002 的表征一致。
6. **断线拆除**（`SelectCharacterLifecycleServices`）：先队伍清理，再 `EndAsync`；保持通知计划和投递顺序不变，以 TASK-002 的断线表征为准。
7. **整队退出**（`GamePartyTransitionCommandHandler.Exit.cs` 的逐人结束）：改用整局结束入口，返回值与原“已结束成员列表”一致。
8. **选图回城**（`DungeonSelectionCommandPhases`）：请求者是副本所属队伍的队长时整局结束；非队长不结束副本（Spec RQ-006、AC-008）。逐人 `ParticipantDeparted` 与回城投影保持不变。
9. **其他调用方**：grep `\.End(`、`EndAsync(`、`TryAbortEntry(` 的全部生产调用（祭坛退出、大会、续选等），逐个按“个人离开”或“整局结束”归类调整，并在 Result 中列出每处归类。

### 测试

10. 新增：
    - **AC-004**：真实队伍、虚拟队伍分别按角色找到副本；退队后找不到；同队开新局后按任一成员找到新副本。
    - **AC-006**：单人放弃副本或断线后整局移除。
    - **AC-008**：非队长选图回城不结束副本。
    - **AC-010**：各路径执行中和执行后读取视图都不抛异常，且恰有一名队长。
    - **AC-002 的反例**：角色仍在队伍中、且有其他成员时，`EndAsync` 被拒绝，副本不变。
11. 既有测试中依赖“不退队直接 `End(成员)`”拆掉多人副本的，改为先退队或改用整局结束；只改夹具，不改断言。

### 检查

完整测试；用 `git diff` 证明表征文件的断言未变；把 AC-002 的代码检查结论写进 Result。

## Result

- Outcome: 完成。副本只挂在队伍上：成员视图只由“队伍当前成员 ∩ 入场名单”得出，角色经 `KeyOf` 找到队伍再找到副本；`ActiveParticipants` 与 `runsByCharacter` 已删除；个人离开与整局结束按 Spec 语义执行，全部生产调用方已调整。未提交。
- Round 1，改动文件分类：
  - 生产代码（10 个）：
    - 副本服务：`Application/World/DungeonRunDomainService.cs`、`.Transitions.cs`、`.Objects.cs`、`.Mechanisms.cs`、`.DynamicActors.cs`；
    - 接口：`Abstractions/World/IDungeonRunDomainService.cs`（新增 `EndRunAsync`，改写 `EndAsync` 注释）；
    - 调用方：`Protocol/Game/GamePartyTransitionCommandHandler.cs`（放弃副本、Eplp）、`GamePartyTransitionCommandHandler.Exit.cs`（整队退出）、`Protocol/Game/DungeonSelection/DungeonSelectionCommandPhases.cs`（选图回城）、`Application/Sessions/SelectCharacterLifecycleServices.cs`（断线）。
  - 新增测试（2 个）：`Application.Tests/World/DungeonRunDomainServiceTests.PartyOwnedRuns.cs`（领域）、`Application.Tests/World/DungeonPartyOwnedRunProtocolTests.cs`（生产容器）。
  - 既有测试只改夹具或登记（5 个）：
    - `Application.Tests/Execution/GameplayWriteEntryTableTests.cs`：把 `EndRunAsync(DungeonRunId)` 登记为写入（W）。
    - `Application.Tests/Game/SelectCharacterReturnTests.cs`：清理服务原本另建了一个 `PartyService`，与副本读取的队伍不是同一个；改为传入 `TestParties.Of(runs)`，断线退队才能落到副本所属的队伍上。断言未改。
    - `Protocol.Tests/Support/DungeonExitDrivers.cs`：`EndOrdinaryRunCoreAsync` 去掉成员参数，驱动随之少传一个参数；命令记录与断言未改。
    - `Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs`、`Protocol.Tests/Game/DungeonEntry/PartySelectDungeonHandlerTests.cs`：替身 `OneRun`、`RollbackRuns` 补 `EndRunAsync`，语义与原逐人 `End` 相同（记录同样的结束者）。
- Changed：
  - 副本服务内部：
    - 索引：`runsByParty`（`PartyKey → 聚合`）加 `runsById`。私有 `RunOf(角色)` 经 `parties.KeyOf` 取副本并确认角色是成员；所有按角色查找（含 `MoveCommit.Prepare` 与对象破坏）统一改用它。
    - 成员：聚合只保留不可变的入场名单（`ImmutableDictionary<CharacterId, AccountId>`）和按入场者固定键的进度（`ImmutableDictionary<CharacterId, ParticipantProgress>`）。`Participants` 取队伍当前成员中属于入场名单的角色，槽位与队长取自队伍。原先读 `ActiveParticipants` 的收件人、翻牌公开、全灭判定、入场中止比对等，一律改读 `Participants`。
    - 逐人清理 `Release`：释放占用登记、恢复虚弱回复、关闭宠物战斗、移除准备与死亡标记、`EntryRevision++`、清理该角色的结算与翻牌领取记录，并把进度标为 `Released`。副本移除 `Remove` 负责解除两个索引、唤醒机制、清理清场回城与全灭期限、清理死亡、参战效果、结算、地面掉落与翻牌领取记录。`EndRun` = 对仍持有本局状态者逐人清理，再移除副本。
    - `EndAsync` / `End(角色, runId)` 共用 `Depart`：角色仍是成员且还有其他成员时拒绝；角色已离队而副本还有成员时，只清理他；角色是唯一成员或已无成员时整局结束。`EndAsync` 另外为每名结束者记录生命周期，未清场且布局适用时设失败虚弱。`End(角色)` 先 `RunOf`，找不到时取仍持有该角色状态的副本（线性查找，不维护索引），供测试和退队后的单人结束使用。
    - 新增 `EndRunAsync(runId)`：`EndRun` 后对每名结束者执行与 `EndAsync` 相同的生命周期与有条件失败虚弱，返回结束者（当前成员按槽位排在前）。
    - 清场回城、全灭、入场中止改调 `EndRun`，保留各自原有的副作用组合。`PreparePartyEntry`（含事务内的 `EntryCommit`）对同一队伍键的旧副本做 `EndRun`（只拆离），再 `Publish`；事务 Prepare 改为校验队伍键绑定未变。
  - 调用方逐个归类（`grep -E "\.End\(|EndAsync\(|TryAbortEntry\(|EndRunAsync\(|TryEnd(PartyWipe|ClearReturn)Async\("`，排除 `OutboundSender` 的同名无关方法）：

    | 调用点 | 归类 | 处理 |
    |---|---|---|
    | `GamePartyTransitionCommandHandler.cs` `GiveUpAsync`（约 330-345 行） | 个人离开（放弃副本） | 有副本时先 `MutatePartyAsync(Leave)`：真实队伍内退队，并在其中 `EndAsync`。虚拟队伍退队被拒（`NotInParty`），或退队未生效时，直接 `EndAsync`（唯一成员即整局结束）。没有副本则不退队，与原行为一致。`ParticipantDeparted` 与投影顺序不变 |
    | `GamePartyTransitionCommandHandler.Stages.cs:176` `MutatePartyStateAsync` | 个人离开（退队、被踢） | 原本就是先改队伍、再 `EndAsync`，未改 |
    | `GamePartyTransitionCommandHandler.Exit.cs` `EndOrdinaryRunCoreAsync`（Eplp） | 整局结束（整队退出） | 改为 `EndRunAsync(runId)`，返回值替代原逐人“已结束成员列表” |
    | `DungeonSelectionCommandPhases.cs` `BackToVillageAsync` | 整局结束（选图回城，仅队长） | 请求者是所属队伍队长时 `EndRunAsync`，对每名结束者 `ParticipantDeparted`；非队长不结束副本（AC-008）。回城投影不变 |
    | `SelectCharacterLifecycleServices.cs` `CleanupAsync` | 个人离开（断线） | 在原位置先 `Find` 记下副本；`EndAsync` 和 `ParticipantDeparted` 移到 `parties.CleanupCharacter` 之后。通知计划的组成与投递未变 |
    | `DungeonEntryCommandPhases.cs:449` `RollbackEntry` → `TryAbortEntry` | 整局结束（入场中止，只拆离） | 调用方未改；服务内改为以当前成员集合比对 `expectedRecipients`，再 `EndRun` |
    | `DungeonReturnTimerCommands.cs:95-96` → `TryEndClearReturnAsync` / `TryEndPartyWipeAsync` | 整局结束（清场回城、全灭） | 接口未变；服务内改用 `EndRun` |
    | `DungeonEntryCommandPhases` 经 `PreparePartyEntry` | 整局结束（同队开新局，只拆离） | 服务内按队伍键结束旧副本 |
    | 同步 `End(...)` | — | 无生产调用，只有测试使用 |

  - AC-002 代码检查结论：
    - `DungeonRunAggregate` 中与人员相关的只有不可变的 `entryParticipants`（`ImmutableArray`）和 `entryAccounts`（`ImmutableDictionary`），以及键集固定为入场者的 `progressByCharacter`（`ImmutableDictionary`，只有值对象的字段会变）。成员由 `Participants` 每次向队伍询问。
    - `ReadyParticipants`、`DeadParticipants`、`BuffParticipants` 是每名成员的本局状态标记，不参与成员判定。
    - 服务只有 `runsById` 与 `runsByParty` 两个副本索引；其余字典以领取键（含 `RunId`）为键，不能由角色定位副本。
    - 唯一构造器要求非空 `IPartyService`（TASK-004）。
    - 队伍服务未引用副本服务（`grep IDungeonRun Application/Parties` 为空）。
- Why：
  - 进度不删除，只标记 `Released`，与 Detailed Plan 第 2 条字面的“清理时移除”不同。`CommitPreparedClearRewardCard` 在角色离开后仍要读其进度：源码注释写明，已 Prepare 的翻牌提交在参与者断线后必须照常完成、不得失败。删除进度会让它抛 `KeyNotFoundException`。`Released` 只用于“同一角色只离开一次”（重复 `EndAsync` 返回 false，避免重复记录生命周期），不参与成员判定，因此不是第二份成员名单。
  - `EndRun` 清理的是“入场名单中仍持有本局状态者”，不只是当前成员。正常情况下两者相同，因为每条离队路径都在同一作业内紧接着 `EndAsync`。若出现“已离队但尚未清理”的中间态，整局结束时这些人也会被清理，不会泄漏占用登记。`EndAsync` 在最后一名成员离开时同样这样处理，与 Plan 第 4 条“对剩余者做同样处理”一致。
  - 放弃副本复用 `MutatePartyAsync(Leave)`。它内部本来就是“退队，然后 `EndAsync`”，因此不需要另写一套。是否已结束沿用 `Stages.cs` 的同类判断（退队成功后按角色已找不到原副本）。真实队伍与虚拟队伍不按 `PartyKey` 分支，而是由 `LeaveParty` 对虚拟队伍返回 `NotInParty` 自然分流，这样对不带队伍键的测试替身也成立。
  - 选图回城的队长判断用请求者所在队伍的 `LeaderWireUserId`（同一处理器的 `ResolveReturningMembersAsync` 与进入选图也这样判断）。副本经请求者的队伍找到，所以这个队长就是副本成员视图里的队长。改用副本视图判断会让 `Participants` 为空的替身（`LeaveDungeonTests.OneRun`）失效。
  - 断线路径只移动副本结束这一段，保持在双塔离开之后。副本查找留在原位，在队伍清理前完成，因为退队后按角色已找不到副本。
  - `EndRunAsync` 的接口默认实现抛 `NotSupportedException`，与 `PreparePartyEntry` 的写法一致。漏实现的替身会在用到时明确失败，而不是静默返回空列表。实际用到它的替身只有 2 个，都已补上。
- Checks：
  - `cd Server && dotnet build 86JP.Server.slnx`：0 警告，0 错误。
  - `dotnet test Server/86JP.Server.slnx` 全量，失败均为 0：Pvf 73 通过／9 跳过；HttpApi 59／0；Infrastructure 655／348；Protocol 1733／60；Networking 189／20；Persistence 484／0；Host 200／12；Application 5496／493。跳过项均为缺 PVF 的内容测试。
  - 表征测试：`git diff -U0` 对 `DungeonRunDomainServiceTests.Departures.cs`、`DungeonDepartureProtocolCharacterizationTests.cs`、`PartyCommandHandlerTests.cs` 输出为空（0 行），三份文件零改动且全部通过。
  - 新增测试（19 个用例，全部通过）：
    - AC-004：`Members_find_the_run_of_their_current_party_and_lose_it_when_they_leave`（真实队伍与虚拟队伍分别找到；退队后找不到）、`A_new_run_of_the_same_party_is_the_one_every_member_finds`。
    - AC-006：`Giving_up_alone_ends_the_whole_run`（生产容器，单人放弃副本：副本、投递登记、占用登记清空，生命周期 +1）；单人断线已由 TASK-002 的 `Disconnecting_alone_ends_the_whole_run_and_sends_nothing` 覆盖，领域层单人结束由 TASK-001 的 `Solo_departure_ends_the_whole_run` 覆盖。另有 `The_last_member_departing_ends_the_whole_run`（[直接结束, 先退队]）。
    - AC-008：`Back_to_village_by_a_member_does_not_end_the_run`（生产容器：两人仍在同一副本，队长不变，投递登记、占用登记、队伍不变，无生命周期）。
    - AC-010：`Views_have_exactly_one_leader_during_and_after_every_exit`，8 条路径：队员退队、队长退队、被踢、整局结束、清场回城、全灭、入场中止、同队开新局。在每次逐人清理内部（`ResumeRecovery`、`SetWeakness`）、退队与 `EndAsync` 之间、以及路径结束后，读取各角色视图和按 `RunId` 的视图，均不抛异常，且恰有一名队长。
    - EndAsync 拒绝反例：`A_member_still_beside_others_in_the_party_cannot_end_the_run_alone`（[队员, 队长]）。`EndAsync`、`End(角色, runId)`、`End(角色)` 都返回 false；副本 `EntryRevision`、成员、队长不变；无任何离场副作用。
    - 其他：`A_departed_member_departs_only_once`、`Ending_the_whole_run_departs_every_member_and_keeps_the_party`、`Ending_the_whole_run_after_clear_applies_no_failure_weakness`。
  - 变异检查（逐项打补丁、构建、运行，之后用备份还原；还原后复跑全量）：
    - 去掉 `Depart` 的拒绝条件：拒绝反例 2 例失败。
    - 选图回城的队长条件恒为真：AC-008 失败。
    - `Participants` 额外按 `Released` 过滤，模拟第二份名单：AC-010 的整局结束、清场回城、全灭、入场中止、开新局 5 例失败。
- Remaining：
  - 本单元未处理 T-004（运行中队伍不能增员的守护）。在它之前，“已清理的角色重新回到同一队伍”的情况只因邀请与接受都要求双方在城镇区域而不可达。T-005 的接口注释与文档未处理：`IDungeonRunDomainService` 的类注释仍写“frozen participants”，`End(CharacterId)` 的注释仍是旧语义，`Document/` 未改。
  - `PreparePartyEntry` 只结束同一队伍键的旧副本。基准会把每名入场者从任意副本拆离，例如入场者还残留在另一队伍或自己虚拟队伍的副本里。按 Spec 的可达性说明，所有回城路径都会结束或离开副本，这种残留不可达；而占用登记允许普通副本之间替换，所以这里不会被兜住。如需防御，可在 T-004 或评审时决定是否补上。
  - 断线时若 `parties.CleanupCharacter` 抛异常，随后的 `EndAsync` 会因角色仍在多人队伍中而被拒绝，副本保留该角色。此前两者互不依赖。异常属于非预期路径，这里不另做兜底。

## Review Round 1

```yaml
kind: rks-review
verdict: approved
```

- 类型：Independent Quick Review（独立快速审查），审查执行 Round 1 的 Result；合同 Revision 1，无修订。
- 审查对象：分支 `task/dungeon-participants-20261027`，HEAD `53265b79` 之上的未提交改动，包括 10 个生产文件、5 个既有测试文件，以及新增的 `DungeonRunDomainServiceTests.PartyOwnedRuns.cs` 和 `DungeonPartyOwnedRunProtocolTests.cs`。
- 依据：`reviewing-work` 的 task-unit 与 code 准则；本单元合同与 Detailed Plan；Spec 的术语、RQ-001～RQ-010、可达性说明与 AC 表；Plan 的 BD-003～BD-005；基准 `53265b79` 中的 `DetachParticipant` 与 `EndAsync`。按要求未运行构建或测试，测试结果以执行者自报为准，由主代理独立核对。

### 结论

本单元交付了合同承诺的内容，未发现阻断缺陷。

1. **成员只有一个来源（RQ-001、RQ-004、AC-002）**
   - 聚合不再有 `ActiveParticipants`，服务不再有 `runsByCharacter`。`Participants` 只由“队伍当前成员 ∩ `entryAccounts`”得出；`RunOf` 先 `KeyOf`，再查 `runsByParty`，然后确认 `IsMember`。
   - `Released`、`HoldsRunState`、`Unreleased()` 只在三处使用：`Depart` 的入口条件（防止同一角色重复离开）、`EndRun` 决定清理谁，以及只有测试调用的同步 `End(CharacterId)` 回退查找。没有任何成员判定、收件人、队长或视图读取依赖它们。
   - 在可达状态下，`Released` 不会与“队伍成员”同时成立：非成员分支只清理已离队的角色；最后一名成员分支会连同副本一起移除。因此它表达的是“本局状态是否已清理”，不是第二份成员名单。执行者的变异检查也证明了这一点：让 `Participants` 按 `Released` 过滤后，5 个用例失败。
   - 同步 `End(CharacterId)` 的线性查找只有测试调用，属于扫描而非维护的索引，不违反 RQ-004。
2. **RQ-008 逐人副作用**
   - `Release` 逐项保留了基准 `DetachParticipant` 的每人副作用：释放占用（按键精确释放）、`ResumeRecovery`、`ChangeCombat(false)`、移除准备与死亡标记、`EntryRevision++`、清理该角色的结算领取，以及未 Prepare 的翻牌领取。`Remove` 保留了副本级清理的全部 9 项：两个索引、唤醒，以及两类期限和 5 类领取记录。唯一删掉的是对 `ActiveParticipants` 的移除。
   - 各路径中，生命周期与失败虚弱的有无与 RQ-008 一致：
     - `EndAsync` 与 `EndRunAsync`：都记录生命周期，未清场且布局适用时设失败虚弱；
     - 全灭：记录生命周期，并在布局适用时设失败虚弱；
     - 清场回城：只记录生命周期；
     - 入场中止与开新局：两者都不产生。
   - 基准从未删除进度，所以保留进度只加标记，与基准一致。`CommitPreparedClearRewardCard` 确实在角色离开后仍要读取进度。
3. **调用方顺序与行为**
   - 放弃副本复用 `MutatePartyStateAsync`，形成“先退队，再 `EndAsync`”。虚拟队伍退队时返回 `NotInParty`，随后直接 `EndAsync`，作为唯一成员整局结束。没有副本时不退队，保持原行为。
   - 断线只把 `EndAsync` 与 `ParticipantDeparted` 移到 `CleanupCharacter` 之后，`Find` 仍留在原位；通知计划的组成未变。
   - Eplp 在 LeaveState 下要求 `returning.Count == roster.Members.Length`，因此 `EndRunAsync` 的结束集合与基准逐人结束的集合相同。
   - 选图回城按请求者所在队伍的队长判断，虚拟队伍的 `LeaderWireUserId` 就是本人，符合 RQ-006 与 AC-008。
   - 三份表征文件 `git diff --quiet` 为零改动，本审查已核实。既有测试只改了夹具或登记，未改断言。
4. **`EndAsync` 拒绝与调用方归类**
   - 本审查自行 grep 了 `\.End(`、`EndAsync(`、`EndRunAsync(`、`TryAbortEntry(`。生产调用点只有 Result 表中列出的那些，同步 `End` 没有生产调用；`IDungeonRunDomainService` 也只有一个生产实现，所以 `EndRunAsync` 的默认抛出实现不会在生产中触发。
   - 所有个人离开路径都先让角色离开队伍，不会触发拒绝分支。
5. **开新局只结束同队旧副本**
   - 与基准的差别只在一种情况下显现：入场者在其他队伍键的副本里还留有未清理的状态。现有路径都在同一作业内紧接着退队执行 `EndAsync`，只有断线时队伍清理抛异常才可能留下这种状态，属于不可达或非预期路径。按 Spec 的可达性说明，可以接受，不阻断。
6. **测试覆盖**
   - AC-004：领域测试分别覆盖真实队伍与虚拟队伍、退队后找不到、同队新局。
   - AC-006：生产容器中的单人放弃，断言了副本、投递、占用和生命周期；单人断线由 TASK-002 覆盖。
   - AC-008：生产容器中非队长选图回城，断言了副本、成员、队长、占用、投递、队伍和生命周期都不变。
   - AC-010：8 条路径，在逐人清理内部和路径结束后读取各角色视图与按 `RunId` 的视图，并要求至少读到一次进行中的视图。
   - 拒绝反例：三种结束入口都断言了返回 false、`EntryRevision` 不变、队长不变、无副作用。
   - 断言足以支撑 Result 的结论。

### 非阻断建议与残余风险

- **R1 断线异常路径**：`parties.CleanupCharacter` 抛异常时，多人队伍里的 `EndAsync` 会被拒绝，副本保留该角色，占用登记也不释放。此时队伍与副本口径一致，都认为他仍在，不构成第二份名单，可以接受。建议在 T-005 文档或日志中注明：这种失败会让占用登记残留到整局结束。
- **R2 放弃副本的 `ended` 判定**：`ended` 由“退队成功且按角色已找不到原副本”推断，`MutatePartyStateAsync` 内部 `EndAsync` 的返回值被丢弃。在可达状态下两者等价；若要更严谨，可以让内部结果透出。
- **R3 已离队未清理者的生命周期记录不一致**：在“已离队但未清理”的中间态下，`TryEndClearReturn` 与 `TryEndPartyWipe` 的 `EndRun` 会清理这些人，但只按快照中的当前成员记录生命周期；而 `EndRunAsync` 与 `EndAsync` 的整局结束会对他们一并记录生命周期。这个中间态不可达，只作记录。
- **R4 开新局的防御**：如需对“入场者在其他副本里还有未清理状态”做防御，建议在 T-004 或最终审查时决定。可选做法是入场时拒绝，但需要一次按 `HoldsRunState` 的扫描。
- **R5 文档残留**：`IDungeonRunDomainService` 的类注释“frozen participants”和 `End(CharacterId)` 注释仍是旧语义，按合同留给 T-005。
