# 领域事件与通知投影

修改领域事件、订阅、通知或多人投影前阅读；作业、事务与发送边界见
[GameplayExecution.md](GameplayExecution.md)。

## 三种入口

领域服务经 `IEventService` 发布事实，订阅者决定如何响应；协议 CMD 经 `ICommandProjections` 登记即时投影。通知只是消费者之一。
组件不得直接依赖 `INotificationBus`：只有 `NotificationProjectionSubscriber<T>` 适配器、
通知运输机制与组合注册接触该接口。协议编码和 Socket 发送仍由 Protocol/Networking 拥有。

- `EnqueueAsync<TEvent>`：当前可写业务事务内记录强类型事实与匹配订阅的 delivery；
  与业务使用同一个 SQLite 事务。提交后只向容量 1 的 Channel TryWrite 唤醒，回滚不投递。
  每事务一个 BatchId，每事实一个 EventId，payload 不带服务、连接、协议消息或可变聚合。
- `DirectAsync<TEvent>`：不入队、直接依序等待运行时订阅者；不自行开启事务，继承调用方当前事务，
  可在事务内外调用，失败与取消向上传播。次数写入订阅要求当前可写业务事务，扣次事实不注册为队列订阅；
  提交后的界面刷新另由持久化变化事件进入 ProjectAsync。马战维护仍在事务外发布阶段变化，
  查询不发布；启动恢复静默、变化去重、失败不重复公告。
- `ProjectAsync<TNotification>`：在当前作业里组装即时协议投影并登记进发件箱，原样返回 NotificationDelivery；
  不持久化 Session/CMD 回复，也不能在未提交的可写事务内调用。登录、队伍、库存等流程
  保持批次、精确会话与 ACK 顺序；CMD 使用具名登记接缝，不能注入传输 sender。

事件稳定名称/版本与 subscriber ID 在组合根注册。每个订阅组一个生产者循环：在执行器外读出到期的
delivery，把每次消费通过 typed Internal CMD 入队为 `event:<订阅 ID>` 作业并等它完成，再处理下一条；闲时 await
唤醒或最近重试时间，启动扫描遗留工作，停服取消未完成消费。Reliable 订阅在作业内的新 scope
与新事务中处理，业务更新与完成标记同事务；失败回滚后另事务记录重试。
Reliable 的注册分发在当前 CMD 内完成，不嵌套 dispatch。组内串行，失败退避期间后续到期事实可先处理，不保证严格 FIFO；
订阅组之间不并发，因为都在同一个执行器上。当前只注册每个稳定名称的一个版本；未知版本保留
诊断，不自动 upcast；新订阅不回填历史 delivery，完成记录暂不自动清理。

LiveOnly 只处理本进程的新鲜事件：发送前提交一次消费决定，离线、旧进程或过期提示跳过，
失败不补播；崩溃窗口允许漏提示。邮件每封记录 MailCreated，提醒按 BatchId/角色合并，
不以合并提醒丢失事实。GM 活动配置在领域事务记录变化，HTTP 不负责广播。

协议即时投影在 CMD 内按原阶段登记。领域动作完成及所属事务落定之后，CMD 将具名结果交给
`ICommandProjections.ProjectAsync`，最后由作业发件箱统一交付；不在薄 handler 中再次调用总线或 sender。
选角的 PVP 发布、精确清理等流程保自己的受众和顺序，不能为了统一写法移动事务点。

## 通知受众与处理器

- **人群以角色表达，不是连接。** `NotificationAudience` 是一个封闭层级的值；
  「这个角色此刻在哪个连接上」只由通知服务回答（`NotificationAudienceResolver` 解析一次，
  `NotificationBus` 在每个收件人轮到时再问一次）。**领域服务不得为投递去查连接**——
  通知服务负责隔离领域受众与传输连接。
  **规则的边界说准**：禁的是「**拿一个角色去查它在哪个连接、为的是给他发东西**」。
  两个限定缺一不可——按角色取会话（`TryGetInWorld`）本身不是禁令的对象：
  `ExperienceBonusDomainService` 这类调用取的是**会话标识**当运行态目录的键，**从不读 `ConnectionId`**，合规。
  按「见到按角色取会话就是违规」去 grep 会把它们误判。
  不禁的是「这次操作本身就发生在其上的那个连接」：清理计划带着离开者自己的
  `session.ConnectionId`（`SelectCharacterLifecycleServices.cs`），必送给本人的那一半直接走它，
  不是扇出。
  **别把这条豁免读宽**：`SelectedSessionIdentity` 里带着 `ConnectionId`，它作为参数传入领域服务不代表该服务可以替任意角色定位连接。
  豁免的是**这次操作的主体自己的**那一个，不是任何拿得到的连接；
  凡是要决定「还该告诉谁」的，一律走人群。
- **通知类型自带它的处理器契约。** `INotification<TSelf>` 是自指的，所以发布调用点的类型由实参定死，
  加宽到契约接口本身编译不过（`CS0311`）。处理器按**收件人**产出该收件人的消息，返回空表示弃权。
- **处理器只读。** 写属于已经提交完的那条命令。`NotificationHandlerArchitectureTests` 守着注入面：
  只允许 `Dfo.Server.Abstractions` 里以 `DomainService` 结尾的接口。
- **通知没有重试，也没有每收件人期限。** 组装或编码失败的收件人记为 `Failed`，只丢他的条目；
  交付失败由发送端关闭该连接，之后的清理是那条连接的 `lifecycle:disconnected` 作业。
  `NotificationOutcome.Registered` 保留原摘要口径，连接已不在而丢弃时也可能为此值。
  真正 outbox 接受由 `NotificationAttempt.RegistrationAccepted` 单独表示；周期水位只能依据这个事实推进。
  两者均不证明已落线或客户端收到，不支持接受结果的旧 sender 默认 false。
- **通知类型必须在 `ProtocolModule` 里注册处理器**，且该程序集要 `AddNotificationCatalog`。
  漏注册会在**开始接受连接之前**让服务器起不来（宿主先实例化 hosted service 再启动）。

## 消费边界

- 人群通知用于 PvP 大厅公示、会话清理以及队伍城镇投影等路径；
  `PartyTownProjectionDelivery` 使用 `TheseSessions` 指定精确会话。
- `SessionAdjacentEntryDomainService` 给入场者本人计算周围角色，不因此成为多人扇出。
- 事件服务拥有领域订阅与分发；通知总线是低层投递机制，只组装并登记进发件箱，不处理可靠领域事实。
- 回给请求方的消息仍可直接投递，不必套上人群抽象。
- 没有专用的频道、全服或组队 Audience 类型；队伍业务已存在，使用现有精确会话人群。
  人群 record 层级不能完全阻止外部派生，解析器会明确拒绝未知形态。

## 结果与日志

投递结果既有摘要，也有日志。总线返回成功（已登记）、弃权、离线与失败结果；
`PartyTownProjectionDelivery` 读取成功和失败集合，用于维护已登记的受众与后续处理；这不证明网络送达。
读取摘要仍是可选的，不能依赖每个调用方都读取它来保证可观测性。
所以在还拿得到原因的地方记日志：

- 组装或编码抛异常 → 总线（或发件箱的逐收件人组装）记 `Warning` + 异常本身，只丢该收件人的条目；
- 交付失败、超时、超过队列上限 → 发送端按连接记，级别见 [玩法执行与出站](GameplayExecution.md#发件箱与发送端)。

**弃权与「人不在了」不记**——它们不是出了问题。
