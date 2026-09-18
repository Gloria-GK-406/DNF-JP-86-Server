# Routing Decision

- TaskContract: `.rks-agent/tasks/remove-session-login-snapshot/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已确认移除长期选角结果快照的边界、保留一次性登录 DTO 与连接代际保护；旧快照回灌耐久已定位，验收可由消费者审计与关键投影测试证明。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求任务契约并重视消除双真源。若直接跨多个业务点修改，容易漏掉一个旧快照消费者而形成隐蔽回灌；持久 Spec、显式迁移计划和完整结果审查值得额外时间。所有修改服务于一个可共同验收的边界，不构成独立子成果。
- Reroute conditions: 若证据表明必须改变登录协议或持久化模式等合同外语义，先回到契约对齐；若出现可独立接受/回滚的成果，再评估 Multi Spec。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 迁移需先固定目录最小职责、再逐类替换读取、最后删除重复写回；明确依赖顺序可防止早期接口选择向多处扩散。Lite 的结果审查不能同样便宜地纠正这种耦合返工；没有证据显示需 Governed 的独立挑战或高风险硬门槛。
