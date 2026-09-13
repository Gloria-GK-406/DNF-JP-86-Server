---
kind: rks-review
verdict: changes_required
scope: change
contract:
  path: ../TaskContract.md
  revision: 1
spec: ../spec.md
plan: ../plan.md
baseline: 2530f35bb0b5fb6d82603fbfaa9a492e37fc6375
---

# Change Review: town-visible 非成员 party 投影

## 结论

`changes_required`。领域侧 observer 冻结、成员/observer audience 分区、正常路径的
`0x0002 -> 0x0009` 顺序及现有 wire 复用方向基本成立，但共享 stream 的 revision 所有权、失败后的真实
恢复路径以及跨 generation 的 read-repair 顺序仍有三个阻断问题。它们直接落在 RQ-003..RQ-005、
AC-003..AC-005 与 SC-4；当前全绿测试没有覆盖这些时序。

## Blocking findings

### F-001 [P1] requester-only realtime 查询可以抢先提交 mutation revision，随后整批 mutation 被当作 stale 丢弃

`PartyTransitionExecution.RunAsync` 在 action 返回后释放按 account 的 transition lease
（`Server/src/Dfo.Server.Protocol/Game/Party/PartyCommandSlice.cs:305-335`），而例如
`SetPartyInfoHandler` 到 lease 外才调用 `PartyNotificationDelivery.DeliverAsync`
（同文件 `:593-608`）。因此 mutation 已把 party revision 推进到 N、但尚未投递时，另一名成员可以完成
`QueryPartyRealtimeCommand`，读到 revision N，并先进入共享 generation stream。

`DeliverRealtimeAsync` 成功发送一个 requester-only `0x0099` 后把 `LatestRevision` 推进到 N
（`PartyNotificationSlice.cs:535-553`）。随后真正的 mutation plan 进入 `DeliverAsync`，命中
`revision <= stream.LatestRevision`（`:470-494`），只处理 stale clear/acceptance side effects，跳过成员
settings/endpoints/roster 以及全部 town observer refresh。以 settings update 为例，查询者只收到 realtime，
其他成员收不到 type-1，非成员收不到 type-0；accept/leave 还会漏掉建立当前 roster 所需的成员阶段。
只要没有下一次 mutation，这就是永久漏帧。

现有并发测试只覆盖两个 `DeliverAsync` caller 的同-generation phase 串行
（`PartyNotificationTests.cs:523-547`），没有覆盖 `DeliverRealtimeAsync` 抢在 mutation delivery 前的情形。
最小修复边界是把“已提交完整 mutation state”的 watermark 与 requester-only realtime 查询分开：
realtime 可以受 gate/已提交 watermark 抑制，但不得推进会抑制完整 mutation 的 revision。补一个可控时序测试：
先让 mutation 产生 revision N，令同 revision realtime 先完成，再投递 mutation，并断言成员完整阶段和 observer
refresh 仍全部出现。

### F-002 [P1] 失败只被标记为“可重试”，生产链路并不会重试 party plan，也无法在 town retry 后补发 `0x0009`

mutation observer 发送失败时，`PartyNotificationDelivery` 的确不提交 `LatestRevision`，但所有 party 通知在
DI 中都是 `retryLimit: 0`，包括 `PartyRosterChanged`（`ProtocolModule.cs:114-119`）；各命令 handler 对
`DeliverAsync` 也只调用一次。所谓失败测试通过的原因是测试自己连续调用了两次同一个 plan
（`PartyNotificationTests.cs:180-193`），生产代码没有对应的第二次调用或 plan queue。因此一次 observer
type-0/type-3 发送失败后，如果没有后续 mutation，该 observer 永久缺状态，RQ-005/SC-4 未闭合。

entry/move 还有更直接的断链：`TownParticipantPublished` 配置了一个通用 retry
（`ProtocolModule.cs:129-130`），但 SelectCharacter/SetUserArea 只从首次 `PublishAsync` 返回值中取
`DeliveredSessions`，并仅为这些 recipient 调用 party read-repair
（`SelectCharacterHandler.cs:292-304`；`TownProjectionSlice.cs:408-420`）。如果首次 `0x0002` 为
`Failed`、通用 retry 随后成功，`NotificationBus.RetryAsync` 只重新 compose/send 原通知
（`NotificationBus.cs:158-177`），不会回到上述 orchestration；客户端最终已有 CNUser，却永远收不到其后的
party `0x0009`。反过来，`PartyTownProjectionDelivery` 对自身 `PartyRosterChanged` 的 delivery 结果完全不处理
（`PartyTownProjectionDelivery.cs:71-74`），且该通知 retryLimit 为 0，所以 `0x0009` 本身失败也不会恢复。

最小修复需要一个真实、可观测的重入路径，而不只是保留 revision：失败的 mutation plan 必须重新经过同一
generation gate；town publication 的成功 retry 必须继续触发对应 current-truth party repair；party repair
自身失败也必须安全重试且仍遵守 exact-session。补三类可控失败测试，并让测试只调用一次顶层生产入口：
mutation observer fail-then-success、town `0x0002` fail-then-success 后紧跟 `0x0009`、以及 read-repair
`0x0009` fail-then-success。

### F-003 [P2] read-repair 只按首次观察到的 generation 加锁，旧 generation 发送可越过新 generation mutation

`RefreshObservedPartiesAsync` 在加锁前读取 represented session 并按当时的
`PartyId + Generation` 分组（`PartyTownProjectionDelivery.cs:35-44`），随后只取得该旧 generation 的 gate
（`:46-50`）。它在 gate 内虽会重读一次（`:52-60`），但从重读到异步 `PublishAsync` 完成之间没有再次
验证；另一个线程可在这段时间把 exact member 清理/重选并建立新 generation，而新 mutation 使用另一个
gate，可以先向同一 observer 发新 roster。旧 `PublishAsync` 恢复后仍会把旧 roster 发在新 roster 后面。
这不满足 AC-004 的“entry 旧候选不会越过新 mutation”和 RQ-004 的 old-generation 不回滚约束。

当前唯一 gate 竞态测试只把同一 generation 从 revision 3 改为 4
（`PartyTownProjectionDeliveryTests.cs:71-103`）；没有切换 generation，也没有让旧 publish 与新 mutation
并发。TASK-03 结果中“另覆盖 replacement generation”的说法与测试内容不符，协议页所称“竞态收敛”也因而
过强。修复应给跨 generation 的状态变化一个共同的顺序所有者，或在真正发送时证明候选仍是 current；仅在
旧 gate 内多做一次 pre-send 查询不足以封住查询后的窗口。补一个阻塞旧 read-repair publish、切到新
generation 并投递新 mutation、再释放旧 publish 的判据，最终不得观察到新状态后的旧 roster。

## 已确认成立的部分

- `PartyNotificationPlan.TownObservers` 是 immutable exact-session 快照；领域测试覆盖 create/settings、
  accept 的多 anchor union、leave/disband、跨 channel/town/area、not-standing、not-shared 与 replacement
  排除。
- member 与 observer 使用 `TheseSessions` 分区，正常路径保持既有 member phase 顺序；leave clear 在
  observer refresh 前，disband observer clear 去重。
- select 与 move 的正常成功路径均在相应 town direct/publication 后触发 read-repair；同 party 去重，
  无 party 不产生消息；没有新增 `0x023C` 生产路径。
- 协议资料正确保留 `High` / 待 ID `0x11` 双客户端实机边界，但“测试证明失败重试和竞态收敛”须在以上
  finding 闭合前降级或改成与证据一致的表述。

## 独立检查

- 当前工作树与接受基线均为 `2530f35bb0b5fb6d82603fbfaa9a492e37fc6375`；评审覆盖全部 tracked diff、
  两个新增源码/测试文件和本 Spec 交付文档。
- 聚焦 Protocol 测试：32 passed / 0 failed。
- 新鲜串行 Release solution test（显式 `PVF_ARCHIVE_PATH`）：3123 passed / 2 skipped / 0 failed；跳过项为
  两个“缺失 archive 应失败”的互斥环境判据。
- `git diff --check` 通过；相对基线的 `Server/src` 新增行中 `0x023C/023C` 命中 0。

这些检查说明普通路径和既有回归稳定，但不能推翻上述未被当前判据执行到的并发/失败路径。

## Round 2

```yaml
kind: rks-review
verdict: approved
```

本轮只复审 Round 1 的 F-001/F-002/F-003 修复、由修复直接触及的行为以及交付文档一致性。
`approved` 覆盖当前 remediation target，并取代 Round 1 对这三项 finding 的阻断结论；它不代替后续
fresh completion verification。

### F-001 — resolved

`DeliverRealtimeAsync` 仍进入同一个 generation gate，并继续以已提交的 `LatestRevision` 抑制真正旧的
snapshot，但 requester-only `0x0099` 成功后不再写这个 full-mutation watermark
（`Server/src/Dfo.Server.Protocol/Game/Party/PartyNotificationSlice.cs:582-605`）。因此 realtime 查询即使先读到
mutation revision N 并抢先发送，也不能让随后 `DeliverAsync` 的完整 N revision 命中 stale 分支。

新增 `RequesterOnlyRealtimeDoesNotCommitTheFullMutationRevision` 先发送 revision 4 realtime，再以同一个
stream 投递 revision 4 mutation，并逐项断言 member realtime/endpoints/roster 与 observer roster 全部出现
（`Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyNotificationTests.cs:215-235`）。这正面复现并关闭了
F-001，而不是用不同 revision 绕开它。未发现该修复改变正常 member phase 顺序。

### F-002 — resolved

`PartyExactNotificationDelivery.PublishWithRetryAsync` 对 exact audience 去重，只把返回为 `Failed` 的 exact
recipient 留到第二次原位尝试，累计真实 `Delivered`，两次后仍失败则显式保留 `HasFailures`
（`PartyTownProjectionDelivery.cs:94-145`）。所有 party 因果发送以及 select/move 的
`TownParticipantPublished` 均经此路径；对应 notification registration 保持 `retryLimit: 0`，所以不存在
generic queue 脱离因果链单独补发 `0x0002` 的旧缺口
（`ProtocolModule.cs:114-132`）。SelectCharacter 与 SetUserArea 只对两次尝试中确实 Delivered 的 exact
recipients 继续 current-truth party repair（`SelectCharacterHandler.cs:292-302`；
`TownProjectionSlice.cs:408-418`）。read-repair 自身的 roster 也使用同一个 helper
（`PartyTownProjectionDelivery.cs:73-77`）。

判据与此语义一致：单次 mutation 顶层调用的 observer fail-then-success、连续两次失败后不提交 revision 且
同 revision 可再次进入、单次 read-repair 的 roster fail-then-success，以及 town `0x0002` 首次失败、第二次
成功后才继续 `0x0009` 均被直接观察。成功 recipient 不随邻居失败而重放；Gone/Declined 也没有被误当成
Failed。F-002 的三个断链点均已闭合。

### F-003 — resolved

共享 registry 新增按 `CharacterId` 唯一、升序获取的 participant gates，并在异常取得途中逆序释放
（`PartyNotificationSlice.cs:426-475`）。mutation 以 snapshot members 加 clear audience 为 participants，
在进入 generation gate 前取得这些 gate（`:500-521,861-866`）；read-repair 同样先锁所有 represented
sessions，再读取候选、进入 generation gate并完成发送
（`PartyTownProjectionDelivery.cs:35-83`）。任何会把被表示成员从旧 generation 迁入新 generation 的
mutation 都至少共享该成员的 participant gate，所以新 mutation delivery 不能越过仍在发送的旧 read-repair；
所有多 participant 获取顺序一致，未形成反向锁序。

`NewGenerationMutationCannotPassABlockedOldGenerationReadRepair` 将 generation 7 的首次 roster send 阻塞，
把当前状态切到 generation 8 后启动 mutation，确认新 delivery 尚未完成；释放旧 send 后最终顺序为
`7, 8, 8`（member roster 与 observer roster），直接覆盖 Round 1 缺失的跨-generation 时序
（`PartyTownProjectionDeliveryTests.cs:122-150`）。F-003 与 TASK-03 先前过强的 replacement-generation
表述现在都有实际判据支撑。

### 直接回归与文档一致性

- 独立重跑五个 remediation Protocol 判据：5 passed / 0 failed；另独立重跑 select 的 transient town
  causal retry：1 passed / 0 failed。
- 独立扩大过滤范围重跑 party/town/cleanup Protocol：110 passed / 0 failed；
  select/party/cleanup Application：67 passed / 0 failed。
- 独立 Release solution build：0 warnings / 0 errors；`git diff --check` 通过。
- `NOTI-0x0009.md` 已准确区分服务端 `High` 与尚待 A21 ID `0x11` 实机验证，并明确原位一次重试、关闭
  detached town retry、participant/generation gate 顺序与 realtime 不推进 full watermark；
  TASK-04 remediation 记录与当前代码和判据一致。Round 1 正文作为历史 finding 保留，没有被改写或删除。

在本轮限定范围内没有遗留 blocking finding。
