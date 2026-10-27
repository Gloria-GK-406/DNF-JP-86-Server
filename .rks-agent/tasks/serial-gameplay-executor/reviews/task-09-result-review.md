---
kind: rks-review
target: tasks/TASK-09.md
scope: Task Unit result quick review（TASK-09 / T-004b2，Contract Revision 1 ＋「主控方裁定（执行前）」7 条，执行轮次：Result 第 1 版，未提交）
verdict: approved
date: 2026-10-22
---

# TASK-09 结果快速审查

## 审查对象与依据

- **对象**：`tasks/TASK-09.md`（契约 Revision 1、Detailed Plan、主控方裁定、Result）；工作区相对 `HEAD`（`690b9926`）的未提交改动：
  - `git diff HEAD -- Server/src`：`MessageHandlerRegistry.cs`、`NetworkMessageDispatcher.cs`（sha1 `ce0e5cfa…`）、`NetworkMessageDispatchingExtensions.cs`、`NetworkPlugins.cs`、`ProtocolModule.cs`（5 个文件，＋142 −24）；
  - `EffectOperationReplayTests.cs`（只删 1 个方法）、`spec.md` 附录 B 一行、`verification/task-07-conflicts.md` §7；
  - 未跟踪：4 个新测试文件、`verification/logs/task-09-*.txt`（7 个）。
- **依据**：`plan.md` 共享决定「请求级上下文」过渡期一条（第 58 行）与过渡期规则 1～4；`spec.md` RQ-005、RQ-008、RQ-010、RQ-011、RQ-020、RQ-025、附录 A/B/H；`tasks/TASK-08.md` Result 与「T-004b 拆分」跨单元规则；`verification/task-07-probe.md` §3.11、§5；`AGENTS.md`。
- **采用的准则**：reviewing-work 的 Task Unit 准则，加 Code 准则与 Deliverable 准则（限本单元的局部缺陷）。

## 结论

`approved`。没有未解决的阻断缺陷。请求级帧确实整体建在作业内，遥测分流与裁定第 1 条等价，Received 边界包住了整个回调，`:292` 的删除理由成立，Result 中的数字与日志一致。下面列出的非阻断项都不影响本单元通过，其中 N-2、N-4 是给 TASK-10 的输入。

## 逐项核对（对应委托的 8 个重点）

### 1. 请求级帧

- **作业体与原文逐字一致**：`HandleOnExecutorAsync` 与 `HEAD` 版 `DispatchAsync` 第 20～45 行逐字相同（diff 里这一段没有改动行），顺序是 scope → `IsBlocked` → `IsSuperseded` → `CaptureAuthority` → `Enter` → `GetHandlerType` → `CanHandle` → 账本或 `HandleAsync`。`await using` 的 scope 和 `using` 的授权帧都在这个 async 方法内建立，也在方法内结束。
- **帧不跨队列**：`GameplayExecutor.EnsureStarted` 在 `SuppressFlow` 下启动消费循环。`Job<T>.RunAsync` 与 `HandleOnExecutorAsync` 都是 async 方法，`EnterJob` 设置的令牌和各个 AsyncLocal 帧都只留在这次作业的流里，不会漏给下一个作业，也不会回流到生产者。
- **D 系测试的区分力**：
  - D3 用辅助执行器里的只读事务做调用方，Result 记录了它在 `HEAD` dispatcher 上 `Assert.Null` 失败，所以它能区分「继承调用方 AsyncLocal」和「不继承」。
  - 我另外做了变异：删掉作业内的 `NetworkCommandAuthorization.Enter`，D2 失败（`Assert.Same` 期望消息的 `Metadata`，实际为 null），其余 7 条通过。可见 D2 确实钉住了「帧在作业里」。还原证明见文末。
- **异常回传**：作业失败走 `result.TrySetException(failure)`，异常对象原样到达插件里 `await dispatcher.DispatchAsync` 的 catch，插件照旧记日志并重新抛出，抛给 TouchSocket 的仍是同一个 `OnTcpReceived` Task。D7 断言了作业内的 `IOException` 原样回到 dispatcher 的调用方。插件传 `CancellationToken.None`，执行器只在调用方令牌已取消时把 OCE 当作取消，所以 handler 抛出的 OCE 仍按失败原样传回，与今天一致。
  - `DispatchAsync` 不再是 `async`，空参数的 `ArgumentNullException` 改为同步抛出。两个插件都在 try 里调用它，生产上没有差别。
  - 生产代码里没有 `AssertNotOnExecutor` 的调用方（已 grep），所以 handler 搬进作业后不会因为粘滞的程序错误而改变结果。
  - 线级测试覆盖不足，见 N-1。

### 2. 遥测路径

- **与今天逐项等价**：今天的顺序是 `IsBlocked` → `IsSuperseded` → `CaptureAuthority` → `Enter` → 解析 → `CanHandle` → 处理。新路径去掉了 `CaptureAuthority` 和 `Enter`，前面加了效果请求检查，其余相同。`CaptureAuthority` 是加锁后的纯读（`LoginSessionRegistry.cs:37-49`），授权帧只对命令总线有意义，而遥测 handler 只注入日志，用不到命令总线。所以对两个遥测 handler 而言，可观察行为（是否调用 handler、日志内容）与今天相同。被封锁的连接不调用 handler，由 D8 覆盖；变异「去掉 `IsBlocked`」时 D8 变红（见 Result）。被顶替的分支没有单独测试，代码与今天逐字一致，不构成缺陷。
- **判据**：`NonLoggingParameters` 遍历全部公共构造器的全部参数，只放行 `ILogger` 和 `ILogger<>`。D6 是反例：注入 `ICommandBus` 的 handler 被拒，报错一次列出两个消息类型，且不误报合规的那条。这条判据在注册表构造时执行，生产中由 `MessageHandlerRegistryValidationService` 在启动时触发。
- **清单**：`HiddenCharacterInfoHandler`（只注入 `ILogger<>`，只记 Information）与 `UnansweredReportHandler`（只注入 `ILogger<>`，只记 Debug），`ProtocolModule` 中二者合计 14 条注册（grep 计数 14），与 diff 的 1 ＋ 13 吻合。被排除的 11 个 handler 我逐个看了构造器，确实注入了非日志参数：
  - `ClientSpecStatistic`、`ClientSpecProfile`、`ClientHardwareDetail`、`SessionMetrics`、`MapEntryReport` 注入 `IOnlineUserQueryDomainService`；
  - 两个 `Creature*Report` 注入 `ISelectedSessionDomainService`；
  - `SecurityStatus`、`ContentsPlayInfo`、`MercenaryInfo`、`GenCeraTicket` 注入 `IGameMessageSender`；
  - `PremiumServiceUsageReport`、`TaggedIdReport` 注入总线、门等。
  - 反方向（某个只注入日志的 handler 没有标成遥测、留在执行器上）是安全的，不需要穷尽。
- **效果请求注册为遥测时抛程序错误**：在 `DispatchTelemetryAsync` 的最前面检查，先于封锁检查，不会被封锁分支吞掉。

### 3. RQ-005 / AC-003

- **W2**：第 1 帧的 handler 异步等待一个 TCS，没有同步占住执行器线程。所以第 2 帧在 300 ms 内没有进入，只能归因于收包回调在 `await` 作业，正是 RQ-005 要的机制。Result 如实写明 W2 在旧代码上也通过，它是守护迁移后不被破坏的回归测试。
- **W3**：探针 handler 进入后先 `await Task.Delay(1)` 再退出。旧代码下两个连接的作业各自在自己的收包回调上并发执行，40 次执行中大部分区间会相交，所以「重叠计数 28～34」是合理的量级。新代码在单消费者上为 0，同时验证了各连接内的编号严格递增。这一条能区分新旧行为。
- AC-003 第 3 条（单连接待执行入站 CMD ≤ 1）按 Detailed Plan §5 留给 T-004c 对账，属于契约内的安排。

### 4. Received 回调与 `RunDetachedAsync`

- 两个 `OnTcpReceived` 都把**整个**方法体（解包、`Connections.Get`、try/catch 与 `LogError`、`throw;`、`e.InvokeNext()`）放进了 `RunDetachedAsync` 的 lambda，`finally` 恢复原来的令牌。生产代码里 `RunDetached` 的使用者只有这两处（grep 确认）。
- R1 在真实 `GameplayExecutor` 的作业里直接调用插件，断言 dispatcher 看到 `IsOnExecutor` 为假、回调返回后作业里又为真。在改插件之前，R1 的两条都是红的（见 Result）。对「边界」这个性质，这个强度已经够了。
- 对 R-5 的影响与 Detailed Plan §9 第 5 条一致：Received 在 TouchSocket 的接收循环上运行，不会在作业的发送中被内联调用。代价是：如果它真的在某个作业里被内联调用，原本响亮的自我等待报错会变成一次没有期限保护的等待（实质上是死锁）。见 N-4。

### 5. 删除冲突用例 `:292`

- 失败形态（`BeforeExplicit` 用 `ManualResetEventSlim` 同步占住执行器线程，`f.Select(2)` 经容器执行器排在它后面，5 秒后 `TimeoutException`）与代码相符。按授权「只删因本单元改动而失败的用例」，可以删除，且已逐条列出。
- **保护意图的承接成立**：
  - 这个用例想制造的窗口是「先捕获会话、再在每连接门上等待、期间换会话」。在新形状下，捕获与执行在同一个作业里，其间不会插入别的作业，所以这个窗口在执行器上已经不存在。
  - 即使在基线上，经收包回调也走不到这个窗口：RQ-005 本来就由插件的 `await` 成立，同一连接不会有两个在途请求。
  - 剩下的风险来自执行器外尚未迁入的写者（断连拆除等）在作业 await 期间换掉会话。这时由授权帧的谓词在命令时复查（`!IsBlocked && authority()`）来拦截，D2 加上我做的变异证明这个帧在作业里生效。D7 证明效果账本在作业里。`Same_ID_new_physical_context…` 与 `:371` 保留。
- 结论：由 W2、D2、D7 加上 `Same_ID…` 承接是充分的。改写成顺序断言也是一种可选的处置（TASK-06 §2.3 允许），但删除并不缺少保护。

### 6. 探针

- **(A) 归零**：我对 4 份原始记录做了核对。
  - 带 `NetworkMessageDispatcher`／`OnTcpReceived`／`EffectOperationLedger.DispatchAsync` 入口的 (A) 只剩 2 条：
    - Networking：`P2:LoginSessionRegistry`，nearest `DispatchTelemetryAsync`，栈顶 `IsSuperseded`。这是裁定第 1 条批准的遥测只读检查；
    - Application：`EffectOperationLedger.DispatchAsync` → `OnlineStateRegistry.FindByConnection`，测试 `:371`，属于顺延用例。
  - 其余命中 dispatcher 字样的都是 (B) 记录（`Already_queued_request…` 在执行器外持门，这是测试自己驱动的）。
- **§3.11 第 1 行**：四个项目里，带 `SetUserAreaHandler` 的 P1／P1b-teardown 记录为 0。Networking 剩下的 2 条 P1b-teardown（8 ＋ 5 ＝ 13 次命中）的 nearest 都是 `OnlineUserConnectionObserver.CleanupSelectedHeldAsync`，即断连拆除。这与 TASK-07 中「`OnTcpClosed` 13 次」的计数相同，而 TASK-07 中经 `SetUserAreaHandler` 的 12 次已经消失。
- Protocol 里有几条 (C) 记录的栈中带 `SetUserAreaHandler`，但它们的栈顶是 `SelectedSessionTransitionGate.CompleteAcquisitionAsync`。按 `task-07-probe.md` §5 第 1 行，这一类归冲突用例（门争用的续体），不属于本族的 §5 栈顶。Result 中「本族 (C) 栈顶为 0」的说法成立。
- **Connected/Closed「记录减少、命中略升」**：没有藏住本单元的缺陷。分离关闭继承令牌后，被读作「在执行器上」的 Closed 清理会让探针**少记**，而观测到的命中是 1191 → 1210，增加了 19。所以这组数据既不能证实、也不能量化这个盲区。见 N-2。
- **还原证明**：Result 给出的 sha1 快照与 `cmp` 我无法独立重算，因为快照在执行者的 scratchpad 里。我只能核对当前状态：`git status --short -- Server/src` 与 `git diff --stat -- Server/src` 只有本单元的 5 个文件（＋142 −24）；`Server/src` 中 `GameplayProbe`／`TestGameplay` 零命中；`IGameplayQueue` 的命中符合 Required check 7。

### 7. SQLite 忙等的中间态风险

依据代码，结论是**低概率、与基线相比增量有限，TASK-10 之前不需要缓解**：

- **执行器外的写者大多已经与入站命令串行**：`DomainEventDispatcher` 和经命令总线的 GM/HTTP、后台命令，都要取全局的 `TransactionalExecutionGate`（57，单许可 `SemaphoreSlim`），所以它们与作业里的 Transactional 命令之间不会出现「过期快照」。
- **门外的写者只剩两个 `AloneAsync`**：拆除时落位置，以及 `SetUserArea` 落位置（后者现在已在执行器上）。二者都是「只写」事务（`CharacterStateAccess.cs:159-173`，只有 `WritePosition` 与 `CommitAsync`），自己不会落入过期快照，只会让别人的快照过期。
- **触发条件**：一次拆除的提交，恰好落在某条持门命令「首次读」到「首次写」之间的几毫秒窗口里。
- **增量**：基线遇到同样的情况时，那条命令已经持着全局门忙等 30 秒，所有 Transactional 命令和事件分发都会跟着停 30 秒。本单元之后，多停的只是非事务的入站作业。
- **如果真的出现**：执行器日志会有一条「Gameplay inbound:X failed … after ≈30000 ms」或一条耗时约 30 秒的完成记录，可以据此定位。
- **成环的风险**：已核对代码，`TouchSocketNetworkConnectionCloser` 先 `Task.Yield` 再分离关闭，`ExecutorSendDeadline` 在 `SuppressFlow` 下 `Task.Run`，`IGameplayQueue` 只有 dispatcher 使用。执行器外的持门者不会等待入站作业。
- **剩余的延迟风险**：执行器外的持门者（例如拆除时持着过渡门、向对端发清理通告）在执行器外的发送没有期限。对端不读包时，在门上等待的作业会停住整个执行器，直到 socket 缓冲写满或发送完成。这与 §9 第 1 条的「只是延迟」属于同一类，通告帧很小，概率低，TASK-10 迁入拆除后消失。

### 8. 数字与耗时

- 全量：各项目的「测试总数」是 50／80／1694／475／969／155／4438／165，合计 8026 ＝ 8013 ＋ 14 － 1。通过 8004，失败只有 2 条 `DimensionDungeonTests`，跳过 2 ＋ 2 ＋ 16 ＝ 20。构建 0 警告、0 错误。日志中「direct send deadline exceeded」「did not drain」「GameplaySelfWait」都是 0 次。线级 ×10 日志里有 44 条「已通过」。以上与 Result 一致。
- 耗时比对：多出 ≥ 9 s 的 9 条（TauKingdom 1、IllusionTowerReward 1、GmEvents 2、Tournament 4、Launcher 1），每条都给出了 t06～t08 的历史值和隔离耗时（≤ 2 s），判为负载波动的依据充分。各项目总耗时基本持平。结论成立。

## 非阻断项

- **N-1　线级测试里没有一条覆盖「作业内抛异常 → 断线」**。`HandlerFailureConnectionIntegrationTests` 的 `ThrowingOnWeddingResponse` 装饰器在**调用内层 dispatcher 之前**同步抛出，异常发生在执行器外，走不到作业的失败路径。全量里另外 3 次真实的作业失败（`SelectCharacterCompensationCharacterizationTests` 2 次、`GameSellItem` 1 次）断开连接，靠的是 handler 自己显式关闭，不是插件把异常抛给 TouchSocket。语义今天由结构保证（异常对象原样回到插件、插件逻辑未改）加上 D7 承担，所以不阻断。T-008 处理双日志去重时，建议补一条「探针 handler 在作业里抛异常 → 无回包、连接关闭、其他连接照常」的线级测试，把这条语义钉在新形状上。
- **N-2　对 3.1 Connected/Closed 变化的解释与数据不符**。Result 写的是这组变化「符合 §9 第 4 条」，但第 4 条预言的是命中**下降**（继承令牌后，探针看不见那段清理），实际命中是上升。准确的说法应当是：这组计数被栈形变化与波动主导，量化不了继承令牌造成的盲区。TASK-10 修正关连接器（`SuppressFlow` 和 Closed 的 `RunDetached`）之后，应当以那次复跑作为 Closed 族的干净基线，不要把 164／1210 当作基线。
- **N-3　spec 附录 B 这一行自相矛盾**。第一列的举例仍然写着「例如客户端规格、会话指标」，第二列的注却说它们按判据不属于本行。裁定第 6 条只授权填清单，所以保留原举例是合规的。建议在 T-009 修正 AGENTS.md 措辞时，一并把第一列的举例改掉。
- **N-4　Received 边界的代价是一种潜在死锁**（Detailed Plan §9 第 5 条已承认，这里登记为残余风险）：如果 TouchSocket 在某个作业里内联调用 Received，自我等待会变成没有期限的死锁，而不是一次 2 秒以内的等待。TouchSocket 4.3.1 的接收模型不会这样做。今后升级 TouchSocket，或者引入在作业里驱动接收的测试宿主时，需要重新核实这一点。
- **N-5　被顶替连接的遥测分支没有测试**（裁定只要求测被封锁的分支）。代码与今天逐字一致，可以不补。

## 审查者的临时实验与还原证明

- 实验：备份 `NetworkMessageDispatcher.cs`（sha1 `ce0e5cfa69c6bba274ecb0851f30e443e9026242`）到 scratchpad，删掉作业内的 `using var authorization = …Enter(…)`，跑定向测试 `GameplayDispatchTests|EffectDispatchOnExecutorTests`：失败 1（D2，`Assert.Same` 得到 null），通过 7。
- 还原：用备份覆盖回去，sha1 仍为 `ce0e5cfa69c6bba274ecb0851f30e443e9026242`，`REVIEW-MUTATION` 标记 0 命中。`git status --short` 与审查开始时相同（8 个已修改文件、12 个未跟踪文件，Server/src 只有本单元的 5 个文件）。之后重新构建 `Dfo.Server.Application.Tests`（Release）：0 警告、0 错误，二进制已与还原后的源码一致。
- 没有 commit，没有 stash，没有改动其他仓库文件（只写了本审查文件）。

```yaml
kind: rks-review
verdict: approved
```
