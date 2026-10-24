# TASK-16 — Quality／RandomOption 两个完整装备品质协议 CMD

## Task Unit Contract

- Revision: 1（本契约与详细计划的同一修订）
- Outcome and references: 交付[Plan P3](../plan.md) T-004 下一有用单元：GameResetItemQuality（含wax reseal／quality reroll）和GameResetRandomOption（magic-seal grade）各一个whole CMD，吸收原handler/fine编排，删除两个fine。消费Task15已接受WriteResult与既有Main／最低Inventory／current replay／ICommandProjections，保原规则、事务、拒绝、当前性及帧字节。覆盖[Spec B2](../spec.md) RQ-001/003～009/012/014/015、AC-001～005/007～009及F-34/F-88/F-93/F-112；不新增VC或raw出口，不宣称T-004全簇完成。
- Owned boundary: §2精确13个生产/文档/测试路径、本Task与task-16新证据；migration-ledger.json/md仅§7四个精确ID。共享core、Task15机制、领域/规则/接口/Persistence、DTO/decoder/encoder/同slice通知composer、EffectRequestTransport及其它helpers、Spec/P3/契约/root/A/baseline/original/旧Task/reviews/证据只读。不扩Socket/Avatar/Warehouse/CollectionBox/普通Inventory，不造footprint链、NonRequest、额外日志管线或跨切片协调器。
- Required checks: 两logical同provider真实decoder→dispatcher→whole/Main→实际domain→SQLite→原projections/NotiBus/同outbox；earlyRejected与domain正常RefusedCommitted、reseal仅null回落、完整ushort、原两种帧序、action SQL/native Commit确认回滚后的原0x13 ACK/同cause/entry原Ordinal重试/current replay、通知普通Failed隔离/直接ACK默认保护。保所有原业务Throws/SQL/UID/bytes与明确seq；一个ownedwhole有价值可编译mutation红绿恢复。完整Release build0/0＋App/Protocol/Networking三full＋其它四actual原DFO controls、74+7/Golden15/Characterization10/capture/strict原八坏副本；8701＋真实N－D为全局延续库存，实测范围分开报告。
- Dependencies: TASK-15 r2/Result R1/fresh required独立approved及controller consumption，本地检查点`d7373b9dd33184ece5f419972e75c45fad48b0d8`（parent `f930f26cc681982daf1f7663867c7ef7e653f877`）。消费其freeze03已验WriteResult/真实来源Guard-KnownStorage-Unknown/带cause回滚与同对象独立失败保因、Main/footprint/唯一ledger/replay，以及Task11～14经该依赖保留的B2两raw/finitefamily/NonRequest。P3/B2及匹配gates不变，Spec baseline仍`10a7218a779493494b138bd6b43cc0fc1a84aa6a`。
- Result review: required — 两条协议共享品质领域但ACK/材料更新与reseal回落不同，误迁会重复消耗或改错option、把guard/已提交误当可重试，编译/codec和总数量不能证明。独立结果快审核真实source、事务/原oracle/同版本数量及四ID闭账，再交主控检查点。Task-plan仅主控readiness，无额外独立审批门。

## Detailed Plan

### 1. 起点与边界

主控先核精确HEAD、原worktree `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`、branch `task/refactor-session-20261021`与唯一写者；读AGENTS、两架构偏好、B2/P3受影响要求、[Task15 R1/独立review/controller consumption](TASK-15.md)。无`.codegraph/`则rg优先。中文、本地实施/核验/主控检查点授权保持；不push/PR/merge/tag/远端issue/版本/部署/cleanup，不运行df_game_r/DNF.exe或起游戏，不创建Task17。发现必需白名单外caller或sharedcore变化，先报主控精确source/最小依赖，不静默扩scope或改中心方案。

Task15 final03八full库存8701＝8681pass＋2准确原Dimensionfail＋18skip；App4935、Protocol1767、Networking199合6901，本轮另Host203/Http57/Infrastructure976/Persistence484/Pvf80合1800是Task15库存，不再引用旧Task12的1793。本轮三full/四control不能冒称八full或Infra两skip已重验。起点capture173logical/203cmd＝22Protocol＋1Internal＋180Legacy，152Tx/38Direct/9ReadOnly/4Workflow；pending135message/180fine/72source、5protection/8transition。

两domain实际没有recorder/ApplyEffects参数：`EquipmentQualityDomainService.cs:17–30`及`EquipmentResealDomainService.cs:16–20`要求当前事务后直接物品生命周期/装备写。保这些接口/业务原样，whole Main的最低Inventory足够，不为结构统一调用unit.Footprint、伪造记录/效果链或NonRequest。原实际Transactional段对应一个新Main，不能把整个CMD/投影包成默认事务。

### 2. 精确白名单（生产/文档6＋测试7＝13个source路径）

路径相对worktree，只有列明职责；不授整目录。新增同职责类型收在指定文件，不另建公共helper。

| 路径 | 可写职责 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/ResetItemQuality/ResetItemQualitySlice.cs` | 精确删除旧ResetItemQualityCommand及handler；消息handler薄化为同bus/原Input/Dispatch/Protocol marker。保record request/response、decoder/encoder及EquipmentQualityCompleted/composer原代码/字节，仅必要using变化 |
| `Server/src/Dfo.Server.Protocol/Game/ResetRandomOption/ResetRandomOptionSlice.cs` | 同上删除ResetRandomOptionCommand及handler，薄入口；保DTO、完整ushort decoder、encoder及MagicSealGradeCompleted/composer |
| 新`Server/src/Dfo.Server.Protocol/Game/ResetItemQuality/GameResetItemQualityCommand.cs` | 唯一whole record/handler，原强类型Input＋Dispatch，EquipmentQualityResult；原前置/双Main地址换算/业务窄catch/错误码与日志/投影搬入 |
| 新`Server/src/Dfo.Server.Protocol/Game/ResetRandomOption/GameResetRandomOptionCommand.cs` | 唯一whole record/handler，同上；OptionIndex全ushort传领域，不新增校验/截断 |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 删除两Transactional fine；两logical切两Direct/Protocol whole，各唯一精确CLR/仅CommittedReplayCurrentStateBatch。原decoder/encoder/notification接线不改 |
| `AGENTS.md` | 仅当前两协议opt-in、两fine删除与现Main/原失败响应的必要事实，不全篇重写或改未完成责任 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/EquipmentQualityTests.cs` | 原fine helper/保存owner调用迁§5 typed领域driver；原wire与decoder/byte/业务case完整保持，允许fixture/content最小internal可见性供新flow复用及必要同职责补证 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/MagicSealGradeTests.cs` | 原Roll/quickslot/stale fine caller迁typed领域driver；保全部ushort/checksum/grade/SQL/PVF/帧序/明确seq，允许原fixture最小可见性 |
| 新`Server/tests/Dfo.Server.Application.Tests/Inventory/EquipmentQualityDomainTestDriver.cs` | 仅具名Reset与Grade typed方法，同host.Services/显式旧owner/真实DomainTransactionScope和SelectedSessionSafety.Capture(owner,null)，保纯领域结果/Throws；不开放任意Func/通用CMD测试平台 |
| 新`Server/tests/Dfo.Server.Application.Tests/Inventory/EquipmentQualityProtocolCommandFlowTests.cs` | 本两logical真实wire/Main/SQLite/action与native Commit恢复/entry/retry/当前重放/通知与直接ACK故障的同provider证据、实际handler/domain forwarding observer；保阶段0旧route探针的最小源版本 |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/EffectSettlementRegistrationTests.cs` | legacy marker名单仅移两fine，保其余名单/反例；新whole元数据另证 |
| `Server/tests/Dfo.Server.Application.Tests/World/CommonExperienceEffectTests.cs` | 同上仅移两fine，不顺清其它旧命令 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` | 两message→两whole/direct/exactCLR/replay-only/无legacy marker及两fine注册0；保所有原case、Use两出口/Buy family/Matrix隔离及Task15四whole |

两个旧fine生产构造caller只有各自slice handler（Quality:92、Random:86）；测试CLR caller为QualityTests:47–50、MagicSealGradeTests:47–49/129–135和两marker字符串名单。`InventorySlotBoundaryArchitectureTests`没有这两fine，故只读，不机械改判据。`EffectRequestTransport.cs:100–103`已有两shape真实Body/decoder/dispatcher，原wire都经它，无directhandler/Empty metadata驱动待修；decoder-only Empty合法保持。ShopFixture/RecordedOutbound/OpeningDomainTestDriver/OpeningProtocolCommandFlowTests/core tests均只读复用方法，不改第二provider或支持层。

### 3. whole与显式事务执行形状

各record实现`IProtocolCommand<EquipmentQualityResult>`，Message＝原Input，Frame＝原Game metadata；薄handler实现相应`IProtocolCommandMessageHandler`，只构造/await同bus新CMD，禁止newCMD→CMD/legacybus、sender/locator/当前请求DI/ambient。whole可注入原领域/具名slot换算、ICommandWorkUnits/ICommandProjections/IGameCommandOutbox及原日志，不加规则/内容/新豁免。

完全保原前置顺序：FindByConnection session→session为空或Input.Valid=false或online不存在拒0x13→target与material两Main槽映射，catch InventorySlotMappingException拒0x13→原会调用fine的分支才验证本次ProtocolEffectSelected及精确captured Session，开`WriteResultAsync(context,new(具名unit,[selected.Recipient],MainSettlement),action,ct)`。ProtocolEffectRejected即使后来出现selection也不能升级；拒绝ACK用原连接/原类型。不能新增ExpectedItemId>0、OptionIndex<=2或IsInWorld前置门：这些原领域返回拒绝属于已进入fine后的正常MainCommitted，不是earlyRejected。

Quality action中同一事务严格`TryResealAsync(session,target,ExpectedItemId,material,ct) ?? RerollAsync(...)`；非null正常拒绝必须直接保留，不能以Receipt为空为由回落。Random action只`RerollMagicSealGradeAsync(session,target,material,Input.OptionIndex,ct)`；领域先`optionIndex>2`返回InvalidOption再内部cast，原ushort256必须默认0x13且无扣减，不是UnmodifiableOption0x17。正常无receipt/NoSession/InvalidRequest/InvalidOption照旧正常返回并提交，MainCommitted/最低Inventory/可无TouchedSlots；不得统一RequireRollback或造fake footprint。

真实抛错才用Task15已验局部方案：action窄try仅原domain链（及该链调用的原时钟），捕获`!OperationCanceledException && !TransactionOutcomeUncertainException.Contains`，保存原E，`unit.RequireRollback(E)`后返回具名local outcome。RequireRollback本身的guard不得被同catch吞；request/current、Main/Freeze/guard/来源判断、outbox/Project/encoder不在try中。不包整个WriteResult外catch，不clear sticky或以业务自报判断已回滚。

unit完全结束后匹配`Returned<局部Outcome>`或`CommitRolledBack.Cause`。普通业务返回仍原result；捕获E或已证Commit rollback的原cause仅在unit外LogError原级别/同cause并发0x13，原原文日志主题可保、观察category迁新whole。Guard/Unknown/cancel/typed/cleanup/Committed/额外scope/drain默认仍由只读机制外抛保护；E＋独立X包括同对象按Task15已验保因，不在本Task重做恢复API或删检查。Main ConfirmedRollback claim不重置、Freeze后projection可非null，下一admission才清projection、同entry原Ordinal重试。

成功unit关闭后只`ICommandProjections.ProjectAsync`原TheseCharacters/原Completed，保NotiBus逐目标普通Failed隔离；失败ACK在unit外typed outbox，编码故障遵默认保护，不新initial raw许可。两whole只existing CommittedReplayCurrentStateBatch；同seqCommitted不再whole/roll/消耗/ACK，只reader ordinaryWrite后原current投影。Use两B2许可、Buy两CLR/NonRequest、Task15来源/guard/默认Write全部保持。

### 4. 分支、帧与旧oracle保持

Quality拒绝MaterialUnavailable0x11、AlreadySealed0x12、ResealLimitReached0x0D、其它0x13。成功**9B CMD0051 ACK（材料slot/剩余数/Operation wax1或quality2）→target NOTI000E**，材料由ACK更新，不额外发材料NOTI。Random拒绝Locked0xD5、MaterialUnavailable0x15、UnmodifiableOption0x17、ItemUnavailable0x11、其它0x13；成功**target000E→material000E（耗尽清槽-1）→CMD01C8单byte[1]**。保composer精确character/physical connection＋IsInWorld，不修改checksum/selector/原字节或改receipt投影成另一读法。

QualityTests原wax PVF费用/限制/封印计数/UID/重穿戴/同seq、rarity/expiry/物料身份/最高品质seed、仅seed变化其余core逐byte、真实PVF/decoder trailing checksum及并发单耗全保。MagicSealGrade原三个grade各自更新/保seed与其它字段/UID/reload、blocked/unknown/expired/locked/材料身份、quickslot、ushort与checksum、帧序/同seq全保。原Fact/Theory/PvfTheory/Skippable身份/InlineData/display/status/multiplicity不机械删除或合并。

原QualityTests:129/210与MagicSealGrade:104 SQL trigger是业务/action UPDATE故障；QualityTests:154–171及218–235是receipt成功后同真实DomainTransactionScope插deferred FK再Commit Throws，属于领域/事务证据，**不是旧handler失败ACK已实跑**。这些测试保原异常与数据库断言，不因whole将普通失败映ACK而改成“未throw就通过”。

### 5. typed领域测试driver与新wire证据

新typed driver参照已接受OpeningDomainTestDriver的同provider/真实事务形状，但不借其opening规则/反射/footprint：具名Reset(host,owner,target,expectedId,material)及Grade(host,owner,target,material,ushort option)；每调用独立TestGameplay job，在本host.Services中Capture(owner,null)/DemandCurrent前后、Begin真实DomainTransactionScope，直接调原领域（Reset保reseal-null回落），Commit；catch E实际AbortAsync(E)后原throw，finally Dispose。两domain不消费cap，故无需新testLease/recorder/noop。保存旧owner的stale case继续先Demand拒绝，不能因domain只返回NoSession而把原Throws弱化。quickslot仍传原BusinessSlot；不把wire地址算法复制进driver，不生产setupCMD/任意Func/嵌套bus。

并发Roll移此driver仍每调用本host独立FIFO job，保单材料只一receipt及stale/SQL目的；原native两case可直接显式target/material，不为拿字段留旧fine兼容record。wire Send在TestGameplay外用现EffectRequestTransport，fresh独立请求用FreshSequence；原明确seq7/retransmit/raw checksum case不改。所有decoder Empty只解码不dispatch，保持其原用途。

**执行顺序与窄before对照：** 先精确before/capture/原case清单，运行原两协议强定向case；产品迁移前在本新flow用同fixture内容/原真实decoder与旧route补两logical各action SQL及native Commit完整协议探针。此为本协议外catch精度的行为基线，不是重开Task15共享机制Phase0 gate；不能将迁后结果倒称before。旧探针最小源码/raw/meta保存后迁新route，同oracle对照。若旧route实得否定既定catch等价或新结果来源不能覆盖，停报实际source/阶段，不放宽机制或臆造许可。

新native探针在seed后建立fixture-only deferred FK表，实际items UPDATE触发器向child插不存在parent（参考Task15 provider方法，外键指向fixture probe parent）；确认该实际连接ForeignKeys=1、domain已成功返回receipt/目标与材料改动可见/child>0，随后原SqliteTransaction.Commit真19/787并保stack。action探针用原UPDATE RAISE(ABORT)路径并分清19/1811；不能用fakeparticipant、单独provider、普通trigger action或外部手工插错冒充完整wire Commit。probe DDL不改生产schema，失败后drop trigger/探针表由fixture处置，不动真实数据库。

观察只用本新flow的typed forwarding domain/actualhandler wrapper和同provider logger/实际request/transaction/ledger/outbox，所有wrapper调用真实实现；不为真实logical注册probe whole、不第二BuildProvider、不靠LegacyLoggingBehavior截获新bus。旧/新输出分别保存raw body/hash/seq/ordinal、原0x13 ACK与Error原cause引用、true transaction CompletionConfirmedRollback、目标core/UID/材料/gold及无新增副作用、entry/state/projection、participant fence/source close；同fixture drop故障后同fingerprint同entry原Ordinal retry只一次消耗/改装，再次重放仅current投影无ACK。before old pipeline和handler可能双Error，迁后只保新whole handled Error，同cause/级别须证，不恢复重复管线。

另补最小新路径区分：两logical各真实成功；早Valid/映射/noactor不Main→Rejected；进入原domain正常缺材料/InvalidOption/NoSession→MainCommitted（NoSession不前移）；Quality有效reseal与quality分支分别保Operation/资产，reseal非null拒绝不回落；初始Rejected晚出现selection不能升权、下一fresh正常。两domain都无record仍Main minimum Inventory/当前重放可用，不以空TouchedSlots误判未结算。

至少一真实首通知encoder故障经现RecordedOutbound/原NotiBus变Failed正常结束Committed/nofence，后续current不二扣；两类直接拒绝ACK encoder错误保持原cause并默认处理，不把同IOException当raw许可。正常domain拒绝已Committed时发生直接ACK故障应原默认typed/fence，earlyRejected/ConfirmedRollback后的故障按真实precommit默认source-close，不强说所有都fence。取消/typed/Unknown/KindGuard/rollback或cleanup独立错误不转0x13正常成功：复用Task15机制强case三full实跑，并在本两协议选至少一实际business E＋cleanup X不发失败ACK正常返回的连接性case，若需要新增通用helper/core先报主控。

### 6. 有价值mutation与正式核验

优先仅在owned GameResetRandomOptionCommand调用原domain处把完整OptionIndex临时cast byte：真实MagicSealGrade fixture的option0=15且可调，wire ushort256原应InvalidOption/0x13、材料2/target不变；错误截断0会真正消费一份并改option0，原indexHighByte case与新资产/Main断言应红。先实测fixture reachability/编译成功，不以缺服务/非法metadata/timeout/无效fixture当RED。变异不动只读decoder/domain/composer，try/finally恢复原bytes/SHA，同目标GREEN后freeze。若此候选实际不可达，向主控给诊断再选ownedwhole reseal-nonnull错误回落的有用坏例，不造假成功。

仅本地协议/测试变化，复用Task14 §7已接受正式编排，Task16每轮新prefix/目录；完整`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`须0warning/0error；App/Protocol/Networking三项目各独立无filter full：`dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`，原class并行/5m/期限/Skip不变。确需sharedcore改动先报主控修scope和full范围，不能借三full默改核心。

同一新DFO目录三full＋Host/HttpApi/Infrastructure/Persistence四actual control-only，filter精确`FullyQualifiedName=Dfo.Server.Testing.GameplayAssertionRecorderControlTests.An_off_executor_violation_in_this_assembly_reaches_the_recorder`。原74 expected精确类/数（App62、Net7、Protocol1、其它四各1）与七程序集各唯一原control保持，outside/exe/orphan0；exe0只观察，不夸全路径穷尽。新故意违规逐类/count实证另列不放宽旧expected，Pvf无control。Golden15逐byte对f01dc226及本起点，Characterization10逐名pass/0skip；不把七control说成七业务full。

以Task15 final03逐case(display/status/multiplicity)对照8701起点，三full原6901保持及真实N－D、其余1800库存来源/四control实得分别报告；原移除0为目标，必要rename逐项旧→新/driver目的说明，不能以总数或Assert多重集代替。App原16skip与两原Dimension失败身份保持；Infra原2skip本轮仅延续。准确原失败只有`Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`、`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，仍Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException，不新增baseline。新失败按身份/真实根因诊断，保失败日志，不放宽deadline/并行/内容期望。

native步骤按已验本机FSEvents/runner/caffeinate授权方法，保存command/cwd/HEAD/UTC起止/exit/elapsed/resources/完整console并自然结束；不TERM父runner/活跃测试、不补造exit、不覆盖旧--output。修源后新freeze/实际受影响正式轮不混拼，before/focused/mutation/formal分开。套件耗时/内存是观察，本Task不替代最终T-012单CMD≤2秒接受线测量。

### 7. capture、四ID闭账与交付

fresh before/after ConfigureServices-only capture，不BuildProvider/启动业务；family sidecar纯构造真实两注册表，保每logical唯一whole/exactCLR、全部已有family/exits。预期173logical/203cmd总数不变，删两Tx fine加两Direct whole：24Protocol＋1Internal＋178Legacy，150Tx/40Direct/9ReadOnly/4Workflow；message26Protocol/147Legacy（原14telemetry仍同总账）。逐row证只有两route/两fine→两whole变化，不以预期代实得。

ledger.json/md仅可闭`message:65617` GameResetItemQuality、`message:65992` GameResetRandomOption、`command:Dfo.Server.Protocol.Game.ResetItemQuality.ResetItemQualityCommand`、`command:Dfo.Server.Protocol.Game.ResetRandomOption.ResetRandomOptionCommand`。填真实原caller/Transactional/实际条件参与者/Main最小Inventory/保护去向、whole/测试/Result/artifacts；state用真实state键，original/baseline/其它行不动。仅预期pending135→133message、180→178fine、72source/5protection/8transition不变，不顺闭来源/过渡或CollectionBox额外raw问题。

证据限`verification/logs/task-16-*`与`verification/evidence/task-16-inspection/**`；只精确owned beforeimages（含Quality slice原134CRLF＋5bareLF混合）、新增/删除清单＋非owned tracked SHA，不复制整仓/旧归档/bin/obj。slice的只读DTO/decoder/encoder/composer原byte范围独立hash对照，保CRLF/混合行尾不整文件格式化。现probe-tools只读复用。完整source freeze/逐case delta/原强assert目的与native阶段/诊断/manifest/Result R1 prefix齐；preclose先核真实artifact，追加Result后strict validate和原八坏副本全拒，不提前填不存在Result骗strict。

最终停共享写、交fresh required独立结果快审；review只追加已freeze R1 prefix，主控消费精确SHA/物理集合后本地检查点，executor不自行stage/commit。Result须说明两whole与两fine零引用、实际Main/正常拒绝/回滚/Ordinal/current、无domain footprint链、reseal-null/ushort/原帧、SQL/native新旧对照与原Throws、mutation/恢复、NotiBus与直接ACK保护、13路径及实际N－D/三full四control边界。余133message/178fine/72source及5/8仍后继；T-012最终新八full/测量、Astra/Spec及根封存、用户分支收尾不免除，不提前宣告B完成。规划阶段只有本契约/详细计划，无Result/review/status。


## Result

- Revision: R1（Task r1；HEAD `d7373b9dd33184ece5f419972e75c45fad48b0d8`；source-freeze-01.json SHA `bbad6e34bc30961dcfb816422629341c84bc4dba065f0cd7aa7f08872a3ce5fe`，13源；待fresh required独立结果快审，未stage/commit）
- Outcome: done
- Changed: 两logical/原精确CLR各由GameResetItemQualityCommand、GameResetRandomOptionCommand完整接管，薄handler仅原Input/Dispatch交同bus；两旧fine CLR/handler/Tx registration删除，无兼容alias/legacybus转发。只6生产/文档＋7测试路径；sharedcore/Task15机制、domain/规则/接口/Persistence及EffectRequestTransport和其它helper均只读。两slice原DTO/decoder/encoder/composer byte范围精确相同，仅必要Dispatching using，Quality原134CRLF＋5bareLF混合保留其只读范围，未整文件格式化。见[workunit-map](../verification/evidence/task-16-inspection/workunit-map.md)／[source-checks](../verification/evidence/task-16-inspection/source-checks-final.json)。
- Main / domain: 原session→Valid/online→两Main槽位换算顺序保持，初始Rejected晚出现selection不能升权，早拒不Main/末PendingRejected；仅原fine有效domain分支claim一次Main。两领域无footprint参数，不造cap/recorder链或调用unit.Footprint；Main最低Inventory可空TouchedSlots，成功与正常Refused均Committed。Quality严格TryReseal非null（含拒绝）不回落，仅null才Reroll；原ExpectedId/IsInWorld及OptionIndex判定都在原domain，ushort256完整进入领域成为InvalidOption/0x13，不新增前置门。
- Failure / protection: 只原domain链窄catch非取消/非typed普通E后RequireRollback(E)，unit完全结束才处理原Error/0x13。只消费Task15封闭Returned／已证CommitRolledBack；current/Main/Freeze/guard/Unknown/typed/cancel/cleanup/outbox/Project/encoder不被业务catch吞。正常domain拒绝不RequireRollback。E＋独立rollback X含同对象由已验机制保两因，本两协议真实SQL连接反例确认真实资产回滚、无正常失败ACK及精确物理fence/close。只既有CommittedReplayCurrentStateBatch，不增加raw/NonRequest或改两B2许可。
- Frames / notification: Quality成功9B0051 ACK（材料slot/count/Operation1wax或2quality）→target000E，不加材料NOTI；Random target000E→material000E（耗尽-1）→01C8[1]。原composer精确session/connection/InWorld守卫与全部字节不变。首通知经ICommandProjections→EventService→NotiBus仍逐目标Failed正常隔离，Committed/nofence且后续仅current replay不再扣减。直接拒绝ACK两类型均用默认：domain正常拒绝已Committed后encoder外抛typed/fence；earlyRejected或ConfirmedRollback后的同IOException保原cause、按实际precommit source-close无新fence，不借异常类开raw。
- Before / after native oracle: 产品迁前70/70强定向＝原两类66＋新增4完整wire；6生产仍HEAD，保存3最小测试源、before freeze与原观察。两个logical各action UPDATE真实19/1811与native deferredFK真实19/787，实际participant连接FK1、action已receipt/目标和材料已改/child1后真正SqliteTransaction.Commit stack；原0x13完整帧、Error级别/同cause、transaction和entry ConfirmedRollback、原Ordinal1、UID/core/material/gold/events/deliveries全恢复、nofence/source-close、去trigger同entry只扣一次、current replay无二ACK。迁后正式轮同4oracle，metadata/raw/hash/ACK/资产/状态/projection/Ordinal/retry/replay逐字段精确对照；旧fine pipeline＋handler双Error改仅whole handled Error，原cause及级别保留，不补回旧管线。见[before](../verification/evidence/task-16-inspection/before-oracle-observations.json)／[after](../verification/evidence/task-16-inspection/final-protocol-observations.json)／[compare](../verification/evidence/task-16-inspection/oracle-before-after-checks.json)。Freeze后native确认回滚保projection，下一admission才清理；同CMD不二claim，retry不延期原Ordinal。
- Original tests / caller closure: 原Quality与MagicGrade的所有Fact/Theory/PVF/InlineData与display/status/multiplicity保留。具名typed Reset/Grade driver每次同host.Services/TestGameplay、显式旧owner Capture(owner,null)/DemandCurrent前后、真实DomainTx/Commit/Abort(E)/Dispose，保纯domain Throws/原SQL/nativeFK/UID/seed/core逐byte/quickslot/stale/并发单耗/PVF和decoder Empty用途。原native两case仍receipt后手工deferredFK再Commit Throws，明确是领域证据，不以whole ACK吞异常替代。两marker名单仅移两fine；注册新增case核两Protocol/Direct/exactCLR/replay-only及两fine注册零，原Use/Buy/Opening等case保留，无构造器豁免改动。
- NoSession boundary: 自然缺材料/InvalidOption是实际MainCommitted；自然未InWorld的registry不能由Admit/Capture获得online.CurrentCharacterId，未强造该状态。按主控既定裁定，两有界受控query反例仅IsInWorld=false，其余Standing/current/registry/physicalowner/真实domain与SQLite全部委托实际实例，证明没有把原domain NoSession擅自前移，结果MainCommitted/资产不变；不是自然registry路径实证。初始Rejected晚Select与下一fresh正常另证。
- Mutation / useful RED: 只临时变异owned GameResetRandomOptionCommand实际domain调用，将OptionIndex cast byte；真实wire256导致材料2→1与option0(15,2,9)→(15,30,30)，新Assets＋ledger oracle及原indexHighByte共同红，11case9pass/2fail、无编译/缺服务/无效metadata冒红。finally精确原bytes/SHA `03786aaae1ced9c5cba3d92bcc223faa2c76a0702ad1cbb5c0f3e7e05cfd0716`恢复；恢复142/142定向＋14/14注册GREEN后freeze01。见[mutation](../verification/evidence/task-16-inspection/mutation-result.json)。原行为基线通过不称TDD RED，结构迁移不靠伪业务测试强迫。
- Fresh final checks: 同freeze01完整Release `--no-incremental` 0warning/0error；App/Protocol/Networking三项分别无filter full，原class并行/5m/期限/Skip不变：App4964＝4946pass＋2准确原Dimensionfail＋16skip、Protocol1768pass、Net199pass，三full6931＝6913pass＋2fail＋16skip；Host/HttpApi/Infrastructure/Persistence仅各原exact control1case实际通过，不称业务full。原两失败仍Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource及All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，均原Dungeon7100/maze0/cell(1,0)/entranceLeft InvalidDataException，无新baseline。七程序集各唯一原control、DFO原74 expected精确/outside-exe-orphan0（exe0仅观察），新有意违规0；Golden15逐byte对f01dc226/HEAD，Char10逐名pass。见[formal](../verification/evidence/task-16-inspection/formal-summary.json)／[DFO](../verification/evidence/task-16-inspection/assertions-check.json)。
- Inventory / scope: 相对Task15 final03三full原6901逐display/status/multiplicity全保，N29App＋1Protocol＝30、D0/rename0/merge0；全局延续8731＝8711pass＋2原fail＋18原skip，另1800是Task15库存而非本轮business重验，Infra两skip不称已复测；四control另记不重复加到库存。9精确owned beforeimages＋4new、非owned tracked13136仅SHA（两授权ledger排除后13134逐SHA不变），无整仓/bin/obj快照；R1 prefix与manifest逐文件SHA/bytes/物理集合封存后停写。
- Capture / ledger: fresh ConfigureServices-only before/after173logical/203cmd不变，实际24Protocol＋1Internal＋178Legacy，150Tx/40Direct/9RO/4Workflow，message26Protocol/147Legacy（14telemetry）；仅2route/2fine→2whole，其余family/bindings/Use两出口/既有permission全部不变。family sidecar复用已验只读工具纯构造真实registry，无BuildProvider/启动业务。仅闭message65617/65992和两个精确fine ID，original/baseline/其它行不动、md原bytes前缀保留；preclose已过，本Result后strict与原8坏副本以ledger-strict.json／ledger-eight-bad.json及独立meta实得为准。
- Diagnostics: 原whole-focused01漏Protocol message接线（140/81pass/59fail）、02新Trace漏DI（142/113/29）、03新fixture重复Main容器（142/140/2）均真实保留，先源证根因再有界修复，不称SQL/Main RED、不改原assert/并行/超时/Skip。Summary01误把Task15两条既有oracle一起数成6，Summary02按新grade字段正确区分4，native/source未变，旧输出不覆盖。每步command/cwd/HEAD/UTC/exit/elapsed/resources＋详细console，自然结束，无TERM/补造exit。详见[diagnostics](../verification/evidence/task-16-inspection/diagnostics.md)与native-step-index。
- Remaining: 本单元待fresh required独立快审及主控检查点，executor未自批/stage/commit/push/PR/merge/tag/版本/游戏/部署/cleanup/Task17。其余133message/178fine/72source及5保护/8过渡，T012最终新八full/单CMD≤2秒测量、Astra/Spec/根封存、用户分支收尾仍pending，不宣告B完成。


## Independent Quick Review — TASK-16 r1 / Result R1 / Round 1

```yaml
kind: rks-review
verdict: approved
```

Reviewer 为 fresh independent `b_task16_review`，非本单元 planner／executor，未再委派。批准仅覆盖本 Task 的两 logical／两 fine 品质协议迁移；没有批准整个 T-004、Spec B 或根目标完成。使用 `reviewing-work/SKILL.md` 的 Task Unit、Code、Deliverable 判据及 `verification-before-completion`，遵守 `using-rks-agent` bounded-subtask 规则。实际读取本 Task r1／R1、B2 相关 RQ／AC、P3 有界结果入口与 T-004、匹配 approved reviews、已提交 Task15 r2／R1 的 required approved review 与 controller consumption、实际 AGENTS 和两份架构偏好。无 `.codegraph/`，采用 rg、定点源码、HEAD 原文与只读 Python 对照。未运行 native／build／test，未改产品、台账或既有证据；唯一写入是在原 R1 prefix 后追加本 review。

### 身份与改动边界

- 显式 workdir 为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`；branch `task/refactor-session-20261021`、HEAD `d7373b9dd33184ece5f419972e75c45fad48b0d8`，暂存区为空。Task15 全文逐 byte 等当前 HEAD，包含 fresh approved 与主控消费。Spec B2 SHA `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228`、P3 SHA `778a28e67377d099d0d0cc43739e5c3083566c5895cb3292f2b47e2625909519` 实际匹配各自 review。
- 下文 E16 指 `../verification/evidence/task-16-inspection/`。实际核 `source-freeze-01.json` SHA `bbad6e34bc30961dcfb816422629341c84bc4dba065f0cd7aa7f08872a3ce5fe`，13 个源 SHA 全部匹配。`manifest-r1.json` SHA `6a6db350f97bb82a181eda4a91ad6dcaeb5b49cf9c9fd20113c28b611c633fc0` 的145项 SHA／bytes全部匹配；加 Task 与 manifest 自身等于147个精确 Git／physical 变化路径，无额外或缺漏。
- 原 nonowned map 13136项只排除两份授权 ledger，其余13134项逐 SHA 不变；sharedcore、Task15机制、domain／规则／接口／Persistence、原工具与 helpers、B2／P3／既有 gates 及原证据无旁改。`git -c core.whitespace=cr-at-eol diff --check` exit0；未以裸 CRLF 检查要求格式化只读源。
- 审前 Task 全文与 E16 `task-result-r1-prefix.md` 精确相等：31869 bytes、SHA `c98b59ab4f2a261d8ea201641b0df894f90014e742220924ead79fb0749fd9c3`；原契约 prefix SHA `d3309b6c674c398e2bc50ba5b783b25a9952c6773b2baa9ae7bdb76209f44947`。本 review 不改该前缀。

### 决定性局部检查

1. **完整接管与原协议保持。** 实读两旧 HEAD slice、当前两 whole／薄 handler 和 ProtocolModule diff：两 handler 只交同 bus 的原 Input／Dispatch；两 fine CLR／handler／Tx注册删除，无 alias、nested CMD、旧 bus 转发、sender／locator 或新 raw／NonRequest。两个 slice 从原 DTO／decoder／encoder 前段到原 Completed／composer 后段独立逐 byte 对 HEAD 相等，仅新 Dispatching using 例外；Quality 原混合行尾的只读范围保持。新注册为各自唯一精确 CLR／Direct Protocol whole，只有既有 CommittedReplayCurrentStateBatch。
2. **Main 与业务拒绝没有前移。** 两 whole 保 session→Valid／online→target／material Main映射顺序，initial Rejected 不因晚选角升级；原有效 fine 分支才 claim 一次 Main。Quality 同一 action／事务中仅 TryReseal 为 null 才 reroll，非null拒绝直接保留；Random完整 ushort 入原 domain，256仍 InvalidOption／0x13，未新增前置 index、ExpectedId 或 InWorld 门。实读两个未改 domain，确无 recorder／ApplyEffects 参数；最低 Inventory Main 可空 TouchedSlots，正常缺材料／InvalidOption仍 Committed。受控 NoSession 两例只将 IsInWorld 查询置false，真实 Standing／registry／current／owner／domain 继续委托原实例；它是查询顺序反例，Result 已准确披露其非自然 registry 状态边界。
3. **恢复仅消费已验机制。** 业务 try 只包原 domain 链；普通非cancel、非typed E 调 RequireRollback(E)，其自身错误不被同 catch 再吞。整个 WriteResult、current／Main／Freeze、投影／outbox／encoder 均在业务 catch 外。实读只读 runner 的 GuardFailure 检查、KnownStorage／同cause／ConfirmedRollback 窄 Commit span、清理与 TrackOperation；两 whole 只消费 Returned／CommitRolledBack，不自签回滚。新实际 action SQL＋独立 rollback X 的不同／同对象两例要求双因、真实资产回滚、无失败ACK正常返回和精确 fence／close；取消／typed／Unknown／Guard 等既有强case由本轮三full保留，未借本 Task 改机制。
4. **旧新完整 wire oracle 有真实来源。** 核 E16 before-oracle-freeze：四既有生产路径逐 SHA 等 HEAD，两新 whole 在 HEAD 不存在，三最小旧测试源 SHA 匹配；旧 flow 调同provider实际领域，经真实 decoder／dispatcher／旧route。before原70/70包含两原类66和新增4。当前 flow 的 forwarding observer 调真实 whole／domain，并经同 SqliteDatabase 读取实际 enlisted connection。四组 old／new observations 独立逐字段对照 ACK完整帧、metadata／hash／seq、资产／UID／事件、事务／账本状态、projection、同cause／Error、原Ordinal、retry／current replay；action为19/1811，native两组真实FK1、receipt／材料已扣／target已改／child>0后进入 SqliteTransaction.Commit 19/787。正式 raw console 的4组与final observations精确一致。原双Error→单whole handled Error为批准的重复日志消除，原cause与级别保持；无孤立probe或fakefallible冒充native。
5. **成功投递与测试目的保持。** 两原 composer 字节未改，Quality仍9B0051 ACK（material/count/operation）→target000E，Random仍target000E→material绝对update／clear→01C8[1]。新flow另证首通知编码Failed隔离、Committed无fence及current replay不二扣／ACK；两种直接拒绝ACK编码故障在earlyRejected／Committed／ConfirmedRollback三个阶段要求各自默认保护，未新增raw许可。原两测试类声明／InlineData逐 byte保持，typed Reset／Grade driver同host.Services、原owner Capture(owner,null)／Demand前后、真实DomainTx／Commit／Abort(E)／Dispose，无任意Func命令平台；原Throws、手工nativeFK、SQL、quickslot、stale及并发目的均保留。
6. **有价值RED与正式证据绑定当前源。** 实读 mutation raw log，ownedwhole将OptionIndex截byte后11case9pass／2fail；新wire256实际材料2→1、option0(15,2,9)→(15,30,30)，原indexHighByte同时红。原whole SHA `03786aaae1ced9c5cba3d92bcc223faa2c76a0702ad1cbb5c0f3e7e05cfd0716`现已恢复；恢复142／142及注册14／14 GREEN后才freeze。完整Release原console为0warning／0error。独立从raw三full console建立display／status／multiplicity Counter，相对Task15三full6901无删除／改名：App4964=4946pass+2原Dimensionfail+16skip，Protocol1768pass，Net199pass；合6931=6913pass+2fail+16skip，N29App+1Protocol=30／D0。两失败身份及Dungeon7100/maze0/cell(1,0)/entranceLeft原内容错误一致，未登记新baseline。四其它程序集实际各仅原exact control1，不称业务full；8731延续库存另1800仍来自Task15，Infra2skip未重验。
7. **取证、capture与四ID闭账。** 原DFO raw7文件独立计74（App62／Net7／Protocol1／其它四各1），七control唯一；既有CLI细分计数及outside／exe／orphan0吻合，exe0仅观察。Golden15逐byte同时等f01dc226和HEAD，Char10正式console通过。before／after capture独立按row核173logical／203cmd：只有message65617／65992路由变化，旧两fine消失、新两whole出现，其他command行不变；实际24Protocol／1Internal／178Legacy、150Tx／40Direct／9RO／4Workflow、message26P／147L。ledger按HEAD逐row仅四授权ID不同，各original／baseline与其它行相同，md旧bytes是精确前缀；strict valid及原8bad全拒日志已留。没有顺闭source／保护／过渡或借总数替代闭包。

### 判决与局限

本 Task r1／R1 未发现未决的局部 blocking defect，required结果快审 **approved**；可交 controller 对同一冻结结果作消费与本地检查点，reviewer不推进生命周期。证据检查是对已执行console／meta／源冻结的独立复核，本 reviewer没有重跑native或测试，批准不扩张为新实机验证。

未验证A21实机、最终单CMD≤2秒性能或最终新八full；其余133message／178fine／72source、5保护／8过渡、T-012、Astra、Spec／根封存和用户分支收尾仍是后继义务。初始Rejected fixture诊断和summary01误计6的旧记录均保留，正式summary02精确4，不将诊断视为有价值RED。以上边界已在R1明示，不构成本次两协议局部批准的阻塞。


## Controller Consumption — TASK-16 r1 / Result R1

主控已完整读取Task/Result、两旧slice/两新whole及两未改domain，并消费fresh independent Round1 approved全部依据。已独立核13个freeze01源SHA、145项manifest SHA/bytes、147实际物理/Git变化与31869字节R1 prefix；22941字节契约prefix仍d3309b6c674c398e2bc50ba5b783b25a9952c6773b2baa9ae7bdb76209f44947。manifest6a6db350f97bb82a181eda4a91ad6dcaeb5b49cf9c9fd20113c28b611c633fc0，源freeze bbad6e34bc30961dcfb816422629341c84bc4dba065f0cd7aa7f08872a3ce5fe，暂存区为空。

主控独立原console Counter核Task15三full6901全部display/status/multiplicity保留，N29App＋1Protocol＝30、D0/rename0；本轮三full6931＝6913pass＋准确原2fail＋16skip，四actual control各1通过。8731＝8711pass＋2fail＋18skip为延续库存，另1800只Task15业务库存，Infra两skip未重验；不冒称八full。Release0/0、DFO74/7、Golden15、Char10、真实旧新4oracle/capture/strict8bad均有当前冻源证据及独立复核。

两domain无cap，Main最低Inventory/正常RefusedCommitted、reseal仅null回落及完整ushort256→InvalidOption0x13保持。自然NoSession不可达与受控查询顺序反例已准确分开；原SQL/native pure Throws未被whole拒绝ACK替换。byte截断mutant真实消费/改错option被原新两强oracle抓住，恢复后才冻结；sharedcore/规则/codec/composer只读。已逐row核仅两个message/两个fine共4ID变化，original/baseline/其它行不动。

结果支持Task16本地checkpoint；133message/178fine/72source及5保护/8过渡、T012/性能/Astra/Spec根封存仍后继，不以局部快审替代，不push/PR/merge/tag/版本/游戏/部署/清理。
