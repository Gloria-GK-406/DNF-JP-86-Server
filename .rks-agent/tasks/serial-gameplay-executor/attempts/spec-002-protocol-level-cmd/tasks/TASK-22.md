# TASK-22 — SellItem 完整 NonEffect 协议 CMD 与具名拒绝回滚

## Task Unit Contract

- Revision: 2（同时标识本契约与详细计划；r1不可变前缀及本次有界task-rework见§10）
- Outcome and references: 交付 [Plan P4](../plan.md) T-004 的下一最小单元：logical65558／wire0016／精确SellItemRequest归唯一GameSellItemCommand，吸收原handler和一个fine的完整用例，删净旧fine/handler/注册/caller。保原一个普通事务、正常返回拒绝与抛异常回滚的区别、金币与物品原子性、原业务日志和单ACK通知。覆盖 [Spec B3](../spec.md) RQ-001、003～009、012、014、015及RQ-013/017本簇义务，AC-001～005、007/009/011局部结果、F-34/F-112；不宣告T-004或Spec完成。
- Owned boundary: §2的11个生产／文档／测试物理路径、本Task、verification/evidence/task-22-inspection/及verification/logs/task-22-*新材料；canonical migration-ledger.json/md只改§8的两ID。原domain/rule/content/存储、Data结果/receipt、通知composer、codec及共享执行机制/公共support只读。不加Main、footprint、ledger/replay、raw许可或P3恢复。
- Required checks: 真正产品前源身份/fresh capture/pure family与旧真实decoder协议oracle；新whole同provider对应复跑。至少区分无Tx前置拒绝、正常返回拒绝的Committed事务、ItemSaleRejected确认回滚拒绝和成功投影Failed隔离；真SQL action及同participant native Commit故障保原cause和完整资产。goldcap漏RequireRollback的可编译mutation须真实资产RED→逐byte恢复GREEN；其已完成r1证据保留。§10仅适配已证VC-B2日志CMD身份，用原精确mapping TCP目标GREEN及最终freeze03完整重跑证明。最终同freeze Release0警告0错误、App/Protocol/Networking三个无filter full＋四fresh assertion controls、原8900逐display/status/multiplicity N-D、原DFO74/7、Golden15、Characterization10、capture/两ID/strict/原八坏副本/完整manifest与非owned SHA。
- Dependencies: 仅 [TASK-21](TASK-21.md) r1／Result R1／fresh Independent Quick Review R1 approved／Controller Consumption，已在a6f19c44f0b3a539ca08100aadd76146c0411512 committed done。B3/P4及matching approved gates继续有效，Spec baseline仍10a7218a779493494b138bd6b43cc0fc1a84aa6a，不重开Task21或A。
- Result review: required — catch移入普通Write后漏回滚会产生拒绝ACK22却保留部分加金；把所有普通拒绝改回滚、把投影Failed改抛异常、删弱BusinessSlot判据或真实PVF coin断言也会改变既有结果。fresh独立结果快审须实读三路拒绝/原事务与日志、真实旧新SQL/native资产及mutation、测试接缝迁移和数量账，并独立审定新增Net日志身份适配仅一literal/原Error过滤和强断言保留、正式02失败与03最终证据不混同，主控消费后才checkpoint；不另设独立Task-plan gate。

## Detailed Plan

### 1. 接手身份与当前库存

唯一workdir为/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021，branch task/refactor-session-20261021，初始接手HEAD为依赖检查点a6f19c44f0b3a539ca08100aadd76146c0411512。主控初期已核前21Task committed done、22未用/唯一ID及DAG；当前r2延续同Task，HEAD未变、index空，但已存在r1授权产品/测试及正式失败证据，不能冒初期clean。producer已确认所有writer/native自然结束并停止；主控readiness后执行者再复核当前身份/owned范围和唯一writer，不覆盖其它Task。当前无.codegraph/，用rg；若接手时已有索引则遵守AGENTS先CodeGraph。

沿routing的governed/long-running/task-units执行，继承中文/本地checkpoint及Co-Authored-By；执行者不stage/commit，不push/PR/merge/tag/Release/版本/远端issue/游戏进程/部署/cleanup，不建Task23。既有Task01～21、A seal、旧证据只读；主控readiness后另授权fresh producer，不因本作者只规划而新设用户停点。

当前总库存为**8900＝8880pass＋2准确旧Dimensionfail＋18skip**。最新Task21三full为App5128、Protocol1773、Networking199，合7100；另**1800＝Host203＋HttpApi57＋Infrastructure976＋Persistence484＋Pvf80**为Task20实际八full库存，含Infra2skip，不冒称本Task八freshfull。此slice不改共享core，最终新三full＋四fresh controls即可；套件耗时/内存只观察，单CMD≤2秒留T-012最终测量。原Task21 Net01两个Connection refused、隔离2/2与final02全绿仍根因未证，不能当登记偶发或已修依据。

### 2. 精确路径与闭合caller

11物理source＝5生产/文档＋6测试，预计8改/2新/1删；r2仅比原10路径新增一个既存Networking测试文件。保原BOM/CRLF/mixed行尾，按bytes定点改动；不顺手格式化整ProtocolModule或改readonly源。

| 路径（相对workdir） | 允许变更 |
|---|---|
| Server/src/Dfo.Server.Protocol/Game/SellItem/SellItemHandler.cs | 只保薄入站handler一次bus；原日志、错误映射及编排归whole |
| Server/src/Dfo.Server.Protocol/Game/SellItem/SellItemCommand.cs | 删除原fine record/handler，所有实际caller与Transactional注册一起消除 |
| 新 Server/src/Dfo.Server.Protocol/Game/SellItem/GameSellItemCommand.cs | 精确typed whole、原判定/clock/domain、普通Write与窄拒绝回滚、原日志/ACK/投影 |
| Server/src/Dfo.Server.Protocol/ProtocolModule.cs | 仅65558 route及一fine→whole；原decoder/encoder/notification注册不变 |
| AGENTS.md | 当前已接线增加Sell，准确描述普通事务/窄回滚及原投影；不写过程或指向.rks-agent |
| Server/tests/Dfo.Server.Application.Tests/Inventory/ItemSaleTests.cs | 原全部方法/参数/资产及业务日志意图保留；手造handler/旧fine/stale驱动改真实wire |
| Server/tests/Dfo.Server.Application.Tests/Inventory/KnightCoinShopTests.cs | 两真实PVF coin case仅Sell驱动/receipt接缝改wire，保购买、价差、金币和数量断言 |
| Server/tests/Dfo.Server.Application.Tests/Inventory/InventorySlotBoundaryArchitectureTests.cs | 仅Sell旧fine断言迁typed Input＋真实domain BusinessSlot边界及能拒绝坏例；其它协议判据保 |
| 新 Server/tests/Dfo.Server.Application.Tests/Inventory/SellItemProtocolCommandFlowTests.cs | 本簇before/after raw driver、观察器/SQL/native/三类拒绝/投影Failed/metadata/同seq/mutation；可有本地helper供上述旧测试复用 |
| Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs | exact logical/CLR/whole、Direct/Protocol/无exit、旧fine缺席断言 |
| Server/tests/Dfo.Server.Networking.Tests/Integration/InventorySlotMappingFailureIntegrationTests.cs | r2唯一新增物理路径：仅WaitForErrorAsync内将日志匹配literal改为Gameplay GameSellItemCommand failed:，其它所有bytes保持；精确边界见§10 |

实读caller：生产只SellItemHandler和ProtocolModule；测试direct fine在ItemSaleTests旧session段、KnightCoinShopTests两参数case，架构文件有两处类型断言。ItemSaleTests另有直接IEventService.ProjectAsync旧receipt测试，保其纯投影目的，不强改成whole。

**全只读**：ItemSaleCompleted.cs（独立通知record/composer整文件）、Messages.cs、SellItemMessageDecoder.cs、SellItemResponseEncoder.cs、Protocol.Tests/Game/SellItem/SellItemCodecTests.cs；Data/Inventory/ItemSaleModels.cs里的ItemSaleResult/ItemSaleReceipt已经有正确归属，不搬迁。只读ItemSaleDomainService/RuleService/所有Abstractions/content/Persistence/schema、CommandBus/WorkUnits/ledger/attempt/outbox/NotificationBus/EventService，以及公共ShopFixture/RecordedOutbound/EffectRequestTransport。无需新的protocol helper/receipt或公共测试能力；必要新增caller/文件/core变化先交主控，不能默加白名单。

### 3. 真正产品前证据与原route

本段r1的真正before已实际封存，35项身份见§10；r2只保存新增Net路径改前身份，不重跑或重造原before，不把下文改前时点冒当前状态。原生产before流程为：先在产品/AGENTS逐byte等HEAD、新whole absent时，保存上述既存源最小before bytes/SHA/行尾及全tracked非owned SHA库存，记录HEAD/branch/index/真实UTC/命令；本Task与新证据另归属，不复制整仓/bin/obj或整Task21。用原migration-ledger.py在新capture-before-01目录实捕，并配同pure family/exits读取方法：起点173logical/203cmd，30Protocol＋1Internal＋172LegacyFine，144Tx＋46Direct＋9ReadOnly＋4Workflow，message41Protocol/132Legacy、telemetry14。actual source/runtime/whole absent一起证明before，不迁后重建冒先前。

产品前只先加本地真实13B body encoder/registered decoder→dispatcher driver与最小observer；GameInboundFrameCounter按原physicalcontext计Ordinal/Fresh，Metadata.WithRawBody保长度/Hash。可以调用公共既有SendRaw，但不改其Body switch/fixture或绕同provider Registering/actualencoder。保原ItemSaleTests全部、KnightCoinShop两个PVF参数及相关架构测试旧结果。pure Sell codec仍原文件/原向量，不改以迁就driver。

旧协议至少实跑：成功pile sale及装备UID/82Bcore、单ACK；sum/无session前置拒绝；正常domain返回拒绝；cap夹取后ItemSaleRejected确认回滚；action SQL与真实native Commit；成功projection实际encoder/composer失败；三种拒绝的直接ACK actualencoder故障分别观察。每case保input/metadata/实际调用次数、原cause引用、交易Completion、是否已提交、所有资产/批次/关闭/原参与者范围，不能先把不同拒绝都写成回滚或raw。最小原driver、sourceproof、原输出/meta封before manifest后再迁产品；新输出总用新编号，存在即不覆盖。普通fixture列名/种类/observer编译诊断保raw后可自治收敛，不作行为RED或额外gate。

### 4. 一个whole与原普通事务

用GameSellItemCommand(NetworkDispatchContext Dispatch, SellItemRequest Input)实现IProtocolCommand<bool>，Message/Frame使用原Input及Metadata；bool仅表示有无成功receipt，不作提交证明。handler只交一次bus。whole注入原domain/查询/槽映射、TimeProvider、ICommandWorkUnits/ICommandProjections/IGameCommandOutbox和所需ILogger；不注入sender/总线/队列/行网关/规则源或当前请求locator。

按实际原接口确定**本簇空参与者范围**：fine只IAccountCommand，无ISelectedSessionCommand/IRequestSettlementCommand；45号行为在此不捕获selected参与者，SellAsync也不报告效果足迹。故本whole为一个ordinary WriteAsync，显式Declaration(name, [], Ordinary, None)，无Main/NonRequest/replay/exit。不是因为Sort用了[]就套用；本结论来自上述Sell源。保NonEffect适用login/connection authority/request健康关闭，不能拿[]绕过这些规则。

原顺序完整保留：

1. **先**slotMappings.ToBusinessSlot(Input.Slot)，再FindByConnection，再FieldSum比较，再session/online存在检查。FieldSum为(byte)Space＋Index＋Count；映射异常原样外抛，不因sum本来错误而先回ACK。缺sum/actor/online按原InvalidRequest/NoSession结果，无原fine的分支不新开事务。
2. 有actor/online且sum合法时才进入一次Write；原fine在action入口求clock.GetUtcNow，再调用原sales.SellAsync(session, businessSlot, Count, NpcId, now)。不要新加IsInWorld或online-ID前置复验：原fine的OnlineUserId字段实际上未被fine/domain使用，world检查由原domain在事务内做。不要新增Load/ReadCurrent、NPC/shop/tower/清房权限或规则计算。
3. **正常返回ItemSaleResult**（包括NoSession/Invalid/ItemUnavailable/Locked/CannotSell及正常GoldLimit）保持原正常完成/Commit，无RequireRollback。targetGold>int.MaxValue及Quote的GoldLimit都是正常拒绝，不与下面的抛异常路径混同。
4. **仅原ItemSaleRejectedException的throw路径**在action内窄try捕获；try含原clock参数求值和domain调用，不包unit/runner guard、mapping、日志、投影或登记。保存原exception局部值；catch结束后用unit.RequireRollback(cause)，此guard不得又被同catch吞掉。以非null私有结果携带domain结果或cause，等Write及rollback/lease cleanup全部正常结束后，才按cause.Reason构造原ItemSaleResult/映射协议拒绝。Abstractions注释所要求的“越过事务边界才成为协议拒绝”以此保留，不改接口或把拒绝ACK提前进action。
5. 普通SqliteException、真实Commit/Prepare故障、取消、typed不确定和其它未知异常不新增catch/StorageFailure/WriteResult恢复，仍外抛sticky。RequireRollback失败不能作为正常拒绝；不清request失败、改HasCommittedUnit或手动Rollback已结算事务。

unit结束后沿原receipt/null分支：成功Info日志的Character/Npc/Slot/Item/Count/Gold内容保，随后ICommandProjections.ProjectAsync(TheseCharacters([receipt.Session.CharacterId]),new ItemSaleCompleted(receipt))；拒绝Info日志Connection/Npc/Container/Slot/Count/Reason保，再RegisterCmdAsync原SellItemResponse(null,ErrorCode)。纯ErrorCode映射随whole迁，不留handler业务编排。错码精确Locked=D6、GoldLimit=22、Unavailable=17、NoSession/Invalid/CannotSell=1，其它仍原ArgumentOutOfRange，不扩默认兜底。

原ILogger<SellItemHandler>的协议编排日志归whole类别；旧fine的重复失败Error可依VC-B2消除，但ItemSaleDomainService缺价/坏价“using zero gold”的业务ERROR原样保留。Data结果/receipt和ItemSaleCompleted整文件不改；没有新状态缓存。

### 5. 原投影、三类拒绝及接缝迁移

ItemSaleCompleted原composer复查character/connection/world，并经slotProjection.Project(receipt.Slot)回**一个**SellItemResponse；它不补inventory NOTI。A21成功ACK会修改金币/扣客户端count，必须保原一次登记，不能重放旧ACK。NotificationBus的普通composer/encoder失败仍该recipient Warning＋Failed/caller正常、已提交卖出不逆转或重试；whole继续原忽略delivery结果，不主动throw/fence/close。caller取消或投影外层scope等实际外抛才按原默认处理，先truebefore分清。

拒绝的错误分类必须分开实证：

| 原分支 | 原事务/新等价方法 | ACK故障的验证重点 |
|---|---|---|
| sum/actor/online前置拒绝 | 原无fine，无Write | cause/零ACK/真实sourceclose，不能伪造已提交 |
| domain正常返回拒绝 | 原空/无资产变化事务仍Commit；新ordinary同样Commit | actual old/new外层分类单列；若新defaulttyped，须精确PostCommitPublicationFailure/inner同cause、原空participants无新增fence、协议/资产/关闭不变，不normalize“完全同异常” |
| cap夹取后抛ItemSaleRejected | 原fine Abort；新窄catch＋RequireRollback(cause)，完全结束后拒绝 | staged部分gold确曾发生但持久资产完全还原，Completion ConfirmedRollback；直接ACK故障不借空Committed规则，cause与关闭/零登记按实测核 |

Task21对其NonEffect空Commit拒绝的defaulttyped解释仅提供已接受方法，不自动替本Sell签结论。Task22须按本表真实before/after及实际HasCommittedUnit/Completion逐项记录；沿B3有限raw/default规则，不新增第4许可。预期内部包装不同的分支不是新协议字段/帧/关闭/持久化变化；若实际出现新的fence/错误消费或其它可见变化，保实证交主控，不擅用例外掩盖。

迁旧测试保持全部方法/参数与强断言意图：精确pile价格/数量、装备durability与InstanceValue不当数量、锁/超量/非法地址、两请求不双卖、cap两值全拒、库存故障金/物品同回滚、失败投递不重试、旧receipt不发新角色、装备attribute不是NPC禁售、零价ERROR字段/显式零价无ERROR/真实禁售不被缺价覆盖；NPC任意ID与tower/boss/shop标记无权限含义的原断言不能删。直接domain/纯投影测试维持其原目的，不都强改whole。

ItemSaleTests旧session fine请求没有wire对应身份字段。可沿本地只读selected-query forwarding proxy造原handler捕获真实旧session，IsInWorld仍查真实已选另一角色的registry，旧/新真实wire对照正常拒绝ACK及原角色gold/count/core不变；不能替成任意invalid请求或称wire携带旧身份。保其bad sum与stale两个原目的。KnightCoinShop两真实PVF购买仍原shop.Command；Sell用真实body映射granted BusinessSlot到GameSlot，按精确成功ACK断言Gold98000/差18000/Count2及库存0，原receipt断言接缝变化逐项说明，不能退成只看未抛异常。

架构文件仅替Sell的旧fine类型断言：新whole.Input精确SellItemRequest、request保持GameSlot、IItemSaleDomainService.SellAsync的slot精确BusinessSlot，已有六domain接口无GameSlot/InventoryContainerType与Business receipt判据仍保。给复用或局部的反射检查一个真的bad contract/model（含GameSlot）证明会拒绝；不能为凑结构造生产private方法或仅删除旧Sell判据，其它协议case/方法名保留。真实unmappable组合验证mapper在业务之前，防反射全绿但运行时少换算。

新flow至少补同一raw sequence连续卖1两次（seed3，余1、gold增两次、两次ACK），fresh/ordinal实际变化但不dedup；fullsale后同seq再请求按当前ItemUnavailable拒绝，而不是旧成功ACK。metadata原对象/Selector/Type/Sequence/Ordinal/Fresh/FullBodyLength/SHA、单ACKbytes/target/batch及原结果处理用同provider观察。

### 6. 实际故障、完整资产和有价值mutation

真实DDL位置已读：InventoryStateTables.SqliteCharacterCarriedGoldTable.AddAsync是UPDATE character_stats SET carried_gold=MAX(0,MIN(ceiling,old+amount)) RETURNING；SqliteItemDomainStore.TakeAsync对全量删item_slots，对partial保存原core及UPDATE count、另发detached piece，再由lifecycle真实Delete结束该piece。可用原ItemSaleTests的BEFORE DELETE和BEFORE UPDATE OF count触发器覆盖action错误，不伪称Commit。

before和新whole各至少一条partial或full sale actual SqliteException19/1811：金已先进入同事务，slot写实际抛，最终原gold/原item和所有相关行完整回滚，零ACK。另真实native路线在本fixture建parent/child，FK DEFERRABLE INITIALLY DEFERRED，在目标item_slots AFTER UPDATE OF count或AFTER DELETE时插入缺失parent child；限定原owner/list/slot，避免seed触发。实际同participant PRAGMA foreign_keys=1、child>0、domain正常完成后的gold/数量/core/事件stage均记录，再证明失败来自SqliteTransaction.Commit19/787而不是action/Prepare/fake participant，原cause引用经Kernel/whole外抛，确认回滚后去trigger、同seq业务可再次执行。准确DDL/observer可依fixture实际收敛，但无可达native证据不能降格成trigger后宣称已测。

local forwarding observer调用原domain及clock，记录相同非只读open DomainTransaction引用，尤其cap分支catch时staged gold100030和原qty10，证明回滚保护有真实部分写；不调用Failed/清失败、不加第二Host/provider/outbox或生产请求通道。可用既有participant测试观察方法读真实prepare/stage/CommitFailure，保持原Fallible participant；SQL异常/rollback/lease cleanup不确定的原cause/独立原因与默认保护由本case及Appfull共同守，不扩core。

资产至少items全部列、item_slots全部列、character_stats gold及实际触及角色/容器参数、domain_events/deliveries与FK；nonnull BLOB无损hex、SQL NULL保null，82Bcore与可空扩展、UID/数量/剩余状态均保。same-run action/native/cap rollback必须全列exact，不豁免时间/ID。跨独立run只允许已证明实际随机的items.created_at/updated_at按各runner UTC窗口/created≤updated具名比较；三事件GUID按EventService实际生成点、每类跨phase双射/非零/不合并rollback瞬态与retry，保sequence/type/schema/payload/time/全部其余列和Delivery FK/batch/origin关系，原raw全部留。不忽略整表/任意GUID或把异常分类抹平；其它差异保诊断交主控。

有效mutation：仅暂删whole在ItemSaleRejected窄路的RequireRollback(cause)，不改domain/cap/测试assert。真实cap100030、oldgold100000、price31、seedqty10卖1，mutant应拒绝ACK22却gold100030/qty10；先输出完整actualassets再精确断言，正常无cap/正常卖出正控仍可达。若变异因实际私有结果设计不能编译，修为同风险的可编译最小删除而不冒编译RED；若部分写未发生，先修seed/价格fixture，不称无差别运行RED。finally保存/逐byte恢复原whole SHA，同目标GREEN后freeze；纯结构搬迁不另造无意义RED。

### 7. 最终freeze与正式检查

r2当前续作顺序以§10为准：新增Net改前归档→一literal→精确目标GREEN→freeze03→完整Release/三full/四controls同03→其余闭账。原freeze02及所有02输出保留但不冒最终通过；r1已验原native/mutation且产品未变，不重复制造其RED/旧oracle。

focused先闭合原强case/真实旧新/三类拒绝/投影隔离/metadata/同seq/SQL/native，再mutation恢复与注册/codec检查。freeze覆盖11物理source（10存在final＋旧fine删除before SHA），包含§10唯一新增Net literal变化、通知/codec/Data readonly、原case声明和实际迁驱动说明、非owned原库存；检索生产旧fine类型/handler/caller不存在而非只看删文件。formal后改影响源/测试须新freeze并重跑受影响，最终证据明确对应版本，不用旧绿掩新源。

使用新Task22 prefix/runner、本机FSEvents可用require_escalated环境，记录cwd/HEAD/命令/真实UTC/exit/elapsed/resources/完整console/DFO路径；不复写旧Task脚本/日志，不TERM活跃native或父runner。先实际`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，exit0、0warning、0error。后分别三个实际csproj：

```text
dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

project为Dfo.Server.Application.Tests、Dfo.Server.Protocol.Tests、Dfo.Server.Networking.Tests；无filter，原xunit类并行/5m/期限/Skip保持。App只容许两精确旧DimensionDungeon displays：Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource、All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，InvalidDataException/7100/maze0/(1,0)/entrance Left。新增错误先完整身份/stack/原log再诊断；Task21的TCP拒绝并非登记基线，不凭隔离绿归因或放宽。其它项目最终0fail。

同freeze另Host/HttpApi/Infrastructure/Persistence四原GameplayAssertionRecorderControlTests各fresh1/1，保持七actual controls。DFO用原violations.py CLI、原expected逐类count74（App62/Net7/Protocol1/四其它各1），outside/exe/orphan及对照范围外0，新增故意违规逐类具名，不放宽原expected；exe0仅观察。Golden15逐byte等HEAD及参考f01dc226、Characterization10逐display通过/0skip。codec原文件/字节/未映射slot纯Throws不放宽。

Counter基线取Task21的App/Proto/Net final02 raw和Task20另外五full原1800，按(display,status,multiplicity)重算原8900全部保、D原则0、新增全passed；rename/status/确有删除逐项给依据。保每Theory参数，不能缩例或只比总数，四controls不重复入业务N；总数8900+N-D，每项目pass/fail/skip与实际fresh/库存分明。旧fine/手造handler驱动迁移、Knight receipt和架构判据的实际断言变化分别说明，不冒“所有旧断言逐byte不动”。suite资源不是单CMD≤2秒测量。

### 8. Capture、两ID闭账与交接

finalfreeze上fresh after source/runtime及同pure family/exits：预期173logical/203cmd不变，31Protocol＋1Internal＋171LegacyFine，143Tx＋47Direct＋9RO＋4Workflow，message42Protocol/131Legacy、telemetry14。实际差分只能message65558及一fine→一whole；新whole精确(SellItemRequest,65558)、Direct/Protocol且无RegistrationExits，原三许可及全部其它family/owner注册保持。不能用expected充actual。

canonical仅准改：

```text
message:65558
command:Dfo.Server.Protocol.Game.SellItem.SellItemCommand
```

各行记录旧1Tx→ordinary Write、原无selected参与者/无Main/足迹、正常拒绝Commit与窄exception RequireRollback/确认回滚、原投影单ACK/Failed隔离和完整before/新wire/SQL/native/mutation/日志证据；不提前关其它source/protection/transition。其它baseline/original/rows/group逐值等HEAD，md旧前缀不变，只append本Task实际结果。预计pending117message/171fine/72source/5protection/8transition，需实际原validator核实。

preclose通过后按executing-tasks追加Result及literal Outcome: done；strict原工具和原八selftest实际运行，八份物理临时坏副本各以原validate按原理由拒绝。副本可变该反例语义所需telemetry/transition等行，canonical仍限两ID；不改tool/基线，不换成较弱两ID伪例。最终完整manifest列所有实际source/evidence/log/脚本/Task/两ledger SHA/bytes及fine删除before SHA，可与Git changed+untracked精确闭合，Task/manifest自身采用清楚prefix身份避免循环；不漏untracked/不纳bin/obj。原before35/原14157 nonowned map及r1前缀immutable；当前最终比较只精确排canonical两ledger＋r2新增Net路径，三键均须实际证明在原map，核14157−2−1＝14154其它原路径逐SHA不变。新Net由改前归档/单literal diff/freeze03另证；可另存排除副本，不能重造历史map或声称原14157全未变。

完整Result说明before真实时点/分类与新whole实测、cause/交易Completion/资产/ACK/关闭/参与者、三种拒绝及日志、同seq实际业务、原测试驱动/断言意图和新增删改加法、有效mutation/恢复、finalfreeze及完整正式结果/DFO/Golden/Char/capture/strict/manifest/非owned与局限；异常分类差异如实单列不normalize，原source-close精确引用用实际Assert.Same和derived输出，不能hardcode冒直接观察。全部checks/Result/manifest齐后停所有共享写/native，交fresh required结果快审；执行者不自批/stage/commit。主控消费后checkpoint并核剩余P4；Astra/最终Spec核验/测量/封存和用户分支收尾仍后继，RecordedOutbound不是实际socket关闭/A21实机。

### 9. 本地选择与何时返主控

local helper/observer/私有非null结果包装、已有counter方法、fixture种类/价格/DDL条件/精确filter和安全JSON字段可由executor收敛；普通编译/fixture诊断保存后自治修正，不逐次加批准门。不得因方便新增OnlineUserId或NPC门、把所有正常拒绝RequireRollback、把业务ERROR当重复日志删掉、迁移后补造before或以fake Commit代真实native。真实before否定上述关键边界、必须扩11路径/共享core、新兼容许可/WHAT/VC/提交点改变或native确不可达而无安全实际路线时，停受影响产品，保最小源/完整实证/候选交主控；不弱断言求绿，不自选Spec终局。


### 10. r2 Controller task-rework：仅适配真实协议CMD日志身份

主控沿同Task22授权本r2；producer已确认formal02/capture-after01全部自然结束、共享writer/native停。本作者仅修本Task，不实施源修改。B3/P4的WHAT/VC、原事务/接口/三raw/默认规则/空参与者和所有授权不变；这是已批准VC-B2“CMD身份改协议CMD”的本地测试接缝适配，不改错误消费/关闭政策，不需B4/P5、Task-plan gate或新Task23。原r1 **25607B／SHA f5b36fb35ff3781241b3026f61e1cf79813c65ef949f4d0c730c9eca6669bc3f** 已存在于task-plan-r1-prefix.md，本作者核逐byte等原Task后保留immutable、不重写。

**实证输入。** 已读networking-error-consumption-diagnosis.md、diagnostics/mapping-whole-log-excerpt.txt、13条匹配原log的networking-mapping-lines.json、readonly Net源snapshot及实际源/正式02 metadata与console。02唯一Networking失败是An_unmappable_slot_is_logged_on_its_connection_and_the_host_serves_the_next_connection，WaitForErrorAsync:88/test:71因原literal `Gameplay inbound:` 未命中新whole日志而等满原10秒；不是TCP拒绝或允许基线。真实ERROR已记录 `Gameplay GameSellItemCommand failed:`，同connection Game:01-00-00-00、非空root/Connection链及原InventorySlotMappingException／255/65／mapper→whole line29均在原输出；healthy第二连接的精确GameExit ACK在等待日志前已通过。旧legacy由dispatcher建inbound:GameSellItem作业，新whole由CommandBus按GameSellItemCommand建作业，日志身份差异属于VC-B2，不能据旧筛选器超时新增一个错误策略或raw许可。

**唯一新增测试改动。** Server/tests/Dfo.Server.Networking.Tests/Integration/InventorySlotMappingFailureIntegrationTests.cs中，仅将WaitForErrorAsync的`logs.ErrorsContaining("Gameplay inbound:")`一处literal改为`logs.ErrorsContaining("Gameplay GameSellItemCommand failed:")`。不改RecordingLoggerProvider的LogLevel.Error过滤、Assert.Single、异常类型/255/65、真实健康第二连接精确ACK、10秒预算/10ms轮询、Host/端口/其它任何行；不改logger、decoder、CMD、注册或业务。新源必须逐byte等于旧源作这一次替换的结果，不能趁机强化或泛化其它断言。

该路径修改前当前/HEAD均 **7413B／SHA d9c0b43f93e5288ebe7c2230bfad42cc11e28f85545d1d61087153d94dc4a29b／LF182、CRLF0**，本作者只读核过；它实际存在原14157 nonowned map。主控readiness后，执行者先再核当前/HEAD与原map三者一致，另在r2-owned-before/保存此单一路径原bytes及manifest（HEAD/真实UTC/SHA/BOM/行尾），再做一literal。diagnostics里的readonly-before/proposed源保持原样，不能拿提案当已实施。原before-manifest **35项／10233B／SHA439bcb2c9c6220dbc3c56e802abb642be8aff2c283e672aa9186227bef93c100** 已由本作者逐项bytes/SHA核全部匹配；不加项、不改原35清单或原map。

**02实值与继续点。** Release --no-incremental真实exit0/0warning/0error；App5143＝5125pass＋2准确原Dimfail＋16skip，Protocol1774全部pass；Net199＝198pass＋上述唯一mapping日志筛选失败，原199个display都存在，没有本轮TCP refusal。父runner按Net必须0fail的原检查自然停，四fresh controls未跑；这些不是最终成功、DFO74/7尚不能据此闭合。capture-after01于19:44:28.666357～19:44:34.602471 UTC真实exit0，pure family尚未启动，仍须完成最终capture/family差分。原source-freeze-02.json **6540B／SHA20e1b16608d861bc471ab2e6fe35e50f339a3b658e37af60a232debdbbaa8ca7** 及原10source状态保留，不覆盖或改称11source旧时点。

readiness后首先以新编号只跑这一个原mapping TCP目标，须实际GREEN且所有原强断言不改；失败若不是该日志身份问题则保新raw回主控，不盲重试/放宽过滤。随后封freeze03，11source＝8mod/2new/1delete，产品与原其它源逐byte等freeze02，仅增加该一literal测试变化；新完整Release＋App/Protocol/Networking三个无filter full＋四fresh controls全部同03自然执行。Net必须0fail，不能把02筛选失败并入旧内容基线，不能用精确目标GREEN替全量。原native/cap rollback/projection/metadata/有效mutation的产品未变，已有真实before及恢复源身份继续沿用，受改日志身份由新target及最终Net full实证，不重造原before或重复无关mutation。

完整Counter仍从Task21 final02三full＋Task20另1800的原8900逐(display,status,multiplicity) N-D；新增Net文件不改任何case名称/参数，02已有App N15＋Protocol N1预计N16（实际日志为准），不能以旧02缺失controls/Net失败闭账。继续原DFO74/7、G15/Char10、两canonicalID、final fresh capture（新编号，不覆盖capture-after01）及pure family、strict原八类/selftest/八物理坏副本、完整manifest/14154非owned检查，全部原Task义务不减。Result明确列出02未通过、日志筛选的实际身份根因、一literal/target/final03实值及前后源，fresh required reviewer独立判断这个适配和保留强断言、原wire/关闭/取证范围，不由主控技术裁定代批准。

本r2作者交付时未改Net源/产品、未运行新target/freeze03/native、未写Result/Blocker/status/review或ledger；仍待主控readiness另授权执行者，写完停全部共享写/native。


## Result R1

- Outcome: done
- Contract: r2；本Result只交付P4 T-004本Sell slice，Spec B3／P4及其它Task不宣告完成。Result需fresh required独立快审，主控消费后才checkpoint；执行者未自批、stage或commit。
- Final identity: HEAD a6f19c44f0b3a539ca08100aadd76146c0411512／branch task/refactor-session-20261021／index空。Taskr1原25607B/SHA f5b36fb3…在task-plan-r1-prefix.md保持原封，Taskr2本文前32641B/SHA f0aefeeb…；最终source-freeze-03.json覆盖11物理路径（8mod/2new/1del），SHA 4d0aa250da21eb1dde7dded8c09b697dd8ba8dd834439d92c446d3304abfc23d。

**交付。** logical65558/wire0016的精确SellItemRequest只归GameSellItemCommand<bool>，Direct/Protocol无registration exit；薄handler一次bus，旧fine record/handler/物理源/Transactional注册和生产caller删净。旧slot mapping→session query→sum→session/online原顺序保，clock在ordinary Write action参数求值原位置，Observer直接Same同一open非只读事务及now；没有新增world/online-ID/NPC/Load gate。原fine只有IAccountCommand、无selected/settlement marker，所以ordinary Write([],Ordinary,None)还原原范围，不借其它协议套用；无Main/NonRequest/footprint/ledger/attempt/replay。Data Result/Receipt、ItemSaleCompleted整文件、codec/decoder/encoder、domain/rules/content/持久化和公共support/core全部逐SHA只读。

**原业务原子性。** 正常ItemSaleResult.Refused继续Commit；仅原ItemSaleRejectedException窄try保存原cause，catch外RequireRollback(cause)，完整unit和cleanup结束后才构造拒绝/ACK。普通SQL/nativeCommit/Prepare/cancel/typed不宽catch，不使用WriteResult恢复。成功/拒绝Info日志字段迁whole，旧fine重复Error按VC-B2去掉，原缺价/坏价业务ERROR和原上下文保。原success composer单ACK/客户端扣count/加gold、不补inventory NOTI。原NotificationBus普通composer/actual encoder故障继续Warning同cause/Failed隔离，whole忽略返回并正常完成，无逆转、重试、fence或close；测试实际观测Warning/无batch/无close/完整已提交资产，Failed返回路径由未改EventService/Subscriber/NotificationBus支持。

**真正before。** 初始HEAD逐byte产品/AGENTS与newwhole absent、fresh capture-before01/pure family-before01先取，再旧真实13B body→原registered decoder→dispatcher sameprovider实跑。old-oracles02 73/73＝原ItemSale51+KnightCoin2+架构6＋新flow14，13结构化observation，before35项10233B/SHA439bcb2c9c6220dbc3c56e802abb642be8aff2c283e672aa9186227bef93c100原封。01 observer曾给normal拒绝多读SQL，raw/driver保diagnostics；02限制仅成功receipt/exception阶段SQL观测，normal拒绝不人为enlist。没有迁移后补造before。

**三路拒绝ACK故障。** 原before三路均raw同cause，保完整原栈。新pre无Write/无提交→rawSame；cap真实部分gold100030/qty10已stage→ConfirmedRollback全资产exact恢复后拒绝→rawSame；正常domain返回拒绝实际Committed，empty participants→default typed PostCommitPublicationFailure，InnerException Same原cause，明确外层分类不同，不normalize“完全同异常”。本Sell实际检查closed ProtocolNonEffect/HasCommittedUnit/empty committed participants/empty attempts/完整GameplayCallChain、Error日志Root/Parent/Depth/Source/SourceConnection/CommandType；三路各自encoder1/ACK登记0/source close Single+IsClose+Assert.Same真实f.Connection，JSON由ReferenceEquals导出，EndedAJob true/nofence/完整资产不变。不是借Task21代证，无新raw许可或默认错误策略。

**实际故障和原新对应。** SQL action BEFORE UPDATE count真实19/1811，native AFTER UPDATE count插缺parent的DEFERRABLE child；同实际participant FK=1、child>0、domain正常完成gold100031/qty9与完整stage后，SqliteTransaction.Commit真实19/787、CommitFailure.Cause与外抛原cause Same。均ConfirmedRollback，items/item_slots/stats/parameters/cargo/events/deliveries全列、BLOBhex/NULL/82Bcore same-run exact，去trigger同seq重做成功。原新13对应全部业务值/帧/metadata/原cause类型消息（normal外层明确差异）；跨run只items.created_at/updated_at按各真实UTC窗口与三具名事件GUID双射/非零/跨phase/DeliveryFK关系校准，其它列/payload/time不豁免，raw全保。见oracle-before-after-checks.json，未忽略整表或任意GUID。

**有效mutation。** 仅删RequireRollback可编译，cap100030/old100000/price31/qty10卖1，实际拒绝ACK22/qty10却gold100030，Expected100000资产RED；cap0另因Committed≠ConfirmedRollback RED，两个正常pile参数可达pass。完整actualassets/实际ACK帧在assert前落raw，finally原whole4246B/SHA142d5f765930d5864e306ff1e151b87e5b2adddb38f089daeaa10313e987c0e0逐byte恢复，同四目标4/4。初mutation评估driver错误预猜3pass1fail/按接口JSON查ErrorCode失败是诊断，不冒编译RED，已保v1并按原raw/实际帧校准2pass2fail，不重造变异。

**原强case迁移。** 原59声明/参数全部保，ItemSale helper从手造handler转真实wire，direct domain/纯receipt投影仍原目的；stale wire无旧身份字段，使用sameprovider read-only query proxy返回真实前session、IsInWorld询真实registry，原角色gold/count及bad sum不变，局限如实。KnightCoin两个真实PVF购买/参数保，Sell以真实Business→Game槽打线，原价差18000/gold98000/库存0保，原Result/receipt接缝改单成功ACK精确slot/count2/gold98000。架构只迁Sell exacttypedInput+真实domain BusinessSlot，原六domain/其它协议/receipt判据仍保，并新增同判据实际拒绝GameSlot坏model。新flow14涵盖成功pile/装备UID不当数量/cap/SQL/native/投影/三拒绝/stale/同rawseq实际再卖；最后耗尽按当前Unavailable17，不重放旧ACK。原source-shape与readonly字节证据见freeze03/source-final-checks-03/r2-mapping-literal-check及workunit-and-coverage-map。

**r2 Net受控rework。** 首build01仅新增注册断言namespace拼写错留raw；02修后Release0/0、App5143仅旧Dim2、Proto1774绿，但Net原199 display为198pass/唯一mapping日志fragment超时，四controls未跑。主控及r2 §10确认是VC-B2日志作业身份从inbound:GameSellItem迁GameSellItemCommand，非新错误消费/关闭政策。新增Net第11路径先核current=HEAD=原nonownedmap 7413B/d9c0b43f…，另封r2-owned-before；仅一literal改为Gameplay GameSellItemCommand failed:，全部其它bytes精确不动（Error过滤/Single/异常type255/65/真实第二连接exactACK/10s/Host/port/parallel原样）。新7432B/139b29…，精确原TCP target1/1 GREEN360ms后freeze03，其余10source等freeze02。原02/raw/源冻结/诊断/旧提案及r1前缀/before/map/mutation保持原样；03完整重跑，不拿target或02绿掩全量。

**最终正式核验。** 同freeze03实际Release --no-incremental exit0／0warning／0error；三无filter full原parallel/5m/Skip自然结束：App5143＝5125pass＋准确原Dim2fail＋16skip、Protocol1774pass、Networking199pass/0fail；另Host/HttpApi/Infrastructure/Persistence四fresh controls各1/1。Counter从Task21 final02三full＋Task20另外1800逐display/status/multiplicity重算：原8900全保，N16（App flow14+架构反例1、Protocol注册1）/D0/rename0/statusChanges0，总8916＝8896pass＋原Dim2fail＋18skip。另五套1800为库存，不冒八freshfull；suite资源不冒单CMD2秒。DFO原CLI、原expected74及七actualcontrol全保，outside/exe/orphan0、新intentional0，不放宽expected；exe0仅观察。Golden15 byte等HEAD和f01dc226、Characterization10全pass/0skip。旧Task21 TCP refusal根因仍未证，非本Task登记基线，本轮最终Net0fail并不宣称修复该旧现象。

**capture与闭账。** final03 fresh capture-after02/pure family-after02实际173logical/203cmd；31Protocol+1Internal+171LegacyFine，143Tx+47Direct+9ReadOnly+4Workflow，message42Protocol/131Legacy、telemetry14。仅message65558与一fine→一whole变化，其它runtime/family/routes/三种exit及全部owner注册精确原值。canonical仅message65558与command:Dfo.Server.Protocol.Game.SellItem.SellItemCommand两ID闭；其它baseline/original/row/group等HEAD，md仅append。pending117message/171fine/72source/5protection/8transition，不提前关其它范围；preclose已实跑valid。strict原工具、原八selftest与八物理bad副本的实际拒绝在Result后按原validate执行，最终以ledger-strict/ledger-eight-bad/ledger-eight-bad-copies及manifest-r1为交接校验，不改工具/基线/反例语义。

**边界与交接。** 原14157 nonowned map不改，精确只排其实际存在的canonical两ledger＋新ownedNet路径，剩14154逐SHA不变；新Net单literal另证。manifest-r1记录所有实际Git changed/untracked源/脚本/证据/log/Task/ledger SHA/bytes及fine删除before身份，Task按Result前缀身份、manifest排自身hash避免循环。全部checks与manifest完成即停全部writer/native交fresh required review，执行者不自行commit/remote/版本/tag/游戏/部署/cleanup/Task23。RecordedOutbound不冒实际socket/A21，T012单CMD≤2秒、Astra/Spec最终核验封存和用户分支收尾仍后继。


## Independent Quick Review R1 — TASK-22 r2／Result R1（2026-10-25）

```yaml
kind: rks-review
verdict: approved
```

**对象与范围。** fresh独立结果快审，未参与本Task规划、生产或r2边界处置；按reviewing-work的Task Unit、Code、Deliverable判据，完整读取Task22 r2／§10与Result R1、相关B3有限raw及默认规则、P4工作单元/注入边界、当前AGENTS及两架构偏好。Task21的done／approved／Controller Consumption已在HEAD提交。审查HEAD为`a6f19c44f0b3a539ca08100aadd76146c0411512`，Task Result前缀41911B／SHA `8b1d96f07bd56b8f6656be2fb4892f4dd0626f7a1deacea10d2e7e102eb482f0`；r2计划32641B／`f0aefeeb27fa1c3975d62a09db5fec0fef4f7dc409623c8cc1c50c2aa88be0ea`。freeze03为6905B／`4d0aa250da21eb1dde7dded8c09b697dd8ba8dd834439d92c446d3304abfc23d`，manifest-r1为55276B／`a1225a749e03b8b071dccb159417feb8a6728db9ba7e591b7b1708e4c1026948`。本approved只覆盖该局部执行结果，不批准新计划、完整Spec或最终验收。

**实现与原边界。** 逐段读取全部产品/测试/文档diff、新whole、HEAD旧fine/handler及实际领域和投影源码。logical65558以精确SellItemRequest、Direct／Protocol／NonEffect进入GameSellItemCommand<bool>；thin handler只交一次bus，旧fine源/handler/Transactional注册删除。mapping→session→sum→online次序、field sum规则、原成功/拒绝Info字段和错误码保持；不追加unusedOnlineId复验、NPC授权或Load。原fine只有IAccountCommand且无selected/settlement marker，ordinary Write([],Ordinary,None)保原空selected参与者范围。clock仍在action调用原domain参数求值处，未先移到事务外。只有clock＋domain的窄try捕获ItemSaleRejectedException，catch外RequireRollback保存原cause，unit及lease完整退出后映射拒绝；普通ItemSaleResult.Refused仍提交，SQL/native/cancellation/typed异常未吞。未引入Main、NonRequest、足迹、ledger、attempt、重放、新出口许可或共享Core改动。

**投影与拒绝分类。** ItemSaleCompleted、Data结果/receipt及codec只读，由原TheseCharacters投影发送单ACK，原成功composer/encoder的Warning＋Failed隔离、caller忽略行为保留，无新增throw/fence/close或ACK重放。零价/缺价业务ERROR原位置未删除，去掉的仅旧fine重复Error，符合VC-B2。独立检查三拒绝的旧driver真实Assert.Same及由ReferenceEquals输出的物理sourceclose：pre无Tx/rawSame、异常ConfirmedRollback/rawSame均保；normal空Committed由old raw IOException变为默认typed PostCommitPublicationFailure、InnerException同cause，完整链/EndedAJob/空participants且attempts已清均有实际断言。两边全列资产不变、ACK0、精确物理sourceclose1、nofence相同。B3有限raw表不授该normal分支第四raw权利；实际默认FailAsync与GamePlugin的EndedAJob收口支持当前分类。比较器明确单列该外壳差异，没有把异常normalize成相同，也没有借Task21结论代本case取证。

**原测试与真实wire保护。** 完整读取新flow、旧driver及原测试迁驱动diff；原ItemSale断言和真实PVF KnightCoin两个参数/价格、98000金币/售出数量/库存保持，直接投影测试仍保。slot架构从旧fine改检查whole.Input为SellItemRequest、wire GameSlot及领域参数BusinessSlot，并用实际BadBusinessModel证明旧无wire地址判据拒绝反例。新/迁驱动确经同provider注册decoder→dispatcher，13B body、原metadata对象及全部字段/Hash受检。同seq两次卖出从3到1及两次增金，耗尽后按当前Unavailable拒绝，未去重/回放。stale只读query proxy返回真实旧session、world问真实registry，不能证明wire携带旧身份；Result准确披露这一限制。

**before、SQL/native与对应。** 独立核35项before全部bytes/SHA，并将preproduct五项产品/AGENTS快照逐byte对HEAD，新whole在HEAD不存在；真old02原始console为73/73，提取的13组对应观察与before JSON精确一致（另两个capactual诊断输出保留）。final03原始console提取的15条task22输出与final JSON精确一致。实读实际触发器/forwarding observer，action为UPDATE count上RAISE的19/1811；native为同participant FK=1、缺父child已入、正常domain完成并完整staged gold100031/qty9之后实际SqliteTransaction.Commit19/787，cause引用不变。clock/domain同一open写事务，cap实际stage100030/qty10后ConfirmedRollback；SQL两路全列same-run回滚及去trigger同seq重执行有实际断言。只读截断比较器报告写入部分重算13组对应、108项具名items时间记录；时间受实际runner窗口约束，三类非零GUID跨phase双射及Delivery FK/batch/origin关系保，全部其它列/82B core/BLOB hex/SQL NULL精确。same-run rollback无时间或GUID豁免。

**行为mutation。** 直接核原始mutant/restored日志、源与记录：仅删RequireRollback可编译，cap30拒绝ACK22却实际gold100030/qty10，Expected100000形成真实资产RED；cap0因Committed不等ConfirmedRollback形成第二RED。两个正常pile正控通过，实际2pass＋2fail；原先3pass1fail预估及接口序列化缺ErrorCode诊断仍保。确认真实ACK bytes为`0116001100000000000000000000000016`，恢复原4246B／SHA `142d5f765930d5864e306ff1e151b87e5b2adddb38f089daeaa10313e987c0e0`且同四目标4/4green。没有重造第二轮RED或将编译/fixture/超时当业务失败。

**r2网络一literal。** 直接核02原始日志：唯一失败是原WaitForErrorAsync等待旧Gameplay inbound文案至10s；实际已有GameSellItemCommand完整root/source/connection/chain Error、原InventorySlotMappingException255/65，healthy第二连接的精确GameExit ACK位于等待之前。该CMD日志身份变化符合VC-B2。r2归档原7413B源码后只将该搜索literal换为Gameplay GameSellItemCommand failed，逐byte重算除此无变化；Error过滤、Single、异常类型/地址、真实socket/Host/port、10s/10ms与其它断言完整保留。相同精确TCP目标1/1（360ms）及final03 Net199/199成立；freeze03除这第11路径外原十源逐项等freeze02。不是放宽期限/并行/Skip或修改故障策略，02失败记录未抹掉。

**最终验证与证据身份。** 只读重算原始display/status/multiplicity Counter：原8900全部保留，N16/D0，8916＝8896pass＋2准确旧Dimension失败＋18skip。final03实际无filter App5143＝5125pass＋2旧Dim＋16skip，Protocol1774/1774、Networking199/199；build Release --no-incremental为0warning/0error。四fresh control各1/1，其余1800明确是Task20接受的full库存，未冒称八freshfull或重复计N。两Dim保持原Fusion/All_six与7100/maze0/(1,0)/Left；Characterization10逐display全pass。原DFO CLI按原command只读再运行exit0，74条/七unique controls，原expected类/计数/程序集集合精确保，outside/exe/orphan0且新增intentional0；exe0只作观察。Golden15独立逐byte同时对HEAD和f01dc226相同。

**capture、闭账与范围。** 只读截断原capture比较器写入部分重算：173logical/203cmd、31Protocol/1Internal/171LegacyFine、143Tx/47Direct/9RO/4Workflow、42Protocol message/131Legacy、telemetry14；仅65558 route及一fine→whole变化，其它runtime/pure family精确，新无exit，原三exit种类和全部owner注册不变。canonical逐值仅message:65558和SellItemCommand两ID变化，其它组/row/original/baseline全等HEAD，md旧前缀保持；pending117message/171fine/72source/5protection/8transition。未改原validator，strict、原8 selftest及8物理坏副本均只读重新验证按原理由拒绝。manifest197现存条＋1删除共198条与manifest自身合成199个实际changed路径精确闭合；freeze10现存＋1删除、before35、14157排canonical2与新增Net1后的14154非owned逐SHA保持，stage空。

**结论与限制。** 未发现本Task局部未解决阻塞缺陷，结果与检查同轮，可供主控消费后本地checkpoint。reviewer仅在Task追加本唯一审查，未改Result、产品、ledger、证据或运行native/build/test。RecordedOutbound并非A21实机/真实关闭socket；单CMD≤2秒、余迁移、最终Spec审查/Astra及根封存、用户分支收尾仍在后继，本批准不授权remote操作。


## Controller Consumption — TASK-22 r2／Result R1（2026-10-25）

主控完整消费当前r2/§10处置/R1与fresh Independent Quick Review R1 approved；review后前缀50210B／SHA `b9edfb22fd5fcfa48c838e02bf0bd00e9ed37c0f0b0b11d7b6109176a8e2265f`，R1前缀41911B／`8b1d96f07bd56b8f6656be2fb4892f4dd0626f7a1deacea10d2e7e102eb482f0`保持，reviewer已停写。本批准仅局部结果，不代Spec最终核验/测量。

主控实读旧handler/fine/domain/notification、新whole及全部产品/文档diff、slot架构反例；核clock/原三拒绝、正常Commit与具名exception回滚/保护cause、原单ACK及Failed隔离和业务ERROR位置。实读Net02真实新CMD日志/原mapper异常及旧筛选器等待，确认VC-B2身份适配；新增Net源逐byte仅一literal，final03其它原十源等freeze02，未改变Host/期限/错误过滤/其它断言或产品。11源（10现有＋1删除before SHA）/原before35/14154非owned逐SHA均核；198项manifest含Task前缀/删除恰等199个实际changed路径，stage空。

主控只读重算13组对应及108个具名items时间、三GUID跨phase双射/FK，除normal拒绝外层明确typed差异外全部业务值/字节/列保持，same-run回滚无豁免；正常拒绝defaulttyped与pre/ConfirmedRollback原raw各自强断言同cause/精确sourceclose/nofence保持。有效cap30真资产RED与cap0第二事务RED、两个正控及原bytes恢复4/4证据已消费，旧诊断原raw不抹掉。

完整raw Counter另算原8900全保、N16/D0、8916=8896pass+2准确旧Dimfail+18skip；最新App5143/Protocol1774/Net199与四controls同freeze03，另1800明确Task20库存。原DFO CLI再运行exit0、74/7controls/outside-exe-orphan0，Golden15对HEAD/f01dc226逐byte，Char10在Net03通过。strict原validator通过，八个原物理反例逐项重验按原理由拒绝；canonical仅65558和原SellItemCommand两ID，其它逐值等HEAD、md旧50046B前缀保持。

原§10有界scope扩展按实证实现，不重开B3/P4/改变WHAT/提交点/三raw/participant/core。前21Task committed done/22唯一/依赖仅21及DAG就绪证明保持，当前HEADa6f19c44且writer/native皆停；允许本Task精确owned集合本地checkpoint。02未通过的日志身份筛选已由target和03全量证明适配，不能拿02本身当成功。Task21的TCP拒绝未知根因仍如实保留，不因本轮Net通过声称已修。

P4 T-004尚pending117message/171fine，另72source/5protection/8transition；检查点后按剩余实际依赖再规划最小有用簇，不以22个Task称Spec2完成。原A、最终Spec审查/单CMD2秒测量/Astra及用户收尾仍后继，无remote/版本/tag/游戏/部署/cleanup授权扩展。
