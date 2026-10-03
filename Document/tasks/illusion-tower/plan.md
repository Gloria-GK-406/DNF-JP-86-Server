---
kind: rks-plan
spec:
  path: ./spec.md
mode: task-units
state: ready
---

# Implementation Plan: 迷妄之塔

## Planning Choice

内容/随机APC必须先可用，领域状态才能有真实清场输入；协议和资产依赖其稳定结果。逐步建立这些接缝，避免在后续单元猜前一层输出。只有临近执行的单元写详细计划。

## Approach and Shared Decisions

- 独立目录 `Abstractions/World/IllusionTower`、`Application/World/IllusionTower`；当前塔的 TowerRoom 不复用普通 Cell。内容能力位于 Infrastructure，仅规则服务读取。
- dgn 持有当前房间和已完成事实；下一层准备后事务切换，并使旧层操作失效。成员与活动模式路由最小化，不能复制第二份进度。
- 新协议集中在 Protocol 的塔切片；已有通用协议通过领域结果选择投影，保留原有普通副本路径。
- 物品/经验/任务使用已有领域能力；奖励决定、提交和显示的职责分开。先补证规则，不能为了接通而用未知客户端字段发奖。
- 一个共享工作区，C#产品/测试写入和所有.NET构建/测试顺序执行；独立只读调查、文档及无共享文件或构建输出的Python校验器修复可以并行。不碰 artifacts 中当前用户运行。

## Work and Dependencies

| Work | Result and boundary | Depends on | Covers |
|---|---|---|---|
| T-001 | PVF内容、层/演员规则和确定性房间种子，含真实内容验证 | 当前研究 | RQ-002 |
| T-002 | dgn/TowerRoom、入场归属、逐层状态、事务和结束原因 | T-001内容接缝 | RQ-001,003–006 |
| T-003 | 塔协议编解码、注册和现有共享入口/通知接入 | T-002领域动作/结果、T-004奖励后端receipt | RQ-004,005,008,009 |
| T-004 | 完整奖励后端先行、结束/返回/再入事务闭环，补足任务与资产集成 | 规则补证、T-002；网络返回再消费T-003 | RQ-006,007,009 |
| T-005 | 跨层全流程、普通副本回归、协议文档和human test list | 前述完整接入 | RQ-001–010 |

## Verification

优先验证真实消费者行为：随机APC非空且稳定、重复消息不跳层、不跨模式、资产回滚和一次提交。新增测试不冻结内部字段布局或构造器选择。跨项目架构用既有测试；协议使用字节向量；内容使用当前PVF并记输入。最终完整结果独立评审，再做新鲜验证和一次本地结果提交。

## Next Planning Boundary

TASK-01/02/03已建立真实内容、独立运行和奖励事务。TASK-04完成协议与返回接入后，TASK-05处理任务材料和公共能力；随后单独处理塔与队伍成员/队长变更，再完成全流程回归、知识库与human test list。队伍状态不参与数据库事务，必须单独验证提交顺序及失败恢复。上述拆分只细化T-004/T-005，不改变接受边界。
