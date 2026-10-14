---
kind: rks-review
verdict: approved
---
# 独立完整结果审查

- 目标：基准 `d5632d90d209b7a63627c1841bd903dfb6288804` 至当前工作树的在线到期清理变更；包括未跟踪的新源码、测试和文档。审查轮次：1，包含完整回归暴露的通知依赖与命令登记修正。
- 依据：TaskContract revision 1、spec.md、sdd-lite focused-delivery；应用 reviewing-work 的 Code 与 Deliverable 标准。
- 独立性：审查者未实施生产变更、未运行服务、未修改数据库，未并发执行构建/重型测试。

## 结论与证据

未发现需要阻断交付的具体缺陷。

1. `OnlineExpiredItemCleanupRegistration` 注册既有 `AddScheduledTaskFactory`，时间通过 `IDailyResetRuleService.PeriodStart` 计算，使用现有默认 Skip/有限重试策略；没有新增专用线程或 timer，也没有离线数据库枚举。
2. 批次从 `ISelectedSessionSnapshotQuery.Snapshot` 取候选，再经 `ISelectedSessionCommand` 生命周期保护、账号独占及事务管线执行。领域入口同时核对账号/连接精确会话、published 状态和认证在线状态。换代的候选被拒绝或跳过，逐人异常被记录后继续其余人，剩余失败交调度器重试。
3. 登录和在线入口实际调用同一个 `ExpiredCharacterItemCleanupDomainService.CleanupAsync`。本次只增加变化容器回执，原有期限判断、所有权检查、Take/Delete 及容器范围未复制或改写；无变化不发新增事件。持久化 Take 已通过 `InventoryContentsEventDomainService` 在同一事务内记录物品变化，新清理事件同事务入队，回滚不会发客户端投影。
4. `ExpiredItemsNotificationSubscriber` 在业务提交后消费 LiveOnly 事件，保留连接 generation 检查。通知组合读取当前容器状态，避免用过期删除回执覆盖同槽后来物品；穿戴变化通过既有当前角色视图得到统一外观，并更新本人和可见玩家。宠物运行态调用现有 `AdvanceAsync`，该路径参加当前事务。
5. 抽出的 `CharacterVisibleSessionDomainService` 与原有 Recipients 分支逐项核对一致，设置刷新和到期刷新共用该结果。新命令 handler 只依赖领域服务；通知 handler 修正后也只注入领域服务。可信后台命令名单精确纳入新增命令，没有放宽整体架构断言。
6. 新增测试内容覆盖实际注册跨六点、离线排除、排队发布状态变化、换代、回滚、重复执行、失败隔离、当前槽位重用和穿戴可见玩家投影。现有登录/SQLite测试仅适配回执类型，原断言保留。Document 与当前源码一致，并明确区分已实机验证的登录清理和尚待观察的在线跨六点行为。

## 验证边界

本结论是当前源码与文档的独立结果审查，不代替主代理的最新完整回归、部署身份核验或真实客户端跨六点验收。主代理早期报告的 focused 结果仅作证据线索；完整回归中的架构问题修正后需重新验证。在线通知沿既有 LiveOnly/零额外重试语义，断线或进程重启后的持久状态由登录快照恢复；文档没有承诺通知恰好一次。