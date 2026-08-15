# Routing Decision

- TaskContract: `.rks-agent/tasks/protocol-vertical-slices/attempts/spec-003-protocol-codec-slices/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 成果边界由根控制器在 `safe-successor` 时定死——协议编解码迁入新建 `Dfo.Server.Protocol` 的按协议切片，DTO 与 handler 的迁移明确划出（理由是循环引用，已勘察确证）。可观察结果确定：切片目录按协议成立、Networking 只剩传输、两张路由表随之迁移、diff 不含逻辑变更。
- Requirement stability: 九条子成功条件全部可观察——切片形态可由目录列示证明，Networking 只剩传输可由文件列示证明，零逻辑变更可由逐文件内容比对证明，分层由引用图证明，线级行为由既有集成测试断言未改证明，无回归对齐已实测的 580 项基线。
- Execution direction: 中心方向已由勘察确定——传输原语（`ChannelFrameEncoder`、`GameServerFrameEncoder`、`DnfFrameProtocol`、`ChannelPayloadCipher`、`GameLoginOptions`）已全部是 public，故依赖方向为 Protocol → Networking 且无需 `InternalsVisibleTo`；被迁移编解码的 using 集合已逐个检视，只依赖 `Abstractions`、`Data` 与上述传输原语。
- Remaining uncertainty: 仅剩非语义的局部选择——切片目录的命名与粒度（按协议名还是按端点分组）、共用件（`ChannelEncryptedFrame`、`SelectCharacter` 静态体系）的落点、模块类的命名与测试项目的目录组织。均不改变成果语义。

## Marginal Control Value

- User precision signal: 用户要求整个根任务走 SDD，并明确「一个协议文件夹能同时看到解包逻辑，包处理分发逻辑和命令处理逻辑」——对最终结构形态有具体预期。根 TaskContract 另以 SC-9 把"零逻辑变更"提为独立成功条件。偏好精确性高于速度。
- Direct failure mode: 本成果是本任务中唯一以"什么都不改变"为主张的一步，涉及约 35 个文件的跨项目搬迁。Direct 下最可能的逃逸失败有三类：（一）**搬迁顺手改写**——迁移时 IDE 或人工对 `internal`/`static`、可空标注、表达式体、常量内联做"顺带整理"，其中任何一处若触及字节布局或越界判定，就在一次声称零变更的 diff 里混入了行为改动，而复核者面对 35 个文件的位置变动极易漏过；（二）**引用图被就地放宽**——迁移中若某处编译不过，最省事的修复是给 `Dfo.Server.Networking` 加一条对切片项目的引用，这会当场瓦解根任务的分层前提且编译无警告；（三）**共用件被复制而非移动**——`ChannelEncryptedFrame` 被三个 Channel 编码器共用、`SelectCharacter` 静态体系被组帧与投影共用，若为迁就切片划分而复制一份，日后两份会分叉，且这类重复在通过测试的前提下不会自己暴露。
- Useful control: Spec 把"零逻辑变更"固化为可验证条件（CSC-6 要求逐文件内容比对而非整体印象，CSC-8 要求可见性改动逐项列示并说明必要性），使搬迁顺手改写从"复核者的注意力问题"变成"必须逐项交代的清单"；CSC-5 把引用图作为独立验收点；完整结果复核对照 580 项基线且要求断言不得改动。
- Time-for-quality rationale: 本成果不产生任何新功能，其全部价值在于结构，全部风险在于顺带改坏字节或瓦解分层。一次混入 35 个搬迁文件中的字节改动，事后归因要在一个"纯搬迁"的提交里逐文件重查，成本远高于一次以内容比对为方法的结果复核。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 项目新建、编解码搬迁、路由表迁移、测试迁移构成一个原子的用户可见成果——只接受其中一部分会留下"项目已建但编解码仍在 Networking"或"编解码已迁但路由表仍在 NetworkingModule 而其引用的类型已不可见"的中间态，后者根本不编译，不是可运行的结果。
- Why Direct is insufficient: Direct 无法为"diff 中除位置、命名空间、using、可见性外无任何改动"提供可归因的验收点，而这正是本成果的主要风险面，也是根 SC-9 单列该条的原因。
- Why Multi is unnecessary: 根控制器已把入站解包、出站编码与 DTO/handler 迁移隔离为独立成果；再拆本成果会产生上述不可编译的中间接受态。
- Reroute conditions: 若实施中发现某个被迁移的编解码依赖 `Dfo.Server.Application` 的类型（勘察未见，但 `SelectCharacter` 的投影链路最长），则切片项目的引用图前提失效，须停在该处上报而非放宽引用；若发现 `ChannelPayloadCipher` 实际依赖具体消息而非仅依赖端点，则传输/协议的划分线需重新决策，回到 `modeling-task-intent`。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: Standard 的显式 Plan 解决三个相互耦合且会向后传播的决策——切片目录的粒度与命名（14 个入站协议与 9 条出站消息并非一一对应，`SelectCharacter` 一个协议牵动七个文件而四个 Channel 控制消息各只有两三个）、共用件的落点、以及搬迁的分批次序（须保证每批之后仍可编译，否则零变更主张无法分段核验）。三者共同决定最终目录形态，而目录一旦建立并被后继成果的 handler 迁移接入就难以回退。其余控制沿用完整结果复核与新鲜验证。
- Why lighter is insufficient: Lite 不产出 `plan.md`，上述三个决策会在搬迁过程中被就地做出；尤其目录粒度若在搬迁中逐文件即兴决定，结果是切片划分不自洽，而这正是用户对最终形态有具体预期的地方。
- Why heavier is unnecessary: Governed 的独立挑战与漂移控制针对高后果逃逸失败。本成果无任何硬性下限触发项：不触及安全、凭据、授权、隐私、财务、法务、不可逆外部变更或持久数据丢失路径；影响限于任务工作树内的服务端内部结构，回滚边界明确（单一未接受尝试，回到 `feef1a5`）。零变更风险由 CSC-6/CSC-8 的逐项交代与 580 项既有断言覆盖，普通复核加新鲜验证已足够。
