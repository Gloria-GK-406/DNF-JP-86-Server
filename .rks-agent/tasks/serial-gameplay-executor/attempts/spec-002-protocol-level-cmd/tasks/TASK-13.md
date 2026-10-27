# TASK-13 — Reinforce／Disjoint／EnchantByBead 三个完整装备效果协议 CMD

## Task Unit Contract

- Revision: 1（本契约与详细计划的同一修订）
- Outcome and references: 交付[Plan P2](../plan.md) T-004 的有界装备效果簇：Reinforce、Disjoint（NPC/Portable同一逻辑协议）、EnchantByBead各自唯一whole CMD，吸收原handler/fine用例编排并删除四fine；直接消费已接受Main/Footprint/replay/ICommandProjections。保持原前置拒绝与域内正常拒绝的不同结算状态、真实事务/业务/顺序/字节。对应[Spec B2](../spec.md) RQ-001/003～009/012/014/015、AC-001～005/007～009及F-34/F-88/F-93/F-112；只完成三消息/四命令，不宣称所有装备、效果、来源或ambient完成。
- Owned boundary: 下文精确源码/测试/AGENTS、本Task和task-13证据；migration-ledger两文件只准三message/四fine已证ID。共享core、领域/规则/存储/DTO/codec/composer、Spec/P2/契约/root/A/baseline/original/旧Tasks/reviews/证据只读。不迁Repair/Move/Forging/MagicSeal/Outworld及其它装备、Mailbox/Rental；不改共享接口、事务/执行器算法、schema、业务规则或协议字段；不新增raw出口、Compose、延迟编码或机制/规则注入豁免。
- Required checks: 三条真实decoder→dispatcher→同一bus/Main→实际Preview/ApplyEffects→SQLite→ICommandProjections/原NotiBus→同outbox路径，NPC/Portable双分支、前置Rejected/域内正常Committed、真SQL rollback重试、同序号当前投影不二结算及不同帧序；原全部case/字节保留，一个有价值可编译mutation红绿并恢复。完整Release build0/0＋App/Protocol/Networking三full、其余四程序集actual DFO control、Golden15/Characterization10/capture/strict原八坏副本；8602＋实得N－D的全局延续口径见末节，不冒称八full。
- Dependencies: TASK-12 r2/Result R1/required独立approved及本地检查点`247a63451c19c6f7622aea1b58a5a66d3e38ba8e`（parent `1cf5051a4566eefa093d3082e73f65a6de130b99`），Task11 Main/唯一ledger/显式recorder/sink/B2最终收口仍有效；B2/P2及其匹配gate不变，Spec baseline仍`10a7218a779493494b138bd6b43cc0fc1a84aa6a`。当前三domain已使用必需footprint，无需重做共享基础。
- Result review: required — 三协议拒绝或分支迁错可误扣材料/保护券/金币、免费分解、重复roll/reward或误发ACK；compile/codec不能证明实际Main和重放，Disjoint旧manual驱动还可能掩盖入站错误。独立结果快审核原oracle、精确业务分支/状态与同版本证据后交主控检查点；Task-plan不增独立门，由主控readiness安排executor。

## Detailed Plan

### 1. 起点、目标贡献与边界

开工由主控确认上述精确HEAD、原worktree `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`、`task/refactor-session-20261021`与唯一owned变化；阅读AGENTS、ArchitecturePhilosophy、RefactorPreferences、B2/P2受影响要求、Task12 r2/R1及独立review。无`.codegraph/`则rg优先。Task12最终完整库存为8602＝8582pass＋2准确原Dimensionfail＋18skip：App4846、Protocol1764、Net199、Host203、Http57、Infra976、Persistence477、Pvf80；是本轮数量起点，不是已运行证据。

真实共同闭包为`EquipmentEffectPlanRuleService`→`InventoryEffectPreflight.PreviewAsync`→`ApplyEffects`，三者已在domain持本次EffectExecutionContext.Footprint；Reinforce120～132、Disjoint96～109、Enchant69～74，前两者共享`EquipmentEffectReceiptProjection.FinalSlotsAsync`。这些业务/映射只读复用，不新造公共EquipmentProtocolCoordinator/第二套plan或绕回旧bus。生产四fine构造仅原三handler；测试还有旧类型反射与Disjoint直接handler驱动，见§5。

中文、本地实施/核验、主控检查点授权保持；不push/PR/merge/远端issue/部署/cleanup/版本改动，不运行df_game_r/DNF.exe或起游戏，不创建Task14。凡需共享core/领域/接口变化、扩大raw出口或中心方案调整，先向主控交实际source与最小边界，不在本Task默扩；当前源证据未发现该必需依赖。

精简preflight：只保存精确owned beforeimages、行尾/BOM/混合byte；非owned tracked仅SHA清单，新增/删除路径单列。禁止复制整仓/旧归档/几千份before或bin/obj。原旧Task13不存在，只有本文件为当前Task ID；后续每证据新路径，不覆盖旧probe输出或旧result。

### 2. 精确生产/文档白名单与只读骨干

所有路径相对worktree；新文件只承担列明职责，不能以整个目录扩权。

| 路径 | 可写职责 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/ReinforceItem/ReinforceItemHandler.cs`、`DisjointItem/DisjointItemHandler.cs`、`EnchantByBead/EnchantByBeadHandler.cs`（后二者同`.../Game/`） | 三薄handler仅ICommandBus（可保ILogger），原Input/Dispatch交whole并await；不得剩业务/映射/领域调用/登记 |
| `.../ReinforceItem/ReinforceItemCommand.cs`、`.../DisjointItem/DisjointItemCommand.cs`、`.../EnchantByBead/EnchantByBeadCommand.cs` | 三文件旧fine及四handler删除；旧CLR/FQN/注册/转发全零，不留Obsolete或newCMD→oldhelper→bus |
| 新`.../ReinforceItem/GameReinforceItemCommand.cs`、`.../DisjointItem/GameDisjointItemCommand.cs`、`.../EnchantByBead/GameEnchantByBeadCommand.cs` | 各一whole record与handler，输入原wire DTO，结果分别既有EquipmentReinforcementResult/EquipmentDisjointResult/EquipmentEnchantResult。原slot转换、TrySlots、ResponseFor和纯错误码映射搬入各自切片whole/helper，可各同目录拆一个纯`*ProtocolMapping.cs`，不得改变值/顺序或抽跨切片业务假门面 |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 三old message route改对应whole、删四Tx fine加三Direct/Protocol whole；每whole只声明已有CommittedReplayCurrentStateBatch；原decoder/encoder/notification注册不变 |
| `AGENTS.md` | 仅当前opt-in列表/装备三协议Main/原NotiBus及四fine已删事实，不改其它规则/来源/未完成责任；三domain已显式footprint现状保持，不全篇重写 |

**只读**：三切片`*Messages.cs`/decoder/encoder/三个`Equipment*Completed.cs`；Application三domain、EquipmentEffectPlanRuleService/FinalSlots/Preview/ApplyEffects及所有effect handlers、Abstractions接口、Persistence/Infrastructure/Data；所有Commands/Dispatching/Transactions/ledger/ExplicitEffectRequest/NonRequest/attempt/outbox/reader/sink/EventService/NotificationBus/RecordedOutbound。本Task没有共享core改动授权；生产依赖只有现有领域服务、槽位换算具名能力、ICommandWorkUnits、IGameCommandOutbox、ICommandProjections及原日志，newCMD不能注入bus/sender/safety/ledger/locator/工厂/规则内容。不会使用Task12 NonRequest模式：三输入均Effect。

### 3. 统一执行形状与应保的状态区别

各whole为`IProtocolCommand<既有领域结果>`，保原强类型Input及Game frame。薄handler只传输入，真实bus保持唯一入队、精确CLR、登录权威/旧连接拒绝与Effect admission；不建立legacy ambient/显式测试source。当前注册图未见这三logical被其它probe占用，测试不得再给真实logical注册第二whole；需要机制probe时仅用独立test logical，保Task12 Matrix修正及闭合反例。

CMD先取得本次Request，照原协议**先后**做原前置，初始ProtocolEffectRejected只能走原无session拒绝，不可因后查出现session取得Main。只有原来将调用fine的分支才要求ProtocolEffectSelected且当前session等于其captured recipient，调用一次Write(MainSettlement,[selected.Recipient])；用`unit.Footprint`直接调用原domain，所有源/owner/current/事务/Freeze/Settled由现core守住。一个actor是原声明范围，不因账号物资或可见投影新增其它selected参与者。

前置拒绝原本无fine/Tx/Main，故entry仅Pending→Rejected，projection仍无；不得为了“统一返回”把每个早拒绝包Main。已经进入domain的正常Refused结果原fine正常返回并Freeze/Commit最低Inventory投影，仍应Committed；不把正常Refused一概RequireRollback/Rejected。真实SQL/取消/异常保持原退出并由runner确认rollback或typed/default围栏，明确ConfirmedRollback才允许同fingerprint重试，不catch吞保护、不clear sticky。本簇没有新增具名异常回收策略或提前业务写，沿原直接领域结果即可。

成功unit完成后仅`ICommandProjections.ProjectAsync`原TheseCharacters(actor)和原Completed通知；失败无receipt按各协议原typed ACK直接outbox登记。首轮通知仍由NotiBus隔离普通Compose/encoder错误为Failed并继续，whole不因Failed再抛、不增加raw许可；取消/publish级独立scope/unknown故障保原默认保护。直接拒绝ACK不因此获吞错或raw特权。全部whole只existing CommittedReplay许可，已有Use两个B2出口及Buy混合族/NonRequest不改。

已Committed同序号由框架ordinary Write reader→事务外sink current投影处理，不再进入whole业务、不重复roll/材料/金币/tool/bead/reward、不重ACK。原raw identity/128窗口/sequence wrap/ConfirmedRollback原entry.Ordinal不更新照旧；只用本次显式recorder，禁止context.LegacyFootprint/Lifecycle/缺帧fallback。

### 4. 三协议的精确编排/帧序

**Reinforce**：原handler28～49是session→Method(仅0/1，错误0x17)→TrySlots(错误0x04)→online，再fine。TrySlots先TargetList Main/Equipment、目标非负、材料/券≥－1；mapping异常只在该帮助器原catch转换为false。保这种早拒绝不Main；无session优先0x04，不因无效Method改0x17。有效分支直接`IEquipmentReinforcementDomainService.ReinforceAsync(session,target,expectedItem,material,ticket,unit.Footprint,ct,(EquipmentUpgradeKind)Method)`，不把amplification改普通强化，不扩大规则/roll来源。ResponseFor仍从原receipt转换目标/销毁reward BusinessSlot→GameSlot，保保护券/自动保护、materialRemaining、reward顺序/数量/上限/Gold与日志。通知composer原样：ACK→按原space/index clear/occupied slots（SharedMaterial空值过滤保持）→gold，不能抽统一装备响应排序。

**Disjoint**：原handler27～35在session查询前就映射target/tool，**没有mapping catch**；不可套Reinforce/Enchant的catch或先无session返回掩盖原映射异常。随后session/online拒NoSession；tool index＜－1、无tool且provider≤0、tool存在且provider≠0早拒InvalidRequest，无Main。tool存在且provider0进入Portable；无tool且provider正值进入NPC，provider只选协议分支、不传domain或当NPC规则ID。原一个逻辑协议只有一个whole，两个分支直接调同IEquipmentDisjointDomainService对应能力且均传unit.Footprint。tool用尽不能使后次请求变免费NPC，batch UI仍是每条消息独立一次Tx/Main。通知composer原样：耗尽tool先clear→ACK→剩余target/reward slots；不能将tool clear移至ACK后或重复发送。

**EnchantByBead**：原handler27～59先无session0x13；bead不是Main或slot＜0为0x11；target不是Main/Equipment、负slot或无online为0x13；mapping catch为0x13；这些都不Main。有效分支直接原IEquipmentEnchantDomainService.EnchantAsync(...unit.Footprint)，正常domain材料/内容拒绝继续原错误码（MaterialUnavailable0x11、UnsupportedContent0x17、其它0x13）及正常主提交语义。成功response的TargetList/Slot仍来自原wire，通知原样重新读当前bead和target→各自update/clear→ACK；receipt不是以后当前count真源。FIFO barrier、后次enchant后的current replay count与Actor过滤不变。

所有codec字段/容许长度/opaque name/枚举边界原样，不顺手收紧Reinforce decoder trailing bytes或改reported name含义。三domain原Preview不记/不结算，Apply显式记录后FinalSlots/Enchant receipt仍在同Main完成；普通facts getter及合法commit callback不另加cap活性检查。不虚构Reinforce/Disjoint已有Preview前持久写或把本迁移变领域事务重构。

### 5. 测试caller白名单与驱动迁移

以下相对`Server/tests`。只改确有旧类型/驱动/新增保护的文件；所有原case/InlineData/SQL/bytes/序号/期限断言保持，纯driver改动单列，不靠删断言求绿。

| 路径 | 允许动作/必须保留 |
|---|---|
| `Dfo.Server.Application.Tests/Inventory/EquipmentReinforcementHandlerTests.cs` | 已用EffectRequestTransport真实raw/decoder/dispatcher，通常原driver可留；补新whole/state/noMain等最小保护。保122 commit-beforeACK、142同seq、161changed raw、178选角隔离、227/358/450真SQL rollback/retry、保护/自动保护/amplification、404销毁reward与account virtual帧/重放原oracle |
| `.../Inventory/EquipmentEnchantHandlerTests.cs` | 已真实wire驱动保原；保16 bead→target→ACK、34同seq/current无ACK、80首通知encoder失败正常返回后replay不二扣、101真实FIFO barrier不调deadline、183later count、213SQL retry，及window127/128相关整个帧计数/wrap原case，不替换弱metadata |
| `.../Inventory/EquipmentDisjointTests.cs` | 核全部方法与529～542 helper，移除manual legacy授权/直接handler驱动，改真实外部raw/decoder/dispatcher；保NPC/Portable、tool耗尽首clear实际frame、provider组合拒绝、avatar/quickslot行为、账号虚拟物资、满包邮寄、244/259/311/359原真实SQL全资产回滚、每消息独立Tx和UID/数量 |
| `.../Inventory/EffectRequestTransport.cs` | 原已编码这三shape，保原Body/metadata/Observe和其他协议行为；仅如Disjoint fresh driver必要，加基于同物理连接现有counter状态的具名fresh sequence分配，Send只观测一次。不得改重放case明确传入的seq/raw/body或其它调用者 |
| 新`.../Inventory/EquipmentProtocolCommandFlowTests.cs`（可选；同职责case也可放上述三file） | 仅本簇同provider三whole、前置Rejected与域内Committed、missing selected/失效/footprint实际记录及replay、最小新反例。复用ShopFixture/原内容，不另建List发送平台/第二provider/生产testsetupCMD；原private test内容若需共享只在已owned文件内最小调整可见性 |
| `.../Inventory/InventorySlotBoundaryArchitectureTests.cs` | 22～24/32～33/70～71旧Npc/Portable typeof编译闭包：改为whole输入保原wire/Input、实际映射/domain方法仍BusinessSlot的两层机器断言及provider仅在Protocol的边界。不能要求wholewire DTO无GameSlot，也不能只删旧行失去保护；domain/receipt/其余item判据保留 |
| `.../Sessions/EffectSettlementRegistrationTests.cs`、`.../World/CommonExperienceEffectTests.cs` | 精确移除四已删除fine marker名单，同时另证三whole Protocol/单shape/replay-only及真实Main，不把whole加回旧marker清单，不动其余旧pipeline保护 |
| `Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` | 生产全图三logical→三whole精确binding/Direct/Protocol、四fine注册0、each仅replay许可；保原Use/Buy finitefamily、构造/架构/未知shape及所有原case |

其余必须跑但只读：`Game/ReinforceItemWireTests.cs`、`Game/DisjointItem/DisjointItemCodecTests.cs`、`Game/EnchantByBeadWireTests.cs`、App三domain/规则/EquipmentEffectExecutionTests及设备材料SQL断言，所有Task11显式core/B2两raw、Task12 finitefamily/NonRequest/Matrix隔离、Net Golden/Characterization。实际没有其它生产/测试new四fine构造；如果编译新揭示caller，先交主控精确位置，不能泛授整个Inventory或共享fixture/Core。

Disjoint最大driver风险：旧多数case外层还有`TestGameplay.RunAsync`，其helper再manual Effect＋直接Handler。**只替helper为Send会在executor内触发newbus自等**。逐case拆成setup job→外部EffectRequestTransport.Send→必要只读/观察job；fixture自身Seed/Select/Shared准备等按真实写边界保留，不正则整块删braces。每次原意为新请求的helper调用分配fresh sequence，已有明确重复seq的真实wire case照旧，避免Empty metadata被新ledger拒绝或两次购买意外变replay。byte/SQL业务断言不能因更换driver消失，不使用Task11 legacy测试Effect或TestGameplay自动信任绕真实admission。

### 6. 顺序、有价值RED/GREEN与反例

先before/capture与逐case inventory，落能在旧路线或去保护时区分的新case，再搬三whole/注册/删fine/改driver，定向通过后冻结正式。机械编译迁移不计行为RED；正式前核变异已byte/SHA恢复。

- 每协议至少一真实rawdecoder→dispatcher→bus/Main→实际domain/SQLite→Project/NotiBus→同RecordedOutbound/outboxes路径；不是跨provider拿一份登记记录冒充。精确对照前置拒绝（无claim、entry Rejected/无projection、无roll/扣款）和进入domain后的普通Refused（正常Main Committed、最低Inventory、无实际效果），不能只Assert同错误码。补无selected不Main，后选中不能升级本次；有效后fresh下一请求能正常结算。
- 原同seq/current replay与SQL retry继续实跑：Main一次、同entry/原Ordinal不延长、材料/tool/bead/gold/reward只结算一次、不会重roll/重ACK；首轮NotiBus encoder Failed正常隔离，后次current状态可重放。发生取消/typed/unknown/直接ACK故障按现默认保护，不新增raw或吞错；三whole只能共享replay出口。
- 三种输出次序单独断言；Portable耗尽先clear且只一次、错误provider/tool组合不得业务、Reinforce自动保护/虚拟账号材料及销毁reward账守原SQL和byte值、Enchant当前bead/target及真实FIFO/wrap守原barrier。原规则/content能力都不变，不能以新whole返回结果代替原frame断言。

选至少一个可编译行为mutation，优先临时删wholeDisjoint中“tool存在且provider非0”前置拒绝：真实合法9B请求原应不进入Main、不消耗tool/target、不授reward；缺守卫则会按portable实际写，原case及entry/SQL oracle变红（不能只Assert某异常）。或对本簇原明确frame排序守护做一个同职责可编译mutant；不修改只读composer来凑范围，若需变异可在ownedwhole调用点错误选择分支/响应触发真oracle。不得删除sharedcore guard或改纯规则，避免扩大本Task。变异保存原bytes/SHA、try/finally恢复、同目标绿后再final freeze；fixture未注册/metadata非法/nested/编译/超时不当RED，记录诊断并修driver。

### 7. 正式核验与数量口径

没有共享接口/core改动，影响为Protocol三用例及App test driver，故完整`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`要求0warning/0error；随后App/Protocol/Networking各一个独立**无filter full**：`dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`。按原本机FSEvents可用runner/caffeinate/必要require_escalated执行，原xUnit类并行/5m/期限/Skip保持；每step保存cwd/HEAD/command/UTC/exit/elapsed/资源和完整console，session须自然结束，不TERM父runner/活跃树或补造exit。修产品后新freeze/受影响正式轮，不混拼不同源码结果。若确需sharedcore改动，先停报主控修scope及full范围，不能用本三full授权静默做core。

同一新DFO目录收三full＋Host/HttpApi/Infrastructure/Persistence各一次**实际control-only**，filter精确`FullyQualifiedName=Dfo.Server.Testing.GameplayAssertionRecorderControlTests.An_off_executor_violation_in_this_assembly_reaches_the_recorder`；七程序集各唯一control齐，原74 expected身份/数不放宽（App62、Net7、Protocol1及其余四各1），外/exe/orphan0核，exe0仅本轮观察。若新故意反例真的产生额外assertion，先定向核精确class/count与理由另列，不替换原expected或吞未知。Pvf不运行control；本轮不能称八full或其它项目业务全量已测。Golden15逐byte对原f01dc226及Task12接受检查点保持；Net Characterization10逐名pass/0skip，三codec/原payload bytes/顺序保持。

从Task12完整inventory8602逐文件/逐case(display,status,multiplicity)核真实N－D，原case移除0为目标；必要rename逐个旧→新映射并说明driver目的，不能以总数量或Assert总多重集相近替代。三full起点App4846＋Protocol1764＋Net199＝6809，其余1793沿Task12已接受库存延续，不冒称本轮重跑。最终全局账8602＋N－D是延续数；报告各实际full/controls实得数分开。允许的两条原DimensionDungeonTests准确身份仍是Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource及All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，Dungeon7100/maze0/cell(1,0)/entrance Left原InvalidDataException；本轮App full原16skip应按身份保持，Infra原2skip仅延续上轮库存，不能说control运行重验18skip。Host/Net/SQLite新失败不入基线、不改deadline/并行盲重试。

### 8. Capture、精确闭账与交付

复用只读`verification/probe-tools/migration-ledger.py`/export和原DFO工具，task-13自己的新脚本/输出限`verification/logs/task-13-*`、`verification/evidence/task-13-inspection/**`，before/after capture为ConfigureServices-only，不BuildProvider/启动业务；finitefamily sidecar可按Task12已接受方法纯构造两注册表，不增生产服务。禁止--output覆盖旧证据/工具，收最少精确owned beforeimages＋未授权SHA/原case映射/metadata，不复制旧归档或全仓。

预期173logical不变；204cmd删四Tx加三Directwhole→203：15Protocol＋1Internal＋187Legacy，159Tx/31Direct/9RO/4Workflow，message15Protocol＋158Legacy（telemetry14仍包含在相同总账、不另加）；均以实际runtime逐row核，不硬填。仅这三消息route及四fine→三whole变化；其它共同bindings/Buy双shape/Use双许可/新三whole仅replay及未知shape拒绝保持。

ledger两文件只拟闭：`message:65616` Reinforce、`message:65562` Disjoint、`message:65808` Enchant，及`command:Dfo.Server.Protocol.Game.ReinforceItem.ReinforceItemCommand`、`command:Dfo.Server.Protocol.Game.DisjointItem.NpcDisjointItemCommand`、`command:Dfo.Server.Protocol.Game.DisjointItem.PortableDisjointItemCommand`、`command:Dfo.Server.Protocol.Game.EnchantByBead.EnchantByBeadCommand`。填实际原caller/Tx/participant/保护去向和whole/测试Artifact，state用真实state键；original/baseline/其它行不改，不顺闭sources/protection/transition。预期pending147msg→144、191fine→187、72source不变，5protection/8transition仍pending；闭账需实际证据，不用预期数替代。

先`preclose --current-task <本Task精确路径>`核拟闭Artifact；executor追加Result后strict validate/selftest原八坏副本全部拒绝，不能提前填不存在Result骗strict。所有owned源/driver/文档/ledger及证据最终freeze/manifest SHA匹配、无未授权写，R1 prefix保存供独立review只追加，主控核精确物理路径和SHA后本地检查点。规划阶段只有本文件，不写Result/review/status。

Result说明三协议真正已闭、四fine已删、前置Rejected与normalMain区别、帧序/SQL/current重放和NotiBusFailed隔离、唯一mutation真实红绿、驱动/断言/数量变化、三full四controls及未重跑项目的精确范围，保所有实际诊断/无效轮/未解根因。T012最终新八full及性能不免；其余144消息/187命令/72来源和legacy清除、Spec/根封存、单CMD≤2秒统一接受线最终测量、Astra及用户分支收尾门留主控，不提前宣告完成。


## Result

- Revision: 1（Task r1；HEAD 247a63451c19c6f7622aea1b58a5a66d3e38ba8e；最终 source-freeze-01；待主控 required 独立结果快审，未提交）
- Outcome: done
- Changed: Reinforce、Disjoint、EnchantByBead 各已成为一条完整的 GameReinforceItemCommand／GameDisjointItemCommand／GameEnchantByBeadCommand；三个薄 handler 仅注入同 ICommandBus、交原精确 Input/Dispatch 并等待。原 ReinforceItemCommand、NpcDisjointItemCommand、PortableDisjointItemCommand、EnchantByBeadCommand 及四 handler/注册实际删除，不留兼容转发。原领域/规则/DTO/codec/composer/Core/接口只读，new whole 仅领域能力、原具名 slot 换算、units/outbox/projections/logger，无 bus/sender/locator、legacy cap 或新 raw。AGENTS 仅同步该已启用范围，见 [workunit-map](../verification/evidence/task-13-inspection/workunit-map.md)。
- Main / refusal: 仅原会调用 fine 的有效分支进入一次 Write(MainSettlement,[initial selected actor])，unit.Footprint 原样交既有 domain 的 Demand→EquipmentEffectPlan→Preview→ApplyEffects，原 FinalSlots/receipt 仍同事务。原前置拒绝不 claim Main，Pending 结束 Rejected、projection null、HasCommittedUnit false；进入 domain 的正常 Refused 则原事务正常返回、Main Committed、最低 Inventory/空TouchedSlots。新增三协议各自实际状态用例与真实 gold/ACK 证明差别。初始无 selected 即使在 sameprovider actual handler 前出现角色2选择，本次仍 ProtocolEffectRejected/不 Main；下一 fresh 正常 Selected/Main。账号素材只原 actor/account，不扩大 selected participant。没有 RequireRollback 所有拒绝、通用 catch、清 sticky 或重做 shared core。
- Reinforce: 原 session→Method0/1→TrySlots→online 次序及 0x04/0x17 拒绝保持；TrySlots/ResponseFor/纯 ErrorCode 原 bytes 完全搬移，mapping catch 仍只在 TrySlots。有效分支原 ReinforceAsync(...unit.Footprint,...kind)；amplification、保护券/自动保护、virtual account material、销毁reward/上限及 roll 规则不动。通知原 ACK→按 space/index 的 clear/occupied→gold。原 SQL/UID/材料/金币/属性 byte/销毁reward/同序号无二扣与无重 ACK oracle 均在本轮 App/Protocol full 实跑。
- Disjoint: 原 target/tool mapping 先于 session/online 且无 catch；新增无 selected＋坏 slot 的真实解码用例仍原 InventorySlotMappingException、无 ACK，未用提前 NoSession 掩盖。tool＜－1、无 tool/provider≤0、tool 存在/provider≠0 原早拒绝不 Main；合法 Portable 与 NPC 为同 whole 的二选一直接 domain 调用，provider 不传领域、不当规则ID。原耗尽 tool clear→ACK→target/reward 顺序及只 clear 一次保持；真实9B/frame oracle、tool耗尽后不可免费NPC、avatar/quickslot/账号素材/满包邮件/UID及每消息独立 Tx 原断言不变。
- Enchant: 原 session→bead Main/nonnegative(0x11)→target Main/Equipment/nonnegative/online(0x13)→mapping catch(0x13) 顺序保持；正常 domain 拒绝 MaterialUnavailable0x11、UnsupportedContent0x17、其它0x13。原 EnchantAsync 的显式 cap/Preview/Apply/receipt 不改。成功 composer 仍实时读取 bead→target，各 update/clear 后 ACK；原 current count、FIFO5s barrier、counter完整帧/wrap/window和SQL同序号retry原 case 全部保留实跑。
- Projection / failure / replay: 成功只走 ICommandProjections→同 EventService→原 NotificationBus，逐目标 Compose/encoder 普通故障返回 Failed 正常隔离、不转 sticky/typed 或添 initial raw；原 Enchant 故障点与原 frames 保持。无 receipt 的直接 typed ACK 外抛另有三实际 encoder case：Main 已 Committed 后 IOException 保原 cause Ref、最终 typed/default fence，未泛化通知吞错。取消/typed/unknown/独立scope默认保护由只读 core/原全量测试保持。三whole仅声明既有 CommittedReplayCurrentStateBatch，原 Use 两出口和 Buy 双shape/NonRequest 不变；committed replay 由原 ordinary Write reader→事务外 sink current 投影，不回 whole、重 roll/授 reward/扣 tool、bead、金币或重 ACK。新 NPC/Portable SQL触发器两 case 实证全资产回滚/ConfirmedRollback/nullprojection，同 fingerprint 原 entry/Ordinal retry Committed，之后 replay handler次数不增、tool/reward不再结算。
- Driver / architecture: Disjoint 原 helper 的 TestGameplay＋manual TestEffectFootprints＋direct handler 已替换为外部 EffectRequestTransport 真实 raw/decoder/dispatcher；唯一仍外包 global TestGameplay 的 provider 理论逐块拆除包装，Seed 自身同provider写边界及全部原 SQL/错误码/assert 保留，未给 new CMD inline 特权。fresh helper只读取同物理连接 counter 的 Next，Send 只 Observe 一次，所有明确 seq/raw replay 与其它协议 Body/metadata 不改。旧 fine 的 SlotBoundary typeof 判据在同原 case 中改 whole.Input=wire/原 GameSlot、实际 domain target/tool=BusinessSlot 与 provider只在Protocol的双层机器断言，保其它 domain/receipt/item规则。两个原 legacy marker名单只移除四 fine，三 whole 另证 Protocol/Direct/单精确shape/replay-only；原其它分类保护不动，没有 probe 占生产 logical。
- TDD / mutation: red01 三真实 decoder/dispatcher/domain 拒绝请求编译运行后，原 fine 的 request 为 null，新 explicit 尚未启用；只作为初始路线缺失 RED，不据此称唯一 Main/资产已证明。唯一 meaningful mutation 仅删 owned GameDisjointItemCommand 中 tool/provider 不相容前置：原合法9B但不相容请求错误进入 Portable，真实 durability45 变 null，test.RED（Expected45/Actual null），同 test 还守 tool1/reward0/Rejected/projection null，并有合法下一 fresh 真提交正例。try/finally 原 bytes/SHA 恢复，green 同目标1/1，再最终冻结。见 [mutation-result](../verification/evidence/task-13-inspection/mutation-result.json)；没有改只读 composer/core/规则或把编译/fixture失败当 RED。
- Formal01 / actual scope: 同一18路径 freeze01 后完整 Release --no-incremental build 0warning/0error；App/Protocol/Net 三个独立无filter full 原 class 并行/5m/skip/期限且自然结束：App4862＝4844pass＋2准确原Dimensionfail＋16原skip，Protocol1765pass，Networking199pass，合计6826＝6808pass＋2fail＋16skip。Host/HttpApi/Infrastructure/Persistence 各实际运行精确 control-only，均1/1，故本轮实际执行6830case；不是八full。两 fail 精确为原 Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource 与 All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，均原 Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException，未修该根因或新增基线。其余1793业务case只延续 Task12 已接受库存，Infra原2skip未由 control 重验。
- Inventory / assertions / bytes: 对 Task12 final01 三项目逐 case(display/status/multiplicity) 比较，原6809全部保持，无 rename/合并/删除；App新增16本簇实际状态/SQL/cause case、Protocol新增1注册闭合，共 N17/D0。全局延续口径8602＋17＝8619＝8599pass＋2fail＋18skip，只是延续账，非八full实测。DFO同一新 formal-01目录收三full及四actualcontrol，原74 expected精确类/数与七程序集各唯一control全齐，外/exe/orphan0，不增 expected，exe0仅观察。Net Characterization10逐名pass/0skip；Golden15对 f01dc226 与本轮HEAD原 bytes 相等。见 [formal-summary](../verification/evidence/task-13-inspection/formal-summary.json) 与 [test-delta](../verification/evidence/task-13-inspection/test-delta.json)。
- Capture: before01/after01 各 fresh ConfigureServices-only，不 BuildProvider/不起业务；173logical不变，204cmd删4Tx加3Direct→203，15Protocol＋1Internal＋187Legacy，159Tx/31Direct/9RO/4Workflow，message15Protocol＋158Legacy/14telemetry。仅 message65562/65616/65808 route与四fine→三whole变化，其余所有绑定保持。fresh family exporter 纯构造 command/message两注册表实际核闭合，三whole各单精确CLR/只replay，Buy两shape与Use原两permission保持，见 [capture-checks](../verification/evidence/task-13-inspection/capture-checks.json)。
- Actual diagnostics: focused01/02分别为薄handler缺Data.Networking using、缺IProtocolCommandMessageHandler marker的编译错误，非行为RED。focused03完整90＝76pass＋14新case fail，测试trace误挂仅legacy的pipeline，实际 explicit直接invoke handler；按已接受同provider actualhandler wrapper修正，不改core/旧业务断言。focused04完整90＝87pass＋3新late-selection fixture fail，ReleaseExact未清旧角色容器而Select(1)重复Register，改真实选尚未注册的角色2，不改共享fixture或放宽期望。focused05 90/90，补两SQL entry/Ordinal保护后prefreeze01 92/92；Protocol定向34/34。所有原meta/log/diagnostics完整保留，正式只有当前freeze01单轮、不拼不同产品轮，未TERM活跃runner/补造exit/覆盖旧输出。完整command/cwd/HEAD/UTC/exit/elapsed/resources与console见meta-inventory。
- Scope / ledger / remaining: 18精确owned source（含3旧fine文件删除）、16必要 beforeimages；其余tracked仅SHA核且未改，无全仓归档复制，BOM/CRLF/混合 byte/whitespace保持。ledger仅闭三message:65616/65562/65808与四原fine精确FQN ID，填实际caller/Tx/participant/保护去向及源/测试证据；original/baseline/其它row不动。preclose已通过，Result后strict validate/原八坏副本、自检/manifest/prefix在task-13-inspection；停止全部共享写后交主控fresh required独立快审，再本地checkpoint，执行者未自批/stage/commit。其余144消息/187旧命令/72来源、5保护/8过渡仍pending；legacyReplay/footprint/Lifecycle最终删除、T012最终新八full/单CMD≤2秒统一接受线/Astra/Spec/根封存及用户分支收尾由后继处理。未push/PR/merge/远端issue/deploy/cleanup/改版本/起游戏/创建Task14。


## Independent Quick Review — TASK-13 R1 / Round 1

```yaml
kind: rks-review
verdict: approved
```

审查对象为 Task13 r1／Result R1、HEAD `247a63451c19c6f7622aea1b58a5a66d3e38ba8e` 上的 owned 变化和 formal01。Reviewer `b_task13_review` 独立于 planner／executor，应用 `reviewing-work` 的 Task Unit、Code、Deliverable 准则；已读本Task、B2/P2受影响要求与匹配approved reviews、AGENTS及两架构偏好、已接受Task12结果／review，并以HEAD原三handler／四fine、领域与composer、强测试和当前实际diff独立对照。本批准仅为局部required结果快审，不代表Spec、最终性能或分支收尾完成。

身份核验：`source-freeze-01.json` SHA256 `76719aca16309a6eb7379877b100ceba1df5d71ba687ef0f620ef88345ea025c` 的18物理源路径（含3删除null）全等当前；`artifact-manifest.json` SHA256 `ad8203d8cd396064aab32621b3518bc1c412634b2bb12ab5843922b1a5130d39` 的132项逐SHA相符，连同Task／manifest恰为134个no-renames变化路径，无额外或缺漏。16份beforeimages逐byte等HEAD，12386个非owned、非ledger原tracked路径全原SHA。审查前Task与snapshot精确33967bytes，SHA256 `b6a72d5c8772e73bf0aef9347cb717e563acb635bfb9fc55c4c18dc99264b135`；本轮仅在该prefix之后追加，stage为空。

决定性检查：

- 三whole分别吸收原handler／fine用例，四旧fine CLR／handler／注册实际删除，三个薄handler仅bus。全部生产及测试diff／新文件已读，共享core／领域／规则／接口／codec／composer不改。Main只在原领域调用分支，显式传本次unit.Footprint及原actor；没有legacy能力、nested bus、sender、locator或新raw许可。原具名slot换算和Reinforce TrySlots／ResponseFor／错误码保持。
- 逐支核对原先后：Reinforce先session再Method／slots／online；Disjoint先target／tool映射且不catch，再session／online及provider组合；Enchant按session、bead、target／online、mapping catch原次序。前置noClaim最终Rejected／nullprojection与domain正常Refused的MainCommitted／最低Inventory有三协议各自真实状态和资产断言；初始Rejected晚出现选择仍不能升权，下一fresh正常。Disjoint无selected坏slot仍实际mapping异常、无ACK。
- 三种投影顺序仍由原只读composer表达：Reinforce ACK→slots→gold，Disjoint耗尽tool先clear→ACK→其余slots，Enchant当前bead→target→ACK。成功经原ICommandProjections／EventService／NotiBus逐目标普通失败隔离；直接拒绝ACK的实际encoder错误另保原cause与默认typed/fence，三whole只共享replay许可。原强化／附魔SQL同序号重试、当前投影／FIFO／完整counter和wrap、材料／保护／reward及帧字节case未改；新增NPC／Portable两SQL触发路径实际ConfirmedRollback、same entry／原Ordinal retry、后续replay不再业务／扣tool／授reward。
- Disjoint helper已从manual Effect＋直接handler改真实外部raw／decoder／dispatcher；provider理论的外层Gameplay包装精确拆除，原Seed边界及所有原资产／错误码断言保留。FreshSequence仅读同物理counter的Next，唯一Send照旧Observe一次，未改明确seq／raw重放或其它Body。slot架构判据保whole.Input的wire形状及实际domain的BusinessSlot／provider隔离；旧marker名单只移除四fine，新增whole注册另证，不删其它保护。新trace仅同provider实际handler包装，不用旧pipeline空观察器或第二provider。
- 唯一meaningful mutation实际删owned Disjoint whole的tool／provider拒绝，原日志在真实SQL目标断言显示Expected45／Actual null；没有用编译／非法metadata／自等或文字异常冒充。原bytes／SHA及mutation-before逐byte恢复等当前freeze，同目标1/1 GREEN后才冻结正式轮。初始路线RED、两编译错误、trace误挂legacy pipeline、重复旧角色容器的fixture诊断均保留并明确排除验收。
- 独立读取正式8组meta与原console：完整Release --no-incremental 0warning／0error；App／Protocol／Networking三无filter full均在同freeze后，保原class并行、5m、Skip且自然结束。以Task12正式01与本轮原结果行重建带display／status／multiplicity的Counter，原6809 case/status移除0，无rename；App N16、Protocol N1、Net N0，共N17／D0。三full实得6826＝6808pass＋准确原2个Dimensionfail＋原16skip；两失败消息仍为Dungeon '7100' maze '0' cell(1,0) entrance 'Left'的原InvalidDataException。另四程序集精确control-only各1pass，本轮实际6830case；其余1793仅延续Task12，Infra2skip没有重验。8619＝8599／2／18是全局延续账，未冒称本轮八full。
- 原violations.py以stdout独立复验同一formal目录，expected参数逐项与Task12相同：74精确类／数、七程序集各唯一control，outside／exe／orphan0，exe0仅观察。Characterization10逐名pass／0skip，Golden15逐byte等f01dc226与本轮HEAD。实际capture为173logical／203cmd、15Protocol＋1Internal＋187Legacy、159Tx／31Direct／9RO／4Workflow；只有三个message route及四fine→三whole变化，common commands逐row不变。family sidecar实际仅三精确shape／replay权限，Buy双shape及Use双出口保持，纯构造注册表而未起业务。
- 逐row对比HEAD ledger，严格只有三message与四fine七个精确ID变化，原original／baseline与其它行保持；实际caller／Tx／participant／保护及Artifact对应已核。独立只读重跑strict validate有效、pending Result空、原八坏副本全拒，未覆盖输出证据。剩144消息／187旧命令／72来源、5保护／8过渡仍pending。

没有本局部范围未解决的blocking finding。两条内容基线未修；legacyReplay／footprint／Lifecycle、其它迁移、T012最终新八full／单CMD≤2秒测量、Astra及Spec／根完成仍留后继。Reviewer未运行native、修改产品／Result／旧证据或git，唯一写入为本段review；主控负责消费匹配证据并作本地checkpoint。


## Controller Consumption — TASK-13 R1

主控已完整消费r1/R1及独立Round1 approved，核expected HEAD/分支、空stage、132项SHA／134物理路径、33967字节R1 prefix及18冻源相符；另从原console独立Counter核App N16、Protocol N1、Net N0，所有原case/status/multiplicity保留、D0。ledger逐row严格仅三message／四fine变化，各original/baseline及其它行未改。三full和四actualcontrol是本轮实测，其余1793只是Task12延续，未将Infra2skip或八full冒称本轮验收。原map/早拒绝／normalMain与三帧序、真SQL及provider变异支撑本单元检查点；144/187/72和5/8仍pending，Spec／性能／Astra及用户收尾门留后继。不推送PR或封存B。
