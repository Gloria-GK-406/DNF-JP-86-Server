---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 通用定时任务调度

## Planning Choice

单一机制可在一份详细Plan中约束；直接实施，无Task Unit分派。独立结果审查在完整实现后执行。

## Approach and Shared Decisions

- Abstractions/Scheduling：计划、不可变注册、missed策略、回调上下文。Daily计划显式固定UTC偏移，Interval计划有显式锚点，二者只消费时间值。
- Application/Scheduling：AddTaskScheduling/AddScheduledTask扩展；BackgroundService从DI快照读取所有注册。ApplicationModule接入空调度器，不注册实际业务。
- 单一PriorityQueue按Next排序，完成Channel唤醒；Task.Run隔离回调首次await前的同步工作，Semaphore限制实际同时运行的回调。每个任务仅一个运行/等待实例。
- 到期一次后直接计算当前时间之后的Next，跳过历史循环。RunOnce启动用Last；运行中已有同任务则标记最新Pending，完成后最多合并补一轮；Skip丢弃重叠触发。其他任务等待全局执行名额仍只有一个实例。
- 每次尝试CreateAsyncScope，异常后释放scope再有限延迟重试。停止token传递到等待与回调，不用超时WaitAsync放弃回调后再重试。无不可取消的常驻专属线程。
- 调度器最迟每分钟重新看墙上时间（同一等待循环），便于处理系统时间变化，不依赖机器本地时区。自定义时间计划运行中异常禁用该任务，不拖垮其他任务。

## Work and Dependencies

| Work | Execution | Coverage |
|---|---|---|
| T-001 | 契约和Daily/Interval计划、验证及扩展接口；先建立真实回调RED测试 | RQ-001/002 |
| T-002 | 共用等待/完成队列、受限执行、非重叠、补执行、重试/scope/停止 | RQ-003/004/005 |
| T-003 | ApplicationModule空接线；用户可注册回调样例和下一阶段边界文档 | RQ-006 |
| T-004 | 可控TimeProvider实际HostedService回归，依赖图/应用与Host相关回归，独立完整审查和新鲜验证 | 全部 |

## Verification

新测试用可推进TimeProvider及真实DI scope，不靠实际六点或脆弱耗时断言。最终运行调度专项、Application完整回归（真实PVF环境）、Host正式组合相关检查。独立审查重点为补执行/重试导致重叠、scope跨尝试泄漏、停止后继续回调、真实删除被越界接入。现有服务运行身份应保持不变。
