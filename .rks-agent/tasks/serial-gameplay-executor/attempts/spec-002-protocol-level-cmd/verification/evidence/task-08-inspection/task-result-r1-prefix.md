# TASK-08 — OtherCharacterDetail 协议 CMD 与即时投影

## Task Unit Contract

- Revision: 1
- Outcome and references: 交付父 [Plan P1](../plan.md) T-003 的 OtherCharacterDetail 单协议闭包，对应 [Spec B1](../spec.md) RQ-001/003～005/007/009/012/014/015、AC-001/002/004/005/007/009：薄 handler 一次交付原请求／dispatch，GameOtherCharacterDetailCommand 吸收原前置判定、直接领域查询、typed 即时投影及日志；旧 QueryOtherCharacterDetailCommand／handler／注册删除。原拒绝、双当前性、同 listener、同 wire 新 generation 重解析和出站字节保持。
- Owned boundary: 下文精确源码／测试／AGENTS、本 Task、B 开放迁移账和 task-08 前缀证据。其它源码、共享执行／投影骨干、领域判断、notification／composer／encoder、冻结 baseline／original、旧 Task、父 Spec/P1／契约、根控制器与 A 封存只读。不迁 world／选角／其它协议，不开新事务，不加 effect／Footprint／MainSettlement／Compose。
- Required checks: 旧四 Flow 合同／原App领域守护保持；至少一条同 provider 的真实 dispatcher→bus→新CMD→typed projection→原notification bus／composer→实际作业outbox路径；新CMD前置target复查的有效行为坏变异及恢复。完整Release build、App／Protocol／Networking三full，另Host／HttpApi／Infrastructure／Persistence四个实际正control，准确DFO范围／原字节／注册capture／两行闭合／数量／范围／行尾检查，详见下文。
- Dependencies: TASK-07（`919c59516adc55422ba374ad4f9df4214432f33e`，独立approved、四名册已提交，inventory8465、capture173消息／205命令）；间接消费TASK-04同bus／显式请求／typed投影与发件箱骨干、TASK-05/06已接受接线。
- Result review: required — 该单元迁移跨generation敏感查询和收件人投影，若漏前置复查或误用target audience，可能读取／投递过期状态；成功帧codec与薄入口替身不能充分发现。结果须独立核原守护去向、同容器实际路径、mutation及范围后提交；Task-plan由主控readiness检查，不增加独立计划审批。

## Detailed Plan

### 1. 起点与精确范围

开工核上述HEAD、原 `task/refactor-session-20261021` 分支、除本Task外无未提交改动及唯一Task身份，消费Task07 Result R1／approved quick review和本地提交。读AGENTS及ArchitecturePhilosophy／RefactorPreferences；当前无.codegraph，按rg和拥有源码读。继承中文、本地实施／核验／检查点授权，不push／PR／合并／远端修改／部署／清理，不运行df_game_r或DNF.exe；主控负责结果快审和Git提交。

Task07首Host端口失败的唯一根因未证，原代码零diff、四variants及Host02全量203/203已通过，且已独立审查接受为局部观察。该事实保留为最终报告输入，不能升级为允许失败基线、称已修或在本Task扩成Host端口调查／HostFull重跑任务。后续若出现新失败按实际身份／源码诊断。

允许生产修改：

| 仓库路径 | 责任 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/AccountRoster/OtherCharacterDetailSlice.cs` | 删除17～30附近旧Query命令／handler；98～160附近GetOtherCharacterDetailHandler变薄；仅必要using调整 |
| 同目录新增 `GameOtherCharacterDetailCommand.cs` | 完整原协议编排、新CMD／handler及无业务字段的GameOtherCharacterDetailCompleted结果 |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 仅本消息／新CMD绑定及删除旧Query注册；保原OtherCharacterDetailRequested notification注册 |

允许测试修改仅：

- `Server/tests/Dfo.Server.Protocol.Tests/Game/AccountRoster/OtherCharacterDetailFlowTests.cs`：原前两例驱动／旧类型断言迁移、薄入口、同provider真实路径、必要前置守护及本文件fixture。
- `Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs`：只加本Protocol route／精确消息CLR绑定／旧Query注册消失判据，不改其它名单和坏例。

原slice的OtherCharacterDetailRequested／Response、OtherCharacterDetailRequestedHandler、OtherCharacterDetailResponseEncoder代码保持；AccountRoster Messages／decoder／payload codec／StateSynchronization也不改。Application的OtherCharacterDetailQueryDomainService与其测试、现notification/event/outbox/dispatcher/bus骨干和所有其它源码只读，不新增共享fixture／IVT／项目引用或注册适配接口。必要越界先给主控实际证据和最小增补。

AGENTS只在当前opt-in列表加入OtherCharacterDetail，并精确说明直接领域查询＋typed即时投影、原无事务／generation与listener保护；不泛写最终模型，不清理其它仍有效legacy例子。可写本Task、B `verification/migration-ledger.json`／`.md`及 `verification/logs/task-08-*`／`verification/evidence/task-08-*`；原probe-tools复用不改，冻结baseline/original、其它closed行和已提交Task/A制品不变，不另造研究／计划副本。

### 2. CMD形状及原行为逐项吸收

新增 `GameOtherCharacterDetailCommand(NetworkDispatchContext Dispatch, GetOtherCharacterDetailRequest Request)`，实现 `IProtocolCommand<GameOtherCharacterDetailCompleted>`；Message返回同一Request，Frame为 `ProtocolFrameMetadata.Game(Request.Metadata)`。完成结果为同文件空readonly record struct，不把原内部query结果或前置target变成新的外部业务合同。

GetOtherCharacterDetailHandler实现对应protocol-handler marker，只注ICommandBus、构造本CMD、一次SendAsync并等待；不再查session、调用细Query或投影。新CMD只注ISelectedSessionDomainService、IOtherCharacterDetailQueryDomainService、ICommandProjections和ILogger<GameOtherCharacterDetailCommandHandler>，不注bus／sender／IEventService／WorkUnits／queue／locator。使用现ProtocolNonEffect路线，原request非effect，不能加IEffectSettlementRequest或旧selected/effect marker。

原OtherCharacterDetailSlice110～158的次序／拒绝／日志逐义搬入CMD：

1. sessions.FindByConnection(原物理connectionId)得到requester；无值按原“ignored for non-current connection”Information日志静默结束。
2. sessions.TryGetInWorld(Request.TargetWireUserId, out target)；失败按原TargetNotCurrent日志结束，不query／projection。
3. 先查requester.IsInWorld，再查target.IsInWorld，各自生成原CharacterDetailQueryResult拒绝；只在二者都current时直接details.QueryAsync(requester, wireId, ct)，没有CMD调用CMD。
4. 有result.Projection才调用ICommandProjections.ProjectAsync，audience保持 `new NotificationAudience.TheseCharacters([requester.CharacterId])`，通知仍为 `new OtherCharacterDetailRequested(projection)`，ct原样。
5. 保原GET_USERINFO mode=3的requester／target／accepted／failure日志；异常仍按原传播，不新增catch／补偿／close协议。

特别保留两个不同阶段：前置target只用于原分支复查；details.QueryAsync仍只接requester＋TargetWireUserId，领域在其执行点重新解析target。不得冻结初次target、增传target identity、要求最终projection.Target等于最初target，或把audience改成target／TheseSessions。原App `ReplacedRequesterOrTargetCannotReadAnOldGeneration`111～129明确同wire的新target可返回generation2。

旧注册Direct，实际协议没有显式Transactional／ReadOnly边界；新CMD仍Direct、无ICommandWorkUnits，不因它“是查询”擅自包ReadOnly或SQLite事务。即时投影保持写事务外，不能启用Compose或将projection改成发布领域事实。这里不触达新持久写，不借无帧／可信内部入口获取网络请求权威。

### 3. 原领域／composer保护只读保留与caller闭合

原领域 `Server/src/Dfo.Server.Application/Characters/OtherCharacterDetailQueryDomainService.cs`13～54继续拥有：sentinel target拒绝、requester current、按wire解析target current、same listener、读取current view后双generation复查、target-state character identity及ProjectedAt。它按当前事实回答，不能把旧状态缓存进CMD。

原slice43～67 composer继续检查recipient的requester character／connection精确匹配、requester current、target current、same listener、target-state identity；原70～95编码器仍NOTI 0x0002、subtype3、query checksum1。它们既有判断不是本Task重构对象，不通过修改这些原保护造scope外mutation或简化判定。

全Server旧完整符号引用只有：本slice的定义／细handler／141调用、Module旧注册、Protocol Flow46旧DTO类型断言；无Host setup／其它生产消费者。开工再次按精确符号rg复核，若发现新caller先交主控定位。删除后生产／测试的QueryOtherCharacterDetailCommand及其handler完整符号为零；允许冻结baseline／原映射快照保历史文字，不把它们当源码残留。

### 4. 原四Flow迁移与真实投影路径

保原OtherCharacterDetailFlowTests的四个业务合同，不用删旧Fact换另一组弱断言：

- 原22～58：旧细Query类型断言退出后，通过实际新CMD执行时query service的记录，继续断言精确requester、TargetWireUserId，typed投影audience为requester TheseCharacters，传入通知的projection为原精确计划。薄handler只交原request/dispatch的检查单独明确，不把执行期捕获的requester塞进新command输入。
- 原62～83：query拒绝仍无projection、无outbox条目。
- 原88～112：composer双方generation变化拒绝的原断言／驱动保持。
- 原116～126：NOTI首字节、type、checksum及subtype原字节断言保持。
- 原App领域测试全部只读，尤其双generation／listener／新target重解析，不改其断言以迎合新驱动。

本文件至少一条实际路径从执行器外调用现INetworkMessageDispatcher.DispatchAsync，经过真实薄handler、同一CommandBus、新CMD、ICommandProjections、原NotificationBus/audience resolver、原OtherCharacterDetailRequestedHandler及同作业GameplayOutboxes。不能包TestGameplay；测试自己准备session等直接写入才用现TestGameplay／本地合法fixture。

可用本文件局部可观察session／query domain替身隔离无关PVF依赖，但projection必须委托同provider的真实实现：现AddEventDispatching使ICommandProjections和IEventService同为EventService（DomainEventDispatchingExtensions24）；其ProjectAsync经NotificationProjectionSubscriber/INotificationBus，原TheseCharacters resolver和composer决定实际输出。若需记录audience/projection，装饰当前descriptor并委托真实能力，不使用裸List替身／第二provider冒充实际投影。

本地提供精确INetworkConnectionContexts、可记录测试发送端和response编码观察，绑定本provider真实常设登记适配／outboxes。可复用公开模块的生产descriptor在本provider注册，不能因internal类型不可见而增加IVT／反射／第二service provider。成功路径证明query只一次、投影只向requester、实际batch目标为其精确connection，target不多收，响应仍由原encoder产出NOTI subtype3／checksum1；记录query和projection发生在GameplayJob内且无环境事务。被测结果仅承诺登记／实际测试发送端观测，不冒称A21实机或网络送达。

fixture建立／机械签名问题先修正常前置，不把构图失败当行为RED。原记录audience/projection／拒绝断言必须继续成立；无目标／无requester／前置失效分支可用少量数据用例覆盖，不镜像重写完整领域服务测试。

### 5. 前置target复查的test-value gate

本次有价值mutation只作用于新CMD内原迁入的target.IsInWorld检查，不改原domain／composer：

- fixture使前置TryGetInWorld返回原target后，该target换代或不再InWorld；记录“lookup完成→target失效”的实际顺序。
- 正常CMD必须在随后的target复查拒绝，query调用0、projection0、outbox0。query替身在被错误调用时仍能返回固定计划，使下层composer拒绝不掩盖“本不应查询”的错误。
- 临时移除／绕开新CMD这一target分支，精确query调用次数oracle应红；即使outbox仍因原composer检查为空，也不能判变异通过。恢复原bytes后同用例绿。

至少取得该有效红绿；机械薄入口／旧DTO删除无需编译RED，若实现实际失败已区分同风险可据实复用而不造重复变异。所有关键断言放CMD外，保存独立log/meta／变异前后SHA、finally恢复；编译错误、未建fixture、timeout不算行为RED，不改旧断言／deadline／skip／xUnit并行求绿。需要更多测试时先说明新的可区分风险，不扩world／选角／Footprint研究。

### 6. 两行ledger、注册和数量

消费Task07已提交capture：173消息＝9Protocol＋164Legacy；205命令＝9Protocol＋1Internal＋195Legacy，166Tx／26Direct／9ReadOnly／4Workflow。本单元旧一条Direct注册换新一条Direct，合理预期总数不变，消息10Protocol＋163Legacy，命令10Protocol＋1Internal＋194Legacy，Kind不变；pending业务150→149、旧命令195→194、来源72不变。最终以本轮ConfigureServices-only实际capture源码↔runtime逐条双向核实，不硬填，不启动业务host或反射运行旧handler。

只闭合以下现有冻结ID：

- `message:16973832`（GameGetOtherCharacterDetail）；
- `command:Dfo.Server.Protocol.Game.AccountRoster.QueryOtherCharacterDetailCommand`。

destination指向GameOtherCharacterDetailCommand，actual_units明确旧Direct领域查询／即时投影无事务→新CMD同位置直接领域与typed投影，无新增事务或selected/effect工作单元；保原notification注册和原domain／composer保护位置。不把“查询无显式事务”泛化为其所有底层读永远无任何实现细节，不冒领五保护／八过渡closed，不改原original／其它closed行。沿preclose仅当前Task待Result→产品／映射预检→执行者写Result→strict validate/selftest，保原八坏副本拒绝。

全库inventory起点8465：Pvf80、App4716、Host203、Http57、Infra976、Net199、Persistence477、Protocol1757。本Task只允许Protocol测试文件变化，按实得case数写N/D/merged和旧驱动／原断言去向；原四Fact不删除，Theory展开逐例计。全库 `8465＋N－D` 是累积inventory，不是本轮三full／四control的执行总数；App／Net无新用例时数保持，未跑的其它完整项目沿已接受inventory，不冒称本轮全量。

### 7. 正式核验与交回

源码冻结后完整Release构建0warning／0error；正式native require_escalated／caffeinate，原class并行、skip、5m hang期限、console detailed及-m:1保持，每轮task-08独立log/meta落cwd／HEAD／实际command／UTC／exit／耗时与实际Darwin资源。三full：

```text
dotnet build Server/86JP.Server.slnx -c Release --no-incremental
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

Host／HttpApi／Infrastructure／Persistence四个项目各实际运行现有精确正control，使用相同Release／no-build／详细log／deadline参数，额外filter为 `FullyQualifiedName=Dfo.Server.Testing.GameplayAssertionRecorderControlTests.An_off_executor_violation_in_this_assembly_reaches_the_recorder`。该FQN已核Server/tests/Support/GameplayAssertionRecorderControlTests.cs；保存四个实得command／exit／各1例结果。不重跑这四项目full，Pvf本轮不选；完整build覆盖全部项目构建，T012八full责任保持。

App只允许原两准确DimensionDungeonTests：Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource、All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，均Dungeon7100／maze0／cell(1,0)／entrance Left的既有InvalidDataException。保原App16skip，不将Task07未证端口观察或新SQLite／其它失败自动塞入允许基线。其它失败按当前AGENTS身份及拥有源码诊断。

七个取证程序集全部有实际control且各唯一；三full按自身既有expected核反例类／数（沿原App62、Net7及Protocol原expected，实际重新读工具确认），其余四只有control过滤范围。用新鲜DFO目录，逐程序集记录full/control、expected/observed、实际records和外0／exe0／orphan0；不复制旧JSONL、不混合轮次、不以Task07的74写成本轮“七full完整74实测”。exe0仅是该观测。新测试若故意触发保护，先定向核精确类／数再给新增expected理由，旧类数不放宽。

完整Net内原Characterization10逐名通过／0skip，不额外重复同一轮表征；15Golden对f01dc226与919c5951无diff。原OtherDetail encoder／payload／composer代码段字节保持，App原generation／listener／samewire新代测试、共享请求／注入／禁嵌套／原F88/F93及F132禁用判据保持。不开新事务，不制造持久提交／回滚或效果性能的新结论。

最终核精确白名单、其它源／Task／baseline／A SHA不变、旧Query完整符号生产和测试零引用、新薄入口／CMD注入面和无bus／sender／IEventService／WorkUnits；AGENTS仅当前opt-in和精确查询例。CRLF／混合文件bytes定点编辑并记录行尾／BOM前后，`git -c core.whitespace=cr-at-eol diff --check`及新文件空白检查通过。原scope双故障残余不借本单元扩修；实际发现新scoped组合触及则先向主控说明。

受影响核验完成后由执行者写Result R1，列实际业务位置／保护保持／原四Flow驱动迁移／同provider路径／有效mutation／准确三full四control／N-D／capture和strict／公开范围限制，交主控required独立结果快审与本地检查点。T003整体、其它协议／效果链／过渡清零、T012八full及同A工具性能／单条2秒、Astra和用户分支收尾门均仍待后续，不在本单元冒称完成。



## Result

- Revision: 1（Task r1／HEAD919c5951，父验收与白名单未改）
- Outcome: done
- Changes: OtherDetail薄handler仅原request／dispatch一次派发GameOtherCharacterDetailCommand；旧Query命令／handler／注册删除。新CMD吸收原requester查找、target wire查找、双方前置IsInWorld、直接IOtherCharacterDetailQueryDomainService.QueryAsync(requester,wireId)、ICommandProjections的TheseCharacters(requester)与原日志。原Direct无Write／ReadOnly／效果marker，未注bus／sender／IEventService／WorkUnits。target仍由领域按同wire当时事实重解析，不冻结前置身份。
- Original protections / drivers: 原notification／composer／response／encoder整个代码块逐字节保持；原Flow后两composer双方换代与NOTI type/subtype/checksum Fact body原样。前两Flow从细DTO stand-in bus改真实完整链，保精确query参数、requester audience、同一projection及拒绝无投影；App领域／samewire新代／generation／listener测试全部只读并在App full通过。AGENTS仅当前opt-in与该查询的无事务／重解析说明。
- Real path: 新Protocol fixture只构建一个provider，实际INetworkMessageDispatcher→薄handler→同CommandBus→新CMD→ICommandProjections同一EventService→NotificationProjectionSubscriber→原NotificationBus／TheseCharacters resolver／原composer→当前作业发件箱。投影记录装饰器调用原descriptor factory取得真实内层并委托，不以裸List或第二provider替代。IOutboundSender测试接收器观测flush后的真实batch，原response encoder实际编码，只requester连接有NOTI；target不多收。query与projection发生在GameplayJob中且无环境事务。此证明登记／本地测试发送端观测，不是A21实机或传输送达验收。
- Test value: target在wire lookup后换代，正常CMD query/projection/outbox均0。把新CMD的target guard临时换成requester，使可编译坏变异的mode2实际query1并在CMD外Assert.Empty红；3例中2过1红，finally恢复源bytes／SHA后3/3绿，原domain/composer不改。见[mutations](../verification/evidence/task-08-inspection/mutations.json)。初版params目标推断、ReadOnlyMemory测试读取的build01/02编译问题在白名单内机械修正，不算行为RED；build03／directed01为0/0和17/17。
- Formal checks: 冻结后完整Release --no-incremental build0warning／0error（17.551s）。三full：App4716＝4698pass＋2准确原Dimensionfail＋16原skip（114.963s）、Protocol1762pass、Net199pass；合6677＝6659pass＋2旧fail＋16原skip。Host／HttpApi／Infrastructure／Persistence只各跑现有精确正control，4/4通过，不冒称其full或本轮八全量。两原失败均Dungeon7100／maze0／cell(1,0)／entrance Left，完整身份／原log/meta见[formal-summary](../verification/evidence/task-08-inspection/formal-summary.json)。没有新增允许失败基线，Task07端口观察保留后续报告输入，本单元未重跑Host full或宣称已修。
- DFO / wire: task-08-formal-01单一新鲜目录，三full＋四exactcontrol实际七程序集各唯一control，74条精确原expected、外／exe／orphan均0，未拼接旧目录／旧JSONL。74不是七full；exe0仅此次取证观察。Net全量中原Characterization10逐名通过／0skip；15Golden对f01dc226／919c5951实际bytes相同；原encoder／composer与四Flow旧合同保持。
- Counts / capture: N5/D0＝Flow新增thin1＋前置失效Theory3＋registration1；原四Fact保留，只迁前两driver。全库inventory8470＝8465＋5，Protocol1757→1762，其它完整数量沿Task06／07接受结果；这不是本轮三full四control执行总数。capture实得173消息（10Protocol＋163Legacy，含14遥测）、205命令（10Protocol＋1Internal＋194Legacy），Kind166Tx／26Direct／9RO／4W不变。仅message16973832与旧Query command两冻结ID闭合，所有original／其它closed／baseline保持，见[workunit-map](../verification/evidence/task-08-mapping/workunit-map.md)。
- Source / process checks: 精确6物理源码／测试／AGENTS路径，无文件删除；旧Query与handler全Server符号0，薄入口／注入面／无嵌套bus／无WorkUnits／同wire参数检查通过。所有其它Server源preflight SHA不变，原notification/composer/encoder block与后两Fact bodies不变，行尾／BOM／whitespace通过；冻结后无执行源变化。App耗时与Task07 115.950s同量级，原资源meta照录，非单条CMD2秒测量。preclose仅当前Task待Result；Result后strict／八坏副本及manifest绑定见下方最终收口。
- Remaining / scope: 本Task不代表T003／Spec完成；非遥测业务149、旧命令194、来源72、五保护／八过渡仍pending。效果链、其它协议、T011清零、T012八full／同A工具性能／单条2秒、Astra与用户分支收尾门仍在后续。未触及scope双故障残余，未新增scoped disposable组合或扩修骨干。未gitadd／commit／push／PR／部署或启动下一Task；required独立结果快审／本地检查点归主控。

- Final closure checks: Result R1后strict有效／completion_results_pending空，八坏副本selftest全部拒绝；独立task-08-strict-final／selftest-final原log/meta及final JSON一致。最终source检查再次通过。manifest排除Task全文件，另绑定R1字节前缀与snapshot供主控追加review，执行者未自批required review／未提交。
