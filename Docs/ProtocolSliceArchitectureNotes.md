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

**现象**：14 个 handler 里有 9 个在总线之外直接调应用层服务。

- 8 个调 `IOnlineUserQueryService.TryGetByConnection` 解连接身份
  （`GameLoginHandler`、`GetAccountCharacterRosterHandler`、`SelectCharacterHandler`
  与六个遥测 handler）。这是只读查询接口——会改状态的
  `RegisterConnection` / `TryDisconnect` / `TryDisconnectConnection` 在另一个接口
  `IOnlineUserService` 上，未注入任何 handler。
- 第 9 个是 `SelectCharacterHandler`，它额外调 `IChannelEntryPolicy.ResolveAsync`。
  这是**除身份解析之外**唯一一处 handler 直接调应用服务。这也是**只读**的
（`ChannelEntryPolicy.ResolveAsync` 只读频道目录并构造一个 `ChannelEntryIdentity`，
判定用的 `Refuse` 刻意没在这里调），所以按"翻译 + 纯读取"的口径它是合规的。

**位置**：`Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs`
第 28-29 行；`Dfo.Server.Application/Channel/ChannelEntryPolicy.cs` 第 18 行
（`ResolveAsync`，只读）与第 34 行（`Refuse`，判定，未被 handler 调用）。

**为什么记下来**：如果"handler 不碰业务语义"要收紧成"handler 只认识
`ICommandBus` 和 sender"，上面 9 处都要重新处理——8 处身份解析会变成必须由
dispatcher 预先解好并放进 `NetworkDispatchContext`，而
`IChannelEntryPolicy.ResolveAsync` 这一处得另想办法。现在的写法有明确理由
（`SelectCharacterHandler.cs` 第 26-27 行注释）：让监听端口留在传输侧，
命令把频道当作既定事实接收。

按当前口径（翻译 + 纯读取 + 派发）这 9 处全部合规，本条只是标出这条规则的
实际边界在哪里。

---

## 10. `SelectCharacterStaticBodyFamilies` 是一张约 843 行的静态字节表

**现象**：一个巨大的静态数组常量文件，混在 `SelectCharacter` 切片里，
和该切片的其余制品完全不是一类东西。

**位置**：`Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterStaticBodyFamilies.cs`。

**为什么没动**：它是逆向出来的线级数据，搬迁不改内容。

**可选处置**：改为资源文件或生成物，让切片目录只剩代码。

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

## 13. SelectCharacter 的投影投递仍横跨三个编译单元

**这是唯一一个制品没有完全收进自己切片的协议**，由根级复核独立发现——
本文档前 12 条漏了它。

**现象**：SelectCharacter 的投影投递链路分布在三处：

- `Abstractions/Networking/INetworkServices.cs`：`ExplicitProjectionTarget`、
  `SelectCharacterProjectionDelivery`、`INetworkMessageSender.SendProjectionAsync`、
  `ISelectCharacterProjectionComposer`——四个以 SelectCharacter 命名的成员
  待在本应通用的接缝里；
- `Networking/Runtime/TouchSocketNetworkMessageSender.cs`：约 90 行
  `SendProjectionAsync` / `DeliverProjectionAsync` / `ValidateProjectionDelivery`，
  含"必投先于尽力投"的次序、逐目标遇错即停的补偿、以及 22 行结构校验——
  全部是 SelectCharacter 独有的逻辑；
- 切片内只有 `SelectCharacterProjectionComposer` 与其 codec。

因此 `AGENTS.md` 项目表里"传输侧不含任何单个协议的知识"这句话，
必须带上这一个例外才成立（该行已相应改写）。

**为什么没动**：它先于本分支存在（基线上同样如此，且当时还多一个 9 分支
switch）。spec-005 已把投递指令重新归类为"不是消息"并迁入 Abstractions，
理由写在接缝处。把这条链路整体收进切片是一次接缝改动，需要重新决定
"sender 如何在不认识具体协议的前提下执行有序多目标投递"，属新设计。

**可选处置**：把有序投递抽象成与协议无关的能力（例如"一批已成帧的字节 +
一组收件人 + 投递策略"），让 sender 只认识这个抽象，SelectCharacter 特有的
次序与补偿规则回到切片里。

---

## 14. 投影组合器内部是一个 16 分支的类型 switch

**现象**：`SelectCharacterProjectionComposer.Compose` 用一个 16 分支的
`switch` 按投影的 CLR 类型选 codec 方法，默认分支抛异常——**形状恰是本次
重构要消除的那种**。

**位置**：`Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterProjectionComposer.cs`
第 16-52 行。

**为什么没动**：它是切片内部的，且分支的对象是**帧片段**而非消息——
消息级的分支确实已全部消除。它随 spec-003 原样搬迁，不是本分支引入的。
真要 DI 化，键空间是 `ISelectCharacterSupplementalProjection` 的实现类型，
与消息注册表是两套东西。

**已顺带修正**：该默认分支的异常文案原为 "has no Networking codec"，
是跨程序集的失真引用（第 7 条那一类），已改为 "has no codec in this slice"。
