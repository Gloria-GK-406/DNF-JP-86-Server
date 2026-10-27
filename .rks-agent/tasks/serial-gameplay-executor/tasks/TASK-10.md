# TASK-10 — T-004b3：连接生命周期、登录会话与停机顺序迁入执行器

基准提交 `1eea890f`（TASK-09 之后）。边界提纲见 `TASK-08.md` 文末「T-004b 拆分」§2 TASK-10 一行与 §1 的跨单元规则；TASK-09 的 Result、主控方裁定第 5 条与「主控方补充」中交给本单元的两项（关连接器继承活令牌、N-2 的 Closed 族基线）是本单元的输入。本文件不重抄这些内容，只写本单元的具体做法。

## Task Unit Contract

- Revision: 1
- Outcome and references:
  - **连接建立与断开成为生命周期 CMD**：Game 与 Channel 的 `OnTcpConnected` / `OnTcpClosed` 不再直接调用观察者，而是经一个新的连接生命周期分发器，把「按注册顺序依次调用全部 `INetworkConnectionObserver`」整段作为一个执行器作业入队，并等待它完成。观察者（`OnlineUserConnectionObserver`、`LoginSessionConnectionObserver`）的代码一字不改，与 TASK-09 搬 dispatcher 的做法相同。初始 NOTI、断连清理、孤儿清理、`AloneAsync` 拆除落位置都随之进入执行器。
  - **传输回调是流边界**（TASK-06 待办 3f 的余下部分）：两个回调整体在 `GameplayJob.RunDetachedAsync` 里执行。
  - **关连接器的分离关闭不再继承作业令牌**（3e 的第二项；TASK-09 裁定第 5 条、Remaining 第 2 条）：`TouchSocketNetworkConnectionCloser` 在 `ExecutionContext.SuppressFlow` 下启动后台关闭。
  - **登录会话**（附录 A 第 4 行）：`LoginSessionConnectionObserver` 随上一条进入生命周期 CMD；`LoginSessionMaintenanceService` 的顶替关闭，每个关闭请求作为一个生命周期 CMD；过期扫描每次到期作为一个定时 CMD，经 `CoalescingGameplaySource` 入队。
  - **停机顺序**（3i，RQ-009）：宿主停止时，Kestrel 与 TCP 先停止接受；传输把剩余连接全部关闭，且每个关闭回调的生命周期 CMD 都已入队并完成；然后执行器排空并停止。容器释放在这之后。
  - **冲突用例**：`SelectCharacterLifecycleTests` 4 条、`OnlineUserServiceTests` 3 条，在本单元失败的执行处置，仍通过的顺延 T-004c，并给出本单元的后继用例（§10）。
  - 覆盖：附录 A 第 3、4 行；RQ-008（生命周期一侧的等待关系）、RQ-009、RQ-012 的第一句（第二句「关连接排在已登记消息之后」的发件箱形态归 T-006，本单元只表征过渡期现状）；RQ-010 的生命周期部分；AC-007 中可在过渡期证明的部分（断线、关连接、被顶替三种情形下清理在执行器上；CMD 关闭自己的来源连接不自锁；停机时拆除落库在执行器停止之前完成）；AC-006 的附录 A 第 3、4 行对账。
- Owned boundary:
  - 生产代码，只限：
    - `Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs`：新增连接生命周期分发器接口（§2）；
    - `Server/src/Dfo.Server.Abstractions/Execution/` 下新增一个文件：停机前的生产者排空接口（§6）；
    - `Server/src/Dfo.Server.Application/Dispatching/`：新增连接生命周期分发器的实现文件；`NetworkMessageDispatchingExtensions.cs` 注册它；
    - `Server/src/Dfo.Server.Application/Execution/`：`GameplayQueue.cs`（在 `AddGameplayExecution` 里注册执行器生命周期服务）与新增的执行器生命周期服务文件；
    - `Server/src/Dfo.Server.Application/Authentication/LoginSessionRuntime.cs`；
    - `Server/src/Dfo.Server.Networking/Runtime/`：`NetworkPlugins.cs`（只改 `NetworkPluginBase` 的构造参数与两个回调）、`TouchSocketNetworkConnectionCloser.cs`、`TouchSocketConnectionRegistry.cs`（在途关闭回调计数与枚举）、新增的传输排空实现文件；`ExecutorSendDeadline.cs` 只在把分离关闭抽成共用辅助时改动；
    - `Server/src/Dfo.Server.Networking/NetworkingModule.cs`：注册传输排空实现；
    - §9 第 (iv) 类确需的缺陷修正，限于上述文件。
  - `OnlineUserConnectionObserver`、`LoginSessionRegistry`、`CharacterStateAccess`、`DungeonCardPresentationCoordinator`、`ProtocolModule`、Host／GameServer／HttpApi 的组合代码**不改**。
  - 测试：
    - 新增 `Server/tests/Dfo.Server.Application.Tests/` 下 `Dispatching/`、`Execution/`、`Authentication/`、`OnlineUsers/` 的测试文件；
    - 新增 `Server/tests/Dfo.Server.Networking.Tests/Runtime/`、`Integration/` 下的测试文件；
    - 新增 `Server/tests/Dfo.Server.Host.Tests/` 下的真实进程停机测试，`EndToEndHostHarness.cs` 只加「优雅停止」一个方法；
    - `Networking.Tests/Runtime/ReceivedStreamBoundaryTests.cs` 只改插件的构造实参；
    - 按 §9 处置的既有测试文件。
  - 文档：`verification/` 下的 `logs/task-10-*.txt`；`task-07-conflicts.md` 追加一节（记录 7 条的去向；出现 §9 第 (ii)、(v)、(vi) 类新冲突时一并记入）。
- Required checks:
  1. `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 警告，0 错误。
  2. **全量**：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-10-build-test.txt`。
     - 总数 ＝ 8026 ＋ N（本单元新增）－ D（本单元删除），与 TASK-09 Result 的计数逐项对照，写出加法；
     - 失败只能是那 2 条 `DimensionDungeonTests`；跳过 20，若新增的 Skippable 用例在本机跳过，另写加法；表征测试 8 通过、2 跳过；没有挂起转储；
     - 日志信号计数：`direct send deadline exceeded`、`did not drain within`、`GameplaySelfWait`、`Gameplay lifecycle:` 开头的失败、§2 新增的「执行器已停止，生命周期 CMD 未执行」Warning；每一条非零的都要逐条说明来源；
     - **耗时比对**：与 `logs/task-09-build-test.txt` 逐条对比，列出多出 ≥ 9 秒的用例并逐条判定（负载波动、释放时 10 秒排空、作业里的 SQLite 忙等，或本单元新增的「宿主停止时排空等满停机期限」）；写出各项目总耗时对照。
  3. **隔离复跑**：Application.Tests、Networking.Tests、Protocol.Tests、Host.Tests 各一次，落盘 `logs/task-10-isolated.txt`。超时形态的失败先按 AGENTS.md「满载假红」的规则隔离复跑，再归类。
  4. **新增的线级与停机测试**（§8 的 W5～W9、K、S1、S2）：隔离连续 10 次，全量负载下 1 次，全部通过，落盘 `logs/task-10-wire-x10.txt`。S2 在本机没有 Script.pvf 时跳过，须在 Result 写明。
  5. **Host.Tests 真实进程停机**（§8 的 S3）：隔离连续 3 次通过；在 Windows 上跳过（§6.5），落盘 `logs/task-10-host-stop.txt`。
  6. **表征测试**：8 通过、2 跳过，断言未改。
  7. **变异核对**（每项用 scratchpad 备份逐字节还原并 `cmp`）：
     - 关连接器改回 `Task.Yield`：C1 变红；
     - 生命周期回调改回在回调里直接调用观察者：W5 变红；
     - 执行器生命周期服务跳过传输排空、直接停止执行器：S1 变红；
     - 执行器生命周期服务改在 `StopAsync` 而非 `StoppedAsync` 里停止执行器：S1 变红，或说明它为什么不红；
     - 顶替关闭改回在执行器外执行：M1 变红。
  8. **测试改动核对**：
     - 新增测试逐条列名，N 写出加法；
     - §9 处置过的每个既有测试文件写明类别与理由；只改包裹方式或构造实参的文件，用 TASK-07 的差异核对脚本证明方法集合与断言行多重集合不变；
     - 删除或改写的冲突用例写成「旧断言 → 新断言 → 保护意图」；顺延的冲突用例逐条给出本单元全量中「已通过」的记录，并写明后继用例（§10）。
  9. **生产改动的边界**：
     - `git diff --stat -- Server/src` 只含 Owned boundary 所列文件；
     - `grep -rn "IGameplayQueue" Server/src`（排除 bin/obj）：命中只在 `Abstractions/Execution`、`Application/Execution`、`Application/Dispatching/NetworkMessageDispatcher.cs`、本单元新增的生命周期分发器、`Application/Authentication/LoginSessionRuntime.cs`；`Networking` 中零命中；
     - `grep -rn "RunDetached" Server/src`：除定义外只有 `NetworkPlugins.cs` 的两个 `OnTcpReceived` 与 `OnTcpConnected`、`OnTcpClosed`；
     - `grep -rn "SuppressFlow" Server/src`：只有 `GameplayExecutor`、`ExecutorSendDeadline` 与关连接器（或二者共用的辅助）；
     - **部分迁移期间的不变量**（拆分原则第 3 条）：列出生命周期分发器、`LoginSessionMaintenanceService` 的作业在执行器外的全部等待方（TouchSocket 回调、TCP 停止、传输排空、维护循环），以及全部 `CloseAsync(` 调用点，逐个确认没有「持门等待生命周期作业」。
  10. **探针复跑**（`verification/probe-tools/`，授权见下）：
      - 项目：Application、Protocol、Host、Networking；原始记录 `PROBE_LOG_PREFIX=task-10-probe`，落盘 `logs/task-10-probe-<Project>.txt`；
      - **本族归零**：入口帧为 `NetworkPluginBase.OnTcpConnected` / `OnTcpClosed`、`OnlineUserConnectionObserver.*`、`LoginSessionMaintenanceService.*`、`LoginSession*Runtime` 的 (A) 记录为 0；`task-07-probe.md` §3.11 第 2 行中经 `OnTcpClosed` 的 P1b-teardown 为 0；§5 中由断连拆除驱动的 (C) 栈顶（`TownAreaProjectionDelivery.*`、`DungeonLifecycleDomainService.RecordDepartureAsync`、`DungeonRunDomainService.EndAsync`、`TransactionalExecutionGate.AcquireAsync` 中属于拆除的那部分）为 0；
      - 残留记录只允许来自 §10 中顺延的冲突用例及其他直接调用观察者的测试，并用排除复跑证明；排除后仍有的按缺陷处理；
      - **Closed 族新基线**（TASK-09 主控方补充 N-2）：§3.1 Connected/Closed 的记录数与命中数，按 TASK-07 → TASK-09 → 本单元三列并列写出，并更正 TASK-09 Result「符合 §9 第 4 条」的说法（命中是 1191 → 1210，上升而非下降）；
      - 本族以外各族的 (A) 计数与 TASK-09 并列写出，作为 TASK-11～13 的输入；§3.9 停机族单列：卡牌协调器 `Dispose` 的记录预计仍在，见 §7；
      - 还原证明三项同 TASK-07 Required check 6；之后先 `--no-incremental` 重新构建，再跑正式全量。
  11. **对账行**：附录 A 第 3、4 行各写一行「来源 → CMD 类型 → 证据（测试名或探针计数）」，标明哪一部分留给 T-006、T-007；分离点清单（3e）写出本单元之后的现状。
- Dependencies: TASK-09（dispatcher 在执行器上、Received 流边界、探针基线、Remaining 第 2、7 条）；TASK-08（容器执行器注册、直发 2 秒期限、`StartDetachedClose`、探针工具）；TASK-04 的执行器、`StopAsync`、`Completion`、`RunDetached`、`CoalescingGameplaySource`；TASK-06／07 的 `TestGameplay` 与冲突总清单。
- Result review: required。
  - 本单元新增两个 Abstractions 契约（连接生命周期分发器、停机前的生产者排空），并改变了宿主停止的行为：凡是用 `ApplicationModule` 组装的宿主，`StopAsync` 之后执行器都已停止。后续 TASK-11～13 的生产者都要接上这个停机次序，T-006 还要在它上面改关连接的语义。接错了，要到那些单元才以「停机时丢 CMD」暴露。
  - 停机时位置有没有丢，只有在真实连接、真实宿主停止、读回 SQLite 的用例里才看得见；这条用例依赖 Script.pvf，CI 上可能被跳过（S2），无 PVF 的 S1 只证明次序，不证明落库内容。
  - R-5 的等待环是否真实出现，取决于 TouchSocket 4.3.1 在发送失败时是否内联调用关闭回调；只有 K 这条真实 socket 用例能说明，而且只在 macOS 上测。
  - 冲突用例「顺延」与后继用例是否真的承接了保护意图，靠人判断。
- Task-specific authorization:
  - 探针复跑（Required check 10）与变异核对（Required check 7）期间，允许临时改动 `Server/src`，用后按三项还原证明；探针与变异改动不提交，不 `git stash`。
  - 只允许删除或改写「因本单元的改动而失败或挂起」的用例，每条在 Result 中列出；其余冲突用例保持原样，留给所属单元。

## Detailed Plan

### 1. 生命周期 CMD 的形状

| 情形 | 今天的入口 | 新的 CMD 类型 | 来源 ／ 调用链 | 作业体（逐字搬迁的范围） |
|---|---|---|---|---|
| 连接建立 | `NetworkPluginBase.OnTcpConnected` → 各观察者 `ConnectedAsync` | `lifecycle:connected` | `NewRoot(Lifecycle, 连接 ID)` | 按注册顺序 `await observer.ConnectedAsync(context, ct)`：在线注册、解析频道、初始 NOTI（直发，受 2 秒期限约束）、失败时 `TryDisconnect` 并重新抛出（DR-12） |
| 连接断开（任何原因：客户端断开、CMD 请求的关闭、传输层自发关闭、停机关闭） | `NetworkPluginBase.OnTcpClosed` → 各观察者 `DisconnectedAsync` | `lifecycle:disconnected` | `NewRoot(Lifecycle, 连接 ID)` | 按注册顺序：`LoginSessionRegistry.Disconnected`；在线身份的断连清理，含孤儿分支（协议拒绝已删在线身份、选中会话仍在）、清理通告投递、`AloneAsync` 拆除落位置 |
| 被顶替（登录交接提交后关闭旧连接） | `LoginSessionMaintenanceService.CloseSupersededAsync`，每个关闭请求 | `lifecycle:close-superseded` | `NewRoot(Lifecycle, 被顶替的连接 ID)` | `onlineUsers.TryDisconnectConnection(connection)` → `closer.RequestCloseAsync(connection, ct)`；关闭随后引起该连接的 `lifecycle:disconnected` |
| 登录会话过期扫描 | `LoginSessionMaintenanceService.SweepAsync`，每 30 秒 | `timer:LoginSessionMaintenanceService` | `NewRoot(Timer)` | `sessions.SweepExpired()` |

- **传输层自己的状态留在作业外**：`Connections.Add` 与 `Connections.TryRemove` 是附录 B「传输层的连接注册」，在回调里、入队之前执行，顺序与今天相同（`Add` 先于观察者，`TryRemove` 先于观察者）。
- **调用链的过渡期标注**：本单元四种 CMD 一律是新根。按 TASK-02 的口径，「CMD 登记的关连接请求」引起的 `lifecycle:disconnected` 应继承登记它的 CMD 的链，传输层自发的关闭才是新根；但发件箱尚未接线，关闭回调也分辨不了关闭是谁发起的，所以过渡期统一取新根，来源种类为 `Lifecycle`、`SourceConnection` 为该连接。`lifecycle:close-superseded` 同理：它执行的是登录 CMD 提交时登记的关闭请求，T-006 把它改为登录 CMD 发件箱里的关连接条目，T-007 让它继承链。两点都写进 Result 的 Remaining。
- **TASK-08 提纲列出的 `lifecycle:close-cleanup` 本单元不出现**：理由同上一条，关闭回调分不出「关连接后的清理」与「断线清理」。T-007 区分时再引入（待裁定第 4 条）。
- **令牌**：两个回调传 `CancellationToken.None`，与今天相同；维护服务传自己的 `stoppingToken`。
- **失败**：作业抛出时，执行器记一条 `Gameplay lifecycle:X failed`（Error），异常原样回到回调，回调照旧抛给 TouchSocket，与今天「观察者抛出就从回调抛出」一致。TASK-09 待裁定第 4 条的双日志在这里同样存在，交 T-008 一并处理。

### 2. 入队位置：连接生命周期分发器

**为什么不在插件里直接入队**：`Networking` 至今只认识 `INetworkMessageDispatcher` 这一个接缝（Plan 共享决定「分层」），它不认识执行器、调用链和 CMD 类型的命名。TASK-09 让 dispatcher 的实现去入队；生命周期照同一形状做，传输侧就只多一个接缝，不多一份执行器知识。备选是插件直接注入 `IGameplayQueue`，见待裁定第 1 条。

**接口**（Abstractions，放在 `INetworkConnectionObserver` 旁边；名字由执行者定，下文称 `INetworkConnectionLifecycleDispatcher`）：

```
ValueTask DispatchConnectedAsync(NetworkConnectionContext connection, CancellationToken ct);
ValueTask DispatchDisconnectedAsync(NetworkConnectionContext connection, CancellationToken ct);
// 返回即该连接的生命周期 CMD 已执行完毕（RQ-005 同口径：执行完，不含交付）
```

**实现**（Application/Dispatching）：注入 `IGameplayQueue`、`IEnumerable<INetworkConnectionObserver>`、日志。

```
DispatchDisconnectedAsync(connection, ct):
  if observers 为空: return                       // 没有工作就不入队
  try:
    await queue.RunAsync(new GameplayWork(
        GameplayShardKey.World,
        GameplayCallChain.NewRoot(GameplayCallSource.Lifecycle, connection.ConnectionId),
        "lifecycle:disconnected",
        async token => { foreach (var o in observers) await o.DisconnectedAsync(connection, token); }), ct)
  catch (GameplayExecutorStoppedException):
    Warning「执行器已停止，lifecycle:disconnected 未执行」（连接 ID）；重新抛出
```

- `foreach` 的内容与 `NetworkPluginBase` 今天的循环逐字相同，观察者顺序就是 DI 的注册顺序（`ApplicationModule` 的 `LoginSessionConnectionObserver` 在前，`ProtocolModule` 的 `OnlineUserConnectionObserver` 在后），不改变。
- 观察者今天不在 DI scope 里调用，作业里也不建 scope。
- 注册：`AddNetworkMessageDispatching` 里 `TryAddSingleton`。凡是能解析出插件的容器都已经调用过它（插件本来就需要 `INetworkMessageDispatcher`）。只调 `NetworkingModule`、不调 `ApplicationModule` 的两个注册测试（`EpicCraftRegistrationTests`、`RecipeCraftRegistrationTests`）若因此解析失败，按 §9 (iv) 处理。
- 停止后的 Warning 是 AC-007 停机测试的观测点（§8 S1、S3 断言它为 0），所以要有。

**插件**：`NetworkPluginBase` 的构造参数 `IEnumerable<INetworkConnectionObserver>` 换成这个分发器；两个回调改为：

```
public Task OnTcpConnected(client, e) => GameplayJob.RunDetachedAsync(async () =>
{
    var context = Connections.Add(client, EndpointKind);
    await lifecycle.DispatchConnectedAsync(context, CancellationToken.None);
    await e.InvokeNext();
}).AsTask();

public Task OnTcpClosed(client, e) => GameplayJob.RunDetachedAsync(async () =>
{
    Connections.BeginClosing();                     // 在途关闭回调计数，§6.3
    try
    {
        var id = CreateConnectionId(client, EndpointKind);
        if (Connections.TryRemove(id, out var context))
            await lifecycle.DispatchDisconnectedAsync(context!, CancellationToken.None);
    }
    finally { Connections.EndClosing(); }
    await e.InvokeNext();
}).AsTask();
```

`ChannelNetworkPlugin`、`GameNetworkPlugin` 只把构造参数原样传给基类。Channel 连接也会入队一个作业（两个观察者对 Channel 都直接返回），代价是每次 Channel 连接多一个空作业；传输侧不认识观察者，不在这里按端点分流。

### 3. 关连接器

- `CloseDetachedAsync` 去掉 `Task.Yield`，改为在 `ExecutionContext.SuppressFlow()` 下 `Task.Run` 启动关闭；已处于 `SuppressFlow` 时不嵌套（参照 `GameplayExecutor.EnsureStarted`）。失败仍以 Warning `Requested network connection close failed` 记录，日志失败也吞掉。
  - 可以把 `ExecutorSendDeadline.StartDetachedClose` 改为接收失败消息，两处共用；也可以在关连接器里写一份同形的私有方法。由执行者选，以改动小为准。
- **`Task.Run` 与 `Task.Yield` 的等价性**：两者都让关闭不在调用方的调用栈上同步执行，所以「TouchSocket 关闭时要等当前收包回调结束，而 handler 就在那个回调里」这一条照旧成立。唯一的区别是关闭任务不再带着调用方的 `ExecutionContext`。
- **改变了什么帧的可见性**（TASK-08 把它归到本单元的理由）：今天的后台关闭带着调用方的 AsyncLocal 帧，包括作业令牌、过渡门的借用帧、效果操作帧；如果 TouchSocket 在 `CloseAsync` 里内联调用关闭回调，观察者就在这些帧上运行。本单元之后，关闭回调本身已是流边界，观察者在执行器的作业里运行，看不到任何调用方帧。两处改动合在一起，观察者看到的帧与「另一条连接上的普通断开」完全相同，这正是要的结果。
- 调用方（`GameLoginHandler.RejectAsync`、`SelectCharacterHandler`、`ReturnToSelectionHandler`、两塔与进本投递、`CreatureRuntimeDelivery`、`SelectedSessionSafety`、`LoginSessionMaintenanceService`）都不等待关闭完成，接口文档本来就这样承诺，不受影响。
- 本单元之后的分离点清单（3e）：`ExecutorSendDeadline.StartDetachedClose`（TASK-08）、关连接器（本单元）、卡牌计时器（TASK-11）、TASK-05 的准入循环与排空循环（T-006 接线时复核）。

### 4. 登录会话

- **连接观察**：`LoginSessionConnectionObserver` 是 `INetworkConnectionObserver` 之一，随 §2 进入 `lifecycle:disconnected` 作业，代码不改。它写的 `LoginSessionRegistry`（`superseded`、`byConnection`、`pendingBindings`）从此只在执行器上被断连路径写入。
- **顶替关闭**：`LoginSessionMaintenanceService` 增加注入 `IGameplayQueue`。`CloseSupersededAsync` 仍在执行器外读通道（「读取到期项」，只读），每读到一个连接，就把原来循环体里的两步（`TryDisconnectConnection`、`RequestCloseAsync`）作为一个 `lifecycle:close-superseded` 作业入队并等待。
  - 原来的 `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }` 与 Warning 捕获保持不变，都放在等待作业的外面：作业抛出的异常照旧被记成「Could not close a superseded game connection」。
  - 不用 `CoalescingGameplaySource`：每个请求是不同连接的关闭，合并就会丢请求。循环逐个等待，同一来源同时至多一条在途，RQ-006 自然成立。
- **过期扫描**：`SweepAsync` 每次到期用一个 `CoalescingGameplaySource`（服务实例自己持有一个）入队 `timer:LoginSessionMaintenanceService`，并等待返回的 `Completion`。
  - 因为循环等待完成，合并在这条循环里实际不会发生；用它是为了与 TASK-11 的定时源统一接线（TASK-08 提纲的写法）。
  - 作业抛出的异常照旧从 `ExecuteAsync` 冒出，与今天 `SweepExpired` 抛出时的结果相同。
  - 扫描间隔改为 `internal TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(30)`，测试可以调短；或者由测试提供一个可手动触发的 `TimeProvider`。由执行者选。

### 5. 等待关系与 R-5 的等待环

**本单元之后，谁在等生命周期作业**：TouchSocket 的连接回调与关闭回调、TCP 服务停止（它关闭会话时会等关闭回调）、§6 的传输排空、维护循环。它们都不持有任何门，所以拆分原则第 3 条的不变量成立（Required check 9 逐个复核）。

**环 1：CMD 关闭自己的来源连接**（AC-007「不自锁」）。作业 J 处理连接 A 的入站，请求关闭 A。关闭在分离的任务里，要等 A 的收包回调结束；收包回调在等 J。J 不等关闭，J 结束 → 收包回调返回 → 关闭继续 → A 的关闭回调入队 `lifecycle:disconnected` 并等待 → 它在 J 之后执行。不成环。W7 证明。

**环 2：CMD 关闭另一条连接**。J 在连接 A 上请求关闭空闲连接 B。B 的关闭不等 A 的回调；B 的关闭回调入队的作业排在 J 之后。本单元之前（TASK-09 状态），这段清理继承了 J 的令牌、与 J 并发执行；现在它严格排在 J 之后。W8 证明。

**环 3：R-5——发送失败时内联触发关闭回调**。作业 J 直发给连接 C；如果 TouchSocket 在这次发送失败的调用栈上**内联**调用 C 的关闭回调：回调经流边界清掉令牌 → 入队 C 的 `lifecycle:disconnected` 并等待 → 它排在 J 之后 → J 在等这次发送 → 环。TASK-08 的期限打破它：

- J 在 2 秒后以 `TimeoutException` 放弃这次发送（`ExecutorSendDeadline` 同时在 `SuppressFlow` 下关闭 C 的会话，此时它已在关闭中，无害）；
- J 结束后，C 的清理作业执行，关闭回调返回，被放弃的发送任务完成并被观察；
- 实际代价：执行器停 2 秒；J 看到的是 `TimeoutException`，而不是今天那条传输异常。两者都走发送失败的路径（附录 E 今天的语义不变），但 `catch (IOException)` 这类只认传输异常的分支会走到外层。

是否真的会内联，要实测：

- **基线上的旁证**：今天关闭回调在回调里直接运行观察者，观察者要独占过渡门与在线用户门。假如发送失败会内联关闭回调，那么任何「持门发送」的路径（选角、进本投递）一旦对端重置，观察者就会在持门者自己的流上等那把门，今天就已经会死锁。基线没有这类报告，说明多半不内联；但这是旁证，不是证据。
- **K 用例**（§8）：作业里对一个发送 RST 的客户端持续直发。作业体在发送前设一个测试专用的 AsyncLocal 标记；记录型观察者在 `DisconnectedAsync` 里读这个标记——如果关闭回调内联在发送里，它会继承调用方的 ExecutionContext，标记可见；如果在 TouchSocket 自己的循环上，标记不可见。K 只**记录**是否内联、记录作业结束的耗时，**断言**的是：作业在「期限 ＋ 3 秒」内结束；C 的 `lifecycle:disconnected` 随后执行；紧接着入队的作业 1 秒内执行。
- **处置**：
  - K 显示不内联：环 3 不存在，记入 Result；
  - K 显示内联、且期限打破了它：按上文代价记入 Remaining，T-006 把发送移出执行器后消失；同时提出待裁定第 3 条的备选；
  - K 显示内联、且 J 在期限后仍不能结束（例如 TouchSocket 在发送里同步阻塞着调用关闭回调）：**停止本单元，回报主控方**。这时 Plan 过渡期规则 3 的前提不成立。
- **部分迁移期的变体**：执行器外的持门者 W（TASK-11～13 的生产者）向 C 直发，执行器外的发送没有期限。如果发送内联了 C 的关闭回调，回调等 C 的清理作业，执行器上恰好有一个作业在等 W 持有的门，就是一个不受期限保护的环。它和「基线上的旁证」是同一个前提：只有 K 显示内联时才存在；那时要在 Remaining 里登记，并在 TASK-11～13 的风险中注明。

**与 RQ-008 的过渡期出入**：RQ-008 要求「收包回调与 HTTP 请求所等待的东西里，不包含关连接与交付」。本单元之后，关闭回调等待的生命周期作业里含有直发（清理通告），这是 T-006 之前的过渡态，在 Result 中登记，T-006 改为登记发件箱后消失（待裁定第 6 条）。

### 6. 停机顺序

#### 6.1 宿主停止的次序（.NET 10 Generic Host）

`Host.StopAsync` 依次：各 `IHostedLifecycleService.StoppingAsync` → 各托管服务的 `StopAsync`（注册的逆序）→ 各 `IHostedLifecycleService.StoppedAsync`。所以只要执行器在 `StoppedAsync` 里停止，就一定排在 Kestrel（`GenericWebHostService`）、两个 TCP 服务、全部 `BackgroundService` 的 `StopAsync` 之后，**与注册顺序无关**。这是选择 `StoppedAsync` 的原因；放在 `StopAsync` 要靠「`ApplicationModule` 先于 GameServer 与 HttpApi 注册」这个约定，Program.cs 与各测试宿主今天都满足，但没有东西守着它。

#### 6.2 执行器生命周期服务（Application/Execution）

- `GameplayExecutorLifetime : IHostedLifecycleService`，注入 `GameplayExecutor`、`IEnumerable<IGameplayProducerDrain>`（§6.3）、日志；由 `AddGameplayExecution` 以 `TryAddEnumerable` 注册。它只有 `StoppedAsync` 做事，其余五个方法立即返回。
- `StoppedAsync(ct)`：
  1. 按注册顺序 `await drain.DrainAsync(ct)`；单个排空失败或超时记 Warning，继续下一个；
  2. `await executor.StopAsync(ct)`：拒绝新工作，执行完已入队的全部作业，消费循环结束；
  3. 记一条 Information，含停止耗时，S3 用它确认真实进程走到了这一步；
  4. `ct` 到期（`HostOptions.ShutdownTimeout`，默认 30 秒）时，`StopAsync` 抛出取消，宿主照常记录；之后容器释放时，执行器的 `DisposeAsync` 再等 10 秒并放弃余下作业。这条不加新逻辑，只在 Result 写明。
- 执行器仍按 Plan 在首次入队时启动；生命周期服务不启动它。没有作业就停止时，`StopAsync` 启动并立即结束空循环。
- **影响面**：凡是用 `ApplicationModule` 组装、走 `IHost` 启停的宿主，`StopAsync` 之后执行器都已停止，此后再向它入队会得到 `GameplayExecutorStoppedException`。只用 `ServiceCollection` 自建、不启停宿主的测试容器不受影响，它们的执行器仍随容器释放而停止（TASK-08）。§9 (vi) 处理受影响的测试。

#### 6.3 传输排空（Networking）

`TouchSocket` 的 TCP 服务停止时会不会关闭全部会话、会不会等它们的关闭回调结束，文档没有写。执行器停止前必须确定「每个连接的 `lifecycle:disconnected` 都已入队」，否则这些拆除在执行器停止后入队，会被拒绝，位置就丢了。所以不依赖 TouchSocket 的内部行为，显式排空：

- Abstractions/Execution 新增 `IGameplayProducerDrain`（名字由执行者定）：`ValueTask DrainAsync(CancellationToken)`，「返回时，这个生产者不再产生新工作，它已经开始的工作都已入队或完成」。它不是 `*DomainService`，命令处理器注入不到它。
- Networking 新增实现，`NetworkingModule` 在 `Enabled` 判断之前注册（禁用网络时注册表为空，立即返回）：
  1. 对 `TouchSocketConnectionRegistry` 里仍登记着的每个连接，按 §3 的方式请求关闭；
  2. 等到注册表为空、且在途关闭回调计数为 0。等待用注册表在计数归零时完成的 TCS，不轮询；受 `ct` 约束。
- `TouchSocketConnectionRegistry` 增加枚举当前条目、`BeginClosing`／`EndClosing` 计数与「全部结束」的等待。计数在 `OnTcpClosed` 进入时加一、退出时减一（§2 的代码），覆盖 `TryRemove` 与等待作业的整段。
- **第 0 步实测**（先于实现，写进 Result）：在 S1 的搭法下，不加排空，只观察 TCP 服务的 `StopAsync` 返回时，每个会话的关闭回调是否已经结束。结论只用于解释 S1 的结果与 Result 中的说明，实现不随它分叉：即使 TouchSocket 已经关好了，排空也只是空跑一遍。

#### 6.4 Kestrel 与后台服务

- Kestrel 的 `StopAsync` 停止接受新请求，并等在途请求结束。在途请求若在等 CMD（TASK-13 之后才会有），执行器那时仍在运行，可以完成。
- `BackgroundService` 的 `StopAsync` 取消 `ExecuteAsync` 并等它结束。正在等作业的生产者，在作业完成后退出。`LoginSessionMaintenanceService` 通道里尚未处理的顶替关闭请求随停机丢弃，与今天相同；那些连接随后被传输排空关闭，拆除照样执行。

#### 6.5 AC-007 停机测试的设计

- **S1（无 PVF，必跑；Networking.Tests/Integration）**：搭法同 TASK-09 的 W 系列（`ApplicationModule`＋`NetworkingModule`＋`ProtocolModule`＋`TestInfrastructure`，真实 TouchSocket），另在真实观察者之后追加一个记录型观察者，其 `DisconnectedAsync` 记录 `IsOnExecutor`、`GameplayJob.Current`（来源、CMD 类型、`SourceConnection`）、此刻 `GameplayExecutor.Completion.IsCompleted`，然后 `await Task.Delay(200)` 模拟一次慢的拆除写。宿主日志接一个记录型提供程序。
  - 连上 K 个 Game 客户端（K ≥ 3，各自读到初始 NOTI），不断开，直接 `host.StopAsync`；
  - 断言：K 条断开记录，全部在执行器上、`lifecycle:disconnected`、来源 `Lifecycle`、`SourceConnection` 是服务端登记的连接 ID、记录时 `Completion` 未完成；`StopAsync` 返回时 `Completion` 已完成；「执行器已停止，生命周期 CMD 未执行」的 Warning 为 0；`did not drain within` 为 0；
  - 变异核对见 Required check 7。
- **S2（需要 Script.pvf，SkippableFact；Networking.Tests/Integration，复用 `CharacterizationHost`）**：在线玩家的位置在停机拆除时落库。
  - `EnterWorldAsync` 进城；读出该角色在运行态的坐标（`TestGameplay.Run(host.Services, …)` 读 `IOnlineCharacterRuntimeDomainService`，在停机之前做）；
  - 用 SQL 把 `characters` 表里该角色的位置列改成一个哨兵值。这是 `CharacterizationHost` 允许的「测试自己说明理由的直接 SQLite 写」，理由：只有拆除会把运行态坐标写回，哨兵值被覆盖就证明拆除在停机时落库了；
  - `host.StopAsync`，然后直接读 SQLite：位置列等于停机前读出的运行态坐标，不再是哨兵；
  - 「CharacterizationHost 只允许修饰 `*DomainService`」的限制不影响本用例，它只读 SQLite。
  - 先在基准 `1eea890f` 上跑一次（临时，不提交），记下基线是否通过：基线上观察者在关闭回调里直接运行，只要 TouchSocket 停止时关闭会话，基线就应当通过；如果基线不通过，说明今天停机本来就丢位置，记入 Result，本单元的排空使它通过。
- **S3（真实进程；Host.Tests，非 Windows）**：用 `EndToEndHostHarness` 拉起真实的 Host 进程，经发布路由连上一个 Game 连接并读到初始 NOTI；`EndToEndHostHarness` 新增的优雅停止方法用 `kill -TERM <pid>` 发送 SIGTERM（Generic Host 的 `ConsoleLifetime` 会因此走 `StopAsync`）。断言：进程在 20 秒内以退出码 0 结束；输出里有 §6.2 第 3 步的 Information；没有「未执行」Warning、没有 `did not drain within`。Windows 上没有向子进程发 SIGTERM 的等价手段，用例跳过（待裁定第 7 条）。S3 不断言位置，位置由 S2 证明。

### 7. 卡牌协调器 `Dispose` 在容器释放时取锁

- 现状：`DungeonCardPresentationCoordinator.Dispose` 在 `lock (gate)` 里置位 `disposed`、取出待处理的计时器，然后取消它们；已经释放过也要先取锁再返回。它由容器释放调用。
- 本单元之后，在宿主路径上，容器释放发生在执行器 `Completion` 之后（§6.2），所以这次取锁一定在执行器外、且执行器已经停了。它不改玩法状态，只取消计时器；那时也已没有任何生产者。
- 本单元**不改**协调器：
  - 它是 TASK-11 的文件，计时器回调改为入队定时 CMD 时，`Dispose` 取消计时器的行为要和回调一起看；
  - 在 T-004c 的判据开启之前，这次取锁没有任何后果。
- 本单元提供的证据：探针 §3.9 停机族中，`Host.Dispose`／`ServiceProvider.Dispose` 下的协调器记录照旧存在；S1 另加一条断言，即在 `host.Dispose()` 之前执行器 `Completion` 已完成。
- 给 T-004c 的建议见待裁定第 5 条。

### 8. 新增测试

**连接生命周期分发器**（`Application.Tests/Dispatching/`，用 `AddNetworkMessageDispatching` 自建容器，不包裹）：

| 条 | 情形 | 断言 |
|---|---|---|
| LD1 | 执行器外调用 `DispatchDisconnectedAsync`，注册两个记录型观察者 | 两个观察者按注册顺序执行，都在执行器上；`Current` 的来源 `Lifecycle`、CMD 类型 `lifecycle:disconnected`、`SourceConnection` 是该连接；跑在容器执行器上。`DispatchConnectedAsync` 同样一条，CMD 类型 `lifecycle:connected` |
| LD2 | 第一个观察者抛出 | 异常原样到调用方；第二个观察者未执行（与今天的 `foreach` 相同） |
| LD3 | 在同一执行器的作业里调用 | `GameplaySelfWaitException` |
| LD4 | 执行器已 `StopAsync` 之后调用 | `GameplayExecutorStoppedException`，并有一条「未执行」Warning |
| LD5 | 没有观察者 | 不入队（执行器 `IsStarted` 为假） |

**执行器生命周期服务**（`Application.Tests/Execution/`）：

| 条 | 情形 | 断言 |
|---|---|---|
| E1 | 两个排空替身，第一个在 `DrainAsync` 里向执行器入队一个作业 | `StoppedAsync` 先依次调用两个排空，那个作业执行完，然后执行器 `Completion` 完成；顺序由记录序列证明 |
| E2 | `StartAsync`、`StartingAsync`、`StartedAsync`、`StoppingAsync`、`StopAsync` | 不启动、不停止执行器 |
| E3 | 一个排空抛出 | 记 Warning，继续下一个，执行器照样停止 |
| E4 | 用 `ApplicationModule` 组装的 `IHost` 启动、入队一个作业、`StopAsync` | `StopAsync` 返回时 `Completion` 已完成；之后入队得到 `GameplayExecutorStoppedException` |

**登录会话**（`Application.Tests/Authentication/`，真实 `LoginSessionRegistry` 与 `LoginSessionMaintenanceService`，关连接器与在线用户服务用记录型替身，替身里读 `GameplayJob`）：

- M1 被顶替：经注册表的公开路径制造一次顶替（发行新登录、提交交接），启动维护服务；关连接器替身被调用时，`IsOnExecutor` 为真、CMD 类型 `lifecycle:close-superseded`、来源 `Lifecycle`、`SourceConnection` 是被顶替的连接；`TryDisconnectConnection` 在同一个作业里、且先于关闭请求。
- M2 过期扫描：调短间隔（或手动触发计时器）；一次到期作为 `timer:LoginSessionMaintenanceService` 在执行器上执行，来源 `Timer`；过期会话被移除（可观察的结果，例如该 mId 不能再绑定）。
- M3 停机：`StopAsync` 后维护服务退出，没有未观察异常。

**传输侧**（`Networking.Tests/Runtime/`）：

- C1 关连接器：在一个真实 `GameplayExecutor` 作业里，对注册表里的替身会话（`DispatchProxy` 构造的 `ITcpSession`，参照 `ReceivedStreamBoundaryTests`）调用 `RequestCloseAsync` 与 `RequestCloseExactAsync`；替身的 `CloseAsync` 记录到 `IsOnExecutor` 为假，且作业此时仍在运行（作业等一个 TCS，直到关闭委托把观察结果写回）。
- C2 关闭失败以 Warning `Requested network connection close failed` 记录，`RequestCloseAsync` 本身不抛。
- L1 回调流边界：在一个**辅助执行器**（不带容器的 `TestGameplay`）的作业里直接调用插件的 `OnTcpConnected`、`OnTcpClosed`，使用记录型生命周期分发器；断言分发器被调用时 `IsOnExecutor` 为假，回调返回后作业里恢复为真。不能用同一个执行器：流边界清掉令牌之后，生命周期作业会排在当前作业之后，等待就成了互等。这是 `RunDetached` 的已知代价（TASK-09 §9 第 5 条），L1 的注释写明。
- L2 在途关闭计数与等待：两个并发的 `OnTcpClosed`（记录型分发器挂在 TCS 上），排空在两者都返回之前不完成，之后完成。

**线级**（`Networking.Tests/Integration/`，真实 TouchSocket 宿主；搭法同 TASK-09 的 W 系列；记录型观察者追加在真实观察者之后；替身 handler 通过替换对应消息类型的 `MessageHandlerRegistration` 接入）：

| 条 | 对应 | 做法与断言 |
|---|---|---|
| W5 断线 | AC-007 第一种情形；AC-006 第 3 行 | 一个 Game 连接、一个 Channel 连接各自连上再由客户端断开；记录型观察者记到 `lifecycle:connected` 与 `lifecycle:disconnected`，都在执行器上、来源 `Lifecycle`、`SourceConnection` 等于服务端登记的连接 ID。Game 连接的客户端在断开前读到了初始 NOTI；Game 连接在断开前另发一条普通消息，记录序列为「建立 → 入站 → 断开」 |
| W6 断开排在最后一条入站之后 | RQ-008；`:236`、`:261` 的后继 | 替身 handler 在一条入站上等一个 TCS；客户端在 handler 运行中关闭 socket；放行前 `lifecycle:disconnected` 未开始（观察 300 ms 起）；放行后记录序列为「入站进、入站出、断开进、断开出」 |
| W7 CMD 关闭自己的来源连接 | AC-007「不自锁」；`:288` 的后继 | 替身 handler 先直发一帧，再对自己的连接调用 `RequestCloseAsync`，然后 `await Task.Delay(300)` 再返回。断言：客户端先读到那一帧，再读到 FIN（表征过渡期「关闭排在此前发出的消息之后」，发件箱形态由 T-006 重证）；`lifecycle:disconnected` 在入站作业结束之后开始；从请求到客户端读到 FIN 小于 2 秒，日志中没有 `direct send deadline exceeded`；紧接着入队的作业 1 秒内执行。另加一个用真实 `GameLoginHandler` 的变体：无效票据登录被拒，连接关闭，记录型观察者记到它的 `lifecycle:disconnected` 在执行器上 |
| W8 CMD 关闭另一条连接 | AC-007「关连接」情形 | 连接 A 上的替身 handler 请求关闭空闲连接 B，再 `await Task.Delay(300)`；B 的 `lifecycle:disconnected` 在 A 的作业结束之后才开始，在执行器上，`SourceConnection` 是 B |
| W9 被顶替 | AC-007「被顶替」情形 | 若测试宿主带启动器 HTTP 能低成本搭出「已绑定的游戏连接 ＋ 同账号再登录」，经真实维护服务：记录型观察者记到旧连接的 `lifecycle:disconnected` 在执行器上，且旧连接的客户端读到 FIN。搭建成本过高时省略，以 M1 ＋ W5 共同作为被顶替的证据，并在 Result 写明 |
| K R-5 实测 | §5 环 3 | 见 §5 |
| S1、S2 | AC-007 停机 | 见 §6.5 |

**Host.Tests**：S3，见 §6.5。

**后继用例**（`Application.Tests/OnlineUsers/`，真实容器，经生命周期分发器驱动，用于 §10 中冲突用例在 T-004c 删除时引用）：

- P1 替换会话不被清理（`:30`、`:365` 的后继）：先以一个作业选中替换会话，再 `DispatchDisconnectedAsync` 旧连接；替换会话保持选中，`Cleanup` 未被调用。这是执行器语义下的「顺序断言」：两个 CMD 先后执行，没有交错。
- P2 旧代数的断开不删新代数（`:519` 的后继）：先以一个作业完成认证（代数加一），再分发旧连接的断开；在线用户仍在、代数是新的。

先写测试再改生产代码：LD1、LD3、LD4、E1、E4、M1、C1、L1、W5、W8、S1 在改动前应当红，Result 中记录红的形态。W6、W7 在今天的形状下可能就通过（顺序由 TouchSocket 的回调次序保证），它们守的是迁移后不被破坏，Result 写明。

### 9. 测试侧的失败分类与处置

沿用 TASK-09 §7 的 (i)～(v)，加一类本单元特有的 (vi)：

| 类 | 判定 | 处置 |
|---|---|---|
| (i) | 冲突总清单里已有的用例 | 本单元执行其处置（§10） |
| (ii) | 不在清单里，靠两个发起方交错制造并发 | 按 TASK-06 §2.3 判为冲突：改写为顺序断言或删除，追加到 `task-07-conflicts.md` 新的一节，更新总数加法 |
| (iii) | 带容器的包裹区域里触发了真实的连接建立或断开、并在区域内等待它的观察者结果，报自我等待或挂起 | 把触发与等待移出包裹，断言不变 |
| (iv) | 本单元改动本身的缺陷（解析失败、注册遗漏、排空不结束） | 在 Owned boundary 内修正，并补测试 |
| (v) | 区域里开着环境事务、门或帧，再触发真实断开，而断言依赖观察者继承这些 AsyncLocal | 同 TASK-09 (v) |
| (vi) | 宿主 `StopAsync` 之后仍向容器执行器入队（`TestGameplay.Run(host.Services, …)`、经真实 dispatcher 或生命周期分发器），得到 `GameplayExecutorStoppedException` | 把读取移到 `StopAsync` 之前，或改为直接读 SQLite；断言不变。若某条用例必须在停机之后读玩法状态，判为 (ii) 冲突 |

**主动复核**（即使全绿也逐项写结论）：

- **调用 `host.StopAsync` 且用到 `TestGameplay` 的文件**：`GameLoginIntegrationTests`、`BossTowerFrameIntegrationTests`、`IllusionTowerStageFrameIntegrationTests`、`CardRewardReconnectIntegrationTests`、`ItemPathLiveTests`、`DomainEventDispatchTests`、`JoustIntegrationTests`（后两者停的是单个托管服务，不是宿主，逐个确认）；以及 `CharacterizationHost.DisposeAsync`、`ComponentLifecycleTests`。确认没有 (vi) 的写法。
- **直接构造或调用观察者的测试**：`SelectCharacterLifecycleTests`（5 处）、`OnlineUserServiceTests`（6 处）。它们不经插件，本单元不改变它们的执行位置；除 §10 的 7 条外，确认其余用例是整方法包裹或不触及门。
- **注册了额外观察者的测试**：`OutboundSendFailureTests.ConnectionCapture`。它的 `ConnectedAsync` 现在在执行器上运行，只取一把私有锁并 `Release` 信号量，不等任何东西；确认测试线程等信号量时没有作业在等测试线程。
- **直接构造插件的测试**：`ReceivedStreamBoundaryTests` 改构造实参（第二个参数改为记录型或空的生命周期分发器），断言不变。
- **真实 socket 用例里的同步短作业**（GameLogin 6、IllusionTowerStage 4、CardRewardReconnect 4、BossTower 3）：它们现在还可能排在 `lifecycle:connected` 之后，只是延迟；确认没有一个是在等「断开清理已完成」之后才读状态的。若有，它依赖的是今天「客户端断开 → 清理在回调里内联完成」的时序，改为轮询（每次求值一个短作业，TASK-07 §6 的写法）。
- **TASK-09 Remaining 第 7 条（执行器外写者撞 SQLite）**：断连拆除的 `AloneAsync` 从本单元起在执行器上，这一类写者少了一个；确认全量中没有 30 秒忙等。

**两种需要额外留意的形状**（沿用 TASK-08、TASK-09）：在容器执行器的作业里释放容器；释放时执行器上仍有卡住的作业。另加本单元的一种：宿主停止时，执行器上有卡住的作业，`StoppedAsync` 会等到停机期限（默认 30 秒，测试多传更短的令牌）。耗时比对（Required check 2）中出现这类增量的，按 (ii) 或 (iii) 判定。

### 10. 冲突用例 7 条

行号是 `task-07-conflicts.md` 的基准行号（`0223e646`），当前文件里已后移，**以方法名为准**。

**共同的预判**：7 条都是在测试线程上**直接** `new OnlineUserConnectionObserver(...)` 并调用 `DisconnectedAsync`（或直接调 `SelectCharacterHandler`、`ReturnToSelectionHandler`），关连接器是测试替身。本单元只改变「经插件进入观察者」的路径，不改变观察者本身、门和替身，所以预计 **7 条全部仍通过**。按拆分原则第 5 条，仍通过的顺延 T-004c，在那里随门断言失败而处置。

| 基准行 | 方法 | 预判 | 去向 | 后继（承接保护意图的本单元用例） |
|---|---|---|---|---|
| `SelectCharacterLifecycleTests:236` | `DisconnectWinningTransitionGate_PreventsPreparationMutationAndPublication` | 通过 | 顺延 T-004c | W6：同一连接的断开 CMD 只能排在它最后一条入站 CMD 之后，「断开赢得过渡门、选角随后被拒」在生产中变为「断开之后不会再有这条连接的选角」 |
| `…:261` | `CommandHoldingTransitionGate_BlocksDisconnectCleanupUntilRegisteredFlowCompletes` | 通过 | 顺延 T-004c | W6 |
| `…:288` | `ObserverQueuedBeforeRollbackClose_CompletesAfterGateRelease` | 通过（关连接器是替身，`OnClose` 等的是测试直接调用的观察者任务） | 顺延 T-004c | W7 |
| `…:365` | `OrphanedDisconnectWaitingForLifecycleGateDoesNotCleanupAReplacementSession` | 通过 | 顺延 T-004c | P1 |
| `OnlineUserServiceTests:30` | `DisconnectWaitingForLifecycleKeepsItsCapturedSessionAndDoesNotCleanReplacement` | 通过 | 顺延 T-004c | P1 |
| `…:67` | `TeardownWaitingForLifecycleDoesNotTakeOnlineOwnershipInReverse`（Theory） | 通过 | 顺延 T-004c（门删除时直接删除） | 无：它验证的是两把门之间的锁序，串行执行器下没有锁序可言 |
| `…:519` | `DisconnectObservedBeforeAuthentication_DoesNotRemoveNewerGeneration` | 通过 | 顺延 T-004c | P2 |

若实际运行与预判不同（失败或挂起），按 §9 (i) 在本单元执行 `task-07-conflicts.md` 中写的处置（改写为顺序断言或删除），以上表的后继用例作为保护意图的承接，写成「旧断言 → 新断言 → 保护意图」。`task-07-conflicts.md` 追加一节，记录 7 条的去向、后继用例名与新的分布加法（T-004b 剩余 14 － 7 ＝ 7；T-004c 67 ＋ 顺延条数）。是否在本单元就删除，见待裁定第 2 条。

### 11. 探针与 Closed 族新基线

- 做法同 TASK-09 Required check 8：`PROBE_WORK` 指向 scratchpad，`patch.py apply` → 构建 → `runall.sh` 跑四个项目 → `patch.py revert` → 三项还原证明。`classify.py` 的入口帧名单不改：迁入后这些帧在执行器上运行，探针不再记录。新增的生命周期分发器不必进入名单，它在执行器外只做入队。
- 预期与判读：
  - §3.1 Connected/Closed、§3.3、§3.4 的 (A) 记录：首轮只剩直接调用观察者的测试（§10 的 7 条与 `OnlineUserServiceTests`、`SelectCharacterLifecycleTests` 中未包裹的其他调用，如有）；排除复跑（排除整份冲突总清单）为 0；
  - §3.11 第 2 行（`AloneAsync` 经 `OnTcpClosed`）为 0；
  - **Closed 族新基线**：三列并列（TASK-07 202 记录 ／ 1191 命中 → TASK-09 164 ／ 1210 → 本单元）。本单元修好关连接器之后的这一列，作为 TASK-11～14 对照 Closed 族的基线；
  - §3.9：`Host.ForeachService` 下 `OnTcpClosed` 一类的记录应为 0（拆除已在执行器上）；协调器 `Dispose` 记录仍在（§7），单列，不算残留；
  - 其余各族与 TASK-09 并列，下降的说明原因（例如栈形变化、40 帧截断）。
- 探针轮中的失败不作为任何结论的证据（TASK-07 §7）。

### 12. 执行顺序

1. **第 0 步实测**（§6.3）：基准代码上搭 S1 的宿主，观察 TCP 服务 `StopAsync` 返回时关闭回调是否已结束；同时在基准上跑一次 S2（若有 PVF），记录基线结果。都只在 scratchpad 或临时测试里做，不提交。
2. C1、C2 → 关连接器改为 `SuppressFlow`；跑 Networking.Tests。
3. LD1～LD5 → Abstractions 接口、Application 实现与注册；L1、L2 → 插件改造与注册表计数；跑 Networking.Tests、Application.Tests。
4. M1～M3 → `LoginSessionMaintenanceService`；跑 Application.Tests 与 Persistence.Tests（`LoginSessionReconnectTests` 直接读通道，确认不受影响）。
5. E1～E4 → 执行器生命周期服务与 Abstractions 排空接口；传输排空实现与注册。
6. W5～W9、K、S1、S2、S3；先跑 Networking.Tests，再跑 Host.Tests 与 Protocol.Tests。K 触发 §5 的停止条件时，到此为止。
7. P1、P2（后继用例）。
8. 全量（带挂起检测），按 §9 逐条分类，重跑到稳定；耗时比对。
9. 变异核对（Required check 7）。
10. 探针复跑与归零分析，还原证明。
11. 写对账行（Required check 11）。
12. 正式全量 → 隔离复跑 → 线级与停机 ×10 → Host 真实进程 ×3 → 测试改动核对 → 边界 grep 与不变量复核 → 写 Result。

### 13. 为什么除可见日志与停机次序外行为不变

- 每个连接事件的处理内容与观察者顺序，与今天逐行相同，只是换到执行器上运行。同一连接上，`lifecycle:connected` 先于它的任何入站（预期 TouchSocket 在建立回调结束之前不交来这个连接的数据；这一点没有文档依据，由 W5 的记录序列核对，若观察到入站先于建立，停下回报主控方），`lifecycle:disconnected` 晚于它的最后一条入站（关闭等收包回调结束，W6 证明）。跨连接从并发变为串行，这是 RQ-001 要的。
- 可见差异：
  - 生命周期 CMD 里的直发也受 2 秒期限约束：清理通告遇到不读包的同伴，会在 2 秒后断开它（VC-8 提前，范围从入站扩大到生命周期，登记进 Result）；
  - 生命周期作业失败时多一条执行器的 Error 日志；执行器停止之后才到达的关闭回调多一条「未执行」Warning（今天它们会在回调里直接执行）；
  - 宿主停止时，剩余连接由传输排空主动关闭，拆除在执行器停止之前全部完成；`StopAsync` 之后执行器不再接受工作。
  - 线上字节不变，由表征测试与既有线级测试守着。

## 待裁定

1. **生命周期 CMD 的入队位置**（§2）：本方案新增 Abstractions 接缝（连接生命周期分发器），实现放在 Application，插件只依赖接缝，`Networking` 中不出现 `IGameplayQueue`。备选：插件直接注入 `IGameplayQueue` 并自己拼 CMD 类型，少一个接口，但传输侧开始认识执行器与 CMD 命名，与 TASK-09 的 dispatcher 形状不对称。建议前者。
2. **7 条冲突用例**（§10）：预计全部仍通过，按规则顺延 T-004c，本单元给出后继用例 W6、W7、P1、P2。另一种做法是本单元就删除其中 6 条（`:67` 除外，它随门删除）：生产路径在本单元已经串行，它们制造的交错在生产中不再可能发生；代价是违反「只在使它失败的那个单元执行」的拆分原则。建议按规则顺延。
3. **R-5 的处置**（§5 环 3）：本方案保留 Plan 规定的「关闭回调入队并等待」，环由 2 秒期限打破。若 K 显示 TouchSocket 确实内联，备选是：关闭回调发现自己是从一个活作业里脱离出来时，只入队、不等待。这样不再停 2 秒，但需要 `GameplayJob` 公开「这次脱离时外层是否有活令牌」，并让这一种关闭回调先于清理完成而返回。只在 K 显示内联时才需要裁定。
4. **CMD 类型命名**（§1）：`lifecycle:connected`、`lifecycle:disconnected`、`lifecycle:close-superseded`、`timer:LoginSessionMaintenanceService`；TASK-08 提纲中的 `lifecycle:close-cleanup` 本单元不出现，由 T-007 在能区分关闭发起者时引入。全部取新根，T-006／T-007 改为继承。请确认。
5. **卡牌协调器 `Dispose`**（§7）：建议 T-004c 开启判据时，把「执行器 `Completion` 已完成之后的容器释放」列为停机期例外，在附录 B 增一行，与「开始监听前的启动期工作」对称；本单元不改协调器。备选：TASK-11 把计时器的取消改为停止前的一个作业，并让 `Dispose` 在已释放时不再取锁。请选择，或交 TASK-11 规划时再定。
6. **RQ-008 的过渡期出入**（§5 末尾）：关闭回调等待的生命周期作业里含直发，T-006 之前不满足 RQ-008「回调等待的东西里不含交付」的字面要求。请确认作为过渡态登记，T-006 时核对。
7. **AC-007 停机测试的证据构成**（§6.5）：S1（无 PVF，次序）＋ S2（需 PVF，位置不丢失，CI 无档案时跳过）＋ S3（真实进程，SIGTERM，Windows 跳过）。CI 上若 S2 总被跳过，「位置不丢失」只有本机证据。请确认可以接受；不能接受的话，需要另设一个无 PVF 的「在线并已入世」搭法（在测试里直接经领域服务选中并发布会话），工作量明显更大。
8. **新增的宿主可见行为**（§6.2）：凡是用 `ApplicationModule` 组装的 `IHost`，`StopAsync` 之后执行器即停止。这是 RQ-009 的直接后果，但会让「停机之后再读玩法状态」的测试写法失效（§9 (vi)）。请确认。
9. **事实出入，供参考，不阻断**：
   - 拆分表把 `LoginSessionMaintenanceService` 的过期扫描写作「定时 CMD（经 `CoalescingGameplaySource`）」；它的循环本来就逐次等待，合并在这条循环里不会发生，本方案照写法接线，AC-004 的合并测试仍归 TASK-11；
   - 附录 A 第 3 行把「孤儿清理」列为独立的一项；它在代码里是断连观察者的一个分支，本方案归入 `lifecycle:disconnected`，不单设 CMD 类型。

## 主控方裁定（执行前）

1. **生命周期入队**走新的 Abstractions 接缝，与 TASK-09 的 dispatcher 对称：接受。
2. **7 条冲突用例**顺延到 T-004c：接受。
3. **K 用例如果显示 TouchSocket 会在发送失败时内联调用 Closed 回调**：停下来回报主控方，不要预先新增 `GameplayJob` 公开 API。如果实测不会内联，就把结果写进 Result，作为 R-5 的现状证据。
4. **CMD 类型命名**按本文件：接受。`close-cleanup` 推迟到 T-007。
5. **卡牌协调器的 `Dispose`**：交给 TASK-11 处理。迁移卡牌计时器时，一并改掉它在容器释放时取锁的路径。本单元不新增附录 B 的例外。如果 TASK-11 之后仍然需要例外，到 T-004c 再裁定。
6. **关闭回调等待的作业里含直发**：作为过渡态接受，在 Result 中登记；T-006 把发送移出执行器之后，这个问题随之消除。
7. **AC-007 的「位置不丢失」**只有依赖 PVF 的 S2 能证明，CI 上会被跳过：接受只有本机证据。CI 没有 PVF 是既有条件。
8. **宿主 `StopAsync` 之后执行器停止**：接受，这是 RQ-009 要求的行为。因此失效的测试，只允许把读取挪到停止之前，断言不变，改动逐条记录。
9. **两处事实出入**：接受。一是过期扫描本来就逐次等待；二是孤儿清理归入 `lifecycle:disconnected`。

## Result

**第 2 轮**（结果审查 `reviews/task-10-result-review.md`：changes_required，主控方处置为 task-rework）。本轮在第 1 轮正文上原地修订，改动如下：

- **B-1**：Remaining 第 3 条的 W7 数字改为日志实际值，并补了同负载对照（见 Checks 12）。
- **B-2**：Remaining 第 4 条改写。
- **N-1**：关连接器恢复基线的 `await Task.Yield()` 起头方法，只是改在 `SuppressFlow` 下调用，不再用 `Task.Run`。
- **N-2**：排空兜底按连接隔离失败，补测试 L5。
- **N-3**：补排空的注册断言（Theory 2 例）。
- **N-4**：Remaining 单列 S1 那次失败的取证缺口。
- **N-5**：`:519` 的承接改为「W6 ＋ P2」。
- 新增测试由 31 增到 34。Checks 1～3、7～9、12 换成本轮的数字；Checks 4～6、10、11 保留第 1 轮的证据，并写明它们对本轮改动的适用范围。

### Outcome

- **第 0 步实测**（`logs/task-10-step0.txt`）：TouchSocket 4.3.1 的 `TcpService.StopAsync` 会逐个关闭仍打开的会话，并等每个关闭回调（整条插件链）结束后才返回；三次测量形状相同。基准上 S2 通过，说明今天的停机本来就会把位置写回。
- **K 实测：不内联**（裁定第 3 条的停止条件未触发）。共 55 次尝试：隔离 10 轮各 5 次，正式全量负载下 5 次。关闭回调都看不到作业设置的 AsyncLocal 标记，`K-INLINE=False` 55/55。发送作业在对端重置后 0～1 ms 内以 `SocketException` 或 `ClientNotConnectedException` 结束，没有一次靠 2 秒期限脱身。所以在 TouchSocket 4.3.1 上，环 3（R-5）及其部分迁移期变体都不存在。这是现状证据，升级 TouchSocket 时要复核。
- **连接生命周期 CMD**：
  - Game 与 Channel 的 `OnTcpConnected`／`OnTcpClosed` 整体包在 `GameplayJob.RunDetachedAsync` 里。注册表的 `Add`／`TryRemove` 仍在回调里、入队之前执行。
  - 回调把连接交给新的 `INetworkConnectionLifecycleDispatcher`。它把「按注册顺序依次调用全部观察者」整段作为一个作业入队，并等待作业完成。CMD 类型为 `lifecycle:connected`／`lifecycle:disconnected`，`NewRoot(Lifecycle, 连接 ID)`。
  - 观察者代码一字未改；执行器已停止时，记一条 Warning「… did not run」并重新抛出。
- **关连接器**（第 2 轮改，N-1）：仍是基线那个以 `await Task.Yield()` 起头的异步方法，只是改在 `ExecutionContext.SuppressFlow()` 下调用。方法同步执行到 `Yield` 就返回，`Undo` 仍在同一线程上；续体捕获到的是空的执行上下文。所以关闭的调度与基线逐字节相同，只是不再继承调用方的作业令牌与帧。第 1 轮用的 `Task.Run` 会进当前工作线程的本地队列，调度与基线不同（审查 N-1）。`ExecutorSendDeadline` 未改；关闭失败照旧记 Warning `Requested network connection close failed`。
- **登录会话**：
  - `LoginSessionMaintenanceService` 注入 `IGameplayQueue`。每个顶替关闭请求作为一个 `lifecycle:close-superseded` 作业（`NewRoot(Lifecycle, 被顶替连接)`），作业内依次执行 `TryDisconnectConnection` 和 `RequestCloseAsync`。
  - 每次过期扫描经服务自己持有的 `CoalescingGameplaySource` 入队 `timer:LoginSessionMaintenanceService`（`NewRoot(Timer)`），并等待它完成。
  - 扫描间隔改为 `internal SweepInterval`，默认 30 秒。
- **停机次序**：
  - `AddGameplayExecution` 以 `TryAddEnumerable` 注册 `GameplayExecutorLifetime : IHostedLifecycleService`。它只在 `StoppedAsync` 里做事：先依次排空 `IGameplayProducerDrain`（失败记 Warning 后继续下一个），再 `executor.StopAsync`，最后记 Information「The gameplay executor stopped after draining its producers in … ms」。
  - `NetworkingModule` 在 `Enabled` 判断之前注册 `TouchSocketTransportDrain`。
- **§9 (iv) 修正：TouchSocket 会丢关闭回调**。第一次正式全量与负载复现中出现了 20 次「executor `StopAsync` 在 30 秒后取消」。Trace 日志显示：执行器上没有卡住的作业；卡住的是排空在等注册表清空。
  - 原因：客户端恰在服务停止时断开，这个连接的关闭回调永远不来，条目一直留在注册表里，排空就等到停机令牌到期。
  - 修正：排空对停机后仍登记的每个连接，先请求关闭，再自己 `TryRemove` 并分发 `lifecycle:disconnected`。`TryRemove` 决定由排空还是迟到的回调执行拆除，所以拆除恰好一次。补测试 L4。
  - 第 2 轮（N-2）：每个连接的兜底拆除各包一层 try/catch。失败记 Error「The drain's teardown of connection … failed」后继续下一个连接，最后照常 `WhenIdleAsync`；只有停机令牌取消时向上抛出。补测试 L5。
  - 兜底拆除用宿主的停机令牌，正常关闭回调用 `CancellationToken.None`。两者只在停机期限到期时有区别：兜底拆除的作业若还在排队，会被取消而不执行。
  - 修正后负载复现 4 轮、第 1 轮正式全量都没有再出现停机取消；第 1 轮正式全量中排空自行拆除了 6 个连接（日志 `the drain tears it down`）。基准上是否同样会丢这些回调没有实测，见 Remaining 第 4 条。
- **冲突用例**：7 条在正式全量中全部通过，按裁定第 2 条顺延 T-004c，后继用例见 `task-07-conflicts.md` §8。没有新的 (ii)、(v)、(vi) 类冲突。
- **W9 未省略**：经真实启动器命令与维护服务实现。

### Changed

**生产代码**：`git diff --stat -- Server/src` 显示 8 个文件 ＋143 −25（第 2 轮），另有 4 个新文件。全部在 Owned boundary 内，Host、GameServer、HttpApi、ProtocolModule 与观察者未改。

| 文件 | 改动 |
|---|---|
| `Abstractions/Networking/INetworkServices.cs` | 新增 `INetworkConnectionLifecycleDispatcher` |
| `Abstractions/Execution/GameplayProducerDrain.cs`（新） | `IGameplayProducerDrain` |
| `Application/Dispatching/NetworkConnectionLifecycleDispatcher.cs`（新） | 生命周期分发器；没有观察者时不入队 |
| `Application/Dispatching/NetworkMessageDispatchingExtensions.cs` | `TryAddSingleton` 注册分发器 |
| `Application/Execution/GameplayExecutorLifetime.cs`（新） | 执行器生命周期服务 |
| `Application/Execution/GameplayQueue.cs` | `AddGameplayExecution` 注册生命周期服务 |
| `Application/Authentication/LoginSessionRuntime.cs` | 维护服务两条循环改为入队 |
| `Networking/Runtime/NetworkPlugins.cs` | 只改 `NetworkPluginBase` 构造参数与两个回调，两个子插件把参数原样传给基类 |
| `Networking/Runtime/TouchSocketNetworkConnectionCloser.cs` | 在 `SuppressFlow` 下调用以 `Task.Yield` 起头的关闭方法；记日志失败也吞掉 |
| `Networking/Runtime/TouchSocketConnectionRegistry.cs` | `Snapshot`、`BeginClosing`／`EndClosing`、`WhenIdleAsync`（TCS，不轮询） |
| `Networking/Runtime/TouchSocketTransportDrain.cs`（新） | 传输排空，含丢失回调的兜底拆除；每个连接的失败互相隔离 |
| `Networking/NetworkingModule.cs` | 注册传输排空 |

**新增测试 N ＝ 15 ＋ 18 ＋ 1 ＝ 34，删除 D ＝ 0**（第 1 轮 31 条，第 2 轮在 Networking 加 3 条：L5 1 条，注册断言 Theory 2 例）

- Application 15：
  - `Dispatching/ConnectionLifecycleDispatchTests` 6 条：LD1 拆成断开、建立两条，加 LD2～LD5；
  - `Execution/GameplayExecutorLifetimeTests` 4 条：E1～E4；
  - `Authentication/LoginSessionMaintenanceExecutorTests` 3 条：M1～M3；
  - `OnlineUsers/SerialDisconnectTests` 2 条：P1、P2。
  - 加法：6 ＋ 4 ＋ 3 ＋ 2 ＝ 15。
- Networking 18：
  - `Runtime/DetachedCloseTests` 2 条：C1、C2；
  - `Runtime/ConnectionCallbackBoundaryTests` 5 条：L1；L2；L3（排空关闭仍登记的连接并等它的回调）；L4（回调永不到来时由排空拆除，迟到的回调不重复拆除）；L5 `A_teardown_that_fails_in_the_drain_does_not_cost_the_other_connections_theirs`（第 2 轮）；
  - `Runtime/TransportDrainRegistrationTests` 2 例（第 2 轮，Theory：`Networking:Enabled` 为 false、true）：`NetworkingModule` 注册的 `IGameplayProducerDrain` 中恰有一个 `TouchSocketTransportDrain`；
  - `Integration/LifecycleExecutorWireTests` 7 条：W5、W6、W7、W7 的真实 `GameLoginHandler` 变体、W8、W9、K；
  - `Integration/ShutdownOrderWireTests` 1 条：S1；
  - `Integration/ShutdownPositionPersistenceTests` 1 条：S2。
  - 搭法 `Integration/LifecycleWireHost.cs` 是辅助类，不含测试。
  - 加法：2 ＋ 5 ＋ 2 ＋ 7 ＋ 1 ＋ 1 ＝ 18。
- Host 1：`HostShutdownOrderTests`（S3，`NonWindowsFact`）。

**红的形态**（先写测试，后改生产代码）：

- C1 在改关连接器之前红：关闭看到 `(OnExecutor True, JobEnded False)`。
- LD、E、M、L 在接口、类型或构造器出现之前都是编译红（CS0246／CS1729／CS0117）。
- E4 在去掉生命周期服务注册时红：`Completion` 未完成。
- S2 在基准上通过（见第 0 步）。
- 行为上的红由变异核对补证（Checks 7）：回调直调观察者时，W5～W9、K、S1 共 8 条全红。

**既有测试文件：2 个**（差异核对见 `logs/task-10-diff-check.txt`）：

- `Networking.Tests/Runtime/ReceivedStreamBoundaryTests.cs`：第二个构造实参由 `[]` 改为 `null!`，共两处。方法 2 ＝ 2，断言行 10 ＝ 10，白名单外只有这两处实参。
- `Host.Tests/EndToEndHostHarness.cs`：只加 `StopGracefullyWithAnOpenGameConnectionAsync` 一个方法，其中一条初始 NOTI 检查。基准的 48 条断言行全部保留。

**文档**：

- `verification/task-07-conflicts.md` 追加 §8；
- 日志 `logs/task-10-{build-test,isolated,wire-x10,host-stop,step0,mutations,probe-summary,w7-race,diff-check,r2-directed-x10}.txt` 与 `logs/task-10-probe-{Application,Protocol,Host,Networking}.txt`。第 2 轮覆盖了 `build-test`，追加了 `mutations`、`w7-race`，新增了 `r2-directed-x10`。

### Why（本地决定与偏离）

- **K 读标记的位置**：标记改由生命周期分发器的装饰器读取，没有放在观察者里。执行器的消费循环不带生产者的 `ExecutionContext`，观察者在作业里读，任何情况下都看不到标记，失去区分力。装饰器在 `RunDetachedAsync` 之内、入队之前读，`RunDetached` 只清作业令牌，其他 AsyncLocal 照常可见。
- **P2 的语义**（第 2 轮按 N-5 改口径）：Plan 原写「先认证、再分发同一连接的断开，在线用户仍在」，在串行执行下做不到：断开 CMD 看到的就是认证后的代数，并会移除它。
  - `:519` 原来的情形是同一条连接上「断开先观察到旧代数，认证随后加一」。在串行执行下，同一连接的断开 CMD 只能排在它最后一条入站之后（W6），这种过期的观察不会出现。
  - P2 测的是另一件事：旧连接断开，不影响同账号在另一条连接上的新身份与代数。
  - 所以 `:519` 的后继是「W6 ＋ P2」。P2 里「旧连接身份已不在」那一条断言，在第二次认证时就可能已经成立，不单独证明是断开的效果。
  - P1 承接 `:30`／`:365`：替换会话在另一条连接上。
- **W7 不断言「先帧后 FIN」**：负载下关闭会越过还在 TouchSocket 发送管道里的帧，这是基线缺陷，两条被跳过的表征测试已有记载。W7 改为表征测试用的过渡期形状：FIN 之前是所发内容的任意前缀（0 或 1 帧，逐字节核对）。正式全量负载下打印 `frames before FIN: 0`，隔离 10 次都是 1。严格形式归 T-006（`logs/task-10-w7-race.txt`）。
- **关连接器不与 `ExecutorSendDeadline.StartDetachedClose` 共用**：后者用 `Task.Run`。关连接器要保持基线的 `Task.Yield` 调度（N-1），两处的失败日志文案也不同。
- **S2 访问 `CharacterizationHost` 的私有 `host` 字段**（反射）：它不在本单元可改文件内，而停机之后、删目录之前必须读 SQLite。第二次 `StopAsync` 会被 TouchSocket 以 `ObjectDisposedException` 拒绝，用例只容忍这一种异常。
- **维护服务的异常面略宽**：`TryDisconnectConnection` 移进作业后，它抛出的异常也被「Could not close a superseded game connection」的 Warning 捕获。今天它会让顶替关闭循环退出。这是按 §4 的写法得到的结果。

### §9 主动复核（逐项结论）

| 项 | 结论 |
|---|---|
| (i)～(vi) 计数 | (i) 7 条全部通过，顺延；(ii) 0；(iii) 0；(iv) 1 处，即排空不结束，已修正并补 L4；(v) 0；(vi) 0 |
| 调用 `host.StopAsync` 且用到 `TestGameplay` 的文件 | 全量中全部通过，`GameplayExecutorStoppedException` 与「did not run」均为 0；没有停止之后再入队的写法 |
| 直接构造观察者的测试 | 只有 `OnlineUserServiceTests`、`SelectCharacterLifecycleTests` 两个文件；它们的执行位置不变，探针残留全部来自这两个文件（见 Checks 10） |
| `OutboundSendFailureTests.ConnectionCapture` | `ConnectedAsync` 在执行器上运行，只取私有锁并 `Release`；测试线程等信号量时没有作业在等测试线程。4 条通过 |
| 直接构造插件的测试 | 只有 `ReceivedStreamBoundaryTests`，已改实参 |
| 真实 socket 用例里的同步短作业 | 没有一个依赖「断开清理在回调里内联完成」；全量、隔离与 x10 全部通过 |
| TASK-09 Remaining 第 7 条（SQLite 忙等） | 执行器作业里没有 30 秒忙等。全量中唯一 30 秒的 `SqliteEntryLifecycleRecoveryTests.DungeonRecovery_Overlapping…` 是两个并发用例争同一个库；把 `Server/src` 换回 HEAD 后照样每次 30 秒，与本单元无关（`logs/task-10-isolated.txt` 末尾） |

### Checks

1. **构建**（第 2 轮）：`--no-incremental`：0 个警告，0 个错误（`logs/task-10-build-test.txt` 开头，同时记录了 `git diff --stat`／`git status --short -- Server/src`：8 个文件 ＋143 −25，另 4 个新文件）。
2. **全量**（第 2 轮正式运行，已覆盖 `logs/task-10-build-test.txt`；运行前确认机器上没有 `yes` 进程）：
   - 总数 50 ＋ 80 ＋ 1694 ＋ 475 ＋ 969 ＋ **173** ＋ **4453** ＋ **166** ＝ 8060 ＝ 8026 ＋ 34 － 0。
   - 逐项对照 TASK-09：Networking 155 → 173（＋18）；Application 4438 → 4453（＋15）；Host 165 → 166（＋1）；其余 5 项相等。
   - 通过 50 ＋ 80 ＋ 1694 ＋ 475 ＋ 967 ＋ 171 ＋ 4435 ＋ 166 ＝ 8038；失败 2，只有那 2 条 `DimensionDungeonTests`；跳过 2 ＋ 2 ＋ 16 ＝ 20，新增用例中没有跳过的，S2 在本机运行；8038 ＋ 2 ＋ 20 ＝ 8060。
   - 表征测试 8 通过、2 跳过；挂起转储 0；`## exit 1` 来自那 2 条已知失败。
   - 日志信号：
     - `direct send deadline exceeded`、`did not complete within`、`did not drain within`、`GameplaySelfWait` 都为 0；
     - `Gameplay lifecycle:… failed` 与 `Gameplay timer:… failed` 都为 0；
     - 「… did not run」、`Draining gameplay producer … failed`、`The drain's teardown of connection … failed`、`Requested network connection close failed` 都为 0；
     - 非零的两项：`the drain tears it down` 1，是丢失关闭回调的兜底拆除，第 1 轮为 6；`Gameplay inbound:X failed` 3 条（`GameSelectCharacter` 2、`GameSellItem` 1），与 TASK-09 相同，是用例故意注入的失败。
     - 本轮全量中 K 5 次都是 `K-INLINE=False`；W7 `frames before FIN: 1`。
   - **耗时比对**（与 `logs/task-09-build-test.txt` 逐条比，阈值 9 秒；共比对 7888 条）：多出 ≥ 9 秒的 12 条，全部判为负载波动。
     - 这 12 条是：`DespairTowerTests` 4 条，`PaidCardFlipCostTests` 3 条，`AltarLifecycleTests.Member_departure…(disconnected: True)`，`PvfCreatureEquipmentLifetimeSourceTests.Real_archive_usable…`，`GmServerInfoHttpTests.ServerInfoReports…`，`ItemCatalogHttpTests.Mail_restrictions…`，`GmOfficialMailHttpTests.ExhaustedWireIdentifiers…(2147483647)`。
     - 本轮隔离复跑，它们都在 0.7～2 秒。它们都读真实 PVF 或拉起 GM 宿主；`AltarLifecycleTests` 是 Application 内的领域测试，不经插件与生命周期分发器。
     - 第 1 轮出现过的那条 30 秒 SQLite 用例，本轮没有出现。
     - 没有「宿主停止时排空等满停机期限」的增量：停机取消为 0。
   - 各项目「总时间」（TASK-09 → 本轮）：Pvf 6.5 → 8.6 s；HttpApi 8.4 → 11.2 s；Protocol 10.7 → 12.5 s；Persistence 22.7 → 28.0 s；Infrastructure 44.5 → 51.5 s；Networking 46.4 → 58.2 s（含新增 18 条）；Application 2.31 → 2.56 min；Host 2.46 → 2.72 min。
   - 逐条耗时按项目求和（共同用例）：Application 1123.3 → 1228.0 s；Host 857.4 → 937.4 s；Networking 90.9 → 103.7 s；Protocol 47.6 → 47.0 s；Persistence 19.9 → 25.5 s；Infrastructure 297.7 → 361.8 s。这些项目整体变慢，而单条用例隔离复跑都不慢，判为本轮机器负载普遍偏高。
   - 第 1 轮的两次正式运行：一次在残留 20 个 `yes` 进程时运行，已作废；另一次的数字是 8057 ＝ 8026 ＋ 31，均被本轮覆盖。
3. **隔离复跑**（第 1 轮记录 `logs/task-10-isolated.txt`；第 2 轮的定向复跑见 Checks 12）：
   - Application 4453 条，4435 通过、2 失败（同上）、16 跳过；Protocol 1694 条全部通过；Host 166 条全部通过。
   - Networking 170 条，167 通过、2 跳过、1 失败：S1，666 ms，失败消息没有抓到。追加复跑：S1 单独 ×6 全过；整项目 ×4 无失败；整项目 ×8 有 3 次 `Connection refused`；整项目 ×8 并保留明细，7 次全过，第 3 次 `GameExitIntegrationTests` 失败。
   - 明细中，TouchSocket 在 `AddListen` 记了 `Address already in use`：用例挑的端口在宿主绑定之前被别人占了。判为并行宿主的端口争用，不是生命周期逻辑。S1 的那一次不能确定归因，但它在其余每一轮都通过。
4. **线级与停机 ×10**（第 1 轮记录 `logs/task-10-wire-x10.txt`；第 2 轮在关连接器与排空改动之后重做，见 Checks 12）：W5～W9（含 W7 登录变体）、K、S1、S2 共 9 条，隔离 10 轮，通过 90、失败 0、跳过 0；全量负载下 1 次 9 条全部通过。S2 在本机有 Script.pvf，没有跳过。
5. **Host 真实进程停机**（`logs/task-10-host-stop.txt`）：S3 隔离 ×3 全部通过，各约 1 秒；Windows 上由 `NonWindowsFact` 跳过。
6. **表征测试**：8 通过、2 跳过，断言未改（`Characterization/` 只增加 `ShutdownPositionPersistenceTests` 对 `CharacterizationHost` 的只读复用，目录内文件未改）。
7. **变异核对**（`logs/task-10-mutations.txt`；每项都用 scratchpad 备份逐字节还原，`filecmp` 相等，之后重新构建）：
   - 关连接器改回 HEAD 版（不抑制流）：C1 红；
   - 生命周期回调改回直调观察者：W5 红，S1 红；扩大范围后 W5～W9、K、S1 共 8 条红，只有 S2 通过（它证明内容，不证明位置）；
   - 跳过传输排空：S1、S2 **不红**，E1、E3 红。原因：第 0 步，TouchSocket 停服时已关好会话并等完回调，排空在宿主路径上通常无事可做；
   - 在 `StopAsync` 里停止执行器：S1、S2 **不红**，E2 红。原因：生命周期服务由 `ApplicationModule` 内的 `AddGameplayExecution` 注册，早于 TCP 服务，`StopAsync` 按逆序执行，仍排在 TCP 服务之后。这个次序只靠注册顺序成立，`StoppedAsync` 让它与注册顺序无关；
   - 顶替关闭改回执行器外：M1 红；
   - 补充：排空退回「只等不兜底」时 L4 红；去掉生命周期服务注册时 E4 红。
   - **第 2 轮**：
     - 第 2 轮关连接器换回 HEAD 版（没有 `SuppressFlow`）：C1 仍然红。两者之间只差上下文抑制这一点，C1 的区分力保持不变；
     - 去掉排空的逐连接 try/catch：L5 红；
     - 删掉 `NetworkingModule` 的排空注册：注册断言 2 例都红，S1 仍然通过（与第 1 轮的记载一致）；
     - 三项都按逐字节还原、`filecmp` 相等，之后重新构建。
     - 第 1 轮其余各项变异针对的是分发器、生命周期服务与维护服务，本轮没有改动这些文件。只有「只等不兜底」那项是在加 try/catch 之前做的，它要证明的形状仍由 L4 守着。
8. **测试改动核对**：新增 34 条见 Changed，加法 15 ＋ 18 ＋ 1 ＝ 34。既有 2 个文件的类别与差异核对见 Changed。没有删除或改写任何冲突用例；顺延的 7 条在正式全量中「已通过」：`:236`、`:261`、`:288`、`:365`、`:30`、`:67`（2 例）、`:519`。后继用例见 `task-07-conflicts.md` §8。
9. **生产改动边界**：
   - `git diff --stat -- Server/src` 与新增文件只有 Changed 表中的 12 个；
   - `grep IGameplayQueue`：`Abstractions/Execution/GameplayQueue.cs`、`Application/Execution/GameplayQueue.cs`（3 处）、`NetworkMessageDispatcher.cs`、`NetworkConnectionLifecycleDispatcher.cs`、`LoginSessionRuntime.cs`；Networking 中 0 处；
   - `grep RunDetached`：`GameplayJob.cs` 4 个定义、`GameplayJobSlot.cs` 1 处 `cref`；使用只有 `NetworkPlugins.cs` 的 `OnTcpConnected`、`OnTcpClosed` 与两个 `OnTcpReceived`；
   - `grep SuppressFlow`：`GameplayExecutor`、`ExecutorSendDeadline`、关连接器（第 2 轮起是在它下面调用以 `Task.Yield` 起头的方法，不再 `Task.Run`），另有一处既有的 `Networking/Outbound/OutboundSender.cs:89`（TASK-05 的发送端，未接线，不是本单元的改动）；
   - 不变量（拆分原则第 3 条）：
     - 等待生命周期分发器作业的执行器外代码有 TouchSocket 的连接与关闭回调、TCP 服务停止（它在关会话时等关闭回调）、传输排空、维护服务的两条循环。它们都不持任何门；关闭回调持有的在途计数不是门，也不被任何作业等待。
     - `CloseAsync(`／`RequestCloseAsync(` 的调用点有 BossTower／IllusionTower 的协调与投递、进本投递、`SelectCharacterHandler`（在 lease 释放之后）、`CreatureRuntimeDelivery` 两处、`GameLoginHandler`、`LoginSessionRuntime`（在作业里）、期限关闭、关连接器、`DrainingSession` 的 `Transport.CloseAsync`、未接线的 `OutboundSender`。它们要么是分离关闭，要么只关传输，都不等待关闭回调或生命周期作业，所以没有「持门等待生命周期作业」。
10. **探针复跑**（`logs/task-10-probe-summary.txt`；原始记录取第 1 轮最终代码上的 p2 轮。第 2 轮只改了两处：关连接器改为在 `SuppressFlow` 下调用 `Task.Yield` 方法，关闭仍然不带调用方上下文；排空的逐连接 try/catch 只在停机兜底时生效。两处都不改变探针挂点的执行位置，所以本轮没有重跑探针，下列数字沿用 p2）：
    - **本族归零**：
      - 入口帧 `OnTcpConnected`／`OnTcpClosed`／`LoginSessionMaintenance`／`LoginSessionConnectionObserver` 的记录在四个项目中均为 0；
      - §3.11 第 2 行 P1b-teardown 为 0（TASK-09：Networking 2／13）；
      - §5 中 `TownAreaProjectionDelivery.*`、`RecordDepartureAsync`、`DungeonRunDomainService.EndAsync`、`TransactionalExecutionGate.AcquireAsync` 都不在 (C) 栈顶之列。
    - **残留**：§3.3 观察者 79／114，全部在 Application，外层测试帧全是 `UserServiceTests` 与 `SelectCharacterLifecycleTests` 及其辅助。排除这两个文件复跑（x2）后为 0。§3.1 dispatcher/Received 1，是 TASK-09 的遥测只读检查；§3.2 1，是 `:371`。
    - **Closed 族新基线**（记录／命中）：TASK-07 202／1191 → TASK-09 164／1210 → 本单元 0／0。**更正 TASK-09 Result**：它说 Connected/Closed 的变化「符合 §9 第 4 条」，而命中是 1191 → 1210，上升而不是下降。本单元修好关连接器并迁入回调之后，这一族归零，作为 TASK-11～14 的基线。
    - **其余各族**（TASK-09 → 本单元 p2，记录／命中）：
      - 3.5 事件 476／1903 → 450／1912；
      - 3.6 卡牌计时器 24／141 → 24／141；
      - 3.7 GM/HTTP 1088／2218 → 923／1968；
      - 3.8 后台与调度 209／4331 → 223／5359；
      - 3.9 启动与停机 158／329 → 373／654；
      - 3.10 命令总线 3498／10699 → 3534／10688。
      - 分组脚本按 `alist.py` 的规则重建，TASK-09 用的脚本不在仓库里，所以本族以外的增减不逐条归因；3.8 的命中是周期性扫描的 tick 数，随宿主寿命与负载变化。
    - **§3.9 单列**：协调器 `Dispose` 的 (A) 记录仍在，55／216（Host 19／158、Networking 32／54、Protocol 4／4），全部在宿主停止之后的容器释放里。它们重新成为 (A)：执行器已在 `StoppedAsync` 停止，`DisposeAsync` 同步完成，后面单例的释放落在宿主自己的栈上。按裁定第 5 条交 TASK-11。`Host.ForeachService` 下没有 `OnTcpClosed` 记录。
    - **还原证明**：
      - p1、p2、x1、x2 四轮的快照前后 sha1 都是 `ca108b50021cb62c76fd18b262bb1c3f24e82590`，`cmp` 相等；
      - 快照只按名字列未跟踪文件，所以另外在每轮前后对 4 个新文件的内容取 sha1 并 `cmp`，四轮都相等。p2、x2 两轮的值是 `5f7cc26d7007aa56497eeb81495bcc4dc849225c`；p1、x1 在排空修正之前，当时的值已被覆盖，只留下了 `UNTRACKED-BYTE-EQUAL` 的输出；
      - `snapcheck`：1577／1577，相等 1569，不等的 8 个就是本单元的 8 个修改，未跟踪 4 个就是本单元的新文件；
      - `patch.py revert` 恢复 25 个文件并删除 `GameplayProbe.cs`；`grep TestGameplay|GameplayProbe Server/src` 0；
      - 之后以 `--no-incremental` 重新构建，再跑正式全量。
    - 探针轮的失败不作为证据：p2、x2 只有那 2 条 `DimensionDungeonTests`。
11. **对账行**：
    - 附录 A 第 3 行（连接建立、断开、孤儿清理、断连拆除落位置）：来源 Lifecycle → `lifecycle:connected`／`lifecycle:disconnected` → W5、W6、W7、W8、S1、LD1、L1；探针 Connected/Closed 0／0、观察者族排除后 0、P1b-teardown 0。`lifecycle:close-cleanup`（由 CMD 发起的关连接之后的清理）与调用链继承留给 T-007；关连接排在已登记消息之后留给 T-006。
    - 附录 A 第 4 行（登录会话的连接观察、顶替关闭、过期扫描）：来源 Lifecycle → `lifecycle:disconnected`（`LoginSessionConnectionObserver`）、`lifecycle:close-superseded`；来源 Timer → `timer:LoginSessionMaintenanceService` → M1、M2、W9、W7 登录变体；探针登录会话族 1／1 → 0／0。改为登录 CMD 发件箱中的关连接条目留给 T-006，继承链留给 T-007。
    - 分离点清单（3e）本单元之后的现状：
      - `ExecutorSendDeadline.StartDetachedClose`：TASK-08，`SuppressFlow`；
      - 关连接器：本单元，`SuppressFlow`；
      - 卡牌计时器：TASK-11，仍在执行器外；
      - TASK-05 的准入循环与排空循环：未接线，T-006 复核；
      - `RunDetached` 位置：插件的 Connected、Closed、两个 Received。

12. **第 2 轮定向复跑与 W7 同负载对照**：
    - 定向测试隔离 ×10（`logs/task-10-r2-directed-x10.txt`），覆盖生命周期、关连接器、排空与停机：
      - Networking 每轮 22 条：C 2、L 5、注册 2、`ExactConnectionCloseTests` 2、`ReceivedStreamBoundaryTests` 2、W 7、S1、S2；
      - Application 每轮 15 条：LD 6、E 4、M 3、P 2；
      - Host 每轮 1 条：S3。
      - 10 轮合计通过 (22 ＋ 15 ＋ 1) × 10 ＝ 380，失败 0，跳过 0；K 50 次都是 `K-INLINE=False`；W7 10 次都是 `frames before FIN: 1`。
    - W7 同负载对照（`logs/task-10-w7-race.txt` 末尾）：两边都在 10 个限时 `yes` 负载进程下运行（脚本 `trap` 结束，事后 `pgrep -x yes` ＝ 0）。
      - 本轮关连接器 20 次：通过 20，越过 0；
      - 基线关连接器 20 次：通过 20，越过 0。
      - 基线那一组是把关连接器换成 HEAD 版逐字节副本跑的，跑完还原并 `cmp` 相等，再重新构建。

### Remaining

1. **生命周期 CMD 一律新根**：关闭回调分不清关闭是谁发起的，所以由 CMD 登记的关连接引起的 `lifecycle:disconnected` 本应继承那个 CMD 的链，现在却是新根；`lifecycle:close-superseded` 是登录 CMD 提交时登记的关闭。改为继承链、引入 `lifecycle:close-cleanup` 归 T-007；改为发件箱条目归 T-006（裁定第 4 条）。
2. **RQ-008 过渡态**（裁定第 6 条）：关闭回调等待的生命周期作业里含直发（清理通告），受 2 秒期限约束。VC-8 的提前范围因此从入站扩大到生命周期：不读包的同伴会在 2 秒后被断开。T-006 之后消失。
3. **RQ-012 第二句未满足**：关闭会越过还在发送管道里的帧，这是基线缺陷。W7 只断言前缀形状，T-006 让帧确定先于关闭之后，W7 改回严格形式。
   - **第 1 轮的数字已更正**（B-1）。当时第 1 轮关连接器（`Task.Run`）共 45 次、越过 3 次：10 个负载进程 3/15，20 个负载进程 0/30。基线关连接器（`Task.Yield`）共 45 次、越过 0 次，全部在 20 个负载进程下。旧关连接器从没在 10 负载下跑过，所以**没有同负载的对照**，不能据此判断哪个更容易越过。
   - **第 2 轮**：关连接器已恢复基线调度（N-1）。同负载对照见 Checks 12：都在 10 个限时负载进程下各 20 次，本轮关连接器 0/20 越过，基线关连接器 0/20 越过。
4. **停机时补做的拆除**（B-2）：服务停止时恰好断开的连接，在本单元的代码上可能收不到关闭回调。排空现在兜底拆除这些连接，第 1 轮正式全量中有 6 次。
   - 定性：这是 RQ-009（断开引起的生命周期 CMD 在执行器停止前全部执行完）与 AC-007（位置不丢失）的落实，属于服务端状态差异（停机时补做拆除落库）。被兜底的连接本身已经断开，所以线上字节不变，不是附录 G 意义上的可见变化。
   - 未知一：基线上是否同样会丢这些关闭回调，没有实测。
   - 未知二：本单元让建立回调等待执行器作业、持续时间变长，这是否扩大了丢回调的窗口，也没有测。
   - 两种情况下，兜底后拆除都恰好执行一次。T-006 改关连接语义时要保留「排空兜底、`TryRemove` 仲裁」；停服之外是否也会丢回调，本单元没有测。
5. **卡牌协调器 `Dispose`**：按裁定第 5 条交 TASK-11；探针 55／216 (A)。
6. **双 Error 日志**：生命周期作业失败时，执行器与插件各记一条（与入站相同），交 T-008。
7. **R-5 现状**：TouchSocket 4.3.1 不内联（K 共 55 次）。升级 TouchSocket 时连同 TASK-09 主控方补充 N-4 一起复核。
8. **线级用例的端口争用**：用例「先绑 0 取端口再释放」的写法在并行宿主多时会撞端口（TouchSocket 记 `Address already in use` 后不带那个监听器启动）。本单元增加了 15 条用到真实 socket 的用例，这类失败在整项目复跑中可见。测试基础设施不在本单元边界内，未改。
9. **S2 依赖 `CharacterizationHost` 的私有字段**（反射）。将来它若提供停机接口，应改用接口。CI 无 PVF 时 S2 跳过，「位置不丢失」只有本机证据（裁定第 7 条）。S3 在 Windows 上跳过。
10. **取证上的污染与处置**：早期一次手写负载循环泄漏了 10 个 `yes` 进程，之后又叠加到 20 个，一直运行到被发现并结束。受影响的有第一次正式全量（已作废并覆盖）、探针 p1（最终数字取 p2）、W7 越过统计（各组负载不等，已在日志写明）。之后的第二次正式全量、隔离复跑、×10、×3 都在没有残留负载进程时运行。第 2 轮的同负载对照用的是限时负载，在第 2 轮全量开始之前就已结束。
11. **取证缺口：S1 那次隔离失败**（N-4）。第 1 轮 Networking 隔离复跑中 S1 失败一次（666 ms），失败消息没有抓到，身份不明。这不归入第 8 条的端口争用：可能是端口争用，但没有证据。此后 S1 在隔离 ×10、单独 ×6、每一轮整项目复跑、审查者的整项目 ×5 以及第 2 轮各项运行中都通过，这能降低风险，但补不上那一次的身份。
12. **未提交**：没有 commit，也没有 stash。
