# 调研 05：测试与验证基础

基准 `a02bce46`（只读盘点；基线数字取自 `b01f6841` 的既有日志 `.rks-agent/tasks/session-currency-checks/verification/logs/final-build-test.txt`，未新跑）。路径相对 `Server/`。

## 项目与冲击

| 项目 | 方法/用例 | 层级 | 受重构冲击 |
|---|---|---|---|
| Application.Tests | 2862/4342（基线失败 2，跳过 16） | 领域服务、命令、经真实总线的 DI 用例 | 高 |
| Protocol.Tests | 1129/1694 | codec 向量；直接 new handler 配假总线；一条真实 socket 用例 | handler 部分高，codec 部分低 |
| Networking.Tests | 74/97 | 帧、运行时；Integration 24 文件 39 方法走真实 TouchSocket | 中 |
| Host.Tests | 120/165 | 真实进程、真实 TCP、GM HTTP | 低到中 |
| Persistence.Tests | 334/475 | 真实 SQLite | 低 |
| Infrastructure.Tests | 487/969 | PVF 内容（272 方法需 PVF） | 低 |
| Pvf.Tests | 63/80 | 档案解析 | 无 |
| HttpApi.Tests | 17/50 | HTTP 管线 | 低 |

全量基线 7872 用例。

## 驱动方式

- **A 直接 new handler + 假 `ICommandBus`（冲击最大）**：测试中 33 个假总线，其中 20 个在 `Protocol.Tests/Game`；30 个文件直接 new `*CommandHandler`；Protocol 27 个文件 new 消息 handler 调 `HandleAsync`。典型 `MonsterDeathHandlerTests.cs:62,89,467`（按命令类型拦截、断言顺序）。handler 只入队后整类失效。
- **B `ServiceCollection` + 真实模块 + `ICommandBus`**：85 个文件（Inventory 40）。命令类型改成协议级后编译失败，但断言的结果（数据库状态、帧）大多可复用。
- **C 真实主机**：见下。

被引用最多的共享件：`DomainTransactionScope` 144、`ProtocolModule` 64、`TestSessions` 37（`tests/Support/TestSessions.cs:19`，只依赖在线身份 API，删锁后大概率可用）、`RecordingSender` 36（31 处各自声明）、`IGameMessageSender` 假实现 46 个（「发送即同步记录」模型，改发件箱后断言时机要重写）、`InventorySide` 17、`ProjectionEventService` 16、`CurrentCharacterStub` 15、`LegacySessionSnapshotFixture` 12、`MoveItemFixture` 6。

整体失效：`NotificationWorld`（`Application.Tests/Notifications/NotificationWorld.cs:179`，内置每收件人期限与 RetryInterval :190-205）；判据 `CommandHandlerArchitectureTests.cs:58,84`、`CommandRegistrationTests.cs:82,114`（断言 ExecutionKind/AccessMode）、`CommandBusTests.cs:83,127,183,191`（嵌套派发、租约释放）。

## 线级与字节级证据

- `Networking.Tests/Integration`（24 文件 39 方法）：真实模块 + TouchSocket（`TestInfrastructure.cs:9`），黑盒，结构重构后基本可原样作证。例外：4 个文件用 `ICommandBus` 准备数据（`GameLoginIntegrationTests.cs:60-70`、BossTower、CardRewardReconnect、IllusionTowerStage）；`HandlerFailureConnectionIntegrationTests.cs:79`、`UnknownInboundFrameIntegrationTests.cs:131` 替换 `INetworkMessageDispatcher`；`BossTowerFrameIntegrationTests.cs:53` 打桩领域服务；`OutboundSendFailureTests`、`TouchSocketNetworkMessageSenderExplicitDeliveryTests` 测的正是要换掉的发送语义。约 11 个文件断言十六进制字节。
- codec/帧/负载/转写测试：82 文件 529 方法（Protocol 73 文件），纯编解码，原样保留；其中 9 个文件调通知 handler 的 `ComposeAsync`（如 `BuyItemCodecTests.cs:30`）。
- 金样本：`SelectCharacterAcceptedTranscriptTests.cs:29`（62 帧 SHA256，经注册编码器，不经 handler）保留；持久化表征 `EntryLoadCharacterizationTests` 等保留；`Application.Tests/Marriage/*CharacterizationTests` 5 个文件经命令总线，需改。
- 工具：`Tool/WireSmoke`（`capture.py` + `framediff.py`）真实 TCP 录建角、选角、接任务三段帧与数据库行，适合前后对比。
- 缺口：没有覆盖全部协议的整段会话回放。

真实 TCP 证据只有：`Networking.Tests/Integration`；Host.Tests 的 `AvatarCompoundTcpTests`、`ChannelHostStartupTests`、`LauncherAuthenticationEndToEndTests`、`LauncherHttpHostStartupTests`（共 11 方法，`EndToEndHostHarness.cs:345` 拉 `dotnet` 进程）与 `ComponentLifecycleTests`（2）；`Protocol.Tests/Game/ItemPathLiveTests.cs:62`。

## 依赖并发与时序的测试

`Task.WhenAll` 47 个文件、TCS 32 个、引用 Gate 34 个。

- 测门与锁本身（删除）：`OnlineUserExecutionGateTests`(5)、`SelectedSessionTransitionGateTests`(12)、`TransactionalCommandSerializationTests`(3)、`MultiAccountExecutionTests`(2)、`LoginSessionQueuedCommandTests.cs:14`、`LoginSessionCommitRaceTests.cs:12`、`AuthenticationConcurrencyTests.cs:10`。
- 用 TCS/门卡住制造交错（改写；串行下可能死锁）：`SelectCharacterLifecycleTests.cs:236,261,288,365`、`PartyTownProjectionDeliveryTests.cs:134,188`、`TownProjectionHandlerTests.cs:164`、`DungeonLoadingProjectionDeliveryTests.cs:78,107`、`BossTowerProjectionConcurrencyTests`(11)、`IllusionTowerProjectionConcurrencyTests`(11)、`DungeonCardPresentationCoordinatorTests.cs:109,235`。
- 「并发只生效一次」约 20 条（如 `EpicCraftTests.cs:134`、`NpcShopPurchaseTests.cs:198`、`WarehouseStorageTests.cs:196`、`MarriageProposalTests.cs:142`、`MailboxCoreIntegrationTests.cs:334`）：串行下仍通过，可保留为不变量，但不再证明互斥。
- 投递期限/重试/补偿：`NotificationDeliveryTests.cs:145,188,216,331,353,379`、`NotificationRetryTests`(9)、`NotificationRetryDrainServiceTests`(4)、`CleanupNotificationDeliveryTests.cs:36,51`、`TouchSocketNetworkMessageSenderExplicitDeliveryTests.cs:10,38,65`、`HandlerFailureConnectionIntegrationTests.cs:60`、`PreparedBatchTests.cs:103`、`ExactConnectionCloseTests.cs:32`。

## 基线失败与偶发

- `DimensionDungeonTests.cs:19`、`:58`（SkippableFact；7100 迷宫 0 的 (1,0) 缺左入口地图），与 `session-currency-checks/verification/baseline.md` 一致。
- SQLite 释放偶发：栈根 `CharacterDatabaseSchema.cs:142`，见 AGENTS.md 清单。
- 满载假红：`IllusionTowerStageFrameIntegrationTests`、`SkillLearningIntegrationTests`、`ComponentLifecycleTests`（Connection refused，隔离复跑通过）；另见 `SelectCharacterLifecycleTests.ObserverQueuedBeforeRollbackClose…` 撞 2 秒超时（`Document/tasks/item-delivery/verification/completion-evidence.md:41`）。AGENTS.md「Networking.Tests 46 条」已过时，现为 97。

## 命令与耗时

- CI（`.github/workflows/validate.yml:60-64`）：`dotnet restore` → `dotnet build -c Release --no-restore -p:ContinuousIntegrationBuild=true` → `dotnet test -c Release --no-build --no-restore -m:1 --logger trx --blame-hang-timeout 3m`。预算 Linux 30 分钟、Windows 90 分钟；CI 无 PVF。
- 本机完整验证：设 `PVF_ARCHIVE_PATH` 后 `dotnet test Server/86JP.Server.slnx -c Release`，按 AGENTS.md 加 detailed logger 并落盘。
- 耗时：构建约 5 秒；测试墙钟约 4 分钟（Host.Tests 3 分 28 秒、Application.Tests 2 分 45 秒、Infrastructure.Tests 1 分 5 秒）。
