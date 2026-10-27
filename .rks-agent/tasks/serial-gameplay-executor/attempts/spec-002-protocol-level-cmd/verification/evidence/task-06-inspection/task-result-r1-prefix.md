# TASK-06 — LOGIN 协议 CMD、频道入场领域与具名部署事实

## Task Unit Contract

- Revision: 4（r1/r2历史保留；以文末真实LOGIN重连合同更正错误的一次性票据假设）
- Outcome and references: 交付父 [Plan P1](../plan.md) T-003 的 LOGIN 闭包，对应 [Spec B1](../spec.md) RQ-001/003～005/007～009/012/014/015、AC-001/002/004/005/007/009：LOGIN 成为薄 handler＋同一总线的非效果协议 CMD；原兑换票据／发布身份的写段显式化；真实频道入场服务吸收已有 listener／种类／准入职责，并通过具名 typed 部署来源取得成功响应事实。保持票据、身份、设置与六 NOTI 的先后和拒绝关闭语义，不把整协议包成一事务。
- Owned boundary: 下文精确生产／测试文件、本Task、B开放迁移账及task-06前缀证据；冻结baseline、已提交Task、父Spec/Plan/契约、A封存只读。不迁名册／选角／其它协议，不启用effect／MainSettlement，不改执行器／总线骨干。
- Required checks: 原真实票据事务断言、新整协议失败与关闭／回放顺序、配置读取时机／短路／快照的区分证据；完整Release构建和八项目全量、原表征／Golden、七control新鲜DFO、注册capture／两行闭合、数量与范围检查。具体接受条件见下文。
- Dependencies: TASK-05（`276a43389c248eb90a07ad82c90ce5de741256ff`，四条Channel已迁、同一协议骨干及8424数量账；间接消费TASK-04普通WorkUnits、Request和登记能力）。
- Result review: required — 本单元改变认证提交边界、原handler编排及真实领域接口／配置接缝；错误可造成已消费票据重用、发布身份丢失、匿名LOGIN失权或disabled部署提前启动失败，纯codec测试无法发现。须独立核实际事务、读取顺序、旧断言迁移和真实线证据后提交。

## Detailed Plan

### 1. 起点与精确范围

核对上述HEAD、原任务分支、干净工作区除本计划、Task唯一身份及已提交依赖。读AGENTS、ArchitecturePhilosophy及RefactorPreferences；当前无.codegraph，按rg／已知路径读拥有源码。继承本地实施／核验／检查点授权，不push／PR／合并／远端修改／部署／清理，不运行df_game_r／DNF.exe；主控负责快审和提交。

允许生产修改：

| 仓库路径 | 最小责任 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/Login/GameLoginHandler.cs`；删`LoginGameUserCommand.cs`，新`GameLoginCommand.cs`；删不再有消费者的`GameLoginConfigKeys.cs` | 薄入口、整个原LOGIN编排、删除生产细命令／result；配置键转由Infra typed来源拥有 |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 仅LOGIN的typed消息／命令接线；旧Activate命令保持 |
| 新`Server/src/Dfo.Server.Abstractions/Channel/IChannelEntryDomainService.cs`；既有`Channel/IChannelSources.cs`仅旧接口的兼容说明；新`Configuration/IChannelLoginDeploymentSource.cs` | 真实领域契约／固定typed来源；不提供任意GetConfig |
| 新`Server/src/Dfo.Server.Data/Channel/ChannelLoginEntry.cs` | environment＋既有PublicGameLoginEndpoints；可带本次解析的ChannelEntryIdentity，不引协议DTO／连接 |
| 将`Server/src/Dfo.Server.Application/Channel/ChannelEntryPolicy.cs`移为`ChannelEntryDomainService.cs`；`ApplicationModule.cs` | 原mapping／Unknown／KindOf／Refuse逐义保留，新增具名LOGIN事实查询，同一singleton的新旧接口别名 |
| 新`Server/src/Dfo.Server.Infrastructure/Configuration/ChannelLoginDeploymentSource.cs`；`InfrastructureModule.cs` | 持同一IServerConfigService引用的惰性方法读取、注册；不缓存第二份配置 |

不改ServerConfigService、PublicEndpointSource的strict实现、现有PublicGameLoginEndpoints record、auth／ticket／OnlineStateRegistry／LoginSessionRegistry算法、旧ClientSettings命令、其余policy生产消费者、Messages／payload codec／encoder、执行骨干。若确有必须越界消费者，先给主控证据与最小增补，不扩大配置或认证重构。

允许测试修改：

- App：`Game/GameLoginTests.cs`、`Authentication/GameLoginCommandTransactionTests.cs`、`Channel/ChannelAdmissionTests.cs`、`Game/SelectCharacterLifecycleTests.cs`；新`Game/GameLoginCommandFlowTests.cs`与`Channel/ChannelEntryDomainServiceTests.cs`。
- Infrastructure：新`Configuration/ChannelLoginDeploymentSourceTests.cs`。
- Networking：`Integration/GameLoginIntegrationTests.cs`、`Integration/BossTowerFrameIntegrationTests.cs`、`Integration/IllusionTowerStageFrameIntegrationTests.cs`。
- Host：`GmPlayerDirectoryHttpTests.cs`、`LauncherAuthenticationEndToEndTests.cs`，后者仅真实登入／构图区分所需。
- Protocol：`Game/CommandRegistrationTests.cs`，仅将旧Redeem自动事务断言转为新route＋真实显式单元证据，保其它名单／坏例。

测试专用内部redemption probe可放在`GameLoginCommandTransactionTests.cs`同文件，仅测试程序集，不加生产注册或任意Func内部CMD。其它setup-only旧调用者用该文件内明确的TestGameplay＋真实domain事务即可，不造新共享测试平台。旧policy接口实现类均可继续旧alias，无需泛改十余个假policy。精确新构造器的直接消费者只有ChannelAdmissionTests中四处及其Policy helper；执行时再次rg验证。

AGENTS仅精确更新LOGIN已opt-in的入口／显式写段／typed领域事实示例及现handler允许项的LOGIN旧描述（当前§端到端示例仍写旧LoginGameUser）；更正“宿主部署配置不是规则输入”段中失真的“三个消费方”表述，按现场IServerConfigService实际注入点说明新Infra来源持同snapshot、DomainService不直接GetConfig。保其宿主配置分类，旧policy仍供其它协议的事实按同实例alias说明，不提前重写最终架构或其它协议路线。B可写开放migration-ledger.json/.md、当前capture确需的两个probe工具，以及本Task和`verification/logs/task-06-*`／`verification/evidence/task-06-*`。baseline和所有original不改。

### 2. 真实频道领域与typed部署读取

现ChannelEntryPolicy只有三依赖：IChannelContentRuleService、ChannelEntryOptions、ChannelListenerOptions。新ChannelEntryDomainService直接吸收它的全部实际职责和实现，不用两个服务各算一套。保普通ResolveAsync／Refuse签名和所有旧结果；新IChannelEntryDomainService可暂继承IChannelEntryPolicy以避免复制签名，旧接口alias到同一具体singleton，不保旧具体类wrapper／三参constructor fallback。旧ChannelEntryPolicyState等DTO不是临时机制，不删。

新增具名ResolveLoginAsync(listenerPort,ct)：先await原ResolveAsync；channel.LoginEnvironment有值时直接使用，只有无值才调用typed来源ReadDefaultEnvironment；随后读取typed endpoints，返回具名事实。它不是GetConfig代理：listener映射、频道分类、SaintHorn最低等级／0xFD及原中文提示都在这个真实领域实例内。

Infra的ChannelLoginDeploymentSource构造器只接收并保存现IServerConfigService引用，不Get、不复制／缓存字典、不用Func／Lazy／ServiceProvider。固定方法仅返回byte环境和PublicGameLoginEndpoints。读取严格按旧GetConfig<byte/int/string>和原键：

1. 环境回退`GameLogin:Environment`仅由前述短路需要时读。
2. `GameLogin:ConnectionPort`的int。
3. `Game:EndpointHost`的原string。
4. `GameLogin:EndpointPortA`的int。
5. `GameLogin:EndpointPortB`的int。

端点构造器Host参数在前不改变上述求值顺序，先用局部变量读取再组record。不要使用IPublicEndpointSource或PublicEndpointConfiguration的IPv4／TCP范围验证；旧int转换可返回尚未在encoder阶段拒绝的值。现IServerConfigService首次解析才建只读快照，Get才验证缺失／转换；adapter不新建ServerConfigService。Launcher enabled专用strict快照保持原注册条件／规则。

普通Resolve／Refuse、服务ctor和invalid LOGIN不得读这些LOGIN字段。DomainService新增typed来源是必需依赖；ChannelAdmissionTests显式提供会在被调用时抛的来源证明普通路径不碰部署读取，不用optional-null或默认来源掩盖缺依赖。完整ProductionGraph仍从Infrastructure得到真实来源。旧IChannelEntryPolicy及别名记入现legacy-routing过渡行，后续消费清零时删除，不能改冻结ID或将它标成已删除。

### 3. 整个LOGIN的显式编排与原边界

新GameLoginCommand携带原GameLoginRequest和精确NetworkDispatchContext，声明ProtocolFrameMetadata.Game；新handler只构造／await一次SendAsync。CMD只注允许的domain能力、ICommandWorkUnits、IGameCommandOutbox、ICommandConnectionClosures和ILogger；不注bus、旧sender／closer、config或policy。全部效果入口仍旧路由，本消息本来是非效果，不加effect marker。

按原顺序搬入CMD：

1. IOnlineUserDomainService按原connection查用户，找不到先原missing_online_registration拒绝。
2. 普通Write单元仅调用IGameLoginAuthenticationDomainService.AuthenticateSessionAsync(onlineUserId,MId,Ticket,ct)。此声明的selected-session参与者为空，但实际事务包含LoginSessionRegistry和OnlineStateRegistry，以及真实认证所用SQLite参与者；不得把空selected列表误写成无参与者。保Prepare／可失败commit→运行态发布→afterCommit superseded清理的原顺序；不改域算法。
3. 写段提交返回后设commandCompleted，再按原OnlineUserId／AccountId／Authenticated重查；无account或未发布分别原类别拒绝。匿名协议的CaptureAuthority返回null是合法的未绑定权威，不是TrustedInternal，也不能在本次发布身份后自动失效。
4. 原LoadSettingsAsync读完，才调用ResolveLoginAsync；invalid ticket、未发布身份、settings失败时部署来源计数必须0。
5. 登记原成功GameLoginResponse，然后原六NOTI：options、hotkeys、auction0、auction1、honor、inform。第二个hotkeys登记后、auction0之前，直接调用IClientSettingsDomainService.ActivateHotkeyProfile，参数保持authenticated.SessionIdentity／account／generation／null character及profile0。不得并入兑换事务或改激活点。
6. 原catch区分OperationCanceledException／其它、redemption／delivery四类别；Warning不带MId/Ticket，TryDisconnectConnection，再向原完整NetworkConnectionContext登记close，ct为None。没有网上真正发送／等待，登记编码失败同样走原拒绝逻辑。

旧Redeem只在生产LOGIN使用，删除它与注册。旧Activate还有SelectCharacterHandler消费者，保持原命令／注册；新LOGIN不经它，开放行只补“LOGIN已直接用域能力、Select仍legacy”的notes，不提前closed。

真实WorkUnits失败会留下粘性失败，CMD执行原Reject后bus仍按既有骨干结束为失败，这是VC-B2执行器记录／来源关闭模型；不能为让旧handler替身测试绿而吞掉ctx failure或修改骨干。已提交之后settings／配置／登记失败不能回滚已用票据、重开认证单元或再次兑换。原已登记条目顺序及关闭保留；新普通单元不启用效果主结算。

### 4. 原测试保留与必须的区分证据

旧细段事务测试与整LOGIN必须分清：GameLoginCommandTransactionTests现要求取消／可失败commit后ticket可被第二连接兑换，第一连接仍Connected匿名。将它们改成整LOGIN后断开会丢掉原阶段断言。采用测试专用typed Internal redemption probe（固定OnlineUserId/MId/Ticket字段，handler用真实ICommandWorkUnits普通Write＋真实auth domain），通过真bus显式测试内部origin驱动；保原匿名状态、Available、原cause、第二连接可用等全部原断言。该probe不进入生产capture，不把业务Func塞进command。另由真实LOGIN线测试验证catch后的原连接断开。

2塔Net与GmPlayerDirectoryHost中的旧Redeem只是setup：改为TestGameplay内真实DomainTransactionScope.Begin→真实IGameLoginAuthenticationDomainService→Commit，保account／generation／后续塔帧和目录断言；不要借整LOGIN制造额外设置帧污染原transcript。GameLoginTests最初旧细handler转发断言转给测试probe或新CMD真实auth调用，thin handler改验原request/context一次交付。SelectCharacterLifecycleTests的重复LOGIN拒绝仍须证明旧selected session留待disconnect observer清理，不能因为旧bus替身移除就删掉它。

新增／迁移的关键可观察合同：

- 正常真实票据单元提交后身份已发布；读取settings时能看见该身份；一条成功CMD＋六NOTI严格原序；hotkeys刚登记时尚未激活，auction0登记时已激活。
- 未登记在线连接／无效ticket、身份未发布、settings抛错分别关闭原来源且不读任何部署字段；日志无secret。
- 成功阶段缺／坏配置只在原响应阶段失败，已消费票据不能第二次兑换，已发布online身份按原Reject清理。channel.LoginEnvironment覆盖时，坏／缺fallback绝不可被读；读取的五项顺序有可观察记录。
- 稀疏配置不妨碍DomainService／adapter构造或普通Resolve／Refuse；转换遵循ServerConfigService，typed来源仍读同一已捕获快照，原IConfiguration之后更改不改变结果；disabled advertised-port／enabled独立listener、Unknown、各kind和SaintHorn原断言全部保持。
- 原真实TCP的成功字节、预提交SQLite／login-session／online-state故障可复用票据、提交后编码／登记失败不复用、关闭与身份清理保持。抓实际原fault探针阶段，不用假sender异常代替真实票据事务证据。

先test-value gate：纯类改名／签名／驱动不造编译RED；至少选一个会逃过普通成功例的真实坏变异，例如将environment回退提前读取，使override＋throwing fallback在明确getter断言红，恢复绿；再选“成功前提前读部署”或“把settings响应失败当可重用票据”的最小变异证实相应oracle。若既有真实失败在实现中暴露并修正同一风险，可复用原红绿，不另造重复变异。每轮独立日志、finally原字节恢复，编译／未建前置／超时不算RED，不改原断言或deadline求绿。

### 5. 数据闭合和当前阶段边界

冻结baseline只读；开放ledger仅按该baseline的稳定ID关闭GameLogin消息行和旧Redeem command行；给原单段Tx→新普通Write、LoginSession／OnlineState／SQLite实际参与者、原afterCommit与后续无事务设置/激活/登记位置逐项证据。旧policy alias记现过渡行introduced_artifacts/notes；Activate旧行继续pending且记共享caller去向。其它已closed行和所有original不变。

当前capture只ConfigureServices，不启动业务；若新旧command各换一条，合理预期消息173／命令205，command route5Protocol＋1Internal＋199Legacy，Kind168Tx＋24Direct＋9RO＋4Workflow，以实得逐条双向核对为准，不硬填。完成后业务154、旧command199、来源72仍pending；不是T003或Spec完成。

沿用精确当前Task preclose→产品／映射检查通过→写Result→严格validate/selftest的顺序，保原八坏数据拒绝，不能修改旧Task或借其done冒领新行。typed adapter／新domain tests的N/D依实得计，相对8424逐项闭合；驱动改造与原断言迁移分列，删除旧DTO类型断言有准确新去向，不把这些当删业务测试。

### 6. 正式核验与交回

本次新增真实领域接口、Infra来源和App注册别名，影响十个旧policy生产消费者以及认证／Launcher／塔setup，采用完整Release构建＋八项目全量。native require_escalated/caffeinate，原xUnit类并行、原每例deadline／Skip，`--blame-hang-timeout 5m -m:1`；所有轮次独立task-06日志和证据，保存cwd/HEAD、实际命令、UTC起止／exit／耗时和实得Darwin资源。

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

构建0warning／0error；八项目起点Pvf80、App4683、Host202、Http57、Infra970、Net199、Persist477、Protocol1756，总8424。仅App的`DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`与`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，均Dungeon7100／maze0／cell(1,0)／entrance Left的原内容错误，以及原18skip为接受基线；其它失败按拥有源码根因诊断，不塞既有SQLite偶发／内容基线。保持F88/F93四守护和F132禁用判据，未启用Compose。

七程序集同一新鲜DFO目录，各唯一control；沿Task04最终74记录／原expected：App62、Net7、其余五各1。新测试不预设新增违规；若确为故意反例，先定向实跑精确类／数量再说明追加依据，原类数不放宽。外0、exe0、orphan0；exe0只是该取证观测。完整Net内原表征10逐名通过／0skip即可不额外重复；15Golden对f01dc226与Task05无diff，原payload codec／线序／连接断言保持。

最终核对精确白名单、其它源／已提交Task／baseline/A hash不变、old Redeem源码引用0、新CMD构造器与薄handler判据通过、没有旧sender/config/policy注入。旧policy接口和Activate尚有具名合法消费者，不误记清零。CRLF／混合文件用bytes定点修改，存行尾/BOM前后；`git -c core.whitespace=cr-at-eol diff --check`及新文件检查通过。Task04scope双故障残余在本新LOGIN selected参与者空且没有新增scoped disposable组合，实际identity参与者仍在domain事务内；若执行发现相关组合触及，报告主控核实际影响，不趁机扩修。

源冻结后完成所有受影响检查，再写Result R1并补strict，列实际改动／重要选择／N-D／原断言与驱动／全量和DFO／公开剩余责任，交主控required独立快审与本地检查点。完整性能与2秒接受线、其它功能簇、T011清零、T012最终核验及Astra／用户收尾门均不在本单元冒称完成。


## Controller Disposition — r2 真实 integration setup 的事务可见性

- Decision: `task-rework`；同Task未提交、Spec/P1及Login边界不变。
- Evidence: 两塔Networking setup按r1由旧Redeem改TestGameplay内真实DomainTransactionScope.Begin时，编译CS0122。主控已读 `Server/src/Dfo.Server.Application/Dfo.Server.Application.csproj`，当前IVT含Application/Host/Protocol/Infrastructure/Persistence测试程序集，确无Networking.Tests；前构建属机械签名/可见性诊断，不计行为RED。
- Amendment: 精确增补该csproj，仅一行 `<InternalsVisibleTo Include="Dfo.Server.Networking.Tests" />`，使已授权Networking integration setup能使用真实事务边界。不新增项目引用、不公开事务类型、不增加生产setup-only CMD或反射调用，不改变运行时服务/算法；原项目引用层级保持。
- Checks / readiness: 原完整Release/八项目/DFO及旧票据阶段、两塔线级断言全部保持，required快审包括此唯一测试可见性变化。此为r1既定真实测试的最小编译期接缝，无新Task依赖或待用户决定，允许恢复实施。


## Controller Disposition — r3 保留真实 reconnect／supersede 合同

- Decision: `task-rework`；父Contract/Spec/P1仍有效，其相对A行为保持要求不变。更正本Task局部错误验收，不改认证算法或旧断言，不终止Spec。
- Direct evidence: 主控已读取未修改的 `GameLoginIntegrationTests.LoginDeliveryFailureAfterCommitCleansIdentityAndAllowsReconnect`（315、371–388）：第一次成功兑换后交付失败，清理身份／连接；新TCP仍用同一MId/Ticket，要求收到成功LOGIN响应。`LoginSessionRegistry.TryReserveBinding:180–203`不拒绝已绑定session，保留有效期内reconnect/supersede。父Spec无“一次性LOGIN票据”要求；效果账本禁止重复结算不可套到非效果LOGIN。
- Correction: r1中“提交后票据不能第二次兑换／不能复用”等说法是没有源证据的误记，全部由本裁定覆盖。正确结果是：单次LOGIN的认证写段已经提交，后续settings／配置／登记失败不回滚该提交，不在同一次CMD里重开认证单元；按原Reject清理已发布身份／关闭原连接；下一连接按现有有效期及reconnect/supersede规则可再次使用同MId/Ticket。保原真实Net重连断言，不能为错误新测试改成一次性凭据。
- Checks: `task-06-directed-app-01`中两条“不能复用”新flow失败是错误验收假设被源码证伪，保日志但不计实现缺陷RED。只调整本Task新增未提交oracle：记录settings读取时身份已发布、认证单元已结束、单次CMD Authenticate/Write仅一次；响应阶段失败后无回滚／第二认证单元；后连接按原规则重连／顶替。新测可用一过性settings／配置失败来源使第二次进入正常路径。重要事实在catch内部应先记录、在CMD外断言，避免Xunit异常被LOGIN catch吞掉后误判成功。
- Readiness / scope: 原预提交事务回滚可重连、匿名状态、配置短路／顺序／snapshot、原成功帧与6NOTI／激活点、准确四拒绝分类和所有完整核验保持。新typed部署／Domain实现无需为本事实改变；原auth/ticket/Registry、Net旧断言及其它已提交制品保持。required快审独立检查此合同更正与新oracle，记录为本Task执行中纠正的错误假设。无用户决策或新Task依赖，允许恢复。


## Controller Disposition — r4 按实际参与者更正，及关闭链的命令身份

- Decision: `task-rework`；只纠正本Task局部未经证实的事实和迁移所需测试标签。Spec B1/P1的行为保持、原边界不变；A不重开。
- Actual participants: 主控读取 `AuthenticationDomainService.BindSessionAsync:37–52`、`GameLoginAuthenticationDomainService.AuthenticateSessionAsync:66–76`、`LoginSessionRegistry.Participant:136–148`、`OnlineStateRegistry.PrepareAuthentication:39–47`：普通LOGIN兑换/发布单元仅加入LoginSessionParticipant与AuthenticationParticipant两个运行态参与者，没有SQLite enlist。注册账号、Launcher验证/发票据的持久化段与后续settings读取不是该单元的SQLite写参与者。r1中“SQLite＋两个registry”“实际SQLite认证提交”等声明均由此覆盖；不得为迎合错误清单增加SQLite、扩大事务或修改认证算法。
- Failure evidence: 旧 `GameLoginCommandTransactionTests.DurableCommitFailureLeavesTheTicketAndIdentityReusableByASecondConnection` 使用 `includePersistence:false`，其 `CommitFailOnceAuthenticationDomainService.TicketParticipant` 故意声明CommitCanFail并抛“database commit failed”。这证明可失败参与者提交前的排序/回滚保护，不是实际SQLite commit失败取证。保原测试名称、cause和全部业务断言；实际SQLite fixture的存在不等于普通LOGIN单元加入SQLite。
- Mapping/docs correction: 允许executor将当前未提交migration-ledger、task-06-mapping证据及AGENTS中的参与者声明改为以上真实闭包；原日志和original源码快照不改。Result逐条记录本次纠正，不把两runtime事务写成无事务或无参与者。r3对重连/顶替的更正照常成立。
- Exact test allowance: 增补 `Server/tests/Dfo.Server.Networking.Tests/Integration/LifecycleExecutorWireTests.cs`，仅将 `A_login_refused_by_the_real_handler_closes_the_connection_and_cleans_up_on_the_executor` 的parentHop名称期望从`inbound:GameLogin`改为`nameof(GameLoginCommand)`（若缺using仅增该精确namespace）。真实首轮FIN、cleanup与执行器断言已过，唯一失败是旧细路径身份；新协议CMD身份属于VC-B2。保Source/Root/Parent/Depth、顺序、FIN和deadline等断言，不改链生成/关闭生产代码。
- Verification / readiness: 首轮完整build0/0；八项目8446＝8425pass＋2原Dimensionfail＋1旧链名称断言失败＋18skip。仅以上文档与单一期望标签修正后，完整Release build及Networking全量fresh02、DFO独立control/原正对照核验；其余七个未改变程序集的首轮结果可沿用。保持全部原日志、数量N22/D0和表征/Golden检查。局部attention独立诊断在进行，完成后由主控追加并检查纠正条件，再进行required Task结果快审；不增加第二次例行attention审批。


## Independent Attention Review — TASK-06 r1 局部验收假设偏离

```yaml
kind: rks-review
verdict: changes_required
```

Reviewer: fresh independent `b_task06_attention`；HEAD276a4338上未提交Task06 r1～r4与实际输出；已读reviewing-work/Attention及完整根/子契约、root map、Spec B1、P1、Task06和AGENTS。本次仅诊断attention incident，不代替Task结果快审或Spec完成判定。

确认：B要求保原认证、重连及实际事务边界，r1详细计划却加入无源码依据的一次性票据、SQLite参与者两条件。原GameLoginIntegrationTests.LoginDeliveryFailureAfterCommitCleansIdentityAndAllowsReconnect与LoginSessionRegistry.TryReserveBinding支持同MId/Ticket有效期内重连/顶替。task-06-directed-app-01的两新flow错误oracle失败只能证明原假设错误，不能计实现RED。BindSessionAsync只格式检查及sessions.TryBind，GameLoginAuthenticationDomainService另PrepareAuthentication；正常段两runtime participant都CommitCanFail=false。旧DurableCommitFailure测试includePersistence:false，由测试TicketParticipant注入可失败提交及文本，不是SQLite commit证据。

起点：最后确认aligned是Spec B1行为保持/RQ007/AC004及P1§1/§4按实际事务迁移。最早可证偏离为Task06 r1 Result review理由及§3.2/§4/§5的两项误记；属于局部计划先错、执行与证据沿用，未见当前认证算法/registry/旧GameLoginIntegrationTests diff。对此文档层起点置信高，但无足够记录证明更早由哪个研究/决定产生，不追溯断言Task04/05同类偏离。

最小纠正：r3/r4足够，不改Spec/P1、不重开A/新建子Spec。新oracle验证当次认证只一次、已提交不撤回/重开、精确清理后下一连接原规则重连；将普通段与注册/Launcher SQLite、后续settings读取、测试故障参与者分清。更正现ledger/map/AGENTS，原日志/原源码快照/r1历史保留并以裁定覆盖。LOGIN例子只经bus入队一次。Lifecycle parentHop只改新CMD身份属VC-B2，保FIN/清理/链关系/期限。

主控可检查条件：当前flow在CMD外断言已发布身份、事务结束、认证次数和实际断开后重连，directed-app-03为77/77；现ledger、mapping、AGENTS已改两runtime参与者/单入队。确认最终无普通兑换SQLite宣称、旧断言/算法保持、build/Net02及原错误日志如实记录后，从Result整理及required独立快审恢复，无须例行第二attention审批。出现相反证据才重开。

## Controller Disposition — attention纠正条件核对及恢复

- 主控已完整重读治理文件并消费上述独立诊断。两错误声明的最早可证点/影响及局部恢复方式已向用户说明，未冒称发现更早历史起因。
- 已读当前GameLoginCommandFlowTests及task-06-mapping/workunit-map.md、ledger GameLogin行、AGENTS353/525～537；其认证一次/事务结束/真实断开后同凭据重连与两runtime参与者说明符合r3/r4。git diff确认Authentication/OnlineStateRegistry及原GameLoginIntegrationTests无修改；未为错误oracle增加一次性凭据或SQLite。
- executor报告原native完整轮8446闭合，final完整Build02为0warning/0error，Net02为199/199，首轮七control74条与finalNet7条精确expected/外0/exe0/orphan0；Task结果须引用这些原日志，不删首轮旧标签失败或错误oracle记录。其余七个测试程序集无后续代码变化，按r4沿用首轮。
- 上述最小纠正已解决本attention发现，允许整理Result R1及manifest并进入单元required独立快审。此裁定不是最终Spec或Task结果批准；不重复attention审批，不改变父验收/授权/中心方案。


## Result

- Revision: 1（对应r1详细计划及r2/r3/r4、attention恢复裁定；r4事实覆盖r1误记）
- Outcome: done
- Changes: LOGIN薄入口只带bus并以原request/context一次交付GameLoginCommand；新ProtocolNonEffect CMD唯一普通Write调用原认证域。原Redeem生产命令/result/注册及无消费者配置键删除；ClientSettings旧Activate因Select caller仍保留。真实ChannelEntryDomainService直接吸收原listener/Unknown/KindOf/Refuse，新增ResolveLoginAsync并同singleton兼容旧policy。Infra具名部署来源保存同一IServerConfigService只读snapshot引用，构造无Get、原类型/读取顺序/短路/非strict语义保持。AGENTS精确更新LOGIN opt-in与真实入队/配置/参与者说明。
- Boundary evidence: 普通兑换实际仅有LoginSessionRegistry与OnlineStateRegistry两个运行态participant，均CommitCanFail=false；selected-session声明为空不表示无参与者。生产没有该段SQLite enlist。注册/Launcher的SQLite、后续settings读取及测试故意可失败TicketParticipant均分开记在[映射](../verification/evidence/task-06-mapping/workunit-map.md)。原prepare→runtime commit/publish→afterCommit顶替清理不改；提交后精确身份复查→settings→typed部署→1CMD＋6NOTI、hotkeys后profile0激活保持，后段不再开认证单元。
- Assumptions corrected: r3纠正一次性票据误记，保原同MId/Ticket有效期内reconnect/supersede；r4纠正SQLite参与者误记。task-06-directed-app-01两条错误oracle红保留，不当实现RED；auth/registry/原TCP重连断言零改动。独立attention及主控恢复裁定见本Task上文。新增flow在CMD外断言已发布身份、事务结束、每次认证计数；一过性settings/config失败后CaptureAuthority仍存在、真实Disconnected后原凭据在第二连接成功。未为错误清单改领域算法。
- Existing assertions and drivers: 原GameLogin单位7消息/同连接/拒绝/secret/关闭断言迁至新CMD驱动；旧细命令result类型删后，参数转交由新CMD实际调用验证，真实发布身份另由flow验证。原取消/可失败commit细段测试用测试专用fixed-payload Internal probe，保Connected匿名、Available、原cause及重试；不是整LOGIN断线替代。两塔及GM纯setup改TestGameplay＋真实DomainTransactionScope，不新增设置帧，Application.csproj仅r2一行IVT。r4生命周期测试仅祖先expected标签改nameof完整类型，其余关系与FIN断言保持。
- Test value: 两轮独立坏变异：eager fallback使override2例真实getter红，恢复2/2绿；部署提前到settings前使外部读取计数2例红（其余3通过），恢复5/5绿。finally恢复原bytes及SHA，见[mutations](../verification/evidence/task-06-inspection/mutations.json)与同名原log/meta。机械编译和新增fixture前置错误不计RED。最后受影响App定向77/77、新Infra来源6/6。
- Checks — formal: 最终task-06-formal-build-02完整Release --no-incremental，6.341s、exit0、0warning/0error。八项目选定结果Pvf80pass；App4699＝4681pass/2原Dimensionfail/16skip（117.663s、exit1）；Host202pass；Http57pass；Infra976＝974pass/2原skip；Net02 199pass（55.419s、exit0）；Persistence477pass；Protocol1756pass。共8446＝8426pass＋2准确旧fail＋18原skip，N22/D0（App＋16、Infra＋6）。仅Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource与All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，均原Dungeon7100/maze0/cell(1,0)/entrance Left内容错误。原完整Net01的一条旧label失败保留，未塞基线；r4后仅该expected和文档变化，完整build＋Net02重跑，其余七程序集依裁定沿用首轮。原class并行/skip/5mdeadline/native+caffeinate不变，详[正式汇总](../verification/evidence/task-06-inspection/formal-summary.json)及原log/json。
- Checks — DFO/wire/data: fresh01七程序集唯一controls/74精确原expected；fresh02仅Net唯一control/7精确原expected，两个目录各外0/exe0/orphan0且不混合。Net02包含表征10逐名通过/0skip；15Golden对f01dc226与276a4338零diff，原codec/帧序/真实TCP连接断言保持。capture实得173messages、205commands＝5Protocol/1Internal/199Legacy，Kind168Tx/24Direct/9RO/4W；不含测试Internal probe。开放ledger仅新闭GameLogin/Redeem两行，oldpolicy alias及Activate去向仍pending；所有original/其它closed行/baseline/A/其它源不变。[最终source checks](../verification/evidence/task-06-inspection/source-checks-final.json)核28物理路径/3删除、行尾/BOM、旧Redeem/键引用0、构造器/薄入口/唯一Write与whitespace；未触及混合行尾文件。
- Checks — closure: 最终preclose已核当前TASK-06及真实映射；Result后strict有效且completion_results_pending为空，八坏副本selftest全部拒绝。manifest绑定最终owned SHA与R1 Task字节前缀，排除Task全文件供后续独立review追加。执行器未自批required review或提交。
- Limits / remaining: 正常兑换的两个runtime参与者不是SQLite持久提交证据。Task04 scope双故障组合没有因本LOGIN新增selected/scoped-disposable组合而触及，未改骨干。业务154/旧命令199/来源72及5保护/8过渡仍pending，T003/Spec未完成；TASK-07未启动。性能2秒、其它功能簇、T011/T012、Astra审核与用户收尾门仍在后续范围。无commit/push/PR/部署/外部发送。
