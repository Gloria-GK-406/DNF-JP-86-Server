# 协议 CMD：当前模型与新增协议步骤

本指南对应 `serial-gameplay-executor` 的协议级 CMD 模型。架构约束以 [AGENTS.md](../AGENTS.md) 为准。
迁移历史和具体有意行为变化保存在任务的 Spec／验证记录中；这里说明今后写代码时的当前入口。

## 入站到出站

1. Networking 仅拆帧并按线级键找 Protocol decoder。未注册键仍静默丢弃。
2. dispatcher 用已注册的消息 CLR／目录成员找到薄 handler。handler 在执行器外原样传入消息和精确
   `NetworkDispatchContext`，调用一次 `ICommandBus.SendAsync` 并等待。
3. 总线以命令 CLR 类型找注册，建立唯一 FIFO 作业。作业先初始化发件箱，再建 CMD scope 与显式 request。
4. CMD 拥有整个协议用例：当前身份读取、正常拒绝、领域调用、各段工作单元、补偿和投影顺序。
5. 总线排空已开始操作、关闭 request、释放 scope、检查健康并处理失败；作业发件箱按登记顺序交给发送端。
   发送端独立处理网络、顺序组、超时、队列字节上限和连接关闭，CMD 不等待网络。

纯遥测只记日志，不进入 CMD；只读查询或纯协议响应仍要有 CMD。已注册协议消息必须与 CMD 双向闭合，
有限消息家族显式列出允许的 CLR 类型。普通 handler 不能查询在线状态、调用领域服务或直接登记响应。

## CMD 的边界

协议命令实现 `IProtocolCommand<TResult>`，携带原 `Dispatch`、原消息及其 `ProtocolFrameMetadata`。
handler 实现同一 `ICommandHandler<TCommand,TResult>`；内部命令使用 `ICommand<TResult>`，由生产者通过
`SendInternalAsync` 提交明确的 `InternalCommandOrigin`。不能以空 connection 或 null session 推断可信来源。

构造器只注入领域服务／store及有理由的具名机制能力。注入表由
[CommandHandlerArchitectureTests](../Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs)
维护。禁止总线、queue、executor、传输 sender／closer、内容源／规则服务、行网关和 service locator。
机制能力不因为有相似名称就自动获准。

具体 CMD 不继承共享 CMD 基类，不以可空服务、缺省服务、`null!` 或 `Func` 能力拼装半个可用对象。
共享业务交原领域 owner，协议共享算法按实际职责拆分；不能另建一个巨型依赖袋把用例重新藏起来。
业务可空数据、纯输入到值的函数和显式单元 action 合法。CMD 间不互调、不转发，执行器内再入队会被拒绝。

`CommandExecutionContext.Request` 只在本次调用中有效。业务不能从 DI、AsyncLocal、job slot 或领域事务
找回请求权限。工作单元必须显式收到当前 execution，完成后能力即失效；catch 后继续调用不能清除 sticky failure。

## 显式工作单元与失败

每段原事务用具名 `CommandWorkUnitDeclaration` 表达，保护参与者是实际精确会话／连接声明。
`WriteAsync` 与 `ReadOnlyAsync` 保各自事务语义；一个 CMD 可以有多段，不能把选角、补偿或逐成员结算
合成一笔事务。真实事务参与者与保护参与者不同，空保护集合不等于没写库。

`RequireRollback(cause)` 可以表示原正常拒绝；需要提交的正常拒绝仍提交。Main 只属于经 admission 的
效果请求，同帧重放保持原 entry 和 Ordinal，读取当前投影而不重复写资产。NonRequest／显式多 owner
footprint 只在获准 ordinary 写单元的活跃 action 使用，不扩大 Main／raw／恢复权限。

所有开始的单元必须完整收口。提交后编码失败、scope 释放失败、未请求取消、不确定结果默认保护已声明
精确参与者并关闭来源，不追加失败 ACK。一个原因在正常调用链传播只算一次，独立 rollback／scope 错误
即使用同一个异常对象仍是另一发生。

有限特殊机制必须保原条件：

- 健康确认回滚结果只认真实阶段／participant／cause／完成事实及完整清理。Mailbox 的领域自结算事实独立，
  不能从 `IsSettled`、异常类型或已经回滚推断该许可。
- 选角／回选精确 cleanup 只做固定动作，失败后不能恢复普通写或登记。HTTP Cera／Mail 审计必须匹配实际
  输入、request、command、origin／job和唯一完整失败；外侧响应搬运不把原作业改为成功。
- BodyOnly 取消仅在本次 producer token 已取消、批准的实际 action/body 发生、健康清理且无其它失败时回给
  producer；DI、commit、guard、scope-only、未知取消和提交后取消不能借用。
- 调度完成桥仅搬运已健康完成的原业务 Aggregate；Reliable 消费保 claim／调用／complete／commit／forget／
  dispose 顺序，原业务失败清理后才回滚并登记 retry。LiveOnly 消费决定先提交，不补播。
- 原三个 raw 编码出口分别是 UseStackable 的 `SettledRequiredExplicit`、具名重放 owner 的
  `CommittedReplayCurrentStateBatch` 与 CollectionBox 的 `SettledInitialBatch`。必须匹配 actual encoder
  的本次 occurrence、精确目标和完整清理，重放许可不覆盖首次成功，不增加第四种出口。

## 登记、当前受众与来源

Game 通过 `IGameCommandOutbox.RegisterCmdAsync`／`RegisterNotiAsync` 保持固定 CMD／NOTI 类别；
Channel 用 `ICommandMessageOutbox`。消息在实际登记处编码，保持原批次、顺序和收件人。
`ICommandProjections` 只做即时投影，不代领域服务发布持久事实。通知总线逐收件人隔离的 Failed 结果
不等于允许其它调用者吞掉必达编码错误。

`NotificationOutcome.Registered` 是兼容摘要，不足以证明真实接受。`RegistrationAccepted` 只有实际
Register 完成才为 true；Dropped、空批、不支持该结果的 sender 不能猜成功。日切等水位按实际接受推进，
也不把它说成已落线或客户端收到。

定时、HTTP、事件、连接生命周期与启动来源都提交 typed Internal CMD。生产者只组织已有输入、链、
busy／取消／排空，不预读世界、不传业务 callback。整轮扫描来源跟踪全部已接受轮次；逐项 CMD 之间
可让普通请求进入 FIFO。原状态 owner 不因新命令而复制第二份队列、截止时间或水位。

两个保留边界必须说清：事务内延迟 `ComposeAsync` 生产禁用（F-132），不能把不可达写成已经修复；
关闭请求已登记、尚未执行时，若传输先关闭，disconnect 仍可能继承登记的关闭链（F-131），
该链证明登记因果，不保证最后触发物理关闭的唯一原因。

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

## 登录实例：MId／Ticket 到 GameLoginCommand

真实入口为 [GameLoginHandler](../Server/src/Dfo.Server.Protocol/Game/Login/GameLoginHandler.cs)、
[GameLoginCommand](../Server/src/Dfo.Server.Protocol/Game/Login/GameLoginCommand.cs) 和
[GameLoginPayloadCodec](../Server/src/Dfo.Server.Protocol/Game/Login/GameLoginPayloadCodec.cs)。

launcher HTTP 登录校验账号凭据后返回 `MId`、`Ticket`、过期时间和频道 bootstrap。
MId 是该次会话签发的六位随机十进制字符串，不是账号名、AccountId 或角色 ID；Ticket 来自16随机字节，
以32位十六进制文本交客户端，服务端保存其摘要。客户端必须使用本次 HTTP 实际响应，不能自己拼一对。
Game LOGIN body 是两个 int32 little-endian 长度前缀的 ASCII 字符串，顺序为 MId、Ticket；
当前 decoder 分别限制256字节并要求 MId 非空，这个解析上限不改变签发值的长度。

薄 handler 的实际调用形状为：

```csharp
_ = await commands.SendAsync(
    new GameLoginCommand(context, message), cancellationToken).ConfigureAwait(false);
```

`message.MId`／`message.Ticket` 是 codec 解出的原值，`context.Connection` 是实际物理连接。
TCP connected 的生命周期 CMD 先注册匿名用户；匿名协议请求依然是 ProtocolNonEffect，不是 TrustedInternal。
登录 CMD 在 `game-login-redemption` ordinary Write 内调用认证领域服务，绑定这条连接、MId 和 Ticket；
正常兑换登记 LoginSessionRegistry／OnlineStateRegistry 内存参与者，提交发布身份后执行原顶替清理。
该兑换不加入 SQLite。launcher 侧账号注册、凭据复验／更新所需的持久事务属于另一个用例；
会话票据自身仍由内存注册表持有，不能把签发票据说成写入 SQLite。

兑换成功后再次核当前精确身份，读取账号设置，经 `IChannelEntryDomainService.ResolveLoginAsync` 取得
listener 对应的环境与部署端点。按原次序登记成功 CMD、游戏设置、热键、两条拍卖状态、荣誉和通知；
热键后激活 profile0，位置仍在兑换事务之后。拒绝记原 Warning、撤销身份并登记完整连接关闭，
没有凭空补一个客户端未建模的登录失败 ACK。真实 TCP／SQLite 行为由
[GameLoginIntegrationTests](../Server/tests/Dfo.Server.Networking.Tests/Integration/GameLoginIntegrationTests.cs) 验证。

## 新增一个协议

1. 在 `protocol/` 按模板写证据与布局，登记 catalog；目录枚举只放身份，不放消息 record。
2. 新建 Protocol 切片的消息、codec、decoder、encoder、薄 handler 和 typed CMD。
   CMD 需要的领域能力先找到真实 owner，不能直接拿规则／存储或再包装一个旧命令。
3. 明确原业务拒绝、事务点、保护参与者、足迹及投影顺序，再用显式工作单元落地；
   不因“整协议一个 CMD”就把所有动作合成一个事务。
4. 在 ProtocolModule 或它唯一调用的具名扩展注册 decoder／encoder，使用
   `AddProtocolCommandHandler<TCommand,TResult,THandler>` 精确绑定消息目录及 CLR，并用
   `RegisterProtocolMessageHandler<THandler,TMessage,TCommand>` 配对薄 handler。没有 ExecutionKind。
   多消息族逐个绑定真实成员；纯遥测必须走专门注册，不能借它绕过 CMD。
5. 测试 codec 字节、原输入传递、正常拒绝与成功、真实工作单元／当前态／精确物理替换及线级顺序；
   让路由闭合与注入结构判据实际扫到新类型。已有强用例能守住的路径不重复测试。
6. 客户端可控且会扇出的变长字段，必须证明解码或领域规则已有上限及编码字节上限；
   不能因本方接收合法就推断不会挤爆第三方连接的发送队列。需要新增限制时先给协议证据，不能猜线上数值。

最终验收的完整测试、Characterization／Golden、同工具负载与每条 CMD 最大耗时上限属于交付证据，
不把文档更新或局部测试当作它们的替代。
