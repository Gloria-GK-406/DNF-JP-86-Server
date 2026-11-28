---
kind: rks-task-contract-review
contract_path: E:/Project/DNF/DNF-JP-86-Server/.worktrees/gameplay-benefits-registry/Document/tasks/gameplay-benefits-registry/TaskContract.md
contract_revision: 1
reviewer: /root/benefits_contract
selection_basis: 由控制器派出的独立审查上下文，未参与契约建模、意图对齐或工作流选择
verdict: approved
---

# 契约审查

适用依据包括用户关于来源登记、显式空声明、替换、时效和有序读取的要求，中性组件涵盖掉落与强化的澄清，队伍共享和任意队友拾取的决定，以及最后对契约、执行和复扫的明确授权。项目约束来源为 [AGENTS.md](../../../AGENTS.md) 和 [服务端架构](../../Architecture.md)。兼容基线为本工作区起点 `f4a413bcf`，额外神器投掷语义见 [掉落实现](../../../Server/src/Dfo.Server.Application/World/MonsterDropDomainService.Sources.cs)。

| 根意图 | 审查结果 |
|---|---|
| RI-1 | 中性登记覆盖多个消费领域。作用范围、来源、名称、时效、整批替换、声明为空及来源可缺席均在范围内 |
| RI-2 | 经验结算实际使用登记权益。原计算顺序、取整、适用入口和恢复行为均有验证条件 |
| RI-3 | 幸运神器共享及任意队友拾取有独立验收条件。当前实现为共享、无个人归属的额外期望 1.5 次投掷 |
| RI-4 | 强化结算受时效权益影响。既有可确认来源归拢，新道具内容另行定义 |
| RI-5 | 请求的契约、重构和复扫均有对应交付。复扫覆盖实际入口及未迁移项 |
| RI-6 | 既有行为和协议受兼容约束。边界与服务端重构授权一致 |

新道具内容、活动数值及客户端协议由独立需求决定。当前契约仍覆盖真实强化入口、已存在的可确认来源以及三类已发现的玩法加成。部署和客户端补丁发布位于这次服务端归拢的授权范围之外。

| 维度 | 证据类别 | 依据 | 实际意义 |
|---|---|---|---|
| quality | observed | SC-1 至 SC-5 定义了各域结果和验证对应关系 | 验收可以以结算行为和覆盖证据判定 |
| efficiency | proxy | 兼容约束延用既有规则与协议 | 有望减少重算旧玩法口径的工作 |
| cost | proxy | 交付限定三个结算领域及现有规则来源 | 成本主要由来源盘点、接线和行为回归构成 |
| cognitive burden | proxy | 通用登记和三类消费者职责明确，契约未固定内部结构 | 后续可以沿来源和结算入口理解实现 |
| actual human participation | observed | 用户明确要求通用组件、决定共享归属并授权执行及复扫 | 核心产品边界来自用户决策，技术评审完成契约核对 |

执行中应将普通 Boss 掉落读取的账号成长服务加成纳入范围盘点。它在 [怪物掉落领域服务](../../../Server/src/Dfo.Server.Application/World/MonsterDropDomainService.cs) 单独读取并传入独立掉落，适用契约现有的玩法来源归拢条件。
