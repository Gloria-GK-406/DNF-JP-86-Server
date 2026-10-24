# Spec 2 注册与工作单元迁移对照

本账以 `aa1e732b0af5cc0040521213422833d2a9b9a5ce` 为起点，属于 TASK-03 的迁移输入，不是另一份执行状态表。`migration-baseline.json` 在本 Task 检查点后冻结；`migration-ledger.json` 逐簇追加实际去向与证据。声明 `ExecutionKind`、实际 Begin/Commit/RequireRollback/自开边界，以及新 CMD 的显式工作单元是三件事，不能互相代替。

## 注册全集与口径

两次独立临时 console 项目只调用五模块 ConfigureServices，再枚举 ServiceCollection 中公开的注册实例。没有 BuildServiceProvider、Host 启动、PVF/SQLite 解析或业务执行。Runtime CLR 身份与源码接线逐条双向对应；源码解析屏蔽注释/字面量，按平衡泛型参数处理，短名以真实命名空间/using及运行时全集唯一配对，未知或多义即失败。

- 消息 173 条／141 个 handler；遥测14条／2类，业务159条／139类。遥测为 HiddenCharacterInfoHandler 与 UnansweredReportHandler，仍保留日志例外，不算协议业务迁移。
- 旧命令204条／203个实际 handler：Transactional170、Direct21、ReadOnly9、Workflow4。ProtocolModule163、ApplicationModule36、五扩展各1。
- HousingBuyItemCommand 与 BuyItemCommand 共用实际 BuyItemCommandHandler；完整名和 using 短名不造成两类型。139不是最终协议 CMD 数。
- TASK03建立时业务消息159、旧命令204和需要进入执行器的来源73全部 pending；TASK04的当前精确闭合见文末。12条明确 OutsideGameplay 来源保持原排除理由，不声称迁成内部 CMD。4保护＋F132及8过渡项的新归属/删除证据也 pending。

消息 ID 为 `message:<枚举值>`，旧命令 ID 为 `command:<CLR全名>`。每行的 original 必须等于冻结基线；refs 必须存在。消息的 candidate_commands 仅为 handler 所在文件中的词法候选，**不是调用图**；共享 Delivery/Coordinator 候选和 original_callers 需逐簇读实际控制流后闭合。不得以词法无命中推断无动态调用或无共享帮助器。

## 如何使用与闭合

```text
python3 .rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd/verification/probe-tools/migration-ledger.py validate
python3 .rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd/verification/probe-tools/migration-ledger.py selftest
python3 .rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd/verification/probe-tools/migration-ledger.py capture --output /private/tmp/另一个不存在的独立目录
```

capture 需要本机 .NET/FSEvents 环境，生成的 csproj/bin/obj 仅在独立 `/private/tmp` 目录；引用路径从当前仓库动态生成，不在持久工具中固定本机路径。构建与导出期间不得与另一个写入／构建同工作区的 worker 并发。一次 capture 的 runtime.json、payload.json、source-registrations.json、crosscheck.json、manifest.json 和 console.log 分开保存；时间和临时路径不混进稳定 payload。

`initialize --capture <本轮capture目录>` 只用于尚未建立基线时，遇现有 baseline/ledger即拒绝覆盖。复现基线使用历史检查点或读取已保存的两轮 payload；后续路由/注册 API 改变时，对应 Task 要同步适配当前导出器，不能重写冻结 original 来掩盖差异。

每簇先加载开放行、原调用者、共享帮助器全部消费者及来源，核实真实事务后再搬。closed 必须具备：

1. destination 的 kind（protocol-cmd/internal-cmd/application-service/deleted）和实际新位置；删除需明确理由。
2. actual_units、participants、protection_locations 各自的 old/new/evidence 映射列表；确无该项时用带证据的 none_reason，而非空列表或猜测“无事务”。
3. 有效的工作区内证据路径及包含 done Result 的 completion_task；内容等价仍由相应 Task 的独立审查判断，工具不批准语义。
4. 保护/过渡项闭合需新归属或删除去向与行为/检索证据。全部临时 legacy 路由和 LegacyRequestAdapter 在最后消费者迁完时删除，不留下新 CMD 回调旧 bus。

validate 检查全集同集/唯一、原行不被改写、分类/引用、pending字段及closed证据形状与路径。selftest用副本拒绝缺真实消息、缺旧命令、重复、遥测误归、断引用、无证据提前closed、缺文件及过渡项无去向；不改原数据。它不能证明“原提交→内存临界序列”语义等价，不能用任意真实文件填充证据冒称通过独立审查。

## 来源索引

来源独立于204旧命令。宽扫描143个代码命中逐项分类；另解析具体 HTTP MapGet/Post/Methods 等实际注册，展开 Cera/Mail 的局部 Get/Edit helper。HTTP 共54条：39 group-enqueued、3 self-enqueued、12 OutsideGameplay；包括 `/gm/{**path}` 静态页，不因模板包含单词 path而漏掉。下表31个其它来源＋54路由＝85行，各原位/链/目标输入/取消/停机/拒绝/合并细节在 JSON；后续新增来源须补开放账，不能凭源文件已经出现就视为覆盖新增方法。

| 来源 ID | 原入口 | 本轮分类 |
|---|---|---|
| `source:lifecycle-connected` | `Server/src/Dfo.Server.Application/Dispatching/NetworkConnectionLifecycleDispatcher.cs:32` | lifecycle |
| `source:lifecycle-disconnected` | `Server/src/Dfo.Server.Application/Dispatching/NetworkConnectionLifecycleDispatcher.cs:42` | lifecycle |
| `source:startup-epic-book` | `Server/src/Dfo.Server.GameServer/GameServerStartupService.cs:78` | startup |
| `source:startup-experience-storage` | `Server/src/Dfo.Server.GameServer/GameServerStartupService.cs:87` | startup |
| `source:startup-gm-bootstrap` | `Server/src/Dfo.Server.GmWeb/GmBootstrapService.cs:29` | startup |
| `source:joust-startup` | `Server/src/Dfo.Server.Application/Events/JoustMaintenanceService.cs:24` | startup |
| `source:joust-periodic` | `Server/src/Dfo.Server.Application/Events/JoustMaintenanceService.cs:73` | timer |
| `source:event-consumer` | `Server/src/Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs:72` | event |
| `source:event-depth-settlement` | `Server/src/Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs:126` | event |
| `source:scheduled-task` | `Server/src/Dfo.Server.Application/Scheduling/ScheduledTaskHostedService.cs:129` | scheduled |
| `source:card-delay` | `Server/src/Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs:348` | delay |
| `source:timer:LoginSessionMaintenanceService` | `Server/src/Dfo.Server.Application/Authentication/LoginSessionRuntime.cs:43` | timer |
| `source:timer:AltarQuotaDailyRefreshService` | `Server/src/Dfo.Server.Protocol/Game/Altar/AltarQuotaDailyRefreshService.cs:23` | timer |
| `source:timer:AltarScheduler` | `Server/src/Dfo.Server.Protocol/Game/Altar/AltarScheduler.cs:43` | timer |
| `source:timer:BossTowerReturnSweepService` | `Server/src/Dfo.Server.Protocol/Game/BossTowerProjectionDelivery.cs:855` | timer |
| `source:timer:DungeonPartyWipeService` | `Server/src/Dfo.Server.Protocol/Game/CharacterDeath/DungeonPartyWipeService.cs:59` | timer |
| `source:timer:CreatureClockService` | `Server/src/Dfo.Server.Protocol/Game/Creatures/CreatureRuntimeDelivery.cs:146` | timer |
| `source:timer:FatigueDailyRefreshService` | `Server/src/Dfo.Server.Protocol/Game/Fatigue/FatigueProjection.cs:52` | timer |
| `source:timer:IllusionTowerReturnSweepService` | `Server/src/Dfo.Server.Protocol/Game/IllusionTowerProjectionDelivery.cs:806` | timer |
| `source:timer:MarriageWeddingScheduler` | `Server/src/Dfo.Server.Protocol/Game/MarriageProposal/MarriageWeddingSlice.cs:206` | timer |
| `source:timer:RoomMechanismMaintenanceService` | `Server/src/Dfo.Server.Protocol/Game/RoomMechanisms/RoomMechanismProjection.cs:83` | timer |
| `source:timer:DimensionQuotaDailyRefreshService` | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/DimensionQuotaDailyRefreshService.cs:22` | timer |
| `source:timer:GoldSilverHornQuotaDailyRefreshService` | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/GoldSilverHornQuotaDailyRefreshService.cs:22` | timer |
| `source:timer:ItemUsageDailyRefreshService` | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/ItemUsageProjection.cs:101` | timer |
| `source:timer:PremiumDailyRefreshService` | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/PremiumDailyRefreshService.cs:48` | timer |
| `source:timer:ReviveCoinQuotaDailyRefreshService` | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/ReviveCoinQuotaDailyRefreshService.cs:22` | timer |
| `source:timer:MirrorQuotaDailyRefreshService` | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/UsageQuotaProjection.cs:103` | timer |
| `source:timer:HerasQuotaDailyRefreshService` | `Server/src/Dfo.Server.Protocol/Game/Tournament/HerasQuotaDailyRefreshService.cs:23` | timer |
| `source:timer:TournamentQuotaDailyRefreshService` | `Server/src/Dfo.Server.Protocol/Game/Tournament/TournamentQuotaDailyRefreshService.cs:23` | timer |
| `source:timer:TournamentRewardSweepService` | `Server/src/Dfo.Server.Protocol/Game/Tournament/TournamentRewardSweepService.cs:28` | timer |
| `source:timer:WeaknessRecoveryService` | `Server/src/Dfo.Server.Protocol/Game/Weakness/WeaknessRecoveryService.cs:56` | timer |
| `http:DELETE /api/gm/v1/mail/templates/{id}` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:42` | group-enqueued |
| `http:GET /api/gm/v1/accounts` | `Server/src/Dfo.Server.GmWeb/GmPlayerDirectoryEndpoints.cs:20` | group-enqueued |
| `http:GET /api/gm/v1/accounts/{accountId}` | `Server/src/Dfo.Server.GmWeb/GmPlayerDirectoryEndpoints.cs:28` | group-enqueued |
| `http:GET /api/gm/v1/audit` | `Server/src/Dfo.Server.GmWeb/GmAuthenticationEndpoints.cs:33` | group-enqueued |
| `http:GET /api/gm/v1/auth/csrf` | `Server/src/Dfo.Server.GmWeb/GmAuthenticationEndpoints.cs:23` | outside |
| `http:GET /api/gm/v1/auth/session` | `Server/src/Dfo.Server.GmWeb/GmAuthenticationEndpoints.cs:30` | outside |
| `http:GET /api/gm/v1/auth/status` | `Server/src/Dfo.Server.GmWeb/GmAuthenticationEndpoints.cs:20` | outside |
| `http:GET /api/gm/v1/cera/accounts` | `Server/src/Dfo.Server.GmWeb/GmCeraEndpoints.cs:29` | group-enqueued |
| `http:GET /api/gm/v1/cera/operations` | `Server/src/Dfo.Server.GmWeb/GmCeraEndpoints.cs:29` | group-enqueued |
| `http:GET /api/gm/v1/cera/operations/{id}` | `Server/src/Dfo.Server.GmWeb/GmCeraEndpoints.cs:30` | group-enqueued |
| `http:GET /api/gm/v1/characters` | `Server/src/Dfo.Server.GmWeb/GmPlayerDirectoryEndpoints.cs:24` | group-enqueued |
| `http:GET /api/gm/v1/characters/{characterId}` | `Server/src/Dfo.Server.GmWeb/GmPlayerDirectoryEndpoints.cs:34` | group-enqueued |
| `http:GET /api/gm/v1/daily-refill` | `Server/src/Dfo.Server.GmWeb/GmDailyRefillEndpoints.cs:20` | group-enqueued |
| `http:GET /api/gm/v1/daily-refill/items` | `Server/src/Dfo.Server.GmWeb/GmDailyRefillEndpoints.cs:24` | group-enqueued |
| `http:GET /api/gm/v1/daily-refill/items/{id:int}` | `Server/src/Dfo.Server.GmWeb/GmDailyRefillEndpoints.cs:26` | group-enqueued |
| `http:GET /api/gm/v1/events` | `Server/src/Dfo.Server.GmWeb/GmEventsEndpoints.cs:20` | group-enqueued |
| `http:GET /api/gm/v1/events/joust/current-round` | `Server/src/Dfo.Server.GmWeb/GmEventsEndpoints.cs:26` | group-enqueued |
| `http:GET /api/gm/v1/events/joust/settings` | `Server/src/Dfo.Server.GmWeb/GmEventsEndpoints.cs:24` | group-enqueued |
| `http:GET /api/gm/v1/events/weekly-attendance/settings` | `Server/src/Dfo.Server.GmWeb/GmEventsEndpoints.cs:22` | group-enqueued |
| `http:GET /api/gm/v1/health` | `Server/src/Dfo.Server.GmWeb/GmWebModule.cs:34` | outside |
| `http:GET /api/gm/v1/initialization` | `Server/src/Dfo.Server.GmWeb/GmInitializationEndpoints.cs:25` | group-enqueued |
| `http:GET /api/gm/v1/initialization/items` | `Server/src/Dfo.Server.GmWeb/GmInitializationEndpoints.cs:31` | group-enqueued |
| `http:GET /api/gm/v1/initialization/items/{id:int}` | `Server/src/Dfo.Server.GmWeb/GmInitializationEndpoints.cs:33` | group-enqueued |
| `http:GET /api/gm/v1/items/icon-pack` | `Server/src/Dfo.Server.GmWeb/GmItemEndpoints.cs:17` | outside |
| `http:GET /api/gm/v1/items/{itemId:int}/icon` | `Server/src/Dfo.Server.GmWeb/GmItemEndpoints.cs:22` | outside |
| `http:GET /api/gm/v1/items/{itemId:int}/tooltip` | `Server/src/Dfo.Server.GmWeb/GmItemEndpoints.cs:29` | outside |
| `http:GET /api/gm/v1/mail/draft` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:24` | group-enqueued |
| `http:GET /api/gm/v1/mail/items` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:26` | outside |
| `http:GET /api/gm/v1/mail/items/{itemId:int}` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:29` | outside |
| `http:GET /api/gm/v1/mail/operations` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:25` | group-enqueued |
| `http:GET /api/gm/v1/mail/operations/{id}` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:32` | group-enqueued |
| `http:GET /api/gm/v1/mail/recipients` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:25` | group-enqueued |
| `http:GET /api/gm/v1/mail/templates` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:24` | group-enqueued |
| `http:GET /api/gm/v1/overview` | `Server/src/Dfo.Server.GmWeb/GmPlayerDirectoryEndpoints.cs:17` | group-enqueued |
| `http:GET /api/gm/v1/server-info` | `Server/src/Dfo.Server.GmWeb/GmServerInfoEndpoints.cs:19` | outside |
| `http:GET /api/launcher/v1/bootstrap` | `Server/src/Dfo.Server.HttpApi/HttpApiApplicationExtensions.cs:45` | outside |
| `http:GET /gm/{**path}` | `Server/src/Dfo.Server.GmWeb/GmWebModule.cs:45` | outside |
| `http:POST /api/gm/v1/auth/login` | `Server/src/Dfo.Server.GmWeb/GmAuthenticationEndpoints.cs:29` | self-enqueued |
| `http:POST /api/gm/v1/auth/logout` | `Server/src/Dfo.Server.GmWeb/GmAuthenticationEndpoints.cs:32` | group-enqueued |
| `http:POST /api/gm/v1/cera/grant` | `Server/src/Dfo.Server.GmWeb/GmCeraEndpoints.cs:36` | group-enqueued |
| `http:POST /api/gm/v1/cera/preview` | `Server/src/Dfo.Server.GmWeb/GmCeraEndpoints.cs:31` | group-enqueued |
| `http:POST /api/gm/v1/daily-refill` | `Server/src/Dfo.Server.GmWeb/GmDailyRefillEndpoints.cs:22` | group-enqueued |
| `http:POST /api/gm/v1/events` | `Server/src/Dfo.Server.GmWeb/GmEventsEndpoints.cs:42` | group-enqueued |
| `http:POST /api/gm/v1/events/joust/current-round/champion` | `Server/src/Dfo.Server.GmWeb/GmEventsEndpoints.cs:28` | group-enqueued |
| `http:POST /api/gm/v1/events/joust/settings` | `Server/src/Dfo.Server.GmWeb/GmEventsEndpoints.cs:35` | group-enqueued |
| `http:POST /api/gm/v1/initialization/account` | `Server/src/Dfo.Server.GmWeb/GmInitializationEndpoints.cs:27` | group-enqueued |
| `http:POST /api/gm/v1/initialization/character` | `Server/src/Dfo.Server.GmWeb/GmInitializationEndpoints.cs:29` | group-enqueued |
| `http:POST /api/gm/v1/mail/preview` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:33` | group-enqueued |
| `http:POST /api/gm/v1/mail/send` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:43` | group-enqueued |
| `http:POST /api/gm/v1/mail/templates` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:40` | group-enqueued |
| `http:POST /api/launcher/v1/login` | `Server/src/Dfo.Server.HttpApi/HttpApiApplicationExtensions.cs:56` | self-enqueued |
| `http:POST /api/launcher/v1/register` | `Server/src/Dfo.Server.HttpApi/HttpApiApplicationExtensions.cs:54` | self-enqueued |
| `http:PUT /api/gm/v1/mail/draft` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:39` | group-enqueued |
| `http:PUT /api/gm/v1/mail/templates/{id}` | `Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs:41` | group-enqueued |

GameplayTimerWork 的入队 token 为 None，body携带调用者 token；Tick吸收 ended-job失败为Warning并继续，原入队拒绝/取消仍抛。周期 coalescing 与直接手动 RunAsync 不能默认为同一个 busy轮次。F07 后续按 P1 拆扫描／逐session CMD，Premium保留完整权益指纹、前次flag和到期清零，不能统一成周期去重。

生命周期 connected 有真实source；disconnected以close cause child或新根处理，source为空，深度拒绝仍重开根完成拆除并关闭被拒链根连接。F131保留“关闭登记尚未执行而传输先关仍继承链”的精度边界，不声称修复。

CardReward的原RunTimerAsync当前显式使用新的Timer root，**不继承发布作业链**；先Yield离开作业，再延迟后None入队，Stop取消pending、Drain等inflight，保持原失败回调。不得把泛化的来源继承说明套成它的当前事实。

事件due读取在executor外，每条消费才入队；本进程发布者child Event在重试间保同parent，旧进程为StartupRecovery root。Reliable业务和完成标记同事务；LiveOnly先持久完成消费、再外部投影，失败不重放。Direct订阅仍由EventService在发布者当前单元直接调用领域能力，不派嵌套CMD。

测试适配边界只存文件/行索引（test_adapter_scan），包括TestGameplay、直发bus与queue；不是把每个测试case变为生产来源。生产注册的204与测试专用命令/反例严格按模块装配事实区分。

## 实际事务入口索引

宽扫描的代码命中保存于 transaction_scan，逐行区分真实入口、提交、回滚请求、机制、ambient/参与者依赖及非事务同名噪音。`ordering?.Begin` 是出站顺序组，`runtime.Begin` 是运行态入场，不能算持久工作单元；DomainTransaction参与者提交和outbox unit提交单独归机制。这里只建立拥有者索引，**没有**给204行假造已核实的提交对应。

| 拥有者 | 原入口 | 已核实的边界形状 |
|---|---|---|
| `transaction-owner:Commands/Pipeline/UnitOfWorkBehavior.cs` | `Server/src/Dfo.Server.Application/Commands/Pipeline/UnitOfWorkBehavior.cs:28` | read-only/write declared command boundary; RequireRollback normal refusal rolls back; Freeze then Commit; exception abort; not durable-commit proof via IsSettled |
| `transaction-owner:DomainEvents/DomainEventDispatcher.cs` | `Server/src/Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs:211` | Reliable claim/business/completion one write unit; LiveOnly claim+completion commit first then external projection; retry/depth markers separate units |
| `transaction-owner:World/TransactionalDungeonVisitDomainService.cs` | `Server/src/Dfo.Server.Application/World/TransactionalDungeonVisitDomainService.cs:14` | reuse ambient else Begin/action/Commit; dungeon entry and movement share caller unit |
| `transaction-owner:World/DungeonLifecycleDomainService.cs` | `Server/src/Dfo.Server.Application/World/DungeonLifecycleDomainService.cs:10` | reuse ambient else own departure fact write/Commit |
| `transaction-owner:Inventory/EpicBookDomainService.cs` | `Server/src/Dfo.Server.Application/Inventory/EpicBookDomainService.cs:74` | startup own Begin migration Commit |
| `transaction-owner:World/BossTower/BossTowerDomainService.cs` | `Server/src/Dfo.Server.Application/World/BossTower/BossTowerDomainService.cs:59` | conditional existing boundary; tower write then memory coordination order must be mapped per caller |
| `transaction-owner:World/IllusionTower/IllusionTowerDomainService.cs` | `Server/src/Dfo.Server.Application/World/IllusionTower/IllusionTowerDomainService.cs:48` | conditional boundary; exact tower participant and phase mapping pending |
| `transaction-owner:Mail/MailboxDomainService.cs` | `Server/src/Dfo.Server.Application/Mail/MailboxDomainService.cs:171` | ambient reuse or Begin(readOnly:!writing), acquire write when writing; caught refusal aborts current transaction; own lease commit/dispose |
| `transaction-owner:Inventory/ItemGrantDelivery.cs` | `Server/src/Dfo.Server.Application/Inventory/ItemGrantDelivery.cs:21` | conditional own write lease; whole-quantity addition commit else rollback/dispose |
| `transaction-owner:Characters/CharacterStateAccess.cs` | `Server/src/Dfo.Server.Application/Characters/CharacterStateAccess.cs:28` | snapshot reads rollback-only; outside reads standalone DomainTransaction rolled back; durable outside writes refuse; volatile changes standalone commit; selected position persist reuses ambient or owns standalone write |
| `transaction-owner:Creatures/CreatureRuntimeDomainService.cs` | `Server/src/Dfo.Server.Application/Creatures/CreatureRuntimeDomainService.cs:289` | runtime read/write helper own boundary when absent; exact-generation handling and pending caches separately guarded |
| `transaction-owner:Inventory/ItemContainers.cs` | `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:181` | store-backed container boundary access; persistent versus volatile caller obligations, no universal per-command transaction inference |

RequireRollback 的领域调用还包括 Rental/AccountLuckyStar、Sao、SouthernDale、BossTower、DungeonAdmission、ItemDelivery、QuestHelperPuppet，详见逐行扫描。拒绝返回与抛错、正常回滚与不确定提交都必须按实际调用者保留；不能只保留pipeline注册。DomainTransaction.IsSettled在Prepare前已置位，不是durable commit证明。F132的事务内延迟组装风险仍在，生产API零调用由TASK02的IL判据守禁用；不能因此豁免其它提交后失败围栏。

## 四保护与 F132

| ID | 原保护 | 当前测试 | 移交规则 |
|---|---|---|---|
| `protection:F-88-recover` | `Server/src/Dfo.Server.Protocol/Game/RecoverStamina/RecoverStaminaSlice.cs:142` | `Server/tests/Dfo.Server.Application.Tests/Inventory/WeaknessGoldRecoveryTests.cs` / `A_selected_session_not_yet_in_world_is_silently_ignored_by_the_recovery_protocol` | selected-not-world remains silent/no currency or weakness changes；新位置pending |
| `protection:F-88-teleport` | `Server/src/Dfo.Server.Protocol/Game/Teleport/TeleportSlice.cs:95` | `Server/tests/Dfo.Server.Application.Tests/Inventory/TeleportConsumableTests.cs` / `A_previous_town_member_from_another_generation_cannot_start_a_teleport` | wrong-generation prior lease cannot begin settlement; adapt current bus-count probe to new CMD protection owner；新位置pending |
| `protection:F-93-discard` | `Server/src/Dfo.Server.Application/Commands/SelectedSessionSafety.cs:64` | `Server/tests/Dfo.Server.Application.Tests/Creatures/CreatureRuntimeDomainServiceTests.cs` / `Blocking_uncertain_settlement_discards_only_the_exact_creature_runtime_without_saving_it` | uncertain settlement clears exact old pending reward/projection without saving stale cache; replacement generation retained；新位置pending |
| `protection:F-93-recipient` | `Server/src/Dfo.Server.Application/Effects/EffectOperationLedger.cs:39` | `Server/tests/Dfo.Server.Application.Tests/Sessions/EffectOperationReplayTests.cs` / `A_recipient_replaced_after_capture_is_rejected_before_the_ordinal_is_consumed` | capture-to-registration replacement rejected before sentinel/ordinal; same ordinal accepted after recovery；新位置pending |
| `protection:F-132` | `Server/src/Dfo.Server.Application/Execution/Outbox/GameplayOutbox.cs:164` | `Server/tests/Dfo.Server.Host.Tests/DeferredOutboxCompositionArchitectureTests.cs` / `No_production_method_references_the_deferred_outbox_composition_API` | mechanism not repaired; actual production IL references forbidden; new typed outbox must not expose API；新位置pending |

四项原单点RED/GREEN和Safety最终强化证据引用TASK02原日志，A封存文件不改。后续handler只入队时，应把观察入口迁到真实CMD/工作单元的保护新归属，保静默／不开始结算／精确丢弃／序号语义；不能删除测试，也不能冻结旧handler里“0次bus调用”的实现形状。

## 过渡机制去向

| ID | 原位置 | 后续责任 |
|---|---|---|
| `transition:T1` | `Server/src/Dfo.Server.Application/Commands/CommandBus.cs:30` | T-003 dual route then T-011 remove InlineAsync/legacy semantics；最终证据pending |
| `transition:T2` | `Server/src/Dfo.Server.Application/Dispatching/NetworkMessageDispatcher.cs:52` | T-003..010 absorb handler/shared Delivery/Coordinator business; T-011 no fine-grained forwarding；最终证据pending |
| `transition:T3` | `Server/src/Dfo.Server.Application/Commands/CommandDispatchingExtensions.cs:34`；`Server/src/Dfo.Server.Application/Commands/CommandContracts.cs:27` | explicit current-CMD work units/participants replace five pipeline/markers; T-011 zero；最终证据pending |
| `transition:T4` | `Server/src/Dfo.Server.Application/Commands/NetworkCommandAuthorization.cs:8`；`Server/src/Dfo.Server.Application/Commands/Pipeline/SelectedSessionExecutionBehavior.cs:40`；`Server/src/Dfo.Server.Application/Effects/EffectOperationContext.cs:30`；`Server/src/Dfo.Server.Application/Commands/CommandBus.cs:16` | explicit request/footprint parameters, no fallback; T-011 only Job+transaction AsyncLocal；最终证据pending |
| `transition:T5` | `Server/src/Dfo.Server.Application/Commands/Pipeline/CommandLoggingBehavior.cs:34` | remove duplicate CMD Error with legacy pipeline, executor remains authority；最终证据pending |
| `transition:T6` | `Server/src/Dfo.Server.Application/Dispatching/NetworkMessageDispatcher.cs:51`；`Server/src/Dfo.Server.Application/Dispatching/NetworkConnectionLifecycleDispatcher.cs:35` | bus job entry initializes before any failure; empty-entry failure closes real source；最终证据pending |
| `transition:planned-legacy-routes` | `Server/src/Dfo.Server.Application/Commands/CommandHandlerRegistry.cs:3`；`Server/src/Dfo.Server.Application/Dispatching/MessageHandlerRegistry.cs:12` | not yet implemented: count LegacyFineGrained/LegacyQueued routes introduced T003, remove T011；最终证据pending |
| `transition:planned-LegacyRequestAdapter` | `Server/src/Dfo.Server.Application/Effects/EffectOperationContext.cs:13` | not yet implemented: old caller boundary only, explicit core no ambient, remove last consumer same unit；最终证据pending |

T1～T6的权威来源为A封存的 `../../verification/transition-mechanisms.md`；本账引用它而不改历史。GameplayJob身份、DomainTransactionScope环境事务、出站登记适配器/通知总线的Current守卫是常设机制，不因字符串相同混进删除范围。

## TASK-03 验证与当时下一候选（历史）

TASK03两次独立capture payload SHA256为 `4861e139bb126adaefba9240d0371d582c22cab149277f1ce8902cf22dc3f043`，逐字一致；源码↔runtime全部377条注册一一对应。完整原命令/UTC/退出/环境/资源和中间修正记录见task-03证据。声明Kind的数量只是核对基线，不是实际单元验收。

下一最小贯通候选为 Channel ScriptVersion（旧查询＋响应吸入协议CMD）和 GmBootstrap（off-executor密码准备不动、内部CMD保原一段写）。当前仅记录原入口，相关行仍pending，不在此创建下一Task。T003还须同一bus双路由、显式request四变体与Closed拒绝、显式internal origin、真实source失败关闭、具名登记/工作单元能力、违规反例；效果单例足迹链迁移不得借旧ambient回落。

本Task产品/测试零变化，新增0／删除0，8365数量账不变；没有重跑业务全量或CMD测量，也不代表Spec完成。后续最终八项目、原codec/Golden、同工具测量及六根条件仍由P1后续单元承担。


## TASK-04 当前首贯通映射

已将 message:5、旧GetChannelScriptVersionCommand、BootstrapGmAdministratorCommand及source:startup-gm-bootstrap四行映射闭合，原original/baseline不变；实际单元与参与者依据见 `evidence/task-04-mapping/workunit-map.md` 和四行evidence。当前pending为业务158、旧命令202、执行器来源72；保留14遥测及12明确outside。五保护及八过渡仍pending，F129在Channel新例里已有真实TCP/临时坏变异证据，不擅造baseline ID。

当前capture认识新Protocol/Internal注册及message↔CMD的CLR绑定，双向校验仍覆盖全部173消息/204命令，原统计只作历史基线。新ProtocolNonEffect与TrustedInternal可用；effect注册/MainSettlement明确拒绝，真正效果账本/足迹/主生命周期仍待首effect单元。三分支、旧Context/Capture重载、默认Internal NotSupported兼容在现有过渡行登记，T011清零；不把保留层冒称最终机制。

`preclose --current-task <本B的tasks/TASK-04.md>` 只豁免该精确当前Task尚未写done Result，其它映射/路径/原值及其它完成Task仍严格。产品及预检过后才能写Result，再以`validate`严格核验；预检不是完成或审查通过。


## TASK-05 剩余 Channel 闭合

Connect/Script/Info三消息（11/9/17）及旧GetScript/GetInfo两命令已对应新协议CMD，旧Get/result/handler及注册删除。原无事务形状保持，key纯算法一处、原同连接/header/body/ciphertext/catalog顺序由新CMD及真实TCP共同保护，见`evidence/task-05-mapping/workunit-map.md`。Task04四行closed不改，current capture应为173消息/205命令，4Protocol+1Internal+200Legacy；actual源/CLR逐条核验见本轮capture。其余155业务消息/200旧命令/72来源仍pending，整体Spec未完成。

## TASK-06：LOGIN 闭包

GameLogin message:65537 与旧 RedeemGameLogin command 两行 closed，实际边界见 `evidence/task-06-mapping/workunit-map.md`。173 消息当前5个 Protocol 入口；实际命令仍205，其中 Protocol5／Internal1／Legacy199。历史 original/baseline 和其它 closed 行不改。正常兑换实际仅有 LoginSessionRegistry、OnlineStateRegistry 两个运行态参与者，均 CommitCanFail=false；selected-session recipients 为空。注册/launcher 的 SQLite 夹具以及测试注入的可失败参与者不等于生产兑换 SQLite enlist。提交后重查身份、settings、typed部署与七消息登记不再开认证事务，hotkeys 后直接 domain 激活 profile0。

原同票据 reconnect/supersede 语义保留；“提交不回滚”不等于新增一次性票据。原 Net postcommit reconnect 断言保持。ChannelEntryDomainService 吸收原 policy 全部职责，旧接口同 singleton alias 与 Activate（Select caller）仍 pending，记于现过渡行／命令 notes。业务154、旧命令199、来源72仍 pending；本行不代表 T003／Spec 完成。新注册精确数以本轮 capture 为准。

## TASK-07：四名册协议闭包

八行（Roster/CheckName/Create/Delete消息及原四细命令）closed，实际边界见`evidence/task-07-mapping/workunit-map.md`。四新CMD直接现领域，旧细API删除、薄入口同物理输入，无新MainSettlement/效果/账号门。创建/删除各原Tx→ordinary Write，Roster/Check原Direct不加事务；创建commit→ACK登记→只成功读带外观名册→NOTI，删除只ACK回原index。空selected集合与实际SQLite/动态内存参与者分开。

173消息预期9Protocol＋164Legacy（Legacy含14纯遥测）；205命令预期9Protocol＋1Internal＋195Legacy；最终以新capture为准。非遥测业务150、原细命令195、来源72仍pending；原5保护/8过渡仍未闭合，不代表T003/Spec完成。其余closed及所有original/baseline不改。

## TASK-08：OtherCharacterDetail

消息16973832及原QueryOtherCharacterDetailCommand两行闭合：新协议CMD吸收前置、直接query与typed即时投影，原Direct无显式事务，不新增effect/WorkUnits；同wire目标在领域执行点重解析，原双当前性/listener/精确recipient与NOTI编码器保持。见`evidence/task-08-mapping/workunit-map.md`。合理预期10Protocol/1Internal/194Legacy，实际以新capture为准；非遥测业务149、旧命令194、来源72仍pending，五保护/八过渡未闭合，不是Spec完成。其它closed/original/baseline保持。

## TASK-09：直接足迹的有界显式能力

仅 T4、planned-legacy-routes、planned-LegacyRequestAdapter 的 notes／introduced_artifacts 更新，三行继续 pending。六域／三 fine 入口的 slots／rental 经必需 IEffectFootprintRecorder 参数；旧隐式接口和 DI alias 删除。真实 LegacyFineGrained 最内层捕获原引用、注册/context身份、作业 ID 与调用租约；实际消息类别快照、真正外部 Standalone、Unknown Unavailable、关闭继承拒绝与 typed sticky 拒绝均保留。尚未迁 ApplyEffects 可选消费者／17 构造及子链内部 await 后中途失效检查；下一效果单元贯通，T011 最终删除 ambient／legacy。不是新 Effect/Main 启用或任何 frozen 行闭合。

## TASK-10：必需效果 Context 与显式 ApplyEffects

仅上述三条开放 transition 的 notes／introduced_artifacts 补充当前事实，仍 pending。17 构造及 Opening／XP／实际 cleanup 全 caller 显式传本次必需能力，ApplyEffects 删除 optional operations／隐式 Record，原完整映射用捕获引用。真实 Consume await-read 后到 Take 前复验有真实存储及 mutation 证据；其它 own 写／委托边界逐项检查，不称未继续传 cap 的下层 await 全部覆盖。Preview 不记录／不结算，原 commit 回调读取普通 facts。断线 observer 是临时 Lifecycle source 唯一生产创建点，明确原 chain／空物理 SourceConnection；T010 真 Internal CMD 取代时删除。未启新 Effect/Main、不迁 routes；149 非遥测消息／194 旧命令／72 来源、五保护及八过渡继续 pending，original／baseline／其它行保持。

TASK-10 r5另补已证Heras额度验证await→Direct前同一context复验，真实Direct/SQLite gate与可编译mutation作区分；未改变周期、额度、事件或事务规则。正式01的Illusion条件作用域回归已恢复原if→foreach，失败轮与源冻结保留，最终完整核验另建r5轮；以上仍不闭合任何行。


## TASK-11：完整 UseStackable 与显式 Main（r3）

仅 `message:65580` 和 `command:Dfo.Server.Protocol.Game.UseStackable.UseStackableCommand` 闭合：原fine删除，新完整CMD在Main内保留全部判断/效果/当前槽，普通selected拒绝仍提交最低Inventory；无selected只Silent ACK。ACK→虚拟资源→源槽绝对状态→可选XP，原bytes且无Creature附帧。新旧唯一CWT、原entry.Ordinal重试窗口、显式Footprint与最终失败收口见 `verification/evidence/task-11-inspection/workunit-map.md`。ConfigureServices-only capture01/02为173消息/205命令；route为11Protocol＋1Internal＋193Legacy，Kind为165Tx/27Direct/9RO/4Workflow，消息162Legacy/11Protocol，14遥测保持。

B2只保Main后RequiredExplicit及replay普通Write后CurrentStateBatch的本次真实encoder原cause；scope/lease额外独立错误（含同一Exception引用）撤raw，其它提交后来源仍全参与者围栏。新reader metadata是必需显式参数，sink无bus；临时LegacyReplay实际上游只有旧ledger Dispatch和Joust，唯一ReadReplayStateCommand dispatch仍pending，最迟T-011删除。只更新已有三条pending transition的notes/artifacts，未关闭其它消息、命令、来源、保护或过渡项；当前业务消息148、旧命令193、来源72 pending按JSON实数复核。最终验证、数量账与required Result review独立于此开放迁移账。
