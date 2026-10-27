# 调研 03：投递与发送路径

基准 `a02bce46`，只读盘点。路径相对 `Server/src/`。

## 汇总

| 发送路径 | 调用点（约） | 是否依赖结果 | 代表位置 |
|---|---|---|---|
| `IGameMessageSender.SendExplicitAsync` | 105 | 多数靠异常决定后续（补偿、回滚、关连接） | `SelectCharacterHandler.cs:156/308-473`；`DungeonEntryProjectionDelivery.cs:361` |
| `SendCmdAsync` / `SendNotiAsync` | 83 / 24 | 一般不看结果，但异常一路传到 handler | `CreatureRuntimeDelivery.cs:68`（失败关连接，返回 ownerDelivered） |
| `SendBatchAsync`（固定 transport） | 9 | 校验 transport 未被替换 | `ReplayStateProjectionDelivery.cs:47`；Rental/Joust/Teleport/CollectionBox |
| `INetworkMessageSender.SendAsync` 直接调用 | Channel 4 + NotificationBus 1 | 否 | `Channel/*Handler.cs` |
| `IEventService.ProjectAsync` | 92 | 约 15 处读取 `NotificationDelivery` | `PartyTownProjectionDelivery.cs:251-290` |
| 关连接 `RequestClose(Exact)Async` | 12 | 对发送失败的反应 | 见 §4 |

直接按连接发送、不经 `NotificationAudience` 合计约 221 处（Explicit 105 + Cmd 83 + Noti 24 + Batch 9），另 Channel 4 处，分布在 113 个持有 `IGameMessageSender` 的文件。BestEffort 多目标扇出：MonsterDeath、两塔 `SendToExactAsync`、LeaveDungeon 同伴、CreatureRuntimeDelivery。

## 1. 发送语义

- `GameMessageSender`（`GameMessageSemantics.cs:91-124`）只转发；CMD/NOTI 首字节由编码器基类固定。
- `TouchSocketNetworkMessageSender`：`SendAsync` 每次按 id 查连接（查不到抛），逐帧 `await ISender.SendAsync`（:80-101）；`SendBatchAsync` 先整批编码、校验长度并 `ReferenceEquals` 确认 transport，再逐帧发（:15-57）；`DeliverExplicitAsync` 的 Required 按序、失败即抛，BestEffort 逐目标、某目标失败跳过其余消息、其他目标继续（:124-149）。
- 应用层无发送队列、无每连接锁。TouchSocket 4.3.1 推断为写锁内写入 PipeWriter + Flush（有背压），await 结束只代表交给管道（`NotificationOutcome` 注释 "Handed over, not received"）。**推断自 dll 字符串，未反编译确认。**
- 同一连接入站回调串行 await（`Networking/Runtime/NetworkPlugins.cs:135-172`）；锁粒度只到单帧，后台服务与 handler 同时向同一连接发时，消息级顺序无保证（今天的隐患）。
- handler 抛异常 → 重新抛出 → 断开连接（`NetworkPlugins.cs:152-167`）：发给请求方失败等于隐式关连接。

## 2. 通知层

- 每收件人期限 2 秒、重试间隔 1 秒（`Notifications/NotificationBus.cs:27-28`）；失败者进 `NotificationRetryQueue`（:194-255）。重试上限：注册为 0 的 59 个，只有 `AccountWalletChanged`、`PvpParticipantPublished` 为 1（`ProtocolModule.cs:452`、`:616`）。`NotificationRetryDrainService` 周期排空（:50-78），重试时重新解析收件人、重新组装；精确会话已被替换则丢弃。
- 解析器直接跳过离线角色（摘要中不出现）；`Gone` 只表示发布中途离开（`NotificationBus.cs:204`）。
- `EventService.ProjectAsync` 在可写事务内调用会在运行期抛出（`EventService.cs:62-63`）。sender 无运行期检查，靠命令处理器注入面判据（`CommandHandlerArchitectureTests.cs:58-89`）+ UnitOfWork 在命令返回前提交。LiveOnly 先提交消费决定再在事务外投影（`DomainEventDispatcher.cs:127-150`）。
- composer 在发送前异步读领域状态（如 `TitleBookSlice.cs:217` 的 `views.TryLoadAsync`）。

### 读取 `NotificationDelivery` 并据此做业务决定

- `PartyExactNotificationDelivery.PublishWithRetryAsync`（`PartyTownProjectionDelivery.cs:251-290`）：只重试 Failed 精确会话，最多 2 次，返回 Delivered 与 HasFailures。`PartyNotificationSlice.cs` 13 处在 HasFailures 时不推进 `stream.LatestRevision/CurrentMembers/FullStateMembers`（:688-711）；`PartyTownProjectionDelivery.cs:90/113/133`；`TownAreaProjectionDelivery.cs:543/603` 只对 Delivered 补发队伍刷新。
- `AltarProjection.cs:98`：有 Failed 抛 `PostCommitPublicationFailure` → `safety.BlockAsync`。
- `DungeonRoomMechanismNotificationSubscriber.cs:18`：有 Failed 抛 IOException 等重试。
- 7 个每日配额刷新（Heras/Tournament/ReviveCoin/GoldSilverHorn/Dimension/Altar/Mirror，`UsageQuotaProjection.cs:114` 等）：只有 Delivered 才记「本期已送」，否则下一分钟再发。
- `GmCeraDomainService.cs:24`：Failed 或 Gone 返回 "unconfirmed"。

### 依赖「发送成功才继续 / 失败就补偿」

- SelectCharacter：发送前验证可编码（:281-285）；阶段间「发送 → EnsureCurrent → 下一命令」，任一发送抛出进 catch，派发补偿命令并在之后关连接（:188-234、:257-271）；`achievementProjectionGate` 压住实时称号投影直到进入批次送达（:142-144）。
- DungeonEntry：先跟随者后请求者（:356-376），失败对已送达者和当前目标 `IsolatePartialCohortAsync`（关连接）再 `RollbackEntry`（:378-381），发完才 `LeaveManyAsync`。
- 两塔：`SendToExactAsync` 收集失败，释放 gate 后对失败者 `CloseQuietlyAsync`（`IllusionTowerProjectionDelivery.cs:435-442、790-808`，Boss 对称 :495）；协调里投影抛出时 `CloseExactAsync`（`BossTowerPartyCoordination.cs:233-238`）。
- MonsterDeath：失败对全部参与者 `BlockAsync`（`MonsterDeathSlice.cs:484-490`）。
- DungeonClear：发送失败且持久效果已提交 → `PublicationFailedAsync`，否则 `ReleaseSettlement`（`DungeonClearSlice.cs:1108-1116`）。
- RenameCreature：`SendVisibleAsync` 返回 false 不发 ACK（`RenameCreatureHandler.cs:72`）。
- CleanupNotificationDelivery：owner 必达失败即抛，后续同伴/队伍通知不发（`SupplementalSynchronizationMessages.cs:250-272`）。
- ReturnToSelection：部分失败 `RequestCloseExactAsync`（`ReturnToSelectionHandler.cs:61-65`）。
- ConnectedAsync：初始 NOTI 失败即 TryDisconnect 并抛出（`OnlineUserConnectionObserver.cs:48-62`）。

## 3. 有序性要求

保证手段：`RequiredDirectMessages` 顺序、单连接顺序 await、`SendBatchAsync` 先整批编码、BestEffort 按目标隔离。

- 单连接：Rental :154 "ACK must precede 03C1"、ItemSockets :155、MoveItemSpace :927、OpeningDelivery :27、TitleBook :214、UseRevivalCoin :110（状态先于 ACK）、PartyNotificationSlice :829（花名册后重放实时状态）、SelectCharacter 进入序列与 refill 增量位置（:296-314）。
- 跨连接：`LeaveDungeonSlice.cs:283` "Peers must see the decision before the leader's ACK"；DungeonEntry 跟随者先于领队。
- 跨路径：party 按 generation 一个 stream + gate 并维护 revision 水位线，因为 "Commands release their gates before transport work"（`PartyNotificationSlice.cs:663-668`）。

## 4. 关连接

- `TouchSocketNetworkConnectionCloser`：`Task.Yield()` 后分离执行 `CloseAsync`，调用方立即返回（:30-44）；Exact 版本校验 Context 引用（:22-28）。
- `OnTcpClosed` → `DisconnectedAsync` 获取 lifecycle 与 online 两个 gate 并清理（`NetworkPlugins.cs:31-43`、Observer :89-119），所以调用方必须先释放 gate 再关。
- 调用点：SelectCharacterHandler :262/264、ReturnToSelection :64、SelectedSessionSafety :71、DungeonEntry :530、Illusion/BossTower Delivery :856/914、两个 PartyCoordination :403/422、CreatureRuntimeDelivery :80/178、GameLoginHandler :152、LoginSessionRuntime :48。

## 改成发件箱 + 异步发送后会变的行为

1. 补偿与关连接的触发点消失（SelectCharacter、DungeonEntry 隔离+回滚、两塔与协调 CloseExact、MonsterDeath/Altar/DungeonClear 的 BlockAsync/PublicationFailed、ReturnToSelection、Creature）。今天靠同步异常触发，需把「发送失败策略」（关连接/隔离）挂到异步发送端。
2. 读取 `NotificationDelivery` 的逻辑失效：队伍 revision 水位线、Town 只对 Delivered 补发、配额刷新「已送」标记、GmCera unconfirmed、RoomMechanism 失败重试、Rename 依赖 ownerDelivered。Delivered 将只能表示「已登记」。
3. 每收件人 2 秒期限与 `NotificationRetryQueue` 重试建立在被 await 的发送上，要改为发送端超时与重试；重试时重新组装（读当前状态）的语义要重新放置。
4. SelectCharacter 在每次发送之间 EnsureCurrent、发送在 transition lease 内；改发件箱后消息落线可能晚于后续命令；`achievementProjectionGate` 的「批次已到客户端」语义不再成立。
5. 跨连接顺序（DungeonEntry 跟随者先于领队、LeaveDungeon 同伴先于领队 ACK）需发送层重新保证；每连接一条队列只保证单连接内顺序。
6. handler 抛异常导致断线的隐式路径消失：发给请求方失败不再断线，需显式关连接。
7. 发件箱条目需带 `NetworkConnectionContext` 引用，刷出时再校验 transport 未被替换。
8. CleanupNotificationDelivery「必达失败不再继续」与 Explicit「Required 失败即中止、BestEffort 按目标隔离」需在异步队列中保留。
9. 同一连接上 handler 与后台服务并发发送导致的帧级交错，在单执行器 + 每连接一条队列后消失。
