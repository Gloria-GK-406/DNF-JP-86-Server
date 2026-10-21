---
kind: rks-review
target: tasks/TASK-07.md
verdict: approved
---

# TASK-07 结果快速审查（Independent Quick Review）

- 审查对象：`tasks/TASK-07.md` 契约第 2 版（含 §6 修订）、Detailed Plan 与 Result（唯一一轮执行，探针 r0～r7、x1～x3）。
- 改动身份：HEAD `c19b3b7b`，工作区未提交改动，即 `git diff HEAD` 的 212 个测试 `.cs`（Application 202、Persistence 1、Protocol 6、Host 2、Networking 1）。另有未跟踪的 `verification/task-07-conflicts.md`、`task-07-diff-check.md`、`task-07-probe.md` 和 `verification/logs/task-07-*`（7 个）。
- 适用准则：reviewing-work 的 Task Unit 快速审查，加上 Code 准则（只看局部的明显缺陷）。
- 依据：`tasks/TASK-06.md` 的 §2 规则、Result 与「主控方补充」；`reviews/task-06-result-review.md`（N-1～N-6）；`reviews/plan-review-r2.md` R-2；`plan.md` 共享决定；`spec.md` 附录 A、F。
- 审查方式：读了契约、三份验证报告、fork 决定记录、一次性脚本，抽读了源码 diff。用执行者的原始转储独立复跑了 R-2 比对、(B) 对照、行级补核和快照。另外写了几个一次性脚本（在 scratchpad，文件名以 `rev_` 开头）：扩展标记的区域扫描、并发标记扫描、冲突用例包裹核对。增量构建后定向跑了 5 个迁移类，166 条全部通过。没有改仓库文件，只写了本文件；没有 commit，没有 stash。

## 结论

`approved`。没有阻断项。

迁移只改了驱动方式，冲突总清单加法正确，探针 (B) 与清单逐条对得上。`Server/src` 当前与 HEAD 逐字节一致，执行者提出的四处偏离都可以接受。下面列出的非阻断项都是记载不准或残余风险，不影响本单元结论，也不影响下游怎么用这些产物。

## 重点逐项判定

### 1. 误包生产入口（R-2）

- **「172 个测试的 (A) 全部消失」的解释成立。**
  - 用执行者的 `adiff.py` 和 `lostcaller.py` 对 `r0/app.json` 与 `r7/Application.json` 复跑，结果相同：172 个，其中 166 个的调用方是生产代码，6 个是测试 lambda。
  - 我另写 `rev_callers.py`，给每个消失的测试列出「最外层入口帧往外三帧」，抽查了 10 个类、60 多个测试：
    - WeaknessGoldRecovery 11、SkillLearningCommand 6、TutorialCompletion 4、WarehouseMoveSynchronization 7；
    - IllusionTowerDomainService 10、MailboxProtocolIntegration 11、CeraCargoCapacityRefresh、PremiumServiceUsageReport、DungeonBuffUsage、MarriageEntity。
  - 结果：消失的入口一律是 `CommandBus.SendAsync`，只有 1 条是 dispatcher。它的直接调用方是 `XxxHandler.HandleMessageAsync`，再往外是 `ClientMessageHandler<T>.HandleAsync`，或者是测试直接调用的 `IllusionTowerProjectionDelivery.*`、`IllusionTowerPartyCoordinator.TryCoordinateAsync`。栈上没有 dispatcher、观察者或 worker 帧。
  - 所以这些都是「测试直接调用 handler 或投递、handler 内部走真实总线」。这是 TASK-06 已接受的 T5 判例（`LeaveDungeonTests`）。
  - 那 6 个测试调用方，我逐个比对了 r0 与 r7 的键：它们在 r7 仍有同一入口的 (A) 记录，只是 lambda 序号从 `b__0` 变成了 `b__1`～`b__5`。所以不是误包。
- **R-2 静态核对。** 执行者的 `regions.py` 不认 `x.SendAsync(` 这类泛化写法，所以我把标记扩到 `.SendAsync(`、`.HandleAsync(`、`EnqueueAsync(`、`PublishAsync(`、`ProcessAsync(`，对全部包裹区域重扫了一遍（`rev_regions.txt`，217 处命中），非 `HandleAsync` 的命中逐类核对：
  - `.SendAsync(`：31 处，都是邮件领域服务 `Mail(…)`、`f.Mail`、`mail` 的 `SendAsync`；
  - `EnqueueAsync(`：都是测试在事务里准备事件。`DomainEventDispatchTests.Fixture.SendAsync` 是「取门、开事务、入队、提交」，不是总线；
  - `RefreshAsync(`、`SweepAsync(`：都是 `IReplayStateProjectionDelivery`、`IllusionTowerProjectionDelivery` 的投递方法，按 T5 判例处理。真正的 worker（`IllusionTowerReturnSweepService`）不在区域里；
  - `ExecuteAsync(`、`StartAsync(`：都是领域服务同名方法（准入、婚房、塔）；
  - `Observer`：`PartyServiceTests` 的变量名。
  - 结论：没有包住真实的 dispatcher、总线、观察者、worker 或 HTTP。
- **fork 处理的文件，抽查 5 个**（OnlineUserServiceTests、SelectCharacterLifecycleTests、MailboxProtocolIntegrationTests、WarehouseMoveSynchronizationTests、DomainEventDispatchTests）：
  - 观察者的 `ConnectedAsync`、`DisconnectedAsync`，`DrainOnceAsync`，`f.Dispatcher.RunAsync` 都留在区域外。只读断言用同步 `Run` 包住，或者用提出返回值的形状。
  - `SelectCharacterLifecycleTests` 的 20 个整方法包裹，用的是记录型的假总线 `RecordingCommandBus`，没有真实总线。
- **`Support/` 适配器的处置理由成立。**
  - `LegacyTownAreaTestAdapter`（`SessionPresenceDomainService`）和 `UserDomainServices` 实现生产接口，并被注入被测对象。如果在内部包，生产代码（观察者、handler）经它们访问玩法状态时就会在作业内发生，探针记不到，入口漏迁也就看不出来。这正是 R-2 要防的情况，所以在调用点包是对的。
  - `PartyServiceTestAdapter` 只被 3 个 Application 测试文件直接使用，Protocol 没有链接它。
- **自动整方法包裹 867 条。** 对全部包裹区域扫并发标记（`Task.WhenAll`、`Task.Run`、`Parallel`、`Barrier`、`Thread`），只命中 `DungeonParticipationDomainServiceTests.Prepared_claim…` 一处。它是在同一个作业里手工交错两个事务，不是两个发起方，Remaining 已经交给 T-004c 复核。说明 §2.3「整段 `WhenAll` 不得包进一个作业」没有被违反。

### 2. 区域规则与漏包

- **(B) 对照**：用执行者的 `conflicts.py check` 对 `Application-noread-plan.json` 复跑，结果是 `unlisted 4 of 82`，与 Result 的「82 ＝ 78 ＋ 4」一致。
- **排除复跑的严谨性**：`x3.out` 记录 Application 在排除整份清单后跑了 4340 条，P1、P2、P3 的 (B) 都是 0，(C) 也是 0。Persistence 和 Host 同样为 0。Networking 只剩 11 个轮询用例，就是 §6 那 11 条。
  - 这一轮早于 r6、r7。r6、r7 之后只**增加**了包裹，没有撤掉任何包裹，所以「排除冲突用例后 (B) 为 0」这个结论对最终状态仍然成立。
  - 证明链是：排除前 (B) 只在 4 个替身或截断键上多出来，排除后全部消失，所以它们来自被排除的冲突用例。这条推理足够。
- **(C) 分诊**：抽查了表中的 P2、P3 栈顶（`CompleteAcquisitionAsync`、`AcquireGroupAsync`、`EnterParticipantsAsync`），都由 x 轮排除复跑支撑。其余 P1b-read 和 Networking 的 (C) 只由入口驱动，判断合理。
- **P1b-read 的 148 条 (B)**：是测试在用例边界外经 `StoreBackedItemContainer.ReadAsync`、`AmbientStoreAccess.ReadAsync` 读 SQLite，开一次性事务，读完回滚，不碰附录 F 的玩法状态（P2 挂点没有同栈命中）。
  - 按 TASK-06 §2.1 T1「只读不包」和附录 B，不该包。
  - 把它单列成交给 T-004c 的判据粒度信息，也是正确处置：判据不能挂在全部 `new DomainTransaction()` 上。

### 3. 冲突总清单

- **加法逐项复算一致**：
  - T-004b：TASK-07 18 ＝ 4 ＋ 3 ＋ 1 ＋ 7 ＋ 1 ＋ 1 ＋ 1；
  - T-004c：TASK-07 36 ＝ 22 ＋ 14；
  - T-006：TASK-07 31 ＝ 2 ＋ 16 ＋ 9 ＋ 4；
  - 合计 51 ＋ 85 ＝ 136，按执行单元 21 ＋ 62 ＋ 53 ＝ 136。
  - 抽数核对：K5 三个文件的方法数实际是 16、9、4；`SelectedSessionTransitionGateTests` 12 个方法，其中 2 个已包；`OnlineUserExecutionGateTests` 5 个方法，其中 1 个已包。都与清单一致。
- **冲突用例确实保持原样**：用 `rev_cwrap.py` 对清单里能定位的 85 个方法体检索 `TestGameplay`。只有 `UsageQuotaTests.Quota_refresh…` 命中，而清单里已写明「只涉及第 1 个事务块，第 2、3 个已包」。K5 整份文件里没有任何包裹。
- **新增 9 条都合理**（`:56` 成就门，加上探针补充的 8 条）。每条都写出了「测试在执行器外取门」「入口内回调取门」「入口与事务共用 AsyncLocal、切不开」或「入口加交错」中的一种具体机制，执行单元的归属与 TASK-06 §0 的规则一致。
- **`LoginSessionCommitRaceTests` 判为不冲突，包裹也不会出问题。**
  - 两条都是单流程、顺序调用，没有门、`Task.Run` 或 TCS，用的是 `RunAsync` 不带容器的形式。
  - 从 T-004b 起，它们仍然各自新建一个执行器，不和任何作业共享，所以不存在互等；里面也没有取门，T-004c 的门断言碰不到它们。
  - 唯一被模拟的「断连回调」是事务参与者在提交时同步调用的 `sessions.Disconnected`，在同一个作业里执行。

### 4. 机械核对

- **扫描距离 14→80**：两侧用同一个口径，放宽只会多认出方法，或者把行数变化造成的偏差变成「两侧不等」的假阳性，不会造成漏判。另外，全量运行各项目的计数与 TASK-06 逐项相等（7999），这本身就独立证明了没有删测试。
- **对齐轮换**：只对纯插入片段生效，而且要求轮换前后的工作区记号串逐字相同。它等价于「同一个结果文本，用另一个对齐位置描述插入」，被放行的插入本身仍然必须整体落在白名单内，所以不会放宽可接受的编辑集合。
  - 收紧的两条约束都已实现：`return` 后面必须紧跟 `} )`；`await` 只能出现在两个位置。另外 `git diff -w` 行级补核也在。
- **4 个白名单外片段**：
  - `WarehouseMove…Send` 删掉 `return`、改成 `async`：`Clear()` 仍在第一个 `await` 之前同步执行；调用方要么是 `await Send(…)`，要么是 `Assert.ThrowsAsync<SqliteException>(() => Send(…).AsTask())`。原来同步抛出的异常，现在改为经任务抛出，而 `ThrowsAsync` 两种都能接住，语义不变。定向运行通过。
  - `PartySettings` 的 2 处是 `Task.Run(() => TestGameplay.Run(() => …))`，即对每个发起方单独包一层，符合 §2.3。
  - `ACharacterInNoRunHasNothingToSettle` 由表达式体改成块体，断言行原样保留。
- **行级补核复跑**：`lines7.py 0223e646` 的输出与报告一致：3656 个 hunk，3646 个通过，10 个需人工复核。10 个都看过，都是「`return await` ＋ 包裹头」、上面两条例外，或者 TASK-06 已经审过的提出返回值形状。

### 5. 探针还原证明

- 我用 `snapshot.py` 现场重新取了一次快照，sha1 是 `5dbaa2e5…`，与改动前的快照 `cmp` 逐字节相等。
  - 快照里既有 `git ls-files -s` 的索引 blob，也有工作区逐文件按 blob 格式算的 sha1，所以索引和工作区两侧都覆盖到了。
- `git diff --cached` 为空，所以索引等于 HEAD。另外：`git diff --stat -- Server/src` 为空，`git status --short -- Server/src` 为空，`grep -rn "TestGameplay\|GameplayProbe" Server/src` 退出码 1，`Server/` 下也没有任何探针残留。`git stash list` 为空。
- 正式全量日志开头记录了运行时 `Server/src` 两项都为空，然后才做 `--no-incremental` 构建。

### 6. (A) 清单能否作为 T-004b 的输入

- §3 按附录 A 的入口族分了 10 组，每组列出具体入口、各挂点的次数和首个触达的状态，可以直接拿来对账。
- TASK-03 的三处门外写在 §3.11 单独标出，写明了出现与否和触发路径。未出现的那一处（`CreatureRuntimeDomainService.cs:274`），明确要求 T-004b 自己补反例。
- 另外给 T-004b 交出了停机路径（协调器 `Dispose`）和总线入口说明，可用。

### 7. Result 数字与日志

- 全量：日志里八个项目的计数（50、1694、80、475、969/967/2、138/136/2、4428/4410/2/16、165）与 Result 一致，失败只有那两条 `DimensionDungeonTests`。隔离复跑的日志也一致。
- 包裹行：`git diff -U0 HEAD` 中 Application 含 `TestGameplay.Run` 的新增行 1263 条、202 个文件，其余项目 24 条，与 Result 和 §7 一致。
- **日志体积**：TASK-07 的 7 个日志共约 8.0 MB，最大的是 `task-07-build-test.txt` 3.76 MB，其次是 `task-07-probe-Application.txt` 3.15 MB。**没有超过 5 MB 的文件**，与已入库的 TASK-04～06 全量日志同一量级。

| 文件 | 大小 |
|---|---:|
| `task-07-build-test.txt` | 3,759,011 B |
| `task-07-probe-Application.txt` | 3,150,746 B |
| `task-07-probe-Networking.txt` | 405,815 B |
| `task-07-probe-Host.txt` | 342,736 B |
| `task-07-probe-Protocol.txt` | 152,210 B |
| `task-07-probe-Persistence.txt` | 17,445 B |
| `task-07-isolated.txt` | 1,147 B |

## 对四处自报偏离的评估

1. **探针提前当检索工具用（r0）**：接受。改动 `Server/src` 的范围没有超出授权；最终状态的还原已经独立复核。它用来解决静态检索看不见夹具间接路径的问题，效果有数据支撑：r0 的 (B) 覆盖 1923 个测试方法，静态候选只有 231 个文件。
2. **补充挂点 P1b**：接受。它是回答 Required check 5「TASK-03 三处是否出现」的必要条件；把读类单列、不计入「(B) 必须为 0」也是正确的口径。
3. **分类口径的补充**：接受。其中「栈上有入口帧即为 (A)」是对的：测试替身或测试写的命令处理器出现在入口内部时，迁移责任属于该入口。原文口径会把它们误判成 (B)。去掉泛型元数、把 `TransactionalExecutionGate` 排除在入口之外、补上后台服务名单，都有出处。
4. **机械核对脚本的两处改动**：接受，理由见上文第 4 节。

## 非阻断项

- **N-1　探针报告 §4 有一处归属写错了，结论不受影响。** 「另作归属」的 4 条里，「同文件的 `RecordingSender.SendAsync`」实际是 `Dfo.Server.Application.Tests.Notifications.RecordingSender.SendAsync`（K5 `NotificationDeliveryTests` 的投递续体），不是 `SelectCharacterLifecycleTests` 的 RecordingSender。证据：r7 的 (B) 记录栈是 `NotificationBus.AttemptAsync` ← `Notifications.RecordingSender+<SendAsync>d__15`。
  - 因为 x3 排除了整份清单（含 K5），这条记录照样被证明来自冲突用例，结论不变。建议提交前把这一句改正。
  - 顺带一提：`task-07-conflicts.md` §6 最后一行列的替身名单（`RecordingActive` 等）与探针 §4 的 4 个键也不完全一致，可以一并对齐。
- **N-2　R-2 的自动核对只能抓到「某个测试的 (A) 全部消失」。** 如果同一测试里同一种入口一处被包、另一处没包，`adiff` 看不出来；入口如果什么挂点都没碰到，探针也看不出来（§7 已自陈）。
  - 本审查用扩展标记的静态扫描补了一遍，没有发现误包，但它仍是按名字匹配的检索。
  - 真正的兜底是 T-004b 注册容器执行器之后的自我等待报错。可是 867 条自动整方法包裹，加上 fork 的整方法包裹，都不带容器，落在新建的执行器上，这道兜底对它们不生效（TASK-06 §1 第 3 种来源的代价）。建议 T-004b 知晓这一点。
- **N-3　Result「偏离」第 1 条写「每次还原都做了快照比对」，但实际只有 3 份快照**（改动前、中途、最终）。多轮探针之间，`Server/src` 是否每轮都还原过，只在中途和最终两次被证明。
  - Required check 6 证明的是最终状态，这一点已经完全满足。建议把措辞改成与证据相符。
- **N-4　运行态 `Begin(…)` 的对账偏弱。** Application 90 处与 Plan「约 97」之间差 7，只用「Plan 是约数」解释，没有追到具体口径。因为最终以探针为准、探针 (B) 已经闭合，这不影响结论。
- **N-5　仓库体积。** `verification/logs/` 已累计约 31 MB，本单元又加了约 8 MB。单个文件都没有超过 5 MB；继续入库与否由主控方决定。

## 残余风险（交给下游，本单元不承担）

- 注册容器执行器之后，使用 `ShopFixture`（内部用 `provider` 包裹）的冲突用例，可能从「失败」变成「挂起」。Remaining 只点名了 `ReplayStateSnapshotTests:206`。
  - 处置冲突用例时，T-004b 要先在同一提交里删除或改写，再注册执行器，避免一次全量卡死。
