---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Projection Delivery Reclassification

## Implementation Approach

Spec 的核心成果是"投递计划不再是消息，且误用它的路径在编译期即被关闭"。必要支撑是不引入新的依赖方向与测试替身自行满足新接缝；周边保证是投递语义与字节不变、断言强度未降、全量测试对齐基线。

最小可行路径基于三项已勘察的项目事实：`Dfo.Server.Abstractions` 已直接引用 `NetworkConnectionId`（`INetworkServices.cs` 的现有 using）与 `ISelectCharacterSupplementalProjection`（`ISelectCharacterProjectionComposer.Compose` 的参数），故两个记录迁入该单元不引入新依赖方向；生产实现者只有 `TouchSocketNetworkMessageSender` 且已显式实现 `SendProjectionAsync`，故去掉默认实现是纯收紧；三个测试替身中只有 `RecordingSender` 依赖默认实现。

改动集中在一个原子步骤：三处生产变更（两个记录搬家并去消息身份、接缝去默认实现、一处 `<remarks>` 同步）必须同时发生，因为去掉默认实现会立刻使 `RecordingSender` 不可编译，而保留 `IServerNetworkMessage` 又会让 `SendAsync` 继续接受投递计划——分步只会产生不可编译的中间态。故 T-001 承担全部实现，T-002 只采集证据。

两个记录的落点选 `INetworkServices.cs` 本身而非新建文件：它们是 `SendProjectionAsync` 唯一的参数形态，与该方法同处一屏可读，且该文件已是投递接缝的声明处。

`RecordingSender` 的调整方向已定死：`SendProjectionAsync` 直接实现并记录参数，第 25-29 行五条断言逐字保留；`SendAsync` 改为抛出，断言投递路径不经消息发送——这比原先的 `Assert.IsType` 更强，因为原先只能证明"到达的是这个类型"，现在证明"根本不走那条路"。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs` | 删除 `ExplicitProjectionTarget` 与 `SelectCharacterProjectionDelivery` 两个记录及其 `Data.Game` 限定引用 | 现文件第 153-167 行 | 只含消息与身份契约的数据契约文件 |
| BD-002 | `Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs` | 接收两个记录（去消息接口与 `MessageType`）；`SendProjectionAsync` 去默认实现 | BD-001 移出的定义 | 投递接缝与其参数形态同处一处 |
| BD-003 | `Server/src/Dfo.Server.Abstractions/Networking/ServerMessageEncoding.cs` | 同步 `<remarks>` 中以"两者共用目录值"为据的表述 | 现文件第 8-17 行 | 与新事实一致的说明 |
| BD-004 | `Server/tests/Dfo.Server.Application.Tests/Networking/SelectCharacterProjectionContractTests.cs` | `RecordingSender` 直接实现 `SendProjectionAsync`；`SendAsync` 改为抛出 | 现文件 | 断言强度不低于调整前的替身 |
| BD-005 | `.rks-agent/.../spec-005-projection-delivery-reclassification/verification/completion-evidence.md` | 新建；归位、编译期不成立、枚举独占、语义不变与引用图证据 | T-001 的检查输出 | 验证证据 |

## Ordered Tasks

### T-001: 投递计划归位且误用路径在编译期关闭

- Covers: RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-007, RQ-009
- Boundaries: BD-001, BD-002, BD-003, BD-004
- Inputs: `NetworkMessages.cs` 现有两个记录定义、`INetworkServices.cs` 现有接缝、`SelectCharacterProjectionContractTests.cs` 现有替身
- Outputs: 归位后可编译且全量测试通过的解决方案

#### Steps

1. 自 `NetworkMessages.cs` 删除 `ExplicitProjectionTarget` 与 `SelectCharacterProjectionDelivery` 及其上方的 `<summary>`/`<remarks>` 注释。
2. 在 `INetworkServices.cs` 中 `INetworkMessageSender` 之前加入这两个记录，注释原文保留；`SelectCharacterProjectionDelivery` 去掉 `: IServerNetworkMessage` 与 `public ServerMessageType MessageType => ...;` 成员；因所在命名空间已 `using Dfo.Server.Data.Networking`，`NetworkConnectionId` 无需限定，`ISelectCharacterSupplementalProjection` 保持其现有的 `Dfo.Server.Data.Game.` 限定写法以与同文件 `ISelectCharacterProjectionComposer` 一致。
3. 将 `SendProjectionAsync` 由 `... => SendAsync(requiredDirectConnectionId, delivery, cancellationToken);` 改为以分号结尾的普通接口成员。
4. 在 `ServerMessageEncoding.cs` 的 `<remarks>` 中，删去"目录无法作键因为两者共用同一值"这一依据，保留"出站消息由服务端构造、调用点静态已知类型"这一仍然成立的理由。
5. 在 `SelectCharacterProjectionContractTests.RecordingSender` 中实现 `SendProjectionAsync(NetworkConnectionId, SelectCharacterProjectionDelivery, CancellationToken)`，记录 `ConnectionId` 与 `Delivery`；`SendAsync` 改为 `throw new InvalidOperationException("Projection delivery must not travel through the message send path.")`。测试方法体第 25-29 行五条断言逐字不动，第 18 行的 `((INetworkMessageSender)sender)` 强制转换保留。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx -nodereuse:false` | 已成功生成，0 警告 0 错误 |
| CK-002 | `dotnet test Server/86JP.Server.slnx -nodereuse:false` | 580 项，0 失败 |
| CK-003 | Non-command: `SelectCharacterProjectionContractTests` 调整前后的断言比对 | 第 25-29 行五条断言逐字未变；新增的 `SendAsync` 抛出使原 `Assert.IsType` 所证之事被更强的判定替代 |

### T-002: 归位、编译期关闭与语义不变的成果证据

- Depends on: T-001
- Covers: RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-008
- Boundaries: BD-005
- Inputs: T-001 的产物
- Outputs: 可供复核与验证引用的证据

#### Steps

1. 检索两个记录的定义处，确认位于 `Dfo.Server.Abstractions.Networking` 且 `Dfo.Server.Data` 中已无它们。
2. 检视 `SelectCharacterProjectionDelivery` 定义，确认无 `IServerNetworkMessage` 与 `MessageType`。
3. 检索报告 `ServerMessageType.GameSelectCharacter` 的类型，确认恰有一个。
4. 检视 `INetworkMessageSender.SendProjectionAsync`，确认无方法体；论证把投递计划传入 `SendAsync` 因参数类型不匹配而无法编译。
5. 取全部 csproj 的 `ProjectReference` 生成引用图，确认 `Dfo.Server.Abstractions` 仍只引用 `Dfo.Server.Data`。
6. 运行全量测试并记录分项计数。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `grep -rn 'record SelectCharacterProjectionDelivery\|record ExplicitProjectionTarget' Server/src --include=*.cs` | 各恰好一处，均在 `Dfo.Server.Abstractions/Networking/` |
| CK-005 | `grep -n 'IServerNetworkMessage\|MessageType' Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs` | 仅 `SendAsync` 的参数声明命中，投递计划定义处无命中 |
| CK-006 | `grep -rn 'ServerMessageType.GameSelectCharacter' Server/src --include=*.cs` | 恰好一处（`SelectCharacterResponse`） |
| CK-007 | `grep -o 'Include="[^"]*csproj"' Server/src/Dfo.Server.Abstractions/Dfo.Server.Abstractions.csproj` | 仅 `Dfo.Server.Data` |
| CK-008 | `dotnet test Server/86JP.Server.slnx -nodereuse:false` | 580 项，0 失败，与基线一致 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-002 | BD-001, BD-002 | CK-004 |
| RQ-002 | T-001, T-002 | BD-002 | CK-005 |
| RQ-003 | T-001, T-002 | BD-002 | CK-001, CK-005 |
| RQ-004 | T-001, T-002 | BD-002 | CK-001 |
| RQ-005 | T-001, T-002 | BD-002 | CK-006 |
| RQ-006 | T-001 | BD-002, BD-004 | CK-002 |
| RQ-007 | T-001 | BD-002 | CK-002 |
| RQ-008 | T-002 | BD-005 | CK-007 |
| RQ-009 | T-001 | BD-004 | CK-003 |

## Risks and Rollback

- Risk: 去掉默认实现后，某个未被勘察发现的实现者悄然失效。缓解：`TreatWarningsAsErrors` 与接口成员的编译期强制会使任何未实现者立即编译失败，不存在静默通过的路径。
- Risk: 调整 `RecordingSender` 时把断言简化。缓解：CK-003 以逐行比对为准，且新 `SendAsync` 抛出而非静默记录。
- Rollback: 本尝试未接受前，回滚边界是回到 `3c82821`；改动仅涉及四个已跟踪文件，可整体还原。

## Assumptions

- 生产实现者只有 `TouchSocketNetworkMessageSender`，且已显式实现 `SendProjectionAsync`（已勘察确认）。
- 基线中 `Dfo.Server.Host.Tests` 有一次未复现的瞬时失败。同类瞬时失败不计为回归，但须如实记录。
