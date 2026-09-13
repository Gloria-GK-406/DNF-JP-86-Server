# TASK-02 — observer delivery 与共享 stream

## Task Unit Contract

- Revision: 1
- Outcome: 保持成员序列，向 nonmember 精确投递 refresh/clear，并让同 generation state 在失败与乱序下收敛。
- Dependencies: TASK-01 accepted result。
- Owned files: Protocol party notification slice/module、对应 Protocol tests；本 Task 记录/证据。
- Required checks: TDD RED/GREEN；受众分区、离队、解散、settings early return、failure retry、旧 revision/generation。

## Result

### Round 1 — observer projection ordering

- RED command: `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PartyNotificationTests.TownObserversReceiveOneRosterRefreshAfterTheMemberProjection"`
- RED exit: `1`
- RED evidence: expected 6 publications, actual 5；成员五阶段完成后没有 observer roster refresh。
- GREEN command: 同一命令；exit `0`，目标测试 `1/1` 通过。

### Round 2 — failed publication retry

- RED command: `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PartyNotificationTests.AFailedObserverPublicationLeavesTheRevisionRetryable"`
- RED exit: `1`
- RED evidence: expected 2 observer attempts, actual 1；第一次 `Failed` 仍提前提交 revision，导致同 revision 被抑制。
- GREEN command: 同一命令；exit `0`，目标测试 `1/1` 通过。

### Round 3 — invalid observer-only plan

- RED command: `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PartyNotificationTests.Invalid_plan_shapes_are_rejected_before_any_publication"`
- RED exit: `1`
- RED evidence: observer-only plan 未抛异常，`Assert.Throws` 报告 no exception；该 plan 没有可绑定的 snapshot/clear version。
- GREEN command: 同一命令；exit `0`，目标测试 `1/1` 通过。

### Completed result

- Changed: party 的所有 member/acceptance/clear/realtime audience 改为 `TheseSessions`；observer 在 member 阶段后只复用 `PartyRosterChanged` 的 type-0/type-3；settings update、leave 与 disband 均按 plan 分区去重。generation stream 抽成注册表 singleton，mutation 与后继 read-repair 可共享。
- Why: `PartyRosterChanged` 自带 exact audience，使同一 handler 可安全投影 member 与 observer，又不复制 `0x0009` encoder；stream revision 只在所有已解析 recipient 均未 `Failed` 后提交，`Gone/Declined` 仍按 best-effort 语义收敛。
- Checks: 三轮真实 RED/GREEN；settings observer、clear-before-refresh、disband 去重、跨两个 delivery 实例串行与 observer handler exact-session 覆盖均通过；Protocol party + cleanup 过滤集 `85/85`，`git diff --check` 通过。
- Remaining: newcomer/mover 的 current-truth read-repair 尚未接入；由 TASK-03 负责。
