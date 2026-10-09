---
kind: rks-verification
decision: pass
---
# 完成验证：主动深渊入场

验证声明：本次主动深渊准入、事务入场、冻结房间配置及协议投影按 TaskContract rev1 完成；不包含部署、实机验收、柱战斗、随机深渊或奖励结算。

| 条件 | 决定性证据 | 结果 |
| --- | --- | --- |
| SC-1 | 类型判定测试；实际注册 CMD 总线测试；独立审查确认业务上下文无网络依赖，CMD switch按类型路由，非1为普通随机桩 | pass |
| SC-2 | HellDungeonEntryTests：无支持/配置无效/练习组合/全队缺票/成员未完成当前前置任务均拒绝，实际库存和run保持不变；塔11000/11001请求不再绕过CMD降级 | pass |
| SC-3 | 真实库存、任务日志和 SQLite 事务测试：通行证优先、分堆扣票、全队票与疲劳、共享run；实际 CMD无外层事务提交；扣票异常/事务放弃回滚 | pass |
| SC-4 | 入场/切房/重访冻结测试、真实PVF样本、001C/001D/02A7编码向量；普通副本和塔回归；协议人工记录分开标注ClientStatic、UpstreamImplementation、CurrentImplementation与待实机 | pass |
| SC-5 | [最终检查](test-summary.md)：Release solution构建0警告/错误，全量6075通过/0失败/2条件跳过；git diff检查；[独立审查](../reviews/change-review.md)批准且R1已解决 | pass |

代码和测试定位见 [实现说明](../implementation.md)，不在此复写调用链或测试日志。独立审查与最终验证针对同一最终产品变更；审查之后未修改产品或测试代码。

部署后的主动入场、扣票和封印房显示仍属于 ClientVerified 待验收项；本合同只要求准确声明这一证据边界。网络传输失败沿用既有清run策略，本次没有新增已提交资源的退款机制。
