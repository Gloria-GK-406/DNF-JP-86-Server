---
kind: rks-verification
decision: pass
---

# TASK-65 验证记录

本记录判定 TASK-65 第一阶段迁移删除与五类迁移账闭合已完成；保留两条精确既有 Dimension 内容失败和18条既有跳过，不声称全仓零失败、A21实机、专项性能、第二阶段或B封存完成。

删除范围为两条无生产消费者 fine、旧路由及细粒度入口、五层 pipeline、六处请求 AsyncLocal，以及 TASK06 的 IChannelEntryPolicy 临时别名。生产只保 GameplayJob.Running 和 DomainTransactionScope.ambient；显式请求、真实事务及每段工作单元不变。未注册消息仍经固定 CommandJobEntry 技术拒绝作业，保真实来源、初始化、blocked／superseded 静默和原 lookup 失败；它不执行 handler 或接收回调，已注册消息误入必须失败。

日志按原执行器归属收口：四处计划任务逐角色报告和 SelectedSessionSafety 的 Unknown settlement blocked 降为无异常栈 Warning，定位字段保留；whole 队列完成桥仍抛原 Aggregate，最终执行器保原 cause 的一条 Error。DiscardExact 自身独立且被隔离的清理失败 Error、吞错并返回业务结果的独立 Error 保留，不作全局日志删除。

## 旧测试的实际去向

| 原测试／规则 | 当前守护 |
|---|---|
| LegacyEffectFootprintTests 三条单例、回滚后下一请求、无账本合法来源 | ExplicitFootprintLifecycleTests 保原库存槽位、Rental needs、事务提交／回滚次数和每次不同 recorder；改真实协议 CMD／可信内部入口。 |
| Required_cap_parameters_and_no_ambient_constructor_dependencies_are_enforced_with_bad_shapes | 同名方法迁 ExplicitFootprintLifecycleTests，保 required 参数／缺参与 optional 反例及 domain 构造器能力拒绝；已删类型本身不再编造可注入性。 |
| Unknown_executor_or_network_effect_without_operation_cannot_obtain_non_request_authority | ProtocolNonEffectFootprintTests.Wrong_source_or_owner_cannot_reach_a_real_gold_write、A_missing_request_cannot_open_a_nonrequest_unit；DeclaredNonRequestFootprintTests.Declared_mode_does_not_grant_another_source_or_completion_capability。 |
| Rejected_capture_stays_rejected_after_a_later_selected_scope_even_for_empty_slots_and_rental；Refusal_only_unselected_operation_cannot_pay_after_a_selection_arrives | ExplicitEffectCommandTests.A_selection_arriving_after_rejected_admission_cannot_acquire_a_main、An_unselected_request_only_acknowledges_and_does_not_consume_the_ledger_ordinal；保原真实资产／ordinal无消费。 |
| Captured_state_is_checked_at_each_record_instead_of_a_boolean_snapshot；旧 effects authority 中 operation／execution／admission／lease 分帧模式 | ExplicitEffectExecutionTests.Captured_effects_authority_rejects_before_gold_even_for_an_empty_effect_list 改为真实 owner／transaction／frozen／closed request／authorization／ended Main 六状态，保空和非空效果均拒绝、SQLite gold不变；旧帧维度不以重复 EndMain 调用凑数。 |
| A_cap_cannot_be_used_after_its_job_ends_even_if_its_local_lease_was_not_disposed；A_live_cap_does_not_belong_to_another_live_job | ProtocolNonEffectFootprintTests.The_capability_ends_with_the_action_before_prepare_and_cannot_revive_after_close、A_live_capability_on_a_foreign_job_cannot_write_and_the_owner_is_drained；DeclaredNonRequestFootprintTests 的 ended action／foreign job 与真实SQL回滚／owner drain 反例。 |
| Default_context_and_record_copies_cannot_forge_live_legacy_admission；New_typed_commands_cannot_use_any_legacy_entry_and_catching_it_cannot_enable_a_write | Legacy admission／bridge API已删除；LockRemovalSourceTests 的实际源码删除名单及其通用违规反例拒绝重引入；ProtocolNonEffectFootprintTests 缺 request 与 sticky guard、ExplicitEffectCommandTests 的真实request来源／阶段守卫保不能catch后写或成功。 |
| An_already_obtained_cap_refuses_the_real_domain_entry_before_its_first_business_delegation | ExplicitEffectExecutionTests.Expiry_inside_consumption_after_real_read_prevents_take_and_preserves_committed_storage、Expiry_during_apply_preflight_or_validation_prevents_the_first_business_write、Expiry_inside_Heras_apply_quota_read_prevents_Direct_and_committed_bonus_write 保实际后备读取／零delegation／真实持久资产；lease已结束不能入domain。 |
| Closed_authorization_or_operation_cannot_be_laundered_by_detaching_the_job；旧 F8／F9 | F8明确只证误用 SendAsync 提交内部类型且无origin被拒、无第二handler；F9证真实 GameplayLeakedFlowException、无第二handler／入队；生产 CMD 不得detach由 DetachmentSiteTests 的去注释源Regex白名单和六反例承担，不冒称IL扫描或能追踪任意已detach内部来源。 |
| SelectedSessionExecutionBehaviorTests 五项 | ExplicitEffectCommandTests.Invalid_main_or_footprint_authority_never_changes_real_storage 的 MissingReporter／WrongOwner，A_main_can_report_another_accounts_exact_participant_and_default_failure_fences_both；CommandWorkUnitsTests.A_nonreporting_participant_that_is_no_longer_current_stops_the_unit_before_its_action 明确保声明[First,Second]而Second不current时action前拒绝、handled空；加回Second后相同声明恰1次成功；该类另保多精确参与者和回滚不确定保护。隐式 selected scope 本身删除，recipient 成为显式输入。 |
| EffectOperationReplayTests.Normal_settlement_requires_a_transaction_and_either_confirmed_rollback_or_frozen_projection | 删除旧帧状态机表；ExplicitEffectCommandTests 的 writable Main／minimum projection／未claim拒绝／ConfirmedRollback同entry同ordinal重试，以及 CommandWorkUnitsTests 的真实事务完成边界承担；不把可手调的旧frame状态当生产证明。 |
| EffectOperationReplayTests.An_inherited_readonly_scope_does_not_clear_the_primary_execution_on_the_parent_flow | 改显式 ordinary read-only unit，调用既有 AssertOrdinaryReplayReadPreservesMainAsync：原 projection对象／主Committed状态／当前库存回放不变。 |
| EffectOperationReplayTests 三个真实SQL／postcommit故障 | 改真实协议CMD＋Main work unit，保gold回滚／持久值／原cause／精确fence；已提交后请求关闭仍Committed，不能伪称回滚重付。 |
| CommandBusTests.Pipeline_ExecutesBehaviorsInOrderAroundTheTerminalHandler、EveryProductionBehaviourResolvesForACommandResultType；UnitOfWorkBehaviorTests.UnitOfWork_OpensInsideTheSelectedSessionSettlementAndOutsideTheExecutionPolicy；CommonExperience旧marker分类 | 删除不存在的pipeline顺序／类别表；实际unit事务／Prepare／Commit／Rollback／cause／精确参与者和注入正反例保留，LockRemovalSourceTests 禁止旧pipeline及marker重现。 |
| CommandBusExecutorTests.F2 inline、旧logging类别 | F2改真实executor上的nested sticky拒绝、零inline／零入队；日志改验执行器 completed／failed及链，不保删除的执行类别。 |
| MarriageProposal Concurrent_cross_proposals...；LeaveDungeon Exit_command_ends_the_run_through_the_real_pipeline | 迁同名功能的 explicit_domain_units／explicit_domain_entry；保原 Task.WhenAll 真并发、唯一关系／pending／预约／各阶段一wedding与 runs.Ended／EndedCharacters，真实bus从执行器外携显式Internal origin。 |
| ChannelEntry Formal alias singleton | Formal_channel_boundary_is_the_registered_concrete_singleton 保正式接口与原具体类同singleton。 |
| T6 dispatcher手工初始化 | 两个dispatcher实际不再Begin／Current或构造GameplayWork；固定技术入口的unknown失败／已注册误用、现有empty source／rootless、blocked physical对照，以及源形状规则三个反例。 |

域算法 fixture 使用 test-only internal origin、明确事务种类、实际 ICommandWorkUnits 和显式参与者／足迹；不作为生产协议admission或线级证据。原 wire fixtures 的自定义handler改为测试typed CMD，观测器识别实际CMD类型名，原字节、顺序、waiting上限、FIFO与不交错断言不变。所有协议only bus doubles显式拒绝未支持的内部入口，不保生产接口默认legacy适配。

旧频道别名删除后，原 ResolveAsync／Refuse 原签名及注释直接归现有 IChannelEntryDomainService；六个实际消费者仍使用原 ChannelEntryDomainService singleton，未新增业务或注入类别。正式边界／登录／会话／经验聚焦57/57通过，最终仍以冻结后八全量为准。

## 剩余错误日志的语义分类

| 类别 | 实际去向与保留理由 |
|---|---|
| 作业体与明确入队拒绝的机制权威 | GameplayExecutor 保原最终 body／被采纳的end-hook cause及完整链的一条Error；未入队深度超限／自等的既定机制报告不冒作已执行业务失败。NetworkPlugins 对 EndedAJob 降级，未执行拒绝保Error。 |
| 本次重复报告已删除／降级 | 旧 CommandLoggingBehavior 随pipeline删除；四 scheduled 每角色与 SelectedSessionSafety 封锁定位为无栈Warning；OrdinaryLoading 保 run／room／version／sequence 的Warning，DungeonSelection 重抛点为无栈Debug。均保原throw及业务边界。 |
| 发件箱结束钩子 | GameplayOutboxes.Flush 在原body正常时把Submit拒绝降为无栈Warning，refused仍成为执行器最终Error；原body已失败／取消时保Submit独立被隔离错误的Error，避免丢失另一失败。fallback close及执行器原cause选择不改；两个现有flush强例同时核此区别。 |
| 健康处理并继续的独立业务错误 | Opening、Cera、Mailbox、Warehouse、Avatar／socket／quality等原正常拒绝或已确认健康回滚结果的mapper，以及 MoveItemSpace、LevelUpTicket、QuestClearCoupon 原允许的辅助读取失败，记录后仍返回既有ACK／投影；不属于同次向executor传播。Party两塔 secondPhaseFailure 来自事务后直接party变更／recipient捕获，正常返回拒绝并精确关闭，Error保留。ItemSale 原价格失败并以零金返回的业务Error保留。 |
| 独立清理与生产者外错误 | DiscardExact 自身失败是被隔离的另一cause，Error保留；DomainEventDispatcher 的持久化扫描／重试记录、Joust／塔来源的队外／未执行失败、Scheduler无效配置和未执行拒绝，以及 GM mapper受 ExecutorFailed门之外的原独立错误保留。已执行的producer失败沿 EndedAJob既定降级规则，不扩新异常豁免。 |

F-132仍是延迟Compose禁用而非已修，Host transitive IL判据及 async／构造器反例不变。F-131仍保原批准的关闭因果精度限制。原始失败与hangdump保留在忽略的logs内；尚无A21实机或专项性能通过主张。

## 冻结和实际运行

源码最终 freeze 为 task65-final-r4-build 的 source-freeze.json。Release --no-incremental --no-restore 构建 0 warnings／0 errors。仅修复三处测试迁移遗漏后重新全八，不混用 r2／r3 为最终绿证据。

r3 Application 的6004项已仅两条原Dimension失败；Protocol两条失败实际为旧Error断言和测试内部二次dispatch，保原cause／room／EndedCharacters／runs.Ended而迁现行驱动。新增迁回的stale非报告参与者例先聚焦1/1通过。r2三条婚礼错误为漏显式origin，原真并发与SQL断言保留；r2 publication自等与Networking旧label造成的原5m诊断证据均保留。没有放宽超时、类并行、skip、断言或产品拒绝机制。

最终全部采用 `verification/logs/task65-final-r4-<lowercaseproject>/`，每项原命令、实际 require_escalated、自然 start/end/exit、console、TRX 与逐程序集 DFO 均在各目录。严格串行、原类并行／5m blame／detailed／DFO／--no-build／--no-restore，未改断言、skip或超时。最终源码哈希与r4构建freeze逐文件相同。

| 项目 | 总数 | 通过 | 失败 | 跳过 | 相对Task64新增／删除 display库存 |
|---|---:|---:|---:|---:|---:|
| Application | 6005 | 5987 | 2 | 16 | 21／59 |
| Protocol | 1795 | 1795 | 0 | 0 | 1／1 |
| Networking | 203 | 203 | 0 | 0 | 0／0 |
| Persistence | 484 | 484 | 0 | 0 | 0／0 |
| Infrastructure | 976 | 974 | 0 | 2 | 0／0 |
| Host | 208 | 208 | 0 | 0 | 0／0 |
| HttpApi | 59 | 59 | 0 | 0 | 0／0 |
| Pvf | 80 | 80 | 0 | 0 | 0／0 |

总计9810＝9790通过＋2原失败＋18原跳过。原Task64为9848，差38来自Application N21/D59；Protocol仅1条同义迁名，其他项目库存无变。逐display的名称／结果／multiplicity及skip消息与精确失败比对见 `logs/task65-final-r4-inventory.json`；旧结构矩阵和方法更名去向见上表，恢复stale非报告参与者原强例，不以测试总数等额为目标。

两条失败均为 DimensionDungeonTests 的 All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot、Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource，原消息逐字仍为 `Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'.`，类型仍InvalidDataException；没有调用PVF偶发豁免。18跳过的名称、消息和multiplicity与Task64相同。

DFO 使用原 violations.py 逐程序集解析最终八目录：68负例＋7 controls，outside／exe／orphan均0；原67的身份与数量不改，增量恰F9真实leak一条（CommandBusExecutorTests合计2）。原始记录与逐项目解析日志 `logs/task65-final-r4-dfo-*.log` 保留。

运行时 `logs/task65-runtime-r3` 与当前production源码双向crosscheck：173消息（159 business＋14 telemetry）／141 message handlers、172实际命令／137 command handlers；真实Protocol/Internal route、绑定类型与显式工作单元model逐项一致。r4仅三处测试改变，当前生产哈希绑定保存在 `logs/task65-r4-runtime-source-binding.json`；route／model／telemetry-route三个篡改反例均拒绝，不改冻结baseline。

迁移账实际只改13行：2fine、3保护、8transition；所有ID、original、行序及非目标行逐项不变。`logs/task65-ledger-integrity.json`、`logs/task65-strict.json`、`logs/task65-eight-bad-cases.json`分别保存完整性、strict通过与原8坏例全部拒绝。当前commands204 closed、messages159 closed/14 retained、sources73 closed/12 retained、protections5 closed、transitions8 closed，五类pending全部0。baseline文件无diff。

本批没有新增需要独立旧green的业务表征：原强例只迁当前入口，T5/T6有限断言和旧stale例迁移随本批实现。`/private/tmp/task65-pure-tests.patch` 为0字节；`/private/tmp/task65-owned-paths.json`列本执行者路径与Task仅Result所有权，原始证据和生成缓存不纳入提交。STOP交主控，不启动阶段二。

## 主控消费

主控独立读取最终八项 TRX、运行元数据和测试差异，复核失败与跳过身份、逐 display 库存、项目串行及实际执行环境；重新运行原 DFO 解析器、strict 和八个坏例，结果与上表一致。当前 2816 个构建冻结输入一致，实际生产检索无旧入口、旧频道别名或请求 ambient 残留；逐行复核仅指定 13 行闭合，原始事实、行序和非目标行保持。旧非报告参与者失效保护已实际迁回并通过健康对照，其他被删结构用例按上表核对，不把删测试当通过。190 个待提交路径均属于本 Task 的产品、测试、工具及人工记录，原始运行证据排除；消费通过，允许本地迁移检查点并进入用户 §8 第二阶段，尚不代表 B 完成或封存。
