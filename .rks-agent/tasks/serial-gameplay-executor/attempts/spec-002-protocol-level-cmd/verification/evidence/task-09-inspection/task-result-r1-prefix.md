# TASK-09 — 直接效果足迹的显式能力与有界 legacy 捕获

## Task Unit Contract

- Revision: 4
- Outcome and references: 交付父[Plan P1](../plan.md) T-004的首个共享足迹单元，对应[Spec B1](../spec.md) RQ-004～006/009/012/014/015、AC-002/003/005/007/009的有界前置成果：六个领域对象与三个fine handler的slots/rental记录改为本次显式IEffectFootprintRecorder，旧IRequestReplayProjectionDomainService接口／隐式方法／DI alias删净；真正legacy invoker在原主执行及事务开始后捕获有存活边界的能力，不从缺帧推导可信内部来源。该结果供后续EffectExecutionContext／ApplyEffects及协议效果迁移消费，不宣称RQ-005全部成立。
- Owned boundary: 下文精确共享机制、领域／Protocol调用闭包、实际测试驱动、AGENTS、本Task和task-09前缀证据；冻结baseline／original、旧Task／A、父Spec/P1／契约、根映射和其它源码只读。不迁任何消息route／命令业务，不启用新Effect两态／MainSettlement／Compose，不改ledger身份／事务算法／执行器或业务规则。
- Required checks: 真实旧pipeline捕获与slots/rental投影、来源分类与闭态／owner／主执行、两请求同singleton不串、无选中空slots/rental拒绝、预取cap结束后域调用在写前拒绝、新CMD不得触桥且catch后不能续写；一个有效可编译行为mutation红绿及原bytes恢复。完整Release build＋八项目full、表征10／Golden15、单一新鲜七control取证、真实注册capture、数量／精确范围／行尾与strict/selftest，详见下文。
- Dependencies: TASK-08（`c57eb0d6f88341b6cc2c2724e3d2ac529edddab4`，独立approved、本地提交，inventory8470、10Protocol／1Internal／194Legacy）；消费TASK-04现显式ctx、请求结束／粘性失败及普通WorkUnits骨干，TASK-02的四保护与F132禁用判据。现有legacy效果ledger和45/58/60继续承担尚未迁的结算职责。
- Result review: required — 来源误分类或旧scope泄漏可令一次请求借后续会话／内部权威写状态；只改签名仍可能把ambient藏进wrapper。后续效果主单元会建立在本能力上，结果须独立核实际参数链、捕获时机／写前校验、旧保护与取证后提交。主控检查Task readiness，不增加Task计划独审。

## Detailed Plan

### 1. 起点、实际闭包和精确文件边界

开工核上述HEAD、原`task/refactor-session-20261021`分支、除本Task无未提交改动及Task唯一身份，读Task08 Result／快审和相关Spec/P1。无.codegraph，按rg及拥有源码读。继承中文、本地实施／核验／检查点授权；不push／PR／合并／远端issue／部署／清理，不运行df_game_r或DNF.exe。主控负责required快审和Git提交。

当前十个隐式消费者是ApplyEffects加六个领域对象和三个fine handler；本单元只迁后九个。实际直接记录点及参数闭包：

| 记录方 | 当前位置／入口 | 本次显式贯通 |
|---|---|---|
| HousingOperations | ConsumeAsync:103；Plant/Water也从ExecuteAsync调用Consume | HousingSlice → IMarriageHousingDomainService.ExecuteAsync → MarriageDomainService.Housing → HousingOperations.ExecuteAsync/ConsumeAsync；UpgradeRing同属该链 |
| GrowthCapsules | ExecuteAsync:42，Take发物后记录 | CapsuleSlice → IMarriageCapsuleDomainService.CapsuleAsync → MarriageDomainService.Capsules → GrowthCapsules.ExecuteAsync |
| NpcShopDomainService | PurchaseAsync:97 | BuyItemCommandHandler普通NPC分支；Housing Purchase分支无此记录，不无据改其签名 |
| ItemSocketDomainService | ApplyAsync:85 | ItemSocketsSlice fine handler |
| AvatarCompoundDomainService | CompoundAsync:84 | AvatarCompoundSlice fine handler |
| ItemUseEffectDomainService | UseAsync:51，租赁物品记录rental | UseStackable fine handler；另SharedEffectSlice → ISharedEffectDomainService.UseAsync → SharedEffectDomainService → ItemUseEffectDomainService |
| CollectionBoxCommandHandler | slice55 | 从本次ctx取cap，传相同actor记录Inventory |
| RentEquipmentCommandHandler | slice79 | 成功后仍记录原TouchedSlots |
| JoustBetCommandHandler | slice152 | 仍记录原Debits slots |

允许生产修改（均在Server/src，表内路径为项目内相对路径）：

| 项目 | 精确路径／责任 |
|---|---|
| Abstractions | 新`Effects/IEffectFootprintRecorder.cs`；`Effects/IReplayStateProjection.cs`仅删除旧IRequestReplayProjectionDomainService；`Inventory/INpcShopDomainService.cs`、`IItemSocketDomainService.cs`、`IAvatarCompoundDomainService.cs`、`IItemUseEffects.cs`、`ISharedEffectDomainService.cs`、`Marriage/MarriageHousingContracts.cs`、`MarriageCapsuleContracts.cs`仅相应方法必需参数 |
| Application共享 | `Commands/CommandContracts.cs`、`CommandBus.cs`、`NetworkCommandAuthorization.cs`、`Dispatching/NetworkMessageDispatcher.cs`、`Effects/EffectOperationContext.cs`、`ApplicationModule.cs`；可新增`Effects/LegacyEffectFootprint.cs`存有界capture／lease／recorder具名类型，或嵌套于现类型，不能多造取current门面 |
| Application领域 | `Inventory/NpcShopDomainService.cs`、`ItemSocketDomainService.cs`、`AvatarCompoundDomainService.cs`、`ItemUseEffectDomainService.cs`、`SharedEffectDomainService.cs`；`Marriage/HousingOperations.cs`、`GrowthCapsules.cs`、`MarriageDomainService.Housing.cs`、`MarriageDomainService.Capsules.cs` |
| Protocol | `Game/BuyItem/BuyItemCommand.cs`、`ItemSockets/ItemSocketsSlice.cs`、`AvatarCompound/AvatarCompoundSlice.cs`、`UseStackable/UseStackableSlice.cs`、`UseStackable/SharedEffectSlice.cs`、`MarriageHousing/HousingSlice.cs`、`MarriageHousing/CapsuleSlice.cs`、`CollectionBox/CollectionBoxSlice.cs`、`Rental/RentalSlice.cs`、`Joust/JoustSlice.cs`；只fine handler参数／能力取得／验证，不迁网络handler或编码 |

不改ProtocolModule、命令注册、Messages／decoder／codec／encoder、业务规则、存储／表结构、EffectOperationLedger身份算法、45/58/60算法或GameplayExecutor／GameplayJob常设身份。EffectOperationContext只重构直接记录核心及新增capture，旧BeginMain／Freeze／Settled／Failed／rollback时点逐义保留。ApplyEffectsDomainService的optional operations注入和Record、EffectExecutionContext及17个构造点本单元只读，明确下一Task消费；不借本单元改preview、XP、Opening或其它effect消费者。

允许修改的既有测试仅以下八个App文件（前七是真实调用／接口实现，末项是真实fake bus适配）：

- `Game/UseStackableHandlerTests.cs`：直接fine／Shared调用与静态UseContext驱动，保原业务断言。
- `Marriage/MarriageHousingTests.cs`、`Marriage/MarriageCapsuleTests.cs`：直接领域Execute/Capsule参数与每次明确测试cap。
- `Sessions/SelectionWorkflowCurrencyTests.cs`：RecordingHousing仅匹配Execute签名；Initialize等原合同不改。
- `World/BossTowerDomainServiceTests.cs`：224/246附近直接NPC购买和物品Use参数。
- `Inventory/RealConsumableIntegrationTests.cs`：42直接ItemUse参数及真实事务驱动。
- `Inventory/EquipmentSocketTests.cs`：174直接Apply参数。
- `Inventory/ItemUseFixtures.cs`：DirectCommandBus受迁UseStackable分支每次明确测试输入／lease，MoveItemSpace与Tutorial分支保持。

新增App `Sessions/LegacyEffectFootprintTests.cs`承载本单元实际桥接／反例／singleton行为；如上述直接调用需共享一个小型测试cap，可新增`Support/TestEffectFootprints.cs`，只表达显式TestInternal声明、owner／job／lease与记录观察，不成为生产授权入口，不自动给TestGameplay信任。生产既有IVT足够时使用；否则局部test fake实现本窄契约，不新增IVT／项目引用／反射或生产测试平台。

CarriedGoldCompositionTests只解析DI；MarriageArchitectureTests只反射领域结构；HousingNetworkSettlementTests所见Initialize未改签名；AvatarCompoundTests等使用真实外部bus，均只读回归，不因粗候选名单机械修改。其余直接handler驱动先rg核实际受迁能力调用；确需越界先给主控源码／失败证据和最小增补，不能顺手改全部39 marker测试。

AGENTS仅补本轮直接slots/rental显式输入、legacy capture／明确来源和仍未迁ApplyEffects的当前边界；opt-in数量仍10，不能写请求ambient清零或新效果已启用。可写B开放ledger的具名notes及本Task、`verification/logs/task-09-*`／`verification/evidence/task-09-*`，原probe-tools／formal工具只读复用，必要task09专用脚本放自己的证据前缀，不另造只读研究归档副本。

### 2. 窄能力、写前校验与直接域参数

IEffectFootprintRecorder只表达本次足迹，以及无副作用的活性／owner校验；本单元实际使用slots和rental两类，不含查当前请求／服务定位／开事务／网络／入队。具体私有类型名可在实现中选，不增加任意公共能力类别。所有上述域方法需要cap参数，不设optional null、静态默认／NullRecorder、DI current或旧签名取ambient重载。删除六领域对象的旧IRequestReplayProjectionDomainService构造依赖、三个fine handler同类依赖及ApplicationModule唯一alias；EffectOperationContext不再实现该旧接口，旧隐式slots/rental API最终源码零引用。

cap验证不能用RecordInventorySlots([])代替，避免验证本身增加Inventory footprint。selected-cap按捕获的本次frame／execution／recipient／owner和原当前性语义校验，未主执行、未开始事务、冻结／关闭／错误owner均拒绝；rejected-cap连空slots与rental也拒；NonRequest必须是下节明确来源赋予、仍受本次lease/job限制，而不是缺状态fallback。未知或缺参是程序错误。

防止调用方在有效期先取走cap、结束后再调用域方法：各受迁域公开／内部入口在业务变更前显式DemandActive/owner；实际变更前若先经过await，应在相应写前重验，不只在末尾Record才发现。尤其Housing facade的Ensure/Storage.Edit、GrowthCapsules的Give peer写及Take发物、NpcShop直接材料变化、ItemUse的前置revive/entry/experience分支，都不能先写后靠最后Record补拒绝。复用同一个无副作用校验，不加await／取消点、任意async tracker或新的世界查询／权限；若实际跨await写段出现超出此闭包的生命周期问题，给主控具体出路，不扩成全局追踪器。

按原次序记录slots与rental，保原业务判断、返回值、事件／持久化／事务归属；不因为cap参数新增effect marker。例如普通BuyItem仍非effect、HousingBuyItem仍原marker，MoveItemSpace不在本单元。共享逻辑直接经域方法，不新建CMD或dispatch另一个CMD。

### 3. 有界legacy捕获与明确来源

捕获仅允许真实CommandHandlerRegistry的LegacyFineGrained分支。在CommandBus.InlineAsync构造pipeline后，最内invoker delegate处捕获：此时45已BeginMain、60已TransactionStarted，不能在构造CommandExecutionContext时提前取值。持有旧frame／execution／rejection对象引用和本次调用lease／job身份，不复制可变Closed/Frozen值成永久快照。finally关闭本次lease；关闭早于外层60 Freeze/Commit是预期，后续pipeline自身结算不调用该cap。Record只读捕获对象，不重新读EffectOperationContext.Current、auth current、DI或环境事务来找请求。

既有CommandExecutionContext可以携带该显式输入及具名取得方法；默认四参context不自带NonRequest，旧测试不能靠它自动获权。新Protocol/Internal有Request时拒legacy桥／该legacy取得分支，按已有request粘性失败记录程序错误，业务catch后下一Write也不能绕过；不得新增AsyncLocal、job slot请求态或单例“当前能力”。现常设GameplayJob仅检查活性／本次identity，不更改其算法。

NetworkCommandAuthorization.Metadata当前把不存在与Closed都返回null；DemandCurrent也不是分类器。因此在**已有frame**上增加明确网络消息类别，具名快照至少区分NoFrame／已知ActiveNonEffect／已知ActiveEffect／Closed；历史Enter未提供类别的active frame归Unknown/Unavailable，不折算NoFrame或NonEffect。NetworkMessageDispatcher唯一生产Enter从实际message是否IEffectSettlementRequest赋值，不用metadata有无、连接ID或命令名猜。旧Persistence LoginSessionQueuedCommandTests的Enter(authority)仅授权QueuedWrite，保原未知Unavailable且原58/100先拒Unauthorized，不为此机械修改该测试或给默认Enter扩大权限。

来源判据按以下已证入口成立：

| 已知输入／捕获状态 | 允许结果 |
|---|---|
| 明确ActiveNonEffect网络类别，无operation／rejection | 本次合法NetworkNonEffect cap；仍非内部来源 |
| 明确ActiveEffect且有效selected operation；或测试明确建立的原operation scope | 捕获selected frame/execution，主执行、owner和事务／存活守护成立才能记录 |
| 明确ActiveEffect的rejection scope | 只允许原拒绝路径；cap不能记录或结算，之后出现选角不能升权 |
| 明确ActiveEffect但应有operation/rejection缺失、相互矛盾 | Unavailable，受迁能力取得／使用拒绝，不能NonRequest |
| 真正off-executor legacy SendAsync自身排队分支 | 在该实际分支生成局部Standalone输入，closure→Inline显式传，不由缺frame推断；保现外部bus marker测试／旧调用合同 |
| executor内无已知来源／仅未知授权frame | Unavailable；不因GameplayJob存在、TestGameplay／test:*／Source.Connection或transaction存在推可信 |
| auth／operation／execution／lease／job任一已关闭 | 优先拒；不能转为内部或Standalone |
| 新Protocol/Internal | 不触legacy桥，无legacy fallback |

现39个生产IRequestSettlementCommand都在Protocol、显式构造点为入站handler/helper，未见生产内部后台／事件构造marker。外部测试真实bus提交UseStackable、ItemSocket、AvatarCompound、FinishQuest等必须由真实Standalone分支继续合法；不是给新typed Internal绕过origin。手工EffectOperationReplayTests的Enter／EnterWithoutSelectedSession是明确状态输入，不能误杀。

为避免未迁39 marker一次全部被迫改驱动：innermost可保存Unavailable快照，不在每个旧invoker进入时全局抛错；本单元**受迁显式能力取得／使用**必须fail-closed。未迁ApplyEffects等仍是已登记临时消费者，不能据此声称完整RQ005已实现。直接fine handler测试须每次显式test来源／cap／lease，静态UseContext改为本次输入；TestGameplay本身不自动授信，不跨job复用cap。

真正外部Standalone分类在入队抑制ExecutionContext前需保留并拒已关闭继承状态，不能因队列丢失ambient把它洗成新根。active网络frame但job被RunDetached清除的情况，先核实际生产caller；目前RunDetached生产点在Networking回调，未证明需要直接外部legacy bus足迹。不得无证默认把所有active-frame调用全入口拒绝或认Standalone；在受迁cap边界按已知网络类别／Unavailable处理，若发现合法caller先给主控具体入口与最小处理，不写泛化白名单。原泄漏bus和新CMD nested/sticky保护保持。

### 4. 行为证据与测试驱动

新增测试优先用真实同provider CommandBus／原45/58/60／domain事务、真实EffectOperationContext/ledger与本次cap，尽量利用现ShopFixture和原效果请求transport；不是只有手工recorder列表。至少一条slots和一条rental从真实dispatcher/旧pipeline完成到原Freeze后的projection，保相同原request metadata、精确recipient、主执行及事务事实。构建fixture／签名编译不算行为RED。

最小区分集合（可合并Theory，不镜像九份相同测试）：

1. 捕获位于主事务开始之后；同一singleton消费者连续两请求取得不同cap，第一份slots/rental不被第二次覆盖；失败／确认回滚后下一请求不串。
2. selected owner／recipient不符、非主执行、未开始事务、冻结、scope／lease／job结束均不能记录；空slots不能绕过rejected。
3. 无选中请求后来选中仍拒绝；明确NetworkNonEffect与真正Standalone合法，ActiveEffect缺state、未知onexecutor和默认四参ctx不获NonRequest。
4. 新Protocol/Internal试触桥被拒；新CMD catch该程序错误后尝试普通Write仍失败且业务计数0。复用Task04真实bus/request而非第二provider。
5. 调用者在有效lease先取得cap，再在结束后的续体调用至少一个实际受迁域方法，证明cap异常前业务写计数／真实存储不变；也覆盖rental或slots的空输入拒绝。断言放业务catch外，不用Xunit异常被吞后计通过。
6. 原直接方法测试逐个显式test lease，保库存／SP／SQL／事务／回滚／slot／域拒绝断言；局部fake能力只用于旧测试驱动，真实桥接由前述路径证明。

一个有价值可编译mutation即可：例如去掉lease关闭/写前Demand，使“预取cap结束后域调用不变更”oracle实际红；或把捕获提前到context构造处使主事务／真实projection oracle红。先选能区分本风险的一种，独立log/meta、finally恢复原bytes/SHA再绿。纯签名替换不造编译RED，不调deadline／Skip／并行求绿，不改变旧保护／算法来让新fixture通过。

结构／可拒反例应守：六域constructor不含旧请求服务或locator；生产直接slots/rental方法为必需cap输入；旧接口/API/DI alias零；新CMD不能legacy capture；不存在current解析wrapper／默认NonRequest／新增请求ambient。它们可以在本单元新测试文件内实现，不为此放宽原架构判据或新增整套扫描框架。F88/F93四守护及F132禁用原测试只读保持。

### 5. 迁移账、注册与数量口径

本Task不迁消息或fine命令，不关闭业务message／command／source行；注册capture合理预期仍173消息（10Protocol＋163Legacy）、205命令（10Protocol＋1Internal＋194Legacy）、166Tx／26Direct／9RO／4W。仍以ConfigureServices-only实际capture双向核对，不能启动业务。pending业务149／旧命令194／来源72保持；不把“有一条桥”记成显式请求或过渡清零。

只对现冻结`transition:T4`、`transition:planned-legacy-routes`、`transition:planned-LegacyRequestAdapter`的notes／introduced_artifacts补本次真实位置、六域／三fine闭包、来源snapshot／Standalone／Unavailable／lease及后继删除责任；它们仍pending。若认为可闭其它保护行，必须先给主控exact frozen ID和验收已完整满足的源码／测试，不能临时新增baseline ID或改original。旧接口删净只是本地API结果，不等于T4的六ambient已删；ApplyEffects可选注入／17构造和新Effect/Main完整实现继续明确后继。

复用迁移工具既有validate/selftest，若没有业务闭合行，不假造preclose特殊豁免。Task Result后strict与原八坏副本拒绝仍必须通过。只写本Task真实增减加法：global8470起点，App4716、Protocol1762、Net199、Host203、Http57、Infra976、Persistence477、Pvf80。新增／删除／合并测试逐项N-D；测试驱动、接口签名、断言变化分别列，不把全库inventory当本轮执行数或预填N。

### 6. 正式冻结核验与交回

共享CommandBus、auth frame与域接口跨度使本单元采用完整Release `--no-incremental` build＋八项目full，native require_escalated／caffeinate、原xUnit类并行及每例deadline／Skip，`--blame-hang-timeout 5m -m:1`。实际每条command、cwd／HEAD、环境、UTC起止／exit／耗时与Darwin资源保存task09独立log/meta。原formal runner/probe只读复用，可用task09前缀薄脚本组织，不改先前日志／脚本／证据。

```text
dotnet build Server/86JP.Server.slnx -c Release --no-incremental
dotnet test Server/tests/Dfo.Pvf.Tests/Dfo.Pvf.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.HttpApi.Tests/Dfo.Server.HttpApi.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

build0warning／0error；只容许原App两条DimensionDungeon失败（Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource、All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，均Dungeon7100／maze0／cell(1,0)／entrance Left）及原18skip。其它失败按AGENTS准确身份／源码诊断，不塞旧SQLite偶发或内容基线。Task07 Host端口观察只保历史，不升级允许失败／声称修复。

新鲜同一个DFO目录，七程序集各唯一正control；旧expected74精确身份／数量不放宽，外／exe／orphan按工具实际观测为0。新故意反例若触发diagnostic，必须先定向实跑精确新class与实际count，明确额外记录依据，不放大旧expected或拼接不同目录；exe0仅观测边界。全Net原Characterization10逐名pass／0skip，Golden15对f01dc226／c57eb0d6实际bytes相同，原codec／帧序／连接断言不改。

冻结后核精确owned physical paths（--no-renames）、其它Server源／旧Task／baseline／A hash不变、旧接口/API/DI alias零、参数闭包／无current wrapper、捕获／来源／闭态及新CMD fail-closed证据。CRLF和混合行尾按bytes定点改，记录BOM／行尾前后；`git -c core.whitespace=cr-at-eol diff --check`和新文件检查过。无新增AsyncLocal／请求jobslot／持久化或执行器算法变化，临时ApplyEffects消费者仍如实登记。

通过产品／正式核验与映射检查后，按verification-before-completion写有界验证结论及Result R1，再跑strict/selftest、source冻结检查和manifest；manifest排除Task全文件并保存R1 prefix bytes/SHA，供主控追加独立review。交回时暂停写，不自批required结果快审、不提交或启动下一Task。单条CMD≤2秒性能验收、效果主单元／17构造链、其它协议及内部来源、T011清零、T012最终核验／Astra／用户收尾仍属后续，不能在本Task冒称完成。


## Controller Disposition — r2 直接入口与未迁子链的生命周期边界

- Decision: `task-rework`；本单元仍交付首批直接slots/rental显式参数，Spec B1/P1中心方案和最终RQ-005要求不变。现计划以r2澄清可验证的局部范围，不把未传cap的下层链写成已保护；无生产实现需要回滚。
- Owning evidence: 主控已读取ItemUseEffectDomainService.UseAsync、ReviveCoinDomainService.TryUseAsync、EntryAllowanceItemDomainService.TryUseAsync、ExperienceItemDomainService.TryUseAsync及OpeningInventoryDomainService.ApplyAsync。前者先委托revive/entry/experience；子服务在规则/facts/progress/quota等await后进入OpeningInventory或ApplyEffects。普通ItemUse也直接调用ApplyEffects。上述子接口/EffectExecutionContext本单元明确只读，其内部尚未得到本次cap。
- Bounded guarantee: 受迁域入口取得同job/本次lease显式能力，结束后再调用该入口必须在业务变更前拒绝；受迁源码自身在实际变更或委托前按r1进行无副作用校验，跨自身await后必要重验。对已经在有效期进入未迁子服务、又被调用方人为不await使其在中途过期的内部执行，本单元不声称已覆盖全部子链写前校验。这里的“委托前验证”不能作为下层await到写入全程已验证的证据。
- Deferred responsibility: EffectExecutionContext必需cap、ApplyEffects/Opening及其17构造和上游贯通仍由下一有用单元承担；实际合法/失效边界到时按 owning source 和原事务/工作单元证据验证，不能用本r2免除父规格最终要求。其它尚未传cap的下层业务委托同样不据本单元冒称中途失效保护；若后继发现超出已定闭包的必要参数/生命周期问题，交主控具体裁定，不私添隐式current或全局tracker。
- Checks retained: 真实旧pipeline slots/rental投影、同singleton两请求/回滚后不串、rejected空slots/rental、owner/closed/同job、未知/新typed桥拒和catch后Write粘性、预取cap在lease结束后调用至少一个实际受迁域入口无业务写，及有效mutation、完整八full/DFO/Golden/数量/范围/strict全部保留。新测试清楚区分“结束后开始调用”与未迁子链中途失效，后者留限制而不伪测全覆盖。
- Readiness: 属P1§6既定有界分段的事实澄清，不是目标被其它工作取代，无需attention回顾、Spec改道或新增独立Task-plan审批。允许按r2恢复；Result和required独立快审必须包含此局部保证与后继责任。


## Controller Disposition — r3 NPC商店原直接handler驱动的明确网络来源

- Decision: `task-rework`；只增补实际漏列的一个App测试文件，父Spec/P1、生产能力分类和本Task直接足迹范围不变。原8个驱动文件保留，此项成为第9个既有App驱动。
- Evidence: 主控已读 `Inventory/NpcShopPurchaseTests.cs:40–50`、`ShopFixture.Buy:489–490` 及BuyItem/Messages.cs。两处通过TestGameplay直接调用BuyItemHandler，再在同job内inline旧bus；请求实际是普通BuyItemRequest（IClientNetworkMessage），不是派生的HousingBuyItemRequest（IEffectSettlementRequest）。它们没有dispatcher建立的网络来源。task-09-directed-existing-01共166例/152pass/13fail/1原skip，13条失败全为NpcShopPurchaseTests同“no explicit footprint source”，包括付款/SQL回滚/送达失败断言之前的来源拒绝；不能据此放宽生产Unknown分类。
- Exact allowance: 增补 `Server/tests/Dfo.Server.Application.Tests/Inventory/NpcShopPurchaseTests.cs`，仅上述首Fact的直接handler body和ShopFixture.Buy body，在现TestGameplay内以原request.Metadata建立明确测试网络NonEffect scope（NetworkCommandAuthorization.Enter，显式MessageCategory.NonEffect；测试authority沿该fixture原有不建真实网络授权的假设为true）。保原请求、handler、sender／故障注入、SQL、状态、期限和并行的全部断言；不改Query/Command/helper其它分支，不给通用TestGameplay授信。
- Rationale / checks: 比改成dispatcher更少改变原驱动语义；真实dispatcher→NonEffect来源另由本Task新桥接行为测试证明。修订后重跑该精确原类与原定向集，后续完整八full仍必须过；原失败日志保留作迁移漏driver诊断，不冒称业务算法缺陷或有价值坏变异RED。若再发现漏调用者，逐个给拥有源码证据，不据此批量改39 marker测试。
- Readiness: 无新生产授权、注册／业务／事务点变更，按r3恢复。Result及required快审须核此唯一额外驱动、原断言保持和真正NonEffect路径；不另加Task计划审批或attention回顾。


## Controller Disposition — r4 幻影塔药水的显式选中效果测试驱动

- Decision: `task-rework`；精确增补 `Server/tests/Dfo.Server.Application.Tests/World/IllusionTowerDomainServiceTests.cs`，只修改Fact `Death_quickslot_potion_use_sends_remaining_then_empty_without_consuming_default_bag` 的handler调用请求状态驱动，不动其它Fact、生产来源分类或父验收。
- Owning evidence: 主控已读该Fact1476～1525、UseStackableRequest声明及ItemUse源。原测试在TestGameplay中直接调用UseStackableHandler，每次原GameMessageMetadata.Empty，没有dispatcher来源/operation；首App full4745＝4726pass＋2原Dimensionfail＋本driver1fail＋16原skip。该request实现IEffectSettlementRequest，不能像r3普通BuyItem那样声明NetworkNonEffect；将它误标非效果不是本次修复路线。
- Exact driver: 每attempt先构造原UseStackableRequest（metadata、slot、InstanceValue25333、ItemCode6521不变），明确Effect类别的测试authorization scope；使用现App IVT内EffectOperationContext.Enter，为该attempt提供独立EffectOperationEntry（同session，ordinal attempt+1，原Empty metadata相应字段、显式测试hash/length）、SelectedSessionSafety.Capture(f.Session, request.Metadata)及request.MinimumReplayNeeds，再调用原handler。实际旧bus45/60/Freeze继续运行，scope逐attempt释放。可用全限定名，仅必要using可同文件增补，不建共享测试平台/新ambient。
- Preserved evidence: 原三次使用、逐帧002C/000E、长度104、quickslot余量/清空及永久背包保留7的全部断言逐行保持；其它setup和Fixture不改。这个手工selected operation只声明该直接handler测试的已知状态，不是实际网络/ledger序号验收；不接入真实ledger来把相同Empty序号的三次独立操作改为replay，也不修改原metadata回避它。真实dispatcher/ledger由本Task新增29例与原Net replay另行证明。
- Verification / readiness: 首轮其余7项目全过（Pvf80、Host203、Http57、Infra976含原2skip、Net199含表征10、Persistence477、Protocol1762），完整进程均已自然结束。r4后仅修本Fact，先定向原Fact，再完整final build与Application full02、最终capture和独立新鲜DFO App目录；产品执行源码保持不变（唯一前述EOF LF清理无语义影响），其它7项目首轮证据可沿用，不能声称均在修改后重复执行。Result保原driver失败、r4真实Effect来源和手工scope边界、所有原断言/限制；失败不增为允许基线或有效mutation RED。
- 所有r1/r2/r3原要求仍在。此为实际漏driver的同Spec局部修订，不改中心方案/Root/A，不需新增attention或独立Task-plan审批。允许按r4恢复后进入required结果快审；不放宽通用TestGameplay或生产来源权限。


## Result

- Revision: 1（Task r4／HEAD c57eb0d6，执行者有界结果，required独立快审待主控）
- Outcome: done
- Changes: 九个直接消费者改必需 IEffectFootprintRecorder 参数：六领域对象经七个接口、Marriage facade／SharedEffect 全 caller 链和三个 fine handler 贯通；旧 IRequestReplayProjectionDomainService／隐式 slots、rental API／DI alias 删除。Housing Purchase、原业务判断／事件／TouchedSlots／Debits和写入顺序保持。没有新消息route、Effect／MainSettlement启用、ledger／事务／执行器算法变更。
- Capture / authority: 只在真实 LegacyFineGrained 最内层、45 BeginMain及60 TransactionStarted之后建立 admission＋percall lease；登记元数据、context引用／ExecutionId及捕获 job ID 一起约束，默认4参ctx／record clone不可伪造。捕获原 frame／execution／rejection 引用，Record／Demand不查当前请求／DI。旧frame快照明确 NoFrame／ActiveNonEffect／ActiveEffect／Closed／Unknown，生产dispatcher按实际消息marker声明。真正offexecutor queue分支经closure传Standalone，闭继承在抑制前拒绝；Unknown保存Unavailable在受迁cap取用拒绝，未对所有39marker全局入口提前抛。新 Protocol／Internal 的 getter／capture／supply／admission 均 sticky Failed，catch后不能Write。未新增ambient或jobslot。
- r2 local boundary: 在受迁方法入口、源码自身写入／委托前无副作用Demand，cap结束后重新进入受迁域会在业务动作前拒绝。已进入而尚未传cap的 ApplyEffects／Opening／Revive 等子链内部 await 后中途失效检查尚未贯通；这包含普通 ApplyAsync，不仅revive三支。17构造／EffectExecutionContext／ApplyEffects optional旧消费保持只读，下一效果单元补完整，父RQ005最终要求不减。
- Drivers / r3 and r4: 原八App驱动只匹配必需参数或每次明确测试lease，不跨job静态UseContext。r3 NpcShopPurchaseTests仅两个Body明确原普通BuyItem真实NonEffect来源，原故障／SQL／权限断言保持。r4 IllusionTower唯一Fact的真实 UseStackableRequest 是 Effect：每attempt明确Effect auth＋独立selected operation测试entry／Safety.Capture／原MinimumReplayNeeds；仍原三次Empty metadata／25333／6521、全部帧与数量SQL断言，未走ledger／伪造真实seq证据。这是手工请求状态驱动，不冒称真实网络／ledger路径；其它该文件bytes和原全部Assert行相同。
- Real behavior / tests: 新29例覆盖同一provider真实dispatcher→旧pipeline→slots／rental→Freeze projection、同singleton两请求和失败重试不串；投影交付stub只观察真实冻结projection，不冒称传输送达。真实NonEffect及offexecutor Standalone合法；未知／缺Effect state、owner错、非main／未txn／frozen、operation／execution／auth／leaseclosed、空slots／rental拒绝、不继承后来的selectedscope、cap跨两个仍active job拒绝、ctx clone／default forgery、新typed四种桥入口sticky均覆盖。8种低层state用实际registration显式manual准备，不当真实pipeline证据。结构反例守必需参数及无ambient构造依赖。
- Test value / RED: 真实 ItemUseEffectDomainService 入口 DemandActive 删除的可编译变异，使已结束cap再次入域时第一个revive业务委托计数由期望0变实际1，单例真实红；finally恢复原bytes／SHA后单例绿。只证明r2结束后再入，不伪测全部子链。见[mutations](../verification/evidence/task-09-inspection/mutations.json)。前置compile／fixture ImmutableArray比较错误不算RED，保原日志；既有定向166=165pass＋1原skip，新增最终29/29。
- Formal checks: 首轮完整Release --no-incremental build01 0warning／0error＋八项目full；App首轮4745＝4726pass＋2原Dimension＋1新Illusion驱动fail＋16skip，r4定向后单例通过。最终完整build02 0warning／0error（17.004s）；最终App03 4745＝4727pass＋2准确原Dimension＋16原skip（103.925s），其余7首轮full Pvf80／Host203／Http57／Infra976（974pass2原skip）／Net199／Persistence477／Protocol1762保持。合8499＝8479pass＋2准确原fail＋18原skip。两fail均Dungeon7100／maze0／cell(1,0)／entrance Left，完整身份在[formal-summary](../verification/evidence/task-09-inspection/formal-summary.json)。Host本轮203pass，无Task07端口fail，不新增允许失败。
- Verification incident: 初版final编排含未必要的6正control，收到主控只保App独立轮方向时仅TERM父PID95317以阻止后继，执行会话也回收App02后代，留下截断log／无native meta。该操作失误使App02无效：父会话143不当App退出码，不补造时间／总计，保留PID／命令／原计划及传播不确定性。已执行定向/build02真实meta保持。App03改独立单步runner自然结束，有真实exit／UTC／耗时meta；未启动额外control，不用改后保存脚本冒充原已加载执行。见[orchestration-adjustment](../verification/evidence/task-09-inspection/orchestration-adjustment.json)。
- Frozen binary scope: 首轮八full用build01初始freeze；删除旧接口遗留末尾1个LF以过diff --check，再加r4唯一testdriver后完整build02。其余所有生产执行代码hash等于initial，原接口声明未变；其它七full沿同语义旧二进制，最终App03为build02。initial／final freeze、EOF前后bytes／SHA和r4精确断言比较都保留；不说首轮跑的是后来不同二进制。未因纯空白重复八full。
- DFO / wire: formal01新鲜目录八full真实七unique controls和74原expected（保首轮新driver故障，未放宽expected）；formal03独立最终Appfull目录62原精确App记录含唯一Appcontrol。两目录分别分析，不拼接／复制、不冒称03七full或完整74。外／exe／orphan均0，exe0只是此次观察；29新class没有额外diagnostic记录。Net原Characterization10逐名pass／0skip，15Golden对f01dc226／HEAD实际bytes相同，原消息／codec／encoder及其它源码未变。
- Accounting / scope: N29／D0，App4716→4745，全局8470＋29＝8499，是本轮八full对应的实际数量；旧测试未删／合。capture02实得173消息／205命令：10Protocol＋1Internal＋194Legacy，Kind166Tx／26Direct／9RO／4W，消息10Protocol＋163Legacy含14遥测。只3个transition的notes／introduced_artifacts更新且仍pending，所有original／其它closed／baseline保持；业务149／旧命令194／source72／五保护／八过渡继续pending，不是Spec完成。
- Source / handback: 精确48物理源码／测试／AGENTS路径，3980预检文件中其它hash保持；原EffectOperationContext Enter→Failed和Record后段bytes保持，ApplyEffects／17构造／45／58／60／ledger／执行器等其它源hash不变，旧接口0／无新增AsyncLocal，行尾BOM／whitespace／最终freeze均通过。Result后strict及八坏副本／manifest见最终证据。单条CMD≤2秒、效果主单元／全部协议／内部来源、T011清零、T012最终核验、Astra与用户收尾均后续；未gitadd／commit／push／PR或启动下一Task，交回主控required独立快审并暂停写。
