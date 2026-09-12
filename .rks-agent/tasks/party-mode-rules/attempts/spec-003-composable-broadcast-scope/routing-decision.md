# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户要求的组合维度和跨频道不可见结果明确；现有 `SessionPresence.ListenerPort/TownId/AreaId` 提供已知
数据来源，六个遗漏 listener 的调用点与通知 resolver 边界也已定位。公开队伍目录被明确留给后继，
因此本结果无需猜测尚未实现的目录状态机。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 直接在六个调用点分别补 listener 虽能暂时修 UI，却会继续复制松散参数并遗漏用户要求的
  全服/频道/区域组合能力；反之只新增抽象而不迁移全部附近角色路径，也会留下可见性漏洞。持久 Spec、
  计划、完整结果独立审查和新鲜验证能把“通用组合语义”与“所有当前消费者完成迁移”作为一个可审计
  边界，精度收益值得额外时间。该基础设施和附近角色隔离必须共同接受或回退，但公开队伍目录可在其上
  独立验收，所以当前采用一个 Single Spec。
- Reroute conditions: 若 listener port 不能唯一代表频道，或某个附近角色路径必须使用与其余路径冲突的
  可见性规则，则返回根控制器重建频道身份/拆分边界；公开队伍目录仅是正常后继，不触发当前重路由。

## Controller Selection

- Controller: `sdd-standard`
- Profile: `planned-delivery`
- Rationale: Lite 没有显式 Plan，无法在实现前固定“组合值语义与校验 → presence 查询 → audience 解析
  → 六个附近角色消费者迁移”的依赖顺序，也难以及早发现动态范围解析与名单快照不一致。Standard 的
  计划与完整结果审查足以保护这些耦合决策；任务不涉及安全、凭据、不可逆外部变更或数据损失硬门槛，
  无需 Governed 的独立挑战与更强审计控制。
