# Implementation Evidence

基准df570d87，现工作分支保持task/next-round-20261013。代码冻结manifest27文件，snapshot.0c66b709624d4bef。

- 模板携带有限期只读声明；新实例准备/创建保底统一规则；奖励投递、BusinessItemContainer.Add、商店与效果预检在堆叠前准备，同期限才能合并。
- 邮件已有ItemRecord转移/分拆保留期限；外部明确非零实例期限和永久哨兵不重新计算；新开包/商城报价约束取最早。
- 期限行为RED为8项测试中6失败，初始stub不生成截止；实现后转绿，另补两项开包组合和两项实际商城同日/旧0堆回归。
- 首轮Application全量发现手工InventorySide测试夹具缺新期限服务，补齐测试装配后过。Persistence全量暴露既有67fce4d2新增ResealCount但golden没跟进，按已有Attribute>>5补六行派生值，未改seed字节或业务。
- 独立审查发现并修复：开包多约束漏每日/日期依赖机器时区；商城Plan前未准备期限；历史预检错把world槽id当模板id。后者数据库尚未修改，已从core读真实模板ID并校验身份，完全重建清单。历史旧67排除结论作废，现只有1个虚拟金币排除。
- 并行完整Application有两例墙钟/Task.Run等待超时，完整失败保留application-reviewed.trx；隔离2/2通过（163ms/184ms）后无并行build/PVF套件单独全量重跑application-verified：3875/3875。未改这些测试或预算。
- Infrastructure-reviewed900通过+2既有内容skip；Persistence-final468/468。协议/Host新鲜核验结果见完成证据。

历史操作脚本只支持1123限时物品改为昨日；以当前PVF SHA与真实core模板ID生成清单。事务内验证items仅期限字节/字段变，所有其他表指纹与schema和槽位不变；改前/改后均留完整备份供独立核验。实际结果写postcheck，不在此预先宣称。
