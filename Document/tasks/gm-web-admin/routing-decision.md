# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

用户接受已调研的模块及技术选型，并授权设计与实施。可观察边界为后台启动、管理员权限、查询、邮件、点券和集成验收，内部设计可在该范围内选择。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Rationale: 启动骨架、管理员权限、只读查询、邮件和点券具有独立可验收结果。直接一次实现容易遗漏开关隔离、在线一致性和重复发放；每个能力独立 Spec、完整审查和验收提交能够发现遗漏并保留前阶段结果。用户明确要求逐步分 Spec，接受该控制的时间成本。
- Reroute conditions: 独立进程、多服/跨系统发布、实时运营切换等扩大原任务语义时重新对齐；某个子 Spec 发现具体安全边界时提升该子控制强度。
