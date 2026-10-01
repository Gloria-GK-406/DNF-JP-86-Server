# TASK-008 — 效果命令生命周期与不确定结果阻断

## Task Unit Contract

- Revision: 1
- Outcome: 全部23个范围内命令在完整UoW中保持精确会话生命周期保护；不确定结果在串行门释放前阻断该精确session／物理连接，丢弃旧pet运行态并精确安排关闭；已支付kill／clear后publication失败不能变为可重付claim。Plan T-006，RQ-003/008/009及AC-002/007/008的故障边界部分。
- Dependencies: TASK-007已接受（BorrowOrAcquireSharedAsync；typed uncertainty Contains/Find；两cleanup锁序；Application88／Protocol88／Persistence392），此前各效果路径实际存在。
- Owned: marker/pipeline45与58/注册，全部23命令Session，Finish领域context的Session贯穿，selected safety/fence，dispatcher和authorization predicate，Networking exact close，Creature DiscardExact，范围内普通catch分类，kill/clear已提交claim保护及专项测试，Task结果/coverage。
- Excluded: 不实施sequence统一ledger／raw hash／effect-footprint／重放current snapshot；不改概率／费用／业务通知首发顺序／party逐成员原子边界，不扩其他领域，不commit/deploy/live DB/PVF/client。后继009纯投影、010统一replay、最终整合仍必需。

## Detailed Plan

1. CommandContracts提供可空Session的具名ISelectedSessionCommand，23类实现：Use、Rename、Fatigue、Coupon、Learn、Finish；GrantKill／ApplyMonsterKill／GrantClear；Reinforce、Enchant、MagicSeal、Outworld、Forging；Npc/PortableDisjoint；Compound、Package、Avatar、MagicBox单批、Lottery、Vending。保留原Transactional及Online/Account模式。Finish新增可空Session，网络入口显式传入并贯穿QuestCompletion领域至EffectExecutionContext；三XP及Finish可信离线null保留，不以marker表面代替actual context。
2. Order45借用或真正取得SelectedSessionTransitionGate Shared，范围覆盖Online50→Account55→global57→58→UoW60；provided Session在等待前后与当前exact identity匹配，始终同account/character/generation。保留Fatigue/Lottery/Vending原outerExclusive，借用不反取；没有self-reported skip bool或shared升级。给后继operation-session校验保留明确入口，本单位暂不造ledger。
3. 在首次gate/bus await前捕获原recipient connection对象（online TryGetByConnection并核对account/current character/ConnectionId及exact selected），等待后复核，不能拿当前替代connection作为旧close目标。不能直接比较online.Generation与Session.ConnectionGeneration（BindCharacter后不等）。Party每recipient捕获，禁止用reporter context替代；session null可信离线异常仍传播，没物理对象时不能假称close成功。
4. 58在57内、UoW60外包住整个结算。递归typed uncertainty出口先同步设不可清的exact-session＋CWT物理connection fence，再DiscardExact、记录具名原因/原cause、account/character/connection/generation/operation及seq/ordinal（有则记），用None安排precise close，随后传播。不因Fresh/Bind/window淘汰清fence，不派nested bus。普通确认rollback保留原异常且未fence；额外cleanup异常不能抹原cause或取消已有阻断。
5. Dispatcher handler lookup前检查原Connection对象fence，无login authority也建立NetworkCommandAuthorization predicate；原authority检查保留。policy100队列之后DemandCurrent同时检查fence，覆盖已排队／重新选角命令。只fence当前精确session/旧物理context，替代session及新connection不能误封。blocked请求不能进handler、开UoW或消费。
6. INetworkConnectionCloser新增RequestCloseExactAsync(expectedContext,ct)，生产实现ID TryGet后ReferenceEquals，捕获entry.Session安排detached close，不二次按ID查、不await cleanup反取生命周期门。保留现非exact API；修改相关fence使用exact，顺带核对本次触碰Return/Select失败关闭原context风险，使用已有明确original context时可改exact；不重写其他关闭业务。测试fake明确实现，不提供把exact退回ID的production默认。
7. CreatureRuntime新增DiscardExact：在既有gate仅移除匹配entry、该exact session roomRewards及pending projections；不Advance/Gain/DB写入、不删replacement。Leave在没有匹配entry时立即返回。异常出口保持整个discard在生命周期保护内；typed failure不能Leave旧cached stomach覆盖已提交DB。
8. 全部正常拒绝或MarkCommitFailed catch先排除Contains(exception)：Compound、Enchant、MagicSeal、Outworld、Forging、Package/Avatar/MagicBox单批、Lottery/Vending、Fatigue、Coupon（含后置projection catches）、Learn、Finish wrapper/handler、kill/clear。合法拒绝仍原ACK/次序；typed uncertainty不发送“已回滚”的普通失败ACK，不授予confirmed retry票据。保留递归包装，不只检查最外异常。
9. 增PostCommitPublicationFailure具名reason。solo/party/clear bus正常返回立刻标记durableCommitted；之后CommitParticipantKillEffect=false、CommitSettlement=null、clear currentProgress=null、取消／publication异常都不释放奖赏claim。kill CommitActorDeathChange=false在已有支付后也保护actor claim／精确已支付recipient；CommitParticipantKillEffect可能已先移除claim，不能只靠不Release而不fence。party已完成成员保持completed/delivery结果，确认rollback的失败成员继续selective retry；已提交发送失败不再次估价／发奖。publication的fence/discard同样取生命周期Shared（实际借用），只安排close不await。原业务结果发送顺序保持。
10. 真正行为RED优先：用现真实XP command bus在真实SQL durable后nonfallible异常，再同Session重试，旧无fence会重复付款；或实际claim publication=false重复支付反例。只认实际运行行为失败，不把新interface编译失败当RED。
11. 新tests走production CommandBus/SQLite/executor/typedfailure及受控barriers：uncertainty在57释放前设立，第二排队命令不读/不写；confirmed Prepare rollback同Session仍能合法再执行；wrapper/Aggregate分支不ACK/Markfailed；23 marker完整注册/Session一致/可信null；outerExclusive三入口不自锁且替代/断线等整个UoW。沿现真实fixture，纯content与明确故障参与者可fake，不把纯fake commit当实际DB durable。
12. 真实pet discard后Leave不写回durable值，旧Session不删替代/roomRewards/projection；Networking actualregistry/context ABA只关闭被捕获session或无操作，不误关替代；close callback在租约释放后才能完成且无死锁。kill/clear用现Protocol regression扩publication false/throw/currentProgress null/postcommit cancel，以及recipient与reporter不同context保护；原confirmed retry／partialparty／发送失败断言保留并说明搬迁。
13. 串行build与受影响Application gate/dispatcher/effects/XP/quest/pet，Protocol生命周期/kill/clear/全部normalcatch，Networking closer/auth与相关Persistence tests；真实PVF按既有路径可达，不动运行环境。记录verification/TASK-008实际TRX/skip/故障阶段、diffcheck及新未跟踪源码复读。写本Task结果和coverage后停止writer，controller检查实际结果再接受。最终fullsuite和replay仍后继，不能本阶段关闭父任务。

## Readiness / Attention Check

007实际API与结果已由根复读且接受；只读范围枚举、catch清单、publication额外漏点已核对preparatory与实际调用者。本单位目标仍是一份effect计划只能安全结算一次，处置未知结局是必要边界；纯规则／基础实体和已验收概率不改。生产只有一个writer，根与其他审计只读。依赖已可用，权限/验证/后继明确，可开始。

## Result

```yaml
kind: rks-task-result
decision: pass
claim: TASK-008 session safety and owned required automated checks
controller_acceptance: accepted
```

### 已实现与后继API

- 全部23命令实现`ISelectedSessionCommand`，保留原Transactional、Online／Account模式；FinishQuest网络入口传入当前Session，领域`FinishAsync(...,ct,session)`直到actual`EffectExecutionContext.Session`。三XP／Finish可信离线null继续支持，没有外观marker替代actual effect context。
- 45在任何admission await前`SelectedSessionSafety.Capture`原recipient；借用007真实生命周期Shared租约，等待后复核exact selected／account／character及原connection对象。50／55／57及58／UoW60均在租约内。58处于总串行57内且UoW外，对提供的recipient再次复核，并在60前调用NetworkCommandAuthorization，排队的其他入站命令也在开启事务前拒绝。
- 单例`SelectedSessionSafety`提供`Capture`、两类`DemandCurrent`、`IsBlocked`、`BlockAsync`、`PublicationFailedAsync`。Exact-session集合与物理connection对象CWT独立于现有replay缓存；没有Fresh／BindSession／窗口清除API。`BlockAsync`同步设fence后才await生命周期门（publication即使被已持Exclusive阻塞，也已封住旧请求）；租约内DiscardExact、具名原因日志、None token exact close安排。TryLog隔离额外logger故障并保留原typedcause；discard／schedule异常记录后不撤销fence。没有物理对象的可信工作不假称已关闭连接。
- Dispatcher在handler lookup之前检查原context对象fence；有无login authority均安装predicate，保留原authority条件。58队后检查覆盖未执行的排队请求和重新选角。NetworkAuthorization携带metadata供结构化sequence／ordinal日志；反射Metadata仅用于telemetry，授权／结算不依赖反射业务字段。线上完整raw hash／ledger仍由后继处理。
- `INetworkConnectionCloser.RequestCloseExactAsync(expectedContext,ct)`生产实现按ID TryGet且ReferenceEquals；只捕获当时entry.Session安排detached close，没有按ID二次查、没有等待close callback反取生命周期门。Return失败使用incoming原context；Select pending-close仅与incoming ID一致时改exact，其他已有关闭行为保留。
- CreatureRuntime新增`DiscardExact`仅移除匹配Entry、该精确session roomRewards与pending projections，绝不Advance／Gain／写DB、不删replacement；Leave无匹配Entry直接返回。既有pet timer仍outer lifecycle Exclusive→AdvanceCreatureClock Transactional57，已复读该链，未扩时钟业务。
- Compound／equipment MarkCommitFailed／opening／fatigue／coupon（含后置投影）／learn／Finish正常拒绝catch递归排除typed uncertainty，不授予confirmed retry票据、不发送假“rolled back”ACK。确认Prepare／commit失败且rollback成立保留原异常与原业务拒绝路径。
- 增`PostCommitPublicationFailure`reason。solo／party kill和clear在bus正常返回当下记录durable状态；participant publication false（即使claim先被移除）、actor publication null／throw／cancel、clear settlement null／cancel／currentProgress null均不Release回可付claim，并fence原recipient。Party每成员在冻结候选循环首部捕获自己的物理context，view／skill等待后复核，绝不使用reporter替代；前面完成成员保持已完成结果。成功claim后的发送失败仍不会重新估价付款。

### 真实RED与最终检查

| 检查 | 实际结果 | 证据 |
|---|---|---|
| 旧实际XP bus durable后nonfallible故障，再同Session发奖 | 1实际失败；第一次DB exp=30，第二次旧实现无阻断 | [RED](../verification/TASK-008/TASK-008-red.trx) |
| Fresh全solution build --no-restore | 退出0，0警告／0错误 | [build](../verification/TASK-008/TASK-008-build.txt) |
| Application受影响gate／dispatcher／transaction／equipment／opening／special／quest／XP／pet／lifecycle | 702通过／0失败／0跳过 | [Application](../verification/TASK-008/TASK-008-application.trx) |
| Actual FatiguePotionHandler outerExclusive（亦包含于702） | 1通过／0失败／0跳过 | [Fatigue handler](../verification/TASK-008/TASK-008-fatigue-handler.trx) |
| Application actual VendingMachine入口补项 | 12通过／0失败／0跳过 | [Vending](../verification/TASK-008/TASK-008-vending.trx) |
| Protocol完整项目（含最新actor throw分支） | 1218通过／0失败／0跳过 | [Protocol](../verification/TASK-008/TASK-008-protocol.trx) |
| Networking Runtime＋GameLoginIntegration | 23通过／0失败／0跳过 | [Networking](../verification/TASK-008/TASK-008-networking.trx) |
| Persistence Database参与者／事务 | 17通过／0失败／0跳过 | [Persistence](../verification/TASK-008/TASK-008-persistence.trx) |
| diffcheck／新未跟踪源码复读 | clean；实际读Safety、45／58、ExactConnectionCloseTests及其入口链 | [diffcheck](../verification/TASK-008/TASK-008-diff-check.txt) |

最终TRX Counters已实际复读；筛选重叠不相加作独立覆盖数。所有dotnet串行，PVF相关final Application／Vending／Protocol／Networking都设置`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，12 Vending含真实archive解析及实际handler的outerExclusive借用；Lottery actualhandler亦在702；最后证据复读发现旧Fatigue tests只直接调用Domain，遂新增real Shop／CommandBus／SQLite FatiguePotionHandler outerExclusive实际入口成功用例（消费2→1并发送成功响应），独立1通过后fresh完整受影响702再通过；原701中间有效证据保留`application-pre-fatigue.trx`。测试使用既有临时绝对SQLite及隔离host，不启动用户服务、不接运行库、未改PVF或客户端。

### 故障阶段与证据边界

- Application CommonExperience实际production CommandBus／SQLite／sharedexecutor：真实DB durable后注入非可失败参与者IOException，外部SQL读30且同Session重试拒绝；受控56 admission＋Prepare barriers证明第二队列命令在57释放后不再读取／估价／写入。确认晚Prepare IOException仍可在原Session正常重试。真实SQLite rollback恢复为0加故障参与者rollback failure须fence；Aggregate非首分支＋wrapper uncertainty亦同样挡重试。没有将所有IOException当unknown。
- Publication Exclusive barrier证明BlockAsync等待生命周期门时CWT／session fence已同步成立；同session下一命令在admission前拒绝。已过dispatcher lookup、无login authority的排队non-marker命令在58拒绝，没有进入handler；另外blocked physical context在未注册handler lookup前就被阻断，同值不同对象不误封。
- Actual effect UoW在outerExclusive之内借用成功，竞争teardown Exclusive等待完整结算；logger主动throw时原durable IOException仍在typedcause，exact close fake捕获原context且callback要取得lifecycle Exclusive，租约释放后完成、无自锁。该callback是受控transport替身；生产registry选择与detached target则另由Networking actualregistry＋ITcpSession proxy测试。
- Actual CreatureRuntime＋SQLite先建立cached stomach，再独立durable写80；Discard后Leave不覆盖回旧值，projections／entry清除；旧session discard／Leave不能清replacement。没有把Discard包装Advance、没有任何SQL恢复写。
- Kill／clear publication branch测试使用既有明确fake bus／run，证明commit后false／null／throw／OCE／missing progress不Release及不重付；不能作为实际SQLite durable证据，SQLite证明来自CommonExperience。新增party不同connection测试使用真实Safety，online.Generation=99而Session generation=1，证明不错误比较这两代次；member被封、已完成leader不被误封，重入也不再次派发两成员奖赏。既有confirmed failure selective retry／partial party／发送失败回归保留且Protocol全项目通过。
- 新增23 command marker完整集合／Transactional注册断言；FinishQuestactual effect context证明与Protocol wrapper/Aggregate unknown不发普通拒绝ACK断言均通过。现有三XP与Finish offline null回归保留。

### 开发失败与修正记录

早期`TASK-008-xp-dev.trx`8通过／2失败是未设置PVF env，非本次已证明baseline；带env重跑10通过，最终702亦包含两例。Application首次700通过／1失败仅原`EveryProductionBehaviourResolves...`硬编码缺新45／58，已更新完整ordered列表再701全过，失败保留`application-old-pipeline-assertion.trx`。Vending首次11／1因stale identity现在由45先于UoW拒绝，定点适配该断言；适配过程中误匹配另一同文业务拒绝断言导致2开发失败，已恢复正常业务断言并12全过，相应旧admission／adaptation TRX保留。开发claims71／1是再次leader请求遇已封member，更新为拒绝且Commands仍2后Protocol全过。没有抹去失败、没有把这些称为已证明旧baseline。Windows Python换行导致本阶段触碰源码CRLF diffcheck噪声已恢复LF，fresh build及clean diffcheck确认。

### 控制器接受

2026-10-01：实现者明确停笔后，独立[TASK-008-stage-audit](../reviews/TASK-008-stage-audit.md) approved。根复读实际Safety／45／58／claim变更、actual Fatigue入口、新鲜build和702／12／1218／23／17 Counters及clean diffcheck，接受本单位，registry进入009。此前已发现的同步fence、Party捕获和normal catch遗漏均关闭；不将此接受等同全部重构或父AC-007完成。

### 后继与停止点

没有实现current replay snapshot／preencoded batches／raw body身份／统一operation ledger／effect footprint／历史cache清理；仍需后继009／010及最终同库补项、全覆盖审计与人工验收列表。此结果仅该Task安全边界，不关闭父AC-007或父重构。未commit／deploy。唯一writer已完成required checks，写本Result及coverage后停止production写入和dotnet，等待控制器／独立只读复核。
