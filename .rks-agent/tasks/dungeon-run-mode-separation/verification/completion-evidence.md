---
kind: rks-verification
decision: pass
---

# Completion Evidence: 普通与剧情地下城运行模式分离

## Claim

当前实现、自动化验证和 A21 实机验证共同满足 SC-1 至 SC-8。普通地下城与剧情地下城的入口、运行目的、目标推进和结算已经分流；任务奖励按角色职业投影并在同一事务中完成。既有误发装备按任务边界保持不动。

## Automated Evidence

- Release 解决方案构建：通过，0 warning、0 error。
- 带真实 A21 PVF 的完整 Release 测试：通过 3194，失败 0，跳过 2；另在清除 `PVF_ARCHIVE_PATH` 后执行缺档案负例 2/2 通过，因此两组互斥环境下适用测试合计 3196 通过。
- Application 测试包含分层约束，完整运行通过 1442/1442；生产 Command Handler 领域形状限制与 Application 不引用 Infrastructure 均未回归。
- 真实 PVF 奖励验证：任务 1776 在 job=0、growType=0 时只投影 `101010437 x1` 和对应金币；任务 130 验证只有 `[gold multiple]`、没有 item-zero 标记时仍正确计算金币。
- 奖励原子性验证：真实 `CommandBus`、同一 SQLite 事务内依次写入任务、物品、金币和经验，在经验写入后的故障点确认四类变更均回滚；协议层只返回 code 22，不泄漏部分成功。
- 怪物/AI 与混合目标验证：type 1/2 使用各自目标通道，AI 不获得怪物经验/掉落；目标选择后再执行权威校验，非法类型、缺失通道、跨权威声明均关闭失败。
- `git diff --check`：通过。
- 独立变更审查已完成六轮并最终 approved；Round 5 的金币倍率和真实晚失败原子证据 finding 已关闭，Round 6 未发现新的代码层 material finding。

## Live A21 Evidence

- 普通入口产生 `Interactive/Ordinary` 运行并选择普通 maze（`questMaze=0`）。
- 任务 1776 剧情入口产生 `Directed/QuestLinked` 运行，冻结 quest 1776 并选择关联 maze（`questMaze=1`）。
- 剧情运行中匹配目标由 1 推进到 0；Boss 房退出产生剧情结算，任务完成成功且后续任务 1777 可接受。
- 用户已分别验证完成退出、手动退出、回城和再次进入，不再出现“客户端在副本、服务端无运行”的分裂状态。

## Success Conditions

- SC-1：显式普通入口在持有任务 1776 时仍选择 Ordinary maze，不建立任务关联运行；自动化与实机均通过。
- SC-2：显式剧情入口校验任务/难度并把关联任务和运行目的冻结到 QuestLinked 运行；自动化与实机均通过。
- SC-3：Ordinary 与 QuestLinked 使用不同完成/结算规则，`questMaze` 不再硬编码；普通路线回归通过。
- SC-4：普通怪物与 AI 目标由各自可信死亡通道推进，任务 1776 的绑定运行、任务、副本、难度和通道约束均通过正反例与实机验证。
- SC-5：两类运行的 GiveUp 都结束服务端运行并复用公共回城投影；手动退出实机通过。
- SC-6：入口差异保持在领域决策和薄协议适配层，未复制房间、怪物或掉落运行机制；构建、分层测试和独立审查通过。
- SC-7：A21 普通/剧情双路径、剧情任务完成、完成退出、手动退出、回城与再次进入已实机确认。
- SC-8：奖励按 job/growType 投影，真正的选择奖励严格校验；任务 1776 只发 `101010437 x1`，金币/经验/物品/任务状态具备失败原子性。

## Residual Evidence Boundaries

- AI type 2 目标和真正的多选一奖励尚无本轮 A21 实机样本；当前结论来自 PVF 结构、生产路径测试和拒绝反例，不提升为 ClientVerified。
- 本次不删除、不回收此前已误发给角色的装备；只修正后续任务结算。
