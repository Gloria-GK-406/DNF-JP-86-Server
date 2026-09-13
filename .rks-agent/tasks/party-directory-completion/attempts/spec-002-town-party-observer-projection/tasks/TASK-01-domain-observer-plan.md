# TASK-01 — 领域 observer plan

## Task Unit Contract

- Revision: 1
- Outcome: party mutation 结果冻结准确、稳定、去重的 town-visible exact observers。
- Owned files: Party data model、Application party domain service 与对应 Application tests；本 Task 记录/证据。
- Required checks: TDD RED/GREEN；create/settings/accept/leave/disband；跨 scope、stale/replacement；既有 party tests。

## Result

### Round 1 — settings mutation observer snapshot

- RED command: `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PartyDomainServiceTests.SettingsMutationsFreezeOnlyCurrentVisibleNonMemberSessionsInCharacterOrder"`
- RED exit: `1`
- RED evidence: 单一目标测试在 `Assert.False(created.Notifications.TownObservers.IsDefault)` 失败；预期 `False`、实际 `True`，证明领域 mutation 尚未生成 observer 快照，而非测试装配错误。
- GREEN command: 同一命令；exit `0`，目标测试 `1/1` 通过。

### Round 2 — accept observer union

- RED command: `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PartyDomainServiceTests.AcceptFreezesTheObserverUnionAroundAllCurrentPartyMembers"`
- RED exit: `1`
- RED evidence: `TownObservers` 仍为 default `ImmutableArray`，枚举时抛出 `InvalidOperationException`；接受入队路径尚未冻结跨成员可见区域的 observer union。
- GREEN command: 同一命令；exit `0`，目标测试 `1/1` 通过。

### Round 3 — leave/disband post-mutation audience

- RED command: `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PartyDomainServiceTests.LeaveAndDisbandFreezeThePostMutationObserverAudience"`
- RED exit: `1`
- RED evidence: member leave 的 `TownObservers` 仍为 default `ImmutableArray` 并在枚举时抛出 `InvalidOperationException`；leave/disband 尚未冻结清理前可见受众。
- GREEN command: 同一命令；exit `0`，目标测试 `1/1` 通过。

### Completed result

- Changed: `PartyNotificationPlan` 增加可选 immutable `TownObservers`；`PartyDomainService` 在 mutation lock 内按每个当前成员/clear anchor 的 `VisibleTownArea` 合并、按 CharacterId 稳定排序、过滤非当前 exact session，并为 create/update/accept/leave/disband 返回冻结快照。
- Why: 非 leader 离队后自身已成为 observer，因此保留在 refresh audience；disband 的 former members 已由 clear audience 覆盖，因此从 observer 集排除。该分区与后继 protocol 顺序直接对应。
- Checks: 三个目标测试各自有真实 RED 与 GREEN；`Dfo.Server.Application.Tests.Parties` Release 过滤集 `26/26` 通过；`git diff --check` 通过。
- Remaining: protocol 层尚未消费 `TownObservers`；由 TASK-02 负责。
