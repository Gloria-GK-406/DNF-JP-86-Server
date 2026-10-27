# TASK-02 附录 D 检索：逐命中处置清单

基准：工作区 HEAD `b76504bb`。范围：`Server/src/Dfo.Server.Protocol` 与 `Server/src/Dfo.Server.Application` 的 `*.cs`。只读产品代码，未运行构建或测试。

## 检索命令与命中数

关键词检索由一次性脚本 `scratchpad/hits.py` 执行（`python3 -I hits.py Server/src hits.json`），逐文件逐行扫描：

| 关键词 | 口径 | 命中行数 |
|---|---|---:|
| `first` | 注释内（`//`、`///`、`*` 之后）匹配 `\bfirst\b`，不区分大小写 | 145 |
| `then` | 注释内匹配 `\bthen\b`，不区分大小写 | 105 |
| `after the` | 整行匹配 `after the`，不区分大小写（与上一轮 grep 口径一致，含非注释行） | 72 |
| `before` | 注释内匹配 `\bbefore\b`，不区分大小写 | 204 |
| 合计（按关键词计，同一行可命中多个关键词） | | 526 |

去重后为 **498** 个不同的（文件, 行号）。下文第 2 节每个去重行号恰好出现一次，并注明命中的关键词。

ProjectAsync 调用点：`grep -rn "ProjectAsync(" --include='*.cs' Dfo.Server.Protocol Dfo.Server.Application | grep -v "public \|interface\|/// "`，命中 **95** 行。其中 IEventService.ProjectAsync 真实调用与同名的局部包装方法（定义或调用）都在内，表中逐行注明。

## 分类码

| 码 | 含义 |
|---|---|
| W | 单帧字节、载荷布局或解码规则（只涉及一个帧内部） |
| S | 同一连接上的帧先后（一个客户端收到的顺序，RQ-032 单连接 FIFO 承接） |
| T | 服务器内部执行次序（事务、锁、校验、认领、持久化、重试），不产生跨连接出站先后 |
| V | 跨 CMD 或跨版本不回退（同一观察者不收到旧状态，DR-16/DR-17/RQ-032 承接），不是跨连接先后 |
| R | 序数或规则含义（第一个槽、首个房间、首次完成、算式先后等），不是时间上的发送先后 |
| I | 客户端入站先后（客户端自己的发包顺序），不是服务器出站要求 |
| H | 历史、取证记录、版本沿革或命名说明 |
| N | 多收件人投递机制本身的描述（串行、期限、重试），是实现顺序而不是业务顺序要求 |
| X | 涉及多个连接的出站先后，已登记为 XO 项（见备注） |

## 1. ProjectAsync 调用点（逐行）

| # | 位置（相对 Server/src） | 受众形态 | 是否跨连接顺序要求 | 理由 |
|---:|---|---|---|---|
| 1 | `Dfo.Server.Application/Administration/GmCeraDomainService.cs:23` | 单会话 TheseSessions([x]) | 不是 | 单收件人，只涉及一个连接 |
| 2 | `Dfo.Server.Application/Events/ActivityNotificationSubscribers.cs:20` | 全服在线 online.Snapshot() | 不是 | 广播，没有收件人间先后 |
| 3 | `Dfo.Server.Application/Events/ActivityNotificationSubscribers.cs:29` | 全服在线 online.Snapshot() | 不是 | 广播，没有收件人间先后 |
| 4 | `Dfo.Server.Application/Mail/MailCreatedNotificationSubscriber.cs:12` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 5 | `Dfo.Server.Application/Quests/MirrorAradQuestDomainService.cs:197` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 6 | `Dfo.Server.Application/Quests/QuestMapClearSubscriber.cs:48` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 7 | `Dfo.Server.Application/UsageQuotas/UsageQuotaSnapshotDomainService.cs:54` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 8 | `Dfo.Server.Application/World/DungeonRoomMechanismNotificationSubscriber.cs:16` | 副本局在线收件人 TheseSessions(audience) | 不是 | 一次投影，失败抛异常重试（DR-8）；没有成员间先后 |
| 9 | `Dfo.Server.Protocol/Game/AccountRoster/OtherCharacterDetailSlice.cs:156` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 10 | `Dfo.Server.Protocol/Game/Achievements/AchievementSlice.cs:89` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 11 | `Dfo.Server.Protocol/Game/Altar/AltarHandlers.cs:62` | 非 IEventService：AltarProjectionDelivery.ProjectAsync（AltarProjection.cs:88） | 不是 | 见 AltarProjection.cs:96 |
| 12 | `Dfo.Server.Protocol/Game/Altar/AltarHandlers.cs:74` | 同上 | 不是 | 同上 |
| 13 | `Dfo.Server.Protocol/Game/Altar/AltarHandlers.cs:81` | 同上 | 不是 | 同上 |
| 14 | `Dfo.Server.Protocol/Game/Altar/AltarProjection.cs:121` | 非 IEventService：调用包装方法 | 不是 | 同 :96 |
| 15 | `Dfo.Server.Protocol/Game/Altar/AltarProjection.cs:125` | 同上 | 不是 | 同 :96 |
| 16 | `Dfo.Server.Protocol/Game/Altar/AltarProjection.cs:88` | 非调用：internal ProjectAsync 包装方法定义 | 不是 | 定义本身 |
| 17 | `Dfo.Server.Protocol/Game/Altar/AltarProjection.cs:96` | 祭坛局成员 TheseSessions(recipients) | 不是 | 一次投影；失败即封锁全体（DR-4），没有成员间先后 |
| 18 | `Dfo.Server.Protocol/Game/Altar/AltarScheduler.cs:68` | 同上 | 不是 | 同上 |
| 19 | `Dfo.Server.Protocol/Game/AvatarCompound/AvatarCompoundSlice.cs:126` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 20 | `Dfo.Server.Protocol/Game/AvatarPackageOpening/AvatarPackageOpeningSlice.cs:88` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 21 | `Dfo.Server.Protocol/Game/BossTowerTicket/BossTowerTicketProjection.cs:18` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 22 | `Dfo.Server.Protocol/Game/BuyItem/BuyItemHandler.cs:49` | 购买者，或婚房受众 HousingAudience | 不是 | 一次投影，没有先后语义 |
| 23 | `Dfo.Server.Protocol/Game/CeraShopPurchase/CeraShopPurchaseHandler.cs:50` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 24 | `Dfo.Server.Protocol/Game/CeraShopPurchase/CeraShopPurchaseHandler.cs:53` | 可见集合 visibility.Resolve | 不是 | 自己的购买结果先发，再向可见者刷新名牌外观；没有注释要求 |
| 25 | `Dfo.Server.Protocol/Game/CeraShopPurchase/NameTagExpiredSubscriber.cs:18` | 可见集合 visibility.Resolve(owner) | 不是 | 单次投影内按受众串行，没有先后语义 |
| 26 | `Dfo.Server.Protocol/Game/CompoundItem/CompoundItemSlice.cs:109` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 27 | `Dfo.Server.Protocol/Game/CrystalContract/CrystalContractSlice.cs:75` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 28 | `Dfo.Server.Protocol/Game/DeleteItem/DeleteItemHandler.cs:110` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 29 | `Dfo.Server.Protocol/Game/DeleteItem/DeleteItemHandler.cs:74` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 30 | `Dfo.Server.Protocol/Game/DisjointItem/DisjointItemHandler.cs:63` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 31 | `Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs:1024` | 单会话 TheseSessions([x]) | 不是 | 单收件人，只涉及一个连接 |
| 32 | `Dfo.Server.Protocol/Game/EnchantByBead/EnchantByBeadHandler.cs:73` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 33 | `Dfo.Server.Protocol/Game/EpicCraft/EpicCraftSlice.cs:83` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 34 | `Dfo.Server.Protocol/Game/EquipmentDurability/DecreaseDurabilityHandler.cs:32` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 35 | `Dfo.Server.Protocol/Game/Forging/ForgingHandler.cs:74` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 36 | `Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs:732` | 非 IEventService：DungeonPickupPostCommitCoordinator.ProjectAsync（:665）→ DungeonClearProjectionDelivery.DeliverAsync | 不是 | 请求者 Required、其他参与者 BestEffort 的普通扇出 |
| 37 | `Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs:750` | 同上 | 不是 | 同上 |
| 38 | `Dfo.Server.Protocol/Game/InventorySort/SortItemHandler.cs:29` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 39 | `Dfo.Server.Protocol/Game/InventorySort/SortLockHandler.cs:29` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 40 | `Dfo.Server.Protocol/Game/ItemSockets/ItemSocketsSlice.cs:135` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 41 | `Dfo.Server.Protocol/Game/Joust/JoustSlice.cs:191` | 全服在线 selected.Snapshot() | 不是 | 先全服广播赔率，再给下注者重放和 ACK；下注者自己的先后是同连接顺序，其他人与下注者 ACK 的先后没有注释或测试要求 |
| 42 | `Dfo.Server.Protocol/Game/LearnSkill/LearnSkillHandler.cs:52` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 43 | `Dfo.Server.Protocol/Game/LevelUpTicket/LevelUpTicketSlice.cs:154` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 44 | `Dfo.Server.Protocol/Game/LotteryOpening/LotteryOpeningSlice.cs:111` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 45 | `Dfo.Server.Protocol/Game/MagicBoxBatchOpening/MagicBoxBatchOpeningSlice.cs:82` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 46 | `Dfo.Server.Protocol/Game/MagicBoxSingleOpening/MagicBoxSingleOpeningSlice.cs:78` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 47 | `Dfo.Server.Protocol/Game/MagicSeal/MagicSealHandler.cs:67` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 48 | `Dfo.Server.Protocol/Game/Mailbox/MailboxDelivery.cs:92` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 49 | `Dfo.Server.Protocol/Game/MarriageHousing/CapsuleSlice.cs:122` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 50 | `Dfo.Server.Protocol/Game/MarriageHousing/CapsuleSlice.cs:127` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 51 | `Dfo.Server.Protocol/Game/MarriageHousing/HousingSlice.cs:106` | 结果受众 result.Audience（夫妻双方） | 不是 | ACK 与物品变化先发给操作者，再投影给双方；没有跨连接先后要求的注释或测试 |
| 52 | `Dfo.Server.Protocol/Game/MarriageProposal/MarriageWeddingSlice.cs:197` | 非调用：private ProjectAsync 包装方法定义（逐收件人吞异常） | 不是 | 定义本身 |
| 53 | `Dfo.Server.Protocol/Game/MarriageProposal/MarriageWeddingSlice.cs:222` | 非 IEventService：逐个 standing 调用 projection.PositionAsync | 是（XO-7，保守列入） | 注释 :220「before either endpoint starts the cinema」，字面可读作跨连接；按「行为不变」保守列入（主控方裁定，见 spec 附录 D）；无测试 |
| 54 | `Dfo.Server.Protocol/Game/MarriageProposal/MarriageWeddingSlice.cs:229` | 非 IEventService：逐参与者 SendExplicitAsync | 不是 | 位置之后给参与者开场；与 :222 的关系见 XO-7 |
| 55 | `Dfo.Server.Protocol/Game/MarriageProposal/MarriageWeddingSlice.cs:232` | 非 IEventService：逐宾客 SendNotiAsync | 不是 | 宾客开场与新人开场之间没有注释要求先后 |
| 56 | `Dfo.Server.Protocol/Game/MarriageProposal/MarriageWeddingSlice.cs:235` | 非 IEventService：逐离场者 MoveAsync | 不是 | 注释 :233 讲的是同一客户端内先移动（S） |
| 57 | `Dfo.Server.Protocol/Game/MarriageProposal/MarriageWeddingSlice.cs:237` | 非 IEventService：逐清理者外观＋命令 | 不是 | 同一角色自己的先后 |
| 58 | `Dfo.Server.Protocol/Game/Outworld/OutworldHandler.cs:79` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 59 | `Dfo.Server.Protocol/Game/PackageOpening/PackageOpeningSlice.cs:102` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 60 | `Dfo.Server.Protocol/Game/Party/PartyTownProjectionDelivery.cs:271` | 队伍精确会话 TheseSessions(pending)；所有队伍投影的唯一出口（PublishWithRetryAsync） | 是（XO-6，保守列入） | 主控方裁定保守列入（spec 附录 D）。本方法只是「失败者重试一次」；跨收件人的阶段屏障在调用方 PartyNotificationSlice.cs:798-842，由 PartyNotificationTests.Formation_is_published_in_cross_recipient_phases 等测试钉住 |
| 61 | `Dfo.Server.Protocol/Game/QuestClearCoupon/EntryAllowanceHandler.cs:71` | 单会话 TheseSessions([x]) | 不是 | 单收件人，只涉及一个连接 |
| 62 | `Dfo.Server.Protocol/Game/QuestClearCoupon/FatiguePotionHandler.cs:73` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 63 | `Dfo.Server.Protocol/Game/QuestClearCoupon/QuestClearCouponSlice.cs:227` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 64 | `Dfo.Server.Protocol/Game/RecoverStamina/RecoverStaminaSlice.cs:167` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 65 | `Dfo.Server.Protocol/Game/ReinforceItem/ReinforceItemHandler.cs:66` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 66 | `Dfo.Server.Protocol/Game/Rental/RentalSlice.cs:137` | 可见集合 visible.Resolve(actor) | 不是 | 操作者自己的批次先发（SendBatchAsync :135），再向可见者刷新外观；没有注释要求可见者晚于操作者 |
| 67 | `Dfo.Server.Protocol/Game/Rental/RentalSlice.cs:165` | 单会话 TheseSessions([x]) | 不是 | 单收件人，只涉及一个连接 |
| 68 | `Dfo.Server.Protocol/Game/RepairItem/RepairItemHandler.cs:49` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 69 | `Dfo.Server.Protocol/Game/ResetItemQuality/ResetItemQualitySlice.cs:115` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 70 | `Dfo.Server.Protocol/Game/ResetRandomOption/ResetRandomOptionSlice.cs:110` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 71 | `Dfo.Server.Protocol/Game/ReviveCoin/DailyReviveCoinSlice.cs:57` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 72 | `Dfo.Server.Protocol/Game/SaoTowerProjection.cs:36` | 奖励涉及的角色集合 TheseCharacters | 不是 | 每人各自一份刷新，彼此之间没有先后依赖，也没有注释或测试要求 |
| 73 | `Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs:382` | PvP 大厅受众 TheseCharacters(Audience) | 不是 | 新人自己的进入批次先发，再公示给大厅其他人；注释（:377-379）只说明人群由命令决定，没有先后要求 |
| 74 | `Dfo.Server.Protocol/Game/SellItem/SellItemHandler.cs:43` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 75 | `Dfo.Server.Protocol/Game/ShopPurchaseCount/ShopPurchaseCountSlice.cs:67` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 76 | `Dfo.Server.Protocol/Game/SouthernDale/SouthernDaleProjection.cs:19` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 77 | `Dfo.Server.Protocol/Game/SouthernDale/SouthernDaleProjection.cs:25` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 78 | `Dfo.Server.Protocol/Game/StateSynchronization/ClientAppearanceSettingsProjection.cs:22` | 可见集合 | 不是 | 同 :18 类 |
| 79 | `Dfo.Server.Protocol/Game/StateSynchronization/ExpiredItemsProjection.cs:32` | 所有者或可见集合 | 不是 | 一次投影，没有先后语义 |
| 80 | `Dfo.Server.Protocol/Game/StateSynchronization/SupplementalSynchronizationMessages.cs:264` | 城镇同伴 TheseSessions(PeerAudience) | 是（XO-5） | 所有者通告（:254）→ 本行同伴通告 → 队伍投影（:270）；由 CleanupNotificationDeliveryTests.The_owner_is_told_before_the_people_around_them、Existing_owner_and_town_notices_finish_before_party_projection 钉住；理由与 DR-10/RQ-034 必达门控重叠 |
| 81 | `Dfo.Server.Protocol/Game/StateSynchronization/UsageQuotaProjection.cs:89` | 单会话 TheseSessions([x]) | 不是 | 单收件人，只涉及一个连接 |
| 82 | `Dfo.Server.Protocol/Game/TauKingdom/TauKingdomProjection.cs:18` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 83 | `Dfo.Server.Protocol/Game/TitleBook/TitleBookSlice.cs:186` | 非调用：private ProjectAsync 包装方法定义 | 不是 | 定义本身 |
| 84 | `Dfo.Server.Protocol/Game/TitleBook/TitleBookSlice.cs:189` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 85 | `Dfo.Server.Protocol/Game/TitleBook/TitleBookSlice.cs:192` | 非 IEventService：调用包装方法（单角色） | 不是 | 单收件人 |
| 86 | `Dfo.Server.Protocol/Game/TitleBook/TitleBookSlice.cs:194` | 非 IEventService：调用包装方法（单角色） | 不是 | 单收件人 |
| 87 | `Dfo.Server.Protocol/Game/TitleBook/TitleBookSlice.cs:196` | 非 IEventService：调用包装方法（单角色） | 不是 | 单收件人 |
| 88 | `Dfo.Server.Protocol/Game/TitleBook/TitleBookSlice.cs:198` | 非 IEventService：调用包装方法（单角色） | 不是 | 单收件人 |
| 89 | `Dfo.Server.Protocol/Game/TitleBook/TitleBookSlice.cs:201` | 非 IEventService：调用包装方法（单角色） | 不是 | 单收件人 |
| 90 | `Dfo.Server.Protocol/Game/TownProjection/TownAreaProjectionDelivery.cs:321` | 同区观察者 TheseSessions(audience) | 不是 | 先给本人 UserPosition（SendNotiAsync :300），再给观察者；没有注释或测试要求 |
| 91 | `Dfo.Server.Protocol/Game/TownProjection/TownAreaProjectionDelivery.cs:672` | 单会话 TheseSessions([x]) | 不是 | 单收件人，只涉及一个连接 |
| 92 | `Dfo.Server.Protocol/Game/VendingMachine/VendingMachineSlice.cs:110` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 93 | `Dfo.Server.Protocol/Game/Warehouse/WarehouseHandler.cs:39` | 单角色 TheseCharacters([x]) | 不是 | 单收件人，只涉及一个连接 |
| 94 | `Dfo.Server.Protocol/Game/WeeklyAttendance/WeeklyAttendanceSlice.cs:109` | 单会话 TheseSessions([x]) | 不是 | 单收件人，只涉及一个连接 |
| 95 | `Dfo.Server.Protocol/Game/WeeklyAttendance/WeeklyAttendanceSlice.cs:110` | 单会话 TheseSessions([x]) | 不是 | 单收件人，只涉及一个连接 |

## 2. 关键词命中（按文件归组）

每行列出该文件中归入同一分类的全部行号；括号内是该行命中的关键词（f=first、t=then、a=after the、b=before）。

| 文件（相对 Server/src） | 分类 | 行号（关键词） | 备注 |
|---|---|---|---|
| `Dfo.Server.Application/Accounts/AccountEntryAssembly.cs` | T | 143(b) |  |
| `Dfo.Server.Application/Accounts/AccountEntryStateServices.cs` | H | 16(b) |  |
| `Dfo.Server.Application/Accounts/PremiumServiceUsageDomainService.cs` | H | 47(b) |  |
| `Dfo.Server.Application/Accounts/PremiumServiceUsageDomainService.cs` | T | 87(b) |  |
| `Dfo.Server.Application/Accounts/QuestHelperPuppetDomainService.cs` | T | 102(b) |  |
| `Dfo.Server.Application/ApplicationModule.cs` | H | 458(f), 460(f), 496(t) |  |
| `Dfo.Server.Application/Characters/CharacterCreationRuleService.cs` | R | 25(f), 54(f), 57(f), 58(a) |  |
| `Dfo.Server.Application/Characters/CharacterDomainService.cs` | T | 165(b) |  |
| `Dfo.Server.Application/Characters/CharacterDomainService.cs` | I | 198(b) |  |
| `Dfo.Server.Application/Characters/CharacterEntryAssembly.cs` | R | 21(f) |  |
| `Dfo.Server.Application/Characters/CharacterNameRules.cs` | T | 7(b), 12(t) |  |
| `Dfo.Server.Application/Characters/CharacterRosterService.cs` | W | 61(t) |  |
| `Dfo.Server.Application/Characters/CharacterRosterService.cs` | T | 115(b), 116(b) |  |
| `Dfo.Server.Application/Characters/CharacterRosterService.cs` | H | 127(a) |  |
| `Dfo.Server.Application/Characters/CharacterRosterService.cs` | R | 265(f), 267(f) |  |
| `Dfo.Server.Application/Characters/CharacterSkillLearningDomainService.cs` | T | 132(fa) |  |
| `Dfo.Server.Application/Characters/CharacterSkillStateDomainService.cs` | T | 50(fb) |  |
| `Dfo.Server.Application/Characters/CharacterStateAccess.cs` | T | 66(t), 69(t) |  |
| `Dfo.Server.Application/Characters/DarkKnightComboBook.cs` | T | 12(b), 13(b) |  |
| `Dfo.Server.Application/Characters/DungeonEffectRecoveryService.cs` | T | 12(b), 20(b), 34(b), 83(b), 187(t) |  |
| `Dfo.Server.Application/Characters/OtherCharacterDetailQueryDomainService.cs` | T | 38(f) |  |
| `Dfo.Server.Application/Characters/PlayingCharacter.Experience.cs` | T | 38(b), 68(b) |  |
| `Dfo.Server.Application/Characters/PlayingCharacter.cs` | T | 67(f), 188(b) |  |
| `Dfo.Server.Application/Characters/PlayingCharacter.cs` | R | 229(f) |  |
| `Dfo.Server.Application/Characters/SelectCharacterPersistenceServices.cs` | R | 11(f) |  |
| `Dfo.Server.Application/Characters/SelectCharacterPostEntryServices.cs` | T | 114(f) |  |
| `Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs` | T | 86(b) |  |
| `Dfo.Server.Application/Characters/SupportCreatureMarkerProjection.cs` | R | 57(t), 66(t) |  |
| `Dfo.Server.Application/Commands/Pipeline/TransactionalExecutionBehavior.cs` | T | 6(fb), 8(f) |  |
| `Dfo.Server.Application/Commands/SelectedSessionSafety.cs` | T | 21(b) |  |
| `Dfo.Server.Application/Creatures/CreatureRuntimeDomainService.cs` | T | 185(b) |  |
| `Dfo.Server.Application/Dispatching/MessageHandlerRegistry.cs` | T | 13(b), 19(t), 20(f) |  |
| `Dfo.Server.Application/Dispatching/NetworkMessageDispatcher.cs` | T | 23(b) |  |
| `Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs` | T | 100(b), 149(b), 170(b) |  |
| `Dfo.Server.Application/Effects/EffectOperationLedger.cs` | T | 27(f), 35(b), 73(b) |  |
| `Dfo.Server.Application/Events/JoustMaintenanceService.cs` | T | 16(fb), 32(b), 38(b), 62(b) |  |
| `Dfo.Server.Application/Inventory/CeraShopDomainService.cs` | T | 100(f) |  |
| `Dfo.Server.Application/Inventory/EquipmentReinforcementFailureRuleService.cs` | R | 59(b), 101(t), 121(b) |  |
| `Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` | T | 17(t), 215(fb) |  |
| `Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` | R | 43(f), 242(f) |  |
| `Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` | S | 295(t) |  |
| `Dfo.Server.Application/Inventory/ItemArrivalRuleService.cs` | R | 18(t), 91(b), 98(f), 99(t), 105(f) |  |
| `Dfo.Server.Application/Inventory/ItemContainers.cs` | R | 10(f) |  |
| `Dfo.Server.Application/Inventory/ItemContainers.cs` | T | 12(b) |  |
| `Dfo.Server.Application/Inventory/ItemDeliveryDomainService.cs` | T | 119(f) |  |
| `Dfo.Server.Application/Inventory/ItemPlacementRuleService.cs` | T | 18(b) |  |
| `Dfo.Server.Application/Inventory/ItemSaleRuleService.cs` | R | 31(t), 32(b) |  |
| `Dfo.Server.Application/Inventory/LegacyWornItemNormalizer.cs` | T | 10(b) |  |
| `Dfo.Server.Application/Inventory/LotteryOpeningDomainService.cs` | R | 30(f) |  |
| `Dfo.Server.Application/Inventory/MaterialConsumptionDomainService.cs` | T | 22(b) |  |
| `Dfo.Server.Application/Inventory/SelectCharacterEntryInventoryDomainService.cs` | H | 71(a), 73(a) |  |
| `Dfo.Server.Application/Inventory/VendingMachineDomainService.cs` | T | 34(b) |  |
| `Dfo.Server.Application/Marriage/ChurchRooms.cs` | W | 153(f) |  |
| `Dfo.Server.Application/Marriage/LovePoints.cs` | R | 31(b) |  |
| `Dfo.Server.Application/Marriage/MarriageDomainService.cs` | T | 12(b) |  |
| `Dfo.Server.Application/Marriage/WeddingSettlement.cs` | T | 81(t) | 81：退费与各宾客礼金是邮件写入的先后，在同一事务内，不是出站 |
| `Dfo.Server.Application/Notifications/NotificationBus.cs` | N | 10(b) | 10：受众串行发送、每收件人 2 秒期限；由 DR-14/RQ-036/RQ-037 承接 |
| `Dfo.Server.Application/Notifications/NotificationBus.cs` | T | 91(b), 185(a), 198(b), 262(b) |  |
| `Dfo.Server.Application/Notifications/NotificationComposer.cs` | N | 8(a) |  |
| `Dfo.Server.Application/Notifications/NotificationDispatchingExtensions.cs` | T | 23(b), 24(a), 25(fb), 26(b), 52(f) |  |
| `Dfo.Server.Application/OnlineUsers/SelectedSessionTransitionGate.cs` | T | 11(b) |  |
| `Dfo.Server.Application/Parties/PartyService.cs` | T | 10(b) |  |
| `Dfo.Server.Application/Quests/QuestAvailabilityRuleService.cs` | R | 48(fa) |  |
| `Dfo.Server.Application/Quests/QuestAvailabilityRuleService.cs` | T | 134(b) |  |
| `Dfo.Server.Application/Quests/QuestCompletionEffectRuleService.cs` | R | 29(a) |  |
| `Dfo.Server.Application/Quests/QuestLog.cs` | T | 15(b), 16(b), 99(b), 165(b), 167(t) |  |
| `Dfo.Server.Application/Quests/QuestObjectiveAuthority.cs` | R | 92(t) |  |
| `Dfo.Server.Application/Quests/QuestOfferRefreshDomainService.cs` | T | 12(a), 62(b) |  |
| `Dfo.Server.Application/Quests/QuestOfferRefreshDomainService.cs` | H | 19(f) |  |
| `Dfo.Server.Application/Scheduling/ScheduledTaskHostedService.cs` | T | 73(b) |  |
| `Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs` | T | 64(a), 195(a) |  |
| `Dfo.Server.Application/Transactions/DomainTransaction.cs` | T | 11(t), 12(t) |  |
| `Dfo.Server.Application/Transactions/DomainTransactionScope.cs` | T | 51(t) |  |
| `Dfo.Server.Application/UsageQuotas/UsageQuotaDomainService.cs` | T | 99(b) |  |
| `Dfo.Server.Application/World/BossTower/BossTowerDomainService.cs` | T | 95(b) |  |
| `Dfo.Server.Application/World/CardRewardDomainService.cs` | T | 38(b) |  |
| `Dfo.Server.Application/World/CardRewardDomainService.cs` | H | 115(a) |  |
| `Dfo.Server.Application/World/ClearRewardRuleService.cs` | T | 22(f), 27(t), 181(f) |  |
| `Dfo.Server.Application/World/ClearRewardRuleService.cs` | H | 229(b) |  |
| `Dfo.Server.Application/World/ClearRewardRuleService.cs` | R | 273(f) |  |
| `Dfo.Server.Application/World/CycleDungeonEntryDomainService.cs` | I | 30(t), 38(b), 40(a) |  |
| `Dfo.Server.Application/World/CycleDungeonEntryDomainService.cs` | T | 122(t) |  |
| `Dfo.Server.Application/World/DungeonCellFactory.cs` | T | 8(b) |  |
| `Dfo.Server.Application/World/DungeonRevivalDomainService.cs` | T | 32(b) |  |
| `Dfo.Server.Application/World/DungeonRoomRuleService.cs` | R | 10(f), 12(f), 26(f), 105(f), 116(f) |  |
| `Dfo.Server.Application/World/DungeonRunDomainService.cs` | T | 569(b), 1240(f) |  |
| `Dfo.Server.Application/World/DungeonStructureRuleService.cs` | T | 11(b), 181(b) |  |
| `Dfo.Server.Application/World/DungeonStructureRuleService.cs` | R | 182(f), 201(f), 202(f) |  |
| `Dfo.Server.Application/World/ExperienceTuningSource.cs` | H | 16(b) |  |
| `Dfo.Server.Application/World/IndependentDropRules.cs` | R | 13(b) |  |
| `Dfo.Server.Application/World/MonsterDropDomainService.cs` | T | 14(tb) |  |
| `Dfo.Server.Application/World/NoDropRateAdjustment.cs` | H | 10(f), 11(f) |  |
| `Dfo.Server.Application/World/QuestMonsterDropRules.cs` | T | 64(f), 65(b) |  |
| `Dfo.Server.Protocol/Channel/ChannelEncryptedFrame.cs` | W | 7(t) |  |
| `Dfo.Server.Protocol/Game/AccountRoster/AccountCharacterRosterPayloadCodec.cs` | W | 13(f), 81(b) |  |
| `Dfo.Server.Protocol/Game/AccountRoster/AccountCharacterRosterPayloadCodec.cs` | H | 18(t) |  |
| `Dfo.Server.Protocol/Game/AccountRoster/GetAccountCharacterRosterHandler.cs` | S | 38(a) |  |
| `Dfo.Server.Protocol/Game/AccountRoster/Messages.cs` | S | 20(a) |  |
| `Dfo.Server.Protocol/Game/AvatarJewelSocketOnTheWire.cs` | W | 8(t), 26(f), 47(t), 49(f) |  |
| `Dfo.Server.Protocol/Game/BossTowerPartyCoordination.cs` | T | 64(b), 129(b), 198(b) |  |
| `Dfo.Server.Protocol/Game/BossTowerProjectionDelivery.cs` | T | 135(b), 136(a), 619(f), 677(f) |  |
| `Dfo.Server.Protocol/Game/BossTowerProjectionDelivery.cs` | S | 150(b) |  |
| `Dfo.Server.Protocol/Game/BossTowerProjectionDelivery.cs` | I | 417(t) |  |
| `Dfo.Server.Protocol/Game/CardReward/CardRewardHandlers.cs` | S | 67(f), 68(b) | 68：0x0047 快照的成员排序在载荷内；先于该操作者自己的私有变化，同连接 |
| `Dfo.Server.Protocol/Game/CardReward/CardRewardPayloadCodec.cs` | W | 36(t), 59(b) |  |
| `Dfo.Server.Protocol/Game/CardReward/Messages.cs` | I | 12(a) |  |
| `Dfo.Server.Protocol/Game/CardReward/Messages.cs` | W | 38(a) |  |
| `Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs` | T | 139(b), 140(f), 153(b), 192(b), 193(t), 219(b), 301(t) |  |
| `Dfo.Server.Protocol/Game/CeraShopPurchase/CeraShopPurchaseCompleted.cs` | S | 23(b) |  |
| `Dfo.Server.Protocol/Game/CharacterDeath/CharacterDeathSlice.cs` | T | 107(t) |  |
| `Dfo.Server.Protocol/Game/CharacterDeath/DungeonPartyWipeService.cs` | T | 33(t), 82(t) |  |
| `Dfo.Server.Protocol/Game/CharacterDeath/UseRevivalCoinSlice.cs` | S | 110(b) |  |
| `Dfo.Server.Protocol/Game/CharacterName/CheckCharacterNameSlice.cs` | W | 15(t) |  |
| `Dfo.Server.Protocol/Game/CharacterName/CheckCharacterNameSlice.cs` | H | 86(t) |  |
| `Dfo.Server.Protocol/Game/CharacterNameField.cs` | W | 14(b) |  |
| `Dfo.Server.Protocol/Game/ClientHardwareDetail/Messages.cs` | I | 7(b) |  |
| `Dfo.Server.Protocol/Game/ClientSpecProfile/ClientSpecProfilePayloadCodec.cs` | W | 9(t) |  |
| `Dfo.Server.Protocol/Game/ClientSpecProfile/Messages.cs` | W | 11(t), 17(b), 23(f) |  |
| `Dfo.Server.Protocol/Game/CompoundItem/CompoundItemSlice.cs` | W | 59(t) |  |
| `Dfo.Server.Protocol/Game/CompoundItem/CompoundItemSlice.cs` | S | 108(f) |  |
| `Dfo.Server.Protocol/Game/ConnectionCheck/ConnectionCheckSlice.cs` | I | 19(f), 20(fb) |  |
| `Dfo.Server.Protocol/Game/ContentsPlayInfo/ContentsPlayInfoSlice.cs` | I | 9(b) |  |
| `Dfo.Server.Protocol/Game/ContentsPlayInfo/ContentsPlayInfoSlice.cs` | H | 13(f) |  |
| `Dfo.Server.Protocol/Game/ContentsPlayInfo/ContentsPlayInfoSlice.cs` | W | 52(b) |  |
| `Dfo.Server.Protocol/Game/CreateCharacter/CreateCharacterSlice.cs` | W | 15(t), 17(a) |  |
| `Dfo.Server.Protocol/Game/CreateCharacter/CreateCharacterSlice.cs` | S | 86(t), 91(ft) |  |
| `Dfo.Server.Protocol/Game/CreateCharacter/Messages.cs` | W | 7(t) |  |
| `Dfo.Server.Protocol/Game/CycleDungeonEntry/CycleDungeonEntrySlice.cs` | I | 36(t) |  |
| `Dfo.Server.Protocol/Game/CycleQuestRechallengeProjection.cs` | S | 9(b) |  |
| `Dfo.Server.Protocol/Game/DeleteCharacter/DeleteCharacterSlice.cs` | W | 43(t), 63(b) |  |
| `Dfo.Server.Protocol/Game/DeleteCharacter/DeleteCharacterSlice.cs` | R | 135(f) |  |
| `Dfo.Server.Protocol/Game/DeleteCharacter/Messages.cs` | W | 7(t) |  |
| `Dfo.Server.Protocol/Game/DisjointItem/EquipmentDisjointCompleted.cs` | S | 31(f) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/DungeonClearProjectionDelivery.cs` | S | 47(a) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs` | H | 31(t), 398(b), 405(fb), 647(b), 871(b) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs` | I | 44(a) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs` | W | 367(t), 386(f), 387(f), 444(b), 464(t), 465(ft), 508(f), 512(f), 610(t), 634(f), 777(a) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs` | R | 658(f) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs` | S | 775(t) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs` | T | 779(b), 868(b), 873(b), 932(b), 1055(b) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs` | W | 47(ft), 48(f) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs` | I | 88(ft) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs` | T | 129(b), 133(b) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs` | X | 283(b) | 283：XO-2（同伴先看到决定，然后才给领队 ACK） |
| `Dfo.Server.Protocol/Game/DungeonClear/ResultScreenSlice.cs` | W | 15(f), 148(f), 149(b), 152(f) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/ResultScreenSlice.cs` | I | 73(t) |  |
| `Dfo.Server.Protocol/Game/DungeonClear/ResultScreenSlice.cs` | T | 155(f), 183(b), 186(f) |  |
| `Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntryProjectionDelivery.cs` | S | 203(ft) | 203：接收者自己的批次内先放自己的 USERINFO，再放同伴的 |
| `Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntryProjectionDelivery.cs` | T | 332(b) |  |
| `Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntryProjectionDelivery.cs` | X | 353(f) | 353：XO-1（跟随者先于请求者） |
| `Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs` | R | 30(f), 117(b), 125(f), 279(f) |  |
| `Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs` | H | 60(b), 191(tb), 192(f), 199(f), 200(f) |  |
| `Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs` | W | 121(t), 331(b), 616(f) |  |
| `Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs` | T | 275(b), 294(b), 657(t) |  |
| `Dfo.Server.Protocol/Game/DungeonEntry/DungeonRunDeliveryRegistry.cs` | T | 15(b) |  |
| `Dfo.Server.Protocol/Game/DungeonEntry/FloorPieces.cs` | T | 46(b) |  |
| `Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs` | W | 32(b), 458(t), 459(t) |  |
| `Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs` | S | 67(b), 118(f), 668(a), 669(f) |  |
| `Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs` | R | 108(f) |  |
| `Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs` | T | 221(b), 552(b), 589(b) |  |
| `Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs` | H | 546(f), 720(f) |  |
| `Dfo.Server.Protocol/Game/Dungeons/DungeonLoadingProjectionDelivery.cs` | T | 84(f) |  |
| `Dfo.Server.Protocol/Game/Dungeons/DungeonLoadingProjectionDelivery.cs` | S | 107(fa) |  |
| `Dfo.Server.Protocol/Game/Dungeons/HellPartyProjection.cs` | W | 7(a) |  |
| `Dfo.Server.Protocol/Game/EnchantByBead/EnchantByBeadHandler.cs` | T | 67(a) |  |
| `Dfo.Server.Protocol/Game/FinishLoading/FinishLoadingSlice.cs` | S | 97(t) |  |
| `Dfo.Server.Protocol/Game/GameCmdEnvelope.cs` | W | 7(b), 21(t), 29(f), 31(f), 36(b) |  |
| `Dfo.Server.Protocol/Game/GameInventoryDelivery.cs` | W | 28(t), 51(a), 62(b), 162(a), 174(a), 185(t), 200(a) |  |
| `Dfo.Server.Protocol/Game/GameInventoryDelivery.cs` | H | 240(t) |  |
| `Dfo.Server.Protocol/Game/GameInventoryDelivery.cs` | R | 332(f), 371(f) |  |
| `Dfo.Server.Protocol/Game/GameInventorySnapshotCodec.cs` | R | 35(f) |  |
| `Dfo.Server.Protocol/Game/GameProtocol.cs` | I | 202(a) |  |
| `Dfo.Server.Protocol/Game/GameProtocol.cs` | S | 226(a) |  |
| `Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs` | W | 33(f), 101(a), 152(f) |  |
| `Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs` | T | 226(f), 283(b), 285(a), 324(b), 634(t) |  |
| `Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs` | S | 509(t) |  |
| `Dfo.Server.Protocol/Game/HiddenCharacterInfo/HiddenCharacterInfoSlice.cs` | S | 19(a), 36(b) |  |
| `Dfo.Server.Protocol/Game/HiddenCharacterInfo/Messages.cs` | H | 9(a) |  |
| `Dfo.Server.Protocol/Game/HonorLevel/HonorLevelSlice.cs` | W | 16(f) |  |
| `Dfo.Server.Protocol/Game/IllusionTowerPartyCoordination.cs` | T | 63(b), 127(b), 196(b) |  |
| `Dfo.Server.Protocol/Game/IllusionTowerProjectionDelivery.cs` | T | 127(b), 128(a), 600(f) |  |
| `Dfo.Server.Protocol/Game/IllusionTowerProjectionDelivery.cs` | S | 145(b) |  |
| `Dfo.Server.Protocol/Game/IllusionTowerStage/IllusionTowerStageSlice.cs` | I | 38(b) |  |
| `Dfo.Server.Protocol/Game/InitialConnection/GameInitialConnectionPayloadCodec.cs` | I | 17(a) |  |
| `Dfo.Server.Protocol/Game/Joust/JoustSlice.cs` | W | 82(f) |  |
| `Dfo.Server.Protocol/Game/Login/GameLoginHandler.cs` | S | 109(b), 113(t) |  |
| `Dfo.Server.Protocol/Game/Login/GameLoginHandler.cs` | H | 119(t), 131(f) |  |
| `Dfo.Server.Protocol/Game/Login/GameLoginResponseEncoder.cs` | W | 22(t), 28(b) |  |
| `Dfo.Server.Protocol/Game/MagicSeal/MagicSealCompleted.cs` | S | 39(f) |  |
| `Dfo.Server.Protocol/Game/MarriageHousing/HousingSlice.cs` | W | 68(b) |  |
| `Dfo.Server.Protocol/Game/MarriageProposal/MarriageEngagementSlice.cs` | W | 76(a) |  |
| `Dfo.Server.Protocol/Game/MarriageProposal/MarriageGuestbookSlice.cs` | W | 26(f) |  |
| `Dfo.Server.Protocol/Game/MarriageProposal/MarriageProposalSlice.cs` | W | 36(t), 37(b) |  |
| `Dfo.Server.Protocol/Game/MarriageProposal/MarriageWeddingSlice.cs` | X | 220(b) | 220：XO-7（是，保守列入；见 spec 附录 D） |
| `Dfo.Server.Protocol/Game/MarriageProposal/MarriageWeddingSlice.cs` | S | 233(f) |  |
| `Dfo.Server.Protocol/Game/MercenaryInfo/MercenaryInfoSlice.cs` | W | 49(t) |  |
| `Dfo.Server.Protocol/Game/MonsterDeath/ApplyObjectDestructionCommand.cs` | T | 47(t) |  |
| `Dfo.Server.Protocol/Game/MonsterDeath/GrantKillExperienceCommand.cs` | T | 43(f) |  |
| `Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs` | I | 29(t) |  |
| `Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs` | W | 33(a), 55(f), 100(a), 134(b), 214(f), 246(t), 355(t) |  |
| `Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs` | I | 28(a), 364(b) |  |
| `Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs` | H | 224(f), 266(t), 292(b), 383(f), 544(t) |  |
| `Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs` | T | 241(b), 496(b), 637(b), 881(b), 1056(f) |  |
| `Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs` | W | 261(f), 1029(b) |  |
| `Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs` | S | 927(fb) |  |
| `Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs` | T | 95(ft) |  |
| `Dfo.Server.Protocol/Game/OpeningDelivery.cs` | S | 27(ftb) |  |
| `Dfo.Server.Protocol/Game/PackageOpening/PackageOpeningSlice.cs` | W | 30(t) |  |
| `Dfo.Server.Protocol/Game/Party/PartyCommandSlice.cs` | T | 321(b), 322(a) |  |
| `Dfo.Server.Protocol/Game/Party/PartyNotificationSlice.cs` | V | 511(a), 664(b), 665(b) |  |
| `Dfo.Server.Protocol/Game/Party/PartyNotificationSlice.cs` | S | 518(f), 829(fb), 830(a) | 829：每个成员各自的连接上重放，同连接顺序；830：同上 |
| `Dfo.Server.Protocol/Game/QuestAcceptance/AcceptQuestCommand.cs` | T | 77(ab), 103(b), 188(b) |  |
| `Dfo.Server.Protocol/Game/QuestAcceptance/AcceptQuestHandler.cs` | H | 21(b) |  |
| `Dfo.Server.Protocol/Game/QuestAcceptance/AcceptQuestPayloadCodec.cs` | W | 17(b), 19(b) |  |
| `Dfo.Server.Protocol/Game/QuestAcceptance/Messages.cs` | W | 9(f), 10(b) |  |
| `Dfo.Server.Protocol/Game/QuestCompletion/FinishQuestCommand.cs` | T | 88(t), 94(b) |  |
| `Dfo.Server.Protocol/Game/QuestCompletion/FinishQuestHandler.cs` | S | 26(t) |  |
| `Dfo.Server.Protocol/Game/QuestCompletion/FinishQuestHandler.cs` | T | 149(b), 186(a), 189(b) |  |
| `Dfo.Server.Protocol/Game/QuestCompletion/FinishQuestHandler.cs` | H | 155(f) |  |
| `Dfo.Server.Protocol/Game/QuestCompletion/FinishQuestPayloadCodec.cs` | W | 25(t), 32(b), 87(t) |  |
| `Dfo.Server.Protocol/Game/QuestCompletion/Messages.cs` | I | 12(a) |  |
| `Dfo.Server.Protocol/Game/QuestCompletion/Messages.cs` | W | 16(f) |  |
| `Dfo.Server.Protocol/Game/QuestProgress/Messages.cs` | W | 14(f), 31(a) |  |
| `Dfo.Server.Protocol/Game/QuestProgress/MirrorQuestNotifications.cs` | V | 24(a) |  |
| `Dfo.Server.Protocol/Game/QuestProgress/SetQuestTriggerCommand.cs` | T | 76(b) |  |
| `Dfo.Server.Protocol/Game/QuestProgress/SetQuestTriggerHandler.cs` | H | 14(b) |  |
| `Dfo.Server.Protocol/Game/QuestProgress/SetQuestTriggerPayloadCodec.cs` | W | 22(b), 24(b), 36(a) |  |
| `Dfo.Server.Protocol/Game/RecoverStamina/RecoverStaminaSlice.cs` | W | 49(a) |  |
| `Dfo.Server.Protocol/Game/ReinforceItem/ReinforceItemHandler.cs` | T | 59(b) |  |
| `Dfo.Server.Protocol/Game/ResetRandomOption/ResetRandomOptionSlice.cs` | S | 130(f) |  |
| `Dfo.Server.Protocol/Game/ReturnToSelection/ReturnToSelectionCommand.cs` | T | 21(b) |  |
| `Dfo.Server.Protocol/Game/ReviveCoin/DailyReviveCoinSlice.cs` | W | 31(a) |  |
| `Dfo.Server.Protocol/Game/RosterFollowUp/Messages.cs` | I | 6(a) |  |
| `Dfo.Server.Protocol/Game/RosterFollowUp/RosterFollowUpHandler.cs` | I | 39(b) |  |
| `Dfo.Server.Protocol/Game/SelectCharacter/PvpParticipantPublishedNotification.cs` | H | 21(b) |  |
| `Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs` | S | 45(a), 296(b), 298(t), 299(a) |  |
| `Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs` | T | 319(b), 323(b), 324(f) |  |
| `Dfo.Server.Protocol/Game/SelectCharacter/SelectedCharacterStateMessageFactory.cs` | S | 37(t), 38(b), 140(a) |  |
| `Dfo.Server.Protocol/Game/SessionMetrics/SessionMetricsPayloadCodec.cs` | W | 7(t), 14(f), 15(b), 16(a), 74(f) |  |
| `Dfo.Server.Protocol/Game/SessionMetrics/SessionMetricsPayloadCodec.cs` | I | 20(tb) |  |
| `Dfo.Server.Protocol/Game/StateSynchronization/CharacterUserStateProjection.cs` | T | 26(fb) |  |
| `Dfo.Server.Protocol/Game/StateSynchronization/ItemUsageProjection.cs` | T | 35(b) |  |
| `Dfo.Server.Protocol/Game/StateSynchronization/PremiumDailyRefreshService.cs` | T | 68(fb) |  |
| `Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs` | S | 150(b), 165(b), 528(b) |  |
| `Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs` | W | 259(t), 400(a), 407(a), 635(a), 691(a), 785(a), 835(a), 851(fa), 945(a), 970(f), 978(b) |  |
| `Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs` | T | 269(b) |  |
| `Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs` | R | 807(f), 1139(f), 1141(f) |  |
| `Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Inventory.cs` | T | 58(b) |  |
| `Dfo.Server.Protocol/Game/StateSynchronization/SupplementalSynchronizationMessages.cs` | X | 226(t) | 226：XO-5（清理通告：所有者 → 城镇同伴 → 队伍） |
| `Dfo.Server.Protocol/Game/StateSynchronization/UsageQuotaProjection.cs` | W | 29(f) |  |
| `Dfo.Server.Protocol/Game/TaggedIdReport/GrantTutorialRewardCommand.cs` | T | 34(b), 35(fb) |  |
| `Dfo.Server.Protocol/Game/TaggedIdReport/Messages.cs` | I | 15(a) |  |
| `Dfo.Server.Protocol/Game/TaggedIdReport/TaggedIdReportHandler.cs` | T | 69(b) |  |
| `Dfo.Server.Protocol/Game/TaggedIdReport/TaggedIdReportPayloadCodec.cs` | W | 24(f), 34(t) |  |
| `Dfo.Server.Protocol/Game/TitleBook/TitleBookSlice.cs` | S | 214(b) |  |
| `Dfo.Server.Protocol/Game/TownProjection/TownAreaProjectionDelivery.cs` | T | 344(b) |  |
| `Dfo.Server.Protocol/Game/TownProjection/TownAreaProjectionDelivery.cs` | S | 484(b) |  |
| `Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs` | W | 34(f), 50(t), 94(t), 178(f) |  |
| `Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs` | H | 80(b), 210(t) |  |
| `Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs` | T | 261(b), 351(b) |  |
| `Dfo.Server.Protocol/Game/TutorialLevelUp/TutorialLevelUpSlice.cs` | R | 26(b), 111(f) |  |
| `Dfo.Server.Protocol/Game/TutorialLevelUp/TutorialLevelUpSlice.cs` | S | 142(t), 240(t) |  |
| `Dfo.Server.Protocol/Game/TutorialLevelUp/TutorialLevelUpSlice.cs` | W | 222(f) |  |
| `Dfo.Server.Protocol/Game/TutorialLevelUp/TutorialLevelUpSlice.cs` | T | 243(a) |  |
| `Dfo.Server.Protocol/Game/UnansweredReports/UnansweredReportSlices.cs` | I | 87(t) |  |
| `Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs` | I | 25(t) |  |
| `Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs` | H | 27(b) |  |
| `Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs` | S | 243(a) |  |
| `Dfo.Server.Protocol/Game/WeeklyAttendance/WeeklyAttendanceSlice.cs` | W | 27(t) |  |
| `Dfo.Server.Protocol/ProtocolModule.cs` | T | 577(t), 609(t) |  |
| `Dfo.Server.Protocol/ProtocolModule.cs` | H | 1059(a) |  |

## 3. 加法

**ProjectAsync**：第 1 节共 95 行，与 grep 命中 95 行相等。其中判定为「是」3 行（XO-5；XO-6、XO-7 为主控方裁定的保守列入），其余 92 行均为「不是」。

**关键词命中按分类（去重行号）**：
W 125 ＋ S 53 ＋ T 183 ＋ V 4 ＋ R 54 ＋ I 27 ＋ H 46 ＋ N 2 ＋ X 4 ＝ 498，9 项；与去重行数 498 相等。

**关键词命中按关键词（清单中出现的次数）**：
`first` 145 ＋ `then` 105 ＋ `after the` 72 ＋ `before` 204 ＝ 526，4 项；与检索命中合计 526 相等。
两种计数之差 526 − 498 ＝ 28，即一行同时命中两个及以上关键词的重复次数。

## 4. 结论

- 逐行处置没有在 XO-1～XO-7 之外发现新的跨连接顺序要求。分类为 X 的 4 行都已登记：XO-1（DungeonEntryProjectionDelivery.cs:353）、XO-2（LeaveDungeonSlice.cs:283）、XO-5（SupplementalSynchronizationMessages.cs:226）、XO-7（MarriageWeddingSlice.cs:220，保守列入）。
- XO-3、XO-4（两塔进入跟随者优先，BossTowerProjectionDelivery.cs:175、IllusionTowerProjectionDelivery.cs:171）没有注释，关键词检索不会命中，来源是上一轮的 OrderBy 模式检索。
- XO-6（队伍跨收件人阶段屏障）在本清单里只出现在 ProjectAsync 第 1 节的 PartyTownProjectionDelivery.cs:271；关键词命中中 PartyNotificationSlice.cs:664/665 归为 V（跨版本连续），829/830 归为 S。主控方裁定保守列入（spec 附录 D）。
