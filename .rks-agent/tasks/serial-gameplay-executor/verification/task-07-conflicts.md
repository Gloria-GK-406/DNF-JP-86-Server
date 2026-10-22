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

## 8. TASK-10 处置（2026-10-22，基准 `1eea890f`）

只记 §1.2 中分给 TASK-10 的 7 条的去向，其余条目不变。7 条都在测试线程上直接构造 `OnlineUserConnectionObserver`（或直接调用 handler），不经插件，本单元没有改变它们的执行位置；正式全量中全部通过，按拆分原则第 5 条顺延 T-004c。本单元没有出现 TASK-10 §9 第 (ii)、(v)、(vi) 类新冲突。

| 用例 | 本单元全量中 | 去向 | 承接保护意图的本单元用例 |
|---|---|---|---|
| `SelectCharacterLifecycleTests:236` `DisconnectWinningTransitionGate_PreventsPreparationMutationAndPublication` | 通过 | 顺延 T-004c | `LifecycleExecutorWireTests.A_disconnect_during_an_inbound_job_starts_only_after_that_job_completes`（W6） |
| `…:261` `CommandHoldingTransitionGate_BlocksDisconnectCleanupUntilRegisteredFlowCompletes` | 通过 | 顺延 T-004c | W6 |
| `…:288` `ObserverQueuedBeforeRollbackClose_CompletesAfterGateRelease` | 通过 | 顺延 T-004c | `LifecycleExecutorWireTests.A_cmd_closing_its_own_source_connection_does_not_wait_on_itself`（W7） |
| `…:365` `OrphanedDisconnectWaitingForLifecycleGateDoesNotCleanupAReplacementSession` | 通过 | 顺延 T-004c | `SerialDisconnectTests.The_old_connections_disconnect_after_a_replacement_session_neither_cleans_nor_deselects_it`（P1） |
| `OnlineUserServiceTests:30` `DisconnectWaitingForLifecycleKeepsItsCapturedSessionAndDoesNotCleanReplacement` | 通过 | 顺延 T-004c | P1 |
| `…:67` `TeardownWaitingForLifecycleDoesNotTakeOnlineOwnershipInReverse`（Theory，2 例） | 通过 | 顺延 T-004c（门删除时直接删除） | 无：测的是两把门之间的锁序 |
| `…:519` `DisconnectObservedBeforeAuthentication_DoesNotRemoveNewerGeneration` | 通过 | 顺延 T-004c | W6（同一连接上断开只排在最后一条入站之后，过期的观察不再出现）＋ `SerialDisconnectTests.The_old_connections_disconnect_after_a_newer_login_leaves_the_newer_identity_and_its_generation`（P2，跨连接不误删） |

按执行单元的新分布（总数 136 不变）：

- T-004b：14 － 7 ＝ 7（TASK-11 3 ＋ TASK-12 1 ＋ TASK-13 2 ＋ 原已标「顺延 T-004c」的 `ReplayStateSnapshotTests:206` 1 ＝ 7）；
- T-004c：67 ＋ 7 ＝ 74；
- T-006：53；
- T-008：1；
- TASK-09 已删除：1。
- 加法：7 ＋ 74 ＋ 53 ＋ 1 ＋ 1 ＝ 136。

## 9. TASK-11 处置（2026-10-22，基准 `ca71eb26`）

记 §1.2 中分给 TASK-11 的 3 条的去向，以及本单元新判定的 1 条（TASK-11 §8.4）。行号是 §1 的基准行号，以方法名为准。

| 用例 | 本单元的结果 | 去向 | 旧断言 → 新断言 → 保护意图 |
|---|---|---|---|
| `DungeonCardPresentationCoordinatorTests:109` `Simultaneous_party_auto_flips_retry_a_contended_column_and_award_each_member_once`（Protocol） | 预判失败：两名成员的自动翻牌成为两条先后执行的定时 CMD，第一条在 `FirstAttemptGate(2)` 上等第二条，而第二条排在它后面 | 改写，改名为 `Party_auto_flips_run_one_after_the_other_and_award_each_member_once` | 旧：两人同时争用同一列、重试后各得一张，索引互不相同 → 新：两条到期 CMD 先后执行，选择数为 2，两人各一次、索引互不相同，每人恰好一帧 `SelectCardResponse` → 意图：每名成员得到且只得到一张；「争用后重试同一个个人确认」由 `:235` 守 |
| `DungeonCardPresentationCoordinatorTests:235` `Auto_flip_retries_the_same_personal_confirmation_after_a_transient_conflict`（Protocol） | 预判失败：`ReleaseAsync(2s)` 返回时布局在另一个执行器上还没执行，同步断言 `Pending(4s) == 1` 不成立 | 改写等待方式，名字不变 | 旧：`ReleaseAsync(2s)` 后同步断言 `Assert.Equal(1, world.Delay.Pending(4s))`，`ReleaseAsync(4s)` 后同步断言 `Assert.Equal(1, world.Bus.Selections)` → 新：前者改为 `await WaitUntilAsync(() => world.Delay.Pending(4s) == 1)`；后者先 `WaitUntilAsync(() => world.Bus.Selections == 1)` 再保留原断言；`Attempts == [0, 0]` 与索引 0 两条断言不变；`Build` 与 `ScheduleAfterSettlement` 放进 `TestGameplay` → 意图不变：瞬时冲突后重试的是同一个个人确认（`Attempts == [0, 0]`、索引 0），不是换一列 |
| `IllusionTowerPublicFlowTests:149` `Sweep_captured_for_an_old_run_cannot_return_or_reward_its_replacement`（Application，`PvfFact`） | 通过（它在 `Task.Run` 里直接调用投递的 `SweepAsync`，本单元未改这条路径） | 顺延 T-004c | 后继：`IllusionTowerPublicFlowTests.A_sweep_tick_after_the_old_runs_return_was_due_leaves_its_replacement_alone`（经两塔扫描的公开 tick 入口跑一次真实定时 CMD；新 run 不回城、无结算帧、经验金币不变） |
| `ScheduledTaskTests.Different_task_can_finish_while_long_task_is_blocked_and_stop_cancels_it`（Application，本单元新判定，§9 第 (ii) 类） | 迁入后两条任务在同一执行器上串行，另一任务在被卡住的任务结束前不会执行 | 按 RQ-013 改写，改名为 `Blocked_task_postpones_rather_than_loses_another_and_stop_cancels_it` | 旧：一个任务被卡住时另一个任务仍能完成；停止取消被卡住的那个并释放 scope → 新：被卡住的任务占着执行器时另一个任务不执行；放行后它执行一次（推迟而不丢失）；停止取消随后再次卡住的那个并释放其 scope → 意图变化（RQ-013 规定）：「任务之间互不阻塞」被规格删除，「停止能取消一个卡住的任务」保留 |

按执行单元的新分布（总数 136 ＋ 1 ＝ 137）：

- T-004b：7 － 3 ＝ 4（TASK-12 1 ＋ TASK-13 2 ＋ 原已标「顺延 T-004c」的 `ReplayStateSnapshotTests:206` 1 ＝ 4）；
- T-004c：74 ＋ 1（`:149`）＝ 75；
- T-006：53；
- T-008：1；
- TASK-09 已删除：1；
- TASK-11 已改写：3（`:109`、`:235`、新判定的 `ScheduledTaskTests` 1 条）。
- 加法：4 ＋ 75 ＋ 53 ＋ 1 ＋ 1 ＋ 3 ＝ 137。

§6 中三条「有意留在作业外」的现状（TASK-11 §3.3）：

- `ControlledDelay.ReleaseAsync` 触发的回调：现在只在分离任务里放行等待，回调随后作为定时 CMD 入队，属于入口驱动；
- `World.Dispose` → `Coordinator.Dispose()`：不再取锁（只置停止标志并取消），探针 (B) 中的那 7 条应当消失，见 TASK-11 Result 的探针一节；
- 测试的 `World`／`PartyWorld` 各持有一个执行器，协调器的定时 CMD 落在它上面；基准的 21 处作业内轮询，加上本单元在 `Early_scroll_request…`、`Early_party_layout_requests…` 中为等待自动翻牌计时器登记各加的 1 处，共 23 处，都在 `TestGameplay.RunAsync(async () => …)` 新建的执行器上，不与定时 CMD 互等；作业外的轮询 6 处（`:109` 改写后 3 处、`:235` 3 处）。

## 10. TASK-12 处置（2026-10-22，基准 `648fbc60`）

记 §1.2 中分给 TASK-12 的 1 条（`:117`）的去向，以及本单元新判定的 1 条（TASK-12 §9.2）。行号是 §1 的基准行号，以方法名为准。

| 用例 | 本单元的结果 | 去向 | 旧断言 → 新断言 → 保护意图 |
|---|---|---|---|
| `UsageQuotas/UsageQuotaTests.cs:117` `Quota_refresh_is_postcommit_coalesced_and_a_failed_projection_never_rolls_back_business_counts`（Application，第 1 个事务块） | 不改也通过：事务检查仍是 `DrainOnceAsync` 的第一步，执行器外照旧抛 `InvalidOperationException` | 在本单元改写（TASK-12 裁定第 5 条）：第 1 个事务块整体包进 `TestGameplay.RunAsync(f.Services, …)`，与第 2、3 块同形 | 旧：执行器外、事务内调用 `DrainOnceAsync` 抛 `InvalidOperationException`，`Assert.Empty(bus.Entries)` → 新：断言一字不改，现在在作业里、事务内调用，抛的仍是 `InvalidOperationException`（事务检查先于执行器守卫，作业不被标为程序错误；变异 M8 把两者对调后，这条用例以 `GameplaySelfWaitException` 变红）→ 意图不变：分发不在发布它的业务事务里开始 |
| `DomainEvents/DomainEventDispatchTests.cs` `A_slow_live_group_does_not_block_other_groups_or_hold_the_business_transaction`（Application，本单元新判定，TASK-08 §4 复核表中「TASK-12 再看」的一项） | 迁入后 live 作业在 `Task.Delay(Infinite, ct)` 上占住执行器，`FastLive`、counter、audit 的事件 CMD 与第二次 `SendAsync` 都排在它后面，5 秒等待必然超时 | 按 RQ-043 改写，改名为 `A_slow_live_hint_postpones_other_groups_and_still_runs_outside_the_business_transaction` | 旧：live 卡住时 `FastLive`、counter 7、audit 7 都完成，第二次发送 5 秒内完成，停止后 live 只收到 1 条，计时器归零 → 新：live 阻塞改为可放行的 TCS（受令牌约束）；`LiveEntered` 之后入队一个标记作业，放行前断言它尚未开始；放行后 `FastLive`、counter 7、audit 7 全部完成，第二次发送完成，停止后计时器归零；owner `"a"` 的 live 提示恰好一次（`"b"` 可能在停止前被消费，不作断言）；live 订阅里的 `Assert.Null(Transactions.Current)` 原样保留 → **意图变化（RQ-043 规定）**：「订阅组互不阻塞」被规格删除；「live 投影不持有业务事务」保留；新增「慢组推迟而不丢失其他组」 |

`Commit_under_database_admission…`（§2.2，T-004c）：测试在执行器外持准入门期间不等待任何作业，组循环的事件作业在门上等，属于反向情形；本单元正式全量中通过，保持原样。

本单元没有出现 TASK-12 §9.4 第 (iii)～(vi) 类的其他冲突：全量中 `GameplaySelfWait` 为 0，除那 2 条 `DimensionDungeonTests` 外没有失败。

按执行单元的新分布（总数 137 ＋ 1 ＝ 138）：

- T-004b：4 － 1（`:117`）＝ 3（TASK-13 2 ＋ 原已标「顺延 T-004c」的 `ReplayStateSnapshotTests:206` 1 ＝ 3）；
- T-004c：75；
- T-006：53；
- T-008：1；
- TASK-09 已删除：1；
- TASK-11 已改写：3；
- TASK-12 已改写：2（`:117`、新判定的 `A_slow_live_group…` 1 条）。
- 加法：3 ＋ 75 ＋ 53 ＋ 1 ＋ 1 ＋ 3 ＋ 2 ＝ 138。

## 11. TASK-14 处置（2026-10-22，基准 `2d8cec4d`）

记 §1.2 中分给本单元（拆分表中原写作 TASK-13，编号顺延后为 TASK-14）的 2 条的去向。行号是 §1 的基准行号，以方法名为准。

| 用例 | 本单元的结果 | 去向 | 旧断言 → 新断言 → 保护意图 |
|---|---|---|---|
| `GmPlayerSamplingHttpTests.cs:14` `OnlineListUsesOneCapturedSetForTotalAndItemsEvenWhenDisconnectOccursDuringDatabaseRead`（Host） | 批 B 之后挂起：GM 查询作为 GM CMD 在执行器上被 `BarrierPersistence` 卡在数据库读里，测试线程同步等 `TestGameplay.Run` 的 `Cleanup` 作业，而那个作业排在查询后面；Host 项目运行中 1 m 46 s 后失败 | 改写（裁定第 11 条）：把交错从「跨作业的门闩」改为「在同一条 CMD 的读回调里内联执行」，与同文件 `:41` 同形。`ReadBarrier` 的 `Entered`／`Release` 两个 TCS 换成回调 `DuringRead`，armed 时在读之前调用；回调里的 `TestGameplay.Run` 发现自己已在作业里而内联执行 | 旧：查询卡在读里时，测试在执行器外做 `Cleanup` 与断连，再放行；本页 `total` 1、`items` 1、`isOnline` 真，刷新后 0／0 → 新：这 5 条断言一字不改；`Cleanup` 与断连发生在查询自己的 CMD 里、数据库读之前的那一刻 → 保护意图不变：在线集合在查询开始时捕获一次，查询中途的变化不改变本页的总数与条目。刷新后的 0／0 仍证明交错确实发生 |
| `Accounts/AuthenticationConcurrencyTests.cs:10` `OverlappingPasswordReadsCannotLeaveTheSecondLoginWithAnUnwritableSqliteSnapshot`（Persistence） | 只换驱动调用就通过 | 改写（裁定第 11 条）：两次登录改经 `fixture.LoginAsync`（reader 在执行器外校验，再经总线发登录命令），`InterleavedPasswords` 照旧在 `Verify` 里交错；第 29～30 行注释改为说明新结构 | 旧：两次登录经总线并发，第一次在写事务内的 `Verify` 里被卡住，第二次或已读到快照、或在 SQLite 写意向上等；两次都 `Success`、票据不同、都能消费 → 新：断言一字不改；两次校验都在 reader 里、任何 SQLite 快照之外重叠，各自的登录命令随后在写意向下重读记录、比对、签发 → **保护意图收窄**：旧前提「两次校验各在自己的写事务里，第二次可能拿着不可写的快照」→ 新前提「校验不在任何事务里，命令重读并比对记录，变了就重做一次校验」→ 理由：RQ-007 把哈希移出 CMD，校验不再持有快照；「重叠的口令校验不会让第二次登录失败」仍由这条守着，与本清单原定处置「按新的先哈希、后单条 CMD 结构重写」一致 |

§8.2 中只因编译换了驱动调用、仍归 T-004c 的 2 条：`AuthenticationConsumptionTests.ExactCredentialBindsAgainButOverlappingUncommittedBindsAreRejected` 与 `LoginSessionQueuedCommandTests.ReplacedLoginCannotWriteAfterWaitingForTheAccountGateEvenAfterDisconnectCallback`。断言不变，本单元正式全量中通过，不算处置。

本单元没有新判定的冲突：正式全量中除那 2 条 `DimensionDungeonTests` 外没有失败，`GameplaySelfWait` 为 0。第一次全量另有 2 条失败，都不是迁入造成的交错，归因与 TASK-14 Result Checks 2 一致：`InboundExecutorWireTests.A_game_and_a_channel_message…` 是测试端口争用（同一输出里 TouchSocket 记了 `Address already in use`，基准上同频）；`ChannelHostStartupTests.PackagedHost…` 的频道列表超时推断为负载下测试进程的停顿（未测到）。证据与复现尝试见 `logs/task-14-slow-isolated.txt`、`logs/task-14-isolated.txt`、`logs/task-14-gm-recheck.txt`。TASK-14 §8.1 (iii) 初筛的 4 个文件（`GmPlayerDirectoryHttpTests` 15 处、`GmOfficialMailHttpTests` 4 处、`GmCeraHttpTests` 1 处、`GmPlayerBrowserFixtureTests` 1 处 `TestGameplay`）都不在包裹区域里发 HTTP 请求，不需要解包。

按执行单元的新分布（总数仍为 138）：

- T-004b：3 － 2 ＝ 1（`ReplayStateSnapshotTests:206`，早已标「顺延 T-004c」）；
- T-004c：75；
- T-006：53；
- T-008：1；
- TASK-09 已删除：1；
- TASK-11 已改写：3；
- TASK-12 已改写：2；
- TASK-14 已改写：2。
- 加法：1 ＋ 75 ＋ 53 ＋ 1 ＋ 1 ＋ 3 ＋ 2 ＋ 2 ＝ 138。

## 12. TASK-16 处置（2026-10-22，基准 `c3115f9a`）

本单元没有分配到冲突用例。记两件事：`ReplayStateSnapshotTests:206` 改记到 T-004c；§4 的协调器容错使一条既有用例的末条断言失效，经主控方裁定（`tasks/TASK-16.md`「主控方裁定（执行中，§4 停止之后）」）按冲突改写。

| 用例 | 本单元的结果 | 去向 | 旧断言 → 新断言 → 保护意图 |
|---|---|---|---|
| `Sessions/ReplayStateSnapshotTests.cs:206` `Inventory_and_account_balances_are_read_from_one_real_sqlite_snapshot`（Application；行号沿用 TASK-07，基准 `c3115f9a` 上方法从第 212 行开始，以方法名为准） | 正式全量中通过 | 从「T-004b（早已标顺延 T-004c）」改记为 T-004c | 不改 |
| `CardReward/DungeonCardPresentationCoordinatorTests.Timers.cs` `The_drain_waits_for_a_timer_already_waiting_on_its_cmd_and_stops_the_rest`（Protocol，TASK-11 的 C7，本单元新判定） | §4.2 生效后末条断言失败：`Assert.Throws() Failure: No exception was thrown`（`logs/task-16-red.txt` 第二段）；前面的断言全部仍成立 | 改写末条断言，其余原样 | 旧：排空之后再 `ScheduleAfterSettlement` 抛 `ObjectDisposedException` → 新：排空之后再调度不抛异常；100 ms 后入队序列仍是 `["test:blocker", LayoutCommand]`（没有新的 `timer:` 作业）；2 秒延时上没有等待者；`pending` 的条目数与排空时相同 → 意图不变：排空之后协调器不再启动计时器。理由：抛异常是旧实现的副作用；容错实现让排空顺序不依赖模块注册次序（TASK-11 主控方补充 N3）。**与裁定文字的出入**：裁定写「`pending` 为 0」，但这条用例在排空前还为 `later` 局调度了一个计时器，它被排空取消后留在 `pending` 里（`Dispose` 的注释写明「被取消的源留在 `pending`」，`Stop`／`DrainAsync` 不在本单元改动范围内），所以排空时 `pending` 为 1；新断言改为「不增长」。与 T1 同形的「`pending` 为 0」由 T1 在一个干净的协调器上断言 |

按执行单元的新分布（总数 138 ＋ 1 ＝ 139）：

- T-004b：1 － 1（`:206`）＝ 0；
- T-004c：75 ＋ 1（`:206`）＝ 76；
- T-006：53；
- T-008：1；
- TASK-09 已删除：1；
- 已改写：TASK-11 3 ＋ TASK-12 2 ＋ TASK-14 2 ＋ TASK-16 1 ＝ 8。
- 加法：0 ＋ 76 ＋ 53 ＋ 1 ＋ 1 ＋ 8 ＝ 139。

本单元没有其他新判定的冲突：探针 x1（排除 T-004c 的 76 条）与正式全量中，`GameplaySelfWait` 为 0，最终正式全量的失败只有那 2 条 `DimensionDungeonTests`（此前两次全量各有一次打包进程用例的启动超时，负载形状，经主控方裁定放宽其就绪等待后不再出现，不是冲突，见 `tasks/TASK-16.md` Result）。x1 中 3.3 仍有 42／54，来自不在本清单里的 `UserServiceTests`（`OnlineUserServiceTests.cs`）方法直接调用观察者；它们今天不因迁移失败，是否构成冲突由 T-004c 开门断言时按拆分原则第 5 条判定（`t004c-entry.md` 第 4 项）。

## 13. TASK-16 第 2 轮：一条 T-006 用例改归 T-004c（2026-10-22，基准 `c3115f9a`）

出处：TASK-16 结果审查 B1（`reviews/task-16-result-review.md`）；主控方处置（第 2 轮）。

| 用例 | 事实 | 去向 | 处置 |
|---|---|---|---|
| `Protocol.Tests/Game/CleanupNotificationDeliveryTests.cs:51` `Existing_owner_and_town_notices_finish_before_party_projection`（`task-06-conflicts.md` K5 表，原归 T-006） | 在测试线程上直接调用 `CleanupNotificationDelivery.DeliverAsync`，经 `PartyNotificationDelivery` 在执行器外获取 `PartyDeliveryStream.Gate` 与 `PartyParticipantGate`（附录 F「删除」栏）。证据是探针 p1 的 P3 (B) 2 条：`logs/task-16-probe-Protocol.txt`，汇总在 `logs/task-16-probe-round2.txt` 的 `k5gates.py` 段。T-004c 开门断言时它必然失败 | **从 T-006 改归 T-004c** | 在开门断言的同一提交里处置；方式由 T-004c 细化时定，倾向「经执行器驱动、断言不变」或按 K5 规则改写 |

同时核对了其余 T-006（K5）用例：以 T-006 冲突用例为最外层测试的取门记录（A／B／C 三类都查），在五个项目中只有上面这一条（`k5gates.py`）。Protocol 排除轮 x3（只排除 T-004c 的 76 条加上这一条，共 77 条）中，执行器外的 P3 与 (C) 记录都归零。没有其他 T-006 用例需要改归。

按执行单元的新分布（总数仍为 139）：

- T-004b：0；
- T-004c：76 ＋ 1 ＝ 77；
- T-006：53 － 1 ＝ 52；
- T-008：1；
- TASK-09 已删除：1；
- 已改写：8。
- 加法：0 ＋ 77 ＋ 52 ＋ 1 ＋ 1 ＋ 8 ＝ 139。

## 14. TASK-17 处置（T-004c1：翻转＋门断言，2026-10-22，基准 `f13e612a`；2026-10-23 第 1 轮评审后原位修订）

- 「触发」取自两段诊断全量（含补跑）：`logs/task-17-flip-only.txt`（只翻转）与 `logs/task-17-gate-on.txt`（翻转＋门断言）。脚本 scratchpad `t17/trigger.py`。
  - 翻转：第一段已失败或挂起；门断言：第一段通过、第二段失败或挂起；仍通过：两段都通过。
  - 挂起 4 条（第一段 `:177`、`:206`；第二段 `:206` 与 DungeonLoading 两条），按 `--blame-hang-timeout` 报出的用例名取证；挂起项目随后排除该用例补跑到底。
- 类别 A～E 按 TASK-17 §4.2。后继用例名都能在工作区检索到。
- 「改写」一律只删交错、门闩与 TCS 本身，其余断言原样保留（逐方法核对见 `logs/task-17-diff-check.txt`）。下表「旧 → 新」只写有变化的断言。
- 通用后继（下文简写）：
  - 〔序〕`GameplayExecutorOrderTests.Concurrent_producers_see_their_work_run_in_enqueue_order_one_at_a_time` 与 `CommandBusExecutorTests.F7_concurrent_off_executor_sends_never_overlap`：同一执行器上 CMD 一次一个、先来先做，两条 CMD 之间不可能交错；
  - 〔门〕`GameplayGateAssertionTests`／`ProtocolGateAssertionTests`：执行器外取门即抛、执行器上需要等待即抛并黏滞到作业、违规后不泄漏计数。

### 14.1 删除（41）

| 用例 | 类 | 触发 | 保护意图 → 去向 | 后继 |
|---|---|---|---|---|
| `OnlineUserExecutionGateTests` 的 `SharedExecutionsOverlap…`、`CancelledWaiterIsRemoved…`、`CancellationRacingWithGrant…`、`IdleEntriesAreReclaimed…`（4） | A | 门断言 | 门上排队、FIFO、取消不泄漏 → 执行器上不再等门，随门删除（T-005） | 〔门〕`A_wait_violation_leaks_nothing_and_the_gate_opens_once_its_holder_releases`、`A_cancelled_caller_gets_cancellation_and_no_violation` |
| `SelectedSessionTransitionGateTests` 的 `ExclusiveBorrowPreserves…`、`PendingCancelledSharedFrame…`、`Cancelled_group…`、`ExplicitAcquireRetainsFifo…`、`ExclusiveOwnerCanBorrow…`、`BorrowDoesNotQueueBehindWriter…`、`DelayedInheritedClosedFrame…`（Theory 2 例）、`PendingCancelledFrameIsNotHeld…`、`GrantCancelRace…`（9） | A | 门断言 | 借用帧与排队、取消竞速 → 随门删除 | 〔门〕`Borrowing_inside_the_job_that_holds_the_lease_is_not_a_violation`、`A_borrow_frame_carried_off_the_executor_is_refused`、`Acquiring_many_contended_on_the_second_account_throws_and_releases_the_first` |
| `SelectedSessionTransitionGateTests.OuterLeaseIsVisibleThroughActualCommandBus…`（Theory 2 例） | A | 翻转 | 「先持门再经总线发命令」的借用＋另一写者等待 → 借用一半由同一作业内借用承接，等待一半随门删除 | `GameplayGateAssertionTests.Borrowing_inside_the_job_that_holds_the_lease_is_not_a_violation`；`LeaveDungeonTests.Exit_command_borrows_the_projection_lease_through_the_real_pipeline`（本单元改写，同一作业内持门经真实管线借用） |
| `TransactionalCommandSerializationTests.DirectCommands_RemainConcurrent` | A | 翻转 | 断言两条 Direct 命令真并发，与新不变量相反 | `CommandBusExecutorTests.F7_concurrent_off_executor_sends_never_overlap` |
| `SelectCharacterLifecycleTests.Achievement_projection_waits_for_the_complete_entry_workflow` | B | 门断言 | 投影门上的次序（投影等完整个选角工作流）→ 机制随门删除：投影是事件作业，选角作业结束前不会开始 | 〔序〕；`ProtocolGateAssertionTests.On_the_executor_the_title_projection_gate_is_taken_released_and_never_waited_on` |
| `SelectCharacterLifecycleTests` 的 `DisconnectWinning…`、`CommandHolding…`、`ObserverQueuedBeforeRollbackClose…`、`OrphanedDisconnect…`（4） | C | 门断言 | 观察者在过渡门上与选角交错 | TASK-10 给出：`LifecycleExecutorWireTests.A_disconnect_during_an_inbound_job_starts_only_after_that_job_completes`（W6，前两条）、`LifecycleExecutorWireTests.A_cmd_closing_its_own_source_connection_does_not_wait_on_itself`（W7）、`SerialDisconnectTests.The_old_connections_disconnect_after_a_replacement_session_neither_cleans_nor_deselects_it`（P1） |
| `UserServiceTests` 的 `DisconnectWaitingForLifecycle…`、`DisconnectObservedBeforeAuthentication…` | C | 门断言 | 同上 | P1；W6 ＋ `SerialDisconnectTests.The_old_connections_disconnect_after_a_newer_login_leaves_the_newer_identity_and_its_generation`（P2） |
| `UserServiceTests.TeardownWaitingForLifecycleDoesNotTakeOnlineOwnershipInReverse`（Theory 2 例） | C | 门断言 | 两把门之间的锁序 → 意图随门删除（T-005）：单消费者下不存在反向锁序 | 无（规格消除：RQ-001 单消费者） |
| `EffectOperationReplayTests.An_inherited_closed_operation_cannot_downgrade_to_trusted_direct_bus` | D | 翻转 | 继承流带着已关闭的效果操作帧调用总线时被拒 → 翻转后执行器外的调用一律成为新根作业、不带任何帧，「继承关闭帧」的前提不存在；只能跨流表达。**既定为「改写」，改为删除**，理由见 Result「与既定处置不同」 | 帧语义交 T-008（同 `:269`）；现状由 `CommandBusExecutorTests.F1_a_send_from_off_the_executor_runs_as_its_own_bus_job` 与 `GameplayDispatchTests` D3 证明作业不继承调用方的 AsyncLocal |
| `IllusionTowerPublicFlowTests.Sweep_captured_for_an_old_run_cannot_return_or_reward_its_replacement` | E | 门断言 | 旧 run 的扫描不顶替新 run | `IllusionTowerPublicFlowTests.A_sweep_tick_after_the_old_runs_return_was_due_leaves_its_replacement_alone`（TASK-11） |
| `PartyTownProjectionDeliveryTests` 的 `ReadRepairWaitsForTheMutationGate…`、`NewGenerationMutationCannotPass…` | B | 门断言 | 流门上的等待与「等到门后重读」、新旧代的次序 → 机制随门删除：读修复与变更是两条 CMD，中间不能插入别的 CMD | 〔序〕；`PartyTownProjectionDeliveryTests.A_read_repair_finding_its_stream_gate_taken_fails_the_job_at_once` 与 `A_moved_party_refresh_finding_its_stream_gate_taken_fails_the_job_at_once`（本路径的两处流门，第 2 轮补）；`PartyTownProjectionDeliveryTests.ACommittedRevisionStillRefreshesANewObserverButAnOlderSnapshotDoesNot`（读修复按修订号取舍） |
| `TownProjectionHandlerTests.AreaCrossingWaitsForTheSameAccountPartyTransitionGate` | B | 门断言 | 跨区等过渡门 → 随门删除 | 〔序〕；`TownProjectionHandlerTests.A_crossing_moves_the_character_the_service_owns` |
| `DungeonLoadingProjectionDeliveryTests` 的两条 `Replacement_…waits…` | B | 门断言（挂起） | 顶替等加载 CMD 结束 → 随门删除 | 〔序〕；`DungeonLoadingProjectionDeliveryTests.A_replacement_generation_cannot_inherit_the_frozen_runs_loading_submission` |
| `LeaveDungeonTests` 的 `Exit_owns_the_transition…`、`An_exit_queued_for_an_old_run…`、`A_return_waiting_on_the_transition_gate…` | B | 门断言 | 退出在门后排队期间被顶替 → 顺序改写不成立（顺序执行时退出读到的就是顶替后的 run），「排队期间被顶替」随门删除 | 〔序〕；`LeaveDungeonTests.Leaving_ends_the_run_and_projects_the_town`、`A_new_selection_intent_prevents_an_old_town_projection`、`A_command_without_a_published_session_is_ignored` |
| `SelectDungeonHandlerTests.Entry_transaction_cannot_reach_the_visit_until_exit_projection_releases_its_lease` | B | 门断言 | 入场事务等退出投影放租约 → 随门删除 | 〔序〕 |
| `PartySelectDungeonHandlerTests` 的 `Selection_waits_for_exit…`、`Cancelling_while_acquiring_commit_gates…` | B | 门断言 | 选择等退出；取门途中取消 → 执行器上不再等门，「等门时取消」不存在 | 〔序〕；`PartySelectDungeonHandlerTests.A_projection_failure_cannot_split_party_members_between_town_and_run` |
| `PartyCommandHandlerTests` 的 `Set_party_info_rechecks…`、`A_target_replaced_before_its_gate…`、`A_higher_account_inviter_replaced…`、`Reverse_direction_pair_requests…`（4） | B | 门断言 | 「门后重查会话」与反向锁序 → 查找与取门在同一 CMD 内，中间无交错；锁序随门删除 | 〔序〕；`PartyCommandHandlerTests.A_connection_without_a_current_selected_session_cannot_write_party_state`、`Stale_targeted_party_frames_stop_before_roster_or_session_capture`、`A_target_that_was_missing_on_the_first_lookup_cannot_appear_behind_the_gate` |
| `PartyNotificationTests.Revisions_of_one_party_generation_are_delivered_as_contiguous_phases` | B | 门断言 | 同代修订的四段投影连续 → 两次投递是两条 CMD | 〔序〕；`PartyNotificationTests.An_older_revision_arriving_after_a_newer_one_is_suppressed` |

删除合计：A 15（4 ＋ 9 ＋ 1 ＋ 1）＋ B 17（应用 1 ＋ Protocol 16：2 ＋ 1 ＋ 2 ＋ 3 ＋ 1 ＋ 2 ＋ 4 ＋ 1）＋ C 7 ＋ D 1 ＋ E 1 ＝ 41。用例（Theory 按例数）：41 ＋ 3（三个 Theory 各多 1 例）＝ 44。
（第 1 轮评审前此处为 43：`MultiAccountExecutionTests` 两条曾整文件删除，后继写的是另一个实现 `SelectedSessionTransitionGate.AcquireManyExclusiveAsync`，不能承接 `AccountExecutionBehavior` 的意图；已恢复并改写，见 §14.2。）

### 14.2 改写（29）

| 用例 | 类 | 触发 | 旧断言 → 新断言 → 保护意图 |
|---|---|---|---|
| `EffectOperationReplayTests` 的 `Postdurable_operation_record_failure…`、`Invalid_projection_freeze…`、`Unknown_applied_effect_change…`（`:229`、`:253`、`:345`） | D | 翻转 | 帧的建立与总线调用放进同一个 `TestGameplay.RunAsync(f.Services, …)`，总线在作业上内联、帧可见；断言一字不改 → 意图不变 |
| `EffectOperationReplayTests.Refusal_only_unselected_operation_cannot_pay_after_a_selection_arrives`（`:371`） | E | 翻转 | 旧：账本分派被 TCS 卡住，测试在外面清容器、重选会话再放行 → 新：分派在作业里执行，「选择到达」写在 handler 里、发命令之前（同一 CMD 内）；`ThrowsAsync<InvalidOperationException>`、数量 3、无消息三条断言不变 → 意图不变：只拒绝的操作在选择到达后也不能付款 |
| `ReplayStateSnapshotTests.Another_selected_settlement_cannot_commit_between_the_replay_read_and_its_batch_delivery`（`:31`） | B | 翻转 | 删 `Assert.False(newer.IsCompleted)`（交错）；回放先作为一条 CMD 完成，再发结算 CMD；`Gold==100000`（回放后）、`100100`、批次里读到的 100000、`TrackedEntryCount==0` 不变 → 意图：批次携带的是它读到的值、结算在其后；「读与发之间不能插入结算」由〔序〕承接 |
| `ReplayStateSnapshotTests.Registered_delivery_reads_fresh_inventory…`（`:60`） | B | 翻转 | 删「投递持有独占租约时另一流程必须等待」的 `Assert.False(exclusive.IsCompleted)`；投递改在作业内执行（生产中它就在作业里），其余 12 条断言（精确连接、6 帧、新库存、错连接被拒、独占可借用、共享不能升级、计数归零）不变 |
| `ReplayStateSnapshotTests.Replacement_while_waiting_for_lifecycle_lease_rejects_the_original_read`（`:177`） | B | 翻转（挂起） | 删 `Assert.False(pending.IsCompleted)`；先顶替（一条 CMD），再发原会话的读 → `ThrowsAsync<InvalidOperationException>`、计数归零不变 → 意图：被顶替的会话读不到 |
| `ReplayStateSnapshotTests.Inventory_and_account_balances_are_read_from_one_real_sqlite_snapshot`（`:206`） | E | 翻转（挂起） | 旧：读被卡住时测试用裸 SQL 改金币、用 `SeedShared`（一条 CMD）写账号材料 → 新：`SeedShared` 排不进被卡住的读，改为读开始前把 3262＝7 播种在账号 99 名下，读的中途用 `fixture.Sql` 把这一行改归账号 1（与金币一样是读中途提交的真实 SQLite 写）；四条断言一字不改 → 意图不变：角色金币与账号余额都出自同一个 SQLite 快照。（第 1 轮把 `SeedShared` 挪到读之后，账号余额一半恒真，评审指出后撤销「意图收窄」） |
| `CommonExperienceEffectTests.Unknown_fence_precedes_transaction_gate_release_and_queued_effects_never_price`（`:164`） | E | 翻转 | 旧：等第二条命令进入管线（Order 56 探针）→ 新：`Assert.False(admitted.Entered.Task.IsCompleted)`（第二条排在第一条后面，第一条结算前不会被准入）；其余断言不变 → 意图不变：排在后面的效果不计价 |
| `CommonExperienceEffectTests.Effect_uow_borrows_outer_exclusive_and_keeps_teardown_waiting_until_settlement`（`:219`） | B | 翻转 | 删拆除等待的两条交错断言；外层租约与命令同在一个作业，命令内联借用；拆除是之后的作业；`Award 30`、`exp 30` 不变 → 意图：效果工作单元借用外层独占；「拆除等结算」随门删除（〔序〕） |
| `CommonExperienceEffectTests.Publication_fence_is_synchronous_even_when_discard_waits_for_lifecycle_exclusive`（`:191`） | B | 门断言 | 删 `Assert.False(failure.IsCompleted)` 与外部持门者；发布失败在作业内执行；Fenced、IsBlocked、命令被拒、原因四条断言不变 → 意图：封锁同步生效；「丢弃等生命周期独占」随门删除 |
| `CommonExperienceEffectTests.Logger_failure_does_not_replace_unknown_cause_and_exact_close_callback_waits_for_lease_release`（`:281`） | B | 门断言 | 替身关连接器不再在回调里 `Task.Run` 取门，只记录请求；测试在命令结束后用一个作业取同一账号的过渡门（立即取得）；原 5 条断言不变 → 意图：日志失败不顶替未知原因、精确关连接；「回调等租约释放」改为「之后的拆除作业立即取得租约」 |
| `NetworkMessageDispatcherTests.Already_queued_request_without_login_authority_is_rejected_before_uow_after_fence`（`:118`） | B | 门断言 | 旧：测试持事务门，请求在门后排队时被封锁 → 新：封锁发生在请求自己的 CMD 里、准入之后、工作单元之前（探针 Order 56 的 `BeforeNext`）；`ThrowsAsync<UnauthorizedAccessException>`、`HandlerCalls==0` 不变。「先封锁再派发」的改法不成立：已封锁连接的请求在分派时被静默丢弃，不抛 |
| `MoveItemSpaceHandlerTests.Two_moves_arriving_at_once_are_both_carried_out_and_neither_is_refused`（`:310`） | B | 门断言 | 两个发起方由 `Task.WhenAll` 并发改为两条先后执行的作业（辅助 `Dispatch` 的 `Task.Run` 换成 `TestGameplay.RunAsync`）；断言不变 → 意图不变（旧实现本来就靠门串行） |
| `DomainEventDispatchTests.Commit_under_database_admission_returns_and_the_worker_consumes_after_admission_is_released`（`:159`） | B | 门断言 | 持准入门、开写事务、提交这一段包进 `TestGameplay.RunAsync(f.Services, …)`；断言不变 → 意图不变：准入门内提交能返回，事件在其后被消费 |
| `JoustIntegrationTests.Native_query_holds_selection_lease_until_exact_physical_batch_is_sent`（`:743`） | B | 门断言 | 删发送回调里的取门探针与 `Assert.False(pending.IsCompleted)`；回执与批次帧的 7 条断言不变 → 意图：查询的回执与批次形状；「持租约到批次发完」由〔序〕承接 |
| `OnlineExpiredItemCleanupTests.Queued_cleanup_revalidates_published_session_after_account_gate`（`:113`） | B | 门断言 | 删账号门与 `Assert.False(pending.IsCompleted)`；释放与重选作为一条 CMD 先执行，再发清理命令；拒绝、数量 3、无待处理三条断言不变 → 意图：离开世界的会话不能清理 |
| `LoginSessionQueuedCommandTests.ReplacedLoginCannotWriteAfterWaitingForTheAccountGateEvenAfterDisconnectCallback`（Persistence） | B | 翻转 | 删账号门与 `Assert.False(queued.IsCompleted)`；旧请求的授权在顶替前取得，顶替登录与断连回调先发生，写命令随后在一个作业里带着旧授权执行；`ThrowsAsync<UnauthorizedAccessException>`、口令哈希不变 → 意图不变 |
| `LeaveDungeonTests.Exit_command_borrows_the_projection_lease_through_the_real_pipeline` | B | 翻转 | 持门与经真实总线发命令放进同一个 `TestGameplay.RunAsync(provider, …)`；断言不变 → 意图不变（过渡期规则 1 点名的写法改为作业内持门） |
| `BossTowerProjectionConcurrencyTests`、`IllusionTowerProjectionConcurrencyTests` 的 `A_third_member_disconnect_during_capture_revalidates_and_completes` | B | 门断言 | 第三名成员离塔、离队、释放会话先作为一条 CMD 完成，转移随后作为一条 CMD 执行；6 条断言不变 → 意图：成员离开后转移仍成功、不带走离开者；「捕获期间」的交错随门删除 |
| `TitleBookProtocolTests` 的 `Mutation_projection_waits_for_ack…`、`Mirror_removal_projection…`（Theory 2 例）、`Achievement_progress_projection_waits…` | B | 门断言 | 删外部持门与「投影未完成」交错断言（`Assert.False(…IsCompleted)`、`Assert.Empty(events.Projected)`、`Assert.Equal(!refresh, delivery.IsCompleted)`）；订阅者在作业里执行；投影内容断言不变 → 意图：存入不变成解锁、镜像移除的通用对账、成就进度投影；「等 ACK」由〔序〕承接（ACK 是请求 CMD 的一部分） |
| `TitleBookMutationHandlerTests.Worn_requests_reply_with_the_native_equipment_coordinates_and_complete_ack_before_projection`（Theory 6 例） | B | 门断言 | handler 在作业里执行（发送替身不再阻塞）；删 `Assert.False(projection.IsCompleted)`，改为之后一个作业立即取得投影门；回执坐标等断言不变 → 意图：原生装备坐标；「ACK 先于投影」由〔序〕承接 |
| `MarriageProposalCodecTests.Two_party_delivery_is_ordered_through_peer_notification_and_command_can_borrow_fences` | B | 门断言 | 两方各为一条 CMD 先后执行，删 `Assert.False(second.IsCompleted)` 与 TCS；次序断言 `["first-ACK","first-NOTI","second-ACK+NOTI"]` 与作业内借用对方租约保留 |
| `CleanupNotificationDeliveryTests.Existing_owner_and_town_notices_finish_before_party_projection`（F-56） | B | 门断言 | `DeliverAsync` 包进 `TestGameplay.RunAsync`（生产中只在作业里调用）；断言不变 |
| `MultiAccountExecutionTests.All_accounts_are_locked_once_and_released_when_execution_fails` | A | 门断言 | 旧：执行体内在测试流上另取同账号门、断言等待并取消 → 新：作业里以 `[2,1,2]` 执行 `AccountExecutionBehavior`，执行体记录门里正好 2 个账号后抛 rollback；之后的作业能立即取得账号 1 与 2，计数归零 → 意图不变：每个账号只锁一次、失败后全部释放（第 2 轮恢复文件后改写） |
| `MultiAccountExecutionTests.Cancellation_while_waiting_for_second_account_releases_first_account` → 改名 `A_second_account_held_by_an_earlier_job_fails_at_once_and_releases_the_first` | A | 门断言 | 旧：测试持账号 2，行为在账号 2 上等待后被取消 → 新：作业 A 取得账号 2 不释放就结束；作业 B 以 `[2,1,2]` 执行行为，断言抛 `GameplayGateWaitException` 且执行体没跑；作业 C 立即取得账号 1 → 意图：取到一半失败时，已取得的账号被释放（「等待中被取消」随门删除，执行器上不再等待）。另加成功路径的去重用例 `A_repeated_account_is_taken_once_and_the_group_runs`（新增，不计入 77） |

改写合计：A 2（`MultiAccountExecutionTests`）＋ D 3 ＋ E 3（`:371`、`:206`、`:164`）＋ B 21（应用 11 ＋ Persistence 1 ＋ Protocol 8 ＋ F-56 1）＝ 29。

### 14.3 仍通过，顺延 T-004c2（7）

`TransactionalCommandSerializationTests` 的 `TransactionalCommands_DoNotRunTheirHandlersConcurrently` 与 `CancelledWaiter_DoesNotEnterAndDoesNotKeepTheGate`（A，翻转后命令在执行器上串行，第二条在排队时被取消），`CommandBusTests` 的租约释放 3 条（A，验证租约的那次总线调用翻转后经执行器），`DungeonObjectQueueTests.Leaving_the_room_before_prepare_rolls_back_the_reservation`（E，两个直接调用的发起方真并发、不经门），`AuthenticationConsumptionTests.ExactCredentialBindsAgainButOverlappingUncommittedBindsAreRejected`（E，执行器外开写事务，RQ-061 开启时才失败）。加法 2 ＋ 3 ＋ 1 ＋ 1 ＝ 7。

### 14.4 本单元新判定（不在 77 条内）

- `SelectCharacterLifecycleTests.RejectedRepeatedLoginStillLetsDisconnectObserverCleanupTheSelectedSession`：门断言下失败，测试线程直接调用观察者（与 F-18 同形）。观察者调用按 T5 包进 `TestGameplay.RunAsync`，断言不变；**不是冲突**（不靠门制造交错），按 §4.3 第 1 条的形状处理，单列于此。
- F-18（`UserServiceTests` 中直接调用观察者的非冲突方法）：9 个方法的 9 处观察者调用包进 `TestGameplay.RunAsync`，断言不变；其中 4 个在门断言下失败（`A_rejected_connection…`、两条 `DisconnectSelectedSession_…`、`DisconnectWithoutSelectedSession_…`），另 5 个（`ConnectedAsync` 不取门）为 3.3 归零一并包裹。没有新判定的冲突。

### 14.5 新分布（总数仍为 139）

- T-004c（顺延 c2）：7；
- T-006：52；
- T-008：1；
- TASK-09 已删除：1；
- 已改写（此前）：8；
- TASK-17 已删除：41；
- TASK-17 已改写：29。
- 加法：7 ＋ 52 ＋ 1 ＋ 1 ＋ 8 ＋ 41 ＋ 29 ＝ 139。按类：A 22 ＝ 15 删 ＋ 2 改 ＋ 5 顺延；B 38 ＝ 17 删 ＋ 21 改；C 7 ＝ 7 删；D 4 ＝ 1 删 ＋ 3 改；E 6 ＝ 1 删 ＋ 3 改 ＋ 2 顺延；22 ＋ 38 ＋ 7 ＋ 4 ＋ 6 ＝ 77；删 15 ＋ 17 ＋ 7 ＋ 1 ＋ 1 ＝ 41，改 2 ＋ 21 ＋ 3 ＋ 3 ＝ 29，顺延 5 ＋ 2 ＝ 7。
