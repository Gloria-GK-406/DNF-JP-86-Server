# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

只读玩家查询与当前在线展示是已授权且可单独验收的稳定结果。数据与运行态真源已明确，具体分页/API/UI组合可在该边界内选择，不预定后续写功能。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户要求逐模块验收；查询容易把entry旧坐标、频道静态人数或密码账号record直接暴露到后台。独立结果Spec固定资料范围与运行态来源，完整审查可发现数据泄露及错误在线归属，收益大于本阶段有限规格/审查时间；查询能力形成一个可独立接受的边界。
- Reroute conditions: 跨进程/多服监控、连接个人资料或写入能力改变该结果边界时回到root successor decision。

## Controller Selection

- Controller: `sdd-governed`
- Profile source: `focused-delivery`
- Rationale: 本阶段直接开放受保护的玩家账号/角色资料，需要独立挑战与批准确认鉴权和最小资料投影；已有成熟共同访问边界，范围清楚，因此采用planless focused，不增加Plan/TaskUnit。生产交bounded worker，读取与审查分别独立，限制挑战只针对具体泄露/错误归属风险。
