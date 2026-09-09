# Routing Decision

- TaskContract: `TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已明确静态查询、规则实例、动态事实的目标，已授权完成重构并明确排除新学习技能协议。动态实现细节委托 Agent，现有创建/选角/升级/技能点路径提供可观察验收边界。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求 SDD。职业维度、费用与免费基线、角色当前状态读取同时影响规则和多个消费者；直接局部迁移可能让登录与在线查询产生两套结果。一个完整 Spec 配合先统一契约后迁移调用的计划，可在错误依赖扩散前固定边界；完整代码审查与回归可识别遗漏调用。静态与动态模型共同完成一个角色模型结果，没有独立验收或集成边界。
- Reroute conditions: 若必须改变已批准的玩法或客户端行为才能满足目标，重新审视契约；若发现无法通过隔离验证证明可恢复的数据迁移风险，则重新选择治理控制。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Rationale: Lite 缺少用于固定职业键、规则实例接口及动态读取迁移顺序的显式计划，错误早期决定会让后续多个调用返工；Standard 提供所需依赖计划、完整审查和验证。实际数据不迁移到生产且已有事务与隔离副本验证路径，不存在必须增加独立规格审批或对抗挑战的高风险边界。所有阶段采用 auto，用户已授权设计和执行细节。
