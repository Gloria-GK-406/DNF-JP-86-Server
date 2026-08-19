# Routing Decision

- TaskContract: `.rks-agent/tasks/inventory-container-refactor/attempts/spec-001-item-container-abstraction/TaskContract.md`
- Contract revision: `2`

## SDD Readiness

- Intent and outcome stability: 成果边界由根契约固定，且本子任务是纯搬迁——目标状态
  「道具读写经容器完成、角色对当前容器有显式绑定」与验收「对外行为一字不变」互相印证，
  不存在需要在实施中决定的语义。
- Requirement stability: 容器要自报什么、注册与绑定的四条语义规则（首个注册者为当前、
  移除当前时回落、无可回落则报错、使用方不区分持久与内存）已逐条可评审。
- Execution direction: 接入点已勘定——`0x000D` / `0x000E` 的编码器已经接受
  `InventoryContainerState`，只需改变该状态的来源；持久容器包住既有仓储即可，
  两个协议处理器共两个调用点。
- Remaining uncertainty: 仅剩接口方法命名与文件划分等局部选择，不改变成果语义。

## Marginal Control Value

- User precision signal: 用户要求「值得我们用sdd来跑。当然只是为了实现别漂移」，
  即此处要的是边界固定，而非流程完备。
- Direct failure mode: 纯重构最典型的偏离是搬迁途中「顺手」改掉行为——例如在迁移
  `UseStackableHandler` 时同时修正 `unlimited*` 道具被扣减的已知缺陷。该缺陷属于根任务
  成果二，若在此处一并改掉，成果一的「行为零变化」验收面就失效，两个成果的独立回滚
  边界也随之消失。
- Useful control: Spec 把「对外行为一字不变」与「既有测试语义不得修改」钉成验收条件；
  完成后的独立评审以既有测试基线与实机表现为判据。
- Time-for-quality rationale: 这层接缝是后续所有背包与副本协议的共同基础，且此刻只有
  两个调用点。参考实现在同一位置没有设边界，实测代价是背包模块 35,647 行、快捷栏区间
  常量四处重复、283 处调用方加锁，以及为死亡之塔复制的 857 行。一次流程成本换掉的是
  这条已被验证过的路径。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 单一连贯的接受边界——容器抽象、绑定服务、两个处理器迁移、投影来源
  更换，共同且只共同支撑「行为零变化地换掉存储接缝」这一个成果，任何一半单独交付都不
  成立（只有抽象没有调用方＝死代码；只迁移调用方没有抽象＝没换成什么）。
- Why Direct is insufficient: Direct 无法为纯重构提供固定的「行为不变」验收面，而这正是
  本子任务唯一真正的风险所在。
- Why Multi is unnecessary: 本子任务不含第二个可独立接受的成果；其内部各部分共享同一个
  回滚边界。
- Reroute conditions: 若实施中发现现有 `InventoryContainerState` 无法在不改动线上格式的
  前提下承载容器投影，则「行为零变化」不再是可达的验收面，应停在安全点并将该事实上报
  根控制器重新评估边界。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-refactor`
- Why this is sufficient: 需要的控制只有两个——把「对外行为零变化」固定成耐久的验收面，
  以及在实施完成后用一次完整结果评审对照契约与 Spec 检查是否偏离。Lite 恰好提供这两者，
  再加一次新鲜验证。中心执行方向已勘定（持久容器包住既有仓储、`0x000D` 编码器已接受
  `InventoryContainerState`、待迁移的调用点只有两个），实施中的局部选择可安全内联决定。
- Why lighter is insufficient: Direct 无法为纯重构提供固定的验收面。本任务最可信的失败是
  在迁移 `UseStackableHandler` 时顺手修掉 `unlimited*` 道具被扣减的已知缺陷——那属于根任务
  成果二，一旦在此发生，「行为零变化」这个验收面即失效，两个成果的独立回滚边界也随之消失。
  这类偏离靠一次廉价的确定性检查发现不了，因为它会让测试更「正确」而不是变红。
- Why heavier is unnecessary: Standard 的耐久 Plan 在此不增加保护——容器要自报什么、
  绑定的四条语义规则、迁移哪两个调用点，全部已在子契约中固定，Plan 只会把同样的内容抄一遍。
  不存在需要提前排序的耦合决策，也不涉及 Governed 的任何硬性门槛（无安全、凭据、隐私、
  资金合规、不可逆外部变更或无恢复路径的持久数据丢失）。
