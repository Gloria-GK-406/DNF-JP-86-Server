---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification：结果 B——协议级 CMD 与显式请求上下文

## Why and Outcome

一个入站协议的业务执行只有一个 CMD：身份与当前性判定、领域调用、各段提交、投影登记和补偿都在这一个用例内可见。网络 handler 把已解包的消息和物理连接身份交给入队边界并等待结果；它不再编排细粒度命令。

A 已提供唯一串行执行器、提交后发件箱、发送端、删锁及调用链。B 在这些结果上改造编程模型，并删掉 A 的六类过渡机制；保留保护意图、事务边界和客户端行为。不得用重命名 handler 或增加一层转发，使旧编排原封不动藏在 CMD 后面。

- 已接受的产品起点：A seal `2079d98d`，对应产品检查点 `d602592c`；历史文档中的旧提交号按 [历史改写对照表](../../history-rewrite-2026-10-25.md) 换算，A 封存制品不改。
- 本 Spec baseline：`51bd1e9c`；契约为同目录 `TaskContract.md` revision 1，根契约 revision 4 均保持。
- 权威来源：根契约 revision 4；父 `../../spec.md` 的 R3 分界、RQ-020～025、RQ-070～072、AC-008～010和附录 C/H；`../../verification/transition-mechanisms.md`；A 最终汇报和18项交接。
- 本文 CMD 是执行单位，和 Game 出站 `0x01` CMD 类别不是一个概念。出站 CMD/NOTI 的类型安全继续成立。

## Scope

包含协议和内部 CMD 的归并、显式请求状态、显式工作单元、保护迁移、过渡删除，以及对应判据、测试、测量和文档。A 的 SC-1、SC-3～SC-7 是 B 的持续不变量，不因更换编程模型而放宽。

不重新设计执行器或发送端，不按频道分流，不删除短锁与 PVF 内容缓存锁；不换数据库、内存权威副本、表结构或协议字段，不改客户端，不处理 #209/#210。F-127/F-130 是已移交的有界修正，不受“不重做 A”排除。A 的封存制品保持不变；新发现和证据更正只写 B 或根后继记录。

F-130 与 F-127 按既有约定进入 B 首个单元。F-88/F-93 的守护证据先于对应保护迁移；F-132 的区分先于任何工作单元内组装调用。其余局部拆分和接口名称由 Plan 决定，本文不预写 Task 清单。

**当前版本为 B10 ready R1。** 依据用户 [流程 §8](../../process-reset-2026-10-25.md) 统一回写 [phase2-backlog](verification/phase2-backlog.md) 的冲刺裁定。TASK-65 检查点 `5f66b5b0` 已使五类迁移账 pending 全部为零，允许开始第二阶段；此事实不代替结构收口、最终测量或完整结果评审。下文 B10 与 VC-B40 是冲刺后续的当前约束，早期修订记录、旧逐协议取证要求和当时的门禁保留为历史，不要求重做已完成取证。根／子契约、17 项 RQ、11 项 AC、中心方案及外部收尾限制不变。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 对全部**已注册**非遥测入站消息，`ClientMessageType → 协议 CMD 类型` 是单值全映射；一 CMD 可处理多个消息号。未注册的枚举成员不要求补接线，未注册帧的静默丢弃语义不变。网络 handler 只交付解包消息及精确连接身份、入队、等待；不得注入领域服务、登记能力、发送器、门或关闭器。纯遥测例外沿用 A 的14消息/2 handler，只注入日志且不改状态、不写库、不发包。 |
| RQ-002 | 生命周期、定时/延时、事件消费/重试、GM/HTTP 和启动等非协议来源采用同样的 CMD 执行形状，以自己的显式输入进入唯一执行器；不由外部生产者直接改世界或登记出站。保留来源合并、FIFO、链继承、深度上限、停机排空及入队拒绝语义；事件的 Direct 订阅仍在调用方 CMD 内执行。 两塔返回来源仅按下文 B9 专节，将一轮批处理分成扫描和逐离场／逐副本 CMD；明确允许项间请求交错、分项发件箱交付及新增离场延后，不将该许可扩到其他来源。 |
| RQ-003 | 协议 CMD 吸收原 handler、Delivery、Coordinator/Coordination 中的具名判定、领域调用、各段工作单元、投影登记、补偿和回滚。CMD 不直接或间接调用另一个 CMD；共用业务收进领域服务或应用服务。旧细粒度命令并入、改成真正共用的应用能力或删除，不留仅转发旧命令的最终结构。纯帧辅助及真实状态所有者不因名字含 Delivery 而删除。最终协议 CMD 不继承共用基类；生产 CMD 不以可空依赖、null! 或 Func 能力隐藏不完整的协作关系，具体界线与反例见 B10。 |
| RQ-004 | 请求上下文是本次 CMD 的显式数据，在执行器上、CMD 开始时建立：物理连接、入站元数据、捕获权威、选中接收者及适用的效果账本状态。封锁/被顶替检查与权威捕获在此执行，过期旧连接保持 A 的拒绝/静默丢弃行为。不得依赖请求 AsyncLocal、隐式“当前请求”、单例可变字段，或借环境事务重新隐藏请求状态。把 singleton 换成 scoped 本身不构成完成；所有消费方都须拿到正确的本次输入。 |
| RQ-005 | 协议请求的三种合法状态按下表区分；效果结算缺应有的状态是程序错误，不能“没有帧就放行”。受信任内部来源须明确由内部入口建立，不能用缺少网络上下文推断；已失效请求或泄漏续体不得降级为受信任内部 CMD。上下文结束后不能继续使用其结算权威。 |
| RQ-006 | 保留 #185 具名判定、一次效果请求一个主结算、已提交同序号重放、序号前进约束、Pending 结束拒绝、登录顶替检查，以及协议槽位到 BusinessSlot 的边界。参与者集合包含报告会话，逐一复查当前性；结果不确定及既有受保护的提交后非传输失败仍按原范围封锁每个参与者，保留 `DiscardExact` 对精确旧会话的隔离，不得缩减为仅封锁请求者。原细粒度 CMD 返回后的具名即时 raw 编码登记出口按下文“即时登记失败的等价边界”保留，不因编排搬入协议 CMD 而新增围栏，也不据此放宽其他提交后失败保护。Cera 购买旧路径吞并不确定／独立清理失败的行为及本次精确买家纳入默认参与者保护的新增范围，仅按下文 VC-B4 明确改变；Mailbox 的不可恢复失败收口及共享自回滚附加失败的原因图精度仅按下文专节和 VC-B5 明确改变，邮箱不新增 Cera actor／footprint／Fence／DiscardExact。CompoundItem／LotteryOpening／VendingMachine／EpicCraft 与 RentEquipment／ChargeRentPoint 的 NoRoom、独立 scope／刷新失败按下文专节及 VC-B6 明列；三个 raw 出口类型不变，该六消息仅按专节的逐具名旧新证据条件保留既有 committed replay 出口，其余协议保护不变。 |
| RQ-007 | 每个旧 Transactional/ReadOnly 边界对应 CMD 内一个显式工作单元：写、只读、普通拒绝 `RequireRollback`、主结算声明与确认/冻结语义保留。合并或拆分提交点必须逐项列出理由和行为证据，不能把多阶段选角或补偿包成一个事务。持久提交后立即更新内存的既有临界序列不能因搬迁新增取消点或重新读取世界状态而改变结果。Cera 正常拒绝、可恢复动作失败及提交阶段确认回滚的结果按下文专节区分，不能将正常返回拒绝一律改为回滚。Mailbox 原领域 action 内即时自回滚、Current=null 及共享 caught refusal 后 Commit 必须抛错的语义保持，不能为迁移改为延迟 RequireRollback 或另开第二事务；恢复与原因图结果另按 Mailbox 专节。四 remaining opening 与 Rental 的各自 Main、Lottery mode1 独立只读和 Rental 独立写刷新逐一保留，健康 NoRoom 正常回滚拒绝的有意修正另按 VC-B6，不将正常拒绝、异常确认回滚和已提交后失败混同。 |
| RQ-008 | 采用工作单元内组装时，真实回滚与提交后的发布/组装失败必须区分。已经提交的写不能记成 ConfirmedRollback 并允许同序号再次生效；单纯 IsSettled 不作为“已提交”的证明。采用工作单元内组装前，先有“持久写已经生效、随后组装取消/失败、重发不重复结算”的区分证据；若未采用，则记录该路径的禁用/不可达边界，不冒称F-132已修。可靠事件的业务写入与完成标记同事务；LiveOnly 仍保持消费提交与外部投递的原有先后，不能套同一默认事务。 |
| RQ-009 | CMD 仅登记本 CMD 发件箱，不等待网络、不触达传输发送。CMD 注入面只含领域服务/领域 store、既有 TimeProvider/IPartyService 例外，以及有具名理由的发件箱登记、关闭、投影登记、槽位映射、日志、只管理当前CMD显式工作单元的机制能力，以及下文限定的祭坛纯唤醒、翻牌延时生产者控制和 B10 逐项具名的固定机制能力；禁止发送接口、入队点、执行器、门、行网关、持久化接缝、内容源和规则服务。不得经 service locator 或转发门面藏这些依赖。具体机制能力由 Plan 具名说明，不默默放宽父 RQ-070。 |
| RQ-010 | F-130：Application.Tests 的清理只作用于自己拥有的数据库/连接池，不调用进程级 ClearAllPools；覆盖实际写池和只读池及规范化路径，在自己的服务/连接释放后清理。保持测试类并行与原断言/期限，不用串行整个程序集、新 Skip 或调大超时替代隔离。此项限定已移交的 App25个调用文件；全库92个调用文件跨多个测试进程，不据此扩大本项范围。 |
| RQ-011 | F-127：发送已经失败且写端确已完成或正在断开时，两条“entry/connection Failed”日志按连接已不在的既有规则降为 Information；普通故障仍 Warning。只改变失败日志的 gone 分类，不把 Resolve 的整体语义改成不可解析，不改变 Failed 结局、关闭/observer 通告、逐连接隔离或队列中条目的处置。不将所有 InvalidOperationException 一律视为关闭。 |
| RQ-012 | A 的 T1～T6及 B 迁移期间为并存引入的临时路由/适配最终全部删除，语义与检索均清零，见下表。常设发件箱登记适配器、作业身份和环境事务不算残留；但不得在这些机制中重藏请求状态。带来源连接的 CMD 即使从未登记条目就失败，也必须按原顺序关闭来源，不依赖两个 dispatcher 手工触碰 Current。 |
| RQ-013 | 18项交接及另列事项按下表逐项给出迁移结果或具名裁定，不能只换主人后声称关闭。F-131 本 Spec **维持 A 的已披露现状**：CMD已登记关闭但发送端未执行时，若传输先关闭，仍沿登记链继承；其余传输自发关闭为新根。此精度差距继续交外审，不作为未解决输入；若之后选择纠正，须先在本 Spec 列明变化和证据，不能静默改变。 |
| RQ-014 | A 的顺序、发件箱、事件可靠性、调用链/深度及执行器外写入/网络等待/自我等待判据不得削弱。重写冲突的结构判据时保留保护意图；“handler只入队”“注册协议有唯一CMD”“CMD注入面”“CMD不调用CMD”等每条规则均有会拒绝的反例。未处理失败只由执行器记录一条带链 Error，生产者保持 A 的降级规则。 |
| RQ-015 | 构建0警告0错误；完整测试仅容许 A 已披露的两条内容基线及按 AGENTS 身份规则确认的已登记偶发。表征、线级字节/帧序/连接开关、codec 与 Golden 断言保持，除本文列明的变化。测试数量从8350起逐单元写出增减加法；只改驱动/准备或改写断言的文件分别说明，迁移后的测试不得用不同容器的登记器冒充被测工作单元的发件箱。 |
| RQ-016 | B 封存状态用 A 的同套工具、口径和负载再测并对照；记录执行时长分位/max、吞吐、等待、并发登录/灌包、冷启与环境。单条 CMD max≤2秒为接受，原性能目标只作对照；数量级问题按根约定回报用户。测试套件时间和内存只记录，F-69的耗时诊断不是性能停止门。 |
| RQ-017 | 文档描述最终协议模型、工作单元、入队、注入和新增协议步骤，不再把过渡机制写成现状；更新AGENTS/架构哲学及F-128①登录示例。最终汇报列结果、相对A变化、遗留/裁定、测试与测量证据，作为整分支Astra输入。全过程中文、原分支、本地检查点；不运行df_game_r/DNF.exe，不推送/PR/合并/远端issue/部署/清理。 |

### 注入约束相对父规格的明确说明（RQ-009）

父RQ-070的保护意图继续成立：CMD不能注入发送接口、队列、执行器或存储/内容接缝。登记能力须以自身契约表达，不能仅因旧Sender在DI中被映射到发件箱就放行，也不能用service locator藏依赖；CMD/NOTI类型安全不变。

本Spec按主控2026-10-24的明确裁定，具名增加“当前CMD的显式工作单元机制”这一能力类别：它只承担事务与结算边界，不入队、不发送、不读取业务状态，也不是隐式请求容器。理由是细粒度管线删除后，CMD仍须明确声明原有提交点；具体接口名称、允许的登记/机制清单及逐项判据理由由Plan确定。这是明示的能力范围，不是任意DI豁免。

B7具名增加祭坛纯唤醒能力，只允许CMD在原用例位置向已有祭坛调度生产者发出无参数、无需等待的唤醒提示。它不能读取或修改世界状态，不能携带命令、请求、回调或业务快照，不能入队或等待CMD，不能暴露任意调度／取消／等待能力，也不形成新的调度状态所有者。原生产者的合并、deadline、取消及入队规则保持；接口名称、同实例接线和精确判据由Plan确定。该增量只为保留普通击杀在当前作业内、成功投影之后的既有唤醒时点，不授权把唤醒移到薄handler返回后、事务提交点或发件箱发送阶段。其它技术注入类别不因此扩张。

B8另具名增加翻牌延时生产者的固定阶段控制能力。CMD只可在原用例位置，向原同一个翻牌延时生产者同步请求“结算后布局”“布局后自动确认”或取消精确角色／run的待办；输入仅为既有精确会话和run身份，不含命令、请求上下文、回调、业务快照或调用方选择的延时。能力本身不查询或修改游戏世界、不发送、不直接入队或等待CMD，不暴露通用调度、任意阶段、任意任务或停机等待；生产者原有待办、替换、取消及drain仍由原单一状态所有者管理。真正到期后，由生产者在当前作业之外使用既有typed内部CMD入口；当前CMD不得借控制能力等待它。接口、同实例接线和精确类型判据由Plan具名。这只保留既有翻牌时点和取消语义，不能把调用挪到薄handler返回后、事务提交钩子或发件箱传输阶段，不扩其它调度者或技术注入类别。


### 冲刺裁定与最终模型（B10，RQ-002～009、012～017）

本节把已实施的具名裁定纳入同一个可验收边界。它不新增通用恢复许可、请求 ambient、发送或嵌套派发出口。来源及逐项对应见 [B10 回写记录](verification/spec-b10-consolidation.md)；具体阶段顺序仍由迁移账的旧边界／实际单元对应和既有业务测试负责，不能只凭新的类型名称声称等价。

#### 具名注入与协议能力

领域服务／store 及原 TimeProvider、IPartyService 规则保持。下表是确切契约的有限补充，不能按 Query、Factory、Options、Dispatch 等后缀放行相似类型；注册机制内部可以解析其固定注册项，不能把 resolver、任意业务委托或 Services 交给 CMD。

| 具名能力 | 允许结果及边界 |
|---|---|
| IReplayStateProjectionSink、ISelectedSessionSnapshotQuery | Joust 按原采样点捕获精确选中集；下注成功仍 DetailsChanged→独立当前态 Write／Inventory 与 Joust 投影→ACK。首次投影以无登记豁免的调用进入原 sink，不嵌套 ReadReplayState CMD、不新增 raw 权利。 |
| ICommandSelectedSessionCleanup | 选角／回选及已具名的断开生命周期只能执行固定精确清理；不是可接任意动作的失败后恢复能力，限制见下文。 |
| ISelectedCharacterStateMessageFactory、SelectCharacterDeliveryOptions、LotteryCounterExperimentOptions | 原选角消息组装与 EncodeBody 预校验、原尾帧配置及默认关闭的彩票实验选项；不查询世界，不成为游戏规则输入或通用 factory／options 类别。 |
| ICommandHttpFailureAudit | 只为本次合法 HTTP 充值／邮件失败记录执行一次固定终止审计，见 HTTP 专节；不恢复普通单元、投影或请求成功。 |
| ICommandConnectionLifecycle | 固定 Connected／Disconnected／DepthRootClose 注册分发，保原 observer 顺序及深度处理；仅匹配实际内部 CMD／Lifecycle 来源可用，不接业务 callback。 |
| ICommandScheduledTaskDispatch | 只分发已注册 typed 计划任务，状态 ticket 只表达调度事实；不传世界快照、任意 Func 或服务定位器。 |
| ICommandDomainEventConsumption | 仅匹配 Consume／Settle 与 Event／StartupRecovery 来源，执行原可靠或 LiveOnly 消费阶段；不向 CMD 暴露持久化、动态服务解析或任意订阅执行。 |

既有 ICommandConnectionClosures 的 ResolveAndRequestCloseAsync 仅保选角原 pendingCloses 中其他连接 ID 在最后关闭点解析的语义：登记的是当时取得的真实物理 context，来源仍使用 exact 方法，不用可能已删除的在线记录替代传输身份，也不改变 F-131 精度。IChannelEntryPolicy 临时别名删除后，ResolveAsync／Refuse 归现有 IChannelEntryDomainService，与原具体实现同 singleton；不保默认兼容实现。

#### 工作单元、正常拒绝和失败事实

VC-B40 是 TASK-58 起的统一基础设施默认失败规则；正常拒绝、原错误码、实际事务点、受众和帧序仍保持。已经批准的三个具名即时登记出口继续受其严格来源、目标、阶段及完整清理条件约束，不成为一般编码异常恢复。Housing、Capsule、WeddingLovePoint 只增加既有 CommittedReplayCurrentStateBatch 的 owner：仅已提交同序号请求的当前态重放可用，首次成功、拒绝、Main 和其它出口不获许可；没有第四类 raw 出口。

现有 WriteResult 的 Returned 与真实 KnownStorage／FallibleCommit 后健康 ConfirmedRollback 继续区分。Joust 下注复用该结果保原 ACK6；catch 只包原领域动作，槽位映射和守卫在外。七个定时单元 creature.clock、altar.deadline、tournament.auto-reward、wedding.advance、wedding.cleanup、altar.wipe、tournament.wipe，以及五个计划任务的逐角色写、可靠事件消费，按原业务 catch 范围复用健康结果；Joust／Weakness 推进和只读 snapshot 不因此取得同样许可。取消、不确定、guard、scope／独立清理或未知来源失败仍默认失败，不能按异常类型或完成枚举猜测恢复。

ReadOnly action 可显式得到当前只读 unit 并 RequireRollback，以保四个 HTTP Events／Joust 查询原正常拒绝的真实 Abort；不授权写入、Main、footprint、Self 或提交失败恢复。DomainTransaction 在真实 AbortAction 完成处记录的内部事实，只供固定 HTTP 审计辨识原领域已自回滚；它绑定本事务、原 cause、真实完成输出与发生身份，不能复活已结束事务，也不扩大既有 Self 观察／消费／Pin 的资格。

CommandRequestContext.HasOnlyFailure 仅观察本次已记录的一个失败发生及其原 cause；普通传播可保持同一发生，独立清理即使复用同一异常对象仍是第二发生。该观察不清 sticky、不授权新工作；选角过期物品原正常拒绝可保原 0x02，额外基础设施失败不能借此被吞。

#### 选角、回选与连接生命周期

选角原准备、每日补货、任务、宠物、发布及 PvP 阶段，以及回选与失败补偿的原边界保持；不合成一个事务。固定 cleanup 在入口冻结精确目标和两塔当前 run 的原所有 owner，保剩余队员结算与原独立位置事务，owner 不自动扩大失败保护集合。仅本次未关闭 request／job 和具名注册可用；单元内须处于该请求唯一 unit 的真实 action，单元外须无在途操作。清理期间拒绝普通新单元和重叠清理，未 await 的清理仍须排空；结束即失效。失效权威被观察后记录失败，不能恢复成功、另建作业或嵌套派发。

Connected、Disconnected、DepthRootClose 都由真实 typed 入口驱动具名注册 handler，producer 只保来源链、深度降级和入队。Connected 保真实 physical source；已断开的 Disconnected 只在 Lifecycle／无来源连接的真实命令上取得既有精确 cleanup 资格，保 orphan、Standing、代次、固定投递与释放顺序，不虚造来源。RecordDisconnectedDeliveryFailure 仅记录同一活跃 job 的本次断开清理后投递失败或其独立次因，使原匹配连接继续释放；最终请求仍失败，普通业务不得恢复。

#### HTTP 与 GM 的统一边界

四十二条在范围内的 HTTP／GM 路由由同一个路由过滤器提交纯数据 HttpRouteCommand；三十九条普通路由与三个原自入队路由共用同一 CLR 命令。HttpApi／GmWeb 保原解析、鉴权 middleware、凭据计算／限流／重验证与响应映射；framework next 只在执行器外生成封闭输入，HttpContext、IResult、Services、执行委托及隐式 current 不进入 CMD。实际 operation／输入类型／Http 或 Gm 来源必须匹配。前置失败与取消保持原入队边界、None admission、body token 和 FailedBody 语义。

充值／邮件的失败审计必须同时匹配本次合法 requestId、实际输入、HttpRouteCommand、真实 origin／job 及完整 runner 失败事实。一次固定 Cera／Mail RecordFailure 审计不接业务委托，受 request／job／lifetime／无在途操作限制并参与排空；不清 sticky，不恢复普通写或投影，不增加 actor／footprint／raw 或第二作业。审计动作自身不可用可以形成 AuditFailed；它的独立 rollback／dispose、guard、scope 等错误会撤销失败响应搬运资格。

只有完整 drain、关闭 request、scope Dispose、健康检查和默认 FailAsync 都完成后，才能把真实签发的阶段及审计失败图包装为数据响应；原作业仍失败，保原 cause。外部映射必须匹配同一命令对象和实际 origin／job；跨请求复用同一 cause 或同一命令对象都不能借旧资格获得响应。不得从任意内部异常中捞未经确认的 HTTP 结果。

本次 RequestAborted 已实际取消时，四个固定 Events／Joust ReadOnly action 可沿原 body-only 观察规则带回原取消；须同一 producer token、真实 action 发生、完整健康 Abort／Dispose、无既有提交或独立错误。充值／邮件输入已读出后的准备或执行取消，则保原独立五秒 token 的失败审计及原 Rejected／RolledBack／真实 Unknown 记录；它不是未请求取消的豁免。未请求取消、guard、独立清理或未知阶段都不能取得这两项权限。

#### 定时来源、日切水位与取消

十九个剩余定时来源及共用推进体的 Joust 启动恢复使用 typed 内部命令和整轮 producer 生命周期；此前两塔返回和翻牌 delay 的已接受边界保持。公共 producer 只管理触发、明确 origin／链、busy、取消与排空，不持世界事实或任意业务 callback。F-07 整轮串行扫描与逐精确会话 FIFO，单 CMD 来源的 manual 立即入队保 FIFO、hosted 只合并自身；所有已接受轮次都纳入排空，不能提前从跟踪集合移除。唯一整轮 SemaphoreSlim 只在执行器外协调 F-07 producer，不是世界状态门。

Login 会话到期、Joust observed／openedRounds、房间机关 run／roomVersion 投影截止及去重，分别归 ILoginSessionMaintenanceDomainService、IJoustMaintenanceDomainService、IRoomMechanismMaintenanceDomainService 的真实 owner；保三十秒清扫、启动静默基线、完成轮次先发布／发布前消费，以及机关成功后记账／失败下轮重试。经验启动归一由 ICharacterExperienceInitializationDomainService 调原经验规则与存储策略；EpicBook 和经验初始化留原启动位置，不新增外层事务或改变 readiness。

F-07 的 Premium／ItemUsage／Fatigue 及七类 quota 只在真实登记接受后推进当前周期与精确会话水位。单条 RegisterWithOutcomeAsync 与 batch RegisterBatchWithOutcomeAsync 必须沿原 TryFind→Encode→Register 路径；实际 Register 完成才为 true，Dropped／空批及不支持结果的 sender 不猜成功。NotificationAttempt 保原 Outcome，另给默认 false 的 RegistrationAccepted；它不代表落线或客户端收到。保原每条尝试顺序、逐收件人隔离、GM 汇总与旧 void 消费者语义。

PremiumSnapshot／ItemUsageSnapshot 是默认 None 的封闭只读取消资格，仅实际注册的 Internal owner、Timer＋BodyOnly、本次 request 及固定 premium.snapshot／item-usage.snapshot 单元可观察真实 action 取消。ConfirmedRollbackWriteResult 同为显式 Internal 注册资格，仅批准的 Scheduled／Consume 与七个定时单元 owner，在 Timer／Event／StartupRecovery、BodyOnly、ordinary writable、Footprint.None、无既有提交的真实 action 使用。两者都要经过同一已取消 producer token、完整健康 Abort／Dispose、唯一失败发生及既有 Finalize 检查；commit、DI、lookup、Main／Self／Declared、未请求取消、guard 或独立清理不获资格。不能把“取消观察”实现成继续业务或清除失败状态。

Creature 时钟原正常投影业务失败仍按逐目标 Warning、精确目标关闭及继续范围处理，原未声明 selected／account 的单元不新增参与者。真实 outbox／close 阶段必须与普通领域读取／受众解析区分；登记、scope／清理、取消、不确定或 sticky 故障按默认失败，不补 owner 关闭后恢复下一项。Direct、Project、NotificationBus 的实际 scope Dispose 失败以内部阶段事实保双因，Joust／Room 不得把它当普通广播失败；正常订阅和既有逐收件人 Failed 隔离保持。

#### 计划任务与事件消费

计划任务只用 typed ticket 表示 Requested／Started／Attempt／Id 等调度事实；在实际 CMD 入口取最新合并时间，保 due 先于 completion、RunOnce／Skip、开始后 Pending、重试和排空。固定机制在 fresh scope 解析已注册 handler，五个真实逐角色循环保原当前性检查、参与者、各自事务与健康失败后继续；没有任意 Func／service locator 交给 CMD。

五任务收集的健康业务失败由具名完成桥在原作业中抛原 Aggregate：须本次 ScheduledGameplayTaskCommand、TrustedInternal Timer、nullSource、同 request／job，并且 ExplicitAsync 的 drain、request 关闭、scope Dispose 和健康检查全部成功，Failure 为空、操作已结束。这样保一次执行器 Error 和原 producer 重试，不把已成功角色误判为 whole 不确定失败。任何独立 scope、编码、guard、取消或不确定失败先走默认路径；不跳 FailAsync，不新建作业，不公开任意失败构造或后置回调。

Reliable 事件保 claim→fresh subscriber scope→Invoke→Complete、成功 Commit／Forget 后 Dispose 的原顺序，业务写与完成标记同事务。业务动作失败先完成原 subscriber scope 清理，再 RequireRollback；完整健康结果后才执行独立 Fail 重试事务，健康提交确认回滚复用 WriteResult。LiveOnly 先消费提交再提示、不重放，Direct 仍在调用方 CMD 内；深度终结保原终态与 root close。取消、不确定、scope、编码或 guard 失败不签发业务 retry 资格，不能复制工作单元 runner 或清 sticky。

每 job 的 outbox 仅在实际必达单条／batch／required explicit encoder catch 记录无权限的失败发生及原 cause、精确目标；optional explicit 原隔离不记。NotificationBus 的固定逐收件人边界只能在真实批登记的窄 catch 中，匹配同一活跃 job／outbox、同目标引用、同 cause、恰一条新发生且观察未结束时，追加该发生已由原隔离边界处理的事实。原失败事实不删、同 cause 的另一发生不复用；事件 Invoke 区间有未处理必达失败时仍拒绝 retry，即使订阅者吞错返回。没有公开 bypass、任意清错、新 ambient 或恢复权限。

#### 最终结构、删除与文档

协议 CMD 必须自行拥有用例编排，不继承 PartyMutationProtocolCommandHandler 或其它共用 CMD 基类；不能换成一个巨大的依赖袋、转发门面或改名基类来隐藏原编排。共享领域能力归真实领域 owner；只涉及协议的共用阶段可以按明确职责复用，但每个 CMD 的判定、工作单元、补偿和投影顺序须可见，不能再调用另一 CMD。生产 CMD 的构造依赖及协作能力为完整非空契约，删除为不同子类填空的 null!、可空服务参数、默认缺依赖和 Func 传能力。业务数据本身可空、纯值变换、既有显式工作单元 action 不因此被禁止；不能用这些合法形式重藏服务定位或执行任意业务的能力。

判据遍历实际生产 CMD，并有会拒绝的继承、可空依赖和委托能力反例；原注入类别、嵌套、薄 handler、注册闭合、source detach 位置及 Compose 禁用反例保持。六处请求 ambient、legacy 路由／重载、旧 fine、五 pipeline 和两项临时适配语义与检索清零，只保作业身份与领域事务的必要 AsyncLocal。未注册消息的技术拒绝归固定 CommandJobEntry：带真实 Connection 链／source 的作业和 scope 内初始化发件箱，先按原 blocked／superseded 规则处理，再抛原 lookup 失败；已注册消息误入也拒绝，不执行 handler，不收 callback。两个 dispatcher 仅传值，不触 Current／Begin 或重建业务作业。

T5 的唯一 Error 指同一次向执行器传播的未处理失败；四个 scheduled 逐项、Unknown settlement、MoveMap 定位保无栈 Warning，SelectDungeon 纯复述可为 Debug，原 cause、顺序与定位保留。Outbox Submit 拒绝仅在此前健康作业由 hookError 成为执行器最终 Error；此前失败／取消时被隔离的独立 Submit 错误保 Error。DiscardExact 独立清理错误及已按业务契约处理而不再传播的 Error 保留，不以全局 Error 清零改变 A 的失败选择或关闭机制。

内存配置测试宿主可以明确关闭不参与断言的 JSON 热重载，仍使用真实 Host／DI／启动和排空；生产配置及热重载功能测试不受影响。原生 SQLite 提交测试以真实事务阶段、KnownStorage／FallibleCommit 来源、完成事实及原 cause 判断，不用异步栈帧名字替代；完整资产、外键、已执行写、回滚与重试断言不减。既有测试迁当前驱动不得消失其保护意图，删旧状态机专属用例须有明确去向，保失败及跳过准确身份。

最终整理 AGENTS、架构哲学、实现／新增协议文档和误导命名，描述当前 typed CMD、显式单元与真实有限机制；历史 A 和各 Task 取证不改写为当前事实。五账全闭合只是第二阶段入口：之后仍须结构检查、八项目正式全量、表征／线级／codec／Golden、同 A 的 G1 工具和负载测量（每条 CMD max≤2秒）、一次完整结果挑战与独立评审。完成后封存 B 并交最终汇报，停等用户安排外部 Astra；内部 Spec／结果评审都不替代外部审核，也不授权任何推送、PR、合并、删除分支或工作树。

### 请求状态与结算许可（RQ-004～006）

| 来源/状态 | 账本与权威 | 允许行为 | 必须拒绝的错误 |
|---|---|---|---|
| 协议：非效果消息 | 无账本条目合法；保留适用登录权威 | 正常执行该协议 | 不能把“无条目”一概当错 |
| 协议：效果请求、未选中会话 | 显式的“可拒绝、不可结算”状态 | 发送原拒绝结果 | 不能开始主结算或写出效果 |
| 协议：效果请求、已选中会话 | 精确接收者、账本条目、序号与主结算状态 | 一次主结算；已提交请求重放 | 缺上下文、重复主结算、旧会话或非法序号不得放行 |
| 受信任内部来源 | 内部入口明确建立自己的执行输入；不伪造网络请求 | 保持各来源既有业务 | 不能因请求结束、帧缺失或续体泄漏而自动获得信任 |

### 两塔返回来源的逐项执行边界（B9，RQ-002／005～009／014）

本节只适用于 BossTowerReturnSweepService 与 IllusionTowerReturnSweepService。当前真实来源已有缺陷：定时作业内的旧 Leave 无显式 footprint 来源，在实际领域动作前回滚，导致到期 run 不能离场。迁移后由真正的受信任内部入口建立显式请求，并在原普通写中声明精确 owner，使合法内部回城能完成；不伪造网络帧、不凭缺网络上下文获得权限，也不把 test-only NonEffect 授权当生产来源。

**一轮与单项。** 一轮先捕获当前待投影离场项的不可变身份，逐项以完整内部 CMD 领取并投影；处理完这份快照后，才在扫描 CMD 内读取当前时钟和到期 run 身份，再逐 run 以完整内部 CMD 执行。一个 run 的成员冻结、原成员顺序、各段事务与回城投影仍在同一个 CMD 内，不拆成逐成员入队，也不在 CMD 中调用或等待另一个 CMD。生产者只提交显式值并等待，不读取世界、领取队列或登记出站。

快照本身不移除离场项。单项 CMD 只有在实际队首仍是其精确项身份时才领取；已被另一手工轮次领取或不再匹配的输入无投影、无旁路删除。并发手工轮次不得把同一项投影两次。入队被拒绝或执行在领取前因失败／取消而结束时，待办不能丢失；领取后的投影失败仍按旧路径消费该项。快照之后新增的离场项留到后续轮次，不把这一变化隐藏为原整批原子性。扫描得到的 run 身份只用于定位和验旧，执行时仍复核原 run／房间版本／到期条件和精确成员，不能凭扫描快照覆盖当前事实。

**可见调度变化。** CMD 之间允许按正常 FIFO 插入请求，发件箱按各 CMD 的既有完成边界交付；同一 CMD 内的原消息次序、事务点与提交后内存临界序列保持。hosted 同源最多一轮未结束，合并覆盖整轮；手工 Tick 保持原不合并。每轮有独立 Timer 根；由该轮扫描产生的后续 CMD 显式继承相应父链与原深度上限，不读取 ambient 请求。来源 physical／metadata 为空不代表 Boss 参与者 physical 为空，后者继续按各原声明捕获；Illusion 原空 selected 保护保持。

**故障隔离与取消。** 已执行的单项失败只结束该 CMD，按执行器的一条带链 Error 收口；生产者只记不携异常的 Warning，并可继续后项，既不清除 sticky 失败，也不让另一 run 进入前项的请求／围栏集合。扫描失败或未入队拒绝结束本轮，不能冒作可忽略的已执行单项失败。BodyOnly 仍让已入队 CMD 排空，body 使用原生产者 token且不新增提前取消点；仅当错误确实来自本次 body 的取消（包括工作单元原 action 取消后健康确认回滚）、原生产者 token 已取消，且完整请求／工作单元／scope 收口后仍只保留这一取消事实时，才在作业正常完成后向生产者重抛，保持纯取消不记执行器 Error。不能只按 OCE 类型或同一对象认定来源：scope-only、工作单元／lease独立清理、guard／sticky、typed 不确定及聚合错误均不取得这一处理；与body复用同一cause的独立清理也不能被吞掉。

原 fine scope 收入逐项 whole 后，已提交后的 scope／非豁免发布失败按该项 whole 默认规则保 cause 与实际已登记帧、保护该项声明的累计精确参与者（VC-B39）。多个单元先记录 session-only、后记录同精确 session 的真实 physical 时，不得以首次 session-only 遮掉后续已声明的物理保护；同 ID 的替换连接仍按引用身份隔离，不重新查询或推断未声明的收件人。这个约束不扩大到其它 run，也不增加 raw／Self／恢复权限。

**验收区分。** 必须同时保留当前真实来源缺陷的旧红／新绿、真实 Boss departure 的旧故障隔离，以及明确标注的“test-only 授权旧 sweep 算法”对照。受控同事务 SQL marker 的回滚／提交证据不冒自然生产 SQL；旧 fake bus 的 Attempts 不作真实事务证据。新真实来源须证明首项持续失败仍处理后项、领取前取消／拒绝不丢、重叠轮次不重复、项间 FIFO 请求插入、旧 run／版本不写、原时钟和两塔取消位置、同源整轮合并／排空与精确物理保护；多单元 session-only／physical 另有会红的机制回归。A 封存结果不回写，本节为 B 的有界来源分解和已证旧缺陷修正。

### 即时登记失败的等价边界（RQ-006）

默认规则保持：结果不确定、既有 typed 发布失败，以及提交后业务组装、评估、运行时认领、账本发布、来源不明的失败，仍保留原 typed 语义及全部精确参与者围栏；MonsterDeath/祭坛的原受保护发布段不变。登记守卫失败和额外独立的 scope dispose/cleanup 失败不能取得下述 raw 保留。父 RQ-023(c) 的“原样保留、位置等价”同时约束受保护发布段和原细粒度 CMD 之外的即时登记出口，不能按新的协议 CMD 外壳统一改变二者结果。

本次具名保留的旧出口仅为以下三个；CollectionBox 的已有证据层次和后继补证边界见下文及 [B3 amendment](verification/spec-b3-amendment.md)：

| 具名旧出口 | 保留的阶段边界 |
|---|---|
| UseStackable 已 Settled 后的 RequiredExplicit 即时登记 | 原主事务提交、Freeze/Settled 完成且旧 fine CMD 返回后，才组装并登记 required direct 消息；只保留下述实际编码失败结果，不涵盖此前业务组装 |
| Committed replay 普通 Write 后的 CurrentStateBatch 即时登记 | 已提交请求不再执行原业务；原 ReadReplayState 的 Transactional 对应普通 Write，读取当前快照后结束该单元，再按原序进行 MirrorMerge、Create、batch 登记及 Town 投影；只保留该 batch 的实际编码失败结果，不涵盖其前后其他动作 |
| CollectionBox 首次成功主结算结束后的成功 batch 即时登记 | 原主事务提交、Freeze/Settled 完成且旧 fine CMD 返回后，对原精确物理连接一次登记成功 batch，保持本次库存变化 → 完整 boxes 及 selected 重复投影 → 原 ACK 的顺序；只保留该次 batch 的实际编码失败结果 |

仅当本次请求拥有已登记的上述具名出口许可，且真实登记适配器证明错误确实来自绑定该 request、job、阶段、调用与精确物理目标的本次实际 encoder 调用，两项同时成立，才具有保留资格。该次登记凭据须不透明、不可伪造且不可复用。所有工作及 drain、scope cleanup 完成后，最终失败必须仍是这一个原始 cause 对象，且不包含 typed 不确定性或额外独立错误。此时原 cause 原样上抛，账本仍为 Committed，不新增参与者围栏；原已提交条目及 source close 顺序不变，后续合法同序号请求只重放当前投影、不重发原 ACK 或重复结算。这不保证真实 socket 关闭后还能接收重发。

不满足上述条件的提交后失败仍按默认保护处理；编码失败叠加额外独立 dispose/cleanup 或业务失败时，保留原 cause 引用并按既有 typed/aggregate 语义封锁全部精确参与者。不得根据 IOException 等异常类型、错误文字、调用方自报来源或可包任意业务的 Func 放宽；许可不得跨请求、跨目标、结束后或泄漏续体复用。业务捕获登记错误也不能恢复结算权、继续写入或将失败作为成功结束。其他出口必须另有旧行为证据并经受影响审查，本文不预授权。

CollectionBox 的第三出口只覆盖首次成功结果的上述一次 batch；不涵盖拒绝 batch、业务组帧（含 Full()/clock）、current/lookup/guard/Register 或其他协议，不把一般 IOException 当作许可，也不得复用 Committed replay 许可。已有 Select/Put 用例绕过 decoder、只断言异常类型，Committed/无新增围栏由同 fixture 后续当前投影重放间接支持；没有直接证明 cause 同对象、真实 ledger entry/Ordinal、围栏和 source-close，Withdraw 尚无动态注错证据。后继须按 AC-003 补证，不将本轮静态复核当作新运行结果。

CompoundItem、LotteryOpening（两 mode）、VendingMachine、EpicCraft、RentEquipment、ChargeRentPoint 只可在各自迁移前通过真正旧 decoder 和原登记适配器的实际 encoder 注错 oracle、迁移后通过同容器新路径对照后，为所属协议 CMD 具名登记现有 Committed replay／CurrentStateBatch 这一出口的有限 owner。须直接证明本节规定的原 cause、真实 ledger entry／原 Ordinal、Committed、无新增围栏、实际 source close 及当前投影不二次结算；[TASK-32](tasks/TASK-32.md) 没有这些 encoder 注错运行，不能用其调查批准或静态共享路径替代。未满足时不授予该 owner，返回受影响 gate；不新增第四 raw 类型，不给六者首次成功投影、拒绝投影、RequiredExplicit 或 CollectionBox 的初次成功 batch 授权。既有其他 owner 与本节所有来源／清理约束保持。

上述故障发生于即时登记编码，不得改称 physical transport 故障或将原测试注错移到物理发送端。此保留不启用 Compose、工作单元内延迟组装或延迟编码，不称 F-132 已修；具体来源证明机制、接口和迁移步骤由 Plan/Task 确定。

### Cera 购买的拒绝与失败收口（RQ-003、006～008、014）

CeraShopPurchase 是非效果协议。保留原购物车一次购买的事务边界、成功投影及正常拒绝；它没有效果 Main／同序号重放承诺，确认回滚后再次提交合法购物车是一次新购买。TASK-28 已通过真实注册 decoder、同容器旧 fine／领域／SQLite／发件箱与真实发送端取得旧行为，见 [取证报告](verification/evidence/task-28-inspection/report.md) 和 [B4 amendment](verification/spec-b4-amendment.md)。受控不确定和 scope 故障并非生产事故；内存传输不等于 TCP／客户端验收。

| 结果边界 | 迁移后要求 |
|---|---|
| 成功和正常业务返回拒绝 | 保留原成功响应／通知顺序、拒绝原因及错误码。正常拒绝原来空提交而资产未变的，仍按原事实处理，不为统一写法伪称或新增回滚 |
| 原可恢复的动作失败，或提交阶段失败且真实确认完整回滚 | 在原 cause、真实完成事实和本次请求健康／清理结果均支持恢复时，仍返回原拒绝响应；持久化资产及事件完整回滚，不新增来源关闭或参与者围栏。取消、typed 不确定、已有 sticky／guard 失败、未知完成事实或额外独立清理错误不属于此行；不能仅凭异常类型、错误文字、IsSettled 或业务自报“已回滚”作判定 |
| typed 结果不确定，含非 fallible Commit 违约和回滚附加失败 | 不再把失败转换为普通 StorageFailure ACK 并正常完成；保留 typed 原因及全部 cause 引用，作为本协议 CMD 的失败进入现有最终收口。实际资产已提交或实际回滚分别照实记录，不得据局部回滚快照把 Uncertain 改称 ConfirmedRollback，也不得自动重做购买 |
| 独立 unit／lease／CMD scope 清理失败，或其他默认不可恢复的 guard／登记失败 | 不作为上述可恢复拒绝。保原 cause 和独立错误事实；已提交写保持提交事实，继续既有 sticky／typed／aggregate 失败收口。独立清理再次抛出同一 cause 对象也不能消除其独立发生事实 |

后两行的可见差异限于 VC-B4 的 Cera 购买。本次精确买家是显式足迹及结算的参与者；当其 Committed／Uncertain 事实进入现有默认失败保护时，必须对该买家的精确 session 执行 Fence、阻断对应精确物理连接、对该身份执行既有 DiscardExact，并沿原来源关闭／调用链及发件箱次序收口。这是相对旧 IAccount-only 空选中保护集合的新增有意变化，不得削弱足迹或默认保护来保留旧空集合。范围仅为本次精确买家及其物理来源，不扩大到替换后的会话、其他玩家／队伍或无关协议。成功、正常拒绝及请求健康且清理成功的已确认回滚仍保持原响应、无新增围栏／关闭；不能仅因普通提交成功就封锁买家。其他尚未满足该默认保护条件的失败仍按其实际阶段收口，不一概新增围栏；不新增恢复开关、清除失败状态或 raw 编码许可。

scope 清理发生前可能已经登记正常消息（包括成功投影或原正常拒绝响应），因此不要求“故障时绝无任何帧”。已经登记的条目按现有单元完成状态、提交事实和登记顺序处理，失败来源关闭仍走原收口；不得为获得零帧新增撤回条目或提前 flush，也不得因该清理错误追加 StorageFailure ACK，或把未完成／不确定单元改成已提交来掩盖失败。提交后的业务投影和其他登记仍受默认保护，不借此增加具名 raw 出口或启用工作单元内延迟组装。具体工作单元和恢复接口由受影响 Plan 决定，本节不是实现或新恢复机制授权。

### Mailbox 六消息的自结算、拒绝与失败收口（RQ-003、006～008、014）

邮箱单附件 Send、多附件 Send、Claim、Action（读／删）、Open、Query 六消息均为 NonEffect；五个旧 handler 与共用 Delivery／fine 的编排归入所属协议 CMD，仍保持原登录权威、精确物理连接、当前 session 的 IsInWorld 及 OnlineUserId 归属检查。原 IAccount-only 的空 selected 参与者保护范围保持；邮箱没有 Cera 的 DemandEffects 依据，不新增 actor、footprint、Main、效果账本重放或同序号幂等语义，不套用 VC-B4 的新增 Fence／DiscardExact。失败的物理来源与选中参与者是两个不同身份，空 selected 不豁免实际来源关闭。

已提交 [TASK-30](tasks/TASK-30.md) 的 Controller Consumption R1 末节是本次后继裁定权威；其旧行为依据为 [报告](verification/evidence/task-30-inspection/report.md)、freeze-07／build-07／[final-03 原记录](verification/evidence/task-30-inspection/final-03/) 及实际帧检查。旧探针 C1 durable 后 nonfallible Commit 违约、C2 真实 action SQL 加 Rollback 第二因、C3 fine DI scope 提交后清理错误均回 ACK3、dispatch 正常返回、无来源关闭且同物理连接健康。C2 的 typed 图丢原 SQL cause 也是已证旧缺口；不是本次应保留的结果。旧调查 approved 只批准这些证据，不批准 B5 或实现；完整冲突、原始证据精度与局限见 [B5 amendment](verification/spec-b5-amendment.md)。

| 结果边界 | 迁移后要求 |
|---|---|
| 成功、正常业务拒绝及协议早拒绝 | 原错误码、资产、事件及帧序保持；不支持 Action 仍不进入业务事务。Send 的 ACK 在资产更新之前，Claim 的资产更新在 ACK 之前，Open 的 ACK 在 inbox 之前；原通知逐目标 Failed 隔离保持。正常返回的原提交／拒绝事实不能为统一写法伪称回滚 |
| 原 MailException／普通非取消动作失败，且真实健康确认完整回滚 | 保留原拒绝响应、原 cause 引用及全资产／事件回滚，不新增来源关闭或参与者围栏。领域 action 实际自回滚与 runner 提交阶段确认回滚分别照实判定：前者返回错误时事务已结算且 Current=null，按该次真实 action 自回滚事实归属，不能冒称 Commit 阶段失败或复活已结算事务；后者须有真实 KnownStorage native Commit 来源事实和同 cause，不能用 action SQL 或受控 Prepare 代替。恢复还要求本次请求／job／精确身份仍健康、无既有 sticky／guard 失败且所有 unit／lease／CMD scope 清理完整成功 |
| 取消、typed 不确定、既有 sticky／guard、未知完成，或独立 unit／lease／CMD scope 错误 | 不转换成普通 ACK 恢复并正常结束，不清失败、不重做已提交发送；保实际 Completion、typed 事实、原 cause 及独立错误，进入现有默认失败并按原 outbox 次序关闭精确物理来源、继承原调用链。独立清理再次抛同一 cause 对象仍是独立发生，不能靠引用去重掩去；普通传播不冒充第二发生 |
| MailboxDomain.RunAsync 自回滚发生附加失败 | 原 action cause 与 Rollback 第二 cause 都须以原对象可达于最终错误图，保 typed RollbackFailure／Uncertain 事实；实际 SQLite 全列回滚也不能据此改成 ConfirmedRollback 或普通拒绝。此原因图精度是共享领域语义的有意变化，适用于通过该 RunAsync 的网络、standalone 和 ambient 调用，不虚称只改网络 |

恢复依据必须对应本次真实发生及其来源、完成和全部清理结果；IsSettled、文字、业务自报“已回滚”或仅有资产快照都不足以授予恢复。共享 standalone 仍即时自结算；ambient 内调用者即使捕获拒绝，也不能提交其先前部分资产，原 Commit 必须抛错。ItemDelivery 的溢出／MailOnly prepared 附件、GM official mail 的多收件人 prepared 发放及整批 receipt／audit、QuestHelperPuppet 的撤销／替换／日标记、Housing harvest 和 Wedding settlement／退款，均保持原健康行为、同事务原子性、即时自结算及已有 IsSettled 防护；触达共享 RunAsync 的双因错误遵守上行原因图要求。原共享强用例不得改弱；TASK-30 对这些 caller 的源码影响核查不代表它们所有故障已端到端通过。MailCreated 与 delivery 仍随业务同事务记录，mail.arrival LiveOnly 仍在消费时投影，不提前并入协议成功响应。

VC-B5 的网络变化只限上述邮箱不可恢复失败，不扩大 selected 参与者集合。迁移后的 whole scope 结束前可能已登记原成功投影或正常拒绝帧，按原 outbox 的真实 unit 状态、提交事实及登记顺序保留，再关闭失败来源；不得因此承诺零帧、撤回条目、提前 flush、补普通失败 ACK、扩大三个 raw 出口或把未知／不确定单元改成 Committed。旧 C3 在 fine scope 故障前直接观察 slots=[]、之后仅旧 ACK3，是旧阶段位置的事实；不能推断未来 whole scope 故障前也为空。健康确认回滚后去注错再发送是合法新发送，不是 Main 或同序号幂等重放。原当前性与 session 身份门不因空 selected 消失，也不得误关替换后的物理连接。具体自结算事实、来源证明、有界恢复和清理机制留受影响 Plan P6 选择，本节不选 API。

### 四 remaining opening 与 Rental 的主结算及失败边界（RQ-003、006～008、014）

本节仅限 CompoundItem、LotteryOpening（mode0／1）、VendingMachine、EpicCraft、RentEquipment、ChargeRentPoint 六条效果消息。已提交 [TASK-32](tasks/TASK-32.md)（`17d924a06b1237ca2b4f5997983c0a4923dfc050`）及其 [matrix-04 报告](verification/evidence/task-32-inspection/report.md) 是旧行为依据；主控的后继 WHAT 选择见 [B6 裁定](verification/spec-b6-controller-decision.md)，完整差异和证据局限见 [B6 amendment](verification/spec-b6-amendment.md)。调查 approved 不批准本节新语义，也不代表实现已通过。

六个原 Transactional Main 各自对应一个主写单元；Lottery mode1 成功后的 ReadPremiumSnapshot 保持另一个 ReadOnly，Rental 成功后的 ReadRentalRefresh 保持另一个普通 Transactional 写单元，不能凭 refresh 名字改成只读或合进 Main。拒绝不新增成功后的刷新。保留原物品／材料／账号材料／pieces、UID／core／租赁 link／绝对到期、gold／lucky star／counters 及事件事实，成功 ACK／通知顺序和早拒绝门保持；全部参与者按本次精确身份逐一复查。一个请求只启动一次 Main，已提交同序号及迟到重放读取当前投影、不重发原 ACK／重复结算。

| 结果边界 | 迁移后要求 |
|---|---|
| 健康成功、正常返回拒绝或早拒绝 | 保原响应和资产／帧序；Rent invalid quote、Charge insufficient gold 等原正常返回且空提交的拒绝仍按真实 Committed 处理，不为统一写法 RequireRollback。早拒绝没有原 Main 时不新增 Main。下行真实 NoRoom 是另一种结果 |
| 四 opening 原动作失败及真实 native Commit 健康确认回滚 | 保原错误码／拒绝 ACK、原 cause 引用及完整资产／事件回滚，不新增围栏或来源关闭。恢复必须对应本次真实动作或提交来源／完成事实，且请求、job、精确身份、guard 和全部清理均健康；不凭异常类别／文字、IsSettled、业务自报或已恢复快照授恢复。guard、登记、独立 unit／lease／scope 错误不纳入普通动作映射 |
| Rental 动作 SQL 或 native Commit 异常且确认回滚 | 保其原非恢复结果：无普通 ACK、原 cause 外抛并实际关闭来源。不得照抄四 opening 的普通异常拒绝恢复；确认回滚事实不自动意味着此协议允许恢复 |
| Rent NoRoom／InventoryFull 正常返回、RequireRollback 且完整健康确认回滚 | 有意修复旧 Frozen=false／Projection=null 后错误 Settled 导致 typed／Blocked／断线的缺口。原暂存 stars／设备等全部回滚；回复 `RentEquipmentResponse(Result:2)`，wire body `01 02 00 00 00`，不是 `Error:2`。Main／ledger 为 ConfirmedRollback，不记 Committed／Blocked，不新增 Fence／物理 Block／DiscardExact／关闭，不执行成功后 refresh。同一帧合法重试复用原 entry 和原 Ordinal，按既有 ConfirmedRollback 重试规则执行；不能套已提交重放或重复扣星 |
| 不确定、非法生命周期／guard、独立 unit／lease／whole scope 失败 | 保实际 Completion、typed／原 cause 与独立发生事实，进入现有默认失败。独立清理再次抛同一 cause 对象也不消除第二发生，正常传播不冒第二错误。实际 Committed／Uncertain 参与者符合默认条件时必须精确 Fence、阻断对应物理连接、DiscardExact 并关闭失败来源；仅 ConfirmedRollback 后又有普通错误，不伪称 Committed／Uncertain 或一概增围栏 |
| 旧 fine scope 收入唯一 whole scope | 有意采用 whole 的完整失败边界。四 opening 不再把提交后 scope 错误映失败 ACK 并健康结束；Rental 也不绕开已经提交的默认参与者保护。原 fine scope 故障发生在 handler 响应前，迁后真实 whole scope 在 body／投影后，届时可能已有原 success／normal refusal 帧。按真实 unit 状态和原顺序保留存活条目，置后关闭精确来源；不因错误补失败 ACK，不承诺零帧、不撤帧或提前 flush |
| Main 提交后 Lottery mode1 premium／Rental 独立 refresh 失败 | 保第二单元真实完成事实，不能回滚已提交 Main 或普通 ACK 恢复；进入 whole 既有默认失败与 typed 收口。Main 已 Committed 时，精确参与者依现有条件 Fence／物理 Block／DiscardExact／来源关闭，这是旧 catch 外无新增围栏出口的有意增强。第二单元确认回滚与其清理后失败分别记录，不将所有失败混叫 Main 回滚 |

已经成功 Settled 为 Committed 的 Main，后续 scope／刷新默认失败不将该 ledger entry 改成 Blocked，也不因外层 typed 包装允许重结算；Main 尚未成功提交记录的不确定结果按既有事实进入 Blocked。不能把实际 Completion、ledger 状态和最终错误类别混为一件事。

VC-B6 仅改变表中 NoRoom 缺口和 scope／独立刷新失败边界。C1 durable 后 nonfallible Commit 违约、C2 真 SQL 加 Rollback 第二 cause 的原 Uncertain／保护结果保持；不得据 SQLite 已回滚的局部观察改称 ConfirmedRollback。取消、失效 request／job、已有 sticky／guard、未知完成或来源、第二错误与未完整清理不能取得上述健康拒绝。若 NoRoom 拒绝投影已经登记后另有清理错误，仍按真实条目／完成事实和默认失败处理，不将它冒健康结束，也不为求零帧撤回原条目。

默认保护仅作用于本次精确参与者及原物理来源；真实替换后的 session／不同物理连接、无关玩家不能被误围栏、丢弃或关闭。无论是否已有条目，原失败来源仍按既有出站顺序实际关闭并继承原链。六者初次成功或失败的非许可登记、提交后业务组帧／clock／current、原 guard 及独立清理保持默认保护；committed replay 的有限原出口只按上一专节逐具名旧新 oracle 条件保留。

本节不授 Mailbox 自结算恢复／SelfSettledAction，不扩其原六 logical／五 CLR 唯一 whole 的权限，不新增通用恢复或 raw 类型，不启用 Compose／事务内延迟组装，也不称 F-132 已修。现有十个 WriteResult 消费方的 commit-only 来源和默认保护保持；早期“五 consumer”只指原四 opening＋Cera 子集，并非当前完整计数。后继四 opening 仅为具名消费扩展，Rental 的异常不因共享机制获恢复；具体 HOW 留 Plan。根「纯搬迁与语义改动分开提交」继续生效。独立 NoRoom 修正与可独立的纯结构准备应分开；唯一 CMD 的生命周期边界及故障点变化按 VC-B6 明列，Plan 依真实依赖确定可执行提交分离，不能仅把混合提交改称“语义提交”规避原约束，也不扩修所有旧正常 rollback-only Main。

### 管线职责迁移对照（RQ-004～009）

| A 中职责 | B 必须保留的结果 |
|---|---|
| dispatcher/authorization | 执行器上建立权威；旧/被顶替连接按既有语义处理 |
| EffectOperationLedger | 序号与精确接收者、登记/重放、Pending结束拒绝及状态区分 |
| 45 | CMD明确主结算单元；账号/会话一致、报告者在参与者内、逐人当前性、逐人封锁 |
| 58 | 提交前后权威/接收者复查；Settled/Failed与不确定围栏 |
| 60 | 工作单元开启/提交/真回滚、RequireRollback、TransactionStarted/RollbackConfirmed/Freeze |
| 100 | 授权检查；未知执行/工作单元语义不能静默放行 |
| 0 | 每条协议/内部CMD由执行器记录耗时、结果与调用链，无细粒度重复Error |

### 必须删除的过渡机制（RQ-012）

| 项 | 最终判据 |
|---|---|
| T1 | CommandBus不再作业内内联细粒度命令；旧 `bus:` 包装消失或只剩明确程序错误路径；执行器内入队等待仍被拒 |
| T2 | handler无业务编排、至多投递自己的协议CMD；旧细粒度注册/处理器全部有并入、应用能力或删除去向；不可留新CMD转发旧CMD |
| T3 | 五层按细粒度命令包装的旧行为及ISelectedSessionCommand/IRequestSettlementCommand/IMultiSelectedSessionCommand/IAccountCommand为零；非锁职责已按上表落位 |
| T4 | 六处请求级AsyncLocal及无帧放行为零；生产AsyncLocal只剩GameplayJob.Running和DomainTransactionScope.ambient两处常设用途 |
| T5 | CommandLoggingBehavior旧失败Error为零；同一失败只余执行器一条带链Error |
| T6 | 两个dispatcher手工 `_ = outboxes.Current` 为零；从未登记条目的来源CMD失败也会关闭连接；登记点本身的执行器守卫保留 |
| B临时机制 | Plan声明的迁移并存分支、转发适配、旧形状兼容入口在封存前为零，不以永久例外保留 |

类名不是删除依据：GameInventory/GameInstance/GameExperience/PartyExactNotificationDelivery等纯帧辅助，以及PartyDeliveryStream、DungeonRunDeliveryRegistry等状态所有者，以实际职责判断；A常设发件箱适配器保留。跨协议共享能力仍遵守项目引用方向，Application不依赖Protocol/Networking。

### 交接项的完成边界（RQ-013）

| 条目 | 本 Spec 的责任 |
|---|---|
| F-07 | 将点名的日切/配额全会话长作业拆为有界内部CMD。扫描CMD只捕获周期和精确session IDs，producer仅依扫描结果入队；每session刷新CMD按实际FIFO位置执行，重新读取当前事实，旧周期或旧session不登记也不推进水位，正确新周期由下一次扫描覆盖。保持周期/精确session/登记水位正确性与单连接条目顺序；不再保证原整批不交错，期间已入队请求可能先响应（VC-B3）。不能在CMD里等待子CMD；水位与来源合并细节由Plan明确 |
| F-12、F-34 | 完成全部事务点到工作单元的对照，以及协议CMD/显式上下文归并 |
| F-58 | 复核请求结束/泄漏流，不能降级成可信内部调用 |
| F-69 | 续写单元测试耗时对比；明显增加按既有排空诊断核对并记录环境，不把套件慢当性能失败 |
| F-86 | 复核卡牌预留冲突3次重试；无业务依据则删除，有依据则以独立于旧锁模型的事实、证据和主控裁定说明 |
| F-88、F-93 | 迁移前补IsInWorld、Owns(previous)、DiscardExact、DemandCurrent(recipient)四项区分用例；移除对应保护会变红 |
| F-109 | 收口删门后无语义的representedSessions参数，保留原刷新结果，不复活门依赖 |
| F-110 | 将“失败来源必关闭”从约定变为结构/行为保证，删除T6 |
| F-112 | 功能簇测试与被测单元共用真实发件箱边界，回滚/提交断言能看见真实结果 |
| F-122 | 将Delivered改为不承诺传输送达的登记结果名称，所有消费/文档一致；连接已不在而丢弃仍属A原语义，GM attempted/offline不变 |
| F-124 | 通知注册改写时裁定PvP发布处理归属的保护方式，恢复等价行为证据；不把目录有任意handler当已证明正确处理 |
| F-127、F-130 | 按首单元约定完成RQ-010/011；不宣称SQLite全部偶发的根因因此已证明 |
| F-129 | 补Channel入站失败降级的区分证据，恢复中间件Error时会被发现 |
| F-131 | 按RQ-013明确保留A现状及精度差距，迁移关闭路径时复核，交外审 |
| F-132 | 按RQ-008在采用事务内组装前处理提交后失败分类 |
| F-128①、无编号扇出字段事项 | 登录文档按实际MId/Ticket及新CMD更新；判定是否需要变长字段上限守卫并给依据，不凭空改线上字段或客户端上限 |

上述18个编号项另加F-128①和无编号事项，沿A交接口径计数。CH-1/CH-2为根控制器接受的低风险残余，若本次触及则有界处理并记录；否则保持后继归属，不能写成已修复。

## 相对 A 的可见变化与明确保留

| 项 | 变化/保留 | 证据要求 |
|---|---|---|
| VC-B1 | 写端完成/断开所致发送失败日志降为Information；Failed、关闭和线级结果不变（RQ-011） | 真实传输路径及普通故障对照 |
| VC-B2 | 细粒度重复Error消失，CMD身份改为协议/内部CMD；一次失败仍有完整链和来源信息 | 失败日志断言，含Channel与非协议来源 |
| VC-B3 | F-07的每session刷新CMD按实际FIFO位置执行和登记；扫描与刷新之间、不同刷新CMD之间，期间已入队请求可能先响应，不保证原整批不交错。周期/精确session/登记水位正确性和单连接条目顺序保持；不改队列插队/预留规则 | 覆盖请求插入、排队跨周期、session退出/替换及水位推进；旧周期/旧session无登记无水位推进，下一次扫描覆盖正确新周期 |
| VC-B4 | 仅 Cera 购买：旧路径把 typed 不确定及已提交 fine scope 清理失败转为普通拒绝 ACK／正常结束，且没有选中会话围栏或来源关闭；迁移后不再用 StorageFailure ACK 恢复。本次精确买家进入显式足迹／结算参与者，Committed／Uncertain 事实进入默认失败保护时，对其精确 session Fence、阻断精确物理连接、DiscardExact，并按原链和出站次序关闭来源；不影响替换后的会话、其他玩家／队伍或无关协议。成功、正常拒绝及健康清理完成的已确认回滚原响应与无新增围栏／关闭保持 | 以 TASK-28 旧 decoder 原 oracle 为 before，迁移后同容器区分普通 SQL／真实 fallible Commit 确认回滚与受控不确定／独立 scope 失败；核完整资产、cause、拒绝 ACK、精确 actor 的 Fence／连接阻断／DiscardExact、其他会话不受影响、已登记条目及实际关闭／链。正常提交成功不触发围栏，不能只看异常类型或以零帧作统一断言 |
| VC-B5 | 仅 Mailbox 六消息的不可恢复失败：旧 C1／C2／C3 ACK3、正常返回及同物理连接健康，改为保 typed／真实完成／独立失败事实的默认失败及精确物理来源关闭／原链；原空 selected 保护范围不变，不新增 Cera actor／footprint／Fence／DiscardExact。成功、正常拒绝和健康完整 ConfirmedRollback 的原响应／资产／顺序保持。另将共享 MailboxDomain.RunAsync 自回滚附加失败丢原 action cause 的旧缺口改为最终错误图保两原 cause／typed 事实，适用于经它的共享调用 | 以 TASK-30 freeze-07／build-07／final-03 的具名旧 oracle 为 before，核新六路真实入口、action 自结算与 native Commit、C1 durable／C2 双因／C3 whole scope、全部资产／事件／实际帧和精确来源关闭／链；共享即时自结算及 caught refusal 后 Commit 必 throw、原子性和原强断言保持。清理前已登记原 success／normal refusal 帧按原机制保留后关闭，不将“未因失败补 ACK”写成“零帧”；共享原因图精度与未测范围明确，不冒称各 caller 全故障端到端 |
| VC-B6 | 仅上述四 opening 与 Rental 六消息：Rent 健康 NoRoom 由旧确认回滚后 typed／Blocked／断线改为 Result2 正常拒绝、ConfirmedRollback 与无新增关闭；旧 fine scope 折 whole 后采用完整默认失败，四 opening 不再吞提交后清理失败为普通失败 ACK／健康结束，Rental 的已提交默认保护不再遗漏。Lottery mode1 premium／Rental 独立 refresh 在 Main 已提交后失败，按 whole 默认 typed／精确参与者保护收口。独立刷新事务不并入 Main，scope 前可能已登记原 success／normal refusal 帧，保其真实存活次序后关闭来源；完整边界及其余原行为保持见专节 | 以 TASK-32 的具名旧真实 decoder oracle 为 before，迁后同容器核 NoRoom 真满包／完整回滚／wire Result2／原 entry 与 Ordinal 重试，四 opening 与 Rental 的不同异常出口，scope 和独立刷新前后实际 Completion／cause／条目及真实发送→关闭／原链；直接核精确 Fence／物理 Block／DiscardExact 和实际替换身份／无关连接隔离，不伪称零帧、全 scope 都 Uncertain 或所有回滚都可恢复 |
| VC-B7 | 仅 RenameCreature／HatchCreature／SharedEffect 的 fine scope 归入 whole scope：旧已提交后 fine Dispose 错误为原 IOException、无新增 actor Fence、无成功帧；新保原 cause 的 PostCommitPublicationFailure、原已登记成功帧后关闭精确来源，Rename／Shared Main 对精确 actor Fence／Block／DiscardExact，Hatch ordinary 无 selected 参与者因而不新增 Fence。仅 SharedEffect 首次 required explicit ACK 编码失败由旧 raw IOException／无 Fence 改为 whole typed／精确 actor 保护；两者 Main 均已提交且只扣一次。Rename 可见 NOTI owner 编码失败仍原 catch／无 ACK／仅关闭 owner；peer 编码失败仍仅关闭失败 peer、继续后续 peer 与 owner ACK，无 owner Fence。Committed replay 原 raw encoder cause／Committed／无新增 Fence／精确来源关闭保持，不扩大 raw 或恢复权限 | 正式 `CreatureAndEffectProtocolCommandFlowTests`：`Whole_scope_failure_preserves_committed_Main_and_closes_physical_source`（三写路，含 Hatch ordinary）、`Initial_actual_encoder_failure_preserves_Main_and_closes_original_source`（owner）、`Rename_peer_encoder_failure_is_isolated_and_owner_notice_precedes_ack`、replay 原物理及同 ID 替换隔离；旧 HEAD＋纯抽取 27/27，迁后同 provider decoder／SQLite／actual encoder／生产 sender→transport.CloseAsync；守原资产／提交／帧序与精确关闭 |
| VC-B8 | 仅 RentEquipment／ChargeRentPoint 首次 required batch 编码失败：旧 raw IOException／无 actor Block 改为 whole 默认 PostCommitPublicationFailure／精确 actor Block；保原 cause、Main Committed、无 ACK／无发送及精确来源关闭。既有 committed replay 的限定 raw 出口与正常结果保持。 | TASK-37 的旧／新真实 decoder→SQLite→actual encoder→production sender／transport.CloseAsync 对照；初次失败两路验证原 cause、资产只结算一次及 Main 原 entry／Committed，重复请求另核原 Ordinal 与同 ID 替换／无关 transport 不受影响。 |
| VC-B9 | 仅 RecoverStamina 的未请求 token 取消、typed／真实 Uncertain、SQL 加回滚第二因及 fine scope 失败：旧 Error4／正常结束／健康连接改为保 cause 与实际 Completion 的默认失败、精确物理来源关闭；已请求 token 的取消仍回滚／关闭。DailyReviveCoin fine scope 与 Recover scope 归入 whole 后，已提交成功帧分别按 CF→0E→B1 与 0E→21→09 送出再关闭；Daily 旧 scope 无成功帧、原 IOException 改为保 cause 的 PostCommitPublicationFailure。仅已进入普通空提交的拒绝（Recover mode65535／Daily 已有钱包）直接 ACK 编码失败由 raw cause 改为默认 typed，原无帧／精确来源关闭保持。两者 selected Participants 仍为空，不新增 Fence／Block／DiscardExact；Recover 无足迹，Daily 仅声明原查询 session 为效果 owner，无 Main／账本。成功投影 encoder 失败仍由原 NotificationBus 逐收件人隔离、连接健康；成功、普通拒绝、健康 action SQL／KnownStorage native commit 确认回滚、资产与原帧序保持 | 正式 `RecoveryAndReviveProtocolCommandFlowTests` 在 e8d9499f＋纯测试准备和新 whole 上对照真实 decoder／SQLite／encoder／生产 sender；区分 token 两种取消、真实 native Commit、nonfallible Commit、SQL＋Rollback 双因、独立 request／scope，核 Completion、资产、帧序、CloseAsync／来源链及同 ID 替换隔离。原业务强测试和新 F-88 guard 移除正式红／恢复绿保 selected-not-world 静默；P8 owners 与空保护范围由真实声明断言守护。 |
| VC-B10 | 仅 Teleport 首次库存 batch 实际编码失败及已提交 fine scope 归入 whole 后的 Dispose 失败：旧 raw IOException／无新增 actor Fence 改为保原 cause 的默认 PostCommitPublicationFailure，对该次精确 actor Fence／物理 Block／DiscardExact；主结算与 ledger entry 仍 Committed、资产只结算一次。scope 前已经登记的库存 batch→城镇 explicit 按原序发出再关闭精确来源，旧 fine scope 在登记前结束因而无帧。仅 SetUserArea 原位置独立提交之后 required explicit 编码失败或 RememberReturn 抛错，旧 raw 改为保 cause 的默认 typed；其 Participants 仍为空，不新增 selected Fence／Block。保存普通返回位置提交→RememberReturn→目的101无持久写的顺序保持。Teleport committed replay 的原 cause／原 entry 与 Ordinal／Committed／无新增围栏及精确来源关闭保持，有限 owner 不扩首次成功 raw 或恢复权限；位置报告保原单 NOTI 登记，其他健康结果与帧序不变 | TASK-41 `TeleportProtocolCommandFlowTests` 旧11＋5例及新同 provider 真实 decoder／SQLite／actual encoder／production sender→transport.CloseAsync，核真实 DI Dispose、deferred FK native Commit、cause、Main 与 current replay／精确物理替换隔离；新路径直接观察原 actor DiscardExact 与原关闭链。`TownMovementProtocolCommandFlowTests` 的旧 Area 原方法体来自 TownProjectionSlice.cs@48511dae，仅类名／注册适配并复用旧路由已验证的纯准备算法，14例核婚房提交时点、拒绝、婚礼外观及实际encoder；其中 Step 仍走新 route，不作为新增旧 Step oracle。F-88 错误代次0 Main＋零帧及可编译去guard变异守旧拒绝。 |
| VC-B11 | 仅 PrepareCycleDungeonEntry 真实 command scope.Dispose 抛 InvalidDataException：旧 Direct fine 已更新内存 cycle intent，再被外层 handler 当内容拒绝而正常回 false ACK；迁后仍无事务／Main，cycle intent 保留，body 先登记 true ACK（原9字节成功body），独立 whole scope 清理故障保原 cause 退出并按原链关闭精确来源，不转内容拒绝、不新增 selected Fence／Block。scope 清理中替换同ID物理context时，旧false ACK在清理后登记给replacement；新true ACK在清理前已绑定原物理来源，之后只关闭原来源、replacement无帧无关闭。普通内容 InvalidDataException 仍 false ACK、健康结束且不改变 intent | TASK-42 `CyclePreparationWireTests` 在 bf1f070f＋TownLeaveMany纯准备上旧12例与新同正式用例对照；真实decoder／dispatcher／scope／encoder／production sender→受控transport，核intent、任务未接受、ACK／物理收件人、原cause及close chain、replacement隔离与无selected围栏。原响应字节另有codec金样；不是A21实机验收。 |
| VC-B12 | 仅 Boss ReleaseForSelection 已成功普通提交后，普通选择required实际编码失败：旧 fine 外 raw IOException／全run无新增selected围栏，改为whole默认PostCommitPublicationFailure并保同cause／Committed，对原run全员精确Fence、原source物理Block；已释放run／参与状态和已记录选择intent保留，ticket不重扣，无成功帧，仅原source按原链关闭、同ID replacement不Block不关闭。仅 Boss／Illusion BackToVillage 已提交fine scope.Dispose归入whole：旧raw／scope在host投影前结束无帧，迁后typed保同cause、先前host帧先交实际peer再关原source；Boss保护原run全员，Illusion selected集合仍空且source/peer均不Fence、不Block，不把footprint owners变成保护集合。peer被Fence不等于peer物理关闭，两路peer均无CloseAsync。 | TASK-42 `DungeonSelectionFailureBoundaryTests` 三条具名旧新正式oracle，旧Enter／Back为bf1f070f原完整handler方法体，仅临时改类名/注册并恢复原Boss PrepareSelection→真实旧Release fine；临时制品最终删净。同provider真实领域事务／Completion、两成员run、actual encoder、production sender→受控transport，核cause、真实peer host帧、source chain、Fence/Block与物理替换隔离；NoReturnView把Back例限定到host投影，CompleteView是selection受控生命周期投影，不冒称全部A21界面或任意塔故障矩阵。 |
| VC-B13 | 仅 SelectDungeon／MirrorDungeonEntry 实际普通、Boss、Illusion入场的提交后actual encoder与fine scope.Dispose归入whole：旧fine外raw原cause改为默认PostCommitPublicationFailure，保持真实Committed；普通／Boss保护原全附件，Illusion空selected且source/peer均不新增Fence／Block。scope旧在投影前失败无成功帧，新已登记source和peer各1batch，按材料→receiver-first属性→ACK→state→UDP→模式地图的原序实际交付后关闭原来源；Fence不等于撤销已登记帧或关闭peer。普通编码失败仍原#185逻辑abort运行态、已提交疲劳不退；普通早拒绝Committed与late配置正常拒绝ConfirmedRollback保留。 | TASK-43正式DungeonEntryFailureBoundaryTests在精确647a00ae与新whole的同11例旧新对照，真实decoder／dispatcher／领域事务及SQLite疲劳／actual encoder／production sender和受控physical transport；核Completion、cause、全员／空保护、真实帧和close chain。最小算法driver不证明scope或生产链，CompleteView受控，不冒A21验收。 |
| VC-B14 | 仅三条已迁入场cleanup：原两塔提交后投影失败按逻辑ID关闭会误关同ID replacement，改为附件捕获原NetworkConnectionContext并显式交现有精确closures。source job仍关闭原physical；peer contexts已替换时保原窄契约precise drop（旧peer与replacement均close0），不新增关闭失去contexts旧peer权限；query-only变化而contexts仍为原peer时关闭原peer、replacement无帧无关闭。普通partial-cohort隔离仍仅已登记及当前失败成员，不扩大到未登记requester。 | 同11例正式旧新oracle分别覆盖contexts/query双替换及仅encoder的query-only变体：actual online查询确实返回replacement，核原physical、同ID新physical、source和peer逐目标send／close及原cause链。共享OutboxConnectionCloser／F-83／RQ-035不改，只有本次入场具名capture与cleanup改变。 |
| VC-B15 | 仅 FinishLoading／MoveMap／IllusionTowerStage：原 fine Dispose 的边界归入whole。普通ready仍Direct无事务、原IOException／无Fence，scope失败从旧release前无帧变为新release＋首次weakness已登记；普通Move保真实已提交SQLite／memory原子事务与空selected，scope及未吞编码失败由旧raw同cause改whole默认typed、无新增Fence，已登记room→fatigue帧保留。Boss Load／Start仍两独立ordinary Write：旧Load.Dispose止于Ready／无帧，新whole.Dispose时两次Committed、Fighting及各成员001E已登记；第二动作失败保第一Committed与第二ConfirmedRollback、Ready不变，新whole默认typed并保护第一unit的原全员，不能称整个数据库结果不确定。Illusion Load始终只Ready、空selected无Fence／Block；scope新已登记001E后失败，Stage1才Start、Stage2无帧不推进。两塔原encoder逐成员收集关闭并正常返回继续保持。 | TASK-44共20例旧新对照（15例生产链＋5例机制方法级）；生产链在精确1515397c旧产品与新whole实际decoder／dispatcher／真实独立DomainTx、普通双方SQLite疲劳／actual encoder／生产outbox／受控physical sender；准确次数、Completion、原cause、帧顺序与数量、Fence／Block及source链均直接断言。机制快照另以实际旧Direct subscriber和新ICommandProjections同通知／当前selection／Failed→IOException对照，不冒两者scope生命周期相同。 |
| VC-B16 | 仅本次两塔加载／换层投影失败cleanup：提前捕获原physical Context，沿现有精确closures及原IsInWorld门关闭。旧逻辑ID清理误关同ID replacement改为replacement零帧零关闭；contexts已换走时原source／peer也只precise-drop，不新增关闭失去contexts原peer的权限；仅online query变化而contexts仍原物理时关闭原物理。共享关闭机制不变。 | TASK-44同ID source＋peer双替换正式oracle旧／新实际online查询与contexts两种变化分开，直接核四个physical的发送／关闭；普通与两塔原成功帧、source job原直接关闭及同cause链另有用例。不把围栏说成撤帧或物理关闭。 |
| VC-B17 | MonsterDeath归并后的scope／未吞即时投影失败沿whole默认失败：普通原fine.Dispose raw且无帧／围栏，新whole.Dispose已提交并登记EXP／death后typed，全部原精确物理参与者Fence／Block；物件仍空selected／无footprint，scope由旧raw变typed但不新增全员保护。Boss原fine.Dispose在已提交奖励后且投影前，新whole.Dispose在原成员各自EXP／库存／death已登记后；全成员selected但仅reporterphysical，默认typed与原保护集合保持。Illusion仍空selected，scope新已登记death后typed，不加Fence／Block。原ordinary发布失败既有typed及全员物理保护保持；两塔逐成员encoder收集后正常返回、仅关实际失败成员保持。 | TASK-45真实decoder／dispatcher／DomainTx／actual encoder／production outbox／受控physical sender在cc212b99旧与最终whole同正式用例直接对照；真实Completion、cause、帧类别／数量、Fence／Block和物理关闭正断言，secondary实际transport失败与encoder故障分开。 |
| VC-B18 | 只在本次两塔Kill投影失败cleanup捕获原physical并沿既有contexts当前实例及IsInWorld准入精确关闭。旧逻辑ID误关同ID replacement改为replacement零帧零关闭；contexts已替换时旧physical也precise-drop，query-only更换而contexts仍原physical时只关闭原physical；共享关闭器／F-83／RQ-035不改。 | TASK-45两塔actual encoder注错的source／peer双替换正式旧新oracle，query与contexts两类分开，直接核全部原／新physical发送和关闭，不扩大失去contexts旧peer关闭权限。 |
| VC-B19 | 仅GetItem拾取whole scope／未吞投影失败沿默认边界：普通原fine.Dispose在提交后投影前raw，新whole.Dispose在ACK／pickup等登记后typed；普通与Illusion仍空selected、不新增Fence／Block。Boss原fine.Dispose在投影前raw且无新增scope围栏，新whole.Dispose在原批次登记后typed并保护原全员selected、仅报告者physical；成功批次encoder仍逐成员catch、继续并正常结束。普通encoder及Illusion可达NoRoom ACK的旧raw错误改为whole typed，但不新增参与者保护；事务结果和资产／claim不变。 | TASK-46正式GroundPickupFailureBoundaryTests在c20f4878与最终whole对照；真实scope、Completion、cause、ACK／NOTI帧、无falseFence、root／source关闭链、重试一次支付。Boss真实满包InventoryThenMail及Illusion六槽满NoRoom分开守护，不以fake NoRoom冒充Boss领域行为。 |
| VC-B20 | 仅两塔Pickup成功批次投影失败cleanup提前捕获原physical，沿原IsInWorld和既有contexts准入精确关闭。旧按ID误关replacement改为replacement零帧零关闭；query-only变化仍关原physical，contexts已换走则原／新都precise-drop。共享closer、其它塔操作与F-83／RQ-035不改。 | TASK-46真实旧新oracle的query-only／contexts双替换，直接核原／新source与peer物理发送／关闭；secondary真实transport失败与actual encoder故障分开，不误报全队围栏。 |
| 内部契约 | NotificationOutcome登记结果改名，含义保持A“接受登记，不保证落线” | 消费点与GM结果回归、文档 |
| VC-B21 | 仅CharacterDeath／UseRevivalCoin归并后的默认生命周期：普通死亡仍无事务，旧fine scope在ACK前原异常，新whole scope在ACK→life登记后仍为原异常，无typed／Fence／Block；普通encoder原异常保持。复活原fine scope在投影前原异常，新whole scope在life→ACK→coin登记后typed；其未吞encoder失败也由原异常转whole typed，仍空selected／无footprint／无新增围栏。两塔死亡scope由投影前原异常转投影后whole typed，Boss保全selected、仅报告者physical，Illusion仍空selected；成功批次encoder仍逐成员catch、继续正常结束。 | TASK-47正式CharacterDeathFailureBoundaryTests在精确fff95b5d与最终whole对照；普通零Tx、两塔／复活真实Completion、cause、scope前已登记帧、精确Fence／Block／来源关闭链、重复死亡无新UI与复活Prepare回滚后新physical重试只扣一次。ACK0／1和默认配置、原拒绝字节与启动校验保持。 |
| VC-B22 | 仅两塔死亡成功批次投影失败cleanup提前捕获原physical，沿原IsInWorld及既有contexts准入精确关闭；旧按ID误关replacement改为replacement零帧零关闭。query-only变化仍关原physical，contexts已替换则原／新均precise-drop。共享closer及其他塔操作不变。 | TASK-47两塔实际旧新query-only／contexts替换oracle，直接核原／新source与peer发送和关闭。两塔实际团灭领域奖励／邮件与重复死亡一次结算另有正式wire用例，不以owners声明替代领域消费。 |
| VC-B23 | 仅结算／翻牌的 whole 默认生命周期：旧 Grant SQL durable 后 fine scope 失败尚未设置 caller 的 durableCommitted，会释放 settlement claim，同 run 重试可再次支付；新 whole scope 在 CommitSettlement／响应登记／布局调度之后，保一次资产与 settled claim，默认 typed／原 cause、精确 actor 保护并关闭原来源。结算即时 encoder 失败仍保已提交 claim／资产且不调度，新归默认 typed／精确 actor 保护。SelectCard 原空 selected／None 不扩大；其提交后投影或 whole scope 失败保已领取资产与 claim，按 whole 默认失败收口。Timer 原在作业内 catch 一般业务错误后正常结束，改由 typed 内部 CMD 的完整请求／scope 失败收口；无 source connection，不借发布者链关闭连接，生产者观察已结束作业只记 Debug。 | TASK-48 `CardSettlementLifecycleTests` 在旧 d0dd43d2 与新 whole 正式对照：真实 SQL 30→60 的旧 scope 重试与新 30／不可重领 claim；scope 时点已调度、encoder 失败无调度，原 cause／Completion／Request.IsClosed、实际帧／关闭与 actor 声明；Timer encoder／真实 DI scope 注错及新根／None 入队／FIFO 取消／drain。无 raw／Self 或吞错许可扩展。 |
| VC-B24 | 仅 card auto-flip 删除同一作业内 3 次 AlreadyFlipped 重试，仍只确认 Free index0、拒绝不换另一共享列。真实生产唯一预留消费者与事务 Prepare／Commit／Rollback不提供尝试间推进；在单执行器上后续释放作业只能在当前作业结束后运行。 | TASK-48 实际预约＋已排队释放＋下一作业可领取的三作业测试；原 fake bus 首次冲突后成功不是业务来源，替换为单次拒绝且不改选列断言。 |
| VC-B25 | 仅普通 EPLP 的 Direct fine scope 归入 whole：旧 scope 失败时 run 已 End、delivery registry 仍保本人，ACK／Town 均未登记；新 whole scope 失败时已移除对应 participant，并已按原序登记 ACK→Town。仍为零事务、原 cause 对象、无 typed／Fence／Block，来源在已有帧后关闭。 | TASK-49 `DungeonExitLifecycleTests` 在 b0c4343e 与新 whole 同正式 decoder→dispatcher／实际 DI Dispose／production outbox／受控 physical 对照；直接核 run、registry、零 unit／Tx、帧及来源关闭。普通 GiveUp 原零事务与 encoder 失败后的已 End 状态另有对照。 |
| VC-B26 | 仅 EPLP 两塔结果选择的默认 whole 生命周期：旧 Select scope 失败停于一次已提交选择、无 ACK／后续 Leave；新 Boss option2 已 ACK，并完成逐成员独立 ordinary Write Leave 后 whole scope 才失败。原 cause 改 whole typed，保此前各次 Committed、原全 selected 及每单元报告者 physical 默认保护；未把选择和多次 Leave 合并事务。Illusion 原 plain 空 selected／None 不扩大，新 scope 已登记 ACK 后 typed，仍无 Fence／Block，关闭来源。 | TASK-49 两塔三 option 与真实 scope 正式旧新 oracle；Boss 两成员的 1＋1＋1 个 Committed、每次报告者／physical／DeclaredNonRequest owners、资产不重复、原／新帧和精确 Fence／来源 Block／Close；Illusion 一个空保护单元。 |
| VC-B27 | 仅祭坛退出结算后的发布失败采用 whole 已提交单元的默认精确保护：旧按投影前重新捕获的收件人逐 null physical Block，新同 cohort 的 selected 保护保留，报告者同时携实际 physical，故来源连接 Block 从 false 变 true；非报告者仍 null physical。原 typed 直接 cause 是 NotificationBus Failed 后合成的 InvalidOperationException，继续保留；不补已经被 NotificationBus 吞掉的 encoder cause。奖励提交与失败前不 End、失败不 Wake、健康回滚无 Fence／Block 及新 physical 合法重试只支付一次保持。 | TASK-49 两出口同组 SQLite 30exp／17gold、真实 SQL trigger 回滚→替换 physical 重试、双人实际投影 encoder 故障，核 Committed／ConfirmedRollback、cohort、原合成 cause、精确 Fence／physical Block 与来源 Close。共享投影同用于未迁祭坛入口，legacy 仍按同一份投影收件人逐目标保护。 |
| VC-B28 | 仅 LearnSkill／IncreaseStatus／LevelUpTicket／ChangeSkillSlot／SwitchSkillBuild／TutorialLevelUp 的 fine scope 归入 whole：旧 Learn／换槽／切页／教程已提交后 Dispose 抛原 IOException、未登记响应，Increase／Level 则吞成 ACK2／99 并健康结束；新六路在原成功投影登记后 Dispose，保 cause 为 PostCommitPublicationFailure，关闭精确来源。三个 Main 按既有默认保护 Fence／Block／DiscardExact；三个 ordinary 仍空 selected／None，不增加 Fence／Block。所有写仍各自 Committed，保真实存活帧及次序，不补失败 ACK、不把它冒称回滚。 | TASK-50 `SkillGrowthProtocolCommandFlowTests.Actual_DI_scope_failure_keeps_transaction_completion_frames_and_exact_source_cause` 六旧新正式 decoder／SQLite／实际 DI Dispose／registered encoder／production outbox／受控 physical 对照，直接核正向技能／消耗／经验／槽位／切页／教程级别、Completion、声明／精确 physical、帧和关闭链。 |
| VC-B29 | 仅 LearnSkill 无库存 receipt 的直接 ACK、IncreaseStatus 首次 required explicit batch 编码失败：旧原 IOException／Main Committed／无 actor Block，改为 whole 默认 PostCommitPublicationFailure／精确 actor 保护；保原 cause、资产一次与来源关闭。LevelUpTicket 原 OpeningCompleted 投影编码返回 Failed 且调用者忽略，继续不抛错、不关闭；Learn 的有库存 receipt 投影仍按原返回语义。三个既有 committed replay owner 的 raw cause／原 entry／Ordinal／无新增围栏及精确旧 physical 关闭保持，不给首次成功新增 raw。 | TASK-50 三路 initial actual encoder 与每 owner 成功后同帧 replay／真实 InventorySnapshot encoder fault 正式旧新对照；核真实事务与SQLite资产、实际 encoder、替换同ID physical 隔离、当次 replay root／source／close cause。 |
| VC-B30 | 仅 AcceptQuest／GiveUpQuest／SetQuestTrigger／FinishQuest 的 fine Dispose 归入 whole：旧事务已提交后 scope 抛原 IOException、未登记 ACK；新 scope 在原完整成功投影已登记后失败，保同 cause 为 PostCommitPublicationFailure，按原序投递后关闭精确来源。三个 plain 仍空 selected／None，不新增 Fence／Block／DiscardExact；Finish 原 Main Committed，精确 actor 默认 Fence／Block／DiscardExact。 | TASK-51 正式 `QuestProtocolCommandFlowTests` 四路 actual DI Dispose 旧新对照；真实 decoder／dispatcher、SQLite、生产 outbox／encoder／physical sender，逐帧核原次序、actual Completion、同 cause、root/source close 与有界保护。 |
| VC-B31 | 仅四入口首次直接 ACK 实际 encoder 失败：旧原 IOException／无 actor 围栏；新 whole 默认 PostCommitPublicationFailure／原 cause，事务及 Finish ledger 仍 Committed，均无帧并关闭精确来源。三个 ordinary 仍无 selected 围栏，Finish 按原 exact actor 默认保护。Finish 动作内专用拒绝仍确认回滚后 ACK22，native Commit 的普通 SQL 异常不映 ACK22；原 committed replay 的 raw cause／原 entry／Ordinal／无新增围栏与替换 physical 隔离保持。 | TASK-51 四路初始 actual encoder、Finish action晚SQL/native Commit分开、取消与递归 typed、成功后同帧 current-state replay及真实 replay encoder替换隔离正式旧新对照；未授首次raw／Self或新恢复能力。 |
| VC-B32 | 仅 0x0204 的 EntryAllowance／FatiguePotion／QuestClearCoupon 已提交 fine scope.Dispose 归入 whole：旧正常结束、拒绝 ACK 与健康连接，改为保原 cause 的默认 PostCommitPublicationFailure，保实际已登记成功投影次序后关闭精确来源，并对本次精确 actor Fence／物理 Block／DiscardExact；Main 与 ledger 仍 Committed。疲劳 Main 后独立 ItemUsage ReadOnly 故障由旧 raw cause／无 actor 围栏改为保 cause 的默认 typed 与相同精确保护；独立单元仍 ordinary／空 selected／None。三路初次 OpeningCompleted 返回 Failed 原调用方继续忽略，不升级为 required，也不新增关闭或围栏。原普通动作及 native Commit 健康确认回滚拒绝／同 entry 与 Ordinal 重试、原 committed replay raw 与物理替换隔离保持。 | TASK-52 `CouponProtocolCommandFlowTests` 的三路 actual decoder／SQLite／DI Dispose／actual encoder／production sender 对照，以及独立 ItemUsage ReadOnly 故障；实际帧、原 cause、Committed、StandingOf.Fenced／Block／委托 DiscardExact 与 physical CloseAsync 均直接核验。原强集成与 codec／使用量 reset、chunks、cooldown 判据保持；不冒称 A21 实机。 |
| VC-B33 | 仅本Task已提交的称号Store／Withdraw／TBM1、成就、外观和周签到默认whole生命周期：三称号／成就旧fine scope.Dispose被业务catch转拒绝ACK且健康，新保成功ACK／原投影后默认typed、原cause并关闭精确来源及Fence／physical Block／DiscardExact；外观／签到旧scope raw且无帧，新保已登记帧后相同默认保护。三称号／外观／成就首次actual encoder与称号结果ToGameSlot失败由旧raw／无围栏改为默认typed及原精确actor保护；业务action与native Commit健康确认回滚仍按原policy。Query只读无写，scope／encoder仍raw且不新增围栏；成功只读投影接缝的scope帧从旧无帧变为whole已登记五帧，生产Get当前既有只读生命周期拒绝仍相同cause／回滚／无帧。周签到两个Project Failed仍忽略、无新增关闭或围栏。 | TASK-53 `TitleProtocolCommandFlowTests`旧新实际decoder／SQLite／DI Dispose／production outbox／actualencoder、完整snapshot与same-ID physical隔离；QueryRows仅只读协议投影接缝，EventView保真实attendance而省其他活动，Host原真实weekly入口另跑。取消／递归typed、slot mapping前置拒绝及结果换算catch外、真实Query拒绝和签到mail原子／重复有正式守护；不授raw／Self或新机制。 |
| VC-B34 | 仅设置／已读六入口的 fine scope.Dispose 归入 whole：旧保存已 Committed 后抛原 IOException，新保同 cause 为默认 PostCommitPublicationFailure；仍无 ACK／snapshot／任何出站帧，精确来源关闭。三个账号设置仅 origin.SelectedSession 非空时及 CharacterOption 保原精确 actor 的默认 Fence／physical Block／DiscardExact；账号态三设置及两 Notice 始终不新增 selected 保护，Secondary 的角色 owner 不构成 actor。普通动作与 native Commit 健康确认回滚仍传播原 cause、无 ACK，不改原资产与 profile／归属门。 | TASK-54 `SettingsProtocolCommandFlowTests`旧新正式 decoder／dispatcher、SQLite、真实 DI Dispose、production outbox／受控 physical 对照，直接核完整 blob／双 profile／双 bank／两 owner、actual Completion、精确 actor／None footprint、原 cause／Fence／Block／实际 DiscardExact 和 same-ID physical 替换隔离。无 ACK 路径不虚构 encoder 失败；无新 Main／ledger／raw／Self 能力。 |
| VC-B35 | 仅 TaggedIdReport／DecreaseDurability／Premium slot3 的已提交 fine scope.Dispose 归入 whole：旧原 IOException 且无响应，新保已登记原 ACK／状态帧后默认 PostCommitPublicationFailure，保同 cause 并关闭精确来源；三普通写仍空 selected／None，不增 Fence／Block。Tagged 与 slot3 首次实际 encoder 失败由原 cause 改默认 typed；Durability 的 Project Failed 仍忽略，无新增关闭。Premium slot4 仍独立 ReadOnly，scope／encoder 继续原 cause、无 Fence，scope 时点由旧无帧改为新状态帧已登记后失败。 | TASK-55 `ReportProtocolCommandFlowTests` 真实 decoder／dispatcher、SQLite／run participant、实际 DI Dispose、生产 outbox／actual encoder／受控 physical 旧新表征；action 与 native Commit 确认回滚分开，精确来源及 same-ID physical 替换隔离直接断言。无新 Main／raw／Self 或 selected 保护。 |
| VC-B36 | 仅 ShopPurchaseCount 的原 Direct fine scope.Dispose 归入 whole：旧查询完成后抛同一原 cause、尚无帧，新保同 cause 且原查询响应已登记，按发件箱顺序发送后关闭精确来源；没有事务、typed 提交结果或新增 selected 保护。其余十四轻量入口的原日志／静态响应、首 encoder 与日志／查询异常原 cause，以及 Shop 的 Project Failed 忽略均保持。 | TASK-56 `LightweightProtocolCommandFlowTests` 同一真实 decoder／dispatcher、实际 DI Dispose、生产 encoder／outbox／physical 旧新正式表征；十五入口完整响应字节、原日志类别／语义、身份有无及 selected-not-world、无环境事务和 same-ID physical 替换隔离直接断言。无新 Write／ReadOnly／Main／raw／Self 或权限。 |
| VC-B37 | 两塔真实定时回城由旧 Leave 在领域动作前因缺显式 footprint 来源回滚，改为真正 TrustedInternal 请求下的原普通写与精确 owner 声明，合法到期 run 能离场并释放占用。不得伪帧、legacy 回落或扩大效果／恢复权限。 | TASK-57 当前真实 Tick 旧表征两例与 `Legal_internal_timeout_return_removes_the_actual_run_and_releases_its_occupancy` 两个业务断言 RED；迁移后须同一实际来源 GREEN及当前性／footprint拒绝反例，未完成前不称已修。 |
| VC-B38 | 仅两塔返回一轮改为扫描与逐离场／逐 run CMD：允许项间正常FIFO请求插入、每CMD发件箱交付、快照后新离场延后、显式子链；保整轮hosted合并、手工不合并、领取前待办不丢和逐项故障隔离。单项失败由执行器Error及生产者无异常Warning记录，不能保持旧fine日志形状而重引重复Error。 | B9专节；TASK-57真实旧departure与授权旧算法矩阵先证旧隔离，新来源须验证完整领取／FIFO／链／取消／排空、持续首项失败及后项资产和状态，不能仅比CMD调用次数。 |
| VC-B39 | 仅两塔内部逐项 fine scope 收入 whole 的默认已提交失败边界：保原 cause、实际资产／域状态与已登记帧，scope在完整投影后收口，失败不再作为原fine的raw scope错误；Boss只保护该项各工作单元实际声明的累计精确参与者与捕获physical，Illusion仍空selected，不误伤另一run或替换连接。多成员时不能丢掉后续已声明physical，也不凭可查询性新增参与者。累计声明精度修复同时覆盖已迁 FinishLoading：先 load 声明 leader null、后 start 声明 leader physical，两个单元已提交后的 whole scope 失败应关闭该已声明 leader physical；仅此旧漏保护断言改变，原帧／事务／Fence／cause／链不变。 | TASK-57实际旧Boss departure scope／uncertain及明确授权的旧sweep算法对照；新逐项actual DI scope、真实投影故障、多成员后单元失败与物理替换必须验证。范围仅本节，raw三出口及其他默认／保留边界不变。 |
| VC-B40 | 自 TASK-58 起的迁移冲刺采用统一基础设施失败规则：迁入 whole scope 后，提交后的编码失败、作用域释放失败、未请求的取消和结果不确定，一律沿现有默认失败收口，保护本次实际声明的精确参与者并关闭精确来源连接（内部来源无连接时不虚造来源），不补业务失败 ACK。Task 不再为这些罕见路径逐协议登记例外或重复建立故障矩阵；正常拒绝、错误码、事务点、帧顺序和收件人的真实业务差异仍单独记录并由业务测试守护。 | 用户 2026-10-26 两阶段指令及 `../../process-reset-2026-10-25.md` §8；后续 Task 引用本行，复用共享默认失败判据及其反例。新能力／注入类别或未明边界的当场裁定记入 `verification/phase2-backlog.md`，迁移账闭合后统一回写并独立审查。 |
| 其余 | 除 VC-B1～B40 明列范围外，不新增协议字段/字节/帧序/关闭/持久化结果变化；A的VC-1～VC-10原有结果保持。RQ-006三个具名即时编码登记出口的 raw 原因对象、Committed、无新增参与者围栏、原 source close 及当前投影重放属于原行为保留，不新增 VC | 表征/Golden/线级及工作单元对照；AC-003的具名登记故障及默认保护对照 |

F-131维持现状不是相对A的新变化。若实施需改变本表之外的可见结果，先记录主控裁定并修订本Spec、重过受影响评审，不能在最终汇报里事后冒称纯搬迁。

## Acceptance Criteria

| ID | Covers | 可判定结果 |
|---|---|---|
| AC-001 | RQ-001～003、009 | 注册表/反射证明每个已注册非遥测消息唯一映射，未注册枚举合法；handler只入队，CMD注入面合规，无CMD调用CMD。每条判据的违规反例被拒，遥测例外闭合。逐项来源表覆盖非协议入口，不以139作为固定最终CMD数量。祭坛纯唤醒另须证明只放行具名窄能力、拒绝完整调度器及相似接口；原当前作业内成功投影后的时点保持，whole清理错误之前已唤醒、此前投影失败则不唤醒，既有生产者的同实例／合并／deadline／取消语义保持。翻牌控制另须证明只放行具名固定阶段能力、拒绝完整生产者及相似接口；原当前作业内投影后调度／手动成功后投影前取消的时点、单一待办状态／替换token身份／已入队取消／停机drain保持，到期仅提交typed内部CMD且保持独立Timer根及无来源连接。 |
| AC-002 | RQ-004～006、014 | 经真实入站路径：非效果消息无账本正常；无选中效果请求原样拒绝且不结算；已选中请求一次提交后同序号重放；缺应有上下文、重复主结算、失效上下文/泄漏续体不能结算或降级内部信任。两个连续请求的权威/足迹不串用，单例领域消费方同样受约束。 |
| AC-003 | RQ-006～008 | F-88/F-93四项先有会红的守护证据，再迁移；具名判定、账本、会话隔离、登录顶替和槽位边界既有断言不放宽。MonsterDeath的共享发布失败仍封锁全部参与者；祭坛等逐参与者围栏和两塔提交后内存变更顺序保持。RQ-006三个具名出口保留真实登记 encoder 注错及原重放断言，并区分证明原 cause 同对象、真实 ledger entry/原 Ordinal、Committed、失败 batch 未登记、无新增参与者围栏、原 source close 顺序、后续同序号当前投影且不重复结算/原ACK。CollectionBox 迁移前须经真正旧 decoder 路径重跑 Select/Put 的固定 encoder cause oracle，迁移后同容器重跑；不把旧手造 request 的 dispatcher 用例当作 decoder 证据，不将 Withdraw 未动态注错写成已有覆盖。相同 IOException 来自提交后业务、非许可登记、原 typed 发布或叠加额外独立 scope dispose/cleanup 错误时，仍保原 cause 并按默认规则全参与者围栏；来源许可不能跨 request/job/目标、错误阶段/调用或失效后复用，捕获登记错误不能继续写入或成功结束；独立 unit/scope cleanup 再次抛同一 cause 对象也撤销 raw 资格，保留独立失败事实及全部精确参与者围栏。真实来源验证须有可编译、有行为价值的 mutation 反例。Cera 仅按专节及 VC-B4 区分可恢复拒绝与默认失败，不取得第四 raw 出口；新增默认保护仅覆盖本次显式足迹／结算的精确买家及其物理来源。Committed／Uncertain 事实进入默认失败保护时，该 actor 的精确 Fence／连接阻断／DiscardExact 必须成立，且不能误伤替换后的会话、其他玩家或队伍。Mailbox 另按专节及 VC-B5 保原 NonEffect／空 selected 范围及全部 current／session 身份门，不新增 Cera actor／footprint／Fence／DiscardExact；不可恢复失败仍须关闭精确物理来源并保原链，不误关替换后的物理连接。VC-B6 六消息的 committed replay 有限 owner 须逐具名先旧 decoder／实际 encoder 注错、后同容器新路径证明本条全部 raw 条件；TASK-32 没有该实证，不授首次成功 raw。 |
| AC-004 | RQ-007、008 | 旧204条注册和其他自开事务/事件边界全部在对照表有原位置、最终CMD/单元、提交点去向与证据；170写/9只读及其余执行形状不凭数字假定同一边界。无遗漏，合并/拆分附理由。主结算声明、真回滚、已提交后失败及重发不重复结算都有区分用例；组装故障部分按RQ-008条件：采用则验证提交后组装失败/重发，未采用记录禁用/不可达、不称已修；该条件不豁免其他既有提交后业务失败的围栏和不重复结算。RQ-006三个具名即时登记保留另按AC-003验收，包括CollectionBox首次成功主结算结束后的原成功batch；不改原主事务/登记分段或replay的普通Write边界，不作为F-132已修证据。Cera 对照 TASK-28 的成功／正常拒绝、真实 action SQL 和 SQLite fallible Commit 原 oracle，证明相同事务资产／事件与原拒绝响应保持；正常返回拒绝的原空提交不误记回滚。确认回滚后的合法新购买只产生该次购买，不冒效果 Main 或同序号幂等重放。Mailbox 六消息及原 fine 的提交／投影点逐项对应，真实 action 内自回滚时点、Current=null、Completion 和原 MailException／普通动作拒绝响应保持；KnownStorage native Commit 确认完整回滚与 action 故障分别证明。原设备 UID／core、stack／gold、mail／attachment／claimed／事件和全部生产列含 NULL／BLOB 回滚保持。standalone／ambient caught refusal 后 Commit 必 throw 的共享强断言、ItemDelivery／GM prepared 整批 audit／QuestHelperPuppet／Housing／Wedding 原子性及即时自结算保持，不以延迟 RequireRollback、第二事务或削弱原测试代替。去注错后的合法新发送不冒 Main／同序号幂等。VC-B6 另按专节逐一核六 Main／独立 premium／refresh 的原提交点、opening 与 Rental 不同异常出口。通过真实满32格 NoRoom 直接证明 Result2 body、全部资产／事件含实际 rowid 回滚、ConfirmedRollback／无新增保护／不刷新，去阻塞后原 entry／Ordinal 合法重试仅结算该次；与原正常返回空提交拒绝的 Committed 对照。 |
| AC-005 | RQ-009、012、014 | 两单元中第二回滚只刷第一；提交后异常不撤销已提交写或误记可重试（组装故障按RQ-008条件：采用则验证提交后组装失败/重发，未采用记录禁用/不可达、不称已修；不豁免其他既有提交后业务失败的围栏和不重复结算）；未登记任何消息就失败仍关闭来源。RQ-006三个具名即时登记保留（含CollectionBox原成功batch）另按AC-003验收；不放宽本条默认不确定保护及额外独立错误的围栏。执行器外登记/写、等待网络、自我等待及不确定围栏判据和反例照常通过，CMD/NOTI类型边界不变。Cera 的 VC-B4 须以受控非 fallible 违约、真实 SQL 加回滚第二错误及独立 CMD scope 清理故障作区分：实际提交／回滚／Uncertain 不混同，原 cause 与附加 cause 引用保留，不再转 StorageFailure ACK 或正常结束；本次精确买家在 Committed／Uncertain 默认失败条件下的 Fence、对应精确连接阻断、DiscardExact及来源关闭／继承链均有直接证据；对照正常成功、正常拒绝及健康的确认回滚无新增围栏／关闭，并证明替换后的会话和其他玩家／队伍不受影响。已登记条目及其实际次序按原机制验证。scope 故障须记录其发生前已登记的真实消息及最终发送／关闭顺序，不把“无普通失败 ACK”误写为“零帧”，不靠清 sticky／撤消息或扩 raw 求过。Mailbox 的 VC-B5 须分别证明 C1 durable Uncertain、C2 实际 SQL 加 Rollback 第二因、C3 从旧 fine scope 对应到 whole 的真实 DI scope 错误均不作普通 ACK 恢复或正常结束；精确来源实际 CloseAsync／原链、空 selected 及无新增 actor／Fence／DiscardExact 有直接证据。C2 的最终 typed 错误图须包含原 action 与 Rollback 两 cause 同对象，既有独立错误事实也不丢；该精度同样覆盖触达共享 RunAsync 的 standalone／ambient 调用。以原共享强用例和所触 caller 验证保持原子性／即时结算，未动态覆盖的 caller 故障须明确。取消／未知来源或完成／旧 sticky／guard／失效 request／独立 unit／lease 清理及同对象独立二次错误不能获得恢复，不放宽既有 Write／ReadOnly／WriteResult 十个实际 consumer 的默认保护。健康成功／拒绝／完整确认回滚为无新增关闭对照；记录 whole scope 故障前已登记的原 success／normal refusal 帧、按原 unit 状态保留的实际发送顺序及置后关闭，不由旧 C3 slots=[] 推零帧。VC-B6 以旧 fine／新真实 whole scope 及 Main 后独立 premium／refresh 故障对照，直接证明专节的 cause／独立发生、实际 Completion、默认保护和实际发送→关闭／原链；真实替换 session／不同物理连接与无关玩家不受误伤。对照纯 ConfirmedRollback 后第二普通错误及健康 NoRoom，不能统一伪称 Uncertain／增围栏或由旧响应前故障推新零帧。根纯搬迁／语义分离由实现检查点及逐条差异核验。 |
| AC-006 | RQ-010、011 | App清理检索不再有ClearAllPools，按自有规范化数据库实际写/只读池清理；不改类并行/期限/Skip。写端完成的真实传输失败按Info记录、普通故障Warning，Failed/关闭/observer及其他连接结果不变，不能由Resolve整体降级导致Stale绕过。 |
| AC-007 | RQ-012 | 对T1～T6及B临时机制逐项检索并检查实际职责，最终命中为零；常设两处AsyncLocal与登记守卫有明确归类。无转发层或Obsolete属性，不能靠改名字通过。 |
| AC-008 | RQ-002、013 | 每项交接有已实现证据或契约允许的具名裁定；F-07按VC-B3验收实际FIFO位置与允许的请求插入，扫描只捕获周期/精确session IDs，producer仅入队；子CMD重新读当前事实，旧周期/旧session无登记无水位推进，下一次扫描覆盖正确新周期。保持日切/登记水位正确性和单连接条目顺序，不要求原整批不交错；事件Reliable同事务及LiveOnly先后不变。两塔返回另按B9／VC-B37～B39验收真实内部回城、逐项事务／故障隔离、整轮合并、待办精确领取、项间FIFO交错、链／拒绝／排空／取消及各塔当前性与物理保护；来源为空不等于参与者physical为空。F-122/F-124/F-129和F-131明确保留结论都有对应证据。未做项如实留后继，不以转交代替本Spec义务。 |
| AC-009 | RQ-014、015 | 新鲜完整Release构建0警告0错误；八测试项目全量数量闭合、失败身份逐项判定，表征10通过0跳过，线级/codec/Golden原断言保持。相对8350的测试增减和断言/驱动变化可追踪；取证七程序集正对照齐，反例类/对照外0，保留exe0的观察边界。 |
| AC-010 | RQ-016 | 冻结状态按A工具和同口径完成测量并和A对照；单条CMD max≤2秒，原目标未达只记录为优化，数量级异常按根约定处置；合成负载与未实机局限明示。 |
| AC-011 | RQ-013、017 | 文档示例/依赖/新增协议步骤与最终实现一致；最终汇报含结果、VC-B1/B2/B3/B4/B5/B6及其余不变、遗留/保留与证据、原性能口径、Astra待审和不推送不PR状态。A封存资料无修改。 |

### B10 的补充验收归属

- AC-001／007 验收具名注入、真实注册分发、纯数据 HTTP 入口、协议 CMD 无共用基类及生产 CMD 无可空依赖／Func 能力；每条结构规则同时接受真实合法形状并拒绝对应坏例，不靠空扫描或固定 handler 数通过。
- AC-002～005 验收 cleanup／HTTP 固定终止审计／生产者取消／完成桥与事件编码发生事实的来源、request／job、阶段、清理与独立失败边界。复用各机制已有强例及其反例；正常拒绝、事务点、帧序、受众的真实差异单独守护，VC-B40 罕见路径不新增逐协议矩阵。
- AC-008／009 验收 F-07 的真实接受水位、整轮排空、事件可靠性、生命周期精确清理、启动顺序和五账完整性；T-011 的删除不删掉旧保护。测试库存、旧用例去向、准确失败／跳过及独立错误日志分类必须闭合。
- AC-010／011 继续承担冻结后的同工具测量、最终文档、六根条件及17 RQ／11 AC／18交接与另列事项的完整汇报；TASK-65 全量不是结构优化后最终证据，五账闭合和本轮 Spec 审查也不代表 B 完成。

## Success Evidence

| 根条件 | 本文验收与主要证据 |
|---|---|
| SC-2 | AC-001/002/007：注册映射、注入/嵌套反例、协议与内部来源清单、显式请求状态证据 |
| SC-8B | AC-002/003/005/006/008/009：A不变量回归、保护迁移、首单元修正、完整测试与取证；不把新驱动的“记录成功”当事务证据 |
| SC-9B | AC-010：冻结测量、环境/样本/负载口径和A对照 |
| SC-10B | AC-007/011：过渡清零、最终文档及登录例子 |
| SC-11B | AC-011：本Spec最终汇报与外审输入 |
| SC-13 | AC-003/004/005/007：保护位置等价、逐旧边界工作单元表、T1～T6和临时机制清零 |

证据保存在本子目录的reviews/verification/tasks等配置路径，不改写A的同类文件。具体检查命令、工作单元清单展开和功能簇步骤由Plan及临近Task承载；不可把拟议方案或未执行检查计为完成。

## 修订记录

### B1（2026-10-24）：F-07拆分的有限可见顺序变化

来源：规格挑战CH-2，以及主控方同日明确裁定选择按F-07原定目标拆分，并接受VC-B3；不是用户本人追加的新要求。唯一FIFO队列中，扫描结束后才由producer入队刷新CMD，不插队、不预留队列位置，也不在CMD内等待子CMD，因此不再承诺原整批刷新不与期间请求交错。

本修订仅明确F-07责任、AC-008和变化表：扫描只捕获周期/精确session IDs；子CMD执行时重新读当前事实，旧周期或旧session不登记/不推进水位，正确新周期由下一次扫描覆盖。周期/精确session/登记水位正确性与单连接条目顺序等其余承诺保持；具体水位和来源合并机制由Plan决定。A保持封存，根目标、六项B条件及协议CMD中心方案不变。当前修订须经匹配的独立Round 2批准后才继续Plan，不沿用旧稿批准硬过门。

### B2（2026-10-24）：具名即时编码登记失败的兼容边界

来源为独立[边界复核](reviews/effect-registration-boundary-review.md)的局部material/spec finding，以及主控沿原授权保留A行为的明确裁定；具体冲突证据、裁定及受影响gate见[amendment记录](verification/spec-b2-amendment.md)。本修订只澄清RQ-006、AC-003及AC-004/005的交叉引用、VC表的原行为保留：旧fine CMD之外已证的两个即时raw编码登记出口，不因移入新协议CMD而新增typed不确定及参与者围栏；原受保护发布、业务失败、来源不明及额外独立清理错误的保护保持。

17项RQ、11项AC、VC-B1/B2/B3及其余要求不变，不新增VC、不修改根契约/子契约或A封存制品，不改变协议CMD中心方案或Spec baseline。具体机制留Plan/Task；state: ready仅表示本稿语义可执行、可判定，不表示独立批准。B2须先经新鲜有界challenge及匹配独立review，再修订并复核Plan P2；amendment-only检查点之后才可规划/实施首次新effect/Main启用，不能沿用B1/P1批准替代受影响gate。

### B3（2026-10-25）：CollectionBox 首次成功 batch 的原编码登记出口

来源为主控在 TASK-19 已独立批准并提交 `dba3ede68e15f892afd35534db9fed7ed00d04a8` 后，沿根 Goal/Scope B/SC-13、父 RQ-023(c) 及子契约 Goal 保留 A 行为的明确裁定；原冲突、旧源码/测试证据层次、未实证边界及受影响 gate 见 [B3 amendment](verification/spec-b3-amendment.md)，前版逐字节快照见 [Spec B2](verification/evidence/spec-b3-amendment/spec-b2-before.md)。本修订仅在 RQ-006、AC-003、AC-004/005 交叉引用与 VC“其余”中补第三具名旧出口，不新增 VC；B2 修订记录所说“当时两个”仍为其历史范围。

本轮只静态复核，没有新 native 运行。Select/Put 旧用例绕过 decoder、只断言 IOException 类型；Committed/无新增围栏由后续当前投影重放间证，未直接验 cause 引用、真实 ledger/Ordinal、围栏或 source-close；Withdraw 未动态注错，RecordedOutbound 的 Discard 不真实关闭 socket。后续产品单元按 AC-003 先旧后新补直接证据及默认保护/来源许可反例，不能提前引用尚不存在的 Task 结果。

17 项 RQ、11 项 AC、VC-B1/B2/B3、Spec baseline、根/子契约及 A 封存制品不变，P3 commit-result、Compose 禁用及 F-132 边界不变。TASK-01～19 未使用此第三出口，其已接受结果及 B2 原两许可/默认保护继续有效。本稿 `state: ready` 不表示 approved；落稿后停止，先 fresh 有界 challenge 和匹配当前 B3 的独立 Spec review，通过才另行授权 Plan P4 受影响方法修订，再过 fresh Plan challenge/review、主控 amendment-only 本地检查点，最后才规划 TASK-20/产品。不得沿用 B2/P3 批准替代受影响 gate，不提前修改 Plan、路由状态或创建下一 Task。

### B4（2026-10-25）：Cera 购买不确定与独立清理失败不再映为普通拒绝

来源为 TASK-28 的已独立 approved 旧行为调查及主控在其检查点 `f42efb55bedad71dc1196100e39255bc483ed72c` 中的明确裁定。旧 action SQL19/1811 与真实 SQLite fallible Commit19/787 均确认完整回滚并发送原拒绝 ACK；受控非 fallible Commit 违约、SQL 加回滚第二 cause，以及 fine scope 提交后清理失败，也均被旧 handler 映为拒绝 ACK，dispatcher 正常返回而没有会话围栏或来源关闭。后者的实际资产可能已经提交，不将其称为普通可恢复回滚。详见 [B4 amendment](verification/spec-b4-amendment.md)；前版逐字节快照为 [Spec B3](verification/evidence/spec-b4-amendment/spec-b3-before.md)。

主控选择只在 Cera 按既有显式 CMD 的 sticky／typed 默认失败收口处理后者，新增 VC-B4 并在 RQ-006/007、专节及 AC-003～005/011 明列；不新增通用恢复、raw 出口、清失败或出站改造。成功／正常拒绝及健康清理完成的真实确认回滚后原响应与无新增围栏／关闭保持，NonEffect 不新增 Main／重放幂等；scope 结束前已登记条目按现有发件箱次序处理，不承诺故障零帧。

ready R2 按 fresh [CH-1](reviews/spec-b4-challenges.md) 和主控对足迹／默认收口源码的后继复核，纠正 R1 未明列新围栏的遗漏：本次精确买家须作为显式足迹／结算参与者，在 Committed／Uncertain 默认失败条件下接受既有精确 Fence、连接阻断与 DiscardExact；不为保旧空选中集合而削弱保护，不扩大其他身份或协议。R1 原稿及其 amendment 已逐 bytes 封存于 [R1 Spec](verification/evidence/spec-b4-amendment/spec-b4-ready-r1-before.md)／[R1 amendment](verification/evidence/spec-b4-amendment/spec-b4-amendment-r1-before.md)。TASK-28 中先前“不擅自增减／不承诺新增围栏”的裁定原样保留；此处是进入本轮 Spec gate 的后继显式裁定，不改其旧取证事实，也不把纠正当独立 review 批准。该 CH-1 保持 immutable；本次是高风险挑战后 review 前的一次最小纠正，R2 直接交另一 fresh 独立 reviewer 验证，不重生挑战。

17 项 RQ、11 项 AC、VC-B1/B2/B3、三个具名 raw 出口、根／子契约、Spec baseline 和 A 封存制品不变；VC-B4 是本文明确且待受影响审查的新增有意变化。TASK-01～28 的已接受结果保持，本轮无新 native 或产品／测试变化，不把旧 oracle 当迁移后通过。`state: ready` 仅表示本稿可审；须 fresh 有界 challenge、匹配本 B4 的独立 Spec review，approved 后再另行修订受影响 Plan P5 并过 fresh challenge/review，主控消费及 amendment-only 本地检查点后，才详细规划后继迁移 Task。不得沿用 B3/P4 的批准替代本次 gate，不提前改 Plan／routing 或创建 Task29。

### B5（2026-10-25）：Mailbox 即时自结算保留与不可恢复失败的精确来源收口

来源为已 committed done 的 TASK-30（检查点 `d3a5fec85b9ecaf633efd2157b65946928b008cf`）Controller Consumption R1 末节主控裁定，不是 writer 新选语义或 TASK-30 review 对 B5 的批准。其 freeze-07／build-07／final-03 已证 action 内真实自 Rollback、Current=null，与 KnownStorage native Commit 确认回滚分离；C1 durable Uncertain、C2 SQL＋Rollback 第二因、C3 fine scope 已提交后的错误原来均 ACK3／正常返回／连接健康，且 C2 typed 图丢原 SQL cause。按根／子 Goal 允许经审查逐条明列有意变化的既有授权，新 VC-B5 仅把这些邮箱不可恢复失败改为现有默认失败／精确物理来源关闭／原链，并要求共享 RunAsync 最终错误图保两原 cause 及 typed 事实。原成功／正常拒绝／健康完整确认回滚响应、空 selected 范围和共享即时自结算／原子性保持，不移植 Cera actor／footprint／Fence／DiscardExact。细节、权威 raw 的单附件3帧／多附件4帧勘误及未测范围见 [B5 amendment](verification/spec-b5-amendment.md)；前版逐 bytes 快照见 [Spec B4](verification/evidence/spec-b5-amendment/spec-b4-before.md)。

17 项 RQ、11 项 AC 的既有 IDs、VC-B1～B4、三个 raw 出口、baseline、根／子契约、A 及已提交 TASK-01～30 保持；本修订只补相关 RQ／AC 交叉引用、Mailbox WHAT 专节和 VC-B5。当前 P5 未改，未选新 API、未运行 native／新测试、未实施或关闭 ledger。本稿 `state: ready` 仅表示可审；须 fresh 有界 challenge → 匹配当前 B5 的独立 Spec review，通过后才另行 Plan P6 的受影响 HOW 修订 → fresh Plan challenge/review → 主控消费及 amendment-only 本地检查点，之后才可规划／执行 Task31。旧 B4／P5／TASK-30 的批准均不替代新 gate；Astra、用户收尾及不推送／PR 等限制不变。writer 落稿后停止，交主控。


### B6（2026-10-25）：四 remaining opening 与 Rental 的可恢复拒绝及完整失败边界

来源为 committed done TASK-32 `17d924a06b1237ca2b4f5997983c0a4923dfc050` 的旧行为调查与后继 [主控 WHAT 裁定](verification/spec-b6-controller-decision.md)，不是调查 reviewer／writer 自行批准新语义。新 VC-B6 明列：Rent 真 NoRoom 健康确认回滚由旧 Frozen／Settled 缺口导致的 typed／Blocked／断线，改为原编码已有 Result2 正常拒绝、ConfirmedRollback 和合法同帧重试；旧 fine scope 收入 whole、Main 后独立 premium／refresh 错误采用完整默认 typed／精确参与者保护，scope 前原已登记条目按真实状态／次序保留后关闭，不承诺零帧。四 opening 健康动作／native Commit 确认回滚原 ACK、Rental 异常确认回滚无 ACK／来源关闭、正常返回空提交拒绝、Main／第二单元提交点及其他健康结果保持。原 C1/C2 不确定及 cause／保护保持，只有真实默认条件才围栏，不把 ConfirmedRollback 的后续普通错误伪称 Uncertain。

本次仅更新 RQ-006／007 交叉引用、相关 WHAT 专节、VC-B6、AC-003～005／011与本记录；17 RQ／11 AC 的 IDs、既有 VC-B1～B5、三个 raw 类型／其他 owner、baseline、根／child、A 及已提交 TASK-01～32 不变。六新 whole 的 committed replay 只是现有 family 有条件的具名 owner：须先补各自旧 decoder／实际 encoder oracle、再新同容器对照及来源／默认／清理反例，TASK-32 没有这项动态证据，未满足不授 owner并回受影响门；不增首次成功 raw 或 Mailbox 自结算权限。现有实际 WriteResult consumer 为十个，当前 AC 的“五”按该已审核事实纠正，历史记录原样保留。详见 [B6 amendment](verification/spec-b6-amendment.md)；B5 前版逐 bytes 快照为 [spec-b5-before.md](verification/evidence/spec-b6-amendment/spec-b5-before.md)。

当前 Plan P6／routing 未改，未选新 API／具体 Task 拆分、未运行 native／新测试、未实施或关闭 ledger。`state: ready` 仅表示可审；下一步 fresh 有界 B6 challenge → 另一 fresh 匹配本稿的独立 Spec review → approved 后另行受影响 Plan P7／fresh challenge／独立 Plan review → 主控消费及 amendment-only 本地检查点 → 才详细规划／执行新 Task。既有批准不替代这些门，根纯搬迁／语义分开提交、Astra／用户收尾和不推送／PR等限制不变。writer 落稿后停止。


### B7（2026-10-25）：祭坛纯唤醒能力的明确边界

TASK-45 的 P9 独立规划审查指出，既有 RQ-009 的封闭技术注入类别不含原祭坛唤醒；只由 Plan 加入接口会越过 Spec。主控接受该冲突，依据既有保留行为及裁定授权，仅在 RQ-009、注入说明和 AC-001 增加上述具名纯唤醒能力。原调用与状态依据为 `MonsterDeathSlice.cs`、`AltarProjection.cs`、`AltarScheduler.cs @ cc212b99`。不增加可见行为变化，不改根／子契约、中心方案、17 RQ／11 AC 的 IDs、其它要求或已接受 Task。

本稿为 B7 ready R1，按 process-reset §4 交同 TASK 唯一独立 Astra 审查本增量，并在其通过后复核对应 P9 方法。无挑战、无额外取证 Task；接口与判据增量在主控消费及本地规划检查点前不得实施。未提前宣称产品或测试通过。原 B6 及更早说明保留历史语义，本轮审查和门消费写入 TASK-45 与其受影响审查文件。


### B8（2026-10-25）：翻牌延时生产者控制的明确边界

TASK-48开工前核实：`DungeonClearSlice.cs`的成功结果投影后要在原作业内ScheduleAfterSettlement；`CardRewardHandlers.cs`的手动免费确认成功后、投影前要Cancel；`DungeonCardPresentationCoordinator.cs`的布局发送后要替换为自动确认，且自动确认仍是旧SelectCard细粒度命令的实际消费者（均以`d0dd43d2`为依据）。B7无参Wake明确不授权这些固定阶段／取消动作，封闭注入表也不能靠重命名DomainService或可执行消息输入绕过。主控按既有保行为及裁定授权，仅在RQ-009、注入说明与AC-001补此具名能力，交TASK-48唯一独立Astra先审本增量，再审P10及最终结果；不做挑战。根／子契约、单执行器与薄handler中心方案、17RQ／11AC IDs及其它边界不变。本文ready待受影响独立门与主控消费，不授权提前实施新接口或白名单。

### B9（2026-10-26）：两塔返回的真实内部来源与逐项隔离

TASK-57 以 `94746fe1` 为产品基线，真实旧来源表征证实两塔 Leave 在领域动作前因缺显式来源被拒；合法回城两个目标用例为业务断言 RED。真实 Boss departure（Start后追加受控同事务marker）在 action SQL1811、native Commit787、nonfallible违约与实际DI scope故障后仍处理后一cohort；另有明确test-only NonEffect授权的两塔sweep算法对照，不能冒实际生产可达性。旧准备 `task57-old-preparation-r2` 286通过，证据只证明旧状态，未证明新方案完成。

主控依据保留隔离的实际证据选择上述有界分解：不把全tick首错停止和其它cohort围栏扩张冒作纯搬迁，也不新开恢复能力。修改只涉及RQ-002、专节、AC-008和VC-B37～B39，17个RQ／11个AC身份及其它要求不变；根／子契约、单执行器、薄handler、CMD不嵌套、显式工作单元中心方案均拟保持，A封存资料不变。根中A“一次定时触发一个作业”的历史形状与本次B来源分解的边界须由独立审查明确核定，不能借F07的既有批准自动放行两塔；若审查判定需改根／子／中心，按既定用户门停止。

本稿为 B9 ready R1，待 TASK-57 唯一 fresh Astra 有界独立Spec审查；通过并由主控消费后才形成P11方法和受影响Plan审查，同一Astra最终审Result。无挑战，不复制来源当证据，不授权任何产品／core变更；当前只有正式旧测试准备。

### B10（2026-10-26）：迁移账闭合后的统一回写与结构收口

依用户流程 §8，在 TASK-65 本地检查点 `5f66b5b0` 确认159业务消息、204旧命令、73在范围内来源、5保护及8过渡均闭合后，统一回写 TASK-58～65 的36条 backlog 裁定；14遥测与12原不入队HTTP技术路由保持既有 retained 分类。本文新增按能力／失败阶段／来源组织的当前边界及结构验收，未改根／子契约、17 RQ／11 AC、A持续不变量、中心方案或既有VC历史。当前介绍的提交号按已接受历史映射更新，历史记录原号不改。

B10 ready R1 仅表示可审。按用户 §8 做一次 fresh 独立 Astra Spec 审查，不重开冲刺 Task 逐项评审或生成本阶段挑战；主控消费并形成规划检查点后才开始结构优化。最终 T-012 仍另有完整结果的一次挑战、独立结果评审、核验与封存；不以本轮批准替代用户安排的整分支外部 Astra。逐项来源及验收归属见 [B10 回写记录](verification/spec-b10-consolidation.md)。
