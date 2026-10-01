# 当前实现：完整效果计划与统一结算

本说明依据001—011当前未提交源码，证据等级 CurrentImplementation。概率、费用、物品能力沿既有实现；本轮没有部署或客户端验收。最终自动检查见 verification/TASK-011-checks.md，人工检查见 human-test-list.md。

一次业务的实际链路是：Package Handler → CommandBus → 领域服务收集事实 → 规则构建多个具名 DomainEffect → IApplyEffectsDomainService 整批执行 → 事务提交 → 原协议投影。通知不属于业务effect；库存和实例基础函数继续存在。

## 一个饲料计划

`ItemUseEffectRuleService.Build` 读取已收集的 source/item/character/currentPetUid/at。自耗是显式效果，饲料24计划包括以下真实类型：

```csharp
new ConsumeFromContainerEffect(source, item.ItemId, 1);
new AddPetStomachEffect(currentPetUid.Value, 30);
```

如果规则声明共享冷却或精灵秘药，列表另有 `StartItemCooldownEffect`、`StartSpiritPotionEffect`；合法不自耗物品不生成消费effect。内容不可读、物品错位／过期、角色或宠物不可用会返回拒绝，不拿空列表冒充内容支持。

`ItemUseEffectDomainService.UseAsync` 的实际执行写法：

```csharp
var plan = rules.Build(rule, source, item, facts, pet?.ItemUid, at);
var applied = await effects.ApplyAsync(
    new(session.AccountId, session.CharacterId, at, session),
    plan.Effects, ct);
```

执行器根据 `effect.GetType()` 找唯一具名handler；预检完整费用／容量，全部Validate后才逐条Apply。消费handler调用 `ItemContainerOperations.ConsumeAsync`，最后一件Clear，否则Put剩余数量；饲料handler重新绑定当前宠物后调用 `pet.AddStomachAsync(amount, ct)`。旧 Feed 业务方法已删除，宠物时钟／成长仍由实例负责。

## 强化成功和失败都返回完整列表

`EquipmentReinforcementDomainService.ReinforceAsync` 收集当前装备、报价、材料、保护信息，抽一次成功判定，失败时才产生既有失败规则的抽签输入，再调用 `EquipmentEffectPlanRuleService.ReinforcementAsync`。规则不写装备，也不扣钱。

规则内的实际关键构建：

```csharp
var effects = new List<DomainEffect> {
    Consume(material),
    new SpendGoldEffect(rule.GoldCost)
};
if (decision.ConsumeProtection)
    effects.Add(Consume(protection
        ?? throw new InvalidOperationException("Protected failure lacks its protection target.")));
effects.Add(decision.Outcome == EquipmentReinforcementOutcome.Destroyed
    ? new DestroyEquipmentEffect(Target(target))
    : new SetEquipmentUpgradeEffect(Target(target), decision.NewLevel));
foreach (var reward in decision.Rewards)
    effects.Add(Grant(reward.ItemId, reward.Count));
```

这里的Consume/Target/Grant是该规则类真实私有适配器，分别生成物理或账号消费、UID＋预期模板目标、物理或账号奖励。片段省略已存在的null检查以突出计划顺序，不是新增公开API。概率失败是正常业务结果，仍有完整费用与惩罚计划；前置拒绝不支付。

`SetEquipmentUpgradeEffectHandler` 只调用 `instance.SetReinforcementLevelAsync(effect.Level, ct)`。实例的实际基础变化保留其他Attribute位：

```csharp
return ChangeAsync(
    core => core with { Attribute = (byte)((core.Attribute & 0xE0) | level) }, ct);
```

`ChangeAsync` 每次按UID读取当前槽与Core，只改指定字段后Put；不是把旧完整快照写回。附魔／气息／封印／锻造分别调用 SetEnchantmentAsync／SetDimensionalAttributeAsync／SetMagicSealOptionsAsync／SetForgingLevelAsync。Destroy清当前物理槽并标记该UID不可在本操作复活。

## 开包、任务和经验

设计图／礼包／魔盒／袖珍罐／售货机先完成一次Roll/Prepare；OpeningEffectPlanRuleService.Build明确源、钥匙、材料、金币、prepared或virtual奖励、权益、幸运及配额。OpeningInventoryDomainService.ApplyAsync仅调用共享executor、flush已决定写入、读当前snapshot构造receipt，不保留另一套结算writer。专用 fatigue/coupon/refund/rename计划由 SpecialConsumableEffectPlanRuleService构建；混合退款整批在第一写入前进入effects，普通无退款学习沿既定豁免。

任务交付 `QuestCompletionEffectRuleService` 一次构建任务完成、物品／金币／经验、转职、全页技能及关联进度的完整计划。经验入口 `CharacterExperienceDomainService.ApplyAwardAsync` 的实际核心：

```csharp
var breakdown = await calculation.CalculateAsync(source, before.Level, ct);
var decision = experienceRules.DecideAward(before, breakdown);
var result = await effects.ApplyAsync(
    new(source.Occasion.Account, source.Occasion.Character, at, session),
    [new CharacterExperienceEffect(decision)], ct);
```

获得经验明细与最终等级／累计经验由同一不可变decision保留；满级并不把earned奖励伪装成0。怪物与通关调用同一入口，同时保持各自claim、recipient及提交后通知顺序。旧 AwardAsync、任务完整Finish writer、Career.Advance等旁路已关闭。

## 原子结算与重复请求

真实CommandBus顺序45精确会话／生命周期门 → 57全局结算门 → 58结算结果与不确定封锁 → 60 UoW。数据库各facet共享同绝对SQLite路径、同参与者与SQL事务；内存冷却、秘药及宠物投影只在durable之后发布。确认rollback才能重试，不确定结果保留原原因并封锁exact session和physical context。

18请求CLR／20wire／20main都接 EffectOperationLedger；物理context以对象引用建立账本，decoder前记录完整raw-body SHA256/length及all-frame ordinal。Fresh表示新操作，同参数也可再次使用；128帧窗口内同exact session＋完整raw identity的Committed重复只刷新当前状态，已确认rollback可重试。后续readonly事务不能覆盖主提交，closed继承flow不能退化trusted；无selected只允许具名refusal-only原拒绝。

重放读取当前纯snapshot，按实际effects触及槽与needs构造typed absolute messages。009的Exclusive覆盖读到发送，整组encoder/framecap验证完成后才发送第一帧；不重复原ACK、增量奖励或earnedXP。网络断线可能只收到部分消息，但已Committed不再付款。完整ushort回绕有自动证明；同物理连接返回选角后客户端sequence是否reset仍Unknown，由人工清单集中取证。

## 最终同库证据与边界

新增 RealConsumableIntegrationTests 使用真实模块、LearningFixture唯一绝对临时数据库、PersistentItemContainer以及实际 item/creature/quest/skill stores。饲料和改名在原数据库参与者Prepare完成facet flush之后读到source1与新目标值，再注入晚Prepare失败，断言SQL回滚与retry；coupon在clear flag晚触发器、water在mixed skill3晚INSERT失败时源和全部目标共同回滚。旧fake unit tests保留，未冒充同库证据。这些为domain＋scope组合，网络／CommandBus／precise transport边界证据分别来自008—010。

范围内活跃writer逐项记录 coverage.md。普通无退款学习、技能栏交换／build选择、选角维护、任务接取丢弃、商城邮件商店、战斗耐久与材料、ground/card、tutorial/recovery、宠物时钟孵化成长等按契约豁免；不是宣称全仓库所有写入进入effects。固定等级券、升级宝珠等尚未实现能力仍范围外。最终交付还需根与独立审查接受，实机所有项目保持HumanPending。
