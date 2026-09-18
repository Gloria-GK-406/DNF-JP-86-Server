# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已明确把公共目录延后并继续功能开发；Story、Cycle、最终建模审计的顺序、排除范围和可观察验收均已
稳定。每类的具体字段语义仍需由其子规格在上游/A21/运行时证据中解析，但该不确定性不会改变根任务含义。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Rationale: 用户要求在不等待公共目录的情况下继续高精度模式开发。若把 Story、Cycle 与最终审计塞入一次
  Direct 或单一 Spec，某一模式的运行时阻塞会使另一模式的已验证结果无法独立接受，也容易在长链路中遗失
  模式隔离和证据等级；串行子规格、独立验收提交和根级建模审查能控制这些具体失败，时间成本与既有工作
  方式一致。Story、Cycle 与最终审计具备独立验收/回滚边界，因此选择 Multi Spec。
- Reroute conditions: 若静态证据证明 Story/Cycle 必须先实现“快速组队”这一新增能力，或两类实际共享一个
  不可分割的原子协议状态机，则暂停子规格并重新对齐范围/结构；公共目录 Issue #20 的新证据不会自动扩展
  本任务。
