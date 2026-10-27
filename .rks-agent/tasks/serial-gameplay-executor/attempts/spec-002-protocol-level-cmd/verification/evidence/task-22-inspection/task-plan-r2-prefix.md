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
