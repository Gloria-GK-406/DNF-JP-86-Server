# TASK-31 — 邮箱六路 whole 与真实自结算的封闭恢复

## Task Unit Contract

- Revision: 1（本契约与详细计划共同版本）。
- Outcome and references: 在一个有用交付中实现 P6 ready R2 §4 的独立 `WriteSelfSettledResultAsync`、真实 `AbortAction` 来源／入口同步 admission pin／单次交接／完整清理后恢复，迁移六 Mailbox logical 至唯一 `GameMailboxCommand`、五薄 handler，删除旧 fine 与业务 Delivery；共享 `MailboxDomain.RunAsync` 最小修正 C2 双因精度。交付真实 before/after、原强断言、可编译行为 mutation、新八 fresh full 与数量账，仅闭合七个 Mailbox canonical 行。消费 B5 RQ-001/003～009/012～015、AC-001～005/007～009、VC-B5；不完成整个 T-005 或 Spec B。
- Owned boundary: §2 的 **25 个精确产品／测试路径**、两份 canonical 的七行、本 Task 后续 append、新 `verification/evidence/task-31-inspection/` 与 `verification/logs/task-31-*`。共 **28 个具名文件＋2 个新证据前缀**；没有其它隐式可写目录。已有 Task、旧证据、Spec/Plan/Contract/routing、A、Support、现有 probe tools、版本与协议知识库只读。
- Required checks: §5～8 全部；真实 same-provider decoder/dispatcher/whole/领域/SQLite/adapter/outbox/production sender，真实 self SQL1811／ClaimGoldLimit 与 native Commit787分证，CH-1下层双窗口及行为 mutation，来源／能力／同对象独立发生／cleanup反例，VC-B5物理来源关闭与链、replacement隔离、原59focused和共享原子性、Release --no-incremental 0/0＋八 fresh full、DFO原74/7及新增 intentional 独立账、Golden15/Char10、注册／纯 family／三 raw 全 owners、完整 Git NUL nonowned bytes及精确 manifest/行尾/四类 whitespace。
- Dependencies: [TASK-30](TASK-30.md) revision 1、Result R1、fresh independent approved、Controller Consumption R1，已 committed done 于 `d3a5fec85b9ecaf633efd2157b65946928b008cf`；完整 Task 28640B、SHA256 `b7df96bbce279817f4f81e28de1063d52cabe531fcb927a6b68442d80e91344c`。其递归依赖不重复添加。B5/P6 的已接受 amendment-only 检查点是治理输入，不造新 Task dependency。
- Result review: required — 共享交易／runner／registration能力与六协议同时改变，来源窗口或独立 cleanup 错误会把不确定结算误判为健康拒绝；局部成功和编译不能充分排除，须 producer 之外 fresh independent result quick review，由主控消费后才本地检查点。本 Task-plan 不另设默认独立 review。
- Task-specific authorization: 仅父契约已有本地实施与验证授权；本 producer 不 stage/commit、自审、规划 next 或创建 Task32。真实Result及§7规定的Result自身postwrite完整性检查封存后停止 writer/native，交主控。不得 push/PR/merge/remote issue/version/tag/deploy/运行游戏/清理其它工作区、服务或数据库；不改上游 WHAT/HOW、不授后簇恢复能力。

## Detailed Plan

### 1. 唯一起点和已接受输入

每个工具／native 调用显式 workdir：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，branch `task/refactor-session-20261021`，开始 HEAD `db4e17c4ad4a2ee6fb69658bb3e890fbfca8ec1c`、index空。主控 readiness 之后另派 fresh producer；本计划本身不运行产品或测试。

B=`.rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd`，E=`B/verification/evidence/task-31-inspection`。current child Contract r1 8898B／`32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6`；Spec B5 53754B／`92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790`；Plan P6 ready R2 93207B／`1ec1dd1fcaa842e033e8b773fc5ed6fd0563199e8cbe3752fd68ac1e0a8c5691`。读取 `reviews/spec-b5-review.md`、immutable `reviews/plan-p6-challenges.md`、`reviews/plan-p6-review.md` 与 `verification/spec-b5-plan-p6-controller-consumption.md`，匹配 current bytes；CH-1对R1成立，R2已由独立review判充分解决并approved，主控已消费并提交。不是旧批准代新gate。

权威 before 是 Task30 `freeze-07`→`task-30-build-07`→`task-30-probe-final-03` 的14场，及 Core30（含partial14）＋ProtocolIntegration19＋codec10＝59focused。原 report 将 single/multi 合写各4帧是已提交文字勘误：依 B5 amendment 及 raw 15B帧解析，single=005E16B＋2×000E119B共3，multi=013B16B＋3×000E119B共4；不改旧 report/raw/checker/Task/review，也不把 Retry/HealthyControl/LiveOnlyDrain 混进原请求。没有 `.codegraph/` 时跳过 CodeGraph。读实际本仓源码，不以 ServerS4A21 或旧单体结构取证。

### 2. 精确源白名单和边界

以下路径相对 workdir；只在所述职责内修改，不以列入白名单授权旁支重构。

| # | 路径 | 允许改动 |
|---:|---|---|
| 1 | `Server/src/Dfo.Server.Application/Commands/ICommandWorkUnits.cs` | 独立封闭结果、新具名入口、当前 unit 直接 await 观察能力与守卫 |
| 2 | `Server/src/Dfo.Server.Application/Commands/CommandHandlerRegistry.cs` | 通用 `SelfSettledAction` 能力与注册形状验证／冻结 |
| 3 | `Server/src/Dfo.Server.Application/Commands/CommandDispatchingExtensions.cs` | 显式注册能力参数传递；现有调用不授新能力 |
| 4 | `Server/src/Dfo.Server.Application/Commands/CommandRequestContext.cs` | registration真实能力及显式 occurrence/guard 收口，不增加ambient |
| 5 | `Server/src/Dfo.Server.Application/Commands/CommandBus.cs` | 本次 registration→request传能力；保持 drain/Close/scope/一次Fail终判 |
| 6 | `Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs` | 独立self runner、候选／完整cleanup／健康检查／单次交接与保因 |
| 7 | `Server/src/Dfo.Server.Application/Transactions/DomainTransaction.cs` | 原action真实Abort签fact、入口pin、原Rollback聚合传cause |
| 8 | `Server/src/Dfo.Server.Application/Transactions/DomainTransactionScope.cs` | 原lease对自身Tx观察/fact/真实unit的窄内部透传，Begin仍原边界 |
| 9 | `Server/src/Dfo.Server.Application/Execution/Outbox/GameplayOutbox.cs` | 原unit/owner的独立admission pin/自身guard；Close不清pin，原发送／Complete语义不改 |
| 10 | `Server/src/Dfo.Server.Application/Mail/MailboxDomainService.cs` | 原RunAsync catch传原cause至真实AbortAction；即时自结算及共享契约保持 |
| 11 | `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 六logical／五CLR唯一whole及SelfSettledAction登记，移除fine/业务Delivery注册 |
| 12 | `Server/src/Dfo.Server.Protocol/Game/Mailbox/MailboxCommands.cs` | 删除MailboxCommand及handler，只保真实需要的MailboxResult/operation形状 |
| 13 | `Server/src/Dfo.Server.Protocol/Game/Mailbox/MailboxDelivery.cs` | 删除MailboxDelivery业务类，四handler薄化；保MailboxCompleted及纯composer |
| 14 | `Server/src/Dfo.Server.Protocol/Game/Mailbox/MailboxSendWire.cs` | 仅Send handler薄化；消息、decoder、wire与encoder原字节保持 |
| 15 | `Server/src/Dfo.Server.Protocol/Game/Mailbox/GameMailboxCommand.cs`（新） | 唯一whole、非null结果、原编排／局部拒绝映射／current门／投影 |
| 16 | `Server/tests/Dfo.Server.Application.Tests/Mail/MailboxCoreIntegrationTests.cs` | 原30参数／强断言保留；追加standalone/ambient真实C2双因观察 |
| 17 | `Server/tests/Dfo.Server.Application.Tests/Mail/MailboxProtocolIntegrationTests.cs` | 原19方法／参数保留，真whole驱动及§4 stale等价加强；App专用纯driver可放文件内 |
| 18 | `Server/tests/Dfo.Server.Application.Tests/Inventory/ItemDeliveryTests.cs` | 删fine调用改同fixture真whole；保prepared/UID/core/类型扩展/原子性强断言 |
| 19 | `Server/tests/Dfo.Server.Host.Tests/GmOfficialMailHttpTests.cs` | 原HTTP/prepared/audit全保，Open/Claim改真实decoded whole；Host纯driver可放partial内 |
| 20 | `Server/tests/Dfo.Server.Host.Tests/GmMailAttachmentHttpTests.cs` | 原真实HTTP装备claim/restart驱动改whole，保持所有core/配置/幂等断言 |
| 21 | `Server/tests/Dfo.Server.Application.Tests/Mail/QuestHelperPuppetTests.cs` | 保原强用例，按§6补真实晚期mail故障原子性缺口 |
| 22 | `Server/tests/Dfo.Server.Application.Tests/Marriage/MarriageHousingTests.cs` | 保原收获/回滚用例，补第二封mail真实失败的整体回滚保护 |
| 23 | `Server/tests/Dfo.Server.Application.Tests/Marriage/MarriageWeddingCharacterizationTests.cs` | 保原成功/取消表征，补Complete/Refund晚期mail失败原子性保护 |
| 24 | `Server/tests/Dfo.Server.Application.Tests/Transactions/CommandSelfSettledWorkUnitsTests.cs`（新） | 封闭来源/能力/guard/cleanup/异步drain机制正反例与结构拒绝坏例 |
| 25 | `Server/tests/Dfo.Server.Application.Tests/Mail/MailboxProtocolCommandFlowTests.cs`（新） | 真decoder六路、原资产帧序、self/native/VC-B5/当前性/下层barrier回归 |

两canonical：`B/verification/migration-ledger.json`、`B/verification/migration-ledger.md`；只改 `message:65630`、`message:65631`、`message:65632`、`message:65670`、`message:65851`、`message:65860` 及 `command:Dfo.Server.Protocol.Game.Mailbox.MailboxCommand`。七行目标／边界／证据指向本次真实交付，其它message/command/source/protection/transition逐对象不变，MD保原行尾。本Task仅append执行Result，原计划前缀另存E精确身份；E和新task-31日志可增加驱动、快照和raw，不能写旧同名证据。

Abstractions、Persistence、Networking/Outbound、MailboxSendDomainService、MailboxWire/Ids/MailDelivered、Cera/four openings、其余簇及所有共享Support/csproj、原工具不改。新增机制类型放上述已有文件中；不为整理命名扩文件。新ICommandWorkUnits成员可提供默认明确拒绝的 `ValueTask.FromException` 实现，使既有仅实现旧入口的test adapter继续拒绝新能力；正式CommandWorkUnits显式实现，新self测试decorator必须真转发，不能默认成功/no-op。若真实实现必须触白名单外，保当前31并向主控具体报path和不可替代理由，不自扩、不另起ID。

### 3. 先把真实来源和封闭恢复做实，再与consumer同交

开始即封 `git ls-files -z` 完整tracked路径、HEAD blob/current bytes/SHA、index、原测试方法/参数/Assert调用点和行尾/BOM、25源是否存在；目前三个新源应不存在。本Task外所有tracked按nonowned完整冻结（不是只扫Server）。保存原核心与Mailbox最小源码，以及所有复制driver的来源路径/bytes/SHA和复制后SHA；E新probe可从Task30 Program/Observation/Delivery/Snapshot及Task29纯family export复制必要部分，不修改旧源或工具。先录59focused before仅在确需新增执行前控制时运行；Task30已接受before无需为满足“先红”重复造旧坏副本。

按P6固定路线落以下闭合，不重选API或抽出空壳Task：

1. `CommandHandlerRegistration`增加通用封闭能力 `SelfSettledAction`（默认None、拒未知值），冻结并随真实registration显式交给request。registry只准Protocol、所有binding非效果；self runner再要求本次 `ProtocolNonEffect`、ordinary writable、空participants、Footprint.None、真实原outbox unit。Application不识别Mailbox CLR；Protocol/测试另核唯一产品owner恰是本六logical/五CLR的GameMailboxCommand，无HTTP/Internal/Main/后簇获许可。能力不能由CMD公开set或缺参fallback取得；原三RegistrationExits不混作self能力。
2. `CommandSelfSettledWriteResult<T>` 与原 `CommandWriteResult<T>` 分离：仅机制internal构造的 Returned(value)、ActionRolledBack(originalCause)、CommitRolledBack(originalCause)。封闭基类／内部构造，调用方不能自报完成或签来源。新 `CommandSelfSettlementPropagationException` 为可由Protocol明确排除的sealed类型、构造仅机制内部，绑定opaque observation并保原cause；不是最终raw出口。原五WriteResult consumer/defaultWrite/ReadOnly及commit-only判据不改。
3. 当前 `DomainTransaction.AbortAsync`先settled再RollbackEnlisted；新增真实 `AbortActionAsync(originalCause)`沿同一原回滚核心。仅当runner为自身lease显式开启了一次观察且Tx仍Open，**进入AbortAction的同步前段先pin原GameplayOutboxUnit和owner**，早于settled、Current=null、任何await/参与者callback、EndUnit→owner.Close。无观察standalone/ambient仍真实即时Abort而不pin／无恢复许可。原Mailbox.RunAsync由catch改catch(Exception originalCause)，把原cause传入；不延迟RequireRollback，不删IsSettled/caughtCommitthrow，不让Commit/Dispose/普通Rollback/runner失败Abort签DomainActionAbort。
4. 回滚全部参与者、实际Completion/EndUnit完成后才签该Tx自己的不可业务签发fact：本Tx、opaque调用、rollback occurrence、DomainActionAbort phase、原cause、实际输出error、Completion。Rollback第二因由已有RollbackParticipantsAsync(cause)聚合保原action与所有第二cause对象；typedRollbackFailure/Uncertain不改称ConfirmedRollback。fact不保存request/job/session/mapper/许可；观察开始也不以ambient反取request。
5. 原outbox的 `open` 与独立pending pin分开：Close仍清open但保pin，BeginUnit/Register/其余普通admission在pin存在即最早拒绝，记录本屏障独立guard occurrence/error，不能先添slot或新事务再检查。屏障只unit identity/pending/自身guard，不持request/job/session/callback、新jobslot或第二outbox。Tx已settled后的Enlist/RequireRollback及当前CommandWorkUnit的RequireRollback/再次Invoke/新unit/登记等受管理拒绝也须保sticky发生；不能被caller catch后重抛旧cause洗掉。runner以显式真实unit读屏障guard合入request，不能假称无request参数的Register已自动记录request。
6. `CommandWorkUnit.InvokeSelfSettlingAsync<T>` 只包本unit一次直接领域await；runner显式绑定request/context/job/unit/lease/call，Tx只持自身opaque identity。整个delegate只直接调用原Send/Claim/Read/Delete之一，不catch/rethrow/登记/清理/Prepare/加其它业务。Read的MailView可直接返回；Delete的ValueTask可由只await该领域调用再返回一个非nullunit值的最小适配，不包其它逻辑。Open/Query不经过RunAsync，不造fact。单次观察失败只消费本call开始后已真实签发、同Tx/lease/phase/cause/实际输出且未消费fact，必须早于runner自己的失败Abort；发sealed单次交接，observer不首次pin、不解pin。
7. 新runner分别管理action交接候选、普通Returned及真正Commit span候选。只在真实完整ConfirmedRollback、非cancel/typed、原request/job/authority健康、无Failure/GuardFailure/独立occurrence时暂列ActionRolledBack；不二次Commit/另开Tx。无fact普通throw后来被runner Abort完整回滚也不能变self来源。CommitRolledBack仍只真实CommitAsync窄span、KnownStorage真实新fact/FallibleCommit同cause、原健康与cleanup条件，Prepare手抛/SQL1811不能冒native787。
8. 返回值先留局部候选，覆盖unit.Close、request.EndUnit、lease.Dispose及结束健康检查后才实际返回；从action至全部cleanup的Task才是tracked operation。仅runner在健康候选的全cleanup和屏障guard检查通过后解pin，允许原单元外拒绝ACK；有独立错误不为恢复清pin/guard。结束失败保原cause与该次独立发生，使用已有request `FailedIndependent(error, source)` 的发生身份，不用RefEquals/Includes去掉第二阶段同对象。普通同栈传播不虚增第二次。
9. Bus保 body→drain所有已启动operation→Close request→真实DI scope cleanup→一次FailAsync→job outbox结束。不得提前成功／Fail终判／flush、撤消息、清sticky、复活request、给self raw出口或Compose。`DomainTransactionScope.Current`继续对settled返回null；原两个ambient（现存过渡机制另有后继责任）不新增AsyncLocal/current请求或locator。

### 4. whole、非null输出与原强测试的真实驱动

新 `GameMailboxCommand(NetworkDispatchContext Dispatch, IClientNetworkMessage Message)` 以有限五CLR family绑定六logical；输入只有精确Dispatch和原decoded Message，`Frame`为只读派生属性，按五种真实消息的 `Metadata` switch 构造 `ProtocolFrameMetadata.Game`，完整Metadata与Message同源，不接受独立Frame参数或外部set。未知消息类型明确拒绝，错误binding由原registry/bus拒绝。五handler实现 `IProtocolCommandMessageHandler<GameMailboxCommand>`，只把decoded消息/精确Dispatch交bus并await；不在GameplayJob内再次dispatch，不注入原Delivery/sender/domain。ProtocolModule six路线与whole capability同次接线，删除旧MailboxCommand/handler/注册/生产caller和MailboxDelivery业务类；pure MailboxCompleted、result模型和composer继续可用。

whole有实际输出职责的非null `GameMailboxOutcome`，包含非null `MailboxResult Result`、本次真实Reply（无session静默时可null）、实际 `MailFailure? Refusal` 与 `Exception? Failure`。这些是业务结果与诊断，不是签发回滚的证明；非失败成功分支仍返回真实Inbox/Assets/Recipient供原调用方观察。可用具名factory保证静默结果不误作ACK、Refusal只源于真实MailException，ordinary异常保原对象；不能为了原Assert.Throws在测试侧伪造throw或新增转发旧fine的代理类。

先按原Delivery做 FindByConnection/IsInWorld/online存在门，无session/不在世界/无online原静默。unsupported Action（非0/2）原早拒绝21、无事务；Action0映Delete、2映Read，Claim按原MailboxIds.DecodeAttachment，Query null仍正常提交后21，不RequireRollback。每个有效分支一个ordinary/None/空participants写单元：

| 分支 | action内业务和原阶段 | 单元结束后 |
|---|---|---|
| single/multi Send | 原精确current守卫→直接observed sending.SendAsync→同事务assets.PrepareAsync→同事务当前GoldAsync | 原Send ACK→updates→cleared→gold/pets |
| Claim | 原current守卫→直接observed mailbox.ClaimAsync→同事务assets.PrepareAsync | 原assets→Claim ACK |
| Read/Delete | 原current守卫→直接observed mailbox.ReadAsync/DeleteAsync | 原Action ACK |
| Open | 原current守卫→query.ReadStorageAsync→原MailboxProjection.Map，均原事务内 | ACK→inbox |
| Query | 原current守卫→FindRecipientAsync和原self拒绝 | 原Query ACK |

**守卫和业务try严格分开。** 原fine的 `sessions.IsInWorld(s)` 与 `users.TryGetByConnection(s.ConnectionId)`／OnlineUserId归属守卫仍在unit action开头、业务拒绝catch之外，失败真实抛NotOwned走default failure；不能先RequireRollback→Returned普通拒绝后靠post-check重造第二NotOwned。窄业务try只包原domain调用及成功后的原事务资产prepare/gold；排除交接、取消及typed，只有无self fact且Tx仍Open的原MailException/InvalidData/普通动作错误才unit.RequireRollback(cause)返回局部拒绝。RequireRollback自己的守卫在catch体内，不能被同一catch吞。投影/encoder/outbox登记、runner守卫均在业务try外。ActionRolledBack／CommitRolledBack／普通局部拒绝要映ACK前，whole用原领域真源再核精确session代次、FindByConnection、IsInWorld、OnlineUserId与原Dispatch物理reference；失效不可普通ACK恢复。沿旧两组错误码、信息/错误日志及wire回复构造，不把通用机制下沉成Mailbox码。成功用ICommandProjections、错误用IGameCommandOutbox；保MailCreated LiveOnly另路，不直接在whole发布事实。

原四fine caller的具体改法：

- App `MailboxProtocolIntegrationTests.Command`（原436）及ItemDelivery原258改用该fixture Services/Connection的真实wire decoder后新whole bus；返回 `outcome.Result`。App文件内的test-only driver编码Open空body、Claim u32、Action u32/u16、Query bounded UTF8、Send实际single/multi body（首个multi不写type、single无附件用原decoder允许的0/0哨兵），用真实metadata/rawbody/counter，不发明wire字段。原可表达handler测试改真实decoder/dispatcher；需读取outcome的少数调用可decoded→同provider whole bus，明确这部分不是dispatcher证据，§5独立driver负责完整dispatcher链。
- 原测试大段 `TestGameplay.RunAsync` 内直调handler会使新bus成为nested。仅将真实whole/dispatcher调用移到executor外；Seed/真实domain setup/直接事务断言留各自TestGameplay，不把整个请求包TestGameplay、RunDetached或第二queue/provider。原资产参数、测试方法和日期/skip不变。纯领域无法wire表达的强输入只保test-only真实domain＋原Tx接缝，标证明范围，不保legacy转发或给生产增加test hook。
- App旧343～345的两个保护分开：仍健康的角色2领取角色1邮件，保原拒绝非零并加强为实际ACK21、gold100000/claimed0、健康无新close；原old selection进入fine的NotOwned强保护改为**真实状态在lookup与unit守卫间变旧**。fixture配置装饰原ISelectedSessionDomainService（原descriptor同provider创建，不复制状态）；只在armed请求首次IsInWorld读出true后，用原service.Select/EnterWorld发布替换selection，再返回这个已取得的true，让whole持旧session抵达unit外置current守卫。下次真实IsInWorld旧值为false，真实guard抛同一NotOwned。优先完整保 `Assert.ThrowsAsync<MailException>`/Reason.NotOwned/claimed0，并补原cause同对象、未Commit、无普通ACK、来源close与replacement状态仍在；不在test driver捕获outcome后伪throw。若实际存在已批准明确阶段的typed包装，只能列原cause/phase和独立原因逐条映射，不能一概允许任意wrapper。单纯传旧session到被删API或lookup直接null都不证明此保护。
- Host `GmOfficialMailHttpTests` 原292/313、`GmMailAttachmentHttpTests` 原91改为同host真实published session/physicalcontext的decoded新whole，输出直接读Result.Inbox/Assets；partial内纯helper共用，StartAsync现有configureServices可装同provider结果观察器。真实HTTP preview/send/replay、第二目标prepared失败、receipt/audit、storedUID/core、27312属性、avatar/creature扩展、configured jewelry claim/restart及原encoder解析断言保持。这里不拿SQL预置或另provider取代HTTP/领取。原pure composer分片编码断言可保，与新whole实际投影分开标注。

方法/参数保留与任何必要assert改写分账：原保护→新观察、具体行/理由/前后Assert调用点、case数量和实际执行结果逐项写E `strong-coverage.json`；不把“只改驱动”笼统覆盖stale增强。模型/encoder纯测试可留；不为维持被删内部API的字面形状削弱协议和资产保护。

### 5. 真实新链、来源攻击与mutation

E新probe项目 `probe/Task31.MailboxProbe.csproj` 可沿Task30真实App.Tests引用/friend名和窄反射fixture，明确其AssemblyName与正式Networking.Tests并非同DLL。原5份必要源复制到E后改新whole观察，不跑旧fine项目。每轮freeze→Release build→加载Location/MVID/SHA→run UTC/argv/exit同档，prod DLL copy等当前build输出；改driver保新round，不覆盖旧raw。

新probe先证明六logical（Action读/删分别真目标）真实registered decoder→同provider dispatcher→GameMailboxCommand→真实Domain/SQLite→原registration encoder/adapter/outbox→production OutboundSender＋受控内存transport。恢复**原第一GameplayOutboxes production factory**，同provider解析原GameplayCloseCauses；原RecordedOutbound最后factory缺causes，不能据空链猜无链。只在probe装记录转发和transport，不替换注册/encoder来源、不手动Record链、不新provider/outbox/Host。所有success先合法可发、同fixture真实设备UID/core＋stack/gold、mail/attachments/claimed、原bytes及事件顺序；记录registration bytes=actual transport bytes/offexecutor/Delivered，不把Discard或closeentry当physical close。拒绝控制含无session、unsupported早21/noTx、Querynull commit/21、健康别角色claim、普通pre-RunAsync错误RequireRollback。

必须直接取得以下组并与14before比较，矩阵可分独立fixture但不得静态猜覆盖：

1. 晚期Send真实trigger RAISE(ABORT)19/1811、Claim真实GoldLimit：证明资产/claimed已暂存→Abort入口TxOpen同步pin→settled/Currentnull→各Rollback/End/Close仍pin→真实Completion/fact→外层observer单次交接→runner全cleanup→ActionRolledBack/原ACK3或10；原cause同对象及同Tx/lease/call/occurrence可见，fact在runner失败Abort之前，Commit未调用/无第二事务。去trigger后的合法新发送只一次转移，不叫Main/重放。
2. 真native Commit：原mail INSERT触发DEFERRABLE FK暂存child，真实连接FK1/child>0、action/receipt成功返回后SqliteTransaction.Commit19/787，KnownStorage/FallibleCommit新来源、cause同对象、ConfirmedRollback/全列回滚、CommitRolledBack仅cleanup后、原ACK。controlledPrepare/ABORT不能替它。
3. 两场CH-1下层窗口：同provider领域proxy内部catch/finally在inner真实Abort完成、Currentnull/fact存在、outer Invoke observer尚未接错时，分别尝试同一原outbox普通Register和同一DomainTransactionScope.Begin→SQLite真实写→Commit。Register最早拒绝且不添slot；Begin最早BeginUnit拒绝，后续SQL/Commit计数0、无第二durable。proxy捕获真实guard后仍重抛同一原cause；屏障guard独立occurrence须存活，不被fact/throw覆盖，runner拒绝恢复、保两发生和默认sourceclose。再覆盖whole已接交接后catch继续写/Register/RequireRollback/再Invoke/新unit/成功return的受管理边界。
4. 来源与能力拒绝：无fact普通throw后runner Abort；旧/foreignTx/lease/call/fact、错cause/phase/重复消费；action成功后Prepare或组装再抛同cause；错误route/无registration能力、Main/非空participants/NonRequest footprint/ReadOnly；closed/foreignrequest、错job、已cancel/OCE、typed/Unknown/Open/Committed、既存sticky/GuardFailure；未awaitoperation/早return/drain。actual fact后故意触发unit/request/current/registration守卫、catch后重抛旧cause，验证真实边界先记独立发生，而非只凭未处理guard或RefEquals。
5. unit.Close、lease.Dispose、whole真实DI scope各独立注错；至少一例同一cause对象、至少一例同一交接对象被独立cleanup再次抛，保阶段/occurrence及原cause，禁止返回健康结果。scope资源必须真由scope解析/Dispose，不body手抛/手动Dispose冒清理；总bus未完成前观察不得成功。机制测试可用实际原lease/反射窄seam，不能为注错增production测试hook。

必须做两个有行为价值的可编译 mutation（可复用一个反例但义务分别实证）：(a) 将真实pin延后至outer observer或使owner.Close清pin，CH-1至少一路实际越过admission并使拒绝断言RED；(b) 去掉一项真实source/phase/owner或独立cleanup资格检查，使相应反例错误恢复而RED。逐mutation保存原source bytes/SHA、变更、build成功、实际fail断言/原命令退出；随后**exact bytes恢复**并重build跑同oracle GREEN。不能compile缺API、无效fixture、改expected冒RED；最后正式freeze不得含mutant。

### 6. VC-B5、共享caller和全资产

C1真实SQLite durable之后nonfallible违约仍Uncertain，C2真实SQL cause1＋controlled Rollback cause2最终typedRollbackFailure/Uncertain且两个原对象可达，C3真实whole scope错误（success及normal-refusal两控制）均不补普通失败ACK/不健康结束。记录scope故障前已登记消息和真实unit状态，按原机制存活和顺序实际发送后Close原物理SourceConnection/原chain；不能从旧slots=[]推新零帧。直接证明空selected、无新增actor/Main/footprint/Fence/DiscardExact；受控结算路径零Discard只限此路径，正常disconnect/teardown照旧，不冒真实全close链零调用。

新source与replacement须真不同physicalreference/不同session，含复用connectionId控制与无关玩家/队伍；只关原source，不Fence/Discard替换者或误关其它连接。原GameplayCloseCauses同provider链/actualClose证明，healthsuccess/refusal/confirmedRollback无新close，失败后健康控制用合法下一请求。未运行真实disconnect child则明确保静态边界，不冒TCP/A21或生产故障。

共享RunAsync因源码改变逐caller保原原子性和即时结算：

- Core原DatabaseFailureAndCaughtRefusalCannotCommitPartialAssets及partial所有参数保；新增standalone和ambient真实SQL＋Rollback第二因，最终图直达两原对象／typedUncertain／Currentnull，ambient catch后原Commit必throw，无网络observer/pin/恢复许可。
- ItemDelivery原溢出/MailOnly prepared与前置inventory、UID/core保。GM原 `MidBatchStorageFailureRollsBackEntireBatchAndSeparatelyRecordsFailure` 五trigger（含第二收件人、attachment/equipment/receipt/audit）真HTTP新跑，前目标/receipt/整批Succeeded audit同撤；独立失败audit原语义保持。
- QuestHelper原MarkerFailure/MailFailure/RolloverFailure强用例新跑，撤旧delivery/替换/日标记同撤和IsSettled分支保持；若原用例已直接覆盖不重复加镜像测试。
- Housing原Water_limit_harvest_two_mails...保，新增真实第二目标mail INSERT失败，核RecordHarvest、第一封/第二封、tree growth/round及原持久状态全撤。
- Wedding原Complete/Cancel表征保，新增真实后段mail失败控制，分别核双方position/礼物/礼金/宾客回礼，及applicant/guests退款没有部分持久化；通过原领域/事务，不以另provider或手造结果替代。随机奖励按fixture真规则及全状态关系核，不扩大随机归一。
- MailCreated/mail/delivery同事务、Reliable业务/完成标记同事务及mail.arrival LiveOnly消费提交→外部投影先后用原测试和新实际记录保；不前移成协议ACK，不给HTTP新恢复。每caller报告实际覆盖和未测，静态共享影响不称全故障端到端。

同run注错DDL后取baseline，全部110生产user表/全列（Task30为644列，实际schema新核，不盲抄数字），按schema/row identity/SQLite值类型逐值比较，含NULL和BLOB，self/native/C2回滚全部原样相等，FK注错另2表独立列。跨run如需before/after等价仅沿已证具名CURRENT_TIMESTAMP、唯一`marriage_housing_migrations(version=1).applied_at`的UTCms来源/类型/真实run界/同case稳定，以及GUID关系双射作窄验证；保raw，不strip时间/GUID/任意数值，不把跨run许可用于同run回滚。

### 7. 逐命令、正式数量账和结构检查

在E新建只负责记录的 `run.py`（Task30原driver来源SHA保存），命令形态：`python3 <E>/run.py task-31-<label>-<round> <argv...>`；每标签唯一，stdout/stderr完整落B新logs，保存argv/cwd/HEAD/UTC start-end/exit/resources/sourcefreeze/DFO目录和native自然结束。dotnet/probe正式运行使用require_escalated以保持FSEvents可用，不等sandbox watcher挂住；保原类并行、`--blame-hang-timeout 5m`、Skip/期限，允许项目 `-m:1`。不能因本机失败串行程序集/调超时求绿。失败先按AGENTS身份规则诊断，原raw保留；全量失败exit1只允许下述两确切基线。

开发检查按依赖顺序：

```text
dotnet build Server/86JP.Server.slnx -c Release --no-incremental
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~MailboxCoreIntegrationTests|FullyQualifiedName~MailboxProtocolIntegrationTests|FullyQualifiedName~MailboxProtocolCommandFlowTests|FullyQualifiedName~CommandSelfSettledWorkUnitsTests" --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~MailboxProtocolTests" --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~GmOfficialMailHttpTests" --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet build <E>/probe/Task31.MailboxProbe.csproj -c Release --no-incremental
dotnet run --project <E>/probe/Task31.MailboxProbe.csproj -c Release --no-build -- <E>/after-<round> <explicit-case-list>
```

case-list由上述已固定矩阵逐项枚举入新driver/run metadata，不接受默认少跑success冒全部；每项case/state/cause/资产/transport断言写checks清单。原59focused的Core30/ProtocolIntegration19/codec10单独数量闭合，新case另列，不能合并成一个没身份的总pass。Host partial GmMailAttachment属于同一GmOfficialMailHttpTests类，新focused须包含其实参。共享caller另外focused按真实类名 `ItemDeliveryTests|QuestHelperPuppetTests|MarriageHousingTests|MarriageWeddingCharacterizationTests`，原五consumer/默认Write/ReadOnly/三raw可在开发按对应类focused，最终八full完整包含，原owner/phase不抽样。

mutation全部恢复、最后产品/测试/driver冻结后，必须新鲜执行下列九条；不复用Task29的任何full结果冒fresh：

```text
dotnet build Server/86JP.Server.slnx -c Release --no-incremental
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.HttpApi.Tests/Dfo.Server.HttpApi.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Pvf.Tests/Dfo.Pvf.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

正式build 0warning/0error/exit0。历史库存9029＝9009pass＋2fail＋18skip，仅Task29 selected full起算；当前新总数=9029＋N−D，按每项目 `(display,status,multiplicity)` Counter列加法，Theory multiplicity/改名/状态漂移分别解释，不拿Assert数或行数充case。原失败仅 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，原形状 `Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'.`；新失败不能混此基线。原18skip无新Skip，Char10全部pass/0skip，Golden15逐bytes等HEAD/原已接受f01dc226。

每full独立DFO路径，最终bundle每assembly一个完整fresh来源，不拼行/混round，含原七actual control，Pvf无control。调用未改原 `../../verification/probe-tools/violations.py`（相对B）实际CLI：

```text
python3 .rks-agent/tasks/serial-gameplay-executor/verification/probe-tools/violations.py <E>/formal-final-bundle --assemblies Dfo.Server.Application.Tests,Dfo.Server.Protocol.Tests,Dfo.Server.Networking.Tests,Dfo.Server.Host.Tests,Dfo.Server.HttpApi.Tests,Dfo.Server.Infrastructure.Tests,Dfo.Server.Persistence.Tests Dfo.Server.Application.Tests:CommandBusExecutorTests=1 Dfo.Server.Application.Tests:EffectOperationReplayTests=1 Dfo.Server.Application.Tests:GameplayOutboxTests=3 Dfo.Server.Application.Tests:GameplayQueueLeakTests=1 Dfo.Server.Application.Tests:GameplayWriteAssertionTests=53 Dfo.Server.Application.Tests:NotificationDeliveryTests=1 Dfo.Server.Application.Tests:ProtocolCommandExecutionTests=1 Dfo.Server.Networking.Tests:OutboundRealSocketTests=1 Dfo.Server.Networking.Tests:OutboxRegistrationTests=5
```

原九expected类67＋七control＝74/7不放宽。新测试若确有intentional违规，先在E按新真实类/精确数独立列，与原67区分；CLI只追加该新expected，不改九原值或合并旧类、不忽略outside/orphan/exe。新矩阵优先在合法job中构造guard，不为凑负例制造执行器外写；probe零记录不冒七control证明，exe0只观测边界。

新source/runtime/purefamily capture：

```text
python3 <B>/verification/probe-tools/migration-ledger.py capture --repo /Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021 --output <E>/capture-after-<round>
python3 <B>/verification/probe-tools/migration-ledger.py preclose --current-task .rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd/tasks/TASK-31.md --output <E>/ledger-preclose.json
python3 <B>/verification/probe-tools/migration-ledger.py validate --output <E>/ledger-strict.json
python3 <B>/verification/probe-tools/migration-ledger.py selftest --output <E>/ledger-eight-bad.json
```

capture前/后和纯family export用原方法，只复制driver到E并加新能力导出，禁止改原cli/expected；完整registration差异应仅六message routes/fine→whole及具名self能力，173routes/201commands基数从实际capture核（预计37Protocol＋1Internal＋163Legacy、135Tx/53Direct/9RO/4Workflow、53Protocol消息/120Legacy消息、14telemetry；不是手算代capture）。原三raw所有owner/phase/actualencoder/source/cleanup集合前后完全相同，原五WriteResult consumer不新增/漂移；仅self owner六logical/五CLR唯一whole。新结构坏例能拒绝缺能力、未知/错误route/效果binding、自授、额外caller、Mailbox外AbortAction产品callsite、handler业务/nestedbus/sender/locator、公开fact构造/当前请求ambient；实际IL/source调用检查避免只grep同名字。Compose生产禁用/F-132和F-131披露保持。

仅在以上已有可复核证据后闭七行；先用现有Task的精确路径运行preclose，实际验证当前 `completion_results_pending` 只含本Task的pending Result，不能传裸TASK-31。全部产品／验收checks通过、证据齐后才append真实Result二级标题和 `Outcome: done`；随后将strict validate与八负例selftest作为Result自身postwrite完整性检查实际运行，通过后才封存并交审。原工具只核completionTask具有Result标题文字及done Outcome，不要求review或commit，不得把这两项检查误留主控或称工具需approval。本计划不预置该标题字面串，以免原工具的substring检查误把计划当Result。Result初写不能预记strict通过；实际通过后补录命令/结果、重核最终Result／Task与manifest身份，准确记录该顺序。postwrite若失败，保raw并如实修正本Task交付状态，不预记成功或伪造review/commit，不篡工具。非ownedpending不挪不关，Rental两Main/独立refresh、T-004残余和T006～012照旧。

### 8. 完整性交付和停止点

E保存before/after源码freeze、DLL身份、原命令raw/exit、矩阵与cause图/occurrence、all-table原始快照及比较、真实帧和close-chain、strong-coverage、mutation RED/exactrestore/GREEN、八fresh inventory/DFO provenance、注册差异、七canonical id diff和证据指针。报告区分动态证明、静态caller影响和未测；本次受控transport不是TCP/A21、套件/probe时间不是单CMD≤2秒性能验收，最终T012仍需冻结测量/全量。

末尾重新 `git ls-files -z`，完整nonowned每项bytes/SHA=current=before=HEAD，不能漏中文路径/历史Task/原工具/Golden/Spec/Plan。owned现存源/新源/仅七canonical对象及原行尾核，Git NUL实际changed/untracked精确等本白名单＋Task＋E/logs manifest；manifest记录全路径/bytes/SHA，manifest自身排hash、Task绑定原精确pre-Result prefix以排循环，停止writer后再外报完整Task/manifest身份。新authored逐项检查 blank-at-eol、blank-at-eof、space-before-tab、cr-at-eol，原CRLF/raw bytes留来源不泛用例外。记录所有native自然退出，无probe/testhost残留，不强杀别任务。

只有所需产品／验证／有界七行证据交付齐后，producer在本Task append Result（`Outcome: done`只表示本单元执行完成待required review/主控检查点）；列改动/为什么/实际检查/相对旧断言变化/计数/风险和未测，不能提前写Result、通过review或整体Spec完成。再按§7实际完成strict／八负例selftest的Result自身postwrite完整性检查，补录真实结果及最终身份后停止全部writer/native，交fresh independent reviewer／主控。若有实质gap，保同一31及已有raw，按实际边界报主控，不擅改上游/另建Task；producer不自审不stage/commit不规划next。

## Result R3 — 同 TASK-31/r1，R2-F1 rework

Outcome: done

新源技术交付完成，待另一fresh independent Result复审及主控消费／检查点，未committed；不表示producer自行批准R1/R2 changes_required或整个Spec002完成。本轮只修主控接受的R2-F1正常同栈传播虚增occurrence，保R1-F1已解决边界，不改B5/P6/原计划前缀/授权/25源/七canonical，不新Task或stage/commit/remote/version/tag/deploy/游戏/清理。原R2目标63951B SHA fda2fbda1bf7a3b7eea79ee120192759851a38f54a3c34ff19bf985d9d89bffb、含review71953B SHA ab105c01f69cd5ddf14290d6a66bec0e320edd509002a4733eb9d0466579fa97、含disposition74276B SHA a84fda1f6bedcbbc6a6093f22ad7f0ecd7d73a9cdb4b4504b68cd6ecfdf22caf已全bytes封存于 `verification/evidence/task-31-inspection/rework-r3/`。全部R1/R2 review、Controller原文及历史R2 postwrite段继续保留在本Task下方；各旧目标由snapshot/hash绑定，不用旧review批准新R3。原41665B计划前缀精确SHA2ebcc61aae524489a016023670741310fd354f7ef1ee65c0c33ba1b918b2ab89不变。

R2-F1的真实C2现有case已加强永久断言：最终error必须ReferenceEquals原actionfact.Output且直接是RollbackFailure typed；原SQL1811和真实独立rollback故障都同引用可达；只读捕获实际whole context.Request的failure list，恰一条Source同引用fact.Occurrence、Error同引用fact.Output。test-only CapturedWhole只记录真实request并转发，不手造错误/fact/occurrence/事务或provider。R2产品core未改时red-build01零警告零错误，原四real SQL场3pass/仅rollback1fail/exit1，实际Assert.Same原typed与重复Aggregate不同对象，不是fixture/编译RED。详见rework-r3/r2-f1-red-green.json及task-31-r3-c2-occurrence-red原logs/freeze/DLL/argv/UTC。

最小修正只识已确认本层正常传播：qualifyingHandoff成立、own actionFact已识别、实际error同引用其Output。此自身signed Output重抛不再先Failed(error)造普通source，仍drain所有已创建observation，随后按原fact.Occurrence登记一次；同predicate统一两处传播判断。未修改CommandRequestContext独立发生语义，未全局按Exception引用去重；独立guard/unit/lease/scope cleanup保各自source，completed plain fault和真正outer独立错误继续drain保因。R1-F1的所有已创建observation在Commit/Returned前裁决、pending不早返回、proper-await显式同cause且真实RollbackOnly/request健康/无RejectedRollbackCause的局部拒绝保持；typed/OCE/sticky/GuardFailure不擦除。

GREEN build0/0、原两相关类41case全部通过，含加强C2、completed两时序、pending TCS、proper-await局部拒绝、samecause独立guard。R2→R3只有CommandWorkUnits与既有新MailboxProtocolCommandFlowTests两路径变动，其余23产品/测试路径精确R2；ICommandWorkUnits的R1-F1实现及原WriteResult/Write/ReadOnly入口/RunAsync尾段精确R2。无新增case，只增强既有case，不更原方法/参数/attrs或弱化断言。

41GREEN后同候选源上两cleanup及两mutation完整一次执行：unit.Close实际抛原同handoff、lease.Dispose实际完成后抛同SQL对象，每场严格恰2个不同request source、含原fact.Occurrence，110表完整回滚、无ACK、actual原sourceclose/chain；exactrestore后action/GoldLimit GREEN。这些是临时真实方法source-fault，非自然生产故障，无永久hook/伪Completion/私有已提交状态篡改。owner.Close清pin可编译后两个永久target及真实lower-Begin write1/Commit1 RED，finally exactrestore重build同target及Register/Begin真双窗GREEN；仅新self移除FallibleCommit phase可编译使Prepare错恢复“No exception was thrown”RED，exactrestore重build同oracle GREEN。新一轮无E错误纠正，所有native自然退出，见final-controlled-boundaries四complete records。

其后才把精确恢复的candidate封为新正式25路径 `rework-r3/product-freeze-final-01/manifest.json`，SHA a69d15bb318644d225f8c4f9cd671cc765a72adc80575a579a08a2bc71c1a519；candidate/final bytes相同，freeze-qualification记录顺序。最终runtime/driver sourcefreeze SHA5c286a9fcd31eed8097cfc3e96c270bea3749ac3c114cc062a31f3ea20a316b7，正式full开始后未改这些冻结源/driver。

正式Release --no-incremental 0warning/0error，新八fresh full自然结束：App5298=5280pass+原exactDimension2fail+16skip；Protocol1778、Networking199、Persistence484、Host203、HttpApi57、Pvf80均pass，Infrastructure976=974pass+2skip。总9075=9055pass+原2fail+18skip，9029起算N46/D0，R2→R3逐display/status/multiplicity全部同集、N0/D0/无rename或旧状态漂移，App实际exit1和两原maze7100(1,0)Left失败保真。原59身份在本次full全pass，原8文件101method/参数/attrs/Assert强度文件bytes精确R1/R2。原DFO新r3-formal-dfo-check01 exit0，九expected67+七control74/7、outside/exe/orphan0，七真实basename原bytes/SHA provenance；未改tool/expected，Golden15固定HEAD精确、Char10新Networking全部通过。

新final-matrix-01显式24场、completed-windows-01三mechanism控制、check-final与compare-before-after实际全部通过。real first production OutboxesFactory/sameproviderCauses、decoder/dispatcher/whole/domain/SQLite/encoder/adapter/outbox/原OutboundSender+controlled physicaltransport重新取得；110生产表644列schema/stableidentity/SQLite类型/NULL-BLOB全部保存，native另2表3列112/647，8故障回滚same-run逐列精确。早rollback callback仍TxCompletionOpen/Currentnull/factnull而pin=true，完成后签fact单交接；native真实actionreturn/FK1/child暂存后Commit787、KnownStorage/FallibleCommit同cause。single3/multi4与原业务帧顺序保持；C1/C2默认失败无普通失败ACK，C3真DI scope在Committed/原投影已登记后成功四帧或normalrefusal21先真实送后只关原source/chain，无补failureACK。lower最早双admission拒绝、后续write0/Commit0、wrong-output/吞cause保因默认失败；replacement复用/更换id两场真不同physical且只关原source，替代/无关玩家留活及替代下一query成功。querynull正常Commit21、pre-RunAsync普通RollbackOnly拒绝7/noactionfact、无session零Tx/ACK保持；受控路径0Fence/Block/Discard不冒全真实disconnect链0Discard。

决定性R3 C2 raw：fact.Output3/Occurrence44103698，最终DispatchError3直接原typed RollbackFailure，RequestFailures仅Source44103698/Error3，guard0，actualsourceclose1；原SQL1811与独立Rollback IOException同对象可达。对照R2 Output3却Aggregate5.Inner[3,3]/两个source，R3已恢复单原signed occurrence，见c2-occurrence-transition。观测exception图仍可含正常Handoff节点/引用，不把它当第二个request failure。真实后续unit/lease独立cleanup则仍各2个不同source，允许相同error/handoff引用，证明非全局ref去重；samecause真实guard亦由41及lower窗口保。

三个completed控制仍真实同provider/domain/SQLite/firstfactory/sender，unit-action-return真TxOpen/mail1/无actionfact且inner已faulted；两个未接纳fault原cause一次、110/644精确回滚、late runnerAbort不造action/commitfact、零ACK、actual原physicalclose/原rootchain1；proper-await显式同cause真实RollbackOnly则零sticky/零close、实际拒绝ACK3。它们是bus/managed-unit test command，不冒额外Mailbox生产decoder/owner；实际Mailboxpre-RunAsync ACK7另由24场证明。14 accepted Task30 freeze07/build07/final03的完整资产/Metadata/frame对照也重新通过，只沿4具名CURRENT_TIMESTAMP、唯一housingv1 UTCms来源/类型/run界/同case稳定和domain-event3GUID双射/deliveryFK窄校验，same-run rollback不归一。

六Mailbox/fiveCLR唯一whole、五薄handler、原Frame只读Metadata同源/非空真实Outcome、删除fine/业务Delivery、真实Abort入口早pin/Close保pin/全cleanup恢复和共享C2双因原交付保持。currentguard业务catch外、真实stale NotOwned/claimed0/无ACK无Commit/原sourceclose与replacement、健康other-character claim21，以及原四fine测试caller的HTTP/UIDcore/资产/原入参强断言均在新full覆盖；共享Core standalone/ambient C2、ItemDelivery/GM五HTTPtrigger/QuestHelper/Housing/Wedding原子性/LiveOnly由新full和实际矩阵保，专项临时source-fault不冒自然故障。无新Main/nonRequest actor/额外self资格/足迹/raw/ambient/Compose。

新原capture173/201、purefamily和structure01全部通过：37Protocol/1Internal/163Legacy，135Tx/53Direct/9RO/4Workflow，消息53Protocol/120Legacy/14telemetry，三raw种类/18全部owner/exits不变；self唯一whole，actualIL self/Abort/factcreator各自唯一产品owner，全部既有10WriteResult集合保持、13compiled坏例全拒。四opening+Cera具名五子集与额外既有五owner完整验证的计数勘误已获R1/R2独立认可，无新上游gate；历史JSON字段original_five_write_result_calls实际数组10不裁剪。只有同七canonical对象/七既有MD行更新R3证据，其它original/对象不变。

原preclose精确Task路径exit0/pending[]只反映旧Result/done字面仍存在、工具不识review/round，不能当R3review放行。写本Result前完整NUL15737/nonowned15713 current=before=固定HEAD与25源freeze已再核；5077个R1/R2非owned旧证据逐SHA保持，所有原目标/manifest/raw不覆盖。报告/Counter/全列/因图/physical/发生精度/边界和strongcoverage在rework-r3。新R3 Result在以上技术和七对象交付后才实际更新；strict/八self新postwrite尚不在本段预记，实际运行后只补录真实结果再封manifest停止writer/native。

局限：受控内存physicaltransport非TCP/A21，临时cleanup source-fault非自然故障，suite/probe walltime非单CMD≤2秒。正常disconnect child未新专项probe，Char10与静态原链保持；F131/F132现状、T004残余、Rental两Main/refresh、T006～012/Astra/Spec终审/主控checkpoint仍后继。

## Independent Quick Review R1 — TASK-31 Result，2026-10-25，fresh result reviewer

```yaml
kind: rks-review
verdict: changes_required
```

### 目标、范围与所读判据

本轮是 producer 之外 fresh independent reviewer 的 Task Unit RESULT quick review，只评本 Task r1 的当前本地实现及真实 Result/postwrite；不是 Task-plan gate、P6 重批或整个 Spec B 的交付批准。唯一 workdir `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，branch `task/refactor-session-20261021`，HEAD `db4e17c4ad4a2ee6fb69658bb3e890fbfca8ec1c`，均已只读核实；无 `.codegraph/`，按仓库要求跳过。

评审目标为追加本段前完整 Task **52378B / SHA256 `17c14c52151a67090a67f12ea539132eb142fdd2d5b54e1d36a13cbb7bfba621`**，含 `## Result` 与实际 postwrite 结果；原计划前缀 **41665B / `2ebcc61aae524489a016023670741310fd354f7ef1ee65c0c33ba1b918b2ab89`**。owned 产品／测试目标是 `product-freeze-final-01/manifest.json` 的25路径，manifest SHA256 `f8f8c4c2f27795456d51001c7a4f715e3339a556da1a520236d088e5c3bf0fa9`；全交付 `manifest-final.json` **1201050B / `5771c1b8d8f81a79f64764b7b2e4c3a247f85ba4ab2bc3d60728386e10bc5b92` / 3249 entries**。实际逐文件重新读 bytes/SHA，3249项均匹配（Task按manifest约定只绑定原计划前缀），不是只信 producer 的完整性报告。

读取 `/Users/puxianguo/.agents/skills/reviewing-work/SKILL.md` 及其 `references/task-unit.md`、`code.md`、`deliverable.md`，按“本地明显违约／回归、证据支持及混合交付一致性”判定。读取本 Task contract/详细计划/Result，B5 Mailbox专节及直接关联 RQ/AC/VC-B5、P6 self runner/source/pin/cleanup/whole 边界、CH-1、R2独立接受和 `spec-b5-plan-p6-controller-consumption.md`。当前 Spec B5 **53754B / `92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790`**、Plan P6 ready R2 **93207B / `1ec1dd1fcaa842e033e8b773fc5ed6fd0563199e8cbe3752fd68ac1e0a8c5691`** 已重核身份。读取实际 owned 核心机制、Mailbox whole/五handler/registration、共享 RunAsync 和相关测试 diff；producer 报告只用于找证据。

### 阻断发现 R1-F1：已完成 faulted 的 inner observation 可被丢弃后继续提交

**严重性 P1，违反本 Task §5(4) 未awaitoperation／早return／drain 义务及 P6“pending／未await／吞错不能成功结束”的明确边界。** `ICommandWorkUnits.cs:37-39` 将 inner `ObserveAsync` 保存为 `observation`，但 `HasUnfinishedObservation` 在 `:73` 只判断 `IsCompleted:false`。`CommandWorkUnits.cs:70-79` 仅在该布尔值为 true 时排空 inner operation；若 inner 已完成 faulted，该分支被跳过。与此同时，`ICommandWorkUnits.cs:45-57` 对“无 actionfact、lease仍 Open”的普通异常只重抛进这个 inner Task，不记 request failure，这是为了允许外层合法业务 catch 明确 `RequireRollback(cause)`，本身不能简单一律改sticky。

由当前代码可确定的触发是：合法 self-capability ordinary unit 中，调用 `unit.InvokeSelfSettlingAsync<int>(_ => ValueTask.FromException<int>(cause), ct)` 后丢弃其 ValueTask，outer action 正常返回，且**没有**任何合法业务 catch／RequireRollback 接纳这个故障。`ObserveAsync` 同步完成 faulted，Tx仍 Open，无fact／handoff／sticky；`HasUnfinishedObservation=false`，于是 `request.DemandCurrent()` 通过，runner 在 `CommandWorkUnits.cs:90` 实际进入 `lease.CommitAsync`，`:101-102` 记 committed 并返回 Returned。若该原事务已有暂存 SQL（例如原领域成功暂存之后、下层代理在返回前抛一个无fact普通错误），相同路径会将暂存写提交。CommandBus 排空的是已成功结束的 outer work-unit operation，inner observation 没有单独登记到 request operations，不能在末端补获这个异常。其结果是丢失原 cause、错误地成功结束，并可持久化失败操作之前的部分写。

这是对**当前源码分支的直接静态裁定**，本 reviewer 遵守 append-only 边界，没有新增／运行临时 probe 或测试，不冒称已取得新的动态 RED。当前生产 `GameMailboxCommand` 的各 observed call 有 await；本发现针对本 Task 明确承担且已增加负例的 managed work-unit／inner-drain 契约，不把当前六条正常消息冒称已触发此误用。

现有 `MailboxProtocolCommandFlowTests.An_unawaited_direct_domain_observation_is_drained_before_abort_and_never_commits_staged_SQL`（`:118-131`，driver `:141-146`，proxy `:242-243`）用 TCS 保持 inner 尚 pending，故只证明 `HasUnfinishedObservation=true` 的分支，不能拒绝本次 completed-fault 分支。`A_plain_throw_followed_by_runner_abort_has_no_action_recovery_source` 则正常 await 抛错；两者均未覆盖本触发。

**最小充分修正要求：** 在任何 Commit／Returned 之前，确保本 unit 启动的 observation 的已完成故障也经实际裁决；未等待且未被原合法局部回滚路径接纳的 fault 必须保原cause并失败、回滚原边界，不能凭 `IsCompleted` 放行。不能将 Mailbox 原合法 awaited pre-RunAsync 普通错误→catch→`RequireRollback(cause)`→健康拒绝改成一律sticky。补有实际暂存 SQL 的 completed-fault 负例（同步完成及返回前已完成的时序控制），先在本目标观察错误成功／Commit 的 RED，再修复为无durable／无ACK／保cause／原source默认关闭的 GREEN；同时保 pending+TCS 和 proper-await／合法回滚拒绝控制。修正后更新本 Task Result/冻结/受影响证据并交 fresh/current 独立复审，由主控决定所需重验范围。此处不替 producer 选 API 或扩大上游范围。

### 已核对的决定性证据与计数勘误裁定

- 实际源中 `AbortActionAsync` 在 Tx仍Open时同步 pin 原unit/owner，随后才 settled 与 rollback；Close只清open不清独立pin。读取实际 `final-matrix-01/action.json`、`commit.json`、`lower-begin.json`、`rollback.json`、`scope.json`、`replacement-reuse.json`：action早期callback pin=true／Currentnull／CompletionOpen／factnull，完成后同Tx/call/cause fact；native Commit是独立 KnownStorage/FallibleCommit 787 而非actionfact；lower Begin拒绝后原Tx确认回滚且关闭来源；C2 typed Uncertain；scope在Committed之后默认关闭。它们支持各自边界，不支持 R1-F1 未测分支。
- 读取真实 `final-boundaries.py`、`run.py` 和关键原 logs/metadata。Close-clearpin mutation 实际编译后 `red-real-window` exit134，raw明确 `writes=1; commits=1`；exactrestore 后双lower测试 GREEN。移除FallibleCommit限定实际 RED“未抛异常”，恢复后 GREEN。unit／lease source-fault确为受控真实方法改写及exactrestore，无永久hook，不将其称自然生产故障。
- 正式build原raw为0warning／0error，八full库存账9072=9052pass+原exactDimension2fail+18skip，+43/D0；App原exit1保留。读完整性、DFO provenance／原CLI尾部74/7及outside/exe/orphan0、strict/eightself实际postwrite metadata及输出。strict与八负例的postwrite顺序和exit0支持其记载；这些检查不取代 required Result review，也不能排除本轮代码发现。未重跑full/native。
- **“原五WriteResult consumer”→实际10的事实勘误本身不构成此次material gate扩大。** 独立读取 `original-write-result-owners.json`，并对全部10个实际源逐一重新 `git show HEAD:path` 和当前bytes/SHA比较，均精确相同；读原WriteResult runner diff，其既有分支未改变。P6具名四opening+Cera五子集仍保持，另外五个既有owner也被纳入完整不漂移核验；新self-capability实际只授Mailbox whole，不是给这10个增加新资格。故在本地quick-review范围，Result据实际收严完整集合属于计数事实纠正，不因字面“5”另造WHAT/HOW变更或要求改上游。但这不影响 R1-F1 的独立阻断，也不替主控批准后簇能力。

受控physical transport非TCP/A21，临时source-fault非自然故障，套件／probe时间非单CMD≤2秒；这些已披露局限继续后继。本轮只有上述一个已示明blocking finding，verdict不表示其余Spec已全量批准。reviewer只追加本段，未改产品／测试／Result／计划前缀／canonical／manifest／raw，未新建文件、stage、commit、remote或启动native；追加后停止writer，交主控消费。

## Controller Disposition R1 — task-rework

主控完整读取 fresh Independent Quick Review R1，接受其唯一阻断 R1-F1：inner observation 已完成 faulted、未被等待且无合法局部回滚接纳时，当前只按未完成状态排空会漏错并继续 Commit。代码分支明确支持该裁定，不能以原65focused／正式9072及pending-TCS测试已通过替代这个遗漏分支。当前不释放 Task commit；本 Task 未 committed done。

Disposition: task-rework，同一 TASK-31 r1 既定未await／drain义务内修正，不改变 B5/P6、授权、25源／七canonical／证据前缀边界或中心方法，不创建新 Task、无需默认 Task-plan review。producer先保原R1目标／review／manifest／raw快照，再用真实暂存 SQLite 覆盖同步完成fault及外层返回前已完成fault，两者在原冻结源观察实际RED后最小修正；保原cause／无durable／无ACK／source默认关闭，同时保 pending+TCS、proper await 与原合法 catch→RequireRollback(cause) 健康拒绝。不能为补排空给原合法拒绝一律sticky，不能伪造 Completion、另事务或永久测试hook。

共享core发生新修改，须本轮新freeze、Release --no-incremental零警告零错误与八fresh full，原两确切Dimension失败和18skip、原并行/期限/DFO/Golden/Char10及数量加法保持。最终24矩阵/受影响来源与cleanup反例/两个mutation在新冻结源准确重验或证明受影响证据边界；本Task已明确最终源mutation和source-fault责任，按新source封存实际RED/exactrestore/GREEN，不能仅引用R1源SHA冒本轮。新round标签／证据路径唯一，不覆盖旧R1 raw/freeze/manifest。更新本未提交Result为实际R2，保持此R1 review和本裁定原文；执行后再实际postwrite strict/八负例/完整NUL/manifest，停止writer/native，交另一fresh independent Result复审。既有review不批新目标。

主控亦接受 R1 对“五→十既有WriteResult owner”事实勘误的独立裁定：全10源码与固定HEAD相等、旧WriteResult分支未变、Mailbox唯一新self能力，属于完整既有集合验证纠正，不扩大WHAT/HOW或consumer资格。该项不是R1-F1的替代处置。

仅本地rework；仍禁止stage/commit（由主控门后做）、push/PR/merge/remote/version/tag/deploy/游戏/清理。若修正需要白名单外文件、改变上游接受边界或中心方法，先停止全部writer/native并报主控，不自行扩大。

### R2 Result postwrite 实际结果与停止协议

R2 Result在全部新源技术验收、七对象更新及原preclose之后实际写入。随后2026-10-25T01:51:58Z原 `migration-ledger.py validate --output .../rework-r2/ledger-strict.json` 实际exit0/strict/valid/无pending；01:51:59Z原 `selftest --output .../rework-r2/ledger-eight-bad.json` 实际exit0，八类坏数据全部rejected。对应新日志 `task-31-r2-ledger-strict-postwrite-01` 和 `task-31-r2-ledger-eight-selftest-postwrite-01`，未改工具、expected或伪记review。原preclose的pending[]来自当时旧R1 Result/done字面存在、工具不识review/round，不能拿它当R2独立批准。

本段为两项实际通过后才补录。最终 `rework-r2/final-integrity.json`、完整current/before/HEAD NUL与15713 nonowned逐SHA、四类authoredwhitespace、R1review/disposition原bytes、R1raw保留、仅七canonical检查以及 `rework-r2/manifest-final.json` 将封存本次交付；manifest排自身、Task只绑定原41665B精确计划前缀，完整最终Task身份记rework-r2/final-stop.json并在writer停止后的外报给出。native全部自然结束后只读确认无测试/probe残留，不清其它进程/服务。封存后停止全部writer/native交主控派另一fresh required Result复审，不stage/commit。


## Independent Quick Review R2 — TASK-31 Result R2，2026-10-25，fresh result re-review

```yaml
kind: rks-review
verdict: changes_required
```

### 目标、范围与判据

本 reviewer 独立于 producer 与 R1 reviewer，只复审 R1 未解决 finding R1-F1 及本次修正的直接回归；不是完整 Spec 批准、Task-plan gate 或重新选择 HOW。唯一 workdir `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，branch `task/refactor-session-20261021`、HEAD `db4e17c4ad4a2ee6fb69658bb3e890fbfca8ec1c` 已实际核实，无 `.codegraph/`。

追加本段前完整 Task **63951B / SHA256 `fda2fbda1bf7a3b7eea79ee120192759851a38f54a3c34ff19bf985d9d89bffb`**，原计划前缀 **41665B / `2ebcc61aae524489a016023670741310fd354f7ef1ee65c0c33ba1b918b2ab89`**。R2全交付 `rework-r2/manifest-final.json` **1930259B / `5fde410e1577d16242d5866c9539476646bd0155081d7b2d9752ae13b9c36b99` / 5105 entries**：逐项独立读取bytes/SHA，全部匹配（Task依manifest约定只绑定原计划前缀）。25源freeze manifest **`800de4463e61d6d1b575630a3975b3eb8e13ed0a53df66ce1c3c03e20517950c`** 的每个当前源也逐项匹配；runtime/driver sourcefreeze为 **`5c440133f56c8427c222456a23f4175b42db3b552f8512f98e875a66e2f82c1d`**。R1→R2产品清单只有两个core（`Commands/ICommandWorkUnits.cs`、`Transactions/CommandWorkUnits.cs`）及既有新 `MailboxProtocolCommandFlowTests.cs` 改变，其余22源身份精确R1。实际读取两core对R1源快照的完整差异与相关测试/driver，未将producer报告代替源码及raw。

读取 `/Users/puxianguo/.agents/skills/reviewing-work/SKILL.md`、`references/task-unit.md`、`code.md`、`deliverable.md`，适用本地明显违约/直接回归及证据一致性判据。读取本Task contract、§3/§5和R2 Result/postwrite、R1 review及Controller Disposition；读取B5 Mailbox与P6来源/发生身份/失败收口相关段、CH-1及既有独立裁定供限定范围。当前Spec **53754B / `92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790`**、Plan **93207B / `1ec1dd1fcaa842e033e8b773fc5ed6fd0563199e8cbe3752fd68ac1e0a8c5691`** 已核身份；未重开既有“五→十owner”计数事实裁定。

### R1-F1 已解决：completed fault 不再漏排空后提交

当前 `CommandWorkUnits.cs:78-80` 在无候选时无条件drain已创建observation，然后才DemandCurrent/Commit；`ICommandWorkUnits.cs:82-85` 仅在request无失败、原transaction真实RollbackOnly、observed error与本unit RollbackCause同引用且无RejectedRollbackCause时接纳原局部拒绝。它没有把普通plain错误一律sticky；原pending-TCS分支仍先记早返回失败、等待inner结束再abort。

独立读取 `task-31-r2-completed-fault-red-{build,test}-01` 原日志、argv/UTC/exit及sourcefreeze。RED build0warning/0error，24个产品源精确R1，唯一不同是新增反例测试；两个真实same-provider原Mailbox Send先暂存mail1、TxOpen/无actionfact，再由下层proxy抛同plain cause。同步完成与outer返回前完成两个case均实际 `completion=Committed; stagedMails=1; durableChanged=True`、2fail/exit1，故不是编译/fixture失败冒RED。新GREEN build0/0，`task-31-r2-completed-fault-green-test-01` 两相关类41case/exit0，含两个completed、pending-TCS、proper-await显式同cause回滚及原source/guard。

另外直接解析 `rework-r2/completed-windows-01/{completed-sync,completed-before-return,completed-accepted}.json` 与原physical日志：三场均真实stagedMail1/inner已faulted，110表Before/After逐对象精确相等。两个未接纳fault的DispatchError与Cause都是同一ID1、仅一个RequestFailure、ConfirmedRollback、ActionRollback/CommitFailure均null、实际零帧且原physical CloseAsync1/offexecutor/原source rootchain；proper-await同cause RequireRollback场真实RollbackOnly、AcceptedCauseSame=true、RequestFailures空、原拒绝ACK3实际17B帧、零close。该探针是同provider真bus/managed unit控制，不冒称新增生产Mailbox decoder路径。由此R1-F1原漏错及健康局部拒绝回归已充分解决。

### 阻断 R2-F1（P2）：新 outer catch 把自身 handoff 的正常传播重复记作独立发生

**这是R1-F1修正的直接回归，违反本Task §3(8)“普通同栈传播不虚增第二次”、P6“发生身份不能由同cause替代／正常同栈传播不冒第二次”，以及B5 Mailbox表（spec.md:112）的相同边界。** 新 `CommandWorkUnits.cs:111-114` 将catch条件从HasUnfinishedObservation扩成HasObservation，凡存在已完成observation就先 `request.Failed(error)`，随后drain。对真实C2 action rollback失败，inner observer已消费原fact并发自身Handoff；runner内层在 `:65-67` 因typed/Uncertain不能恢复而重抛原fact.Output。此时没有新的guard/cleanup发生，但新`:113`先给该Output造了一个普通failure occurrence；`:123`又用原actionFact.Occurrence对同一个Output调用FailedIndependent。Drain忽略自身Handoff只避免它自身被再记，不能撤销前面的重复登记。`CommandRequestContext.cs:139-143`按source身份接受两条，最终合成包含同一个typed对象两次的Aggregate。

实际证据已在producer本轮原raw中，无需reviewer越写界补probe：

- R1 `verification/evidence/task-31-inspection/final-matrix-01/rollback.json`：真实action fact `Output=3`、`Occurrence=32585784`、CompletionUncertain、Consumed=true；RequestFailures只有 `{Source:32585784,Error:3}`，最终 `DispatchError=3`，是原 `TransactionOutcomeUncertainException(RollbackFailure)`，其内部保action SQL1811与独立Rollback IOException。
- 同场R2 `rework-r2/final-matrix-01/rollback.json`：action fact仍 `Output=3`、`Occurrence=26847985`、CompletionUncertain、Consumed=true；没有admission guard。RequestFailures却为 `{Source:27756887,Error:3}` 和 `{Source:26847985,Error:3}`。最终 `DispatchError=5`，Exceptions中ID5为 `AggregateException("Independent command failures...")`、`Inner=[3,3]`；ID3才是原typed RollbackFailure，其内部ID6 `Inner=[1,2]` 精确保留SQL1811与Rollback IOException。两个source不是两个真实新失败，前者仅为当前外层重抛的同一Output人为造出的发生。该场仍零ACK/原source Close1/SQL回滚，不能因此消除已示明的发生身份和最终异常语义回归。

此缺陷把一项已签来源的正常传播改成“两个独立错误”，也使Result中“own单次handoff传播不当独立第二次”的声明缺乏当前实现支持。现有41pass及矩阵checker只核两原cause可达/失败关闭，未拒绝这一重复source/`Inner=[3,3]`，不能用它们放行本回归。

**最小充分要求：** 在继续排空所有已创建observation、保completed plain fault和outer独立错误的同时，使同一已签action fact的自身handoff/原Output正常传播只登记其真实occurrence一次；真实后续guard、unit/lease/scope cleanup再次抛同对象仍必须保留独立发生，不能靠异常引用全局去重。用当前真实C2场证明最终仍原typed Output与单action occurrence，保两真实cause，并保本轮completed两负例、proper-await局部拒绝、pending-TCS及原同cause/handoff独立cleanup控制。更新受影响Result/冻结/实际证据后交fresh/current独立复审。此review不修改实现或指定替代API，不要求重开上游gate。

本轮仅该一个新的blocking finding，R1-F1本身判已解决。未改部分沿R1已核证据引用；R2正式库存/边界记录只作受影响核对，未冒称本review已重新批准整Spec或完成全部验收。已披露的受控内存transport非TCP/A21、source-fault非自然故障、suite/probe时间非单CMD≤2秒及后继任务边界继续保留。reviewer仅追加本段；未写产品/测试/Result/上游/canonical/manifest/raw，未新建文件、stage/commit/remote或启动native/broad rerun。追加后停止writer，交主控消费。

## Controller Disposition R2 — task-rework R3

主控完整消费 fresh Independent Quick Review R2，接受R1-F1已充分解决，同时接受其唯一新阻断R2-F1。主控另独立读取R1/R2同场rollback原raw：R1 action Output3只有原fact occurrence一条并直接抛typed3；R2同Output3有两个不同Source，最终Aggregate5.Inner=[3,3]，无真实新增guard或cleanup。新outercatch按HasObservation先Failed(error)，后又按fact.Occurrence记同传播，是修正引入的发生身份回归；技术全量通过不能替代该明确契约。

Disposition: task-rework，同TASK-31/r1、既定§3(8)正常同栈传播不虚增发生及§5排空/保因义务内修正；不改上游B5/P6/计划前缀/授权/25源及七canonical边界、不新建Task、不重开默认Task-plan gate。保持R1-F1 completed fault提交前裁决、proper-await显式同cause真实RollbackOnly健康拒绝，以及普通outer独立错误保因；只让自身真实已签fact的合法handoff/原Output传播归于原occurrence一次，不按全局Exception引用消除真实第二次guard/cleanup错误。

producer先保存本R2目标/含review/本disposition完整快照，保所有R1/R2 raw/freeze/manifest不覆盖。用当前真实C2 SQL1811＋Rollback第二因案例增强永久断言：原typed Output与单action occurrence、两真实cause均保持，在当前R2产品取得行为RED后最小修正；与completed两时序/proper-await拒绝/pending-TCS及真实同cause/handoff独立guard/cleanup控制同时GREEN，再冻结开始正式验证。新sharedcore改动依既定Task仍须新Release零警告零错误、八freshfull及数量/DFO/Golden/Char10；新final24中加强发生精度，两个最终源mutation与unit/lease source-fault按新源准确绑定并exactrestore/GREEN，新增round/路径唯一。

更新同一未提交Result为R3；下方全部R1/R2 review与主控裁定原文保留，旧目标由明确snapshot/hash绑定。新Result后实际postwrite strict/八负例/完整NUL/nonowned/whitespace/新manifest，停止writer/native，交另一fresh independent Result复审；R2审查不是新目标批准。仍不stage/commit/push/PR/merge/remote/version/tag/deploy/游戏/清理。若真正需要白名单外文件、上游接受或中心方法变化，先停止自然退出并返主控。

### R3 Result postwrite 实际结果与停止协议

新R3 Result在全部新源技术验收、发生精度及七canonical交付后实际更新；随后2026-10-25T02:14:59Z原 `migration-ledger.py validate --output .../rework-r3/ledger-strict.json` 实际exit0/strict/valid/无pending，02:14:59～02:15:00Z原 `selftest --output .../rework-r3/ledger-eight-bad.json` 实际exit0、八类坏数据全部rejected。原tool/expected未改，两新postwrite日志为task-31-r3-ledger-strict-postwrite-01与task-31-r3-ledger-eight-selftest-postwrite-01。preclose先前pending[]只是历史Result/done字面存在且工具不识round/review，未当新独立批准。

本段只在两项真实通过后补录；原R1/R2 review与Controller全文原bytes仍在上方，历史R2 postwrite不冒新轮。最终完整NUL15737/15713nonowned=current=before=固定HEAD、25源freeze、only-seven、原目标/5077旧证据SHA、四类authoredwhitespace与actualGit路径将由rework-r3/final-integrity.json等文件封存；新manifest-final.json排自身，Task只绑定41665B原计划前缀，完整最终Task身份由rework-r3/final-stop.json与停止后的外报给出以免循环。所有native自然结束后只读确认测试/probe无残留，不清其它服务或进程；manifest写完只读复核，再停止writer/native交主控派另一fresh required Result复审，未stage/commit。


## Independent Quick Review R3 — TASK-31 Result R3，2026-10-25，fresh result re-review

```yaml
kind: rks-review
verdict: approved
```

### 目标、范围与实际判据

本 reviewer 独立于 producer、R1/R2 reviewer，仅复审尚未解决的 R2-F1 与修正直接回归，保持 R1-F1 的已解决裁定；不是 Spec 全量批准、Task-plan gate 或替主控推进检查点。实际 workdir `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`、branch `task/refactor-session-20261021`、HEAD `db4e17c4ad4a2ee6fb69658bb3e890fbfca8ec1c` 已只读核实，无 `.codegraph/`，因此未使用 CodeGraph。

追加前 Task **75638B / SHA256 `a373e7006d4f5bcd785044abbff09d2969ae46cfc78dd095974858ffd6dd7f2b`**；原计划前缀 **41665B / `2ebcc61aae524489a016023670741310fd354f7ef1ee65c0c33ba1b918b2ab89`**。R3 `rework-r3/manifest-final.json` **2659453B / `f64b5c516d5fba53c62ba6d8a380479787366f5b9426f42bc8b34fbea394f57b` / 6961项** 已逐项独立读取实际 bytes/SHA，全部匹配（Task仅依既定约定绑定原计划前缀）。25个当前源逐项匹配产品freeze **`a69d15bb318644d225f8c4f9cd671cc765a72adc80575a579a08a2bc71c1a519`**；正式build／最终matrix／completed物理控制的runtime/driver freeze均为 **`5c286a9fcd31eed8097cfc3e96c270bea3749ac3c114cc062a31f3ea20a316b7`**，并逐项读取这些capture的实际源核SHA。RED及GREEN capture亦已核源，而非仅接受source_unchanged报告字段。

读取 `reviewing-work/SKILL.md`、`references/task-unit.md`、`code.md`、`deliverable.md`，依本地明显缺陷、直接回归和报告／实际交付一致性裁定；读取本Task契约、§3(8)、§5排空／发生身份义务、当前Result R3及postwrite、全部R1/R2 review与Controller dispositions，以及直接关联 B5 `spec.md:112-113`、P6 `plan.md:77-87` 的来源／正常传播／独立发生边界。Spec **53754B / `92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790`**、Plan **93207B / `1ec1dd1fcaa842e033e8b773fc5ed6fd0563199e8cbe3752fd68ac1e0a8c5691`** 身份匹配。R1/R2原review及主控文字与对应旧目标snapshot逐字保留；未改证据按前两轮引用，未重开已裁定的“五→十既有owner”事实勘误。

### R2-F1 已解决，未发现新的局部阻断

独立比较R2快照与当前完整差异：只有 `Transactions/CommandWorkUnits.cs` 与既有新 `MailboxProtocolCommandFlowTests.cs` 两路径改变，其余23个产品／测试路径精确R2。当前runner `CommandWorkUnits.cs:111-126` 只有在本层已资格校验的自身handoff、已有actionFact且当前error就是该fact.Output时，跳过普通 `Failed(error)`；仍排空所有已创建observation，随后以原 `fact.Occurrence` 记录一次。它没有改变 `FailedIndependent` 的发生身份语义，也没有全局按Exception引用消除错误。R1-F1的Commit前无条件drain、pending等待、显式同cause／真实RollbackOnly／request健康／无RejectedRollbackCause的局部拒绝接纳保持不变；旧WriteResult／Write／ReadOnly尾段也精确R2。

永久测试 `MailboxProtocolCommandFlowTests.cs:76-86` 直接断言最终error与真实fact.Output同对象、直接typed RollbackFailure、原SQL1811及真实独立Rollback cause同对象可达，并读取实际request.failures，要求恰一条且Source/Error分别就是原fact.Occurrence/Output。新增test-only `CapturedWhole` 只记录context.Request并转发原whole，无替代业务或伪造异常。

实际读取 `task-31-r3-c2-occurrence-red-{build,test}-01` 的argv／UTC／exit与原log，RED capture除增强测试外24源精确R2。build为0warning/0error；原4场中3pass，真实rollback场在 `Assert.Same` 因原typed对重复Aggregate失败，exit1，故不是编译／fixture失败冒RED。GREEN build0/0及原两相关类41case/exit0，原log包含强化C2、completed两时序、pending-TCS、proper-await局部回滚及既有samecause guard。

独立读取三轮原 `final-matrix-01/rollback.json`：R1为Output3／单签发source；R2为Output3但两个source、最终Aggregate5.Inner=[3,3]；R3恢复 **fact.Output3／Occurrence44103698、DispatchError3直接原typed RollbackFailure、RequestFailures仅 `{Source:44103698,Error:3}`**。R3异常图typed3→aggregate5→原SQL1811(1)与Rollback IOException(2)，两cause保留；Completion仍Uncertain、guard0、110表全列Before/After精确相等、无ACK、原物理来源实际CloseAsync1且保原root chain。未把SQLite实际已回滚冒称ConfirmedRollback或普通拒绝。

直接回归证据亦读取实际raw与注错源差异：`controlled-source-faults-final/unit-cleanup/actual/unit-cleanup.json` 为原fact occurrence40803818/error1与独立source63258058/error2（本unit同Handoff）；lease对应raw为相同两个不同source而Error均1（Dispose再抛原Output）。两场均110表精确回滚、无ACK、实际原source关闭一次；两变体精确恢复后GREEN。这证明正常传播去重没有吞掉后续同对象独立清理。新lower-register／lower-begin raw各保guard与原fact两不同source、第二SQL/Commit均0。`completed-windows-01`三场均真实暂存mail1且inner已faulted、110表精确恢复；两个未接纳fault以原cause失败、无fact／无ACK／实际Close1，合法显式同cause拒绝则request.failures空、ACK3实际17B、Close0。R1-F1及合法局部拒绝边界继续成立。

### 受影响验证和交付边界

实际核正式Release --no-incremental原log0warning/0error；八fresh full的原log／exit／同源freeze一致：总 **9075=9055pass+原2fail+18skip**，App exit1保留两个原DimensionDungeon失败；其余7项目自然结束通过。R2→R3库存N0/D0、原59身份、DFO74/7与Golden15／Char10沿本轮原记录及前两轮未改证据核对，没有把baseline失败涂成全绿。实际读Close清pin mutation原log的RED `writes=1; commits=1`／exit134、恢复双窗口GREEN，及fallible-phase mutation的真实“未抛异常”RED／恢复GREEN；均绑定本轮源，未因修正而复用旧源结论。

postwrite两项原metadata及输出证明strict valid／pending[]、八坏例全部rejected，时间在本轮Result写后；不把它当独立批准。完整性记录的NUL15737／nonowned15713、仅七canonical、旧5077证据保留及四类whitespace，连同当前所有实际manifest文件SHA一致，支持未扩写本轮边界。受控transport仍非TCP/A21；临时source-fault不是自然生产故障；suite/probe时间不是最终单CMD≤2秒测量，T012后继不在本裁定内。

本轮没有未解决blocking finding，**R2-F1判已充分解决，R1-F1保持解决**。approved仅覆盖本次TASK-31 Result R3的限定复审，不表示整个Spec完成，也不替主控消费／提交。本reviewer只追加本段，未改产品、测试、Result、上游、canonical、manifest或raw，未新建文件、stage/commit/remote，也未启动native或broad rerun；追加核身份后停止全部writer并交主控。

## Controller Consumption R3 — approved，释放本地Task检查点

主控已完整读取 fresh Independent Quick Review R3 并核对其当前目标75638B／a373e7006d4f5bcd785044abbff09d2969ae46cfc78dd095974858ffd6dd7f2b、25源a69d15bb318644d225f8c4f9cd671cc765a72adc80575a579a08a2bc71c1a519、6961项R3 manifest f64b5c516d5fba53c62ba6d8a380479787366f5b9426f42bc8b34fbea394f57b。接受其approved：R2-F1已充分解决，R1-F1保持解决，无新局部阻断；旧R1/R2 changes_required由对应明确rework/当前复审闭合，不被删改或倒签。

主控另直接核验R3所有6961项bytes/SHA及Task原41665B前缀绑定，实际Git NUL23 tracked changed＋6938 untracked精确等28具名文件及证据/logs前缀，index原空；15737 tracked／15713 nonowned全bytes=current=before=固定HEAD，59 active authored四类whitespace均清。旧review/disposition尾部与R2保存原目标逐bytes相等。当前产品源25项精确freeze；完整读R3 Result、修正core对R2差异、增强永久C2断言和真正RED raw；新source只有本轮说明的有界修改，旧WriteResult/Write/ReadOnly及原consumer不变。

主控独立解析本轮八formal raw与exit：9075=9055pass+原exactDimension2fail+18skip，R2→R3 N0/D0，9029起算N46/D0；原build0/0。读取原checker及本轮变化、执行无写验证前缀核新24场实际raw，全部列／帧／原因／早pin／物理来源及替代隔离通过；另直接解析C2原图，Output3与唯一Occurrence44103698、最终directtyped3吻合，110表回滚、零ACK、actualClose1与原chain。三个completed控制保持原cause/完整回滚和合法拒绝；独立unit/lease cleanup各恰2不同source含原fact，原cause/handoff同对象再次抛出仍独立。原DFO CLI本轮只读复核exit0，74/7及outside/exe/orphan0。没有重复原native全量或把受控transport、source-fault、suite时间冒TCP/A21／自然故障／2秒CMD验收。

接受技术checks、postwrite strict/八self、canonical仅七对象和独立Result门，释放本Task本地checkpoint；实际commit与干净状态由随后Git核验给出，不预填未知commit哈希。本次仍只完成Mailbox单元，Spec B保持active。后续继续T004残余与T005租赁，再按既有T006～012全责任推进；需要调查旧失败出口或扩大明确能力时仍依受影响门，不借Mailbox许可。Astra审核与用户明确分支收尾前，不push/PR/merge/远端、版本/tag/release/deploy或清理。
