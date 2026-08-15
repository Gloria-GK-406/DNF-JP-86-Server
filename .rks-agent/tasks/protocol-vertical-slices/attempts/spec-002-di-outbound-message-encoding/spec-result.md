---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: DI-Driven Outbound Message Encoding

## Delivered

- 出站编码由 DI 注册的编码器提供。`TouchSocketNetworkMessageSender.SendAsync` 的九分支 `message switch`、`SelectCharacterProjectionDelivery` 特判与八个私有编码方法全部消失；该方法现只做查表、端点校验与逐帧发送。
- 九条出站消息各有一个编码器，方法体自 sender 整体搬入未改写；三个加密 Channel 编码器共用一个 `ChannelEncryptedFrame` 信封辅助。
- **端点归属从编码里分离出来**：基线上九个 `when connection.Context.EndpointKind == X` 子句把"编成什么字节"和"允许发往哪个端点"绑在同一个模式里；现由注册记录声明端点，sender 一处统一判定。拒绝措辞逐字未变。
- `SendProjectionAsync` 由接口默认实现改为显式实现，投递路径不再经 `SendAsync` 绕行，特判随之消失。`DeliverProjectionAsync` 等三个方法体逐字未动。
- `GameLoginPayloadCodec.EncodeSuccess` 迁入登录编码器，该 codec 只剩入站职责，对 `Networking.Configuration` 的依赖消失。
- `NetworkingModule.RegisterOutboundEncoders` 成为一处可通览的出站路由表，每消息一行且端点在同一行可见。

## Success Evidence

| Condition | Evidence |
|---|---|
| CSC-1 | `verification/completion-evidence.md`：sender 残留检索 |
| CSC-2 | 同上：复核者取 `cb59bb1` 逐字节对照九个编码器；四组线级集成测试断言未改且通过 |
| CSC-3 | 同上：三个投递方法体与 `cb59bb1` 逐字一致；三项投影测试零 diff 且通过 |
| CSC-4 | 同上：九条端点实参与基线九个 `when` 子句逐条吻合；`OutboundSendFailureTests` 两个方向 |
| CSC-5 | `OutboundSendFailureTests.AMessageWithNoRegisteredEncoderIsRejectedByName` |
| CSC-6 | `verification/completion-evidence.md`：新出站消息接入探针实测 footprint，已精确撤销 |
| CSC-7 | 同上：`GameLoginPayloadCodec` 仅剩两个 `System` using，无 `EncodeSuccess` |
| CSC-8 | `OutboundSendFailureTests.AFailedLoginResponseIsNotEncodable` |
| CSC-9 | `verification/completion-evidence.md`：引用图与 csproj 零改动 |
| CSC-10 | 同上：全量 580 项，515 通过 65 跳过 0 失败 |

### 被修改的既有测试

仅 `GameLoginCodecTests`（2 插入 1 删除）：`EncodeSuccess` 随出站职责迁走，测试的调用目标改为登录编码器，并补一行 using。**字节断言原样未动**。这是机制性改动而非断言改动——它证明的仍是同一段字节。其余既有测试零 diff。

## Repository Result

- Changed paths：改 `Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs`、`Server/src/Dfo.Server.Networking/NetworkingModule.cs`、`Server/src/Dfo.Server.Networking/Framing/GameLoginPayloadCodec.cs`、`Server/tests/Dfo.Server.Networking.Tests/Framing/GameLoginCodecTests.cs`；新增 `Server/src/Dfo.Server.Abstractions/Networking/ServerMessageEncoding.cs`、`Server/src/Dfo.Server.Networking/Framing/Encoders/`（9 编码器 + 1 信封辅助）、`Server/src/Dfo.Server.Networking/Runtime/ServerMessageEncoderRegistry.cs`、`Server/tests/Dfo.Server.Networking.Tests/Runtime/ServerMessageEncoderRegistryTests.cs`、`Server/tests/Dfo.Server.Networking.Tests/Integration/OutboundSendFailureTests.cs`，以及本尝试的流程制品。
- Baseline or accepted result：基于 `cb59bb1`，由父控制器接受为一次已验证的本地结果提交。
- Parent rollback required：no

## Successor Guidance

- **编解码已完全脱离 Networking 的传输职责，切片重排的前置已满足。** 入站 14 个解包器与出站 9 个编码器现在都只依赖 Abstractions 的接缝与 Data 的消息契约；`GameLoginPayloadCodec` 的双重身份已拆除，它可以随 Login 切片整体搬走。Networking 现在真正只剩传输：帧头拆装、TouchSocket 适配、连接注册表、发送与加密原语。
- **两个接缝必须留在 Abstractions，不随切片搬走。** `ClientMessageDecoding.cs` 与 `ServerMessageEncoding.cs` 是 Networking 与切片项目之间的唯一通路；把它们搬进切片项目会使 Networking 失去对编解码契约的可见性。
- **切片的自然内容已经成型。** 以 GameLogin 为例，其切片应含：消息 DTO（`GameLoginRequest`/`GameLoginResponse`）、`GameLoginPayloadCodec`、`GameLoginMessageDecoder`、`GameLoginResponseEncoder`、`GameLoginHandler`、`LoginGameUserCommand` 与其 handler。两条注册行分别属于入站与出站路由表。
- **仍留在 Networking 的编解码相关物需要单独判定归属。** `SelectCharacterResponseComposer`、`SelectCharacterStaticBodyFamilies`（843 行）、`SelectCharacterStaticPayloadCodec`、`SelectCharacterSupplementalProjectionCodec`、`SelectCharacterProjectionComposer`、`AccountCharacterRosterPayloadCodec`、`GameInitialConnectionPayloadCodec` 属协议内容，应随各自切片走；`ChannelPayloadCipher`、`ChannelEncryptedFrame`、`FrameEncoders`、`DnfFrameProtocol`、`*FrameRequestInfo`、适配器属传输，留在 Networking。`ChannelEncryptedFrame` 被三个 Channel 编码器共用，若三者分入不同切片，它需要一个共享落点。
- **两个路由表现在是新增协议的唯一注册点。** 入站 `RegisterInboundDecoders`、出站 `RegisterOutboundEncoders`，均在 `NetworkingModule` 且位于 `Enabled` 判断之前。切片重排若改变注册落点，须保持这两点：注册无条件生效、且入站产出仍被 `MessageHandlerRegistry` 的闭合性检查覆盖。
- **`ServerMessageType` 现在确认是纯目录。** 无任何代码按它派发；出站按 CLR 类型建键。该枚举保留以承担协议全量登记，其成员可以领先于接线。

## Artifacts

- `spec.md`（RQ-001..RQ-009 / AC-001..AC-012 / EV-001..EV-007）
- `plan.md`（BD-001..BD-009 / T-001..T-004 / CK-001..CK-009）
- `reviews/change-review.md`（独立子代理复核，approved-with-findings，0 阻断；两项非阻断已修复，其余记入验证证据的已知偏差）
- `verification/completion-evidence.md`（decision: pass）
