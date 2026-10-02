# Routing Decision

- TaskContract: ./TaskContract.md
- Contract revision: 1

## SDD Readiness

根授权覆盖按账号增加三种点券，独立可观测验收范围已稳定。余额表、账号租约/事务和提交后通知接缝明确，无外部未决前置；合法范围、防重和前后余额等细节由Spec约束。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 用户明确分模块精确验收；直接实施易遗漏无角色账号、同账号去重、重试后的持久到账及钱包投影顺序。可恢复发放与整批事务由一份Spec及完整独立审查集中验收，避免在昂贵实机测试才发现重复/丢币。一个按账号发放闭环具同一接受/回滚边界，独立于已接受邮件。
- Reroute conditions: 如发现公共钱包事实无法安全变更或原事务与持久防重假设不成立，回到根评估，不虚构角色或广泛改全effect体系；如出现独立新运营范围则回MultiSpec。

## Controller Selection

- Controller: sdd-governed
- Profile source: focused-delivery
- Rationale: 写入会产生持续玩家资产和受GrantCera权限约束的管理能力；先独立裁决防重复/并发/不确定提交方案，再执行及完整结果审查，保护重复到账或并发覆盖的持久后果。Lite只有最终审查，缺少先约束账号和权限边界；普通Plan不能替代独立判断。执行路线已有成熟邮件和账号接缝，不增加Plan或Task层。
