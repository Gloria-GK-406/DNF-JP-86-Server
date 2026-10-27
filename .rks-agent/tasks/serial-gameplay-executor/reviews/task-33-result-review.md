---
kind: rks-review
target: tasks/TASK-33.md
verdict: approved
---

# TASK-33 结果快速审查（T-009d：文档与附录对账，独立审查者）

- 审查对象：`tasks/TASK-33.md` Revision 1。范围是第 1 轮执行的结果、「主控方裁定（执行前）」1～9，以及 Result。工作区 HEAD 为 `a5e3bccb`，代码冻结点为 `2bdb1afd`。审查的改动是相对 HEAD 的未提交差异与未跟踪文件：`AGENTS.md`（+170／−83）、`Docs/Agent/ArchitecturePhilosophy.md`、`spec.md`（附录 A、B、F、G 与 R4）、`verification/entry-reconciliation.md`、`verification/follow-ups.md`，以及新建的 `verification/t009-docs-and-appendices.md` 和 `verification/logs/task-33-*.txt`（9 份）。
- 适用标准：reviewing-work 的 Task Unit 准则与 Deliverable 准则。
- 方式：只读，没有构建，也没有跑测试。逐段对照冻结点源码（`Server/src`、`Server/tests`）核对 AGENTS.md 新增与改写的断言。数字和计数用 `git grep` 和 `grep` 独立复算；对执行者的日志，只抽查其中的结论行。

## 结论：`approved`

AGENTS.md 的执行模型现状图与冻结点代码一致。我抽查了 40 余处具体断言，包括类名、方法名、作业类型名、数字和次序，没有发现与代码相反的说法。过渡态六项都写成「今天如此」，没有把子 Spec B 的目标写成今天的事实。A 删除的名字在 AGENTS.md 与 `Docs/Agent/*.md` 中检索为 0。Spec 只改了附录 A、B、F、G 与 R4：8 处改动与 R4 的 9 行一一对应，RQ、AC、正文和 frontmatter 都没有动。RQ-024、AC-009 ①②、AC-022 的结论都有日志支撑。凡是附条件的项，对账记录都写成了「满足，附条件」。冻结核对为空，行尾全为 LF，`diff --check` 无输出。

下面七条发现都不阻塞。L1 改的是要交外部审核的 Spec 验收文字，建议在提交前顺手改掉；其余是对账记录的措辞或出处问题，以及一条已登记的遗留。

## 发现（按严重度）

1. **L1（低，建议提交前改）VC-10 的第一句把跳过范围写大了。**
   - 位置：`spec.md` 附录 G 的 VC-10「变化」栏：「发件箱里排在他之后的条目都不再交付」。
   - 问题：发送端只跳过**同一有序组**内、排在失败的必达条目之后的条目。证据是 `OutboundSender.cs:366` 的组内判定，以及 `PartyNotificationSlice.cs:677` 的 `ordering?.Begin("XO-6 party state stages", OutboundDelivery.Required)`。同一作业发件箱里不在该组的条目照常交付；发往失败者本人连接的条目则因连接关闭被丢弃。同一行的「理由」栏写的是「组内其后的全部条目」，两栏口径不一致。VC 是交外部审核的验收文字，读者会按字面理解成整只发件箱都不再交付。
   - 修法：把第一句改为「同一有序组（XO-6 的队伍状态阶段）里排在他之后的条目都不再交付」。改动只涉及措辞，证据组合不变。

2. **L2（低，已登记，交子 Spec B）AGENTS.md 今天对同一件事给了两个不同的命令名。**
   - 位置：「命令执行总线」注册一段已改成 `RedeemGameLogin`（与 `ProtocolModule.cs:555` 一致）；但「端到端示例：GameLogin」第 3 步仍写 `commandBus.SendAsync(LoginGameUserCommand)`、`LoginGameUserCommandHandler`、`IAccountDomainService.LoginAsync`。这几个名字在 `Server/src` 中检索为 0，执行者对这一步做了改写，但没有改这几个名字。
   - 判断：这是裁定第 3 条（B 所属的节只改 A 改变的事实）允许的结果，也已经如实登记为 F-128 ①，所以不算本单元的缺陷。代价是：子 Spec B 的 agent 读到第 3 步，会看到一个不存在的命令名。
   - 修法：不在本单元改。TASK-34 应把 F-128 ① 列进 A 最终汇报的遗留，并提示 B 的首个单元先改这一节。

3. **L3（低，措辞）过渡态 T3（管线五层）既没有写「新代码不要依赖什么」，句式也有歧义。**
   - 位置：AGENTS.md「2) 命令执行总线」：「管线（…）。五层包在每一条细粒度命令外，这是过渡形态：」，冒号后紧接 1～5 的列表。
   - 问题：读起来像是五个行为本身都是过渡形态（包括 `UnitOfWorkBehavior`），而真正的过渡态是「每条细粒度命令各套一层管线」这个粒度。计划 §2.5 第 3 项把这一栏标为「—」，所以不算违约。但过渡态的约束写法是「今天如此，新代码不要依赖 X」，这一项缺了后半句。
   - 修法（可选）：改为「这五层今天包在每一条细粒度命令外；这个粒度是过渡形态，新代码不要依赖『一条命令一套管线』。」TASK-34 对照 SC-12 时可以一并处理（裁定第 9 条）。

4. **L4（低，对账记录措辞）附录 A 第 5 行的计数说明与检索式不符。**
   - 位置：`t009-docs-and-appendices.md` §3 第 5 行：「`timer:<类型名>` 字面量 22 处（含两塔扫描、卡牌计时器的 4 处常量，另有调度任务的插值 1 处）」。
   - 问题：检索式 `"timer:\w+"`（`logs/task-33-appendices.txt:57`）匹配不到 `"timer:DungeonCardPresentationCoordinator/layout"` 和 `/auto-flip` 这两条，因为字符串里带 `/`。我独立复算：冻结点上 `"timer:…"` 字面量共 24 处，去掉卡牌 2 处正好 22。也就是说，这 22 处包含两塔 2 处、`LoginSessionMaintenanceService` 1 处和 Joust 2 处，不含卡牌。括号里「含卡牌计时器」的说法不成立。结论「满足」不受影响。
   - 修法：括号改为「含两塔扫描 2 处常量；卡牌计时器的 2 处带 `/` 另行检索（第 6 行），调度任务的插值 1 处」。

5. **L5（低，对账记录措辞）回调数写成了三个，实际是四个。**
   - 位置：`t009-docs-and-appendices.md` §4「传输层的连接注册」行：「`NetworkPlugins` 三个回调的 `RunDetachedAsync`」。
   - 问题：冻结点上 `NetworkPlugins.cs` 有四处，分别在 `:25` Connected、`:42` Closed、`:71` Channel Received、`:159` Game Received。附录 B 原文写的也是「四个回调外层」。
   - 修法：改成「四个回调」。

6. **L6（低，出处）R4 第 5 行（附录 A 第 5 行）的依据指错了日志。**
   - 位置：`spec.md` R4 表「附录 A 第 5 行『来源』」一行，依据写的是「冻结点检索（`logs/task-33-appendices.txt`）」。
   - 问题：托管服务 34 → 33 的检索输出在 `logs/task-33-verify.txt`「## t33/hosted.sh」一节（第 379 行起），`task-33-appendices.txt` 只在文件头提到 `hosted.sh`，没有它的输出。数本身经核对是对的：只少了 `NotificationRetryDrainService` 一行。
   - 修法：依据改为 `logs/task-33-verify.txt`（t33/hosted.sh）。

7. **L7（提示）AC 状态表里 AC-022 的「满足」没有带上检索范围。**
   - 位置：`t009-docs-and-appendices.md` §12 的 AC-022 一行，依据只写「§11」。
   - 判断：§11 已经如实写明 `Docs/ProtocolSliceArchitectureNotes.md:162` 是一份不带日期、已过期的现状描述，按裁定第 2 条不在检索范围内，并列进了 F-128。所以「满足」本身成立。但 TASK-34 会照抄这张表，外部审核看到的只有「满足」二字。
   - 修法（可选）：依据改为「§11（检索范围按 TASK-33 主控方裁定第 2 条：现状文档为 0，范围外的一份过期现状描述列 F-128）」。

## 已核实无问题的项目

| 项 | 核对内容与证据（冻结点） |
|---|---|
| 一条队列与作业类型名 | `MessageHandlerRegistry.cs:89` `inbound:{messageType}`；`NetworkConnectionLifecycleDispatcher.cs:29/33/59` 三个 `lifecycle:`；`ScheduledTaskHostedService.cs:140` `timer:ScheduledTaskHostedService/{Id}`；`DungeonCardPresentationCoordinator.cs:64/217`；`DomainEventDispatcher.cs:117` 的 `event:`／`startup:event/` 与 `:130` 的 `/depth-exceeded`；`HttpGameplay.cs` `CommandTypeOf` 生成的 `gm:`／`http:`；`startup:` 四个字面量；`CommandBus.cs` 的 `bus:`，注释写明生产中没有调用方 |
| 生产者与合并 | `CoalescingGameplaySource` 用 `Interlocked.CompareExchange(ref outstanding…)` 防止重复入队；入站 dispatcher 等作业完成才读下一帧；遥测路径 `NetworkMessageDispatcher.cs:44` 不入队 |
| 玩法路径不加门 | `LockRemovalSourceTests`、`DetachmentSiteTests` 存在；`SuppressFlow` 只出现在 `GameplayExecutor`、`OutboundSender`、`DungeonCardPresentationCoordinator` 三个文件；`RunDetachedAsync` 只在 `NetworkPlugins` 的四个回调；`PasswordComputationLimiter` 持有 `SemaphoreSlim`，并调用 `AssertNotOnExecutor` |
| 作业内不等网络 | 自我等待抛 `GameplaySelfWaitException`（`GameplayExecutor.RunAsync`）；`OutboundRealSocketTests` 覆盖「作业直接写连接即程序错误」 |
| 运行期判据 | `GameplayWriteAssertion.Writing` 无条件抛 `GameplayOffExecutorException`（`GameplayAssertions.cs:41-47`），`DomainTransactionScope.cs:42` 对写模式调用它；`Diagnostics.IsEnabled` 只控制记录，不是开关；`GameplayWriteEntryTableTests`、`GameplayStateCensusTests`、`GameplayJobHostStructureTests` 与 `tests/Support/TestGameplay.cs` 都存在 |
| 请求级帧与 E3 | `GameplayStateCensusTests.cs:129-134` 的 E3 是 `NetworkCommandAuthorization`、`SelectedSessionCommandScope`、`EffectOperationContext`；`CommandBus` 的 `DispatchDepth` 是静态 AsyncLocal |
| 调用链与深度 | `GameplayCallChain.cs:24` 的 `GameplayCallDepth.Limit = 32`；超限时执行器记 Error，不入队；超限的拆除改作新根，并由 `lifecycle:depth-exceeded` 关闭根连接；超限的事件 delivery 由 `SettleAsync` 系统作业落定 |
| 失败与 `EndedAJob` | 执行器失败日志在 `GameplayExecutor.cs:312`，级别 Error，并标记 `MarkJobFailure`；`NetworkPlugins.cs:89/185`、`TouchSocketTransportDrain`、`HttpApiApplicationExtensions.cs:255`、卡牌协调器按 `EndedAJob` 降为 Debug；两塔扫描、Joust、调度器、事件组改记不带异常的 Warning；`CommandLoggingBehavior` 另记一条 Error |
| 发件箱 | `GameplayOutbox` remarks 写明：单元内的条目随单元存亡，单元外的条目与关连接总会保留；提交时组装；结果不确定时丢弃；作业结束时、调用方看到结果之前 flush；失败时最后关闭来源连接；逐收件人组装失败只记 Warning；两个 dispatcher 在作业开始时读 `outboxes.Current`（`NetworkMessageDispatcher.cs:51`、`NetworkConnectionLifecycleDispatcher.cs:35`）；`OutboxConnectionCloser` 存在 |
| 发送端 | `OutboundLimits`：`DeliveryTimeout` 2 s、`OrderedWaitLimit` 2 s、`QueueByteLimit` 1 MiB；必达失败后组内其后的条目 Skipped，关连接请求除外（`OutboundSender.cs:133-149`）；级别：失败、超时、超限记 Warning，`Failed when gone` 记 Information 并带后缀 `; the connection was already gone`；`IsGone` 只问 `transport.Resolve`，与 F-127 的已知缺口描述一致 |
| 停机 | `GameplayExecutorLifetime.StoppedAsync` 在所有 `StopAsync` 之后依次排空生产者，再 `executor.StopAsync` |
| 命令执行总线 | `CommandExecutionKind` 有 Direct、Transactional、Workflow、ReadOnly 四种；`ISelectedSessionCommand`、`IRequestSettlementCommand`、`IMultiSelectedSessionCommand`、`IAccountCommand` 四个接口都在，其中 `IAccountCommand` 只在 45 号行为里用于账号一致检查；`SendAsync` 有两种语义；管线的 Order 为 0、45、58、60、100；`UnitOfWorkBehavior` remarks 写明 45 < 58 < 60 < 100，ReadOnly 开只读事务；`CommandExecutionPolicyBehavior` 先 `DemandCurrent`；Workflow 只有四条（Publish、PvpReady、Compensate、CompleteReturnToSelection，见 `ProtocolModule.cs:550/581/583/598`）；ReadOnly 注册 9 条；`ApplicationModule` 中 `AddCommandHandler<` 36 处；`SelectedSessionPositionPersistence.PersistAsync` 有环境事务时并入，没有时走 `AloneAsync` 自开边界；`AcknowledgeGameExit` 检索为 0；`RedeemGameLogin` 为 Transactional |
| Socket 层与 SelectCharacter | 端点校验在 `RegistryOutboundEncoder`（`OutboundEncoder.cs:29`），登记时由 `OutboxAdapters` 编码；`cleanupDelivery.DeliverAsync(` 共 7 处，其中 4 处在 catch 里：`OnlineUserConnectionObserver.cs:120`、`SelectCharacterHandler.cs:171/208/214`，`:208` 在补偿命令之后；关连接在工作流与补偿都结束后才请求（`SelectCharacterHandler.cs:245-252`）；`SelectCharacterLifecycleServices.cs:204` |
| 领域事件 | Reliable 在作业内依次开事务、`TryBeginAsync`、新 scope、完成标记，同一事务提交，路径上没有门；`ProjectAsync` 在可写事务内抛错（`EventService.cs:69`）；`NotificationRetry*` 检索为 0 |
| 偶发清单与其他偏差 | `PvfEquipmentDefinitionSource.cs:160` 抛 `required field is missing`；`ClientSpecStatisticHandler` 注入了 `IOnlineUserQueryDomainService`，经 `RegisterMessageHandler` 注册；`MessageHandlerRegistry.cs:73` 对遥测 handler 的非日志注入抛错；登录失败仍无法编码（`GameLoginResponseEncoder` 对失败体抛错） |
| 构建与运行 | `logs/task-31-isolated.txt:50-51` 记 Networking 188 条、52.75 秒；`logs/task-31-build-test.txt` 中 188 条那一项的总时间是 1.9021 分钟 |
| 后缀表 | `IGameplayQueue` 不以 `DomainService` 或 `DomainStore` 结尾，也不在豁免清单里，判据会拒绝它注入命令处理器 |
| 在线身份 | `Application/OnlineUsers/` 已不存在；`OnlineStateRegistry` 仍是一把 `lock (sync)` |
| 自身规矩 | 新增行里 `.rks-agent`、本轮、这一轮、F-、TASK-、T-00、RQ-、AC-0、VC-、SC-、spec-00、子 Spec 均为 0 处；`CMD` 只有一处，指 0x01 类别；「过渡」恰好出现 6 处，与 `t009-docs-and-appendices.md` §13 的 T1～T6 一一对应；F-127 写作「已知缺口」，并用「可能」限定，与 F-127 的读码推断等级相称 |
| 删除名字 | 我对 `租约`、`lease`、`执行门`、`准入门`、`重试上限`、`投递期限`、`TransactionalExecution`、`NotificationRetry`、`PersistOnTeardown`、`AccessMode`、`SelectedSessionTransitionGate`、`UserExecutionGate`、`TouchSocketNetwork`、`CloseRequests`、`close-superseded`、`retryLimit`、`groupGates` 独立检索 AGENTS.md 与 `Docs/Agent/*.md`。剩下的命中都在否定句（「没有门与租约」「没有租约」「不取租约」）或无关语境（物品「租约缓存已移除」、`EntryLease` 已移除、版本 tag）里。`Server/src` 中 `Lease` 的 22 行都是同名无关的属性或常量 |
| ArchitecturePhilosophy.md | 文件头加了一行改写记录；§1 走向图与「第二条出站路」改为登记进发件箱；§5 只改了门与管线两条；§2～§4 在 diff 中没有改动 |
| Spec 附录与 R4 | `IGameplayQueue` 的 `git grep` 计数：`c3115f9a` 为 37 行，`2bdb1afd` 为 38 行，差异恰好是 +`CommandBus`、`GameplayTimerWork` 1 → 2、−`NotificationRetryDrainService`；短 `lock` 184、11、3；托管服务注册 34 → 33；`OutboxConnectionCloser`、`HttpGameplay.cs:87/93` 的 F-33 分支、两条 `HttpGameplayTests`、VC-10 的两条证据用例都存在，且断言内容与 VC-10 所写一致；8 处 hunk 对应 R4 的 9 行（附录 A 第 3 行占两行）；R4 放在 R3 之前；RQ、AC、正文、frontmatter 与附录 C、D、E、H 都没有动 |
| VC-10「满足，附条件」 | 组合证据成立。`OutboundSenderTests.A_required_failure_stops_its_group_and_a_best_effort_failure_does_not` 证明必达失败后，组内发往另外两个连接的条目被 Skipped、这两个连接不被关闭；`PartyNotificationTests.A_failed_delivery_in_one_formation_stage_skips_every_later_stage` 证明后续阶段全部跳过；XO-6 的条目以 Required 进入同一组。第 3 句写明「整条衔接为读码」，没有越级 |
| RQ-024 | `logs/task-33-commit-points.txt`：两侧各 204 条，分布都是 T170、D21、R9、W4；事务开启点 20 → 22，两处差异的归类（`BeginSnapshot` 一对一、`SettleOnExecutorAsync` 属新系统作业）有授权单元的裁定 |
| AC-009 ①② | 195 个方法中 181 个同名、12 个不同、2 个删除，14 条都指到处置记录；我抽查了 `task-07-conflicts.md:35/39/195/371` 与 `t006-test-dispositions.md` 的 S11、S12，都相符；TASK-31 正式全量的 2 个失败是基线已知的 `DimensionDungeonTests`，不在清单内；结论写作「满足，附条件」 |
| `entry-reconciliation.md` | 只在开头新增落地状态，原文没有删除行；「`SuppressFlow` 只在三个文件」与冻结点相符 |
| `follow-ups.md` | 文首补了 TASK-33 一行；关闭、更新与新增的编号和 Result 一致；F-128 的三项都有出处 |
| 计划表外的改动 | 注册一段的三处基准漂移（`LoginGameUser` 改为 `RedeemGameLogin`、删去不存在的 `AcknowledgeGameExit`、`ApplicationModule` 也注册命令）落在 A 所属、本单元重写的段落里，都按冻结点改对了；`SelectCharacterLifecycleServices.cs:120` 改为 `:204` 也对；`OnlineUsers/` 整条删去成立 |
| 边界与行尾 | `git diff 2bdb1afd --stat -- Server Tool` 为空；`git status` 中没有 `Server/`、`Tool/` 条目，改动的文件都在 Owned boundary 内；`git -c core.whitespace=cr-at-eol diff --check` 无输出；改动和新建的文件 CR 均为 0 |
