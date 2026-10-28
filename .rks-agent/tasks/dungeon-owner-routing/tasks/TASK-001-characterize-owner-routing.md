# TASK-001 — 表征 13 个命令的现行分发结果

## Task Unit Contract

- Revision: 1
- Outcome and references：交付 Plan T-001，覆盖 Spec RQ-006、AC-004，以及 AC-002 中“现行已丢弃、保持丢弃”的格。用测试钉住新鲜帧在“位置 → 通道对照”各格的现行结果，以及现行已丢弃陈旧帧的组合。全部测试在未改动的基准上通过。
- Owned boundary：
  - 只新增或修改测试：`Server/tests/` 下的测试类与共享测试辅助。
  - 不修改 `Server/src/` 任何生产代码。
  - 新测试必须从**命令处理器的公开入口**（CMD handler 的 `HandleAsync`／`RunAsync`，或现有驱动对它们的封装）发起，不能直接调用将被重构的内部分支方法（如 `PickupBossAsync`、`SelectIllusionReturnAsync`、`CoordinateBossTowerPartyAsync`、`CompleteBossLoadingAsync`）。这样测试在结构迁移后仍能原样运行。
  - 断言只针对可观察结果：登记的回包（帧类型与关键字段、收件人）、领域状态（副本、背包、队伍、占用登记）、是否发生工作单元写入。不断言日志。
- Required checks：
  - `cd Server && dotnet build 86JP.Server.slnx`：0 警告，0 错误。
  - 新增测试全部通过；相关既有测试类照常通过。
  - 对关键格做变异检查：临时改动生产代码中对应分支（例如交换两塔分支顺序、让幻影分支不再先判陈旧、让 0x000E 幻影分支不发拒绝帧），确认相应表征测试会失败，然后用备份还原。变异检查不提交。
  - 最后跑一次完整测试 `dotnet test Server/86JP.Server.slnx`，失败须归因。
- Dependencies：无
- Result review：required。理由：后续 T-002～T-005 全部以这些测试作为“行为不变”的判据，表征不足会让结构迁移中的行为偏差在最终审查前一直不可见；而表征本身是否充分，执行者的自检无法可靠判断。

## Detailed Plan

1. **盘点覆盖**。对 Spec“位置 → 通道对照”表的每一格（13 个命令 × 无会话 / Boss 塔 / 幻影塔 / 普通或不在副本），以及以下特殊格，找出现有测试中是否已有从公开入口发起、断言可观察结果的用例。把盘点表写进 Result，列出覆盖用例（文件:用例名）或“缺口”。
   - 会话代次窗口：登记为塔，但塔服务不认这个会话；
   - 离队后尚未结束本局；
   - 组队类命令在调用者不在塔、队员在塔时进塔通道；
   - 0x000E 失败时，幻影塔发拒绝帧而 Boss 塔不发；
   - 0x000F 在 Boss 塔释放成功后续选图，未释放则结束；
   - 0x0027 物件破坏：Boss 塔忽略，幻影塔与城镇回空通知；
   - 0x0029：Boss 塔回拒绝帧，幻影塔与城镇静默返回。

   背景证据见 `../evidence/background-7adec858.md` 第 1.3、1.4、2.3、3 节。
2. **补表征测试**。对缺口补用例，优先放进同一命令现有的测试类或驱动旁。若跨命令的格更适合集中放置，新建 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonOwnerRoutingCharacterizationTests*.cs`。夹具优先复用：
   - 塔：`Server/tests/Support/TowerAlgorithmDrivers.cs`、`ProtocolAlgorithmFixture`；
   - 普通副本：`DungeonExitDrivers`、`LoadingAlgorithmTestDriver`、`EntryHandlerDriver`，以及 `Server/tests/Support/TestParties.cs`。
   - 会话代次窗口可用同一角色的两个 `SelectedSessionIdentity`（不同 `ConnectionGeneration`）构造：旧会话仍在塔中，新会话发命令。
   - 如果某格在现有夹具下确实无法从公开入口构造，在 Result 中写明原因与替代证据，不要改生产代码。
3. **补“保持丢弃”的陈旧帧用例**。Spec 中“陈旧帧行为变化清单”之外、现行已丢弃的组合，在可从公开入口构造的位置补用例：断言无工作单元写入、无回包。陈旧帧用 `new GameMessageMetadata(...) { InboundOrdinal = 正数, SequenceIsFresh = false }` 构造，参照 `SelectDungeonHandlerTests.cs:364`。不需要覆盖属于“行为变化清单”的格，那些由 T-003～T-005 写成新行为的测试。
4. **检查**：按 Required checks 执行，并把变异检查的具体改动与失败用例写进 Result。

## Result

- Outcome: done（Round 2 已按 Result Review Round 1 返工，改动见各节标注“Round 2”的条目）

### 覆盖盘点

缩写：
- N = `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonOwnerRoutingCharacterizationTests*.cs`，本 Task 新增，用例名后括号是 Theory 数据。
- A = `Server/tests/Dfo.Server.Application.Tests`，P = `Server/tests/Dfo.Server.Protocol.Tests`。

“现有”只列从公开入口发起、断言可观察结果的用例：真实解码器加 `DispatchAsync`、各 RouteDriver，或测试侧构造 CMD 后调用 `HandleAsync`。直接调用 internal 分支方法的不计入，例如 `CompleteBossLoadingAsync`、`TryLeaveAsync/TryRemoveAsync/TryTransferAsync`、`DeliverPickupAsync`、`PickUpAsync`。

“普通或不在副本”一列分为普通副本与城镇。N 的夹具对普通流程取城镇和“塔座位只被旧会话代次持有”两种情形。普通副本内的新鲜帧结果沿用现有用例。

| 命令 | 无会话 | Boss 塔 | 幻影塔 | 普通副本 | 城镇 |
|---|---|---|---|---|---|
| 0x002B 拾取 | N:Pickup_is_owned_by_the_requesters_place(NoSession) | 现有 A/World/GroundPickupFailureBoundaryTests:Task46_boss_pickup_real_wire_commit_scope_and_encoder_boundary；N:Pickup…(Boss) | 现有 同文件:Task46_illusion_pickup_real_wire_commit_scope_and_encoder_boundary；N:Pickup…(Illusion) | 现有 P/Game/GroundPickup/GetItemHandlerTests:Successful_pickup_removes_the_world_piece_for_every_frozen_participant；A/World/GroundPickupFailureBoundaryTests:Task46_ordinary_pickup_real_wire_commit_scope_and_encoder_boundary | N:Pickup…(Town) |
| 0x0028 角色死亡 | N:Character_death_is_owned_by_the_requesters_place(NoSession) | 现有 A/World/CharacterDeathFailureBoundaryTests:Task47_boss_death_real_wire_scope_encoder_and_exact_physical；N:(Boss) | 现有 同文件:Task47_illusion_death_real_wire_scope_encoder_and_exact_physical；N:(Illusion) | 现有 P/Game/CharacterDeath/CharacterDeathHandlerTests:Death_targets_exact_admitted_sessions_and_duplicate_reports_do_not_restart_the_ui；A/World/CharacterDeathFailureBoundaryTests:Task47_ordinary_death_has_zero_transactions_and_repeat_never_restarts_ui | N:(Town) |
| 0x0027 非物件 | N:Monster_death_is_owned_by_the_requesters_place(NoSession) | 现有 A/World/MonsterDeathFailureBoundaryTests:Task45_boss_kill_real_wire_commit_scope_and_encoder_boundary；N:(Boss) | 现有 同文件:Task45_illusion_kill_real_wire_commit_scope_and_encoder_boundary；N:(Illusion) | 现有 P/Game/MonsterDeath/MonsterDeathHandlerTests:TheKillReportsTheStandingItLeftBehind；A/World/MonsterDeathFailureBoundaryTests:Task45_ordinary_and_object_keep_distinct_transactions_and_protection(passive=false) | N:(Town)，回 Empty |
| 0x0027 物件破坏 | N:Object_destruction_is_ignored_in_a_boss_tower_and_otherwise_acknowledged_empty(NoSession) | N:同上(Boss)，忽略 | N:同上(Illusion)，经普通流程回 Empty | 现有 P/Game/MonsterDeath/MonsterDeathHandlerTests:Object_rewards_are_projected_only_to_each_owners_exact_session；A/World/MonsterDeathFailureBoundaryTests:Task45_ordinary_and_object…(passive=true) | N:同上(Town)，回 Empty |
| 0x0025 加载完成 | N:Finish_loading_is_owned_by_the_requesters_place(NoSession) | 现有 A/World/DungeonLoadingFailureBoundaryTests:Task44_boss_last_peer_loading_preserves_two_units_and_publication_boundary；P/Game/BossTower/BossTowerPublicHandlerTests:Finish_loading_handler_waits_for_all_members_then_starts_once_and_opens_each_gate；N:(Boss) | 现有 A/World/DungeonLoadingFailureBoundaryTests:Task44_illusion_loading_keeps_empty_protection_and_start_belongs_only_to_stage_mode_one；N:(Illusion) | 现有 同文件:Task44_ordinary_ready_is_direct_and_move_uses_real_sqlite_atomic_boundary(move=false) | N:(Town)，回 LoadingComplete |
| 0x002D 过图 | N:Move_map_is_owned_by_the_requesters_place(NoSession) | 现有 P/Game/BossTower/BossTowerPublicHandlerTests:Invalid_move_is_handled_without_ordinary_fallback…（前半）；N:(Boss) | 现有 A/World/IllusionTowerDomainServiceTests:Cleared_floor_keeps_drops_until_move_and_moves_immediately_without_a_minimum_stay；N:(Illusion) | 现有 P/Game/DungeonEntry/PartyMoveMapHandlerTests:The_leaders_room_change_is_sent_to_every_frozen_current_generation；A/World/DungeonLoadingFailureBoundaryTests:Task44_ordinary…(move=true) | N:(Town) |
| 0x0048 Eplp | 现有 P/Game/DungeonClear/LeaveDungeonTests:A_command_without_a_published_session_is_ignored；N:Eplp_is_owned_by_the_requesters_place(NoSession) | 现有 A/World/DungeonExitLifecycleTests:Task49_boss_result_choice_keeps_select_then_independent_leaves_and_exact_wire；N:(Boss) | 现有 同文件:Task49_illusion_result_choice_keeps_plain_empty_selected_transaction；N:(Illusion) | 现有 A/World/DungeonDepartureProtocolCharacterizationTests:Eplp_by_the_leader_ends_the_run_for_both_with_the_peer_decision_before_the_leader_ack；A/World/DungeonExitLifecycleTests:Task49_ordinary_Eplp_real_scope_has_no_transaction_and_ends_before_scope_failure | 现有 P/Game/DungeonClear/LeaveDungeonTests:Leaving_without_a_run_is_still_answered_and_still_projects_the_town；N:(Town) |
| 0x0084 回城 | N:Back_to_village_is_owned_by_the_requesters_place(NoSession) | 现有 A/World/DungeonSelectionTowerBoundaryTests:Task42_back_to_village_commits_tower_departure…；N:(Boss) | 现有 A/World/DungeonSelectionFailureBoundaryTests:Task42_illusion_back_scope_failure_keeps_the_original_empty_selected_protection；N:(Illusion) | 现有 A/World/DungeonDepartureProtocolCharacterizationTests:Back_to_village_by_the_leader_with_a_run_ends_it_for_both_and_keeps_the_party | 现有 A/Inventory/DungeonSelectionProtocolFlowTests:Task42_real_selection_and_return_are_one_shot_and_keep_the_gate_position；N:(Town) |
| 0x000F 进入选图 | N:Enter_selection_has_only_a_boss_owner(NoSession) | 未释放即结束：N:Enter_selection_has_only_a_boss_owner(Boss)。释放后续选图：N:Enter_selection_continues_into_the_ordinary_selection_once_the_boss_run_is_released；现有 A/World/DungeonSelectionFailureBoundaryTests:Task42_released_boss_run_keeps_its_full_protection_scope_when_the_real_selection_encoder_fails | N:Enter_selection_has_only_a_boss_owner(Illusion)，走普通选图 | （选图从城镇发起） | 现有 A/Inventory/DungeonSelectionProtocolFlowTests:Task42_real_selection_and_return…；P/Game/DungeonEntry/PartySelectDungeonHandlerTests:The_leader_opens_one_selection_context_for_every_party_member；N:(Town) |
| 0x0029 复活币 | N:Revival_coin_is_refused_only_by_a_boss_tower(NoSession) | N:同上(Boss)，回拒绝帧 | N:同上(Illusion)，静默 | 现有 A/World/CharacterDeathFailureBoundaryTests:Task47_revival_real_wire_payer_target_flag_scope_encoder_and_repeat | N:同上(Town)，静默 |
| 0x002A 放弃 | N:Give_up_alone_is_owned_by_the_requesters_place(NoSession) | N:Give_up_alone…(Boss)；N:Giving_up_from_town_is_owned_by_the_tower_a_member_sits_in(Boss) | N:Give_up_alone…(Illusion)；N:Giving_up_from_town…(Illusion) | 现有 A/World/DungeonPartyOwnedRunProtocolTests:Giving_up_alone_ends_the_whole_run；A/World/DungeonDepartureProtocolCharacterizationTests:Giving_up_detaches_the_giver_then_returns_them_to_town… | 现有 P/Game/BossTower/BossTowerProjectionConcurrencyTests:Give_up_outside_a_tower_runs_the_ordinary_path_once_and_answers_before_the_town；N:Give_up_alone…(Town)、Giving_up_from_town…(Town) |
| 0x000D 退队 | 现有 P/Game/Party/PartyCommandHandlerTests:A_connection_without_a_current_selected_session_cannot_write_party_state；N:Party_mutations_without_a_published_session_change_nothing(13) | 现有 A/World/PartyTowerFootprintBoundaryTests:Task39_cancellation_after_the_real_tower_commit…（Boss 部分类）；P/Game/BossTower/ExactTowerPhysicalClosureTests:Typed_tower_party_leave_failure_does_not_close_a_same_ID_replacement_physical_connection；N:A_member_leaving_from_town_is_owned_by_the_tower_the_leader_sits_in(Boss) | 现有 A/World/PartyTowerFootprintBoundaryTests:Task39_cancellation…（Illusion 部分类）；N:同上(Illusion) | 现有 P/Game/Party/PartyCommandHandlerTests:Leaving_party_in_a_run_detaches_only_the_departing_character；A/World/DungeonDepartureProtocolCharacterizationTests:Leaving_the_party_in_a_run_detaches_the_leaver_without_a_town_return | 现有 P/Game/BossTower/BossTowerProjectionConcurrencyTests:Leave_party_outside_a_tower_runs_the_ordinary_path_once_and_delivers_the_party_change；N:同上(Town) |
| 0x000E 踢人 | N:Party_mutations_without_a_published_session_change_nothing(14) | 现有 A/World/PartyTowerFootprintBoundaryTests:Task39_whole_and_legacy_tower_departure_keep_the_same_membership_boundary(remove=true, legacy=false)；N:Removing_a_seated_member_from_town_is_owned_by_that_members_tower(Boss)；N:A_non_leaders_removal_is_refused_by_the_owner_of_the_whole_partys_place(Boss) | 现有 同上（Illusion 部分类）；N:两个同名用例(Illusion) | 现有 P/Game/Party/PartyCommandHandlerTests:Removing_a_member_in_a_run_detaches_only_the_removed_character；A/World/DungeonDepartureProtocolCharacterizationTests:Removing_the_member_in_a_run_detaches_them_then_returns_them_to_town | 现有 P/Game/Party/PartyCommandHandlerTests:Removing_an_empty_slot_returns_the_native_error_without_a_domain_write；N:两个同名用例(Town) |
| 0x0079 转让队长 | N:Party_mutations_without_a_published_session_change_nothing(121) | N:Leadership_given_from_town_to_a_seated_member_is_owned_by_that_members_tower(Boss)；N:A_boss_seat_in_the_party_wins_over_an_illusion_seat | N:Leadership_given…(Illusion) | 现有 A/World/DungeonDepartureProtocolCharacterizationTests:Transferring_leadership_in_a_run_keeps_both_members_and_moves_the_run_leader | 现有 P/Game/BossTower/BossTowerProjectionConcurrencyTests:Change_leader_outside_a_tower_runs_the_ordinary_path_once_and_delivers_the_party_change；N:Leadership_given…(Town) |

特殊格：

| 格 | 覆盖 |
|---|---|
| 会话代次窗口：单人命令（登记为塔、塔不认新会话） | N:A_tower_seat_held_by_an_older_generation_routes_the_new_generation_to_the_ordinary_flow(tower, type)。Round 2 起拆成按命令的 Theory（Boss、Illusion × 11 个命令，共 22 格），每格只发一个命令；新增 0x0027 物件破坏（Boss 代次窗口下现行回 Empty，误判为 Boss 时会被忽略）。用 `ReselectOnSameConnection` 构造：同一连接、同一角色、新代次，旧代次仍是塔成员，登记仍指向塔。0x002B、0x0028、0x0027、物件破坏、0x0025、0x002D、0x0029、0x0048、0x0084、0x000F、0x002A 都走普通流程，结果与城镇一致。现有用例中没有公开入口的同类覆盖 |
| 会话代次窗口：组队类命令（Round 2 新增） | N:A_party_whose_only_tower_seat_is_held_by_an_older_generation_takes_the_ordinary_flow(tower, callerSeated, type)，Boss、Illusion × 调用者或队员 × 0x000D、0x000E、0x0079、0x002A，共 16 格。两人队在城镇组队；把队长（调用者）或队员送进塔后，再用 `Reselect` 换成新代次，由队长发命令。断言：两塔都没被请求动作、没有工作单元、没有任何回包（包括 `UdpHostNotification` 与拒绝帧），结果与城镇格相同（0x000D 队长离队、0x000E 队员被移出、0x0079 队长转给队员，这三项都有队伍通知投影；0x002A 读取队长当前视图、队伍不变） |
| 离队后尚未结束本局 | N:An_ordinary_registration_without_a_findable_run_is_answered_by_the_ordinary_flow（0x0027、0x0025）。见下文“无法从公开入口构造的格” |
| 组队类命令：调用者不在塔、队员在塔，进塔通道 | N:A_member_leaving_from_town…、Leadership_given_from_town…、Removing_a_seated_member_from_town…、Giving_up_from_town…（各取 Boss、Illusion、Town）；整队位置 Boss 优先：N:A_boss_seat_in_the_party_wins_over_an_illusion_seat |
| 0x000E 失败：幻影塔发拒绝帧、Boss 塔不发 | 可预判的拒绝：N:A_non_leaders_removal_is_refused_by_the_owner_of_the_whole_partys_place（Boss 无帧，Illusion 与 Town 发 `RemovePartyMemberRefusal`）。塔写入未生效：N:Removing_a_seated_member_from_town…（Boss 无帧，Illusion 发拒绝帧） |
| 0x000F：Boss 塔释放成功后续选图，未释放则结束 | N:Enter_selection_continues…（释放后读取请求者当前视图，即进入普通选图）；N:Enter_selection_has_only_a_boss_owner(Boss)（未释放时不读视图）。现有 Task42_released_boss_run_keeps… 在真实 DI 下证明续选图会写入选图意图并调用编码器 |
| 0x0027 物件破坏：Boss 塔忽略，幻影塔与城镇回空通知 | N:Object_destruction_is_ignored_in_a_boss_tower_and_otherwise_acknowledged_empty |
| 0x0029：Boss 塔回拒绝帧，幻影塔与城镇静默 | N:Revival_coin_is_refused_only_by_a_boss_tower |

“保持丢弃”的陈旧帧（不含“陈旧帧行为变化清单”中的格）：
- N:A_stale_frame_is_dropped_where_it_is_dropped_today（37 格）：
  - 0x002B、0x0028、0x0027（非物件）、0x0025、0x002D、0x0048、0x0084、0x002A，各取普通副本、Boss、幻影、城镇；
  - 0x002B、0x0025、0x002A 的无会话；
  - 物件破坏的 Boss；
  - 0x000F 的 Boss。
- N:A_stale_party_mutation_is_dropped_wherever_the_party_stands（15 格）：0x000D、0x000E、0x0079 各取整队在普通副本、Boss、幻影、城镇以及无会话。这是这三个命令首次有 CMD 层的陈旧帧覆盖。
- 断言内容：无回包；无工作单元；两塔都没有被请求动作；不读当前视图；队伍、普通副本（RoomVersion、死亡与就绪人数）、两塔座位都保持原样；组队类另断言无通知投影。

无法从公开入口构造的格，以及替代证据：
- **离队后尚未结束本局（W2）**：现行代码只在同一个 CMD 内出现这个窗口（`MutatePartyStateAsync` 先离队、再 `EndAsync`），跨命令无法构造。替代做法是在夹具的登记替身中写入残留的 Ordinary 登记，同时让 `runs.Find` 找不到副本，钉住“普通流程按城镇作答”。现行代码不读登记，所以这条用例在当前基准上只是前向守护：等定位能力改为读登记后，它才真正生效。
- **城镇列中 0x0028、0x002D、0x0029 的普通流程**：没有副本时普通流程不产生任何可观察结果，只能断言“无写入、无回包、没有请求任何塔”。
- **城镇列中 0x0048、0x0084、0x000F、0x002A 的普通流程**：夹具中没有可加载的角色视图，只钉到“普通流程读取了请求者的当前视图并停止”。完整的城镇回包由上表列出的现有用例覆盖。
- **组队类无会话格的证据边界**（Round 2 注明）：`Party_mutations_without_a_published_session_change_nothing` 以及陈旧组队矩阵中的 NoSession 格，都经 `ComponentContext.For` 构造执行上下文，其当前性判定恒为 `() => true`（`Server/tests/Dfo.Server.Protocol.Tests/Support/CardProtocolDrivers.cs:71-77`）。生产中 `request.DemandCurrent()` 先于陈旧判定（`GamePartyTransitionCommandHandler.Stages.cs:759-760`），这些用例没有钉住它对无会话连接的真实结果，只钉住“当前性成立时，无会话连接不写队伍、不回包”。
- **普通副本列的新鲜帧**：本 Task 没有在新夹具里重复覆盖。现有用例中有一部分经 `ProtocolAlgorithmFixture` 驱动，塔服务是 EmptyWorld 默认值，T-002 补登记时需要一并适配（BD-004）。

### Changed

- 新增 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonOwnerRoutingWorld.cs`：路由表征夹具。
- 新增 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonOwnerRoutingCharacterizationTests.cs`：单人命令的新鲜帧矩阵、代次窗口、残留登记。
- 新增 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonOwnerRoutingCharacterizationTests.Party.cs`：整队位置、0x000E 回包差异、无会话。
- 新增 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonOwnerRoutingCharacterizationTests.Stale.cs`：保持丢弃的陈旧帧。
- Round 2：`DungeonOwnerRoutingCharacterizationTests.cs` 把代次窗口用例改为按命令的 Theory，并加入物件破坏；物件破坏常量 `ObjectDestruction` 从 `.Stale.cs` 移到主文件。`.Party.cs` 新增组队类代次窗口 Theory。
- 未修改 `Server/src/`（`git diff --stat -- Server/src` 为空），也未修改既有测试与共享辅助。

### Why

- **入口**：每次请求都用 `ProtocolAlgorithmFixture.Create<Handler>` 新建一个 CMD handler，再调用其 `HandleAsync`。生产中每个 CMD 也是一个 Scoped 实例，`closeTargets` 等字段状态只在一个 CMD 内有效。测试不触碰 `PickupBossAsync`、`Coordinate*PartyAsync`、`CompleteBossLoadingAsync` 等 internal 分支。
- **塔替身**：Boss 与幻影塔替身按精确会话 `Find`，同时记录被请求的动作（动作名、会话、RunId）。所有动作都返回“未改变”，拾取返回“无空间”，使塔通道在第一次领域写入后就停下。通道归属由三项共同判定：哪座塔被请求了动作、写入了哪个工作单元、登记了哪些回包。替身在 `Record` 内断言只有当前精确成员才能请求塔动作，路由出错会直接失败。
- **普通通道判据**：普通通道没有专属领域写入时，用它自己的工作单元（`pick-up-ground-drop`）、回包（Empty、LoadingComplete、拒绝帧）或第一次读取当前视图来识别。
- **登记替身**：夹具已经提供 `IDungeonParticipationDomainService` 替身，按角色由塔与普通副本的状态推出登记，另有 `Residual` 表示残留登记。现行处理器不注入它，`Create` 会忽略多余的依赖。T-002 起处理器若注入它，这些用例不改断言即可继续运行。会话代次窗口因此能体现为“登记为塔、塔不认会话”。
- **队伍服务**：使用真实的 `PartyClientProjectionService`。组队通知需要线上队伍编号，裸的 `PartyService` 会在通知形状校验处抛异常。
- **Unit 标记**：全部用例都是内存替身，按 `Document/server-ci-release.md` 标注 `[Trait("Category", "Unit")]`。

### Checks

- `cd Server && dotnet build 86JP.Server.slnx`：0 警告，0 错误。
- Round 1：新增用例 119 个（含 Theory 展开），基准 aee80420 上全部通过：新鲜帧 67 个，陈旧帧 52 个。
- Round 2：用例共 155 个，全部通过。单人代次窗口由 2 格改为 22 格（+20），组队代次窗口新增 16 格；新鲜帧 103 个，陈旧帧 52 个。
- 变异检查：每次先备份源文件，改动后构建并只跑本测试类，然后还原。Round 2 起还原后立即 `touch` 被改文件，下一次构建必定重编。最后 `git diff --stat -- Server/src` 为空，并刷新时间戳重新构建。

| 变异 | 改动 | 失败用例 |
|---|---|---|
| M1 | `PickupIllusionAsync` 改为先查归属、后判陈旧 | 陈旧帧 0x002B(Town)、(Ordinary) |
| M2 | `HandleMutationAsync` 交换两塔协调顺序（先幻影后 Boss） | A_boss_seat_in_the_party_wins_over_an_illusion_seat |
| M3 | `HandleMutationAsync` 中 0x000E 幻影分支失败时不再发拒绝帧 | Removing_a_seated_member_from_town…(Illusion)；A_non_leaders_removal…(Illusion) |
| M4 | `SelectionPrepareBossAsync` 把 `return !released.Changed` 改为 `return true` | Enter_selection_continues_into_the_ordinary_selection_once_the_boss_run_is_released |
| M5 | 0x0029 的 Boss 拒绝分支永不进入 | Revival_coin_is_refused_only_by_a_boss_tower(Boss) |
| M6 | 0x0027 去掉 `ObjectCode is null` 前置，物件破坏改进幻影分支 | Object_destruction…(Illusion) |
| M7 | 0x0029 的 Boss 判定由 `Find(payer)` 改为 `IsInTower(characterId)` | A_tower_seat_held_by_an_older_generation…(Boss) |
| M8 | `BossTowerCapture` 只取调用者，不再看整队 | Leadership_given…(Boss)；Removing_a_seated_member…(Boss)；A_boss_seat_in_the_party…；Giving_up_from_town…(Boss)；A_member_leaving_from_town…(Boss) |
| M9 | `KillIllusionAsync` 改为先查归属、后判陈旧 | 陈旧帧 0x0027(Ordinary)、(Town) |
| M10 | 删除 `HandleMutationAsync` 入口的陈旧判定 | 陈旧组队帧 0x000D、0x000E、0x0079 × Ordinary、Boss、Illusion、Town，共 12 个 |
| X1（Round 2，审查者变异） | `HandleMutationAsync` 取到 caller 后加 `if (bossTowers.IsInTower(caller.CharacterId) && bossTowers.Find(caller) is null) return false;` | A_party_whose_only_tower_seat…(Boss, callerSeated=True, 0x000D/0x000E/0x0079)，共 3 个 |
| X2（Round 2，组队归属只信登记） | `HandleMutationAsync` 中 Boss 协调未接手时，只要队伍里任一成员 `bossTowers.IsInTower(角色)` 就当作 Boss 归属返回 | A_party_whose_only_tower_seat…(Boss, callerSeated=True/False, 0x000D/0x000E/0x0079)，共 6 个 |
| X3（Round 2，放弃只信登记） | `TryGiveUpTowersAsync` 中 Boss 协调未接手时，只要队伍里任一成员 `bossTowers.IsInTower(角色)` 就当作已处理 | A_party_whose_only_tower_seat…(Boss, True/False, 0x002A)；A_tower_seat_held_by_an_older_generation…(Boss, 0x002A)，共 3 个 |
| X4（Round 2） | 0x0027 外层 Boss 判定由 `Find(reporter)` 改为 `IsInTower(characterId)` | A_tower_seat_held_by_an_older_generation…(Boss, 0x0027) 与 (Boss, 物件破坏)，共 2 个 |

- Round 1 完整测试 `cd Server && dotnet test 86JP.Server.slnx --no-build`，退出码 0：

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Pvf.Tests | 73 | 9 | 0 |
| Dfo.Server.HttpApi.Tests | 59 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 655 | 348 | 0 |
| Dfo.Server.Protocol.Tests | 1866 | 60 | 0 |
| Dfo.Server.Networking.Tests | 189 | 20 | 0 |
| Dfo.Server.Persistence.Tests | 484 | 0 | 0 |
| Dfo.Server.Host.Tests | 200 | 12 | 0 |
| Dfo.Server.Application.Tests | 5512 | 493 | 0 |

  跳过项都是按既有规则在缺 PVF 等条件下跳过，不是本次改动引入的。

  第一次完整运行时 Protocol.Tests 失败 1 个，即 Revival_coin_is_refused_only_by_a_boss_tower(Boss)。归因：变异 M5 还原源文件时，文件修改时间早于变异后编出的 DLL，增量构建判定无需重编，测试实际跑的是残留的变异产物。与生产行为无关。刷新时间戳、重新构建后再跑完整测试，全部通过。
- Round 2：返工后刷新被变异文件的时间戳，`dotnet build 86JP.Server.slnx` 得 0 警告 0 错误，再跑完整测试，退出码 0。除 Protocol.Tests 外，各项目数字与 Round 1 相同。

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Pvf.Tests | 73 | 9 | 0 |
| Dfo.Server.HttpApi.Tests | 59 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 655 | 348 | 0 |
| Dfo.Server.Protocol.Tests | 1902 | 60 | 0 |
| Dfo.Server.Networking.Tests | 189 | 20 | 0 |
| Dfo.Server.Persistence.Tests | 484 | 0 | 0 |
| Dfo.Server.Host.Tests | 200 | 12 | 0 |
| Dfo.Server.Application.Tests | 5512 | 493 | 0 |

  Protocol.Tests 比 Round 1 多出的 36 个，正是新增的用例。
- 完整测试日志：
  - Round 1：`.rks-agent/tasks/dungeon-owner-routing/logs/task-001-full-test.log`
  - Round 2：`.rks-agent/tasks/dungeon-owner-routing/logs/task-001-full-test-round2.log`

### Remaining

- **T-002**：处理器注入登记能力后，现有用例里经 `ProtocolAlgorithmFixture` 构造、但没有提供登记服务的驱动需要适配（BD-004）。本 Task 的新夹具已经提供登记替身。
- **T-003～T-005 的检查要求**（Review Round 1 非阻断 2）：每个 Task 都要跑完整测试，不能只跑 `Category=Unit`。并在 Result 中列出塔通道内部行为所依赖的既有测试类，例如 Task44～Task49 的 real-wire 用例（`DungeonLoadingFailureBoundaryTests`、`MonsterDeathFailureBoundaryTests`、`GroundPickupFailureBoundaryTests`、`CharacterDeathFailureBoundaryTests`、`DungeonExitLifecycleTests`、`DungeonSelectionTowerBoundaryTests`、`DungeonSelectionFailureBoundaryTests`、`PartyTowerFootprintBoundaryTests`）、`BossTowerPublicHandlerTests`、`ExactTowerPhysicalClosureTests`。新夹具让塔动作在第一次写入后就停下，写入成功后的投影、Exact 失败时吞掉请求、0x0048 Boss 先 Capture 等通道内部行为，都只由这些既有用例保护。
- **工作单元声明名**（Review Round 1 非阻断 3）：本测试精确断言工作单元声明名（如 `pick-up-boss-tower-item`、`selection-release-boss-tower`、`party-synchronize-boss-leader`）。T-003～T-005 迁移通道时不得改名；如确需改名，应先由主代理认可，再同步修改这些断言。
- **变异检查脚本的时间戳问题**：还原文件后应 `touch` 再构建，避免残留产物。后续 Task 做变异检查时需要注意。

## Result Review Round 1

- 审查对象：Task Unit Contract Revision 1；执行结果为上方 `## Result`（Outcome: done）；改动为基准 HEAD aee80420 上未提交的 4 个新文件 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonOwnerRouting*.cs`。`git diff --stat -- Server/src` 为空。
- 依据：`reviewing-work` 的 Task Unit 与 Code 准则（Task 局部范围）；spec.md（位置定义、“位置 → 通道对照”、陈旧帧行为变化清单、AC-002、AC-004）；plan.md BD-002～BD-004、T-001；evidence/background-7adec858.md §1.3、§1.4、§2.3、§3；`Document/server-ci-release.md:113`。
- 独立核查：
  - 基线：构建 0 错误，本测试类 119 个用例全部通过。
  - Result 覆盖表列出的 44 个现有用例名逐一 grep，全部存在。抽查 `BossTowerPublicHandlerTests.Finish_loading_handler…` 和 `IllusionTowerDomainServiceTests.Cleared_floor…`，分别经 `GameMapLoadingCommandHandler.HandleAsync`、`MapLoadingRouteDriver` 从公开入口发起。
  - 重做变异 M3（删除 `HandleMutationAsync` 幻影分支的 `RemovePartyMemberRefusal`）。失败的正是 Result 所列 2 个用例，变异声明可信。
  - 自拟变异 X1：在 `GamePartyTransitionCommandHandler.Stages.cs` `HandleMutationAsync` 取到 caller 后加入 `if (bossTowers.IsInTower(caller.CharacterId) && bossTowers.Find(caller) is null) return false;`，模拟只信登记、不核对塔是否认可会话的错误定位。结果：本类、`PartyCommandHandlerTests`、`BossTowerProjectionConcurrencyTests`、`ExactTowerPhysicalClosureTests` 共 171 个用例全部通过；Application 中 `PartyTower*`、`DungeonDepartureProtocol*`、`DungeonPartyOwnedRun*` 等 76 个用例也全部通过（跳过 1 个）。
  - 每次变异前备份，检查后还原并 `touch`，Application 与 Protocol 测试项目都重新构建。最终 `git diff --stat -- Server/src` 为空，本类重跑 119 个全部通过。

```yaml
kind: rks-review
verdict: changes_required
```

- Verdict: changes_required
- Findings:

  阻断：
  1. **组队变更命令（0x000D/0x000E/0x0079）缺少会话代次窗口的表征；队员处于代次窗口的整队位置也没有覆盖。**
     - 现状：`DungeonOwnerRoutingCharacterizationTests.cs:290-324` 的代次窗口用例只发单人命令和单人 0x002A，`.Party.cs` 中没有任何代次窗口用例。Result 的 Remaining 也把这一格留到 T-005“如需要”再补。
     - 为什么必须现在补：Spec 的“整队位置”按候选逐个套用“位置”定义，代次窗口要落到“普通或不在副本”；AC-004 把“会话代次窗口”列为必须先于结构改动提交的表征。T-005 恰好要把这三个命令改为 `LocateParty`。
     - 已证实的后果：变异 X1 就是“组队定位只信登记”这种错误重构，它让 0x000D/0x000E/0x0079 在代次窗口中被塔吞掉、不再走普通流程，而现有全部相关测试照样通过。另外，队员（不是调用者）只由旧代次占着塔座时，任何命令都没有覆盖；错误的 `LocateParty` 会把整队送进塔通道。
     - 最小修改：在 `.Party.cs` 增加两组用例，夹具已有 `world.Reselect`，可直接使用。
       - (a) 调用者在窗口：组建两人队，把调用者 Seat 到 Boss 或幻影塔，然后 `Reselect` 调用者，由新代次分别发 0x000D、0x000E、0x0079。断言两塔都没有被请求动作、没有塔工作单元，并按普通流程断言队伍结果与回包（与对应 Town 格一致）。
       - (b) 队员在窗口：队长在城镇，队员 Seat 进 Boss 或幻影塔后再 `Reselect` 队员，队长发 0x0079（建议同时加 0x002A）。断言走普通流程：队长转移、两塔没有被请求动作、没有 `UdpHostNotification`。
       - 补完后，用 X1 或同类变异确认新用例会失败，并写进 Result 的变异表。

  非阻断：
  1. **代次窗口用例把 10 个命令放在同一个 Fact 中顺序发送**（`DungeonOwnerRoutingCharacterizationTests.cs:304-322`）。T-003～T-005 某个命令回退时，失败信息无法定位到具体命令；前面命令的副作用也可能掩盖后面的命令。建议改成按命令的 Theory，并补上 0x0027 物件破坏：Boss 代次窗口下现行回 Empty，错误定位到 Boss 时会被忽略。
  2. **塔替身让所有动作返回“未改变”，塔通道在第一次写入后就停下。** 写入成功后的投影与回包、Exact 失败时吞掉请求、0x0048 Boss 先 Capture 等通道内部行为，新用例都没有钉住，只能依靠 Result 覆盖表中的既有用例（Task44～Task49 real-wire、`BossTowerPublicHandlerTests`、`ExactTowerPhysicalClosureTests` 等）。这些用例都没有 Unit 标记，PR CI 不会运行它们。建议在 T-003～T-005 的 Required checks 中明确列出要跑的既有测试类，或者每个 Task 都跑一次完整测试，不能只跑 `Category=Unit`。
  3. **断言精确匹配工作单元声明名**（如 `"pick-up-boss-tower-item"`、`"selection-release-boss-tower"`）。合约允许断言“是否发生工作单元写入”，所以目前可以接受；但这会把迁移约束为不改声明名。建议在 T-003～T-005 计划中写明“通道迁移不改工作单元声明名”，以免正确的重构被误报为失败。
  4. **组队类无会话用例**（`.Party.cs:189-206`，陈旧矩阵中的 NoSession）经 `ComponentContext.For` 构造，其当前性判定恒为 `() => true`（`Server/tests/Dfo.Server.Protocol.Tests/Support/CardProtocolDrivers.cs:71-77`）。生产中 `request.DemandCurrent()` 早于陈旧判定（`GamePartyTransitionCommandHandler.Stages.cs:759-760`），对无会话连接的真实结果没有被钉住。这是既有夹具的惯例，迁移后断言仍然成立，只需在 Result 的证据边界中注明。
  5. **Result 写着完整测试输出“不入库”、保存在临时目录**，但 `.rks-agent/tasks/dungeon-owner-routing/logs/task-001-full-test.log` 已存在，内容即 full-test-2.log。建议在 Result 中引用它或删除它，与 Spec Success Evidence 要求的 `verification/` 位置保持一致。

- 已确认、无问题的方面：
  - 全部用例经 `ProtocolAlgorithmFixture.Create<Handler>` 加 `HandleAsync` 从公开入口发起，没有调用 internal 分支方法。
  - `Create` 按 `IsInstanceOfType` 匹配依赖（`Server/tests/Support/ProtocolAlgorithmFixture.cs:28-34`）。T-002 起处理器注入 `IDungeonParticipationDomainService` 后，会拿到夹具提供的登记替身。
  - 登记替身按角色推出，旧代次仍是塔成员时登记仍指向塔，忠实于生产“按角色、不含代次”的语义。`Residual` 能表达 W2。替身不模拟事务内的待定视图，但定位发生在事务之外（evidence §0），所以不影响结论。
  - 陈旧帧矩阵 37 + 15 格与 evidence §1.4 中“现行已丢弃”的格逐一对应，不含行为变化清单中的格。
  - 0x000E 两塔回包差异、0x000F 释放后续选图与未释放即结束、0x0027 物件破坏、0x0029、整队 Boss 优先等特殊格都有区分性断言，变异 M3 已复现。
  - `[Trait("Category","Unit")]` 符合规则：夹具全在内存中，没有 SQLite、PVF 或 socket，与已标 Unit 的 `PartyServiceTests`、`CommandRequestContextTests` 同类。
  - 没有发现时间或顺序依赖：每个用例新建 world，时间戳固定为 UnixEpoch。

## Result Review Round 2

- 审查对象：Round 1 意见返工后的 Result（含 Round 2 补充）与同 4 个未提交测试文件；基准 HEAD aee80420；`git diff --stat -- Server/src` 为空。
- 复核范围：Round 1 阻断项 1 及其修复带来的直接回归；非阻断 1～5 的处置。
- 独立核查：
  - `cd Server && dotnet build 86JP.Server.slnx`：0 警告，0 错误。本测试类 155 个用例全部通过。
  - 读了 `.Party.cs:208-263` 的 `A_party_whose_only_tower_seat_is_held_by_an_older_generation_takes_the_ordinary_flow`：Boss、幻影 × 调用者或队员被重选 × 0x000D、0x000E、0x0079、0x002A，共 16 格。断言两塔都没被请求动作、没有工作单元、没有回包，并按命令断言普通流程的队伍结果，与城镇格一致。Round 1 要求的“调用者在窗口”和“队员在窗口”两种情形都已覆盖。
  - 读了 `DungeonOwnerRoutingCharacterizationTests.cs:293-340`：单人代次窗口已拆成按命令的 Theory，共 22 格，含 0x0027 物件破坏。每格只发一个命令，并分别断言工作单元、回包和视图读取。
  - 重做变异 X2：在 `HandleMutationAsync` 中，Boss 协调没有接手时，只要队伍里有成员 `bossTowers.IsInTower(角色)` 就返回。失败 6 个，即 (Boss, callerSeated=True/False, 0x000D/0x000E/0x0079)，与 Result 声明一致。X1 已在 Round 1 由审查者构造，Result 记录的失败集合与该变异的作用范围相符。
  - 变异前备份，还原后 `touch` 并整解重建；`git diff --stat -- Server/src` 为空，155 个用例重跑全部通过。
  - `logs/task-001-full-test-round2.log` 中 8 个测试项目都是“已通过”，没有“失败!”，末行为 `exit=0`。
- 非阻断项处置：
  - 1：已修复，即上述按命令的 Theory，并补了物件破坏。
  - 2、3：已作为约束写进 Remaining，交给 T-003～T-005。
  - 4：证据边界已写进 Result。
  - 5：两份日志都已在 Result 中引用。

```yaml
kind: rks-review
verdict: approved
```

- Verdict: approved
- Findings:
  - 阻断：无。Round 1 阻断项 1 已解决。
  - 非阻断：无新增。Remaining 中的两条约束（迁移 Task 要跑完整测试并列出依赖的既有测试类；不改工作单元声明名）需要主代理在 T-003～T-005 的 Task 合约中落实。本 approved 只覆盖 TASK-001 的局部结果，不代表整体完成。
