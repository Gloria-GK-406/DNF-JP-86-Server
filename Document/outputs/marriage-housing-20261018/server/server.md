# 婚房 / 情侣仓库 / 情侣树 / 成长胶囊 —— 服务端接缝调查

> 状态：完成（主机崩溃后续作）。只读调查，未改代码、未切分支、未提交；未运行测试、未读 PVF 原档、未实机。
> 分支：`task/next-round-20261013`。日期：2026-10-18。
> 路径若无前缀，均相对 `Server/src/`。标注：**[现状]** = CurrentImplementation（读代码得出）；**[建议]** = Proposal。
> 协议布局（03D8..03DE、03FD..0406 字段）不在本报告范围，由 A21 子代理负责；本报告只讨论服务端落点。

## 0. 结论速览

1. **可复用**：`0x0024` 换城入口与 `CanCrossAsync` 门位；`TownAreaKey` 分区可见性；教堂的准入表形状与 `MarriageWeddingProjection.MoveAsync`（强制挪出）；
   `IMultiAccountCommand` + 排序账号租约 + `CoupleAccountsAsync` 预取复核（心意券是完整样板）；通用物品生命周期与 `0x000D/0x000E` 快照/增量；
   `IItemDeliveryDomainService(MailOnly)` 给配偶发奖；`IEffectSettlementRequest` 重放防重复；`TimeProvider` + `IDailyResetRuleService` + 通用 `ScheduledTaskHostedService`。
2. **必须新增**：一个与婚前任务单元 T 平级、只读关系的新单元 H（`CoupleHousingDomainService`）及组件 CoupleRoomLayout / CoupleStorage / CoupleTree / GrowthCapsules / RoomVisits；
   整数夫妻号表；摆位、房间尾部、树、浇水、收获表；通用容器的第三种主人 `couple`（需重建 `item_slots` 放宽 CHECK）或婚姻托管表二选一（倾向前者）。
3. **实例化是第一难点**：教堂的"隔离"只是每场婚礼占一个不同 area（town 2 area 12..62），没有实例层。101 若只有一个 area，要么给 `TownAreaKey` 加实例维度，要么所有夫妻同处一屏。
4. **现存缺口**：今天只要 PVF 声明了 101 的 area，任何人发 0x0024 都能进（非教堂区一律放行）。
5. **投影时机**：选角在 `SelectedCharacterStateMessageFactory.cs:111` 处"已婚发真实 03D8、否则保持 204 零"；进 101 在 `TransferAsync` 之后发房主的 03D8/树状态；新增重放位必须同步改 `ReplayProjectionRequest.Validate` 白名单。
6. **主要风险**：101 坐标落库导致重登卡房、`item_slots` 重建迁移、心意点若被消耗会被启动对账"复活"、effect context 单收件人（配偶份额只能走邮件/共享容器）、未闭合协议字段导致客户端错位断线、离婚资产规则缺失、"成长胶囊"与账号成长胶囊同名。

## 1. 进入婚房（town 101）

### 1.1 [现状] 城镇/区域移动只有一个入口

- 换城、跨区都走 CMD `0x0024 SET_USER_AREA`，由 `SetUserAreaHandler` 处理（`Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs:222-379`）。
  区内走动是 `0x0023`（同文件 `:392-429`）；`0x002D MoveMap` 只管地下城换房（`Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:650-719`）；
  传送道具另有一条换城路径，最后同样调 `ProjectTransferAsync`（`Dfo.Server.Protocol/Game/Teleport/TeleportSlice.cs:105-125`）。
- `0x0024` 的门按顺序是（`TownProjectionSlice.cs`）：
  1. `towns.DeclaresAreaAsync`：PVF `town/town.lst` 没声明的 town/area 静默丢弃（`:278-290`；判定在 `Dfo.Server.Application/World/EntryPlacementRuleService.cs:63-73`，内容源 `Dfo.Server.Infrastructure/Content/PvfTownDefinitionSource.cs:39,95-103`）。
  2. `towns.CanEnterTownAsync`：频道策略。普通频道只排除 17/10/19 三个特殊城（`EntryPlacementRuleService.cs:50-61,134-143`），拒绝时 `RefuseTownChangeAsync`（`TownProjectionSlice.cs:295-312`）。
  3. 婚姻门：`marriage.CanCrossAsync`（`:317-323`），再 `marriage.CanLeaveAsync`（`:324-329`）。
  4. 非临时频道先持久化目标坐标（`:332-340`），再 `positions.CrossInto` + `townAreaProjection.TransferAsync`（`:342-358`）。
  5. 离开教堂且婚礼不在 Preparing 时补发 03CF 复原（`:360-364`）。
- 在哪个区由内存 `TownAreaDomainService` 持有：`Dictionary<TownAreaKey,TownArea>` + `byCharacter` 索引，区按需建、空了即删（`Dfo.Server.Application/World/TownAreaDomainService.cs:9-14,225-247`）。
  键是 `TownAreaKey(ChannelId, TownId, AreaId)`（`Dfo.Server.Abstractions/World/ITownAreaDomainService.cs:6-20`）。坐标另归 `ICharacterPositionDomainService`。
- town 编号**没有白名单**，全部来自 PVF 与频道配置。PVF 侧事实：`101 -> CoupleRoom.twn` 在 `town/town.lst`，指向 `CoupleRoom/CoupleRoom.map`，声明 `[couple room] 474 234`（`Document/outputs/marriage-research-20261016/pvf/pvf.md:212`；`protocol/patterns/data/probes/pvf-town-list-20260817.md:35`）。
  **本次没有核 CoupleRoom.twn 声明了几个 area**；pvf.md 只提到一张 map。

**推论 [现状/High]**：只要 101 的 area 已在 PVF 中声明，今天客户端发 `0x0024 town=101` 在普通频道就会被放行——**没有任何"必须已婚/必须是这对夫妻或受邀者"的门**。
`CanCrossAsync` 对非教堂区一律 `return true`（`Dfo.Server.Application/Marriage/MarriageDomainService.Query.cs:87-89`）。这既是"进入几乎不用改"，也是一处现存的越权入口（见 §7 R1）。

### 1.2 [现状] 教堂"隔离房间"的真实形状

教堂没有独立的实例层，隔离靠**每场婚礼占用一个不同的真实 PVF area**：

- `MarriageChurchRuleService.LocationsAsync` 只认 town 2、area 12..62、map 以 `church/church.map` 结尾的区，站位取第一块可走区域中心（`Dfo.Server.Application/Marriage/MarriageChurchRuleService.cs:31-44`）。
- 表 `marriage_church_rooms`：`UNIQUE(channel_id,town_id,area_id)`、`CHECK(area_id BETWEEN 12 AND 62)`、`token` 自增（`Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:289-297`）；
  准入表 `marriage_church_admissions(ceremony_id, character_id)`（`:298-301`）。
- `ChurchRooms.PrepareAsync` 逐个 `TryCreateAsync`（`INSERT … ON CONFLICT DO NOTHING`），第一个空闲 area 即本场房间（`Dfo.Server.Application/Marriage/ChurchRooms.cs:51-68`；`Dfo.Server.Persistence/Marriage/SqliteMarriageChurchDomainStore.cs:19-32`）。每频道最多 51 个并发房间。
- "在不在房里"就是比较 `TownAreaKey(room.ChannelId, room.TownId, room.AreaId)`（`ChurchRooms.cs:31,35-48,158`）。
- 进出门：`CanCrossOwnAsync`（新人：房间匹配 + 未过 ReadyUntil 或已在房内 + 已准入，`ChurchRooms.cs:100-110`）、`CanCrossAsGuestAsync`（`:113-123`）；`CanLeaveAsync` 只在最后 60 秒锁离场（`MarriageDomainService.Query.cs:100-108`）。
- **"原生上下文恢复"并不保存入场前位置**：出口是固定常量 `ExitLocation = (town 2, area 2, 2016, 212)`（`Dfo.Server.Data/Marriage/MarriageWeddingModels.cs:37-38`）。
  取消/清场时 `WeddingSettlement.PersistExitAsync` 把仍存着教堂坐标的角色（含离线者）改写到出口（`Dfo.Server.Application/Marriage/WeddingSettlement.cs:89-96`）；
  在线者由调度器 `MarriageWeddingProjection.MoveAsync` → `CrossInto` + `TransferAsync`（`Dfo.Server.Protocol/Game/MarriageProposal/MarriageWeddingSlice.cs:185-191`）。
  03CF（`WeddingAvatar`，b[0]=Restore）只是**临时外观投影**，不是位置恢复（`MarriageWeddingSlice.cs:20-31`；进入时 Restore=false 在 `MarriageChurchSlice.cs:129-130`）。

### 1.3 [现状] 同屏可见与广播

- 可见性完全按 `TownAreaKey` 分区；`TownAreaSnapshot.VisibleTo` 中 `SharesTownPresence=false`（area 种类为 Gate）的人只看得到自己（`ITownAreaDomainService.cs:80-88`；`EntryPlacementRuleService.cs:75-85,156-157`）。
- 入区：新人收 0x0017 + 每个在场者的 0x0002/0x0165/0x0017 + 0x0018；在场者收 `TownParticipantPublished`（`Dfo.Server.Protocol/Game/TownProjection/TownAreaProjectionDelivery.cs:124-209`；`TownPresenceNotifications.cs:35-49,88-126`）。
  离区：给旧区剩余者重发 0x0018（`TownPresenceNotifications.cs:22-33`；`TownAreaProjectionDelivery.cs:602-664`）。走动 0x0016（`:241-321`）。强制站位 0x008A（`:212-239`）。
- 人群以角色/会话表达，经 `IEventService.ProjectAsync` 与 `NotificationAudience` 投递；领域服务不得为投递查连接（仓库 `AGENTS.md`「领域事件与即时投影」）。

### 1.4 [建议] 婚房实例化的三个选项

| 选项 | 做法 | 代价 | 判断 |
|---|---|---|---|
| A. 共享一个 area | 所有夫妻进 `(ch,101,area)` 同一个键 | 0 改动 | **不可用**：所有夫妻互相可见，03D8 家具投影无法"按房主"成立 |
| B. 仿教堂分 area | 每对夫妻占用 101 的一个空闲 area | 只在 CoupleRoom.twn 声明多 area 时成立；area 数即并发上限 | 先核 PVF。若 101 只有 1 个 area，此路不通 |
| C. `TownAreaKey` 加实例维度 | `TownAreaKey(Channel, Town, Area, Instance=0)`，婚房用 relation 派生的实例号 | 波及 `TownAreaDomainService`、`TownAreaProjectionDelivery.KeyOrder`（`:723-724`）、Party 目录 scope、所有 `new TownAreaKey(...)` 构造点 | **倾向**。实例号默认 0 时现有行为不变；一对夫妻一个实例，访客跟随房主进入同一实例 |

无论选 B 还是 C，都需要：

1. **进入门**：在 `CanCrossAsync` 前加婚房分支（或拆出新的 `IMarriageHousingDomainService.CanEnterRoomAsync`，在 `TownProjectionSlice.cs:317` 之前调），只放行"本人是已婚（stage=Married）参与者"或"持有该房访客许可"的会话。
   拒绝时复用 `RefuseTownChangeAsync`（0x0017 退回原位 + 0x01F7 提示）。
2. **"进的是谁的房"**：0x0024 正文只有 town/area/x/y（`TownProjectionSlice.cs:154-174`），没有房主字段。房主只能来自服务端状态：本人进 → 自己这对；访客进 → 先由某条婚房协议（布局待 A21 子代理确认）登记"要去谁家"，再由 0x0024 兑现。这条许可应是**短期内存状态**（类似 `Sessions/` 下的运行态服务），不必持久。
3. **离开与重登恢复**：婚房坐标不应被持久化成"下次登录直接落进 101"（若用选项 C，实例号不持久，重登时无法还原到同一实例）。
   建议仿 `0x0024` 的 `channel.IsTransient` 分支：进入 101 时**不写** `PersistAsync`，保留进房前的城镇坐标（`TownProjectionSlice.cs:332-340` 的条件加一个"婚房不落库"）；
   另外登录恢复 `CanRememberInnAsync` / `ResolveTownGateSpawnAsync` 要求 town 有可用 Gate，否则回落城 1（`EntryPlacementRuleService.cs:28-30,87-132`），这一层可作兜底但不应依赖。
4. **教堂能复用的部分**：准入表的形状（`*_admissions`）、`CanCrossAsync` 的分支位置、`RefuseTownChangeAsync`、`MarriageWeddingProjection.MoveAsync`（强制挪出房间，用于离婚/踢访客）。
   **不能复用的部分**：`marriage_church_rooms` 的 area 12..62 CHECK 与 ceremony 外键、`ChurchRooms` 本身（它的 store 归属已被架构判据锁定为单一所有者，见 §5）。

## 2. 家具 / 情侣仓库物品

### 2.1 [现状] 物品容器只认"角色"和"账号"两种主人

- 容器类型 `InventoryContainerType`（`Dfo.Server.Data/Inventory/InventoryModels.cs:6-26`）：Main=0、Avatar=1、PersonalCargo=2、Equipment=3、Pet=7、AccountCargo=12、TitleBook 19..23、QuickSlot=29、SharedMaterial=240 等。
  主人枚举只有 `InventoryOwnerType { Character=0, Account=1 }`（`:134-138`）。
- `ContainerId` 是字符串 `"{character|account}:{ownerId}:{TypeName}"`，`TryGetOwner` 只接受这两种前缀和正整数 owner（`Dfo.Server.Abstractions/Inventory/IItemContainer.cs:17-27,81-101`）。
- 持久层 `SqliteItemDomainStore.OwnerOf` 对其它主人/类型直接抛异常，并强制 AccountCargo/SharedMaterial 归账号（`Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs:386-395`）。
- 表（`Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`）：
  - `items`（`:379`）：`item_uid` 自增主键 + 82 字节 core + 时装/宠物扩展列；位置列已移出。
  - `item_slots`（`:380`）：`owner_scope TEXT CHECK (owner_scope IN ('character','account'))`、`owner_id INTEGER`、`list_type`、`slot_index`、`kind IN('world','template')`；`UNIQUE(owner_scope,owner_id,list_type,slot_index)`。
  - 容器状态：`account_cargo_state`（`:384`）、`character_container_state`（`:397`）；邮件三表 `:425-427`。
- 生命周期：`Get/Take/Put/Update`，销毁必须先 `Take` 再 `IItemLifecycleDomainService.DeleteItem`；`ItemLifecycleScope` 保证 detached 记录在提交前被安置或删除（仓库 `AGENTS.md`「物品：统一属性、容器内引用与取出记录」）。
  邮件附件是唯一的"非容器持久托管"先例：`IMailItemCustodySession`（`Dfo.Server.Abstractions/Mail/IMailItemCustodySession.cs:7-12`），由 `SqliteItemDomainStoreSession` 的 partial 实现（`Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.Mail.cs:12-60`）。
- 在线/离线入口：`ICharacterBusinessInventoryDomainService.GetStore`（在线注册表）与 `GetPersistentStore(ContainerOwner, kind)`（显式离线持久访问，不注册在线容器）（`Dfo.Server.Abstractions/Inventory/ICharacterBusinessInventoryDomainService.cs:30-33`；实现 `Dfo.Server.Application/Inventory/CharacterBusinessInventoryDomainService.cs:49-69`）。
- **今天唯一的"共享"是同账号多角色**：AccountCargo(12)、SharedMaterial(240)，以 `item_slots('account', account_id, …)` 存放；跨两个账号的共享容器不存在（Inventory、Persistence 目录下搜 couple/marriage 零命中）。

### 2.2 [现状] 移动、删除、同步消息

| 动作 | C2S | 命令与注册 | 回包/同步 |
|---|---|---|---|
| 物品移动（含个人/账号仓库存取） | CMD `0x0013` | `MoveItemSpaceCommand : IAccountCommand`（`Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs:270-280`），Transactional / Account Exclusive（`Dfo.Server.Protocol/ProtocolModule.cs:576-577`；同文件 `:570-571` 注释说"不取账号锁"，已过期）。仓库分支走 `ICargoTransferDomainService.MoveAsync`（`MoveItemSpaceSlice.cs:422-446`；`Dfo.Server.Application/Inventory/CargoTransferDomainService.cs:23-34,68-71`） | CMD 回执 + 涉及容器的整容器快照 NOTI `0x000D`（`MoveItemSpaceSlice.cs:861-873,913-934`；`Dfo.Server.Protocol/Game/GameInventorySnapshotDelivery.cs:23-35`），`SendExplicitAsync` 一次发出（`:904-907`） |
| 丢弃/删除 | CMD `0x0012` | `DiscardItemCommand`，Transactional、OnlineUser+Account Exclusive（`Dfo.Server.Protocol/Game/DeleteItem/DiscardItemCommand.cs:11-21`；`ProtocolModule.cs:687-688`） | `ProjectAsync(ItemDiscardCompleted)` → CMD `DeleteItemResponse` 等（`Dfo.Server.Protocol/Game/DeleteItem/ItemDiscardCompleted.cs:33-47`） |
| 金库开设/升级/金币存取 | `0x0131..0134`、`0x0198`、`0x01AF` | `WarehouseCommand` Exclusive/Exclusive（`ProtocolModule.cs:677-678,892-903`） | CMD + Main/仓库 `0x000D` 快照（`Dfo.Server.Protocol/Game/Warehouse/WarehouseHandler.cs:39-46,59-78`） |
| 增量 | — | — | NOTI `0x000E UPDATE_ITEM_LIST`（`Dfo.Server.Protocol/Game/GameInventoryDelivery.cs:255-300`）、`0x00FB` 锁定状态 |

仓库没有独立"打开/关闭"opcode（搜 OpenCargo/CloseCargo 无结果），所以 `0x03FD` 情侣仓库开关在服务端是**全新概念**。要先由 A21 子代理确认它是否需要服务端状态；若只是 UI 开关加回发 03D9 快照，就没有持久状态。

### 2.3 [建议] 情侣仓库的持久化落点

| 方案 | 做法 | 优点 | 代价/风险 |
|---|---|---|---|
| **A. 通用容器加一种主人 `couple`** | `InventoryOwnerType.Couple=2`；`ContainerId` 认 `couple:{no}:{Type}`；新容器类型（编号待 A21 确认 03D9 的 list type）；`OwnerOf` 放行 | Take/Put/Update/Delete、`ItemLifecycleScope`、单数据库单参与者、0x000D 快照编码、UID 跨容器不变全部现成；物品仍只有一个真源 | ① `item_slots.owner_scope` 的 CHECK 只能**重建表**才能放宽（SQLite 不改 CHECK），这是全库核心表，须单独迁移 + `integrity_check` + 真实库副本演练（参照 `MarriageRelationMigration`）；② `owner_id` 是 INTEGER 而关系 id 是 TEXT GUID（`CharacterDatabaseSchema.cs:302-303`），要新增整数夫妻号；③ 所有按 `Scope` 分支的代码要补一支 |
| B. 婚姻自管托管表 | 仿邮件附件：新 `IMarriageCoupleStorageCustodySession` 由 `SqliteItemDomainStoreSession` 新 partial 实现，表 `marriage_couple_storage(couple_no, slot, item_uid…)` | 不动 `item_slots` | 要重写一套 Take/Put/合堆/准入校验（邮件 custody 的 Accept 校验已近 50 行，`SqliteItemDomainStore.Mail.cs:20-60`），0x000D/03D9 快照也要另写；两条持久化路径并存，漂移风险高 |

**倾向 A**。AGENTS 明确"物品写入统一通过生命周期接口"，B 等于再开一条路。A 的迁移是一次性成本，之后婚房家具、情侣树产出都能直接表达成"放进 couple 容器"。
夫妻号建议新表 `marriage_couple_numbers(couple_no INTEGER PRIMARY KEY AUTOINCREMENT, relation_id TEXT NOT NULL UNIQUE REFERENCES marriage_proposals(id))`，在 T8（婚礼完成）同一事务里分配，或在首次使用时懒分配。它同时可作 §1.4 选项 C 的实例号。

### 2.4 [建议] 房内摆放家具

- 03D8 每条是 `u16 slot + u32`（`Document/MarriagePhase1Research-20261016.md:111-118`；字段语义待 A21 子代理）。服务端最自然的形状是每个摆位一行：
  `marriage_room_furniture(couple_no, slot u16, value u32, item_uid NULL, placed_at, PRIMARY KEY(couple_no, slot))`，
  外加 `marriage_room_state(couple_no PK, 尾部 3×u32 + u8, revision)` 存 13B 尾部。
- **关键未决（要 A21 证据）**：家具摆上后，是"仍是一件物品，只是被引用"，还是"被消耗成摆位记录"。
  - 仍是物品：摆位行只存 `item_uid` 引用，物品本体留在 couple 容器（单一真源）；收回就是删摆位行。
  - 被消耗：`Take` + `DeleteItem` 与写摆位行同事务；收回是删行 + `IItemLifecycleDomainService.Create` 新建（UID 会变）。
  两种不能混用，否则会出现"物品在仓库、同时摆在房里"的双计。
- `0x03FE` 婚房移动物品：做成 **Transactional + Account Exclusive 的 `IMultiAccountCommand`**，账号表由 `CoupleAccountsAsync` 预取（模式见 `Dfo.Server.Protocol/Game/MarriageProposal/MarriageLovePointSlice.cs:54-73`），用例内复核（照 `Dfo.Server.Application/Marriage/MarriageDomainService.Couple.cs:161-174` 的 `accounts.SequenceEqual(CoupleAccounts(...))`）。
- `0x03FF` 情侣仓库删除：照 `DiscardItemCommand` 的 Take → DeleteItem，只是命令换成双账号。

### 2.5 双人共享资源的并发与离线配偶

- [现状] `AccountExecutionBehavior`（Order 55）按 `Distinct().OrderBy(Value)` 逐个取账号租约，全部到手后才进入 Order 60 的事务（`Dfo.Server.Application/Commands/Pipeline/AccountExecutionBehavior.cs:35-49`；契约 `Dfo.Server.Application/Commands/CommandContracts.cs:58-62`）。
- [现状] 婚姻 handler 在命令外层再按同样顺序取一次 `SelectedSessionTransitionGate`，把 ACK 和对端 NOTI 包进同一道栅栏（`Dfo.Server.Protocol/Game/MarriageProposal/MarriageProposalSlice.cs:258-274`；调度器同法 `MarriageWeddingSlice.cs:211-216`）。
- [现状] 租约是**内存门**，离线账号也能取到；`CoupleAccounts` 对已成对的关系总是返回双方账号，与在线无关（`Dfo.Server.Application/Marriage/MarriageDomainService.cs:42-45`）。所以一方离线不会阻塞共享仓库操作，离线方的数据经持久 store 读写即可。
- [建议] 双人资源（couple 容器、摆位、树）**一律走双账号租约**，即使操作者只碰共享物、不碰对方的个人物：配偶可能同时在另一条连接上操作同一件共享物。第二道防线是各表的 `revision` CAS（仿 `marriage_relations`）。
- [建议] 对方的在线投影只在提交后、按 `sessions.Find(peer.AccountId) == peer` 判断后发（照 `MarriageLovePointSlice.cs:82`）；离线方不补播，重登时由 §4 的选角投影还原。

## 3. 情侣树与时间机制

### 3.1 [现状] 可用的时间与调度设施

| 设施 | 位置 | 适用 |
|---|---|---|
| `TimeProvider`（环境时钟，在命令处理器注入豁免清单上） | 各组件构造器，如 `ChurchRooms.cs:143`、`CoupleLedger.cs:155` | 一切成长计算的"现在" |
| 惰性到期 | 写路径在用例里结算（`MarriageDomainService.cs:48-73`），读路径只投影不写库（`MarriageDomainService.Query.cs:10-32`） | 树的"当前阶段"在读时计算 |
| 每日周期 | `IDailyResetRuleService.PeriodStart / IsCurrentPeriod`（`Dfo.Server.Abstractions/Configuration/IDailyResetRuleService.cs:3-7`） | "每人每天浇水一次" |
| 通用调度器 | `ScheduledTaskHostedService` + `AddScheduledTask(Factory)`（`Dfo.Server.Application/Scheduling/ScheduledTaskHostedService.cs`、`ScheduledTaskExtensions.cs:21-36`）；先例 `inventory.expiration.online`（`Dfo.Server.Application/Inventory/OnlineExpiredItemCleanup.cs:91`）、`tau-kingdom.ticket-refill.online`（`Dfo.Server.Application/World/TauKingdomDailyRefill.cs:52`） | 只在确有"到点主动推送"需求时用 |
| 婚礼专用 1 秒轮询 | `MarriageWeddingScheduler`（`MarriageWeddingSlice.cs:193-247`） | 婚礼专用，不建议复用到树 |
| 账号成长胶囊 | `IExperienceCapsuleDomainService.AddAsync(account, exp)`（`Dfo.Server.Abstractions/Characters/IExperienceCapsuleDomainService.cs:5-8`），投影 `SupplementalSynchronizationCodec.EncodeGrowthCapsule`（`Dfo.Server.Protocol/Game/StateSynchronization/SupplementalSynchronizationCodec.cs:43-76`） | **注意同名**：0x0404..0406 的"成长胶囊"是不是这个账号胶囊，要 A21/PVF 确认后再定复用还是新建 |

### 3.2 [建议] 树的成长用惰性计算

- 存事实，不存阶段：存 `planted_at`、浇水记录、施肥记录；阶段由规则服务按 `TimeProvider.GetUtcNow()` 和这些事实算出。纯函数放静态类 `MarriageTreeRules`，由 `IMarriageHousingRuleService` 调用，符合 AGENTS 的三层约定。
- 写操作（浇水、收获、移除）在命令事务内先按当前时间结算，再做守卫，与婚姻"写路径到期结算"同构。
- 只有需要"成熟时主动提醒在线玩家"时才注册 `AddScheduledTaskFactory` 任务。它只做提示投影，**不得**推进存储状态，否则就有了第二个推进者。默认不需要：玩家进房或打开树 UI 时由读路径投影 03DC/03DD 即可。

### 3.3 奖励发放路径与 effect context 约束

- [现状] `EffectOperationContext.Record` 要求效果 owner 的 session/account/character 与本次主请求捕获的收件人完全一致，否则抛 `InvalidOperationException`（`Dfo.Server.Application/Effects/EffectOperationContext.cs:123-133`）；库存足迹同样只许主请求本人（`:115-121`）。
- [建议] 由此得出：
  - **操作者本人**的消耗（种子、肥料、水壶）和产出，走 `IApplyEffectsDomainService.ApplyAsync(new(actor.AccountId, actor.CharacterId, at, actor), plan.Effects)`，照心意券 `LovePoints.SpendAsync`（`Dfo.Server.Application/Marriage/LovePoints.cs:313-327`）。请求实现 `IEffectSettlementRequest` 并声明 `MinimumReplayNeeds`（照 `WeddingLovePointRequest`，`MarriageLovePointSlice.cs:14-18`），同一帧重发就走既有重放，不再结算一次。
  - **配偶**那一份**不能**在同一个 effect context 里发。可选两条路：`IItemDeliveryDomainService.DeliverAsync(..., ItemDeliveryMode.MailOnly)`（婚礼奖励先例 `WeddingSettlement.cs:66-67`；邮件不要求在线，`Dfo.Server.Abstractions/Mail/IMailboxDomainService.cs:6-16`）；或者直接放进 couple 容器（若采用 §2.3 方案 A，"收获进共享仓库"本身就解决了双人问题）。
  - 不要用 `GrantAllPersistentAsync` 往**在线**配偶背包里塞东西：它绕过在线投影，对方客户端看不到增量。
- [现状] 简报里说的"幂等奖励账本 `CoupleLedger`"与代码不符。`CoupleLedger` 是关系真源（阶段 CAS 和心意点），不是奖励账本（`CoupleLedger.cs:153-231`）。现有"不重复发奖"靠两种手段：结算点 CAS（婚礼行 `FinishAsync` 只能从 Preparing 走一次，`WeddingSettlement.cs:29-30`），或者主键（如 `sao_first_clear_rewards PRIMARY KEY(run_id,character_id)`，`CharacterDatabaseSchema.cs:314`）。
  [建议] 树收获用 `marriage_tree_harvests`：每人各收一次就用 `PRIMARY KEY(planting_id, character_id)`，整棵只收一次就用 `PRIMARY KEY(planting_id)`。插入失败即拒绝，与发奖同事务。
- "夫妻双方都能浇水/收获"不受 effect 约束影响：每次请求只结算操作者本人；双方共享的是树的状态行（由双账号租约 + revision CAS 保护），不是 effect owner。

## 4. 选角 / 重登 / 入城投影时机

### 4.1 [现状]

- 选角序列：`SelectedCharacterStateMessageFactory` **无条件**加 `CoupleRoomEmptyCompatibilityMessage`（204 字节全零的 NOTI `0x03D8`），随后若有婚约再加 `WeddingInfoMessage`（03D0）（`Dfo.Server.Protocol/Game/SelectCharacter/SelectedCharacterStateMessageFactory.cs:111-113`；消息定义 `Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs:410-415`，注册 `:635`）。
- `Engagement` 来自 `SelectCharacterPreparationService` 末尾的 `marriage.SnapshotAsync(...)`（`Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs:236`；可空注入 `:41-42`；DTO 字段 `Dfo.Server.Data/Game/SelectCharacterModels.cs:46`）。
- 重放：`ReplayProjectionNeeds.MarriageLovePoints = 16384`，`ReplayProjectionRequest.Validate` 用**显式白名单**拒绝未知位（`Dfo.Server.Abstractions/Effects/IReplayStateProjection.cs:14-31`）；取数在 `Dfo.Server.Application/Effects/ReplayStateDomainService.cs:155-156`，投影在 `Dfo.Server.Protocol/Game/ReplayStateProjectionDelivery.cs:160-161`。
- 旧彩票兼容：选角已不再从账号彩票进度构造 03D8，固定发 204 零；回归用例是 `SelectCharacterPayload_CoupleRoomEmptyCompatibilityDoesNotProjectLegacyLotteryData`（`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs:212`）；协议记录 `protocol/game/server-to-client/NOTI-0x03D8.md:5`。
  `Document/MarriagePhase1Research-20261016.md:118` 写的"当前仓仍把该 opcode 实现为 204 字节旧彩票进度"已过时。

### 4.2 [建议]

| 时机 | 发什么 | 落点 |
|---|---|---|
| 选角（已婚） | 本对夫妻的 03D8（家具）；03D9（情侣仓库，前提是 A21 证明选角时需要） | `SelectCharacterResult` 加 `CoupleRoom` 快照字段，由 `SelectCharacterPreparationService` 经新的只读 `IMarriageHousingQueryDomainService` 填；factory 在 `:111` 处"有快照发真实 03D8，否则发 204 零" |
| 选角（未婚/无房） | 维持 204 零 | 不改，保住现有回归 |
| 进入 101（本人或访客） | **房主**的 03D8、树状态（03DC/03DD 候选） | 在 `SetUserAreaHandler` 的 `TransferAsync` 成功之后（`TownProjectionSlice.cs:342-358` 之后），位置仿教堂 03CF 的补发点；访客看到的是房主的数据，不是自己的 |
| 0x03FD 打开情侣仓库 | 03D9 整仓快照 | 命令回执后 `SendExplicitAsync` |
| 共享物变化（03FE/03FF/树操作） | 操作者：CMD 回执 + 增量；在线配偶：同类 NOTI；同房访客：家具变了就发 03D8 更新 | 收件人用 `NotificationAudience.TheseSessions`；"同房有谁"从 `TownAreaSnapshot` 取，不在领域服务里查连接 |
| 重放（同帧重发） | 新增 `ReplayProjectionNeeds.CoupleRoom / CoupleInventory / CoupleTree` 位 | **必须同时改 `Validate` 白名单**（`IReplayStateProjection.cs:29`），否则第一次重放就抛 `InvalidDataException` |

对旧彩票 03D8 的影响：只要"未婚 = 204 零"保持不变，旧彩票数据就继续不被投影。**不要**把空态缩短成 17 字节，现有回归和实机空态都以 204 为准。非空婚房体长 = 17 + 6×count；A21 handler 按计数读并容忍尾随字节（`MarriagePhase1Research-20261016.md:111-116`），所以理论上不必补零，但要 A21 实机确认。

## 5. 婚姻聚合扩展建议

### 5.1 [现状] 必须遵守的结构判据

`Server/tests/Dfo.Server.Application.Tests/Marriage/MarriageArchitectureTests.cs`：

- `Dfo.Server.Abstractions.Marriage` 命名空间下每个 `*DomainStore` 接口，在 `Application.Marriage` 里**只能有一个构造器接收者**；关系 store 只归 `CoupleLedger`（`:32-33,43-55`）。新的婚房 store 只要放进同一命名空间，就自动受这条判据约束。
- 只有 M（`MarriageDomainService`）接 `IMarriageRelationWriter`；T 接只读的 `IMarriageRelations`；没有类型直接接 `CoupleLedger`（`:57-72`）。
- M 构造参数里唯一的接口是 `IMarriageRelationWriter`；M 的具体组件不出现在彼此的构造器里，只有 T 豁免（`:75-86`）。
- 不许 `Func<>`/`Lazy<>`/`IServiceProvider`；T、R 的传递依赖不得到达 M、`IApplyEffectsDomainService` 和三张门面（`:28-29,202-237`）。
- M 的构造器在 `Dfo.Server.Application/Marriage/MarriageDomainService.cs:15-18`；DI 在 `Dfo.Server.Application/ApplicationModule.cs:136-164`。

### 5.2 [建议] 新增一个与 T 平级的单元 H，不塞进 M

判据取自 AGENTS："这块状态参不参与别的聚合的不变量"。婚房、仓库、树**不改变阶段**，阶段不变量 I1–I5 也不依赖它们，它们只要求"关系是 Married"。所以不该把它们做成 M 的第 8～11 个组件：那会让 M 的三张门面再多十来个方法，而且这些组件都得靠 M 编排。

```
H  CoupleHousingDomainService      实现 IMarriageHousingQueryDomainService + IMarriageHousingDomainService
   构造器：IMarriageRelations（只读，判 Married 与成员身份）、TimeProvider、下列组件
   组件  CoupleRoomLayout   独占 marriage_room_furniture、marriage_room_state、marriage_couple_numbers
         CoupleStorage      方案 A 无专属表（经通用容器 + IItemLifecycleDomainService）；方案 B 独占托管表
         CoupleTree         独占 marriage_trees、marriage_tree_waterings、marriage_tree_harvests
         GrowthCapsules     独占 marriage_growth_capsules（语义待定）
         RoomVisits         内存：访客许可（谁可以进哪对夫妻的房，短期有效）
```

- H 的依赖方向：H → R（只读）；H **不得**到达 M 或 `IMarriageRelationWriter`，为此补一条与 T 同款的 reach 判据。H 可以接 `IApplyEffectsDomainService`（消耗种子、家具券），那条限制只针对 T 和 R。
- 以后做离婚（阶段 6+，`Dfo.Server.Data/Marriage/MarriageRelationModels.cs:5`）要清算共享资产时：M 像接 T 那样接 H 的具体类型，在离婚迁移的同一事务里调 H 的清算方法；H 仍然不反向依赖 M。到时 `UseCaseShapeViolations` 的豁免要从"只有 quests"扩成"quests 和 housing"（`MarriageArchitectureTests.cs:81`）。
- 进入婚房的门：`TownProjectionSlice` 已经可空注入 `IMarriageQueryDomainService`（`:233`）。建议再注入 `IMarriageHousingQueryDomainService?`，在 `:317` 之前调 `CanEnterRoomAsync`；不要把婚房判断塞进 M 的 `CanCrossAsync`，否则 M 要去读 H 的状态。

### 5.3 [建议] 接口面草案（`Dfo.Server.Abstractions/Marriage/MarriageHousingContracts.cs`）

```csharp
public interface IMarriageHousingQueryDomainService
{
    ValueTask<CoupleRoomSnapshot?> RoomAsync(SelectedSessionIdentity viewer, CancellationToken ct);       // 选角/进房：本人或所访房主
    ValueTask<CoupleStorageSnapshot?> StorageAsync(SelectedSessionIdentity actor, CancellationToken ct);  // 03D9
    ValueTask<CoupleTreeView?> TreeAsync(SelectedSessionIdentity viewer, CancellationToken ct);           // 惰性投影，不写库
    ValueTask<bool> CanEnterRoomAsync(SelectedSessionIdentity actor, int channel, byte town, byte area, CancellationToken ct);
    ValueTask<IReadOnlyList<AccountId>> HousingAccountsAsync(SelectedSessionIdentity actor, CancellationToken ct); // 租约预取
}
public interface IMarriageHousingDomainService
{
    ValueTask<CoupleRoomResult> MoveRoomItemAsync(SelectedSessionIdentity actor, CoupleRoomMove move,
        IReadOnlyList<AccountId> accounts, CancellationToken ct);                                          // 03FE
    ValueTask<CoupleStorageResult> DeleteStorageItemAsync(SelectedSessionIdentity actor, int slot, int count,
        IReadOnlyList<AccountId> accounts, CancellationToken ct);                                          // 03FF
    // 0400..0403：PlantAsync / WaterAsync / HarvestAsync / RemoveTreeAsync，签名同上（actor + 参数 + accounts）
    // 0404..0406：成长胶囊，语义确认后再定
}
```

每个写方法的顺序：会话是否当前 → `relations.ForSessionAsync` 只解析一次 → `stage == Married` → 复核 `accounts.SequenceEqual(...)` → 组件副作用 → 各表 revision CAS。子记录改动之后 CAS 失败一律抛异常、整笔回滚，与 `MustCommitAsync` 同语义（`CoupleLedger.cs:202-205`）。

### 5.4 [建议] 表草案（放进 `CharacterDatabaseSchema.TableDeclarations`，`Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:232` 起）

```sql
CREATE TABLE IF NOT EXISTS marriage_couple_numbers (
  couple_no INTEGER PRIMARY KEY AUTOINCREMENT,
  relation_id TEXT NOT NULL UNIQUE REFERENCES marriage_proposals(id));
CREATE TABLE IF NOT EXISTS marriage_room_state (
  couple_no INTEGER PRIMARY KEY REFERENCES marriage_couple_numbers(couple_no),
  tail_0 INTEGER NOT NULL DEFAULT 0, tail_1 INTEGER NOT NULL DEFAULT 0, tail_2 INTEGER NOT NULL DEFAULT 0,
  tail_flag INTEGER NOT NULL DEFAULT 0, revision INTEGER NOT NULL CHECK(revision>=1));
CREATE TABLE IF NOT EXISTS marriage_room_furniture (
  couple_no INTEGER NOT NULL REFERENCES marriage_couple_numbers(couple_no),
  slot INTEGER NOT NULL CHECK(slot BETWEEN 0 AND 65535),
  value INTEGER NOT NULL CHECK(value BETWEEN 0 AND 4294967295),
  item_uid INTEGER, placed_at INTEGER NOT NULL, PRIMARY KEY(couple_no, slot));
CREATE TABLE IF NOT EXISTS marriage_trees (
  couple_no INTEGER PRIMARY KEY REFERENCES marriage_couple_numbers(couple_no),
  planting_id TEXT NOT NULL UNIQUE, seed_item_id INTEGER NOT NULL, planted_by INTEGER NOT NULL,
  planted_at INTEGER NOT NULL, revision INTEGER NOT NULL CHECK(revision>=1));
CREATE TABLE IF NOT EXISTS marriage_tree_waterings (
  planting_id TEXT NOT NULL, character_id INTEGER NOT NULL, period_start INTEGER NOT NULL, watered_at INTEGER NOT NULL,
  PRIMARY KEY(planting_id, character_id, period_start));
CREATE TABLE IF NOT EXISTS marriage_tree_harvests (
  planting_id TEXT NOT NULL, character_id INTEGER NOT NULL, harvested_at INTEGER NOT NULL,
  PRIMARY KEY(planting_id, character_id));
```

- 树收获或移除后删除 `marriage_trees` 那一行，历史只留在 waterings/harvests 里，以 `planting_id` 区分每一轮种植，避免第二棵树沿用第一棵的收获记录。
- 情侣仓库若用 §2.3 方案 A，不新增表，但要做一次 `item_slots` 重建迁移（放宽 `owner_scope` 的 CHECK），挂在 `CharacterDatabaseSchema.cs:81` 一带，与 `MarriageRelationMigration.Apply` 同级的启动迁移里。
- 所有字段语义（尤其 `tail_*`、`value`）都是占位命名，以 A21 子代理的布局结论为准再改名。

### 5.5 [建议] DI 与接线位置

| 层 | 位置 | 内容 |
|---|---|---|
| Abstractions | `Dfo.Server.Abstractions/Marriage/MarriageHousingContracts.cs`（新） | 两个 `*DomainService`、各 `*DomainStore`、`IMarriageHousingRuleService`、`IMarriageHousingContentSource` |
| Application | `Dfo.Server.Application/ApplicationModule.cs:162` 之后 | `TryAddSingleton` 注册各组件和 `CoupleHousingDomainService`，两个门面用工厂指向同一实例（照 `:159-162`）；规则服务 `TryAddSingleton<IMarriageHousingRuleService, …>` |
| Persistence | `Dfo.Server.Persistence/PersistenceModule.cs:61-66` 一带 | 各 `SqliteMarriage*DomainStore` |
| Infrastructure | `Dfo.Server.Infrastructure/InfrastructureModule.cs:34` 一带 | `PvfMarriageHousingContentSource`：`[couple room item]` 76 件（`protocol/patterns/data/probes/pvf-item-use-taxonomy-20260818.md:41`）、树和胶囊规则 |
| Protocol | 新切片 `Dfo.Server.Protocol/Game/MarriageHousing/`；接线在 `ProtocolModule.cs:264-321` 婚姻块之后 | 解包器、编码器、handler、命令，全部 Transactional + Account Exclusive（照 `:306`） |

## 6. 测试约定

- [现状] 婚姻用例集中在 `Server/tests/Dfo.Server.Application.Tests/Marriage/`：`MarriageHarness.cs`（夹具和假内容源）、`MarriageArchitectureTests.cs`（结构判据）、`MarriageEntityTests`、`MarriageRelationLedgerTests`、`MarriagePairCorrectionTests`，以及一批 `*CharacterizationTests`。
  其他位置：迁移 `Server/tests/Dfo.Server.Persistence.Tests/Schema/MarriageRelationMigrationTests.cs`、`MarriageWeddingMigrationTests.cs`；codec `Server/tests/Dfo.Server.Protocol.Tests/MarriageChurchCodecTests.cs`、`MarriageProposalCodecTests.cs`；真实 TCP `Server/tests/Dfo.Server.Networking.Tests/Integration/MarriageProposalIntegrationTests.cs`；内容 `Server/tests/Dfo.Server.Infrastructure.Tests/Content/MarriageProposalContentTests.cs`；命令注入面 `Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs`。
- [现状] `Tool/WireSmoke` 只驱动注册→建角→选角→接任务，并落帧、落库（`Tool/WireSmoke/README.md:1-40`），没有婚姻流程。它适合做"选角 03D8 前后对比"（`framediff.py` 比较前后两份帧文件），不适合双账号婚房交互。
- [建议] 新功能应补：
  1. **结构判据**：H 的 store 单一所有者（新 store 进 Marriage 命名空间即自动覆盖）；H 不到达 M 和 writer；附反例用例证明判据会拒绝。
  2. **迁移**：`item_slots` 重建前后行数、内容、`integrity_check` 一致；重复启动幂等；真实库副本演练；`marriage_couple_numbers` 懒分配幂等。
  3. **应用层**：非已婚或已关闭的关系一律拒；账号表与关系不符就拒（照 B1-1）；配偶离线时仓库存取和浇水成功、且不发对端投影；同人同日重复浇水被拒；收获主键冲突被拒、不重复发奖；配偶的奖励走邮件；revision CAS 冲突整笔回滚（照 `MarriageEntityTests` 的两条回滚用例）。
  4. **协议**：03D8 非空和空两种编码向量；未婚仍是 204 零（保住 `SelectCharacterPayloadCodecTests.cs:212`）；新增的 `ReplayProjectionNeeds` 位能通过 `Validate`。
  5. **TownProjection**：非夫妻、非受邀者用 0x0024 进 101 被拒并收到 0x0017；夫妻进房不落库坐标；访客进的是房主的实例。
  6. **真实 TCP**：在 `Networking.Tests/Integration` 复用 `MarriageProposalIntegrationTests` 的双账号夹具，走一遍"进房→摆家具→对方看到 03D8"。
  7. 以上都不替代 A21 双端实机验收。

## 7. 风险清单

| # | 风险 | 依据 | 缓解 |
|---|---|---|---|
| R1 | **现存越权入口**：只要 PVF 声明了 101 的 area，任何角色发 0x0024 都能进婚房 | `MarriageDomainService.Query.cs:87-89`（非教堂一律放行）、`TownProjectionSlice.cs:278-358` | 婚房功能落地前，可以先单独加"101 只许已婚/受邀者进入"的门 |
| R2 | 婚房实例：若 CoupleRoom.twn 只有 1 个 area，所有进入者共用同一个 `TownAreaKey`，互相可见，03D8 的归属也无从谈起 | `ITownAreaDomainService.cs:6-20` | 先核 PVF 的 area 数；倾向给 `TownAreaKey` 加实例维度（§1.4 C） |
| R3 | 婚房坐标被持久化，重登直接落进 101，而实例号和访客许可已经丢了 | `TownProjectionSlice.cs:332-340` | 进 101 不落库坐标（仿临时频道） |
| R4 | `item_slots` 重建迁移出错会伤及全部物品 | `CharacterDatabaseSchema.cs:380` 的 CHECK | 单独 PR、真实库副本演练、`integrity_check`、启动幂等判据 |
| R5 | 家具"仍是物品"和"已被消耗"两种模型混用，导致双计或丢失 | §2.4 | 按 A21 证据只定一种，并写成不变量判据 |
| R6 | **心意点"只增不减"的前提会被打破**（如果家具/婚房消耗心意点）：启动对账会把关系行抬回旧婚礼列的较大值，已扣的点数在重启后复活 | `Dfo.Server.Persistence/Schema/MarriageRelationMigration.cs:42-43`；加点双写 `Dfo.Server.Persistence/Marriage/SqliteMarriageRelationDomainStore.cs:65-78` | 扣点时同样双写旧列；或先停写旧列、结束回退兼容。不能只扣关系行 |
| R7 | 配偶离线或在线时投影缺失：持久路径直接改了在线配偶的背包，但对方客户端不知道 | `EffectOperationContext.cs:123-133`；`GrantAllPersistentAsync` 绕过在线投影 | 配偶的份额走邮件或共享容器；在线投影只在提交后按精确会话发 |
| R8 | 并发：一方用 03FE 摆家具，另一方同时用 03FF 删同一件 | — | 双账号排序租约 + 各表 revision CAS；外层再取 `SelectedSessionTransitionGate`（照 `MarriageProposalSlice.cs:258-274`） |
| R9 | 重复请求：同一帧重发导致重复扣种子或重复收获 | — | 请求实现 `IEffectSettlementRequest` + `MinimumReplayNeeds`；收获用主键做幂等 |
| R10 | 新重放位没进 `Validate` 白名单，重放时抛异常、连接断开 | `IReplayStateProjection.cs:29` | 加位时必改白名单，并补判据 |
| R11 | **未知协议字段导致断线**：03D8 尾部 13B、03D9/03DC/03DD 的字段语义未闭合，长度或计数发错会让 A21 handler 错位 | `MarriagePhase1Research-20261016.md:107-124` | 没经 A21 确认的 opcode 不发；先保留 204 零空态；用 handler 的消费长度做 codec 判据 |
| R12 | 子记录改动后 CAS 失败，对外表现是断连而不是错误码 | `Document/MarriageAggregateDesign-20261017.md:72` | 正常竞争路径在改子记录之前先检查，返回拒绝码，不抛异常 |
| R13 | 离婚后资产归属：仓库、家具、树归谁今天没有规则（离婚未实现，阶段 6..15 只是预留，Protocol 遇到未知阶段会抛异常） | `MarriageRelationModels.cs:5`；`Dfo.Server.Protocol/Game/MarriageProposal/MarriageEngagementSlice.cs:54-60` | 设计 H 时预留清算入口，由 M 在离婚迁移的同一事务里调用；先定规则再开放离婚 |
| R14 | "成长胶囊"与现有账号成长胶囊同名 | `IExperienceCapsuleDomainService.cs:5-8` | 先确认是不是同一个东西，避免出现两套 EXP 账本 |
| R15 | 访客许可只在内存：服务重启或房主下线后，访客仍留在房里 | — | 房主下线、离婚、踢人时，用 `MarriageWeddingProjection.MoveAsync` 同款路径把访客挪出 |
