# TASK-004 — 专用道具及已有退款／改名消费

## Task Unit Contract

- Revision: 1
- Outcome and references: 疲劳药、任务清除券、已有遗忘之水退款整批技能变化、宠物改名卡通过完整effects结算；删除TASK-003临时消费接口。Plan T-004的专用品部分，RQ-002—004/006/008，AC-002/005/007。普通FinishQuest及共用经验属于下一单位。
- Dependency: TASK-001共享执行器与基础消费／宠物setter，TASK-002整数组preview，TASK-003 Opening.Apply及prepared／副作用接缝；三个实际结果已由控制器检查接受。
- Owned boundary: 上述四个已有消费入口、只读事实与规则计划、具名状态effects／基础handlers、共享guard必要兼容改动、DI、直接回归、coverage与本Task结果。不得实施普通任务奖励、怪物通关XP、其他技能学习流程重构或新增功能；不提交／部署／运行数据库修改。
- Required checks: 事务失败／正常拒绝不留改动，跨日fatigue／usage真实raw与有效投影，任务券槽位／重复／关联trigger，混合退款一次扣水及每条技能身份，改名不可变名称／永久期限／旧锁语义；删除临时接口的调用扫描、新鲜构建及直接相关回归。

## Detailed Plan

1. 疲劳药一次收集源、静态声明、位置等级、同一At下的raw fatigue与有效balance、raw usage与使用限制事实。规则决定明确源消费、现有内存冷却、fatigue终值及usage次数／LastUsedAt；恢复Used保留ConsumedToday，保留必须容纳整个恢复量的旧限制，不新增部分恢复。基础handler检查预期原行后写已决定值，不调用RefundAsync／RecordUseAsync重跑业务，不读新时钟。
2. 任务券沿既有catch-up选择规则取得目标。读取真实carried／tracked槽索引和cleared mark，纯规则决定每条删除／clear mark，重复ID归并；原nonzero mark保留。只读推导移除后的carried及newlyCleared，重算相关quest-clear channel0，保留其它bits、声明中重复条件的计数语义；effect写预期原行到终值。不得以ReportAsync／CompleteWithoutRewardsAsync包装旧完整业务；不产生奖励。
3. 遗忘之水仍只在整批存在refund时消费一瓶，按QuickSlot→Consumable和旧槽序寻找物品3。先验证全部申请的页、level、最终前置条件、SP／TP，再构建全批Remove／Set／Insert技能effects（包括同行纯学习），带page／slot／skill／原level身份。决定目标槽位与新level在规划阶段完成，Apply不重新找技能或算前置；保留Extras。无退款普通学习路径保留边界，不为整个技能系统扩范围。
4. 改名卡在CreatureConsumable已有binder上读取源／当前佩戴pet事实。规则保留kind7卡、kind5宠物、UID／template及已有期限判定，名称允许空／最多12字节／禁控制字节。RenamePetEffect用ImmutableArray复制，handler调用基础RenameAsync且失败必须抛出联合回滚；不调用完整CreatureNaming业务或推进pet成长。ConsumeFromContainerEffect可增byte ExpectedEquipmentLockId默认0；普通材料规则仍选择0，改名卡按原允许的实际lock记录预期值，preview和handler只比较事实一致性。
5. 必要期限表示兼容：int.MaxValue永久哨兵不能在共享consume／preview／普通源规则或pet基本IsLive被误拒；集中复用现有表示层方法（若存在），无新业务许可bool。只修本迁移依赖的期限判定，pet时钟／成长保持原职责；增加未来At永久卡＋pet反例。
6. 开放基础store接缝只做已决定状态CAS／写入。Validate阶段全体先运行，对依赖前序变化的effects不要按初始状态否定合法序列；期望比较与最终写入可在Apply中顺序做，异常回滚。只读facts API不得Ensure／Refund／Report暗写；handler也不得隐藏完整旧业务。协议沿实际结果投影，通知不成为effect。
7. 删除IOpeningInventoryDomainService.SettleDeferredConsumptionAsync及实现／替身接口，确认所有三个消费者已完整迁移。扫描四个入口直接Consume／Refund／RecordUse／CompleteWithoutRewards／技能写／Rename旁路，逐项解释基础handler或范围外。更新coverage，真实同库整合不足明确保留到最终单位。
8. 生产写入／dotnet检查串行，PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf。复用已有高价值fatigue、usage、coupon、progress、skill refund、naming回归；实际缺口新增RED→GREEN。至少覆盖末尾存储故障，拒绝原行不变，tracked洞与重复，packed其他channel保留，混合技能退款一瓶／替换身份，名称源数组改变不影响计划，锁变更拒绝。保存命令、实际通过／失败／跳过数与测试替身边界。

## Planning Evidence

只读调查详见../verification/preparatory-findings.md。控制器已核对TASK-003实际结果和IOpening.Apply／explicit source临时接口签名；shared Consume／preview及Opening.Source／RecipeCosts已保留永久哨兵，本Task只补尚未一致的普通源及pet基本IsLive依赖。四个入口为已有行为，按完整消费影响细化父契约，不增加技能或宠物功能。当前计划可执行，父Plan方向与剩余普通任务／XP工作保持。

## Actual Result — 2026-10-01

```yaml
kind: rks-verification
decision: pass
```

此pass仅指本Task四个专用品入口及Required checks的当前自动验证，等待控制器检查接受；不宣称父Spec完成、AC-007重放满足或ClientVerified。生产writer完成本记录后停止，不修改父registry，不提交或部署。

### 实现与保留行为

- 新增SpecialConsumableEffects具名契约、SpecialConsumableEffectPlanRuleService、ConsumableStateDomainService与七类handler，注册DI。Fatigue／coupon先读raw事实，规则决定完整effects；handler不包装Refund、RecordUse、CompleteWithoutRewards或Report完整业务。fatigue使用同一At计算有效Used／ConsumedToday和usage count，原raw作为CAS预期，完整恢复限制不变，内存冷却与持久usage冷却分别保留。
- Coupon按真实carried／tracked槽删除选中行，归并完成ID，保留nonzero mark；在规划阶段由移除后的carried和cleared计算关联任务channel0未满足数，保留其他9bit channels及重复声明条件计数。原公开IQuestLog.CompleteWithoutRewardsAsync及实现已删除；旧三项无奖励完成测试的主要语义移入coupon实际effects测试，避免留下完整旧业务旁路。
- 含refund的LearnSkill整批先决定最终level／slot／前置／SP和TP，单瓶水与所有同行Remove／Set／Insert明确列出；普通no-refund沿原实现。更新技能保留原ExtraValues，新学习默认空Extras；ledger是计划结果，未新增ledger持久写入。
- NameCard仅Consume1与RenamePet两个effects；名称在入口首次await前复制为ImmutableArray，规则／handler保持空名、最多12字节、禁控制字节。Consume新增ExpectedEquipmentLockId默认0：普通材料保持0，改名卡使用旧允许actual lock快照；handler与preview只比对事实，不引入AllowLocked玩法bool。2050时int.MaxValue源／pet依然永久，补普通源规则与基础IsLive的表示兼容，不改内部成长。
- BusinessInventorySlotDomainService原本支持CreatureConsumable，通过LegacyInventoryStorageCoordinates映射Pet storage189..239，local0..50；生产binder无需改动。直接反例覆盖-1／51拒绝。
- 三个Opening消费者的SettleDeferredConsumption接口及实现删除。Fatigue／coupon／water旧业务和旧fixture都显式consume1，故ConsumptionAsync保留固定1并仅从通用声明读取可读性／cooldown，不因IsConsumedOnUse声明暗增special不自耗行为；NameCard原专用固定1同样保留。

### 新鲜验证与测试边界

环境PVF_ARCHIVE_PATH为E:/Project/DNF/DNFClient-A21-protocol/Script.pvf；所有dotnet串行。最终构建和三组测试均在最后生产改动之后运行。

| 检查 | 实际结果 | 证据 |
|---|---|---|
| dotnet build Server/86JP.Server.slnx --verbosity quiet | exit0；0警告，0错误 | [build.log](../verification/TASK-004/build.log) |
| Application直接专项＋共享effects | exit0；222通过，0失败，0跳过 | [TRX](../verification/TASK-004/TASK-004-application.trx) |
| Protocol相关直接专项 | exit0；70通过，0失败，0跳过 | [TRX](../verification/TASK-004/TASK-004-protocol.trx) |
| Infrastructure真实PVF声明专项 | exit0；16通过，0失败，0跳过 | [TRX](../verification/TASK-004/TASK-004-content.trx) |
| git diff --check | exit0，无输出 | 最终工作区检查；包括既有未提交T1—T3差异 |

共同测试参数为--no-build --verbosity quiet --logger trx（LogFileName见上述TRX）--results-directory Document/tasks/item-effects-unification/verification/TASK-004。精确filters：

```text
Application: FullyQualifiedName~SpecialConsumableEffectTests|FullyQualifiedName~Fatigue|FullyQualifiedName~ItemUsageTests|FullyQualifiedName~QuestClearCoupon|FullyQualifiedName~QuestProgressDomainServiceTests|FullyQualifiedName~QuestLogTests|FullyQualifiedName~SkillBuildResetTests|FullyQualifiedName~CharacterSkillLearningTests|FullyQualifiedName~SkillLearningCommandTests|FullyQualifiedName~CreatureNamingDomainServiceTests|FullyQualifiedName~ApplyEffectsTests|FullyQualifiedName~ItemUse|FullyQualifiedName~EquipmentEffectExecutionTests|FullyQualifiedName~OpeningEffectExecutionTests
Protocol: FullyQualifiedName~Fatigue|FullyQualifiedName~QuestClearCoupon|FullyQualifiedName~LearnSkill|FullyQualifiedName~ItemUsageProjection|FullyQualifiedName~Creatures
Infrastructure: FullyQualifiedName~FatiguePotionContentTests|FullyQualifiedName~PvfStackableActionDefinitionSourceTests|FullyQualifiedName~PvfItemUseRuleDefinitionSourceTests
```

SpecialConsumableEffectTests.Permanent_item_can_be_used_after_the_timestamp_sentinel先实际RED（1失败／0通过），修普通源表示后GREEN。其他新回归保护重构结果，不追称为行为RED。中途新coupon测试曾错误假设8bit channel，修oracle为现有9bit；新pet测试曾因record私有stomach缓存参与相等而失败，改为逐项公共成长状态比较，未因此修改生产成长。

新增有意义反例：实际lock不符拒绝并不扣；真实SQLite potion源数量／fatigue／usage／内存冷却在usage末尾失败全部回滚；跨日raw读取不写、usage次数复位但LastUsedAt冷却仍生效；整数组Validate先全跑后顺序技能2→3→4可用，第二条错skill身份整批恢复；carried连续3→2→1保留原SQL预期到flush；coupon重复tracked洞／未携带任务／旧mark2／packed其他channels／晚cleared写失败；退款含Remove＋Set＋Insert只扣一瓶，晚INSERT失败恢复全部及其他页；永久锁卡＋永久pet／空名、非法控制字节、caller数组变更不污染immutable计划。

真实联合存储边界：新potion用生产ShopFixture inventory＋SQLite fatigue／usage＋实际内存cooldown；coupon与water用真实SQLite quest／skill state和统一handlers，但支付仍为同库bonus_sp／bonus_tp标量替身。NameCard使用真实统一执行器、基础consume／pet.Rename及实例实现，卡与petstore为替身；不宣称已证明这三类的生产同库联合回滚。保留给最终整合单位。未执行全Application、Host、实机；前序全App计时／SQLite fixture-before-business故障不在此扩修，也未被当成本Task通过证据。

### 写入扫描与后继接缝

扫描Server/src，SettleDeferredConsumptionAsync和CompleteWithoutRewardsAsync零匹配；RefundAsync（疲劳领域）与RecordUseAsync仅接口／定义，生产caller零。它们是较广领域API，保留以免无关边界扩张。个别测试fake仍有同名CompleteWithoutRewards未用成员，不在生产接口且无调用。FatiguePotionDomainService／QuestClearCouponDomainService／CreatureNamingDomainService无直接Consume、Refund、RecordUse、CompleteWithoutRewards、Report或基础Rename；CharacterSkillLearningDomainService剩余table写入只在早期refund分流之后的普通no-refund路径。新基础state服务只做CAS／insert／set／remove，Rename handler只校验当前owner／UID／template后调用基础实例setter。

可复用IConsumableStateDomainService签名：FatigueAsync(CharacterId,ct)→FatigueStateFacts、UsageAsync(CharacterId,itemId,ct)→ConsumableUsageRow?、QuestsAsync(AccountId,CharacterId,ct)→QuestStateFacts；SetFatigueAsync(CharacterId,SetFatigueStateEffect,ct)、SetUsageAsync(CharacterId,SetItemUsageStateEffect,ct)、SetCarriedAsync(AccountId,CharacterId,SetCarriedQuestEffect,ct)、RemoveTrackedAsync(AccountId,CharacterId,RemoveTrackedQuestEffect,ct)、SetClearedAsync(AccountId,CharacterId,SetClearedQuestEffect,ct)、SetSkillAsync(CharacterId,SetLearnedSkillEffect,ct)。这些读取不Ensure或暗写；Quests需要当前事务。

SetLearnedSkillEffect(Page,Slot,SkillId,ExpectedLevel,Level)：Page只要求非负，因此Locked额外Page256可用；Slot限定0..254；Level null为remove，ExpectedLevel null为insert。Validate只做shape，Apply逐条读取当前状态，因此expected-after、先删除再插入可沿序列执行。SetLevel保存原ExtraValues，InsertAsync没有Extras参数，effect也没有显式Extras；后继普通任务需要非空新增Extras时自行扩接缝。全页删除可由事实展开每条已知技能effect，本Task未增加bulk删除政策。SetCarriedQuestEffect同槽连续改变时保留最初ExpectedTriggerValue直到QuestStore flush，避免第二次staged变更拿中间值作SQL原行预期。普通FinishQuest、共用XP及任务条件系统保留后继范围。

### Controller acceptance

控制器已读取实际四入口、基础state CAS及shared consume／pet期限表示，核对最终TRX counters为222／70／16全部0失败0跳过。旧临时接口／无奖励完整业务API扫描确认关闭，普通no-refund学习与无生产caller的广泛fatigue／usage API边界有具名说明；不把残留定义误记为活跃scope入口。接受本Task既定输出作为普通任务依赖。coupon／water支付、NameCard生产同库整合不足按上节保留，最终单位必须补，父AC-007与实机仍未完成。writer已停止。

随后独立special_stage_audit核对相对HEAD行为和存档TRX，未发现可复现阶段遗漏／回归：raw事实读取、sameAt、真实tracking槽、packed9bit、固定单瓶退款／Extras、immutableName、锁事实及永久表示均符合结果说明。该代理只读，未重跑测试，也未评价TASK005进行中代码；确认上述三类生产同库不足与全入口重放仍是后继必做项。
