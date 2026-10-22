# T-004c 进入条件核对（TASK-16）

- 日期 2026-10-22。基准提交 `c3115f9a` 加 TASK-16 的协调器改动。
- 用途：T-004c 翻转 `SendAsync`（命令总线只在执行器上执行）、开启门断言与 RQ-061 判据之前，需要成立的事实。逐项写「事实 → 现状 → 证据 → 结论」。缺口不在 TASK-16 补，写明由谁、在 T-004c 的哪一步补。
- 证据底稿：`entry-reconciliation.md`（下称「对账」）、`logs/task-16-probe-summary.txt`、`logs/task-16-boundary.txt`、`logs/task-16-build-test.txt`。

## 1. 持门者全部迁入（Plan 过渡期规则 1）

| 附录 F「删除」栏的原语 | 生产中的获取点 | 获取时是否在执行器上 | 证据 | 结论 |
|---|---|---|---|---|
| `SelectedSessionTransitionGate`（含借用帧、`AcquireManyExclusiveAsync`） | 选角 handler、各投递、管线 | 是 | 探针 P3（挂 `AcquireManyExclusiveAsync`、`BorrowOrAcquireAsync` 与基类 `AcquireAsync`）在 3.1～3.9 为 0；执行器外的 **(A) 记录**只在 3.3（测试直接调观察者）与 3.10（测试直接调总线）。执行器外的 (B)／(C) 记录（测试直接取门、续体丢帧）另见第 4 项的表 | 具备（生产路径）；测试侧见第 4 项 |
| `OnlineUserExecutionGate`、`AccountExecutionGate`、`UserExecutionGate<T>` 的全部派生（`TownAreaProjectionGate`、`DungeonProjectionGate`、两塔的投影门与进入门） | 管线行为、各投递与 handler | 是 | P3 挂在基类 `UserExecutionGate.AcquireAsync`，按派生类名记录；同上 | 具备 |
| `TransactionalExecutionGate` | `TransactionalExecutionBehavior`、事件分发的数据库准入 | 是 | P3（`TransactionalExecutionGate.AcquireAsync`）同上 | 具备 |
| `AchievementTitleProjectionGate`、队伍按角色信号量与 `PartyDeliveryStream.Gate` | 称号簿与队伍投递 | 是 | P3 对应三个挂点同上 | 具备 |
| `EffectOperationLedger` 每连接 `SemaphoreSlim`（`EffectOperationLedger.cs:81`） | `DispatchAsync`，入站作业内 | 是 | P3 没有挂。静态：`DispatchAsync` 只由入站 CMD 内的效果结算调用（对账 §2.1 第 2 行）；探针 3.2 只剩测试 `:371` | 具备（静态） |
| Weakness `WeaknessProjectionDelivery.deliveryGate` | `SendCurrentAsync`：`WeaknessRecoveryService` 的 tick，`SelectCharacterHandler`、`FinishQuestHandler`、`DungeonLoadingProjectionDelivery`、`TownAreaProjectionDelivery` | 是 | P3 没有挂。静态：调用方都是 handler、投递或定时作业 | 具备（静态） |
| Joust `ticks`（`JoustMaintenanceService.cs:15`） | `TickAsync`，在 `timer:`／`startup:JoustMaintenanceService` 作业里 | 是 | P3 没有挂。静态与 TASK-11 E1 的 Joust 两行 | 具备（静态） |
| `DomainEventDispatcher.groupGates`（`DomainEventDispatcher.cs:46`） | 组循环 `RunGroupAsync` 调用的 `DrainOnceAsync(string)`：`:76` 取门后 `ReadDueAsync`、逐条入队事件作业并等待，`:94` 释放 | **否**：在执行器外持有，且持有期间等待事件作业 | 静态 | **缺口**：门断言若覆盖它，会在执行器外报错。T-004c 开门断言时要么把它排除在断言之外，要么先按附录 F 删除（与 T-005 的顺序须在 T-004c 计划中定）。作业从不获取它，所以今天不成环 |
| `ScheduledTaskHostedService` 并发槽（`:69`、`:139`）与 `Task.Run`（`:129`） | 计时循环启动的尝试在执行器外取槽，再等定时作业 | **否**：同上 | 静态 | **缺口**：同上，T-004c 排除或先删（附录 F 交 T-005） |
| `NotificationRetryQueue`、`NotificationRetryDrainService` | 排空服务的 tick 作业 | 是（TASK-11 E2d） | 探针 3.8 为 0 | 具备；整体随 T-006 删除 |
| 管线行为的取锁部分、`IMultiAccountCommand`、OnlineUser／Account 访问方式、「锁内复查、重试」循环与 `LeaseSet` | 经总线进入的命令 | 生产中是（总线只在作业里调用，对账 §1.3）；测试直接调用总线时不是 | 3.10 只剩测试直接调用 | 翻转后成立（见第 2 项） |

## 2. `SendAsync` 翻转的前提

- **事实**：生产代码中在执行器外调用 `ICommandBus.SendAsync` 的位置为 0。
- **现状与证据**：对账 §1.3。Networking、GameServer、Infrastructure、Persistence 命名 `ICommandBus` 的文件为 0；Protocol 191 行（handler 135、定时源 9、其他类 47，逐个追到入口）；Application、GmWeb、HttpApi 由 TASK-14 Checks 8 核对。
- **翻转后应当消失的量**（探针 3.10，(A) 记录／命中）：Application 3212／9303，Protocol 13／25，Host 233／664，Networking 55／211，Persistence 46／180。这些全部是测试直接调用总线。翻转后若某个项目仍有 3.10 记录，按对账 §1.2 的两种形状重新归因；不要用排除轮归因（TASK-14 第 2 轮非阻断 2）。
- **结论**：具备。翻转本身会让上述测试在执行器外调用总线时失败，处置按冲突用例清单与「测试直接调用总线」的规则（第 4 项）。

## 3. RQ-061 判据的挂点事实

1. **工作单元入口**：
   - `DomainTransactionScope.Begin`（非只读）；
   - 5 处裸 `new DomainTransaction()`：`CharacterStateAccess.cs:52`（`ReadAsync`，边界外只读）、`:94`（`ChangeAsync`，易失存储写）、`:162`（`AloneAsync`，拆除落位置）、`ItemContainers.cs:181`、`CreatureRuntimeDomainService.cs:274`；
   - 只挂 `Begin` 的判据看不见后四处写（TASK-09 Remaining 3）。
   - **只读一次性事务要与写单元区分**：探针 P1b-read 在执行器外仍有记录（Application (A) 3／(B) 143／(C) 3，Protocol (B) 1，Networking (B) 1），P1b-teardown、P1b-volatile、P1b-creature 全为 0（对账 §1.4）。判据若把 `ReadAsync` 的只读事务当写单元，会拒掉这些读（`task-07-probe.md` §4.1）。
   - 结论：事实具备；判据如何区分由 T-004c 设计。
2. **附录 F「改为执行器内访问」的类在执行器外仍有的读**：
   - 遥测路径的 `IsBlocked`／`IsSuperseded`（dispatcher 在入队前、执行器外读，TASK-09 裁定第 1 条；探针 3.1 Received 1／1 就是它）；
   - `TouchSocketTransportDrain` 读传输注册表（不属附录 F）。
   - 判据若挂在锁入口而不是写入口，会把这些读拒掉。结论：事实具备；挂点选择由 T-004c 定。
3. **启动期**：探针 3.9 中执行器外的工作单元入口为 0，P1 带启动帧的 (A) 记录为 0。三个写库的启动工作已是启动作业；仍在执行器外的建表与迁移在 Persistence 单例构造时直连 SQLite，不经工作单元入口（TASK-14 Remaining 6）。结论：**RQ-061 的启动期例外可以不设**——前提是 RQ-061 的判据只挂在工作单元入口、发件箱登记与附录 F 的写入口上；若 T-004c 把直连 SQLite 的建表也纳入判据，就需要保留这一例外。
4. **`CreatureRuntimeDomainService.cs:274` 的反例**：生产与测试中都不可达（对账 §1.5），T-004c 的判据要自己补一条反例（RQ-072），例如给它注入一个非 `DomainTransactionScope` 的访问器、在执行器外调用并断言被拒。**缺口，归 T-004c 判据一步。**

## 4. 测试侧

- **冲突用例**：T-004c 77 条：§12 的 76 条，加上 §13 按主控方裁定从 T-006 改归的 `CleanupNotificationDeliveryTests.Existing_owner_and_town_notices_finish_before_party_projection`。它在测试线程上直接调用 `CleanupNotificationDelivery.DeliverAsync`，经 `PartyNotificationDelivery` 在执行器外获取 `PartyDeliveryStream.Gate` 与 `PartyParticipantGate`，门断言开启时必然失败。在开门断言的同一提交里处置；处置方式由 T-004c 细化时定，倾向「经执行器驱动、断言不变」或按 K5 规则改写。x1 的过滤器与方法清单见 `logs/task-16-probe-summary.txt` 末尾（76 条）；x3 的见 `logs/task-16-probe-round2.txt`（77 条）。
- **直接调用观察者的非冲突用例**：`UserServiceTests`（`OnlineUserServiceTests.cs`）中不在冲突清单里的方法也在测试线程上直接调用 `OnlineUserConnectionObserver`（x1 中 3.3 仍有 42／54，对账 §1.4）。门断言开启后它们同样会在执行器外取门；**缺口**：T-004c 处置冲突用例时把这些方法一并检查，按拆分原则第 5 条判定是否冲突。
- **Host 中直接调总线的 10 个测试类**（TASK-14 Checks 9）：Host 3.10 233／664，翻转时处理。
- `task-07-probe.md` §5 中 `SelectDungeonHandler` 一条的归属已由 TASK-09 更正。

### 4.1 执行器外的 (B)／(C) 记录：五个项目，按去向（TASK-16 第 2 轮，审查 B1）

探针轮 p1。(B) 按最外层测试帧（所属类名以 `Tests` 结尾的那一帧）对到冲突清单今天的去向，脚本是 scratchpad `t16/btable.py` 与 `conflictmap.py`（`task-07-conflicts.md` §7～§13 的去向）。(C) 没有测试帧。数字写作「记录／命中」。全部明细在 `logs/task-16-probe-round2.txt`。

| 项目 | 挂点 | (B) T-004c | (B) T-006 | (B) T-008 | (B) 不在清单 | (B) 只有辅助帧 | (C) |
|---|---|---|---|---|---|---|---|
| Application | P1 开写事务 | 11／11 | — | — | — | — | — |
| Application | P1b-read 只读一次性事务 | 22／23 | — | — | 121／3472 | — | 3／5 |
| Application | P2 附录 F 类访问 | 180／229 | 130／189 | 2／2 | 8／52 | 2／4 | 1／1 |
| Application | P3 取门 | 109／1566 | — | — | 1／1 | — | — |
| Persistence | P1／P2／P3 | 1／1、5／5、1／1 | — | — | — | — | — |
| Protocol | P1b-read | — | — | — | 1／1 | — | — |
| Protocol | P2 | 25／37 | — | — | 4／4 | 5／7 | 7／14 |
| Protocol | P3 | 61／83 | — | — | — | — | 18／29 |
| Networking | P1b-read | — | — | — | 1／1 | — | — |
| Networking | P2 | — | — | — | 27／94 | — | — |
| Host | — | — | — | — | — | — | — |

各组的加法与原始计数一致，例如 Application P2 (B)：180 ＋ 130 ＋ 2 ＋ 8 ＋ 2 ＝ 322；Protocol P2 (B)：25 ＋ 4 ＋ 5 ＝ 34。

**取门（P3）**：
- 执行器外的取门记录，全部属于 T-004c 的冲突用例。其中包括按主控方裁定从 T-006 改归 T-004c 的 `CleanupNotificationDeliveryTests.Existing_owner_and_town_notices_finish_before_party_projection`（`task-07-conflicts.md` §13）。
  - Application：x1（排除 T-004c 的 76 条）中 P3 (B)／(C) 为 0。表中那 1 条「不在清单」，是冲突用例 `CommonExperienceEffectTests:281` 的替身 `CallbackCloser.RequestCloseExactAsync`，栈截在替身上。
  - Protocol：x3（排除 T-004c 的 76 条加上上面那条，共 77 条）中 P3 (B)／(C) 为 0。x2（排除整份冲突清单 98 项）同样为 0。所以审查指出的 18 条 (C) 取门记录，都来自 T-004c 冲突用例在 handler 之后的续体。
  - Persistence 的 1 条属于 T-004c。
  - Networking 与 Host 为 0。
- 检查其余 T-006（K5）用例：最外层测试是 T-006 冲突用例的取门记录（A／B／C 三类都查），只有上面那一条（`k5gates.py`）。**没有其他 T-006 用例需要改归**。
- 结论：门断言开启时，因执行器外取门而失败的测试，限于 T-004c 的 77 条冲突用例（76 ＋ 1）。

**附录 F 类访问（P2）与只读事务（P1b-read）中不属于 T-004c 的部分**，都是**读**。它们会不会受影响，取决于 RQ-061 判据是否连读也拦：
- **T-006 130 条**：通知投递与重试测试（K5，随 T-006 改写）在执行器外读在线身份。
- **轮询读**：Networking 的 27 条，是 `GameLoginIntegrationTests`、`IllusionTowerStageFrameIntegrationTests`、`CardRewardReconnectIntegrationTests`、`BossTowerFrameIntegrationTests` 中 `WaitUntilAsync`／`ReadFrameAsync` 的轮询读（`task-07-conflicts.md` §6 第 1 行）。Protocol 中卡牌测试的 `WaitUntilAsync` 1 条也属此类。
- **本单元新增 T2 的 1 条**：`A_layout_opened_after_the_drain_still_answers_but_starts_no_auto_flip` 在作业外 `world.Runs.Find(...)`，读 `DungeonRunDomainService`（审查非阻断 7）。
- **测试替身与辅助帧**：Application 中 `SelectCharacterLifecycleTests` 的 `RecordingSender`（5）、`ReplayStateSnapshotTests` 的 `DelayedInventory`（3）、Notifications 的 `RecordingSender`（2）；Protocol 中两塔并发测试的 `SendAsync` 替身（2）、`SessionPresenceDomainService` 辅助帧（5）。前两者在 x1 中消失；Protocol 的 4 条在 x3 中消失（只剩卡牌那两条读）。
- **P1b-read 不在清单的 121＋1＋1 条**：测试在用例之外的只读一次性事务，例如 `ShopFixture` 播种和 `AchievementTitleBookTests.WriteAsync`。
- **(C) Application 4 条**：`SelectDungeonHandler`、`IllusionTowerProjectionDelivery.LeaveAndReturnAsync` 的读续体，以及 `CreatureRuntimeDomainService.DiscardExact`。它们在 x1 中都消失，来自 T-004c 冲突用例。
- **T-008 2 条**：`EffectOperationReplayTests:269`。

**翻转前需关注**（T-004c 在开门断言、开 RQ-061 判据的那一步处理）：
1. T-004c 的冲突用例共 77 条，含 §13 改归的那一条。
2. 判据若连读也拦，上面这些读会被拒，需要先处置：轮询读、T2、测试替身，以及 T-006 那 130 条。若只拦写入口与取门，它们不受影响。
3. `UserServiceTests` 中直接调用观察者的非冲突方法（见上）。

结论：事实具备。**缺口**是上面第 2、3 两项的处置，由 T-004c 定。审查要的「18 条 (C) 归因」已由 x2、x3 补证，不再是缺口。

## 5. 分离点与结构判据

- 分离点表与证明：对账 §3。全部从作业分出的工作在 `SuppressFlow` 下启动；返回的任务没有被作业 await（依据是代码阅读）。
- 结构判据 `DetachmentSiteTests` 已就位：`RunDetached*` 只在 `NetworkPlugins.cs`（与定义），`SuppressFlow` 只在 5 个文件，配反例与变异 V2、V3。`RunDetached` 不会被新代码静默当作绕过门断言的出口。
- **判据的边界**（审查非阻断 2）：
  - 它只钉住 `SuppressFlow`／`RunDetached*` **出现在哪里**。
  - 作业里新增的、未抑制的 `_ = Task.Run(...)` 或 fire-and-forget 会继承作业令牌，这是过渡期规则 3 要防的，但判据不会变红。
  - `GameplayJob.RunDetached` 不带括号的方法组写法，它也认不出来。
  - 所以「全部从作业分出的工作都在 `SuppressFlow` 下启动」只对今天的代码成立（对账 §3）。今后新增的分离点仍要人工检索 `Task.Run|ContinueWith|_ = `，对照分离点表。
- 结论：具备（今天的代码）；今后新增的分离点靠人工检索。

## 6. 停机与排空

- 执行器停止前，`GameplayExecutorLifetime.StoppedAsync` 依次排空全部生产者（传输、卡牌协调器），再停执行器（TASK-10）。
- TASK-16 之后，卡牌协调器对排空之后到达的调度请求容错：不抛出、不启动计时器、记一条 Information（T1、T2，C7 末条改写，V1）。排空顺序因此与正确性无关，不需要钉住模块装配次序。
- 正式全量 `did not drain within` 0、`GameplayExecutorStoppedException` 0。
- 结论：具备。

## 7. 已知的过渡态

| 过渡态 | 现状 | 何时消失 | 证据 |
|---|---|---|---|
| VC-8 提前（直发 2 秒期限，不读包的客户端被断开） | 范围已覆盖入站、生命周期（清理通告）、定时（Fatigue、Premium、婚礼、比武、卡牌、生物投影）、事件（22 个 LiveOnly 投影）、GM（Cera 提醒） | T-006 | TASK-08 Remaining、TASK-10 Remaining 2、TASK-11 Remaining 3、TASK-12 Remaining 3、TASK-14 可见差异 2；**T-004c 完成核对**（TASK-06 §7 4f，缺口归 T-004c） |
| R-5（TouchSocket 在作业中内联关闭／接收回调） | TouchSocket 4.3.1 不内联（TASK-10 K 实测 55／55） | 升级 TouchSocket 时复核 | TASK-09 补充 N-4、TASK-10 Remaining 7 |
| 中间态的 SQLite 忙等（执行器上的作业与执行器外写者撞「先读后写」） | TASK-14 之后执行器外没有写者：探针 3.1～3.9 只剩允许的只读残留，P1b 写类全为 0 | 已消失 | 对账 §1.1、§1.4 |
| AC-003 第 3 条（持续灌包时待执行入站 CMD ≤ 1） | 没有单独测；结构上由收包回调 await 作业保证 | — | TASK-09 Remaining 5，**缺口，留 T-004c 对账** |
| 「测试直接调用总线」 | 3.10 分项目计数见第 2 项 | T-004c 翻转 | — |
