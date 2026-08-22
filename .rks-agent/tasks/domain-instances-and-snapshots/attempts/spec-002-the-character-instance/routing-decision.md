# Routing Decision

- TaskContract: `.rks-agent/tasks/domain-instances-and-snapshots/attempts/spec-002-the-character-instance/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 目标形状由 `Docs/Refactor/DomainInstanceDesign.md` 裁定，本成果实施它。
  要动的两处都已定位到文件与行：可用性判定（`ItemUsabilityDomainService.cs:28`，身份解析在 `:71`）、
  以及实例的诞生与清除处（与背包同处，`SelectCharacterEntryInventoryDomainService.cs:61` 一带）。
- Requirement stability: 七条成功条件全部可当场核对——某个类型是否存在、某个处理器还持有哪些依赖、
  某组签名里有没有身份、六种判定是否逐条可构造、构建与全量是否绿。
- Execution direction: 已知。先给判定次序立守卫，再造实例、再迁调用方、最后把规则那半提纯。
- Remaining uncertainty: 实例的确切成员与它从哪里取得所在地与冷却。不改变成果语义。

## Marginal Control Value

- User precision signal: 用户要求跑完再汇报，中途没有人工检查点。
- Direct failure mode: 具名失效仍是**搬运时丢掉一条只活在代码里的规则**。本成果有两条这样的规则：
  一是**判定的先后次序**——永久性的原因先问、冷却最后问，理由只写在注释里，六条用例各自只断言
  单一条件成立时的结果，把冷却提到等级前面**不会有任何东西变红**；二是「所在地读不到时按村庄算」
  这条刻意的宽松默认，同样只由注释承载。
  上一个成果的实测又添了第三种：**换通道会改变可见时机**，而逐行 diff 看不出来——本成果把读的来源
  从服务内部换到实例内部，属于同一类。
- Useful control: 一份 durable 的次序清单（Plan），把每一条只活在注释里的规则列成必须先立守卫的步骤；
  以及对完整改动集的独立评审。前一个成果的实测是：一条 A 级由评审捕获、零条由测试捕获，
  且评审在第一轮就发现了一条全仓无人构造的拒绝。
- Time-for-quality rationale: 两条只写在注释里的规则加一次读取来源的搬迁，都不会当场变红。
  相对实施工作量，一份计划加两轮评审的时间占比可接受。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个连贯的验收边界——人物成为可操作的实例，并因此让最后一个不纯的规则服务变纯。
  两者不可分：把规则那半提纯需要有个东西来供身份，而实例的第一个真实用途正是它。
- Why Direct is insufficient: Direct 拿不到对完整改动集的独立评审，而本成果最可能的失效方式不会变红。
- Why Multi is unnecessary: 实例与提纯没有各自独立的回滚价值；拆开会留下一个只有一个方法、
  且那个方法只为证明实例存在而生的中间态。
- Reroute conditions: 若裁定「人物实例即运行时会话态」为真且实施它会改变行为，
  则把该裁定移出本成果、单独立项，并在设计文档中记录理由。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: 需要的是一份 durable 的次序与「只活在注释里的规则」清单（Plan），
  加对完整改动集的独立评审（reviewing-code），加新鲜验证。
- Why lighter is insufficient: `sdd-lite` 无 Plan。本成果的核心风险是判定次序与两条注释级规则，
  没有 durable 清单时，实施中把它们弄丢不会有任何东西变红——这一点在上一个成果里已经实测发生过一次
  （半栈交换的拒绝全仓无人构造，故意删掉全绿）。
- Why heavier is unnecessary: `sdd-governed` 的对抗性挑战与逐工件批准适用于高后果且需审计强度证据的场合。
  本成果每一条保证都能用一次真改真跑证伪，独立评审已足够。
