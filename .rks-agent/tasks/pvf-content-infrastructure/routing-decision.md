# Routing Decision

- TaskContract: `.rks-agent/tasks/pvf-content-infrastructure/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 分层在多轮对话中已由用户逐步确定并收敛——格式库作为与 `Dfo.Server.Application` 平级的新 project、基础设施实现领域抽象、Application 只依赖抽象；用户并已明确否决在格式库内做规则判定，也已决定 v1 不引入投影缓存。可观察结果是重构分支具备一条只读内容读取链路。
- Requirement stability: 可枚举且可复核的显式要求已存在：格式正确性对照主干实现、内存与分配基准（旧库实测 276.4 MB 常驻、489.8 KB/次取字段）、跨边界类型的中立性、G5 `spec-006` RQ-001 所列的内容类别。
- Execution direction: 中心方向已知——只读格式库直读脚本记录、基础设施收敛格式归一化、抽象层以领域语言表达；重构分支已有 `IChannelScriptSource` ← `FileChannelScriptSource` 的同形先例可循。
- Remaining uncertainty: 仅剩非语义的局部选择（目录细分、类型命名、缓冲策略的具体实现），不改变任务含义。

## Marginal Control Value

- User precision signal: 用户连续多轮要求以实测数据支撑设计判断，主动否决过度设计（撤掉投影 LRU），并纠正过我的分层错误；对精确度的偏好明显强于对速度的偏好。
- Direct failure mode: 两个具体失败模式。其一，内容来源漏掉 G5 所需的某一类定义（任务/日常/收集箱/宠物），导致 G5 后继尝试二次阻塞——这正是 `spec-006` 已经发生过一次的失败。其二，跨基础设施边界的类型泄漏 PVF 词法（如以字符串数组暴露 `[impossible contents]`），使归一化在消费方各自重实现——主干已实证该模式产生了四份实现三种归一化，其中一处漏判。
- Useful control: Spec 对内容类别覆盖面与边界类型中立性作出可复核的显式要求；完整结果评审对照契约检查是否有格式概念越界。
- Time-for-quality rationale: G5 已因内容不完整阻塞过一次并回滚了全部产品改动；边界类型一旦定型将被后续所有内容域沿用，事后收敛成本远高于此刻规格化的时间。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Boundary evidence: 存在两个可独立接受的结果。格式库的验收是格式正确性与资源基准（SC-1~SC-4），不需要任何内容来源存在即可完成与评审；内容来源的验收是 G5 类别覆盖与边界中立性（SC-5、SC-6），其评审视角是领域与格式的交界。二者的回滚边界也分离：内容投影若判定有误应可独立回滚而保留已验收的格式库，反之格式库的性能返工不应牵动内容契约。格式库另有独立于本任务的消费方（未来的物品目录、命令行工具），其所有权不归属 select-character 内容域。
- Why Direct is insufficient: Direct 无法在实现前固定"内容类别覆盖面"与"边界类型中立性"这两条验收，而它们恰是已发生过的与已实证的失败模式所在。
- Reroute conditions: 若在建立格式库过程中发现内容来源无法与格式库分离验收（例如 G5 类别的读取必须反向决定格式库的公开接口形态），则合并为单一 Spec 重新路由；若 `Script.pvf` 被证明不足以覆盖 G5 所需类别，则本任务的 SC-6 失去可满足性，须停止并回到意图对齐。
