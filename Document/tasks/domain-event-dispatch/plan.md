---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan：统一事件服务

## Planning Choice

一个详细计划维护统一接口及迁移次序，独立调查与最终审查按明确范围交给子代理；不额外维护 Task Unit 注册。先保护事务和协议语义，再完成全量调用迁移。

## Approach and Shared Decisions

- 新机制放 Abstractions/DomainEvents、Application/DomainEvents、Persistence/DomainEvents，避开已有活动 Events 命名。
- IEventService 为组件统一入口：EnqueueAsync 记录可靠业务事实；PublishAsync 分发仅进程内领域事件；ProjectAsync 分发即时协议投影并返回 NotificationDelivery。即时投影通过类型化 NotificationProjectionSubscriber，INotificationBus 仅在这一适配层出现。
- 可靠目录用稳定字符串类型名、版本与 JSON；IDomainEventSubscriber<T> 接收强类型信封，独立订阅组。不对协议消息/SelectedSessionIdentity 做序列化。
- 使用现有 SQLite 单一参与者和 TransactionalExecutionGate，提交回调仅唤醒。Reliable subscriber 处理与完成标记同事务；LiveOnly 先提交消费/合并标记再发通知。
- 唤醒 Channel 容量 1，DropWrite、异步 continuations；每组固定任务，清空当前到期批次后等待信号/最近重试/停止。启动必先扫描。注册校验不 eager resolve 所有网络 handler。
- 邮件 MailCreated 每信一条，BatchId 以事务为边界，LiveOnly 提醒按 BatchId/Recipient 合并。马战事件不持久化旧公告，仅迁移维护发布到运行时类型化事件订阅。

## Work and Dependencies

| Work | Executable scope / dependencies |
|---|---|
| T-001（RQ-001/002/003） | 固定契约、事件目录/订阅注册、信封、公开 EventService 方法、类型化即时投影与运行时事件分发；先写目录/路由/无事务拒绝测试 |
| T-002（RQ-003/004/009） | 实现 SQLite event/delivery 表、唯一 Schema 声明和 Persistence 注册；验证与业务共用事务、回滚、完成/合并/重试状态；依赖 T-001 契约 |
| T-003（RQ-004/005） | 后台分发和 scope/gate/事务控制、Channel/timer 等待、恢复/取消/失败边界；验证独立订阅和同事务处理；依赖 T-002 |
| T-004（RQ-006/008） | 邮件事实及订阅提醒、马战运行时事件与通知转换；修改相关通知断言为确定性 drain；依赖 T-001/003 |
| T-005（RQ-001/007/010） | 根据矩阵迁移全部 Protocol/GmWeb/GM reminder 直接调用到具名事件投影入口，保持现有投递结果与 ACK 顺序；建立依赖判据；依赖 T-001 |
| T-006（全部） | 序列构建与测试，派遣独立子代理审查结构和全部发布点、持久化/后台边界；修复 findings 后重测、复核并写证据、唯一接受结果提交 |

## Verification

序列运行 .NET 构建和必要测试（共享输出不并行）。事件机制针对事务、恢复、多订阅、payload、失败/取消验证；Application 通知/邮件/Joust 与 Protocol 相关集成回归；Host GM 及组件生命周期回归。真实 PVF 测试使用 protocol2 的 Script.pvf。源码/反射审查补强架构检查，审查报告必须覆盖所有旧发布点而非仅字符串替换。不开启测试部署，不写运行数据库。
