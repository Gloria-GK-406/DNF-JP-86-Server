# 架构哲学：一个请求怎么走、一个领域怎么切

> 2026-10-26，按协议级 CMD 与显式工作单元模型整理。
> 本文解释结构意图；具体约束以 [AGENTS.md](../../AGENTS.md#架构设计) 为准，
> 开发步骤见 [协议 CMD 开发指南](../../Document/ProtocolCommands.md)。
> 以下路径相对 `Server/src/`；历史任务记录只证明当时状态，不作为当前实现说明。

## 1. 一个请求有一个拥有完整用例的 CMD

用户的总架构是 `PacketHandler → CMD → 领域服务 → NotiBus（或 EventBus → NotiBus）`。
消息 handler 只做入队适配，原消息和精确物理连接一起交给 `CommandBus`。
CMD 才是用例编排的主人：解析当前身份、调用领域能力、划出各段工作单元、执行补偿并登记投影。

```
收包回调：解帧 → Protocol 解码 → dispatcher → 薄消息 handler
    → CommandBus.SendAsync(typed protocol command)
唯一 GameplayExecutor：初始化发件箱 → CMD scope → 显式 request
    → CMD：领域调用 / Write、ReadOnly 等工作单元 / 补偿 / 投影次序
        → 领域服务 → 规则、仓储与领域 store
        → ICommandProjections → EventService → NotificationBus → 消息登记
        → IGameCommandOutbox / ICommandMessageOutbox → 消息登记
    → 排空操作、关闭 request、释放 scope、完成失败裁决
    → 作业发件箱交 OutboundSender → 每连接发送队列
```

纯遥测仅记日志，留在收包回调中。定时、事件、生命周期、HTTP／GM 与启动来源提交 typed Internal CMD，
其来源、调用链和取消策略必须显式给出。生产者不在队列外预读世界再把可变对象送进去，
也不以任意业务委托代替命令。执行器内再次 dispatch 会自我等待，因此拒绝。

命令契约仍为 `Application/Commands/CommandContracts.cs` 的 `ICommand<TResult>` 与
`ICommandHandler<TCommand,TResult>`；没有第二总线、细粒度内联命令或按 ExecutionKind 自动套事务。
`CommandRequestContext` 是本次方法输入，不是注入服务、AsyncLocal 或可从 job slot 取出的全局权限。

## 2. 领域能力与协议编排各有归属

领域按功能内聚：`Abstractions/<领域>/` 放契约，`Application/<领域>/` 放业务、规则、订阅与投影，
`Persistence/<领域>/` 放存储，PVF 内容源在 `Infrastructure/Content`。同一事实只由一个 owner 保存。
几个组件互相维护同一事实副本，或同一问题有两套判定，是重新划边界的信号。

CMD 可以编排多个领域能力，但不接管库存、角色、任务等领域规则；领域服务也不认识某条协议的字节布局。
需要同时看具体消息与领域结果的逻辑留在 Protocol。纯帧算法及真正共享的协议阶段可以复用，
完整用例的判断、事务点、补偿和投影顺序则必须在具体 CMD 中看得见。

因此，不用共用 CMD 基类装全部协议依赖，不用可空服务、`null!` 或 `Func` 能力选择“本次能做什么”，
也不把它们改名为依赖袋或转发门面。协作者的构造契约应完整且非空，状态仍留在原领域 owner。
业务值可空、纯输入变换和显式工作单元 action 是另一回事，不能用禁止依赖空值去改变业务可选性。

构造器允许领域服务／store与有限具名机制能力，白名单理由和拒绝反例由
[CommandHandlerArchitectureTests](../../Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs) 维护。
方法声明、类型绑定、启动闭合检查和真实生产扫描共同守边界，文档不替代判据。

## 3. 事务是一段业务边界，不是整个协议

`ICommandWorkUnits` 复用既有领域事务。一个 CMD 可以有多个 Write／ReadOnly，甚至在其中穿插
不属于事务的阶段。选角的准备、补货、发布、任务与宠物不能为了迁移而合为一个大事务；
补偿也不能因统一包装而撤销此前成功的独立清理。

参与者按实际 identity 加入，SQLite 同库共用一个可失败参与者；声明的精确 selected-session 保护集合
则用于失败隔离，两者不能混为一谈。空保护集合不证明没有写数据库。
提交、确认回滚和不确定结果分别处理；正常拒绝保原边界，不把所有拒绝都改成异常或回滚。

效果账本、Main、NonRequest footprint 与内部来源各有显式权限，不能从“有 session”或“没有 connection”
猜测。已提交重放只读当前投影；发生 guard 或独立清理失败时，捕获异常或返回业务值不能恢复请求健康。
有限确认回滚结果、HTTP 审计、选角清理和取消观察只服务各自获准阶段，不构成通用恢复框架。

## 4. 规则只读内容与传入事实

| 层 | 形态 | 责任 |
|---|---|---|
| 领域服务 | `*DomainService` | 读取当前世界、编排业务、修改所属状态 |
| 规则服务 | `*RuleService` | 读取内容／规则配置，接收 facts，产生不可变规则或决定 |
| 纯规则 | `static *Rules` | 纯计算，不碰 I/O |

规则服务不查角色／会话状态，业务服务不绕过规则服务读取 PVF。CMD 不直接注入内容源或规则服务。
频道部署端点等由具名领域边界返回；宿主配置、协议配置与内容规则仍按 AGENTS 中的具体分类处理。

## 5. 出站登记与领域事实不同

领域服务在可写事务中 `EnqueueAsync` 记录事实；`DirectAsync` 在当前调用中执行订阅者，不创建 CMD。
可靠与 LiveOnly 消费由独立 typed Internal CMD 驱动各自原阶段。可靠业务及完成标记同事务，
健康失败后另事务登记重试；LiveOnly 先提交消费决定，不承诺补播。

CMD 的即时 `ICommandProjections.ProjectAsync` 只是投影登记。ACK 等也可直接通过具名 outbox 登记，
两条路最后进入同一作业发件箱。组装／编码发生在原登记位置，作业不等网络；真实 Register 接受、
发送端落线与客户端接收是不同事实，周期水位只能依据前者的明确结果。

通知逐目标隔离保持原 Failed 语义；直接必达登记、scope 释放、未请求取消和不确定故障按默认失败收口。
失败作业即使无消息条目也能关闭精确来源。同一传播故障由执行器记一次 Error，独立清理错误不能消音。
事务内延迟 Compose 仍未用于生产；关闭因果保留原登记链的精度边界，详见开发指南。

## 6. 项目引用图是第一道边界

Application 不引用 Protocol、Networking、Infrastructure 或 Persistence；Protocol 才能同时看到
具体消息与应用能力。Host 是唯一组合根，同一进程、同一根 DI；不以新 ServiceProvider 藏依赖。
ProtocolModule 及其具名扩展负责协议接线，消息／CMD 双向绑定在启动时检查。
目录枚举可以先于实现存在；未知内部命令在 dispatch 时拒绝，不凭枚举数量宣称所有业务都已实现。
