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
