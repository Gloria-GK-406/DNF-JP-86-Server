# 当前角色事实与协议投影

日期：2026-09-09；基准提交：`8b19eaac`。

角色实例按持久 ID 提供当下答案，规则服务提供共享静态知识。这一分工让一个角色对象跨越
升级、提交、回滚与重载时保持有效，无需维护一份会话外的角色副本。基础规则的档案解释见
[CharacterBaseStatRules.md](CharacterBaseStatRules.md)，技能规则和账本算法见
[CharacterSkillRules.md](CharacterSkillRules.md)。

## 当前事实如何进入规则

`PlayingCharacter.BaseStatsAsync` 通过已有 `FindStandingAsync` 一次读取职业、成长型、等级，
再查询已准备的不可变属性表。身份与进度继续从各自的现有角色行读取。
`CharacterSkillStateDomainService` 读取普通技能页和奖励点数，保留页号、slot、extra_values；
`LearnedSkillsAsync` 只公开技能 ID 与等级事实。`CharacterSkillPointDomainService` 将当前事实
交给共享技能规则，完整 long 账本与旧协议 ushort 镜像分开。未知技能既不从事实中消失，也
不能被误认作已完整计价：账本携带缺失 ID，并产生警告。

这些领域服务均使用现有持久化接缝。处于命令事务中时，它们通过相同的 `SqliteDatabase`
参与者读取尚未提交的修改；回滚后重新查询恢复原事实。服务不另开角色缓存，也不按等级和
已学集合缓存技能规则。事务外的一次方法调用回答查询时的事实，并不声明多个调用之间的
全局原子快照。

## 选角和发帧的边界

选角的 storage owner 检查之后，领域服务仅覆盖基础属性的 19 个字段。原始入场读取仍检查
完整性，历史 stat 列仍被加载，但不负责当前职业/等级的基础数值。Progress1/2、当前血蓝
百分比、宠物等级等未建模字段保留原值；没有把装备属性或 Buff 假定为零后重新合成总属性。

副本选择发帧前，`CharacterStateProjectionDomainService` 局部刷新当前身份、进度、基础属性、
技能页与余额。库存继续使用物品移动已维护的投影，World、设置和其他 entry 领域仍保持原有
边界。该服务不写回完整运行态，不声称解决整个会话聚合的并发覆盖问题。现有帧形状与顺序
不变；技能页 slot/extra_values 保留，活动页索引 `SkillTreeIndex` 仍在现有配置边界内。

已完成的授予动作返回等级和经验，消费者可直接使用该次结果；动作没有返回授予结果时，
回落读取角色当前进度。卡牌首次结算固定的价格继续沿用，翻牌命令不再接受会话提供的等级。
教程升级也在命令事务内检查当前进度，避免旧 1 级副本导致角色降级。
