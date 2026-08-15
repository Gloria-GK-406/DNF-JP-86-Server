---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Protocol Codec Slices

## Implementation Approach

Spec 的核心成果是"协议内容以一协议一目录的形态存在于与传输分离的编译单元中，且迁移未改变任何行为"。必要支撑是依赖方向、最小可见性与路由表的无条件生效；周边保证是零变更比对、可见性交代与全量测试。

最小可行路径基于三项已勘察的项目事实：传输原语（`ChannelFrameEncoder`、`GameServerFrameEncoder`、`DnfFrameProtocol`、`ChannelPayloadCipher`、`GameLoginOptions`）已全部是 `public`；被迁移的 14 个解包器与 9 个编码器只依赖 `Dfo.Server.Abstractions`、`Dfo.Server.Data` 与上述传输原语；被迁移的 `internal` 编解码类型在迁移后彼此仍同处一个编译单元。三者合起来意味着**本次迁移预期不需要任何可见性放宽**——这是 T-005 要证实或证伪的具体主张，而非事后总结。

最早的核心成果任务是 T-002（Channel 端点的四个切片落地）。它之前只有 T-001，且 T-001 不可省略：切片项目与其被 Host 接线的模块必须先存在，否则任何被迁移的文件都没有落点，且路由表迁移会使解决方案不可编译。T-001 自身不搬迁任何文件，因此它交付的是一个空模块加一次绿色构建——这正是后续每一批搬迁能以"构建与全量测试仍绿"为检查点的前提。

搬迁分两批（Channel 先、Game 后）而非一次完成，因为两个端点的协议内容互不引用，各自可在批次末尾达到可编译可测试状态。这使"零逻辑变更"的核验可以分段进行：每批之后 diff 规模仍在可逐文件比对的范围内，而不是一次面对 43 个文件。

目录形态按端点分两层、端点内一协议一目录。端点级承载该端点的线上标识与该端点内多个协议共用的编解码件：`ChannelProtocol` 与 `ChannelEncryptedFrame` 属 Channel 端点级（后者被三个加密编码器共用），`GameProtocol` 属 Game 端点级。线上标识不拆进各切片，理由与用户保留消息枚举的理由同构——需要一处能全量通览该端点的线上号。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Protocol/Dfo.Server.Protocol.csproj` | 新建；引用 Abstractions、Data、Networking；对测试项目开放 internals | 现有 csproj 的属性约定 | 协议编译单元 |
| BD-002 | `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 新建；承载入站与出站两张路由表与协议侧服务注册，全部无条件生效 | `NetworkingModule` 现有两张路由表 | 协议接线的唯一注册点 |
| BD-003 | `Server/src/Dfo.Server.Protocol/Channel/` | 新建；`ChannelProtocol.cs`、`ChannelEncryptedFrame.cs` 与 `Connect/`、`ScriptVersion/`、`Script/`、`Info/` 四个切片目录 | Networking 现有 Channel 协议文件 | Channel 端点的四个切片 |
| BD-004 | `Server/src/Dfo.Server.Protocol/Game/` | 新建；`GameProtocol.cs` 与 11 个切片目录 | Networking 现有 Game 协议文件 | Game 端点的十一个切片 |
| BD-005 | `Server/src/Dfo.Server.Networking/NetworkingModule.cs` | 删除两张路由表与协议侧服务注册及其失效 using；保留选项、连接、插件与 TCP 接线 | 现文件 | 只剩传输接线的模块 |
| BD-006 | `Server/src/Dfo.Server.Networking/Framing/` | 移除全部协议内容文件（含 `Decoders/`、`Encoders/` 两个目录） | 现目录 | 只剩传输的 Framing |
| BD-007 | `Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs` | 仅修复第 103 行失效的 `<see cref="GameProtocol.Command"/>` 文档引用 | 现文件 | 可解析的文档注释 |
| BD-008 | `Server/src/Dfo.Server.Host/Program.cs` | 在模块列表中加入 `ProtocolModule` | 现文件 | 协议模块被组合根接线 |
| BD-009 | `Server/86JP.Server.slnx` | 加入两个新项目 | 现文件 | 解决方案包含新项目 |
| BD-010 | `Server/tests/Dfo.Server.Protocol.Tests/` | 新建测试项目并接收迁入的 11 个编解码向量测试 | `Dfo.Server.Networking.Tests` 现有 codec 测试 | 协议编解码的测试落点 |
| BD-011 | `Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj` | 增加对协议项目的引用（集成测试拉起的宿主需注册协议模块） | 现文件 | 可编译的传输测试项目 |
| BD-012 | `.rks-agent/tasks/protocol-vertical-slices/attempts/spec-003-protocol-codec-slices/verification/completion-evidence.md` | 新建；零变更比对、引用图与可见性清单的证据 | T-002..T-005 的检查输出 | 验证证据 |

## Ordered Tasks

### T-001: 协议编译单元与其测试项目存在并被接线

- Covers: RQ-001, RQ-006
- Boundaries: BD-001, BD-002, BD-008, BD-009, BD-010
- Inputs: 现有 csproj 属性约定、`Program.cs` 的模块列表、`86JP.Server.slnx`
- Outputs: 可构建的空协议模块，测试项目就位

#### Steps

1. 新建 `Dfo.Server.Protocol.csproj`，`ProjectReference` 为 `Dfo.Server.Abstractions`、`Dfo.Server.Data`、`Dfo.Server.Networking`，并加 `InternalsVisibleTo Include="Dfo.Server.Protocol.Tests"`，属性沿用 `Dfo.Server.Networking.csproj` 的写法。
2. 新建 `ProtocolModule.cs`，实现 `IServerModule`，`Name` 为 `"Protocol"`，`ConfigureServices` 目前为空实现（仅做参数空值校验）。
3. 新建 `Dfo.Server.Protocol.Tests.csproj`，包引用沿用 `Dfo.Server.Networking.Tests.csproj`，`ProjectReference` 指向 `Dfo.Server.Protocol`；新建其 `Usings.cs`，内容与 `Dfo.Server.Networking.Tests/Usings.cs` 一致。
4. 在 `86JP.Server.slnx` 的 `/src/` 与 `/tests/` 文件夹下各加一行项目。
5. 在 `Program.cs` 的 `builder.AddServerModules(...)` 参数列表中，于 `new NetworkingModule()` 之后加入 `new ProtocolModule()`。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx -nodereuse:false` | 已成功生成，0 警告 0 错误 |
| CK-002 | `dotnet test Server/86JP.Server.slnx -nodereuse:false` | 580 项，0 失败（新测试项目为空，不改变计数） |

### T-002: Channel 端点的四个切片落地

- Depends on: T-001
- Covers: RQ-002, RQ-003, RQ-005, RQ-007, RQ-009
- Boundaries: BD-002, BD-003, BD-005, BD-006
- Inputs: Networking 中 10 个 Channel 协议文件与 `NetworkingModule` 的 4 条入站与 4 条出站 Channel 注册
- Outputs: `Protocol/Channel/` 下四个切片，Networking 中不再有 Channel 协议内容

#### Steps

1. 将 `Framing/ChannelProtocol.cs` 与 `Framing/Encoders/ChannelEncryptedFrame.cs` 移至 `Protocol/Channel/`，命名空间改为 `Dfo.Server.Protocol.Channel`。
2. 建立 `Protocol/Channel/Connect/`、`ScriptVersion/`、`Script/`、`Info/` 四个目录，各移入该协议的解包器与编码器共 8 个文件，命名空间改为 `Dfo.Server.Protocol.Channel.<切片名>`。
3. 为每个移入文件补齐原先靠父命名空间隐式可见的引用：对 `ChannelFrameEncoder`、`ChannelPayloadCipher`、`DnfFrameProtocol` 需加 `using Dfo.Server.Networking.Framing;`；对 `ChannelProtocol`、`ChannelEncryptedFrame` 需加 `using Dfo.Server.Protocol.Channel;`。除此之外文件内容逐字不动。
4. 将 `NetworkingModule.RegisterInboundDecoders` 中 4 条 `AddChannelMessageDecoder` 与 `RegisterOutboundEncoders` 中 4 条 Channel `AddServerMessageEncoder` 整段移入 `ProtocolModule.ConfigureServices`，保持逐行内容与顺序不变，且不置于任何条件分支之下。
5. 删除 `NetworkingModule` 中因此失效的 using。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-003 | `dotnet build Server/86JP.Server.slnx -nodereuse:false` | 已成功生成，0 警告 0 错误 |
| CK-004 | `dotnet test Server/86JP.Server.slnx -nodereuse:false` | 580 项，0 失败；`ChannelConnectIntegrationTests` 与 `ChannelControlMessagesIntegrationTests` 通过且断言未改 |
| CK-005 | Non-command: 对 10 个被移动文件，逐个取 `feef1a5` 版本与迁移后版本比对 | 差异仅为 `namespace` 行与新增的 using 行 |

### T-003: Game 端点的十一个切片落地

- Depends on: T-002
- Covers: RQ-002, RQ-003, RQ-004, RQ-005, RQ-007, RQ-009
- Boundaries: BD-002, BD-004, BD-005, BD-006, BD-007
- Inputs: Networking 中 33 个 Game 协议文件与 `NetworkingModule` 的 10 条入站与 5 条出站 Game 注册
- Outputs: `Protocol/Game/` 下十一个切片；Networking 只剩传输

#### Steps

1. 将 `Framing/GameProtocol.cs` 移至 `Protocol/Game/`，命名空间改为 `Dfo.Server.Protocol.Game`。
2. 建立 11 个切片目录并移入其文件，命名空间改为 `Dfo.Server.Protocol.Game.<切片名>`：`Login/`（`GameLoginMessageDecoder`、`GameLoginPayloadCodec`、`GameLoginResponseEncoder`）、`Exit/`（`GameExitMessageDecoder`、`GameExitResponseEncoder`）、`SelectCharacter/`（`SelectCharacterMessageDecoder`、`SelectCharacterPayloadCodec`、`SelectCharacterResponseEncoder`、`SelectCharacterResponseComposer`、`SelectCharacterStaticBodyFamilies`、`SelectCharacterStaticPayloadCodec`、`SelectCharacterStaticSequence`、`SelectCharacterSupplementalProjectionCodec`、`SelectCharacterProjectionComposer`）、`AccountRoster/`（`GetUserInfoMessageDecoder`、`GetUserInfoPayloadCodec`、`AccountCharacterRosterPayloadCodec`、`AccountCharacterRosterResponseEncoder`）、`RosterFollowUp/`、`ClientSpecStatistic/`、`ClientHardwareDetail/`、`ClientSpecProfile/`、`SessionMetrics/`、`TaggedIdReport/`（后六者各含该协议的解包器与其 codec）、`InitialConnection/`（`GameInitialConnectionNoticeEncoder`、`GameInitialConnectionPayloadCodec`）。
3. 为每个移入文件补齐失效引用：对 `GameServerFrameEncoder`、`DnfFrameProtocol` 加 `using Dfo.Server.Networking.Framing;`；对 `GameLoginOptions` 加 `using Dfo.Server.Networking.Configuration;`（原已显式，保持）；对 `GameProtocol` 加 `using Dfo.Server.Protocol.Game;`。除此之外文件内容逐字不动，包括 `SelectCharacterStaticBodyFamilies` 的全部静态字节表。
4. 将 `NetworkingModule` 剩余的 10 条 `AddGameMessageDecoder`、5 条 Game `AddServerMessageEncoder`，以及 `TryAddSingleton<ISelectCharacterProjectionComposer, SelectCharacterProjectionComposer>()` 移入 `ProtocolModule`，保持顺序与内容不变。删除 `NetworkingModule` 中 `RegisterInboundDecoders`、`RegisterOutboundEncoders` 两个已空的方法及其 `<remarks>`，并把这两段 `<remarks>` 原文迁至 `ProtocolModule` 对应位置。
5. 删除 `Framing/Decoders/` 与 `Framing/Encoders/` 两个已空目录，删除 `NetworkingModule` 中失效的 using。
6. 将 `NetworkPlugins.cs` 第 103 行的 `<see cref="GameProtocol.Command"/>` 改为纯文本 `<c>0x01</c>`，其余文字不动。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet build Server/86JP.Server.slnx -nodereuse:false` | 已成功生成，0 警告 0 错误 |
| CK-007 | `dotnet test Server/86JP.Server.slnx -nodereuse:false` | 580 项，0 失败；`GameLoginIntegrationTests`、`GameExitIntegrationTests`、`UnknownInboundFrameIntegrationTests`、`OutboundSendFailureTests`、`TouchSocketNetworkMessageSenderProjectionTests` 通过且断言未改 |
| CK-008 | Non-command: 对 33 个被移动文件，逐个取 `feef1a5` 版本与迁移后版本比对 | 差异仅为 `namespace` 行与新增的 using 行 |
| CK-009 | `git -C . diff feef1a5 --stat -- Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs` | 仅 1 行增 1 行删（第 103 行文档引用） |
| CK-010 | Non-command: 列示 `Server/src/Dfo.Server.Networking/` 的全部 `.cs` 文件并逐个判定 | 无任何与具体消息绑定的编解码内容 |

### T-004: 编解码向量测试迁入协议测试项目

- Depends on: T-003
- Covers: RQ-011
- Boundaries: BD-010, BD-011
- Inputs: `Dfo.Server.Networking.Tests/Framing/` 下 11 个编解码向量测试
- Outputs: 协议测试项目承载编解码测试，传输测试项目只剩传输测试

#### Steps

1. 将 `ClientHardwareDetailPayloadCodecTests`、`ClientSpecProfilePayloadCodecTests`、`ClientSpecStatisticPayloadCodecTests`、`GameLoginCodecTests`、`GetUserInfoPayloadCodecTests`、`SelectCharacterPayloadCodecTests`、`SelectCharacterProductionProjectionTests`、`SelectCharacterResponseComposerTests`、`SelectCharacterSupplementalProjectionTests`、`SessionMetricsPayloadCodecTests`、`SmallReportPayloadCodecTests` 移入 `Dfo.Server.Protocol.Tests/`，按其被测协议归入对应目录，只改命名空间与 using，断言逐字不动。
2. 在 `Dfo.Server.Protocol.Tests.csproj` 中补齐这些测试需要的 `ProjectReference`（`Dfo.Server.Data`、`Dfo.Server.Abstractions`、`Dfo.Server.Networking` 视实际引用而定）。
3. 为 `Dfo.Server.Networking.Tests.csproj` 增加对 `Dfo.Server.Protocol` 的 `ProjectReference`——其集成测试拉起的宿主须注册 `ProtocolModule` 才能获得解包器与编码器。
4. `ChannelPayloadCipherTests`、`FrameAdapterTests`、`FrameEncoderTests`、`FrameRequestInfoTests` 与 `Runtime/`、`Integration/` 下的测试留在原处不动。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-011 | `dotnet build Server/86JP.Server.slnx -nodereuse:false` | 已成功生成，0 警告 0 错误 |
| CK-012 | `dotnet test Server/86JP.Server.slnx -nodereuse:false` | 合计 580 项，0 失败；Networking 与 Protocol 两套件的项数之和等于原 Networking 的 129 项 |
| CK-013 | Non-command: 对 11 个被移动测试文件，逐个取 `feef1a5` 版本与迁移后版本比对 | 差异仅为 `namespace` 行与 using 行，断言未改 |

### T-005: 结构、引用图与可见性的成果证据

- Depends on: T-004
- Covers: RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-008, RQ-009, RQ-010
- Boundaries: BD-012
- Inputs: T-001..T-004 的产物
- Outputs: 可供复核与验证引用的结构、引用图与可见性证据

#### Steps

1. 列示 `Dfo.Server.Protocol` 的完整目录树，逐协议核对其解包与编码是否同处一目录。
2. 取全部 csproj 的 `ProjectReference` 生成引用图，核对 `Dfo.Server.Networking` 不引用 `Dfo.Server.Application` 与 `Dfo.Server.Protocol`。
3. 用 `git diff feef1a5` 全量列出可见性修饰改动，逐项记录其必要性；若清单为空，明确记录"无可见性放宽"这一结论。
4. 检索 `ChannelEncryptedFrame`、`SelectCharacterStaticBodyFamilies`、`SelectCharacterResponseComposer` 的定义处，确认各只有一份。
5. 核对 `ProtocolModule.ConfigureServices` 中全部注册均不在任何条件分支之下。
6. 运行全量测试并记录分项计数。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-014 | Non-command: 协议项目目录树列示 | 每个已实现协议一个目录；两侧皆有内容者其解包与编码同处一目录 |
| CK-015 | `grep -o 'Include="[^"]*csproj"' Server/src/Dfo.Server.Networking/Dfo.Server.Networking.csproj` | 仅 Abstractions 与 Data |
| CK-016 | `git diff feef1a5 -- 'Server/src/*.cs' \| grep -E '^[+-]\s*(public\|internal\|private\|protected)'` | 输出仅含因文件移动产生的成对增删，无净可见性放宽；结果逐项记入证据 |
| CK-017 | `grep -rn 'class ChannelEncryptedFrame\|class SelectCharacterStaticBodyFamilies\|class SelectCharacterResponseComposer' Server/src --include=*.cs` | 各命中恰好一处 |
| CK-018 | Non-command: `ProtocolModule.ConfigureServices` 代码审阅 | 全部注册无条件生效，无 `Enabled` 类分支 |
| CK-019 | `dotnet test Server/86JP.Server.slnx -nodereuse:false` | 580 项，0 失败，与基线一致 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-005 | BD-001, BD-009 | CK-001, CK-014 |
| RQ-002 | T-002, T-003, T-005 | BD-003, BD-004 | CK-014 |
| RQ-003 | T-002, T-003, T-005 | BD-003, BD-004 | CK-014 |
| RQ-004 | T-003 | BD-006 | CK-010 |
| RQ-005 | T-002, T-003, T-005 | BD-002, BD-005 | CK-018 |
| RQ-006 | T-001, T-005 | BD-001, BD-011 | CK-015 |
| RQ-007 | T-002, T-003 | BD-003, BD-004, BD-007 | CK-005, CK-008, CK-009 |
| RQ-008 | T-005 | BD-012 | CK-016 |
| RQ-009 | T-002, T-003, T-005 | BD-003, BD-004 | CK-017 |
| RQ-010 | T-002, T-003, T-005 | BD-005, BD-006 | CK-004, CK-007, CK-019 |
| RQ-011 | T-004 | BD-010, BD-011 | CK-013, CK-012 |

## Risks and Rollback

- Risk: 用工具批量重命名命名空间时顺带触碰文件其他部分（含仅换行符变化），使"零变更"主张失真。缓解：CK-005、CK-008、CK-013 以逐文件内容比对为准而非以 `git status` 为准；spec-002 已发生过一次纯换行符触碰被误判为改动的情况。
- Risk: 某个被迁移文件实际依赖 `Dfo.Server.Application` 的类型，勘察未发现。缓解：一旦在 T-002 或 T-003 编译时出现该情况，停止并按 routing-decision 的 reroute 条件上报，不得以增加引用绕过。
- Rollback: 本尝试未接受前，回滚边界是回到 `feef1a5`；新建的项目文件与目录均为未跟踪路径，可精确移除。

## Assumptions

- 迁移不需要任何可见性放宽（依据：被迁移的 `internal` 类型迁移后仍同处一个编译单元，传输原语已是 `public`）。此为待 CK-016 证实的主张，若证伪则须逐项记录并说明最小性。
- `Dfo.Server.Networking.Tests` 的集成测试可通过增加对协议项目的引用继续留在原处，因为它验证的是装配后的整条线级路径而非某个协议的编解码。
- 基线中 `Dfo.Server.Host.Tests` 有一次未复现的瞬时失败。同类瞬时失败不计为回归，但须如实记录。
