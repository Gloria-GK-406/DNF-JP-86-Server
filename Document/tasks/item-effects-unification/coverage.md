# 物品效果迁移覆盖

基准 `b5506390`；当前总表记录 TASK-001—TASK-011 未提交实现；下方各阶段段落保留历史证据，表中状态以最后整合结果为准。源码与自动测试证据为 CurrentImplementation。

| 分区／入口 | 规则构建 | 显式效果 | 执行／底层写入 | 验证与状态 |
|---|---|---|---|---|
| 普通使用源消费（USE_STACKABLE） | ItemUseEffectRuleService.Build，包含完整BusinessSlot、预期ItemID；合法不自耗与缺内容拒绝分开 | ConsumeFromContainerEffect | ConsumeFromContainerEffectHandler每次读事务当前槽位 → ItemContainerOperations.ConsumeAsync → business container Put/Clear | TASK-001已迁移；同槽累计／超额回滚／跨栏／未知／换物／过期测试，普通自耗、不自耗和最后清槽既有回归 |
| 普通使用冷却 | 同一规则列表，cooldown key沿现有判断服务 | StartItemCooldownEffect | StartItemCooldownEffectHandler → 非失败提交参与者 → cooldowns.Start；提交时校验Session代次 | TASK-001已迁移；真实回滚RED→GREEN、Commit故障与旧会话检查 |
| 精灵秘药 | 同一规则列表，固定bonus rate和duration | StartSpiritPotionEffect | StartSpiritPotionEffectHandler → 非失败提交参与者 → SpiritPotionDomainService.ApplyForSession | TASK-001已迁移；既有回滚／退出／替换会话回归及Commit故障检查 |
| 宠物饲料24 | 同一规则列表，输入当前pet UID，列出消费1与饱食+30 | ConsumeFromContainerEffect + AddPetStomachEffect | AddPetStomachEffectHandler → pet.AddStomachAsync；沿实例Synchronize／Stage／Notify | TASK-001已迁移；Feed API已删除；011实际同库PersistentItemContainer＋SqliteCreatureDomainStore，晚facet flush后Prepare失败source2／stomach50且无投影、retry1／80；原实例时钟／待发经验回归保留 |
| 共享目标归属 | EffectExecutionContext明确账户／角色／时间／可选Session | 全部效果共同前置条件 | ICharacterDomainStore.SessionIn(tx).HoldsAsync；有Session时校验精确当前会话 | TASK-001已实现；跨账户RED→GREEN、离线消费与模块解析通过 |
| 范围内18Request／20wire／20main重试 | 完整raw hash/length＋allframe ordinal＋exactSession，Fresh与重传分开 | typed实际effect footprint提交前冻结，Committed只刷新当前绝对状态 | physical CWT ledger→45 exact recipient→60 Freeze→58同步Commit／confirmedAbort／typedBlocked→009Exclusive batch | 010实际DI集合及真实bus/SQL RED→GREEN、128窗口／fullround／发送失败／会话隔离；011完整回归，seq-reset Unknown |
| 强化／增幅（含保护和失败产物） | EquipmentEffectPlanRuleService.ReinforcementAsync复用报价、成功判定和失败规则；失败抽签仅在原成功判定为false时发生 | ConsumeFromContainer／ConsumeAccountMaterial、SpendGold、条件消保护、SetEquipmentUpgrade或DestroyEquipment、全部显式Grant | 共享执行器 → 基础材料／金币handlers、装备实例setter／Destroy、常规placement／账号Add | TASK-002已接受；成功／保护／损毁计划、失败奖励原顺序、后续故障全部回滚既有与新增回归 |
| 宝珠附魔 | EquipmentEffectPlanRuleService.EnchantAsync复用卡片适用规则 | ConsumeFromContainer + SetEquipmentEnchantment | 基础消费handler + 实例SetEnchantmentAsync | TASK-002已迁移；适用／拒绝／持久化／并列字段回归 |
| 异次元清除／净化／赋予／扭转 | EquipmentEffectPlanRuleService.OutworldAsync复用EquipmentUpgrade规则 | ConsumeFromContainer + SetEquipmentDimensional | 基础消费handler + 实例SetDimensionalAttributeAsync，保留其他当前字段 | TASK-002已迁移；材料／目标限制／首饰／红字、真实PVF与帧回归 |
| 魔法封印 | EquipmentEffectPlanRuleService.MagicSealAsync复用封印规则并复制只读options | SpendGold + SetEquipmentMagicSeal | 基础金币handler + 实例SetMagicSealOptionsAsync | TASK-002已迁移；封印选择／字段／金币／提交与帧回归 |
| 锻造 | EquipmentEffectPlanRuleService.ForgingAsync复用锻造概率及武器规则 | ConsumeFromContainer，成功时SetEquipmentForging | 基础消费handler + 实例SetForgingLevelAsync | TASK-002已迁移；成功／失败／非武器拒绝／真实PVF与帧回归 |
| NPC／便携共用分解 | EquipmentEffectPlanRuleService.DisjointAsync含机器适用／等级与全部产物 | 便携ConsumeFromContainer + DestroyEquipment + 全部显式物理／账号Grant | 共用基础handlers；最终receipt读取实际槽位，奖励保留增量 | TASK-002已迁移；机器最后一件释放格RED→GREEN、账户／物理混合、真实SQLite提交失败回滚及拒绝码回归 |
| 整数组只读库存／账户／金币预检 | InventoryEffectPreflightDomainService按effect顺序累计standing，执行器在所有类型识别后重复保护 | 消费／销毁腾槽、多个物理产物竞争、账户先扣后加、累计金币、UID身份 | 只读；扩展IInventoryEffectPreviewHandler共享IInventoryEffectPreviewState，未注册InventoryEffect拒绝 | TASK-002已实现；同UID多字段、累计超支、账户上限、销毁后旧UID、真实SQLite晚提交故障回归；prepared grant由T-003接入 |
| 设计图制作 | OpeningEffectPlanRuleService.RecipeCosts／Build，源规则声明自耗、所有物理／账户成本和产物 | ConsumeFromContainer／ConsumeAccountMaterial + GrantPreparedItem + Cooldown | 共用执行器／底层容器；OpeningInventory仅flush／最终投影 | TASK-003已接受；真实PVF、限容、材料、零晶体、回滚 |
| 普通／自选／时装包、魔盒单／批次 | 原一次Roll／Prepare + 完整Build | 源／钥匙消费、prepared／virtual创建、权益期限、luck终值、冷却 | 五类具名handler／共享standing，保留能力期限petfacts及最终槽UID | TASK-003已迁移；合法不自耗及2040永久源RED→GREEN，权益／幸运／晚故障 |
| 袖珍罐／双倍／金币 | 只读DoublePotFacts含raw round／total，同At规则决定next与全部资产效果 | SpendGold、源消费、GrantPreparedItem、SetDoublePotUsage、Cooldown | 只按已决定值基础写入，两个raw expected条件更新 | TASK-003已迁移；一次抽签、独立UID、限额／跨日／失败／重试 |
| 售货机币开物品 | 原黑钻／城镇资格、一次weighted选取后Build明确币成本／奖励 | ConsumeFromContainer + prepared／virtualGrant + Cooldown | 共用执行器；010统一账本，旧sequence缓存已删除 | TASK-003已迁移；普通／高级币、满包、并发、最后一枚、ACK顺序 |
| 疲劳药 | SpecialConsumableEffectPlanRuleService.FatigueAsync读取raw fatigue／usage，同一At归一化，完整恢复量及次数／双冷却规则 | 固定源Consume1、Cooldown、SetFatigueState、SetItemUsageState | 基础CAS写完整Used／ConsumedToday／UpdatedAt与次数／LastUsedAt，不调用Refund／RecordUse | TASK-004已接受；真实同库源数量＋fatigue＋usage＋内存冷却成功／usage晚失败回滚，跨日raw只读与quota既有回归 |
| 任务券 | 原catch-up选择＋CouponAsync纯规则，先计算selected删除、newly cleared及关联channel0终值 | Consume1、Cooldown、SetCarriedQuest、RemoveTrackedQuest、SetClearedQuest | 按真实槽位／原行基础写；CompleteWithoutRewards公开完整旧业务已删，不调用Report | TASK-004已迁移；carried／uncarried、重复tracked洞、nonzero mark、9bit packed其他channels、无奖励／晚失败回滚；011真实Main物品源＋quest表同库，clear flag late trigger回滚／retry；旧标量unit保留 |
| LearnSkill退款／遗忘水 | RefundSkills决定含退款整批技能／槽／最终前置／SP及TP ledger，RefundAsync决定一次水与每条技能effects | Consume1、Cooldown、SetLearnedSkill（删除／更新／新增） | 基础技能table操作，逐条原page／slot／skill／level校验；ledger是只读计算结果 | TASK-004已迁移；混合学习／退款扣一次、晚INSERT失败整批恢复、其他页不变；011真实水3 Main69＋mixed skills同库，skill3晚INSERT全回滚／retry；旧标量unit保留，普通无退款路径豁免保留 |
| 宠物改名卡25 | Rename复制名称为ImmutableArray；旧空名称／12字节／实际lock／永久表示保留，CreatureConsumable既有binder local0..50 | Consume1（ExpectedEquipmentLockId快照）＋RenamePet | 基础consume＋当前UID／template校验后pet.RenameAsync，不推进成长 | TASK-004已迁移；源数组变更、非法名／错UID／过期拒绝，2050永久锁卡及pet、其他成长字段不变；011真实pet／卡同库，晚facet flush后Prepare失败保留源／名称／growth，retry成功；旧替身unit保留 |
| 普通任务交付／完整奖励／转职／关联进度 | QuestCompletionEffectRuleService：有效Seeking与旧级reward筛选；shared preview最终Main／Gold；XP纯决定、新career下全页技能、关联channel0终值 | 完成carried＋clear mark、Basic／AccountGrant、决定GoldAfter、CharacterExperience、GrowType、Build物化、逐条Skill删除／重建、最终carried trigger | QuestCompletionDomainService一次收集facts→完整计划→共享执行器；Basic复用CharacterContainerAddition完整数量基础Add；无Report／旧Finish／旧Career完整writer | TASK-005已接受；两条真实行为RED→GREEN；生产同库晚XP／skill／UoW Prepare trigger故障全部回滚；真实1776筛选与7840转职、BasicAvatarCreature、全页256／locked／bonus保真；普通拒绝只读projection；010 unified request replay已迁移 |
| XP纯决定／基本写入接缝 | IExperienceRuleService.DecideAward(before,breakdown)→ExperienceAwardDecision；阈值来自同一Progression服务 | CharacterExperienceEffect携带完整已决定award与before，无估价delegate | handler仅Apply顺序查progress并写已决定level／total；zero不写；同actor两条expected-after可累计 | TASK-005供任务使用，TASK-006接入共用奖励；跨多级／cap完整earned／zero不升级／缺目标、全Validate先跑后序列Apply通过；无新增AccountExclusive，旧AwardAsync生产API已删 |
| 共用经验奖励／怪物通关入口 | ApplyAwardAsync接具名ExperienceCalculationContext，读事务当前DB level→原Calculate→纯DecideAward；保留完整earned breakdown | 单个CharacterExperienceEffect；返回实际CharacterExperienceChange的同一Award | shared Apply→既有XP基本handler；GrantKill独立／helper、ApplyMonsterKill、solo／party各recipient、GrantClear均接入，传各自精确Session与一次At | TASK-006已接受；归属真实RED→GREEN；SQLite XP后真实quest Prepare失败联合回滚、clear晚Prepare失败、跨TResult真实bus门与提交／回滚后估价通过；原claim／retry／通知顺序回归通过，008精确recipient／typed uncertainty与事件claim保护已完成；XP不属于20main请求账本，保留其事件claim |
| 最终写入扫描／整合回归／人工验收 | 011动态全Abstractions effects注册与活跃caller分类 | 现有全部具名effects，未知明确拒绝 | 四组实际同库／Host唯一绝对tempDB／8项目串行完整回归 | 见011检查与下方当前豁免；人工清单全部HumanPending，待正式父审查 |

TASK-007完成事务不确定分类和生命周期租约基础，待controller接受：具名异常递归Inner／Aggregate分类，DomainTransaction／UoW Abort保留原cause与rollback cause，SQLite durable后cleanup及操作＋cleanup双cause全部保留。Selected专属BorrowOrAcquireShared同步frame／真实取得／引用计数保护，explicit Acquire FIFO不变，父关闭不释放未结束child、延迟继承closed frame不借；Observer／Return统一lifecycle→online，保持原captured session保护及orphan／无selected路径。真实旧锁序2项RED和旧违约类型1项RED，最终完整build0警告0错误、Application88／Protocol88／全Persistence392通过0skip，详见[TASK-007](tasks/TASK-007.md)。尚未接命令pipeline、fence、精确close／pet discard／claim保护或全入口replay，AC-007不能据此标完成。

TASK-006经验扫描：Server/src无旧AwardAsync及估价Func入口，共用服务不直接SetProgress；实际earned XP只生成具名effect。底层SetProgress仍供XP handler、tutorial TrySetProgress和DungeonEffectRecoveryService恢复journal，后二者范围外。三XP command保留Transactional注册；既有singleton TransactionalExecutionGate Order57包住UoW60完整Prepare／Commit／Rollback，真实clear与canonical kill不同TResult测试证明提交后新level估价、失败后原standing估价且下一命令可用。expected-read加WHERE character_id更新不是SQL CAS，本单位没有扩Account锁。测试用静态content／calculation和effect trace装饰器，但executor、character／quest表、SQLite连接、CommandBus及UoW为生产实现；latePrepare为注入的事务参与者Prepare故障，不当作SQLite Commit故障。完整构建0警告／0错误；Application385、Protocol197全通过0跳过，Infrastructure67通过1skip（可达真实档案时缺档案反向用例），证据及14项原经验断言搬迁见[TASK-006](tasks/TASK-006.md)。事件正常返回后的claim publication失败、uncertain fence、完整guard及实机验收仍待后继。

宠物实例内部的时钟推进、待发房间经验同步、成长和演化属于保留的底层实例职责；本单元没有重写宠物成长。其他领域的旧写入不作为本单元已迁移，也没有用opaque旧业务effect包装隐藏它们。

本分区写入扫描：UseStackable切片与ItemUseEffectDomainService没有直接消费／冷却／秘药／Feed写入；实际变化都在具名handler及已有底层实例／store完成。检查命令及结果见 [TASK-001](tasks/TASK-001.md)。后续handler扩展仍须解释其自己的底层写入并更新对应分区。

装备分区写入扫描：六个已迁移领域服务没有直接Consume／TrySpendGold／Set／Destroy／Add结算，规则只构建具名effects；handler复用的装备instance每次以UID读取当前Core并修改指定字段，保留所有权及事务寿命保护。修理、战斗耐久属于本单元外已有直接写入，不声明全仓库迁移完毕。共享消费guard补齐便携机器的过期／锁定拒绝；分解NoRoom沿InventoryFull，其余前置／不支持拒绝沿既有通用拒绝码。证据与明确行为修正见 [TASK-002](tasks/TASK-002.md)：Application相关回归400、Protocol38、Networking帧3，全部通过、0跳过；实机验收未执行。

制作／开包扫描：PackageOpeningDomainServices、RecipeCraftDomainService、LotteryOpeningDomainService、VendingMachineDomainService、OpeningInventoryDomainService均无直接业务写入或旧Settle。五类新handlers承接基础创建／virtual／premium／luck／quota；OpeningInventory仅flush和最终receipt投影。完整结果及API见 [TASK-003](tasks/TASK-003.md)：最终Application专项130／Protocol42／Networking5均通过且0跳过，全Application两次各有1失败（计时、fixture-before-business SQLite disposed），未声称全suite通过；实机未执行。

专用品扫描：三个Opening消费者已删除SettleDeferredConsumption接口；fatigue／coupon／naming入口无直接Consume／Refund／RecordUse／CompleteWithoutRewards／Report／基础Rename写入。含refund整批技能在第一写入前分流到完整effects；剩余直接技能table写入仅为原普通no-refund路径。基础handlers只做已决定值CAS与基础pet.Rename。RefundAsync／RecordUseAsync领域API保留，目前Server/src扫描仅定义，无生产调用；CompleteWithoutRewards完整业务API及实现已删除，旧测试语义转为coupon effects回归，部分测试替身剩余同名未用成员不构成生产旁路。证据见 [TASK-004](tasks/TASK-004.md)：新鲜整方案构建0警告／0错误，Application专项222／Protocol70／真实PVF声明16全部通过且0跳过。三种专用品原固定自耗1行为保留，不因为通用规则IsConsumedOnUse而暗改为不自耗；改名卡同样固定1。

阶段接受不等于父AC-007全部满足。独立opening_stage_audit确认Package／Avatar／MagicBox无sequence保护、Lottery只有最后一条缓存；控制器确认普通使用、疲劳、任务券、改名、技能退款、普通交付缺guard，强化简单cache缺Fresh／ordinal旧帧门。最终重放单位逐项补齐并证明合法下一次／wrap、穿插后重传、发送失败及会话隔离；在此之前不将“迁入effects”当作已安全重复结算。

普通交付扫描：FinishQuestCommand只调用新的完成领域服务并投影receipt；QuestCompletionDomainService无直接结算写入，规则无store／live读取。旧IQuestLog.FinishAsync和CharacterCareerDomainService.AdvanceAsync的接口／实现／DI已删除；IQuestLog.CompleteAsync作为既有基础状态verb保留，额外测试替身残留未用Finish成员和旧verdict枚举不构成生产旁路。具名handlers仅做决定值检查与SetProgress／Gold.Add／SetGrowType／Build.Ensure／逐条Skill CAS／容器完整数量Add。金币和XP只在Apply检查expected，不在Validate按初始standing误拒expected-after；普通任务没有新增需求材料或需求金币扣除。

TASK-005的生产SQLite组合使用同一数据库、实际item store、account bag、quest store、gold table、character table、skill/build table和CommandBus UoW；静态content、技能表和经验阈值是确定性替身。晚XP／skill在command内部抛出并保留SQLite cause；关联trigger在QuestStore的UoW Prepare flush抛出原SqliteException，所有状态仍回滚且命令没有返回成功。后继不确定提交／重放处理必须覆盖command之后的Prepare／Commit边界，不能仅检查包装异常。结果、原测试逐项搬迁、API与命令见[TASK-005](tasks/TASK-005.md)。最终构建0警告／0错误；Application396与Protocol56全部通过0跳过，Infrastructure58通过1跳过（真实PVF可达时既有缺档反例跳过）。未运行全suite或实机，本单位不声明父scope／AC-007完成。

## TASK-008 生命周期与不确定出口补查

23个范围内命令均ISelectedSessionCommand＋Transactional，45精确recipient捕获／生命周期Shared借用→50／55／global57→58fence／authorization→UoW60；Finish网络Session已进入actual effect context，三XP／Finish可信offline null仍支持。Safety同步建立不可由Fresh／Bind清除的exact-session／物理对象CWT fence；discard不Advance或写DB，Leave无匹配entry不回写。生产exactclose按原对象核对并捕获Session detached，替代相同ID对象不误关。

装备／制作／全部opening／special／Finish正常失败catch不吞recursive uncertainty或授予confirmed retry。kill／clear bus返回立即durable标记，claim publication false／null／throw／cancel／clear当前进度缺失不回到可重复付款；party精确recipient物理对象与reporter分开。真实XP／SQLite durable故障、queued 57／dispatcher、确认rollback retry、wrapper／rollback failure、pet durable覆写保护、actualregistry ABA及受控lifecycle callback自动证据均见TASK-008；claim分支替身测试和真实SQLite证据明确分列。

最终Application702（含新补actual FatiguePotionHandler）＋独立actual Vending12，Protocol1218，Networking23，Persistence Database17，均0失败0skip；build0警告错误，diffcheck clean。尚未统一全入口replay／current readonly snapshot／整组编码／raw hash／effect footprint；普通Use与special／quest重复提交仍待后继单位，不能据本阶段宣称AC-007全完成。无部署、无实机验证。


## TASK-009 当前纯投影与预编码发送

InventoryEntry具名ReadCurrent仅repository＋projection，无LegacyWornNormalizer；Opening最终receipt、Recipe Refresh、Select current-view主角及support均pure，entry维护保留。新bounded immutable snapshot经ReadReplayStateCommand Transactional/ISelectedSessionCommand、真实owner校验和前后exact safety，在单SQLite read snapshot中按needs读取，不重播selection59帧、不nested bus／维护写／pet Advance。snapshot At明确覆盖fatigue、usage period/cooldown、premium usage和rolling display；外观pet marker纯规则按同At完成。

Protocol只刷新当前绝对状态：六inventory/14virtual零余额、当前空物理槽-1（金ID0分开）、pet/skills/quest/currentXP无earnedbonus/fatigue/usage reset＋255 chunks/premium CMD036F/必要NOTI2。触及物理地址与needs未知或不支持明确拒绝，pet/skill u8和inventory u16沿actual encoder；source/outbound arrays两向复制。outerExclusive保护read到batch，45借已held实际Exclusive；Shared拒升、pending/closed/canceled不假借。此修改由productionbus＋真实SQLite GoldProbe pre-send barrier旧Shared真正RED→GREEN证明；PartyXP secondary链只读结构证据，未伪称实测。

Network typed batch用actualregistry ReferenceEquals原context与captured Session，整组encoder/endpoint/actualframecap预检及frameowncopy后第一send；保留wire Debug，旧Explicit语义不变。晚非法pet/skills/extras/avatar/未知/endpoint/oversize均zero frames；sameID replacement不转投新Session，I/O断线可部分投影非原子网络。详细API、全部新未跟踪source复读和真实SQLite纯读／一At／冻结／currentzero／occupied与empty地址测试见[TASK-009](tasks/TASK-009.md)。

Fresh build0警告错误；完整Application2567、完整Protocol1226、Networking sender/registry/close/counter21全部通过0skip。首次完整Application2566/1发现TASK004 RenamePetEffect单宠物UID分类漏项，根授权最小具名分类＋保留generic/pile负例后全项目通过；失败TRX保留，不当作baseline。未接统一operation ledger/rawhash/footprint或关闭旧guard；010及011仍需完成，不能以本阶段宣称父AC-007／重构完成。无部署和实机验收。


## TASK-010 全范围请求结算与当前重放

前文各阶段“尚无guard／缓存仅一条／待重放”的历史缺口由本单位统一接入；旧阶段验收记录仍保留。真实DI exact 18request／20wire／20main，20main均Transactional＋精确session保护；3XP／ReadReplay保持受保护且不冒充request main。普通使用、改名／任务券／fatigue／skill／Finish、六设备入口、分解及全部opening进入同physical CWT ledger，full raw SHA256/length＋allframe ordinal标识；fresh同参数正常新操作，窗口内Committed重复只009当前绝对刷新，confirmed rollback同fingerprint可重试，Rejected／Blocked／冲突／过旧拒绝。

typed effect/change exhaustive footprint在真实60 Commit前复制／Validate，未知pair或needs真SQL rollback；58同步Committed早于NOTI，发送失败不再付款。closed inherited frame不能变trusted，readonly flow token不污染main；第二main在UoW前拒绝。六ReplayGuard类／DI／handler旧分支、Lottery/Vending Completed缓存与业务sequence参数已删除。保留首次ACK顺序、zero crystal event及所有旧经济／保护／销毁／wrap／lateSQL反例，不发送历史ACK或earnedXP。

首次真实 Use sender-fault重复付款RED source3→2→1及全部开发失败保留。最终实际current batch GREEN原context／000D数量2／无ACK／SQL2；并发Lottery draw和支付1；gate前capture后selected替代拒付；sameID新context＋distinct generation不清oldfence；未知footprint／lateSQLAbort；laterreadonly真bus保持footprint；readonly并发token为context-method证据单列。NoSelected具名refusal-only兼容原三实际TCP拒绝；await后选角主命令拒付由ledger／bus／SQL证明。postdurableProbe实际SQLdurable＋typedcause＋fence，transportclose沿008，不虚称本例真实socket。网络raw metadata实际TCP测试用capturing dispatcher，结算另有实际bus／SQL。

最终整方案fresh build0警告错误，完整Application2590／Protocol1226／Networking受影响30全部通过0skip，diffcheck clean；专项23／110／38重叠不累加。细节与真实命令见[TASK-010](tasks/TASK-010.md)、[检查记录](verification/TASK-010-checks.md)。仍留011四组同库整合／动态注册与最终扫描／Host临时DB／全方案回归／人工验收；seq-reset和无nonce古老raw碰撞Unknown，I/O部分投影非原子，不标ClientVerified或父AC全部完成。无部署、无新道具功能、无运行库改动。


## TASK-011 最终活跃写入扫描（当前分类）

扫描覆盖Server/src所有tracked与新增untracked源；先发现实际文件，再读取命中调用与上下文。不是只数类名，也不将定义当活跃writer。完整命令／结果记录于verification/TASK-011-caller-scan.txt及TASK-011-checks.md。纳入范围的六设备、Recipe／所有opening、ordinary use／special、Rename与QuestCompletion领域服务扫描无直接Consume/Put/Clear/Set/Destroy/TrySpend/Add/旧Settle调用；业务变化经过rules→Apply，底层具名handlers才执行基础写。

| 命中链／源文件 | 当前分类及理由 |
|---|---|
| ItemEffectHandlers／InventoryEffectHandlers／OpeningEffectHandlers／SpecialConsumableEffectHandlers／QuestCompletionEffectHandlers／ConsumableStateDomainService | 执行器底层：具名effect的基础consume／spend／add／instance setter／CAS／XP／career／skill。事务与归属由shared Apply保护；未包装opaque完整业务 |
| EquipmentInstances／ItemContainers／AccountMaterialBag／PlayingCharacter基础AddGold/TrySpend | 保留基础verb，调用者分别由具名handler或范围外系统解释；instance当前读取、字段隔离、destroy保护保持 |
| CharacterExperienceDomainService.ApplyAwardAsync→ApplyMonsterKill／GrantKill／GrantClear | 范围内共用XP，实际生成CharacterExperienceEffect；旧AwardAsync／估价delegate无生产入口。TrySetProgress只有Tutorial caller豁免 |
| CharacterSkillLearningDomainService | Refund!=0任一条时首写前整批分流effects；剩余SetLevel/Insert只all Refund0普通学习豁免，循环Remove退款分支在现准入不可达，不能误列活跃退款旁路 |
| ChangeSkillSlot→SwapSlotsAsync、SwitchSkillBuild／build unlock/activate、CharacterSkillStateDomainService reconciliation/reset定义 | 技能栏/页操作与选角维护豁免；Reset/ResetAll/Reconcile独立公开定义没有范围内refund/career业务caller，新的任务career用完整skill effects |
| CharacterRosterService、SelectCharacterPersistenceServices、SelectCharacterPreparationService、ExpertSkillReconciliation、LegacyWornNormalizer | 创建／selectionEntry维护豁免；009 replay及current read无维护writer |
| QuestTriggerDomainService／QuestLog的接取、丢弃及trigger推进 | 原任务条件／战斗或accept/drop豁免；普通Finish／coupon已effects，旧完整Finish/CompleteWithoutRewards writer删除 |
| DungeonEffectRecoveryService.SetProgress、TutorialLevelUp→TrySetProgress | recovery journal恢复／tutorial设级豁免，共用奖励XP未回到这里 |
| MaterialConsumptionDomainService→ConsumeSkillMaterials／ConsumeEffectMaterials、战斗耐久 | 地下城战斗技能与装备耐久消耗豁免，不扩大本轮普通消耗品范围 |
| CardReward.SelectCardCommand、GroundPickupSlice | 地面拾取／翻牌费用和奖励属于既有地下城流程豁免；共用经验入口仍effects；本轮没有重写整套地下城资产系统 |
| CeraShopDomainService／NpcShopDomainService／ItemSaleDomainService／EquipmentRepairDomainService／ItemDiscardDomainService | 商城／商店买卖／修理／丢弃豁免，保留当前基础消费/金币操作；不将其Source定义算新effect旁路 |
| MailboxDomainService／MailStackTransfers、move/sort／仓库／交易 | 邮件与物品搬运／交易系统豁免；物理/账户归属及原事务机制保留 |
| CreatureRuntimeDomainService／CreatureRuntimeDelivery／MoveItemSpace advance、PetInstance／EggInstance／hatch与成长 | pet clock/待发房间经验/孵化/成长保留底层实例职责；普通Feed业务删除，饲料与改名都具名effects |
| RefundAsync fatigue／RecordUseAsync usage | 原基础接口／定义保留，Server/src无生产调用；新的fatigue potion写完整决定值effects，不能仅因定义存在宣称旁路 |
| 固定等级强化／增幅券、升级宝珠、尚未支持PVF效果 | 契约范围外／未新增实现；人工清单不要求测试不存在的能力 |

四组新同库为实际模块/domain＋scope，不冒称network/CommandBus组合；008—010已分别证明实际bus／SQL／TCP/transport安全边界。最终所有人工项为HumanPending；此表当前实现完备不等于父独立审查或ClientVerified。


## TASK-011 正式整合结果

四组actualSQLite source＋target success／latefailure／retry八例与动态全Abstractions DI集合一例9/0/0，旧fake单位保留但不冒充同库。唯一temp Host composition无运行库访问。完整8项目4912 passed／0 failed／2 skip（4914 total）；各项目为App2599／Protocol1226／Networking86／Persistence392／Infra473+2skip／HttpApi50／Host26／Pvf60。两missing_archive负例因真实PVF可达skip，原因/TRX完整保留。fresh solution build0warning0error，effective runner settings/actual collectionsoff/TRX intervals peak1、diffcheck clean；首专项与编译诊断失败历史保留。详见[TASK-011](tasks/TASK-011.md)、[checks](verification/TASK-011-checks.md)。

范围内活跃caller已逐链分类，未发现未解释旧结算旁路；实现说明与24项HumanPending清单齐备。没有改变概率费用、新增道具或部署。CurrentImplementation覆盖不等同ClientVerified；最终父独立审查与一次完整本地commit由根完成，保留sequence reset／无nonce碰撞与I/O部分投影边界。
