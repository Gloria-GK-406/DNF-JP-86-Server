---
kind: rks-spec-result
outcome: failed
terminal_stage: plan
---

# Spec Result: Protocol Handler Slices（未执行，边界被证伪）

## 失效的前提

本 Spec 的验收边界建立在一条被证伪的前提上：`SelectCharacterProjectionDelivery` 与 `ExplicitProjectionTarget` 是"钉死类型"——因 `Abstractions/Networking/INetworkServices.cs` 的 `SendProjectionAsync` 直接以其为参数，故必须永久留在 `Dfo.Server.Data`。TaskContract 的 Current Situation 第 1 条、Assumptions 第 1 条与 CSC-2 均以此为基础。

在 plan 阶段前的确认性勘察中，该前提被推翻。全仓检索确定：**唯一要求该类型实现 `IServerNetworkMessage` 的地方，是 `INetworkServices.cs:24` 那条接口默认实现** `SendProjectionAsync(...) => SendAsync(..., delivery, ...)`。而 spec-002 已使 `TouchSocketNetworkMessageSender` 显式实现 `SendProjectionAsync`，该默认实现在生产路径上已成死代码；出站编码器注册表的 9 条注册中亦无该类型，真经 `SendAsync` 发送它会以"has no registered encoder"失败。

进一步检视其字段，该类型不含任何线上字段：它承载 `Data.Game` 的领域投影列表与 `NetworkConnectionId`（收件人）。其余任何消息 DTO 都不携带自己的收件人。它表达的是一份扇出投递计划，而非一条消息。它与 `SelectCharacterResponse` 共用 `ServerMessageType.GameSelectCharacter` 这一既有异常（spec-002 中迫使出站注册表按 CLR 类型建键的那个冲突），正是这一归类错误的征兆。

因此它不是"必须留在 Data 的钉死类型"，而是**被错误归类为消息的投递指令**，其正确归属是 `Dfo.Server.Abstractions.Networking`，紧邻 `INetworkMessageSender`。

## 为何是 failed 而非本 Spec 内的返工

该发现直接推翻本 Spec 的 CSC-2（"留在 `Dfo.Server.Data` 的网络类型仅限协议身份契约与被接缝钉死的投递 DTO"）——修正后将不存在"被钉死的投递 DTO"这一类别。同时它引入一项本 Spec 明确划在 Scope/Out 之外的工作：移除一个接口默认实现并去掉一个类型的 `IServerNetworkMessage` 实现，属接缝的语义变更而非迁移。

把该变更并入本 Spec 会摧毁其可复核性：本 Spec 的核心约束是"迁移部分不得夹带逻辑变更"，而这正是让一次约 60 个文件的搬迁 diff 可被逐文件核验的唯一凭据。故须先以独立成果完成归类修正，再以无例外的纯搬迁完成切片归位。

用户在收到该分析后的决定：「你先按你的想法完成整个任务。然后我再做一次复审处理。」——授权按此重排继续，不再逐步确认。

## Repository Result

- Changed paths：无产品或测试文件被修改。本尝试仅产出流程制品（`TaskContract.md`、`routing-decision.md`、`sdd-execution.yaml`、`spec.md` 与本结果），全部位于本尝试目录内。
- Baseline：`3c82821`，未变。
- Parent rollback required：yes（仅需证明无产品变更；本尝试目录作为持久证据保留）

## Successor Guidance

- **先做归类修正，再做切片归位。** 后继成果一：把 `SelectCharacterProjectionDelivery` 与 `ExplicitProjectionTarget` 迁入 `Abstractions/Networking/`，去掉 `: IServerNetworkMessage` 与 `MessageType` 成员，并把 `SendProjectionAsync` 由带默认实现的成员改为普通接口成员。后继成果二：消息 DTO、消息 handler、命令与命令 handler 的切片归位，届时无任何钉死例外。
- **载荷类型不动。** `ISelectCharacterSupplementalProjection` 在 `Data.Game`，`ISelectCharacterProjectionComposer` 早已直接引用它，Abstractions → Data.Game 是既成且正确的方向。它是领域投影而非线上类型，不进切片。
- **有一处测试假实现骑在待删的默认实现上。** `Dfo.Server.Application.Tests/Networking/SelectCharacterProjectionContractTests.cs` 的假 sender 不覆写 `SendProjectionAsync`，靠默认实现落到 `SendAsync` 后 `Assert.IsType<SelectCharacterProjectionDelivery>(message)`。删除默认实现后它须直接实现 `SendProjectionAsync`；断言因此更直接，不构成削弱。另有 `SelectCharacterLifecycleTests` 与 `SessionGenerationOwnershipTests` 的假实现已显式实现该方法，不受影响。
- **`ServerMessageEncoding.cs` 的 `<remarks>` 需同步。** 其第 12 行以"两者共用同一目录值"作为按 CLR 类型建键的理由之一；归类修正后该冲突消失。按 CLR 类型建键的结论仍然成立（出站消息由服务端构造、调用点静态已知类型），只是理由变短。
- **本 Spec 的其余内容仍然有效，可作为后继成果二的直接输入。** 其 Scope、约束、handler 审计要求、AGENTS.md 更新项与 `NetworkPlugins.cs` 注释归位均未被此次证伪触及；只需删去"钉死类型"相关表述并把 CSC-2 改为"`Data` 中仅剩协议身份契约"。
- **用户另要求一份留待其复审的文档。** 需在 `Docs/` 下记录本次重构中观察到的架构不和谐点。该文档应在最终形态确定后写，纳入后继成果二。

## Artifacts

- `TaskContract.md`（revision 1，ready；其 Current Situation 第 1 条与 Assumptions 第 1 条为被证伪的前提）
- `routing-decision.md`
- `spec.md`（RQ-001..RQ-011 / AC-001..AC-012 / EV-001..EV-008；其 RQ-002 与"钉死类型"定义需在后继中修正）
