# Spec-002 完整结果独立挑战

## 固定对象、依据与攻击范围

对象为 `task/refactor-session-20261021` 的产品检查点 `0c5acb958f673d3caf4352c3914960bed78a2c68`，B baseline 为 `51bd1e9cb7b4a6ace5b0b05b098e7e50adc1cdea`，A seal 为 `2079d98d179ffd423298f18dba358949cd7ae4f1`。完整 B 实现及其 A+B 集成是对象，范围不限于 TASK-66。三份未提交候选汇报 `verification/b-final-report.md`、`final-evidence-map.md`、`final-measurement.md` 只作证据导航。

依据为根 TaskContract r4、子 TaskContract r1、Spec B10 ready R1、根 `process-reset-2026-10-25.md` §8，以及当前 AGENTS、架构哲学与重构偏好。攻击边界涵盖 17 RQ／11 AC、40 VC、18 项交接与 F-128①／变长字段、六条 B 根条件和 A 的 SC-1、SC-3～SC-7 持续不变量。本记录是给下一位独立 reviewer 的挑战输入，不给批准或封存 verdict，也不声明没有遗漏。

实际定向检查了请求建立与关闭、Main／重放／footprint、显式单元与真实事务完成、独立清理错误、发件箱与精确关闭、事件 Reliable／LiveOnly、HTTP／GM 纯数据入口、typed 定时／日切／两塔／调度来源，以及选角回选与共享协议阶段。读取最终八个映射运行的原始 TRX，独立计得 9817＝9797 Passed＋2 个具名 Dimension Failed＋18 NotExecuted；逐字节比较当前 15 个 Golden 与 B baseline 相同。读取最终 runtime、source identity、原始运行映射及测量材料；未重跑 build、test 或测量。当前性能下降、默认日志无逐 CMD 样本、环境污染、TitleQuery 真实 ReadOnly 拒绝、F-131 精度、F-132 生产 Compose 禁用和 A 原 CH-1／CH-2 均已有具名披露，未把它们重新包装为新缺陷。

未建立需要阻断下一阶段的高风险挑战。保留以下一项低风险取消边界问题，供 reviewer 判定其是否已被 VC-B23 明确覆盖；不要求为了挑战数量补充条目。

## CH-1：翻牌 action 内的已请求停机取消可能仍被报告为失败 CMD

- Risk: low-risk
- Failure and impact: 自动翻牌已进入真实写单元、正在等待可取消的领域读取时，停机 `DrainAsync` 取消 pending token。action 抛出的 OCE 被工作单元记为 sticky failure；外层自动翻牌 handler 虽捕获这个已请求取消，最终请求检查仍会重抛。该 producer 向总线传的是 `CancellationToken.None`，而 pending token 仅在命令字段中，故 BodyOnly 的同 producer-token 取消观察无法签发，执行器将其记录为 Error。原 timer 在作业内捕获该取消后正常结束。这条路径主要造成健康停机的错误诊断噪声；没有据此推断资产重复支付、漏回滚、额外关闭或 drain 提前完成，故不构成高风险阻断。VC-B23 已授权 Timer 一般业务错误改为 whole 默认失败，需准确区分它是否也有意涵盖这里的已请求停机取消，避免把“已入队前取消已覆盖”理解成所有取消位置都保持原结果。
- Evidence: `Server/src/Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs:155-181` 把 pending token 放入 typed 命令，却在 `SendInternalAsync` 传 None；`GameCardCommandHandler.cs:41-55` 将 pending token 传给 Award 并捕获其 OCE；`Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs:375-390` 在真实 Abort 后保留请求失败；`Commands/CommandBus.cs:51-70` 与 `ProducerCancellationObservation.cs:21-33` 只认实际传入的 producer token。现有 `CardSettlementLifecycleTests.Task48_real_queued_timer_is_cancelled_in_fifo_order_and_drain_waits` 取消发生于 blocker 尚未释放、timer CMD 尚未执行时，且明确断言没有事务，不能回答 action 内位置。可复现探针：在实际自动翻牌 ordinary Write 的可取消领域读取处挂起，调用同 producer 的 Drain，放行取消后核实真实 rollback、claim、drain 和执行器日志；不以 scope-only OCE 代替 action 取消。
- Minimum correction: 若 reviewer 认定此路径属于 VC-B23 已批准的 Timer whole 默认失败，产品无需修改，只需在取消证据／最终限制中明确“排队前取消保持；action 内已请求停机取消可能记失败 CMD”，避免扩大已有测试的证明范围。若现有取消保持承诺要求该位置也无 Error，则仅修正这一 producer 的真实取消发生传递并补该聚焦反例；不得清 sticky、吞独立 cleanup／guard／不确定失败或扩大通用取消恢复权限。
