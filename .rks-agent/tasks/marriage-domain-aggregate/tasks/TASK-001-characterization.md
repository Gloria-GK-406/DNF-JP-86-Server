# TASK-001 — 婚姻行为表征测试与基线

## Task Unit Contract

- Revision: 1
- Outcome and references:
  - 把现有婚姻用例逐条对照 Spec 行为目录 B-01…B-16，形成覆盖对照表；
  - 为没有逐条覆盖的分支补表征测试；
  - 建立共享构造入口，供 T-003 切换接口时只改接线；
  - 产出基线运行记录 EV-001。
  - 对应 Plan T-001、Spec RQ-009 / AC-009 的前半部分。
- Owned boundary：
  - `Server/tests/Dfo.Server.Application.Tests/Marriage/` 下新增的测试文件和共享构造入口；
  - 如需覆盖 03D0 投影，可在 `Server/tests/Dfo.Server.Protocol.Tests/` 新增婚姻投影测试；
  - 本 Task 文件，以及 `.rks-agent/tasks/marriage-domain-aggregate/evidence/`。
  - **不得修改任何生产代码（`Server/src/**`），也不得修改现有测试的断言。**
- Required checks：
  - `dotnet test` 运行 Application.Tests 和 Protocol.Tests 中与婚姻相关的过滤集，全部通过；
  - `git status` / `git diff` 证明 `Server/src` 没有改动；
  - 新增用例在注释掉被测分支的关键判断时会失败。这一点只需对 2–3 条代表性用例手工确认一次，记录在结果里，**确认后恢复原状**。

## Detailed Plan

1. **覆盖对照**
   - 逐个读 `Marriage/` 下的 5 个测试文件和 `Inventory/MarriageConsumableProtectionTests.cs`，再对照 `Server/src/Dfo.Server.Application/Marriage/*.cs` 的每个分支。
   - 把每条用例映射到 B-01…B-16 的子行为，列出没有覆盖的分支。行为目录在 `spec.md`。
   - 对照表写进本文件的结果段，用表格，不抄代码。
2. **共享构造入口**
   - 新增 `Marriage/MarriageHarness.cs`（名字可调整），把现有 `MarriageProposalTests.Fixture`、`MarriageCeremonyTests.Fixture` / `MakeEligibleAsync`、`MarriageChurchTests` 的 `Reserved` 等准备步骤，以「按能力取服务」的方式暴露出来。例如 `Proposals(f)`、`Ceremonies(f)`、`Churches(f)`、`Weddings(f)`、`LovePoints(f)`、`Guestbook(f)`、`Quests(f)`、`Engagement(f)`，每个都返回当前接口。
   - 新测试一律经由它取服务，不直接 `GetRequiredService<IMarriage*DomainService>()`。
   - 可以复用现有 internal static helper；如果它们是 private，就在 harness 里另写等价的准备步骤，不改现有文件。
3. **补测试**：只补对照表里的缺口，不重复已经覆盖的行为。从代码现状看，下列是需要优先核对的候选，以第 1 步的对照结果为准：
   - **B-04 / 03D0**：无关系、Pending、Engaged、Reserved、Married 各自的 03D0 state 字节（`MarriageEngagementSlice` 的投影），以及快照里的 MarriedAt 和 LovePoints。
   - **B-08**：`AppearanceAsync` 的返回条件：会话不是当前会话、未入场、跨频道、婚礼已结束且人不在房间。
   - **B-09**：请柬 -1（会话不当前、mode>1、截止后）、-4（额度用尽）、-5（重复邀请）、-6（邀请新人本人）、-7（没有可邀请对象）；随机模式的派送数量不超过剩余额度；`RemainingAsync` 按邀请人计算。
   - **B-10**：礼金低于下限或高于上限返回 -1；已付后相同金额返回 0、不同金额返回 -1，且只扣一次款；宾客 03F7 入场时 area 不符返回 -10、未付礼金返回 -6。
   - **B-11**：礼金分成 `(总额×(100-抽成)/100)/2` 发给两位新人；宾客按档位回礼（取 `MaximumGold ≥ 礼金` 的最小档位）。
   - **B-12**：取消时退给申请人的婚礼费用，以及退给每位宾客的礼金邮件，金额正确。
   - **B-13**：清理重试：
     - 已有新婚礼邀请的角色，旧清理不发投影，直接完成；
     - 当前处在另一场婚礼外观中的角色也一样处理；
     - 成功婚礼时人在场内，不进入 Cleanup；
     - 取消婚礼时人在场内，进入离场列表；
     - 房间无人时释放房间，并改写离线角色的坐标。
   - **B-14**：心意点 -1（不在随身栏、非心意点道具、账号列表不匹配）、-2（超上限）、-3（未订婚或未婚）。
   - **B-15**：祝词簿访问矩阵：
     - 新人可读，可删自己关系下的条目，不能写；
     - 已入场宾客可写；
     - 同频道但未入场的宾客可读、不能写；
     - 婚礼已取消时宾客无权访问；
     - key 为 0 或越界返回 -1；
     - 文本空、超过 20 字节、含控制字符时返回 -1；
     - 达到 100 条后返回 -1。
   - **B-16**：试炼计数的事件过滤：非 boss 房、`RunId` 为空、参与人数不是 2、参与者不是这一对、非普通图、接受时间晚于通关时间，以上情况都不计数。
4. **记录现状怪癖**：如果发现看起来不对的行为，照现状写断言，在本文件结果段的「现状怪癖」列表里登记（行为、位置、为什么看起来不对），留给 T-004 或后续判断。测试代码里不加解释性长注释。
5. **基线**：运行婚姻相关过滤集，把测试数量、通过数和新增用例名单写到 `evidence/TASK-001-baseline.txt`，作为 EV-001。

## Result

### Changed

新增文件（全部在 `Server/tests/Dfo.Server.Application.Tests/Marriage/`，未改任何现有文件）：

| 文件 | 内容 |
|---|---|
| `MarriageHarness.cs` | 共享构造入口：按能力取服务（`Proposals` / `Ceremonies` / `Churches` / `Weddings` / `LovePoints` / `Guestbook` / `WeddingQuests` / `Engagement` / `TrialClears`）；准备步骤（`ChurchTownAsync`、`EligibleAsync`、`ReserveAsync`、`PreparedAsync`、`AddCharacter`、`InviteAsync`、`PaidGuestAsync`、`Arrive` / `MoveTo` / `GoOffline`）；`InTx` 事务包装；`WeddingInfoAsync` 产出当前 03D0 的 15 字节 body |
| `MarriageRequestCharacterizationTests.cs` | B-01 / B-02 / B-03，9 条 |
| `MarriageEngagementCharacterizationTests.cs` | B-04 / B-16，4 条 |
| `MarriageChurchCharacterizationTests.cs` | B-05 / B-06 / B-08，3 条 |
| `MarriageWeddingCharacterizationTests.cs` | B-09 ~ B-13，6 条 |
| `MarriageCoupleCharacterizationTests.cs` | B-14 / B-15，2 条 |

新增用例共 24 条（9 + 4 + 3 + 6 + 2），全部经 harness 取服务，没有直接 `GetRequiredService<IMarriage*DomainService>()`。

覆盖对照表（已有用例名省略类名后缀 `Tests`，所在类写在括号里；新增用例所在类见上表）：

| 行为 | 已有用例 | 已有覆盖的缺口 | 新补用例 |
|---|---|---|---|
| B-01 求婚 | (Proposal) Acceptance_is_persistent_symmetric_and_replay_does_not_duplicate_relation、Refusal_and_client_timeout_release_both_slots×2、Rollback_removes_relation_and_both_participant_reservations、Only_invited_exact_generation_can_answer_and_pending_pair_cannot_be_reversed、Server_expiry_cannot_accept_and_a_new_request_can_claim_slots_again、Same_gender_wrong_town_and_self_requests_do_not_create_rows、Leaving_town_prevents_acceptance_but_does_not_prevent_refusal、Concurrent_cross_proposals_through_real_command_pipeline_commit_only_one_relationship | 只断言了 Ineligible 一个失败码，其余只断言 `Succeeded=false`；到期后答 2、到期后答 0、会话被替换导致的惰性到期、接受时复核失败后的状态均未断言 | Proposal_guards_answer_their_own_failure_codes、Acceptance_rechecks_town_and_eligibility_without_closing_the_proposal、Expired_proposal_answered_with_timeout_is_recorded_once、Expired_requests_answered_with_acceptance_are_stale_yet_recorded_expired、Pending_proposal_lapses_when_a_participant_session_is_replaced |
| B-02 预约 | (Ceremony) Eligible_pair_reserves_once_without_spending_gold_and_restores_after_reopen、Reserved_attempt_rejects_changed_reply_and_changed_request、Expired_attempt_is_idempotent_and_releases_relation_for_a_new_request、Refusal_and_client_timeout_release_relation_for_a_new_request×2、Rolled_back_request_leaves_no_ceremony、Concurrent_cross_requests_through_command_bus_commit_only_one_pending、Failed_acceptance_recheck_expires_pending_and_releases_relation_immediately、Reconnected_participant_expires_old_pending_before_new_request、Invalid_content_quest_binding_gold_session_and_target_do_not_reserve；(CeremonyHandler) Ordinary_reservation_request_acknowledges_and_notifies_the_invited_partner | 未订婚 / 非一对 → Ineligible、跨区 → Unavailable、自己对自己 → Invalid、response>2 → Invalid；接受时复核 Unavailable / Ineligible 转过期；到期后首次答 2 | Reservation_guards_answer_their_own_failure_codes_and_failed_rechecks_expire、Expired_request_answered_with_timeout_is_recorded_once、Expired_requests_answered_with_acceptance_are_stale_yet_recorded_expired（与 B-01 共用）；已婚 → Ineligible 在 Engagement_snapshot_profiles_the_partner_and_keeps_reserved_false_once_married 中 |
| B-03 接受预约 → 婚礼 | (Church) Wedding_preparation_charges_once_and_admits_both_in_one_transaction、Preparation_rollback_restores_fee_room_admission_and_positions、Both_partners_share_one_persistent_room_and_preparation_does_not_charge_or_marry、Native_cinema_names_are_bride_then_groom_for_either_applicant×2；(CeremonyHandler) 同上 | 未 Reserved / 金币不足 / 对方离线时 PrepareAsync 拒绝且不留痕；两条入场信息的 peer 互指；已付费额 | Wedding_preparation_refuses_pending_unpaid_or_offline_ceremonies_and_leaves_nothing、Wedding_preparation_records_paid_fee_and_gives_each_spouse_the_other_as_peer |
| B-04 快照与 03D0 | (Engagement) Pending_has_no_menu_state_and_cannot_accept_wedding_by_raw_ID、Engagement_offers_only_first_trial_and_restores_both_profiles_after_session_loss_and_database_reopen；(Ceremony) Eligible_pair_reserves_once…（IsCeremonyReserved）；(Church) Opening_time_commits_marriage_only_with_both_present_and_settlement_is_once（IsMarried / MarriedAt）、Love_coupon_requires_marriage_consumes_once_updates_shared_points_and_rolls_back（LovePoints）；Protocol.Tests `MarriageProposalCodecTests` 覆盖 snapshot → 字节的 0/1/2/4 编码 | 从领域状态到 03D0 字节的端到端对照（无关系、Pending、Rejected、Engaged、Requested、Reserved、Preparing、取消后、Married）；伙伴资料字段 | Wedding_info_state_byte_follows_each_relationship_stage、Engagement_snapshot_profiles_the_partner_and_keeps_reserved_false_once_married |
| B-05 新人 03F7 入场 | (Church) Entry_requires_exact_partner_church_area_and_flag_and_admission_is_idempotent、Expired_preparation_does_not_extend_when_partner_relogs、Stale_generation_cannot_use_the_room、A_reserved_payload_bound_to_another_pair_cannot_allocate_or_reuse_a_church、Rolled_back_preparation_and_entry_leave_no_room_or_access_grant | 只断言 `Succeeded=false`，未区分 -10 与 -7；跨频道 | Newlywed_entry_answers_minus_ten_for_mismatches_and_minus_seven_after_opening |
| B-06 进入教堂区域 | (Church) Unreserved_characters_cannot_prepare_or_cross_into_any_church_area、Two_reserved_couples_get_distinct_rooms_and_cannot_cross_into_each_others_room、Entry_requires_exact_partner…、Final_minute_locks_active_wedding_but_not_completed_departure | 宾客分支（按入场许可；过开场时间后只认场内）；婚礼完成后新人与宾客只在场内为真 | Crossing_admits_guests_by_admission_and_completed_weddings_only_from_inside |
| B-07 离开教堂 | (Church) Final_minute_locks_active_wedding_but_not_completed_departure（61/60/59 秒边界、同区、完成后） | 无 | — |
| B-08 外观快照 | (Church) Final_minute_locks…（筹备中场外同频道可取）、Native_cinema_names…（准备阶段名字顺序） | 会话不当前、跨频道、未入场、完成后不在场、宾客 key、AppearanceAsync 自身的名字顺序 | Wedding_appearance_depends_on_session_admission_channel_and_phase |
| B-09 请柬 | (Church) Invitations_and_gifts_are_durable_idempotent_and_end_before_opening（成功、剩余 10→9；重复与截止只断言非 0） | -1/-4/-5/-6/-7 各码；随机派送数量；剩余额度按邀请人；截止边界 -61/-60 | Invitation_status_codes_per_inviter_quota_random_dispatch_and_cutoff |
| B-10 礼金与宾客入场 | (Church) Invitations_and_gifts…（已付后同额幂等、只扣一次、未付 -6） | 上下限、已付后不同金额、金币不足、错误 key、跨频道、开场后、area 不符 -10 | Gift_payment_bounds_idempotency_and_guest_entry_codes |
| B-11 到点完成 | (Church) Opening_time…(both=true)（站位、MarriedAt、完成后重试为 null）；Current_PVF_wedding_completion_delivers_one_real_box_per_spouse_once×2（回礼物品，本机无 PVF 被跳过） | 礼金分成公式、宾客按档回礼、新人礼物经 MailOnly 投递（非 PVF）、宾客入场条目、清理排队 | Completion_splits_gifts_returns_bracket_rewards_and_queues_every_cleanup |
| B-12 到点取消 | (Church) Opening_time…(both=false)（退婚礼费、离场 1、清理 1）、Cancellation_moves_offline_admitted_character_out_of_persisted_church、Invitations_and_gifts…（宾客退款） | 预约转 Expired、在线场内角色存档坐标改写、离场列表内容、清理名单、剩余额度归 0 | Cancellation_expires_ceremony_refunds_each_payer_and_moves_saved_church_positions_out |
| B-13 清理重试 | (Church) Opening_time…(both=false)（取消后场内者进离场列表、无人时释放房间、队列持久、CompleteCleanup） | 有新婚礼邀请 / 处在另一场婚礼外观中的角色直接完成；成功婚礼场内不发 03CF，等空房再释放并改写存档坐标 | Cleanup_retry_completes_entries_owned_by_a_newer_wedding_without_projection_and_releases_the_empty_room、Cleanup_retry_after_completion_waits_for_an_empty_room_then_moves_saved_positions_out |
| B-14 心意点 | (Church) Love_coupon_requires_marriage…（成功；账号列表不符和超上限只断言非 0）；(Inventory) MarriageConsumableProtection 三条（普通道具使用路径拒绝心意券） | -1/-2/-3 各码分因；成功时的点数与对方角色 | Love_point_refusals_answer_minus_one_two_or_three_by_cause |
| B-15 祝词簿 | (Church) Invitations_and_gifts…（场外不能写、场内写、21 字节拒、新人不能写、新人读、宾客不能删、新人删） | key 为 0 / 越界、新人用自己 id、同频道未入场宾客可读不能写、陌生人、空文本、控制字符、20 字节边界、100 条上限、取消后宾客无权 | Guestbook_access_matrix_text_limits_and_capacity |
| B-16 婚前任务 | (Engagement) Pending_has_no_menu_state…、Engagement_offers_only_first_trial…、Five_distinct_ordinary_pair_boss_clears_are_durable_and_client_reports_cannot_finish_trial（非普通副本、非 boss、接受晚于通关、人数 1、非这一对）、Repeated_acceptance_preserves_progress_binding_and_clear_deduplication_after_reopen、Repeated_acceptance_cannot_repair_or_bypass_invalid_relationship_binding×4、Give_up_then_accept_is_a_new_trial_with_a_fresh_clear_ledger、Current_PVF_trial_hand_in…（跳过） | `EligibleQuestIdsAsync` / `CanAcceptAsync` 直接口径（账号不符、非婚礼任务、材料任务前置）；事件过滤：RunId 为空、同一角色两次、任一参与者账号不符、同副本非普通 map | Quest_eligibility_follows_the_engaged_owner_only、Trial_clears_from_an_empty_run_repeated_or_foreign_participants_or_odd_maps_are_not_counted |

### 现状怪癖

| # | 行为 | 位置 | 为什么看起来不对 | 是否有用例钉住 |
|---|---|---|---|---|
| Q1 | 求婚到期后用 response≠2 答复：库里已转 Expired，但返回 `Stale`、`Changed=false`、`Proposal=null`；预约同场景返回 `Stale`、`Changed=true` 并带出过期后的预约 | `MarriageProposalDomainService.cs:49-54`；`MarriageCeremonyDomainService.cs:64-70` | 同一种「到期落库」两边报告口径不同，求婚一侧还把已发生的写报告成未变 | Expired_requests_answered_with_acceptance_are_stale_yet_recorded_expired |
| Q2 | 婚礼完成后预约行一直停在 Reserved（state=1），没有任何迁移；`IsCeremonyReserved` 靠 `!married` 掩盖 | `MarriageWeddingDomainService.cs:140-167`（完成分支不碰预约）；`MarriageEngagementDomainService.cs:33` | 「已预约」与「已婚」同时成立，任何只看预约 state 的读法都会把已婚当成已预约 | Engagement_snapshot_profiles_the_partner_and_keeps_reserved_false_once_married（断言 state=1） |
| Q3 | 取消分支对预约做 `TransitionAsync(…Expired…)` 时忽略返回值；revision 不符时婚礼照样取消、照样退款，预约留在 Reserved | `MarriageWeddingDomainService.cs:170` | CAS 失败被静默吞掉，会留下「已取消的婚礼 + 仍 Reserved 的预约」 | 无（读代码得出，未构造 revision 不符的场景） |
| Q4 | 祝词簿的新人判定只比角色 ID，不比账号；心意点按 `FindActiveAsync(actor.CharacterId)` 找关系，也不比账号 | `MarriageGuestbookDomainService.cs:15-24`；`MarriageLovePointDomainService.cs:27`、`:34` | 与 Spec 对「一对」的定义（账号 + 角色）不一致，即 RQ-010(a) 的修正对象 | 无（需伪造参与者账号，留给 T-004 的对照测试） |
| Q5 | 宾客礼金高于所有回礼档位的 `MaximumGold` 时拿不到任何回礼 | `MarriageWeddingDomainService.cs:162-165` | 付得越多反而没有回礼；是否真会发生取决于 PVF 档位是否覆盖到礼金上限 | Completion_splits_gifts_returns_bracket_rewards_and_queues_every_cleanup（75 号付 60000，无投递记录） |
| Q6 | 求婚接受时复核失败（Unavailable / Ineligible）保持 Pending；预约接受时复核失败则立即转 Expired | `MarriageProposalDomainService.cs:56-57`；`MarriageCeremonyDomainService.cs:72-85` | 两段流程对「接受时复核失败」的处理不对称；Spec 迁移表只对预约（T7）写了转回 | Acceptance_rechecks_town_and_eligibility_without_closing_the_proposal；Reservation_guards_answer_their_own_failure_codes_and_failed_rechecks_expire |
| Q7 | `LovePoint.UseAsync` 收到没有游戏映射的业务槽位（实测 `QuickSlot/20`）时抛 `InventorySlotMappingException`，不返回 -1 | `MarriageLovePointDomainService.cs:38`（经 `inventory.Find`） | 其余条件不符都返回 -1；若协议解码只会产出已映射槽位，则线上碰不到 | 无（开发过程中一次中间运行观察到该异常；最终用例改用已映射的空槽位，未保留该断言） |
| Q8 | 清理重试释放房间时，按存档坐标改写所有仍在队列里的角色，包括此刻在线、运行时已站在别处的角色（存档改成出口，运行时仍在城镇 1 区） | `MarriageWeddingDomainService.cs:236-241` → `PersistExitAsync` | 存档与运行时位置短暂不一致；下次运行时落盘会覆盖，可能无害 | Cleanup_retry_after_completion_waits_for_an_empty_room_then_moves_saved_positions_out（71 号在线，存档 area=2） |

### Why

- **03D0 投影放在 Application.Tests，用反射调用 Protocol 的 internal `MarriageEngagementPayload.Info`。** Protocol 的 internal 只对 Protocol.Tests 可见，而真实 DI + SQLite 的 `LearningFixture` 只在 Application.Tests。要从真实领域状态一路断言到 15 字节 body，只能二选一；反射集中在 harness 的 `WeddingInfoAsync` 一处，T-003 若改投影入口只改这里。因此没有在 Protocol.Tests 新增文件。
- **B-11 用记录型 `IItemDeliveryDomainService` 替身。** 真实投递需要 PVF 物品放置定义，现有 PVF 回礼用例在本机被跳过；替身只记录（角色、物品×数量、来源、模式），让分成与回礼档位在无档案环境下也被钉住。
- **harness 的任务服务取名 `WeddingQuests`。** 叫 `Quests` 会与测试项目已有命名空间 `Dfo.Server.Application.Tests.Quests` 冲突（CS0118）。
- **现有测试一律未动。** 它们仍直接 `GetRequiredService<IMarriage*>()`；T-003 切换接口时这些文件也要按 AC-009 的「只改类型名和接线」处理，harness 只覆盖新增用例。

### Checks

- 构建：`dotnet build Server/86JP.Server.slnx` → 0 个警告，0 个错误。
- Application.Tests 过滤集：`dotnet test Server/tests/Dfo.Server.Application.Tests --no-build --filter "FullyQualifiedName~Marriage" --logger "console;verbosity=detailed"` → 测试总数 77，通过 74，跳过 3（均为无 PVF 档案的 Skippable），失败 0。新增前同一过滤集为 总计 53 / 通过 50 / 跳过 3，差值 24 = 新增用例数。
- Protocol.Tests 过滤集：`dotnet test Server/tests/Dfo.Server.Protocol.Tests --no-build --filter "FullyQualifiedName~Marriage" --logger "console;verbosity=detailed"` → 测试总数 36，通过 36。
- `Server/src` 无改动：变异全部恢复后，`git diff --stat Server/src` 与 `git status --short Server/src` 输出均为空；每次恢复后另用 `cmp` 与变异前备份逐字节比对一致。
- 变异确认（每次改一处 → 构建 → 跑对应新类 → 恢复）：
  1. `MarriageWeddingDomainService.cs:157` 把 `(100 - rule.GiftChargePercent)` 改成 `100` → `Completion_splits_gifts_returns_bracket_rewards_and_queues_every_cleanup` 失败（Expected 31500，Actual 45000），同类其余 5 条通过。已恢复。
  2. `MarriageWeddingDomainService.cs:224` 把 `hasNewInvitation ||` 改成 `false && hasNewInvitation ||`（直接删除会触发 CS0219，被 TreatWarningsAsErrors 拦下，故改为短路）→ `Cleanup_retry_completes_entries_owned_by_a_newer_wedding_without_projection_and_releases_the_empty_room` 失败（Expected [71, 72]，Actual [71, 72, 75]），同类其余 5 条通过。已恢复。
  3. `MarriageGuestbookDomainService.cs:42` 去掉 `|| x == 127` → `Guestbook_access_matrix_text_limits_and_capacity` 在 0x7F 那一行失败（Expected -1，Actual 0）。已恢复。
- 基线记录：`evidence/TASK-001-baseline.txt`（EV-001）。

### Remaining

- B-07 没有新增用例：现有 `Final_minute_locks_active_wedding_but_not_completed_departure` 已覆盖 61/60/59 秒边界、同区移动与完成后放行。
- 3 条 PVF 用例在本机被跳过，其覆盖（真实回礼箱、试炼交付）本次未运行。
- 未覆盖：Q3 的 CAS 失败路径、Q4 的账号不符（需构造伪关系，属 T-004 的对照测试）、祝词簿跨关系删除（需要第二对新人的条目）。
