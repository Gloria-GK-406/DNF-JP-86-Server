---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Projection Delivery Reclassification

## Delivered

- `SelectCharacterProjectionDelivery` 与 `ExplicitProjectionTarget` 自 `Dfo.Server.Data.Networking` 迁入 `Dfo.Server.Abstractions.Networking`，与消费它们的 `SendProjectionAsync` 同处一文件。
- 投递计划不再实现 `IServerNetworkMessage`，也不再报告 `ServerMessageType`。**它本来就不是消息**：它不含线上字段（载荷是领域投影），且携带自身的复数收件人，而消息的收件人由 sender 的调用方给出。
- `INetworkMessageSender.SendProjectionAsync` 由带回退实现的成员改为普通接口成员。**把投递计划当消息发送从运行期失败变成编译期不成立**——此前它会走到出站编码器注册表并以"has no registered encoder"失败。
- **`ServerMessageType.GameSelectCharacter` 恢复一值一义。** 此前 `SelectCharacterResponse` 与投递计划共用该值，正是这一冲突在 spec-002 中迫使出站注册表必须按 CLR 类型建键。该冲突消除后，按 CLR 类型建键的结论仍成立（出站消息由服务端构造、调用点静态已知类型），只是不再需要"目录值不可用"这条理由，`ServerMessageEncoding.cs` 的说明相应缩短。
- 7 处测试替身自行实现该接缝：6 处为纯新增（5 个抛出、1 个空实现），1 处 5 条断言逐字保留且净增强。

## Success Evidence

| Condition | Evidence |
|---|---|
| CSC-1 | `verification/completion-evidence.md`：定义处检索，两者均在 `INetworkServices.cs` |
| CSC-2 | 同上：类型声明无基类列表、无 `MessageType` |
| CSC-3 | 同上：`SendAsync` 参数为 `IServerNetworkMessage` 而投递计划不再实现它；接缝无方法体 |
| CSC-4 | 同上：`ServerMessageType.GameSelectCharacter` 全仓恰一处命中 |
| CSC-5 | 同上：复核者取 Networking 侧 diff 为空，四个投递方法体整文件零差异；五项语义逐项定位 |
| CSC-6 | 同上：三项投影投递测试与投影契约测试通过，断言未改 |
| CSC-7 | 同上：零 csproj 改动；Abstractions 仍只引用 Data，未引入新方向 |
| CSC-8 | 同上：580 项 515 通过 65 跳过 0 失败，逐套件对齐基线 |

### 被修改的既有测试

7 个文件，均因接缝由带回退实现改为普通成员而必须自行实现。6 处为纯新增方法、零删除行、既有断言未触碰。第 7 处 `SelectCharacterProjectionContractTests.RecordingSender` 的 `SendAsync` 由"记录并 `Assert.IsType`"改为抛出，并新增直接实现——测试方法体 5 条断言逐字保留，且"投递不经消息路径"由未被断言升级为被断言。无测试被删除或 `Skip`。

## Repository Result

- Changed paths：改 `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`、`Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs`、`Server/src/Dfo.Server.Abstractions/Networking/ServerMessageEncoding.cs`、`Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs`（仅一段 `<remarks>`）、以及 7 个测试文件；加本尝试与已封存的 spec-004 的流程制品。
- Baseline or accepted result：基于 `3c82821`，由父控制器接受为一次已验证的本地结果提交。
- Parent rollback required：no

## Successor Guidance

- **切片归位的前置已完全满足，且不再有任何钉死例外。** `Dfo.Server.Data/Networking/NetworkMessages.cs` 现在只剩两个枚举、两个消息接口、两个 `*MessageMetadata` 与纯粹的消息 record。后继成果可把**全部**消息 record 迁入切片，`Data` 只留协议身份契约——无需记住"这两个为什么留下"。
- **spec-004 的 spec.md 可作为后继的直接输入，需两处修正。** 其 RQ-002 与"钉死类型"定义须改为"`Data` 中仅剩协议身份契约"；其余 Scope、约束、handler 审计要求、AGENTS.md 更新项与 `NetworkPlugins.cs` 注释归位均未被触及，仍然有效。
- **注释归位是一个反复出现的类别，后继须主动排查而非被动等编译器报错。** 本成果修了 `ServerMessageEncoding.cs`，复核又发现漏掉的 `TouchSocketNetworkMessageSender`。跨程序集搬迁后，凡描述"另一侧如何工作"的注释都可能失真，而其中只有 `cref` 会被编译器发现，散文不会。后继切片归位涉及约 60 个文件，须专门过一遍。
- **用户要求一份留待其复审的架构文档。** 需在 `Docs/` 下记录本次重构中观察到的不和谐点，随后继成果一并交付。已知应收录者至少包括：`ServerMessageType` 作为纯目录无机械用途（写错不被发现）、`Dfo.Server.Utilities` 为空占位项目、decoder 声明产出与实际产出无一致性检查、`SelectCharacterStaticBodyFamilies` 的巨型静态字节表、以及本成果暴露的"注释描述另一程序集事实"这一类别。
- **`Dfo.Server.Utilities` 至今未被本任务触及**，仍是空占位项目，宜在架构文档中记录而非顺手处置。

## Artifacts

- `spec.md`（RQ-001..RQ-009 / AC-001..AC-010 / EV-001..EV-006）
- `plan.md`（BD-001..BD-005 / T-001..T-002 / CK-001..CK-008）
- `reviews/change-review.md`（独立子代理复核，approved-with-findings，0 阻断；F-1 已修复，其余记入验证证据的已知偏差；复核者另就归类修正本身给出认同意见 F-3）
- `verification/completion-evidence.md`（decision: pass）
