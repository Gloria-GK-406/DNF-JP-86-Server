# TASK-14 — MagicSeal／Forging／Outworld 五消息的三个完整装备效果 CMD

## Task Unit Contract

- Revision: 1（本契约与详细计划的同一修订）
- Outcome and references: 交付[Plan P2](../plan.md) T-004 的下一装备效果簇：MagicSeal（unseal/change）、Forging、Outworld（purify/invest，其中invest含twist）分别一个whole CMD，覆盖五个既有logical消息，吸收原handler/fine编排并删除三个fine。仅消费已接受Main、必需Footprint、committed replay与ICommandProjections，保原业务/事务/当前投影/拒绝差异/帧序与字节。对应[Spec B2](../spec.md) RQ-001/003～009/012/014/015、AC-001～005/007～009及F-34/F-88/F-93/F-112；不宣称其余物品、来源或过渡清零。
- Owned boundary: 下文精确生产/测试/AGENTS、本Task与task-14新证据；migration-ledger两文件只准五message/三fine精确ID。共享core、领域/规则/接口/存储/DTO/codec/composer、Spec/P2/契约/root/A/baseline/original/旧Tasks/reviews/证据只读。不扩package opening、quality/socket、warehouse、普通inventory或Mailbox/Rental，不新增raw出口、NonRequest来源、Compose、延迟编码、公共装备协调器或注入豁免。
- Required checks: 五logical真实decoder→dispatcher→同bus/Main→实际Preview/ApplyEffects→SQLite→原通知/同outbox；早拒绝Rejected与domain正常RefusedCommitted、Forging失败roll仍提交、真SQL回滚同entry/原Ordinal重试与current replay不二结算、三类原帧序、unseal静默拒绝、NotiBus普通Failed隔离及直接ACK默认保护。一个ownedwhole有价值可编译mutation红绿并恢复；完整Release build0/0＋App/Protocol/Networking三full＋其它四actual DFO controls、原74+7/Golden15/Characterization10/capture/strict原八坏副本。8619＋真实N－D为全局延续库存，实际执行范围分开报告。
- Dependencies: TASK-13 r1/Result R1/required独立approved与主控本地检查点`de65601cc9fa5fa15db6afae59fc589265717cc8`（parent `247a63451c19c6f7622aea1b58a5a66d3e38ba8e`）；消费其已验whole形状、同provider实际handler观察方法与FreshSequence helper，以及Task11/12经该依赖保留的Main/唯一ledger/显式recorder/replay/finite family/NonRequest。B2/P2及匹配gates不变，Spec baseline仍`10a7218a779493494b138bd6b43cc0fc1a84aa6a`。
- Result review: required — 五消息共享三个fine，分支/结算状态迁错会误扣金币或材料、重复roll/改装备、错误重放或发出客户端未处理的unseal ACK；编译与codec不能证明这些事务和顺序。独立结果快审核实际source、原oracle、同版本数量/证据及闭账后交主控检查点。Task-plan由主控readiness安排executor，不增独立审批门。

## Detailed Plan

### 1. 起点与实施边界

由主控确认上述精确HEAD、原worktree `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`、`task/refactor-session-20261021`和唯一写者；读AGENTS、ArchitecturePhilosophy、RefactorPreferences、B2/P2受影响要求、[Task13 R1及独立review](TASK-13.md)。现无`.codegraph/`，rg优先。中文、本地实施核验与主控检查点授权保持；不push/PR/merge/远端issue/部署/cleanup/改版本，不运行df_game_r/DNF.exe或起游戏，不创建Task15。

Task13已验18源freeze `76719aca16309a6eb7379877b100ceba1df5d71ba687ef0f620ef88345ea025c`；其三full加四actualcontrol边界和原诊断作为已接受输入，不复制归档。当前全局延续库存8619＝8599pass＋2准确原Dimensionfail＋18skip；三full起点App4862＋Protocol1765＋Networking199＝6826，其余1793仍是Task12库存而非Task13重验。Task14从此逐case核真实N－D。

共同业务已闭合：`EquipmentMagicSealDomainService.cs:36,68–72`、`EquipmentForgingDomainService.cs:26,75–79`、`EquipmentOutworldDomainService.cs:28,77–80`先Demand本次owner，再原plan→Preview→ApplyEffects。MagicSeal plan为SpendGold＋SetMagicSeal，Forging为Consume＋仅成功roll时SetForging，Outworld为Consume＋SetDimensional（`EquipmentEffectPlanRuleService.cs:45–74`）。这些服务、plan、effect handlers和持久化不改；不能按效果名字臆造Preview前写。它们没有共享helper→bus依赖，三个旧fine的唯一生产构造caller就是各自handler。

精简preflight按Task13已验操作：精确owned beforeimages保存原bytes/BOM/行尾/混合byte，非owned tracked只SHA清单，新增/删除单列；禁止复制整仓、旧归档、bin/obj或几千份before。每次probe用新输出路径，勿以--output覆盖原证据。若发现必须改sharedcore/领域/接口、扩大raw许可或改变中心方案，先向主控给实际source/最小依赖，不能默扩本Task；现已读源未发现该依赖。

### 2. 精确生产／文档白名单

所有路径相对worktree；下表`Game/`指`Server/src/Dfo.Server.Protocol/Game/`，职责不能扩成整目录授权。

| 路径 | 可写职责 |
|---|---|
| `Game/MagicSeal/MagicSealHandler.cs`、`Game/Forging/ForgingHandler.cs`、`Game/Outworld/OutworldHandler.cs` | 三薄handler只注入ICommandBus（可保ILogger），原Input/Dispatch构造唯一whole并await，实现相应IProtocolCommandMessageHandler；不剩会话/业务/映射/登记编排 |
| `Game/MagicSeal/MagicSealCommand.cs`、`Game/Forging/ForgingCommand.cs`、`Game/Outworld/OutworldCommand.cs` | 删除三fine及各自handler；旧CLR/FQN/注册/兼容转发全零，不留Obsolete或newCMD→legacybus |
| 新`Game/MagicSeal/GameMagicSealCommand.cs`、`Game/Forging/GameForgingCommand.cs`、`Game/Outworld/GameOutworldCommand.cs` | 各一whole record/handler，原强类型wire Input＋Dispatch，结果依次既有EquipmentMagicSealResult/EquipmentForgingResult/EquipmentOutworldResult；原ErrorCode/Reject与纯地址换算在各自此文件搬移，不新增跨切片能力/映射helper文件 |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 五old message route切到三个whole；删三Tx fine，加三Direct/Protocol注册。MagicSeal的两pair同MagicSealRequest，Outworld的两pair同OutworldRequest，Forging一pair；每whole仅现有CommittedReplayCurrentStateBatch许可，原decoder/encoder/notification注册保持 |
| `AGENTS.md` | 仅同步本簇已opt-in、三个fine删除、Main/原通知与拒绝差异的必要当前事实；不全篇改写、不指向过程稿、不改其它规则/未完成责任 |

**只读骨干**：三切片全部`*Messages.cs`、decoder/encoder、`MagicSealCompleted.cs`/`ForgingCompleted.cs`/`OutworldCompleted.cs`；Application三domain、plan/规则/Preview/Apply/effect handlers；Data/Abstractions/Persistence/Infrastructure；Commands/Dispatching/Transactions/ExplicitEffectRequest/Footprint/NonRequest/ledger/attempt/outbox/reader/sink/EventService/NotificationBus/RecordedOutbound。原SlotBoundary判据没有这三fine的typeof，故也只读，不机械添加改动。

可注入仍限既有领域服务、具名slot换算能力、ICommandWorkUnits、IGameCommandOutbox、ICommandProjections和原日志。whole不注入bus/sender/safety/ledger/执行器/locator/工厂/内容或其它规则。三输入均Effect，**不用Task12 NonRequest声明**；不改其ordinary守护，也不复活旧LegacyFootprint/Lifecycle能力。

### 3. 三whole共用的执行形状与事务事实

各record实现`IProtocolCommand<原结果>`，Message为原Input，Frame为原Game metadata；MagicSeal/Outworld各多个logical属于同一whole，这符合Spec RQ-001及现pair机制，无需改schema、CLR/消息目录或拆假协议。每logical仍一精确允许CLR、双向route闭合，不能用AssignableFrom放行未知shape。测试不为这些真实logical注册第二个probe whole，保Task12 Matrix隔离。

参照已接受GameEnchantByBeadCommand的显式方式取得`context.Request`，按§4原前置次序查询本连接session/online及换算；初始ProtocolEffectRejected即使handler执行前晚出现新session，也只能原拒绝，不升级本次权威。只有原将调用fine的分支验证ProtocolEffectSelected及精确captured session后，开一次`WriteAsync(context,new(具名unit,[selected.Recipient],MainSettlement),...)`，直接调用原domain并传`unit.Footprint`。一个actor保持原参与者范围，不因账号或投影可见性加其它selected。普通facts、合法commit callback不另加recorder活性限制。

原三fine注册Transactional（当前ProtocolModule:670–672/685–690），每个有效调用对应一个新Main Write，不合并提交/不提前投影。SQLite参与者按实际连接串动态Enlist（`SqliteDatabase.cs:100–103`）；MagicSeal实际效果需金币/装备、其余需材料/装备的原存储面在该边界中，不能把每个面称独立CommitCanFail参与者，或把fixture观察当所有配置恒定全集。记录实际测试路径的事务参与者/条件、Preview不记录、Apply记录/Freeze/Settled和原回滚事实即可，不重做事务机制。

前置无fine分支不claim Main，Pending结束Rejected、无projection/HasCommittedUnit；进入domain后的正常无receipt原本fine正常返回并提交，仍Main Committed/最低Inventory（可无TouchedSlots），**不一概RequireRollback/Rejected**。Forging roll失败另属有receipt的成功提交，不能和业务拒绝混同。真实SQL/取消/异常按原外抛，runner实际确认回滚后才同fingerprint重试；不吞异常/clear sticky/新增业务catch。原测试名含Failed_commit不证明故障发生在Commit阶段：当前SQL trigger是实际语句失败，Result按实测阶段描述。

成功unit结束后只`ICommandProjections.ProjectAsync`原TheseCharacters和原Completed。NotiBus普通Compose/encoder失败仍逐目标Failed隔离、whole正常结束；无receipt时依§4原typed ACK/静默决定，直接ACK外抛仍默认保护。只声明existing committedReplay出口，无initial raw许可；取消/typed/unknown/额外cleanup保现默认全participant保护。既有Use两B2出口、Buy双shape/NonRequest、Task13全部guard不改。

Committed同序号由现ordinary Write reader→事务外sink当前投影，不回whole、不二次roll/扣gold或材料/改equipment，也不重成功或拒绝ACK。ConfirmedRollback才可重试同entry、原Ordinal不更新；早Rejected重复fingerprint拒绝行为不改。不能因旧测试名写着“replay same error”而重发原ACK或延长窗口。

### 4. 原分支及帧序逐项保留

**MagicSeal**（Handler:26–68）：先session；再TargetSlot非负、MaterialSlot必须change时0/unseal时－1、online；再Main槽位映射，原InventorySlotMappingException转拒绝。传domain的Material始终原`null`，不能把reserved0当实际材料槽、或unseal－1当免费物料。有效change直接ChangeAsync(session,target,null,OptionIndex,unit.Footprint)，unseal直接UnsealAsync(session,target,null,unit.Footprint)。所有前置或domain正常拒绝：unseal原无ACK；change发ChangeRandomOptionResponse(false,code)，默认0x13，InsufficientGold0x0A、MaterialUnavailable0x11、Locked0xD6、UnsupportedContent0x17。成功composer原target当前slot→当前gold→仅change成功ACK；保unseal没有未知ACK、Change option三byte及reload/当前gold读法，不从receipt替代当前读取。

**Forging**（Handler:26–74）：先无session0x04；再Variant非0一律0x0D（原ticket1/2不走普通PVF路径）；再TargetList Main/Equipment、target/material非负、TargetItemId正、ReportedNameBytes长度1～128、online；随后target与Main material映射，catch仍0x04。直接ForgeAsync(session,target,TargetItemId,material,unit.Footprint)，reported name只保原协议形状，不加身份/名字规则。原domain拒绝码Durability0x07、UnsupportedContent0x0D、NotWeapon0x13、MaterialUnavailable0x16、MaxLevel0x5F、Locked0xD5、其它0x04。正常roll失败仍Receipt/主提交/材料消耗，ACK Success=true且ForgeSucceeded=false，旧/新level和remaining保持。成功通知ACK→当前target→当前material，不能套MagicSeal排序。

**Outworld**（Handler:26–81）：先session；再target/material非负且不同、两ExpectedItemId正、online；再非purify只容WireAction0/1，action2等早拒绝0x08不Main；随后两Main映射catch拒绝。Purify→Purify；invest/action0→Invest；invest/action1→Twist，SelectedOption及两expectedID原样传ApplyAsync(...unit.Footprint)。purify所有拒绝仍PurifyItemResponse(false)原四byte；invest保WireAction及UnsupportedContent0x08、SameOption0x17、AlreadyUpgraded0x12、AlreadyHasAmplifyOption0x14、NoAmplifyOption0x15、其它0x11。成功ACK→target→material，composer原精确recipient/IsInWorld、material当前remaining与target缺失时receipt回落不改。action2动态尾decoder接受范围不在本迁移收紧。

### 5. 测试与编译caller精确白名单

以下相对`Server/tests`；保持原case/InlineData/序号/SQL/bytes/状态/期限与全部业务断言，仅真实驱动/观察位置必要迁移单列。

| 路径 | 允许动作及必须保留 |
|---|---|
| `Dfo.Server.Application.Tests/Inventory/MagicSealHandlerTests.cs` | 原:23 reserved0；32 unseal提交后无ACK；47 change slot/gold/ACK、option bytes和SQLite reload；96domain拒绝；108/121不二扣与当前gold；135非法sentinel静默；149真实SQL回滚retry。已真实EffectRequestTransport，无manuallease待修；可在本文件最小调整private fixture可见性供本簇新flow，保原内容/seed规则 |
| `.../Inventory/EquipmentForgingHandlerTests.cs` | 保17提交先ACK及replay、42真实失败roll材料消耗/有receipt、62两个variant早拒绝与重复seq异常、82真实SQL语句回滚retry；可同职责补本簇状态和fixture可见性，不能把roll失败改正常拒绝或弱化remaining |
| `.../Inventory/EquipmentOutworldHandlerTests.cs` | 保18purify提交/同seq、42invest/twist动作、64action2拒绝、82same-option只一次错误ACK后currentreplay、103真SQLretry、126逐状态错误码/资产、153/174强化书/无durability饰品、204坏content日志/无ACK/资产/可再次尝试；唯一日志观察迁移见下段 |
| 新`.../Inventory/EquipmentOptionProtocolCommandFlowTests.cs`（可选；同职责case可放上述三file） | 仅本簇五logical的sameprovider真实入站、actualhandler/显式request观察、早Rejected vs domainCommitted/footprint、SQL同entry/Ordinal/currentreplay及通知失败最小反例；复用ShopFixture/原内容和Task13实际handler wrapper方法，不新第二provider/发件箱/生产testsetupCMD，不给actuallogical加probe注册 |
| `.../Sessions/EffectSettlementRegistrationTests.cs`、`.../World/CommonExperienceEffectTests.cs` | 原legacy marker名单精确移除三fine，保其余名单/request markers和反例；另证三个whole Protocol/Direct、各2/1/2精确bindings、只replay许可、不实现legacy settlement marker及真实Main |
| `Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` | 原生产全图新增本簇五logical→三whole闭合断言、三fine注册0、perlogical唯一精确shape、三whole仅replay；保Task13原三个单binding/Use两出口/Buy双shape/Matrix和全部原case |

**明确只读的测试依赖**：`EffectRequestTransport.cs`已有这三shape真实Body/decoder/dispatcher且Task13 FreshSequence已接受，无需改；`ShopFixture`、RecordedOutbound/Support、Task13 flow/slot architecture、core/注册机制测试只读。`MagicSealWireTests.cs`、`EquipmentForgingWireTests.cs`、`EquipmentOutworldWireTests.cs`及三domain/规则/MagicSealGrade/EquipmentEffectExecution等原oracle在full实跑，不为迁移改变codec/composer测试。全Server检索三fine除了各原handler/文件/Module及上述两marker名单没有其它构造/typeof；ForgingHandler.ErrorCode无外部caller，无需兼容壳。若新编译事实揭示白名单外真实caller，先向主控报精确位置。

Outworld:204–233原坏content case捕获`ILogger<CommandLoggingBehavior<EquipmentOutworldResult>>`；新bus直接invoke whole，不执行legacy pipeline。只在此owned测试把观察点换成同provider `ILogger<GameplayExecutor>`（source构造:16，失败:278–318以原failure记录Error再MarkJobFailure），保至少Error及Exception同原cause引用、`[durability]`、无ACK、材料/core不变和同seq可再次抛。这同时落实RQ-014唯一执行器Error，不删日志断言、不恢复旧pipeline、不在whole新增重复Error。其余logger/content诊断原级别保持。

新增driver调用必须在TestGameplay外Send；setup/reload仍各自真实准备job，不把新bus放进现job等待。原MagicSeal reload:78–84的TestGameplay仅清容器/Select/legacy snapshot观察，仍被其它旧route消费，不借本簇顺迁该readonly帮助器或删除跨域旧接口。新fresh请求可直接用既有FreshSequence，明确seq/raw重复case不改；Send仍Observe一次，不以Empty metadata/inline测试信任绕admission。

### 6. 实施顺序与有价值的反例

先精确before/capture与原逐case库存，落能区分新route/错误迁移的新case，再搬三个whole/注册/删fine/改名单和必要日志观察；定向绿且mutation恢复后freeze正式。机械编译错误、空trace（legacy pipeline不执行）、非法metadata、自等或fixture重复选择不能当业务RED；沿Task13真实actualhandler wrapper方法避免其已披露误用。

- 五logical各至少原有一条真实成功路径，证明同provider rawdecoder→dispatcher→whole/Main→domain Apply/SQLite→projections→原NotiBus/同outbox，不用仅new whole或替身domain返回receipt冒充。可跨现有强case和少量新flow组成证据，无需五套镜像平台。
- 三whole分别对照早拒绝不Main/Rejected/无projection和domain普通无receipt正常MainCommitted/最低Inventory/无改资产；MagicSeal两种静默/ACK必须分别验证，Forging失败roll另证有receipt/材料真实扣减/Committed；初始无selected晚出现选择仍不能升级本次，后续fresh正常。不把Assert同错误码当事务状态证据。
- 扩原实际SQL trigger case（或新flow用同原触发点）观察ConfirmedRollback/无projection/无success、真实gold/材料/core全回滚、同fingerprint同entry及Ordinal retryCommitted，再重放业务次数不增。原触发点足够时不新增镜像fake CommitCanFail；最终实测才描述具体参与者/commit状态。
- 保五logical当前重放不重ACK/roll/扣资产；三composer输出顺序逐分支守原byte/类型。至少一真实首通知encoder故障经原NotiBus Failed隔离后仍Committed/currentreplay，不能误触新raw。直接change/Forging/invest拒绝ACK的实际encoder外抛仍按既有默认typed/fence；unseal静默分支不可为测试强造ACK。复用原RecordedOutbound实际encoder注错与alias，不改wrapper/core。

至少一个可编译meaningful mutation，优先仅删owned `GameMagicSealCommand` 的reserved/sentinel条件（保其它前置）：真实合法change wire但第二槽非0原应早拒绝、gold100000/core不变/无Main；缺guard则原domain收到原null material会真实改变装备并扣200，原`Change_requires_the_A21_reserved_zero_without_consuming_assets`及entry断言可红。unseal非－1原静默且不花500同样是有效对照。保新合法reserved0成功正例，不靠编译/fake异常/文字匹配。变异只动ownedwhole，不改只读composer/领域/规则；原bytes/SHA、try/finally恢复、同目标GREEN后才final freeze，记录实得而非预言RED。

### 7. 核验范围、数量与证据

复用Task13 §7/§8已接受正式编排和工具方法，Task14各轮新prefix/目录。未改sharedcore/接口，所以完整`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`要求0warning/0error，App/Protocol/Networking各独立无filter full：`dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`，原xUnit类并行/5m/Skip/期限不变；保存cwd/HEAD/command/UTC/exit/elapsed/resources及完整console。step自然结束，不为省后继control杀父runner/活跃测试、补造exit或覆盖失败轮；修源后新freeze/实际受影响正式轮不混拼。必要本机FSEvents/runner/caffeinate按已验授权方法。确需sharedcore更改先报主控修scope及full范围，不借三full静默改core。

同一新DFO目录收三full＋Host/HttpApi/Infrastructure/Persistence四actual control-only，filter精确`FullyQualifiedName=Dfo.Server.Testing.GameplayAssertionRecorderControlTests.An_off_executor_violation_in_this_assembly_reaches_the_recorder`。原74 expected精确类/数（App62、Net7、Protocol1、其它四各1）及七程序集各唯一control保持，outside/exe/orphan0；exe0只观察，不夸全路径穷尽。新增故意违规须逐class/count实证另列，不放宽旧expected；Pvf无control。本轮不是八full，其余业务库存不等于control重验。Golden15逐byte对f01dc226及本Task产品起点，Net Characterization10逐名pass/0skip，三codec/帧序原assert全保。

对Task13正式01三项目逐case(display/status/multiplicity)核原6826全部保持及真实N－D，原移除0为目标；必要rename逐项旧→新映射、driver/日志观察目的说明，不以总数/Assert多重集相似代替。全局8619＋N－D是延续账，报告三full和四controls各实得数、其余1793仅Task12库存。App原16skip按身份保持；Infra原2skip仅延续，不能冒称本轮重验18skip。仅两原DimensionDungeonTests失败身份容许：`Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`、`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，原Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException；新Host/Net/SQLite等失败不新增基线，按AGENTS身份/实证诊断而非盲重试。

新脚本/输出只在`verification/logs/task-14-*`、`verification/evidence/task-14-inspection/**`，现probe-tools只读复用；同freeze完整manifest/Result prefix/owned beforeimages和非ownedSHA，禁止整仓before/复制旧归档。before/after capture各fresh ConfigureServices-only，不BuildProvider/启动业务；现family sidecar纯构造两注册表验证多logical和exact CLR闭合。运行登记预期173logical/203cmd不变，删3Tx fine加3Directwhole→18Protocol＋1Internal＋184Legacy、156Tx/34Direct/9RO/4Workflow；message20Protocol＋153Legacy（原14telemetry仍同总账）。这是预期，须实际逐row证明仅五route/三fine→三whole变化，Use/Buy/Task13/core其余commonbindings不变，不硬填结果。

ledger两文件仅可拟闭`message:65937` GameUnsealRandomOption、`message:65974` GameChangeRandomOption、`message:65975` GameSeparateUpgrade、`message:65740` GamePurifyItem、`message:65741` GameInvestAmplifyOption，以及`command:Dfo.Server.Protocol.Game.MagicSeal.MagicSealCommand`、`command:Dfo.Server.Protocol.Game.Forging.ForgingCommand`、`command:Dfo.Server.Protocol.Game.Outworld.OutworldCommand`。填真实caller/原Tx/实际参与者条件/Main及保护去向和source/test/Result artifacts；state用真实state键，original/baseline/其它rows不改。仅预期pending144msg→139、187fine→184、72source不变，5protection/8transition仍pending，不因碰到legacy快照/效果接口就顺闭来源或过渡。

先`preclose --current-task <本Task精确路径>`核拟闭artifact；executor追加Result后strict validate/selftest原八坏副本全部拒绝，不填不存在Result骗strict。最终精确source freeze/manifest及非ownedSHA一致、停止共享写后，交fresh required独立结果快审；review只追加已freeze R1 prefix，主控消费后本地checkpoint。规划阶段只有本文件，无Result/review/status。

Result报告五logical/三whole和三fine删净、2/1/2bindings、原拒绝/roll提交区别、gold/材料/equipment真实SQL/currentreplay、帧序/通知Failed/默认直接ACK故障、唯一mutation红绿、日志观察迁移及其它driver/数量变化、三full四controls精确范围与所有实际诊断。其它139消息/184旧命令/72来源及5/8保护过渡仍由后继闭合；T-012最终新八full、单CMD≤2秒统一接受线测量、Astra、Spec/根封存与用户分支收尾不因本单元免除，不提前宣告B完成。


## Result

- Revision: 1（Task r1；HEAD de65601cc9fa5fa15db6afae59fc589265717cc8；最终source-freeze-01；待主控required独立结果快审，未提交）
- Outcome: done
- Changed: 五logical现由三个完整 GameMagicSealCommand／GameForgingCommand／GameOutworldCommand 接管，原Input/Dispatch/Game metadata明示，三个薄handler仅同bus+既有marker。原MagicSealCommand/ForgingCommand/OutworldCommand及其handler/三Tx注册实际删除，无兼容壳/oldbus转发。MagicSeal同MagicSealRequest两pairs，Forging一pair，Outworld同OutworldRequest两pairs，现有multi-logical机制闭合，无sharedcore/接口/领域/DTO/codec/composer修改。AGENTS仅同步本簇事实，见[workunit-map](../verification/evidence/task-14-inspection/workunit-map.md)。
- Main / refusal: 仅原会调用fine的有效分支验证initial selected及captured session、执行一个Main([actor])；本unit.Footprint直接交既有domain，原owner Demand→plan→Preview→ApplyEffects、Freeze/Settled不改。前置不claim Main，Pending结束Rejected/无projection/HasCommitted false；domain普通无receipt照旧正常MainCommitted/最低Inventory/可空TouchedSlots。五logical各自实证这两种状态及原静默/ACK，初始Rejected在actualhandler前晚出现角色2仍不升级，下一fresh能正常Main。不把正常拒绝一概RequireRollback，不新增NonRequest、catch豁免或clear sticky。测试路径的gold/material/equipment使用fixture同SQLite连接串归属，原facet不是独立CommitCanFail；真实SQL语句故障回滚证其原子性，不声称所有配置固定同一参与者全集。
- MagicSeal: 原session→target非负/second(change0、unseal－1)/online→mapping catch顺序保持，传domain Material始终原null，未把reserved0/－1当实际物料。change走原ChangeAsync(...OptionIndex,unit.Footprint)，unseal走UnsealAsync；前置与domain所有unseal拒绝都无ACK，change才发原拒绝码（默认0x13/0x0A/0x11/0xD6/0x17）。成功仍当前target→当前gold→仅change成功ACK；原三byte option/SQLite reload/currentgold/不二扣及unseal不发未处理ACK的实际断言保留。
- Forging: 原session0x04→所有非0Variant0x0D→原list/slots/itemId/opaque name长度/online→mapping catch0x04；原ForgeAsync及全部业务拒绝码不变。失败roll有Receipt，正常Commit且真实材料50→7，Success=true/ForgeSucceeded=false、old/new level1/remaining7；新增case实证receipt.Roll5000、entryCommitted、footprint非空、core不变，不混成普通Refused。原成功ACK→当前target→当前material顺序与原SQL物品/材料更新在同Main内保持，不新增名字规则或ticket业务。
- Outworld: 原session→两slot非负不同/expectedID正/online→非purify仅action0/1→双Mainmapping catch；action2仍0x08早拒无Main。Purify/Invest/Twist、SelectedOption与expectedID原样调用原ApplyAsync；purify拒绝四byte、invest原WireAction和0x08/0x17/0x12/0x14/0x15/默认0x11保持。成功原ACK→target→material，composer的当前remaining/target缺失receipt回落与精确recipient过滤不改。坏content case仅把日志观察从旧CommandLoggingBehavior移到同provider实际ILogger<GameplayExecutor>，Error、原cause同引用、[durability]、无ACK、材料/core不变及同seq再次抛全部原assert保持；不恢复legacy pipeline、不在whole加重复Error。
- Projection / failure / replay: 三whole只ICommandProjections→同EventService→原NotiBus，初次普通Compose/encoder故障仍逐目标Failed正常隔离，不加initial raw。新增实际change FailBatch首轮Committed/gold99800/request.Failure null/noFence；改真实gold99000后同seq只重放current99000，无ACK/二扣或第二whole。直接拒绝ACK另四实际encoder case（change/Forging/purify/invest）同IOException cause Ref、MainCommitted后最终typed/default fence，未把通知隔离泛化所有输出；unseal未为测试造ACK。三whole只既有CommittedReplayCurrentStateBatch，Use两出口/Buy双shape与NonRequest/Task13守卫不变。三SQL触发器为实际UPDATE语句故障，不称Commit阶段假参与者；实证gold/material/core回滚、ConfirmedRollback/nullprojection，同fingerprint同entry及原Ordinal retryCommitted，再replay不重ACK/roll/扣资产。普通取消/typed/unknown/独立scope默认保护沿只读core与原full保护保持。
- Tests / method: 原EffectRequestTransport/ShopFixture/RecordedOutbound和所有明确seq/raw/Body/期限只读，三个owned测试fixture只最小可见性/callback共享原内容；新增sameprovider actualhandler wrapper只记录context/result并调用实际handler，无第二provider、生产setup CMD或actual logical probe。原case身份/InlineData/全部业务/byte断言保留；两个legacy名单仅移除三fine，三whole另证Protocol/Direct与2/1/2精确bindings/replay-only、不实现legacy marker。行为保持结构迁移先跑原reserved baseline2/2 GREEN，不制造disabled/编译RED或把后写状态case说成严格TDD；新增24App状态/SQL/cause oracle是实现后的证据。
- Meaningful mutation: 唯一变异只删除owned GameMagicSealCommand reserved/sentinel条件，其余前置不动；真实合法change wire带非法second，本应保SQLgold100000，actualdomain以原null material实际花200变99800，原两goldcase RED，新early entry也由Rejected错误变Committed。7case=3pass/4fail，非编译/假异常/文字代理。try/finally原bytes/SHA恢复后同目标7/7，再freeze01；原合法reserved0/unseal成功原强case在定向及full实跑，见[mutation-result](../verification/evidence/task-14-inspection/mutation-result.json)。
- Formal01 / exact scope: 同一18路径freeze01后完整Release --no-incremental build 0warning/0error；App/Protocol/Networking各独立无filter full、原class并行/5m/skip/期限并全部自然结束：App4886＝4868pass＋2准确原Dimensionfail＋16原skip，Protocol1766pass，Net199pass；三full共6851＝6833pass＋2fail＋16skip。Host/HttpApi/Infrastructure/Persistence各实际精确control-only1/1，本轮总6855实际case，不是八full。两fail精确为原Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource及All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，均原Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException，根因未处理，不增基线。其余1793业务case仅Task12接受库存延续，Infra2skip未由control重验。
- Inventory / DFO / bytes: 对Task13 formal01三项目逐case(display/status/multiplicity)核原6826全保，无rename/合并/删除；App新增24、Protocol新增1，共N25/D0，全部通过。全局延续口径8619＋25＝8644＝8624pass＋2fail＋18skip，非本轮八full实测。DFO同一新formal-01目录三full＋四actualcontrol，原74expected精确类/数、七程序集各唯一control齐，outside/exe/orphan0，未放宽expected，exe0只观察。Characterization10逐名pass/0skip，Golden15对f01dc226及起点HEAD原bytes相等，见[formal-summary](../verification/evidence/task-14-inspection/formal-summary.json)与[test-delta](../verification/evidence/task-14-inspection/test-delta.json)。
- Capture: before01/after01 fresh ConfigureServices-only，不BuildProvider/启动业务；173logical/203cmd保持，删3Tx fine加3Direct：18Protocol＋1Internal＋184Legacy，156Tx/34Direct/9RO/4Workflow；message20Protocol＋153Legacy/14telemetry。仅五route/三fine→三whole变化，其余commonbindings全保。family sidecar纯构造两注册表核每logical精确单CLR和2/1/2归属，所有prior Protocol route/许可（含Use、Buy、Task13）逐项不变，新三whole仅existing replay，见[capture-checks](../verification/evidence/task-14-inspection/capture-checks.json)。
- Actual diagnostics: focused01是新fixture RandomOption0条件default(null)造成CS8601编译失败，改明确空option(0,0,0)，非RED；focused02为55/55、Protocol定向22/22。第一次formal launcher残留task13-run.py basename，在任何dotnet前childPython exit2/launcher1退出，无step log/meta/native启动；只修自有脚本文件名，源freeze不变，formal-launch-failure.json保留，不补造native exit或称第二正式轮。真实正式只有当前freeze01一轮，全部18份native/probe metadata含command/cwd/HEAD/UTC/exit/elapsed/resources及console，见[diagnostics](../verification/evidence/task-14-inspection/diagnostics.md)/meta-inventory。未TERM活跃runner/覆盖旧output/拼不同source；无其它新产品失败或未解根因冒充修复。
- Scope / ledger / remaining: 18精确owned source（含三旧fine文件删除），14必要beforeimages；非owned tracked只SHA且全未改，无整仓/旧归档复制，原BOM/CRLF/混合byte/whitespace与纯map/前置及action块原byte保持。ledger仅闭message:65937/65974/65975/65740/65741及三个原fine精确FQN共8ID，填实际caller/Tx/参与者条件/Main/保护/source/test/Result证据；original/baseline/其它rows不动。preclose已通过，Result后strict/原八坏副本、自检/manifest与R1prefix在task-14-inspection；封存后停止共享写交主控fresh required独立快审及checkpoint，不自行批准/stage/commit。其余139消息/184旧命令/72来源与5保护/8过渡pending，T-012最终新八full/单CMD≤2秒统一接受线/Astra/Spec/根封存、legacyReplay/footprint/Lifecycle最终删除及用户分支收尾仍由后继处理。未push/PR/merge/远端issue/deploy/cleanup/改版本/起游戏/创建Task15。
