# TASK-20 r2：ProgramError 终局覆盖已完成 typed/独立清理结果

实际 HEAD仍d6708faf，2026-10-25。本报告是 §10 新实证/主控 task-rework 输入，不是 Result，不改变原断言、B3/P4 或作业API。

## 复现和界限

- `task-20-guard-green-01`：同名原两个 guard RED 目标实际2/2 GREEN，real SQLdelegate0、gold100000、caller最终失败/Failure保原cause；未删除或削弱断言。
- `task-20-whole-focused-01`：真正dotnet exit1，58＝50pass＋8fail；完整meta/console保在verification/logs。四新whole decoder/actualencoder/actionSQL/nativeCommit oracle、原CollectionBox pure与Network cases全部通过。8个失败都是原shared guard/cleanup contract，不属于原Dimension基线。
- 新窄API精确复用既有Active/token.Fault first-wins；Enter、原DemandTarget窄guard与Begin catch标current caller，actual EncoderFailed仍只标request。源码快照另存 r2-conflict-source/，不覆盖旧49manifest。

8个失败（每条theory display独立计）：

1. A_completed_attempt_is_rejected_after_Close_even_on_its_original_job_during_scope_disposal(swallowed: True)
2. 同上 swallowed: False
3. A_real_live_attempt_is_rejected_on_another_job_before_encoding_and_the_owner_is_drained
4. A_same_ID_replacement_at_registration_cannot_borrow_the_original_attempt
5. A_real_completed_attempt_cannot_register_twice_in_the_same_request
6. A_real_pending_rollback_cleanup_cannot_issue_an_attempt_and_its_failure_keeps_all_causes_and_the_fence
7. Encoder_failure_and_a_scope_guard_propagation_count_each_real_cause_once_and_keep_an_extra_scope_cause(extra: True)
8. 同上 extra: False

原断言要求 TransactionOutcomeUncertainException、全部精确participant围栏以及独立scope causes；实际最外层却成为 registration guard 的原 InvalidOperationException。例如encoder E＋scope guard C＋extra cleanup D 原须最终typed aggregate包含E,C,D；新的current ProgramError C最终覆盖它，不可仅凭实际围栏发生或caller禁写称正确。

## 已证根因

`CommandBus.ExplicitAsync` 原收口仍在 scope活着时Drain → Close → scope cleanup独立错误记录 → 最后一次CommandWorkUnits.FailAsync；后者已依据HasCommittedUnit构造typed并逐participant围栏。外层 `GameplayExecutor.Job<T>.RunAsync` 在body catch之后现有代码：

```csharp
if (executor.ProgramErrorOf(token) is { } programError)
{
    failure = programError;
    cancelled = false;
}
```

新的registration guard第一次进入原ProgramError槽，故此处无条件把body已经完成的typed/aggregate替换为raw guard。此前registration guard仅request.Failed、没有ProgramError，因此这些原强测试得到typed/aggregate。这个冲突由新窄API激活，不是CollectionBox codec或普通fixture错误。scope同一原cause独立发生/全cleanup final资格不能通过放宽旧assert或把所有guard称raw来解决。

## 待主控审定的最小候选（未实施）

现16路径内，窄API被要求Active时必须existing token.Fault；无其它字段/新receipt/currentRequest，故不能在RecordProgramError里暗装另一套终判，或用EndHook/secondHost/ambient绕开。仅不标same-owner只能解决一部分：unknown/foreign caller在自己已有commit时仍需要完整typed/aggregate，不能把它作为足够修复。

候选是新增且仅新增物理路径 `Server/src/Dfo.Server.Application/Execution/GameplayExecutor.cs`，在上面**现有选择最终失败**位置保留已完整传播该 ProgramError 的body结果：若body failure为空或不含同一programError对象，保持原ProgramError优先；若body最终failure为包含同一原ProgramError的typed/aggregate，保留其wrapper/所有独立cause，仍failed=true/同sourceclose，原ProgramError槽/first-wins不变。可用一个private纯引用树检查（InnerException及Aggregate.InnerExceptions）；不能按异常类型/文字造登记来源，不改queue/drain/host/关闭/日志次数或写权。

须由主控明确限定是否对全部含同引用的body composite生效，或只限已经完成的typed不确定结果；前者自然保完整cleanup但触及旧self-wait composite终局，后者需核无commit多独立failure等路径，不能未实测就认充分。另一方案需明确证明对foreign/unknown和两个已commit参与者仍保持B3全部cause；目前没有16路径内且不暗改既定API/模型的充分方案。

后继应先原8强反例GREEN＋原两个callerGREEN，同时保first-programerror prior/unrelated failure对照、已提交多参与者/cleanup同对象、三合法actualencoder raw无ProgramError，随后完成原r2剩余所有矩阵/正式八full。不得修改原断言求绿。当前已停止产品推进，等待主控task-rework，不继续扩白名单或改Executor。
