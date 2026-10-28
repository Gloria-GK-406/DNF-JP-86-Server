# TASK-003 — 整队退出与选图回城的逐人副作用表征

## Task Unit Contract

- Revision: 1
- Outcome and references: 在未改动的基准上，钉住整队退出（Eplp）与选图回城（队长、有副本）两条路径中每名成员的逐人副作用：记录离场生命周期、在适用条件下设失败虚弱、释放占用登记。补上 TASK-002 快速审查指出的缺口（Plan T-001；Spec RQ-008、AC-007、AC-011）。
- Owned boundary: 只修改 `Server/tests/Dfo.Server.Application.Tests/World/DungeonDepartureProtocolCharacterizationTests.cs`，不改生产代码。
- Required checks:
  - 构建无错误；
  - `DungeonDepartureProtocolCharacterizationTests` 全部通过；
  - 变异检查：分别临时去掉 `EndAsync` 中的生命周期记录与失败虚弱，确认新断言失败；还原后 `git diff -- Server/src` 为空。
- Dependencies: TASK-002
- Result review: not-required — 改动只是在两个已审过的测试上追加断言。变异检查能直接证明新断言抓得住被去掉的副作用；断言写错会表现为基准上失败或变异时不失败，两者都由必需检查暴露。

## Detailed Plan

在现有两个用例（`Eplp_by_the_leader_...`、`Back_to_village_by_the_leader_with_a_run_...`）的末尾，为两名成员追加断言：
- `domain_events` 中 `event_type='dungeon.participant-left'` 的行数恰为 2；
- 占用登记 `IDungeonParticipationDomainService.Find` 对两人都为空；
- 失败虚弱：结束前读出副本布局的 `AppliesFailureWeakness`，并确认当前 Cell 未清场。适用时两人的虚弱值都大于入场前的值，不适用时与入场前相同。

入场前的虚弱值在 `EnterAsync` 之后、发请求之前读取。

## Result

- Outcome: done
- Round: 1；owned change：`Server/tests/Dfo.Server.Application.Tests/World/DungeonDepartureProtocolCharacterizationTests.cs`。生产代码未改。
- Changed:
  - `Eplp_by_the_leader_...` 与 `Back_to_village_by_the_leader_with_a_run_...` 末尾新增 `AssertEachMemberDeparted`：`dungeon.participant-left` 领域事件增加 2 条；两人的占用登记为空；两人的虚弱值等于按规则算出的预期值。
  - 新增 `CaptureDepartureBaseline`：在请求前读取事件数、各人虚弱值，并按 `AppliesFailureWeakness`、未清场、`IWeaknessRuleService.InitialValue(等级)` 算出各人退出后应有的虚弱值。它会断言至少一名成员的虚弱值会变化，否则判定夹具失效。
- Why:
  - 虚弱值 100 表示“没有虚弱”，失败惩罚会把数值降到该等级的初始值；低于最低等级的角色在基准上调用了 `SetWeakness`，但数值不变。夹具中队长等级低于门槛、队员为 65 级，所以按规则逐人计算预期值，而不是统一断言“下降”。
  - 生命周期以持久化的领域事件计数观察，不另做替身。
- Checks:
  - `dotnet build`（Application.Tests）无错误。
  - `DungeonDepartureProtocolCharacterizationTests`：11 通过、0 失败。
  - 变异检查（只运行这两个用例），每项两例都失败；检查后还原，`git diff -- Server/src` 为空：
    - 把 `EndAsync` 的生命周期记录改为空块；
    - 把 `EndAsync` 的失败条件置假；
    - 把 `DetachParticipant` 的 `ReleaseCommitted` 改为空块。
