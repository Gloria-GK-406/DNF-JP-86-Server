# Routing Decision

- TaskContract: `.rks-agent/tasks/protocol-vertical-slices/attempts/spec-002-di-outbound-message-encoding/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 成果边界由根控制器在 `safe-successor` 时定死——出站编码由 DI 注册表驱动，目录重排明确划出。可观察结果确定：sender 的九分支 switch 与投递特判消失、九条出站消息字节逐字不变、投递次序与失败隔离语义不变。
- Requirement stability: 十条子成功条件全部可观察——分支消除可由检索与审阅证明，字节不变可由既有出站字节测试与四组线级集成测试证明（断言不得改），投递语义由既有两组投影测试证明，端点校验与未注册失败各有针对性测试，分层由引用图证明，无回归对齐已实测的 572 项基线。
- Execution direction: 中心方向已由勘察确定——按消息 CLR 类型建键（`ServerMessageType` 存在共用值，按枚举建键不可行；且该枚举在 `NetworkMessages.cs` 外零引用，改键不移除既有职责），端点由编码器声明，投递路径由显式实现 `SendProjectionAsync` 承接（全部生产调用点已走该方法）。
- Remaining uncertainty: 仅剩非语义的局部选择——编码器接缝的返回形状（单帧与多帧的统一）、`GameLoginPayloadCodec` 拆分后两部分的命名与落点、注册扩展方法的签名。均不改变成果语义。

## Marginal Control Value

- User precision signal: 用户要求整个根任务走 SDD，并在同类重构中主动提出「这重构还是有点危险的」。偏好精确性高于速度。
- Direct failure mode: 本成果改动的是所有出站字节的必经之路，且触及 `SelectCharacter` 的多帧投递。Direct 下最可能的逃逸失败有三类：（一）**端点校验在迁移中被削弱**——现有校验写在九个 `when` 子句里，抽成统一判定时极易把"消息不支持该端点"从抛出改成放行，而放行的结果是 Channel 载荷发往 Game 连接，客户端侧表现为难以归因的解码错乱；（二）**投递次序或失败隔离被改变**——`DeliverProjectionAsync` 中 best-effort 目标失败时 `break` 当前目标而继续后继目标，这是极易在重构中被"改善"成 `continue` 或吞掉的语义，而其后果只在多人同场景下才显现；（三）**多帧与单帧的统一改变了帧数或顺序**——八条消息产出单帧、`SelectCharacterResponse` 产出多帧，统一返回形状时若误把单帧包装层次弄错，会多发或少发帧。三者都能通过编译与大部分测试。
- Useful control: Spec 把上述三条固化为可验证条件（CSC-2/CSC-3 要求既有断言不得改，CSC-4 覆盖跨端点两个方向），使"字节与次序不变"成为独立验收面而非整体印象；完整结果复核对照 572 项基线与既有投影测试断言。
- Time-for-quality rationale: 本成果不产生任何新功能，其全部价值在于结构，全部风险在于顺带改坏出站字节或投递次序。一次逃逸的投递次序变更只在真实客户端的多人场景下显现，定位成本远高于一次结果复核。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 编码器注册机制、九条消息的接入、sender 分支消除、投递路径承接构成一个原子的用户可见成果——只接受其中一部分会留下"注册表已建但 sender 仍分支"或"分支已删但投递无人承接"的中间态，后者直接导致 `SelectCharacter` 无法投递，不是可运行的结果。
- Why Direct is insufficient: Direct 无法为"字节与投递次序逐字不变"提供可归因的验收点，而这正是本成果的主要风险面。
- Why Multi is unnecessary: 根控制器已把入站解包与目录重排隔离为独立成果；再拆本成果会产生上述不可运行的中间接受态。
- Reroute conditions: 若拆分 `GameLoginPayloadCodec` 时发现其入站与出站共享不可拆的私有状态，则该文件无法在本成果内解耦，须记录并上报，因其决定第三个成果能否搬迁该协议；若发现某条出站编码依赖 sender 的连接级状态而非仅依赖消息与配置，则编码器接缝形状需重新决策，回到 `modeling-task-intent`。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: Standard 的显式 Plan 解决三个相互耦合且会向后传播的决策——编码器接缝的返回形状（单帧/多帧统一）、端点归属的表达方式（由编码器声明还是由注册声明）、以及 `GameLoginPayloadCodec` 的拆分边界。三者共同决定注册 API 的形状，而注册形状一旦被九条消息接入就难以回退。其余控制沿用完整结果复核与新鲜验证。
- Why lighter is insufficient: Lite 不产出 `plan.md`，上述三个决策会在实现过程中被就地做出；尤其端点归属若在迁移中被就地简化，其错误不会在编译期显形，而九条消息接入之后再改要重做全部注册。
- Why heavier is unnecessary: Governed 的独立挑战与漂移控制针对高后果逃逸失败。本成果无任何硬性下限触发项：不触及安全、凭据、授权、隐私、财务、法务、不可逆外部变更或持久数据丢失路径；影响限于任务工作树内的服务端内部结构，回滚边界明确（单一未接受尝试，回到 `cb59bb1`）。字节与投递风险由 CSC-2/CSC-3 的既有测试断言覆盖，普通复核加新鲜验证已足够。
