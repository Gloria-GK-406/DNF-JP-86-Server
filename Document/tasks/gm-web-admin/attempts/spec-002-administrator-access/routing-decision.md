# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

认证、权限、注销和追踪是当前唯一独立结果，具体实现由生产 worker 在成熟 ASP.NET 机制和现有领域边界内选择。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 管理登录涉及凭据和私有玩家资料入口，漏掉注销/跨站/权限隔离会让后续资产写入被非管理员触发；一个封闭认证 Spec 独立验收。
- Reroute conditions: 外部 SSO、多服权限、管理员管理 UI 等扩大当前边界时回到根 successor decision。

## Controller Selection

- Controller: `sdd-governed`
- Profile source: `focused-delivery`
- Rationale: 直接改变凭据和访问控制，独立挑战与审批保护实际管理入口；与 Standard 相比这些控制能在资产 API 接入前发现授权绕过。流程清晰，不增加 Plan，仅独立 Spec 和完整结果挑战/审查，生产交给 bounded worker。
