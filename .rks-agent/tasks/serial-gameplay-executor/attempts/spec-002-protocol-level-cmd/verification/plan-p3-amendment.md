# Plan P3 amendment：四 opening 的 commit 阶段已确认回滚结果

## 目标、授权和已消费输入

本次是[Plan P3](../plan.md)的受影响共享设计补足，不是Spec修订或产品实现。主控裁定沿原授权保留PackageOpening、AvatarPackageOpening、MagicBoxSingleOpening、MagicBoxBatchOpening四协议旧失败响应；只允许本writer修改`plan.md`并新建本记录。未编辑routing-decision、Spec/Contract/root/A、旧Task/Result/review/证据、产品/测试/ledger；未运行native/build/test或git，未创建Task15。

当前产品起点为Task14 required独立approved后主控检查点`0d5df6a4288a1b34751054d59a527eaa55ca8b74`，parent `de65601cc9fa5fa15db6afae59fc589265717cc8`。工作区仍`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支仍`task/refactor-session-20261021`；本writer消费主控已核检查点，不自行执行git核验/提交。writing-plans采用现有high-level/task-units路线；attention_review on-drift、controller/profile、控制强度、T-001～012总路线/其它依赖不改变。

写前实际核对的治理身份：

| 制品 | SHA-256 / 消费范围 |
|---|---|
| 原Plan P2 | `0dd2dc61f5a6fa606e0fc308d2339cd578b7f543c87de9d55bc3ec9d29dc3212`；本次在其上定点补足 |
| [Spec B2](../spec.md) | `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228`；17RQ/11AC与VC-B1/B2/B3不改 |
| 子[TaskContract](../TaskContract.md) r1 | `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6`；baseline仍`10a7218a779493494b138bd6b43cc0fc1a84aa6a` |
| [B2 amendment](spec-b2-amendment.md) | `ca46e1cef8daf0e5d3c717986e2c2818264b8c60a82a471e18371e0b365bab87`；不修改已绑定输入 |
| [P2 amendment](plan-p2-amendment.md) | `14a1cb4dca0d0dda5c00b533d18403b1048d61c414ffe96285861f2eee11625e`；历史保留 |
| [P2 fresh challenge](../reviews/plan-p2-challenges.md) | `0448d159a3603d0d467cc503953f720bb8fe9fd5e32086bbc4c8d12df160ce7b`；不替代本次fresh挑战 |
| [P2 independent review](../reviews/plan-p2-review.md) | `45c4f9c5438ed12599cac2f1504e7af012e979369f553521d32d57d505b711c9`；approved且匹配上述P2/旧amendment/routing |
| [routing-decision](../routing-decision.md) | `54a0dcf923faa180316ddda5e31d18052c937f709c24a2cdac677c4ebb817f33`；只读，由主控后续更新当前Plan gate制品路径 |

本记录对应作者交付的Plan P3 SHA-256为`778a28e67377d099d0d0cc43739e5c3083566c5895cb3292f2b47e2625909519`。ready仅表示共享设计可供fresh挑战/独立Plan评审，不是producer批准；后续若有修订须重新绑定目标，不能以本记录预批后续API或测试。

## 真实旧行为与缺口

以下路径相对worktree；源码是本次实际读取的行为依据，测试名/catch语法/类型名不代替阶段证据。

| 源码位置 | 已核事实及对P3的意义 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/PackageOpening/PackageOpeningSlice.cs:65–75,90–104` | 原fine在自身handler内执行wire转换/clock/领域调用；外层message handler在await整个fine之后catch非uncertain OpeningRejected转原reason/detail，catch普通非取消/非uncertain转StorageFailure并Error，随后事务外失败ACK或成功OpeningCompleted |
| `.../AvatarPackageOpening/AvatarPackageOpeningSlice.cs:76–90` | 同类fine外catch；成功仍响应＋reward popup及OpeningCompleted，失败ACK不在catch内 |
| `.../MagicBoxSingleOpening/MagicBoxSingleOpeningSlice.cs:66–80` | 同类fine外catch，不是仅包domain action；非取消/非uncertain普通故障转StorageFailure |
| `.../MagicBoxBatchOpening/MagicBoxBatchOpeningSlice.cs:70–84` | 同上；single/batch共享同box domain，但仍四个原独立协议/事务边界 |
| `Server/src/Dfo.Server.Application/Commands/Pipeline/UnitOfWorkBehavior.cs:51–70` | Transactional fine先action，再Freeze，再Commit；异常Abort并记RollbackConfirmed后向外重抛。故旧四handler catch覆盖真正Prepare/fallible Commit回滚后的原错误，不能只用action内RequireRollback替代完整边界 |
| `.../Pipeline/SelectedSessionSettlementBehavior.cs:27–36`、`.../Effects/EffectOperationContext.cs:88–113` | 旧Freeze设置Entry.Projection，RollbackConfirmed/Failed设置回滚事实/状态而不强清已Freeze projection；typed uncertain才按原selected保护fence，普通已确认回滚映射ConfirmedRollback，不是Committed。外层若成功发失败ACK，原作业可正常结束；具体ACK/entry/source状态还须后继旧route实跑 |
| `.../Transactions/DomainTransaction.cs:97–129,139–154` | Commit在Prepare之前置settled=true；Prepare/唯一fallible Commit异常调用RollbackEnlistedAsync，完整回滚成功则原cause重抛；非fallible Commit违约成为typed Uncertain |
| `.../Transactions/DomainTransaction.cs:190–226,231–260`；`DomainTransactionScope.cs:82–92` | settled后的Abort/Dispose不再回滚；真正Completion由rollback操作决定。Rollback失败保原cause和cleanup并typed Uncertain；IsSettled或外层再Abort一次不是确认回滚的新证据 |
| `.../Transactions/DomainTransaction.cs:154–187` | CompletionCommitted之后的outbox composition异常不能改成回滚结果；afterCommit callback原逐项Warning并保持提交，不扩本次恢复span |
| `Server/src/Dfo.Server.Persistence/Database/SqliteDatabaseParticipant.cs:127–150,159–210` | Prepare实际校验facets/flush；唯一fallible Commit调真实SQLite pending.CommitAsync。CompleteAsync在Commit操作失败后仍Settle，清理成功原cause原样重抛；清理失败typed Uncertain并保各因。不能将普通SQL trigger动作失败命名为真实Commit失败 |
| `Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs:75–95,99–126` | 当前显式runner在action之后先request/participants检查、Freeze/Commit；任何逃逸异常都request.Failed并tracked fault，finally还处理unit/lease cleanup。因此whole外catch不能消除sticky，action内RequireRollback也覆盖不了后续Commit错误 |
| `.../Commands/CommandRequestContext.cs:43–79`、`.../Effects/ExplicitEffectRequest.cs:24–54` | DemandCurrent/Track/Drain/结束检查及Main claim有原生命周期；新结果不能复活请求、清已有failure或重置claim。Freeze后ConfirmedRollback的projection可能仍非null，不能拿projection有无推断commit |
| `.../Effects/EffectOperationLedger.cs:53–61` | 只有下一次ConfirmedRollback admission才复用entry、置Pending/清projection，保原entry.Ordinal不延长窗口；本次unit返回不能模拟新请求 |

四顶层领域入口`Inventory/PackageOpeningDomainServices.cs:14–103`及`OpeningInventoryDomainService.cs:51–127`不自行Commit/Rollback。实际物理奖励`Effects/OpeningEffectHandlers.cs:115–123`调用`ItemDeliveryDomainService.DeliverAsync`，失败在该服务`:172–175`仅RequireRollback；opening plan固定InventoryOnly（`OpeningEffectPlanRuleService.cs:102`），不进入溢出MailOnly/InventoryThenMail及Mailbox自身RunAsync早回滚。虚拟奖励用`ItemGrantDomainService.ApplyPlannedAsync:21–37`，不调用会自Rollback的`ItemGrantDelivery.AddAsync:67`。因此本四协议不需要为Mailbox式已经settled的服务新增通用恢复能力。

真实参与者按触达条件动态加入，不能把某fixture名单当恒定全集。`SqliteItemDomainStore.cs:20–24`先接ItemLifecycleScope及连接串SQLite participant；`Abstractions/Inventory/ItemLifecycleScope.cs:68–74` Prepare检查无悬空custody，SQLite Prepare还复查facet。容器查询/写入在事务内加入`CharacterContainerDomainService.Changes`（`:18–20,134–155`），Prepare有绑定版本守卫；库存事实加入`InventoryContentsEventDomainService.Batch:25–28`、`DomainEvents/EventService.EventBatch:83–101`。有cooldown时才加入`Effects/ItemEffectHandlers.StartItemCooldownEffectHandler.Commit:49–65`，Prepare空、非fallible Commit启动冷却。保这些真实守卫与提交点，不把程序守卫或非fallible违约降级为StorageFailure恢复。

## 已证与未证的测试边界

现有开包业务强oracle必须继续保留，以下都**不是本次重新运行**：

- `Application.Tests/Inventory/PackageOpeningTests.cs:200–215`：reward item_slots INSERT trigger，在action内真实SQL失败；原源保留、cooldown未开始、去trigger后retry。
- 同文件`:401–411`：accounts.seria_luck_value UPDATE trigger，前面库存写之后的action失败；原源/材料/奖励/luck回滚。它不证明SQLite Commit失败。
- `DarkRepulsorPackageTests.cs:53–64`：reward INSERT trigger，直接fine抛SqliteException，源/奖励回滚后重试；`PackageOpeningTests`、`OpeningCapacityRegressionTests`多处正常OpeningRejected也主要直接调用fine。
- `Networking.Tests/Integration/PackageOpeningIntegrationTests.cs:18–60`：四opcode未选角/截断请求实际失败帧与连接继续可用，不含事务故障。
- `Application.Tests/Transactions/CommandWorkUnitsTests.cs:86–103`、`DomainTransactionTests.cs:88–119`：recording participant的Prepare/fallible Commit故障能证明机制回滚/原cause；不能替代四opening真实SQLite/protocol响应证据。

主控另完成仅用于provider可行性的独立探针，作者本次实际读回[Program](evidence/plan-p3-provider-probe/Program.cs)、[project](evidence/plan-p3-provider-probe/probe.csproj)、[observation](evidence/plan-p3-provider-probe/observation.json)及[stdout工具转录](evidence/plan-p3-provider-probe/stdout.log)：实际引用Persistence项目的transitive Microsoft.Data.Sqlite，唯一临时`:memory:`数据库，session56612自然exit0；default_foreign_keys=1、sqlite_version=3.53.4，deferred FK INSERT成功，真实`SqliteTransaction.Commit()`报19/787，Rollback成功。Program SHA `aaf38e0739c260ae1254936205d2de9d7af92e9dd98b77991c3fbbd40cb0936d`、project SHA `9100df1c3bc8898698f4e61ca45650003bd69d29fa96c5b8ebbeff9974b3a763`、stdout SHA `3c78cba3339035b5607d41efbdff023081249598d93a6a30242d96c63b5ac57e`。stdout明确为完成tool session的精确转录，未捕进程start/end/resources，不冒称正式全量metadata；这四文件由主控写，非本writer新增或修改。

该探针只证明真实Commit注错方法在实际provider可行；未走opening/DomainTransaction/legacy route，**没有证明四opening的失败ACK、Completion、无fence、同entry retry**。当前仍没有已实跑的四opening真实Prepare／SQLite fallible Commit恢复oracle或rollback/cleanup不确定性的完整协议证据。捕获语法表明旧恢复边界可达的结构位置，不等于所有阶段已测。后继必须先在旧route取得真实oracle，不能把本provider探针或静态推演当协议通过。

## 主控裁定及P3共享决策

主控明确保A/旧四opening失败响应，选最小commit-only机制，不改变B2 RQ-006的两个即时raw登记出口。受影响的是Plan HOW：RQ-003/006/007/008/014/015及AC-001～005/008/009已有等价/事务/保护/验证要求；17RQ/11AC、VC与Spec文字保持，不新增visible change或终态授权。

1. **现有默认入口不变。** WriteAsync/ReadOnlyAsync仍任何逃逸异常sticky、catch后不能继续写/正常结束。新具名写结果方法仍context/declaration/action显式输入，最终名称留Task；不新增默认permission或通用action恢复。
2. **封闭的已完成单元值。** 正常Returned<T>包括RequireRollback正常业务拒绝，不能叫Committed。CommitRolledBack只由runner在transaction.CommitAsync窄span出错时建立：真实CompletionConfirmedRollback、原cause非取消/非typeduncertain、request/job健康且无已记guard/登记失败、当前性/精确participants有效、全部unit/lease cleanup成功。调用者不能自报/构造该回滚事实，不清request.Failed；结果不是许可/授权回执框架。
3. **失败阶段不扩张。** Begin/Main/Footprint、action前后current/participants、Freeze/非法生命周期等runner守卫不在新span；已有sticky、unknown/unhealthy/Uncertain、rollback或额外lease cleanup、非fallible违约、CompletionCommitted含durable后组装、额外scope失败保持默认typed/fence与所有cause。只有实际回值的tracked operation正常完成，不能先回值后finally/drain失败；独立错误不能因同cause引用被忽略。CommitAttempt仅记已证调用阶段，不谎称内部更细阶段。
4. **Main/账本保原事实。** ConfirmedRollback不Settled/Committed，不清claim；Freeze后的projection可能尚在。下次admission按原entry/Ordinal重置，不在当前CMD开第二Main，不用IsSettled判断持久结果。
5. **action业务映射在各whole。** 窄try仅包原domain/OpeningWireSlots/clock调用，照旧OpeningRejected和普通非cancel/nonuncertain过滤，保存local异常、调用unit.RequireRollback并正常返回动作失败结果；纯业务mapper在unit外将其原因转原领域拒绝/StorageFailure、日志/失败ACK。RequireRollback的request/current校验不能被同一业务catch吞掉；guard已标记失败时不会被恢复。outbox/Project/encoder不进业务try。commit-only回值也在完整清理之后按原cause映射。Application不认识Opening类型/协议错误码，不给CMD新factory/locator能力。

不新增request ambient、job slot、DI当前请求、单例可变请求或从环境事务查请求，不新增global tracker、任意Func raw豁免、多层receipt。原B2 source proof/最终raw裁决、普通登记/BestEffort/NotiBus Failed、唯一ledger/reader/sink、事务内Compose禁用都保持。Mailbox自Rollback留后继按其真实source单独规划，不借四opening预授权。

Plan仅改Planning Choice当前修订、§4、§5的ICommandWorkUnits说明、T-004依赖/结果、相应Verification、Next Planning Boundary及追加P3本地修订；§1～3、其它共享决定/工作包/交接覆盖/最终接受线/P1与P2历史修订原文保留。当前下一UsefulUnit为四opening与被真实消费者验证的最小共享结果能力，不先交一个无人消费的空壳核心。

## 后继取证和门

本P3改变共享机制的风险基础：把已确认回滚误作成功提交，或错误恢复guard/取消/已有sticky；返回时未清完unit/lease导致晚故障；Prepare后projection或Main claim误重置使同请求重复结算；旧action故障冒充真实SQLite Commit证明；回滚响应丢失或ACK顺序/日志漂移。这些需要**fresh受影响adversarial challenge＋独立Plan review**，不能沿用P2 approved或本writer结论。review须绑定当前P3/本amendment，routing-decision的当前gate路径由主控另写；旧P2挑战/评审不动，Spec gates不重开。

门通过后主控先作amendment-only检查点，再给Task15精确路径/详细规划授权，之后才能测试/产品写。计划要求Task15先以未迁旧route实跑真实action SQL原oracle及真实SQLite fallible Commit确认回滚；可将已证provider deferred FK方法接到fixture临时表＋奖励INSERT trigger。**接入opening的完整方法尚未实测**：必须再核该fixture实际ForeignKeys状态/stack，action与奖励INSERT先成功、真正SQLite Commit失败且完整回滚，连同旧ACK、cause/日志、ledger状态、资产/冷却/事件、fence/source及sameentry/原Ordinal retry形成证据。普通trigger RAISE/action SQL失败、RecordingParticipant、仅测试名或孤立provider探针不能替代。接入方法不成立保诊断并换可达有证据方法，必要阻碍报告主控；controlled Prepare＋真实SQLite资产作为独立阶段证据，不能改称native Commit。此剩余技术实证留临近Task，不否定当前已读commit-only分界，也不预授权扩大恢复span。

迁新API后同provider真实decoder/dispatcher/Main/domain/SQLite/原outbox重跑上述旧oracle，并实证Returned拒绝与CommitRolledBack两路、Prepare/fallible Commit/rollback和cleanup不确定、guard/cancel/unhealthy/已有登记sticky、额外scope失败和未await drain。原cause、Main一次及Freeze后状态、同entry/原Ordinal retry保持；默认Write/ReadOnly原坏例、两B2 raw正反例、其它既有pipeline/footprint/finitefamily/NonRequest不削弱。至少有价值可编译mutation按临近Task test-value选择，不靠编译/fixture失败冒充RED。共享core预计完整Release build0/0、八项目full、原DFO74 expected＋七actual controls、Golden15/Characterization10、逐case/display/status/multiplicity增减核对；结果仍required独立快审。具体API、probe/测试白名单和编排门后落Task，不在本记录提前物化。

## 已接受单元与当前数量边界

Task01–14各自结果/独立review/检查点保持有效。Task11已启用Main/Use及原两个B2 raw，Task12有限CLR族/NonRequest，Task13/14装备消费者仍使用原默认Write；四opening还在旧route，本P3不把它们算已迁。新入口不是修正已接受消费者默认策略，也不追写旧Task或声称新机制已验证；新能力的共享回归在后继重新做。

主控当前延续库存8644＝8624pass＋2原Dimensionfail＋18skip；最新App4886＋Protocol1766＋Networking199＝6851为Task14三full，另1793仅Task12已接受库存（Host203＋Http57＋Infrastructure976＋Persistence477＋Pvf80＝1793），不是本次八full。runtime仍173logical/203cmd＝18Protocol＋1Internal＋184Legacy，156Tx/34Direct/9RO/4Workflow；pending仍139message/184fine/72source/5protection/8transition。P3没有产品/测试运行、capture或闭账，不以这些引用数字冒充新取证。

不改根/子契约或baseline，不重开sealed A，不宣告B/根完成。最终T-012新八full、单CMD≤2秒测量、Astra、Spec/根封存和用户分支收尾门均保持；中文、不push/PR/merge/远端issue/部署/cleanup/改版本或运行游戏的限制继续有效。

## 作者交付核对与未验证条件

```yaml
kind: rks-verification
decision: manual_required
```

主张限于“P3高层共享设计已写成供独立gate审查的ready稿”，不主张Plan已获批准或实现通过。作者以写前P2文本逐段对比：frontmatter、共享§1～3/§6以后未涉本次的决定、除T-004外全部工作包、交接索引、最终集成验证及P1/P2历史修订保持；Spec/子Contract/routing、旧B2/P2 amendment、P2 challenge/review及Task14 SHA均等上表/写前值，Task15不存在，amendment绑定当前Plan SHA。作者仅写两个获准文档，主控另写的provider探针为只读消费。

**Unverified Conditions：** P3 fresh受影响challenge、独立Plan review、主控amendment-only检查点尚待执行；四opening旧route及新结果入口的完整协议/DomainTransaction测试仍待门后Task，本记录无其通过证据。决定与推进权由主控持有。
