# TASK-28 — Cera 购买旧协议事务与失败出口取证

## Task Unit Contract

- Revision: 1（本契约和详细计划共同修订）。
- Outcome and references: 在未迁移的 CeraShopPurchase 真正解码／dispatcher／旧 fine／SQLite／发件箱路径取得有界旧行为证据，区分 action SQL、实际 fallible Commit 确认回滚及受控不确定／清理失败；给主控提供后续受影响 Plan HOW 审查和可能 WHAT 裁定的输入。本单元属于 P4 T-004 的临近事实调查，消费 Spec B3 RQ-003/006/007/008/013/014、AC-003/004/005/008/009 与 VC 保留边界，不实施协议 CMD、不扩张 P3 四 opening 的 WriteResult 消费范围。
- Owned boundary: 本 Task；`verification/evidence/task-28-inspection/` 下新建的专用探针项目、源码、观察和证据；`verification/logs/task-28-*` 新日志。没有现存产品、Server/tests、配置或文档编辑白名单。当前全部 tracked 文件、两份 canonical migration-ledger、原工具／基准、Spec／Plan／routing、A seal、已提交 Task 和证据均只读。
- Required checks: 起点／完整 NUL tracked 字节封存；evidence-only harness 可行性检查；最小成功／业务拒绝对照和 §4 三组故障的确切阶段、cause、事务／资产、ACK／异常、会话围栏、登记／实际发送关闭及链观察；探针 Release 构建和实际运行的原命令／退出码；末尾全部原 tracked＝HEAD＝before、路径／manifest／行尾与新增 authored whitespace 闭合。Task27 的八 full 9014 库存只引用，不冒本单元重跑；不做无关全量或新坏副本。
- Dependencies: TASK-27 已独立 approved、主控消费并 committed done 于 `1aae55555ed207f3ced6593180fa8d211b60d09f`，其 Task 最终 34903B、SHA `48654b4faf34accdf5dfa51cd21b10528b950748ea7e247cf8195b86605eb60a`。其它历史方法只作只读参考，不作为未交付依赖。
- Result review: required — 后继会依据本单元决定恢复范围和可见结果；误认故障阶段、将记录当实际关闭或漏掉已提交资产，会使错误方案继续累积，探针自己断言不能独立验证这一解释。Result 后须 fresh 独立局部快审，随后由主控决定推进和本地检查点。
- Task-specific authorization: 仅调查与证据，不改运行时行为、原测试或任何现存 tracked 文件，不修 Plan／Spec、不新建后继 Task、不关闭 ledger 行；不 stage／commit／push／PR／merge／远端 issue／版本／tag／部署／游戏／工作区清理。原 fixture 独占的临时数据库按其既有构造／Dispose 生命周期使用，不接触用户库，不使用 ClearAllPools。

## Detailed Plan

### 1. 固定起点、问题及停止界线

唯一工作目录 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支 `task/refactor-session-20261021`；开始时核 HEAD 为上述 TASK-27 检查点、index 空，除本新 Task 外没有其它变化。本文 B 是 `.rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd`，E 是 `B/verification/evidence/task-28-inspection`。先读当前 Task 和 P4 §4、T-004、Next Planning Boundary 以及 B3 RQ-003/006～008、VC、AC-003～005 的相关文字。

本次已读治理身份：child Contract r1 SHA `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6`；Spec B3 32948B SHA `1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05`；Plan P4 61551B SHA `db6da8763f55366a4d44358f4537d2106f6f55d1fab0e55b6d08e357ff5d8ad7`。实施前复核，若漂移先定位，不据旧 hash 覆盖任何文件。

要回答的是旧 Cera 如何结束，不是替新 whole 选恢复 API。P4 的 WriteResult 有界消费方仍只有四 opening，§236 禁止暗扩；三 raw 出口也不包含 Cera。普通 action／Commit 已确认回滚失败 ACK 的保持至少需要受影响 Plan HOW 门。旧不确定或已提交后 scope 故障若实际也被转换成 ACK／不围栏，可能涉及 WHAT；本单元仅报告，不把它裁成获准 VC、既有保护已修、必须改 Spec 或可新增通用恢复。没有产品改动，不会因调查发现差异而偷偷修复旧行为。

### 2. 已核源码和最小探针接法

下列实际位置是实现入口，行号只为导航，执行时按当前内容校验：

| 位置 | 本单元利用的事实 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/CeraShopPurchase/CeraShopPurchaseCommand.cs:10–21`、`ProtocolModule.cs:725–726` | fine 只实现 IAccountCommand，Transactional；不实现 ISelectedSessionCommand。 |
| `.../CeraShopPurchaseHandler.cs:32–56` | await 整个 fine 的 try 捕获业务拒绝与所有非取消异常；成功通知和可选名字牌刷新在 catch 外。 |
| `.../CeraShopPurchaseMessageDecoder.cs`、`ProtocolModule.cs:979,1265` | Game CMD `0x0040` 的真实注册 decoder 和 handler；body 解出完整购物车，不接受有效前缀代替截断整包。 |
| `Application/Inventory/CeraShopDomainService.cs:101–203` | wallet、gold、限制、扩容／名字牌／权益、物品与 receipt 读取依次发生，没有自 Rollback。 |
| `Application/Commands/Pipeline/SelectedSessionExecutionBehavior.cs:14`、`SelectedSessionSettlementBehavior.cs:16–36` | IAccount-only 路径不自动进入 selected scope；不能据普通 typed 规则推断实际围栏。 |
| `Application/Transactions/DomainTransaction.cs:132–173,231–260` | Prepare／唯一 fallible Commit 失败可确认回滚；非 fallible 违约在 durable 提交之后为 Uncertain；回滚附加失败保原因。Completion 是事实，不以 IsSettled 猜提交。 |
| `Persistence/Database/SqliteDatabase.cs:56–75`、`SqliteDatabaseParticipant.cs:127–210` | 同环境事务读能看见本次未提交写；真实 SQLite Commit 与 action／Prepare 可区分。 |
| `Application.Tests/Inventory/CeraShopPurchaseTests.cs:495–515,531–578` | 已有晚期宠物 SQL 回滚用例；CeraShopFixture 可传 configure、暴露 Host。其 Handle 手造 request 直 handler，不能用作本次 decoder 证明。 |
| `Application.Tests/Inventory/NpcShopPurchaseTests.cs:330–425,542–563` | ShopFixture 使用真实模块／SQLite／选中会话和真实注册发件箱；Host.Services、Connection、Session、Sender 和独占临时数据库均可读取。 |

首选一个 E 内的 `Task28.CeraOldBoundaryProbe.csproj` 和少量专用 `.cs`，作为独立 net10.0 Release 可执行项目，ProjectReference 当前 `Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj`，复用其现有编译 fixture 和 transitive 真实项目。显式设置 Nullable／ImplicitUsings／TreatWarningsAsErrors 等必要项目属性；不向原项目注入 Compile 项、不改包版本或引用图，不复制整个测试树。

fixture 是 internal，但实际有 public ctor `(bool useArchive, Action<IServiceCollection>? configure)`，Host 的 Services／Connection／Session 等也是 public member。可在探针内用精确 Assembly.GetType／Activator／PropertyInfo 窄适配现有 fixture；反射仅作测试装配和只读观察，不反射调用旧 handler、改私有状态、篡改事务／账本或构造替代执行器。先验证装配名、ctor／成员形状、依赖解析和一条成功 wire 请求，再展开故障。规划时未运行，若 ProjectReference 的构建资产或内部访问不成立，保存原诊断，在同 E 内做普通局部接法纠正；不能改原项目／暴露生产 API 来求通，无法维持真实路径时带最小缺口返主控。

购物车按当前 decoder 字段逐字节写 body：first／second／count／coupon、每 entry 的 mode／attribute／variant／product、avatar 和 selection 数量及字段、末尾 int／ushort；由注册表解析 `IGameMessageDecoder`，用实际 `GameMessageMetadata`、raw body 及原物理 Connection 解码，再从 fixture 同 provider 取 `INetworkMessageDispatcher.DispatchAsync`。不得调用 fixture.Handle、new CeraShopPurchaseRequest 充当入站证明、直接 fine 代替协议，或用 TestGameplay 再包生产 entry 造成自等。必要 setup 按原 TestGameplay／原 fixture 事务运行；每个独立故障使用新 fixture。

### 3. 观察器只记录，不替代事务或业务

在 fixture 的 configure 中装配窄 `ICeraShopDomainService` decorator，保留实际注册实例并逐参数委托真正 PurchaseAsync。记录入参、调用次数、当前 `IDomainTransactionAccessor.Current` 原对象、进入／正常 receipt／抛错时点和 `GameplayJob.Current`。既有 fine handler 可用窄委托 wrapper 观察其原结果／异常及装配 scoped 清理探针，不替换命令逻辑或管线。ILoggerProvider 保存真实 exception 引用、category／level／chain；不从文字重造 cause。

internal transaction 的 Completion／CommitFailure、selected scope 等用精确反射只读；缺失就报告具体观测缺口，不以 mock 值补齐。事务内通过真实 SqliteDatabase.ReadAsync 只读 delegate 查本连接 `PRAGMA foreign_keys`、临时 child 行和全列资产；它可用反射调用原 generic 读方法，但不借此写入或新开伪事务。额外标明该读取会复用／加入同一个原 SQLite participant，不增第二 fallible participant。进入、action 退出和协议结束分别记时，不把最后状态反推成 action 时状态。

保存同一运行内的完整生产表 schema／列名／类型、稳定主键排序和每列值；NULL 与 BLOB hex 单独编码，包含 accounts、characters／stats、items／slots／UID、creatures、premiums／restriction／capacity／条件及 domain_events／deliveries 等实际所有 user tables，而非只比余额／行数。fixture-only 注错表在 DDL 安装后单列入基线并要求失败回滚后无 child 遗留。不规范化同 run SQL 时间或 GUID，不使用 System.Byte[] 的 ToString 丢 BLOB；原前／暂存／最终快照均保存。

发送证据分清三层：adapter 调用／编码、outbox 提交条目、sender 实际处理。原 RecordedOutbound 的默认 Discard 只证明登记／提交，不能冒称真实物理关闭。为本 probe 可在同 provider 仅将 IOutboundSender 后端接到真实 `Networking.Outbound.OutboundSender`，外加记录转发器、观察者和 E 内的 in-memory `IOutboundTransport/IOutboundConnection`（或复用当前 XoTransport 这一无 provider 的对象）；内部 sender 的 ctor 可用窄反射装配。沿原 GameplayOutboxes 和原注册 adapter，只换证据侧接收端，不另建 ServiceProvider／outbox、不开 TCP Host。探针记录提交的真实 OutboundEntry、实际 SendAsync／CloseAsync 与 delivery report，等待原 sender 排空后判断；若改用了此后端，结束后读探针 recorder，不把旧 ShopSender 的 Discard 摘要当新后端输出。数据来自真实 sender＋受控内存 transport，不宣称 TCP／客户端实机。

至少用成功对照证明该 sender 真的收到实际编码帧，且传输未预先关闭。记录 exact physical source、session StandingOf／SelectedSessionSafety.IsBlocked、关闭登记和实际 CloseAsync 次数／顺序／chain。无关闭时也保这些零值、连接仍可用的对照；只缺 observer 不能把“没有记录到”写成“不关闭”。不改变真实 sender 的 options／超时／重试或 job 内等待网络。

### 4. 最小实际矩阵，分阶段回答三组问题

先一条 fixture 普通水商品成功对照，核真实 domain／SQLite Committed、余额／物品变化、实际 ACK／wallet／inventory 顺序和 frame bytes；再以不足余额的正常业务拒绝作对照，核最终拒绝码、无资产变化、实际事务是否空提交／回滚等按事实记录。必要时加一条 malformed body 验证 decoder 全包拒绝；不为此复制名字牌、扩容和权益的全部强矩阵，原覆盖仍在 TASK-27 全量库存中。

| 组 | 注错及必须区分的事实 | 结束／后继观察 |
|---|---|---|
| A：晚期 action SQL | 沿原强用例的水＋宠物购物车，水价格含 gold，`item_slots` 的宠物 `list_type=7` BEFORE INSERT trigger `RAISE(ABORT,...)`。真实 native 19/1811；在 domain decorator 的异常路径读到前面 wallet／gold／首 grant 已暂存，action 未返回 receipt，stack 不来自 SqliteTransaction.Commit。 | 原 cause 同对象经过 fine／handler 日志的事实；Completion 与全列资产回到 before；实际失败 ACK／帧及 no/yes fence／source close。去掉本 fixture trigger 后再送同一个合法 cart，核仅该次真实购买与成功序列。 |
| B：真实 fallible Commit | 只在 fixture 库加 `DEFERRABLE INITIALLY DEFERRED` 外键 child 表，以真实 grant INSERT trigger 写缺失 parent；复用 TASK-15 phase0 已接受方法，不复制其 Main／ledger 假设。必须先实际 domain 返回 receipt、真实连接 FK=1、child>0、资产／事件已暂存，随后 stack 确为 SqliteTransaction.Commit 的 native 19/787，并读取真实 ConfirmedRollback。 | 与 A 独立保 cause、ACK／异常／日志、全列 rollback 和会话／close。移除 trigger 后合法再买；禁止拿 controlled Prepare／普通 trigger ABORT／fake participant 冒充 native Commit。 |
| C：受控不确定／清理 | C1 在同真实 transaction Enlist 一个 `CommitCanFail=false` participant，只在其 Commit 抛固定 cause；真实 SQLite 先 commit，再观察 typed NonFallibleCommitContractViolation／Uncertain。C2 在 A 的实际 SQL 失败路径 Enlist 只在 Rollback 抛固定第二 cause 的 participant，使真实回滚链继续执行并产生 typed uncertainty；记录原 SQL 与第二 cause 引用、实际数据库结果。C3 仅在 fine 的真实 DI scope 解析一个 probe-owned IAsyncDisposable，使正常购买已提交后 DisposeAsync 抛固定 cause；这是独立 scope cleanup，不假称事务 rollback。 | 三个子例分别记录旧 dispatcher 是正常返回还是异常、失败 ACK／无 ACK、typed reason／全部 cause 引用、实际资产已提交／回滚、selected scope／participants、围栏、出站及 actual source close／chain。不得预设“旧一定 ACK／无 fence”，也不得把 C2 推广成所有 lease／scope cleanup。C1/C3 的故障后不自动重发购物车来冒充安全重试；使用新 fixture 隔离下一例。 |

C 的人工 participant／DisposeAsync 是明确的受控故障源，domain、真实持久化、旧管线和协议出口保持真实；不宣称生产中已发现同类实际故障。participant 的 Prepare／Commit／Rollback 调用顺序和 SQLite 的实际阶段都入观察。C2 的 rollback 故障应在真实回滚操作仍会执行的位置记录，不能为了制造“资产未回滚”中止真实 cleanup。C3 必须确实被 fine 的 scope 追踪并异步 Dispose，而不是在外层新 throw 或错误的 root scope 上注入。

本次 NonEffect：不查询／创建 Main，不做 EffectOperationLedger admission，不记录同 entry／Ordinal replay。A/B 去 trigger 后的相同合法 cart 是一次新的购买，不能宣称相同序号幂等；成功后再次购买本可再次收费。必要取消对照只用于分清入口取消与 body 取消，记录其实际触达阶段；未测的取消／stale／替换场景如实留空，不据一个预取消未入队例推导 fine 的取消行为。探针不模拟完整旧客户端／整帧拆包，证明从注册 decoder 开始的路径。

### 5. 原工具、执行和证据范围

写 probe 前用 `git ls-files -z` 保存完整 tracked NUL、各项 bytes／SHA 并证明 current＝HEAD；本 Task 此时是唯一自有 untracked，原 tracked owned=0，全部 tracked 是 nonowned。实际数量从 Git 取得，不凭上一个 Task 推算。显式证明两 canonical `B/verification/migration-ledger.json`／`.md` 都在原 map 中；不靠 C-quoted 文本路径漏中文文件。保存实际相关生产／fixture／管线／工具身份及 probe 源 freeze、已加载 assemblies 的 Location／MVID／SHA。

先以原 Release 构建方式构建新 E 项目，所引用原项目从当前 source 构建；记录 `dotnet build <probe.csproj> -c Release --no-incremental` 的实际 cwd、UTC、命令、环境、原 exit、warnings/errors 和资源。随后直接运行该已构建 probe DLL，每轮使用新的 output／log 前缀，原始 stdout／stderr 和诊断完整保留。native 用 `require_escalated` 规避 sandbox FSEvents hang；不 TERM 活跃 native 或父 runner，不更改 deadline／并行／Skip／原断言来求绿。普通编译／fixture纠正可在 E 内完成并保 raw；行为阶段未满足不能写通过或删掉该 case。

本 Task 不改变运行源、原 tests 或注册，无需八项目 fresh full，也不制造新 canonical capture 副本。9014＝8994 pass＋原 exact2 Dimension fail＋原18 Skip 是 TASK-27 的已接受库存，不是本轮新运行；新 probe 例数另列，绝不混入原 xUnit display 库存。可把 `DFO_GAMEPLAY_ASSERTIONS` 指向本轮 E 的独立目录，核探针无意外 off-executor／self-wait 等违规；这不是正式74/7核验，不套九类 expected 或宣称本轮有七 control。若出现违规，保 raw 定位，不放宽原 expected。没有源／ledger 变化时，原 strict validator 和原 selftest 可作一次只读复核，输出只写 E；不需要新造八物理坏副本。原工具不改，不覆盖 TASK-27 或更早证据。

### 6. 收敛、完整性与交接

E 中用一个简短 report 把每个结论分为：本次 actual native／actual decoder 证据、controlled failure 证据、仅静态依据、仍未知。每例关联原 wire body／metadata、真实 decode 类型／route、原事务对象和阶段快照、日志／cause 图、全列资产、实际 frames、围栏／关闭／链以及原命令；保留 JSON NULL／BLOB 和 original exception reference 的可检验对照，不以序列化文字相等冒同对象。明确 Cera NonEffect 及无实机／性能声明。

向主控交付三类裁定输入即可：A/B 的旧回滚响应和完成事实；C 旧异常／ACK／fence／durable／close 的真实差异；下一步需要的受影响 Plan HOW 门或 WHAT 问题及其最小具体证据。不要在 Result 预写新 API／whole catch 方案，不标 P5/B4 approved、不添加 VC、不授权放宽默认 Write/ReadOnly sticky、KnownStorage／guard／cleanup／typed 规则或 raw 出口。若关键探针无法保持真实接法，清楚列已得事实和无法证明的精确原因，返主控处理，不拿静态 catch 当动态结果。

结束时重算原完整 tracked 全部 current＝before＝HEAD，index 仍空；所有新路径仅在本 Task、E、task-28-*日志，Git NUL changed∪untracked 与 manifest 精确相等，ignored bin/obj 仍按原 Git 规则处理，不强制加入或清理其它工作。manifest 对各 whole-file bytes／SHA、自身排 hash、Task 精确 pre-Result prefix 明确分开，避免循环；保原行尾／BOM，不改原字节。对全部新 authored 文件显式查 trailing whitespace／space-before-tab／blank-at-EOF，不能靠 git diff 漏未跟踪文件；raw snapshot／stdout 原样保留并单列，不重写求整洁。

probe 全部自然结束、证据与局限收口后，在本 Task append Result（交付目标实际成立才写 `Outcome: done`），给主控完整 bytes／SHA、prefix／source freeze／manifest 身份，然后停止全部写入和 native。fresh required 独立快审由主控安排；审核和本地 checkpoint 后才能计 committed done。保持中文；不 stage／commit／remote，不自行开启下一 Task、修改 Spec／Plan 或宣告 Spec002 完成。

## Result R1

- Outcome: done
- Producer/identity: fresh `b_task28_execute`；HEAD `1aae55555ed207f3ced6593180fa8d211b60d09f`、原任务分支与唯一worktree未变。此为证据调查结果，不是 Cera 迁移或 Spec002 完成。Result review required，交主控 fresh 独立快审，不自批。
- Changed: 仅本 Task、`verification/evidence/task-28-inspection/` 新探针／观察／报告及 `verification/logs/task-28-*`。原15205 tracked全部before＝HEAD＝final字节相同，ownedTracked0，两actual canonical在完整NUL map中且不改；index空、原tracked diff空。没有产品／Server/tests／Plan／Spec／routing／其它历史证据修改，没有新协议或ledger行。
- Checks/接法: evidence-only项目直接ProjectReference原Application.Tests，以窄反射复用原CeraShopFixture；真实registered decoder(selector1/type0040)→原provider dispatcher→旧fine→原domain／SQLite／真实注册适配器／outbox。未手造request或调用fixture.Handle替代decoder，也未外包TestGameplay。IOutboundSender只换为记录转发器→production OutboundSender→受控内存transport；实际发送／Close计数与条目／frames／delivery report分别记录，不拿原Discard摘要当实际关闭，不声称TCP/A21。
- 实际观察（最终 `final-01/` 七例）：success原成功ACK／wallet／inventory、Committed、实际一批发送；不足余额正常拒绝实际Committed但资产不变、ACK11。A晚期宠物slot真实native19/1811，在domain异常前water、wallet/gold99900、物品和events已暂存，随后ConfirmedRollback、110表完整列／NULL／BLOB exact rollback、失败ACK1实际Delivered。B真实grant触发deferredFK，domain已receipt、FK1/child1，真正SqliteTransaction.Commit19/787；原transaction CommitFailure＝SqliteDatabaseParticipant／KnownStorage／FallibleCommit，cause与两条Error同对象，ConfirmedRollback、112表全部列exact rollback、失败ACK1实际Delivered。A/B去trigger后新合法购买分别只收费该次并返回两商品／单商品成功ACK；它们是NonEffect新购买，不是Main或同entry／Ordinal replay。
- 受控C组：C1非fallible Commit违约的SQLite资产已提交、typed NonFallibleCommitContractViolation／Uncertain；C2实际SQL1811＋controlled Rollback第二cause形成typed RollbackFailure，引用图保两因、实际资产回滚而CompletionUncertain；C3原fine真实DI scope的DisposeAsync在购买Committed后抛固定cause。三个旧出口均实际被handler转失败ACK1，dispatcher正常返回，selected scope实际null，无fence／close。全部七例Standing.Fenced=false／ConnectionRevoked=false、IsBlocked=false，close条目0／真实transport.CloseAsync0／精确close-chain空，真实sender均报Delivered；完整root chain、参与者／Tx同对象身份、异常引用ID图、帧bytes和前／暂存／结束快照在原JSON。
- 命令/轮次: `dotnet build <E/Task28.CeraOldBoundaryProbe.csproj> -c Release --no-incremental` 最终build04自然exit0、0warning/0error；`dotnet <E/bin/Release/net10.0/Dfo.Server.Networking.Tests.dll> <E/final-01> success refusal action commit nonfallible rollback scope` final01自然exit0，七例和A/B两条新购买随例核验。源freeze04 SHA `623b5e8fcd727aa34668aa7d81b6873aa380e174e2711302920571f855d3d7d5`。build01的probe logger同名CS0542、build02的快照nullable CS8619保原诊断及对应源，不算行为RED；build03/success01/matrix01已通过，之后只补只读Tx/来源字段并在freeze04重建重跑全部七例。所有native自然结束，无TERM或遗留进程。
- 其它核验: `verify.py`检查全部实际native阶段／cause／完整资产／wire ACK布局／outbound frames、真实参与者、同Tx身份、围栏／close和原加载assembly SHA；`verification.json`、`report.md`提供actual/controlled/static/unknown区分。探针采用既有friend程序集名Dfo.Server.Networking.Tests，只在E下构建，不改原friend清单或工程；其Location/MVID/SHA与production/原AppTests loaded copy均记录，明确不是正式Networking.Tests全量／七control。原strict＋原8selftest只读通过；独立DFO路径没有违规JSONL，只是probe观察，非74/7验收。Task27原9014库存只引用，本轮无八full／新增xUnit case／性能≤2秒或实机主张。
- Ownership/manifest: 完整15205 tracked逐blob封存且final逐SHA核一致；所有68新增路径仅自有范围，Git NUL untracked精确等manifest。`E/manifest-final.json`21161B SHA `dfb1dfd50d2ad029ae0402e3123b901bdd8aa4d7e51c0d3accbc52e01c9c6314`，68路径含manifest self-excluded一项；Task仅绑定pre-Result19761B SHA `052d86da14a6f0a5cc9d5663f7c347fe5fdeafb23715af475a818310c3db482a`避循环。其余每项whole-file bytes/SHA。原行尾不动，新增authored和最终生成JSON／Result另作显式whitespace核，raw日志／source-round快照原样保留。完整最终Task bytes/SHA在交接外部报告，不误用prefix冒完整身份。
- Remaining/裁定输入: A/B确认回滚响应保持需P4限定四opening之外的受影响Plan HOW门。C组已实际证明旧typed／已提交scope失败会转失败ACK且不围栏／关闭；后继若按新默认sticky／typed收口，可能涉及WHAT变化，必须返主控和受影响Spec/Plan门，不可默称纯搬迁。本Result不决定保留还是修正，不预定P5/B4、新VC／恢复API、raw许可或放宽guard／KnownStorage／cleanup／typed规则。未测取消／stale／替换／名字牌／扩容／权益，不把受控故障称生产incident；domain-enter快照可能提前加入原SQLite participant的观察影响已披露。
- 本轮停止: 写完Result及最终只读校验后，停止所有writer/native，等主控fresh required review。没有stage／commit／push／PR／merge／远端／版本／tag／部署／游戏／清理或Task29。

## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

- Reviewer：fresh independent `b_task28_review`，与 planner／producer／主控分离；应用 `reviewing-work` 的 Task Unit、Code 和 Deliverable 判据。范围仅为 TASK-28 contract r1／Result R1 的旧边界证据调查，不批准 Cera 迁移、任何 P5／B4／VC 或 Spec002 整体完成。
- 精确目标：review 前完整 Task 25639B，SHA `dccb4432e9c3db7f7877f65165e1d011c9d3b7004a3ecca8b97b70a325d58baa`；pre-Result prefix 19761B，SHA `052d86da14a6f0a5cc9d5663f7c347fe5fdeafb23715af475a818310c3db482a`。HEAD `1aae55555ed207f3ced6593180fa8d211b60d09f`；freeze04 SHA `623b5e8fcd727aa34668aa7d81b6873aa380e174e2711302920571f855d3d7d5`；manifest-final 21161B，SHA `dfb1dfd50d2ad029ae0402e3123b901bdd8aa4d7e51c0d3accbc52e01c9c6314`。本 review 只在该完整目标前缀之后追加。
- 已读 current Task／Result、适用 AGENTS、Spec B3 RQ-003/006～008、AC-003～005及VC边界、Plan P4 commit-result四opening限制和下一边界，实际旧Cera handler／fine／decoder／领域／事务／pipeline／fixture，以及完整探针 Program、Observation、Delivery、project、run/verify脚本、报告与正式原日志／JSON。没有未解决阻塞发现。

### 实际接法与事实核验

1. 探针的 ProjectReference 复用当前 Application.Tests；窄反射构造原 CeraShopFixture／读取 Host，不调用其手造request的 Handle。实际 body 经注册 selector1/type0x0040 decoder，再到同 provider dispatcher、旧 fine、原 ICeraShopDomainService 与 SQLite。decorator／fine wrapper逐参数委托原 descriptor 创建的实现，仅观察和注入具名受控故障，没有替换业务或事务。原 Cera fine 的 IAccount-only 声明、SelectedSessionExecutionBehavior 的直接 next、handler 非取消 catch 与动态 selectedScope=null 相符；C3 的 scoped IAsyncDisposable 确实由 fine 自己的 CreateAsyncScope/await-using 路径清理。
2. 实际出站沿原 adapter/outbox，经记录转发器进入 production OutboundSender 和受控内存 transport。独立核全部七例提交条目的 frames 逐 bytes 等于 transport 实际接收，所有报告 Delivered，SendAsync 均在 executor 外；成功对照实际为 ACK→wallet→inventory。各例 close条目0、实际CloseAsync0、connection-failure通告0、精确source close-chain空，Standing.Fenced/ConnectionRevoked/IsBlocked均false；不是从缺失observer推断no-close。probe使用既有friend名称，DLL实际位于E/bin；其身份与原正式Networking.Tests严格区分，九个原加载程序集的实际copy SHA与当前对应Release output一致。没有第二provider、替代outbox或TCP/A21主张。
3. A的真实native19/1811在action／宠物slot INSERT失败：domain-throw时wallet、gold99900、前grant和事件已暂存，action没有返回receipt；随后ConfirmedRollback。B的domain已receipt、真实连接FK1/child1，native19/787 stack确含SqliteTransaction.Commit；CommitFailure原source为SqliteDatabaseParticipant、KnownStorage/FallibleCommit，cause ID与原两条Error及participant.LastFailure一致。A的110表和B含两probe表的112表，schema／完整列／值／NULL／BLOB均 before=after；无同run时间或GUID规范化。去trigger后经decoder的合法新购买收到对应成功ACK，余额／gold符合只收费该次；NonEffect不冒Main／ledger replay或幂等。
4. C1真实SQLite已提交后受控nonfallible Commit违约，CompletionUncertain，typed原因Inner保固定cause；C2真实SQL加受控Rollback第二cause，typed RollbackFailure→Aggregate保两原引用，实际全列回滚但CompletionUncertain；C3在原fine handler return后scope DisposeAsync抛固定cause，资产Committed。三者均实际由旧handler转失败ACK1且dispatcher正常返回／无fence／close。该结论由原JSON、cause图、frame布局和真实sender报告共同支持，报告没有把受控故障称为生产incident，也没有从C2实际回滚推广成一般可重试。
5. 独立stdout执行 `verify.py` 唯一输出write之前的完整只读前缀，七例阶段／cause／事务同对象身份／全部资产／ACK布局／outbound bytes／chain／loadedSHA／freeze／metadata断言通过；另直接检查各例connectionFailures为空、实际参与者只有一个fallible SQLite、C组controlled participant为nonfallible。domain-enter读取可能提前enlist原SQLite的观察影响已披露，不隐去参与者顺序变化。build04原命令为Release --no-incremental、自然exit0且0warning/0error；final01原命令及stdout确含七例、自然exit0。前两轮探针编译诊断和前三轮源快照均保留，未当行为RED；不需要为这些只读观察再运行native。

### 完整性与范围

独立核15205原tracked NUL集合与before完整相同，并逐文件以Git blob格式SHA1对照实际HEAD对象、再核bytes/SHA256=before；ownedTracked0，index与tracked diff空，两canonical在原map中。manifest实际68路径含self-excluded一项，其余67个whole/prefix hash逐项复算通过，路径精确等Git untracked。freeze04当前探针源全部吻合。独立检查41个authored路径无trailing/space-before-tab/blank-at-EOF问题；24项rawVerbatim保持manifest原bytes，Task原25639B前缀不改。原 strict validator和原八selftest只读重执行，结果／拒绝原因与ledger-readonly.json精确相等。

本轮无产品／原测试／canonical／Spec／Plan改动，Task27的9014库存明确只是引用；probe零DFO记录不冒74/7或七control。未测取消、stale／替换、名字牌、扩容、权益及其它购物车组合的局限明确。A/B为后继Plan HOW提供实际依据，C组为主控受影响WHAT裁定提供事实；这些问题已正确交主控，不在本review中选择保留或修正策略，也不授权扩大恢复、raw许可或清sticky。批准仅指本有界调查目标和证据成立，不是后续方案批准。reviewer未运行native、未改Result／产品／其它证据、未stage/commit；此追加后停止写入。

## Controller Consumption R1

主控已完整读 Task r1、Result R1、fresh Independent Quick Review R1；producer/reviewer 均停止 writer/native。review approved，无未解决阻塞。目标原 Result 前缀25639B SHA `dccb4432e9c3db7f7877f65165e1d011c9d3b7004a3ecca8b97b70a325d58baa`，消费前含review Task31738B SHA `db950af022c34f86d73caba2f436df233888bdbe23f671ce4e0246df05a159c0`，本段仅追加，不改其独立判断。

主控实际读 Program／Observation／Delivery／project／verify脚本、report及Result，并stdout执行verify.py唯一write之前的完整只读前缀：七例真实decoder／cause图／同Tx／全部资产／native阶段与KnownStorage／ACK布局／实际sender bytes／transport／chain／assembly SHA／freeze／metadata断言通过。另独立复算68路径manifest（含self-excluded；其余67 whole/prefix hash）、Git NUL untracked精确集合及15205原tracked当前SHA＝before；两canonical在完整map、index/原tracked diff为空。freeze04为 `623b5e8fcd727aa34668aa7d81b6873aa380e174e2711302920571f855d3d7d5`，manifest SHA `dfb1dfd50d2ad029ae0402e3123b901bdd8aa4d7e51c0d3accbc52e01c9c6314`。原HEAD blob逐bytes、原strict/eight-selftest、全部新增authored whitespace由独立快审核实；主控不重跑native或写原证据。只允许这68个路径的本地检查点，提交前后核父提交／精确路径／clean／footer；committed Outcome才计done。

### 对后继边界的主控裁定（待受影响gate，不是方案批准）

A/B 已实际证明原普通action SQL与真实fallible Commit确认回滚后能发送原拒绝ACK，后继应保此业务行为；扩展具名commit-result消费仍须P4之后的受影响Plan HOW门。C1/C2/C3实际证明旧IAccount-only路径把typed不确定和已提交fine scope清理故障也转普通拒绝ACK。为保持既有显式CMD的sticky／typed失败不变量，主控选择后继Cera不恢复这些故障，沿现有最终失败收口而不另加通用catch恢复、清sticky、raw许可或新执行器／outbox机制。

这是相对旧Cera的有意可见差异，不能伪称纯搬迁。下一步先由fresh Spec producer编写受影响B4及amendment，明确只限Cera：保成功／正常拒绝／已确认回滚原响应；typed不确定、guard/额外独立cleanup等默认保护不能转成StorageFailureACK并成功结束，原cause和提交事实保留，来源关闭／链与已登记出站顺序沿现有最终收口。不得预写“没有任何帧”而漏掉scope结束前可能已登记的消息，不为该调查擅自扩大精确参与者集合或承诺新增会话fence。实际参与者与原工作点由后继Plan／迁移Task对照并动态证明。

B4须fresh challenge及另一独立Spec review；approved之后才另作受影响Plan P5、fresh challenge/review，主控消费并amendment-only本地检查点后，才详细规划并实施Cera迁移。现有Spec B3／Plan P4、三raw出口、默认Write/ReadOnly／KnownStorage与cleanup边界、已接受Task、根/子Contract、baseline和A封存不在本Task修改。契约允许经审查Spec逐条列明有意变化，保原授权和auto门，无新增用户HITL；任何超出该授权或核心不变量的实际冲突仍返主控处理。此处裁定不等于B4/P5已通过，也不创建Task29。

本单元只交付旧边界证据。Task27的9014库存只是已接受引用，没有本轮八full、七control、性能或实机主张；中文、不push/PR/merge/远端/版本/tag/部署/cleanup及Astra后用户明确收尾限制继续有效。
