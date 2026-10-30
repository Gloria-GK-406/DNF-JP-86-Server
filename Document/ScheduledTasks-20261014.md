# 注册式定时任务调度器

日期：2026-10-14。证据级别：CurrentImplementation / 自动化回调测试；不涉及客户端协议变化。

## 职责与使用

`ApplicationModule` 注册一个共用调度器。模块可以在同一 DI 容器内添加稳定唯一 ID、时间计划和异步回调；实际业务放在回调调用的领域服务中。调度器负责等待到期、受限执行、异常重试和停止，不读取角色、不删除物品、不启动业务事务、不发送 NOTI。

它只承接固定时间表的系统任务。一局地下城之内的到点推进不用它，见 [DungeonRunKinds.md](DungeonRunKinds.md#一局之内的到点推进)。

每日计划使用明确的固定 UTC 偏移，不读取机器本地时区。间隔计划默认锚定 UnixEpoch，也可以指定锚点；按时间轴计算到期，不按上次回调结束时间漂移。

下面是**未来接入示意**，本次没有将它注册到生产代码：

```csharp
services.AddScheduledTask(new ScheduledTaskRegistration(
    "inventory.expiration.online",
    new DailyTaskSchedule(new TimeOnly(6, 0), TimeSpan.FromHours(8)),
    async (context, ct) =>
    {
        var cleanup = context.Services.GetRequiredService<IOnlineExpirationCleanup>();
        await cleanup.ExecuteAsync(ct);
    })
{
    MissedRun = ScheduledTaskMissedRunPolicy.RunOnce,
    MaximumAttempts = 3,
    RetryDelay = TimeSpan.FromSeconds(5)
});
```

示意中的 `IOnlineExpirationCleanup` 不是已实现接口。正式清理时间必须来自现有每日重置规则，不另设硬编码六点真源。可用 `AddScheduledTaskFactory` 从根容器读取 singleton 配置/规则再构建注册；根容器不能捕获 scoped 业务服务。回调只通过 `context.Services` 解析本次尝试的业务服务，不能把该 scope 或其服务保存到回调外。

`AddTaskScheduling(new ScheduledTaskSchedulerOptions(MaximumConcurrency: 4))` 设置所有任务共享的执行并发上限。重复默认注册不会覆盖显式选项；重复显式配置以后者为准。ID、计划及执行策略在启动解析时验证，重复 ID 拒绝启动。注册为启动时静态快照，不支持运行时新增/修改。

## 到期、失败和停止语义

| 情况 | Skip（默认） | RunOnce |
|---|---|---|
| 服务启动 | 等待严格未来一次 | 执行最近已到期一次，再等待未来 |
| 等待循环跨过多个周期 | 执行最近到期一次 | 执行最近到期一次 |
| 同任务上次仍在执行/重试 | 丢弃新的到期触发 | 合并为最新一次，上一轮结束后补一次 |
| 尚未获得全局执行名额 | 保留原等待实例，丢弃重叠触发 | 更新该等待实例的到期时间，不积压另一轮 |

`Skip` 指跳过启动历史与忙碌期间重叠触发，并不要求后台线程在到期瞬间得到 CPU；普通唤醒延迟仍会执行最近一次。每个任务最多一个执行/等待实例，加一个合并补执行标记。不同任务在上限内可并行。回调同步前缀经线程池隔离，不阻塞唯一计时循环；没有每任务常驻专属线程。无注册时立即退出，不创建计时器。等待最长一分钟重新读取墙上时间，以识别系统时钟调整。

失败后最多尝试 `MaximumAttempts` 次（1～10），每次都创建新 DI scope，释放后才重试。重试等待不占全局执行名额。失败耗尽记录 error 并结束本轮，后续正常周期仍会执行。自定义计划在运行中失效时只禁用该任务并记录 error；启动即失效则拒绝启动。

服务停止会取消等待、重试和回调 token，并等待正在执行的回调释放 scope。回调必须响应取消；调度器不强制放弃仍在运行的回调，也不在其尚未结束时另起并发重试。

本机制不持久化执行记录。`RunOnce` 每次重启都可能重复执行最近一次，系统时钟回拨也不提供跨历史周期去重保证。业务回调必须幂等；不承诺跨进程恰好一次或事件不丢的持久队列语义。

## 已接入业务

每日在线物品清理已注册，见[在线角色每日到期清理](OnlineExpiredItemCleanup-20261014.md)。登录和定时使用同一删除服务，离线角色仅登录时处理。现有次数后台服务保持原路径。

## 验证入口

`Server/tests/Dfo.Server.Application.Tests/Scheduling/` 通过可推进 TimeProvider 和真实 HostedService/DI scope 验证回调行为，包括每日边界、间隔锚点、跳跃合并、慢任务、并发、重试、scope、空注册、取消与非法注册。任务契约和审查证据见 `.rks-agent/tasks/scheduled-task-dispatcher/`。
