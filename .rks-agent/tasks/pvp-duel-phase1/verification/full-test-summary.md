# TASK-08 全量测试汇总

- 源码：分支 `feature/pvp-duel-phase1`，HEAD `2f2bc04ef` 加 TASK-08 工作区改动（新集成测试与文档，无生产代码改动）。
- 构建：`dotnet build Server/86JP.Server.slnx -m:1`，0 警告 0 错误。
- 执行：解决方案内 9 个测试项目逐个顺序执行，`dotnet test <项目> --no-build -m:1 -- xUnit.MaxParallelThreads=1`，
  `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`，2026-11-14 22:14–22:54。
- 每项目日志：`full-test-<项目>.log`（计数、非通过用例与首行错误；原始结果为日志中列出的 TRX 文件）。
- 基线：`baseline-failed.txt`（`main@ef20398a3`，48 行；理论用例按名称前缀匹配）。

## 计数

| 项目 | 总数 | 通过 | 失败 | 跳过 | 基线外失败 |
|---|---|---|---|---|---|
| DbMigrate.Tests | 33 | 33 | 0 | 0 | 0 |
| Dfo.Pvf.Tests | 82 | 82 | 0 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 1066 | 1047 | 17 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 489 | 489 | 0 | 0 | 0 |
| Dfo.Server.HttpApi.Tests | 146 | 146 | 0 | 0 | 0 |
| Dfo.Server.Host.Tests | 327 | 325 | 1 | 1 | 0 |
| Dfo.Server.Protocol.Tests | 2488 | 2488 | 0 | 0 | 0 |
| Dfo.Server.Networking.Tests | 226 | 212 | 14 | 0 | 0 |
| Dfo.Server.Application.Tests | 6995 | 6960 | 27 | 8 | 1 |

失败合计 59（17 + 1 + 14 + 27）；跳过 11（2 + 1 + 8）为测试自身的条件跳过（未逐项核对；Host 那一项是既有的 Windows 不可用 Sigterm 用例）。

## 与基线对比

- 基线清单中每一行都仍失败（没有"基线失败现已通过"的条目）。
- 基线外失败 1 项：`Dfo.Server.Application.Tests.Inventory.PackageOpeningTests.Archive_package_referencing_missing_equipment_is_refused_without_consumption`
  （期望 `UnsupportedContent`，实际 `ItemUnusable`；单独复跑同样失败，确定性）。
  - 归因：与本任务无关，来自测试所用 PVF 在基线之后被替换。`E:\Project\DNF\DNFClient-A21-protocol\Script.pvf` 修改时间
    2026-11-14 21:00:06，SHA-256 `A97132DDA0970B3E0CE7C3DE8052ACF0BE2664F52E26C819B1D8CB6D427CFCD2`，晚于基线（16:15）且不是
    工作区说明登记的修订版 `e1f3a25c…`。该用例依赖 PVF 中礼包 10000606 与装备 102550441 的内容；本分支相对 `ef20398a3`
    没有改动 `Server/src` 下任何 Inventory、Opening、Package 或内容源（除两个新增 PvP 内容源）文件。
  - 未做：在 `ef20398a3` 上用当前 PVF 复跑以直接确认（需另建工作区重新构建）。
- 新增的 `PvpDuelWireTests` 4 项均在 Networking.Tests 中通过。

## 失败名单

### Dfo.Server.Infrastructure.Tests

- `Dfo.Server.Infrastructure.Tests.Content.PvfClearRewardRateSourceTests.The_closing_invariant_holds_here_and_in_the_monster_table_but_in_no_abyss_group`
- `Dfo.Server.Infrastructure.Tests.Content.PvfDarkElfRuinsContentSourceTests.Every_ruins_tier_has_enterable_ordinary_and_declared_guardian_topologies`
- `Dfo.Server.Infrastructure.Tests.Content.PvfDropItemPoolSourceTests.A_dropped_piece_carries_the_durability_its_own_file_declares`
- `Dfo.Server.Infrastructure.Tests.Content.PvfDropItemPoolSourceTests.The_equipment_pool_comes_from_the_equipment_files`
- `Dfo.Server.Infrastructure.Tests.Content.PvfEntryLifecycleContentSourceTests.DailyRefill_RealArchiveProducesAuthoritativeRulesWithStackLimits`
- `Dfo.Server.Infrastructure.Tests.Content.PvfExtraDropContentSourceTests.A21_extra_table_and_empty_tagged_pool_initialize_and_are_cached`
- `Dfo.Server.Infrastructure.Tests.Content.PvfGuardianAltarContentTests.Units_skills_towers_gage_and_achievements_come_from_their_own_tables`
- `Dfo.Server.Infrastructure.Tests.Content.PvfIndependentDropTableSourceTests.A21_complete_table_is_retained_including_unresolved_types`
- `Dfo.Server.Infrastructure.Tests.Content.PvfItemLifetimeTests.Actual_item_placement_exposes_the_declared_lifetime(id: 490002925, days: 0, daily: False)`
- `Dfo.Server.Infrastructure.Tests.Content.PvfNpcShopDefinitionSourceTests.The_two_sampled_npcs_resolve_their_actual_shop_pages`
- `Dfo.Server.Infrastructure.Tests.Content.PvfPremiumDurationTests.A21_contract_levels_and_short_term_products_are_read_from_the_archive`
- `Dfo.Server.Infrastructure.Tests.Content.PvfQuestDefinitionSourceTests.Projects_every_neutral_eligibility_input_from_the_real_archive`
- `Dfo.Server.Infrastructure.Tests.Content.PvfQuestEnemyRewardTests.A21_APC_object_and_repeated_candidate_declarations_are_preserved`
- `Dfo.Server.Infrastructure.Tests.Content.PvfQuestHelperTests.A21_helper_values_and_mixed_quest_requirements_keep_materials_separate`
- `Dfo.Server.Infrastructure.Tests.Content.PvfStackableActionDefinitionSourceTests.Batch_coupon_has_action_and_level_ceiling_independent_of_item_id`
- `Dfo.Server.Infrastructure.Tests.Content.PvfTeleportConsumableDefinitionSourceTests.Real_potion_selection_destinations_and_Shonan_gate_are_typed`
- `Dfo.Server.Infrastructure.Tests.Content.PvfWorldDropTableSourceTests.A21_archive_resolves_all_active_references_and_preserves_level_values`

### Dfo.Server.Host.Tests

- `Dfo.Server.Host.Tests.GmEventsHttpTests.Actual_A21_archive_uses_client_script_reward_order_and_delivers_declared_first_reward`

### Dfo.Server.Networking.Tests

- `Dfo.Server.Networking.Tests.Characterization.PartyCharacterizationTests.A_disconnect_in_town_announces_the_departure_to_the_other_player`
- `Dfo.Server.Networking.Tests.Characterization.PartyCharacterizationTests.A_party_entering_a_dungeon_produces_the_recorded_transcripts`
- `Dfo.Server.Networking.Tests.Characterization.PartyCharacterizationTests.Leaving_a_dungeon_produces_the_recorded_transcripts`
- `Dfo.Server.Networking.Tests.Characterization.PartyCharacterizationTests.Leaving_a_party_projects_to_both_players`
- `Dfo.Server.Networking.Tests.Characterization.PartyTowerCharacterizationTests.A_party_entering_the_tower_of_death_receives_the_recorded_transcripts`
- `Dfo.Server.Networking.Tests.Characterization.PartyTowerRunCharacterizationTests.A_defeated_party_run_settles_and_departs_with_the_recorded_transcripts(name: "Boss")`
- `Dfo.Server.Networking.Tests.Characterization.PartyTowerRunCharacterizationTests.A_defeated_party_run_settles_and_departs_with_the_recorded_transcripts(name: "Death")`
- `Dfo.Server.Networking.Tests.Characterization.PartyTowerRunCharacterizationTests.A_leader_leaving_mid_run_hands_the_host_over_with_the_recorded_transcripts(name: "Boss")`
- `Dfo.Server.Networking.Tests.Characterization.PartyTowerRunCharacterizationTests.A_leader_leaving_mid_run_hands_the_host_over_with_the_recorded_transcripts(name: "Death")`
- `Dfo.Server.Networking.Tests.Characterization.SelectCharacterCompensationCharacterizationTests.A_failed_expired_item_cleanup_delivers_the_whole_refusal_pair_before_the_close`
- `Dfo.Server.Networking.Tests.Characterization.SelectCharacterCompensationCharacterizationTests.A_failed_expired_item_cleanup_refuses_the_entry_and_closes_the_connection`
- `Dfo.Server.Networking.Tests.Characterization.SelectCharacterCompensationCharacterizationTests.A_failed_quest_synchronization_after_publication_compensates_and_closes_the_connection`
- `Dfo.Server.Networking.Tests.Characterization.SelectCharacterCompensationCharacterizationTests.A_failed_quest_synchronization_delivers_the_whole_entry_batch_before_the_close`
- `Dfo.Server.Networking.Tests.Characterization.SelectCharacterSuccessCharacterizationTests.Login_create_and_select_produce_the_recorded_transcript`

### Dfo.Server.Application.Tests

- `Dfo.Server.Application.Tests.Inventory.PackageOpeningTests.Archive_package_referencing_missing_equipment_is_refused_without_consumption`
- `Dfo.Server.Application.Tests.Quests.QuestMapClearTests.Actual_awakening_quest_975_targets_the_map_of_death_tower_floor_30`
- `Dfo.Server.Application.Tests.World.DarkElfRuinsTests.Entry_cost_and_daily_limit_are_atomic_persistent_and_reset_at_six`
- `Dfo.Server.Application.Tests.World.DarkElfRuinsTests.Licensed_star_selects_effective_difficulty_even_when_client_keeps_another_ordinary_tier(license: 2, requested: 0, cost: 20)`
- `Dfo.Server.Application.Tests.World.DarkElfRuinsTests.Licensed_star_selects_effective_difficulty_even_when_client_keeps_another_ordinary_tier(license: 2, requested: 4, cost: 20)`
- `Dfo.Server.Application.Tests.World.DarkElfRuinsTests.Licensed_star_selects_effective_difficulty_even_when_client_keeps_another_ordinary_tier(license: 3, requested: 0, cost: 40)`
- `Dfo.Server.Application.Tests.World.DarkElfRuinsTests.Licensed_star_selects_effective_difficulty_even_when_client_keeps_another_ordinary_tier(license: 4, requested: 0, cost: 80)`
- `Dfo.Server.Application.Tests.World.DarkElfRuinsTests.Monthly_quota_resets_at_the_first_daily_boundary_without_resetting_license`
- `Dfo.Server.Application.Tests.World.DarkElfRuinsTests.Party_admission_refusal_does_not_charge_any_member_and_success_commits_both`
- `Dfo.Server.Application.Tests.World.GiantDragonDungeonTests.Entry_plan_owns_each_quest_mode_and_directed_maze_policy(dungeon: 3536, quest: 20720, requirement: Cleared, directed: False)`
- `Dfo.Server.Application.Tests.World.GiantDragonDungeonTests.Entry_plan_owns_each_quest_mode_and_directed_maze_policy(dungeon: 3537, quest: 20722, requirement: Cleared, directed: False)`
- `Dfo.Server.Application.Tests.World.GiantDragonDungeonTests.Entry_plan_owns_each_quest_mode_and_directed_maze_policy(dungeon: 3538, quest: 20728, requirement: ActiveOrCleared, directed: False)`
- `Dfo.Server.Application.Tests.World.GiantDragonDungeonTests.Entry_plan_owns_each_quest_mode_and_directed_maze_policy(dungeon: 3539, quest: 20728, requirement: Cleared, directed: False)`
- `Dfo.Server.Application.Tests.World.GiantDragonDungeonTests.Entry_plan_owns_each_quest_mode_and_directed_maze_policy(dungeon: 3540, quest: 20727, requirement: Active, directed: True)`
- `Dfo.Server.Application.Tests.World.GuardianAltarLifecycleTests.All_36_ordinary_stage_difficulties_finish_with_independent_unlocks_and_72_stars`
- `Dfo.Server.Application.Tests.World.GuardianAltarLifecycleTests.Center_death_is_failure_and_death_rollback_does_not_kill_or_reward`
- `Dfo.Server.Application.Tests.World.GuardianAltarLifecycleTests.Energy_upgrades_summon_cost_cooldown_and_abandon_obey_the_run_transaction`
- `Dfo.Server.Application.Tests.World.GuardianAltarLifecycleTests.Entry_and_progress_rollback_together_and_daily_refill_preserves_extra_tickets`
- `Dfo.Server.Application.Tests.World.GuardianAltarLifecycleTests.Loading_timeout_fails_without_reward_and_departure_cleans_pending_callbacks`
- `Dfo.Server.Application.Tests.World.GuardianAltarLifecycleTests.Managed_disable_blocks_admission_and_old_selected_session_cannot_continue_or_mutate_progress`
- `Dfo.Server.Application.Tests.World.GuardianAltarLifecycleTests.Real_stage_runs_all_phases_pause_fences_old_callbacks_and_clear_awards_once`
- `Dfo.Server.Application.Tests.World.GuardianAltarLifecycleTests.Survival_retains_the_center_without_loading_again_and_consumes_one_reward_ticket_per_clear(extraTickets: 0, expectedRounds: 3)`
- `Dfo.Server.Application.Tests.World.GuardianAltarLifecycleTests.Survival_retains_the_center_without_loading_again_and_consumes_one_reward_ticket_per_clear(extraTickets: 100, expectedRounds: 99)`
- `Dfo.Server.Application.Tests.World.IllusionTowerDomainServiceTests.Apc_kill_grants_real_quest_950_material_once_inside_the_tower_transaction`
- `Dfo.Server.Application.Tests.World.IllusionTowerDomainServiceTests.Quest_950_drop_respects_active_task_cap_and_a_full_quest_page`
- `Dfo.Server.Application.Tests.World.IllusionTowerDomainServiceTests.Quest_material_kill_sql_failure_rolls_back_actor_experience_material_and_progress_then_retries`
- `Dfo.Server.Application.Tests.World.IndependentDropRuleServiceTests.A21_entire_cache_prewarms_with_one_warning_and_preserves_all_rows`
## 基线外失败的归因复核（主控）

- `PackageOpeningTests.Archive_package_referencing_missing_equipment_is_refused_without_consumption`：在本分支同一构建上把 `PVF_ARCHIVE_PATH` 指向登记的修订版 PVF `PvfBuilds/revised-20261113/Script.pvf`（SHA-256 前缀 `E1F3A25C9350`）单独复跑，通过。全量运行时用的 `DNFClient-A21-protocol/Script.pvf` 已于 2026-11-14 21:00:06 被替换为 `A97132DDA097…`。结论：该失败由 PVF 替换引起，与本分支无关。
