# Plan 独立挑战记录

- Target：`../plan.md`，`mode: task-units`、`state: ready`；2026-10-01读取版本，SHA-256 `916F5300BFFC72D489508B98668BAADEC8C25BAA52AF88781C58737175ECF1C9`。
- Governing sources：`../TaskContract.md` revision 1；`../spec.md`（`spec-review.md` 已批准，SHA-256 `7E9FB4A1C3867A2604EABB0E9C7815E653FD995F65C0F081A348D1B6D2639B96`）；工作区 `AGENTS.md`；`generating-adversarial-challenges/SKILL.md`。
- Attack scope：共享执行接口的建立顺序、混合effect的整批结算和旧写入覆盖。仅检查计划及当前源码，没有执行测试、访问数据库或修改生产代码。该Plan只保留高层依赖，后续接口和局部算法由Task Unit确定；以下问题不要求现在展开所有远期接口，也不构成审查结论。

## CH-1: T-004任务经验接入与T-005共享经验实现之间的迁移缝隙

- Risk: low-risk
- Failure and impact: T-004先迁移任务奖励，T-005再完成共享经验和成长。如果T-004保留原AwardAsync并同时加入经验effect，任务会双发经验；若直接移除旧调用但新effect尚无真实执行，任务又会漏发。另一种情况是用“执行旧AwardAsync”暂存全部成长业务，然后T-005只迁移怪物／通关调用方，任务旁路留到最终检查。现有阶段回归和T-006可以控制这些问题，无须现在颠倒全部单位顺序；需要在T-004具体计划中显式处理衔接。
- Evidence: `../plan.md:32-33`将任务奖励置于共享经验迁移之前；`../spec.md:29,42`要求经验及成长通过共享effects并与任务一致提交。当前 `Server/src/Dfo.Server.Protocol/Game/QuestCompletion/FinishQuestCommand.cs:222-237`在奖励发放后调用共享AwardAsync，再调用转职服务，两个调用都不能只按任务物品奖励迁移状态推断为已覆盖。
- Minimum correction: T-004详细计划明确经验／成长基础effect执行由哪个单位提供；若暂时保留未迁移链路，记录精确写入路径并指派给T-005，T-005结束时复核任务完成入口。局部验证比较原任务一次结算的经验增量、成长结果和失败回滚，避免新旧路径共同执行。

## CH-2: 混合effect不能按类型切组后丢失容量和顺序依赖

- Risk: low-risk
- Failure and impact: 为复用现有批量库存结算，执行器可能先抽取全部消费／发放交给库存适配器，再执行其他类型。当同一计划中某项状态或目标变化使后面的发放可行，按类型切组可能仍按旧状态拒绝容量，或使同槽后续effect读到模拟前状态；最终通知也可能拿到中间回值。反之，已经消耗材料后发放失败若在适配器里转成普通Failure，仍可能部分提交。Plan已有累计当前状态和异常回滚约束，因此这属于T-001至T-003局部实现需要验证的组合问题，并非高层Plan必须现在选定算法。
- Evidence: `../plan.md:19-20`同时提出完整数组预检、同事务累计及复用库存模拟；`../spec.md:37-38`要求同槽累计、腾槽及竞争最后槽位。当前 `Server/src/Dfo.Server.Application/Inventory/OpeningInventoryDomainService.cs:54-99`接收独立的消费／发放数组，先模拟消费后的库存；该方法的局部模型不能自动证明通用effect列表在不同类型之间保持相同顺序和可见状态。
- Minimum correction: 在共享执行及批量适配的单位计划中确定一种顺序／模拟状态约定，并加入一个实际存在的混合类型计划探针：后续effect须读取前面effect的暂存结果，末步失败整批回滚，成功receipt只含最终状态。无需预写整个库存算法。

## CH-3: 任务“收集事实／刷新投影”调用中的真实写入可能被归为范围外

- Risk: low-risk
- Failure and impact: 只从扣费／Grant／Award等明显资产写入反查覆盖，可能漏掉任务进度Report和Synchronize中的写入。任务完成目前先刷新持有物目标，再判断能否交付；若迁移时继续把这一步当只读事实收集，在前置拒绝返回时仍可能提交任务进度变化，且成功计划也看不出这些连带变化。用户要求所有相关状态影响从计划可见，Spec要求前置拒绝无修改；但不需要因此重写范围外的整个任务条件系统。T-004的精确写入审查能廉价控制该问题。
- Evidence: `../plan.md:23,32`要求写入分类并迁移连带进度，但分类包含生命周期／范围外，需要沿调用链确认而非按调用名判断。当前 `Server/src/Dfo.Server.Protocol/Game/QuestCompletion/FinishQuestCommand.cs:145-154`在FinishAsync拒绝前调用ReportAsync；`Server/src/Dfo.Server.Application/Quests/QuestProgressDomainService.cs:74-76`转入SynchronizeAsync；`Server/src/Dfo.Server.Application/Quests/SeekingQuestProgressDomainService.cs:58-64`实际RecordProgressAsync并更新进度。任务完成后的 `FinishQuestCommand.cs:239-244`同样会触发连带进度写入。
- Minimum correction: T-004覆盖表将这两条任务完成Report调用链追到实际RecordProgress写入，区分只读事实计算与需进入计划的进度变化；增加“持有物事实已变化但本次交付被拒绝”的局部探针，核对拒绝后状态与计划边界。其他任务事件入口继续遵循契约范围外边界。
