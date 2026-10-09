# 旧通知发布点迁移矩阵

基线：`ef00741dc3d4ee826c14b3d273042f38c3a1c31f`。全仓清单剔除通知传输机制与契约后共 **43 个文件**（Protocol 39、Application 3、GmWeb 1）。计数为旧直接 PublishAsync 调用；0 不代表可省略，Party/Cleanup 等封装仍须迁移构造器和转发参数。

| 基线文件（相对 Server/src） | 旧直接调用数 | 当前入口/职责 |
|---|---:|---|
| `Dfo.Server.Application/Administration/GmCeraDomainService.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Application/Events/JoustMaintenanceService.cs` | 1 | Publish JoustLifecycleChanged；运行时订阅转投影 |
| `Dfo.Server.Application/Mail/MailboxDomainService.cs` | 1 | Enqueue MailCreated；mail.arrival LiveOnly 消费 |
| `Dfo.Server.GmWeb/GmEventsEndpoints.cs` | 1 | HTTP 只返回结果；领域 Change 事务 Enqueue 配置事实 |
| `Dfo.Server.Protocol/Game/AccountRoster/OtherCharacterDetailSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/AvatarPackageOpening/AvatarPackageOpeningSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/BuyItem/BuyItemHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/CeraShopPurchase/CeraShopPurchaseHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/CompoundItem/CompoundItemSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/CrystalContract/CrystalContractSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/DeleteItem/DeleteItemHandler.cs` | 2 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/DisjointItem/DisjointItemHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/EnchantByBead/EnchantByBeadHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/EquipmentDurability/DecreaseDurabilityHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/Forging/ForgingHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/InventorySort/SortItemHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/InventorySort/SortLockHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/Joust/JoustSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/LearnSkill/LearnSkillHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/LevelUpTicket/LevelUpTicketSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/LotteryOpening/LotteryOpeningSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/MagicBoxBatchOpening/MagicBoxBatchOpeningSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/MagicBoxSingleOpening/MagicBoxSingleOpeningSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/MagicSeal/MagicSealHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/Mailbox/MailboxDelivery.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/Outworld/OutworldHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/PackageOpening/PackageOpeningSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/Party/PartyNotificationSlice.cs` | 0 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/Party/PartyTownProjectionDelivery.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/QuestClearCoupon/FatiguePotionHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/QuestClearCoupon/QuestClearCouponSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/RecoverStamina/RecoverStaminaSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/ReinforceItem/ReinforceItemHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/RepairItem/RepairItemHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/SellItem/SellItemHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/ShopPurchaseCount/ShopPurchaseCountSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/StateSynchronization/SupplementalSynchronizationMessages.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/TownProjection/TownAreaProjectionDelivery.cs` | 2 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/VendingMachine/VendingMachineSlice.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/Warehouse/WarehouseHandler.cs` | 1 | ProjectAsync；保持等待、audience、批次和返回值 |
| `Dfo.Server.Protocol/Game/WeeklyAttendance/WeeklyAttendanceSlice.cs` | 2 | ProjectAsync；保持等待、audience、批次和返回值 |

## 保留的行为

- 商城、消耗品、开包、升级券：保持 ACK 和库存绝对状态投影顺序，取消与发送结果继续 await。
- 邮箱操作：Send ACK、资产变动、Inbox 的既有顺序保持；每封新信独立记录，同一事务同角色的到信提醒合并。
- SelectCharacter/Town/Party/Cleanup：保持精确会话、revision/stream gate、失败成员重试以及 required-owner 后 peers 的次序。回包不持久化，不重放到新连接。
- ShopPurchaseCount/OtherCharacterDetail/Joust 查询：只读；即时投影或现有明确 sender，不产生业务事实。JoustQuery 的 double 拒绝 Publish/Enqueue/不应有的 Project。
- WeeklyAttendance/JoustBet/DungeonClear：既有即时结果与刷新仍同步投影；配置变化与马战阶段公告由独立订阅者承担，不把查询当阶段迁移。
- GM 钱包 reminder 保留同步投递结果用于 HTTP reminder 字段；改用 Project 统一入口，不能异步化后假称送达。

## 唯一低层适配

调用方都使用 IEventService；业务服务、协议和 HTTP 不再直接接触 INotificationBus。唯一适配类型是 `Application/DomainEvents/NotificationProjectionSubscriber<T>`（位于 EventService.cs），转到现有 NotificationBus。在线解析、精确会话、组包、发送结果及运输层重试仍由通知机制拥有。剩余源码引用是该适配、NotificationBus、NotificationDispatchingExtensions 组合注册、INotificationBus 契约。

新增 GmEventsDomainService 在事务记录 ManagedEventConfigurationChanged，订阅者决定刷新在线客户端。MailCreatedNotificationSubscriber、JoustLifecycleNotificationSubscriber、EventsConfigurationNotificationSubscriber 不依赖低层 bus。

## 原有明确协议边界

IGameMessageSender 的显式 CMD reply、必需投影/受众逐个发送由 Protocol 拥有：CreatureRuntimeDelivery、MonsterDeath、DungeonEntryProjectionDelivery、DungeonSelection、DungeonCardPresentationCoordinator 等不宣称全部迁入事件服务。事件系统不替代编码器和 Socket；它们的线级顺序和 required/best-effort 责任保持。

原 dungeon_effect_outbox 继续归地下城入场/经验投影恢复，不与事实表混用。此次新增两张表只承载领域事实及订阅状态。

## 可靠性边界

可靠 subscriber 更新与 delivery complete 同事务；不得重入 ICommandBus 或在数据库准入门内获取外层账户/会话锁。每组串行，退避失败事件允许后续到期事实先处理，不保证严格业务 FIFO。新增 subscriber 只消费注册后事实，不自动为历史事件回填 delivery。每稳定名称注册一个当前版本；未知版本保留诊断，未实现自动 upcast。完成记录暂保留，不自动清理。LiveOnly 发送前提交消费决定，崩溃窗口可能漏提醒，但不会补播。
