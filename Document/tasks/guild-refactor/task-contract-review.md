---
kind: rks-task-contract-review
contract_path: E:/Project/DNF/DNF-JP-86-Server/.worktrees/guild-system/Document/tasks/guild-refactor/TaskContract.md
contract_revision: 1
reviewer: /root/contract
selection_basis: 独立读取原始请求、工作树规范和完整评审方案
verdict: approved
---

# 公会重构任务契约审查

用户要求在指定公会工作树一次性完成上一轮公会设计评审提出的重构。已核对[范围来源](../../../../../../Document/JP86-公会系统设计评审与重构方案-20261128.md)、[本次契约](TaskContract.md)及[仓库开发约束](../../../AGENTS.md)。五组功能结果逐项对应 RI-1 至 RI-5，整体验证及证据等级归入 RI-6，均为 `in_scope`。基地访问和会籍代际通过同一选图与结算链协同，二者可分别验收位置实例、同秒重入的识别能力。

人数上限须有确切依据，基地战争与基地升档保持既有裁定。用户授权覆盖本地重构、编译和隔离测试，玩家库和远端发布均在此边界之外。回归时以方案列出的无关收件人、取消重选、历史归属不确定数据、仓库审计失败等情形具体化契约要求。

| 维度 | 证据类别 | 依据与影响 |
|---|---|---|
| quality | observed | 五组主要发现及金币改名回滚、职级规则集中已有对应验收条件，可以逐条核验。 |
| efficiency | proxy | 同一工作树整体验证能共享会籍和基地准入的准备，实际耗时须由执行记录给出。 |
| cost | proxy | 隔离库可验证迁移及回滚，避免影响玩家数据。准确性依赖旧库样本覆盖。 |
| cognitive burden | observed | 六行意图按职责和全程交付分组，附原方案可以追查具体场景。 |
| actual human participation | observed | 用户在收到评审和实施次序后明确授权执行全部重构。新增实机验收仍需实际客户端记录。 |
