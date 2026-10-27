# 事件链统计（RQ-052）

基准：HEAD `b76504bb`，产品代码与 `a02bce46` 相同。路径相对 `Server/src/`。只读检索，没有运行构建或测试。

## 方法

- 订阅注册：`grep -rn -E "AddDomainSubscriber<|AddRuntimeEventSubscriber<"`，排除定义文件 `DomainEventDispatchingExtensions.cs`，共 52 行。Reliable 与 LiveOnly 用 `AddDomainSubscriber`，Direct 用 `AddRuntimeEventSubscriber`。
- 发布点：`grep -rn -E "\.(EnqueueAsync|DirectAsync)\("`。除接口和 `EventService` 自身外，没有别的调用形式。Persistence、Infrastructure、Networking 都不注入 `IEventService`。
- 隐式边：
  - `SqliteItemDomainStoreSession` 的 Take、Put、Update 会调用 `ContentsChangedAsync`（`Persistence/Inventory/SqliteItemDomainStore.cs:46`；触发点 `:119/:126/:153/:169/:196`），再经 `Application/Inventory/InventoryContentsEventDomainService.cs:19` 发出 `EnqueueAsync(InventoryContentsChanged)`。所以凡是在事务内写 SQLite 物品的订阅，都有一条通往 `InventoryContentsChanged` 的边。
  - 没有可写事务时，`EnqueueAsync` 会直接抛出（`EventService.cs:24-26`）。LiveOnly 的订阅在消费标记提交之后、事务之外调用（`DomainEventDispatcher.cs:127-160`），所以在结构上不能再入队事件。22 个 LiveOnly 处理器都只调用 `ProjectAsync`。
- 计数口径：
  - 源头那条 CMD 算第 1 个；
  - 每次 Reliable 或 LiveOnly 的消费都是一条新 CMD，计数加 1；
  - Direct 订阅在调用方的 CMD 内执行，不加 1，但它里面的 Enqueue 继续沿边计算。

## 事件 → 订阅 → 事件

### Reliable（17）

| # | 事件 | 订阅 ID | 注册 | 处理器 | 发布 |
|---|---|---|---|---|---|
| R1 | AltarChallengeCompleted | quest.altar-complete | Application/ApplicationModule.cs:133 | World/AltarDungeonQuestSubscriber.cs:10 | QuestProgressChanged（:18）；镜像副本内另发 MirrorQuestChanged（QuestProgressDomainService.cs:96 → MirrorAradQuestDomainService.cs:127 → :165） |
| R2 | DungeonParticipantLeft | altar.departure | ApplicationModule.cs:134 | World/AltarDungeonDomainServices.cs:84 | 无 |
| R3 | DungeonMapCleared | marriage.capsule-clear | ApplicationModule.cs:169 | Marriage/CapsuleClearSubscriber.cs:14 | MarriageCapsuleEnergyChanged（:26） |
| R4 | DungeonMapCleared | quest.wedding-trial | ApplicationModule.cs:181 | Marriage/MarriageTrialClearSubscriber.cs:9 → MarriageQuestDomainService.cs:77 | QuestProgressChanged（:95） |
| R5 | DungeonParticipantLeft | tournament.departure | ApplicationModule.cs:314 | World/TournamentDungeonDomainService.cs:229 | 无 |
| R6 | DungeonParticipantLeft | quest.mirror-leave | Quests/MirrorAradQuestRegistration.cs:18 | Quests/MirrorAradQuestDomainService.cs:146 → :142 | MirrorQuestChanged（:144 → :165） |
| R7 | DungeonMapCleared | quest.map-clear | Quests/QuestMapClearSubscriber.cs:59 | :24 | QuestProgressChanged（:40） |
| R8 | InventoryContentsChanged | quest.inventory | QuestMapClearSubscriber.cs:60 | Quests/SeekingQuestProgressDomainService.cs:32 | QuestProgressChanged（:66）；只处理 Main 与 SharedMaterial（:35/:41） |
| R9 | DungeonMapCleared | quest.mirror-map-clear | QuestMapClearSubscriber.cs:61 | MirrorAradQuestDomainService.cs:148 | MirrorQuestChanged（:161 → :165） |
| R10 | MonsterDefeated | achievement.monster | Achievements/AchievementRegistration.cs:33 | Achievements/AchievementDomainService.cs:204 → :156 | AchievementProgressChanged（:28/:122）、AchievementCompleted（:123） |
| R11 | DungeonCompleted | achievement.dungeon | AchievementRegistration.cs:34 | AchievementDomainService.cs:206 | 同 R10 |
| R12 | QuestCompleted | achievement.quest | AchievementRegistration.cs:35 | :208 | 同 R10 |
| R13 | InventoryContentsChanged | achievement.inventory | AchievementRegistration.cs:36 | :171 → :183 | 同 R10；只认 Main、SharedMaterial、Avatar、Pet、Equipment（:185-186） |
| R14 | ItemUsed | achievement.item | AchievementRegistration.cs:37 | :209 | 同 R10 |
| R15 | MaterialConsumed | achievement.material | AchievementRegistration.cs:38 | :210 | 同 R10 |
| R16 | CharacterLevelChanged | achievement.level | AchievementRegistration.cs:39 | :211 | 同 R10 |
| R17 | AchievementCompleted | title.unlock | AchievementRegistration.cs:40 | Achievements/TitleBookDomainService.cs:53 → :85 | TitleUnlocked（:125）；`session.PutAsync(Book(...))`（:123）经 SqliteItemDomainStore.cs:169 发 InventoryContentsChanged，容器为称号簿（:19-20） |

### LiveOnly（22，都只调用 `ProjectAsync`，不发布事件）

| # | 事件 | 订阅 ID | 注册 | 处理器 |
|---|---|---|---|---|
| L1 | CharacterNameTagExpired | character.name-tag.expiration.notifications | Protocol/ProtocolModule.cs:241 | Protocol/Game/CeraShopPurchase/NameTagExpiredSubscriber.cs:9 |
| L2 | OnlineExpiredItemsRemoved | inventory.expiration.notifications | ProtocolModule.cs:243 | Game/StateSynchronization/ExpiredItemsProjection.cs:19 |
| L3 | ClientAppearanceSettingsChanged | settings.appearance-visibility | ProtocolModule.cs:245 | Game/StateSynchronization/ClientAppearanceSettingsProjection.cs:16 |
| L4 | LuckyStarChanged | rental.lucky-star-refresh | ProtocolModule.cs:288 | Game/Rental/RentalSlice.cs:160 |
| L5 | MarriageCapsuleEnergyChanged | marriage.capsule-notification | ProtocolModule.cs:344 | Game/MarriageHousing/CapsuleSlice.cs:119 |
| L6 | MarriageCapsuleGiftChanged | marriage.capsule-gift-notification | ProtocolModule.cs:345 | CapsuleSlice.cs:124 |
| L7 | AchievementProgressChanged | achievement.noti | ProtocolModule.cs:661 | Game/Achievements/AchievementSlice.cs:84 |
| L8 | TitleUnlocked | title.noti-unlock | ProtocolModule.cs:662 | Game/TitleBook/TitleBookSlice.cs:183 |
| L9 | TitleStored | title.noti-store | ProtocolModule.cs:663 | 同上 |
| L10 | TitleWithdrawn | title.noti-withdraw | ProtocolModule.cs:664 | 同上 |
| L11 | ItemMirrorRemoved | inventory.noti-mirror-removed | ProtocolModule.cs:665 | 同上 |
| L12 | TitleAppearanceChanged | title.noti-appearance | ProtocolModule.cs:672 | 同上 |
| L13 | SaoTowerCleared | sao-tower.notifications | ProtocolModule.cs:1131 | Game/SaoTowerProjection.cs:33 |
| L14 | SouthernDaleInventoryChanged | southern-dale.inventory-notifications | ProtocolModule.cs:1132 | Game/SouthernDale/SouthernDaleProjection.cs:16 |
| L15 | SouthernDaleDailyRefillCompleted | southern-dale.daily-refill-notifications | ProtocolModule.cs:1134 | SouthernDaleProjection.cs:22 |
| L16 | BossTowerTicketRefilled | boss-tower.ticket-notifications | ProtocolModule.cs:1138 | Game/BossTowerTicket/BossTowerTicketProjection.cs:15 |
| L17 | TauKingdomTicketRefilled | tau-kingdom.ticket-notifications | ProtocolModule.cs:1142 | Game/TauKingdom/TauKingdomProjection.cs:15 |
| L18 | UsageQuotaChanged | usage-quota.notifications | Application/ApplicationModule.cs:119 | Application/UsageQuotas/UsageQuotaSnapshotDomainService.cs:50 |
| L19 | MailCreated | mail.arrival | ApplicationModule.cs:220 | Application/Mail/MailCreatedNotificationSubscriber.cs:7 |
| L20 | ManagedEventConfigurationChanged | activity.refresh | ApplicationModule.cs:224 | Application/Events/ActivityNotificationSubscribers.cs:25 |
| L21 | MirrorQuestChanged | quest.mirror-notification | Quests/MirrorAradQuestRegistration.cs:19 | MirrorAradQuestDomainService.cs:194 |
| L22 | QuestProgressChanged | quest.progress-notification | Quests/QuestMapClearSubscriber.cs:62 | QuestMapClearSubscriber.cs:45 |

### Direct（13，在调用方的事务和 CMD 内执行）

| # | 事件 | 注册（ApplicationModule.cs） | 处理器 | 发布 |
|---|---|---|---|---|
| D1～D11 | ConsumableUsageRecorded、PremiumUsageRecorded、MirrorEntryRecorded、MirrorRewardRecorded、DimensionEntryRecorded、TournamentEntryRecorded、AltarEntryRecorded、HerasQuotaRecorded、GoldSilverHornEntryRecorded、DespairQuotaRecorded、ReviveCoinClaimRecorded | :108、:110、:112、:114、:123、:135、:137、:185、:195、:212、:715 | UsageQuotas/UsageQuotaDomainService.cs（:35、:42、:48、:51、:59、:75、:69、:81、:87、:63、:20） | UsageQuotaChanged（ApplyAsync :93 → :130） |
| D12 | JoustLifecycleChanged | :228 | Application/Events/ActivityNotificationSubscribers.cs:8 | 无，只调用 ProjectAsync |
| D13 | DungeonRoomMechanismsChanged | :327 | Application/World/DungeonRoomMechanismNotificationSubscriber.cs:8 | 无，只调用 ProjectAsync |

### 对账加法

- 按类别：Reliable 17 ＋ LiveOnly 22 ＋ Direct 13 ＝ 52，共 3 项，与 `research/01` 的 52 一致。
- 按注册文件：ProtocolModule 17 ＋ ApplicationModule 21 ＋ MirrorAradQuestRegistration 2 ＋ QuestMapClearSubscriber 4 ＋ AchievementRegistration 8 ＝ 52，共 5 项。

## 源头 → 首个事件

| 事件 | 发布点 | 源头种类 |
|---|---|---|
| DungeonMapCleared | World/MonsterDeathDomainService.cs:92；AltarDungeonEngine.cs:278；BossTower/BossTowerDomainService.cs:300；IllusionTower/IllusionTowerDomainService.cs:261 | 入站；定时（推断） |
| DungeonParticipantLeft | World/DungeonLifecycleDomainService.cs:15/:21，由 DungeonRunDomainService.cs:1329/:1340/:1352 调用 | 入站（LeaveDungeonCommand.cs:28）；关连接 → 生命周期（Sessions/SelectCharacterLifecycleServices.cs:127）；定时（DungeonPartyWipeService.cs:21/:30） |
| AltarChallengeCompleted | AltarDungeonEngine.cs:243 | 入站、定时 |
| MonsterDefeated、DungeonCompleted | AchievementDomainService.cs:216/:217，由 MonsterKillRewardsDomainService.cs:44/:46、AltarDungeonEngine.cs:247、TournamentDungeonDomainService.cs:153 调用 | 入站 |
| QuestCompleted | Settlement/QuestCompletionSettlementService.cs:45 | 入站 |
| InventoryContentsChanged | InventoryContentsEventDomainService.cs:19（由 SQLite 物品写入触发） | 入站、定时、GM（推断）、R17 |
| ItemUsed | ItemUseEffectDomainService.cs:31/:57、ReviveCoinDomainService.cs:45、AvatarCompoundDomainService.cs:85、ItemSocketDomainService.cs:83 | 入站 |
| MaterialConsumed | MaterialConsumptionDomainService.cs:40 | 入站 |
| CharacterLevelChanged | Characters/PlayingCharacter.Experience.cs:82 | 入站 |
| AchievementProgressChanged、AchievementCompleted | AchievementDomainService.cs:28/:122/:123 | R10～R16；入站 |
| TitleUnlocked | TitleBookDomainService.cs:125 | R17；入站（推断） |
| TitleStored、TitleWithdrawn、TitleAppearanceChanged | TitleBookDomainService.cs:153/:166、:204/:226/:262、:40 | 入站 |
| ItemMirrorRemoved | ItemMirrorDomainService.cs:24 | 入站 |
| QuestProgressChanged | R1/R4/R7/R8；World/CycleDungeonEntryDomainService.cs:143 | 入站、订阅 |
| MirrorQuestChanged | MirrorAradQuestDomainService.cs:165 | 入站、订阅 |
| MarriageCapsuleEnergyChanged | CapsuleClearSubscriber.cs:26 | 只来自 R3 |
| MarriageCapsuleGiftChanged | Marriage/GrowthCapsules.cs:50 | 入站（推断） |
| BossTowerTicketRefilled、TauKingdomTicketRefilled | BossTowerTicketDomainService.cs:23、TauKingdomDomainService.cs:60 | 定时 |
| SouthernDaleInventoryChanged、SouthernDaleDailyRefillCompleted | SouthernDaleDomainService.cs:88/:61 | 定时、入站 |
| SaoTowerCleared | SaoTowerDomainService.cs:78 | 入站 |
| MailCreated | Mail/MailboxDomainService.cs:90 | 入站、GM、定时（推断） |
| ManagedEventConfigurationChanged | Events/GmEventsDomainService.cs:28 | GM |
| ClientAppearanceSettingsChanged | Accounts/ClientSettingsDomainService.cs:37 | 入站 |
| OnlineExpiredItemsRemoved | Inventory/OnlineExpiredItemCleanup.cs:37 | 定时 |
| CharacterNameTagExpired | Characters/CharacterNameTagDomainService.cs:65 | 定时 |
| LuckyStarChanged | Rental/AccountLuckyStarDomainService.cs:32 | 入站 |
| UsageQuotaChanged | UsageQuotaDomainService.cs:130 | 只来自 D1～D11，处在源头 CMD 内 |
| JoustLifecycleChanged | Events/JoustMaintenanceService.cs:58 | 定时 |
| DungeonRoomMechanismsChanged | Protocol/Game/RoomMechanisms/RoomMechanismProjection.cs:59、:90 | 入站、定时 |

- 启动器 HTTP 没有找到发布点。
- 启动恢复中的遗留 delivery 是新根，不会让链变长；Joust 的启动恢复是静默的。
- 标注「推断」的，没有逐层追到入口。这只影响源头种类的标签，不影响链长，因为每个源头都算作第 1 跳。

## 环

**类型图上有一个环**：AchievementCompleted → title.unlock（R17）→ InventoryContentsChanged（称号簿 Put 的隐式边）→ achievement.inventory（R13）→ AchievementCompleted。

**数据层面这个环不会转起来**：title.unlock 只写称号簿容器，而 R13 只认 Main、SharedMaterial、Avatar、Pet、Equipment，R8 只认 Main、SharedMaterial，遇到称号簿都直接返回。没有测试守着这处切断；一旦某个 Reliable 订阅开始写这五类容器，环就会真的成立，深度上限会把它截断（这正是 RQ-053 的用途）。

## 最长合法链

**裁定**：类型图有环，RQ-052 的字面方法在有环时没有定义。取下面两者中较保守的口径：
1. 类型图上的最长**简单路径**，不使用数据层面的过滤切断；
2. 再接上「CMD 登记的关连接 → 生命周期」的尾巴。

关于关连接这条边，口径已写进 RQ-050：
- 只有 CMD 登记进发件箱的关连接请求，才继承登记它的那条 CMD 的链；
- 传输层自发的关闭（交付失败、交付超时、队列超限，RQ-036；客户端断开）引起的生命周期 CMD 是新根，来源种类为「生命周期」。

否则，「给同伴的交付失败 → 关闭同伴 → 同伴的生命周期 CMD 再发通告」这种级联会让链越过深度上限，进而误关根连接。

| 跳 | CMD | 来源种类 | 代码位置 | 计数 |
|---|---|---|---|---:|
| 1 | 入站怪物死亡 → Enqueue MonsterDefeated | 入站 | MonsterKillRewardsDomainService.cs:44 → AchievementDomainService.cs:216 | 1 |
| 2 | achievement.monster（R10）→ AchievementCompleted | 事件 | AchievementRegistration.cs:33；AchievementDomainService.cs:204 → :123 | 2 |
| 3 | title.unlock（R17）→ InventoryContentsChanged（称号簿） | 事件 | AchievementRegistration.cs:40；TitleBookDomainService.cs:123 → SqliteItemDomainStore.cs:169 → InventoryContentsEventDomainService.cs:19 | 3 |
| 4 | achievement.inventory（R13），不考虑过滤 → AchievementProgressChanged | 事件 | AchievementDomainService.cs:171 | 4 |
| 5 | achievement.noti（L7） | 事件 | ProtocolModule.cs:661 | 5 |
| 6 | 第 1～5 跳中某条 CMD 登记关连接 → 生命周期 CMD：两塔断线离开，剩余成员全灭判为 Defeated → 结算：升级发出 CharacterLevelChanged，放入物品发出 InventoryContentsChanged | 生命周期（继承） | Sessions/SelectCharacterLifecycleServices.cs:149-151、:163；BossTowerRun.cs:146、IllusionTowerDgn.cs:137；BossTowerDomainService.cs:349-351（拆除时自开事务 :56-60）；BossTowerRewardDomainService.cs:127；PlayingCharacter.Experience.cs:82 | 6 |
| 7 | achievement.level（R16）→ AchievementCompleted | 事件 | AchievementRegistration.cs:39 | 7 |
| 8 | title.unlock（R17）→ InventoryContentsChanged | 事件 | AchievementRegistration.cs:40 | 8 |
| 9 | achievement.inventory（R13），不考虑过滤 | 事件 | AchievementRegistration.cs:36 | 9 |
| 10 | achievement.noti（L7） | 事件 | ProtocolModule.cs:661 | 10 |

加法：
- 主链 1 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＝ 5，共 5 跳；
- 尾巴 1 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＝ 5，共 5 跳；
- **L ＝ 5 ＋ 5 ＝ 10**。

尾巴有 5 跳，是因为生命周期 CMD 能发出的事件种类和入站 CMD 一样，以它为起点的最长链也是 5。早先只算了 3 跳（生命周期 → quest.mirror-leave → 通知），漏掉了两塔断线结算这条路径；这是 TASK-02 结果审查的 B-1。尾巴接在最深那一跳之后是一个上界：目前没有找到哪个 Reliable 或 LiveOnly 的 CMD 会登记关连接。只看数据层面，实际可走的最长链是 4。

**深度常量下限：max(2 × 10, 8) ＝ 20。常量取 32**，判据钉住「常量 ≥ 2 × 10」。取 32 是为了给日后新增订阅留余量；真正的无限链仍会在 32 层内被截断。

## 不确定

- LiveOnly 通知处理器的 `ComposeAsync` 没有逐个追完调用链，依据的是两个论据：结构上「事务外 Enqueue 会抛出」，以及「通知处理器只读」的判据（`NotificationHandlerArchitectureTests`）。
- `CreatureRuntimeDomainService.cs:252` 在拆除时写宠物物品，只会在生命周期 CMD 内产生 InventoryContentsChanged。它被上面那条长度为 5 的尾巴覆盖，不会让 L 变长。
