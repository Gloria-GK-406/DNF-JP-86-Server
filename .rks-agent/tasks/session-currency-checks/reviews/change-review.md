---
kind: rks-review
target: b97e6662..97c5af5d（基线 ea6a1544；规划提交 b97e6662；实现提交 97c5af5d；plan.md「执行偏差」「执行后的定稿」以工作区未提交版本为准）
verdict: changes_required
reviewer: 独立审查子代理（fresh context，未参与实现）
---

# 变更审查：会话当前性收成具名判定

## 范围

- 代码：`git diff b97e6662..97c5af5d`，共 262 个文件（生产约 170 个，测试约 90 个，`AGENTS.md`）。
- 依据：`TaskContract.md`（revision 2）、`spec.md`（RQ-001～RQ-018、AC-001～AC-008）、`plan.md`（含工作区里的「执行偏差」「执行后的定稿」）、仓库 `AGENTS.md`。
- 审查方式：`reviewing-work` 的 Code 路线（完整变更）。文档改动按 Deliverable 口径核对与代码是否一致。
- 不在本轮范围：SC-6 issue 是否已创建；AC-006 全量测试的数量与断言说明（属于完成验证）。

## 依据与取证

- 逐段读了 `OnlineStateRegistry` 的新判定、接口与门面，以及 Application、Protocol 两层的全部生产差异。
- 用脚本列出旧代码里「key 不取自比较对象本身」的全部 `Find…(…) ==/!=` 比较（约 50 处），逐处核对替换后是否丢了「key 属于这个会话」的断言。
- 检查被删除条件里的 `OnlineUserId`、`Generation`、`ReferenceEquals`。
- 检查拆除服务 `SelectedSessionCleanupService.CleanupAsync` 调到的每一个下游。
- 检查选角工作流在 `EnterWorld` 之前的阶段命令。
- 用工作树文件逐个检查换行；对两个混合换行的文件，与基线版本做了对比。
- 定向测试：`dotnet test Server/tests/Dfo.Server.Application.Tests/... --filter SessionCurrencyChecksTests|NotificationInWorldTests|SelectionWorkflowCurrencyTests|NpcShopPurchaseTests|OtherCharacterDetailQueryDomainServiceTests|NotificationDeliveryTests|CreatureRuntimeDomainServiceTests`，74 条全部通过。
- `verification/logs/impl-build-test.txt` 记录 Application.Tests 有 2 条失败（`DimensionDungeonTests`，迷宫内容问题），其余项目全绿。

## 按重点逐条的发现

### 1. 保底规则（RQ-012）

**判定实现本身没有放宽。**
- `OnlineStateRegistry.IsSelected`（`Users/OnlineStateRegistry.cs:242`）等于 `FindExact && !IsRevoked`。
- `InWorld`（`:273`）就是原来 `IsActiveExact` 的方法体。
- `TryGetInWorld` 三个重载与 `FindBy…` 一样，在同一把锁内用 `SingleOrDefault` 找记录，再用 `InWorld` 判断。

**key 来自别处的比较：抽查的约 50 处都没有丢断言。**
- 绝大多数情况，key 按构造就等于会话字段。例如会话来自 `FindByConnection(context.Connection.ConnectionId)`（CharacterDeath、GiveUp、Eplp、Altar、Recover、Joust、Mailbox、Lottery、Vending 等），或者命令的 `AccountId` / `CharacterId` 本来就是 `Session.*` 的派生属性（`IncreaseStatusCommand`、`InitializeHousingCommand`、`EnsureDailyContractPuppetsCommand`）。
- key 真正来自别处的地方，断言都已显式写出：
  - `TownPositionEffectHandler.cs:401`：`context.Session!.AccountId == context.AccountId && IsInWorld(...)`
  - `DungeonLoadingProjectionDelivery.cs:151`：`session.ConnectionId == connectionId && IsInWorld(session)`
  - `ClientSettingsDomainService.cs:93`：`session.ConnectionId == origin.Connection.ConnectionId && IsSelected(session)`
  - `ChurchRooms.cs:44`、`ExperienceBonusDomainService.cs` 都保留了 `AccountId` 比对。

**更严格的条件都保留了。**
- `OnlineUserId`：`SortItemCommand`、`SortLockCommand`、`MailboxCommands`、`RecoverStaminaSlice` 都改成 `online?.OnlineUserId != command.OnlineUserId`，`null` 时同样拒绝。
- 代数：`ClientSettingsDomainService.IsCurrent` 保留了 `user.Generation == origin.Generation`。
- 连接对象：`JoustSlice` 保留了 `ReferenceEquals(recipient.Connection, c.Connection)`。
- 被删除而没有替换的检查只有 `NpcShopDomainService.IsCurrent` 这个 helper，调用处已经改成 `IsInWorld`。

**清理路径：主体正确，但有一处下游误用 `IsInWorld`，见必须修复项 1。**
- 以下三处改成 `StandingOf(...) is not null`，与原语义等价，接受已撤销的记录：
  - `SelectCharacterLifecycleServices.cs:59` 的 `ownsSelectedCharacter`
  - `OnlineUserConnectionObserver.cs:138`
  - `ReturnToSelectionCommand.cs:67`

### 2. 归级（RQ-011）

- 留在 `IsSelected` 的 5 处与 plan 的定稿表一致，都位于工作流里，或位于工作流与玩法共用的方法内：
  - `SelectCharacterHandler.cs:542`
  - `EnsureDailyContractPuppetsCommand.cs:21`
  - `HousingSlice.cs:149`
  - `SelectCharacterLifecycleServices.cs:384`（`Publish`）
  - `ClientSettingsDomainService.cs:93`
- `EnterWorld` 之前运行的阶段（Prepare、CleanupExpiredCharacterItems、EnsureDailyContractPuppets、InitializeHousing、PrepareSelectedCharacterState、ApplySelectedCharacterDailyRefill）都没有被改成 `IsInWorld`。`EnterWorld` 在 `PublishSelectedCharacterSessionCommandHandler`（`SelectCharacterPhaseCommands.cs:244`）里先于 `Publish` 执行，所以之后的门票补充、PvP、任务、宠物、投影阶段问 `IsInWorld` 不会让选角中途失败。
- 没有发现留在 `IsSelected` 上的玩法路径。

### 3. RQ-014a：旁人的查找

- `PlanPvpAsync`（`SelectCharacterLifecycleServices.cs:333-341`）从 `?? throw` 改成跳过，符合要求。
- `PlanTownPublicationAsync`、拆除时的同城旁人，以及队伍、塔、副本成员的查找，原来就是 `continue` 或拒绝，收紧后没有新增抛异常的路径。
- `WeddingSettlement.cs:60` 的 `FindByCharacter ?? throw` 没有改，不会因为收紧而新增失败。
- 剩一处间接风险：两塔提交前对旁人的检查，见非阻断风险 1。

### 4. 投递（RQ-015）

- `NotificationAudienceResolver`：精确会话人群用 `IsInWorld(exact)`，角色人群和范围人群用 `TryGetInWorld`。
- `NotificationBus.IsStillHere`：精确会话人群用 `IsInWorld(exact)`，角色人群用 `TryGetInWorld` 加 `ConnectionId` 比对。
- 以上与 RQ-015 一致。
- 拆除通告里发给离开者本人的那一半，`CleanupNotificationDelivery.DeliverAsync`（`SupplementalSynchronizationMessages.cs:252-260`）仍按 `plan.OwnerConnectionId` 直接 `SendExplicitAsync`，不经过人群，照常送达。
- 同城旁人只有在 `Publish` 之后才进入城镇区域，而 `Publish` 在 `EnterWorld` 之后。所以拆除通告的旁人人群从 `FindByCharacter` 改成 `TryGetInWorld`，不会漏掉一个「已在区域里但尚未入世」的旁人。

### 5. 测试

- **AC-002**：`SessionCurrencyChecksTests` 覆盖了选中、在世界、隔离、撤销（入世前和入世后两种）、被替换、已释放、换代，三个 `TryGetInWorld` 重载都有断言。满足。
- **AC-004**：
  - 玩法写操作：`NpcShopPurchaseTests.A_purchase_after_the_connection_is_revoked_is_refused_before_teardown`。
  - 跨账号目标：`OtherCharacterDetailQueryDomainServiceTests.ATargetStillEnteringTheWorldIsNotCurrent`。
  - 投影：`NotificationInWorldTests` 的撤销用例。
  - 选角工作流：测的是命令处理器 `InitializeHousingCommandHandler`，不是 spec 字面要求的「领域服务」，见非阻断风险 2。
- **AC-005**：
  - 精确会话人群撤销后不投递：`A_revoked_exact_session_is_not_in_an_exact_audience`，以及解析后才撤销的 `A_connection_revoked_after_resolution_is_gone_at_delivery`。
  - 角色人群连接不变时照常投递、尚未入世时不投递：有新用例。
  - 角色人群解析后连接变化不投递：由既有的 `NotificationDeliveryTests.Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed` 覆盖。
  - 满足。
- **夹具**：原来凭空 `Select` 一个会话、不登记连接的写法，改成 `TestSessions.InWorld` / `Selected` / `EnterWorldWithConnection` / `ReselectOnSameConnection`，代数取认证后的连接代数，替换前先 `ReleaseExact`。这与生产流程一致（Prepare 先清理旧会话，`EnterWorld` 要求代数匹配）。
- **断言**：没有发现为变绿而削弱的断言。被删除的断言只有 `IsActiveExact` 的 6 处，都换成了 `IsInWorld`。其中 `OnlineIdentityTests.cs:122-123` 原来断言撤销后 `IsInWorld` 仍为真，现在改成假，并补了 `StandingOf` 不为空，这正是本次有意的语义变更。
- **测试替身（RQ-010）**：`IsSelected` 都按替身自己的 `StandingOf` 回答（`ConnectionRevoked: false`），`TryGetInWorld` 用替身自己的 `FindBy…` 加 `IsInWorld`。`AwkwardSessions` 改为委托真实目录。都没有恒真或恒假。

### 6. 代码质量

- 改法与周围代码一致，新增注释只写代码说不出的约束：例如 `PlanPvpAsync` 为何跳过、`ReturnToSelection` 为何问 `StandingOf`、`MarriageEligibility.IsCurrent` 为何保留。
- 换行：没有整文件改写（最大的生产改动是 `SelectCharacterLifecycleServices.cs`，35 行）。`ResetItemQualitySlice.cs`（134 行 CRLF / 5 行 LF）和 `TitleBookSlice.cs`（247 / 1）在基线里就是这样的混合换行，本次保持原样。其余都是纯 LF 或纯 CRLF。
- 小遗留：见非阻断风险 4～6。

### 7. 文档

- `AGENTS.md` 新增段落对三个判定的描述与代码一致：都不看隔离标记，清理路径问 `StandingOf` / `ReleaseExact`。
- 但 `AGENTS.md:269` 写「`Find…` 只用于两种情况」，与代码不符，见必须修复项 2。

## 必须修复项

### 1. 宠物运行时在清理路径上不再接受已撤销的会话（违反 RQ-011 的清理规则）

**位置**
- `Server/src/Dfo.Server.Application/Creatures/CreatureRuntimeDomainService.cs:138`：`AdvanceCoreAsync` 开头 `if (!sessions.IsInWorld(session)) return [];`，原来是 `FindByCharacter(...) != session`。
- 同文件 `:288`：`InstanceRuntime.EnsureActive` 改成 `IsInWorld`。

**调用链**
拆除服务 `SelectCharacterLifecycleServices.cs:122` 在 `ownsSelectedCharacter` 为真时（`StandingOf`，接受已撤销）调用 `creatures.LeaveAsync(session)`，后者（`:250`）进入 `AdvanceCoreAsync`。

**后果**
- 会话的连接已被撤销时，`LeaveAsync` 在 `AdvanceCoreAsync` 第一行就直接返回，宠物最后一段时间的饥饿结算不会落库。
- `roomRewards` 队列里尚未发放的房间经验也不会发放：队列在 `:186` 读取，在 `:363` 随提交落库；之后 `DiscardExact` / `Enter` 会清空这个队列，经验随之丢失。
- 改动前，`FindByCharacter == session` 对已撤销的记录为真，这些结算照常执行。
- 当前生产中的撤销来源是协议拒绝：`GameLoginHandler.RejectAsync` 先 `TryDisconnectConnection`，之后由 `OnlineUserConnectionObserver.cs:82` 的 orphaned 分支执行拆除。普通断线时，拆除在 `TryDisconnectConnection`（`:114`）之前完成，不受影响。因此频率低，但这是 spec 明确要求「与今天一样接受连接已撤销的记录」的清理路径上一次未授权的收紧，现有测试也没有覆盖。

**最小修正**
离场结算这一条路径（`LeaveAsync` → `AdvanceCoreAsync`，以及其中经 `CreateContext` 创建的 `InstanceRuntime.EnsureActive`）按清理口径判断，即 `StandingOf(session) is not null`；玩法入口（`AdvanceAsync`、`HatchAsync`、`GainExperienceAsync`）保持 `IsInWorld`。补一条用例：连接撤销后执行 `LeaveAsync`，饥饿值与已排队的房间经验仍然落库。

### 2. AC-001 / SC-1 未达成，且 `AGENTS.md` 写入了与代码不符的规则

**位置**
- `AGENTS.md:269`：「`Find…` 只用于两种情况：请求方按自己这条连接解析会话，以及清理路径取出要释放的会话。」
- `verification/logs/ac001-scan.txt` 的 E3 段：21 处「其余 Find」只有清单、没有说明。

**问题**
AC-001 要求剩余的会话 `Find…` 逐条归入 (a) 或 (b)；spec 另外只允许 (c)，即 `ClientSettingsDomainService.cs:94`。E3 里的其余 20 处多数正是 RQ-005、RQ-006 要改的当前性判定，例如：
- 查出来再按字段比较：`NameTagExpiredSubscriber.cs:15-17`、`ExpiredItemsProjection.cs:27-29`、`AccountWalletChangedHandler.cs:14-16`。
- 查不到就当不在线：`RentalSlice.cs:164`（TaskContract 点名的例子）、`TournamentSlice.cs:173`、`MarriageLovePointSlice.cs:62`、`BossTowerPartyCoordination.cs:353` / `IllusionTowerPartyCoordination.cs:351` 的 `ExactSessions`、`TournamentDungeonDomainService.cs:63`、`GmCeraDomainService.cs:21` 等。

此外检索有两处盲区，没有出现在任何一类里：
- `SelectCharacterLifecycleServices.cs:242`：方法组写法 `.Select(sessions.FindByCharacter)`。它在 `PartyNotificationSlice` 里的同构代码已经改成 `TryGetInWorld`，这里没有改。
- `MarriageEligibility.SessionOf`（`MarriageEligibility.cs:20`）把 `FindByCharacter` 包了一层，调用方 `MarriageDomainService.Couple.cs:148` 写的是 `actor != original`，实质上就是「查出来再比较」。

**后果**
这些地方都不比原来宽，下游投递也大多已被解析器的 `IsInWorld` 兜住，所以不是行为回归。但验收条款没有满足，而 `AGENTS.md` 作为现状图写下了一条假规则；按本仓约定，下一个读者会据此误判。

**最小修正（二选一）**
- 逐条按 RQ-005、RQ-006 改写，收紧处补测试或说明；
- 或者在 spec / plan 里为确有理由保留的查找增加一个具名类别并逐条写明理由（例如按账号查找，目前没有 `TryGetInWorld(AccountId)`），同时把 `AGENTS.md:269` 改成与代码一致。

两种做法都要把上述两处检索盲区纳入归类。

## 非阻断风险

1. **两塔在提交前检查旁人。** `BossTowerDomainService.cs:394`、`IllusionTowerDomainService.cs:318` 的 `AuthenticatedSessions.Any(x => !owner.ValidSession(x))` 现在用 `IsInWorld`。拆除时离塔走 `leaving: true`，但其他成员在变更时被加入了这个集合。如果其他成员的连接在变更与提交之间被撤销，离开者的拆除离塔会抛异常，被拆除服务汇总为失败。原来只有替换或释放会触发这种情况；现在撤销也会。窗口很窄，而且当前撤销只来自协议拒绝，所以记为风险。
2. **AC-004 选角工作流一条测的是命令处理器。** `SelectionWorkflowCurrencyTests` 测的是 `InitializeHousingCommandHandler`。spec 字面要求领域服务；`SelectCharacterSessionEntryService.Publish` 与 `ClientSettingsDomainService.IsCurrent` 拒绝已撤销会话的行为没有直接测试。
3. **定时任务的 `catch` 过滤器吞得更多。** `CharacterNameTagExpiration.cs:47`、`OnlineExpiredItemCleanup.cs:69`，以及 Boss、Southern、Tau 三个 DailyRefill 的 `catch` 里，过滤条件从 `Find != session` 改成 `!IsInWorld(session)`。连接已撤销的会话失败后会被静默跳过，不再计入失败汇总。语义上说得通，但 plan 的 RQ-014 表没有写。
4. **未用参数。** `OnlineUserConnectionObserver.CleanupSelectedHeldAsync` 的 `connectionId` 参数（`:133`）在改动后已不再使用。
5. **被蕴含的条件没删干净。** `PartyMemberSessionEndpointSource.cs:17-21` 在 `TryGetInWorld` 之后仍保留 `onlineUser.AccountId` / `CurrentCharacterId` 比对，这已经被 `TryGetInWorld` 蕴含（RQ-008 没有完全执行）。没有行为影响。
6. **重复断言。** `OnlineIdentityTests.cs:85-86` 把同一句 `Assert.True(identity.Sessions.IsInWorld(session))` 写了两遍。
7. **测试替身代码大量复制。** 同一个 27 行的替身成员块复制到了约 40 个测试替身里。这与既有 `IsInWorld` 替身的写法一致，但以后再改接口的成本会继续累积。
8. **「每处收紧都要有测试证据」只覆盖了样本。** Constraints 的这条要求只由 AC-004 的样本覆盖；plan 的「执行后的定稿」把其余收紧归为「没有合法调用方受影响」，依据是同一连接的报文按顺序处理。这一依据对请求方自己的连接成立，但对跨账号、定时任务和事件订阅者，只有列表，没有逐项证据。

## 结论

`changes_required`。

- **做对了的部分：**
  - 判定实现、归级、key 断言、更严格条件的保留、投递语义和测试夹具，都经得起核对。
  - 没有发现任何调用点的接受范围比原来宽。
- **需要修复的两项：**
  - 宠物离场结算在清理路径上被 `IsInWorld` 拦截，属于未授权的收紧，会丢失宠物饥饿结算和房间经验。
  - AC-001 的归类没有完成（E3 的 20 处，外加两处检索盲区），`AGENTS.md:269` 因此写下了一条与代码不符的规则。

两项修正都很小。修正后需要重新独立审查受影响的文件，并补跑 `CreatureRuntimeDomainServiceTests`。

## 修复复审

```yaml
kind: rks-review
target: 97c5af5d..7b55d4bf（修复提交 7b55d4bf；spec.md / plan.md 以工作区未提交版本为准）
verdict: changes_required
reviewer: 独立审查子代理（fresh context，未参与实现与修复）
```

### 范围与取证

- 代码：`git diff 97c5af5d..HEAD -- Server AGENTS.md`，17 个文件。任务文件：`git diff -- .rks-agent`。
- 依据：上一轮的两个必须修复项与 8 个非阻断风险；`spec.md` 新增的保留类别 (d)(e)(f) 与 RQ-009 例外；`plan.md` 的「执行偏差」「执行后的定稿」。
- 审查方式：`reviewing-work` 的 Code 路线，只复审上一轮未决的发现及其修复直接带来的回归；上一轮已核对且本次未改动的部分沿用上一轮结论。
- 检索：在 HEAD 上重跑 `ac001.py Server/src`，E3 段为 18 处，逐处读了上下文。另用 grep 补查方法组写法（`.Select(x.FindBy…)`）以及返回 `SelectedSessionIdentity?` 的包装方法。
- 定向测试：
  - `Dfo.Server.Application.Tests`，过滤 Creature / Marriage / Wedding / DungeonAdmission / PartyMemberSession / ConnectionObserver / Tournament / CycleDungeon / SessionCurrency / OnlineIdentity / ExpiredItems / NameTag / Rental / Wallet：300 条通过，3 条按设计跳过（真实 PVF）。
  - `Dfo.Server.Protocol.Tests`，过滤 Marriage / Observer / Wedding：82 条全部通过。
- 探针：在 `CreatureRuntimeDomainServiceTests` 里临时加了一条用例，跑完已用 `git checkout` 还原，工作区没有残留。用例内容：排队一条房间经验 → 撤销连接 → `LeaveAsync`。结果见下面 1.3。

### 1. 必须修复项 1（宠物离场结算）：只修了一半，仍未关闭

**1.1 `leaving` 标记的传递：直接路径已贯通，房间经验那条支路没有带上。**
- 已贯通：
  - `LeaveAsync` → `AdvanceCoreAsync(..., leaving: true)`：入口按 `StandingOf(session) is null` 判断，与基线 `FindByCharacter(...) != session` 接受的范围一致。
  - `CreateContext(tx, ..., leaving)` → `InstanceRuntime.EnsureActive` 同样按 `StandingOf` 判断。
  - `InstanceRuntime.SynchronizeAsync` 把 `leaving` 继续往下传。
- 没贯通：`AdvanceCoreAsync` 发放已排队房间经验时调 `GainCoreAsync`（`CreatureRuntimeDomainService.cs:225-233`），它内部执行 `CreateContext(tx, session.CharacterId)`，没有传 `leaving`。这个上下文的 `InstanceRuntime.EnsureActive` 因此仍按 `IsInWorld` 判断。基线这里是 `owner.IsCurrent(session)`，即 `FindByCharacter == session`，接受连接已撤销的记录。

**1.2 玩法入口：仍然要求 `IsInWorld`，符合要求。**
`AdvanceAsync`、`HatchAsync`、`GainExperienceAsync`，以及 `Of(...)` 和 `GainCoreAsync` 创建的上下文，都按 `IsInWorld` 判断。

**1.3 后果（探针已证实）**

探针抛出的异常：

```
System.InvalidOperationException : The creature's selected session has changed.
  at InstanceRuntime.EnsureActive()                      CreatureRuntimeDomainService.cs:291
  at CreatureInstanceContext.FindAsync                   CreatureInstanceContext.cs:55
  at CreatureRuntimeDomainService.GainCoreAsync          CreatureRuntimeDomainService.cs:231
  at CreatureRuntimeDomainService.AdvanceCoreAsync       CreatureRuntimeDomainService.cs:196
  at CreatureRuntimeDomainService.LeaveAsync             CreatureRuntimeDomainService.cs:257
```

只要离场时还有已排队、未发放的房间经验，`LeaveAsync` 就会抛异常，整个事务回滚。这比上一轮更糟：上一轮只是丢掉这次结算，现在连已经修好的饥饿结算也一起回滚。具体是：
- 饥饿结算和房间经验都没有落库；
- 拆除服务 `RunAsync` 把这个异常收进失败汇总，`CleanupAsync` 抛出 `SelectedSessionCleanupFailure`；
- `entries` 里的宠物条目没有清除（`pending.Write(null)` 也被回滚了），这个条目会一直出现在 `DueSessions` 里，每次定时推进都只是空转返回。

这个情况在生产中触发得了：连接撤销后，定时推进因为 `IsInWorld` 为假直接返回，排队的房间经验因此不会在离场前被消化掉。只要撤销发生在清完一个房间、下一次定时推进之前，排队的经验就会留到 `LeaveAsync`。

**1.4 新测试：能抓住饥饿那一半，抓不住房间经验那一半。**
- `Leaving_after_the_connection_is_revoked_still_settles_the_stomach` 先断言正常离场后的饥饿值不等于 50，再断言撤销后离场得到同一个值。
- 换成修复前的代码，撤销后 `AdvanceCoreAsync` 第一行就返回，饥饿值停在 50，用例会失败。所以对饥饿结算这一半，用例是有效的。
- 上一轮的最小修正要求「饥饿值**与已排队的房间经验**仍然落库」，这条用例没有排队房间经验，正好漏过了 1.3 的缺陷。

**1.5 替身的撤销状态：如实。**

替身 `Sessions.Revoke` 之后：
- `Find` / `FindByCharacter` 仍然返回该记录；
- `StandingOf` 报告 `ConnectionRevoked: true`；
- `IsInWorld`、`IsSelected`、`TryGetInWorld` 都为假。

这与 `OnlineStateRegistry` 在撤销后「记录保留、连接不再归它」的行为一致，没有恒真或恒假（RQ-010）。

**最小修正**
- `GainCoreAsync` 接收 `leaving` 参数，`AdvanceCoreAsync` 发放房间经验时传入，并传给 `CreateContext`。
- 新用例或现有用例补上「排队一条房间经验 → 撤销 → `LeaveAsync`」，断言经验与饥饿值都落库，且 `LeaveAsync` 不抛异常。

### 2. 必须修复项 2（剩余 `Find…` 归类与 `AGENTS.md`）：代码侧已关闭，文字侧还差两处

**2.1 E3 的 18 处逐条归类：全部能归入 (c)～(f)，理由站得住。**

| 位置 | 类别 | 核对 |
|---|---|---|
| `ClientSettingsDomainService.cs:94` | (c) | 原样保留 |
| `GmCeraDomainService.cs:21` | (d) | 只用于 `ProjectAsync(TheseSessions)`，解析器按 `IsInWorld(exact)` 把关 |
| `CreatureRuntimeDomainService.cs:27` | (d) | 只用于构造 `InstanceRuntime`，必经 `EnsureActive`（离场支路的缺陷见 1.3） |
| `CycleDungeonEntryDomainService.cs:50` | (d) | 新增 `IsInWorld` |
| `DungeonAdmissionDomainService.cs:54`、`:59` | (d) | 新增 `IsInWorld` |
| `AccountWalletChangedHandler.cs:14` | (d) | 紧随其后有 `IsInWorld(current)` 复查 |
| `NameTagExpiredSubscriber.cs:15` | (d) | 新增 `IsInWorld` |
| `ExpiredItemsProjection.cs:27` | (d) | 新增 `IsInWorld` |
| `RentalSlice.cs:164` | (d) | 新增 `IsInWorld` |
| `MoveItemSpaceSlice.cs:401` | (d) | 只传给 `creatures.AdvanceAsync`，后者入口是 `IsInWorld` |
| `BossTowerPartyCoordination.cs:190`、`IllusionTowerPartyCoordination.cs:188` | (e) | `affected` 集合只用于后续投影与加锁，投影经人群把关 |
| `BossTowerPartyCoordination.cs:353`、`IllusionTowerPartyCoordination.cs:351` | (e) | `ExactSessions` 是队员身份一致性快照，比对 `AccountId` / `WireUserId`。若改成 `TryGetInWorld`，一个连接已撤销的队员就会让请求方的队伍操作整体失败（RQ-014a），所以保留是对的 |
| `MarriageLovePointSlice.cs:62` | (e) | 伴侣会话只用于 `WeddingProposalExecution.RunAsync` 加锁 |
| `TownAreaProjectionDelivery.cs:429` | (e) | 只用于计算要锁的旧区域，多锁一个区域没有坏处 |
| `TournamentSlice.cs:173` | (f) | 淘汰不以在线为前提，发送前有 `IsInWorld` |

**2.2 上一轮指出的两处检索盲区：都已处理。**
- `SelectCharacterLifecycleServices.cs:242`：方法组写法已改成 `TryGetInWorld` lambda。grep `\.(FindByCharacter|FindByConnection|FindByWireUserId)\b[^(]` 在 `Server/src` 中已经没有命中。
- `MarriageEligibility.SessionOf`：
  - 已删除，改成 `InWorldSessionOf`（内部用 `TryGetInWorld`），Venue 的三个调用方都改用它；
  - `Couple.cs:148` 改成 `IsCurrent(original)`，也就是 `IsInWorld`。
- 其余返回 `SelectedSessionIdentity?` 的包装方法逐个看过，内部都已经是 `TryGetInWorld` 或目录读取，没有新的盲区：
  - `PartyTownProjectionDelivery.ExactSession`
  - `TownAreaProjectionDelivery.ExactSession`
  - `PartyNotificationSlice.Character`
  - 两塔的 `LeaderToSynchronize`

**2.3 文字侧仍有两处不一致，需要修正：**
- `AGENTS.md:269-273` 写「`Find…` 只剩下面几种用法」并列了四条，对应 (a)(b)(d)(e)，但漏了两类：
  - (c)：`ClientSettingsDomainService` 的「这条连接上尚无会话」；
  - (f)：比武淘汰这类不以在线为前提的业务动作。
  
  这一段写成了穷举清单，按本仓「现状图直接改准」的约定，漏掉的两类就是新的不实陈述。上一轮这一项被判为必须修复，正是因为这一句。
- `spec.md` 的 AC-001 仍写「剩余的会话 `Find…` 调用逐条归入『保留的查找』(a) 或 (b)」，而定义里已经扩展到 (a)～(f)。照字面验收，AC-001 无法满足。

**最小修正**
- `AGENTS.md` 补上 (c)(f) 两条，或者把「只剩」改成不穷举的说法并指向 spec；
- AC-001 改为「归入 (a)～(f)」。

归类表本身属于 T-010 的完成证据，现在还没生成，这一点不算缺陷。但 `verification/logs/ac001-scan.txt` 是修复前的结果（E3 为 21 处），T-010 需要在 HEAD 上重跑。

### 3. 本次修复有没有引入新的放宽（RQ-012），或者「旁人状态导致请求方失败」（RQ-014a）

没有发现新的放宽，也没有新的 RQ-014a 违例。逐处如下：

- **婚礼结算 `WeddingSettlement.CompleteAsync`（`:60`）**：`FindByCharacter ?? throw` 改成 `TryGetInWorld … throw`，只比原来严。
  - 两位新人是婚礼的当事人，不是旁人。
  - 唯一调用方 `AdvanceAsync`（Venue `:92-114`）只在 `both` 为真时调用它，而 `both` 由 `ChurchRooms.IsPresent` 计算，`IsPresent` 本身就是 `TryGetInWorld`。所以新增的抛异常只会出现在两次读取之间被撤销的竞态窗口里，后果是事务回滚、下次推进时按「缺席」取消，与基线中会话被释放的竞态同一形态。
- **`Couple.OpenWeddingAsync`（`:148`）**：从「`FindByCharacter` 再比较」改成 `IsInWorld(original)`，同样只严不宽。
  - 两人都是当事人。
  - 协议 handler（`MarriageProposalSlice.cs:188`）已经用 `TryGetInWorld` 取对方，不在世界的对方在命令之前就判为 Invalid。
  - 新增的失败也只在竞态窗口里出现，走既有的 `MarriageWeddingUnavailableException` 撤回分支。
- **副本准入 `DungeonAdmissionDomainService` 新增 `IsInWorld`**：
  - 情侣副本与镜像副本的队员不在世界时，返回 `Refuse(MissingPrerequisite, slot)`，不抛异常。
  - 原来的「`Find` 再比 `CharacterId`」本来就是当前性判定，这次属于 RQ-011 的玩法收紧，plan 的 RQ-014 表已经列出。
  - `CycleDungeonEntryDomainService` 同理。它的调用方是接任务命令和任务查询，都在入世之后；它依赖的 `intents.Find(session)` 本来也只有在世界的会话才会有。
- **`PartyMemberSessionEndpointSource` 删掉的两项**：
  - `onlineUser.AccountId == selected.AccountId` 被 `InWorld` 的 `user.AccountId == selection.Session.AccountId` 蕴含；
  - `CurrentCharacterId == characterId` 被 `BoundCharacter` 的投影定义蕴含（阶段为 `InWorld`、Owner 是这个用户时，投影出来的就是这条记录的角色）。
  - 蕴含只在 `TryGetInWorld` 那一刻成立。紧接着的 `TryGetByConnection` 是另一次加锁，两次读取之间如果同一连接上换了角色，旧条件会拒绝，新条件不会。返回的端点属于同一个在线用户、同一条连接，实际影响可以忽略，记为残余风险，见 4.5。
- **断线观察者删掉的参数**：
  - 基线中的 `FindByConnection(connectionId) != selected` 里，`selected` 本身取自 `FindByConnection(connection.ConnectionId)`，`connectionId` 与 `selected.ConnectionId` 按构造相等，而 `SelectedSessionIdentity` 的相等比较包含 `ConnectionId`。
  - 所以 `StandingOf(selected) is not null` 与原条件等价，删掉 `CleanupSelectedHeldAsync` 的参数不改变接受范围。
  - 但上一层 `CleanupSelectedAsync(connectionId, selected, ct)` 的 `connectionId` 现在成了未用参数，见 4.4。
- **三个事件订阅者新增的 `IsInWorld`**（`NameTagExpiredSubscriber`、`ExpiredItemsNotificationSubscriber`、`LuckyStarChangedSubscriber`）：
  - 它们后续都走 `ProjectAsync(TheseSessions)`，解析器本来就按 `IsInWorld(exact)` 把关，所以对本人的效果不变；
  - `ExpiredItemsNotificationSubscriber` 在发送前持有 `SelectedSessionTransitionGate`，不会在选角工作流中途执行，因此不影响工作流阶段。

### 4. 上一轮非阻断风险的处置

| # | 风险 | 状态 | 说明 |
|---|---|---|---|
| 1 | 两塔提交前检查旁人（`BossTowerDomainService.cs:394`、`IllusionTowerDomainService.cs:318`） | **仍开着** | 代码没改，plan 也没有记载。窗口很窄，撤销只来自协议拒绝，可以接受；建议在 plan 的 RQ-014 表里补一行。 |
| 2 | AC-004 选角工作流一条测的是命令处理器 | **仍开着** | 没有新增测试。`Publish` 与 `ClientSettingsDomainService.IsCurrent` 拒绝已撤销会话的行为仍然没有直接测试。可以接受，留给完成验证判断 AC-004 字面是否满足。 |
| 3 | 定时任务的 `catch` 过滤器吞得更多 | **已处理** | plan 的「收紧后的行为」表已写明。 |
| 4 | 未用参数 | **部分处理** | `CleanupSelectedHeldAsync` 的参数已删，但 `CleanupSelectedAsync`（`OnlineUserConnectionObserver.cs:122-124`）的 `connectionId` 现在不再使用。没有行为影响。 |
| 5 | 被蕴含的条件没删干净（`PartyMemberSessionEndpointSource`） | **已处理** | 残余的跨锁窗口见第 3 节，可以忽略。 |
| 6 | 重复断言 | **已处理** | 已删除。 |
| 7 | 测试替身代码大量复制 | **仍开着** | 可以接受，属于既有写法。 |
| 8 | 收紧的测试证据只覆盖样本 | **仍开着** | plan 增加了逐路径的行为表，依据仍是「同一连接的报文按顺序处理」。这条依据对跨账号、定时任务、订阅者不成立，但这些路径的收紧都是「跳过 / 拒绝 / 不投影」，没有抛异常，可以接受。 |

另有一条证据层面的小问题：`verification/logs/remediation-build-test.txt` 的头部写着 `HEAD 97c5af5d`，说明是在修复提交之前、对未提交工作树跑的。完成验证时，应在 7b55d4bf 或之后的提交上重跑并记录。

### 结论

`changes_required`。

- **已经关闭的：**
  - 饥饿结算的直接路径；
  - 方法组和 `SessionOf` 两处盲区；
  - E3 的 18 处全部可归类且理由成立；
  - 修复没有引入新的放宽或 RQ-014a 违例。
- **必须修复：**
  1. **`GainCoreAsync` 没有传递 `leaving`。** 连接撤销后，只要还有排队的房间经验，`LeaveAsync` 就会抛异常，并回滚包括饥饿在内的全部离场结算；还要补一条覆盖房间经验的用例。
  2. **两处文字修正**：`AGENTS.md:269-273` 的穷举清单漏了 (c)(f)；`spec.md` AC-001 仍写「(a) 或 (b)」。
- 修正后需要重新独立复审 `CreatureRuntimeDomainService.cs`，并补跑 `CreatureRuntimeDomainServiceTests`。

## 第二轮修复复审

```yaml
kind: rks-review
target: 7b55d4bf..a29d7586（修复提交 a29d7586；spec.md / plan.md 以工作区未提交版本为准）
verdict: approved
reviewer: 独立审查子代理（fresh context，未参与实现与修复）
```

### 范围与取证

- 代码：`git diff 7b55d4bf..HEAD`，4 个文件：
  - `CreatureRuntimeDomainService.cs`
  - `OnlineUserConnectionObserver.cs`
  - `CreatureRuntimeDomainServiceTests.cs`
  - `AGENTS.md`
- 任务文件：`git diff -- .rks-agent`，看 `spec.md` 与 `plan.md`。
- 依据：上一节「修复复审」的两个必须修复项，以及 4 号非阻断风险（未用参数）。
- 审查方式：`reviewing-work` 的 Code 路线，再加 Deliverable 一致性核对（`AGENTS.md` / spec 与代码是否一致）。只复审未决发现及修复直接带来的回归，其余沿用前两轮结论。
- 定向测试：
  - `dotnet test Server/tests/Dfo.Server.Application.Tests/... --filter "FullyQualifiedName~Creature"`
  - HEAD 上：80 条全部通过。
  - 反向验证：临时把 `CreatureRuntimeDomainService.cs` 换回 7b55d4bf 的版本，其余文件保持 HEAD。结果是 1 条失败、79 条通过。失败的正是 `Leaving_after_the_connection_is_revoked_still_settles_the_stomach_and_queued_experience`，异常为 `InvalidOperationException: The creature's selected session has changed.`，栈与上一轮探针一致：`EnsureActive ← GainCoreAsync:231 ← LeaveAsync:257`。
  - 验证后已用 `git checkout HEAD --` 还原，重跑仍是 80 条通过；`git status` 中 `Server/` 没有残留改动。

### 1. 必须修复项 1（宠物离场结算的房间经验支路）：已关闭

**1.1 离场路径上的上下文全部带上了 `leaving`。** 逐个追了 `LeaveAsync` 能到达的创建点：

- `LeaveAsync` → `AdvanceCoreAsync(..., leaving: true)`：
  - 入口按 `StandingOf(session) is null` 判断；
  - `:149` 的 `CreateContext(tx, ..., leaving)` 带上了标记。
- 房间经验支路：
  - `:196` 改为 `GainCoreAsync(..., ct, leaving)`；
  - `:230` 的 `CreateContext(tx, session.CharacterId, leaving)` 把标记交给 `InstanceRuntime`；
  - `InstanceRuntime.EnsureActive` 因此按 `StandingOf` 判断。
- `PetInstance.AddExpCoreAsync` 以及它可能调用的 `EvolveAsync`：
  - 只使用传入的同一个 `context`，没有再经 `Of(...)` 或 `CreateContext` 新建上下文；
  - 注释写明了「不得递归进入同步」，`AddExpCoreAsync` 也确实不调 `SynchronizeAsync`。
- `InstanceRuntime.SynchronizeAsync` 把 `leaving` 继续往下传，上一轮已核对。
- 检索 `CreateContext(` 的调用，共 5 处：
  - 不带 `leaving` 的只剩 `Of(...)`（`:22`）和 `HatchAsync`（`:208`），两处都不在离场路径上；
  - `GainCoreAsync` 的默认值 `false` 只在 `leaving` 为假时生效。

**1.2 玩法入口仍要求 `IsInWorld`。**
- `AdvanceAsync`：它调 `AdvanceCoreAsync` 时不传 `leaving`，默认为 false。
- `HatchAsync`：`:205` 检查 `IsInWorld`。
- `GainExperienceAsync`：`:220` 检查 `IsInWorld`，之后经 `Of(...)`，上下文不带 leaving。
- `GainRoomExperienceAsync`：走 `GainExperienceAsync`。
- `CreatureNamingDomainService`：`:21` 检查 `IsInWorld`。

`leaving` 只能由 `LeaveAsync` 置为真，没有新的放宽。

**1.3 扩展后的测试能抓住这条支路。**
- 用例在撤销连接之前用 `QueueRoomExperience` 排了一条房间经验。
- 用例最后的断言 `Assert.Equal(settled, await LeaveAfterAnHour(revoke: true))` 比较 (饥饿, 经验) 元组：
  - 修复前，撤销那次的 `LeaveAsync` 直接抛异常，用例失败（已实测）；
  - 若某次修复把房间经验悄悄跳过而不抛异常，经验值会停在种子值 90，与正常离场不等，同样失败。
- `Assert.Empty(f.Runtime.DueSessions())` 在正常离场与撤销后离场两种情况下都断言了：没有留下会被定时任务空转的条目。上一轮 1.3 指出的「条目残留」也一并覆盖。

### 2. 必须修复项 2（文字侧）：已关闭

**2.1 `AGENTS.md:269-275` 的清单与 spec (a)～(f) 逐条对应。**

| AGENTS.md 条目 | spec 类别 | 代码依据（上一轮 2.1 表） |
|---|---|---|
| 请求方按自己这条连接解析会话 | (a) | — |
| 清理路径取出要释放的会话 | (b) | 拆除服务、返回选角、断线观察者 |
| 查到之后必须经过一处具名判定 | (d) | 钱包复查、人群投递、`EnsureActive` 等 |
| 只用于加锁，或只做身份一致性快照 | (e) | 两塔 `affected` / `ExactSessions`、爱心点数、城镇旧区域 |
| 不以在线为前提的业务动作，发送前以 `IsInWorld` 把关 | (f) | `TournamentSlice.cs:173` |
| 客户端设置确认「这条连接上还没有选中会话」 | (c) | `ClientSettingsDomainService.cs:94` |

六类齐全，没有多出 spec 以外的类别，穷举陈述现在成立。
排列顺序与 spec 的字母顺序不同，但 `AGENTS.md` 没有标注字母，不影响读者。

**2.2 spec AC-001** 已改为「逐条归入『保留的查找』(a)～(f)」，与类别定义一致。
保留类别 (d)(e)(f) 的定义和 RQ-009 的婚姻例外也已写进 spec，上一轮已核对。

### 3. 断线观察者删掉 `connectionId` 参数：行为不变

- `CleanupSelectedAsync` 现在只接收 `selected`，唯一调用方是 `:84`（孤儿会话分支），传入的就是由 `FindByConnection(connection.ConnectionId)` 取到的 `orphaned`。
- 删掉的参数在 7b55d4bf 中已经没有被读取：函数体只用 `selected.AccountId` 取租约，再调 `CleanupSelectedHeldAsync(selected)`。
- 两处调用、租约顺序与 `StandingOf` 守卫都没变。
- 这是上一轮 4 号非阻断风险的收尾，现已关闭。

### 4. 有没有新的放宽或新问题

没有发现阻断问题。只有两条非阻断观察：

1. **测试里 `Assert.NotEqual(0L, settled.Experience)` 是空断言。**
   - 种子经验是 90，见同文件 `:203`、`:334` 的 `Assert.Equal(90L, ...)`，所以无论经验发没发放，这条都成立。
   - 它没能证明「正常离场时排队经验确实发放」。
   - 因为元组相等断言已经能抓住撤销那条支路（见 1.3），这条不影响本次修复的判据。
   - 建议改成 `Assert.Equal(100L, settled.Experience)`，或 `NotEqual(90L, ...)`，让「经验落库」有正面证据。
2. **spec 的 (b) 引用了 `OnlineUserConnectionObserver.cs:106`。**
   - 本次把 `:84` 的调用合成一行之后，原来 `:106` 的 `FindByConnection` 现在在 `:105`。
   - 该文件里的 `Find…` 实际有三处：`:82`、`:93`、`:105`。
   - T-010 生成归类表时应按 HEAD 上的行号重新标注，并重跑 `ac001.py`。上一轮已指出 `verification/logs/ac001-scan.txt` 是修复前的结果。

其他核对：
- `GainCoreAsync` 把带默认值的 `bool leaving` 放在 `CancellationToken` 之后，与同文件里 `AdvanceCoreAsync` 的既有写法一致；在 `TreatWarningsAsErrors` 下构建通过。
- plan 的「收紧后的行为」表已补上两条：
  - 宠物离场结算「不收紧」那一行，写明了包含排队的房间经验；
  - 两塔提交前检查旁人那一行，即上一轮 1 号非阻断风险，现已记录为接受。

### 结论

`approved`。

上一轮的两个必须修复项都已关闭：
- 离场路径上的上下文全部带上了 `leaving`，玩法入口仍要求 `IsInWorld`，扩展后的用例在修复前失败、修复后通过；
- `AGENTS.md` 的清单与 spec (a)～(f) 一致，AC-001 措辞已修正。

本结论只覆盖本轮的复审范围，不代替完成验证。完成验证仍需要：
- 在 a29d7586 或之后的提交上重跑全量构建与测试；
- 重跑 `ac001.py` 并生成 T-010 的归类表。
