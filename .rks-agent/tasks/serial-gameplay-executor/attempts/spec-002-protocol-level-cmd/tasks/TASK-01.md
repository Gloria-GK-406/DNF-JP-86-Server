# TASK-01 — 自有 SQLite 池清理与发送写端关闭的失败日志分类

## Task Unit Contract

- Revision: 2（r1原计划保留；本修订只按文末主控裁定补两处同接口测试消费者）
- Outcome and references: 完成父 [Plan](../plan.md) T-001，对应 [Spec B1](../spec.md) RQ-010、RQ-011、AC-006、VC-B1：Application.Tests 已移交的 25 个文件不再进程级清池，仅清理自身规范化数据库的实际写池及只读池；发送已经失败、写端已完成或正在关闭时，两条 Failed 日志用既有 Information/gone 后缀，普通故障仍 Warning，发送结果、关闭、observer、后继条目及其他连接不变。
- Owned boundary: 下文「精确文件边界」中的产品、测试与文档；本 Task，以及本子 Spec 自己的 `verification/logs/task-01-*`、`verification/evidence/task-01-*` 执行证据。研究输入 [first-unit-facts.md](../research/first-unit-facts.md) 只读。不得修改 A 封存资料、父 Spec/Plan/契约，不启动 CMD 骨干或后续迁移。
- Required checks: 下文检查矩阵及命令：有意义的清池隔离 RED/GREEN；真实传输两个时序窗口的 RED/GREEN 与普通故障对照；完整 Release 构建；完整 Application.Tests 与 Networking.Tests（原类并行、原断言/期限/Skip，detailed 日志与新鲜断言取证）；源调用/所有权/catch/行尾核对、数量账和范围检查。
- Result review: required — 25 处清理中任何漏池或越权会影响同进程其他并行类；传输共享接口和真实关闭窗口的错误可能改变 Failed/关闭/通告而在局部假连接测试中漏检。须独立核对实际连接串、所有权及真实线证据，再由主控做检查点；本计划不代替结果快审。

## Detailed Plan

### 1. 起点、授权与精确文件边界

输入为已独立 approved 的父 Plan P1（`reviews/plan-review.md`），Spec B1 Round 2 与 child TaskContract r1。规划检查点为 `49ce251a5321013b18ffd49fe833bb565fd2c4de`，Spec baseline 为 `10a7218a779493494b138bd6b43cc0fc1a84aa6a`。本 Task 没有 Task Unit 依赖；这些检查点是输入锚点，不是伪造的 TASK 依赖。沿用父任务已授权的实施、验证与本地检查点，不推送、不开 PR、不合并、不改远端 issue、不部署或清理工作区，不运行 df_game_r/DNF.exe。检查点由主控按现有 Co-Authored-By 约定处理。

开工先复核 `git status --short`、HEAD、本文件输入锚点及原调用数；差异若不是已交付本 Task 的计划文件，不覆盖他人改动。读取实际 owning source；没有 `.codegraph/` 则按仓库约定直接检索。记录下表各文件的实际连接串、数据库路径、Dispose/清池/删除/catch 次序及行尾统计，作为最终 diff 对照，不仅做调用次数替换。

以下 25 路径相对 `Server/tests/Dfo.Server.Application.Tests/`，均含一个直接 ClearAllPools 调用：

| 允许修改的文件 | 自有数据库与须保持的清理边界 |
|---|---|
| `Authentication/GameLoginCommandTransactionTests.cs` | `directory/game-login.db`；services 先释放，目录删除原样抛错 |
| `Characters/CharacterDomainStoreSettlementTests.cs` | `this.Path`；局部连接先结束，删除仅吞 IOException |
| `Characters/CharacterItemUseTests.cs` | `directory/characters.db`；将 databasePath 提到 try 外复用，目录删除原样抛错 |
| `Characters/CharacterSettingsTests.cs` | `this.Path`；删除仅吞 IOException |
| `Characters/CollectionBoxTests.cs` | `path`；Services.DisposeAsync 后清池，db/wal/shm 删除原样抛错 |
| `Characters/DarkKnightComboBookTests.cs` | `this.Path`；删除仅吞 IOException |
| `Characters/OutsideTransactionTests.cs` | `this.Path`；删除仅吞 IOException |
| `Creatures/CreatureRuntimeDomainServiceTests.cs` | `this.Path`；删除原样抛错 |
| `Creatures/CreatureTransformationTests.cs` | 局部 `path`；连接先结束，删除原样抛错 |
| `Events/SaoTowerEntranceEventTests.cs` | `directory/test.db`；Services.Dispose 后 `owns=false` 立即返回，owner 才清池和删除 |
| `Execution/GameplayExecutorLifetimeTests.cs` | `directory/test.db`；host 先释放，目录删除仅吞 IOException |
| `Execution/TimerSourceHost.cs` | `directory/timer.db`；停止已启动服务→provider.DisposeAsync→清池；目录删除保留 IOException/UnauthorizedAccessException |
| `Game/CreatureGoesBackOnTests.cs` | `path`；content.Dispose 后清池，删除仅吞 IOException |
| `Inventory/CreatureKindAgreesWithTheArchiveTests.cs` | `path`；连接先结束，删除仅吞 IOException |
| `Inventory/DraggedAvatarStaysWholeTests.cs` | `path`；删除仅吞 IOException |
| `Inventory/GrantedAvatarIsWholeTests.cs` | `path`；原混合 283 CRLF/297 LF，字节级定点编辑；删除仅吞 IOException |
| `Inventory/MovingInsideTheAvatarListTests.cs` | `path`；删除原样抛错 |
| `Inventory/PickedUpAvatarStaysWholeTests.cs` | `path`；删除仅吞 IOException |
| `Mail/MailboxCoreIntegrationTests.cs` | `directory/mail.db`；provider 先释放，已有非 owner 返回继续保留 |
| `Marriage/MarriageArchitectureTests.cs` | `path`；Provider.Dispose 后清池，删除原样抛错 |
| `OnlineUsers/SerialDisconnectTests.cs` | `directory/test.db`；await using provider 先结束，目录删除仅吞 IOException |
| `Quests/QuestLogTests.cs` | `this.Path`；删除仅吞 IOException |
| `Transactions/DungeonPickupTransactionTests.cs` | `path`；await using provider 先结束，删除仅吞 IOException |
| `Transactions/OneDatabaseOneParticipantTests.cs` | `this.Path` 与 `this.OtherPath`；两库各清写/只读池，再各按原 IOException 边界删文件 |
| `World/PaidCardFlipCostTests.cs` | `this._path`；Discard 自有库，删除仅吞 IOException |

另外允许：

- App 新增 `Support/SqliteTestPools.cs` 与 `Support/SqliteTestPoolsTests.cs`（namespace `Dfo.Server.Application.Tests`；子 namespace 可解析），仅承载本项清池与隔离回归；`Execution/TimerSourceJobFailureTests.cs` 仅同步 `SqliteTestFiles.Delete` 的显式数据库路径调用。
- `Server/src/Dfo.Server.Networking/Outbound/OutboundTransport.cs` 中的 `IOutboundConnection` 和 TouchSocket adapter；`OutboundSender.cs` 中两处现有发送 Failed catch 的分类接线及其说明。
- `Server/tests/Dfo.Server.Networking.Tests/Outbound/OutboundSenderTests.cs`、`OutboundRealSocketTests.cs`、`OutboundTestSupport.cs`，仅本项回归、真实 fixture 及 FakeConnection 的接口适配。
- 根 `AGENTS.md` 只更新当前 F-127 发送端日志已知缺口那一段，使其与新证据一致；不修改其他架构、A 审查/封存结论或偶发登记。

不改持久化产品/schema、其他测试进程的清池、Resolve/精确连接语义、Failed/关闭/observer/队列策略；不按所有 InvalidOperationException 降级；不扩大删除 catch 吞清池异常；不新增生产测试开关或无关防御性重构。若真实执行表明必须越出上述边界，先将具体证据交主控调整，不顺手扩项。

### 2. F-130：先证明可观察差异，再统一自有连接串与清池

本地 pinned Microsoft.Data.Sqlite 10.0.10 按连接串原文分组。生产 `Persistence/Database/SqliteDatabase.cs` 的写串仅 `new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath) }.ToString()`，`SqliteDatabaseParticipant` 的只读串从它加入 `Mode=ReadOnly`。手写 `Data Source=...` 与 builder 输出不能假定池键相同。

选择小型 App-only helper，而不复制多个清池算法：

1. `SqliteTestPools.ConnectionString(string databasePath, bool readOnly = false)` 按上面的生产规则返回串；默认写串不显式填 Mode、Cache、Pooling、ForeignKeys 等默认项，不 ResolveLinkTarget。只读从同一写串 builder 加 Mode=ReadOnly，顺序与生产一致。
2. `SqliteTestPools.ClearOwned(params string[] databasePaths)` 对明确传入的各自有库清两个池。每个串用一个未 Open 的 `SqliteConnection` 交给 `SqliteConnection.ClearPool`，随后 Dispose 该临时连接；驱动构造时已绑定 PoolGroup，不为清池执行额外读写。此方法不删文件、不扫描目录、不全局清池、不吞异常。
3. 表中直接 raw/builder 连接串及显式 schema 初始化串统一调用 `ConnectionString`；已来自 `database.ConnectionString` 的保持同一生产真源。检查所有该文件的直接 Open，不只邻近 Dispose 的一处；含相同文件中 `fixture.Path`、`f.Path`、`this.Path` 等变体。研究已列的现用构造只有 DataSource，无另一个需保留的测试选项；若现场不同先核实实际用途，不悄悄丢选项。

先建有价值的 RED（可暂将新 ClearOwned 的实现保留为原全清行为，运行结束后立即替换，不提交全清 stub）：

- 新回归创建 A、B 两个独立临时数据库，分别建立**写池、只读池**。写侧用生产 `SqliteDatabase.ConnectionString` 或生产同串打开；只读侧经真实只读 domain transaction 建池，或用已核对 `SqliteDatabaseParticipant` 的实际派生串打开，避免所有预置和清理都只调用同一 helper 而自证。
- 保存每个已 Open 连接的公开 `Handle`，再 Close/Dispose 使其成为空闲池连接，断言四个 Handle 清理前均未关闭。对 A 使用含相对成分的路径（用 GetRelativePath，不改进程 CurrentDirectory），但预置池的 DataSource 为实际规范化路径。
- 调 `ClearOwned(A)` 后，A 的写/只读 Handle 均应 IsClosed；B 的两 Handle 均应未关，且重新打开两种 B 连接仍可查询。旧全清使 B 句柄被关，形成真正断言 RED；仅 B 的活动连接能查询不能作替代 oracle。
- 该同一 oracle 还应能区别只清写池（A 只读未关）、遗漏 GetFullPath（A 原池未关）；在 scoped 实现后做有界临时变异运行，并恢复，保留断言失败证据。不要增加复制 helper 算法的字符串镜像测试。
- finally 仅用各自明示路径清理 A/B，再按已有释放顺序删其临时文件；RED 跑单类避免旧全清 stub 干扰其他类。编译失败不计 RED。

然后实现 scoped helper 并替换表中 25 处；新 helper 和所有 source 均不再保留进程全清调用。每处保留原 provider/连接停止与释放先于清池，清池在原删除 try/catch 之外。`SaoTowerEntranceEventTests.Fixture.Dispose` 调整为 dispose 服务、非 owner 返回、owner 清池和删目录，Reopen 不清原 fixture 的池。`OneDatabaseOneParticipantTests` 显式传两个自有路径。

`SqliteTestFiles.Delete` 改为显式接收目录及 databasePath：先调用清池 helper，再在原删除 catch 范围内删目录；`TimerSourceHost` 和 `TimerSourceJobFailureTests` 两个调用点显式提供实际 timer.db，后者保持 host 在外层 finally 前释放。避免重命名/移动其他 timer 辅助能力。

混合行尾文件先存字节统计及所改片段原字节；按原行尾做 bytes 定点替换，只删除不再需要的 using，不运行 formatter、不整文件 read_text/write_text。其他文件保持原行尾。清池隔离 GREEN 后才跑使用这些 fixture 的完整 App 项目。

### 3. F-127：真实写端失败 RED 与仅失败出口的分类

现有 `OutboundSender.DeliverAsync` 有同步启动发送 catch、异步等待发送 catch 两处 Failed 出口；超时与取消的前置 catch 继续原样。`IsGone` 目前只看 Resolve-null；`OutboundTransport.Resolve` 的 exact+Online 检查必须不变，否则真实窗口会变成 Stale/close:false，绕过本来要保留的失败通告。

先扩真实 `OutboundRealSocketTests.RealOutboundServer` 夹具，使最小 `Received` 回调收到控制字节后通知 entered TCS 并等待 release TCS。真实客户端 A 触发回调；回调被挡期间关闭/完成真实 transport，Session receive loop 仍无法结束，保留 Session.Online 和 registry 的精确 Context。取得 pinned `TcpSessionClientBase.Transport` 若为 protected，可在测试夹具边界使用最小 subclass 或已核实成员的反射；不改生产访问级别或加测试开关。另接健康客户端 B。

两个独立场景经**真实 ISender→真实 PipeWriter→现有 OutboundSender**触发：

| 场景 | 发送前必须观察到的状态 | 必须区分的行为 |
|---|---|---|
| 真实 `Transport.CloseAsync`，Received 仍被 gate 挡住 | transport ClosedToken 已取消；Session.Online 为真；生产 Resolve(A) 仍非 null | 发送真实失败而非 Stale；两条 Failed 日志应 Information/gone |
| 只 Complete 实际 `Transport.Writer`，保持 socket/Received 活着 | ClosedToken 未取消；Session.Online 为真；生产 Resolve(A) 仍非 null | 真实 writer-completed 失败也降级，证明仅查 token 不足 |

先保持当前产品实现运行，证明夹具在既有 Patience 内确实产生 writer-completed 原异常、Failed 和当前两 Warning，加入期望 Info 后获得明确 RED。若不能触发真实异常或前置状态不成立，这是夹具未建立窗口，先调整 fixture 并记录事实；不能把超时、编译失败、fake 异常或尚未运行的研究候选当 RED。finally 无条件释放 gate 并等生命周期结束，不让挂起 receive 泄漏到后续例。

落定接口：在现有 internal `IOutboundConnection` 增加只读 `bool HasClosedSendSide(Exception error)`，TouchSocket connection 实现只查询发送失败所用连接的写端事实；FakeConnection 显式提供同语义的可控值，默认 false。该方法不发帧、不关连接、不修改 registry。

- adapter 对已取消的真实 ClosedToken 返回关闭事实；再补 pinned writer 已完成窗口的严格识别。只能识别真实 RED 中观察并对照 pinned System.IO.Pipelines NoWritingAllowed throw-site 的确定形态（精确 InvalidOperationException 类型、该 throw-site 的资源文本及发送栈/等价直接写端完成事实）。不能只匹配一个泛化单词、所有 InvalidOperationException 或所有 pipe 错误；通用 sender 不匹配 TouchSocket/框架错误文本。具体异常形态以真实运行保留的栈为输入，不猜写一个未实证的字面量。
- `ClosedEventArgs != null` 不是事实：pinned getter 未关闭也返回默认对象。ClosedToken 单独也不够，RunSend 可能先完成 Writer、RunReceive 后取消 token。若对已 disposed transport 读取 token 确实抛 ObjectDisposedException，在 adapter 此处窄范围将已释放记作关闭事实，不让分类覆盖原始发送异常；不为此重构发送/Resolve/关闭路径或扩大 catch 吞错。
- Sender 仅在上述两处 Failed catch 求 `IsGone(item) || connection.HasClosedSendSide(error)`，把一个固定 gone bool 传入原 queue.End；entry 和 connection 两日志复用该值。保留原异常对象（report 与日志仍持同一 error），不包成新异常、不重新发送；不改 Complete/FailConnection/CloseAsync/queue.End 的执行流程。

真实测试除了两 Info/gone 与原异常外，验证首条 Failed、同连接后继 Discarded、failure observer 仅一次、sender 发出的 close 请求保持原次数、健康 B 正常落线。fixture 如需捕获原异常/close 次数，可用透明测试 wrapper 转发真实 connection 并 `throw;`，不注入伪失败。区分测试主动 CloseAsync 与 sender 后续 close 请求的计数，不把预先关 transport 误算为 sender 重复关闭。记录报告与捕获错误 ReferenceEquals。

保留并运行已有「连接已摘除」Info、普通 IOException Warning、gone 时 TimedOut Warning 对照。加普通 InvalidOperationException 的 Warning 负例，守住 sender 不按类型降级；另在真实健康连接、未取消 token 的前提下，向真实 adapter 的 HasClosedSendSide 传入同文本但非该 writer throw-site 的异常，断言 false，随后真实发送成功，避免仅 FakeConnection 默认 false 自证 adapter 分类。两 Failed catch 至少各有可达的同步/异步 fake 控制，真实窗口覆盖真实 adapter。既有健康连接、跨连接及 Required/后继顺序用例不改断言，完整 Networking 回归接着运行。

### 4. 检查矩阵、命令与证据

所有正式 dotnet 构建/测试在原生 `require_escalated` 本机环境运行，避免已知 sandbox FSEvents 注册停滞；不是靠改 watchdog/类并行求绿。使用 tool 的 cwd 指向本 worktree，原 native 命令参数：`--blame-hang-timeout 5m`、`-m:1` 可串项目，保持 xUnit 原类并行、原每例 Patience、Skip/断言不变。若环境挂起，保留中断/诊断，不能把未结束测试当通过。

证据前缀 `task-01-<阶段>-<唯一运行号>`，先检查不存在再建。RED、GREEN、变异、正式全量分开目录；日志同时保存 cwd/HEAD、实际命令、起止时间、环境差异和退出码。以下 `B` 表示本子 Spec 目录，仅在记录/脚本中作为普通局部变量，不重用 HOME/CODEX_HOME。

| 阶段 | 检查及接受依据 |
|---|---|
| Pool RED/GREEN | 定向 `FullyQualifiedName~SqliteTestPoolsTests`；旧全清确因 B 空闲句柄关闭而红；scoped 清 A 关 A 两池、保 B 两池；write-only/不规范化两变异红后恢复 |
| Transport RED/GREEN | 定向新真实两窗口；每轮保留前置状态、真实异常/栈、Info/Warning 对照及 outcomes。新故障分类接线前红、后绿；普通 InvalidOperationException/Timeout/健康回归绿 |
| Release build | `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`；0 警告、0 错误 |
| 完整 App | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`；不加 filter |
| 完整 Networking | `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`；不加 filter |
| 源码及差异 | 下述精确路径、调用/串、owner、catch、行尾检查；`git -c core.whitespace=cr-at-eol diff --check`，未跟踪文件另核对，差异交主控检查点 |

正式 App/Networking 两项目测试使用同一**新鲜** `DFO_GAMEPLAY_ASSERTIONS=$B/verification/evidence/task-01-formal-<运行号>`（不能混入 RED/变异数据），按实际执行项目做 partial 取证核验：

```text
python3 .rks-agent/tasks/serial-gameplay-executor/verification/probe-tools/violations.py <本轮freshDir> --assemblies Dfo.Server.Application.Tests,Dfo.Server.Networking.Tests Dfo.Server.Application.Tests:CommandBusExecutorTests=1 Dfo.Server.Application.Tests:EffectOperationReplayTests=1 Dfo.Server.Application.Tests:GameplayOutboxTests=3 Dfo.Server.Application.Tests:GameplayQueueLeakTests=1 Dfo.Server.Application.Tests:GameplayWriteAssertionTests=53 Dfo.Server.Application.Tests:NotificationDeliveryTests=1 Dfo.Server.Networking.Tests:OutboundRealSocketTests=1 Dfo.Server.Networking.Tests:OutboxRegistrationTests=5
```

预计 App 61（60 反例+1 control）、Networking 7（6 反例+1 control），共 68；两个实际程序集各一 control，反例类/control 外 0、exe 0、无测试帧 0。新两个真实失败例应不产生 gameplay assertion；若有新增记录须先解释真实归属，不能为求汇总过关放宽 expected。定向轮只要求该轮实际包含的 control/反例，不能要求未执行的七程序集文件；最终八项目核验保留给父 Plan T-012。

正式测试基数：App 4626＝4608 通过+2 已知失败+16 跳过，Networking 190 全通过。新加 N、删除 D 按实际新增/理论 case 分项，不预填固定数；Task 结束闭合 App/Networking 实测数量和 `8350+N-D` 总账，明确其他六项目此 Task 未重跑，不能据推算写八项目已通过。两条允许基线仍是 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot` 的 dungeon7100/entrance Left 内容错误；App 因此 exit1 必须如实记录。其他失败按 AGENTS 的具体身份规则调查；新栈根不能直接套“已知 SQLite 偶发”，本项不声称证明所有历史偶发根因。

本 Task 没有改玩法执行路径，不要求提前跑 CMD 性能负载；记录构建/测试时长及已实际取得的内存数据，未测项目不编数字。单条 CMD 2 秒接受线与最终同 A 测量仍由 T-012执行。

### 5. 完成实施后的边界核对与交回

只将 AGENTS 上述当前日志段更新为核验后的事实：已不可解析、真实写端关闭/已完成的发送 Failed 均走既有 Info/gone；普通故障与超时 Warning；其余关闭/队列语义不变。真实证据未通过前不删缺口、不声称修复。

逐文件对照第 1 节的自有路径、所有权、Dispose/清池/删除/catch 顺序；检查 25 文件全部实际连接串与 production write/read-only 原文一致，Timer 所有调用点传实路径，双库覆盖，Reopen 不越权。对 App source 执行 ClearAllPools 检索并人工分类要求调用 0（含新增 helper/tests）；不扩查结果为其他测试项目的修复范围。检查新 helper 没有目录扫描/全池枚举，未改 schema/类并行/deadline/Skip；核对 `GrantedAvatarIsWholeTests` 行尾统计及 diff 中没有无关换行。检查 Resolve 与超时/关闭/observer 代码未被顺手改写，两个 Failed catch 使用同一个分类出口。

执行者将真实 RED/GREEN、原文串/owner清单、完整命令/日志/取证、N-D 数量、断言改动与仅驱动/准备改动分别记入本 Task 后续执行结果并引用 B 证据；保留未能建立窗口或异常失败的原日志，不重写历史。然后交主控组织 required 独立结果快审和检查点；此处不预写结果、批准或 Spec 完成判断。


## Controller Disposition — r2 有界接口消费者补齐

- Decision: `task-rework`；保持 TASK-01 未提交并继续同一结果，不新增 Task，不修改已批准 Spec B1 / Plan P1。
- Evidence: 执行者枚举新 `IOutboundConnection.HasClosedSendSide(Exception)` 的实现者后发现原精确清单漏列 `Server/tests/Support/XoDelivery.cs:214` 的 `Connection` 与 `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationWorld.cs:195` 的 `WorldConnection`；二者实际实现同一接口，缺成员会导致完整构建 CS0535。主控已直接检索确认。
- Amendment: Owned boundary 仅补上述两文件的接口适配，各显式返回 `false`，保留原 fake 的发送/关闭/失败语义，不改其它内容；其余 r1 计划与所需检查保持。linked Xo helper 的额外程序集消费者由完整 Release 构建覆盖；本 Task 正式测试仍为完整 App/Networking，最终八项目仍归 T-012。
- Readiness / goal check: 此修正是本单元已选共享接口的必需消费者适配，未扩展 F-127 行为或 F-130 范围；required 独立结果快审仍包含这两处。没有新 Task 依赖或待用户决定，主控允许恢复生产与原检查。


## Result

- Outcome: done（实施轮 R1，Task 契约 r2；等待主控 required 独立结果快审及本地检查点，不表示 Spec B 完成）。
- Changed: F-130 的 App25 源文件改为显式自有数据库 `ClearOwned`；新增 App-only `SqliteTestPools` 统一生产同形的规范化写/只读连接串并分别清池，全部实际 raw/builder 直连同步归一。Timer 两调用点显式 timer.db、双库显式两路径、Sao 非 owner Reopen 释放服务后即返回，原释放顺序和删除 catch 不扩展。F-127 增 `IOutboundConnection.HasClosedSendSide`；TouchSocket adapter 仅按 ClosedToken 或 pinned Pipelines 的 exact type+完整资源+真实 throw-site+assembly 判断，两个 Failed catch 在自身 close 前取事实，复用原两 Info/gone 日志。原异常、Failed/Timeout/Stale、close/observer/后继/其他连接语义保持。r2 两个额外 fake 实现只返回 false。AGENTS 只更新该日志段。
- Why: pinned SQLite 按原文串分池，所以只按相同文件名或只清写池不足；四空闲句柄 oracle 已实际区分。真实 transport 证明 Writer 可先完成而 token 未取消、Online/Resolve 仍真，故 Resolve 或 token 单独均不覆盖窗口；同文异 throw-site负例守住普通故障不降级。关闭完成后读取实际 session.ClosedToken 仍为 true，没有观察到 ObjectDisposedException，因此没有加入猜测性 catch。
- Checks: 精确 owned 产品/测试/AGENTS 共36路径（34 tracked+2新增），[diff与源码SHA256](../verification/evidence/task-01-inspection/owned-product-sha256.json)、[逐文件前后/所有权](../verification/evidence/task-01-inspection/owned-source-after.json)、[源码自检](../verification/evidence/task-01-inspection/source-checks.json)。App source ClearAllPools 调用0；25处原删除 catch 列表逐字相同；Resolve实现逐字不变；Granted 原始/最终283 CRLF+14纯LF＝297总换行，其他行尾保持。未改schema、并行/期限/Skip、旧业务/codec/Golden断言；旧测试只改驱动/准备/清理及接口适配，新增断言均在新测试。`git -c core.whitespace=cr-at-eol diff --check`通过；新增源码无尾随空白，原始日志原样保留。
- Checks — RED/GREEN: `verification/logs/task-01-pool-red-01`（全清stub使B闲置写句柄被关，exit1）→`pool-green-01`（exit0，1/1）；`pool-mutation-write-only-01`与`pool-mutation-unnormalized-01`均exit1，分别拒绝A只读池漏清及未规范化，已恢复。`transport-red-03`两真实窗口均先通过 Failed/Discarded/Delivered、原异常引用、observer1/close1与健康落线，仅Info期望对Warning红（exit1）→`transport-green-02`7/7 exit0。所有同名前缀的.log/.json在[B logs](../verification/logs/)记录实际命令、HEAD、UTC起止、环境和退出码；[说明与实际变异脚本](../verification/evidence/task-01-inspection/README.md)包含test-value gate、真实栈、官方runtime资源及复现方法。transport-red-01/green-01是编译修正，red-02是fixture等待环超时，均不计行为RED，原日志保留；环已改为先观察关闭请求、finally释放receive再等close完成。
- Checks — 正式: 本机require_escalated、caffeinate、原类并行和5m blame期限。`formal-build-01`17.676s及最终`formal-build-02`24.940s，均exit0/0 warning/0 error。`formal-app-01`134.553s，4627＝4609通过+2失败+16跳过，exit1；仅两条既有DimensionDungeon方法失败，均同样 dungeon7100 maze0 cell(1,0) entrance Left 的 InvalidDataException，无新增失败。`formal-networking-01`67.848s，197/197 exit0；最终自检把新fixture的Write/Entered等待纳入无条件finally释放保护（不改断言），重新完整build后`formal-networking-02`54.313s，197/197 exit0。产品/App代码在这些轮次中未变；最后测试夹具小改只需重复Net，未冒称重复App。其他六程序集仅完整构建，没有本Task全量测试。
- Checks — 断言取证: `formal-01`新鲜目录 App61+Net7＝68条、两程序集各一control；`formal-02`独立新鲜目录只有最终Net7及其一control。分别运行原 violations.py（仅列实际程序集/原expected类数量），`task-01-assertions-formal-01/02`均exit0，outside0/exe0/无测试帧0，原类expected全部匹配；未混合/复制jsonl，保留exe0仅为观测边界。
- Checks — 数量与资源: 新增8 case＝App清池1+Net真实两窗口2+健康同文负例1+同步/异步分类4；删除0、合并0，整体数量账8350+8＝8358（不是八项目实跑声明），[数量账](../verification/evidence/task-01-inspection/test-accounting.json)。每轮rusage `children_maxrss`原值在json，Darwin单位bytes、仅子进程观测而非系统/完整树峰值；套件耗时/内存不作门槛。本Task未改玩法执行路径，CMD同工具测量仍留T-012。
- Remaining: F-130不宣称证明或消除所有历史SQLite偶发根因；F-127 writer补充分支绑定当前实测.NET/TouchSocket版本及严格throw-site，升级时由这些真实回归验证。完整Spec的协议CMD/显式上下文迁移及六根条件仍待后续Task；A封存文件未改，不推送/PR/合并/远端修改。


## Independent Result Quick Review — R1

```yaml
kind: rks-review
verdict: approved
```

Reviewer: fresh independent `b_task01_review`；目标为本 Task 契约 r2、Result R1、HEAD `49ce251a5321013b18ffd49fe833bb565fd2c4de` 未提交变更。采用 `reviewing-work` 的 Task Unit、Code 判据及文档一致性；只读审查，未运行额外测试。

未发现阻止本 Task 提交的局部缺陷。36产品路径SHA256与清单一致，二fake仅false适配。逐项核对25文件自有库、双库、Timer路径、Sao非owner返回、生产同形写/只读池串及原catch；App全清调用0，Granted行尾仍283 CRLF+14纯LF。F-127只修改两Failed分类；Resolve/超时/关闭/队列未改，严格类型/全文/throw-site/assembly与同文异源负例匹配，原异常结算路径不变，AGENTS一致。

原始Pool RED、两变异、真实双窗口RED均在预定行为断言失败；真实RED的Online/Resolve/token差异、Failed/Discarded/Delivered、引用及关闭/observer先通过，随后仅日志级别红；GREEN 7/7。编译失败与fixture等待环未冒充RED。最终Release构建0/0；App4627=4609+2准确既有Dimension内容失败+16skip；Net197/197。DFO原始目录68/7各control/expected吻合、outside/exe/无帧0。新增8删除0与8358数量账如实，未宣称其余六项目或完整Spec核验通过。

主控消费：批准仅释放当前Task本地检查点。检查点后继续Plan T-002先行保护与迁移账，Spec 2仍未完成，最终测量/Astra/收尾门保持。

检查点前置核对：staged diff-check 另命中原样保存的官方 runtime-Strings.resx 尾空白；它与原始日志作为不可改写外部证据保留，非自编源码缺陷。排除这两个原样 runtime 源文件及原始日志后检查所有本次自编源码/文档/脚本；提交命令尚未调用，无失败提交或历史变化。
