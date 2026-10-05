# T-002 后端实施结果

基准：`b4bf8d0d01113c39962c885b44002453dcb740c6`，工作区 `warehouse-storage`。经验重构未改；全部变更未提交，未部署，未访问真实玩家数据库或运行目录。

## Changed

- 保留恢复前的 `JoustModels.cs` / `JoustServices.cs` 形状并补快照与后台只读查询。新增 `JoustDomainService` / `JoustRuleService`，2365 同一个 singleton 同时实现管理和玩法接口。
- `PvfJoustDefinitionSource` 按 typed PVF section 读取 `event/chn_event/chn_joust.evt`、12 骑士/名称/攻型/Win/Loss、17 级、1000 限额、两个材料和两种奖励。活动只读显示不暴露内部授予快照。
- SQLite 统一 schema 新增 schedule revision、round 全快照、bet 稳定请求账本。设置不会用默认覆盖；已生成轮次的时间、内容、名单和完整 8→4→2→1 结果不会受修改或重启重抽。
- 下注同币跨槽，指定槽优先；指定槽有非法物品直接拒绝，不误扣另一个币。角色当前等级、个人限额、全池上限、封盘、余额均在扣料前校验。键重复不双扣，键复用但骑士/数量/槽位不同拒绝 `RequestConflict`。
- 一位小数倍率采用 `AwayFromZero`；参与奖励为个人总下注量，中奖数量再 floor。扣料/投注同事务；全部 prepared 邮件附件和 round settled 标记同事务。关闭/过期仍推进已生成轮次并结算，失败回滚后可重试。
- 后台每秒事务推进；未启用且无未结算轮次时，`NeedsAdvanceAsync` 只读检查跳过 writer。当前轮次用 SQL 时间索引查找，避免每秒装载 500 条历史。新设置 GET/POST、GM 全权限/CSRF/审计/revision冲突和 DI 已接线。

## Why

- 旧 2016 到期策略限定在本活动 PVF 声明的材料及奖励：奖励保存 `ExpireTime=0` 的完整 grant template 后经 `lifecycle.CreateAsync` + `mailbox.SendPreparedAsync` 投递，其他物品规则未改。材料只额外接受与自身归档日期完全相等的旧时间戳，任意新的过期实例仍拒绝。
- 轮次完整結果落盘；公开 State 在下注/封盘揭示 0 个结果，Quarter/Semi/Final/Result 累计揭示 4/6/7/7，供原生动画使用。只在 `Phase.Result` 开始时派奖，因此最终动画期间不会提前收奖。
- 全池上限 `int.MaxValue-1000`：单角色投注≤1000，倍率舍入最多增加 0.05×1000=50，给协议和奖励整数留裕量；扣料前超界拒绝。
- 自动判据保护真实余额、投注数、邮件数、物品领取和恢复结果，维护价值高于纯结构代理；本恢复阶段测试为 implementation 后新增证据，没有宣称 TDD RED。

## Checks

环境：PowerShell / .NET SDK 10.0.302，`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`；所有 .NET 命令串行，使用临时 SQLite。

1. `dotnet build Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj --no-restore --verbosity minimal`：成功，0 警告/0 错误。普通 sandbox 首次仅返回无诊断 exit1；require_escalated 后成功。随后 Host 测试也重新构建了当前 Host 依赖链。
2. `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~Joust|FullyQualifiedName~CommandHandlerArchitectureTests|FullyQualifiedName~SkillMaterialConsumptionTests|FullyQualifiedName~MailboxCoreIntegrationTests|FullyQualifiedName~CharacterExperience' --verbosity quiet`：**89/89 通过，0 skip**。其中新 Joust 10 项真实 PVF/SQLite或纯规则用例覆盖两币/同币跨槽、重放/冲突、非法材料、等级/限额/封盘、全池整数边界、扣料失败回滚、配置与结果快照、关闭/到期/重启、最终动画时间、派奖失败回滚与两种实际附件领取；其他为命令架构、库存消费、邮件及经验相关回归。日志：`backend-application-check.log`。
3. `dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --no-restore --filter 'FullyQualifiedName~GmJoustHttpTests|FullyQualifiedName~GmEventsHttpTests' --verbosity quiet`：**13/13 通过，0 skip**，新专属 API 和既有周签到（含真实 PVF）回归。日志：`backend-host-check.log`。首次新 fault fixture 在登录前启用审计故障，导致登录 500；定位到既有 Login 审计，改成登录后注入，当前 fresh run 全过。
4. 真实 PVF 探针确认上述 ID/限额；测试验证每骑士 Win/Loss 各 28 个整数、归档年份 2016。`git diff --check` 通过。没有遗留执行中 dotnet session。

## API / 下游接线

`GET /api/gm/v1/events/joust/settings`

`POST /api/gm/v1/events/joust/settings`，body：`{expectedRevision,schedule}`。

`schedule` 字段：`dailyStartTime`（严格 HH:mm）、`roundsPerDay`、`roundIntervalMinutes`、`bettingMinutes`、`lockMinutes`、`stageSeconds`。默认 `10:00/7/120/90/10/200`。验证总期数、不跨午夜、阶段总时间不超每期。

返回 `JoustSettings`：`eventId=2365`、`revision`、`schedule`、`sourcePath`、`available/unavailableReason`、`minimumLevel/maximumBetting`、12 个 `knights`、两 `currencies`、`reward`、`bettingReward`、`updatedAt/updatedBy`。物品只读 ID/名称/归档日期，内部 grant template 不通过设置 API 返回。

`IJoustDomainService.StateAsync/BetAsync/HistoryAsync/AdvanceAsync` 必须通过 `Transactional` 命令或明确的已有 ambient 域事务调用；这些读取允许補推进。`SettingsAsync/NeedsAdvanceAsync` 可无事务只读调用。`KnightIndex` 为 PVF ID 0..11，State 保留参赛顺序及八个骑士状态。

`AdvanceAsync` bool 当前表示发生结算；不是 phase change/round creation 通知标记。T-003 应周期比较公开 State/投影并做提交后的在线通知。协议稳定请求键来自会话/连接身份与帧序号，必须防止 16-bit 序号回绕把正常追加当重放；域端显式拒绝 payload collision。

## Remaining

T-003 原生切片、进场 EventPlayerSnapshot 可选 Joust 与在线通知，T-004 GM UI，由主控后续串行交付。主控还需完整 diff 审查/新鲜综合回归/本地最终提交。原生客户端动画、黑骑士显示、刷新与实际游戏窗口操作仍待用户实机验收，当前证据仅 CurrentImplementation/ClientStatic，不是 ClientVerified。

T-002 的后端实施和本阶段 required checks 已完成；交回唯一生产写入权。
