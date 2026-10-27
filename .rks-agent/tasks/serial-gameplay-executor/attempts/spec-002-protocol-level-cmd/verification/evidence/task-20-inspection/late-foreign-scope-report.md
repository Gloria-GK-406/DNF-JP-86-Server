# TASK-20：late foreign scope 单一有界 test-only 诊断

2026-10-25。产品维持 r2-conflict-source 快照不变，原8个强反例断言未修改；仅在现 `ExplicitEffectCommandTests.cs` 加一个真正注册 Matrix/new bus request 的 case，并给现 DisposeProbe 一个可选旧owner token与DemandHealthy只读观察。没有新增provider/Host、fake真实logical、产品修正、Task或ledger写入。

## 实际结果

`verification/logs/task-20-late-foreign-scope-probe-01.{json,log}`：2026-10-24 17:47:31.540485～17:47:40.582418 UTC，dotnet exit1，1行为FAIL（预期typed，实际raw InvalidOperationException），不是编译/fixture/timeout。

第一真实bus request已结束，owner request closed、entry Committed／Ordinal1、完整required attempt已完成。第二真实bus request在不同job／不同request中也实际提交自己的Main并正常登记成功batch；库存3→2→1、两次actualencoder，共两个不同entry（caller Committed／Ordinal2）。第二scope Dispose发生在caller已Close之后，实际用旧owner token进原adapter guard，catch并swallow，Dispose正常返回。

原guard对象只写进旧owner.Failure；同caller job的DemandHealthy抛 **同一对象**，证明ProgramError确已标actualcaller。currentcaller request.Failure仍null。最终原guard同引用raw外抛，无typed；caller participant没有fence。已提交成功batch保留，并在其后追加exact source-close（newEntries=[batch,close]、均原physicalcontext）。两个request均closed，owner仍Committed且Ordinal仍1，callerCommitted／Ordinal2。完整引用判断、job IDs、cause stacks及资产都在raw/派生observation。

这与原same-owner scope用例不同：原token.owner就是正在Close/dispose的request，所以owner.Failed实际进入当前request的scope失败记录；本例token.owner属于先前request，故currentrequest收口根本没有看到late ProgramError。只修Executor的“保留含同一ProgramError引用的body composite”不足：本例body已正常返回，没有composite可保，Bus未运行其默认typed/fence。

## 初步最小方案影响判断（未实施、须主控限定）

要满足本例和原8强例，需要两个不同责任点：

1. `CommandBus` 在Close→scope dispose／记录独立错误之后、原唯一 `FailAsync` 之前，使用既有 `GameplayJob.DemandHealthy()` 对actualcaller首错作有界复查，捕获原对象并合入本次**显式request**失败收口；无ambient／DI currentrequest或新slot。这才能让本例已commit的caller执行typed与精确participants围栏。
2. `GameplayExecutor` 原首错选择点：已有body最终typed／aggregate含同一ProgramError引用时保留完整body结果；body结果为空或不含该引用时维持原ProgramError优先。first-program-error槽／Interlocked first-wins不变，不能以任意业务IOException给encoder章。

仅第2点本身对“已先有ProgramError、body另抛无关联错误”的原control保持原first优先；对含同一引用的完整composite才有意保wrapper／各独立cause。第1点需要额外小心：若无条件将原ProgramError导入一个本来只含无关body错误的request，会新造一个含它的aggregate，继而改变原unrelated控制的最终形状。不能把“first槽没变”当完整兼容证明；需按主控明确的错误发生阶段/本来是否已传播来限定并实际重跑first／unrelated控制。尤其既有首错时后到foreign guard原throw/owner记录仍须保，不能以first-wins掩盖独立发生事实。

本轮只完成这一个测试诊断，未测试或批准上述候选；没有提前修改Bus/Executor，未放宽原8断言。已停止全部共享写与native，交主控决定task-rework范围/精确方法与后续控制。
