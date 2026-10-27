---
kind: rks-review
target: tasks/TASK-16.md
verdict: approved
---

> 第 1 轮结论为 `changes_required`（B1）；第 2 轮复核后更新为 `approved`，见文末 `## Round 2`。

# TASK-16 结果快速审查（独立审查者）

- 审查对象：`tasks/TASK-16.md`，Contract Revision 1；含「主控方裁定（执行前）」1～8、「主控方裁定（执行中，§4 停止之后）」(a)、「主控方裁定（Result 之后）」1～2；Result 为第 3 次正式全量之后的版本。
- 归属改动：基准 `c3115f9a` 之上的未提交改动（`git status`，2026-10-22 22:11）。生产代码只有 `DungeonCardPresentationCoordinator.cs`（＋40 −9）。测试：`DungeonCardPresentationCoordinatorTests.Timers.cs`、`LauncherHttpHostStartupTests.cs:34`，新增 `DetachmentSiteTests.cs`。文档：`spec.md` 附录 A／B、`task-07-conflicts.md` §12，新增 `entry-reconciliation.md`、`follow-ups.md`、`t004c-entry.md`、`probe-tools/{fam,residual,hostnotest}.py` 与 `logs/task-16-*`。
- 适用标准：reviewing-work 的 Task Unit 快速审查，加 Code 与 Deliverable 两类标准，范围限于本单元。
- 依据：`plan.md` 共享决定（过渡期规则 1～4、判据与测试的关系）；`spec.md` RQ-010／011／061、附录 A／B／F、AC-006；TASK-08～15 的 Result；`task-07-probe.md`；`task-06-conflicts.md`、`task-07-conflicts.md`。

## 我做过的独立核对

1. **定向测试**：Release 增量构建。
   - `Protocol.Tests --filter CardReward`：105／105 通过；
   - `Application.Tests --filter DetachmentSiteTests`：8／8 通过。
   - 两次运行都没有改动仓库文件。
2. **分离原语检索**：在 `Server/src` 检索 `SuppressFlow|RunDetached|Task.Run|Task.Factory|ContinueWith|ThreadPool|new Thread|UnsafeStart|UnsafeRegister|new Timer(|Parallel.|async void`，另查「`_ = ` 丢弃返回值」的写法。
   - 命中与 §3.2 的 10 行表一一对得上。
   - `OutboundTransport` 的 `PipeScheduler.ThreadPool` 未接线，不属于从作业分出的工作。
   - `PvfAvatarCompoundDefinitionSource:34`、`PvfDungeonDefinitionSource:103` 的丢弃，与表后说明一致。
3. **工作单元入口检索**：检索 `new DomainTransaction(` 与 `.Begin(`。
   - 裸 `new` 共 5 处，与 `t004c-entry.md` §3.1 一致。
   - `DomainTransactionScope.Begin` 的自建调用点有：EpicBook、ItemGrantDelivery、Mailbox、两塔、DungeonLifecycle、TransactionalDungeonVisit 与事件分发器三处。读过调用上下文，都在作业体或 CMD 内（事件分发器的三处在 `GameplayTimerWork.RunAsync` 的作业体里）。
4. **托管服务计数**：`Server/src` 中 `IHostedService` 的注册行独立数出 34 条。
   - 按「本行 22、第 4 行 1、第 8 行 1、启动期 4、不碰玩法 5、执行器自身 1」逐个归位，都对得上。
   - 附录 A 第 5 行的 `22 ＋ 1 ＋ 1 ＋ 4 ＋ 5 ＝ 33`，加 `GameplayExecutorLifetime` 得 34，成立。
5. **总线调用点**：抽查 Protocol 中「其他 47 行」。
   - 16 个类的行数相加为 47。
   - 两塔投影类中，`SweepAsync`、`DrainDeparturesAsync`、`ProjectCommittedDepartureAsync`、`ProjectCurrentHostAsync` 的生产调用方，只有同文件 tick 作业体和 `*PartyCoordinator`。后者由队伍与通关 handler 调用。
   - Application 中调用总线的 6 行（3 个每日补给、过期物品清理、称号过期、Joust）都在调度任务或 tick 作业体里。
   - 「生产代码中在执行器外调用 `SendAsync` 为 0」的结构论证，在抽查范围内成立。
6. **观察者调用方**：生产中只有 `NetworkConnectionLifecycleDispatcher`。x1 中 3.3 为 42／54，归因于 `UserServiceTests` 直接调用观察者，成立。
7. **数字核对**：
   - 正式全量 8 个项目的总数、通过、失败、跳过与 Result 逐项一致：8147 ＝ 8125 ＋ 2 ＋ 20，0 个警告，没有挂起转储。
   - `handler-count`：173／141／14／2／159／139。
   - 379 ＝ S1 337 ＋ S2 42，S2 中含 3.3 的 4 条。
   - 冲突加法 0 ＋ 76 ＋ 53 ＋ 1 ＋ 1 ＋ 8 ＝ 139。
8. **探针 (B)／(C) 记录**：逐项目读原始探针记录，按测试方法归并（这一项引出 B1）。

## 阻断项

### B1　`t004c-entry.md` 只写了 Application 的 (B)／(C) 事实，漏掉其余项目；其中有一条 T-006 冲突用例会被 T-004c 的门断言打红

**事实**：

1. 各项目在执行器外的 (B)／(C) 记录（P3 是取门，P2 是附录 F 类访问，P1 是开写事务）：
   - Protocol：P3 (B) 61、(C) 18；P2 (B) 34、(C) 7；
   - Networking：P2 (B) 27；
   - Persistence：P1 (B) 1、P2 (B) 5、P3 (B) 1。

   出处是 `logs/task-16-probe-summary.txt` 第 97～150 行，以及 `logs/task-16-probe-Protocol.txt` 第 4～7 行与第 663～812 行。

   `t004c-entry.md` §4 只给了 Application 的数（P1 (B) 11、P2 (B) 322、P3 (B) 110）。§1 写「执行器外记录只在 3.3 与 3.10」，那是只看 (A) 得出的；Protocol 中 18 条 (C) 取门记录没有归因，也没有提到。

2. Protocol 的 P3 (B) 中有 `CleanupNotificationDeliveryTests.Existing_owner_and_town_notices_finish_before_party_projection`（`task-16-probe-Protocol.txt:330`、`:367`）。
   - 它在测试线程上直接调用 `CleanupNotificationDelivery.DeliverAsync`，经 `PartyNotificationDelivery` 获取 `PartyDeliveryStream.Gate` 与 `PartyParticipantGate`，都在执行器外。
   - 这条用例在 `task-06-conflicts.md` K5 表中归 **T-006**（`task-07-conflicts.md` §3 的 53 条之一），不在 T-004c 的 76 条里：x1 过滤器中没有它，`grep CleanupNotification logs/task-16-probe-summary.txt` 为 0。
   - `t004c-entry.md` §1 明确把「`PartyDeliveryStream.Gate`、队伍按角色信号量」列为附录 F 要删的门，也就是门断言的覆盖对象。
   - 因此 T-004c 按过渡期规则 2 打开门断言时，这条 T-006 用例会因为在执行器外取门而失败。TASK-08～16 的文档都没有记下这个事实（只在 task-11、12、16 的探针原始记录里出现）。

3. Protocol 其余 (B) 记录逐个对过，都属于 T-004c 的 76 条（`LeaveDungeonTests` 4 条、`PartyCommandHandlerTests` 4 条、`TitleBook*` 4 条、两塔并发、`MarriageProposalCodecTests` 等）。
   - 18 条 (C) 的栈根在 `SelectedSessionTransitionGate.CompleteAcquisitionAsync`／`AcquireGroupAsync` 上恢复，执行帧是两塔与队伍协调器、`PartyTransitionExecution`、`WeddingProposalExecution`、`SetUserAreaHandler`、`EplpCommandHandler`。
   - 按形状看，最可能是冲突用例直接调用 handler 之后的续体。但本单元没有像 TASK-07 x2 那样跑 Protocol 排除轮，这个归因没有证据。

**后果**：Result review 的理由写的正是这种情形——进入条件少了一项事实，问题要到门断言开启后的全量才暴露，而且会被当成翻转本身的问题去查。B1 中第 2 点是确定会发生的失败，第 3 点是 18 条无人认领的执行器外取门记录。

**最小修正**（只改文档；如要补证据，可再跑一轮探针）：

- `t004c-entry.md` §4 补一张表：五个项目 P1／P2／P3 的 (B)／(C) 记录数，各自按测试方法对到冲突清单的去向（T-004c／T-006／T-008／不在清单）。
- §1 的「执行器外记录只在 3.3 与 3.10」限定为「(A) 记录」，并指向这张表。
- 明写 `CleanupNotificationDeliveryTests.Existing_owner_and_town_notices_finish_before_party_projection`（T-006）会撞上 T-004c 的门断言，并给出处置归属。可选：T-004c 处置时一并改写；或者门断言暂时排除测试直接构造的 `PartyProjectionStreamRegistry`；或者改记 T-004c。由主控方裁定。`follow-ups.md` 加一行。
- 18 条 Protocol (C) 有两种处理：跑一次 Protocol 排除轮（排除全部 T-004c 冲突后看 (B)／(C) 是否归零，同 TASK-07 x2），或者写成取证缺口、交给 T-004c 开门断言之前补证。

## 非阻断项

1. **`NextDueAsync` 未列入附录 B**：`DomainEventDispatcher.RunGroupAsync`（`:205`）在执行器外调用 `Persistence.NextDueAsync`，这是一次只读 SQLite 读取。附录 B「只读 SQLite」行补齐了 `ReadDueAsync`，没有带上它；`entry-reconciliation.md` §2.4 也没有。性质与 `ReadDueAsync` 相同，补一个词即可。
2. **`DetachmentSiteTests` 的覆盖边界**：判据只钉住 `SuppressFlow`／`RunDetached*` 出现在哪里。作业里新增的、未抑制的 `_ = Task.Run(...)`／fire-and-forget 会继承作业令牌，这正是过渡期规则 3 要防的情形，而判据不会变红；方法组写法（`GameplayJob.RunDetached` 不带括号）也认不出来。这与计划 §3.2 第 3 条的限定一致，不算缺陷。但 `t004c-entry.md` §5 写「全部从作业分出的工作在 `SuppressFlow` 下启动……具备」，应当注明：今后新增的分离点仍要靠人工检索，判据只管出现位置。
3. **行号与标签漂移**：
   - `ReplayStateSnapshotTests:206` 是旧行号。基准上第 206 行在 `Replacement_while_waiting_for_lifecycle_lease_rejects_the_original_read`（`:177` 那条，同样是 T-004c 冲突）里，`Inventory_and_account_balances_…` 实际从第 212 行开始。§12 用方法名记录，不影响去向；建议注明「行号沿用 TASK-07」。
   - `t004c-entry.md` §1 写「组循环 `DrainGroupAsync`」，实际方法是 `DrainOnceAsync(string)`，由 `RunGroupAsync` 调用；行号 `:46`／`:76`／`:94` 是对的。
   - 分离点表第 3 行的 `:18`、`:26` 是丢弃点，`SuppressFlow` 在 `:37`。
4. **证据日志不全**：`logs/task-16-boundary.txt` 的托管服务注册段只有 33 行，漏了 `ApplicationModule.cs:382` 的 `JoustMaintenanceService`（该行逗号后没有空格，检索模式没匹配上）。「基准注册 34 个」这个结论本身成立（见上面第 4 项），但日志不能单独证明它。
5. **`follow-ups.md` 的数字不一致**：
   - §2.1 写「正式全量开始时 swap 已用 7576 MB」，那是 `-s1` 的数；正式日志 `task-16-build-test.txt` 开头是 8651 MB，Result 也写 8651 MB。
   - §2.1 中 `GmTestHost` 是 149 例，§2.2 第 1 条写 153 例。
6. **F-54**：只把就绪等待从 15 s 放宽到 60 s，`CancellationTokenSource` 由整个用例体共用，健康检查、404、端口可达等断言都没改。隔离 8 次都在 4～5 s，满载下 26 s。这说明满载时打包 exe 加预热的启动会超过 15 s，是负载与内存形状，不是功能缺陷。按用户偏好，耗时不作阻断。顺带一个信息：开预热的打包宿主，GM 健康检查要等预热完成才可达。
7. **本单元新增 T2 自身产生 P2 (B)**：`world.Runs.Find(...)` 在执行器外读 `DungeonRunDomainService`（`task-16-probe-Protocol.txt` 开头第 2 条 [B]），与同类的既有 `WaitUntilAsync` 一样。只要 RQ-061 判据只挂写入口就没有影响；若 T-004c 把附录 F 类的读也纳入，它会和 B1 那张表里的其他读一起受影响。

## 核对通过、无发现的部分

- **§4 排空容错**：
  - `TryReplace` 在 `gate` 内读 `stopped`，被拒时释放新建的源，不改 `pending`，不加在途计数。
  - 锁内检查之后才 `Stop` 的窗口里，新源已链接到被取消的 `stopping`；`RunTimerAsync` 在延时之后检查令牌，不会入队。在途计数加在 `StartDetached`，与 `DrainAsync` 的「先 `Stop` 再读计数」配合，不会漏等。
  - 两个调用点都在 null 时记 Information 后返回。`OpenLayoutAsync` 中此前的发帧与 `ScheduleClearReturn` 照常执行，计时器之后没有别的被跳过的代码。
  - T1、T2 以及 C7 改写后的断言（`pending` 不增长，理由成立，主控方已接受）都能观察到容错；V1 的记录是 3／19 变红；我在当前代码上复跑，全部通过。
- **`DetachmentSiteTests`**：白名单 2＋5 个文件与分离点表第 1～6 行一致；过期豁免检查有效。6 例反例中有两例是「放在另一种原语的白名单文件里」，加上注释用例，判定函数确实会拒绝。V2、V3 证明它扫到了真实源码树。
- **附录 A 修正**：173／141／14、`22＋1＋1＋4＋5`、`51 ＝ 40 ＋ 11`、第 7 行写明的理由都与代码或日志相符；没有改 RQ／AC。逐行对账表引用的测试类与方法，我抽查了 10 个，都存在。
- **附录 B 新增「生产者的入队侧」与补齐的四处**：都有安全理由。组门与调度并发槽这一例外写得准确，并已指向 `t004c-entry.md` 第 1 项。在我的检索范围内，没有发现执行器外的**写者**漏列。
- **3.10 的两种形状归因**：作为结构论证可以接受，前提是第 5 项的总线调用点普查。3.3 x1 的 `UserServiceTests` 归因成立，并已记为 F-18。
- **`follow-ups.md`**：抽查 TASK-04／09／11 Remaining 的来源条号，对得上，没有发现漏掉重大事项。
- **还原证明**：快照前后 sha1 相同，`snapcheck` 只有协调器一处不等，`revert` 恢复 25 个文件，`grep TestGameplay|GameplayProbe` 为 0。

## 交还

- B1 修正后需要重新审查，只看 `t004c-entry.md` §1、§4 与 `follow-ups.md` 的新增行，以及如果补跑了 Protocol 排除轮，它的记录。代码部分不用重新审查。
- 本审查没有改动任何生产或测试文件，没有 commit，没有 stash，也没有做变异实验。

## Round 2

```yaml
kind: rks-review
verdict: approved
```

- 复核对象：`tasks/TASK-16.md`「Result（第 2 轮）」。主控方裁定：`CleanupNotificationDeliveryTests.Existing_owner_and_town_notices_finish_before_party_projection` 从 T-006 改归 T-004c，在开门断言的同一提交里处置。
- 范围（按第 1 轮的交还建议）：
  - `t004c-entry.md` §1、§4、§4.1、§5；
  - `follow-ups.md` F-11、F-56、F-57 与 §2；
  - `task-07-conflicts.md` §12 注记、§13；
  - `logs/task-16-probe-round2.txt`；
  - `logs/task-16-boundary.txt` 末尾的补正；
  - `spec.md` 附录 B 与 `entry-reconciliation.md` 中 `NextDueAsync` 的补齐；
  - 7 条非阻断项的落实。
- 代码边界：`git diff --stat HEAD -- Server/` 仍是第 1 轮那 3 个文件（协调器 ＋40 −9 等）；`DetachmentSiteTests.cs` 未改；`grep TestGameplay|GameplayProbe Server/src` 为 0。本轮没有代码改动，代码部分的结论沿用第 1 轮。

### B1：已解决

1. **去向表**：`t004c-entry.md` §4.1 列出五个项目 P1／P1b-read／P2／P3 的 (B)／(C)，并按去向拆分。我与 `logs/task-16-probe-round2.txt` 第 5～39 行逐格比对，全部一致。与第 1 轮原始计数的对照也成立：
   - Protocol：P2 (B) 25 ＋ 4 ＋ 5 ＝ 34、(C) 7；P3 (B) 61、(C) 18；
   - Application：P2 (B) 180 ＋ 130 ＋ 2 ＋ 8 ＋ 2 ＝ 322；P3 (B) 109 ＋ 1 ＝ 110；
   - Networking：P2 (B) 27；Persistence：1／5／1。
2. **§1 措辞**：已限定为「执行器外的 (A) 记录」，并指向第 4 项；结论列改为「具备（生产路径）；测试侧见第 4 项」。
3. **Protocol 排除轮**（第 1 轮要求的补证）：
   - x3 只排除 T-004c 的 77 条。我核对了过滤器：恰好 77 个 `FullyQualifiedName!~` 项，含改归的那一条。结果是 P1／P3 的 (B)、P2／P3 的 (C) 全为 0，只剩 3 条读（卡牌 `WaitUntilAsync`、T2 的 `world.Runs.Find`、`ContainerInventoryProjectionTests` 的只读事务）。
   - x2 排除整份冲突清单，结果同样归零。
   - 由此，第 1 轮指出的 18 条 (C) 取门与 7 条 (C) 访问，都由 T-004c 冲突用例认领。两轮各自的快照前后 sha1 都是 `bf76d2e1…`；`snapcheck` 只有协调器不等；`revert` 恢复 25 个文件。
4. **T-006 用例全面复查**：`k5gates.py` 的结果是，在五个项目中，以 T-006 冲突用例为最外层测试的取门记录只有改归的那 2 条（同一用例）。Application P3 中「不在清单」的 1 条，是冲突用例 `CommonExperienceEffectTests` 的替身 `CallbackCloser`，x1 中 P3 (B) 为 0，与此相符。所以「门断言开启时，因执行器外取门而失败的只有 T-004c 那 77 条」这个结论成立。
5. **改归落实**：`task-07-conflicts.md` §13 的分布是 0 ＋ 77 ＋ 52 ＋ 1 ＋ 1 ＋ 8 ＝ 139，加法成立。F-11 改为 77 条；F-56 写明改归、原因、处置时点，以及「其余 T-006 用例没有」的结论；F-57 把「判据若连读也拦」时要先处置的读单列出来。三处互相一致。

### 非阻断项的落实

| # | 第 1 轮非阻断项 | 现状 |
|---|---|---|
| 1 | `NextDueAsync` | 已补：`spec.md` 附录 B「只读 SQLite」行、`entry-reconciliation.md` §2.3 与 §2.4 |
| 2 | `DetachmentSiteTests` 的覆盖边界 | 已写进 `t004c-entry.md` §5：只钉住出现位置，拦不住未抑制的 `Task.Run`／fire-and-forget，也认不出方法组写法；「具备」限定为今天的代码，今后新增分离点靠人工检索 |
| 3 | 行号与标签 | 已改：`:206` 注明「行号沿用 TASK-07，基准上从第 212 行开始」；组门改称 `RunGroupAsync` 调用的 `DrainOnceAsync(string)`；分离点表第 3 行注明 `SuppressFlow` 在 `:37` |
| 4 | 托管服务日志 | `task-16-boundary.txt` 末尾追加补正后的检索，34 行，含 `ApplicationModule.cs:382` |
| 5 | `follow-ups.md` 数字 | swap 改为正式全量 8651 → 9758 MB，另列 `-s1`、`-s2`；§2.2 第 1 条改为 149 例，与 §2.1 一致 |
| 6 | F-54 | 只是信息，无需动作 |
| 7 | T2 的执行器外读 | 已写进 §4.1「翻转前需关注」第 2 项与 F-57 |

### 本轮非阻断项

1. **磁盘上的 Release 二进制带着探针**：
   - **现象**：x3 构建时带着探针挂点（`Dfo.Server.Abstractions.dll` 的时间是 22:17:00），之后只还原了源码，没有重新构建。磁盘上的 Release 二进制因此仍是带探针的版本。
   - **影响**：源码已还原，增量构建会重新编译；但任何 `--no-build` 的测试运行（例如照搬 Required check 2 的全量命令）会跑到带探针的二进制。
   - **处理**：提交前或下一次跑测试前，先以 `--no-incremental` 重建一次。这不影响本单元的证据：正式全量是在第 1 轮重建之后跑的。
2. **`t004c-entry.md` §4.1 的读类缺口**：要等 T-004c 定下 RQ-061 判据是否连读也拦，现在已如实记为缺口（F-57），不属于本单元的缺陷。

### 结论

`approved`：在本单元范围内，没有未解决的阻断缺陷。这个结论不代替提交前的完成验证；见本轮非阻断第 1 条，提交前先重建。本审查没有改动生产或测试文件，没有 commit，没有 stash，也没有做变异实验。
