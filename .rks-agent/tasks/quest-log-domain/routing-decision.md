# Routing Decision

- TaskContract: `.rks-agent/tasks/quest-log-domain/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 形状在前置对话里已经谈定并由用户裁决——「对，是挪出去」，
  即两个候选方案里的第一个（任务行迁出人物存储，与背包完全同构），而不是留在原处加一层外壳。
  可观察的结果也已确定：任务由自己的桶作答、协议层不再持有受理规则、线级字节不变。
- Requirement stability: 九条成功条件全部可观察且已枚举——桶的存在与不缓存、存储迁出且同参与者、
  已通关可读回、规则移位、两个写入方改造、读取路径关系理清、线级与落库不变、测试通过、文档更新。
- Execution direction: 中心方向不是待定项，而是**照抄仓内既有同构物**：背包
  （`IItemContainer` + `ICharacterContainerDomainService` + `ContainerId.ForCharacter` + `IItemStore`）。
  模板已存在且可读，不需要发明。
- Remaining uncertainty: 只剩非语义的局部选择（接口动词的具体命名、已通关表的列形状、
  入场投影是合并还是保留并交代）。任一选择都不改变任务含义。

## Marginal Control Value

- User precision signal: 用户主动要求走 SDD，并且**以上下文压缩为由**要求先产出契约与 spec、plan
  再执行——这是对「跨会话不丢约束」的明确精度诉求，而非对速度的诉求。
- Direct failure mode: 执行发生在**压缩之后的新上下文**里，届时设计对话已不在场。具体会丢的是：
  ①「不得改变已验证的线级字节」这条约束的来源与判据；②同一张表有两个读法、两个写入方这件事
  （漏掉任一处都会留下半迁状态）；③「不快照目标与奖励」这个已裁定的取舍——新上下文很可能
  按用户原话「做的任务里要记录奖励，目标是什么」重新实现成快照，而那已被判定为会让服务端与
  客户端不一致。Direct 下这三条没有任何载体，只能靠记忆。
- Useful control: 一份耐久 **Plan**，把迁移按依赖定序并把上述取舍写进步骤；一份 **Spec** 把
  「线级字节不变」定成可观察验收而不是口头约束。二者合起来正是跨压缩的交接物。
- Time-for-quality rationale: 额外成本是本轮多写两份文件；收益是执行上下文不必重新推导已经
  裁决过的事，也不会漏掉两个写入方之一。用户已明确愿意为此付时间。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个连贯成果——任务成为自己的领域。搬迁、建模、规则移位、文档四者
  互相依赖，任何一个单独落地都不构成可验收状态（例如只建桶不迁存储，任务行仍挂在人物上；
  只迁存储不移规则，规则仍在协议层）。没有可独立接受或独立回滚的子边界。
- Why Direct is insufficient: 见上「Direct failure mode」——跨压缩执行会丢掉三条已裁决的约束，
  而这三条都不是实现细节，是会改变结果的取舍。
- Why Multi is unnecessary: 只有一个验收边界。文件数与步骤数不构成 Multi 边界。
- Reroute conditions: ①若发现任务状态必须与物品分库（则事务参与者假设不成立，边界与方案都要重议）；
  ②若入场投影的读取路径无法在不动服务架构的前提下理清（则该条要么出 scope 要么重议契约）；
  ③若「已通关」被发现另有承载而非新表（则 SC-3 的证据形式改变）。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: 需要的额外控制正是 Standard 提供的那一项——**耐久的 Plan 与依赖定序**。
  搬迁有明确的先后：先立自有存储与桶、再改两个写入方、再撤掉人物存储上的分区、最后理清读取路径；
  次序错了会出现「分区已撤而写入方未改」这种编译不过或半迁的中间态。Plan 让这个次序跨压缩存活。
  任务性质是行为不变的重构，`code-refactor` Profile 的测试先行与完整结果复审正对得上
  「线级字节不变」这条核心约束。
- Why lighter is insufficient: `sdd-lite` 是**无 Plan** 的。本任务唯一真正买的控制就是 Plan，
  因为执行上下文与决策上下文不是同一个。Lite 下实现期的局部规划只存在于当时的会话里，
  而那正是要被压缩掉的东西。
- Why heavier is unnecessary: `sdd-governed` 的独立挑战与审计强度证据没有对应的高后果失败路径：
  不触及安全、凭据、隐私、财务合规、不可逆外部变更；库变更在开发阶段采取重建而非迁移，
  没有持久数据丢失路径。普通复审加新鲜验证足以保护「字节不变」这一核心，因为它有逐字节的
  线级重放作判据。
