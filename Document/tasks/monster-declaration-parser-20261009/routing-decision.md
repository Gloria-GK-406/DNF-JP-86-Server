# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

授权、范围和结果明确。解析结构可由当前 PVF 与参考代码调查，生成语义不确定项按契约保留，避免扩展为完整机关引擎。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求任务契约；若只修已知标记会漏掉其他变体，或将占位/模板当普通实体，引入清房和奖励错误。统一覆盖表、消费者计划和独立结果审查保护这一连贯结果，收益高于简短制品成本。
- Reroute conditions: 若出现必须实现全新机关运行引擎才能满足解析接线的事实，重新确定边界，不隐式扩大。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 解析模型、模板身份与消费者之间需要明确接线计划；无需额外人工审批或 Governed 控制。直接详细 Plan，调查并行、生产写入串行。

## Workspace

- Root: `E:/Project/DNF/DNF-JP-86-Server/.worktrees/monster-declaration-parser`
- Branch: `task/monster-declaration-parser`
- Accepted baseline: `8e8192b94f5acc3ab7a471e27ed197d08ae8c7fd`
- 新工作区初始 clean；原 `achievement-state-events` 有四份未提交文档，原样保留。原生工作区工具返回 Not a git repository，使用主仓库 git worktree add 创建非嵌套工作区，目录已被 ignore。
