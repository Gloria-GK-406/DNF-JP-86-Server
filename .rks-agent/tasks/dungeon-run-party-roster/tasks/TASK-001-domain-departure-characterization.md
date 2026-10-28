# TASK-001 — 副本领域层离开与结束路径的表征测试

## Task Unit Contract

- Revision: 1
- Outcome and references: 在未改动的基准代码上，用领域层自动测试钉住普通地下城副本各离开与结束入口的副本状态、队伍状态和逐人副作用。覆盖 Plan T-001 的领域部分，服务 Spec RQ-008、RQ-010、AC-011，并为 AC-001、AC-005～AC-007 提供基准。
- Owned boundary: 只新增或修改 `Server/tests/Dfo.Server.Application.Tests/World/` 下的测试文件：把 `DungeonRunDomainServiceTests` 改为 `partial`，新增 `DungeonRunDomainServiceTests.Departures.cs`，以及该文件内的记录型替身。不改任何生产代码。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx` 无错误；
  - `dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~DungeonRunDomainServiceTests"` 全部通过（新增测试在基准上通过）；
  - 对每个新增测试，临时注释掉 `DetachParticipant` 或 `EndAsync` 中对应的一项副作用，确认相应测试失败；只需确认，不保留改动，在 Result 中记录。
- Result review: required — 后续 T-002、T-003 都以这些测试作为“行为不变”的判据。表征缺项或断言过宽，结构切换时就漏掉行为变化，而后续检查无法发现。
- Dependencies: 无（基于规划提交 `d407b186`）

## Detailed Plan

**夹具**：沿用同文件 `Successor_can_advance_the_shared_run_after_the_original_leader_departs`（约 541 行）的写法，用真实 `PartyService` 组两人真实队伍，单人场景直接用虚拟队伍。副本服务用完整构造：

```text
new DungeonRunDomainService(transactions: null!, parties, weakness, creatures, lifecycle,
    clock, participation: participation)
```

`transactions` 传入与现有测试一致的值，具体按无事务路径的可用写法决定。

新增四个记录型替身，只记录本任务需要的调用，其余成员抛 `NotSupportedException`：
- 虚弱：`IWeaknessDomainService`/`IWeakness`，按角色记录 Pause、Resume、Set；
- 宠物运行时：`ICreatureRuntimeDomainService`，只记录 `ChangeCombat(角色, 值)`；
- 生命周期：`IDungeonLifecycleDomainService`，记录 `DungeonParticipantLeft`；
- 占用登记：`IDungeonParticipationDomainService`，用字典实现 `TryClaim`、`Find`、`ReleaseCommitted`、`Release`。

建立副本、清场开放、全灭等状态的写法，沿用同类中已有的私有辅助方法与布局（`BeginPartyWithCell`、`Tutorial`、`Clear_return_expires_once...`、`Party_wipe_waits_for_last_death...` 中的写法）。“失败虚弱”分支选一个 `AppliesFailureWeakness` 为真的布局；若现有布局都不满足，就构造一个最小布局。

**测试**：每条路径一个测试，A 为队长、B 为队员；“B 为队长”的变体用 Theory 参数。

1. **个人离开（退队、被踢共用领域路径）**：`parties.LeaveParty(B)`（或 `RemoveMember(A,B)`）后执行 `EndAsync(B, runId)`。断言：
   - B：`Find` 为空；占用已释放；虚弱 Resume；`ChangeCombat(B,false)`；生命周期 `DungeonParticipantLeft(B)` 一次；未清场的失败布局上 Set 一次，清场后不 Set；B 的准备与死亡标记从视图消失；B 之后的结算领取被拒。
   - A：原 `RunId` 不变，A 是唯一成员且为队长；A 的占用、虚弱、宠物战斗标志未变。
   - 队伍：只剩 A，A 为队长。
2. **副本内转让队长**：`TransferLeadership(A→B)`。断言：`RunId` 不变；视图中 B 为队长；两人都在；没有任何离场副作用（无 Resume、无 `ChangeCombat(false)`、无生命周期、无释放）。
3. **单人虚拟队伍 `EndAsync`**：副本被移除（按 `RunId` 查找为空），逐人副作用齐全。
4. **清场回城** `TryEndClearReturnAsync`：副本被移除；两人各有 Resume、`ChangeCombat(false)`、释放、生命周期一次；无失败虚弱；队伍成员与队长不变。
5. **全灭结束** `TryEndPartyWipeAsync`：同第 4 条，但在 `AppliesFailureWeakness` 布局上两人各 Set 一次。
6. **入场中止** `TryAbortEntry`：副本被移除；两人有 Resume、`ChangeCombat(false)`、释放；**无**生命周期、**无**失败虚弱（基准行为）；队伍不变。
7. **同一队伍开新局**：对同一份成员名单再次 `PreparePartyEntry`。旧副本按 `RunId` 查找为空，新副本可由两人找到；旧局两人有 Resume、`ChangeCombat(false)`、释放；无生命周期、无失败虚弱；队伍不变。

断言只针对可观察结果：副本视图、`Find`、替身记录、队伍快照。不断言内部集合。这样 T-003 删除内部集合后，这些测试不需要改断言。

**检查方法**：先运行新测试确认在基准上通过；再按 Required checks 第 3 项，逐项注释副作用，确认对应测试变红；最后还原。

## Result

- Outcome: done
- Round: 1；owned change：`Server/tests/Dfo.Server.Application.Tests/World/DungeonRunDomainServiceTests.cs`（类改为 `partial`，仅此一行）与新文件 `Server/tests/Dfo.Server.Application.Tests/World/DungeonRunDomainServiceTests.Departures.cs`。生产代码未改。
- Changed: 新增 10 个表征测试（3 个 Theory 用例加 7 个 Fact）和 4 个记录型替身（虚弱、宠物战斗标志、生命周期、占用登记）。覆盖的路径：
  - 两人真实队伍中队员或队长退队、队员被踢后 `EndAsync`；
  - 清场后个人离开（不设失败虚弱）；
  - 副本内转让队长（无任何离场副作用）；
  - 单人虚拟队伍 `EndAsync`（整局移除）；
  - 清场回城、全灭结束（整局结束，队伍不变，失败虚弱分别为无和有）；
  - 入场中止、同一队伍开新局（只拆离，无生命周期、无失败虚弱）。
- Why: 占用登记用记录型替身，没用真实的 `DungeonParticipationDomainService`。真实实现要求在事务中运行，而本任务只需钉住“是否以正确归属释放”，替身可以直接记录 `ReleaseCommitted` 的参数。断言只读副本视图、`Find`、队伍快照和替身记录，不读内部集合，这样 T-003 删除内部集合后无需改断言。
- Checks:
  - `dotnet build Server/86JP.Server.slnx` 依赖的测试项目构建无错误。
  - `dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~DungeonRunDomainServiceTests"`：88 通过、0 失败（含新增 10 个）。按快速审查的非阻断建议，提交前补了前置操作返回值的非空断言和“队伍只剩剩余成员”的断言，复跑仍 88 通过。
  - 变异检查：在 `DungeonRunDomainService.cs` 上逐项临时去掉以下副作用，每项都使上述筛选出现失败；检查后用备份还原，`git diff` 为空：
    - `ReleaseCommitted`；
    - `ResumeRecovery`；
    - `ChangeCombat(false)`；
    - 准备标记移除、死亡标记移除；
    - `EndAsync` 的生命周期记录与失败虚弱（后者以把 `failed` 条件置假的方式验证）；
    - 全灭路径的生命周期记录与失败虚弱；
    - 清场回城路径的生命周期记录。

## Review Round 1

```yaml
kind: rks-review
verdict: approved
```

### 审查范围

- 类型：Independent Quick Review（Task Unit 结果快速审查），只看本单元的明显局部缺陷，不作整体审批。
- 对象：Contract Revision 1、Result Round 1；owned change 为未提交的工作区改动，基于 `d407b186`：`Server/tests/Dfo.Server.Application.Tests/World/DungeonRunDomainServiceTests.cs`（仅加 `partial`）与新文件 `DungeonRunDomainServiceTests.Departures.cs`（364 行）。生产代码无改动。
- 依据：`reviewing-work` 的 Task Unit 与 Code 准则；spec.md 的 RQ-008、RQ-010、AC-011（并对照 AC-005～AC-007）；plan.md 的 T-001、BD-004、BD-005；被表征代码 `DungeonRunDomainService.cs` 中的 `PreparePartyEntry`、`TryAbortEntry`、`TryEndClearReturn(Async)`、`TryEndPartyWipe(Async)`、`EndAsync`、`DetachParticipant`、`DungeonRunAggregate.Participants/ViewFor`；真实 `DungeonParticipationDomainService`；调用方 `MutatePartyStateAsync`、放弃副本与断线拆除路径。

### 复核的检查

- `dotnet build Server/86JP.Server.slnx`：0 警告 0 错误（审查者复跑）。
- `dotnet test tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~DungeonRunDomainServiceTests"`：88 通过、0 失败（审查者复跑）。`--list-tests` 确认新增 10 个用例（3 个 Theory 用例加 7 个 Fact），基准原有 78 个。
- 变异检查未复做：审查者不得改生产代码。根据代码推断各项可检测：视图的 `ReadyParticipants`、`DeadParticipants` 直接读聚合的原始集合；释放与 `ChangeCombat(false)` 都按次数断言；`Resume` 断言要求存在。这与 Result 所述一致。

### 结论

1. **路径与断言覆盖**：合同列出的 7 类路径都有对应测试。个人离开覆盖了队员退队、队员被踢、队长退队三种组合，另有清场后离开的用例。逐人副作用中，释放占用（按归属 `Ordinary/RunId` 计数为 1）、`Resume`、`ChangeCombat(false)` 各一次，生命周期恰为一条且内容相等，失败虚弱按条件为 1 或 0，准备与死亡标记从存续视图中消失，离开后结算领取被拒，这些都有断言。整局结束的四条路径断言了按 `RunId` 查找为空、队伍成员与队长不变。转让队长断言了 `RunId` 不变、两人都在、B 为唯一队长、无任何离场副作用。副本级期限清理已有基准测试覆盖（`Clear_return_expires_once...` 第 92 行，`Party_wipe_waits...`）。
2. **断言是否依赖内部集合**：所有断言只读 `Find`、视图、队伍快照和替身记录。逐条按新模型（成员 = 队伍当前成员 ∩ 入场名单，`Find` 经队伍键查找）推演，结论都不变：个人离开后存续视图只有 A；整局结束后 `Find(run)` 与 `Find(角色)` 都为空；开新局后按角色找到新局。没有发现隐含依赖“副本自己维护名单”的断言。
3. **“先退队再 `EndAsync`”的顺序**：这正是基准中退队与被踢的真实顺序。`MutatePartyStateAsync` 先取得副本，再改队伍，然后调用 `EndAsync`。放弃副本和断线拆除在基准中是先 `EndAsync` 后退队，但领域层的副作用与顺序无关：`DetachParticipant` 不读队伍，`failed` 只取决于布局与当前 Cell，两种顺序的最终领域状态相同。因此这个顺序在领域层没有掩盖差异。两种顺序真正的差异在协议层（`ParticipantDeparted`、队伍通知的帧序与收件人），属于 Plan T-001 的协议部分，不在本单元范围内。
4. **替身是否失真**：`RecordingParticipation.Release` 委托给 `ReleaseCommitted`。真实实现在无事务时也是这样处理的，而本测试的 `transactions` 为 null，所以语义等价。`TryClaim` 恒为真且直接覆盖，真实实现对 Ordinary→Ordinary 同样允许覆盖，因此开新局用例中“旧局释放不影响新占用”的断言符合真实行为。其余替身只记录调用，未发现会让断言失真的地方。

没有阻断问题。

### 非阻断建议与残留风险

- **Result 计数有误**：Result 写的是“11 个表征测试（4 个 Theory 用例加 7 个 Fact）”，实际是 10 个（3 个 Theory 用例加 7 个 Fact），88 − 78 = 10。建议执行者提交前更正 Result。
- **Spec 与基准存在张力，需控制者裁定**：RQ-008 与 AC-007 的字面要求是入场中止、开新局也对每名成员“记录离场生命周期”，但基准在这两条路径上不记录生命周期，也不设失败虚弱。本单元按合同和 RQ-010 钉住了基准行为（无生命周期），这是正确的。按 BD-004，T-003 计划让整局结束共用逐人清理过程；如果把生命周期也并进去，这两个测试会失败。建议控制者在 T-003 前明确：要么保持基准，要么在 Spec 中把它列为行为修正。
- **逐人结算与翻牌领取记录的清理未被表征**：这项属于 RQ-008，但离开者此后已无法访问副本，公开接口上观察不到，测试只能验证“领取被拒”，而领取被拒本身来自 `Find` 失败。T-003 需要靠代码审查保证这项清理没有丢失。
- **前置操作的返回值未断言**：`TryMarkParticipantReady`、`TryMarkParticipantDead`、`TryOpenCardLayout`、`FixClearRewardCards` 的返回值没有检查。T-002 迁移夹具后，如果这些前置操作静默失败，“标记已移除”一类断言会空过。建议对前置操作加 `Assert.NotNull` 或等价断言。
- **个人离开用例的队伍断言不完整**：只断言了队伍的队长，没有断言合同要求的“队伍只剩 A”。这是队伍服务自身的行为，后续结构切换不会改动它，所以风险低。
- **协议部分尚待完成**：AC-005 中放弃副本、断线拆除在协议层的帧序与收件人，以及整队退出、选图回城两条路径，不在本单元范围内。按 Plan，它们须由 T-001 的协议部分在 T-003 之前补齐。
