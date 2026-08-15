---
kind: rks-verification
decision: pass
---

# Completion Evidence: spec-002 出站编码由 DI 注册表驱动

## 被评估的主张

出站"类型化消息 → 帧"的编码已由 DI 注册的编码器提供；sender 的九分支 switch 与投递特判消失，只保留查表、端点校验与发送；端点归属由注册声明并在一处统一判定；`GameLoginPayloadCodec` 只剩入站职责；现有九条出站消息与投递路径的字节、帧序与失败隔离语义逐字不变。

评估对象为任务工作树 `codex/protocol-vertical-slices` 的未提交工作区状态，HEAD 仍为接受基线 `cb59bb1`。全部证据在复核补救（公共接缝补 null 校验、还原被误触碰的 `NetworkMessages.cs`）之后重新采集。

## 逐条核对

| ID | 条件 | 判定 | 证据 |
|---|---|---|---|
| CSC-1 | sender 中无按消息 CLR 类型选择编码的分支，也无投递类型特判 | pass | 检索确认无 `message switch`、无私有 `Encode*` 方法；文件中仅存的 `SelectCharacterProjectionDelivery` 出现于 `SendProjectionAsync`、`DeliverProjectionAsync`、`ValidateProjectionDelivery` 三处**参数声明**，非类型判定。 |
| CSC-2 | 九条出站消息的字节与基线逐字一致 | pass | 独立复核者取 `cb59bb1` 版本逐个对照九个编码器的常量、偏移、长度校验与异常措辞，确认一一对应；特别确认 `AccountCharacterRosterResponse` 仍为字面量 `command: 0x00`、三个加密编码器的 `compress` 标志（0x06→false、0x0A→true、0x12→true）与 classification `0x7C`/ack `1` 未变、`EncodeSuccess` 搬迁逐字未变。四组线级集成测试通过且断言未改。 |
| CSC-3 | 投递次序、尽力批次失败隔离与空值校验语义不变 | pass | `DeliverProjectionAsync`、`SendFrameAsync`、`ValidateProjectionDelivery` 三个方法体与 `cb59bb1` 逐字一致（复核者独立比对）；三项 `TouchSocketNetworkMessageSenderProjectionTests` 与 `SelectCharacterProjectionContractTests` 零 diff 且通过。 |
| CSC-4 | 端点校验等效，拒绝信息指明消息与端点 | pass | 九条注册的端点实参与基线九个 `when` 子句逐条吻合（4 Channel / 5 Game）；统一判定位于取连接之后、编码之前；拒绝措辞逐字未变。`OutboundSendFailureTests` 覆盖两个方向各一项。 |
| CSC-5 | 未注册消息被发送时显式失败并指明该消息 | pass | `OutboundSendFailureTests.AMessageWithNoRegisteredEncoderIsRejectedByName` 通过，断言含消息类型名与 "encoder"。 |
| CSC-6 | 接入新出站消息只需新增编码器与一行注册 | pass | 实测探针：新增一条出站消息并接入，编译通过后精确撤销。footprint 为新增 `ProbeOutboundResponseEncoder.cs` 一个文件，加上两处纯追加——目录枚举成员与消息记录（`NetworkMessages.cs`）、编码注册一行（`NetworkingModule.cs`）。无任何既有编码器、既有注册项或 sender 被修改；而在基线上该接入必须在 sender 的 switch 中新增一个带 `when` 子句的分支。撤销后 `NetworkMessages.cs` 已还原至 `cb59bb1`（不在变更集中）。 |
| CSC-7 | `GameLoginPayloadCodec` 入站部分不再依赖 `Networking.Configuration` | pass | 该文件现仅有 `System.Buffers.Binary` 与 `System.Text` 两个 using，无 `EncodeSuccess`；`TryDecode` 与 `TryReadDString` 方法体未变。 |
| CSC-8 | 登录失败体仍抛出而非产生字节 | pass | `OutboundSendFailureTests.AFailedLoginResponseIsNotEncodable` 通过，断言异常信息含 "failure body"。 |
| CSC-9 | 编译期分层未放宽 | pass | `Dfo.Server.Networking.csproj` 的 `ProjectReference` 仍仅为 Abstractions 与 Data；自 `cb59bb1` 起无 csproj 被修改；`TreatWarningsAsErrors` 仍为 true，零抑制（复核者独立验证）。 |
| CSC-10 | 无回归 | pass | 全量 580 项，515 通过、65 跳过、0 失败、0 警告。对齐 572 项基线并净增 8 项（4 项出站失败测试、4 项编码器注册表测试）。跳过数 65 与基线一致。**唯一被修改的既有测试**是 `GameLoginCodecTests`（2 插入 1 删除：一行 using、一行调用目标），其字节断言原样；其余既有测试零 diff。 |

## 实际运行的检查

- `dotnet test 86JP.Server.slnx` — Pvf 60（51/9）、Application 266（256/10）、Networking 129（129/0）、Infrastructure 121（75/46）、Host 4（4/0）；0 失败。
- `git rev-parse HEAD` — `cb59bb1`，与接受基线一致，未提交。
- sender 残留检索、`GameLoginPayloadCodec` using 检视、Networking csproj 引用图、`git diff --name-only cb59bb1 -- '*.csproj'`（空）。
- 独立子代理复核（`reviews/change-review.md`）：approved-with-findings，0 阻断；其自行取 `cb59bb1` 版本逐字节对照九个编码器、九条端点归属、三个投递方法体，并独立验证既有断言未被削弱。

## 已知且被接受的偏差与局限

- **测试落点偏离 Plan。** Plan 的 BD-009 把出站失败测试放在 `Runtime/`，实际落在 `Integration/`。原因是端点校验与未注册失败必须经真实连接才能取到带端点的连接上下文，该测试需要拉起真实 host，属集成性质。断言内容与 Plan 一致。
- **投递路径空值参数名改变。** 基线上投递经 `SendAsync` 进入，空 delivery 抛出的 `ArgumentNullException` 参数名为 `message`；现经 `SendProjectionAsync` 进入，参数名为 `delivery`。无测试或调用点依赖该参数名，属更准确的命名。
- **注册表测试在 `using var provider` 释放后返回其产物。** 仅因注册表在构造期即解析并冻结编码器实例才成立。写法与 spec-001 的解包器注册表测试同构；若将来注册表改为惰性解析，两处测试都需同时调整。
- `RegisterOutboundEncoders` 使用 `AddSingleton` 而非 `TryAdd`，与入站注册及 `ApplicationModule` 的既有写法一致，模块在组合根只注册一次。
- 基线已知的 `Dfo.Server.Host.Tests` 瞬时失败在本次验证的多次运行中均未复现。
