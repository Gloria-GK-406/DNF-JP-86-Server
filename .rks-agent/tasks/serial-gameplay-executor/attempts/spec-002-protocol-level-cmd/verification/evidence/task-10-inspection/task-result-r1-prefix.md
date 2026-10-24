# TASK-10 — EffectExecutionContext 必需足迹与 ApplyEffects 显式贯通

## Task Unit Contract

- Revision: 5
- Outcome and references: 交付父[Plan P1](../plan.md) T-004的EffectExecutionContext／ApplyEffects共享切换，对应[Spec B1](../spec.md) RQ-004～006/009/012/014/015、AC-002/003/004/005/007/009：Context必需Footprint，ApplyEffects彻底移除可选operations和隐式Record，原完整effects→projection映射由捕获recorder显式记录；17处生产构造及Opening／XP真实上游原子贯通，不留optional/default/noop／旧签名或从ambient／事务补请求。真实离场奖励所需的临时具名Lifecycle NonRequest来源同时显式化。
- Owned boundary: 下文精确生产／测试／AGENTS、本Task及task-10证据、B开放ledger中已证transition metadata。父Spec/P1／契约、Root/A、冻结baseline/original、旧Task及其它源码只读。不迁完整UseStackable／其它协议route，不启用新Effect/MainSettlement/Compose，不改账本身份、业务／规则、事务／执行器算法、协议字段或字节。
- Required checks: 必需参数闭包、来源／owner／lease／job与写前拒绝、真实同provider selected legacy→ApplyEffects→Freeze、两请求singleton和失败后不串、Preview不记录／不结算、offline及有session的明确NonRequest、Cera首写前与中途失效真实区分、合法commit callback／旧持久回滚和协议断言；有效可编译mutation。完整Release build＋八full、七程序集实际controls/原expected DFO、Golden15／表征10、capture／数量／精确范围及strict/selftest，详见下文。
- Dependencies: TASK-09（`d1020731bac4f5785739d76141e4918862a1a1ea`，r4/ResultR1已独立approved；8499 inventory、直接slots/rental必需cap与真实legacy admission/source/lease）；间接消费TASK-04显式ctx/失败粘性、TASK-02四保护/F132禁用与原legacy45/58/60语义。
- Result review: required — 必需Context与共享ApplyEffects影响所有效果处理及普通XP／Opening来源，误分类会让未选中／关闭请求借内部权威写状态，遗漏会破坏离场奖励、Preview或已提交callback。后续效果协议将消费此基础，结果须独立核完整caller、真实来源、写前证据及旧断言后提交；主控检查readiness，不增加独立Task-plan门。

## Detailed Plan

### 1. 已接受起点与控制边界

开工核上述HEAD、原task/refactor-session-20261021分支、除本计划无未提交改动及唯一Task身份。读Task09 r2/r3/r4、ResultR1/approved review、AGENTS和两偏好文档；无.codegraph，rg优先。沿父授权中文、本地实施／核验／检查点，不push/PR/合并/远端issue/部署/清理，不运行df_game_r/DNF.exe；主控负责结果快审与提交。

已接受的实际API是：Abstractions的IEffectFootprintRecorder含DemandActive(SelectedSessionIdentity)、RecordInventorySlots、RecordRentalState；CommandExecutionContext.LegacyFootprint由真实LegacyFineGrained最内层admission提供；LegacyEffectFootprint绑定该context引用／ExecutionId、job、authorization snapshot及捕获frame/execution/rejection引用，finally关闭。ActiveEffect/ActiveNonEffect/真正外部Standalone明确分类，Unknown/Closed/缺Effect状态不获权；新typed Request触桥sticky失败。不能把该getter扩成新Protocol/Internal的后门，不能用default四参ctx或伪造admission创建生产来源。

Task09 r2只证明受迁入口及其自己写入／委托前校验，不证明已进入未迁ApplyEffects/Opening子链的全部await后失效保护。本单元按下面实际写前路径补证；参数已传入不等于所有深层await已安全。若真实被测出路需要超出白名单的参数或生命周期机制，先交主控拥有源码及最小方案，不私加全局tracker／第二ambient／取消协议。

Task09的App02因TERM父进程导致后代回收而无完整meta，已作废并由自然结束App03替代。本Task formal按独立step自然完成、使用实际meta；不能在active测试时为省后继control杀父runner。需改后继编排则等当前轮次结束，再启动新明确step，不补造exit/time或用修改后脚本冒充原运行体。

### 2. 窄能力与显式映射设计

在现IEffectFootprintRecorder增加具名的effects owner校验与记录能力：建议同文件增加 `EffectFootprintOwner(AccountId AccountId, CharacterId CharacterId, SelectedSessionIdentity? Session)`，接口增加 `DemandEffects(EffectFootprintOwner owner)`、`RecordEffects(EffectFootprintOwner owner, IReadOnlyList<AppliedEffect> applied)`。二者无默认实现，不以Record空列表代替活性检查。原slots/rental三方法保持，避免改变Task09直接链的业务形状。局部命名可统一为同义具名API，但不得转成object/nullable默认值／任意委托授权。

EffectExecutionContext增加必需非nullable `Footprint`，例如放在At之后、原optional Session之前。AccountId/CharacterId/At/Session仍是普通事实；不能给这些getters附lease活性副作用。没有无cap旧constructor、默认cap／DI cap／Noop、Sessionnull推内部的fallback；显式null等非法输入在执行前拒绝。静态事实可以共享，能力不得静态跨job共享。

LegacyEffectFootprint扩同一lease的effects方法，DemandAvailable仍只检查已捕获引用／guard和本次job，不重新读取Current、DI、NetworkCommandAuthorization.current或事务。selected effects校验account/character/Session与捕获recipient完全相符，仍主执行／TransactionStarted／未Freeze／未关闭；rejected连空effects也拒绝。明确NonEffect/Standalone的NonRequest可在Sessionnull或有session下合法使用，校验显式owner形状和本次lease/job；不是从“没有frame”新推信任。多参与者NonRequest可以按实际业务owner逐次调用，不强套报告者selected cap，原领域账号归属／current检查继续存在。

将EffectOperationContext.Record的完整switch迁为以捕获Frame为输入的私有／纯映射核心，由CaptureLegacy创建的recorder委托调用，或同职责的新EffectFootprintProjection文件；删除旧公开Record(EffectExecutionContext,...)及ApplyEffects的可选EffectOperationContext构造依赖。不能把Current查找藏进新RecordEffects wrapper。Enter/BeginMain/Freeze/Settled/Failed/rollback算法与45/58/60顺序不改，临时legacy桥本身仍在。

完整映射保留原分支：consume/equipment/stackable/prepared/basic items的Inventory和slots（宠物物品加Pets）；SpendGold/quest gold/opening gold/virtual的Inventory；town位置与source/destination areas；HerasQuota、Fatigue、ItemUsage、Quests、Skills、Pets、Experience、Level的Experience|Skills、career/awakening/materialize的CharacterAppearance|Skills、Premium；cooldown/spirit/SeriaLuck原不增加绝对投影；未知effect/change组合在selected路径原NotSupported保持。明确NonRequest按原无请求投影用途处理，不把它变成可注入默认值或无来源放行。

ApplyEffects仍要求原当前事务、真实character-store owner和可选session current匹配；新cap须在任何业务变更前DemandEffects，并在自身await后、逐effect委托前及最终RecordEffects前必要复验。保原preview→validate all→apply all→record顺序、效果返回值和异常。未选中/缺能力/关闭不能先写完再靠最后Record失败。既有effect handlers已收同一context，无需给其所有事实读取加主结算授权；实际写前依赖按§6审查，不以一次入口Demand宣称所有子链完整保护。

同样的无副作用校验应用于本次受迁域方法的入口及其自己实际变更／委托之前，必要时在自身await后重验。两塔Settle在AddExp之前已有金币／发物，不能只在最后XP处才验cap；按每个eligible成员的实际owner，在其金币／delivery等自身写段前复验。MonsterKillRewards等也不能在XP后任由关闭能力继续其自己业务委托。只改已列拥有源码，不给尚未带cap的下层API写“已全部覆盖”；真实更深漏窗按§6.8交主控最小处理。

### 3. 原子生产白名单与17构造闭包

以下路径均相对Server/src；限定为本次必需参数／source／验证／记录迁移，不改业务算法。具体未列的新增source必须先回主控，不因类型引用自动扩大。

| 项目 | 精确允许路径／责任 |
|---|---|
| Abstractions核心 | `Effects/DomainEffects.cs`、`Effects/IEffectFootprintRecorder.cs`：必需Footprint／owner与effects窄能力 |
| Application核心 | `Effects/ApplyEffectsDomainService.cs`、`EffectOperationContext.cs`、`LegacyEffectFootprint.cs`；可新`Effects/EffectFootprintProjection.cs`；`ApplicationModule.cs`仅下节临时Lifecycle来源注册 |
| 17构造的Preview／Quest | App `Quests/QuestCompletionDomainService.cs`、`MirrorAradQuestDomainService.cs`、`Settlement/QuestCompletionSettlementService.cs`；Abs `Effects/QuestCompletionEffects.cs`、`Quests/IMirrorAradQuestDomainService.cs`、`Settlement/IQuestCompletionSettlementDomainService.cs` |
| 装备六类 | App `Inventory/EquipmentReinforcementDomainService.cs`、`EquipmentMagicSealDomainService.cs`、`EquipmentEnchantDomainService.cs`、`EquipmentForgingDomainService.cs`、`EquipmentDisjointDomainService.cs`、`EquipmentOutworldDomainService.cs`；Abs Inventory同名I接口六文件 |
| 其余直接构造 | App `Creatures/CreatureNamingDomainService.cs`、`Inventory/ExperienceItemDomainService.cs`、`TeleportConsumableDomainService.cs`、`CeraShopDomainService.cs`、`ItemUseEffectDomainService.cs`、`Marriage/LovePoints.cs`、`MarriageDomainService.Couple.cs`、`Characters/PlayingCharacter.Experience.cs`；Abs `Creatures/ICreatureNamingDomainService.cs`、`Inventory/IExperienceItemServices.cs`、`ITeleportConsumableDomainService.cs`、`ICeraShopDomainService.cs`、`Marriage/MarriageDomainContracts.cs`、`Characters/ICharacter.cs` |
| Opening中心／上游 | App `Inventory/OpeningInventoryDomainService.cs`、`PackageOpeningDomainServices.cs`（Package/Avatar/MagicBox三类）、`LotteryOpeningDomainService.cs`、`VendingMachineDomainService.cs`、`EpicCraftDomainService.cs`、`RecipeCraftDomainService.cs`、`LevelUpTicketDomainService.cs`、`EntryAllowanceItemDomainService.cs`、`FatiguePotionDomainService.cs`、`ReviveCoinDomainService.cs`、`Characters/CharacterSkillLearningDomainService.cs`、`Settlement/QuestClearCouponSettlementService.cs` |
| Opening接口 | Abs `Inventory/IPackageOpeningDomainServices.cs`、`ILotteryOpeningDomainService.cs`、`IVendingMachineDomainService.cs`、`IEpicCraftDomainService.cs`、`IRecipeCraft.cs`、`ILevelUpTicketDomainService.cs`、`IFatiguePotionDomainService.cs`、`IReviveCoinServices.cs`、`World/HerasDungeon.cs`（仅IEntryAllowanceItemDomainService）、`Characters/ICharacterSkillLearningDomainService.cs`、`Settlement/IQuestClearCouponSettlementDomainService.cs` |
| XP业务参数 | App `World/MonsterKillRewardsDomainService.cs`、`MonsterDeathDomainService.cs`、`World/BossTower/BossTowerRewardDomainService.cs`、`BossTowerDomainService.cs`、`World/IllusionTower/IllusionTowerRewardDomainService.cs`、`IllusionTowerDomainService.cs`；Abs `World/IMonsterDeathDomainService.cs`（含rewards接口）、`World/BossTower/IBossTowerDomainService.cs`（含reward接口）、`World/IllusionTower/IIllusionTowerDomainService.cs`、`IIllusionTowerRewardDomainService.cs` |
| 实际写前验证 | App `Effects/ItemEffectHandlers.cs`的Consume await-read后到consume前必需复验；其余`Effects/InventoryEffectHandlers.cs`、`OpeningEffectHandlers.cs`、`QuestCompletionEffectHandlers.cs`、`SpecialConsumableEffectHandlers.cs`、`TownPositionEffectHandler.cs`、`DungeonEntryAllowanceEffectHandler.cs`、`GrantOpeningGoldEffectHandler.cs`、`SetCharacterLevelEffectHandler.cs`、`SetPurchaseRestrictionEffectHandler.cs`只允许经自身真实Apply写前依赖核实所需的无副作用cap复验，逐方法记录理由，不改effect业务／commit callback事实读取 |
| 生命周期必要闭包 | 下节精确Lifecycle来源API/实现、observer/cleanup及其真正caller；不迁其它cleanup编排 |

生产构造点必须逐条对照，不能只搜单行new漏ExperienceItem换行：

- 显式9处：QuestCompletionDomain54、MirrorAradQuest69、QuestCompletionSettlement20；Reinforcement119、MagicSeal66、Enchant68、Forging73、Disjoint95、Outworld75。
- target-typed8处：CreatureNaming33、ExperienceItem49～50、OpeningInventory56、TeleportConsumable36、LovePoints45、CeraShop108、ItemUseEffect当前60、PlayingCharacter.Experience24。

对应fine handler精确允许文件（相对Dfo.Server.Protocol/Game）：`ReinforceItem/ReinforceItemCommand.cs`、`MagicSeal/MagicSealCommand.cs`、`EnchantByBead/EnchantByBeadCommand.cs`、`Forging/ForgingCommand.cs`、`DisjointItem/DisjointItemCommand.cs`（NPC/portable两类）、`Outworld/OutworldCommand.cs`、`Creatures/RenameCreatureHandler.cs`、`QuestCompletion/FinishQuestCommand.cs`、`Teleport/TeleportSlice.cs`、`MarriageProposal/MarriageLovePointSlice.cs`、`CeraShopPurchase/CeraShopPurchaseCommand.cs`、`IncreaseStatus/IncreaseStatusSlice.cs`、`PackageOpening/PackageOpeningSlice.cs`、`AvatarPackageOpening/AvatarPackageOpeningSlice.cs`、`MagicBoxSingleOpening/MagicBoxSingleOpeningSlice.cs`、`MagicBoxBatchOpening/MagicBoxBatchOpeningSlice.cs`、`LotteryOpening/LotteryOpeningSlice.cs`、`VendingMachine/VendingMachineSlice.cs`、`EpicCraft/EpicCraftSlice.cs`、`CompoundItem/CompoundItemSlice.cs`、`LevelUpTicket/LevelUpTicketSlice.cs`、`QuestClearCoupon/EntryAllowanceHandler.cs`、`FatiguePotionHandler.cs`、`QuestClearCouponSlice.cs`、`ReviveCoin/DailyReviveCoinSlice.cs`、`LearnSkill/LearnSkillCommand.cs`；仅fine handler从本次ctx.LegacyFootprint取cap并传参，原网络handler、Messages/marker、codec/encoder不迁。

XP fine handler允许 `MonsterDeath/GrantKillExperienceCommand.cs`（含ApplyAwardAsync必需参数）、`ApplyMonsterKillCommand.cs`、`ProcessMonsterDeathCommand.cs`、`DungeonClear/GrantClearExperienceCommand.cs`、`BossTowerCommands.cs`、`IllusionTowerCommands.cs`。两塔只改真正通往RewardingMutation的Kill/Die/Leave及reward.Kill/Settle所需参数，Load/Start/Pickup/Next/Leader/Return等不按接口类型机械改。ability经当前调用参数／局部awaited delegate传递，绝不存singleton、run、Pending或跨请求集合。

ItemUseEffect已有Task09必需cap，直接用于构Context并传给Revive/EntryAllowance/Experience三支；不重复造同源能力。Opening只给实际Apply上游必需参数，SourceAsync／ReadPremiumsAsync本身不构Context，不给纯读机械授结算。CharacterSkillLearning只Learn→LearnRefund需要参数，SwapSlots/Switch不变。Quest/Mirror Prepare从同一次Finish接相同cap用于Preview，不记录/开始/settle；生产Prepare“独立旧invocation非main”是已撤回误推断，不据此造新PreviewFactory。直接Preview测试按实际显式测试来源处理。

XP只改ICharacter.AddExpAsync(ExperienceCalculationContext, At, optionalSession, ...)重载；ExperienceBreakdown重载、SetExp/SetLevel/TrySetExp及其已有调用只读。来源确定为普通网络／内部语义的XP仍传明确NonRequest，不能以有Session等同selected effect。MonsterDeath逐participant和两塔eligible奖励每人独立真实owner，保既有归属、当前性、奖励／事件／事务／commit临界序列，不把报告者能力绑定为所有奖励接收者。

### 4. 真实Lifecycle NonRequest来源与精确离场闭包

已核实必要性：SelectedSessionCleanupService149/163直接两塔Leave；BossTowerRun.Leave138～147在剩余成员全Dead时End(Defeated)，RewardingMutation再SettleAsync，reward服务按eligible成员调用AddExp(source)。不能假定cleanup无奖而绕过cap。其来源既有fine caller，也有OnlineUserConnectionObserver111由NetworkConnectionLifecycleDispatcher.TeardownOf直接调用，后者没有legacy admission。

按主控已确认的最小方案，新增Application/Effects的 `ILifecycleEffectFootprintSource.cs`（可同文件定义只暴露Footprint/Dispose的lease）及 `LifecycleEffectFootprintSource.cs`，ApplicationModule注册。方法只收现 `InternalCommandOrigin`，返回本次NonRequest lease；没有Func业务、入队／发送／开事务／查当前请求能力，不新增ambient、jobslot或全局tracker。

生产唯一创建调用点限定OnlineUserConnectionObserver.DisconnectedAsync的Game cleanup入口：明确构造origin，Kind=Lifecycle，Chain=当前已执行lifecycle作业的原GameplayJob.Current chain，CommandType为具名observer cleanup，SourceConnection=null。factory验证显式Kind/origin有效、chain对应当前job并DemandHealthy，cap绑定本次job/close并以owner事实验证/记录；不靠缺frame、TestGameplay或job名称推信任，不forge旧admission，也不从旧关闭网络frame恢复权威。scope于本次cleanup await结束后Dispose，事实读取仍合法。

SourceConnection=null保持NetworkConnectionLifecycleDispatcher48原Teardown的source:null；chain可能继承另一根连接作为cause，不得把被关闭目标填成物理来源，也不改root/parent/depth。无需改LifecycleDispatcher实现／接口、队列或触发关闭语义。该factory不得获得新CMD构造器豁免，结构检查限定生产创建调用点为该observer并有坏例；新typed CMD不能把它当通用NonRequest入口。其它生产来源需要第二factory或更宽授权时先向主控给具体caller，不泛授权。

精确追加生产白名单：

- Abs `Sessions/ISelectCharacterLifecycleServices.cs`：CleanupAsync必需cap。
- App `Sessions/SelectCharacterLifecycleServices.cs`：cap只经Cleanup到两塔Leave，原逐步清理／收集异常／通知／位置持久化次序不改。
- Protocol `Game/OnlineUserConnectionObserver.cs`：必需source依赖、原Disconnected Game分支及private CleanupSelectedAsync传参／本次scope；Connected行为保持。
- Protocol `Game/SelectCharacter/SelectCharacterPhaseCommands.cs`：Prepare的三处cleanup及Compensate的cleanup，传本次ctx.LegacyFootprint；`Game/ReturnToSelection/ReturnToSelectionCommand.cs`：Complete cleanup同样传cap。
- 已列两塔Leave/Reward/XP接口与实现。

这些现fine caller按真实来源取得NonRequest，不能新建typed CMD或把全部选角／cleanup迁完。Lifecycle临时source adapter记在B开放过渡账，P1 T-010改为真正Internal CMD时删除；对应source／transition仍pending。scope结束后不能续用其effects能力；保其它零奖／旧generation清理照常进行，不让给奖励owner的cap校验阻断移除已经失效会话的非奖励动作。

### 5. 实际测试白名单与驱动保留

下列路径均相对Server/tests。只改必需参数、每调用显式能力／source驱动或本节具名新增证据；所有原Assert、SQL、错误cause、回滚/commit、事件、经验/技能和线字节保留。测试构图／编译失败不算行为RED。纯DI/反射／真实bus不改签名的调用不机械编辑。

| 测试组 | 精确允许文件／最小责任 |
|---|---|
| 测试能力／核心行为 | `Dfo.Server.Application.Tests/Support/TestEffectFootprints.cs`：复用Task09helper扩effects owner／记录，支持真实offline owner和optional Session，每调用lease；`Sessions/LegacyEffectFootprintTests.cs`仅原fake Revive签名／接口扩充及相应驱动；新`Sessions/ExplicitEffectExecutionTests.cs`集中本Task来源／映射／singleton／失效行为，不镜像17套 |
| 直接Context／executor | App `Inventory/ApplyEffectsTests.cs`、`EquipmentEffectExecutionTests.cs`、`OpeningEffectExecutionTests.cs`、`SpecialConsumableEffectTests.cs`、`AssetGrantServiceTests.cs`、`Creatures/CreatureRuntimeDomainServiceTests.cs`、`CreatureNamingDomainServiceTests.cs`、`Sessions/EffectOperationReplayTests.cs` |
| 装备／已有直接handler | App `Inventory/EquipmentReinforcementDomainTests.cs`、`EquipmentMagicSealDomainTests.cs`、`EquipmentEnchantDomainTests.cs`、`EquipmentForgingDomainTests.cs`、`EquipmentOutworldDomainTests.cs`；`EquipmentDisjointTests.cs`仅真实直接DisjointItemHandler body的明确Effect测试来源驱动，保外部bus其它用例 |
| Opening／Learn实际直接链 | App `Inventory/FatiguePotionTests.cs`、`LevelUpTicketTests.cs`、`RealConsumableIntegrationTests.cs`；`Characters/CharacterSkillLearningTests.cs`（含LearningFixture）、`SkillBuildResetTests.cs`：实际Learn参数／四个Payment/WaterInventory等IOpening实现与Context |
| Quest／Preview真实caller | App `Quests/OrdinaryQuestEffectTests.cs`、`MirrorAradQuestTests.cs`、`QuestClearCouponDomainServiceTests.cs`、`Characters/CareerReconciliationTests.cs`、`Achievements/TitleQuestUnlockRoutingTests.cs`、`AchievementTitleBookTests.cs`：直接Prepare/Finish/Apply及IOpening fake签名，单纯IEffectHandler/Preview接口实现无变化的不改 |
| LovePoints直接调用 | App `Marriage/MarriageCoupleCharacterizationTests.cs`、`MarriagePairCorrectionTests.cs`、`MarriageChurchTests.cs`、`MarriageRelationLedgerTests.cs`：只UseLovePointAsync必需参数/lease，原婚姻其它调用不动 |
| XP／两塔直接与接口实现 | App `World/CommonExperienceEffectTests.cs`、`MonsterDeathTransactionTests.cs`、`BossTowerDomainServiceTests.cs`、`IllusionTowerDomainServiceTests.cs`、`IllusionTowerPublicFlowTests.cs`、`IllusionTowerRewardProjectionTests.cs`；`Characters/CharacterExperienceTests.cs`仅439～445附近AwardAsync的Source重载显式Test lease，不改Breakdown/SetExp/SetLevel测试；`Support/CurrentCharacterStub.cs`及`World/PaidCardFlipCostTests.cs`仅全限定名Source重载接口签名。保其它业务／奖励断言 |
| cleanup真正caller／stub／observer构造 | App `Sessions/SessionGenerationOwnershipTests.cs`、`SelectedSessionPartyCleanupTests.cs`、`Game/SelectCharacterReturnTests.cs`、`SelectCharacterLifecycleTests.cs`、`OnlineUsers/OnlineUserServiceTests.cs`、`SerialDisconnectTests.cs`；Protocol `Game/InitialConnectionDeliveryFailureTests.cs`：仅实际cleanup驱动／接口／ctor参数 |
| Protocol XP接口／直接fine | `Dfo.Server.Protocol.Tests/Game/ExperienceCharacters.cs`、`CarriedGoldStandIns.cs`、`ExperienceFrameStandIns.cs`（后两仅全限定名Source重载签名）、`MonsterDeath/AdvanceHuntEnemyQuestCommandTests.cs`、`GrantKillExperienceCommandTests.cs`、`MonsterDeathHandlerTests.cs`、`DungeonClear/GrantClearExperienceCommandTests.cs`、`QuestCompletion/FinishQuestCommandHandlerTests.cs`；新`Support/TestEffectFootprints.cs`可作为该测试程序集每调用显式lease小helper，不引用App测试程序集／改项目／生产测试平台 |
| 两塔stub／接口编译闭包 | Protocol `Support/TowerDomainStub.cs`、`Game/IllusionTower/IllusionTowerProjectionConcurrencyTests.cs`、`Game/BossTower/BossTowerProjectionConcurrencyTests.cs`、`BossTowerPublicHandlerTests.cs`；Net `Integration/BossTowerFrameIntegrationTests.cs`仅Tower实现Kill/Die/Leave参数、`Integration/IllusionTowerStageFrameIntegrationTests.cs`仅284处全限定名ICharacter.Source重载签名，原真实TCP断言保持 |

静态 `ApplyEffectsTests.Offline` 是真实EffectExecutionContext，不能在上面挂跨jobcap；改成静态owner/time事实及每调用构Context的显式lease。原withSession的冷却/SpiritPotion等测试仍可为明确TrustedTest，不因Session非null硬造selected网络状态。已有手工EffectOperationReplay测试须保selected/rejected真实声明，不全部改NonRequest；用Task09原admission/manual来源方式，不能让default四参ctx自动获权。

真实外部bus的Package/Lottery/Vending/ReviveCoin等测试一般不需改，Standalone能力由原真实入口产生；先核实际调用而非批量迁所有39marker。直接handler在TestGameplay中不自动可信：若原请求确为Effect，沿Task09 r4每attempt明确Effect auth及独立selected operation的已知测试输入，保原metadata，不改成NonEffect或声称是ledger验收。测试cap/helper不成为生产来源，也不跨job静态复用。

CarriedGoldCompositionTests仅DI、MarriageArchitectureTests主要反射、InventorySlotBoundaryArchitectureTests和Package/LevelUp/Compound/LearnSkill等多份codec测试仅类型／规则／codec引用，原则上只读回归；不因为rg命中接口名就加入改动。其它接口实现／直接caller若被遗漏，需给主控精确源码／机械签名证据后增补，不把任何新失败都归为来源问题而放宽分类。

原observer直接构造测试可显式注入测试source实现，每调用创建已知Test lease并记录origin，不能改变通用TestGameplay或降低生产factory验证；真正Lifecycle factory由新真实dispatcher路径另证。无新全局Skip／串行／放大deadline，无反射调用生产private、IVT或项目引用变化。

### 6. 真实行为证据与r2中途失效

新增证据集中在一个App新类，复用原ShopFixture／legacy admission和小lease helper，避免17份镜像：

1. 同一provider真实dispatcher→legacy pipeline45/58/60→实际ApplyEffects→原Freeze后的projection，至少验证Inventory slots与Experience/Skills/Pets/Town等有区别的映射；原完整switch分支另以紧凑表驱动守映射，无新的协议route或伪造已提交ledger证据。
2. 缺cap/null、rejected（含空effects）、错误owner、非main／未txn／frozen、operation/admission/auth/lease/job结束均在业务写前拒绝；保Task09旧slots/rental和新typed sticky反例。效果应用不能把Unavailable变为NonRequest。
3. 同一ApplyEffects singleton两请求得到不同cap，第一足迹不被第二覆盖；失败/真实回滚后下一次不串；生产新公开入口不含current请求依赖，RecordEffects只用捕获引用。
4. Preview沿上游同cap调用，记录计数0、未开始/冻结/settle、不产生业务写；QuestCompletionSettlement在同一次Finish内Prepare→Apply保持。直接Prepare测试用明确Test来源，没有新生产PreviewFactory。
5. 明确offline Sessionnull和withSession Test来源都可合法Apply；有Session的GrantKill/Clear、MonsterDeath多participant／两塔奖励仍为明确NonRequest，每owner原归属/current规则保持。CeraShopPurchaseRequest与DailyReviveCoinRequest是普通IClientNetworkMessage，不因产生effects新增marker；至少以实际普通网络/真实bus证两者不误拒。
6. CeraShop101wallet/105gold在108Apply前写已证；新增cap入口及其首写前复验，控制原规则/报价await后结束lease再恢复，实际余额/gold/写计数不变。Reinforce/Disjoint“Apply前另写准备/flush”已撤回，实际为Preview→Apply，不能当新增事实或修改其算法。
7. 补Task09 r2真正子链区分：ConsumeFromContainerEffectHandler.ApplyAsync内部await ReadAsync后、Consume前，复验本次context.Footprint。用真实业务container/Get的受控gate，使调用在有效期进入handler、读挂起时关闭lease、恢复后检查0Consume／真实item存储不变；不是仅在结束后重新进入域入口。ApplyEffects自身preflight/validate等await后也以实际暂停恢复证没有继续调用下一effect/写入。
8. 审查其它effect handler实际写前依赖：各自已收context，可在自身await后、首次变更或业务委托前无副作用复验；记录逐方法保护位置和测试含义，不能把“委托前查过”描述为未传cap的下层所有await安全。若更深具体服务仍可在中途过期后写，交主控真实source和最小传参/结算方案，不先造全局tracker，不用最终Record异常冒称从未写。
9. Lifecycle新来源通过真实NetworkConnectionLifecycleDispatcher→observer→cleanup→两塔Leave／奖励链或可观察真实Apply证明；origin.Kind=Lifecycle、当前原chain、SourceConnection=null，旧根cause另连接不被改写。scope结束/跨job续体拒绝；无源/default/test-job-name不隐式授信。factory生产使用点及新CMD构造器禁注入有实际违规反例，不新增factory类别例外。
10. 原真实SQL回滚／资产／经验／任务／技能／事件断言保持。cooldown、spirit、town的transaction participant在inner lease关闭后仍可合法commit，仅读context事实不需recorder重新授权；原提交/回滚测试必须原样证明该点。

至少一个有效可编译mutation选择第7项：去掉handler在await-read后到Consume前的复验，gate oracle应发现真实Consume/存储变化；只Apply入口Demand仍在，因而能区分r2漏窗。恢复原bytes/SHA后绿。若真实container下层仍先拒绝而该变异不红，应定位真实被拦点、选能区分本新增保护的oracle，不用假的业务抛错／编译失败填RED。所有轮独立log/meta、finally恢复；原断言/deadline/skip/并行不改，关键Assert放业务catch外。若需扩大到未定深链或新的生产机制，先回主控而非放宽本合同。

### 7. 文档、迁移账与注册范围

AGENTS精确更新必需EffectExecutionContext.Footprint、ApplyEffects已无optional operations、直接/Opening/XP来源参数、Preview和commit事实边界、临时Lifecycle来源及剩余legacy；不写新Effect/Main已启用、ambient全清或全部149消息完成。可写本Task、B开放ledger和task-10前缀log/evidence；probe-tools复用，任务专用运行/映射脚本放自身证据目录，旧证据和original不改。

本Task不迁CoreMessage/Command注册，不改ProtocolModule、message/command分类或消息形状；合理预期仍173消息＝10Protocol＋163Legacy、205命令＝10Protocol＋1Internal＋194Legacy，Kind166Tx/26Direct/9ReadOnly/4Workflow。实际ConfigureServices-only capture逐条源码↔runtime核，不启动业务，不硬填。BuyItem同message有普通BuyItemRequest及Effect HousingBuyItemRequest两CLR家族，是已证未来effect路由/Buy单元输入；本Task不改注册骨干或marker/协议字段，173不硬增。

pending业务149／旧命令194／来源72保持。本次只对现 `transition:T4`、`transition:planned-legacy-routes`、`transition:planned-LegacyRequestAdapter` 的notes/introduced_artifacts补真实必需Context、显式映射、17闭包及临时Lifecycle source adapter/后继删除责任，仍pending。若认为另一个保护或source已完整满足，先给主控exact冻结ID和实际证据批准，不能擅造baseline ID／closed或改original。T-010真正Internal CMD取代临时Lifecycle入口时删除，当前不冒称source全typed。

源冻结／业务与映射检查通过后由执行者写Result，再strict validate/selftest、原八坏副本拒绝、source invariants/manifest。没有业务closed行时不假造preclose豁免。只改本Task及允许的开放metadata，不重写Task09 r2历史或其已披露局部限制。

### 8. 正式核验与交回

共享Context/API、XP／Opening／cleanup参数影响广，采用完整Release --no-incremental构建和八项目全量，原class并行/skip/期限不改；native require_escalated/caffeinate，独立step自然结束，每轮实际cwd/HEAD/command/UTC/exit/elapsed及Darwin资源完整落盘，不在active父进程下靠TERM跳过后继。

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

起点8499＝App4745、Protocol1762、Net199、Host203、Http57、Infra976、Persistence477、Pvf80；逐文件N/D/merged、驱动/断言迁移分开，按Theory实得case数计，`8499＋N－D`与八项目实跑数闭合，不预填新增数量。保Task09旧29例及全部原SQL／业务断言，无测试删减求绿。只容许准确原DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource和All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot（Dungeon7100/maze0/cell(1,0)/entrance Left原InvalidDataException），及原App16/Infra2的18skip；新来源、端口、SQLite或其它失败按拥有源码诊断，不塞基线。

单一新鲜DFO目录跑实际八full，七取证程序集各唯一control，按原expected逐类/数核（现App62、Net7、其余五各1，开工读工具确认）；外/exe/orphan0如实披露，exe0仅观察。新增故意反例确需新增expected时先定向实跑精确类/数并解释，不放宽原类；后续受影响重跑用另新鲜目录独立核，不拼接旧JSONL或把control-only说全量。完整Net含原Characterization10逐名通过0skip；Golden15对f01dc226/d1020731实际bytes不变，原metadata/slot/codec/连接断言保持。

最终source核查：精确owned路径/行尾/BOM/whitespace；其它Server源、旧Task、baseline/Root/A SHA不变；17构造全为必需cap、旧ApplyEffects optional operations/旧Record完整符号和DI依赖零；没有新增ambient／请求jobslot／txn查请求／静态跨jobcontext／legacy默认重载。记录各公开参数、来源与真实caller，pureDI/反射/codec未机械改。新Lifecycle factory仅observer一生产创建点、无新CMD注入豁免；已废接口和旧Query式API不复活。`git -c core.whitespace=cr-at-eol diff --check`及新文件空白检查通过。

Result R1必须区分：已证r2实际中途失效出路、其它尚未触子链的真实边界、合法Preview与commit事实读取、明确普通XP/Cera/Daily/Lifecycle来源及多owner、原接口/驱动/断言迁移、正式全部轮次和作废轮次、N-D/DFO/capture/strict/范围。受影响检查完成后交主控required独立结果快审／本地检查点，不自批或启动下一Task。单条CMD≤2秒性能、完整新效果/Main协议、其余149/194/72及T011清零/T012最终核验、Astra和用户收尾仍后续，不冒称Spec完成。

## 执行增补 r2 — Altar 必需参数调用闭包

compile-02 的真实 CS7036 暴露白名单遗漏：`Server/src/Dfo.Server.Protocol/Game/Altar/AltarCommands.cs` 的 `AltarDeathBatchCommandHandler.HandleAsync`（原43～49行）在原 `Actors.Distinct()` 循环中直接调用 `IMonsterDeathDomainService.SettleAsync`。该接口已属本Task的XP／MonsterDeath必需参数迁移；主控已核原源码及 `AltarMessages.cs`：`AltarDeathBatchRequest` 是普通 `IClientNetworkMessage`，不加Effect marker。

精确新增可写路径仅上述 `AltarCommands.cs`，允许原 `deaths.SettleAsync(new(...), ct)` 调用在ct前传递本次 `context.LegacyFootprint`。保持actor去重与遍历顺序、state/RoomVersion保护、聚合返回、原fine事务注册和所有业务语义；不迁完整Altar route、不增加来源工厂或修改消息分类/ProtocolModule。此处是既定必需参数闭包的补漏，不把编译失败计作行为RED。正式结果、source范围与manifest纳入该路径，并保留compile-02原始证据。

## 执行增补 r3 — 四处原有测试驱动闭包

compile-04真实必需参数编译错误揭示三处测试白名单遗漏，另经源码确认一处普通网络直接handler驱动会缺来源。主控已逐处读取原调用及相邻断言；精确新增以下App测试可写路径和动作：

- `Inventory/EpicCraftTests.cs`：原55行CraftAsync直接调用，每次明确owner/session Test lease传cap；保延迟外键提交失败及pieces/material/equipment SQL回滚断言。
- `Marriage/MarriageEngagementTests.cs`：原292/305/316/324行四个FinishAsync各次明确owner(1,71,f.Session) lease传cap；保持任务接取、消耗、库存、婚姻状态及提交顺序断言。
- `World/SouthernDaleTests.cs`：原195/203/214行（含重试循环）MonsterDeath.SettleAsync各调用明确多owner NonRequest测试cap，覆盖两名原参与者，不把所有成员绑到报告者单owner；保持触发器错误、未提交回滚、奖励数量与重复重试断言。测试helper可扩成显式已知owner集合的每调用lease，不产生生产默认来源。
- `Inventory/CeraShopPurchaseTests.cs`：仅fixture.Handle（原561～562行）在现TestGameplay作业中为真实普通 `CeraShopPurchaseRequest : IClientNetworkMessage` 显式建立NetworkCommandAuthorization NonEffect scope；保request、GameMessageMetadata.Empty、直接原handler与全部原断言。不扩通用TestGameplay，不新增marker或把Effect测试改NonEffect。

三个接口错误仅为机械签名迁移，不记行为RED；compile-04证据保留。所有修改纳入source范围/manifest，原SQL、业务、帧及错误断言不因迁参改写。其它新漏caller仍需真实源码和主控增补，不能以本段授权批量改未列测试。

## 执行增补 r4 — 首轮原有App回归的精确驱动与分类签名

existing-application-01为诊断轮（排除原Dimension类），实际4732＝4644pass＋72fail＋16skip。主控已核失败栈及拥有源码：大部分是本Task已列文件的直接handler/mockbus构造未供应cap或未声明实际网络类别；不放宽生产默认ctx。精确新增三个App测试白名单：

- `World/TournamentDungeonTests.cs`：仅Kill helper原56行直接ProcessMonsterDeathCommandHandler的每次fine ctx，以实际请求participant session构造已知Test lease并明确供应；保原事务、commit参数、怪物顺序、奖励/疲劳/回滚断言。
- `Characters/SkillLearningCommandTests.cs`：仅Handle helper原99行直接LearnSkillHandler驱动。实际LearnSkillRequest是Effect，Minimum为Inventory|Skills；每调用明确Effect auth、独立manual selected entry与精确Safety recipient，并保原GameMessageMetadata.Empty及最低足迹。不改为NonEffect、不声称真实ledger验收，不更换provider或改原SQL/ACK/IOException断言。
- `Execution/GameplayWriteEntryTableTests.cs`：仅两塔DieAsync/KillAsync/LeaveAsync共6条原W成员签名加IEffectFootprintRecorder参数类型，原W归类和write断言、拒绝反例保持；不删除行或放宽全表穷举。

已owned的SelectCharacterLifecycle等fixture中mockbus/直接fine ctx只能对本次真实调用明确给cap；原受影响Effect驱动仍按实际请求分类。保诊断轮完整记录，最终正式全量不得使用排除Dimension的filter。

Consume受控gate的初始有效RED已见真实Take1及持久3→2；只作为缺少await Read后到Consume前复验的区分证据。测试catch后commit是为了证明不是回滚掩盖，不能解释为生产失效请求获准继续提交。新增guard后应Take0且store3，并用可编译mutation恢复同一RED；记录SHA恢复和逐轮真实meta。

## 执行增补 r5 — 已证Heras等待窗口及正式01控制流回归修正

主控逐读 `World/HerasDungeonDomainService.cs:60～74`、DungeonEntryAllowanceEffectHandler及真实UsageQuotaDomainService订阅者，确认context已传入，但ApplyAllowanceAsync在await ValidateAllowanceAsync之后直接发布HerasQuotaRecorded，handler委托前校验不能覆盖此内部等待。精确增加生产可写路径 `Server/src/Dfo.Server.Application/World/HerasDungeonDomainService.cs`：仅ApplyAllowanceAsync入口及await验证后、DirectAsync之前调用本次context.Footprint.DemandEffects(context.Owner)。不改验证/周期/额度业务、Direct事件/事务顺序或其它方法/API，不新增机制。

在已owned ExplicitEffectExecutionTests增加一个实际写入区分用例：真实provider的quota/事件订阅/store，控制第二次bonus真实读取后的等待（handler Validate及Apply内部Validate），等待中关闭本次lease，恢复后以Direct调用及SQLite bonus持久状态证明无写；catch后受控提交仅用于排除回滚掩盖。去掉await后的复验、保入口保护的可编译mutation必须使真实事件/额度写oracle变红，finally恢复bytes/SHA后绿。不把仅最后Record抛错记为写前阻止。

正式01全部自然结束且保留：build0/0；App4768中2准确Dimension基线＋11新失败；Net199中1新失败；二者已证/待原case验证的共同源为本轮owned IllusionTower RewardingMutation插入Demand时漏braces，使原DungeonId==11000条件仅管Demand而不再包含Leave foreach。精确修正同方法braces，恢复原if→foreach范围和原业务次序；不得改11 App及Net原断言/超时。两塔Reward各2处连续同参Demand只保一条。主控已只读枚举新增Demand紧邻控制行，当前唯一未终结条件移位是此处；执行者仍须按实际diff逐方法复核，不泛称自动证明所有语义。

Host01另有HostShutdownOrderTests.Sigterm_with_an_open_game_connection_stops_the_executor_after_its_teardown_and_exits_cleanly的advertised route Connection refused（332→289），不纳入新基线、不混同App错误。Harness Start仅等HTTP bootstrap 200、未校验Game listener，是源码时序假说，尚无唯一实例/端口证据，不能称已证根因或已修。本Task不改Host源码/harness/timeout/skip；保原轮，允许精确原单例诊断，最终Host full须通过；若再失败据实际日志/实例诊断，不盲目重试求绿。

正式01 source-freeze与所有日志/meta/DFO不重写。r5更改完成后新source freeze、完整Release build及八项目无filter全量使用新轮次和新鲜DFO目录重新执行，不能把两轮拼成最终结果。原23新增case及本次新增按实际N/D闭合；继续严格2准确内容fail＋18skip边界、原帧/SQL/Golden/表征/结构判据。结果区分本轮真实可避免回归、已修后证据、Host未确证瞬态及其最终结果。


## Result

- Revision: 1（Task r5；HEAD d1020731；执行者有界结果，required 独立快审由主控负责）
- Outcome: done
- Changes: EffectExecutionContext 必需非null Footprint，Owner/AccountId/CharacterId/At/可选Session均为普通事实；ApplyEffects 删除 optional EffectOperationContext 依赖及隐式 Record。新增 DemandEffects/RecordEffects 窄接口，旧完整 effects→projection switch bytes 原样迁入 private static 捕获frame映射；Record 不重查当前请求／DI。17处生产构造、Opening／Quest／装备／XP及真实fine caller、实现和原测试stub原子改必需参数，无旧默认重载／noop／singleton跨job cap；无route、marker、ProtocolModule、协议字节或原事务／账本算法变化。
- Authority / owner: 消费 Task09 真实 LegacyFineGrained admission、捕获source/job/frame/execution/rejection及percall lease；selected精确校验account/character/session、main/txn/未冻结/未关闭，rejected空effects也拒绝。普通Cera／Daily、XP多participant和有session／offline按明确NonRequest来源，不能由Sessionnull或TestGameplay推信任，不能把reporter selected cap套给其他owner。原直接Effect测试保真实Effect类别、原metadata及独立manual selected状态，未当真实ledger序号证据。
- Lifecycle: 临时具名 source API／实现仅 OnlineUserConnectionObserver 生产Open调用，显式Lifecycle origin＋当前原chain＋物理SourceConnection=null；factory核实际Lifecycle job/chain/healthy/close，不伪造旧admission、未知actor授信或新增CMD构造豁免。cap贯通cleanup→两塔Leave／reward→XP，原清理／异常收集／位置保存／通知先后保持。真实生命周期dispatcher测试证明根cause可为另一连接而物理source仍null、scope结束／跨job拒绝；当前仍临时adapter，未来T010真正Internal替代时删除，不冒称source全部typed。
- Owning write boundaries: ApplyEffects在入口、真实owner读取／Preview／各Validate后及每Apply前复验，最后显式Record。各自已收context的effect handler按实际read→写／业务委托边界校验；Cera在Apply前原wallet/gold首写前复验，XP/tower在原金币／发物首写前按真实成员复验。详见 [boundary-audit](../verification/evidence/task-10-inspection/boundary-audit.json)。委托前校验不等于未收cap的下层所有内部await均安全，本单元不声称最终RQ005已全闭合；无新增global tracker／ambient／隐式授权。
- Mid-await evidence: Consume真实container Get受控挂起，失效后恢复，实际Take0且提交后item数量仍3；只删Read后guard、保Apply入口及其它guard的可编译mutation真实红为(1,2)，finally恢复原bytes/SHA后绿。Heras真实quota第二次bonus读挂起，cap关闭后恢复，Direct0且SQLite bonus0；只删await Validate后guard、保入口的可编译mutation红为(1,1)，恢复bytes/SHA后绿。两种catch后受控commit仅排除回滚掩盖，不是生产失效请求继续commit的授权。见 [Consume mutation](../verification/evidence/task-10-inspection/mutations.json)、[Heras mutation](../verification/evidence/task-10-inspection/mutations-heras.json)。编译／fixture错误不算行为RED。
- Preview / commit: Quest及Mirror Prepare在同次Finish内沿同cap Preview，不录足迹、不开始／冻结／settle，无额外PreviewFactory。冷却／Spirit／Town原transaction participant在inner lease关闭后仍合法commit，callback只取context事实、不重新要求活lease；原rollback／SQL／技能／经验／codec和帧断言保留。
- Behavior / test migration: 新行为集中 ExplicitEffectExecutionTests 实得24例：同provider真实dispatcher→legacy45/58/60→Apply→Freeze、singleton连续／失败重试不串、非法10状态、null／owner／closed／offline、有session NonRequest、Preview零写、Cera首写门控、实际普通Cera／Daily、真实Lifecycle及受控中途失效。原完整映射另以34组紧凑输入检查不同family／slot／area／Pets等分支，这不是34个实际业务用例的声称。原66个修改测试文件的Assert方法多集及Fact/Theory声明保持；仅实际caller／必需签名／明确percall测试来源迁移，未批量改39 marker或放宽通用TestGameplay。
- Formal01 actual failures: 首轮 build0/0＋八full全部自然结束，8522＝8489pass＋15fail＋18原skip。App4768＝4739pass＋13fail＋16skip（2准确原Dimension＋11本轮可避免Illusion回归）；Net199＝198pass＋1原TCP退场新失败；Host203＝202pass＋1 advertised route Connection refused。原owned Illusion未加braces使原if→foreach范围移位，本轮负责且r5恢复原范围，四处机械重复Demand删除；原App覆盖类209/209及原Illusion TCP两例通过，不新增镜像断言求绿。首轮所有freeze/log/meta/DFO完整保留，未当允许基线或混成最终绿。
- Host boundary: Host01 Start只等HTTP bootstrap200、不校验Game listener，是源码时序假说；无唯一实例／端口证据，不能声称已证明或修复其根因。未改Host/harness/断言/超时/skip。r5后原精确单例1/1，最终Host full02 203/203；首轮真实失败仍披露，不新增基线。
- Final formal02: r5新source-freeze-02之后完整Release --no-incremental build02 0warnings/0errors，八项目全部无filter自然完成（原class并行／5m／skip不改，native metadata含cwd/HEAD/command/UTC/exit/elapsed/资源）。8523＝8503pass＋2准确原Dimensionfail＋18原skip；Pvf80、App4769（4751pass/2fail/16skip）、Host203、Http57、Infra976（974pass/2skip）、Net199、Persistence477、Protocol1762。两fail均原Dungeon7100/maze0/cell(1,0)/entrance Left；完整身份及原skip逐名比较见 [formal-summary](../verification/evidence/task-10-inspection/formal-summary.json)。8499＋N24－D0＝8523，未删或合并测试。
- DFO / wire / registration: Formal01与02各自新鲜目录分别分析，均七程序集唯一control、74原expected精确类/数，外／exe／orphan0；未拼接目录，exe0仅此次观察。Net原Characterization10逐名通过0skip；15Golden对f01dc226及HEAD实际bytes不变。ConfigureServices-only capture02实得173messages／205commands：10Protocol＋1Internal＋194Legacy，Kind166Tx/26Direct/9RO/4Workflow，message10Protocol＋163Legacy，14telemetry；不启动业务或新游戏进程。
- Invalid rounds: compile01只打印output丢session且日志截断／无native meta，实际ps确认无残留后作废；不补造exit或计RED。compile／fixture诊断及首轮旧driver缺cap故障完整保留，仅真正可编译业务guard变异计RED。Formal01/02所有独立step自然结束，未TERM任何活跃编排树。
- Scope / ledger / handback: 精确192生产／测试／AGENTS物理路径，预检4098中其它SHA保持；行尾/BOM/混合行尾、whitespace、原lifecycle prefix及完整mapping／原断言／最终冻源检查通过。预检曾流式采集10个历史ignored TestResults文件，owned before-image逐项等HEAD，未清理或修改历史dump（见preflight-audit）。ledger仅3个既有pending transition的notes/artifacts更新；original／其它行／baseline保持，149业务消息／194旧命令／72来源及其它pending不关闭。Result后strict/selftest八坏副本及manifest见独立证据。完整新Effect/Main、单条CMD≤2秒、其余迁移、T011/T012、Astra及用户收尾仍后续；未gitadd／commit／push／PR／清理或启动下一Task。交主控fresh required结果快审与本地检查点，非Spec完成／自批。
