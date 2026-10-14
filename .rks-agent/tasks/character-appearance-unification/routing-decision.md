# Routing Decision

- TaskContract: `.rks-agent/tasks/character-appearance-unification/TaskContract.md`
- Contract revision: 1

## SDD Readiness

职责划分与结果已获授权；候选回退、所有路径统一、原blob真源、部署后用户验收均明确。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 上轮漏掉选角且初始化路径显示失配，单纯局部修复容易再次遗漏。用户明确要求契约和全路径归一，持久Spec与独立全结果审查能直接防止遗漏，值得增加流程成本。只有一个角色外貌一致性的验收边界。
- Reroute conditions: 必须修改客户端或新建外貌持久化才能达到结果时重新判断范围。

## Controller Selection

- Controller: sdd-lite
- Profile source: focused-delivery
- Rationale: 统一候选、纯规则与协议适配的中心路线已经确定，路径矩阵和结果审查可约束实施，不需要单独Plan或强治理。沿用用户指定的当前工作分支/工作区，基准1e146ba1，已有Tool/WireSmoke/__pycache__/不归本任务。
