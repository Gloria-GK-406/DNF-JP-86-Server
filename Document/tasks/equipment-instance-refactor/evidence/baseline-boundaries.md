# 6e60c4c6 迁移兼容边界

本记录由当前代码推导，属于CurrentImplementation，不能当客户端原服事实。

| 业务 | 当前修改字段 | 迁移保护 |
|---|---|---|
| 强化 | Attribute低5位/ReinforcementLevel；旧实现错误使用EnchantUpgradeCount，TASK-004修复；失败可能移除装备 | 按失败阈值100000、处罚1/2/3，+30当前准入，rarity上限、+19后缺成本weight取1；自动保护优先超级券，只有相应失败才扣券；销毁产物提前验证空间 |
| 附魔 | EnchantCardId | 升级珠子仍UnsupportedContent，不能把附魔独立等级写进强化字段；耗珠1个并保留其他core字段 |
| 异次元 | AmplifyType、AmplifyValue | 0x80未净化/1..4已净化/0无属性按旧规则处理；净化、赋予、扭转的材料和seed保持；不新增增幅指令 |
| 魔法封印 | RandomOption0..2和确认暂态 | 完整选项组更新，reset State=0/ChangedIndex=255/ChangeState=0/Change=0；维持materialSlot非null拒绝和费用/seed |
| 锻造 | GenuineUpgrade | 失败仍扣材料但不改等级；保留成功weight/roll和旧等级receipt |
| 修理 | Durability | float32计算/截断和grade整数除法不变；无耐久价0/max0是零费用且不写装备；先报价全量再扣费，拒绝不可读的repair-all而不部分收费 |
| 自动修理 | Durability | 既定slot集、broken条件、Premium资格与每日额度保持；全有或全无 |
| 耐久 | Durability | 减1，下界0；已零不写装备但仍回应；既定可穿slot集不扩大 |
| 分解 | 移除装备，材料产物 | 仅适配实例facts/Destroy；工具和产物不是装备状态写入，不为此扩张通用容器设计 |

规则服务不能读世界：材料现数量、角色金币、拥有者/连接与空间规划仍为领域编排。规则服务可读取PVF静态输入，计算报价和养成决定；实例不能自行判断哪种玩法、费用、概率或保护处罚。

强化随机在会话/装备/资源准入以后执行；提交失败不发布success或记成功重放缓存。同连接128条序列缓存语义不变。所有领域结果为原receipt，经原Protocol CompletedHandler投影，wire文件不必因接口调整而改字段。

既有测试是主要公式/业务oracle：EquipmentReinforcementDomain/Handler/RuleTests、EquipmentEnchantDomain/HandlerTests、EquipmentMagicSealDomainTests/MagicSealHandlerTests、EquipmentOutworldDomain/HandlerTests、EquipmentForgingDomain/Handler/RulesTests、EquipmentRepairTests、EquipmentDurabilityTests、EquipmentDisjointTests/RuleServiceTests。新身份契约需真实store/container证据，不以接口编译错误作RED。
