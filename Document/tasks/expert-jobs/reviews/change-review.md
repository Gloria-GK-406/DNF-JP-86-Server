---
kind: rks-review
verdict: approved
---

# JP86 四副职业独立交付评审

复核范围为基线 `cada01a0e` 至 `d8e3b6148` 的四职业实现及当前 TASK-06 中的普通宝珠兼容修正、摊位写入守卫、集成测试和证据文档。按 Code 与 Deliverable 协同评审。交付范围以第 3 版契约、实现规范、计划和仓库约定为准。 [任务契约](../TaskContract.md) · [实现规范](../spec.md) · [计划](../plan.md) · [仓库约定](../../../../AGENTS.md)

| 条件 | 独立复核结果 |
| --- | --- |
| SC-1 / SC-2 | 职业行以角色为主键，配方联合主键继续使用原两表。旧档缺行读作无职业，零耐久保留。四种 getter 只返回相符职业，写操作重读职业和所需会话。任务领奖和授职共用事务，放弃资产、任务、技能和职业写入后按精确会话提交关摊。 [存储](../../../../Server/src/Dfo.Server.Persistence/Characters/SqliteExpertJobStateStore.cs) · [职业入口](../../../../Server/src/Dfo.Server.Application/ExpertJobs/ExpertJobDomainService.cs) · [领奖](../../../../Server/src/Dfo.Server.Application/Settlement/QuestCompletionSettlementService.cs) |
| SC-3 | 玩家分解从同一个摊位访客实例进入双人工作单元。两方金币、原物、产物、经验与磨损成组写入。机器耗尽以事务参与者方式撤摊，执行失败保留原摊位与访客。NPC 和便携入口保留。 [玩家分解](../../../../Server/src/Dfo.Server.Application/ExpertJobs/DisjointerOperations.cs) · [摊位状态](../../../../Server/src/Dfo.Server.Application/Sessions/ExpertStoreDomainService.cs) · [NPC 与便携分解](../../../../Server/src/Dfo.Server.Protocol/Game/DisjointItem/GameDisjointItemCommand.cs) |
| SC-4 | 炼金和控偶经各自类型进入配方规则，达到资格后可学习、批量制作和提取。生产的材料、产物和经验共用结算，职业升级写入自动解锁；提取保留提取器。 [生产与提取](../../../../Server/src/Dfo.Server.Application/ExpertJobs/ExpertJobProductionOperations.cs) · [协议入口](../../../../Server/src/Dfo.Server.Protocol/Game/ExpertJobs/ExpertJobProductionSlice.cs) |
| SC-5 | 制作前验证 PVF 卡片、宝珠及其档位。普通宝珠保留既有写入语义，职业宝珠写入已校验档位。摊位按资格、部位、费用和设备校验，两方资产在同一事务内结算。 [宝珠解析](../../../../Server/src/Dfo.Server.Application/Inventory/EquipmentEnchantRuleService.cs) · [摊位附魔](../../../../Server/src/Dfo.Server.Application/ExpertJobs/EnchanterStoreOperations.cs) |
| SC-6 | 复核正常命令注册、开发验证与客户端证据分界。协议清单与人工记录保留参考证据，A21 清单可逐条采集帧及资产差量。 [协议清单](../../../../protocol/catalog.md) · [上游与 70 对照](../UpstreamEvidence.md) · [A21 验收顺序](../A21验收顺序.md) |

评审时 Application 定向验证已记录 237 例通过、2 例失败。基线复现的两例是组队写入分类和旧的 `0x0447` 请求登记。复核新增用例的输入和断言，覆盖四个真实 PVF 任务、授职晚故障回滚、最高档图纸、炼金和控偶批量制作跨级、双人分解失败回滚后重试和耗尽、关摊后旧访问拒绝、付费附魔成功和失败、普通及职业宝珠档位。复核了城镇夹具针对专家摊位通知的精确受众和顺序断言。最终全解及测试汇总以 TASK-06 的执行记录为准。 [任务执行记录](../tasks/TASK-06.md) · [任务集成测试](../../../../Server/tests/Dfo.Server.Application.Tests/Quests/ExpertJobProtocolFlowTests.cs) · [生产集成测试](../../../../Server/tests/Dfo.Server.Application.Tests/Inventory/ExpertJobCraftProtocolTests.cs) · [摊位集成测试](../../../../Server/tests/Dfo.Server.Application.Tests/Inventory/ExpertStoreProtocolFlowTests.cs)

已核对的实现和文档没有待修阻断项。A21 实机未执行，客户端包序、关摊尾部和提取器制作后的界面刷新仍待玩家端核对。 [A21 验收顺序](../A21验收顺序.md)

最终回归修正补审通过。材料容器表改为不可变集合，原六种容器和遍历顺序保持一致。仅有 chain 20 标记的旧任务仍适用原有资格，已配置的副职业任务在展示、接取和领奖校验 PVF 归属，三个真实后续任务有接取及放弃清理断言。
