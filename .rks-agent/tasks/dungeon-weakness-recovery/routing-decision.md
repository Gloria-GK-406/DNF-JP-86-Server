# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

规则和内存生命周期已经用户明确选定，协议投影已实测。付费规则和资格排除，不阻塞自然恢复。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户重视既定生命周期规则。Direct 容易漏掉断线与重复清理，持久 Spec 加独立完整结果评审可逐出口核对，值得轻量流程成本。所有变化共同交付一个虚弱生命周期。
- Reroute conditions: 若实际代码要求改变已选生命周期或不可安全隔离退出结算，再评估；普通局部接线不改路线。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `focused-delivery`
- Rationale: 无待定中心设计，Spec 和完整结果独立评审足够，不需要额外计划审批。

## Workspace

- Root: `E:/Project/DNF/DNF-JP-86-Server/.worktrees/dungeon-weakness-recovery`
- Branch: `feat/dungeon-weakness-recovery`
- Baseline: `88741631d4f104fe63d35945f896dd492d647c50`
- 创建后 root、branch、HEAD 匹配，status 空；主工作区原有文档修改与外部报告保留。原生工具在父目录返回 Not a git repository，使用仓库内 git worktree 创建。
