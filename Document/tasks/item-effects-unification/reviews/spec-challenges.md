# Spec 独立挑战

- Target: `Document/tasks/item-effects-unification/spec.md`，`state: ready`，关联 `TaskContract.md` revision 1（2026-10-01 读取版本）。
- Governing sources: 同目录 `TaskContract.md`，工作区 `AGENTS.md`，`generating-adversarial-challenges/SKILL.md`。
- Attack scope: 资产完整性、迁移遗漏、经验与宠物既有语义。只读核对当前源码；未执行测试、修改数据库或验证客户端。下述源码依据均为 `CurrentImplementation`。
- 本文件提供独立挑战，供后续审查裁定；不构成 verdict，也不指定执行器实现方案。

## CH-1: 重放必须与合法的下一次相同操作区分

- Risk: high-risk
- Failure and impact: RQ-008／AC-007 要求重放复用提交结果，却没有定义本次操作与合法下一次操作的可观察边界。玩家再次使用同格饲料、再次改造同一件装备，或重新接受并完成同一个可重复任务，可能具有相同业务参数。如果将相同参数视为重放，合法操作会被吞掉；如果每次生成新计划就视为新操作，提交成功但响应失败后的重试仍会重复扣费、抽签或奖励。这会决定各入口的计划／结果归属，不能只靠既有单次成功和回滚回归发现；等统一接缝迁移完再修会波及多个入口。
- Evidence: Spec RQ-008、AC-007；`UseStackableSlice.cs:214-217` 的命令仅携带 Session、Slot、ItemCode，`:366-369` 从请求构造命令未保留独立操作标识；`QuestLog.cs:87-95` 明确允许可重复任务重新接受。源文件分别位于 `Server/src/Dfo.Server.Protocol/Game/UseStackable/` 和 `Server/src/Dfo.Server.Application/Quests/`。这些事实说明“相同业务参数”不能单独证明重放，并不证明当前客户端实际会重传。
- Minimum correction: 在要求或验收中明确去重适用的请求／业务事件边界及生命周期，并加入成对验收：同一次已提交操作重试不再结算；合法下一次相同操作正常结算。边界可以按入口说明，不要求统一采用某种标识、缓存或持久化机制。

## CH-2: 喂食包含的宠物同步不能被误当成简单饱食度加法

- Risk: low-risk
- Failure and impact: 当前喂食先同步宠物运行态，再执行加饱食度。同步可能同时处理经过时间的饱食变化和已排队的房间经验，后者还可能触发进化。若迁移只核对“+30 与消耗一件饲料”，可能改变同步与喂食的先后语义，或者把这些既有变化遗漏于覆盖说明；反过来，为满足“全部变化均在计划中”而重写宠物成长，又会扩大契约明确排除的范围。总体保留既有行为及事务要求已能约束此问题，属于需要定向验收的非阻断挑战。
- Evidence: Spec Scope 保留宠物内部，RQ-004／AC-003 关注饲料增量；TaskContract Out 排除宠物成长重写。`Server/src/Dfo.Server.Application/Creatures/PetInstance.cs:23-34` 加饱食度前调用 SynchronizeAsync；`CreatureRuntimeDomainService.cs:123-179` 同步会更新饱食状态并结算待发房间经验；`PetInstance.cs:60-71` 经验成长可能进一步触发进化。当前基础方法调用链因此包含额外既有业务，而非单纯字段相加。
- Minimum correction: 补充一项饲料验收：存在已流逝时间与待发房间经验时，最终饱食、经验／可能进化及饲料数量与基线一致，故障后不丢失待发奖励。覆盖记录明确这些保留的宠物内部路径归属即可，无需把整个宠物成长拆成新效果类型。

## CH-3: 任务成长奖励还包含职业变化及技能重置

- Risk: low-risk
- Failure and impact: AC-006 列举物品、金币、经验、跨级和满级，容易令任务迁移的验证只检查等级经验。现有任务成长奖励还可更改 GrowType 并重置技能；如果仅让主奖励进入计划，保留一个未说明的职业奖励业务调用，就可能留下用户特别担心的旧写入旁路。RQ-007 的“关联成长”及 RQ-009 的写入覆盖已经原则性涵盖它，因此可在阶段检查中低成本解决，不需要新增全技能系统改造。
- Evidence: `Server/src/Dfo.Server.Protocol/Game/QuestCompletion/FinishQuestCommand.cs:236-237` 奖励包含 GrowType 时调用 careers.AdvanceAsync；`Server/src/Dfo.Server.Application/Characters/CharacterCareerDomainService.cs:25-28` 同时执行 SetGrowTypeAsync 与 ResetAllAsync。Spec RQ-007／AC-006 未点明该复合结果，RQ-009／AC-008 要求未解释旁路归零。
- Minimum correction: 任务阶段覆盖表与验收纳入已有转职奖励：职业状态及技能重置和任务清除、其余奖励一起提交／回滚，并明确共享效果与保留底层执行的归属。无需规定技能重置内部的字段级实现。
