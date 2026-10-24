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
