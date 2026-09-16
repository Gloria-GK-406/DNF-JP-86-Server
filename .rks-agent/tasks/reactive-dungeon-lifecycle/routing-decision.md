# Routing Decision

- TaskContract: `E:\Project\DNF\DNF-JP-86-Server\.rks-agent\tasks\reactive-dungeon-lifecycle\TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已经确认现象边界：P2P 移动正常、权威地面物消失正常，仅远端拾取表现缺失；任务物品进度、Boss 清关、参与者结算、翻牌与独立回城的期望也有可观察的双客户端结果。运行日志、PVF 任务内容、现有领域状态与 A21 上游结算时序为执行方向提供了足够证据，不需要再发明任务语义。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Rationale: 用户偏好先建立清晰领域边界再实机验证。直接在现有 Handler 中同时修改拾取、任务、结算与计时，最可能遗漏共享事实和参与者私有事实的边界，或让旧直发路径继续成为第二真源。SDD 的不可变子结果、独立复审和逐次集成能直接控制该风险，且相对于后续双客户端返工成本值得。拾取表现与统一运行期变更、任务物品与 derived Trigger、结算翻牌与独立回城各有独立验收和回滚边界，因此选择 Multi Spec。
- Reroute conditions: 若证据证明远端拾取动作完全由不可服务端控制的客户端 UDP 表现决定，则第一子结果缩减为保留现状并只迁移权威拾取变更；若 A21 实证否定现有任务完成或结算时序，先封存当前子结果并回到根控制器重新评估后续边界。
