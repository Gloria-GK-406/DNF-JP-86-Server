# TASK-005 — 集成回归、残留核对与实机交接

## Task Unit Contract

- Revision: 1.
- Outcome: 对完整改动进行跨边界审计与新鲜测试，明确 A21 双客户端验收步骤和不能离线证明的客户端效果。无部署授权前不变更 main Host；不将静态协议测试冒充实机确认。
- Owned boundary: 全部任务改动及任务契约结果记录、回归测试和必要小范围修复；不做主干合并、部署或工作区清理。
- Checks: 全方案 Release build/test，PVF 真实路径；PartyId 领域残留、PartyKey 反查、旧连接投递、运行中离队和新 Cell、掉落归属；完整审查报告，A21 手工测试步骤。
- Depends on: TASK-001–004。

## Detailed Plan

1. 从 RQ/AC 逐项对照代码与测试，检查全部 `PartyId` 使用点以及 Run 的成员资格、运行进度、投递资格三者没有互当真源。检查全部掉落落地/授予入口与实机证据边界。
2. 使用已有 `Script.pvf` 的显式环境变量执行全解决方案构建和测试；对任何非本任务失败按系统调试流程分类，必要时最小修复重测。
3. 独立审查完整 diff 与任务文档，修复 material finding 并复审。记录不阻断本次可测版本的容量/客户端行为残余风险。
4. 准备双客户端验收：城镇两人队长退出提示及旧清新建；进地下城队长退出后旧 RunId 延续、新 Cell 只含继任者；地面掉落及直接任务物；单人虚拟队；旧连接和非入场新队员不收通知。实机未运行则列为待验，不声称 ClientVerified。

## Result

- State: 自动化与交接材料完成；完整差异的独立审查已批准。A21 双客户端实机待部署授权与运行，不声称客户端验收完成。
- Release 全解决方案测试在显式 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol2\Script.pvf` 下新鲜通过：Pvf 60、Launcher 63、Networking 65、Infrastructure 314（另 2 skip）、Protocol 941、Application 1508、Persistence 353、LauncherApi 50、Host 17。Release 构建 0 警告、0 错误；`git diff --check` 通过。此前未设置 PVF 路径时 43 个测试因 `Script.pvf` 缺失失败，已归类为测试环境问题，并非代码断言失败。
- PartyService 领域内部不再用客户端 PartyId 定位队伍；Run 保留每角色物理进入/进度索引，但成员关系以 PartyService 为真源。Run 使用 PartyKey，客户端 ID 换代通过协议投影处理；`git diff --check` 已通过。
- 实机步骤见 `Document/PartyKey-Dungeon-Membership-A21-Acceptance.md`。客户端“是否保留队伍”提示、旧队伍投影的视觉效果及双端连通性不能由协议测试替代。
- 残余风险：协议层客户端队伍 ID 注册表目前只增不回收，长时间运行可能耗尽 ushort 空间，达到容量时明确抛错；需后续设计带安全回收/代际隔离的策略。该风险不应被解释为领域 PartyKey 的寿命限制。
- 独立审查发现并已修复队长离队时城镇移动补发抢占旧清新建的竞态；交错回归修前失败、修后通过。审查结论见 `reviews/change-review.md`。
- 本任务契约明确不含合并、清理或运行中 Host 部署。以上只代表隔离工作区，主干及当前 Host 未变。
