# 物品与奖励的effect结算流程

证据边界：本页为CurrentImplementation流程与限定ClientVerified反馈，不由领域代码反推协议布局。单条消息布局由所链接协议页负责。版本收口见[0.2.0](../../Document/releases/0.2.0.md)。

1. 协议切片解码请求，验证物理连接与当前选中角色，发具名CMD。领域服务读取持有物品／装备／角色facts；规则服务读取类型化PVF并返回完整effect列表，自耗、材料、金币、装备改变与奖励都显式列出。
2. ApplyEffects在同一事务中预检和执行全部effects，实例承担基础加减／设置。失败整批回滚；提交成功才有后续通知。earned经验和最终成长分别保存，不将不能升级改写成没有获得奖励。
3. 提交后投递当前权威库存／金币／状态。受众绑定原精确连接及角色；已提交请求重放只读当前投影，不再次抽取、消费或奖励。通知失败不将已提交事务当成未提交重做。
4. 具体通知顺序由客户端读取依赖裁决，不采用统一ACK-first。魔法封印[01B6](../game/client-to-server/0x01B6.md)先更新装备和金币，再发成功[ACK](../game/server-to-client/CMD-0x01B6.md)触发窗口刷新；portable分解先清空耗尽源，再成功结果，使client当次关窗；[设计图0019](../game/client-to-server/0x0019.md)与[普通强化0050](../game/client-to-server/0x0050.md)沿各自结果与库存投影。
5. 账户晶体余额降为0时仍投影余额清零；数量为0的奖励event在通知消费者端不生成“获得×0”。操作调用方不自行散布此判断。
6. 登录读取已有LastUsedAt／截止时间，投影[00AC](../game/server-to-client/NOTI-0x00AC.md)剩余秒数；不重启24小时冷却。支持该持久化路径的疲劳药C01/C02由用户确认，不泛化为每种普通消耗品都新增持久化冷却。

普通强化／保护／损毁返料、普通武器锻造、宝珠、魔法封印、设计图、portable分解、疲劳药、当前通关出售与城镇瞬移的验收沿[当前列表](../../Document/tasks/item-effects-unification/human-test-list.md)。S02窗口／背包／重登一致在29d2b100部署后确认。红字未验组合、宠物特殊能力、0197再生／01C8重置、锻造隔离券变体和专业入口仍由相应Issue跟踪，不在此升级为ClientVerified。
