# Routing Decision

- TaskContract: `.rks-agent/tasks/protocol-vertical-slices/attempts/spec-006-protocol-handler-slices/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 成果边界由根控制器在接受 spec-005 后于 `safe-successor` 时定死——DTO、消息 handler、命令与命令 handler 迁入 spec-003 已建立的切片，加 AGENTS.md 更新、handler 业务语义审计、注释归位与一份留待用户复审的架构不和谐点文档。可观察结果确定：六类制品同目录、`Data` 只剩协议身份契约、协议接线集中在切片模块、diff 无逻辑变更、线级行为不变。
- Requirement stability: 十一条子成功条件全部可观察——切片形态可由目录列示证明，`Data` 残留可由文件内容逐项归属说明证明，零逻辑变更可由逐文件比对证明，分层由引用图证明，handler 边界由逐个审计结论证明，无回归对齐已实测的 580 项基线。
- Execution direction: 中心方向已由勘察确定——切片项目新增对 Application 的引用（Protocol → Application，方向无环）；spec-005 已消除唯一的钉死项，全部消息 record 经全仓检索确认在 Protocol 之外零引用，可整体搬走；切片目录已存在，落点无须再决策。
- Remaining uncertainty: 仅剩非语义的局部选择——`NetworkMessages.cs` 拆分后各切片内 DTO 文件的命名与粒度、`ChannelControlMessageHandlers` 一文件三 handler 的拆分方式、迁移的分批次序。均不改变成果语义。

## Marginal Control Value

- User precision signal: 用户要求整个根任务走 SDD，对最终结构形态有具体预期（「一个协议文件夹能同时看到解包逻辑，包处理分发逻辑和命令处理逻辑」），并对 handler 边界给出过明确理由（「handler 做了业务修改但是领域服务层根本不知道」是隐性 bug 滋生点）。偏好精确性高于速度。
- Direct failure mode: 本成果是根任务的收尾，同时承担一次大规模搬迁与一次实质性审计。Direct 下最可能的逃逸失败有三类：（一）**编译不过时以放宽引用图作为最省事的修复**——切片项目新增对 Application 的引用是本成果唯一应有的引用图变更；一旦某处出现意料之外的编译错误，给 `Abstractions`、`Data` 或 `Networking` 补一条对 Protocol 的引用即可当场通过，而那会瓦解前三个成果建立的全部编译期保证且编译无警告；（二）**handler 审计退化为走过场**——SC-11 要求的是逐个 handler 的实质判定，而 14 个 handler 中多数是薄转发，容易在扫过前几个之后对其余给出未经检查的"符合"结论，而真正违反者恰恰最可能藏在最长的那个（`SelectCharacterHandler`）里；（三）**搬迁顺手改写**——约 60 个文件跨项目移动，任何一处对可空标注、表达式体或常量的"顺带整理"，若触及消息字段或判定，就在一次声称零变更的 diff 里混入了行为改动。
- Useful control: Spec 把上述三条固化为可验证条件（CSC-4 把引用图作为独立验收点并显式覆盖 Abstractions 与 Data 两侧，CSC-7 要求逐个 handler 给出其所调用的命令或服务而非整体结论，CSC-5 要求逐文件内容比对且注释归位单独列示，CSC-11 要求架构文档每一条可由代码核对）；完整结果复核对照 580 项基线与既有断言。
- Time-for-quality rationale: 本成果不产生任何新功能，其价值在结构与一条被记入文档的边界规则。一次逃逸的分层放宽会使前三个成果建立的编译期保证全部失效，且因为编译通过而不会被任何测试发现；一次走过场的 handler 审计会把用户明确要求防范的隐性缺陷源留在原地并盖上"已审计"的印。两者的事后代价都远高于一次结果复核。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: DTO 迁移与 handler 迁移不可分——单独迁 DTO 会使仍留在 Application 的 handler 反向依赖 Protocol，与 Protocol 对 Application 的依赖构成循环，中间态根本不编译。AGENTS.md 更新与 handler 审计依赖最终形态确定后才能如实记录，构成同一成果的收尾而非独立验收面。
- Why Direct is insufficient: Direct 无法为"引用图未放宽"与"逐个 handler 已实质审计"提供可归因的验收点，而这两条正是本成果的主要风险面。
- Why Multi is unnecessary: 根控制器已把入站解包、出站编码与编解码切片隔离为三个独立成果；本成果是余下的最后一块，再拆会产生不可编译的中间接受态。
- Reroute conditions: 若审计发现某个 handler 确实直接实施了业务语义或状态迁移，其纠正属行为改动而非迁移，须记录为待处理并在结果与架构文档中明示，不在本成果内改写；若发现仍有消息 DTO 被 Abstractions 或 Networking 直接引用，`Data` 的残留边界需重新决策而非放宽引用图。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: Standard 的显式 Plan 解决三个相互耦合且会向后传播的决策——`NetworkMessages.cs` 的拆分边界（成分类型跟谁、身份契约留下什么）、`ApplicationModule` 与切片模块之间注册职责的切分线（协议注册走、命令总线与领域服务留）、以及搬迁的分批次序（须保证每批之后仍可编译，否则零变更主张无法分段核验）。三者共同决定最终形态，而形态一旦确立就是根任务的交付结果。其余控制沿用完整结果复核与新鲜验证。
- Why lighter is insufficient: Lite 不产出 `plan.md`，上述三个决策会在搬迁过程中被就地做出；尤其 `Data` 的残留边界若逐类型即兴判定，最可能的结果就是遇到编译错误时靠加引用绕过，而那是本成果最严重的逃逸失败。
- Why heavier is unnecessary: Governed 的独立挑战与漂移控制针对高后果逃逸失败。本成果无任何硬性下限触发项：不触及安全、凭据、授权、隐私、财务、法务、不可逆外部变更或持久数据丢失路径；影响限于任务工作树内的服务端内部结构，回滚边界明确（单一未接受尝试，回到 `7545921`）。分层与审计风险由 CSC-4/CSC-7 的逐项交代与 580 项既有断言覆盖，普通复核加新鲜验证已足够。
