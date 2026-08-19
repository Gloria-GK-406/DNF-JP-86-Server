# Routing Decision

- TaskContract: `.rks-agent/tasks/inventory-container-refactor/TaskContract.md`
- Contract revision: `2`

## SDD Readiness

- Intent and outcome stability: 设计经过一轮完整的往返对话收敛，且用户在过程中主动否掉了
  两个由 Agent 提出的形状（把叠加做进服务、让判定服务吃事实），最终形态是用户自己给出的。
  两个成果的可观察结果都已明确：其一是「对外行为一字不变」，其二是「84 个 `unlimited*`
  道具不再被扣减」与「冷却按 `[cooltime group]` 共享生效」。
- Requirement stability: 容器要自报什么、注册与绑定的语义（首个注册者为当前、移除当前时
  回落、无可回落则报错）、判定服务的入参形状、效果的目标域划分、冷却的键与存储方式，
  全部已定且可逐条评审。
- Execution direction: 接入点已勘定——`0x000D` / `0x000E` 的投影已经由
  `InventoryContainerState` 驱动，线上格式不需要改；持久容器包住既有仓储即可。
- Remaining uncertainty: 只剩非语义的局部选择（效果是否包在单个事务里、接口方法命名），
  已在契约的 Assumptions 中显式留给实现阶段。

## Marginal Control Value

- User precision signal: 用户明确要求「值得我们用sdd来跑。当然只是为了实现别漂移」，
  并且要求把第二刀现在就记录下来，理由是「省的后面上下文多了，到时候忘了第二刀干啥」。
  即：此处要精度与留痕，而非速度。
- Direct failure mode: 两个具体失败。其一，第一个成果是纯重构，Direct 下最可能的偏离是
  搬迁过程中「顺手」改掉行为——而没有被钉死的验收边界时，这种偏离不会被发现。其二，
  第二刀的设计（判定/效果/冷却三者的职责与边界）目前只存在于对话里，本轮上下文已经很长，
  压缩后极可能丢失，届时会以不同的形状重做一遍。
- Useful control: 第一个成果由 Spec 把「对外行为一字不变」钉成验收条件；第二个成果由一份
  持久化的子契约把设计固定下来，使其不依赖对话上下文存活。两者都配独立评审。
- Time-for-quality rationale: 这条接缝是后续所有背包与副本协议的共同基础。参考实现在同一
  位置没有设边界，代价是 35,647 行、四份重复常量、283 处调用方加锁、以及为死亡之塔复制的
  857 行。在只有两个调用点的此刻付出一次流程成本，换掉的是那条已被实测过的路径。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Boundary evidence: 两个成果各自独立可验收，且第一个单独交付即有价值。成果一的验收是
  「行为零变化 + 既有测试全绿 + 实机一致」，完全不涉及成果二的任何新行为；成果二的验收是
  「`unlimited*` 不再被扣减 + 冷却共享生效」，是可观察的行为变更。两者的接受边界、回滚边界
  互不重叠：成果一被接受后，成果二失败也不必回退成果一。
- Why Direct is insufficient: Direct 无法为一次纯重构提供「行为不变」的固定验收面，也无法
  把第二刀的设计从对话上下文里救出来——而这两点正是用户要求 SDD 的原因。
- Reroute conditions: 若成果一在实施中发现容器抽象无法在不改变线上帧的前提下接管
  `0x000D` / `0x000E` 投影，则两个成果的边界不再独立，应停在安全点重新评估结构；
  若成果二的验收被证明依赖成果一尚未提供的能力，则应合并为一个 Spec 而非两个。
