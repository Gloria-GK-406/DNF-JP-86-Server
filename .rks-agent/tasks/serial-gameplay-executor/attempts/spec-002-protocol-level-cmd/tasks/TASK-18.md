# TASK-18 — AvatarCompound 完整效果协议 CMD

## Task Unit Contract

- Revision: 1（本契约与详细计划的同一修订）
- Outcome and references: 交付[Plan P3](../plan.md) T-004 下一有用单元：GameAvatarCompound logical65635/type0063由一个GameAvatarCompoundCommand吸收原handler/fine编排，删除AvatarCompoundCommand。消费已接受Main/显式footprint/WriteResult/Inventory|AvatarCompound最低投影及现current replay，保先创建新output、再消费输入的原事务顺序、拒绝/UID/89或131B首响应/26B恢复UI。覆盖[Spec B2](../spec.md) RQ-001/003～009/012/014/015、AC-001～005/007～009及F-34/F-88/F-93/F-112；不新增VC/NonRequest/raw许可，不宣称T-004完成。
- Owned boundary: §2精确10个生产/文档/测试路径、本Task及task-18新证据；migration-ledger.json/md仅§7两个精确ID。DTO/decoder/encoder/同slice composer、domain/规则/接口/Persistence、Task15共享core/Task16/17、reader/sink、helpers/transport、Spec/P3/契约/root/A/baseline/original/旧Task/reviews/证据只读。不并Socket/其它Inventory，不加公共协调器/构造器豁免或新机制。
- Required checks: 必须真正先跑产品前fresh注册capture与旧route action SQL/native Commit两条完整wire oracle，再迁后同provider同输入/状态对照；真实domain/footprint/Main/SQLite/ItemUsed、earlyRejected/正常RefusedCommitted、输出创建先于消费、独立新UID/无残留、正常/稀有帧、26B UI先当前库存、同entry原Ordinal retry/current不重delta、NotiBus Failed/直接ACK默认保护及E+cleanup保因。纯领域Throws/PVF/SQL/概率/codec原case全保，一个ownedwhole有价值可编译mutation红绿恢复。完整Release0/0＋App/Protocol/Networking三full＋其它四actual原DFO controls、74+7/Golden15/Characterization10/capture/strict原八坏副本；8761＋真实N－D库存与实测范围分开报告。
- Dependencies: TASK-17 r1/Result R1/fresh required独立approved与main消费，本地检查点`c03ae3476d52fe8afaf9165047877fb7958ae046`（parent `1dc9bfb1f4952f101f5dee8b3fecadb69e0e31a8`）。消费其typed领域driver/真实cap guard/单provider observer/窄旧新wire与失败连接性方法，以及Task15 WriteResult/来源/带cause保因、Task11～16 Main/唯一ledger/replay/finitefamily/NonRequest/B2两许可。B2/P3/gates不变，Spec baseline仍`10a7218a779493494b138bd6b43cc0fc1a84aa6a`。
- Result review: required — 误迁可先丢输入再失败、错给输出/重复扣box、丢UI恢复或将guard/不确定性变普通17，编译/codec/总数量不能识别这些状态。fresh独立结果快审核真实source/前时点证据/原oracle/数量与两ID，主控消费后检查点。Task-plan仅main readiness，不另设独立审批门。

## Detailed Plan

### 1. 起点、已接受输入与产品前顺序

主控先核精确HEAD、workdir `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`、branch `task/refactor-session-20261021`、唯一写者/owned状态；读AGENTS/两架构偏好、B2/P3及[Task17 R1/required review/controller consumption](TASK-17.md)。无`.codegraph/`则rg优先。中文、本地实施核验/主控检查点授权保持；不push/PR/merge/tag/远端issue/版本/游戏/df_game_r/DNF.exe/部署/cleanup，不创建Task19。真实白名单外caller/sharedcore依赖或否定中心方案先报主控source/最小方案，不静默扩scope。

当前8761＝8741pass＋2准确原Dimensionfail＋18skip；最新App4993＋Protocol1769＋Networking199＝6961，同Task17 freeze的App01/Proto01/Net02是有效三full，Net01额外bind48诊断保留，不当根治或基线。另Host203/Http57/Infrastructure976/Persistence484/Pvf80合1800仍Task15 final03业务库存。capture173logical/203cmd＝25Protocol＋1Internal＋177Legacy、149Tx/41Direct/9ReadOnly/4Workflow；message30Protocol/143Legacy；pending129message/177fine/72source/5protection/8transition。

**先执行、再迁移，不能补造前时点。** 最小preflight只精确owned before原bytes/BOM/行尾、新增删除清单与非owned tracked SHA，不复制整仓/旧档/bin/obj。生产四路径仍精确HEAD时，真正运行fresh ConfigureServices-only registration capture及family sidecar（仅真实registry构造，不BuildProvider/启动业务），保存实际生产SHA/HEAD/cwd/command/UTC/exit及原输出；然后只增本Task测试/observer，跑原强定向与§5两条旧完整wire，保存最小before测试源/raw/meta。确认实际注册before和旧route两oracle均有前时点证据，再写whole/迁route。不能遗漏后用旧Task输出/HEAD重建冒称fresh-before；Task17重建偏差已经披露，本轮不得照搬。此是本簇取证精度，不重开Task15共享机制gate。若旧实得否定既定异常恢复假设，保诊断向主控报告，不先改产品再倒称before。

### 2. 精确白名单（生产/文档4＋测试6＝10个source路径）

下列相对workdir，只有列明职责，不授整目录。新增同职责类型收在指定文件，不另建通用helper。

| 路径 | 可写职责 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/AvatarCompound/AvatarCompoundSlice.cs` | 精确删旧AvatarCompoundCommand/命令handler，消息handler薄化为同bus/原Input/Dispatch/Protocol marker；仅必要using。Request/Response、decoder/encoder、AvatarCompoundCompleted/composer原代码/字节只读 |
| 新`Server/src/Dfo.Server.Protocol/Game/AvatarCompound/GameAvatarCompoundCommand.cs` | 唯一whole record/handler，原强类型Input/Dispatch/AvatarCompoundResult；搬原前置/地址变换/窄domain catch/日志/错误码/投影 |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | logical65635切唯一Protocol/Direct whole/精确AvatarCompoundRequest，删原Tx fine；仅CommittedReplayCurrentStateBatch，原decoder/encoder/notification不改 |
| `AGENTS.md` | 仅当前AvatarCompound opt-in/fine删除/Main与原响应必要事实，不全篇重写或改变其它责任 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/AvatarCompoundTests.cs` | 原Roll fine caller迁§5 typed领域driver；保原业务/Throws/SQL/PVF/明确seq/UI/概率/字节case，允许Content/Fixture/seed最小internal可见性供新flow与必要同职责补证 |
| 新`Server/tests/Dfo.Server.Application.Tests/Inventory/AvatarCompoundDomainTestDriver.cs` | 仅具名Compound(host,owner,AvatarCompoundInput)同provider真实事务/current-owner/每调用新test footprint，保纯领域结果/Throws；不开放任意Func或CMD测试平台 |
| 新`Server/tests/Dfo.Server.Application.Tests/Inventory/AvatarCompoundProtocolCommandFlowTests.cs` | 本protocol前时点旧新wire/action/native、真实Main/足迹/资产/UID/UI/current与失败保护；本地typed domain forwarding/actualhandler wrapper，不第二provider |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/EffectSettlementRegistrationTests.cs` | legacy marker名单只移AvatarCompoundCommand，其余marker/反例保留，新whole元数据另证 |
| `Server/tests/Dfo.Server.Application.Tests/World/CommonExperienceEffectTests.cs` | 同上精确移一fine，不顺清其它命令 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` | 单logical/唯一whole/Direct/Protocol/exactCLR/replay-only/无legacy marker及fine注册零；保Use/Buy/Opening/Quality/Socket/Matrix等原case |

fine生产构造唯一caller slice:115、Module原Tx注册；测试fine caller仅AvatarCompoundTests:67及两marker名单。slot boundary无此fine，LegacyEffectFootprintTests:289/297只反射domain接口/构造器，均只读；没有已证白名单外必需编译接口依赖。IAvatarCompoundDomainService/唯一实现/ApplicationModule原注册、AvatarCompoundRuleService/全部存储、ReplayStateProjectionDelivery/reader/ledger/core、EffectRequestTransport/ShopFixture/RecordedOutbound/Task15～17 helpers/tests只读。若实施发现新caller先报精确位置。

Protocol AvatarCompoundCodecTests和Host AvatarCompoundTcpTests/EndToEndHostHarness只读；它们是保留的原oracle，不为占测数或driver统一而修改。Host现仅未选角22/21B请求拒17不断线，不冒称完整成功/事务路径。

### 3. whole、Main与失败结果

record实现`IProtocolCommand<AvatarCompoundResult>`，Message＝原Input，Frame＝原Game metadata；薄handler实现`IProtocolCommandMessageHandler`且只构造/await同bus唯一whole。不注入bus/sender/locator/工厂/当前请求DI/规则/内容，不CMD→CMD/legacy转发。whole注入原session/online领域查询、具名地址映射、IAvatarCompoundDomainService、ICommandWorkUnits/ICommandProjections/IGameCommandOutbox及原日志，沿现封闭清单无新豁免。

完全保原顺序：FindByConnection→Valid/session/online不足拒17→Main material与两Avatar槽映射/原expected IDs/target/完整ushort Ability/RandomTarget/Category原样构造输入，catch InventorySlotMappingException拒17。映射catch不包outbox登记（记录前置拒绝后在外发），不把编码错误当地址错误。不得提前同槽/模板/职业等级/ability/category/寿命/材料等domain规则。ProtocolEffectRejected晚出现selection不升级；早拒不Main/Pending→Rejected。只有原fine分支验证本次Selected及精确captured Session，开一次`WriteResultAsync(context,new(具名unit,[selected.Recipient],MainSettlement),action,ct)`。

getter `unit.Footprint`在业务try外，直接原`CompoundAsync(session,input,footprint,ct)`。domain首次/各写前DemandActive保留，若拒绝已记sticky不可借catch恢复。只该domain调用窄try捕获非cancel/非ContainsUncertain E，保存原cause、RequireRollback(E)并返回本地失败outcome；RequireRollback自身guard不吞。caller侧request/Main/Footprint getter/Freeze/来源/current/cleanup/outbox/Project/encoder机制动作不纳入try，不外catch整个WriteResult、不清sticky或自报已回滚。

unit完全结束后匹配Returned或CommitRolledBack.Cause；正常domain result保原result（无receipt正常Refused也MainCommitted，绝不统一RequireRollback）。只捕获E或已证commit rollback才Error原级别/同cause，unit外发17。Guard/Unknown/cancel/typed/cleanup/Committed/额外scope/drain仍由只读机制默认保护。正常拒绝MissingMaterial16/UnsupportedLifetime13/LockedD2/其它17；原信息日志主题/字段守住，handled category迁新whole，不恢复legacy重复pipeline。

请求最低`Inventory | AvatarCompound`始终保持，不仅成功才加UI、不从ReplayProjectionNeeds.All假定包含UI、不单独复刻reader/sink。只有本次selected actor为Main participant，目标/物料/outputs由原domain判断，不扩大到通知受众。成功unit后只ICommandProjections→原TheseCharacters/AvatarCompoundCompleted；无receipt unit外typed ACK。首通知普通Failed继续NotiBus隔离，不新raw；直接ACK按默认。只existing CommittedReplayCurrentStateBatch许可，Committed同seq不再whole/业务/原deduction ACK，而由原ordinary Write reader/current sink恢复。

### 4. 原事务顺序、字节与强oracle

原domain:22–108有必需footprint、当前事务及角色/物品事实，不用ApplyEffects/selfTx/helper→bus。排除first/second占位算output目标→**先创建全部output records/new InstanceValue**→按first、second、box顺序consume→put全部output→deductions/RecordInventorySlots(first/second/material/outputs)→同TxItemUsed。SQLite CreateAsync真实Insert输出行（SqliteItemDomainStore:88–103/327–348），不能挪成先删输入后create或提前发UI。原plan失败不消耗；新UID必须独立于两旧输入，回滚无孤立output/custody，不额外臆测全局UID allocator回退规则。参与者按实际同库facet/容器/lifecycle/event batch动态加入，不将fixture集合称所有配置固定全集。

首成功composer只有一个AvatarCompoundResponse(receipt)，不加库存NOTI。原encoder写deductions后固定两份conditional result；非稀有89B、稀有131B；缺第二输出写FFFF/模板0跳过额外块，不伪补空Avatar。输出永久/能力/30B JewelSocket/染色/UID字段由原业务/codec保持，不能从new whole另拼协议。

原ReplayStateProjectionDelivery:69–74先加Recovery，再current库存：Recovery body26B=[1,0]＋两份空结果，无扣减/无新输出，UI临时恢复后由当前库存纠正。同seq不能重发89/131B receipt或再消费/roll/记ItemUsed。保MinimumNeeds两bit，即使domain正常拒绝也不擅自删UI needs；earlyRejected按原账本出路，不造Main。无initial raw许可，不改reader/sink/其即时编码阶段。

Task17已验AvatarCompoundTests原27case全保display/status/multiplicity，Protocol AvatarCompoundCodecTests原4case保持。AvatarCompoundTests原强case全保：:82普通/稀有1/2输出、两旧输入和box消费、新UID高于旧、permanent/nojewels/新identity；:100首包89/131B与deduction/conditional字段；:119材料/template/同槽/target/ability/category/part/grade/锁/寿命拒绝；:143 output已create后BEFORE DELETE item_slots trigger的真实action SQL Throws/输入box恢复/无两新输出；:154并发pair只用一次；:163权威职业等级；:170(UI旧定位176)26B恢复先库存；:180后真PVF材料/options/golden随机及概率三层纯规则。以当前源码实际行/case身份为准，不按旧行号机械删改。

原fixture:40即使customContent也用真实PVF placement/CanUse/job/level，原Skip条件/职业0等级80/Avatar容器保持，不借简化NPC样板或fake plan代替；PVF不可用时保真实skip/停止所需oracle，不伪称实跑。Protocol codec保unsigned slots/ushortAbility/22B及checksum26B/拒绝/Recovery；不能修改codec来让新错误输入通过。

### 5. typed领域driver、必要旧新wire与新反例

原Roll:67改具名Compound(host,owner,input)：每次同host.Services独立TestGameplay job，Capture(owner,null)/DemandCurrent前后、Begin真实DomainTransactionScope、每调用新TestEffectFootprints.Lease(owner)，直接原CompoundAsync/Commit；catch E实际AbortAsync(E)原throw，finally Dispose。保纯领域结果/Throws/SQL/PVF/并发/UID目的，不能全换whole吞异常；不留oldfine兼容record、生产setup CMD、任意Func平台/静态lease/反射cap。wire Send:69已真实EffectRequestTransport，readonly，Send在准备job外；原seq7 retransmit保持，独立fresh用现FreshSequence，不同body同seq不得当新请求。decoder-only Empty用途不改。

§1产品前capture完成后，新增两条oldwire oracle共用原Content Rate10000确定两output、真实两输入/box种子：一条沿原BEFORE DELETE item_slots RAISE(ABORT)为action19/1811，观察已新建output行后首次consume失败；另一条seed完成后建fixture deferred FK parent/child＋AFTER INSERT ON items trigger，按实际列`WHEN NEW.avatar_expire_date IS NOT NULL`只捕获新Avatar输出（原Create输出有AvatarDetail，box部分Take还会Insert临时pile，不能全表INSERT都误数为output），使真实两输出创建成功/child2、domain完整receipt/两output stage和输入/box已stage，随后SqliteTransaction.Commit真19/787并stack。不猜不存在item_id列；actualitems主值为item_core/附属列，DDL只需实际INSERT点，生产schema只读。新flow的typed domain forwarding observer记录真实tx/cause/receipt及stage。

stage内ForeignKeys、output UID/行/物料与child查询必须经同provider SqliteDatabase.ReadAsync/WriteAsync加入Current actual participant；ShopFixture.Scalar/Sql另开连接只用于seed/DDL及前后持久状态。不能用fake CommitCanFail、孤立provider、外部手插FK坏行或普通action trigger冒充完整协议native。fixture失败须保日志、确认真实write点/完成阶段，不放宽native要求；DDL生命周期限fixture。

两oldwire保存真实metadata/body/hash/seq/ordinal/产品SHA/最小测试源/meta/原17完整ACK，Error原cause引用/级别，tx及entry ConfirmedRollback、两旧UID/core/box/gold及ItemUsed/events/deliveries还原、无新output残留、nofence/source-close；去trigger同fingerprint同entry原Ordinal retry仅一次合成，再同seq只26B recovery→current库存无重复delta/ItemUsed。迁后同raw/seed/oracle逐字段对照；仅旧pipeline＋handler双Error移为whole handled Error，同cause/级别守原。native Freeze后projection可非null，下一admission才清，不重置main claim/Ordinal。旧action并不称native，现原测试没有native case，本新两oracle覆盖一个协议两故障，不夸所有规则分支已做native。

新flow只本地typed IAvatarCompoundDomainService forwarding wrapper调实际singleton＋actual whole wrapper/logger，沿Task15～17已验shape，无第二provider/发件箱/支持层修改、不为真实logical注册probe whole。新增真实normal/rare成功/拒绝/Main/足迹（两输入/material/outputs、最低两needs）；earlyInvalid/mapping/noactor无MainRejected，正常domainMissingMaterial/InvalidChoice等Committed；initialRejected晚selection不升权/下一fresh正常。自然未InWorld未必通过admission，不伪造registry；若证明NoSession没前移，只沿Task16受控IsInWorld query，其余真实Standing/current/physicalowner/SQLite，并明确非自然registry路径。

至少一首成功通知encoder故障经原RecordedOutbound/NotiBus Failed隔离仍Committed/nofence，后current只Recovery→真实库存不二扣；直接拒绝ACK encoder默认：正常Refused已Committed后typed/fence，earlyRejected/ConfirmedRollback按实际precommit source-close无新增fence，保cause而非按IOException开raw。给本簇一条真实action E＋独立rollback/cleanup X连接性（同对象发生按需最小参数化）证E/X保因、真实资产恢复、无正常17结束与精确fence/close；不是用fake故障当native。真实domain初次Demand的错误owner/closed能力反例可用有界forwarder，仅将错误owner送入实际domain，其cap拒绝sticky/无普通17，原cap实现不改。核心cancel/guard/Unknown/typed/defaultWrite/两raw全量原反例保；白名单外helper/接口需要先报主控。

### 6. 可编译mutation与正式核验

候选只在ownedwhole构造input时错误把ushort Ability cast byte，原其余输入沿Content Rate0合法fixture。真实wire Ability256原规则InvalidRequest/17，pair/box/UID全部不动；错误截断0会真实消费两输入＋box、生成新UID/output并首发89B。新wire资产/帧/ledger oracle应红；旧codec用40/旧拒绝用99，不能冒称旧已覆盖256。先确认fixture与PVF/合法ability0实际可达；只变whole、编译成功且实际状态红才算，缺服务/非法metadata/timeout不是RED。不动只读domain/rules/codec/composer，finally恢复原bytes/SHA，同目标GREEN后freeze；若不可达保具体诊断向主控收敛其它有用owned输入分支，不造fake成功。

core不改，沿Task16/17已验正式编排，每轮freshprefix/目录；完整`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`须0warning/0error；App/Protocol/Networking各独立无filter full：`dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`，原class并行/5m/期限/Skip保持。必需sharedcore变化先报main修scope及full范围，不借三full默扩。

同新DFO目录三full＋Host/HttpApi/Infrastructure/Persistence四actual control-only，精确filter`FullyQualifiedName=Dfo.Server.Testing.GameplayAssertionRecorderControlTests.An_off_executor_violation_in_this_assembly_reaches_the_recorder`；原74 expected精确类/count（App62、Net7、Protocol1、其它四各1）和七程序集各唯一control，outside/exe/orphan0、exe0仅观察；新故意违规逐类/count另列不放宽原expected。Pvf无control；七control不叫七业务full。Golden15逐byte对f01dc226及本起点，Characterization10逐名pass/0skip。

以Task17三full有效同源App01/Proto01/Net02原6961逐display/status/multiplicity核原保留和真实N－D，8761＋N－D为延续总库存；另1800来源Task15 final03/四control实际数分开，不能冒称八full或Infra两skip已重验。原移除0为目标，必要rename逐项旧→新及driver目的，不能靠总数/Assert多重集。原App16skip逐身份保持，Infra2skip延续；准确原失败仅Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource及All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，仍Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException。新bind/SQL/metadata失败保日志/按实源定位，不当原基线、不放宽并行/deadline/Skip或猜peer/根治。

native按已验本机FSEvents/runner/caffeinate授权方法，保存command/cwd/HEAD/UTC起止/exit/elapsed/resources/完整console并自然结束；不TERM父runner/活跃测试，不补造exit/覆盖旧output。源变后新freeze/实际受影响正式轮，不拼不同源；before/focused/mutation/formal独立。耗时/内存仅观察，T-012最终新八full与单CMD≤2秒接受线测量仍后继。

### 7. capture、两ID闭账与交付

§1实际fresh-before与最终fresh-current均ConfigureServices-only，无provider/gameplay；family sidecar纯真实registry构造，核单logical/唯一whole/exact AvatarCompoundRequest/仅replay许可，其余family/Matrix/exits严格不动。预期总173logical/203cmd不变，一Tx fine→一Direct whole：26Protocol＋1Internal＋176Legacy、148Tx/42Direct/9ReadOnly/4Workflow；message31Protocol/142Legacy，原telemetry14同总账。逐row以实得证仅单route/fine→whole，不硬填预期或重写before时间点。

ledger.json/md只可闭`message:65635` GameAvatarCompound、`command:Dfo.Server.Protocol.Game.AvatarCompound.AvatarCompoundCommand`。填写真实原caller/Tx/条件参与者/Main/Inventory|AvatarCompound最低needs与实际slots/保护去向、whole/source/tests/Result/artifacts；state用真实state键，original/baseline/其它rows不动。仅预期pending129→128message、177→176fine，72source/5protection/8transition不变，不顺闭其它Inventory/CollectionBox额外raw/来源/过渡。

证据限`verification/logs/task-18-*`、`verification/evidence/task-18-inspection/**`；只精确owned before原bytes/BOM/行尾与新增删除清单、非owned tracked SHA，不复制整仓/旧档/bin/obj。slice现149LF/无CRLF及BOM，只读DTO/decoder/encoder/composer范围独立hash/byte对照，原bytes/行尾不整文件格式化；现probe-tools只读复用。完整source freeze/原case delta/前后真实阶段/诊断/manifest/Result R1 prefix齐；preclose先核真实artifact，追加Result后strict validate及原八坏副本全拒，不填不存在Result骗strict。

最终停共享写交fresh required独立结果快审；review只追加已freeze R1 prefix，main消费精确SHA/物理集合后本地检查点，executor不自行stage/commit。Result报告唯一whole/fine零引用、normal/rare资产/独立UID/原写序、Main/足迹/正常Refused与回滚/Ordinal/current、26B UI先库存不重delta、旧新action/native实际范围/原Throws、cap及通知/直接ACK/E+X保护、mutation恢复、10路径/实际数量与三full四control/1800延续边界。余128message/176fine/72source及5/8仍后继；Astra/Spec根封存/用户分支收尾与最终T-012不因本Task免除，不提前宣告B完成。规划阶段只有契约/详细计划，无Result/review/status。

## Result

### R1 — 2026-10-25（实跑UTC为2026-10-24），待 required fresh 独立 quick review／main 检查点

- Outcome: done（本 Task 执行结果；尚未独立批准／提交，不表示 Spec B 或 T-004 完成）。父 HEAD `c03ae3476d52fe8afaf9165047877fb7958ae046`、分支 `task/refactor-session-20261021`，精确10源、三新增；source-freeze-01 SHA-256 `541fd442c3c19bad69fcf85d2ec1ab903bf960ad94d6ab9ef7f08451b73f3ac0`。证据根 `../verification/evidence/task-18-inspection/`，所有实际命令／cwd／HEAD／UTC／exit／elapsed／resources／console 见 `../verification/logs/task-18-*`；完整清单／SHA／bytes 与 R1 prefix 身份以 `manifest-r1.json` 为准。
- Changed: logical65635/exact AvatarCompoundRequest 由唯一 `GameAvatarCompoundCommand` 接管，thin handler 同 bus 只交原 Input/Dispatch；删除旧 AvatarCompoundCommand/handler/Tx 注册及真实测试 caller，两反射名单只减该 fine。Main 的 `unit.Footprint` getter 在窄 domain try 外，直接消费原 mandatory cap；原有效分支仅一次 Main，正常领域 Refused 仍提交 Inventory|AvatarCompound 最低投影，早拒／初始 Rejected 不 Main、不因稍后 selection 升权。映射失败 ACK 登记在 mapping catch 外；普通 action E 只 RequireRollback(E)，完全退出 unit 后才 Error／17，已证 commit rollback 同样消费原 cause。原 domain／规则／codec／composer／reader／transport／shared core 没有改动，没有新 raw、NonRequest、第二 provider 或旧 fine 转发。
- **原业务／协议保持:** 原27 App case 全保同 display/status/multiplicity（纯领域结果／Throws、创建后 SQL 回滚、并发、真 PVF、权威职业等级、概率三层、89/131B、26B UI）；原4 codec case 不改。typed Compound driver 每次同 provider 的独立 TestGameplay、显式 Capture(owner,null)/current 前后、新 test footprint、真实 DomainTx Commit/Abort(E)/Dispose。真实 domain 仍先创建所有 output records／新身份，再 consume first→second→box，后 put／RecordInventorySlots／ItemUsed。新 flow 正常／稀有各实际独立 ItemUid、输入与 box 消耗、minimum 两 needs／三槽足迹、只一个原成功 ACK、无首库存附帧；原 composer 与 bytes 范围精确保留。
- **真正产品前及迁后 wire:** 在四生产路径仍 HEAD、未增测试时先运行 fresh `capture-before-01` 与 pure registry `runtime-family-before-02.json`，真实时序／源 SHA 见 `preproduct-capture-source-proof.json`，未重建或倒填。再增最小 observer/test，old-oracle02 实跑29/29并保存 `old-route-source/` 与 `old-route-freeze.json`，之后才迁产品。action probe 在两新输出已创建（Avatar rows4、旧两输入 slots2、box2）后真实 DELETE trigger 抛19/1811；native probe 的 trigger 仅实际 items.avatar_expire_date非NULL输出，真实 participant连接 FK1、receipt两输出／child2／Avatar rows2／box1／slots2均已 stage，随后 `SqliteTransaction.Commit` 真19/787。不是 fake participant、孤立 provider 或手插坏行。两路原cause引用／Error级别、ConfirmedRollback／完整17 ACK、两输入core/UID、box/gold、items全行及Avatar details、slots、events/deliveries/cooldown/effects完整回滚、无孤立输出／fence／source-close；去 trigger 后同 fingerprint、同 entry／原 Ordinal retry 只合成一次，后同 seq 只 Recovery26B→current库存、不重 delta／ItemUsed。native Freeze 后 projection 可保留，下一 admission 才清，未篡改已发生事实。
- **比较边界:** `oracle-before-after-checks.json` 核旧新原 body/hash/metadata、ACK17、真实阶段、ItemUid、数量、属性寿命／jewels／colors与其余确定字段、状态／Ordinal／回滚资产及 UI。独立执行原 `ArrivingAvatar.NewIdentity` 的 InstanceValue 不跨运行假精确相等：只 Avatar core 明确 bytes[5..9) 作关系比较，pile Count不忽略，原始core/值全留；旧新每次每份输出解析验证正数、与输入12345/54321及彼此独立，其余78B core及 Avatar details/slots逐字段相等。这是领域性质保证，不称随机身份字节相同。旧 pipeline＋handler 两个 Error 迁为 whole 一个 handled Error，原 cause／level 保持，未恢复 legacy pipeline 重复日志。
- **保护:** 实际 normal MissingMaterial／InvalidChoice／Locked／UnsupportedLifetime 正常 MainCommitted、不消费；early invalid/mapping不Main；late selection不能提升初始 Rejected，下一 fresh 可实际成功。首成功通知 encoder Failed 仍经原 NotiBus 隔离、Committed/nofence，随后 Recovery 先于当前已改为9的材料库存，不重复成功 receipt／ItemUsed。直接拒绝 ACK encoder 外抛按真实 earlyRejected／normalRefusedCommitted／ConfirmedRollback 三阶段分别原 source-close 或 typed/fence，未按IOException开新豁免。实际 domain首次 DemandActive 错owner sticky 不能映普通17；真实 action SQL E＋独立 rollback X（不同对象／同对象两次）均保两cause、资产恢复、Blocked/fence/source-close且无普通ACK。共享 cancel/typed/Unknown/current/leaf/facet/defaultWrite/ReadOnly／两B2许可原反例随本次 App full 保留，未改机制或声明全分支 native。
- **有价值 mutation:** `mutation-result.json`／`task-18-mutation-ability256-01`：只 whole 错截 ushort Ability256→byte0，编译成功后真实 wire 把两旧 Avatar 删除、box2→1、产生39088新 UID4与89B成功 ACK、ItemUsed/events增加；资产 oracle RED，另两合法0正常／稀有 case 同轮 pass。finally 精确恢复 whole 原 bytes/SHA `b9e3a962a75aa98e74157317d27987b745f18e3070240c3fe61d34d779a707be`，完全同三目标 GREEN3/3 后才 freeze01；未把编译或 fixture 错算 RED。
- **正式同源检查:** formal01 全部自然结束且10源持续匹配 freeze01：完整 Release `--no-incremental` exit0／0warning／0error；App 无filter full5012＝4994pass＋两准确原Dimensionfail＋16原skip，Protocol1770pass，Networking199pass；三full6981＝6963pass＋2fail＋16skip。两失败精确为 Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource 与 All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，仍同 Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException，不新增基线／放宽期望。Host/HttpApi/Infrastructure/Persistence另四 actual control-only各1pass。DFO原 CLI 实得74、七程序集各唯一control、outside/exe/orphan0，新故意违规0；exe0仅观察。Golden15逐byte对f01dc226与HEAD，Char10逐名pass/0skip。
- **实际case与范围:** `test-delta.json` 对 Task17 有效 App01/Proto01/Net02 原6961逐display/status/multiplicity全部保留：App N19、Proto N1、Net N0，D0／rename0。延续8781＝8761pass＋2原fail＋18skip；其中其它1800仅Task15 final03库存（含Infrastructure两skip未在本Task业务full重验），四control实际另列，不称八full／性能≤2秒完成。原并行／5m／期限／Skip不变；本轮Net199首轮全过，无新增Bind/网络失败，不声称已根治Task17旧Bind48。
- **捕获／账本／源边界:** 真 before／fresh after 及 family sidecar（仅ConfigureServices＋真实registry构造，无BuildProvider／gameplay）实得173logical／203cmd保持，只一 old fine→whole；26Protocol＋1Internal＋176Legacy、148Tx/42Direct/9ReadOnly/4Workflow，message31Protocol/142Legacy，telemetry14。其余完整family/Matrix/registration exits严格不动，唯一新binding65635/AvatarCompoundRequest，只existing replay。只闭 message:65635 与 command:Dfo.Server.Protocol.Game.AvatarCompound.AvatarCompoundCommand 两ID，其它rows等HEAD、md原前缀保持；preclose→追加本Result→strict validate＋原八坏副本全拒，证据见 ledger-preclose/ledger-strict/ledger-eight-bad。精确7 existing before原bytes/BOM/行尾、3new、非owned13430哈希库存中除两ledger外13428精确不变；不复制整仓／旧档，DTO/decoder/encoder/composer范围及原case声明byte核，stage空。
- **实际诊断，均保原始文件:** preflight首脚本把untracked Task误当tracked diff而assert，未触产品/native；family-before01在/var符号tmp出现MSB3202相对路径，保持原meta后以/private/tmp实路径family-before02成功，仍产品前。old-oracle01新Batches tuple缺Messages的CS0021/CS0411；focused01 marker泛型／缺using的CS0305/CS0246；均非行为RED。focused02新late-selection fixture重复Main容器；focused03复用旧session因原EnterWorld已推进generation被拒；分别源码定位，最终仅owned setup按当前generation用原f.Select并清旧bindings／重登同持久Avatar容器，focused04 93/93。strict01/selftest01在已有Result后仍拒绝，是Outcome标签加粗导致原literal parser未识别；只修本Result为既有字面格式，以新strict02/selftest02实得为准，旧失败日志保留，非产品或行为RED。没有为绿改生产安全守卫／共享fixture或原case。见 test-value-and-diagnostics.md 与所有轮console/meta；正式轮没有混拼不同源，旧失败未覆盖。
- **Remaining:** required fresh 独立结果快审与main精确消费后才本地checkpoint；本执行者未自批／stage／commit。pending128message／176fine／72source／5protection／8transition仍后继，legacyReplay及其他raw来源未借此闭合；T012最终新八full／单CMD≤2秒、Astra／Spec根封存与用户分支收尾仍待，不建Task19，不push／PR／merge／tag／版本／游戏／部署／cleanup。交付后停全部共享写、无active native，供fresh review追加到已freeze R1 prefix。


## Independent Quick Review R1 — TASK-18 r1／Result R1（独立审查者 b_task18_review，2026-10-25）

```yaml
kind: rks-review
verdict: approved
```

### 对象、规则与身份

本轮只审 TASK-18 revision 1／Result R1 的局部完成结果，依据 reviewing-work 的 Task Unit、Code、Deliverable 三份判据，以及当前 AGENTS、架构哲学／重构偏好、Spec B2 的本 Task 所引 RQ／AC 与 Plan P3 的 T-004、显式 Main／窄恢复及取证要求。已读 TASK-17 的独立批准与主控消费，未借用其批准替代本次审查。未另派审查者，未运行 native／build／test，未写产品、测试、账本或证据，未 stage／commit。

审查基点为 `c03ae3476d52fe8afaf9165047877fb7958ae046`，分支 `task/refactor-session-20261021`，工作区为本 Task 契约指定路径。追加前实核 R1 原文精确 33290 bytes／SHA-256 `96c54be111ec29e912650976982e975e9a26c5467e4c1fdf5a5f9c711cbc2186`，契约前缀与源冻结一致；`source-freeze-01.json` SHA `541fd442c3c19bad69fcf85d2ec1ab903bf960ad94d6ab9ef7f08451b73f3ac0` 的10源逐一匹配。`manifest-r1.json` SHA `9bd222109954742c51bda572908bf81ed38ead4745fbbd534af4b55f334876f3` 的135项逐 SHA／bytes 全匹配，加本 Task 和 manifest 恰为实际137变化路径，无遗漏／额外路径，暂存区为空。

### 决定性检查

1. **完整接管且没有旧转发。** 实读旧 HEAD、当前 slice／whole／ProtocolModule 全部产品差异：logical65635 及精确 AvatarCompoundRequest 只绑定唯一 Direct／Protocol GameAvatarCompoundCommand，消息 handler 只交原 Dispatch／Input 给同一 bus。旧 AvatarCompoundCommand／handler／Tx 注册及原真实测试 caller 删除；两个旧 marker 名单各只减该 fine。whole 自己承接原身份、地址映射、领域调用、日志、拒绝码及投影，没有 CMD→CMD、locator、sender 或新 raw／NonRequest。DTO／decoder／encoder 前段除必要 using 外、Completed／composer 后段与 HEAD 逐 byte 相等，AGENTS 只补本协议当前事实。
2. **Main、足迹和原业务顺序保持。** 仅原领域分支核 originally selected 精确会话后开一次 Main；`unit.Footprint` getter 位于业务 try 外，原 mandatory recorder 直接送原 CompoundAsync。实读未改的 domain：首次及每个写前 DemandActive、全部 output Create／新身份先于 first→second→box 消费，随后 put／足迹／ItemUsed。正常领域拒绝不 RequireRollback，仍提交 Inventory | AvatarCompound 最低 needs；前置 invalid／mapping 不 Main，初始 Rejected 不因晚 selection 升权。新 flow 真实普通／稀有成功覆盖资产、输出独立 UID、三槽足迹、原89／131B单 ACK；原27领域／Throws／SQL／PVF／概率／字节用例的断言未删弱，typed driver 每调用同 provider、新测试足迹、真实事务及 Capture(owner,null)／current 前后，异常经 Abort(E) 后原抛。
3. **异常恢复边界没有外扩。** whole 窄 try 只包 CompoundAsync；非 cancel／非 typed 不确定 E 经 RequireRollback(E)，unit 完全结束后才 Error／17；CommitRolledBack 消费原 cause。实读只读 CommandWorkUnits／CommandWorkUnit：sticky／GuardFailure 复查、真实 CommitAsync span 的 KnownStorage 同 cause／ConfirmedRollback 条件、cleanup 收口均保留；whole 没有外 catch 整个 WriteResult。新错 owner 送入实际 domain 首次 DemandActive 的反例禁止普通17，真实 action SQL E＋独立 rollback X 的同对象／不同对象两例保两次 cause、资产还原、Blocked／fence／source-close。首通知编码 Failed 仍 NotiBus 隔离、Committed／无围栏；直接拒绝 ACK 编码的 earlyRejected、normalRefusedCommitted、ConfirmedRollback 三阶段分别维持原默认保护。共享机制未改。
4. **真正产品前证据与两条完整 wire 可对照。** 实读 capture／family／old-oracle02 的原 meta／console、产品 SHA 证明及保存旧测试：fresh capture 15:46、family02 15:47、旧29／29在15:49，旧生产仍精确 HEAD；没有以迟采重建冒充前时点。旧29＝原27＋两实际 wire。action 的新 Avatar rows4／原槽2／box2 后真实 DELETE 19／1811；native 用同 provider 当前 participant 的 FK1、receipt双输出／child2／Avatar rows2／box1 已 stage 后真实 SqliteTransaction.Commit 19／787，不能混称 action 或 fake participant。独立把 before／final observations 与两轮原 console 逐对象核相等，并逐字段重算原 body／hash／metadata／完整17 ACK、真实阶段、资产／details／slots／UID／事件、tx／entry／projection、同 entry 原 Ordinal retry 及 current replay。除真实 stack、旧 pipeline＋handler 两条 Error→whole 一条 Error，以及下述随机字段外，确定字段一致；两轮各自原 cause 引用及 Error 级别保持，回滚无孤立输出／围栏／source-close。
5. **随机比较器限定在实际随机字段。** 已读 task18-oracle-compare.py 并独立重算：只 Avatar core 的 bytes[5..9) InstanceValue 在独立运行间归一，pile Count、其余78B core、明细、ItemUid／slots及其它资产仍精确比较；没有按整条 item 或全部 core 忽略差异。原 hex／数值保留；两路每次 attempt／retry 的两个真实输出均验证正数、与12345／54321输入及彼此独立，不将随机身份称作跨运行字节相等。原新 UID 与两输入独立，真实回滚和后续 retry／replay另有精确资产断言。
6. **UI／mutation 有真实状态判据。** 实读原 ReplayStateProjectionDelivery 先 Recovery 后 current 库存，及新 encoder-failure／改材料为9再重放的测试：26B UI恢复先于真实当前库存，同 seq 不再 Compound／消费／ItemUsed／成功receipt。Ability256 mutation 只改 owned whole 为 byte 截断：原 console 编译成功、三case两过一真实失败，资产从旧两 Avatar＋box2变成新39088 UID4＋box1，首89B成功 ACK；这是资产行为 RED。finally 恢复 whole SHA `b9e3a962a75aa98e74157317d27987b745f18e3070240c3fe61d34d779a707be`，同三目标 GREEN3／3后才 freeze，当前仍匹配。编译／fixture／strict01诊断未充作 RED。
7. **正式证据与数量没有夸大。** 实读完整 Release 原 console 为0警告／0错误，原 meta 为 no-incremental；三 full 无 filter、原并行／5m／期限保持。独立从 raw display／status／multiplicity Counter 比 Task17 有效 App01／Proto01／Net02，原6961全部保留，N19 App＋1 Proto＋0 Net＝20，D0／rename0；本次 App5012＝4994pass＋准确原两 Dimensionfail＋16skip、Proto1770pass、Net199pass，合6981＝6963pass＋2fail＋16skip。两失败仍精确原 Fusion／All_six 身份及 Dungeon7100／maze0／cell(1,0)／entrance Left；四其它项目实际各只 exact control1pass。独立核七 DFO 文件74条（App62＋Net7＋Proto1＋其余四各1）及七唯一control、原 expected 类/count，原 CLI outside／exe／orphan0且新故意违规0；exe0仅观察。Golden15同时逐 byte 等 HEAD／f01dc226，Char10原 console逐名通过。8781＝8761pass＋2fail＋18skip是延续库存，另1800仅 Task15 final03业务库存，不能称本次八 full。
8. **闭账和写入边界准确。** 独立比 actual capture／family：173logical／203cmd不变，只有一旧 fine→whole和 message:65635路线变化；26Protocol＋1Internal＋176Legacy、148Tx／42Direct／9ReadOnly／4Workflow，原其它 command／family／exits逐项不变，新绑定精确且只既有 replay。ledger 对 HEAD逐 row仅 command:AvatarCompoundCommand／message:65635两授权ID变化，原 baseline／其它 rows 保持；strict02 valid及原八坏副本全拒有原 meta／console，strict01误识别加粗 Outcome 的诊断仍在。非 owned13430项中除两授权ledger外13428项已独立逐SHA全相等，无隐性 shared core／fixture／协议修改。

### 判决与局限

本 Task r1／Result R1 局部 **approved**，没有发现未决 blocking defect 或必需证据缺口，可交主控消费本次精确前缀、冻结源及审查后决定本地检查点。此审查只复核既有执行证据，没有复跑测试、改变 Task／Spec 生命周期或代替主控最终验证。

产品前 family01路径、old-oracle01／focused01编译、focused02／03 fixture与strict01的原失败材料均保留，最终决定性轮次有独立源／命令／自然退出身份；没有把它们登记新基线。本次 Net199首轮过不代表 Task17旧 Bind48已根治。A21实机、T-012最终新八 full／单CMD≤2秒、剩128message／176fine／72source／5保护／8过渡、Astra及Spec／根封存／用户分支收尾仍后继，本局部批准不免除。未执行 push／PR／merge／tag／版本／游戏／部署／cleanup。

## Controller Consumption R1 — 2026-10-25

主控已完整读取 TASK-18 r1／Result R1 与 fresh b_task18_review 的 Independent Quick Review R1（approved），消费审查后42081bytes／SHA `2dccee30d6cdd5b7a38882edc6d05af95876a240adb232eb96350b5b572725a0`，没有把局部批准当 Spec 完成。已独立复核 R1 33290bytes prefix、freeze01 的10当前源、manifest SHA `9bd222109954742c51bda572908bf81ed38ead4745fbbd534af4b55f334876f3` 全135项 SHA／bytes及137物理变更集合；stage为空、父 HEAD 与分支精确。

主控从原console逐display／status／multiplicity复算原6961全部保留、N20／D0，三full6981＝6963pass＋两准确原Dimensionfail＋16skip；四其它实际control-only各1，另1800仅Task15库存，延续8781不冒称八full或性能完成。Golden15逐byte对f01dc226独立复核；只两授权ledger ID变化。已消费真正before／after wire范围、InstanceValue仅4B关系比较、实际 action／native Commit及异常保因、原UI／current replay、mutation恢复、DFO74／七control与strict原八坏副本证据。没有共享机制或raw边界变化。

授权本Task精确137路径本地检查点，按约定Co-Authored-By；不push／PR／merge／tag／版本／部署／cleanup。提交后继续P3 T-004尚余128message／176fine及后继来源／删除／最终核验，下一候选Warehouse六logical／一fine，Task未提交前不开始下一单元。Astra、T-012单CMD≤2秒及Spec／根封存仍待后继。
