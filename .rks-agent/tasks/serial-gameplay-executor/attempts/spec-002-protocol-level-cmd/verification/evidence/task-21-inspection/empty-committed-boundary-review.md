# InventorySort empty-committed 拒绝 ACK：独立边界复核

2026-10-25。fresh独立审查者 `b_sort_empty_boundary` 在协作上下文只读完成，以下为其交付原文由主控原样落盘。审查者未参与本Task规划/生产或主控裁定，未写文件或运行native；这是已发生的具体边界解释，不是Task-plan gate、Spec/Task完成批准。

```yaml
kind: rks-review
verdict: approved
```

结论选 **①**：本例可按现有 `WriteAsync([], Ordinary, None)` 与默认 typed 收口继续，**无需新增第四 raw 许可或 B4/P5**。批准仅针对这个边界解释，不批准尚未实施的新 whole 或 Task 完成。

决定性依据：

- 子契约 `TaskContract.md:42`、根契约 `TaskContract.md:208–209` 要求保住协议、持久化、隔离和关闭行为；父 `spec.md:81` 的 RQ-023(c) 没有要求所有未具名路径的外层异常类型恒定。
- B3 `spec.md:71–81` 明确只有三个具名出口具有 raw 保留资格，其余提交后失败采用默认规则。本 NonEffect 无账本，不满足 `CommandRequestContext.cs:163–167` 的 raw 条件。P4 `plan.md:67–69` 与 Task21 `TASK-21.md:59、74` 已明确采用该默认终局。
- 实际代码确会产生预测中的包装：`CommandWorkUnits.cs:125–127`、`CommandRequestContext.cs:172–173` 记录空事务已提交；`CommandWorkUnits.cs:188–194` 包装后只围栏声明的参与者，空集合不会新增围栏。
- 游戏传输入口不按 IOException／typed 类别分流：`NetworkPlugins.cs:176–197` 只检查 `EndedAJob`；执行器在 `GameplayExecutor.cs:300–322` 对最终失败统一执行结束钩子并标记。因此这里不能仅因外层分类变化就推导出新的断线策略。

B3 `spec.md:69` 的宽泛“位置等价”应与后面的有限 raw 表及默认规则合读，不能据此另授所有旧 fine 外登记出口 raw 权利；也不能把 VC-B2 当成任意错误分类变化的通行证。

继续前应把本例的精确对照写入 Task 处置，迁后实际验证：

- 旧：raw 原 cause；新：`PostCommitPublicationFailure`，inner 保同一 cause；
- 原空事务仍 Committed，无跳过、改回滚或另拆单元；
- 全资产不变、失败 ACK 未登记、精确 source-close 恰一次、原空参与者范围无新增封锁；
- 最终错误仍被标记为作业失败，保持完整链和原关闭顺序；
- 成功通知的 Warning＋Failed 隔离、三个既有 raw 出口不变。

旧 before 断言与快照必须保留；新断言应明确验证这个内部外壳差异，不能写成“异常完全相同”。若迁后实际产生新的围栏、回包、关闭、持久化或错误消费差异，再回主控处理。

已独立核实：旧 `empty-committed-before-01` 实跑 raw／同 cause／Committed／nofence／source-close；`old-oracles-04` exit0；42 项 before manifest 全部 bytes/SHA 匹配，Task r1 身份匹配，产品仍为 HEAD。未写文件、修改源或运行 native。
