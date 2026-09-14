# Routing Decision

- TaskContract: `.rks-agent/tasks/party-service-directory-scope-refactor/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

目标、范围和可观察验收条件已经稳定：PartyService 单独承载队伍行为及索引，角色领域提供
位置镜像，目录按逻辑频道和共享可见区域过滤，协议行为保持兼容。跨城镇/跨区域的原服语义
不作为本任务前提，剧情地下城功能修复也已明确排除，因此可以在现有证据边界内执行。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求用任务契约控制这次领域边界重构。若直接跨 Party 数据模型、应用命令、
  Presence/Channel 适配和协议投递修改，最容易遗漏 Session 泄漏、私有 `[gate]` 隔离或队长迁移
  后的陈旧索引；一份统一 Spec、显式依赖计划和完整结果复核能在这些耦合点扩散前固定顺序和
  验收。所有变化必须共同形成一个可工作的队伍垂直切片，没有可单独验收或回滚的第二结果，
  因而采用 Single Spec。
- Reroute conditions: 若实现调查证明角色在线位置镜像必须先由独立任务建立，或剧情地下城流程
  修复成为本重构不可分割的前置验收结果，则停止并重新评估任务边界；若仅发现局部适配或测试
  工作，不改变当前结构。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: Lite 的最终复核不足以约束“先建立角色镜像/逻辑频道边界，再迁移 Party 状态和协议
  audience，最后切换范围索引”的依赖顺序，错误的早期模型会造成多层返工；Standard 的显式 Plan
  和普通独立结果复核足以降低该风险。本任务不涉及凭据、安全、不可逆外部操作或数据损失，且
  已有明确协议证据，因此 Governed 的独立挑战与审批成本没有额外收益。
