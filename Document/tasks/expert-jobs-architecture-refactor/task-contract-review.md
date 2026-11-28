---
kind: rks-task-contract-review
contract_path: E:/Project/DNF/DNF-JP-86-Server/.worktrees/expert-jobs/Document/tasks/expert-jobs-architecture-refactor/TaskContract.md
contract_revision: 1
reviewer: /root/contract_review
selection_basis: 按契约评审技能选用独立审阅者，未参与本契约建模、意图对齐或路由。
verdict: approved
---

# 副职业架构重构契约评审

## 依据

- 用户明确授权“你来根据方案来写任务契约后完成重构任务吧”。
- [架构审查与重构方案](/E:/Project/DNF/Document/expert-jobs-architecture-review.md)界定已确定的五组重构与验证范围。
- [工作树指令](/E:/Project/DNF/DNF-JP-86-Server/.worktrees/expert-jobs/AGENTS.md)、[现行架构](/E:/Project/DNF/DNF-JP-86-Server/.worktrees/expert-jobs/Document/Architecture.md)、[架构审核规范](/E:/Project/DNF/DNF-JP-86-Server/.worktrees/expert-jobs/Document/ArchitectureReview.md)约束职责、依赖和交付边界。
- [原副职业契约](/E:/Project/DNF/DNF-JP-86-Server/.worktrees/expert-jobs/Document/tasks/expert-jobs/TaskContract.md)界定需保护的玩法及独立 A21 验收，[新功能开发流程](/E:/Project/DNF/DNF-JP-86-Server/.worktrees/expert-jobs/Document/FeatureDevelopment.md)明确测试数据、用户实机和分支交付的阶段责任。

## 意图与排除范围

| 条目 | 处置核对 |
| --- | --- |
| RI-1 | `in_scope`。契约保留原话、授权和对照方案，锁定指定分支及工作区基线。 |
| RI-2 至 RI-6 | `in_scope`。逐项覆盖规则层、职业事实、任务技能及资产、关摊结果、既有卡片升级，分别可由 SC-1 至 SC-5 验证。 |
| RI-7 | `in_scope`。四职业行为、工作区已有修正和原子结算按 SC-2 至 SC-6 复核。保护既有协议、玩法及表结构，且收口的是已有 `0x025C` 实现。 |
| RI-8 | `preserved_upstream`。依契约链接的原验收计划与仓库开发流程，A21 用户验收仍由用户在另行协调和授权的环境完成，主库切换和备份由验收准备阶段负责，PR、合并与发布由后续独立阶段负责。该处置不转移本契约的自动验证和实机边界记录。 |

范围外的新增玩法、协议定义、玩法数值调整和数据库结构迁移与方案的结构重构及兼容范围一致。共享库、运行实例和发布边界有仓库规范与原任务契约支持。SC-1 至 SC-6 对应可观察结果，测试手段与题设风险相称。契约的通过只确认意图与执行边界。

## 五维评估

| 维度 | 证据级别 | 依据 | 实际影响 |
| --- | --- | --- | --- |
| quality | observed | SC-1 至 SC-6 对照审查方案中的事实所有权、拒绝及事务场景 | 可以逐项检查职责收口及业务兼容，实施质量仍待结果验证 |
| efficiency | proxy | 同一分支上复用已有玩法事务、职业表与自动回归入口 | 可缩小迁移范围，实际工时取决于调用方迁移和测试结果 |
| cost | proxy | 范围外明确迁移、发布和共享实例操作，验证使用隔离数据 | 预计减少运行环境切换和数据操作成本，未测量实际成本 |
| cognitive burden | observed | 一份契约对应五组结构结果和一组综合验证，复用已审查方案 | 人与实施者可沿条件定位责任和证据 |
| actual human participation | observed | 用户直接授权按上一轮方案编写契约并重构 | 意图和授权由用户给出，技术评审不代替 A21 用户验收 |
