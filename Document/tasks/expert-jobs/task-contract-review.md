---
kind: rks-task-contract-review
contract_path: E:/Project/DNF/DNF-JP-86-Server/.worktrees/expert-jobs/Document/tasks/expert-jobs/TaskContract.md
contract_revision: 3
reviewer: /root/expert_active_contract_review
selection_basis: 独立于意图对齐、契约编写和路由选择的新评审上下文，按任务风险确定评审深度
verdict: approved
---

# JP86 四副职业工作区契约评审

## 评审依据

- 用户要求参照上游和 70，确认角色副职业存储及领域服务设计，并明确授权按第 3 版契约开始实现四个副职业。四职业实现完成后再进行 A21 实机。
- 工作区契约与已确认的原契约逐字比对。将原契约的对照资料与开发流程链接改为工作区相对路径后，正文一致。
- [已确认的领域设计](DomainDesign.md)、[上游与 70 对照](UpstreamEvidence.md)、[仓库约定](../../../AGENTS.md)、[新功能开发流程](../../../Document/FeatureDevelopment.md)。

## 意图与范围核对

- RI-1 至 RI-3 对应先据现有证据确认角色存储与服务组织、统一就职和放弃、按当前职业取得对应服务。SC-1 与 SC-2 有在线和重登核对条件，并覆盖错配及失效会话。
- RI-4 与 SC-3 至 SC-5 涵盖四职业、独立设备状态、相关 PVF 规则、摊位业务和双人原子结算，保留 NPC/便携分解与普通宝珠入口回归。
- RI-5 与 SC-6 保留实现后自动验证、集成验证和 A21 实机的次序，并区分基于参考实现的协议与仍需实帧确认的行为。
- RI-6 将独立怪物卡合成、升级和绑定留作取证，附魔师的卡片转宝珠及摊位业务仍在当前范围。RI-7 对应独立开发和验收阶段主库协调，PR、合并及发布沿用仓库流程与各自授权。

## 五项评估

| 维度 | 证据状态 | 依据 | 影响 |
| --- | --- | --- | --- |
| quality | observed | SC-1 至 SC-6 可观察并涵盖在线刷新、放弃关店、两方资产共同结算及旧入口回归 | 实现后的验证可逐项检查契约，尚无代码或实机通过结论 |
| efficiency | proxy | 领域设计沿用两张存档表和既有会话、任务与库存事务边界 | 有望减少重复实现，实际效率需待实现确认 |
| cost | proxy | 本次范围在独立工作区落实，保留需另行取证和主库协调的边界 | 开发成本可以按四职业评估，尚无实际用时或实机成本数据 |
| cognitive burden | observed | 经确认的契约和领域设计在工作区相邻，只有相对链接变化 | 用户确认过的范围和实施验收条件可直接回查 |
| actual human participation | observed | 用户提出领域入口、匹配语义，确认领域设计并明确授权第 3 版实施 | 工作区契约保留真实用户决定，A21 实机仍待实现完成 |
