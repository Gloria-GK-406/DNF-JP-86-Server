# TASK-15 — 四 opening 完整协议与 commit-only 确认回滚结果

## Task Unit Contract

- Revision: 1（本契约与详细计划的同一修订）
- Outcome and references: 交付[Plan P3](../plan.md) T-004 下一有用单元：PackageOpening、AvatarPackageOpening、MagicBoxSingleOpening、MagicBoxBatchOpening四whole吸收原fine/handler，删除四fine；以真实消费者验证P3 §4的commit-only写结果能力。先旧route取得action SQL和真实SQLite Commit完整协议oracle，再迁新route，保拒绝/回滚响应、主结算/重放、资产/冷却/事件及帧字节。落实[P3 review](../reviews/plan-p3-review.md) CH-1与[controller consumption](../verification/plan-p3-controller-consumption.md)的真实guard来源、业务E与清理X保因。覆盖Spec B2 RQ-001/003～009/012/014/015、AC-001～005/007～009及F-34/F-88/F-93/F-112；不新增VC/raw出口或泛化Mailbox恢复。
- Owned boundary: §2/§6精确生产/测试/AGENTS、本Task与task-15新证据；ledger两文件仅四message/四fine精确ID。Spec/P3/契约/routing/root/A、旧Task/Result/reviews/证据、游戏业务/规则/DTO/codec/composer/持久schema及其余来源只读。共享改动仅既有unit结果、明确transaction/participant失败来源、所列真实guard降权和带cause正常回滚，不造ambient/globaltracker/通用action恢复/任意Func命令平台。
- Required checks: **产品前阶段门**先在旧route实跑同provider真实action SQL及真正SQLite fallible Commit后的ACK/日志/cause/entry/资产/冷却/事件/nofence/source/retry oracle；新route同oracle重跑，真实custody/绑定/volatile版本/facet守卫排除恢复、已知storage vs unknown来源、业务E＋独立rollback/cleanup X（含同对象两次发生）、默认Write/ReadOnly sticky、取消/不确定/Committed/guard/drain/两B2 raw等区分。至少一个有价值可编译mutation。完整Release build0/0＋八full、原DFO74+7/Golden15/Characterization10、逐case8644＋N－D、capture/strict八坏副本。旧测试Throws/SQL/字节目的不得因whole转失败响应丢失。
- Dependencies: TASK-14 r1/R1/required独立approved产品检查点`0d5df6a4288a1b34751054d59a527eaa55ca8b74`；P3 fresh gate及主控amendment-only检查点`f930f26cc681982daf1f7663867c7ef7e653f877`（parent同前），Server tree与产品parent相等。P3 SHA `778a28e67377d099d0d0cc43739e5c3083566c5895cb3292f2b47e2625909519`，amendment `36f8f21fbada4c6e2a04a1a4a0d86f56486f62962f4e280a736664c939531e05`，review `3d346d1cad4003c3b9d306abdad54a5a18e96562f45821cf630c61fac5f7ba7f`、routing `8423e6e6bfb2d8f65285891ae968d20e3da49fc529432f120d0abfcd497fd786`均已approved/匹配；Task11～14显式Main/footprint/replay/finitefamily/NonRequest不重做。
- Result review: required — 此单元改变共享异常出口，误判会把guard或unknown变正常失败ACK、丢业务/清理cause、误重试已提交奖励或使旧回滚响应断线；codec与总数量不能识别。独立结果快审核真实旧/新oracle、来源/完成状态/保因及同版本八full后交主控检查点。Task-plan仅主控readiness，无额外独立审批。

## Detailed Plan

### 1. 起点、非目标与产品前阶段门

开工由主控确认上述HEAD、worktree `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`、`task/refactor-session-20261021`、唯一写者和owned状态；读AGENTS、两架构偏好、B2/P3及匹配review/CH-1/controller-consumption、Task14结果。无`.codegraph/`则rg优先。中文、不push/PR/merge/远端issue/部署/cleanup/改版本、不运行df_game_r/DNF.exe或起游戏、不创建Task16；共享发现超白名单/否定P3时先交主控实际source，不默扩恢复span/Spec。

当前延续库存8644＝8624pass＋2原Dimensionfail＋18skip：最新App4886、Protocol1766、Net199，另Host203/Http57/Infra976/Persistence477/Pvf80合1793仅Task12库存，本轮将八full重新实测。capture173logical/203cmd＝18Protocol＋1Internal＋184Legacy，156Tx/34Direct/9RO/4Workflow；pending139msg/184fine/72source/5protection/8transition。

**Phase 0（生产仍精确未改）：** 只先增本Task的测试/线级driver及证据，在原四LegacyQueued/Transactional fine上运行§5两条最小完整oracle。保存实际旧生产源SHA、raw body/selector/type/seq/ordinal/hash、代码/命令/完整meta和结果；不先实现新API、改route或迁四fine再倒称before。旧业务测试原action trigger仍跑，另需真正native Commit。达到ACK/ConfirmedRollback/同entry原Ordinal retry及真实资产等区分后才Phase 1产品写；若实际不符合P3假设或探针无法达到，保诊断并报主控具体source，不放宽oracle或拿孤立provider替代。

before只精确owned原bytes/BOM/行尾/混合byte，新增/删除单列，非owned tracked仅SHA；禁止整仓/旧归档/几千before/bin/obj快照。每输出独立prefix，不用--output覆盖旧证据。旧route本轮新增oracle的源码原样存最小必要版本，和迁后版本作明确对应，不制造归档研究副本。

### 2. 精确生产／文档白名单（19个物理路径）

以下相对worktree，只有表列职责；新类型可同列文件内收敛，不能另添文件/整目录。Application不识别Protocol类型，P3中心方案不变。

| 路径 | 允许职责 |
|---|---|
| 新`Server/src/Dfo.Server.Abstractions/Transactions/TransactionFailureSources.cs` | 只读participant失败事实契约/封闭类别与真实phase；§3的Guard/KnownStorage/Unknown及原cause/实例/发生身份，不含请求、协议、恢复许可或可变全局容器 |
| `Server/src/Dfo.Server.Abstractions/Inventory/ItemLifecycleScope.cs` | 仅现有custody/active/ValidateForCommit不变量的原异常来源事实；原判定、消息、异常对象、Prepare/Commit/Rollback顺序不变 |
| `Server/src/Dfo.Server.Application/Inventory/CharacterContainerDomainService.cs` | 仅每事务Changes实例的既有Prepare绑定版本guard事实，不改在线/绑定规则或锁 |
| `Server/src/Dfo.Server.Persistence/Inventory/RunItemDomainStore.cs` | 仅Session现有Prepare的active/custody/changed-version及未prepare生命周期guard事实；不改版本判定、存储/提交或玩法 |
| `Server/src/Dfo.Server.Persistence/Database/SqliteDatabaseParticipant.cs` | 仅本participant真实失败来源事实：facet.ValidateForCommit窄catch、已有readOnly-staged/未prepare guard、真实SQLite Prepare存储与pending.Commit调用；原Settle/事务/SQL/清理保因算法不变 |
| `Server/src/Dfo.Server.Application/Transactions/DomainTransaction.cs` | 捕获本次实际participant/Prepare或fallible Commit/原cause/事实；对已入事务guard事实的检查；保持prepare→唯一fallible→其它commit/反向rollback/Completion和原cause聚合次序 |
| `Server/src/Dfo.Server.Application/Transactions/DomainTransactionScope.cs` | lease只读转交本transaction完成/失败来源事实，原Begin/ambient/Abort/Dispose语义不变，无请求回查 |
| `Server/src/Dfo.Server.Application/Commands/ICommandWorkUnits.cs` | 新WriteResultAsync与封闭Returned<T>/CommitRolledBack结果（同文件）；CommandWorkUnit.RequireRollback(Exception cause)及本次local cause；旧Write/ReadOnly/无参RequireRollback保留 |
| `Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs` | §3/§4最小结果入口、确认回滚与全cleanup/track、带cause回滚与默认sticky；不改bus最终收口/失败许可 |
| 四原`Server/src/Dfo.Server.Protocol/Game/{PackageOpening/PackageOpeningSlice.cs,AvatarPackageOpening/AvatarPackageOpeningSlice.cs,MagicBoxSingleOpening/MagicBoxSingleOpeningSlice.cs,MagicBoxBatchOpening/MagicBoxBatchOpeningSlice.cs}` | 精确删fine/命令handler并薄化消息handler；**同文件DTO/decoder/encoder前段原bytes不动**，仅必要using随删除调整 |
| 新四`.../Game/{PackageOpening/GamePackageOpeningCommand.cs,AvatarPackageOpening/GameAvatarPackageOpeningCommand.cs,MagicBoxSingleOpening/GameMagicBoxSingleOpeningCommand.cs,MagicBoxBatchOpening/GameMagicBoxBatchOpeningCommand.cs}` | 每协议唯一whole＋handler、原强类型Input/Dispatch/结果、原业务窄catch与unit外纯mapper/日志/ACK/投影；不抽跨协议协调器 |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 删四Transactional fine，四logical改四Direct/Protocol whole/单精确CLR；只existing CommittedReplayCurrentStateBatch，decoder/encoder/通知注册不改 |
| `AGENTS.md` | 仅当前四opening opt-in/两种unit结果、默认异常守护及四fine删除事实，不写过程/扩其它未完成责任 |

生产/文档逐路径19。

只读：`Abstractions/Transactions/DomainTransactions.cs`、所有生产csproj/DI公共机制、CommandBus/CommandRequestContext/ExplicitEffectRequest/ledger/admission/LegacyLease/outbox/attempt/reader/sink/RecordedOutbound、原DomainTransaction默认completion/rollback规则；所有Opening三domain/规则/Apply/effect handlers/ItemDelivery/ItemGrant、Data、schema和其它存储/World/Login/Creature参与者。SqliteItemDomainStore.ValidateForCommit的Active/custody已由actual facet窄调用来源覆盖，毋须改该文件。若实际不能在所列文件内实现，先报主控具体最小依赖，不静默改public transaction契约或其它guard。

### 3. 已决定的共享API、真实来源及CH-1

接口落定为`ICommandWorkUnits.WriteResultAsync<T>(context,declaration,action,ct)`；返回封闭`CommandWriteResult<T>`，其`Returned<T>(Value)`/`CommitRolledBack(Cause,CommitAttempt)`构造仅runner可用（Application内部factory/private构造，外部CMD只读匹配）。Returned不叫Committed，涵盖正常RequireRollback；CommitAttempt只称CommitAsync span，不虚称已细分native阶段。旧WriteAsync/ReadOnlyAsync继续同签名、同异常sticky与原tests，不让默认调用偷偷获新恢复。

**精确来源事实，未知默认失败。** 新窄契约如`ITransactionFailureSource.LastFailure`只读，值含实际source实例、原Exception引用、具名阶段/类别与本次发生身份；实现私有写，仅在真实出错点产生。DomainTransaction在本次实际participant Prepare/fallible Commit调用前后采集并绑定事实，lease只读暴露。非本participant/非本次调用/非原cause/失配phase或无事实＝Unknown；**没有接口或LastFailure为空绝不等于已证明可恢复**。故World/Login/Creature等其它参与者不改、默认sticky关闭；不遍改全仓guard、不让CMD自报来源/已回滚。事实只可证明或收紧本次结果，不授继续执行权，无ambient/jobslot/globaltracker。

三个真实叶来源：ItemLifecycleScope的ValidateForCommit/相关active和custody guard；CharacterContainer.Changes.Prepare绑定版本guard；RunItem.Session.Prepare的scope/active及`changed && state.Version != version`真实guard。各保原异常对象原样throw，将其本实例/实际guard发生事实记为Guard；新异常不靠文字或全InvalidOperation分类。SqliteParticipant在**facet.ValidateForCommit调用的窄catch**把当前原cause作为Guard（覆盖SqliteItemDomainStore的Active/Scope.Validate），其readOnly且有staged、未prepare即Commit的现有检查也Guard；不能把整个Prepare catch标Guard，亦不能把整个Flush catch都标可恢复。

生产正来源目前只SqliteParticipant的已证SQLite存储操作：Prepare的Open/Flush物化阶段实际provider SqliteException与真实pending.CommitAsync中的provider SqliteException，可记KnownStorage，绑定当前participant、实际phase/调用及原cause；这是“实际存储位置＋provider失败”的联合来源，**不是全局按SqliteException豁免**。facet验证先分离，Flush中的普通未明InvalidOperation/程序不变量及未识别参与者为Unknown并sticky；取消/typeduncertain仍另排除。SQLite现有cleanup错误typed，不形成KnownStorage回滚结果。受控Prepare正例须用真实SQLite participant/资产和已证Flush错误，普通同类异常来自未声明参与者应反例；若发现该窄正来源会覆盖新真实guard，停报实际source，不扩大放行。

DomainTransaction需在action正常回值后的runner检查点也查看本transaction已enlist来源的Guard事实（不是只看request.Failure或commit catch），发现guard即原cause sticky外抛，哪怕whole业务catch暂存它并RequireRollback。Commit catch则同时核本次失败source/phase事实。来源事实和错误发生次数分开：custody异常沿facet/transaction传播不制造两个独立业务错误；但rollback/cleanup独立阶段即使又抛同一Exception对象，仍是独立发生，不能因ReferenceEquals/Includes或Distinct删掉。只需要本transaction/unit局部事实，不建立全局审计框架。

**CH-1真实反例如何到达：** 新unit测试在同provider/健康job中由真实IItemDomainStore创建一个未放置/未删除record，触发真正ItemLifecycleScope.Prepare；另用真实CharacterContainerDomainService与测试专用可控既有IDomainTransactionAccessor，让其捕获Changes后在无该事务的测试访问中改变同真实绑定版本，再恢复原事务进入Prepare；RunItem用两真实transaction session写同真实state，先提交一份，另一份Prepare命中version guard。此类测试source切换仅局部fixture、不进生产、不作为新CMD信任输入。SQLite facet验证另外用真实SqliteItemDomainStoreSession/真实scope的悬空custody直接到actual participant.Prepare窄点，保原cause/真实SQL回滚。至少一个真实guard须经新unit完整路径证明无CommitRolledBack/无失败ACK正常结束，另外各来源逐点核事实与原cause；不能用一个fake typed异常冒充真实guard或反射改guard条件。

### 4. 正常回滚cause、结果时机和四whole

`CommandWorkUnit.RequireRollback(Exception cause)`与无参方法同样先DemandCurrent/open/transaction.RequireRollback，只额外保存本次捕获的原业务E。仅用于旧合法过滤catch；typeduncertain/取消或已有request/guard失败不得由它变正常回值；重复声明不能覆盖此前cause，不引入多层receipt。新runner的rollback-only分支有E时调用实际`lease.AbortAsync(E)`，无E仍原Dispose/无参回滚；原DomainTransaction.RollbackParticipantsAsync的失败集合会prepend E，因此rollback X与E同对象也保留两次发生。成功回滚E留给whole的unit外mapper，不记sticky；rollback/cleanup失败则E＋X typed并外抛，不能先返回拒绝或只丢E。若后置guard等独立故障夹在动作返回与Abort之间，也保E和实际新因，而不是覆盖成最后一个引用；不要按对象去重独立阶段。

WriteResult只对`transaction.CommitAsync`调用span的KnownStorage原失败，在actualCompletionConfirmedRollback、非cancel/ContainsUncertain、request/job健康无已记guard/登记failure、精确current/participants有效、全部unit/lease cleanup成功后返回CommitRolledBack。所有前/后current、Begin/Main/Footprint/Freeze/unknown来源/非法生命周期、非fallible违约、CompletionCommitted含持久后组装、rollback/lease失败均sticky；不得catch整个RunAsync为恢复区。tracked task涵盖所有cleanup，真正返回值后才正常完成；CommandBus仍drain→Close→scope dispose→最终失败裁决，额外scope失败不会被unit值清除。无需改bus/core失败去重算法来给新结果开后门。

Main只原将进入fine的有效分支claim一次，报告者精确participant、本次unit.Footprint下传原domain。无Input先InvalidRequest、再session/online NoSession，保持原顺序；ProtocolEffectRejected晚出现selection也不升权，早拒不Main/Pending→Rejected。domain旧throw在whole的**domain/OpeningWireSlots/clock窄try**捕获，仅非取消/非uncertain普通业务映成动作失败并RequireRollback(E)；先取unit.Footprint等机制能力在try外，runner/guard/outbox/Project/encoder不在业务try。unit完全结束后纯mapper按OpeningRejected reason/detail或StorageFailure作原结果、日志、ACK；guard来源/已记sticky优先，不能被catch恢复。

Package保AvatarInput有值就avatar domain，否则package；Avatar保Choices.Empty走普通package、否则avatar；single/batch同box domain但不同原Input/count/key、ACK字段。原成功通过ICommandProjections→OpeningCompleted：Responses先（Avatar还含reward popup）、replaced清空→updates→cleared→premium→creature→gold/following原序，composer/encoder/OpeningWireSlots只读。失败ACK在unit外直接typed outbox；普通NotiBus Failed仍隔离，不因本单元加initial raw。四whole只既有CommittedReplay许可，普通再发由current replay不重复开箱/roll/扣源/材料/加luck或重ACK。

动作RequireRollback成功与commit结果都令Main ConfirmedRollback而非Settled；Freeze后projection可能非null，保真实事实不强清；同一CMD不能再claim Main。下一次admission才原entry Pending/清projection、原Ordinal不更新。ConfirmedRollback响应失败的新故障、取消、未知/typed按实际默认保护，不把所有precommit失败强说都fence，也不把已提交当可重试。

### 5. 先旧后新完整oracle、反例与有价值RED/GREEN

Phase0使用新增OpeningProtocolCommandFlowTests、OpeningFixture最小configure扩展及EffectRequestTransport四shape/raw入口；生产/route/事务/算法仍起点SHA。同provider真实decoder/dispatcher/ledger/原fine→实际SQLite→原失败ACK/原outbox。原cause从同provider ILoggerProvider/实际业务wrapper捕获，旧fine/handler日志共因引用对照，迁后只保原handled日志、移除legacy重复Error按RQ-014说明；不修改FaultingEncoder/RecordedOutbound别名。旧entry如无可见只读入口，测试可对同ledger私有CWT/Entries做最窄只读观察，不能写字段/反射制造状态、重复调用Admit推进或另建ledger；新route优先actual whole wrapper捕获context/state。该观察不是业务guard证明，真实资产/帧/重试同时成立才验收。

- **action SQL旧oracle：** 原reward INSERT RAISE(ABORT)路径，完整协议应StorageFailure原ACK、处理日志有同一原SqliteException、无成功通知、源/奖励/材料/gold/luck/冷却/已记录事件delivery真实回滚、entryConfirmedRollback/原Ordinal、无新增participant fence/无失败作业source-close；删除trigger，同seq/raw合法重试同entry仅生效一次，再重放只当前投影。原directfine Throws仍由§6领域driver保，不能把这个ACK测试当它的替代。
- **真正native Commit旧oracle：** 用fixture临时parent/child表的DEFERRABLE FK及奖励item_slots INSERT触发器，只在目标reward写child缺父；**不是生产schema改动**。主控provider probe已证方法可行，现Persistence TransactionUncertaintyTests:69–93也有real deferred Commit/cleanup测试，但都不替代这里。核实际fixtureForeignKeys=1，actual domain wrapper/legacy pipeline观察原action已返回receipt且奖励写完成、失败stack有SqliteTransaction.Commit/19/787，再核DomainCompletionConfirmedRollback、ACK/cause/ledger/nofence/source/全部资产/冷却/event与去触发器sameentry retry。保存DDL/trigger/rawmeta和完整旧源/运行元数据；只在INSERT失败、fakeParticipant或孤立:memory:成功均不达门，先修探针或报具体阻碍。
- Phase1迁完同一oracle不改业务期望再跑；补其余三个logical各真实成功/拒绝（包含Avatar popup、Package选择/AvatarInput、single/batch count/key/luck）；旧/新不同观察方式逐项说明，不改原frame/数量/Throw目的。
- controlled Prepare用实际SQLite暂存资产＋真实participant存储Flush故障单独证明可恢复Prepare；custody/绑定/RunItem/facetguard及unknown participant普通InvalidOperation不能返回结果，正负同类异常对照证明没有粗类型分类。无selected/晚selection、当前性丢失、已有登记sticky、action catch guard、取消（action/Prepare/Commit）、非fallible违约、Committed后错误、额外scope、未await drain按实际状态区分；原默认Write/ReadOnly两种异常sticky/catch后禁写不变，两B2 raw正反例原样全跑。
- **E＋X必须实证：** 真实opening业务拒绝E/或controlled unit带真实SQLite资产映成RequireRollback(E)，另加入受控rollback/cleanup失败X；核typed原因树同时含原E与X、无正常失败ACK/无Returned/CommitRolledBack。Theory再让X引用同E对象，核两个独立occurrence未被去重。正常无参RequireRollback拒绝仍回滚正常返回；纯测试fault injection明确标控制机制，不冒native SQLite故障。现Persistence真实invalidated rollback/cleanup及deferred cleanup case继续只读实跑。

至少一个有价值可编译mutation：优先临时去掉owned runner对Guard/Unknown来源的恢复排除，让真实custody或绑定guard被误变CommitRolledBack/ACK，真实entry/返回状态/资产oracle应红；或去掉带cause的Abort(E)令E＋X原引用断言红。只动owned机制，不改业务/只读composer，原bytes/SHA及try/finally恢复，同目标GREEN再freeze。编译、无效metadata、fixture自等/缺服务/期限失败不是RED；原路线beforeoracle是保行为证据，不把“尚无API编译失败”算TDD。

### 6. 测试／driver精确白名单（13个现有＋4个新文件）

路径相对`Server/tests`；只列允许实际改者，未用者不为凑覆盖改。原Fact/Theory/InlineData/display/status/multiplicity保留，必要驱动/观察改变逐case解释，不能删SQL/Throws/bytes换绿。

| 路径 | 实际caller／允许动作 |
|---|---|
| `Dfo.Server.Application.Tests/Inventory/PackageOpeningTests.cs` | 76/113/143/159/224/244/262/356及OpeningFixture479–486直接四fine；多数是领域内容/SQL/冷却/并发/奖励oracle，改§下明确测试domain事务driver，保OpeningRejected/SqliteException Throws与原资产/60箱/选择/expiry/luck/虚拟/宠物断言。仅fixture构造可加本簇configure钩子，不改ShopFixture |
| `.../Inventory/OpeningSynchronizationTests.cs` | 28/45/61是domain结果再手工Compose/client replica，迁domain driver保原Responses/clear-reuse/replica字节目的；同文件CompoundItem case原bus不迁/不改 |
| `.../Inventory/OpeningCapacityRegressionTests.cs` | 79直接Batchfine，改domain driver保NoRoom/满包/virtual/无邮件断言与实际capacity fixture |
| `.../Inventory/DarkRepulsorPackageTests.cs` | 69业务Open改domain driver，保wrong-job/SQL Throws；21/36两个directhandler＋Empty metadata则改外部真实raw/decoder/dispatcher/fresh frame（第二次是新请求，不意外同seq replay），保原永久avatar/UID/交易限制/frame和不二授予 |
| `.../Inventory/GoldSilverHornRewardTests.cs` | 43 Package分支改domain driver；Lottery旧分支/Throws/SQL全部保持，不扩迁Lottery |
| `.../Inventory/SouthernDaleCompatibilityTests.cs` | 129完整FQN Batchfine改domain driver，仅该case准备/驱动；保原holy-water消耗/奖池，其余Southern/Lottery/mail不动 |
| `.../Inventory/EffectRequestTransport.cs` | 增四精确shape的真实Body，必要具名SendRaw供malformed/原capture；复用同Counter/Observe一次、FreshSequence与同decoder/dispatcher。Package selection两opaque字节/selectedIds/count/options、Avatar choices、single5B/batch15B严格照现decoder，原其它Body/seq/replay行为不改 |
| `.../Sessions/EffectSettlementRegistrationTests.cs`、`.../World/CommonExperienceEffectTests.cs` | legacy名单精确去四fine，request marker名单/其余断言保留；四whole另证Protocol/Direct/单shape/replay-only、不实现旧settlement marker |
| `Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` | 四logical→四whole完整生产图、四fine0、只replay许可及构造闭合；保所有Use/Buy/装备/Matrix和坏例 |
| `Dfo.Server.Application.Tests/Transactions/CommandWorkUnitsTests.cs` | 原全部默认测试/数量保留；仅如共用fixture签名编译必要适配，不把旧Write测试切到新入口或删catch后失败断言 |
| `Dfo.Server.Application.Tests/Transactions/DomainTransactionTests.cs` | 保原完成/排序/rollback原因/非fallible/prepare/fallible全部断言；可补本次事实/同对象独立因，不能把RecordingParticipant冒native |
| `Dfo.Server.Persistence.Tests/Database/TransactionUncertaintyTests.cs` | 保当前真实deferred Commit/invalidated rollback/cleanup全case；仅补来源事实/真实facetguard/原cause对照或fixture局部支持，不改原SQL/存储不确定断言 |
| 新`Dfo.Server.Application.Tests/Inventory/OpeningDomainTestDriver.cs` | 四领域测试的具名typed方法（Package/Avatar/Box），不是可传任意业务Func的测试CMD/总线平台；同provider真实事务/明确每调用test lease见下段 |
| 新`.../Inventory/OpeningProtocolCommandFlowTests.cs` | Phase0旧协议oracle→Phase1同oracle及四whole/state/映射/真实SQL/currentreplay、日志/事务观察；不另造provider/outbox或生产testsetup |
| 新`Dfo.Server.Application.Tests/Transactions/KnownRollbackWorkUnitTests.cs` | 新结果/guard/unknown/source绑定/全cleanup/track/保因/default对照，复用现CommandWorld/RecordingParticipant仅机制场景，真实guard/SQLite使用真实所有者；不新增通用Func命令平台 |
| 新`Dfo.Server.Persistence.Tests/Database/TransactionFailureSourceTests.cs` | actual SQLite facet/source/原cause、真实RunItem版本/生命周期等必要局部反例，保真实SQL状态；不反射改生产守卫/引入新实际schema |

表中现有文件实际逐项为13（六caller＋Transport＋两名单＋Protocol注册＋三个事务文件），新4，共17个测试路径；生产19＋测试17＝36精确source路径（含AGENTS）。本Task/ledger两文件/本Task证据另计，不将其混作source数。

**领域测试driver落实：** 使用同host.Services，外部`TestGameplay.RunAsync`组织一次明确测试来源；同provider SelectedSessionSafety.Capture(session,null)/Demand当前owner，`TestEffectFootprints.Lease`每调用创建关闭，真实`DomainTransactionScope.Begin`，原OpeningWireSlots转换后直接原domain、原clock。正常结果Commit；异常以真实lease.AbortAsync(cause)确认回滚、原对象重抛，finally完整Dispose；不得静态cap/跨job lease、legacy ambient、伪装wire/Main或调用新whole吞掉原Throws。用现显式test lease/helper只读复用，源测试不新增生产setup或第二provider。此driver只为已识别纯领域case；完整协议另走actualdecoder/dispatcher。原业务并发通过真实串行TestGameplay作业保“一次成功其余OpeningRejected”目的。

其余只读但必须full：四codec/Networking PackageOpeningIntegration、OpeningDelivery/原通知字节/算法测试；StorageLifecycleTests与全部default unit/NonRequest/explicit Main/B2/finitefamily/矩阵/guard/事件判据。Application/Support/TestEffectFootprints、NpcShopPurchaseTests中的ShopFixture、CeraShopFixture、Support/RecordedOutbound不改。若只读fixture不足先交主控精确理由，不按“测试驱动”泛授目录或反射注入授权。

### 7. 完整核验与数量／capture闭合

本单元改共享core，final同freeze完整Release `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`须0warning/0error；八项目各无filter full、原xUnit类并行/5m/Skip/期限保持，`dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`。七原control包含在各full中，**不另重复control-only**，Pvf无control；同新DFO目录原74 expected/七control齐，outside/exe/orphan0，exe0仅观察，新有意违规逐类/数单列不放宽旧expected。Golden15逐byte对f01dc226及本起点、Characterization10逐名pass/0skip。

所有步骤保存command/cwd/HEAD/UTC起止/exit/elapsed/resources/完整console并自然结束；不为省后续步骤TERM父runner/活跃测试、不补造exit/覆盖旧轮。按已验本机FSEvents/runner/caffeinate及必要require_escalated授权方法；源码改动后新freeze/实际正式轮，不拼不同源结果。beforeoracle/focused/mutation/formal分开，保存诊断，不把旧provider工具转录补造成formal metadata。

从8644逐case(display/status/multiplicity)核真实N－D，原移除0为目标，必要rename给逐个映射/理由；域driver/响应观察/logging有意变化单列，不以总数或Assert总多重集替代。两原DimensionDungeon准确失败身份/原Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException按Task14/AGENTS保留；18skip逐身份保持，本轮八full才可说全部项目重验。任何新失败先身份/根因诊断，不放宽timeout/deadline/并行/Skip/基线。结果required独立快审，不以P3 approved代产品通过。

fresh ConfigureServices-only before/after capture，不BuildProvider/起业务；family sidecar按已验方法构造真实注册表。预期173logical/203cmd不变，四Tx fine→四Direct whole：22Protocol＋1Internal＋180Legacy，152Tx/38Direct/9RO/4Workflow；message24Protocol＋149Legacy（原telemetry14仍同总账）。仅四route/四fine→四whole变化，原Use两raw/Buy双shape/Task13/14及全部共同bindings不动，以实得核不硬填。

### 8. 精确闭账、证据与交付

ledger两文件仅可闭：`message:65696` PackageOpening、`message:66055` AvatarPackageOpening、`message:65744` SingleBox、`message:66640` BatchBox；`command:Dfo.Server.Protocol.Game.PackageOpening.PackageOpeningCommand`、`command:Dfo.Server.Protocol.Game.AvatarPackageOpening.AvatarPackageOpeningCommand`、`command:Dfo.Server.Protocol.Game.MagicBoxSingleOpening.MagicBoxSingleOpeningCommand`、`command:Dfo.Server.Protocol.Game.MagicBoxBatchOpening.MagicBoxBatchOpeningCommand`。填写实际原caller/Tx/参与者条件/保护与whole及源码/旧新oracle/测试/Result路径，使用真实state，original/baseline/其它row不改。仅预期pending139→135msg、184→180fine、72source及5protection/8transition不变，不顺闭Mailbox/legacyReplay/来源。

本Task新脚本/日志限`verification/logs/task-15-*`、`verification/evidence/task-15-inspection/**`，现probe-tools/旧provider evidence只读。精确before/非ownedSHA、阶段0与final源freeze、逐casedelta/命令meta/manifest及Result R1 prefix齐；preclose先核artifact，追加Result后strict validate及原八坏副本全拒，不能提前填不存在Result骗strict。停止共享写后fresh独立required结果快审，review仅追加freeze R1之后，主控核精确SHA/物理路径并检查点；executor不自行stage/commit。

Result必须分别说明旧route实跑及native Commit证据、新sameprovider对照、Returned与CommitRolledBack/三leaf＋facet/unknown来源、action E＋X同对象保因、Main/entry/Ordinal/投影、四分支/帧与业务Throws目的、默认core/B2保持、mutation红绿和恢复、八full/DFO/Golden/表征/实际N－D/capture与所有诊断。其余135msg/180fine/72source/5保护8过渡、最终T-012新八full与单CMD≤2秒测量、Astra/Spec根封存及用户分支收尾不因本Task免除，不提前宣告B完成。规划阶段只有本契约/详细计划，无Result/review/status。
