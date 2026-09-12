# TASK-03 — 投影并投递组队通知

## Task Unit Contract

- Revision: 1
- Outcome and references: 交付五种类型化 S2C 消息、编码器、Notification handler 与共享投递器，覆盖 Spec RQ-004、RQ-007、RQ-008、RQ-011，Plan T-003。
- Owned boundary: `Dfo.Server.Data` 中组队网络消息、`Dfo.Server.Protocol/Game/Party/` 的通知/编码/投递、协议常量和注册及相应 Protocol 测试；不得改变领域状态规则或生命周期调用点。
- Required checks: 字节级测试固定 `NOTI 0x0007/0008/0009/000B/0099` 的长度、字段、端口序与 slot；投递测试证明形成阶段按 `0099 -> 0008(仅邀请者) -> 000B -> 0009`、同代快照、清队与幸存者 audience 不串线。
- Dependencies: TASK-01 的不可变队伍快照与通知计划。

## Result — Round 1

- **Changed:** 新增五种类型化 Game NOTI 消息和编码器：`0x0007` 邀请、`0x0008` 接受、`0x0009` 完整/清除名册、`0x000B` 成员端点、`0x0099` 实时成员槽位；补齐 `ServerMessageType`、`GameProtocol`、出站 encoder 与五种 Notification handler 注册。新增 `PartyNotificationDelivery`，以同一个不可变 `PartySnapshot` 按 `0099 -> 0008(仅邀请者) -> 000B -> 0009` 发布形成投影，并把离队清除 audience 与幸存者刷新 audience 分离。四个 C2S handler 仅在 Command 成功后、且双方 transition gate 已释放后投递 Command 产出的通知计划。`PartyNotificationPlan` 增加清队所需的 `ClearPartyId` 投影数据，未改变任何领域状态规则。
- **Why:** 形成阶段必须让双方从同一代快照得到一致 uid/slot/endpoint，不能由每个接收方再次读取可变队伍状态；Notification handler 在最终发送前重新验证 snapshot 中的精确 session identity，旧连接或已换代角色不会收到迟到投影。端点编码保留上报 IPv4 字节，UDP port 按协议网络序输出；缺失端点保持固定记录长度并以零地址/端口降级，不改变成员关系。
- **Checks:** RED：新增通知测试最初因五种消息/encoder/投递器不存在而无法编译；GREEN：组队聚焦测试 43 passed、0 failed、0 skipped。字节测试固定 `0x0007=13B`、`0x0008=7B`、两成员 `0x0099=11B`、`0x000B=45B`、完整 `0x0009=60B` 与清除 `0x0009=5B`；另有精确 session 换代拒投影、阶段顺序/audience、以及发布时重入账号 gate 的有界测试，证明通知不在 Command transition gate 内发送。设置 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-debug\Script.pvf` 后完整 Protocol 测试为 761 passed、0 failed、0 skipped，TRX 逐项统计 `Passed=761`；`git diff --check` 通过。
- **Remaining:** 等待独立 quick review；通过后进入 TASK-04，把断线、返回选人和同账号替代的精确清理计划接入同一投递器。

## Result — Round 2

- **Changed:** 闭合 IQR-01-F1：通知计划为清队投影携带原队伍 generation/revision；队长解散先推进 revision。`PartyNotificationDelivery` 以 generation 建立单例内串行流，并在每个流内记录最高 revision：先到的旧 revision 会完整结束后才允许新 revision 发送；先到的新 revision 会让迟到旧 revision 无副作用返回。因此同一队伍的多阶段投影既不跨调用交错，也不会由迟到旧名册回退客户端。终止 generation 的最终 revision 作为 tombstone 保留，防止解散后的迟到调用复活名册。
- **Checks:** 新增两个确定性并发回归：其一把旧 revision 阻塞在第一阶段，证明新 revision 在其完整三阶段结束前不能开始，观察顺序为 `3,3,3,4,4,4`；其二先完成 revision 4 再投递 revision 3，证明旧调用零发布。组队 Protocol 聚焦测试 45 passed、0 failed、0 skipped；Application 组队聚焦测试 15 passed、0 failed、0 skipped；设置 PVF 后完整 Protocol 测试与 TRX 逐项统计为 763 passed、0 failed、0 skipped；`git diff --check` 通过。
- **Remaining:** 等待 Round 2 独立复核；通过后进入 TASK-04。

## Independent Quick Review — Round 1

```yaml
kind: rks-review
verdict: changes_required
```

- **Reviewed execution:** `Result — Round 1`；Task Unit Contract Revision 1，无 amendment；基线 `355aaf5cb6714044b4781185ec3118007bc97bec`；限定 RQ-004/RQ-007/RQ-008/RQ-011、AC-004 与 Plan T-003。
- **Reviewed change identity:** `PartyNotificationSlice.cs`=`C0E6E4508E79061A`、`PartyNotificationTests.cs`=`59E6F55CF796B98B`、`PartyCommandSlice.cs`=`37D058BE7CAF0FE2`、`PartyCommandHandlerTests.cs`=`EB1F6288737C0D59`、`PartyModels.cs`=`CF2F6C41E75D6572`、`NetworkMessages.cs`=`68F847049E8B8CD0`、`GameProtocol.cs`=`14E12AB83E43E8A7`、`ProtocolModule.cs`=`0B95E2FECE98EC68`。
- **Confirmed evidence:** 五种 NOTI 的字段宽度、总长、IPv4 原始 octet、UDP 网络序、slot 与 `partyId` 编码与当前证据一致；单次投递确实使用同一 `PartySnapshot` 按 `0099 -> 0008(仅 inviter) -> 000B -> 0009` 发布，清除与幸存者 audience 分离，各 Notification handler 在组包前校验精确 session；Command 结果返回后 `PartyTransitionExecution` 的 `finally` 先释放全部 gate，再调用投递器；五种 handler/encoder 均有生产注册。独立复跑 `FullyQualifiedName~Party` 为 43 passed、0 failed、0 skipped，`git diff --check` 通过。

### Blocking Findings

- **IQR-01-F1 — 同一队伍不同 revision 的投影流可在 gate 外并发交错，较旧名册可在较新名册之后到达。** 四个 C2S handler 都在 `PartyTransitionExecution.RunAsync` 返回、即双账号 gate 释放后直接调用 singleton `PartyNotificationDelivery`（`PartyCommandSlice.cs:232-244,288-301,345-358,385-396`）；投递器本身没有按 `PartyId+Generation+Revision` 排序、丢弃陈旧投影或串行化机制，只保证单次 `DeliverAsync` 内的四个 `PublishAsync` 顺序（`PartyNotificationSlice.cs:340-388`）。因此，A 先后邀请 B/C 后，B 接受产生 rev2，C 在 rev2 释放 A/B gate 后即可接受并产生 rev3；若 rev2 停在 `0x0099` 后、rev3 完成全流程后 rev2 再恢复，A/B 最后会收到 rev2 的 `0x000B/0x0009`，把刚得到的三人端点和名册回退为两人旧快照。这破坏 RQ-007/AC-004 的同代投影与阶段顺序，也会直接造成客户端成员状态与领域状态不一致。当前 `Formation_is_published_in_cross_recipient_phases` 只调用一次投递，gate 重入测试也只证明发送位于 gate 外，没有保护跨调用的 revision 顺序。请在不重入 transition gate 的前提下，为同一 party generation 提供有界、代际感知的投影排序/陈旧丢弃边界，并增加两个可控并发 `DeliverAsync` 的测试：暂停旧 revision 的中间阶段，让新 revision 尝试完成，最终断言每个接收方的全局阶段序列不回退且旧名册不会在新名册后发送。

## Independent Quick Review — Round 2

```yaml
kind: rks-review
verdict: passed
```

- **Reviewed execution:** `Result — Round 2`；Task Unit Contract Revision 1，无 amendment；基线 `355aaf5cb6714044b4781185ec3118007bc97bec`；仅复核 IQR-01-F1 与直接回归。
- **Reviewed change identity:** `PartyNotificationSlice.cs`=`F5C4CAE10F4D8236`、`PartyNotificationTests.cs`=`B50B1A128C918B22`、`PartyModels.cs`=`630C67F044B21560`、`PartyDomainService.cs`=`AE06033BE129BFFE`。
- **Code disposition:** IQR-01-F1 已闭合。`PartyNotificationPlan` 现为 clear-only 与 clear+survivor 投影保留原队伍 generation/revision；普通成员离队的 clear 版本与 survivor snapshot 严格一致，队长解散在移除队伍前先推进 revision，因而终止 clear 会覆盖所有早期投影。`PartyNotificationDelivery` 在 transition gates 外按全局唯一 generation 使用单例串行流，并在租约内以 revision high-water 丢弃迟到旧计划：旧流先进入时完整发完后才放行新流，新流先进入时旧流零发布；解散 generation 的最终 high-water 保留为 tombstone，不会被同代迟到名册复活。
- **Regression disposition:** `Revisions_of_one_party_generation_are_delivered_as_contiguous_phases` 在旧 revision 首阶段中人为阻塞，并断言新 revision 未完成、全局序列为 `3,3,3,4,4,4`；Round 1 原实现会在阻塞期间立即产生新 revision 的三次发布，导致 `Assert.Single` 和有界未完成断言确定失败。`An_older_revision_arriving_after_a_newer_one_is_suppressed` 先完成 rev4 再调用 rev3，期望仅有 3 个 rev4 发布；原实现会发布 6 次，同样确定失败。
- **Evidence:** 独立复跑 `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter FullyQualifiedName~Party` 为 45 passed、0 failed、0 skipped；Application 组队聚焦测试为 15 passed、0 failed、0 skipped；`git diff --check` 通过。未发现未解决阻断项。
