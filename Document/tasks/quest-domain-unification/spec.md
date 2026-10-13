---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Quest 统一查询与只读完成计划

## Why and Outcome

将Quest条件解释集中在任务领域，调用方不再用各自的材料快照解释任务是否完成。任务领域对外资产只读，结算编排消费计划并执行一次原子变动。

## Requirements / Acceptance

| Requirement | Acceptance |
|---|---|
| RQ-001 三个只读查询：单任务生命周期、有效已接状态列表、可接列表 | AC-001 未接/已接/已提交完成；已接优先于历史完成记录；CanComplete单独表达；查询无写入，未知任务明确不可接 |
| RQ-002 一个状态计算器处理动态条件 | AC-002 登录/持有刷新/提交前后/任务材料缺口共用；共享为0＋背包旧副本不计；普通材料、金币、seek-n-meet-NPC其他通道保留 |
| RQ-003 可接资格属于Quest | AC-003 World/Cycle/Replay/offer刷新经同一规则，等级职业前置OR互斥/答案宠物筛选保持；Title/Circle/Crack不误入普通列表 |
| RQ-004 完成检查返回完整计划，不执行外部写入 | AC-004 普通/镜像Prepare后数据不变；显式cost/reward及自身状态变化；统一编排应用后才产生资产/完成记录与完成事件 |
| RQ-005 保持特殊路径及原子性 | AC-005 镜像RunId/虚拟槽/重复领奖/每日权益；完成券免条件无普通奖励；转职技能经验；提交失败回滚、重复提交不重奖、满包邮寄保持 |
| RQ-006 协议适配与部署可验证 | AC-006 既有CMD/NOTI顺序及重放足迹保持；完整调用点审查无外部资产写旁路；构建/回归通过，备份部署身份核验，实机待用户验收 |

## Structural Change

重构前：Characters登录自行计数；Quest实时/提交重复计数；World拥有可接规则；普通/镜像Quest服务直接apply；Inventory完成券拥有任务依赖规则。

重构后：Quest状态规则＋事实收集＋只读查询；进度适配器只落任务进度。Quest完成规划只读生成cost/reward/任务变化，外部Settlement编排预演并ApplyEffects、投递事件；Inventory完成券向Quest索取任务变化计划。网络层只映射结果。

## Compatibility

不改wire/PVF/schema/任务玩法。累积目标继续以日志为准，只有持有量等可重算条件读当前事实。regional residual规范化保留。整个PackedTrigger为0才可提交。完成计划仅在本次事务内使用，不建立跨请求缓存。

## Evidence

自动化覆盖查询只读、一致性、Prepare只读、完整结算、重复提交/回滚/特殊路径；独立审查迁移矩阵；部署检查＋用户实机验收。测试不提升为ClientVerified。
