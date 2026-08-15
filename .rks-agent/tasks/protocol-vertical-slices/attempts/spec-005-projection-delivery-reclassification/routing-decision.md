# Routing Decision

- TaskContract: `.rks-agent/tasks/protocol-vertical-slices/attempts/spec-005-projection-delivery-reclassification/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 成果边界由根控制器在 `safe-successor` 时定死——投递指令归位到抽象接缝一侧并去掉其消息身份，接缝由带默认实现改为普通成员。可观察结果确定：类型位置与定义、`SendAsync` 不再接受该类型、枚举成员不再被两个 CLR 类型共用、投递字节与次序不变。
- Requirement stability: 八条子成功条件全部可观察——归位与定义可由代码审阅证明，编译期不成立可由接口形态与类型定义共同证明，枚举独占可由检索实现者证明，投递语义与字节由既有投影测试断言未削弱证明，分层由引用图证明，无回归对齐已实测的 580 项基线。
- Execution direction: 中心方向已由勘察确定——两个记录迁入 `Dfo.Server.Abstractions.Networking`（该单元已直接引用 `NetworkConnectionId` 与 `ISelectCharacterSupplementalProjection`，不引入新引用方向）；去掉 `IServerNetworkMessage` 与 `MessageType`；`SendProjectionAsync` 去默认实现。全部生产实现者已显式实现该方法，故为纯收紧。
- Remaining uncertainty: 仅剩非语义的局部选择——两个记录落在 `INetworkServices.cs` 内还是新建文件、`ServerMessageEncoding.cs` 的 `<remarks>` 改写措辞。均不改变成果语义。

## Marginal Control Value

- User precision signal: 用户对本次重构全程要求 SDD，并在本议题上主动追问架构含义（「这是一个很清晰的味道」），随后明确表示中途讨论架构有压力、要求跑完后自己复审。这意味着精确性要求不变，但决策与证据必须落在制品里而非对话里。
- Direct failure mode: 本成果改的是所有投影投递的必经接缝。Direct 下最可能的逃逸失败有两类：（一）**去掉默认实现时顺手改动投递语义**——`DeliverProjectionAsync` 的 best-effort 目标失败时 `break` 当前目标而继续后继目标、以及与 required-direct 相同连接的目标被跳过，这两条极易在触碰该路径时被"改善"，而其后果只在多人同场景下显现；（二）**测试假实现的调整削弱断言**——`SelectCharacterProjectionContractTests` 的假 sender 当前靠默认实现落到 `SendAsync` 再断言类型，改为直接实现后若顺手把断言简化为"被调用过"，就丢掉了这条测试原本证明的东西，而全绿会掩盖它。
- Useful control: Spec 把上述两条固化为可验证条件（CSC-5/CSC-6 要求既有投影测试断言不得削弱，CSC-3 要求编译期不成立而非仅运行期失败）；完整结果复核对照 580 项基线并专查被调整的假实现。
- Time-for-quality rationale: 本成果改动面很小（两个记录、一个接口成员、一处假实现），但它位于全部投影投递的必经路径上，且其正确性无法由"编译通过 + 测试全绿"独立证明——被削弱的断言恰恰仍然全绿。一次结果复核的成本远低于一条逃逸的投递次序变更。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 类型归位、去消息身份、接缝去默认实现三者构成一个原子成果——只接受其中一部分会留下"类型已移但仍实现消息接口"或"默认实现已删但类型仍在 Data 且实现消息接口"的中间态，后者使 `Data` 保留一个无人可用的消息实现，不是可交付的结果。
- Why Direct is insufficient: Direct 无法为"投递语义与断言强度未被削弱"提供可归因的验收点，而这正是本成果的主要风险面。
- Why Multi is unnecessary: 该成果只有一个验收面，且规模小于此前任一成果；拆分只会产生上述不可交付的中间态。
- Reroute conditions: 若发现除已知三处外还有实现 `INetworkMessageSender` 而未实现 `SendProjectionAsync` 的生产类型，则"去默认实现属纯收紧"这一前提失效，须停下重新评估；若发现某处生产代码确实依赖把投递当消息经 `SendAsync` 发送，则归类结论本身需重议，回到 `modeling-task-intent`。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: Standard 的显式 Plan 解决两个会向后传播的决策——两个记录的落点（决定后继切片归位时 `Data/Networking` 的残留边界）与被调整假实现的断言形态（决定该测试此后证明什么）。两者都在本成果之后被后继成果直接继承。其余控制沿用完整结果复核与新鲜验证。
- Why lighter is insufficient: Lite 不产出 `plan.md`，假实现的断言形态会在实现中被就地决定，而这正是本成果最可能被悄悄削弱的地方。
- Why heavier is unnecessary: Governed 的独立挑战与漂移控制针对高后果逃逸失败。本成果无硬性下限触发项：不触及安全、凭据、授权、隐私、财务、法务、不可逆外部变更或持久数据丢失路径；影响限于任务工作树内的服务端内部接缝，回滚边界明确（单一未接受尝试，回到 `3c82821`）。投递语义风险由既有投影测试断言覆盖，普通复核加新鲜验证已足够。
