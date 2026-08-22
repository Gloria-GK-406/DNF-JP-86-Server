# Routing Decision

- TaskContract: `.rks-agent/tasks/domain-instances-and-snapshots/attempts/spec-001-item-rules-in-the-bag/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 目标形状已由 `Docs/Refactor/DomainInstanceDesign.md` 裁定，本成果只实施它。
  三处调用方、两处反向注释、一处重复落地都已逐条定位到文件与行。
- Requirement stability: 八条成功条件全部可当场核对——某个分支在全仓出现几次、某个工程是否还引用某类服务、
  某个拒绝分支是否仍可构造、构建与全量是否绿。
- Execution direction: 已知。先加动词、再迁调用方、最后收旧路；两处失败语义各自保留而不是统一。
- Remaining uncertainty: 只剩局部选择——三个动词的确切签名、到货落地那一段放在容器的哪一层
  （基类还是具体类）。都不改变成果语义。

## Marginal Control Value

- User precision signal: 用户要求跑完再汇报，中途没有人工检查点；同时表达过速度偏好，接受「少一点评审」。
- Direct failure mode: 具名失效是**搬运时丢掉一条只活在代码里的规则**。本成果正是高发场景：
  三处调用方的失败语义**互不相同**（每日补充抛、教程奖励静默跳过、移动返回拒绝码），
  而它们即将共用同一个背包动词。把三种反应统一成一种，是这次最可能发生且测试未必会红的事——
  教程奖励那条「装不下就把该件排除在回执之外」只在一个 `default: break` 分支里，没有注释以外的守卫。
  第二个具名失效是移动的判定被搬运时丢掉一条分支：`ItemMoveStatus` 有十个值，其中四个是判定结果。
- Useful control: 一份 durable 的计划，把「加动词 → 迁调用方 → 收旧路」的次序与每处失败语义的存续
  写成可核对的步骤；以及对完整改动集的独立评审。上一个同规格根任务的实测是三条 A 级全部由评审捕获、
  零条由测试捕获。
- Time-for-quality rationale: 三处调用方语义各异且其中两处的差异只写在注释里，错误次序或统一化不会当场
  变红。相对本成果的实施工作量，一份计划加一轮评审的时间占比可接受。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个连贯的验收边界——物品规则收回背包。三处调用方必须一起迁移，
  否则「到货落地只剩一处」不成立。
- Why Direct is insufficient: Direct 拿不到对完整改动集的独立评审，而本成果最可能的失效方式不会变红。
- Why Multi is unnecessary: 三处调用方没有各自独立的回滚价值；拆开反而使中间态出现两份到货落地。
- Reroute conditions: 若发现每日补充改走容器会改变下发帧（与已核实的代理键结论相反），
  则把每日补充从本成果移出、单独立项，并在设计文档中记录该反例。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: 需要的是一份 durable 的次序与逐处失败语义清单（Plan），加一轮对完整改动集的
  独立评审（reviewing-code），加新鲜验证。这三样正好各自对应上面两个具名失效方式。
- Why lighter is insufficient: `sdd-lite` 无 Plan。本成果的核心风险恰恰是**次序与逐处差异**——
  三处调用方的失败语义互不相同且其中两处只由注释承载，没有 durable 清单时，
  实施中把它们统一掉不会有任何东西变红。
- Why heavier is unnecessary: `sdd-governed` 的对抗性挑战与逐工件批准，适用于高后果且需要审计强度证据的
  场合。本成果的后果是一个尚未上线的开发期服务器的物品落地路径，且每一条保证都能用一次真改真跑证伪，
  独立评审已足够。
