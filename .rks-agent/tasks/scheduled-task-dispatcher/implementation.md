# Implementation Evidence

基准：1a0d8f9e；工作分支：task/next-round-20261013。未部署、未重启。

## Delivered Surface

- Abstractions/Scheduling：时间计划、稳定注册、策略、回调上下文与并发配置。
- Application/Scheduling：每日固定偏移/间隔锚点计划，DI 注册，单一时间优先队列与完成队列，共用受限执行器。
- ApplicationModule：只接入空调度器，生产无真实任务注册。
- 测试：可推进 TimeProvider、真实 DI scope/HostedService 回调；使用信号判断执行结果，实时时间仅作挂死保护。
- Document/ScheduledTasks-20261014.md：语义、使用方式和下一阶段清理边界。

## TDD and Corrections

1. 首个真实注册回调测试在空实现上运行超时（Startup_catchup_executes_the_registered_callback_once，exit 1），建立行为 RED；实现等待/执行后同一测试通过。
2. 构建期出现的目标类型推导重载歧义、xUnit 分析器、值类型 DI 测试配置问题均已修正；这些不是行为 RED。
3. 扩展行为测试初版的 Tick 辅助函数无条件等待新 timer，在推进不足下次到期时间时误报超时。按可控时钟实际触发 timer 的数量决定是否等待唤醒后，全部专项通过；没有据此改变生产调度语义。
4. 发现默认 DI 注册先于显式 options 时可能忽略显式配置，已让显式配置覆盖默认、后续默认不覆盖显式；有实际解析回调回归。

最终正式结果以 verification/completion-evidence.md 为准。完整独立审查见 reviews/change-review.md。

## Structural Boundary

调度器没有账号、库存、事务、EventBus 或 NOTI 业务依赖。没有改动登录清理、有效期生成、数据库或旧后台服务。后续新增业务回调才能调用领域能力，且定时清理只针对在线角色；离线登录与在线定时应复用同一清理逻辑。
