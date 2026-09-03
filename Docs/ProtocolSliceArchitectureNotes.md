# 协议切片重构：架构不和谐点记录

本文只**陈述**，不改动任何代码。它记录 socket 编解码 DI 化与协议垂直切片重排
过程中观察到的、当前形态里仍然别扭的地方，供后续复审时逐条处置。

每条给出：**现象** → **可核对的位置** → **为什么现在没动它**。

对应重构：`.rks-agent/tasks/protocol-vertical-slices/`，涉及
spec-001（入站解包 DI 化）、spec-002（出站编码 DI 化）、spec-003（编解码切片）、
spec-005（投影投递重分类）、spec-006（handler 与命令切片）。

---

## 1. 出站 `ServerMessageType` 没有任何机械用途

**现象**：每个出站消息 record 都实现 `IServerNetworkMessage.MessageType`，但出站
编码表是按**消息 CLR 类型**注册的（`ProtocolModule.RegisterOutboundEncoders`），
编码器拿到的是具体类型，从不读 `MessageType`。因此一个出站 DTO 上写错
`ServerMessageType` 不会被任何东西发现——不报错，不影响线级字节。

**位置**：`Dfo.Server.Protocol/*/*/Messages.cs` 的每个 `IServerNetworkMessage`
实现；`Dfo.Server.Protocol/ProtocolModule.cs` 的 `RegisterOutboundEncoders`。

**为什么没动**：留枚举是明确的产品决定——需要一个地方能全量看到所有协议。
但"能全量看到"和"被机械校验"是两件事，当前只有前者。

**可选处置**：出站注册行同时声明 `ServerMessageType`，启动时断言它与 DTO 自报的
一致；或反过来承认它是纯文档，并在类型上标注这一点。

---

## 2. 解包器"声明能产出的类型"与"实际产出的类型"之间没有一致性检查

**现象**：入站注册行声明该解包器可产出哪些 `ClientMessageType`
（闭合性检查读的就是这份声明），但解包器实际 `return` 的消息是什么类型，
没有任何检查。声明与实现可以不一致而不被发现。

**最尖锐的一例**：`GetUserInfoMessageDecoder` 注册在线级 type `0x0008` 下，
声明产出 `GameGetAccountCharacterRoster`——这个目录成员没有自己的线号，是
合成出来的。这里声明与实现的距离最远，也最需要检查，恰恰没有。

**位置**：`ProtocolModule.RegisterInboundDecoders`；
`Dfo.Server.Protocol/Game/AccountRoster/GetUserInfoMessageDecoder.cs`。

**为什么没动**：闭合性检查（解包器声明 × handler 注册）本身是这次重构的成果，
它替换了原来的枚举完备性检查。再往前一步（声明 × 实际产出）需要类型系统层面的
改动，属于新设计而非搬迁。

---

## 3. `OnlineUserConnectionObserver` 带着业务语义住进了协议层

**现象**：它现在在 `Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs`，
但它做的事大半是领域动作：`RegisterConnection`、`TryDisconnect`、
`SelectedSessionCleanupService.CleanupAsync`、手工获取执行门。
它落在协议层的唯一原因是：连接建立时要发 `GameInitialConnectionNotice`，
而消息 DTO 在切片里，`Application` 又不能引用 `Protocol`。

**位置**：`Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs`，
`ConnectedAsync` 与 `DisconnectedAsync`。

**为什么没动**：它同时需要"某个具体出站消息"和"若干领域服务"，在当前引用图下
只有协议层能同时看到两者。拆开会改变 `ConnectedAsync` 失败路径与
`RegisterConnection` 的耦合，那是行为改动。

**这是本次重构中最刺眼的一条。** 它同时违反了"handler 不承载业务语义"的精神
（虽然它不是 handler，是连接观察者，规则字面上管不到它）和"协议层只做翻译"。

**可选处置**：在 `Abstractions` 定义一个"连接建立后要发什么"的出站端口，
由协议层实现、由 Application 的观察者调用；或者把连接生命周期本身建模为命令。

---

## 4. `RosterFollowUpDescriptor.ObservedSentinel` 是一份不再有理由存在的副本

**现象**：`0xFFFF` 在同一个切片目录里出现两次——
`RosterFollowUpPayloadCodec.ObservedSentinel` 与
`RosterFollowUpDescriptor.ObservedSentinel`。原注释说这份副本是为了
"让 Application 层不依赖 Networking"。搬迁后两者同在一个目录、一个程序集，
这个理由已经不成立。

**位置**：`Dfo.Server.Protocol/Game/RosterFollowUp/RosterFollowUpPayloadCodec.cs`
与同目录 `RosterFollowUpHandler.cs` 末尾的 `RosterFollowUpDescriptor`。

**为什么没动**：spec-006 的验收边界要求被迁移文件除命名空间、using、可见性与
失效引用外逐字不动。删掉一个常量不属于这四类。注释已改为陈述现状。

**可选处置**：删掉 `RosterFollowUpDescriptor.ObservedSentinel`，handler 直接引用
codec 的常量。这是纯化简，无行为变化。

---

## 5. `GameLoginHandler` 无条件回 `GameLoginResponse(true)`

**现象**：它不读命令结果，直接发成功响应——"命令没抛异常"即视为成功。
兄弟 handler（`ChannelScriptVersionHandler`、`GetAccountCharacterRosterHandler`
等）都是把 `result.*` 投影进响应的。

**位置**：`Dfo.Server.Protocol/Game/Login/GameLoginHandler.cs`。

**为什么没动**：这是 spec-006 之前就存在的形态，不是搬迁引入的。改它要先决定
登录失败该怎么回——而失败体目前**根本无法编码**（见第 6 条）。

---

## 6. 登录失败路径没有出站表示

**现象**：登录失败时没有可发送的消息，异常直接上抛，客户端看不到错误。
出站编码表里没有失败体对应的注册行。

**位置**：`ProtocolModule.RegisterOutboundEncoders` 里只有
`GameLoginResponse → GameLoginResponseEncoder`；`AGENTS.md` 的"诚实清单"已记此事。

**为什么没动**：属于协议行为缺口，不是重构范围。

---

## 7. 注释里描述"另一个编译单元的事实"，编译器只管 `cref`

**现象**：跨程序集搬迁后，一批散文注释变成了无法在本处核实的断言，例如
"命令字节不再在这里检查，因为所有 Game 解包器都注册在 `0x01` 下"——`0x01` 是
协议侧的事实，硬编码在传输侧，两边分离后没有任何东西维护这个一致性。
`<see cref="..."/>` 会被编译器验证，纯散文不会。

**位置**：本次已归位的两处——`Dfo.Server.Networking/Runtime/NetworkPlugins.cs`
的 `Decode(GameClientFrameRequestInfo)` remarks（现在只陈述传输侧能自证的部分），
与 `ProtocolModule.RegisterInboundDecoders` remarks（协议侧断言，带
`<see cref="Game.GameProtocol.Command"/>` 符号引用）。

**为什么没动**：已经处理了这一处。记在这里是因为**这是个会复发的类别**——
每次跨程序集移动代码，都要重新排查一遍散文注释，而没有任何自动化能提醒。

**可选处置**：约定跨单元的事实一律用 `cref` 表达，不能用 `cref` 表达的就不写。

---

## 8. 测试仍按功能组织，源码已按协议切片

**现象**：源码现在是 `Protocol/Game/Login/` 六件套一屏可见，但
`GameLoginTests` 在 `Application.Tests/Game/`，`GameLoginCodecTests` 在
`Protocol.Tests/Game/Login/`——同一个协议的测试分散在两个测试项目里。
`Dfo.Server.Application.Tests` 因此必须引用 `Dfo.Server.Protocol`。

**位置**：`Server/tests/Dfo.Server.Application.Tests/`——17 个 `.cs` 文件因本次迁移
被改动，其中 16 个需要补 `using Dfo.Server.Protocol.*`；
`Server/tests/Dfo.Server.Protocol.Tests/`。

**为什么没动**：这是 spec-006 里**有意识的取舍**。阻碍是具体的：
`Support/EntryServices.cs` 是与领域测试共享的脚手架；
`SelectCharacterLifecycleTests` 同时验证命令 handler 与领域生命周期；
`ExitFlushHandlerTests` 横跨三个协议。拆分它们的风险大于测试目录对齐的收益。

**可选处置**：单独一个 spec 处理测试重排，先拆共享脚手架。

---

## 9. handler 在总线之外持有应用层协作者

**现象**：普通协议 handler 通常只在总线之外读取连接身份；
`SelectCharacterHandler` 则是显式批准的多阶段协议工作流协调者。除
`IOnlineUserQueryDomainService`、`IChannelEntryPolicy` 和 `ICommandBus` 外，它还持有
`SelectedSessionTransitionGate`、`ISelectedSessionDomainService`、
`INetworkMessageSender` 与 `INetworkConnectionCloser`。

**位置**：`Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs`。

**为什么这样设计**：`01/0004` 接受之后需要按既有顺序发送多组可复用状态消息，
而每日补货、发布、PVP、任务和宠物又各自属于不同领域阶段。handler 因而在
一个 transition-gate lease 内顺序派发多个顶层命令、检查 session 仍为 current，
并协调具体协议消息的发送和失败补偿；命令 handler 仍禁止嵌套 dispatch，也不持有
sender、closer 或 socket。连接关闭刻意在 lease 释放后执行，避免 disconnect callback
与同一 gate 互等。

这条边界不应推广成所有 handler 的默认形态；它是 SelectCharacter 一入站对应多阶段
状态同步的明确工作流职责，不是 Networking 或 Data 应承载的协议例外。

---

## 10. ~~`SelectCharacterStaticBodyFamilies` 巨型静态字节表~~（已处理）

2026-08-15 的状态同步重构已删除该文件。原 58 位兼容序列现在由具体
`GameStateSynchronizationMessage` 实例组成，body 编码按领域拆入
`Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.*.cs`；完整帧
hash 与顺序测试保留了原行为。这里保留条目用于说明历史问题已关闭。

---

## 11. `Dfo.Server.Utilities` 是空占位项目

**现象**：项目存在、参与构建、无任何内容。

**位置**：`Server/src/Dfo.Server.Utilities/`。

**为什么没动**：与本次重构无关。`AGENTS.md` 已标注为空占位。

---

## 12. 命令注册表没有启动完备性检查，消息注册表有闭合性检查

**现象**：两套注册表的启动校验强度不对称——消息侧会在启动时发现"解包器能产出但
没人接"，命令侧要等到 dispatch 一个未注册命令时才报错。

**位置**：`Dfo.Server.Application/Dispatching/MessageHandlerRegistry.cs`
构造函数中的闭合性检查；`Dfo.Server.Application/Commands/CommandHandlerRegistry.cs`。

**为什么没动**：这是既有形态，`AGENTS.md` 已记录。命令的键空间是封闭的
（服务器自己选），性质上和线级键不同，是否需要同等强度的启动检查是个待定问题。

---

## 13. ~~SelectCharacter 投影投递横跨三个编译单元~~（已处理）

2026-08-15 已用 `ExplicitMessageDelivery` / `ExplicitMessageTarget` 替换专用接缝。
调用者提交具体 `IServerNetworkMessage` 和已解析的连接 ID；Networking 只执行
必达顺序、endpoint 校验、owner 排除和逐目标尽力隔离。源码扫描要求
`Dfo.Server.Networking` 与 `Dfo.Server.Abstractions/Networking` 中不存在
`SelectCharacter` 或 `Projection`。协议特有的消息构造、PVP/清理含义和收件人发现
均留在 Application/Protocol。

---

## 14. ~~投影组合器内部 16 分支类型 switch~~（已处理）

`ISelectCharacterSupplementalProjection`、`SelectCharacterProjectionComposer` 和类型
switch 已删除。X-02–X-18 由具体 `SupplementalSynchronizationMessage` 子类型表达，
每个 CLR 消息类型恰好注册一个 Game encoder。可选性由“是否创建消息”表达；一项
语义本来就包含相邻多帧时，单个具体消息可以返回固定有序帧组。
