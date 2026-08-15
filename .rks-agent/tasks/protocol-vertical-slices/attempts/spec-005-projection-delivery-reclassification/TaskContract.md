---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-4
  - SC-8
  - SC-10
depends_on: []
---

# TaskContract: Projection Delivery Reclassification

## Context

### Source Request

根控制器在 spec-004 因边界前提被证伪而封存后，于 `safe-successor` 时确定的后继：把被错误归类为消息的投递指令归位到抽象接缝一侧。用户授权见根 TaskContract 的 Authorization 行，并在收到分析后明确授权按此重排继续：「你先按你的想法完成整个任务。然后我再做一次复审处理。……直接按你想的跑吧。」

### Current Situation

`Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs` 第 153-167 行定义两个类型：

```csharp
public sealed record ExplicitProjectionTarget(
    NetworkConnectionId ConnectionId,
    IReadOnlyList<ISelectCharacterSupplementalProjection> OrderedProjections);

public sealed record SelectCharacterProjectionDelivery(
    IReadOnlyList<ISelectCharacterSupplementalProjection> RequiredDirectProjections,
    IReadOnlyList<ExplicitProjectionTarget> BestEffortTargets) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSelectCharacter;
}
```

四项勘察结论确定它们被归错了类：

1. **无任何线上字段。** 其载荷是 `Data.Game` 的领域投影列表；`ExplicitProjectionTarget` 另带 `NetworkConnectionId`——收件人。其余任何消息 DTO 都不携带自己的收件人，收件人由 sender 的调用方给出。它表达的是一份扇出投递计划。
2. **`IServerNetworkMessage` 实现是残留的。** 全仓检索确认，唯一要求它的地方是 `Abstractions/Networking/INetworkServices.cs:24` 的接口默认实现 `SendProjectionAsync(...) => SendAsync(..., delivery, ...)`。spec-002 已使 `TouchSocketNetworkMessageSender` 显式实现 `SendProjectionAsync`，该默认实现在生产路径上已成死代码。
3. **它没有编码器。** 出站编码器注册表的 9 条注册中无此类型；真经 `SendAsync` 发送它会以"has no registered encoder"失败。即该默认实现即使被触发也不产生有效行为。
4. **它与 `SelectCharacterResponse` 共用 `ServerMessageType.GameSelectCharacter`。** 这是 spec-002 中迫使出站注册表必须按 CLR 类型建键的那个冲突，也是归类错误的直接征兆——两个 CLR 类型抢一个目录值。

三处测试假实现引用该类型：`SelectCharacterLifecycleTests` 与 `SessionGenerationOwnershipTests` 已显式实现 `SendProjectionAsync`；`SelectCharacterProjectionContractTests` 的假 sender 未覆写它，靠默认实现落到 `SendAsync` 后断言 `Assert.IsType<SelectCharacterProjectionDelivery>(message)`。

接受基线 `3c82821`：编译 0 警告 0 错误，全量测试 580 项 515 通过 / 65 跳过 / 0 失败。

## Goal

投递指令与消息分开：投递计划类型位于声明投递接缝的抽象单元内，不再实现消息接口、不再占用消息目录值；`SendProjectionAsync` 成为每个实现者必须自行实现的接口成员，把投递误当消息发送在编译期即不成立；且投递路径的可观察行为与字节逐字不变。

## Scope

### In

- 将 `SelectCharacterProjectionDelivery` 与 `ExplicitProjectionTarget` 迁入 `Dfo.Server.Abstractions.Networking`。
- 去掉 `SelectCharacterProjectionDelivery` 的 `IServerNetworkMessage` 实现与 `MessageType` 成员。
- 将 `INetworkMessageSender.SendProjectionAsync` 由带默认实现的成员改为普通接口成员。
- 调整因此需要自行实现该方法的测试假实现。
- 同步 `ServerMessageEncoding.cs` 中以"两者共用同一目录值"为据的 `<remarks>` 表述。

### Out

- 投递次序、失败隔离、空值校验与所产字节的任何改变。
- `ISelectCharacterSupplementalProjection` 及其实现的迁移或改写。它在 `Data.Game`，是领域投影而非线上类型。
- 出站编码器按 CLR 类型建键这一既有结论的改变。
- `ServerMessageType` 其余成员的增删或取值改变。
- 消息 DTO、消息 handler、命令与命令 handler 的切片归位。属后继成果。
- 任何协议语义、字节布局与加密参数的改变。

## Constraints

- 投递路径的可观察语义必须不变：required-direct 先于 best-effort、best-effort 目标失败时不影响后继目标、取消异常照原样上抛、与 required-direct 连接相同的目标被跳过、以及投递前的空值校验及其异常类型。
- 投递所产字节必须逐字不变。
- 编译期分层不得放宽：`Dfo.Server.Abstractions` 不得引用 `Dfo.Server.Protocol` 或 `Dfo.Server.Application`；`Dfo.Server.Networking` 不得引用 Application 与 Protocol。
- 不得以放宽类型、抑制诊断或关闭 `TreatWarningsAsErrors` 的方式换取编译通过。
- 既有 580 项测试须继续通过。仅当某测试假实现因接口成员形态改变而必须自行实现该方法时方可调整该假实现，且其断言强度不得下降。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| CSC-1 | 投递计划类型位于声明投递接缝的抽象单元内 | 该类型所在文件与命名空间的代码审阅 |
| CSC-2 | 投递计划类型不再实现消息接口，也不再报告消息目录值 | 该类型定义的代码审阅 |
| CSC-3 | 把投递计划当作消息发送在编译期即不成立 | `SendAsync` 不接受该类型的证据；`SendProjectionAsync` 无默认实现的代码审阅 |
| CSC-4 | `ServerMessageType.GameSelectCharacter` 不再被两个 CLR 类型共用 | 全仓检索该枚举成员的实现者 |
| CSC-5 | 投递路径的次序、失败隔离与空值校验语义不变 | 既有投影测试通过且断言未削弱 |
| CSC-6 | 投递所产字节不变 | 既有线级投影测试通过且断言未改 |
| CSC-7 | 编译期分层未放宽 | 各 csproj 引用图 |
| CSC-8 | 无回归 | 全量测试对齐 580 项基线，0 失败，并说明任何测试假实现的调整 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 复用根 TaskContract 的 `task` 行（「按ssd跑完吧……」），并经用户对本次重排的明确授权：「你先按你的想法完成整个任务。然后我再做一次复审处理。……直接按你想的跑吧。我后面会处理掉不和谐的地方。」 | 在任务工作树 `codex/protocol-vertical-slices` 内完成投递指令的归类修正与接缝收紧及相应测试调整，产出一次已审阅并已验证的本地结果提交。该成果落在根 Goal 与 Scope/In 之内，未扩张根授权范围。 |

## Requires Confirmation

- 合并、推送、发起 PR，或对 `main` 与其他分支的写入。
- 改变投递次序、失败隔离或所产字节。
- 迁移或改写 `ISelectCharacterSupplementalProjection` 及其实现。

## Assumptions

- `NetworkConnectionId` 在 `Dfo.Server.Data.Networking`，`ISelectCharacterSupplementalProjection` 在 `Dfo.Server.Data.Game`，两者均已被 `Dfo.Server.Abstractions` 直接引用（`INetworkServices.cs` 现有 using 与 `ISelectCharacterProjectionComposer` 的签名），故迁移不引入新的引用方向。
- 去掉接口默认实现属收紧：现有全部生产实现者均已显式实现该方法，不存在依赖回退语义的生产路径。
- 基线中 `Dfo.Server.Host.Tests` 有一次未复现的瞬时失败（见根 map 的 `baseline_evidence`）。同类瞬时失败不得计为本成果的回归，但须在结果中如实记录其发生。
