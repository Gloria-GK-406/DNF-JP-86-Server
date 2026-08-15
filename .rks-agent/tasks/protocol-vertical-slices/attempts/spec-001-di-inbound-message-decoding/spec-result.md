---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: DI-Driven Inbound Message Decoding

## Delivered

- 入站"线上帧键 → 类型化消息"的映射由 DI 注册的解包器提供。`ChannelNetworkPlugin.CreateMessage`（4 分支）与 `GameNetworkPlugin.CreateMessage`（10 分支）已消失，两个 plugin 各只剩一次注册表查表。
- 14 个入站协议各有一个解包器文件，全部转发既有 `*PayloadCodec` 静态方法；既有 codec 零改动。
- 解包器接缝、两个端点的注册记录、产出登记与 DI 注册扩展位于 `Dfo.Server.Abstractions/Networking/ClientMessageDecoding.cs`，使 Networking 与 Application 在不互相引用的前提下共见同一份注册声明。
- `MessageHandlerRegistry` 的启动检查由"目录每个枚举成员必须有 handler"换为"每个已注册解包器申报的产出类型必须有 handler"，缺口一次性全部报告。目录因此可以承载未接线的协议登记。
- `NetworkingModule.RegisterInboundDecoders` 成为一处可通览的入站路由表，每协议一行，位于 `Enabled` 判断之前，故检查在监听关闭时同样生效。

## Success Evidence

| Condition | Evidence |
|---|---|
| CSC-1 | `verification/completion-evidence.md`：`grep -rn "CreateMessage" Server/src/` 无匹配；14 条注册对应 14 个解包器文件 |
| CSC-2 | `verification/completion-evidence.md`：`git diff --stat` 对 `Framing/` 为空；Networking.Tests 121 项通过含四组线级集成测试，断言未改；`reviews/change-review.md` 的 14 协议逐条等价比对 |
| CSC-3 | `verification/completion-evidence.md`：`UnknownInboundFrameIntegrationTests` 以位置证明覆盖三种丢弃情形 |
| CSC-4 | `verification/completion-evidence.md`：type `0x0117` 接入探针实测 footprint，已精确撤销 |
| CSC-5 | `NetworkMessageDispatcherTests.BuildRegistry_AcceptsCataloguedMessageTypesThatAreNotWired` |
| CSC-6 | `NetworkMessageDispatcherTests.BuildRegistry_RejectsADecoderProductionThatNoHandlerConsumes` |
| CSC-7 | `verification/completion-evidence.md`：两个 csproj 的引用图；`git diff --name-only -- '*.csproj'` 为空；无诊断抑制 |
| CSC-8 | `verification/completion-evidence.md`：全量 572 项，507 通过 65 跳过 0 失败，对齐 563 基线 |

### 有意移除的断言

`NetworkMessageDispatcherTests.BuildRegistry_RejectsMissingMessageTypeRegistrations` 已删除。它断言的"`ClientMessageType` 每个成员都必须有已注册 handler"正是本成果依 TaskContract Scope/In 第 4 条有意移除的行为。其保护意图由 CSC-6 的闭合性检查承接，覆盖面从"目录完备"收窄到"已接线闭合"。同文件 `BuildProvider` 中按枚举回填 handler 的代码一并删除，那是为满足旧检查而存在的脚手架，非断言。

## Repository Result

- Changed paths：改 `Server/src/Dfo.Server.Application/Dispatching/MessageHandlerRegistry.cs`、`Server/src/Dfo.Server.Networking/NetworkingModule.cs`、`Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs`、`Server/tests/Dfo.Server.Application.Tests/Dispatching/NetworkMessageDispatcherTests.cs`；新增 `Server/src/Dfo.Server.Abstractions/Networking/ClientMessageDecoding.cs`、`Server/src/Dfo.Server.Networking/Framing/ChannelProtocol.cs`、`Server/src/Dfo.Server.Networking/Framing/Decoders/`（14 文件）、`Server/src/Dfo.Server.Networking/Runtime/ClientMessageDecoderRegistries.cs`、`Server/tests/Dfo.Server.Networking.Tests/Runtime/ClientMessageDecoderRegistryTests.cs`、`Server/tests/Dfo.Server.Networking.Tests/Integration/UnknownInboundFrameIntegrationTests.cs`，以及本尝试的流程制品。
- Baseline or accepted result：基于 `06b24e7`，由父控制器接受为一次已验证的本地结果提交。
- Parent rollback required：no

## Successor Guidance

- **出站编码尚未脱离 Networking。** `GameLoginPayloadCodec` 仍同时承载入站 `TryDecode` 与出站 `EncodeSuccess(GameLoginOptions)`，后者是它对 `Networking.Configuration` 的唯一依赖。第二个成果拆出站编码时须一并处理该文件的双重身份；在此之前它不能随切片迁出 Networking。
- **入站 codec 无传输依赖这一前提已被证实。** 除 `GameLoginPayloadCodec` 外，其余入站 codec 仅依赖 Data 的消息契约与 `BinaryPrimitives`（14 个解包器全部只需 `Dfo.Server.Abstractions.Networking` 与 `Dfo.Server.Data.Networking` 两个 using 即可编译）。根契约 Assumptions 第一条成立，第三个成果的切片落点不需重估。
- **接缝的落点已定，勿再迁移。** 解包器接缝在 Abstractions，实现在 Networking，闭合性检查在 Application。第三个成果把解包器迁入切片项目时，只搬实现，不动 Abstractions 中的接缝——否则 Networking 会失去对解包器契约的可见性。
- **产出申报与实际产出无机制约束。** 一个申报 A 却返回 B 的解包器不会被闭合性检查发现。第三个成果把解包器与消息 DTO 收进同一切片目录后，两者相邻可读，届时可评估是否值得加约束；本成果内不具备低成本手段。
- **`ClientMessageType` 目录现在可以领先于接线。** 新增协议时可先登记目录成员再补解包器与 handler，启动不会因此失败。但只要注册了解包器，其产出就必须有 handler。
- **基线脆性。** `Dfo.Server.Host.Tests` 曾在源检出与工作树连续跑全量测试时出现一次未复现的失败。本成果的多次运行均 4/4 通过。后续成果遇到同类瞬时失败应先单独重跑该套件再判定。

## Artifacts

- `spec.md`（RQ-001..RQ-009 / AC-001..AC-013 / EV-001..EV-007）
- `plan.md`（BD-001..BD-010 / T-001..T-005 / CK-001..CK-011）
- `reviews/change-review.md`（独立子代理复核，approved-with-findings，0 阻断；两项非阻断已修复）
- `verification/completion-evidence.md`（decision: pass）
