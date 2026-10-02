# Routing Decision

- TaskContract: ./TaskContract.md
- Contract revision: 1

## SDD Readiness

已接受aa5b6636闭合业务模块；标准CI/打包和最终验收缺口明确，接缝可直接实施，无未决外部前置。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 用户要求逐模块精确验收；Direct容易只验证现有dist而遗漏干净源码发布。保留一份构建/发布Spec与完整结果审查，集中核对源码到候选包及使用清单；独立于已接受业务，具有一个集成接受边界。
- Reroute conditions: 若需要修改管理员/资产规则、现服部署或新增独立能力，返回根评估；不以集成名义扩展范围。

## Controller Selection

- Controller: sdd-lite
- Profile source: focused-delivery
- Rationale: 既有接缝与执行路线确定；主控直接执行，最终独立结果审查和实际候选包/完整回归足以发现资源漏装。保留既有认证和资产逻辑，只接入确定构建并校验隔离候选；不存在需先独立裁决的新权限或持久资产方案，额外Plan/challenge不能提高此边界精度。
