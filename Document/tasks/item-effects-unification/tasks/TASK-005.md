# TASK-005 — 普通任务交付、完整奖励与转职计划

## Task Unit Contract

- Revision: 1
- Outcome and references: 普通FinishQuest的任务状态、全部奖励、金币、XP成长、转职与全页技能重建、连带任务进度进入同一完整effects；拒绝只返回只读有效投影。Plan T-004任务奖励部分，RQ-002/003/006—008，AC-002/005/007。
- Dependency: 已接受TASK-001共享执行器／归属保护、TASK-002 standing、TASK-003 prepared／副作用及最终receipt，TASK-004实际raw任务与技能基础CAS输出。控制器已核对这些依赖。
- Owned boundary: 普通交付command→领域事实→纯规则计划→统一执行→结果投影、必要XP纯决定／基本写入接缝、basic物理全数量grant及shared preview snapshot、转职全页展开、DI及直接测试、coverage和本Task结果。共用AwardAsync／kill／clear迁移留下一单位，协议重放留后继单位；不新增需求品扣除，不重写任务条件或怪物系统，不提交／部署。
- Required checks: 正常拒绝零写入／真实有效投影，stale trigger正反向，多堆完整数量与满包拒绝、Basic字段／创建facts保真、金币封顶顺序，跨级／满级／零XP、locked与额外技能页、关联trigger其它bits保持、真实UoW晚故障整体回滚及原协议顺序。

## Detailed Plan

1. 提取XP纯决定入口：输入原progress与已算不可变ExperienceBreakdown，返回Before／After／earned／LeveledUp；同一经验阈值服务唯一负责成长。引入带已决定结果的CharacterExperienceEffect，handler只验证预期progress并基本写After，不调用旧AwardAsync、不含估价delegate。零earned不写且不重算升级，满级保留完整earned而progress单独封顶，跨多级／角色缺失与零奖励区分。此接缝供下一单位关闭旧共用Award写入。
2. 领域一次读取owner／identity／当前DB progress、原carried／cleared状态、定义和已筛选reward、Main完整holdings＋实际Gold、所有技能页和build／bonus facts。reward按XP前当前DB level、旧job/grow筛选，保持selection／CompletionCount现有裁定。规则可查静态内容，不能读store／live world。
3. pure seeking projection按当前Main正ItemId实际数量和counts[0]=实际金币决定channel0，保留其余bits、夹取0..511；账号虚拟材料不冒充Main。保存原行与effective行分别。拒绝effects=[]、不调用ReportAsync暗写，不调用旧FinishAsync用陈旧stored trigger重查；反向stale（stored未归零但持有足够）可交付。需求品和需求金币只检查，仍不扣除。
4. 规则展开完成指定carried槽、清除标记（普通完成原来不删tracked），保留repeatable等当前gate及mark语义。关闭已无生产caller的旧完整Finish业务API／career完整Advance旧写入路径，必要底层基础setter和范围外caller保留并解释；不能仅把旧完整操作包成effect。
5. basic任务奖励保持旧默认core及创建策略，不能盲用Package.Prepare引入durability／seal／期限／能力／商城诊断。账号材料先明确分流GrantAccountMaterial；物理用具名GrantBasicCharacterItemEffect表达item/count与已知placement策略，handler复用CharacterContainerAddition循环基础Add（保留Avatar永久detail、Creature StartLevel等旧facts）。preview注册到同一standing并沿共享arrival能力，累计完整全部奖励、扣除腾槽及新槽；不复制一套placement。实际缺口RED：旧ItemGrant一次Add丢剩余量；修正完整数量到账，满包则全部拒绝，不改其它奖励入口。
6. shared standing只读输出最终Main holdings与GoldAfter；必须含未改动空间的旧Main物品、overlay已预检变化并正确映射Quick／各业务空间／native virtual，不能从奖励条数猜数量。任务规则用该snapshot计算奖励后的相关任务，不再次实现placement。金币effect明确原Gold、按XP前等级携带上限决定的After／实际增量；基础handler不重新算上限，preview沿同一Gold standing。物理＋账号＋非零金币项在结算前检查255 ACK条目上限。
7. 按已决定完整计划序列：完成任务→全部物理／账户奖励→金币→XP→GrowType→全部页逐技能删除／free重建→相关trigger最终行。技能freeLevel与slot在纯规则计划按XP后level、新grow决定，包含locked、额外页256、未知旧技能，保留active／unlocked／bonus。旧ResetAll隐藏build Ensure物化应显式具名effect，不留handler暗跑reset／reconcile。必要基础CAS接缝可复用TASK004；handler不得找技能或重新算前置。正常无转职不扩大技能重建范围。
8. 关联progress按原已完成集合＋本次quest、完成后carried、shared snapshot推导；seeking与quest-clear若修改同槽合成原DB→最终的一条effect，selected完成槽跳过，channel1—3保持。返回完整按槽排序通知snapshot。保留新技能→ACK→账号材料／XP／职业外观库存／可接任务／关联进度／weakness顺序；SP提交后派生，不新增GrantSP。异常不发送未提交snapshot，包装Inner以便后继识别结果不确定。
9. 新增有价值反例：原trigger0且物品消失，拒绝DB仍0／projection1（旧测试DB=1改成明确修复）；stored非零且已有品足够成功且需求品数量不变；StackLimit5已有4奖3得到5+2、空包奖12得到5+5+2、装备奖2双身份、无剩余槽整体拒绝；非默认旧堆字段保持／Basic新槽仍默认，与Opening不同。保留筛选选择、跨级／cap、两次当前DB估价、Gold旧级截顶后寻金、账号材料不算寻物、全技能页及协议ACK顺序。
10. 必须有生产SQLite同库UoW晚技能／trigger写故障：任务、物品、账号材料、金币、XP、grow、全页技能全部不变；替身只用于确定性规则或注入故障，不用不能回滚fake bag声称原子。dotnet串行，PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf，本任务与shared effects/XP/相关协议专项；保留当前完整suite两次不稳定失败到最终单位，不扩大修pool。记录RED／GREEN、实际pass/skip、源码写入分类，完成后停止writer。

## Planning Evidence

只读准备见../verification/preparatory-findings.md。Quest奖品basic字段、单arrival丢剩余量、CharacterContainerAddition既有完整数量能力已静态核对，尚未运行反例。TASK-004结果已接受，QuestsAsync提供真实raw Carried／Tracked／Cleared，SetCarried／SetCleared及SetLearnedSkill供基础逐条执行；Page允许256，Slot0..254，新增skill Extras默认[]（原转职新free技能也是[]），修改原row保留Extras。共有Validate只查shape，Apply顺序比较expected，carried保留原SQL trigger预期到flush。本计划可执行，未发现需要改变父路线或权限的缺口。

## Task Result — 2026-10-01

```yaml
kind: rks-verification
decision: pass
claim: TASK-005 bounded implementation and required automated checks
```

本结果只覆盖普通FinishQuest与所需XP纯决定／基础effects接缝。源码与自动验证为CurrentImplementation；共用AwardAsync／kill／clear、全入口请求重放、最终整合和实机仍是后继范围。未commit／deploy，未操作运行数据库或服务。生产功能writer已停止，控制器负责接受和下一单位。

### 实际实现与接缝

- `Server/src/Dfo.Server.Abstractions/Effects/QuestCompletionEffects.cs:13`：`ExperienceAwardDecision(Before, Award)`、`CharacterExperienceEffect`、`SetQuestRewardGoldEffect(Expected,Value)`、`SetCharacterGrowTypeEffect(Job,Expected,Value)`、`MaterializeSkillBuildsEffect(Active,ImmutableArray<CharacterSkillBuild> Expected)`、`GrantBasicCharacterItemEffect(ItemId,Count,Placement)`；完整计划的Effects为ImmutableArray，技能build事实复制为ImmutableArray。
- `IExperienceRuleService.DecideAward(CharacterProgressionState before, ExperienceBreakdown breakdown)`是纯入口，默认实现调用`World/ExperienceProgressionRules.cs:9`并使用既有`Progression().LevelThresholds`。返回完整earned breakdown和独立封顶progress。零earned不重算level；基础handler在Apply检查当前progress，只写已决定After，不含估价Func、不调用旧AwardAsync。整数组Validate先全部运行；两XP同actor预期依次为原值与第一条After的序列能累加。旧AwardAsync与其Advance暂留TASK-006收束，尚不能称共用writer已统一。
- `Application/Quests/QuestCompletionDomainService.cs:18`：读取owner、identity、当前DB progress、raw quests、Main、Gold，转职才读取技能／build；有效Seeking独立保存到拒绝projection；失败不调用Report或Apply。reward按XP前level／旧job／旧grow和原selection／count规则决定。Inventory preview与ACK条目上限通过后才构建并统一Apply完整计划。
- `Application/Quests/QuestCompletionEffectRuleService.cs:24`：Seeking以Main正ItemId实际数量＋counts[0]=实际Gold决定，账号材料不冒充Main；ReplaceChannel保持另外9bit channels。成功序列为carried完成／mark1→全部物理／账号奖励→Gold→XP→Grow→显式Build物化→每页逐条技能删除与free重建→相关carried最终trigger。tracked行沿旧普通完成语义保留。技能按XP后level／新career决定，包含locked、page256和未知旧技能；active／unlocked及bonus语义保留，新增free Extras为空，SP由提交后ledger派生。
- `IInventoryEffectPreflightDomainService.PreviewWithHoldingsAsync(context,effects,ct)`返回`InventoryEffectPreviewResult.Holdings`，内容为immutable最终Main和Gold。`InventoryEffectPreflightDomainService.cs:222`从完整Main叠加shared standing业务槽变化，映射Quick／Main空间并叠加native virtual，不凭奖励条数拼数量。现有PreviewAsync继续只做预检。本单位Basic grant与原stackable预检复用同一arrival循环，不复制placement算法。
- `Application/Effects/QuestCompletionEffectHandlers.cs:77`：Basic handler复用既有`CharacterContainerAddition.AddAsync`完整数量循环，保留旧Quest基本core／创建facts：新普通core默认字段0，已有同ItemId堆字段保留，Avatar永久detail与随机新身份、Creature StartLevel沿原创建能力。未调用Package Prepare，未加入商城期限／能力／Cera诊断限制。
- `Protocol/Game/QuestCompletion/FinishQuestCommand.cs:91`只调用新领域服务并将实际addition／Gold增量／Account变化／Award投影为旧outcome。`FinishQuestHandler`与codec保持，技能→ACK→账户／XP→career公开状态与库存→offers→关联progress→weakness顺序仍由原handler执行，协议专项覆盖顺序和SP提交后读取。

### 写入分类与关闭路径

旧IQuestLog.FinishAsync接口和QuestLog实现删除，旧ICharacterCareerDomainService／CharacterCareerDomainService及DI删除，Server/src无旧生产caller。IQuestLog.CompleteAsync基础状态verb与无关ResetAll技能系统仍保留。旧QuestFinishVerdict与4个测试替身额外未用Finish成员暂留兼容，不是生产API或结算旁路。

Quest完成领域服务只Apply。规则只读静态content并决定值，没有store／world写入；没有ExecuteReport、旧Finish、Career Advance、ResetAll或旧ItemGrant隐藏业务。基础handlers限定为progress比较＋SetProgress、Gold比较＋Add固定增量、Grow比较＋SetGrowType、显式Build比较＋Ensure、TASK004逐条技能CAS和既有容器基本全数量Add。共享Apply先认识全部类型、inventory预检及所有Validate，再按计划顺序Apply。没有新增寻物需求品／需求金币扣除；金币上限按XP前level决定，handler不重新估价。

### RED／GREEN与检查

真正行为RED保留在`verification/TASK-005/TASK-005-red.trx`：Seeking拒绝预期stored0却实为1；既有9/10堆奖3预期12却只得10。两项都实际失败，随后`TASK-005-green.trx`两项通过。新增其他测试为迁移／边界保护，不追称全部为RED。

| 检查 | 实际结果 | 证据 |
|---|---|---|
| 完整解决方案新鲜build | 0 warning／0 error | 本次tool输出，命令见下 |
| 本Task quest／career／log专项 | 74 passed，0 failed，0 skipped | TASK-005-quest-career.trx |
| Application最终关联专项 | 396 passed，0 failed，0 skipped | TASK-005-application-final.trx |
| Protocol最终关联专项 | 56 passed，0 failed，0 skipped | TASK-005-protocol-final.trx |
| Infrastructure真实PVF专项 | 58 passed，0 failed，1 skipped | TASK-005-infrastructure-final.trx |
| git diff --check | 通过 | 本次tool输出 |

全部TRX在`Document/tasks/item-effects-unification/verification/TASK-005/`。Infrastructure唯一skip为`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`，原反例在真实PVF可达配置下跳过，原因`An archive is reachable from this working tree.`。没有PVF unavailable跳过。全Application／全solution test未在本单位执行；TASK003记录的全suite计时／fixture-before-business SQLite disposed不稳定失败仍留最终整合处理，没有据此宣布全suite通过。

串行命令（每次设置同一环境）：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet build Server/86JP.Server.slnx --verbosity quiet
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-build --filter 'FullyQualifiedName~Quests|FullyQualifiedName~Experience|FullyQualifiedName~CareerReconciliationTests|FullyQualifiedName~SkillBuildResetTests|FullyQualifiedName~ApplyEffectsTests|FullyQualifiedName~ContainerAdditionTests|FullyQualifiedName~PackageOpeningTests|FullyQualifiedName~LotteryOpeningTests|FullyQualifiedName~VendingMachineTests|FullyQualifiedName~RecipeCraftTests' --verbosity quiet --logger 'trx;LogFileName=TASK-005-application-final.trx' --results-directory Document/tasks/item-effects-unification/verification/TASK-005
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-build --filter 'FullyQualifiedName~QuestCompletion|FullyQualifiedName~GrantKillExperienceCommandTests|FullyQualifiedName~GrantClearExperienceCommandTests|FullyQualifiedName~QuestProgress' --verbosity quiet --logger 'trx;LogFileName=TASK-005-protocol-final.trx' --results-directory Document/tasks/item-effects-unification/verification/TASK-005
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-build --filter 'FullyQualifiedName~PvfQuestParameterSourceTests|FullyQualifiedName~PvfQuestDefinitionSourceTests|FullyQualifiedName~PvfDungeonExperienceProgressionPolicySourceTests|FullyQualifiedName~PvfExperienceParameterSourceTests|FullyQualifiedName~PvfCreatureDefinitionSourceTests' --verbosity quiet --logger 'trx;LogFileName=TASK-005-infrastructure-final.trx' --results-directory Document/tasks/item-effects-unification/verification/TASK-005
git diff --check
```

### 原断言搬迁与证据边界

原Protocol FinishQuestCommandHandlerTests混合真实quest store、recording character、fake inventory、fake进度事件以及9参旧构造。现在Protocol文件保留receipt／异常映射专项，持久行为转入Application生产SQLite，逐项对应如下；未把构造失效当成删除保护的理由。

| 原断言 | 当前验证位置／保留内容 |
|---|---|
| AFinishedQuestPaysItsOwnAmountAndSaysWhereItLeftTheCharacter | OrdinaryQuestEffectTests:273：101 earned、1101总值与真实exp1101、clear flag1；原fake QuestCleared事件改为真实clear和依赖trigger结果 |
| ASecondCompletionAddsToWhatTheFirstLeftBehind | OrdinaryQuestEffectTests:143：合法再次carried按当前总值加、未携带重复不能再次付款；:273再次改变DBlevel后的估价与累计 |
| ACompletionWhoseAwardCannotHappenIsRefused | OrdinaryQuestEffectTests:342缺角色返回Unusable，无占位成功；:264直接基础handler缺progress时零award也必须拒绝 |
| AQuestWhoseObjectiveIsUnmetPaysNothing | OrdinaryQuestEffectTests:135未完成／跨owner零付款；SeekingQuestCompletionTests:41真实stored0拒绝且projection1／DB不变 |
| AQuestIsPricedAtTheLevelTheStoreHolds | OrdinaryQuestEffectTests:273同一fixture两次当前DBlevel1与40，后者earned更大且总值从首笔1101继续累加 |
| ARewardThatCannotBePlacedLeavesTheQuestCarriedThroughTheRealUnitOfWork | OrdinaryQuestEffectTests:75满包统一预检拒绝，任务／item／gold／exp均保持；:334 physical260条虽可放仍因ACK容量拒绝零写 |
| ALateExperienceFailureRollsBackRealQuestItemGoldAndExperienceWrites | OrdinaryQuestEffectTests:236，真实同库fault=xp／skill／trigger，准确SQLite故障文本，全部任务／items／account／gold／XP／Grow／skill／build／bonus恢复；比原混合fake更强 |
| RealQuest1776ActuallyGrantsOnlyJobZeroEquipment | OrdinaryQuestEffectTests:292真实PVF1776与SQLite，唯一物理装备101010437数量1、Gold101和ACK101；原fake QuestHoldingsChanged改为:105生产snapshot关联Seeking |
| 原Career直接Advance全部页、未知技能、错误owner／career、late grant及真实7840提交／回滚 | CareerReconciliationTests:20／50／65／83／104改走完整新任务领域／command并保留各原断言；不靠额外Reconcile补足结果；Ordinary:219额外locked／256／XP后level／build／bonus证据 |
| 旧QuestLog普通Finish gate | 普通gate搬到完整任务上述拒绝／重复／成功用例；QuestLog成功clear与repeatable再接受等测试继续使用基础CompleteAsync验证状态能力，未用新的假Finish wrapper重造业务 |

新增高价值反例还包含：反向stale已有需求品仍成功且不扣（:56）；空包12／23按完整堆数量（:68）；旧堆durability／attribute／seal／expiry保留、新堆零默认（:86）；装备双身份（:97）；未改Material和奖励Quick共同参与最终Seeking而账号材料不算Main（:105）；相关channel1保留与tracked保留（:118）；旧job/grow筛选后的selection0（:285）；oldlevel Gold cap与GoldAfter寻金（:176）；BasicAvatar永久detail／pet饱食100与StartLevel1（:303）；所有Validate先跑后的expected-after两XP（:151）；跨多级／满级仍earned／zero不写（:323）。

真实同库组合用生产CommandBus、UoW、SqliteDatabase、item／account／quest／gold／character／skill／build实现，静态任务／技能／stats／threshold替身只用于确定性规则。fault=xp／skill必须得到QuestFinishRejectedException且Inner精确SqliteException和`late xp`／`late skill`；fault=trigger精确原SqliteException和`late related trigger`，因quest raw section在UoW Prepare flush，位于command返回之后。此点没有伪称由command包装；事务仍全部回滚、bus不返回success。缺progress用例直接测试基础handler，不冒称完整actor归属证明。协议handler／codec用既有替身验证消息投影与順序，并不证明真实客户端。

下一单位可复用上述XPDecision／CharacterExperienceEffect及shared snapshot；需要关闭旧AwardAsync并迁移kill／clear，之后对普通任务等全入口补RQ008重放及Prepare／Commit结果不确定分支。所有任务条件触发／教程／recovery journal仍范围外。

## Controller Acceptance

控制器读取实际生产接缝、具体SQLite故障断言、原保护断言搬迁表及最终TRX，核对App396／Protocol56通过和Infra58通过／1既有missingarchive跳过，git diff --check通过。独立quest_stage_audit静态检查新增untracked文件及相对HEAD路径，未发现可复现本阶段遗漏／行为回归；未运行测试，不替代上述实际运行证据。接受TASK-005的有界结果，writer已停止。共用Award旧writer、全入口重放与最终生产同库整合仍未满足父验收，不以本阶段通过代替。
