---
kind: rks-verification
decision: pass
---

# spec-006 完成前验证

## 被评估的主张

spec-006「协议 handler 与命令切片」已完成：`Dfo.Server.Data` 的全部消息 record、
`Dfo.Server.Application` 的全部消息 handler 与协议命令已迁入
`Dfo.Server.Protocol` 的按协议切片目录；协议接线集中到 `ProtocolModule`；
迁移部分无逻辑变更；线级可观察行为不变；文档已更新。

工作树 `D:/project/DNF/DNF-JP-86-Server/.worktrees/protocol-vertical-slices`，
分支 `codex/protocol-vertical-slices`，基线 `7545921`，改动未提交。

## 条件到证据的映射

| CSC | 结论 | 证据 |
|---|---|---|
| CSC-1 | 满足 | 见下"切片目录列示"。15 个切片（Channel 4 + Game 11）各自持有其应有制品；14 个入站协议每一个的消息、解包器、消息 handler 同处一个目录 |
| CSC-2 | 满足 | 见下"`NetworkMessages.cs` 剩余内容"。恰为 6 项协议身份契约，无任何具体消息 record |
| CSC-3 | 满足 | `ProtocolModule.cs` 现含 4 类注册：命令 8 条、消息 handler 14 条、入站解包器 14 条、出站编码器 9 条，另加投影组合器与连接观察者各 1 条；`ApplicationModule.ConfigureServices` 只剩领域服务注册 + `AddCommandDispatching()` + `AddNetworkMessageDispatching()`，无任何 `RegisterMessageHandler` 或协议 `AddCommandHandler` |
| CSC-4 | 满足 | 见下"CK-008 引用图" |
| CSC-5 | 满足 | 见下"CK-006 逐项内容比对"：15 个整文件 + 29 个消息 record 全部逐字一致（模掉命名空间/using/可见性）。注释改动单独列于"注释改动清单" |
| CSC-6 | 满足 | 全量测试 580 项 0 失败；测试非 using 类改动仅 3 行（见下"CK-011"），断言零改动 |
| CSC-7 | 满足 | 见下"14 个 handler 逐个审计"。全部合规，无违规项 |
| CSC-8 | 满足 | `AGENTS.md` 项目表与引用图、"### 3) Socket 层"、"### 切片边界规则"、"### handler 业务语义规则"、"## 快速开发：新增一个协议的固定动作" |
| CSC-9 | 满足 | 见下"注释改动清单"第 1、2 条 |
| CSC-10 | 满足 | 全量测试 580 项（51+256+45+84+75+4 通过 / 65 跳过 / 0 失败），对齐基线 |
| CSC-11 | 满足 | `Docs/ProtocolSliceArchitectureNotes.md`，12 条，每条含现象 / 可核对位置 / 为什么没动 / 可选处置；该文档不改动任何代码 |

## 切片目录列示（CSC-1 / CK-015）

```
Channel/Connect/         ChannelConnectHandler · ChannelConnectMessageDecoder ·
                         ChannelConnectResponseEncoder · EstablishChannelConnectionCommand · Messages
Channel/Info/            ChannelInfoHandler · ChannelInfoMessageDecoder ·
                         ChannelInfoResponseEncoder · GetChannelInfoCommand · Messages
Channel/Script/          ChannelScriptHandler · ChannelScriptMessageDecoder ·
                         ChannelScriptResponseEncoder · GetChannelScriptCommand · Messages
Channel/ScriptVersion/   ChannelScriptVersionHandler · ChannelScriptVersionMessageDecoder ·
                         ChannelScriptVersionResponseEncoder · GetChannelScriptVersionCommand · Messages
Game/AccountRoster/      AccountCharacterRosterPayloadCodec · AccountCharacterRosterResponseEncoder ·
                         GetAccountCharacterRosterCommand · GetAccountCharacterRosterHandler ·
                         GetUserInfoMessageDecoder · GetUserInfoPayloadCodec · Messages
Game/ClientHardwareDetail/ ClientHardwareDetailHandler · ClientHardwareDetailMessageDecoder ·
                         ClientHardwareDetailPayloadCodec · Messages
Game/ClientSpecProfile/  ClientSpecProfileHandler · ClientSpecProfileMessageDecoder ·
                         ClientSpecProfilePayloadCodec · Messages
Game/ClientSpecStatistic/ ClientSpecStatisticHandler · ClientSpecStatisticMessageDecoder ·
                         ClientSpecStatisticPayloadCodec · Messages
Game/Exit/               AcknowledgeGameExitCommand · GameExitHandler · GameExitMessageDecoder ·
                         GameExitResponseEncoder · Messages
Game/InitialConnection/  GameInitialConnectionNoticeEncoder · GameInitialConnectionPayloadCodec · Messages
Game/Login/              GameLoginHandler · GameLoginMessageDecoder · GameLoginPayloadCodec ·
                         GameLoginResponseEncoder · LoginGameUserCommand · Messages
Game/RosterFollowUp/     Messages · RosterFollowUpHandler · RosterFollowUpMessageDecoder ·
                         RosterFollowUpPayloadCodec
Game/SelectCharacter/    Messages · SelectCharacterCommand · SelectCharacterHandler ·
                         SelectCharacterMessageDecoder · SelectCharacterPayloadCodec ·
                         SelectCharacterProjectionComposer · SelectCharacterResponseComposer ·
                         SelectCharacterResponseEncoder · SelectCharacterStaticBodyFamilies ·
                         SelectCharacterStaticPayloadCodec · SelectCharacterStaticSequence ·
                         SelectCharacterSupplementalProjectionCodec
Game/SessionMetrics/     Messages · SessionMetricsHandler · SessionMetricsMessageDecoder ·
                         SessionMetricsPayloadCodec
Game/TaggedIdReport/     Messages · TaggedIdReportHandler · TaggedIdReportMessageDecoder ·
                         TaggedIdReportPayloadCodec

endpoint 层：
Channel/ChannelProtocol · Channel/ChannelEncryptedFrame   （跨切片共用的线级常量）
Game/GameProtocol                                          （同上）
Game/OnlineUserConnectionObserver                          （见下注）
根：ProtocolModule
```

注（`OnlineUserConnectionObserver`）：它不是跨切片共用件，而是
`GameInitialConnectionNotice` 的唯一产出方——实际上就是 `Game/InitialConnection`
的"handler"，只是停在上一级目录。放在 endpoint 层是因为它另一半（断开清理）
与该切片无关，且它整体带着业务语义。这一条本身是不和谐点，见
`Docs/ProtocolSliceArchitectureNotes.md` 第 3 条。

`Game/InitialConnection` 是纯出站协议，故无解包器与消息 handler。
六个遥测协议（ClientSpecStatistic / ClientHardwareDetail / ClientSpecProfile /
SessionMetrics / TaggedIdReport / RosterFollowUp）只记日志、无领域变更、无响应，
故无命令与出站编码器。

## `NetworkMessages.cs` 剩余内容（CSC-2 / CK-007）

| 声明 | 归属理由 |
|---|---|
| `enum ClientMessageType` | 入站目录：服务器自己的协议总账 |
| `enum ServerMessageType` | 出站目录：同上 |
| `interface IClientNetworkMessage` | 入站消息的身份契约 |
| `interface IServerNetworkMessage` | 出站消息的身份契约 |
| `record ChannelMessageMetadata` | Channel 帧头元数据，全部 Channel 协议共用 |
| `record GameMessageMetadata` | Game 帧头元数据，全部 Game 协议共用 |

无第七项。文件从 274 行降至 60 行。

## CK-006 逐项内容比对（CSC-5）

脚本化比对：把当前文件与 `git show 7545921:<旧路径>` 的内容各自剥去 using 与
namespace 行、忽略空行、把 `public/internal sealed class X` 归一化后逐行相等。

15 个整文件全部通过：

```
OK  Protocol/Game/Exit/GameExitHandler.cs                            17 lines
OK  Protocol/Game/Login/GameLoginHandler.cs                          28 lines
OK  Protocol/Game/AccountRoster/GetAccountCharacterRosterHandler.cs  25 lines
OK  Protocol/Game/SelectCharacter/SelectCharacterHandler.cs          29 lines
OK  Protocol/Game/ClientSpecStatistic/ClientSpecStatisticHandler.cs  102 lines
OK  Protocol/Game/ClientHardwareDetail/ClientHardwareDetailHandler.cs 76 lines
OK  Protocol/Game/ClientSpecProfile/ClientSpecProfileHandler.cs      83 lines
OK  Protocol/Game/SessionMetrics/SessionMetricsHandler.cs            48 lines
OK  Protocol/Game/TaggedIdReport/TaggedIdReportHandler.cs            28 lines
OK  Protocol/Game/RosterFollowUp/RosterFollowUpHandler.cs            31 lines
OK  Protocol/Game/Exit/AcknowledgeGameExitCommand.cs                 11 lines
OK  Protocol/Game/Login/LoginGameUserCommand.cs                      21 lines
OK  Protocol/Game/AccountRoster/GetAccountCharacterRosterCommand.cs  11 lines
OK  Protocol/Game/OnlineUserConnectionObserver.cs                    87 lines
OK  SPLIT SelectCharacterCommand.cs                                  696 lines
OK  MESSAGES (15 个切片 Messages.cs)                                 29 records
```

- `SPLIT` 行：`SelectCharacterCommand.cs`（765 行）拆为
  `Protocol/Game/SelectCharacter/SelectCharacterCommand.cs`（命令 record +
  `SelectCharacterCommandHandler`）与
  `Application/Characters/SelectCharacterPreparationService.cs`
  （`ISelectCharacterPreparationService` + `SelectCharacterPreparationService`）。
  两半合并后与基线逐行相等。接口留在 Application 是必需的：实现类留在
  Application，而 `Application` 不能引用 `Protocol`。
- `MESSAGES` 行：29 个消息 record（含成分类型）逐个在基线中定位其完整块
  （含前置文档注释），要求该块在且仅在一个切片 `Messages.cs` 中原样出现；
  并要求切片文件中不存在任何不属于某个基线块的行。

可见性放宽仅一处：`OnlineUserConnectionObserver` 由 `internal` 改 `public`。
它必须迁出 Application（连接建立时构造 `GameInitialConnectionNotice`，
而该 DTO 已在切片内，`Application` 不能引用 `Protocol`），迁入后
`Dfo.Server.Application.Tests` 的既有测试需要构造它，而 `Dfo.Server.Protocol`
的 `InternalsVisibleTo` 只对 `Dfo.Server.Protocol.Tests` 开放。
spec RQ-006 明确允许可见性修饰变更。

## CK-008 引用图（CSC-4）

```
Dfo.Server.Networking.csproj   -> Abstractions, Data
Dfo.Server.Abstractions.csproj -> Data
Dfo.Server.Data.csproj         -> （无）
```

三者均不引用 `Dfo.Server.Protocol`；`Networking` 亦不引用 `Application`。
`Dfo.Server.Protocol.csproj` -> Abstractions, Application, Data, Networking。
方向单向，无环。

## CK-011 测试改动（CSC-6）

`git diff -- Server/tests/` 中排除 `using` 行与空行后，非 using 类改动共 3 行：

```
-  var result = await Dfo.Server.Application.Commands.Game.SelectCharacterPreparationService
+  var result = await Dfo.Server.Application.Characters.SelectCharacterPreparationService
+  <ProjectReference Include="..\..\src\Dfo.Server.Protocol\Dfo.Server.Protocol.csproj" />
```

第一处是全限定名跟随类型迁移，第二处是测试项目引用。**断言零改动。**
测试文件位置未动：测试仍按功能组织，源码已按协议切片，此不对称是有意取舍，
已记入 `Docs/ProtocolSliceArchitectureNotes.md` 第 8 条。

## 注释改动清单（CSC-5 / CSC-9）

| # | 位置 | 改动 |
|---|---|---|
| 1 | `Networking/Runtime/NetworkPlugins.cs` `Decode(GameClientFrameRequestInfo)` remarks | 删去 `<c>0x01</c>` 硬编码副本与"所有 Game 解包器都注册在 0x01 下"的协议侧断言；只留传输侧能自证的部分（command 是线级键的一部分，查不到即丢弃），并指出该断言在解包器注册处 |
| 2 | `Protocol/ProtocolModule.cs` `RegisterInboundDecoders` remarks | 新增协议侧断言，以 `<see cref="Game.GameProtocol.Command"/>` 符号引用取代硬编码常量 |
| 3 | `Protocol/ProtocolModule.cs` 类型 remarks | 原文"承载了原本在 networking 模块里的两张路由表，后者现在只管传输"是对另一编译单元的断言，改为陈述本处事实（六类制品同处一个协议目录，本模块是它们变为可达的地方） |
| 4 | `Protocol/Game/Login/GameLoginMessageDecoder.cs` remarks | 原文"复现了 plugin 原本的行为"引用已删除的 switch，无处可核实；改为陈述本处事实（解码失败返回 null，即本接口的"无消息"） |
| 5 | `Protocol/Game/RosterFollowUp/RosterFollowUpHandler.cs` `RosterFollowUpDescriptor` | 原文"为了让 Application 层不依赖 Networking"在同一程序集同一目录内已不成立；改为 `<see cref="RosterFollowUpPayloadCodec.ObservedSentinel"/>` 并陈述该副本已失去理由。常量本身未删（超出 RQ-006 允许的四类改动），记入不和谐点文档第 4 条 |

另：迁移脚本给 6 个遥测 handler 补入的 `using Dfo.Server.Application.Commands;`
在这些文件中未被使用，已移除。

## 14 个 handler 逐个审计（CSC-7 / CK-012）

独立子代理逐文件通读全部 14 个 handler 及其协作者接口后给出：

| # | Handler | 协作者与调用 | 读/写 | 结论 |
|---|---|---|---|---|
| 1 | `ChannelConnectHandler` | `commandBus.SendAsync(EstablishChannelConnectionCommand)`；`sender.SendAsync(ChannelConnectResponse)` | 命令派发；传输写 | 合规 |
| 2 | `ChannelScriptVersionHandler` | `commandBus.SendAsync(GetChannelScriptVersionCommand)`；`sender.SendAsync` | 读；传输写 | 合规 |
| 3 | `ChannelScriptHandler` | `commandBus.SendAsync(GetChannelScriptCommand)`；`sender.SendAsync` | 读；传输写 | 合规 |
| 4 | `ChannelInfoHandler` | `commandBus.SendAsync(GetChannelInfoCommand)`；`sender.SendAsync` | 读；传输写 | 合规 |
| 5 | `GameExitHandler` | `commandBus.SendAsync(AcknowledgeGameExitCommand)`；`sender.SendAsync` | 命令派发；传输写 | 合规 |
| 6 | `GameLoginHandler` | `onlineUsers.TryGetByConnection`；`commandBus.SendAsync(LoginGameUserCommand)`；`sender.SendAsync` | 读；命令派发；传输写 | 合规（认证状态迁移在命令内） |
| 7 | `GetAccountCharacterRosterHandler` | `onlineUsers.TryGetByConnection`；`commandBus.SendAsync(GetAccountCharacterRosterCommand)`；`sender.SendAsync` | 读；读；传输写 | 合规 |
| 8 | `SelectCharacterHandler` | `onlineUsers.TryGetByConnection`；`channels.ResolveAsync`；`commandBus.SendAsync(SelectCharacterCommand)` | 读；读（纯目录投影）；命令派发 | 合规（见下注） |
| 9 | `ClientSpecStatisticHandler` | `onlineUsers.TryGetByConnection`；`logger.LogInformation` + 静态描述器 | 读；日志 | 合规 |
| 10 | `ClientHardwareDetailHandler` | 同上 | 读；日志 | 合规 |
| 11 | `ClientSpecProfileHandler` | 同上 | 读；日志 | 合规 |
| 12 | `SessionMetricsHandler` | 同上 | 读；日志 | 合规 |
| 13 | `TaggedIdReportHandler` | 同上 | 读；日志 | 合规 |
| 14 | `RosterFollowUpHandler` | 同上 | 读；日志 | 合规 |

**违规项：无。** 没有任何 handler 自行改变状态，也没有任何 handler 直接调用会改变
状态的领域/应用服务；全部状态迁移路径都经 `commandBus.SendAsync`。

支撑事实：14 个 handler 均无字段、无可变成员；唯一被注入的在线用户协作者是
`IOnlineUserQueryService`（只暴露 `GetRequired` 与 `TryGetByConnection`），
会改变状态的 `RegisterConnection` / `TryDisconnect` / `TryDisconnectConnection`
在另一个接口 `IOnlineUserService` 上，未注入任何 handler；
遥测 handler 尾部的 `internal static class *Descriptor` 是纯 `switch` 与字符串格式化。

注（总线之外的应用层协作者）：14 个 handler 中有 9 个在总线之外直接调应用层服务。
其中 8 个只调 `IOnlineUserQueryService.TryGetByConnection` 解连接身份，
该接口只读。第 9 个 `SelectCharacterHandler` 额外调
`IChannelEntryPolicy.ResolveAsync`——这是除身份解析之外唯一一处；已核实它只读
频道目录并构造 `ChannelEntryIdentity`，作判定的兄弟方法 `Refuse` 刻意未在此调用。
按"翻译 + 纯读取 + 派发"的口径这 9 处全部合规；若规则收紧为"只认识
`ICommandBus` 与 sender"，这 9 处都要重新处理。已记入不和谐点文档第 9 条。

## 构建与测试（CSC-6 / CSC-10 / CK-004 / CK-005）

```
dotnet build DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/86JP.Server.slnx -nodereuse:false
  -> 已成功生成。0 警告 0 错误

dotnet test  DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/86JP.Server.slnx -nodereuse:false
  Dfo.Pvf.Tests                失败 0  通过  51  跳过  9  总计  60
  Dfo.Server.Application.Tests 失败 0  通过 256  跳过 10  总计 266
  Dfo.Server.Networking.Tests  失败 0  通过  45  跳过  0  总计  45
  Dfo.Server.Protocol.Tests    失败 0  通过  84  跳过  0  总计  84
  Dfo.Server.Infrastructure.Tests 失败 0 通过 75 跳过 46 总计 121
  Dfo.Server.Host.Tests        失败 0  通过   4  跳过  0  总计   4
  合计 580 项，0 失败，对齐基线
```

构建须以仓库外目录（`/d/project/DNF`）为 cwd 以绕过 `global.json` 的 SDK 锁定，
并传 `-nodereuse:false` 避免残留 MSBuild 节点锁定 `obj/`。

上表为复核修复之后的**新鲜**运行结果。

期间观察到两次单项失败，均为并发端口争用：一次落在 `Dfo.Server.Host.Tests`
（该项目拉起真实 exe 并绑定 TCP 端口），一次落在 `Dfo.Server.Networking.Tests`
（集成测试同样绑定端口）。两者由 `dotnet test` 并行调度，谁输掉端口竞争谁失败。
两次都单独重跑该项目全绿（分别 4/4、45/45），随后整套重跑 580/0 全绿，
最终一次整套运行亦为 580/0 全绿。属环境层面的并发抖动，非本次改动引入——
失败项在两次中不是同一个，且与被迁移代码无关。

## 复核

`reviews/change-review.md`，独立子代理，对抗性取向，`decision: approved`，
**阻断发现 0**。复核方独立机械核对了 22 条注册的逐字节一致性、35 个消息类型的
正文一致性、`SelectCharacterCommand.cs` 拆分的无损性、csproj 引用图与源码中的
跨层 grep、以及连接观察者注册形态与枚举顺序。

复核提出 5 项文档与注释的准确性缺陷（F-1..F-5）与 4 项非阻断观察，
其中 F-1..F-4 已修复，F-5 经论证后保留（指针而非重复值，RQ-009 已满足），
非阻断观察中的失效 using 与拆分接缝空行已修复，可见性取舍与观察者归类
已分别记录理由。逐项处置见复核件的"发现与处置"。

修复只触及注释、Markdown 与失效 using，未改动任何可执行语句；
修复后重跑构建 0 警告 0 错误、测试 580/0。

## 未验证条件

无。CSC-1..CSC-11 全部具备新鲜证据。
