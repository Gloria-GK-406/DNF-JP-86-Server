---
kind: rks-review
target: spec-001
dimension: production-code
verdict: approved
---

# spec-001 封存前完整结果评审：生产代码正确性

- 评审对象：`git diff a02bce46 2bdb1afd -- Server/src`（313 个文件）。HEAD `9aeb638c` 与冻结点 `2bdb1afd` 在 `Server/src` 上无差异（`git diff --stat 2bdb1afd 9aeb638c -- Server/src` 为空）。
- 依据：`spec.md`（含 R3、R4）、子契约 `attempts/spec-001-executor-outbox-lock-removal/TaskContract.md`、`verification/a-final-report.md`、`verification/transition-mechanisms.md`、`verification/follow-ups.md`、`AGENTS.md`。
- 准则：reviewing-work 的 Code 准则，按「完整变更／累积工作」范围审查，重点找跨单元缺陷。只读审查，没有运行测试；下面的结论都来自读码，证据写到文件:行（行号以冻结点为准，路径相对 `Server/src/`）。
- 已知残余 F-126、F-127，以及 `follow-ups.md` 中已登记的条目，不重复计为发现。

## 结论

**approved**。在审查范围内，没有发现阻断封存的缺陷。执行器、发件箱与发送端、生命周期与停机、删锁之后的串行覆盖，这几块经跨单元核对后是一致的。下面 4 条发现都是低风险，可以移交子 Spec B 或后续处理，不阻断封存。

## 发现（按严重度排序，均不阻断封存）

### L1（低风险，影响面大）：多数定时后台服务不吸收作业失败，一次程序错误就会让整个宿主停下

- 位置：下面这些 `ExecuteAsync` 在 `GameplayTimerWork.TickAsync` 外只捕获 `OperationCanceledException`：
  - `Protocol/Game/Tournament/TournamentRewardSweepService.cs:27-31`
  - `Protocol/Game/Fatigue/FatigueProjection.cs:75-76`
  - `Protocol/Game/Weakness/WeaknessRecoveryService.cs:55-59`
  - `Protocol/Game/Creatures/CreatureRuntimeDelivery.cs:145-149`
  - `Protocol/Game/CharacterDeath/DungeonPartyWipeService.cs:58-62`
  - `Protocol/Game/MarriageProposal/MarriageWeddingSlice.cs:205-206`（连 OCE 也不捕获）
  - `Protocol/Game/RoomMechanisms/RoomMechanismProjection.cs:83-113`
  - `Protocol/Game/Altar/AltarScheduler.cs:43-60`
  - `Application/Authentication/LoginSessionRuntime.cs:38-50`
  - 6 个 `*QuotaDailyRefreshService`、`PremiumDailyRefreshService`、`UsageQuotaProjection.cs:128`、`ItemUsageProjection.cs:124`

  只有 `BossTowerProjectionDelivery.cs:882-905`、`IllusionTowerProjectionDelivery.cs:833-856`、`JoustMaintenanceService.cs:73-77` 按 `GameplayJob.EndedAJob` 吸收失败后继续运行。
- 失败情景：作业内出现黏滞的程序错误时（`GameplayJobToken.Fault`：自我等待、网络等待断言、发件箱交给发送端被拒），tick 体内逐项的 `catch (Exception)` 即使吞掉了异常，作业照样以失败结束（`GameplayExecutor.cs:288-292`）。失败经 `CoalescingGameplaySource.ReleaseAfterAsync` → `GameplayTimerWork.Run.WaitAsync` 抛回 `ExecuteAsync`，BackgroundService 随之失败。宿主没有设置 `BackgroundServiceExceptionBehavior`（全仓检索为 0），按 .NET 默认值 `StopHost`，整个服务器会停下。
- 与基准的差异：基准的 tick 体形状相同，逐项吞异常；A 新增了「黏滞」语义，这条路径才变得可达。触发前提是程序错误，所以风险低，但后果是全服停机，而且各服务的处理方式彼此不一致。
- 修法：按 BossTower 的写法统一，在 `TickAsync` 外加 `catch (Exception e) when (GameplayJob.EndedAJob(e))` 记 Warning 后继续；也可以在 `GameplayTimerWork.TickAsync` 里统一吸收作业失败（OCE 照旧上抛）。补一条反例用例：在 tick 内触发一次自我等待，断言服务继续运行。去向：子 Spec B 首个单元，或单独的修正子 Spec。

### L2（低风险）：有序组里有多个慢前序时，后继的等待会累加，与 RQ-033 的结论句不符

- 位置：`Networking/Outbound/OutboundSender.cs:168-189`（`PredecessorsDeliveredAsync` 从早到晚逐个等待未完成的前序，每个前序各给 `OrderedWaitLimit` 2 s；前序自己的投递另有 2 s `DeliveryTimeout`）。
- 失败情景：XO-1、XO-3、XO-4 中，k 个跟随者各自都慢时，请求者那一批要等约 k × 2 s 才交付（4 人队最坏约 6 s）。RQ-033 写的是「有顺序依赖的一组连接，最多被拖慢一个等待上限」，这句结论只在只有一个慢前序时成立。逐个限时是 TASK-05 评审有意的选择（`reviews/task-05-result-review.md:69`），代码注释也写明了，所以这是 Spec 的结论句与实现不一致，不是实现错误。现有测试都只覆盖单个慢前序。
- 修法（二选一）：在 B 或后续中修正 RQ-033 的这句结论（写成「每个未完成前序各一个等待上限」）；或者给整条前序链设一个总期限，超时后把链上所有未完成的条目一并判为 TimedOut。

### L3（低风险）：XO-1 的有序作用域范围与 XO-3、XO-4 不一致

- 位置：`Protocol/Game/DungeonEntry/DungeonEntryProjectionDelivery.cs:340` 用的是 `using var order = ordering?.Begin("XO-1 …")`，作用域一直延续到方法末尾，包住了 `:365-368` 的 `townAreaProjection.LeaveManyAsync`。对照的 `BossTowerProjectionDelivery.cs:167-172`、`IllusionTowerProjectionDelivery.cs:165-170` 用块作用域，在 `LeaveManyAsync` 之前就结束了。
- 影响：组队进本时，城镇离场的名册与队伍刷新（发给城镇里的无关旁观者）也加入了 XO-1 组，排在全部队员批次之后；有队员慢时，这些旁观者的条目会被多拖最多一个等待上限。这些条目都是 BestEffort，不会被跳过，也不会关连接，所以只是多出来的顺序约束，附录 D 并没有这项要求。
- 修法：改成块作用域，只包住队员与请求者的批次，与 XO-3、XO-4 一致。

### L4（信息，今天不可达，给 B 的前提提醒）：提交后的组装一旦失败，效果账本会被记成「已确认回滚」

- 位置：`Application/Transactions/DomainTransaction.cs:150-181`（参与者提交之后才组装；组装抛出的异常在后提交回调之后重新抛出）；`Application/Commands/Pipeline/UnitOfWorkBehavior.cs:63-70`（`CommitAsync` 抛出后进入 catch，`AbortAsync` 因为事务已落定而空转，接着调用 `operations.RollbackConfirmed`）。
- 情景：工作单元内登记的组装（`GameplayOutbox.ComposeAsync`）在提交后因为取消而抛出 OCE（逐收件人的失败在 `GameplayOutbox.cs:453-462` 会被吸收，只有取消会漏出），这时事务已经提交，主结算却被标为 `ConfirmedRollback`。客户端重发同一序号时会被当作可重试，可能重复生效。
- 为什么今天不可达：生产代码中 `GameplayOutbox.ComposeAsync` 没有调用方（检索确认）；`EventService.ProjectAsync` 禁止在未提交的可写事务里调用（`EventService.cs:69-70`），所以生产上的组装都在单元外、登记时完成；入站作业的取消令牌是 `CancellationToken.None`。
- 建议：B 若开始在工作单元内登记组装，先把「提交后失败」与「回滚」在 UnitOfWorkBehavior 和效果账本里分开（例如把组装异常包装成不可被当作回滚的类型，或者在调用 `RollbackConfirmed` 前检查 `IsSettled`）。今天不需要改。

## 已核实无问题的要点

1. **执行器核心**（`Application/Execution/GameplayExecutor.cs`、`Abstractions/Execution/GameplayJob.cs`）
   - 单消费者：消费循环在抑制执行上下文的 `Task.Run` 中启动，`await job.RunAsync()` 跑完一个作业才读下一个，满足 FIFO 与互斥。
   - 作业令牌：在 `Job<T>.RunAsync` 这个异步方法内进入与退出，作业结束即失效。泄漏的流由 `GameplayLeakAssertion` 在入队点与总线两处拒绝。
   - 黏滞程序错误：先于结束钩子判定，取消被改判为失败；结束钩子在令牌仍有效时运行，所以发件箱先交给发送端，调用方才看到结果。
   - 取消：排队中的作业被取消时 CAS 成 Cancelled，之后跳过；运行中的作业被取消按失败处理，并关闭来源连接。
   - 停机与释放：`StopAsync`、`DisposeAsync` 都先拒绝新作业；超时后弃置剩余作业，弃置路径与循环用 `abandoned` 互斥。
   - 日志的占位符与参数个数逐条核对一致；日志异常都被吞掉，不影响作业结果。
2. **调用链与深度**：深度检查先于自我等待检查；被拒的作业不标为作业失败，所以 `EndedAJob` 能把它与作业失败区分开。生命周期超限时改作新根再执行拆除，事件超限时由系统 CMD 落定，两条路径都由不继承链的新根完成，不会反复命中。`GameplayCloseCauses`、`GameplayJobSlot`、`JobFailures` 都用 `ConditionalWeakTable`；`DomainEventCallChains` 在落定或回滚时删除对应条目，只有结果不确定的提交会留下条目，代码注释里写明了这一点。没有发现无界增长。
3. **发件箱与工作单元**
   - 只有可写事务开单元；只读事务与读快照不开单元。
   - 提交路径的各种结局（参与者提交失败、准备失败、`RequireRollback`、不可失败的参与者提交失败、未提交就释放）都会把单元结束为 Committed、RolledBack 或 Uncertain；作业结束时仍未结束的单元一律丢弃。
   - 关连接请求不属于任何单元，也不进有序组，一律执行。失败作业的来源关闭排在保留条目之后。
   - 编码都在登记时、在作业内完成（`OutboxAdapters.cs`、`NotificationBus.cs:126-127`），发送端只处理字节。
   - 结论：没有找到「回滚但已发」或「提交但丢失」的生产路径，只有 L4 那条今天不可达的组装边角。
4. **发送端**（`Networking/Outbound/OutboundSender.cs`）
   - `Submit` 串行，准入循环按提交顺序 FIFO，每个连接各自 FIFO；有序等待只指向更早提交的条目，不会成环（按代码注释的推导复核成立）。
   - 同一连接上，关连接排在它之前的条目之后。连接是否被替换、是否仍在线，在交付前判定（RQ-035）。
   - 超时、超限、失败只关闭该连接，余下条目判为 Discarded。
   - Required 失败只中断所在的组；BestEffort 组里的条目全部降为 BestEffort（`GameplayOutbox.cs:252-254`）。
   - 没有生产实现的 `IOutboundDeliveryObserver`，所以执行器外没有修改玩法状态的观察者。
5. **等待关系与停机**
   - 执行器从不等待发送端或传输层：关闭由发送端的分离循环或 `FailConnection` 的分离任务执行，排空时的关闭也不等待。
   - 收包回调等待作业，作业不依赖该连接的发送队列，TouchSocket「关闭等收包回调」不会成环。
   - 停机顺序：先停 Kestrel 与 TCP，再按顺序运行各生产者的排空（传输排空会拆除仍登记的连接），最后停执行器。定时、事件与 HTTP 都以 `CancellationToken.None` 入队，已入队的作业照常执行。卡牌协调器在停止后拒绝新的计时，所以与传输排空谁先谁后无关。
6. **删锁之后的不变量**
   - 抽查了选角（`SelectCharacterHandler.cs`、准备阶段的 `displaced` 检查）、登录顶替（`LoginSessionRegistry.cs`：提交后回调在作业内断开在线身份并登记关闭）、断线清理（`OnlineUserConnectionObserver.cs`）、两塔协调（`BossTowerPartyCoordination.cs`：去掉重试循环后，判定与写入发生在同一作业内，中间不会插入其他作业）、队伍水位线（`PartyNotificationSlice.cs:648-694`）、效果账本（`EffectOperationLedger.cs`：用 RQ-005 取代了每连接的信号量）、卡牌计时（分离启动并抑制执行上下文，到期时入队）。原来门保护的交错，现在由「一个作业执行到完成才轮到下一个」覆盖。
   - 检索 `Task.Run`、`async void`、`ContinueWith`、`Timer`、`TaskCompletionSource`、`WaitAsync`：生产代码里只剩执行器与发送端自身的分离点、内容初始化、HTTP 读体，以及卡牌延时，都在附录 B 的范围内。没有找到附录 B 之外、在执行器外修改玩法状态的路径。
   - HTTP：超过 32 KiB 或不是 JSON 的请求体，各处理器在读体之前就拒绝，不会在执行器上读网络。
7. **跨单元一致性**
   - 来源连接：只有入站与 `lifecycle:connected` 带来源，两者都在作业开始时打开发件箱（T6 已登记）。
   - 日志级别：作业失败在执行器记 Error，生产者一侧按 `EndedAJob` 降级。各处写法一致，只有 L1 列出的服务不吸收失败。
   - 编码时机：全部在登记时。
   - 深度超限：生命周期与事件两条路径都用新根系统 CMD 落定，都通过 `GameplayDepthGuard` 关闭根连接。

## 去向汇总

| 发现 | 严重度 | 是否阻断封存 | 去向 |
|---|---|---|---|
| L1 定时服务不吸收作业失败 | 低（后果是全服停机） | 否 | 子 Spec B 首个单元或修正子 Spec |
| L2 多个慢前序时等待累加 | 低 | 否 | B 或后续：修正 RQ-033 结论句，或加总期限 |
| L3 XO-1 作用域过宽 | 低 | 否 | B 或后续 |
| L4 提交后组装失败被记为回滚 | 信息（今天不可达） | 否 | 作为 B 的前提提醒 |

## Round 2：TASK-35 修正定点复核（2026-10-24）

```yaml
kind: rks-review
verdict: approved
```

- 独立评审：`a_review_code`；对象为 `9aeb638c` 加当前 TASK-35 修正，范围为 Round 1 的 L1～L4 与直接回归。
- 使用 `reviewing-work` Code 准则，检查实际生产 diff、测试源码、定向及变异日志；未重复运行测试。新鲜全量由封存核验承担。
- L1 已修正：`GameplayTimerWork.TickAsync:42-55` 统一吸收 `EndedAJob`，记不附异常的 Warning；拒绝与调用方取消仍上抛。20 个调用文件传 logger，登录清理也迁入；三处不可达 catch 删除，作业外异常分支保留。26 个定时源逐一故障注入、组合完备性、真实宿主与4例单测覆盖该边界。定向142/142，M1产生21红，M2～M4分别守登录源、完备性和日志。
- L2 已对齐：RQ-033、R4 与交审重点第23项写明对每个未完成前序各等一个上限。相对原结论句的放宽已披露，实现未改。
- L3 更正 Round 1：原 `using var` 位于 `try` 块内，`LeaveManyAsync` 本来就在其作用域之外。显式块仅澄清边界；B1原写法绿，M7把组移到try外红，与源码一致，不应声称修复既有顺序错误。
- L4 正确移交：F-132、过渡机制§5和最终汇报明确尚未修复、当前生产不可达，B若引入工作单元内组装必须先处理。相关事务/账本路径未改，未把移交当成关闭。

本轮范围没有未解决的阻断缺陷。
