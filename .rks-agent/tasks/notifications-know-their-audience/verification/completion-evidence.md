---
kind: rks-verification
decision: pass
---

# Verification — 通知知道自己要告诉谁

## 被评估的确切主张

> 「通知知道自己要告诉谁」这个成果已完成：契约 `TaskContract.md`（revision 2）的成功条件
> SC-1..SC-12 全部达成，且服务器对客户端的行为满足 revision 2 的口径。

## 验证者身份与边界

独立验证者，fresh context，未参与实现、未参与评审。**未做任何提交，未改动任何实现文件、
判据文件或任务控制状态。** 编译探针建在 scratchpad 的临时工程里（`Probe.csproj` +
`ProjectReference` 指向 `Dfo.Server.Application.csproj`），验完已删除。

验证前后 `git status --porcelain` 相同：**15 M + 6 ??**（含 `.rks-agent/` 产物目录），
分支 `notifications-know-their-audience`，基线 `b05d47e4`，未提交。

评审结论 `approved`、执行方自检、各 Task 文件里的快审 verdict **仅作线索**；下面每一条
都用本轮现跑的命令或本轮亲自读到的代码重新判定。

## 环境与全局判据

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`

### 清洁重建

```
dotnet build Server/86JP.Server.slnx --no-incremental -v:n
```

`BUILD_EXIT=0`；日志末尾「已成功生成。 **0 个警告 0 个错误**」；
全日志 `grep -aoE "(warning|error) [A-Z]+[0-9]+"` 命中 **0**。

### 全量判据，跑了两趟，两趟同值

```
dotnet test Server/86JP.Server.slnx --nologo --logger "console;verbosity=detailed"
```

| | 退出码 | 通过 | 跳过 | 失败 |
|---|---|---|---|---|
| 第 1 趟 | 0 | 2126 | 2 | 0 |
| 第 2 趟 | 0 | 2126 | 2 | 0 |

计数用 `grep -o`（并发写会粘行，不能按行数）：`已通过 ` 2126、`已跳过 ` 2、`失败 ` 0。
七个程序集分项：Networking 52 / Pvf 60 / Protocol 517 / Persistence 256 / Host 6 /
Infrastructure 223(+2 跳过) / Application 1012 = 2126 + 2。
整份输出 `warning|警告` 计数 **0**。

与交付声称的 2126 / 2 / 0 逐值相同（基线 2069 / 2 / 0，净 +57）。

两条 skip 是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`，
跳过原因文本 **"An archive is reachable from this working tree."** —— 档案确已加载，
与 Assumptions 一致。日志里仅有的 2 处 `错误消息:` 就是这两条跳过原因，不是失败。

### 关于「判据偶发失败」那条新规则的现场检验

**没有取到现场检验。** 两趟全量判据 0 失败，所以 `AGENTS.md` 新写的三条栈根表、
「用例名不是判别依据」、「取不到身份一律是取证缺口」这一整套规则，在本轮**仍然是零次现场检验**。
如实记下：它的有效性本轮既未被证实也未被证伪。

## 逐条成功条件

### SC-1 — 类型由实参决定；不匹配不能通过编译或启动校验 — **达成**

**我自己写的编译探针**（scratchpad 临时工程，已删），不采信记录里的 CS0311：

| 用例 | 结果 |
|---|---|
| 正对照：`bus.PublishAsync(a, new Foo(1), ct)` + `AddNotificationHandler<Foo, FooHandler>(0)` | **已成功生成，0 警告 0 错误** |
| A：`PublishAsync<INotification<Foo>>(a, new Foo(1), ct)` | **error CS0311**（`INotification<Foo>` → `INotification<INotification<Foo>>` 无隐式引用转换） |
| B：`AddNotificationHandler<Foo, BarHandler>(0)` | **error CS0311**（`BarHandler` → `INotificationHandler<Foo>` 无隐式引用转换） |
| C：`PublishAsync<Foo>(a, new Bar(1), ct)` | **error CS1503**（参数 2 无法从 `Bar` 转换为 `Foo`） |
| D：`PublishAsync(a, new NotANotification(1), ct)` | **error CS0311** |

签名：`INotificationBus.PublishAsync<TNotification>(...) where TNotification : class, INotification<TNotification>`
（`INotificationBus.cs:85-89`），自指约束 `INotification<TSelf> where TSelf : INotification<TSelf>`
（`INotificationHandling.cs:27-28`）。

启动校验一侧：`NotificationHandlerRegistry` 构造时对已编目程序集做闭包检查并抛出
（`NotificationHandlerRegistry.cs:57-70`）；判据
`A_catalogued_notification_with_no_handler_is_refused`、
`A_notification_registered_twice_is_refused`、
`The_registry_is_asked_for_before_the_first_hosted_service_starts`、
`The_registry_is_asked_for_before_the_listeners_are_registered` 本轮全部通过。

**另核了两处「文档自陈的漏洞」是否属实**（关系到 SC-12 的「无一句比代码强」）：

- 非密封基类可被加宽发布 —— 探针 E：`PublishAsync<BaseNote>(a, new DerivedNote(1,2), ct)`
  **编译通过**。`INotificationHandling.cs:14-20` 说它挡不住，属实。
- 外部 record 借复制构造函数从 `NotificationAudience` 派生 —— 探针 F 用
  `INotificationServices.cs:26-29` 里那段一模一样的代码，**编译通过**。文档说它编译得出来，属实。

两处文档都没有把保证说大。

### SC-2 — 人群是值、以角色表达；解析由服务完成；调用方不出现连接查找 — **达成**

`NotificationAudience` 是私有构造函数的 abstract record，唯一形态
`TheseCharacters(IReadOnlyList<CharacterId>)`（`INotificationServices.cs:41-56`），**全程没有
`NetworkConnectionId`**。解析在 `NotificationAudienceResolver.Resolve/Reachable`
（`NotificationAudienceResolver.cs:21-45`），未教过的形态在最后一个 arm 被**指名**抛出（`:29-30`）。

**全仓 `FindByCharacter` 生产调用逐处判定**（`grep -rn "FindByCharacter" Server/src --include=*.cs`，
7 行，其中 1 行是接口声明、1 行是实现）：

| 位置 | 用途 | 判定 |
|---|---|---|
| `ISessionPresence.cs:85` | 接口声明 | 不适用 |
| `SelectedSessionDomainService.cs:48` | 实现 | 不适用 |
| `NotificationAudienceResolver.cs:40` | 人群解析 | 合规（本服务内部，这正是它该在的地方） |
| `NotificationBus.cs:212` | 投递前复问在场 | 合规（同上） |
| `SelectCharacterLifecycleServices.cs:111` | 清理后判「有没有人已经重新选入」，**结果只与 null 比较，不取会话** | 合规 |
| `SelectCharacterLifecycleServices.cs:154` | `PlanPvp`，`owner` 只作 `runtimes.GetRequired(owner)` 的键 | **合规**，我逐行核过：未读 `ConnectionId`，且它建的是回给请求方自己的大厅快照，不是扇出 |
| `ExperienceBonusDomainService.cs:31` | `session` 只作 `runtimeSessions.TryGet(session, ...)` 的键 | **合规**，同上，未读 `ConnectionId` |

两个 `PublishAsync` 调用点（`SelectCharacterHandler.cs:291`、
`SupplementalSynchronizationMessages.cs:255`）**均不做连接查找**：前者传
`pvpPublication.Publication.Audience`（一串 `CharacterId`），后者传 `plan.PeerAudience`。
`CleanupNotificationDelivery.DeliverAsync` 里出现的 `plan.OwnerConnectionId` 是「这次操作主体自己的
那个连接」，属 `AGENTS.md:439-444` 明写的豁免，不是扇出寻址。

判据：`NotificationAudienceTests` 6 条全绿，含
`The_only_way_to_make_an_audience_from_nothing_is_private_to_the_hierarchy`、
`The_shape_that_should_not_exist_is_named_rather_than_resolving_to_nobody`。

### SC-3 — 按收件人取内容；可对个别收件人弃权 — **达成**

`INotificationHandler<T>.ComposeAsync(NotificationRecipient recipient, T notification, ct)`
（`INotificationHandling.cs:44-51`）。总线把 `null` 或空列表判为 `Declined`
（`NotificationBus.cs:181-182`）。

判据：`Each_recipient_is_told_what_the_handler_made_for_them`（我读过断言：两人两帧，
逐 `(ConnectionId, Label)` 比对）、
`A_recipient_the_handler_declines_hears_nothing_and_is_not_a_failure`
（断言弃权者不在 `Sent` 里、归 `Declined`、且**不在 `Failed` 里**）。两条本轮通过。

### SC-4 — 处理器不写任何状态 — **达成**

`NotificationHandlerArchitectureTests`（`Exempt` 字典**为空**，`IsPermitted = Exempt ∪ IsDomainService`，
后者 = 接口 ∧ 程序集 == `Dfo.Server.Abstractions` ∧ 名字以 `DomainService` 结尾，
`:186-199`）。反向判据齐备：`TheSweepFindsTheNotificationHandlers` 防空扫描；
`ATypeOutsideTheVocabularyIsRefused` 拒 store / table / runtime directory / command bus / string；
`A_type_outside_the_seam_layer_is_refused_however_it_is_named` 拒本项目内同名接口。
本轮 **14 条全绿**。

本成果新增的两个生产处理器 `PvpParticipantPublishedHandler`、`SessionCleanupAnnouncedHandler`
我逐字读过：**都没有构造函数参数**，注入面为空，方法体只做 record → message 的纯映射。

顺带核实了控制器事后改的那处注释：`NotificationHandlerArchitectureTests.cs:73-76` 现写
"The first four below are the ways a handler could reach a write … The fifth is not a way to reach
anything"，其下确为 5 个 `[InlineData]`（第五个 `typeof(string)`）。**断言与 `[InlineData]` 一个未动**
（`git diff` 该文件为新增文件，无既有断言可削弱）。

### SC-5 — 单个收件人失败或超时不影响其余 — **达成**

`NotificationBus.AttemptAsync`（`:160-202`）逐收件人 try/catch，只放行调用方自己的取消
（`catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) throw;`）。
每收件人一个 `CreateLinkedTokenSource` + `CancelAfter`（`:166-167`）。

判据本轮通过且断言我读过：`A_recipient_whose_delivery_fails_does_not_stop_the_others`
（中间那位失败，`Delivered = [First, Third]`、`Sent` 只有这两个连接）、
`A_handler_that_throws_for_one_recipient_fails_only_that_one`、
`A_presence_lookup_that_throws_costs_only_that_recipient`、
`A_recipient_who_runs_out_of_time_is_a_failure`、
`The_callers_own_cancellation_is_not_reported_as_a_failed_recipient`。

### SC-6 — 「已不在」与「投递失败」分开；前者不重试 — **达成**

`IsStillHere`（`NotificationBus.cs:211-213`）两半都问：有没有会话、会话是不是**同一个连接**；
在**合成之前**问（`:176`），所以走人的人不花处理器调用。`Gone` 不入队（只有 `Failed` 触发
`Requeue`，`:72-73`）。

判据：`Someone_who_left_is_told_apart_from_someone_whose_delivery_failed`（同一份摘要里三个人
分别落 `Delivered` / `Gone` / `Failed`）、
`Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed`（断言 `Gone` 且
`Failed` 为空且新连接**没有收到帧**）、
`Somebody_who_left_before_the_retry_is_let_go`（`Waiting == 0`，只尝试过一次）。本轮全绿。

### SC-7 — 重试重新解析、重新取内容、达上限丢弃 — **达成**

`NotificationRetry` 只记 `(Registration, Notification, CharacterId, Attempts)`
（`NotificationRetryQueue.cs:14-18`）——**不存连接、不存帧**。
`RetryAsync`（`:132-150`）重新走 `audiences.Resolve(new TheseCharacters([entry.CharacterId]))`
再重新 `ComposeAsync`。上限来自注册：`Requeue` 判 `used < registration.RetryLimit`（`:113`）。

判据（断言我逐条读过）：

- `A_retry_asks_the_handler_again_rather_than_resending_what_it_had`：首投失败后**把内容改成
  "second answer"**，排空后 `Sent` 里只有 `["second answer"]`。这条正是「重试不缓存」的牙。
- `A_retry_goes_to_the_connection_they_are_on_now`：首投失败 → `Reconnect` → 排空 → 送到**新连接**。
- `A_recipient_who_keeps_failing_is_dropped_at_the_declared_limit`：`retryLimit: 2` 下
  1 + 2 = 3 次尝试，队列归零，第四趟不再尝试。
- `A_notification_that_declares_no_retries_queues_nobody`、
  `A_pass_works_through_what_was_waiting_and_no_further`、
  `A_pass_called_off_leaves_what_it_had_not_reached`。

处理器只读那条（SC-4）与这条的组合意味着重试也不会写。

### SC-8 — 交回一份摘要，能回答覆盖了谁、谁失败了 — **达成**

`NotificationDelivery(IReadOnlyList<NotificationAttempt>)` + `With(outcome)`
（`INotificationBus.cs:53-70`），四归类 `Delivered / Declined / Gone / Failed`。
判据 `The_summary_accounts_for_every_recipient`：人群点了三个人、只有两个在线，
摘要的 `Attempts` 恰是这两个人。「谁失败了」由上面数条的 `delivery.With(NotificationOutcome.Failed)`
断言覆盖。

### SC-9 — 至少一处扇出改经此服务，其领域服务不再做连接查找 — **达成（实际两处）**

**第一处 · PvP 大厅公示。** `PlanPvpPublication` 从

```
.Select(value => value.Session ?? sessions.FindByCharacter(value.CharacterId))
.Where(...).Select(value => new PvpParticipantPublicationTarget(value!.ConnectionId, newcomer))
```

变成 `new(pvp.ReadyOn(...).Select(value => value.CharacterId), CreatePvpParticipant(result))`
——**该方法内连接查找计数为 0**。

**第二处 · 会话清理的「尽力送旁人」段。** `SelectedSessionCleanupService.CleanupAsync` 里的
`var peerSession = peer.Session ?? sessions.FindByCharacter(peer.CharacterId);` **已删除**；
现在只 `audience.Add(peer.CharacterId)`（`:53-57`），旁人的 notice 只算一份
（`:63-70`）。计划类型由 `BestEffortTargets` 改为 `PeerAudience` + `PeerNotices`。

旧类型 `PvpParticipantPublicationTarget` / `SelectedSessionCleanupNotificationTarget` /
`CleanupSynchronizationDelivery` 全仓已无残留引用。

### SC-10 — 帧逐值不变；收件人集合与投递语义的每一处差异都已列出、每条指到代码窗口 — **达成（revision 2 口径），带两处精度瑕疵**

**帧逐值不变 —— 我自己比对基线，成立。**

- PvP：基线 `SelectCharacterHandler.cs:303-304` 是
  `new PvpParticipantPublicationMessage(target.Participant)`，`target.Participant` 即
  `PlanPvpPublication` 里的 `newcomer = CreatePvpParticipant(result)`；现在
  `PvpParticipantPublishedHandler` 返回 `new PvpParticipantPublicationMessage(notification.Newcomer)`，
  `Newcomer` 同为 `CreatePvpParticipant(result)`。**同一表达式。**
- 清理：基线 `CleanupSynchronizationDelivery.ToMessage` 的 switch 两个 arm，与现在的
  `CleanupNoticeMessages.ToMessage` **逐字符相同**；本人半边 `RequiredOwnerNotices.Select(ToMessage)`
  未变；旁人半边由 `target.OrderedNotices.Select(ToMessage)` 变为
  `notification.Notices.Select(ToMessage)`，内容与顺序相同（旧代码在每个 peer 上重建同一组条目）。
- 判据 `The_peers_notices_become_the_frames_they_always_did` 钉住
  `["ExpertStoreClosedMessage", "TownDepartureMessage"]` **两条、按序**——比 `OnlineUserServiceTests`
  里消失的那条 `Assert.Equal("TownDepartureMessage", ...)` **更强**（旧断言只钉一条）。我核实属实。

**既有判据的期望值未被改写 —— 成立。** `git diff -- Server/tests` 只有 4 个文件，我逐 hunk 读过：
`SessionGenerationOwnershipTests` 的期望由连接改为角色、由 `Assert.All(每个 target 一份)` 改为
「一份 `PeerNotices`」，**同强度**（新模型里「每人一份」是结构性的）；
`OnlineUserServiceTests` 里失去的帧名断言已如上更强地补回；
`SelectCharacterLifecycleTests` 与 csproj 的改动是签名跟随与一个 `Microsoft.Extensions.Hosting` 引用。
**没有任何断言被削弱或删除。**

**差异清单，每条我都回到它自称的代码位置核过：**

TASK-05 第四版，八条：

| 条目 | 自称的窗口 | 我核到的 |
|---|---|---|
| D-1 同角色重新选入 | `SelectCharacterPhaseCommands.cs:91`（`sessions.Select`）之后、`SelectCharacterLifecycleServices.cs:87`（`ClearReadyExact`）之前，隔着 `:81` 的 await | `:91` **精确命中**；后两个行号**已漂移**（见下方 F-1），构造现在在 `:100` / `:94`，窗口结构成立 |
| D-2 换成另一个角色 | `sessions` 以 `AccountId` 为键，`FindByCharacter(旧角色)` 答 null | `SelectedSessionDomainService` 的键语义成立；`NotificationAudienceResolver.cs:40` 处该收件人整个不产生 attempt，成立 |
| D-5 解析后轮到前换连接 | `NotificationBus.cs:176/211` | **精确命中**（`:176` `if (!IsStillHere(recipient)) return Gone;`，`:211-213` 两半判断） |
| D-4 重试 `retryLimit: 1` | `ProtocolModule` 注册 + `NotificationBus.cs:72-73` | **精确命中**（`ProtocolModule.cs:169-170` `AddNotificationHandler<PvpParticipantPublished, …>(retryLimit: 1)`；`:72-73` `if (outcome == Failed) Requeue(…, used: 0)`）；判据 `The_pvp_publication_declares_the_allowance_it_meant_to` 钉住这个 1 |
| D-7 每收件人 2 秒期限 | 新 `NotificationBus.cs:166-167`（+`:186`）；旧 `TouchSocketNetworkMessageSender.DeliverExplicitAsync:88` 无期限 | 新侧**精确命中**（`:26-27` `Default = (2s, 1s)`）；旧侧行号**off by 3**（见 F-2），方法与语义命中 |
| D-6 发布前三步不在逐收件人守卫内 | `NotificationBus.PublishAsync` 开头 | **命中**：`:58` `GetRegistration`、`:59` `Resolve`、`:65-66` composer 的 DI 解析，全在 `:68` 的 `foreach` 之前 |
| 空 publish | 旧判 `Targets.Count > 0`、新判 `Audience.Count > 0` | **命中**：基线 `SelectCharacterHandler.cs:296` 正是 `if (pvpPublication.Targets.Count > 0)`；现为 `:289` `if (pvpPublication.Publication.Audience.Count > 0)` |
| 摘要被丢弃 | `SelectCharacterHandler` 不读 `PublishAsync` 的返回 | **命中**：`:291-294` 的 `await notifications.PublishAsync(...)` 返回值未被接收 |

TASK-06，C-D1/C-D2/C-D5/C-D7/C-D8：

| 条目 | 自称的窗口 | 我核到的 |
|---|---|---|
| C-D1 | 枚举在 `SelectCharacterLifecycleServices.cs:53-57`；解析在 `NotificationAudienceResolver.cs:40` | **两处均精确命中** |
| C-D2 | `FindByCharacter(旧角色)` 答 null，该旁人整个不出现 | 命中（同 D-2 的机制，解析器 `:38-41` 的 `if … is { } session` 决定） |
| C-D5 | `NotificationBus.cs:212` | **精确命中** |
| C-D7 | `NotificationBus.cs:166-167`；旧 `TouchSocketNetworkMessageSender:88` | 新侧**精确命中**；旧侧同 F-2 的 off by 3 |
| C-D8 | 旁人半边映射搬进 `SessionCleanupAnnouncedHandler`，跑在 `NotificationBus.cs:196-201` 的裸 catch 下；六个投递点都不读返回 | **逐环命中**：`:196-201` 正是那个 catch；`retryLimit: 0` 在 `ProtocolModule.cs:86-90`；六个投递点见下 |
| 窗口更宽的一点（投递点 3/4） | 跨过 `SelectCharacterPhaseCommands.cs:106-112` 的 `PrepareAsync` / `PlaceAtSpawnAsync` / `PrepareLeaseAsync` | **精确命中，三行三步** |

**六个投递点与「四个在 catch」我逐行核过，行号全部精确：**
`OnlineUserConnectionObserver.cs:95`（try 内）、`:102`（`catch (SelectedSessionCleanupFailure)` 内）；
`SelectCharacterHandler.cs:104`（try 内）、`:136`（`catch (SelectCharacterPhaseFailure)` 内）、
`:156`（`catch (Exception)` 内，**且在 `CompensateSelectedCharacterSessionCommand` 之后**）、
`:162`（嵌套 `catch (SelectedSessionCleanupFailure)` 内）。**四个在 catch，属实。**

**我自己走了一遍两条路径找第九条：** 逐分叉点比
`TouchSocketNetworkMessageSender.DeliverExplicitAsync`（`:69-105`）与
`NotificationBus.PublishAsync` / `AttemptAsync` / `RetryAsync`。**没有找出清单外的差异。**
另注意到旧侧 `:84-85` 的 `if (target.ConnectionId == requiredDirectConnectionId) continue;`
在新路径无对应物——`ReadyOn` / `InArea` 都以 `excluding` 排掉本人，该分支今天不可达，
不构成可观察差异（TASK-05 已把它列入「没有变的」，TASK-06 未列，可接受）。

**这句话的强度我要说准**：它只等于「又一个独立上下文读了一遍没找出第九条」，
**不等于清单已被证明穷尽**。`spec.md` 的 C-3 已明写「这条成功条件今天真正靠的是独立评审，
不是任何自动化判据」——**我确认这句自陈准确且必要**。

### SC-11 — 命令处理器注入面判据仍通过，且不靠放宽或扩充 — **达成**

`Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs`：

```
git diff --stat -- <该文件>            → 空
git status --porcelain -- <该文件>     → 空
git rev-parse b05d47e4:<该文件>        → 411de89a9f2c2a574015448aea68ec26a87521dd
git hash-object <该文件>               → 411de89a9f2c2a574015448aea68ec26a87521dd
```

**对基线逐字节相同**，判定谓词与 `Exempt` 清单无从被放宽或扩充。本轮该类 **12 条判据全绿**。

新增的 `NotificationHandlerArchitectureTests` 是**另一张更严的表**（不含 `DomainStore`、
`Exempt` 为空），不是对旧表的扩充。

### SC-12 — 仓库约定记下 `CMD ⇒ Notify`，并如实写下没做的部分 — **达成**

`AGENTS.md` 新增两节（`+98 / −1`，删除行确为 **1**，文件 627 行，`od -c` 确认 **LF、无 CR、无 BOM**）。
**里面每一个数字我都核到代码：**

| 记载 | 我实测 |
|---|---|
| 37 处 `SendExplicitAsync` | `grep -rn "SendExplicitAsync" Server/src --include=*.cs` = **42 行**，减去三个定义文件（`GameMessageSemantics.cs` 3 行、`TouchSocketNetworkMessageSender.cs` 1 行、`INetworkServices.cs` 1 行）= **37** ✓ |
| 7 处 `SendNotiAsync` | 同口径 **9 − 2（`GameMessageSemantics.cs`）= 7** ✓ |
| 六个投递点、**其中四个在 catch**、`:156` 在补偿命令之后（`OnlineUserConnectionObserver.cs:102`、`SelectCharacterHandler.cs:136/:156/:162`） | **逐行核过，六个点与四个 catch 的行号全部精确** ✓ |
| 「在它之前有**两个**领域服务知道了 socket」 | 恰为 `PlanPvpPublication` 与 `CleanupAsync` ✓ |
| `SelectCharacterLifecycleServices.cs:120` 带离开者自己的 `ConnectionId` | `:119-120` 正是 `new SelectedSessionCleanupNotificationPlan(session.ConnectionId, required, audience, peerNotices)` ✓ |
| 两处合规的 `FindByCharacter`：`SelectCharacterLifecycleServices.cs:154`、`ExperienceBonusDomainService.cs:31`，只当运行态目录的键、从不读 `ConnectionId` | **两处行号精确，两处代码逐行核过属实** ✓（这是控制器事后按 N-2 改的那处，改对了） |
| `CS0311` | 探针 A/B/D 实测 ✓ |
| `CS8878` / 外部 record 派生得出来 | 探针 F 实测**编译通过** ✓ |
| `NotificationHandlerArchitectureTests` 14 条 | 判据日志实测 **14** ✓ |
| 栈根表三条；第一条见于三个用例名；取证缺口两条 | 表 3 行 ✓、首行 3 个用例名 ✓、缺口 2 条 ✓ |
| 第三个候选 `SessionAdjacentEntryDomainService` 里的 `presence.InArea` 不是扇出 | `SessionAdjacentEntryDomainService.cs:27` 确为「给入场者本人算周围有谁」，产出一份给一个人，且不查连接 ✓ |
| `retryLimit` 0 表示只发一次；到两次会出问题的应声明 0 | `ProtocolModule.cs` 清理 `retryLimit: 0`、PvP `retryLimit: 1`，各带自己的理由 ✓ |
| 「漏注册会在开始接受连接之前起不来」 | `NotificationDispatchingExtensions.cs:23-31` + 两条判据本轮通过 ✓ |

「没做的部分」四项（周期触发、发布订阅、其余回复路径、三种未实现人群）我逐项核过，
与代码一致；「一条已知限制：通知投递失败今天是无声的」也与代码一致
（`NotificationBus` 内 `ILogger` 计数 0；两个 `PublishAsync` 调用点与六个 `DeliverAsync` 投递点
无一读返回值）。

## 非阻断发现

- **F-1｜TASK-05 的 D-1 有两个行号已漂移。** `SelectCharacterLifecycleServices.cs:87`
  （`pvp.ClearReadyExact`）与 `:81`（await）在基线上是**正确的**（我用
  `git show b05d47e4:… | grep -n` 核过：基线正是 81 / 87），但 TASK-06 后来在该方法上方加了 13 行，
  两处现在分别是 `:100` 与 `:94`。窗口的**结构**仍然成立（`await RunAsync(persistence.PersistAsync)`
  在前、`ClearReadyExact` 在后），构造名在该文件内唯一，条目仍可指到代码。**精度瑕疵，不阻断。**
- **F-2｜`TouchSocketNetworkMessageSender.DeliverExplicitAsync:88` off by 3。**
  该文件本轮未改动，`await sendMessage(target.ConnectionId, message, cancellationToken)`
  在基线与现在都是 `:91`；`:88` 是同一个 `foreach` 块的开括号。方法名给对了、语义给对了，
  行号差 3。TASK-05 的 F-2 与 TASK-06 的 C-D7 都用了这个数。**精度瑕疵，不阻断。**
- **F-3｜「判据偶发失败」那套新规则本轮仍是零现场检验**（见上）。
- **F-4｜`ItemPathLiveTests` 一族本轮未硬红**，因为 `PVF_ARCHIVE_PATH` 全程带着；
  这只证明带上它的情形，没有证明不带它会怎样（也不需要）。

## 不由我裁定、按要求复述状态的四件

1. **TASK-06 `Permitted files` 的事后扩张**（补入 `Dfo.Server.Data/World/SessionEntryModels.cs`
   与 `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`）：**仍悬着，至今未获所有者认可。**
   我确认它改的是**实现**而非记载，因此不落在所有者「跨界修记载」那条规矩的射程内；
   `TASK-07` 文件里对此有连续多轮的自陈（`:301`、`:501`、`:578`、`:1213`、`:1267`），
   其中还记着一次「曾把裁定读宽、写成就地认可、后已恢复原状」。**我不裁定。**
2. **那条规矩自陈的代价**（边界对「修记载」不再有硬约束力）：**照原样悬着**，本次验证未改变其状态。
3. **SC-10 今天真正靠的是独立评审而非自动化判据**：属实，且已由 `spec.md` C-3 明写。
   本轮我又加了一次独立复核（未找出第九条），这仍然是「又一个人读了一遍」，不是自动化保证。
   **交所有者。**
4. **可观测性退化（通知投递失败无声）：修还是记债** —— **交所有者。** 我核到的事实：
   `NotificationBus` 无 logger；两个 `PublishAsync` 调用点与六个 `DeliverAsync` 投递点无一读摘要；
   但迁移前旁人投递失败**本来就是无声的**（`TouchSocketNetworkMessageSender.cs:97-101` 是
   `catch { break; }`，不带日志），所以对 socket / 编码失败那一族是**平移不是退化**；
   真正新增的无声只有旁人半边的 notice→帧**映射**异常（C-D8），而
   `SelectedSessionCleanupNoticeKind` 只有两个成员且都已映射、`A_notice_kind_nobody_mapped_is_named`
   钉着，**今天不可达**。`AGENTS.md:475-477` 已如实记为「一条已知限制」。**我不裁定。**

## 结论

十二条成功条件**每一条都有本轮现取的证据**：清洁重建 0 警告 0 错误；全量判据两趟
2126 / 2 / 0、退出码 0；SC-1 由**我自己写的编译探针**证明（四种不匹配全部编译失败，正对照通过）；
SC-2 由全仓 `FindByCharacter` 逐处判定证明（两处合规调用确只取会话标识当运行态目录的键，
从不读 `ConnectionId`）；SC-11 由该判据文件对基线的 **blob hash 相同**证明；
SC-10 的帧不变由我与基线的逐表达式比对证明、差异清单十三条目每条都被我核到它自称的位置
（两条行号有精度瑕疵，见 F-1 / F-2，均不影响可指认）；SC-12 的每一个数字都被我实测复现。
评审通过之后控制器就地做的两处改动（`AGENTS.md` 的 N-2 收紧、
`NotificationHandlerArchitectureTests.cs:73-76` 的数字更正）我已一并核对，**两处都改对了**，
且后者未动任何断言与 `[InlineData]`。

**decision: pass**

---

## Re-verification — after the logging change and the N-11 fix

### 被评估的确切主张

> 「通知知道自己要告诉谁」这个成果已完成：契约 `TaskContract.md`（revision 2）的 SC-1..SC-12 全部达成。
> 上一次验证给过 `pass`（2126/2/0）；此后按所有者裁定加了投递失败日志（+3 条判据 → 2129），
> 又按增量复评的 N-11 修了一处真缺陷（超时/异常分叉只看时钟会丢掉异常，+1 条判据 → **2130**）。

### 验证者身份与边界

第三个独立 fresh-context 验证者。**未做提交、未改任何实现文件或判据文件、未改任务控制状态。**
本轮唯一写入的是本文件的本节（追加，未覆盖任何已有段落）。
变异做完已逐字还原并核过哈希（见下）。环境全程带
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。

工作前后 `git status --porcelain` 均为 **21 项（15 M + 6 ??）**，分支 `notifications-know-their-audience`，
`HEAD = b05d47e4e587d6ab85312ef7f5a3802716805e3c`，未提交。
上一次验证的结论、`reviews/change-review.md` 两节的 verdict、`spec-result.md` 的自陈**仅作线索**。

### 1. 改动面——我自己划的，无夹带

mtime 在本工作区仍然可用（本成果全部工作发生在同一天），以上一次验证收尾
（`verification/completion-evidence.md` 写于 `20:12:13`）为界。
`find Server AGENTS.md .rks-agent -newermt "2026-09-04 20:12"`（排除 `obj/` `bin/`）**全部结果**：

| 时刻 | 文件 | 类别 |
|---|---|---|
| 20:12:13 | `verification/completion-evidence.md` | 上一轮产物本身 |
| 20:13:30 | `tasks/TASK-05-*.md` | 上一轮收尾 |
| 20:50:57 | `Server/tests/.../Notifications/NotificationWorld.cs` | 日志改动 |
| 20:52:04 | `Server/tests/.../Notifications/NotificationRetryTests.cs` | 日志改动 |
| 21:07:01 | `reviews/change-review.md` | 增量复评产物 |
| 21:08:15 | `AGENTS.md` | N-10 措辞改写 |
| 21:08:15 | `Server/tests/.../Notifications/NotificationDeliveryTests.cs` | N-11 判据 |
| 21:08:46 | `tasks/TASK-06-*.md` | N-13 前向注记 |
| 21:08:46 | `Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs` | N-11 修复 |
| 21:09:41 | `sdd-execution.yaml` / `spec-result.md` | 任务控制与自陈 |

**代码与记载侧恰好是自陈的五处 + TASK-06 的一处前向注记，一个不多。**
`Server/src` 里本轮唯一被改的生产文件是 `NotificationBus.cs`；`Server/tests` 里唯一被改的是那三个
未跟踪的 `Notifications/` 文件。四个**已跟踪**判据文件（mtime 17:03）与
`NotificationHandlerArchitectureTests.cs`（19:56）本轮未被触碰。

### 2. SC-4 / SC-8 / SC-10 / SC-11 是否仍成立——**四条全部仍成立**

- **SC-11 — `CommandHandlerArchitectureTests.cs` 对基线逐字节相同（我自己重新算的）**

  ```
  git rev-parse b05d47e4:<该文件>   → 411de89a9f2c2a574015448aea68ec26a87521dd
  git hash-object <该文件>           → 411de89a9f2c2a574015448aea68ec26a87521dd
  git status --porcelain -- <该文件> → 空
  git diff --stat -- <该文件>        → 空
  ```

  判定谓词与 `Exempt` 清单无从被放宽或扩充。本轮该类判据全绿。

- **SC-4** — `NotificationHandlerArchitectureTests.cs` mtime 19:56，早于本轮起点，未被触碰；
  本轮 **14 条全绿**。两个生产处理器 `PvpParticipantPublishedHandler`
  （`PvpParticipantPublishedNotification.cs:23`）与 `SessionCleanupAnnouncedHandler`
  （`SupplementalSynchronizationMessages.cs:193`）我重新看过声明行：
  **都不是主构造函数、无构造参数**，注入面为空。
  `ILogger<NotificationBus>` 进的是**总线**，总线既不实现 `INotificationHandler<>` 也不在
  Protocol 程序集，不在这张表的扫描面内——这个区分我核过，成立。

- **SC-8** — `INotificationBus.cs` mtime 14:42，本轮未改；摘要形状与四个 `NotificationOutcome`
  一字未动。我通读了改后的 `NotificationBus.cs`：`Requeue` 与 `AttemptAsync` 新增的只有
  `logger.LogWarning`，**没有一处改变 outcome 的判定或 `attempts` 的构造**。

- **SC-10** — 帧未被触碰。本轮未动任何处理器、消息类型、`ToMessage` 或收件人解析。
  生产 `PublishAsync` 仍恰为两处（`SelectCharacterHandler.cs:291`、
  `SupplementalSynchronizationMessages.cs:255`），`cleanupDelivery.DeliverAsync` 仍恰为六处
  （`OnlineUserConnectionObserver.cs:95/102`、`SelectCharacterHandler.cs:104/136/156/162`），
  我重新 grep 过，行号全部精确。

### 3. N-11 的修复——**修对了；但两个条件只有一个有判据咬住**

现状 `NotificationBus.cs:232`：

```csharp
if (error is OperationCanceledException && deadline.IsCancellationRequested)
```

变异实测（每次都从 pristine 副本重铺，跑
`dotnet test Dfo.Server.Application.Tests --filter "FullyQualifiedName~Notifications"`，基线 **56/56 绿**）：

| 变异 | 结果 | 红的是谁 |
|---|---|---|
| **MA** 退回只看时钟 `if (deadline.IsCancellationRequested)` | **红 1 / 通过 55** | `Something_thrown_after_the_deadline_is_still_reported_as_what_it_was` |
| **MB** 只看异常类型 `if (error is OperationCanceledException)` | **全绿 56 / 56** | **没有任何判据红** |
| M1 删掉超时那条 `LogWarning` | 红 1 / 通过 55 | `A_recipient_who_ran_out_of_time_is_named_as_that_and_not_as_an_error` |
| M2 `RetryLimit > 0` → `>= 0`（恒真） | 红 2 / 通过 54 | `A_failed_delivery_is_said_out_loud`、`Something_thrown_after_the_deadline_...` |
| M3 从失败那条日志里去掉异常参数 | 红 2 / 通过 54 | 同上两条 |
| M4 `RetryLimit > 0` → `> 1`（收紧） | 红 1 / 通过 55 | `Giving_up_on_a_recipient_is_said_out_loud` |

**结论分两半，必须分开说：**

- **异常类型这一半是必要的，且有牙。** MA 证明退回只看时钟会立刻红，且红的正是 N-11 那条新判据。
  N-11 描述的缺陷确实存在过、确实被修掉了。
- **时钟这一半今天没有任何判据咬住（MB 全绿）。** 它在语义上仍然值得留着——去掉它，一个
  与期限无关的裸 `OperationCanceledException`（调用方自身取消已在前一个 `catch ... when` 被放行，
  所以只剩处理器/传输内部某个无关令牌这一种）会被说成「ran out of its 2s」。但那是**低价值**的差别
  （一个 OCE 本来就不带原因），而 N-11 真正丢东西的那一半已经被钉住了。见 **N-15**。

**还原核对**：`cp` 回 pristine 后
`git hash-object` = `e1ff18beac06bb7a2aea9dafd5e7415f8d3659b5`、
`md5` = `39b28a624a3dfeb498d3d862e168604e`，与变异前逐字节相同（也与 `spec-result.md` 自陈的
`39b28a62...` 相符）；`git status --porcelain` 仍 21 项。**下面所有全量判据都跑在还原后的源码上。**

### 4. 新增的四条判据是否都有牙——**都有，且各咬各的**

由上表：M1 → 超时那条；M2/M3 → 失败那条（并连带 N-11 那条）；M4 → 放弃那条；MA → N-11 那条。
四条判据各自都能被至少一种变异单独打红，没有一条是摆设。
`RetryLimit > 0` 被 M2（放宽）与 M4（收紧）**两侧夹住**，不是可以漂移的字面量。

### 5. `AGENTS.md` 那段改后与代码逐句核对——**准确，且没有一句比代码强**

上一版被判不准的「（那时手上没有异常）」在文件里已**全无残留**
（`grep -n "无声\|已知限制\|那时手上没有异常" AGENTS.md` 为空）。新写法（`AGENTS.md:479-483`）：

| 句 | 代码 | 判定 |
|---|---|---|
| 「投递抛异常 → `Warning` + 异常本身」 | `NotificationBus.cs:242-245` `logger.LogWarning(error, ...)` | ✓ |
| 「到了每收件人期限 → `Warning`，与抛异常**分开说**」 | `:232` 的分叉，两条不同措辞 | ✓ |
| 「（超时也是从 catch 里出来的，异常就在手上；不记它是因为那是取消令牌，不是原因）」 | 超时走 `catch (Exception error)`，`error` 在作用域内；而改后的条件 `error is OperationCanceledException && ...` **保证**进这一支时手上的确是取消——这句现在不但准确，还被代码强制成真 | ✓ 已改对 |
| 「重试额度用尽 → `Warning`；声明 0 次重试的不记这一条，失败那一条已经说过了」 | `Requeue` 的 `if (registration.RetryLimit > 0)`；且 `Requeue` 只在 `outcome == Failed` 时被调，而 `Failed` 只能从 `AttemptAsync` 的 catch 产生，那个 catch **两个分支都必然记一条 Warning**——因果链我自己走过 | ✓ |
| 「弃权与『人不在了』不记——它们不是出了问题」 | 两者在 try 内提前 `return`（`:199`、`:204`），根本不进 catch | ✓ |
| 「37 处 `SendExplicitAsync` / 7 处 `SendNotiAsync`」 | 实测 42 − 5 个定义处 = **37**；9 − 2 = **7** | ✓ |

文件 **635 行**，`git diff --numstat` = **`106  1`**，删除行仍是最初那 1 条，
0 个 CR，无 BOM（`head -c 3 | od -c` = `#  A`）。

**没有造出比代码强的话。** 唯一可挑的是这段没有说出分叉的**判据**已经变成「异常类型 + 时钟」
两个条件——它只说「到了期限 → Warning」，而严格讲，到了期限但抛的是真异常时走的是失败那一支。
两支都记 `Warning`，读者问「失败会不会被说出来」得到的答案仍然正确，所以这是**省略而非过度承诺**，
不落在「一句比代码强」的射程内。

### 6. 计数——自己跑，逐项目相加；**跑了六趟**

清洁重建（两次）：`dotnet build Server/86JP.Server.slnx --no-incremental -v:n` →
`BUILD_EXIT=0`，「已成功生成。0 个警告 0 个错误」，
全日志 `(warning|error) [A-Z]+[0-9]+` 命中 **0**。`TreatWarningsAsErrors` / CS9113 无碍——
`logger` 在两处被实际使用。

`dotnet test Server/86JP.Server.slnx --nologo --logger "console;verbosity=detailed"`，
计数**逐项目读 `通过数:` 再相加**（未用会数错的 `grep -c`）：

| 程序集 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Networking.Tests | 52 | 0 | 0 |
| Protocol.Tests | 517 | 0 | 0 |
| Pvf.Tests | 60 | 0 | 0 |
| Persistence.Tests | 256 | 0 | 0 |
| Host.Tests | 6 | 0 | 0 |
| Infrastructure.Tests | 223 | **2** | 0 |
| Application.Tests | **1016** | 0 | 0 |
| **合计** | **2130** | **2** | **0** |

52+517+60+256+6+223+1016 = **2130**。**与主张的 2130 / 2 / 0 逐值相符**
（Application.Tests 1015 → 1016，恰 +1，与「N-11 新增一条」相符，没有谁被顺手删掉换名额）。
全日志「警告」命中 **0**。两条跳过仍是
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`，
跳过原因 **"An archive is reachable from this working tree."**（档案已加载）。
四条新判据在日志里逐条 `已通过`，我按名核过。

**六趟的实际结果：**

| 趟 | 退出码 | 通过 / 跳过 / 失败 |
|---|---|---|
| 1 | 0 | 2130 / 2 / 0 |
| **2** | **1** | **2129 / 2 / 1** |
| 3 | 0 | 2130 / 2 / 0 |
| 4 | 0 | 2130 / 2 / 0 |
| 5 | 0 | 2130 / 2 / 0 |
| 6 | 0 | 2130 / 2 / 0 |

### 6b. 那条「偶发失败先取身份」的规矩，本轮**拿到了第一次现场检验**

第 2 趟出现一次失败。按 `AGENTS.md:487-500` 的三条一起看：

```
失败 Dfo.Server.Application.Tests.Notifications.NotificationDeliveryTests
      .The_deadline_a_stalled_recipient_runs_into_is_the_configured_one [1 s]
  错误消息:
   Two 50ms deadlines took 1045ms, so some other value was applied
   -- the production default of two seconds would land near four.
  堆栈跟踪:
     at ...NotificationDeliveryTests.The_deadline_a_stalled_recipient_runs_into_is_the_configured_one()
        in ...\NotificationDeliveryTests.cs:line 194
```

| 判别项 | 记载要求 | 实测 | 命中 |
|---|---|---|---|
| 1 异常类型 | `ObjectDisposedException: ... 'SQLitePCL.sqlite3'` | xunit `Assert.True` 断言失败（自带消息，含实测毫秒数） | **否** |
| 2 复跑即绿 | 要真的跑 | 第 3/4/5/6 趟**连续四趟全绿** | 是 |
| 3 栈根在表内 | 三条 SQLite 栈根 | `NotificationDeliveryTests....:line 194`，**不在表内** | **否** |

**判定：这不是已记录的那条 SQLite 偶发，而是一条本成果自己的、此前未被记录的时钟偶发。**
规矩本身在这次检验里**按设计工作了**——它拒绝把这次失败塞进已知那一条，逼出了真身份。

它是什么：`The_deadline_a_stalled_recipient_runs_into_is_the_configured_one`
（`NotificationDeliveryTests.cs:177-197`）读挂钟，断言两个 50ms 期限的总耗时
`>= 100ms` 且 `< 1s`。实测 1045ms，只超上界 4.5%。下界通过。
该用例自己的 `<remarks>` 预写了这件事：headroom 是实测的（四趟并行全量下 197–315ms，上界取约三倍），
并明写「**if this ever goes red at a plausible-looking number, it is the machine, not the deadline**」。
1045ms 正是这样一个数：生产默认的 2 秒两人会落在 4000ms 附近，离得很远。

**它不是产品行为缺陷**：通知总线的收件人集合、outcome 判定、帧内容、日志分叉全都没有参与这次失败，
失败的是一句挂钟上界。但它确实使全量判据在本机**不是确定性绿**（六趟里红一趟）。见 **N-16**。

### 7. 回归——既有断言无一被削弱或删除

- `git diff -- Server/tests` 仍是**同样的 4 个已跟踪文件**，`^-.*Assert` **12** 行、`^+.*Assert` **17** 行，
  与上一轮验证和增量复评**逐值相同**；这四个文件 mtime 17:03，本轮根本没被打开过。
  12 行删除全部是 `BestEffortTargets` 换形（连接 → 角色）与那条以更强形式补回的 `"TownDepartureMessage"`，
  上一轮已逐条裁定，本轮无新增删除。
- 未跟踪的 `Notifications/` 与 `CleanupNotificationDeliveryTests.cs` 无法 diff，改用**点名核对**：
  上一轮验证与增量复评逐条读过断言的 **19 条判据**（含
  `A_recipient_whose_delivery_fails_does_not_stop_the_others`、
  `A_retry_asks_the_handler_again_rather_than_resending_what_it_had`、
  `Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed`、
  `The_peers_notices_become_the_frames_they_always_did`、
  `The_pvp_publication_declares_the_allowance_it_meant_to` 等）**逐个 grep，19/19 全部仍在**，
  且在本轮判据日志里逐条 `已通过`。
- 加上 Application.Tests 净增**恰为 +1**、七个程序集其余六个计数与上一轮**逐值不变**，
  **可以判定：没有判据被删除、改名或换成更弱的写法。**

### 追加的 Non-blocking Observations（承 N-1..N-14 编号）

- **N-15｜N-11 修复里「时钟」那一半没有任何判据咬住。**
  MB 变异（把条件收成只看异常类型）**56 条全绿**。留着它是对的——它把「与期限无关的裸 OCE」
  从「超时」里摘出来——但今天没有任何判据会在有人把它删掉时报警。
  与 N-12 是同一族：**这次修复的牙只长在两个条件中的一个上**。
  若要补，一条「处理器抛出裸 `OperationCanceledException` 而期限未到，记的是失败不是超时」
  的判据就够。不阻断：MA 已证明真正会丢东西的那一半有牙。

- **N-16｜全量判据在本机不是确定性绿：新发现一条本成果自己的时钟偶发（六趟红一趟）。**
  `The_deadline_a_stalled_recipient_runs_into_is_the_configured_one` 的挂钟上界 1s，
  实测 1045ms 而红（超 4.5%）。**它不匹配已记录的 SQLite 偶发**（异常类型不同、栈根不在表内），
  所以按仓库自己的规矩不能算作已知那一条。产品行为未参与这次失败。
  值得所有者看一眼的三个选项：把上界放宽到能容下本机的抖动、把这条用例的挂钟断言换成不读挂钟的写法、
  或把它作为第四行加进 `AGENTS.md` 的偶发身份表并写明「红在一个 plausible 的数上就是机器」。
  **同时这也是那张表的第一次现场检验——表按设计工作了**（它把这次失败挡在了已知那条之外）。

- **N-17｜TASK-06 的前向注记只覆盖了 C-D8 条目本身，同文件另两处「无声」的说法仍是旧的。**
  前向注记写在 `TASK-06:629-633`，内容准确（「总线现在注入 `ILogger`，投递抛异常与到期限各记一条
  `Warning`」，我核过属实），且没有回改历史。但同一份文件的 `:642`
  （「通知投递失败今天是无声的……」，作为交给 TASK-07 的已知限制原文引用）与 `:713-714`
  （轮次小结里重复同一句）今天都已是假的，而注记在 `:642` 之**前**、在 `:713` 之**远前**。
  顺序读的人会先读到注记，所以不构成误导；但 `AGENTS.md:473` 明写「完整差异清单在 TASK-05 / TASK-06」，
  这份文件因此是**在用的参考材料**。若要收尾，在 `:714` 之后补一行指回 `:629-633` 即可。
  不阻断：SC-10 的差异清单**没有变得不完整**，变的只是它对严重性的定性。

### 不由我裁定、按要求复述状态的两件（仍悬着）

1. **那条「跨界修记载」规矩自陈的代价**——边界对「修记载」不再有硬约束力：**照原样悬着**，
   本轮验证未改变其状态，也不代为决定。
2. **SC-10 今天真正靠的是独立评审而非自动化判据**：属实，`spec.md` 的 C-3 已明写这句自陈。
   本轮我没有重新逐条走那份十三条目的差异清单（上一次验证已逐条核到代码窗口，
   而本轮改动未触碰任何收件人解析、帧内容或投递语义，那些结论不会被推翻）。
   所以这条今天仍然是「几个人各读了一遍没找出第九条」，**不是自动化保证**。**交所有者。**

已由所有者裁定并落地、本轮确认落地正确的两件：TASK-06 的 `Permitted files` 事后扩张（**认可**）；
可观测性（**现在就修**，已修，日志与判据都在，四条判据都有牙）。

### 结论

本次改动**没有推翻上一轮成立过的任何一条**：SC-11 的判据文件对基线**哈希仍逐字节相同**；
SC-4 的扫描面不含总线、14 条全绿、两个生产处理器注入面仍为空；SC-8 的摘要文件本轮未动、
新增代码只加日志不改任何 outcome；SC-10 的帧、收件人解析与投递语义一个未改。
改动面就是自陈的五处代码/记载 + TASK-06 一处前向注记，**无夹带**（mtime 划界，全部结果已列出）。
N-11 的修复**修对了**，异常类型那一半由 MA 变异证明有牙；四条新判据**都有牙**且各咬各的，
`RetryLimit > 0` 被两侧夹住；变异后源码**逐字还原并核过哈希**。
`AGENTS.md` 那段与代码逐句一致，上一版被判不准的那句已改对，**没有一句比代码强**。
清洁重建 0 警告 0 错误；全量判据**逐项目相加 2130 / 2 / 0**，与主张逐值相符。

唯一需要所有者知道的新事实是 **N-16**：六趟全量里有一趟红在本成果自己的一条**挂钟**判据上
（1045ms vs 1s 上界），它**不是**已记录的 SQLite 偶发（异常类型与栈根都不匹配），
复跑连续四趟全绿，产品行为未参与。这不改变十二条成功条件的达成，但它使
「全量判据 0 失败」在本机是**高概率**而非**确定**。我把它如实记在这里，不代所有者决定怎么处置。

**decision: pass**

---

## Close-out Verification

### 被评估的确切主张

执行方在 `spec-result.md` 末节「验证发现的三件，及其处置」自陈：N-15 / N-16 / N-17 三条已处置；
**判据 2131 通过 / 2 跳过 / 0 失败，连续三趟同值、全绿**；**生产代码在这三件处置里逐字节未改**
（`NotificationBus.cs` md5 与 N-11 修复后相同）。本次是收口前最后一次验证，只核这一批处置
与「上两次成立过的十二条是否被此后的改动推翻」。

### 验证者身份与边界

第三次独立验证，全部结论出自本轮亲手跑的命令。**未提交、未改任何实现文件或判据、未动任务控制状态。**
唯二的写操作是两次源码变异，**两次都逐字还原并核过 md5**（见第 3 节）。
环境 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，
两条跳过的原因均为 `An archive is reachable from this working tree.`（档案已加载，与基线同形）。

### 0. 改动面——自己划的，与自陈一致，无夹带

上一轮验证的产物 `completion-evidence.md` 写于 `21:24:35`。按 mtime 划界，此后被动过的文件**恰好四个**：

| 文件 | mtime | 性质 |
|---|---|---|
| `Server/tests/.../Notifications/NotificationDeliveryTests.cs` | 21:25:47 | N-16 上界 + N-15 新判据 |
| `Server/src/.../Notifications/NotificationBus.cs` | 21:26:14 | **内容 md5 未变**（变异后还原） |
| `tasks/TASK-06-evaluate-remaining-fanouts.md` | 21:26:40 | N-17 注记 |
| `spec-result.md` | 21:28:26 | 自陈 |

`AGENTS.md`（21:08）、其余全部生产文件与判据文件的 mtime 都在 21:24 之前，本轮处置未触及。
`git status --porcelain` 仍 **21** 项，与上一轮逐值相同。

### 1. 生产代码逐字节未改——**属实**

`md5sum Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs`
→ `39b28a624a3dfeb498d3d862e168604e`，与**上一轮验证亲手记下的**同一个值
（`completion-evidence.md:496`）逐字节相同。除它之外没有任何生产文件在 21:24 之后被动过（见第 0 节）。
清洁重建 `dotnet build Server/86JP.Server.slnx -c Debug --no-incremental` → 退出 0、
**0 个警告 0 个错误**（`TreatWarningsAsErrors` / CS9113 无碍，`logger` 两处实际使用）。

### 2. N-16 的处置——**算术对、解释属实、不再假红（六趟无一红）**

**(a) 算术。** 生产默认在 `NotificationBus.cs:27-28`：
`NotificationDeliveryOptions.Default = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1))`，
即每收件人 **2 秒**。判据用两个收件人，所以「把生产默认抄成常数」会落在 **4 秒**附近，
**2.5 秒的上界仍然挡得住**（`NotificationDeliveryTests.cs:196-199`）。下界 `>= 100ms` 未动
（`:193-195`）。由 `总耗时 ∈ [100ms, 2500ms)` 反推每收件人常数的存活窗口
= **[50ms, 1.25s)**，与自陈的数**逐值相符**；放宽前上界 1 秒对应 [50ms, 500ms)，也对。
**这确实是真的削弱，执行方主动说了，本轮核实数字无误。**

**(b) 「先前那个数是怎么测错的」。** 新 remarks（`:170-177`）写：首测四趟并行全量下 **197–315 ms**，
一秒上界「看起来是最坏值的三倍」，实际不是——后来一趟 **1045 ms**，超上界 **4.5%** 而红。
逐项对照上一轮验证亲手记下的原文（原 remarks 自陈「四趟并行全量下 197–315ms，上界取约三倍」）
与 N-16 实测（1045ms / 1s，超 4.5%）：**每一个数都对得上，没有一句比事实强**。
`3 × 315 = 945 ≈ 1000`，「看起来是三倍」也成立。

**(c) 是否不再假红——自己跑，六趟全量。** 该判据在六趟里的实测耗时：

| 趟 | 1 | 2 | 3 | 4 | 5 | 6 |
|---|---|---|---|---|---|---|
| 耗时 | **1 s** | 317 ms | 345 ms | 401 ms | 328 ms | 720 ms |
| 结果 | 绿 | 绿 | 绿 | 绿 | 绿 | 绿 |

**六趟全绿。** 而第 1 趟（冷启动那趟）报 `1 s`——**在旧的 1 秒上界下它本来就是一次红或擦线**。
这独立坐实了「放宽是必要的」，不是为了掩盖一次孤立抖动。

### 3. N-15 的处置——**两个方向各自变异，两个条件各有一条判据夹住**

分叉现状 `NotificationBus.cs:232`：`if (error is OperationCanceledException && deadline.IsCancellationRequested)`。
基线过滤集 `--filter FullyQualifiedName~Notifications` → **57 通过 / 0 失败**。

| 变异 | 改成 | 结果 |
|---|---|---|
| **只留异常类型**（丢掉时钟） | `if (error is OperationCanceledException)` | 56 通过 / **1 失败**，恰为 `A_cancellation_raised_before_the_deadline_is_a_failure_with_a_reason` |
| **只留时钟**（丢掉异常类型） | `if (deadline.IsCancellationRequested)` | 56 通过 / **1 失败**，恰为 `Something_thrown_after_the_deadline_is_still_reported_as_what_it_was` |

**两个方向各红且只红对应的那一条。N-15 关掉。** 新判据本身
（`NotificationDeliveryTests.cs:352-366`）用 30 秒期限 + 处理器自抛 `OperationCanceledException`，
断言 `entry.Error` 是那个异常**且**日志文本不含 `ran out of`——两侧都咬。
**变异后已逐字还原，md5 复核 `39b28a62…`，`:232` 目视核对，`git status` 仍 21 项。
本报告所有全量判据均跑在还原后的源码上。**

### 4. N-17 的处置——**没有回改历史；但注记的行号索引不完整且有系统性 ±1**

**没有回改历史：属实。** 上一轮验证亲手记下的行号（`TASK-06:629-633` 的旧前向注记、`:642` 的原文引用）
今天**仍在原位、原文逐字未变**；就地标注全部是**追加的括注**（`:537` 行内、`:643` 独立一行、`:714` 行内），
统一注记是**文末新增一节**（`:1241-1253`，全文 1253 行）。**没有一句历史陈述被改写或删除。**

**但「现在文件里不再有未被标注为过时的『无声』陈述」这句不成立。** 统一注记的
「受影响的行」清单（`:1250`）列了六条：`:537`、`:622`、`:642`、`:713`、`:779`、`:1235`。逐条核后：

- **漏了三处**：`:514`（F-5 标题「从『抛出』变成了『无声吞掉』」）、`:559`（「是否顺带把这个无声吞掉修掉……
  留给最终评审」——而它其后确实被修了）、`:598`（Round 2 小结「旁人半边的组帧失败在迁移后被无声吞掉」）。
  另有 `:533`「`NotificationBus` **不注入任何 logger**」，同族、今天同样已假，也不在清单里
  （不过它紧邻已标注的 `:537`，顺序读的人会撞到标记）。
- **`:713`／`:779`／`:1235` 三条各差 1**：实际句子在 `:714`／`:780`／`:1236`。原因可复现——
  `:643` 那行标注是**新插入的一整行**，其后所有行号 +1，而清单用的是插入前的编号。

**不阻断**，理由与 N-17 原判同源：统一注记的**首句是无差别的**（「本文件里**多处**写着……这些句子……
今天已经是假的」），语义上覆盖漏掉的那三处；且 SC-10 的差异清单本身没有变得不完整
（`:1252-1253` 明写差异仍成立，变的只是后果）。记为 **N-18**。

### 5. 计数——自己跑，逐项目读 `通过数:` 相加，**六趟**

`dotnet test Server/86JP.Server.slnx -c Debug --no-build --logger "console;verbosity=detailed"`。

| 程序集 | 通过 | 跳过 | 失败 | 与上一轮 |
|---|---|---|---|---|
| Networking.Tests | 52 | 0 | 0 | 不变 |
| Protocol.Tests | 517 | 0 | 0 | 不变 |
| Pvf.Tests | 60 | 0 | 0 | 不变 |
| Persistence.Tests | 256 | 0 | 0 | 不变 |
| Host.Tests | 6 | 0 | 0 | 不变 |
| Infrastructure.Tests | 223 | **2** | 0 | 不变 |
| Application.Tests | **1017** | 0 | 0 | 1016 → **+1** |
| **合计** | **2131** | **2** | **0** | 2130 → **+1** |

`52+517+60+256+6+223+1017 = 2131`。**与主张的 2131 / 2 / 0 逐值相符。**
净增**恰为 +1**，与「N-15 新增一条」一一对应，其余六个程序集**逐值不变**——
**没有谁被顺手删掉换名额**。

**六趟结果：1–6 趟退出码全 0，全部 2131 / 2 / 0。**（要求三趟，实跑六趟。）
**本轮零失败，所以 `AGENTS.md` 那张偶发身份表没有第二次现场机会**——如实记下：这次没用上。

点名复核（run 6 日志，逐条 `已通过`）：`CommandHandlerArchitectureTests` **12** 条、
`NotificationHandlerArchitectureTests` **14** 条、
`A_recipient_whose_delivery_fails_does_not_stop_the_others`、
`A_retry_asks_the_handler_again_rather_than_resending_what_it_had`、
`Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed`、
`The_peers_notices_become_the_frames_they_always_did`、
`The_pvp_publication_declares_the_allowance_it_meant_to`、
`Something_thrown_after_the_deadline_is_still_reported_as_what_it_was`、
`A_cancellation_raised_before_the_deadline_is_a_failure_with_a_reason`——**全部在且全绿**。

### 6. SC-1..SC-12 是否被此后的改动推翻——**一条都没有**

本轮改动面只有四个文件（第 0 节），其中生产代码 **md5 未变**，判据只**加了一条、放宽了一个挂钟上界**，
另两个是记载。逐条判断：

- **SC-1 / SC-2 / SC-3 / SC-5 / SC-6 / SC-7 / SC-9**：涉及的生产代码与判据一行未动，
  且相关判据本轮六趟全绿。**未被推翻。**
- **SC-4**：`NotificationHandlerArchitectureTests` **14 条全绿**，扫描面与两个生产处理器注入面未动。**成立。**
- **SC-8**：摘要形状与 outcome 判定本轮零改动（`NotificationBus.cs` 逐字节相同）。**成立。**
- **SC-10**：帧、收件人解析、投递语义**一个字节未改**；TASK-06 只多了注记，差异清单条目本身
  （C-D1…C-D8 等）未被删改。**成立（revision 2 口径）。** 见下方仍悬着的一条。
- **SC-11**：`Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs`
  对基线 `b05d47e4` **`git diff` 为空**，md5 双向核对
  `090c93272b2ae9e5019f4411e8a330fe` == `090c93272b2ae9e5019f4411e8a330fe`，**逐字节相同**；
  12 条判据全绿。**不是靠放宽谓词或扩充豁免通过的。**
- **SC-12**：`AGENTS.md` mtime 21:08，本轮处置未触及；上一轮已逐句与代码核过。**未被推翻。**

### 追加的 Non-blocking Observations（承 N-1..N-17 编号）

- **N-18｜TASK-06 统一前向注记的行号索引不完整且系统性差 1。** 漏 `:514`、`:559`、`:598`
  （外加同族的 `:533`「不注入任何 logger」）；`:713`／`:779`／`:1235` 实际在 `:714`／`:780`／`:1236`，
  因为 `:643` 插了一整行而清单用的是插入前的编号。注记首句是无差别的，所以不构成误导。
  若要收干净：把三条漏项补进 `:1250`，并把后三条行号各 +1。**见第 4 节。**

- **N-19｜放宽后的判据 remarks 举例仍用旧窗口的量级。** `:166-168` 写「一个每收件人一百毫秒
  左右的硬编码值也会落在这个窗口里」——这句在 [50ms, 500ms) 时代是贴切的举例，
  在今天的 **[50ms, 1.25s)** 下它**低估了自己承认的削弱幅度**（举例本身仍为真，只是不再是边界附近的例子）。
  完整的新数字只写在 `spec-result.md`，**没有写进判据自己的 remarks**。
  而按本仓自己的习惯，判据的 remarks 才是那句话最可能被读到的地方。

- **N-20｜这条判据的可用走廊已经变窄，值得所有者知道。** 本机六趟实测上限 ≈ **1 s**，
  新上界 **2.5 s**，而它必须守住的那条线（生产默认落点）是 **4 s**。
  也就是说余量只剩约 **2.4 倍**，而它离「失去鉴别力」只有 **1.6 倍**。
  再遇到一次比今天更重的抖动，就没有第三次放宽的空间了——那时唯一的出路是
  **把这条判据改成不读挂钟的写法**（例如让期限来源可观测、断言取的是配置值而非耗时）。
  不阻断：今天它有牙（挡得住生产默认）且六趟全绿。

### 不由我裁定、按要求复述状态的四件

**已由所有者裁定并落地、本轮确认落地正确的两件：**

1. TASK-06 的 `Permitted files` 事后扩张——**认可**。
2. 可观测性——**现在就修**，已修；日志三处、判据四条，且本轮变异证明分叉的两个条件**各有牙**。

**仍悬着、本轮未改变其状态、也不代为决定的两件：**

3. **那条「跨界修记载」规矩自陈的代价**——边界对「修记载」不再有硬约束力。照原样悬着。
4. **SC-10 今天真正靠的是独立评审而非自动化判据**：属实，`spec.md` 的 C-3 已明写这句自陈。
   本轮未重走那份十三条目的差异清单（第一次验证已逐条核到代码窗口，而本轮生产代码
   **逐字节未改**，收件人解析／帧内容／投递语义一个未动，那些结论不会被推翻）。
   所以这条今天仍然是「几个人各读了一遍没找出第九条」，**不是自动化保证**。**交所有者。**

### 结论

三件处置**逐条核实**：

- **N-16** —— 算术对（生产默认实测为 `FromSeconds(2)`，两收件人 4 s，2.5 s 仍挡得住）；
  remarks 里「先前那个数是怎么测错的」与事实**逐值相符**；**六趟全量全绿**，
  而其中第 1 趟报 `1 s`——**旧上界在本轮本来就会再红一次**，放宽是必要的而非掩盖。
  削弱幅度（每收件人 [50ms, 500ms) → [50ms, 1.25s)）**经核属实，且执行方主动说了**。
- **N-15** —— **两个方向各自变异，各红且只红对应的那一条**，两个条件各有一条判据夹住。**关掉。**
- **N-17** —— 没有回改历史（旧行号原位、原文未变、标注全是追加）；但注记的行号索引
  **漏三处、后三条各差 1**，见 **N-18**，不阻断。

**生产代码逐字节未改**（md5 与上一轮验证亲手记下的值相同，且除它之外没有生产文件在 21:24 后被动过）。
**计数逐项目相加 2131 / 2 / 0，六趟同值**，净增恰为 +1 与新判据一一对应，其余六个程序集逐值不变。
清洁重建 0 警告 0 错误。**SC-1..SC-12 无一被此后的改动推翻**，SC-11 的判据文件对基线 `b05d47e4`
**`git diff` 为空、md5 相同**。本轮零失败，`AGENTS.md` 那张偶发身份表没有第二次现场机会。

三条新观察 **N-18 / N-19 / N-20** 全部**非阻断**，其中 **N-20**（这条挂钟判据的余量只剩约 2.4 倍，
离失去鉴别力只有 1.6 倍）值得所有者看一眼，因为下一次抖动就没有第三次放宽的空间了。

**decision: pass**
