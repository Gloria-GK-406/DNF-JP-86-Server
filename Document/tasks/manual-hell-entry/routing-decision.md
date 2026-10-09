# Routing Decision
- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness
用户确认输入、输出类型、路由与主动流程，授权实现。真实客户端验收与后续战斗不包含在本次自动完成边界。

## Structural Selection
- Selected workflow: `single-spec-workflow`
- Rationale: 主动深渊入场为一个完整接受边界。Direct易遗漏模式严格匹配、全队扣票及不降级规则；短Spec和独立完整结果审查可一起约束这些遗漏，额外成本小于实机才发现扣票或路由错误的返工。
- Reroute conditions: 若入场协议无法靠静态证据建立可用投影，回到边界评估；若需独立战斗/掉落成果，另立任务。

## Controller Selection
- Controller: `sdd-lite`
- Profile source: `focused-delivery`
- Rationale: 当前接受流程明确，可直接实现；无需Plan/Task registry。独立最终审查足以保护成本与共享run一致性。

## Workspace
- Root: `E:/Project/DNF/DNF-JP-86-Server/.worktrees/abyss-research`
- Branch: `task/abyss-research`
- Accepted baseline: `b596b06bcef121e6444127850980032115a1b0fb`
- Baseline evidence: linked git-dir `.git/worktrees/abyss-research`，common-dir主仓库.git，工作树初始clean；仅创建本任务Document/tasks/manual-hell-entry下启动文档。
