# TASK-23 — DeleteItem 三操作归一完整 NonEffect 协议 CMD

## Task Unit Contract

- Revision: 1（同时标识本契约与详细计划）
- Outcome and references: 交付 [Plan P4](../plan.md) T-004 的下一最小单元：logical65554／wire0012 的精确 DeleteItemRequest 归唯一 GameDeleteItemCommand，吸收原丢弃、技能材料消耗和效果材料消耗三个 fine，删净旧命令、handler、注册与实际 caller。保留原一次普通事务、任务物品重算和材料事件同事务、原前置／正常拒绝及投影次序。覆盖 [Spec B3](../spec.md) RQ-001、003～009、012、014、015 及 RQ-013/017 本簇义务，AC-001～005、007/009/011 局部结果、F-34/F-112；不宣告 T-004 或 Spec 完成。
- Owned boundary: §2 的 13 个物理生产／文档／测试路径、本 Task、verification/evidence/task-23-inspection/ 和 verification/logs/task-23-* 新材料；canonical migration-ledger.json/md 只改 §8 四 ID。原通知、消息、codec、domain／rule／content／持久化／公共 support／共享执行机制只读；不引入 Main、footprint、ledger、replay、attempt、raw 许可或 P3 恢复。
- Required checks: 真正产品前源身份、fresh capture／pure family 和原真实 decoder 三操作 oracle；新 whole 同 provider 对应复跑。保原强用例及超 wire 形状的材料批量能力，分清无事务拒绝、正常返回拒绝 Commit、异常 Abort 和成功投影 Failed 隔离；实际 SQL action、同 participant native Commit、完整资产／任务／材料／事件及 cause。有效可编译材料数量 mutation 须真实资产 RED、可达正控和逐 byte 恢复同目标 GREEN。最终同 freeze Release0警告0错误、App／Protocol／Networking 三个无 filter full、四 fresh controls、原8916逐 display/status/multiplicity N-D、原 DFO74/7、Golden15、Characterization10、capture／四 ID／strict／原八 selftest 与八物理坏副本／完整 manifest 与 nonowned SHA。
- Dependencies: 仅 [TASK-22](TASK-22.md) r2／Result R1／fresh Independent Quick Review R1 approved／Controller Consumption，已在 7c9f39c1d974270f0005147c99daeee5861c9a57 committed done。当前 B3/P4 及 matching approved gates 继续有效；Spec baseline 仍 10a7218a779493494b138bd6b43cc0fc1a84aa6a，不重开 Task01～22 或 A。
- Result review: required — 丢弃后漏任务重算、材料预检／扣款／事件拆事务、把多 cost 测试缩成单 cost、拒绝门或时钟移位、把 Failed 当外抛均会改变真实资产或客户端结果。fresh 独立结果快审须实读原新分支、全列 SQL/native 回滚及 mutation、超 wire domain oracle、测试接缝与数量账；主控消费后才 checkpoint。不另设独立 Task-plan gate。

## Detailed Plan

### 1. 身份与库存

唯一 workdir 为 /Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021，branch task/refactor-session-20261021，接手 HEAD 7c9f39c1d974270f0005147c99daeee5861c9a57。主控已核前22 Task committed done、23唯一未用、DAG／clean／index空；执行者再核实际身份及唯一 writer。当前无 .codegraph/，按 rg 定位；若接手已存在索引则遵守 AGENTS。

沿 governed／long-running／task-units 执行，继承中文、本地 checkpoint／Co-Authored-By。执行者不 stage/commit，不 push/PR/merge/tag/Release/版本/远端 issue/游戏进程/部署/cleanup，不建 Task24；旧 Task、A seal、既有证据只读。主控 readiness 后另授权 fresh producer，本计划不自批结果。

当前库存 **8916＝8896pass＋2准确旧Dimension fail＋18skip**。Task22 final03 的新鲜三 full 为 App5143／Protocol1774／Networking199，合7116；另1800＝Host203＋HttpApi57＋Infrastructure976＋Persistence484＋Pvf80 为 Task20 实际 full 库存，含 Infra2skip。此 slice 不改共享 core，最终三 full＋四 fresh controls；不冒称八 fresh full。套件资源只观察，单 CMD≤2秒留 T-012。Task21 旧 TCP Connection refused 根因仍未证，不作登记偶发或已修依据。

### 2. 精确边界与 caller 闭合

13物理路径＝7生产／文档＋6测试，预计8改／2新／3删；保原 BOM／CRLF／mixed 行尾，按 bytes 定点改，不整文件格式化。

| 相对 workdir 路径 | 允许变化 |
|---|---|
| Server/src/Dfo.Server.Protocol/Game/DeleteItem/DeleteItemHandler.cs | 变为只交原 Dispatch／Input 的一次 bus 薄入口 |
| Server/src/Dfo.Server.Protocol/Game/DeleteItem/DiscardItemCommand.cs | 删除 fine、handler 和只为旧 fine 服务的 DeleteItemOutcome；本用例需要的私有结果收进新 whole |
| Server/src/Dfo.Server.Protocol/Game/DeleteItem/ConsumeSkillMaterialsCommand.cs | 删除原 fine／handler |
| Server/src/Dfo.Server.Protocol/Game/DeleteItem/ConsumeEffectMaterialsCommand.cs | 删除原 fine／handler |
| 新 Server/src/Dfo.Server.Protocol/Game/DeleteItem/GameDeleteItemCommand.cs | 一个 typed whole，吸收三操作、原 clock／domain／任务重算、ordinary Write、日志／ACK／投影 |
| Server/src/Dfo.Server.Protocol/ProtocolModule.cs | 只65554精确 route 与三 fine→一 whole；原 decoder／encoder／通知注册不变 |
| AGENTS.md | 现状加入 DeleteItem 三操作及普通事务／任务／材料事件／原投影；不写过程或引用 .rks-agent |
| Server/tests/Dfo.Server.Application.Tests/Inventory/ItemDiscardTests.cs | 原协议 helper 改真实 wire；旧 fine stale 接缝按§5迁移，保原强业务目的 |
| Server/tests/Dfo.Server.Application.Tests/Inventory/SkillMaterialConsumptionTests.cs | 原 wire/helper 改真实入口；多 cost、重复 location、旧 session 用例保真实 domain＋原事务能力，不丢批量证据 |
| Server/tests/Dfo.Server.Application.Tests/Inventory/InventorySlotBoundaryArchitectureTests.cs | 仅迁 discard 两处旧 fine 判据，保 wire／business 边界及实际拒绝坏例 |
| 新 Server/tests/Dfo.Server.Application.Tests/Inventory/DeleteItemProtocolCommandFlowTests.cs | 本族真实旧新 wire driver、局部 observer、完整资产／SQL／native／projection／metadata／同序号／mutation；可局部 helper 供本 App 三文件复用 |
| Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs | exact CLR/logical/whole、Direct/Protocol/无 exit、三旧类型及 handler 缺席 |
| Server/tests/Dfo.Server.Protocol.Tests/Game/DeleteItem/DeleteItemCodecTests.cs | 只原31行起三个 unmapped-slot case 的旧手造多依赖 handler 驱动迁移；其余 codec／composer／字节向量和强断言不改 |

实读生产 caller 只有 DeleteItemHandler、ProtocolModule；DeleteItemOutcome 只在旧 fine／handler 使用，无独立公共领域消费者。材料 domain 另被 Application/World/IllusionTower/IllusionTowerDomainService 与 Application/Events/JoustDomainService 直接消费，故 **IMaterialConsumptionDomainService、MaterialCost／Debit／Result、domain API 不改**，不把多 cost 能力删成 wire 的一项。

只读：Messages.cs、DeleteItemMessageDecoder.cs、DeleteItemResponseEncoder.cs、ItemDiscardCompleted.cs、MaterialsConsumed.cs、AccountMaterialNotifications.cs、GameInventoryDelivery.cs；所有 Data、Abstractions、ItemDiscard／MaterialConsumption domain/rules、quest 服务、Persistence/schema、CommandBus/WorkUnits/EventService/NotificationBus/发件箱/ledger/attempt 和公共 ShopFixture/RecordedOutbound/TestGameplay。现存 Networking .cs 的 Gameplay inbound 消费只涉及 Wedding／Exit，SlotMapping 用新 Sell 名称，无 Delete caller；本 Task 不预改 Net 文件。若实际新发现 caller／必须新文件或共享修改，带实证交主控，不默扩清单。

### 3. 真正产品前证据

在任何产品／AGENTS 修改之前，核上述既存源等 HEAD、新 whole absent，存最小 before bytes／SHA／行尾、HEAD／branch／index／UTC／命令及全 tracked nonowned SHA 库存；新 Task／新证据另归属，不复制全仓/bin/obj。以原 migration-ledger.py 向新 capture-before-01 实捕 source/runtime，配同 pure family/exits 方法：当前实际应为173logical／203cmd、31Protocol＋1Internal＋171LegacyFine、143Tx＋47Direct＋9ReadOnly＋4Workflow、message42Protocol/131Legacy、telemetry14，必须 actual 核实。

产品前仅先加局部测试 driver／observer，通过 **原 registered decoder→dispatcher→原 fine→SQLite→同 provider 实际 Registering/outboxes/registration encoder** 实跑。body 精确19B：space@0、entries@1、op ushort@2、slot ushort@4、item int@6、count int@10、reserved uint@14、safe byte@18，selector1/type0012；使用原 metadata.WithRawBody、physical frame counter，观测 Sequence／Ordinal／Fresh／长度／SHA。真实生产入口不能包 TestGameplay 形成 self-wait；TestGameplay 只用于 fixture 的准备／直接 domain／纯 composer。

至少取得 op1 丢弃任务最后一件／partial pile／装备 UID、op2 carried／shared、op3 shared 剩余及耗尽、前置拒绝、域内正常拒绝、SQL action／native Commit、成功投影故障和直接拒绝 ACK 编码失败的真实旧 oracle。保原相关测试旧声明与参数；超 wire 多 cost oracle 同时先取。每 case 留完整 output/meta、input、调用数、cause引用、同事务／Completion／stage、资产／事件、帧 bytes／batch／精确目标／关闭。先保原最小 driver/source proof/before manifest，再迁产品；不得迁后重建 before，已存在输出不覆盖。fixture 列名／观察器／编译诊断原样留后可自治收敛，不冒行为 RED 或另开批准门。

### 4. 唯一 whole 与原次序

采用 GameDeleteItemCommand(NetworkDispatchContext Dispatch, DeleteItemRequest Input) : IProtocolCommand<bool>，Message／Frame 引用原 Input／Metadata。三个 op 是一个精确 request/logical 的分支，不拆多个 whole、不造隐藏批量协议字段或兼容 fine。bool 仅表有无业务成功，不当持久提交证明。handler 只一次 bus，whole 只注入原领域／查询／槽映射、TimeProvider、ICommandWorkUnits、ICommandProjections、IGameCommandOutbox、日志；无 sender／bus／队列／行网关／内容或 locator。

三个旧 fine 均为 Transactional＋IAccountCommand，**没有 ISelectedSessionCommand／IRequestSettlementCommand，也无 recorder**；domain 不自开写事务。因此有原 fine 的分支各对应一次 ordinary WriteAsync(context, new(name, []), action)，显式空 selected 参与者保原范围，普通 unit 无 Main／NonRequest footprint。[] 不代表没有 SQLite／事件 participant，不豁免适用 login／connection authority／request 健康检查。

1. 原 handler 先按 op2/3 分流材料；未知 op（非1/2/3）不映射、不查 session、不进 Tx，直接原 Slot＋CannotDelete ACK。
2. op1 先 ToBusinessSlot，**仅此映射表达式**窄 catch InventorySlotMappingException：保原 Info 和原 wire Slot 的 CannotDelete，立即返回。ToGameSlot 在 catch 外。再 FindByConnection，session／online 缺少用 NoSession 结果，无 Write。不得新增 ReservedZero／SafeModeSkipIndex 校验或把其它异常并入这条拒绝。
3. op1 有 session／online 才一次 Write：原 discards.DiscardAsync(session,businessSlot,EntryCount,OperationType,ItemId,DeleteCount,clock.GetUtcNow(),token)，clock 保在原 fine 的事务 action 参数求值处；有 receipt 且 **carried item space** 才同一 action 继续 quests.ReportAsync(account,char,[new QuestHoldingsChanged()])，保存 NotificationSnapshot。共享材料丢弃不凭新猜想增加任务报告。无 receipt 的正常拒绝仍 Commit，不一律 RequireRollback。
4. op1 unit 完整结束后，成功 Info 字段及 TheseCharacters([receipt.Session.CharacterId])／ItemDiscardCompleted(receipt,questSnapshot) 原序保；拒绝原 Info 字段后以 ToGameSlot 的 responseSlot 直接登记 CannotDelete。只移动编排，不缓存当前世界。
5. op2/3 **先 FindByConnection**，result 初始 InvalidRequest；只有 session存在、EntryCount==1、ReservedZero==0、online存在才映射 Slot。仅 ToBusinessSlot 的 InventorySlotMappingException 映 UnmappedSlot；不吞 domain／SQL／取消。映射成功才一次 ordinary Write 调原 materials.ConsumeAsync(session,[new MaterialCost(location,ItemId,DeleteCount)],token)，两 op 同一真实能力，无新 `Effect`／Main 含义。
6. 原材料 domain 在本 Tx 内先 IsInWorld／请求有效性／逐项全部来源预检，再逐项 ConsumeAsync＋Enqueue MaterialConsumed；不把预检、扣款、事件拆开或移至事务外。所有正常 MaterialConsumptionResult 拒绝仍正常 Commit，无新 RequireRollback 或 world／OnlineId 门。
7. 材料 unit 后保原 Info 字段，Success 时用原 Debits.Single()，**DateTimeOffset.UtcNow 在事务外**构造 MaterialsConsumed，再 TheseCharacters 投影；不能顺手换 TimeProvider／挪进事务。失败只在 session==null **或** sessions.IsInWorld(session) 时以原 wire Slot 发 CannotDelete；session已失效则原样静默，不强补 ACK。SafeModeSkipIndex 原不参与决定。

普通 SQL／Commit／Prepare／取消／guard／typed 未知异常均按原默认 sticky 外抛，没有业务异常→拒绝的原 catch，故不借 Sell 的 ItemSaleRejected、P3 WriteResult 或任何新的 StorageFailure 映射。保存原 material／discard Info 的内容，类别随 whole 迁移；仅旧 fine 重复 Error 按 VC-B2 消除，业务日志不删。

### 5. 原投影与测试接缝

ItemDiscardCompleted／MaterialsConsumed 两 composer 保整文件只读，复查 character／connection／world。成功丢弃为 ACK→（共享材料有剩余时绝对材料 NOTI）→（有 questSnapshot 时任务进度）；材料为 ACK→绝对 carried 库存变化／清槽，或 ACK→shared 剩余资源；**shared 耗尽只有 ACK**，不加零资源 obtained UI。NotificationBus Resolve→逐 recipient Compose→一次 SendBatch 的边界保持，不把列表拆成数次 direct register。材料 PreparedAt 的 UTC 来源／事务外位置实观察，选无到期物品核原新帧 exact，不按“时间”忽略一片帧 bytes。

成功投影的普通 composer／actual encoder 异常仍逐目标 Warning＋Failed，caller 原忽略结果并正常完成，已提交资产不逆转／重试／fence／close；caller cancellation、外层 scope／lookup 等真正外抛仍走默认错误。至少各通知真实注错一次，核 cause、一次 batch 全失败、无半 ACK、完整已提交资产和原调用者处理。不要把 NotificationBus 捕获的 Failed 伪称 CMD 外抛，也不要为求 typed 增加 throw。

直接拒绝 ACK 至少按 **无 Write 前置拒绝** 与 **域内正常返回后的 Committed Write 拒绝** 分别取旧新 actual encoder 故障：cause／InnerException Same、原物理 source close、零 ACK、空 selected participants／attempts、HasCommittedUnit／Completion、完整链与 EndedAJob 实测。op1 和材料各选真实到达的正常拒绝，材料失效 session 的静默另证。若新默认 PostCommitPublicationFailure 包装不同，要明确列外壳差异及实际 error 消费，不 normalize“完全同异常”；Task21/22 只提供方法，不能替本族取证。实际新协议／关闭／持久化／围栏或错误消费变化先返主控，不新增第四 raw 许可。本族不存在原具名异常回滚后拒绝 ACK，不为凑三类复制 Sell 行为；SQL/native Abort 原无 ACK。

迁测试保所有方法／Theory参数和业务意图：最后任务物品重开目标、partial pile、装备 UID／InstanceValue 非数量、锁／不匹配／超量、无效 wire 形状、op2/3、shared／carried／耗尽、storage回滚、全部来源先验、部分扣款全回滚、重复 location 与旧 session。ItemDiscard 原 stale fine 可改为真实 IItemDiscardDomainService＋显式原 DomainTransactionScope，保 NoSession／旧槽数量／零通知；说明只证明 domain stale 能力。另由新 flow 在真实 old/new wire 用有界只读 selected-query proxy 返回真实旧 session，而 IsInWorld 询真实已切角色 registry，保 op1拒绝与材料静默／无资产变化。wire 不携带旧 session，不能把 proxy 说成网络身份字段证据。

SkillMaterialConsumptionTests 的两参数 Batch_validation_and_storage_failure_never_leave_partial_consumption 保 **[shared cost, carried cost]**：余额不足先验不扣 shared，后续 carried DELETE SQL失败时已扣 shared及事件一并回滚；Stale_session_and_duplicate_locations 保 [cost,cost] 与旧 identity。旧 direct fine 改真实 IMaterialConsumptionDomainService＋原显式 Tx／Commit／失败自然 Abort，原两个参数和材料事件／零通知目的保留，准确标为超 wire 的应用/domain 能力；不为测试保留 command、不造隐藏多 cost 输入，不把它们改成一条 wire 成功而丢掉能力。真正协议单 cost 路径另外真实 decoder 保护，直接 domain 测试不强改 whole。

架构文件把 discard 旧 Slot/NoGameAddress 判据迁成 whole.Input 精确 DeleteItemRequest、request.Slot 为 GameSlot、真实 IItemDiscardDomainService.DiscardAsync 的 slot 为 BusinessSlot，material Costs 仍 BusinessSlot；保原六接口、receipt、其它协议。已有坏 model 可复用同判据的真实拒绝；必要本地坏 model，不造生产 private 方法凑反射。DeleteItemCodecTests 三个 unmapped-slot 参数用本文件局部真实 whole 编排／登记驱动或本地同 provider 图迁旧构造，映射实际执行且 query/domain 不被触及；不能只 fake bus 抓 request 后宣称业务拒绝。原 encoder向量、纯通知 composer、19Bdecoder/18与20拒绝、safe字段向量均保原字节／断言，不改公共 fixture 或 csproj。测试只改驱动与断言接缝的差异分别列，不冒全部 assertions 逐 byte 未变。

新 flow 补 metadata 同对象／selector/type/sequence/ordinal/fresh/body长度/SHA、精确帧/目标/batch；op1保 reserved非零／safe0和1不新增拒绝。同 raw sequence 的 op1 与至少一个材料 op 从现有余量连续实际扣两次，耗尽后同 seq 按当前不足／不可用拒绝，不重放旧 ACK、不去重；证明无效果账本权限。

### 6. 实际故障、完整资产与 mutation

旧新各覆盖真实 carried slot partial UPDATE 或 full DELETE 的 action SqliteException19/1811，观察原 SQL 窄阶段和同事务 cause；保丢弃＋QuestHoldingsChanged 同 Tx，以及材料扣款＋MaterialConsumed enqueue 同 Tx。材料多 cost 的既有触发器原强回滚用例继续存在，不能用只在首写前失败的单 cost 冒已证部分扣款回滚。

另在同 fixture **实际 SQLite participant** 建 parent/child＋DEFERRABLE INITIALLY DEFERRED FK，用目标 item_slots AFTER UPDATE OF count／AFTER DELETE 限定 owner/list/slot 插缺父 child，seed 后再启。同 participant PRAGMA foreign_keys=1，action正常完成、child>0、资产／任务或材料事件已 stage 后，实际 SqliteTransaction.Commit 报19/787，CommitFailure.Cause 与外抛原 cause Same、Completion ConfirmedRollback，回滚全列 exact。覆盖丢弃任务重算与一个材料事件 route 的可达 Commit；两个材料 op共享 domain能力由 op2/3成功/拒绝及mutation另护，不无意义重复全部矩阵。去trigger同 raw seq 重试按当前事实成功，无 ledger replay。普通 trigger19/1811、controlled Prepare 或 fake participant 不当 native Commit；普通 fixture／DDL／观察方法可按实际源收敛，不改 domain API／Persistence 或换参与者。

用局部 forwarding observer 调原真实 domain／quest／event／clock，核同一 open 非只读 DomainTransaction 引用与原先后；不额外对原拒绝读 SQL 而人为 enlist、不调用 Failed／清失败、不加第二 Host/provider/outbox。捕捉 mutation／回滚的完整 actualassets 在断言前输出。资产至少 items全列／UID／82Bcore／扩展、item_slots全列／quantity／共享账号 owner、gold、实际角色／容器参数、character_active_quests及相关quest账、domain_events/deliveries/FK；SQL NULL保null，BLOB保hex，不忽略整表。same-run回滚全列exact。跨run仅可沿已证明 items.created_at/updated_at 的真实 UTC窗口／顺序，以及 EventService 三具名 GUID（event/batch/instance）按类型／phase双射、非零、delivery FK／origin／callchain关系校准；其它列/payload/time/数量原样，新增差异先找来源，不把任意 GUID／整时间列自动豁免。

有价值 mutation 选 whole 的 **op3 MaterialCost.Count 临时改成 DeleteCount+1，op1/2保持**；使用 shared seed2、请求1，原应剩1，mutant实际剩0／MaterialConsumed count2，完整材料账真实 RED，不能只靠返回bool或结构断言。op2同输入与op1普通丢弃作可达正控，原强批量／rollback checks仍保。原目标先GREEN，保存whole bytes/SHA；变异必须编译成功并实际执行到资产差异，编译／fixture／timeout不是RED。finally原bytes/SHA恢复，同目标和正控GREEN后才freeze；若实际代码结构不同，保持此最小数量风险可编译，不另造无意义结构变异。

### 7. 最终 freeze 与正式核验

focused 完成原强 case、旧新 wire 对应、超 wire批量/domain、直接拒绝默认包装、成功投影Failed、SQL/native、metadata／同seq、mutation恢复及注册/codec后 freeze13路径（10存在final＋3删除before SHA），附 readonly源／case声明／驱动迁移差异与非owned map。检索生产／测试旧三fine及handler/caller清零；保最小生产签名和whole绑定。formal后再改受影响源／测试须新freeze并重跑相关，不用旧绿掩新源。

新Task23 prefix/runner，FSEvents用 require_escalated 可用环境，保存 cwd/HEAD/实际命令/UTC/exit/elapsed/resources/完整console与DFO路径；原 xunit 并行、5分钟、Skip和自然结束保持，不TERM活跃native／父runner。先实际：

```text
dotnet build Server/86JP.Server.slnx -c Release --no-incremental
dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

build须exit0/0warning/0error；test分别 Dfo.Server.Application.Tests、Dfo.Server.Protocol.Tests、Dfo.Server.Networking.Tests，无filter。App只容许原 DimensionDungeon 两显示 Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource、All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot 的准确 InvalidDataException／7100／maze0／(1,0)／entrance Left；其它最终0fail。新错误先保完整身份/stack/输出再诊断，不把 Task21 TCP问题默作baseline，不能调并行／timeout/Skip求绿。

同freeze跑 Host／HttpApi／Infrastructure／Persistence 四原 GameplayAssertionRecorderControlTests fresh各1/1，保持原七actual controls。DFO原 violations.py CLI、原 expected74逐类/数/程序集（App62/Net7/Protocol1/其余四各1），outside/exe/orphan0，新增intentional具名说明；exe0只观察，不放宽expected。Golden15逐byte同时等HEAD和f01dc226，Characterization10逐display全pass/0skip，pure codec字节原样。

Counter用Task22 final03三个full＋Task20另外1800原full，按(display,status,multiplicity)逐case重算原8916保留、D原则0、新增全pass；rename/status/实际删除逐项给依据。保全部Theory参数及directdomain能力；四controls不重复入业务N。总8916+N-D和各项目pass/fail/skip、当前fresh与库存分明。测试数与套件耗时不当单CMD2秒证明。

### 8. Capture、四 ID 与交接

finalfreeze fresh after source/runtime/pure family/exits，**预期而非结果**：173logical；201cmd＝203−3fine＋1whole，32Protocol＋1Internal＋168LegacyFine；140Tx＋48Direct＋9ReadOnly＋4Workflow；message43Protocol/130Legacy；telemetry14。实际差分只能65554 route及原三fine→一whole，新精确(DeleteItemRequest,65554)／Direct/Protocol／无RegistrationExits；原三许可种类/所有owner及其他family/注册精确不变。

canonical仅准改四ID：

```text
message:65554
command:Dfo.Server.Protocol.Game.DeleteItem.DiscardItemCommand
command:Dfo.Server.Protocol.Game.DeleteItem.ConsumeSkillMaterialsCommand
command:Dfo.Server.Protocol.Game.DeleteItem.ConsumeEffectMaterialsCommand
```

各行说明原三个互斥1Tx→各branch一次ordinary Write／empty selected participants、正常拒绝Commit／异常Abort、任务与材料事件同事务、原投影批次／Failed隔离、真实before/after/SQL/native/mutation/测试接缝证据；不提前关其它source/protection/transition。其它baseline/original/row/group逐值等HEAD，md旧前缀保持只append。预计pending116message/168fine/72source/5protection/8transition，由原validator实际核，不拿算术充capture。

preclose后按executing-tasks追加Result与literal Outcome: done；原strict、原8selftest及8份物理临时坏副本逐项按原validate拒绝，保持原反例语义，不改tool/基线或换较弱四ID检查。最终manifest列全部实际source/evidence/log/脚本/Task/两ledger SHA/bytes及三fine删除before身份，与Git changed+untracked精确闭合；Task/manifest自身用清楚prefix避免循环。初始nonowned map除事先明确owned及canonical两ledger外逐SHA保持，排除必须在原map实证，不回写历史map或遗留证据。

Result说明真正before时点、op1/2/3原新证据和局限、两类直接ACK异常壳差异／实际原cause/close/参与者、投影Failed／batch、普通Commit与SQL/native回滚、完整资产／任务／材料／事件、同seq、原测试驱动／断言意图及N-D、有效mutation与恢复、finalfreeze与所有正式检查/capture/strict/manifest/nonowned。全部齐后停所有共享writer/native，交fresh required独立结果快审；不自批/stage/commit。主控消费后checkpoint及下一规划；Astra、最终Spec核验/测量/封存和用户分支收尾仍后继，RecordedOutbound不冒真实socket关闭／A21实机。

### 9. 自治收敛与返主控

局部helper／observer／私有结果形状／测试准备／native DDL条件／Protocol.Tests局部驱动/精确filter可按实际接口自治收敛，保诊断不逐次新设门。不得为方便保旧fine兼容入口、把批量用例缩例、加op1 reserved门／新world门、全拒绝Rollback、把通知Failed改抛异常或新增raw/P3许可。真正before否定上述提交点／边界，必须扩13路径／共享core，或WHAT／VC／错误消费／许可变化时，保最小源和完整实证交主控；native确不可达须带实际阻碍和可行路线，不降格假成功。不得用弱断言／假before求绿，不自选Spec终局。


## Result R1

- Outcome: done
- Contract: r1，原计划前26880B／SHA c3c3bbd23aa3bd9c3b6574dfa46968665eee6069e0afabf40f4305808c50436b 保持；只交付 B3/P4 T-004 的本 DeleteItem slice，不宣告 Spec 完成。Result 待 fresh required 独立结果快审、主控消费后才 checkpoint；执行者未自批、stage/commit 或 remote。
- Final identity: HEAD 7c9f39c1d974270f0005147c99daeee5861c9a57／branch task/refactor-session-20261021／index空。source-freeze-01.json 13物理源＝10present＋3deleted，SHA ba2c9719ca226abae2e74d2e250fb36bca8c02ad4ed79fda0f5942d9db53eaf8；普通native与capture/family父进程均自然完成，源冻后未再改产品／测试。

**交付与原提交点。** logical65554/wire0012 的精确 DeleteItemRequest 只归 GameDeleteItemCommand<bool>，Direct/Protocol、无RegistrationExits；原 Dispatch／Input／Metadata 保持。handler一次bus，三个旧fine record/handler/物理文件/注册及实际caller删净，旧专用Outcome变whole私有结果。op1先mapper窄catch、ToGameSlot在catch外、随后session/online；未知op不映射不查session不事务；op2/3先session/entries/reserved/online再mapper；未加op1 reserved/safe、world或online-ID规则。各旧fine仅IAccountCommand，所以各有效branch一次 ordinary Write([],Ordinary,None) 保原空selected参与者。clock仍在op1 action调用参数处，carried receipt后的QuestHoldingsChanged同Tx；材料原全部来源预检、扣款和MaterialConsumed同Tx，成功PreparedAt保持事务外DateTimeOffset.UtcNow。正常拒绝仍Commit；SQL/native/cancel/typed默认sticky外抛，无Main/NonRequest/footprint/ledger/attempt/replay/P3恢复/新raw许可，domain/API/Persistence/core完全只读。

**真正before。** 产品与AGENTS逐byte HEAD、新whole absent及fresh capture/pure family先取；新局部真实19B driver以原registered decoder→dispatcher→原fine→实际domain/SQLite→同provider outbox/encoder，在old-oracles04实际69/69及原codec01 11/11 GREEN，30结构化观察。before-manifest43项／SHA8729e846e2782a0caa37f94116f651ff54498c953e9530d22c5c12f32758d482封后才产品；before-driver.cs 保原实际执行源。初局部namespace/帧数量/任务暂存观察器诊断保raw，不冒行为RED。partial discard无任务变化原本只ACK；last quest快照先在domain store暂存，Prepare再刷SQL，nativeCommit失败回滚仍exact。

**观察账校正。** 最初非NUL git ls-files草图遗漏192个C-quoted中文路径/旧TRX，且排除ledger路径写错；原before-source.json未重写，保作诊断。产品前已用git ls-files -z完整逐路径current=HEAD取得before-source-02 SHA1db34ca048cc062cc2722b07b5531b1a508debbf1d3a49ca69ac41ce53f6f64c，14352＝owned现11＋nonowned14341，完整分区无交集，两actual verification/migration-ledger路径均存在且在map。最终仅排canonical2键，14339其它原tracked逐SHA不变，中文192均包括。原43封存项、计划前缀和源freeze当前全部身份重核，未改旧Task或旧证据。

**原新对应及资产。** task23-oracle-compare.py 对真正before和final同freeze的30组实比80全列资产快照，items/slots/characters/stats/参数/cargo、active quest/clear flags/acceptance/tracked、events/deliveries，BLOBhex/SQL NULL/UID/82Bcore不丢。same-run回滚全列exact无豁免；跨run仅逐items.created_at/updated_at实际UTC窗口/先后，以及三具名EventService GUID非零、跨phase双射/Delivery FK与batch/origin关系校准，其它列/payload/time/count/帧bytes精确相同，不合并native失败暂存和retry事件ID。原scope所有readonly通知/codec/domain/持久化/共享机制逐SHA保。

**实际故障与同事务。** discard任务重算、material事件各实际action fullDELETE触发19/1811和sameparticipant DEFERRABLE missing-parent child的真正SqliteTransaction.Commit19/787。actual open非只读Tx引用相同、FK1/child>0/action正常完成和资产/quest快照或material event已stage，CommitFailure.Cause与原cause Same、ConfirmedRollback；全资产/任务/事件原样恢复，去trigger同rawseq重做成功。没有fake Prepare/第二参与者/把触发器1811冒native787；没有为正常拒绝额外SQL观察而人为enlist。多cost仍用真实domain＋原显式Tx保[shared,carried]预验不足不扣、已扣shared并记录count2事件后carriedDELETE失败回滚shared/carried/events全部。

**拒绝与投影故障。** 原ItemDiscardCompleted/MaterialsConsumed整文件只读，每target一次preparedbatch，ACK→可选shared资源／carried绝对更新清槽→可选任务变化，shared耗尽只有ACK。两通知各actual encoder和composer普通故障保Warning同cause/Failed隔离、caller正常继续，Committed资产不逆转、无batch/半ACK/close/fence/retry。op1与op2直接拒绝ACK故障分别取pre无Write和normalCommitted原新oracle：pre原rawSame；normal由oldraw变为既有默认typed PostCommitPublicationFailure、InnerException Same，外壳差异明确单列，不normalize成同异常。四路各实际完整Root/Parent/Depth/Source/SourceConnection/CommandType日志、EndedAJob、empty selected participants/attempts、Completion/HasCommittedUnit、全资产不变/ACK0/physicalsourceclose Single＋Assert.Same及ReferenceEquals输出/nofence。原NetworkPlugins按EndedAJob降Debug/return及原outbox关闭政策只读，没有增加错误消费/关闭许可；VC-B2日志身份inbound:GameDeleteItem→GameDeleteItemCommand单列。

**原测试与真实入口。** 原ItemDiscard、SkillMaterial全部方法与Theory参数保，协议helper改真实registered decoder/dispatcher，wire本身不包TestGameplay自等待。原stale fine改真实domain＋显式Tx；多cost、重复location、stale identity保真实IMaterialConsumptionDomainService多cost API与原两参数，未保兼容fine或造hiddenwire。新singlecost真实wire另护op1/2/3 carried/shared/耗尽、partial/装备UID、reserved非零与safe0/1。stale proxy只读返回真实旧session且IsInWorld询实际切角registry，op1拒绝、材料静默；wire不带旧session身份局限明确。slot架构迁discard whole.Input精确request/wireGameSlot/domainBusinessSlot，六接口/receipts/MaterialCost/其它协议和真实badmodel拒绝保。Protocol codec仅迁原3unmapped driver为actualwhole/mapper/null其余能力强守、局部realoutbox；其余19B/18与20拒绝/安全字段/纯composer/encoder向量byte块保持。metadata同对象、selector/type/seq/ordinal/fresh/body19/SHA，实际batch目标帧序守；op1/op3同rawseq真实扣两次后按当前耗尽拒绝，无去重/旧ACK重放。

**有效mutation。** 仅op3 MaterialCost.Count临时DeleteCount+1，shared2/request1实际剩0且MaterialConsumed.Count2，在assert前完整落actualassets/event/frame；op1与op2正控pass，真实2pass/1资产RED，可编译而非fixture/timeout/结构RED。finally原wholebytes SHA446eec72435efbd5d355b0e1a69af82e3f7c7d95073ebe964cb8eac42eee89d3恢复，同3目标3/3GREEN，才freeze；最小mutation源码/原raw/恢复结果保留。业务保护通过完整资产和事件观察，可耐内部结构改变；结构搬迁未加无价值RED。

**最终正式核验。** source-freeze01上Release --no-incremental exit0／0warning／0error。原parallel、5m、Skip自然运行三个无filterfull：App5173＝5155pass＋准确原Dimension2fail＋16skip；Protocol1775pass、Networking199pass/0fail。Host/HttpApi/Infrastructure/Persistence四freshcontrols各1/1，原七actualcontrols齐。Counter以Task22 final03三full＋Task20另外1800原full逐display/status/multiplicity，原8916全保，N31/D0/rename0/statusChange0，总8947＝8927pass＋原Dim2fail＋18skip；其它1800明确库存而非八freshfull。两旧失败仍Fusion/All_six精确InvalidDataException7100/maze0/(1,0)/Left；Task21旧TCP refusal未知根因未据本轮Net0fail宣称已修。原DFO74/7control/原expected逐类逐数及程序集集合保，outside/exe/orphan0/新增intentional0，exe0仅观察。Golden15逐byte对HEAD与f01dc226、Characterization10逐display全pass/0skip。资源/套件时间不作单CMD2秒证明。

**capture与四ID闭账。** final fresh source/runtime/purefamily实际173logical/201cmd＝203−3fine＋1whole；32Protocol＋1Internal＋168LegacyFine，140Tx＋48Direct＋9ReadOnly＋4Workflow；message43Protocol/130Legacy、telemetry14。只65554 route与三fine→一whole变化，其它family/注册/三原许可及owners逐值原样。canonical只message:65554和三个旧fine ID闭，其它baseline/original/rows/groups等HEAD，md旧前缀保持只append；pending116message/168fine/72source/5protection/8transition。preclose实际通过；Result后strict原工具、原8selftest和8物理坏副本按原理由拒绝，再完整manifest/source/nonowned/Git changed＋untracked闭合，证据见ledger-strict/ledger-eight-bad/ledger-eight-bad-copies/manifest-r1/final-state-checks。

**边界与交接。** 本Result是本轮执行与检查完成，Task尚未committed done；写完全部manifest后停所有共享writer/native，交fresh required独立结果快审，主控消费后本地checkpoint。本Task无阻塞遗留；T-004其余迁移、T-012单CMD≤2秒测量、Spec最终审查/Astra/封存和用户分支收尾仍后继。RecordedOutbound不是实际socket关闭/A21实机；未运行游戏、部署/cleanup、remote/版本/tag或建Task24。
