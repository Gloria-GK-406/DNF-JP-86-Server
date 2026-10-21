# 冲突用例总清单（TASK-06 ＋ TASK-07，按执行单元）

- 日期 2026-10-22，基准提交 `0223e646`（TASK-06 开始之前）。行号是基准上的方法声明行。
- 规则与「改写」的含义沿用 `task-06-conflicts.md` 开头一节，这里不重抄：
  - 经入口驱动、靠交错制造时序的，归 T-004b；
  - 直接构造或取用门、在门上制造争用的，归 T-004c；
  - 投递期限、重试与「发送在执行之内」的语义，归 T-006。
- 下列用例在工作区都**保持原样，没有包进 `TestGameplay`**。本单元只做决定，不执行删除或改写。
- TASK-06 的条目只按用例名引用 `task-06-conflicts.md`，机制、处置与理由以那份为准，避免两处漂移。
- TASK-07 的条目来源有两类：逐个阅读 Application.Tests 计划初始清单（K1～K5）确认的；以及探针 (B) 报出、阅读后判为冲突的（标「探针补充」）。
- 机器可读的同一份清单：`/private/tmp/claude-501/-Users-puxianguo-project/19adbc1e-6ba1-4530-bca0-dcab5d727d8a/scratchpad/t07/conflicts.py`。探针 (B) 的逐条对照就是用它做的（`task-07-probe.md` §4）。

## 1. T-004b

### 1.1 TASK-06 条目（引用）

| 用例 | 见 `task-06-conflicts.md` |
|---|---|
| `AuthenticationConcurrencyTests.OverlappingPasswordReadsCannotLeaveTheSecondLoginWithAnUnwritableSqliteSnapshot` | K2 表第 1 行 |
| `DungeonCardPresentationCoordinatorTests.Simultaneous_party_auto_flips_retry_a_contended_column_and_award_each_member_once` | K3（Protocol）表 `:109` |
| `DungeonCardPresentationCoordinatorTests.Auto_flip_retries_the_same_personal_confirmation_after_a_transient_conflict` | K3（Protocol）表 `:235` |

### 1.2 TASK-07 条目

| 文件:方法（声明行号） | 冲突机制 | 处置决定 | 理由 |
|---|---|---|---|
| `Game/SelectCharacterLifecycleTests.cs:236` `DisconnectWinningTransitionGate_PreventsPreparationMutationAndPublication` | 测试先取过渡门，观察者断连与选角 handler 在门后排队，放门后断连胜出 | 改写为「断连 CMD 与选角 CMD」的顺序断言，或删除 | K3：经观察者入口，靠门上交错制造时序。测试本身也取门；若拖到 T-004c 门断言才失败，由 T-004c 执行 |
| `…:261` `CommandHoldingTransitionGate_BlocksDisconnectCleanupUntilRegisteredFlowCompletes` | 选角 handler 被发送替身的 TCS 卡住、持门，观察者断连在门后等待 | 同上 | K3 |
| `…:288` `ObserverQueuedBeforeRollbackClose_CompletesAfterGateRelease` | 同上，另加关连接回调等待断连任务 | 同上 | K3 |
| `…:365` `OrphanedDisconnectWaitingForLifecycleGateDoesNotCleanupAReplacementSession` | 测试取过渡门，观察者断连在门后排队，期间替换会话 | 同上 | K3；与 `:236` 一样，若拖到 T-004c 才失败由 T-004c 执行 |
| `OnlineUsers/OnlineUserServiceTests.cs:30` `DisconnectWaitingForLifecycleKeepsItsCapturedSessionAndDoesNotCleanReplacement` | 测试在执行器外取过渡门独占，观察者断连在门后挂起，挂起期间替换会话再放门 | 改写为断连 CMD 与替换 CMD 的顺序断言，或删除 | K4：经观察者入口，靠门制造交错（类名实际是 `UserServiceTests`） |
| `…:67` `TeardownWaitingForLifecycleDoesNotTakeOnlineOwnershipInReverse`（Theory） | 测试持过渡门 Shared，观察者断连与 `ReturnToSelectionHandler` 在门后挂起，测试再取 `OnlineUserExecutionGate` 验证锁序 | 删除或改写：串行执行器下没有反向锁序 | K4；验证的是门之间的锁序本身 |
| `…:519` `DisconnectObservedBeforeAuthentication_DoesNotRemoveNewerGeneration` | 测试持 `OnlineUserExecutionGate` 独占，观察者断连在门后挂起，期间认证推进代数 | 改写为顺序断言，或删除 | K4 |
| `Sessions/ReplayStateSnapshotTests.cs:206` `Inventory_and_account_balances_are_read_from_one_real_sqlite_snapshot` | 经总线的读命令被替身仓储 `DelayedInventory` 的 TCS 卡在读取中，期间测试写 SQL、经 `ShopFixture.SeedShared` 写共享材料，再放行 | 改写：快照一致性改用顺序断言，或删除 | K4：经总线入口、靠 TCS 交错。翻转后被卡住的命令作业与 `SeedShared` 的作业落在同一执行器会互等；若拖到 T-004c 翻转才失败，由 T-004c 执行 |
| `Sessions/EffectOperationReplayTests.cs:159` `An_inherited_closed_operation_cannot_downgrade_to_trusted_direct_bus` | 发送回调里 `Task.Run` 继承 dispatcher 流（效果操作帧），TCS 卡住，放行后经总线发命令 | 改写 | K4：经入口，靠流继承与 TCS 交错；请求级帧移进作业（3b）后前提不再成立 |
| `…:229` `Postdurable_operation_record_failure_preserves_typed_cause_and_exact_connection_fence` | 测试在执行器外用 `EffectOperationContext.Enter` 建立请求级帧，再经总线发命令 | 改写：帧与命令在同一个作业里建立 | K4：帧建立在作业外；3b 与翻转之后帧不跨作业边界 |
| `…:253` `Invalid_projection_freeze_aborts_real_SQL_before_durability_and_records_confirmed_rollback` | 同 `:229` | 同 `:229` | 同 `:229` |
| `…:269` `An_inherited_readonly_scope_does_not_clear_the_primary_execution_on_the_parent_flow` | 直接操作 `EffectOperationContext` 帧，子 `Task.Run` 继承流，TCS 交错父子两条流 | 改写或删除 | K4：测的是跨流的帧语义，靠交错制造时序 |
| `…:292` `Waiting_on_the_connection_gate_keeps_the_original_selected_session` | `ManualResetEventSlim` 卡住第一次 dispatch 的发送，第二次 dispatch 在每连接 `SemaphoreSlim`（附录 F 删除）上等待 | 删除或改写 | K4：经入口在每连接门上排队，由 RQ-005 替代 |
| `…:345` `Unknown_applied_effect_change_is_rejected_before_SQL_commit` | 同 `:229` | 同 `:229` | 同 `:229` |
| `…:371` `Refusal_only_unselected_operation_cannot_pay_after_a_selection_arrives` | 直接调用 `EffectOperationLedger.DispatchAsync`（入口），TCS 卡住，期间测试释放并重选会话 | 改写 | K4：经入口（账本重放），靠 TCS 交错 |
| `World/IllusionTowerPublicFlowTests.cs:149` `Sweep_captured_for_an_old_run_cannot_return_or_reward_its_replacement` | `Task.Run` 里直接调用 `IllusionTowerProjectionDelivery.SweepAsync`（生产中由离场扫描定时驱动），被装饰的 `Find` 用 `ManualResetEventSlim` 卡住；卡住期间测试经 handler 回城、重新进塔，再放行扫描 | 改写：离场扫描迁为定时 CMD 之后，改用顺序构造「旧 run 的到期上下文被新 run 顶替」的取证；或删除 | 探针补充：经入口（扫描）驱动、靠交错；作业内阻塞等待另一作业推进会自锁 |
| `UsageQuotas/UsageQuotaTests.cs:117` `Quota_refresh_is_postcommit_coalesced_and_a_failed_projection_never_rolls_back_business_counts`（只涉及第 1 个事务块） | 测试开写事务，在事务内调用 `DomainEventDispatcher.DrainOnceAsync` 并断言被拒；事务与入口共用 AsyncLocal，区域切不开 | 改写：入口迁入执行器后，把「事务内排空被拒」改成执行器语义下的断言 | 探针补充。同一方法的第 2、3 个事务块已包 |
| `Host.Tests/GmPlayerSamplingHttpTests.cs:14` `OnlineListUsesOneCapturedSetForTotalAndItemsEvenWhenDisconnectOccursDuringDatabaseRead` | GM 在线列表 HTTP 读取被 `BarrierPersistence` 卡在数据库读里；卡住期间测试 `Cleanup` 会话、`TryDisconnectConnection`，再放行 | 改写：GM 查询迁为 GM CMD 后，把「读取期间断连」改成两条 CMD 的顺序断言，或删除 | 探针补充（TASK-06 漏判）。经 GM 入口、靠门闩交错。GM 查询进入执行器后，测试在卡住期间提交的作业会排在被卡住的 CMD 之后，互等 |

T-004b 小计：TASK-06 3 条 ＋ TASK-07 18 条 ＝ 21 条。
TASK-07 的 18 条：SelectCharacterLifecycle 4 ＋ OnlineUserService 3 ＋ ReplayStateSnapshot 1 ＋ EffectOperationReplay 7 ＋ IllusionTowerPublicFlow 1 ＋ UsageQuota 1 ＋ GmPlayerSampling 1 ＝ 18。

## 2. T-004c

### 2.1 TASK-06 条目（引用）

| 用例 | 见 `task-06-conflicts.md` |
|---|---|
| `LoginSessionQueuedCommandTests.ReplacedLoginCannotWriteAfterWaitingForTheAccountGateEvenAfterDisconnectCallback` | K2 表第 2 行 |
| `AuthenticationConsumptionTests.ExactCredentialBindsAgainButOverlappingUncommittedBindsAreRejected` | K2 表第 3 行 |
| `PartyTownProjectionDeliveryTests` 的 `ReadRepairWaitsForTheMutationGateAndRequeriesCurrentPartyState`、`NewGenerationMutationCannotPassABlockedOldGenerationReadRepair` | K3（Protocol）表 `:134`、`:188` |
| `TownProjectionHandlerTests.AreaCrossingWaitsForTheSameAccountPartyTransitionGate` | K3（Protocol）表 `:164` |
| `DungeonLoadingProjectionDeliveryTests` 的两个 `Replacement_…` | K3（Protocol）表 `:78`、`:107` |
| `BossTowerProjectionConcurrencyTests.A_third_member_disconnect_during_capture_revalidates_and_completes` | K3（Protocol）表 `:292` |
| `IllusionTowerProjectionConcurrencyTests.A_third_member_disconnect_during_capture_revalidates_and_completes` | K3（Protocol）表 `:291` |
| `PartyCommandHandlerTests` 4 条 | K4（Protocol）表 `:313`、`:466`、`:507`、`:553` |
| `LeaveDungeonTests` 4 条 | K4（Protocol）表 `:147`、`:203`、`:661`、`:242` |
| `SelectDungeonHandlerTests.Entry_transaction_cannot_reach_the_visit_until_exit_projection_releases_its_lease` | K4（Protocol）表 `:54` |
| `PartySelectDungeonHandlerTests` 2 条 | K4（Protocol）表 `:66`、`:547` |
| `TitleBookProtocolTests` 3 条 | K4（Protocol）表 `:73`、`:88`、`:153` |
| `TitleBookMutationHandlerTests.Worn_requests_reply_with_the_native_equipment_coordinates_and_complete_ack_before_projection` | K4（Protocol）表 `:27` |
| `MarriageProposalCodecTests.Two_party_delivery_is_ordered_through_peer_notification_and_command_can_borrow_fences` | K4（Protocol）表 `:149` |
| `PartyNotificationTests.Revisions_of_one_party_generation_are_delivered_as_contiguous_phases` | K4（Protocol）表 `:791` |

加法：2 ＋ 2 ＋ 1 ＋ 2 ＋ 1 ＋ 1 ＋ 4 ＋ 4 ＋ 1 ＋ 2 ＋ 3 ＋ 1 ＋ 1 ＋ 1 ＝ 26，与 TASK-06 Result 的「交给 T-004c：26 个」相等。

### 2.2 TASK-07 条目

K1（测门本身）。共同机制：测试直接构造门或经总线管线取门，并在门上制造等待。共同处置：争用类删除，由 T-004c 门断言的反例代替（「在执行器外取门，或在执行器上取门时需要等待，都立即抛出」）；门本身在 T-005 删除。

| 文件:方法（声明行号） | 冲突机制 | 处置决定 | 理由 |
|---|---|---|---|
| `Commands/OnlineUserExecutionGateTests.cs:10` `SharedExecutionsOverlap_AndWaitingExclusiveBlocksLaterSharedExecution` | 两个 Shared 持有期间，Exclusive 与后到的 Shared 必须等待 | 删除 | 门上争用 |
| `…:39` `CancelledWaiterIsRemoved_AndDoesNotLeakExecutionRights` | Exclusive 持有期间 Shared 等待者被取消 | 删除 | 同上 |
| `…:69` `CancellationRacingWithGrant_DoesNotDeadlockOrLeakRights` | 250 轮「取消」与「授予」竞速 | 删除 | 同上 |
| `…:102` `IdleEntriesAreReclaimedWithoutSplittingConcurrentExclusiveExecution` | 16 个并发 worker 争用同一 Exclusive | 删除 | 同上 |
| `Commands/SelectedSessionTransitionGateTests.cs:13` `ExclusiveBorrowPreservesActualOwnerModeAcrossSharedChildrenAndParentDisposal` | 借用帧存续期间，隔离流上的 Shared 等待 | 删除 | 同上 |
| `…:42` `PendingCancelledSharedFrameDoesNotCountAsHeldForExclusiveBorrow` | 被取消的等待帧与后续借用 | 删除 | 同上 |
| `…:59` `OuterLeaseIsVisibleThroughActualCommandBusAndOutlivesParentWhileCommandBorrows`（Theory） | 先持门再经真实总线发命令借用，期间另一写者等待 | 删除，或在门删除前改为「作业内持门再发命令」 | 「先持门再发命令」是过渡期规则 1 点名的写法；翻转后命令进入执行器，而门在执行器外持有 |
| `…:103` `Cancelled_group_releases_earlier_accounts_and_invalidates_pending_frames` | `AcquireManyExclusiveAsync` 在第二个账号上等待后被取消 | 删除 | 门上争用 |
| `…:122` `ExplicitAcquireRetainsFifoInsteadOfSilentlyBorrowing` | Exclusive 与后到的 Shared 排队，验证 FIFO | 删除 | 同上 |
| `…:140` `ExclusiveOwnerCanBorrowAndUpcastAcquisitionStillTracksTheLease` | 借用存续期间写者等待 | 删除 | 同上 |
| `…:159` `BorrowDoesNotQueueBehindWriterButAnotherFlowStillDoes` | 写者与另一流的 Shared 排队，借用不排队 | 删除 | 同上 |
| `…:183` `DelayedInheritedClosedFrameCannotBorrowAnotherChildsReference`（Theory） | 延迟的继承流在另一子帧持有期间等待 | 删除 | 同上 |
| `…:208` `PendingCancelledFrameIsNotHeldAndQueuedAcquisitionPropagatesToItsCaller` | 隔离流持 Exclusive，本流的获取排队 | 删除 | 同上 |
| `…:249` `GrantCancelRaceAndExceptionUnwindingDoNotLeakRights` | 100 轮「取消」与「放门」竞速 | 删除 | 同上 |
| `Commands/TransactionalCommandSerializationTests.cs:9` `TransactionalCommands_DoNotRunTheirHandlersConcurrently` | 经总线，第二条事务命令在 `TransactionalExecutionGate` 后等待 | 删除，或改为「执行器串行」的断言 | 门删除后机制不复存在；翻转后串行由执行器保证 |
| `…:28` `DirectCommands_RemainConcurrent` | 断言两条 Direct 命令真并发（最大并发 2） | 删除 | 翻转后同一执行器上不可能并发，断言与新不变量相反 |
| `…:46` `CancelledWaiter_DoesNotEnterAndDoesNotKeepTheGate` | 第二条命令在门后等待时被取消 | 删除 | 门上争用 |
| `Commands/MultiAccountExecutionTests.cs:16` `All_accounts_are_locked_once_and_released_when_execution_fails` | 直接驱动 `AccountExecutionBehavior`，在执行体内另取同账号门并断言等待 | 删除 | 门上争用；管线行为的取锁部分按附录 F 删除 |
| `…:40` `Cancellation_while_waiting_for_second_account_releases_first_account` | 测试持账号 2，行为在账号 2 上等待后被取消 | 删除 | 同上 |
| `Commands/CommandBusTests.cs:127` `ExclusiveAccountLease_IsReleasedWhenHandlerThrows` | 经真实总线；验证账号租约在异常后释放 | 不包（入口用例）；保留到访问方式删除（T-005）时一并删除 | 不争用。T-004c 翻转后应仍然通过，届时复核；它守的租约在 T-005 删除 |
| `…:183` `SharedOnlineUserLease_IsReleasedWhenHandlerThrows` | 同上（在线用户租约） | 同上 | 同上 |
| `…:191` `ExclusiveOnlineUserLease_IsReleasedWhenHandlerIsCancelled` | 同上 | 同上 | 同上 |

K1 中**不争用、已包裹、保留到 T-005** 的 3 条（不计入冲突）：`OnlineUserExecutionGateTests.DifferentOnlineUsersDoNotBlockEachOther`，`SelectedSessionTransitionGateTests` 的 `HeldSharedExplicitlyRefusesExclusiveUpgradeWithoutLeakingOrChangingSharedBorrow` 与 `DifferentAccountAndGateDoNotBorrowAndCancelledBorrowCannotGainRights`。

其余（K3、K4 与探针补充）：

| 文件:方法（声明行号） | 冲突机制 | 处置决定 | 理由 |
|---|---|---|---|
| `Game/SelectCharacterLifecycleTests.cs:56` `Achievement_projection_waits_for_the_complete_entry_workflow` | 选角 handler 被发送替身的 TCS 卡住、持有 `AchievementTitleProjectionGate`；测试在执行器外另取同一门并断言未完成 | 改写为顺序断言，或随门删除 | 测试在门上制造争用（计划初始清单没有，阅读时新增） |
| `Sessions/ReplayStateSnapshotTests.cs:31` `Another_selected_settlement_cannot_commit_between_the_replay_read_and_its_batch_delivery` | 直接调用的 `IReplayStateProjectionDelivery.RefreshAsync` 被发送替身的 TCS 卡在批量发送（持过渡门），同时经真实总线发 `AddGoldProbe`，断言它在门后未完成 | 改写或删除 | K4：直接调用 Delivery，并与第二个发起方在门上争用 |
| `…:60` `Registered_delivery_reads_fresh_inventory_and_keeps_the_exact_connection_and_exclusive_lease_through_batch` | 投递被 TCS 卡住时，测试用 `SuppressFlow`＋`Task.Run` 另取过渡门独占并断言必须等待 | 改写或删除 | K4：测试在执行器外取门并制造争用 |
| `…:177` `Replacement_while_waiting_for_lifecycle_lease_rejects_the_original_read` | 测试在另一流程持过渡门独占，经总线的 `ReadReplayStateCommand` 在门后等待，期间替换会话 | 改写或删除 | K4 |
| `Inventory/OnlineExpiredItemCleanupTests.cs:113` `Queued_cleanup_revalidates_published_session_after_account_gate` | 测试持 `AccountExecutionGate` 独占，经总线的清理命令在门后排队，期间释放并重选会话 | 改写为执行器上的顺序 CMD，或随门删除 | K4：该文件中只有这一条争用 |
| `World/CommonExperienceEffectTests.cs:164` `Unknown_fence_precedes_transaction_gate_release_and_queued_effects_never_price` | 两条总线命令，第一条在 Prepare 里被 TCS 卡住，测试等第二条进入管线（Order 56 探针）后才放行 | 改写或删除 | K4：翻转后第二条排在执行器后，等不到探针 |
| `…:191` `Publication_fence_is_synchronous_even_when_discard_waits_for_lifecycle_exclusive` | 测试在 `Task.Run` 里持过渡门独占，`PublicationFailedAsync` 在门后挂起 | 改写或删除 | K4：测试取门制造争用 |
| `…:219` `Effect_uow_borrows_outer_exclusive_and_keeps_teardown_waiting_until_settlement` | 测试持门后经总线发命令（借用），另取门作拆除并断言挂起 | 改写或删除 | K4：先持门再发命令，加门上争用 |
| `…:281` `Logger_failure_does_not_replace_unknown_cause_and_exact_close_callback_waits_for_lease_release` | 注入的 `CallbackCloser` 在关连接回调里 `Task.Run` 取过渡门，等入口释放租约 | 改写或删除 | K4：替身在门上与入口争用（计划写「(3)」，实际 4 条） |
| `Dispatching/NetworkMessageDispatcherTests.cs:118` `Already_queued_request_without_login_authority_is_rejected_before_uow_after_fence` | 测试直接取 `TransactionalExecutionGate`，经真实 dispatcher 发出的事务命令在门后排队，排队期间测试封锁会话，再放门 | 改写为「先封锁再派发」的顺序断言，或随门删除 | 探针补充：测试在执行器外取门并在门上制造争用 |
| `World/DungeonObjectQueueTests.cs:86` `Leaving_the_room_before_prepare_rolls_back_the_reservation` | 测试持写事务认领对象，另一流程（`SuppressFlow`＋`Task.Run`）直接 `TryMove` 离开房间，再断言提交失败；第二个发起方写在断言行内 | 改写为顺序断言，或删除 | 探针补充：两个直接调用的发起方真并发交错；第二个发起方是断言行，不改断言就无法按 §2.3 单独包 |
| `Game/MoveItemSpaceHandlerTests.cs:310` `Two_moves_arriving_at_once_are_both_carried_out_and_neither_is_refused` | 两个 `Task.Run` 发起方各自直接 `TransactionalExecutionGate.AcquireAsync`，再经测试替身总线开事务，靠门上争用串行 | 改写为两条顺序 CMD 的断言，或随门删除 | 探针补充：测试直接取门并制造争用（它独用的辅助 `Dispatch` 同样不包） |
| `DomainEvents/DomainEventDispatchTests.cs:159` `Commit_under_database_admission_returns_and_the_worker_consumes_after_admission_is_released` | 测试持 `TransactionalExecutionGate` 租约并在其内提交，事件 worker（入口）必须在门后等待 | 改写或删除 | 探针补充：测试取门并与入口争用 |
| `Events/JoustIntegrationTests.cs:743` `Native_query_holds_selection_lease_until_exact_physical_batch_is_sent` | 发送回调（在 dispatcher 入口内）里 `Task.Run` 取过渡门，断言它在入口持租期间挂起 | 改写或删除 | 探针补充：测试取门并与入口持有的租约争用 |

T-004c 小计：TASK-06 26 条 ＋ TASK-07 36 条 ＝ 62 条。
TASK-07 的 36 条：K1 22（OnlineUserExecutionGate 4 ＋ SelectedSessionTransitionGate 10 ＋ TransactionalCommandSerialization 3 ＋ MultiAccountExecution 2 ＋ CommandBus 3）＋ 其余 14（SelectCharacterLifecycle 1 ＋ ReplayStateSnapshot 3 ＋ OnlineExpiredItemCleanup 1 ＋ CommonExperienceEffect 4 ＋ NetworkMessageDispatcher 1 ＋ DungeonObjectQueue 1 ＋ MoveItemSpaceHandler 1 ＋ DomainEventDispatch 1 ＋ Joust 1）＝ 36。

## 3. T-006

### 3.1 TASK-06 条目（引用）

`task-06-conflicts.md` 的 K5 表：`CleanupNotificationDeliveryTests` 6、`PreparedBatchTests` 5、`TouchSocketNetworkMessageSenderExplicitDeliveryTests` 3、`HandlerFailureConnectionIntegrationTests` 2、`ExactConnectionCloseTests` 2、`OutboundSendFailureTests` 4。加法 6 ＋ 5 ＋ 3 ＋ 2 ＋ 2 ＋ 4 ＝ 22。

### 3.2 TASK-07 条目

| 文件:方法（声明行号） | 冲突机制 | 处置决定 | 理由 |
|---|---|---|---|
| `Sessions/EffectOperationReplayTests.cs:176` `Later_readonly_transactions_do_not_replace_the_primary_footprint_or_commit_state` | 发送回调（dispatcher 入口内）同步嵌套 `ICommandBus.SendAsync(...).GetResult()` | 改写 | 依赖「发送发生在主执行之内」；发件箱化后回调在 CMD 之后 |
| `…:307` `Second_primary_command_cannot_pay_or_overwrite_the_first_committed_entry` | 同 `:176` | 改写 | 同 `:176` |
| `Notifications/NotificationDeliveryTests.cs` 全部 16 条 | 每收件人投递期限、逐收件人失败隔离、取消与超时的区分 | 改写为发件箱条目与发送端失败的断言 | K5：DR-14，期限与重试队列删除 |
| `Notifications/NotificationRetryTests.cs` 全部 9 条 | 重试队列：声明的重试上限、重试走当前连接、放弃时记日志 | 删除或改写 | K5：DR-13、DR-14，不再重试 |
| `Notifications/NotificationRetryDrainServiceTests.cs` 全部 4 条 | `NotificationRetryDrainService` 的排空循环 | 随排空服务删除（附录 F） | K5：入口用例，本身不经 T1～T5 |

`NotificationWorld` 夹具同时被 K5 两个文件和 `NotificationInWorldTests` 使用，所以它**没有在内部包**（内部包会让 K5 用例也进入作业）；`NotificationInWorldTests` 的 5 条在调用点整方法包裹。

T-006 小计：TASK-06 22 条 ＋ TASK-07 31 条（2 ＋ 16 ＋ 9 ＋ 4）＝ 53 条。

## 4. 总数

- TASK-06 条目：T-004b 3 ＋ T-004c 26 ＋ T-006 22 ＝ 51。
- TASK-07 条目：T-004b 18 ＋ T-004c 36 ＋ T-006 31 ＝ 85。
- 总表：51 ＋ 85 ＝ 136；按执行单元：T-004b 21 ＋ T-004c 62 ＋ T-006 53 ＝ 136。
- 口径：一个测试方法算一条（Theory 不按数据行拆）。

## 5. 计划初始清单（Application 部分）与阅读结果的出入

| 组 | 计划 | 实际 | 说明 |
|---|---|---|---|
| K1 `OnlineUserExecutionGateTests` | 5 | 4 | `DifferentOnlineUsersDoNotBlockEachOther` 不争用，已包，保留到 T-005 |
| K1 `SelectedSessionTransitionGateTests` | 12 | 10 | 文件里 12 个测试方法；`HeldShared…`、`DifferentAccount…` 不等待，已包，保留到 T-005 |
| K1 `TransactionalCommandSerializationTests` | 3 | 3 | |
| K1 `MultiAccountExecutionTests` | 2 | 2 | |
| K1 `CommandBusTests` 租约释放 | 3 | 3 | 经总线、不争用；归 T-004c 复核、T-005 删除 |
| K2 `LoginSessionCommitRaceTests` | 在列 | 0 | 两条都是单流程顺序调用，用持久参与者模拟「准备与提交之间断连」，不涉及门或并发。已整方法包 |
| K3 `SelectCharacterLifecycleTests` | 4 | 5 | 另加 `:56` `Achievement_projection_waits…`（在成就称号投影门上争用，归 T-004c） |
| K4 `OnlineUserServiceTests` | 3 | 3 | 同文件 `DisconnectWithoutSelectedSession_DoesNotWaitForAccountLifecycleGate` 在作业里取一把空闲的门、观察者调用在外，判为不冲突、已包 |
| K4 `EffectOperationReplayTests` | 在列 | 9 | T-004b 7、T-006 2；其余用例只经已包的夹具 |
| K4 `ReplayStateSnapshotTests` | 在列 | 4 | T-004c 3、T-004b 1 |
| K4 `OnlineExpiredItemCleanupTests` | 在列 | 1 | |
| K4 `CommonExperienceEffectTests` | 3 | 4 | 多出 `Logger_failure…`；`Different_result_commands…` 两个发起方都经总线，不冲突 |
| K5 三个通知文件 | 在列 | 29 | 16 ＋ 9 ＋ 4 |
| 探针补充 | — | 8 | Application：NetworkMessageDispatcher、IllusionTowerPublicFlow、DungeonObjectQueue、MoveItemSpaceHandler、DomainEventDispatch、Joust、UsageQuota 各 1；Host：GmPlayerSampling 1 |

## 6. 有意留在作业外的点（不是冲突，探针 (B) 中会出现，逐条对照用）

TASK-06 Remaining 第 3 条的各项按 TASK-07 §6.1 原样保留，加上本单元新识别的同类点：

| 点 | 出现在 | 为什么不包 | 交给 |
|---|---|---|---|
| 轮询入口进度的读取（`WaitUntilAsync` 的谓词、`while (!online.TryGetByConnection(…)) await Task.Delay(…)`） | Networking：`CardRewardReconnectIntegrationTests`、`IllusionTowerStageFrameIntegrationTests`、`GameLoginIntegrationTests`、`BossTowerFrameIntegrationTests` | 等的是入口推进。把整个轮询放进作业，T-004b 之后会等不到入口 | T-004c：判据若对玩法状态的**读**也报错，这些谓词要改为每次求值一个短作业 |
| 卡牌计时器的模拟释放：`ControlledDelay.ReleaseAsync` 触发的自动翻牌／布局回调 | Protocol：`DungeonCardPresentationCoordinatorTests` | 它就是附录 A 的「延时计时器」入口，由测试替身代为触发 | T-004b（迁卡牌计时器时；同 TASK-06 审查 N-3） |
| `DungeonCardPresentationCoordinatorTests` 的 `World` / `PartyWorld` 在方法末尾 `Dispose` → `Coordinator.Dispose()` | Protocol | 模拟宿主停机时容器释放协调器；属于停机路径 | T-004b（3i 停机顺序） |
| `GmPlayerSamplingHttpTests.SessionInvalidatedWhileReadingPositionIsExcludedBeforePagination` 的 `duringRead` 注入回调 | Host | 回调在 GM 入口内部执行（探针记为 (A)） | T-004b（GM 入口迁入后在同一作业内内联） |
| `SelectCharacterProductionStateTests` 只驱动 `SelectCharacterPreparationService` 的 6 个用例 | Protocol | 协作者全是替身；本轮探针在它们上没有 (B) 记录 | — |
| 测试替身在生产代码回调里读写玩法状态：`SelectCharacterLifecycleTests.RecordingSender`、`RecordingActive`，`ReplayStateSnapshotTests.DelayedInventory`，`CommonExperienceEffectTests.CallbackCloser` | Application | 替身被注入被测对象，由生产代码调用；内部包会把生产路径的访问藏进作业 | 归属见 `task-07-probe.md` §4：排除本清单的冲突用例后复跑，这些记录全部消失，所以它们都来自清单里的冲突用例 |

## 7. TASK-09 处置（2026-10-22，基准 `690b9926`）

只记 §1.2 中 `EffectOperationReplayTests` 7 条的去向，其余条目不变。本单元没有出现 TASK-09 §7 第 (ii)、(v) 类新冲突。

| 用例 | 本单元全量中 | 去向 |
|---|---|---|
| `:159` `An_inherited_closed_operation_cannot_downgrade_to_trusted_direct_bus` | 通过 | 顺延 T-004c |
| `:229` `Postdurable_operation_record_failure_preserves_typed_cause_and_exact_connection_fence` | 通过 | 顺延 T-004c |
| `:253` `Invalid_projection_freeze_aborts_real_SQL_before_durability_and_records_confirmed_rollback` | 通过 | 顺延 T-004c |
| `:269` `An_inherited_readonly_scope_does_not_clear_the_primary_execution_on_the_parent_flow` | 通过 | **改归 T-008**（TASK-09 主控方裁定第 3 条：测的是跨流的 AsyncLocal 帧语义，帧改为显式数据时一并改写或删除） |
| `:292` `Waiting_on_the_connection_gate_keeps_the_original_selected_session` | 失败（`TimeoutException`，5 s） | **已删除**，见 `tasks/TASK-09.md` Result |
| `:345` `Unknown_applied_effect_change_is_rejected_before_SQL_commit` | 通过 | 顺延 T-004c |
| `:371` `Refusal_only_unselected_operation_cannot_pay_after_a_selection_arrives` | 通过 | 顺延 T-004c |

按执行单元的新分布（总数 136 不变；删除的一条仍按条计入，便于对账）：

- T-004b：21 － 7 ＝ 14（TASK-10 7 ＋ TASK-11 3 ＋ TASK-12 1 ＋ TASK-13 2 ＋ 原已标「顺延 T-004c」的 `ReplayStateSnapshotTests:206` 1 ＝ 14）；
- T-004c：62 ＋ 5 ＝ 67；
- T-006：53；
- T-008：0 ＋ 1 ＝ 1；
- TASK-09 已删除：1。
- 加法：14 ＋ 67 ＋ 53 ＋ 1 ＋ 1 ＝ 136。
