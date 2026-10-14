```yaml
kind: rks-review
verdict: approved
```

# 独立完整结果审查：注册式定时任务调度器

## 审查目标与边界

- 目标：分支 `task/next-round-20261013` 当前冻结工作树，基线与当前 `HEAD` 均为 `1a0d8f9e8f64f400aa250ffe88a0a64badfe3e35`。
- 实际输出：`Server/src/Dfo.Server.Abstractions/Scheduling/ScheduledTasks.cs`、`Server/src/Dfo.Server.Application/Scheduling/*`、`ApplicationModule.cs` 的两处接线、`Server/tests/Dfo.Server.Application.Tests/Scheduling/*`、`Document/ScheduledTasks-20261014.md`。新增文件尚未跟踪，本审查逐个读取了实际文件，未以 `git diff` 代替。
- 治理来源：`TaskContract.md` revision 1、`spec.md`、`plan.md`；审查标准为 `reviewing-work/references/code.md` 与 `deliverable.md`。
- 范围约束：只审查通用调度器；不要求实际清理接入、旧 timer 迁移、部署、重启或数据库变化。

## 结论

未发现阻断缺陷。实现满足 RQ-001～RQ-006，文档与代码行为一致，且没有把未来在线角色清理示意接入生产。此 `approved` 只通过当前完整结果审查门，不替代后续完成验证或本地接受提交。

## 决定性证据

| 要求 | 独立检查结果 |
|---|---|
| RQ-001 注册与校验 | `ScheduledTaskHostedService` 构造阶段按 Ordinal 检查唯一 ID，并验证空 ID、计划、回调、missed policy、尝试次数和重试延迟；`AddTaskScheduling` 用 `TryAddEnumerable` 保持单一 hosted service，显式 options 用 `Replace` 覆盖默认，后续默认注册不会反向覆盖。重复/非法注册测试通过真实容器解析触发，而非只测辅助函数。 |
| RQ-002 时间规则 | `DailyTaskSchedule` 用显式固定 UTC offset 计算最近已到与严格未来一次；`IntervalTaskSchedule` 用锚点和向负无穷取整计算周期。边界、正点、UTC+8、负/正锚点及非法规则均有测试，不依赖机器时区。 |
| RQ-003 不重叠与全局上限 | 每个 `Job` 只有一个 `Execution`；未取得全局名额时只更新 `Requested`，执行或重试中只保留一个 `Pending`。共享 `SemaphoreSlim` 只包围实际尝试，其他任务可在上限内推进。慢任务与全局上限测试在注册回调内部阻塞/计数并断言结果，实际观察了回调，不是只观察排队状态。 |
| RQ-004 补执行语义 | 到期时以当前 `LastOccurrence` 取得最新到期点，并立即把同一 job 排到严格未来的 `NextOccurrence`，因此时间跃迁不会枚举历史周期。`Skip` 丢弃忙碌重叠；`RunOnce` 在运行中覆盖为最新单个 pending，等待名额时更新原实例。启动、三日跃迁、慢回调及等待名额合并测试与文档表格一致。 |
| RQ-005 重试、scope、取消与隔离 | 每次尝试在取得全局 slot 后创建独立 `AsyncServiceScope`，scope 释放及 slot 释放后才按 `TimeProvider` 延迟重试；重试等待不占 slot。回调异常在本 job 内耗尽并记录，健康任务仍能完成；运行期非法 schedule 只禁用该 job。停止 token 传入等待、重试和回调，最终等待所有已启动执行结束。测试从 `context.Services` 解析 scoped probe、逐次确认前一 scope 已释放，并验证取消后 scope 释放。 |
| RQ-006 业务边界 | 生产调用面中没有 `AddScheduledTask`/`ScheduledTaskRegistration` 业务注册；正式接线只有 `AddTaskScheduling()`。调度目录不含账号、角色、库存、数据库、事务、EventBus 或 NOTI 依赖。说明文档明确未来定时仅处理仍在线的精确会话，离线角色走登录清理，两者复用同一领域逻辑；示例接口标明未实现。 |

## 测试与冻结目标核对

- `artifacts/scheduled-task-dispatcher-20261014/application.trx` 中调度命名空间用例为 25/25 Passed；其中启动补执行测试的完成信号直接由注册回调设置，重试、慢任务、并发、scope 与取消测试也都在回调内部产生可观察结果。
- 同一冻结目标的 `application.log` 为 3861/3861，通过且无跳过；`host.log` 为组合/启动相关 11/11，通过且无跳过。
- `source-manifest.json` 列出的 9 个代码、测试和说明文件 SHA-256 均与审查时实际文件一致，排除了“测试通过后源目标又变化”的陈旧证据问题。
- 完整未跟踪清单除任务治理文件、上述交付和既有 `Tool/WireSmoke/__pycache__/` 外无额外生产改动；后者按 TaskContract 明确保留，不属于本任务。

## 已接受的边界风险

- 取消是协作式；不响应 token 的回调会延长停止时间。实现没有强行放弃该回调并并发重试，符合 Spec Limits。
- 执行记录不持久化，`RunOnce` 跨重启可能再次执行，业务回调必须幂等；不提供跨进程恰好一次。这些限制已在说明文档中明确，且属于 TaskContract 的显式范围外。
