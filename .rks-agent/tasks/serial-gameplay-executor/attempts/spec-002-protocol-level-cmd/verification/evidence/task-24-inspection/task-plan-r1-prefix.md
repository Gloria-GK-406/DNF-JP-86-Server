# TASK-24 — RepairItem 普通与自动修理归一完整 NonEffect 协议 CMD

## Task Unit Contract

- Revision: 1（同时标识本契约与详细计划）
- Outcome and references: 交付 [Plan P4](../plan.md) T-004 的下一最小单元：logical65559／wire0017 的普通8B与具名自动7B RepairItemRequest 归唯一 GameRepairItemCommand，吸收原 handler/fine 的编排、原一次写事务、拒绝映射与成功投影，删除旧 fine。覆盖 [Spec B3](../spec.md) RQ-001、003～009、012、014、015及RQ-013/017本簇义务，AC-001～005、007/009/011局部结果、F-34/F-112；不宣告T-004或Spec完成。
- Owned boundary: §2精确9个物理生产／文档／测试路径、本Task、verification/evidence/task-24-inspection/和verification/logs/task-24-*新材料；canonical migration-ledger.json/md仅改§8两个ID。原codec/通知/domain/rule/premium/usage/事件/持久化/公共support/执行机制只读。无Main、footprint、ledger、replay、attempt、新raw许可或P3恢复。
- Required checks: 产品前源身份、完整NUL tracked分区及真正旧registered decoder→dispatcher→fine同容器oracle封存；原新普通/auto完整资产、次数与事件、精确帧/批次、clock及Direct事实同事务、正常拒绝Commit/异常Abort、SQL action和真正sameparticipant nativeCommit回滚、拒绝ACK异常壳及cause/close/chain、成功投影Failed隔离、原强测试/metadata/同seq、可编译资产mutation RED→逐byte恢复GREEN。最终同freeze Release0/0、三无filterfull及四freshcontrols、原8947逐display/status/multiplicity N-D、DFO74/7、Golden15/Characterization10、source/runtime/purefamily、两canonicalID、strict/原八selftest及八物理坏副本、完整manifest和nonowned SHA。
- Dependencies: 仅 [TASK-23](TASK-23.md) r1／Result R1／fresh Independent Quick Review R1 approved／Controller Consumption，已在c0e7245cba281f3565e12550af877481f53204bd committed done；B3/P4及matching gates继续有效，Spec baseline仍10a7218a779493494b138bd6b43cc0fc1a84aa6a，不重开旧Task或A。
- Result review: required — 错把auto当普通修理会扣金且绕过额度，拆开次数Direct事件/耐久写会留下半结算，拒绝/投影错误语义搬错会改变ACK或关闭。fresh独立结果快审须实读原新分支、真实回滚与mutation、测试接缝及数量/路径账；主控消费后才checkpoint。不另设独立Task-plan gate。

## Detailed Plan

### 1. 身份、授权与当前库存

唯一workdir为/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021，branch task/refactor-session-20261021，接手HEAD c0e7245cba281f3565e12550af877481f53204bd。主控已核Task24唯一未用、依赖Task23 committed done、DAG/clean/index空；执行者开始时再核实际身份和唯一writer。当前无.codegraph/，按rg定位，不自行建索引。

沿既有governed／long-running／task-units执行；中文、本地checkpoint与Co-Authored-By继续适用。执行者不stage/commit、不push/PR/merge/remote/版本/tag/Release/部署/cleanup，不运行df_game_r/DNF.exe，不建Task25。旧Task/A seal/既有证据只读，主控readiness后由fresh producer执行；本计划不自批结果。

当前库存8947＝8927pass＋准确旧Dimension2fail＋18skip。Task23 formal01新鲜三full为App5173＝5155pass/2fail/16skip、Protocol1775pass、Networking199pass；另1800＝Host203＋HttpApi57＋Infrastructure976（含2skip）＋Persistence484＋Pvf80为Task20原full库存。本slice不改core，最终跑三full加四freshcontrols，不冒称八freshfull。套件时间/资源只观察，单CMD≤2秒仍在T-012统一测量。Task21旧TCP Connection refused未知根因不因本轮绿而称已修，也不作为登记偶发豁免。

### 2. 精确路径与实际caller闭合

9物理路径＝5生产／文档＋4测试，预计6改／2新／1删。按bytes定点改并保各文件BOM/CRLF/mixed行尾，不整文件格式化。

| 相对workdir路径 | 允许变化 |
|---|---|
| Server/src/Dfo.Server.Protocol/Game/RepairItem/RepairItemHandler.cs | 变为只交原Dispatch/Input的一次bus薄入口；原ErrorCode纯映射随用例迁入whole |
| Server/src/Dfo.Server.Protocol/Game/RepairItem/RepairItemCommand.cs | 删除旧fine及handler，不留兼容签名 |
| 新Server/src/Dfo.Server.Protocol/Game/RepairItem/GameRepairItemCommand.cs | 精确typed whole、原顺序/一次ordinary Write、auto日志、ACK及成功投影 |
| Server/src/Dfo.Server.Protocol/ProtocolModule.cs | 只65559 route与一fine→whole；decoder/encoder/notification注册不变 |
| AGENTS.md | 更新已迁RepairItem现状与原普通事务/auto额度原子性，不写过程或引用.rks-agent |
| Server/tests/Dfo.Server.Application.Tests/Inventory/EquipmentRepairTests.cs | 原协议helper改真实wire，stale fine保真实domain+原显式Tx，保全部方法/参数/强业务断言 |
| Server/tests/Dfo.Server.Application.Tests/Inventory/InventorySlotBoundaryArchitectureTests.cs | 仅迁Repair旧Slot/NoGameAddress判据，保nullable business边界及实际badmodel拒绝 |
| 新Server/tests/Dfo.Server.Application.Tests/Inventory/RepairItemProtocolCommandFlowTests.cs | 本族真实旧新wire/局部observer/完整资产与次数事件/SQL/native/投影/metadata/同seq/mutation，可局部helper供EquipmentRepair复用 |
| Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs | 精确logical/CLR/Direct/Protocol/无exit与旧fine/handler缺席 |

已实读的旧fine生产caller只有RepairItemHandler和ProtocolModule；测试caller为EquipmentRepairTests及SlotBoundary架构。原RepairItemHandler.ErrorCode没有其它caller，纯映射可成为whole内私有方法，无需保旧handler业务入口。现有RepairItemRegistrationTests只有4个纯decoder/encoder测试，无旧handler构造，整文件只读；保自动7B逐byte变异拒绝、普通decode、repair-all保list及成功/失败向量。

只读：RepairItemMessages.cs、RepairItemMessageDecoder.cs、RepairItemResponseEncoder.cs、EquipmentRepairCompleted.cs、StateSynchronization消息/codec、所有Data/Abstractions、EquipmentRepairDomainService/EquipmentInstances/repair规则、PremiumServiceUsageDomainService、UsageQuotaDomainService/订阅注册、Persistence/schema、CommandBus/WorkUnits/EventService/NotificationBus/发件箱/ledger/attempt及ShopFixture/RecordedOutbound/TestGameplay。公共领域修理能力仍供实际领域用户使用，不为迁fine改API或引入协议字段。当前Networking日志消费者只见Wedding/Exit及已迁Sell，不预改Net文件。若真实caller闭合需新路径、共享core或不同领域接口，带最小实际证据交主控，不能默扩白名单。

### 3. 真正产品前证据和两种wire形状

先核9源既存bytes等HEAD、新whole absent，保存最小before源码、SHA/bytes/行尾、HEAD/branch/index/UTC/真实命令和结果；新Task/新证据自有，不复制全仓/bin/obj。用git ls-files -z取得完整tracked集合，证明tracked＝本Task原存在owned源＋nonowned、两集合无交集无遗漏；每项nonowned currentSHA等HEAD。明确两actual canonical键为本B的verification/migration-ledger.json和.md，必须先证明在map，再允许最终排除，不能以C-quoted中文路径或Path.exists筛选丢行。沿用Task23产品前纠正的NUL方法，不回写旧错误map或sealed历史。

以原migration-ledger.py向本Task新prefix捕source/runtime，另配原pure family/exits方法；当前预期173logical/201cmd、32Protocol＋1Internal＋168LegacyFine、140Tx＋48Direct＋9ReadOnly＋4Workflow、43message Protocol/130Legacy、telemetry14，全部要actual核实。

产品前先只加入局部真实wire driver/observer。selector1/type0017；普通8B为space@0、signed short index@1（-1代表all）、signed short material@3、auto@5=0、reserved@6、quick@7。自动只允许原exact7B 00FFFFFFFF0100，decoder返回Slot=null、MaterialSlot=-1、AutoRepair=1、Reserved/Quick=0、RepairAllSpace=Main。8B body[5]非0以及不匹配exact7B的输入由原decoder抛InvalidDataException；不放宽它、不拼造8B auto、不在新whole隐藏多消息输入。

使用原metadata.WithRawBody及物理GameInboundFrameCounter，观测同对象、selector/type/Sequence/InboundOrdinal/Fresh/body7或8/完整SHA。通过实际注册decoder→dispatcher→原handler/fine→真实SQLite→同provider Registering/outbox/actualencoder取得旧oracle；真正入站驱动不能外包TestGameplay导致self-wait，TestGameplay只用于准备或显式domain能力。先取普通single/all、auto成功/当前拒绝、pre拒绝/静默、正常domain拒绝、SQL action/nativeCommit、成功投影composer/actualencoder故障及直接拒绝ACK编码故障的旧证据，再封before manifest，最后才产品迁移。不在迁后重建before，不覆盖原输出。

保原EquipmentRepair测试全部方法/参数；Unsupported_variant实际QuickRepair=1可用普通8B body[7]=1表达。若额外验证的是typed request中decoder不可能产出的组合，准确标为typed编排边界，不冒真实wire；无需为凑wire改变codec或删原拒绝目的。普通fixture/DDL/observer/编译问题可自治收敛，保诊断原raw，不冒行为RED或逐项新设批准门。

### 4. 一个whole承接原先后与一次事务

采用GameRepairItemCommand(NetworkDispatchContext Dispatch, RepairItemRequest Input):IProtocolCommand<bool>，Message/Frame引用原Input/Metadata；bool只表达用例成功，不作提交证明。薄handler实现对应IProtocolCommandMessageHandler，只一次commands.SendAsync。whole仅注入原online/session查询、slot mapper、IEquipmentRepairDomainService、TimeProvider、ICommandWorkUnits、ICommandProjections、IGameCommandOutbox、ILogger；无sender/bus/queue/gate/行网关/内容或locator。

原fine是Transactional＋IAccountCommand，没有ISelectedSessionCommand/IRequestSettlementCommand/recorder，也无自开写事务。因此真实执行domain的分支是一段ordinary WriteAsync(context,new("RepairItem",[]),action)，显式空selected参与者保原范围；[]不代表没有SQLite/Event participant，不豁免真实request/login/connection守卫。不增加Main/NonRequest footprint/ledger/attempt/replay，不复制Sell的具名业务异常或opening的WriteResult恢复。

逐项保持原序：

1. message.Slot非null时先slotMappings.ToBusinessSlot；nullable all为null。不catch原mapper异常，不把它改业务拒绝；mapper早于session查询，原ToBusiness规则不搬入domain。
2. sessions.FindByConnection；session=null立即静默return，随后IsInWorld=false也静默。不得补NoSession ACK或增加先读取online的顺序。
3. 原automatic严格为AutoRepair==1 && Slot is null && RepairAllSpace==Main。在字段合法性判断之前计算；原MaterialSlot!=-1、非零AutoRepair但!automatic、Reserved!=0或QuickRepair!=0映InvalidRequest。仅这些原条件，不加RepairAllSpace或其他猜测门。
4. 字段合法后online查询缺失映NoSession；有online才一次Write。action按automatic在同一原位置直接调用repairs.AutoRepairAsync(session,token)或repairs.RepairAsync(session,businessSlot,token)，不用nested fine/bus。所有正常EquipmentRepairResult拒绝继续正常Commit，不一律RequireRollback。
5. unit完全结束后automatic原Info日志保character/result字段。成功receipt用TheseCharacters([receipt.Session.CharacterId])投影原EquipmentRepairCompleted(receipt,message.RepairAllSpace,result.Entitlements,result.Usage,clock.GetUtcNow())。clock是成功Tx之后的参数求值，普通/auto都要保；不能挪入action、换系统UTC或用修理前快照。失败直接outbox RegisterCmdAsync原RepairItemResponse(null,ErrorCode(...))。
6. ErrorCode原映射迁whole：NoSession/InvalidRequest/ItemUnavailable/CannotRepair/InsufficientGold→10，DailyLimitReached→0x73，MissingEntitlement/NoBrokenEquipment→4，其余仍throw ArgumentOutOfRangeException。原注释提failed commit 0x0A只是参照描述；当前实际handler无SQL/commit catch，本次不据注释造StorageFailure ACK。

领域内部保持整段只读源码：普通先规则/候选全量检查，再TrySpendGold和逐装备SetDurability；nondurable可0价不写item。auto先entitlement、候选与broken判定，再TryUseAutoRepair、逐装备修耐久、读取gold/完整entitlements/usage。usage通过原Direct PremiumUsageRecorded→UsageQuotaDomainService.CompareExchange→UsageQuotaChanged enqueue与耐久同原写事务，不转内部CMD/异步子作业，不另开unit，不让领域实例/交易collection越过unit寿命。auto修QuickSlot0..5及具名Worn槽，普通all较广原范围保持，不因一个logical而合并业务规则。

### 5. 原投影、拒绝与测试接缝

EquipmentRepairCompleted/handler整文件只读：当前recipient character/connection/IsInWorld检查保持，一次preparedbatch先RepairItemResponse，再仅当Entitlements非null时PremiumServiceStateMessage；普通不凭空增加Premium，auto保原Usage与PreparedAt。RepairAllSpace原样透传，即普通nullslot的PersonalCargo请求仍保ACK list值，不把list值误当domain all扫描范围。原decoder/encoder4纯测试及所有byte向量整文件不改。

成功投影的普通composer/actualencoder异常依原NotificationBus逐target Warning+Failed隔离，caller原忽略结果；普通和auto各取对应真实故障，核已提交gold/durability/usage/events仍在、无半batch/ACK、无意外close/fence/重做。不要为了typed终局把Failed变成throw。成功clock在投影前的异常若实测是外抛则按原默认保护/来源关闭记录，不能把time/callback/lookup冒actualencoder许可。

直接失败ACK取至少两种真实可达分支：普通8B QuickRepair=1（session/world仍current，未Write）与domain内正常返回的普通InsufficientGold或auto MissingEntitlement/DailyLimitReached（Write已Committed）。在真正旧/新actualencoder分别注唯一固定cause：原raw与新默认PostCommitPublicationFailure外壳差异明确单列，核InnerException Same、完成态/HasCommittedUnit、空selected参与者/attempts、完整资产、ACK0/无batch、Single physicalsourceclose且Assert.Same/ReferenceEquals、Root/Parent/Depth/Source/SourceConnection/CommandType日志及EndedAJob/nofence。pre无Write保持raw Same。无session/不在world静默不强求不存在的编码故障。Task21～23的方法可复用但不能替本族旧新实证；新协议/关闭/围栏或真正error consumer变化先报主控，不新增第四raw许可。

EquipmentRepairTests原helper迁registered wire，所有现有quote attribute/enchant独立、gold/durability、all QuickSlot/Worn、nondurable0写、insufficientGold、pet非目标、auto free/bag不变、missing/expired entitlement/noBroken、6th/7th/dayreset、同角色双请求和两角色独立quota用例保留。原stale fine用例改真实IEquipmentRepairDomainService.RepairAsync＋原显式DomainTransactionScope/Commit，保旧session拒绝与旧gold/零通知；不为测试留fine兼容。另在新flow用只读session query proxy返回真实旧session，IsInWorld询真实切角registry，验证原handler阶段静默/零资产/零unit；wire无旧session字段的局限明确。不能把domain stale能力说成真实wire携带了旧身份。

SlotBoundary只迁RepairItemCommand.Slot为whole.Input精确RepairItemRequest、request.Slot是GameSlot?、domain RepairAsync.slot是BusinessSlot?；保其余协议/六接口/receipt及实际BadBusinessModel反例。注册判据核exact logical/CLR/Direct/Protocol、唯一whole、NonEffect、无exits和旧fine/handler从assembly/注册缺席。无新只为反射造生产private方法。

NonEffect同rawseq按当前事实再次执行业务，不套去重或旧ACK重放：普通修好后再发按当前0缺损/成本结果；auto成功后无新broken再发是NoBrokenEquipment不耗第2次，外部合法seed再次broken后同seq可以再耗一次，直到原额度限制。完整metadata/body/ordinal/fresh与真实资产/次数共同证明，不能只断言调用数。

### 6. 真实SQL、nativeCommit、资产和mutation

先旧后新分别取普通与auto原一次写事务的真实action SqliteException19/1811：可沿原BEFORE UPDATE OF item_core trigger，但明确它不是nativeCommit；普通已扣gold及auto已写usage/Direct事实/变化事件等先前成果必须完整回滚。保原Failed_item_commit测试方法/目的，只在Result澄清实际阶段。

再在同fixture实际SQLite participant建parent/child＋DEFERRABLE INITIALLY DEFERRED FK，用目标item_uid限定AFTER UPDATE OF item_core触发missing-parent child，seed之后才启；PRAGMA foreign_keys=1、child实际产生、action正常返回/逻辑写已stage、真正SqliteTransaction.Commit报19/787、CommitFailure.Cause与外抛cause Same、Completion ConfirmedRollback和native stack必须可核。普通与auto各一个可达真实commit：普通gold与耐久、auto quota/耐久/Direct PremiumUsageRecorded/UsageQuotaChanged同一open非只读DomainTransaction及同database participant，全部资产/usage/events/delivery回滚；去trigger同seq按当前事实成功而非ledger replay。若某item mutation先暂存在领域store待Prepare刷SQL，就按真实flush时点观察child/暂存结果，不谎称在action SQL已生效，不用fake Prepare/controlled participant替nativeCommit。普通DDL/局部observer可自治收敛，不能改Persistence/核心或加第二fallible participant。

局部forwarding observer记录真实domain、Direct事实/事件、usage与clock的同Tx引用/先后；clock用于通知时应无当前未完写Tx，而premium内部clock仍在原Tx。拒绝观察不得额外查SQL而人为enlist。完整快照至少items/item_slots全列82B core/BLOBhex/null、characters/character_stats、container/cargo参数、account_premiums、character_daily_counters全字段、domain_events/domain_event_deliveries；保characterID/key/period/value/last_consumed_at、entitlement end_time和事件payload/sequence/FK，不只计数。

same-run rollback全列exact零豁免。跨run只允许实际来源具名的SQL CURRENT_TIMESTAMP列（先核owning source、actual run窗口及行关联/先后）及EventService三GUID字段非零的每case跨phase双射/DeliveryFK/batch/origin关联；其它列、fixed TimeProvider决定的quota时间/PreparedAt、payload/bytes/序列精确对应。不得泛忽略时间/BLOB/事件ID或把native失败暂存和retry GUID合并。原新ordinary/auto ACK与Premium帧顺序/长度/body exact，不能用“时间字段”跳过一大片帧。

行为mutation选择whole里一个真正危险的branch替换：临时将automatic action调用AutoRepairAsync改为原普通RepairAsync(session,null,token)，其余判定/普通路径不动。具备有效auto权益、gold100000和broken piece的同一真实wire应原样免费修复并usage+1；mutant会按普通价实际扣gold且不耗quota/无原Premium状态，资产oracle必须真实RED。保普通single或ordinary-all可达正控，记录actualgold/usage/core/events/frames后再assert，不把fixture/编译/timeout当RED。finally逐byte还原whole，核SHA/实际diff只有该替换，再同目标全GREEN才freeze；最终实现如代码形状不同可等价收敛本地mutation拼写，不改保护的业务事实、不动领域服务或保留测试开关。

### 7. 最终freeze与正式核验

focused覆盖原强case和本族所有required证据后freeze9源＝8final存在＋1deleted before SHA，另记readonly源/原case声明参数/驱动或断言变化说明；检索旧fine/caller清零，仅允许显式缺席断言字符串。正式后再改受影响源/测试须新freeze并重跑相关，不拿旧绿掩新源。保source-final checks及全部诊断，不覆盖输出prefix。

在require_escalated可用环境运行本Task新runner/native，保存cwd/HEAD/command/UTC/exit/elapsed/resources/完整console/DFO路径。保持原xunit并行、5m、Skip及自然结束，不TERM活跃native或父runner：

```text
dotnet build Server/86JP.Server.slnx -c Release --no-incremental
dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

build exit0/0warning/0error；test分别Application.Tests、Protocol.Tests、Networking.Tests无filterfull。App只容许原两Dimension精确display Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource、All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot及InvalidDataException／Dungeon7100/maze0/cell(1,0)/entrance Left。其它失败先取完整身份/stack，按AGENTS登记规则实际复跑确认，不调parallel/timeout/Skip或把Connection refused归baseline。

同freeze Host/HttpApi/Infrastructure/Persistence四GameplayAssertionRecorderControlTests fresh各1/1；DFO原violations.py CLI保74records、7actual controls、原expected逐类/数及程序集集合、outside/exe/orphan0/新intentional0，exe0仅观察；Golden15逐byte同时等HEAD和f01dc226，Characterization10逐display全pass/0skip。RepairItemRegistrationTests4原向量保持。Counter以Task23 formal01三full和Task20其余1800实际库存逐(display,status,multiplicity)，原8947保留，N-D每项rename/status/删除依据写明，预计D0/新增全pass，不拿预期替实际；四controls不重复算业务N。数量/套件时长不证明单CMD2秒。

### 8. Capture、两ID闭账与交接

finalfreeze fresh after source/runtime/purefamily/exits。预期173logical/201cmd（onefine→onewhole总数不变）、33Protocol＋1Internal＋167LegacyFine、139Tx＋49Direct＋9ReadOnly＋4Workflow、message44Protocol/129Legacy、telemetry14；actual逐值核只65559 route、一fine→whole，其它command/family/route和原三许可所有owner完全等HEAD。精确binding为(RepairItemRequest,65559)、Direct/Protocol/无RegistrationExits，不因auto增加新logical/CMD。

canonical仅准改两ID：message:65559、command:Dfo.Server.Protocol.Game.RepairItem.RepairItemCommand。说明原一次Tx→ordinary Write/空selected，普通与auto分支、正常拒绝Commit/异常Abort、Direct次数事件/耐久同事务、成功clock/批次/Warning+Failed及默认ACK壳差异的实际证据。其余baseline/original/rows/groups逐值等HEAD，md旧前缀只append。预计pending115message/167fine/72source/5protection/8transition，由原validator实测，不提前关source/protection/transition。

按executing-tasks在checks通过后追加Result R1与literal Outcome: done，原计划前缀bytes/SHA明确；Result前缀/manifest自身避免循环。实际preclose/strict、原8selftest及8份物理坏副本按原semantic逐项拒绝；临时坏副本可改变其原需用行，canonical仍仅本两ID，不改tool/基线或弱化为只核两ID。完整manifest列全部source/evidence/scripts/log/Task/两ledger的bytes/SHA及删除HEAD身份，精确等Git NUL changed+untracked；完整nonowned map排仅两actual canonical后逐SHA原样，readonly域与旧证据不改。

Result交代真正before时点、原新ordinary/auto/限额/同seq/clock/Direct事件对应、两类ACK壳及cause/close/chain、成功投影Failed、action与nativeCommit差别、完整资产/usage/events、有效mutation和恢复、原测试接缝/数量账及正式范围、capture/strict/manifest/nonowned和局限。全部完成后停止所有共享writer/native，由主控派fresh required独立结果快审并消费后checkpoint。RecordedOutbound证明登记和精确关闭条目，不冒真实socket关闭/A21实机；Astra、T-012测量、Spec最终核验/封存和用户分支收尾仍后继。

### 9. 自治收敛与返主控

局部helper/observer/fixture/nativeDDL条件/测试driver/私有结果及mutation表达式可按真实接口自治收敛并保diagnostics，不每次新增门。不得把auto改普通、偷偷加payload/世界门、所有拒绝回滚、把Failed改throw、原SQL外抛变10ACK或扩raw/P3许可；不能为通过断言保旧fine/造隐藏wire/改domain或共享支持。真正before推翻原事务/错误消费/保护边界，或必须扩9路径/WHAT/共享接口/机制时，带最小完整实证返主控；不可达native须给实际阻碍与可行路线，不降格假成功，不自选Spec终局。
