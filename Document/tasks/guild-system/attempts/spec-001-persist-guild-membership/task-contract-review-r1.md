---
kind: rks-task-contract-review
contract_path: Document/tasks/guild-system/attempts/spec-001-persist-guild-membership/TaskContract.md
contract_revision: 1
reviewer: /root/contract_review
selection_basis: 独立同级评审者，未参与本子契约建模、意图对齐或路由选择，用户与项目未指定最强评审者
verdict: changes_required
---

# 公会创建与成员关系基础契约评审

评审 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/guild-system/Document/tasks/guild-system/attempts/spec-001-persist-guild-membership/TaskContract.md` 第 1 版，SHA-256 `A51492C4A4F8735F88B8855BBA56151B043269AA6911CB525D4095B9CB2180EC`。

## 权威来源与意图覆盖

- 用户选择「完整公会系统分期实现」，并授权编写任务契约和实施。子任务通过[根任务契约](../../TaskContract.md)承接 SC-01、SC-02、SC-03 的基础领域部分。
- [公会领域服务设计](E:/Project/DNF/Document/JP86-公会领域服务设计-20261124.md)说明核心成员操作及持久化边界，[公会系统设计方案](E:/Project/DNF/Document/JP86-公会系统设计方案-20261124.md)说明首期与后续 A21 协议验证的交付边界。
- [服务器开发流程](../../../../FeatureDevelopment.md)要求隔离数据库、区分自动化与实机验收。当前阶段交付领域能力，后续 A21 交互、完整社会玩法、用户验收等仍由父契约承担。RI-4 的 `preserved_upstream` 归属明确。当前 Topology、API 签名与表结构仍可在详细设计阶段决定。
- RI-1 至 RI-3 的阶段边界与父契约一致，成功条件存在以下漏项。

## 阻断项

1. 目标范围承诺入会申请及踢出。AC-3 可以通过预置申请并直接验证批准，没有成功提交申请及按角色和公会持久保存的条件。AC-4 只要求拒绝越权踢人，实现若拒绝所有踢人也能通过。将「有效申请可提交、持久化并可复查」加入 AC-3，将「有权操作者踢出同会普通成员会清除该成员关系」加入 AC-4。
2. AC-1 的旧数据检查只写角色和资产。父契约 SC-01 还要求保有现存账号，本期负责迁移，应明确检查账号、角色与资产。

## 五项评估

| 维度 | 证据状态 | 依据 | 影响 |
| --- | --- | --- | --- |
| quality | `proxy` | AC-1 至 AC-4 覆盖迁移、事务、成员唯一及删角边界，尚有上述正向操作和账号保留漏项。 | 补齐后可独立证明基础行为；尚无实现结果。 |
| efficiency | `proxy` | 子契约将持久化与协议连线分别交付，并保留父契约的 A21 验证。 | 可在不依赖尚待采集的客户端交互的情况下推进原子行为。 |
| cost | `missing` | 子契约没有经观察的开发工时或运行成本。 | 阶段不以未经验证的工时指标设置门槛。 |
| cognitive burden | `proxy` | 四条验收覆盖迁移、创建、审批、退出，后续玩法继续由父契约承担。 | 两处验收漏项可在现有行内修复。 |
| actual human participation | `observed` | 用户明确选择完整分期并授权执行。 | 阶段授权来自父契约，不代表用户已进行了客户端验收。 |
