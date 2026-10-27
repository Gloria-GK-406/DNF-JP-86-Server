---
kind: rks-review
target: tasks/TASK-31.md
verdict: approved
---

# TASK-31 结果快速评审（Independent Quick Review，第 1 轮）

- 被审：`tasks/TASK-31.md` Contract Revision 1、「主控方裁定（执行前）」1～6、Result 第 1 轮（执行轮次 1）。
- 改动身份：HEAD `122576d5` 之上未提交的差异——`Server/src` 13 个文件（102 增、34 删）；`Server/tests` 30 个修改 ＋ 新建 `Integration/EffectReplayWireTests.cs`；`verification/t009-logs-and-cases.md`（新建）、`verification/follow-ups.md`、`verification/logs/task-31-*.txt` 12 个。
- 采用的评审准则：reviewing-work 的 Task Unit 快速评审 ＋ Code（局部范围）＋ Deliverable（收口记录与交接口径）。
- 评审者独立于执行者；只读，未改生产与测试，唯一写入的是本文件。

```yaml
kind: rks-review
verdict: approved
```

结论：没有阻断本单元提交（即代码冻结点）的缺陷。下面第 1 条是交给 TASK-32 的判定口径里的一处不准确，**不需要改代码**，但须由主控方在 TASK-32 开始前处置（它改的是主控方裁定第 2 条已批准的文字）。

## 发现（按严重度）

### 1. 中——§4 第 1 条自称「口径与 M-2 相同」，实际丢了 M-2 对 (d) 轮登录连接 Warning 行的豁免；F-126 的形状让这条缺口可以触达

- 位置：`verification/t009-logs-and-cases.md` §4 第 1、3 条（与 `tasks/TASK-31.md:229-235` 计划 §4.4 原文一致）。
- 对照：M-2 的第 5 项（`tasks/TASK-28.md:139-145`）对**收尾窗口外的 Warning 行**有一条豁免：属于 (d) 轮里工具在 LOGIN 应答后主动断开的登录连接、且发生在断开后 1 s 内的，归为「工具主动断开，不计」。新口径第 1 条只说「Warning 及以上……窗口外计入」，(d) 的豁免只出现在第 3 条，而第 3 条只管带后缀的 Information 行。所以对 Warning 行而言，新口径比 M-2 严，与「口径与 M-2 相同」的说法不符。
- 为什么可触达：正式全量的原始输出（scratchpad `t31/formal-raw.txt` 约 3712、3739 行）表明，仍为 Warning 的那 2 对 `Failed` 是 `SocketException (32): Broken pipe`，出自 TouchSocket 的 `TcpTransport.RunSend`，当时 `m_online` 还是 true，所以 `Resolve` 不为 null；带后缀的 4 对都是本仓 `IOException("closed before its send pipe drained")`。这就是 F-126。`Broken pipe` 的前提是：对端关闭后服务端先写成功一次，对端回了 RST，服务端再写第二次。(d) 轮的登录连接若在工具断开后又收到不止一帧，就可能出这种行，按新第 1 条直接计入 S5。M-2 的 16 次运行中，(d) 一类 Warning 为 0（`m2-summary.md:307` 表），所以概率低，但会造成不稳定的误判。
- 就绪探测本身**不受影响**（任务第 2 点的直接答案）：M-2 那 16 行的异常是 `ClientNotConnectedException`。TouchSocket 4.3.1 的 `ThrowIfClientNotConnected` 只在 `m_online` 为 false 时抛出（scratchpad `ildump/sess.txt:441-446`），而 `Resolve` 读的是同一个字段（`get_Online`，同文件 `:56-58`），这个字段关闭后不会恢复为 true。所以这一形状下 `IsGone` 必然为真，两行必然是带后缀的 Information。要出 F-126 的 Warning，探测连接得先收到一帧、触发 RST，服务端再写第二帧；而探测连接只会收到一条初始连接通知。第 4 条「探测行以 Information 出现」因此是稳定的。
- 最小修法（只改文档，不改代码，由主控方修订裁定第 2 条后写进 §4）：
  1. 第 1 条恢复 M-2 原规则：「收尾窗口外的 Warning 行，若属于 (d) 轮里工具在 LOGIN 应答后主动断开的登录连接、且在断开后 1 s 内，归为『工具主动断开，不计』，逐条列出」，并删去「口径与 M-2 相同」之外的歧义；
  2. （可选）第 4 条补一句判读：工具先断开的连接上出现 Warning 的 `Failed`，且异常是 `SocketException`（Broken pipe／Connection reset），按 F-126 记录并回报，不当作「§3 的判定没覆盖这条路径」的证据。这样 F-126 与判定写宽两种情形分得开。
  3. 另外建议写明：探测连接上两行都没有出现（初始通知在关闭被处理之前就送达了）时，记为「本次无级别证据」，不算命中。

### 2. 低——Channel 入站一支的 F-30 降级没有用例或变异守着

- `Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs:88-101` 与 Game 一支（`:183-197`）写法相同，但 MF1 只变异 Game 一支，新用例也只走 Game。代码与 Game 对称，读码确认无误，实际风险低。不要求本单元补；可以写进 TASK-34 的遗留问题，或者接受现状。

### 3. 极低——格式瑕疵

- `Server/tests/Dfo.Server.Application.Tests/DomainEvents/DomainEventDispatchTests.cs` Fixture 中 `Services =services.BuildServiceProvider(...)` 少一个空格，是这次改动带进来的。不影响行为与断言，不要求返工。

## 已核实无问题的项目

| 项 | 核实内容与依据 |
|---|---|
| F-30 的 11 处降级 | 逐处读差异：Debug 6 处（Channel、Game、HTTP 中间件、卡牌两个回调、传输层排空），Warning 5 处（事件组退避、两塔扫描、计划任务重试用尽、Joust）；6 ＋ 5 ＝ 11。每处都按 `GameplayJob.EndedAJob(error)` 分支。标记是 `ConditionalWeakTable` 按异常对象登记，只在执行器 `MarkJobFailure` 处登记（`GameplayExecutor.cs:318`，在失败日志之后）。拒绝入队（已停止、深度超限）与作业体之外的异常不会被标记，仍是原来的 `Error` 并带异常。入站插件的返回／重抛控制流不变。两塔与 Joust 用 `when` 过滤加在原 `catch` 之前，原 `Error` 分支保留。 |
| 「只复述」与「带额外事实」的分类 | 合理：退避、服务继续、重试用尽、下个 tick 重评都是执行器那条 `Error` 里没有的事实，所以留 Warning、不附堆栈。 |
| Joust、传输层排空两处新生产者 | Joust 是 `ExecuteAsync` 包住 `TickAsync` 的 catch；排空是 `DispatchDisconnectedAsync` 之外的 catch。生命周期分发器对作业失败不吞异常、对已停止是记 Warning 后重抛（未标记，排空那一行仍 `Error`），所以排空处的判定是对的。 |
| `OnTcpClosed` 不加 catch | `logs/task-31-verify.txt` V5 的 IL 读法与代码一致：异常经 `SafeWaitAsync` 收成 `Result`，`PluginManager` 不记日志，不存在第二条记录。 |
| 执行器失败日志 | 只有失败模板加了 `connection {SourceConnection}`；开始、完成、取消三条模板与级别未动。M-2 的 `t28/signals.py:45` 解析式 `Gameplay (.+?) failed: ` 不受影响。 |
| F-31 | 三处 `GameplayExecutorStoppedException` 都把异常传给了日志；事件组与卡牌那两行 `InnerException` 非空时为 Warning，生命周期那一行仍是 Warning。既有用例 `Group_loops_end_quietly…` 判的是 Information 计数，正常停机时 `InnerException` 为空，不受影响。 |
| F-121 判定的范围 | `IsGone` 只在两处 `Failed` 结束时求值，`Complete` 与 `FailConnection` 都再判一次 `reason/outcome == Failed`。`TimedOut`／`OverLimit`／有序等待超时不走这条路。`OutboundOutcome`、报告与观察者通知不变；`gone` 参数只经私有的 `End`／`Complete`／`FailConnection` 传递。`Resolve` 只读 registry 与 `Online`，没有副作用。三条成对用例与 MF7a/b/c 守住两个方向。我自己按小过滤复跑：Networking 中 `OutboundSenderTests.A_*` 与 `HandlerFailureConnectionIntegrationTests` 共 16 条，全部通过。 |
| F-33 | 可写分支逐字未变。只读／缺失分支为 `EnableBuffering(bodyLimit)`：阈值 32 KiB，不设上限，读体上限由服务器自己的 `MaxRequestBodySize` 在读时执行，失败照旧经 `FailedBody` 重放给委托，与基准里委托自己读体的上限相同。两条用例经 `RunsOnGameplay` 的真实过滤器进入（不改可见性），40 KiB、按 4 KiB 分块、不可 seek 的请求体，逐字节相同且 `Position` 为 0；MF8 让两条都变红。B1 的断言没改。临时文件这一处偏离已按裁定第 3 条写进收口记录。 |
| F-32 用例 | 装饰的是 `ISelectedSessionDomainService.FindByConnection`，并且只在 `inbound:GameWeddingResponse` 作业内、只抛一次，所以异常确实从作业体里出来。断言覆盖：失败连接第一次读就是 FIN（没有回包）；另一条连接回包字节与 EXIT 正常；执行器的 `Error` 恰好 1 条，带注入的异常，`SourceConnection` 是失败连接；整个日志 `Error` 只有 1 条。MF1、MF5、MF9 各自变红。 |
| HTTP 根用例 | 经真实的启动器注册、登录 → 游戏端口 LOGIN → 再次 HTTP 登录完成顶替；观察者只读，在拆除时记下 `IsOnExecutor` 与 `Current`。断言来源 `Lifecycle`、类型 `lifecycle:disconnected`、深度 1、来源连接为空、唯一祖先跳 `(Http, "http:POST /api/launcher/v1/login")`。MF10 下变红。补 `GameLogin:Environment` 只在用例内，`LauncherHost` 未改。 |
| AC-009③ 用例 | `WireClient` 用 `++sequence`（`WireClient.cs:65`），所以发送后读到的私有字段就是刚发出的那一帧的序号，重发帧确实同序号。MF11（账本把已知重发当新请求）让该用例变红，说明重发帧解析有效、到达了账本，「没有第二个结算回包、库存与幸运星都不变」不是因为帧被丢弃而空过。直接写 `initialization_settings` 在建角前、用例内写明了理由，表征目录未改。用「连接仍开、EXIT 得 `[1]`」代替执行器日志：失败作业会关闭来源连接，能证明「作业没有失败」；它证明不了「作业体内被接住的内部错误」，这一点已在收口记录写明，可以接受。 |
| 三处日志断言改写 | `OutboxWireTests` 两处由「生产者一条」改为「执行器 `inbound:GameExit failed` 在 Error 恰 1 条 ＋ 生产者在 Error 为 0」，`InventorySlotMappingFailureIntegrationTests` 等待的片段换成执行器那一条，异常类型与消息断言不变。保护意图保留，而且更强。线上字节、帧序、连接开关的断言一处未动。 |
| `LogEntry` 加 `Exception` | `DomainEventDispatchTests.*` 里对 `Logs.Entries` 的使用全是谓词（没有按记录相等比较），可选字段默认为 null，既有断言的语义不变；只有新用例读它。 |
| F-111 与 26 处改名 | 自己按文件统计差异：16 个文件只改方法声明行；`DomainEventDispatchTests.cs` 有 1 处改名，其余是新增内容。合计 26 处改名，分布在 17 个文件。F-111 的三个文件只有删除（23／25／31 行），四个类型名在 `Server/tests` 中 0 命中。第 24 条的新名 `Pvp_plan_publishes_the_newcomer_to_its_audience_and_the_selection_completes_every_stage` 与它今天的断言相符（阶段顺序、单条 `TheseCharacters` 公示、未关闭连接）。 |
| 数字 | 原始输出各项目通过数 57／80／1752／477／968／188／4576（失败 2）／197，与日志开头一致。8295 ＋ 2 ＋ 18 ＝ 8315 ＝ 8302 ＋ 13 － 0。N 13 ＝ Application 4 ＋ HttpApi 3 ＋ Networking 5 ＋ Host 1；R 26。取证 73 ＝ 2 ＋ 1 ＋ 1 ＋ 69，反例类与对照以外为 0，`exe:` 为 0。MF1～MF11（含 MF2 重跑、MF7a/b/c）都有指定用例变红，变异前后 sha1 相同。表征用例 10 条通过、0 条跳过。 |
| 边界与行尾 | `git -c core.whitespace=cr-at-eol diff --check` 无输出。`spec.md`、`plan.md`、`AGENTS.md`、`Docs/`、`protocol/`、`Characterization/` 相对基准都没有差异。新文件为 LF。共享契约、csproj 与可见性都未改。 |

## 交给主控方

- 第 1 条发现：须在 TASK-32 开始前修订裁定第 2 条的文字（只改 `t009-logs-and-cases.md` §4，不触及冻结的代码）。不修订也可以运行，但 (d) 轮的 F-126 形状会被计为 S5 命中，届时按回报处理。
- 本评审不代替提交前的完成核对。提交哈希仍要按 Result Remaining 补写进 `follow-ups.md` 与收口记录 §8。
