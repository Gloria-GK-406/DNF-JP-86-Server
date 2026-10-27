---
kind: rks-review
target: .rks-agent/tasks/session-currency-checks/TaskContract.md
target_revision: 1
verdict: changes_required
reviewer: independent subagent (reviewing-work, deliverable + modeling-task-contracts readiness rules)
---

# TaskContract 审查：session-currency-checks（revision 1）

## 范围

- 目标：`.rks-agent/tasks/session-currency-checks/TaskContract.md`，frontmatter `revision: 1`、`state: ready`。
- 重点：Current Situation 的事实陈述与代码是否一致；两级口径与用途规则能否执行、是否会误分类；有没有把机制过度约束成硬要求；Root Intent Coverage 与 Scope Out；ready 条件。
- 只审查，不修改契约或代码，不审查实现方案。

## 依据

- `reviewing-work` 的 Deliverable 参考：完整性、正确性、来源忠实度、可用性、范围、无依据的断言。
- `/Users/puxianguo/.claude/skills/modeling-task-contracts/SKILL.md` 的「Separate Outcomes from Mechanisms」、Create or Revise 第 5–7 步、「Readiness」。
- 来源：issue #185（OPEN），以及它提到的 #182、#204（都是 OPEN）；契约 Source Request 与 Authorization 中记录的用户原话（审查者看不到会话原文，按契约记录采信）；`Docs/Agent/RefactorPreferences.md`；用户声明 AGENTS.md 只作参考。
- 代码基准：worktree HEAD `ea6a1544`。下文路径相对 `Server/src/`。

## 关键证据

### Current Situation 的事实核对

| 契约陈述 | 代码核对 | 结论 |
|---|---|---|
| `ISelectedSessionDomainService` 现有 `Find`/`FindByCharacter`/`FindByWireUserId`/`FindByConnection`/`StandingOf`/`IsInWorld` | `Abstractions/Sessions/ISessionPresence.cs:16-55` | 一致 |
| 四种查法读同一条记录，「查再比」答案相同，都是「是否当前选中」 | `Application/Users/OnlineStateRegistry.cs:204-225`：四个方法都在同一把 `sync` 锁下读 `selections`。`Select`（`:139-157`）保证角色与线上号跨账号唯一，记录键就是 `session.AccountId`，因此 `X(session.键) == session` 与 `FindExact(session) != null` 等价 | 成立。小注：`FindByConnection` 依赖「一条连接只认证一个账号」（`OnlineUser.Authenticate` 抛异常保证），`Select` 本身不检查连接唯一，`SingleOrDefault` 理论上会抛异常而不是返回 false |
| `IsInWorld` = 当前且阶段为在世界；调用 12 处 | `OnlineStateRegistry.cs:241-245`；生产调用：TownProjection、TauKingdom、MonsterDeath、ExperienceBonus×2、SouthernDale、CurrentView×2、BossTowerTicket、OnlineExpiredItemCleanup、GmPlayerRuntimeQuery、CharacterNameTag，共 12 处 | 一致 |
| `IsActiveExact` 调用 6 处 | TauKingdom、SouthernDale、BossTowerTicket、OnlineExpiredItemCleanup、GmPlayerRuntimeQuery、CharacterNameTag，共 6 处 | 一致 |
| `IsActiveExact` 与在线用户组合（`CharacterActive`+账号+`CurrentCharacterId`）是「在世界且连接仍归它」的两种写法 | `IsActiveExact`（`:247-256`）要求精确会话、`InWorld`、Owner 的连接就是会话的连接。组合写法依赖快照：`CharacterActive` 由 `BoundCharacter`（`:317-323`）推出，条件是该账号的选中记录处于 `InWorld` 且 Owner 就是这个连接上的用户。它与 `Find(account)==session` 叠用时，与前者等价（只差 Game 端点判断，而 `EnterWorld` 只经 Game 连接发生） | 成立 |
| 组合写法约 32 处 | `CharacterActive` 在 Application/Protocol 的非注释命中 36 处，扣除 `ICharacterActiveQuestTable` 等 3 处同名误中和注册表自身 1 处，约 32 处 | 一致 |
| 查再比约 322 行（Application 约 86、Protocol 约 236） | 按同类正则复算得 87 / 234。其中约 20 行是 `runs.Find(...)?.RunId`、`towers.Find(...)?.RunId`，属于副本或塔，不是会话 | 量级成立；会话类实际约 300 行 |
| 自定义当前性 helper 约 10 个，同名不同义 | `MarriageEligibility.IsCurrent`、`HousingState.IsPresent`、`ChurchRooms.IsPresent`、`NpcShopDomainService.IsCurrent`、`GmPlayerRuntimeQuery.Current`、`CreatureRuntimeDomainService.IsCurrent`、`NotificationBus.IsStillHere`、两塔 `ProjectionDelivery.Current`、`DungeonLoadingProjectionDelivery.IsCurrentSession`、`DungeonCardPresentationCoordinator.IsCurrent`、`SelectCharacterHandler.EnsureCurrent`、`ClientSettingsDomainService.IsCurrent` | 量级成立（10–13 个）；但其中至少两个的口径不属于两级中的任何一级，见阻断项 1 |
| `OnlineExpiredItemCleanup` 一个条件四连判 | `Application/Inventory/OnlineExpiredItemCleanup.cs:28-29`：`Find==`、`FindByConnection==`、`IsInWorld`、`IsActiveExact` | 一致 |
| 选角在 Publish 之前派发准备、过期物品清理、契约人偶、婚房初始化、状态准备、每日补货、补偿 | `Protocol/Game/SelectCharacter/SelectCharacterHandler.cs:107,124,139,141,145,301,213`；Publish 在 `:334` | 一致 |
| **未列入的一类** | 「取到即用」的查询：`Find…(...) is { } x` / `is not { } x` 约 93 处，例如 `Notifications/NotificationAudienceResolver.cs:91`、`Characters/CharacterVisibleSessionDomainService.cs:34`、`Protocol/Game/Rental/RentalSlice.cs:164`、`Protocol/Game/Weakness/WeaknessRecoveryService.cs:67`、`Protocol/Game/Joust/JoustSlice.cs:159`。它们以「查不到」作为「不在线、不投递、不处理」，自身不做比较 | 契约没有点名这一类，见阻断项 2 |

### 用途规则抽查

| 调用点类别 | 代码 | 按契约规则的归类与后果 |
|---|---|---|
| 选角工作流 Publish **之前**的 `EnsureCurrent` | `SelectCharacterHandler.cs:540-548`：`Find(account) == session` **且** `TryGetByConnection(session.ConnectionId)` 的账号仍是它，即「当前选中且连接未撤销」。`:294` 的检查在 `:301` 每日补货之前 | 约束 1 把它归为 `IsSelected`，会**丢掉**「连接未撤销」这一半，口径放宽。见阻断项 1 |
| 选角工作流 Publish **之后**的命令（门票补发、热键、PvP、任务、宠物、各类投影） | `:337-452`，仍在同一把独占租约内 | 约束 1 没有点名。按「其余」规则归为 `IsInWorld`；发布后阶段已是 `InWorld`，加严后与现有 `EnsureCurrent` 等价。可以执行，但应写明 |
| 断线观察者 | `Protocol/Game/OnlineUserConnectionObserver.cs:82,104,135`：按连接取孤儿会话、精确比对后清理 | 「断线清理」→ `IsSelected`，与现状一致 |
| 拆除中的拥有判定 | `Application/Sessions/SelectCharacterLifecycleServices.cs:59` `ownsSelectedCharacter` | 「拆除」→ `IsSelected`，与现状一致；同文件 `:74,262` 给同伴的通知属于投递 → `IsInWorld` |
| 后台定时任务 | 门票定时补发（三个 `RefillOnline*Ticket`）已经用 `IsActiveExact`；`WeaknessRecoveryService.cs:67` 用 `is not { }` | 前者 → `IsInWorld`，等价；后者属于阻断项 2 的未定类别 |
| 通知投递 | `Notifications/NotificationBus.cs:264-268`：角色人群看 `FindByCharacter(...).ConnectionId == recipient.ConnectionId`，不论阶段；`NotificationAudienceResolver.cs:79-80` 精确会话人群是 `FindByCharacter == exact`，`:91` 角色人群是 `is { }` | SC-4 只点名总线的投递前检查，没有点名人群解析器；角色人群「保留连接语义」是否同时收紧到 `InWorld`，契约没有说清。见非阻断风险 2 |
| 连接代数检查 | `Accounts/ClientSettingsDomainService.cs:88-93`：在线用户 `Generation == origin.Generation`；`origin.SelectedSession` 可以为 null（登录阶段） | 两级都表达不了「连接代数」，也表达不了「没有会话」。见阻断项 1 |
| 管线 | `Commands/SelectedSessionSafety.cs:35-38,49-56`：`Capture`/`DemandCurrent(recipient)` 内含 `user.AccountId`+`user.CurrentCharacterId` 组合与连接对象 `ReferenceEquals` 比较 | Scope Out 1 不改它，但 SC-1 字面要求生产代码里不再有「用于当前性的在线用户标志组合」。见非阻断风险 3 |

### 独占租约假设

`Commands/Pipeline/SelectedSessionExecutionBehavior.cs:17-26`：只有 `ISelectedSessionCommand` 会按账号借共享租约；`SelectCharacterHandler.cs:103` 对本账号持独占租约，覆盖整条工作流。`Networking/Runtime/NetworkPlugins.cs:64,147` 在接收回调里 `await` 分发，同一连接的报文按顺序处理。对「本账号、本连接」的命令，假设成立。但另一个账号的 handler 把这个角色当**目标**（组队、交易、同伴投递），以及定时任务、事件订阅者，都不经过这把租约，可以看到处于 `Selected` 阶段的会话。所以收紧的影响不只限于「连接已撤销、待拆除的窗口」，见非阻断风险 1。

### 机制与来源

- 两级、`TryGetInWorld`、先替换再收紧、DB 再查归属保留、会话命令内保留一处检查、隔离留在管线：都是 Agent 提议、用户以“其他的都OK”“tryget也算能接受”认同的方案，可作为约束的依据。
- 约束 3 把命名写成硬要求。用户原意是「命名交给 Agent」，同时又认同了这组名字，所以不算越权，只是 RI-3（授予自由）被落成了一条锁定条款，见非阻断风险 5。
- 约束 2「每个领域服务公开入口保留一处具名检查」比 RI-7 的来源「会话命令内部保留一处具名检查」更宽，见非阻断风险 4。
- 约束 5 的出处 `RefactorPreferences.md` §2「一次性全迁移吧」与 §3.5「不留兼容层」一致。

### Root Intent Coverage 与 Readiness

- RI-1～RI-9 各出现一次，处置值合法，没有 `unresolved`。RI-8 的 `preserved_upstream` 指向 #182、#204，两者都是 OPEN，职责（加锁原语、租约、并发声明）与本契约不同；#182 也列了「`safety.Capture/DemandCurrent` 样板手写 8 处」，与 Scope Out 1 的分界一致。
- RI-4 的 `authorized_change` 有依据，但只覆盖**收紧**。阻断项 1 里出现的放宽不在授权之内。
- RI-9（全部 issue 做完再一次性推送）在 Source Request 中找不到对应原话，只在 Authorization 和 Scope Out 6 里有体现，可追溯性偏弱，见非阻断风险 6。
- Scope Out 4 与 RI-2 冲突，见阻断项 2。
- Authorization 只有一行 `task`，范围覆盖本地实施、检查点提交和创建一个 issue，与 Scope Out 6 一致。
- 每条 SC 都有可观察的证据。Assumptions 可见，没有 Open Questions。
- 有两条阻断项，因此 `state: ready` 不成立。

## 问题

### 阻断项

1. **「只有两级」漏掉了现有的第三种口径，按约束 1 归类会放宽检查并改变持久化结果。**
   - 缺陷：`SelectCharacterHandler.EnsureCurrent`（`:540-548`）检查的是「当前选中**且**连接未撤销」，任何阶段都适用。Publish 之前它出现在 `:289`、`:294`、`:304`、`:316`、`:332`，其中 `:294` 挡在 `:301` 的 `ApplySelectedCharacterDailyRefillCommand`（Transactional，会落库）之前。约束 1 规定「入场发布前……问 `IsSelected`」，SC-1 又要求删掉所有自定义 helper，二者合起来，这几处只能换成 `IsSelected`。
   - 后果：发布前连接被顶替时（`LoginSessionRuntime` 不经生命周期门、立即撤销在线用户），今天在补货之前就中止；改后补货照样提交，直到 `EnterWorld` 才抛异常。这违反 Goal「除有意收紧外……持久化结果不变」，而 RI-4 的授权只覆盖收紧。同类还有 `ClientSettingsDomainService.IsCurrent`（`:88-93`）：它比对在线用户代数，并且允许没有会话，两级都表达不了，SC-1 和 SC-3 却要求它归入其中一级。
   - 最小修正：在契约里写明一条保底规则：替换后每个调用点都不得比原来宽松（RI-4 授权的收紧除外）；并为「发布前工作流：当前选中且连接仍归它」明确归处。可以选的做法：在具名判定面上给出这一语义；明确允许经 `StandingOf` 组合；或把它归为不属于会话当前性的连接级检查（与 `ClientSettingsDomainService.IsCurrent` 一样），移出 SC-1。前两种做法偏离了用户认同的「只留两级」，需要先取得用户确认。

2. **Scope Out 4 会吞掉 RI-2 的主体，SC-1 也测不出来。**
   - 缺陷：Scope Out 4「只取会话来用、不做当前性比较的 `Find…` 查询保留」，与 Goal「这个角色在不在线、是哪个会话只由具名判定回答」以及 RI-2「通知里的人是否在线，按角色检查（TryGet）」直接冲突。用户点名的「通知里的这个人到底在不在线」，代码里正好就是 `is { }` 这种不比较的写法：`NotificationAudienceResolver.cs:91`（角色人群解析）、`CharacterVisibleSessionDomainService.cs:34`、`RentalSlice.cs:164`、`WeaknessRecoveryService.cs:67`，这类写法约 93 处。SC-1 的检索对象只有「查再比、标志组合、`IsActiveExact`、自定义 helper」，不包括它们。
   - 后果：按字面执行，这些点留在「任何阶段、含已撤销」的口径上，RI-2 基本落空，而 SC-1 仍会通过。反过来，如果实施者自行把它们算作当前性判定，则 Scope Out 4 的边界没有依据，范围靠临场判断。
   - 最小修正：重新定义 Scope Out 4：凡是以「查不到」决定不投递、不处理、不在线的查询，都属于当前性判定，在 Scope In 3 和 SC-1 的范围内，按用途归级；只有拿已知当前的会话、或为生命周期清理取会话的查询才保留。并在 SC-1 的证据中加上这一类。

### 非阻断风险（交给 Spec 处理）

1. **独占租约假设比实际窄。** 本账号、本连接的会话命令确实要等到发布之后。但作为其他账号操作的目标，以及定时任务、事件订阅者看到的 `Selected` 阶段会话，都会因为收紧到 `IsInWorld` 而被拒绝或跳过，正常路径上也会出现（例如目标正在切换角色）。Spec 应列出跨账号目标检查和非命令路径，说明收紧后的行为，并在 SC-3 的测试证据里覆盖。
2. **投递的人群语义没有说清。** 角色人群「保留解析出的连接仍是它的」语义，是否同时加上 `InWorld`，契约没有说。人群解析器（`NotificationAudienceResolver`）的三个 `Reachable` 也没有点名。Spec 应按人群形态写清解析和投递两步的口径，并确认没有给 `Selected` 阶段的本人或同伴、必须送达的投递（例如拆除通告）因此被丢弃。
3. **SC-1 与 Scope Out 1 字面冲突。** `SelectedSessionSafety.Capture` / `DemandCurrent(recipient)` 含在线用户标志组合和连接对象引用比较，后者比 `IsInWorld` 更严格。Spec 应写明：这里的表达式是否改写为具名判定、行为是否保持不变，或者明确排除出 SC-1。
4. **约束 2 比来源宽。** RI-7 的来源是「会话命令内部保留一处具名检查」；约束 2 变成「每个领域服务公开入口保留一处」。如果理解成「每个入口都要有」，就会给原本没有检查的入口（只读查询、发布前被调用的服务）新增检查，带来未授权的行为变化。建议改成结果层面的表述：删掉被包含的重复检查后，原来有保护的入口仍有等价保护。
5. **命名锁定。** 约束 3 把名字写死了。用户既授予了命名自由，也认同了这组名字，所以可以保留；但如果阻断项 1 需要第三种语义，命名应允许随之调整，不应成为阻碍。
6. **RI-9 缺少来源锚点。** 「全部 issue 在本分支做完后再一次性推送」没有出现在 Source Request 里。建议补上原话出处。
7. **计数口径。** 「查再比约 322 行」里有约 20 行是副本或塔的 `RunId` 比较；`TownAreaMember` 的 `Owns`（49 处）是区域成员租约比对，经常与会话查询叠用（`peer.Owns(member)`），契约没有说它算不算。Spec 归类时应说明 `Owns` 是区域成员语义，不属于会话当前性，并修正计数。

## 结论

`changes_required`。Current Situation 的事实陈述基本准确，数量级站得住；授权、RI-8 与 #182/#204 的分界都成立。但有两处必须先修改契约：

1. 两级口径不完备，按约束 1 执行会在选角发布前放宽检查，改变落库结果（阻断项 1）；
2. Scope Out 4 与 Goal、RI-2 冲突，会把「按角色判在线」的主体排除在外，SC-1 也检测不到（阻断项 2）。

阻断项 1 的修正如果引入第三种语义，就偏离了用户认同的「只留两级」，需要先请用户确认。修订时 `revision` 递增到 2，然后重新审查。
