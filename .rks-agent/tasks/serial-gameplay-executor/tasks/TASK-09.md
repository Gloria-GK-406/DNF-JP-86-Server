# TASK-09 — T-004b2：入站协议与效果账本迁入执行器

基准提交 `690b9926`（TASK-08 之后）。边界提纲见 `TASK-08.md` 文末「T-004b 拆分」§2 TASK-09 一行，以及 §1 的跨单元规则；本文件不重抄，只写本单元的具体做法。

## Task Unit Contract

- Revision: 1
- Outcome and references:
  - **dispatcher 入队并等待**：`NetworkMessageDispatcher.DispatchAsync` 把一条入站消息的整段处理作为一个执行器作业入队，并等待它完成。收包回调本来就按连接逐条 `await` 插件，所以同一连接的下一条消息要等上一条的作业完成才会被解包和入队（RQ-005，入站背压自然成立）。
  - **请求级帧整体移进作业**（Plan 共享决定「请求级上下文」过渡期一条、TASK-06 待办 3b）：DI scope、`IsBlocked`、`IsSuperseded`、`CaptureAuthority`、`NetworkCommandAuthorization.Enter`、handler 解析与 `CanHandle`、效果账本 `EffectOperationLedger.DispatchAsync`，原样、按原顺序在作业里执行。附录 H 第 1、2 行在过渡期的形态由此达成：位置在执行器上、CMD 开始时；改为显式数据仍归 T-008。
  - **纯遥测 handler 留在执行器外**（TASK-06 待办 3d）：按 handler 注册元数据在入队前分流，判定标准为 RQ-020「只能注入日志」，并在启动时由注册表强制检查。清单补进 spec 附录 B。
  - **Received 回调是流边界**（TASK-06 待办 3f 的 Received 部分）：Game 与 Channel 的 `OnTcpReceived` 整体在 `GameplayJob.RunDetachedAsync` 里执行。
  - **`SetUserAreaHandler` 的门外写**（TASK-06 待办 5a 之一）：随入站进入执行器，生产代码不改，处置与证据见 Detailed Plan §6。
  - **冲突用例**：`EffectOperationReplayTests` 的 7 条，在本单元失败的执行处置，仍然通过的顺延 T-004c（§8）。
  - 覆盖：附录 A 第 1、2 行（入站部分）；RQ-005；RQ-008（入站一侧的等待关系）；RQ-010、RQ-011（入站部分）；RQ-020 的纯遥测例外；RQ-025 的过渡期形态；附录 B 的纯遥测一行；附录 H 第 1、2 行的位置。AC-003 的前两条线级测试在本单元完成。
- Owned boundary:
  - 生产代码，只限：
    - `Server/src/Dfo.Server.Application/Dispatching/`：`NetworkMessageDispatcher.cs`、`MessageHandlerRegistry.cs`、`NetworkMessageDispatchingExtensions.cs`；
    - `Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs`：只改两个 `OnTcpReceived`；
    - `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`：只把 §4 清单中的 14 条注册改为遥测注册。
  - `EffectOperationLedger`、`SetUserAreaHandler`、`CharacterStateAccess`、`TouchSocketNetworkConnectionCloser` 以及执行器组件**不改**。
  - 测试：
    - 新增 `Server/tests/Dfo.Server.Application.Tests/Dispatching/` 下的 dispatcher 与注册表测试；
    - 新增 `Server/tests/Dfo.Server.Networking.Tests/` 下的线级测试与 Received 流边界测试；
    - 按 §7 处置的既有测试文件。
  - 文档：
    - `spec.md` 附录 B「纯遥测 handler」一行，只填清单（有 TASK-02 填附录 A 统计结果的先例）；
    - `verification/` 下：`logs/task-09-*.txt`；`task-07-conflicts.md` 追加一节（只在出现 §7 第 (ii)、(v) 类新冲突时）。
- Required checks:
  1. `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 警告，0 错误。
  2. **全量**：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-09-build-test.txt`。
     - 总数 ＝ 8013 ＋ N（本单元新增）－ D（本单元删除），各项目与 TASK-08 Result 的计数逐项对照，写出加法；
     - 失败只能是那 2 条 `DimensionDungeonTests`；跳过 20（若 D 中含跳过用例，另写加法）；表征测试 8 条通过、2 条跳过；
     - 没有挂起转储。
     - **耗时比对**（TASK-08 审查 N-2）：与 `logs/task-08-build-test.txt` 逐条对比，列出多出 ≥ 9 秒的用例，逐条判定是负载波动、执行器释放时的 10 秒排空，还是作业里的 SQLite 忙等；同时写出各项目的总耗时对照。排空或忙等造成的，按 §7 处置，必要时给该测试容器加日志提供程序以取得「did not drain within」的证据。
  3. **隔离复跑**：Application.Tests、Networking.Tests、Protocol.Tests、Host.Tests 各一次，落盘 `logs/task-09-isolated.txt`。Networking 与 Host 中任何超时形态的失败，先按 AGENTS.md「满载假红」的规则隔离复跑，再归类。
  4. **新增的线级测试**（§5 的 W1～W4）：隔离连续 10 次，全量负载下 1 次，全部通过，落盘 `logs/task-09-wire-x10.txt`。
  5. **表征测试**：8 条通过、2 条跳过，断言未改。
  6. **测试改动核对**：
     - 新增测试逐条列名，N 写出加法；
     - §7 处置过的每个既有测试文件，写明类别与理由；只改包裹方式的文件，用 TASK-07 的差异核对脚本证明方法集合与断言行多重集合不变；
     - 删除或改写的冲突用例，写成「旧断言 → 新断言 → 保护意图」；顺延的冲突用例逐条写明「本单元仍通过」的运行证据。
  7. **生产改动的边界**：
     - `git diff --stat -- Server/src` 只含 Owned boundary 所列文件；
     - `grep -rn "IGameplayQueue" Server/src`（排除 bin/obj）：命中只在 `Abstractions/Execution`、`Application/Execution` 与 `Application/Dispatching/NetworkMessageDispatcher.cs`；
     - `grep -rn "RunDetached" Server/src`：除定义外只有 `NetworkPlugins.cs` 的两个 `OnTcpReceived`；
     - 部分迁移期间的不变量（拆分原则第 3 条）：列出 `INetworkMessageDispatcher.DispatchAsync` 与 `EffectOperationLedger.DispatchAsync` 在执行器外的全部调用方，以及会等待收包回调结束的关闭调用（检索 `CloseAsync(`），逐个确认没有「持门等待入站作业」。
  8. **探针复跑**（`verification/probe-tools/`，授权见下）：
     - 项目：Application、Protocol、Host、Networking；
     - 本族归零：入口帧为 `NetworkMessageDispatcher.DispatchAsync`（含其 lambda）、`GameNetworkPlugin.OnTcpReceived`、`ChannelNetworkPlugin.OnTcpReceived`、`EffectOperationLedger.DispatchAsync` 的 (A) 记录为 0；`task-07-probe.md` §3.11 第 1 行（`SetUserAreaHandler` 路径的 P1b-teardown 12 次、P1 17 次）为 0；§5 中归入本族的 (C) 栈顶（`ClientMessageHandler<T>.HandleAsync` 等四个、`SelectDungeonHandler.HandleMessageAsync`）为 0；
     - 残留记录只允许来自 §8 中顺延的冲突用例，并用排除复跑证明（同 TASK-07 的 x 轮做法）；排除后仍有的，按缺陷处理；
     - 本族以外各族的 (A) 计数，与 `task-07-probe.md` §2 并列写出，作为 TASK-10～13 的输入（下降的要说明原因，见 §9 第 4 条）；
     - 原始记录用 `PROBE_LOG_PREFIX=task-09-probe` 落盘为 `logs/task-09-probe-<Project>.txt`；
     - 还原证明三项同 TASK-07 Required check 6：sha1 快照逐字节相等；`git diff --stat -- Server/src` 与 `git status --short -- Server/src` 只剩本单元的正式改动；之后先 `--no-incremental` 重新构建，再跑正式全量。
  9. **对账行**：附录 A 第 1、2 行与附录 H 第 1、2 行，各写一行「来源 → CMD 类型 → 证据（测试名或探针计数）」，标明哪一部分留给 T-008。
- Dependencies: TASK-08（容器执行器注册、直发 2 秒期限、探针工具、§4 复核表中「TASK-09 再看」的各项、Remaining 中的 N-2 耗时比对要求）；TASK-04 的执行器、调用链与 `RunDetached`；TASK-06/07 的 `TestGameplay` 与冲突总清单。
- Result review: required。
  - 所有入站协议从这一单元起经执行器，后续 TASK-10～14 和 T-004c 都建立在这个形状上。dispatcher 里帧的顺序或作用范围错了（例如把授权帧建在作业外、作业里看不到），保护会静默失效：多数用例只走「没有帧就放行」的分支，全量测试不一定变红。
  - 遥测分流靠注册元数据和人工判定；分错一条，就会让一个改状态的 handler 留在执行器外，而这个漏迁只有探针能看出来。
  - 中间态的风险（作业在门上等执行器外的持有者、执行器上的 SQLite 忙等会拖住全部入站、分离关闭继承活作业令牌）只在真实 socket 的负载下显形，靠测试结果的判读，不只靠通过数。
  - 冲突用例的「顺延」判断和 §7 的失败分类都靠人判断，判错会在 T-004c 以挂起的形式重现。
- Task-specific authorization:
  - 探针复跑（Required check 8）期间，允许临时改动 `Server/src`，用后按三项还原证明；探针改动不提交，不 `git stash`。
  - 只允许删除或改写「因本单元的改动而失败或挂起」的用例，每条在 Result 中列出；其余冲突用例保持原样，留给所属单元。

## Detailed Plan

### 1. dispatcher 的新形状

`NetworkMessageDispatcher` 增加注入 `IGameplayQueue`。注册已经由 `AddNetworkMessageDispatching` → `AddCommandDispatching` → `AddGameplayExecution` 保证（TASK-08）；测试里经 `ActivatorUtilities` 构造的装饰器（`HandlerFailureConnectionIntegrationTests` 等）由 DI 补上这个参数。

```
DispatchAsync(context, message, ct):
  空值检查
  if registry.TryGet(message.MessageType, out reg) && reg.IsTelemetry:
      return DispatchTelemetryAsync(...)          // §4，执行器外
  return queue.RunAsync(new GameplayWork(
      GameplayShardKey.World,
      GameplayCallChain.NewRoot(GameplayCallSource.Connection, context.Connection.ConnectionId),
      CommandTypeOf(message.MessageType),          // "inbound:<ClientMessageType>"
      token => HandleOnExecutorAsync(context, message, token)), ct)
```

- **分流不得改变「封锁先于查表」的顺序**。注册表查不到的消息类型照旧入队，在作业里先过 `IsBlocked`，再由 `GetHandlerType` 抛出 `KeyNotFoundException`。`NetworkMessageDispatcherTests.Blocked_physical_connection_stops_before_handler_lookup…` 钉的就是这个顺序。所以分流用 `TryGet`，不用会抛异常的查表。
- **CMD 类型**：`inbound:<ClientMessageType 的枚举名>`，例如 `inbound:GameLogin`、`inbound:ChannelConnect`。它在注册表构造时为每个已注册类型预先算好；未注册的类型现场拼接。
- **调用链**：每条入站消息都是一个新根，来源是 `Connection`，`SourceConnection` 是该连接的 ID。父子关系归 T-007。
- **分流键**：`GameplayShardKey.World`。
- **等待**：`await` 作业的完成。作业的异常原样回到插件，插件照旧记 `Game/Channel handler failed` 并重新抛出，客户端可见的行为与今天相同。执行器自己也会记一条 `Gameplay inbound:X failed`（Error），所以过渡期内一次 handler 失败会有两条 Error 日志（见「待裁定」第 4 条）。
- **令牌**：插件传 `CancellationToken.None`，作业体拿到的也是它。

`HandleOnExecutorAsync` 就是今天 `DispatchAsync` 第 20～45 行的原文，顺序不变：建 scope → `IsBlocked` → `IsSuperseded` → `CaptureAuthority` → `NetworkCommandAuthorization.Enter`（含反射取 `Metadata`）→ 解析 handler → `CanHandle` → 效果请求走 `EffectOperationLedger.DispatchAsync`，否则直接 `HandleAsync`。`await using` 的 scope 和 `using` 的授权帧都在作业里结束。

**为什么整段都放进作业**：作业在执行器的消费循环里运行，这个循环启动时已经 `SuppressFlow`，生产者的 `ExecutionContext` 不会跟进来。如果把帧建在作业外，handler 里的 `commandBus.SendAsync`（过渡期仍按旧语义在调用方流里直接执行）就看不到授权帧和效果帧，于是走「没有帧就放行」的分支，保护静默失效。帧全部建在作业里，handler 和它内联执行的命令都在同一条流上，与今天一致。

**self-wait**：在同一个执行器的作业里调用 dispatcher，会由 `GameplayExecutor.RunAsync` 报 `GameplaySelfWaitException`。生产代码里不存在这种调用（只有两个插件调用 dispatcher）；测试里出现时按 §7 (iii) 处理。

### 2. 请求级帧与效果账本在作业里的情况

- 四个帧的持有者 `SelectedSessionSafety`、`LoginSessionRegistry`、`NetworkCommandAuthorization`、`EffectOperationContext` 都是单例，AsyncLocal 帧建在作业流上，作业内联执行的命令都能看到，作业结束即释放。
- `EffectOperationLedger.DispatchAsync` 一字不改，只是调用它的地方换成了作业。它那把每连接的 `SemaphoreSlim`（附录 F 删除栏，归 T-005）保留：
  - 生产中，RQ-005 保证同一连接的效果请求不会同时在途，这把门总是空闲的；
  - 只有测试在执行器外直接调用账本、而且一直占着这把门时（`:371`），作业才会在门上等待。这属于拆分原则第 3 条允许的「只是延迟」。
- 重放（`delivery.RefreshAsync`）在作业里直接发送，受 TASK-08 的 2 秒期限约束。
- 帧的反面：作业**不再继承调用方的 AsyncLocal**，包括环境事务、门的借用帧、调用方自己建的效果帧和授权帧。生产中 dispatcher 只被收包回调调用，而收包回调身上没有这些帧，所以不影响生产。测试中经真实 dispatcher、又依赖外层帧的用例，按 §7 (v) 处理。

### 3. 注册表元数据

- `MessageHandlerRegistration` 加一个带默认值的位置参数 `bool IsTelemetry = false`。既有的 `new MessageHandlerRegistration(type, handler)` 写法不受影响。
- `NetworkMessageDispatchingExtensions` 新增 `RegisterTelemetryMessageHandler<THandler>(ClientMessageType)`，与 `RegisterMessageHandler` 的区别只有这个标记。
- `MessageHandlerRegistry`：
  - 新增 `TryGet`（或等价的成员），返回 handler 类型、遥测标记与预先算好的 CMD 类型；`GetHandlerType` 保持不变，因为它有 3 处测试调用；
  - 构造时增加一项检查：标为遥测的 handler 类型，每个公共构造器的每个参数都必须是 `ILogger` 或某个 `ILogger<T>`，否则启动即抛，并一次列出全部违规项（照「闭合性」检查的写法）。这条检查就是 RQ-020「只能注入日志」的判据，配一条反例测试。
- 同一消息类型不能既是普通注册又是遥测注册，现有的重复检查已经覆盖这一点。

### 4. 纯遥测 handler

**判定**：handler 的构造器只注入日志（RQ-020），据此可推出它不改状态、不写库、不发包。这条由 §3 的启动检查强制，不靠人记。

**代码检索结果**（基准 `690b9926`，`ProtocolModule` 共 173 条 `RegisterMessageHandler`，涉及 142 个不同的 handler 类型）——只注入日志的有 2 个类型，共 14 个消息类型：

| handler | 消息类型 | 条数 |
|---|---|---:|
| `HiddenCharacterInfoHandler` | `GameHiddenCharacterInfo` | 1 |
| `UnansweredReportHandler` | `GameUseSkill`、`GameSyncItemSpace`、`GameOverflowInfo`、`GameLogDomainConnect`、`GameLogConnectProcess`、`GameCharacterStatistic`、`GameRequestBlacklist`、`GameFarmEventAction`、`GamePi`、`GameRequestIgaVersion`、`GameFpsPerformanceStatistic`、`GameFrameLagPerDungeon`、`GameLagStatistics` | 13 |

加法：1 ＋ 13 ＝ 14。执行者在改注册前用同一检索复核一次，以复核结果为准。

**名字像遥测、但不进附录 B 的**（构造器除日志外还注入了别的，所以进执行器）：

- `ClientSpecStatisticHandler`、`ClientSpecProfileHandler`、`SessionMetricsHandler`、`MapEntryReportHandler`：注入 `IOnlineUserQueryDomainService`，读在线身份，用于日志归属；
- `CreatureResponseReportHandler`、`CreatureScriptMessageReportHandler`：注入 `ISelectedSessionDomainService`；
- `SecurityStatusHandler`：会发包；`PremiumServiceUsageReportHandler`、`TaggedIdReportHandler`：会发命令或发包。

AGENTS.md 把 `ClientSpecStatisticHandler` 称作「纯遥测」，指的是它不经命令总线。按 RQ-020 的判据它不属于附录 B。文档修正归 T-009。

**执行器外的路径**（`DispatchTelemetryAsync`）：

- 建 scope → 解析 handler → `CanHandle` → `HandleAsync`。
- 若消息实现了 `IEffectSettlementRequest`，抛出程序错误：效果请求不可能是遥测。
- **不执行** `IsBlocked`、`IsSuperseded`、`CaptureAuthority` 与授权帧。这几项都是读玩法状态（`SelectedSessionSafety`、`LoginSessionRegistry`），留在执行器外会让本族探针 (A) 无法归零；而遥测 handler 不能注入任何领域服务，用不到授权。
- 代价：被封锁或被顶替的连接发来的遥测帧，今天被静默丢弃，改动后会照常记一条 Debug/Information 日志。线上字节与状态都不变。这一条作为待裁定第 1 条交主控方确认。
- 顺序：遥测仍在收包回调里执行。前一条入站作业完成之后，收包回调才会处理下一帧，所以遥测与本连接其他消息的相对顺序不变。

**附录 B**：把上表 14 个消息类型与 2 个 handler 填进「纯遥测 handler」一行，安全理由引用 §3 的启动检查；被排除的那几个 handler 及排除理由，作为该行的注释写上。

### 5. 新增测试

**dispatcher 与注册表**（`Application.Tests/Dispatching/`，用 `AddNetworkMessageDispatching` 自建容器，不包裹）：

| 条 | 情形 | 断言 |
|---|---|---|
| D1 | 普通消息经真实 dispatcher | handler 内 `GameplayJob.IsOnExecutor` 为真；`GameplayJob.Current` 的来源为 `Connection`，`SourceConnection` 是该连接，CMD 类型为 `inbound:ChannelConnect`；跑在容器执行器上（运行后 `IsStarted`） |
| D2 | 请求级帧在作业里 | handler 内 `NetworkCommandAuthorization.Metadata` 等于消息的 `Metadata`；handler 经真实总线发的命令能看到授权帧（例如封锁后 `DemandCurrent` 抛 `UnauthorizedAccessException`，参照 `Already_queued_request…` 的写法） |
| D3 | 调用方流上的帧不进作业 | 调用方在辅助执行器的作业里开一个环境事务（`DomainTransactionScope.Begin()`），在事务内 dispatch；handler 内 `IDomainTransactionAccessor.Current` 为空。今天的形状下它会看到调用方的事务，所以这条能区分新旧行为。只读事务即可，避免撞写锁 |
| D4 | 在容器执行器的作业里调用 dispatcher | `GameplaySelfWaitException` |
| D5 | 遥测消息 | handler 内 `IsOnExecutor` 为假；容器执行器未启动（`IsStarted` 为假，前提是此前没有别的作业） |
| D6 | 遥测注册的 handler 构造器注入了非日志参数 | 构造注册表时抛异常，消息列出该 handler 与消息类型（反例） |
| D7 | 效果请求经真实 dispatcher（`ShopFixture` ＋ `EffectRequestTransport`） | 发送替身 `BeforeExplicit` 里记录到 `IsOnExecutor` 为真、CMD 类型以 `inbound:` 开头；同序号重发（重放）同样在执行器上，且不二次扣除 |

D7 可以放在 `Sessions/` 下，与 `EffectOperationReplayTests` 共用夹具。

**线级**（`Networking.Tests/Integration/`，真实 TouchSocket 宿主、真实插件与 dispatcher；搭法参照 `HandlerFailureConnectionIntegrationTests`；替身 handler 通过移除对应消息类型的 `MessageHandlerRegistration` 描述符再注册来接入，解包器沿用 `ProtocolModule` 的，也可以自建一个只含 `NetworkingModule` 与 `AddNetworkMessageDispatching` 的最小宿主，由执行者选择）：

| 条 | 对应 | 做法与断言 |
|---|---|---|
| W1 入口证据 | 拆分原则第 6 条；AC-006 对账 | 一个 Game 连接、一个 Channel 连接各发一条普通消息；替身 handler 记录到 `IsOnExecutor` 为真、来源为 `Connection`、CMD 类型为 `inbound:<类型>`、`SourceConnection` 等于服务端登记的连接 ID |
| W2 同连接顺序 | AC-003 第 1 条 | 同一连接连发两帧；第 1 帧的 handler 异步等待一个 TCS。放行前，第 2 帧的 handler 没有进入（观察窗口 300 ms 起，执行者按稳定性调整并写明）；放行后记录序列为「1 进、1 出、2 进、2 出」 |
| W3 两连接交替 | AC-003 第 2 条 | 两个连接交替各发 N 帧（N ≥ 20，帧序号或 body 中带编号）；全部处理完毕，各连接内的编号严格递增；任何两次 handler 执行的区间不重叠（进出计数器），这同时是「真实入站路径上串行」的证据 |
| W4 遥测留外 | 附录 B | 同一连接先发一条普通消息、再发一条遥测消息；遥测替身（用 `RegisterTelemetryMessageHandler` 注册）记录到 `IsOnExecutor` 为假，且观察到前一条普通消息的作业已经完成 |

AC-003 第 3 条（持续灌包时待执行入站 CMD ≤ 1）在结构上由「收包回调 await 作业」直接保证，本单元不单独测，留给 T-004c 的对账。

**Received 流边界**（`Networking.Tests/Runtime/`）：

- R1：在一个真实 `GameplayExecutor` 作业里直接调用插件的 `OnTcpReceived`，使用记录型 dispatcher。断言 dispatcher 被调用时 `IsOnExecutor` 为假，回调返回后作业里的 `IsOnExecutor` 恢复为真。
- `ITcpSession` 与 `ReceivedDataEventArgs` 的替身由执行者构造（例如用 `DispatchProxy` 生成 `ITcpSession`）。若在 TouchSocket 4.3.1 上构造不出可用的替身，改为在 Result 中说明，并以代码与 grep 作为证据。不能静默跳过。

**`SetUserAreaHandler`（可选）**：复用 Networking 表征里的 `CharacterizationSteps.TwoPlayersInTownAsync` / `MoveToSharedAreaAsync`，用装饰器包住 `ISelectedSessionPositionPersistence`，记录 `PersistAsync` 时 `IsOnExecutor` 为真。搭建成本高时可以省略，必需的证据是 Required check 8 中 §3.11 那一行归零。

先写测试再改生产代码：D1、D5、D6、W1、W4、R1 在改动前应当红，Result 中记录红的形态。

### 6. `SetUserAreaHandler` 的门外写

- **现状**：TASK-03 记下的问题是「全局门外的自开事务写」。`SetUserAreaHandler` 在 `SelectedSessionTransitionGate` 内，经 `SelectedSessionPositionPersistence.PersistAsync` 落位置；这时没有环境事务，于是走 `AloneAsync`，自己 `new DomainTransaction()` 并提交（`CharacterStateAccess.cs:159` 附近）。这次写入在 `TransactionalExecutionGate`（57）与 `UnitOfWorkBehavior` 之外，可能与正在提交的 Transactional 命令撞上 SQLite 写锁，等满 30 秒。
- **随入站进入执行器之后**：
  - handler 在作业里，这次 `AloneAsync` 写也在执行器上；
  - 它**仍在**全局门和工作单元管线之外，本单元不把它放进门里：放进去会改变提交方式（违反「纯搬迁」），而门本身在 T-005 删除；
  - 它与其他入站作业里的 Transactional 命令从此串行，不再可能撞锁；
  - 残余：它仍可能与执行器外尚未迁入的写者撞锁。这些写者是：断连拆除的 `AloneAsync`（`OnTcpClosed`，TASK-10）、后台服务（TASK-11）、事件分发（TASK-12）、GM/HTTP（TASK-13）。这与基线相同，不是新风险；但撞上时，等待的是整个执行器，而不只是一条连接，见 §9 第 2 条。
- **处置**：不改代码。
  - 证据：探针 §3.11 第 1 行归零（Required check 8），可选测试见 §5；
  - 提交点：`AloneAsync` 是今天独立的提交点，按 RQ-024 在 CMD 内仍是独立的工作单元，T-008 归并时写入附录 C；
  - 判据：T-004c 的写工作单元判据要覆盖裸 `new DomainTransaction()`，不能只覆盖 `DomainTransactionScope.Begin`，否则这条与拆除那条都看不见（`task-07-probe.md` §1.1 的 P1b 已经说明这一点），在 Result 的 Remaining 中交给 T-004c。

### 7. 测试侧的失败分类与处置

沿用 TASK-08 §4 的 (i)～(iv)。另外加一类本单元特有的 (v)，并按本单元调整 (iii)：

| 类 | 判定 | 处置 |
|---|---|---|
| (i) | 冲突总清单里已有的用例 | 本单元执行其处置（§8） |
| (ii) | 不在清单里，靠两个发起方交错制造并发 | 按 TASK-06 §2.3 判为冲突：改写为顺序断言或删除，追加到 `task-07-conflicts.md` 新的一节「TASK-09 新增」，更新总数加法 |
| (iii) | 带容器的包裹区域（`RunAsync(f.Services, …)`、`Run(provider, …)`，或经夹具内联到容器执行器）里调用了真实 dispatcher 或 `EffectRequestTransport.Send`，报 `GameplaySelfWaitException` | 把入口调用移出包裹，断言不变（TASK-06 §2.4） |
| (iv) | dispatcher 改动本身的缺陷 | 在 Owned boundary 内修正，并补测试 |
| (v) | 包裹区域里开着环境事务、门或帧，再经真实 dispatcher 下发，而断言依赖作业继承这些 AsyncLocal（典型表现：SQLite 忙等 30 秒、`already closed`、该放行的被放行） | 断言不依赖继承的，先结束外层事务或帧，再在区域外下发；依赖继承的，按 (ii) 判为冲突 |

**主动复核**（即使全绿也逐项写结论）：

- **调用 dispatcher 且用到 `TestGameplay` 的 15 个文件**（检索 `INetworkMessageDispatcher|EffectRequestTransport|.DispatchAsync(` 再与 `TestGameplay` 取交集）：
  - Application.Tests 的 `HousingNetworkSettlementTests`、`EffectOperationReplayTests`、`EpicCraftTests`、`WarehouseStorageTests`、`EquipmentReinforcementHandlerTests`、`EquipmentAmplificationTests`、`EquipmentQualityTests`、`TeleportConsumableTests`、`HerasEntryCouponTests`、`EquipmentSocketTests`、`MagicSealHandlerTests`、`EquipmentDisjointTests`、`NetworkMessageDispatcherTests`、`JoustIntegrationTests`、`VendingMachineTests`；
  - 逐个确认 dispatcher 调用不在带容器的包裹里，整方法包裹（辅助执行器）里的 dispatcher 调用没有依赖外层帧。
- **TASK-08 §4 复核表中标「TASK-09 再看」的两项**：
  - Protocol 5 个文件（LeaveDungeon、PartySelectDungeon、MonsterDeath、QuestClearVertical、Joust）：带容器的包裹区域内是否经真实 dispatcher；
  - Networking 真实 socket 用例中的同步短作业（GameLogin 6 处、IllusionTowerStage 4 处、CardRewardReconnect 4 处、BossTower 3 处）：它们现在会排在入站作业之后。确认测试线程在等这些短作业时，没有入站作业在等测试线程的下一步动作。
- **`NetworkMessageDispatcherTests.Already_queued_request…`**：测试在执行器外持有 `TransactionalExecutionGate`，入站作业会在 57 号行为上等待。这是允许的「作业等执行器外持有者」。确认测试放门不依赖作业先完成。

**两种需要额外留意的形状**（沿用 TASK-08）：

- 在容器执行器的作业里释放这个容器，会报自我等待，按 (iii) 处理；
- 容器释放时执行器上仍有被卡住的作业，会多等 10 秒，见 Required check 2 的耗时比对。

### 8. 冲突用例 7 条

行号是 `task-07-conflicts.md` 的基准行号（`0223e646`）。TASK-07 改动后，当前文件里的方法声明行依次后移到 :163、:233、:257、:273、:296、:353、:379，**以方法名为准**。

| 基准行 | 方法 | 预判（依据代码阅读） | 处置 |
|---|---|---|---|
| :159 | `An_inherited_closed_operation_cannot_downgrade_to_trusted_direct_bus` | `BeforeExplicit` 在作业里的发送中被调用，`Task.Run` 子任务继承作业流上的效果帧；作业结束后帧关闭，子任务经旧语义总线执行，仍然得到 `already closed`。**预计仍通过** | 通过则顺延 T-004c：翻转后，执行器外的总线调用会入队一个没有帧的新作业，前提不再成立 |
| :229 | `Postdurable_operation_record_failure_preserves_typed_cause_and_exact_connection_fence` | 不经 dispatcher，只在执行器外 `Enter` 帧后经总线。**预计仍通过** | 顺延 T-004c（同提纲） |
| :253 | `Invalid_projection_freeze_aborts_real_SQL_before_durability_and_records_confirmed_rollback` | 同上 | 顺延 T-004c |
| :269 | `An_inherited_readonly_scope_does_not_clear_the_primary_execution_on_the_parent_flow` | 只操作 `EffectOperationContext`，不经 dispatcher、账本与总线。**预计仍通过** | 顺延。它测的是跨流的 AsyncLocal 帧语义，翻转可能也不会让它失败；真正让它失效的是 T-008 把帧改为显式数据。见待裁定第 3 条 |
| :292 | `Waiting_on_the_connection_gate_keeps_the_original_selected_session` | **预计失败**：第一次下发的作业在 `BeforeExplicit` 里用 `ManualResetEventSlim` 同步阻塞执行器线程；`f.Select(2)` 经 `TestGameplay.Run(provider, …)` 排在它之后，测试线程同步等待，直到 5 秒后 `TimeoutException`。另外，选中会话的捕获已经移到作业开始时，测试想制造的「先捕获、再在门上等、期间换会话」这个窗口已经不存在 | **删除**。旧断言：在每连接门上等待的第二个请求保持原来捕获的会话，换会话后被拒、不扣。保护意图（旧请求不得绑定到替换后的角色）改由三处承担：RQ-005（同一连接的下一条入站在上一条 CMD 完成后才开始，W2 证明）；捕获发生在作业开始时（D2、D7）；`Same_ID_new_physical_context…` 与 `:371`。若实际运行与预判不同（仍通过），改为顺延并在 Result 说明 |
| :345 | `Unknown_applied_effect_change_is_rejected_before_SQL_commit` | 同 :229 | 顺延 T-004c |
| :371 | `Refusal_only_unselected_operation_cannot_pay_after_a_selection_arrives` | 测试在执行器外直接调用账本；`Clear` 与 `Select` 是容器执行器上的短作业，执行器没有被占住。**预计仍通过** | 顺延 T-004c：翻转后，账本回调里的总线调用会入队一个不带拒绝帧的新作业。它的探针 (A) 记录（入口帧 `EffectOperationLedger.DispatchAsync`）是 Required check 8 允许的残留，须由排除复跑证明 |

预计 D ＝ 1（`:292`），顺延 6 条。以实际运行为准。顺延的每条都在 Result 中给出本单元全量中通过的记录。

### 9. 中间态的风险与等待关系

1. **作业在门上等待执行器外的持有者**：观察者（`OnTcpConnected` / `OnTcpClosed`）、后台服务、事件分发器、GM/HTTP、登录会话维护都还在执行器外，会持有过渡门、在线用户门、账号门、`TransactionalExecutionGate`、队伍信号量。入站作业等它们，只是延迟。成环的条件是：执行器外的持有者一边持门，一边等待「需要入站作业完成」的东西，也就是 dispatcher 本身，或者某条连接收包回调的结束（TouchSocket 关闭连接时会等它）。代码事实：
   - dispatcher 只被两个插件调用；
   - 关连接一律是分离的：`TouchSocketNetworkConnectionCloser` 用 `Task.Yield` 后台关闭，`ExecutorSendDeadline` 在 `SuppressFlow` 下关闭；
   - `TcpServices.DrainingSession` 的关闭本身就在关闭回调里；
   - `OutboundSender` / `OutboundTransport` 尚未接线。
   - Required check 7 复核这几条，写进 Result。
2. **执行器上的 SQLite 忙等会拖住全部入站**：作业里的写与执行器外写者（§6 所列）撞上「先读后写」的过期快照时，会等满 30 秒。基线上这只拖住一条连接；现在整个执行器停 30 秒，Networking 与 Host 中 10 秒预算的用例可能因此变红。判读与处置：
   - 先隔离复跑；
   - 复跑仍红，就用执行器的失败日志（耗时约 30000 ms）和测试日志定位那个执行器外的写者；
   - 不加门、不加重试；
   - **若某条用例确定性地变红**，停下回报主控方。可选方向：把对应写者所属单元的迁移提前，例如拆除写归 TASK-10；或者在测试层调整。不在本单元自行选择。
3. **直发期限开始对生产生效**（VC-8 提前，TASK-08 Remaining 4f）：入站作业里的直接发送，遇到 2 秒内不放手的客户端会被断开。全量与线级日志中出现「direct send deadline exceeded」时，逐条确认是测试客户端读得慢，还是作业里本来不该有的长等待，并记入 Result。
4. **分离关闭继承活的作业令牌**：一个入站作业请求关闭**另一条**空闲连接时，`TouchSocketNetworkConnectionCloser` 的后台关闭不在 `SuppressFlow` 下，它继承了这个仍在运行的作业的令牌。于是那条连接的 `OnTcpClosed` 观察者清理会与作业并发执行，却读作「在执行器上」。
   - 本单元内的后果：这段清理的发送受 2 秒期限约束；探针看不见这段清理，所以 §3.1 的 Closed 与 §3.3 的计数可能下降，而这种下降不是迁移造成的。
   - 它不会自锁：观察者在本单元不入队；关闭自身连接要等收包回调结束，那时作业已经结束，令牌已失效。
   - 修正（关连接器改到 `SuppressFlow` 下、Closed 回调用 `RunDetached`）按拆分表归 TASK-10，本单元不提前。Result 里把 Closed/Connected 相关 (A) 计数的变化单列，交给 TASK-10 复核。见待裁定第 5 条。
5. **`RunDetached` 在 Received 上与 R-5 的关系**：
   - R-5 说的是 Closed 回调：TouchSocket 若在作业的发送中内联触发 Closed 回调，清除令牌会把「自我等待」变成一个最长 2 秒的等待环，由 TASK-08 的期限打破。
   - Received 的情形不同：它在 TouchSocket 的接收循环上运行，不会在作业的发送中被内联调用。它身上可能出现活令牌的唯一途径，是接收循环的 `ExecutionContext` 恰好捕获自一个仍在运行的作业（例如测试在作业里启动宿主）。
   - 边界保证这种情况不会被当作「在执行器上」，例如误套直发期限，或在同一执行器上报自我等待。
   - 代价：假如 Received 真的在某个作业里被内联调用，原本响亮的自我等待报错，会变成一次没有期限保护的等待。按 TouchSocket 的接收模型，这种情况不会发生。
6. **执行器释放与宿主停机**：宿主停机时先停 TCP 服务，再释放容器。作业仍在执行器上时，释放最多等 10 秒（TASK-08 N-2）。停机顺序（RQ-009）归 TASK-10，本单元只在耗时比对中观察。

### 10. 执行顺序

1. 注册表元数据、遥测注册与启动检查，先写 D6 并让它红，再改代码。
2. dispatcher 新形状，先写 D1～D5 并让它们红，再改代码；跑 `NetworkMessageDispatcherTests`。
3. `ProtocolModule` 的 14 条遥测注册；复核 §4 的检索。
4. 两个 `OnTcpReceived` 加 `RunDetachedAsync`，配 R1。
5. 跑 Application.Tests：按 §7 分类处置，执行 §8；写 D7。
6. 写 W1～W4，先跑 Networking.Tests，再跑 Host.Tests 与 Protocol.Tests。
7. 全量（带挂起检测），逐条分类，重跑到稳定；做耗时比对。
8. 探针复跑与归零分析（Required check 8），还原证明。
9. 填 spec 附录 B 的遥测行；写对账行（Required check 9）。
10. 正式全量 → 隔离复跑 → 线级 ×10 → 测试改动核对 → 边界 grep 与不变量复核 → 写 Result。

### 11. 为什么除可见日志外行为不变

- 每条入站消息的处理内容、顺序与帧，和今天逐行相同，只是换到执行器上运行。连接内顺序由收包回调的 `await` 保持；跨连接从并发变为串行，这是 RQ-001 要的。
- 可见差异只有三处：
  - 慢客户端在 2 秒期限后被断开（VC-8 提前，已登记）；
  - 被封锁或被顶替连接的遥测帧会被记日志（待裁定第 1 条）；
  - handler 失败时多一条执行器的 Error 日志（待裁定第 4 条）。
  - 线上字节不变，由表征测试与既有线级测试守着。

## 待裁定

1. **遥测路径跳过请求级检查**（§4）：被封锁或被顶替的连接，其遥测帧今天被静默丢弃，改动后照常记日志；线上字节与状态都不变。另外两种做法：
   - (a) 在执行器外照旧读 `IsBlocked` / `IsSuperseded`：本族探针 (A) 就不能归零，执行器外也多出一处读玩法状态的地方；
   - (b) 遥测也入队：附录 B 不再有遥测这一行，RQ-020 的例外不用，代价只是执行器上多一些只记日志的作业。
   - 本方案取「跳过」。若主控方更看重日志等价，建议选 (b)，不选 (a)。
2. **遥测判据取 RQ-020 的字面「只能注入日志」**：于是 `ClientSpecStatisticHandler` 等 7 个只读在线身份或选中会话、用来做日志归属的 handler 进执行器，附录 B 只有 2 个 handler、14 个消息类型。这与 AGENTS.md 把 `ClientSpecStatisticHandler` 叫作「纯遥测」的说法不一致（那里指的是不经命令总线），文档修正归 T-009。请确认。
3. **`EffectOperationReplayTests` 7 条中预计只有 `:292` 在本单元失败**。其余 6 条按规则顺延 T-004c。其中 `:269` 只操作 `EffectOperationContext`，翻转也未必会让它失败，建议把它的归属改为 T-008（帧改为显式数据时一并改写或删除）。请确认归属。
4. **handler 失败时会有两条 Error 日志**：执行器的 `Gameplay inbound:X failed` 和插件的 `Game/Channel handler failed`。本方案两条都保留，后者带帧号与 selector，前者带调用链。若要去重，建议在 T-008 handler 退化为只入队时处理，本单元不动插件日志。
5. **分离关闭继承活的作业令牌**（§9 第 4 条）：本单元起才真实出现，危害有限。按拆分表仍归 TASK-10，本单元不提前。若主控方希望 TASK-10 的探针基线不受这个盲区影响，可以把关连接器的 `SuppressFlow` 提前到本单元，代价是本单元会改变关闭回调能看到的门借用帧（TASK-08 当初把它归入 TASK-10 的理由）。
6. **改动 `spec.md` 附录 B**：只填清单，不改要求，先例是 TASK-02 填写附录 A 的统计结果。请确认可以在本单元的提交里修改 spec。
7. **事实出入，供参考，不阻断**：
   - 附录 A 写「141 个 handler」；基准上 `ProtocolModule` 有 173 条注册、142 个不同的 handler 类型（含遥测 2 个）。Result 以检索数为准；
   - 冲突清单的行号已经后移，见 §8。

## 主控方裁定（执行前）

1. **遥测路径不跳过检查**：遥测 handler 仍留在执行器外，但在调用之前，于执行器外先做和今天相同的只读检查（`IsBlocked`、`IsSuperseded`）。被封锁或被顶替的连接，其遥测帧照旧不记日志，这样日志行为与今天等价。这两项检查只读，RQ-061 的判据只覆盖写入，所以不违反判据。要补一条测试：被封锁连接的遥测帧不调用 handler。
2. **遥测判据取 RQ-020 的字面**：只能注入日志。`ClientSpecStatistic` 等 7 个 handler 进执行器。AGENTS.md 中「纯遥测」的说法在 T-009 修正。
3. **`:269` 的冲突用例**：改归 T-008，冲突清单同步更新。
4. **两条 Error 日志**：暂不去重，记入 Remaining，交给 T-008。
5. **关连接器的后台关闭继承令牌**：按拆分表留给 TASK-10，本单元不改。在 Remaining 中写明：从本单元起，这个问题已经真实存在；它只影响探针能看到的 Closed 记录，不影响功能，因为门断言尚未开启。
6. **Spec 附录 B**：允许在附录 B 中填入纯遥测 handler 的清单，有 TASK-02 先例。这只是填清单，不改需求。
7. **事实出入**：附录 A 写的是「141 个 handler」，实测为 173 条注册、142 个不同类型。在 Result 中记录这一出入，附录 A 的最终对账在 TASK-14 完成。

## Result

### Outcome

- **dispatcher 入队并等待**：`NetworkMessageDispatcher` 注入 `IGameplayQueue`。非遥测消息一律作为一个作业入队（`GameplayShardKey.World`、`GameplayCallChain.NewRoot(Connection, 连接 ID)`、CMD 类型 `inbound:<ClientMessageType>`），`DispatchAsync` 返回的就是作业的完成。作业体 `HandleOnExecutorAsync` 是原 `DispatchAsync` 第 20～45 行的原文、原顺序：scope → `IsBlocked` → `IsSuperseded` → `CaptureAuthority` → `NetworkCommandAuthorization.Enter` → 解析 handler → `CanHandle` → 效果账本或直接 `HandleAsync`。注册表查不到的类型照旧入队，在作业里先过封锁、再由 `GetHandlerType` 抛 `KeyNotFoundException`（`Blocked_physical_connection…` 照常通过）。
- **纯遥测留在执行器外**（裁定第 1 条）：`DispatchTelemetryAsync` 在收包回调里先做只读的 `IsBlocked`、`IsSuperseded`，被封锁或被顶替连接的遥测帧照旧静默丢弃；不建授权帧；效果请求被注册成遥测时抛程序错误。
- **注册表元数据与启动检查**：`MessageHandlerRegistration` 加 `IsTelemetry = false`；新增 `RegisterTelemetryMessageHandler<T>`；`MessageHandlerRegistry` 新增 `TryGet`（返回 `MessageHandlerRoute`：handler、遥测标记、预先算好的 CMD 类型）与静态 `CommandTypeOf`；构造时检查遥测 handler 每个公共构造器的每个参数都是 `ILogger`／`ILogger<T>`，一次列出全部违规后抛出。`GetHandlerType` 不变。
- **14 条遥测注册**：`ProtocolModule` 中 `HiddenCharacterInfoHandler` 1 条、`UnansweredReportHandler` 13 条改为 `RegisterTelemetryMessageHandler`（1 ＋ 13 ＝ 14），清单已填入 spec 附录 B。
- **Received 流边界**：Game 与 Channel 的 `OnTcpReceived` 整体在 `GameplayJob.RunDetachedAsync` 里执行（方法体缩进不变，只换了签名行与结尾）。
- **`SetUserAreaHandler` 门外写**：生产代码未改；随入站进入执行器，探针 §3.11 第 1 行归零（见 Checks 8）。
- **冲突用例**：`:292` 在本单元失败，已删除；其余 6 条本单元仍通过，`:269` 按裁定第 3 条改归 T-008，另 5 条顺延 T-004c。`task-07-conflicts.md` 追加 §7 记录去向与新分布。
- **停止条件未触发**：没有 SQLite 忙等造成的确定性变红；没有「direct send deadline exceeded」；没有排空超时。

### Changed

**生产代码**（`git diff --stat -- Server/src`：5 个文件，＋142 −24，全在 Owned boundary 内）

| 文件 | 改动 |
|---|---|
| `Application/Dispatching/NetworkMessageDispatcher.cs` | 入队并等待；`HandleOnExecutorAsync`（原文搬入）；`DispatchTelemetryAsync` |
| `Application/Dispatching/MessageHandlerRegistry.cs` | `IsTelemetry`、`MessageHandlerRoute`、`TryGet`、`CommandTypeOf`、遥测注入面检查 |
| `Application/Dispatching/NetworkMessageDispatchingExtensions.cs` | `RegisterTelemetryMessageHandler<T>` |
| `Networking/Runtime/NetworkPlugins.cs` | 两个 `OnTcpReceived` 经 `RunDetachedAsync`；加一行 `using` |
| `Protocol/ProtocolModule.cs` | 14 条注册改为遥测注册，其余未动 |

**新增测试 N ＝ 7 ＋ 1 ＋ 4 ＋ 2 ＝ 14**

- `Application.Tests/Dispatching/GameplayDispatchTests.cs`（7）：D1 `An_inbound_message_runs_as_a_connection_rooted_job_on_the_container_executor`；D2 `Request_frames_are_built_inside_the_job_and_reach_the_commands_it_runs`；D3 `The_callers_ambient_transaction_does_not_follow_the_message_into_the_job`；D4 `Dispatching_from_a_job_of_the_same_executor_is_a_self_wait`；D5 `A_telemetry_message_is_handled_off_the_executor`；D6 `A_telemetry_handler_that_injects_anything_but_logging_is_refused_at_startup`；D8（裁定第 1 条要求补的）`A_telemetry_message_from_a_blocked_connection_is_not_handled`。
- `Application.Tests/Sessions/EffectDispatchOnExecutorTests.cs`（1）：D7 `An_effect_request_and_its_replay_both_run_as_the_inbound_job`（首次在 `BeforeExplicit`、重放在 `BeforeSend` 记录，全部在执行器上、CMD 类型 `inbound:GameUseStackable`；重放不二次扣除）。
- `Networking.Tests/Integration/InboundExecutorWireTests.cs`（4）：W1 `A_game_and_a_channel_message_each_run_as_an_inbound_job_rooted_at_their_connection`；W2 `The_next_frame_of_a_connection_starts_only_after_the_previous_job_completes`（观察窗口 300 ms，未调整）；W3 `Two_connections_sending_alternately_keep_their_own_order_and_never_overlap`（N ＝ 20，进出计数器）；W4 `A_telemetry_frame_is_handled_off_the_executor_after_the_previous_job_completed`。搭法：`ApplicationModule`＋`NetworkingModule`＋`ProtocolModule`＋`TestInfrastructure`，移除 `GameExit`、`ChannelConnect`、`GamePi` 三条 `MessageHandlerRegistration` 描述符后改接探针 handler；dispatcher 外包一层只记录「插件的 await 已返回」的装饰器（经 `ActivatorUtilities` 构造真实 dispatcher）。
- `Networking.Tests/Runtime/ReceivedStreamBoundaryTests.cs`（2）：R1 Game 与 Channel 各一条。`ITcpSession` 用 `DispatchProxy` 构造，`new ReceivedDataEventArgs(ReadOnlyMemory<byte>.Empty, frame)` 在 TouchSocket 4.3.1 上可用，`e.InvokeNext()` 不抛。
- 可选的 `SetUserAreaHandler` 测试未写，证据取探针（Checks 8）。

**红的形态**（先写测试再改生产代码）：

- 加 `RegisterTelemetryMessageHandler` 之前：`GameplayDispatchTests.cs` 第 115、130、148～150 行 CS1061（D5、D8、D6 编译红）。
- 只加注册 API、dispatcher 未改：D1、D2（`Assert.True` 在 `OnExecutor`）、D3（`Assert.Null` 失败：看到了调用方事务）、D4（未抛）、D6（未抛）失败，5 失败 2 通过；D5、D8 通过（今天本来就没有作业）。
- R1：改插件前两条均 `Assert.Equal() Failure: Collections differ`（dispatcher 看到 `IsOnExecutor` 为真）。
- 变异核对（全部用 scratchpad 备份逐字节还原，`cmp` 相等，`&& false` 计数 0）：
  - 换回 HEAD 版 dispatcher：D1～D4、D7、W1、W3、W4 失败；W2 通过（RQ-005 今天已由收包回调的 `await` 成立，W2 守的是迁移后不被破坏）；临时把 W3 的重叠断言提前，三次各数到 34／28／29 次重叠；
  - 遥测也入队：D5、W4 失败；
  - 遥测路径去掉 `IsBlocked`：D8 失败。

**既有测试文件：1 个改动**

- `Sessions/EffectOperationReplayTests.cs`：只删 `Waiting_on_the_connection_gate_keeps_the_original_selected_session`（`git diff --stat`：15 行删除，0 行新增，即空行 1 ＋ 方法 14 行）。
  - 旧断言：第一次 dispatch 卡在发送里时，第二次 dispatch 在每连接门上等待并保持原来捕获的会话；换会话后它被拒、不扣，计数 2、无批次。
  - 本单元的失败形态：第一次作业在 `BeforeExplicit` 里用 `ManualResetEventSlim` 同步占住执行器线程，`f.Select(2)` 经 `TestGameplay.Run(provider, …)` 排在它后面同步等待，5 秒后 `TimeoutException`。
  - 保护意图的承接：同一连接的下一条入站在上一条作业完成后才解包入队（W2）；选中会话与授权在作业开始时捕获（D2、D7）；`Same_ID_new_physical_context…` 与 `:371` 仍在。
- 没有改写任何用例，也没有改任何断言。D ＝ 1。

**文档**：spec 附录 B「纯遥测 handler」一行填入清单与排除项（裁定第 6 条）；`verification/task-07-conflicts.md` 追加 §7；日志 `logs/task-09-{build-test,isolated,wire-x10}.txt`、`logs/task-09-probe-{Application,Protocol,Host,Networking}.txt`。

### Why（本地决定）

- **分流用 `TryGet` 并带回预先算好的 CMD 类型**：既不改变「封锁先于查表」，也不在每条消息上拼字符串；未注册类型才现场拼。
- **遥测路径里效果请求的检查放在最前**：它是程序错误，不应被封锁检查吞掉。
- **W1～W4 用全模块宿主而不是最小宿主**：解包器、插件、`TestInfrastructure` 都与既有线级测试一致，替身只替换三条 handler 注册。W4 额外断言普通消息在执行器上，否则它在旧代码上也会通过，失去区分力。
- **D2 的「命令看得到授权帧」用封锁后 `DemandCurrent` 抛 `UnauthorizedAccessException` 证明**，命令处理器未被调用（`CommandRuns` 为 0）。

### §7 主动复核（全绿，逐项写结论）

| 项 | 结论 |
|---|---|
| (i)～(v) 计数 | (i) 1（`:292` 删除）；(ii) 0；(iii) 0；(iv) 0；(v) 0。`task-07-conflicts.md` 不需要「TASK-09 新增」冲突，只追加去向一节 |
| 调用 dispatcher 且用到 `TestGameplay` 的 15 个文件 | 静态扫描（`TestGameplay.Run*(` 的括号区域内查 dispatcher／`EffectRequestTransport.Send`／`.DispatchAsync(`）：66 个区域（1＋34＋1＋1＋2＋2＋2＋3＋2＋1＋10＋2＋2＋2＋1），区域内命中 0；全量与隔离复跑中自我等待 0 次。整方法包裹里没有 dispatcher 调用，不存在依赖外层帧的问题 |
| Protocol 5 个文件、其余三项目 | 对 Protocol／Networking／Host／Persistence 中使用 `TestGameplay` 的 96 个文件做同一扫描：457 个区域，只有 `BossTowerPublicHandlerTests` 2 处命中，读后确认是 `NetworkDispatchContext` 的工厂方法 `Dispatch(first)`，不是 dispatcher；Protocol 全部 1694 条通过 |
| Networking 同步短作业（GameLogin 6、IllusionTowerStage 4、CardRewardReconnect 4、BossTower 3） | 现在排在入站作业之后，只是延迟。入站作业不等测试线程：它们等的最多是对测试客户端的直发，帧都很小，落在 socket 缓冲里；全量、隔离与探针三轮中「direct send deadline exceeded」0 次，这些用例全部通过 |
| `NetworkMessageDispatcherTests.Already_queued_request…` | 作业在 57 号行为上等测试在执行器外持有的 `TransactionalExecutionGate`（允许的方向）；测试在 `finally` 里放门，不依赖作业先完成；通过 |
| 容器执行器作业里释放容器；释放时有卡住的作业 | 自我等待 0；「did not drain within」0 条；耗时比对见 Checks 2，没有排空或忙等造成的 ≥ 9 s 增量 |

### Checks

1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 个警告，0 个错误（`logs/task-09-build-test.txt` 开头，同时记录了正式运行前的 `git diff --stat`／`git status --short -- Server/src`，只有本单元 5 个文件）。
2. **全量**（`logs/task-09-build-test.txt`，带 `--blame-hang-timeout 5m`）：
   - 总数 50 ＋ 1694 ＋ 80 ＋ 475 ＋ 969 ＋ **155** ＋ **4438** ＋ 165 ＝ 8026 ＝ 8013 ＋ 14 － 1；
   - 逐项对照 TASK-08：Networking 149 → 155（＋6 ＝ W 4 ＋ R 2）；Application 4431 → 4438（＋7 ＝ ＋8（D 7 ＋ D7 1）－ 1（`:292`））；其余 6 项相等；
   - 通过 50 ＋ 1694 ＋ 80 ＋ 475 ＋ 967 ＋ 153 ＋ 4420 ＋ 165 ＝ 8004；失败 2，只有那 2 条 `DimensionDungeonTests`；跳过 2 ＋ 2 ＋ 16 ＝ 20（D 中无跳过用例）；8004 ＋ 2 ＋ 20 ＝ 8026；
   - 表征测试 8 通过、2 跳过；挂起转储 0（日志末尾 `find` 计数 0）；`## exit 1` 来自那 2 条已知失败；
   - 日志信号：「direct send deadline exceeded」0、「did not drain within」0、`GameplaySelfWait` 0；执行器的 `Gameplay inbound:X failed` 3 条（`GameSelectCharacter` 2、`GameSellItem` 1），都是用例故意注入的 handler 失败，插件侧 `Game/Channel handler failed` 同时记录（待裁定第 4 条的双日志，交 T-008）。
   - **耗时比对**（与 `logs/task-08-build-test.txt` 逐条，阈值 9 s）：多出 ≥ 9 s 的 9 条，全部判为负载波动：
     | 用例 | t06／t07／t08／本单元探索轮 r1／正式 | 隔离 | 判定 |
     |---|---|---|---|
     | `TauKingdomTests.Real_entry…` | 8／24／8／5／23 | 1 s | 负载（TASK-07 也是 24） |
     | `IllusionTowerRewardRuleServiceTests.Current_archive…` | 7／11／10／7／24 | 1 s | 负载；纯规则服务读真实 PVF，不经执行器与 dispatcher |
     | `GmEventsHttpTests.Permissions…` | 24／18／13／15／24 | 2 s | 负载 |
     | `GmEventsHttpTests.Specific_settings…` | 44／13／6／8／16 | 1 s | 负载 |
     | `TournamentDungeonTests.Black_dragon…` 4 条 | 8～15／≈1／≈1／8～10／10～12 | 均 < 1.1 s | 负载：读真实 PVF；TASK-06（容器尚未注册执行器、不可能有排空）也是 8～15 s |
     | `LauncherAuthenticationEndToEndTests.HttpChannelAndGame…` | 12／12／4／11／13 | 1 s | 负载 |
     各项目总耗时（「总时间」，TASK-08 → 本单元）：Pvf 7.0 → 6.5 s；HttpApi 9.7 → 8.4 s；Protocol 9.8 → 10.7 s；Persistence 21.6 → 22.7 s；Infrastructure 46.4 → 44.5 s；Networking 47.0 → 46.4 s；Application 2.35 → 2.31 min；Host 2.45 → 2.46 min。逐条耗时按项目求和：Application 1122.0 → 1123.4 s，Host 853.0 → 857.5 s，Networking 78.6 → 90.6 s（新增 6 条约 3 s，其余为负载），Protocol 35.8 → 46.9 s。
3. **隔离复跑**（`logs/task-09-isolated.txt`）：Application 4438 条，4420 通过、2 失败（同上）、16 跳过，1.71 min；Networking 155 条，153 通过、2 跳过，16.7 s；Protocol 1694 条全部通过；Host 165 条全部通过。没有超时形态的失败。
4. **线级 ×10**（`logs/task-09-wire-x10.txt`）：W1～W4 隔离连续 10 次，每次 4 条全部通过（40 条）；全量负载下 1 次 4 条通过（取自正式全量，571～937 ms）。合计 44 条「已通过」。
5. **表征测试**：8 通过、2 跳过，断言未改（`Server/tests/.../Characterization` 无改动）。
6. **测试改动核对**：新增 14 条见 Changed，N ＝ 7 ＋ 1 ＋ 4 ＋ 2 ＝ 14；既有文件只有 `EffectOperationReplayTests.cs` 一处删除（15 行删除、0 行新增），其余既有测试文件 0 改动，所以没有「只改包裹方式」需要差异核对脚本证明的文件。顺延的 6 条在正式全量中均为「已通过」：`An_inherited_closed_operation…`、`Postdurable_operation_record_failure…`、`Invalid_projection_freeze…`、`An_inherited_readonly_scope…`、`Unknown_applied_effect_change…`、`Refusal_only_unselected_operation…`；探针定向复跑 x2 也是 6 条全过。
7. **生产改动的边界**：
   - `git diff --stat -- Server/src`：只有 Owned boundary 的 5 个文件；无未跟踪文件。
   - `grep -rn IGameplayQueue Server/src`：`Abstractions/Execution/GameplayQueue.cs`、`Application/Execution/GameplayQueue.cs`（3 处）、`Application/Dispatching/NetworkMessageDispatcher.cs`。`CoalescingGameplaySource` 是 `Application/Execution` 内的既有命中。
   - `grep -rn RunDetached Server/src`：`GameplayJob.cs` 4 个定义、`GameplayJobSlot.cs` 1 处 `cref`，使用只有 `NetworkPlugins.cs:56`、`:138` 两个 `OnTcpReceived`。
   - 不变量（拆分原则第 3 条）：
     - `INetworkMessageDispatcher.DispatchAsync` 的生产调用方只有两个插件的 `OnTcpReceived`，它们不持任何门；
     - `EffectOperationLedger.DispatchAsync` 的生产调用方只有 `NetworkMessageDispatcher.HandleOnExecutorAsync`（在作业里）；
     - 会等待收包回调结束的关闭调用：`TouchSocketNetworkConnectionCloser` 一律 `Task.Yield` 后分离关闭；`ExecutorSendDeadline.StartDetachedClose` 在 `SuppressFlow` 下 `Task.Run`；`TcpServices.DrainingSession` 的 `Transport.CloseAsync` 在关闭回调里；`LoginSessionRuntime` 经关连接器（分离）；`OutboundSender`／`OutboundTransport` 未接线。没有执行器外的代码「持门等待入站作业」。
8. **探针复跑**（`verification/probe-tools/`，`PROBE_WORK` 在 scratchpad，轮次 p1，项目 Application、Protocol、Host、Networking；原始记录 `logs/task-09-probe-<Project>.txt`）：
   - **本族 (A)**：dispatcher／`OnTcpReceived`／账本 三类入口帧的记录从 TASK-07 的 5208 ＋ 17 条（命中 16257 ＋ 102）降到 1 ＋ 1 条：
     - Networking 1 条（同一条记录同时带 `NetworkMessageDispatcher.DispatchTelemetryAsync` 与 `GameNetworkPlugin.<OnTcpReceived>b__0` 帧）：`P2:LoginSessionRegistry`，栈顶 `LoginSessionRegistry.IsSuperseded`，命中 1。这是裁定第 1 条要求的遥测路径只读检查，不是漏迁；
     - Application 1 条：`EffectOperationLedger.DispatchAsync` → `OnlineStateRegistry.FindByConnection`（P2，命中 1），测试 `EffectOperationReplayTests.Refusal_only_unselected…`（`:371`，顺延 T-004c）。定向复跑 x2（只跑顺延的 6 条）复现这一条。
   - **§3.11 第 1 行**（`SetUserAreaHandler` 路径的 P1b-teardown 与 P1）：四个项目均为 0。
   - **§5 本族 (C) 栈顶**：`ClientMessageHandler<T>.HandleAsync`、`EplpCommandHandler`、`ResponsePeerHandler`、`SetUserAreaHandler` 在四个项目中均为 0。`SelectDungeonHandler.HandleMessageAsync`（P1b-read，命中 4）在 Application 仍有：定向复跑 x3（只跑 `IllusionTowerPublicFlowTests.Sweep_captured…`，即 TASK-11 的冲突用例 `:149`）复现了这 4 次。该用例在作业外直接调用 handler，读一次性快照；**这更正了 `task-07-probe.md` §5「Application 中作业外的执行只来自 dispatcher 路径」的归属**。它不来自 §8 的顺延用例，但来自冲突总清单中另一单元的用例，且只读，按「排除后仍有的才算缺陷」不构成缺陷。
   - **排除复跑 x1**：Application 排除整份冲突总清单（沿用 TASK-07 的 `excl.filter`），本族 (A) 0、本族 (C) 0、SetUserArea 0。Protocol、Host 首轮即为 0；Networking 唯一一条是遥测只读检查。
   - **本族以外各族 (A)**（四个项目合计，TASK-07 最终轮 → 本轮，记录／命中）：
     | 族 | 记录 | 命中 |
     |---|---|---|
     | 3.1 Connected/Closed 回调 | 202 → 164 | 1191 → 1210 |
     | 3.2 效果账本 | 17 → 1 | 102 → 1 |
     | 3.3 连接观察者 | 83 → 83 | 118 → 118 |
     | 3.4 登录会话 | 1 → 1 | 1 → 1 |
     | 3.5 事件 | 461 → 476 | 1899 → 1903 |
     | 3.6 卡牌计时器 | 24 → 24 | 141 → 141 |
     | 3.7 GM/HTTP | 1031 → 1088 | 2198 → 2218 |
     | 3.8 后台与调度 | 223 → 209 | 4601 → 4331 |
     | 3.9 启动与停机 | 190 → 158 | 376 → 329 |
     | 3.10 命令总线 | 4171 → 3498 | 18235 → 10699 |
     分组口径：按栈上帧归族，dispatcher／Received 优先于其余各族（所以带 dispatcher 帧的总线记录计在 3.1）。下降的原因：
     - 3.10：消失的总线记录中，Networking 493 条里 425 条、Application 696 条里 163 条、Host 152 条里 22 条带 Protocol 的 handler／投递帧，是 40 帧上限截掉了 dispatcher 帧的入站命令，现在在作业里；
     - 3.9：Networking 消失的 31 条中 29 条是 `DungeonCardPresentationCoordinator.Dispose`，命中没有减少（41 → 45），只是变成 (C)：这些宿主现在启动过执行器，容器释放时先 `await` 执行器的 `DisposeAsync`，后面单例的释放在异步续体里，栈上不再有宿主帧。Host 消失的是 `ForeachService` 下的启动事务记录（同一批命中换了栈形，命中 325 → 315）；
     - 3.1 Connected/Closed：记录数下降而命中略升，符合 §9 第 4 条「分离关闭继承活的作业令牌，Closed 清理读作在执行器上」以及栈形变化，交 TASK-10 复核；
     - 3.8、3.7、3.5：不同栈的记录数随时序波动，命中基本持平，与本单元无关。
   - 探针轮 Networking 有 1 条失败（`SkillLearningIntegrationTests.A_captured_request…`，`SocketException: Connection refused`，端口竞争），正式全量与隔离复跑中均通过；探针轮结果不作为通过证据（TASK-07 §7）。
   - **还原证明**：快照 `snap-before.txt` 与 `snap-after.txt` sha1 都是 `82f3e9b838c377589356b8e43a49dc044f729b94`，`cmp` 逐字节相等；快照内 1577 条索引项中 1572 条与工作区相等，差的 5 条就是本单元的 5 个正式改动，未跟踪 0；`patch.py revert` 恢复 25 个文件并删除 `GameplayProbe.cs`；之后 `git diff --stat`／`git status --short -- Server/src` 只剩 5 个正式改动，`grep TestGameplay|GameplayProbe Server/src` 0 命中；再以 `--no-incremental` 重新构建后跑正式全量。
9. **对账行**：
   - 附录 A 第 1 行（Game 与 Channel 入站协议）：来源 Connection → `inbound:<ClientMessageType>`（例 `inbound:GameExit`、`inbound:ChannelConnect`）→ W1、W3、D1；探针本族 (A) 5208 → 1（遥测只读检查）。附录 B 的 14 个遥测消息类型除外（W4、D5）。「每个消息类型一个协议 CMD」的归并留给 T-008。
   - 附录 A 第 2 行（效果账本重放）：随所属协议 CMD 执行，CMD 类型同上（例 `inbound:GameUseStackable`）→ D7；探针账本 (A) 17 → 1（`:371` 顺延 T-004c）。
   - 附录 H 第 1 行（dispatcher 的 `IsBlocked`／`IsSuperseded`／`CaptureAuthority`／`Enter`）：位置已在执行器上、CMD 开始时 → D2、D3；改为显式数据留给 T-008。
   - 附录 H 第 2 行（`EffectOperationLedger` 的登记、重放、Rejected、无选中会话帧）：位置已在执行器上 → D7；成为 CMD 的请求级上下文（RQ-025 三态）留给 T-008。
   - 事实出入（裁定第 7 条）：反射 `ProtocolModule` 得 173 条注册、**141** 个不同 handler 类型（Plan 写 142，附录 A 写 141，反射与附录 A 一致）；遥测之外名字像遥测的还有 `ClientHardwareDetailHandler`、`ContentsPlayInfoHandler`、`MercenaryInfoHandler`、`GenCeraTicketHandler`，已补进附录 B 的排除注释。最终对账在 TASK-14。

### Remaining

1. **两条 Error 日志**（裁定第 4 条）：handler 失败时执行器的 `Gameplay inbound:X failed` 与插件的 `Game/Channel handler failed` 各一条，交 T-008 去重。
2. **关连接器分离关闭继承活的作业令牌**（裁定第 5 条）：从本单元起真实存在（入站作业里 `RequestCloseAsync` 的 `Task.Yield` 续体带着作业令牌），只影响探针能看到的 Closed 记录，门断言尚未开启所以不影响功能；修正归 TASK-10。上表 3.1 Connected/Closed 的变化交 TASK-10 复核。
3. **T-004c 判据要覆盖裸 `new DomainTransaction()`**：`SetUserAreaHandler` 的 `AloneAsync` 写与拆除写都不经 `DomainTransactionScope.Begin`；只挂 `Begin` 的判据看不见它们。提交点按 RQ-024 是独立工作单元，T-008 归并时写入附录 C。
4. **AGENTS.md「纯遥测」措辞**（裁定第 2 条）：把 `ClientSpecStatisticHandler` 称作纯遥测，与附录 B 判据不一致，T-009 修正。
5. **AC-003 第 3 条**（持续灌包时待执行入站 CMD ≤ 1）未单独测，结构上由收包回调 `await` 作业保证，留 T-004c 对账。
6. **`task-07-probe.md` §5 的一条归属需要更正**：`SelectDungeonHandler.HandleMessageAsync` 的 (C) 来自 `IllusionTowerPublicFlowTests:149`，TASK-11 处置该冲突用例时一并归零。
7. **中间态风险仍在**：入站作业与执行器外写者（断连拆除、后台服务、事件分发、GM/HTTP）撞 SQLite「先读后写」时会让整个执行器等满 30 秒；本单元全量、隔离与探针三轮均未出现，归 TASK-10～13 迁移后消失。
8. **N-2 的间接核对仍是唯一手段**：测试容器多数没有日志提供程序，「did not drain within」计数 0 不能单独作为没有排空的证据；本单元用多轮耗时对照与隔离复跑排除。
9. **未提交**：没有 commit，也没有 stash。

### 主控方补充（结果审查通过之后，提交之前）

- **交给 T-008**（审查 N-1）：补一条线级测试，覆盖「作业内 handler 抛异常 → 断线」。现有 `HandlerFailureConnectionIntegrationTests` 的装饰器在进入内层 dispatcher 之前就抛出了，没有覆盖这条路径。
- **交给 TASK-10**（审查 N-2）：
  - Result 里说 Connected/Closed 的变化「符合 §9 第 4 条」，与数据不符：命中数是从 1191 升到 1210，不是下降。
  - TASK-10 修好关连接器之后，以那次复跑作为 Closed 族的基线重新对照。
- **交给 T-009**（审查 N-3）：Spec 附录 B 纯遥测那一行，第一列的举例（客户端规格、会话指标）与本行清单矛盾，需要一并修正。
- **残余风险**（审查 N-4）：如果 TouchSocket 在作业中内联调用接收回调，Received 边界会变成一个没有期限的死锁。TouchSocket 4.3.1 不会这样调用，但升级 TouchSocket 时要复核这一点。
- **不补测试**（审查 N-5）：被顶替连接的遥测分支没有测试，但这部分代码与今天逐字一致。
