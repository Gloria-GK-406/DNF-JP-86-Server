# TASK-006 — 共用经验与怪物／通关奖励统一执行

## Task Unit Contract

- Revision: 1
- Outcome and references: 共用earned XP只由纯DecideAward生成具名CharacterExperienceEffect并通过共享Apply落地，覆盖独立kill、canonical kill/helper/party及clear现有全部生产入口，关闭旧valuation delegate Award writer。Plan T-005，RQ-003/007/008/009，AC-002/006/007/008的经验部分。
- Dependency: 已接受TASK-005提供ExperienceAwardDecision、IExperienceRuleService.DecideAward、CharacterExperienceEffect及基础handler；TASK-001真实Apply／归属／Session保护；原event claim与TransactionalExecutionGate实际只读核对见preparatory-findings。App396／Protocol56／Infra58＋1skip及TASK005独立只读审计已由controller接受。
- Owned boundary: 共用经验领域API／实现／DI，GrantKillExperienceCommand静态helper、ApplyMonsterKill XP接缝、GrantClearExperienceCommand，solo／party／clear recipient session输入及相应自动回归、coverage和本Task结果。不重写怪物任务计数／pending loot／翻牌／战斗／任务条件／tutorial／恢复journal；请求guard与不确定fence排后继。无commit／deploy／运行库改变。
- Required checks: 当前DB level估价与连续合法发放，完整immutable earned breakdown／零／跨级／满级，生产SQLite的XP＋后继写入／晚Prepare联合回滚，归属与精确recipient会话，跨TResult真实bus等待完整提交，kill/clear既有claim／selective retry／通知顺序无回归，旧Award生产API与caller关闭。

## Detailed Plan

1. 共用领域服务增加具名ApplyAwardAsync入口，接现有ExperienceCalculationContext（Occasion已有Account／Character）、一致At和可选SelectedSessionIdentity；不接Func、任意effect closure或整个旧Award包装。领域从当前事务读取progress，原IExperienceCalculationDomainService.CalculateAsync按当前DB level计算固定breakdown，再纯DecideAward生成单个CharacterExperienceEffect数组，shared Apply并从CharacterExperienceChange返回同一Award。缺角色返回null；零earned返回正确原standing且不升级，owner／provided exact Session仍受保护。一次规划一次估价，不重试重算已提交事件。
2. 删除旧AwardAsync(CharacterId,Func<...>)接口／实现及private Advance，所有production caller改走新入口。ThresholdForLevel／TrySetProgress保留，仅tutorial direct level2使用；基础SetProgress保留供effect/tutorial/recovery journal，不宣称这些范围外已迁移。现基础XP handler已按决定值写且拒写throw，不能为了旧null语义吞异常；结果明确说明旧拒写正常null改为整批失败。
3. GrantKill的独立handler和internal静态helper均改适配；ApplyMonsterKill仍直接调用共享helper／领域，不嵌套bus。KillExperienceContext保留difficulty／monster level/rank／weight／冻结party count。AI/APC仍null XP，原pending item／quest facts随后在同一UoW继续，不能把这些范围外变化混成完整monster effect。GrantClear用ClearExperienceContext，ClearBase／RankBonus从实际award breakdown投影。
4. 三command可追加可选精确recipient Session，真实solo派自身、party派各recipient、clear派自身；At一次取自TimeProvider／现有事件时间接缝，不用reporter session套其他成员。传Session与Occasion不一致必须拒绝无写；可信离线直接调用明确传null。为后继统一fence保留可表达exact session的输入，不在本单位建立新的replay政策。
5. 保留三command原Transactional注册与现有57全局gate，不加AccountExclusive；Order57跨TResultsingleton、UoW60提交／回滚在释放门前。当前expected-read＋SetProgress不是SQL CAS，测试及结果说明实际串行依据。CommandBus拒绝nested dispatch，所有生产派发从协议handler，不能再声称已有并发XP缺陷。
6. 保留原event claims：solo run/actor/recipient一次，party逐成员transaction及CompletedKillEffects仅重试失败成员，clear run/character一次且新run合法；kill summary用完整breakdown Base／GrowthContract而非progress差。发送失败不再次估价或发奖。后继uncertain分支及bus正常返回后claim publication失败已列preparatory，保持pending但不以本单元完成声称AC007全满足。
7. 保留通知原顺序：solo XP→Death→autoempty→quest→offer→clear；party XP→sharedDeath→offer→quest→autoempty；clear PlayResult→XP→offer→ClearReward。SP提交后派生不加SP effect。原calculation领域仍收集premium/session/channel／spirit事实，rule不能查world；逐modifier floor、blackdiamond basis含rank、其他basis不含rank不变。
8. 先构造有意义旧旁路反例：真实common Award或生产kill/clear bus里检查shared effect execution trace与后继故障整体rollback，旧代码不能满足新契约；真正RED只记录实际运行失败。迁移CharacterExperienceDomainServiceTests、GrantKill/GrantClear PricingGrant等测试，逐项保留当前stored-level／breakdown／cap／missing／zero／tutorial断言，不为构造改动删保护。可用静态content/calculation替身保持确定性，关键store/executor/UoW用生产SQLite。
9. 生产真实同库integration：XP之后真实quest写／latePrepare失败恢复progress；clear持久失败没有successresult；连续合法event按新DB level估价且满级earned完整；wrong owner/stale session无写。增加跨不同TResult真实bus gate用例：首command在Prepare等待，第二XP不得估价／开启事务，释放后读首提交的standing；首失败后下一可执行，不残留gate／ambient事务。现同类Probe gate测试不替代这个业务整合。
10. 串行dotnet，PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf。先新checks再完整build和相关Application（Experience/Effects/Monster/Quest）／Protocol（MonsterDeath/DungeonClear/QuestCompletion）及对应真实PVF回归，保留实际TRX／skip原因。扫Server/src全部Award／SetProgress／helper调用，逐项归类写入及已知后继，更新coverage和本Task结果／原测试搬迁／替身边界，完成即停writer。最终fullsuite及pool/计时旧失败保留后继，不扩生产pool修复。

## Planning Evidence

common_xp_readonly核对全部实际caller及全局gate，根控制器复读TransactionalExecutionBehavior、CommandBus、XP接口／实现及solo/party/clear调用。此前缺AccountExclusive→缺总串行的推论已明确撤回并记录preparatory；新单位不沿错误推断扩锁。实际依赖已可用，父结果／范围／权限不变，本计划可以执行。Guard／fence／全同库整合仍须后继逐项完成。

## Result — 2026-10-01

共用earned XP已从旧delegate writer迁到具名ApplyAwardAsync。当前事务DB progress决定估价level，原Calculate生成固定breakdown，纯DecideAward构建一个CharacterExperienceEffect，再由shared Apply落地；对外返回实际CharacterExperienceChange的同一个Award。ICharacterExperienceDomainService及实现中的旧AwardAsync与private Advance已删除。缺角色仍返回null；zero仍保留standing且不写／不补升级；底层拒写现在抛异常并回滚整批，与已接受的XP handler语义一致，不再正常返回null。

GrantKill独立handler与静态helper、ApplyMonsterKill XP分支、GrantClear全部接入。solo传自身Session，party逐recipient传自己的Session，clear传自身Session，At一次取自TimeProvider。AI／APC保持null XP。原monster任务条件／pending item、event claims、party selective retry、clear事件边界和通知顺序保留；未增加AccountExclusive或OnlineUser锁，三条Transactional注册与既有Order57全局门不变。

### 当前实现检查

```yaml
kind: rks-verification
decision: pass
claim: TASK-006 owned implementation and required automated checks
controller_acceptance: accepted
```

先新增生产clear bus的跨账户反例再改production：旧Award路径没有走共享归属验证，`A_clear_cannot_award_another_accounts_character`实际运行失败，`Assert.Throws() Failure: No exception was thrown`，退出1、1失败。记录为[TASK-006-red.trx](../verification/TASK-006/TASK-006-red.trx)。这不是构造编译错误或替身强行失败；其预期拒绝和零持久写是共享effects契约。改后该反例连同zero-earned跨账户拒绝、real executor trace、晚Prepare故障等专门回归23项通过。中途测试适配编译／fixture错误已修正，不作为行为RED。

| 检查 | 当前结果 | 证据 |
|---|---|---|
| 完整`dotnet build Server/86JP.Server.slnx --verbosity quiet` | 退出0；0警告／0错误 | [build](../verification/TASK-006/TASK-006-build.txt) |
| Application `CommonExperienceEffectTests`＋`CharacterExperienceDomainServiceTests` | 23通过／0失败／0跳过 | [green TRX](../verification/TASK-006/TASK-006-green.trx) |
| Application Experience／ApplyEffects／EffectExecution／OrdinaryQuestEffect／Quests／Monster／TransactionalCommandSerialization／UnitOfWork／CommandHandlerArchitecture | 385通过／0失败／0跳过 | [Application TRX](../verification/TASK-006/TASK-006-application.trx) |
| Protocol MonsterDeath／DungeonClear／QuestCompletion／CommandRegistration | 197通过／0失败／0跳过 | [Protocol TRX](../verification/TASK-006/TASK-006-protocol.trx) |
| Infrastructure PvfExperienceParameter／PvfDungeonExperienceProgressionPolicy／ChannelScriptExperience／PvfQuestParameter／PvfQuestDefinition | 67通过／0失败／1skip | [Infrastructure TRX](../verification/TASK-006/TASK-006-infrastructure.trx) |
| `rg -n '\bAwardAsync\(' Server/src` | 无匹配，旧production API／caller关闭 | 当前静态扫描 |
| `rg -n 'SetProgressAsync\(' Server/src` | 实际writer只余XP basic handler、tutorial TrySetProgress、recovery journal；另有table定义 | 当前静态扫描 |
| `git diff --check` | 无错误 | 当前diff检查 |

所有dotnet串行，测试均`--no-build --verbosity quiet --logger trx`，结果目录`Document/tasks/item-effects-unification/verification/TASK-006`，环境`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。唯一skip为`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`，原因`An archive is reachable from this working tree.`；真实档案经验／任务用例实际执行，未把档案不可达算通过。上述筛选集合含重叠测试，数字不相加作独立覆盖数；未声明fullsuite／实机通过。

### 决定性事务证据与替身边界

- `All_three_commands_report_the_applied_award_and_exact_execution_context`走真实CommandBus／SQLite／shared executor，逐条验证返回Award与实际XP change为同一引用、source为具名kill／clear、精确Session及固定At。Protocol既有party测试新增leader／member各自Session顺序断言，solo／clear替身在实际协议入口检查自身Session。
- `Quest_flush_failure_after_experience_restores_both_durable_ledgers`使用生产character table、quest store及同库SQL trigger。XP已Apply后，真实QuestStore在UoW Prepare更新任务触发SqliteException；bus不返回成功，外部连接读取XP=0及原task trigger=1，移除故障后同command可正常XP=30／trigger=0。
- `Clear_late_prepare_failure_returns_no_success_and_leaves_the_next_award_usable`在XP执行后enlist事务参与者，Prepare抛IOException；SQLite XP回滚，ambient为空，下一clear可用。该故障发生在真实UoW Prepare阶段，参与者为测试注入，不能称作真实SQLite Commit故障。
- `Different_result_commands_wait_for_prepare_then_price_committed_or_rolled_back_progress`分别覆盖提交和失败：先clear TResult在Prepare等待，再派canonical kill另一个TResult；后一command不完成也不估价，外部DB仍原exp=90。释放后提交分支按level2估价total=150；失败分支按原level1估价total=120。两个估价都在真实事务内，最终持久值一致，gate／ambient不残留。真实串行依据是Order57 singleton门包住UoW60全过程；expected-read＋WHERE character_id更新没有SQL CAS语义。
- 归属／过期代次／Session角色不一致均拒绝且无XP写；离线null Session有真实领域测试。确定性content、progression阈值、calculation、quest definition使用替身；effect trace只是包装生产ApplyEffectsDomainService，SQLite表／store／executor／bus／UoW均生产实现。未把测试用pricing-only grant当成真实持久化证据。

### 原断言搬迁清单

`CharacterExperienceDomainServiceTests`原14项均保留保护，award用例从RecordingCharacterTable迁到生产SQLite fixture、真实effects与事务；audit trigger记录是否实际写，确定性阈值仍为100／250／450／700。测试fixture内的Func仅提供固定估价，不是production API。

| 原测试 | 新入口／保护 |
|---|---|
| AnAwardRaisesTheStoredTotal | ApplyAwardAsync；持久total及audit写值 |
| AnAwardPastAThresholdLevelsTheCharacter | 单阈值升级／持久level |
| AnAwardPastSeveralThresholdsCrossesThemAll | 跨多级 |
| TheValuationIsAskedWithTheStoredLevel | 当前DB level传给calculation |
| ASecondAwardAddsToWhatTheFirstLeftBehind | 连续合法发放累计 |
| ContributionsAreAddedAndCarriedBackNamed | 固定全分量breakdown |
| AnAwardOfNothingLeavesTheStoreAlone | zero保standing／无audit写 |
| AWriteTheStoreRefusesIsNoAwardAtAll | 改名AWriteTheStoreRefusesAbortsTheAward；真实拒写SqliteException及rollback／无audit写，不吞整批失败 |
| ACharacterTheStoreDoesNotHoldHasNoStanding | missing仍null／无placeholder |
| ATotalCannotRunPastTheLastThreshold | progression上限 |
| CrossingTheCeilingKeepsTheRewardAndCapsOnlyProgression | earned与cap分离 |
| ACharacterAtTheCeilingKeepsTheFullRewardBreakdown | 满级完整earned分量 |
| AnAwardCanSayWhereItCameFrom | 命名分量／字符串表现 |
| TutorialRequestWithStaleLevelNeverLowersCurrentProgress | 原tutorial fake／TrySetProgress保护保留 |

GrantKill／GrantClear command tests的PricingGrant从旧Func适配为具名context入口，并在测试替身内调用原calculation；原difficulty／weight／party count／modifier floor／clear rank与结果投影断言未删。AdvanceHuntEnemyQuestCommandTests只改构造依赖与fakes接口，原AI／APC无XP、冻结party人数、XP与任务次序／失败保护保留。MonsterDeathHandlerTests及SetPlayResultHandlerTests保留原claim／失败成员重试／新run合法／通知顺序与满级earned报告断言，并新增精确recipient Session检查。

### 范围与交接

本单位没有修改三command注册、全局gate、原计算规则、怪物／任务条件、物品奖励和controller registry。tutorial direct TrySetProgress及DungeonEffectRecoveryService恢复journal保留。TASK-007仍需按证据处理uncertain／session fence／guards／正常bus返回后claim publication失败；本结果不代表父AC-007全部满足。最终全suite、同库其他分区整合与实机验收由后继承担。

owned源码为共用经验interface／service、GrantKill／ApplyMonsterKill／GrantClear、MonsterDeathSlice／DungeonClearSlice；owned测试为新CommonExperienceEffectTests、原CharacterExperienceDomainServiceTests及MonsterDeath／DungeonClear上述五个测试文件。只更新本Task结果与coverage，TRX与build落本Task verification。未提交／部署／触碰运行库，检查结束后停止writer，等待根控制器独立结果审查与状态推进。

### Controller Acceptance

根控制器已复读当前API／调用者、结果、TRX counters与build记录；385 Application／197 Protocol／67 Infrastructure＋上述1skip及diff检查符合本单位边界。独立xp_stage_audit只读核对新未跟踪测试、生产bus／SQLite／gate与通知调用者，未发现可复现的阶段缺陷；该审计未重新执行测试。接受本单位，父任务的生命周期／uncertainty／重放和最终整合仍未完成。
