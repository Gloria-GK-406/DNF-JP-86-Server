---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: DI-Driven Inbound Message Decoding

## Implementation Approach

Spec 的核心成果是"接线成为一处局部注册"，必要支撑是"解码结果与丢弃语义逐字段不变"，外围保障是启动期的接线缺口检查。任务顺序据此排列：先立接缝与注册契约（T-001），再接入 14 个协议的解包器（T-002），然后用注册表替换共享分支（T-003）——到此核心成果成立；随后换向启动检查（T-004），最后补齐丢弃语义与接入范围的证据（T-005）。T-001 先于 T-002 是不可避免的执行前置：解包器必须实现接缝定义的契约才能编译。

**接缝归属决策。** 解包器抽象与注册记录放入 `Dfo.Server.Abstractions/Networking/`，解包器实现留在 `Dfo.Server.Networking`，接线闭合性检查并入 `Dfo.Server.Application` 既有的 `MessageHandlerRegistry`。理由：闭合性检查须同时看到解包器产出与处理器注册，而 Networking 不得引用 Application；Abstractions 是两者共同引用的唯一层，且已引用 Data（消息类型与消息契约所在），无需新增任何项目引用。备选一「检查上移 Host 组合根」把一条编译期分层不变量散入组合根，且使其只能由 Host.Tests 覆盖——该套件已知存在端口相关的瞬时失败，不宜承载此不变量。备选二「解包器此时迁入 Application」属根任务第三个成果的范围，在本成果内执行会同时改变结构与位置，使 CSC-2 的"逐字段一致"失去可归因的 diff。

**行为保持策略。** 既有 `*PayloadCodec` 静态类**不改写**：每个解包器是一个薄类，把调用转发给对应的静态方法。`GameLoginPayloadCodec.TryDecode` 的 bool + out 形状不归一到 `Decode`，而是由其解包器原样调用并在返回 false 时产出 null——与现有 plugin 内的组装逻辑逐字相同。这样"归一 GameLogin 时把静默失败改成异常"这一失败路径被结构性排除，而非依赖复核发现。

**丢弃语义。** 注册键为 `(command, type)`，全部 Game 注册使用 command `0x01`，因此 command 非 `0x01` 的帧自然查表未命中并被丢弃——与现有显式 `frame.Command != GameProtocol.Command` 判断的可观察结果相同。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Networking/ClientMessageDecoding.cs` | 新增：解包器接缝、两个端点的注册记录、产出登记记录、DI 注册扩展 | `Dfo.Server.Data.Networking` 的消息与消息类型契约 | `IGameMessageDecoder`、`IChannelMessageDecoder`、`GameMessageDecoderRegistration`、`ChannelMessageDecoderRegistration`、`ClientMessageProductionRegistration`、`AddGameMessageDecoder`/`AddChannelMessageDecoder` |
| BD-002 | `Server/src/Dfo.Server.Networking/Framing/ChannelProtocol.cs` | 新增：Channel 四个消息号常量，与既有 `GameProtocol` 对称 | 现有 Channel 分支使用的消息号 | 四个 `const byte` |
| BD-003 | `Server/src/Dfo.Server.Networking/Framing/Decoders/` | 新增：14 个解包器，每协议一文件，全部转发既有静态 codec | 既有 `*PayloadCodec` 静态方法 | 14 个 `internal sealed` 解包器类 |
| BD-004 | `Server/src/Dfo.Server.Networking/Runtime/ClientMessageDecoderRegistries.cs` | 新增：两个端点各一个注册表，构造期查重并冻结查表 | `IEnumerable<*DecoderRegistration>`、`IServiceProvider` | `GameMessageDecoderRegistry`、`ChannelMessageDecoderRegistry` |
| BD-005 | `Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs` | 修改：删除两处 `CreateMessage`，改为经注册表查表 | 帧信息、注册表 | 无分支的 plugin |
| BD-006 | `Server/src/Dfo.Server.Networking/NetworkingModule.cs` | 修改：注册两个注册表与 14 条解包器注册，位置在 `Enabled` 判断之前 | 解包器类型、线上帧键、产出消息类型 | 无条件生效的注册集合 |
| BD-007 | `Server/src/Dfo.Server.Application/Dispatching/MessageHandlerRegistry.cs` | 修改：移除枚举完备性检查，加入产出×处理器闭合性检查 | `IEnumerable<MessageHandlerRegistration>`、`IEnumerable<ClientMessageProductionRegistration>` | 保留查重、放行未接线目录成员、缺口时具名抛出的注册表 |
| BD-008 | `Server/tests/Dfo.Server.Application.Tests/Dispatching/NetworkMessageDispatcherTests.cs` | 修改：删除枚举完备性断言与枚举回填脚手架，新增闭合性两个方向的断言 | 变更后的注册表 | 覆盖 RQ-007、RQ-008 的测试 |
| BD-009 | `Server/tests/Dfo.Server.Networking.Tests/Runtime/ClientMessageDecoderRegistryTests.cs` | 新增：注册表查重、未命中、产出登记的测试 | 两个注册表 | 覆盖 RQ-001 查表语义的测试 |
| BD-010 | `Server/tests/Dfo.Server.Networking.Tests/Integration/UnknownInboundFrameIntegrationTests.cs` | 新增：三种丢弃情形的线级测试 | 真实 host 与连接 | 覆盖 RQ-005 的测试 |

## Ordered Tasks

### T-001: 建立解包接缝与注册契约

- Covers: RQ-001, RQ-006, RQ-009
- Boundaries: BD-001
- Inputs: 现有 `Dfo.Server.Abstractions/Networking/INetworkServices.cs` 的接缝风格；`Dfo.Server.Data.Networking` 的 `ClientMessageType`、`IClientNetworkMessage`、两种帧元数据
- Outputs: 编译通过的接缝与注册扩展，尚无实现者

#### Steps

1. 在 `Dfo.Server.Abstractions/Networking/` 新增 `ClientMessageDecoding.cs`，定义 `IGameMessageDecoder.Decode(GameMessageMetadata, ReadOnlySpan<byte>)` 与 `IChannelMessageDecoder.Decode(ChannelMessageMetadata, ReadOnlySpan<byte>)`，返回类型均为 `IClientNetworkMessage?`。两个端点分列接缝，因两者帧头形状不同，合并会引入按端点无意义的字段。
2. 定义 `GameMessageDecoderRegistration(byte Command, ushort Type, Type DecoderType)` 与 `ChannelMessageDecoderRegistration(byte MessageNumber, Type DecoderType)`。
3. 定义 `ClientMessageProductionRegistration(ClientMessageType MessageType, Type DecoderType)`，作为闭合性检查唯一需要读取的扁平登记，使该检查不依赖任何端点形状。
4. 定义 `AddGameMessageDecoder<TDecoder>(this IServiceCollection, byte command, ushort type, params ClientMessageType[] produces)` 与 `AddChannelMessageDecoder<TDecoder>(this IServiceCollection, byte messageNumber, params ClientMessageType[] produces)`；各自注册解包器为 singleton、追加对应端点注册记录、并为每个 `produces` 追加一条产出登记。
5. 确认 `Dfo.Server.Abstractions.csproj` 无需新增引用。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `cd /d/project/DNF && dotnet build DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/86JP.Server.slnx -nodereuse:false` | 生成成功，0 警告 0 错误 |
| CK-002 | Non-command: 检视 `Dfo.Server.Abstractions.csproj` 的 `ProjectReference` 集合 | 仅 `Dfo.Server.Data`，未新增 |

### T-002: 为 14 个入站协议接入解包器

- Depends on: T-001
- Covers: RQ-003, RQ-004
- Boundaries: BD-002, BD-003
- Inputs: 现有 `*PayloadCodec` 静态方法；现有两处 `CreateMessage` 的分支体
- Outputs: 14 个解包器类，各自转发既有 codec

#### Steps

1. 新增 `Framing/ChannelProtocol.cs`，以常量固化 Channel 四个消息号（`0x05`、`0x09`、`0x0B`、`0x11`），取值与现有分支所依据的枚举值一致。
2. 在 `Framing/Decoders/` 下每协议新增一个文件，实现对应端点的解包器接缝。
3. 九个已具备 `Decode(GameMessageMetadata, ReadOnlySpan<byte>)` 形状的 codec，其解包器体为单行转发，不改动 codec 本身。
4. `GameExitRequest` 的解包器直接构造该消息，与现有分支体逐字相同。
5. `GameLoginPayloadCodec` 的解包器调用其既有 `TryDecode` 并在返回 true 时构造 `GameLoginRequest`、false 时返回 null；不改动 `GameLoginPayloadCodec` 的任何成员，其 `EncodeSuccess` 原样保留。
6. 四个 Channel 解包器由 `ChannelMessageMetadata` 构造对应请求记录，与现有分支体逐字相同。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-003 | `cd /d/project/DNF && dotnet build DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/86JP.Server.slnx -nodereuse:false` | 生成成功，0 警告 0 错误 |
| CK-004 | Non-command: 对 `Server/src/Dfo.Server.Networking/Framing/` 下既有 `*PayloadCodec.cs` 取变更前后差异 | 除 `GameLoginPayloadCodec` 因可见性需要可能出现的修饰符调整外无差异；无任何方法体改动 |

### T-003: 用注册表替换两处共享分支

- Depends on: T-002
- Covers: RQ-001, RQ-002, RQ-005
- Boundaries: BD-004, BD-005, BD-006
- Inputs: 解包器注册记录；现有 plugin 的接收路径
- Outputs: 无消息构造分支的 plugin，注册集合驱动的查表

#### Steps

1. 新增两个注册表：构造函数接收对应端点的注册记录序列与 `IServiceProvider`，按线上帧键查重（重复即抛并指名该键），解析解包器实例并冻结为只读字典；提供按键取解包器的查询，未命中返回 null。查重与冻结的形状对齐既有 `MessageHandlerRegistry`。
2. 修改 `GameNetworkPlugin`：删除 `CreateMessage`，接收路径改为按 `(frame.Command, frame.Type)` 取解包器；未命中直接返回，命中则以 `GameMessageMetadata` 与 `frame.Body.Span` 解码，产出为 null 时直接返回。删除显式的 command 判断——全部注册使用 command `0x01`，非 `0x01` 的帧必然未命中。
3. 修改 `ChannelNetworkPlugin`：删除 `CreateMessage`，按 `frame.MessageNumber` 取解包器，其余同上。
4. 修改 `NetworkingModule`：在 `options.Enabled` 判断之前注册两个注册表与 14 条解包器注册，使注册集合在监听关闭时同样生效。
5. 确认 `Dfo.Server.Networking.csproj` 未新增对 `Dfo.Server.Application` 的引用。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-005 | `cd /d/project/DNF && dotnet test DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj -nodereuse:false` | 全部通过，含四组既有线级集成测试，断言未改 |
| CK-006 | Non-command: 在 `Server/src/Dfo.Server.Networking` 内检索 `CreateMessage` | 无匹配 |
| CK-007 | Non-command: 检视 `Dfo.Server.Networking.csproj` 的 `ProjectReference` 集合 | 仅 `Dfo.Server.Abstractions` 与 `Dfo.Server.Data` |

### T-004: 启动检查由枚举完备性换为接线闭合性

- Depends on: T-003
- Covers: RQ-007, RQ-008
- Boundaries: BD-007, BD-008
- Inputs: 产出登记序列；处理器注册序列
- Outputs: 放行未接线目录成员、缺口时具名失败的注册表及其测试

#### Steps

1. 修改 `MessageHandlerRegistry` 构造函数，追加接收 `IEnumerable<ClientMessageProductionRegistration>`；保留重复处理器注册的检查。
2. 删除"枚举成员必须全部注册"的检查。
3. 新增闭合性检查：若某条产出登记的消息类型不在处理器注册集合中，抛出并同时指名该消息类型与申报它的解包器类型。
4. 修改 `NetworkMessageDispatcherTests`：删除 `BuildRegistry_RejectsMissingMessageTypeRegistrations`（其断言的行为本身被本成果有意移除，须在结果中说明），删除 `BuildProvider` 中按枚举回填处理器的脚手架。
5. 新增两项测试：目录中既无解包器也无处理器的成员不导致注册表构造失败；一条产出登记缺少处理器时构造失败且失败信息含该消息类型名。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-008 | `cd /d/project/DNF && dotnet test DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -nodereuse:false` | 全部通过，含两项新增断言 |
| CK-009 | Non-command: 检视 `MessageHandlerRegistry` | 无 `Enum.GetValues<ClientMessageType>` 完备性判定 |

### T-005: 补齐丢弃语义与接入范围的证据

- Depends on: T-004
- Covers: RQ-002, RQ-005
- Boundaries: BD-009, BD-010
- Inputs: 变更后的注册表与 plugin
- Outputs: 覆盖三种丢弃情形的线级测试、注册表语义测试、一次新协议接入的范围记录

#### Steps

1. 新增注册表测试：同一线上帧键重复注册即失败并指名该键；未注册键查询返回未命中；产出登记随注册一并产生。
2. 新增线级集成测试，在同一连接上依次发送：已注册 type 但帧体使解包器不产出消息的帧、未注册 type 的帧、command 非 `0x01` 的帧；断言三者均无出站字节、无异常、连接保持可用（随后一次正常请求仍得到应答）。
3. 记录一次新协议接入的变更范围：在临时分支状态下为一个未接线的线上帧键增加解包器与一条注册，记录所触及文件集合，随后撤销该临时改动。该记录作为 RQ-002 的证据，不进入最终结果。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-010 | `cd /d/project/DNF && dotnet test DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/86JP.Server.slnx -nodereuse:false` | 0 失败；总数不少于 563 项基线并含新增项 |
| CK-011 | Non-command: 新协议接入探针所触及的文件集合 | 仅含该协议自身的新增解包器文件与 `NetworkingModule` 的一行注册；无既有解包器或既有注册项被修改 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-003 | BD-001, BD-004, BD-005 | CK-006, CK-009 |
| RQ-002 | T-003, T-005 | BD-005, BD-006 | CK-011 |
| RQ-003 | T-002 | BD-003 | CK-004, CK-005 |
| RQ-004 | T-002 | BD-003 | CK-004, CK-005 |
| RQ-005 | T-003, T-005 | BD-005, BD-010 | CK-010 |
| RQ-006 | T-001 | BD-001 | CK-005 |
| RQ-007 | T-004 | BD-007, BD-008 | CK-008 |
| RQ-008 | T-004 | BD-007, BD-008 | CK-008 |
| RQ-009 | T-001, T-003 | BD-001, BD-006 | CK-002, CK-007 |

## Risks and Rollback

- Risk: `GameLoginPayloadCodec.TryDecode` 与其余 codec 形状不同，归一时易把"返回 false 即丢弃"改成抛出。缓解：不归一——解包器原样调用 `TryDecode` 并在 false 时返回 null，codec 本身零改动，由 CK-004 的差异检视证明。
- Risk: `GET_USERINFO` 的产出条件包含 `target == 0xFFFF`，转发时若误抄为仅判 mode 会放宽条件。缓解：解包器不重写该判定，整体转发既有 `Decode`，由 CK-004 与既有 `GetUserInfoPayloadCodecTests` 证明。
- Risk: 删除显式 command 判断后，若某条注册误用非 `0x01` 的 command，非法帧可能被接受。缓解：CK-010 的线级测试断言 command 非 `0x01` 的帧被丢弃。
- Risk: `Dfo.Server.Host.Tests` 已知存在未复现的瞬时失败。缓解：出现时单独重跑该套件，并在结果中如实记录其发生与重跑结论，不计为回归。
- Rollback: 本成果全部改动限于任务工作树内且未提交；如需放弃，由父控制器执行到 `06b24e7` 的精确回滚。

## Assumptions

- 解包器为无状态单例，可在注册表构造时一次性解析并冻结。
- 现有各协议解码向量测试直接针对 `*PayloadCodec` 静态方法，故 codec 零改动时这些测试无需修改即继续有效。
- `Dfo.Server.Networking.Tests` 已有 `InternalsVisibleTo`，新增的注册表可保持 internal 并被测试访问。
