# 协议 CMD：当前模型与新增协议步骤

新增或修改协议、handler、CMD 前阅读。项目分层见 [Architecture.md](Architecture.md)。
修改工作单元、权限或失败处理时同时阅读 [玩法执行与事务](GameplayExecution.md)。
帧级基线见 [NetworkingFrameProtocol.md](../Docs/NetworkingFrameProtocol.md)。

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

业务依赖只允许 Abstractions 中的 `*DomainService`、`*DomainStore` 和既有具名 `IPartyService`。
角色／任务簿／容器实例从领域服务取得，事实值从命令输入传入。
机制能力限定为显式工作单元、作业发件箱／关闭／投影、出站顺序、固定槽位换算、时钟和日志；
选角、翻牌、祭坛、Joust、HTTP 审计、生命周期、事件和调度的特殊能力须有具名理由。精确注入表由
[CommandHandlerArchitectureTests](../Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs)
维护。禁止总线、queue、executor、传输 sender／closer、内容源／规则服务、行网关和 service locator。
机制能力不因为有相似名称就自动获准。

具体 CMD 不继承共享 CMD 基类，不以可空服务、缺省服务、`null!` 或 `Func` 能力拼装半个可用对象。
共享业务交原领域 owner，协议共享算法按实际职责拆分；不能另建一个巨型依赖袋把用例重新藏起来。
业务可空数据、纯输入到值的函数和显式单元 action 合法。CMD 间不互调、不转发，执行器内再入队会被拒绝。

`CommandExecutionContext.Request` 只在本次调用中有效。业务不能从 DI、AsyncLocal、job slot 或领域事务
找回请求权限。工作单元必须显式收到当前 execution，完成后能力即失效；catch 后继续调用不能清除 sticky failure。

## 工作单元与出站

CMD 显式划分各段 Write／ReadOnly，声明真实保护参与者；一条协议不是一笔自动事务。
事务、权限、重放、有限恢复及 raw 出口的完整约束统一见
[玩法执行与事务](GameplayExecution.md#总线与显式工作单元)。
Game 经 `IGameCommandOutbox` 登记，Channel 经 `ICommandMessageOutbox` 登记，保持 AES-128-ECB、zlib 与当日密钥规则。
即时投影经 `ICommandProjections`，其受众与结果语义见 [事件与通知](EventsAndNotifications.md)。
作业不发送 socket、不等待网络；出站顺序、关闭及框架兼容见 [发送端](GameplayExecution.md#发件箱与发送端)。

## 切片边界

一个协议 = `Dfo.Server.Protocol/<Endpoint>/<Protocol>/` 一个目录，最多六类制品：

| 制品 | 文件 | 何时可省 |
|---|---|---|
| 消息 record | `Messages.cs` | 从不 |
| payload codec | `*PayloadCodec.cs` | body 无需解码时（Channel 系） |
| 入站解包器 | `*MessageDecoder.cs` | 纯出站协议 |
| 出站编码器 | `*ResponseEncoder.cs` / `*NoticeEncoder.cs` | 纯入站协议（遥测类） |
| 消息 handler | `*Handler.cs` | 纯出站协议 |
| 命令 + 命令 handler | `<Verb>Command.cs` | 仅纯遥测无命令；只读或纯协议响应仍有 CMD |

规则：

- **切片内不跨协议引用**。要共用就上提到 endpoint 层
  （`Channel/ChannelProtocol.cs`、`Game/GameProtocol.cs`）或领域服务。
- 目录名不得与任何被引用类型同名——C# 名称查找里外层命名空间优先于
  using 导入的类型，同名会得到 CS0118。`AccountRoster` 之所以不叫
  `AccountCharacterRoster` 就是因为这个。
- 新协议只改一处接线：`ProtocolModule`。`ApplicationModule` 不承载任何
  单个协议的注册。

Game `0x01` 是 CMD、`0x00` 是 NOTI；每个出站消息必须且只能实现 `IGameCmdMessage` 或
`IGameNotiMessage` 之一，编码器继承固定类别基类。CMD 用 `IGameCommandOutbox.RegisterCmdAsync`／
`RegisterNotiAsync`；批量和显式收件人登记保每条消息自己的类别，不让调用者覆盖首字节。
Networking 仅把此字节当不透明 Selector。

`CLIENT_SPEC_STATISTIC`（0x00AB）需查询在线归属，因此走 CMD；EXIT 与 Channel Connect 虽只返回协议响应，
也走 CMD。纯遥测经 `RegisterTelemetryMessageHandler` 注册，handler 构造器只能注入日志，`MessageHandlerRegistry` 在构造时拒绝越界依赖。
ScriptVersion、Script、Info 经 `IChannelDirectoryDomainService` → `IChannelContentRuleService`
取得目录与脚本，不能由 CMD 直接读取内容源。
连接生命周期由 typed Internal CMD 驱动；`OnlineUserConnectionObserver` 持有 Game 初始消息等协议知识，
作业内调用领域与精确清理；传输 observer 只提交并等待。

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
不编码登录失败体，客户端看不到错误码。真实 TCP／SQLite 行为由
[GameLoginIntegrationTests](../Server/tests/Dfo.Server.Networking.Tests/Integration/GameLoginIntegrationTests.cs) 验证。

## SelectCharacter：接受与状态同步

薄 `SelectCharacterHandler` 只提交 `GameSelectCharacterCommand`。其 CMD 顺序执行准备、补货、发布、
PVP、任务、宠物和同步投影，保留每段独立事务及当前性检查，不嵌套派发。`01/0004` 是进入接受，
发送前完整预校验必达接受／前置状态；失败走具名精确 cleanup 与原补偿顺序，再登记连接关闭。
清理自己的独立失败不能被原失败遮盖，也不能借 cleanup 重新开启普通业务。

进入接受、PremiumService、CloneTitle 为 CMD，其余当前状态推送为 NOTI；拒绝仍为相邻的
`01/0004` 与 `00/01F7`。关闭登记排在此前消息之后，真正发送和关闭由作业外发送端执行。
入场 DTO 仅供当次组包，后续投影从当前领域事实重建。

## 新增一个协议

1. 按 [协议知识库规则](../protocol/README.md) 创建 `protocol/<endpoint>/<direction>/<type>-<name>/README.md`，
   登记 [catalog](../protocol/catalog.md)。`Data/Networking/NetworkMessages.cs` 只加目录枚举身份，不放消息 record；
   目录成员可以先于实现存在，已注册 decoder 产出的成员必须有 handler，启动时检查闭合。
2. 新建 Protocol 切片的消息、codec、decoder、encoder、薄 handler 和 typed CMD。
   CMD 需要的领域能力先找到真实 owner，不能直接拿规则／存储或再包装一个旧命令。
3. 明确原业务拒绝、事务点、保护参与者、足迹及投影顺序，再用显式工作单元落地；
   不因“整协议一个 CMD”就把所有动作合成一个事务。
4. 在 ProtocolModule 或它唯一调用的具名扩展注册 decoder／encoder：入站声明线级键与可产出的目录成员，
   出站声明消息 CLR 类型与允许端点。使用
   `AddProtocolCommandHandler<TCommand,TResult,THandler>` 精确绑定消息目录及 CLR，并用
   `RegisterProtocolMessageHandler<THandler,TMessage,TCommand>` 配对薄 handler。没有 ExecutionKind。
   多消息族逐个绑定真实成员；纯遥测必须走专门注册，不能借它绕过 CMD。
5. 测试 codec 字节、原输入传递、正常拒绝与成功、真实工作单元／当前态／精确物理替换及线级顺序；
   让路由闭合与注入结构判据实际扫到新类型。已有强用例能守住的路径不重复测试。
6. 客户端可控且会扇出的变长字段，必须证明解码或领域规则已有上限及编码字节上限；
   不能因本方接收合法就推断不会挤爆第三方连接的发送队列。超限会关闭收件的第三方连接。
   在协议记录中写明上限依据与证据等级；尚无通用判据自动证明任意新字段的扇出字节上限，
   不能用统一 string 长度检查替代协议分析，不能猜线上数值。

最终验收的完整测试、Characterization／Golden、同工具负载与每条 CMD 最大耗时上限属于交付证据，
不把文档更新或局部测试当作它们的替代。

修改 handler 前检查真实行为覆盖：类名搜索只能定位候选测试，不能证明覆盖充分。
重点检查当前事实读取、事务结果、精确会话和发帧行为；有实际回归风险且现有验证不足时再补测试。
可从 `MonsterDeathHandlerTests`、`TownProjectionHandlerTests`、`SelectDungeonHandlerTests` 进入。
