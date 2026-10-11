# Routing Decision

- TaskContract: `Document/tasks/yellow-dragon-tournament/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

黄龙大会完整循环与部署的范围已明确，用户授权研究并实施。剩余协议与轮次细节属于可由静态研究解决的执行问题，不改变交付目标。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求契约与研究后计划。直接套用普通地下城会使第一轮误通关、轮次回包错误或重复收费；一份 Spec 固定完整大会验收，配合顺序计划保护这些相互依赖的决策，额外记录成本具有实际价值。研究、实现和部署共同服务同一个黄龙大会验收边界。
- Reroute conditions: 若原生协议证明必须客户端补丁才能成立且超出授权，则重新对齐；发现独立验收目标才拆分，不按开发步骤拆 Spec。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 先闭合客户端字段与加载时序，再实现大会实体、死亡推进和奖励，显式依赖计划可阻止早期错误传播。Lite 的直接执行不适合尚待确定的耦合协议决策；当前有可备份数据库及可恢复部署，不需要 Governed 的额外审批。

## Workspace

- workspace_root: `E:/Project/DNF/DNF-JP-86-Server/.worktrees/despair-tower`
- task_branch: `task/despair-tower`
- accepted_baseline: `711f489e8cdf33e22f95e07a2d140d217afc2e09`
- durable_artifact_paths: `Document/tasks/yellow-dragon-tournament/`
- baseline_evidence: Git root、git-dir/common-dir、分支与 HEAD 已核对；契约创建前 `git status --short` 为空。复用既有任务隔离，主干不变，不嵌套工作区。
