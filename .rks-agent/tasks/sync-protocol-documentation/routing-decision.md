# Routing Decision

- TaskContract: `.rks-agent/tasks/sync-protocol-documentation/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

目标、范围与成功条件已经稳定：在新分支中同步当前协议实现面与 A21 资料库，同时以源码、逆向和客户端行为三方证据限制结论。无法闭合的协议可以显式保留未知，因此现有证据差异不会改变任务语义。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户重视协议结论的证据强度，直接批量修改的具体风险是把 85 个以上的注册/编码入口误写成已验证协议，或在大量页面间漏掉矛盾。一个统一 Spec、显式证据矩阵、计划化分批更新和完整结果审查能够减少这种遗漏与错误传播，其精度收益值得额外时间。所有改动共同服务于“资料库重新成为权威进度源”这一单一验收边界，不需要独立接受或回滚的子成果。
- Reroute conditions: 若发现必须修改协议业务代码才能满足文档真实性，或出现需要分别接受/回滚的独立客户端逆向成果，则停止当前文档任务并重新路由；单纯的证据不足不触发 reroute，而是保留未知状态。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: Lite 的最终审查不足以防止早期分类错误传播到 catalog、单协议页、流程页和生成输入；Standard 的显式 Plan 与依赖顺序能先固定证据口径，再批量同步并验证。该任务不触及安全、凭据、不可逆外部变更或数据丢失硬门槛，普通完整结果审查足够，不需要 Governed。
