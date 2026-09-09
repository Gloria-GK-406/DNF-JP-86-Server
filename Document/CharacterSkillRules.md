# 角色技能规则与点数

日期：2026-09-09。基准提交：`8b19eaac`。

`ICharacterSkillRuleService` 按职业提供共享的只读技能规则。`Get(job, skillId)` 返回单个规则或明确的缺失；`GetAll(job)` 返回规则目录；`GetTotalPoints(job, level)` 直接回答静态 SP/TP 总预算。目录里的规则可以回答说明、前置、职业等级上限、学习资格、累计费用和免费等级。职业组合必须由角色档案声明，打包格式合法本身不代表档案支持。

例如，剑魂 25 级的十字斩规则允许目标等级 3，普通上限为 6；同职业不能学习血之狂暴。元素师 86 级的元素点燃上限为 20。强化元素点燃要求元素点燃至少 5 级，费用为 TP。这些结论来自 A21 `Script.pvf` 的职业技能目录和技能声明，不是客户端实机验证。

`[maximum level]` 单值适用于各方向，多值按转职索引取值；零值保持不可学。觉醒上限按 `growType * 2 + awakening - 1` 取正值，缺失或非正值回落到转职上限。`[skill fitness growtype]` 仅保留为技能分组元数据，不作为职业白名单或费用折扣。

`GetSP(level)` 与 `GetTP(level)` 返回从零到目标等级的累计费用，费用表用完后重复末项。实际投入是累计已学费用减去累计免费费用，最低为零；免费等级取初始、转职、觉醒赠送和自动成长中的最高值。`[fixed level skill]` 即使没有直接整数也表示自动成长声明，其子块提供成长间隔和增量。缺失 base 保持 0，依据参照服务端 `ServerS4A21` 提交 `2db3a135` 的 `SkillStaticData.GetFixedLevel`，属于代码推导。TP 总预算沿用同一参照的 `SkillPointLedger`：50 级起每级一点。

`GetLedger(career, level, pages, basis)` 接收当前已学页和奖励事实，返回两页的完整 `long` 余额。`ToProtocolMirror()` 仅在输出协议时将数值限制到 ushort 范围。找不到费用定义时保留已学事实及既有零投入表现，但结果的 `MissingSkillIds` 和 `IsComplete` 会明确表明账本不完整；现有余额投影记录警告。未知职业组合明确拒绝查询，不自动迁移历史数据。

PVF 内容源仅缓存有界的职业静态目录，规则服务共享目录中的规则实例；角色等级、已学集合和余额不进入缓存。现有入场对账需要的 `ISkillPointDefinitionSource` 保留为 Application 规则服务的兼容投影，生产投影携带同一个规则实例。选角、角色余额和新账本入口共用 `SkillPointLedgerRules`，不保存第二份算术。
