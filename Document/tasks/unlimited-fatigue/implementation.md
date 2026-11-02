# 无限疲劳值活动（#230）

适用：服务器活动启停扩展、疲劳免扣、部署与 A21 验收。

## 范围

用户授权：按已讨论方案在新的 worktree 实现；竞技地下城也免扣，GM 不提供独立开关。
交付边界：用户随后明确本轮只完成实现，暂不准备实机环境；不迁移玩家库、不切换服务、不创建功能 PR。
基线为 `origin/main@1bc7d38b7`；分支 `task/230-unlimited-fatigue-20261030`。功能分支不修改产品版本号。

无限疲劳活动身份为 `1`（70 `CEventManager` 槽位 1 是 `CUnlimitFatigueEvent`，70Reference），类型 `unlimited-fatigue`，以带详情的 EVENT_INFO 显示在客户端活动页。A21 上编号 1 的其他含义未取证，以实机显示为准。
活动开启时，普通地下城入场、首次换房及竞技地下城每轮均不扣疲劳；零余额可通过疲劳准入。
活动结束不补满、不追扣；其他准入条件、材料、金币、次数和奖励保持原有规则。
疲劳药、退款、黑钻及业务日重置仍按真实账本执行。免扣不会增加当日累计消耗。

## 所有权与事务

- `ManagedEventLifecycleDomainService` 统一扫描注册活动。按活动自身判定取得目标状态，并比较配置修订号与已应用状态。
- `ManagedEventActivationChanged` 是目标状态事实。同步订阅在当前可写事务执行；应用记录和异步通知记录同事务提交。
- `UnlimitedFatigueActivationSubscriber` 是免扣设置的唯一生产控制方；疲劳服务拥有设置和行为，不反向查询活动框架。
- `CharacterFatigueDomainService.SpendAsync` 在免扣时不写角色疲劳行，真实余额与累计值保持不变。
- 准入准备与最终消费两处都通过 `CanEnterDungeonAsync` 判断，最终消费重新读取当前设置。
- 注册表启动校验拒绝重复活动身份和重复运行时订阅；架构测试限制免扣控制接口的生产消费方。

GM 修改通过 `EventManagementDomainService.ChangeAsync` 同事务对账；关键订阅失败连同配置修改回滚。
分钟任务按活动分别开工作单元，失败活动回滚，健康完成的其他活动保留，下一轮重试。
时间到点通常约一分钟内应用，另加执行器排队时间；失败不承诺一分钟内恢复，保留失败日志和页面同步差异。
活动进行中改配置会以新修订号重新应用。启停可能使用相同配置修订号，去重键必须包含 Active。

`InitializeManagedEventsCommand` 在 `GameServerStartupService.StartingAsync` 中被等待，先于所有监听开放，
且不放进“存在 PVF 才执行”的分支。`RunOnce` 只作为调度兜底，不能替代启动屏障。
停机跨过整个活动期只恢复当前目标，不补发历史开启/关闭事件；一次性奖励不属于此机制。

## 数据兼容

结构版本从 2 升至 3：新增 `event_activation_state` 与 `fatigue_consumption_settings`。
旧库必须停服后经 `DbMigrate` 升级。新库与迁移后结构由形状测试对照，迁移不更改角色疲劳数据。
未存储设置时为正常扣除；启动对账应用当前活动状态。回到旧程序需使用迁移前备份，不直接复用 v3 库。

## 证据与限制

| 来源 | 依据 | 可支持的结论 |
|---|---|---|
| CurrentImplementation | 本任务的 Application、Persistence、GameServer 与 GM 前端源码 | 生命周期、持久化、事务、准入与扣除逻辑 |
| ClientStatic / ClientVerified | [FATIGUE 协议页](../../../protocol/game/server-to-client/NOTI-0x0024.md) | 已有疲劳字段与有限的显示零疲劳入场样本；不等于本活动实机通过 |
| 70Reference | [既有疲劳研究](../../outputs/fatigue-system-20260929/implementation.md#70reference与证据边界) | 已用与累计消耗分离、退款不倒扣累计；沿用既有取证，本任务未重新反汇编 |
| 上游参考 | `E:/Project/DNF/ServerS4A21@04e04b4` 的疲劳持久化与竞技入口 | 版本对照；上游规则不能证明 A21 无限疲劳活动行为 |
| Unknown | 编号 1 在 A21 上除活动页列表外的含义、真实零余额下所有选图/重挑战分支 | 不伪造余额，按用户验收结果决定是否需后续客户端处理 |

本功能没有新增已验证协议结论，不提升 catalog 的实机证据等级。客户端验收结果写入对应协议人工区。

## 用户测试

按 [human-test.md](human-test.md) 编号执行。自动测试不替代 A21 验收；用户通过前不创建功能 PR。
测试使用当前主干实际玩家库前，核实当时进程、配置和数据库绝对路径，完成一致备份与副本迁移演练。
不能根据历史路径直接认定当前玩家库，不能让两个 Host 同时写同一库。
