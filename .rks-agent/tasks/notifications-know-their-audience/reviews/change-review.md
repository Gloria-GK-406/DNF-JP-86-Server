---
kind: rks-review
verdict: approved
---

# Change Review — 通知知道自己要告诉谁

Scope: `change`（完整变更集，不是单个 Task Unit）。
Reviewer: 独立 fresh-context，未参与实现，未读快审结论即先读代码。
Baseline `b05d47e4`，分支 `notifications-know-their-audience`，未提交。

Inputs：`TaskContract.md`(revision 2 含 Amendments)、`spec.md`(含 C-1/C-2/C-3)、`plan.md`、
`routing-decision.md`(含 Post-hoc Annotation)、按需读取的 TASK-05 / TASK-06 / TASK-07 片段、
以及完整 diff + 五处未跟踪新增。**七个 Task 文件里的 17 轮快审 verdict 仅作支持性证据**，
本判定的每一条都回到代码与判据重新核过。

无 adversarial challenge set，故无 `Challenge Dispositions` 一节。

## 变更集完整性

`git status --porcelain` = 15 M + 5 ??（另加 `.rks-agent/` 产物目录），与交付描述逐行一致。
`git diff b05d47e4 --stat` = +388 / −111。无残留的已删类型引用
（`PvpParticipantPublicationTarget` / `SelectedSessionCleanupNotificationTarget` /
`CleanupSynchronizationDelivery` 全仓 0 命中）。评审过程未在工作区留下任何改动。

**判据独立复跑**（`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`，exit 0）：

| 程序集 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Protocol.Tests | 517 | 0 | 0 |
| Networking.Tests | 52 | 0 | 0 |
| Pvf.Tests | 60 | 0 | 0 |
| Persistence.Tests | 256 | 0 | 0 |
| Host.Tests | 6 | 0 | 0 |
| Infrastructure.Tests | 223 | 2 | 0 |
| Application.Tests | 1012 | 0 | 0 |
| **合计** | **2126** | **2** | **0** |

与交付声称的 2126/2/0 逐值相同（基线 2069/2/0，净 +57）。整份输出 `warning|警告` 计数为 **0**。
两条 skip 是 `PvfQuestDefinitionSourceTests` / `PvfTownDefinitionSourceTests` 的
`A_missing_archive_fails...`——档案已加载的标志，与 Assumptions 一致。

## 契约成功条件逐条独立判定

| ID | 判定 | 独立核对到的事实 |
|---|---|---|
| SC-1 | **达成** | `INotificationBus.PublishAsync<TNotification>` 约束 `where TNotification : class, INotification<TNotification>`。自指约束使 `PublishAsync<INotification<X>>(...)` 不满足 `INotification<INotification<X>>` → CS0311；显式类型实参与实参不符时亦不可编译。未注册类型在启动前被拒（下条）。 |
| SC-2 | **达成** | `NotificationAudience` 只有一个形态 `TheseCharacters(IReadOnlyList<CharacterId>)`，全程无 `NetworkConnectionId`。解析在 `NotificationAudienceResolver.Resolve`。调用点（`SelectCharacterHandler.cs:291`、`SupplementalSynchronizationMessages.cs:255`）不出现连接查找。 |
| SC-3 | **达成** | `INotificationHandler<T>.ComposeAsync(recipient, ...)` 按收件人；空列表 = 弃权。判据 `Each_recipient_is_told_what_the_handler_made_for_them`、`A_recipient_the_handler_declines_hears_nothing_and_is_not_a_failure`。 |
| SC-4 | **达成** | `NotificationHandlerArchitectureTests` 注入面规则 = 「`Dfo.Server.Abstractions` 内以 `DomainService` 结尾的接口」，`Exempt` 字典**为空**。两个生产处理器（`PvpParticipantPublishedHandler`、`SessionCleanupAnnouncedHandler`）**无构造参数**。反向判据（store / table / runtime directory / command bus / string 被拒；本地同名接口被拒）齐备。 |
| SC-5 | **达成** | `NotificationBus.AttemptAsync` 逐收件人 try/catch；`A_recipient_whose_delivery_fails_does_not_stop_the_others`、`A_handler_that_throws_for_one_recipient_fails_only_that_one`、`A_presence_lookup_that_throws_costs_only_that_recipient`。 |
| SC-6 | **达成** | `IsStillHere`（`NotificationBus.cs:211-213`）先问再合成 → `Gone`，不入队；`Failed` 入队。判据 `Someone_who_left_is_told_apart_from_someone_whose_delivery_failed`、`Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed`。 |
| SC-7 | **达成** | `NotificationRetry` 只记 (registration, notification, characterId, attempts)，**不存连接、不存帧**。`RetryAsync` 重新 `Resolve` + 重新 `ComposeAsync`。上限来自注册：`Requeue` 判 `used < RetryLimit`；`retryLimit:2` 时总尝试 3 次并归零（判据实证）。 |
| SC-8 | **达成** | `NotificationDelivery(IReadOnlyList<NotificationAttempt>)` + `With(outcome)`，四归类 `Delivered/Declined/Gone/Failed`。`The_summary_accounts_for_every_recipient` 钉住「摘要覆盖解析到的每个人」。 |
| SC-9 | **达成** | `PlanPvpPublication` 现为 `new(ReadyOn(...).Select(v => v.CharacterId), CreatePvpParticipant(result))`，**无任何连接查找**。见下方「关于 CK-022 / CK-023 的独立裁定」。 |
| SC-10 | **达成（revision 2 口径）** | 帧逐值不变已核（下节）；差异清单完整性见下节。 |
| SC-11 | **达成** | `Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs` 对基线 **`git diff` 为空**，不在 `git status` 内。判定谓词（`IsPermitted` = `Exempt` ∪ `IsDomainShape`；后者 = Abstractions 程序集 + `DomainService`/`DomainStore` 后缀）与 `Exempt` 字典均未动。新增的 `NotificationHandlerArchitectureTests` 是**另一张更严的表**（不含 `DomainStore`、`Exempt` 为空），不是对旧表的扩充。 |
| SC-12 | **达成** | `AGENTS.md` 新增两节；逐数复核见下节。 |

**未发现缺失行为。多余行为均可归因**：迁移第二处扇出（清理旁人段）由 plan T-006 的判定规则明文授权；
`ICleanupNotificationDelivery` 是该迁移的必要构成（六个投递点的顺序封装）；
`NotificationHandlerRegistry` 的重复注册检查、`NotificationCatalogRegistration` 的「声明而非发现」
均超出 Spec 字面但方向安全、有判据、且各自写明理由。

## SC-10 / CK-024 / CK-027：帧不变与差异清单完整性（独立复核）

**帧逐值不变——成立。**
PvP：旧 `new PvpParticipantPublicationMessage(target.Participant)`，其中 `Participant` 即
`CreatePvpParticipant(result)`；新 `PvpParticipantPublishedHandler` 返回
`new PvpParticipantPublicationMessage(notification.Newcomer)`，`Newcomer` 同为 `CreatePvpParticipant(result)`。
清理：旧 `target.OrderedNotices.Select(ToMessage)`；新 `notification.Notices.Select(CleanupNoticeMessages.ToMessage)`
——**同一个 `ToMessage` 函数、同序、同内容**。
我另核了终点一致性：`GameMessageSender.SendExplicitAsync`（`GameMessageSemantics.cs:101-113`）**只做 `Cast`**，
不加校验或行为，两条路径终点同为 `TouchSocketNetworkMessageSender.SendAsync`。

**差异清单完整性——我这一轮没能找出未列的第九条。**
方法：抛开两份清单，自己逐分叉点比 `TouchSocketNetworkMessageSender.DeliverExplicitAsync`（`:69-105`）
与 `NotificationBus.PublishAsync/AttemptAsync/RetryAsync` 两侧，然后回头对表。

| 我自己找到的分叉点 | 清单归属 |
|---|---|
| 名单来源：旧 `value.Session ?? FindByCharacter` vs 新投递时 `FindByCharacter` | D-1 / D-2（PvP）、C-D1 / C-D2（清理） |
| 解析后、轮到前换连接 → `IsStillHere` 判 `Gone` | D-5 / C-D5 |
| 每收件人 2 秒期限（旧侧无） | D-7 / C-D7 |
| 重试（PvP `retryLimit:1`，可致重复送达；清理 `retryLimit:0` 不适用） | D-4 |
| 发送门槛：旧按已解析连接数 vs 新按角色数 | 「空 publish」 |
| 发布前三步（`GetRegistration`/`Resolve`/DI 解析）不在逐收件人守卫内 | D-6 |
| 摘要产出后被丢弃 | 「摘要被丢弃」 |
| 旁人半边帧映射失败：旧抛到调用点 → 新被逐收件人守卫吞掉 | C-D8 |
| 清理投递点 3 / 4 的窗口跨过 `PrepareAsync`/`PlaceAtSpawnAsync`/`PrepareLeaseAsync` | TASK-06「窗口比 TASK-05 更宽的一点」 |
| 逐 target `break` vs 逐 recipient 中止 | **无差异**（同语义，已复核） |
| 旧侧 `if (target.ConnectionId == requiredDirectConnectionId) continue;` 无对应物 | 见下方 N-1 |
| 旧侧 best-effort 失败 `catch { break; }` **本来就无日志** | **无差异**（对判定「可观测性退化」有直接影响，见下） |

**结论与其强度**：这句话只等于「又一个独立上下文读了一遍两条路径没找出第九条」，
**不等于清单已被证明穷尽**——`spec.md` 的 C-3 已经把这一点写明（这条成功条件今天靠的是独立评审
而非自动化判据），我确认那句自陈**准确且必要**，没有把「可证伪」说得比实际强。

我另核了 TASK-05 Round 5 快审曾判阻断的两处操作化文字，**均已收口**：
`spec.md` AC-014 与 `plan.md` CK-024 现均写「收件人集合**与投递语义**」，
`spec.md` External Behavior 那句也已加上「首轮投递内」的限定并指向 D-4。

## 收件人集合的竞态窗口差异——独立核实结论

清单里的每一条我都回到了它自称的代码窗口，**行号逐条命中**：
`NotificationAudienceResolver.cs:40`（`FindByCharacter`）、
`NotificationBus.cs:211-213`（`IsStillHere` 两半）、
`NotificationBus.cs:166-167`（`CreateLinkedTokenSource` + `CancelAfter`）、
`NotificationBus.cs:26-27`（`Default = (2s, 1s)`）、
`SelectCharacterLifecycleServices.cs:53-57`（旁人枚举）、
六个投递点（`OnlineUserConnectionObserver.cs:95` / `:102`，
`SelectCharacterHandler.cs:104` / `:136` / `:156` / `:162`）——**四个在 catch 里，`:156` 在补偿命令之后**，
逐行核过属实。

差异的**方向**也核过：三类窗口全部落在「旧代码把帧送到一个已被顶替的会话」上，
即旧行为是**送错**而非失败——这一点对判定「下调是否掩盖了退化」是关键的，属实。

## AGENTS.md / `<remarks>` / Task 行号表——逐数复核

| 记载 | 位置 | 复核 |
|---|---|---|
| 37 处 `SendExplicitAsync` | `AGENTS.md:465` 附近 | **实测 37**（`grep -rn "SendExplicitAsync" Server/src --include=*.cs` 得 42，去掉 `INetworkServices.cs` / `GameMessageSemantics.cs` / `TouchSocketNetworkMessageSender.cs` 三个定义处得 37）✓ |
| 7 处 `SendNotiAsync` | 同上 | **实测 7**（同口径）✓ |
| 「其余『回给请求方』的发送一处没动」 | 同上 | 基线同口径为 43，净 −6 = 移除 7（handler 5 + observer 2）+ 新增 1（`CleanupNotificationDelivery`），**全部是本轮两处扇出**，回复路径确实一处未动 ✓ |
| 六个投递点 / 四个在 catch | `AGENTS.md:358-360`、`SupplementalSynchronizationMessages.cs` `<remarks>`、`CleanupNotificationDeliveryTests` `<remarks>` | 三处同数，逐行核过 ✓ |
| `SelectCharacterLifecycleServices.cs:120` 带离开者自己的 `ConnectionId` | `AGENTS.md:432` | `:119-120` 正是 `new SelectedSessionCleanupNotificationPlan(session.ConnectionId, ...)` ✓ |
| 「在它之前有**两个**领域服务……知道了 socket」 | `AGENTS.md:433` | 恰为 `PlanPvpPublication` 与 `CleanupAsync` 两处 ✓ |
| 摘要「四种归类」 | `INotificationBus.cs` `<remarks>`「Four, and the two that are not failures…」 | 枚举恰 4 个成员，且「两个不是失败的」= `Declined`/`Gone` ✓（此处即已修过的 `两个→四个`） |
| 「迁移了两处扇出」「第三个候选不是扇出」 | `AGENTS.md:452-454` | `presence.InArea` 全仓 3 处调用；`SessionAdjacentEntryDomainService.cs:27` 确为「给入场者本人算周围有谁」，收件人 1 个，且不查连接 ✓ |
| 栈根表三条 / 第一条见于三个用例名 / 取证缺口两条 | `AGENTS.md:483-497` | 表 3 行、首行列 3 个用例名、缺口列 2 条 ✓（契约 Assumptions 写的是「两条不同用例」，是更早的快照，非矛盾） |
| `NotificationHandlerArchitectureTests` 14 条 | TASK-05 Round 5 | 实数 1+1+5+2+1+1+1+1+1 = **14** ✓（此处即已修过的 `13→14`） |
| 清单八条 / C-D 五条 | TASK-05 第四版表、TASK-06 F-2 | 八格逐条与正文对应；C-D1/2/5/7/8 各带自己的窗口 ✓ |

**唯一发现的数字不准**：`NotificationHandlerArchitectureTests.cs:73` 的
「The **four** below are the ways a handler could reach a write」之下有 **5 个 `[InlineData]`**
（第五个 `typeof(string)` 不属于所描述的四条写路径）。见 N-4，非阻断。

## 关于 CK-022 / CK-023 的独立裁定（TASK-05 明文留给完整评审）

全仓 `FindByCharacter` 生产调用现有 4 处：
`NotificationAudienceResolver.cs:40`、`NotificationBus.cs:212`（通知服务，本就该在）、
`SelectCharacterLifecycleServices.cs:111`（清理后的存在性判断，不取会话）、
`SelectCharacterLifecycleServices.cs:154`（`PlanPvp`）；另有
`ExperienceBonusDomainService.cs:31`。

**裁定：RQ-004 / SC-9 达成。** 理由：`:154` 的 `owner` 与 `ExperienceBonusDomainService` 的 `session`
**只作为 `runtimes.GetRequired(...)` / `runtimeSessions.TryGet(...)` 的键**，全程未读 `ConnectionId`，
且都不是为投递寻址——`PlanPvp` 建的是回给请求方的大厅快照，经验加成读的是驻留状态。
SC-9 的对象是「被迁移的那处扇出所在的领域服务不再做**连接**查找」，`PlanPvpPublication` 已归零。
CK-022 的 plan 文字是**文件**级口径（「`SelectCharacterLifecycleServices` 中连接查找的计数 0」），
按其字面不成立；按 SC-9 的语义成立。我按契约的成功条件判，不按 plan 的措辞判。
相关的记载精度问题见 N-2。

## 架构：落地的形状是不是谈定的那个

逐参数对照 `routing-decision.md` 记下的所有者取舍：

- **人群作为值、按角色而非枚举 + id** — `NotificationAudience` 是 record 层级，唯一形态 `TheseCharacters`；
  `OneCharacter` 按 plan 的判断退化为单元素集合，未多立类型。✓
- **自指泛型（调用点没得选）** — `INotification<TSelf>`，且 `<remarks>` 诚实写明它**不能**阻止
  「向未密封基类加宽」，只关上契约接口本身那扇门。✓ 没有把保证说大。
- **处理器只读** — 接口 `<remarks>` + 空豁免的注入面判据 + 两个零依赖的生产处理器。✓
- **重试上限按通知声明** — `AddNotificationHandler<,>(retryLimit)` 落在 registration 上，
  计数落在 `NotificationRetry.Attempts` 上（「上限在注册、计数在实例」正是谈定的形状）。✓
- **超时全局一致，不按通知特例** — `NotificationDeliveryOptions.PerRecipientTimeout` 单例，
  **没有任何按通知覆盖的入口**。✓ 这正是路由决策点名担心的「合理得像个改进」的偏离之一，未发生。
- **重试不缓存** — `NotificationRetry` 不含连接、不含帧；判据
  `A_retry_asks_the_handler_again_rather_than_resending_what_it_had` 以「重试前改内容」钉死。
  ✓ 这是第二处点名担心的偏离，未发生。
- **不套到那些回复路径上** — 实测回复路径一处未动。✓ 第三处点名担心的偏离，未发生。

**我逐一找了「合理得像个改进」的偏离，找到一处需要写在明处但不构成偏离的判断**：
重连的收件人在 publish 途中被判 `Gone` 而**不是**改投新连接。改投看起来更对，
但它会让一个早于新会话存在的帧与新会话自己的入场序列赛跑。这个取舍在
`NotificationOutcome.Gone` 的 `<remarks>` 里写满了理由，并有判据
（`Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed`）与其在重试路径上的**相反**
处置（`A_retry_goes_to_the_connection_they_are_on_now`）互为对照。**我判定这是自洽且被记下的选择，
不是偏离。**

分层与切片（`AGENTS.md` 口径）：
`Abstractions/Notifications/` 放人群、通知契约、处理器契约、总线与摘要——与 plan 的 BD-001/003/005 一致，
且 Abstractions 引用 `Dfo.Server.Data.*` 是既有惯例（`ISessionPresence.cs` 同样如此）。
`Application/Notifications/` 放解析、注册表、总线实现、重试——BD-002/004/006/007 一致。
通知类型与处理器住在 Protocol 对应切片（`Game/SelectCharacter/`、`Game/StateSynchronization/`）——BD-009 一致。
`ICleanupNotificationDelivery` 是 Protocol 内的投递服务，替换的 `CleanupSynchronizationDelivery` 原本就在同一文件。
**位置全部合规。**

## 判据是否被迁就（`git diff -- Server/tests` 逐条）

四个被改的判据文件，改动全部可归因，**未发现被削弱或删除的断言**：

- `Dfo.Server.Application.Tests.csproj`：+`Microsoft.Extensions.Hosting`，为
  `The_registry_is_asked_for_before_the_first_hosted_service_starts` 用真实 host。三个同级项目已有此依赖。
- `SelectCharacterLifecycleTests.cs`：替身签名跟随构造函数变化；`SessionEntry.PlanPvpPublication` 跟随返回类型变化；
  **净增**两条断言（人群逐值 + newcomer 取自实际发布的 state）。`RecordingNotifications` 在 ledger 里
  记录同一个 `"target"` 字样，使既有的顺序断言语义不变——已核。
- `SessionGenerationOwnershipTests.cs`：`BestEffortTargets` → `PeerAudience` + `PeerNotices`，
  期望值由连接改为角色，**同强度**。原 `Assert.All(targets, …)` 消失是因为「每人一份」在新模型里
  结构性成立（一份 `PeerNotices` 给所有人），不是断言被放弃。
- `OnlineUserServiceTests.cs`：**这是被点名的那一处**。`Assert.Equal("TownDepartureMessage", …)`
  在此处确实消失了（该层不再做映射），但已在
  `CleanupNotificationDeliveryTests.The_peers_notices_become_the_frames_they_always_did`
  以**更强**的形式补回：断言的是 `["ExpertStoreClosedMessage", "TownDepartureMessage"]` **两条帧、按序**，
  而原断言只钉了一条。**核实属实，且确实更强。**
  另有 `A_notice_kind_nobody_mapped_is_named` 把未映射的 kind 钉成会指名的抛出。

新增判据本身的质量：反向判据齐备（`TheSweepFindsTheNotificationHandlers` 防空扫描、
`ATypeOutsideTheVocabularyIsRefused` 防「规则全放行」、`A_type_outside_the_seam_layer_is_refused_however_it_is_named`
防「靠命名冒充」、`ShapeNoOneCanSend` 防「筛选器被删」、`The_deadline_a_stalled_recipient_runs_into_is_the_configured_one`
的上界防「写死一个数」），并且每条上界/下界都写明了它**不能**决定什么。这一点少见地诚实。

## 必须由所有者裁定的四件——状态复述（我不代为决定）

1. **TASK-06 `Permitted files` 的事后扩张**（补入 `Dfo.Server.Data/World/SessionEntryModels.cs` 与
   `ProtocolModule.cs`）：仍**悬着**。我确认它改的是**实现**而非记载，因此不落在所有者「跨界修记载」
   那条规矩的射程内；也确认两处都是该迁移的必要构成（计划换形 + 接线），不含顺带改动。
   **由所有者裁定，我不裁。**
2. **那条规矩自陈的代价**（边界对「修记载」不再有硬约束力）：**照原样悬着**，本次评审未改变其状态。
3. **SC-10 今天靠的是独立评审而非自动化判据**：已授权下调属实（契约 Authorization 表新增
   `contract-amendment` 一行，scope 限死在 SC-10 / RQ-014 及同源条款），且 `spec.md` C-3 已把这层
   「靠什么支撑」写明。**我确认这一自陈准确**，并把它作为本次 approved 的已知条件交回所有者。
4. **可观测性退化：修，还是记成债**：**交所有者。** 我把独立取到的事实放在下面 N-3，
   包括一条会改变判断的事实——它比「彻底新增的退化」要窄。

## Non-blocking Observations

- **N-1｜旧侧 `requiredDirectConnectionId` 自我跳过无新对应物。**
  `TouchSocketNetworkMessageSender.cs:84-85` 的 `if (target.ConnectionId == requiredDirectConnectionId) continue;`
  在新路径没有等价物。我核过其可达性：`ReadyOn` / `InArea` 都以 `excluding` 排掉本人，
  且一个连接只对应一个账号 → 一个选中角色，因此该分支今天不可达，**无可观察差异**。
  TASK-05 的分叉表把它列为「未变（跳过规则的结构性对应物）」，TASK-06 未列。
  由于它被判定为「无差异」，不列入**差异**清单是可接受的。记在这里以免日后被当作漏项重新发现。

- **N-2｜`AGENTS.md:434` 那句读起来比代码严。**
  「禁的是『拿一个角色去查它在哪个连接』——`FindByCharacter` 之类」。生效的限定其实是上一句的
  **「为投递」**；但一个照这句去 grep 的读者会撞上两处仍然存在的生产调用
  （`SelectCharacterLifecycleServices.cs:154`、`ExperienceBonusDomainService.cs:31`），而记载没提它们。
  两处都只把会话当运行时目录的键、从不读 `ConnectionId`，因此**不违反规则本身**。
  建议一行补注：「拿到会话只作运行时目录的键、不读 `ConnectionId` 的，不在此列（现有两处：…）」。
  这条落在 SC-12 / CK-029「无一句比代码强」的射程内，故写在此处而非略过。

- **N-3｜可观测性退化的准确边界**（交所有者的第 4 件，我只给事实）：
  `NotificationBus` 确实**无 `ILogger`**（该文件 `ILogger` 计数为 0），
  两个生产 `PublishAsync` 调用点（`SelectCharacterHandler.cs:291`、`SupplementalSynchronizationMessages.cs:255`）
  与六个 `DeliverAsync` 投递点**都不读摘要**。但有一条会缩小这个判断的事实：
  **迁移前的旁人投递失败本来就是无声的**——`TouchSocketNetworkMessageSender.cs:97-101` 是
  `catch { break; }`，不带任何日志。所以对「socket 失败 / 编码失败」这一族，**不是退化，是平移**。
  **真正新增的无声只有一族**：旁人半边的 notice→帧**映射**异常，迁移前抛到调用点（`:104` 那一处
  会打进工作流失败），现在被 `AttemptAsync` 的兜底 catch 吞成 `Failed`，且该通知 `retryLimit: 0`
  → 直接丢弃 → 无人读摘要（即 C-D8）。其今天的可达性为**零**：
  `SelectedSessionCleanupNoticeKind` 只有两个成员且都已映射，本人半边仍在调用点即时映射会照旧抛出，
  并有 `A_notice_kind_nobody_mapped_is_named` 钉住。
  另需记下的是**未被这条抵消的部分**：`Failed` / `Gone` / 达上限丢弃这三种结局在生产里
  今天完全没有出口（`NotificationRetryDrainService` 只在**整趟排空抛异常**时记日志）。
  `AGENTS.md:471-473` 已如实把这条写成「一条已知限制」。
  **我判定这不构成阻断**：契约、Spec、Plan 都未要求日志；SC-8/RQ-012 要求的是「交回一份摘要」
  而非「有人读它」，摘要已交回并有判据；而唯一真正新增的无声路径今天不可达且被判据钉住。
  **修还是记成债，由所有者定。**

- **N-4｜数字不准一处。** `NotificationHandlerArchitectureTests.cs:73` 写「The four below」，
  其下有 5 个 `[InlineData]`。第五个 `typeof(string)` 不是所描述的四条写路径之一。
  建议改为「The four write paths below, plus one plainly foreign type」或类似。

- **N-5｜切片表已被这次新增再撑宽一格。** `AGENTS.md:322` 的「最多六类制品」未把
  「通知类型 + 处理器」算进去（`Game/SelectCharacter/PvpParticipantPublishedNotification.cs`）。
  该表在基线就已经不严（同目录的 `SelectedCharacterStateMessageFactory.cs` 同样不在六类内），
  故非本轮引入的漂移；但新增的 CMD ⇒ Notify 一节是补这一格的自然位置。

- **N-6｜通知处理器的返回类型不携带 Game 的 `0x00`/`0x01` 纪律。**
  `ComposeAsync` 返回 `IReadOnlyList<IServerNetworkMessage>`，而它替换掉的
  `GameExplicitMessageDelivery` 走的是 `IGameServerMessage`。这是总线要同时服务多端点的必然代价，
  两个生产处理器返回的类型都正确；但「每个 Game 出站 message 必须且只能实现二者之一」这条纪律
  在这条路径上由**编码器注册表在运行时**兜底，而运行时失败正好落在 N-3 那条无声路径上。
  值得在 CMD ⇒ Notify 一节添一句提醒。

- **N-7｜D-4 / N-9 的措辞比事实略窄。** 「被重试的收件人排到队尾，其帧晚于同批其他人到达」
  只说了同批内的次序；实际上重试帧由 `NotificationRetryDrainService` 的后台循环
  （默认 1 秒周期）发出，因此它可以晚于**该连接在这一秒内收到的任何无关帧**。
  差异的**类别**（重试导致延后送达）已列并指到代码窗口，故不算漏项；建议把这句的范围说满。

- **N-8｜TASK-06 已如实留档的两处名不副实仍在**：`OnlineUserServiceTests` 里
  `..._AttemptsResolvedNotificationsWhenCleanupFails` 的 "Resolved" 已不成立，
  且该用例仍构造 `RecordingExplicitSender` 而无人断言它。不影响任何断言效力，已披露，不必本轮改。

- **N-9｜上位文档的「46 处 `SendExplicitAsync`」是陈旧数，且在基线就不对。**
  同口径实测基线为 **43**、现为 **37**。它出现在 `TaskContract.md` 的 Current Situation / Scope-Out
  与 `spec.md` 的 Non-Goals 里，作为「不动回复路径」的理由的**量级**支撑——量级结论不受影响
  （绝对多数无旁人收件人）。`AGENTS.md` 已给出实测数并加了「数会变，看的是它旁边那句理由」的限定，
  处理恰当。契约与 Spec 是历史产物，我不建议改写。

## Verdict

**approved。**

十二条成功条件逐条独立核对全部达成（SC-10 按已授权的 revision 2 口径）；
判据独立复跑 2126 / 2 / 0、0 警告；SC-11 所依赖的判定谓词与豁免清单文件对基线**逐字节未变**；
未发现缺失行为，多余行为均可归因到已授权的范围；未发现被削弱或删除的断言，
被点名的那条 `"TownDepartureMessage"` 断言确已以更强形式补回；
落地的接口形状与所有者逐参数谈定的那个一致，路由决策点名担心的三处「合理得像个改进」的偏离
一处也没发生。

上列九条 Non-blocking Observations 均不影响本次接受。
第「必须由所有者裁定的四件」原样交回，本评审**不代为决定**其中任何一件——
尤其 N-3（可观测性）与 TASK-06 的 `Permitted files` 事后扩张。

## Controller Note — 评审通过之后就地处理的两条观察

评审给的是 `approved`，下面两条是它记的 non-blocking 观察。**在这里处理而不是留给验证阶段，
因为两条都是本成果的招牌毛病（一句话比代码强或宽），留着就会进最终交付。**

### N-2：`AGENTS.md` 那条规则读起来比代码严

原句：「禁的是**拿一个角色去查它在哪个连接**——`FindByCharacter` 之类」。
生效的限定其实在**上一句**的「为投递」，而单看这句会把两处合规调用误判：

- `SelectCharacterLifecycleServices.cs:154`（`PlanPvp`）
- `ExperienceBonusDomainService.cs:31`

两处取的都是**会话标识**当运行态目录的键，**从不读 `ConnectionId`**。

已改成把两个限定写进同一句（「查它在哪个连接、**为的是给他发东西**」），并**点名这两处合规调用**，
外加一句：按「见到 `FindByCharacter` 就是违规」去 grep 会把它们误判。

### 评审找到的唯一一处数字不准

`NotificationHandlerArchitectureTests.cs:73` 写 "The four below"，其下有 **5** 个 `[InlineData]`。
第五个 `typeof(string)` 不属所述的四条写路径——它在那里是为了让「规则悄悄变成接受一切」也会红。
已改成 "The first four … The fifth is not a way to reach anything"，把它的用途说出来。

**这是本成果修的第三个错数**（前两个：`13→14`、`两个→四个 in catch`）。三个都在注释或文档里，
三个都是「数了别的东西」或「没数」。

### 改动范围与核对

- `AGENTS.md`：累计 `+98 / −1`，删除行仍为 **1**（最初那条有意替换的第 4 类），0 CR，无 BOM，627 行。
- `NotificationHandlerArchitectureTests.cs`：仅 `<remarks>`，**断言与 `[InlineData]` 一个未动**。
- 构建 0 警告 0 错误；全量判据复跑见验证阶段。

**这两处改动发生在 approved 之后**，须在验证阶段一并核对。

## Independent Re-review — after the logging change

Scope: `change`（增量完整变更评审）。上一轮 `approved`、验证 `pass` 之后，所有者裁定
「现在就修：给总线加日志」，本节只判**这次改动**以及它**有没有推翻上一轮成立过的东西**。
Reviewer: 独立 fresh-context，先读代码再读上一轮结论。基线 `b05d47e4`，分支
`notifications-know-their-audience`，未提交。未提供 adversarial challenge set，故无
`Challenge Dispositions` 一节。**本次评审未做提交、未改任何实现文件或判据、未改任务控制状态**；
变异做完已逐字还原并核对哈希（见「变异」一节）。

### 本次改动的边界——我自己核的，不是照抄自陈

用 mtime 划出这一轮真正动过的文件（上一轮验证收尾在 19:56 之前）：

| 文件 | mtime | 属本轮 |
|---|---|---|
| `Server/tests/.../Notifications/NotificationWorld.cs` | 20:50 | 是 |
| `Server/tests/.../Notifications/NotificationDeliveryTests.cs` | 20:52 | 是 |
| `Server/tests/.../Notifications/NotificationRetryTests.cs` | 20:52 | 是 |
| `AGENTS.md` | 20:53 | 是 |
| `Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs` | — | 是 |
| `INotificationBus.cs` / `INotificationHandling.cs` / `INotificationServices.cs` | 14:42 / 13:01 / 13:21 | 否 |
| `ProtocolModule.cs`、两个生产处理器、`SupplementalSynchronizationMessages.cs` | ≤ 18:33 | 否 |
| 四个被改的**已跟踪**判据文件 | 17:03 | 否 |
| `NotificationHandlerArchitectureTests.cs` | 19:56 | 否（上一轮 Controller Note 那次） |

**恰好是自陈的五处，无夹带。** `git status --porcelain` 仍为 15 M + 6 ??，与上一轮一致。

### 1. 上一轮成立过的，有没有被推翻

| 条 | 判定 | 独立核到的事实 |
|---|---|---|
| **SC-4**（处理器只读） | **未被触碰** | 判据的扫描面是 `typeof(ProtocolModule).Assembly` 里实现 `INotificationHandler<>` 的非抽象类型（`NotificationHandlerArchitectureTests.cs:179-184`）。`NotificationBus` 在 `Dfo.Server.Application`，实现的是 `INotificationBus` / `INotificationRetryDrain`，**两个条件都不满足**。两个生产处理器本轮 mtime 未变，仍是零构造参数。 |
| **SC-8**（摘要形状） | **未被触碰** | `INotificationBus.cs` 本轮未改（mtime 14:42）。`PublishAsync` 里 `attempts.Add(...)` 与 `AttemptAsync` 的四个返回值与上一轮逐字相同；`Requeue` 新增的只有 `logger.LogWarning`，不改任何 outcome。 |
| **SC-10**（帧逐值不变 + 差异清单） | **帧未被触碰**；清单有一处**变陈旧**（见 N-13） | 本轮未动任何处理器、消息类型或 `ToMessage`。日志不产生帧、不改收件人集合、不改 `Delivered/Declined/Gone/Failed` 的任何一条判定。 |
| **SC-11**（`CommandHandlerArchitectureTests` 未动） | **仍成立** | 我重新核了哈希：`git rev-parse b05d47e4:<该文件>` = `git hash-object <该文件>` = `411de89a9f2c2a574015448aea68ec26a87521dd`，`git status --porcelain -- <该文件>` 为空。**对基线逐字节相同。** |

**关于 `ILogger` 进入构造函数是否触碰注入面规则——这个区分成立，我逐条核过：**

- `NotificationHandlerArchitectureTests` 管的是**处理器**（`INotificationHandler<>` 实现者，且限
  Protocol 程序集）。总线既不是处理器、也不在那个程序集，**不在扫描面内**。
- `CommandHandlerArchitectureTests` 管的是**命令处理器**，同样不含总线。
- 全仓再无第三张注入面判据（`grep -rln "Architecture\|GetConstructors()" Server/tests` 得 7 个文件，
  其余五个是领域服务 / 奖励规则的普通用例）。
- 且 `ILogger<T>` 在本层是**既有形状**，不是本轮新开的口子：`NotificationRetryDrainService`
  （同一个 `Notifications/` 目录）、`CommandLoggingBehavior`、`MonsterDropService` 都已如此注入。

**判定：本轮没有推翻上一轮成立过的任何一条。**

### 2. 日志记在对的地方、说的是对的事吗

**(a) 超时与抛异常确实分开了——成立，且超时那条确实拿不到异常。**
`AttemptAsync` 的 `catch (Exception error)` 内以 `deadline.IsCancellationRequested` 分叉：
真分支 `logger.LogWarning("...ran out of its {Timeout}...", CharacterId, PerRecipientTimeout)`
——**不带 `error`**；假分支 `logger.LogWarning(error, "...failed.", CharacterId)`——带异常。
调用方自身取消在前一个 `catch ... when (cancellationToken.IsCancellationRequested) throw;`
被放行，不会落进来。判据两侧都钉住了：超时那条断言 `Assert.Null(entry.Error)`，
失败那条断言 `Assert.IsType<IOException>(entry.Error)`。分叉的**精度**有一处窄边界，见 N-11。

**(b) `Requeue` 只在 `RetryLimit > 0` 时记——理由成立，且 `retryLimit: 0` 不丢可观测性。**
我不采信自陈，走了因果链：`Requeue` 只在 `outcome == Failed` 时被调用（`:71-73`、`:164-165`），
而 `Failed` **只能**从 `AttemptAsync` 的 catch 产生，那个 catch 的**两个分支都必然记一条 Warning**。
所以每一次失败在到达 `Requeue` 之前就已经被说出来了；`retryLimit: 0` 的通知（即清理那条、
C-D8 所在的那条路径）**不存在无声的失败**。这不是我的推理孤证——
`A_failed_delivery_is_said_out_loud` 用的正是 `new NotificationWorld()`（默认 `retryLimit: 0`），
它断言的就是「retryLimit 为 0 时失败仍被记下来」。
去掉这个条件的代价我也实测了（M2）：**同一个事件会被记两条**。

**(c) 弃权与「人不在了」不记——恰当。**
`Declined` 是处理器**主动**决定这个人不该收（`ComposeAsync` 返回空），`Gone` 是玩家正常下线或重连，
两者都在 try 内**提前 return**、根本不进 catch。把它们记成 `Warning` 会让日志在每次有人正常离开时
报警，也会淹掉真正的失败。代码注释与 `AGENTS.md` 给的都是这个理由（「它们不是出了问题」），
与代码一致。

### 3. 三条新判据有没有牙——变异实测

变异前 `git hash-object NotificationBus.cs` = `9103848b5d7eeadfc3644195dc914fff88abb693`。
每次变异后跑
`dotnet test Server/tests/Dfo.Server.Application.Tests/... --filter "FullyQualifiedName~Notifications"`（55 条）。

| 变异 | 结果 | 红的是谁 / 怎么红的 |
|---|---|---|
| **M1** 删掉超时那条 `LogWarning` | **红 1 / 通过 54** | `A_recipient_who_ran_out_of_time_is_named_as_that_and_not_as_an_error`：`Assert.Single() Failure: The collection did not contain any matching items, Collection: []`。失败那条**不受影响**——两条判据确实各咬各的。 |
| **M2** 把 `if (registration.RetryLimit > 0)` 改成恒真 | **红 1 / 通过 54** | `A_failed_delivery_is_said_out_loud`：`Assert.Single() Failure: The collection contained 2 matching items`，两条分别是 `"Notification for character 7001 failed."` 与 `"Notification TownBellRang gave up on character 700…"`。**与执行方自陈当场撞见的失败逐字一致。** |
| **M3** 从失败那条 `LogWarning` 里去掉异常参数 | **红 1 / 通过 54** | `A_failed_delivery_is_said_out_loud`：`Assert.IsType() Failure: Value is null, Expected: typeof(System.IO.IOException)`。 |
| **M4**（我自己加的反向变异）把条件收紧成 `RetryLimit > 1` | **红 1 / 通过 54** | `Giving_up_on_a_recipient_is_said_out_loud`（世界是 `retryLimit: 1`）：`Assert.Single() ... did not contain any matching items`，集合里只剩两条 `failed`。 |

M2 与 M4 一起说明**这个条件被两侧夹住**：放宽会红，收紧也会红——它不是一个可以随手漂移的字面量。
**三条新判据都有牙，且是各自独立的牙。**

**还原核对**：`cp` 回原件后 `git hash-object` = `9103848b5d7eeadfc3644195dc914fff88abb693`，
与变异前**逐字节相同**；`git status --porcelain` 仍 21 项（15 M + 6 ??）。
随后做的清洁重建与两趟全量判据，用的都是还原后的源码。

### 4. `AGENTS.md` 改后那段与代码逐句核对

原来那条「已知限制：通知投递失败今天是无声的」在文件里已**全无残留**
（`grep -n "无声\|已知限制" AGENTS.md` 为空）。新段（`AGENTS.md:475-486`）逐句：

| 句 | 代码 | 判定 |
|---|---|---|
| 「摘要（成功 / 弃权 / 人不在了 / 失败）」 | `NotificationOutcome` 恰 4 个成员 | ✓ |
| 「读摘要是可选的，今天没有一个调用方读它」 | 两个 `PublishAsync` 调用点（`SelectCharacterHandler.cs:291`、`SupplementalSynchronizationMessages.cs:255`）的返回值均未被接收；全仓生产 `PublishAsync` 只有这两处 | ✓ |
| 「投递抛异常 → `Warning` + 异常本身」 | `logger.LogWarning(error, "...failed.", ...)` | ✓ |
| 「到了每收件人期限 → `Warning`，与抛异常**分开说**」 | `deadline.IsCancellationRequested` 分叉，两条不同消息 | ✓ |
| 「（那时手上没有异常）」 | 那时 `error` **在作用域内**，只是它是一个不说明原因的 `OperationCanceledException`——代码是**选择不记它**，不是记不了 | **不准，见 N-10** |
| 「重试额度用尽、彻底放弃某收件人 → `Warning`」 | `Requeue` 的 else 路径 | ✓ |
| 「声明 0 次重试的通知不记这一条，因为它从来就没有第二次机会可放弃，失败那一条已经说过了」 | `if (registration.RetryLimit > 0)`；且失败必已在 `AttemptAsync` 记过（上文 2(b) 的因果链） | ✓，理由属实 |
| 「弃权与『人不在了』不记——它们不是出了问题」 | 两者在 try 内提前 return | ✓ |

**没有一句把保证说得比代码大**（唯一那处不准是把理由说岔了，方向是**低估**代码手上有什么，
不是高估它做到了什么）。文件 634 行、`+105 / −1`，删除行仍为最初那 1 条，0 CR，无 BOM。

### 5. 计数核对——自己跑，逐项目相加

环境 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。
**没有用 `grep -c` 数行**（detailed logger 并发写会粘行），用的是逐项目读 `通过数:` 再相加。

清洁重建：`dotnet build Server/86JP.Server.slnx --no-incremental -v:n` → `BUILD_EXIT=0`，
「0 个警告 0 个错误」，全日志 `(warning|error) [A-Z]+[0-9]+` 命中 **0**，
17 个程序集、55 次 `CoreCompile`（确实是整解重编，不是跳过）。**TreatWarningsAsErrors / CS9113 无碍**——
`logger` 被实际使用。

`dotnet test Server/86JP.Server.slnx --nologo --logger "console;verbosity=detailed"`，**跑了两趟**：

| 程序集 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Networking.Tests | 52 | 0 | 0 |
| Protocol.Tests | 517 | 0 | 0 |
| Pvf.Tests | 60 | 0 | 0 |
| Persistence.Tests | 256 | 0 | 0 |
| Infrastructure.Tests | 223 | **2** | 0 |
| Application.Tests | **1015** | 0 | 0 |
| Host.Tests | 6 | 0 | 0 |
| **合计** | **2129** | **2** | **0** |

52+517+60+256+223+1015+6 = **2129**。两趟同值，`EXIT=0`，七个程序集各自「测试运行成功。」，
全日志 `失败` 命中 **0**、`warning|警告` 命中 **0**。**与判据 2126 → 2129 / 2 / 0 逐值相符**
（Application.Tests 1012 → 1015，恰 +3，与「新增三条」相符，没有谁被顺手删掉换名额）。
**未出现任何失败，故无需动用 `AGENTS.md` 的三条栈根表。** 两条跳过仍是
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
（Infrastructure.Tests，档案已加载的标志）。三条新判据在日志里逐条 `已通过`，我按名核过。

### 6. 回归——既有断言有没有被削弱或删除

- `git diff -- Server/tests`：仍是**同样的 4 个已跟踪文件**，`^-.*Assert` 共 **12** 行、
  `^+.*Assert` 共 **17** 行，**与上一轮 approved 时逐行相同**（这四个文件 mtime 17:03，本轮未动）。
  上一轮已逐条裁定过的那 12 行（`BestEffortTargets` 换形、`"TownDepartureMessage"` 以更强形式补回），
  本轮没有新增删除。
- 未跟踪的 `Notifications/` 与 `CleanupNotificationDeliveryTests.cs` 无法 diff，故我改用**点名核对**：
  上一轮验证逐条读过断言的 17 条判据（`A_recipient_whose_delivery_fails_does_not_stop_the_others`、
  `A_retry_asks_the_handler_again_rather_than_resending_what_it_had`、
  `Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed`、
  `A_recipient_who_runs_out_of_time_is_a_failure` 等）**逐个 grep，17/17 全部仍在**，
  且在本轮判据日志里逐条 `已通过`。加上 Application.Tests 净增恰为 +3，
  **可以判定：没有判据被删除、改名或换成更弱的写法。**
- `NotificationWorld.cs` 的改动是**纯增量**：`RecordingLoggerProvider` + `Log` 属性 +
  `services.AddLogging(builder => builder.AddProvider(Log))`。`AddLogging` 是解析
  `ILogger<NotificationBus>` 的必要条件，不改变任何既有替身的行为（世界里只有总线会记日志；
  排空仍由手工驱动，`RetryInterval` 仍是 `Timeout.InfiniteTimeSpan`）。

### 追加的 Non-blocking Observations（承上一轮 N-1..N-9 编号）

- **N-10｜`AGENTS.md` 的「（那时手上没有异常）」把理由说岔了。**
  超时进的是 `catch (Exception error)`，`error` 就在手上——它是一个
  `OperationCanceledException`，**不说明原因**，所以代码**选择不记它**（判据
  `A_recipient_who_ran_out_of_time_is_named_as_that_and_not_as_an_error` 断言 `Assert.Null(entry.Error)`
  钉的正是这个选择）。代码自己的注释（`NotificationBus.cs:220-226`）反而说准了：
  「the two are told apart in what is logged rather than merged into one word」。
  建议把括号改成「那个异常只是取消，不带原因，记它等于记一句废话」。
  这是本成果第四次栽在同一族毛病上（前三次：`13→14`、`两个→四个`、`four` 下有五个 `[InlineData]`），
  但方向与前三次相反：这次是**低估**代码手上有什么，不是高估它做到了什么。
  落在 SC-12 的射程内，故写在明处。

- **N-11｜超时/异常的分叉判据在一个窄边界上会把异常吞掉。**
  分叉用的是 `deadline.IsCancellationRequested`，不是「这个异常是不是取消」。于是：处理器或传输
  **忽略了 token**、跑过 2 秒之后才抛出一个真异常（例如 socket 死掉的 `IOException`），
  会走进超时分支，日志只说「ran out of its 00:00:02」，**异常本身丢掉**。
  同族的还有：调用方 token 已取消、同时抛出的是非取消异常时，也会被说成超时。
  两者都窄（正常传输对着被取消的 token 抛的是 `OperationCanceledException`），
  且「期限确实到了」这句话本身不假，所以不构成阻断。更精确的写法是
  `error is OperationCanceledException && deadline.IsCancellationRequested`，否则落 else 带异常记。
  **这也正是 N-10 那句话遮住的东西**——认定「手上没有异常」，就不会去想「万一手上那个不是取消呢」。

- **N-12｜`RetryLimit > 0` 这个条件的牙长在一条名字不提它的判据上。**
  夹住这个条件的是 `A_failed_delivery_is_said_out_loud` 的 `Assert.Single`（M2 实测），
  而那条判据的名字与 `<remarks>` 讲的是「失败要被说出来」，一个字没提「而且只说一次」。
  日后有人为别的正当理由再加一条提到该角色的日志，会撞红这条判据，
  而红出来的信息与它的名字对不上。建议在其 `<remarks>` 补一句「用 `Single` 而非 `Contains`：
  一次失败只该被说一次」。断言本身是对的，不必改。

- **N-13｜`TASK-06` 的 C-D8 条目现在读起来比事实悲观，而 `AGENTS.md` 明文把读者指向它。**
  `TASK-06:622-627` 写「映射失败从『抛给调用点』变成『**彻底无声**』…… 总线**没有 logger** ……
  **没有任何地方会说出来**」。这三句今天都已不成立：总线有 logger，该异常会带着异常对象记成 `Warning`。
  条目的**核心差异**（抛给调用点 → 被吞成 `Failed`、不重试、无人读摘要）仍然成立，
  代码窗口也仍然指得到，所以 SC-10 的清单**没有变得不完整**——变的是它对严重性的定性。
  之所以值得记：`AGENTS.md` 明写「完整差异清单在 TASK-05 / TASK-06」，
  这就把这两份轮次记录变成了**在用的参考材料**，不只是历史。
  同族的还有 `TASK-06:707-708`、`TASK-07:57 / 87 / 170 / 511 / 801 / 1152 / 1217 / 1430 / 1535`
  与上一轮的 `reviews/change-review.md` N-3、`verification/completion-evidence.md` 的第 4 件。
  **我的建议是不要回改这些轮次记录**（那是在改历史，且本仓的规矩就是逐轮留档），
  而是在 C-D8 下加一行前向注记：「后续：所有者裁定加日志，本条的『无声』部分已不再成立，
  见 `spec-result.md` 的 `## Post-verification Change`」。执行方本轮改了 `AGENTS.md` 却没管这条
  被 `AGENTS.md` 指过去的记录，是本次改动自身记账的一处不完整。

- **N-14｜上一轮 N-3 的状态更新。**
  上一轮 N-3 记的是「`Failed` / `Gone` / 达上限丢弃三种结局在生产里完全没有出口」。
  本轮 `Failed` 与「达上限丢弃」都拿到了 `Warning`，**只剩 `Gone` 没有任何记录**。
  这是**有意的**（见 2(c)，它不是出问题），我判定处置正确；此处仅为把上一轮 N-3 的状态补完整，
  以免日后有人拿旧的 N-3 当现状。

### Verdict

**approved。**

这次改动**没有推翻上一轮成立过的任何一条**：SC-4 的扫描面不含总线（`INotificationHandler<>` 实现者
且限 Protocol 程序集，两个条件总线都不满足），SC-8 的摘要形状文件本轮未动，
SC-10 的帧与收件人语义一个未改，SC-11 的判据文件对基线**哈希仍逐字节相同**。
`ILogger<NotificationBus>` 不触碰任何注入面规则——**处理器 / 总线这个区分我逐条核过，成立**，
且 `ILogger<T>` 在本层是既有形状（同目录的 `NotificationRetryDrainService` 已如此）。

日志记在了对的地方：超时与抛异常确实分开，超时那条确实不带异常（由判据两侧钉住）；
`RetryLimit > 0` 的取舍成立且**不让 `retryLimit: 0` 的通知丢失可观测性**——
因为每一个 `Failed` 在到达 `Requeue` 之前必已被记过一条，这条因果链我自己走过，
而且正是 `A_failed_delivery_is_said_out_loud`（世界即 `retryLimit: 0`）在钉它；
弃权与「人不在了」不记是对的。

三条新判据**都有牙**：四次变异（删超时日志 / 放宽条件 / 去掉异常参数 / 收紧条件）
各红一条且各不相同，条件被两侧夹住；变异后源码**逐字节还原并核过哈希**。
清洁重建 0 警告 0 错误（55 次 `CoreCompile`），全量判据两趟同值、逐项目相加
**2129 / 2 / 0**、退出码 0、0 警告，与判据逐值相符；Application.Tests 恰 +3。
既有断言无一被削弱或删除（4 个已跟踪判据文件本轮未动，17 条点名判据 100% 仍在且通过）。

`AGENTS.md` 那段与代码逐句一致，**没有一句比代码强**；唯一不准的是一句括号里的理由（N-10），
方向是低估代码手上有什么。

新增 N-10..N-14 五条 non-blocking 观察，均不影响本次接受。其中 **N-13 与 N-11 值得优先看一眼**：
前者是本次改动自身记账的一处不完整（`AGENTS.md` 指向的 C-D8 条目现在比事实悲观），
后者是这次分叉判据的一个窄边界（可能把真异常说成超时并丢掉它）——两者由同一处认知失误相连。

上一轮「必须由所有者裁定的四件」中，第 1 件（TASK-06 `Permitted files` 事后扩张）与第 4 件
（可观测性）已由所有者裁定并落地，本节确认第 4 件的落地是对的；第 2、3 件**照原样悬着**，
本次评审未改变其状态，也**不代为决定**。
