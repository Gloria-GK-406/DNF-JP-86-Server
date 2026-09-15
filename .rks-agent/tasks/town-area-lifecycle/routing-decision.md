# Routing Decision

- TaskContract: `.rks-agent/tasks/town-area-lifecycle/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已稳定指定区域身份、生命周期职责、广播归属、同区移动归一及主动退本语义；问题 2 和问题 4 明确排除。现有代码和运行证据已经定位状态缺口与散落广播，无阻塞问题，任务可执行。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 本任务的可验收结果是一个原子性的区域生命周期闭环；若区域状态迁移和协议投影拆成独立验收边界，任一半完成都会留下更不一致的运行状态。直接边改边补最容易漏掉选角、选图、取消、进本、回城、主动退出或断线中的一条路径。一个持久 Spec 加生命周期矩阵、实施计划和完整结果复核能以合理成本防止这种遗漏，因此采用单一 Spec 的标准 SDD。
- Reroute conditions: 若证据表明现有客户端协议无法用同一成员快照投影满足城镇加入/返回，或主动退本消息无法与正常完成返回可靠区分，则停止并回到契约边界；若出现可独立交付和回滚的协议逆向前置任务，则转为 Multi Spec。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: Lite 缺少显式计划，不能充分保护区域身份、并发代次、生命周期入口迁移和协议投影顺序这几个相互约束的早期决策；Standard 的计划、普通独立结果复核和新鲜验证足够降低返工。任务不触及凭据、安全、不可逆数据或外部生产变更，Governed 的独立挑战与多重审批没有相称收益。
