---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-2
  - SC-3
  - SC-4
depends_on: []
---

# TaskContract: DI-Driven Outbound Message Encoding

## Context

### Source Request

根任务的第二个有界成果，由根控制器在接受 spec-001 后于 `safe-successor` 时确定：出站编码由 DI 注册表驱动。用户授权见根 TaskContract 的 Authorization 行。

### Current Situation

出站编码集中在 `Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs`：

- 第 25-34 行：对 `SelectCharacterProjectionDelivery` 的 CLR 类型特判，命中则走 `DeliverProjectionAsync` 并直接返回。
- 第 37-60 行：九个分支的 `message switch`，每个分支形如 `TMessage response when connection.Context.EndpointKind == X => ...`，即**编码与端点校验写在同一个模式里**；落空分支抛出"该消息不支持该端点"。
- 第 154-234 行：八个私有编码方法，加上外部的 `GameInitialConnectionPayloadCodec.Encode`、`SelectCharacterResponseComposer.Encode`。

九个分支的产出不同构：八个产出单帧（经 `One(...)` 包成单元素列表），`SelectCharacterResponse` 经 `SelectCharacterResponseComposer.Encode` 产出多帧。

三项勘察结论决定了本成果的形态：

1. **`ServerMessageType` 在 `NetworkMessages.cs` 之外零引用。** 全解决方案检索确认没有任何代码按出站枚举派发、查表或分支。它当前纯粹是协议目录。
2. **枚举无法作为出站派发键。** `SelectCharacterResponse` 与 `SelectCharacterProjectionDelivery` 都自报 `ServerMessageType.GameSelectCharacter`，两个 CLR 类型共用一个枚举值。出站消息由服务端代码构造、调用点静态已知类型，因此按 CLR 类型建键既可行又是唯一可行的——这与入站必须按线上帧键建键的理由相反而结论对称。
3. **投递特判可无损消除。** `INetworkMessageSender` 已有 `SendProjectionAsync` 方法，其接口默认实现转发给 `SendAsync`；`TouchSocketNetworkMessageSender` 未覆写它，故投递特判是经该默认实现绕回来的。全部生产调用点（`SelectCharacterCommand.cs` 八处、`SelectCharacterLifecycleServices.cs` 一处）均已直接调用 `SendProjectionAsync`，无一处以 `SendAsync` 传递 delivery。

编码侧的外部依赖：`GameLoginOptions`（登录成功体与初始连接通知）、`IChannelEncryptionKeyProvider` 与 `ChannelPayloadCipher`（Channel 载荷 AES-128-ECB + zlib + 当日密钥）、`ChannelFrameEncoder` / `GameServerFrameEncoder`（组帧）、`ISelectCharacterProjectionComposer`（投递组帧，已是 Abstractions 接缝）。

`GameLoginPayloadCodec` 同时承载入站 `TryDecode` 与出站 `EncodeSuccess(GameLoginOptions)`，是入站 codec 中唯一依赖 `Networking.Configuration` 的一个（spec-001 Successor Guidance 记载）。

接受基线 `cb59bb1`：编译 0 警告 0 错误，全量测试 572 项 507 通过 / 65 跳过 / 0 失败。

## Goal

出站"类型化消息 → 帧"的编码由 DI 注册的编码器提供，sender 中按消息 CLR 类型选择编码的分支与投递特判一并消失；sender 只保留查表、端点校验与发送；接入或移除一个出站消息的编码只需增删该消息自己的编码器与一条注册，不触及既有文件；且现有九条出站消息与投递路径产生的字节与投递次序逐字不变。

## Scope

### In

- 引入按出站消息 CLR 类型索引的编码器注册与查表，覆盖现有九条出站消息。
- 编码器声明其所属端点，使 sender 的端点校验成为一处统一判定，不再散在九个 `when` 子句里。
- 消除 `TouchSocketNetworkMessageSender.SendAsync` 的 `message switch` 与 `SelectCharacterProjectionDelivery` 特判；由显式实现 `SendProjectionAsync` 承接投递路径。
- 现有八个私有编码方法与两个外部编码入口迁入各自的编码器；`GameLoginPayloadCodec` 的出站 `EncodeSuccess` 与入站 `TryDecode` 拆开，使入站部分不再依赖 `Networking.Configuration`。
- 编码器的 DI 注册落点与 Networking 侧到达它的接缝（Networking 不得因此引用 Application）。
- 相应测试：既有出站字节测试的接入方式调整、注册与查表的单测、端点校验与未注册消息的失败测试。

### Out

- 目录重排、新建项目、文件搬迁。属根任务第三个成果。
- 任何出站字节布局、加密参数、组帧规则与投递次序的改变。
- `ServerMessageType` 的成员与取值；本成果不删除它，也不使其成为派发键。
- 入站解包（spec-001 已完成的机制）。
- 登录失败体仍不可编码这一现状——不在本成果内补齐。
- 消息 handler、命令与领域服务的任何改动，包括 `SelectCharacterCommandHandler` 的投递调用点。

## Constraints

- 现有九条出站消息与投递路径产生的字节必须逐字不变，包括 Channel 载荷的 AES-128-ECB + zlib + 当日密钥、Game 出站 15 字节头、`SelectCharacterResponse` 的多帧序列与其顺序。
- 投递路径的可观察语义必须不变：required-direct 先于 best-effort、best-effort 目标失败时 `break` 当前目标而不影响后继目标、取消异常照原样上抛、与 required-direct 连接相同的目标被跳过、以及投递前的空值校验及其异常类型。
- 端点校验必须保持等效：Channel 消息只能发往 Channel 连接，Game 消息只能发往 Game 连接，违反时抛出且信息指明消息与端点。
- 登录失败体仍必须抛出而非产生字节。
- 编译期分层不得放宽：`Dfo.Server.Networking` 不得引用 `Dfo.Server.Application`。
- 不得以放宽类型、放宽可见性要求或抑制诊断的方式换取编译通过；`TreatWarningsAsErrors` 不得关闭。
- 既有 572 项测试须继续通过；仅当某测试断言的行为本身被本成果有意移除时方可修改其断言，且须在结果中说明。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| CSC-1 | `TouchSocketNetworkMessageSender` 中不存在按消息 CLR 类型选择编码的分支，也不存在对投递类型的特判 | 该文件的代码审阅，加上全仓检索无等价分支残留 |
| CSC-2 | 现有九条出站消息产生的字节与基线逐字一致 | 既有出站字节测试与四组线级集成测试通过，断言未改 |
| CSC-3 | 投递路径的次序、best-effort 失败隔离与空值校验语义不变 | `TouchSocketNetworkMessageSenderProjectionTests` 与 `SelectCharacterProjectionContractTests` 通过，断言未改 |
| CSC-4 | 端点校验等效：跨端点发送被拒绝且信息指明消息与端点 | 跨端点发送两个方向各一项的测试 |
| CSC-5 | 未注册的出站消息被发送时显式失败并指明是哪个消息 | 针对未注册消息的测试 |
| CSC-6 | 接入一条新出站消息只需新增该消息自己的编码器与一行注册 | 一个探针消息的接入 diff，仅含新增文件与注册行 |
| CSC-7 | `GameLoginPayloadCodec` 的入站部分不再依赖 `Networking.Configuration` | 该文件（或其拆分后的入站部分）的 using 与引用检视 |
| CSC-8 | 登录失败体仍抛出而非产生字节 | 针对失败体的测试或既有等效断言 |
| CSC-9 | 编译期分层未放宽 | 各 csproj 引用图，`Dfo.Server.Networking` 不引用 `Dfo.Server.Application` |
| CSC-10 | 无回归 | 全量测试对齐 572 项基线，0 失败，并说明任何断言改动 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 复用根 TaskContract 的 `task` 行：承接 Agent 提出的三步方案后，用户答「按ssd跑完吧。可以选multispec来跑，一个任务多个spec。或者如果你喜欢也可以多个spec单独跑。」，并在 spec-001 接受后答「继续吧」 | 在任务工作树 `codex/protocol-vertical-slices` 内完成出站编码的 DI 化及其测试，产出一次已审阅并已验证的本地结果提交。该成果完整落在根 Scope/In 第 2 条之内，未扩张根授权范围。 |

## Requires Confirmation

- 合并、推送、发起 PR，或对 `main` 与其他分支的写入。
- 任何超出本成果边界的改动（目录重排、出站字节语义、补齐登录失败体）。

## Assumptions

- 出站消息由服务端代码构造，调用点静态已知其 CLR 类型，故按 CLR 类型建键不引入任何动态查表风险；这与 `CommandBus` 按命令 CLR 类型建键的既有结论一致。
- `SelectCharacterProjectionDelivery` 的投递是投递策略而非编码，故由显式 `SendProjectionAsync` 承接，而不进入编码器注册表。
- 编码器可能需要注入 `GameLoginOptions` 与 `IChannelEncryptionKeyProvider`，故编码器为 DI 解析的实例而非静态方法。
- 基线中 `Dfo.Server.Host.Tests` 有一次未复现的瞬时失败（见根 map 的 `baseline_evidence`）。同类瞬时失败不得计为本成果的回归，但须在结果中如实记录其发生。
