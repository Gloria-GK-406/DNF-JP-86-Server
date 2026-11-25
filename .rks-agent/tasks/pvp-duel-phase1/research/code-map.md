# 决斗场一期代码地图（基线 ef20398a3，Explore 调研摘要）

路径相对 `Server/src/`，测试相对 `Server/tests/`。

## 事务参与者（内存态）
- 接口 `Abstractions/Transactions/DomainTransactions.cs`：`ITransactionParticipant`（13，`CommitCanFail`、Prepare/Commit/Rollback）；`IDomainTransaction`（39，`Enlist(identity, create)`、`RegisterAfterCommit`、`RequireRollback`）；`IDomainTransactionAccessor.Current`（69）。
- `Application/Transactions/DomainTransaction.cs`：Enlist 137–176（同 identity 一次；`CommitCanFail=true` 每事务最多一个，SQLite 已占）；CommitAsync 178–269（全部 Prepare → 可失败者 Commit → 其余 Commit → outbox → afterCommit）。
- 模板：`Application/World/IllusionTowerDomainService.cs` 33 `Pending => scope.Current!.Enlist(this, () => new Changes(this))`；34–38 Lookup 读本事务暂存；47–51 无事务即抛；334–394 `Changes`（Prepare 乐观校验，Commit 应用，Rollback 空）。用 `IDomainTransactionAccessor` 的写法见 `World/DungeonParticipationDomainService.cs:12`。
- Wake 在参与者 Commit 内：`World/AltarDungeonEngine.cs:361-366`。
- 领域服务不得自己开事务（`GameplayWriteEntryTableTests.ScopeBeginSites`）；写方法须 `GameplayWriteAssertion.Writing("类型.方法")`。

## CMD 与切片
- `Application/Commands/ICommandWorkUnits.cs`：`CommandWorkUnitDeclaration(Name, Participants, ...)`（9）；`WriteResultAsync/WriteAsync/ReadOnlyAsync`（195–213）；TResult 必须 notnull。Participants 前后 `DemandCurrent`，可传 `[]`。
- 切片样板 `Protocol/Game/Blacklist/`（Messages、PayloadCodec、Decoder、Encoder、Commands、Registration；`ProtocolModule.cs:327` 调用）。CMD 回包 `GameCmdEnvelope.Accept/Refuse`。
- 投影 `ICommandProjections.ProjectAsync(audience, notification)`；发件箱 `IGameCommandOutbox.RegisterCmdAsync/RegisterNotiAsync/RegisterExplicitAsync`（写事务内登记的条目提交后才发）。
- CMD 注入白名单 `Application.Tests/Commands/CommandHandlerArchitectureTests.cs:35-58,274-279`：Abstractions 中 `*DomainService`/`*DomainStore` 接口 + 机制白名单；不可注入规则服务、内容源、ICommandBus、具体类。

## 数据库
- 版本 `Persistence/Schema/CharacterDatabaseSchema.cs:22`（=10）；建表 184/441；`character_pvp` 427、`character_pvp_missions` 428。
- 迁移 `Tool/DbMigrate/Migrator.cs:15-28` `Steps`；样板 `PlayerPortalUpgrade.cs`（SQL 复制、不引用声明）；`Tool/DbMigrate.Tests` 的 `SchemaVersionTests` 要求迁移后结构与新建库一致；`Document/DatabaseSchemaMigration.md` 31–40 加版本说明。
- DomainStore 样板 `Abstractions/World/SaoTower.cs:26` + `Persistence/Characters/SqliteSaoTowerDomainStore.cs` + `PersistenceModule.cs:81`；无事务写即抛（`Persistence/Database/SqliteDatabase.cs:58-104`）。

## character_pvp 读写方
- `Abstractions/Persistence/CharacterBodyTables.cs:93-100,161-175`（`CharacterPvpRow`、`ICharacterPvpTable`）；`Persistence/Characters/CharacterBodyTables.cs:93-108,179-197`；`PersistenceModule.cs:139`。
- 建角插入 `Application/Characters/CharacterRosterService.cs:233`；入场读取 `CharacterEntryAssembly.cs:144-162`（缺行抛）、216（段位字节）、535/544/549（范围校验）；`CharacterEntryStateServices.cs:24,42-43`；USERINFO `Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs:761`。
- 技能初始化 `Application/Characters/SelectCharacterPersistenceServices.cs:18-56`；`SelectCharacterPreparationService.cs:178-189`；`CharacterCurrentStateQueryDomainService.cs:98-104`。

## 现有大厅
- `Application/Sessions/PvpLobbyDomainService.cs`；契约 `Abstractions/Sessions/ISessionPresence.cs:145-195`（`PvpLobbyParticipant`、`PvpRoom`）；注册 `SessionsRegistration.cs:23`。
- 读取方：`SessionAdjacentEntryDomainService.cs:62-87`；`SelectCharacterLifecycleServices.cs:207`（清理）、408–446 `PlanPvpAsync`（房间须 Published、Name 非空、8 座位）、455、480。
- 选角 `Protocol/Game/SelectCharacter/GameSelectCharacterCommand.cs:391-409,714-721,749-754`。
- 编码 `Protocol/Game/StateSynchronization/SupplementalSynchronizationCodec.cs:94,158,177,210`；消息 `SupplementalSynchronizationMessages.cs:105,111,166-170`；状态 `Data/Game/StateSynchronizationModels.cs:14-45`；通知 `Protocol/Game/SelectCharacter/PvpParticipantPublishedNotification.cs`。
- 写入口表 `Application.Tests/Execution/GameplayWriteEntryTableTests.cs:110,471-478`。

## 组队邀请
- `Protocol/Game/Party/PartyCommandSlice.cs:30-48,124-150,345-358`；`PartyProtocolCommands.cs:150-226`（165 type≠0 忽略）、228–294（243）；拒绝体 `PartyRefusals.cs:61-100`；邀请状态 `Application/Parties/PartyService.cs:35`（不事务化，30s）。
- NOTI 0x0007 `PartyNotificationSlice.cs:20,79-105`（body[2] 固定 0）；NOTI 0x000B `PartyEndpointsMessage` 150–190（端口大端）；端点来源 `Application/Parties/PartyMemberSessionEndpointSource.cs`。

## 调度、停服、清理
- `Application/Execution/TypedGameplayTimerSource.cs`（`SourceName`、`RunRoundAsync`、`SubmitAsync`、`ItemAsync`；`AddTypedGameplayTimer<T>()`）。样板 `Application/World/DungeonDeadlineScheduler.cs`、`Protocol/Game/Altar/AltarDeadlines.cs:17-68`、内部命令注册 `Protocol/WorldMaintenanceCommandRegistration.cs:15-16`。
- 停服 `Application/Execution/GameplayExecutorLifetime.cs`：按 DI 顺序 drain（Application 的 drain 先于 `Networking/Runtime/TouchSocketTransportDrain.cs`，后者拆连接并走断线清理）。无现成"拆连接前"钩子：在 Application 注册一个 `IGameplayProducerDrain`，排在传输 drain 之前提交内部 CMD。
- 清理 `Application/Sessions/SelectCharacterLifecycleServices.cs:33`（`AffectedSessions` 57–64；`CleanupAsync` 66–260；`SettleStepAsync` 308–318）；包装 `Application/Commands/CommandSelectedSessionCleanup.cs:22-48`；调用方 `OnlineUserConnectionObserver.cs:21`、`GameReturnToSelectionCommand.cs:59`、`GameSelectCharacterCommand.cs:77`。
- 离场队列样板 `Protocol/Game/TowerDepartureProjectionQueue.cs`、扫描 `TowerReturnSweepSources.cs`、`TowerReturnCommands.cs:119-131`、注册 `ProtocolModule.cs:333-355`。

## 位置、内容、频道
- `Abstractions/World/ITownTransitDomainService.cs:77,81`（`ReturnToTownAsync`、`LeaveTown`）；`TownAreaDepartureReason` 在 `ITownAreaDomainService.cs:87`（无 PvP 成员）；用法 `DungeonSelectionCommandPhases.cs:250-252`、`GamePartyTransitionCommandHandler.Stages.cs:78-86`。
- 内容源样板 `Abstractions/World/SaoTower.cs:13` + `Infrastructure/Content/PvfSaoTowerContentSource.cs` + `InfrastructureModule.cs:36,202`；`PvfDocument.TryOpen`/`PvfSections` 见 `PvfEquipmentRepairDefinitionSource.cs:66-69`；规则服务 `Application/World/SaoTowerRuleService.cs`。`pvp_ref.etc`、`pvpmapbasicdata.etc` 无现成读取。
- 游戏日 `Abstractions/Configuration/IDailyResetRuleService.cs`（`PeriodDay(now)`）。
- 频道 `Application/Channel/ChannelEntryDomainService.cs:93-111`；`ChannelEntryIdentity`（`Data/Channel/ChannelModels.cs:50`，`LoginEnvironment`）；`IChannelEntryDomainService.ResolveAsync(listenerPort)`。

## 架构测试与测试设施
- `GameplayStateCensusTests.cs:86`（Exemptions，timer 源 E1）；`DungeonRunKindTests.cs:64-100`（源码扫描兜底分支样板）。
- `tests/Support/TestGameplay.cs`；处理器测试样板 `Protocol.Tests/Game/Joust/JoustQueryHandlerTests.cs`、`Game/Party/PartyCommandHandlerTests.cs`；TCP 集成 `Networking.Tests/Integration/PartyEstablishmentWireTests.cs`、`Characterization/CharacterizationSteps.cs`、PvP 频道整命令测试 `Characterization/SelectionLifecycleWholeCommandTests.cs:60-93`（时钟冻结，`Decorate` 只能装饰 `*DomainService`）。

## 消息目录
- `Data/Networking/NetworkMessages.cs`：CMD `0x0001_xxxx`、NOTI `0x0000_xxxx`。0x0032–0x003B、0x00C3、0x012A 均无成员；NOTI 0x0029 现由原始 `Payload(0x0029)` 发；NOTI 0x0035 已被点券余额以原始 Type 占用（与 CMD 0x0035 不冲突）。

## 基线测试
- `main@ef20398a3` 全量测试有 48 项失败（真实 PVF 内容与录制转写类），清单 `verification/baseline-failed.txt`。
