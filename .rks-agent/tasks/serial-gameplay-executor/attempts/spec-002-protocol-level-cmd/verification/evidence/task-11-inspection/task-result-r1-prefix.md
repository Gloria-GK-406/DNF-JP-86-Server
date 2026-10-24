# TASK-11 — 首显式效果 admission／Main 与完整 UseStackable 协议 CMD

## Task Unit Contract

- Revision: 4（本契约及详细计划的同一修订）
- Outcome and references: 交付 [Plan P2](../plan.md) T-004 的首个完整效果协议有用单元：新旧唯一 ledger 的纯 admission、显式 Selected／Rejected 与 MainSettlement runner、B2 两个具名即时编码登记出口及最终失败收口，加上完整 GameUseStackable CMD；删除旧 UseStackable fine 命令／处理器，新路径不经 legacy ambient／桥／nested bus。对应 [Spec B2](../spec.md) RQ-001/003～009/012/014/015、AC-001～005/007～009，以及 F-34/F-88/F-93/F-112；仅覆盖本协议和共享基础，不宣称所有效果或六处 ambient 已清零。
- Owned boundary: 下文精确生产／测试路径、必要同职责新文件、本 Task 和 task-11 前缀证据、开放 migration-ledger 的已证行及 metadata。Spec B2／P2／契约／root／sealed A／baseline／original／旧 Task 与旧证据只读；不迁 BuyItem 双 CLR 家族、SharedEffect 或其它完整协议，不启用 Compose／延迟编码，不更改执行器／事务算法、账本身份及窗口、业务规则／协议字段／字节。
- Required checks: 同 provider 真实 ledger／SQLite／outbox 贯通、唯一 Main／普通 selected 拒绝提交／无 selected 仅 ACK、真 rollback 与 durable commit、旧序号与窗口／当前投影重放、显式足迹及结束续体／singleton 隔离、真实 encoder 出处与单 cause／额外错误最终分类、全部参与者围栏及 source close、实际旧驱动迁移；一个有价值可编译 mutation 红绿并恢复。共享核心采用完整 Release --no-incremental build＋八项目 full、原两条准确内容失败／18skip、七程序集原74 expected DFO、Golden15／Characterization10、capture 前后／N-D／精确闭账／strict 八坏副本，方法见下文。
- Dependencies: TASK-10 r5／Result R1／required 独立 approved，产品检查点 `2d8081ae2a3ef687e88ec284df9e97588cc625a7`；TASK-09 已验 recorder／legacy admission，TASK-04 显式入口／runner／sticky 与默认 postcommit 保护；B2/P2 fresh challenge 及独立 approved（[Spec review](../reviews/spec-b2-review.md)、[Plan review](../reviews/plan-p2-review.md)）、两 amendment 和四 routing override，amendment-only 检查点 `30a17361233482aac05aea219d7374a2dbd7cb24`。后者 Server tree 与 TASK-10 相同，不把文档批准计作实现通过。
- Result review: required — 唯一账本、结算状态和共享失败收口错误会导致重复扣物／已提交误回滚／误围栏／漏围栏，单纯编译和 Golden 不足以发现；后续全部效果协议消费此基础。独立结果快审核真实来源、cause 引用、全部参与者、旧断言及证据同版本后由主控检查点。Task-plan 无新增独立门，由主控 readiness 后安排执行。

## Detailed Plan

### 1. 起点、事实与执行边界

开工由主控核当前分支 `task/refactor-session-20261021`、工作区 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`、上述精确 HEAD 和唯一 owned 变更；重新确认 B2/P2 gate 仍匹配。阅读本地 AGENTS、ArchitecturePhilosophy、RefactorPreferences 与 TASK-10 r5/R1；无 `.codegraph/` 则 rg 优先。原 TASK-10 正式02：8523＝8503pass＋2准确原 Dimension fail＋18skip；App4769、Protocol1762、Net199、Host203、Http57、Infra976、Persistence477、Pvf80。这是数量起点，不是本 Task 实跑。

当前真实 API：`IEffectFootprintRecorder` 含 DemandEffects／RecordEffects(EffectFootprintOwner)、DemandActive／RecordInventorySlots／RecordRentalState；EffectExecutionContext 必需 Footprint。EffectOperationContext 的 private static RecordEffects(Frame?, applied) 是完整无 current 查找映射，可提纯复用；LegacyEffectFootprint 与临时 Lifecycle factory 不是新 CMD 的来源。Request 两个 effect 变体仍 private 空壳，registry／bus 拒 Effect，runner 拒 Main。旧 ledger 一张 CWT 按物理 connection、精确 session、sequence、raw hash/length/selector/type 工作。

两项不得漂移的旧事实：UseStackableResponseEncoder 只 OnePayload ACK，不因 CreatureChange 添加宠物附帧；ReadReplayStateCommand 在 ApplicationModule 原788～789注册 Transactional，新 replay 必须 ordinary Write 后事务外 MirrorMerge→Create→batch→Town。原 UseStackableHandler Resolve 后 SendExplicit 没有 catch；两条原 replay IOException 来自 RecordedOutbound 的真实登记 encoder，不是 physical transport。B2/P2 已就其兼容边界批准，不另创 IOException 类型豁免。

继承中文／本地实施与核验授权，主控负责本地检查点。禁止 push／PR／merge／远端 issue／部署／cleanup，不运行 df_game_r／DNF.exe；不改版本或启动游戏实机。不创建 TASK-12，不把本 Task 扩成其它139 handler 扫迁。若真实新来源或共享决定超 P2，先给主控拥有源码、实际调用和最小受影响边界，不能用宽默认／测试特权补洞。

### 2. 生产白名单与新文件归属

以下均相对 worktree；新文件只能承担表中具名职责，先列入 preflight／source manifest。机械 caller 闭包也要有实际符号命中，越表先交主控，不用全目录授权。

| 归属 | 可写路径／职责 |
|---|---|
| Application 命令机制 | `Server/src/Dfo.Server.Application/Commands/CommandBus.cs`、`CommandRequestContext.cs`、`ICommandWorkUnits.cs`、`CommandHandlerRegistry.cs`、`CommandDispatchingExtensions.cs`；必要新增同目录 `CommandRegistrationAttempt.cs`（许可枚举、内部 sealed 实现／本请求失败发生记录），不新 DI current／bus 入口 |
| Application 事务边界 | `Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs`；保现 DomainTransactionScope／Lease 明确 completion，不改其事务算法 |
| Application 效果真源 | `Server/src/Dfo.Server.Application/Effects/EffectOperationLedger.cs`、`EffectOperationContext.cs`、`ReplayStateDomainService.cs`；新增 `ExplicitEffectRequest.cs`／`EffectFootprintProjection.cs` 等同职责具名文件承载纯 admission／显式 accumulator／recorder，唯一 CWT 仍 ledger 持有，不另造共享状态注册表 |
| Application 实际登记 | `Server/src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs`，仅新增显式 attempt overload／内部验证及 encoder 窄 catch；不改 GameplayOutbox／GameplayExecutor／transport 算法或默认 Required／BestEffort |
| Abstractions 接缝 | `Server/src/Dfo.Server.Abstractions/Effects/IReplayStateProjection.cs`：reader 必需显式 metadata、无 bus 的投影 sink；`Networking/ICommandMessageOutbox.cs`：两个具名 overload 和不透明 attempt 参数。若拆新契约文件限同目录同职责；不引用 Application、不把 RequestContext 下沉 |
| Protocol 适配及用例 | `Server/src/Dfo.Server.Protocol/Game/IGameCommandOutbox.cs`、`Game/ReplayStateProjectionDelivery.cs`（纯 sink／Create）、同层新 `LegacyReplayStateProjectionDelivery.cs`（仅旧 route bus 适配）、`Game/UseStackable/UseStackableSlice.cs` 及同目录新 `GameUseStackableCommand.cs`（整协议）；`ProtocolModule.cs`、必要 ApplicationModule 接线。纯拒绝映射可同切片拆 helper；旧 UseStackableCommand／Handler 必须删净 |
| 旧 replay 必需 caller | 同职责 `ReadReplayStateCommand/Handler` 与 ledger 旧 Dispatch；JoustSlice 只有若显式 legacy metadata 签名所需的 Refresh 调用改参，保持旧业务／路由／交易点。不允许借此迁 Joust、改 marker 或即时投影业务 |

`SelectedSessionSafety.Capture(session, metadata)` 已有，优先使用；本 Task 不需更改其策略。新 reader／sink 内部也不调用单参 ambient 重载。新 handler 构造器只 ICommandBus；新 whole CMD 仅领域服务、TimeProvider、槽位换算、ICommandWorkUnits、IGameCommandOutbox、日志，按既有封闭判据；context／recorder／attempt 都是方法输入。不向新 CMD 注入 SelectedSessionSafety、ledger、reader adapter 工厂、sender、locator／queue。

### 3. 唯一 ledger 的显式 admission 与 Main 生命周期

先抽内部 `Admit(connection, IEffectSettlementRequest)` 返回封闭结果 RejectedNoSelection／SelectedNewOrRollbackRetry／CommittedReplay（携带原 entry、显式 recipient、minimum）。它不调用 handler、bus、delivery、Enter 或任何 legacy ambient；Selected capture 显式 request.Metadata 并核 ReferenceEquals 物理 connection。旧 Dispatch 用同一结果再按旧路径 Enter／Refresh；新 ExplicitAsync 消费结果直接建 Request 或框架 replay。ledger 不在 CommandBus 构造器注入：在已有 job scope 中解析，避开 ledger→LegacyReplay→ICommandBus→ledger DI 环；不新增 Provider、Lazy／Func 环兜底。

保持实际算法逐项：ordinal>0，full body 0..1048576，64hex；无 session 分支在 ledger 序号登记之前，保持不消耗 ordinal；selected 当前性在 LatestOrdinal 推进前；必须严格递增；清除 `ordinal-entry.Ordinal>=128`；fresh覆盖原(session,sequence)；nonfresh原 selector/type/hash/len 全等。ConfirmedRollback 只 State=Pending、Projection=null，**不更新只读 entry.Ordinal**；Committed 仅当前快照重放，Pending／Rejected／Blocked 拒绝。FinalizePending 在所有已启动单元 drain 完、结算事实确定后仅 Pending→Rejected，不能在未 await commit 尚运行时抢先拒绝，也不把失败当回滚证据。

`ProtocolEffectSelected` 建于 bus job 开始，持 dispatch/message/frame、捕获 authority、精确 recipient、admission／entry 与本次显式 state；Rejected 同持捕获权威及初始无 selection 事实，无可升级选中能力。新路径绝不 Enter NetworkCommandAuthorization／EffectOperationContext 或捕获 legacy／Lifecycle source。非 effect／TrustedInternal 现有合法变体继续工作；错误变体不落到 NonRequest。

将 needs／slots／areas 的 accumulator 及 TASK-10 完整 applied-effect switch 提纯为无 ambient 的实例／纯 helper：legacy Frame 与新 selected state 各持自己的 accumulator，旧最低足迹、所有 effect family／slot／town 规则保持。不共用本次可变 accumulator，不把它注入 singleton。新 `ExplicitEffectFootprint` 实现现接口，只读本次 request/state/recipient；Demand* 先核同 job、healthy、未 closed／sticky、authority/current、唯一 Main active、TransactionStarted、未 Freeze、owner 精确一致，连空 slots/effects 和 rental 也不能绕过。领域仍使用 TASK-10 的无副作用 Demand，在真正写前校验；不能只末尾 Record 拒绝。commit callback 读普通 context facts 不要求已关闭 recorder 再获活性。

runner 在 Write(MainSettlement) reserve 唯一 main claim，永不在本 request 内释放重试；ReadOnly(Main)／Rejected／NonEffect／Internal Main 在任何业务前拒绝。声明 participant 精确去重、报告 recipient 必须包含、报告者账号／session 和 selected 一致，逐个 DemandCurrent；其他参与者可属别的账号，不能把全部强改同账号。事务 Begin 后 TransactionStarted，再运行原业务；向 action 的 CommandWorkUnit 提供仅 Main 可取的必需 Footprint（非 Main getter 拒绝）。返回后复验 request 与所有 participants，真 rollback-only 才 Dispose／确认 rollback，否则 Freeze(validate projection)→Commit→明确 Committed→Settled，保旧顺序。已提交但后续失败不得 ConfirmedRollback；事务不确定保 typed、Blocked 和全参与者。claim 重复、缺最低投影、未完成事务等程序错误 sticky。

区分 lease completion、rollback confirmed、projection Frozen、ledger Settled；不用 IsSettled 推已提交。保 ordinary Write／ReadOnly／RequireRollback、两单元第二回滚和 pending outbox 隔离。请求 Close、Main action/unit 结束后 recorder 不再写；结束续体不能获取新 Main／NonRequest。旧45/58/60不全删，legacy 使用原职责且同 pure admission／映射。

### 4. Replay reader／sink 与唯一旧适配

reader 最小签名定为 `ReadAsync(SelectedSessionIdentity session, ReplayProjectionRequest request, GameMessageMetadata? metadata, CancellationToken ct)`，metadata **必需参数**，null 仅是明确来源决定，不留旧默认重载。内部 `safety.Capture(session, metadata)`，保原 transaction requirement、owner/storage/current checks及快照内容；不得外层显式、内部又单参 Capture。旧 ReadReplayStateCommand 可临时增加显式 metadata 字段，其 handler 原样透传；旧 factory／LegacyRefresh 必须在入口决定原 metadata/null，不能 reader 自补 ambient。

新增 Abstractions `IReplayStateProjectionSink.ProjectAsync(NetworkConnectionContext originalConnection, SelectedSessionIdentity session, GameMessageMetadata? metadata, ReplayStateSnapshot snapshot, ICommandRegistrationAttempt? attempt, CancellationToken ct)`；metadata 与 attempt 均无默认值，legacy 显式 null attempt。Protocol 实现无 ICommandBus，按原 MirrorMerge→Create→CurrentStateBatch→Town 顺序；batch 用 typed IGameCommandOutbox 并显式透传 attempt。Create 的纯帧算法／DTO／bytes不变，仍可保类上原 static Create 避免无意义所有测试改名。

sink 不加 locator：显式注入 IMirrorAradQuestDomainService、IOnlineCharacterRuntimeDomainService、ITownAreaDomainService、ISelectedCharacterCurrentViewDomainService 和 TownAreaProjectionDelivery 等已有实际能力；按原 flag 使用，optional mirror 的缺席语义保持（仅组合必须时才调整测试装配）。TownAreaProjectionDelivery／PartyTownProjectionDelivery 无 nested bus，可保原方法及原参数，任何额外隐藏依赖先读实际调用，不把纯 Create 等同全 Town 行为。新 CMD 不注入 sink，框架 bus 的 replay 分支在 scope 解析 reader/workUnits/sink，ordinary **Write** 内 direct ReadAsync，participants 包含原 recipient；提交结束后才调用 sink，不启 Main、不调用原用例、不重 ACK。

`IReplayStateProjectionDelivery.RefreshAsync` 仅作为具名临时 LegacyReplay 接口保留：当前生产上游 ledger旧 Dispatch 和 JoustBetHandler，两者下游合计一处旧 ReadReplayStateCommand dispatch。实现只把旧精确输入转成 ReadCMD→无bus sink，显式传 metadata/null；旧 reader CMD 外部测试仍可运行，所有 caller 若签名变化原子改。新 bus/CMD/sink不经此入口。不按“旧三个 replay command”误计。ledger记录其真实上游层级、依赖和最后消费者删除要求，最迟 T-011 清零；本 Task 不闭掉仍旧route可达的 adapter。

### 5. B2 两个具名登记许可及最终一次失败裁决

在 CommandHandlerRegistration 添加冻结的具名机制许可集合，枚举仅 `SettledRequiredExplicit`、`CommittedReplayCurrentStateBatch`，验证仅相容 Protocol effect 注册可声明。ProtocolModule 为 GameUseStackable 注册这两项；Application 不识别 UseStackable CLR、异常类型或文字。普通注册无许可、普通 Register API 默认保持 Task04 规则；新增出口不能只照抄旗标就获本 Task 授权。

最小 API：Abstractions `ICommandRegistrationAttempt` 仅不透明 token，无报告错误／签发／回调方法；Application 内部 sealed `CommandRegistrationAttempt`，私有／internal 构造及 encoder failure 记账。Request 提供受检的 `BeginRegistrationAttempt(exit, exactConnection)`（或同义具名两方法）：核当前 job／healthy／未closed、注册许可、对应真实 Main 已Settled 或本次 replay Write 已提交且结束、物理目标与原 recipient 同引用；绑定 request、job、阶段、目标及一次 call identity。不是永久 exemption，不是 CMD DI 能力。显式状态还需记录 attempt 已签发／已进入 adapter／已完成，未完成 attempt 不能成功结束或开后续工作单元，防 callback 在 adapter 前抛错被 catch 后继续；不要求 caller 自报错误来恢复该状态。

窄 overload 为 `ICommandMessageOutbox.RegisterBatchAsync(exactConnection,messages,attempt,ct)`、`RegisterExplicitAsync(connectionId,delivery,attempt,ct)`，Game 同形 typed overload；原三方法保默认。Game adapter 只类型转换／透传，不签发／设置证明。Application adapter 先检查内部 sealed token 和此次 method/target/job/阶段／one-call 状态；explicit 查得 direct 时还必须 ReferenceEquals handle 绑定的物理 connection，ID相同的替代目标无资格。无encoder／gone／空batch仍按原丢弃规则完成 attempt，不伪造 encoder provenance。

实现 actual encoding 的最小内部 helper，将 **仅** `encoder.EncodeMessage`／`encoder.EncodeBatch` 单次调用放在窄 try/catch，内部记录原 error 对象并 `throw;`；explicit EncodeEach 的 MemoryStream.Write／ToArray、validation、ct、lookup、outboxes.Current、buffer、outbox.Register 等均在证明范围外。包整个 EncodeEach 会误把 buffer 错标成 encoder，禁止。Required direct 的实际 encoder 可以签发证明；BestEffort 继续原逐目标 Warning／skip，不能扩资格。actual adapter 其它异常仍让本次 request sticky（无编码凭据）；取 attempt 后 Game转换／wrapper callback失败留下未完成 attempt也不能变成功，不把 callback 运行位置／inFlight当编码证据。

真实 encoder 抛出时立即 sticky，业务 catch 后 DemandCurrent／Footprint／新 unit／attempt／DemandFinished 都拒，不能继续业务写或报告成功。opaque 跨request/job/目标/closed复用拒绝；普通方法不因曾发生过许可encoder错误而成为忽略区。仅 request 内记录发生阶段与传播来源；无全局 tracker／ambient／jobslot／任意Func装饰框架。若选择不同同义 API，必须保上述无自报／目标／阶段／未完成／sticky约束。

修改现 bus 两次 FailAsync：body catch 先记录失败，不立即 typed包装／fence；scope仍存活时 drain **所有** 已启动含未await unit，哪怕 body正常返回也先核未完成并 drain；保持原 Close 请求→Dispose scope 生命周期，并分别记录 dispose／unit cleanup 独立发生；最后一次 FinalizeFailure/FailAsync 裁决和逐人围栏，再向 executor 原样抛最终cause。drain不可Close后再借请求权威继续新结算；已在运行的 unit受sticky检查回滚，完成/abort facts归结算机制。typed事务未知事实出现即保留，不能清理后降成raw。

失败聚合需保存 occurrence 的独立阶段／所属已追踪 operation，而不只现 Includes 引用去重：同一次 fault沿 action→unit task→body→drain传播可识别为同一发生；独立 scope.Dispose／lease.Dispose／第二 unit即使抛 **同一个 Exception 对象** 仍记第二发生，raw资格撤销。原cause作为Inner/aggregate成员引用保留；不要为“单 cause”吞 cleanup 错。不得用每次catch都计新错而误拒单纯传播，也不得ReferenceEquals吞独立故障。

最终仅当具名许可＋本次真实 encoder proof、entry已Committed、最终单一原cause引用、无 typed／额外独立错误，保 raw对象、Committed、无新增 participant fence；source close仍由原executor/outbox完成，已提交条目不撤回。其他提交后业务 IOException、非许可encoder、registration guard／lookup／callback、Mirror/Create/Town、typed／unknown／额外独立error保持默认 PostCommitPublicationFailure／aggregate及全 committed/uncertain participants fence。Fence自身错误继续收集，不能反向获raw。此处实现 B2“按既有规则”，不保证已关真实socket可再发包。

### 6. 完整 UseStackable 搬迁

新 `GameUseStackableCommand(Dispatch, UseStackableRequest)` 实现 IProtocolCommand，Frame 用原 request.Metadata，处理器整合旧 network handler 与 fine handler。可仍返回 ItemUseResult 供原业务结果测试，但整协议执行及登记必须发生，不留细粒度wrapper／转发。薄 UseStackableHandler 只 bus.SendAsync并等待；ProtocolModule 改该唯一 message route，删旧 fine 注册和 UseStackableCommand／UseStackableCommandHandler 类型。

保持 slot顺序：协议 GameSlot→BusinessSlot→canonical response GameSlot，然后原 FindByConnection；selected结果须和初始 request.Recipient 精确相符。Rejected无初始selection即使后来有selection也只能原 Warning＋Silent ACK，不开始 Main、不写效果。selected 的 IsInWorld／可用 carried或CreatureConsumable／container bound／Get／ItemCode一致／effects判定 **全留在原主事务内**，普通拒绝也走最低 Inventory Freeze→Commit→Settled；不因前置只读移到Main外造成PendingRejected。结果 ItemUseStatus／StatusFor／ErrorCodeFor 原每个分支、slot disagreement 当前绝对状态、pet change字段、clock、cooldown及实际业务不改。

Main声明仅该精确recipient，调用 ItemUseEffect.UseAsync(session,businessSlot,item,unit.Footprint,ct)，沿TASK10完整 effects链。保业务 read/write前复验、剩余槽读取与原receipt／XP值；不能偷用 ctx.LegacyFootprint／Lifecycle factory／TestInternal。主段后按原信息日志与拒绝日志，不重复Error。

事务外组原一份 RequiredExplicit：ACK→OpeningReceipt中VirtualResource更新→源槽绝对update/clear→可选ExperienceNotification；award/points必须同时存在，否则仍程序错误默认fence；不添加 Creature附帧。仅原response/消息组装成功、真实Main Settled后签发 SettledRequiredExplicit attempt再即时登记；无session ACK走普通登记无该许可。允许用纯切片 helper共享拒绝映射／组帧，但不把bus编排藏进去，不新增跨切片业务门面。完整 CMD 内没有 catch吞登记失败。

### 7. 测试与工具 caller 精确白名单

以下列的是实际符号／驱动目的，不要求每文件都机械改。保 Fact/Theory实际case及所有原业务/SQL/bytes/metadata/期限断言；仅旧类型／接口变化的编译闭包不是行为 RED。新增测试文件限 `Commands/ExplicitEffectCommandTests.cs`、`Sessions/ExplicitEffectRegistrationTests.cs`（App，或将同职责用例放既有相应文件），避免平行镜像测试平台。

| 路径（Server/tests 下） | 实际受影响处／允许动作 |
|---|---|
| `Dfo.Server.Application.Tests/Game/UseStackableHandlerTests.cs` | 大量TestGameplay内直接Handler＋DirectCommandBus(use)＋直接fine SpiritCommand。按实际目的改一个局部 fixture：保持原fake领域对象，单provider装配真实bus／Main／ledger／RecordedOutbound；setup/mutation在明确job，协议执行在executor外真实dispatcher或整CMD，使用完整fresh metadata。结果／帧断言原样；不在TestGameplay内调新协议bus，不造inline后门 |
| `.../Inventory/ItemUseFixtures.cs` | DirectCommandBus 只删 Use fine分支／use参数；保MoveItemSpace与Tutorial旧测试driver。允许局部测试helper建立完整 Use入站并返回wholeCMD的ItemUseResult或原观察结果；共享fake内容／clock／container不扩授权 |
| `.../Inventory/HerasEntryCouponTests.cs`、`ReviveCoinTests.cs`、`ExperienceItemUseTests.cs`、`MarriageCapsuleItemUseTests.cs` | 实际外部Use fine bus改完整入站／整CMD，保status／coupon/bonus/quota／revive／exp/consume／cap/rollback SQL断言；Experience既有真实dispatcher replay不改语义；Revive现真实入站可原留。新wholeCMD返回ItemUseResult可直接保结果断言，无需从ACK反推模糊状态 |
| `.../World/IllusionTowerDomainServiceTests.cs` | 原1496附近仅Use loop：当前同TestGameplay内manual auth／entry/directhandler，改setup job→外部真实fresh入站→观察job；原三次QuickSlot/2frames/byte104/剩余槽与tower恢复断言不改。其它两千行不泛改；原ReadReplayStateCommand签名必要改参 |
| `.../World/CommonExperienceEffectTests.cs`、`.../Inventory/CharacterContainerDomainServiceTests.cs` | 前者XP写入注册名单旧fine由新wholeCMD接替；后者原reflection container依赖检查改到真正业务处理器。不能简单删掉Use保护行 |
| `.../Inventory/InventorySlotBoundaryArchitectureTests.cs` | 原AssertSlotProperty<UseStackableCommand>/AssertNoGameAddress迁为新协议输入仍GameSlot、实际领域调用BusinessSlot的明确边界断言；不要要求wire DTO变BusinessSlot，保反例 |
| `.../Sessions/EffectOperationReplayTests.cs` | 保两条raw encoder失败测试97／219的真实故障点、当前数量7、原frames、不重ACK、不重复扣；构造ledger及旧ReadCMD接口必要改参。加samecause／Committed／noFence／sourceclose，不改故障成transport |
| `.../Sessions/EffectSettlementRegistrationTests.cs`、`Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs`、`Game/ItemRefusalCodeTests.cs` | 原列表Use fine→wholeCMD及显式Main route；原状态/错误码纯映射可迁helper位置，原判定值保持，不机械删断言 |
| `.../Sessions/ReplayStateSnapshotTests.cs`、`.../Inventory/MagicSealHandlerTests.cs`、`.../Inventory/SouthernDaleCompatibilityTests.cs` | 实际旧Refresh／ReadCMD／直接ReadAsync接口签名闭包，必需显式原metadata/null；保原snapshot、current、ownership、SQL/bytes断言 |
| `.../Sessions/LegacyEffectFootprintTests.cs`、`ExplicitEffectExecutionTests.cs` | 仅若旧IReplayStateProjectionDelivery fake签名变化，精确stub闭包；TASK09/10全部原来源／owner／await保护仍跑，不能把旧effect测试改NonEffect |
| `.../Commands/ProtocolCommandExecutionTests.cs`、`CommandRequestContextTests.cs`、`CommandHandlerArchitectureTests.cs`、`.../Transactions/CommandWorkUnitsTests.cs` | 原effect/Main disabled判据转成真实启用＋非法状态拒绝；保其余注入反例、普通Write/ReadOnly、rollback、sticky、未await drain和默认postcommit／多参与者测试；补本Task矩阵 |
| `Dfo.Server.Protocol.Tests/Game/GameCommandOutboxTests.cs`、`Game/ReplayStateProjectionTests.cs` | typed新overload透传／CMD-NOTI反例；Create纯组帧原case与字节保留，仅必要定位/构造变化 |
| `Support/RecordedOutbound.cs` | Registering 同实例实现 ICommandMessageOutbox和INetworkMessageSender；AddTo alias同对象／同GameplayOutboxes，新增opaque透传保BeforeSend/BeforeExplicit、inFlight/finally、batch/explicit观察。底层仍同actual OutboxNetworkMessageSender，不直解析绕wrapper；不新provider／List发送替身 |
| `Dfo.Server.Networking.Tests/Integration/EffectReplayWireTests.cs`、`Outbound/OutboxRegistrationTests.cs` | 原真实TCP同序号不二扣及Required/BestEffort／callback／guard基线必须跑；只有实际新增断言或必要同契约测试driver可改，不改socket失败语义／期限 |

当前 Tool 下无 Use fine／Replay契约实际caller，不预授 Tool 改动。`NpcShopPurchaseTests.cs` ShopFixture/ShopSender目前通过真实dispatcher及RecordedOutbound，FailExplicit/FailBatch无需为Use新路由改故障点；只读复用，若实际必需驱动闭包需主控核源码后增补。既有TestGameplay本身不改为自动可信、不静态跨jobcap、不绕新bus的executor内拒绝。单元测试直接领域能力可用既有显式测试lease，但不把这当真实ledger/admission证据；新Main／B2关键证据必须真实入口。

### 8. 有价值保护顺序与测试 oracle

先保留源码／原断言与capture baseline，落新矩阵中最小能区分错误的case，再实现；API编译迁移可一次完成，不强造不编译RED。按以下真实依赖顺序推进：纯映射/admission→显式state/Main→reader/sink与legacy隔离→attempt／最终失败收口及RecordedOutbound→完整Use／route／实际测试driver→定向回归→冻结正式核验。一个Task不能停在基础设施全绿而Use仍legacy。

- ledger/Main：真实provider连续两请求同singleton不串，失败后下一次不串；fresh／同raw committed replay／confirmed SQL rollback retry／原entry.Ordinal窗口边界／序号不前进／hash或len冲突；原entry同对象在新旧route共享同一ledger验证，不造第二CWT。selected普通业务拒绝仍Committed最低Inventory；无selected只SilentACK、Main0、后选中不升权；缺状态／重复Main／reporter遗漏／旧session／ReadOnlyMain／未await/closed/跨job拒绝。保当次store数量与ledger最终状态同时断言。
- 足迹与事务：真实ApplyEffects slots/rental／XP等只在本Main记录并Freeze；旧legacy其它效果仍可走原45/58/60；新路径旧Enter与桥0次（结构/可观测拒绝），default四参ctx不获权。事务失败后明确ConfirmRollback才重试；commit已落SQLite后业务故障不重结算，普通多participant/typed仍逐人Fence＋DiscardExact，不能只有请求者。
- B2正例：原EffectOperationReplayTests两条保持真实FaultingEncoder；补原异常ReferenceEquals、同entry Committed、无新增Fence、source close在原已提交条目之后、之后当前数量7投影、不重ACK/扣物。一次为Main后explicit，一次为replay ordinaryWrite后batch；fixture同物理connection语义不冒称关socket后可再收包。
- B2反例：同一IOException从提交后业务、非许可普通登记、callback／conversion／lookup／Current／Register guard、Mirror/Create/Town、原typed发布均无raw资格；原typed／unknown与多participants保护保持。encoder加独立unit/lease/scopeDispose错，及独立阶段故意抛同一Exception对象，均撤raw且保cause引用；单一cause沿unit/body/drain传播不误判额外错。跨request／target同ID替换／job／closed／一次call复用，及业务catch后再开unit/record/成功返回均拒绝。callback并不因为inFlight获encoder证明。
- outbox：同provider确认 ICommandMessageOutbox与旧sender为同Registering，并观测真实FaultingEncoder inFlight；两个新overload仍即时编码、原Required顺序／BestEffort跳过、空batch／gone、source空条目失败close与rootless目标不误close。ordinaryRead写禁止、两unit第二rollback保第一条目的原case不退化。

至少一个可编译行为mutation：优先删 actual adapter encoder出处验证，使“已Settled许可存在但非encoder callback/业务同IOException”错误获得raw，原allparticipant fence oracle变红；或删唯一Main claim guard，用真实两次Main/store/ledger区分。选实际有稳定oracle的一个即可，不堆镜像反射测试。mutation前后保存bytes/SHA，try/finally恢复，恢复后重新build/目标绿；源未恢复不得正式核验。若初始红来自fixture未注册／不合法metadata／编译错误，记录诊断不计RED，先修驱动至真正到达目标分支。故意DFO坏例先定向确认精确class/count，不把新增assertion类别批量加入原expected。

### 9. 正式核验、闭账与可复核交付

用 task-11 独立 evidence/logs/meta，复用已存 probe-tools/migration-ledger.py 与既有DFO工具只读；可新增task-11脚本，不复制研究归档、不覆盖旧脚本/证据或同名 --output。开工preflight保 owned before-image、文件行尾/BOM/CRLF混合、未授权路径SHA；精确byte编辑，逐方法核原条件/loop/catch范围（TASK10已发生过漏braces真回归，本次不能靠机械插入相信语义）。所有变异finally恢复及最终source-freeze SHA必须吻合。

共享核心变化正式运行完整 `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0warning/0error；之后八项目各一个独立full step：`dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`，project为Dfo.Pvf.Tests及Dfo.Server.Application/Host/HttpApi/Infrastructure/Networking/Persistence/Protocol.Tests。无filter、原class并行／Skip／timeout/deadline不变。native在require_escalated的本机FSEvents环境执行，使用原caffeinate/runner；每step保原command/cwd/HEAD/UTC/exit/elapsed/资源(meta rss/user/sys等)和完整console detailed。工具返回session必须poll到自然结束；不TERM父runner/活跃编排树或丢失session后补造退出码。诊断轮／作废轮／最终轮分开；修产品后新冻结＋新完整轮，不拼接不同代码轮次。

测试总数从8523按逐文件实际N-D（Theory计case）闭合，各项目及全局实数对齐；驱动/准备更改、纯映射定位、断言改变/新增/删并分别列证据。仅允许原DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource 与 All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，准确 Dungeon7100/maze0/cell(1,0)/entrance Left 的原 InvalidDataException，以及App16＋Infra2原18skip。其它SQLite/Host端口/来源失败先核真实实例/source，不能塞基线或盲重试求绿。

每最终轮用一个全新DFO_GAMEPLAY_ASSERTIONS目录涵盖八full；七程序集各唯一原control，原74expected精确类/数（App62、Net7、其余五各1；开工核实际工具），旧expected不放宽。新增故意反例单列已定向实跑的身份/count与理由，不能借此吞新未知断言；外／exe／orphan0如实核，exe0仍只观察。完整Net Characterization10逐名pass/0skip，Golden15对f01dc226及Task10产品检查点原bytes不变，原codec/slot/sequence/连接断言保持。

ConfigureServices-only capture前后各独立task-11目录，不启动业务进程。目标173messages不变，GameUseStackable一条Legacy→Protocol；删fine加whole后205commands可能不变，预期11Protocol＋1Internal＋193Legacy、Kind165Tx/27Direct/9RO/4Workflow，必须按实际runtime/source逐条核，不硬填。message163Legacy→162、10Protocol→11，14telemetry不动。两个具名出口metadata单独落源码/运行时证据，未知shape拒绝判据不放宽；BuyItem两CLR仍待其自身最小单元。

开放账只闭有证据的 frozen ID：`message:65580`（GameUseStackable，完整业务与新route），`command:Dfo.Server.Protocol.Game.UseStackable.UseStackableCommand`（旧fine实际删除，introduced_artifacts指新wholeCMD与Main），以及确有本条legacy route去向的已有transition metadata；完整RQ005／ambient／来源／oldReplay不误closed。闭message必须证明原handler编排不再存在、唯一新CMD、真实工作单元与outbox，闭fine必须检索／runtime无旧注册；非只重命名。其它旧ReadReplayStateCommand、LegacyReplay ledger/Joust路径仍pending，逐caller/层级/最终删除去向写notes/artifacts。新expected pending149→148、194→193、source72通常不变，按实际ledger计；original/baseline/其它行不改，不关闭并未迁的所有Effect保护或来源。

先用probe `preclose --current-task <TASK-11精确路径>` 校验拟闭行的实际Artifact；Result后再strict validate/selftest，原八坏副本全拒，工具本身只读复用。不以completion_result_path提前填不存在Result骗strict。冻结全部owned源码／闭账／新证据，生成manifest SHA和Task Result R1 prefix快照；精确新增/删/改路径核对，无未授权源／旧Task/A/root变动。结果快审前保存原R1 prefix，review只追加自身段，主控据匹配结果做本地检查点。

Result须明确实现了哪一条完整协议、哪些共享状态/失败分类已经实证，保留两个raw出口兼容边界、所有默认反例与cause引用、真实SQL／原bytes和同provider证据、所有正式/诊断/作废轮、数量账、DFO/Golden/capture/ledger与暂留legacy入口。不得把B2/P2门、编译迁参或原测试历史当本次通过。单CMD≤2秒统一接受线在最终测量按原工具执行；本Task记录实际耗时，不提前宣称最终性能／Spec完成。Astra外审、其余148消息／193命令／72来源、T011删除与T012测量/封存、用户分支收尾门仍由后继主控处理。

## 执行增补 r2 — AGENTS 当前状态精确同步

主控已读AGENTS当前351～367行及419～434行：其正文仍将显式Effect/Main写为尚未启用，并把UseStackable列为旧fine Transactional。完整迁移后这些句子会失实，故精确增加可写路径 `AGENTS.md`，仅按本Task实际结果同步命令总线／注册当前状态相关段落：加入完整UseStackable协议路线、显式admission/Main及唯一账本／足迹、两具名即时raw encoder出口与默认失败保护的边界、replay普通Write后事务外sink和临时LegacyReplay仍pending、旧Use fine已删及当前旧细命令注册名单。保留其余legacy／Lifecycle临时来源和最终ambient删除责任，不将全部协议、T011或Spec写成已完成，不改其他架构约束或全篇重构。

文档更新与产品一起进入最终冻源／范围／manifest及正式结果；只有实现／实证成立后才将相关状态改为已交付。此为现有迁移文档职责补漏，无新产品范围或验收变化，不增加Task-plan审批门。旧Spec/P2/契约、旧Task和旧证据仍只读。

## 执行增补 r3 — 保留旧机制测试意图、Pending语义与两个精确闭包

主控已逐读HEAD原EffectOperationReplayTests的Second_primary（306～314）、Later_readonly（186～197）、Refusal_only（376～389）及当前诊断候选。旧Second_primary在BeforeExplicit里调用另一个旧Use fine，总线可内联但第二主结算被拒；raw异常在旧fine之外上抛，原断言随后当前状态重放。把它机械换成新whole CMD nested dispatch会先触发A既有GameplaySelfWait/ProgramError及新默认fence，测试变成了另一个机制，不能作为原目的已迁的证据。此处依根契约已授权的旧细命令测试改写及B2/P2禁止CMD调用CMD，作以下精确处置，不给新bus加inline/豁免、不改执行器算法：

- Second_primary旧方法的当前SelfWait诊断候选不得当最终版本；替换为同一真实显式CMD内两次Write(Main)的有界probe（复用已owned新测试基础及同provider，不带任意Func业务输入）。验证第一实际道具仅扣一次、第二Main业务未进入、同一entry身份／原projection／已确认Committed事实不被第二claim覆盖，重复claim为sticky程序错误、当前默认提交后保护围栏／sourceclose，原cause引用保留。旧nested API被删除，其异常来源／后续同连接可重放oracle不能强套到新非法nested CMD；合法协议重放及B2两条真实encoder原oracle仍逐字保持。若将该方法移到新机制测试文件，记录一对一旧case→新case映射及真实N/D，不能删除保护后以其他无关case补数；可保持方法位置／名称并改为调用有界probe。
- Later_readonly的原名称不是注册事实：其ReadReplayState实际Transactional。将其改为同一显式CMD的Main完成后，再ordinary Write内直接reader（显式metadata/null），不dispatch另一CMD；后续同raw重放仍必须使用第一Main的原足迹，保持原实际清空slot69、数量0、帧0x000E槽69及item=-1断言。把读取移到请求外不能证明原同请求第二事务不覆盖，故不是充分迁移。可以同用途命名替换并记录身份，不扩大业务或改readonly策略。
- Refusal_only当前旧legacy ledger测试可用尚存在的SharedEffect fine保持其旧guard目的与原无扣物／InvalidOperation／无消息断言，明确是旧兼容路径证据；不把它声称为新显式Rejected的证明。新Rejected/后来选角不升权仍须单独实际新入口验证。

主控另核旧ledger正常handler返回后仅在finally将Pending→Rejected，而不要求所有selected请求必有Main；实际ReinforceItemHandler在有session但Method非0/1或slots失败时仅回拒绝、不发fine，证明这是通用旧不变量。当前新ExplicitEffectRequest.DemandFinished无claim必throw缺合同依据，须纠正：未claim的selected正常拒绝结束仍FinalizePending→Rejected，不取得Footprint／结算权；只有claimed-but-incomplete保程序错误。新Mode.NoMain用例改为真实正常拒绝／无写／最终Rejected／相同seq不能再次生效的oracle，不能将未执行Main混为缺失请求状态。Use自身已有selected普通拒绝仍必须按本Task原主事务提交与最低Inventory；不迁Reinforce或新增可选Main注册策略，不改B2/P2。

精确新增两处App测试白名单，主控已读源码/实跑失败身份：

- `Execution/GameplayWriteEntryTableTests.cs`：仅EffectOperationLedger的Admit(NetworkConnectionContext, IEffectSettlementRequest)及FinalizePending(EffectOperationEntry)两条实际新增写成员列W，保原穷举／分类／反例；生产这两方法各首要路径有GameplayWriteAssertion.Writing，原Dispatch guard保留。此表是IL分类判据，勿无实跑就推定DFO新增量，原expected默认仍守；任何新故意反例须按实际身份单列。
- `Sessions/EffectDispatchOnExecutorTests.cs`：仅原An_effect_request_and_its_replay_both_run_as_the_inbound_job的方法名/CommandType从inbound:GameUseStackable改为实际nameof(GameUseStackableCommand)，对应已批准VC-B2；保真实dispatcher两次同raw、扣一次、explicit/send hooks、各自在executor/唯一作业的原保护和原IOException注错。不更换sender、provider、metadata或放宽断言。

Same_ID_new_physical_context原测试仍保两物理对象、原fence不消失、新ledger独立、新连接仅扣一次与旧连接不借用原oracle。若测试在OnlineStateRegistry替换物理对象，也须同步RecordedOutbound的测试connection table以模拟真实替换；只修该fixture注册的一致性，不放宽actual adapter exact-target校验或把旧对象误当新对象。

Registration.Freeze对无集合/无许可的旧registration保持原实例，只有真实集合才防变更冻结克隆；恢复原38项Assert.Same，不改其oracle。Main禁用旧测试按已批准的启用范围改为新合法selected Main及NonEffect/Rejected/Main非法状态拒绝，保业务0与其它原断言，是原Task已有迁移义务。所有诊断轮原样保留，不计机械错误为RED；上述精确测试重构、原断言有意变化/保留及N-D在Result单列，交required独立结果快审。产品修复后新freeze和完整最终轮，不拼接诊断，其他范围原封。

## 执行增补 r4 — 原窗口用例恢复、真实token负例与可达性澄清

正式01全部自然结束，原freeze01及meta保存：build0/0，App4792＝4774pass＋2准确原Dimensionfail＋16skip，其他七项目无新失败；全局8546＝8526pass＋2fail＋18skip。此轮少于预期8548两例，执行者case审计与独立预检分别确认r3替Later方法时脚本按下个Fact切块，误覆盖后面的Window_counts_every_inbound_frame_including_unknown_types Theory（HEAD原199～209，[127,true]／[128,false]）。必须从HEAD／before-image逐字恢复原两case及原帧计数／数量／过窗拒绝oracle，不计D2、不把恢复算新增覆盖，不以总数相近掩盖原case消失。正式01不是最终验收轮，不和修后拼接。

在已owned ExplicitEffectCommandTests等集中测试补齐实际opaque token负例，不能用旧request/footprint的泛化测试冒充：同请求真实完成token第二次调用前拒绝，encoder无第二次；第二真实CMD使用前请求token，进入攻击分支前不Main，SQL不变／encoder无增量；原请求仍open且已合法签发时，测试从独立global TestGameplay job攻击同provider实际typed outbox，在encoder前因job不符拒绝，finally释放原TCS并收齐原请求/源保护；该测试仅模拟不合法跨job调用，不新增生产executor／第二provider或信任豁免。闭后反例在真实scope Dispose中使用本请求token，使bus已Close但仍原job，证明命中closed而非仅job不符。保实际已确认store／entry／cause及最终默认fence保护，不私造token或反射生产private。

关于“encoder＋独立unit/lease”字面矩阵，主控与fresh reviewer已实际核可达性：TrackOperation追踪整个RunAsync（含finally/lease.Dispose），BeginRegistrationAttempt同时查unitOpen及所有operation.IsCompleted；即使EndUnit已执行、lease异步清理尚未完成仍不能签发。签发后pending attempt阻止新unit，实际encoder错立即sticky，完成后也不能再开unit。因此合法encoder与未完unit/lease错误组合被此结构guard排除。用真实未完unit时签发拒绝、encoder0、其后真实unit故障保持默认typed/fence的可达反例，以及上述全Task的追踪/清理源码依据守意图；不为凑组合造fakelease/反射或新全局机制。若另有实际可达独立unit/lease失败，仍必须撤raw且保cause；不能借此省略既有真实rollback/cleanup双因测试。scopeDispose是仍可达的独立第二阶段，原已实测同一Exception对象亦撤raw、保cause并全参与者fence，继续保持。

只做原Task已授权的收尾：原Use测试移出wrapper留下的新增空白行尾精确修正，保原混行尾；InventorySlotBoundaryArchitectureTests重复wire属性检查的一处改为实际UseAsync领域参数BusinessSlot边界及有意义命名，保wire输入GameSlot与反例，不增无关规则。源码/测试case审计既比较算术N-D，也核每个原Fact/Theory/InlineData身份与明确迁移对应，任何其他删并必须另经主控，不悄然计入D。

完成上述修复和新增负例后新freeze02、完整Release build及八full无filter／新鲜DFO独立轮。最终N-D以8523及实际新增case实数计，原窗口恢复非N；正反例/两个B2原注错/Golden15/表征10/原精确2fail18skip、所有原expected保护保持。旧轮和所有实际失败/弱oracle诊断均独立披露，Result及required review只消费最终同一冻源身份。其余白名单、B2/P2、产品范围与权限不变。


## Result

- Revision: 1（Task r4；HEAD 30a17361233482aac05aea219d7374a2dbd7cb24；最终 source-freeze-03；本轮执行结果待主控 required 独立快审，未提交）
- Outcome: done
- Changes: 已将完整 GameUseStackable 协议迁为唯一 GameUseStackableCommand，薄 handler 只发送整 CMD；原 UseStackableCommand/Handler fine 实际删除，非新 CMD 转发旧fine。wire GameSlot→BusinessSlot→canonical GameSlot、selected 的全部原业务检查／effects.UseAsync／绝对库存读取在同一 Write(MainSettlement)，普通 selected 拒绝仍提交最低 Inventory；无selected仅原SilentACK且Main0，之后选中不升权。原ACK→虚拟资源→绝对源槽→可选XP次序保持，没有Creature附帧；原业务body除显式参数/Demand外、codec及Replay Create原bytes等价，见 [source检查](../verification/evidence/task-11-inspection/source-checks-final.json)。
- Admission / Main: 原唯一物理连接CWT供新旧共用，pure Admit只检查/登记并返回Rejected/Selected/CommittedReplay；新路不调用旧Enter/bridge。原full raw identity/hash/length/selector/type、128窗口、latest ordinal及回滚确认后沿原entry.Ordinal retry不延长。每request捕获独立ExplicitEffectRequest/recorder，本job/owner/live Main事务/未冻结和关闭守卫；claimed Main唯一，缺reporter/readonly/第二Main拒绝，真实SQL一次/同entry和projection/Committed事实不被覆盖。通用未claim正常拒绝仍Finalize Pending→Rejected，不取得结算权；Use自己的selected拒绝仍Main。默认四参context不授Main权。
- Replay / legacy: 新committed replay在ordinary Write内直接reader，外层和reader内部Capture都必需显式metadata/null；unit结束后事务外sink按原MirrorMerge→Create→即时batch→Town次序。sink无bus/locator/Compose/deferencoding。临时LegacyReplayStateProjectionDelivery仅旧ledger.Dispatch/Joust消费者→一处旧ReadReplayStateCommand dispatch→同sink，仍pending；没有提前迁Buy双CLR/SharedEffect或全部旧来源。
- Failure / provenance: 两个具名出口只许可SettledRequiredExplicit与CommittedReplayCurrentStateBatch；opaque attempt绑定本request/job/阶段/同一物理target/单次实际call，actual adapter仅EncodeMessage/EncodeBatch窄catch记录原cause。业务、callback/conversion、lookup/current、buffer/Register/Mirror/Create/Town都无encoder出处资格，不能按IOException类或HasCommitted统一豁免。token未完成不能成功或开新unit，body故障sticky；在scope存活时drain全部已启动operation（含lease finally），再Close、收齐scopeDispose，最后一次FailAsync。只有最终单一原cause、实际encoder出处、原entry确认Committed、无typed/独立错误才原样raw、无新增fence；仍走原source-close处理。其余保持默认全committed/uncertain participants的typed/fence/DiscardExact，执行器/物理transport算法不变。
- Cleanup occurrence: scope处于本request显式phase，无ambient/global tracker。adapter closed guard同一事件向scope catch传播只计一次；body E＋scope guard C精确两cause，E＋scope Aggregate(C,D)精确三cause且保原aggregate/D，Dispose吞guard也sticky。body E在独立scope再次抛同一对象仍两次并撤raw；真实SQL/encoder/Committed、cause Ref与物理fence先后均实际断言。曾用beforeFailure snapshot遗漏已有E组合，真实RED后改phase；最终93定向全过，见 [诊断](../verification/evidence/task-11-inspection/diagnostics.md)。
- Negative matrix: 同provider真实已签token的复用/第二request借用/第一request仍open的独立global测试job攻击/原job已Close后的scope攻击均到实际outbox且无额外encoder/SQL；不造private token/lease或新生产executor，TCS finally释放并收齐。BeginAttempt同时检查unitOpen和全部TrackOperation完成；其覆盖RunAsync含lease.Dispose，合法encoder与未完unit结构上不可同现。可达替代是真实SQLite ordinary unit写gold后rollback cleanup挂起时拒签encoder0，release后真实unit双因失败仍默认fence，gold已回滚，首Main保Committed。新selected普通拒绝、无selected/noordinal、泛用无claimRejected、后来选中拒升权、singleton连续/失败不串、owner/rental/footprint关闭、multi-account全部参与者均有实际状态oracle。原typed/unknown/default tests保持。
- Driver / oracle migrations: 原两B2 encoder正例保持原FaultingEncoder故障点，补cause Ref/同entry Committed/no newfence/sourceclose后当前数量7投影，不重ACK/不二扣。Second_primary按r3改同真实CMD两Main，保原case身份，拒绝第二业务/store且sticky/defaultfence；不拿非法nested新CMD的SelfWait代替。Later_readonly按原ReadReplay真实Transactional改同CMD Main后ordinaryWrite directReader，保slot69/item-1/0x000E。Refusal_only明确旧SharedEffect兼容证据，新Rejected另证。SameID仅同步fixture两张注册表，保旧/新物理context与exact-target拒绝。Freeze原38 Assert.Same恢复；新增Admit/Finalize W分类和effect job label按Task r3闭包。完整映射、原case/断言迁移见 [test-delta](../verification/evidence/task-11-inspection/test-delta.json) 与test-source-audit；没有inline特权/新provider/自动信任TestGameplay。
- TDD / mutation: initial red-02可编译证实Effect/Main未启用，但不当唯一保护证明。最终可编译mutation-red-02删除实际encoder出处条件，使callback同IOException错误获raw；先真实SQL count2/Committed后fence oracle RED。try/finally原bytes/SHA恢复，green-02为1/1，再冻结完整formal03；见 [最终mutation](../verification/evidence/task-11-inspection/mutation-result-final.json)。scope-combination-red-01/02也为真实分支/状态/cause RED，green01为93/93；编译/fixture错误不计RED。
- Final formal03: 同一freeze03后Release --no-incremental 0warnings/0errors及八个独立无filter full全部自然完成，原class并行/5m/skip/期限保持；8556＝8536pass＋2准确原Dimensionfail＋18原skip。Pvf80；App4802＝4784pass/2fail/16skip；Host203；Http57；Infra976＝974pass/2skip；Net199；Persistence477；Protocol1762。两fail精确为原Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource及All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，均Dungeon7100/maze0/cell(1,0)/entrance Left原InvalidDataException。8523＋N33－D0＝8556（Explicit32真实case＋freeze1）；四个批准改名逐一映射，所有原case/status/18skip身份保持。完整command/cwd/HEAD/UTC/exit/elapsed/resources与console detailed见 [formal-summary](../verification/evidence/task-11-inspection/formal-summary.json)，未TERM活跃runner或补造exit。
- DFO / bytes / runtime: 三轮DFO各独立目录分析，七程序集唯一原control及原74expected精确类/数，无新增expected，外/exe/orphan0，exe0仅本轮观察。最终Net Characterization10逐名pass/0skip，Golden15对f01dc226、Task10产品检查点及HEAD原bytes相等。ConfigureServices-only before01/after03不建provider/不启动业务，173messages/205commands不变：11Protocol＋1Internal＋193Legacy，Kind165Tx/27Direct/9RO/4Workflow，message11Protocol＋162Legacy、14telemetry。仅message65580变route，旧fine移除、新whole唯一，两具名权限仅whole，其他runtime绑定不变，见 [capture-checks](../verification/evidence/task-11-inspection/capture-checks.json)。
- Non-final rounds / actual failures: 全部meta/log与 [diagnostics](../verification/evidence/task-11-inspection/diagnostics.md) 保留。formal01完整8546＝8526pass/2fail/18skip，但原Window两case被替换脚本误覆盖，按before逐字恢复，不计N/D且01不可接受。formal02完整8553＝8532pass/3fail/18skip；除2原fail，Networking原refuseJob:true在line145 ConnectAsync Connection refused，未到业务/拒队分支，唯一host/端口根因未证；原精确定向2/2通过仅未复现，不称已修/不增基线，不改非owned harness/并行/timeout。02后产品修scope组合，所以也不拼最终。此前fake/nested/缺using/旧注册identity/弱Rejected generation等诊断逐项披露；没有用未到目标的异常冒充保护。
- Scope / ledger / handback: 49精确owned源码/测试/AGENTS，preflight9481中9436未授权tracked文件SHA保持；原BOM/行尾/混合byte、whitespace、原business/codec/Create、唯一CWT及最终冻源匹配。ledger只闭message:65580和旧Use fine精确ID，三个既有pending transition仅notes/artifacts；original/baseline/其余行保持。preclose通过，Result后strict/selftest原八坏副本及本轮selfcheck/manifest见task-11-inspection。R1 prefix另保存供fresh required review只追加；未stage/commit/push/PR/merge/remote issue/deploy/cleanup/运行游戏/改版本/起TASK12。其余148消息/193旧命令/72来源、5保护/8过渡pending，临时LegacyReplay/LegacyFootprint/Lifecycle删除、T011/T012测量封存、最终≤2秒线/Astra/Spec及用户分支收尾由后继处理；本结果仅交主控required独立快审和本地checkpoint，非自批或Spec完成。
