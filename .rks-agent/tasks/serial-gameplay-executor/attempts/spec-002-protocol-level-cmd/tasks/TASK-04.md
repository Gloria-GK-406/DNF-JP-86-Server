# TASK-04 — 同一命令骨干与 ScriptVersion／GM 启动首贯通

## Task Unit Contract

- Revision: 3（r1原计划与r2裁定保留；本修订按原真实降级行为提前补F-129区分证据）
- Outcome and references: 交付父 [Plan P1](../plan.md) T-003 的首个可用闭包，对应 [Spec B1](../spec.md) RQ-001～005、007～009、012～015及AC-001/002/004/005/007/008：保留同一 ICommand／Handler／Invoker／CommandBus，建立显式 Protocol/Internal 路由、请求与来源数据、登记能力和普通显式工作单元，以真实 ChannelScriptVersion 与 GmBootstrap 验证。其余协议继续批准的 legacy 路径；新效果路由和主结算在本单元明确拒绝，不借旧 ambient 获权，不冒称已交付效果账本或整个T-003。
- Owned boundary: 下文精确生产／测试／证据边界；冻结 migration-baseline.json、已提交 Task、父 Spec/Plan/契约和 A 封存制品只读。不得顺手迁其他协议或修旧 pipeline 的潜在问题。
- Required checks: 新入口/请求/登记/普通工作单元的真实区分验证及两生产首例；启动注册和注入判据含坏例；完整 Release 构建、八项目原类并行全量、表征与Golden无变化、新鲜七程序集DFO及数量账；迁移账严格校验与精确范围/行尾检查。见验证段。
- Dependencies: TASK-03（`26d0e4d1bccef7a5721535f71a91ab6939b3f75f`，冻结全集和开放账；间接消费TASK-02四保护/F132与TASK-01基线），当前数量账8365。
- Result review: required — 改共享入口、注册闭合、请求权威、事务和发件箱接缝；错误可被后续百余切片迁移复制，普通线级字节验证不足以发现重复作业、误获信任或提交后误判回滚。须独立检查实际实现、反例与完整证据后提交。

## Detailed Plan

### 1. 开工、边界和已明确的延期

确认上述HEAD、唯一Task身份及已提交依赖，除本计划无其它未提交改动。执行者先读根AGENTS、Docs/Agent/ArchitecturePhilosophy.md和RefactorPreferences.md；本工作区无.codegraph则按已有rg路线。继续原分支/工作区、本地带Co-Authored-By检查点；不推送/PR/合并/远端修改/部署/清理，不运行df_game_r或DNF.exe。主控负责结果快审和Git操作。

本Task只在生产启用 ProtocolNonEffect 和 TrustedInternal。四种具名请求状态有封闭形状、关闭和非法使用语义，但 **ProtocolEffectRejected／ProtocolEffectSelected 不通过新路由执行业务**，注册/调用均在进入业务或事务前明确拒绝；全部效果协议仍LegacyQueued。不得造空句柄或默认Noop声称满足选中效果结算。显式效果句柄、IEffectFootprintRecorder/EffectExecutionContext及singleton传播、一次主结算/Freeze/重放/Pending结束和连续singleton验证仍属首效果单元，记在开放账和Result，最终RQ/AC不降低。ICommandWorkUnits的MainSettlement声明本轮也在执行业务前拒绝，普通Write/ReadOnly/RequireRollback和不确定/提交后错误保护则必须可用并实证。

允许的生产文件（相对Server/src；新文件可按所列职责在同目录合并，不另扩领域）：

| 位置 | 允许责任 |
|---|---|
| `Dfo.Server.Application/Commands/CommandContracts.cs`、`CommandBus.cs`、`CommandHandlerRegistry.cs`、`CommandDispatchingExtensions.cs` | 同一模型的路由/显式入口、context携带、注册/启动验证，legacy隔离 |
| 新 `Dfo.Server.Application/Commands/CommandRequestContext.cs`、`InternalCommandOrigin.cs`、`ICommandWorkUnits.cs`、`CommandJobEntry.cs` | 封闭请求/来源、工作单元声明和共同作业起点；不放新ambient或locator |
| `Dfo.Server.Application/Commands/SelectedSessionSafety.cs` | 新路径显式metadata捕获入口，原精确检查/DiscardExact保持 |
| `Dfo.Server.Application/Dispatching/MessageHandlerRegistry.cs`、`NetworkMessageDispatcher.cs`、`NetworkMessageDispatchingExtensions.cs`、`NetworkConnectionLifecycleDispatcher.cs` | opt-in路由/闭合及共同entry初始化；生命周期本轮不迁内部CMD |
| 新 `Dfo.Server.Application/Transactions/CommandWorkUnits.cs`；既有 `DomainTransaction.cs`、`DomainTransactionScope.cs` | 复用事务的普通单元runner；仅增加明确完成状态/lease只读暴露，不改变旧提交/回滚/回调/组装行为 |
| 新 `Dfo.Server.Abstractions/Networking/ICommandMessageOutbox.cs`、`ICommandConnectionClosures.cs`；新 `Dfo.Server.Abstractions/DomainEvents/ICommandProjections.cs` | P1三个窄登记契约，不引传输/队列/事实发布/Compose |
| `Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs`、`Dfo.Server.Application/Execution/GameplayQueue.cs` | 复用同一常设登记实现及DI别名，不新队列或执行器依赖环 |
| `Dfo.Server.Application/DomainEvents/EventService.cs`、`DomainEventDispatchingExtensions.cs` | 同一EventService实现窄Project接口和DI别名 |
| 新 `Dfo.Server.Protocol/Game/IGameCommandOutbox.cs`；`Game/GameMessageSemantics.cs` | typed CMD/NOTI登记及原端点接线，不改变编码/帧首字节语义 |
| `Dfo.Server.Protocol/Channel/ScriptVersion/ChannelScriptVersionHandler.cs`；删 `GetChannelScriptVersionCommand.cs`，新 `ChannelScriptVersionCommand.cs`；`Dfo.Server.Protocol/ProtocolModule.cs` | 首协议归并、原查询命令与result删除、仅本协议注册变更；decoder/encoder原样 |
| `Dfo.Server.Application/Administration/GmAccessCommands.cs`、`ApplicationModule.cs`；`Dfo.Server.GmWeb/GmBootstrapService.cs` | 仅Bootstrap内部来源、普通显式Write；其它GM命令不迁 |

允许测试边界：Application.Tests现有 `Commands/CommandHandlerArchitectureTests.cs`、`Commands/CommandBusExecutorTests.cs`、`Commands/CommandBusTests.cs`、`Dispatching/NetworkMessageDispatcherTests.cs`、`Dispatching/GameplayDispatchTests.cs`、`Channel/ChannelControlMessageHandlerTests.cs`、`Commands/Channel/ChannelControlCommandHandlerTests.cs`、`Execution/GameplayStateCensusTests.cs`、`Execution/GameplayWriteEntryTableTests.cs`；新增 `Commands/ProtocolCommandExecutionTests.cs`、`Commands/CommandRequestContextTests.cs`、`Transactions/CommandWorkUnitsTests.cs`；既有 `Transactions/DomainTransactionTests.cs`、`Execution/Outbox/GameplayOutboxTests.cs`按新完成状态/窄登记对照补例。Protocol.Tests既有 `Game/CommandRegistrationTests.cs`及新增 `Game/GameCommandOutboxTests.cs`；Networking.Tests的 `Integration/ChannelControlMessagesIntegrationTests.cs`、`Integration/InboundExecutorWireTests.cs`、`Outbound/OutboxRegistrationTests.cs`及同目录 `OutboxRegistrationHost.cs`仅夹具/新能力对照；Host.Tests的 `GmGameplayRouteTests.cs`、`GmAuthenticationHttpTests.cs`和新 `ProtocolCommandHostTests.cs`负责真实首例/DI/持久化。旧fake如未用新Internal入口不泛改，见下节。若结构/写入口判据因新增真实entry需更新，保原集合并补实际新入口，不能移除旧写assert或坏例求绿。

AGENTS.md只加准确的opt-in过渡说明/新能力与删除责任，最终整体架构文档仍归T-012。B本地允许本Task、`verification/logs/task-04-*`、`verification/evidence/task-04-*`、开放 `migration-ledger.json/.md`和两probe工具适配实时注册。baseline/original字段不得改写；产品/schema、其它协议、旧pipeline/EffectOperationContext/EffectLedger实现不在本Task改动范围。新增必要fixture/实际消费者越出上述清单时先给主控证据和最小增补。

### 2. 同一总线、注册身份与唯一入队

沿P1增加 `IProtocolCommand<TResult> : ICommand<TResult>`，携带原强类型消息、精确NetworkDispatchContext及显式的Game/Channel元数据形状；不反射Metadata属性，不用两nullable字段猜种类。保原Invoker实现。命令注册显式区分 LegacyFineGrained/Protocol/Internal，新类型不通过legacy默认pipeline或ExecutionKind推断事务。命令和结果类型仍编译期绑定。

新Internal入口接收同一ICommand和InternalCommandOrigin，生产CommandBus显式实现：来源种类、已选root/child链、入队/作业取消策略和可选真实source明确验证；不能由null网络字段推断TrustedInternal。Origin不能带业务Func。类型/来源类别不匹配（Protocol经Internal、Internal无origin、未知来源）在业务前拒绝。所有入口先检查过期job/leak，再处理on-executor；新整CMD内调用任何bus入口（包括legacy）均拒绝，不能落旧Inline。legacy旧调用可继续原Inline，仅为尚未迁路径。无新AsyncLocal/depth/request slot；不在singleton存“正在执行的新CMD”。

主控已允许一个有界测试兼容：若给ICommandBus加Internal成员，默认interface实现只throw NotSupported，不转发Send、不inline、不建可信状态；生产bus必须覆盖且首内部例用真bus。尚未消费新入口的约40旧fake不用泛改，确需消费的fixture显式适配。将默认拒绝兼容及实际消费者列在既有 `transition:legacy-routing` introduced_artifacts/notes，T-011删除/收口；不新增baseline ID或把此兼容说成最终结构。

消息新注册绑定已注册消息号、允许消息CLR形状、薄handler和实际Protocol CMD；启动同时查命令注册，拒绝缺注册/错类别/多义/错CLR绑定和注入违规。LegacyQueued保持原decoder闭合、未注册枚举可存在和fence前lookup语义；14遥测保持日志例外。ProtocolCommand分支在executor外只解包交付给薄handler，由bus创建唯一GameplayWork，禁止dispatcher先入队后等待bus。真实队列计数/失败测试证明只一作业。

### 3. 显式请求和共同作业起点

bus job body第一步调用窄CommandJobEntry初始化当前发件箱，然后才建scope、查身份/authority/业务。两legacy dispatcher暂用同一entry代替各自散落Current，不给executor注入outboxes、不用Func延迟解析绕DI环。无sender容器继续原丢弃行为；source来自真实协议连接或明确origin，不从收件人推导。

新请求在该job上建立，Protocol先按A读取fence/superseded并静默丢弃，捕获登录权威后在需要时复查；上下文显式传递，finally Close。四个封闭具名变体依P1；本Task未启用效果两态，绝不回退legacy callback/frame。新增SelectedSessionSafety显式metadata重载，new path不读NetworkCommandAuthorization.Metadata；原重载只供legacy并登记删除责任。Channel缺Game metadata是明确的Channel形状，不是TrustedInternal。新context拒缺失/关闭/错误来源/跨job使用；legacy四参数context构造若暂保，只供旧pipeline，新runner不得将其缺Request解释为可信。

已有CaptureAuthority返回权威谓词，若沿用应作为本请求显式只读捕获值，不能用于延迟DI解析；也可在LoginSessionRegistry内提供等价具名值（这需要先由主控增补精确文件边界）。请求不注册DI当前值、不放GameplayJobSlot/事务、不新增ambient。不改GameplayJob常设身份。ctx记录保护失败/单元失败状态，业务catch不能使它继续授予单元权利；结束时确认失败并Close，原cause优先，不用新异常覆盖。

### 4. 普通显式单元与登记能力

ICommandWorkUnits只管理显式ctx、单元名/声明、复制冻结的精确参与者集合、业务delegate和当前单元RequireRollback。普通Write可为空参与者（真实GM启动）；非空逐人复查、元数据和原物理recipient由显式输入提供，不经旧SelectedSessionCommandScope。ReadOnly保持只读参与者限制，不开写outbox unit。delegate只在当前CMD内调用领域能力，不能承载另一个CMD或作为internal输入的任意业务回调。

复用DomainTransactionScope/Lease，正常结果后Commit；RequireRollback先真正Abort/Dispose确认再返回原拒绝，不能随后误记提交；失败清理尝试全部参与者且原cause/typed uncertainty保留。最小完成状态可加到现DomainTransaction并由Lease只读暴露：在准备/回滚/全部participant提交的真实分界记录ConfirmedRollback、Committed或Uncertain，不能从IsSettled猜。记录元数据不改变旧算法、提交点或afterCommit逐回调Warning策略。Commit后普通错误（包括CMD返回前的后续业务错误）不得描述为已回滚；结合ctx已确认单元和精确参与者逐一围栏，禁止后续写/重结算。typed uncertainty通过递归Contains判断，不仅最外层类型。精确围栏保留DiscardExact和原异常，close登记不被回滚。

MainSettlement声明目前必须在业务前fail-closed，不能转调旧operations；其显式账本/Freeze完整实现留首effect单元。F132延迟Compose API不经新契约暴露、不启用，原Host禁用判据保持；不能把新增完成状态元数据冒称该机制已修复。

ICommandMessageOutbox复用OutboxNetworkMessageSender同一编码/登记核心，旧sender与新窄契约用DI同实例/具名核心，不复制发送逻辑或把transport整接口换名。Register/Batch/Explicit保持原编码点、缺连接丢弃、Required/BestEffort逐目标隔离与顺序。ICommandConnectionClosures仅登记精确关闭，复用同一closer核心。IGameCommandOutbox在Protocol保CMD/NOTI类型与typed batch/explicit，适配新登记契约，不依赖旧IGameMessageSender获得隐藏发送能力。ICommandProjections由同一EventService实现，DI别名到同一实例；不暴露Enqueue/Direct/Compose，写事务内即时投影仍拒绝。

构造器判据保Abstractions后缀规则/IPartyService/TimeProvider，P1具名窄能力/ILogger为精确例外（附理由）。反例至少覆盖table、persistence、registry、bus、旧sender/closer、queue/executor、locator、伪命名Outbox；不能“任意interface/Outbox后缀都允许”。Protocol路由handler仅bus/logger，legacy handler不以新规则误杀；新handler业务/登记能力的坏例必须能拒。

### 5. 两个生产闭合行

ScriptVersion薄handler只把原request/context交给新CMD并等待；CMD查询IChannelDirectoryDomainService.ScriptVersion、登记原ChannelScriptVersionResponse到原connection，删除旧Get命令/handler/result及其唯一注册。ChannelControlMessageHandlerTests改为验证原输入和精确context交付，旧返回映射断言迁到真实CMD/总线/线级入口，不能简单删用例；其它Script/Info测试不改。新协议CMD返回无业务结果的具名完成值即可，不造第二结果模型。原codec/encoder与线字节不动。

Bootstrap命令仍现有类/业务数据，不新增密码ToString/log；改Internal注册，handler普通Write调用IGmAccessDomainService.BootstrapAsync并保bool。GmBootstrapService外部仍配置校验/PrepareBootstrapAsync，再真bus Internal入口，StartupRecovery新根、原 `startup:GmBootstrapService` 命令标识、无source、原取消行为。删producer的queue包裹，不把hash搬executor，不包额外事务。用真实SQLite首次启动/重复启动身份不覆盖、prepared null拒绝及原GM host测试证明持久化与单段边界；只有此内部来源迁移。

开放账只闭合 `message:5`、旧GetChannelScriptVersion命令、Bootstrap旧命令和 `source:startup-gm-bootstrap`；列原Direct无事务与新无事务的有证据none，原Bootstrap Tx与新普通Write一一对应、账号/会话参与者无此类的依据、保护位置与测试。original/baseline不变，其余pending。既有legacy-routing行记录本轮新分支/默认拒绝/旧context重载及最终删除责任，不标已删除；effect能力延期义务明确记notes。

probe工具适配新注册API/route/current执行形状，历史baseline校验仍以冻结数据为准，不要求已删旧源文件当前仍存在。避免Result与closed校验循环：可提供仅精确当前TASK-04的闭合预检，除该Task暂未写Result外照常校验全部映射/证据；其它Task仍严格。产品/预检通过后写Result，再跑正式严格validate并更新同一R1 Checks。不能伪造done或借TASK03冒领闭合。

### 6. 验证、证据与交回

先应用test-value gate：纯签名/注册机械搬迁不造编译RED；行为保护用可运行断言验证，并以选定的局部坏变异（例如误让新route先queue、Protocol经Internal获信任、关闭ctx仍开单元、runner将已确认提交记回滚）证明对应oracle能发现实质错误。每次变异独立、立即原字节恢复，不用fixture超时/编译错误冒充RED。执行中最小可用探针先走两真实首例，再扩边界验证，不一次重写139handler。

必需区分项：

- 真dispatcher→薄handler→真bus→ScriptVersion仅一作业，原response/connection/线字节；真实GmBootstrap普通Write及hash off-executor/原来源/敏感字段不日志。
- Protocol/Internal错入口、错误消息CLR/CMD绑定与重复/缺注册在启动或业务前拒；legacy inline与遥测/未接线枚举原测试仍有效。
- 连续两请求、失败后下一请求、同一singleton接收显式context不串状态；结束后ctx/泄漏续体/错误job不能开单元或借Internal获信任；新CMD nested任何bus入口不执行业务。效果状态/主单元未启用明确拒绝，不把这些例冒称真正effects lifecycle验收。
- 普通Write提交、ReadOnly拒写、RequireRollback正常拒绝真回滚；Prepare/commit失败确认回滚保原异常、清理失败typed保原cause；两单元第二回滚仅刷第一、未完成单元不刷、close跨回滚；不确定或确认提交后普通错误逐人fence且禁止继续写；普通未提交失败的rootless内部作业不把target当source关闭。不确定结算依据Safety明确关闭参与者是另一条原有业务保护，测试要区分这两个理由。
- 发件箱在身份/handler失败之前建立：零登记错误仍在作业结束关闭真实source；source空的内部失败不把目标当来源。新窄能力只登记、不在executor等待网络，显式多收件人失败隔离/顺序及Game类别原断言保持。无sender容器DI/启动/丢弃仍可用。
- 四保护及F132禁用原测试保持；架构/写入口/census判据只因新实际形状增补，旧反例保留，不新增request AsyncLocal。

正式native `require_escalated`/caffeinate，保存每轮cwd/HEAD、实际命令、UTC起止、exit/耗时/可得资源；唯一task-04日志/证据目录。完整 `dotnet build Server/86JP.Server.slnx -c Release --no-incremental` 0warning/0error，然后八项目全部 `dotnet test <csproj> -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`，保原xUnit类并行、期限/Skip；八项目为Pvf、Application、Host、HttpApi、Infrastructure、Networking、Persistence、Protocol。起点分别80、4631、200、57、970、197、477、1753，总8365。仅两准确DimensionDungeon内容失败和18原Skip为披露基线，其余新失败先诊断；不改断言/Skip求绿。

新鲜DFO用原violations.py核七程序集各一control；原App61/Net7/其余五各1=73条口径保留。新增故意leak/自等反例若产生证据，先在定向轮确定其明确测试类/数量并记录新expected依据，再全量核查；不得放宽原类数或容纳无归属记录。反例类/control外0、exe0、无测试帧0，保留exe0仅观测边界。原表征10/0及codec/Golden字节/顺序/开关断言保持，本Task不更Golden。此骨干改变执行路径，但完整性能同工具冻结测量仍归T-012，本Task不以suite总时长/内存作停止门槛。

结果列N/D/合并逐条加法（尤其旧Get测试适配与新断言分开），数量8365+N-D，原断言保留/有意改变仅VC-B2去重复Error有依据；不把未迁legacy重复日志全删。检查精确白名单、新文件及staged authored diff-check、原混合/CRLF字节格式、旧受保护资料/baseline不变。严格ledger validate通过后，交主控required快审与本地检查点。剩余T003其它简单协议、真实效果句柄/主结算/footprint及其余来源/清零/最终核验明确未完成。


## Controller Disposition — r2 复用作业既有粘性错误

- Decision: `task-rework`；同Task保持未提交、Spec B1 / Plan P1 / 已定首贯通与效果延期边界不变。
- Evidence: 执行者检查发现 `GameplayJob` 已持有 `Active.ProgramError`，`AssertNotOnExecutor` 会把保护错误记到现token；目前仅有写入该故障的入口、没有读取健康状态的公开接缝。仅拒绝嵌套bus但允许业务catch后继续开单元会违反本Task“保护失败后不得继续写”的合同。主控已读实际GameplayJob.Current/Active/AssertNotOnExecutor/ProgramError源确认状态现存，不需要新请求ambient。
- Amendment: 精确增补 `Server/src/Dfo.Server.Abstractions/Execution/GameplayJob.cs`，只允许公开窄 `DemandHealthy`（或等价具名只读方法）：检查现Active、用EDI重抛其原ProgramError；不新增字段/AsyncLocal/slot/执行循环，不改原进出作业、Fault及end-hook行为。新ctx的DemandCurrent使用此健康检查；新整CMD在执行器内dispatch用既有AssertNotOnExecutor记故障，使catch后仍不可继续写。legacy Nested原行为保持，结束/泄漏入口仍先拒。
- Checks / readiness: 新测试覆盖“catch nested错误再开Write，业务sentinel/持久写均未执行，作业仍以原保护错误失败”；该测试的故意反例取证须如实加入当前类expected，不能削弱原类。原完整八项目与七control要求保持。此是P1保护失败粘性要求的最小现有状态接缝，非重设计执行器，已required快审包含该文件；无新依赖或待用户决定，允许恢复实施。


## Controller Disposition — r3 F-129 真实入站降级保护

- Decision: `task-rework`；P1已允许在Channel迁移当单元补F-129，现ScriptVersion首贯通是该有用边界。本Task未提交，Spec/P1中心方案及A封存不变。
- Evidence / factual correction: 主控核对Spec §F-129（第109行）、AC-008、A follow-ups F-129及当前 `NetworkPlugins.cs:89–101`。要求是守住业务已由executor记录后的降级并能发现恢复中间件Error；Spec没有规定Warning/Information。P1对应概述的这两个级别为描述误记，实际且应保留的是 `GameplayJob.EndedAJob(error)`→Debug、未入队拒绝→Error。本轮不改生产日志级别来迎合误记，不改写P1/Spec/A。
- Amendment: 允许在已列 `Networking.Tests/Integration/ChannelControlMessagesIntegrationTests.cs` 加真实TCP业务失败Debug与停止executor后入队拒绝Error的区分例。仅新增 `Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs` 的临时变异权限：将Channel catch的Debug单行恢复Error，运行业务失败例必须在级别/重复Error断言红；立即原字节恢复，最终该产品文件SHA与HEAD相同、无diff。Game分支/解码/关闭/策略不动，不放宽期限或虚构尚未收到的Channel帧。
- Checks / readiness: 原真实Channel入口、正确队列状态与日志类别前置先成立，编译/夹具连接失败不计RED；变异只影响目标日志行。F129若这些证据均通过可在本Result/开放账现有notes中记已交付，不需等待T011。required快审/完整八项目/新鲜取证与数量账保留，精确当前临时权限不加入最终源改动白名单。无新Task依赖或待用户决定，允许恢复。


## Result

- Outcome: done（实施轮R1，Task契约r3；待required独立快审与主控本地检查点，不表示整个T003／Spec完成）。
- Changed: 同一ICommand/Handler/Invoker/CommandBus保留Legacy/Protocol/Internal显式路由；强类型Game/Channel元数据、封闭请求/显式origin、早期共同outbox entry、具名登记能力及普通WorkUnits可用。新整CMD不能进入legacy inline/内部loop，缺Request/关闭/错job不获权；复用GameplayJob原粘性错误，catch nested后不能继续写。没有第二bus/执行器、新AsyncLocal/请求slot或DI当前值fallback。
- Changed — 收口: DomainTransaction仅按真实阶段暴露ConfirmedRollback/Committed/Uncertain，不从IsSettled猜提交，不改原事务算法/回调/Compose。普通Write/RO/RequireRollback、原cause/typed uncertainty、提交后普通错误逐精确参与者围栏有实证；显式ctx追踪单元完整Task，未await的action/commit先失权再等待全部settlement/cleanup收口，随后关闭ctx与释放scoped依赖，不让业务续体越过job结束。消息/关闭/投影与Game CMD/NOTI窄能力别名到原实现/登记核心，不等待传输。
- Changed — 首例: ChannelScriptVersion薄handler原输入/精确context交新CMD，一次真实入队，原领域读/response/connection/线字节保留；删除旧Get命令/result/handler与唯一注册。GM Bootstrap仍原类和domain业务，hash在executor外，真Internal入口保持StartupRecovery原root/label/source空和queue/body取消；单个普通Write使用原真实SQLite事务。只迁这两个生产能力，实际生命周期仍legacy。
- Checks — Test value与RED/GREEN: 新入口/权威/普通单元及真实线级能力是可观察行为，使用真实断言；纯签名/机械注册不造编译RED。core-tests-02真实暴露await-using清理先于catch导致业务cause丢失，03修复后92/92；scope-order-red-01两个action gate真实暴露scoped依赖提前Dispose，green-01为2/2。六独立坏变异（closed、健康、来源替代、重复入队、提交metadata、Channel日志级别）全部断言红→原字节恢复绿，详见[mutation结果](../verification/evidence/task-04-mutations/results.json)。core-tests-01编译适配错误、channel-failure-green-01缺脚本fixture失败均保留日志且不计RED。后续选组core-tests-07实际79/79，不与早先96组混分母；GM真实首例15/15（新2+旧13）、typed Game登记2/2、真实Channel成功/两降级例3/3最终亦在全量覆盖。
- Checks — 正式: 最终`task-04-formal-build-02`完整Release `--no-incremental` 14.441s，exit0/0warning/0error；八项目均原类并行、原Skip/deadline、`--no-build --logger console;verbosity=detailed --blame-hang-timeout 5m -m:1`，本机require_escalated/caffeinate。Pvf80/80；App4680＝4662pass+2fail+16skip，exit1；Host202/202；HttpApi57/57；Infra970＝968pass+2skip；Net199/199；Persist477/477；Protocol1755/1755，其余exit0。合计8420＝8400pass+2准确原Dimension失败+18原skip，没有新增失败。两原失败仍是Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource与All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，均Dungeon7100/maze0/cell(1,0)/entrance Left内容错误。逐轮原命令/cwd/HEAD/UTC/exit/耗时/children_maxrss在日志同名json；Darwin子进程资源仅观测，不作性能门槛。[正式汇总](../verification/evidence/task-04-inspection/formal-summary.json)。第一轮完整日志也保留：原EffectSettlementRegistrationTests39项Assert.Same暴露新Freeze无条件克隆legacy null绑定；只改null返回原record、有绑定仍只读快照，原测试不动，第二轮真实通过。
- Checks — 数量/断言: N55/D0，8365+55−0＝8420。App新增49＝Protocol17+Context5+WorkUnits14+架构坏例5/许可8；Host2、Net2、Protocol2，余0。两个旧ScriptVersion用例仅改驱动，旧版本值/response/原connection断言转移到新CMD和真实TCP；被删旧DTO的类型断言随API删除，薄handler改验原输入/精确context，没有删除测试或改变其它旧断言。新Capture表项只分类实际readonly入口，不增测试数。[逐项加法](../verification/evidence/task-04-inspection/test-delta.json)。
- Checks — 保护/字节/取证: 四保护原例及Host真实IL F132禁用判据均在最终全量通过；F132仍未修且新API不暴露Compose。原表征定向10/10、32.930s，15个Golden文件对f01dc226零diff，codec/顺序/类别断言由完整Net/Protocol覆盖。新鲜七程序集formal-02各一control；原73记录口径不改，仅追加经定向17/17实测的ProtocolCommandExecutionTests ended-flow反例1，合74；violations.py exit0，原类数全部匹配、外0/exe0/orphan0。exe0仅此测试取证边界，不冒称运行游戏exe；本轮没有运行df_game_r/DNF.exe。
- Checks — VC-B2/F129: 新CMD没有旧pipeline重复Error；Channel真实业务失败已由executor记录后middleware仍Debug，停止executor未入队拒绝仍Error。临时Debug→Error变异被真实TCP断言拒绝，NetworkPlugins最终SHA与HEAD相同。P1概述Warning/Information为描述误记，按Spec109/AC008及原生产事实保Debug/Error，没有改P1/Spec/A迎合误记。其余legacy重复日志仍待T011，不冒称全部去重。
- Checks — 开放账: 当前capture仅ConfigureServices/枚举真实注册，无BuildServiceProvider或业务执行，173消息/141handler、204命令/203handler全部377条源码↔runtime闭合；Kind169Tx/22Direct/9RO/4Workflow，route为1Protocol+1Internal+202Legacy，消息1Protocol+172Legacy。[capture](../verification/evidence/task-04-capture-01/crosscheck.json)。仅message:5、旧Get、Bootstrap旧command、startup-gm-bootstrap四行closed，actual units/参与者/保护逐行原新映射见[workunit-map](../verification/evidence/task-04-mapping/workunit-map.md)，冻结baseline/original不改。最终preclose只豁免精确当前Task尚缺Result；四坏副本证明strict缺Result拒绝、其它Task不被豁免、路径外拒绝、映射仍必需。严格validate与selftest在本Result落盘后以原版运行均exit0，completion_results_pending为空；8个坏副本全部拒绝，见`task-04-ledger-strict-01`/`task-04-ledger-bad-data-01`及mapping目录strict-final/bad-data-final.json。
- Checks — 范围: [source-checks](../verification/evidence/task-04-inspection/source-checks.json)含48个产品/测试/工具/账本条目的最终SHA（含1删除null）、精确白名单、行尾/BOM及`git -c core.whitespace=cr-at-eol diff --check`；33个tracked变更及15新Server文件均有授权，baseline/旧Task/父制品/A/其它源逐文件比开工SHA不变，NetworkPlugins临时变异已恢复。新著源/工具/Task空白检查通过；staging/commit仍由主控。最终证据全文件SHA见[artifact-manifest](../verification/evidence/task-04-inspection/artifact-manifest.json)。
- Remaining: 158业务消息、202旧命令、72执行器来源pending，14遥测/12 Outside保原排除，五保护新归属和八过渡删除证据pending。两effect状态无工厂、注册/执行和MainSettlement在业务前明确拒绝，实际ledger/footprint/singleton传播/一次主结算/Freeze/重放/Pending生命周期必须由首effect单元补齐，最终RQ/AC不降低。T003其它部分、T011清零、T012最终核验/同工具测量和单条2秒验收未在本Task冒称完成。默认Internal NotSupported旧fake兼容、legacy四参数Context/旧Capture重载、复用的signed DispatchDepth/InlineAsync等在既有过渡行登记T011删除责任。无提交/推送/PR/合并/远端修改/部署/清理；required快审和本地检查点交主控。

- Review attention — scope双故障: body失败先FailAsync等待完整单元并围栏；若随后scope.Dispose也抛错，代码会合并已有cause再调FailAsync、再遍历CommittedParticipants。精确Fence/Discard是幂等保护，但可能再次登记close/log。本Task没有声称避免双次围栏，也未专门实证这个scope双故障组合；请独立快审按真实影响边界核对，不据此扩新框架。已实证的action/commit gate与scoped探针覆盖收口先于正常Dispose。


## Independent Result Quick Review — R1

```yaml
kind: rks-review
verdict: approved
```

Reviewer: fresh independent `b_task04_review`；目标Task契约r3、ResultR1、base26d0e4d1，采用 `reviewing-work` Task Unit/Code/Deliverable。独立只读并复跑validate/selftest，没有重复native测试。206项R1 SHA全匹配，白名单/冻结baseline/Golden及临时变异恢复正确；同一总线、显式请求、earlyoutbox及两首例一致，效果和Main禁用且无旧frame补权。真实completion、完整unit收口先于scope释放、原cause/typed清理错误与全参与者围栏有有效测试。八项目8420=8400+2准确原失败+18原skip，表征10/10、DFO74/7control/原expected/外exeorphan0；六变异及两修正有真实红绿。账仅四行闭合，strict及8坏数据拒绝成立，N55D0准确。未发现阻止提交的局部缺陷。

非阻塞残余：body失败后scope再次释放失败可二次遍历围栏并重复登记close/log。披露准确，当前两生产CMD没有该参与者/资源组合，不阻塞此单元；后续启用相关组合时须重新核对。主控接受此局部残余，保留本条作为后续实际触及时的输入，不扩成额外机制。

主控消费：批准只释放本Task本地检查点。R1 artifact-manifest原样保留，其Task SHA是本审查追加前的执行正文；controller-checkpoint记录其原前缀hash及新增审查后的Task hash，代码/证据R1未改。继续T003剩余Channel，再Login/其它簇；完整Spec、性能、Astra与分支收尾均未完成。
