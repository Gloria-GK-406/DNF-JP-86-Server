---
kind: rks-review
verdict: approved
---

# TASK-57 — P11 方法审查

**结论：approved。P11 ready R1 的共享方法可承接已批准的 B9；未发现必须先返修的规划缺口。须由主控消费并完成约定的 metadata 检查点后实施，本结论不证明新实现正确或 Task 完成。**

目标为 `plan.md` 的 Planning Choice 首段与末节 P11 增量，保留 `mode: task-units`：跨簇机制归 Plan，具体搬迁与结果归 TASK-57。输入包括已批准 B9、`spec-b9-review.md`、TASK-57 的主控消费与详细计划、当前 Plan diff；临时 executor proposal 仅作说明，不覆盖正式 Plan。按 `reviewing-work` Plan 标准审查，沿用同一独立 reviewer，不重开 Spec 门、不生成挑战。实际产品 HEAD 仍为 `94746fe1db2f102df5bda5367528d003872c4d7c`，`Server/src` 无 diff。

## 决定性核对

1. **内部足迹许可有明确闭合边界。** 当前 `CommandWorkUnits.RunAsync` 和 `ExplicitDeclaredNonRequestFootprint` 的 ProtocolNonEffect 限制是实际待改接缝；P11 同时约束 runner 与能力构造，只增加真正 TrustedInternal 的 ordinary writable Write，不借基类引用接受任意请求。非空唯一 owners、完整 session／账号／角色、job／request／action／事务寿命及健康检查保留；Main、ReadOnly、WriteResult、Self 和效果请求均不能得到该许可。owners 与保护 participants 分开，Boss 只捕获原本段 reporter physical，Illusion 保空 selected，方案不会靠扩大保护集合来补权限。

2. **取消观察位置可落在现有真实边界。** 已读 `CommandBus.ExplicitAsync`、`CommandInvoker`、`CommandRequestContext`、ordinary runner、lease 和 `GameplayJob`。invoker 的 DI 构造已解析 handler，因此 resolve 完成之后的 body await 可与 DI 失败分开；runner 已有 Action 阶段、Abort 完成事实、GuardFailure 与最后 lease Dispose。P11 要求每个实际 runner 入口先记尝试，ordinary Action 候选在健康确认回滚和全部 unit／lease 收口后才合格；无 runner 的直接 body 是另一条窄来源，不能以外层 Invoke catch 替代。发生身份、独立同 cause 清理与不可逆失效要求，比现有 `Failed` 的引用包含判断更精确；实现必须增加这部分真实记录，不能直接把现有去重结果当作证明。完整 request drain、Close、scope Dispose、DemandHealthy、FailAsync 后才搬运，且先等 queue completion 再重抛，既不清 sticky，也不会把内部占位值交给调用者。已提交后的 typed 失败和其他路线原有取消行为不获豁免。

3. **physical 修复与现有保护接缝一致。** 当前 `FailAsync` 确实按 session 取首次声明；`SelectedSessionSafety` 的 physical fence 则按对象引用保存，关闭使用 `RequestCloseExactAsync`。P11 两遍枚举能避免 null 遮掉后来真实连接，并保留同值不同引用，首次 pair metadata 与实际声明顺序明确。仅删除被真实声明覆盖的 null 项，不增加在线查询或 capture；精确 session 的 Fence／DiscardExact 及所有已声明 physical 仍走原保护路径。跨 run 和替换连接的反例是必要实现检查，不是已通过的事实。

4. **来源分解与排空有可实现的归属。** 四类 Internal CMD、0／1／2 显式父链和 `ForCommand` label 对应当前 chain API；单项领取在执行器内按 opaque 队首身份完成，不需新增玩法锁。原队列、窄接口、sink 及 concrete source／hosted／drain 同 singleton；500ms 周期、整轮 busy 与手工不合并均明确。`GameplayExecutorLifetime.StoppedAsync` 当前确实先遍历 producer drains 再 stop executor，P11 在首次 await 前登记轮次并让 Drain 关闭新轮次、等待整轮，解决子 CMD 之间的停机空档，而非只等待当前作业。原 Boss dead-first／固定成员与 None 回城 token、Illusion 原 token／11000 库存／Eplp 差异由强例承接。

## 实施与验证边界

正式旧红恢复为新绿、真实来源与 TEST-ONLY 算法区分、受控 SQL marker、真实 scope／encoder、持续首项失败后项推进、队首领取与重叠轮次、完整取消反例、累计 physical 与替换隔离，均已纳入正式验证。P8／card-delay／raw／Main／WriteResult／Self 强例和旧 coordinator 的业务断言不得因删适配层而消失；最终主控独占旧源码回放与恢复，之后 fresh 非增量构建和八项目 full 的责任及证据口径明确。没有要求以预写代码或提前测试代替本级规划判断。

本轮仅写本文件，未 build／test，未改产品、测试、Plan、Spec，未作 Git 写入。停止于 Plan 门，等待主控消费；后续仍由同一 reviewer 审最终 Result。
