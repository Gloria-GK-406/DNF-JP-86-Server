---
kind: rks-review
verdict: changes_required
remediation: material
remediation_target: spec
---

# 效果提交后即时登记失败边界复核

## 目标与适用范围

独立 reviewer：`b_effect_boundary_review`，非实现者；适用 `reviewing-work/SKILL.md` 与 `references/plan.md` 的高层 Plan 共享决策准则。原只读检查工作区为 `.claude/worktrees/refactor-session-20261021`，检查 HEAD 为 `d1020731bac4f5785739d76141e4918862a1a1ea`。本次目标是子 TaskContract r1、Spec B1（尤其 RQ-006/007/008/015、VC 表）及 Plan P1 §4 的共享失败边界，判断 Task04 默认机制能否直接承接下一效果启用单元；不是 Plan 整体重审，也不是尚未存在的下一 Task 详细计划审查。

主控随后告知 Task10 已获独立 approved，检查点为 `2d8081ae2a3ef687e88ec284df9e97588cc625a7`，工作区 clean，Effect/Main 仍 disabled，相关已检查边界未变。该检查点信息由主控提供，本 reviewer 未重新执行 git 核验。本结论不回溯否定、不阻塞 Task10；它约束后续首次启用显式 effect admission/Main runner 的单元。

原复核仅静态读取源码、测试及治理制品，未运行测试、native 程序或 git mutation。此轮仅按主控授权创建本 review 文件，不修改 Spec、Plan、Task、产品、旧 review、根契约或 A 封存制品。

## 裁定

**确认存在局部的实际承接冲突。** Task04 的通用 `FailAsync` 若原样承接新 Main，以及 committed replay 的普通 Write 后即时登记，会把旧 raw 登记失败改成 typed uncertainty 并增加 selected fence，破坏既有重放断言。当前 B1 对“协议 CMD 内提交后非传输失败”的宽泛文字与“不改变已封存行为及原断言”的约束没有明确分出这个旧边界；下一 Task 不应自行发明异常豁免来消解它。

**允许由父/主控在现有授权内作有界 amendment，无须新用户语义授权或修改根契约。** 用户及子契约已经要求保留 A 行为，且仅 VC-B1/B2/B3 可变。将原 fine-grained CMD 返回之后、已有证据支持的具名即时 raw 登记出口明确保留下来，是落实该兼容目标；不是新增 VC、改变核心 acceptance，或重做 A。它与保留 MonsterDeath/Altar 原 typed 发布保护、提交后业务失败/未知结果/额外 dispose 失败全参与者围栏可以同时满足，故本发现不要求将 Spec 判为 terminal，也不宣告 P1 整体失效。

主控已作本地处置裁定：Task10 完成并 checkpoint 后处理本边界；先完成有界 Spec/Plan 澄清及其受影响的独立 challenge/review，再做 amendment-only checkpoint，之后才在后续 Task 启用效果。此处 `remediation: material` / `remediation_target: spec` 是 Plan review 对上游需求歧义的分类，不表示必须扩大产品范围或重开根契约。

## 决定性事实

1. **首次 UseStackable 的登记点在旧细粒度提交之外。** `Server/src/Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs:346-376` 先等待 `ResolveAsync`，随后组消息并 `SendExplicitAsync`，没有 catch；`ResolveAsync:399-402` 等待旧 `UseStackableCommand`。`Server/src/Dfo.Server.Application/Commands/Pipeline/SelectedSessionSettlementBehavior.cs:18-24` 在旧命令返回前执行 `operations.Settled`；`Server/src/Dfo.Server.Application/Effects/EffectOperationContext.cs:102-107` 将条目置为 Committed。后续即时登记失败不再经过该旧主结算管线。
2. **旧 ledger 保留 Committed 重放。** `Server/src/Dfo.Server.Application/Effects/EffectOperationLedger.cs:57-61` 对 Committed 调用当前状态重放；`:69-74` 只在 finally 将仍为 Pending 的条目变为 Rejected，没有捕获后续登记错误并将 Committed 改成 Blocked。因此原错误可以 raw 上抛而账本仍可重放。
3. **不是 physical transport 故障。** `Server/src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs:45-54`、`:61-79`、`:88-138` 在当前 job 同步编码后才登记；required direct 编码错误会抛出，best-effort target 编码失败则维持原逐目标跳过。`:141-146` 的 `ICommandMessageOutbox.Register*` 直接复用这些路径。`Server/src/Dfo.Server.Protocol/Game/IGameCommandOutbox.cs:14-23` 只是 typed 转交，并未把编码延迟到 transport。
4. **两条旧测试的注错确在编码点。** `Server/tests/Dfo.Server.Application.Tests/Inventory/NpcShopPurchaseTests.cs:574-594` 的 ShopSender 将 FailExplicit/FailBatch 交给 RecordedOutbound.FailAny；`Server/tests/Support/RecordedOutbound.cs:326-369` 使用实际 adapter，并在登记期间设置 in-flight 标记；`Server/tests/Support/OutboundRegistrationParts.cs:82-101` 的 FaultingEncoder 在 EncodeMessage/EncodeBatch 的 Check 中抛出该错误。错误文本中出现 transport 不改变其真实出处。
5. **旧可观察结果已有断言。** `Server/tests/Dfo.Server.Application.Tests/Sessions/EffectOperationReplayTests.cs:97-126` 要求首次登记 IOException 后只扣一次，随后相同 raw 请求重放当前库存、不重发原 ACK；`:219-227` 要求 replay batch 登记 IOException 后仍保留提交状态，后来重放得到已变为 7 的当前数量。原测试应保留，不能改断言或把注错搬到 physical transport 来适配新策略。
6. **source close 与 selected fence 是不同保护。** `Server/src/Dfo.Server.Application/Execution/GameplayExecutor.cs:294-297` 在失败作业结束时提交 outbox；`Server/src/Dfo.Server.Application/Execution/Outbox/GameplayOutbox.cs:213-240` 保留原条目顺序并在最后追加 source close。fixture 中同一 connection 的再次 dispatch 证明账本与围栏语义，不证明真实断线后仍可沿原 socket 接收。
7. **replay 仍须对应原写模式事务。** `Server/src/Dfo.Server.Application/ApplicationModule.cs:788-789` 将 ReadReplayStateCommand 注册为 Transactional；`Server/src/Dfo.Server.Application/Effects/ReplayStateDomainService.cs:28-33` 的 handler 只调用 ReadAsync。`Server/src/Dfo.Server.Protocol/Game/ReplayStateProjectionDelivery.cs:36-61` 的 MirrorMerge、Create、batch 登记、Town 投影位于命令返回后。新 replay 应直接普通 Write 读取快照，再在该单元之外走 sink；不能 nested bus，也不能默改为 ReadOnly。
8. **Task04 默认策略将改变以上结果。** `Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs:90-104` 只要 HasCommittedUnit 就把非 typed 错误包装成 PostCommitPublicationFailure，并遍历 CommittedParticipants 围栏。普通 Write 即使只读取快照，成功后也会记录已提交。`Server/src/Dfo.Server.Application/Commands/CommandBus.cs:90-109` 还在 scope Dispose 失败时合并 cause 再进入 FailAsync。
9. **不能反向推广为“所有登记错误皆 raw”。** `Server/src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs:455-491` 将评估、组帧及登记包含在原受保护发布段，catch 包装 PostCommitPublicationFailure 并围栏 captured 全体。该既有 typed 语义必须继续保留。`Server/tests/Dfo.Server.Application.Tests/Transactions/CommandWorkUnitsTests.cs:265-302` 明确覆盖普通已提交后 IOException 仍 typed、保原 inner cause、全参与者 fence；`:306-317` 覆盖业务与 rollback cleanup 双 cause 同时保留及全体 fence。

## 最小受影响需求与接受条件

- **RQ-006 / AC-002、AC-003：** 明确“一次效果主结算已被确定记录为 Committed”之后，原 fine-grained CMD 之外的具名即时 raw 登记失败仍保持 Committed 可重放；不将其伪称为传输失败。已提交同序号重放、不重复生效、当前性、全部参与者及 DiscardExact 保护不放宽。原已受保护的发布路径仍 typed 且逐参与者围栏。
- **RQ-007 / AC-004：** UseStackable 的旧主事务与之后即时登记仍分段；replay 的旧 Transactional 对应普通 Write，后续 sink 保持在其之外。迁移账明确旧位置、最终位置与失败策略，不合并事务或改只读。
- **RQ-008 / AC-004、AC-005：** 该澄清不启用 Compose/deferred encoding，不削弱真实提交、真回滚与不确定结果区分。真实不确定、提交后业务/受保护发布错误，以及额外 dispose/cleanup 失败仍走原 typed 全参与者围栏。
- **RQ-015 / AC-009、VC 表：** 原断言保留，新增对照只补充来源和异常对象身份；该项作为旧行为保留边界记录，不新增 VC。P1 §4/§5 及共享回归说明对应澄清；不改根契约或 A 封存结论。

## 最小局部 API / 策略约束

1. 默认保持现有 fail-closed 结算策略。只有经过旧边界映射具名确认、确实处于主单元/普通 replay Write 完成之后的即时登记尝试，才有保持 raw 的资格。当前证据至少覆盖 UseStackable required direct 编码登记与 committed replay batch 编码登记；不得据此授予任何 CMD 或所有 encoder 全局豁免。
2. 由真实 outbox adapter 在窄编码调用点记录异常真实来源，并原样 `throw;`。从本次 CommandExecutionContext 通过显式的登记 handle/sink 传递到 Game adapter 和底层 outbox；不藏入 AsyncLocal、GameplayJob slot、单例可变字段、DI 当前请求或环境事务。Abstractions 的窄契约不能反向引用 Application 的 CommandExecutionContext。
3. 不提供可包任意业务 Func 的“登记阶段”、可由 CMD 任意设定的 IgnorePostCommitFailure，或按 IOException 类型/错误文本作 transport 分类。来源记录只覆盖一次真实登记尝试；原 protected 发布段或 typed uncertainty 的优先级高于该记录。
4. 在 drain 与 cleanup 收口后检查最终 cause。只有最终仍是获许可的那一个原始登记 cause，且没有 typed uncertainty、业务错误或额外独立 dispose/cleanup 失败，才保持 raw 原对象、Committed 账本及原 source close。新增独立失败必须撤销 raw-only 资格；不能让一个永久 exemption 标志掩盖后来的错误。typed/aggregate 路径保留原 cause 对象引用，不能替换为新造的等价异常。
5. 仍要锁住失败后的请求执行权：即使业务 catch 掉登记错误，也不能继续结算或把失败包装成成功。该控制应复用显式请求生命周期与最终失败收口，不引入新的隐式请求容器。

这些是后续局部设计必须满足的约束，不预先指定全局异常框架或尚未规划 Task 的全部接口名称。

## 受影响检查

- 两条 EffectOperationReplayTests 原断言保持，继续在登记编码点注错；验证持久效果只生效一次，replay 读当前值，不重发原 ACK。
- 增补精确原 cause 对象、ledger Committed、无 selected fence、精确 source-close 的断言；不把 fixture 同连接重放夸大为实机 socket 结论。
- 对照相同 IOException 在提交后业务阶段、原 protected 发布段仍 typed，保原 cause 并 fence 全部精确参与者。
- raw 登记失败叠加 scope Dispose/transaction cleanup 独立失败时仍 typed/aggregate、保留两 cause 并全体 fence；已有 typed uncertainty 即使经过登记入口也不获 raw 豁免。
- 显式来源句柄跨请求、closed/leaked 使用和 singleton 连续请求不串状态；错误被 catch 后不能继续写或以成功结束。
- replay 对应普通 Write，后续即时 sink 不在事务内；保无 nested bus、无 Compose/deferred encoding 的结构判据。
- 保持原 best-effort target 失败隔离、required 注册顺序与已提交条目/source close 顺序。

上述检查是 amendment 后下一效果单元的输入，不是本次已运行证据。

## 挑战处置与移交

- 旧 raw 登记边界与 Task04 默认 FailAsync 的行为冲突：**confirmed**。
- 以 IOException 类型或带 transport 的文字将此解释为物理传输失败：**rejected**。
- 在 Task 内自行新增通用异常豁免，或改变旧断言/注错位置来适配默认策略：**rejected**。
- B1/P1 的该边界：**upstream-return，限本局部 amendment**。父/主控可以依据既有保 A 行为授权澄清，经受影响独立 challenge/review 和 amendment-only checkpoint 后继续，无须新用户语义授权、根契约修改或 terminal 处置。
- Task10 的已批准范围与 checkpoint：**不受影响**。

controller 负责 amendment、评审与后续启用次序；本 review 不自行推进 Spec/Task 状态或宣布产品完成。
