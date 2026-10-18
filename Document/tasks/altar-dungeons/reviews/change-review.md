---
kind: rks-review
verdict: approved
---

# 无尽与极限祭坛独立结果审查

## 目标与范围

- 基线及当前 `HEAD`：`a375a148d7fe8a711a64f56959bd41067b4cf217`，分支 `task/next-round-20261013`；审查对象为该基线上的未提交完整实现、测试、`protocol/` 记录及 `Document/tasks/altar-dungeons/` 契约、规格、计划和研究记录。
- 依据：`TaskContract.md` revision 1、`spec.md`、`plan.md`、`research-and-implementation.md`，以及仓库 `AGENTS.md` 的分层、协议证据和实机验收约束；按代码完整结果审查核对生产调用路径、事务边界、调度、投影与回归保护。
- 重点覆盖每日次数和金币费用事务、多人加载与准备、D2 尾值及清房门控、15/7 轮生命周期、奖励至多一次、主动放弃/断线/整队退出、普通副本回归和 Application 不引用 Infrastructure 的架构边界。
- 审查者独立于实现者；只读检查产品代码和已有测试，未运行构建或测试，未修改产品代码。仅写入本审查记录。

## 判定依据

| 边界 | 核查结论 |
|---|---|
| 领域与内容所有权 | 无尽和极限分别持有自己的状态仓，门面只按 Run 的 Altar kind 路由；共享 Engine、规则、动态 Cell 和协议投影不形成跨玩法竞争状态。PVF 内容源、规则、领域、Protocol 分层明确，Application 未新增对 Infrastructure 的引用。 |
| 入场、计次和费用 | 开放时间、等级、每日次数和各成员金币在建 Run 前预检；`DungeonAdmissionOptions` 将每名成员费用与 Altar quota 放进同一多账户事务，失败和回滚不扣金币、不计次。当前 PVF 没有有效固定时段，因此默认全天是明确的采用策略，可选 `OpeningWindows` 使用 UTC+8 半开区间并在非法配置时启动失败；文档没有将其写成 A21 原生事实。 |
| 多人加载和房间切换 | 只有当前队长的 `0102` 能提出准备。队长已加载而队员未加载时只写 `PreparationRequested` 和新 deadline，不发 D7/D2；到期检查在当前 Party/ActiveParticipants 全员 ready 后启动，无需队长重复请求。Run、RoomVersion、Generation 和当前 Session 均参与旧请求拒绝。 |
| 波次与清房门控 | 动态 Actor 提交同时校验 Run、RoomVersion、Cell revision；序号跨房间单调。未来波次、活怪、难度选择或未完成房间保持 `MechanismBlocksClear`，普通图缺省为 false。D2 尾值按 A21 ClientStatic 的布尔清房许可编码：非本房末批为 1，仅本房最后一轮最后一批为 0；服务端仍要求待刷波次为空且活怪清零。最终地图才提交清图事实和完成事件。 |
| 轮次、难度与失败 | 真实内容回归覆盖无尽 15 轮、极限 7 轮、手选和超时默认难度；极限首尾固定难度，中间选择校验 leader、room、generation 和 deadline。死亡批次去重并复用现有死亡/掉落事务；祭坛不触发普通 Boss 自动抑制或普通通关奖励。 |
| 结算和 exactly-once | 结算先固定奖励，再在同一事务内应用经验、金币、InventoryThenMail 物品并提交 Altar `SettledParticipants`/`Awarded` 状态；回滚保留可重试资格，提交后重放不再发奖。整队退出在实例 projection gate 和稳定顺序的账户 transition gates 下重校验后先结算再移除 Run。 |
| 主动放弃与断线 | `CMD002A` 在移除参与者前调用 `SettleParticipantBeforeExit`：多人只结算离开者，剩余当前 Party 成员继续；单人先失败再发已完成轮次收益。断线策略已在规格中收敛为放弃未结算收益、已入账不回退，不承诺离线补偿或续打；Run 移除后 Pending/Advance 找不到旧实例，旧 deadline 不再刷怪或发奖。生产 `DungeonRunAggregate.Participants` 由当前 Party membership 与 ActiveParticipants 求交并重算 leader，离开成员不会永久阻塞 ready。 |
| 协议与证据等级 | 0102、00FF、013D 及 D0/D1/D2/D3/D4/D7/0129 的实现和字段向量均有测试；D3 使用 u32 mapId 的 15B 布局，013D 有 CMD 响应，未发送错误的 0104 退出包。`catalog.md` 和各协议人工区明确标为 ClientStatic / CurrentImplementation，保留无 WireCaptured / ClientVerified 的边界。 |

## 已解决发现

1. 初版主动 `CMD002A` 直接移除 Run，没有祭坛失败/部分收益结算。当前处理器在同一实例与账户门下先发事务化 `SettleParticipantBeforeExit`，再 `EndAsync`；应用测试覆盖已完成轮次、回滚后重试及只发一次，协议测试断言真实 002A handler 在 End 前发出该命令且重放不再结算。
2. 初版队长 `0102` 可在其他参与者仍加载时立即开始并投影 D7。当前以 `PreparationRequested` 进入 250ms 到期检查，只有当前参与者全部 ready 后才开始；反例测试确认等待期间没有 RoundStarted 或 Spawns。
3. 审查中曾担心成员离开后冻结参与者会永久阻塞 ready。复核生产 `DungeonRunAggregate.Participants` getter 后撤回：有 PartyService 时它按 ActiveParticipants 过滤当前 membership，并按当前队长重建参与者。新增真实 PartyService 的主动离开及断线续跑测试覆盖该边界。
4. D2 尾字段一度沿用上游“生成间隔毫秒”解释。A21 客户端静态路径证明其零/非零控制清房许可；当前编码、测试、研究记录、catalog 和协议人工区已统一为本房末批 0、其余 1。

## 验证证据与限制

- 执行方最终报告：Application 3750 通过、0 失败、0 跳过；Protocol 1484 通过；Infrastructure 876 通过、2 跳过（均非 Altar）；Persistence 468 通过。Altar 专项 16 项包含真实 15/7 轮、费用/回滚/三次持久化与日切、全员 ready、主动放弃/断线后的真实 Party 续跑、部分收益回滚及 exactly-once；真实 002A handler 路由也包含在 Protocol 通过集。审查者未重跑这些命令。
- 没有未解决的实质性缺陷。`Tool/WireSmoke/__pycache__/` 是无关未跟踪缓存，不应进入本次提交。
- 批准范围是当前源码和自动验证结果。默认全天开放、D1 操作按钮、完整消息时序、两种祭坛的原生 UI、多人高延迟及奖励展示尚未经过当前 A21 原生客户端验收，不能提升为 WireCaptured 或 ClientVerified。
- SC-5 的发布包身份、数据库和配置备份、监听/健康核对、回滚输入及实机测试结果由主执行者在部署阶段完成；这些部署工件不在本次代码批准范围内。
