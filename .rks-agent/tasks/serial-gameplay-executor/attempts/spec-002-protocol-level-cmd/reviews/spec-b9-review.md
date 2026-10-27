---
kind: rks-review
verdict: approved
---

# TASK-57 — B9 Spec 独立审查

**结论：approved。两塔的具名分解可以在当前根／子契约与中心方案内进入 P11；无需修改根契约 r4 或子契约 r1。本结论只通过 B9 规格门，不批准尚未形成的 P11、产品变更或 Task 完成。**

输入：工作区 `refactor-session-20261021`，产品 HEAD `94746fe1db2f102df5bda5367528d003872c4d7c`；当前未提交的 `spec.md` B9 ready R1 及其 diff、根契约 r4、子契约 r1、`TASK-57.md`、根 `process-reset-2026-10-25.md` 与交接 §3。按 `reviewing-work` 的 Spec 标准独立审查；未生成挑战、未再委派。另核实际两塔 timer／departure 代码、`CommandBus`／`GameplayTimerWork`／`CommandWorkUnits.FailAsync`，旧正式测试及下述原始运行记录。

## 授权与结构判断

根 Goal 的“一次定时触发一个作业”确实不能被忽略，但也不能脱离根允许经审查 Spec 明列有意变化、SC-8B 的 B 变化例外及子 Goal 一起解释为“内部来源永远禁止生成子工作”。本方案每轮仍有唯一 Timer 根；扫描所得的离场项／run 产生显式继承链的内部 CMD，实际世界读取、领取、事务和投影全部在唯一执行器内完成，producer 只提交值并等待。一个入站协议仍是一条完整 CMD，一个 run 的多成员和事务仍在同一 CMD，无 CMD 嵌套、自我等待、插队、并行玩法执行或外部世界编排。改变的是两条内部来源的批次粒度，不是执行器、发件箱或协议 CMD 中心方案。

此处许可来自当前契约的上述变化出口、用户交给主控的执行中裁定权，以及本次独立审查确认的具名边界，**不来自 F-07 对两塔的自动授权**。VC-B38 已明列项间 FIFO 请求插入、分项交付、新离场延后、子链与日志变化；VC-B37／39 明列旧内部来源缺陷修复和默认 whole 失败保护差异。原真实逐项隔离、提交点、单项消息顺序、精确身份、hosted 整轮合并和停机排空仍是验收义务。子契约允许记录并处理 A 的回归；补足已声明 physical 的保护与保留 timer 纯取消口径属于该边界，不是重新设计 A，也不修改 A 的封存结论。因此当前不命中 process-reset §6 的根／子／中心变更用户门。

## 证据与规格充分性

- 独立读取 `task57-old-preparation-r4/Application` 与 `task57-old-preparation-r3/Protocol` 的 `run.json`／TRX：分别 289／43 通过，真实 child exit 均为 0。Spec 修订记录中的 r2 286 是较早运行，不是最终准备计数。两份 RED 记录分别有两项 `Assert.Null` 合法回城失败，以及一项已声明 physical 未封锁的 `Assert.True` 失败；不是编译失败。matrix-r1 的 20 项通过仅证明旧状态。
- 真实 Boss departure 测试通过实际 Start、领域事务与 DI scope，证明首 cohort 的 action SQL／native Commit／uncertain／scope 失败不阻止后 cohort。其 gold marker 是受控同事务注入；两塔 due 的 TEST-ONLY NonEffect 算法证据与实际生产来源缺陷分开陈述，B9 没有混用二者。整 tick 直接并成 sticky whole 会破坏这个已证隔离，不能用“保持一作业”反向批准该回归。
- B9 对扫描不 pop、精确队首领取、领取前拒绝／取消不丢、重叠轮次不重投、旧 run／版本复核和持续首项失败均有可失败的验收要求。取消只允许完整收口后仍单独成立的真实 body 取消；scope-only、独立同 cause 清理、guard／sticky、typed／aggregate 均不豁免。physical 要求覆盖累计实际声明且按引用隔离，不增加 capture、raw、Self 或恢复许可；Boss 与 Illusion 的差异明确。

无未解决的 Spec 阻断项。P11 须把上述要求落实为具体接口、同一状态所有者、失败／取消来源证明及正式旧新对照，尤其真实内部来源新绿、physical 机制新绿和多成员保护；本审查不预判实现可行性或测试结果。已停止在 Spec 门，等待主控消费及后续 Plan 审查授权；本轮只写本文件，未 build／test、未改产品／测试／Spec／Plan、未作 Git 写入。
