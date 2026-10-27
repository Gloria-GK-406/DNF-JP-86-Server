---
kind: rks-review
target: .rks-agent/tasks/serial-gameplay-executor/plan.md
target_revision: worktree HEAD a02bce46（plan.md 当前文本：新增「判据与测试的关系」小节，并修正 R-a～R-e）
round: 2
spec_state: ready
contract_revision: 2
verdict: approved
reviewer: 独立子代理（全新上下文；未参与 Plan 编写、修正与挑战生成；只读；依据 reviewing-work Plan 参考、sdd-governed「Adversarial Review Transitions」）
---

# Plan 复审（第 2 轮，bounded）：serial-gameplay-executor

## 范围

- 这是该目标第一次 bounded 复审，对象是 `plan.md` 修正后的文本。
- 只核对三件事：
  - 上一轮唯一阻断项 B-1 是否消除；
  - 本次修改是否带来直接回归，或与 Plan 其余部分、Spec（RQ-061、RQ-003、RQ-036、附录 E/G）矛盾；
  - R-a～R-e 是否写清楚。
- 不重做全面审查。上一轮的覆盖映射、CH-1～CH-6 裁定与仓库约束判断不受本次修改影响，沿用 `reviews/plan-review.md`。
- 代码路径相对 `Server/src/`，测试路径相对 `Server/tests/`。

## 亲自核对的事实

1. **测试里的 `.Begin()` 属于哪一层 API。**
   - `Application/Transactions/DomainTransactionScope.cs:32-43` 的 `Begin(bool readOnly = false)` 是 `internal` 的 Application 工作单元入口，直接在调用流上设环境事务。
   - `Dfo.Server.Application.csproj:14-18` 用 InternalsVisibleTo 向 Application.Tests、Host.Tests、Protocol.Tests、Infrastructure.Tests、Persistence.Tests 开放它。
   - 生产代码中写事务的开启点有：`UnitOfWorkBehavior.cs:51`、`DomainEventDispatcher.cs:102/116/132`、`EpicBookDomainService.cs:71`、`ItemGrantDelivery.cs:21`、`MailboxDomainService.cs:173`，以及世界与生物运行时的若干服务。它们都经过同一个 `DomainTransactionScope.Begin`。
   - 结论：把判据挂在「Application 的工作单元入口」上，确实能覆盖 RQ-061 所说的「写 SQLite 的工作单元开启」。
2. **Persistence.Tests 是否经过这个入口：经过。**
   - `Dfo.Server.Persistence.Tests.csproj:19` 引用了 Application。
   - 非只读 `.Begin()` 共 60 处，分布在 27 个文件，例如 `Database/AfterCommitEffectsTests.cs:30`、`Mail/SqliteMailboxRepositoryTests.cs:24`、`Accounts/AuthenticationTicketTests.cs:41`。它们调用的都是 `DomainTransactionScope.Begin`，另有 43 个文件引用 `DomainTransactionScope`。
   - Plan 给出的约 694 处正好是 Application.Tests 604 + Persistence.Tests 60 + Protocol.Tests 30，所以这 60 处已经算进「改为经辅助运行」的范围。不经入口、只直接测存储的用例（如部分 Schema 用例）才不受影响。
   - 两条表述合起来自洽，只是括号里的例子容易让人误读，见残余 R-1。
3. **Persistence.Tests 能否用上测试辅助：能。** 它引用 Application，并有 InternalsVisibleTo，所以可以构造执行器并以作业身份运行测试体。`tests/Support` 已有经 `<Compile Include … Link>` 链接进多个测试项目的先例（`TestSessions.cs`、`PvfAttributes.cs`）。
4. **启动期的写工作单元。**
   - `GameServer/GameServerStartupService.cs:75` 在启动时调用 `IEpicBookDomainService.InitializeAsync`，后者在 `EpicBookDomainService.cs:71` 开写事务。
   - T-004 开启判据之后，这条路径不在执行器上就会抛出，见残余 R-3。
5. **依赖栏。** T-005 依赖「T-004、M-1」，T-007 依赖「T-004、T-006、M-2」，T-008 依赖「T-005、T-006、T-007、M-2」。

## B-1 是否消除：已消除

上一轮要求补的三点，逐条对照：

| 上一轮要求 | 修改后的 Plan | 判断 |
|---|---|---|
| 1. 判据按调用位置（作业令牌）判定，不按执行器是否启动放行；生产代码没有关闭开关 | 「判据与测试的关系」第 1 条原样写明 | ✓ 堵住了「执行器启动才检查」这条旁路 |
| 2. 测试统一采用一种方式，并说明 Persistence.Tests 是否适用 | 选了推荐方案：`tests/Support` 的辅助把测试体作为执行器作业运行；明确「不另设豁免作用域」。判据只挂在工作单元入口与附录 F 写入口上，不经这两类入口的纯存储用例不受影响 | ✓ 堵住了「全局测试开关」这条旁路。Persistence.Tests 的适用范围按事实 2 理解是对的，措辞见 R-1 |
| 3. 假总线、直接 new handler 的用例保留的前提是按第 2 点运行；工作量计入 T-004，必要时拆分，翻转仍在最后 | 「测试」一节改为「经上面的测试辅助以作业身份运行」；改写计入 T-004；拆成 T-004a（辅助与测试迁移）、T-004b（入口逐类迁入）、T-004c（语义翻转并开启判据） | ✓ 顺序成立：辅助只依赖 T-002 的执行器；判据和翻转在 T-004c 一起打开，所以「零违规」到那时才被要求 |

修改之后，T-004 的完成条件「全量测试通过，且门断言和 RQ-061 判据零违规」可以在不放宽判据的前提下达成。T-005「零违规才进入」的条件也重新有了意义。

## 直接回归与一致性

| 检查点 | 结论 |
|---|---|
| RQ-061 | 两类挂点与 RQ-061 的三项对得上：工作单元开启、附录 F 写入口在 T-004 打开；发件箱登记在 T-006 打开。没有收窄判据 |
| RQ-072 | 反例用例要在辅助之外运行，才能证明判据会拒绝。辅助本身不影响反例 |
| RQ-003 | 过渡内联规则不变。测试体作为作业运行时，内层 `SendAsync` 在执行器上内联，与生产一致。如果测试体里再经真实 dispatcher 入队并等待，会被当作自我等待，这正是 RQ-003 要的行为，不算回归。需要在 T-004a 里分清哪些用例不该包裹，见 R-2 |
| RQ-036 与附录 E | R-b 的修正是：超时作为发送异常抛给调用方，并关闭连接。这样附录 E 的今天语义（补偿、隔离、关连接）在过渡期不变，2 秒取自 RQ-036 的「沿用今天每收件人 2 秒」。没有矛盾 |
| 附录 G | 期限导致的断开就是 VC-8，已经在附录 G 里，所以把它提前到 T-004 合法。Verification 一节写的是「只有 T-006 中附录 G 允许的变化可以修改」，与这一点在措辞上不一致，见 R-4 |
| 与 Plan 其余部分 | 「执行器生命周期，不留旁路」「发件箱按作业归属」「过渡期规则 1～4」与新小节之间没有冲突。R-e「容器释放时停止消费循环」与「首次入队即启动」配套 |

## R-a～R-e 是否写清楚

| 项 | 修改后的位置 | 判断 |
|---|---|---|
| R-a 测量闸进依赖链 | 依赖栏：T-005 依赖 M-1，T-007、T-008 依赖 M-2 | ✓ 清楚 |
| R-b 规则 4 的语义例外 | 规则 4 写明「第 3 步唯一的语义例外」「以发送异常抛给调用方」「同时关闭该连接」 | ✓ 清楚。上一轮建议配的那条测试没有写出来，可以在 T-004 的 Task Unit 里补上，不阻断 |
| R-c 传输回调是流边界 | 规则 3 末尾：`NetworkPlugins` 的 Connected / Received / Closed 与关连接器回调，进入时清除作业令牌 | ✓ 意图清楚。它有一个副作用，见 R-5 |
| R-d T-004 覆盖 RQ-006 | 覆盖栏写的是「RQ-005～RQ-015（含 RQ-006 定时源合并的接线）」 | ✓ |
| R-e 容器释放停循环 | 「执行器的生命周期」第 2 条 | ✓ |

## 非阻断残余风险

- **R-1：Persistence.Tests 的例子容易被误读。**
  - 括号里写的是「例如 Persistence.Tests 中直接操作存储的用例」。实际上 Persistence.Tests 有 60 处、27 个文件经过 `DomainTransactionScope.Begin`，它们在判据范围内，已经计入 694 处。
  - 后果有限：生产代码没有开关，误读的结果只是 T-004c 的全量运行变红，不会变成旁路。
  - 建议 T-004a 的逐文件清单把 Persistence.Tests 的这 27 个文件明确列进去。
- **R-2：辅助用错地方，会把漏迁的入口藏起来。**
  - CH-2 要靠判据抓住漏迁的生产入口。如果某个以生产入口为测试对象的用例（后台服务的一次 tick、连接观察者、GM/HTTP 端点、事件分发器）在测试体里整段包进辅助，入口自己漏迁时这条用例照样绿。
  - 建议 T-004a 写明：辅助只用于测试体自己做的准备和直接调用；以入口为对象的用例要经入口本身驱动（真实 dispatcher、托管服务启动、Host.Tests 的真实进程），不加包裹。附录 A 的对账要的「测试或检索证据」也应当来自这类不包裹的用例。
  - 辅助必须真的把测试体交给一个执行器实例运行，不能只是铸一个作业令牌。否则它在效果上就是一个豁免作用域。
- **R-3：启动期例外与「没有关闭开关」的关系还没说定。**
  - T-004 就打开了写工作单元判据，可是 Plan 只在 T-009 写了「含启动期例外」。`GameServerStartupService.cs:75` 的 EpicBook 迁移会在 T-004 撞上判据。
  - 与「不留旁路」一致的做法，是把附录 B 所列的启动期工作作为执行器作业运行。执行器首次入队即启动，这条路走得通；Spec 的例外是「允许」，不是「必须」。
  - 如果改走「开始监听前判据不生效」这种按阶段的例外，就要写成一处显式、有反例的机制，不能变成「执行器是否启动」式的开关。建议 T-004c 的 Task Unit 二选一并写明。
- **R-4：Verification 一节的措辞没跟上。** 「表征测试…只有 T-006 中附录 G 允许的变化可以修改」没有把规则 4 提前到 T-004 的 VC-8 算进去。实际影响很可能为零，因为表征用例不涉及慢客户端。建议顺手改成「附录 G 允许的变化（T-004 的 VC-8、T-006 的其余各项）」。
- **R-5：清除令牌后，自我等待的报错会变成一次最长 2 秒的等待环。**
  - 场景：TouchSocket 在作业等待中的发送失败时，内联触发 Closed 回调，回调入队生命周期 CMD 并等待它完成。
  - 回调进入时清除了令牌，所以不再被判成自我等待，清理也不会丢。但作业此时在等这次发送，发送又在等回调，回调在等这个作业结束才能执行的 CMD，就形成 RQ-008 要排除的等待环。
  - 打破这个环的只有规则 4 的 2 秒期限。所以期限必须实现成「放弃这次等待并抛出」，例如 `WaitAsync` 式的实现；不能只把取消令牌传进 TouchSocket，然后等它自己响应。
  - T-006 把发送移出执行器之后，这个环就不存在了。触发前提（TouchSocket 是否内联回调）尚未证实，属于 T-004 的局部设计。

## 结论

`approved`。

- B-1 已消除：判据只按作业令牌判定，生产代码没有开关；测试统一经执行器作业辅助运行，不设豁免；改写计入 T-004，并按 a/b/c 拆分，翻转和判据放在最后。核对的事实（`.Begin()` 属于 Application 内部工作单元入口；Persistence.Tests 经过它，并已计入改写范围）支持这一修正可以落地。
- 本次修改没有带来阻断性回归，与 RQ-061、RQ-003、RQ-036、附录 E/G 不矛盾；R-a～R-e 都写清楚了。
- R-1～R-5 是非阻断残余，都可以在 T-004 各 Task Unit 的细化中消化。
- 本审查只放行 Plan 这一关，不代表整体完成。按 TaskContract，实施仍要等用户在 compact 之后明确开始；下一单元仍是 T-001。
