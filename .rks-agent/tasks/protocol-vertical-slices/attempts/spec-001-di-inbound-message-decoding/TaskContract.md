---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-1
  - SC-3
  - SC-4
  - SC-5
  - SC-6
depends_on: []
---

# TaskContract: DI-Driven Inbound Message Decoding

## Context

### Source Request

根任务的第一个有界成果，由根控制器在 `safe-successor` 时确定：入站解包由 DI 注册表驱动。用户授权见根 TaskContract 的 Authorization 行；形态决定（枚举保留、完备性检查取消、解包器产出携带消息身份）见根 TaskContract 的 Context。

### Current Situation

入站解包目前由两个静态方法承担，位于 `Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs`：

- `ChannelNetworkPlugin.CreateMessage`（第 72-87 行，4 分支）：把 `frame.MessageNumber` 强转为 `ClientMessageType` 后 switch，产出四个只携带 `ChannelMessageMetadata` 的请求记录。Channel 侧**不解码 body**，消息号本身即触发器。
- `GameNetworkPlugin.CreateMessage`（第 111-142 行，10 分支）：先要求 `frame.Command == GameProtocol.Command`（否则返回 null），再按 `frame.Type` switch 到各 payload codec。

两处的返回值均为可空：null 表示"不是本服务端认识的消息"，调用方据此静默丢弃，不记日志、不抛异常。

十个 Game 分支中，九个已具备一致形状 `Decode(GameMessageMetadata, ReadOnlySpan<byte>) -> IClientNetworkMessage?`（其中 `SelectCharacterPayloadCodec` 与 `TaggedIdReportPayloadCodec` 等返回具体类型，可协变到接口）；`GameProtocol.ExitType` 分支不调用 codec，直接构造 `GameExitRequest`；只有 `GameLoginPayloadCodec.TryDecode(span, out loginId, out passwordHash)` 形状不同——它返回 bool 与两个 out 参数，消息由 plugin 组装。该 codec 同时承载出站的 `EncodeSuccess(GameLoginOptions)`，是入站 codec 中唯一依赖 `Networking.Configuration` 的一个。

线上帧键与语义消息键是两个键空间。`ClientMessageType` 的取值规则是 Game 取 `cmd<<16|type`、Channel 取消息号，看似可直接用作解包键，但 `GetUserInfoPayloadCodec` 证伪了这一点：线上 `0x0008` 一个号按 body 第 2 字节的 mode 分流，mode `0x02` 产出 `GameGetAccountCharacterRoster = 0x0001_0208`——该值是合成键，线上不存在该号；其余 mode 的产出目前为 null。因此解包必须按线上帧键索引，产出的 `ClientMessageType` 是解包结果而非索引键。

`MessageHandlerRegistry`（`Server/src/Dfo.Server.Application/Dispatching/MessageHandlerRegistry.cs` 第 26-33 行）现要求 `ClientMessageType` 每个成员都有 handler，否则启动即抛。当前 14 个成员恰好全部接线，故检查成立；在根契约记载的 600+ 类型目标规模下不再成立，因为枚举将承担协议全量目录角色，其成员数远大于已接线数。

覆盖入站解包的既有测试：`Dfo.Server.Networking.Tests/Framing/` 下的 codec 向量测试（`GameLoginCodecTests`、`GetUserInfoPayloadCodecTests`、`SelectCharacterPayloadCodecTests`、`ClientSpecStatisticPayloadCodecTests`、`ClientHardwareDetailPayloadCodecTests`、`ClientSpecProfilePayloadCodecTests`、`SessionMetricsPayloadCodecTests`、`SmallReportPayloadCodecTests`），以及 `Dfo.Server.Networking.Tests/Integration/` 下的四组线级集成测试。

接受基线 `06b24e7`：编译 0 警告 0 错误，全量测试 563 项 498 通过 / 65 跳过 / 0 失败。

## Goal

入站"线上帧键 → 类型化消息"的映射由 DI 注册的解包器提供，两处 `CreateMessage` 分支消失；接入或移除一个入站协议的解包只需增删该协议自己的文件与一行注册，不触及既有文件；已注册解包器与消息 handler 之间的接线缺口在启动时被发现，而只登记未接线的枚举成员不再导致启动失败；且现有 14 个入站协议的解码结果与静默丢弃行为逐字不变。

## Scope

### In

- 引入按线上帧键（endpoint + command + type，Channel 侧为消息号）索引的解包器注册与查表，覆盖现有 14 个入站协议。
- 消除 `ChannelNetworkPlugin.CreateMessage` 与 `GameNetworkPlugin.CreateMessage` 的分支；plugin 退化为拆帧 → 查表 → 派发。
- 将 `GameLoginPayloadCodec` 的入站解码归一到与其余解包器一致的形状；其出站 `EncodeSuccess` 保持原样不动。
- 移除 `MessageHandlerRegistry` 中「枚举成员必须全部注册」的启动检查，代之以「已注册解包器申报的产出消息类型必须有已注册 handler」的启动检查。
- 解包器的 DI 注册落点与 Networking 侧到达它的接缝（Networking 不得因此引用 Application）。
- 相应测试：既有 codec 向量测试的接入方式调整、注册与查表的单测、启动检查两个方向的测试。

### Out

- 出站编码与 `TouchSocketNetworkMessageSender` 的分支。属根任务第二个成果。
- 目录重排、新建项目、文件搬迁。属根任务第三个成果。
- 任何 payload codec 的字节语义、长度校验规则与异常措辞。
- `ClientMessageType` / `ServerMessageType` 的成员与取值、消息 DTO 的形状、消息 handler 与命令的任何改动。
- 帧的拆包与组包（`Framing/` 下的适配器、`*FrameRequestInfo`、`FrameEncoders`、`DnfFrameProtocol`）。
- 共享字段读写原语的抽取。

## Constraints

- 现有 14 个入站协议的解码结果必须逐字段不变，包括 `GET_USERINFO` 按 mode 分流为两个语义消息（mode `0x02` 产出账号花名册请求，其余产出 null）、各 codec 的长度校验与其现有异常类型及措辞。
- 静默丢弃语义不得改变：未注册的线上帧键、`frame.Command != GameProtocol.Command` 的 Game 帧、以及解包器返回 null 的情形，都必须继续被静默丢弃——不抛异常、不新增日志、不回送任何字节。
- 编译期分层不得放宽：`Dfo.Server.Networking` 不得引用 `Dfo.Server.Application`。
- 不得以放宽类型、放宽可见性要求或抑制诊断的方式换取编译通过；`TreatWarningsAsErrors` 不得关闭。
- 既有 563 项测试须继续通过；仅当某测试断言的行为本身被本成果有意移除时方可修改其断言，且须在结果中说明。移除枚举完备性检查所对应的断言属此列。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| CSC-1 | `Dfo.Server.Networking` 中不存在按消息号或 type 选择消息构造的分支 | `CreateMessage` 全仓检索为空，加上 plugin 的代码审阅 |
| CSC-2 | 14 个入站协议的解码结果与基线逐字段一致 | 既有 `Framing/` codec 向量测试与四组线级集成测试通过，断言未改 |
| CSC-3 | 未注册的线上帧键、非 `0x01` command 的 Game 帧、解包器返回 null 三种情形均被静默丢弃 | 覆盖三种情形的测试，断言无异常、无出站字节 |
| CSC-4 | 接入一个新入站协议只需新增该协议自己的文件与一行注册 | 一个探针协议的接入 diff，仅含新增文件与注册行 |
| CSC-5 | 只登记而未接线的 `ClientMessageType` 成员不导致启动失败 | 针对该情形的测试 |
| CSC-6 | 已注册解包器申报的产出消息类型若无对应 handler，启动即失败并指明缺口 | 针对该缺口的测试，断言异常指名缺失的消息类型 |
| CSC-7 | 编译期分层未放宽 | 各 csproj 引用图，`Dfo.Server.Networking` 不引用 `Dfo.Server.Application` |
| CSC-8 | 无回归 | 全量测试对齐 563 项基线，0 失败，并说明任何断言改动 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 复用根 TaskContract 的 `task` 行：承接 Agent 提出的三步方案后，用户答「按ssd跑完吧。可以选multispec来跑，一个任务多个spec。或者如果你喜欢也可以多个spec单独跑。」 | 在任务工作树 `codex/protocol-vertical-slices` 内完成入站解包的 DI 化及其测试，产出一次已审阅并已验证的本地结果提交。该成果完整落在根 Scope/In 第 1、3、4 条之内，未扩张根授权范围。 |

## Requires Confirmation

- 合并、推送、发起 PR，或对 `main` 与其他分支的写入。
- 任何超出本成果边界的改动（出站编码、目录重排、codec 字节语义）。

## Assumptions

- `frame.Body` 为 `ReadOnlyMemory<byte>`，解包在派发前的同步路径上完成，故解包器以 span 接收帧体不改变现有生命周期假设。
- Channel 侧四个协议不解码 body，其解包器为仅携带 `ChannelMessageMetadata` 的构造，无字节级风险。
- 除 `GameLoginPayloadCodec` 外的入站 codec 不依赖 `Networking.Configuration` 或任何传输类型（已抽查 GetUserInfo、SelectCharacter、TaggedIdReport、RosterFollowUp 四个）。若归一过程中发现反例，须在结果中记录，因其影响根任务第三个成果的切片落点。
- 基线中 `Dfo.Server.Host.Tests` 有一次未复现的瞬时失败（见根 map 的 `baseline_evidence`）。同类瞬时失败不得计为本成果的回归，但须在结果中如实记录其发生。
