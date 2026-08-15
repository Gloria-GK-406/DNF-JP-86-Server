---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-7
  - SC-8
  - SC-9
depends_on: []
---

# TaskContract: Protocol Codec Slices

## Context

### Source Request

根任务的第三个有界成果，由根控制器在接受 spec-002 后于 `safe-successor` 时确定：把协议编解码内容自 `Dfo.Server.Networking` 迁入新建的 `Dfo.Server.Protocol`，按协议建立切片目录。用户授权见根 TaskContract 的 Authorization 行。

### Current Situation

spec-001 与 spec-002 之后，`Dfo.Server.Networking` 内的内容已可按职责一分为二，且两侧不再互相纠缠：

**协议内容**（应迁走）——入站 9 个 `*PayloadCodec`、`Framing/Decoders/` 下 14 个解包器、`Framing/Encoders/` 下 9 个编码器与共用信封 `ChannelEncryptedFrame`、出站 `AccountCharacterRosterPayloadCodec` 与 `GameInitialConnectionPayloadCodec`、`SelectCharacter` 的五个组帧与投影件（`SelectCharacterResponseComposer`、`SelectCharacterStaticBodyFamilies`、`SelectCharacterStaticPayloadCodec`、`SelectCharacterStaticSequence`、`SelectCharacterSupplementalProjectionCodec`）、`SelectCharacterProjectionComposer`、线上标识 `GameProtocol` 与 `ChannelProtocol`，以及 `NetworkingModule` 中的 `RegisterInboundDecoders` 与 `RegisterOutboundEncoders` 两张路由表。

**传输**（应留下）——`DnfFrameProtocol`、`FrameEncoders`（`ChannelFrameEncoder` / `GameServerFrameEncoder`）、`FrameDataHandlingAdapters`、`FrameProtocolException`、两个 `*FrameRequestInfo`、`ChannelPayloadCipher`，以及 `Runtime/` 下的插件、三个注册表、sender、连接注册表与 TCP 服务。

四项勘察结论决定了本成果的形态：

1. **传输原语已经是 `public`。** `ChannelFrameEncoder`、`GameServerFrameEncoder`、`DnfFrameProtocol`、`ChannelPayloadCipher`、`GameLoginOptions` 均为 public，故切片项目引用 `Dfo.Server.Networking` 即可组帧与加密，无需放宽任何传输侧可见性，也无需 `InternalsVisibleTo`。
2. **编解码对 Networking 的依赖只剩传输原语。** 14 个解包器的 using 集合只有 `Abstractions.Networking` 与 `Data.Networking`；9 个编码器另加 `Abstractions.Channel` 与 `Networking.Configuration`（`GameLoginOptions`）。对 `FrameEncoders`、`DnfFrameProtocol`、`GameProtocol` 的引用因同处 `Dfo.Server.Networking.Framing` 父命名空间而未出现在 using 中，迁移后须显式化。
3. **消息 DTO 与消息 handler 在本成果中不动，否则产生循环引用。** 最终形态中切片项目需引用 `Dfo.Server.Application`（`SelectCharacterCommand` 依赖 `Application.Characters`、`Application.Sessions`、`Application.Inventory`、`Application.OnlineUsers`，命令契约在 `Application.Commands`）。若本成果同时把 DTO 迁出 `Dfo.Server.Data`，则仍留在 `Dfo.Server.Application` 的 14 个消息 handler 将反向依赖切片项目，形成 Application ↔ Protocol 的循环。因此 DTO、消息 handler、命令与命令 handler 的迁移属后继成果，本成果只迁编解码。
4. **`NetworkPlugins.cs` 有一处 `GameProtocol` 引用，只出现在 XML 文档注释的 `cref` 中**（第 103 行），不是可执行代码。`GameProtocol` 迁走后该 `cref` 无法解析，须改写为纯文本表述。

接受基线 `feef1a5`：编译 0 警告 0 错误，全量测试 580 项 515 通过 / 65 跳过 / 0 失败。

## Goal

存在一个 `Dfo.Server.Protocol` 项目，其中每个协议有一个自己的目录，该目录内可一处看到该协议的入站解包与出站编码；`Dfo.Server.Networking` 只剩传输职责；两张 DI 路由表随编解码迁入该项目；且服务端的线级可观察行为与全部既有断言逐字不变，本次 diff 中除文件位置、命名空间、using、可见性修饰与失效文档引用的修复外不含任何行为改动。

## Scope

### In

- 新建 `Dfo.Server.Protocol` 项目并纳入解决方案，确定其引用图。
- 按协议建立切片目录，将上述"协议内容"各文件迁入其所属协议的目录；被多个协议共用者（`ChannelEncryptedFrame`、`SelectCharacter` 的静态体系）落在其唯一消费者的切片内或一处明确的共享位置。
- `RegisterInboundDecoders` 与 `RegisterOutboundEncoders` 随之迁入该项目的模块，并保持"注册无条件生效、不受 `Enabled` 开关影响"这一现状。
- 新建 `Dfo.Server.Protocol.Tests` 并迁入 `Dfo.Server.Networking.Tests` 中针对被迁移编解码的测试；针对传输的测试留在原处。
- 修复迁移导致的失效引用：显式化原先靠父命名空间可见的类型引用、`NetworkPlugins.cs` 中失效的文档 `cref`、以及为跨程序集可见性所必需的修饰调整。
- `Dfo.Server.Host` 对新模块的组合根接线。

### Out

- 消息 DTO（`Data/Networking/NetworkMessages.cs`）、消息 handler、命令与命令 handler 的迁移。属后继成果，理由见 Current Situation 第 3 条。
- AGENTS.md 的更新与消息 handler 业务语义边界（根 SC-11）的审计。属后继成果，须在切片最终形态确定后才可如实记录。
- 任何字节布局、加密参数、组帧规则、投递次序、解包判定与丢弃语义的改变。
- 抽取共享的字段读写原语（D-string、定长 ASCII、计数列表等）。根 Scope/Out 已排除。
- 传输侧内容的重写或重组；`ChannelPayloadCipher` 与 `FrameEncoders` 留在 `Dfo.Server.Networking` 且不改其形态。
- 新增或删除任何协议、消息、注册项。

## Constraints

- 本次 diff 不得夹带逻辑变更：除文件位置、命名空间、using、可见性修饰，以及迁移直接导致的失效引用修复（含文档 `cref`）之外，不得包含任何行为改动。任何被迫的例外须逐处在结果中列明并说明其不可避免性。
- 编译期分层不得放宽：`Dfo.Server.Networking` 不得引用 `Dfo.Server.Application` 或 `Dfo.Server.Protocol`。切片项目对传输的依赖方向必须是 Protocol → Networking。
- 线级可观察行为必须不变：未知线上帧键静默丢弃、出站按 endpoint 校验、Channel 载荷 AES-128-ECB + zlib + 当日密钥、Game 出站 15 字节头、`SelectCharacter` 的静态帧流与补充投影的有序投递及其失败补偿次序、登录失败体仍不可编码。
- 不得以放宽类型、抑制诊断或关闭 `TreatWarningsAsErrors` 的方式换取编译通过。可见性放宽仅限迁移后跨程序集访问所必需者，且须取最小必要范围。
- 既有 580 项测试须全部继续通过，且断言内容不得改动。迁移可改变测试的文件位置、命名空间与 using，不得改变其断言。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| CSC-1 | 存在 `Dfo.Server.Protocol` 项目，其中每个已实现协议有一个自己的目录 | 切片目录列示，对全部 14 个入站协议与 9 条出站消息成立 |
| CSC-2 | 每个切片目录内可同时看到该协议的入站解包与出站编码（该协议两侧皆有时） | 逐协议的目录内容列示 |
| CSC-3 | `Dfo.Server.Networking` 中不再有协议编解码内容 | 该项目文件列示与检视，仅剩传输 |
| CSC-4 | 两张 DI 路由表随编解码迁入切片项目，且注册仍无条件生效 | 路由表所在文件的代码审阅，加上注册不受 `Enabled` 影响的证据 |
| CSC-5 | 编译期分层未放宽 | 各 csproj 引用图，`Dfo.Server.Networking` 不引用 `Dfo.Server.Application` 与 `Dfo.Server.Protocol` |
| CSC-6 | 本次 diff 不含逻辑变更 | 逐文件 diff 审阅结论，配合内容比对证明被迁移文件除命名空间/using/可见性外与基线一致 |
| CSC-7 | 线级可观察行为不变 | 既有集成测试（GameLogin、ChannelConnect、ChannelControlMessages、GameExit、未知帧、出站失败、投影投递）通过且断言未改 |
| CSC-8 | 可见性放宽取最小必要范围 | 全部可见性修饰改动的逐项列示与必要性说明 |
| CSC-9 | 无回归 | 全量测试对齐 580 项基线，0 失败，并说明测试的位置改动与任何断言改动 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 复用根 TaskContract 的 `task` 行：承接 Agent 提出的三步方案后，用户答「按ssd跑完吧。可以选multispec来跑，一个任务多个spec。或者如果你喜欢也可以多个spec单独跑。」，并在 spec-002 接受后答「好，我们开始下一个任务吧」 | 在任务工作树 `codex/protocol-vertical-slices` 内新建 `Dfo.Server.Protocol` 与其测试项目，迁入协议编解码与两张路由表并迁移相应测试，产出一次已审阅并已验证的本地结果提交。该成果完整落在根 Scope/In 第 5、6 条之内，未扩张根授权范围。 |

## Requires Confirmation

- 合并、推送、发起 PR，或对 `main` 与其他分支的写入。
- 任何超出本成果边界的改动，特别是 DTO 与 handler 的迁移、AGENTS.md 更新、编解码原语抽取。

## Assumptions

- 切片项目在本成果中不需要引用 `Dfo.Server.Application`：被迁移的编解码只依赖 `Abstractions`、`Data` 与 `Networking` 的传输原语（已由 using 检视与父命名空间引用勘察确认）。若实施中发现反例，须停在该处评估而非放宽引用图。
- `ChannelPayloadCipher` 属传输：它实现 Channel 端点的载荷保护，与具体消息无关，且已被三个 Channel 编码器共用。其归属若在实施中被证明相反，属需重新评估的边界事实而非可自行改判的细节。
- 被迁移的 `internal` 编解码类型在切片项目内仍可保持 `internal`，仅其跨程序集被消费者需要的部分才放宽。
- 基线中 `Dfo.Server.Host.Tests` 有一次未复现的瞬时失败（见根 map 的 `baseline_evidence`）。同类瞬时失败不得计为本成果的回归，但须在结果中如实记录其发生。
