---
kind: rks-review
verdict: approved
---

# TASK-39：P8 共享方案独立审核

**批准 P8 ready R1 的受影响 Plan 增量，无阻塞发现。此结论不是 TASK-39 Result 通过。** 审核对象为 `plan.md` 新增 P8 引言、owner／Participants 分离节及修订说明，关联 TASK-39 revision 2；基点 `f04a5b1b`。依据 `process-reset-2026-10-25.md` §3～4、Spec B6 及现行 VC 表、RQ-004～009／AC-002～005，使用 `reviewing-work` 的 Plan 判据；未生成挑战，也未审未完成产品的整体结果。

1. **阻塞真实，属于共享 HOW 缺口。** `CommandWorkUnits.cs:250–256` 和 `ExplicitNonRequestFootprint` 将普通无账本写限制为唯一入站 actor；旧 `EffectOperationContext.CaptureLegacy:133–188` 的 ActiveNonEffect／无 frame 分支不作该限制，但 `LegacyEffectFootprint` 仍守 invocation、job、授权与 owner 自洽。正式 `PartyTowerFootprintBoundaryTests` 的两塔 Leave／Remove 四例经薄 handler、生产 bus／runner 命中此门且 run 未变，再对同 run 执行旧生产 Leave 成功。已读取 `verification/logs/task39-footprint-boundary.log`：4/4；这是缺口证据，不是新模式实现验证。
2. **owner 与保护范围确实不同。** 旧 Boss `LeaveBossTowerCommand` 的 Sessions 来自原 run 全员，selected pipeline 另要求报告会话在集合中；Illusion Leave 是 plain ICommand，没有该保护集合。两塔 `RewardingMutation` 既要求 departing owner，也会结算剩余有效成员。`BossTowerRun.Leave:138`／`IllusionTowerDgn.Leave:129` 只移除成员，不新增身份，因此从变更前 run 冻结全员足以覆盖本 Task。P8 保留 Boss 全参与者检查与 Illusion 空 selected，并保留领域 current／run 门，符合既有保护和事务点要求；不能将 Cera 的特殊保护扩至这里。
3. **许可边界足够且方案最小。** 独立模式只在 ProtocolNonEffect ordinary writable action 签发，显式绑定 request／job／transaction，关闭后与失败后拒绝，所有守卫 sticky；不改旧 NonRequest，不给 Main、Internal、ReadOnly、raw 或恢复资格。无需改根／子契约或 RQ／AC，也不新增 VC；后续若实际实现产生额外可见差异，仍须按流程另列证据，不能由本批准覆盖。

**实现及 Result 的核验重点（既有 P8 义务，不是已通过结论）：** runner 签发时独立复制并冻结 owner 集合，不能持有调用方可变列表；实际两塔测试覆盖踢出非入站成员以及离队触发剩余成员结算。反例应直接证明未声明／替换身份、账号角色错配、跨请求／job、action 后续用被拒，尤其捕获 guard 后不能再持久写或成功结束。失败测试分别直接核 Boss 原全员保护、Illusion 空 selected、精确来源关闭与替换隔离，不用 owner 数量代替保护证明。最后执行既有 Cera／Main／WriteResult／self-settlement 回归、新鲜 Release build 和八项目全量；保留全部原强断言。主控消费并固定规划检查点后方可实施机制增量。
