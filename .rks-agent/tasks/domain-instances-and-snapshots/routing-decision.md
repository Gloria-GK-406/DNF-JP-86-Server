# Routing Decision

- TaskContract: `.rks-agent/tasks/domain-instances-and-snapshots/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 设计已经收敛并落成文档 `Docs/Refactor/DomainInstanceDesign.md`（274 行，
  六条规则、三类对象、一条规模前提），且该文档是在本轮与用户三次往复中逐条对齐的——包括一次由用户
  提出、经实测后被推翻的性能理由。目标是实施一份已裁定的设计，不是再决定它。
- Requirement stability: 十条成功条件全部可观察且可当场核对：某个 switch 在全仓出现几次、某个工程
  是否还引用某类服务、某组接口签名里是否出现 `CharacterId`、三份表征用例的期望值 `git diff` 是否为空。
- Execution direction: 已知。四个子成果的次序与各自的成果边界已与用户确认，第一块是纯增量、
  第四块的保绿手法（只改产生期望值的路、不改期望值）在上一轮同规格任务中已实际用过。
- Remaining uncertainty: 只剩三处局部裁定（人物实例是否即运行时会话态、人物仓库最终持有多少规则服务、
  `ItemUseEffectDomainService.ApplyAsync` 的归属），三处都已在设计文档中明确列为「等有实物再判」，
  且都不改变任务语义。

## Marginal Control Value

- User precision signal: 用户既表达了速度偏好（「一边重构一边完全绿太费劲了」，提议中间 spec 允许红），
  也在被论证后接受了更慢但可验的跑法（「既然你有更好的想法，那就按你的跑吧」）。同时明确要求
  「重构结束后再找我」——**中途没有人工检查点**。这一条把自动化门的价值抬高了：唯一会看这批改动的
  第二双眼睛，是评审与验证本身。
- Direct failure mode: 具名失效是**搬运时丢掉一条只活在代码里的规则**。这不是假设：上一个同规格根任务
  （`schema-cut-by-domain`）发生了三次，全部同一物种——选项块的「没有」被读成「空的」（每个角色每次
  选人少四字节）、一次保存删光称号册、写入侧的 mode 字面量无人守。三次全部由独立评审抓到，全量测试
  当时都是绿的。本任务把规则从一处搬到另一处，正是同一物种的高发场景。
  第二个具名失效是**静默丢列**：数据形状不变但组装路径改写，漏掉一列时测试不红，而是实机选人下发一个
  字段为 0 的包。第四块（快照重塑）整块都在这个风险面上。
- Useful control: 独立评审（对完整改动集，非增量）；三份整记录表征用例作为不可改期望值的控制；
  每个子成果 accept 前的新鲜验证。这三样恰好各自对应上面两个失效方式的一半。
- Time-for-quality rationale: 用户要求跑完再汇报，所以流程内的门是唯一的门。上一轮的实测数据是
  三条 A 级全部由评审捕获、零条由测试捕获，把这些门去掉等于把这三条放进主干。相对于四个子成果的
  实施工作量，评审与验证的时间占比可接受。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Boundary evidence: 四个成果各有独立的可接受边界与独立的回滚边界。
  (1) 仓库两种产物是纯增量——不删任何东西，可独立接受，回滚只需撤掉新增。
  (2) 物品侧迁实例可独立验收（那处 `ItemArrival` 重复塌成一处是可当场清点的结果），且回滚不影响 (1)。
  (3) 人物实例与规则拆分依赖 (1) 的产物但不依赖 (2)。
  (4) 快照重塑不依赖前三块中的任何一块，风险最高、体量最大（`CharacterEntryAssembly` 624 行 +
  `InventoryEntryAssembly` 578 行 + 三份表征用例），单独回滚的价值最高。
  把四块压成一个 Spec，会让第四块的失败连带撤掉前三块已经可以独立成立的成果。
- Why Direct is insufficient: Direct 拿不到「对完整改动集的独立评审」。而上一轮的证据是这道门捕获了
  三条 A 级、测试捕获了零条。用户又要求中途不汇报，Direct 下没有任何第二双眼睛。
- Reroute conditions: 若第一块落地后发现四块之间存在此处未预见的强耦合（例如快照重塑必须与人物实例
  同时发生才能保绿），则合并受影响的子成果为一个 Spec 并记录理由；若某一块被证明可在一次会话内直接
  完成且无独立回滚价值，把它降级为前一块的一个 Task Unit 而不是单独立项。
