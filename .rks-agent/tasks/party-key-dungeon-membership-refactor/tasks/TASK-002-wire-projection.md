# TASK-002 — 客户端队伍代际投影

## Task Unit Contract

- Revision: 1.
- Outcome: 领域队伍保持 PartyKey，客户端 `ushort PartyId` 独立生成；队长退出但有剩余成员时旧客户端队伍先清、继任队长的新编号和全员槽位再建立。单人队伍保留给客户端显示是否继续组队。
- Owned boundary: PartyService 的客户端 ID 状态与通知快照、PartyNotificationSlice/PartyTownProjectionDelivery 的有序发送、相关协议和应用测试；本单元不迁移地下城 Run/Cell。
- Required checks: 城镇双人/三人换届字节序列和通知对象快照断言；旧编号不复用冲突；普通加入/离队与单人虚拟队回归；解决方案编译。
- Depends on: TASK-001.

## Detailed Plan

1. 从现有 `PartyId`/`Generation`/`Revision` 和通知路径重建各自语义与并发门。确认 A21 旧队伍清除 type=3、新队伍完整建立 type=0 的真实编码和上游证据，测试先冻结目标收件人、顺序、编号变化。
2. 给同一个稳定 `PartyState.Key` 提供可更替的客户端代际及 ID。队长换届生成新 ID，领域成员、设置、目录和 Key 原位保留；旧 ID 在通知计划内作为不可变快照，以免稍后又查到新值。保证新编号分配不与并存队伍冲突。
3. 投影边界在单个参与者顺序门中对旧成员和当前成员发清除/重建；按受众区别离队者、继任者、其他队友和区域观察者，不先发旧 ID 上的 leadership slot swap 让客户端抹空自身队伍。
4. 按分层规则审查 `PartyId` 在领域/协议之间的残留。T-003 前允许过渡字段在 PartyService 的快照构建处保留，但不再作为领域查找/副本身份；完成时记录剩余迁移点。
5. 跑 targeted protocol、party application 回归和完整构建；把任何 A21 运行未验证的效果标成待实机，不用编码断言代替客户端判定。

## Result

- State: code complete, 2026-09-17; A21 客户端行为留在最终实机验收。领域 `PartyService` 不再分配、存放或决策 `ushort PartyId`，只维护稳定 Key 和队长版本；协议层 `PartyClientIdRegistry` 分配编号，`PartyClientProjectionService` 投影查询/通知。
- 换队长（离队或显式移交）保留 Key 与队伍设置，增加领导版本；通知计划同时携带旧版本、清除受众和新快照。协议发送旧 ID 清除后发送新 ID 全量成员，区域观察者也先清后建。
- Evidence: `PartyClientProjectionServiceTests` 两种换届通过；`PartyNotificationTests` 的旧清新建顺序测试通过；协议项目 939 项通过、Host 17 项通过、解决方案构建 0 错误。
- Remaining risk for T-005: 编号注册表对历史 `(Key, LeadershipVersion)` 暂不回收，避免延迟通知误用已再分配的短编号；长期开服达到 `ushort` 空间上限时会显式失败，需独立设计安全回收策略。当前未进行 A21 双客户端确认。
