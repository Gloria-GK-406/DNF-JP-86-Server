# TASK-25 — CrystalContract 选择归一完整 NonEffect 协议 CMD

## Task Unit Contract

- Revision: 1（同时标识本契约与详细计划）
- Outcome and references: 交付 [Plan P4](../plan.md) T-004 的下一最小单元：logical66072／wire0218 的精确 CrystalContractRequest 归唯一 GameCrystalContractCommand，吸收原 handler/fine 的判定、原一次写事务、保存后当前性复核、日志及投影，删除旧 fine 两类型。覆盖 [Spec B3](../spec.md) RQ-001、003～009、012、014、015及RQ-013/017本簇义务，AC-001～005、007/009/011局部结果、F-34/F-112；不宣告T-004或Spec完成。
- Owned boundary: §2精确7个物理生产／文档／测试路径、本Task、verification/evidence/task-25-inspection/及verification/logs/task-25-*新材料；canonical migration-ledger.json/md只改§8两个ID。mixed slice的消息／decoder／encoder／通知块、原domain/premium/持久化/共享core与公共support只读。无Main、footprint、ledger、replay、attempt、新raw许可或P3恢复。
- Required checks: 产品前源身份、完整NUL tracked分区、真正旧registered decoder→dispatcher→fine／sameprovider SQL与actualencoder oracle封存；原新两字节/完整condition行和其它资产、普通Write／正常拒绝Commit／异常Abort、保存后world复核、原单batch及Failed隔离、真实SQL action与sameparticipant nativeCommit、正常拒绝ACK异常壳及cause/close/chain；原测试强目的/metadata/同seq、可编译真实保存错误mutation RED→逐byte恢复GREEN。最终同freeze Release0/0、三无filterfull及四freshcontrols、原8969逐display/status/multiplicity N-D、DFO74/7、Golden15/Characterization10、source/runtime/purefamily、两canonical ID、strict／原八selftest及八物理坏副本、完整manifest和nonowned SHA。
- Dependencies: 仅 [TASK-24](TASK-24.md) r1／Result R1／fresh Independent Quick Review R1 approved／Controller Consumption，已在9b73025147005749584eb4ce6b6b51bf9a6e2934 committed done。B3/P4及matching gates继续有效，Spec baseline仍10a7218a779493494b138bd6b43cc0fc1a84aa6a，A与旧Task不重开。
- Result review: required — 把mode当材料、错存selection、提前world检查或删保存后复核，会改变持久选择或回包；通知故障与拒绝ACK的不同处理也容易在搬迁时混淆。fresh独立结果快审实读mixed slice保留块、原新真实保存／回滚／mutation／测试接缝和路径数量账；主控消费后才checkpoint。不另设独立Task-plan gate。

## Detailed Plan

### 1. 身份、授权与原库存

唯一workdir为/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021，branch task/refactor-session-20261021，接手HEAD 9b73025147005749584eb4ce6b6b51bf9a6e2934。主控已核Task25唯一未用、仅依赖Task24 committed done、DAG／clean／index空；执行者再核实际身份和唯一writer。当前没有.codegraph/，按rg定位，不自行建立索引。

沿既有governed／long-running／task-units执行，中文及本地Co-Authored-By检查点约定保持。执行者不stage/commit、不push/PR/merge/remote issue/版本/tag/Release/部署/cleanup，不运行df_game_r/DNF.exe，不建Task26。主控readiness后由fresh producer实施，旧Task／A seal／既有证据只读；本计划不是结果批准。

当前库存8969＝8949pass＋原准确Dimension2fail＋18skip。Task24 formal01三fresh full为App5194（5176pass/2fail/16skip）、Protocol1776pass、Networking199pass；其它1800＝Host203＋HttpApi57＋Infrastructure976（2skip）＋Persistence484＋Pvf80仍是Task20原full库存。本slice不改共享core，最终三full加四freshcontrols，不冒称八freshfull。套件耗时与资源只观察，单CMD≤2秒留T-012；Task21旧TCP Connection refused根因仍未知，不因本轮通过而声称已修，也不按已登记偶发豁免。

### 2. 精确路径、保留块与caller闭合

7物理路径＝4生产／文档＋3测试，预计5改／2新／0删文件。旧fine两个类型在原mixed slice内删除，不为追求物理文件删除而拆动其余制品。保原BOM／CRLF／mixed行尾，按bytes定点修改。

| 相对workdir路径 | 允许变化 |
|---|---|
| Server/src/Dfo.Server.Protocol/Game/CrystalContract/CrystalContractSlice.cs | 只删除SelectCrystalContractCommand及其handler，CrystalContractHandler变一次bus薄入口；必要using调整。其余消息／decoder／encoder／notification record和handler块逐byte保持 |
| 新Server/src/Dfo.Server.Protocol/Game/CrystalContract/GameCrystalContractCommand.cs | typed whole、原一次ordinary Write、原先后／日志／保存后复核／ACK与投影 |
| Server/src/Dfo.Server.Protocol/ProtocolModule.cs | 只66072 route与一fine→whole，原decoder/encoder/notification注册不变 |
| AGENTS.md | 更新已迁CrystalContract现状与保存／当前性／单batch边界，不写过程或引用.rks-agent |
| Server/tests/Dfo.Server.Application.Tests/Inventory/CrystalContractTests.cs | 原协议helper改真实wire，stale fine改真实domain＋原显式Tx；保全部方法、参数及原强业务目的 |
| 新Server/tests/Dfo.Server.Application.Tests/Inventory/CrystalContractProtocolCommandFlowTests.cs | 本族真实旧新wire／局部observer、完整condition资产／SQL/native／拒绝及投影故障／metadata／同seq／mutation；可提供本App原测试共用的局部wire helper |
| Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs | exact logical/CLR、Direct/Protocol、NonEffect／无exit、旧fine及handler从assembly/注册缺席 |

实读旧fine生产caller只有本CrystalContractHandler与ProtocolModule；测试仅Application/Inventory/CrystalContractTests的stale直接bus调用。没有独立Delivery/Coordinator或其它fine消费者。现有Server/tests/Dfo.Server.Protocol.Tests/Game/CrystalContractTests.cs是7项纯decoder/encoder/向量（3个两字节输入、3个长度拒绝、1个成功与CubeProgression body）且无旧handler构造，整文件只读。此族不含槽位映射，不为了套上前Task模板修改SlotBoundary判据。

只读：CrystalContractDomainService、ICrystalContractDomainService、ICharacterConditionTable、CharacterBodyTables/SqliteDatabase及schema、Premium服务／权益判断、StateSynchronization/CubeProgression、整个执行器/CommandBus/WorkUnits/EventService/NotificationBus/outbox/ledger/attempt、ShopFixture/RecordedOutbound/TestGameplay。mixed slice按原命名类型边界保存保留块bytes/SHA，最终逐块核相同，不能用整文件已改而跳过codec/通知保护。Networking现有Gameplay inbound错误身份consumer只涉Wedding/Exit和已迁Sell，无本族consumer；若执行时实证必须扩7路径或共享接口，带最小源与实际失败返主控，不能默扩。

### 3. 产品前真实证据

先核7源既存bytes等HEAD、新whole absent，记录before最小源、SHA/bytes/行尾、HEAD/branch/index/UTC/实际命令和完整raw。以git ls-files -z取完整tracked集合，证明tracked＝本Task原存在owned源＋nonowned、无交集/无遗漏；每项currentSHA等HEAD，不以C-quoted中文路径或Path.exists筛选丢行。明确本B的verification/migration-ledger.json与.md两actual canonical键先在map，最终才排除；Task与新证据另归属。旧Task23/24完整NUL方法可借鉴，既有输出不覆盖。

在产品／AGENTS修改前用原migration-ledger.py向Task25新prefix捕source/runtime，另按原pure family/exits方法实捕。当前预期173logical/201cmd、33Protocol＋1Internal＋167Legacy、139Tx＋49Direct＋9ReadOnly＋4Workflow、44message Protocol/129Legacy、telemetry14，只作校验预期，不代替actual。

产品前先只加本族局部新flow/driver/observer：selector1/type0218、精确body2B为mode@0/selection@1，原metadata.WithRawBody＋GameInboundFrameCounter记录同对象、seq/ordinal/fresh/length/SHA。通过原registered decoder→dispatcher→原handler/fine→真实domain/SQLite→同provider Registering/outbox/actualencoder实跑；真实wire不能外包TestGameplay导致self-wait，准备和直接domain能力另在TestGameplay中运行。

至少先取成功(0,0)/(0,5)/(1,3)/(0,255)、原非法mode/selection和缺失/恰好过期权益拒绝、旧会话domain拒绝及后续静默、真实SQL action/nativeCommit、成功通知composer/actualencoder故障、正常拒绝直接ACK encoder故障、同seq当前状态的旧oracle。局部新旧structured case可以合并同等覆盖，不能为了数量重复矩阵。先封before manifest及原driver/source身份，再迁产品；不迁后重建before。普通fixture/编译/DDL/observer诊断可自治修正，保全部原raw，不把诊断当行为RED或每次另设批准门。

### 4. 一个whole保原先后与普通Write

采用GameCrystalContractCommand(NetworkDispatchContext Dispatch, CrystalContractRequest Input):IProtocolCommand<bool>，Message/Frame引用原Input/Metadata；bool表达本次领域saved结果，不是投递或持久提交凭据。handler只实现对应IProtocolCommandMessageHandler并一次commands.SendAsync，不再查询session/users、记录业务Info、登记或投影。whole仅注入原session/user查询、ICrystalContractDomainService、ICommandWorkUnits、ICommandProjections、IGameCommandOutbox、ILogger；不需要新增TimeProvider、sender/bus/queue/行网关/规则/内容或locator。

旧fine为Transactional＋IAccountCommand，没有ISelectedSessionCommand/IRequestSettlementCommand/recorder，自身无selfTx。因此有session/online的分支对应一次ordinary WriteAsync(context,new("SelectCrystalContract",[]),(_,token)=>crystals.SelectAsync(session,message.Mode,message.Selection,token))。显式空selected集合保旧范围；[]不代表没有SQLite participant，也不豁免request/login/connection守卫。不加Main/NonRequest/footprint/ledger/replay/attempt/registration exit，不用WriteResult或RequireRollback。

原序逐项保持：

1. sessions.FindByConnection，随后users.TryGetByConnection；任一缺少原样静默return false。原handler此处没有IsInWorld门，**不得提前加world检查**。
2. 有session/online才一次Write，直接调用原crystals.SelectAsync。domain中mode>1、selection>5且非255、或!IsInWorld先返回false；之后查CrystalContract权益，成立才SaveCrystalSelectionAsync。mode仅UI来源标记，不是材料ID/扣除操作；不添加材料消耗、quota、事件或状态缓存。
3. 所有正常saved=false都照旧正常Commit；真正SQL/commit/unknown/cancel/typed异常继续sticky外抛，没有旧具名catch可复制，不造失败ACK恢复。
4. 单元完整结束后保原Info字段characterId/mode/selection/saved，然后原sessions.IsInWorld(session)复核。false静默，保已提交写，不补ACK、不补偿、不重做，返回saved即可。
5. saved=true才TheseCharacters([session.CharacterId])投影原CrystalContractSelected(session,mode,selection)；false且post-world仍current才直接RegisterCmdAsync原CrystalContractResponse(false,0,255)。不把失败response字段“修正”为原输入。

实际持久化SqliteCharacterConditionTable.SaveCrystalSelectionAsync（CharacterBodyTables.cs:13起）在同database.WriteAsync内即时UPDATE character_condition的crystal_cube_type/crystal_cube_grade并限定character_id，affected!=1仍抛InvalidDataException。domain/API/table/schema全不改，不把两列保存变成另一事务或内存投影。没有新领域模块或共享业务抽取需要。

### 5. 投影、当前性及原测试

保mixed slice原CrystalContractSelected/handler整块：recipient character/connection及IsInWorld检查成立才[成功ACK(true,inputMode,inputSelection),CubeProgressionStateMessage]，一次preparedbatch、两帧原序/字节/精确目标不变。成功通知的普通composer或actualencoder故障仍由NotificationBus逐target Warning＋Failed隔离，caller原忽略结果并正常结束，已保存选择不逆转／重试、无半batch/意外close/fence。caller自身取消、外层lookup/scope等真正外抛不改成Failed。

本族whole在投影前也问IsInWorld；**不能照抄Task24在Commit后第一次IsInWorld就throw的proxy作为composer故障**，那会错打handler后置复核。局部测试可保存原Scoped INotificationHandler<CrystalContractSelected> descriptor，以同provider局部forwarding decorator只在其ComposeAsync内throw固定cause，正常时调原handler；不改生产catalog/registration/composer或公共support、不把调用点外层故障冒通知隔离。actualencoder仍经同Registering/FaultingEncoder注错，记录实际调用和整批未登记，不能用假投递器替代。

保存后world复核要有针对证据：成功保存后，在whole/旧handler的该只读查询处报告不current，核保存确已Commit但无ACK/NOTI及无重做；可用有界phase-aware只读query proxy配真实domain保存观察，不直接篡改持久写或假称真实并发退出。若proxy只是模拟可见性，Result明确它证明分支顺序，非真实socket退出。原stale-session直接fine用例改真实ICrystalContractDomainService.SelectAsync＋显式原DomainTransactionScope/Commit，保旧character选择不变；另真实wire queryproxy可返回真实旧session，而IsInWorld问实际切角registry，domain拒绝/后置静默。wire本身不携旧session字段，不将proxy说成网络身份取证。

直接拒绝ACK的真实路径都在fine／ordinary Write正常返回后；无session/user的前置是静默，**不虚构noWrite拒绝ACK**。至少分别取非法mode/selection短路（可能没有SQL participant）与MissingEntitlement（实际权益读取）两种normalCommitted encoder故障：真正old raw与新既有默认PostCommitPublicationFailure外壳差异单列，InnerException Same、Completion/HasCommittedUnit、empty selected participants/attempts、完整资产不变、ACK0/无batch、Single source close且Assert.Same/derived ReferenceEquals、Root/Parent/Depth/Source/SourceConnection/CommandType日志及EndedAJob/nofence实测。保原NetworkPlugins消费与outbox关闭政策；不因外壳变化新增第四raw许可。拒绝observer不额外读SQL而人为enlist。

保Application/Inventory/CrystalContractTests全部方法和InlineData：原4成功shape的committed-before-ACK观察、mode/grade/stamina与角色2保持，3非法输入、缺失/恰好到期权益、stale旧角色、持久化失败零输出均保持强目的；只改驱动/必要接缝。新helper真实2B decoder/dispatcher，不保fine兼容。Protocol原7codec/向量整文件不动；注册判据核exact(66072,CrystalContractRequest)、唯一Direct/Protocol whole、NonEffect/无exits及旧fine/handler从注册和assembly缺席。此族无SlotBoundary改造。

NonEffect同rawseq继续按当前事实保存，不用ledger/旧ACK重放：相同2B请求先成功，外部通过合法原事务或fixture明确改变当前已存选择，再以同seq同body请求，核重新保存请求的mode/selection而非复用旧ACK；另同seq不同合法两字节输入应按该次输入保存/响应，不能套Effect payload冲突围栏。记录原new metadata/ordinal/fresh及真实rows/frames，证明不只是调用计数。

### 6. SQL/native、全行资产与有效mutation

先旧后新各取实际character_condition目标character_id=1的BEFORE UPDATE触发RAISE(ABORT)产生SqliteException19/1811，核原cause、action阶段、ConfirmedRollback、原全行及其它资产不变/零ACK，不能将其叫nativeCommit。原Failed_persistence测试继续有同等保护，不靠新用例替掉旧断言。

同fixture、同实际SQLite participant另建parent/child＋DEFERRABLE INITIALLY DEFERRED FK，只在seed之后启用限定character_id=1的AFTER UPDATE OF crystal_cube_type,crystal_cube_grade触发missing-parent child。action真实完成、两列实际已stage、FK1/child>0后，真正SqliteTransaction.Commit报19/787；CommitFailure.Cause与原外抛cause Same、native stack与Completion ConfirmedRollback可核，全部condition行/其它资产恢复，去trigger同rawseq请求按当前事实成功。可用局部forwarding domain observer记录真实open非只读DomainTransaction、完成时SQL及stage；不加第二fallible participant、不以fake Prepare/controlled participant替nativeCommit。按实际flush时点收敛observer/DDL，Persistence/core不改。

完整快照至少character_condition两角色全列（含creature_buffer BLOBhex/NULL及非选择字段）、characters/character_stats、items/item_slots、account_premiums、domain_events/domain_event_deliveries；种入非默认stamina、若干其它列/BLOB/SQL NULL及不同角色选择，保只改当前角色两列、权益end_time及无材料/金币/事件副作用。无expiry帧字段时逐byte比较ACK/CubeProgression；记录真实input、目标/one batch和原持久值。same-run rollback全部列exact零豁免。跨run仅actual owning source已核的SQL CURRENT_TIMESTAMP列按同UID或account/premium身份/真实UTC窗口及先后校准，以及EventService三GUID非零逐case跨phase双射/DeliveryFK/batch/origin；fixed clock决定的权益时间、condition所有列和其余payload/bytes精确，不能泛忽略BLOB/时间/ID。没有实际变动就不另制造豁免。

业务mutation采用whole内临时将某合法selection映成另一个合法值，例如仅message.Selection==3时传给domain的值由3改5（显式byte），其它输入/响应不变；实际request(1,3)必须错误存grade5，完整condition资产oracle真实RED，合法(0,0)或(0,255)可达正控pass。保存actualrows/frames后才assert，不能只断言参数/调用次数，也不把编译/fixture/timeout当RED。finally逐byte恢复whole并核唯一本mutationdiff/SHA，同目标全GREEN后才freeze。具体语法可按最终代码形状自治收敛，不改保护事实、不动domain/存储或留测试开关；纯结构搬迁不制造无价值RED。

### 7. 最终freeze与正式检查

focused完成原强测试、旧新真实wire、正常拒绝ACK壳/成功投影Failed、post-world顺序、SQL/native、metadata/同seq及mutation恢复后，freeze7源＝7final存在／0文件删除（其中2新增），附原case声明参数、驱动／断言接缝变化、mixed slice保留块与readonly源SHA。旧fine名称的生产/测试实际caller清零，仅注册缺席断言字符串可保。全部先前native自然完成后才声明finalfreeze，preliminary若有必须保诊断且不作正式身份；正式后改产品/测试须新freeze和相应正式复跑。

以本Task新runner/prefix，在require_escalated可用环境运行native（sandbox FSEvents可能挂），保cwd/HEAD/实际命令/UTC/exit/elapsed/resources/完整console/DFO目录；不TERM活跃native/父runner、不覆盖原输出。原parallel、5m、Skip及自然结束保持：

```text
dotnet build Server/86JP.Server.slnx -c Release --no-incremental
dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

build exit0/0warning/0error；分别Application.Tests、Protocol.Tests、Networking.Tests三个无filterfull。App只容原Dimension两精确display Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource、All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot及InvalidDataException／Dungeon7100/maze0/cell(1,0)/entrance Left。其它失败先保完整身份/stack/输出，依AGENTS实际登记身份规则诊断/复跑，不调parallel/timeout/Skip，也不把Connection refused视已登记基线。

同freeze Host/HttpApi/Infrastructure/Persistence四GameplayAssertionRecorderControlTests fresh各1/1，DFO原violations.py CLI保持74records、7actual controls、原expected逐类/数和程序集集合、outside/exe/orphan0/新增intentional0，exe0仅观察。Golden15逐byte同时等HEAD/f01dc226，Characterization10逐display全pass/0skip，Protocol原CrystalContract7codec向量整源原样。Counter以Task24 formal01三full及Task20其它1800实际库存逐(display,status,multiplicity)，原8969保留、N-D闭合，rename/status/实际删除逐项说明，预计D0/新增全pass不替actual；四freshcontrols不重复计业务N。suite资源/时间不作单CMD2秒证明。

### 8. Capture、两ID及交接

finalfreeze fresh after source/runtime/purefamily/exits，预期173logical/201cmd（一fine→whole总数不变）、34Protocol＋1Internal＋166Legacy、138Tx＋50Direct＋9ReadOnly＋4Workflow、message45Protocol/128Legacy、telemetry14。actual仅66072 route及SelectCrystalContract fine→GameCrystalContract whole变化；其它command/family/route和原三exit种类及所有owner精确等HEAD。精确binding为(CrystalContractRequest,66072)、Direct/Protocol/无RegistrationExits。

canonical只准改message:66072及command:Dfo.Server.Protocol.Game.CrystalContract.SelectCrystalContractCommand两个ID：原一次Tx→ordinary Write/空selected、正常拒绝Commit/异常Abort、保存后world复核、原Info/ACK与单batch／Failed隔离、实际SQL/native/默认拒绝ACK壳证据。其它baseline/original/rows/groups逐值等HEAD，md旧前缀只append。预计pending114message/166fine/72source/5protection/8transition，由原validatoractual确认，不提前闭其它范围。

required checks齐后按executing-tasks追加Result R1与literal Outcome: done，原计划前缀bytes/SHA明确。Result前缀/manifest自身避免循环；实际preclose/strict、原8selftest及8物理坏副本按原semantic逐项拒绝，临时坏副本可改其原需用行但canonical仍仅本两ID，不改tool/基线或弱化反例。完整manifest列全部source/evidence/scripts/log/Task/两ledger bytes/SHA，与Git NUL changed+untracked精确闭合，无文件删除不伪造deleted项。完整nonowned map仅排两actual canonical后逐SHA原样，旧Task/A/证据不改。

Result说明真正before、原新condition/两字节/提交点/post-world/同seq证据及proxy局限、normal拒绝ACK外壳差异／cause/close/chain、成功Failed隔离、SQLaction与nativeCommit差别、全部资产/其它角色/权益、有效mutation/恢复、原测试接缝/数量账、正式范围/capture/strict/manifest/nonowned。完成后停止全部共享writer/native，交fresh required独立快审及主控消费后本地checkpoint，不自批Spec。RecordedOutbound是登记/精确关闭条目证据，非真实socket关闭/A21实机；Astra、T-012测量、Spec最终核验/封存与用户分支收尾仍后继。

### 9. 自治收敛与返主控

本地helper/observer/fixture/DDL条件/私有结果与mutation表达式可按实际接口自治修正，保诊断，不逐项新设批准门。不能提前world检查、改mode/selection语义、正常拒绝一律回滚、把通知Failed变throw、把SQL外抛改拒绝ACK，或扩raw/P3许可；不能保兼容fine、隐藏第二总线或改domain/公共support求绿。真正before否定本计划的事务/保护/错误消费，或必须扩7路径/WHAT/共享接口/core时，带最小完整实证返主控；不可达native给出实际阻碍和可行路线，不降格假成功、不自选Spec终局。
