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


## TASK12 — BuyItem 双精确形状

只闭 message:65557 与 command:Dfo.Server.Protocol.Game.BuyItem.BuyItemCommand、command:Dfo.Server.Protocol.Game.BuyItem.HousingBuyItemCommand。一个logical/唯一whole CMD接受两个显式精确CLR；plain ordinary Write有单元级NonRequest cap，Housing保唯一reporter Main。具名拒绝真回滚，成功NotiBus逐目标Failed隔离，拒绝ACK外抛仍默认保护，只有既有committed replay许可。旧fine已删除；未关闭其它来源/保护/过渡，未改original/baseline。

实际数量：messages12closed/147pending/14retained；commands13closed/191pending；sources1closed/72pending/12retained；protections5pending/transitions8pending。证据及原caller/事务映射见 verification/evidence/task-12-inspection/workunit-map.md、formal-summary.json、capture-checks.json；ResultR1后strict/8bad与required独立快审另行完成。


## TASK13 — 三个装备效果协议

仅闭 message:65616／65562／65808 与 ReinforceItemCommand、NpcDisjointItemCommand、PortableDisjointItemCommand、EnchantByBeadCommand 四个原精确fine ID。三logical各唯一whole，原前置拒绝无Main/Pending→Rejected，原domain正常拒绝MainCommitted；原slot/provider顺序、三种帧序与SQL原子性不变。初购ICommandProjections复用NotiBus Failed隔离，直接拒绝ACK外抛默认保护；只既有committed replay出口。其它original/baseline/row不动。

实际账：messages15closed/144pending/14retained，commands17closed/187pending，sources1closed/72pending/12retained、5protection/8transition pending。正式本轮为Release build+App/Protocol/Net三full+另外四actualcontrols，其余1793业务case只延续Task12，不称八full；Result/strict/selftest和workunit/capture/source证据见task-13-inspection。


## TASK14 — 五消息／三个装备效果whole

仅闭message:65937/65974/65975/65740/65741及MagicSealCommand/ForgingCommand/OutworldCommand三个原fine精确ID。MagicSeal2/Forging1/Outworld2 logical bindings分别归唯一whole；原早拒绝无Main/Rejected、domain普通拒绝MainCommitted、失败roll有receipt/扣材料、unseal拒绝无ACK和各自原帧序保持。成功原NotiBus普通Failed隔离，直接ACK默认保护，只既有committed replay许可。

实际账messages20closed/139pending/14retained、commands20closed/184pending；source1closed/72pending/12retained，5protection/8transition pending。original/baseline/其它row不动。本轮正式为Release+三full/四actualcontrols，1793业务库存只延续Task12；最终八full/性能/Spec/legacy清除仍pending。证据/Result及strict/8bad见task-14-inspection。


## TASK15 — 四 opening 完整协议与 commit-only 结果

仅闭 message:65696/66055/65744/66640 及 PackageOpeningCommand/AvatarPackageOpeningCommand/MagicBoxSingleOpeningCommand/MagicBoxBatchOpeningCommand 四原 fine。每 logical/精确 shape 唯一 whole，原有效分支 Main，前置拒绝不 Main/Rejected；真实业务动作回滚或已证 SQLite CommitAsync span 确认回滚后才映原失败 ACK，同 entry/原 Ordinal 重试不延期，成功响应/Avatar popup/绝对库存顺序和通知 Failed 隔离保持，仅既有 committed replay。

P3 新结果由真实 source/phase/原 cause/发生身份限制，Guard/Unknown/typed/取消/已提交/清理失败仍默认；RequireRollback(E) 保 E+独立X含同对象和重复声明。旧/新同provider协议 action SQL19/1811 与真实 native Commit19/787 两完整 oracle、三 leaf/facet guard、两 B2/default、mutation、完整 Release 与新八 full、DFO74/7、Golden15/Char10、case delta 和 R1 见 task-15-inspection。

实际账 messages24closed/135pending/14retained、commands24closed/180pending；source1closed/72pending/12retained、5protection/8transition pending。original/baseline/其它 row 不动。最终 T-012 八 full/单 CMD≤2秒测量、Astra/Spec根封存及用户分支收尾仍 pending，不因本Task宣布B完成。


## TASK16 — Quality／RandomOption 完整协议

仅闭message:65617／65992与ResetItemQualityCommand／ResetRandomOptionCommand两精确fine。每logical原shape唯一Direct whole，原domain有效分支一次Main最低Inventory且不造footprint链；normal domainRefusedCommitted，reseal仅null回落，ushort完整。真实action回滚与已证nativeCommit确认回滚才映原0x13，原cause/Error、同entry/Ordinal retry/current replay保持。Quality ACK→target与Random target→material→ACK原字节/顺序、NotiBus普通Failed隔离保持；直接拒绝ACK仍默认保护，只有既有committed replay许可。

原两业务类Throws/SQL/UID/quickslot/stale/nativeFK目的用同provider显式typed domain driver保持。四旧/新完整wire SQL19/1811和真nativeCommit19/787、最低Inventory/自然拒绝、受控NoSession查询边界、E+独立X同对象、ushort真实存储mutation及恢复，见task-16-inspection。完整Release0/0＋三full/四actualcontrol，原74+7/Golden15/Char10与实际N30-D0；其余1800仅Task15库存，不称新八full。

实际pending133message／178fine／72source及5protection／8transition；original/baseline/其它row不改。最终T012新八full/单CMD≤2秒测量、Astra/Spec根封存和用户分支收尾仍pending，不因本单元宣告B完成。


## TASK17 — Socket四logical一个完整CMD

仅闭message65737/65742/66425/66426及原ItemSocketCommand精确ID。四exact ItemSocketRequest绑定同一Direct whole，原领域分支Main传unit.Footprint，所有原DemandActive/目标与验证物料足迹保持；正常domainRefusedCommitted，早拒无Main。sharedpile分组Count2/UID与ItemUsed、AvatarDefaults零消耗仍验证pile/无ItemUsed/FFFF/仅target，原ACK→materials→target当前读不改。只既有committed replay，不新增raw。

真实产品前两窄wire探针是EquipmentEmbed actionSQL与AvatarOpen非default nativeCommit；迁后同oracle、四logical成功拒绝/原35业务测试、真实Take1 storageRED/恢复、默认通知/ACK/E+X保护及三full四actualcontrols/74+7/Golden15/Char10/N30-D0见task-17-inspection。注册before捕获曾遗漏，按主控裁定迁后精确HEAD重建ConfigureServices语义基线，实际时间/源SHA/finally10源恢复单独披露，不称产品前fresh捕获。

实际pending129message/177fine/72source及5protection/8transition；original/baseline/其它行与原md字节前缀保持。另1800仅Task15库存，最终T012新八full/单CMD≤2秒、Astra/Spec根封存及用户分支收尾仍pending，不宣告B完成。


## TASK18 — AvatarCompound 一个完整 CMD

仅闭 message:65635 与 command:Dfo.Server.Protocol.Game.AvatarCompound.AvatarCompoundCommand。唯一 Direct whole 消费原消息/精确连接，一次 Main 传活跃 unit.Footprint；最低 Inventory | AvatarCompound 始终保留。原正常领域拒绝仍 Committed、早拒无 Main、初始 Rejected 不升权。保先创建全部独立新 UID 输出后消费 first/second/box 的事务顺序；首成功只有原 89／131B ACK，重放26B UI Recovery 先于当前库存，不重扣／重授予／ItemUsed。仅既有 committed replay，无新 raw/NonRequest。

产品仍 HEAD 时实际 fresh ConfigureServices capture/family 已先跑；原两窄完整 wire(action SQL19/1811 与真正 native SQLite Commit19/787)及迁后同 provider FK1/两 output/child2/stage/ACK17/全资产回滚/same entry原Ordinal retry/UIcurrent 对照、原27App4codec、Main/足迹/NotiBus Failed与直接ACK默认保护/E+独立X及真实 Ability256 storage mutation红绿见 task-18-inspection。只 Avatar InstanceValue 明确4B按原NewIdentity关系比较，原值及其它确定字段保留；不泛忽略core／随机差异。

实际 pending128message/176fine/72source及5protection/8transition；original/baseline/其它rows和原md前缀保持。三full6981=6963pass+2精确原Dim+16skip，N20/D0；延续8781=8761pass+2+18，另1800只Task15库存，四actualcontrol不称业务full。最终T012新八full/单CMD≤2秒、Astra/Spec根封存和用户分支收尾仍pending，不宣告B完成。


## TASK19 r2 — Warehouse 六 logical 一个完整 CMD

仅闭 message:65841/65842/65843/65844/65944/65967 与 command:Dfo.Server.Protocol.Game.Warehouse.WarehouseCommand 七ID。唯一Direct/Protocol GameWarehouseCommand承接六精确WarehouseRequest，一次Main最低Inventory，无新增足迹能力或额外事务边界。旧fine物理删除，首投递仍原Tx SnapshotAsync/ApplyDecidedWrites receipt；ACK→Main→Account/Personal，01AF所有结果无ACK、成功只有Main。正常domain拒绝仍Committed，earlyRejected无Main，initialRejected不晚升权。signedamount/cost/ticket/UID/core/容量及current限制/原SQL/20并发强断言保持。

原产品仍HEAD时先fresh registration/family，然后old-oracle02四真实wire/原业务33pass；但SELECT* items的Convert.ToString把BLOB记作System.Byte[]，原前时点失败stage完整corehex存在明确缺口，不回填。主控task-rework/readiness的Task19r2 §8授权late exactHEAD重建补证，最终late-core02四pass＋late-named03三pass及11physical快照/finally精确restore，均明确真实晚时点。当前formal02同输入对照所有82Bcore/其它item字段/UIDslots/receipt/frames，仅items.created_at/updated_at跨run作实际SQLUTC时间关系比较、保raw原值；同run rollback全列/时间仍精确相等。fresh requiredreview须判补证充分性，不用主控处置代approval。

四路径deposit value32 UPDATE与01AF list_param16真实UPDATE，各action19/1811与actualparticipant FK1/child/stage/fullreceipt后真实SqliteTransaction.Commit19/787，保cause/ACK10或完全静默、同entry原Ordinal retry及current-only replay。domain mapping/InventoryExpansionException旧pipeline可能Error但handlerInfo，新whole无handledError；generic原两Error→whole一Error；clock在原窄span，unit后才映码/日志/ACK。NotiBus Failed隔离、直接ACK各阶段默认保护（含mapping编码错）与独立E+X保因，取消/typed/guard不恢复；仅既有committed replay许可。

formal02三full7028=7010pass+2精确原Dim+16skip，N47/D0，延续8828=8808pass+2fail+18skip；另1800仍仅Task15库存，四actualcontrol各1pass另列，不称八full/性能完成。pending122message/175fine/72source/5protection/8transition；original/baseline/其它rows及md原前缀保持。T012最终新八full/单CMD≤2秒、Astra/Spec根封存及用户分支收尾仍后继。


## TASK20 r3 — CollectionBox 一个 logical／四 wire 的完整 CMD

仅闭 message:66533 与 command:Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxCommand 两ID。一个精确CollectionBoxRequest、唯一Direct/Protocol GameCollectionBoxCommand<bool>，旧内嵌fine/handler/Tx注册/caller删净；原一次Main与mandatory足迹、四namedcatch带cause回滚、ordinary SQL/native外抛、minimum及主后逐条clock/完整盒及selected重复/ACK exact一次batch保持。新通用SettledInitialBatch贯通registry→签发→opaque原对象→实际batchadapter→窄actualencoder；仅该whole拥有thirdexit，拒绝batch/业务/其它来源默认保护，原两许可/P3/Compose禁用不变。

真正产品前49snapshot与旧四oracle全保，formal新whole对应Select/Put fixedencoder同cause/actualentry原Ordinal/Committed/nofence/failedbatch零登记/sourceclose/current，以及Putaction19/1811和FK1/child1/完整mutation后真实Commit19/787/全列回滚/retry/current逐项相符。成功Put→Withdraw真实UID/82Bcore/Box清空/帧序/clock与成功Withdraw actualencoder动态注错已补；replacement callback真发生且旧context不误交replacement。随机领域事件三GUID只按owning源与主控observer澄清做跨phase双射及Delivery外键/batch/origin/sequence payload关系校准；两items时间保72个原字段出现和runnerUTC关系，same-run rollback所有列仍exact，不丢整事件/列/NULL/BLOB。

原caller吞guard后真实SQLite+37的两RED→GREEN；r2暴露typed/cause覆盖八强fail及lateForeignScope nofence RED，经r3仅Bus末端DemandHealthy桥＋Executor同P引用树保完整body修正，原8/两caller/late同目标GREEN、unknown/Begin/call-kind/liveforeign/first/unrelated/nested/独立cleanup及三个actualraw无ProgramError控制保持。完整provenance mutant可编译，callback真1/actualencoder0却错误raw/nofence为RED；finally精确恢复同7目标GREEN。

finalfreeze18source上Release0警告0错误、八无filter full8883=8863pass+2准确原Dimensionfail+18原skip，原8828逐display/status/multiplicity全保，N55=54App+1Protocol、D0；DFO原74/七uniquecontrols/outside-exe-orphan0，Golden15与Characterization10。pending121message/174fine/72source/5protection/8transition。suite耗时/资源仅观察，非单CMD≤2秒或A21实机；T004/B/Spec根/Astra/分支收尾仍后继。


## TASK21 r1 — InventorySort 三logical／两typed whole

仅闭65556/66250/66251及SortItem/SortLock两个fine，Direct/Protocol三route→两whole，无registration exit。两个old fine物理文件删除，receipt原形搬迁；普通Write([],Ordinary,None)、原action内正常拒绝、Sort→character.Load隐藏写→account.Load→clock同原事务；原通知字节段/单recipient prepared batch/Warning+Failed隔离保留。空participants只还原原fine无selected marker的范围，NonEffect重复同seq实际再执行，无Main/footprint/ledger/attempt/replay。

42项真正before封存，原40强case意图保留并迁stale/tower driver；新16 oracle和注册1全部passed。原SQL19/1811、真实FK deferred nativeCommit19/787、完整stage/资产BLOBhex/NULL/同run rollback精确，十组原新对应及跨run两items时间/三eventGUID具名关系保留。emptyCommitted拒绝ACK旧raw与新typed PostCommitPublicationFailure/inner同cause为Task10已审解释，明确不称异常相同；新final直接Same物理close，旧empty只Single/IsClose/nofence，旧JSON硬码不是该case独立Same证据。

Level→Default有效mutation 1行为RED+2可达正控，finally逐byte恢复3/3。freeze01 Net两Connection refused保raw/栈/端口诊断，隔离2/2，根因未证且不归Dim；仅empty身份断言增强后freeze02重跑Release0/0、三无filterfull App5128/Protocol1773/Net199及另四freshcontrols。原8883逐display/status/multiplicity全保，N17/D0；加另1800 Task20库存8900=8880pass+原Dim2fail+18skip，不称八freshfull。DFO原74/七controls/outside-exe-orphan0、Golden15/Char10、注册capture仅3message/2fine替换、三出口种类及所有owner原样。pending118message/172fine/72source/5protection/8transition；单CMD≤2秒、A21与Spec最终审查/主控checkpoint仍后继。


## TASK22 r1 — SellItem 单 logical／单 typed whole

仅闭65558与SellItemCommand fine，Direct/Protocol exact SellItemRequest→GameSellItemCommand<bool>，无registration exit。old fine物理删除，Data receipt/result、原ItemSaleCompleted与codec原样。普通Write([],Ordinary,None)，保mapping→session→sum→online及action内clock/domain；正常Refused Commit，仅ItemSaleRejected窄路保存cause、catch外RequireRollback，完整unit后ACK；原Info/业务零价ERROR/单ACK及Warning+Failed隔离保。无Main/足迹/attempt/ledger/replay或新NPC/online gate。

35项真before，原59强case及新14 flow在old73/73实跑；新增架构实际坏model与注册断言。原新13对应全列资产、两items时间具名UTC/三event GUID双射与FK关系；真实action SQL19/1811、deferredFK nativeCommit19/787、samecause/fullrollback；三拒绝pre无Tx、normal Committed、cap ConfirmedRollback各分开。normal ACK oldraw→newtyped PostCommitPublicationFailure/innerSame/fullchain明确单列，不normalize；其余rawSame，均精确物理sourceclose1/ACK0/nofence/资产不变。

仅删RequireRollback实际资产RED：cap100030/old100000/price31/qty10卖1，ACK22却gold100030/qty10；cap0另因Commit分类RED，两正常卖出正控pass，finally原bytesSHA恢复同四目标4/4。首formal构建新增断言namespace拼写错留raw；02 Net唯一原日志fragment过时，r2仅准第11源一literal迁CMD身份，原TCP目标1/1后freeze03完整Release0/0、三无filterfull及四freshcontrol；原8900逐display/status/multiplicity保，实际N=16/D=0，总8916=8896pass+原Dim2fail+18skip，另1800为Task20库存不冒八freshfull。原DFO74/七controls/outside-exe-orphan0、Golden15/Char10、capture仅一message/一fine→whole，其它family/三许可owner均原样。T012单CMD2秒/A21/Spec最终审查及主控checkpoint仍后继。


## TASK23 r1 — DeleteItem 三操作／单 typed NonEffect whole

仅闭message65554及DiscardItemCommand、ConsumeSkillMaterialsCommand、ConsumeEffectMaterialsCommand三fine。精确DeleteItemRequest→GameDeleteItemCommand<bool>，Direct/Protocol，无registration exit；旧三物理源/handler/注册和caller删除，原domain/多cost API/通知/codec/共享core全部只读。每有效branch原一次ordinary Write([],Ordinary,None)，op1 clock和carried QuestHoldingsChanged同事务，material先全部预验再扣款和MaterialConsumed同事务，材料preparedAt保事务外UTC；正常拒绝Commit，异常Abort，无新Main/足迹/ledger/attempt/replay/raw/P3。

真before43项封存后才产品，原入口69/69＋codec11/11；30组旧新完整资产/事件/metadata/帧字节对应，原stale/multicost/duplicate接缝用实际domain＋显式事务保，单cost真实wire另护。实际action19/1811及discardquest/material各同participant deferredFK nativeCommit19/787、FK1/child>0/原causeSame/ConfirmedRollback全列exact/同seq重试；普通composer及actualencoder仍Warning+Failed隔离/单preparedbatch/无半ACK/已提交资产不逆转。op1/op2前置ACK故障rawSame，normalCommitted ACK默认typed PostCommitPublicationFailure/innerSame，外壳差异明确，实际EndedAJob/完整chain/物理sourceclose Same1/nofence不变，无新增许可。

可编译op3数量+1 mutation真实剩0/事件count2资产RED，op1/op2正控pass，finally原bytes/SHA恢复同3目标3/3。freeze01 13源=10present+3deleted；Release0/0、三无filterfull及四freshcontrol，原8916逐display/status/multiplicity全保，N=31/D=0，总8947=8927pass+原Dim2fail+18skip；其余1800为Task20库存非八freshfull。DFO原74/7control/outside-exe-orphan0、Golden15/Char10。capture203→201命令/173logical，仅一message和三fine→一whole；其它families/三许可owners等原值。原非NULinventory漏192中文/旧TRX诊断保原map，产品前NUL完整14352=11owned+14341nonowned逐byte HEAD，最终仅canonical2排除后14339不变。T012单CMD2秒/A21/Astra/Spec最终/主控checkpoint仍后继。


## TASK24 r1 — RepairItem 普通8B／自动7B单 typed NonEffect whole

仅闭message65559及RepairItemCommand fine。精确RepairItemRequest→GameRepairItemCommand<bool>，Direct/Protocol/无exit，一fine物理源/handler/注册/caller删除；普通/auto在原位置各一次ordinary Write([],Ordinary,None)，正常拒绝Commit、SQL/commit异常Abort，auto Direct PremiumUsageRecorded→额度CAS/UsageQuotaChanged与durability/gold同事务。成功clock在Tx后；原ACK→可选Premium单batch与Warning+Failed隔离保持，无新Main/footprint/ledger/replay/raw/P3。

真before31项封后才迁产品，原入口45/45与codec4/4；21组新旧57全列asset快照及具名SQL时间/GUID双射/DeliveryFK保完整core/BLOBhex/NULL/次数/period/权益和帧。ordinary/auto各action UPDATE19/1811、sameparticipant deferredFK nativeCommit19/787/FK1/child>0/阶段与causeSame/完整回滚及同seq当前重做。pre quick1拒绝ACK编码故障rawSame，normal普通与auto拒绝ACK变既有默认PostCommitPublicationFailure/innerSame，明确两外壳差异，原chain/EndedAJob/物理sourceclose Same/nofence保持，无第四许可。

可编译auto调用误换ordinary mutation实际auto扣113且usage0为资产RED，ordinary正控pass，finally原wholebytes/SHA恢复同2目标2pass。正式finalfreeze02 9源8present1deleted，Release0/0及三无filterfull+四freshcontrols，原8947逐display/status/multiplicity全保N=22/D=0→8969=8949pass+原Dim2fail+18skip；其余1800是Task20库存，非八freshfull。DFO74/7/outside-exe-orphan0、Golden15/Char10。actualcapture201cmd/173logical，原一fine→whole唯一65559，family/三许可owners其它逐值不变。完整NUL14505=7owned+14498nonowned，最终仅两actualcanonical排除后14496全SHA同。T012单CMD2秒/A21/Astra/Spec终局及主控checkpoint仍后继。


## TASK25 r1 — CrystalContract 两字节选择单 typed NonEffect whole

仅闭message66072及SelectCrystalContractCommand fine。精确request→GameCrystalContractCommand<bool>，Direct/Protocol/无exit；旧fine两类型从mixedslice删除，其余消息/decoder/encoder/通知chunks逐byte保持。session/online静默与原domain字段/current/权益/两列保存保一次ordinary Write([],Ordinary,None)，normalfalse Commit、SQL/commit异常Abort；unit后Info→world复核→成功ACK/CubeProgression一个batch或原拒绝ACK，普通通知Warning+Failed隔离，无Main/footprint/ledger/attempt/replay/raw/P3。

真正before33项封后才迁产品，原registered2B入口31/31+原codec7/7，21组旧新54全列asset快照及具名SQL时间/GUID双射/DeliveryFK保护两角色condition/BLOBhex/NULL/非选择列/权益/其它资产与帧。action19/1811与sameparticipant deferredFK nativeCommit19/787/FK1/child>0/action完成stage/causeSame/fullrollback及同seq当前重存有直接证据。normal非法及缺失权益拒绝ACK旧raw→既有默认PostCommitPublicationFailure innerSame两处壳差异明确，原chain/EndedAJob/物理sourcecloseSame/nofence保；没有原preACK，不虚构第四许可。postworld proxy仅证保存后分支顺序，不冒实际并发退出。

可编译selection3→5域实参mutation实际保存grade5资产RED、selection0正控pass；finally原wholebytes恢复同2目标2pass。finalfreeze01七present/零删除文件，Release0/0和三无filterfull+四freshcontrols，原8969逐display/status/multiplicity全保N=22/D=0→8991=8971pass+原Dim2fail+18skip；其余1800仍Task20库存，非八freshfull。DFO74/7/outside-exe-orphan0、Golden15/Char10。actualcapture173/201只有66072/一fine→whole，purefamilies/三许可所有owner其它精确同原。完整NUL14646=5owned+14641nonowned，排两actualcanonical后14639原SHA同。T012单CMD2秒/A21/Astra/Spec终局/主控checkpoint后继。


## TASK26 r1 — MoveItemSpace 完整 NonEffect whole与库存移动领域能力

仅闭message65555及MoveItemSpaceCommand fine。新whole一次ordinary Write([],Ordinary,None)驱动具名InventoryMove领域能力；原算法全体body按类型/方法/未读context机械归一精确，Session=null/Tower业务地址/两wear同Tx/UID/pet/mirror等旧18源能力保持。mixed消息/codec/encoder逐byte同原，无兼容fine或隐藏总线。postTx cargo/currentView/Housing读取不添unit，原两catch及同frames部分追加保持，Housing失败保已追加appearance，最后一次explicit必达登记。

真正before55项封后才产品：原strong App249/Protocol106及21真实wire观察；新strong249/107。21原新54全列资产快照/258具名SQL时间和GUID双射/DeliveryFK，真实双端action19/1811与sameparticipant nativeCommit19/787、source已扣/完整UID-core-BLOB-null回滚及同seqretry。noWrite encoder原rawSame；normalCommitted成功与拒绝的encoder oldraw→既有默认typed innerSame两处壳差异明列，实际rootchain/EndedAJob/精确sourcecloseSame/nofence和资产结果保，不增第四raw。受控wear/currentview baseline+真实库存/Housing正常null只证明本族顺序，不冒生产全量投影或A21。

restatement guard mutation导致真实物品逆向回65资产RED、fresh drag正控pass，finally原domainbytes恢复同2目标2pass。finalfreeze01 27present/0文件删除；Release0/0与八fresh full/七实际controls，原8991逐display/status/multiplicity全保N=22/D=0→9013=8993pass+原Dim2fail+18skip，DFO74/7/outside-exe-orphan0，Golden15/Char10。actual173/201只有65555/一fine→whole，其它families/三许可owner精确。完整NUL14793=22owned+14771nonowned，排actualcanonical2键后14769SHA同。Host01唯一channel端口False断言原raw保留，邻近bind48仅旁证/未证明具体端口竞争者或唯一根因；samefreeze无filterHost02全203pass，最终按02选取同203显示名/参数/重数，无新增baseline豁免。T012单CMD2秒/Astra/Spec终局/主控checkpoint后继。


## TASK29 r2 — Cera 普通购买整协议与精确买家失败保护

仅闭 `message:65600` 与 `command:Dfo.Server.Protocol.Game.CeraShopPurchase.CeraShopPurchaseCommand`。唯一 GameCeraShopPurchaseCommand 返回原非空结果，薄 handler 仅入队，旧 fine／注册／caller 删除；一次 Ordinary WriteResultAsync、NonRequest、唯一买家含原 physical context 与显式 metadata，无 Main／操作账本／重放／新 raw。正常拒绝仍 Committed；窄 clock/domain 普通异常 RequireRollback 保因，真实 KnownStorage nativeCommit787 确认回滚后映原 ACK1，健康均不围栏关闭。

按已批准 VC-B4，C1 nonfallible、C2 SQL＋第二 rollback cause、C3 真 whole scope Dispose 均保 typed 原因、精确 Fence／Block／DiscardExact／原来源实际关闭及原调用链；C3 已登记成功帧按原 sender 顺序先发后关，替代身份／运行态／另一 physical 保留。原 Buy/Command 强断言改真实 domain＋事务＋现 TestEffectFootprints 测试接缝，Handle 真 decoder→同 provider dispatcher；NameTag setup job 与入口分开，Host 真 HTTP grants＋三次 whole receipt／97810／三 operation 保。

取证与具名跨 run 时间／GUID 校验、原子回滚、消费者 mutation、最终 full／DFO／capture／完整所有权及局限见 TASK-29 Result 和 `verification/evidence/task-29-inspection/`。仅这一逻辑消息与原 fine 两行闭合，其它 canonical 行不变；required 独立 review 与主控 checkpoint 后继，非 Spec002 整体完成。

本单元具体证据文件：`verification/evidence/task-29-inspection/after-02/scope.json`（实际关闭链／出站次序）、`verification/evidence/task-29-inspection/mutation/result.json`（消费者变异与恢复）。原目录 locator 的 preclose01 拒绝保留；仅将两 Cera 行定位器修正为文件，运行时源码与正式全量 freeze01 相同，最终 freeze02 另记此治理差异。


## TASK31 r1 — Mailbox 六logical／唯一whole

| canonical ID | 本单元实际去向与边界 | 证据 |
|---|---|---|
| `command:Dfo.Server.Protocol.Game.Mailbox.MailboxCommand` | 旧 fine／handler／注册／caller删除，唯一 GameMailboxCommand；共享 domain 调用方保原事务。 | `tasks/TASK-31.md`、`verification/evidence/task-31-inspection/rework-r3/report.md`、`verification/evidence/task-31-inspection/checks-final.json` |
| `message:65630` | GameMailboxCommand／SelfSettledAction／Ordinary[] None；真实 Abort入口pin、单次fact、完整cleanup后恢复；不确定默认失败实际sourceclose。 | `tasks/TASK-31.md`、`verification/evidence/task-31-inspection/rework-r3/report.md`、`verification/evidence/task-31-inspection/checks-final.json` |
| `message:65631` | GameMailboxCommand／SelfSettledAction／Ordinary[] None；真实 Abort入口pin、单次fact、完整cleanup后恢复；不确定默认失败实际sourceclose。 | `tasks/TASK-31.md`、`verification/evidence/task-31-inspection/rework-r3/report.md`、`verification/evidence/task-31-inspection/checks-final.json` |
| `message:65632` | GameMailboxCommand／SelfSettledAction／Ordinary[] None；真实 Abort入口pin、单次fact、完整cleanup后恢复；不确定默认失败实际sourceclose。 | `tasks/TASK-31.md`、`verification/evidence/task-31-inspection/rework-r3/report.md`、`verification/evidence/task-31-inspection/checks-final.json` |
| `message:65670` | GameMailboxCommand／SelfSettledAction／Ordinary[] None；真实 Abort入口pin、单次fact、完整cleanup后恢复；不确定默认失败实际sourceclose。 | `tasks/TASK-31.md`、`verification/evidence/task-31-inspection/rework-r3/report.md`、`verification/evidence/task-31-inspection/checks-final.json` |
| `message:65851` | GameMailboxCommand／SelfSettledAction／Ordinary[] None；真实 Abort入口pin、单次fact、完整cleanup后恢复；不确定默认失败实际sourceclose。 | `tasks/TASK-31.md`、`verification/evidence/task-31-inspection/rework-r3/report.md`、`verification/evidence/task-31-inspection/checks-final.json` |
| `message:65860` | GameMailboxCommand／SelfSettledAction／Ordinary[] None；真实 Abort入口pin、单次fact、完整cleanup后恢复；不确定默认失败实际sourceclose。 | `tasks/TASK-31.md`、`verification/evidence/task-31-inspection/rework-r3/report.md`、`verification/evidence/task-31-inspection/checks-final.json` |


## TASK-41：城镇移动与传送

仅闭 SetUserPosition／SetUserArea／Teleport 三消息、旧 Teleport fine 与 F-88-teleport；原始行与非目标保持。F-109移除只做空值校验的 represented 参数，无单独canonical行，不伪造额外闭合数。消息76/159、fine61/204、来源1/73、保护2/5、过渡0/8，T-007仍未整体收口。

原普通位置独立保存、婚房先提交返回位置再记内存、101目的无持久写及Teleport单Main保持；精确guard、真实受众和投影顺序在CMD内。唯一可见差异VC-B10与实际故障、纯准备patch、19新增用例和诊断边界见 [TASK-41](../tasks/TASK-41.md) 及 [核验摘要](task41-verification.md)。

## TASK-57：两塔内部返回来源

五fine（StartBossTower、LeaveBossTower、SynchronizeBossTowerLeader、LeaveIllusionTower、SynchronizeIllusionTowerLeader）与两ReturnSweep来源共七行闭合，零消息；每塔四类Direct／Internal CMD，旧fine／Delivery／bus Coordinator清零，Refill保留。ordinary Write的owner与selected保护独立，原每member Tx及两塔取消差异保留；B9／P11具名交错、合法timer修复、whole累计physical及已commit取消默认typed见VC-B37～39与 `task57-verification.md`。

仅本七行变化，全部original／行序／非target恒等；消息136/159、fine123/204、来源4/73（另12 retained），保护2/5、过渡0/8。最终主控旧回放及恢复后fresh八full9700pass＋原2fail＋18skip，实际八注册／同实例／五fine0、strict与八坏例通过；原日志和变更审计在 `evidence/task57`／`logs/task57-final-r2`。不以本Task关闭整个T-008／T-010或Spec B。

## TASK-58：祭坛三消息与两塔余项核清

本批仅闭合 message:65791／65794／65853 及旧 AltarDeathBatchCommand 共四行；三个 whole CMD 复用 AltarCommandPhases 实质阶段，死亡批次单事务与 Distinct、完整 cohort 与报告者 physical、Ready 后配额、Difficulty 先 ACK 且抑制重复0129、原 Wake 位置保持。罕见基础设施失败统一引用 VC-B40；没有新能力或注入类别。实际验证在 [task58-verification.md](task58-verification.md)。

AltarActionCommand 仍由 AltarScheduler 及 DungeonPartyWipeService→AltarProjectionDelivery.Fail 消费，RefillOnlineBossTowerTicketCommand 仍由 SelectCharacterHandler 和 BossTowerDailyRefill 消费，保持 pending；两塔其他已闭行不重做，T-008 整簇仍开放。JSON 是唯一进度依据，original、原顺序及非本四行均保持。


### TASK-64：生命周期、事件、启动与计划调度

仅闭合 `source:lifecycle-connected`、`source:lifecycle-disconnected`、`source:event-consumer`、`source:event-depth-settlement`、`source:startup-epic-book`、`source:startup-experience-storage`、`source:scheduled-task` 七来源及其五个最后消费者 fine；JSON 是唯一逐行权威。消息159 closed／14 retained；fine202 closed／2 pending；来源73 closed／12 retained；保护2 closed／3 pending，过渡8 pending。ReadReplayState 与 ReportDungeonQuestFacts 留 T-011，original／ID／顺序与非目标不变。

真实业务移入 typed CMD／注册拥有者，Direct事件不排队。原生命周期代次／清理／关闭链、Reliable同事务与独立retry、LiveOnly先提交后投影、启动准备位置、scheduled实际开始时最新Requested及原五业务阶段保持；部分业务失败经固定完成桥保原Aggregate和重试，其他infra按VC-B40。最终证据见 `task64-verification.md`：r2 App＋r1其余七项，9848＝9828通过＋2原Dimension失败＋18原跳过，新增27／删除0；DFO原67＋7、strict及八坏例通过。此Task不宣告T-011／B／最终性能完成。
