# Routing Decision

- TaskContract: `.rks-agent/tasks/protocol-vertical-slices/attempts/spec-001-di-inbound-message-decoding/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 成果边界由根控制器在 `safe-successor` 时定死——入站解包由 DI 注册表驱动，出站编码与目录重排明确划出。可观察结果确定：两处 `CreateMessage` 分支消失、14 个协议解码逐字段不变、启动检查换向。
- Requirement stability: 八条子成功条件全部可观察——分支消除可由检索与审阅证明，解码不变可由既有 codec 向量测试与四组线级集成测试证明（断言不得改），静默丢弃可由三种情形的针对性测试证明，启动检查两个方向各有独立测试，分层由引用图证明，无回归对齐已实测的 563 项基线。
- Execution direction: 中心方向已由现状确定——按线上帧键索引（`GET_USERINFO` 一型两义已证实键空间必须分离），注册形态参照同仓既有的 `MessageHandlerRegistry`（`IEnumerable<Registration>` 注入、构造期查重、冻结字典），而非 `CommandHandlerRegistry` 的 CLR 类型键。九个 Game codec 已具备一致形状，归一工作集中于 `GameLoginPayloadCodec` 一个。
- Remaining uncertainty: 仅剩非语义的局部选择——线上帧键的载体类型、注册扩展方法的签名与落点模块、解包器申报产出类型的表达方式。均不改变成果语义。

## Marginal Control Value

- User precision signal: 用户要求整个根任务走 SDD，并在同类重构中主动提出「这重构还是有点危险的」。偏好精确性高于速度。
- Direct failure mode: 本成果改动的是所有入站字节的必经之路。Direct 下最可能的逃逸失败有三类：（一）**归一 `GameLoginPayloadCodec` 时改变解码语义**——它是唯一形状不同的 codec，其 `TryDecode` 的失败条件（D-string 越界、长度超 256、loginId 为空）目前决定了"静默丢弃"而非"抛异常"，归一成 `Decode` 返回可空时极易把失败路径改成异常，从而使畸形登录帧由丢弃变为抛出；（二）**`GET_USERINFO` 的 mode 分流在键空间迁移中被压平**，使 mode `0x02` 以外的取值由产出 null 变为产出错误消息；（三）**静默丢弃语义被"改善"**——查表未命中时顺手加日志或抛异常，看似无害，实则改变了服务端对未知流量的可观察反应。三者都能通过编译与大部分测试。
- Useful control: Spec 把上述三条固化为可验证条件（CSC-2 要求断言不得改、CSC-3 覆盖三种丢弃情形），使"行为不变"成为独立验收面而非整体印象；完整结果复核对照 563 项基线与既有集成测试断言。
- Time-for-quality rationale: 本成果不产生任何新功能，其全部价值在于结构，全部风险在于顺带改坏入站字节处理。一次逃逸的丢弃语义变更需要在真实客户端上复现——畸形帧本就罕见——定位成本远高于一次结果复核。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 注册机制、14 个解包器的接入、plugin 分支消除、启动检查换向构成一个原子的用户可见成果——只接受其中一部分会留下"注册表已建但 plugin 仍分支"或"检查已删但新检查未立"的中间态，任一中间态都不是可运行的完整结果。
- Why Direct is insufficient: Direct 无法为"解码逐字段不变"提供可归因的验收点，而这正是本成果的主要风险面。
- Why Multi is unnecessary: 根控制器已把出站编码与目录重排隔离为独立成果；再拆本成果会产生上述不可运行的中间接受态。
- Reroute conditions: 若归一 `GameLoginPayloadCodec` 时发现其解码与 `EncodeSuccess` 共享不可拆的私有状态，则入站与出站无法分离，需回到根控制器重新划界；若发现除 GameLogin 外仍有 codec 依赖 `Networking.Configuration` 或传输类型（推翻子契约 Assumptions 第三条），须记录并上报，因其影响根任务第三个成果的切片落点。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `sdd-standard` 的代码重构 Profile（由该 Skeleton 选取其最接近的受支持 Profile）
- Why this is sufficient: Standard 的显式 Plan 解决一个会向后传播的耦合决策——**接缝归属**。CSC-6 要求的启动检查必须同时看到解包器注册与消息 handler 注册，而前者天然属 Networking（解包器现为其 internal 静态类），后者属 Application（`MessageHandlerRegistry`），且根约束禁止 Networking 引用 Application。可行落点至少有三种：注册声明下沉到 Abstractions 使两侧共见、检查上移到 Host 组合根、或解包器此时即迁入 Application。三者决定了注册 API 的形状、解包器的可见性、以及第三个成果搬迁时的起点，选错需要推倒注册形态重来——而这发生在 14 个解包器已按该形状接入之后。Plan 把该决策连同「线上帧键的载体」「产出类型的申报方式」一并前置定死并排序，其余控制沿用 Lite 的完整结果复核与新鲜验证。
- Why lighter is insufficient: Lite 不产出 `plan.md`，上述接缝决策会在实现过程中被就地做出。它的错误不会在编译期或测试中显形——三种落点都能通过 563 项测试——只会在第三个成果搬迁时才暴露为返工，届时完整结果复核已经过去。
- Why heavier is unnecessary: Governed 的独立挑战、独立制品审批与漂移控制针对的是高后果逃逸失败。本成果无任何硬性下限触发项：不触及安全、凭据、授权、隐私、财务、法务、不可逆外部变更或持久数据丢失路径；其全部影响限于任务工作树内的服务端内部结构，回滚边界明确（单一未接受尝试，回到 06b24e7）。行为风险由 CSC-2/CSC-3 的既有测试断言与针对性测试覆盖，普通复核加新鲜验证已足够。
