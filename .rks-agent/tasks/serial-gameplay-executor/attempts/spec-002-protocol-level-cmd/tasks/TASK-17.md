# TASK-17 — Socket 四 logical 的一个完整效果协议 CMD

## Task Unit Contract

- Revision: 1（本契约与详细计划的同一修订）
- Outcome and references: 交付[Plan P3](../plan.md) T-004 下一有用单元：AvatarEmblem/AvatarSocketOpen/EquipmentEmblem/EquipmentSocketOpen四logical共用一个GameItemSocketCommand，吸收ItemSocketHandler/fine编排，删除ItemSocketCommand。消费现finitefamily/exactCLR、Task15 WriteResult、Main/显式footprint/current replay/ICommandProjections，保业务/事务/拒绝/分组扣减/UID及原帧字节。覆盖[Spec B2](../spec.md) RQ-001/003～009/012/014/015、AC-001～005/007～009及F-34/F-88/F-93/F-112；不新增VC/NonRequest/raw出口或改Socket规则。
- Owned boundary: §2精确10个生产/文档/测试路径、本Task与task-17新证据；migration-ledger.json/md仅§7五个精确ID。DTO/decoder/encoder/同slice composer、domain/规则/接口/Persistence、Task15共享core/Task16/EffectRequestTransport及其它helper、Spec/P3/契约/root/A/baseline/original/旧Task/reviews/证据只读。不扩AvatarCompound/其它Inventory/CollectionBox，不加通用协调器/构造器豁免或新机制。
- Required checks: 四logical同provider真实decoder→dispatcher→whole/Main→原domain/footprint→SQLite/ItemUsed→原通知/同outbox；earlyRejected/正常RefusedCommitted、零消耗但实际物料先验证、共享pile分组/剩余UID、ACK-first/current读取、action SQL/native Commit确认回滚后原04 ACK/同cause/entry原Ordinal重试/current replay，NotiBus普通Failed与直接ACK默认保护。纯领域Throws/原nativeFK/PVF/字节case全保；一个ownedwhole有价值可编译mutation红绿恢复。完整Release0/0＋App/Protocol/Networking三full＋其它四actual原DFO controls、74+7/Golden15/Characterization10/capture/strict原八坏副本，8731＋真实N－D库存与实际范围分开报告。
- Dependencies: TASK-16 r1/Result R1/fresh required独立approved及controller consumption，本地检查点`1dc9bfb1f4952f101f5dee8b3fecadb69e0e31a8`（parent `d7373b9dd33184ece5f419972e75c45fad48b0d8`）。消费其真实wire/current与窄前后对照、typed纯领域driver及受控查询边界的已验方法；Task15 WriteResult/Guard-KnownStorage-Unknown/带cause保因及Task11～15的Main/唯一ledger/replay/finitefamily/NonRequest/B2两许可经依赖保留。B2/P3/gates不变，Spec baseline仍`10a7218a779493494b138bd6b43cc0fc1a84aa6a`。
- Result review: required — 四logical共享fine但目标list/零扣减/材料聚合及ACK本地减量不同，误迁会丢emplacement、重复扣pile、错更新Avatar或把不确定失败映普通ACK；编译/codec/总数不能证明。fresh独立结果快审核真实source、旧新oracle/数量及五ID后交主控检查点。Task-plan仅main readiness，不另设独立审批门。

## Detailed Plan

### 1. 起点与非目标

主控先核精确HEAD、worktree `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`、branch `task/refactor-session-20261021`和唯一写者；读AGENTS、两架构偏好、B2/P3受影响要求与[Task16 R1/required review/controller consumption](TASK-16.md)。无`.codegraph/`则rg优先。中文、本地实施核验/主控检查点授权保持；不push/PR/merge/tag/版本/远端issue/游戏/df_game_r/DNF.exe/部署/cleanup，不创建Task18。必需白名单外caller/sharedcore变化或否定现中心方案时，先报主控实际source/最小依赖，不静默扩scope或改Plan。

起点8731＝8711pass＋2准确原Dimensionfail＋18skip；Task16三full App4964＋Protocol1768＋Networking199＝6931，另Host203/Http57/Infrastructure976/Persistence484/Pvf80合1800仍Task15 final03业务库存。Task16其它四只实跑原control，本轮亦不能称八full/Infra两skip已复测。capture173logical/203cmd＝24Protocol＋1Internal＋178Legacy，150Tx/40Direct/9ReadOnly/4Workflow；message26Protocol/147Legacy；pending133message/178fine/72source/5protection/8transition。

四logical精确是65737 AvatarEmblem(00C9)、65742 AvatarSocketOpen(00CE)、66425 EquipmentEmblem(0379)、66426 EquipmentSocketOpen(037A)，共用ItemSocketRequest及一个旧Transactional fine。协议字段/list/规则不变；既有四pair同一精确CLR机制足够，不拆假协议、不放宽AssignableFrom、不新增注册schema。Main只有本次selected actor，原domain决定目标/物料，不能按通知受众扩大事务参与者。

### 2. 精确白名单（生产/文档4＋测试6＝10个source路径）

以下相对worktree；只列明职责，不授整目录。新增同职责类型在指定文件内收敛。

| 路径 | 可写职责 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/ItemSockets/ItemSocketsSlice.cs` | 精确删除旧fine/命令handler，消息handler薄化为同bus/原Input/Dispatch/Protocol marker；仅必要using。WireEmblemPlacement、Request/Response、decoder/encoder及ItemSocketCompleted/composer原代码/字节只读 |
| 新`Server/src/Dfo.Server.Protocol/Game/ItemSockets/GameItemSocketCommand.cs` | 唯一whole record/handler，原强类型Input/Dispatch及ItemSocketResult，搬原前置/地址变换/窄action catch/错误码/日志/投影；不另抽公共input mapper |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 四message route切唯一whole/四exact bindings，删旧Tx fine，加Direct/Protocol；仅CommittedReplayCurrentStateBatch。原四decoder/encoder/通知接线保持 |
| `AGENTS.md` | 仅当前Socket四logical opt-in/一个fine删除与Main/原失败响应必要事实，不全篇重写或改其它责任 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/EquipmentSocketTests.cs` | 原Apply/stale fine caller改§5 typed领域driver；原业务/Throws/SQL/nativeFK/PVF/明确seq/checksum/帧原case不删弱。允许原Content/Fixture/seed最小internal可见性供新flow及补zero-default/pile UID |
| 新`Server/tests/Dfo.Server.Application.Tests/Inventory/ItemSocketDomainTestDriver.cs` | 仅具名Apply(host,owner,ItemSocketInput)同provider真实事务/current-owner/每调用新test footprint，保原纯领域结果/Throws；不是Func/CMD测试平台 |
| 新`Server/tests/Dfo.Server.Application.Tests/Inventory/ItemSocketProtocolCommandFlowTests.cs` | 四logical真实wire/Main/footprint/current、两条旧新SQL/native对照、零消耗/共享pile/失败保护；本地typed forwarding observer和actual whole wrapper，保最小before源版本 |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/EffectSettlementRegistrationTests.cs` | legacy名单仅移ItemSocketCommand，保其余marker/反例；新whole metadata另证 |
| `Server/tests/Dfo.Server.Application.Tests/World/CommonExperienceEffectTests.cs` | 同上精确移一fine，不顺清其它旧命令 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` | 四logical→同一Protocol/Direct whole/exact ItemSocketRequest/replay-only/无legacy marker、旧fine注册0；保Use/Buy/Opening/Quality/Matrix等原case |

生产fine构造唯一caller为ItemSocketsSlice:120；测试CLR caller只有EquipmentSocketTests:81/206及两marker名单。InventorySlotBoundaryArchitectureTests无此fine/handler，故只读，不机械改；没有本簇其它必需编译接口caller；LegacyEffectFootprintTests:288/297只反射domain方法/构造器，接口不变则只读。`IItemSocketDomainService.ApplyAsync`及唯一生产实现不改，ApplicationModule原注册不改。所有Socket领域/规则/实例/存储、ShopFixture/Support/RecordedOutbound、EffectRequestTransport/Task15/16 tests/core全部只读；若实施编译揭示新真实caller先报精确位置。

### 3. whole、前置与失败结果

record实现`IProtocolCommand<ItemSocketResult>`，Message＝原Input，Frame＝原Game metadata；薄handler实现`IProtocolCommandMessageHandler`并只向同bus构造/await唯一whole。whole只注入原session/online领域查询、具名slot映射、IItemSocketDomainService、ICommandWorkUnits/ICommandProjections/IGameCommandOutbox及原日志；不注入bus/sender/ledger/locator/工厂/规则/内容，不新增构造器豁免。

保原顺序：FindByConnection；Valid/session/online不满足拒04；Avatar TargetList非Avatar/Equipment拒04；按原TargetList/TargetSlot换BusinessSlot，Open的Drill从Main换，全部Emblems从Main换；仅InventorySlotMappingException转04。此映射catch内不包outbox登记（先记录前置拒绝，再外部发ACK），不能吞编码异常。初始ProtocolEffectRejected晚出现selection不得升权，早拒不Main/末Pending→Rejected。

原将调用fine的分支验证本次ProtocolEffectSelected及精确captured Session，开一次`WriteResultAsync(context,new(具名unit,[selected.Recipient],MainSettlement),action,ct)`；在业务try外取得`unit.Footprint`，直接`domain.ApplyAsync(session,input,footprint,ct)`。不提前ExpectedItemId/IsInWorld/AlreadyOpen/材料或SocketIndex校验：这些是原domain正常结果，进入后无receipt也MainCommitted/最低Inventory，绝不统一RequireRollback。自然缺材料/AlreadyOpen/错误emblem等优先作真实正常拒绝证据；自然未InWorld未必通过admission，不强造registry状态。若验证未前移NoSession，按Task16已验最小受控query仅IsInWorld=false，其余Standing/current/owner仍真实并准确标非自然路径。

只原domain调用窄try捕获非cancel/非ContainsUncertain E，保存原cause、`unit.RequireRollback(E)`并正常返回本地失败outcome。RequireRollback自身guard不能被同catch恢复；caller侧request/Main/Footprint getter/Freeze/来源/current/cleanup/outbox/Project/encoder机制动作不包进该try；domain内部既有DemandActive仍原样执行，cap/current拒绝一旦已记sticky必须优先外抛，不能借此业务catch变普通04。unit全部关闭后匹配Returned或CommitRolledBack.Cause，普通result保原result；捕获E/已证commit rollback才Error同cause并原04 ACK。Guard/Unknown/cancel/typed/cleanup/Committed/额外scope/drain仍只读机制默认，不clear sticky、不自报回滚、不改Task15共享API。

正常无receipt映Locked16/MissingMaterial11/WrongEmblem08/其它04；原信息日志主题和字段保留，handled Error观察category迁新whole，不能恢复legacy重复logging pipeline。成功只`ICommandProjections`→原TheseCharacters/ItemSocketCompleted；失败typed ACK在unit外。首通知普通Failed继续NotiBus隔离，不新raw；直接ACK编码错误默认。只有existing CommittedReplayCurrentStateBatch许可；Committed同seq不再whole/业务/ACK，只现ordinary Write reader→原current sink，不二开孔/扣材料/记ItemUsed。

### 4. 原领域足迹、零消耗与帧边界

`ItemSocketDomainService.cs:20–90`原链DemandActive→当前事务/业务facts→rules→写前Demand→OpenSockets或Embed→按物料slot GroupBy一次消费对应count并Enqueue ItemUsed→RecordInventorySlots(target＋所有验证物料slot)。接口已有必需recorder，无ApplyEffects/selfTx/共享helper→bus，whole直接传Main足迹，不造NonRequest/新chain。SQLite按实际同库连接串/存储facet动态加入，容器/生命周期与事件batch同一事务；记录实测路径条件，不把facet或fixture观察宣称所有配置固定参与者全集。

Open无Emblems且Drill非null；Embed无Drill、Emblems非空且Avatar最多3/Equipment最多2。共享pile在domain按Material分组，先验证全部再改target/扣对应分组count；两个socket同pile只有一个receipt remainder和一次ItemUsed(count2)。保单socket 0/1 alias归一/重复拒绝，以及替换不退旧emblem。

Avatar DefaultTypes非空可成功MaterialCount0：**domain仍先检查传入实际物料为pile、count足够、未锁/未过期/非mirror**，不能为“免费”跳过该验证。规则此分支原不查DrillAsync类型声明，不能额外加drill模板/Avatar类型规则。成功改socket但不consume、不Enqueue ItemUsed、receipt.Materials空；原RecordInventorySlots仍包含target和已验证物料slot，不擅自删足迹。补Content.AvatarDefaults非空的真实wire成功与缺失/锁定/过期物料拒绝；不把“无消耗”误作无Main或无projection。

composer:145–180精确recipient/IsInWorld后**原当前Read**物料与target；ACK先，再每个group物料absolute NOTI，最后target NOTI。Open ACK5B含target/drill；默认零材料时drill FFFF，之后仅target（没有伪材料NOTI）。Embed ACK每pile一组slot/absolute remaining。不能套品质的target-first、不用receipt.remaining代替原当前读取、不改其重复Read和avatar detail字节。

EffectRequestTransport:90–95已支持四shape，readonly：Open8B target/id/drill，EquipmentEmbed7B prefix＋7B/entry，AvatarEmbed额外TargetList byte；optional checksum与decoder原上限不改。AvatarOpen decoder固定Avatar list，worn Avatar Open现仅领域case，不能把request里写Equipment但body不携list说成wire覆盖；worn Avatar Embed可经原prefix进入Equipment→WornAvatar映射。四logical绑定同精确CLR，不加兼容shape或占真实logical的probe CMD。

### 5. 原测试迁移与旧新wire对照

原EquipmentSocketTests:81 Apply helper、:206保存旧owner直fine改具名typed driver：每调用同host.Services独立TestGameplay job；Capture(owner,null)/DemandCurrent前后、Begin真实DomainTransactionScope、每调用新TestEffectFootprints.Lease(owner)，直接ApplyAsync，Commit；catch E实际AbortAsync(E)原throw，finally Dispose。无静态跨job lease，不反射guard/cap、不留oldfine兼容record/production setup/任意Func平台。stale原Throws保留，不能退成domain NoSession正常值；原:167 native test本来就是直接domain+TestEffectFootprints.Run，可保持，不必为驱动统一改目的。

Task16已验EquipmentSocketTests原35case全pass，逐case/display/status/multiplicity全保。保原全部Fact/Theory/PVF/InlineData：:101 target UID/无关core/只一drill；:112两socket共pile与替换不退；:134全批次拒绝；:151单socket alias；:159真实UPDATE SQL Throws与资产回滚；:167–179 receipt后手插deferredFK再Commit Throws；:183/192原wire ACK字节/材料group/同seq不二扣；:212 carried/worn Avatar UID/染色/外观/seed及三record；:236投影过长拒绝；:269真PVF四分支实例/UID；:295过期drill；:29/259/304四layout/非法body/checksum。纯领域native case不是完整handler失败ACK/entry证据。补共享pile部分消费后的**剩余材料UID**与target UID同时不变，原case只有目标UID强断言，不伪称已覆盖。

产品前只取本簇必要两条窄before：EquipmentEmbed共pile双socket的action UPDATE SQL故障；AvatarOpen普通非default且有drill消费的真正native Commit故障。两条均原真实decoder/dispatcher/legacy fine/原handler、同fixture/services/outbox，保raw body/hash/seq/ordinal/生产SHA与最小before测试源/meta；迁后同输入/seed/oracle重跑。此是当前簇行为对照，不重开Task15共享机制gate，不声称四logical都跑过旧native；迁后四logical各成功/拒绝及zero-default另证。

action用fixture原UPDATE RAISE(ABORT)真实19/1811；native在seed后建临时deferred FK parent/child表与实际items UPDATE trigger，真实drill/目标写后child>0，实际participant ForeignKeys1、domain已成功receipt/资产stage后，SqliteTransaction.Commit真19/787/stack，完整回滚。不能用fake participant/孤立provider/手工外插坏行或普通actiontrigger冒充完整协议native。stage内FK/receipt对应target/material/child查询必须经同provider SqliteDatabase.ReadAsync/WriteAsync加入Current实际participant；ShopFixture.Scalar/Sql另开连接，只用于seed/DDL及前后持久状态，不拿它读未提交stage。所有DDL仅fixture，不改生产schema；若Avatar真实写未触预定点，保diagnostic找实际存储点报主控，不放宽声称native。

观察仅本新flow typed IItemSocketDomainService forwarding wrapper（调原实际singleton）＋actual whole wrapper/logger，读本次真实tx/request/ledger/outbox，沿Task15/16已验shape最小实现，不修改其private helper或第二BuildProvider。旧新核原04完整ACK/Error原cause引用、tx及entry ConfirmedRollback、UID/core/材料/gold与ItemUsed/events/deliveries回滚、noFence/source-close；drop故障后同fingerprint同entry原Ordinal retry只一次改装/扣group/记事实，再current replay不二ACK。native Freeze后projection可非null不强清；下一admission才清projection，当前CMD不二claim。旧pipeline＋handler双Error迁后仅whole handled Error，同cause/级别守原，不恢复pipeline。

新flow四logical各真实wire/Main/domain/SQLite/footprint/原顺序；早Invalid/mapping/noactor无MainRejected与正常domainRefusedCommitted分开，initialRejected晚selection不升权/下一fresh正常。正常成功足迹含target/各验证物料；零消耗仍足迹但无ItemUsed、无扣减/无材料NOTI。如需当前值区分，用独立真实准备job在重放前改变实际物料，再同seq验证current投影；不在同Main中额外结算或修改只读composer造测试。初次composer原Read/顺序另由原强wire与只读byte对照守住。

至少一真实首通知encoder故障经原RecordedOutbound/NotiBus变Failed仍Committed/nofence，重放current不二扣；直接04/具名拒绝ACK编码失败守默认：正常Refused已Committed时typed/fence，earlyRejected/ConfirmedRollback时按实际precommit source-close无新增fence，均保cause，不按IOException开raw。复用Task15核心反例三full实跑，并在本簇一条真实action SQL E＋独立rollback/cleanup X连接性case（含同对象可沿已验方法），证真实资产回滚、E/X保因、无正常失败ACK/precise fence与close；不用假CommitCanFail冒native。cancel/guard/Unknown/typed不映04正常成功；如需白名单外接口/helper先报主控。

### 6. 有价值mutation与正式核验

优先仅在ownedwhole构造Embed的Emblems映射处临时错误`.Take(1)`再Select，保其余输入/metadata/前置：真实EquipmentEmbed两socket共同pile原应扣2/两孔均设/remaining1；mutant实际只处理第一个合法entry、扣1/第二孔不改/ACK remainder2。实际wire的原:192及新资产/足迹/frames oracle可真实红；:112纯领域不经whole，仅作保留的原业务oracle，不能冒称被whole mutant命中。先实测fixture reachability/编译，缺服务/非法metadata/重复fixture/timeout不算RED。只变ownedwhole，不改domain/rules/codec/composer，try/finally原bytes/SHA恢复，同目标GREEN后freeze；不可达时报主控实际诊断再换有用whole输入校验/分支，不为机械改名制造RED。

core不变，复用Task14/16正式编排与工具方法，每轮新prefix/目录。完整`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`须0warning/0error；App/Protocol/Networking各独立无filter full：`dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`，原class并行/5m/期限/Skip不变。必需sharedcore改动先主控修scope及核验范围，不借三full默扩。

同新DFO目录三full＋Host/HttpApi/Infrastructure/Persistence四actual control-only，精确filter`FullyQualifiedName=Dfo.Server.Testing.GameplayAssertionRecorderControlTests.An_off_executor_violation_in_this_assembly_reaches_the_recorder`；原74 expected精确类/count（App62、Net7、Protocol1、其它四各1）、七程序集各唯一原control保持，outside/exe/orphan0，exe0仅观察；新有意违规逐类/count另列不放宽原expected。Pvf无control，七control不叫七业务full。Golden15逐byte对f01dc226及本起点；Characterization10逐名pass/0skip。

对Task16正式三full6931逐case(display/status/multiplicity)核原保持和真实N－D；8731＋N－D为全局延续账，另1800库存来源Task15 final03/四control实得分别说明，不能冒称八full或Infra两skip重验。原移除0为目标，必要rename逐项映射与业务driver目的，不以总数/Assert多重集替代。仅两原Dimension失败身份容许：Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource、All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，仍Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException；App16skip逐身份保，Infra2skip延续。新失败保诊断/按实源定位，不放宽期限/并行/Skip或新增基线。

native按已验本机FSEvents/runner/caffeinate授权方法，保存command/cwd/HEAD/UTC起止/exit/elapsed/resources/完整console并自然结束；不TERM父runner/活跃测试、不补造exit、不覆盖旧output。修源后新freeze/实际受影响正式轮不混拼，before/focused/mutation/formal分开。套件时长/内存仅观察，最终T-012单CMD≤2秒接受线测量仍后继。

### 7. capture、五ID与交付

fresh ConfigureServices-only before/after capture，不BuildProvider/启动业务；family sidecar纯构造真实两registry，核四logical同whole/四exact ItemSocketRequest bindings/仅replay许可，保全部原families/exits/Matrix。预期总173logical/203cmd不变，一Tx fine→一Direct whole：25Protocol＋1Internal＋177Legacy、149Tx/41Direct/9ReadOnly/4Workflow；message30Protocol/143Legacy，原14telemetry仍同总账。必须逐row实证只有四route/一fine→一whole变化，不硬填预期。

ledger.json/md仅可闭`message:65737` GameAvatarEmblem、`message:65742` GameAvatarSocketOpen、`message:66425` GameEquipmentEmblem、`message:66426` GameEquipmentSocketOpen、`command:Dfo.Server.Protocol.Game.ItemSockets.ItemSocketCommand`。填真实原caller/Tx/条件参与者/最低Inventory与实际slots/保护去向、whole及源码/测试/Result/artifacts，state用真实state键，original/baseline/其它rows不动。仅预期pending133→129message、178→177fine、72source/5protection/8transition不变，不顺闭其它Inventory/CollectionBox已知raw边界/来源或过渡。

证据限`verification/logs/task-17-*`和`verification/evidence/task-17-inspection/**`。精确owned beforeimages保原bytes/BOM/行尾（含任何混合），新增/删除单列，非owned tracked仅SHA；禁止整仓/旧归档/bin/obj快照。slice只读DTO/decoder/encoder/composer范围独立hash/byte对照，不全文件格式化。现probe-tools只读复用；完整source freeze/逐case delta/native阶段/诊断/manifest/Result R1 prefix齐。preclose先核真实artifact，追加Result后strict validate与原八坏副本全部拒绝，不填不存在Result骗strict。

最终停共享写交fresh required独立结果快审，review只追加已freeze R1 prefix；主控消费精确SHA/物理集合后本地检查点，executor不自行stage/commit。Result分别报告四logical/唯一whole/旧fine零引用、足迹/zero-default/grouped pile UID/当前读取/四帧、正常Committed与回滚/Ordinal/current、旧两探针与迁后实际覆盖范围/纯领域Throws/nativeFK区别、mutation恢复/通知与直接ACK/E+X保护、10路径/数量与三full四control/1800延续边界。余129message/177fine/72source及5/8仍后继，T-012新八full/测量、Astra/Spec与根封存及用户分支收尾不免除，不提前宣告B完成。规划阶段只有契约/详细计划，无Result/review/status。


## Result

- Revision: R1（Task r1；HEAD `1dc9bfb1f4952f101f5dee8b3fecadb69e0e31a8`；source-freeze-01.json SHA `0e5e822a87ef28e4759338337dcf60c8154b1de2e3e110e3ae83d375136e22f8`，10源；待fresh required独立结果快审，未stage/commit）
- Outcome: done
- Changed: 四logical65737/65742/66425/66426、原唯一精确ItemSocketRequest由一个GameItemSocketCommand完整接管，四exact bindings/Direct/Protocol；薄handler只原Input/Dispatch交同bus，ItemSocketCommand CLR/handler/Tx registration删除，无alias/legacybus/嵌套dispatch。仅4生产/文档＋6测试路径，3new/7before；domain/规则/接口/Persistence/Task15机制/所有helpers及transport只读。slice全LF/noBOM，原DTO/decoder/encoder前段与ItemSocketCompleted/composer后段byte精确相同，仅加必要using。见[workunit-map](../verification/evidence/task-17-inspection/workunit-map.md)／[source-checks](../verification/evidence/task-17-inspection/source-checks-final.json)。
- Main / footprint: 原session/Valid/online、Avatar list、目标/Drill/全部Emblem地址换算顺序保持，映射try不登记outbox；原fine分支才核captured selected actor、一次Main，getter unit.Footprint在业务try外。直接原ApplyAsync和其全部DemandActive/当前事务/原facts/规则/写前guard、RecordInventorySlots(target＋验证物料)，不造新cap/NonRequest/协调器或前移ExpectedId/IsInWorld/材料/SocketIndex。早拒无Main/PendingRejected，初始Rejected晚出现selection不升权且下一fresh可正常。正常domain无receipt仍MainCommitted/最低Inventory，Locked16/MissingMaterial11/WrongEmblem08/其它04保持。
- Failure boundary: 只原domain调用窄catch普通非cancel/非typedE，RequireRollback(E)后unit外原Error/04；来源/current/Main/Footprint getter/Freeze/cleanup/outbox/Project/encoder不被业务catch恢复。只消费已接受Returned／KnownStorage确认回滚CommitRolledBack；Guard/Unknown/typed/cancel/Committed/独立清理错误默认机制不改。受控forwarding observer把错误owner送入**真实原domain首次DemandActive**，实际cap记录sticky后原cause外抛，SQL不变、无普通04/批投影、无新fence且原source-close，证明catch不能复活请求。实际SQL E＋独立rollback X含同对象两发生保顺序/引用、真实core/material回滚、无正常ACK、Blocked及精确物理fence/close。
- Group / zero-default / current frames: 四logical真实成功/自然Locked拒绝分别实跑。Equipment两孔共享pile按组扣2、余1，一receipt／一次ItemUsed Count2，剩余pile UID及target UID不变；Avatar三孔按两pile分组，ItemUsed行数和Count精确核稳定event_type，非混算InventoryContentsChanged。AvatarDefaults非空仍先验证实际pile/count/锁期，正常4001及原规则不查drill模板的1001pile均真wire成功；不扣、不记ItemUsed、receipt.Materials空、ACK drillFFFF→仅target，但足迹仍target＋验证物料。缺失/锁定/过期pile均正常拒绝MainCommitted。composer原current Read/重复Read和ACK→group materials→target顺序/Avatar bytes不改；独立真实准备job把pile改9后同seq重放，当前snapshot9、无二ItemUsed/ACK/开孔。AvatarOpen wire固定Avatar，worn-open仍保原纯domain用途，不伪称worn-open wire覆盖。
- Before / after wire: 产品前before02实跑37/37＝原35＋两窄probe，四生产/doc精确HEAD、2最小测试源/真实meta与观察保存：EquipmentEmbed共享pile actionUPDATE19/1811；AvatarOpen普通非default真实nativeCommit19/787。actualparticipant连接FK1，native原domain已receipt/目标socket及drill stage/child1后SqliteTransaction.Commit stack，原04完整帧/Error级别/同cause、tx/entry ConfirmedRollback、UID/core/socket/material/gold/events/deliveries全回滚、同entry原Ordinal1去trigger retry只扣一次/current replay/nofence/source均证。迁后同2probe逐raw/hash/metadata/ACK/资产/projection/Ordinal/retry/replay字段精确一致；旧fine pipeline＋handler双Error移为仅whole handled Error，原cause/级别保持。只这两条旧完整wire，不称四旧native；原:167 domain手插deferredFK Commit Throws仍独立保留。见[before](../verification/evidence/task-17-inspection/before-oracle-observations.json)／[after](../verification/evidence/task-17-inspection/final-protocol-observations.json)／[compare](../verification/evidence/task-17-inspection/oracle-before-after-checks.json)。native Freeze后确认回滚projection可保留，下一admission才清，重试不延期原Ordinal、同CMD不二claim。
- Original tests / driver: 原EquipmentSocketTests35 case的全部Fact/Theory/PVF/InlineData/显示状态次数保留，纯领域Apply/stale捕获旧owner改具名typed driver，同host.Services独立TestGameplay job、Capture(owner,null)/DemandCurrent前后、真实DomainTx/每次新TestEffectFootprints.Lease/原Apply/Commit/Abort(E)/Dispose。原SQL Throws、native-domainFK、UID/无关core/部分pile/替换不退款/单socket0-1alias/三Avatar records/染色外观seed/锁期/真PVF/四layout/非法body/checksum/明确seq均保，不以新whole吞异常替旧Throws。两legacy marker列表仅移一fine；LegacyEffectFootprintTests域方法/构造器及slot架构名单不改。新增注册case核四logical同whole/4exactCLR/replay-only/无legacy/旧fine0，保全部原Use/Buy/Opening/Quality/Matrix等case，无新构造器豁免。
- Notification / direct ACK: 初次真正notification encoder失败仍NotiBus逐目标Failed正常结束Committed/nofence，后续只current不二扣；原outbox别名/helpers不改。直接拒绝ACK同IOException在earlyRejected／ConfirmedRollback保原cause并按实际precommit source-close无新增fence，在正常domainRefused已Committed时默认typed/fence；不按异常类新增raw。whole唯一许可既有CommittedReplayCurrentStateBatch，两B2许可/Buy finitefamily/NonRequest等全部原样。
- Useful RED / restoration: ownedwhole Emblems映射临时.Take(1)，合法原wire两entry仍完整，实际Equipment pile3→2/只扣1/第二孔-1，原wire192 ACK和新Equipment/Avatar真实storage/分组/足迹用例共同红，5cases2pass/3fail，非compiler/缺服务/假metadata。原纯domain112不经whole，未冒称命中。finally原bytes/SHA `64431b99f6020d03cff596bf650ba1f026a7d592dd1484cf0745e5e33b93b290`恢复；经下述精确HEAD捕获恢复后140/140＋注册15/15 GREEN，再freeze正式，见[mutation](../verification/evidence/task-17-inspection/mutation-result.json)。before行为通过不称TDD RED。
- Capture timing deviation / main disposition: 两oldwire确在产品前，但本Task漏跑当时fresh ConfigureServices-before，这是实际取证顺序遗漏。主控认可**迁后精确HEAD注册语义重建**作为对照，明确非产品前时间点fresh capture，不复制/回填Task16输出、不倒填UTC。mutation自然结束后全10current bytes/SHA快照；stage空/HEAD1dc核；只临时恢复4ownedprod/doc精确HEAD、新whole停非编译E17；具名capture-head-reconstruction-01实际ConfigureServices-only、不BuildProvider/业务/编译tests，保存真实prodSHA/cwd/HEAD/UTC/exit；finally10源逐byte恢复，后140/15 GREEN→freeze。此与真正产品前wire、后续fresh-current捕获分别呈现，供required独立review按实证判定。见[provenance](../verification/evidence/task-17-inspection/capture-reconstruction-provenance.json)／[diagnostics](../verification/evidence/task-17-inspection/diagnostics.md)。
- Capture / closure: lateHEAD语义基线与fresh current实际173logical/203cmd不变，后25Protocol＋1Internal＋177Legacy、149Tx/41Direct/9RO/4Workflow，message30Protocol/143Legacy（14telemetry）；仅4route/1fine→1whole变化。pure family registry实际构造4exact ItemSocketRequest pairs同whole，其余routes/families/bindings/全部prior exits精确不动，无provider/gameplay。只闭message65737/65742/66425/66426与精确旧ItemSocketCommand五ID，original/baseline/其它rows不动、md原bytes前缀保留；preclose过，本Result后strict/原8bad以独立json/meta实得为准。
- Final same-source verification: freeze01完整Release0warning/0error；App01 4993＝4975pass＋2准确原Dimensionfail＋16skip、Protocol01 1769pass；Networking01新fail按下项处置后，同freeze原class并行Net02 199pass及4control02各1case实际完成。三full最终6961＝6943pass＋2fail＋16skip，原Task16三full6931 display/status/multiplicity全保，N29App＋1Proto＝30、D0/rename0/merge0。另1800为Task15业务库存，Infra2skip未business重验；全局延续8761＝8741pass＋2原fail＋18原skip，不称新八full。原允许的两失败仅Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource／All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，同Dungeon7100/maze0/cell(1,0)/entranceLeft InvalidDataException，不新baseline。原DFO74 expected＋7唯一control/outside-exe-orphan0、新故意违规0（exe0仅观察），Golden15对f01dc226/HEAD逐byte、Char10逐名pass，见[formal](../verification/evidence/task-17-inspection/formal-summary.json)／[delta](../verification/evidence/task-17-inspection/test-delta.json)。
- Networking01 diagnostic / affected repeat: 首轮Net199＝198pass＋1真实新fail，父runner自然exit1、当时4control未启动，不冒首轮全绿。精确身份HandlerFailureConnectionIntegrationTests.A_handler_that_throws_inside_its_job_closes_only_its_connection_and_is_logged_once；原175全部Error唯一性断言有本host CapturedLogs额外SocketException48 Address already in use，先前wire155/161/163及167–174原Gameplay cause/SourceConnection/无重复handlerError已过。Bind/AddListen→TcpServices57/62，Port函数先释放ephemeral再Start，具体端口/抢占peer未记录，不归因某peer/不宣称根治。它不属AGENTS既有SQLite或预算cancel身份，也不混入两Dim基线；Networking/test/bind/logging路径非owned SHA不变。主控接受一次精确定向case1pass，再原并行无filterNet02 full199pass与四control02；无源/断言/期限/并行/Skip变化。原Net01 console/meta/DFO全留，最终DFO view逐assembly原文件/step/meta/目录/SHA/bytes明确App01/Proto01＋Net02/control02，剔除Net01失败记录但不删原证据，全部同freeze01、不拼不同源。见[diagnosis](../verification/evidence/task-17-inspection/networking-formal01-diagnosis.md)／[DFO provenance](../verification/evidence/task-17-inspection/final-dfo-view-provenance.json)。
- Other diagnostics / source: before01新probe remaining误期望2（36/37），whole-focused01新event总数/准备job误假设（135/140），均源证后只改新增断言，真实原ItemUsed按event_type/Count区分，原35期望不动，日志全留，不冒RED。10精确source、7owned before＋3new、2必要oldwire测试版本及主控要求10源恢复快照，非owned13274仅SHA除两授权ledger外13272不变，无整仓/旧档/bin/obj快照。每native真实command/cwd/HEAD/UTC/exit/elapsed/resources＋详细console，自然完成，无TERM/覆盖output/补造exit。R1 prefix/manifest逐SHA/bytes/物理集合核后停写。
- Remaining: 待fresh required独立快审及主控检查点，executor未自批/stage/commit/push/PR/merge/tag/版本/游戏/部署/cleanup/Task18。余129message/177fine/72source及5保护/8过渡、T012最终新八full/单CMD≤2秒测量、Astra/Spec/根封存与用户分支收尾仍pending，不宣告B完成。


## Independent Quick Review — TASK-17 r1 / Result R1 / Round 1

```yaml
kind: rks-review
verdict: approved
```

Reviewer 为 fresh independent `b_task17_review`，非本单元 planner／executor，未再委派。应用 `reviewing-work/SKILL.md` 的 Task Unit、Code、Deliverable 判据及 `verification-before-completion`；读取 `using-rks-agent` bounded-subtask 规则，不重做父路由。实际检查本 Task r1／R1、Spec B2 相关 RQ／AC、P3/T-004 与匹配 approved gates、已提交 Task16 required approved 和 controller consumption、实际 AGENTS 及两架构偏好。无 `.codegraph/`，使用 rg、定点源码、HEAD 原文及只读 Python 对照。未重跑 native／build／test，未改产品、台账或既有证据，唯一写入为本 review；批准只覆盖四 logical／一 fine 的局部结果，不批准 T-004、Spec B 或根目标完成。

### 身份与范围

- 唯一 workdir `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`；branch `task/refactor-session-20261021`，HEAD `1dc9bfb1f4952f101f5dee8b3fecadb69e0e31a8`，审前暂存区为空。Spec B2 SHA `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228`、P3 SHA `778a28e67377d099d0d0cc43739e5c3083566c5895cb3292f2b47e2625909519` 与各自 review 匹配；Task16 审批／消费已在本 HEAD。
- 下文 E17 指 `../verification/evidence/task-17-inspection/`。独立逐项核 `manifest-r1.json` SHA `5bbd83917cb513461ea3a47abca192b24f2165a0d8e60101a5388d6d6a28efcd` 的163文件 SHA／bytes；加 Task 与 manifest 自身为165个 Git／physical 变化路径，精确无额外、缺漏或 rename。`source-freeze-01.json` SHA `0e5e822a87ef28e4759338337dcf60c8154b1de2e3e110e3ae83d375136e22f8` 的10源当前全部匹配，7份 beforeimage 逐 byte 等 HEAD，另3新源。非 owned 13274项仅两授权 ledger 不同，其余13272项逐 SHA 不变；sharedcore、领域／规则／接口／Persistence、Task15/16、helpers、transport、B2/P3/gates及旧证据无旁改。`git -c core.whitespace=cr-at-eol diff --check` exit0。
- 审前 Task 全文精确等 E17 `task-result-r1-prefix.md`：34982 bytes、SHA `160518be9539788e77ef932ee9fb1c1bc107df14d152f10e8a264cd8022fb6b9`。原契约／计划 prefix 为23472 bytes、SHA `3629c02820f0c6542169c22dbbff42aa8dcdeec34654e89722bc3d9ed6ef5d84`；本 review 只追加，不改原前缀。

### 决定性局部检查

1. **四路接管完整且原字节边界保持。** 实读旧 HEAD slice、当前 whole／薄 handler 和 ProtocolModule diff。65737/65742/66425/66426 各精确 ItemSocketRequest binding 共用唯一 Direct／Protocol GameItemSocketCommand；旧 ItemSocketCommand CLR／handler／Transactional注册删除，薄 handler 只交同 bus 的原 Input／Dispatch，无旧 bus 转发、嵌套 dispatch、sender／locator 或新 raw／NonRequest。slice 的 DTO／decoder／encoder 前段及 ItemSocketCompleted／composer 后段分别逐 byte 等 HEAD，仅必要 Dispatching using 例外，全 LF／noBOM。AGENTS 只补本迁移事实。
2. **Main、足迹与正常拒绝相符。** whole 保 Valid/session/online→Avatar list→target/drill/全部 emblem 地址转换，只有 mapping 异常转04；原 fine 分支才检查 originally selected 精确 session 并开一次 Main。unit.Footprint getter 在业务 try 外，原 domain 直接消费它；实读 domain 的首次／写前 DemandActive、材料按 BusinessSlot 分组、原规则、消费与 ItemUsed 及 target＋所有验证材料足迹，全部未改。正常 Locked/MissingMaterial/WrongEmblem/其它仍16/11/08/04，未前移成早拒或统一 RequireRollback。initial Rejected 晚出现 selection 不升权，下一 fresh 请求可正常。
3. **异常恢复未扩大。** 窄 try 只包原 ApplyAsync；普通非cancel／非typed E 保存后 RequireRollback(E)，单元完全结束后才原 Error／04。whole 不包 Main／Footprint getter／Freeze／current／cleanup／Project／outbox／encoder。实读只读 runner 的 sticky／GuardFailure、KnownStorage／同cause／ConfirmedRollback 窄 Commit span 与原 footprint Request.Failed；实际 BadOwner 送进原 domain 首次 DemandActive 的反例要求原 cause 外抛、无正常 ACK、无批投影及原 source-close。真实 action SQL E＋独立 rollback X 的不同及同对象两例要求双发生保因、真实资产回滚、Blocked／精确 fence／close，未改共享保护。
4. **业务与投影有强 oracle。** 四真实 wire 形状各成功及自然 Locked 拒绝；Equipment 双 socket 同 pile 消费2／余1、一 ItemUsed Count2，目标与余 pile UID 保持；Avatar 三孔按两 pile 分组验证事件行数与 Count。AvatarDefaults 的4001及原规则不查 drill 模板的1001真实 pile 均成功，不扣、不记 ItemUsed、receipt.Materials 空，但仍先验证 pile/count/锁/期限、保材料足迹，ACK drillFFFF 后仅 target。missing／locked／expired pile 真实拒绝仍 Committed。原 composer current Read／重复 Read、ACK→group absolute materials→target 顺序逐 byte未动；独立准备 job 改 pile 为9后同seq重放读取9，无二 ACK／消费／ItemUsed。AvatarOpen wire 固定 Avatar，wornOpen 只保原 domain 用途，没有虚称 wire 覆盖。
5. **两条真实旧新回滚对照成立。** 独立将 `before-oracle-observations.json`／`final-protocol-observations.json` 与 before02／正式 App01 raw console 逐对象核相等，再对照两轮 metadata/body/hash/完整ACK、资产／UID／事件、tx／entry／projection、原 Ordinal／retry／current replay。除有意消除 legacy pipeline 重复 Error 的 logs 与各自真实 stack 外，对照字段相等。EquipmentEmbed action UPDATE 真19/1811；AvatarOpen native 在实际 participant FK1、receipt／材料1／目标 socket已改／child1之后真 SqliteTransaction.Commit19/787，ConfirmedRollback 后原04、同cause／Error，无 fence／source-close，同 entry 原 Ordinal retry一次后 current replay。不是 fake fallible、孤立 provider 或原 pure-domain 手插 FK case 冒充完整协议。真正产品前37/37＝原35＋两探针，旧生产 SHA 与 HEAD 匹配；只这两旧完整 wire，不宣称四旧 native。
6. **测试目的与有价值 RED 保持。** 原 EquipmentSocketTests diff 仅必要 fixture 可见性／configure 与 Apply／stale caller 换具名 typed driver；原声明／InlineData／强断言未删弱。driver 同 host.Services、Capture(owner,null)／Demand前后、真实 DomainTx／每调用新 test footprint／Commit／Abort(E)／Dispose，原 SQL/native-domainFK Throws、PVF、UID、alias、checksum／seq等目的仍在。mutation raw 为完整合法 wire 上 owned whole 的 Take(1)：5case中2pass/3真实红，命中原 Embed ACK 及新 Equipment／Avatar；实际 Equipment pile3→2、第二孔仍-1。未把不经 whole 的 pure-domain112冒称红。whole原 SHA `64431b99f6020d03cff596bf650ba1f026a7d592dd1484cf0745e5e33b93b290`已恢复，恢复140/140及注册15/15后才 freeze。首通知 encoder Failed 的 Committed/no-fence 与直接 ACK early／Committed／ConfirmedRollback 默认保护分开验证。
7. **正式取证、数量和闭账可追溯。** 实读 Release原console为0警告／0错误；独立从 raw display/status/multiplicity Counter 得 App4993＝4975pass＋准确两原Dimensionfail＋16skip、Proto1769pass、Net02 199pass，原 Task16三full6931全部保留，N29App＋1Proto＝30、D0／rename0。三full6961＝6943＋2＋16；四其它项目各只实际 exact control1pass，另1800仅Task15库存，Infra两skip未business重验。DFO derived view七文件逐 byte／SHA等各原producer记录，独立按 frames核原74（App62/Net7/Proto1/其它四各1）、七唯一control及原expected类/count精确，无未知类；outside/exe/orphan0，exe0仅观察。Golden15逐 byte同时等HEAD/f01dc226；Char10正式逐名pass。capture实际173logical/203cmd逐row仅四message与一fine→一whole变化，其余runtime command行完全相等，四exact bindings及prior family/exits保留。ledger对HEAD独立逐row仅五授权ID pending→closed，每行original及其余row／顶层baseline不动，md原bytes前缀保持，strict valid／八坏副本拒绝均有原日志。

### 两项取证偏差的独立裁定

- **漏跑产品前 ConfigureServices capture：接受有界修复，保留时间局限。** 原计划所写 fresh-before 当时未执行，不能将迟采称作产品前时点。实际 reconstruction 脚本／provenance、原 capture meta/console 与只读 exporter 表明：mutation自然结束后保全10个current bytes，只有4 owned生产/文档暂回精确HEAD，新whole停在非编译E17；实际 ConfigureServices-only（无BuildProvider／业务／test编译）迟采保存真实UTC与SHA，finally10源恢复，之后140/15 GREEN及freeze。当前10源与该恢复快照一致；语义baseline能证明原注册内容，真正产品前两wire不受影响。主控已明确授权此处置；R1及证据没有复制Task16、倒填UTC或隐藏遗漏。此缺口不再阻碍本局部注册语义对照，但原时间点的fresh capture从未存在。
- **Networking01 新 bind48失败：接受同freeze受影响复验，不登记新基线。** raw证实199＝198pass＋1fail，175全部Error唯一性遇本host CapturedLogs额外 SocketException48 Address already in use／Bind→AddListen→TcpServices57/62；先前 wire及167–174 Gameplay cause／source／无重复handlerError断言已过。实读 Port()确先释放ephemeral后才Start，有间隔，但数值端口及竞争peer未记录，不能断言具体peer或根治。它不属既有SQLite／预算cancel或两Dim身份，Net01父runner自然exit1、四controls未启动，原console/meta/DFO均保留。主控授权的exact case1pass后，原并行无filter Net02 full199pass＋四control02均在同freeze、无源／原断言／期限／skip改变。最终view明确App01/Proto01＋Net02/control02各producer/meta/目录/原file/SHA，非不同源拼接；批准没有把首次失败改写成首轮全绿。

### 判决与局限

本 Task r1／R1 **approved**，没有未决局部 blocking defect，可交 controller 消费精确冻源与本 review 后决定本地检查点。reviewer仅独立复核既有执行证据，未重跑 native／测试、未推进 Task/Spec 生命周期。原before01新探针期望与focused01新断言误假设的诊断保留，不冒称TDD RED。

未验证A21实机、最终单CMD≤2秒或T-012最终新八full；8761＝8741pass＋2原fail＋18skip为延续库存，不能称本轮八full。其余129message／177fine／72source及5保护／8过渡、Astra、Spec／根封存、用户分支收尾仍后继，不由本四logical局部批准免除。没有push／PR／merge／tag／版本／游戏／部署／cleanup授权扩张。


## Controller Consumption — TASK-17 r1 / Result R1

主控已完整消费Task/Result与fresh independent Round1 approved及两项偏差的独立裁定，读取原slice/domain与新whole、原错误console/实际取证provenance。已独立核10个freeze01源SHA、163项manifest SHA/bytes、165个精确物理/Git变化与34982字节R1 prefix，23472字节契约prefix保持3629c02820f0c6542169c22dbbff42aa8dcdeec34654e89722bc3d9ed6ef5d84。manifest5bbd83917cb513461ea3a47abca192b24f2165a0d8e60101a5388d6d6a28efcd，sourcefreeze0e5e822a87ef28e4759338337dcf60c8154b1de2e3e110e3ae83d375136e22f8，暂存区空。

主控独立Counter核同一freeze的App01/Proto01/Net02：原Task16三full6931全部display/status/multiplicity保留，N29App＋1Proto＝30/D0；6961＝6943pass＋准确原2fail＋16skip，四control02实际各1。8761＝8741pass＋2原fail＋18skip是延续库存，另1800只Task15业务库存，Infra两skip未重验；不称八full。Release0/0、DFO74/7、Golden15、Char10、旧新两个完整wire/当前重放/typed保护与五IDstrict8bad均有匹配证据。Take1 mutation真实漏第二孔/少扣材料，恢复后才冻结；原35纯domain/Throws目的保留。

明确消费两项偏差而非隐藏：产品前oldwire37pass是真实前时点，注册fresh-before当时遗漏；晚采重建production三原文件逐SHA等HEAD、新whole在HEAD不存在，10源恢复后现SHA匹配，捕获真实UTC/ConfigureServices-only，证明原注册语义但不补造原时间点记录。Net01新额外本hostBind48及失败原文已取证，不归原两Dim基线/不归因未知peer/不称根治；准确case定向绿后同freeze原并行Net02全量与四control02有效。主控独立逐文件核七份finalDFO view与原producer记录bytes/SHA完全相同、各自meta/目录来源明确；只替换同源受影响失败轮，原Net01不删除，不拼不同源。

主控逐row确认只四message/一个fine共五授权ID变化，original/baseline/其它行不动。局部结果支持Task17本地checkpoint；129message/177fine/72source及5保护/8过渡、T012最终核验/性能/Astra/Spec根封存仍后继。不push/PR/merge/tag/版本/游戏/部署/清理。
