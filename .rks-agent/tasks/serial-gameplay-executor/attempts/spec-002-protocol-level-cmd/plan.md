---
kind: rks-plan
spec:
  path: ./spec.md
mode: task-units
state: ready
---

# Implementation Plan：协议级 CMD 与显式请求上下文

## Planning Choice

本 Plan 当前为 **P9 ready R2**，承接待独立审查的 **Spec B7 ready R1**。TASK-45核实：普通击杀成功投影之后原地唤醒AltarScheduler，但现有CMD封闭注入清单缺少该纯调度信号；移到薄handler会越过当前作业／scope边界。P9只补`IAltarSchedulerWakeSignal.Wake()`这一具名技术能力，复用原同一个AltarWakeSignal及其Channel；不新增事件系统、命令入队能力或领域服务转发门面。它跨T-007／T-008／T-010消费，故按process-reset §4做一次有界Plan补充。B7仅明确RQ-009的祭坛纯唤醒类别及AC-001对应验收，其他RQ／AC、根／子契约、中心方案、功能簇顺序不变；同TASK-45唯一独立Astra先审B7、通过后审本增量，最后审Result，无挑战。主控消费及规划检查点前不实施该接口／白名单增量。

此前 **P8 ready R1** 已获批准。TASK-39 旧路径表征已通过；接入真实显式工作单元时发现，现有 `NonRequest` 只允许一个入站 actor，不能表达两塔离队对其他队员的效果调用。P8 仅补共享显式足迹的多 owner 声明，保留原事务点和独立的失败保护参与者集合；功能簇次序、Spec B6 及已追加变化表、根／子契约和中心方案不变。依据和详细执行范围见 TASK-39 下方的边界裁定。按 process-reset §4，仅重复受影响的独立 Plan review，无挑战；审查和主控消费前不实施该机制增量。以下 P7 段落保留为此前已接受规划的历史说明。

本Plan P7 ready R1承接Contract r1及fresh independent approved的Spec B6（reviews/spec-b6-review.md；匹配immutable reviews/spec-b6-challenges.md high0/low0），主控已在verification/spec-b6-plan-p7-controller-consumption.md消费Spec门。baseline仍为10a7218a779493494b138bd6b43cc0fc1a84aa6a，当前HEAD为TASK-32检查点17d924a06b1237ca2b4f5997983c0a4923dfc050。Task31已交Mailbox，Task32只交四remaining opening与Rental旧边界调查；T-004/T-005仍未全闭。本次落实B6 HOW：先单独修旧正常确认回滚记账/NoRoom，再逐具名补旧encoder oracle并迁四opening及Rental whole，保各自恢复/提交点和显式足迹，明列whole scope/独立刷新默认保护。现十个WriteResult消费者/原三个raw类型及其他owner、Mailbox唯一self能力、默认sticky、Compose禁用/F-132保持。依据和原P6逐bytes快照见verification/plan-p7-amendment.md及verification/evidence/plan-p7-amendment/plan-p6-before.md。17RQ/11AC、根/child、A、task-units/on-drift及T-006～012责任不变；VC-B6由approved B6拥有。本ready仅表示高层路线可审，须本P7 fresh challenge、另一fresh独立Plan review、主控消费及amendment-only本地检查点后，才详细规划下一Task；writer不自批、不改routing、不建Task、不运行native或写产品/正式测试。

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

新旧效果入口共用EffectOperationLedger原唯一ConditionalWeakTable及账本身份，不创建第二ledger。抽取纯admission只判定并返回具名结果/原entry，不调用CMD、delivery或建立legacy ambient；旧Dispatch临时消费同一admission并承接其旧执行路径，新CMD直接消费显式Selected/Rejected/replay结果与本次recorder。ConfirmedRollback重试仍复用原entry，保持原entry.Ordinal、不以本次重试ordinal延长其窗口；LatestOrdinal推进、128窗口、原raw身份冲突和Pending结束拒绝保持。新入口不得借旧Dispatch/EffectOperationContext/NetworkCommandAuthorization读取或授权，旧入口待最后消费者迁完删除。

新ledger、reader及sink全链捕获精确recipient时显式传入本次Game metadata或经来源判定的null，或直接传递既有显式recipient；不能调用会隐式读NetworkCommandAuthorization.Metadata的SelectedSessionSafety.Capture(session)。特别是ReplayStateDomainService.ReadAsync内部现有旧Capture也须贯通显式输入，不能只改新bus/sink的外层。仍被旧route消费的具名LegacyReplay适配入口可暂将原输入显式化，登记实际调用者/去向后在最后消费者迁完时删净，最迟T-011清零；新CMD与参数式reader不能经它回落ambient。

EffectOperationContext不再是注入singleton的隐式请求服务。其每请求可变状态成为上述显式句柄；EffectOperationLedger及安全真源仍可singleton，但只能以参数操作该句柄。新增Abstractions/Effects的IEffectFootprintRecorder，只表达本次效果的领域足迹（effects、BusinessSlot、租赁/区域等），不含网络发送/查当前请求/开事务能力。EffectExecutionContext增加必需的该能力；HousingOperations、GrowthCapsules、NpcShop、ItemSocket、AvatarCompound、ItemUseEffect及ApplyEffects等所有实际singleton消费链按领域形状参数或该既有context传下去，三处细粒度handler调用同样迁移。不得只把注入改scoped，或从环境事务找回请求。

选中效果请求的recorder验证自身活跃、主单元状态、owner和精确recipient；无选中效果请求的recorder拒绝效果记录/主单元，不能是no-op。非效果/可信内部业务确需无账本足迹时，由已判定来源显式发给NonRequestFootprint；它不是通用可注入默认值，也不能作为缺参fallback。领域API不得optional null静默忽略。两次连续请求、失败后下一次请求、singleton同实例及结束后续体分别验证不串状态。

### 4. 显式工作单元：机制只拥有边界，CMD拥有用例

具名机制接口ICommandWorkUnits放Application/Commands，实现CommandWorkUnits放Application/Transactions。它的WriteAsync/ReadOnlyAsync方法显式接收CommandExecutionContext、单元名/声明及业务delegate；Write声明MainSettlement或普通单元和精确参与者集合，delegate拿到当前单元的RequireRollback能力。它仅管理当前CMD的已有DomainTransactionScope和发件箱unit，不排队、不发送、不选择业务参与者、不访问内容，不替CMD调用另一CMD。

每旧实际Transactional/ReadOnly段对应一个显式调用；Direct/Workflow的领域动作按原阶段保留，不能由全CMD默认事务包裹。主结算由CMD代码声明一次，runner验证报告者包含于参与者、账号/会话一致、逐人DemandCurrent，写开始/Freeze/Settled/Failed/ConfirmedRollback与现有账本顺序等价。只读禁止写，RequireRollback正常拒绝仍真回滚。两单元第二回滚保留第一已提交消息；未完成单元不刷消息，关闭登记继续遵守A单元之外语义。

**P3的有界commit结果入口。** 现WriteAsync/ReadOnlyAsync所有异常sticky的默认契约及原坏例保持；不新增通用action异常恢复、clearFailure或catch后复活请求。为PackageOpening、AvatarPackageOpening、MagicBoxSingleOpening、MagicBoxBatchOpening旧fine外catch保留响应，ICommandWorkUnits增加具名写结果方法（例如WriteResultAsync，最终API名在临近Task收敛），仍显式接收本次context、单元声明和action。它返回封闭的单元值：正常`Returned<T>`包含业务正常返回及RequireRollback正常拒绝，不能命名为Committed而误称每次都持久提交；仅下述机制事实成立才返回`CommitRolledBack(cause[, CommitAttempt])`，该变体只能由runner建立，调用方不能自报已回滚或签发它。结果只是已完成单元的值，不是新授权/许可/多层receipt框架，不在Application下沉协议错误码或Opening业务类型。

新恢复span严格限runner的`transaction.CommitAsync`调用本身，含其中Prepare和唯一fallible Commit；不是包住整个RunAsync的catch。只有此span抛出的原cause非OperationCanceledException、不ContainsUncertain，实际lease.Completion为ConfirmedRollback，请求/job仍健康、没有已记录guard/登记等sticky failure、current及全部精确participants仍有效，且该unit/lease全部cleanup成功，才能给CommitRolledBack。默认入口仍将相同错误外抛并保持sticky；新入口也不按异常类/文字、IsSettled或业务自报判定回滚。runner前后current/participants、Begin/Main/Footprint/Freeze/非法生命周期等自身守卫，已失败/结束/泄漏请求，unknown/Uncertain、rollback/lease cleanup失败、非fallible违约、CompletionCommitted（含durable后组装错误）都不能转此值，仍沿原sticky/typed/fence规则；CommandBus额外scope失败保持原最终保护，不能被已返回的unit值消除。commit期间触发已有sticky guard也不因ConfirmedRollback被清除。

返回判定在该unit/lease清理完成之后；tracked operation覆盖完整action、提交和cleanup，只有真正返回上述值才正常完成，不能先返回“回滚成功”再由finally或drain补报失败。保留原cause引用和真实阶段，CommitAttempt仅表示在CommitAsync调用内，不伪造已知Prepare或SQLite Commit精细阶段。Main此时仍ConfirmedRollback，不作Settled/Committed，不清main claim；Freeze之后projection可能仍非null，不能为统一断言强清或拿它当提交证明。下一次ledger admission才按原逻辑复用entry、重置Pending/清projection，并保持原entry.Ordinal；当前CMD不能凭结果再claim主结算。

**四opening的action映射由各whole拥有。** 原domain／OpeningWireSlots／clock调用的窄业务try内，按旧过滤捕获OpeningRejected及原普通非取消、非uncertain故障，保存本次local异常、调用unit.RequireRollback并正常返回动作失败结果；纯业务mapper放在单元之外，将所捕获原因映射为原领域拒绝/StorageFailure、日志和失败ACK。RequireRollback自身的request/current守卫仍可能拒绝，不能被同一catch吞掉；已有sticky guard/登记失败不因业务catch或结果值恢复。outbox/Project/encoder及runner自身守卫不纳入此业务try。若action正常返回拒绝且已RequireRollback，结果为Returned<T>而非CommitRolledBack；若action成功而CommitAsync确认回滚，whole才在单元外按commit结果中的原cause应用同样旧响应映射。两路均在单元完全结束后登记原响应；取消/typed不确定和新失败继续原出口。四opening没有Mailbox.RunAsync式自Rollback；其既有入口不扩张，Mailbox只走下述P6独立入口，不借commit来源冒充action来源。

**P5仅给Cera增加既有结果入口的具名消费者。** 已落地的`ICommandWorkUnits.WriteResultAsync`不需要为Cera新增API或修改runner。原Cera fine只有一次Transactional；whole对应一次Ordinary write，显式声明`CommandFootprintMode.NonRequest`和仅一个`SelectedSessionRecipient`买家，包含本次原session、原物理`Dispatch.Connection`及本次Game metadata。Cera领域入口和写前都调用DemandEffects，故不能用无足迹Write、空participants、Main或LegacyFootprint替代；只在本次健康action内传`unit.Footprint`给原`ICeraShopDomainService.PurchaseAsync`，不注入当前request、不从ambient补请求，也不增加Main/ledger admission/同序号幂等。原缺session/online直接拒绝路径不进入写单元；正常业务返回拒绝保原结果和空提交事实，不为统一写法RequireRollback。原session/ownership等领域拒绝的强用例保留，不能用actor装配将原普通拒绝无说明地改成异常。

**Cera窄业务映射与确认回滚分开。** 单元声明、actor/current/footprint取得和runner守卫放在业务try之外；try只包原clock/领域调用。按原类别把CeraShopPurchaseRejected及普通非取消、非typed不确定动作异常保存为本次local结果，调用`unit.RequireRollback(cause)`后正常返回，再于单元完整结束后映原reason/detail、StorageFailure及原日志。RequireRollback自己的request/current/typed守卫不能被该catch吞掉，已sticky的业务／登记／guard错误不能恢复。原动作正常返回拒绝不调用RequireRollback。Commit阶段沿现有真实runner窄span，仅`KnownStorage`来源事实、cause同对象、非取消/非uncertain、真实CompletionConfirmedRollback、无GuardFailure/已有request failure、当前买家仍有效且全部unit/lease清理成功才返回`CommitRolledBack`；whole在单元外按其原cause映相同拒绝。不能把Prepare中的任意受控业务／程序守卫仅凭回滚成功都当KnownStorage，不能以IsSettled、文字或业务自报恢复；已有四opening入口和默认Write/ReadOnly的判据不改。Cera领域不自行Rollback，因此无需Mailbox式已结算恢复或第二事务。

**Cera投影和VC-B4默认失败。** 原cart日志、session/online判定、receipt/refusal日志、错误码（NoRoom4、InsufficientFunds11、其余1）、`CeraShopPurchaseCompleted`及可选NameTag可见会话刷新都进唯一whole；原通知handler及其扩容快照→产品ACK→wallet→可选gold→inventory→pet→restriction→premium/state次序保留，两次Project的受众／顺序也不改。handler只入队，旧fine及其注册/所有生产转发删净；共享业务仍由原领域服务拥有。新whole只用ICommandProjections和IGameCommandOutbox，不注入旧sender/IEventService或Nested bus，不给Cera声明任何raw出口。NonRequest actor在真实Committed/Uncertain时由现有runner记录；发生默认不可恢复错误后，让原CommandBus drain→Close request→scope cleanup→FailAsync的最终收口执行精确Fence、物理连接阻断、DiscardExact及关闭，保cause和独立错误事实。不能为保旧空selected集合削弱足迹，也不能每次普通Commit成功就Fence。成功、正常拒绝及健康清理完成的确认回滚不新增Fence/close；若拒绝后又发生独立登记/清理错误，那已不是正常拒绝成功结束，按实际默认条件保护。

C1非fallible违约、C2动作SQL加Rollback第二cause不进入普通拒绝映射；C3从旧fine scope映到新whole的真实DI scope，用测试侧decorator让原scope实际解析并Dispose受控资源，不在body手抛或手动Dispose冒scope证据。body正常返回前可能已登记success或正常拒绝帧，现有outbox按单元状态保留已提交及单元外条目，按原序刷出后关闭失败来源；不因C3追加StorageFailure ACK、不撤条目/提前flush或承诺零帧。保护仅限本次精确买家及物理来源；替换后的session/runtime和无关玩家不被误围栏、丢弃或关闭，既有关连接生命周期的正常投影不在“误伤”之列。具体whole结果形状、探针/测试路径及局部fixture接法留临近Task；若实际实现要求扩大机制、恢复来源或VC边界，先返主控，不在Task暗扩。

**P6来源与独立契约。** 原Mailbox action在RunAsync内部真实Rollback，返回错误时Current=null；不延迟RequireRollback、不复活settled事务、不第二事务，standalone/ambient caught refusal后Commit必throw及原IsSettled防护保持。给既有ICommandWorkUnits增加具名`WriteSelfSettledResultAsync<T>`，独立封闭`CommandSelfSettledWriteResult<T>`只有机制internal构造的`Returned(value)`、`ActionRolledBack(originalCause)`、`CommitRolledBack(originalCause)`。原CommandWriteResult/WriteResult及五consumer不改。新入口只允许ordinary writable、ProtocolNonEffect、空participants/None footprint；注册增加通用封闭`SelfSettledAction`能力，bus由本次真实registration显式交给request，CMD不能自行授予。ProtocolModule只为六Mailbox logical/五CLR家族的唯一whole登记；结构判据精确核owner，无HTTP/Internal/Main/后簇consumer。Application只认能力/来源形状，不硬编码Mailbox类型/错误码，也不新增注入的当前请求。

**真实签发与发生阶段。** DomainTransaction增加internal `AbortActionAsync(originalCause)`及不可由业务new/签发的`DomainActionRollbackFact`。Mailbox.RunAsync只将原catch改为保cause调用此真实Abort路径：仍在原action catch即时、不可取消地回滚原Open事务，仅对本次显式观察的原真实unit，AbortAction刚进入且原Tx仍Open时，先同步pin其unit/owner admission barrier，再把Tx置settled并执行任何rollback await/参与者回调；所有参与者回滚及真实Completion完成后才签fact。pin只是保护开始，不预造ConfirmedRollback或任何完成fact。fact只含自身Tx、当前观察调用opaque identity、唯一rollback occurrence、`DomainActionAbort`阶段、原cause、实际输出error和Completion；Tx不保存request/job/session/mapper或结算许可。共享standalone/ambient无观察调用也走同一真实Abort和双因修正，不取得恢复资格。除机制自身实现外，此internal action-abort来源的唯一产品调用点为原Mailbox.RunAsync catch，测试以实际callsite约束拒绝其它业务签源；这是Protocol/Application外的架构验证责任，交易机制不硬编码Mailbox知识。普通RollbackAsync、runner失败Abort、Dispose或Commit失败不签DomainActionAbort；已settled不能再次签发。没有真实完整回滚不能签ConfirmedRollback；Rollback第二失败保持typedRollbackFailure/Uncertain，即使外部SQLite全列已恢复也不改事实。

新入口的`CommandWorkUnit.InvokeSelfSettlingAsync<T>`只观察当前单元的一次**直接领域await**。runner在自己拥有的lease上开一个唯一调用观察，显式保存(request对象、context/job、unit/lease与该调用identity)绑定；Tx仅识别属于自身操作的opaque identity，不由ambient取回请求。whole交原Send/Claim/Read/Delete领域调用，delegate不吞错、重抛、登记、清理或加其它业务；资产Prepare及gold仍在await成功后、原事务内。Open/Query不经过RunAsync，不造self事实。观察器在真实领域await失败catch立即读取fact，必须同Tx/lease、本次call开始后新签发、同原cause/输出、当前观察阶段、未消费；该fact必须在runner自己的失败Abort**之前**已经存在。普通throw没有fact，runner后来Abort确认回滚也不能补作self来源。旧/foreign fact、错误phase/cause、未知完成、action成功后资产Prepare/组装再抛同cause均无资格；IsSettled/Currentnull/快照/类型文字/业务自报不是来源。

直接await发生自回滚时，观察器只消费AbortAction入口已pin的状态/fact并发出sealed、机制internal构造的`CommandSelfSettlementPropagationException`，保原cause为Inner并绑定opaque调用，交回唯一拥有它的runner；这是机制交接而非最终错误/raw出口。unit继续既有pending handoff，observer catch不负责首次启用barrier；禁止后续持久写、RequireRollback、登记、再调用/新单元或复用；这些受管理边界的拒绝必须sticky，业务catch不能清除。whole的普通业务catch明确排除此交接、取消和typed；无fact且事务仍Open的原MailException/InvalidData/普通动作故障才依旧RequireRollback(cause)正常返回局部拒绝。自结算交接不在whole提前映ACK。此安排使business不能自己凭异常引用签发ActionRolledBack，也不能在已结算事务上RequireRollback。

pending handoff的登记/新事务拒绝落在**已有真实GameplayOutboxUnit及其所属outbox的单元admission**：本次受观察的AbortAction入口先同步pin原unit及owner，必须早于Tx的settled/Current=null、任何rollback await/回调、原unit.End→owner.Close(open=null)及向下层caller暴露完成；普通Register/BeginUnit从pin起即不得越过它。owner.Close可照旧释放open，但不得释放这个独立pin的原unit barrier；fact/Completion发布、领域throw传播、下层代理catch/finally和outerobserver接错之间始终无普通admission窗口。barrier只存unit identity、pending与自身guard occurrence/error，不持request/job/session或捕获这些对象的callback，不新jobslot/第二outbox。下层同provider代理在inner已回滚而outerobserver尚未接错时的Register及Begin尝试，也须在最早admission拒绝并记录独立guard occurrence；之后正常签发fact或重抛原cause不得覆盖/清除该guard。runner以显式unit引用读取该barrier发生的guard并合入request sticky；普通登记接口因无request参数不能被假称已有此保护。DomainTransactionScope.Begin原本会创建该outbox unit，故第二事务同样在真实BeginUnit拒绝；无原真实unit关联不得取得新入口self许可。仅runner在全部unit/lease cleanup、显式request/job/current健康检查及barrier自身guard均通过后，才解除健康候选的pin并允许原单元外拒绝ACK；有独立错误则保其发生到最终失败，不能为恢复解除pin/清guard。observer无权提前解除。保原unit RolledBack/Uncertain/Committed含义、已有条目存活及Complete/source-close顺序，不新增flush或改变A发送设计。此barrier仅本次显式观察的self入口使用；没有观察的standalone/ambient不pin，仍保持原即时自结算及共享双因要求，原五consumer/普通单元语义不变；这不是把request搬到outbox。

runner只在本次action失败出口取得本次未消费交接、真实ConfirmedRollback、非取消/typed、request/job/current健康且无Failure/GuardFailure时暂列候选；不立即返回结果。真实unit/lease清理全部成功、EndUnit及结束后机制健康检查完成后才返回ActionRolledBack，tracked operation覆盖action到cleanup，绝不再次Commit/开事务。commit分支仍仅真实CommitAsync窄span、KnownStorage/FallibleCommit、同cause、ConfirmedRollback及原健康/cleanup条件；action19/1811或Prepare手抛不能冒native Commit19/787。Returned不虚称全Committed，正常返回拒绝保原提交事实。

**发生身份不能由同cause替代。** runner保存阶段/调用occurrence，self交接只证明该一次直接await的传播。fact产生后若又发生独立unit/request/current/registration守卫错误，即使业务catch后重抛旧cause或同一交接对象，真实边界必须已记sticky/GuardFailure/独立occurrence；候选检查拒绝恢复且保原cause及第二发生。真实unit关闭、lease Dispose、scope各用自身phase/occurrence，独立再次抛同对象不能靠ReferenceEquals/Includes去掉；正常同栈传播不冒第二次。未知发生/phase也不恢复。此机制只要求可观察的受管理边界，不假设全局捕捉所有普通.NET getter错误；禁止用exception.StackTrace/文字冒来源。whole不在观察delegate中catch/重抛是可检查调用约束，受管理guard的catch后继续/重抛旧cause必须另有可拒绝行为测试。

ActionRolledBack只代表单元健康结束，whole顺序await后才映拒绝及登记。CommandBus继续body→drain全部已启动operation→Close request→真实DI scope cleanup→一次FailAsync终判；pending/未await/吞错不能成功结束，scope失败不因先前单元结果被清掉，整个bus不在scope结束前返回成功。不清sticky、不撤消息、不扩原五consumer/default Write/ReadOnly，不新增raw/Compose、第二Host/outbox、AsyncLocal、jobslot或DI当前请求。

**共享C2仅作最小保因修正。** RunAsync的原catch传原cause到真实AbortActionAsync；DomainTransaction原RollbackFailure聚合须包含原action与全部Rollback causes的原对象，保CompletionUncertain。健康回滚仍向共享caller抛原MailException/普通cause；原catch传播不是独立第二次。保持standalone自开lease/ambient借用、Current=null和caught refusal Commit必throw，ItemDelivery/QuestHelperPuppet的IsSettled分支不删；不把共享接口改为永不throw或向GM/HTTP授新恢复。原子性、MailCreated/delivery同事务与LiveOnly消费投影先后保持。

**六路whole与原阶段。** 唯一`GameMailboxCommand`接收六logical有限五CLR家族、metadata及精确Dispatch；五handler只入队等结果。whole吸收旧Delivery/fine的operation、attachment wire解码、日志/错误映射、领域调用、原事务资产准备及投影。原FindByConnection/IsInWorld/在线存在/OnlineUserId归属门完整；在恢复映ACK前，whole再用原领域真源核精确session代次、连接、IsInWorld/OnlineUserId及物理Dispatch身份，失效不吞成普通ACK。机制另守request/job/authority，不用新actor代替业务current。无session原静默、不支持Action原早ACK21且无事务，Query null原正常提交后ACK21，不为统一拒绝强回滚。

| 原消息/分支 | whole显式边界 | 单元后原投影 |
|---|---|---|
| 005E single / 013B multi Send | 一ordinary写，空participants/None；Send→RunAsync成功后Assets.Prepare及当前gold仍同事务 | ACK→updates→cleared→gold/pets；single权威3帧，multi4帧 |
| 005F Claim | 一ordinary写，attachment解码→Claim/RunAsync→Assets.Prepare同事务 | assets→ACK |
| 0086 Action2读/0删 | 每次一ordinary写，真实Read/Delete RunAsync | 原Action ACK；不支持action无业务事务 |
| 0060 Open | 一ordinary写，query.ReadStorage及Map仍在原事务 | ACK→inbox |
| 0144 Query | 一ordinary写，FindRecipient/原self检查 | 原Query ACK；null不RequireRollback |

原MailboxCompleted/只读composer/encoder/helper可保，ICommandProjections复用逐目标Failed隔离；删除MailboxDelivery业务orchestrator、旧MailboxCommand/handler/注册及fine转发，不藏到whole背后。projection/clock位置不改，Send/Claim Prepare不移至单元外现读。全六仍NonEffect/空selected，无Cera actor/NonRequest footprint/Main/ledger重放/Fence/DiscardExact，不新增后簇许可。

**VC-B5终局。** C1 durableUncertain、C2真实SQL＋Rollback第二cause、C3 whole真实scope以及取消/unknown/sticky/guard/独立cleanup不补失败ACK/正常结束，不重做发送；保真实完成/typed/各原cause，由原outbox终局置后Close精确物理SourceConnection并继承原chain。空selected不免source-close，不凭复用connectionId误关replacement。正常disconnect observer/既有teardown照旧，受控transport的结算Discard零不能外推真实close全链零。whole scope故障前可能已有success/normal refusal，按原unit状态及登记顺序保留实际发送后关闭；不撤条目/earlyflush/求零帧。健康success/refusal/确认回滚无新增close，去注错后是合法新发送，不称Main/同序号幂等。


**P7当前边界与可执行提交分离。** 以下是B6受影响HOW；上文P3～P6机制与邮箱细节是已交能力及保留约束，不是要求下一Task重造接口或重跑历史全部矩阵。主控先选择一个独立NoRoom语义修复单元，只改旧Main正常确认回滚记账及它所需回归/证据，不改六消息注册、handler、领域规则、mapper/encoder或显式runner。随后四opening和Rental分别按下面真实依赖形成有用闭合单元；每单元required fresh结果快审。能独立保持原行为的文件/类型纯移动、签名整理或共享抽取不得夹进NoRoom或可见行为修正；若实际需要这种准备，先单独可核等价提交，再消费其输出。当前源码已可复用现成领域入口和runner，不预造无consumer准备Task。whole切换本身改变命令/请求/DI生命周期与默认失败阶段，必须连同VC-B6及对应测试作为协议模型变更明列；这不是“方法体原样移动”的纯搬迁，不以改提交标签豁免根约束。旧fine与其转发随所属whole切换删除，不为保旧故障时点另建第二scope、临时authority、撤帧或伪Uncertain。

**P7独立NoRoom前置。** 原`EquipmentRentalDomainService.RentAsync`的NoRoom已RequireRollback并正常返回；原UOW已实际完成回滚再调用`operations.RollbackConfirmed`，应在legacy `EffectOperationContext.Settled`的本次Main正常返回收口中识别该确认事实，将entry记ConfirmedRollback后返回，而不是要求未发生的Freeze/Projection再记Committed。仍要求本次Main确已开始事务；没有真实确认回滚时，原缺Freeze/Projection程序错误保持，异常/取消/typed不确定及第二失败不被正常返回分支吞掉。只使用已有frame/执行绑定及真实UOW时序，不新增能力、恢复开关或按Rental类型特判。当前直接normal-return RequireRollback的另外两源是BossTower/DungeonAdmission，其fine不是IRequestSettlementCommand，For/ForSettlement不处理它们；临近Task再核完整callsite/登记清单并保原非Main结果。该补丁不增加Main、refresh、重放raw或迁移账闭行；旧模型最终仍由T-011删除。原Rental Result2 mapper/`01 02 00 00 00`编码不改；去满包阻塞后的同帧重试沿原entry/原Ordinal，才执行那一次合法租赁。显式runner已有normal rollback-only→Main.Failed路径，不为这个前置修改它。

**P7四opening whole：复用结果机制，保窄动作映射。** CompoundItem、LotteryOpening（mode0/1）、VendingMachine、EpicCraft各自吸收原handler/Delivery/fine的用例；允许的多消息CLR家族仍按实际注册有限闭合，不能以泛型bag吞未知消息。使用现`WriteResultAsync`、一个MainSettlement和本次selected精确participants；在原领域/槽位转换/clock的窄业务try内传活跃`unit.Footprint`，普通非取消、非typed故障调用`unit.RequireRollback(cause)`并正常返回带原cause的非空局部结果。原OpeningRejected和普通失败mapper在单元完全结束后处理Returned拒绝或CommitRolledBack，保各自错误码/日志。仅真实CommitAsync的KnownStorage/FallibleCommit同cause、ConfirmedRollback及原健康/cleanup门可返回commit结果；不修改runner、来源事实或默认Write/ReadOnly，不借Prepare手抛、IsSettled或恢复快照授恢复。动作映射的恢复授权不涵盖request/current/footprint守卫、投影/encoder/登记或任何cleanup；这些机制调用不包进业务try，下层守卫即使随领域await进入业务catch也仍须sticky，RequireRollback不能使它恢复。主控每Task核实际变更如需动共享机制，先返受影响边界。

现有实际十个WriteResult消费者保持，四remaining opening具名接入后为十四；这只是已落地入口的有限consumer扩展。Rental不得取得此普通异常恢复，Mailbox SelfSettledAction仍只原六logical/五CLR唯一whole。四opening正常业务拒绝和真实动作/commit确认回滚后，才登记原失败ACK；前置早拒绝不额外claim Main。Lottery mode1仅成功后在Main外另`ReadOnlyAsync`，直接调用原premium领域读法、保持原clock及Entitlements/Usage准备顺序，不能调用ReadPremiumSnapshotCommand或合进Main；该fine若还有其他生产caller，保给未迁caller且列去向，不提前删除共享命令。premium失败不纳动作catch，Main持久结果不回滚。其余成功投影经ICommandProjections复用原OpeningCompleted/Epic等只读composer和Failed隔离，IGameCommandOutbox登记原直接拒绝，不新增初次成功raw。

**P7 Rental whole：正常回滚不等于异常恢复。** Rent和Charge各自原Main对应`WriteAsync(MainSettlement,[本次精确recipient])`，原session/参数/current门完整；Rent在action内以`unit.Footprint.DemandActive(actor)`及成功后TouchedSlots记录代替旧fine的LegacyFootprint，Charge保原最小Inventory|Rental投影，不为形式加Domain规则或无意义的新footprint API。原正常拒绝继续空提交，NoRoom沿已有正常rollback-only路径ConfirmedRollback；Rental action SQL/native Commit异常仍外抛、无ACK并关闭来源，不套四opening窄catch或Mailbox自结算。仅成功后另一个普通`WriteAsync`直接依原序Panel→当前角色库存→账号库存→clock，组成原RentalRefreshSnapshot；不是ReadOnly、不是第二Main，不递归dispatch。共享的ReadRentalRefresh fine仅在全部真实caller归并后删除。Rent保inventory→ACK→panel及装备外观投影；Charge保ACK→main snapshot→panel；拒绝不执行成功后的刷新。Panel、LuckyStarChanged订阅、ReplayStateDomainService及ItemUse/Dungeon共享星数入口不顺便重构或改事务/事件策略。

**P7 whole的失败位置与既有默认收口。** 六者都让现CommandBus按body→drain所有operation→Close request→真实whole DI scope cleanup→一次FailAsync完成，不在CMD自行提前终判/围栏。新whole scope覆盖原响应登记，故scope故障前可能已有success或normal-refusal帧；按原unit状态保留并沿原序实际发送后关闭精确物理来源，不补失败ACK/撤帧/提前flush/承诺零帧。Main后的独立premium/refresh错误保其第二单元真实完成；Main已Committed时由现HasCommittedUnit/CommittedParticipants默认typed/Fence/physical Block/DiscardExact处理。已经Settled的ledger仍Committed，不改Blocked/不重结算；只有Main尚未成功记录的不确定等原事实按既有规则处理。纯ConfirmedRollback后第二普通cleanup错误不伪称Uncertain或一概围栏。各独立unit/lease/scope再抛同一对象仍保发生事实与原cause；正常传播不冒第二次。精确旧session/真实旧physical与真实replacement/无关玩家必须分开，不能只看ConnectionId。既有发送/关闭链和F-131精度不变。

**P7六个replay owner的前置证据。** 不改CommandRegistrationExit或attempt框架；六消息所属whole仅可在各自旧decoder→原Main/SQLite→普通ReadReplayState写单元→实际CurrentStateBatch encoder固定cause oracle已成功取得后，登记现`CommittedReplayCurrentStateBatch`。随后用新注册decoder/dispatcher/whole、同provider原outbox/production sender重跑相同oracle，直接核cause对象、ledger entry/原Ordinal、Committed、失败batch未登记、无新增保护、实际source-close及当前投影不二次结算。旧oracle可在该迁移Task尚未切换时先完成；不满足就不授owner/不切换受影响路径，返主控受影响门，不造假before或绕actualencoder。TASK-32完全没有encoder fault证据，静态共享只供定位。已实际Close的transport不能假装还能接同帧；账本重放可在未物理关闭的受控登记边界单列证明，并另场核真实sender关闭，或以新合法连接证明其真实范围，不把它冒旧socket重发。首次成功/普通拒绝不签attempt，其他raw owner/两类许可保持；同IOException的业务组帧/clock、current/lookup/普通登记与额外独立cleanup仍按原默认保护。有限consumer/注册声明和架构反例随各whole迁移闭合，不借self能力。

runner的异常出口分真回滚、提交已确认及不确定：使用事务操作的明确结果/既有typed失败，不用DomainTransaction.IsSettled猜已提交（它在Prepare前就变true）。默认保持Task04已验收的普通提交后业务失败、既有typed发布、未知来源、登记守卫及不确定结果按全部精确参与者逐一围栏，禁止重结算并保留原cause引用；MonsterDeath/祭坛原受保护发布段不变。仅Spec B6 RQ-006表中的三个具名旧出口依下述机制保留raw，不将所有编码或IOException当豁免；其中CollectionBox旧动态证据的局限及补证由本P4下文单列。所有权检查、当前性检查和主单元生命周期保护在runner/显式句柄，不散成每CMD手写一份。

具名许可在ProtocolModule等实际命令注册元数据中声明，限定UseStackable已Settled后的RequiredExplicit、CommittedReplay普通Write后的CurrentStateBatch，以及CollectionBox首次成功Main结束后的原一次batch。Application只认识机制层许可标识/阶段及注册身份，不认识具体Protocol CLR类型。request只在对应实际阶段、当前job且健康/未closed、精确物理目标匹配、所有已启动unit/cleanup均结束时签发本次不可伪造attempt，绑定request、job、实际注册许可、阶段、目标及调用身份。许可不以永久exemption标志开启，不是新构造器能力或第二凭据框架；普通登记API默认策略不变。新增出口须另有旧行为证据及受影响审查，不能只声明元数据即获授权。

**P4选择沿现有attempt作最小闭合扩展。** Application的`CommandRegistrationExit`增加机制名`SettledInitialBatch`，不出现CollectionBox CLR或业务结果；ProtocolModule仅给CollectionBox对应whole及精确`CollectionBoxRequest`绑定声明它，其他协议仍只有原许可。`CommandHandlerRegistry`的未知/重复exit拒绝与Protocol/effect绑定验证覆盖新项，不在Application硬编码协议白名单。`ExplicitEffectRequest.DemandRegistration`改为穷尽switch，先保共同exact physical target、Entry.Committed、Main已结束条件，再按下表判阶段；不继续用“非Required全为replay”的二分，不把默认/未知enum当作合法新出口。

| attempt携带的具名exit | 状态与真实适配调用 |
|---|---|
| `SettledRequiredExplicit`（原） | 非replay且已settled；仅RequiredExplicit登记入口的required direct实际encoder |
| `CommittedReplayCurrentStateBatch`（原） | replay且其普通Write已确认提交；仅batch入口，不准借给首次成功 |
| `SettledInitialBatch`（P4） | 非replay且已settled，Main和全部unit/operation清理已结束；仅batch入口，不准借给RequiredExplicit |

不增加Abstractions对Application enum的引用：沿现有`IGameCommandOutbox → ICommandMessageOutbox`带`ICommandRegistrationAttempt`的batch重载原样透传这个不透明对象，phase留在内部sealed attempt，调用方没有可改phase/签encoder章的方法。`CommandRegistrationAttempt.Enter`收敛为具名batch/required-explicit调用入口（可共用私有校验核心），内部按上表校验已捕获phase与实际adapter调用种类：batch仅接受replay或initial两项，required-explicit仅接受原required一项，任何未知phase/call-kind fail closed。继续检查owner request当前job/健康/未closed、single-call entered/Completed、精确connection身份及state阶段，成功进入只消费一次；`OutboxAdapters.RegisterBatchAsync(token)`改消费这个batch入口，不再硬绑定replay，required入口仍单独固定。其无token普通batch/explicit重载不动，不从异常类型、入口名字或任意caller传入的phase推导许可。新项必须贯通注册→签发→typed适配→opaque契约→实际adapter→窄encoder→最终收口，不能只加enum/metadata。

**CollectionBox whole消费点。** 原fine的一个Transactional段直接成为一次显式Main Write，action依原序执行`unit.Footprint.DemandActive(actor)`→`boxes.ExecuteAsync(...)`→按result.Inventory记录`unit.Footprint.RecordInventorySlots`，不经legacy footprint或bus；Select最低CollectionBox，其余action最低Inventory|CollectionBox保持。原handler前后current检查及精确物理输入保持；旧四类catch（CollectionBoxRejected、NoRoom的ContainerAddition、ArgumentOutOfRange、InventorySlotMapping）及原拒绝码/空actor路径按原位置语义保留，action内需要正常拒绝回滚时用已有RequireRollback(cause)，其自身guard不得吞掉。一般SQL等原来未catch的失败继续外抛，不能为统一写法新增StorageFailure映射或用P3 commit-result强恢复。Main正常结束后，仅whole的真实成功`result != null`分支按原库存变化→Full boxes/selected重复→ACK顺序组帧，组帧/clock与前后current保持在许可来源之外；完成这些业务动作后，才签发`SettledInitialBatch`并对原context.Connection一次RegisterBatch。拒绝batch没有该许可，不引入全局success receipt。whole的非空结果形状、详细driver和路径白名单留临近Task，但须符合`IProtocolCommand<TResult> where TResult : notnull`，不能拿nullable mutation作协议命令结果。

来源证明复用现有最小显式数据流：本次request签发attempt → Protocol的typed Game登记适配器 → Abstractions窄登记契约原对象透传 → Application实际outbox adapter；Application内部sealed attempt实现现有不透明接口。batch扩展不得绕过同容器Registering wrapper或直接解析底层adapter。真实adapter与request同程序集，仍只有adapter能在实际encoder调用的窄catch内内部记下原cause并原样throw，不扩大catch到整个RegisterBatchCore。调用方不能自行报告/设置来源，也不增加多层receipt/result框架；内部具名入口的最终方法拼写留临近Task。CMD/NOTI类型约束不变，Abstractions不引用Application，Game适配器不新建请求或改变许可。callback、校验、转换、lookup、取消、outbox.Current守卫、缓冲区操作及outbox.Register等非encoder错误没有编码来源凭据；RequiredExplicit仅涵盖required direct实际encoder调用，BestEffort原逐目标跳过/继续保持。所有编码仍在原登记点即时完成，不造AsyncLocal、GameplayJob slot、单例可变请求字段、DI当前请求或从事务取请求。

body一旦登记失败，request立即保持sticky失败，业务catch不能恢复权威、开始后续结算/写入或成功结束。沿用当前CommandBus已落地的失败收口：body先记原失败，scope仍活着时drain全部已启动/未await工作单元；然后按原生命周期Close请求、dispose scope并收集独立失败，最后一次FailAsync裁决raw或typed/fence，再交作业结束处理原source outbox顺序。P3对当时两次FailAsync的描述是历史问题，不得在第三出口扩展中退回提前终判。Close不可为清理再打开；合法commit读facts及dispose不取得继续结算权。不得在清理前根据HasCommittedUnit提前作不可撤销的整CMD typed包装/fence或raw许可终判；原事务操作已产生的typed不确定事实仍照常保留，不能事后改称raw。保留原cause及业务/清理各因引用。

最终raw资格要求具名许可和本次实际encoder出处同时成立，且drain/所有unit/scope cleanup后最终失败仍为该同一原cause、无typed不确定性、无额外独立失败。须区分独立阶段的失败发生与同一失败沿调用栈传播；即使独立dispose/unit阶段恰好抛出同一个Exception对象，也不能因ReferenceEquals或现有Includes去重而忽略该独立失败。失败发生的阶段/独立性只在本次显式request收口记录，不新造全局追踪框架。条件全满足时保原raw对象、Committed、无新增participant fence及原source close；否则沿默认typed/aggregate及全部参与者保护，保各cause引用。不得按异常类、文字、自报transport或可包任意业务的Func豁免。

replay旧ReadReplayStateCommand的实际注册是Transactional，故新路径在普通Write内直接调用领域reader读取当前快照，单元结束后仍按MirrorMerge → Create → CurrentStateBatch → Town原序在事务外执行；不改ReadOnly、不调用另一CMD、不重新开始Main、不重复原业务/ACK。只有上述batch实际encoder故障可取对应许可，其前后业务组装和Town路径没有该凭据。协议第一次UseStackable的主事务/Freeze/Settled与之后RequiredExplicit分段同样保持。CollectionBox首次成功分支与replay分别签发不同phase，不借replay读取或再次Main来开启新许可。fixture同连接重放只证明账本/围栏语义，不保证真实socket关闭后还能接收。

本Plan选择不启用工作单元内延迟组装：新登记契约不暴露GameplayOutbox.ComposeAsync，不在打开的单元内调用该路径；协议/通知的原即时组包顺序和登记单元归属保持，投影按实际原位置迁移。T-002核实生产该延迟API的调用为零，T-011以结构判据守禁用；其机制测试不等于生产启用。F-132记录为该路径禁用/不可达，不称已修。普通已提交后业务失败的围栏/不重结算仍必须验证。若实际切片需要启用延迟组装，先由主控修订此决策及受影响审查，并取得Spec RQ-008要求的持久写→组装失败/取消→重发不重复证据，不能直接在Task里启用。

选角Prepare/DailyRefill/Quests/Pet、发布/PvP和补偿仍是原来独立阶段，专用补偿业务直接成为当前协议CMD内显式动作，不再dispatch补偿CMD。BossTowerPartyCoordination原100～194的塔持久提交→内存队伍更新序列作为不可分割的逻辑段搬入对应CMD，二者之间不新增取消点、await来源或世界复读。MonsterDeath共享发布、祭坛逐参与者及回选精确拆除按原保护范围迁移。

### 5. CMD可注入能力的封闭清单与归属

下表是新CMD的构造器判据清单，T-003实现并给违规反例；不按“任何Interface都允许”或“名字带Outbox即允许”。机制实现可依赖既有基础设施契约，但不能变成任意服务转发门面。

| 可注入能力 | 契约 / 实现归属 | 允许理由及边界 |
|---|---|---|
| *DomainService、*DomainStore | Abstractions各领域 / 原实际状态所有者 | 维持现有类别判定；业务服务调用规则/存储，CMD不绕过它们 |
| IPartyService、TimeProvider | 原契约/实现 | 维持具名队伍聚合及环境时钟例外，不扩成其它Directory/Registry |
| ICommandWorkUnits | Application/Commands / Application/Transactions | 只管当前显式单元；原Write/ReadOnly sticky、现十WriteResult consumer的commit-only来源保持，P7只给四remaining opening具名接入（完成后十四），Rental不获异常恢复。P6已交WriteSelfSettledResult/当前unit直接领域await观察，仅原真实registration的SelfSettledAction六Mailbox logical/五CLR唯一whole可用，真实Tx签fact、完整cleanup后封闭结果；不增第二机制/当前请求、自报来源、清失败/映协议码、入队/发送/动态解析。 |
| ICommandMessageOutbox | Abstractions/Networking / Application/Execution/Outbox | RegisterAsync、RegisterBatchAsync、RegisterExplicitAsync只登记当前CMD；§4三个出口复用现有opaque attempt重载，batch在实际adapter内部仅接受replay/initial phase，required-explicit独立且不可借batch。仅encoder窄catch记cause；普通重载默认不变，无caller自报来源、传输或Resolve，不造第二队列/请求容器 |
| IGameCommandOutbox | Protocol/Game / Protocol/Game | RegisterCmdAsync、RegisterNotiAsync及typed batch/explicit；沿现有attempt重载将同一opaque对象传至前项，原typed转换在encoder来源catch之外，保CMD/NOTI类型和exact physical connection，不签发/改phase或包装任意业务Func。Application不知道CollectionBox等协议类型 |
| ICommandConnectionClosures | Abstractions/Networking / Application/Execution/Outbox | RequestCloseAsync只登记精确关闭；维持close总执行、链原因与F-131现状，不直接断socket |
| ICommandProjections | Abstractions/DomainEvents / Application/DomainEvents | 仅ProjectAsync并返回登记结果；EventService同一实例实现此窄契约，不暴露Enqueue/Direct或重建第二事件系统。领域事实仍由实际领域服务记录 |
| IAltarSchedulerWakeSignal | Abstractions/World / 原Protocol AltarWakeSignal | 仅无参数同步Wake，尝试写入原容量1、DropWrite Channel；不读写世界、不传callback／request、不入队CMD、不暴露Wait或任意调度。whole仅在原祭坛唤醒位置调用；Scheduler仍是原生产者，Wait／deadline／coalescing不变，同DI对象无第二Channel。 |
| IOutboundOrdering | 既有Abstractions/Execution / Application发件箱 | 仅显式跨收件人顺序组作用域；不改变Required/BestEffort/逐连接语义 |
| IInventorySlotMappingRuleService | 既有Abstractions/Inventory / 原实现 | 仅协议槽位与BusinessSlot的固定地址换算这一既有例外，不允许任意规则服务 |
| ILogger<T> | 原日志框架 | 业务拒绝/producer降级等原级别；不可重复记录执行器已负责的异常Error |

INetworkMessageSender、IGameMessageSender、INetworkConnectionCloser等旧sender/closer契约即使DI当前映射到发件箱，也不允许注入新CMD；只登记的新契约代替CMD的旧用法。底层encoder/transport/observer真正需要的sender仍按职责保留，最终不能把整个传输接口重命名为Outbox冒充完成。

§4的attempt仍是现有显式方法参数，不扩大此构造器清单或增加注入的当前request；第三phase沿同一接口通过，不另设CollectionBox专用总线/请求容器。注册元数据、签发/收口归Application命令机制，opaque契约归Abstractions，CMD/NOTI适配归Protocol，实际编码出处归Application常设outbox。生产和测试窄接口均须复用同容器/同adapter/同GameplayOutboxes。RecordedOutbound的旧INetworkMessageSender与新ICommandMessageOutbox须alias同一个Registering实例，新增方法保留其inFlight、回调/观察及实际FaultingEncoder路径；不能由新接口直接解析底层adapter绕过wrapper，不建第二provider/outbox。旧callback错误仍属callback，不借inFlight取得encoder证明。

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

### P8：无账本效果 owner 与失败保护参与者分别声明

既定 `NonRequest` 保持现状：仅一个绑定入站物理连接的 actor，Cera 的许可与保护不变。为 TASK-39 的两塔普通写增加独立声明模式 `DeclaredNonRequest`，只适用于真实 `ProtocolNonEffect` 请求的 ordinary、非只读工作单元。声明显式携带非空、唯一的精确 `SelectedSessionIdentity` owner 集合；不得由缺参推断、在领域调用时动态追加、从 ambient／legacy 捕获，或借此获得 Main、账本、raw、恢复与可信内部来源权限。本轮不开放 Internal 或效果请求消费。

owner 集合限定本单元可以调用效果的对象，`Participants` 仍单独表达原有失败保护范围，二者不自动互换。TASK-39 从原协调阶段捕获的塔 run／目标事实建立 owner 集合，不在持久提交到即时队伍内存修改之间新增读取。Boss 原报告会话加全部 run 参与者及逐人 current 检查保留；Illusion 原无 selected marker 的空保护集合保留，不能为了取得足迹而新增 Fence／DiscardExact。领域自身的 session／run／成员判定不删减。后簇使用该模式须在对应 Task 按真实旧行为明确 owner 与保护集合，不能由本模式推定其范围。

机制仅由当前显式 runner 签发，绑定本 request、job 和本 writable transaction；action 结束即关闭，事务结束、请求结束／失败、跨 job 或泄漏续体均拒绝。每次调用先检查请求健康与活跃状态，再校验 owner 的账号／角色／完整 session 自洽并精确属于声明集合；其他 owner、替换代次、空 session 不放行。这些身份校验不另行把 owner 作为安全服务的 current／Fence 参与者；原业务 current 门和声明的保护参与者检查各守原职责。所有能力守卫拒绝须保持 sticky，捕获后不能继续写或正常提交；不建立无条件 no-op recorder。

验证覆盖真实 runner＋生产两塔链的旧成功／新拒绝／修复后成功，至少含踢出非入站队员和结算其他成员；普通单 actor、Main／未选中效果请求、ReadOnly、Internal 和来源缺失不能误获新许可。补未声明 owner／替换代次／账号角色错配、跨请求／job、action 结束续用、捕获 guard 后提交等反例；按旧范围验证 Boss 全参与者和 Illusion 空 selected 的失败保护、原 cause 与真实 source close。既有 Cera 与所有已有 footprint／Main／WriteResult／self-settlement 判据须通过；修改共享 runner 后执行新鲜 Release solution build 和八项目全量。具体用例和结果只写 TASK-39，不新增取证 Task。

## Work and Dependencies

| 包 | 有用结果与责任边界 | 真实依赖 / 拆分理由 | 覆盖 |
|---|---|---|---|
| T-001 首单元有界修正 | App25文件按拥有的规范化写/只读池清理；F-127真实传输失败只调整gone日志分类，含普通故障对照 | 仅已批准Plan及checkpoint；不依赖CMD迁移，先稳定清理与失败日志基线。一个首Task含两项，但实现与验证证据分开 | RQ-010/011，AC-006；F-130/F-127，VC-B1 |
| T-002 保护与迁移账 | 冻结注册/来源/实际事务点清单；F-88/F-93四项先红守护；建立可执行不变量对照，核实F-132延迟路径禁用 | T-001结果；先有保护再搬保护，清单不是详写远期Task。保护可按即将迁移簇物化，但四项不得晚于对应搬迁 | RQ-001～008/013～015，AC-001～005/008；F-12/F-34/F-88/F-93/F-112/F-132 |
| T-003 共享执行骨干与简单协议先行 | 同一命令模型、新路由/请求状态、runner、typed登记接口及注入反例；用简单单命令/单响应协议验证贯通，再迁Channel/登录/名册等适合的简单簇 | T-002的清单/相关保护；骨干不能靠空壳编译算完成，至少一条真实入站路径和内部来源验证。跨切片接口与可见行为分Task按test-value gate处理 | RQ-001～009/012/014，AC-001/002/004/005/007；F-58/F-110，VC-B2 |
| T-004 物品与效果簇 | 既有物品/槽位/效果whole、F-122和三个raw义务保持；后继四remaining opening归并及有限replay owner；其余Rename/Hatch/SharedEffect/GenCeraTicket仍待实际边界迁移 | Task01～32已接受结果保持；Task32只调查不闭行。先独立NoRoom记账前置，再按四opening共享OpeningInventory/ApplyEffects直接具名入口/原阶段闭合；每路切换前旧encoder oracle、切换后新路径/default保护；其余四消息/旧fine不被本轮冒done，不借Mailbox能力 | RQ-001/003～009/013～015，AC-001～005/008；F-34/F-112/F-122，VC-B4/B6 |
| T-005 邮箱、市场、租赁簇 | Mailbox由Task31已交；下一独立结果为真实NoRoom正常确认回滚记账修复，再Rental两Main/独立写刷新whole闭合，异常不恢复/whole默认保护及有限replay验证；市场按实际入口保边界 | 消费Task32真实before、B6/P7匹配gate和amendment-only检查点，先修可独立NoRoom再整CMD迁移；复用现WriteAsync，不扩SelfSettledAction或WriteResult。拍卖只有outboundB7无入站实现，不凭包名造Task；Mailboxdone不代表本包done | RQ-001/003/006～009/013～015，AC-001～005/008；F-12/F-112，VC-B5/B6 |
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

B2/P2对应首效果单元保留EffectOperationReplayTests的A_use_already_durable_before_sender_failure_cannot_consume_twice_on_the_same_raw_request及Batch_IO_failure_keeps_committed_state_and_uses_later_current_quantity原业务/字节/不重ACK断言，故障仍注入真实登记encoder，不改physical transport。补同cause对象、Committed、无新增participant fence、原已提交条目/source-close序与后续当前数量重放的同provider证据。对照相同IOException来自提交后业务、非许可出口、原typed发布、非encoder登记守卫仍typed且全参与者fence；登记故障叠加独立dispose/unit/cleanup错误（含独立阶段抛同一个Exception对象）撤销raw资格、保原cause引用。覆盖跨request/job/target、closed/leaked、连续singleton请求、catch后不能继续写/成功，以及去掉真实来源验证会行为变红的可编译mutant；不能用编译失败/无效fixture代替RED。

受影响结构/回归同时证明：仅注册具名出口可签发、普通Register默认不变、opaque handle无公开自报入口、Application不识别Protocol CLR、没有新ambient/jobslot/DI当前请求/事务查请求；new ledger/reader/sink每处Capture使用显式recipient/metadata/null，LegacyReplay仅旧route可达且有最终删除账；唯一CWT、ConfirmedRollback不更新原entry.Ordinal、普通Write读取后事务外MirrorMerge/Create/batch/Town、无nested bus/Compose/延迟编码。RecordedOutbound新旧契约同Registering、同outboxes且inFlight注错/观察仍生效，Required/BestEffort及原帧序保护不放宽。详细测试放临近Task，不把这些拟验项目计为本轮已跑。


**P7最小语义前置的检查。** 以TASK-32 `rent-noroom`旧raw为已有before，门后先新增实际已注册old decoder/dispatcher/fine的满32设备格回归，在未修源码时真实失败RED；最小记账修正后GREEN，不以编译错误或直接domain正常返回替代网络链。完整同run110生产表/644列含NULL/BLOB及真实item_slots rowid在baseline→实际暂存扣星/设备→结束逐值回滚；原NoRoom Result2完整body/帧、MainStarted/实际ConfirmedRollback、ledgerConfirmedRollback、无Frozen/Projection也不能误记Committed、no refresh、无新增Fence/physical Block/DiscardExact/Close直接核。去满包阻塞后同raw sequence/body复用entry/原Ordinal，仅结算这次成功，后续正常Committed当前投影与空提交拒绝作对照；transport已Close时不伪造继续接收。保原异常动作/native回滚的cause与关闭出口、缺事务/缺Freeze投影/非法Main/第二错误或typedUncertain不能借新增分支正常结束。使用既有EffectOperationReplayTests、ExplicitEffectCommandTests的真实事务/库存强断言与正式Main/non-Main登记判据；不削弱Boss/Dungeon结果或其它旧Main。该单元只修记账及验证，canonical迁移行不关闭；不把无改动的十consumer、六self或raw owner重写成新成果。

**P7六路迁移的同源对照。** TASK-32 matrix-04的52场/142 focused及freeze只为旧事实，明确Vending/Epic未逐路native、opening C1/C2以Compound代表、dispatcher仅三代表；迁后按实际新边界取得新证据，不冒所有组合已测。每条whole都保真实registered decoder→同provider dispatcher→显式Main/原domain/SQLite→原adapter/outbox→production OutboundSender及精确physical的链；Lottery0/1健康分列，Rental新租/同UID续租与绝对到期、Charge扣gold/加星/事件及原帧序直接核，早拒绝/正常空提交/健康确认回滚分开。四opening实际action19/1811及真实native Commit19/787均须有可比原来源/阶段/同cause和完整回滚，原source缺的Vending/Epicnative如以共享来源覆盖须明确范围，不冒逐路动态通过；Rental对应异常依旧noACK/sourceClose。受控Prepare/事后手抛不冒原native provider failure，Release栈没有显式Commit帧时按实际provider源/fact交叉，不猜JIT原因或补造栈。

whole scope使用实际DI resolve的IAsyncDisposable及DisposeAsync故障，不在body手抛/手动Dispose冒scope；记录其发生前原success或normal拒绝帧、各unit真实Completion、ledger及最终实际Send→Close/原chain。Lottery1独立premiumReadOnly和Rental独立refreshTransactional分别注错，证明第二单元不撤Main已durable、默认精确actor保护及已Committedledger不重结算；另有纯ConfirmedRollback后第二普通cleanup错误对照，不统一写Uncertain/全围栏。C1真实SQLite durable后controlled nonfallible违约、C2实际SQL加独立Rollback第二cause按原机制抽有价值共享代表并记覆盖范围；原cause/独立cause同对象和再次抛同cause的独立发生均保留，不能靠引用去重。真实替换session/不同physical及无关玩家对照旧actorFence/Block/DiscardExact/实际Close，无误伤；健康success/refusal/NoRoom无新增关闭/保护。原fullschema快照、观察提前enlist同一SQLite的影响、受控fault/transport非自然事故/TCP/A21限制继续披露，不制造第二fallible participant或伪Completion。

六个raw owner严格按§4先旧后新oracle；actual encoder固定cause、失败batch零登记与callback/clock/Full/current/Register非encoder分开，scope/lease独立cleanup含同对象二次错误仍撤raw、保cause/默认保护。优先复用既有真实来源/owner/phase/target/closed/leak/未await-drain及可编译mutation强例，迁移修改了哪段就扩到那段，不能仅测试名称/未触发fixture计pass，也不为六consumer重复整套无价值笛卡尔积。有限注册/sole route/不含旧fine/CLR消息shape判据在CommandRegistrationTests与EffectSettlementRegistrationTests同步更新；ReadPremiumSnapshot/refresh的真实剩余caller及OpeningInventory/ApplyEffects/ItemDelivery、LuckyStar/Panel/通知共享强用例都按所触边界保留，不把静态caller图当全故障验证。

P7各临近Task按实际风险选择聚焦检查和相关项目full：NoRoom触共享旧结算分支，至少相关真实legacy/explicit settlement、Rental wire/原域/codec及旧Main/nonMain判据，并扩大所触Application/Networking等实际运行面；四opening/Rental迁移至少其真实协议/共享业务与Application/Protocol/Networking相关full，若触及Host用例/跨层机制则相应扩大，改变runner/来源/事务核心须先主控核边界再扩大八freshfull。具体命令/过滤/逐用例数留Task；不把每小Task八full设成无条件门，也不把Task31的9075=9055pass+2 exact Dimensionfail+18skip、DFO74/7/Golden15/Char10冒本轮新跑。每个新结果绑定当轮source freeze/加载DLL Location-MVID-SHA、原argv/UTC/exit/自然终止及逐display/status/multiplicity数量加法，原5m/类并行/Skip/断言保持。DFO expected/control不放宽；部分轮只有部分control或零记录不称新七程序集74/7，确需有意新违规须逐类/数明确。最终T-012八freshfull及同工具2秒测量仍全责。精准Task-owned manifest/NUL nonowned与行尾/四类whitespace、原strict validate/八坏例和真实canonical行仅在各迁移consumer闭合后更新；NoRoom前置不闭行。所有这些是未执行义务，本Plan无新pass主张。

**P6 Mailbox历史验证责任（Task31已交；以下将来时与库存为P6当时要求，不是P7下一单元或新运行）。** before使用TASK-30已提交freeze-07→build-07→final-03十四场及原59focused；不改历史harness/report/Task来适配删fine。B5 amendment已勘误single：CMD005E16B＋2×NOTI000E119B=3帧；multi：CMD013B16B＋3×NOTI000E119B=4帧，原request不混Retry/HealthyControl/LiveOnlyDrain。新Task独立driver沿真实registered decoder→同provider dispatcher→whole→实际Domain/SQLite→原registration adapter/outbox→production OutboundSender＋controlledtransport，六消息及Action读/删各真实完整body；不手造request/direct domain/换provider冒whole。source freeze/加载Location-MVID-SHA/UTC命令退出/自然终止同轮绑定，controlledtransport不称TCP/A21。

新六路成功/原拒绝、无session、unsupported早拒绝、Querynull正常提交、fine内精确session门保持；设备UID/core/stack/gold/mail/attachment/claimed及事件、原字节/错误码/Send ACK→assets、Claim assets→ACK、Open ACK→inbox及Failed隔离都直接对照。projection Prepare/gold仍原事务内。self正证为真实晚期Send SQL19/1811与ClaimGoldLimit：暂存改变→RunAsync真实Abort/fact签发→observer失败交接→runner读取，核同Tx/lease/call/原cause、CompletionConfirmedRollback/Currentnull/unitRolledBack，证明fact在runner失败Abort之前，不二次Commit/新事务。普通非self动作拒绝依旧窄catch/RequireRollback→Returned；正常返回拒绝不误称回滚。native Commit另证action/receipt成功返回后deferredFK的FK1/child暂存、真实SqliteTransaction.Commit19/787/KnownStorage/FallibleCommit/同cause和全回滚，不能用ABORT/controlledPrepare代替。去注错合法新发送只做该次转移，不称重放。

来源保护必须有可拒绝反例：无fact而runner自己Abort后才ConfirmedRollback；旧/foreign Tx/lease/fact/观察call；错cause/phase/重复消费；action成功后的Prepare/组装错误；closed/foreignrequest、错job、token已cancel/OCE、typed/Unknown/Open/Committed、原sticky/GuardFailure；未await/drain、caller catch交接继续写/登记/新unit或成功return。重点反例：actual selfRollback fact已存在后，再触发真实unit/request/current/registration守卫，catch后重抛同一旧cause；guard的独立occurrence/sticky必须使恢复失败并保两个发生，不能只测未处理guard或RefEquals。直接登记与第二事务必须实际到达同一原outbox的Register/BeginUnit barrier拒绝并在runner读取guard后不能恢复，不能只测试CommandWorkUnit本地布尔；除whole已接signal后的反例外，必须另在同provider真实下层领域proxy catch/finally设置窗口：inner真实自Rollback已完成/Currentnull/fact已产生，outer InvokeSelfSettlingAsync observer尚未接到原错误。两场独立新fixture分别尝试(1)同一原outbox普通Register，(2)同一DomainTransactionScope.Begin→真实SQLite write→Commit；两者均须在最早Register/BeginUnit admission拒绝，第二场后续SQL/Commit未到达、无第二事务durable，第一场无新条目。proxy捕获guard后仍重抛同一原cause，核barrier独立guard occurrence不被后续fact/原cause传播冲掉，runner不得ActionRolledBack/普通失败ACK/健康结束，最终保原cause和guard及原source-close。该窗口与真实unit.End/owner.Close释放open的原时序一起观察，不仅在whole catch signal后注错。来源/恢复mutation必须有可编译行为价值：将pin延后到outerobserver或让owner.Close清pin时，上述至少一条原真实路径应实际越过admission并使拒绝断言RED；恢复入口pin/保pin后GREEN，不能以compile缺API冒RED。unit关闭、leaseDispose、whole scope真实cleanup分别注错，含独立再次抛同cause/交接对象，与普通传播区分；单元清理前不可返回成功，bus完整scope结束前不可成功。新增至少一项可编译、有行为价值的真实source/phase/owner或cleanup保护mutation触发错误恢复RED，恢复GREEN；API缺失compile不冒RED。原五consumer/defaultWrite/ReadOnly、KnownStorage/GuardFailure/cause/commit-only及三个raw全部owner/actualencoder/source/cleanup正反例保持。

VC-B5分别证明C1 SQLite已durable仍typedUncertain；C2实际SQLcause1＋Rollbackcause2都原对象可达最终typedRollbackFailure，SQLite全回滚仍Uncertain；C3真实whole DI scope解析并Dispose受控资源，不用body手抛/手动Dispose冒scope。三者无新增普通失败ACK/正常结束。直接观察精确SourceConnection的actualCloseAsync及原chain/实际帧→置后关闭，不能仅登记/Discard。记录fault前已有success/normal refusal及原unit存活帧，不从旧C3 slots=[]推零帧；空selected/无新增actor/footprint/Fence/DiscardExact的结算保护直接核。实际disconnect child若运行则单列原teardown/继承链，不要求真实全链Discard零；旧来源与真实replacement不同physicalreference/不同session及无关连接对照，不能仅“Exact”名称/复用ID推隔离。健康success/refusal/确认回滚无新增关闭，不手动Record伪造链。

原59focused方法/参数/断言（Core30含partial14＋ProtocolIntegration19＋codec10）保留，驱动与断言变化分列。重新rg全部finecaller，已核Host `GmMailAttachmentHttpTests`/`GmOfficialMailHttpTests`、App `Mail/MailboxProtocolIntegrationTests`/`Inventory/ItemDeliveryTests`；删fine后改真实driver，不换provider/SQL预置或domain冒whole。保GM真实HTTP、prepared多目标/receipt/audit、UID/core/codec及stale归属拒绝/同fixture资产强断言；wire无法表达的旧领域强输入可用test-only真实领域+原显式事务接缝并限证明范围，不保legacy转发/发明wire字段。非nullwhole结果/driver接法/白名单/逐命令留gate后Task31。

共享双因另用standalone/ambient真实RunAsync SQL＋Rollback第二因直接核两原对象/typedUncertain/Currentnull/caughtCommit必throw，不只网络logger。所触caller保健康及失败原子性：ItemDelivery溢出/MailOnly prepared前置inventory和UID/core；GM第二目标失败时前目标、operation receipt、整批audit同撤；QuestHelperPuppet撤销旧delivery/替换/日标记与IsSettled；Housing RecordHarvest/双方mail/tree轮次；Wedding位置/双方礼物礼金/宾客回礼/退款。优先原强用例，按缺口补真实SQLite控制，逐caller记实际覆盖和未测，不将静态影响当全故障端到端。MailCreated/delivery同事务、Reliable原子性及mail.arrival LiveOnly消费提交→外部投影另保，不前移成协议ACK，不借shared保因给HTTP新恢复。

同run全部110生产user tables/全部列含NULL/BLOB逐值原样回滚，注错DDL后取baseline；不能泛strip时间/GUID。跨run若需只沿已证具名SQL时间、Task29 HousingUTC及GUID图作窄关系验证，写清允许字段/理由，不替同run原样回滚。临近实施冻结最小源/原强测试/完整git ls-files -z；末尾NUL nonowned逐bytes/SHA/HEAD、canonical原行尾、精确owned manifest/排循环/四类whitespace核验；仅闭Mailbox六message＋原fine七实际ledger行，不挪非ownedpending。

预计共享Tx/runner/API改动，必须新鲜Release solution --no-incremental 0warning/0error＋八项目full，原类并行/5m/Skip、console detailed及DFO保持。当前库存仅Task29的9029=9009pass＋原exactDimension2fail＋18skip；逐项目/逐display/status/multiplicity增减闭合，新失败先身份诊断。DFO原九expected类67＋七actualcontrols7=74不放宽，必要新intentional逐类/数独立列，七程序集正对照/反例对照外0与exe0局限保持；Golden15/Char10、完整注册/runtime/purefamily/3raw全owners保持。Task30 probe/59focused不冒新full；最终T-012八freshfull、2秒CMD测量仍后继，不把套件时长/probe冒性能或本轮pass。

**P5 Cera历史验证要求（Task29已交付；以下将来时/旧起点保为历史，非P6下一单元或新运行）。**

**P5 Cera：复用已接受before，直接核新whole与默认保护。** Task28的`report.md`、`final-01/`七例、freeze04和原日志是已提交、独立approved的旧边界，不再编造before或修改历史harness以使它在删fine后编译。临近Task先封存当前最小生产/原强测试及完整tracked NUL，再将必要探针适配复制到该Task自己的新路径；保持真正注册decoder→同provider dispatcher→whole→原domain/SQLite/适配器/outbox。原Task28使用production OutboundSender+受控内存transport的边界继续明确，新观察也不能拿Discard或仅登记条目冒实际发送/CloseAsync。组装及取消/guard/cleanup异常来自哪个真实位置、cause引用、全部列含NULL/BLOB及事件/临时FK表的before/暂存/结束/新合法购买快照、原UTC/命令/exit和加载assembly/source freeze对应均直接落证。

保旧success/正常不足余额拒绝；普通返回拒绝实际Committed不误称rollback。A晚期宠物slot SQL19/1811前wallet/gold/前grant/events已暂存、domain未receipt，迁后RequireRollback(cause)的Returned拒绝须完整回滚并保原ACK1/no-fence/no-close；B须同样真实domain先receipt、FK1/child>0、SqliteTransaction.Commit19/787及KnownStorage/FallibleCommit/cause事实后才CommitRolledBack，全部列回滚并保相同响应。普通trigger ABORT或controlled Prepare不能替代native Commit。A/B去trigger后的相同合法cart是一次新的购买，只产生该次扣费/物品/事件，不冒Main/同entry/Ordinal幂等。请求失效、取消、原sticky/guard、unknown/typed、额外unit/lease或scope错误均不能取得这种可恢复值；对既有核心正反例按实际所触边界复用或重跑，不以测试名代替路径。

VC-B4必须同时区分C1真实SQLite已提交后controlled nonfallible违约、C2真实SQL加controlledRollback双因（实际资产可回滚而Completion仍Uncertain）、C3新whole真实scope在body后清理故障。C1/C2保typed及原cause/第二cause对象；C3保已提交事实及独立cause/默认包装，三者不按StorageFailureACK恢复或正常结束。直接核本次精确actor的Fence、精确连接IsBlocked、DiscardExact目标和实际source close/chain；对照健康success/refusal/确认回滚无此保护。至少以真实不同session身份/运行态或受控实际替换，证明旧actor保护不误清/误围栏/误关替换后的身份及其他物理连接，不能只断言用例名或静态“Exact”。C3观察scope错误前已登记的真实消息、最终按原unit状态存活的帧序和关闭置后；不统一断言零帧，不靠修改outbox、补失败ACK或减参与者求绿。受控participant/scope故障及观察查询可能提前enlist原SQLite的影响按Task28方式披露；它们不等同生产事故或TCP/A21实机。

原Cera直接fine强测试的真实PVF、wallet/gold、容量/名字牌/权益、UID/宠物、晚期回滚、旧selection/持久所有权、receipt不投递给替换身份和原codec参数/断言保留。旧fine删除后，wire可表达的用例按真实新whole/decoder驱动；不能wire表达的旧session/领域输入可保在真实领域服务+原显式事务的test-only接缝，明确其证明范围且不假造生产字段、删除拒绝断言或保一个legacy转发命令。Host的`RealShopPurchasesCompeteWithGrantsWithoutLostSpendingOrRoleMultiplication`保实际HTTP grants与三次真实购物竞争、每次receipt及最终97810/三操作事实；不能替换成SQL预置余额。已有非效果来源/结束后足迹、真实通知/字节和处理失败后下一次合法购买的强意图保持。具体fixture方式和白名单由临近Task按此约束收敛，不在本Plan预写实现。

临近Cera Task冻结后完整Release solution build0警告0错误；按实际影响至少Application/Protocol/Networking相关full，若改Host竞争用例则Host完整full，保存原并行/5m/Skip及逐display/status/multiplicity变化。若确需改共享core/API则返主控确认范围并扩大八freshfull，不将当前“无需改core”的选择作为以后复用旧证据的借口。原9014＝8994pass＋2 exact旧Dimensionfail＋18skip只引用Task27已接受库存（Task28无新xUnit）；每个新full与其它未受影响库存的来源/冻结身份明确，最终T-012八freshfull不减。正式DFO沿原expected类/数、actual controls及轮次来源闭合，混用full轮次只允许原bytes复制的新bundle及逐SHA provenance，不双算control、不改expected求过；Golden15/Characterization10、原source/runtime/pure family和canonical严格验证/坏例按实际变更完整保留。只闭合Cera真实消息/旧fine对应ledger行，三个raw出口全部owner不变且Cera无许可；本P5不运行这些检查或提前记pass。test-value gate优先原强行为/已证before和有价值保护，编译失败不冒RED，局部结果仍required fresh独立快审。

**P4已消费的CollectionBox验证要求（以下原文保留，8828及Task19是当时起点，非P5新运行或当前起点）。**

**P4受影响单元的前后直接oracle与安全矩阵。** 产品迁移前先在真正旧CollectionBox decoder→dispatcher→fine/Main→SQLite→同provider实际Registering/outboxes/registration encoder路径实跑Select和Put，固定每case唯一encoder cause对象；保最小原源码/driver、production SHA、命令、真实UTC/退出状态与完整输出，不重建假before或回写旧证据。旧手造CollectionBoxRequest直接dispatcher只证明原间证，不能替代decoder；先验证注错确实发生、注入在actual encoder窄span且整失败batch未登记。迁移后对应新decoder/dispatcher/whole/Main路径重跑，直接核同cause引用、真实同ledger entry及原Ordinal、Committed、无新增participant fence、原source-close顺序，外部改变当前boxes/库存后同序号只重放当前投影，不二结算/不原ACK。RecordedOutbound.Discard不物理关socket；不声称真实关闭后可重发。

原拒绝码、成功帧序/完整boxes及selected重复、无session/初始Rejected、ordinary SQL语义和精确物理目标断言保留。旧physical reuse case的BeforeSend hook未断言真正替换发生：保原case并补真实可观察的替换触发反例/对照，明确确认hook/旧target与replacement事实，不能仅凭用例名宣称已有强覆盖。成功Withdraw现仅静态共用路径、Spec review列nonblocking residual；可在后继补真实成功Withdraw wire/encoder case闭合该范围，若未实跑继续披露，不能由Select/Put或Withdraw拒绝用例冒称动态通过。

第三出口矩阵对照相同IOException来自Full()/clock/业务组帧、current/lookup/guard/Register、拒绝batch、其他或无许可出口，均不得盖encoder章或取raw；无/错许可、未知phase、RequiredExplicit借batch、initial借replay及反向、错request/job/physical target/调用、closed/leaked/重复使用都拒绝，catch后不能写或成功结束。保原两许可完整正反例与现有mutation，新增可编译有行为价值的provenance mutation须由本次真正encoder来源保护去除而RED、恢复GREEN，编译失败/未触发fixture不算。独立unit/scope cleanup含再次抛同一cause对象必须撤销raw、保各因及独立发生事实、全部精确participants围栏；正常传播不误算第二发生。原typed发布、default Write/ReadOnly sticky、P3来源GuardFailure/RequireRollback两error保因/commit-only门、drain/Close/最终裁决顺序均完整回归，不重设计GameplayJob。

另有**尚未动态证明的有界guard风险**：实际Enter在old/foreign attempt拒绝时只Failed其owner；DemandRegistrationCurrent的closed/job失配抛出是否能让当前新request在catch后继续，不由现PreviousAttempt的未处理throw或liveforeign仅owner-drain断言证明。临近Task先在同provider真实新请求中捕获旧/foreign guard，再尝试实际持久写与正常return，观察当前job/request终局；同时观察原owner/drain/typed及全部参与者。若已有其它守卫阻止则保区分证据；若可继续，仅在现有显式请求/attempt/登记调用边界补最小sticky传播并使该行为反例RED→GREEN。此处是受影响B3 catch后禁写/禁成功的验证义务，不是已证bug或新WHAT；禁止为假设风险加当前request ambient/DI/job slot、从Tx取请求、第二GameplayJobHost，或滥用self-wait fault API。

本单元触及共享core，必须新鲜完整Release build 0警告0错误＋八项目full（非三full加四controls）；保持原并行、5分钟deadline与Skip，console detailed及原DFO74 expected＋七actual controls、任何新增intentional逐类/数、Golden15、Characterization10及display/status/Counter相对当前8828逐case N-D闭合。当前起点仅为Task19已接受库存8828＝8808 pass＋2 exact旧Dimension fail＋18 skip；最新三个full7028、另1800为Task15库存，不冒称当前八full或P4新跑。技术白名单/driver/命令与non-null whole结果留临近Task；本P4不运行native、不更改早期证据或要求尚不存在的Task交完成结果。

**P3四opening的历史验证要求（以下两段原文保留；当时的下一Task/8644库存不是P4当前起点）。**

P3对应四opening单元须**先在未迁旧route实跑**至少一条真实action SQL故障原oracle及一条真实SQLite fallible Commit故障确认回滚oracle，记录旧失败ACK/日志、原cause、ledger状态/同entry与Ordinal、资产/冷却/事件及无新增fence/来源关闭行为；不能只用旧catch语法或现有direct-fine action trigger测试冒称协议恢复已测。主控的`verification/evidence/plan-p3-provider-probe/`已实际证明同Persistence依赖的provider支持deferred FK INSERT成功后在真实SQLite Commit报19/787并Rollback成功；这仅是注错方法可行性，不证明opening或DomainTransaction结果。后继可在fixture临时引入DEFERRABLE外键与奖励INSERT触发器，仍必须核该fixture的ForeignKeys状态及故障stack/阶段，证明奖励/action已完成、失败来自真实SQLite Commit、回滚资产成立；普通trigger语句失败或fake participant不是native Commit证据。不可行时保留诊断，换有证据可达方法并报告实际阻碍，不能降格或默换基线；controlled Prepare＋真实SQLite资产应另列其证明范围，不能改名作fallible Commit已测。完整协议探针只在门后Task内收敛，不在本Plan预写实现或宣称已跑。

迁移后以同provider真实decoder/dispatcher/Main/SQLite/原outbox重跑对应oracle，action窄catch→RequireRollback→Returned拒绝与CommitAsync确认回滚→CommitRolledBack两路都保原ACK和正确账本状态，原cause引用/不二结算/同entry原Ordinal retry不变。另以实际状态验证Prepare、fallible Commit、rollback/lease cleanup不确定、非fallible违约、已持久提交后的组装失败、guard/取消/job失效、已有登记sticky、额外scope失败及未await operation drain；含已有guard失败即使被业务catch也不能取得结果/写权、不能伪造结果变体、不能于unit清理前观察成功。保默认WriteAsync/ReadOnlyAsync原异常sticky与catch后禁写反例、Main一次/claim不复位、Freeze后失败projection事实、B2两raw许可全部正反例。本入口是共享core变更，预计该单元完整Release build0/0＋八项目full、原DFO74 expected＋七actual controls、Golden15/Characterization10及逐case增减账，结果required独立快审；若真实测试需要有意新违规，逐类/数有界说明，不放宽原expected/期限/并行/Skip。下一Task起点为8644延续库存（最新App4886/Protocol1766/Net199，其余1793仅Task12库存），本Plan未执行上述测试。

F-07在T-010验证各主题差异：配额同周期余额变化不新增刷新；Premium同周期权益新增/有效权益fingerprint变化应刷新，水晶/成长激活→到期应按原水晶清零NOTI、成长清零NOTI、状态帧的适用顺序登记并更新完整去重状态，下一轮相同状态不再登记。还覆盖首轮已到期权益的清零、跨周期旧输入无登记/水位、session退出/替换、请求插入FIFO及下一扫描补当前周期。不得用仅跨周期/重复tick通过替代同周期真变化证据。

首Task的F-127使用真实transport两条路径（真实关闭期间、仅真实Writer完成且token未cancel），确认Resolve仍可解析且返回Failed；异常原对象、关闭/observer、队列其余条目与其它连接保持，普通故障仍Warning。HasClosedSendSide等细节在Task中选定并RED/GREEN验证，不能把本Plan的候选探针当已实跑证据。F-130只App25文件；已有fixture完整释放后按它实际拥有的写/只读规范化连接串各ClearPool，保持失败清理语义，ClearPool异常不吞并为文件删除异常。两个自有Path都覆盖、非拥有Reopen不越权，混合行尾文件做字节级定点修改。

### 最终集成：新鲜完整证据而非迁移次数

T-012在冻结状态运行完整Release构建，0警告0错误；八项目全量（Application、Protocol、Networking、Persistence、Infrastructure、Host、HttpApi、Pvf），保持原xUnit类并行。允许-m:1串行项目，不把程序集串行作为隔离修复。正式dotnet全量在本机FSEvents可用的require_escalated环境执行，保留原deadline，DFO_GAMEPLAY_ASSERTIONS和console detailed完整落盘，环境/命令/退出码及数量同档。sandbox watcher停滞不能解释成业务回归或冒称全部managed根因已证。

8350基线为8330通过、2个已披露DimensionDungeon内容失败、18跳过；表征10/0。保留两条内容失败准确身份及当前AGENTS允许已登记偶发的证明规则，新增失败不能混入基线。逐项目数量与总数按Task增减闭合，原codec/Golden字节/帧序/连接开关断言保持，只有VC-B1/B2/B3/B4/B5/B6明确允许相对A变化。

同A工具重跑执行器外写/网络等待/自等/泄漏取证，七程序集正对照齐，反例类/对照外0；保留exe0仅为观测而非所有吞错证明的边界。旧机制检索包含六处请求AsyncLocal/旧marker/旧pipeline/legacy路由和重载、bus包装、旧重复Error、两个手工Current；生产AsyncLocal最后只允许GameplayJob.Running与DomainTransactionScope.ambient。把命中按实际职责分类，不能只grep类名为零。

最终同A的工具、负载、口径与环境再测执行分位/max、吞吐/等待、并发登录/灌包、冷启；每条CMD max≤2秒即接受。原目标作对照优化，数量级异常按根约定报告；套件总时长/内存只记录。报告合成/非A21实机边界。若测量后再改影响执行的产品，受影响检查/测量需重跑。

最终文档更新AGENTS与架构哲学/新增协议步骤、真实分层及typed登记/单元用法、F-128①。报告包括六根条件、17RQ/11AC与18交接去向、VC-B1/B2/B3/B4/B5/B6、F-131保留及F-132禁用、基线/遗留、测量证据、Astra待审及不推送/PR状态。A封存文件不修改；B最终核验/封存由主控按工作流推进，本Plan不自行宣告完成。

## Next Planning Boundary

当前HEAD为已committed done TASK-32的17d924a06b1237ca2b4f5997983c0a4923dfc050；Task31已交Mailbox，Task32仅调查四remaining opening/Rental六消息旧边界，未闭它们的canonical行。Task31的9075/DFO74+7及Task32的52场/142focused分别是历史全量与有界调查，不是本轮P7执行。Task01～32和P1～P6已接受结果保持；T-004其余Rename/Hatch/SharedEffect/GenCeraTicket、T-005实际租赁/刷新、T-006～012及全部未闭来源/过渡机制仍完整有责。

下一Useful Unit为独立legacy NoRoom正常确认回滚记账修复及真实old decoder RED→GREEN/完整资产、原ACK、账本/原Ordinal retry与非法状态反例；不是whole/注册迁移，不扩大恢复consumer或raw/self权限，不闭canonical迁移行。门后由主控指定TASK-33详细计划；本Plan不物化ID/文件或写执行状态。该单元提交后再以各自旧encoder oracle为切换前置，按共享依赖形成四opening和Rental whole的有用闭合单元，机制已有不先交空壳。两簇可分别完成/审核/本地提交；每条迁移连同所属fine/业务Delivery删除、有限注册/测试/证据闭合，不遗留新whole转旧bus。NoRoom独立修正与可独立纯结构准备不混；whole固有生命周期差异按VC-B6直接验收，不靠额外scope/临时authority模拟旧时点。

P7 ready R1 writer落稿后停止，先fresh有界Plan challenge→另一fresh匹配本稿/immutable挑战的独立review→主控消费B6/P7并amendment-only本地检查点，之后才Task详细规划/实施。B6/Task32或旧P6批准不代P7门。若实际需要扩大恢复来源/consumer、选中参与者或WHOTo其它协议，返回受影响gate，不在Task暗扩。task-units/on-drift、根/child、baseline/A、controller/profile/auto门、Astra/用户收尾和不pushPR/远端/版本tag/部署/游戏/cleanup保持；本轮只有Plan/amendment/旧P6快照，无native/产品/正式测试/routing/gitmutation或新验证结论。

## 本地修订说明

P1（2026-10-24，首次Plan评审前）：按独立reviews/plan-challenges.md的CH-1及PremiumDailyRefreshService现有去重/清零实现，定点更正F-07共享状态、T-010和对应验证。配额保持原周期状态，Premium保持完整权益fingerprint与前次激活flag；扫描仅周期/精确session、子CMD重读事实及VC-B3保持。不引入新可见变化，不修改Spec或immutable挑战；本ready目标仍须首轮独立Plan评审。

P2（2026-10-24，Task10检查点之后）：消费已匹配approved的Spec B2，定点对齐§3显式重放链/唯一ledger、§4具名即时编码登记及最终失败收口、§5窄透传能力、T-004依赖、对应结构/回归与当前Next Planning Boundary；routing-decision只更新四个本轮gate制品路径。详见verification/plan-p2-amendment.md。保持task-units、完整T-001～012路线和其余依赖、Spec baseline/Contract、VC-B1/B2/B3及其他要求，不重写旧P1审查或前10Task。本ready稿待匹配P2独立gate，不自批或提前写产品。

P3（2026-10-24，Task14检查点之后）：按旧四opening的fine外异常响应与DomainTransaction明确完成状态，定点补§4 commit阶段已确认回滚结果、§5共享能力、T-004依赖、对应先旧后新验证和Next Planning Boundary。默认Write/ReadOnly sticky及B2两个raw出口不变；action映射仍各whole内窄业务try＋RequireRollback，不给调用方清失败或自报回滚能力。详见verification/plan-p3-amendment.md。本ready稿仅供fresh受影响challenge/独立Plan review，随后主控amendment-only检查点；routing-decision由主控更新，旧制品与Spec不改，门后才详细规划下一Task和产品写。

P4（2026-10-25，Task19检查点之后）：消费已匹配approved的Spec B3，仅补CollectionBox首次成功Main结束后的第三具名原batch出口。§4确定通用SettledInitialBatch、穷尽阶段判定、现有opaque接口透传和实际adapter batch/required调用矩阵；whole原Main/footprint、四namedcatch/普通SQL及结果非空边界保持。§5、T-004、对应先旧后新直接oracle/默认反例及当前下一规划边界受影响更新；P3 commit-result/来源guard/保因、默认sticky及原两许可、Compose/F-132和其它路线保持。详见verification/plan-p4-amendment.md，原P3逐字节快照另存；本轮无native/产品/Task20、无路由或git写入。ready仅供fresh Plan challenge/另一独立review，主控门后amendment-only检查点再指定临近Task。

P5（2026-10-25，Task28检查点之后）：消费已匹配approved的Spec B4 ready R2及immutable CH-1处置，仅给Cera增加现有WriteResultAsync/KnownStorage commit-only结果入口的具名消费；原一次普通写声明NonRequest和单精确买家，窄action映射/RequireRollback(cause)与commit确认回滚原响应分开，正常拒绝不强回滚。VC-B4的C1/C2/C3默认失败及精确actor Fence/连接阻断/DiscardExact/关闭、scope前已登记消息按既有顺序落实，不为旧空集合改core保护，不增raw出口或Compose。§4/§5、T-004、对应direct before/after/强测试和当前NextBoundary受影响；P3/P4历史要求保原文并明确其旧起点。详见verification/plan-p5-amendment.md及原P4逐bytes快照。本轮无native/产品/Task29、无routing/git写入；ready仅供fresh challenge/独立Plan review，门后主控amendment-only检查点再推进。


P6（2026-10-25，Task30检查点之后）：消费fresh approved B5，独立self结果入口以真实Tx DomainActionAbort事实、直接await单次交接和完整cleanup恢复；原五consumer/commit-only/defaultsticky不变。Mailbox六NonEffect/空selected ordinary保原事务/投影/current门，共享RunAsync仅传cause最小修C2双因/typed；VC-B5的真实source-close/chain、replacement物理隔离、已登记帧和独立同cause/来源反例及共享caller/八freshfull入计划。T-004残余/T-005市场租赁/T-006～012保持，Next从已交Cera转Mailbox有用闭合。详见verification/plan-p6-amendment.md和原P5逐bytes快照；无native/产品/Task31/routing/git写，ready不自批。


P6 ready R2（2026-10-25，immutable P6 R1挑战后、独立review前的单次最小高风险纠正）：主控接受CH-1，真实自回滚的unit.End→owner.Close及Currentnull早于outerobserver接错，R1的observer后pending遗漏下层同provider代理窗口。将本次受观察AbortAction的barrier同步pin前移到入口、settled/任何rollback await/回调之前，owner.Close不清pin；完成fact仍只能在真实rollback/Completion后签。observer仅消费/交接，runner完整cleanup/健康检查后才解除健康候选；下层Register/Begin guard occurrence不得被fact或原cause传播冲掉。补下层proxy窗口两路真实拒绝/延后pin或Close清pin的行为mutation义务，保持无观察shared与原五consumer/WHAT/A发送边界。R1 Plan/amendment原bytes已另存；immutable reviews/plan-p6-challenges.md不改/不重生，本R2未运行/未approved，交另一fresh reviewer独立裁决CH-1。


P7（2026-10-25，Task32检查点之后）：消费fresh approved B6，先独立修legacy正常确认回滚记账/NoRoom，再四remaining opening与Rental按真实旧新encoder oracle和共享依赖归并。仅四opening增加现WriteResult具名consumer（现十→十四），Rental保WriteAsync异常不恢复；六Main/独立premiumRO/RentalrefreshTx、unit.Footprint/最低投影、VC-B6 wholeScope/独立刷新default typed/精确actor/实际来源链及条目存活、Committed ledger与纯ConfirmedRollback后第二错误分开。六有限replay owner不提前授证、不增raw/Self/通用机制；根纯搬迁与语义提交分离落实为NoRoom独立前置和无无关纯搬动的whole闭合。T-004/005、§4/§5、验证/NextBoundary更新，T-006～012、A/Contract/baseline/门保持，原P1～P6历史段逐bytes保留。详见verification/plan-p7-amendment.md及原P6快照。本轮metadata-only，未建Task/运行native/改产品/正式测试/canonical/routing/Git，ready不等于approved。

### P8（2026-10-25）：TASK-39 多 owner 无账本足迹

补齐既定显式足迹方案对非入站队员效果的表达能力，新增独立声明模式，保留单 actor NonRequest 及独立失败保护集合。只改变跨簇共享 HOW；不改 RQ／AC、中心方案、原保护范围或外部授权。受影响审查见 `reviews/task-39-plan-review.md`，未批准前不实施机制增量。

### P9（2026-10-25）：TASK-45 祭坛纯唤醒接缝

R1独立审查指出Spec RQ-009未包含纯唤醒类别，主控接受；本R2以B7的具名能力及验收边界为前置，不再由Plan自行扩张封闭清单。

实际依据：`MonsterDeathSlice.cs @ cc212b99`在成功death／clear投影后调用`AltarProjectionDelivery.Wake`，其唯一动作是`AltarWakeSignal.Wake`写原bounded Channel。`NetworkMessageDispatcher.DispatchProtocolAsync`的薄handler在执行器外；把wake放在SendAsync返回之后会改变FIFO机会以及whole scope失败时是否发出信号，不能作为等价迁移。也不能把Action／signal塞进command输入、假借DomainService名称或扩ICommandProjections.Direct绕过边界。

新增Abstractions/World的`IAltarSchedulerWakeSignal`仅声明`void Wake()`。原Protocol/AltarWakeSignal实现它，ProtocolModule把接口alias到同singleton；原Scheduler仍持有原实例的Wait，未迁Delivery／Coordinator继续原调用。仅为此精确接口在构造器判据添加具名理由，任意相似接口、旧Delivery、完整signal具体类型与队列继续拒绝。新MonsterDeath whole在原body／try中、原先所有投影之后调用；不推迟到外层handler或发件箱发送，不提早到领域Commit。不新增Main／footprint／raw／Self／恢复或调度状态，不关闭Timer来源行。

实现验收：正式旧新测试观察wake仍在当前job且在成功death／clear投影之后；相同scope故障前信号仍发生，投影先失败时不唤醒。验证interface与原signal／scheduler同一个实例，重复wake仍按原Channel合并、取消／deadline等待保持；保旧Altar调度器原正式用例。新增白名单正反例，不用接口命名绕过判据。冻结后Release build及八项目fresh full，原并行／5m／DFO与逐用例库存保持；此处仅规划，未冒执行通过。受影响Plan审查单独记录，主控批准规划检查点后才实施；最终同Task结果快审再次验证真实产品。
