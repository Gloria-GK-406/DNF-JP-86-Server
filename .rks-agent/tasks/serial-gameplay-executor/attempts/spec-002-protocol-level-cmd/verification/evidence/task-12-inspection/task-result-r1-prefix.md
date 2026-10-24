# TASK-12 — GameBuyItem 双精确 CLR 家族与普通 NPC／Housing 完整协议 CMD

## Task Unit Contract

- Revision: 2（本契约及详细计划的同一修订）
- Outcome and references: 交付[Plan P2](../plan.md) T-004 的下一最小完整逻辑协议：GameBuyItem 的普通 BuyItemRequest 与 HousingBuyItemRequest 绑定同一 whole CMD，有限 CLR 家族闭合、未知派生拒绝；普通 NPC 用普通 Write 与明确 ProtocolNonEffect 的单元级 NonRequest recorder，Housing effect 用已接受 Main／replay。删除两个旧 fine command/共用 handler，保原购买、拒绝回滚、Quest同事务、通知隔离和协议字节。对应[Spec B2](../spec.md) RQ-001/003～009/012/014/015、AC-001～005/007～009；只完成本协议和实际必要共享依赖，不宣称全部效果/来源/ambient清零。
- Owned boundary: 下文精确生产／测试／文档路径、本Task及task-12前缀证据、migration-ledger两文件的三个已证ID。Spec/P2/契约/root/A/baseline/original/旧Tasks/reviews/challenges/旧证据只读。不迁ShopPurchaseCount、其他Housing动作、CERA/BuyItem以外物品协议，不改变业务规则、SQL、数据模式、协议字段、执行器/事务算法；不新增B2 raw出口、Compose、延迟编码或sender/工厂注入豁免。
- Required checks: 同provider真实decoder两形状→dispatcher→唯一bus→普通Write/Main→SQLite→typed projection/NotificationBus→实际outbox；有限族启动闭合与未知CLR/派生、cap生命周期/错误来源、真实拒绝回滚及原业务/字节/重放oracle；至少一个有价值可编译mutation红绿并恢复。共享registry及unit能力变化采用完整Release build＋八full、准确原2fail/18skip、原DFO74＋七唯一control、Golden15/Characterization10；8556＋N－D、capture/精确三行闭账/strict原八坏副本及清单，见末节。
- Dependencies: TASK-11 r4/Result R1/required独立approved及本地检查点`1cf5051a4566eefa093d3082e73f65a6de130b99`；其原49源freeze03及最终formal03。消费其真实Main/唯一admission/显式reader-sink/opaque attempt/失败收口，不重做该Task。B2/P2及其匹配独立gate保持，Spec baseline仍`10a7218a779493494b138bd6b43cc0fc1a84aa6a`，不以本Task改中心方案。
- Result review: required — 有限族错误可使Housing降级普通请求或未知派生执行，错误NonRequest/拒绝处理可造成部分扣款、重复结算或错误围栏；编译和codec不足以揭示。独立结果快审核真实两分支、原oracle、同容器及冻源证据后由主控检查点。Task-plan不新增独立审批，由主控readiness后安排执行。

## Detailed Plan

### 1. 接受起点、边界与预备

开工由主控核精确HEAD、`task/refactor-session-20261021`、原worktree和唯一Task/owned变更；读AGENTS、ArchitecturePhilosophy、RefactorPreferences、B2/P2及其gate和TASK-11 r4/R1。无`.codegraph/`则rg优先。TASK-11最终formal03库存为8556＝8536pass＋2准确原Dimensionfail＋18skip：App4802、Protocol1762、Net199、Host203、Http57、Infra976、Persistence477、Pvf80；是数量起点，不是本轮通过。旧formal01误覆盖Window Theory两case、formal02 Net端口未证根因均保历史，不新增允许基线；本轮逐case审计防重演。

继承中文、本地实施／核验授权，主控负责检查点；禁止push/PR/merge/远端issue/部署/cleanup/版本改动、运行df_game_r/DNF.exe或起游戏实机，不创建下一Task。若出现中心设计冲突或白名单外必需source，先给主控具体调用及最小依赖，不用通用factory/ambient/宽例外补洞。

先在task-12目录保存**精确owned**源码/测试/文档beforeimages及行尾/BOM，另外只保存非owned tracked路径的SHA清单；不得再次复制几千份全仓before快照、bin/obj/旧研究或旧Task证据。新增文件列expected-add；原文件删除列expected-delete；限定编辑后再冻结同一源身份。计划当前只有本文件，下面授权的是后续executor范围。

### 2. 精确可写生产与文档白名单

路径相对worktree；新文件仅下列具名职责，不能按目录扩授权。

| 路径 | 允许动作／共享边界 |
|---|---|
| `Server/src/Dfo.Server.Application/Commands/CommandHandlerRegistry.cs` | 协议binding按(MessageType,精确CLR)去重；允许同logical的有限族但只能归同一CMD；完整族/exit规则见§3。既有CommandRoute/Kind、单CMD身份和Freeze保持 |
| `Server/src/Dfo.Server.Application/Dispatching/MessageHandlerRegistry.cs`、`NetworkMessageDispatchingExtensions.cs`、`NetworkMessageDispatcher.cs` | 一个logical一条route；明示/冻结允许CLR集合，薄handler参数兼容检查和runtime exact集合成员检查，保普通单shape/遥测/legacy闭合及注入规则 |
| `Server/src/Dfo.Server.Application/Commands/ICommandWorkUnits.cs`、`Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs` | ordinary Write具名声明NonRequest footprint；默认None，runner只按已建ProtocolNonEffect/唯一精确actor签发并关闭。保所有Main/ReadOnly/rollback/drain/typed收口；不加入业务异常类型知识 |
| 新`Server/src/Dfo.Server.Application/Effects/ExplicitNonRequestFootprint.cs` | 仅上述单元的内部具名能力，绑定request/job/unit/transaction/唯一actor，完整实现现IEffectFootprintRecorder并验证；不是DI服务/默认实例/生命周期或legacy适配 |
| `Server/src/Dfo.Server.Protocol/Game/BuyItem/BuyItemHandler.cs` | 薄化为只注入ICommandBus，传原消息/精确Dispatch并等待；不保旧业务编排 |
| `.../BuyItem/BuyItemCommand.cs` | 删除旧BuyItemCommand、HousingBuyItemCommand、BuyItemCommandHandler；仍需的BuyItemOutcome移到下项，旧符号完全零，不留转发/Obsolete |
| 新`.../BuyItem/GameBuyItemCommand.cs` | 一whole CMD及handler，返回既有BuyItemOutcome；可同文件保结果record/纯错误码映射，或同切片新`BuyItemOutcome.cs`只承载这些值/纯转换。执行原业务与两种明确unit，见§5 |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 删除两个Tx fine注册，新增一Direct/Protocol whole注册及同logical两个精确binding；单条薄route声明族。仅复用已有CommittedReplayCurrentStateBatch，不声明SettledRequiredExplicit |
| `AGENTS.md` | 只同步当前opt-in BuyItem、有限族、普通NonRequest/Main及旧fine已删的事实；其余legacy/来源/删除责任保持 |
| `Docs/Agent/ArchitecturePhilosophy.md` | 仅§1 BuyItem链示例旧命令名及§5 BuyItemHandler承载Npc分支的当前状态描述随迁移对齐；不泛重写历史/哲学/其余偏离 |

以下业务及骨干**只读复用/核验**：BuyItem `Messages.cs`/decoder/encoder/`NpcShopPurchaseCompleted.cs`，`NpcShopDomainService`/规则/表、MarriageDomainService.Housing/HousingOperations.Shop及其接口/存储、Quest领域、NotificationBus/EventService、SelectedSessionSafety、CommandBus/CommandRequestContext、CommandDispatchingExtensions、Task11 ExplicitEffectRequest/ledger/reader/sink/attempt/outbox与RecordedOutbound。它们现有API已足够；若实际需修改不能默默越界。特别不为本Buy向CMD注入槽位映射、safety/ledger/reader工厂、bus、sender或locator；Use的既有槽位豁免不套用。

### 3. 有限 CLR 家族：保一个logical一CMD，不能用继承作运行许可

已接受API的`ProtocolCommandMessageBinding(MessageType,Type)`本就表达一对；保它，CommandHandlerRegistry只将原“MessageType唯一”改为**pair唯一**，并在全部Protocol注册间检查每个MessageType只归一个CommandType，即使不同CLR也不能拆给不同CMD。拒重复pair、空族、非IClientNetworkMessage、接口/abstract/open generic等不实际可解码shape及跨CMD歧义。不要静默Distinct掩盖重复注册。

消息侧保一logical一条MessageHandlerRegistration/Route及薄handler。最小表示为：现MessageClrType明确保留为薄handler声明的参数类型，另增加并冻结`AllowedMessageClrTypes`有限集合表示实际准入形状（普通旧调用正规化为单元素，不是第二份可替代allowlist）。RegisterProtocolMessageHandler的现泛型TMessage仍是handler参数，可显式追加有限shape；本Buy声明[BuyItemRequest,HousingBuyItemRequest]。族必须非空、精确不重复、全部实现消息接口且与该handler参数静态兼容；registry逐logical对照同CMD所有binding，允许集合必须完全相等，无缺项/多项。反向闭合不能再要求每pair一条logical route。集合复制只读，调用者改原数组不能改变注册/route。

兼容性检查可用类型可赋值性证明handler接得住，但dispatcher执行许可必须`message.GetType()`为声明集合的**精确成员**；未知BuyItemRequest派生即使CanHandle为true也在业务前拒绝。CommandBus现136～137已按实际MessageType/GetType精确pair复查，保持，无需改成AssignableFrom。反例同时打dispatcher和直接whole-bus，不能只有某入口守住。Logical未注册帧静默丢弃、遥测日志约束及single-shape原行为不改。

exit校验不能把混合族统一当Effect：SettledRequiredExplicit仍要求全部binding为Effect，混合族声明它启动拒绝；已有CommittedReplayCurrentStateBatch可在明确含Effect形状的混合族声明，但只能实际`ProtocolEffectSelected`的CommittedReplay阶段签发，普通shape/首次发布/Rejected永远不得取得。仅此既有共享replay许可，绝不新增enum/新raw出口或首购许可。纯NonEffect族声明任一Effect出口仍拒绝；新Buy登记元数据只包含该replay值。旧Use两许可及其原反例完全保留。

### 4. 单元级 ProtocolNonEffect NonRequest recorder

Task11 `unit.Footprint`目前仅Main供给；不能借context.LegacyFootprint、LifecycleSource或测试lease用于新生产路径。给CommandWorkUnitDeclaration增加单一具名选择（如`CommandFootprintMode.None/NonRequest`，默认None），仅普通**Write**且request实际为ProtocolNonEffect并有唯一精确actor participant时合法。Main继续由原机制供selected footprint，不能同时声明NonRequest；ReadOnly、Selected/Rejected/TrustedInternal/缺request、零或多owner、未声明均不能获此能力。本Task不为offline/internal/multiowner提前扩策略。

runner在校验request/current/participants且真实写事务Begin后，创建内部ExplicitNonRequestFootprint并通过既有unit.Footprint交给action；普通未声明unit getter仍拒绝。此cap绑定实际request/job/该unit及该事务、actor session/account/character和精确物理connection；验证actor属于本ProtocolNonEffect的Dispatch，不接受同ID替换连接或另一用户。实例只由runner创建，不注册DI、不存singleton/run/pending。必须在当前action/活跃事务阶段才可用，action退出/提交准备或unit关闭即失效，finally失败路径同样关闭；不能借仍未关闭request继续用旧unit能力。

所有Demand/Record（含空slots/effects/rental）先验证same job/healthy/request未失败未closed/捕获登录权威/current/owner/unit及捕获事务仍活跃；错owner和错误状态在实际持久写前拒绝。验证事务仅核已显式捕获边界，不从环境事务寻找请求。NonRequest不创建/修改effect entry、settlement footprint或Main资格；Record合法时只表达本来源无需请求重放账本，不能是任意权限noop/default。普通facts仍是facts，不给合法commit回调增加cap活性要求。复用现接口及NpcShop已接受的写前Demand/Record，不改领域签名/规则。

### 5. 完整 GameBuyItem 用例与拒绝回滚

新`GameBuyItemCommand(NetworkDispatchContext Dispatch, BuyItemRequest Input) : IProtocolCommand<BuyItemOutcome>`保原Game metadata；同一handler接受两种精确CLR。CMD仅注入原ISelectedSessionDomainService/IOnlineUserQueryDomainService/IMarriageQueryDomainService/INpcShopDomainService/IMarriageHousingDomainService/IQuestProgressDomainService、TimeProvider、ICommandWorkUnits、IGameCommandOutbox、ICommandProjections、ILogger。强类型请求、cap/recipient是显式方法数据，不注入机制factory。

按原次序解析session/online；缺任一即原NoSession拒绝，不强造unit或为已Rejected请求重选升级。保原NpcId==20011才读取CoupleAccounts，否则[actor.AccountId]；此只读调用仍在购买unit之前。**结算许可看实际CLR/本次request，业务走向仍按NpcId**：普通精确BuyItemRequest对应ProtocolNonEffect的ordinary Write＋显式NonRequest；Housing精确形状只在本次Selected并session==captured actor时Main([selected.Recipient])。未选中Effect仍原拒绝ACK/无Main，无session后续出现不得升权。不能按npc把普通消息升级Effect，或把所有父类/派生消息统一降级；不新增原decoder从未声明的归一化规则。

普通分支的participant用本次actor、Dispatch精确connection和Input.Metadata构造，交runner验证；Main用已捕获recipient。旧Housing marker仅actor，并非IMulti，couple AccountIds是领域归属输入、HousingAudience是投影收件人，不能擅自新造配偶selected参与者。每种有效分支只一原Tx对应unit，NPC调用`shops.PurchaseAsync(...clock.GetUtcNow(),unit.Footprint,ct)`；Housing原PurchaseAsync没有footprint/ApplyEffects调用，按原API直接调用，不为迁Buy扩其完整Execute/编辑/种植链。Main最低Inventory/LovePoints/Housing仍来自原HousingBuyItemRequest。购买成功后在**同一unit** Report QuestHoldingsChanged并保NotificationSnapshot；正常无receipt结果按原返回，不人为RequireRollback所有业务拒绝。

实际冲突必须这样处理：新runner所有逃出action的异常会sticky失败，不能在await WriteAsync外catch购买拒绝后照常成功。只在该unit action内包原Purchase＋Quest调用，**仅catch NpcShopPurchaseRejectedException**，调用unit.RequireRollback并返回`BuyItemOutcome.Refused(reason)`；runner先真实Dispose/ConfirmedRollback后whole CMD才登记原拒绝。与旧异常路径一样，Housing该拒绝留下ConfirmedRollback可重试，不能假Commit；异常类外的SqliteException、取消、typed uncertainty、rollback/dispose错误全部继续默认失败/围栏，不清sticky、不加通用catch白名单。原正常无写拒绝返回仍按原事务正常完成/最低Main投影，与具名异常真回滚区分。

unit结束后原receipt信息日志/拒绝日志保持，成功用ICommandProjections.ProjectAsync原TheseCharacters（HousingAudience非空用之，否则actor）及NpcShopPurchaseCompleted；无receipt才typed outbox登记原错误码ACK。原Project→NotificationBus逐recipient catch Compose/SendBatch普通编码异常为Failed且继续，本CMD不把Failed outcome重新抛出、不加raw许可或把捕获变sticky。调用方取消及外层独立scope错误仍保现默认保护。首购不是RequiredExplicit raw出口，不能借replay许可包通知。

原输出不改：材料变更→适用HousingStorage/LovePoints→pile/Housing的绝对库存→零增量ACK→适用gold→非pile库存→绝对限额→Quest进度；配偶仅共享storage，actor精确原session/connection/InWorld过滤，119B ACK/101B item及尾0、stack gain、限额和原字节保留。Committed replay由Task11框架ordinary Write reader→事务外sink执行，不调用购买/Quest/首购通知、不再扣款或重ACK，原固定最低needs和current投影保持。仅既有共享CurrentStateBatch许可适用其实际encoder错误，不扩大B2。

### 6. 测试及准备的精确白名单

以下均在`Server/tests`；仅有真实caller/反射断言/目标保护的文件可改，不按名称批量改业务断言。没有生产接口签名变更时，不改MarriageHousingTests/SelectionWorkflowCurrencyTests等纯领域driver。

| 路径 | 目的/保留方法 |
|---|---|
| `Dfo.Server.Application.Tests/Commands/ProtocolCommandExecutionTests.cs` | 新增同logical有限族闭合/反向闭合/冻结/duplicate pair/crossCMD/非法类型/mixed-exit/未知派生正反例；保全部原single-shape与exit/authority/source/catch测试 |
| 新`Dfo.Server.Application.Tests/Transactions/ProtocolNonEffectFootprintTests.cs`；既有`Transactions/CommandWorkUnitsTests.cs`仅必要同职责测试复用 | 真实ordinary写unit cap正例/未声明、ReadOnly、各错误request、owner/target/job/closed/lifetime和catch后失败；保默认Main/ordinary/rollback/drain/typed所有测试。新probe只在测试实际注册具名CMD，不引入生产testsetup CMD/任意业务Func命令输入 |
| 新`Dfo.Server.Application.Tests/Game/BuyItemCommandFlowTests.cs` | 同ShopFixture/provider的真实decoder两shape→dispatcher/bus→SQLite/projection/outbox正例及未知派生；普通无effect ledger/Main、Housing同seq重放/拒绝真回滚。可复用原测试数据/Marry准备但不建平行测试平台 |
| `Dfo.Server.Application.Tests/Inventory/NpcShopPurchaseTests.cs` | 实际两个direct handler及ShopFixture.Buy/Command驱动改外部whole输入，保SQL/限额/金币/材料/装备/Quest/current/投影失败/并行入队原oracle。只改fixture必要局部，Query旧handler保持；ShopSender故障点/RecordedOutbound语义保持 |
| 新`Dfo.Server.Application.Tests/Inventory/WholeBuy.cs` | 局部测试输入helper按16B及实际decoder/fixture counter构造两形状/whole输入，提供原BuyItemOutcome观察，不包新bus进TestGameplay，不新增生产接口或来源特权 |
| `Dfo.Server.Application.Tests/Inventory/HerasPartsExchangeTests.cs`、`GoldSilverHornRewardTests.cs` | 唯二其它直接old Buy fine构造，改WholeBuy/外部whole真实NonEffect输入；保PVF材料数量/装备实例/奖励及后续未迁Package行为断言 |
| `Dfo.Server.Application.Tests/Sessions/HousingNetworkSettlementTests.cs` | 原两case保满包金币/点数/错误4/不误fence，以及FailBatch首次正常返回、同seqcurrentstorage不二扣/无BuyACK；可用真实decoder替手工new以闭合分叉，不移动注错 |
| `Dfo.Server.Application.Tests/Sessions/EffectSettlementRegistrationTests.cs`、`World/CommonExperienceEffectTests.cs` | 删除旧Housing fine的已删除类型期望，同时加入wholeGameBuy的Protocol/two exact shapes/普通与Main分流断言；不能将整个mixedwhole加入IRequestSettlementCommand清单或删保护 |
| `Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs`、`Game/BuyItem/BuyItemCodecTests.cs` | 实际全图一logical一CMD/两精确shape/旧两注册0/仅共享replay权限；新增npc20011 decoder分类＋原普通/坏长度；保原四wallet/119B/zeroACK/限额bytes |

只读必须跑：`Game/BuyItem/NpcShopGainProjectionTests`、`MarriageHousingCodecTests`、原MarriageHousingTests家具回滚/提交、Task11 ExplicitEffectCommandTests/B2两raw原oracle、全部架构注入/Slot边界、真实Net Characterization/EffectReplayWire/Golden。不修改`Support/RecordedOutbound.cs`（Task11已同Registering alias窄接口）、TestGameplay/测试lease helper/全局harness。若实际编译揭示表外caller，先给主控确切符号，不泛扩范围。

ShopFixture.Buy必须在executor外进入新dispatcher/wholebus，seed/观察保原TestGameplay作业；不得保“TestGameplay内调用薄handler→新bus自等”或解锁inline。多数Command(item,count)可返回whole结果；原`A_purchase_from_the_previous_selection...`显式old session不是新wire输入，不往生产wholeCMD塞可伪造owner。保其两层oracle：新协议仍取当前真实session；旧owner拒写用原INpcShopDomainService在明确test lease/真实事务下直接验证，随后对原committed receipt投影仍不得通知replacement。撤销连接case同样不能用新bus静默authority丢弃的null结果替代原领域NoSession；将领域拒绝与协议权威保护各保真实断言。测试转换附旧case→新case/driver用途映射，不删SQL/字节条件换弱断言。

### 7. 可达正反例、RED/GREEN与实施顺序

先保存before/capture并落最小失败oracle，再按“有限族闭合→单元NonRequest→wholeBuy两分支/真拒绝回滚→旧caller/注册删除→定向→冻源正式”推进；不能只交基础设施而Buy仍legacy。编译签名迁移不当行为RED。

- 普通实际NPC：原材料/金币/限额/Quest同事务，record validates但effect ledger/admission/Main为0；第二fresh购买真实再扣一次，不能误走effect去重。真实SQL触发器使商品写失败时金币/材料/限额/Quest无残留、无通知；恢复触发器后下一请求成功。
- Housing实际decoder：同provider首购点数98/storage1，通知encoder FailBatch被原NotiBus隔离且dispatch正常完成、entry Committed不新增fence；同seq只重放当前storage且无ACK/二扣。改变当前存储再replay证明不是旧receipt。满bag具名拒绝实际回滚前扣点/金，原错误4/无fence且下一请求合法；同时核entry ConfirmedRollback而非Committed，随后合法同fingerprint可重试、原Ordinal窗口不延长。不得把首次通知故障改raw Throws来迁就新机制。
- 有限族：一个logical两形状都唯一whole，未知第三派生在dispatcher和直接wholebus都拒业务0；duplicate pair/跨CMD同logical不同或相同shape/漏族/多族/非法接口/原数组修改都拒或不影响冻结。mixed声明SettledRequiredExplicit拒，ordinary shape尝试Main/replay能力拒；Housing实际Main保reporter/current且不凭audience增参与者。
- NonRequest：实际普通Write/action内合法owner cap，empty Record仍验证；未声明/ReadOnly/Selected/Rejected/Internal/错误owner/同ID不同物理连接/closed/跨job/unit结束后重用在写前拒绝，下一请求不串singleton。cap不创建effect entry，不获取Main；捕获后再结束unit使用仍拒。未完成unit/原sticky/default postcommit多参与者/Task11 scope双错不回归。

至少一可编译mutation：优先将dispatcher的exact allowed membership改为按handler base可赋值，未知派生会到薄handler但仍被bus拒；因此oracle必须区分“未知派生不允许到handler/bus”，例如真实计数薄handler进入/queue调用为0，不能只Assert最终throws而被第二层保护遮住。另可选删runner的明确NonRequest声明/owner/lifetime guard，用真实unit/SQL写前0区分。选最有稳定真实oracle的一项即可，先证正例确到目标；红不得来自缺注册/非法fixture/编译/超时。变异before/SHA、try/finally字节恢复、恢复后同目标绿，再冻结，不叠镜像mutation。

### 8. 正式核验、数量、闭账与交付

本Task改变所有协议使用的注册机制及共享unit能力，故正式采用完整`dotnet build Server/86JP.Server.slnx -c Release --no-incremental` 0warning/0error，随后八项目各独立无filter full：`dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`；项目为Dfo.Pvf.Tests及Dfo.Server.Application/Protocol/Networking/Host/HttpApi/Infrastructure/Persistence.Tests。原xUnit类并行/期限/Skip不变；在本机FSEvents可用环境沿原runner/caffeinate及必要require_escalated，保每step cwd/HEAD/完整command/UTC/exit/elapsed/资源与console。每个session等自然结束，不TERM父runner/活跃测试树；产品改后新freeze＋新完整轮，诊断/无效/最终轮不混拼。

数量以Task11 final03逐项目/逐case(display＋status＋multiplicity)作原inventory：8556＋实际N－D；真实rename一对一映射且附源码driver目的，不只比总Assert/总数多重集。原Window127/128等原case/status不得误删，恢复不算N。仅允许`DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`及`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`准确原Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException，App16＋Infra2原18skip；Host/Net/SQLite新失败诊断，不添基线或盲重试。

每最终轮新鲜DFO目录含八full，原七程序集唯一control齐、原74expected精确身份/数（App62、Net7、其余五各1）不放宽；新增故意反例仅按定向实得精确身份/count另列，不凭源码改表。外/exe/orphan0核实，exe0只观察；Golden15对既有f01dc226及Task11产品检查点原bytes保持，Net Characterization10逐名pass/0skip、codec原bytes保持。

ConfigureServices-only capture before/after各独立task-12目录，不BuildProvider/起业务；复用只读probe-tools/migration-ledger.py/export及既有DFO工具，`--output`必须新路径，禁止覆盖旧输出/工具/证据。现export的registered_message_clr表示handler基参数，本Task保该含义；有限族另用task-12新检查脚本记录route冻结family/wholebinding pairs/runtime闭合，不能伪把两shape算两logical。预期173messages不变；205commands删两Tx加一Direct whole→204，12Protocol＋1Internal＋191Legacy，Kind163Tx/28Direct/9RO/4Workflow，message12Protocol＋161Legacy/14telemetry，须以实际捕获复核，不硬填。原Use两exit保持，newBuy仅既有CommittedReplayCurrentStateBatch，普通shape无签发权。

开放ledger两文件只拟闭三个frozen ID：`message:65557`、`command:Dfo.Server.Protocol.Game.BuyItem.BuyItemCommand`、`command:Dfo.Server.Protocol.Game.BuyItem.HousingBuyItemCommand`。按实际whole/普通Write/Main/同provider原测试证据填introduced artifacts及completion_result_path，state键用真实`state`；不改original/baseline，不闭sources/protection/transition或其他message/command。当前messages11closed148pending14retained、commands11closed193pending、sources1closed72pending12retained；预期pending147/191/72（closed12msg/13cmd），按实数核，5protection/8transition仍pending。无必要不触其它行，确需过渡notes也先交主控批准。

先`preclose --current-task <本Task精确路径>`核拟闭真实Artifact；executor追加Result后strict validate/selftest原八坏副本全拒，未存在Result不伪填通过。任务证据限`verification/logs/task-12-*`、`verification/evidence/task-12-inspection/**`及必要task-12脚本；保精确owned beforeimages、未授权SHA、mutation恢复、final freeze、case映射/capture/ledger/meta和manifest。manifest只含本轮真实owned文件，无全仓before复制；保存Result R1 prefix供独立review只追加，主控核物理路径集合及SHA后本地检查点。此规划阶段不写Result/review/status。

Result须区分两种来源/原Tx及投影吞错与B2 raw边界，说明新NonRequest能力实际守住什么、finitefamily哪些入口/反例已证；保所有诊断和真实未解失败，不把编译/旧历史当本轮通过。其余147消息/191命令/72来源、legacy replay/footprint/Lifecycle清除、T011/T012、单CMD≤2秒最终测量、Astra及Spec/根/用户收尾门留主控后继，不提前宣告完成。


## 执行增补 r2 — 原显式机制探针的独立逻辑身份

主控已读原TASK11接受后的 `Commands/ExplicitEffectCommandTests.cs` World 503～505、MatrixProbe 572～575和真实Use用例187～204：World在ProtocolModule真实GameUseStackable注册之外，又把MatrixProbe绑定同一个GameUseStackable／UseStackableRequest。TASK12 finitefamily的一logical唯一CMD判据正确拒绝此冲突；focused04自然149＝117pass＋32fail，32旧机制case在业务前失效，不能新增基线或删掉真实协议注册求绿。执行者已暂停全部共享写且无native，主控据真实caller补精确白名单，仅增加 `Server/tests/Dfo.Server.Application.Tests/Commands/ExplicitEffectCommandTests.cs` 的有界fixture闭包。

给Matrix探针一个不冲突的test-only logical身份和具名 `IEffectSettlementRequest` 包装：显式携带原Use输入（及必要Mode），Metadata／MinimumReplayNeeds原样透传；MatrixProbe.Message使用包装、Frame和所有实际业务仍读原Input。配对注册的binding及正确薄route必须符合真实双向闭合，薄route只注入bus并传明示输入，不新增生产消息、decoder、目录项或机制豁免，不改metadata raw/hash/ordinal/sequence来绕账本。测试身份需在该fixture中证实不与生产注册冲突；不能借AssignableFrom放宽实际业务许可。真实GameUseStackable whole注册保持，原An_unselected用例继续调用真实whole并守SilentACK／ordinal不消费／后续真实一次扣物，不替换为probe。

保原32全部case身份／InlineData、业务动作、SQL数量、entry和projection身份、原实际FaultingEncoder、cause引用、scope组合／token／多参与者／sourceclose及帧字节断言，不删并用例，不放宽异常/围栏或把探针说成真实wire Use。Result记录只是原core探针驱动身份与注册闭包迁移，原fixture冲突诊断不计本轮行为RED；恢复后新完整定向及最终同一freeze完整正式核验。此为注册判据生效揭示的实际测试caller补漏，不改产品接受条件／Spec B2／P2／旧Task或证据，无新Task-plan审批门。其余白名单与权限原封。


## Result

- Revision: 1（Task r2；HEAD 1cf5051a4566eefa093d3082e73f65a6de130b99；最终 source-freeze-01；本轮结果待主控 required 独立快审，未提交）
- Outcome: done
- Changes: 完整 GameBuyItem 迁为唯一 GameBuyItemCommand，薄 BuyItemHandler 仅传原 Input/Dispatch 并等待 bus；普通 BuyItemRequest 与 HousingBuyItemRequest 保一个 logical、两个明示精确 CLR，旧 BuyItemCommand/HousingBuyItemCommand 及共用 fine handler/两注册实际删除，不留转发。按原次序读取当前 session/online、仅 npc20011 查询 CoupleAccounts；业务分支仍按 NpcId，实际 CLR 决定来源/结算许可，不凭父类或 npc 升级。原 Purchase 与 QuestHoldingsChanged 同单元、同真实事务；原查询/规则/领域/decoder/encoder/通知业务均只读复用，见 [workunit-map](../verification/evidence/task-12-inspection/workunit-map.md)。
- Finite family: command binding 保原 (logical, exact CLR) 形状，按 pair 去重且整个注册图每 logical 唯一 CMD；message 仍一 logical 一 route，handler 参数含义保持，另以冻结复制的 AllowedMessageClrTypes 与同 CMD 的该 logical 全部 pairs 双向完全闭合。拒空族/重复族或 pair/接口/abstract/open generic/跨 CMD 拆分/缺项多项。dispatcher 对实际 GetType 精确成员检查，原 bus 继续独立 exact pair 复查；实际两个合法 shape 均到同一 handler/bus，未知派生在 dispatcher 业务/入队前拒绝，直接 whole-bus 也拒绝。mixed family 不得声明 SettledRequiredExplicit；Buy 仅既有 CommittedReplayCurrentStateBatch，原 Use 两具名出口保持，无新 raw 类型/首购许可。
- Ordinary source / cap: 普通精确 shape 进入一个 ordinary Write，只有明确声明 NonRequest 且 request 为 ProtocolNonEffect/唯一当前 actor 才由 runner 在真实事务 Begin 后签发内部 ExplicitNonRequestFootprint。默认 None、Main/ReadOnly/Selected/Rejected/Internal/缺 request/零或多 actor 均不得取得该能力。cap 捕获本 request、实际事务、actor session/account/character/物理 Dispatch connection，所有 Demand/Record（含空 slots/effects/rental）先验证同 job、healthy/open/current/登录权威、精确 owner 及活跃事务；不查 ambient request、不注入 DI/默认实例。action finally 关闭早于 Prepare，旧 unit/下一 unit/prepare/scope closed/独立 global 测试 job 重用均拒写。真实 gold/SQL、无 effect entry、无 Main、第二 fresh 请求真实再购买及 caught 错 owner sticky 证明边界，不以 noop/assert 日志代替状态。
- Housing / refusal: Housing 仅本次 selected 的捕获 reporter Main，最低 Inventory/LovePoints/Housing 与原请求相同；CoupleAccounts 是业务归属输入，HousingAudience 是通知对象，均不增加事务 participant。无 session/初始 Rejected 保原 NoSession ACK，不造 Main 或后来升级。只在 action 内 catch 具名 NpcShopPurchaseRejectedException，RequireRollback 后返回原拒绝值；runner 真实回滚确认后才登记错误 ACK，Housing entry 实证 ConfirmedRollback/无 projection，扣点/金币/库存恢复，腾槽后同 fingerprint 沿原 entry/Ordinal 重试成功，不 clear sticky。正常无 receipt 拒绝仍原正常提交/最低 Main；其它 SQL/取消/typed/unknown/rollback/dispose 失败继续默认保护。
- Output / replay: 成功保原 ICommandProjections→同 EventService→NotificationBus；原逐目标 Compose/SendBatch 普通故障返回 Failed 并继续，whole 不重抛或添 raw。保原材料→HousingStorage/LovePoints→pile/Housing 绝对库存→零增量 ACK→gold→非 pile 库存→绝对限额→Quest 次序及 actor/配偶过滤，119B ACK/101B item/尾0 等原 byte oracle 保持。无 receipt 的拒绝 ACK 则直接 typed outbox 登记；实际正常提交后的 ACK encoder IOException 实证仍 typed/default fence，不能把首购通知隔离泛化为所有输出吞错。Housing FailBatch 首购正常完成、entry Committed、points98/storage1/no new fence；改变真实 storage 为3再同 seq replay，只读当前帧 count3、无 ACK/二扣/第二次购买。replay 继续原 ordinary Write direct reader→事务外 sink，仅既有 replay 权限，不改 Task11 核心/opaque attempt/outbox/RecordedOutbound。
- Driver / preserved oracles: ShopFixture.Buy 用原16B真实 decoder 后外部 dispatcher，Command 用同真实输入 whole-bus 观察原 BuyItemOutcome，不包新 CMD 进 TestGameplay；Heras/GoldSilver 两个其它旧 fine caller 同样迁移，后续未迁 Package/Lottery 保旧驱动。原 previous-selection 和 revoked-connection 的 owner 不塞进 wire：明确 test lease/真实事务直接领域 NoSession 保护，与新协议读取当前真实 session 分层验证。首购 receipt 已断言成功，清 Messages/Batches 仅隔离随后 replacement 观察，原旧 receipt 不通知 replacement 的 Empty oracle 保持；独立 fresh-wire 用例证明当前 actor 购买及旧库存不动。所有原 SQL/金币/材料/限额/装备/Quest/字节断言保留。Task r2 只把原 Task11 Matrix fixture 从冲突 GameUse logical 改独立 test-only logical/具名 Effect 包装/正确薄 bus route，明示透传原 Input/Mode/metadata/minimum；真实 Use 注册与原实际 Use 用例保持，原32 case/store/entry/projection/cause/帧断言未删，fixture 实际解析 MessageHandlerRegistry 证明闭合，不声称该 probe 是真实 wire。
- TDD / mutation: family-red-01 可编译且实际到达注册边界，原规则拒同 logical 两 shape；family-green-01 原18+新增1=19/19。最终 mutation-red-01 只将 dispatcher exact membership 放宽为 handler base assignability，两个合法 shape 先实际 queue2，未知派生错误到 bus 使 queue3，即使 bus 随后拒绝仍被真实 oracle 揭示（expected2/actual3）；try/finally 原 bytes/SHA 恢复，mutation-green-01 同目标1/1后冻源。其余矩阵依真实 SQL/cap/request/entry/fence 状态证实，未把缺 using、非法 fixture 或未到目标的异常当 RED。见 [mutation-result](../verification/evidence/task-12-inspection/mutation-result.json) 与 [test-delta](../verification/evidence/task-12-inspection/test-delta.json)。
- Final formal01: 同一26路径 source-freeze-01 后 Release --no-incremental build 0warning/0error，八项目分别无 filter full --no-build/console detailed/原 class 并行/5m/skip/期限，全部自然结束。8602＝8582pass＋2准确原 Dimensionfail＋18原 skip；Pvf80，App4846＝4828pass/2fail/16skip，Host203，Http57，Infra976＝974pass/2skip，Net199，Persistence477，Protocol1764。仅原 Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource、All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot 失败，均原 Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException。8556＋N46－D0＝8602；逐 case(display/status/multiplicity) 对 Task11 final03 核原全部保留，无 rename/合并；App 新15有限族+21cap+8Buyflow，Protocol新2，共46全过。完整身份/console/meta见 [formal-summary](../verification/evidence/task-12-inspection/formal-summary.json)。
- Assertions / bytes / capture: 最终 DFO 新 formal-01 目录沿原精确74 expected/七程序集各唯一 control，外/exe/orphan0，不放宽类/数，exe0只观察。Net Characterization10逐名pass/0skip，15 Golden 对 f01dc226 与 HEAD 原 bytes 保持。ConfigureServices-only before01/after01 均不建 provider/不起业务；173msg不变，205cmd删两 Tx 加一 Direct whole→204；12Protocol+1Internal+191Legacy，163Tx/28Direct/9RO/4Workflow，message12Protocol+161Legacy/14telemetry。仅 message65557 route 改变，除旧两 fine/新 whole 外其余实际绑定不变。fresh runtime-family-after-02 纯构造 command/message 两注册表并核精确双向族、原 Use 两 permission/Buy仅 replay，见 [capture-checks](../verification/evidence/task-12-inspection/capture-checks.json)。
- Actual diagnostics: 所有26份 step metadata 完整保留 command/cwd/HEAD/UTC/exit/elapsed/resources 和原 console。compile-01 缺 using、focused-01 overload 歧义均编译诊断；focused-03 为103=102pass+1fail，新测试先 decode 消耗 seq1 后跳 seq7 被真实 counter/ledger 在购买前拒绝，改真实 dispatch seq1/同 seq1 replay，未改生产 counter。focused-04 为149=117pass+32fail，旧 Matrix 与真实 Use 双占 logical 在启动前正确拒绝，按 Task r2 有界 fixture 修复，focused-05 为149/149；protocol-focused01 41/41、prefreeze-focused01 142/142（含原32及原两 B2 actual FaultingEncoder）。family-capture-01 在 /var 别名临时项目触 MSB3202，canonical /private/tmp 新项目/fresh02自然exit0；原证据不覆盖。正式仅一轮01，不拼其它产品轮；两原 Dimension 根因未处理，不添允许基线，见 [diagnostics](../verification/evidence/task-12-inspection/diagnostics.md) 和 meta-inventory。
- Scope / ledger / handback: 26精确 owned source（含旧 fine 删除），22必要 beforeimages，未授权 tracked 仅存 SHA、不复制全仓；未授权原文件、BOM/CRLF/混合 byte、whitespace、旧错误码纯转换、原业务/codec/Golden 均保持，冻源 SHA 与 mutation 恢复一致。ledger 只闭 message:65557、command:Dfo.Server.Protocol.Game.BuyItem.BuyItemCommand、command:Dfo.Server.Protocol.Game.BuyItem.HousingBuyItemCommand 三个 frozen ID，逐项映射原 caller/Tx/participant/保护与现证据；original/baseline/其余 rows 不改。preclose 已通过；Result 后执行 strict validate/原八坏副本及 selfcheck/manifest，证据在 task-12-inspection。保存本 R1 prefix 后停止全部共享写，交主控 fresh required review，再由主控本地 checkpoint；本结果不是独立审批。未 stage/commit/push/PR/merge/远端 issue/deploy/cleanup/改版本/起游戏/创建下一 Task。其余147消息/191旧命令/72来源、5保护/8过渡仍 pending，legacy replay/footprint/Lifecycle 清除、T011/T012、最终单 CMD≤2秒/Astra/Spec/根及分支收尾接受均留后继，不冒称完成。
