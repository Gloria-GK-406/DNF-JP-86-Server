---
kind: rks-review
target: tasks/TASK-10.md
scope: Task Unit result quick review（TASK-10 / T-004b3，Contract Revision 1 ＋「主控方裁定（执行前）」9 条，执行轮次：Result 第 1 版，未提交）
verdict: approved
date: 2026-10-22
rounds: "Round 1 changes_required → Round 2 approved（见文末）"
---

# TASK-10 结果快速审查

## 审查对象与依据

- **对象**：`tasks/TASK-10.md`（契约 Revision 1、Detailed Plan、主控方裁定、Result）；工作区相对 `HEAD`（`1eea890f`）的未提交改动：
  - `git diff HEAD -- Server/src`：8 个文件 ＋143 −28；新增 4 个文件（`GameplayProducerDrain.cs`、`NetworkConnectionLifecycleDispatcher.cs`、`GameplayExecutorLifetime.cs`、`TouchSocketTransportDrain.cs`）；
  - 测试：修改 2 个（`EndToEndHostHarness.cs`、`ReceivedStreamBoundaryTests.cs`），新增 11 个；
  - 文档：`verification/task-07-conflicts.md` §8，`verification/logs/task-10-*.txt`（14 个）。
  - 目标身份：`git diff HEAD | shasum` ＝ `9b080a77…`（审查前后相同）；按 `probe-tools/snapshot.py` 重算的 `Server/src` 快照 sha1 ＝ `ca108b50021cb62c76fd18b262bb1c3f24e82590`，4 个新文件的 `shasum` 清单再取 sha1 ＝ `5f7cc26d7007aa56497eeb81495bcc4dc849225c`，两者都与 Result Checks 10 记录的 p2／x2 轮相同。
- **依据**：`plan.md` 共享决定「停机与等待关系」、过渡期规则 3、spec-review-r3 R-5 的澄清；`spec.md` RQ-008、RQ-009、RQ-012、RQ-082、AC-007、附录 A 第 3／4 行、附录 G；`tasks/TASK-08.md`「T-004b 拆分」跨单元规则；`tasks/TASK-09.md` Result 与主控方补充；`AGENTS.md`。
- **采用的准则**：reviewing-work 的 Task Unit 准则，加 Code 准则与 Deliverable 准则（限本单元的局部缺陷）。
- **审查者自己跑的东西**：`Dfo.Server.Networking.Tests` 整项目 ×5（Release，`--no-build`，构建确认是最新的，明细落在 scratchpad）：5 次都是 170 条，168 通过、2 跳过、0 失败；`drain tears it down` 0 次，W7 `frames before FIN: 1` 5/5，`K-INLINE=False` 25/25。没有改仓库文件，没有用 stash；`git status --short` 前后逐字节相等（`cmp`），diff sha1 前后相同。机器上没有残留的 `yes` 进程。

## 结论

**changes_required。** 生产代码在本单元范围内没有发现阻断性缺陷：生命周期分发器是逐字搬迁，停机次序成立，排空兜底在结构上恰好一次，K 的「不内联」证据充分。阻断的是 Result 里两处与证据或规格相矛盾的记载（B-1、B-2）。它们都只需改 Result 的文字，不需要改代码、不需要重跑；但主控方对第 4 项「关连接器」的判断已经引用了错误的数字，所以要先改。

## 阻断项

### B-1　Remaining 第 3 条的 W7 计数与日志不符，而且把一次负载不同的比较写成了同负载对照

- **Result 写的**（Remaining 第 3 条）：「新关连接器 90 次中有 3 次越过，旧的 45 次中 0 次，两组负载不同」。
- **日志写的**（`logs/task-10-w7-race.txt`）：

  | 关连接器 | 负载 | 次数 | 越过 |
  |---|---|---:|---:|
  | 新（SuppressFlow） | 10 个 `yes` | 15 | 3 |
  | 新（SuppressFlow） | 20 个 `yes` | 30 | 0 |
  | 旧（`Task.Yield`） | 20 个 `yes` | 15 | 0 |
  | 旧（`Task.Yield`） | 20 个 `yes` | 30 | 0 |

  新关连接器共 15 ＋ 30 ＝ **45** 次、越过 3 次；旧的 15 ＋ 30 ＝ 45 次、越过 0 次；两者合计 90 次。日志里那句「3 failures in 90 runs」指的是合计，Result 把它写成了「新的 90 次」。
- **后果**：分母错了一倍，同时掩盖了真正的混杂：**3 次越过全部出在唯一一组 10 负载的批次里（3/15），而旧关连接器从来没有在 10 负载下跑过。** 主控方给本审查的提示已经照抄了「3/90 vs 0/45」，说明这个数字已经在影响判断。
- **最小修正**：把 Remaining 第 3 条改成上表的口径（新 3/45，其中 10 负载 3/15、20 负载 0/30；旧 0/45，全部在 20 负载），并写明「没有同负载的对照」。处置意见见非阻断项 N-1。

### B-2　Remaining 第 4 条自称「可见的行为变化」，与 RQ-082 冲突，「基准上根本不会执行」也没有测过

- **Result 写的**（Remaining 第 4 条）：「这是可见的行为变化：基准上这些连接的拆除（包括落位置）根本不会执行。」RQ-082 规定可见行为变化只允许附录 G 所列各项，而附录 G 里没有这一项。照字面，这一条就是本单元自己登记了一处越界。
- **审查对主控方初步判断的检验**：判断成立。排空兜底只在宿主停止、TCP 服务已经关掉全部会话之后才动作；被兜底的连接本身已经断开，清理通告要发给的同伴也正在或已经关闭（日志中 6 次兜底，第一次是 Channel 连接，观察者对它什么也不做）。所以线上字节没有变化，变化只在服务端状态上，也就是拆除落库。这正是 RQ-009（「断开引起的生命周期 CMD……全部执行完」）和 AC-007（「位置不丢失」）要求的结果，属于落实需求，不是附录 G 意义上的可见变化。
- **另一半没有证据**：「基准上这些连接的拆除根本不会执行」是推出来的，不是测出来的。日志只能证明在**本单元的代码上**，TouchSocket 停服时会有连接收不到关闭回调；基准上会不会同样丢，没有测。另有一种可能没有排除：本单元让 `OnTcpConnected` 等待执行器作业，回调持续的时间变长，丢回调的窗口也可能随之变大。两种情况下兜底的结果都不差于基准，所以这不影响代码的正确性，只影响记载。
- **最小修正**：把这一条改写成「服务端状态差异（停机时补做拆除落库），线上不可见，属 RQ-009／AC-007 的落实，不是附录 G 条目」；把「基准上不会执行」改成「基准上是否也丢回调没有测；本单元在兜底后拆除恰好一次」。如果主控方不认同这个定性，就要按 RQ-082 补进附录 G 并配证据，两条路只能选一条，不能维持现在的写法。

## 非阻断项

### N-1　关连接器从 `Task.Yield` 改为 `Task.Run`，调度并不等价；建议本单元内消除这个问题（第 4 项）

- Plan §3 说两者「唯一的区别是关闭任务不再带着调用方的 ExecutionContext」，这个说法不准确。执行器没有自定义调度器（`GameplayExecutor.ConsumeAsync` 用 `Task.Run` 启动，全程 `ConfigureAwait(false)`）。在线程池线程上，`await Task.Yield()` 的续体进**全局队列**；`Task.Run` 经 `ThreadPoolTaskScheduler` 以 `preferLocal: true` 进**当前工作线程的本地队列**。handler 随后 `await Task.Delay` 让出线程，这个线程会先取本地队列，所以关闭会更早开始。在负载下，全局队列里排着别的工作，旧写法的关闭会被推迟，帧也就更有机会先刷出去。这个机制和 B-1 表里的数据方向一致，但数据不能证明它。
- **为什么不阻断**：这是基线就有的缺陷（关闭越过发送管道里的帧，两条跳过的表征测试已有记载），RQ-012 第二句和 AC-007 的严格形式都归 T-006，Result 也已经把它登记为风险。
- **建议**（任选其一，第一种代价最小，而且能直接让这个问题消失）：
  1. 保留 `await Task.Yield()` 起头的异步方法，只在 `ExecutionContext.SuppressFlow()` 下**调用**它。方法同步执行到 `Yield` 就返回，`Undo` 仍在同一线程上；续体捕获到的是空的 ExecutionContext。这样调度与基准逐字节相同，只去掉了上下文，C1 照样能证明。
  2. 保留 `Task.Run`，在同一负载档位（例如 10 个限时负载）下新旧各跑同样次数，写出对照。
- 被拒登录（`GameLoginHandler.RejectAsync` 先发拒绝再关闭）属于「先发帧再自关」的形状，是这一项在线上最可能被看到的地方。

### N-2　排空兜底没有按连接隔离失败

`TouchSocketTransportDrain.DrainAsync` 逐个连接 `await lifecycle.DispatchDisconnectedAsync(...)`，中间没有捕获。只要某一个连接的拆除抛出（观察者失败，例如 SQLite 错误），快照里后面的连接都不会被拆除，`WhenIdleAsync` 也会被跳过；`GameplayExecutorLifetime` 记一条 Warning 后照常停止执行器，那些连接的位置就丢了。正常路径上每个关闭回调各自独立，一个失败不会牵连其他连接。触发条件是「停机时丢回调」和「拆除失败」同时发生，概率低，修正却很简单：每个连接一个 `try/catch`，记 Warning 后继续，最后照常 `WhenIdleAsync`。建议本单元顺手做掉，并让 L4 加一个「第一个拆除抛出，第二个仍被拆除」的变体。另外，兜底拆除用的令牌是宿主停机令牌，而正常回调用的是 `CancellationToken.None`；只有停机期限到期时才有区别（作业还在排队就会被取消），在 Result 里写一句即可。

### N-3　`NetworkingModule` 注册排空这一步没有判据守着

变异核对已经说明 S1、S2 对「跳过排空」不红，因为 TouchSocket 自己就会关会话并等回调。E1／E3 用的是替身，L1～L4 直接 `new` 排空实现。所以如果把 `NetworkingModule` 里的 `TryAddEnumerable<IGameplayProducerDrain, TouchSocketTransportDrain>` 删掉，没有任何用例会红；而首轮全量里那 20 次停机取消，正好说明兜底在真实宿主上是需要的。建议加一条注册断言：从 `NetworkingModule` 组装的容器里解析 `IEnumerable<IGameplayProducerDrain>`，断言其中有 `TouchSocketTransportDrain`；或者让 S1 断言排空确实被调用过。

### N-4　隔离复跑中 S1 那次失败是取证缺口，应进 Remaining

Checks 3 承认 S1 那次 666 ms 的失败「不能确定归因」，失败消息也没有抓到。按 AGENTS.md「取不到身份的失败一律是取证缺口」，这一条应当单独写进 Remaining，不要只混在第 8 条的端口争用里。审查者复跑整项目 ×5 都是干净的，与 Result 的追加复跑一致，这能降低风险，但补不上那一次的身份。

### N-5　P2 承接 `:519` 的说法偏宽（第 7 项）

`:519` 的原情形是**同一条连接**上「断开先观察到旧代数、认证随后加一」，保护的是「过期的观察不删新代数」。在串行执行下，同一连接的断开 CMD 只能排在它最后一条入站之后，这一点由 W6 证明，那种过期观察根本不会出现。P2 测的是另一件事：旧连接断开不影响同账号在**另一条连接**上的新身份，它本身成立，但不是 `:519` 那个情形的对应物。另外，P2 里的 `Assert.False(online.TryGetByConnection(old…))` 在第二次认证时可能就已经成立（同账号顶替），不一定是断开的效果。建议在 `task-07-conflicts.md` §8 把 `:519` 的后继写成「W6（同连接次序）＋ P2（跨连接不误删）」，Result「Why」里那句改成同样的口径。P1 承接 `:30`／`:365` 的说法成立。

## 逐项检验（对应委托的 8 个重点）

1. **生命周期分发器**：`NetworkConnectionLifecycleDispatcher` 里的 `foreach` 与基准 `NetworkPluginBase` 的循环逐字相同；观察者从同一个 `IEnumerable<INetworkConnectionObserver>` 解析，顺序与注册顺序一致；回调传 `CancellationToken.None`，作业体拿到的就是它（`Job.RunAsync` 把 `callerToken` 传给 `work.Body`）。`Add`／`TryRemove` 仍在回调里、入队之前。两个回调整体在 `RunDetachedAsync` 里，`RunDetached` 只清 `Running`。执行器已停止时记 Warning 并重新抛出（LD4）。CMD 类型与 `NewRoot(Lifecycle, ConnectionId)` 和裁定第 4 条一致。**成立。**
2. **兜底「恰好一次」**：仲裁点是 `ConcurrentDictionary.TryRemove`，排空和迟到的回调只有一方能拿到条目；回调在 `BeginClosing` 之后、`TryRemove` 之前被排空抢先时，回调拿不到条目，只做 `EndClosing`。`WhenIdleAsync` 在锁内检查并登记等待者，`SignalIfIdle` 在移除或计数归零之后进锁检查，不会漏掉唤醒。兜底与正常回调走的是同一个分发器（测试宿主里还套着同一个装饰器）、同一个 CMD 类型，都在执行器上执行。差异只有令牌（见 N-2）和不调用 `e.InvokeNext()`，后者是 TouchSocket 插件链自己的事。L3 覆盖并发情形（关闭触发回调，二者竞争），L4 只覆盖「排空先、回调后」的顺序情形，强度够用；隔离失败见 N-2。**成立，有 N-2。**
3. **停机次序**：`StoppedAsync` 在全部托管服务的 `StopAsync` 之后执行，这一点与注册顺序无关；Kestrel 和两个 TCP 服务都在 `StopAsync` 阶段停止接受，所以 RQ-009 的「先停止接受」成立。今天 HTTP 侧没有任何东西入队到执行器（`IGameplayQueue` 的使用方只有 dispatcher、生命周期分发器与登录维护），所以不存在「执行器停止后 HTTP 入队失败」；TASK-13 接入以后，Kestrel `StopAsync` 会先等在途请求结束，次序照样成立，只有停机期限到期时例外。S1、S2 不能区分「跳过排空」或「改在 `StopAsync` 停止执行器」，这两种情形由 E1～E3 与变异核对兜住，加上 E4 的真实 `IHost`、S3 的真实进程，次序的证据是充分的；缺口只在注册这一步（N-3）。另有一项基线就有的残余风险：TouchSocket 停服时逐个等关闭回调，连接多、拆除慢时会耗尽 30 秒停机期限，与本单元无关。
4. **关连接器**：见 N-1。审查意见是本单元内处置，首选「SuppressFlow 下调用 `Task.Yield` 起头的方法」，不必另做对照。
5. **K**：「不内联」的证据有三层，强度足够：55/55 看不到标记；55 次中有 24 次关闭回调**在作业仍在运行时**执行（`during the job=True`），却仍看不到标记，这说明它不是从作业的上下文派生出来的；发送在对端重置后 0～1 ms 内以传输异常结束，没有一次靠 2 秒期限。标记改到分发器装饰器里读，理由成立：观察者在执行器的消费循环上运行，那里不带生产者的 ExecutionContext，在那里读永远读不到，测不出任何东西；装饰器在 `RunDetachedAsync` 之内、入队之前读，能看到调用方的其他 AsyncLocal。审查者复跑 25 次，结果相同。局限：只测了 RST 这一种失败形状，只在 macOS 上测，Result 已写明升级时复核。
6. **登录会话维护**：每个顶替关闭请求一个 `lifecycle:close-superseded` 作业，作业内先 `TryDisconnectConnection` 再 `RequestCloseAsync`，与基准循环体的次序相同。基准上它与执行器上的入站作业并发，现在严格排在当时已入队的作业之后（包括发起顶替的登录作业），这只是把今天本来就不确定的交错收敛成其中一种，语义集合不变。过期扫描经 `CoalescingGameplaySource`，循环逐次等待。停机时令牌在作业排队期间被取消，作业就不执行，而那些连接随后由传输关闭并经正常拆除清理，与 Plan §6.4 一致。异常面变宽（`TryDisconnectConnection` 的异常现在被 Warning 捕获）已在 Result 中写明。**成立。**
7. **P2**：见 N-5。
8. **测试修改与数字**：两个既有文件的差异与 `task-10-diff-check.txt` 一致（`ReceivedStreamBoundaryTests` 只改两处实参，方法 2 ＝ 2，断言行 10 ＝ 10；`EndToEndHostHarness` 只加一个方法，基准的 48 条断言行全部保留）。全量加法已复算：总数 8057，通过 8035，失败 2，跳过 20，与日志相符；`did not run`、`did not drain within`、`direct send deadline exceeded`、`GameplaySelfWait`、生命周期／定时失败都是 0；`drain tears it down` 6；`inbound` 失败 2 ＋ 1。错的只有 W7 那一处（B-1）。**取证污染**：源文件的修改时间与日志时间戳吻合——09:52:30 探针还原（25 个文件），09:53:08 正式全量开始，10:01:44 基准对照脚本把本单元文件放回，10:06:59 隔离复跑，10:22:22 最后一项变异还原后重新构建。当前源码的快照 sha1 与正式全量之前 p2／x2 轮的记录相同，所以正式全量、隔离、×10 的证据适用于当前代码。正式全量各项目耗时与 TASK-09 接近（Application 2.31 → 2.42 min），不像是有 20 个负载进程在跑。认可 Result 的处置；受污染的只剩 W7 那组统计，它本来就是有意加负载的探索，B-1 改正之后就是准确的记载。

## 交给主控方

- 改 B-1、B-2 两处 Result 文字之后可以按原证据通过，不需要重跑。
- N-1、N-2 建议在本单元内处置。两处都只改本单元自己的文件，改完要重跑 C1、L3／L4、W7 与 Networking.Tests 隔离一次。如果主控方选择顺延，就把它们登记进 Remaining，并写明归属的单元（N-1 归 T-006，N-2 归停机兜底的所有者）。
- N-3、N-4、N-5 是记载与判据的补强，不影响本单元能否通过。

## Round 2

```yaml
kind: rks-review
verdict: approved
```

- **对象**：`tasks/TASK-10.md` Result 第 2 轮（契约 Revision 1 不变）；工作区相对 `HEAD`（`1eea890f`）：`git diff HEAD | shasum` ＝ `f3a45b89…`（本轮审查前后相同）。`Server/src` 8 个文件 ＋143 −25，另有 4 个新文件。本轮改动的生产文件只有 `TouchSocketNetworkConnectionCloser.cs`、`TouchSocketTransportDrain.cs`（`NetworkingModule.cs` 内容未变）。测试新增 L5 与 `TransportDrainRegistrationTests`。新日志 `task-10-r2-directed-x10.txt`；`build-test` 被覆盖；`mutations`、`w7-race` 有追加。按 `snapshot.py` 重算的本轮 `Server/src` 快照 sha1 ＝ `462644bf…`。
- **审查者复跑**：`Dfo.Server.Networking.Tests` 整项目 ×5（Release，`--no-build`，构建是最新的）：每次 173 条，171 通过、2 跳过、0 失败；W7 `frames before FIN: 1` 5/5，`K-INLINE=False` 25/25；`teardown of connection … failed` 与 `Requested network connection close failed` 都是 0。审查者没有改仓库文件（只追加了本审查），没有 stash；机器上没有 `yes` 进程。

### 第 1 轮各项的处置

- **B-1　已解决。** Remaining 第 3 条现在与 `task-10-w7-race.txt` 一致：第 1 轮关连接器 3/45（10 负载 3/15，20 负载 0/30），基线 0/45（全部 20 负载），并写明「没有同负载对照」。补上的同负载对照（10 个限时负载，`trap` 结束，事后 `pgrep -x yes` ＝ 0）是本轮关连接器 0/20、基线 0/20，日志末尾有记录。
- **B-2　已解决。** Remaining 第 4 条定性为 RQ-009／AC-007 的落实、服务端状态差异、线上不可见，并列出两项未知：基线上是否同样丢回调，窗口是否被本单元扩大。Outcome 第 449 行改为「基准上是否同样会丢这些回调没有实测」，前后一致。
- **N-1　已解决，基线调度确实恢复了。**
  - 现在的写法是在 `using (ExecutionContext.SuppressFlow())` 里调用一个以 `await Task.Yield()` 起头的异步方法。方法同步执行到 `Yield` 就返回，`AsyncMethodBuilder.Start` 把线程的上下文恢复成那个被抑制的上下文，所以 `Undo` 在同一线程、同一被抑制状态上执行，合法（已处于抑制状态时直接调用，不嵌套）。
  - 在第一个 `await` 处，状态机盒子用 `ExecutionContext.Capture()` 取上下文；流被抑制时它返回 null，所以续体在线程池的默认上下文上运行，不带作业令牌和调用方的帧。
  - 续体的排队路径与基线相同：没有 SynchronizationContext、默认调度器时，`YieldAwaitable` 以 `preferLocal: false` 进全局队列；基线走的也是这条 unsafe 路径，区别只在盒子带不带上下文。第 1 轮 `Task.Run` 走本地队列的那个差异已经消除。
  - **C1 仍有区分力**：第 2 轮的变异把关连接器换成 HEAD 版（同样 `Yield`，但不抑制流），C1 红（`mutations` 末尾）。两者只差上下文抑制，C1 守的正是这一点。
- **N-2　已解决，停机令牌取消时的行为正确。**
  - 每个连接的兜底拆除各包一层 `try/catch`，失败记 Error 后继续，最后照常 `WhenIdleAsync`。
  - 停机令牌取消时，`catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` 把异常原样抛出。这样做是对的：令牌已取消时，此后每次 `DispatchDisconnectedAsync` 都会在入队前就以取消返回（`GameplayExecutor.RunAsync` 先检查令牌），继续循环只会多出 N 条无用的 Error。抛出之后，`GameplayExecutorLifetime` 记 Warning，`executor.StopAsync` 用同一个已取消的令牌，取消随即传回宿主，与停机期限到期的既定语义（Plan §6.2 第 4 步）一致。
  - 令牌未取消时，观察者自己抛出的取消异常走通用分支，记 Error 后继续，也是对的。
  - L5：`ConcurrentDictionary` 的枚举顺序随进程的字符串哈希而变，但去掉隔离后，不论失败的那个连接排第几，异常都会从 `DrainAsync` 冒出，L5 照样会红；变异也确认了它红。
  - 取消这条路径没有专门的用例。它只有一个分支，读代码就能确认，不作为缺陷。
- **N-3　已解决。** `TransportDrainRegistrationTests`（`Enabled` 为 false、true 两例）断言注册表中恰有一个 `TouchSocketTransportDrain`。变异「删注册」时两例都红，S1 仍绿，与第 1 轮判断一致。
- **N-4　已解决。** Remaining 第 11 条单列为取证缺口，不归入端口争用。
- **N-5　已解决。** `task-07-conflicts.md` §8 与 Result「Why」都改为「W6 ＋ P2」，并写明 P2 里「旧身份已不在」那条断言不单独证明是断开的效果。

### 沿用第 1 轮证据的 Checks，适用理由是否成立

- **Checks 4（线级 ×10）、5（S3 ×3）**：本轮由 Checks 12 在第 2 轮代码上重做（W 系列 7 条、S1、S2、S3 各 ×10，380 条全过），第 1 轮的数字只作历史记录。**成立。**
- **Checks 6（表征测试）**：第 2 轮全量里重新得到 8 通过、2 跳过（Checks 2）。**成立。**
- **Checks 10（探针未重跑）**：探针记录的是执行器外的取门与入口帧。本轮的两处改动：
  - 关连接器：第 1 轮和第 2 轮的关闭都在空的执行上下文里运行，关闭回调都经 `RunDetached` 入队，回调与观察者的执行位置没有变化，变的只是关闭任务的排队方式；
  - 排空的 try/catch：只在停机兜底、拆除抛出时生效。

  所以 p2 的「Closed 族 0／0」及其余各族的数字仍然适用。这是推理不是重测，但改动面小，推理链完整，审查者认可。**成立。**
- **Checks 11（对账行）**：本轮没有改变 CMD 类型、来源或对账对象。**成立。**
- **Checks 3（隔离复跑）**：整项目隔离仍是第 1 轮的记录；本轮只有 Networking 的生产代码变了，由 Checks 12 的定向 ×10 与审查者的 Networking 整项目 ×5 覆盖。可以接受。

### 数字核对

- 全量（`task-10-build-test.txt`，10:45:48 开始）：
  - 总数 50 ＋ 80 ＋ 1694 ＋ 475 ＋ 969 ＋ 173 ＋ 4453 ＋ 166 ＝ 8060 ＝ 8026 ＋ 34；
  - 通过 8038，失败 2，跳过 20，8038 ＋ 2 ＋ 20 ＝ 8060；
  - 日志信号：`drain tears it down` 1，各类失败与期限信号 0，`inbound` 失败 2 ＋ 1，K 5 次都不内联，W7 1 帧。与 Result 一致。
- N：Networking 2 ＋ 5 ＋ 2 ＋ 7 ＋ 1 ＋ 1 ＝ 18，155 ＋ 18 ＝ 173；总 N ＝ 15 ＋ 18 ＋ 1 ＝ 34。
- 定向 ×10：每轮 22 ＋ 15 ＋ 1 ＝ 38，10 轮合计 380，与日志逐行一致。
- 时间线：关连接器源文件最后修改 10:45:35，构建产物 10:45:50，全量 10:45:48 开始，定向 ×10 10:49:36 开始。同负载对照需要换关连接器并重新构建，只能在 10:45:35 之前完成，与「全量前已无 `yes` 进程」的说法相容。

### 非阻断（只改文字，不影响通过）

1. **Changed 段第 455 行仍写「8 个文件 ＋143 −28」**，本轮实际是 −25（Checks 1 与日志里的 diff stat 都是 −25）。
2. **Checks 7 第一条「关连接器改回 `Task.Yield`：C1 红」是第 1 轮的口径。** 现在本轮的关连接器本身也以 `Task.Yield` 起头，这一句读起来会有歧义。建议改成「改回 HEAD 版（不抑制流）」，与第 2 轮那一条同口径。
3. **Remaining 第 10 条「之后的第二次正式全量……」是第 1 轮的措辞。** 本轮全量是第 2 轮的运行，同负载对照有意加了负载。建议补一句：第 2 轮同负载对照的负载在全量前已结束。

**结论**：第 1 轮的两项阻断都已解决，五项非阻断都已处置。在本单元范围内没有未解决的阻断性缺陷，verdict 更新为 **approved**。上面三处文字建议主控方提交前顺手改掉。
