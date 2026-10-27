---
kind: rks-review
target: tasks/TASK-17.md
verdict: approved
---

> 第 1 轮结论为 `changes_required`（1 条阻断、2 条中、1 条低、1 条细节）；第 2 轮复核后更新为 `approved`，见 `## Round 2`。两轮评审者都按调用方要求只读，本文件由主控方按评审回报落盘。

# TASK-17 结果快速审查（独立审查者）

- 审查对象：`tasks/TASK-17.md`；第 1 轮为合同 Revision 1，第 2 轮为 Revision 2 与第 2 轮 Result。改动为基准 `f13e612a` 之上的未提交差异。
- 适用标准：reviewing-work 的 Task Unit 快速审查，加 Code 标准，范围限于本单元。

## Round 1 — `changes_required`

1. **【阻断】** `Protocol/Game/Party/PartyTownProjectionDelivery.cs:65`、`:197` 两处 `PartyDeliveryStream.Gate.WaitAsync` 未挂门断言。这是附录 F「删除」栏的同一把门（同一 `PartyProjectionStreamRegistry`）。边界检索只看了计划列出的文件，漏项来自 `t004c-entry.md` 的挂点清单；§14.1 中两条删除用例写的后继测的是 `PartyNotificationSlice` 那一处。
2. **【中】** `MultiAccountExecutionTests` 被删，写的后继测的是 `SelectedSessionTransitionGate.AcquireManyExclusiveAsync`，不是 `AccountExecutionBehavior` 的多账号去重与 finally 释放；10 个 `IMultiAccountCommand` 在 T-005 前都走这条路径。
3. **【中低】** `ReplayStateSnapshotTests:192-199` 账号余额那一半的断言在改写后恒真。
4. **【低】** `CommandBus.SendAsync` 的执行器外分支跳过 `DispatchDepth` 检查：处理器内泄漏的续体会作为新根作业执行；`RunDetached` 内调用会死锁而不是报嵌套错误。
5. **【细节】** `TitleBookSlice.cs:225` 一行 LF→CRLF 的无关改动。

第 1 轮已核实无问题：翻转语义（F4、F6）；`UserExecutionGate.Entry.TryAcquire` 新分支的 `references--` 不会提前 retire 或泄漏；`SelectedSessionTransitionGate` 两个入口的失败清理；`Enter` 的释放配对与取消优先级；其余门挂点与 §2.1 一致；组门与调度并发槽的收窄已登记且反例有区分力；反例计数可从用例代码独立推出；数字与日志一致；A～E 各类冲突处置抽查成立。

主控方处置见 `tasks/TASK-17.md`「主控方处置（第 1 轮评审后）」。

## Round 2 — `approved`

五条逐条关闭：

1. `PartyTownProjectionDelivery.cs:66`、`:198` 改为 `GameplayGateAssertion.Enter`，释放在 `:146`、`:224`。评审者独立在 `Server/src` 全仓检索（`SemaphoreSlim` 声明、`.WaitAsync(`／`.Wait(`、`UserExecutionGate<` 派生、其它锁原语），结果与 `logs/task-17-boundary.txt` 一致，附录 F「删除」栏的门再无漏挂。两条新反例在变异 M9 下变红；PartyTown 全类 7 例过滤复跑通过。§14.1 后继已改指。
2. `MultiAccountExecutionTests` 恢复并改写为作业形态三条。按代码推演：去掉 `Distinct` 时用例 1、3 变红；去掉 finally 释放时三条都变红；只漏放部分租约时由用例 2 的作业 C 暴露。实测变异 M2 下用例 2 变红。
3. `ReplayStateSnapshotTests:206` 改为读前播种在账号 99、读中途用 `fixture.Sql` 改归账号 1；四条断言与基准逐字相同。实现若不共用同一快照，「等于 0」那条会红；`current == 7` 证明改归命中。
4. `CommandBus.SendAsync` 先检查 `DispatchDepth`，以 `ValueTask.FromException` 返回嵌套错误；`InlineAsync` 的深度进出由 try/finally 配对，协议 handler 顺序多次派发不受影响。F8、F9 在 M8 下变红；`CommandBusExecutorTests` 9 例复跑通过；全量中「Nested command dispatch」为 0。
5. 按主控方裁定的 `cr-at-eol` 口径，`diff --check` 无输出；逐字节比对确认只有新增行用 CRLF，未改动的行换行符不变。

数字核对：全量 8162 ＝ 通过 8140 ＋ 失败 2（`DimensionDungeonTests`）＋ 跳过 20；`violations.py` 38 条全部来自 4 个反例类且与预期相等；§14 删除 41、改写 29 逐条清点一致，总分布 139。

非阻断备注：`SendAsync` 改为非 async 后，`ThrowIfNull(command)` 同步抛出（基准在返回的任务里）。第 1 轮已存在，生产调用方都传非空，影响可忽略。

残余风险（不阻断）：「去掉 `Distinct`」「去掉 finally」两种变异的区分力为代码推演，未实跑；隔离复跑与探针沿用第 1 轮结果，第 2 轮改动不涉及探针挂点集合。
