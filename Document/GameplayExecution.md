# 玩法执行、事务与出站

修改执行器、工作单元、后台来源、发件箱或传输交付前阅读。协议开发步骤见
[ProtocolCommands.md](ProtocolCommands.md)，领域事件消费见 [EventsAndNotifications.md](EventsAndNotifications.md)。

## 单一执行器与生产者

**一条队列。** 进程内只有一个 `GameplayExecutor`，FIFO、单消费者，一个作业结束才取下一个。
生产入口通过 `CommandBus.SendAsync`（协议）或 `SendInternalAsync`（具名内部来源）提交 typed CMD；
底层 `IGameplayQueue` 由机制使用，不把任意业务委托当作新的内部命令输入。协议作业以 CMD 类型命名，
内部来源明确给出 timer／event／lifecycle／http／gm／startup 标签与调用链。

**生产者只组织输入、提交、等待。** 薄 handler、定时源、事件组循环、HTTP／GM 路由与启动服务不在队列外
读写玩法世界、开写事务或发包。HTTP 的解析、身份输入、密码计算与响应映射属于外侧边界，每条路由的
解析器构造自己的具名 CMD（`HttpOperationCommand`，账号用例在 `Application/Authentication`，GM 用例在
`Application/Administration`），只带纯数据，不能传入业务 `Func`；解析失败也作为 `FailedHttpInputCommand`
入队。`HttpRouteOperation` 只是 HttpApi／GmWeb 选择响应映射的路由标识，不进入 Application。定时源按原规则合并 hosted tick，
两塔与每日刷新跟踪完整扫描／逐项轮次，手工已接受轮次仍须排空；同一轮的不同 CMD 之间允许正常 FIFO 插入。
每日刷新在真实 outbox 登记接受后才推进当前周期和精确会话水位，不把“尝试过”当成接受。

**玩法路径上不加门、信号量与「锁内复查、重试」循环**，串行只靠这条队列（`LockRemovalSourceTests` 按名字与文件守着）。
短 `lock(sync)`、PVF 内容源与规则初始化的锁保留；执行器外的口令哈希限流保留它的信号量。

**作业里不等网络，也不入队后等待。** 在执行器上调用入队点并等待是自我等待，以 `GameplaySelfWaitException`
作为程序错误失败；作业直接写或关传输连接同样失败。从作业里分出去的工作在 `ExecutionContext.SuppressFlow`
下启动，传输回调入口经 `GameplayJob.RunDetachedAsync` 清除作业身份；两者能出现在哪些文件由 `DetachmentSiteTests` 列名单。

**运行期判据没有开关。** 执行器外开启写工作单元、登记发件箱、调用玩法状态的写入口，立即抛出
`GameplayOffExecutorException`。玩法状态名单的唯一出处是 `GameplayWriteEntryTableTests.Types`（挂点）与
`GameplayStateCensusTests.Exemptions`（豁免）；新增持有进程内可变状态的单例或静态字段必须进其中之一，
普查判据会拒绝漏登记的。测试在执行器外写玩法状态要经 `Server/tests/Support/TestGameplay.cs`。
只有 `GameplayExecutor` 能进出作业（`GameplayJobHostStructureTests`）。
口令哈希只经 `PasswordComputationLimiter`，在执行器外计算，结果作为作业的输入；在执行器上调用即失败。

## 请求上下文与调用链

**请求上下文显式传递。** 每个 CMD 的 `CommandExecutionContext.Request` 只属于当前作业、当前调用；
结束后关闭，不能由 DI、静态字段、job slot 或 AsyncLocal 获取“当前请求”。ProtocolNonEffect、
ProtocolEffectRejected、ProtocolEffectSelected、TrustedInternal 是封闭状态，不以 null 会话推断内部权限。
仅执行器当前 job 和领域事务保留各自必要的 ambient；它们都不是请求权限的替代。

**调用链。** 每个作业带根、父、深度、来源种类与来源连接。事件作业是发布它的那个作业的子；作业登记的关连接
引起的 `lifecycle:disconnected` 是登记它的那个作业的子；入站、定时、GM、HTTP、启动恢复与传输层自发的关闭是新根。
深度上限 `GameplayCallDepth.Limit`（32）：超限的作业不入队，执行器记一条带完整链的 `Error`，根来源是客户端连接的，
关闭该连接；超限的事件 delivery 由系统作业落定，超限的拆除改作新根执行。

**失败与日志。** 作业失败由执行器记一条带调用链与来源连接的 Error。生产者用 `GameplayJob.EndedAJob`
识别已经报告的失败，同一传播链降为 Debug 或带额外事实且不附异常的 Warning；独立业务／清理错误仍按真实
发生报告。定时源只隔离已经执行的项失败，拒绝入队不伪装作已执行成功；重试、取消与停机必须保留各来源的语义。
同一 cause 的独立清理失败也是另一发生，不能用异常引用相同来抹掉。

## 总线与显式工作单元

只保留 `ICommand<TResult>`、`ICommandHandler<TCommand,TResult>` 与一个 `CommandBus`。注册类别只有
Protocol／Internal，没有细粒度内联路由、ExecutionKind、标记接口管线或逐命令自动事务。
协议 handler 提交一个 `IProtocolCommand` 并等待；内部来源显式提供 `InternalCommandOrigin`。
CMD 内禁止直接或间接再次 dispatch，执行器内入队等待、泄漏续体或已关闭请求都会失败。

总线初始化发件箱，创建 CMD scope 和显式 request，调用 handler；随后排空已开始的操作、关闭 request、
释放 scope、检查作业健康并裁决默认失败。未 await 的单元不会因 handler 提前返回就越过收口。
注册检查消息／CMD 类型绑定、来源、有限能力和重复注册；未知内部命令仍在 dispatch 时拒绝，
没有按所有 `ICommand` 类型枚举补注册的规则。

每段事务用具名 `CommandWorkUnitDeclaration` 表达，保护参与者是实际精确会话／连接声明。
`WriteAsync` 与 `ReadOnlyAsync` 保各自事务语义；一个 CMD 可以有多段，不能把选角、补偿或逐成员结算
合成一笔事务。真实事务参与者与保护参与者不同，空保护集合不等于没写库。

`RequireRollback(cause)` 可以表示正常业务拒绝；需要提交的正常拒绝仍提交。Main 只属于经 admission 的
效果请求，同帧重放保持原 entry 和 Ordinal，读取当前投影而不重复写资产。NonRequest／显式多 owner
footprint 只在获准 ordinary 写单元的活跃 action 使用，不扩大 Main／raw／恢复权限。

所有开始的单元必须完整收口。提交后编码失败、scope 释放失败、未请求取消、不确定结果默认保护已声明
精确参与者并关闭来源，不追加失败 ACK。一个原因在正常调用链传播只算一次，独立 rollback／scope 错误
即使用同一个异常对象仍是另一发生。

有限特殊机制必须满足以下条件：

- 健康确认回滚结果只认真实阶段／participant／cause／完成事实及完整清理。Mailbox 的领域自结算事实独立，
  不能从 `IsSettled`、异常类型或已经回滚推断该许可。
- 选角／回选精确 cleanup 只做固定动作，失败后不能恢复普通写或登记。失败审计只给注册时声明
  `CommandFailureAudit` 的 HTTP／GM 命令：工作单元以 `CommandFailureAuditRole` 声明自己是执行单元
  （确认回滚或不确定时审计）还是准备单元（仅请求取消时审计），机制不按命令或单元名判断；审计必须匹配
  request、command、origin／job 和唯一完整失败，写什么记录由命令自己决定。外侧响应搬运不把原作业改为成功。
- BodyOnly 取消仅在本次 producer token 已取消、批准的实际 action/body 发生、健康清理且无其它失败时回给
  producer；DI、commit、guard、scope-only、未知取消和提交后取消不能借用。只读单元的 action 取消另须
  命令注册时声明 `ReadOnlyActionCancellation`（仅内部路由），且来源为 Timer／Http／Gm 的 BodyOnly；
  机制不按路由、操作或单元名判断，能否干净取消是 CMD 自己的能力。
- 调度完成桥仅搬运已健康完成的原业务 Aggregate；Reliable 消费保 claim／调用／complete／commit／forget／
  dispose 顺序，原业务失败清理后才回滚并登记 retry。LiveOnly 消费决定先提交，不补播。
- 三个获准的 raw 编码出口分别是 UseStackable 的 `SettledRequiredExplicit`、具名重放 owner 的
  `CommittedReplayCurrentStateBatch` 与 CollectionBox 的 `SettledInitialBatch`。必须匹配 actual encoder
  的本次 occurrence、精确目标和完整清理，重放许可不覆盖首次成功，不增加第四种出口。

Committed、ConfirmedRollback、Uncertain 是不同事实；不能拿 `IsSettled` 推断成功。
普通 ProtocolNonEffect 没有 Main／操作账本／raw 权限；效果消息须经过真实 admission，唯一 Main
绑定原精确物理连接账本与 entry。同帧确认回滚重试复用 entry，不延长原 Ordinal 窗口；Pending 收尾为拒绝。
Footprint 必须绑定 request、job、owner、活跃 action 与事务。NonRequest／DeclaredNonRequest
不扩大 Main 或恢复权限；保护只取实际声明的精确 session／physical，早期 null 不遮掉后续 physical。
`WriteResultAsync` 只在获准真实阶段、健康确认回滚及完整清理后返回 `CommitRolledBack`；
独立 guard、rollback、lease／scope 错误否定单一健康恢复依据，不能清除请求失败后继续普通业务。
raw 出口还必须匹配 actual encoder 的 opaque attempt 与原 cause；业务、lookup、登记守卫或额外失败仍默认处理。

## 领域事务与存储载体

参与者按 identity 去重，全部 Prepare → 唯一可失败者 Commit → 其余 Commit；
一次事务只允许一个 `CommitCanFail`，第二个 Enlist 即拒，回滚只涵盖已登记参与者。
SQLite identity 是连接字符串，一个数据库一个参与者，物品／人物／任务／暗骑页是同一个参与者的四个 `ISqliteStoreFacet`，
可读本次未提交写。`IDomainTransactionAccessor` 用 AsyncLocal 暴露事务，不能用它取得当前请求。
`IDomainStore<TSession>` 给出载体身份、持久性与事务会话；`IDomainStoreSession<TPlace,TKey,TValue>` 共享
按位置读／写／清空的类型词汇：Item 与 DarkKnightCombo 是具名齐次 session；Character 的 Creatures 与其余
具名分区、Quest 的 Carried／Cleared／Tracked 分区组合成各自 session。`AmbientStoreAccess` 边界内用环境事务，
边界外读放行、持久写拒绝、易失写自开一次性边界；实例／快照不因共享词汇丢失类型。

## 发件箱与发送端

**发件箱。** CMD 通过具名 outbox／projection／closure 接缝登记，登记时立即编码，不等网络。
单元内条目随真实完成状态保留或丢弃；单元外条目及关闭请求按原规则保留，作业结束后按登记顺序交发送端。
生产禁止调用事务内延迟 `ComposeAsync`；机制测试不代表生产路径可用。
`CommandJobEntry.Begin` 在身份检查、CMD scope 建立前初始化发件箱；即使没有任何消息条目，失败仍可关闭
精确来源。调用者不负责隐式补建发件箱。

提交后的编码失败、scope 释放失败、未请求取消及不确定结果走默认失败：保留完整原因，保护已声明的精确
参与者，关闭实际来源，不补失败 ACK。正常拒绝仍按各协议原语义处理。已批准的三类即时登记 raw 出口与
有限健康回滚结果另有真实阶段证明，不能从异常类型或“可重放”推导许可。通知总线原逐收件人 Failed 隔离
保持；它不代表其它调用者可以清除 request failure。

**发送端** `OutboundSender`（`Networking/Outbound/`）：每连接一个发送队列；条目分必达与尽力；跨连接的有序组
按登记顺序交付，组内一条必达条目没交付成功，其后的条目一律跳过（关连接请求除外）。交付超过
`OutboundLimits.DeliveryTimeout`（2 秒）、等前序有序条目超过 `OutboundLimits.OrderedWaitLimit`（2 秒）、
或队列超过 `OutboundLimits.QueueByteLimit`（1 MiB，按编码后的字节计）时，关闭该连接并丢弃它余下的条目；
连接已不在登记表里时条目直接丢弃；没有重试。执行器从不等待发送。
日志：普通交付失败、超时、超限记 `Warning`；交付失败时传输层已无法解析精确连接、连接关闭 token 已取消，
或真实发送命中当前 .NET 的已完成 PipeWriter 抛错点时，记 `Information` 并带后缀
`; the connection was already gone`。这覆盖 TouchSocket 写端先完成、连接仍 Online/已登记的窗口；
不按任意同类或同文异常降级，超时仍记 `Warning`。失败结果、关闭及 observer 通告、后继丢弃规则保持。

## 物理关闭与框架兼容

关闭请求已登记、尚未执行时，若传输先关闭，disconnect 仍可能继承登记的关闭链；
该链证明登记因果，不保证最后触发物理关闭的唯一原因。

TouchSocket 4.3.1 的 `StartAsync` 先 AddListen 后置 Running，过早接入可能使 OnAccepted 退出且不续挂 Accept。
服务端先无监听启动，再经公开 `AddListen` 绑定原始选项；成功或失败都恢复配置，失败撤销已绑定监听。
升级或修改此适配时验证绑定状态、重复启动、两种监听配置、失败清理和重试，不能靠增加冷启动等待掩盖竞态。
快速重连时，旧异步接收未结束就回池会使下一连接复用 `SocketAsyncEventArgs`。`OnTcpClosed` 必须先等
transport 收发任务完成，再进行连接观察者清理与框架回池；不能在该回调中等待 session 自己关闭。

有序关闭在 flush 已确认的帧之后取得 transport 的唯一关闭权，取消管道等待，再停止原接收／发送
任务。macOS／Linux 通过 `Shutdown(Both)` 解除不支持取消 token 的 native receive，原任务退出后
才丢弃剩余本地入站缓冲。Windows 的 Winsock `SD_RECEIVE` 会禁止后续接收、并使未读输入触发
reset，因此只执行 `Shutdown(Send)`，通过 `CancelIoEx` 撤销该 socket 的旧 overlapped I/O；
`ERROR_NOT_FOUND` 只代表本次没有 pending I/O，仍需等待原任务真正退出后才读取当前可用的本地
入站数据。最后默认 socket `Close()`，让 TCP 保留已接受的发送尾部。整个物理关闭阶段使用同一个
独立两秒期限，包含重试取消和缓冲丢弃；超时仍释放 socket 并保留错误。不等待对端 FIN，也不把
本地 EOF／Available=0 说成所有在途输入完成或任意持续输入都能无损关闭。
随后调用框架会话关闭完成生命周期清理，该阶段也独立受 `OutboundLimits.DeliveryTimeout` 限制，
均发生在发送端独立循环中。连接回调等待物理阶段结束才继续清理和回池；物理阶段不等待该回调。
TouchSocket 4.3.1 的 transport 固定调用 `Socket.Close(0)`，在 Linux 上属于 abortive close；其公开
会话 API 没有 graceful close 或 socket 入口。生产会话以精确 `UnsafeAccessor` 读取
`TcpTransport.m_socket`，并调用 `BaseTransport.TryBeginClose`、`SignalClose`、
`WaitForTransportClosedAsync`，避免原 reader 与关闭阶段争抢入站数据、框架提前执行 abortive close。
这是固定版本的兼容接缝，升级依赖时必须重验真实小接收窗口的大帧＋FIN、内核未读数据且接收管道
背压时的完整帧、阻塞回调的关闭期限与快速重连；不得在成员不匹配时静默回退到 abortive close。

## 停机

宿主先停止接受连接与 HTTP、停掉各后台服务；`GameplayExecutorLifetime` 再依次排空生产者
（传输排空会拆除仍登记的连接），最后排空并停止执行器。在线玩家的拆除落库因此在执行器停止之前完成。
