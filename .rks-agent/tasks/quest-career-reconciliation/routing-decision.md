# Routing Decision
- TaskContract: `./TaskContract.md`
- Contract revision: 1
## SDD Readiness
用户明确要求任务契约并实施；缺失链路已有代码证据，范围为任务转职和统一技能整理。
## Structural Selection
- Selected workflow: single-spec-workflow
- Rationale: 单一验收结果。持久化转职、技能和通知顺序容易遗漏，短Spec及独立结果审查值得成本。
- Reroute conditions: 如发现新任务链或不可复用的职业规则，重新评估范围。
## Controller Selection
- Controller: sdd-lite
- Profile source: focused-delivery
- Rationale: 实施方向已确定，无需额外计划阶段。主代理执行，独立审查检查原子性和技能丢失风险。
## Workspace
- Root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/quest-career-reconciliation
- Branch: feat/quest-career-reconciliation
- Baseline: 6672b21f19435e69d97a6af190b37a938fa91a41
- Source: main（保留原有未提交文档）
- Evidence: worktree创建后HEAD相同，唯一未跟踪文件为本任务契约，转移前后SHA256一致。
