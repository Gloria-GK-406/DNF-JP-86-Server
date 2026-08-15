---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Protocol Vertical Slices

## Context

### Source Request

用户在审阅 socket 层时指出两个问题并授权重构：「socket解包逻辑没有用DI给消除switch。可以看看commandbus怎么注册的，来完成注册」；以及「我们的项目是以协议为单位来组织的。但是命令，解包器，分发器却是以功能为单位组织的问题……一个协议的解包器，包处理handler和可能存在的command和commandhandler都是文件夹级远离的。我希望能在结构熵将这三个收集到一个，按协议或者其他什么文件夹来分离。一个协议文件夹能同时看到解包逻辑，包处理分发逻辑和命令处理逻辑」。

对话中用户就形态作出四项决定：

1. **枚举保留**。「socket层的枚举不去是我个人喜好问题，对于协议类我希望有个地方能全量看到所有协议，所以习惯留一个枚举」「解包器要返回messagetype，以携带一些信息。直接用type我不太喜欢」——消息身份继续由 `ClientMessageType` / `ServerMessageType` 承载，不改为按 CLR 类型派发。
2. **完备性检查取消**。「目前的信息是服务端和客户端的message类型应该在600多个。可能不需要这个完备性机制了」——枚举承担协议全量目录角色后，其成员数远大于已接线数，"每个枚举成员必须有 handler"不再成立。
3. **protocol 层隔离编解码**。「这个包编解码我觉得用protocol来隔离是对的。至少后续的开发复杂度会被压缩到protocol和领域服务层」。
4. **handler 不得承载业务语义**。「handler不应该内部操作任何业务语义。只要有业务语义的行为必须通过业务服务来实现。可以防止handler做了业务修改但是领域服务层根本不知道的情况。这是一个隐性的bug滋生点」。

### Current Situation

一个协议的事实被声明五遍，散在三个项目：线上标识在 `Networking/Framing/GameProtocol.cs`，解包在 `Networking/Framing/*PayloadCodec.cs`，线上标识到消息的映射在 `Networking/Runtime/NetworkPlugins.cs`，消息 DTO 与枚举在 `Data/Networking/NetworkMessages.cs`，出站编码在 `Networking/Runtime/TouchSocketNetworkMessageSender.cs`，消息 handler 在 `Application/Game|Channel/`，命令与命令 handler 在 `Application/Commands/Game|Channel/`，注册在 `Application/ApplicationModule.cs` 的两段列表里。

三处按消息分支的 switch 共约 23 个分支：`ChannelNetworkPlugin.CreateMessage` 4 个、`GameNetworkPlugin.CreateMessage` 10 个、`TouchSocketNetworkMessageSender.SendAsync` 9 个。新增一个协议因此要跨四个项目改五处（见 AGENTS.md「新增一个协议的固定动作」）。

线上帧键与语义消息键是两个不同的键空间，现有代码已有实证：`GET_USERINFO` 线上只有 `0x0008` 一个号，按 mode 字节分流为两个语义消息，其中 `GameGetAccountCharacterRoster = 0x0001_0208` 是合成键，线上不存在该号。因此解包器必须按线上帧键索引，其产出的 `ClientMessageType` 是解包结果而非索引键。

`MessageHandlerRegistry` 现有启动完备性检查要求 `ClientMessageType` 每个成员都有 handler。当前 14 个入站协议（Channel 4 + Game 10）恰好全部接线，故检查成立；在 600+ 类型的目标规模下不再成立。

命令侧已在 `typed-command-dispatch` 任务中完成同类改造（枚举键改为 CLR 类型键，`AddCommandHandler<TCommand,TResult,THandler>` 三参数注册），其结论明确将消息侧列为不受质疑的范围外——本任务是对消息侧的独立处理，且因上述键空间差异不照搬其形态。

基线：解决方案编译 0 警告 0 错误（`TreatWarningsAsErrors` 开启）；全量测试 563 项，498 通过、65 跳过、0 失败（Pvf 60、Application 265、Networking 113、Infrastructure 121、Host 4）。工作区干净，位于 `main`。

## Goal

一个协议的入站解包、出站编码、消息处理与命令处理位于同一个目录中，可一处通览；协议的接线由 DI 注册完成，新增协议不需要修改任何既有文件；消息身份继续由枚举承载，该枚举成为可全量通览的协议目录；且服务端的线级可观察行为不变。

## Scope

### In

- 入站解包改为按线上帧键（endpoint + command + type / 消息号）注册的 DI 查表，消除两处 `CreateMessage` switch。
- 出站编码改为按消息注册的 DI 查表，消除 sender 中按消息 CLR 类型分支的 switch；sender 只保留组帧、按 endpoint 校验与发送。
- 移除 `MessageHandlerRegistry` 中「枚举成员必须全部注册」的启动完备性检查。
- 保留 `ClientMessageType` / `ServerMessageType`，使其成为协议全量目录兼派发键；解包器产出携带该身份的消息。
- 新建 `Dfo.Server.Protocol` 项目，按协议建立切片目录；将各协议的消息 DTO、入站解包、出站编码、消息 handler、命令与命令 handler 迁入其对应切片目录。
- 迁移受影响的测试至对应位置（含 `Networking.Tests/Framing/` 下的 codec 向量测试）。
- 更新 AGENTS.md：新的项目职责与引用图、「新增一个协议的固定动作」、以及切片可拥有与不可拥有内容的边界规则。

### Out

- 删除 `ClientMessageType` / `ServerMessageType` 枚举，或改为按 CLR 类型派发消息。已由用户明确否决。
- 抽取共享的字段读写原语（D-string、定长 ASCII、计数列表等）与统一越界语义。重复模式需待切片落地后才可观察，此时抽取属猜测。
- 任何协议结论的变更，以及 `protocol/` 知识库的内容改动。
- 命令总线本身、管线行为、执行闸门与 access mode 取值。
- 领域服务、仓储、PVF 内容源与 `Tool/`、`ClientHack/`。
- 新增协议、新增命令，或对现有协议语义的修改。

## Constraints

- 线级可观察行为必须不变：未知线上 type 静默丢弃、出站按 endpoint 校验、Channel 载荷 AES-128-ECB + zlib + 当日密钥、Game 出站 15 字节头、`SelectCharacter` 的 59 帧静态流与补充投影的有序投递及其失败补偿次序、登录失败体仍不可编码。
- 切片重排步骤不得夹带逻辑变更：除文件位置、命名空间、using 与可见性修饰外，其 diff 不得包含行为改动。
- 消息 handler 不得直接实施业务语义或状态迁移，须经命令与领域服务到达。此为用户明确要求，理由是 handler 私改状态而领域服务不知情构成双写，属隐性缺陷来源。
- 编译期分层不得放宽：`Dfo.Server.Networking` 不得引用 `Dfo.Server.Application` 或 `Dfo.Server.Protocol`，只能经 `Dfo.Server.Abstractions` 的接缝到达它们。
- 不得以放宽类型、放宽可见性要求或抑制诊断的方式换取编译通过；`TreatWarningsAsErrors` 不得关闭。
- 既有 563 项测试须继续通过；仅当某测试断言的行为本身被本任务有意移除时方可修改其断言，且须在结果中说明。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 入站不存在按消息号或 type 选择消息构造的 switch，接线由注册完成 | 全仓检索 `CreateMessage` 及等价分支为空，加上解包注册表的代码审阅 |
| SC-2 | 出站不存在按消息 CLR 类型选择编码的 switch，sender 只做组帧、endpoint 校验与发送 | `TouchSocketNetworkMessageSender` 代码审阅，加上全仓检索无残留分支 |
| SC-3 | 新增一个协议的入站与出站接线只需新增文件与一行注册，不需修改任何既有文件 | 一个探针协议的完整接线 diff，仅含新增文件与注册行 |
| SC-4 | 消息身份仍由 `ClientMessageType` / `ServerMessageType` 承载，解包器产出携带该身份的消息 | 枚举仍存在并被派发使用的代码审阅 |
| SC-5 | 枚举中只登记而未接线的成员不导致启动失败 | 针对该情形的测试 |
| SC-6 | 已注册解包器产出的消息类型若无对应 handler，在启动时被发现而非在收到该消息时 | 针对该接线缺口的测试 |
| SC-7 | 每个已实现协议的消息 DTO、入站解包、出站编码、消息 handler、命令与命令 handler 位于同一目录 | 切片目录列示，对全部 14 个入站协议成立 |
| SC-8 | 编译期分层未放宽 | 各 csproj 引用图，`Dfo.Server.Networking` 不引用 Application 与 Protocol |
| SC-9 | 切片重排步骤未夹带逻辑变更 | 该步骤 diff 的逐文件审阅结论 |
| SC-10 | 线级可观察行为不变 | 既有集成测试（GameLogin、ChannelConnect、ChannelControlMessages、GameExit、SelectCharacter 投递）通过且断言未改 |
| SC-11 | 消息 handler 不直接实施业务语义或状态迁移 | 全部消息 handler 的代码审阅结论，加上 AGENTS.md 中该边界规则的记录 |
| SC-12 | 无回归 | 全量测试运行结果对齐 563 项基线，0 失败，并说明任何断言改动 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 承接 Agent 提出的三步方案（入站解包 DI 化 / 出站编码 DI 化 / 切片重排到新 `Dfo.Server.Protocol` 项目）及「要我按这个开始吗」之问，用户答「按ssd跑完吧。可以选multispec来跑，一个任务多个spec。或者如果你喜欢也可以多个spec单独跑。」；形态决定另见 Context 所引四条原话 | 在本仓库的任务工作区内，按上述三个成果完成 socket 编解码的 DI 化与协议切片重排，含相应测试迁移、AGENTS.md 更新，以及各成果一次已验证的本地结果提交 |

## Requires Confirmation

- 合并、推送、发起 PR，或对 `main` 与其他分支的写入。
- 抽取共享编解码原语，或对基础层的其他完善。
- 删除消息枚举或改为按 CLR 类型派发。
- `protocol/` 知识库与协议结论的任何改动。

## Assumptions

- 入站 payload codec 除 `GameLoginPayloadCodec` 外仅依赖 `Data` 的消息 DTO 与 `BinaryPrimitives`，不带传输依赖（已抽查 GetUserInfo、SelectCharacter、TaggedIdReport、RosterFollowUp 四个）。若迁移中发现反例，切片落点需重新评估。
- 拆帧与组帧（13B Game 头 / 11B Channel 头、TouchSocket 适配器、`FrameEncoders`）属传输，留在 `Dfo.Server.Networking`。`GameLoginOptions` 与 `ChannelPayloadCipher` 的归属由 Spec 决定，但不得因此使 Networking 反向依赖 Protocol。
- `ClientMessageType` 现有取值规则（Game 为 `cmd<<16|type`，Channel 为消息号，一型多义者取合成键）在目标规模下继续成立，本任务不改变既有取值。
- 600+ 消息类型是未来规模预期而非当前状态；本任务只处理现有 14 个入站协议与 9 个出站消息。
- `Dfo.Server.Utilities` 为空占位项目，本任务不涉及。
