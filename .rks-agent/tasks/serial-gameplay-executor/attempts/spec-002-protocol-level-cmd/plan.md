---
kind: rks-plan
spec:
  path: ./spec.md
mode: task-units
state: ready
---

# Implementation Plan：协议级 CMD 与显式请求上下文

## Planning Choice

本Plan承接Contract r1及已独立approved的Spec B1（reviews/spec-review.md Round2），baseline为10a7218a779493494b138bd6b43cc0fc1a84aa6a。AC-011的B3文字对齐仅落实该批准已确认的义务。结果A已封存；其资料和接受结论不是本Plan的编辑对象。

采用逐单元规划：必须先落定跨切片共享契约，但后续功能簇的最小实际Task取决于前簇消除的共享依赖、实际事务点和回归结果。下文T-NNN是粗粒度工作包，不是预先分配的Task清单；详细计划只存于主控临近实施时指定的Task Unit。每Task保留独立结果快审；Spec级独立Plan审查及planning checkpoint在产品改动之前完成。本文件ready仅表示规划可供审查，不代表自批或实施完成。

继续同一工作区、同一task/refactor-session-20261021分支；全部本地检查点按根约定带Co-Authored-By。不推送、PR、合并、远端issue、部署或清理工作区，不运行df_game_r/DNF.exe。根六项B条件和A持续不变量按Spec保持。

## Approach and Shared Decisions

### 1. 用已注册入口和真实边界建立迁移账，不拿枚举数量当范围

已核实起点是173条消息注册对应141个handler；其中遥测14条/2类，故待归并为CMD的是159条消息/139个handler，但139不是最终CMD数。消息一对一映射到CMD类型，同一CMD可以明确接收多个消息号。逐项检查ProtocolModule及五处具体扩展注册，不能遍历ClientMessageType后补齐未接线成员。

旧命令204条注册为170 Transactional、21 Direct、9 ReadOnly、4 Workflow，共203个实现类；接线归属为ProtocolModule 163、ApplicationModule 36及具体扩展5（name-tag、expired-item、BossTower、Tau、Southern）。T-002建立B的verification迁移对照：原注册、消息/来源、旧handler/Delivery/Coordinator、实际事务/自开边界、最终CMD与显式单元、去向、证据。170/9仅作查漏基数，不推定每注册只开一次事务；同时查DomainTransactionScope、自开持久写、事件消费与完成标记。每簇把自己的行闭合后再移至下一簇。

另建轻量来源表覆盖连接建立/断开、定时/延时、启动恢复、事件泵/重试、GM/HTTP及测试入口：原生产者、合并键、链/深度、source connection、目标输入、取消/停机/拒绝、最终内部CMD。表是迁移证据，不建立运行时审计框架。A的context-notes.md所更正的FSEvents/并行证据范围继续适用。

### 2. 同一命令模型，入队边界与作业内业务严格分开

保留Application/Commands中的ICommand<TResult>、ICommandHandler<TCommand,TResult>和唯一CommandBus；CLR类型仍是命令身份。增加IProtocolCommand<TResult>这一ICommand的输入形状，携带解包消息与精确NetworkDispatchContext，不创造第二种命令基类、第二总线或反射调用旧handler的执行器。

协议handler只用ICommandBus将本协议CMD入队并等待；可注入ILogger，遥测保留原日志例外。解析字段可在CMD内使用纯payload/帧帮助函数，handler不得查询世界、登记或持有sender/closer。ProtocolModule的注册显式绑定已注册消息、handler、CMD类型和允许的消息CLR形状，启动闭合性与唯一性都由实际注册证明。未注册枚举和未注册帧原语义不改。

CommandBus按注册的Protocol/Internal来源类别创建唯一GameplayWork。在作业body开始时先初始化本作业发件箱，再建立scope及显式CommandExecutionContext，调用同一ICommandHandler。Protocol的精确来源连接写入GameplayWork.SourceConnection；内部来源必须由明确入口提交InternalCommandOrigin（来源种类、根/继承链、取消策略、可选的真实来源连接），不能由网络字段为null推断受信任。注册为Protocol的类型不能用Internal入口执行。Bus所有入口先检查GameplayJob失效令牌/泄漏，再拒绝执行器内再次dispatch；最终不保留InlineAsync细粒度分支或DispatchDepth AsyncLocal。CMD间共享业务是直接调用领域能力，不借bus、queue或委托回调执行另一个CMD。

InternalCommandOrigin是Application/Commands的入口数据，构造/验证集中在bus及具名来源适配器；CMD构造器不可注入它的工厂。各外部producer仅组织已拥有的输入、入队及等待，禁止携带任意修改世界的Func作为新内部CMD的业务body。Direct领域事件订阅仍在当前CMD内直接调用领域能力，不变成嵌套CMD；异步可靠/LiveOnly消费在各自已批准阶段作为内部CMD。

### 3. 显式请求状态和singleton消费链

扩展现有CommandExecutionContext，携带唯一的CommandRequestContext；该对象在作业开始建立、只属于一次CMD，结束即Close。它是显式方法输入，不注册为DI中的当前请求，也不放到GameplayJobSlot、singleton字段或DomainTransactionScope。物理连接、强类型入站元数据、捕获的登录权威标识、精确SelectedSessionRecipient和账本操作句柄由此传递。当前性验证使用现有真源，不把执行开始时的快照当永久授权。

请求状态采用封闭的具名变体：ProtocolNonEffect、ProtocolEffectRejected（未选中，只能原样拒绝）、ProtocolEffectSelected（精确会话/序号/账本）、TrustedInternal。未知、缺应有状态和Closed都抛程序错误；不同变体不能用nullable字段相互降级。TrustedInternal只由已登记内部来源入口建立；作用域结束和泄漏续体没有重新获信任的入口。被封锁/被顶替的协议请求在建立前按A静默拒绝，不创建可结算句柄。ledger仍按精确物理连接和序号/载荷身份判定新请求、已提交重放、确认回滚重试；Pending在结束时转拒绝。重放读取当前投影，不重执行CMD业务。

EffectOperationContext不再是注入singleton的隐式请求服务。其每请求可变状态成为上述显式句柄；EffectOperationLedger及安全真源仍可singleton，但只能以参数操作该句柄。新增Abstractions/Effects的IEffectFootprintRecorder，只表达本次效果的领域足迹（effects、BusinessSlot、租赁/区域等），不含网络发送/查当前请求/开事务能力。EffectExecutionContext增加必需的该能力；HousingOperations、GrowthCapsules、NpcShop、ItemSocket、AvatarCompound、ItemUseEffect及ApplyEffects等所有实际singleton消费链按领域形状参数或该既有context传下去，三处细粒度handler调用同样迁移。不得只把注入改scoped，或从环境事务找回请求。

选中效果请求的recorder验证自身活跃、主单元状态、owner和精确recipient；无选中效果请求的recorder拒绝效果记录/主单元，不能是no-op。非效果/可信内部业务确需无账本足迹时，由已判定来源显式发给NonRequestFootprint；它不是通用可注入默认值，也不能作为缺参fallback。领域API不得optional null静默忽略。两次连续请求、失败后下一次请求、singleton同实例及结束后续体分别验证不串状态。

### 4. 显式工作单元：机制只拥有边界，CMD拥有用例

具名机制接口ICommandWorkUnits放Application/Commands，实现CommandWorkUnits放Application/Transactions。它的WriteAsync/ReadOnlyAsync方法显式接收CommandExecutionContext、单元名/声明及业务delegate；Write声明MainSettlement或普通单元和精确参与者集合，delegate拿到当前单元的RequireRollback能力。它仅管理当前CMD的已有DomainTransactionScope和发件箱unit，不排队、不发送、不选择业务参与者、不访问内容，不替CMD调用另一CMD。

每旧实际Transactional/ReadOnly段对应一个显式调用；Direct/Workflow的领域动作按原阶段保留，不能由全CMD默认事务包裹。主结算由CMD代码声明一次，runner验证报告者包含于参与者、账号/会话一致、逐人DemandCurrent，写开始/Freeze/Settled/Failed/ConfirmedRollback与现有账本顺序等价。只读禁止写，RequireRollback正常拒绝仍真回滚。两单元第二回滚保留第一已提交消息；未完成单元不刷消息，关闭登记继续遵守A单元之外语义。

runner的异常出口分真回滚、提交已确认及不确定：使用事务操作的明确结果/既有typed失败，不用DomainTransaction.IsSettled猜已提交（它在Prepare前就变true）。既有提交后非传输失败和不确定提交按参与者逐一围栏、禁止重结算；原异常保留。配合显式请求结束状态防止业务catch后吞掉保护失败。所有权检查、当前性检查和主单元生命周期保护在runner/显式句柄，不散成每CMD手写一份。

本Plan选择不启用工作单元内延迟组装：新登记契约不暴露GameplayOutbox.ComposeAsync，不在打开的单元内调用该路径；协议/通知的原即时组包顺序和登记单元归属保持，投影按实际原位置迁移。T-002核实生产该延迟API的调用为零，T-011以结构判据守禁用；其机制测试不等于生产启用。F-132记录为该路径禁用/不可达，不称已修。普通已提交后业务失败的围栏/不重结算仍必须验证。若实际切片需要启用延迟组装，先由主控修订此决策及受影响审查，并取得Spec RQ-008要求的持久写→组装失败/取消→重发不重复证据，不能直接在Task里启用。

选角Prepare/DailyRefill/Quests/Pet、发布/PvP和补偿仍是原来独立阶段，专用补偿业务直接成为当前协议CMD内显式动作，不再dispatch补偿CMD。BossTowerPartyCoordination原100～194的塔持久提交→内存队伍更新序列作为不可分割的逻辑段搬入对应CMD，二者之间不新增取消点、await来源或世界复读。MonsterDeath共享发布、祭坛逐参与者及回选精确拆除按原保护范围迁移。

### 5. CMD可注入能力的封闭清单与归属

下表是新CMD的构造器判据清单，T-003实现并给违规反例；不按“任何Interface都允许”或“名字带Outbox即允许”。机制实现可依赖既有基础设施契约，但不能变成任意服务转发门面。

| 可注入能力 | 契约 / 实现归属 | 允许理由及边界 |
|---|---|---|
| *DomainService、*DomainStore | Abstractions各领域 / 原实际状态所有者 | 维持现有类别判定；业务服务调用规则/存储，CMD不绕过它们 |
| IPartyService、TimeProvider | 原契约/实现 | 维持具名队伍聚合及环境时钟例外，不扩成其它Directory/Registry |
| ICommandWorkUnits | Application/Commands / Application/Transactions | 仅本CMD的显式边界，上节职责；拒绝bus/queue/执行器/动态解析入口 |
| ICommandMessageOutbox | Abstractions/Networking / Application/Execution/Outbox | RegisterAsync、RegisterBatchAsync、RegisterExplicitAsync只登记当前CMD；保留原显式收件人失败隔离，不含传输或Resolve。实现复用常设发件箱适配器，不能另造消息队列 |
| IGameCommandOutbox | Protocol/Game / Protocol/Game | RegisterCmdAsync、RegisterNotiAsync及typed batch/explicit；适配前项，保Game CMD/NOTI类型约束。Application不知道这些协议类型 |
| ICommandConnectionClosures | Abstractions/Networking / Application/Execution/Outbox | RequestCloseAsync只登记精确关闭；维持close总执行、链原因与F-131现状，不直接断socket |
| ICommandProjections | Abstractions/DomainEvents / Application/DomainEvents | 仅ProjectAsync并返回登记结果；EventService同一实例实现此窄契约，不暴露Enqueue/Direct或重建第二事件系统。领域事实仍由实际领域服务记录 |
| IOutboundOrdering | 既有Abstractions/Execution / Application发件箱 | 仅显式跨收件人顺序组作用域；不改变Required/BestEffort/逐连接语义 |
| IInventorySlotMappingRuleService | 既有Abstractions/Inventory / 原实现 | 仅协议槽位与BusinessSlot的固定地址换算这一既有例外，不允许任意规则服务 |
| ILogger<T> | 原日志框架 | 业务拒绝/producer降级等原级别；不可重复记录执行器已负责的异常Error |

INetworkMessageSender、IGameMessageSender、INetworkConnectionCloser等旧sender/closer契约即使DI当前映射到发件箱，也不允许注入新CMD；只登记的新契约代替CMD的旧用法。底层encoder/transport/observer真正需要的sender仍按职责保留，最终不能把整个传输接口重命名为Outbox冒充完成。

纯协议key算法改为使用已允许TimeProvider的纯辅助调用；LOGIN端点/频道事实由真正拥有频道解析与入场决策的IChannelEntryDomainService收口（Abstractions/Channel契约、Application/Channel实现，吸收原ChannelEntryPolicy的实际职责及typed部署端点快照），不在CMD注入IServerConfigService或内容源，不造只转发任意GetConfig的假DomainService。规则输入仍由所属规则服务拥有，Protocol只组帧。所有新增类型遵守Application→Abstractions/Data及Protocol→Application方向；Application不引用Protocol/Networking。

### 6. 可执行的渐进兼容边界

不把所有协议一次切换。MessageHandlerRegistry临时增加LegacyQueued / ProtocolCommand两种注册路由：前者完全保持A的dispatcher入队及旧handler→旧bus内联路径；后者dispatcher只调用薄handler，由CommandBus创建那一个作业。新路径不先包inbound job再等待bus，否则自等；旧路径不误入新bus再排队。遥测单独保留，不通过这两个业务路由获信任。

CommandHandlerRegistry同一模型临时区分LegacyFineGrained与Protocol/Internal整CMD。旧bus内联仅允许legacy路由；新整CMD在executor中调用任一bus入口均拒绝，包括试图转发旧细粒度命令。其真实共用逻辑先抽为具名应用/领域能力，由新旧调用点直接使用。所有临时分支和注册计入迁移账，T-011删净，而非长期兼容功能。

显式领域参数的跨切片迁移采用有界双入口：新入口必需显式recorder；尚未迁移的旧签名及旧请求适配只允许legacy路径。先迁实际singleton消费方到参数式核心；临时LegacyRequestAdapter只在旧bus/旧调用者的边界将已有旧frame转为显式参数，经CommandExecutionContext和领域形状参数传下去，旧领域方法签名不保留自行取ambient的重载。新CMD不能调用该adapter；参数式核心不注入旧context、scoped请求或ServiceProvider，也不新增ambient或job-slot请求。若同一服务被新旧簇共享，其legacy调用者adapter必须和消费方清单一并登记，并用新CMD调用拒绝/结构检查防止回落；最后一个旧调用者迁完当单元就删除。这样不要求每簇重写整个底层，但显式路径不会借旧ambient获得授权。

每簇必须连同共享Delivery/Coordinator的bus依赖一起迁移：先列其所有调用者，搬用例分支/提交/补偿进CMD；抽出的共用业务按真实领域所有者归属，纯帧辅助保留。若共享帮助器仍调用bus，则该簇尚未闭合，不能用新CMD→旧帮助器→bus完成。PartyDeliveryStream、DungeonRunDeliveryRegistry等真实状态所有者不按名字删除；GameInventory/GameInstance/GameExperience等纯帧辅助可保持。

T6以同一CommandBus作业入口初始化GameplayOutboxes.Current保证，而不是给每个handler手摸Current；临时legacy入口也调用同一窄的作业启动实现。初始化发生在身份/handler/ledger可能抛出之前，故零条目失败仍先按已有发件箱保留顺序再关真实来源。两个dispatcher手工触碰删除后有真实空条目失败回归。该依赖在Application的bus/入口实现，不给GameplayExecutor构造器注入GameplayOutboxes，避免executor→outbox→sender→executor的DI环；无sender测试容器继续允许丢弃登记结果。记录源连接与目标会话彼此独立，不能从recipient推导SourceConnection。

### 7. F-07：扫描、生产、子CMD与登记水位

点名日切/配额刷新都用同一边界：扫描内部CMD在executor读取当前周期及精确SelectedSessionIdentity清单，只返回不可变(period, session IDs)；不在返回输入夹带当前角色/配额快照。producer在executor外仅按清单依次提交并等待每session刷新CMD，不插队/预留，不修改水位/世界。每次子CMD最多处理一个精确session，以实际FIFO位置重新读当前周期、会话与对应领域事实，登记本session原顺序的条目。扫描/子CMD均为Timer或原内部来源，SourceConnection保持原rootless；目标session不自动成为失败来源，原出站失败关闭规则不变。

Abstractions/Sessions新增ISelectedSessionRefreshDomainService，Application/Sessions实现，按刷新主题+精确session持有该主题原有的“已接受登记”去重状态，不能统一固定成topic/session/period三元组。它拥有NeedsRefresh/MarkRegistered/Prune这组主题状态操作，所有调用在executor；周期从所属日切规则服务取得，不由CMD直注规则服务。配额主题仍只按原period去重，不将QuotaRemaining等当前余额新增为fingerprint。Premium沿用完整(Period, Active权益fingerprint, CrystalActive, GrowthActive)，保留既有Active的规范化/排序及BlackDiamond默认表示、前次激活flag与是否已有状态；同周期新增/变更/到期仍可刷新。尤其水晶/成长由前次激活转到期时，按原顺序登记清零NOTI再登记状态；首轮已到期但仍有对应权益记录的清零路径也保留，后续相同状态不重复。

Protocol的各主题CMD通过该服务检查周期/精确身份，并通过所属领域服务重新读取当前事实、形成该主题去重状态；比较时使用前次完整状态，不由扫描或producer携带旧权益快照。按原投影链登记后拿到实际登记结果，再仅在period仍匹配、精确session仍current且该结果表示接受登记时MarkRegistered并保存本次主题状态；失败/旧周期/旧session不推进。扫描按当前精确身份修剪失效状态；MarkRegistered对相同主题/精确session/完整主题状态幂等，不能禁止同周期的真实Premium变化。登记状态不承诺落线送达，连接已不在的原登记结果语义按A保留。

父扫描和子CMD使用同一来源链的既有继承规则；生产者不在CMD里等待下一CMD。来源的合并覆盖整个“扫描+逐session提交/等待”轮次（producer的普通运行状态即可，不保存世界数据），不得在只完成扫描时就释放busy而并发启动第二轮；busy期间新tick沿原合并语义合并。正常轮次结束释放，失败逐目标隔离及producer原Warning/取消策略保留；原先由刷新业务捕获的逐目标错误仍在子CMD内按原类别处理，不把普通失败结果人为改为未处理异常增加Error；停机已入队工作按A排空/取消策略执行，不用新的全局取消让队列失真。跨周期的旧子CMD拒绝登记/推进；正常周期timer继续下一轮，下一次扫描覆盖新周期，不能把旧周期结果改名为新周期已送。手动Refresh入口遵守相同轮次边界。

这明确接受VC-B3：排在实际子CMD前的请求可先响应，多个刷新CMD之间可插入请求；单个子CMD内不交错，单连接条目顺序保持。防重复沿各主题原有登记状态，不依靠队列插队或原整批锁；临近Task可细化主题状态的本地类型/返回值，不能退化成统一周期去重或新增配额余额指纹。测试必须插入真实排队请求、队列跨周期、session退出/替换和重复tick，证明旧输入无登记/水位、新扫描补新周期，并区分Premium同周期真变化与相同状态重复。

## Work and Dependencies

| 包 | 有用结果与责任边界 | 真实依赖 / 拆分理由 | 覆盖 |
|---|---|---|---|
| T-001 首单元有界修正 | App25文件按拥有的规范化写/只读池清理；F-127真实传输失败只调整gone日志分类，含普通故障对照 | 仅已批准Plan及checkpoint；不依赖CMD迁移，先稳定清理与失败日志基线。一个首Task含两项，但实现与验证证据分开 | RQ-010/011，AC-006；F-130/F-127，VC-B1 |
| T-002 保护与迁移账 | 冻结注册/来源/实际事务点清单；F-88/F-93四项先红守护；建立可执行不变量对照，核实F-132延迟路径禁用 | T-001结果；先有保护再搬保护，清单不是详写远期Task。保护可按即将迁移簇物化，但四项不得晚于对应搬迁 | RQ-001～008/013～015，AC-001～005/008；F-12/F-34/F-88/F-93/F-112/F-132 |
| T-003 共享执行骨干与简单协议先行 | 同一命令模型、新路由/请求状态、runner、typed登记接口及注入反例；用简单单命令/单响应协议验证贯通，再迁Channel/登录/名册等适合的简单簇 | T-002的清单/相关保护；骨干不能靠空壳编译算完成，至少一条真实入站路径和内部来源验证。跨切片接口与可见行为分Task按test-value gate处理 | RQ-001～009/012/014，AC-001/002/004/005/007；F-58/F-110，VC-B2 |
| T-004 物品与效果簇 | 物品/槽位/效果协议CMD、显式footprint从singleton传到底、主结算/重放、F-122登记结果全消费点改名 | T-003；按实际共享领域服务和通知依赖选择最小簇，不容新CMD回调legacybus。覆盖旧三个细粒度replay调用 | RQ-001/003～009/013～015，AC-001～005/008；F-34/F-112/F-122 |
| T-005 邮箱、市场、租赁簇 | 协议CMD吸收交易与投影编排，逐提交点及拒绝回滚；清除所触共享帮助器bus依赖 | T-004稳定的效果/登记接口；交易簇用真实持久化/同容器发件箱证明，不复制旧handler进代理 | RQ-001/003/006～009/013～015，AC-001～005/008；F-12/F-112 |
| T-006 队伍簇 | 队伍协议、显式收件人顺序组、共享Delivery/Coordinator解耦；真实状态所有者保留 | T-005及共享业务抽取；先稳定队伍结果/投影，再迁世界/副本依赖 | RQ-001/003/006/007/009/014，AC-001/003～005；F-34 |
| T-007 城镇与副本簇 | Town/Dungeon/MonsterDeath等CMD；F-86卡牌重试裁定；F-109无语义参数收口，全部参与者围栏不缩减 | T-006、对应F-88/F-93保护；按实际领域事务及共享顺序链继续分最小Task，保#185判定 | RQ-001/003～009/013/014，AC-001～005/008；F-86/F-88/F-93/F-109 |
| T-008 两塔及特殊玩法簇 | 两塔、祭坛、Joust、婚姻等CMD，保提交→内存临界序列/逐参与者围栏 | T-007；沿父T008既定顺序两塔→祭坛/Joust/婚姻，只有真实共享依赖才调整局部先后并记账 | RQ-001/003～009/013～015，AC-001～005/008；F-12/F-34/F-112 |
| T-009 选角与回选簇 | 整个多阶段协议CMD，阶段事务/补偿和精确关闭、PvP发布归属守护；消除共享生命周期帮助器的bus编排 | T-008及DiscardExact等先行保护；阶段不并事务，gate/旧scope职责有明确替代，失败关闭与F-131保留精度复核 | RQ-001/003～009/012/013，AC-001～005/007/008；F-88/F-93/F-124/F-131 |
| T-010 非协议来源与日切拆分 | 生命周期、Timer/delay、事件/重试、GM/HTTP、启动全部typed内部CMD；F-07扫描/子CMD/主题去重状态，配额保period、Premium保完整fingerprint/前次激活flag及同周期清零；Reliable/LiveOnly边界逐项对应 | T-003能力可供提前安全迁移，整体收口在T-009后；F-07需相关投影/领域簇已迁。每来源合并/取消/链保持，Direct订阅不排队 | RQ-002/004～009/013/014，AC-001～005/008；F-07/F-58/F-110/F-112/F-131，VC-B3 |
| T-011 删除旧模型与更新最终判据 | 全部注册行/来源行闭合后删legacy路由/重载、细粒度处理器、旧pipeline/marker/请求ambient、重复Error；删T6并验证空条目失败关闭；补Channel降级保护 | T-004～010全覆盖，T-003的统一入口；不能提前删仍消费的保护。重写结构判据保违规反例，实际职责与检索双清零 | RQ-001～009/012～015，AC-001～005/007～009；T1～T6、F-58/F-110/F-129，VC-B2 |
| T-012 整体核验、测量与交付 | 最终八项目构建/测试、表征/线级/codec/Golden/同工具取证和测量；最终架构/新增协议文档、登录MId/Ticket例子、18项去向和报告 | 所有迁移/删除闭合且相应局部检查已过；耗时与数量账从每Task累积，最终冻结后统一取证/测量，不把测量前基线冒称结果 | RQ-013～017，AC-008～011；F-69/F-128①/无编号扇出事项，六根SC |

上表路线沿父T-008的功能簇次序渐进展开。后簇并非必须一次写成一Task；相邻共享业务若必须同改可合成有用单元，并记录消除的实际依赖。不得为了追求并发同时迁同一协议/领域footprint核心。T-002和T-011是前后证据边界，不能把全部风险保护拖到最终检查。

### 交接闭合索引

| 交接 | 包与可判定结果 |
|---|---|
| F-07 | T-010：VC-B3、各主题原有去重状态/周期/精确session；真实排队与Premium同周期变化/到期清零区分；详见共享决策7 |
| F-12、F-34 | T-002建账，T-003～010逐行工作单元/协议归并，T-011无旧转发 |
| F-58 | T-003/010/011：请求Close、leak先拒绝、Internal入口不能误获信任 |
| F-69 | 每Task记时间/环境，T-012按相同口径对比；异常增长做排空诊断，不改接受线 |
| F-86 | T-007先核实卡牌3次预留重试的真实冲突来源；无业务依据删除，有依据则独立于旧锁说明并取主控裁定 |
| F-88、F-93 | T-002四个反例在对应T-003～009搬迁前；RecoverStamina.IsInWorld、Teleport.Owns(previous)、SelectedSessionSafety.DiscardExact、EffectOperationLedger.DemandCurrent(recipient)分别受保护 |
| F-109 | T-007 representedSessions仅无语义部分删除，保刷新结果不复活门 |
| F-110 | T-003/010/011结构保证早于删除T6，空条目失败也关闭真实source |
| F-112 | T-004～010同一DI容器、同一实际工作单元和发件箱，真实持久化与刷出断言 |
| F-122 | T-004将NotificationOutcome.Delivered改为Registered，语义仍接受登记；全消费点/文档同步，GM attempted/offline分支和“离线丢弃”不变 |
| F-124 | T-009以确切PvP发布事件/notification类型与所需处理者注册配对，并用缺失/错绑反例证明；不能以任意handler存在代替归属 |
| F-127、F-130 | T-001及AC-006，首Task关闭；不宣称解决所有SQLite偶发 |
| F-129 | T-011前或Channel迁移当单元补真实入站业务失败的Warning/Information降级区分，恢复中间件Error会红 |
| F-131 | T-009/010/011复核关闭cause；明确保留“已登记但未执行关闭遇传输先关仍继承链”精度，T-012披露给Astra，不写已修 |
| F-132 | T-002/003/011落实本Plan禁用事务内延迟组装决定，T-012列未启用；不豁免其它提交后失败保护 |
| F-128①、无编号 | T-012按真实MId/Ticket及新CMD写登录例子；查扇出变长字段编码上限/来源约束，给不需或需守卫的依据，必要守卫同Task验证，不能猜改线上字段 |

18个编号项按Contract的11+4+3口径；F-128①与无编号另列。根CH-1/CH-2仅在实际触及时有界处理记录，不能把改归属记已修。全部RQ与AC由上表覆盖；SC-2对应T-003～011，SC-8B对应T-001～012，SC-9B对应T-012测量，SC-10B/11B对应T-011/012，SC-13对应T-002～011。

## Verification

### 每单元：保护风险并保留数量账

每Task在详细计划中按test-value gate选择真实可观察行为RED/GREEN；结构搬迁的编译错误不是RED，不要求给纯重命名/机械签名改造造行为测试。架构判据必须有能被拒绝的坏例：handler注入/业务、注册缺失/多义、CMD注入sender/queue/locator、CMD调用CMD、无/旧请求状态、禁止的延迟组装入口。范围按所改簇和共享接口选本地回归，跨切片核心变更适当扩大至相关项目/全量，不强制每个小Task跑八项目。

每Task结果记录：代码与文档范围；原有断言保留或有意变化的依据；新增/删除/合并测试的加法；只改测试驱动/准备与改写断言分别列；实际命令/日志路径、耗时与环境；结果独立快审。数量以A的8350起算并逐Task累积，不能以“约139CMD”推导测试数量。失败先按当前AGENTS身份规则诊断，不用放宽类并行、超时、deadline或Skip求绿。

共享核心回归至少覆盖：三协议状态与内部入口；连续请求singleton足迹隔离；一次主结算/重复主结算/序号前进/已提交重放/Pending结束；非法结束续体；多参与者失败围栏；只读/RequireRollback/两段第二回滚；普通提交后失败不重结算；source空条目失败关闭、rootless目标不误关闭；CMD/NOTI类型与显式收件人顺序。F-88/F-93四项修改/临时去掉保护应变红，随后恢复。Reliable业务+完成标记同事务，LiveOnly消费提交及外部投影原先后，事件Direct继承当前单元。

F-07在T-010验证各主题差异：配额同周期余额变化不新增刷新；Premium同周期权益新增/有效权益fingerprint变化应刷新，水晶/成长激活→到期应按原水晶清零NOTI、成长清零NOTI、状态帧的适用顺序登记并更新完整去重状态，下一轮相同状态不再登记。还覆盖首轮已到期权益的清零、跨周期旧输入无登记/水位、session退出/替换、请求插入FIFO及下一扫描补当前周期。不得用仅跨周期/重复tick通过替代同周期真变化证据。

首Task的F-127使用真实transport两条路径（真实关闭期间、仅真实Writer完成且token未cancel），确认Resolve仍可解析且返回Failed；异常原对象、关闭/observer、队列其余条目与其它连接保持，普通故障仍Warning。HasClosedSendSide等细节在Task中选定并RED/GREEN验证，不能把本Plan的候选探针当已实跑证据。F-130只App25文件；已有fixture完整释放后按它实际拥有的写/只读规范化连接串各ClearPool，保持失败清理语义，ClearPool异常不吞并为文件删除异常。两个自有Path都覆盖、非拥有Reopen不越权，混合行尾文件做字节级定点修改。

### 最终集成：新鲜完整证据而非迁移次数

T-012在冻结状态运行完整Release构建，0警告0错误；八项目全量（Application、Protocol、Networking、Persistence、Infrastructure、Host、HttpApi、Pvf），保持原xUnit类并行。允许-m:1串行项目，不把程序集串行作为隔离修复。正式dotnet全量在本机FSEvents可用的require_escalated环境执行，保留原deadline，DFO_GAMEPLAY_ASSERTIONS和console detailed完整落盘，环境/命令/退出码及数量同档。sandbox watcher停滞不能解释成业务回归或冒称全部managed根因已证。

8350基线为8330通过、2个已披露DimensionDungeon内容失败、18跳过；表征10/0。保留两条内容失败准确身份及当前AGENTS允许已登记偶发的证明规则，新增失败不能混入基线。逐项目数量与总数按Task增减闭合，原codec/Golden字节/帧序/连接开关断言保持，只有VC-B1/B2/B3明确允许相对A变化。

同A工具重跑执行器外写/网络等待/自等/泄漏取证，七程序集正对照齐，反例类/对照外0；保留exe0仅为观测而非所有吞错证明的边界。旧机制检索包含六处请求AsyncLocal/旧marker/旧pipeline/legacy路由和重载、bus包装、旧重复Error、两个手工Current；生产AsyncLocal最后只允许GameplayJob.Running与DomainTransactionScope.ambient。把命中按实际职责分类，不能只grep类名为零。

最终同A的工具、负载、口径与环境再测执行分位/max、吞吐/等待、并发登录/灌包、冷启；每条CMD max≤2秒即接受。原目标作对照优化，数量级异常按根约定报告；套件总时长/内存只记录。报告合成/非A21实机边界。若测量后再改影响执行的产品，受影响检查/测量需重跑。

最终文档更新AGENTS与架构哲学/新增协议步骤、真实分层及typed登记/单元用法、F-128①。报告包括六根条件、17RQ/11AC与18交接去向、VC-B1/B2/B3、F-131保留及F-132禁用、基线/遗留、测量证据、Astra待审及不推送/PR状态。A封存文件不修改；B最终核验/封存由主控按工作流推进，本Plan不自行宣告完成。

## Next Planning Boundary

下一Useful Unit固定为T-001中的首Task：App25文件F-130自有写/只读池清理，加F-127已失败发送的gone日志分类。前提是当前Plan独立approved及planning checkpoint，精确Task路径由主控提供；本次不创建Task。Task planner消费research/first-unit-facts.md中的只读源码定位与真实transport候选，但必须通过真实RED/GREEN验证，保Resolve/Failed/observer语义。

首Task结束并独立快审后，以清池检索、两类失败日志对照及数量账为已验证输入，细化T-002的保护/清单与T-003首条贯通单元。后续每次只规划能消除实际依赖的最小有用单元，先读已闭合行及尚未迁调用者再选边界；重大共享决策变更、漏注册/事务点、行为超出VC表或长时间偏离目标触发主控的on-drift复核/受影响Plan评审，不在Task中默改中心方案。没有待用户裁定的共享接口或下一安全步骤阻塞。

## 本地修订说明

P1（2026-10-24，首次Plan评审前）：按独立reviews/plan-challenges.md的CH-1及PremiumDailyRefreshService现有去重/清零实现，定点更正F-07共享状态、T-010和对应验证。配额保持原周期状态，Premium保持完整权益fingerprint与前次激活flag；扫描仅周期/精确session、子CMD重读事实及VC-B3保持。不引入新可见变化，不修改Spec或immutable挑战；本ready目标仍须首轮独立Plan评审。
