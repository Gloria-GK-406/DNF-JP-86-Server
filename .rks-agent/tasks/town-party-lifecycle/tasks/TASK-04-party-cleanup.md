# TASK-04 — 接入选人和断线清理

## Task Unit Contract

- Revision: 1
- Outcome and references: 让连接结束、返回选人和同账号替代共用精确会话组队清理与共享通知投影，覆盖 Spec RQ-009、RQ-010、RQ-011，Plan T-004。
- Owned boundary: `SelectedSessionCleanupService`、`SelectedSessionCleanupNotificationPlan`、既有 `CleanupNotificationDelivery`、相关 Application/Protocol 生命周期测试及必要组合注册；不得扩展到重连或持久化。
- Required checks: 定向测试证明旧代际不会移除新状态，普通成员掉线刷新幸存者、队长掉线解散全队，且原有 owner-before-town-peer 顺序不回归。
- Dependencies: TASK-02 的 Command 接入结果和 TASK-03 的共享通知投递器。

## Result — Round 1

- **Changed:** `SelectedSessionCleanupNotificationPlan` 增加独立 `PartyNotificationPlan`，保留 owner 直发、town peer fan-out 与 party clear/refresh 三种 audience，不把城镇邻居和队友混成一组。`SelectedSessionCleanupService` 注入唯一 `IPartyDomainService`，在释放 selected-session 目录前调用 `CleanupSessionExact`，把邀请、端点和成员资格的精确清理结果保存进同一个生命周期计划；即使后续某个清理动作失败，已经解析出的 party 投影仍进入 `SelectedSessionCleanupFailure.NotificationPlan`。既有 `CleanupNotificationDelivery` 注入 TASK-03 的共享投递器，严格保持 `owner -> town peers -> party` 顺序。
- **Why:** 断线、返回选人和同账号替代已经共用 `ISelectedSessionCleanupDomainService` 与 `ICleanupNotificationDelivery`；把队伍清理接在这两个唯一接缝上即可覆盖三条入口，同时继续由各入口现有 transition gate 保护。领域清理使用完整 `SelectedSessionIdentity`，迟到旧连接只能撤销仍属于旧代际的端点/邀请/成员，不能碰替代会话后来形成的新队伍。
- **Checks:** RED：新增生命周期测试最初因 cleanup plan 不含 `PartyNotifications` 且 cleanup service 不接收队伍领域服务而编译失败。GREEN：4 个新 Application 聚焦测试全部通过，覆盖普通成员清理、队长解散、旧代际迟到重清理和后续失败仍保留 party 投影；8 个 `CleanupNotificationDeliveryTests` 全部通过并固定 `owner -> town peers -> party`。设置 PVF 后完整 Application TRX 逐项为 1359 passed、0 failed、0 skipped，完整 Protocol TRX 逐项为 764 passed、0 failed、0 skipped；Release 解决方案构建 0 warning、0 error；`git diff --check` 通过。
- **Remaining:** 无。独立 quick review 已通过，进入 TASK-05 协议资料库同步。

## Independent Quick Review — Round 1

```yaml
kind: rks-review
verdict: passed
```

- **Reviewed execution:** `Result — Round 1`；Task Unit Contract Revision 1，无 amendment；限定 TASK-04 owned boundary，并对照 Spec RQ-009/RQ-010/RQ-011、AC-005/AC-006/AC-007 与 Plan BD-004/T-004。
- **Reviewed change identity:** `SelectCharacterLifecycleServices.cs`=`35CCBAAB06ADD821`、`SessionEntryModels.cs`=`5AC4152CC7BEB902`、`SupplementalSynchronizationMessages.cs`=`4EB68E567F1C1FF1`、`SelectedSessionPartyCleanupTests.cs`=`99B10C16C0131AE2`、`CleanupNotificationDeliveryTests.cs`=`3CB468FDC2B16A58`。
- **Disposition:** `SelectedSessionCleanupService` 在 `sessions.ReleaseExact(session)` 前调用 `IPartyDomainService.CleanupSessionExact(session)`，并把成功得到的独立 `PartyNotificationPlan` 带入统一 cleanup plan；后续 exact cleanup 失败时，`SelectedSessionCleanupFailure.NotificationPlan` 仍保留该 party 投影。普通成员清理产生离开者 clear 与幸存者同 revision 快照，队长清理产生全队 clear；迟到旧代际清理不能命中替代会话的端点、邀请或成员资格。
- **Lifecycle reuse:** 断线由 `OnlineUserConnectionObserver.CleanupSelectedAsync` 调用同一 `ISelectedSessionCleanupDomainService` 并在成功及 `SelectedSessionCleanupFailure` 路径交给 `ICleanupNotificationDelivery`；返回选人由 `CompleteReturnToSelectionCommandHandler` 调用同一 cleanup，`ReturnToSelectionHandler` 经 `CharacterSelectionDelivery` 交给同一 delivery；同账号/同连接替代由 `PrepareSelectedCharacterSessionCommandHandler` 清理 displaced/previous session 并把成功或 phase failure 中的计划统一交回 `SelectCharacterHandler` 投递。三条路径均由既有 selected-session transition gate 和精确 identity 检查保护。
- **Ordering and layering:** `CleanupNotificationDelivery` 逐段 await owner 直发、town peer notification、party delivery，固定 `owner -> town peers -> party`；party notification handler 再校验接收方当前精确 session，避免旧计划污染替代会话。`Dfo.Server.Application.csproj` 的生产引用仍仅有 Abstractions 与 Data，不引用 Infrastructure。
- **Evidence:** 独立复跑 `SelectedSessionPartyCleanupTests` 为 4 passed、0 failed、0 skipped；`CleanupNotificationDeliveryTests` 为 8 passed、0 failed、0 skipped；`SelectCharacterLifecycleTests` 与 `SessionGenerationOwnershipTests` 合计 35 passed、0 failed、0 skipped；`ReturnToSelectionTests` 为 4 passed、0 failed、0 skipped。`dotnet list ...Dfo.Server.Application.csproj reference` 仅列出 Abstractions/Data，`git diff --check` 通过。未发现 blocking finding。
