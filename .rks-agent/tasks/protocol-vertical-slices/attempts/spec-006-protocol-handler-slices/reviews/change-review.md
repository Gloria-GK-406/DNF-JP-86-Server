---
kind: rks-review
decision: approved
blocking_findings: 0
---

# spec-006 完整改动复核

复核方式：独立子代理，对抗性取向——任务是**证伪**"纯搬迁、零逻辑变更"这一主张，
而非确认它。输入为 TaskContract（CSC-1..CSC-11）、spec.md（RQ/AC）、plan.md（BD/T/CK）
与工作树中全部未提交改动（`git status --short` / `git diff -M` / 新增未跟踪文件直读）。
基线 `7545921`。复核方未运行构建与测试，未修改任何文件。

## 结论

**未发现行为改动。** "纯搬迁"主张在全部可执行代码上成立。

机械核对的内容：

- **注册（第 1 优先级）**：`ProtocolModule.cs` 的 14 条 `RegisterMessageHandler`
  与 8 条 `AddCommandHandler` 与从 `ApplicationModule.cs` 删除的行逐字节相同，
  相对顺序相同，`ClientMessageType` 值相同，`CommandExecutionKind` 相同，
  位置参数与具名参数的用法相同——包括 `LoginGameUserCommand` 那条刻意用位置参数
  传 `UserAccessMode.Exclusive`（即 onlineUser 模式），而另两条用具名
  `accountAccessMode:`。无新增、无遗漏。`ApplicationModule` 保留
  `AddCommandDispatching()` 与 `AddNetworkMessageDispatching()`（RQ-004 满足）。
  跨模块注册顺序不构成风险：`AddCommandHandler`/`RegisterMessageHandler` 用的是
  `AddScoped`/`AddSingleton`/`AddTransient` 而非 `TryAdd`，注册表由收集到的
  `IEnumerable` 构建；两个 `IHostedService` 校验服务仍在 `ApplicationModule` 中
  先于 `NetworkingModule` 的监听器注册，启动校验仍早于开始监听。
- **消息 DTO**：`git show 7545921:.../NetworkMessages.cs` 与 15 个新
  `Messages.cs` 加存留的 `NetworkMessages.cs` 逐块比对——**35 个类型全部在位、
  正文全部相同**，无记录参数重排。`NetworkMessages.cs` 恰剩 6 项身份契约
  （CSC-2 / RQ-002 满足）。
- **`SelectCharacterCommand.cs` 拆分（第 2 优先级）**：765 行 → 292 + 497。
  归一化后的合并 diff 显示**唯一变化是 `ISelectCharacterPreparationService`
  从"命令 record 与命令 handler 之间"移到了 Application 侧文件顶部**。
  无声明丢失、重复或改动。接口必须留在 Application：其实现留在 Application，
  而 `Application` 不能引用 `Protocol`。
- **`ChannelControlMessageHandlers.cs` / `ChannelControlCommands.cs` 拆分**：
  分别 3 个与 9 个类型，正文全部相同。
- **其余被移动文件**：diff 仅为 namespace 与 using 行。
- **分层（第 4 优先级）**：csproj 图为 `Data → ∅`、`Abstractions → Data`、
  `Networking → Abstractions, Data`、`Application → Abstractions, Data`、
  `Protocol → Abstractions, Application, Data, Networking`。
  在 Networking/Abstractions/Data 源码中 grep `Dfo.Server.Protocol`：零命中；
  在 Networking 中 grep `Dfo.Server.Application`：零命中。CSC-4 满足。
- **连接观察者（第 3 优先级）**：仍是
  `TryAddEnumerable(ServiceDescriptor.Singleton<INetworkConnectionObserver, OnlineUserConnectionObserver>())`；
  它是生产代码中唯一的 `INetworkConnectionObserver`；`OutboundSendFailureTests`
  里测试自己的捕获实现仍在三个模块之后加入，枚举顺序未变。正文未改。
- **测试**：全部测试 diff 仅为 using/namespace，另加
  `CharacterEntryInventoryTests.cs` 一处全限定名修正与一条 `ProjectReference`。
  **断言零改动**（RQ-011 满足）。

## 发现与处置

均为文档与注释的准确性缺陷，无一涉及可执行代码。全部已修复。

| # | 发现 | 违反 | 处置 |
|---|---|---|---|
| F-1 | `AGENTS.md` 请求生命周期图仍写 `ClientMessageHandler<TMessage>（Application/Game\|Channel）`，而该目录已不存在；同段还把 payload codec 归给 `NetworkPlugins.cs`、把编码归给 `TouchSocketNetworkMessageSender`，与同文件新写的"传输侧不认识任何具体协议"自相矛盾 | CSC-8 / RQ-008 / AC-008 | 已重写整张图：解包器与编码器归到 `Protocol/<Endpoint>/<Protocol>/`，传输侧只查注册表，命令总线机制在 Application 而命令 handler 在切片内 |
| F-2 | 四处注释仍称 `Networking` 拥有实际住在 `Protocol` 的代码：`Data/Game/SelectCharacterSupplementalModels.cs` 两处、`Abstractions/Networking/INetworkServices.cs` 两处。实际拥有者是 `Protocol/Game/SelectCharacter/SelectCharacterProjectionComposer.cs`；`TouchSocketNetworkMessageSender` 只调 `composer.Compose(...)` 并发出 `byte[]` | RQ-012 / AC-013 / CSC-5 | 四处全部改写为本处可核实的陈述；`INetworkServices.cs` 那两处改用 `<see cref="ISelectCharacterProjectionComposer"/>` 与"实现随协议走"的说法 |
| F-3 | `Docs/ProtocolSliceArchitectureNotes.md` 第 9 条写"`SelectCharacterHandler` 是唯一直接调应用服务的 handler"与"唯一需要重新处理的一行"。实际另有 8 个 handler 在总线之外调 `IOnlineUserQueryService.TryGetByConnection` | RQ-013 / AC-014 | 第 9 条改写为"9 个 handler 在总线之外持有应用层协作者"，区分"8 处身份解析"与"1 处 `IChannelEntryPolicy.ResolveAsync`"，并说明规则收紧后这 9 处都要重新处理；`completion-evidence.md` 同处一并订正 |
| F-4 | 同文档"13 个文件涉及已迁类型"沿用自 plan，未重数 | RQ-013 | 实测为 17 个 `.cs` 被改动、其中 16 个需补 `using Dfo.Server.Protocol.*`，已订正 |
| F-5 | 两处归位后的注释仍指向另一编译单元：`NetworkPlugins.cs` 声称"该断言写在解包器注册处"，`GameLoginMessageDecoder.cs` 声称"调用方会丢弃该帧" | RQ-012（低） | 保留。两者都是**指针**而非重复的值，不会像 `0x01` 那样静默漂移；复核方明确认定 RQ-009 已满足（硬编码副本已删，存留的断言可由下方两行 `decoders.Find(frame.Command, frame.Type)` 自证，协议侧断言已带真实 `<see cref="Game.GameProtocol.Command"/>`） |

复核方另列的非阻断观察，处置如下：

- **`ApplicationModule` 残留 5 条失效 using**（`Abstractions.Networking`、
  `Application.OnlineUsers`、`Data.Characters`、`Data.Game`、`Data.Networking`）。
  BD-005 本就写明"删除…及失效 using"，属边界内遗漏。**已删除**，编译仍 0 警告。
- **`SelectCharacterCommand.cs` 拆分接缝处多一个空行**。**已合并**。
- **`OnlineUserConnectionObserver` 的可见性放宽不是最小的**：`ProtocolModule`
  同程序集，`internal` 本可满足注册；`public` 是被
  `Dfo.Server.Application.Tests` 直接构造它逼出来的，加第二条
  `InternalsVisibleTo` 会是更小的改动。**保留 `public`**：让
  `Dfo.Server.Application.Tests` 窥视 `Dfo.Server.Protocol` 的内部，是把"测试仍按
  功能组织"这一既有不对称固化成程序集特权，比放宽一个 DI 注册类型的可见性更糟。
  RQ-006 明确允许可见性变更。该取舍记入
  `Docs/ProtocolSliceArchitectureNotes.md` 第 3、8 条。
- **`OnlineUserConnectionObserver` 在证据中被列在"endpoint 层（跨切片共用）"下，
  但它并非跨切片共用件**，而是 `GameInitialConnectionNotice` 的唯一产出方。
  **已在证据中加注说明**其实际性质与停在上一级目录的原因。

## 复核后重跑

修复上述项后重跑：构建 0 警告 0 错误；全量测试 580 项 0 失败。
本次复核的全部改动只触及注释、Markdown 与失效 using，未改动任何可执行语句。

无未解决的阻断发现。
