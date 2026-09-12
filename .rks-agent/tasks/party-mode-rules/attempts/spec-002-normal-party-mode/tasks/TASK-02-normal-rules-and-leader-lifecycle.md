# TASK-02 — Normal 规则与队长离队生命周期

## Task Unit Contract

- Revision: 2 (current-A21 correction; identifies this contract and its detailed plan)
- Outcome and references: 消费 TASK-01 verdict，交付 Plan T-002、RQ-002..RQ-005、AC-002..AC-005
  的规则/领域结果：Normal canonical default/target 为 `0`；canonical `[1]` 仍统一为 0；当前 A21
  队长离开时清除全体成员并解散旧队，普通成员离开才刷新幸存者。
- Owned boundary: `Dfo.Server.Data/Parties` 的协议无关结果模型、`Dfo.Server.Abstractions/Parties` 契约、
  `Dfo.Server.Application/Parties` 规则与领域实现及其 Application/Data 测试；本 Task 文件。不得修改
  Protocol decoder/encoder/notification delivery、公开队伍目录、Story/Cycle 专属规则或协议资料。
- Required checks: 先以失败测试固定 Normal target/default、无污染拒绝、预设标题索引不被规则清除、
  多人队长离开 replacement、最后成员解散、普通成员离开、会话清理和精确旧会话；随后跑 Data 与
  Application Party 聚焦测试、完整 Application 测试及 `git diff --check`。
- Dependencies: TASK-01 `evidence/task01/runtime-verdict.md`；accepted spec-001 的 `PartySettings`、
  `IPartyModeRuleService`、精确 session/generation/revision 与 immutable `PartyNotificationPlan` 基线。

## Detailed Plan

1. 先跑当前 Data/Application Party 聚焦测试并记录基线。新增 RED 用例固定：
   - `DefaultSettings` 的 Normal target 从 `0xFFFF` 改为 `0`；Normal 非零 target 确定性拒绝，
     Story/Cycle 在各自后继 Spec 前不新增 target 语义；
   - rule normalization 只清 canonical `[1]`，canonical `[0]` 的预设标题索引与其余十字节保持不变；
   - 两/三人队长离开不会清除幸存者，旧 party id/generation 退休，新队由最低旧槽成员成为 slot0 队长，
     其余成员按旧槽顺序紧凑排列，设置、标题和 endpoint/HP 快照保持；
   - 单人最后成员离开只产生旧队 clear 并删除队伍；普通成员离开继续使用同 generation refresh；
     `CleanupSessionExact` 和显式离队遵守同一规则，旧 session 重放不改变新队。
2. 在 Data 中增加最小协议无关的退休描述值（旧 party id/generation/revision + 精确 clear audience），
   使一次领域结果可以同时携带旧 generation retirement 与新 generation snapshot。不要把网络 type、
   发送顺序或目录 audience 放进领域模型；保留现有同 generation member-leave clear 字段以免扩大回归面。
3. 在 `PartyModeRuleService` 将 Normal default target 改为 `0`，并只对 Normal 拒绝非零 target；把新规则
   失败映射为明确领域失败，保证 create/update 的失败前后 snapshot、revision、邀请和通知均不变。
4. 在 `PartyDomainService.LeaveExactLocked` 将 leader 分支拆为：
   - 移除后为空：删除原队与 membership，返回原 generation 的 clear；
   - 仍有幸存者：先捕获并退休旧队，再分配新 party id/generation，以最低旧槽幸存者为 leader/slot0，
     其余幸存者按旧槽排序重建；原子替换所有 membership，返回 retirement + 新 snapshot。
   复用一个受锁 helper，避免显式离队与 session cleanup 分叉。清理与旧会话判断仍在同一锁内。
5. 检查 pending invitation：任何绑定退休 generation 的邀请不得在 replacement 后成功；若现有 generation
   校验已经保证，则用测试固定而不增加重复清理；若会造成悬挂状态，则在同一原子 mutation 中只移除
   精确绑定旧 generation 的邀请。
6. 运行 Required checks，检查实际 diff 只在 owned boundary；把 Changed / Why / Checks / Remaining
   追加到本文件。输出给 TASK-03 的依赖必须明确列出 retirement/new-snapshot 形状和需要的投影顺序，
   但本单元不实现或声称客户端通知已修复。

## Result — Round 1

### Changed

- `PartyModeRuleService` 的 Normal 默认 target 改为 `0`，并对 Normal 非零 target 返回独立的
  `InvalidPartyModeTarget`；既有 normalization 仍只清 canonical `[1]`。
- `PartyNotificationPlan` 增加协议无关的 `PartyRetirement`：旧 party id、generation、递增后的 revision 与
  精确 audience；既有普通成员离队 clear 字段保持不变。
- 多人队长离队/清理现在原子退休旧队，以最低旧槽幸存者创建新 party id/generation、继任 slot0 队长，
  其余幸存者按旧槽顺序紧凑排列；单人最后离队仍只清除并删除原队。
- 测试固定了 Normal 合法/非法 target、无污染拒绝、设置与标题及 endpoint 保持、两名幸存者的槽位重建、
  旧 generation 邀请拒绝、显式离队/cleanup 一致性和旧会话/幂等重放边界。

### Why

TASK-01 的两份真实创建帧都给出 Normal target `0`，所以 `0xFFFF` 是现行服务端错误默认，不是等价
sentinel。当前 A21 对多人队长离队只发送同一个空 body `0x000D`；正确差异属于领域生命周期，而不是另一个
客户端命令。

### Checks

- Application Party + SelectedSession cleanup 聚焦测试：28 Passed / 0 Failed。
- 完整 `Dfo.Server.Application.Tests`：1264 Passed / 0 Failed / 116 Skipped。
- 仓库没有独立 `Dfo.Server.Data.Tests` 项目；新增 Data 结果模型已随 Application 完整构建和用例覆盖。
- `git diff --check`：Passed；实际代码 diff 限于本 Task owned boundary。

### Remaining

- TASK-03 必须先按 `Retirement` 的 old id/generation/revision 向其 audience 投影旧 stream clear，再向新
  snapshot 当前成员投影完整新 generation；两阶段都继续使用既有 delivery ordering/stale guard。
- TASK-03 还必须交换 C2S `0x000C` 前两字节为 canonical 顺序，并让 `PartyInformationBlockEncoder` 在
  canonical titleIndex `0` 时编码 `PartyTitle` raw-dstr；本 Task 未修改或声称协议通知已修复。

## Result Correction — Round 2

Round 1 的 `PartyRetirement` 与 replacement party 是依据旧版上游作出的实现。TASK-04 的当前 A21 UI
证据推翻该兼容假设，已移除这两项增量并恢复“队长离队整队解散”。Normal target/default、规则拒绝和
无污染状态测试继续有效。
