---
kind: rks-change-review
contract:
  path: ../TaskContract.md
  revision: 1
spec: ../spec.md
plan: ../plan.md
baseline: cb59bb1
reviewer: independent
state: complete
---

# 变更复核：DI 驱动的出站消息编码

## 结论

**approved-with-findings**

九个编码器的常量、字段偏移、长度校验与异常措辞经逐字节机械比对，与基线 `cb59bb1` 完全一致；九条端点归属与旧 `when` 子句逐条吻合；投递路径三个方法体逐字未变；既有测试断言仅有那一处被授权的机制性改动。**无阻断性发现。** 下列七项为非阻断性发现，其中 NB-004（工作区噪声）与 NB-006（CSC-6 证据缺失）建议在提交前处理。

## 阻断性发现

无。

以下五项是本次对抗性复核重点怀疑、逐条排除的假设，记录于此以证明排除过程而非仅结论：

| 怀疑 | 排除依据 |
|---|---|
| AccountCharacterRoster 的 command 被误搬为 `GameProtocol.Command`（0x01） | 基线与新版均为字面量 `command: 0x00`；`GameProtocol.Command = 0x01` 仅用于 GameExit 与 GameLogin，未被误用。见 `Framing/Encoders/AccountCharacterRosterResponseEncoder.cs:11` |
| 三个加密编码器的 `compress` 标志被搬错 | 0x06 → `compress: false`，0x0A → `true`，0x12 → `true`，与基线一一对应 |
| 端点归属在九行注册表中错配 | 九条逐条比对，四条 Channel、五条 Game，与基线九个 `when` 子句完全吻合（见下表） |
| `SelectCharacterResponse` 被多包一层单元素列表 | `SelectCharacterResponseComposer.Encode` 返回 `IReadOnlyList<byte[]>`，编码器直接 `return`，未经 `One(...)`。见 `SelectCharacterResponseEncoder.cs:12` |
| 有生产调用点以 `SendAsync` 传递 `SelectCharacterProjectionDelivery`，删除特判后将改抛 | 全仓检索：唯一以 `SendAsync` 传 delivery 的位置是 `INetworkServices.cs:24` 的接口默认实现本身；十处生产调用点（`SelectCharacterCommand.cs` 九处、`SelectCharacterLifecycleServices.cs` 一处）全部直接调用 `SendProjectionAsync`，而 `TouchSocketNetworkMessageSender` 已以完全匹配的签名接管该方法，默认实现不再被命中 |

## 逐字节比对：九个编码器 vs 基线

方法：`git show cb59bb1:.../TouchSocketNetworkMessageSender.cs` 取出八个私有编码方法与 `One`/`EncodeEncrypted`/`WriteFixedAscii`；`git diff` 取出 `GameLoginPayloadCodec.EncodeSuccess` 的被删块；对新旧两侧作标识符归一（`response.`/`message.` → `M.`，`EncodeEncrypted`/`ChannelEncryptedFrame.Encode` → `CE`）后剥空白，再按常量、偏移、校验三类逐行提取对照。

| # | 消息 | 关键常量与偏移 | 长度校验与措辞 | 结果 |
|---|---|---|---|---|
| 1 | `ChannelConnectResponse` | classification `0x7C`、messageNumber `0x0C`、checksum `0`、ack `1`、`new byte[36]`、key 写入 offset `4` | `> 32` → "The channel connect key cannot exceed 32 ASCII bytes." | 一致 |
| 2 | `ChannelScriptVersionResponse` | `CE(0x06, …, compress: false)`、`new byte[20]`、version 写入 offset `4` | `> 16` → "The channel script version cannot exceed 16 ASCII bytes." | 一致 |
| 3 | `ChannelScriptResponse` | `CE(0x0A, M.Content, …, compress: true)` | 无 | 一致 |
| 4 | `ChannelInfoResponse` | `checked(6 + Count * 48)`、count 写 `AsSpan(2,4)`、起始 offset `6`、Name `(offset,20)`、Max `(offset+20,4)`、Cur `(offset+24,4)`、Host `(offset+28,16)`、Port `(offset+44,4)`、步进 `48`、`CE(0x12, …, compress: true)` | `WriteFixedAscii` 截断逻辑逐字一致 | 一致 |
| 5 | `GameExitResponse` | `GameProtocol.Command` / `GameProtocol.ExitType` / `[Succeeded ? 0x01 : 0x00]` | 无 | 一致 |
| 6 | `GameInitialConnectionNotice` | 原样委托 `GameInitialConnectionPayloadCodec.Encode(message, options)` | 无 | 一致 |
| 7 | `GameLoginResponse` | `Succeeded ? EncodeSuccess(options) : throw`；`EncodeSuccess` 内 `checked(45 + ip.Len)`、`body[0]=0x01`、`body[1]=18`、`body[2]=0x00`、`body[3]=Environment`、`body[4]=0x00`、port `(5,4)`、iplen `(9,4)`、ip 写 `13`、`offset=13+len`、UnknownPort `(offset,4)` 与 `(offset+4,4)`、`GameProtocol.Command`/`LoginType` | "The observed Game LOGIN protocol has no failure body yet."（逐字未变）；`ArgumentNullException.ThrowIfNull(options)` 一并搬入 | 一致 |
| 8 | `AccountCharacterRosterResponse` | **`command: 0x00`**（非 `GameProtocol.Command` 的 0x01）、`AccountCharacterRosterNotificationType`、`AccountCharacterRosterPayloadCodec.Encode(Roster)` | 无 | 一致 |
| 9 | `SelectCharacterResponse` | `SelectCharacterResponseComposer.Encode(message.Result)` 原样返回 | 无 | 一致 |

`ChannelEncryptedFrame.Encode` 与基线私有 `EncodeEncrypted` 逐字一致：`ChannelPayloadCipher.Encrypt(plaintext, key, compress)` → `ChannelFrameEncoder.Encode(0x7C, messageNumber, 0, 1, body)`。

**多帧/单帧包装**：八个单帧编码器均恰好经 `One(...)`（基类 `protected static IReadOnlyList<byte[]> One(byte[] frame) => [frame]`，与基线 sender 私有 `One` 同形）产出一帧；第 9 条直接返回 composer 结果，无额外包装层。

## 端点归属逐条比对

| 消息 | 基线 `when` 子句 | 新注册表实参 (`NetworkingModule.cs:135-153`) | 结果 |
|---|---|---|---|
| `ChannelConnectResponse` | `== NetworkEndpointKind.Channel` | `NetworkEndpointKind.Channel` | 吻合 |
| `ChannelScriptVersionResponse` | `Channel` | `Channel` | 吻合 |
| `ChannelScriptResponse` | `Channel` | `Channel` | 吻合 |
| `ChannelInfoResponse` | `Channel` | `Channel` | 吻合 |
| `GameExitResponse` | `Game` | `Game` | 吻合 |
| `GameInitialConnectionNotice` | `Game` | `Game` | 吻合 |
| `GameLoginResponse` | `Game` | `Game` | 吻合 |
| `AccountCharacterRosterResponse` | `Game` | `Game` | 吻合 |
| `SelectCharacterResponse` | `Game` | `Game` | 吻合 |

**统一判定的位置**（`TouchSocketNetworkMessageSender.cs:19-32`）：`connections.Get(connectionId)` → `encoders.Find(message.GetType())` → 端点比较 → `entry.Encoder.Encode(message)`。判定在取连接之后、在编码之前，与基线 `when` 子句的求值时机（模式的 `when` 先于分支表达式）等价。

**措辞**：`$"Server message '{Name}' is not supported for endpoint '{Kind}'."` 与基线逐字相同（含换行处的字符串拼接位置）。

**精确类型 vs 模式匹配**：注册表按 `Type` 精确查表，基线按 `is` 模式匹配（含派生类）。九条消息在 `NetworkMessages.cs` 中全部声明为 `sealed record`，不存在派生类型，两种匹配等价。

**已知且经授权的行为差异**：基线中"消息类型未列入 switch"与"端点不匹配"共用 `_ =>` 分支，同抛"is not supported for endpoint"；新版将前者分离为"has no registered encoder"。这是 CSC-5/RQ-005 要求的新增可观察行为。全仓检索确认无既有测试断言旧措辞（`"not supported for endpoint"` 在 `Server/tests` 下零命中）。

## 投递语义比对

- `DeliverProjectionAsync`（`:58-98`）、`SendFrameAsync`（`:100-115`）、`ValidateProjectionDelivery`（`:117-138`）三个方法体与 `cb59bb1` **逐字一致**，包括三个 `ThrowIfNull` 的顺序、required-direct 先于 best-effort、`target.ConnectionId == requiredDirectConnectionId` 跳过、`catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) throw`、兜底 `catch { break; }` 及其注释原文、四条 `InvalidDataException` 措辞。
- 新的 `SendProjectionAsync`（`:47-56`）转发参数为 `(requiredDirectConnectionId, delivery, projectionComposer, SendFrameAsync, cancellationToken)`，与基线特判路径的 `(connectionId, delivery, projectionComposer, SendFrameAsync, cancellationToken)` 一一对应，且与基线一样**不预先调用 `connections.Get`**。
- 签名与 `INetworkServices.cs:20-24` 的默认实现完全匹配（`NetworkConnectionId, SelectCharacterProjectionDelivery, CancellationToken` → `ValueTask`），因此确实接管了该接口成员，默认实现不再被 `TouchSocketNetworkMessageSender` 命中。
- 三项既有投影测试（`TouchSocketNetworkMessageSenderProjectionTests`）直接调用 `internal static DeliverProjectionAsync`，未经 `SendAsync`，断言完全未改（`git diff cb59bb1 -- Server/tests/` 该文件零变更）。

## CSC-1..CSC-10 逐条核对表

| ID | 判定 | 依据 |
|---|---|---|
| CSC-1 | 通过 | `TouchSocketNetworkMessageSender.cs` 全文 139 行，无 `message switch`、无 `SelectCharacterProjectionDelivery` 的 `is` 特判、无任何私有编码方法与 `One` 辅助。全仓检索 `IServerNetworkMessage message` 仅命中接口声明与编码接缝；`ServerMessageType.` 在 `NetworkMessages.cs` 之外仅出现在一处 XML 注释中，无任何按枚举派发的残留 |
| CSC-2 | 通过 | 上文九条逐字节比对全一致；四组线级集成测试（`ChannelConnectIntegrationTests`、`ChannelControlMessagesIntegrationTests`、`GameExitIntegrationTests`、`GameLoginIntegrationTests`）与 `GameLoginCodecTests` 全部通过；`git diff cb59bb1 --stat -- Server/tests/` 显示既有测试文件仅 `GameLoginCodecTests.cs` 有 `2 insertions(+), 1 deletion(-)`，即一行 `using` 与一行调用目标，字节断言原样 |
| CSC-3 | 通过 | 三个投递方法体逐字未变；`TouchSocketNetworkMessageSenderProjectionTests`（3 项）与 `SelectCharacterProjectionContractTests` 均零 diff 且通过 |
| CSC-4 | 通过 | `OutboundSendFailureTests.AGameMessageIsRejectedOnAChannelConnection` 与 `.AChannelMessageIsRejectedOnAGameConnection` 两个方向各一项，经真实 Host、真实注册表、真实 sender 驱动，断言异常信息同时含消息名与端点名 |
| CSC-5 | 通过 | `OutboundSendFailureTests.AMessageWithNoRegisteredEncoderIsRejectedByName` 断言信息含消息名与 "encoder"；`ServerMessageEncoderRegistryTests.ReportsAMissForAnUnregisteredMessage` 覆盖查表侧 |
| CSC-6 | **结构成立，证据缺失** | 结构上确实成立：新增一条消息只需一个新文件 + `RegisterOutboundEncoders` 一行；`AddServerMessageEncoder<TMessage, TEncoder>` 的 `where TEncoder : ServerMessageEncoder<TMessage>` 约束使错配无法编译，九条既有注册与九个既有编码器互不耦合。但 plan T-004 步骤 3 要求的"探针接入 diff"未落在任何制品里（`sdd-execution.yaml` 声明的 `spec-result.md` 尚不存在）。见 NB-006 |
| CSC-7 | 通过 | `GameLoginPayloadCodec.cs` 现仅 `using System.Buffers.Binary; using System.Text;`，`Networking.Configuration` 的 using 已删且全文无该命名空间任何引用；`TryDecode` 与 `TryReadDString` 方法体在 diff 中零改动 |
| CSC-8 | 通过 | `GameLoginResponseEncoder.cs:20-21` 保留原措辞抛出，不产生字节；`OutboundSendFailureTests.AFailedLoginResponseIsNotEncodable` 断言之 |
| CSC-9 | 通过 | `Dfo.Server.Networking.csproj` 的 `ProjectReference` 仅 `Dfo.Server.Abstractions` 与 `Dfo.Server.Data`，未改动；`Dfo.Server.Abstractions.csproj` 未新增引用（`Microsoft.Extensions.DependencyInjection.Abstractions` 与 `Dfo.Server.Data` 均已存在）。`Server/Directory.Build.props:6` 的 `TreatWarningsAsErrors=true` 未动，全仓 `Server/src`、`Server/tests` 下零 `#pragma warning` / 零 `SuppressMessage`，两次全量运行零警告零错误 |
| CSC-10 | 通过 | 全量 580 项 / 515 通过 / 65 跳过 / **0 失败**，= 基线 572 + 8 新增项（4 项 `OutboundSendFailureTests` + 4 项 `ServerMessageEncoderRegistryTests`），跳过数 65 与基线完全对齐。既有断言改动仅一处，已在 CSC-2 说明。两次连续全量运行均未复现 `Dfo.Server.Host.Tests` 的已知瞬时失败 |

RQ 侧对应：RQ-001/002（CSC-1、CSC-6）、RQ-003（CSC-2）、RQ-004（CSC-4）、RQ-005（CSC-5）、RQ-006（CSC-3）、RQ-007（CSC-7）、RQ-008（CSC-8）、RQ-009（CSC-9）——均成立，RQ-002 的证据强度见 NB-006。

plan Risks 一节的四项风险经独立验证全部未兑现（对应上文"阻断性发现"表的前四行与第五行）。

## 非阻断性发现

**NB-001 —— 空 delivery 的 `ArgumentNullException.ParamName` 由 "message" 变为 "delivery"**
`TouchSocketNetworkMessageSender.cs:47`。基线路径 `SendProjectionAsync(id, null, ct)` → 接口默认实现 → `SendAsync` 的 `ThrowIfNull(message)`，paramName 为 `"message"`；新路径直达 `DeliverProjectionAsync` 的 `ThrowIfNull(delivery)`，paramName 为 `"delivery"`。异常类型与"以 faulted ValueTask 返回"的时序均不变，无测试断言 paramName，且 AC-009 关心的是 delivery **内部**的空值（仍为 `InvalidDataException`，逐字未变）。仅记录，不建议改。

**NB-002 —— 失败测试的落点偏离 plan BD-009**
plan 声明 `tests/Dfo.Server.Networking.Tests/Runtime/OutboundSendFailureTests.cs`，实际落在 `tests/Dfo.Server.Networking.Tests/Integration/OutboundSendFailureTests.cs`。实际落点更合适（该测试起真实 Host 与真实 TCP 连接，属集成级），但与已批准的 plan 不符，属应记录的计划漂移。

**NB-003 —— 注册表测试在 provider 释放后使用其产物**
`ServerMessageEncoderRegistryTests.cs:63-69`：`using var provider = services.BuildServiceProvider(); return provider.GetRequiredService<ServerMessageEncoderRegistry>();` —— provider 在 `Build` 返回时即被释放。当前可行仅因注册表在构造期一次性解析并冻结编码器实例；若日后注册表改为惰性解析，这四项测试会以 `ObjectDisposedException` 而非断言失败的形式崩溃，误导排查。建议把 provider 的生命周期延长到测试方法结束。

**NB-004 —— 工作区含三个仅行尾差异的"修改"**
`git status` 将 `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs` 列为已修改，但 `git diff cb59bb1` 对该文件产出零内容差异——纯 LF→CRLF 触碰。`NetworkingModule.cs` 与 `.rks-agent/.../multi-spec-map.md` 也带同样的行尾警告（这两个另有真实内容改动）。`NetworkMessages.cs` 落在本成果 Scope/Out（"`ServerMessageType` 的成员与取值"）之内，虽无实质改动，仍建议提交前 `git checkout --` 还原该文件，使成果提交的文件集与 plan 的 Owned Boundaries 精确一致。

**NB-005 —— 公共接缝 `ServerMessageEncoder<TMessage>.Encode` 对 null 无防护**
`ServerMessageEncoding.cs:32-36`：`message` 为 null 时 `message is not TMessage` 为真，随即 `message.GetType()` 抛 `NullReferenceException` 而非 `ArgumentNullException`。经 sender 调用不可达（`SendAsync` 先 `ThrowIfNull`），但该基类是 `Dfo.Server.Abstractions` 的 public API，第三方编码器的直接调用者会遇到它。补一行 `ArgumentNullException.ThrowIfNull(message)` 即可，不影响任何既有字节路径。

**NB-006 —— CSC-6 / RQ-002 / CK-009 的探针证据未落盘**
`sdd-execution.yaml` 声明的 `./spec-result.md` 不存在，plan T-004 步骤 3 要求的"临时接入一条消息、记录所触及文件集合、随后撤销"的记录无处可查。本次复核以结构检视独立确认了该条件成立（见 CSC-6 行），但 EV-001 要求的"一次新消息接入的变更范围记录"这一制品仍缺。建议执行方在 result 制品中补记，否则 verification 阶段将无 CK-009 的一手证据。

**NB-007 —— 注册扩展用 `AddSingleton` 而非 `TryAdd`（与入站同构，非本次引入）**
`ServerMessageEncoding.cs:73-77`。若同一 `IServiceCollection` 上重复执行 `NetworkingModule.ConfigureServices`，九条注册会翻倍，注册表构造期即抛"registered more than once"。既有入站的 `AddGameMessageDecoder`/`AddChannelMessageDecoder`（`ClientMessageDecoding.cs:65-66, 82-83`）行为完全相同，故属既有约定的一致延续而非本次回归。仅记录，不建议在本成果内改。

## 实际运行过的验证及结果

| # | 验证 | 结果 |
|---|---|---|
| V-1 | `dotnet test .../86JP.Server.slnx --nologo -nodereuse:false`（第一次） | 退出码 0。Infrastructure 121（75/46 跳过）、Application 266（256/10 跳过）、Networking 129（129/0）、Host 4（4/0）；Pvf 部分因输出被 `tail` 截断未捕获 |
| V-2 | 同上（第二次，完整捕获各项目汇总行） | 退出码 0。Pvf **60**（51/9 跳过）、Networking **129**（129/0）、Application **266**（256/10）、Infrastructure **121**（75/46）、Host **4**（4/0）。合计 **580 项 / 515 通过 / 65 跳过 / 0 失败**。零编译警告、零错误 |
| V-3 | 与基线对账 | 580 = 572 + 8（新增 4 + 4）；515 = 507 + 8；跳过 65 = 基线 65。无回归，无既有项消失 |
| V-4 | `Dfo.Server.Host.Tests` 瞬时失败 | 两次运行均 4/4 通过，未复现已知脆性 |
| V-5 | `git show cb59bb1:.../TouchSocketNetworkMessageSender.cs` 与九个编码器的机械常量对照（标识符归一 + 剥空白 + 正则提取常量/偏移/校验） | 新旧两侧常量集合一一对应，无遗漏、无多余、无数值差异 |
| V-6 | `git diff cb59bb1 --stat` | 5 个受追踪文件：`multi-spec-map.md`、`GameLoginPayloadCodec.cs`(−27/+?)、`NetworkingModule.cs`(+38)、`TouchSocketNetworkMessageSender.cs`(−160 净)、`GameLoginCodecTests.cs`(+2/−1)。无越界文件 |
| V-7 | `git diff cb59bb1 --stat -- Server/tests/` | 仅 `GameLoginCodecTests.cs`，2 插入 1 删除。证明既有断言（含三项投影测试、四组线级集成测试、`SelectCharacterProjectionContractTests`）零改动 |
| V-8 | 全仓检索 `SendProjectionAsync` / 以 `SendAsync` 传 delivery | 生产调用点 10 处全走 `SendProjectionAsync`；`SendAsync(…, delivery, …)` 唯一命中为接口默认实现自身 |
| V-9 | 全仓检索 `TreatWarningsAsErrors` / `NoWarn` / `#pragma warning` / `SuppressMessage` | 仅 `Directory.Build.props:6` 的 `true`；`Server/src` 与 `Server/tests` 下零抑制 |
| V-10 | csproj 引用图检视 | `Dfo.Server.Networking` → {Abstractions, Data}，未引用 `Dfo.Server.Application`；`Dfo.Server.Abstractions` → {Data} + 两个既有 `Microsoft.Extensions.*` 包，未新增 |
| V-11 | DI 生命周期检视 | 编码器注入的 `IChannelEncryptionKeyProvider`（`ApplicationModule.cs:42`，singleton）与 `GameLoginOptions`（`NetworkingModule.cs:48`，singleton 实例）均为单例，注册表在构造期从根 provider 一次性解析编码器不构成 captive dependency |
| V-12 | 可见性检视 | `EncodeSuccess` 由 `public`（旧 `GameLoginPayloadCodec`）收紧为 `internal`（新编码器），为收紧而非放宽；九个编码器 `internal sealed`；注册表 `internal sealed`；接缝在 Abstractions 为 `public`（跨程序集继承所必需）。无为编译通过而放宽可见性之处 |
| V-13 | 消息类型密封性检视 | 九条出站消息在 `NetworkMessages.cs` 均为 `sealed record`，精确类型查表与基线 `is` 模式匹配等价 |

---

复核范围说明：`.rks-agent/` 下制品按约定不计入代码复核，仅在 NB-004、NB-006 中作为证据落点被引用。
