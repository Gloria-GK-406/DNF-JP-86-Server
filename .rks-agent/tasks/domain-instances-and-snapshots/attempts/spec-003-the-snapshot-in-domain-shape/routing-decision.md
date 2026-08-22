# Routing Decision

- TaskContract: `.rks-agent/tasks/domain-instances-and-snapshots/attempts/spec-003-the-snapshot-in-domain-shape/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 目标由设计文档 R-5 裁定，本成果实施它。要动的两族记录、拍平处、
  按位复制处与全部消费者都已逐一定位。
- Requirement stability: 七条成功条件全部可当场核对——全仓搜索某类型名的命中在哪个工程、
  某个方法还在不在、收窄语句位于哪个工程、两组期望值的 `git diff` 是否为空。
- Execution direction: 已知。存储侧先按概念切开、入场侧的段装配再整体移入协议工程，两步之间保持全绿。
- Remaining uncertainty: 领域记录的确切分组名与入场侧装配落在协议工程的哪个文件。不改变成果语义。

## Marginal Control Value

- User precision signal: 用户要求跑完再汇报，中途没有人工检查点。
- Direct failure mode: 本成果是三块里**下发帧风险最集中的一块**，具名失效是**静默丢列**——
  78 个字段在两族记录之间按位置对应，搬运时错位一格不会有编译错误，
  因为相邻字段常是同一种整数类型。上一个同规格根任务真发生过同类：选项块的「没有」被读成「空的」，
  每个角色每次选人少四个字节，全量当时全绿。
  第二个具名失效是**位宽收窄丢失**：`checked((byte)…)` 有 78 处，漏掉一处不会红，
  只会在某个玩家的值第一次超过 255 时把一个错的字节发出去。
- Useful control: 三份整记录表征用例的期望值不动（它们逐字段断言，是错位的唯一挡板）；
  一份 durable 的次序计划；以及对完整改动集的独立评审。
  前两个成果的实测是：两条 A 级均由评审捕获、零条由测试捕获。
- Time-for-quality rationale: 78 个字段的机械搬运，错一格无声。相对实施工作量，
  一份计划加两轮评审的时间占比可接受，且这是三块里最该付这笔钱的一块。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个连贯的验收边界——快照成为领域形状、段布局归协议。两侧不可分：
  只切存储侧会留下一个「领域记录 → 段记录」的按位复制仍在领域里，SC-C1 不成立。
- Why Direct is insufficient: Direct 拿不到对完整改动集的独立评审，而错位一格不会变红。
- Why Multi is unnecessary: 两侧没有各自独立的回滚价值。
- Reroute conditions: 若入场侧的段装配移入协议工程会造成工程依赖倒置（领域反过来依赖协议），
  则把该半移出本成果、单独立项，并在设计文档中记录该约束。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: 需要一份 durable 的次序（先存储侧、再入场侧，两步之间全绿），
  加对完整改动集的独立评审，加新鲜验证。
- Why lighter is insufficient: `sdd-lite` 无 Plan。78 个字段两次搬运，没有 durable 的次序与
  逐步验证点时，中间态会同时存在两族记录，错位将无从定位。
- Why heavier is unnecessary: `sdd-governed` 适用于需要审计强度证据的场合。
  本成果的每一条保证都能用一次真改真跑证伪，且表征用例逐字段断言，独立评审已足够。
