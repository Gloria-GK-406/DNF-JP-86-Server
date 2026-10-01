# Routing Decision
- TaskContract: `TaskContract.md`
- Contract revision: 1
## SDD Readiness
用户已批准宠物生命周期范围及70优先方向，具体公式由调查落实。
## Structural Selection
- Selected workflow: single-spec-workflow
- Rationale: 身份、背包消耗、时间状态和多报文容易局部完成却遗漏重登路径；单一Spec和逐任务覆盖可避免重演已发生的断线与空详情，值得增加计划和独立结果评审。各阶段共同交付一个宠物系统。
- Reroute conditions: 必须改变客户端或核心游戏规则时重新对齐。
## Controller Selection
- Controller: sdd-standard
- Profile source: long-running
- Rationale: 后续任务依赖70/A21调查，需渐进任务单元；无额外人工批准需求。
## Workspace
- root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap
- branch: task/solo-play-roadmap
- accepted_baseline: b8d2a4dca763ab27ccee391c10e45a66dee18c34
- baseline: linked worktree verified, clean before task artifacts; reuse current task branch, no nested worktree.
