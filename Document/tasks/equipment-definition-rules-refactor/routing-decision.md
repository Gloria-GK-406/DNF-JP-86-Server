# Routing Decision

- TaskContract: `TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已授权执行，结果与范围已稳定。具体接口聚合、无耐久语义、非法内容异常和最终用户验收由契约限定。用户另明确授权直接提交并接受当前功能不完整基线，完成后复用现有 linked worktree。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 单一装备基本信息/规则边界重构，所有调用方需要共同迁移后才可接受。用户明确要求细致与最终验收；直接替换字段容易遗漏旧读取路径、原始禁止声明或异常事务边界。固定 Spec、迁移 Plan 与独立完整审查能发现这些遗漏，额外控制成本小于再次实机发现首饰被误拒绝的返工成本。
- Reroute conditions: 如必须新增增幅升级交易、扩展全仓 PVF 解析框架或修改资产恢复策略，先返回范围对齐，不将其混入本重构。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 通用内容契约、内容源、统一规则、生产接线与测试夹具必须按依赖顺序迁移；明确计划避免旧源在部分消费者中继续生效。Lite 的末尾审查不能替代这一迁移顺序约束。未新增资产结算算法或持久化结构，既有事务回滚可恢复，不需要 Governed 的额外挑战与批准阶段。
- Workspace prerequisite: 用户已授权本地提交当前功能不完整状态；提交后证明工作区 clean，并以新 HEAD 作为本重构基线。已知缺口见 `Accepted-Baseline.md`。
