# 婚房、情侣仓库、情侣树、成长胶囊与戒指升级：参考边界与当前接缝

日期：2026-10-18
性质：只读调查与预设计；未实现、未部署、未改数据库、未运行服务、未改 PVF/客户端；子代理不单独提交，由主代理统一归档
当前仓库：`DNF-JP-86-Server`，分支 `task/next-round-20261013`，调查基线 `61beac4036eaad20a92f7c74483bf097764eb543`

## 1. 结论

现有材料足够确定婚房的架构接缝，但不足以直接实现 A21 婚房协议。

1. 70 级 ELF 能确认通用 `Arad_Housing`、`Arad_HousingManager`、`Arad_HousingTree` 和一组 `HOUSING_*` 消息存在；有限符号扫描没有发现 wedding/couple/marriage 业务符号。它只能作为 `70Reference` 的通用住房机制参考，不能证明 JP86 A21 的婚房报文、字段或玩家规则。
2. `E:\Project\86JP` 固定版本对婚房、情侣仓库、情侣树、成长胶囊、戒指升级只有协议枚举，没有可复用的业务、持久化或事务流程。
3. `ServerS4A21` 曾加入 `03D0` 15 字节常量、`03F3` 31 字节零体、`03D8` 113 字节固定家具列表并在换宠、进本、回城后重放。这是兼容性回放，不是关系归属、家具库存、消费或放置流程。
4. 当前仓库已经有可复用的婚姻关系真源、双账号排他锁、事务、请求重放和教堂准入/清理模式；缺少关系所有的家具库存、保管箱单阅览者租约、婚房私有场景实例、家具/树/戒指领域状态，以及经实机确认的 A21 报文布局。
5. 入口最大的结构缺口是场景隔离。当前在场广播键只有 `(ChannelId, TownId, AreaId)`。如果两对夫妻都进入 `town 101 / area 0`，他们会处于同一个广播域；把 `SharesTownPresence` 设为 `false` 又会让配偶彼此不可见。
6. 余额最大的迁移风险是当前关系心意点只增不减，启动迁移会以 `max(新关系表, 旧婚礼表)` 提升余额。加入消费后若继续执行该合并，旧表的较高余额会在重启时复活已经花掉的点数。消费必须采用 CAS/revision，并配套一次性迁移截止点。

因此，第一轮玩家闭环应只做“已婚夫妻从入口进入同一个私有婚房、不同夫妻互不可见、正常退出和重登可恢复”。在这个闭环通过后，再接真实 `03D8` 房间快照、点数归属确定后的购买和保管箱租约。

## 2. 证据等级与参考身份

| 标记 | 本文含义 |
|---|---|
| `CurrentImplementation` | 直接核对当前仓库 `61beac40` 源码 |
| `70Reference` | 70 级 Linux 服务端 ELF 的符号/字符串参考 |
| `LegacyServerReference` | 旧 `86JP` 或 `ServerS4A21` 固定提交的源码参考 |
| `ClientStatic` | A21 客户端静态探针；没有实帧与玩家行为验收 |
| `HistoricalCNReference` | 国服历史玩法资料；不能外推为日服 A21 事实 |
| `Unknown` | 仍需 Claude 比对、逆向、抓包或实机确认 |

### 2.1 固定参考

| 参考 | 身份 | 本次能证明什么 | 局限 |
|---|---|---|---|
| 70 级 `df_game_r` | `E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r`；SHA-256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7` | ELF 符号含 `Arad_Housing.cpp`、`Arad_HousingConvertor.cpp`、`Arad_HousingManager.cpp`、`Arad_HousingTree.cpp`；字符串含进入/离开/邀请/房间用户/树/浇水/收获等 `HOUSING_*` 名称 | 不是 JP86 A21；窄查未见婚姻/情侣业务符号；未做完整控制流逆向 |
| 旧 `86JP` | `25e6f0af600b32e550a9126fff5758b602f42ce4` | `PacketTypes.cs` 枚举了不同年代的 HOUSING、WEDDING、COUPLE_ROOM、COUPLE_INVEN、COUPLE_TREE、成长胶囊等名称 | 只有枚举；同文件混合多个协议年代，编号不可直接搬到 A21 |
| `ServerS4A21` | 基线 `c20dba5fa52c2faf4b5087a3b477676be8434e8f`；相关提交 `c10f80c3be66b7c629e907b2c6291ef46bc25681`、`fdc290754bd194a7bf24e4ecf3f4f75168bd0bdf` | `c10f80c` 加入 03D0/03F3/03D8 固定回放；`fdc2907` 把三包补发到换宠、进本、回城路径 | `03D8` 是固定常量，无 DB、关系归属、库存、扣点和放置流程；其中通用账号成长胶囊也不是情侣成长胶囊 |
| 当前 A21 客户端静态探针 | `DNF.unpacked-b1-debug.exe` SHA-256 `CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14` | 定向反汇编已读出多组婚房、家具、树、戒指消息的宽度；详见 A21 专项 | 只有 `ClientStatic`；多个字段的业务含义、入口和实际交互仍未闭合 |
| 2014 国服历史介绍 | [17173 汇总页](https://dnf.17173.com/content/2014-02-25/20140225140628839_all.shtml) | 历史资料称心意点归各自角色、家具/房间由情侣共享、保管箱一次只允许一名角色阅览，并描述 30 件摆设、40 至 120 格保管箱、种树等玩法 | 国服历史版本，不是 A21 日服线证据；只能帮助形成待验证问题 |

符号和身份的小型复核记录见 `Document/outputs/marriage-housing-reference-20261018/`。本次没有继续穷尽 70 ELF，也没有把旧枚举或历史玩法当成 A21 协议事实。

## 3. 参考实现能复用什么，不能复用什么

### 3.1 70 ELF：只复用领域词汇和流程形状

可复用的抽象形状：房间进入/离开、邀请、房间用户列表、树信息、种植、浇水、收获、历史记录。它提示住房系统天然包含“房间身份、成员集合、时间状态、一次性结算”四类能力。

不能复用的内容：opcode 数字、报文布局、婚姻资格、家具槽位、点数归属、A21 客户端刷新时机。窄符号表只显示 housing 编译单元及静态初始化函数；`wedding|couple|marriage` 没有命中。

### 3.2 旧 `86JP`：只作消息候选目录

固定提交的搜索命中集中在 `Server/DfoServer/Network/Core/PacketTypes.cs`。其中同时出现旧 HOUSING 消息和后期 COUPLE/WEDDING 消息，说明枚举文件是跨年代目录，不是可执行协议记录。

这些名称可供后续 Claude 比对：`COUPLE_ROOM`、`COUPLE_INVEN`、`COUPLE_ROOM_UPDATE`、`COUPLE_ROOM_REFRESH`、`COUPLE_TREE`、`COUPLE_SIMPLE_INFO`、`COUPLE_TREE_HARVEST_REWARD`、`COUPLE_GROWTH_CAPSULE` 及相应命令。没有实现就不能据此推定方向、字段或返回码。

### 3.3 `ServerS4A21`：保留“重放时机”线索，不保留业务语义

`c10f80c` 的三包是：

- `03D0`：15 字节固定常量；
- `03F3`：31 字节全零；
- `03D8`：113 字节，形状为 `u32 count=16 + 16 × (u16,u32) + 13 字节尾部` 的固定列表。

`fdc2907` 在换宠、进本、回城后重放这三包，注释表明客户端在 `USERINFO` 重建后会丢失婚礼 UI/属性状态。这能形成一个有价值的 `LegacyServerReference` 风险：真正的家具投影除选角外，可能还需在会重建角色状态的路径补发。

它不能证明固定列表中的 ID 是真实家具 DB，也不能证明 113 字节是当前客户端最终布局。当前仓库反而在 [StateSynchronizationMessages.cs:410](../Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs#L410) 把 `03D8` 明确命名为 empty compatibility，并在 [StateSynchronizationMessages.cs:414](../Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs#L414) 固定发送 204 个零字节；测试也要求两种输入都得到 204 零字节（[SelectCharacterPayloadCodecTests.cs:212](../Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs#L212)）。两者不是可以靠猜测合并的同一份布局。

## 4. 当前可复用接缝

### 4.1 关系真源与 DI 方向

当前 `CoupleLedger` 已经把只读 `IMarriageRelations` 与写入 `IMarriageRelationWriter` 分开，写接口包含建立关系、阶段 CAS、心意点变更；实现被声明为关系阶段和心意点的唯一写者（[CoupleLedger.cs:18](../Server/src/Dfo.Server.Application/Marriage/CoupleLedger.cs#L18)、[CoupleLedger.cs:52](../Server/src/Dfo.Server.Application/Marriage/CoupleLedger.cs#L52)、[CoupleLedger.cs:68](../Server/src/Dfo.Server.Application/Marriage/CoupleLedger.cs#L68)）。DI 将两个接口解析到同一个 `CoupleLedger`（[ApplicationModule.cs:140](../Server/src/Dfo.Server.Application/ApplicationModule.cs#L140)）。婚前任务是独立组件，主婚姻用例单向依赖它（[MarriageDomainService.cs:15](../Server/src/Dfo.Server.Application/Marriage/MarriageDomainService.cs#L15)、[ApplicationModule.cs:156](../Server/src/Dfo.Server.Application/ApplicationModule.cs#L156)）。

这是婚房扩展应保留的边界：

- `CoupleLedger/IMarriageRelationWriter` 继续是关系和婚姻点数的唯一写入口；
- 婚房内部组件接收上层预解析的不可变关系事实，不反向注入 `MarriageDomainService` 或公共婚姻门面；
- Protocol 只依赖窄的 `IMarriageHousingDomainService`，不直接拿 store、关系 writer、家具容器或租约表；
- 婚房内部依赖自己的 store、规则、家具库存和运行时租约；它不能写 `marriage_relations`。

建议的依赖形状：

```text
Protocol
  -> IMarriageHousingDomainService
       -> IMarriageRelationWriter / IMarriageRelations（解析关系、受控扣点）
       -> HousingUseCases
            <- MarriageHousingFacts（relation、双方、stage、revision、actor）
            -> HousingStore / FurnitureRules / FurnitureInventory / StorageLeaseRegistry
```

`HousingUseCases` 不得回调婚姻主服务。由窄门面先解析关系并传 facts，可以保留单向依赖，也能让每个命令只读取一次关系快照。

### 4.2 事务、双账号锁与重放

当前心意券命令已具备可复用形状：请求声明库存与婚姻点数重放需求（[MarriageLovePointSlice.cs:14](../Server/src/Dfo.Server.Protocol/Game/MarriageProposal/MarriageLovePointSlice.cs#L14)），命令实现 `IMultiAccountCommand` 和 `IRequestSettlementCommand`（[MarriageLovePointSlice.cs:47](../Server/src/Dfo.Server.Protocol/Game/MarriageProposal/MarriageLovePointSlice.cs#L47)），注册为双账号排他、事务命令（[ProtocolModule.cs:306](../Server/src/Dfo.Server.Protocol/ProtocolModule.cs#L306)）。账号锁按账号 ID 排序后全部取得，事务在锁内打开；异常时整体回滚（[AccountExecutionBehavior.cs:23](../Server/src/Dfo.Server.Application/Commands/Pipeline/AccountExecutionBehavior.cs#L23)、[UnitOfWorkBehavior.cs:43](../Server/src/Dfo.Server.Application/Commands/Pipeline/UnitOfWorkBehavior.cs#L43)）。已有回归覆盖丢 ACK 后重放当前库存和点数而不二次扣券（[EffectOperationReplayTests.cs:34](../Server/tests/Dfo.Server.Application.Tests/Sessions/EffectOperationReplayTests.cs#L34)）。

家具购买、放置、删除、树收获、戒指升级都应沿用这一命令边界：双方账号排他锁、一个 DB 事务、请求结算/重放。Protocol handler 不能先扣点再另起事务授家具。

### 4.3 教堂可以复用的是生命周期模式

教堂已有“分配房间、准入、占用者查询、释放、离线清理”模式。`marriage_church_rooms` 用 `UNIQUE(channel_id,town_id,area_id)` 保证同一个物理 area 只给一场婚礼（[CharacterDatabaseSchema.cs:289](../Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs#L289)），`ChurchRooms` 逐个候选 area 尝试占用（[ChurchRooms.cs:58](../Server/src/Dfo.Server.Application/Marriage/ChurchRooms.cs#L58)）。

可复用：relation/ceremony 归属校验、准入记录、准确释放、会话代际校验、过期清理、异常回滚。

不可直接复用：教堂没有“同一 town/area 的多个实例”。它最终仍把房间转成普通三元 `TownAreaKey`（[ChurchRooms.cs:158](../Server/src/Dfo.Server.Application/Marriage/ChurchRooms.cs#L158)），隔离来自不同 `AreaId`，不是隐藏 room key。

## 5. 当前缺口

### 5.1 同一 `town 101 / area 0` 的夫妻隔离

`TownAreaKey` 只有 channel、town、area 三个字段（[ITownAreaDomainService.cs:6](../Server/src/Dfo.Server.Abstractions/World/ITownAreaDomainService.cs#L6)）。同键成员的可见性只有两态：自己共享时看同键全部共享成员，自己不共享时只看自己（[ITownAreaDomainService.cs:80](../Server/src/Dfo.Server.Abstractions/World/ITownAreaDomainService.cs#L80)）。广播、移动、名单重绘和投影门也都按该三元键工作。

所以当前能力不足以表达：

```text
town 101 / area 0 / relation A -> 夫妻 A1、A2 互相可见
town 101 / area 0 / relation B -> 夫妻 B1、B2 互相可见
A 与 B 彼此不可见
```

推荐引入服务端私有场景键，例如 `TownSceneKey(ChannelId, TownId, AreaId, RoomScope)`，其中普通城镇为 `Public`，婚房为稳定的 `MarriageRelationId` 或从它派生的 `HousingRoomId`。发给客户端的仍是 `town 101 / area 0`。至少以下消费者必须同时改用 scene key，而不能只在入口校验一次“已婚”：

- membership 反向索引与快照；
- 到达、离开、移动、强制位置和 AREA_USERS 名单；
- `TownAreaProjectionGate`；
- `Find`/`Query` 和当前视图装配；
- party directory 是否对私有场景发布；
- 断线、切角、跨区、回城的 exact leave/transfer。

如果不希望扩大普通城镇模型，也可以建立专用 `HousingPresenceDomainService` 和投影层，但它必须复用相同的 selected-session generation、网络投影和清理原则。仅给现有 `TownAreaMember` 增加一个过滤条件而不改变键，会让名单重绘、移动广播或其他查询绕过过滤。

教堂只能复用 room allocation/admission/cleanup 的模式，不能直接复用现有三元 room key。婚房是长期稳定的 relation room，不应占用有限的教堂 area 12..62。

### 5.2 家具库存没有 relation owner

当前持久库存 owner 只有 `Character` 和 `Account`（[InventoryModels.cs:134](../Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs#L134)）；`ContainerId` 只会构造 character/account 身份（[IItemContainer.cs:23](../Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs#L23)），SQLite store 也只接受这些 owner 与现有容器类型（[SqliteItemDomainStore.cs:386](../Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs#L386)）。因此当前没有“夫妻关系共同拥有的家具保管箱”。

预设计建议优先使用专用婚房表，而不是立刻扩展全局 `InventoryOwnerType`：

- owner：`relation_id`；
- 家具条目：稳定实例 ID、模板 ID、数量、库存槽、状态 revision；
- 摆放状态：实例 ID、位置、朝向、层级/槽位及房间 revision；
- 同一个实例只能处于保管箱或摆放态之一；
- 删除、移动、摆放在服务端数据层采用 revision/CAS；原生请求是否带可作为 expected 值的字段由 wire 证据决定；
- 普通物品奖励仍走现有 item delivery，不把情侣家具强行伪装成某一角色背包。

只有后续实帧证明情侣仓库完全复用普通 ITEM_LIST/容器协议时，才值得把通用容器 owner 扩展为 relation 范围。关系 ID 是 GUID，现有 `ContainerOwner.Value` 是 `int`，这不是增加一个枚举值就能安全完成的改动。

### 5.3 保管箱没有单阅览者租约

仓库搜索未发现 relation/couple 范围、跨两个在线会话的“只有一人打开”租约。账号排他锁只串行化单个请求，不会在 UI 打开期间长期阻止另一方打开。

建议新增进程级单例 `ICoupleStorageLeaseRegistry`：

- key：`relation_id`；
- holder：完整 `SelectedSessionIdentity` 与服务端内部生成的不可猜 lease identity；该身份保存在服务端，不要求原生客户端携带未确认的新 token 字段；
- `Open` 使用 CAS，只允许一个 exact session generation；重复打开由同一 holder 幂等成功；
- 每个修改命令根据当前会话解析并校验服务端 lease identity、当前已婚关系和服务端 storage revision；
- `Close`、断线、切角、离开婚房、离婚用 compare-and-release，旧连接不能释放新连接的 lease；
- 单进程重启后租约自然清空，客户端需重新打开；未来多 Host 时再升级为带 TTL/owner generation 的分布式租约。

租约只是 UI 互斥，不是数据授权。即使持有 lease，每次写操作仍需重新验证关系，并在服务端数据层使用 revision/CAS。原生请求是否提供可用的 expected revision 仍是 `Unknown`；在 wire 未确认前，不能要求客户端新增 token 或 revision 字段。

### 5.4 心意点当前是关系共享余额，历史资料称个人余额

`MarriageRelation` 只有一个 `LovePoints`（[MarriageRelationModels.cs:7](../Server/src/Dfo.Server.Data/Marriage/MarriageRelationModels.cs#L7)）。当前心意券先按关系共享余额检查上限，再经唯一 writer 增加关系余额（[MarriageDomainService.Couple.cs:161](../Server/src/Dfo.Server.Application/Marriage/MarriageDomainService.Couple.cs#L161)）。历史国服资料则称点数归各自角色、家具共享。

这是一项必须先由 A21 报文/客户端字段/实机确认的产品语义，不能拿国服历史资料覆盖现有实现：

- 若 A21 是共享余额，可在关系 writer 上增加显式 `TrySpendLovePoints(expectedRevision, amount)`；
- 若 A21 是个人余额，需要 relation participant 维度的新余额与 revision，不能把现有单值假装成两份；
- 家具仍应由 relation 共享，不随购买者角色改变 owner；
- 谁购买、谁扣点、双方看到何种总值必须被报文字段和客户端 UI 同时验证。

### 5.5 余额扣减与迁移会发生“复活”

当前 store 明确拒绝非正数，并只做 `love_points=love_points+amount`（[SqliteMarriageRelationDomainStore.cs:65](../Server/src/Dfo.Server.Persistence/Marriage/SqliteMarriageRelationDomainStore.cs#L65)）。迁移注释假设点数只增不减，并在每次应用时用旧婚礼表较大值抬高关系表（[MarriageRelationMigration.cs:12](../Server/src/Dfo.Server.Persistence/Schema/MarriageRelationMigration.cs#L12)、[MarriageRelationMigration.cs:40](../Server/src/Dfo.Server.Persistence/Schema/MarriageRelationMigration.cs#L40)）。

加入消费前必须同时完成：

1. 新增明确的扣点动作，不允许用负数调用 `AddLovePointsAsync`；
2. 扣点 SQL 带余额下限、关系阶段和服务端数据 revision/CAS；原生请求是否能提供 expected 值仍是 `Unknown`；
3. 扣点与家具授予/库存增加在同一个事务；
4. 复用现有请求结算管线的稳定 operation identity，重发只回放当前余额/家具快照；这是服务端请求身份，不能要求原生客户端携带尚未确认的新 operation-id 字段；
5. 用一次性 schema migration marker 截止旧表合并，消费上线后不再每次启动执行 `max(new, legacy)`；
6. 若必须兼容旧二进制回退，扣减也要同步写旧表，并设计可识别来源的版本；否则明确“不支持旧版继续写后再升级”，回退必须恢复配套 DB 备份；
7. 回归覆盖：消费成功后重启、回退再升级、事务失败、丢 ACK、双方并发购买，均不能复活余额或重复授家具。

如果 A21 最终是个人余额，当前单一关系余额无法无损拆分。迁移政策必须显式选择初始分配规则，不能从历史说明猜测。

### 5.6 戒指、树、情侣成长胶囊只有内容线索，没有领域实现

当前 PVF 规则读取能识别 `[upgrade wedding ring]`，但通用 item-use 路径故意把它标为不可读并拒绝消费，避免误吞道具（[ItemUseEffectRuleService.cs:11](../Server/src/Dfo.Server.Application/Inventory/ItemUseEffectRuleService.cs#L11)、[MarriageConsumableProtectionTests.cs:16](../Server/tests/Dfo.Server.Application.Tests/Inventory/MarriageConsumableProtectionTests.cs#L16)）。这是安全占位，不是戒指升级实现。

当前账号级 `GrowthCapsuleExperience` 和相关同步属于普通成长胶囊能力；旧枚举中的 `MAKE/GIVE/TAKE_COUPLE_GROWTHCAPSULE` 是另一组候选语义，不能复用同一个余额或 handler 只因名称相近。

当前仓库没有婚房家具目录、情侣树时间状态、浇水/收获防重、戒指等级/消耗规则、情侣成长胶囊制作/赠送/领取的领域 store。PVF 中出现 action 或物品 ID，只证明静态内容存在，不证明服务端流程和协议布局。

## 6. 入口、重登和回放风险

### 6.1 进入婚房

入口命令必须在事务/会话边界内完成：

1. 解析 exact selected session；
2. 读取当前有效 `Married` 关系并校验 actor 是 participant；
3. 解析或建立稳定 `HousingRoomId`；
4. 将 membership 转入服务端私有 scene scope，客户端仍看到 `town 101 / area 0`；
5. 只向同 relation scene 的在线配偶投影双方；
6. 下发真实房间、家具、树和权限快照；
7. 失败时留在原场景，不能出现位置已持久化而准入/投影未完成的半状态。

如果两人不在同一 channel，A21 是把后进入者切到配偶 channel、建立 relation 全局房间，还是各自创建同关系的 channel 副本，仍是 `Unknown`。不要让 `relation_id` 省略 channel 后跨监听器直接互播，也不要默认夫妻必须事先同城。

### 6.2 离开、断线和离婚

离开需要撤销 scene membership、保管箱 lease、未完成的交互并落到合法普通城镇位置。exact session generation 必须贯穿 compare-and-remove，避免旧连接清掉新连接状态。

离婚/关系关闭时，家具如何处置、个人点数是否保留、戒指如何降级、树和胶囊如何结算均未确认。数据模型应先允许关系资产冻结，不要在未知时自动删除。

### 6.3 选角和重登

若持久位置仍是 `town 101 / area 0`，选角恢复前必须先重建 relation room scope；否则会进入公共三元广播域。两种安全策略：

- 持久化婚房位置，但选角入口先验证婚姻并重建 scene key，失败时回安全城镇；
- 婚房位置仅运行时存在，持久位置保留入口前的赛丽亚房间，重登总从普通房间进入。

应由 A21 实机行为决定。无论哪种，当前 204 零字节 `03D8` 都不能作为真实房间恢复；它可能清空 UI 或覆盖家具状态。选角、换宠、进本、回城后是否重放真实房间快照，需要逐条实帧验证。

## 7. 推荐玩家闭环顺序

### M1：私有婚房入口

完成已婚门禁、服务端 scene scope、夫妻互见、不同夫妻隔离、正常离开、断线/切角清理、重登安全恢复。暂不加入家具写操作。验收至少包含两对夫妻同时处于 `town 101 / area 0`，每个客户端只看到自己和配偶。入口能否成立可能依赖 `03D8` 或相邻消息的必要只读子集，因此 M1/M2 是玩家验收关注点的推进顺序，不表示 wire 实现可以严格切开；若客户端进房必须先收到该子集，应把它纳入 M1 的最小入口握手。

### M2：真实只读房间快照

先逆向/抓包闭合 A21 的 `03D8` 及相邻房间消息，再从空房、单家具、多家具、重登、换宠、进本、回城做差分。只读投影通过后才能替换 204 零兼容体。

### M3：余额、购买与共享保管箱

先决定 A21 是个人还是共享心意点，完成一次性余额迁移；再实现扣点与授家具同事务、relation owner 的家具库存、保管箱单阅览者 lease、购买/打开/关闭/重发/重登闭环。

### M4：摆放、撤回与房间属性

实现家具实例在保管箱与摆放态之间的 CAS 转换，双方实时刷新；属性从已摆放家具的规范化事实派生，不能信客户端上报总值。验证超限、重复请求、双方竞争、事务失败和重登恢复。

### M5：情侣树

在房间/库存/事务基础稳定后加入种植、浇水、成长、收获、移除；所有时间判断使用服务端时钟，收获要有 once-only operation 或 revision，奖励失败必须整体回滚或进入可恢复 outbox。

### M6：戒指升级

闭合 `[upgrade wedding ring]` 的物品、点数、等级和报文；升级券消耗、余额扣减、等级 CAS、属性刷新同事务。保留当前通用 item-use 的拒绝保护，直到专用命令接管。

### M7：情侣成长胶囊

最后处理制作、赠送、领取及双方归属。先证明它与当前账号成长胶囊的 wire/state 是否独立，避免共享错误的进度表或通知。

## 8. 后续实现的最低验收

| 范围 | 最低验收 |
|---|---|
| 房间实例 | 两对夫妻同 town/area 不串可见性、移动、名单或离开通知；配偶互见 |
| 关系门禁 | 未婚、旧关系、错误账号/角色、Closed 关系均不能进入或操作 |
| 租约 | 一方打开后另一方被确定性拒绝；旧连接/旧的服务端 lease identity 不能写或释放新 lease；不要求客户端新增未确认 token 字段 |
| 购买事务 | 扣点和授家具共同提交/回滚；余额不足、满仓、CAS 冲突不产生半状态 |
| 重放 | 重复帧、丢 ACK、重连不二次扣点、授家具、摆放、浇水、收获或升级 |
| 迁移 | 消费后重启不复活余额；明确并测试支持或禁止的旧版回退路径 |
| 重登 | 房间、家具、树、戒指、余额来自 DB 真源；无公共 `101/0` 泄漏 |
| 刷新时机 | 选角、换宠、进本、回城仅在实帧证明需要时重放正确快照 |
| 证据标注 | 70Reference、LegacyServerReference、ClientStatic、WireCaptured、ClientVerified 分开 |

## 9. 待 Claude 比对点

1. A21 `03D8` 的真实 body：当前 204 零、S4 113 固定列表分别来自什么证据，是否存在空房/非空房两种布局。
2. A21 婚房进入/离开对应的命令、通知、town/area、加载完成顺序；`town 101 / area 0` 是否已由客户端静态或实帧确认。
3. 客户端是否携带隐藏 room/owner/token 字段；若没有，服务端 scene scope 应如何从 relation/session 推导。
4. 夫妻跨 channel 时的目标行为，以及 private room 是否应从 party directory 隐藏。
5. 心意点在 A21 是 participant 个人余额还是 relation 共享余额；`03D0`、角色面板、购买应答各显示哪一份。
6. 家具购买、保管箱打开/关闭、移动/摆放/删除的 opcode、返回码、槽位、数量和 revision 字段。
7. 历史“一次只允许一人阅览”在 A21 是否仍成立；客户端断线、强退和超时后的解锁表现。
8. 家具目录、价格、最大摆放数、保管箱容量和房间属性规则来自 PVF 哪些节点；不能用 2014 国服数值替代。
9. 树的服务端时间、浇水人、成长阶段、收获奖励、防重及离婚处置。
10. 戒指升级券的 item action、等级表、个人/共享扣点及属性投影。
11. `COUPLE_GROWTH_CAPSULE` 与现有账号成长胶囊是否完全独立，制作/赠送/领取的归属键是什么。
12. 选角、换宠、进本、回城何时真的需要重放婚房状态；S4 的补发只能当候选路径。

## 10. 预设计边界

本报告没有修改协议目录、服务端代码、数据库、PVF 或客户端，没有启动服务或做实机验收。结论足够用于下一轮精确逆向和接口设计，但不构成 A21 wire proof、ClientVerified 或可部署方案。
