---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: DI-Driven Outbound Message Encoding

## Implementation Approach

核心成果是"出站接线成为一处局部注册，且编码与端点校验分离"，必要支撑是"字节、帧序与投递语义逐字不变"。任务顺序：先立接缝与注册契约（T-001），再把九个既有编码搬进编码器（T-002），然后消除 sender 的分支与特判（T-003）——到此核心成果成立；最后补齐失败行为与接入范围的证据（T-004）。

**接缝形态。** 编码器接缝与注册记录放入 `Dfo.Server.Abstractions/Networking/`，与入站解包接缝并列；编码器实现留在 `Dfo.Server.Networking`。注册按**消息 CLR 类型**建键：出站消息由服务端代码构造，调用点静态已知类型，这与 `CommandBus` 按命令 CLR 类型建键同理；而按 `ServerMessageType` 建键不可行，因 `SelectCharacterResponse` 与 `SelectCharacterProjectionDelivery` 共用 `GameSelectCharacter` 一个值。注册点用 `AddServerMessageEncoder<TMessage, TEncoder>(endpoint)` 两个类型参数加端点实参，使"编码器与消息不匹配"无法通过编译，且端点在注册表上一眼可见。

**端点归属。** 现有九个分支各自带一个 `when connection.Context.EndpointKind == X` 子句，把编码与校验绑在一起。改为由注册声明端点、由 sender 一处统一判定。拒绝时沿用现有措辞 `Server message '{X}' is not supported for endpoint '{Y}'.`——全仓检索确认无测试断言该措辞，但沿用它可使这条行为在 diff 上显然未变。未注册消息改为一条区分性的失败信息，这是 CSC-5 要求的新行为。

**投递路径。** 全部生产调用点已走 `SendProjectionAsync`，其接口默认实现转发给 `SendAsync`，才使得 `SendAsync` 里需要那个类型特判。显式实现 `SendProjectionAsync` 即可让特判自然消失。既有投影测试直接调用内部静态 `DeliverProjectionAsync`，不经 `SendAsync`，故该路径的测试与断言完全不受影响——投递次序与失败隔离的既有覆盖原样保留。

**行为保持策略。** 九个既有编码方法整体搬入各自编码器，方法体不改写。`GameLoginPayloadCodec.EncodeSuccess` 搬入登录编码器，`GameLoginPayloadCodec` 只留 `TryDecode`，其对 `Networking.Configuration` 的依赖随之消失（CSC-7）。该搬迁使 `GameLoginCodecTests.EncodeSuccess_PreservesLegacyLoginBody` 的调用目标改变，其字节断言原样保留。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Networking/ServerMessageEncoding.cs` | 新增：编码器接缝、类型化基类、注册记录、DI 注册扩展 | `Dfo.Server.Data.Networking` 的出站消息契约与端点枚举 | `IServerMessageEncoder`、`ServerMessageEncoder<TMessage>`、`ServerMessageEncoderRegistration`、`AddServerMessageEncoder` |
| BD-002 | `Server/src/Dfo.Server.Networking/Framing/Encoders/` | 新增：九个编码器，每消息一文件，方法体自 sender 整体搬入 | 现有九个编码方法与两个外部编码入口 | 九个 `internal sealed` 编码器类 |
| BD-003 | `Server/src/Dfo.Server.Networking/Framing/GameLoginPayloadCodec.cs` | 修改：移出 `EncodeSuccess`，只留入站 `TryDecode` | — | 不再依赖 `Networking.Configuration` 的入站 codec |
| BD-004 | `Server/src/Dfo.Server.Networking/Runtime/ServerMessageEncoderRegistry.cs` | 新增：按消息 CLR 类型索引的注册表，构造期查重并冻结 | `IEnumerable<ServerMessageEncoderRegistration>`、`IServiceProvider` | 查表与端点信息 |
| BD-005 | `Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs` | 修改：删除九分支 switch、投递特判与八个私有编码方法；显式实现 `SendProjectionAsync`；加入统一端点判定 | 注册表、连接注册表 | 只做查表、校验与发送的 sender |
| BD-006 | `Server/src/Dfo.Server.Networking/NetworkingModule.cs` | 修改：注册编码器注册表与九条编码器注册 | 编码器类型、消息类型、端点 | 出站路由表 |
| BD-007 | `Server/tests/Dfo.Server.Networking.Tests/Framing/GameLoginCodecTests.cs` | 修改：`EncodeSuccess` 的调用目标改为登录编码器，字节断言不变 | 搬迁后的编码器 | 断言未变的测试 |
| BD-008 | `Server/tests/Dfo.Server.Networking.Tests/Runtime/ServerMessageEncoderRegistryTests.cs` | 新增：注册表查重、未命中、端点声明的测试 | 注册表 | 覆盖 RQ-001 的测试 |
| BD-009 | `Server/tests/Dfo.Server.Networking.Tests/Runtime/OutboundSendFailureTests.cs` | 新增：跨端点两个方向、未注册消息、登录失败体的测试 | 变更后的 sender 与编码器 | 覆盖 RQ-004、RQ-005、RQ-008 的测试 |

## Ordered Tasks

### T-001: 建立编码接缝与注册契约

- Covers: RQ-001, RQ-009
- Boundaries: BD-001
- Inputs: 入站解包接缝的既有形态；`ClientMessageHandler<TMessage>` 的类型化基类风格
- Outputs: 编译通过的接缝与注册扩展

#### Steps

1. 新增 `ServerMessageEncoding.cs`，定义 `IServerMessageEncoder.Encode(IServerNetworkMessage) -> IReadOnlyList<byte[]>`，以及类型化基类 `ServerMessageEncoder<TMessage>`，其非泛型实现在 CLR 类型不符时抛出并指名编码器与消息，形状对齐既有 `ClientMessageHandler<TMessage>`。
2. 定义 `ServerMessageEncoderRegistration(Type MessageType, Type EncoderType, NetworkEndpointKind Endpoint)`。
3. 定义 `AddServerMessageEncoder<TMessage, TEncoder>(this IServiceCollection, NetworkEndpointKind endpoint)`，约束 `TMessage : class, IServerNetworkMessage` 与 `TEncoder : ServerMessageEncoder<TMessage>`，使两者错配无法通过编译；注册编码器为 singleton 并追加注册记录。
4. 确认 `Dfo.Server.Abstractions.csproj` 无需新增引用。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `cd /d/project/DNF && dotnet build DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/86JP.Server.slnx -nodereuse:false` | 生成成功，0 警告 0 错误 |
| CK-002 | Non-command: 检视 `Dfo.Server.Abstractions.csproj` 的 `ProjectReference` | 仅 `Dfo.Server.Data`，未新增 |

### T-002: 九条出站消息接入编码器

- Depends on: T-001
- Covers: RQ-003, RQ-007
- Boundaries: BD-002, BD-003
- Inputs: sender 中八个私有编码方法、`GameInitialConnectionPayloadCodec.Encode`、`SelectCharacterResponseComposer.Encode`、`GameLoginPayloadCodec.EncodeSuccess`
- Outputs: 九个编码器，方法体未改写

#### Steps

1. 在 `Framing/Encoders/` 下每消息新增一个文件，继承 `ServerMessageEncoder<TMessage>`。
2. 八个单帧编码器把对应的现有方法体整体搬入并以单元素列表返回；`SelectCharacterResponseEncoder` 直接返回 `SelectCharacterResponseComposer.Encode(response.Result)` 的多帧结果。
3. 需要外部依赖的编码器经构造器注入：Channel 三个加密编码器注入 `IChannelEncryptionKeyProvider`，登录与初始连接编码器注入 `GameLoginOptions`。
4. 把 `GameLoginPayloadCodec.EncodeSuccess` 整体搬入 `GameLoginResponseEncoder`，并从 `GameLoginPayloadCodec` 删除该方法与其 `Networking.Configuration` using；`TryDecode` 与其私有 `TryReadDString` 不改动。
5. 登录失败体沿用现有抛出，措辞不改。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-003 | `cd /d/project/DNF && dotnet build DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/86JP.Server.slnx -nodereuse:false` | 生成成功，0 警告 0 错误 |
| CK-004 | Non-command: 检视 `GameLoginPayloadCodec.cs` | 无 `EncodeSuccess`，无 `Networking.Configuration` 依赖，`TryDecode` 与 `TryReadDString` 方法体未变 |

### T-003: 消除 sender 的分支与特判

- Depends on: T-002
- Covers: RQ-001, RQ-002, RQ-004, RQ-005, RQ-006
- Boundaries: BD-004, BD-005, BD-006, BD-007
- Inputs: 编码器注册记录；现有 sender 的发送路径
- Outputs: 只做查表、校验与发送的 sender

#### Steps

1. 新增 `ServerMessageEncoderRegistry`：构造期按消息 CLR 类型查重（重复即抛并指名该消息类型），解析编码器实例并冻结为只读字典，查询返回编码器与其端点或未命中。
2. 修改 `TouchSocketNetworkMessageSender.SendAsync`：取连接 → 按 `message.GetType()` 查表，未命中抛出并指名该消息 → 端点不匹配时以现有措辞抛出 → 按产出顺序逐帧发送。删除九分支 switch、`One` 辅助方法与八个私有编码方法。
3. 显式实现 `SendProjectionAsync`，转发到既有 `DeliverProjectionAsync`；删除 `SendAsync` 中对 `SelectCharacterProjectionDelivery` 的特判。`DeliverProjectionAsync`、`SendFrameAsync`、`ValidateProjectionDelivery` 三者方法体不改动。
4. 修改 `NetworkingModule`：注册编码器注册表与九条编码器注册，位置与入站注册相邻，同在 `Enabled` 判断之前。
5. 修改 `GameLoginCodecTests` 中 `EncodeSuccess` 的调用目标为登录编码器，字节断言不变。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-005 | `cd /d/project/DNF && dotnet test DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj -nodereuse:false` | 全部通过，含四组线级集成测试与三项投影投递测试，断言未改 |
| CK-006 | Non-command: 检视 `TouchSocketNetworkMessageSender.cs` | 无 `message switch`、无 `SelectCharacterProjectionDelivery` 特判、无私有编码方法 |
| CK-007 | Non-command: 检视 `Dfo.Server.Networking.csproj` 的 `ProjectReference` | 仅 `Dfo.Server.Abstractions` 与 `Dfo.Server.Data` |

### T-004: 补齐失败行为与接入范围的证据

- Depends on: T-003
- Covers: RQ-002, RQ-004, RQ-005, RQ-008
- Boundaries: BD-008, BD-009
- Inputs: 变更后的注册表与 sender
- Outputs: 失败行为测试与一次新消息接入的范围记录

#### Steps

1. 新增注册表测试：同一消息类型重复注册即失败并指名该类型；未注册类型查询返回未命中；注册记录携带声明的端点。
2. 新增发送失败测试：Channel 消息发往 Game 连接、Game 消息发往 Channel 连接两个方向各一项，断言抛出且信息同时含消息与端点；未注册消息发送时抛出且信息指明该消息；失败的登录响应编码时抛出且不产生字节。
3. 记录一次新出站消息接入的变更范围：临时增加一条消息的编码器与注册，记录所触及文件集合，随后撤销。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-008 | `cd /d/project/DNF && dotnet test DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/86JP.Server.slnx -nodereuse:false` | 0 失败；总数不少于 572 项基线并含新增项 |
| CK-009 | Non-command: 新消息接入探针所触及的文件集合 | 仅含新增编码器文件与 `NetworkingModule` 的一行注册；无既有编码器或既有注册项被修改 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-003 | BD-001, BD-004, BD-005 | CK-006 |
| RQ-002 | T-003, T-004 | BD-005, BD-006 | CK-009 |
| RQ-003 | T-002 | BD-002 | CK-005 |
| RQ-004 | T-003, T-004 | BD-005, BD-009 | CK-008 |
| RQ-005 | T-003, T-004 | BD-004, BD-009 | CK-008 |
| RQ-006 | T-003 | BD-005 | CK-005, CK-006 |
| RQ-007 | T-002 | BD-003 | CK-004 |
| RQ-008 | T-002, T-004 | BD-002, BD-009 | CK-008 |
| RQ-009 | T-001, T-003 | BD-001, BD-006 | CK-002, CK-007 |

## Risks and Rollback

- Risk: 端点校验从九个 `when` 子句抽成一处统一判定时被削弱为放行。缓解：拒绝措辞沿用原文使 diff 上显然未变，并由 CK-008 的两个方向测试独立证明。
- Risk: 单帧与多帧统一返回形状时包装层次出错，导致多发或少发帧。缓解：八个单帧编码器统一以单元素列表返回，`SelectCharacterResponse` 直接返回既有 composer 的结果；由既有线级集成测试的逐字节断言与投影测试证明。
- Risk: 删除 `SendAsync` 的投递特判后，若有调用点以 `SendAsync` 传递 delivery，将改为抛"无注册编码器"。缓解：全仓检索确认九处生产调用点全部使用 `SendProjectionAsync`，既有投影测试直接调用 `DeliverProjectionAsync`；实现后复检该结论。
- Risk: 搬迁 `EncodeSuccess` 时改动字节。缓解：整体搬入不改写方法体，由 `GameLoginCodecTests` 的既有字节断言与 GameLogin 线级集成测试证明。
- Rollback: 本成果全部改动限于任务工作树内且未提交；如需放弃，由父控制器执行到 `cb59bb1` 的精确回滚。

## Assumptions

- 编码器为无状态单例（其注入的配置与密钥来源本身为单例），可在注册表构造时一次性解析并冻结。
- `IChannelEncryptionKeyProvider` 在 Abstractions 中已可被 Networking 引用，编码器注入它不引入新的跨层依赖。
- `Dfo.Server.Networking.Tests` 已有 `InternalsVisibleTo`，新增注册表与编码器可保持 internal 并被测试访问。
