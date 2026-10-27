---
kind: rks-review
target: tasks/TASK-29.md
verdict: approved
---

# TASK-29 结果快速审查（T-007 调用链贯通与深度上限，独立审查者）

- 审查对象：`tasks/TASK-29.md` 第 1 轮执行结果（合同 Revision 1，主控方裁定 0～6）；HEAD `bc735cd0` 之上的未提交差异与未跟踪文件。评审者只读，本文件由主控方按评审回报落盘。
- 适用标准：reviewing-work 的 Task Unit 与 Code 准则。
- 评审者自跑：Application 过滤 `DomainEventDispatchTests | LifecycleCallChainTests | GameplayQueueLeakTests | GameplayCallDepthTests | ConnectionLifecycleDispatchTests`，58 条全部通过，测试二进制新于全部被改源码。`git diff --check` 被工作区守卫拦下，沿用日志 `logs/task-29-diff-check.txt` 的记录。

## 发现（均不阻断）

1. **残余风险**：生产 HTTP 登录路径的链继承没有直接用例。`LifecycleExecutorWireTests.cs:162` 的父链为 `bus:AuthenticateAndIssueTicketCommand`（测试经 `TestCredentials.LoginAsync` 在执行器外直调总线，`CommandBus.cs:34`）；生产路径为 `HttpApiApplicationExtensions.cs:180` → `HttpGameplay.cs:34`，根为 Http。继承机制与来源无关（`GameplayOutbox` 在 Flush 时记录 `GameplayJob.Current`），读码成立，没有掩盖缺陷，但这一条只有读码证据。
2. `DomainEventCallChains` 另有两类孤儿项未写进已知限制：LiveOnly 的 `TryBeginAsync` 返回 false 时直接返回不 `Forget`；`ReliableAsync`／`LiveOnlyAsync` 的 `TryBeginAsync` 返回 false 时同样。只在并发认领竞态下出现，每条记录最多一项，进程结束清空，不会无界增长。
3. Result「Changed」写「28 个用例方法」，Checks 与日志写 17 个：实为 `.cs` 17 ＋ `.Executor.cs` 11。
4. `DomainEventDispatchTests.cs` 中 `Services =services` 少一个空格。
5. `GameplayDepthGuard.CloseRootConnection` 在 `contexts` 为 null 时也记「is no longer registered」，只影响 Debug 日志措辞。

## 已核实无问题

| 项 | 结论 |
|---|---|
| 链继承 | 发布时按目标订阅记录 `GameplayJob.Current`；`EventBatch.RollbackAsync` 撤销（MT7）；消费取 `publisher.Child(Event)`；非终态失败重试用同一父链（E3）；落定后在四处遗忘；跨进程 delivery 为 StartupRecovery 新根。关闭原因在 Flush 时、交发送端前记录，作业失败补登记的来源关闭与拒收整箱的兜底关闭计入（裁定 6(a)）；断开取子链，原因只用一次（L4、MT8）；无因关闭仍为新根（D1、MT4） |
| 内存与生命周期 | 链存储上界为本进程待处理 delivery 行数，`MaximumAttempts = 8` 后转终态遗忘；LiveOnly 跳过、过期、去重都写完成并遗忘；关闭原因用 `ConditionalWeakTable` 按引用比较，同 ID 新连接拿不到旧原因（L3），被丢弃的关闭随对象回收 |
| 超限处理 | 执行器拒绝逻辑与 Error 日志未改（MT9）；事件超限以新根系统 CMD 落定、不执行订阅处理、再关根连接，D4 证明不空转；生命周期超限以新根重入队拆除（MT10、L5），不会形成循环，拆除不丢（RQ-012、AC-007） |
| 队列层泄漏流拒绝 | 判定在深度检查之前，只认带着已结束作业令牌的流；Networking 插件回调、卡牌计时器、发送端 `Task.Run`、维护循环、`await` 恢复都不受影响；正式全量 `queue-leaked-flow` 只有 Q1，`exe:` 为 0；Q2 对照覆盖 `RunDetached` 与 `SuppressFlow` |
| 4 处线级断言改写 | 原保护意图（在执行器上、来源 Lifecycle、类型、`Order` 晚于关闭它的 CMD、请求方不断开）都保留；新断言对继承与新根有区分力（RED 时全红、MT5 全红） |
| `ContentWarmupTests.W2` | `GameplayOutbox` 不是内容索引，改为 slot 标记去掉 `??=` 字段，没有改豁免表或判据 |
| RQ-053 (d) | 措辞与实现一致 |
| 附录 A | 对比基准 `b76504bb`，注册 52 个、发布点 71 对无增删，改动的 8 个文件只动构造参数；L ＝ 10、下限 20、常量 32 不变 |
| 数字 | 8262 ＝ 8247 ＋ 15；8242 ＋ 2 ＋ 18 ＝ 8262；取证 41 ＝ 2 ＋ 1 ＋ 1 ＋ 37，反例类与对照以外 0；MT1～MT11 都有指定用例变红；表征 10 过 0 跳过；边界只动 Application 与 `GameplayAssertions.cs` |

## 主控方处置

见 `tasks/TASK-29.md`「主控方处置（评审后，提交前）」：第 1、2、5 条补记到 `t007-closure.md`，HTTP 根继承一条移交 T-009；第 3、4 条就地改正，`DomainEventDispatchTests` 过滤运行 38 条通过。
