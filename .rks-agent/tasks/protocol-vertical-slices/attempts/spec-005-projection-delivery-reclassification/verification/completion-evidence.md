---
kind: rks-verification
decision: pass
---

# Completion Evidence: spec-005 投递指令归位

## 被评估的主张

投递计划类型位于声明投递接缝的抽象单元内，不再实现消息接口、不再报告消息目录值；`SendProjectionAsync` 无回退实现，每个实现者必须自行实现；把投递计划传入 `SendAsync` 在编译期即不成立；选择角色的目录值不再被两个 CLR 类型共用；投递次序、失败隔离、空值校验与所产字节逐字不变；被调整的测试替身断言强度未下降。

评估对象为任务工作树 `codex/protocol-vertical-slices` 的未提交工作区状态，HEAD 仍为接受基线 `3c82821`。全部证据在复核补救（F-1 过时 `<remarks>` 修正）之后重新采集。

## 逐条核对

| ID | 条件 | 判定 | 证据 |
|---|---|---|---|
| CSC-1 | 投递计划位于声明投递接缝的抽象单元内 | pass | `grep -rn 'record SelectCharacterProjectionDelivery\|record ExplicitProjectionTarget' Server/src` 各恰好一处，均在 `Dfo.Server.Abstractions/Networking/INetworkServices.cs`（第 14、30 行），与 `INetworkMessageSender` 同文件；`Dfo.Server.Data` 中已无它们。 |
| CSC-2 | 不再实现消息接口，也不报告目录值 | pass | 该记录声明以 `BestEffortTargets)` 加分号结束，无基类列表、无 `MessageType` 成员。复核者独立确认。 |
| CSC-3 | 把投递计划当消息发送在编译期即不成立 | pass | `SelectCharacterProjectionDelivery` 不再实现 `IServerNetworkMessage`，而 `SendAsync` 的参数类型为 `IServerNetworkMessage`，故传入将是类型不匹配的编译错误而非运行期"has no registered encoder"。`SendProjectionAsync` 声明以分号结尾，无方法体。 |
| CSC-4 | 目录值不再被两个 CLR 类型共用 | pass | `grep -rn 'ServerMessageType.GameSelectCharacter' Server/src` 恰好一处命中（`SelectCharacterResponse`，`NetworkMessages.cs:150`）；复核者另确认 tests 侧零命中。 |
| CSC-5 | 投递次序、失败隔离与空值校验语义不变 | pass | 复核者取 `git diff 3c82821 --stat -- Server/src/Dfo.Server.Networking/` 在 F-1 修正前**输出为空**——`TouchSocketNetworkMessageSender.cs` 整文件零差异，四个投递方法体无需逐行比对即已确认未变。其另通读定位五项语义仍在原处：必送先于尽力、`break` 位于内层帧循环故不影响后继目标、重复目标 `continue` 跳过、取消 `catch` 置于通用 `catch` 之前、校验异常均为 `InvalidDataException`。F-1 修正后该文件的唯一差异是一段 `<remarks>` 的改写（5 行换 4 行），不触及任何可执行语句。 |
| CSC-6 | 投递所产字节不变 | pass | `TouchSocketNetworkMessageSenderProjectionTests`（3 项）与 `SelectCharacterProjectionContractTests` 通过，字节与结构断言未改。 |
| CSC-7 | 编译期分层未放宽 | pass | 无任何 csproj 被修改。`Dfo.Server.Abstractions` 仍只引用 `Dfo.Server.Data`；迁入所需的 `NetworkConnectionId`（Data.Networking）与 `ISelectCharacterSupplementalProjection`（Data.Game）在基线时就已被同一文件使用（现有 using 与 `ISelectCharacterProjectionComposer.Compose` 的签名），未引入新方向。`Dfo.Server.Networking` 仍不引用 Application 与 Protocol。 |
| CSC-8 | 无回归 | pass | 全量 580 项，515 通过、65 跳过、0 失败、0 警告。分布 Pvf 60、Application 266、Networking 45、Protocol 84、Infrastructure 121、Host 4，与基线逐项一致。复核者独立跑两轮均全绿。 |

## 测试替身调整的断言强度核对

7 处替身需自行实现 `SendProjectionAsync`。其中 **6 处为纯新增方法**（各 +6 或 +3 行、零删除行），未触碰任何既有断言：

- `ChannelConnectHandlerTests`、`ChannelControlMessageHandlerTests`、`GameExitMessageHandlerTests`、`GameLoginTests`、`OnlineUserServiceTests` 的记录型替身新增抛出实现——它们所测的 handler 不发投影，投影抵达属意外，抛出使其显形。
- `SessionGenerationOwnershipTests.NoopSender` 新增空实现——它是唯一真正接入清理服务的替身，基线上默认实现落到其空操作 `SendAsync`，与新空实现行为等价。

第 7 处 `SelectCharacterProjectionContractTests.RecordingSender` 是唯一有删除行的：其 `SendAsync` 由"记录并 `Assert.IsType`"改为抛出，同时新增直接实现 `SendProjectionAsync`。**测试方法体的 5 条断言逐字保留**。该改动为净增强——旧的 `Assert.IsType` 所证之事现由形参静态类型在编译期保证，而"投递不经消息路径"由未被断言升级为被断言。

**抛出未被触发的独立证明**（不依赖全绿）：复核者检索 `SendProjectionAsync` 在 `Server/src` 的调用宿主只有 `SelectCharacterCommand.cs` 与 `SelectCharacterLifecycleServices.cs`，再确认那 5 个测试文件均不构造 `SelectCharacterCommandHandler` 或 `SelectedSessionCleanupService`（`OnlineUserServiceTests` 用的是 `NoopCleanup`/`ClearingCleanup` 桩），故静态不可达。

## 实际运行的检查

- `dotnet build 86JP.Server.slnx -nodereuse:false` — 已成功生成，0 警告 0 错误。
- `dotnet test 86JP.Server.slnx -nodereuse:false` — 580 项，0 失败（分布见 CSC-8）。
- `git rev-parse HEAD` — `3c82821`，与接受基线一致，未提交。
- 定义处、目录值报告者、接缝形态、csproj 引用图的检索与代码审阅。
- 独立子代理复核（`reviews/change-review.md`）：approved-with-findings，**0 阻断**；其自行验证了 Networking 侧零差异、10 个 `INetworkMessageSender` 实现者与 10 处 `SendProjectionAsync` 的配平、抛出路径的静态不可达性，并自行完成两轮构建与全量测试。

## 已知且被接受的偏差与局限

- **复核发现 F-1 已修复。** `TouchSocketNetworkMessageSender.SendProjectionAsync` 的 `<remarks>` 仍在描述已被本次删除的接口默认实现——与本成果特意修正的 `ServerMessageEncoding.cs` 过时注释属同一类问题，被漏掉。已改写为陈述该方法存在的真实理由（投递自带复数收件人、无单一帧、无编码器）。修正后重跑构建与全量测试仍 580 项 0 失败。
- **5 个抛出型替身今天静态不可达。** 复核记录了一点前瞻：`SelectedSessionCleanupService` 的 try/catch 会把此类抛出吞成 `AggregateException`。今天无影响，未作改动。
- **`ExplicitProjectionTarget` 与 `SelectCharacterProjectionDelivery` 落在 `INetworkServices.cs` 内而非独立文件。** 该文件已是投递接缝的声明处，两个记录是 `SendProjectionAsync` 唯一的参数形态，同处一屏可读；`Dfo.Server.Abstractions` 本就有多处接口与其伴随记录同文件的既有写法。
- **迁入的记录保留了 `Dfo.Server.Data.Game.` 完全限定写法。** 与同文件 `ISelectCharacterProjectionComposer.Compose` 的既有风格一致，未引入新 using。
- 基线已知的 `Dfo.Server.Host.Tests` 瞬时失败在本次验证与复核者的两轮运行中均未复现。
