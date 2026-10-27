# TASK-66 实现与验证记录

状态：具体 CMD 结构实现及正式 fresh Release / 八项目串行执行结束；主控负责最终统计、验收及后续 G1。本文不声明 G1 通过。范围仅 B10；未触及 G1、主干、PR、tag、issue、部署或清理。

## 源码绑定与结构

- 基线 a7f47c1，工作区 `.claude/worktrees/refactor-session-20261021`。
- READY 源码哈希：`2cd7b909da3d58d1166df489ba20e25b360ef9dae47c3c287a4c06f403b5c67c`。定义和完整2862项（含删除标记）见 `logs/task66-manifests/task66-ready-source-manifest.json`；153个变更路径=93生产+60测试，23新增、15删除。相同 manifest 的 `changedPaths` 是完整增删改清单；不只统计 git tracked 文件。
- 共享 CMD 基类及其工作流继承已移除。单位顺序、拒绝/补偿、两塔及普通路由回到具名具体 CMD；共享部分限固定协议阶段、受众/值计算。TownArrivalProjection 是含领域状态调用的固定城镇到达阶段，不冒称纯函数；不接受 execution/request/units 或任意动作回调。
- 59个原生产 leaf virtual 已移除；测试不再继承 Game*CommandHandler。直接阶段测试所需既有算法入口改 internal，其余 private；没有为测试增加生产接口、成功开关或覆盖钩子。
- 构造判据从实际 Application/Protocol 实现扫描，并将 ProtocolModule 注册的 protocol/internal handlers 与扫描集合互核。保留具名能力豁免理由；继承/nullable/default-null/delegate 四种坏例各按真正原因拒绝；非空领域依赖、ICommandWorkUnits 和 nullable 业务输入区别验证。新增7测试case，预期全量9817=9797pass+2既有Dimension失败+18既有skip；须以最终实际记录为准。
- 已核的具体CMD缺依赖回落/可选调用按其真实必需字段清理；这不是Protocol全库?.语法零命中声明。OnlineUserConnectionObserver保留两个clientSettings?.，但构造器必需非空，正常DI下两次调用均必达，不存在生产null填空；Nullable session、run、award、业务数据和失败结果仍保留。生产 `null!` 两处是 Try 模式 out 业务结果初始化（PartyCharacterCapture 与 Card run），不是缺注入/依赖回落。

## 回调及可见性边界

| 位置 | 当前分类与处理 |
|---|---|
| CreatureVisibleRecipients | 只保留 Resolve 查询；timer 与 Rename 各自持有登记/隔离/关闭循环。timer 保留 ProjectionPhase.Registering 的原窄失败分类及 ResolveAndRequestClose；Rename 仍由在线查询取得 physical context 后精确关闭。取消、逐目标隔离及 ownerDelivered 不变。 |
| Boss/Illusion Entry | 外传 Action restoreIntent 删除；同一具体 CMD 私有 RestoreEntryIntent 在三个原拒绝点按原顺序校验 current 并恢复意图。测试在既有 entry-intent domain seam 观察。 |
| Selection CleanupExpired(Action<Exception>) | 仅把该次原 cause 交回局部变量，用 ReferenceEquals + Request.HasOnlyFailure 识别原拒绝路径；不封装领域/发送/关闭能力。保留以避免改变异常边界。 |
| Marriage/TowerLoading Func | 具体 CMD 内显式 ICommandWorkUnits action，不是构造器依赖、跨 CMD 协调器或可替换工作流。 |
| coordinator StartDetached Func | 队外 timer 启动机制；不充当可覆盖业务阶段。 |
| test-only ProductionSelectionStages/EntryUnits | 既有 standalone 算法观察入口仍可用 InlineUnits action(null)/未触达依赖占位。它们不证明真实 work unit、完整 admission 或生产事务，不与下面真实路径混写。 |

## 测试接缝的证明边界

- LifecycleWorkflow 现组合真实 sealed Selection CMD；原 preparation/inventory/session-entry/quest/pet/cleanup 域实例保留。SelectionUnits 仅在具名单位记录阶段后委托 ProtocolAlgorithmFixture.Units 执行真实 action；后者有本地 DomainTransaction/footprint，但不是生产 CommandWorkUnitRunner、完整 request admission 或 SQLite 验证。发布/Ready/party观察在原领域查询边界，未复制完整阶段。
- GroundPickup、CardAlgorithms、ObjectDestruction、DeathAlgorithmDriver 调具体真实算法；Death 的 settle-monster-death 委托原 domain + 原 scope transaction/footprint，不返回假结算。
- 获准的旧结果投影组件 GetItem/PickupResultUnit、Card/ClearResultUnit 仅以精确单位名及结果类型提供旧 fixture 结果；未知单位/方法拒绝。只证明结果后的真实 CMD 入场、受众、顺序与投影，不证明领域结算。没有修改全局 Units 为 spy，也没有把 Golden、Characterization、真实 native 事务/real-wire 转成 spy。
- 真实结算/事务覆盖仍由 Application 的 DungeonPickupTransactionTests、CommandWorkUnitsTests、KnownRollbackWorkUnitTests、CommandSelfSettledWorkUnitsTests、OneDatabaseOneParticipantTests、GameLoginCommandTransactionTests，以及 Networking Characterization/real-wire 负责；本轮仅在正式八项目完成后报告其运行结果。
- ReturnToSelection 的两个送达失败用例仍使用 XoDelivery 真登记/运输失败；改为真实 CMD + 当前 selected session + 域 cleanup 计数。旧断言保留。

## 已裁定的夹具覆盖增强（非产品行为变更）

1. LeaveDungeonTests：Leaving_ends_the_run_and_projects_the_town、Leaving_without_a_run_is_still_answered_and_still_projects_the_town、BackToVillage 对应普通返回用例补合法城镇在场/意图与当前弱化37；保原 ACK/三条 town 字段次序，另核末尾唯一弱化。GiveUp 的既有测试继续守 ACK→弱化→town；TowerReturn 固定不投影弱化。
2. BossTowerProjectionConcurrencyTests.Give_up_outside_a_tower_runs_the_ordinary_path_once_and_answers_before_the_town：真实 CMD 走普通路由，旧 NoTown/塔发送器不再覆盖显式 town/sender 依赖。现核恰五条 ACK、Weakness(0)、UserState、UserArea、AreaUsers，当前坐标100/100、Town/Area1/1、唯一当前wire id；每次发送都断言准确owner且无旁观目标。原 ACK唯一/非tower PartyCalls=0 断言保留。
3. PartyMoveMapHandlerTests：Normal_room_diagnostics_are_debug_only_and_do_not_change_delivery(Debug/Information)、The_leaders_room_change_is_sent_to_every_frozen_current_generation、A_replacement_connection_does_not_inherit_the_old_runs_room_change、If_the_requester_is_replaced_after_commit_the_room_reaches_remaining_members。真实疲劳读数156/17/23；每个合法当前目标恰两条 StartMap→Fatigue，原 StartMap 字段、拒绝/替换目标空集断言保留，不给过期目标增加消息。
4. CardRewardTests.The_scroll_acknowledgement_is_followed_by_the_layout 和 ResultScreenTests.The_scroll_state_is_acknowledged：补实际 settled run、固定cards及初始closed布局；同一次 ScoreScroll 经过真实 OpenLayout，原 ACK→layout 两帧断言保留，加开布局/调度断言。ResultScreenTests.The_scroll_state_is_acknowledged_without_a_published_session：按原必需control的真实行为改为恰一条ACK；删除仅测试null control触发的不存在layout。
5. SelectCharacterLifecycleTests：a7f47c1 构造及 ApplicationModule 注册证明 Tau/Southern/Boss ticket、client settings、weakness、quota 为真实DI能力。fixture补明确域准备，所有原阶段/字段断言保留。WithSupplementalStages 在原完整 ledger 显式插入 weakness、premium后的七个quota；实际 Published 全保留。AssertSupplementalDelivery 验连续九段尾部、弱化唯一/目标/当前值0、七family次序/每项准确session/与先前通知完整concat，然后再对旧Town/Pvp通知做原断言。无静默过滤。r19覆盖36/36。
6. PartyCommandHandlerTests：原测试以缺塔协调能力省略了boss与illusion两次 current party查询；组合真实CMD后在原完整调用序列保留这两次只读查询。邀请/接受/离队/换队长/踢人/拒绝/设置及帧断言未删。
7. DungeonLoadingProjectionDeliveryTests 将已废弃的 ReadyCommandBus 观察移到原 Runs.TryMarkParticipantReady；原次数和投递断言不变。EntryHandlerDriver 在现有 Hell/Mirror/Boss/Illusion 域接缝转发原具名测试输入，不再只有普通Dimension路由携带旧结果。

## 原生验证记录

全部实际 dotnet build/test 均由 require_escalated 原生调用；不以 sandbox拒绝、超时或脚本起跑状态当通过。历史一次auto-review超时在启动前重试，无测试超时/并行设置修改。没有人为终止 testhost。

- 最后增量编译 solution57：Release --no-restore，自然退出0，0warnings/0errors。
- r9 Return 2/2；r11 Pickup 61/61；r13 Card 246/246；r16中Death/Loading已通过，5个MoveFatigue夹具省略随后由r17修复；r19 Selection/Revival36/36。
- r20最后受影响集合309=296pass+13fail：5个Creature fixture传null tower、7个Entry fixture Hell/Mirror结果未接真实域、1个GiveUp fixture自动抽取覆盖了正确sender；均为明确夹具缺口。r21对受影响三组96/96；r22新增完整GiveUp断言1/1。其余r20通过的集合未因相同内容重复跑。
- 所有 r1–r22 的 TRX/console/run.json/DFO 位于 logs，相应实际计数见 logs/task66-manifests/task66-focused-results.json；保留失败轮，不以最后焦点数替代全量。
- 最终正式构建位置 logs/task66-final-r1-build；八项目分别 task66-final-r1-{application,protocol,networking,persistence,infrastructure,host,httpapi,pvf}。运行脚本/CPU墙钟/RUSAGE_CHILDREN高水位/真实退出码留存，RUSAGE_CHILDREN不是同时驻留子进程RSS求和。正式结果、DFO、Golden/Characterization、冷启动、吞吐、GC/内存和基线回归判断待实际完成补记。

## 测试文件逐项清单

下表与 READY manifest 的60条测试路径逐项对应；具名增强与证明边界以前述条目为准。机械构造/using调整不等于增加测试覆盖。Golden与Networking Characterization源码未修改，正式运行仍待验证。

| 路径 | 变化说明 |
|---|---|
| `Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs` | 真实注册覆盖+结构坏例/正例，新增7case；既有类型后缀判据与豁免理由保留。 |
| `Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj` | 链接共享test-only夹具，生产引用图不变；不改变测试并行或跳过。 |
| `Server/tests/Dfo.Server.Application.Tests/Game/SelectCharacterLifecycleTests.cs` | 真实sealed CMD组合、领域与unit观察；补充投递完整序列及当前目标断言（条目5）。 |
| `Server/tests/Dfo.Server.Application.Tests/Game/SelectionStageBehaviorDriver.cs` | 旧standalone阶段直接调用真实算法；只调整可见性/完整构造；保留InlineUnits局限。 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/ItemUseFixtures.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Application.Tests/OnlineUsers/OnlineUserServiceTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Application.Tests/Support/CardAlgorithmsDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Application.Tests/Support/DungeonEntryRouteDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Application.Tests/Support/GroundPickupAlgorithmDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Application.Tests/World/DungeonRevivalTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj` | 链接共享test-only夹具，生产引用图不变；不改变测试并行或跳过。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj` | 链接共享test-only夹具，生产引用图不变；不改变测试并行或跳过。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/BossTower/BossTowerEntryOrderTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/BossTower/BossTowerProjectionConcurrencyTests.cs` | 组合实际CMD；唯一普通GiveUp增强五帧/字段/目标（条目2）；并发断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/BossTower/BossTowerPublicHandlerTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CardReward/CardRewardTests.cs` | 补settled run与实际调度，保同次ACK→layout；其余原字段/次数断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CharacterDeath/CharacterDeathHandlerTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CharacterDeath/DeathProjectionDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CleanupNotificationDeliveryTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/Creatures/CreatureProjectionDeliveryTests.cs` | 原真实登记/运输故障用例仅补显式无tower及非空unused units；原断言全部保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/DungeonClearProjectionDeliveryTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs` | 真实退出CMD组合；合法在场准备与弱化完整序列（条目1）；真实bus/transaction部分保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/ResultScreenTests.cs` | 同次请求真实settled run布局；无session恰一ACK裁定（条目4）。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/DungeonSelectionTestDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/EntryHandlerDriver.cs` | 真实Entry CMD组合；普通/Hell/Mirror与两塔原域接缝；restore在entryIntent域观察。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/LoadingAlgorithmTestDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/PartyMoveMapHandlerTests.cs` | 当前run查找及疲劳准备；五个success case增强完整房间/疲劳序列（条目3）。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/PartySelectDungeonHandlerTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonLoadingProjectionDeliveryTests.cs` | ReadyCommandBus替换为原Runs.TryMarkParticipantReady观察（条目7）。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/GroundPickup/GetItemHandlerTests.cs` | 按批准的精确结果单位spy组合实际CMD；保原投影断言，不宣称结算证明。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/GroundPickup/PickupProjectionDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/GroundPickup/QuestClearVerticalTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/IllusionTower/IllusionTowerProjectionConcurrencyTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/InitialConnectionDeliveryFailureTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/Joust/JoustQueryHandlerTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/MonsterDeath/MonsterDeathHandlerTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/MoveItemProtocolTestDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyCommandHandlerTests.cs` | 完整真实CMD代替覆盖；增加两塔current party只读查询观测（条目6）。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyMutationTestDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyNotificationTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyTownProjectionDeliveryTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/ReturnToSelection/ReturnToSelectionDeliveryFailureTests.cs` | 实际CMD准备/完成，域cleanup计数，XoDelivery原真实故障断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/ExpiredItemsProjectionTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/MarriageChurchTestDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/SharedTownMovementDomainService.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/TownAreaProjectionDeliveryTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/TownArrivalProjectionDriver.cs` | test-only固定town阶段驱动；不复制CMD，不宣称全流程。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/TownMovementTestDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Support/CardProtocolDrivers.cs` | 实际CMD入场/受众/投影；精确Card/Clear结果spy与显式真实run准备；未知单元拒绝。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Support/CardSelectionProjectionFixture.cs` | 更新必需构造依赖/领域准备或已移动固定投影入口；原断言保留。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Support/DungeonExitDrivers.cs` | 实际Exit CMD组合；显式current/town/sender优先于独立塔fixture依赖。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Support/DungeonProjectionParty.cs` | 固定已有run参与者的只读party事实；未知操作拒绝，无生产用途。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Support/GroundPickupAlgorithmDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Support/ObjectDestructionAlgorithmDriver.cs` | 改组合具体CMD/真实既有算法；完整领域准备与既有测试接缝；无生产override。 |
| `Server/tests/Dfo.Server.Protocol.Tests/Support/SettledCardRun.cs` | 真实DungeonRunDomainService创建clear/fixed cards/settlement，初始layout closed。 |
| `Server/tests/Support/DungeonClearProjectionDriver.cs` | test-only具体CMD真实clear算法驱动；不宣称生产request/事务。 |
| `Server/tests/Support/ProtocolAlgorithmFixture.cs` | test-only构造/域默认；未声明行为拒绝，Units执行真实action+本地事务；不等同生产runner。 |
| `Server/tests/Support/TowerAlgorithmDrivers.cs` | 以真实CMD组合与既有塔域装饰接缝驱动；不继承/覆盖工作流。 |

## 正式构建前纯空白收口

主控runtime capture在21文件34处纯空白修正前完成；修正仅限git diff --check报告的行尾空白及新增EOF空行，无token/逻辑变化。精确位置保留/private/tmp/task66-whitespace-fixes.json。初次fresh build task66-final-r1-build自然退出0；修正后另跑task66-final-r1-build-after-whitespace并以更新sourceHash绑定八项目。

## 正式r1发现与r2

r1 Application自然退出1：6012total/5990pass/6fail/16skip；6失败是两条既有Dimension内容不兼容，加UserServiceTests四个断线fixture缺ForgetConnection。仅在OnlineUserServiceTests显式提供无已激活profile时的ForgetConnection域操作，未知方法仍拒绝，原四例断线断言不变；生产源码不变。r1证据全部保留。更新sourceHash后fresh build与八项目前缀改task66-final-r2；主控runtime capture仍描述相同生产token。

## Protocol单文件补正与组合证据

r2 Protocol1795例1794pass/1fail：InitialConnectionDeliveryFailureTests的运输失败后断线阶段缺ForgetConnection。仅该测试文件补明确域操作；r3新增目标断言遗漏GameSessionIdentity重载而再次同例失败，r4补齐两个原有重载并均核ConnectionId。生产、共享Support、Application源码和项目配置逐字节不变，见logs/task66-manifests/task66-r2-r4-source-delta.json。按主控裁定正式Application沿用r2实际日志，fresh build后Protocol与余六项目使用r4，不改名/复制旧run；这一组合边界需在最终结论披露。

## 最终执行状态

正式组合全部自然结束：Application使用task66-final-r2-application，只有精确两条原Dimension内容失败；Protocol、Networking、Persistence、Infrastructure、Host、HttpApi、Pvf全部使用task66-final-r4-*，均exit0。原失败轮不删除/改名；主控依据logs/task66-manifests/task66-r2-r4-source-delta.json维护显式run映射，并统一核对计数、skip身份、DFO、Golden/Characterization和测量。不以本段替代主控的证据消费。

最终fresh build为task66-final-r4-build。最终2862项源码manifest为logs/task66-manifests/task66-final-source-manifest.json，已在全部测试自然结束后逐项复核与r4相同；完整153个owner路径同时在该manifest.changedPaths与/private/tmp/task66-owned-paths.json。源哈希`2cd7b909da3d58d1166df489ba20e25b360ef9dae47c3c287a4c06f403b5c67c`。执行代理已停止全部build/test，未运行G1、未提交/推送/创建PR/合并/部署/清理。

## 主控正式证据消费（最终）

主控统一消费上述原始run后确认：组合八项目9817=9797通过+2条精确既有Dimension失败+18条精确既有skip；相对TASK-65增加7个结构case、零删除。对A的显示实例比较为8350+1510−43=9817，其中包括改名，不把1510称为业务新增。

15个Golden与A逐字节相同；10个Characterization全部Passed、0skip。Characterization相对A的源码diff只有PartyTower注释与mask说明名称两处；本任务不将这些说明变化当新增或减弱线级断言。DFO使用原parser，八组均PASS：Application62+Networking6=68原负例，七个程序集各一个control，other/exe/orphan均0。

八套件runner时间合计405.955秒；A为321.639秒，当前/A=1.262倍。这里是完整套件墙钟的实际比较，不是纯执行器吞吐结论。内存仅有B的RUSAGE_CHILDREN高水位，不与缺少同口径数据的A作改善/退化判断。G1测量与验收仍由主控单独进行。

执行代理到此停止源码及本文写入，无任何build/test在本代理中运行。主控可补Result/提交，并独占后续G1。

原始源码清单、差异和焦点结果已按原字节移入既有忽略目录 `logs/task66-manifests/`，只提交叙述性验证记录和迁移账，不提交这些原始证据。
