# 2026-10-01 冷却恢复与魔法封印品质修复

## 范围与人工状态

用户授权处理眼前两个bug：精力唤醒药剂重登后冷却显示消失、封印转换武力只出现S。本轮沿用task/solo-play-roadmap、基线bdb26ee7，只做一个验证后的本地结果提交；不push、merge或修改测试资产。用户确认H18、H19、H16、H24、H22通过。H11转换路径已能操作，品质问题仍待复验；红字系列和H21暂缓，H25尚无明确反馈。

## 冷却证据与实现

CurrentImplementation：通用CharacterItemCooldownDomainService只保存会话内内存，退出角色清除；但疲劳药另有SetItemUsageStateEffect，已将LastUsedAt存入character_daily_counters。FatigueAsync本来就按LastUsedAt+CooldownMilliseconds拒绝冷却内使用。此前将这次疲劳药问题笼统说成未持久化并不完整，真正缺口是登录同步。

7518真实PVF：cool time=86400000ms、无total usable count，每日次数无限额。GetSnapshotAtAsync旧实现只返回DailyLimit>=0的条目，所以7518没有进入登录通知。现在快照同时包括有限日次数与仍有效的elapsed冷却；021E投影只选有DailyLimit的条目，00AC单独按截止时间减快照时间、向上取整秒数，按255条分包发送。00AC没有发明清零包；过期项不发。登录原ItemUsageProjectionDelivery及effect重放都复用这套投影。只读恢复已有时间戳，不增加新表或启动时重写业务行，也不将一次使用日期迁移到当前时刻。

ClientStatic / High：A21 NOTI00AC handler RVA D81AE0读取u8数量、每条i32物品ID/i32秒数，D81B95将秒数乘1000调用冷却设置；详见协议记录人工区。实际UI倒计时待第四轮实机验收。普通消耗品没有既有usage记录的跨重登冷却，不在本次扩展范围，仍按原会话策略。

## 封印品质证据与实现

CurrentImplementation旧内容源把Options/RandomOptions_15_physicalattack.etc中Lv75的physical attack=13,98写成RandomOptionState(15,13,98)，规则直接返回候选，没有生成品质值；整数范围之外的Single也被遗漏，PVP可覆盖DUNGEON同级范围。

70Reference / Confirmed：df_game_r的CRandomOptionItemHandle::_get_option_value地址085F2B02；change_option在085F390C、085F392D分别调用取值写入两个字节；give_option在085F3236、085F3265同样分别取值。上下限来自RandomOptionScript的DifferentWeightValue，而不是某一个属性文件的幅度。参考代码不能当作A21实机证明。

CurrentImplementation：候选改为Type+原始stat范围+Weight，品质区间单独建模。读取A21 randomizedoptionoverall1的option type/modified option type和overall2的different weight。Lv75稀有转换质量桶权重1500/7000/1500，对应0—25、10—30、30—55；解封读独立初始权重7000/7000/2500。在选定品质桶内两个字节独立均匀抽样。候选属性的13—98仍作为模板信息保留，不再写进装备品质。Single属性幅度合法保留，取DUNGEON同级范围；超过255的属性幅度不再隐式clamp。品质范围或权重损坏抛异常，不能降成合法默认值。

Evidence boundary：70_get_option_value另有option value ratio加权缩放机制，当前A21上述两个overall文件没有该声明。本轮采用明确的区间内均匀整数采样，只保证品质生成不再固定端点、使用A21已声明的桶权重及范围，不宣称所有70/A21历史概率已完全复刻。复杂option效果仍沿已有支持边界，客户端档位名称及随机显示需人工验收。

## 验证与交付

Release解决方案build已通过，0 warning/0 error。第一轮定向83项通过；新增实际PVF取值试验覆盖1000种seed，解封与转换各1000次，武力及全体品质都覆盖0—13、14—36、37—46、47—55四段；不是统计证明历史概率一致。冷却自动回归覆盖无每日限额、重新构建服务提供器（模拟服务重启）、跨06:00仍守原deadline、deadline到期允许使用、00AC秒单位及分包、不送替代会话。

完整八项目回归：5286通过、0失败、2跳过；跳过项为配置真实PVF后不适用的missing-archive检查，实际内容检查执行。fresh reviewer cooldown_seal_review按reviewing-work独立检查完整diff、登录顺序、会话门、品质读法与最终TRX，verdict=approved，无范围内阻断缺陷。git diff --check通过。最终部署身份由下述artifact记录；客户端结果仍待[第四轮表](../tasks/item-effects-unification/human-test-list-20261001-round4.md)。产物目录为artifacts/cooldown-seal-fixes-20261001，实际部署必须有deployment.json state=deployed。继续使用原DB及配置，PVF不变；切换前须释放正在运行的游戏。


1123切换前只读数据：7518的LastUsedAt为2026-10-01 22:21:44.918+08，原24小时截止为2026-10-02 22:21:44.918+08。恢复时不清除这条记录，不将旧使用时间改成部署时间。
