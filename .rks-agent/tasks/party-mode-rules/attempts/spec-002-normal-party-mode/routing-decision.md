# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

该子结果只有一个 Normal 模式接受边界，目标、范围、证据停止条件和客户端可观察结果均已明确。取证可以
在授权范围内决定 target/default 的真实规则，不会把 Story/Cycle 或公开队伍目录引入结果语义。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 直接实现最可能把客户端 Normal target `0` 与现行服务端默认 `0xFFFF` 草率视为等价，
  从而污染创建、编辑和后继 Story/Cycle 的规则。持久 Spec、显式调查/交付计划、完整结果审查与新鲜
  验证能实质降低该逃逸风险，精度收益值得额外时间。Normal 的证据、规则、生命周期和实机闭环共同
  形成一个接受/回退边界，不需要拆为多个独立 Spec。
- Reroute conditions: 若 A21 证明 Normal 的创建与编辑由两个不能共享原子边界的独立协议/状态机承担，
  或现有共享模型无法表达已确认 Normal 差异，则返回根控制器重评；局部字段、默认值或生命周期修正
  保持当前 Single Spec。

## Controller Selection

- Controller: `sdd-standard`
- Profile: `planned-delivery`
- Rationale: Lite 的完成审查发生得太晚，无法低成本阻止错误 target/default 结论先后传播到规则、领域、
  通知与实机脚本。Standard 的显式 Plan 能把“证据裁决 -> 规则/行为 -> 当前 A21 闭环”设为有依赖的
  停止边界，并在开始实现前暴露证据缺口。任务没有安全、凭据、不可逆外部变更或数据损失硬门槛，
  普通独立完整结果审查和新鲜验证足够，不需要 Governed。
