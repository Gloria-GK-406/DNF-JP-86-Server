# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已经确定同仓库、WPF、单一 HTTP BaseUrl、服务端动态下发端点、账号注册登录、短时票据与 Game 登录联动，并授权直接实现；结果可通过 API、票据消费、启动参数和 WPF 启动行为观察。UI 具体样式与安全机制的局部实现选择已授权由 Agent 决定，不存在改变任务含义的未决问题。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户要求功能优先但完整交付；Direct 最可能遗漏的是启动器成功、HTTP 成功和 Game 登录成功各自成立，却没有形成同一条不可冒名、不可重放的身份链。一个贯穿三者的 Spec、独立挑战与完整结果审查能直接降低该失败风险，且该功能只有端到端共同成立才可接受，没有独立集成或回滚价值足以拆成多个 Spec。
- Reroute conditions: 只有发现必须修改客户端二进制、需要独立部署多个持久化认证服务，或启动器与服务端被要求分别交付/回滚时，才重新评估任务含义或 Multi Spec 边界。

## Controller Selection

- Controller: `sdd-governed`
- Profile source: `code-feature`
- Rationale: 任务直接改变密码验证、登录票据与账号授权，属于 credentials/access-control 硬下限；Standard 的普通评审不足以隔离同一实现者对可重放、账号错绑和旧密码迁移风险的盲点，因此需要独立规格/计划挑战、独立代码评审、漂移检查和新鲜验收证据。`code-feature` 提供所需的 TDD 与完整代码审查，未增加与本任务无关的控制。
