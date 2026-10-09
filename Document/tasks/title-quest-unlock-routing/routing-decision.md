# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

用户已明确并授权双路线重构/部署。4303→6501、6531实读PVF证明路线差异；现有任务事实推进、事务完成和EventBus可复用。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 单一可验收闭环。上次将任务受理与被动进度混合的遗漏已造成真实拒绝及潜在提前奖励；Spec固定阶段责任、独立审查检查旧计数污染和重复奖励，收益高于短文档成本。
- Reroute conditions: 真实PVF不能区分两路线或奖励无法安全映射时，重新核对证据，不能改成默认累计。

## Controller Selection

- Controller: sdd-lite
- Profile source: focused-delivery
- Rationale: 边界已明确，主代理直接实施，完整结果独立审查和回归足够；无独立计划/任务单元。
- Workspace: `E:/Project/DNF/DNF-JP-86-Server/.worktrees/abyss-research`，`task/abyss-research`，HEAD `9da9b83ee2ad3f42502d575254d8a176e6bbfeac`。
- Baseline: 继续用户指定原分支；现有未提交镜像实现及深渊资料按开始时状态/哈希保留，作为当前运行基线。不采用清理或回滚来满足纯净工作区模板；本次只增量修改任务解锁职责，独立增量审查。未授权提交未验收的既有镜像结果。
